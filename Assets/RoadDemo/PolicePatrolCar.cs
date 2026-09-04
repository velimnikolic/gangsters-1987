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

        public enum Mode { Resting, Undocking, Patrolling, Returning, Docking, Responding, OnScene }

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
            State = Mode.Patrolling;
            _waypointsLeft = Random.Range(_waypointRange.x, _waypointRange.y + 1);
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
            if (Officer != null) Officer.Show(State != Mode.Resting && State != Mode.OnScene);
            switch (State)
            {
                case Mode.Resting:
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
                    // at the kerb, and the swing is nobody else's: LimitTarget holds
                    // the car at nought here, so a held lease costs a tick's wait
                    if (State == Mode.Returning && CurrentEdge == _home &&
                        Progress >= _kerbS - 0.15f && !SwingHeld())
                    {
                        if (HasBay || (AskForABay != null && AskForABay(this))) BeginDock();
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
             State == Mode.Docking);
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
                case Mode.Undocking:
                case Mode.Docking:
                    _sceneWanted = true; // out of the stall first (Resting starts the undock)
                    break;
                case Mode.Patrolling:
                case Mode.Returning:
                case Mode.Responding:
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
            State = Mode.Returning;
            _waypoint = null;
            _routeToWaypoint = null;
            Profile = DriverProfile.Patrol;
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
        static void ResetLeases() => Swinging.Clear();

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
                    State = Mode.Returning;
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
