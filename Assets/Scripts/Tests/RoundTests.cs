using System.Collections.Generic;
using LivingCity.Outfit;
using LivingCity.Territory;

namespace LivingCity.Tests
{
    /// <summary>
    /// RIVAL-004. The round walked with no city at all: the shop owes, the men go out,
    /// the doors settle, the bag comes home and the safe grows - all of it through the
    /// pure <see cref="TerritoryRoundLedger"/>, for whichever house asked.
    ///
    /// The two clocks are the point. A round moved by bodies on a pavement and the same
    /// round moved by <see cref="TerritoryPaperClock"/> call the ledger's three methods
    /// in the same order, so they must be worth the same money to the penny.
    /// </summary>
    public static class RoundTests
    {
        static readonly TerritoryBlockId Street = new TerritoryBlockId("block:corner");
        static readonly TerritoryBusinessId Shop =
            new TerritoryBusinessId("biz:corner-shop");
        static readonly TerritoryBusinessId Bar = new TerritoryBusinessId("biz:bar");

        /// <summary>Weekly rate and days accrued that leave a door owing exactly $700 -
        /// the dues meter carries sevenths and pays out whole days.</summary>
        const int WeeklyRate = 700;
        const int WeekDays = 7;

        public static List<string> Run()
        {
            var failures = new List<string>();

            APaperRoundBringsTheTakeHome(failures);
            EveryHouseCollectsTheSameWay(failures);
            AnAbandonedRoundLosesWhatItCarried(failures);
            TwoMissesLetTheArrangementLapse(failures);
            BothClocksAreWorthTheSameMoney(failures);
            AShakedownCarriesNothingHome(failures);

            return failures;
        }

        // ------------------------------------------------------------------ the rig

        /// <summary>A bench city: one block, two doors twenty metres apart, and a front
        /// to walk the bag to. No scene, no bodies, no geography service - the round only
        /// ever needed points and sums.</summary>
        sealed class Rig
        {
            public readonly TerritoryRacketLedger Racket = new TerritoryRacketLedger();
            public readonly TerritoryDuesLedger Dues = new TerritoryDuesLedger();
            public readonly TerritoryRoundLedger Ledger;
            public readonly Dictionary<TerritoryBusinessId, TerritoryPoint> Doorsteps =
                new Dictionary<TerritoryBusinessId, TerritoryPoint>
                {
                    { Shop, new TerritoryPoint(0f, 0f) },
                    { Bar, new TerritoryPoint(20f, 0f) },
                };

            public readonly TerritoryPoint Front = new TerritoryPoint(0f, 60f);

            public Rig()
            {
                Ledger = new TerritoryRoundLedger(Racket, Dues);
            }

            /// <summary>The door pays this house, and a week of the meter has run.
            /// </summary>
            public void OweUs(TerritoryBusinessId businessId, TerritoryGangId house)
            {
                Racket.Demand(businessId, house, Strong(), 1.0, out _);
                for (var day = 0; day < WeekDays; day++)
                    Dues.AccrueDay(businessId, house, WeeklyRate);
            }

            public List<TerritoryRoundStop> Stops(params TerritoryBusinessId[] doors)
            {
                var stops = new List<TerritoryRoundStop>();
                for (var i = 0; i < doors.Length; i++)
                    stops.Add(new TerritoryRoundStop(doors[i], Doorsteps[doors[i]]));
                return stops;
            }

            /// <summary>What the world says about a door - the one thing the ledger asks
            /// of anybody. Fixed here, so a settlement is a function of the roll alone.
            /// </summary>
            public TerritoryStopInputs Inputs(
                TerritoryRound round, TerritoryRoundStop stop, int day) =>
                new TerritoryStopInputs(
                    true,
                    Dues.OwedOf(stop.BusinessId, round.House),
                    TerritoryOwnerProfile.Deal(1987, stop.BusinessId),
                    40f, 5f,
                    (int)Personnel.CrewPolicy.Normal,
                    (int)Personnel.LieutenantArchetype.Soldier,
                    1987, day);
        }

        static TerritoryComplianceInputs Strong() =>
            new TerritoryComplianceInputs(70f, 60f, 10f, 0f, 0f, false);

        static TerritoryGangId Gang(int id) => new TerritoryGangId(id);

        /// <summary>A house on its own, with its own safe - no Underworld, no city.
        /// </summary>
        static House HouseOf(int gangId)
        {
            var roster = Personnel.RosterSeeder.Generate(1987, gangId);
            return new House(gangId, roster, new CampaignRunner { Seed = 1987 });
        }

        /// <summary>Walks a round to its end on the paper clock. Answers what was banked.
        /// </summary>
        static int WalkOnPaper(Rig rig, TerritoryRound round, double from)
        {
            var clock = new TerritoryPaperClock(rig.Ledger);
            var banked = 0;
            clock.Send(round, rig.Front, rig.Front, true, false, 0, 1f, from);
            for (var hour = from; hour < from + 48.0 && !round.Finished; hour += 0.25)
                clock.Tick(hour, (r, s) => rig.Inputs(r, s, (int)(from / 24.0)),
                    (r, sum) => banked += sum);
            return banked;
        }

        // ------------------------------------------------------------------ RIVAL-004

        /// <summary>
        /// (a) A house with one paying shop runs a round with nobody standing anywhere.
        /// The take reaches the safe and the meter is cleared.
        /// </summary>
        static void APaperRoundBringsTheTakeHome(List<string> failures)
        {
            var rig = new Rig();
            var house = HouseOf(0);
            var mine = Gang(house.GangId);
            rig.OweUs(Shop, mine);

            var owed = rig.Dues.OwedOf(Shop, mine);
            if (owed != WeeklyRate)
                failures.Add("ROUND-001: a week of the meter did not leave $" +
                             WeeklyRate + " owing (it left $" + owed + ").");

            var safe = house.Runner.Accounts.Safe;
            var round = rig.Ledger.Open(
                mine, 1, 11, Street, TerritoryRoundKind.Collect, rig.Stops(Shop), 9.0);
            if (round == null)
            {
                failures.Add("ROUND-001: the round would not open with a door owing.");
                return;
            }

            var banked = WalkOnPaper(rig, round, 9.0);
            if (!round.Finished || round.Stage != TerritoryRoundStage.Banked)
                failures.Add("ROUND-001: the paper round never reached the front.");
            house.Runner.BankCollection(banked);

            if (banked <= 0)
                failures.Add("ROUND-001: the round came home with nothing.");
            if (house.Runner.Accounts.Safe != safe + banked)
                failures.Add("ROUND-001: the safe did not grow by what was banked.");
            if (rig.Dues.OwedOf(Shop, mine) != 0)
                failures.Add("ROUND-001: the door still owes after it paid in full.");
        }

        /// <summary>
        /// (b) The same round for the player and for a family settles identically. There
        /// is one rule, and the house number is not one of its inputs.
        /// </summary>
        static void EveryHouseCollectsTheSameWay(List<string> failures)
        {
            var ours = Take(0);
            var theirs = Take(7);
            if (ours != theirs)
                failures.Add("ROUND-002: house 0 collected $" + ours +
                             " where house 7 collected $" + theirs +
                             " from the same door on the same day.");
        }

        static int Take(int gangId)
        {
            var rig = new Rig();
            var mine = Gang(gangId);
            rig.OweUs(Shop, mine);
            var round = rig.Ledger.Open(
                mine, gangId * 1000 + 1, 11, Street, TerritoryRoundKind.Collect,
                rig.Stops(Shop), 9.0);
            return WalkOnPaper(rig, round, 9.0);
        }

        /// <summary>
        /// (c) A round called off mid-walk files the loss and puts nothing in the safe.
        /// </summary>
        static void AnAbandonedRoundLosesWhatItCarried(List<string> failures)
        {
            var rig = new Rig();
            var house = HouseOf(3);
            var mine = Gang(house.GangId);
            rig.OweUs(Shop, mine);
            rig.OweUs(Bar, mine);

            var safe = house.Runner.Accounts.Safe;
            var round = rig.Ledger.Open(
                mine, 3001, 11, Street, TerritoryRoundKind.Collect,
                rig.Stops(Shop, Bar), 9.0);

            // One door settled, then the crew is called off with the bag.
            rig.Ledger.Arrive(round, 9.1);
            rig.Ledger.Settle(round, rig.Inputs(round, round.Stop, 0), 9.1);
            rig.Ledger.Advance(round, 9.2);
            var carried = round.Carried;
            if (carried <= 0)
            {
                failures.Add("ROUND-003: the fixture's first door paid nothing.");
                return;
            }

            var before = rig.Racket.Dispatches.Count;
            rig.Ledger.Abandon(round, 9.3);

            if (house.Runner.Accounts.Safe != safe)
                failures.Add("ROUND-003: a lost bag moved the safe.");
            if (round.Stage != TerritoryRoundStage.Lost)
                failures.Add("ROUND-003: the abandoned round did not end lost.");
            var filed = false;
            for (var i = before; i < rig.Racket.Dispatches.Count; i++)
                if (rig.Racket.Dispatches[i].News == TerritoryDoorNews.RoundLost)
                    filed = true;
            if (!filed)
                failures.Add("ROUND-003: a bag worth $" + carried +
                             " went missing and nothing was filed.");
        }

        /// <summary>
        /// (d) Two misses running and the arrangement lapses - the rule the racket has
        /// always had, now enforced from inside the round.
        /// </summary>
        static void TwoMissesLetTheArrangementLapse(List<string> failures)
        {
            var rig = new Rig();
            var mine = Gang(2);
            rig.OweUs(Shop, mine);

            var round = rig.Ledger.Open(
                mine, 2001, 11, Street, TerritoryRoundKind.Collect, rig.Stops(Shop), 9.0);

            // A door that is asked and cannot pay, twice running. The owner's roll is
            // not what is under test; a settlement of nothing against a real debt is.
            var missed = 0;
            for (var day = 0; day < 2; day++)
            {
                var settlement = rig.Ledger.Settle(
                    round,
                    new TerritoryStopInputs(
                        true, WeeklyRate, Broke(), 0f, 90f,
                        (int)Personnel.CrewPolicy.Lenient,
                        (int)Personnel.LieutenantArchetype.Soldier, 1987, day),
                    9.0 + day * 24.0);
                if (settlement.Missed)
                    missed++;
                if (settlement.Lapsed && missed < 2)
                    failures.Add("ROUND-004: the arrangement lapsed after one miss.");
            }

            if (missed != 2)
            {
                failures.Add("ROUND-004: the fixture did not produce two misses (it " +
                             "produced " + missed + ").");
                return;
            }
            if (rig.Racket.StateOf(Shop, mine) == TerritoryProtectionState.Compliant)
                failures.Add("ROUND-004: two misses running left the door paying.");
            if (rig.Dues.OwedOf(Shop, mine) != 0)
                failures.Add("ROUND-004: the meter kept running after the lapse.");
        }

        /// <summary>An owner with nothing behind the counter and the nerve to say so -
        /// the fixture for a miss.</summary>
        static TerritoryOwnerProfile Broke() =>
            new TerritoryOwnerProfile(TerritoryOwnerTrait.Stubborn, 1f, 1f, 0f);

        /// <summary>
        /// (e) The same stops and the same rolls, driven once by the physical call
        /// sequence and once by the paper clock, come home with the same money.
        /// </summary>
        static void BothClocksAreWorthTheSameMoney(List<string> failures)
        {
            var street = new Rig();
            var mineStreet = Gang(5);
            street.OweUs(Shop, mineStreet);
            street.OweUs(Bar, mineStreet);
            var walked = street.Ledger.Open(
                mineStreet, 5001, 11, Street, TerritoryRoundKind.Collect,
                street.Stops(Shop, Bar), 9.0);

            // The street's own sequence: arrive, settle inside, step out, next door,
            // and home. Exactly what NoteRoundArrival and NextStop call.
            var day = 0;
            while (!walked.Finished)
            {
                if (walked.Stage == TerritoryRoundStage.HeadingHome)
                {
                    street.Ledger.Bank(walked, 12.0);
                    break;
                }
                street.Ledger.Arrive(walked, 9.5);
                street.Ledger.Settle(walked, street.Inputs(walked, walked.Stop, day), 9.5);
                street.Ledger.Advance(walked, 9.6);
            }

            var paper = new Rig();
            var minePaper = Gang(5);
            paper.OweUs(Shop, minePaper);
            paper.OweUs(Bar, minePaper);
            var flown = paper.Ledger.Open(
                minePaper, 5001, 11, Street, TerritoryRoundKind.Collect,
                paper.Stops(Shop, Bar), 9.0);
            var onPaper = WalkOnPaper(paper, flown, 9.0);

            if (walked.Carried != flown.Carried || onPaper != walked.Carried)
                failures.Add("ROUND-005: the street banked $" + walked.Carried +
                             " and paper banked $" + onPaper +
                             " from the same doors on the same day.");
            if (walked.Missed != flown.Missed)
                failures.Add("ROUND-005: the two clocks disagree about who missed.");
        }

        /// <summary>A shakedown ends where it was walked - there is no bag and no front
        /// to walk it to.</summary>
        static void AShakedownCarriesNothingHome(List<string> failures)
        {
            var rig = new Rig();
            var mine = Gang(4);
            var round = rig.Ledger.Open(
                mine, 4001, 11, Street, TerritoryRoundKind.ShakeDown, rig.Stops(Shop),
                9.0);

            rig.Ledger.Arrive(round, 9.1);
            if (rig.Ledger.Advance(round, 9.2))
                failures.Add("ROUND-006: a one-door shakedown had a second door.");
            if (round.Stage != TerritoryRoundStage.Banked)
                failures.Add("ROUND-006: the shakedown did not end where it was walked.");
            if (round.Carried != 0)
                failures.Add("ROUND-006: a shakedown came home carrying money.");
        }

    }
}
