using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// A car of the outfit's (or the law's, when PoliceDispatch drives one): the
    /// ledger's item - a Sedan, a Coupe, a Panel Van, whatever body plays it - owned
    /// by whichever lieutenant the book has assigned it to, ridden by his crew once
    /// they are in it. The driving is the shared RoadCar's - the lane network, the
    /// claims on the road, the junction discipline - with the GANGSTER at the wheel
    /// (DriverProfile.Gangster on an errand, DriverProfile.Hot with the guns out:
    /// quicker, no patience, the crown between the lanes, the far lane, a turn in
    /// the road, a red when the box is clear; the law's car drives the Police
    /// profile). What this class adds is the outfit's business: the orders
    /// (DriveTo, ParkNear, DriveBy, Stop), the body's seats, doors and windows
    /// (CarBody), and who is aboard - which is DemoCrews' to fill.
    /// </summary>
    public sealed class CrewCar : RoadCar
    {
        public enum Mode { Parked, Driving, DriveBy }

        static readonly List<CrewCar> ActiveRoadblocks = new List<CrewCar>();

        /// <summary>Player cars currently standing across a carriageway. Police transfer
        /// logic reads this through <see cref="RoadblockAhead"/>; it does not need a
        /// parallel trigger or physics-only idea of what is blocking the street.</summary>
        public static IReadOnlyList<CrewCar> Roadblocks => ActiveRoadblocks;

        public CarBody Body { get; private set; }

        /// <summary>The ledger equipment item this car stands for; -1 until the first
        /// deal binds it to the roster's first vehicle.</summary>
        public int ItemId = -1;

        /// <summary>Not the outfit's at all - a police cruiser the dispatcher drives:
        /// the books never bind it, the outfit never boards it.</summary>
        public bool Civic;

        /// <summary>The crew whose lieutenant OWNS the item per the ledger - only that
        /// crew may board. Re-read on every deal; null means nobody's car.</summary>
        public DemoCrews.Unit Owner;

        /// <summary>The crew inside it, or null.</summary>
        public DemoCrews.Unit Occupant;

        /// <summary>The men in it, and which seat each has.</summary>
        public readonly HashSet<CrewWalker> Aboard = new HashSet<CrewWalker>();
        public readonly Dictionary<CrewWalker, int> SeatOf = new Dictionary<CrewWalker, int>();

        /// <summary>The crew being shot up on a drive-by, or null.</summary>
        public DemoCrews.Unit DriveByTarget { get; private set; }

        /// <summary>The crew aboard is in a fight - a drive-by, or shot at on the way
        /// somewhere: the driver puts his foot down and goes round anything in his
        /// way at once. The arena sets it each frame; a drive-by is hot on its own.</summary>
        public bool Hot;

        /// <summary>A civic car is answering a call with its roof lights on. Kept
        /// separate from <see cref="Hot"/>, which the crew fight loop owns: a police
        /// car uses the fast response profile only for this leg, then returns to an
        /// ordinary patrol pace.</summary>
        public bool CivicResponse;

        const float PassOvershoot = 22f;    // metres past the target before the turn-round

        /// <summary>How far off a carriageway the mark may stand and still have a street
        /// the pass can be driven down (CrewBike.PassReach's opposite number).</summary>
        static readonly float[] PassReach = { 14f, 30f, 60f };
        const float PassSpeed = 9f;         // metres a second alongside the mark

        int _passDir = 1;
        Carriageway _driveByRoad;
        bool _localPass;

        Carriageway _roadblockRoad;
        float _roadblockS;
        int _roadblockHeading;
        bool _roadblockOrdered;
        StoodCar _roadblockTraffic;
        SidewalkPlan _roadblockWalk;

        /// <summary>This body has reached its order and is now the roadblock, rather than
        /// merely being on the way to establish one.</summary>
        public bool IsRoadblock => _roadblockTraffic != null;

        protected override bool RequiresInRoadTurn =>
            DriveByTarget != null && _localPass && Road == _driveByRoad;

        public CrewCar()
        {
            Profile = DriverProfile.Gangster;
            Tag = "crew";
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetRoadblocks() => ActiveRoadblocks.Clear();

        // ------------------------------------------------------------------ setup

        /// <summary>The body is read the moment the transform is set: seats, doors,
        /// wheels, size - any pack car. The car is put on the road under it (or on
        /// open ground, off any road).</summary>
        public void Attach(Transform tf)
        {
            Tf = tf;
            Body = new CarBody(tf);
            HalfLen = Body.HalfLength;
            HalfWide = Body.HalfWidth;
            AxleBack = Body.AxleBack;
            Net ??= LaneNet.Active;
            PlaceAt(tf.position, tf.forward);
        }

        public Mode State =>
            DriveByTarget != null ? Mode.DriveBy
            : HasGoal || FreeGoal.HasValue || Mathf.Abs(Speed) > 0.05f ? Mode.Driving
            : Mode.Parked;

        public bool Moving => State != Mode.Parked || Mathf.Abs(Speed) > 0.05f;

        // ------------------------------------------------------------------ the body, passed through

        public int Seats { get => Body != null ? Body.Seats : 4; set { if (Body != null) Body.Seats = value; } }
        public int FreeSeats => Mathf.Max(0, Seats - Aboard.Count);
        public bool HasDoors => Body != null && Body.HasDoors;

        /// <summary>The lowest seat nobody has been given, or -1 when the car is full.</summary>
        public int FreeSeat()
        {
            for (int s = 0; s < Seats; s++)
            {
                bool taken = false;
                foreach (var kv in SeatOf) if (kv.Value == s) { taken = true; break; }
                if (!taken) return s;
            }
            return -1;
        }

        public Vector3 Seat(int index) => Body != null ? Body.Seat(index) : Position;
        public static float SeatSide(int index) => CarBody.SeatSide(index);
        public Vector3 DoorPoint(int seat) => Body != null ? Body.DoorPoint(seat) : Position + (Tf != null ? Tf.right : Vector3.right) * 1.7f;
        public Vector3 Window(int index, Vector3 target) => Body != null ? Body.Window(index, target) : Position + Vector3.up;
        public void OpenDoorFor(int seat) => Body?.OpenDoorFor(seat);
        public void CloseDoorFor(int seat) => Body?.CloseDoorFor(seat);
        public void CloseAllDoors() => Body?.CloseAllDoors();
        public bool DoorOpenFor(int seat) => Body == null || Body.DoorOpenFor(seat);
        public void SetWindow(int seat, bool down) => Body?.SetWindow(seat, down);
        public void CloseAllWindows() => Body?.CloseAllWindows();

        /// <summary>A crew's car brakes for everybody EXCEPT the men it is fighting.
        ///
        /// A rival's man walks into the road, stops in front of the bonnet, and the car
        /// stops for him - then the two of them stand there shooting at each other
        /// through the windscreen at four metres, for as long as it takes. That is not a
        /// gunfight, it is a queue, and the answer a driver would actually reach for is
        /// under his right foot. The law is still given way to (a crew does not run a
        /// policeman down by accident) and so is everybody the crew has no quarrel with.
        /// The running down itself is DemoCrews.RunDown - this only takes the driver's
        /// foot off the brake.</summary>
        protected override bool GivesWayTo(int faction)
        {
            if (Civic) return true;
            var unit = Occupant ?? Owner;
            if (unit == null || faction == unit.Faction) return true;
            if (faction == StreetAlarm.PoliceFaction) return true;
            return unit.TargetUnit == null || unit.TargetUnit.Faction != faction;
        }

        /// <summary>The way from the car's middle to its kerb - the side a man steps
        /// off the pavement to it. Off any road, the car's right.</summary>
        public Vector3 KerbSideDir
        {
            get
            {
                if (Road != null) return Road.Right * (D >= 0f ? 1f : -1f);
                return Tf != null ? Tf.right : Vector3.right;
            }
        }

        // ------------------------------------------------------------------ orders

        /// <summary>Drive toward this point - and pull in at the kerb nearest it, on
        /// the side of the street the point lies. A car of the outfit's does not
        /// stop in the middle of the road.</summary>
        public void DriveTo(Vector3 point) => ParkNear(point);

        /// <summary>Pull in at the kerb nearest this point and stop there, nose along
        /// the street the way that side's traffic runs: turns round in the road when
        /// the spot is behind or across, routes there when it is on another street.
        /// Off any road, the point is the stop.</summary>
        public void ParkNear(Vector3 point)
        {
            // Any ordinary driving order is also an unambiguous MOVE ON. The static
            // traffic/walking claims have to leave before this RoadCar rejoins its lane.
            if (IsRoadblock && !ResumeFromRoadblock()) return;
            if (_roadblockOrdered) ClearRoadblockOrder();
            DriveByTarget = null;
            _driveByRoad = null;
            _localPass = false;
            Profile = Civic
                ? (CivicResponse ? DriverProfile.Police : DriverProfile.Patrol)
                : Hot ? DriverProfile.Hot : DriverProfile.Gangster;
            if (!OnRoad || Net == null)
            {
                GoFree(new Vector3(point.x, RoadY, point.z));
                return;
            }
            // "here" means the nearest kerb the car can actually reach: the stopping
            // distance on from where it is, no turning round
            if (Road != null)
            {
                Road.Project(point, out float ps, out float pd);
                float ahead = (ps - S) * Heading;
                float room = Speed * Speed / (2f * Brake) + 8f;
                if (ahead > -4f && ahead < room) point = Road.Pose(S + Heading * room, pd) + Vector3.up * point.y;
            }
            if (!GoTo(point, park: true)) GoFree(new Vector3(point.x, RoadY, point.z));
        }

        /// <summary>Can this point name an actual carriageway on which the selected car
        /// can stand across the road? Used by the command card so an impossible order is
        /// shown faded instead of being accepted and silently becoming a free-ground
        /// drive.</summary>
        public bool CanRoadblockAt(Vector3 point)
        {
            if (Civic || Wrecked || EngineDead || Tf == null || Net == null) return false;
            return Net.Locate(point, out _, out _, within: 12f) != null;
        }

        /// <summary>Drive to the road point, stop, then turn the real car body across the
        /// carriageway. On arrival it is transferred from the moving RoadCar ledger to a
        /// StoodCar claim for traffic and one equivalent SidewalkPlan box for walkers.</summary>
        public bool OrderRoadblock(Vector3 point)
        {
            if (!CanRoadblockAt(point)) return false;
            if (IsRoadblock && !ResumeFromRoadblock()) return false;

            var road = Net.Locate(point, out float s, out float d, within: 12f);
            if (road == null) return false;

            DriveByTarget = null;
            _driveByRoad = null;
            _localPass = false;
            _roadblockRoad = road;
            _roadblockS = Mathf.Clamp(s, 6f, road.Length - 6f);
            _roadblockHeading = Road == road ? Heading : d >= 0f ? 1 : -1;
            if (road.LaneFor(_roadblockHeading, d) == null) _roadblockHeading = -_roadblockHeading;
            _roadblockOrdered = GoTo(road.Pose(_roadblockS, d), park: false,
                standOff: 0f, stopAtGoal: true, wantHeading: _roadblockHeading);
            if (_roadblockOrdered) return true;

            ClearRoadblockOrder();
            return false;
        }

        /// <summary>Remove the physical roadblock and drive far enough on to clear the
        /// queued traffic. Safe to call when the car is not a roadblock.</summary>
        public bool MoveOnFromRoadblock()
        {
            if (!IsRoadblock) return false;
            var road = _roadblockRoad;
            float s = _roadblockS;
            int heading = _roadblockHeading;
            if (!ResumeFromRoadblock()) return false;
            ParkNear(road.Pose(Mathf.Clamp(s + heading * 30f, 6f, road.Length - 6f),
                road.KerbD(heading, HalfWide)));
            return true;
        }

        /// <summary>The nearest established player roadblock ahead on this same
        /// carriageway. Crosswise bodies are deliberately detected by their road-s, not
        /// by a loose world radius that could mistake a parallel street for this one.</summary>
        public static bool RoadblockAhead(RoadCar traveller, float reach, out CrewCar blockade)
        {
            blockade = null;
            if (traveller == null || traveller.Road == null || reach < 0f) return false;
            float nearest = float.MaxValue;
            for (int i = ActiveRoadblocks.Count - 1; i >= 0; i--)
            {
                var car = ActiveRoadblocks[i];
                if (car == null || !car.IsRoadblock || car.Wrecked || car.Tf == null)
                {
                    ActiveRoadblocks.RemoveAt(i);
                    continue;
                }
                if (car._roadblockRoad != traveller.Road) continue;
                float ahead = (car._roadblockS - traveller.S) * traveller.Heading;
                float berth = traveller.HalfLength + car.HalfWidth;
                if (ahead < -berth || ahead > reach || ahead >= nearest) continue;
                nearest = ahead;
                blockade = car;
            }
            return blockade != null;
        }

        void EstablishRoadblock()
        {
            if (!_roadblockOrdered || _roadblockRoad == null || Tf == null || Wrecked) return;
            _roadblockOrdered = false;

            // Put the model at the road crown and the visible nose at right angles. It
            // leaves the moving ledger before the static claim is added, so traffic sees
            // exactly one car occupying this body.
            PlaceAt(_roadblockRoad.Pose(_roadblockS, 0f), _roadblockRoad.Right);
            Despawn();
            StreetTraffic.Users.Remove(this);
            Tf.SetPositionAndRotation(Tf.position,
                Quaternion.LookRotation(_roadblockRoad.Right, Vector3.up));

            _roadblockTraffic = StoodCar.Park(Tf.gameObject);
            _roadblockWalk = new SidewalkPlan();
            var box = SidewalkPlan.Make(new Vector2(Tf.position.x, Tf.position.z),
                Tf.eulerAngles.y, new Vector2(HalfWide, HalfLen), solid: true);
            _roadblockWalk.Take(box);
            WalkObstacles.RegisterPlan(_roadblockWalk);
            if (!ActiveRoadblocks.Contains(this)) ActiveRoadblocks.Add(this);
        }

        bool ResumeFromRoadblock()
        {
            if (!IsRoadblock) return true;
            var road = _roadblockRoad;
            float s = _roadblockS;
            int heading = _roadblockHeading;
            ReleaseRoadblockClaims();
            if (road == null || Tf == null || Wrecked) return false;
            var lane = road.LaneFor(heading, 0f) ?? road.LaneFor(-heading, 0f);
            if (lane == null) return false;
            heading = lane.Heading;
            if (!PlaceAt(road.Pose(s, lane.Offset), road.Axis * heading)) return false;
            if (!StreetTraffic.Users.Contains(this)) StreetTraffic.Users.Add(this);
            return true;
        }

        void ReleaseRoadblockClaims()
        {
            _roadblockTraffic?.Forget();
            _roadblockTraffic = null;
            if (_roadblockWalk != null) WalkObstacles.UnregisterPlan(_roadblockWalk);
            _roadblockWalk = null;
            ActiveRoadblocks.Remove(this);
            ClearRoadblockOrder();
        }

        void ClearRoadblockOrder()
        {
            _roadblockOrdered = false;
            _roadblockRoad = null;
            _roadblockS = 0f;
            _roadblockHeading = 0;
        }

        /// <summary>Shoot the place up: passes along the street past this crew, a
        /// turn-round at the end of each, until told otherwise or nobody is left;
        /// then in at the kerb.</summary>
        public void DriveBy(DemoCrews.Unit target)
        {
            if (target == null) return;
            if (IsRoadblock && !ResumeFromRoadblock()) return;
            if (_roadblockOrdered) ClearRoadblockOrder();
            DriveByTarget = target;
            _driveByRoad = null;
            _localPass = false;
            Profile = DriverProfile.Hot;
            var t = target.Position;
            if (Road != null)
            {
                Road.Project(t, out float ts, out _);
                _passDir = (ts - S) * Heading >= 0f ? Heading : -Heading;
            }
            else _passDir = Vector3.Dot(t - Position, Forward) >= 0f ? 1 : -1;
            PlanPass();
        }

        // A pass: down the target's street past the target by a safe distance, no stop
        // at the end - the next pass laid behind us is what turns the car round.
        void PlanPass()
        {
            if (DriveByTarget == null) return;
            var t = DriveByTarget.Position;
            if (Net == null || !OnRoad)
            {
                var f = t - Position; f.y = 0f; f.Normalize();
                GoFree(new Vector3(t.x, RoadY, t.z) + f * PassOvershoot);
                return;
            }
            // the same widening the bike's pass does, and for the same reason: a crew at
            // a frontage stands further than fourteen metres off the carriageway, and
            // ParkNear would clear the mark and end the drive-by before a round was fired
            // The first pass chooses the attack segment. Keep it for the whole order:
            // a wounded mark may run toward the next corner, but that must not quietly
            // turn the following pass into a lap around a different block.
            Carriageway road = _driveByRoad;
            float ts = 0f, td = 0f;
            if (road != null) road.Project(t, out ts, out td);
            else foreach (float within in PassReach)
            {
                road = Net.Locate(t, out ts, out td, within);
                if (road != null) break;
            }
            if (road == null)
            {
                var f0 = t - Position; f0.y = 0f;
                if (f0.sqrMagnitude < 1e-4f) f0 = Forward;
                GoFree(new Vector3(t.x, RoadY, t.z) + f0.normalized * PassOvershoot);
                return;
            }
            _driveByRoad = road;
            // which way along the target's road this pass runs: on it already, the way
            // the passes alternate; coming from elsewhere, the lane on the mark's side
            int dir = Road == road ? _passDir : (td >= 0f ? 1 : -1);
            var lane = road.LaneFor(dir, td) ?? road.LaneFor(-dir, td);
            if (lane == null) { ParkNear(t); return; }

            // A car drive-by owns this one street segment until the mark is finished.
            // Leave enough road at either end for the complete turn, the junction's stop
            // box and the braking distance from the nine-metre pass pace. Without these
            // interior endpoints the ordinary trip planner eventually gives up on its
            // U-turn and faithfully routes the car around the surrounding blocks.
            float maxRadius = Mathf.Max(2.2f, road.HalfRoad - HalfWide - 0.45f);
            float radius = Mathf.Clamp(Mathf.Abs(lane.Offset), 2.2f, maxRadius);
            float brakingRoom = PassSpeed * PassSpeed /
                                Mathf.Max(1f, 2f * Brake) + 2f;
            float turnBody = radius + HalfLen + 3f + brakingRoom;
            float minS = turnBody + (road.NodeA != null ? road.NodeA.StopSetback : 0f);
            float maxS = road.Length - turnBody -
                         (road.NodeB != null ? road.NodeB.StopSetback : 0f);
            bool twoWay = road.TwoWay && road.MedianHalf <= 0f &&
                          road.LaneFor(-lane.Heading, -lane.Offset) != null;
            _localPass = twoWay && maxS - minS >= Mathf.Max(8f, HalfLen * 2f + 2f);

            float endS = _localPass
                ? Mathf.Clamp(ts + lane.Heading * PassOvershoot, minS, maxS)
                : Mathf.Clamp(ts + lane.Heading * PassOvershoot, 8f, road.Length - 8f);
            _passDir = lane.Heading;
            var goal = road.Pose(endS, lane.Offset);
            GoTo(goal, park: false, standOff: 0f, stopAtGoal: false, wantHeading: lane.Heading);
        }

        protected override void OnArrived()
        {
            if (_roadblockOrdered)
            {
                EstablishRoadblock();
                return;
            }
            if (DriveByTarget != null)
            {
                // the end of a pass: the next one runs the other way, turning inside
                // this same segment and coming back past the mark
                _passDir = -_passDir;
                PlanPass();
            }
        }

        /// <summary>Coast to a stop where it is (the crew is getting out and the car is
        /// already at the kerb, or the player changed his mind).</summary>
        public new void Stop()
        {
            if (_roadblockOrdered) ClearRoadblockOrder();
            DriveByTarget = null;
            _driveByRoad = null;
            _localPass = false;
            Halt(hard: false);
        }

        /// <summary>Both feet on the brake, here, now - the plan torn up. What "get
        /// out" means: the crew is climbing down where the car stands, so it does not
        /// go looking for a kerb first, and whatever it was in the middle of (a
        /// drive-by, an errand) it is not doing any more.</summary>
        public void HardStop()
        {
            if (IsRoadblock) return;
            if (_roadblockOrdered) ClearRoadblockOrder();
            DriveByTarget = null;
            _driveByRoad = null;
            _localPass = false;
            Halt(hard: true);
        }

        void EndDriveBy()
        {
            DriveByTarget = null;
            _driveByRoad = null;
            _localPass = false;
            // the job is done: on down the road a little and in at the kerb on this side
            ParkNear(Position + Forward * 34f);
        }

        /// <summary>The arena tells the car the crew it was after is finished.</summary>
        public void TargetDone()
        {
            if (DriveByTarget != null) EndDriveBy();
        }

        // ------------------------------------------------------------------ frame

        public new void Tick(float dt)
        {
            if (Tf == null) return;
            Body?.TickDoors(dt);
            // who is driving this frame: the law its own way, the outfit hot or cold
            Profile = Civic ? (CivicResponse ? DriverProfile.Police : DriverProfile.Patrol)
                : DriveByTarget != null || Hot ? DriverProfile.Hot : DriverProfile.Gangster;
            base.Tick(dt);
        }

        protected override void OnPlaced(float dt, float speed, float steerDegrees)
        {
            Body?.TickWheels(dt, speed, steerDegrees);
        }

        protected override void OnWrecked()
        {
            // A bombed roadblock leaves wreckage visuals to CarShatter, but it must not
            // leave an invisible static car and pedestrian box behind as a second wreck.
            if (IsRoadblock) ReleaseRoadblockClaims();
            else ClearRoadblockOrder();
        }

        /// <summary>Coming up on the mark, the car comes off the throttle.
        ///
        /// A pistol reaches ten metres and the pavement is eight from the crown, so the
        /// whole shot is one second of road; at the hot pace (eighteen a second) a man
        /// with the gun out of the window gets his mark abeam for a heartbeat and the
        /// pass goes by without a round fired, which is what the runs showed. Slowed to
        /// a walking-pace nine, the same pass gives every gun on that side two or three.
        /// The run-up uses the profile's own pace; once the car is on the locked attack
        /// segment it keeps the pass pace all the way to each turn-round.</summary>
        protected override float LimitTarget(float target)
        {
            // nothing under the bonnet is turning: it rolls to a stop and stays there
            if (EngineDead) return 0f;
            if (DriveByTarget == null || Tf == null) return target;
            // Once the attack segment is chosen, every metre on it is part of the pass.
            // Do not release the car back to the Hot profile merely because the mark ran
            // more than 45 m from it: at eighteen metres a second the remaining end berth
            // is no longer enough to brake for the in-road turn, so the car crosses the
            // junction and the route table quite reasonably sends it round the block.
            if (_localPass && Road == _driveByRoad)
                return Mathf.Min(target, PassSpeed);
            var to = DriveByTarget.Position - Position;
            to.y = 0f;
            float dist = to.magnitude;
            if (dist > 45f) return target;
            float pace = Mathf.Lerp(PassSpeed, target, Mathf.InverseLerp(20f, 45f, dist));
            return Mathf.Min(target, pace);
        }

        // The car's own sentence - "Waiting for a gap", "Drive-by on Falcone" - was cut
        // for the card that floated over a selected lieutenant. That card was withdrawn
        // (2026-09-02, the user's word) and his chip on the top bar has room for two
        // words, which CrewStatus reads off State: nothing was left reading this.
    }
}
