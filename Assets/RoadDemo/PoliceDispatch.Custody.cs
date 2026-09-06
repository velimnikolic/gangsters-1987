using System.Collections.Generic;
using LivingCity.Gameplay;
using LivingCity.Personnel;
using LivingCity.Police;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>Physical arrest pickup and booking, independent of the collar slot.</summary>
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
            public bool WalkingIn;
            public bool Booked;
            public float StationRetryAt;
        }

        sealed class CustodyCar
        {
            public IPoliceUnit Ride;
            public DemoCrews.Unit Escort;
            public bool OnFoot;
        }

        readonly List<Custody> _custodies = new List<Custody>();
        readonly List<PolicePatrolCar> _custodyCars = new List<PolicePatrolCar>();
        readonly List<CrewWalker> _custodyMen = new List<CrewWalker>();

        // Dispatch owns the carriers; custody/convoys must unseat their real bodies
        // before the vehicle root is destroyed. An engine failure grants no release.
        void PrepareCarRemoval(PolicePatrolCar car)
        {
            Force?.PrepareCarRemoval(car);
            Unregister(car);
            foreach (var custody in _custodies)
            {
                var load = custody.Cars.Find(candidate => candidate.Ride == car);
                if (custody.Finished || load == null) continue;
                if (car.Wrecked || EscortWiped(custody) || custody.Crew == null || custody.Crew.Wiped)
                {
                    TickCustody(custody); // Preserve death, escape and booking precedence.
                    continue;
                }
                load.OnFoot = custody.Stage == CustodyStage.WalkingIn;
                foreach (var prisoner in custody.Prisoners)
                {
                    if (!prisoner.InWave || prisoner.Booked || prisoner.Man?.Tf == null) continue;
                    if (prisoner.Man.Tf.parent != car.Tf && !custody.Boarding.Exists(
                        boarding => boarding.Car == car && boarding.Man == prisoner.Man)) continue;
                    prisoner.WalkingIn = load.OnFoot = true;
                }
                PrisonerCarriage.RestoreBodies(custody.Bodies, car.Position,
                    atEachCarrier: true, onlyCarrier: car.Tf);
                custody.Boarding.RemoveAll(boarding => boarding.Car == car);
                if (!load.OnFoot)
                {
                    RetireCustodyCars(custody, car);
                    if (custody.Cars.Count == 0) custody.Stage = CustodyStage.WaitingForCars;
                    continue;
                }
                var door = custody.Precinct != null ? custody.Precinct.Door : custody.Pickup;
                foreach (var prisoner in custody.Prisoners)
                    if (prisoner.InWave && prisoner.WalkingIn && !prisoner.Booked)
                    {
                        PrisonerCarriage.WalkIntoStation(prisoner.Man, door);
                        prisoner.StationRetryAt = Time.time + 5f;
                    }
                if (load.Escort != null && !load.Escort.Wiped)
                    _crews.MarchTo(load.Escort, door + Vector3.right * 2.2f);
            }
        }

        void RetireCustodyCars(Custody custody, PolicePatrolCar only = null)
        {
            for (var i = custody.Cars.Count - 1; i >= 0; i--)
            {
                var load = custody.Cars[i];
                if (!(load.Ride is PolicePatrolCar patrol) || patrol.Fleetworthy ||
                    (only != null && patrol != only)) continue;
                if (load.Escort != null && !load.Escort.Wiped) _crews.RemoveUnit(load.Escort);
                patrol.HoldAtKerb = patrol.CustodyReserved = false;
                custody.Cars.RemoveAt(i);
            }
        }

        internal bool KeepsUnbookedBody(int characterId)
        {
            foreach (var custody in _custodies)
            {
                if (custody.Finished) continue;
                foreach (var prisoner in custody.Prisoners)
                    if (prisoner.CharacterId == characterId && !prisoner.Booked &&
                        prisoner.Man != null && !prisoner.Man.Dead)
                        return true;
            }
            return false;
        }

        /// <summary>Unserialized transfers load unbooked men as fugitives;
        /// threshold-booked men remain in the prison snapshot.</summary>
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

            // Take hoods first: the lieutenant anchors the physical unit until the
            // final wave; booking him earlier lets Sync retire the waiting men.
            for (var i = 0; i < crew.Hoods.Count; i++)
                AddCustodyPrisoner(custody, crew.Hoods[i]);
            AddCustodyPrisoner(custody, crew.Boss);

            crew.InCustody = true;
            crew.CustodyTracked = true;
            // Cancel existing doorway movement before custody takes command.
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

            var living = PrisonerCarriage.ReadLivingBodies(crew, _custodyMen);
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

        void AddCustodyCar(Custody custody, IPoliceUnit ride,
            DemoCrews.Unit escort)
        {
            if (custody == null || ride == null || ride.Tf == null) return;
            for (var i = 0; i < custody.Cars.Count; i++)
                if (custody.Cars[i].Ride == ride) return;
            foreach (var other in _custodies)
                if (!other.Finished)
                    foreach (var load in other.Cars)
                        if (load.Ride == ride) return;
            if (ride is PolicePatrolCar patrol) patrol.CustodyReserved = true;
            if (ride is PoliceCruiser cruiser) cruiser.CustodyReserved = true;
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
            // Physical booking wins over a wreck or escort loss on the same tick.
            if ((custody.Stage == CustodyStage.WalkingIn ||
                 custody.Prisoners.Exists(p => p.InWave && p.WalkingIn)) &&
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
                    // One pickup can make repeat trips while the station keeps its reserve.
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
            // Retry the physical leg; elapsed time alone cannot book a prisoner.
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

        void FindMoreCustodyCars(Custody custody, int prisoners)
        {
            if (Force == null || custody == null) return;
            var wanted = CustodyPlan.CarsNeeded(prisoners);
            var before = custody.Cars.Count;
            Force.CollectCustodyCars(custody.Pickup, prisoners, _custodyCars);
            for (var i = 0; i < _custodyCars.Count && custody.Cars.Count < wanted; i++)
                AddCustodyCar(custody, _custodyCars[i], null);
            for (var i = before; i < custody.Cars.Count; i++)
            {
                var ride = custody.Cars[i].Ride;
                ride.RouteTo(custody.Pickup,
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
            // The pickup's holding detail remains part of custody during each trip.
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

        /// <summary>Custody owns input and surrender gates through physical booking.</summary>
        void ReassertCustody(Custody custody)
        {
            if (custody?.Crew == null) return;
            // Custody may still hold a hood after the lieutenant has posted bail.
            bool hasCommander = custody.Crew.Boss != null && !custody.Crew.Boss.Dead;
            bool held = hasCommander && CustodyHolds(custody, custody.Crew.Boss);
            if (!hasCommander)
                foreach (var man in custody.Crew.All())
                    held |= CustodyHolds(custody, man);
            custody.Crew.InCustody = held;
            custody.Crew.Surrendered = held;
            if (!held) custody.Crew.CustodyTracked = false;
            for (var i = 0; i < custody.Prisoners.Count; i++)
            {
                var prisoner = custody.Prisoners[i];
                if (!prisoner.Booked && prisoner.Man != null && !prisoner.Man.Dead)
                    prisoner.Man.Surrendered = true;
            }
        }

        bool CustodyHolds(Custody custody, CrewWalker man)
        {
            if (man == null || man.Dead) return false;
            if (Force != null && Force.KeepsCustodyAlive(man.CharacterId)) return true;
            foreach (var prisoner in custody.Prisoners)
                if (prisoner.Man == man && !prisoner.Booked) return true;
            return false;
        }

        /// <summary>Refresh guards as the load and holding detail change.</summary>
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

            // Additional carriers dismount their two-man escorts here.
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

            // A car collar leaves a holding detail with overflow prisoners;
            // a foot collar already has its beat pair.
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
                        PrisonerCarriage.EscortAt(load.Escort, p % CustodyPlan.EscortSeats));
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
            var roadCar = PrisonerCarriage.CarrierOf(car.Ride);
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
            custody.Boarding.Add(boarding);
            man.Disengage();
        }

        /// <summary>Each escort finishes one prisoner before taking the next.</summary>
        void ActivateNextPrisoners(Custody custody)
        {
            if (custody == null) return;
            for (var i = 0; i < custody.Boarding.Count; i++)
            {
                var next = custody.Boarding[i];
                if (next.Man == null || next.Man.Dead || next.Man.Tf == null)
                {
                    next.Seated = true;
                    continue;
                }
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
                PrisonerCarriage.BeginPrisonerBoarding(next, _crews);
            }
        }

        void TickPrisonerBoarding(Custody custody)
        {
            ActivateNextPrisoners(custody);
            for (var i = 0; i < custody.Boarding.Count; i++)
            {
                var boarding = custody.Boarding[i];
                if (boarding.Seated) continue;
                if (!boarding.Activated) continue;
                PrisonerCarriage.TickPrisonerBoarding(boarding, _crews,
                    _sitLoop, custody.Bodies, custody.Crew);
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
                if (load.Escort == null || load.OnFoot) continue;
                foreach (var officer in load.Escort.All())
                {
                    if (officer == null || officer.Dead || officer.Tf == null || seat >= 2)
                        continue;
                    var boarding = new PrisonerCarriage.BoardingMan
                    {
                        Man = officer,
                        EscortUnit = load.Escort,
                        Car = PrisonerCarriage.CarrierOf(load.Ride),
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
                if (load.OnFoot) continue;
                if (_lights.TryGetValue(load.Ride, out var lights))
                    lights.Set(false, siren: false);
                // Set before Release so a loaded car waits at its station kerb.
                if (load.Ride is PolicePatrolCar patrol) patrol.HoldAtKerb = true;
                load.Ride.Release();
            }
            custody.Stage = CustodyStage.Riding;
            custody.By = Time.time + CustodyTransferPatience;
            // Only the final load releases the holding detail.
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
            RetireCustodyCars(custody);
            if (custody.Cars.Count == 0)
            {
                custody.Stage = CustodyStage.WaitingForCars;
                ExtendPhysicalTransfer(custody);
                return;
            }
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

        bool AllAtStation(Custody custody)
        {
            for (var i = 0; i < custody.Cars.Count; i++)
            {
                if (custody.Cars[i].OnFoot) continue;
                var ride = custody.Cars[i].Ride;
                if (ride is PolicePatrolCar patrol)
                {
                    // Unload at the kerb even when every bay is occupied.
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
            ReassertCustody(custody);
            // Only this wave walks in; overflow prisoners wait at the pickup.
            for (var i = 0; i < custody.Prisoners.Count; i++)
            {
                var prisoner = custody.Prisoners[i];
                var man = prisoner.Man;
                if (!prisoner.InWave || prisoner.Booked || man == null || man.Dead ||
                    man.Tf == null) continue;
                prisoner.WalkingIn = true;
                PrisonerCarriage.WalkIntoStation(man, door);
                prisoner.StationRetryAt = Time.time + 5f;
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
                "THE ESCORT IS WALKING THE PRISONERS IN", 4f,
                new Color(0.55f, 0.78f, 1f));
        }

        /// <summary>Book physical crossings; true when the wave changes stage.</summary>
        bool TickStationThresholds(Custody custody)
        {
            for (var i = 0; i < custody.Prisoners.Count; i++)
            {
                var prisoner = custody.Prisoners[i];
                if (!prisoner.InWave || prisoner.Booked || !prisoner.WalkingIn) continue;
                var man = prisoner.Man;
                if (man == null || man.Dead || man.Tf == null)
                {
                    prisoner.InWave = false;
                    continue;
                }
                if (!CustodyPlan.CanBook(DoorBeat.Held(man)))
                {
                    // Retry an interrupted door approach under the same custody order.
                    if (!DoorBeat.Active(man) && Time.time >= prisoner.StationRetryAt && custody.Precinct != null)
                    {
                        prisoner.StationRetryAt = Time.time + 5f;
                        PrisonerCarriage.WalkIntoStation(man, custody.Precinct.Door);
                    }
                    continue;
                }
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
            // Keep the HUD identity locked while the pipeline holds the boss;
            // finishing a hood's booking cannot detain a boss already released on bail.
            bool bossHeld = custody.Crew.Boss != null && Force != null &&
                Force.KeepsCustodyAlive(custody.Crew.Boss.CharacterId);
            custody.Crew.InCustody = bossHeld;
            custody.Crew.Surrendered = bossHeld;
            custody.Crew.CustodyTracked = bossHeld;
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
                // Custody owns release; prevent a second release by the complaint.
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
                if (load.Ride is PolicePatrolCar patrol)
                {
                    patrol.HoldAtKerb = false;
                    patrol.CustodyReserved = false;
                }
                if (load.Ride is PoliceCruiser reservedCruiser)
                    reservedCruiser.CustodyReserved = false;
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
