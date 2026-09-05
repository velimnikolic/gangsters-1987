using System.Collections.Generic;
using LivingCity.Gameplay;
using LivingCity.Personnel;
using LivingCity.Police;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// The physical part of custody shared by the arrest drive and both later prison
    /// legs: one exact body, a real escort unit, physical vehicle doors, real seats and
    /// a walked threshold. Scheduling and court outcomes remain PoliceForce's business.
    /// </summary>
    public sealed class PrisonerCarriage
    {
        public sealed class SeatedBody
        {
            public CrewWalker Man;
            public Transform Parent;
            public Vector3 LocalScale;
            public Renderer[] Renderers;
            public bool[] Shown;
            public CarOccupant Visual;
            public bool Prisoner;
        }

        /// <summary>One body's physical route into one seat. Arrest pickup and court
        /// transfer both use this exact state machine; their callers keep only the
        /// surrounding case, wave and scheduling policy.</summary>
        public sealed class BoardingMan
        {
            public CrewWalker Man;
            public CrewWalker Escort;
            public DemoCrews.Unit EscortUnit;
            public RoadCar Car;
            public int Seat;
            public bool Prisoner;
            // A pickup queues several prisoners per escort before starting their routes.
            public bool Activated;
            public bool Started;
            public bool Seated;
            public float StartedAt;
            public float RetryAt;
            public bool GeometryReady;
            public Vector3 Door;
            public Vector3 EscortPost;
        }

        const float EscortJoinReach = 3.2f;
        const float EscortControlReach = 6f;
        const float EscortSeatReach = 5.5f;
        const float DoorDestinationReach = 0.8f;
        const float RetryEvery = 1.25f;

        readonly DemoCrews _crews;
        readonly AnimationClip _sitLoop;
        readonly List<SeatedBody> _bodies = new List<SeatedBody>();
        readonly List<CrewWalker> _officers = new List<CrewWalker>();
        readonly List<BoardingMan> _officerBoarding = new List<BoardingMan>();

        float _walkRetryAt;
        float _lastJeopardy = -1000f;
        float _provokedAt = -1000f;
        int _jeopardyRolls;
        bool _prisonerSeated;
        bool _footMarch;
        Vector3 _footTarget;
        BoardingMan _prisonerBoarding;

        public PrisonerCarriage(int characterId, CrewWalker prisoner,
            DemoCrews.Unit escort, RoadCar car, DemoCrews crews, AnimationClip sitLoop)
        {
            CharacterId = characterId;
            Prisoner = prisoner;
            Escort = escort;
            Car = car;
            _crews = crews;
            _sitLoop = sitLoop;
            Stage = CarriageStage.Calling;
            ReadEscort();
        }

        public int CharacterId { get; }
        public CrewWalker Prisoner { get; }
        public DemoCrews.Unit Escort { get; private set; }
        public RoadCar Car { get; private set; }
        public CarriageStage Stage { get; private set; }
        public bool PrisonerSeated => _prisonerSeated;
        public bool FootMarching => _footMarch;
        public int EscortBodies => _officers.Count;
        public bool EscortWiped => Escort != null && Escort.Wiped;
        public IReadOnlyList<SeatedBody> Bodies => _bodies;

        /// <summary>The transfer pair has the same small piece of police judgement as a
        /// beat: when its unit is provoked, find the nearby crew which actually ordered
        /// or fired the attack and answer it. This matters on the two walked legs; a bare
        /// temporary Unit otherwise has no dispatcher object ticking ReadProvocation.</summary>
        public DemoCrews.Unit ReadProvocation()
        {
            if (Escort == null || Escort.Wiped || _crews == null ||
                Escort.ProvokedAt <= _provokedAt)
                return null;
            _provokedAt = Escort.ProvokedAt;
            DemoCrews.Unit attacker = null;
            var best = float.MaxValue;
            foreach (var other in _crews.Units)
            {
                if (other == null || other == Escort || other.IsPolice || other.Wiped)
                    continue;
                if (other.TargetUnit != Escort &&
                    Time.time - other.PoliceFightOrderedAt > 2f)
                    continue;
                var delta = other.Position - Escort.Position;
                delta.y = 0f;
                var distance = delta.sqrMagnitude;
                if (distance >= best) continue;
                best = distance;
                attacker = other;
            }
            if (attacker != null) _crews.Sic(Escort, attacker);
            return attacker;
        }

        public void SetEscort(DemoCrews.Unit escort)
        {
            Escort = escort;
            ReadEscort();
        }

        public void ChangeCar(RoadCar car)
        {
            Car = car;
            _prisonerSeated = false;
            _footMarch = false;
            _jeopardyRolls = 0;
            _lastJeopardy = -1000f;
            _prisonerBoarding = null;
            _officerBoarding.Clear();
            Stage = CarriageStage.Boarding;
        }

        void ReadEscort()
        {
            _officers.Clear();
            if (Escort == null) return;
            foreach (var officer in Escort.All())
                if (officer != null && !officer.Dead && officer.Tf != null &&
                    _officers.Count < 2)
                    _officers.Add(officer);
        }

        /// <summary>Open the source door and let the real booked body walk out.</summary>
        public void BeginWalkingOut(Vector3 sourceDoor)
        {
            if (Prisoner == null || Prisoner.Tf == null) return;
            _footMarch = false;
            Prisoner.Disengage();
            Prisoner.Disarm();
            Prisoner.Surrendered = true;
            DoorBeat.SendOut(Prisoner);
            Stage = CarriageStage.WalkingOut;
            _prisonerBoarding = null;
            _officerBoarding.Clear();
        }

        /// <summary>Walk everybody to a physical door and seat them. True on the first
        /// tick the prisoner and every available officer are actually in the car.</summary>
        public bool TickBoarding()
        {
            if (Car?.Tf == null || Prisoner?.Tf == null || Prisoner.Dead)
                return false;

            // SendOut has a real exit beat. Do not steal the body while it is still
            // hidden on the far side of the threshold.
            if (Stage == CarriageStage.WalkingOut)
            {
                if (!Prisoner.Tf.gameObject.activeInHierarchy) return false;
                Stage = CarriageStage.Boarding;
            }
            if (Stage != CarriageStage.Boarding) return false;

            if (_prisonerBoarding == null)
            {
                var boarding = new BoardingMan
                {
                    Man = Prisoner,
                    Escort = EscortAt(0),
                    EscortUnit = Escort,
                    Car = Car,
                    Seat = 2,
                    Prisoner = true,
                    StartedAt = Time.time,
                };
                if (!BeginPrisonerBoarding(boarding, _crews)) return false;
                _prisonerBoarding = boarding;
            }

            if (!_prisonerBoarding.Seated)
                TickPrisonerBoarding(_prisonerBoarding, _crews, _sitLoop,
                    _bodies, prisonerCrew: null);
            _prisonerSeated = _prisonerBoarding.Seated;
            if (!_prisonerSeated) return false;

            if (_officerBoarding.Count == 0)
            {
                for (var i = 0; i < _officers.Count && i < 2; i++)
                {
                    var boarding = new BoardingMan
                    {
                        Man = _officers[i],
                        EscortUnit = Escort,
                        Car = Car,
                        Seat = i,
                        Prisoner = false,
                    };
                    if (BeginOfficerBoarding(boarding, _crews))
                        _officerBoarding.Add(boarding);
                }
            }

            for (var i = 0; i < _officerBoarding.Count; i++)
                if (!_officerBoarding[i].Seated)
                    TickOfficerBoarding(_officerBoarding[i], _crews, _sitLoop,
                        _bodies);
            if (!AllBoarded(_officerBoarding)) return false;
            Stage = CarriageStage.Riding;
            return true;
        }

        public void BeginHalt()
        {
            if (Stage == CarriageStage.Riding)
                Stage = CarriageStage.Halted;
        }

        /// <summary>Only unseat once the carrier has finished braking.</summary>
        public bool DismountHalted(Vector3 around)
        {
            if (!CustodyPlan.ShouldDismount(Stage,
                    Car == null || Mathf.Abs(Car.Speed) < 0.05f))
                return false;
            RestoreBodies(_bodies, around);
            _prisonerSeated = false;
            _footMarch = false;
            Prisoner.Surrendered = true;
            EscortAt(0)?.HoldAtGunpoint(Prisoner);
            return true;
        }

        /// <summary>The capped friendly-fire budget. The supplied sample makes this
        /// deterministic in contracts and lets the live caller use Unity's roll.</summary>
        public bool Jeopardy(float now, float sample, float chance)
        {
            if (!CustodyPlan.InJeopardy(Stage, _prisonerSeated,
                    now - _lastJeopardy, _jeopardyRolls))
                return false;
            _lastJeopardy = now;
            _jeopardyRolls++;
            var hit = sample < Mathf.Clamp01(chance);
            if (DriveTrace.On)
            {
                var sb = DriveTrace.Take();
                DriveTrace.Int(sb, "prisoner", CharacterId);
                DriveTrace.Int(sb, "roll", _jeopardyRolls);
                DriveTrace.Num(sb, "sample", sample, "F4");
                DriveTrace.Num(sb, "chance", chance, "F4");
                DriveTrace.Bool(sb, "hit", hit);
                DriveTrace.Row("jeopardy", sb.ToString());
            }
            return hit;
        }

        public void BeginWalkingIn(Vector3 door)
        {
            if (Prisoner == null || Prisoner.Tf == null) return;
            RestoreBodies(_bodies, Car != null ? Car.Position : door);
            _prisonerSeated = false;
            _footMarch = false;
            WalkIntoStation(Prisoner, door);
            if (Escort != null && !Escort.Wiped)
                _crews?.MarchTo(Escort, door + Vector3.right * 2.2f,
                    run: true, keepOffRoad: false, allowCustody: true);
            Stage = CarriageStage.WalkingIn;
            _walkRetryAt = Time.time + RetryEvery;
        }

        public bool CrossedThreshold =>
            Stage == CarriageStage.WalkingIn && DoorBeat.Held(Prisoner);

        /// <summary>Retry a failed doorway passage without ever replacing the walk with
        /// an arrival decree. DoorBeat owns its own stall/backstop logic; the carriage
        /// only asks again after that call has genuinely ended.</summary>
        public bool TickThreshold(Vector3 door)
        {
            if (CrossedThreshold) return true;
            if (Stage != CarriageStage.WalkingIn || _footMarch ||
                Prisoner == null || Prisoner.Dead || Prisoner.Tf == null)
                return false;
            if (!DoorBeat.Active(Prisoner) && Time.time >= _walkRetryAt)
            {
                _walkRetryAt = Time.time + RetryEvery;
                DoorBeat.MoveIn(Prisoner, door);
            }
            return false;
        }

        /// <summary>With no fresh car nearby the same escort may walk a short remaining
        /// leg. The exact prisoner moves alone through the shared routed-door command;
        /// his organization unit is deliberately not marched with him.</summary>
        public void BeginFootMarch(Vector3 destination)
        {
            if (Prisoner == null || Prisoner.Tf == null) return;
            RestoreBodies(_bodies, Car != null ? Car.Position : destination);
            _prisonerSeated = false;
            _footMarch = true;
            _footTarget = WalkObstacles.ClearSpot(
                destination, WalkObstacles.CrewTravelRadius, 4f);
            _footTarget.y = Prisoner.Tf.position.y;
            Prisoner.Surrendered = true;
            OrderFootMarch();
            Stage = CarriageStage.WalkingIn;
            _walkRetryAt = Time.time + RetryEvery;
        }

        public bool TickFootMarch(float reach = 3.5f)
        {
            if (!_footMarch || Stage != CarriageStage.WalkingIn ||
                Prisoner == null || Prisoner.Dead || Prisoner.Tf == null)
                return false;
            if (At(Prisoner, _footTarget, reach))
                return true;
            if (Time.time >= _walkRetryAt &&
                (!Prisoner.HasOrder || Prisoner.RoutedLegStalled))
            {
                _walkRetryAt = Time.time + RetryEvery;
                OrderFootMarch();
            }
            return false;
        }

        public void FinishFootMarch() => _footMarch = false;

        void OrderFootMarch()
        {
            _crews?.SendToVehicleDoor(Prisoner, _footTarget, graph: true);
            if (Escort != null && !Escort.Wiped)
                _crews?.MarchTo(Escort, _footTarget + Vector3.right * 2.2f,
                    run: true, keepOffRoad: false, allowCustody: true);
        }

        public void DeliverOffMap(Vector3 at)
        {
            RestoreBodies(_bodies, at);
            _prisonerSeated = false;
            _footMarch = false;
            if (Prisoner?.Tf != null)
            {
                Prisoner.Tf.position = at;
                Prisoner.Tf.gameObject.SetActive(false);
            }
            Stage = CarriageStage.Delivered;
        }

        public void MarkDelivered() => Stage = CarriageStage.Delivered;

        public void Restore(Vector3 around)
        {
            RestoreBodies(_bodies, around);
            _prisonerSeated = false;
            _footMarch = false;
        }

        public CrewWalker EscortAt(int wanted)
        {
            if (_officers.Count == 0) ReadEscort();
            CrewWalker first = null;
            var at = 0;
            for (var i = 0; i < _officers.Count; i++)
            {
                var officer = _officers[i];
                if (officer == null || officer.Dead || officer.Tf == null) continue;
                first ??= officer;
                if (at++ == wanted) return officer;
            }
            return first;
        }

        public static CrewWalker EscortAt(DemoCrews.Unit escort, int wanted)
        {
            if (escort == null || escort.Wiped) return null;
            var at = 0;
            CrewWalker first = null;
            foreach (var officer in escort.All())
            {
                if (officer == null || officer.Dead || officer.Tf == null) continue;
                first ??= officer;
                if (at++ == wanted) return officer;
            }
            return first;
        }

        /// <summary>Start the shared prisoner-and-covering-officer approach. The caller
        /// owns which prisoner, car and seat belong to this wave; the carriage owns the
        /// physical geometry and the retry-safe route.</summary>
        public static bool BeginPrisonerBoarding(BoardingMan boarding,
            DemoCrews crews)
        {
            if (boarding?.Man?.Tf == null || boarding.Car?.Tf == null) return false;
            if (!PrepareBoardingGeometry(boarding, boarding.Man.Tf.position))
                return false;
            boarding.Activated = true;
            boarding.Man.Disengage();
            // A new pickup supersedes any earlier transfer or foot-march order.
            // Wait at the physical position while the covering officer joins.
            boarding.Man.OrderToPoint(boarding.Man.Tf.position);
            OrderEscortToPrisoner(boarding, crews);
            return true;
        }

        /// <summary>Advance one prisoner from the pavement into his assigned rear seat.
        /// True means this body's physical boarding edge is complete.</summary>
        public static bool TickPrisonerBoarding(BoardingMan boarding,
            DemoCrews crews, AnimationClip sitLoop, List<SeatedBody> bodies,
            DemoCrews.Unit prisonerCrew)
        {
            if (boarding == null) return true;
            if (boarding.Seated) return true;
            var man = boarding.Man;
            if (man == null || man.Dead || man.Tf == null)
            {
                boarding.Seated = true;
                return true;
            }

            if (boarding.Escort == null || boarding.Escort.Dead ||
                boarding.Escort.Tf == null)
                boarding.Escort = EscortAt(boarding.EscortUnit, 0);
            var escort = boarding.Escort;
            if (escort == null || escort.Dead || escort.Tf == null)
                return false;

            var door = CarDoor(boarding);
            escort.HoldAtGunpoint(man);
            if (!boarding.Started)
            {
                if (Flat(escort.Tf.position - man.Tf.position).sqrMagnitude <=
                    EscortJoinReach * EscortJoinReach)
                {
                    boarding.Started = true;
                    boarding.StartedAt = Time.time;
                    OrderPairToRearDoor(boarding, crews, onlyIdle: false);
                }
                else if (CustodyPlan.ShouldRetryBoarding(
                             escort.HasOrder, atDestination: false,
                             retryElapsed: Time.time >= boarding.RetryAt,
                             routeStalled: escort.RoutedLegStalled))
                    OrderEscortToPrisoner(boarding, crews);
                return false;
            }

            // A temporary spread round the car must not erase two live routes. Only an
            // idle or genuinely stalled escort stops the prisoner and rejoins him.
            if (Flat(escort.Tf.position - man.Tf.position).sqrMagnitude >
                EscortControlReach * EscortControlReach)
            {
                if ((!escort.HasOrder || escort.RoutedLegStalled) &&
                    Time.time >= boarding.RetryAt)
                {
                    if (man.HasOrder) man.OrderToPoint(man.Tf.position);
                    boarding.Started = false;
                    OrderEscortToPrisoner(boarding, crews);
                }
                return false;
            }

            var atDoor = AtBoardingDoor(man, door);
            var escortBeside = Flat(escort.Tf.position - man.Tf.position)
                               .sqrMagnitude <= EscortSeatReach * EscortSeatReach;
            // The accepted door radius and the covering post can leave two idle
            // bodies just beyond seating reach. Close that gap instead of treating
            // both independent destinations as a completed boarding approach.
            if (atDoor && !escortBeside)
            {
                if (CustodyPlan.ShouldRetryBoarding(escort.HasOrder, false,
                        Time.time >= boarding.RetryAt, escort.RoutedLegStalled))
                    OrderEscortToPrisoner(boarding, crews);
                return false;
            }
            if (CustodyPlan.CanSeatPrisoner(atDoor, escortBeside))
            {
                DisarmPrisoner(prisonerCrew, man);
                var body = SeatBody(boarding.Car.Tf, man, boarding.Seat,
                    prisoner: true, sitLoop: sitLoop);
                if (body == null) return false;
                bodies?.Add(body);
                boarding.Seated = true;
                return true;
            }

            if (Time.time >= boarding.RetryAt)
                OrderPairToRearDoor(boarding, crews, onlyIdle: true);
            return false;
        }

        public static bool BeginOfficerBoarding(BoardingMan boarding,
            DemoCrews crews)
        {
            if (boarding?.Man?.Tf == null || boarding.Car?.Tf == null) return false;
            boarding.Man.LowerGunpoint();
            boarding.Started = true;
            boarding.StartedAt = Time.time;
            boarding.RetryAt = Time.time;
            if (!PrepareBoardingGeometry(boarding, boarding.Man.Tf.position))
                return false;
            OrderOfficerToSeat(boarding, crews, run: true);
            return true;
        }

        /// <summary>Advance one officer to his assigned front seat.</summary>
        public static bool TickOfficerBoarding(BoardingMan boarding,
            DemoCrews crews, AnimationClip sitLoop, List<SeatedBody> bodies)
        {
            if (boarding == null || boarding.Seated) return true;
            var man = boarding.Man;
            if (man == null || man.Dead || man.Tf == null)
            {
                boarding.Seated = true;
                return true;
            }
            var atDoor = AtBoardingDoor(man, CarDoor(boarding));
            if (atDoor)
            {
                man.LowerGunpoint();
                var body = SeatBody(boarding.Car.Tf, man, boarding.Seat,
                    prisoner: false, sitLoop: sitLoop);
                if (body == null) return false;
                bodies?.Add(body);
                boarding.Seated = true;
                return true;
            }
            if (CustodyPlan.ShouldRetryBoarding(
                    man.HasOrder, atDoor,
                    retryElapsed: Time.time >= boarding.RetryAt,
                    routeStalled: man.RoutedLegStalled))
                OrderOfficerToSeat(boarding, crews, run: true);
            return false;
        }

        public static bool AllBoarded(IReadOnlyList<BoardingMan> boarding)
        {
            if (boarding == null) return true;
            for (var i = 0; i < boarding.Count; i++)
                if (boarding[i] != null && !boarding[i].Seated) return false;
            return true;
        }

        static Vector3 EscortJoinSpot(BoardingMan boarding)
        {
            var man = boarding.Man;
            var car = boarding.Car.Tf;
            var toCar = Flat(car.position - man.Tf.position);
            if (toCar.sqrMagnitude < 0.04f) toCar = car.forward;
            toCar.Normalize();
            var side = Vector3.Cross(Vector3.up, toCar) *
                       (boarding.Seat % 2 == 0 ? -1f : 1f);
            var wanted = man.Tf.position - toCar * 0.8f + side * 1.1f;
            wanted.y = man.Tf.position.y;
            return WalkObstacles.ClearSpot(
                wanted, WalkObstacles.CrewTravelRadius, 3f);
        }

        static Vector3 EscortDoorSpot(BoardingMan boarding)
        {
            if (boarding == null) return Vector3.zero;
            if (!boarding.GeometryReady && boarding.Man?.Tf != null)
                PrepareBoardingGeometry(boarding, boarding.Man.Tf.position);
            return boarding.EscortPost;
        }

        static bool AtBoardingDoor(CrewWalker man, Vector3 door)
        {
            if (man?.Tf == null) return false;
            var reach = PoliceProcedure.CustodyStoppedDoorReach;
            return Flat(man.Tf.position - door).sqrMagnitude <= reach * reach;
        }

        static void OrderEscortToPrisoner(BoardingMan boarding, DemoCrews crews)
        {
            if (boarding?.Escort?.Tf == null || boarding.Man?.Tf == null) return;
            boarding.RetryAt = Time.time + RetryEvery;
            OrderCustodyLeg(boarding.Escort, EscortJoinSpot(boarding), run: true);
            boarding.Escort.HoldAtGunpoint(boarding.Man);
        }

        static void OrderPairToRearDoor(BoardingMan boarding, DemoCrews crews,
            bool onlyIdle)
        {
            if (boarding?.Man?.Tf == null || boarding.Escort?.Tf == null) return;
            var door = CarDoor(boarding);
            var escortSpot = EscortDoorSpot(boarding);
            var manAtDoor = AtBoardingDoor(boarding.Man, door);
            var escortAtPost = Flat(boarding.Escort.Tf.position - escortSpot)
                               .sqrMagnitude <=
                               DoorDestinationReach * DoorDestinationReach;
            var retryElapsed = Time.time >= boarding.RetryAt;
            var orderMan = !onlyIdle || CustodyPlan.ShouldRetryBoarding(
                boarding.Man.HasOrder, manAtDoor, retryElapsed,
                boarding.Man.RoutedLegStalled);
            var orderEscort = !onlyIdle || CustodyPlan.ShouldRetryBoarding(
                boarding.Escort.HasOrder, escortAtPost, retryElapsed,
                boarding.Escort.RoutedLegStalled);
            if (!orderMan && !orderEscort) return;

            boarding.RetryAt = Time.time + RetryEvery;
            if (orderMan)
                OrderBoarderToDoor(crews, boarding.Man, door, run: false);
            if (orderEscort)
                OrderCustodyLeg(boarding.Escort, escortSpot, run: false);
            boarding.Escort.HoldAtGunpoint(boarding.Man);
        }

        static bool PrepareBoardingGeometry(BoardingMan boarding, Vector3 from)
        {
            var tf = boarding?.Car?.Tf;
            if (tf == null) return false;
            var seats = CarBody.MeasureSeats(tf);
            if (seats == null || seats.Length == 0) return false;

            // The cushion and entry door are separate. Pick the nearest door from the
            // appropriate front/rear pair once, then keep it for the whole attempt.
            var firstDoor = boarding.Prisoner ? 2 : 0;
            var lastDoor = Mathf.Min(firstDoor + 2, seats.Length);
            if (firstDoor >= lastDoor)
            {
                firstDoor = Mathf.Clamp(boarding.Seat, 0, seats.Length - 1);
                lastDoor = firstDoor + 1;
            }
            var chosen = firstDoor;
            var best = float.MaxValue;
            for (var doorSeat = firstDoor; doorSeat < lastDoor; doorSeat++)
            {
                var candidate = VehicleDoor(tf, seats[doorSeat],
                    boarding.Car.HalfWidth);
                var distance = Flat(from - candidate).sqrMagnitude;
                if (distance >= best) continue;
                best = distance;
                chosen = doorSeat;
                boarding.Door = candidate;
            }

            var side = seats[chosen].x >= 0f ? 1f : -1f;
            boarding.EscortPost = boarding.Door + tf.right *
                (side * PoliceProcedure.CustodyEscortCarClearance);
            boarding.EscortPost.y = boarding.Door.y;
            boarding.GeometryReady = true;
            return true;
        }

        static void OrderBoarderToDoor(DemoCrews crews, CrewWalker man,
            Vector3 door, bool run)
        {
            if (man == null || man.Dead || man.Tf == null) return;
            // The approach must clear the car as well as nearby props. Otherwise
            // an adjustment toward the boarder can land inside the far flank, where
            // the parked-car detour cannot reach it. The real door still gates seating.
            if (!WalkObstacles.TryClearSpot(door, WalkObstacles.Radius, out var approach,
                    PoliceProcedure.CustodyStoppedDoorReach - DoorDestinationReach)) return;
            crews?.SendToVehicleDoor(man, approach);
            man.Urgent = run;
        }

        static bool OrderCustodyLeg(CrewWalker man, Vector3 target, bool run)
        {
            if (man == null || man.Dead || man.Tf == null) return false;
            // A short station-yard leg can still cross parked props. Local steering
            // cannot solve the row of pickups beside the cells; use the same proved
            // route as a longer walk. Open ground takes the planner's direct shortcut.
            var accepted = man.OrderAcross(target);
            if (accepted) man.Urgent = run;
            return accepted;
        }

        static void OrderOfficerToSeat(BoardingMan boarding, DemoCrews crews,
            bool run)
        {
            if (boarding?.Man?.Tf == null) return;
            boarding.RetryAt = Time.time + RetryEvery;
            OrderBoarderToDoor(crews, boarding.Man, CarDoor(boarding), run);
        }

        static Vector3 CarDoor(BoardingMan boarding)
        {
            if (boarding == null) return Vector3.zero;
            if (!boarding.GeometryReady && boarding.Man?.Tf != null)
                PrepareBoardingGeometry(boarding, boarding.Man.Tf.position);
            return boarding.Door;
        }

        static void DisarmPrisoner(DemoCrews.Unit crew, CrewWalker man)
        {
            if (man == null || man.Dead) return;
            man.Disarm();
            var underworld = LivingCity.Outfit.Underworld.Current;
            var roster = crew != null ? underworld?.Of(crew.Faction)?.Roster : null;
            PrisonPipeline.ConfiscateWeapons(roster, man.CharacterId);
            PersonnelDirector.Instance?.Touch();
        }

        public static void WalkIntoStation(CrewWalker man, Vector3 door)
        {
            if (man == null || man.Dead || man.Tf == null) return;
            man.Surrendered = true;
            DoorBeat.MoveIn(man, door);
        }

        public static SeatedBody SeatBody(Transform car, CrewWalker man, int index,
            bool prisoner, AnimationClip sitLoop)
        {
            if (car == null || man?.Tf == null) return null;
            var seats = CarBody.MeasureSeats(car);
            if (seats == null || seats.Length == 0) return null;
            var seat = seats[Mathf.Clamp(index, 0, seats.Length - 1)];
            var body = new SeatedBody
            {
                Man = man,
                Parent = man.Tf.parent,
                LocalScale = man.Tf.localScale,
                Renderers = man.Tf.GetComponentsInChildren<Renderer>(true),
                Prisoner = prisoner,
            };
            body.Shown = new bool[body.Renderers.Length];
            for (var i = 0; i < body.Renderers.Length; i++)
                body.Shown[i] = body.Renderers[i] != null && body.Renderers[i].enabled;

            man.Disengage();
            man.SetRiding(true);
            man.Tf.SetParent(car, false);
            man.Tf.localPosition = seat;
            man.Tf.localRotation = Quaternion.identity;
            man.Tf.localScale = body.LocalScale;
            body.Visual = CarOccupant.Seat(car, man.SourcePrefab, sitLoop, seat,
                man.Tf.gameObject.layer);
            for (var i = 0; i < body.Renderers.Length; i++)
                if (body.Renderers[i] != null) body.Renderers[i].enabled = false;
            return body;
        }

        public static void RestoreBodies(List<SeatedBody> bodies, Vector3 around)
        {
            if (bodies == null) return;
            var prisoner = 0;
            var escort = 0;
            for (var i = 0; i < bodies.Count; i++)
            {
                var body = bodies[i];
                if (body?.Visual != null) Object.Destroy(body.Visual.gameObject);
                if (body?.Man?.Tf == null) continue;
                body.Man.Tf.SetParent(body.Parent, true);
                body.Man.Tf.localScale = body.LocalScale;
                var n = body.Prisoner ? prisoner++ : escort++;
                var side = body.Prisoner ? -1f : 1f;
                body.Man.Tf.position = WalkObstacles.ClearSpot(
                    around + new Vector3(side * (2f + n * 0.8f), 0f,
                        (n % 2 == 0 ? -1f : 1f) * 1.2f),
                    WalkObstacles.CrewTravelRadius, 3f);
                body.Man.SetRiding(false);
                if (!body.Man.Tf.gameObject.activeSelf)
                    body.Man.Tf.gameObject.SetActive(true);
                for (var r = 0; r < body.Renderers.Length; r++)
                    if (body.Renderers[r] != null)
                        body.Renderers[r].enabled = body.Shown[r];
            }
            bodies.Clear();
        }

        public static Vector3 VehicleDoor(Transform car, Vector3 localSeat,
            float halfWidth)
        {
            if (car == null) return Vector3.zero;
            var side = localSeat.x >= 0f ? 1f : -1f;
            var door = car.position + car.right * (side * (halfWidth + 0.9f)) +
                       car.forward * localSeat.z;
            door.y = car.position.y;
            return door;
        }

        static bool At(CrewWalker man, Vector3 point, float reach)
        {
            if (man?.Tf == null) return false;
            var d = man.Tf.position - point;
            d.y = 0f;
            return d.sqrMagnitude <= reach * reach;
        }

        static Vector3 Flat(Vector3 value)
        {
            value.y = 0f;
            return value;
        }
    }
}
