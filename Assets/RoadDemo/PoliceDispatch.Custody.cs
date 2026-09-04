using System.Collections.Generic;
using LivingCity.Gameplay;
using LivingCity.Personnel;
using LivingCity.Police;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// The first leg of an arrest: hands up, a real car to the crew, the ride home,
    /// then the station threshold.  It deliberately lives outside the single collar
    /// slot; a prisoner crossing the city must not stop another officer making an
    /// arrest elsewhere.
    /// </summary>
    public sealed partial class PoliceDispatch
    {
        const float CustodyTransferPatience = 300f;

        enum CustodyStage
        {
            WaitingForCars,
            BoardingPrisoners,
            BoardingOfficers,
            Riding,
            WalkingIn,
            ReturnBoarding,
            Returning,
        }

        sealed class Custody
        {
            public DemoCrews.Unit Crew;
            public DemoCrews.Unit ArrestingSquad;
            public DemoCrews.Unit HoldingSquad;
            public PoliceBeat Beat;
            public Deed Deed;
            public DoorAnswer Answer;
            public CourtCase File;
            public CallOut Call;
            public PoliceForce.Precinct Precinct;
            public Vector3 Pickup;
            public CustodyStage Stage;
            public float By;
            public bool Finished;
            public readonly List<CustodyCar> Cars = new List<CustodyCar>();
            public readonly List<CustodyPrisoner> Prisoners =
                new List<CustodyPrisoner>();
            public readonly List<PrisonerCarriage.BoardingMan> Boarding =
                new List<PrisonerCarriage.BoardingMan>();
            public readonly List<PrisonerCarriage.SeatedBody> Bodies =
                new List<PrisonerCarriage.SeatedBody>();
        }

        sealed class CustodyPrisoner
        {
            public CrewWalker Man;
            public int CharacterId;
            public bool InWave;
            public bool Booked;
        }

        sealed class CustodyCar
        {
            public IPoliceUnit Ride;
            public DemoCrews.Unit Escort;
        }

        sealed class SeatedBody
        {
            public CrewWalker Man;
            public Transform Parent;
            public Vector3 LocalScale;
            public Renderer[] Renderers;
            public bool[] Shown;
            public CarOccupant Visual;
            public CustodyCar Car;
            public bool Prisoner;
        }

        sealed class BoardingMan
        {
            public CrewWalker Man;
            public CrewWalker Escort;
            public CustodyCar Car;
            public int Seat;
            public bool Prisoner;
            public bool Activated;
            public bool Started;
            public bool Seated;
            public float StartedAt;
            public float RetryAt;
            public bool GeometryReady;
            public Vector3 Door;
            public Vector3 EscortPost;
        }

        readonly List<Custody> _custodies = new List<Custody>();
        readonly List<PolicePatrolCar> _custodyCars = new List<PolicePatrolCar>();
        readonly List<CrewWalker> _custodyMen = new List<CrewWalker>();

        /// <summary>Custody itself is deliberately not serialized. Preserve the honest
        /// fallback in the snapshot instead: any unbooked man whose physical transfer
        /// disappears on load comes back on the street as a fugitive, while men who
        /// already crossed the station threshold remain in the prison snapshot.</summary>
        public void WriteCustodySaveFallback(LivingCity.Save.CampaignFile file)
        {
            var houses = file?.underworld?.houses;
            if (houses == null) return;
            for (var c = 0; c < _custodies.Count; c++)
            {
                var custody = _custodies[c];
                if (custody == null || custody.Finished || custody.Crew == null) continue;
                LivingCity.Personnel.RosterDto roster = null;
                for (var h = 0; h < houses.Length; h++)
                    if (houses[h] != null && houses[h].gangId == custody.Crew.Faction)
                    {
                        roster = houses[h].roster;
                        break;
                    }
                if (roster?.members == null) continue;

                for (var p = 0; p < custody.Prisoners.Count; p++)
                {
                    var prisoner = custody.Prisoners[p];
                    if (prisoner.Booked || prisoner.CharacterId < 0 ||
                        prisoner.Man == null || prisoner.Man.Dead)
                        continue;
                    for (var m = 0; m < roster.members.Length; m++)
                    {
                        var member = roster.members[m];
                        if (member == null || member.id != prisoner.CharacterId) continue;
                        if (WantedLevels.Severity(member.wantedLevel) <
                            WantedLevels.Severity(WantedLevels.Fled))
                            member.wantedLevel = WantedLevels.Fled;
                        member.hidingSince = 0;
                        break;
                    }
                }
            }
        }

        void BeginCustody(DemoCrews.Unit crew, Deed deed, CourtCase file,
            CallOut call, PoliceBeat beat, DemoCrews.Unit arrestingSquad,
            DoorAnswer answer)
        {
            if (crew == null || crew.Wiped) return;

            var custody = new Custody
            {
                Crew = crew,
                ArrestingSquad = arrestingSquad,
                Beat = beat,
                Deed = deed,
                Answer = answer,
                File = file,
                Call = call,
                Precinct = Force != null ? Force.Nearest(crew.Position) : null,
                Pickup = crew.Position,
                Stage = CustodyStage.WaitingForCars,
                By = Time.time + CollarPatience + CustodyTransferPatience,
            };

            // Fill the pickup's prisoner load from the hoods first. The lieutenant is the unit's
            // stable physical anchor, so leaving him with the final odd man keeps the
            // crew alive at the pickup while the first four cross the city.  Booking
            // him first would make Sync retire the whole physical unit and strand the
            // men who were meant to wait for the next car.
            for (var i = 0; i < crew.Hoods.Count; i++)
                AddCustodyPrisoner(custody, crew.Hoods[i]);
            AddCustodyPrisoner(custody, crew.Boss);

            crew.InCustody = true;
            crew.CustodyTracked = true;
            // A crew already filing into one of its own doors is brought back onto the
            // pavement before the law takes charge. Otherwise that old doorway beat
            // can keep moving surrendered men after every player order has been shut.
            CrewQuarters.CallOut(crew);
            if (call != null)
            {
                call.Transfer = custody;
                call.HomeBy = custody.By;
            }

            // A car which brought the arresting squad is the first carrier.  Claim its
            // response record now so TickSquad cannot send it home under the prisoners.
            var carrier = ClaimArrestingCar(arrestingSquad);
            if (carrier == null && call?.Unit != null && call.Unit.Carries)
                carrier = call.Unit;
            if (carrier != null)
                AddCustodyCar(custody, carrier, arrestingSquad);

            var living = LivingPrisoners(crew, _custodyMen);
            var wanted = Mathf.Max(1, CustodyPlan.CarsNeeded(living));
            if (Force != null && custody.Cars.Count < wanted)
            {
                Force.CollectCustodyCars(crew.Position, living, _custodyCars);
                for (var i = 0; i < _custodyCars.Count && custody.Cars.Count < wanted; i++)
                    AddCustodyCar(custody, _custodyCars[i], null);
            }

            for (var i = 0; i < custody.Cars.Count; i++)
            {
                var ride = custody.Cars[i].Ride;
                if (ride != carrier || !ride.OnScene)
                    ride.RouteTo(crew.Position, PoliceProcedure.CustodyCarStandOff);
                if (_lights.TryGetValue(ride, out var lights))
                    lights.Set(true, siren: false);
            }

            _custodies.Add(custody);
            CrewOverlay.AnnounceOurs(crew.Faction,
                "A CAR IS COMING FOR THE PRISONERS", 4f,
                new Color(0.55f, 0.78f, 1f));
        }

        static void AddCustodyPrisoner(Custody custody, CrewWalker man)
        {
            if (custody == null || man == null || man.Dead || man.Tf == null)
                return;
            custody.Prisoners.Add(new CustodyPrisoner
            {
                Man = man,
                CharacterId = man.CharacterId,
            });
        }

        IPoliceUnit ClaimArrestingCar(DemoCrews.Unit men)
        {
            if (men == null) return null;
            for (var i = _squads.Count - 1; i >= 0; i--)
            {
                if (_squads[i].Men != men) continue;
                var ride = _squads[i].Ride;
                _squads.RemoveAt(i);
                return ride;
            }
            return null;
        }

        static void AddCustodyCar(Custody custody, IPoliceUnit ride,
            DemoCrews.Unit escort)
        {
            if (custody == null || ride == null || ride.Tf == null) return;
            for (var i = 0; i < custody.Cars.Count; i++)
                if (custody.Cars[i].Ride == ride) return;
            custody.Cars.Add(new CustodyCar { Ride = ride, Escort = escort });
        }

        void TickCustody(float dt)
        {
            for (var i = _custodies.Count - 1; i >= 0; i--)
            {
                var custody = _custodies[i];
                TickCustody(custody);
                if (custody.Finished) _custodies.RemoveAt(i);
            }
        }

        void TickCustody(Custody custody)
        {
            if (custody == null || custody.Crew == null)
            {
                if (custody != null) custody.Finished = true;
                return;
            }
            if (custody.Crew.Wiped)
            {
                FinishCustody(custody);
                return;
            }
            ReassertCustody(custody);
            KeepCustodyCovered(custody);
            // Crossing the precinct threshold is booking. It wins over a wreck or an
            // escort loss in the same frame: once a man is through the door his first
            // leg is over even if the escort falls on that same tick.
            if (custody.Stage == CustodyStage.WalkingIn &&
                TickStationThresholds(custody))
                return;
            if (CustodyPlan.ShouldSpring(
                    CustodyWrecked(custody), EscortWiped(custody)))
            {
                Spring(custody);
                return;
            }

            switch (custody.Stage)
            {
                case CustodyStage.WaitingForCars:
                    var waiting = WaitingPrisoners(custody);
                    if (waiting == 0) { FinishBookedCustody(custody); return; }
                    var wanted = CustodyPlan.CarsNeeded(waiting);
                    if (custody.Cars.Count < wanted)
                        FindMoreCustodyCars(custody, waiting);
                    // One pickup can make another trip. The station keeps one car on
                    // duty, so waiting for an unavailable extra carrier must never turn
                    // the reserve rule into a deadlock.
                    if (custody.Cars.Count == 0 || !AllAtScene(custody))
                    {
                        if (Time.time >= custody.By)
                            ExtendPhysicalTransfer(custody);
                        return;
                    }
                    BoardCustody(custody);
                    return;

                case CustodyStage.BoardingPrisoners:
                    TickPrisonerBoarding(custody);
                    return;

                case CustodyStage.BoardingOfficers:
                    TickOfficerBoarding(custody, returning: false);
                    return;

                case CustodyStage.Riding:
                    if (AllAtStation(custody))
                    {
                        WalkIntoStation(custody);
                        return;
                    }
                    if (Time.time >= custody.By)
                        ExtendPhysicalTransfer(custody);
                    return;

                case CustodyStage.WalkingIn:
                    if (Time.time >= custody.By)
                        ExtendPhysicalTransfer(custody);
                    return;

                case CustodyStage.ReturnBoarding:
                    TickOfficerBoarding(custody, returning: true);
                    return;

                case CustodyStage.Returning:
                    if (AllAtScene(custody))
                    {
                        ArriveForNextWave(custody);
                        return;
                    }
                    if (Time.time >= custody.By)
                        ExtendPhysicalTransfer(custody);
                    return;
            }
        }

        static void ExtendPhysicalTransfer(Custody custody)
        {
            // A clock is recovery patience, never a teleport into jail. The real body
            // remains in the HUD and the physical stage keeps retrying until it crosses
            // the precinct threshold (or custody is genuinely broken).
            if (custody != null)
                custody.By = Time.time + CustodyTransferPatience;
        }

        static int WaitingPrisoners(Custody custody)
        {
            var count = 0;
            if (custody == null) return count;
            for (var i = 0; i < custody.Prisoners.Count; i++)
            {
                var prisoner = custody.Prisoners[i];
                if (prisoner.Booked || prisoner.InWave || prisoner.Man == null ||
                    prisoner.Man.Dead || prisoner.Man.Tf == null) continue;
                count++;
            }
            return count;
        }

        static bool HasUnbookedPrisoners(Custody custody)
        {
            if (custody == null) return false;
            for (var i = 0; i < custody.Prisoners.Count; i++)
            {
                var prisoner = custody.Prisoners[i];
                if (!prisoner.Booked && prisoner.Man != null &&
                    !prisoner.Man.Dead && prisoner.Man.Tf != null)
                    return true;
            }
            return false;
        }

        static int LivingPrisoners(DemoCrews.Unit crew, List<CrewWalker> into)
        {
            into.Clear();
            if (crew == null) return 0;
            foreach (var man in crew.All())
                if (man != null && !man.Dead && man.Tf != null)
                    into.Add(man);
            return into.Count;
        }

        void FindMoreCustodyCars(Custody custody, int prisoners)
        {
            if (Force == null || custody == null) return;
            var wanted = CustodyPlan.CarsNeeded(prisoners);
            var before = custody.Cars.Count;
            Force.CollectCustodyCars(custody.Crew.Position, prisoners, _custodyCars);
            for (var i = 0; i < _custodyCars.Count && custody.Cars.Count < wanted; i++)
                AddCustodyCar(custody, _custodyCars[i], null);
            for (var i = before; i < custody.Cars.Count; i++)
            {
                var ride = custody.Cars[i].Ride;
                ride.RouteTo(custody.Crew.Position,
                    PoliceProcedure.CustodyCarStandOff);
                if (_lights.TryGetValue(ride, out var lights))
                    lights.Set(true, siren: false);
            }
        }

        static bool AllAtScene(Custody custody)
        {
            for (var i = 0; i < custody.Cars.Count; i++)
                if (custody.Cars[i].Ride == null || !custody.Cars[i].Ride.OnScene)
                    return false;
            return true;
        }

        static bool CustodyWrecked(Custody custody)
        {
            for (var i = 0; i < custody.Cars.Count; i++)
            {
                var ride = custody.Cars[i].Ride;
                if (ride is PolicePatrolCar patrol && patrol.Wrecked) return true;
                if (ride is PoliceCruiser cruiser && cruiser.Car != null && cruiser.Car.Wrecked)
                    return true;
            }
            return false;
        }

        static bool EscortWiped(Custody custody)
        {
            // A beat left at the pickup owns the prisoners who did not fit this
            // trip.  It remains part of custody while the first load is on the road.
            if (WaitingPrisoners(custody) > 0 && custody.Beat != null &&
                (custody.Beat.Unit == null || custody.Beat.Unit.Wiped))
                return true;
            if (WaitingPrisoners(custody) > 0 && custody.HoldingSquad != null &&
                custody.HoldingSquad.Wiped)
                return true;
            for (var i = 0; i < custody.Cars.Count; i++)
                if (custody.Cars[i].Escort != null && custody.Cars[i].Escort.Wiped)
                    return true;
            return custody.Cars.Count == 0 && custody.ArrestingSquad != null &&
                   custody.ArrestingSquad.Wiped;
        }

        /// <summary>Custody is authoritative for every frame of the transfer. No input
        /// path or stray choreography may quietly clear these gates and hand a prisoner
        /// back to the player before the station threshold.</summary>
        static void ReassertCustody(Custody custody)
        {
            if (custody?.Crew == null) return;
            custody.Crew.InCustody = true;
            custody.Crew.Surrendered = true;
            for (var i = 0; i < custody.Prisoners.Count; i++)
            {
                var prisoner = custody.Prisoners[i];
                if (!prisoner.Booked && prisoner.Man != null && !prisoner.Man.Dead)
                    prisoner.Man.Surrendered = true;
            }
        }

        /// <summary>Every visible unbooked prisoner has a visible gun trained on him.
        /// The assignment is refreshed because a car load and the holding detail change
        /// as the transfer moves through its stages.</summary>
        void KeepCustodyCovered(Custody custody)
        {
            if (custody?.Crew == null) return;
            var beatTarget = NearestPrisonerToCover(custody,
                custody.Beat != null ? custody.Beat.Position : custody.Pickup);
            if (custody.Beat != null && beatTarget != null)
                custody.Beat.HoldAtGunpoint(beatTarget);
            HoldSquadAtGunpoint(custody.ArrestingSquad, custody);
            HoldSquadAtGunpoint(custody.HoldingSquad, custody);
            for (var i = 0; i < custody.Cars.Count; i++)
                HoldSquadAtGunpoint(custody.Cars[i].Escort, custody);
        }

        static void HoldSquadAtGunpoint(DemoCrews.Unit unit, Custody custody)
        {
            if (unit == null || unit.Wiped || unit.TargetUnit != null) return;
            foreach (var officer in unit.All())
            {
                if (officer == null || officer.Dead || officer.Tf == null || officer.Riding)
                    continue;
                var target = NearestPrisonerToCover(custody, officer.Tf.position);
                if (target != null) officer.HoldAtGunpoint(target);
                else officer.LowerGunpoint();
            }
        }

        static CrewWalker NearestPrisonerToCover(Custody custody, Vector3 from)
        {
            CrewWalker nearest = null;
            var best = float.MaxValue;
            for (var i = 0; i < custody.Prisoners.Count; i++)
            {
                var prisoner = custody.Prisoners[i];
                var man = prisoner.Man;
                if (man == null || man.Dead || man.Tf == null ||
                    !man.Tf.gameObject.activeInHierarchy ||
                    !CustodyPlan.MustCoverPrisoner(
                        custody.Crew.InCustody, prisoner.Booked, man.Riding))
                    continue;
                var distance = CustodyFlat(man.Tf.position - from).sqrMagnitude;
                if (distance >= best) continue;
                best = distance;
                nearest = man;
            }
            return nearest;
        }

        void BoardCustody(Custody custody)
        {
            if (WaitingPrisoners(custody) == 0)
            {
                FinishBookedCustody(custody);
                return;
            }
            custody.Boarding.Clear();

            // Every car has a two-man escort.  The response car already brought its
            // own; additional roster cars put theirs down beside the rear doors here.
            for (var i = 0; i < custody.Cars.Count; i++)
            {
                var load = custody.Cars[i];
                if (load.Escort != null && !load.Escort.Wiped) continue;
                var tf = load.Ride.Tf;
                var towards = CustodyFlat(custody.Crew.Position - tf.position);
                if (towards.sqrMagnitude < 0.01f) towards = tf.forward;
                load.Escort = SpawnSquad(tf.position + tf.right * 2.4f,
                    towards.normalized, 2, aboardOf: null);
            }

            var prisonerAt = 0;
            var loaded = 0;
            var waiting = WaitingPrisoners(custody);
            var tripCapacity = CustodyPlan.PrisonersThisTrip(waiting, custody.Cars.Count);

            // A car squad cannot drive away and leave an overflow prisoner alone. A
            // foot collar already has its beat pair; a car collar gets a holding detail
            // which stays at the pickup until the carrier returns for the last load.
            if (tripCapacity < waiting && custody.Beat == null &&
                (custody.HoldingSquad == null || custody.HoldingSquad.Wiped))
            {
                var face = custody.Cars[0].Ride.Tf != null
                    ? custody.Cars[0].Ride.Tf.forward : Vector3.forward;
                custody.HoldingSquad = SpawnSquad(
                    custody.Pickup + Vector3.right * 2.2f, face, 2, aboardOf: null);
                if (custody.HoldingSquad == null) return;
            }
            for (var i = 0; i < custody.Cars.Count; i++)
            {
                var load = custody.Cars[i];
                for (var p = 0; p < CustodyPlan.PrisonersPerPickup && loaded < tripCapacity; p++)
                {
                    CustodyPrisoner record = null;
                    while (prisonerAt < custody.Prisoners.Count)
                    {
                        var candidate = custody.Prisoners[prisonerAt++];
                        if (candidate.Booked || candidate.InWave || candidate.Man == null ||
                            candidate.Man.Dead || candidate.Man.Tf == null) continue;
                        record = candidate;
                        break;
                    }
                    if (record == null) break;
                    record.InWave = true;
                    BeginPrisonerEscort(custody, load, record.Man, 2 + p,
                        EscortAt(load.Escort, p % CustodyPlan.EscortSeats));
                    loaded++;
                }
            }

            if (loaded == 0) { FinishBookedCustody(custody); return; }

            custody.Stage = CustodyStage.BoardingPrisoners;
            custody.By = Time.time + CollarPatience;
            ActivateNextPrisoners(custody);
            CrewOverlay.AnnounceOurs(custody.Crew.Faction,
                "THE PRISONERS ARE BOARDING", 4f,
                new Color(0.55f, 0.78f, 1f));
        }

        void BeginPrisonerEscort(Custody custody, CustodyCar car, CrewWalker man,
            int seat, CrewWalker escort)
        {
            var roadCar = RoadCarOf(car);
            if (custody == null || roadCar?.Tf == null || man == null || man.Tf == null)
                return;
            var boarding = new PrisonerCarriage.BoardingMan
            {
                Man = man,
                Escort = escort,
                EscortUnit = car.Escort,
                Car = roadCar,
                Seat = seat,
                Prisoner = true,
                StartedAt = Time.time,
            };
            if (!PrisonerCarriage.BeginPrisonerBoarding(boarding, _crews)) return;
            custody.Boarding.Add(boarding);
            man.Disengage();
        }

        /// <summary>Two officers load the pickup in pairs. A named escort finishes one
        /// prisoner before taking the next, so assigning six men to the rear does not
        /// overwrite the officer's walk order six times in the same frame.</summary>
        static void ActivateNextPrisoners(Custody custody)
        {
            if (custody == null) return;
            for (var i = 0; i < custody.Boarding.Count; i++)
            {
                var next = custody.Boarding[i];
                if (!next.Prisoner || next.Seated || next.Activated || next.Escort == null)
                    continue;
                var busy = false;
                for (var earlier = 0; earlier < custody.Boarding.Count; earlier++)
                {
                    var current = custody.Boarding[earlier];
                    if (current == next || current.Escort != next.Escort ||
                        !current.Activated || current.Seated) continue;
                    busy = true;
                    break;
                }
                if (busy) continue;
                next.Activated = true;
                OrderEscortToPrisoner(next);
            }
        }

        void TickPrisonerBoarding(Custody custody)
        {
            ActivateNextPrisoners(custody);
            var allSeated = true;
            for (var i = 0; i < custody.Boarding.Count; i++)
            {
                var boarding = custody.Boarding[i];
                if (boarding.Seated) continue;
                allSeated = false;
                if (!boarding.Activated) continue;
                var man = boarding.Man;
                if (man == null || man.Dead || man.Tf == null)
                {
                    boarding.Seated = true;
                    continue;
                }

                if (boarding.Escort == null || boarding.Escort.Dead ||
                    boarding.Escort.Tf == null)
                    boarding.Escort = EscortAt(boarding.Car.Escort, 0);
                var escort = boarding.Escort;
                if (escort == null || escort.Dead || escort.Tf == null)
                    continue;

                var door = CarDoor(boarding);
                escort.HoldAtGunpoint(man);
                if (!boarding.Started)
                {
                    if (CustodyFlat(escort.Tf.position - man.Tf.position).sqrMagnitude <=
                        EscortJoinReach * EscortJoinReach)
                    {
                        boarding.Started = true;
                        boarding.StartedAt = Time.time;
                        OrderPairToRearDoor(boarding, onlyIdle: false);
                    }
                    else if (CustodyPlan.ShouldRetryBoarding(
                                 escort.HasOrder, atDestination: false,
                                 retryElapsed: Time.time >= boarding.RetryAt,
                                 routeStalled: escort.RoutedLegStalled))
                        OrderEscortToPrisoner(boarding);
                    continue;
                }

                // A transient spread while both men are walking round the car is not a
                // lost escort. Stopping and restarting the pair here used to erase both
                // walkers' remembered avoidance side every 1.25 seconds, so they chose a
                // different end of the car for ever. Only an idle, genuinely lost escort
                // stops the prisoner and rejoins him.
                if (CustodyFlat(escort.Tf.position - man.Tf.position).sqrMagnitude >
                    EscortControlReach * EscortControlReach)
                {
                    if ((!escort.HasOrder || escort.RoutedLegStalled) &&
                        Time.time >= boarding.RetryAt)
                    {
                        if (man.HasOrder) man.OrderToPoint(man.Tf.position);
                        boarding.Started = false;
                        OrderEscortToPrisoner(boarding);
                    }
                    continue;
                }

                var atDoor = AtBoardingDoor(man, door);
                var escortBeside = CustodyFlat(escort.Tf.position - man.Tf.position)
                                   .sqrMagnitude <= EscortSeatReach * EscortSeatReach;
                if (CustodyPlan.CanSeatPrisoner(atDoor, escortBeside))
                {
                    DisarmPrisoner(custody.Crew, man);
                    Seat(custody, boarding.Car, man, boarding.Seat, prisoner: true);
                    boarding.Seated = true;
                    continue;
                }

                if (Time.time >= boarding.RetryAt)
                    OrderPairToRearDoor(boarding, onlyIdle: true);
            }
            if (PrisonerCarriage.AllBoarded(custody.Boarding))
                BeginOfficerBoarding(custody, returning: false);
            else if (Time.time >= custody.By)
                ExtendPhysicalTransfer(custody);
        }

        void BeginOfficerBoarding(Custody custody, bool returning)
        {
            custody.Boarding.Clear();
            for (var i = 0; i < custody.Cars.Count; i++)
            {
                var load = custody.Cars[i];
                var seat = 0;
                if (load.Escort == null) continue;
                foreach (var officer in load.Escort.All())
                {
                    if (officer == null || officer.Dead || officer.Tf == null || seat >= 2)
                        continue;
                    var boarding = new PrisonerCarriage.BoardingMan
                    {
                        Man = officer,
                        EscortUnit = load.Escort,
                        Car = RoadCarOf(load),
                        Seat = seat,
                        Prisoner = false,
                    };
                    if (!PrisonerCarriage.BeginOfficerBoarding(boarding, _crews))
                        continue;
                    seat++;
                    custody.Boarding.Add(boarding);
                }
            }
            custody.Stage = returning
                ? CustodyStage.ReturnBoarding : CustodyStage.BoardingOfficers;
            custody.By = Time.time + PoliceProcedure.OfficerBoardingSeconds;
        }

        void TickOfficerBoarding(Custody custody, bool returning)
        {
            for (var i = 0; i < custody.Boarding.Count; i++)
            {
                var boarding = custody.Boarding[i];
                if (boarding.Seated) continue;
                PrisonerCarriage.TickOfficerBoarding(boarding, _crews,
                    _sitLoop, custody.Bodies);
            }

            if (!PrisonerCarriage.AllBoarded(custody.Boarding))
            {
                if (Time.time >= custody.By) ExtendPhysicalTransfer(custody);
                return;
            }
            if (returning) DepartForNextWave(custody);
            else DepartCustody(custody);
        }

        void DepartCustody(Custody custody)
        {
            for (var i = 0; i < custody.Cars.Count; i++)
            {
                var load = custody.Cars[i];
                if (_lights.TryGetValue(load.Ride, out var lights))
                    lights.Set(false, siren: false);
                // the men are in the back: the car drives to the station kerb, stops
                // there whether or not a bay is free, and waits to be unloaded. Set
                // BEFORE the release, which is what reads it to pick the station over
                // the round.
                if (load.Ride is PolicePatrolCar patrol) patrol.HoldAtKerb = true;
                load.Ride.Release();
            }
            custody.Stage = CustodyStage.Riding;
            custody.By = Time.time + CustodyTransferPatience;
            // If somebody did not fit, the beat keeps him at the pickup while these
            // cars make the first trip.  Only the last load releases the doorstep.
            if (WaitingPrisoners(custody) == 0)
            {
                ReleaseHoldingSquad(custody);
                if (custody.Beat != null) custody.Beat.Release();
                if (custody.Call != null)
                {
                    custody.Call.Unit = null;
                    custody.Call.Men = null;
                }
            }
            CrewOverlay.AnnounceOurs(custody.Crew.Faction,
                "THE PRISONERS ARE ON THEIR WAY TO THE STATION", 4f,
                new Color(0.55f, 0.78f, 1f));
        }

        void BeginReturnForNextWave(Custody custody)
        {
            BeginOfficerBoarding(custody, returning: true);
        }

        void DepartForNextWave(Custody custody)
        {
            for (var i = 0; i < custody.Cars.Count; i++)
            {
                var ride = custody.Cars[i].Ride;
                if (ride == null) continue;
                ride.RouteTo(custody.Pickup,
                    PoliceProcedure.CustodyCarStandOff);
                if (_lights.TryGetValue(ride, out var lights))
                    lights.Set(true, siren: false);
            }
            custody.Stage = CustodyStage.Returning;
            custody.By = Time.time + CustodyTransferPatience;
        }

        void ArriveForNextWave(Custody custody)
        {
            RestoreBodies(custody, custody.Pickup);
            custody.Boarding.Clear();
            custody.Stage = CustodyStage.WaitingForCars;
            custody.By = Time.time + CollarPatience + CustodyTransferPatience;
            BoardCustody(custody);
        }

        static RoadCar RoadCarOf(CustodyCar car)
        {
            if (car?.Ride is RoadCar roadCar) return roadCar;
            if (car?.Ride is PoliceCruiser cruiser) return cruiser.Car;
            return null;
        }

        static Vector3 PrisonerCargoPoint(Vector3[] seats, int seatIndex)
        {
            var slot = Mathf.Clamp(seatIndex - CustodyPlan.EscortSeats, 0,
                CustodyPlan.PrisonersPerPickup - 1);
            var rear = seats[Mathf.Min(4, seats.Length - 1)];
            return new Vector3(
                slot % 2 == 0 ? -0.32f : 0.32f,
                rear.y,
                rear.z - (slot / 2) * 0.18f);
        }

        bool AllAtStation(Custody custody)
        {
            for (var i = 0; i < custody.Cars.Count; i++)
            {
                var ride = custody.Cars[i].Ride;
                if (ride is PolicePatrolCar patrol)
                {
                    // the kerb is the threshold, not the bay: a car that found every
                    // bay held used to go back round the city with its load
                    if (!patrol.AtHomeKerb) return false;
                }
                else if (ride is PoliceCruiser cruiser)
                {
                    if (!cruiser.AtHome) return false;
                }
                else if (custody.Precinct == null ||
                         CustodyFlat(ride.Position - custody.Precinct.Door).sqrMagnitude >
                         20f * 20f) return false;
            }
            return true;
        }

        void WalkIntoStation(Custody custody)
        {
            var door = custody.Precinct != null
                ? custody.Precinct.Door
                : custody.Cars.Count > 0 ? custody.Cars[0].Ride.Position
                : custody.Crew.Position;
            RestoreBodies(custody, door);
            custody.Boarding.Clear();
            custody.Crew.InCustody = true;
            custody.Crew.Surrendered = true;
            // Only this car's load walks through the station door.  Men held at the
            // original doorstep are not marched across the map by a whole-crew order;
            // they wait visibly for these cars to return.
            for (var i = 0; i < custody.Prisoners.Count; i++)
            {
                var prisoner = custody.Prisoners[i];
                var man = prisoner.Man;
                if (!prisoner.InWave || prisoner.Booked || man == null || man.Dead ||
                    man.Tf == null) continue;
                PrisonerCarriage.WalkIntoStation(man, door);
            }
            for (var i = 0; i < custody.Cars.Count; i++)
            {
                var escort = custody.Cars[i].Escort;
                if (custody.Cars[i].Ride is PolicePatrolCar patrol) patrol.HoldAtKerb = false;
                if (escort == null || escort.Wiped) continue;
                _crews.MarchTo(escort, door + Vector3.right * (2f + i * 1.4f));
            }
            custody.Stage = CustodyStage.WalkingIn;
            custody.By = Time.time + CollarPatience;
            CrewOverlay.AnnounceOurs(custody.Crew.Faction,
                "THE PRISONERS ARE AT THE STATION", 4f,
                new Color(0.55f, 0.78f, 1f));
        }

        /// <summary>Books each man only after his own threshold crossing. Returns true
        /// when the current wave has ended and changed custody stage.</summary>
        bool TickStationThresholds(Custody custody)
        {
            for (var i = 0; i < custody.Prisoners.Count; i++)
            {
                var prisoner = custody.Prisoners[i];
                if (!prisoner.InWave || prisoner.Booked) continue;
                var man = prisoner.Man;
                if (man == null || man.Dead || man.Tf == null)
                {
                    prisoner.InWave = false;
                    continue;
                }
                if (!CustodyPlan.CanBook(DoorBeat.Held(man))) continue;
                if (!_crews.TakeInOne(custody.Crew, man, custody.Deed,
                        Force != null ? Force.Pipeline : null, custody.File,
                        custody.Answer, sprung: custody.Crew.CustodySprung))
                    continue;
                Force?.PinCustody(prisoner.CharacterId, man.Tf.position);
                prisoner.Booked = true;
                prisoner.InWave = false;
            }

            for (var i = 0; i < custody.Prisoners.Count; i++)
            {
                var prisoner = custody.Prisoners[i];
                if (prisoner.InWave && !prisoner.Booked && prisoner.Man != null &&
                    !prisoner.Man.Dead && prisoner.Man.Tf != null)
                    return false;
            }

            if (HasUnbookedPrisoners(custody))
                BeginReturnForNextWave(custody);
            else
                FinishBookedCustody(custody);
            return true;
        }

        void FinishBookedCustody(Custody custody)
        {
            CrewQuarters.Forget(custody.Crew);
            // Booking advances the court pipeline, but it does not retire the HUD
            // identity. Keep it command-locked while the pipeline physically holds
            // him; the release transitions clear this latch and its position pin.
            custody.Crew.InCustody = true;
            custody.Crew.Surrendered = true;
            custody.Crew.CustodyTracked = true;
            custody.Crew.CustodySprung = false;
            LawWire.TakenIn(custody.Call?.Call, custody.Crew);
            FinishCustody(custody);
        }

        void Spring(Custody custody)
        {
            var at = custody.Cars.Count > 0 && custody.Cars[0].Ride != null
                ? custody.Cars[0].Ride.Position : custody.Crew.Position;
            RestoreBodies(custody, at);
            CrewQuarters.Forget(custody.Crew);
            custody.Crew.InCustody = false;
            custody.Crew.Surrendered = false;
            custody.Crew.CustodyTracked = false;
            custody.Crew.CustodySprung = true;
            PrisonPipeline.AttachCharge(custody.File, Deed.Resisting);
            if (custody.Call != null) custody.Call.MenRefused = true;

            var underworld = LivingCity.Outfit.Underworld.Current;
            var roster = underworld?.Of(custody.Crew.Faction)?.Roster;
            var today = Today();
            for (var i = 0; i < custody.Prisoners.Count; i++)
            {
                var prisoner = custody.Prisoners[i];
                var man = prisoner.Man;
                if (prisoner.Booked || man == null || man.Dead) continue;
                prisoner.InWave = false;
                man.Surrendered = false;
                DoorBeat.SendOut(man);
                if (prisoner.CharacterId < 0) continue;
                WantedLevels.Mark(roster?.Find(prisoner.CharacterId),
                    WantedLevels.FreedFromTransfer, today);
                Force?.Pipeline.Sprung(roster, prisoner.CharacterId, today);
            }
            PersonnelDirector.Instance?.Touch();
            LawWire.Sprung(custody.Call?.Call, custody.Crew);
            CrewOverlay.AnnounceOurs(custody.Crew.Faction,
                "THE PRISONERS ARE SPRUNG", 5f,
                new Color(1f, 0.72f, 0.35f));
            FinishCustody(custody);
        }

        void RestoreBodies(Custody custody, Vector3 around)
        {
            PrisonerCarriage.RestoreBodies(custody.Bodies, around);
        }

        void FinishCustody(Custody custody)
        {
            RestoreBodies(custody, custody.Crew != null ? custody.Crew.Position : Vector3.zero);
            if (custody.Call != null)
            {
                custody.Call.Transfer = custody;
                // Custody owns and releases every carrier and escort. Leaving these
                // references behind makes the complaint close them a second time.
                custody.Call.Unit = null;
                custody.Call.Men = null;
            }
            if (custody.Beat != null && custody.Beat.Unit != null && !custody.Beat.Unit.Wiped)
                custody.Beat.Release();
            for (var i = 0; i < custody.Cars.Count; i++)
            {
                var load = custody.Cars[i];
                if (_lights.TryGetValue(load.Ride, out var lights))
                    lights.Set(false, siren: false);
                if (load.Ride is PoliceCruiser cruiser && load.Escort != null &&
                    !load.Escort.Wiped && cruiser.Car != null)
                    _crews.BoardCar(load.Escort, cruiser.Car);
                else if (load.Escort != null && !load.Escort.Wiped)
                    _crews.RemoveUnit(load.Escort);
                // whatever ended it, nothing is in the back any more
                if (load.Ride is PolicePatrolCar patrol) patrol.HoldAtKerb = false;
                load.Ride?.Release();
            }
            ReleaseHoldingSquad(custody);
            custody.Finished = true;
        }

        void ReleaseHoldingSquad(Custody custody)
        {
            if (custody?.HoldingSquad == null) return;
            if (!custody.HoldingSquad.Wiped)
                _crews.RemoveUnit(custody.HoldingSquad);
            custody.HoldingSquad = null;
        }

        static Vector3 CustodyFlat(Vector3 value)
        {
            value.y = 0f;
            return value;
        }
    }
}
