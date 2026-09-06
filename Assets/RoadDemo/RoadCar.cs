using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    public enum Turn { Straight, Left, Right }

    /// <summary>
    /// A car on the lane network - every car: the traffic, the outfit's, the law's.
    /// It lives in its carriageway's frame, (s, d), and moves parametrically: s runs
    /// along the road at its speed, d follows a lateral profile (a smoothstep from
    /// where it was to where it is going - a lane change, a pull-in, a swing round
    /// something), junctions are crossed on a connector's polyline, and a turn in
    /// the road is an arc. Nothing here steers toward a point and hopes.
    ///
    /// Road occupancy publishes the body and active manoeuvre; following and
    /// manoeuvre admission respect that shared state. Junction admission checks
    /// body envelopes and exit room, retaining its claim until the tail clears.
    /// RoadSpace guards physical steps. A RoadDeadlock lease may temporarily
    /// exempt one mutually blocked traffic pair at walking speed, never parking.
    ///
    /// Who is driving is a DriverProfile: thresholds and permissions only.
    /// </summary>
    public partial class RoadCar : IRoadUser
    {
        static readonly List<RoadCar> Registered = new List<RoadCar>();

        /// <summary>Every simulated vehicle, including ones temporarily pulled off the
        /// lane list at a pump or kerb. World fog reads this without changing traffic.</summary>
        public static IReadOnlyList<RoadCar> All => Registered;

        /// <summary>Drop the cars that are no longer on any street: the backstop clears
        /// itself on Vanish, but a whole district streamed out destroys its bodies without
        /// going through it, and a list that only ever grows would make every reader of
        /// <see cref="All"/> slower for the rest of the session.</summary>
        public static void PruneRegistered()
        {
            for (var i = Registered.Count - 1; i >= 0; i--)
            {
                var car = Registered[i];
                if (car == null || car.Gone || car.Tf == null)
                {
                    // A destroyed view must leave the collision/occupancy books too.
                    // Scene reload used to remove it only from Registered, leaving
                    // invisible stationary cars in StreetTraffic.Users forever.
                    car?.Despawn();
                    StreetTraffic.Users.Remove(car);
                    Registered.RemoveAt(i);
                }
            }
        }

        public RoadCar()
        {
            Deadlock = new RoadDeadlock(this);
            Registered.Add(this);
        }

        internal readonly RoadDeadlock Deadlock;
        System.Predicate<RoadOccupant> _escapeLeaderFilter;
        bool BlocksEscapePath(RoadOccupant other) => !Deadlock.Ignores(other.Car);
        internal Connector PlannedCrossing => _via;
        internal bool CanEaseTraffic => OnRoad && !Gone && !Wrecked && !Derelict && !Parked &&
            !_halted && !_haltWhenClear && !EngineDead && !Sliding && _man == Manoeuvre.None &&
            !(_hasGoal && _goalPark) && !(Via?.UTurn ?? false) && Mathf.Abs(CrossingOffset) < .05f;
        internal bool TrafficPoseAhead(float distance, out Vector3 position, out Vector3 forward)
        {
            position = Position; forward = Forward;
            if (Via != null)
            {
                if (ViaS + distance - Via.Length + HalfLen >= Via.To.Length) return false;
                JunctionClearance.Pose(Via, ViaS + distance, Axle, out position, out forward);
                return RecoveryPeopleClear(position, forward);
            }
            if (Road == null || Road.Path != null) return false;
            float station = S + Heading * distance;
            float progress = Heading > 0 ? station : Road.Length - station;
            if (progress > Road.Length - HalfLen || progress < HalfLen && !_boxLeft) return false;
            // Crossing the connector/road seam does not end the body's real curve.
            // Its rear axle can still be in the box while its centre is on this road.
            if (_tailVia != null)
            {
                float behind = (_tailViaEndS - (station - Heading * Axle)) * Heading;
                if (behind > 0f && behind <= _tailVia.Length)
                {
                    _tailVia.Pose(_tailVia.Length - behind, out position, out forward);
                    position += forward * Axle;
                    return RecoveryPeopleClear(position, forward);
                }
            }
            forward = Road.DirAt(station) * Heading;
            position = Road.Pose(station - Heading * Axle, D) + forward * Axle;
            return RecoveryPeopleClear(position, forward);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetRegisteredCars() => Registered.Clear();

        public enum Manoeuvre { None, Pass, Crown, UTurn, PullIn, PullOut, Reverse, Aside, LaneChange }

        // ------------------------------------------------------------------ body

        public Transform Tf;
        /// <summary>The name shown on an order card. Owned cars replace it from the
        /// ledger; civic cars keep their service name.</summary>
        public string DisplayName = "Car";
        /// <summary>Collision and parking clearance cover the complete visible body.
        /// Apply once when measuring/spawning, equally for moving and parked cars.</summary>
        public const float TrafficFootprintScale = 1f;
        public float HalfLen = 2.3f * TrafficFootprintScale;
        public float HalfWide = 0.95f * TrafficFootprintScale;
        /// <summary>Metres from the body's origin back to the rear axle: the point that
        /// follows the line, the front swinging into the bend ahead of it (a car steers
        /// with its front wheels). Unset: read as six tenths of the half length.</summary>
        public float AxleBack = float.NaN;
        float Axle => float.IsNaN(AxleBack) || AxleBack <= 0f ? HalfLen * 0.6f : Mathf.Min(AxleBack, HalfLen);
        internal float CrossingAxle => Axle;
        internal float CrossingOffset => Via != null ? _viaD0 : (D - _laneD) * Heading;
        /// <summary>The road surface's height: where the body sits.</summary>
        public float RoadY;
        public DriverProfile Profile = DriverProfile.Traffic;
        public LaneNet Net;

        // ------------------------------------------------------------------ the machine

        LivingCity.Gameplay.VehiclePerformance.Machine _machine;
        bool _machineKnown;

        /// <summary>What is under him, as against who he is. The DriverProfile says how
        /// fast he MEANS to go and what he is willing to do about what is in his way;
        /// this says what the body he is in can actually manage, as three multipliers on
        /// those numbers (VehiclePerformance.Machine - the pace, the pull, the grip).
        /// Every pace, every braking curve and every bend in this file reads through it.
        ///
        /// Nobody has to set it. Left alone it is read ONCE off the name of the body's
        /// own transform, which is the pack prefab's name at every place in the game that
        /// builds a vehicle - so a car gets its machine by being built, not by whoever
        /// built it remembering to hand it one. The setter is for the two cases where the
        /// transform was renamed for the hierarchy's sake and the prefab is still in the
        /// builder's hand (DemoCrews.AddCar names one "Outfit Car").</summary>
        public LivingCity.Gameplay.VehiclePerformance.Machine Machine
        {
            get
            {
                if (_machineKnown) return _machine;
                // before there is a body there is nothing to read: answer plainly and
                // do NOT remember the answer, or a car asked one frame too early would
                // drive like a saloon for the rest of its life
                if (Tf == null) return LivingCity.Gameplay.VehiclePerformance.Ordinary;
                _machine = LivingCity.Gameplay.VehiclePerformance.For(Tf.name);
                _machineKnown = true;
                return _machine;
            }
            set { _machine = value; _machineKnown = true; }
        }

        /// <summary>The driver's numbers with the machine folded in: what the driving
        /// actually uses. Nothing else in this file reads the profile's own Accel, Brake,
        /// HardBrake, LateralG, TurnSpeed or Cruise any more - a lorry and a supercar
        /// with the same commuter at the wheel used to pull away from a light together,
        /// and that is the whole of what these six lines are for.
        ///
        /// Profile.UTurnSpeed is deliberately NOT among them and is still read raw. A
        /// turn in the road is a manoeuvre a driver chooses to make at a walking pace,
        /// not a limit the machine imposes; what the machine does say is how tight the
        /// arc may be, and that comes through LateralG, which is scaled.</summary>
        protected float Accel => Profile.Accel * Machine.Pull;
        protected float Brake => Profile.Brake * Machine.Grip;
        protected float HardBrake => Profile.HardBrake * Machine.Grip;
        protected float LateralG => Profile.LateralG * Machine.Grip;
        protected float JunctionSpeed => Profile.TurnSpeed * Machine.Grip;
        /// <summary>The pace with no road under it - what a driver who keeps to no limit
        /// would do. <see cref="Cruise"/> is the one to ask when there IS a road.</summary>
        protected float TopSpeed => Profile.Cruise * Machine.Top;

        static int _ids;
        public readonly int Id = ++_ids;

        /// <summary>How many times the belt had to refuse a step, all cars: a
        /// planning bug counter - it should read nought.</summary>
        public static int BeltHits;

        /// <summary>Stopped for good where it should not be - across a junction, the
        /// driver gone. It holds nothing against anybody: the street drives round it.</summary>
        public bool Derelict { get; private set; }

        /// <summary>Blown apart where it stood (CarShatter): the body is being torn into
        /// loose debris and the husk is off the network for good - it holds no lane, no
        /// box, and Tick does nothing for it. Unlike a wreck's cousin Derelict (a car
        /// that merely stopped and is driven round), a wreck is GONE from the model.</summary>
        public bool Wrecked { get; private set; }

        /// <summary>Take the car out of the traffic model, for good: off its lane, out
        /// of any junction box, speed nil, never ticked again. Called the instant a
        /// bomb goes off under it, before its shell is pulled to pieces - so nobody
        /// queues behind a car that is no longer there.</summary>
        public void Wreck()
        {
            if (Wrecked) return;
            Wrecked = true;
            _parkPlanReady = _hasGoal = false;
            Speed = 0f;
            Leave();
            OnWrecked();
        }

        /// <summary>A derived vehicle may release auxiliary static claims when its
        /// physical body is destroyed.</summary>
        protected virtual void OnWrecked() { }

        // ------------------------------------------------------------------ the tin

        /// <summary>Rounds that found something in the engine bay. Shared by every car:
        /// police paint is no harder than an outfit car's paint.</summary>
        public int EngineHits { get; private set; }

        public static int EngineHitsToKill = 3;
        public static float EngineChance = 0.4f;

        public bool EngineDead => EngineHits >= EngineHitsToKill;
        public bool EngineJustDied { get; private set; }

        /// <summary>A round into the body. The front third may stop the engine; every
        /// round leaves its hole on the panel it reached.</summary>
        public void TakeRound(Vector3 at, Vector3 from)
        {
            if (Tf == null || Wrecked) return;
            var local = Tf.InverseTransformPoint(at);
            var bonnet = local.z > HalfLength * 0.34f;
            if (bonnet && !EngineDead && Random.value < EngineChance)
            {
                EngineHits++;
                if (EngineDead)
                {
                    EngineJustDied = true;
                    CarSmoke.Bonnet(this);
                }
            }

            var acrossness = Mathf.Abs(local.x) / Mathf.Max(HalfWidth, 1e-3f);
            var alongness = Mathf.Abs(local.z) / Mathf.Max(HalfLength, 1e-3f);
            var outward = acrossness >= alongness
                ? Tf.right * Mathf.Sign(local.x == 0f ? 1f : local.x)
                : Tf.forward * Mathf.Sign(local.z == 0f ? 1f : local.z);
            CrewGore.Hole(Tf, at, outward);
        }

        public bool TakeEngineDeath()
        {
            if (!EngineJustDied) return false;
            EngineJustDied = false;
            return true;
        }

        /// <summary>Immediate recovery used by a transfer facing a full-width player
        /// barricade. It ignores the normal near-junction jam gate: first turn round if
        /// the sweep is available, otherwise reverse far enough to make another attempt.</summary>
        public bool EscapeBarricade()
        {
            if (Wrecked || Derelict) return false;
            // Finish clearing a junction before stopping, or simply brake if the car
            // has not yet settled on a lane. Both are live waiting states: RouteTo
            // wakes the car when MOVE ON opens the road again.
            if (Road == null || Via != null)
            {
                BrakeForBarricade();
                return true;
            }
            if (_man == Manoeuvre.UTurn || _man == Manoeuvre.Reverse)
            {
                _halted = false;
                return true;
            }
            if (Mathf.Abs(Speed) > Profile.UTurnSpeed + 1.5f)
            {
                BrakeForBarricade();
                return true;
            }
            _halted = false;
            if (TryUTurn(escape: true))
            {
                // Once round, take the graph's long way to the destination. Leaving the
                // ordinary in-road turn preference armed would turn straight back into
                // the same full-width block on the following tick.
                _turnBackFor = TurnBackPatience + 1f;
                return true;
            }
            if (Sliding) return true;
            if (TryReverse(null)) return true;
            BrakeForBarricade();
            return true;
        }

        void BrakeForBarricade()
        {
            // Unlike Halt, this keeps the destination and next-hop table. They are what
            // the escape manoeuvre will re-plan from once the car has slowed enough to
            // turn; clearing them here made every successful U-turn immediately turn
            // back toward the obstruction.
            _haltHard = true;
            _freeGoal = null;
            if (Via != null)
            {
                _haltWhenClear = true;
                _keepGoalWhenHaltClear = true;
                return;
            }
            _haltWhenClear = false;
            _keepGoalWhenHaltClear = false;
            _halted = true;
            if (_man != Manoeuvre.UTurn && _man != Manoeuvre.Reverse)
            {
                _man = Manoeuvre.None;
                ClearClaim();
            }
        }

        /// <summary>Gunfire has finished this carrier as a moving vehicle. It remains a
        /// road obstacle and can be routed around, unlike a bombed wreck.</summary>
        public void StandDerelict()
        {
            Halt(hard: true);
            if (Mathf.Abs(Speed) < 0.05f && Via == null)
                StandDown();
        }

        /// <summary>What held the car back this frame, for the overlay and the sim.</summary>
        public string Why = "";
        public string PassWhy = "";

        /// <summary>Why the turn in the road was refused this time - the trace's own
        /// question, and the player's: "empty street and he only turns at the end".</summary>
        public string UTurnWhy = "";

        /// <summary>No turning round in the road on this errand, whatever the table says
        /// is shorter. What it is for is the RIDE HOME from a drive-by: the way back the
        /// way you came is back past the men you have just emptied a gun at, and the
        /// shortest route home is exactly that. Set for the getaway leg, cleared by the
        /// next order.</summary>
        public bool NoTurnBack;

        /// <summary>This errand is not allowed to replace a turn in the current road with
        /// a route around the block. Crew drive-bys use it while shuttling on the target's
        /// carriageway; ordinary trips still give up after their normal patience and take
        /// the lane graph's detour.</summary>
        protected virtual bool RequiresInRoadTurn => false;

        /// <summary>Held by a junction ahead - the light, the box - or stood behind
        /// somebody who is: a queue, not a jam; nobody goes round a queue.</summary>
        public bool InQueue { get; private set; }
        public static string LastBeltHit = "";

        protected DriverNerve _nerve;
        protected virtual bool Fearless => Profile.Fearless;

        // ------------------------------------------------------------------ where

        public Carriageway Road { get; private set; }
        public RoadEdge Lane { get; private set; }
        /// <summary>Along the carriageway (metres from its A end) and across it
        /// (metres right of its axis); the way the nose points along the axis.</summary>
        public float S { get; private set; }
        public float D { get; private set; }
        public int Heading { get; private set; } = 1;
        /// <summary>In a junction: the connector and the metres along it.</summary>
        public Connector Via { get; private set; }
        public float ViaS { get; private set; }
        /// <summary>Metres a second along the travel direction; negative in reverse.</summary>
        public float Speed { get; protected set; }
        /// <summary>Stood at the kerb, engine off, not in anyone's way in the lane.</summary>
        public bool Parked { get; private set; }

        /// <summary>Switched off: nothing is turning under the bonnet, whatever the car
        /// is doing. A car standing at a pump is off, and a car being hand-driven across
        /// a forecourt is not - which is the difference Parked cannot express, since the
        /// forecourt is not the road and the car left it (Despawn) to get there.</summary>
        public bool EngineOff;
        public bool OnRoad => Road != null || Via != null;
        public Manoeuvre Doing => _man;

        /// <summary>Progress along the lane the car belongs to (for those that think
        /// in lanes: the patrol car's kerb, a spawn).</summary>
        public float Progress => Lane != null ? Lane.Progress(S) : 0f;
        public RoadEdge CurrentEdge => Lane;

        Vector3 _pos, _fwd = Vector3.forward;

        /// <summary>Where the car is, WITH the height of the road it is on: what the
        /// belt needs to tell a deck from the slip road passing under it. Everything
        /// else that reads this works flat and ignores y.</summary>
        public Vector3 RoadPosition => new Vector3(_pos.x, SurfaceLift(), _pos.z);
        public Vector3 RoadForward => _fwd;
        public float RoadSpeed => Mathf.Abs(Speed);
        public float HalfLength => HalfLen;
        public float HalfWidth => HalfWide;
        public Vector3 Position => _pos;
        public Vector3 Forward => _fwd;

        // ------------------------------------------------------------------ occupancy

        RoadOccupant _occ;       // on Road
        RoadOccupant _occNext;   // on the road beyond the junction while crossing it
        NodeOccupant _inNode;    // in a junction box
        RoadNode _nodeOf;        // whose box
        float _boxEntryS;        // road-s on the far road where we left the box
        bool _boxLeft;           // out of the box onto the far road, tail still to clear

        // ------------------------------------------------------------------ lateral profile

        float _dFrom, _dTo, _sFrom, _sLen;    // d runs dFrom -> dTo over sLen metres of travel from sFrom
        public bool Sliding => _sLen > 0f;
        float _viaD0;                          // lateral offset off the connector, eased out along it
        Connector _tailVia;                    // the connector just left, while the axle is still on it
        float _tailViaEndS;                    // the road s its end (the lane's start) is at

        // ------------------------------------------------------------------ route

        RoadEdge _next;
        Turn _turn;
        Connector _via;
        bool _committed;
        // Where we mean to come to rest this frame (road-s of the centre), or NaN.
        // Published on the band so whoever is behind brakes for the same place
        // rather than for our bumper - a queue at a red stops together that way.
        float _stopAt = float.NaN;
        // who the road says is in front of us this frame, and how far off - the one
        // thing a following fault cannot be read back from anything else
        int _leadId = -1; float _leadGap = -1f;
        // inside a box: the car on a crossing line we are standing for, and how
        // long we have stood - a standoff nothing else can end
        RoadCar _gaveWay; float _boxStuck;
        float _heldAtLine;
        float _waitingLineAt;
        bool yieldingForWaitingTurn;
        /// <summary>The next lane after each lane toward wherever the car is
        /// going (LaneNet.RouteToward); null: the driver wanders.</summary>
        public Dictionary<RoadEdge, RoadEdge> Route;

        // the goal: a stop on a road
        bool _hasGoal;
        Carriageway _goalRoad;
        float _goalS, _goalD;
        int _goalHeading;
        bool _goalPark;
        bool _goalStop = true;   // brake to a stop at the goal (else just pass it and say so)
        RoadEdge _goalLane;
        bool _halted, _haltHard; // told to stop where it stands

        // ------------------------------------------------------------------ manoeuvre

        Manoeuvre _man;
        float _laneD;            // the lane's own lateral, what a swing returns to
        float _manD;             // the band being driven
        float _manPastS;         // road-s the thing being passed ends at (travel sense)
        float _claimS0 = 1f, _claimS1 = -1f, _claimD0, _claimD1; // the claim beyond the body (S0 > S1: none)
        RoadOccupant _blocker;
        float _blockedFor, _jammed, _standoffFor;
        IRoadUser _backedFor;
        float _backedAt = -999f;

        /// <summary>Have we already backed off THIS body, recently enough that doing it
        /// again would just be the same two metres over? One reverse per blocker was the
        /// old rule and it was a rule for life: a car that had once backed off the
        /// motorcycle parked in front of it could never do it again, so the next order to
        /// drive - a minute later, a different errand - found it wedged in the same slot
        /// with nothing left to try. A driver forgets; so does this.</summary>
        bool JustBackedOff(RoadOccupant o) =>
            o != null && ReferenceEquals(_backedFor, o.Who) && RoadCarSimulation.Now - _backedAt < 15f;
        IRoadUser _jamLeader;      // what the jam was behind: the far lane stays allowed against it
        float _backLeft;
        float _giveUntil, _yieldUntil;
        bool _asideDone;
        // the arc of a turn in the road
        float _arcS0, _arcR, _arcAng;
        int _arcSide;
        float _stuckFor;

        // ------------------------------------------------------------------ setup

        /// <summary>Onto this lane at this progress, stood in the lane.</summary>
        public void Spawn(RoadEdge lane, float progress)
        {
            Net ??= lane.Road?.Net ?? LaneNet.Shared;
            if (lane.Road == null) Net.Adopt(lane);
            Leave();
            Road = lane.Road;
            Heading = lane.Heading;
            S = lane.RoadS(progress);
            D = lane.Offset;
            _laneD = D;
            SetLane(lane);
            Speed = Mathf.Min(Cruise(), Road.SpeedLimit) * 0.5f;
            Parked = false;
            // Rejoining traffic must not retain a halt from an earlier forecourt/kerb stop.
            _halted = false;
            _haltWhenClear = false;
            _occ = NewOccupant(Road);
            _lastPlaced = false;
            Place(0f);
            UpdateOccupant();
        }

        /// <summary>Onto the road nearest this point, facing this way - stood where
        /// it is (parked if it stands at a kerb). False: no road there; the car is
        /// on open ground and drives straight lines (CrewCar's free mode).</summary>
        public bool PlaceAt(Vector3 pos, Vector3 fwd)
        {
            Leave();
            _pos = pos;
            fwd.y = 0f;
            _fwd = fwd.sqrMagnitude > 1e-6f ? fwd.normalized : Vector3.forward;
            if (Net == null) return false;
            var road = Net.Locate(pos, out float s, out float d, within: 3f);
            if (road == null) return false;
            Road = road;
            S = Mathf.Clamp(s, 0f, road.Length);
            D = d;
            Heading = Vector3.Dot(_fwd, road.Axis) >= 0f ? 1 : -1;
            var lane = road.LaneFor(Heading, d) ?? road.LaneFor(-Heading, d);
            if (lane != null && lane.Heading != Heading) Heading = lane.Heading;
            _laneD = lane != null ? lane.Offset : d;
            SetLane(lane);
            Speed = 0f;
            Parked = lane == null || Mathf.Abs(d) > Mathf.Abs(lane.Offset) + 0.9f;
            _occ = NewOccupant(Road);
            _lastPlaced = false;
            Place(0f);
            UpdateOccupant();
            return true;
        }

        /// <summary>Off the network altogether (a patrol car swinging into its stall).
        /// The owner must not Tick it until the next Spawn/PlaceAt.</summary>
        public void Despawn() => Leave();

        /// <summary>Removed for good - the body destroyed, every claim released. The
        /// owner's list may still hold the reference; Tick refuses it from here on.</summary>
        public bool Gone { get; private set; }

        /// <summary>May this vehicle be CLEARED OFF THE STREET once it has stood dead
        /// still long enough? The release backstop: whatever logic wedged it - a
        /// derelict, a silent stop nothing ever undoes, a pair the belt cannot part -
        /// a dead thing must not hold a quarter for the rest of a session. Opt-IN -
        /// nothing anybody owns or the scene means (a crew's motor on the books, a
        /// bombed wreck, the law, a machine down in a drive-by) ever vanishes; plain
        /// street traffic does.</summary>
        protected virtual bool VanishesWhenStuck => false;

        /// <summary>Seconds of unbroken standstill before the backstop clears the car.
        /// Longer than any wait a car makes on purpose - a red with a queue discharging
        /// through it, the patience before a turn - so every honest recovery gets its
        /// chance first; the flag-watching this began with missed the very jam it was
        /// written for (the derelict flag clears itself the tick after the box is given
        /// up, while the car goes on standing).</summary>
        const float VanishAfterStill = 60f;

        float _stoodStillFor;

        /// <summary>The backstop itself: off the road, off the street's lists, and the
        /// body destroyed. Idempotent; safe against a later Despawn from the owner.</summary>
        public void Vanish()
        {
            if (Gone) return;
            Gone = true;
            Registered.Remove(this);
            if (DriveTrace.On)
                DriveTrace.Event("man", "car " + Id, "derelict cleared off the street", ManFields());
            Leave();
            StreetTraffic.Users.Remove(this);
            if (Tf != null) Object.Destroy(Tf.gameObject);
        }

        void Leave()
        {
            Deadlock.Cancel(detach: true);
            _parkPlanReady = _parkReturnApproach = _parkingRetreat = _parkReservationBlocked = false;
            _parkingRetryAt = 0f;
            if (_occ != null) { _occ.Road.Occupants.Remove(_occ); _occ = null; }
            if (_occNext != null) { _occNext.Road.Occupants.Remove(_occNext); _occNext = null; }
            LeaveBox();
            SetLane(null);
            Road = null;
            Via = null;
            _tailVia = null;
            _next = null;
            _via = null;
            _committed = false;
            _man = Manoeuvre.None;
            _sLen = 0f;
            ClearClaim();
        }

        void SetLane(RoadEdge lane)
        {
            if (Lane == lane) return;
            Lane?.Cars.Remove(this);
            Lane = lane;
            Lane?.Cars.Add(this);
        }

        RoadOccupant NewOccupant(Carriageway road)
        {
            var o = new RoadOccupant { Who = this, Car = this, Road = road, Priority = Profile.Priority };
            road.Occupants.Add(o);
            return o;
        }

        void LeaveBox()
        {
            if (_inNode != null && _nodeOf != null) _nodeOf.Inside.Remove(_inNode);
            _inNode = null;
            _nodeOf = null;
            _boxLeft = false;
        }

        /// <summary>Release the exit-lane reservation when a crossing completes or
        /// is abandoned, so a cancelled route cannot hold up that lane.</summary>
        void DropNext()
        {
            if (_occNext == null) return;
            _occNext.Road.Occupants.Remove(_occNext);
            _occNext = null;
        }

        /// <summary>Road-class cruise scaled by vehicle performance, then capped
        /// by the speed limit for drivers who obey it.</summary>
        float Cruise()
        {
            // ... and while he is IN a junction he is on no road at all, so the road he
            // is joining answers for it. Without that a car crossing the seam where a
            // motorway hands one carriageway to the next is told, for those two metres,
            // that he is on a high street - and stands on the brakes at every one of
            // them, at fifty miles an hour, with the traffic behind him doing the same.
            var road = Road ?? Via?.To?.Road ?? Via?.From?.Road;
            float own = road != null ? Profile.CruiseOn(road.Class) * Machine.Top : TopSpeed;
            return Profile.ObeysLimit && road != null ? Mathf.Min(own, road.SpeedLimit) : own;
        }

        /// <summary>The most a bend of this radius may be taken at: v² = a·R, with the
        /// profile's own lateral acceleration - and a little in hand, because the line
        /// the car follows is not exactly the line the road was drawn on.</summary>
        float BendSpeed(float radius)
        {
            if (float.IsInfinity(radius) || radius > 2000f) return float.MaxValue;
            return Mathf.Sqrt(Mathf.Max(0.5f, LateralG * 0.8f) * Mathf.Max(4f, radius));
        }

        protected static float Allowed(float endSpeed, float dist, float brake)
            => Mathf.Sqrt(endSpeed * endSpeed + 2f * brake * Mathf.Max(0f, dist));

        /// <summary>The speed from which the profile's brake stops in this distance, or
        /// slows to endSpeed: the one curve every stop here is made on.</summary>
        protected float Allowed(float endSpeed, float dist) => Allowed(endSpeed, dist, Brake);

        // A short street beyond this connector may already end at a blocked box.
        // Carry enough stopping distance out of the current turn, including when
        // an emergency response ends and the driver resumes ordinary braking.
        float ExitStopSpeed(float remainingConnector)
        {
            var nextNode = _next?.To;
            if (nextNode == null || nextNode.Seam) return float.MaxValue;
            float room = remainingConnector + _next.Length - HalfLen - nextNode.StopSetback;
            float ordinaryBrake = Mathf.Min(Brake, DriverProfile.Traffic.Brake * Machine.Grip);
            return Allowed(0f, room, ordinaryBrake);
        }

        /// <summary>The speed to hold behind something <paramref name="gap"/> metres
        /// ahead going <paramref name="vLead"/> our way: brake to its pace with the
        /// standing gap kept, and under the standing gap slower than it, to open the
        /// gap again - never merely its speed with no room.</summary>
        protected float Follow(float vLead, float gap, float leadSlowing = 0f)
        {
            vLead = Mathf.Max(0f, vLead);
            float room = gap - Profile.FollowGap;
            if (room <= 0f) return Mathf.Max(0f, vLead - (0.5f - room) * 2.5f);
            // What must be stopped short of is not where he IS but where he can COME TO
            // REST - while he is rolling, his own braking distance further on, which is
            // the ordinary give and take of a queue and the same arithmetic as before.
            // The moment he STANDS ON THE BRAKES that room collapses, and a follower who
            // is still only keeping his pace is inside him before he can do anything
            // about it: the barrier must move back the same instant his does, so we brake
            // WITH him instead of half a second after him. (A motorcycle five metres
            // behind a car that stopped dead at a junction line needed ninety metres a
            // second squared to stay off it; the belt refused the step.)
            float b = Mathf.Max(1f, leadSlowing > 0.1f ? leadSlowing : Brake);
            float his = vLead * vLead * 0.5f / b;
            float v = Allowed(0f, room + his);
            float byTime = vLead + room / Mathf.Max(0.2f, Profile.TimeGap);
            return Mathf.Min(v, Mathf.Max(vLead * 0.8f, byTime));
        }

        /// <summary>We mean to come to rest here (road-s of the centre) - the nearest
        /// such place this frame wins, and it is what everybody behind us brakes for.</summary>
        void MeanToStop(float s)
        {
            if (float.IsNaN(_stopAt)) _stopAt = s;
            else _stopAt = Heading > 0 ? Mathf.Min(_stopAt, s) : Mathf.Max(_stopAt, s);
        }

        // ------------------------------------------------------------------ orders

        /// <summary>Stop at this point on the road: pulled in at the kerb on the side
        /// the point lies (<paramref name="park"/>), or in the lane (a cruiser at a
        /// scene). Off the road the nearest road is meant. Routes there; turns round
        /// in the road when the profile lets it and the spot is behind or across.</summary>
        /// <param name="wantHeading">Which way along the goal's road the caller means to
        /// be travelling when it gets there (+1/-1), or 0 to let the kerb decide. It has
        /// to be askable: a DRIVE-BY picks its direction from where the mark is (the pass
        /// runs toward him), and this used to re-derive the heading from which side of the
        /// street the goal point sat on. When the two disagreed the goal came out BEHIND
        /// the car, "past the mark on the move" fired on the first frame, and the pass was
        /// over a tenth of a second after it was ordered - ridden, logged, and with the
        /// guns never out.</param>
        public bool GoTo(Vector3 point, bool park, float standOff = 0f, bool stopAtGoal = true, int wantHeading = 0)
        {
            Deadlock.Cancel();
            if (Net == null) return false;
            _parkTrying = 0f;
            ClearParkingFailure();
            _failedKerbSpots.Clear();
            _parkPlanReady = false;
            _parkTrafficCheck = 0f;
            _parkingRetreat = false;
            _parkReturnApproach = false;
            _halted = false;
            _freeGoal = null;
            var road = Net.Locate(point, out float s, out float d, within: 12f);
            if (road == null)
            {
                var lane = Net.NearestLane(point, out float prog, 12f);
                if (lane == null) return false;
                road = lane.Road;
                s = lane.RoadS(prog);
                d = lane.Offset;
            }
            int heading = wantHeading != 0 ? wantHeading : (d >= 0f ? 1 : -1);   // the kerb on that side is that way's
            if (road.LaneFor(heading, d) == null) heading = -heading;
            if (standOff > 0f) s -= heading * standOff;
            s = Mathf.Clamp(s, 6f, road.Length - 6f);
            float kerb = road.KerbD(heading, HalfWide);
            var goalLane = road.LaneFor(heading, d);
            if (goalLane == null) return false;
            _hasGoal = true;
            _goalRoad = road;
            _goalS = s;
            _parkRequestedS = s;
            _goalHeading = heading;
            _goalD = park ? kerb : goalLane.Offset;
            _goalPark = park;
            _goalStop = stopAtGoal || park;
            _goalLane = goalLane;
            _turnBackFor = 0f;
            _goalUTurns = 0;
            if (park && !FindParking()) { FailParking(); return true; }
            if (Parked)
            {
                if (park && _parkEntryLen == 0f && ParkingAligned(_goalD)) Parked = false;
                else PullOut();
            }
            Replan(newOrder: true);
            return true;
        }

        /// <summary>Seconds the belt has refused our step without a break - i.e. how long
        /// a body has been standing in ours.
        ///
        /// It matters because NOTHING SEPARATES TWO WEDGED BODIES FROM THE INSIDE. The
        /// belt eases a wedged car out a few centimetres a frame, but the easing is
        /// applied to the drawn position only: the next frame's pose is computed from the
        /// connector (or the lane) all over again, which puts the body straight back
        /// where it was. So a pair that has met stays met until one of them GIVES UP THE
        /// ROAD IT IS ON - and until this, only a car that had deliberately given way
        /// ever did (TickNode's _gaveWay). A wedge nobody yielded into simply stood, and
        /// the queue behind it stood with it: a motorcycle and a pickup held a junction
        /// of the demo for as long as anybody watched, and one run of the lab counted
        /// 1827 refused steps between two cars in a box.</summary>
        float _beltFor;

        /// <summary>WHEN the belt last refused a step of ours - which is not the same
        /// question as <see cref="_beltFor"/>, the length of the UNBROKEN run of
        /// refusals, and the difference is why a wedged pair used to stand for the whole
        /// scene. The shove buys one of them a clear frame every couple of seconds; that
        /// frame zeroes the run, and every recovery hung off the run is disarmed with it.
        /// Measured in the jam that finally showed it: the run never passed 2.5 s and the
        /// box's own clock never passed 1.0 s of the 1.5 s it needed, for eighty seconds
        /// on the trot. A stamp does not care about the odd frame that gets through.</summary>
        float _beltAt = -999f;

        /// <summary>Seconds this car has been WEDGED - refused recently and going
        /// nowhere - as against refused every single frame.</summary>
        float _wedgedFor;

        /// <summary>How long a refusal is remembered for. Longer than the gap the shove
        /// buys - a single clear frame, so 0.3 s bridges it even at ten frames a second
        /// - and shorter than the once-every-half-second touch of a queue compressing
        /// through a box, which at the old full second read as one unbroken wedge and
        /// had ordinary queued cars snapped out of junctions they were driving through.</summary>
        const float BeltMemory = 0.3f;

        /// <summary>Metres already shoved sideways to come apart from a body we met - the
        /// budget, so the shove is a nudge and never a crab across the street.</summary>
        float _shoved;

        /// <summary>Move the road user to where the WORLD has put it, without driving it
        /// there.
        ///
        /// One caller, and it is the reason this exists: a motorcycle on its side slides
        /// twenty metres down the road under its own class (BikeSpill), which owns the
        /// transform outright while it does. Everything the street knows about where a
        /// vehicle IS comes from this position and not from the transform - the belt, the
        /// queueing, the parking search - so a wreck that slid and never said so leaves
        /// the traffic refusing a spot with nothing standing on it and driving through
        /// the one that has a machine lying in it. The height is not the caller's: a
        /// road user's y is the road's.</summary>
        public void Slid(Vector3 worldPos)
        {
            _pos = new Vector3(worldPos.x, _pos.y, worldPos.z);

            // AND THE ROAD FRAME WITH IT. A vehicle is in this project TWICE: as a world
            // position, which is what the belt reads (RoadPosition), and as (s, d) on a
            // carriageway, which is what every question about LANES reads - the parking
            // search, the queueing, the room to pull out. They are refreshed in different
            // places, so a fix to one looks right in any test that watches bodies meet
            // and says nothing at all in a test that watches traffic plan.
            //
            // Left out, the band this wreck claims stays where it fell while the wreck
            // itself slides twenty metres on: the street plans round empty road at the
            // one end and drives through the machine at the other. Its published speed
            // stays whatever it was doing when it lost the road, too, so the queue behind
            // it waits for a phantom to move off instead of going round a dead thing.
            if (Road == null) return;
            Road.Project(_pos, out float s, out float d);
            S = Mathf.Clamp(s, 0f, Road.Length);
            D = d;
            UpdateOccupant();
        }

        /// <summary>Publish both parts of a pose driven outside the lane graph.</summary>
        public void Slid(Vector3 worldPos, Vector3 worldForward)
        {
            worldForward.y = 0f;
            if (worldForward.sqrMagnitude > 1e-6f) _fwd = worldForward.normalized;
            Slid(worldPos);
        }

        /// <summary>It is not going anywhere again - a wreck, and the street is to plan
        /// round it rather than queue behind it. The same standing-down the belt does to
        /// a car wedged with no answers left, said by whatever knows the vehicle is
        /// finished (a motorcycle on its side: CrewBike.GoDown).</summary>
        protected void StandDown()
        {
            if (Derelict) return;
            Derelict = true;
            _parkPlanReady = _hasGoal = false;
            LeaveBox();
            DropNext();
        }

        /// <summary>Nothing more to do: the goal dropped, the car drives on as the
        /// traffic does (a patrol car released from a call).</summary>
        public void Stop()
        {
            Deadlock.Cancel();
            ClearParkingFailure();
            _hasGoal = false;
            Route = null;
            _turnFirst = false;
            if (_man != Manoeuvre.UTurn) { _man = Manoeuvre.None; ClearClaim(); }
        }

        /// <summary>Stop where it stands and stay stopped - gently, or both feet on
        /// the brake - until the next order. Stood in the lane it is in everyone's
        /// way, and they go round it; that is the caller's choice.</summary>
        bool _haltWhenClear;
        bool _keepGoalWhenHaltClear;

        public void Halt(bool hard)
        {
            Deadlock.Cancel();
            // Never dead in the middle of a junction. The box is everybody else's road,
            // and a car left standing across one queues a whole quarter behind it - the
            // lab found six cars nose to tail for four minutes behind one whose driver
            // had been shot. Told to stop while crossing, the car finishes the crossing
            // (a few metres) and stops on the far side.
            if (Via != null)
            {
                _haltWhenClear = true;
                _keepGoalWhenHaltClear = false;
                _haltHard = hard;
                _hasGoal = false;
                Route = null;
                _turnFirst = false;
                _freeGoal = null;
                return;
            }

            _haltWhenClear = false;
            _keepGoalWhenHaltClear = false;
            _hasGoal = false;
            Route = null;
            _turnFirst = false;
            _freeGoal = null;
            _halted = true;
            _haltHard = hard;
            if (_man != Manoeuvre.UTurn && _man != Manoeuvre.Reverse) { _man = Manoeuvre.None; ClearClaim(); }
        }

        public bool Halted => _halted;

        /// <summary>Is the car where it was sent, stood still?</summary>
        public bool AtGoal => !ParkingFailed && !_hasGoal && Mathf.Abs(Speed) < 0.05f;
        public bool HasGoal => _hasGoal;

        /// <summary>Actually tucked against a kerb, rather than merely carrying the
        /// legacy <see cref="Parked"/> flag after a parking goal stopped in its lane.
        /// A car held just short by the vehicle occupying its chosen slot is still at
        /// the kerb; a lane centre is several metres away and cannot pass this test.</summary>
        public bool ParkedAtKerb => Parked && Road != null &&
            Mathf.Abs(D - Road.KerbDOnSide(D, HalfWide)) <= KerbParkReach;

        /// <summary>The goal reached and the car stopped: for a subclass to hear.</summary>
        protected virtual void OnArrived() { }

        // The route to the goal from wherever the car is now.
        float _retry;
        float _parkTrying;   // seconds spent looking for somewhere to pull in
        float _heldInBox;    // seconds held inside a junction by something stood beyond it

        float _turnBackFor;     // seconds spent looking for the turn-round on this road

        bool RequiredTurnHere() => RequiresInRoadTurn && !NoTurnBack && _hasGoal &&
            Road != null && Road == _goalRoad && Road.TwoWay &&
            Profile.UTurnsInRoad && Road.MedianHalf <= 0f;

        /// <summary>Turns in the road already spent on THIS goal. A parking goal is
        /// re-chosen on the approach, and a re-pick that lands behind the car asks for
        /// a turn whose completion puts the next re-pick behind the car again - a crew
        /// car was driven round that circle for ever. Two turns is one honest overshoot
        /// and one correction; the third want takes the long way round instead
        /// (_turnBackFor is pinned at the giving-up mark for the goal's whole life).</summary>
        int _goalUTurns;
        const int MaxGoalUTurns = 2;

        /// <summary>Seconds waiting for a safe U-turn gap before taking the fallback
        /// route. Parking and pull-out manoeuvres do not spend this patience.</summary>
        public static float TurnBackPatience = 5f;

        /// <summary>The driver means to turn round HERE before he goes anywhere - the
        /// mark is behind him and the way round the block is the long way. TickRoad does
        /// the turning (the same block that turns a car round on its goal's own road);
        /// this only says that it should.</summary>
        bool _turnFirst;
        bool _replanOnExit;

        /// <summary>U-turn cost and hysteresis, in the route table's street-distance
        /// units. Turning back must be materially cheaper than continuing.</summary>
        public static float UTurnCost = 25f;

        void Replan(bool newOrder = false)
        {
            if (DriveTrace.On) DriveTrace.Event("man", "car " + Id, "replan", ManFields());
            Route = null;
            RouteShift?.Clear();
            _turnFirst = false;
            bool refresh = newOrder || _replanOnExit;
            if (refresh) _previewBuilt = false;
            // Preserve the committed crossing, but show the new order immediately.
            _replanOnExit = _hasGoal && (Via != null || _committed);
            if (_replanOnExit)
            {
                RouteShift ??= new Dictionary<RoadEdge, RoadEdge>();
                Route = LaneNet.RouteToward(Net.Edges, _goalLane, out _, RouteShift);
                return;
            }
            // Retrying the same local turn keeps its fallback connector and preview.
            if (!refresh && PlansTurnRoundToGoal()) return;
            _next = null;
            _via = null;
            DropNext();
            if (!_hasGoal || Road == null) return;
            if (Road == _goalRoad && Heading == _goalHeading && (_goalS - S) * Heading > -3f) return; // straight down this road
            if (PlansTurnRoundToGoal()) return;
            RouteShift ??= new Dictionary<RoadEdge, RoadEdge>();
            Route = LaneNet.RouteToward(Net.Edges, _goalLane, out var dist, RouteShift);

            // The lane graph has no mid-street U-turn edge. Compare both directions
            // from the car's current station and let TickRoad find a safe gap.
            if (dist != null && !NoTurnBack && Road.TwoWay && Profile.UTurnsInRoad && Road.MedianHalf <= 0f &&
                _turnBackFor < TurnBackPatience && _man != Manoeuvre.UTurn)
            {
                // his own lane and the one he would come out in - mirrored across the
                // crown, which is where the arc puts him (TickArc)
                var cur = Lane ?? Road.LaneFor(Heading, D);
                var opp = Road.LaneFor(-Heading, -D);
                if (cur != null && opp != null && cur != opp)
                {
                    // Subtract progress already travelled in each direction.
                    bool ahead = dist.TryGetValue(cur, out float dAhead);
                    bool back = dist.TryGetValue(opp, out float dBack);
                    if (ahead) dAhead -= cur.Progress(S);
                    if (back) dBack -= opp.Progress(S);
                    if (back && (!ahead || dBack + UTurnCost < dAhead))
                    {
                        // Keep the fallback table: a refused turn must not make the
                        // driver wander randomly while still carrying a live goal.
                        _turnFirst = true;
                        if (DriveTrace.On)
                        {
                            var sb = DriveTrace.Take();
                            DriveTrace.Str(sb, "who", "car " + Id);
                            DriveTrace.Num(sb, "ahead", ahead ? dAhead : -1f, "F0");
                            DriveTrace.Num(sb, "back", dBack, "F0");
                            DriveTrace.Row("turnfirst", sb.ToString());
                        }
                    }
                }
            }

            _next = null; // think the next turn over again
            _committed = false;
        }

        // ------------------------------------------------------------------ frame

        public void Tick(float dt)
        {
            if (dt <= 0f) return;
#if UNITY_5_3_OR_NEWER
            if (Application.isPlaying)
            {
                RoadCarSimulation.Queue(this, dt);
                return;
            }
#endif
            // The campaign can run at 16x. Feeding an entire accelerated frame into
            // steering and junction entry lets a car cross several decision points
            // before its next collision/occupancy check. Consume all elapsed time in
            // bounded driving steps, including when an editor frame stalls.
            int steps = Mathf.Max(1, Mathf.CeilToInt(dt / (1f / 30f)));
            float step = dt / steps;
            for (int i = 0; i < steps; i++) TickStep(step);
        }

        internal virtual void TickStep(float dt)
        {
            if (Gone) return;      // cleared off the street; the owner's list may still hold us
            if (Wrecked) return;   // a blown car drives nowhere and holds nothing
            Deadlock.Tick();
            if (Deadlock.Active)
            {
                _beltFor = _wedgedFor = _boxStuck = _backOutFor = _holdFor = _giveUntil = 0f;
                if (Deadlock.Waiting)
                { Speed = _want = 0f; Why = "Letting a blocked vehicle clear"; UpdateOccupant(); OnPlaced(dt, 0f, 0f); return; }
                Speed = Mathf.Min(Speed, RoadDeadlock.Pace);
            }
            if (!Deadlock.Active && !Deadlock.Mutual) { WatchTrafficRecovery(dt); WatchDerelict(dt); }
            if (!OnRoad) { TickFree(dt); return; }
            if (Via != null) TickNode(dt);
            else TickRoad(dt);
        }

        bool _lastPlaced;
        float _derelictFor;

        /// <summary>A car that has stopped FOR GOOD with its nose in a junction - the
        /// driver shot, the crew out on the pavement, the plan torn up - still holds the
        /// box against everyone who would cross it, and a whole quarter queues behind a
        /// body nobody is coming back for. (A run of the lab found six cars nose to tail
        /// for four minutes behind one abandoned car.) A few seconds of that and the
        /// claim is given up: the wreck is still there to be driven round like any other
        /// stopped car, but the junction is a junction again.
        ///
        /// Only a car that was TOLD to stop, or parked - never one merely waiting its
        /// turn in a queue, which still means to go and must keep its place.</summary>
        void WatchDerelict(float dt)
        {
            bool derelict = Mathf.Abs(Speed) < 0.15f && _halted && !Parked;
            // A prolonged stationary junction occupant must eventually release its claim;
            // a brief crossing wait is not abandonment.
            bool wedged = Mathf.Abs(Speed) < 0.15f && !Parked && _inNode != null;
            // Only opt-in ambient traffic may vanish after a prolonged standstill.
            // Owned vehicles retain their normal recovery and lifecycle rules.
            if (VanishesWhenStuck && !Parked && Mathf.Abs(Speed) < 0.15f)
            {
                _stoodStillFor += dt;
                if (_stoodStillFor > VanishAfterStill) { Vanish(); return; }
            }
            else _stoodStillFor = 0f;
            if (!derelict && !wedged) { _derelictFor = 0f; Derelict = false; return; }
            _derelictFor += dt;
            // a stopped-for-good car gives up quickly; a wedged one is given longer, so a
            // car merely easing through a busy box is never mistaken for a deadlock
            if (_derelictFor < (derelict ? 6f : 10f)) return;
            if (Derelict) return;
            Derelict = true;
            if (Via == null) { LeaveBox(); DropNext(); }
            if (DriveTrace.On) DriveTrace.Event("man", "car " + Id,
                Via != null ? "stopped in the box; physical occupancy retained" : "gave up the box it had stopped in", ManFields());
        }

        void TickRoad(float dt)
        {
            // Every cancelled admission returns here, not just a clean box exit.
            if (_replanOnExit && !_committed) Replan();
            var road = Road;
            _stopAt = float.NaN;
            UpdateOccupant();
            TickBoxExit();
            // Lane keeping must not pull an active parking approach off its kerb.
            bool parkingHere = _hasGoal && _goalPark && Road == _goalRoad && Heading == _goalHeading &&
                ((_parkPlanReady && _parkEntryLen == 0f) ||
                 ((_goalS - S) * Heading > -6f && (_goalS - S) * Heading < 40f));
            if (_man == Manoeuvre.None && !Sliding && !Parked && !_halted && Lane != null &&
                Mathf.Abs(D - _laneD) > 0.5f && !parkingHere && RoadCarSimulation.Now >= _yieldUntil)
            {
                // A stopped car at the kerb needs a checked exit, including room
                // behind and the neighbour in front; a bare lane correction cannot.
                if (Mathf.Abs(D - _laneD) > 1.5f && Mathf.Abs(Speed) < 0.5f) PullOut();
                else Slide(_laneD, SlideLength(Mathf.Abs(D - _laneD), Mathf.Abs(Speed)));
            }
            // Not crossing anything (the plan dropped, or stood at a kerb): whatever was
            // held on the far side of a junction goes back. Here, where every way of
            // giving up a crossing ends, rather than at each of them.
            if (!_committed || Parked) DropNext();
            if (Parked)
            {
                Speed = 0f;
                Place(dt);
                return;
            }

            // A failed exit may have backed into the original slot. Re-enter the
            // checked pull-out path before approaching the selected lane-to-kerb entry.
            if (_hasGoal && _goalPark && _parkPlanReady && _parkEntryLen > 0f &&
                _man == Manoeuvre.None && !Sliding && !_halted &&
                Road == _goalRoad && Heading == _goalHeading &&
                (_goalS - S) * Heading > .5f && Mathf.Abs(D - _parkEntryD) > .5f)
                PullOut();

            // One local parking budget includes every attempted entry, reverse and
            // pass. Retrying a manoeuvre cannot renew it or move the request elsewhere.
            // Time spent driving across town is not time spent trying to park.
            if (_hasGoal && _goalPark && road == _goalRoad &&
                Mathf.Abs(S - _parkRequestedS) < ParkingSearchReach + 15f)
            {
                _parkTrying += dt;
                if (_parkTrying > ParkingAttemptLimit)
                { ParkingReason = "Parking search timed out"; FailParking(); }
                else if (!_parkPlanReady && _man == Manoeuvre.None && !Sliding &&
                    RoadCarSimulation.Now >= _parkingRetryAt && !FindParking(aheadOnly: true))
                    FailParking();
            }

            // out of the lane ahead - the junction (or the end of the road)
            var node = road.NodeAhead(Heading);
            bool requiredTurn = RequiredTurnHere();
            if (requiredTurn && !_committed)
            {
                // The pending U-turn is the way on. A connector selected earlier in the
                // pass must not survive into this wait: once it does, the junction owns
                // the car and the lane graph turns a refused sweep into a lap of the
                // block. Keep the far side unclaimed until the in-road turn is complete.
                _next = null;
                _via = null;
                DropNext();
            }
            if (_next == null && node != null && _man != Manoeuvre.UTurn && !requiredTurn)
                PlanNext(node);

            // Keep a junction claim synchronized with the chosen connector.
            // A stale claim admits conflicting movements against the wrong route.
            if (_inNode != null && Via == null)
            {
                // turning round in the road ends the crossing, tail or no tail: the car
                // is not going that way any more, and its body is the road's business now
                if (Parked || _man == Manoeuvre.UTurn) LeaveBox();
                else if (!_boxLeft)
                {
                    if (_nodeOf != node) LeaveBox();
                    else if (_via != null) _inNode.Via = _via;
                }
            }

            float noseS = S + Heading * HalfLen;
            float tailS = S - Heading * HalfLen;
            float endS = road.EndS(Heading);
            float toEnd = (endS - noseS) * Heading;             // nose to the box edge

            // ---- the throttle
            float v = Cruise();
            // the bend under him, and the one he is about to be in: a motorway corner is
            // taken at what it is signed at, not at what the straight before it allowed
            if (road.Path != null)
            {
                float look = Mathf.Max(20f, Mathf.Abs(Speed) * 2.5f);
                float tightest = Mathf.Min(road.RadiusAt(S), road.RadiusAt(S + Heading * look));
                v = Mathf.Min(v, BendSpeed(tightest));
            }
            // BOLTING FROM GUNFIRE lifts the CEILING, and nothing else. A frightened
            // driver drives faster than he otherwise would - past the limit, over the
            // cruise - but he does not drive into the back of the car in front, and this
            // used to: the lift was put on at the END of the throttle, on top of the room
            // he was keeping to the man ahead, so a bolting car closed thirteen metres a
            // second onto a queue standing at a red and the belt was the only thing
            // between them. Put on here, everything ahead still clamps it.
            if (!Fearless && _nerve.Bolting && !_nerve.Approaching) v *= 1.3f;
            if (Sliding) v = Mathf.Min(v, LateralCap(_sLen, Mathf.Abs(_dTo - _dFrom)));
            bool hard = false;
            bool returningToParking = ReturnToParkingEntry();
            if (returningToParking) v = 0f;

            // Shared U-turn intent: a local opposite kerb, or a cheaper route back
            // toward another road. Failure near the junction selects the fallback.
            if (_hasGoal && !returningToParking && _man != Manoeuvre.UTurn && !NoTurnBack &&
                (_goalUTurns < MaxGoalUTurns || requiredTurn) &&
                (_turnFirst || (road == _goalRoad && Heading != _goalHeading && Route == null)) &&
                road.TwoWay && Profile.UTurnsInRoad && road.MedianHalf <= 0f)
            {
                // A stationary queue after a corner must not freeze the fallback clock.
                if (_man == Manoeuvre.None && !Parked) _turnBackFor += dt;
                // THE THROTTLE COMES DOWN FOR A TURN THE ROAD HAS ROOM FOR, and stays
                // down for as long as the driver is still asking (his patience, five
                // seconds). Held to "the arc is free THIS INSTANT" it never came down at
                // all on a street with anything moving on it: one refusal - a car four
                // seconds away in the other lane - and the driver was back at fourteen
                // metres a second, at which speed the turn is refused for being too fast,
                // for the rest of the street. Round the block he went, on what the player
                // rightly called an empty road. Slowing for a turn the road has NO room
                // for is still the rolling roadblock the old rule was written against,
                // and that is still refused.
                if (UTurnAvailable() ||
                    ((requiredTurn || _turnBackFor < TurnBackPatience) &&
                     UTurnAvailable(timing: false)))
                    v = Mathf.Min(v, UTurnApproachSpeed());
                if (requiredTurn)
                {
                    // A drive-by waits for a safe gap before the junction instead of
                    // crossing it and letting the route table turn the next block into
                    // part of the pass. Stop at the last point whose whole U-turn sweep
                    // still fits this carriageway; when the band clears, TryUTurn below
                    // releases it in the opposite lane.
                    float margin = UTurnRadius() + HalfLen + 3f;
                    var turnNode = road.NodeAhead(Heading);
                    if (turnNode != null) margin += turnNode.StopSetback;
                    float lastTurnS = road.EndS(Heading) - Heading * margin;
                    float toLastTurn = (lastTurnS - S) * Heading;
                    v = Mathf.Min(v, Allowed(0f, Mathf.Max(0f, toLastTurn)));
                }
                _retry -= dt;
                if (_retry <= 0f && _man == Manoeuvre.None)
                {
                    _retry = 0.3f;
                    // Out of road, or out of patience: the turn is given up ON THIS
                    // STREET and the long way round drawn instead. _turnBackFor is left
                    // at the giving-up mark so that Replan - which asks the same
                    // question - does not simply set the driver turning again.
                    if (TryUTurn()) _goalUTurns++;
                    else
                    {
                        if (DriveTrace.On) DriveTrace.Event("man", "car " + Id, "no turn: " + UTurnWhy, ManFields());
                        // Match SweepClear's actual arc footprint, not a guessed fixed
                        // distance. Otherwise an empty short road gives up immediately.
                        float arcRoom = UTurnRadius() + HalfLen + 2f;
                        var turnNode = road.NodeAhead(Heading);
                        bool noRoom = (road.EndS(Heading) - (S + Heading * arcRoom)) * Heading <
                                      (turnNode != null ? turnNode.StopSetback : 0f);
                        if (!requiredTurn && (noRoom || _turnBackFor > TurnBackPatience))
                        {
                            _turnBackFor = 99f;
                            Replan();
                        }
                    }
                }
            }
            // the reset is what the latch has to survive: a completed turn satisfies
            // the heading the same frame, and zeroing the patience here handed the next
            // overshoot a fresh five seconds - for ever
            else if (_goalUTurns < MaxGoalUTurns) _turnBackFor = 0f;

            // the goal on this road
            if (_hasGoal && !returningToParking && road == _goalRoad && Heading == _goalHeading && _man != Manoeuvre.UTurn &&
                (!_goalPark || _parkPlanReady && (_man == Manoeuvre.PullIn || (_man == Manoeuvre.None && !Sliding))))
            {
                if (_goalPark && _man == Manoeuvre.None && (_goalS - S) * Heading >= -3f)
                {
                    _spotCheck -= dt;
                    bool missedEntry = _parkPlanReady && _parkEntryLen > 0f &&
                        !((_goalS - S) * Heading <= .3f && ParkingAligned(_goalD)) &&
                        ((_parkEntryS - S) * Heading < -.1f || Mathf.Abs(D - _parkEntryD) > .01f);
                    if (!_parkPlanReady || missedEntry || _spotCheck <= 0f)
                    {
                        _spotCheck = .7f;
                        if (!_parkPlanReady || missedEntry || ParkingDestinationTaken(stationaryOnly: true) ||
                            !ParkingEntryClear(stationaryOnly: true))
                            if (!FindParking(aheadOnly: true)) FailParking();
                    }
                }
                float toGoal = (_goalS - S) * Heading;
                if (_hasGoal && toGoal < -3f)
                {
                    // overshot it: round, if the road lets us, else the long way. The
                    // throttle comes down first - a turn is refused above the speed its
                    // arc can carry, so charging at it only means never being allowed one.
                    bool mayTurn = Profile.UTurnsInRoad && road.TwoWay && road.MedianHalf <= 0f;
                    if (_goalUTurns < MaxGoalUTurns && mayTurn && UTurnAvailable())
                        v = Mathf.Min(v, UTurnApproachSpeed());
                    _retry -= dt;
                    if (_man == Manoeuvre.None && _retry <= 0f)
                    {
                        _retry = 0.5f;
                        if (_goalUTurns >= MaxGoalUTurns)
                        {
                            // two turns spent on this goal already: the third want is
                            // the circle, and the long way round is the answer to it
                            _turnBackFor = 99f;
                            if (Route == null) Replan();
                        }
                        // still braking for it: ask again in a moment rather than give up
                        // on the turn and send the car round the block
                        else if (!mayTurn || Mathf.Abs(Speed) <= UTurnApproachSpeed() + 1f)
                        {
                            if (TryUTurn()) _goalUTurns++;
                            else if (Route == null) Replan();
                        }
                    }
                }
                else if (_hasGoal)
                {
                    if (_goalStop) v = Mathf.Min(v, Allowed(0f, toGoal));
                    // The approach and the swing are one stored plan. Traffic can
                    // delay its start, but does not move the target every two seconds.
                    if (_hasGoal && _goalPark && _parkPlanReady && _man == Manoeuvre.None &&
                        !Sliding && toGoal > 0f && _parkEntryLen > 0f)
                    {
                        float toEntry = (_parkEntryS - S) * Heading;
                        float entrySpeed = LateralCap(_parkEntryLen, Mathf.Abs(_goalD - _parkEntryD));
                        v = Mathf.Min(v, Allowed(entrySpeed, Mathf.Max(0f, toEntry)));
                        float trafficLook = Mathf.Max(8f, Speed * Speed / (2f * Brake) + Mathf.Abs(Speed) * .3f + 2f);
                        float armReach = Mathf.Max(.6f, Mathf.Abs(Speed) * dt + .2f);
                        if (toEntry <= trafficLook)
                        {
                            _parkTrafficCheck -= dt;
                            if (_parkTrafficCheck <= 0f || (toEntry <= armReach && _parkTrafficClear))
                            {
                                _parkTrafficCheck = .2f;
                                _parkTrafficClear = ParkingEntryClear(stationaryOnly: false);
                            }
                            if (!_parkTrafficClear)
                            {
                                ParkingReason = "Waiting for parking approach";
                                // Brake before the fixed start, not after reaching it.
                                // A moving car gets time to clear the same planned arc.
                                v = Mathf.Min(v, Allowed(0f, Mathf.Max(0f, toEntry - .3f)));
                                MeanToStop(_parkEntryS - Heading * .3f);
                            }
                            else if (toEntry <= armReach)
                            {
                                ParkingReason = "Approaching parking";
                                BeginPullIn();
                                v = Mathf.Min(v, entrySpeed);
                            }
                        }
                    }
                    // Parking completes at the driven pose, after the axle has
                    // straightened. A blocked approach is not an arrival in the lane.
                    if (_hasGoal && !_goalStop && toGoal <= 0f)
                    {
                        // past the mark on the move: done, and the next order may come at once
                        _hasGoal = false;
                        Route = null;
                        OnArrived();
                    }
                    else if (_hasGoal && toGoal <= 0.25f && Mathf.Abs(Speed) < 0.3f &&
                        (!_goalPark || (!Sliding && ParkingAligned(_goalD) && ParkingCanComplete())))
                    {
                        Speed = 0f;
                        _hasGoal = false;
                        Route = null;
                        if (_goalPark) { Parked = true; _parkPlanReady = false; _man = Manoeuvre.None; _sLen = 0f; ClearClaim(); LeaveBox(); DropNext(); }
                        OnArrived();
                        UpdateOccupant();
                        Place(dt);
                        return;
                    }
                }
            }

            // the junction ahead
            bool heldByNode = false;
            if (node == null)
            {
                v = Mathf.Min(v, Allowed(0f, toEnd - 0.5f));
                MeanToStop(S + Heading * (toEnd - 0.5f));
                if (v < 0.5f && Mathf.Abs(Speed) < 0.5f) Why = "road ends, no node";
                if (toEnd < 1f && Mathf.Abs(Speed) < 0.1f && Profile.UTurnsInRoad && road.TwoWay && _man == Manoeuvre.None) TryUTurn();
            }
            else if (_man != Manoeuvre.UTurn)
            {
                float stopDist = toEnd - node.StopSetback;
                if (_next == null)
                {
                    v = Mathf.Min(v, Allowed(0f, stopDist));
                    MeanToStop(S + Heading * stopDist);
                    // SAY SO. This stop stood mute through three soaks: a lane the graph
                    // gave no way out of read as "want 0, why ''" for two hundred
                    // seconds and cost a live-probe session to name.
                    if (v < 0.5f && Mathf.Abs(Speed) < 0.5f) Why = "no way on from this lane";
                }
                else
                {
                    float turnV = _via != null && _via.UTurn ? Profile.UTurnSpeed
                        : _turn == Turn.Straight ? Mathf.Min(Cruise(), Profile.ObeysLimit ? _next.SpeedLimit : TopSpeed) : TurnSpeed(_via);
                    turnV = Mathf.Min(turnV, ExitStopSpeed(_via != null ? _via.Length : 0f));
                    v = Mathf.Min(v, Allowed(turnV, toEnd));
                    if (!_committed)
                    {
                        // the decision is made where the car can still stop: from there on
                        // it is in the box's list, and everybody plans round it
                        float commitAt = Mathf.Max(1.6f, Speed * Speed / (2f * Brake) + 1.0f);
                        bool may = CanEnter(node, stopDist);
                        if (!may)
                        {
                            v = Mathf.Min(v, Allowed(0f, stopDist));
                            MeanToStop(S + Heading * stopDist);
                            if (_heldAtLine <= 0f) _waitingLineAt = RoadCarSimulation.Now;
                            _heldAtLine += dt;
                            heldByNode = true;
                            // held on a full exit long enough: pick another way out of
                            // this junction (a wanderer only - a car on a route has
                            // somewhere to be and waits its turn)
                            if (_exitFull != null && Route == null)
                            {
                                _fullFor += dt;
                                if (_fullFor > 5f) { _fullFor = 0f; PlanNext(node); _exitFull = null; }
                            }
                            else _fullFor = 0f;
                            // late: both feet on the brake; past the point of no return even
                            // so - in we go, and on the list, so everybody plans round us
                            if (stopDist < Speed * Speed / (2f * Brake)) hard = true;
                            // Integrating one braking step leaves a small distance lag;
                            // that lag must not turn a refused crossing into permission.
                            if (stopDist < Speed * Speed / (2f * HardBrake) - Speed * dt - 0.3f && Speed > 1f) EnterBox(node);
                        }
                        else
                        {
                            _heldAtLine = 0f;
                            _exitFull = null;
                            _fullFor = 0f;
                            if (stopDist <= commitAt) EnterBox(node);
                        }
                    }
                }
            }

            bool reversingAhead = false;
            if (node != null && Lane != null)
                foreach (var other in Lane.Cars)
                    if (other != this &&
                        ((other.Via != null && other.Via.From == Lane && other._backOutFor > 0f &&
                          (toEnd < 20f || _reverseQueueYield)) ||
                         (other._reverseQueueYield && other.Road == Road &&
                          (other.S - S) * Heading > 0f && (other.S - S) * Heading < 15f)))
                    { reversingAhead = true; break; }
            if (reversingAhead && Speed <= .15f && _man == Manoeuvre.None && !Sliding)
            {
                _reverseQueueYield = true;
                _committed = false;
                LeaveBox();
                var remainingYield = road.Length;
                var roadRoom = Heading > 0 ? S : road.Length - S;
                remainingYield = Mathf.Min(remainingYield, Mathf.Max(0f, roadRoom - .1f));
                if (remainingYield <= .001f || !YieldBackToLine(node, node.StopSetback - remainingYield, dt))
                {
                    Speed = 0f; _want = 0f;
                    Why = "waiting for a car reversing out of the box";
                    UpdateOccupant();
                }
                return;
            }
            if (!reversingAhead) _reverseQueueYield = false;
            if (heldByNode && YieldBackToLine(node, toEnd, dt)) return;

            // through the box ahead: whoever is on our way across it, or just out of it
            if (node != null && _via != null && toEnd < 40f && _man != Manoeuvre.UTurn)
            {
                if (_committed && _occNext != null) RefreshNextOccupant(-(toEnd + _via.Length) - HalfLen);
                float vb = BoxFollow(node, v);
                if (vb < v) { v = vb; if (v < 0.5f) Why = "box: following ahead"; }
            }

            // whoever is ahead in the band we stand in or are moving into
            float d0 = BandLo(), d1 = BandHi();
            var leader = road.Ahead(_occ, Heading, noseS, tailS, d0, d1, out float gap,
                Deadlock.Active ? (_escapeLeaderFilter ??= BlocksEscapePath) :
                _man == Manoeuvre.PullIn || _man == Manoeuvre.PullOut || StraightKerbApproach
                    ? (_parkingLeaderFilter ??= BlocksParkingPath) : null);
            _leadId = leader == null ? -1 : leader.Car != null ? leader.Car.Id : -2;
            _leadGap = leader == null ? -1f : gap;
            float vLead = 0f;
            if (leader != null)
            {
                vLead = Mathf.Max(0f, leader.Vel * Heading);
                if (leader.Vel * Heading < -0.5f) vLead = 0f;  // coming at us
                // he is on us, in the band we are sliding INTO and not against our body: the
                // slide is off (somebody took the gap); the thing we are sliding away from is
                // no reason to stop alongside it
                bool inTarget = Sliding && leader.Overlaps(_dTo - HalfWide - SideAir, _dTo + HalfWide + SideAir);
                bool onBody = leader.BodyOverlaps(BodyLo() - SideAir, BodyHi() + SideAir);
                // He is beside the line we are leaving, not on the one we are taking:
                // we are going PAST him, not queueing behind him.
                bool slidingPast = Sliding && !inTarget && vLead < 0.5f &&
                                   (_man == Manoeuvre.Pass || (_man == Manoeuvre.PullOut && leader.Parked));
                if (gap < 0.5f && Sliding && inTarget && !onBody && _man != Manoeuvre.UTurn) AbortLateral();
                else if (slidingPast)
                {
                    // Keep room until the flank has cleared the body we are passing.
                    v = Mathf.Min(v, Allowed(0f, gap - 0.4f));
                }
                else v = Mathf.Min(v, Follow(vLead, gap, leader.Slowing));
                // AND WHERE HE MEANS TO STOP, if he means to. Braking for his bumper is
                // braking for a thing that is still moving away: at a red light he begins
                // his stop before we begin ours - he is nearer the line - and those three
                // tenths of a second are the whole gap between us (two cars nose to tail
                // at eleven metres a second, and the belt had to separate them). Braking
                // for the PLACE he is going to stand puts the whole queue on the brakes
                // at once, and it says nothing at all on an open road.
                // ...and not the place he means to stand either. A PARKED car means to
                // stand where it is, so the stopping-place brake - the one that puts a
                // whole queue on the brakes together at a light - reads a kerb as a
                // queue and holds us three metres short of a body we are sliding past.
                // That, and not the following gap, is what kept a crew car half out of
                // its slot with the swing already begun.
                if (!slidingPast && !float.IsNaN(leader.StopAt))
                {
                    float behind = leader.StopAt -
                                   Heading * (leader.Length * 0.5f + Profile.FollowGap + HalfLen);
                    float vq = Allowed(0f, (behind - S) * Heading);
                    if (vq < v) { v = vq; if (v < 0.5f) Why = DriveTrace.On ? "queue: behind car " + (leader.Car != null ? leader.Car.Id.ToString() : "?") : "queue"; }
                    MeanToStop(behind);
                }
                // something stood at the kerb or dead in the road that we mean to go round:
                // held back far enough that the swing out is still possible from here
                bool queue = leader.Car != null && leader.Car.InQueue && !Profile.PushesPastQueues;
                bool roundIt = leader.Parked || leader.Car == null || (Profile.Patience <= 1f && !queue);
                if (roundIt && !leader.Moving && !IsOurParkingSpot(leader) && !Sliding && _man == Manoeuvre.None)
                    v = Mathf.Min(v, Allowed(0f, gap - (leader.Parked ? KerbHoldBack : PassHoldBack)));
            }
            _blocker = leader;
            if (leader != null && v < .5f) Deadlock.BlockedBy(leader.Car);
            InQueue = heldByNode || (leader != null && leader.Car != null && leader.Car.InQueue && !leader.Moving);
            // the full reason is a trace string and nothing but the trace reads it (the
            // sim only ever tests Why for "red"/"yellow", which are set as constants
            // elsewhere), so it is built only when the trace is open - untraced play was
            // allocating one of these a frame for every car in a queue, the bulk of the
            // crowd's garbage and the ten-second GC hitch it fed
            if (leader != null && v < 0.5f)
                Why = DriveTrace.On
                    ? "behind " + (leader.Car != null ? "car " + leader.Car.Id : "static") + $" gap {gap:F1} band[{d0:F1},{d1:F1}] his[{leader.D0:F1},{leader.D1:F1}] s[{leader.S0:F1},{leader.S1:F1}] me s={S:F1}"
                    : "behind";

            // people in the road
            v = Mathf.Min(v, WalkersAhead(StreetTraffic.Walkers));
            v = Mathf.Min(v, BodiesAhead(dt));

            // gunfire
            if (!Fearless)
            {
                float cap = _nerve.Limit(_pos, _fwd, Brake, out hard);
                v = Mathf.Min(v, cap);
            }

            // giving way: stood aside, or dead still
            if (RoadCarSimulation.Now < _giveUntil) v = Mathf.Min(v, _man == Manoeuvre.Aside ? 2.5f : 0f);
            // waiting at the kerb for a gap to pull out into
            if (_man == Manoeuvre.PullOut && _pullOutWanted) v = 0f;
            // told to stop where it stands
            if (_halted)
            {
                v = 0f;
                if (_haltHard) hard = true;
                if (!ParkingFailed && Mathf.Abs(Speed) < 0.05f && Lane != null &&
                    ParkingAligned(Road.KerbDOnSide(D, HalfWide))) Parked = true;
            }
            // stood back from something, waiting for the way round to open
            if (_holdFor > 0f) { _holdFor -= dt; if (_man == Manoeuvre.None) v = 0f; }

            v = LimitTarget(Deadlock.Active ? Mathf.Min(v, RoadDeadlock.Pace) : v);

            // ---- which lane he means to be in, and the move over into it
            TickLaneChange(dt, toEnd);

            // ---- the driver's tactics: round what is stopped, the crown, the turn, the reverse
            Decide(dt, leader, gap, vLead, node, toEnd);
            if (Road == null) return; // the decision took us off the road (should not)

            // ---- move
            if (_man == Manoeuvre.Reverse)
            {
                float previousReverseS = S, previousReverseD = D;
                TickReverse(dt);
                Place(dt, previousReverseS, previousReverseD);
                return;
            }
            if (_man == Manoeuvre.UTurn) { TickArc(dt, v); return; }

            float rate = v < Speed ? (hard || v <= 0.01f ? HardBrake : Brake) : Accel;
            if (v < Speed && v > 0.01f && !hard) rate = Brake;
            _want = v; _wantHard = hard;
            Speed = Mathf.MoveTowards(Mathf.Max(0f, Speed), Mathf.Max(0f, v), rate * dt);
            float step = Speed * dt;
            if (_hasGoal && _goalPark && _parkPlanReady && !_parkTrafficClear && _parkTrafficCheck > 0f &&
                _parkEntryLen > 0f && road == _goalRoad && Heading == _goalHeading &&
                _man == Manoeuvre.None && !Sliding)
            {
                // A late-arriving obstruction must not let this frame carry us
                // beyond the entry and trigger another drive-forward/re-pick loop.
                float room = Mathf.Max(0f, (_parkEntryS - S) * Heading - .25f);
                if (step > room) { step = room; Speed = dt > 0f ? step / dt : 0f; }
            }
            float prevS = S, prevD = D;
            S += Heading * step;
            D = LateralAt(S);

            // over the end of the road into the box
            bool atEnd = (S - endS) * Heading >= -0.01f;
            if (atEnd && _next != null && _committed && node != null)
            {
                float overshoot = Mathf.Max(0f, (S - endS) * Heading);
                EnterNode(node, overshoot);
                Place(dt);
                return;
            }
            if (atEnd)
            {
                // the end with nowhere to go: stood at it
                S = endS - Heading * 0.01f;
                Speed = 0f;
            }
            Place(dt, prevS, prevD);
        }

        // A step that the belt (RoadSpace) may cut short: the car never enters another.
        bool _reverseQueueYield;
        float _placeViaBefore = float.NaN;
        float _placeArcBefore = float.NaN;
        void PlaceInNode(float dt, float previous)
        {
            _placeViaBefore = previous;
            try { Place(dt); }
            finally { _placeViaBefore = float.NaN; }
        }

        void Place(float dt, float prevS = float.NaN, float prevD = float.NaN)
        {
            var prevViaS = _placeViaBefore;
            Vector3 pos, fwd;
            Pose(out pos, out fwd);
            float steer = 0f;
            if (_lastPlaced && dt > 0f)
            {
                var to = pos;
                var moved = RoadSpace.Advance(this, _pos, to, fwd, HalfLen, HalfWide, out var hit);
                if (hit != null)
                {
                    Deadlock.BlockedBy(hit as RoadCar);
                    BeltHits++;
                    if (DriveTrace.On)
                    {
                        var sb = DriveTrace.Take();
                        DriveTrace.Int(sb, "id", Id);
                        DriveTrace.Str(sb, "tag", Tag);
                        DriveTrace.Str(sb, "hit", hit is RoadCar bc ? "car " + bc.Id : "static");
                        DriveTrace.Num(sb, "v", Speed);
                        DriveTrace.Str(sb, "why", Why);
                        DriveTrace.Vec(sb, "p", pos);
                        DriveTrace.Row("belt", sb.ToString());
                    }
                    // a write-only debug field (nothing reads it): built only when the
                    // trace is open, or every one of the thousands of belt hits a run
                    // sees was assembling this whole string for no reader
                    if (DriveTrace.On)
                        LastBeltHit = $"car {Id} {Describe()} v={Speed:F1} fwd={fwd} hit {(hit is RoadCar hc ? "car " + hc.Id + " " + hc.Describe() + " v=" + hc.Speed.ToString("F1") : "static at " + hit.RoadPosition + " fwd " + hit.RoadForward + " hl " + hit.HalfLength + " hw " + hit.HalfWidth)} at {pos} from {_pos} sliding={Sliding} slope={(Sliding ? LateralSlope(S) : 0f):F2}";
                    // stood where we were: the arithmetic let two bodies meet, the belt did not
                    if (!float.IsNaN(_placeArcBefore))
                    {
                        _arcAng = _placeArcBefore;
                        pos = _pos; fwd = _fwd;
                    }
                    else if (!float.IsNaN(prevViaS) && Via != null)
                    {
                        ViaS = prevViaS;
                        if (_inNode != null) _inNode.S = ViaS;
                        RefreshNextOccupant(ViaS - Via.Length);
                        pos = _pos;
                        fwd = _fwd;
                    }
                    else if (!float.IsNaN(prevS))
                    {
                        S = prevS; D = prevD;
                        // AND COME APART, a little. Standing still where we were is right
                        // for one frame and wrong for a hundred: the belt's own easing is
                        // thrown away by this very line (the pose is computed from S and D
                        // again next frame), so a pair that has merely touched can only
                        // separate if the shove is written into the car's PLACE on the
                        // road.
                        //
                        // A LITTLE, and no more. A shove with no budget is a car crabbing
                        // across a street: told to separate two bodies dropped exactly on
                        // top of one another it drove both of them to opposite kerbs,
                        // thirteen metres, and they were STILL touching (they were on two
                        // crossing streets, where "across the road" means two different
                        // things). Eighty centimetres at a metre a second is what a real
                        // touch needs; anything deeper than that is not a touch, it is a
                        // wedge, and a wedge is given up rather than shoved out of
                        // (TickNode backs out of a box; Derelict has the road).
                        if (_beltFor > 1f && Road != null && Mathf.Abs(_shoved) < 0.8f)
                        {
                            RoadSpace.Inside(this, _pos, fwd, HalfLen, HalfWide, out var shove);
                            float across = Vector3.Dot(shove, Road.RightAt(S));
                            if (Mathf.Abs(across) > 0.001f)
                            {
                                float budget = Mathf.Max(0f, 0.8f - Mathf.Abs(_shoved));
                                float step = Mathf.Clamp(Mathf.Clamp(across, -1f, 1f) * dt, -budget, budget);
                                _shoved += step;
                                D = Road.ClampD(D + step, RoadSpace.LateralExtent(fwd, HalfLen, HalfWide));
                            }
                        }
                        Pose(out pos, out fwd);
                    }
                    else pos = moved;
                    // Cancelling a lateral plan can change Pose even after S/D are
                    // restored. Validate that restored pose before writing the body.
                    RoadSpace.Advance(this, _pos, pos, fwd, HalfLen, HalfWide, out var restoredBlocker);
                    if (restoredBlocker != null) { pos = _pos; fwd = _fwd; }
                    Speed = 0f;
                    _stuckFor += dt;
                    _beltFor += dt;
                    _beltAt = RoadCarSimulation.Now;
                }
                else { _beltFor = 0f; _shoved = 0f; }
                float yawRate = Vector3.SignedAngle(_fwd, fwd, Vector3.up) * Mathf.Deg2Rad / Mathf.Max(dt, 1e-3f);
                steer = Mathf.Clamp(Mathf.Rad2Deg * Mathf.Atan(2.6f * yawRate / Mathf.Max(Mathf.Abs(Speed), 1f)), -35f, 35f);
            }
            if (DriveTrace.On) TraceStep(dt, pos, steer);
            _pos = pos;
            _fwd = fwd;
            _lastPlaced = true;
            if (Tf != null) Tf.SetPositionAndRotation(
                new Vector3(pos.x, RoadY + SurfaceLift(), pos.z), Quaternion.LookRotation(fwd, Vector3.up));
            OnPlaced(dt, Speed, steer);
        }

        /// <summary>How much higher than the road level the surface under the car
        /// stands: the carriageway's own - which on a slip road or a freeway's run down
        /// off its pillars CLIMBS along the road, so it is asked where the car actually
        /// is - or, in a junction, eased from the end of the road it came off to the
        /// start of the one it is going onto.</summary>
        float SurfaceLift()
        {
            if (Road != null) return Road.SurfaceOn(S);
            var via = Via;
            if (via == null) return 0f;
            // the height at the end of the lane we came off, and at the start of the
            // one we are joining: a lane runs A to B (heading +1) or B to A (-1)
            float a = EndLift(via.From, leaving: true), b = EndLift(via.To, leaving: false);
            if (Mathf.Approximately(a, b)) return a;
            float t = via.Length > 0.01f ? Mathf.Clamp01(ViaS / via.Length) : 1f;
            return Mathf.Lerp(a, b, t);
        }

        static float EndLift(RoadEdge lane, bool leaving)
        {
            var road = lane?.Road;
            if (road == null) return 0f;
            // leaving: the far end of that lane; joining: its near end
            bool atB = leaving == (lane.Heading > 0);
            return atB ? road.SurfaceB : road.SurfaceA;
        }

        /// <summary>The wheels, the doors - a subclass with a body animates it here.</summary>
        protected virtual void OnPlaced(float dt, float speed, float steerDegrees) { }

        /// <summary>A derived driver's last word on the target speed.</summary>
        protected virtual float LimitTarget(float target) => target;

        // The body's pose from the line it is on. The REAR AXLE is the point that
        // follows the line - where it is, which way the line runs there - and the
        // body's centre rides the axle's length ahead of it along the heading: the nose
        // points into a bend before the body has moved across, the tail follows, the
        // way a front-steered car goes (no crabbing sideways into a lane).
        void Pose(out Vector3 pos, out Vector3 fwd)
        {
            float a = Axle;
            if (Via != null)
            {
                JunctionClearance.Pose(Via, ViaS, a, out pos, out fwd);
                if (Mathf.Abs(_viaD0) > 0.01f)
                {
                    float t = Mathf.Clamp01(ViaS / Mathf.Max(1f, Via.Length * 0.6f));
                    var right = Vector3.Cross(Vector3.up, fwd);
                    pos += right * (_viaD0 * (1f - Mathf.SmoothStep(0f, 1f, t)));
                }
                return;
            }
            if (_man == Manoeuvre.UTurn)
            {
                ArcPose(_arcS0, _arcR, _arcSide, _arcHeading0, _arcAng, out pos, out fwd);
                return;
            }
            {
                float sa = S - Heading * a;
                if (_tailVia != null)
                {
                    // just out of a box: the axle still on the connector behind the lane's
                    // start (the body turns out of the corner, it does not snap straight)
                    float behind = (_tailViaEndS - sa) * Heading;
                    if (behind > 0f && behind <= _tailVia.Length)
                    {
                        _tailVia.Pose(_tailVia.Length - behind, out var axle, out fwd);
                        pos = axle + fwd * a;
                        return;
                    }
                    _tailVia = null;
                }
                if (Sliding)
                {
                    SlidePose(Road, Heading, _sFrom, _dFrom, _dTo, _sLen,
                        (S - _sFrom) * Heading, out pos, out fwd);
                    return;
                }
                float da = LateralValue(sa);
                // the lateral slope is per metre travelled; the heading turns toward the
                // side the line moves to, whichever way the road's axis runs - and on a
                // road that bends, "the road's axis" is the way it runs AT THE AXLE
                float slope = Sliding ? LateralSlope(sa) : 0f;
                var f = Road.DirAt(sa) * Heading + Road.RightAt(sa) * slope;
                fwd = f.normalized;
                pos = Road.Pose(sa, da) + fwd * a;
            }
        }

        // The rear axle follows the arc; the body stays on it until the axle reaches
        // the far straight. Completing at pi turned the remaining body angle in place.
        void ArcPose(float start, float radius, int side, int heading, float angle,
            out Vector3 position, out Vector3 forward)
        {
            float axleAngle = angle - Axle / radius;
            Vector3 axle;
            if (axleAngle < 0f)
            {
                axle = Road.Pose(start + heading * radius * axleAngle, side * radius);
                forward = Road.DirAt(start) * heading;
            }
            else if (axleAngle >= Mathf.PI)
            {
                float beyond = radius * (axleAngle - Mathf.PI);
                axle = Road.Pose(start - heading * beyond, -side * radius);
                forward = -Road.DirAt(start) * heading;
            }
            else
            {
                axle = Road.Pose(start + heading * radius * Mathf.Sin(axleAngle),
                    side * radius * Mathf.Cos(axleAngle));
                forward = (Road.DirAt(start) * (heading * Mathf.Cos(axleAngle)) +
                    Road.RightAt(start) * (-side * Mathf.Sin(axleAngle))).normalized;
            }
            position = axle + forward * Axle;
        }

        // Lane bands miss the swinging nose beside a parked car. Test the same
        // complete body poses the turn will actually follow before granting it.
        bool PhysicalArcClear(float radius, int side)
        {
            float end = Mathf.PI + Axle / radius;
            int samples = Mathf.Clamp(Mathf.CeilToInt(
                (Mathf.PI * (radius + HalfLen + HalfWide) + Axle) / .2f), 12, 160);
            for (int i = 0; i <= samples; i++)
            {
                ArcPose(S, radius, side, Heading, end * i / samples, out var at, out var facing);
                if (RoadSpace.Inside(this, at, facing, HalfLen, HalfWide, out _) != null)
                    return false;
            }
            return true;
        }

        // The lateral position the line has at s, read without moving anything.
        float LateralValue(float s)
        {
            if (!Sliding) return D;
            float p = (s - _sFrom) * Heading / _sLen;
            if (p >= 1f) return _dTo;
            if (p <= 0f) return _dFrom;
            return Mathf.Lerp(_dFrom, _dTo, Mathf.SmoothStep(0f, 1f, p));
        }

        // ------------------------------------------------------------------ lateral

        // Sample Pose's rear-axle geometry without moving the car. The axle
        // finishes Axle metres after the centre's road progress reaches the end.
        void SlidePose(Carriageway road, int heading, float startS, float fromD,
            float toD, float len, float travel, out Vector3 position, out Vector3 forward)
        {
            float axleTravel = travel - Axle;
            float axleS = startS + heading * axleTravel;
            float p = Mathf.Clamp01(axleTravel / Mathf.Max(0.001f, len));
            float d = Mathf.Lerp(fromD, toD, Mathf.SmoothStep(0f, 1f, p));
            float slope = (toD - fromD) * 6f * p * (1f - p) / Mathf.Max(0.001f, len);
            forward = (road.DirAt(axleS) * heading + road.RightAt(axleS) * slope).normalized;
            position = road.Pose(axleS, d) + forward * Axle;
        }

        void Slide(float toD, float length)
        {
            _dFrom = D;
            _dTo = toD;
            _sFrom = S;
            _sLen = Mathf.Max(2f, length);
        }

        float LateralAt(float s)
        {
            if (!Sliding) return D;
            float p = (s - _sFrom) * Heading / _sLen;
            // the slide is over when the AXLE is past its end, not the body's centre: the
            // axle follows the curve and would otherwise jump to the line at the end
            if (p >= 1f + Axle / _sLen) { _sLen = 0f; return _dTo; }
            if (p >= 1f) return _dTo;
            if (p <= 0f) return _dFrom;
            return Mathf.Lerp(_dFrom, _dTo, Mathf.SmoothStep(0f, 1f, p));
        }

        float LateralSlope(float s)
        {
            if (!Sliding) return 0f;
            float p = Mathf.Clamp01((s - _sFrom) * Heading / _sLen);
            return (_dTo - _dFrom) * 6f * p * (1f - p) / _sLen;
        }

        // The speed a smoothstep slide of this length and lateral reach takes at the
        // profile's lateral acceleration: peak curvature is 6*dd/L^2.
        float LateralCap(float len, float dd)
        {
            if (dd < 0.05f) return float.MaxValue;
            return len * Mathf.Sqrt(LateralG / (6f * dd));
        }

        // And the length that slide needs to be taken at this speed.
        float SlideLength(float dd, float speed)
        {
            float len = speed * Mathf.Sqrt(6f * dd / LateralG);
            // no shorter than the turning circle lets: peak curvature 6dd/L^2 <= 1/2.2
            float byCircle = Mathf.Sqrt(6f * 2.2f * dd);
            return Mathf.Clamp(len, Mathf.Max(3f, byCircle), 40f);
        }

        // ------------------------------------------------------------- changing lane

        /// <summary>Where the way on is another LANE of the road he is already on
        /// (LaneNet's route search knows a lane change as one of its own edges): a
        /// motorway exit is not reached from the through lane at all.</summary>
        public Dictionary<RoadEdge, RoadEdge> RouteShift;

        /// <summary>How many lane changes anybody has made: a black-box counter.</summary>
        public static int LaneChanges;

        /// <summary>How often a wanderer who meets an exit takes it. Nothing else puts
        /// ordinary traffic on and off a motorway: a routed car has a reason to be
        /// there, and a car with no reason at all would ride the deck for ever.</summary>
        const float ExitChance = 0.35f;
        /// <summary>Metres before an auxiliary lane runs out that leaving it stops being
        /// a good idea and becomes the plan. An acceleration lane is 180 m long; this is
        /// most of it, which is what it is for.</summary>
        const float AuxLeaveBy = 150f;
        /// <summary>Seconds behind something slower before he thinks about going round
        /// it on a motorway, and how much slower it has to be.</summary>
        const float OvertakeAfter = 2.5f, OvertakeBy = 3f;

        Carriageway _rolledOn;
        bool _wantsExit;
        float _slowSince, _innerSince;

        /// <summary>The lane he means to be in on this road, and the move over into it
        /// when there is room for it. Four reasons, in this order: the lane he is in is
        /// about to run out; the route says the way on is beside him; he means to take
        /// the exit; the man in front is slower than he wants to be.</summary>
        void TickLaneChange(float dt, float toEnd)
        {
            if (_man == Manoeuvre.LaneChange && !Sliding) { _man = Manoeuvre.None; ClearClaim(); }
            var road = Road;
            var lane = Lane;
            if (road == null || lane == null || Parked || _halted || Derelict) return;

            // once for each road he joins: does this one take the exit off it?
            if (!ReferenceEquals(_rolledOn, road))
            {
                _rolledOn = road;
                var ex = ExitLaneOn(road, lane);
                _wantsExit = ex != null && Route == null && Profile.Wanders && Random.value < ExitChance;
            }

            var want = WantedLane(dt, road, lane, toEnd);
            if (want == null || want == lane) return;
            if (_man != Manoeuvre.None || Sliding) return;
            // a slide is metres TRAVELLED: a car standing still cannot change lane, and
            // asking it to would only claim road it never crosses
            if (Mathf.Abs(Speed) < 1.5f) return;
            // and NOT across a junction. A car that is still moving over when it reaches
            // the box carries the offset into it, and the box squeezes it out over the
            // length of a connector - which at a motorway seam is a few metres. That is
            // a lurch sideways: three tenths of a metre in a frame at two metres a
            // second, which is the black box's "jump", and a wheel wound over with it.
            // one lane at a time, whatever the plan is
            int toward = (want.Offset - lane.Offset) * Heading > 0f ? +1 : -1;
            var step = road.Beside(lane, toward) ?? want;
            if (toEnd < SlideLength(Mathf.Abs(step.Offset - D), Mathf.Abs(Speed)) + 15f) return;
            if (!GapForLane(step)) return;
            BeginLaneChange(step);
        }

        /// <summary>The exit lane of this road, if it has one and he is not in it.</summary>
        static RoadEdge ExitLaneOn(Carriageway road, RoadEdge lane)
        {
            for (int i = 0; i < road.Lanes.Count; i++)
            {
                var l = road.Lanes[i];
                if (l.Exit && l.Heading == lane.Heading && l != lane) return l;
            }
            return null;
        }

        RoadEdge WantedLane(float dt, Carriageway road, RoadEdge lane, float toEnd)
        {
            // 1. this lane ends: an acceleration lane goes back to being shoulder, and
            //    what is at the end of it is a merge he would rather not have to make
            if (lane.Auxiliary && !lane.Exit && toEnd < AuxLeaveBy)
            {
                var inward = road.Beside(lane, -1);
                if (inward != null) return inward;
            }
            // 2. the route says so
            if (RouteShift != null && RouteShift.TryGetValue(lane, out var byRoute) && byRoute != null &&
                byRoute.Road == road && byRoute.Heading == lane.Heading) return byRoute;
            // 3. the exit he means to take
            if (_wantsExit)
            {
                var ex = ExitLaneOn(road, lane);
                if (ex != null) return ex;
            }
            // 4. the man in front is slower than the road allows, and the lane inside is
            //    open; and when nothing is holding him up, back over to the outside -
            //    without which every car in the city ends its life in the fast lane
            if (road.Class != RoadClass.Freeway || lane.Auxiliary) return null;
            var blocker = _blocker;
            bool slow = blocker != null && blocker.Car != null &&
                        Mathf.Max(0f, blocker.Vel * Heading) < Mathf.Abs(Speed) - OvertakeBy;
            _slowSince = slow ? _slowSince + dt : 0f;
            if (_slowSince > OvertakeAfter)
            {
                var inner = road.Beside(lane, -1);
                if (inner != null && !inner.Auxiliary) { _innerSince = 0f; return inner; }
            }
            var outer = road.Beside(lane, +1);
            if (outer != null && !outer.Auxiliary && !slow)
            {
                _innerSince += dt;
                if (_innerSince > 8f) { _innerSince = 0f; return outer; }
            }
            else _innerSince = 0f;
            return null;
        }

        /// <summary>Room to move over: the band between here and there clear ahead for a
        /// following gap and a bit, and whoever is coming up behind in the lane he is
        /// taking far enough back that he does not have to stand on the brakes.</summary>
        bool GapForLane(RoadEdge target)
        {
            var road = Road;
            float noseS = S + Heading * HalfLen, tailS = S - Heading * HalfLen;
            float lo = Mathf.Min(D, target.Offset) - HalfWide - SideAir;
            float hi = Mathf.Max(D, target.Offset) + HalfWide + SideAir;
            float need = Mathf.Abs(Speed) * Profile.TimeGap + Profile.FollowGap + 2f;
            if (road.FreeAhead(_occ, Heading, noseS, tailS, lo, hi, need + 6f) < need) return false;
            var back = road.Behind(_occ, Heading, tailS,
                                   target.Offset - HalfWide - SideAir, target.Offset + HalfWide + SideAir,
                                   out float bgap);
            if (back != null)
            {
                float vb = Mathf.Max(0f, back.Vel * Heading);
                if (bgap < vb * 1.2f + 5f) return false;
            }
            return true;
        }

        void BeginLaneChange(RoadEdge target)
        {
            float dd = Mathf.Abs(target.Offset - D);
            if (dd < 0.2f) { SetLane(target); _laneD = target.Offset; return; }
            _man = Manoeuvre.LaneChange;
            Slide(target.Offset, SlideLength(dd, Mathf.Abs(Speed)));
            SetLane(target);
            _laneD = target.Offset;
            DropNext();
            _next = null; _via = null; _committed = false;
            LaneChanges++;
            if (DriveTrace.On)
            {
                var sb = DriveTrace.Take();
                DriveTrace.Int(sb, "id", Id);
                DriveTrace.Str(sb, "why", target.Exit ? "exit" : target.Auxiliary ? "merge" : "over");
                DriveTrace.Num(sb, "v", Speed);
                DriveTrace.Num(sb, "d", target.Offset);
                DriveTrace.Vec(sb, "p", _pos);
                DriveTrace.Row("lanechange", sb.ToString());
            }
        }

        // the band the car's body covers, and the band it covers together with its plan
        float BodyLo() => D - HalfWide;
        float BodyHi() => D + HalfWide;
        const float SideAir = 0.3f;   // metres of air the car wants off anything it passes
        float PassHoldBack => SlideLength(5f, 0f) + 2f;       // metres kept back from something stood in the lane: room to swing right across
        float KerbHoldBack => SlideLength(1.2f, 0f) + 2.5f;   // and from a car at the kerb: room for the little swing round it
        float BandLo() => (Sliding ? Mathf.Min(D, _dTo) : D) - HalfWide - SideAir;
        float BandHi() => (Sliding ? Mathf.Max(D, _dTo) : D) + HalfWide + SideAir;

        void AbortLateral()
        {
            if (DriveTrace.On) DriveTrace.Event("man", "car " + Id, "abort " + _man, ManFields());
            if (_man == Manoeuvre.PullIn || _man == Manoeuvre.PullOut) return;
            bool laneReturn = _man == Manoeuvre.None && Sliding;
            _man = Manoeuvre.None;
            ClearClaim();
            // GIVING UP A PASS MEANS COMING BACK IN. Only a car still crossing over was
            // brought back before, and one that had already ARRIVED on its passing line
            // was simply left there: manoeuvre over, three metres off its lane, driving
            // down a part of the street where no lane is, for the rest of the run. It
            // reaches the junction alongside a car that is properly in the lane, both
            // are let into the box on lines that do not cross, and the one in the lane
            // turns straight into it - two cars locked together for 154 seconds, and six
            // thousand refused steps, in the run that found this.
            // A plain slide with no named manoeuvre is the automatic return to the lane.
            // If its angled body is what has wedged, end that angle where the car stands;
            // laying the same return again here merely repeats the collision. The yield
            // below lets it move clear before lane keeping asks again.
            if (laneReturn) _sLen = 0f;
            else if (Sliding && Mathf.Abs(D - _dFrom) < 0.4f) { _sLen = 0f; D = _dFrom; }
            else if (Mathf.Abs(D - _laneD) > 0.3f) Slide(_laneD, SlideLength(Mathf.Abs(D - _laneD), Mathf.Abs(Speed)));
            _yieldUntil = RoadCarSimulation.Now + 2.5f;
        }

        // ------------------------------------------------------------------ occupancy

        void UpdateOccupant()
        {
            if (_occ == null || Road == null) return;
            var o = _occ;
            // S/D describe the driving line. The actual centre and heading come
            // from its rear axle, including the tail still leaving a junction.
            Pose(out var bodyPosition, out var bodyForward);
            Road.Project(bodyPosition, out float s, out float d);
            float cosine = Mathf.Abs(Vector3.Dot(bodyForward, Road.DirAt(s)));
            float sine = Mathf.Abs(Vector3.Dot(bodyForward, Road.RightAt(s)));
            float along = cosine * HalfLen + sine * HalfWide;
            float across = sine * HalfLen + cosine * HalfWide;
            o.BodyS0 = s - along; o.BodyS1 = s + along;
            o.BodyD0 = d - across; o.BodyD1 = d + across;
            o.S0 = o.BodyS0; o.S1 = o.BodyS1; o.D0 = o.BodyD0; o.D1 = o.BodyD1;
            if (_claimS1 > _claimS0)
            {
                o.S0 = Mathf.Min(o.S0, _claimS0); o.S1 = Mathf.Max(o.S1, _claimS1);
                o.D0 = Mathf.Min(o.D0, _claimD0); o.D1 = Mathf.Max(o.D1, _claimD1);
            }
            // A SLIDE IS A PLAN, AND A PLAN NOBODY IS CARRYING OUT IS NOT ROAD ANYBODY
            // OWES US. A slide is measured in metres travelled, so a car that stops in
            // the middle of one never finishes it - and while it stands there its plan is
            // published across the running lane it never reached. Sixty-five seconds of a
            // live lane went to a crew car that was standing at the kerb the whole time.
            // Standing still, a car is its BODY; the plan goes back on the books the
            // moment it is actually moving again.
            if (Sliding && Mathf.Abs(Speed) > 0.1f)
            {
                o.D0 = Mathf.Min(o.D0, Mathf.Min(D, _dTo) - HalfWide);
                o.D1 = Mathf.Max(o.D1, Mathf.Max(D, _dTo) + HalfWide);
                float slideEnd = _sFrom + Heading * _sLen;
                o.S0 = Mathf.Min(o.S0, Mathf.Min(S, slideEnd) - HalfLen);
                o.S1 = Mathf.Max(o.S1, Mathf.Max(S, slideEnd) + HalfLen);
            }
            o.Vel = Speed * Heading;
            o.Slowing = _want < Speed - 0.05f ? (_wantHard ? HardBrake : Brake) : 0f;
            o.StopAt = Parked || Derelict ? S : _stopAt;
            o.Heading = _man == Manoeuvre.UTurn ? 0 : Heading;
            // A car nobody is coming back for reads to the street as a PARKED one, which
            // is what it is: everybody plans round it instead of queueing behind it for
            // ever. Without this a crew shot in its car left eight minutes of traffic
            // nose to tail behind a body in the lane.
            // and a car stood off the running lane waiting for a gap to pull out into IS
            // a parked car to everybody else, whatever it means to do next: they plan
            // round it rather than queueing behind a kerb.
            o.Parked = Parked || Derelict ||
                       (_man == Manoeuvre.PullOut && _pullOutWanted && Mathf.Abs(Speed) < 0.05f);
            o.Priority = Profile.Priority;
        }

        void Claim(float s0, float s1, float d0, float d1)
        {
            _claimS0 = Mathf.Min(s0, s1); _claimS1 = Mathf.Max(s0, s1);
            _claimD0 = Mathf.Min(d0, d1); _claimD1 = Mathf.Max(d0, d1);
            UpdateOccupant();
        }

        void ClearClaim()
        {
            _claimS0 = 1f; _claimS1 = -1f;
            if (_occ != null) UpdateOccupant();
        }

        // Is this band free to move into over this stretch ahead: nobody's claim on it
        // ahead within `needAhead`, nobody alongside in it, nobody behind in it who
        // would have to brake hard for us, nobody coming down it within the margin.
        bool BandFree(float dLo, float dHi, float needAhead, float seconds, out float freeAhead, bool allowParkedBeyond = false)
        {
            float noseS = S + Heading * HalfLen, tailS = S - Heading * HalfLen;
            freeAhead = Road.FreeAhead(_occ, Heading, noseS, tailS, dLo, dHi, needAhead + 40f, allowParkedBeyond);
            if (freeAhead < needAhead) return false;
            var behind = Road.Behind(_occ, Heading, tailS, dLo, dHi, out float gapBehind);
            if (behind != null && behind.Car != null && !behind.Parked)
            {
                float vb = behind.Vel * Heading;
                if (vb > 0.5f)
                {
                    float closing = vb - Mathf.Max(0f, Speed);
                    float need = Profile.FollowGap + vb * 0.8f + (closing > 0f ? closing * closing / (2f * 3f) : 0f);
                    if (gapBehind < need) return false;
                }
                else if (gapBehind < 0.5f) return false;
            }
            float farS = noseS + Heading * needAhead;
            if (Road.OncomingWithin(_occ, Heading, noseS, farS, dLo, dHi, seconds, Mathf.Abs(Speed))) return false;
            return true;
        }

        // ------------------------------------------------------------------ the tactics

        float _lateralStillFor;

        bool ReleaseStalledLateral(float dt)
        {
            _lateralStillFor = Sliding && Mathf.Abs(Speed) < 0.2f &&
                               _man != Manoeuvre.UTurn && _man != Manoeuvre.Reverse
                ? _lateralStillFor + dt : 0f;
            if (_lateralStillFor < 6f) return false;
            if (_man == Manoeuvre.PullIn || _man == Manoeuvre.PullOut)
            {
                _lateralStillFor = 0f;
                RetreatFromKerb();
                return true;
            }
            // Withdraw a stalled sweep's reservation to release waiting traffic;
            // following and the collision belt still protect the actual body.
            _lateralStillFor = 0f;
            _sLen = 0f;
            _pullOutWanted = false;
            _man = Manoeuvre.None;
            ClearClaim();
            _yieldUntil = RoadCarSimulation.Now + PullOutPatience;
            UpdateOccupant();
            return true;
        }

        void Decide(float dt, RoadOccupant leader, float gap, float vLead, RoadNode node, float toEnd)
        {
            if (Deadlock.Active || Deadlock.Mutual) return;
            // An explicit halt after failure still overrides autonomous recovery.
            if (ParkingFailed && _halted) return;
            float now = RoadCarSimulation.Now;
            bool stopped = Mathf.Abs(Speed) < 0.4f;
            if (ReleaseStalledLateral(dt)) return;

            // Remember recent refusals: an isolated clear frame cannot reset a wedge.
            _wedgedFor = now - _beltAt < BeltMemory && Mathf.Abs(Speed) < 0.15f
                ? _wedgedFor + dt : 0f;

            // A late obstruction can wedge a sweep. Retrace parking curves;
            // other lateral manoeuvres use their ordinary abort path.
            bool lateralWedge = Sliding ||
                (_man != Manoeuvre.None && _man != Manoeuvre.UTurn && _man != Manoeuvre.Reverse);
            if (_wedgedFor > 1.5f && Road != null && lateralWedge)
            {
                if (DriveTrace.On) DriveTrace.Event("man", "car " + Id, "gave up " + _man + ": wedged", ManFields());
                if (_man == Manoeuvre.PullIn || _man == Manoeuvre.PullOut)
                {
                    // Retrace either parking curve, preserving the angled body.
                    RetreatFromKerb();
                    _yieldUntil = now + 2.5f;
                }
                else AbortLateral();
                _beltFor = 0f;
                _wedgedFor = 0f;
            }
            // With no manoeuvre to retract, back away or become an obstacle
            // that the rest of the traffic can plan around.
            else if (_wedgedFor > 3f && Road != null && _man == Manoeuvre.None &&
                     Mathf.Abs(Speed) < 0.15f && !Parked)
            {
                _beltFor = 0f;
                _wedgedFor = 0f;
                _backedFor = null;                       // this is an emergency, not a tactic
                if (_blocker != null && TryReverse(_blocker))
                {
                    if (DriveTrace.On) DriveTrace.Event("man", "car " + Id, "backing out of a wedge", ManFields());
                }
                else if (!Derelict)
                {
                    Derelict = true;
                    LeaveBox();
                    DropNext();
                    if (DriveTrace.On) DriveTrace.Event("man", "car " + Id, "wedged and out of answers - stood down", ManFields());
                }
            }

            // behind something that is not moving (or, hot, crawling)
            float slowUnder = Profile.Patience <= 0f ? Cruise() * 0.6f : 2.5f;
            bool blocked = leader != null && !leader.Parked && vLead < slowUnder && leader.Vel * Heading > -0.5f &&
                           (gap < Mathf.Max(Profile.FollowGap + Mathf.Abs(Speed) * Mathf.Abs(Speed) / (2f * Brake) + 6f,
                               leader.Car == null ? PassHoldBack + 1.5f : 0f) ||
                            (_holdFor > 0f && gap < 16f));
            bool behindParked = leader != null && leader.Parked && gap < Profile.FollowGap + Mathf.Abs(Speed) * 1.5f + 8f;
            bool headOn = leader != null && leader.Vel * Heading < -0.5f && gap < 12f;
            bool standoff = leader != null && stopped && !leader.Moving && leader.Car != null && gap < 6f &&
                            (leader.Heading != Heading) && !leader.Parked;
            _blockedFor = blocked ? _blockedFor + dt : 0f;
            _standoffFor = standoff ? _standoffFor + dt : 0f;
            bool jam = stopped && leader != null && !leader.Moving && gap < PassHoldBack + 1.5f && !_nerve.Frightened &&
                       !InQueue && toEnd > 30f;
            _jammed = jam ? _jammed + dt : 0f;
            _stuckFor = stopped && leader != null && gap < PassHoldBack + 1.5f ? _stuckFor + dt : 0f;

            switch (_man)
            {
                case Manoeuvre.None:
                    if (Parked) return;
                    if (_hasGoal && _goalPark && _parkPlanReady && !Sliding &&
                        Road == _goalRoad && Heading == _goalHeading &&
                        Mathf.Abs(_goalS - S) < .3f && ParkingAligned(_goalD)) return;
                    // nose to nose with a car that is not going anywhere either: the lower
                    // priority gives way - pulls over to his kerb where it is free, or holds
                    // dead still - and the other comes through
                    if (standoff && _standoffFor > Profile.StandoffPatience)
                    {
                        _standoffFor = 0f;
                        bool iYield = leader.Priority < Profile.Priority ? false
                            : leader.Priority > Profile.Priority ? true
                            : (leader.Car != null && leader.Car.Id < Id);
                        if (iYield && Profile.GivesWay)
                        {
                            _giveUntil = now + Profile.GiveWayFor;
                            _jammed = 0f;
                            if (!TryAside()) { /* dead still where we stand */ }
                            return;
                        }
                        if (!iYield && Profile.Reverses && stopped && _stuckFor > 2.5f)
                        {
                            // he cannot move; back off and find a way round him
                            if (TryReverse(leader)) return;
                        }
                    }
                    if (now < _giveUntil) return;

                    // gunfire ahead and a way out: round and away (the traffic's escape)
                    if (!Fearless && _nerve.Approaching && Mathf.Abs(Speed) < 5f && Road.TwoWay && node != null && toEnd > 20f)
                    {
                        if (TryUTurn(escape: true)) { _nerve.SlowUntil = 0f; _nerve.BoltUntil = now + 8f; return; }
                    }

                    // Waiting at a planned parking entry is not a traffic jam to
                    // overtake. Keep emergency yielding above, but do not turn this
                    // deliberate hold into another pass/return/re-pick cycle.
                    if (_hasGoal && _goalPark && _parkPlanReady && _parkEntryLen > 0f &&
                        !_parkTrafficClear && _parkTrafficCheck > 0f && !Sliding &&
                        Road == _goalRoad && Heading == _goalHeading &&
                        Mathf.Abs(_parkEntryS - S) < 1f) return;

                    // the crown between the lanes, with the guns out: a queue in our lane and
                    // the crown open - drive it for as long as it is open
                    if (Profile.UsesCrown && Profile.Patience <= 0f && LaneBusyAhead(45f) && now >= _yieldUntil)
                    {
                        if (TryCrown()) return;
                    }

                    // a car at the kerb ahead sticking into our lane: swing round it, a
                    // little over the crown when nothing is coming
                    if (behindParked && Profile.PassesAtKerb && now >= _yieldUntil && !IsOurParkingSpot(leader))
                    {
                        if (TryPass(leader, kerbOnly: true)) return;
                        // too close behind it to swing out: a few metres back first
                        if (Profile.Reverses && stopped && gap < 4f && !JustBackedOff(leader) && TryReverse(leader, gap)) return;
                    }

                    // something stopped in the lane: round it through whatever the profile
                    // allows, after the patience; wedged too close to swing - back off first
                    if (blocked && _blockedFor > Profile.Patience && now >= _yieldUntil && !IsOurParkingSpot(leader) &&
                        // A static obstacle cannot clear with the junction's queue.
                        // TryPass still requires room to return before the stop line.
                        (!InQueue || Profile.PushesPastQueues || leader.Car == null))
                    {
                        if (TryPass(leader, kerbOnly: false, desperate: ReferenceEquals(_jamLeader, leader.Who))) return;
                        if (Profile.Reverses && stopped && gap < 5f && !JustBackedOff(leader) && TryReverse(leader, gap)) return;
                    }

                    // a jam that does not clear: the traffic uses the far lane when it is
                    // empty, and failing that turns round
                    if (_jammed > 5f && now >= _yieldUntil)
                    {
                        _jammed = 0f;
                        _jamLeader = leader.Who;
                        if (TryPass(leader, kerbOnly: false, desperate: true)) return;
                        if (Profile.Reverses && stopped && gap < 5f && TryReverse(leader, gap)) return;
                        if (Road.TwoWay && toEnd > 12f && TryUTurn(escape: true)) return;
                    }
                    break;

                case Manoeuvre.Pass:
                case Manoeuvre.Crown:
                    TickPass(dt, leader, gap, node, toEnd);
                    break;

                case Manoeuvre.Aside:
                    if (!Sliding && _claimS1 > _claimS0) ClearClaim();   // stood aside: the lane is theirs
                    if (now >= _giveUntil && !Sliding)
                    {
                        // back into the lane once it is free behind and beside us
                        float lo = _laneD - HalfWide - 0.3f, hi = _laneD + HalfWide + 0.3f;
                        if (BandFree(lo, hi, 10f, 2.5f, out _))
                        {
                            _man = Manoeuvre.None;
                            ClearClaim();
                            Slide(_laneD, SlideLength(Mathf.Abs(D - _laneD), Mathf.Max(Speed, 3f)));
                        }
                    }
                    break;

                case Manoeuvre.PullOut:
                    if (_pullOutWanted) TickPullOut();
                    else if (!Sliding && _exitAdvance) PullOut();
                    else if (!Sliding)
                    {
                        _man = Mathf.Abs(D - _laneD) > .2f ? Manoeuvre.Pass : Manoeuvre.None;
                        ClearClaim();
                        if (_man == Manoeuvre.None && _hasGoal && _goalPark && Road == _goalRoad && Heading == _goalHeading)
                            ChooseKerbSpot(aheadOnly: true);
                    }
                    break;

                case Manoeuvre.PullIn:
                    if (!Sliding) { _man = Manoeuvre.None; ClearClaim(); }
                    // A newly occupied destination can stop an entry mid-curve.
                    // Retrace it before selecting another slot; never snap straight.
                    else if (Mathf.Abs(Speed) < 0.2f && now - _pullInAsked > 5f)
                    {
                        if (DriveTrace.On) DriveTrace.Event("man", "car " + Id, "gave up a pull-in that was not moving", ManFields());
                        RetreatFromKerb();
                    }
                    break;
            }
        }

        // Something stood or crawling in our lane within sight, or a car going our way
        // we will be on the bumper of within a few seconds.
        bool LaneBusyAhead(float look)
        {
            float noseS = S + Heading * HalfLen, tailS = S - Heading * HalfLen;
            float lo = _laneD - HalfWide, hi = _laneD + HalfWide;
            var o = Road.Ahead(_occ, Heading, noseS, tailS, lo, hi, out float gap);
            if (o == null || gap > look) return false;
            float his = o.Vel * Heading;
            if (his < -0.5f) return false;
            if (his < 2.5f) return true;
            float closing = Cruise() - his;
            return closing > 0.5f && gap / closing < 3f;
        }

        /// <summary>Check the complete angled body along its slide and the following
        /// <paramref name="run"/> metres, rather than the whole band between lanes.</summary>
        bool SlidePathClear(float toD, float len, float run)
            => SlidePathClear(Road, Heading, S, D, toD, len, run);

        // A broad following band can include a parked shoulder that the existing
        // slide never touches. Only a fixed body with a clear complete remainder
        // may be excluded from following; a driver preparing to leave still owns
        // the normal traffic priority and stopping space.
        bool RemainingSlideClearOf(RoadOccupant other)
        {
            if (!Sliding || Road == null || other == null || other.Who == null ||
                ReferenceEquals(other.Who, this) || other.Moving) return false;
            var car = other.Car;
            if (car != null && (car.HasGoal || car._pullOutWanted || car._man == Manoeuvre.PullOut ||
                (!car.Parked && !car.Derelict && !car.Wrecked))) return false;
            var body = other.Who;
            if (RoadSpace.Overlap(RoadPosition, RoadForward, HalfLen, HalfWide,
                body.RoadPosition, body.RoadForward, body.HalfLength, body.HalfWidth,
                SideAir, out _)) return false;
            float from = Mathf.Max(0f, (S - _sFrom) * Heading);
            float remaining = Mathf.Max(0f, _sLen + Axle - from);
            float dd = Mathf.Abs(_dTo - _dFrom);
            float step = 0.2f / (1f + 1.5f * dd / _sLen +
                (Axle + HalfLen + HalfWide) * 6f * dd / (_sLen * _sLen));
            int steps = Mathf.Max(1, Mathf.CeilToInt(remaining / step));
            for (int i = 0; i <= steps; i++)
            {
                SlidePose(Road, Heading, _sFrom, _dFrom, _dTo, _sLen,
                    from + remaining * i / steps, out var position, out var forward);
                Road.Project(position, out float s, out _);
                position.y = Road.SurfaceOn(s);
                if (RoadSpace.Overlap(position, forward, HalfLen, HalfWide,
                    body.RoadPosition, body.RoadForward, body.HalfLength, body.HalfWidth,
                    SideAir, out _)) return false;
            }
            return true;
        }

        // Planning ahead considers fixed obstacles. Immediately before starting a
        // manoeuvre the same path is checked with moving traffic and reservations.
        bool SlidePathClear(Carriageway road, int heading, float startS, float fromD,
            float toD, float len, float run, bool stationaryOnly = false,
            Vector3? extraPosition = null, Vector3 extraForward = default,
            float extraHalfLength = 0f, float extraHalfWidth = 0f)
        {
            if (road == null || len < SlideLength(Mathf.Abs(toD - fromD), 0f) - 0.01f)
                return false;
            float dd = Mathf.Abs(toD - fromD);
            float total = len + Axle + Mathf.Max(0f, run);
            // Bound both centre movement and the corner movement caused by yaw.
            const float sweepAir = SideAir + .03f;
            float step = 0.05f / (1f + 1.5f * dd / len +
                (Axle + HalfLen + HalfWide) * 6f * dd / (len * len));
            int steps = Mathf.Max(4, Mathf.CeilToInt(total / step));
            for (int i = 0; i <= steps; i++)
            {
                float travel = total * i / steps;
                SlidePose(road, heading, startS, fromD, toD, len,
                    travel, out var position, out var forward);
                road.Project(position, out float s, out float d);
                float cosine = Mathf.Abs(Vector3.Dot(forward, road.DirAt(s)));
                float sine = Mathf.Abs(Vector3.Dot(forward, road.RightAt(s)));
                float along = cosine * HalfLen + sine * HalfWide;
                float across = sine * HalfLen + cosine * HalfWide;
                float axleP = Mathf.Clamp01((travel - Axle) / len);
                float axleD = Mathf.Lerp(fromD, toD, Mathf.SmoothStep(0f, 1f, axleP));
                // Keep the road's existing kerb support allowance; parked bodies
                // deliberately overhang the kerb. Obstacle checks below use the
                // complete angled body, independently of that support policy.
                if (s - along < 0f || s + along > road.Length || !road.Drivable(axleD, HalfWide))
                    return false;
                position.y = road.SurfaceOn(s);
                if (extraPosition.HasValue && RoadSpace.Overlap(position, forward, HalfLen, HalfWide,
                    extraPosition.Value, extraForward, extraHalfLength, extraHalfWidth, sweepAir, out _)) return false;
                for (int j = 0; j < road.Occupants.Count; j++)
                {
                    var other = road.Occupants[j];
                    if (ReferenceEquals(other.Who, this)) continue;
                    if (stationaryOnly && other.Car != null && !other.Car.Parked &&
                        !other.Car.Derelict && !other.Car.Wrecked) continue;
                    bool overlapsBand = other.S0 < s + along + sweepAir && other.S1 > s - along - sweepAir &&
                        other.D0 < d + across + sweepAir && other.D1 > d - across - sweepAir;
                    if (!overlapsBand) continue;
                    // A physical body is an oriented box. Its bounding road band is
                    // only a broad phase; the empty corners do not block a safe swing.
                    var body = other.Who;
                    if (body == null || RoadSpace.Overlap(position, forward, HalfLen, HalfWide,
                        body.RoadPosition, body.RoadForward, body.HalfLength, body.HalfWidth,
                        sweepAir, out _)) return false;
                    bool hasClaim = other.S0 < other.BodyS0 - 0.01f || other.S1 > other.BodyS1 + 0.01f ||
                        other.D0 < other.BodyD0 - 0.01f || other.D1 > other.BodyD1 + 0.01f;
                    if (hasClaim && other.S0 < s + along && other.S1 > s - along &&
                        other.D0 < d + across && other.D1 > d - across) return false;
                }
                // Other roads can cross or run beside this one. The physical index
                // includes those bodies without inventing a road-band collision.
                if (RoadSpace.Inside(this, position, forward,
                    HalfLen, HalfWide, out _, stationaryOnly) != null) return false;
            }
            return true;
        }

        // The way round what is stopped ahead: the bands the profile allows, nearest
        // our line first - our own lane shifted, the crown, the far lane - the first
        // that is free from here to past it, with the margin off oncoming; then the
        // slide out, the run past, and the return laid as one claim.
        bool TryPass(RoadOccupant blocker, bool kerbOnly, bool desperate = false)
        {
            if (blocker == null || Road == null) return false;
            float noseS = S + Heading * HalfLen;
            float bFar = Heading > 0 ? blocker.S1 : blocker.S0;
            float bNear = Heading > 0 ? blocker.S0 : blocker.S1;
            float past = (bFar - noseS) * Heading + HalfLen * 2f + 3f;     // metres of travel to be clear of it
            if (past < 0f) return false;
            if (blocker.Moving) past += Mathf.Abs(blocker.Vel) * 3f;
            var node = Road.NodeAhead(Heading);
            float toEnd = (Road.EndS(Heading) - noseS) * Heading;
            float room = node != null ? toEnd - node.StopSetback - 2f : toEnd - 2f;
            float needBack = SlideLength(1.0f, Mathf.Max(Speed, 4f));     // the least shift's return; per candidate below
            // the reasons are trace strings and nothing but the trace reads them (the
            // same rule as Why): a blocked car asks this every frame, and untraced play
            // was building a handful of them per car per frame for nobody
            if (past + needBack > room) { PassWhy = DriveTrace.On ? $"no room: past {past:F1} back {needBack:F1} room {room:F1}" : "no room"; return false; }
            PassWhy = "";

            float w = HalfWide + 0.3f;
            // candidate lateral positions, nearest our lane first
            float crownSide = Mathf.Sign(_laneD);                          // our side of the axis
            var cands = _cands;
            cands.Clear();
            // 1. the least shift that clears him, staying mostly in our lane (a parked car)
            float clearD = crownSide > 0f ? blocker.D0 - w - 0.5f : blocker.D1 + w + 0.5f;
            float minOut = crownSide > 0f ? Mathf.Min(clearD, _laneD) : Mathf.Max(clearD, _laneD);
            bool overCrown = crownSide > 0f ? minOut - HalfWide < 0f : minOut + HalfWide > 0f;
            float over = crownSide > 0f ? -(minOut - HalfWide) : (minOut + HalfWide);
            if (!overCrown || over <= Profile.OverCrown) cands.Add(minOut);
            if (!kerbOnly || desperate)
            {
                if (Profile.UsesCrown) cands.Add(crownSide * Mathf.Min(HalfWide * 0.5f, 0.6f));
                if (Profile.UsesCrown) cands.Add(0f);
                if (Profile.UsesOpposite || desperate)
                {
                    var opp = Road.LaneFor(-Heading, -_laneD);
                    if (opp != null) cands.Add(opp.Offset);
                }
                // the kerb side of him, if he stands out in the road
                float kerbSide = crownSide * (Road.HalfRoad - HalfWide - 0.4f);
                cands.Add(kerbSide);
            }
            foreach (float cand in cands)
            {
                if (!Road.Drivable(cand, HalfWide)) { if (DriveTrace.On) PassWhy += $"cand {cand:F1}: not drivable; "; continue; }
                needBack = SlideLength(Mathf.Abs(cand - _laneD), Mathf.Max(Speed, 4f));
                if (past + needBack > room) { if (DriveTrace.On) PassWhy += $"cand {cand:F1}: no room for the return; "; continue; }
                float lo = cand - w, hi = cand + w;
                // the band must clear the blocker himself
                if (blocker.Overlaps(lo, hi)) { if (DriveTrace.On) PassWhy += $"cand {cand:F1}: blocker in band; "; continue; }
                float tOccupy = (past + needBack) / Mathf.Max(Mathf.Abs(Speed), 5f) + 1f;
                bool opposite = Mathf.Sign(cand) != crownSide && Mathf.Abs(cand) > 0.8f;
                float margin = opposite ? Profile.OncomingMargin : Profile.OncomingMargin * 0.6f;
                if (!BandFree(lo, hi, past + needBack + 2f, tOccupy + margin, out float fa)) { if (DriveTrace.On) PassWhy += $"cand {cand:F1}: band not free (free {fa:F1} need {past + needBack + 2f:F1}, {tOccupy + margin:F1}s); "; continue; }
                // the slide out must be done before the first thing in what it sweeps
                float sweepLo = Mathf.Min(D, cand) - w, sweepHi = Mathf.Max(D, cand) + w;
                float swept = Road.FreeAhead(_occ, Heading, noseS, S - Heading * HalfLen, sweepLo, sweepHi, past);
                float outLen = SlideLength(Mathf.Abs(cand - D), Mathf.Max(Mathf.Abs(Speed), 3f));
                float outMin = SlideLength(Mathf.Abs(cand - D), 0f);     // the turning circle's least
                // the slide out must be complete a length and a half before what it sweeps past
                if (swept < 0f || (Mathf.Abs(cand - D) > 0.3f && swept < outMin + 1.5f))
                {
                    // The band says no. It says no to every car swinging out of a kerb
                    // slot, because the body it is going round is in the band by
                    // definition - so ask the swing itself (SlidePathClear) before
                    // refusing, and take the tightest one the turning circle allows.
                    if (!SlidePathClear(cand, outMin, past)) { if (DriveTrace.On) PassWhy += $"cand {cand:F1}: swept {swept:F1} < {outMin + 1.5f:F1}, and the swing itself is fouled; "; continue; }
                    outLen = outMin;
                }
                else outLen = Mathf.Min(outLen, Mathf.Max(outMin, swept - 1.5f));
                // go
                _jamLeader = null;
                _man = Manoeuvre.Pass;
                _manD = cand;
                _manPastS = bFar + Heading * (HalfLen * 2f + 3f);
                Slide(cand, outLen);
                float farS = noseS + Heading * (past + needBack + 2f);
                Claim(S - Heading * HalfLen, farS, Mathf.Min(lo, _laneD - HalfWide), Mathf.Max(hi, _laneD + HalfWide));
                _blockedFor = 0f;
                return true;
            }
            return false;
        }

        static readonly List<float> _cands = new List<float>();

        // The crown between the lanes, held while the lane is busy and the crown open.
        bool TryCrown()
        {
            float cand = Mathf.Sign(_laneD) * Mathf.Min(HalfWide * 0.5f, 0.6f);
            if (!Road.Drivable(cand, HalfWide)) return false;
            float w = HalfWide + 0.3f;
            float need = 25f;
            if (!BandFree(cand - w, cand + w, need, Profile.OncomingMargin, out _)) return false;
            _man = Manoeuvre.Crown;
            _manD = cand;
            Slide(cand, SlideLength(Mathf.Abs(cand - D), Mathf.Max(Mathf.Abs(Speed), 5f)));
            float noseS = S + Heading * HalfLen;
            Claim(S - Heading * HalfLen, noseS + Heading * need, Mathf.Min(cand - w, _laneD - HalfWide), Mathf.Max(cand + w, _laneD + HalfWide));
            return true;
        }

        // On the pass / the crown: keep the claim rolling ahead of us, and come back
        // into the lane once it is clear ahead and we are past what we went round.
        void TickPass(float dt, RoadOccupant leader, float gap, RoadNode node, float toEnd)
        {
            // The passing line can feed the parking entry directly. Returning to
            // the lane first spends the room needed by that entry and misses it.
            if (_man == Manoeuvre.Pass && !Sliding && _hasGoal && _goalPark &&
                Road == _goalRoad && Heading == _goalHeading && Mathf.Abs(_parkRequestedS - S) < ParkingSearchReach + 15f &&
                ChooseKerbSpot(aheadOnly: true))
            {
                _man = Manoeuvre.None;
                ClearClaim();
                return;
            }
            float noseS = S + Heading * HalfLen, tailS = S - Heading * HalfLen;
            float w = HalfWide + 0.3f;
            float laneLo = _laneD - HalfWide, laneHi = _laneD + HalfWide;
            float laneFree = Road.FreeAhead(_occ, Heading, noseS, tailS, laneLo, laneHi, 60f);
            float bandFree = Road.FreeAhead(_occ, Heading, noseS, tailS, _manD - w, _manD + w, 60f);
            bool pastIt = _man == Manoeuvre.Crown || (_manPastS - noseS) * Heading <= 0f;
            float backLen = SlideLength(Mathf.Abs(_manD - _laneD), Mathf.Max(Mathf.Abs(Speed), 3f));
            float backMin = SlideLength(Mathf.Abs(_manD - _laneD), 3f);     // slowed right down
            float room = toEnd - (node != null ? node.StopSetback : 0f) - 2f;
            bool mustReturn = room < backMin + 6f;
            if (room < backLen + 4f) backLen = Mathf.Max(backMin, room - 4f);  // a shorter, slower return fits
            bool laneClear = laneFree >= backLen + 6f || laneFree >= 60f;
            bool wantBack = (pastIt && laneClear) || mustReturn ||
                            (_man == Manoeuvre.Crown && (bandFree < 12f && laneFree > bandFree));
            if (_man == Manoeuvre.Crown && !wantBack && !LaneBusyAhead(45f) && laneFree > 30f) wantBack = true;

            if (!Sliding && !wantBack)
            {
                // roll the claim on ahead, re-checking the oncoming each time
                float ahead = Mathf.Min(bandFree, 30f);
                float farS = noseS + Heading * Mathf.Max(8f, ahead);
                float seconds = Mathf.Max(8f, ahead) / Mathf.Max(Mathf.Abs(Speed), 5f) + Profile.OncomingMargin;
                bool opposite = Mathf.Sign(_manD) != Mathf.Sign(_laneD) && Mathf.Abs(_manD) > 0.8f;
                if (opposite && Road.OncomingWithin(_occ, Heading, noseS, farS, _manD - w, _manD + w, seconds, Mathf.Abs(Speed)))
                {
                    // somebody coming down it: back into our lane if we can, else hold the claim short
                    if (laneFree >= backLen + 2f) wantBack = true;
                    else Claim(tailS, noseS + Heading * Mathf.Max(2f, Mathf.Min(ahead, gap)), Mathf.Min(_manD - w, laneLo), Mathf.Max(_manD + w, laneHi));
                }
                else Claim(tailS, farS, Mathf.Min(_manD - w, laneLo), Mathf.Max(_manD + w, laneHi));
            }
            if (wantBack && !Sliding && Mathf.Abs(D - _laneD) > 0.2f)
            {
                if (laneFree >= Mathf.Min(backLen, 6f))
                {
                    Slide(_laneD, Mathf.Min(backLen, Mathf.Max(4f, laneFree - 1f)));
                    Claim(tailS, noseS + Heading * (backLen + 2f), Mathf.Min(_manD - w, laneLo), Mathf.Max(_manD + w, laneHi));
                }
            }
            if (!Sliding && Mathf.Abs(D - _laneD) <= 0.2f)
            {
                _man = Manoeuvre.None;
                ClearClaim();
            }
        }

        // Over to our own kerb and a stand there: the whole road left to whoever is
        // coming through. Only with the kerb ahead empty.
        bool TryAside()
        {
            float kerb = Road.KerbDOnSide(_laneD, HalfWide);
            float w = HalfWide + 0.2f;
            float noseS = S + Heading * HalfLen, tailS = S - Heading * HalfLen;
            float free = Road.FreeAhead(_occ, Heading, noseS, tailS, kerb - w, kerb + w, 20f);
            if (free < 12f) return false;
            _man = Manoeuvre.Aside;
            _manD = kerb;
            Slide(kerb, 9f);
            Claim(tailS, noseS + Heading * 12f, Mathf.Min(kerb - w, _laneD - HalfWide), Mathf.Max(kerb + w, _laneD + HalfWide));
            return true;
        }

        // ------------------------------------------------------------------ the turn in the road

        int _arcHeading0;

        /// <summary>The radius the turn-round would be taken on from where the car
        /// stands: its own distance off the crown, held between the tightest a body can
        /// swing round and the widest the carriageway has room for.</summary>
        float UTurnRadius() =>
            Road == null ? 2.2f : Mathf.Clamp(Mathf.Abs(D), 2.2f, Road.HalfRoad - HalfWide - 0.45f);

        /// <summary>How fast the arc itself may be taken - the profile's own figure, or
        /// as much as the radius will bear at its lateral limit, whichever is less.</summary>
        float UTurnArcSpeed() =>
            Mathf.Min(Profile.UTurnSpeed, Mathf.Sqrt(LateralG * UTurnRadius()));

        /// <summary>What the throttle is held to while the driver MEANS to turn round.
        ///
        /// It has to be a pace the turn will actually be granted at, and for a long time
        /// it was not. The throttle was held to UTurnSpeed + 2 and the gate below admits
        /// arcSpeed + 1.5, where arcSpeed is UTurnSpeed AT BEST and less than it on any
        /// street narrow enough to make the arc tight - so a driver who had slowed all
        /// the way to the cap he was given was still half a metre a second too fast to
        /// be allowed the turn, on every road, for ever. The turn then only ever
        /// happened where something ELSE had slowed the car: a queue, a red, or the end
        /// of the street. Which is exactly what the player reported - the machine
        /// "either rides to the end of the street to turn round or goes round the
        /// block", and never simply turns where it stands.
        ///
        /// Both numbers now come off the same arithmetic, so they cannot drift apart
        /// again: the approach sits a metre a second inside the gate, which is room for
        /// the throttle to settle without ever closing it.</summary>
        float UTurnApproachSpeed() => UTurnArcSpeed() + 1f;

        /// <summary>Turn round inside the carriageway, here or as soon as the sweep is
        /// clear: the arc from this side to the mirror lane, claimed whole, only when
        /// nothing stands on it and nothing is coming down either band in time.</summary>
        public bool TryUTurn(bool escape = false)
        {
            UTurnWhy = "";
            if (Road == null || !Road.TwoWay || Via != null || _man == Manoeuvre.UTurn) { UTurnWhy = "no road for it"; return false; }
            if (!Profile.UTurnsInRoad && !escape) { UTurnWhy = "profile"; return false; }
            if (Road.MedianHalf > 0f) { UTurnWhy = "median"; return false; }
            float r = UTurnRadius();
            int side = D >= 0f ? 1 : -1;
            if (Mathf.Abs(Mathf.Abs(D) - r) > 0.3f)
            {
                // not on a radius we can turn from: over to it first, and ask again
                if (!Sliding && Road.Drivable(side * r, HalfWide)) Slide(side * r, 8f);
                UTurnWhy = DriveTrace.On ? $"off the radius (d {D:F1}, r {r:F1})" : "off the radius";
                return false;
            }
            // The arc is a couple of metres across. Taken at cruising speed it is a
            // pirouette - half a turn in half a second, the body slewing round on the
            // spot - which is what a car doing it at fifteen metres a second looked
            // like. The driver slows FOR the turn (TickRoad holds the throttle down to
            // UTurnApproachSpeed while it means to make one); until he has, the answer
            // is no.
            float arcSpeed = UTurnArcSpeed();
            if (Mathf.Abs(Speed) > arcSpeed + 1.5f) { UTurnWhy = DriveTrace.On ? $"too fast ({Mathf.Abs(Speed):F1} > {arcSpeed + 1.5f:F1})" : "too fast"; return false; }

            if (!SweepClear(r, side, out float sweepS0, out float sweepS1, out float sweepLo, out float sweepHi))
            { UTurnWhy = DriveTrace.On ? "sweep: " + SweepWhy : "sweep"; return false; }
            _man = Manoeuvre.UTurn;
            _tailVia = null;
            _arcS0 = S;
            _arcR = r;
            _arcSide = side;
            _arcAng = 0f;
            _arcBacking = false; _arcBlockedFor = 0f;
            _arcHeading0 = Heading;
            _sLen = 0f;
            Claim(sweepS0, sweepS1, sweepLo, sweepHi);
            _next = null;
            _via = null;
            _committed = false;
            return true;
        }

        /// <summary>Is the arc itself free: room to swing before the junction, nothing
        /// standing on the sweep, nothing coming down either band in the time it takes,
        /// nobody behind in the band we end up in. Everything the turn asks of the ROAD,
        /// with nothing said about the driver.</summary>
        string SweepWhy = "";

        bool SweepClear(float r, int side, out float sweepS0, out float sweepS1,
                        out float sweepLo, out float sweepHi, bool timing = true)
        {
            SweepWhy = "";
            sweepLo = -r - HalfWide - 0.3f;
            sweepHi = r + HalfWide + 0.3f;
            float s0 = S;
            sweepS0 = Mathf.Min(s0 - Heading * HalfLen, s0 + Heading * (r + HalfLen + 0.5f));
            sweepS1 = Mathf.Max(s0 - Heading * HalfLen, s0 + Heading * (r + HalfLen + 0.5f));
            var node = Road.NodeAhead(Heading);
            float endS = Road.EndS(Heading);
            if ((endS - (s0 + Heading * (r + HalfLen + 1f))) * Heading < (node != null ? node.StopSetback : 0f)) { SweepWhy = "too near the junction"; return false; }
            if (Road.Busy(_occ, sweepS0, sweepS1, sweepLo, sweepHi)) { SweepWhy = DriveTrace.On ? $"something on the arc (s[{sweepS0:F1},{sweepS1:F1}] d[{sweepLo:F1},{sweepHi:F1}])" : "something on the arc"; return false; }
            if (!PhysicalArcClear(r, side)) { SweepWhy = "body crosses the turning arc"; return false; }
            if (!timing) return true;   // the ROOM is there; whether the traffic gives him the moment is another question
            float seconds = (Mathf.PI * r + Axle) / Mathf.Max(1f, UTurnArcSpeed()) + Profile.OncomingMargin;
            foreach (var other in Road.Occupants)
            {
                if (other.Who == this || !other.Overlaps(sweepLo, sweepHi) || Mathf.Abs(other.Vel) < .5f) continue;
                // Measure arrival at the sweep in HIS direction. A car that already
                // passed us and is driving away cannot close this gap.
                float gap = other.Vel > 0f ? sweepS0 - other.S1 : other.S0 - sweepS1;
                if (gap < 0f) continue; // intersecting bodies/claims were refused above
                float speed = Mathf.Abs(other.Vel);
                var follower = other.Car;
                if (other.Heading == Heading && other.Vel * Heading > 0f &&
                    other.BodyOverlaps(D - HalfWide, D + HalfWide) && follower != null &&
                    follower.CanEaseTraffic && !follower.Sliding)
                {
                    // A following car yields to our published sweep using the same
                    // brakes and gap it already uses to follow us. It is not head-on.
                    float stop = speed * speed / (2f * Mathf.Max(.1f, follower.Brake)) +
                        speed * .3f + follower.Profile.FollowGap;
                    if (gap >= stop) continue;
                    SweepWhy = "follower needs braking room"; return false;
                }
                if (gap < speed * seconds) { SweepWhy = "traffic approaching the turning arc"; return false; }
            }
            return true;
        }

        /// <summary>Room for a turn here, optionally including approaching traffic.
        /// While seeking a gap, the driver slows for a physically clear arc; admission
        /// still requires enough time for oncoming traffic and braking room behind.</summary>
        bool UTurnAvailable(bool timing = true)
        {
            if (Road == null || !Road.TwoWay || Via != null || _man != Manoeuvre.None) return false;
            if (!Profile.UTurnsInRoad || Road.MedianHalf > 0f) return false;
            float r = UTurnRadius();
            int side = D >= 0f ? 1 : -1;
            // off the radius he must cross to it first, which is itself done slowly
            if (Mathf.Abs(Mathf.Abs(D) - r) > 0.3f) return Road.Drivable(side * r, HalfWide);
            return SweepClear(r, side, out _, out _, out _, out _, timing);
        }

        bool _arcBacking;
        float _arcBlockedFor;

        void TickArc(float dt, float vCap)
        {
            float end = Mathf.PI + Axle / _arcR;
            float v = _arcBacking
                ? -Mathf.Min(2.5f, Mathf.Max(0f, ClearBehind() - .5f) / Mathf.Max(dt, .001f))
                : Mathf.Min(vCap, Profile.UTurnSpeed, Mathf.Sqrt(LateralG * _arcR));
            _want = v; _wantHard = false;
            Speed = Mathf.MoveTowards(Speed, v, Accel * dt);
            float previous = _arcAng;
            _arcAng = Mathf.Clamp(_arcAng + Speed * dt / _arcR, 0f, end);
            int belts = BeltHits;
            // A refused physical step must also refuse its angular progress.
            _placeArcBefore = previous;
            try { Place(dt); }
            finally { _placeArcBefore = float.NaN; }
            if (BeltHits != belts)
            {
                _arcBlockedFor += dt;
                // Traffic may enter after admission. Back along the known curve;
                // each reverse step still checks vehicle and pedestrian clearance.
                if (_arcBlockedFor > 3f) _arcBacking = true;
                return;
            }
            _arcBlockedFor = 0f;
            bool reversedOut = _arcBacking && _arcAng <= .0001f;
            if (!reversedOut && _arcAng < end) return;
            Heading = reversedOut ? _arcHeading0 : -_arcHeading0;
            S = reversedOut ? _arcS0 : _arcS0 - _arcHeading0 * Axle;
            D = (reversedOut ? _arcSide : -_arcSide) * _arcR;
            _arcBacking = false;
            _man = Manoeuvre.None;
            ClearClaim();
            var lane = Road.LaneFor(Heading, D);
            SetLane(lane);
            _laneD = lane != null ? lane.Offset : D;
            if (Mathf.Abs(D - _laneD) > .2f) Slide(_laneD, 10f);
            _next = null; _via = null; _committed = false;
            if (reversedOut)
            {
                Speed = 0f;
                _yieldUntil = RoadCarSimulation.Now + 2.5f;
                _holdFor = 2.5f;
            }
            if (_hasGoal) Replan();
        }

        // ------------------------------------------------------------------ reverse

        bool TryReverse(RoadOccupant blocker, float gap = 0f)
        {
            if (!Profile.Reverses || Via != null) return false;
            float room = ClearBehind();
            if (room < 3f) return false;
            _backedFor = blocker?.Who;
            _backedAt = RoadCarSimulation.Now;
            _jamLeader = blocker?.Who;
            // far enough back to swing right across the road if need be
            float wanted = Mathf.Clamp(SlideLength(5f, 0f) + 2.5f - Mathf.Max(0f, gap), 4f, 10f);
            _backLeft = Mathf.Min(wanted, room - 0.5f);
            _man = Manoeuvre.Reverse;
            Speed = 0f;
            float tailS = S - Heading * HalfLen;
            Claim(tailS, tailS - Heading * _backLeft, D - HalfWide, D + HalfWide);
            return true;
        }

        // Metres of free road straight behind the rear bumper, to ten.
        float ClearBehind(float reach = 10f)
        {
            float tailS = S - Heading * HalfLen;
            var o = Road.Behind(_occ, Heading, tailS, D - HalfWide - 0.2f, D + HalfWide + 0.2f, out float gap);
            float free = o != null ? Mathf.Min(reach, gap) : reach;
            // the end of the road behind us
            float back = (tailS - Road.EndS(-Heading)) * Heading;
            free = Mathf.Min(free, Mathf.Max(0f, back - 0.5f));
            var backDir = -_fwd;
            for (int list = 0; list < 2; list++)
                foreach (var body in list == 0 ? Behind(StreetTraffic.Bodies) : StreetTraffic.Walkers)
                {
                    var d = body - _pos;
                    d.y = 0f;
                    float behind = Vector3.Dot(d, backDir) - HalfLen;
                    if (behind < -0.5f || behind > free) continue;
                    if (Mathf.Abs(Vector3.Dot(d, Vector3.Cross(Vector3.up, _fwd))) > HalfWide + 0.6f) continue;
                    free = Mathf.Min(free, Mathf.Max(0f, behind - 1f));
                }
            return free;
        }

        // Every reverse yields to pedestrians, including during combat.
        static readonly List<Vector3> _behind = new List<Vector3>();

        static List<Vector3> Behind(List<StreetTraffic.Body> bodies)
        {
            _behind.Clear();
            for (int i = 0; i < bodies.Count; i++) _behind.Add(bodies[i].At);
            return _behind;
        }

        void TickReverse(float dt)
        {
            float room = ClearBehind();
            float step = 0f;
            if (room > 0.6f && _backLeft > 0.01f)
            {
                Speed = Mathf.MoveTowards(Speed, -2.5f, Accel * dt);
                step = Mathf.Min(-Speed * dt, Mathf.Min(_backLeft, room - 0.5f));
                S -= Heading * step;
                _backLeft -= step;
                if (_parkingRetreat) D = LateralValue(S);
            }
            else _backLeft = 0f;
            if (_backLeft > 0.01f && step > 0f) return;
            _backLeft = 0f;
            Speed = 0f;
            if (_parkReturnApproach)
            {
                _man = Manoeuvre.None;
                ClearClaim();
                _holdFor = 0f;
                return;
            }
            if (_parkingRetreat)
            {
                if ((S - _sFrom) * Heading > .02f) { FailParking(); return; }
                _parkingRetreat = false;
                D = _dFrom;
                _sLen = 0f;
                _man = Manoeuvre.None;
                ClearClaim();
                _holdFor = 0f;
                return;
            }
            _man = Manoeuvre.None;
            ClearClaim();
            _blockedFor = Profile.Patience + 1f;  // and another look from here at once
            _holdFor = 6f;                        // stood here looking for the gap, not creeping back up
        }

        float _holdFor;

        // ------------------------------------------------------------------ junctions

        void PlanNext(RoadNode node)
        {
            if (Net != null) Net.Prepare(node);
            RoadEdge straight = null;
            var lefts = _lefts; var rights = _rights;
            lefts.Clear(); rights.Clear();
            var lane = Lane;
            if (lane == null) { lane = Road.LaneFor(Heading, D); SetLane(lane); }
            if (lane == null) return;
            for (int i = 0; i < node.Outgoing.Count; i++)
            {
                var e = node.Outgoing[i];
                if (node.ConnectorFor(lane, e) == null) continue;
                // NOT INTO A TRAP. A road with no junction at its far end is only a way
                // on if the car can turn round in it - two-way, a driver who makes
                // turns, and LONG enough for the arc. The city keeps the odd stub (a
                // few metres of carriageway closed at the far end): a wanderer that
                // took one stopped at its end wanting nothing, could never be granted
                // the turn with the queue crowding its tail, and the junction behind
                // wedged shut for the rest of the run (car 156, 197 seconds, a quarter
                // queued). Such a road is left to the dead-end fallback below - taken
                // only when it is the only connector there is.
                if (e.Road.NodeAhead(e.Heading) == null &&
                    (!e.Road.TwoWay || !Profile.UTurnsInRoad ||
                     e.Road.Length < UTurnRadius() + HalfLen + 2f)) continue;
                float dot = Vector3.Dot(e.Dir, lane.Dir);
                if (dot > 0.5f)
                {
                    if (straight == null || (e.Start - lane.End).sqrMagnitude < (straight.Start - lane.End).sqrMagnitude) straight = e;
                }
                else if (dot < -0.5f) { /* the dead-end turn-round: taken only when nothing else */ }
                else if (Vector3.Cross(lane.Dir, e.Dir).y > 0f) rights.Add(e);
                else lefts.Add(e);
            }
            // The way on he had chosen is full and has been for a while: a man with
            // nowhere particular to be turns instead. Junctions gridlock when four
            // queues all wait on each other's full exits, and one car choosing another
            // way out is what breaks the ring - which is what a driver does anyway.
            if (_exitFull != null && Route == null)
            {
                if (straight == _exitFull) straight = null;
                lefts.Remove(_exitFull);
                rights.Remove(_exitFull);
            }
            // The route is a table of "from this lane, take that one". A car that comes
            // off the table - a turn taken to get round something stopped, a junction
            // whose exit was full, a set-down that put it on a street the table never
            // covered - would go on picking at random with its goal still set: the lab
            // watched one drive a quarter of the city for two minutes and never come
            // within forty metres of where it had been sent. Off the table, the table is
            // drawn again from where the car actually is.
            // An order issued during a crossing has no Road to plan from yet.
            // Build that missing route on the next approach as well as repairing
            // an existing table, or the car wanders forever with a live goal.
            if (_hasGoal && _goalLane != null && Net != null &&
                !PlansTurnRoundToGoal() && (Route == null || !Route.ContainsKey(lane)))
            {
                RouteShift ??= new Dictionary<RoadEdge, RoadEdge>();
                Route = LaneNet.RouteToward(Net.Edges, _goalLane, out _, RouteShift);
            }

            RoadEdge next = null;
            if (Route != null && Route.TryGetValue(lane, out var toward) && toward != null && node.ConnectorFor(lane, toward) != null) next = toward;
            else if (straight != null || lefts.Count > 0 || rights.Count > 0) next = PickNext(straight, lefts, rights);
            if (next == null)
            {
                // the turn-round at a dead end, or anything at all
                foreach (var c in node.Connectors) if (c.From == lane) { next = c.To; break; }
            }
            _next = next;
            if (next == null) return;
            _via = node.ConnectorFor(lane, next);
            _turn = _via != null ? _via.Kind : Turn.Straight;
            _committed = false;
            _heldAtLine = 0f;
        }

        /// <summary>The exit that was refused for want of room on the far side.</summary>
        RoadEdge _exitFull;
        float _fullFor;

        static readonly List<RoadEdge> _lefts = new List<RoadEdge>(), _rights = new List<RoadEdge>();

        /// <summary>The default driver: random wander biased to straight, then right;
        /// frightened, whichever way leads farthest from the shooting. A derived
        /// driver substitutes a routed choice.</summary>
        protected virtual RoadEdge PickNext(RoadEdge straight, List<RoadEdge> lefts, List<RoadEdge> rights)
        {
            if (!Fearless && _nerve.Frightened)
            {
                RoadEdge best = null;
                float bestD = -1f;
                void Consider(RoadEdge e)
                {
                    if (e == null) return;
                    float d = (e.End - _nerve.Threat).sqrMagnitude;
                    if (d > bestD) { bestD = d; best = e; }
                }
                Consider(straight);
                for (int i = 0; i < lefts.Count; i++) Consider(lefts[i]);
                for (int i = 0; i < rights.Count; i++) Consider(rights[i]);
                if (best != null) return best;
            }
            // THE STREET A JOB HAS BEEN ORDERED ON IS LEFT TO IT. A wanderer with no
            // reason to be anywhere takes the turning that keeps him out of it - or, if
            // he is already inside, the one that gets him out soonest. He is told
            // nothing else: he does not brake, he does not hurry, and the moment the
            // claim lapses he wanders it like any other street.
            if (Profile.Wanders && StreetTraffic.QuietOpen)
            {
                RoadEdge away = null;
                float awayD = -1f;
                bool anyClear = false;
                void Weigh(RoadEdge e)
                {
                    if (e == null) return;
                    // the WHOLE stretch, not where it ends: an avenue that runs through
                    // the middle of the claim and out the far side ends further from the
                    // fight than anything else at the junction, and judged by its end
                    // alone it was the turning the traffic kept being sent down
                    bool through = StreetTraffic.CrossesQuiet(e.Start, e.End);
                    var d = e.End - StreetTraffic.QuietAt;
                    d.y = 0f;
                    float far = through ? -1f : d.magnitude;
                    if (!through) anyClear = true;
                    if (far > awayD) { awayD = far; away = e; }
                }
                Weigh(straight);
                for (int i = 0; i < lefts.Count; i++) Weigh(lefts[i]);
                for (int i = 0; i < rights.Count; i++) Weigh(rights[i]);
                // only when there IS somewhere else to go: a junction whose every exit
                // lies inside the claim is driven as usual, or the car stands at the
                // line for as long as the fight lasts
                if (away != null && anyClear) return away;
            }

            return TrafficDistribution.Choose(straight, lefts, rights, Random.value);
        }

        // A stopped approach can lose its permission after overshooting the line.
        // Its nose then blocks the car already turning, while that car's claim
        // prevents it going forward. Restore the stopping space physically; the
        // rear-clearance check includes pedestrians and the ordinary belt limits
        // the actual step, so an occupied queue behind us is waited for.
        bool YieldBackToLine(RoadNode node, float noseToEnd, float dt)
        {
            if (node == null || _committed || _halted || Sliding ||
                _man != Manoeuvre.None || Speed > .15f) return false;
            float over = node.StopSetback - noseToEnd;
            if (over <= (Speed < 0f ? .01f : .5f)) return false;
            float room = Mathf.Max(0f, ClearBehind() - .5f);
            if (room <= .001f) { Speed = 0f; return false; }
            Speed = Mathf.MoveTowards(Speed, -2.5f, Accel * dt);
            float step = Mathf.Min(-Speed * dt, Mathf.Min(over, room));
            if (step <= 0f) return false;
            float previousS = S, previousD = D;
            S -= Heading * step;
            _want = 0f;
            Why = "yielding back to the stop line";
            Place(dt, previousS, previousD);
            if (step >= over - .001f) Speed = 0f;
            UpdateOccupant();
            return true;
        }

        // May we go into the box now: the signal, the connectors in use, the room to
        // leave it on the far side.
        bool CanEnter(RoadNode node, float stopDist)
        {
            if (_via == null || _next == null) { Why = "no way on"; return false; }
            // just reversed out of this box, or standing in somebody this instant: not
            // back in until it is clear
            if (RoadCarSimulation.Now < _boxHoldUntil) { Why = "backed out: waiting for the box"; return false; }
            // A refused reverse is not an overlap. Treating every recent belt hit
            // as one seals a clear junction while the car keeps retrying backwards.
            if (_beltFor > 0.2f &&
                RoadSpace.Inside(this, _pos, _fwd, HalfLen, HalfWide, out _) != null)
            { Why = "something is standing in us"; return false; }
            // the lane we are leaving from: the connector wants us in it
            if (!Sliding && Mathf.Abs(D - _laneD) > 1.2f && _man != Manoeuvre.None) { Why = "off lane at the line"; return false; }
            // the toll: stop at the window, pay, and go when the arm is up. Asked here
            // and nowhere else, because to a driver it is the same question the light
            // asks - and asked FIRST, since no light on earth waves you past a barrier.
            if (node.Toll != null && !node.Toll.MayPass(this)) { Why = "toll"; return false; }

            // Reserve a responder's crossing early; independent movements still drain.
            if (YieldsToEmergencyAt(node))
            {
                Why = "emergency vehicle";
                return false;
            }

            var sig = node.Signal;
            if (sig != null)
            {
                bool green = sig.GreenFor(Lane.NorthSouth);
                bool yellow = sig.YellowFor(Lane.NorthSouth);
                if (green)
                {
                    // A boulevard turn lasts most of a phase. Feeding its tail at
                    // the end of every green can consume the next street's entire
                    // green, leaving that queue starved for minutes. Stop a car that
                    // can still brake until it has a usable crossing window.
                    float crossingSpeed = _via.UTurn ? Profile.UTurnSpeed
                        : _turn == Turn.Straight ? Cruise() : TurnSpeed(_via);
                    float crossingSeconds = (_via.Length + 2f * HalfLen +
                        Mathf.Max(0f, stopDist)) / Mathf.Max(1f, crossingSpeed) + 1f;
                    crossingSeconds = Mathf.Min(crossingSeconds, TrafficSignal.HalfCycle - 2f);
                    if (!Profile.EmergencyRightOfWay &&
                        sig.ClearanceRemaining(Lane.NorthSouth) < crossingSeconds &&
                        stopDist > Speed * Speed / (2f * Brake) + 2f)
                    { Why = "waiting for a full crossing window"; return false; }
                    if (!Profile.EmergencyRightOfWay && _turn == Turn.Left && OncomingPriority(node))
                    { Why = "left: oncoming"; return false; }
                }
                else if (yellow)
                {
                    if (!Profile.EmergencyRightOfWay && stopDist > Speed * Speed / (2f * Brake) + 2f)
                    { Why = "yellow"; return false; }
                }
                else
                {
                    if (!Profile.RunsRed) { Why = "red"; return false; }
                    // Non-emergency red runners require a deserted junction, not a predicted
                    // gap that can close after commitment. Responders reserve their crossing
                    // in advance but still respect physical occupants.
                    if (!Profile.EmergencyRightOfWay &&
                        (ConflictApproaching(node, 4f) || !BoxDeserted(node)))
                    { Why = "red: traffic"; return false; }
                }
            }
            else
            {
                // no lights: give way to what is already coming at the box on a crossing path
                if (!Profile.EmergencyRightOfWay && _turn == Turn.Left && OncomingPriority(node))
                { Why = "left: oncoming"; return false; }
                // THE TURN-ROUND GIVES WAY TO EVERYBODY COMING. Its half circle takes the
                // whole box (LaneNet), and the check below sees only cars already IN the
                // box: two that commit to it in the same second from two roads both find
                // it empty and meet in the middle (DEPOT-004 S2 seed 102, twice). The car
                // turning round is the one with time to spare, so it is the one that waits.
                if (!Profile.EmergencyRightOfWay && _via != null && _via.UTurn && ConflictApproaching(node, 3f))
                { Why = "u-turn: traffic"; return false; }
            }
            // Two lanes can feed conflicting turns throughout every green. After a
            // full cycle, leave a gap for the oldest stopped approach whose exit is
            // clear. Existing box occupants still finish before either car enters.
            if (!Profile.EmergencyRightOfWay &&
                (yieldingForWaitingTurn || Speed < 0.3f || stopDist > Speed * Speed / (2f * Brake) + 2f) &&
                YieldsToWaitingApproach(node))
            {
                // Once braking begins, the remaining distance approaches the braking
                // distance. Keep yielding until the waiting movement takes its turn.
                yieldingForWaitingTurn = true;
                Why = "giving a waiting turn its gap";
                return false;
            }
            yieldingForWaitingTurn = false;
            // the connectors in use
            for (int i = 0; i < node.Inside.Count; i++)
            {
                var o = node.Inside[i];
                if (o.Car == this || Deadlock.Ignores(o.Car)) continue;
                if (o.Via.From == _via.From && o.Via != _via)
                { Why = "box: previous turn clearing " + o.Car.Id; return false; }
                if (o.Via == _via || o.Via.From == _via.From)
                {
                    // An overtaker beside the queue merges onto the same connector
                    // origin. Longitudinal following alone cannot see that merge.
                    bool merging = Mathf.Abs(D - _laneD) > .5f ||
                        (o.Car.Via != null ? Mathf.Abs(o.Car._viaD0) > .5f :
                         Mathf.Abs(o.Car.D - o.Car._laneD) > .5f);
                    if (merging && (o.Car.Via != null ||
                        Vector3.Dot(o.Car.Position - Position, _fwd) > -.1f))
                    { Why = "box: merging behind " + o.Car.Id; return false; }

                    // following him across: room behind him (he may still be on the approach)
                    if (FollowBody(o.Car, _fwd, Vector3.Cross(Vector3.up, _fwd)) < 1f) { Why = "box: following " + o.Car.Id; return false; }
                    continue;
                }
                if (JunctionClearance.Conflicts(_via, this, o.Via, o.Car))
                { Deadlock.BlockedBy(o.Car); Why = "box: crossing " + o.Car.Id; return false; }
            }
            // room to leave the box: the far lane's start clear for our length and a gap
            var far = _next.Road;
            float farS0 = _next.S0;
            float need = 2f * HalfLen + Profile.FollowGap + 0.5f;
            float free = far.FreeAhead(null, _next.Heading, farS0, farS0 - _next.Heading * 0.1f,
                _next.Offset - HalfWide - 0.2f, _next.Offset + HalfWide + 0.2f, need + 1f);
            if (free >= 0f && free < need && !(_heldAtLine > 12f && free > 2f * HalfLen))
            {
                Why = "box: no room beyond";
                _exitFull = _next;   // the way on is full: a wanderer takes another
                return false;
            }
            Why = "";
            return true;
        }

        /// <summary>How early a responding police car reserves a conflicting movement.
        /// Four seconds at its actual top pace, plus a car length of warning, lets normal
        /// traffic brake at its usual rate instead of discovering the lightbar at the
        /// stop line. Seventy metres is the floor while the pickup is still accelerating
        /// or briefly held behind a queue.</summary>
        public static readonly float EmergencyPrioritySeconds = 4f;
        public static readonly float EmergencyPriorityMinimumRange = 70f;

        bool YieldsToWaitingApproach(RoadNode node)
        {
            // Compare fixed arrival times. Elapsed counters are updated car by
            // car: incrementing the first one could make the second yield back
            // to it in the same step, leaving both stopped through every green.
            if (_via == null || Lane == null || node.Signal == null) return false;
            for (int i = 0; i < node.Incoming.Count; i++)
            {
                var edge = node.Incoming[i];
                if (edge == Lane || edge.NorthSouth != Lane.NorthSouth) continue;
                for (int k = 0; k < edge.Cars.Count; k++)
                {
                    var other = edge.Cars[k];
                    if (other == this || other._committed || other.Parked || other._halted ||
                        other.Wrecked || other.Derelict || other.Sliding || other._next == null ||
                        other._via == null || Mathf.Abs(other.Speed) > 0.2f ||
                        other._heldAtLine < TrafficSignal.Cycle ||
                        (_heldAtLine > 0f && (other._waitingLineAt > _waitingLineAt + .001f ||
                        (Mathf.Abs(other._waitingLineAt - _waitingLineAt) <= .001f && other.Id > Id)))) continue;
                    float noseToEnd = edge.Length - other.Progress - other.HalfLen;
                    if (noseToEnd > node.StopSetback + 1.5f || noseToEnd < -1f ||
                        Mathf.Abs(other.D - edge.Offset) > 1f ||
                        !JunctionClearance.Conflicts(_via, this, other._via, other)) continue;
                    var exit = other._next;
                    float need = 2f * other.HalfLen + other.Profile.FollowGap + 0.5f;
                    float free = exit.Road.FreeAhead(null, exit.Heading, exit.S0,
                        exit.S0 - exit.Heading * 0.1f, exit.Offset - other.HalfWide - 0.2f,
                        exit.Offset + other.HalfWide + 0.2f, need + 1f);
                    if (free < 0f || free >= need) return true;
                }
            }
            return false;
        }

        bool YieldsToEmergencyAt(RoadNode node)
        {
            if (Profile.EmergencyRightOfWay || _via == null) return false;

            for (int i = 0; i < node.Incoming.Count; i++)
            {
                var edge = node.Incoming[i];
                for (int k = 0; k < edge.Cars.Count; k++)
                {
                    var response = edge.Cars[k];
                    if (response == this || response.Parked || response.Derelict || response.Wrecked ||
                        !response.Profile.EmergencyRightOfWay)
                        continue;

                    float distance = edge.Length - response.Progress - response.HalfLen;
                    // A stopped response still behind its queue (or at a kerb)
                    // cannot reserve every conflicting arm: those arms may be the
                    // very traffic its queue needs to drain. Reserve on approach
                    // again as soon as it moves, or reaches the actual stop line.
                    if (!response._committed && Mathf.Abs(response.Speed) < 0.2f &&
                        (distance > node.StopSetback + 1.5f ||
                         Mathf.Abs(response.D - edge.Offset) > 1.5f))
                        continue;
                    float range = Mathf.Max(EmergencyPriorityMinimumRange,
                        Mathf.Max(Mathf.Abs(response.Speed), response.TopSpeed) * EmergencyPrioritySeconds +
                        response.HalfLen);
                    if (distance > range) continue;

                    var his = response._via ??
                              (response._next != null ? node.ConnectorFor(edge, response._next) : null);
                    if (his != null)
                    {
                        // Following the same approach lets the road ahead empty. A route
                        // that the conflict table proves independent need not be stopped.
                        if (his.From == _via.From) continue;
                        if (!JunctionClearance.Conflicts(_via, this, his, response)) continue;
                    }
                    else if (edge == Lane)
                    {
                        // During the response car's first planning frame its connector
                        // may not exist yet. Do not stop the lane it needs to drain.
                        continue;
                    }

                    return true;
                }
            }
            return false;
        }

        // Junctions allow a brisker pace than the comfort limits used for parking
        // and lane changes. Approach braking, crossing time and the turn itself all
        // use this same pace, still bounded by the roads' cruising speeds.
        const float TurnPaceScale = 1.25f;
        const float TurnLateralScale = 1.75f;

        float TurnSpeed(Connector via)
        {
            float v = JunctionSpeed * TurnPaceScale;
            if (via != null && via.MinRadius < 1000f)
                v = Mathf.Min(v, Mathf.Sqrt(LateralG * TurnLateralScale * via.MinRadius));
            float ceiling = Cruise();
            var exit = via?.To;
            if (exit != null)
            {
                float exitCruise = exit.Road != null
                    ? Profile.CruiseOn(exit.Road.Class) * Machine.Top : TopSpeed;
                if (Profile.ObeysLimit) exitCruise = Mathf.Min(exitCruise, exit.SpeedLimit);
                ceiling = Mathf.Min(ceiling, exitCruise);
            }
            return Mathf.Min(ceiling, Mathf.Max(2.5f, v));
        }

        // Following through the box, by where the bodies actually are: anyone in the
        // box who left the same lane we did (on our connector or one diverging from
        // it - they run together at the start), and anyone just out of it on the lane
        // we are making for. His box projected on our heading: how far ahead his
        // tail is, and whether he is across our line at all.
        float BoxFollow(RoadNode node, float v)
        {
            if (_via == null) return v;
            var f = _fwd;
            var r = Vector3.Cross(Vector3.up, f);
            float mine = Via != null && _via.Length > 0.5f ? ViaS / _via.Length : 0f;
            for (int i = 0; i < node.Inside.Count; i++)
            {
                var o = node.Inside[i];
                if (o.Car == this || o.Car == null) continue;
                if (o.Via == _via || o.Via.From == _via.From)
                {
                    // Following and crossing use the same strict order: progress, priority, id.
                    // Projecting both noses independently around a bend can make each car
                    // mistake the other for its leader.
                    if (YieldsInBox(o, mine)) v = Mathf.Min(v, FollowBody(o.Car, f, r));
                    continue;
                }
                // A late arrival unable to stop can share a conflicting crossing.
                // Sequence these cars by progress, priority and id; never yield both ways.
                if (!JunctionClearance.Conflicts(_via, this, o.Via, o.Car)) continue;
                if (!YieldsInBox(o, mine)) continue;
                float vy = FollowBody(o.Car, f, r);
                if (vy < v) { v = vy; _gaveWay = o.Car; if (v < 0.5f) Why = "box: giving way to " + o.Car.Id; }
            }
            if (_next != null)
                for (int i = 0; i < _next.Cars.Count; i++)
                {
                    var c = _next.Cars[i];
                    if (c == this || c.Via != null || c.Progress > 14f) continue;
                    v = Mathf.Min(v, FollowBody(c, f, r));
                }
            return v;
        }

        /// <summary>A transitive order for following and crossing: progress bucket,
        /// profile priority, then id. Pairwise epsilon comparisons can form cycles.</summary>
        bool YieldsInBox(NodeOccupant o, float mine)
        {
            float his = o.S > 0f && o.Via.Length > 0.5f ? o.S / o.Via.Length : 0f;
            int ourBucket = Mathf.FloorToInt(mine * 50f), hisBucket = Mathf.FloorToInt(his * 50f);
            return hisBucket > ourBucket ||
                   (hisBucket == ourBucket &&
                    (o.Car.Profile.Priority > Profile.Priority ||
                     (o.Car.Profile.Priority == Profile.Priority && o.Car.Id < Id)));
        }

        float FollowBody(RoadCar c, Vector3 f, Vector3 r)
        {
            if (Deadlock.Ignores(c)) return float.MaxValue;
            var d = c._pos - _pos;
            d.y = 0f;
            float along = Vector3.Dot(d, f);
            if (along <= 0f) return float.MaxValue;
            float side = Vector3.Dot(d, r);
            var cf = c._fwd;
            float hisAlong = Mathf.Abs(Vector3.Dot(cf, f)) * c.HalfLen + Mathf.Abs(Vector3.Dot(cf, r)) * c.HalfWide;
            float hisAcross = Mathf.Abs(Vector3.Dot(cf, r)) * c.HalfLen + Mathf.Abs(Vector3.Dot(cf, f)) * c.HalfWide;
            // one of us turning: his corners swing - the whole of him counts, both ways
            if ((Via != null && Via.Kind != Turn.Straight) || (c.Via != null && c.Via.Kind != Turn.Straight))
            { hisAlong = Mathf.Max(hisAlong, c.HalfLen * 0.8f); hisAcross = Mathf.Max(hisAcross, c.HalfLen * 0.8f); }
            if (Mathf.Abs(side) > HalfWide + hisAcross + 0.3f) return float.MaxValue;
            float gap = along - HalfLen - hisAlong;
            float his = Vector3.Dot(cf, f) * Mathf.Abs(c.Speed);
            float speed = Follow(his, gap);
            if (speed < .5f) Deadlock.BlockedBy(c);
            return speed;
        }

        // Somebody coming the other way, straight across, within reach of the box.
        bool OncomingPriority(RoadNode node)
        {
            for (int i = 0; i < node.Incoming.Count; i++)
            {
                var e = node.Incoming[i];
                if (Vector3.Dot(e.Dir, Lane.Dir) > -0.5f) continue;
                for (int k = 0; k < e.Cars.Count; k++)
                {
                    var c = e.Cars[k];
                    if (c == this || c.Via != null || c._turn != Turn.Straight) continue;
                    if (c._via != null && !JunctionClearance.Conflicts(_via, this, c._via, c)) continue;
                    if (c.Speed < 0.3f && c.Profile.Priority <= Profile.Priority && c._heldAtLine > 3f) continue;
                    float dist = e.Length - c.Progress;
                    // the longer we have waited, the smaller the gap we take (nobody waits for ever)
                    float reach = 28f * Mathf.Clamp(1f - _heldAtLine / 25f, 0.35f, 1f);
                    if (dist < reach) return true;
                }
            }
            return false;
        }

        // Anyone on an approach whose movement would cross ours, arriving within seconds.
        /// <summary>Metres of every other approach to a junction that must be empty
        /// before a red is run across it. A whole street's worth: far enough that
        /// nothing on any arm can reach the box while we are in it, whatever its
        /// intended turn and whatever the belt would have made of the meeting.</summary>
        public static float RedRunClear = 45f;

        /// <summary>Is there NOBODY on any other arm of this junction, moving or not,
        /// inside <see cref="RedRunClear"/>? Deliberately blunter than
        /// <see cref="ConflictApproaching"/>: that one asks whether a particular car's
        /// line crosses ours and when it is due, and both halves of that go stale
        /// between committing and arriving. This asks whether the junction is DESERTED,
        /// which does not go stale in the couple of seconds that matter and is the only
        /// condition under which jumping a red costs the city nothing.</summary>
        bool BoxDeserted(RoadNode node)
        {
            if (node.Inside.Count > 0) return false;
            for (int i = 0; i < node.Incoming.Count; i++)
            {
                var e = node.Incoming[i];
                if (e == Lane) continue;
                for (int k = 0; k < e.Cars.Count; k++)
                {
                    var c = e.Cars[k];
                    if (c == this || c.Parked) continue;
                    if (e.Length - c.Progress - c.HalfLen < RedRunClear) return false;
                }
            }
            return true;
        }

        bool ConflictApproaching(RoadNode node, float seconds)
        {
            for (int i = 0; i < node.Incoming.Count; i++)
            {
                var e = node.Incoming[i];
                if (e == Lane) continue;
                for (int k = 0; k < e.Cars.Count; k++)
                {
                    var c = e.Cars[k];
                    if (c == this || c.Via != null) continue;
                    float dist = e.Length - c.Progress - c.HalfLen;
                    if (dist > Mathf.Max(c.Speed, 4f) * seconds + 4f) continue;
                    var his = c._via ?? (c._next != null ? node.ConnectorFor(e, c._next) : null);
                    if (JunctionClearance.Conflicts(_via, this, his, c)) return true;
                }
            }
            return false;
        }

        void EnterBox(RoadNode node)
        {
            if (Derelict) { _committed = true; return; }   // a wreck claims nothing: it is driven round
            // Keep the previous junction until the tail clears it. Claiming two boxes
            // or releasing the first early gives other drivers contradictory admission.
            if (_inNode != null && _nodeOf != node) return;
            _committed = true;
            if (_inNode != null) return;   // this one is already ours
            _inNode = new NodeOccupant { Car = this, Via = _via, S = -1f };
            _nodeOf = node;
            _boxLeft = false;
            // Going in while somebody else is crossing it, and the table says their line
            // and ours never meet: either it is right and they pass each other, or this
            // is the pair the belt is about to have to separate. Said out loud, with both
            // lines named, because it cannot be read back from anything else.
            if (DriveTrace.On)
                for (int i = 0; i < node.Inside.Count; i++)
                {
                    var o = node.Inside[i];
                    if (o.Car == this || o.Car == null || o.Via == _via || o.Via.From == _via.From) continue;
                    if (JunctionClearance.Conflicts(_via, this, o.Via, o.Car)) continue;
                    if (DriveTrace.On)
                        DriveTrace.Event("man", "car " + Id,
                            $"in with car {o.Car.Id} ({Line(o.Via)}) - the table says {Line(_via)} does not meet it",
                            ManFields());
                }
            node.Inside.Add(_inNode);
            // The hold must be on the road we are ACTUALLY leaving by. A plan thought
            // again between taking one and arriving here - the route redrawn, another
            // exit picked, in the same frame - leaves a hold on a road we are no longer
            // bound for, and coming out of the box the car takes THAT as its place on
            // the street: it then stands in a lane it is not in anybody's list for, and
            // the first car down that lane drives into it at full speed. (The belt
            // caught one at 9 m/s, bumper to bumper, in the run that found this.)
            if (_occNext != null && (_next == null || _occNext.Road != _next.Road)) DropNext();
            // the lane beyond, claimed from its start so its traffic keeps off our exit
            if (_occNext == null && _next != null)
            {
                _occNext = NewOccupant(_next.Road);
                float toEdge = Road != null ? (Road.EndS(Heading) - (S + Heading * HalfLen)) * Heading : 0f;
                RefreshNextOccupant(-(Mathf.Max(0f, toEdge) + (_via != null ? _via.Length : 10f)) - HalfLen);
            }
        }

        void RefreshNextOccupant(float progressOnNext)
        {
            if (_occNext == null || _next == null) return;
            var o = _occNext;
            float s = _next.RoadS(progressOnNext);
            int h = _next.Heading;
            o.BodyS0 = Mathf.Min(s - h * HalfLen, s + h * HalfLen);
            o.BodyS1 = Mathf.Max(s - h * HalfLen, s + h * HalfLen);
            o.BodyD0 = _next.Offset - HalfWide;
            o.BodyD1 = _next.Offset + HalfWide;
            o.S0 = o.BodyS0; o.S1 = o.BodyS1; o.D0 = o.BodyD0; o.D1 = o.BodyD1;
            o.Vel = Mathf.Abs(Speed) * h;
            o.Slowing = _want < Speed - 0.05f ? (_wantHard ? HardBrake : Brake) : 0f;
            o.Heading = h;
            o.Parked = Parked || Derelict;
        }

        void EnterNode(RoadNode node, float overshoot)
        {
            if (_via == null) return;
            // Road offsets use the carriageway's canonical right; connector
            // offsets use the direction of travel. Reverse headings invert it.
            _viaD0 = (D - _laneD) * Heading;
            Via = _via;
            ViaS = overshoot;
            if (_occ != null) { _occ.Road.Occupants.Remove(_occ); _occ = null; }
            Road = null;
            _sLen = 0f;
            ClearClaim();
            // The claim must be on THIS junction. Still holding the last one - the tail
            // not yet clear of it when the next was reached - the old entry was written
            // to instead, and the car crossed this box invisible to everybody planning
            // through it: two cars on crossing lines, and the belt refusing both their
            // steps for minutes at a time. Whatever is held, it is given up here and the
            // box we are actually in is claimed.
            if (_inNode != null && _nodeOf != node)
            {
                if (DriveTrace.On)
                    DriveTrace.Event("man", "car " + Id, "crossed a box on the last one's claim", ManFields());
                LeaveBox();
            }
            if (_inNode == null) EnterBox(node);
            if (_inNode != null) _inNode.S = ViaS;
        }

        /// <summary>Metres that count as having made ground while reversing.</summary>
        const float BackOutMoved = 0.5f;

        float _backOutFor, _boxSaidAt, _boxStillFor;

        /// <summary>Seconds before a near-exit stall may crawl out, still respecting
        /// pedestrians and the physical guard.</summary>
        const float BoxCrawlAfter = 8f;

        /// <summary>The pace it comes out at - walking speed, so it is a car easing out of
        /// a junction and not a car making a break for it.</summary>
        const float BoxCrawl = 1.5f;

        /// <summary>Seconds the crawl gets to prove itself before the box is given up
        /// altogether.</summary>
        const float BoxCrawlGiveUp = 3f;

        float _crawlFor;
        Vector3 _crawlFrom;

        void TickNode(float dt)
        {
            var via = Via;
            var previousViaS = ViaS;


            // A stalled crossing normally backs along its driven connector.
            // A confirmed reciprocal wait may instead receive a pair-scoped escape;
            // neither recovery may discard the physical curve or bypass a third body.
            _wedgedFor = RoadCarSimulation.Now - _beltAt < BeltMemory && Mathf.Abs(Speed) < 0.15f
                ? _wedgedFor + dt : 0f;
            bool wedgedInBox = _wedgedFor > 1.5f;
            // Trace persistent stalls with their physical and planning causes.
            _boxStillFor = Mathf.Abs(Speed) < 0.15f ? _boxStillFor + dt : 0f;
            if (DriveTrace.On && _boxStillFor > 4f && RoadCarSimulation.Now >= _boxSaidAt)
            {
                _boxSaidAt = RoadCarSimulation.Now + 1f;
                DriveTrace.Event("man", "car " + Id,
                    $"standing in a box: still {_boxStillFor:F1}s wedged {_wedgedFor:F1}s belt {_beltFor:F1}s " +
                    $"stuck {_boxStuck:F1}s viaS {ViaS:F1}/{(_via != null ? _via.Length : 0f):F1} " +
                    $"why '{Why}'", ManFields());
            }
            // A prolonged blocked exit can form the same cycle as physical contact.
            // Normal recovery reverses along the existing connector.
            _boxStuck = wedgedInBox || _heldInBox > 20f ||
                        (_gaveWay != null && !_gaveWay.Parked && Mathf.Abs(Speed) < 0.15f)
                ? _boxStuck + dt : 0f;
            _gaveWay = null;
            foreach (var occupant in via.Node.Inside)
                if (occupant.Car != this && occupant.Car.Via != null &&
                    occupant.Via.From == via.From && occupant.Car._backOutFor > 0f &&
                    occupant.Car.ViaS > ViaS + .1f)
                { _backOutFor = Mathf.Max(.01f, _backOutFor); break; }
            if (!Deadlock.Active && !Deadlock.Mutual &&
                (_backOutFor > 0f || _boxStuck > (wedgedInBox ? 1.5f : 3f)) && _man != Manoeuvre.UTurn)
            {
                float back = Mathf.Min(2.5f * dt, ViaS);
                Speed = 0f;
                _want = 0f;
                if (back > 0.001f)
                {
                    // Reverse along the same connector; wait for rear clearance
                    // while the following queue receives the request to make room.
                    _backOutFor += dt;
                    // Ask for physical rear clearance before committing a reverse.
                    // Keep the reverse latched while its queue makes room.
                    ViaS -= back;
                    Pose(out var backPosition, out var backForward);
                    ViaS = previousViaS;
                    RoadSpace.Advance(this, Position, backPosition, backForward,
                        HalfLen, HalfWide, out var reverseBlocker);
                    if (reverseBlocker != null)
                    { Why = "box: waiting for room to reverse"; return; }
                    ViaS -= back;
                    if (_inNode != null) _inNode.S = ViaS;
                    RefreshNextOccupant(ViaS - via.Length);
                    Why = "box: backing out";
                    PlaceInNode(dt, previousViaS);
                    return;
                }
                BackOutOfBox(dt);
                return;
            }
            _backOutFor = 0f;

            float remaining = via.Length - ViaS;
            RefreshNextOccupant(ViaS - via.Length);
            if (_inNode != null) _inNode.S = ViaS;

            float v = via.UTurn ? Profile.UTurnSpeed : _turn == Turn.Straight
                ? Mathf.Min(Cruise(), Profile.ObeysLimit ? _next.SpeedLimit : TopSpeed) : TurnSpeed(via);
            v = Mathf.Min(v, ExitStopSpeed(remaining));
            // the cars ahead of us through the box, and just out of it
            float vb = BoxFollow(via.Node, v);
            if (vb < v) { v = vb; if (v < 0.5f) Why = "box: following"; }
            // and on the lane beyond, from where we will come out
            var far = _next.Road;
            float farNose = _next.RoadS(ViaS - via.Length) + _next.Heading * HalfLen;
            float farTail = farNose - _next.Heading * 2f * HalfLen;
            var lead = far.Ahead(_occNext, _next.Heading, farNose, farTail, _next.Offset - HalfWide - SideAir, _next.Offset + HalfWide + SideAir, out float fgap,
                Deadlock.Active ? (_escapeLeaderFilter ??= BlocksEscapePath) : null);
            // A CLAIM IS NOT A CAR. The lane beyond is claimed ahead of time by whoever
            // is coming into it - off its approach, or across this very box on a merging
            // way - and that claim stopped us dead in the middle of the junction: a
            // police car straight through braked to nought for the claim of a car
            // turning in BEHIND it, and that car, following it across, drove into its
            // tail (DEPOT-004 S2 seed 103, 31 refusals). A body already on the road we
            // come out into is a lead; a claim from a car not on that road yet is the
            // box's business to sequence, not ours to wait for.
            if (lead != null && lead.Car != null && !lead.Parked && lead.Car.Road != far)
            {
                lead = null;
                fgap = float.MaxValue;
            }
            // A stopped body beyond the exit may be passed once on the road.
            // After a prolonged wait, accept a tighter gap, but never emerge inside it.
            // A stationary queue tail can cause the same cycle as a parked obstacle.
            bool stood = lead != null && (lead.Parked ||
                         (lead.Car != null && (lead.Car.Derelict || lead.Car.RoadSpeed < 0.15f)));
            _heldInBox = stood ? _heldInBox + dt : 0f;
            float wantsGap = _heldInBox > 12f ? HalfLen + 1f : 2f * HalfLen + 2f;
            bool standing = stood && fgap > wantsGap;
            if (lead != null && standing)
            {
                // out of the box and stopped short of it ON THE ROAD, where it can be
                // driven round - not stopped across the junction, where nobody can pass
                v = Mathf.Min(v, Allowed(0f, fgap - HalfLen - 1.2f));
            }
            else if (lead != null)
            {
                float vl = Mathf.Max(0f, lead.Vel * _next.Heading);
                if (lead.Vel * _next.Heading < -0.5f) vl = 0f;
                v = Mathf.Min(v, Follow(vl, fgap, lead.Slowing));
                if (v < .5f) Deadlock.BlockedBy(lead.Car);
                if (v < 0.5f) Why = DriveTrace.On
                    ? "box: far lane " + (lead.Car != null ? "car " + lead.Car.Id : "static") + $" gap {fgap:F1} his s[{lead.S0:F1},{lead.S1:F1}] d[{lead.D0:F1},{lead.D1:F1}] farNose {farNose:F1}"
                    : "box: far lane";
            }
            // After a prolonged near-exit stall, allow a crawl if the far lane has room.
            // This floor precedes pedestrian and physical checks, and must not override
            // the pair escape speed cap. An unsuccessful crawl requests a real reverse.
            if (_boxStillFor > BoxCrawlAfter && remaining < 2f * HalfLen && fgap > HalfLen + 1f &&
                _man != Manoeuvre.UTurn)
            {
                // If forward recovery cannot progress, request a physical reverse.
                if (_crawlFor <= 0f) _crawlFrom = Position;
                _crawlFor += dt;
                // the same refusal test as the back-out: a crawl held up by traffic
                // creeping ahead of it is a queue, not a stall
                if (_crawlFor > BoxCrawlGiveUp && RoadCarSimulation.Now - _beltAt < BeltMemory &&
                    (Position - _crawlFrom).sqrMagnitude < BackOutMoved * BackOutMoved)
                {
                    if (DriveTrace.On)
                        DriveTrace.Event("man", "car " + Id,
                            $"crawling out made {(Position - _crawlFrom).magnitude:F2} m in " +
                            $"{_crawlFor:F1}s - requesting a reverse", ManFields());
                    BackOutOfBox(dt);
                    return;
                }
                v = Mathf.Max(v, BoxCrawl);
            }
            else _crawlFor = 0f;

            v = Mathf.Min(v, WalkersAhead(StreetTraffic.Walkers));
            v = Mathf.Min(v, BodiesAhead(dt));
            bool hard = false;
            if (!Fearless) v = Mathf.Min(v, _nerve.Limit(_pos, _fwd, Brake, out hard));
            v = LimitTarget(Deadlock.Active ? Mathf.Min(v, RoadDeadlock.Pace) : v);
            if (_halted) { v = 0f; if (_haltHard) hard = true; }
            InQueue = v < 0.5f;
            _want = v; _wantHard = hard;
            Speed = Mathf.MoveTowards(Speed, Mathf.Max(0f, v), (v < Speed ? (hard ? HardBrake : Brake) : Accel) * dt);
            ViaS += Speed * dt;
            PlaceInNode(dt, previousViaS);
            if (ViaS >= via.Length)
            {
                float over = ViaS - via.Length;
                var lane = _next;
                Road = lane.Road;
                Heading = lane.Heading;
                S = lane.RoadS(over);
                D = lane.Offset;
                _laneD = D;
                _tailVia = via;
                _tailViaEndS = lane.RoadS(0f);
                SetLane(lane);
                // and the same rule where it matters most: the car's place on the street
                // it comes out onto is a place in THAT street's list, never another's
                if (_occNext != null && _occNext.Road != Road) DropNext();
                _occ = _occNext ?? NewOccupant(Road);
                _occNext = null;
                _boxEntryS = S;
                _boxLeft = true;
                Via = null;
                _next = null;
                _via = null;
                _committed = false;
                _viaD0 = 0f;
                if (_replanOnExit) Replan();
                return;
            }
        }

        /// <summary>Off the box altogether, back onto the lane we came in on, and the
        /// junction is somebody else's again. The car stands where it stood when it
        /// crossed the line, its claim given back, its way through thought again from
        /// scratch - which is what a driver who has backed out of a junction does.</summary>
        void BackOutOfBox(float dt)
        {
            var lane = _via != null ? _via.From : null;
            if (lane == null) { _boxStuck = 0f; return; }
            // Complete the physical reverse before changing road membership.
            if (ViaS > .001f) { _backOutFor = Mathf.Max(.01f, _backOutFor); return; }
            var offset = lane.Offset + _viaD0 * lane.Heading;
            LeaveBox();
            DropNext();
            Road = lane.Road;
            Heading = lane.Heading;
            D = offset;
            _laneD = lane.Offset;
            S = lane.RoadS(lane.Length);
            SetLane(lane);
            _occ ??= NewOccupant(Road);
            Via = null; ViaS = 0f; _via = null; _next = null;
            _committed = false; _boxLeft = false; _sLen = 0f;
            _viaD0 = 0f; _tailVia = null;
            Speed = 0f; _want = 0f; _boxStuck = 0f; _backOutFor = 0f;
            _crawlFor = 0f; _boxStillFor = 0f; _heldInBox = 0f;
            _boxEntryS = S;
            _boxHoldUntil = RoadCarSimulation.Now + BoxHold;
            Why = "physically backed out of the box";
            UpdateOccupant();
        }

        // The tail out of the box: off the node's list.
        void TickBoxExit()
        {
            if (_inNode == null || Road == null || !_boxLeft) return;
            // HOW FAR FROM THE BOX, not how far ON from it. Measured along the way the
            // car was going, a car that turns round the moment it is out - a U-turn in
            // the road, a reverse - counts backwards for ever and never clears the
            // junction it is standing well clear of. It then holds that box against
            // everybody for the rest of the run, and holds it under the name of the turn
            // it made half a minute ago, so two cars are let into it on lines that cross.
            if (Mathf.Abs(S - _boxEntryS) <= HalfLen + 0.8f) return;
            LeaveBox();
            // the stop that was asked for while we were crossing: here, clear of the box
            if (_haltWhenClear)
            {
                var keepGoal = _keepGoalWhenHaltClear;
                _haltWhenClear = false;
                _keepGoalWhenHaltClear = false;
                if (keepGoal) BrakeForBarricade();
                else Halt(_haltHard);
            }
        }

        // ------------------------------------------------------------------ people

        // The speed that stops short of the nearest person stood in the car's way
        // (within a car's width of its line, up to fourteen metres on).
        float _bodyHeld;   // seconds stood still for a man lying in the road

        /// <summary>The cap a body in the road puts on us. A BODY DOES NOT GET UP.
        /// Stopping for a man in the road is right; standing behind him until the scene
        /// is closed is not - a car stood 248 seconds behind one in the run that found
        /// this, with the whole street queued behind the car and the lights going green
        /// and red over an empty junction. A few seconds of it and the driver edges
        /// past at walking pace, which is what a driver does.</summary>
        float BodiesAhead(float dt)
        {
            float cap = BodiesAhead();
            _bodyHeld = cap < 0.5f ? _bodyHeld + dt : 0f;
            return _bodyHeld > 6f ? Mathf.Max(cap, 1.5f) : cap;
        }

        /// <summary>The same reach as WalkersAhead, over the men whose OWNER matters:
        /// one this vehicle will not give way to is not in its way (GivesWayTo).</summary>
        float BodiesAhead()
        {
            var people = StreetTraffic.Bodies;
            if (people.Count == 0) return float.MaxValue;
            var f = _fwd;
            var r = Vector3.Cross(Vector3.up, f);
            float best = float.MaxValue;
            for (int i = 0; i < people.Count; i++)
            {
                if (!GivesWayTo(people[i].Faction)) continue;
                var d = people[i].At - _pos;
                d.y = 0f;
                float ahead = Vector3.Dot(d, f);
                if (ahead < 0f || ahead > 14f) continue;
                if (Mathf.Abs(Vector3.Dot(d, r)) > 1.6f) continue;
                best = Mathf.Min(best, Allowed(0f, ahead - HalfLen - 1.5f));
            }
            return best;
        }

        /// <summary>Whether this vehicle brakes for a man of that faction stood in front
        /// of it. Everything brakes for everybody, and the one thing that does not is a
        /// crew's car with a fight on (CrewCar.GivesWayTo) - see the run-down.</summary>
        protected virtual bool GivesWayTo(int faction) => true;

        float WalkersAhead(List<Vector3> people)
        {
            if (people.Count == 0) return float.MaxValue;
            var p = _pos;
            var f = _fwd;
            var r = Vector3.Cross(Vector3.up, f);
            float best = float.MaxValue;
            for (int i = 0; i < people.Count; i++)
            {
                var d = people[i] - p;
                d.y = 0f;
                float ahead = Vector3.Dot(d, f);
                if (ahead < 0f || ahead > 14f) continue;
                if (Mathf.Abs(Vector3.Dot(d, r)) > 1.6f) continue;
                best = Mathf.Min(best, Allowed(0f, ahead - HalfLen - 1.5f));
            }
            return best;
        }

        // ------------------------------------------------------------------ open ground

        Vector3? _freeGoal;

        /// <summary>Off any road: drive a straight line to this point and stop.</summary>
        public void GoFree(Vector3 point)
        {
            ClearParkingFailure();
            _parkPlanReady = false;
            _parkingRetreat = false;
            _freeGoal = point;
            _halted = false;
            _hasGoal = false;
            Route = null;
            _turnFirst = false;
        }

        void TickFree(float dt)
        {
            if (!_freeGoal.HasValue || _halted) { Speed = Mathf.MoveTowards(Speed, 0f, (_haltHard ? HardBrake : Brake) * dt); }
            else
            {
                var to = _freeGoal.Value - _pos;
                to.y = 0f;
                float dist = to.magnitude;
                if (dist < 0.3f) { _freeGoal = null; Speed = 0f; OnArrived(); }
                else
                {
                    var dir = to / dist;
                    _fwd = Vector3.RotateTowards(_fwd, dir, Mathf.Deg2Rad * 90f * dt, 0f).normalized;
                    float v = Mathf.Min(Cruise(), Allowed(0f, dist));
                    v = Mathf.Min(v, WalkersAhead(StreetTraffic.Walkers));
                    _want = v; _wantHard = false;
            Speed = Mathf.MoveTowards(Speed, v, (v < Speed ? Brake : Accel) * dt);
                }
            }
            var next = _pos + _fwd * Speed * dt;
            if (_lastPlaced)
            {
                next = RoadSpace.Advance(this, _pos, next, _fwd, HalfLen, HalfWide, out var hit);
                if (hit != null) Speed = 0f;
            }
            _pos = next;
            _lastPlaced = true;
            if (Tf != null) Tf.SetPositionAndRotation(new Vector3(_pos.x, RoadY + SurfaceLift(), _pos.z), Quaternion.LookRotation(_fwd, Vector3.up));
            OnPlaced(dt, Speed, 0f);
        }

        // A 10x city's next-hop table is too expensive to walk in every TurfMap redraw.
        // Keep sampled geometry until a route edge, goal or manoeuvre actually changes;
        // callers still own their output buffers and therefore allocate nothing here.
        readonly List<Vector3> _plannedRoutePreview = new List<Vector3>();
        readonly List<Vector3> _plannedRouteScratch = new List<Vector3>();
        Dictionary<RoadEdge, RoadEdge> _previewRoute;
        Carriageway _previewRoad, _previewGoalRoad;
        RoadEdge _previewLane, _previewGoalLane;
        Connector _previewVia;
        Manoeuvre _previewMan;
        Vector3? _previewFreeGoal;
        float _previewGoalS, _previewGoalD, _previewSpacing;
        int _previewGoalHeading, _previewCursor;
        bool _previewHasGoal, _previewTurnFirst, _previewBuilt, _previewValid;

        /// <summary>Copies the route this driver currently means to take into a caller-owned
        /// buffer. The expensive lane-table walk is cached across redraws and rebuilt only
        /// when the route, goal, road edge or active manoeuvre changes.</summary>
        public bool CopyPlannedRoute(List<Vector3> into, float spacing = 4f,
            bool retainLastPlan = false)
        {
            if (into == null) return false;
            into.Clear();
            if (Tf == null) return false;

            spacing = Mathf.Clamp(spacing, 1f, 12f);
            if (PreviewChanged(spacing))
            {
                _plannedRouteScratch.Clear();
                var rebuilt = BuildPlannedRoute(_plannedRouteScratch, spacing);
                if (rebuilt)
                {
                    _plannedRoutePreview.Clear();
                    _plannedRoutePreview.AddRange(_plannedRouteScratch);
                    _previewCursor = 0;
                    _previewValid = true;
                }
                else if (!retainLastPlan)
                {
                    _plannedRoutePreview.Clear();
                    _previewCursor = 0;
                    _previewValid = false;
                }
                // Transfers may retain the announced route while halted by gunfire.
                RememberPreview(spacing);
            }
            if (!_previewValid || _plannedRoutePreview.Count < 2) return false;

            AdvancePreviewCursor(spacing);
            PreviewAdd(into, PreviewCurrent());
            for (var i = _previewCursor; i < _plannedRoutePreview.Count; i++)
                PreviewAdd(into, _plannedRoutePreview[i]);
            // Keep the destination ring when the final centimetres collapse to one point.
            if (into.Count == 1) into.Add(_plannedRoutePreview[_plannedRoutePreview.Count - 1]);
            return into.Count > 1;
        }

        bool BuildPlannedRoute(List<Vector3> into, float spacing)
        {
            PreviewAdd(into, PreviewCurrent());

            if (_freeGoal.HasValue)
            {
                var free = _freeGoal.Value;
                free.y = RoadY + PreviewLift;
                PreviewAdd(into, free);
                return into.Count > 1;
            }
            if (!_hasGoal || _goalRoad == null || _goalLane == null) return false;

            RoadEdge edge;
            float startS, startD;
            var crossing = Via ?? (_committed ? _via : null);
            if (crossing != null)
            {
                float progress = Via != null ? ViaS : (S - Road.EndS(Heading)) * Heading;
                PreviewConnector(into, crossing, progress, crossing.Length, spacing);
                edge = crossing.To;
                startS = edge != null ? edge.S0 : 0f;
                startD = edge != null ? edge.Offset : 0f;
            }
            else
            {
                if (Road == null) return false;
                edge = Lane ?? Road.LaneFor(Heading, D);
                startS = S;
                startD = D;

                // The route table deliberately stays alive while a driver waits for a
                // chance to turn round. The preview must show the intended turn, not the
                // longer fallback that table represents.
                if (_man == Manoeuvre.UTurn)
                {
                    PreviewUTurn(into, Road, _arcS0, _arcR, _arcSide, _arcHeading0,
                        _arcAng, spacing);
                    edge = Road.LaneFor(-_arcHeading0, -_arcSide * _arcR);
                    startS = _arcS0;
                    startD = edge != null ? edge.Offset : -_arcSide * _arcR;
                }
                else if (_turnFirst || PlansTurnRoundToGoal())
                {
                    float radius = UTurnRadius();
                    int side = D >= 0f ? 1 : -1;
                    int heading = Heading;
                    float centreS = S + heading;
                    PreviewUTurn(into, Road, centreS, radius, side, heading, 0f, spacing);
                    edge = Road.LaneFor(-heading, -side * radius);
                    startS = centreS;
                    startD = edge != null ? edge.Offset : -side * radius;
                }
            }

            if (!RoutePreviewBudget.Fit(edge, startS, _goalRoad, _goalHeading, _goalS,
                Route, RouteShift, into.Count, ref spacing)) { into.Clear(); return false; }
            for (int leg = 0; leg < RoutePreviewBudget.MaxLegs && edge != null && edge.Road != null; leg++)
            {
                var road = edge.Road;
                if (road == _goalRoad && (edge.Heading == _goalHeading || Route == null))
                {
                    PreviewGoal(into, road, edge, startS, startD, spacing);
                    if (into.Count == 1) into.Add(PreviewRoadPoint(road, _goalS, _goalD));
                    return into.Count > 1;
                }

                var routeEdge = edge;
                float laneD = edge.Offset;
                if (RouteShift != null && RouteShift.TryGetValue(edge, out var shifted) &&
                    shifted != null && shifted.Road == road && shifted.Heading == edge.Heading)
                {
                    routeEdge = shifted;
                    laneD = shifted.Offset;
                }

                float endS = road.EndS(routeEdge.Heading);
                PreviewRoadToLane(into, road, startS, endS, startD, laneD,
                    routeEdge.Heading, spacing);

                RoadEdge next = null;
                if (Route != null) Route.TryGetValue(routeEdge, out next);
                if (next == null) break;
                var connector = routeEdge.To?.ConnectorFor(routeEdge, next);
                if (connector == null) break;
                PreviewConnector(into, connector, 0f, connector.Length, spacing);

                edge = next;
                startS = edge.S0;
                startD = edge.Offset;
            }

            into.Clear();
            return false;
        }

        bool PreviewChanged(float spacing) =>
            !_previewBuilt ||
            !Mathf.Approximately(_previewSpacing, spacing) ||
            _previewRoute != Route ||
            _previewRoad != Road ||
            _previewLane != Lane ||
            _previewVia != (Via ?? (_committed ? _via : null)) ||
            _previewGoalRoad != _goalRoad ||
            _previewGoalLane != _goalLane ||
            _previewHasGoal != _hasGoal ||
            _previewGoalHeading != _goalHeading ||
            !Mathf.Approximately(_previewGoalS, _goalS) ||
            !Mathf.Approximately(_previewGoalD, _goalD) ||
            _previewFreeGoal != _freeGoal ||
            _previewMan != _man ||
            _previewTurnFirst != _turnFirst;

        void RememberPreview(float spacing)
        {
            _previewBuilt = true;
            _previewSpacing = spacing;
            _previewRoute = Route;
            _previewRoad = Road;
            _previewLane = Lane;
            _previewVia = Via ?? (_committed ? _via : null);
            _previewGoalRoad = _goalRoad;
            _previewGoalLane = _goalLane;
            _previewHasGoal = _hasGoal;
            _previewGoalHeading = _goalHeading;
            _previewGoalS = _goalS;
            _previewGoalD = _goalD;
            _previewFreeGoal = _freeGoal;
            _previewMan = _man;
            _previewTurnFirst = _turnFirst;
        }

        void AdvancePreviewCursor(float spacing)
        {
            if (_previewCursor >= _plannedRoutePreview.Count) return;
            // Road height distinguishes an overpass from the street beneath it.
            var here = Road != null ? PreviewRoadPoint(Road, S, D) : PreviewCurrent();
            var last = Mathf.Min(_plannedRoutePreview.Count - 1, _previewCursor + 8);
            var nearest = _previewCursor;
            var nearestSqr = float.MaxValue;
            for (var i = _previewCursor; i <= last; i++)
            {
                var sqr = PreviewDistance(i, here, out int cursor);
                if (sqr >= nearestSqr) continue;
                nearestSqr = sqr;
                nearest = cursor;
            }
            var catchup = Mathf.Max(8f, spacing * 2.5f);

            // After a hidden-map gap, catch up to the FIRST nearby cluster; a later
            // crossing or return leg must not steal the monotonic cursor.
            var catchupSqr = catchup * catchup;
            if (last < _plannedRoutePreview.Count - 1 &&
                (nearest == last || nearestSqr > catchupSqr))
            {
                var caught = -1;
                var caughtSqr = float.MaxValue;
                var rising = 0;
                for (var i = last + 1; i < _plannedRoutePreview.Count; i++)
                {
                    var sqr = PreviewDistance(i, here, out int cursor);
                    if (sqr > catchupSqr)
                    {
                        if (caught >= 0) break;
                        continue;
                    }
                    if (sqr < caughtSqr)
                    {
                        caught = cursor;
                        caughtSqr = sqr;
                        rising = 0;
                    }
                    else if (caught >= 0 && ++rising >= 2)
                        break;
                }
                if (caught >= 0)
                {
                    nearest = caught;
                    nearestSqr = caughtSqr;
                }
            }
            if (nearestSqr <= catchupSqr)
                _previewCursor = Mathf.Max(_previewCursor, nearest);
        }

        float PreviewDistance(int end, Vector3 here, out int cursor)
        {
            var a = _plannedRoutePreview[Mathf.Max(0, end - 1)];
            var delta = _plannedRoutePreview[end] - a;
            float t = delta.sqrMagnitude > .0001f
                ? Mathf.Clamp01(Vector3.Dot(here - a, delta) / delta.sqrMagnitude) : 0f;
            cursor = t > 0f ? end : Mathf.Max(0, end - 1);
            return (a + delta * t - here).sqrMagnitude;
        }

        const float PreviewLift = 0.14f;

        Vector3 PreviewCurrent()
        {
            float y = Tf != null ? Tf.position.y : RoadY + SurfaceLift();
            return new Vector3(Position.x, y + PreviewLift, Position.z);
        }

        Vector3 PreviewRoadPoint(Carriageway road, float s, float d)
        {
            var point = road.Pose(s, d);
            point.y = RoadY + road.SurfaceOn(s) + PreviewLift;
            return point;
        }

        bool PlansTurnRoundToGoal()
        {
            bool requiredTurn = RequiredTurnHere();
            if (!_hasGoal || Road == null || Road != _goalRoad || NoTurnBack ||
                !Road.TwoWay || !Profile.UTurnsInRoad || Road.MedianHalf > 0f ||
                (!requiredTurn && (_turnBackFor >= TurnBackPatience || _goalUTurns >= MaxGoalUTurns))) return false;
            var node = Road.NodeAhead(Heading);
            if (!requiredTurn && (Road.EndS(Heading) - S) * Heading <
                UTurnRadius() + HalfLen + 2f + (node != null ? node.StopSetback : 0f)) return false;
            return Heading != _goalHeading || (_goalS - S) * Heading < -3f;
        }

        void PreviewGoal(List<Vector3> into, Carriageway road, RoadEdge edge,
            float fromS, float fromD, float spacing)
        {
            float distance = Mathf.Max(0f, (_goalS - fromS) * edge.Heading);
            float settle = Mathf.Min(_goalPark ? 18f : 10f, distance);
            float settleS = _goalS - edge.Heading * settle;

            float settledD = fromD;
            if (distance > settle + 0.1f)
            {
                PreviewRoadToLane(into, road, fromS, settleS, fromD, edge.Offset,
                    edge.Heading, spacing);
                settledD = edge.Offset;
            }
            else
                settleS = fromS;
            PreviewRoad(into, road, settleS, _goalS, settledD, _goalD, spacing);
        }

        void PreviewRoadToLane(List<Vector3> into, Carriageway road, float fromS,
            float toS, float fromD, float laneD, int heading, float spacing)
        {
            float distance = Mathf.Abs(toS - fromS);
            if (Mathf.Abs(fromD - laneD) < 0.05f || distance < 0.1f)
            {
                PreviewRoad(into, road, fromS, toS, fromD, laneD, spacing);
                return;
            }

            float shift = Mathf.Min(18f, distance);
            float shiftedAt = fromS + heading * shift;
            PreviewRoad(into, road, fromS, shiftedAt, fromD, laneD, spacing);
            if (distance > shift + 0.1f)
                PreviewRoad(into, road, shiftedAt, toS, laneD, laneD, spacing);
        }

        void PreviewRoad(List<Vector3> into, Carriageway road, float fromS, float toS,
            float fromD, float toD, float spacing)
        {
            float distance = Mathf.Abs(toS - fromS);
            int count = Mathf.Max(1, Mathf.CeilToInt(distance / spacing));
            for (int i = 1; i <= count; i++)
            {
                float t = i / (float)count;
                float across = Mathf.Lerp(fromD, toD, Mathf.SmoothStep(0f, 1f, t));
                PreviewAdd(into, PreviewRoadPoint(road, Mathf.Lerp(fromS, toS, t), across));
            }
        }

        void PreviewConnector(List<Vector3> into, Connector connector, float fromS,
            float toS, float spacing)
        {
            if (connector == null) return;
            float distance = Mathf.Abs(toS - fromS);
            int count = Mathf.Max(1, Mathf.CeilToInt(distance / spacing));
            float fromLift = EndLift(connector.From, leaving: true);
            float toLift = EndLift(connector.To, leaving: false);
            for (int i = 1; i <= count; i++)
            {
                float t = i / (float)count;
                float s = Mathf.Lerp(fromS, toS, t);
                var point = connector.Point(s);
                float along = connector.Length > 0.01f ? Mathf.Clamp01(s / connector.Length) : 1f;
                point.y = RoadY + Mathf.Lerp(fromLift, toLift, along) + PreviewLift;
                PreviewAdd(into, point);
            }
        }

        void PreviewUTurn(List<Vector3> into, Carriageway road, float centreS, float radius,
            int side, int heading, float fromAngle, float spacing)
        {
            float distance = Mathf.Max(0f, Mathf.PI - fromAngle) * radius;
            int count = Mathf.Max(2, Mathf.CeilToInt(distance / spacing));
            for (int i = 1; i <= count; i++)
            {
                float angle = Mathf.Lerp(fromAngle, Mathf.PI, i / (float)count);
                float s = centreS + heading * radius * Mathf.Sin(angle);
                float d = side * radius * Mathf.Cos(angle);
                PreviewAdd(into, PreviewRoadPoint(road, s, d));
            }
        }

        static void PreviewAdd(List<Vector3> into, Vector3 point)
        {
            if (into.Count == 0 || (into[into.Count - 1] - point).sqrMagnitude > 0.01f)
                into.Add(point);
        }

        public Vector3? FreeGoal => _freeGoal;
    }
}
