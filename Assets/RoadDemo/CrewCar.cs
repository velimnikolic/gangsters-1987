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

        public CarBody Body { get; private set; }

        /// <summary>The ledger equipment item this car stands for; -1 until the first
        /// deal binds it to the roster's first vehicle.</summary>
        public int ItemId = -1;
        public string DisplayName = "Car";

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

        const float PassOvershoot = 22f;    // metres past the target before the turn-round

        /// <summary>How far off a carriageway the mark may stand and still have a street
        /// the pass can be driven down (CrewBike.PassReach's opposite number).</summary>
        static readonly float[] PassReach = { 14f, 30f, 60f };
        const float PassSpeed = 9f;         // metres a second alongside the mark

        int _passDir = 1;

        public CrewCar()
        {
            Profile = DriverProfile.Gangster;
            Tag = "crew";
        }

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

        // ------------------------------------------------------------------ the tin

        /// <summary>Rounds that went into the engine bay. A car is not a health bar and
        /// this is not one either: it counts the rounds that went into the FRONT of it,
        /// and everything else a round does to a car it does to the paint.</summary>
        public int EngineHits { get; private set; }

        /// <summary>What the engine takes before it stops, and how much of a round that
        /// went through the bonnet ever reaches anything that matters.
        ///
        /// A bonnet is mostly air, a radiator and a lot of tin. Most of what goes through
        /// one goes through it. So killing a car is DELIBERATE - about thirty rounds into
        /// the front of it - rather than a thing that happens to a crew because a fight
        /// went on a while: at fourteen straight bonnet hits the first test run lost its
        /// car to a rival crew's stray fire inside a minute, which is not "we shot the
        /// engine out", it is "cars stop working".</summary>
        public static int EngineHitsToKill = 3;

        /// <summary>Of the rounds that go into the bonnet, this many find something that
        /// turns.
        ///
        /// The pair of them were first set for a car that stands under fire all day, and
        /// that car does not exist: over thirty runs of the soak the whole quarter put
        /// 178 rounds into the outfit's tin and 22 of those into something that turns -
        /// four in the busiest single run, against a threshold of ten. A consequence
        /// nothing can reach is not a consequence. Three, at four rounds in ten, is about
        /// SEVEN into the front of one car: sustained fire into a bonnet, which is what
        /// the player asked for, and still not something a stray gets you.</summary>
        public static float EngineChance = 0.4f;

        /// <summary>Nothing under the bonnet is turning. The car rolls to a stop and
        /// stays where it stops - DemoCrews puts the crew out of it.</summary>
        public bool EngineDead => EngineHits >= EngineHitsToKill;

        /// <summary>True on the frame the engine died, for whoever wants to say so.</summary>
        public bool EngineJustDied { get; private set; }

        /// <summary>A round into the tin. Where it went decides what it cost: the front
        /// third is the engine bay, everything behind that is a hole in a door.</summary>
        public void TakeRound(Vector3 at, Vector3 from)
        {
            if (Tf == null) return;
            var local = Tf.InverseTransformPoint(at);
            bool bonnet = local.z > HalfLength * 0.34f;
            if (bonnet && !EngineDead && Random.value < EngineChance)
            {
                EngineHits++;
                // the smoke is lit HERE and not where EngineJustDied is read: that flag is
                // a one-shot for whichever caller wants to say so out loud, and a car whose
                // engine has been shot out must look shot out to anybody who happens to be
                // driving it, announced or not
                if (EngineDead) { EngineJustDied = true; CarSmoke.Bonnet(this); }
            }
            // WHICH PANEL IT WENT THROUGH, not which way the shooter was stood. The
            // hole lies in the tin, so what it needs is the tin's own normal: the flank
            // it is nearest, or the front or back when the round went into an end. Read
            // off the box the car already is, in the car's own frame.
            float acrossness = Mathf.Abs(local.x) / Mathf.Max(HalfWidth, 1e-3f);
            float alongness = Mathf.Abs(local.z) / Mathf.Max(HalfLength, 1e-3f);
            var outward = acrossness >= alongness
                ? Tf.right * Mathf.Sign(local.x == 0f ? 1f : local.x)
                : Tf.forward * Mathf.Sign(local.z == 0f ? 1f : local.z);
            CrewGore.Hole(Tf, at, outward);
        }

        /// <summary>Read once - the caller that says "the engine's gone" clears it.</summary>
        public bool TakeEngineDeath()
        {
            if (!EngineJustDied) return false;
            EngineJustDied = false;
            return true;
        }

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
            DriveByTarget = null;
            Profile = Civic ? DriverProfile.Police : Hot ? DriverProfile.Hot : DriverProfile.Gangster;
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

        /// <summary>Shoot the place up: passes along the street past this crew, a
        /// turn-round at the end of each, until told otherwise or nobody is left;
        /// then in at the kerb.</summary>
        public void DriveBy(DemoCrews.Unit target)
        {
            if (target == null) return;
            DriveByTarget = target;
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
            Carriageway road = null;
            float ts = 0f, td = 0f;
            foreach (float within in PassReach)
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
            // which way along the target's road this pass runs: on it already, the way
            // the passes alternate; coming from elsewhere, the lane on the mark's side
            int dir = Road == road ? _passDir : (td >= 0f ? 1 : -1);
            float endS = Mathf.Clamp(ts + dir * PassOvershoot, 8f, road.Length - 8f);
            var lane = road.LaneFor(dir, td) ?? road.LaneFor(-dir, td);
            if (lane == null) { ParkNear(t); return; }
            _passDir = lane.Heading;
            var goal = road.Pose(endS, lane.Offset);
            GoTo(goal, park: false, standOff: 0f, stopAtGoal: false, wantHeading: lane.Heading);
        }

        protected override void OnArrived()
        {
            if (DriveByTarget != null)
            {
                // the end of a pass: the next one runs the other way - a turn-round in
                // the road (or the long way round the block) and back past the mark
                _passDir = -_passDir;
                PlanPass();
            }
        }

        /// <summary>Coast to a stop where it is (the crew is getting out and the car is
        /// already at the kerb, or the player changed his mind).</summary>
        public new void Stop()
        {
            DriveByTarget = null;
            Halt(hard: false);
        }

        /// <summary>Both feet on the brake, here, now - the plan torn up. What "get
        /// out" means: the crew is climbing down where the car stands, so it does not
        /// go looking for a kerb first, and whatever it was in the middle of (a
        /// drive-by, an errand) it is not doing any more.</summary>
        public void HardStop()
        {
            DriveByTarget = null;
            Halt(hard: true);
        }

        void EndDriveBy()
        {
            DriveByTarget = null;
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
            Profile = Civic ? DriverProfile.Police
                : DriveByTarget != null || Hot ? DriverProfile.Hot : DriverProfile.Gangster;
            base.Tick(dt);
        }

        protected override void OnPlaced(float dt, float speed, float steerDegrees)
        {
            Body?.TickWheels(dt, speed, steerDegrees);
        }

        /// <summary>Coming up on the mark, the car comes off the throttle.
        ///
        /// A pistol reaches ten metres and the pavement is eight from the crown, so the
        /// whole shot is one second of road; at the hot pace (eighteen a second) a man
        /// with the gun out of the window gets his mark abeam for a heartbeat and the
        /// pass goes by without a round fired, which is what the runs showed. Slowed to
        /// a walking-pace nine, the same pass gives every gun on that side two or three.
        /// Away from the mark the pace is the profile's own again - a getaway is a
        /// getaway.</summary>
        protected override float LimitTarget(float target)
        {
            // nothing under the bonnet is turning: it rolls to a stop and stays there
            if (EngineDead) return 0f;
            if (DriveByTarget == null || Tf == null) return target;
            var to = DriveByTarget.Position - Position;
            to.y = 0f;
            float dist = to.magnitude;
            if (dist > 45f) return target;
            float pace = Mathf.Lerp(PassSpeed, target, Mathf.InverseLerp(20f, 45f, dist));
            return Mathf.Min(target, pace);
        }

        // the words and what they were cut for: the popup reads this every frame,
        // and a drive-by concatenates the mark's name
        string _statusLine;
        (Mode state, Manoeuvre doing, bool hot, DemoCrews.Unit mark, bool occupied) _statusKey;

        public string StatusLine
        {
            get
            {
                var state = State;
                var key = (state, Doing, Hot, DriveByTarget, Occupant != null);
                if (_statusLine != null && key.Equals(_statusKey)) return _statusLine;
                _statusKey = key;
                _statusLine = state switch
                {
                    Mode.Driving => Doing == Manoeuvre.PullOut ? "Waiting for a gap" : Hot ? "On the road, under fire" : "On the road",
                    Mode.DriveBy => DriveByTarget != null ? "Drive-by on " + DriveByTarget.GangName : "Drive-by",
                    _ => Occupant != null ? "In the car" : "Parked",
                };
                return _statusLine;
            }
        }
    }
}
