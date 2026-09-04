using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    // One patrol car cycling Resting -> Undocking -> Patrolling -> Returning ->
    // Docking forever. The driving halves ride DemoVehicle's own lane-graph logic
    // (the same car-following and signal discipline as the civilian traffic); the
    // parked halves are hand-animated over PatrolDocking's Bezier.
    //
    // A patrol is a run of WAYPOINTS drawn uniformly over the whole lane graph,
    // each reached by BFS-routed turns - not a random wander, which statistically
    // loiters around the station and never sees the far districts. The budget
    // counts waypoints; when it runs dry the same routing brings the car back to
    // the kerb in front of the station and it swings into its stall.
    public class PolicePatrolCar : DemoVehicle, IPatrolMarker, IPoliceUnit
    {
        // the law rests on its docking and holds a scene: never cleared away
        protected override bool VanishesWhenStuck => false;

        public enum Mode { Resting, Undocking, Patrolling, Returning, Docking, Responding, OnScene, Parking }

        /// <summary>Redraws tolerated when a drawn waypoint has no route from
        /// here - the grid is strongly connected, so one draw normally lands.</summary>
        const int WaypointRetries = 4;

        public Mode State { get; private set; }

        /// <summary>1-based, set by the builder - "Patrol Car 2" on the popup.</summary>
        public int UnitNumber;

        Vector3 _stall;
        Quaternion _stallRot;
        RoadEdge _home;      // the lane in front of the station
        float _kerbS;        // where along it the forecourt driveway meets it
        Vector3 _kerb;
        List<RoadEdge> _allEdges;
        Dictionary<RoadEdge, RoadEdge> _routeHome; // next lane toward _home, per lane
        Vector2 _restRange;
        Vector2Int _waypointRange;
        float _restTimer;

        RoadEdge _waypoint;
        Dictionary<RoadEdge, RoadEdge> _routeToWaypoint;
        int _waypointsLeft;

        PatrolDocking.Curve _curve;
        float _t;
        Quaternion _fromRot, _toRot;

        // the call: its physical position, the requested clearance and a call that
        // came while the car was in its stall or swinging in or out of it
        Vector3 _scenePos;
        float _sceneStandOff;
        float _responseRetryAt;
        bool _sceneWanted;

        /// <summary>Set for the leg the car drives FORWARDS - out of the bay - where
        /// the heading is the curve's own tangent instead of a slerp between the
        /// endpoints. Clear for the way in, which is a reversing manoeuvre: the car
        /// ends nose-out, so there its heading is not its direction of travel.</summary>
        bool _steerByTangent;

        public void InitParked(Vector3 stall, Quaternion stallRot, RoadEdge home, float kerbS,
            List<RoadEdge> allEdges, Dictionary<RoadEdge, RoadEdge> routeHome,
            Vector2 restRange, Vector2Int waypointRange, float firstRest)
        {
            _stall = stall;
            _stallRot = stallRot;
            _home = home;
            _kerbS = kerbS;
            _kerb = home.Start + home.Dir * kerbS;
            _allEdges = allEdges;
            _routeHome = routeHome;
            _restRange = restRange;
            _waypointRange = new Vector2Int(
                Mathf.Max(1, waypointRange.x), Mathf.Max(1, waypointRange.y));

            State = Mode.Resting;
            _restTimer = firstRest;
            HasBay = true;
            Profile = DriverProfile.Patrol;
            Tag = "police";
            Tf.SetPositionAndRotation(stall, stallRot);
            Slid(stall);
            if (!Fleet.Contains(this)) Fleet.Add(this);
        }

        /// <summary>
        /// A CAR WITH NO BAY, DEALT STRAIGHT ONTO THE ROAD (2026-09-03). A quarter is
        /// authorised more cars than its station has bays, and the rest of that fleet is
        /// not a car that does not exist: it is a car already out on its round when the
        /// city opens.
        ///
        /// It never rests at a kerb - a resting car at a kerb is a registered obstacle
        /// and gridlocked the ambient traffic in about a quarter of seeds (the note on
        /// RoadDemoBuilder.SpawnPatrolCars) - so when its round ends it asks its station
        /// for a bay, and goes back round the city again when there is none.
        /// </summary>
        public void InitRolling(RoadEdge start, float startS, RoadEdge home, float kerbS,
            List<RoadEdge> allEdges, Dictionary<RoadEdge, RoadEdge> routeHome,
            Vector2 restRange, Vector2Int waypointRange)
        {
            _home = home;
            _kerbS = kerbS;
            _kerb = home.Start + home.Dir * kerbS;
            _allEdges = allEdges;
            _routeHome = routeHome;
            _restRange = restRange;
            _waypointRange = new Vector2Int(
                Mathf.Max(1, waypointRange.x), Mathf.Max(1, waypointRange.y));

            HasBay = false;
            Profile = DriverProfile.Patrol;
            Tag = "police";
            State = Mode.Patrolling;
            _waypointsLeft = Random.Range(_waypointRange.x, _waypointRange.y + 1);
            _waypoint = null;
            _routeToWaypoint = null;
            var at = start.Start + start.Dir * startS;
            Tf.SetPositionAndRotation(at, Quaternion.LookRotation(start.Dir));
            Spawn(start, startS);
        }

        /// <summary>Whether this car has a bay of its own to go home to.</summary>
        public bool HasBay { get; private set; }

        /// <summary>SOMEBODY IS IN THE BACK. Set by custody for the ride to the station:
        /// the car stops at its kerb whether or not it has a bay and stands there until
        /// the men have been walked in and the flag comes off. Without it a car that
        /// reached the kerb with every bay held went back round the city with the
        /// prisoners still in it, and the booking never came (MiniCoreDemo, 2026-09-04:
        /// ten cars to six bays).</summary>
        public bool HoldAtKerb { get; set; }

        /// <summary>At its station's kerb - standing there, queued right behind it,
        /// swinging in or already in its bay: the place a load of prisoners is walked
        /// in from. A bay is not the condition; the kerb is.</summary>
        public bool AtHomeKerb =>
            State == Mode.Docking || State == Mode.Resting ||
            (State == Mode.Returning && CurrentEdge == _home &&
             Progress >= _kerbS - KerbQueueReach);

        /// <summary>Metres short of the kerb a returning car still counts as there: the
        /// second car of a two-car load stands nose to tail behind the first.</summary>
        const float KerbQueueReach = 14f;

        /// <summary>Asked its station for a bay on this pass of the home lane already.
        /// </summary>
        bool _askedAtHome;

        // ------------------------------------------------------------ the kerb model

        /// <summary>
        /// THE CAR LIVES ON THE STREET, NOT AT THE STATION (the user's call, 2026-09-04).
        /// Its round ends at a kerb somewhere in the city - the one farthest from where
        /// the rest of the fleet is standing, so the cars sit spread over the blocks like
        /// a real patrol - it rests there, answers calls from there, and goes round
        /// again from there. The station is where a replacement is spawned, where a
        /// prisoner is walked in, and where a transfer starts; nothing else.
        ///
        /// The earlier spread (SpreadPatrolHomes, before 2026-08-27) stood the resting
        /// cars ON the running lane and gridlocked a quarter of seeds; this one parks
        /// them the way a response car parks at a scene - a legal kerb slot clear of
        /// the junctions, pulled fully out of the lane (CrewCars.KerbSlotNear), which
        /// did not exist then. False keeps the docking model: bays, the swing, the
        /// kerb lease.
        /// </summary>
        public bool RestsAtKerbs;

        /// <summary>Seconds a car stands at its kerb between rounds in the kerb model.
        /// Long: the fleet is meant to be SEEN parked over the blocks, and to go round
        /// now and then for the look of it - not to circulate for ever.</summary>
        public Vector2 KerbRestSeconds = new Vector2(90f, 240f);

        /// <summary>Where this car rests, or means to - read by the others when they
        /// choose theirs, so the fleet spreads instead of piling onto one street.</summary>
        public Vector3 RestSpot { get; private set; }
        public bool HasRestSpot { get; private set; }

        /// <summary>Every patrol car in the city, for the spread.</summary>
        static readonly List<PolicePatrolCar> Fleet = new List<PolicePatrolCar>();

        /// <summary>Kerb candidates drawn per choice. Eight uniform draws over the lane
        /// graph and the farthest of them from the rest of the fleet is a spread that
        /// covers the blocks without a global search every round.</summary>
        const int RestDraws = 8;

        /// <summary>Seconds a car will spend looking for its kerb before it gives up and
        /// goes round again. A street with every kerb taken is not a place to stand.</summary>
        const float ParkingPatience = 120f;

        float _parkingBy;

        /// <summary>
        /// A CAR PUT DOWN AT A KERB when the city opens: already parked, resting, spread
        /// over the map by the builder. Its station is still its home - the kerb a
        /// prisoner is walked in from, the route there - but it never docks.
        /// </summary>
        public void InitAtKerb(Vector3 at, Quaternion facing, RoadEdge home, float kerbS,
            List<RoadEdge> allEdges, Dictionary<RoadEdge, RoadEdge> routeHome,
            Vector2 restRange, Vector2Int waypointRange, float firstRest)
        {
            _home = home;
            _kerbS = kerbS;
            _kerb = home.Start + home.Dir * kerbS;
            _allEdges = allEdges;
            _routeHome = routeHome;
            _restRange = restRange;
            _waypointRange = new Vector2Int(
                Mathf.Max(1, waypointRange.x), Mathf.Max(1, waypointRange.y));

            HasBay = false;
            RestsAtKerbs = true;
            KerbRestSeconds = restRange;
            Profile = DriverProfile.Patrol;
            Tag = "police";
            Net ??= LaneNet.Active;
            Tf.SetPositionAndRotation(at, facing);
            PlaceAt(at, facing * Vector3.forward);
            RestSpot = at;
            HasRestSpot = true;
            State = Mode.Resting;
            _restTimer = firstRest;
            if (!Fleet.Contains(this)) Fleet.Add(this);
        }

        /// <summary>The round is over: find a kerb and stand on it. Entered from the
        /// corner the last waypoint was reached at; the goal itself is set on the next
        /// tick, outside the turn choice.</summary>
        void BeginParking()
        {
            State = Mode.Parking;
            _waypoint = null;
            _routeToWaypoint = null;
            _responseRetryAt = 0f;
            _parkingBy = Time.time + ParkingPatience;
            HasRestSpot = false;
        }

        /// <summary>
        /// WHICH KERB. Draws over the whole lane graph, like the waypoints, and keeps the
        /// draw farthest from where the rest of the fleet is standing or heading - so a
        /// fleet of ten ends up one to a block rather than three to the station's street.
        /// Then the nearest legal kerb slot to that draw, which is what actually gets
        /// parked on.
        /// </summary>
        void SetRestGoal()
        {
            _responseRetryAt = Time.time + 1.25f;
            var net = Net ?? LaneNet.Active;
            if (net == null || _allEdges == null || _allEdges.Count == 0) return;

            RoadEdge best = null;
            var bestScore = -1f;
            for (var draw = 0; draw < RestDraws; draw++)
            {
                var edge = _allEdges[Random.Range(0, _allEdges.Count)];
                if (edge == null || edge == _home || edge.Auxiliary || edge.Length < 30f) continue;
                var mid = edge.Start + edge.Dir * (edge.Length * 0.5f);
                var score = SpreadScore(mid);
                if (score > bestScore) { bestScore = score; best = edge; }
            }
            if (best == null) return;

            var near = best.Start + best.Dir * (best.Length * 0.5f);
            if (!CrewCars.KerbSlotNear(net, near, HalfLen, HalfWide, out var kerb, out _))
                return;   // no legal kerb on that street: the next tick draws again
            RestSpot = kerb;
            HasRestSpot = true;
            GoTo(kerb, park: true);
        }

        /// <summary>Metres to the nearest other car's kerb (or the one it is driving
        /// to). What the choice maximises.</summary>
        float SpreadScore(Vector3 at)
        {
            var nearest = float.MaxValue;
            for (var i = 0; i < Fleet.Count; i++)
            {
                var other = Fleet[i];
                if (other == null || other == this || other.Wrecked || other.Tf == null ||
                    !other.HasRestSpot) continue;
                var d = LivingCity.Police.PoliceProcedure.AirDistanceSquared(
                    at.x, at.z, other.RestSpot.x, other.RestSpot.z);
                if (d < nearest) nearest = d;
            }
            return nearest;
        }

        /// <summary>Rested: out of the kerb and round the city again.</summary>
        void LeaveTheKerb()
        {
            HasRestSpot = false;
            BackOnTheRound();
            PullOut();
        }

        /// <summary>Asked for a bay at the moment the car reaches its station's kerb with
        /// nowhere to put itself. Returns true when one was found - the caller has taken
        /// the bay in the station's book and handed it over with TakeBay.</summary>
        public System.Func<PolicePatrolCar, bool> AskForABay;

        /// <summary>A bay has been given to this car; from here it docks like any other.
        /// </summary>
        public void TakeBay(Vector3 stall, Quaternion stallRot)
        {
            _stall = stall;
            _stallRot = stallRot;
            HasBay = true;
        }

        /// <summary>Round the city again: no bay was free, and a police car does not
        /// stand at a kerb waiting for one.</summary>
        void BackOnTheRound()
        {
            // whatever it was driving to is forgotten, and a car stood at a kerb pulls
            // out: a round that kept a parking goal would have parked at the end of it
            // and stood there for ever, patrolling in name only
            if (HasGoal) Stop();
            if (Parked) PullOut();
            State = Mode.Patrolling;
            // off the watch, the next corner is the end of the round
            _waypointsLeft = OffWatch ? 0 : Random.Range(_waypointRange.x, _waypointRange.y + 1);
            _waypoint = null;
            _routeToWaypoint = null;
        }

        /// <summary>Whether this car is standing in the yard with no bay of its own.
        /// </summary>
        bool _inTheYard;

        /// <summary>
        /// OFF THE WATCH WITH NOWHERE TO PARK. A car that reaches its station at the end
        /// of its shift and finds every bay held cannot go round again - the whole
        /// meaning of a shift is that its cars are OFF the road - and it cannot stand at
        /// the kerb either, which is the one thing the traffic has been proved not to
        /// survive. So it goes into the yard: off the lane graph, body away, answering
        /// nothing, until the watch calls it back out.
        /// </summary>
        void StandInTheYard()
        {
            if (Lane != null) Despawn();
            Swinging.Remove(this);
            _inTheYard = true;
            State = Mode.Resting;
            _restTimer = float.MaxValue;
            if (Tf != null)
            {
                // below the world as well as switched off: nothing that sweeps bodies
                // rather than the graph can read it as an obstacle at the kerb
                Tf.position = _kerb + Vector3.down * 50f;
                Tf.gameObject.SetActive(false);
            }
        }

        /// <summary>Back out of the yard when the watch turns: onto its own kerb lane and
        /// straight into the round, because there was no bay to swing out of.</summary>
        void OutOfTheYard()
        {
            _inTheYard = false;
            if (Tf != null)
            {
                Tf.gameObject.SetActive(true);
                Tf.SetPositionAndRotation(_kerb, Quaternion.LookRotation(_home.Dir));
            }
            Spawn(_home, _kerbS);
            // A call may have selected this rolling reserve while it was in the yard.
            // Preserve that call across the spawn instead of sending the car on a random
            // patrol and leaving _sceneWanted latched for ever.
            if (_sceneWanted) BeginResponding();
            else BackOnTheRound();
        }

        /// <summary>The officer at the wheel (CarOccupant), set by the builder. Shown
        /// whenever the car is out: in its stall he is indoors, and at a scene the
        /// squad that climbed out (PoliceDispatch) is him.</summary>
        public CarOccupant Officer;

        /// <summary>Which precinct's car this is. One station in the city today, so it
        /// is nought everywhere - it exists because a loss has to land on the right
        /// roster the day the city has several (GAN-226, ROSTER-004).</summary>
        public int Precinct { get; set; }

        /// <summary>Parked for the watch. A car of a shift that is not on is docked and
        /// stays docked: it answers no call, because the whole meaning of a night shift
        /// with more cars on it is that the day shift's cars are NOT on it.</summary>
        public bool OffWatch { get; private set; }

        /// <summary>Off duty: the round it is on is its last, then it docks and stays.
        /// Never yanked off the road mid-leg - a car that vanished from the street at
        /// seven o'clock would read as the car having been deleted.</summary>
        public void StandDown()
        {
            if (OffWatch) return;
            OffWatch = true;
            _waypointsLeft = 0;   // the next corner it reaches sends it home
        }

        /// <summary>On duty: out of the stall at the handover, or as soon as the kerb
        /// is clear.</summary>
        public void StandTo(float firstRest = 0f)
        {
            if (!OffWatch) return;
            OffWatch = false;
            if (State == Mode.Resting) _restTimer = Mathf.Min(_restTimer, firstRest);
        }

        public void TickPatrol(float dt)
        {
            if (Officer != null)
                Officer.Show(State != Mode.OnScene &&
                             (State != Mode.Resting || (RestsAtKerbs && !_inTheYard)));
            switch (State)
            {
                case Mode.Resting:
                    if (RestsAtKerbs && !_inTheYard)
                    {
                        // at a kerb somewhere in the city; off the watch it stays put
                        if (OffWatch) break;
                        _restTimer -= dt;
                        if (_restTimer <= 0f) LeaveTheKerb();
                        break;
                    }
                    if (_inTheYard)
                    {
                        // no bay to swing out of; it rejoins the road at its kerb
                        if (!OffWatch && KerbClear()) OutOfTheYard();
                        break;
                    }
                    if (OffWatch && !_sceneWanted) break;   // off the watch: it stays in
                    _restTimer -= dt;
                    if ((_restTimer <= 0f || _sceneWanted) && KerbClear()) BeginUndock();
                    break;

                case Mode.Undocking:
                case Mode.Docking:
                    _t = PatrolDocking.Advance(_curve, _t, PatrolDocking.Speed * dt);
                    Tf.SetPositionAndRotation(PatrolDocking.Point(_curve, _t),
                        _steerByTangent
                            ? PatrolDocking.Heading(_curve, _t, _toRot)
                            : Quaternion.Slerp(_fromRot, _toRot, Mathf.SmoothStep(0f, 1f, _t)));
                    // off the graph the transform is driven by hand, and the street reads
                    // where a car IS off its road position, never off the transform: left
                    // at the kerb, a car in its stall stood as a phantom in the running
                    // lane for the whole of its rest (the same word CrewBike gives a spill)
                    Slid(Tf.position);
                    // the body is out of the lane by mid-swing: off the graph from here
                    if (State == Mode.Docking && Lane != null && _t >= 0.5f) Despawn();
                    if (_t < 1f) break;
                    Swinging.Remove(this);
                    if (State == Mode.Undocking)
                    {
                        State = Mode.Patrolling;
                        _waypointsLeft = Random.Range(_waypointRange.x, _waypointRange.y + 1);
                        _waypoint = null;
                        _routeToWaypoint = null;
                        Spawn(_home, _kerbS);
                        // a replacement in the kerb model leaves the yard for good: the
                        // bay it was spawned in is the next replacement's
                        if (RestsAtKerbs) HasBay = false;
                        if (_sceneWanted) { _sceneWanted = false; BeginResponding(); }
                    }
                    else
                    {
                        if (Lane != null) Despawn();   // a swing short enough to skip the midpoint
                        State = Mode.Resting;
                        _restTimer = Random.Range(_restRange.x, _restRange.y);
                        Tf.SetPositionAndRotation(_stall, _stallRot);
                        Slid(_stall);
                    }
                    break;

                case Mode.Patrolling:
                case Mode.Returning:
                    Tick(dt);
                    if (State != Mode.Returning || CurrentEdge != _home)
                    {
                        _askedAtHome = false;
                        break;
                    }
                    // NO BAY, NO STOP. The bay is asked for as the car turns onto the
                    // home lane, not at the kerb: a car with nothing to swing into used
                    // to roll to a dead stop in the running lane anyway and only then
                    // learn it was going round again - and with a fleet bigger than the
                    // yard, that stop, ten metres off the junction, was a queue back
                    // into the box (MiniCoreDemo, 2026-09-04). A car with men in the
                    // back, or one off the watch, still has to stop.
                    if (!_askedAtHome)
                    {
                        _askedAtHome = true;
                        if (!HoldAtKerb &&
                            (RestsAtKerbs || (!HasBay && !OffWatch &&
                                              (AskForABay == null || !AskForABay(this)))))
                        {
                            BackOnTheRound();
                            break;
                        }
                    }
                    // at the kerb, and the swing is nobody else's: LimitTarget holds
                    // the car at nought here, so a held lease costs a tick's wait. A
                    // car held for its load stands until custody lets it go.
                    if (Progress >= _kerbS - 0.15f && !SwingHeld() && !HoldAtKerb)
                    {
                        if (RestsAtKerbs) BackOnTheRound();   // unloaded; the street is home
                        else if (HasBay || (AskForABay != null && AskForABay(this))) BeginDock();
                        else if (OffWatch) StandInTheYard();
                        else BackOnTheRound();
                    }
                    break;

                case Mode.Responding:
                    Tick(dt);
                    // The parking goal already names the nearest free legal kerb. Do
                    // not reject its completed answer through a second, unrelated
                    // radius around the shop: that made a correctly parked car pull
                    // out and drive past the prisoners again. A fake lane-centre
                    // "Parked" state is still rejected.
                    if (LivingCity.Police.PoliceProcedure.ResponseCarArrived(
                            AtGoal, ParkedAtKerb))
                        State = Mode.OnScene;
                    else if (!HasGoal && Time.time >= _responseRetryAt)
                        SetResponseGoal();
                    break;

                case Mode.Parking:
                    Tick(dt);
                    if (LivingCity.Police.PoliceProcedure.ResponseCarArrived(
                            AtGoal, ParkedAtKerb))
                    {
                        State = Mode.Resting;
                        _restTimer = Random.Range(KerbRestSeconds.x, KerbRestSeconds.y);
                    }
                    else if (Time.time > _parkingBy)
                        BackOnTheRound();   // every kerb it tried was taken: round again
                    else if (!HasGoal && Time.time >= _responseRetryAt)
                        SetRestGoal();
                    break;

                case Mode.OnScene:
                    break; // parked at the kerb, doors open, men out
            }
        }

        // ------------------------------------------------------------ the call

        Transform IPoliceUnit.Tf => Tf;
        Vector3 IPoliceUnit.Position => Tf.position;
        public bool Available => !_sceneWanted && !OffWatch && !Wrecked &&
            (State == Mode.Resting || State == Mode.Undocking ||
             State == Mode.Patrolling || State == Mode.Returning ||
             State == Mode.Docking || State == Mode.Parking);
        bool IPoliceUnit.Available => Available;
        bool IPoliceUnit.OnScene => State == Mode.OnScene;
        bool IPoliceUnit.Carries => true;

        /// <summary>Sent to a scene through the shared road-car goal. The car routes to
        /// the nearest reachable kerb, pulls fully out of the running lane and only then
        /// reports OnScene.</summary>
        public void RouteTo(Vector3 scene, float standOff)
        {
            _scenePos = scene;
            _sceneStandOff = Mathf.Max(0f, standOff);
            switch (State)
            {
                case Mode.Resting:
                    if (RestsAtKerbs && !_inTheYard)
                    {
                        // at a kerb, not in a stall: it pulls out on the order itself
                        HasRestSpot = false;
                        BeginResponding();
                        break;
                    }
                    _sceneWanted = true; // out of the stall first (Resting starts the undock)
                    break;
                case Mode.Undocking:
                case Mode.Docking:
                    _sceneWanted = true; // out of the stall first (Resting starts the undock)
                    break;
                case Mode.Patrolling:
                case Mode.Returning:
                case Mode.Responding:
                case Mode.Parking:
                // AND A CAR STOOD AT A SCENE CAN BE SENT ON TO THE NEXT ONE. Without this
                // a transfer that pulled in to collect its man could never leave again:
                // every other caller gates on Available, which OnScene is not, so nothing
                // else reaches this case (GAN-237).
                case Mode.OnScene:
                    BeginResponding();
                    break;
            }
        }

        void BeginResponding()
        {
            State = Mode.Responding;
            _sceneWanted = false;
            Profile = DriverProfile.Police;   // the lights on: brisk, the crown, a red when the box is clear
            SetResponseGoal();
        }

        void SetResponseGoal()
        {
            _responseRetryAt = Time.time + 1.25f;
            _waypoint = null;
            _routeToWaypoint = null;
            var net = Net ?? LaneNet.Active;
            var hasKerb = CrewCars.KerbSlotNear(net, _scenePos,
                HalfLen, HalfWide, out var kerb, out _);
            if (!hasKerb)
                hasKerb = CrewCars.NearestLegalKerbSlot(net, _scenePos,
                    HalfLen, HalfWide, out kerb, out _);

            // The selected world point is already the closest clear slot, so applying
            // another stand-off would move the goal away from it. The old scene goal is
            // retained only as a defensive fallback for a network with no legal kerb.
            if (hasKerb)
                GoTo(kerb, park: true);
            else
                GoTo(_scenePos,
                    park: LivingCity.Police.PoliceProcedure.ResponseCarsParkAtKerb,
                    standOff: _sceneStandOff);
        }

        /// <summary>Done at the scene: back to the station.</summary>
        public void Release()
        {
            if (State != Mode.Responding && State != Mode.OnScene) { _sceneWanted = false; return; }
            Profile = DriverProfile.Patrol;
            if (RestsAtKerbs && !HoldAtKerb)
            {
                // nothing to take to the station: the round goes on from here, and
                // ends at whichever kerb the spread gives it
                HasRestSpot = false;
                BackOnTheRound();
                PullOut();
                return;
            }
            State = Mode.Returning;
            _waypoint = null;
            _routeToWaypoint = null;
            // Parking at a scene leaves the car off its lane. A real road goal pulls
            // it out again and carries it back to the station kerb.
            GoTo(_kerb, park: false);
        }

        protected override bool Fearless => true;

        /// <summary>Room behind the kerb, in seconds. The swing from the bay to the
        /// kerb is walked at PatrolDocking.Speed and takes its time; a car that would
        /// reach the kerb inside that time, at the speed it is doing, meets us halfway
        /// out - and a car on the running lane has no lead to brake for until the
        /// footprint is already in it (DEPOT-004 S2 run-01, 2026-09-03: one undock, a
        /// civilian nose to tail with it for 65 s, 2 274 belt refusals). So the window
        /// behind is TIME, not metres, and it reaches back onto the roads that feed
        /// this one. A busy moment costs only the next tick's retry.</summary>
        float SwingSeconds =>
            Vector3.Distance(_stall, _kerb) * 1.4f / PatrolDocking.Speed + 1.5f;

        /// <summary>THE KERB LEASE. A car on the swing between its bay and the kerb is
        /// on no road, so the cars still in their bays cannot see it on the lane the
        /// way they see traffic - and two of one yard with rests five seconds apart
        /// swung out together and met at the kerb, three pairs in one run, 27 000
        /// belt refusals (DEPOT-004 S2 seed 102). One car of a yard swings at a time,
        /// out or in; the next waits its tick in the bay or at the kerb.</summary>
        static readonly List<PolicePatrolCar> Swinging = new List<PolicePatrolCar>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetLeases() { Swinging.Clear(); Fleet.Clear(); }

        bool SwingHeld()
        {
            for (int i = 0; i < Swinging.Count; i++)
            {
                var other = Swinging[i];
                if (other == this || other.Wrecked) continue;   // a wreck holds nothing
                if (other._home == _home && Mathf.Abs(other._kerbS - _kerbS) < 30f) return true;
            }
            return false;
        }

        bool KerbClear() =>
            !SwingHeld() && LaneGate.Clear(_home, _kerbS, SwingSeconds, this);

        void BeginUndock()
        {
            Swinging.Add(this);
            State = Mode.Undocking;
            _curve = PatrolDocking.Undock(
                _stall, _stallRot * Vector3.forward, _kerb, _home.Dir);
            _t = 0f;
            _fromRot = _stallRot;
            _toRot = Quaternion.LookRotation(_home.Dir);
            _steerByTangent = true;
        }

        void BeginDock()
        {
            Swinging.Add(this);
            // NOT off the road yet. The first half of the swing is still in the lane,
            // and a car that left the graph at the kerb was no longer anybody's lead: the
            // car behind, just out of the box, drove into the swinging body (DEPOT-004
            // S2 seed 101, one refusal). It stays a standing car on the lane - Slid keeps
            // its (s, d) under the body - and leaves the graph once the body has cleared
            // the lane (the Docking tick).
            State = Mode.Docking;
            _curve = PatrolDocking.Dock(Tf.position, _stall, _stallRot * Vector3.forward);
            _t = 0f;
            _fromRot = Tf.rotation;
            _toRot = _stallRot;
            _steerByTangent = false;
        }

        // Reaching the current waypoint edge draws the next one - anywhere on the
        // map - or, with the budget spent, flips to Returning. Both patrol legs
        // and the trip home ride the same BFS next-edge maps.
        protected override RoadEdge PickNext(RoadEdge straight, List<RoadEdge> lefts, List<RoadEdge> rights)
        {
            if (State == Mode.Patrolling && (_waypoint == null || CurrentEdge == _waypoint))
            {
                if (_waypointsLeft <= 0)
                {
                    if (RestsAtKerbs) BeginParking();
                    else State = Mode.Returning;
                }
                else
                {
                    _waypointsLeft--;
                    DrawWaypoint();
                }
            }

            if (State == Mode.Patrolling && _routeToWaypoint != null &&
                _routeToWaypoint.TryGetValue(CurrentEdge, out var toward) && toward != null)
                return toward;

            if (State == Mode.Returning &&
                _routeHome.TryGetValue(CurrentEdge, out var homeward) && homeward != null)
                return homeward;

            return base.PickNext(straight, lefts, rights);
        }

        void DrawWaypoint()
        {
            for (int attempt = 0; attempt <= WaypointRetries; attempt++)
            {
                var target = _allEdges[Random.Range(0, _allEdges.Count)];
                if (target == CurrentEdge || target == _home) continue;

                var map = RouteToward(_allEdges, target);
                if (!map.ContainsKey(CurrentEdge)) continue;

                _waypoint = target;
                _routeToWaypoint = map;
                return;
            }

            // no reachable draw - wander this leg instead of stalling the patrol
            _waypoint = null;
            _routeToWaypoint = null;
        }

        // Returning to the station retains the old station-kerb clamp. Scene response
        // uses RoadCar.GoTo(..., park: true), the same complete pull-in as crew cars.
        protected override float LimitTarget(float target)
        {
            if (State == Mode.Returning && CurrentEdge == _home && Progress <= _kerbS)
                target = Mathf.Min(target, Allowed(0f, _kerbS - Progress));
            return target;
        }

        /// <summary>
        /// Reverse BFS from the target lane over the turn graph (no U-turns), then
        /// one greedy hop per lane: the value is the next lane after the key on the
        /// shortest route to the target. The target itself maps to its shortest
        /// loop, so overshooting comes around again. Cheap - the demo's graph is a
        /// hundred-odd edges - so patrol legs build one per waypoint draw.
        /// </summary>
        public static Dictionary<RoadEdge, RoadEdge> RouteToward(List<RoadEdge> edges, RoadEdge target)
        {
            var dist = new Dictionary<RoadEdge, int> { [target] = 0 };
            var queue = new Queue<RoadEdge>();
            queue.Enqueue(target);
            while (queue.Count > 0)
            {
                var f = queue.Dequeue();
                foreach (var e in f.From.Incoming)
                {
                    if (Vector3.Dot(f.Dir, e.Dir) < -0.5f) continue; // U-turn
                    if (dist.ContainsKey(e)) continue;
                    dist[e] = dist[f] + 1;
                    queue.Enqueue(e);
                }
            }

            var next = new Dictionary<RoadEdge, RoadEdge>();
            foreach (var e in edges)
            {
                RoadEdge best = null;
                int bestD = int.MaxValue;
                foreach (var f in e.To.Outgoing)
                {
                    if (Vector3.Dot(f.Dir, e.Dir) < -0.5f) continue;
                    if (dist.TryGetValue(f, out int d) && d < bestD) { bestD = d; best = f; }
                }
                if (best != null) next[e] = best;
            }
            return next;
        }

        // ------------------------------------------------------------ the marker

        Transform IPatrolMarker.MarkerTf => Tf;
        float IPatrolMarker.MarkerHeight => 2.8f;
        bool IPatrolMarker.MarkerDimmed => State == Mode.Resting;
        string IPatrolMarker.MarkerTitle => "Patrol Car " + UnitNumber;

        string IPatrolMarker.MarkerLine => State switch
        {
            Mode.Resting => "Resting at the station",
            Mode.Undocking => "Pulling out on patrol",
            Mode.Patrolling => _waypoint != null
                ? "On patrol - making for the "
                    + PatrolInfo.Toward(Tf.position, WaypointPos()) + " of town - "
                    + (_waypointsLeft == 0 ? "last stop, then home"
                                           : _waypointsLeft + " more stops until return")
                : "On patrol heading " + PatrolInfo.Heading(Tf),
            Mode.Returning => "Returning to the station",
            Mode.Docking => "Parking at the station",
            Mode.Responding => "Responding - shots fired " + PatrolInfo.Toward(Tf.position, _scenePos) + " of here",
            Mode.OnScene => "At the scene",
            _ => string.Empty,
        };

        Vector3 WaypointPos() => (_waypoint.Start + _waypoint.End) * 0.5f;
    }
}
