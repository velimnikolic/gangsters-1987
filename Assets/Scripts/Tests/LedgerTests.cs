using System.Collections.Generic;
using LivingCity.Outfit;
using LivingCity.Personnel;
using LivingCity.UI;

namespace LivingCity.Tests
{
    /// <summary>
    /// The ledger's money model: the campaign calendar, wage derivation, balance-sheet
    /// arithmetic, and the exact-cash formatter. Same discipline as
    /// <see cref="PersonnelTests"/>: plain static class, failures as data, no
    /// UnityEngine - the whole Outfit core is engine-free on purpose.
    /// </summary>
    public static class LedgerTests
    {
        public static List<string> Run()
        {
            var failures = new List<string>();

            CalendarDerivesYear(failures);
            WagesDeriveFromTheRoster(failures);
            HiringMovesThePayroll(failures);
            BalanceArithmetic(failures);
            RecoveredBagBooksAsAJob(failures);
            BagBranchShapeFollowsTheNode(failures);
            HireKeyDiesWhenTheBranchIsFull(failures);
            TaxOnlyOnProfit(failures);
            RiskThresholds(failures);
            AssetsAreBookValue(failures);
            ReportUsesFrozenWagesWhenClosed(failures);
            CashFormatsExactly(failures);
            PurchaseGateDebitsAndBooks(failures);
            CataloguePricesMatchTheSheet(failures);
            MotorcyclesAreOnTheCounter(failures);
            CarsAreOnTheCounter(failures);
            NewStockEntersThePoolUnheld(failures);
            StancesTurnOverAtMidnight(failures);
            TurfIsHeldPerBuilding(failures);
            StanceWordingIsExhaustive(failures);
            OrderTableCoversEveryType(failures);
            TravelIsHoursNotBudget(failures);
            TheBookRunsOneJobPerCrew(failures);
            OddsFollowTheStars(failures);
            MoneyMovesEitherWay(failures);
            QuietMenDrawNoHeat(failures);
            RecruitFloorsAgree(failures);
            CrewKitReadsVehiclesAndSkill(failures);
            AJobRunsItsCourse(failures);
            AStandingWatchPaysDaily(failures);
            PaydayFallsEveryDay(failures);
            AScriptedMonthIsRepeatable(failures);

            return failures;
        }

        // ------------------------------------------------------- the campaign running

        /// <summary>A runner over the day-one roster, with the job door a fixed
        /// distance away so travel is a known quantity rather than a scene's.</summary>
        static CampaignRunner Runner(out Roster roster, float metres = 800f)
        {
            roster = RosterSeeder.GenerateStaffed(42);
            RosterOps.NormalizeArms(roster);
            var runner = new CampaignRunner { Seed = 42, DistanceOf = _ => metres };
            runner.OpenFirstSheet();
            return runner;
        }

        /// <summary>What the fixture's shop takes in a day.</summary>
        const int Takings = 400;

        static Job JobFor(Roster roster, OrderType type, int men = 2)
        {
            var job = new Job
            {
                CrewId = roster.Crews[0].Id,
                Type = type,
                Men = men,
                TargetBlockId = 3,
                TargetLabel = "a door on Kirby Street",
            };
            return job;
        }

        static void AJobRunsItsCourse(List<string> failures)
        {
            var runner = Runner(out var roster);
            var job = JobFor(roster, OrderType.Donate);
            if (!runner.Issue(roster, job).Ok)
                failures.Add("AJobRunsItsCourse: the job would not issue.");

            if (job.Stage != JobStage.Queued || runner.Records.Count != 0)
                failures.Add("AJobRunsItsCourse: a job existed before anybody moved.");

            // An hour in, they are on the road and no answer has been written.
            runner.AdvanceHours(roster, 1f);
            if (job.Stage == JobStage.Queued)
                failures.Add("AJobRunsItsCourse: nobody left the front.");
            if (runner.Records.Count != 0)
                failures.Add("AJobRunsItsCourse: the record was written before the work.");

            // Enough hours for any journey and any donation.
            runner.AdvanceHours(roster, 200f);
            if (job.Live || runner.Records.Count != 1)
                failures.Add("AJobRunsItsCourse: the job never came back.");
            if (runner.Book.Jobs.Count != 0)
                failures.Add("AJobRunsItsCourse: the finished job stayed on the book.");

            var record = runner.Records[0];
            if (record.Type != OrderType.Donate || record.Men != 2 || record.Day != 1)
                failures.Add("AJobRunsItsCourse: the record misdescribes the job.");
            if (record.Outcome == OrderOutcome.CalledOff)
                failures.Add("AJobRunsItsCourse: nobody called it off.");

            // A donation costs money whichever way it went, and the safe agrees with
            // the line the sheet booked it on.
            var donate = OrderTable.SpecOf(OrderType.Donate);
            if (runner.Accounts.Safe >= Accounts.StartingSafe)
                failures.Add("AJobRunsItsCourse: the donation cost nothing.");
            if (runner.Accounts.Current.Bribes <= 0)
                failures.Add("AJobRunsItsCourse: the money went on no line.");
            if (Accounts.StartingSafe - runner.Accounts.Safe !=
                runner.Accounts.Current.Bribes)
                failures.Add("AJobRunsItsCourse: the safe and the sheet disagree.");

            // And the men who did it learned the trade the job was made of - which is
            // the ACTIVITY's trade, not the order's own attribute. The two are not the
            // same and were never meant to be: a donation is carried by the best
            // Streetwise man on the crew and it teaches him to talk (Persuasion,
            // Connections), because the improvement table knows nothing about order
            // types and is the only authority on what a day's work teaches.
            var lieutenant = roster.Find(roster.Crews[0].LieutenantId);
            var taught = ActivityXp.RowOf(OrderTable.ActivityOf(donate.Type)).Trains;
            var banked = 0;
            for (var i = 0; i < taught.Length; i++)
                banked += lieutenant.GetPractice(taught[i]);
            if (banked <= 0)
                failures.Add("AJobRunsItsCourse: nobody learned anything.");
        }

        static void AStandingWatchPaysDaily(List<string> failures)
        {
            var runner = Runner(out var roster);
            var job = JobFor(roster, OrderType.RunBusiness);
            // WHAT THE SHOP MAKES. Every surface that offers RUN THE BUSINESS puts the
            // premises' own NetPerDay on the order (DoorMenu, the almanac's order page),
            // because minding a shop is worth a share of what that shop takes - and a
            // share of nothing is nothing.
            job.TargetWorth = Takings;
            runner.Issue(roster, job);
            runner.AdvanceHours(roster, 100f);

            if (job.Stage != JobStage.Working)
                failures.Add("AStandingWatchPaysDaily: they never got to work.");

            var before = runner.Accounts.Safe;

            // What the watch teaches, and where the men who stand it still have room.
            // A man at his ceiling in every one of those trades cannot rise and his
            // points are thrown away by Practice.Convert - "he cannot pre-pay past his
            // own ceiling" - so the contract is asked of the men who still have
            // somewhere to go, and of nobody else.
            var taught = ActivityXp.RowOf(
                OrderTable.ActivityOf(OrderType.RunBusiness)).Trains;
            var onJob = new List<int>();
            CrewKit.MenOnJob(roster, roster.Crews[0], job.Men, onJob);
            var room = new List<(int Id, CharacterAttribute Trade, int Standing, int Banked)>();
            for (var i = 0; i < onJob.Count; i++)
            {
                var man = roster.Find(onJob[i]);
                for (var t = 0; t < taught.Length; t++)
                    if (Practice.NextCost(man, taught[t]) > 0)
                        room.Add((man.Id, taught[t], man.GetHalfSteps(taught[t]),
                            man.GetPractice(taught[t])));
            }
            if (room.Count == 0)
                failures.Add("AStandingWatchPaysDaily: the day-one crew has nothing " +
                             "left to learn, so the day proves nothing.");

            runner.DayTick(roster);

            if (job.DaysStood != 1 || !job.Live)
                failures.Add("AStandingWatchPaysDaily: the watch did not stand a day.");

            // The books close EVERY midnight now, so the day the watch just stood is
            // the sheet that closed - Current is already tomorrow, and empty. Reading
            // the takings off Current would have been reading the wrong day.
            if (runner.Accounts.Sheets.Count < 2)
                failures.Add("AStandingWatchPaysDaily: the day did not close a sheet.");
            else
            {
                var stood = runner.Accounts.Sheets[runner.Accounts.Sheets.Count - 2];
                if (stood.LegalIncome <= 0)
                    failures.Add("AStandingWatchPaysDaily: the takings went on the wrong line.");

                // A BONUS, NOT A SECOND RENT (D22). The premises already pays its own
                // NetPerDay into the safe at midnight; men minding it make it go a
                // quarter better and no more.
                var expected = (int)(Takings * OrderResolution.RunBusinessBonus);
                if (stood.LegalIncome != expected)
                    failures.Add("AStandingWatchPaysDaily: a day's watch over a shop " +
                                 "taking $" + Takings + " paid $" + stood.LegalIncome +
                                 ", not the $" + expected + " a quarter of it is.");
                if (!stood.Closed)
                    failures.Add("AStandingWatchPaysDaily: the day's sheet stayed open.");

                // The safe moved by exactly the day's takings less the day's wages.
                // The watch may still leave the outfit DOWN on the day - six men cost
                // more than one storefront takes - and that is the game, not a fault.
                var moved = runner.Accounts.Safe - before;
                if (moved != stood.LegalIncome - stood.WagesPaid)
                    failures.Add("AStandingWatchPaysDaily: the safe and the day disagree.");
            }

            for (var i = 0; i < room.Count; i++)
            {
                var man = roster.Find(room[i].Id);
                var rose = man.GetHalfSteps(room[i].Trade) > room[i].Standing;
                var banked = man.GetPractice(room[i].Trade) > room[i].Banked;
                if (!rose && !banked)
                    failures.Add("AStandingWatchPaysDaily: a day's work taught " +
                                 man.FullName + " no " +
                                 room[i].Trade + ", and he had room for it.");
            }

            // Days later it is still standing - a watch is never finished, only called
            // off, and calling it off is the only thing that writes its line.
            for (var day = 0; day < 5; day++)
                runner.DayTick(roster);
            if (!job.Live || runner.Records.Count != 0)
                failures.Add("AStandingWatchPaysDaily: the watch resolved itself.");

            runner.Cancel(roster, job.Id);
            if (runner.Records.Count != 1 ||
                runner.Records[0].Outcome != OrderOutcome.CalledOff)
                failures.Add("AStandingWatchPaysDaily: calling it off wrote no line.");
        }

        /// <summary>
        /// The books keep DAYS. Every midnight closes a sheet and pays the men - there
        /// is no envelope, no boundary and no seventh day that anything waits for. Run
        /// a fortnight and every one of its days must have paid.
        /// </summary>
        static void PaydayFallsEveryDay(List<string> failures)
        {
            var runner = Runner(out var roster);
            var payroll = Wages.DailyPayroll(roster);

            const int days = 14;
            var paydays = 0;
            var paid = 0;
            for (var day = 0; day < days; day++)
            {
                var wages = runner.DayTick(roster);
                if (wages <= 0)
                {
                    failures.Add("PaydayFallsEveryDay: a day went by unpaid.");
                    continue;
                }
                paydays++;
                paid += wages;
            }

            if (paydays != days)
                failures.Add($"PaydayFallsEveryDay: {paydays} paydays in {days} days.");
            if (paid != payroll * days)
                failures.Add("PaydayFallsEveryDay: the day's pay is the wrong size.");
            if (Accounts.StartingSafe - runner.Accounts.Safe != paid)
                failures.Add("PaydayFallsEveryDay: the safe did not pay them.");

            // Each day gets its own sheet: the fortnight's fourteen closed ones plus
            // today's, still open.
            if (runner.Accounts.Sheets.Count != days + 1)
                failures.Add("PaydayFallsEveryDay: the books did not turn over daily.");
            if (!runner.Accounts.Sheets[0].Closed || runner.Accounts.Current.Closed)
                failures.Add("PaydayFallsEveryDay: the wrong sheet is open.");
            // Every sheet is one day, and they run consecutively.
            for (var i = 1; i < runner.Accounts.Sheets.Count; i++)
                if (runner.Accounts.Sheets[i].Day != runner.Accounts.Sheets[i - 1].Day + 1)
                {
                    failures.Add("PaydayFallsEveryDay: the sheets skip a day.");
                    break;
                }
        }

        /// <summary>
        /// The determinism claim, end to end: a scripted month at a known seed answers
        /// the same way twice, down to the safe and every man's practice. This is the
        /// assertion that makes the whole realtime layer debuggable - a campaign that
        /// drifted could only ever be argued about.
        /// </summary>
        static void AScriptedMonthIsRepeatable(List<string> failures)
        {
            string Play()
            {
                var runner = Runner(out var roster);
                var types = new[]
                {
                    OrderType.Extort, OrderType.Bribe, OrderType.Recruit,
                    OrderType.Torch, OrderType.Audit, OrderType.CollectProtection,
                };

                for (var day = 0; day < 28; day++)
                {
                    // One job a day into the same crew's book, so the queue genuinely
                    // backs up and the Organization penalty gets exercised.
                    var job = JobFor(roster, types[day % types.Length]);
                    job.BlockTargets.Add(day);
                    runner.Issue(roster, job);

                    // The day in four steps, so arrival and work land mid-day rather
                    // than always on a tick boundary.
                    for (var quarter = 0; quarter < 4; quarter++)
                        runner.AdvanceHours(roster, 6f);
                    runner.DayTick(roster);
                }

                var state = new System.Text.StringBuilder();
                state.Append(runner.Accounts.Safe).Append('|')
                     .Append(runner.Accounts.RiskyMoney).Append('|')
                     .Append(runner.Heat).Append('|')
                     .Append(runner.Records.Count).Append('|')
                     .Append(roster.Members.Count);
                foreach (var member in roster.Members)
                {
                    state.Append('|').Append(member.FullName).Append(':')
                         .Append(member.TotalHalfSteps()).Append(':')
                         .Append((int)member.Status);
                    for (var a = 0; a < AttributeScale.Count; a++)
                        state.Append(',').Append(member.GetPractice((CharacterAttribute)a));
                }
                foreach (var record in runner.Records)
                    state.Append('|').Append(record.Day).Append(record.Type)
                         .Append(record.Outcome).Append(record.Money);
                return state.ToString();
            }

            var first = Play();
            var second = Play();
            if (first != second)
                failures.Add("AScriptedMonthIsRepeatable: the same month played twice " +
                             "came out differently.");

            // And it must actually have DONE something - a month that quietly did
            // nothing would match itself perfectly.
            if (!first.Contains("Completed") && !first.Contains("Failed"))
                failures.Add("AScriptedMonthIsRepeatable: nothing was ever resolved.");
        }

        static void OrderTableCoversEveryType(List<string> failures)
        {
            foreach (OrderType type in System.Enum.GetValues(typeof(OrderType)))
            {
                var spec = OrderTable.SpecOf(type);
                if (spec.Type != type)
                    failures.Add($"OrderTableCoversEveryType: {type} has no spec row.");
                if (LedgerText.OrderLabel(type).Length == 0)
                    failures.Add($"OrderTableCoversEveryType: {type} has no label.");
                if (spec.HoursPerTarget <= 0f)
                    failures.Add($"OrderTableCoversEveryType: {type} takes no time at all.");
            }

            // Every violent order is the street's to answer - an abstract roll deciding
            // a killing while the crew stands in the road is the bug this asserts away.
            foreach (var spec in OrderTable.Specs)
                if (spec.Category == OrderCategory.Violence &&
                    spec.Resolution != JobResolution.Street)
                    failures.Add($"OrderTableCoversEveryType: {spec.Type} is violence " +
                                 "settled off-screen.");

            // A watch is stood, never finished, so its hours must not be owed as work.
            var guard = OrderTable.SpecOf(OrderType.Guard);
            if (!Near(OrderMath.WorkHours(guard, 1, 1), 0f))
                failures.Add("OrderTableCoversEveryType: a standing watch owes hours.");
        }

        // derived hours are float arithmetic; an exact compare on them is a compare on
        // the order the multiplications happened in
        static bool Near(float a, float b) => System.Math.Abs(a - b) <= 1e-4f;

        static void TravelIsHoursNotBudget(List<string> failures)
        {
            // 2,000m: a working morning on foot, an hour in a car.
            var foot = OrderMath.TravelHours(2_000f, hasVehicle: false, drivingHalfSteps: 6);
            var car = OrderMath.TravelHours(2_000f, hasVehicle: true, drivingHalfSteps: 6);
            if (!Near(foot, 5f))
                failures.Add($"TravelIsHoursNotBudget: on foot {foot}h, expected 5.");
            if (car >= foot * 0.3f)
                failures.Add("TravelIsHoursNotBudget: the car did not shrink the city.");

            // A wheelman is worth real minutes, and only with a car under him.
            var slow = OrderMath.TravelHours(6_000f, true, AttributeScale.MinHalfSteps);
            var fast = OrderMath.TravelHours(6_000f, true, AttributeScale.MaxHalfSteps);
            if (!(fast < slow))
                failures.Add("TravelIsHoursNotBudget: Driving buys nothing on the road.");
            if (OrderMath.TravelHours(6_000f, false, AttributeScale.MaxHalfSteps) !=
                OrderMath.TravelHours(6_000f, false, AttributeScale.MinHalfSteps))
                failures.Add("TravelIsHoursNotBudget: a fast driver walked faster.");

            // Nobody is anywhere instantly, and nothing takes forever.
            if (OrderMath.TravelHours(0f, false, 6) != OrderMath.MinTravelHours)
                failures.Add("TravelIsHoursNotBudget: no floor on the journey.");
            if (OrderMath.TravelHours(5_000_000f, false, 6) != OrderMath.MaxTravelHours)
                failures.Add("TravelIsHoursNotBudget: no ceiling on the journey.");

            // THE MACHINE, and not only the man. A panel van and a sedan used to cross
            // town in the same hours with the same wheelman in them, which made the
            // counter's prices a matter of taste.
            var van = LivingCity.Gameplay.VehiclePerformance
                .For(ArmoryCatalog.BodyFor("Panel Van")).Top;
            var sedan = LivingCity.Gameplay.VehiclePerformance
                .For(ArmoryCatalog.BodyFor("Sedan")).Top;
            var byVan = OrderMath.TravelHours(6_000f, true, 6, van);
            var bySedan = OrderMath.TravelHours(6_000f, true, 6, sedan);
            if (!(byVan > bySedan))
                failures.Add($"TravelIsHoursNotBudget: the van ({byVan}h) is no slower than " +
                             $"the sedan ({bySedan}h) - the machine buys nothing on the map.");

            // and it must not buy anything to a crew that has no car at all
            if (OrderMath.TravelHours(6_000f, false, 6, 1.25f) !=
                OrderMath.TravelHours(6_000f, false, 6, 0.6f))
                failures.Add("TravelIsHoursNotBudget: a machine changed the pace of men walking.");

            // A listing nobody wrote a row for, and a nonsense scale, both drive the book
            // speed - the arithmetic must never divide by what a caller left at zero.
            if (OrderMath.TravelHours(6_000f, true, 6, 0f) !=
                OrderMath.TravelHours(6_000f, true, 6, 1f))
                failures.Add("TravelIsHoursNotBudget: a machine of nought was not read as ordinary.");

            // Men divide the work; the calendar is the price of sending too few.
            var extort = OrderTable.SpecOf(OrderType.Extort);
            var alone = OrderMath.WorkHours(extort, 4, 1);
            if (!Near(OrderMath.WorkHours(extort, 4, 2), alone / 2f))
                failures.Add("TravelIsHoursNotBudget: a second man did not halve the job.");
        }

        static void TheBookRunsOneJobPerCrew(List<string> failures)
        {
            var book = new OrderBook();
            for (var i = 0; i < 3; i++)
            {
                var job = new Job { CrewId = 7, Men = 2, Type = OrderType.Extort };
                job.Id = book.NextJobId();
                book.Jobs.Add(job);
            }

            if (book.LiveCount(7) != 3)
                failures.Add("TheBookRunsOneJobPerCrew: live jobs miscounted.");
            if (book.CurrentFor(7) != book.Jobs[0])
                failures.Add("TheBookRunsOneJobPerCrew: the crew is not on its first job.");

            // Queued men are still at the front - only a job under way holds anyone.
            if (book.MenOut(7) != 0)
                failures.Add("TheBookRunsOneJobPerCrew: a queued job took men out.");
            book.Jobs[0].Stage = JobStage.Working;
            if (book.MenOut(7) != 2)
                failures.Add("TheBookRunsOneJobPerCrew: men out miscounted.");

            if (book.DepthOf(book.Jobs[2]) != 2)
                failures.Add("TheBookRunsOneJobPerCrew: depth misread.");

            // A finished job hands the crew on and leaves the book on the next pass.
            book.Jobs[0].Stage = JobStage.Finished;
            if (book.CurrentFor(7) != book.Jobs[1])
                failures.Add("TheBookRunsOneJobPerCrew: the crew did not move on.");
            book.DropFinished();
            if (book.Jobs.Count != 2)
                failures.Add("TheBookRunsOneJobPerCrew: the finished job stayed on the book.");
        }

        static void OddsFollowTheStars(List<string> failures)
        {
            var extort = OrderTable.SpecOf(OrderType.Extort);
            var floor = OrderResolution.FloorOf(extort);

            var atFloor = OrderResolution.ChanceFor(extort, floor, 0, 10);
            if (System.Math.Abs(atFloor - OrderResolution.BaseChance) > 0.0001f)
                failures.Add($"OddsFollowTheStars: at the floor it reads {atFloor}.");

            // One full star over the floor is two half-steps: 0.35 + 0.20.
            var starOver = OrderResolution.ChanceFor(extort, floor + 2, 0, 10);
            if (System.Math.Abs(starOver - 0.55f) > 0.0001f)
                failures.Add($"OddsFollowTheStars: a star over reads {starOver}.");

            // A stated floor of zero still resolves against two stars, not against
            // nothing - a hopeless man is never as good as a capable one.
            var collect = OrderTable.SpecOf(OrderType.CollectProtection);
            if (OrderResolution.FloorOf(collect) != OrderResolution.ImplicitFloorHalfSteps)
                failures.Add("OddsFollowTheStars: a floorless order has no implicit floor.");

            // A scattered lieutenant botches the tail of his list.
            var deep = OrderResolution.ChanceFor(extort, floor + 2, depth: 12,
                organizationHalfSteps: 10);
            if (System.Math.Abs(starOver - deep - 2f * OrderResolution.DepthPenalty) > 0.0001f)
                failures.Add("OddsFollowTheStars: queue depth cost nothing.");

            // Nothing is certain and nothing is hopeless.
            if (OrderResolution.ChanceFor(extort, AttributeScale.MaxHalfSteps, 0, 10) >
                OrderResolution.MaxChance ||
                OrderResolution.ChanceFor(extort, 0, 40, 2) < OrderResolution.MinChance)
                failures.Add("OddsFollowTheStars: the clamps do not hold.");
        }

        static void MoneyMovesEitherWay(List<string> failures)
        {
            var roster = RosterSeeder.GenerateStaffed(42);
            var crew = roster.Crews[0];
            var bribe = OrderTable.SpecOf(OrderType.Bribe);

            var job = new Job { CrewId = crew.Id, Type = OrderType.Bribe, Men = 1 };
            job.TargetLabel = "a sergeant";

            // The same seed answers the same way twice - the whole determinism claim.
            var seed = OrderResolution.Mix(42, day: 3, jobId: 1);
            var first = OrderResolution.Resolve(bribe, job, roster, crew,
                new System.Random(seed));
            var again = OrderResolution.Resolve(bribe, job, roster, crew,
                new System.Random(seed));
            if (first.Outcome != again.Outcome || first.Money != again.Money)
                failures.Add("MoneyMovesEitherWay: the same seed answered twice.");

            // A bribe that bought nothing is still a bribe that was paid.
            if (first.Cost <= 0)
                failures.Add("MoneyMovesEitherWay: the attempt cost nothing.");
            if (first.Outcome == OrderOutcome.Failed && first.Payout != 0)
                failures.Add("MoneyMovesEitherWay: a failure paid out.");

            // A clever man buys the same policeman cheaper - and never for free.
            var dear = OrderResolution.CostFor(bribe, 1, OrderResolution.FloorOf(bribe));
            var cheap = OrderResolution.CostFor(bribe, 1, AttributeScale.MaxHalfSteps);
            if (!(cheap < dear) || cheap < bribe.Cost / 4)
                failures.Add($"MoneyMovesEitherWay: bribe {dear} down to {cheap}.");

            // The take scales with the man who did the leaning, both ways off the book.
            var extort = OrderTable.SpecOf(OrderType.Extort);
            var poor = OrderResolution.PayoutFor(extort, 4, AttributeScale.MinHalfSteps);
            var rich = OrderResolution.PayoutFor(extort, 4, AttributeScale.MaxHalfSteps);
            if (poor >= extort.Payout * 4 || rich <= extort.Payout * 4)
                failures.Add($"MoneyMovesEitherWay: the yield band is wrong ({poor}/{rich}).");
        }

        /// <summary>
        /// RosterSeeder names the Recruit order's floor as a private const so the
        /// Personnel core stays free of the Outfit layer. Two numbers that must agree
        /// and cannot see each other are a drift waiting to happen; this is the
        /// assertion its comment promises.
        /// </summary>
        static void RecruitFloorsAgree(List<string> failures)
        {
            var order = OrderTable.SpecOf(OrderType.Recruit);

            // A walk-in gets no bonus rolls at all, and a recruiter exactly at the
            // order's floor gets none either - the bonus is for being BETTER than the
            // job asks. One half-step over buys exactly one extra look.
            var walkIn = Deal(0);
            var atFloor = Deal(order.PrimaryFloorHalfSteps);
            if (walkIn != atFloor)
                failures.Add("RecruitFloorsAgree: the seeder's floor is not the order's.");

            var better = Deal(order.PrimaryFloorHalfSteps + 4);
            if (better <= atFloor)
                failures.Add("RecruitFloorsAgree: a sharp recruiter found no better man.");

            // And a raw recruit stays under his ceiling however the rolls fall.
            var roster = new Roster();
            var rng = new System.Random(7);
            for (var i = 0; i < 40; i++)
            {
                var member = RosterSeeder.Recruit(roster, rng);
                for (var a = 0; a < AttributeScale.Count; a++)
                    if (member.GetHalfSteps((CharacterAttribute)a) >
                        RosterSeeder.RecruitCeilingHalfSteps)
                        failures.Add("RecruitFloorsAgree: a corner boy came in over " +
                                     "the ceiling.");
            }

            // The same stream and the same recruiter must deal the same man, so the
            // comparison above is of the bonus and nothing else.
            int Deal(int recruiter) =>
                RosterSeeder.Recruit(new Roster(), new System.Random(99), recruiter)
                    .TotalHalfSteps();
        }

        static void QuietMenDrawNoHeat(List<string> failures)
        {
            var kill = OrderTable.SpecOf(OrderType.Kill);
            var loud = OrderResolution.HeatFor(kill, 1, stealthHalfSteps: 2);
            if (loud != kill.Heat)
                failures.Add("QuietMenDrawNoHeat: a loud killing did not draw its heat.");

            // A careful crew works at half the noise.
            var careful = OrderResolution.HeatFor(OrderTable.SpecOf(OrderType.Torch), 1,
                OrderResolution.QuietHalfSteps);
            if (careful != OrderTable.SpecOf(OrderType.Torch).Heat / 2)
                failures.Add("QuietMenDrawNoHeat: Stealth did not halve the noise.");

            // A man who works in the dark: nobody heard a shot, because nobody heard
            // anything at all.
            if (OrderResolution.HeatFor(kill, 1, OrderResolution.QuietHalfSteps) != 0)
                failures.Add("QuietMenDrawNoHeat: the quiet kill was still heard.");
        }

        static void CrewKitReadsVehiclesAndSkill(List<string> failures)
        {
            var roster = RosterSeeder.GenerateStaffed(42);
            var crew = roster.Crews[0];

            if (CrewKit.MenOf(crew) != 3)
                failures.Add("CrewKitReadsVehiclesAndSkill: the lieutenant does not count.");
            if (CrewKit.HasVehicle(roster, crew))
                failures.Add("CrewKitReadsVehiclesAndSkill: a car out of nowhere.");

            // Sign the seeded car out to the crew's lieutenant - gear only issues
            // to him now - and the crew rides.
            RosterEquipment car = null;
            foreach (var item in roster.Equipment)
                if (item.Kind == EquipmentKind.Vehicle)
                    car = item;
            RosterOps.GiveEquipment(roster, car.Id, crew.LieutenantId);
            if (!CrewKit.HasVehicle(roster, crew))
                failures.Add("CrewKitReadsVehiclesAndSkill: the signed-out car is invisible.");

            var best = CrewKit.BestAt(roster, crew, CharacterAttribute.Combat);
            var manual = 0;
            void Consider(int id)
            {
                var m = roster.Find(id);
                var v = m.GetHalfSteps(CharacterAttribute.Combat);
                if (v > manual)
                    manual = v;
            }
            Consider(crew.LieutenantId);
            foreach (var id in crew.HoodIds)
                Consider(id);
            if (best != manual)
                failures.Add("CrewKitReadsVehiclesAndSkill: BestAt disagrees with the sum.");
        }

        static void StancesTurnOverAtMidnight(List<string> failures)
        {
            var relations = new HouseRelations();

            if (relations.StanceBetween(0, 1) != Stance.Peace)
                failures.Add("StancesTurnOverAtMidnight: the outfit does not arrive quietly.");

            relations.SetPending(0, 1, Stance.War);
            if (relations.StanceBetween(0, 1) != Stance.Peace)
                failures.Add("StancesTurnOverAtMidnight: war landed mid-day.");
            if (!relations.TryGetPending(0, 1, out var pending) || pending != Stance.War)
                failures.Add("StancesTurnOverAtMidnight: the pending change vanished.");

            // "Never mind" - setting back to the current stance withdraws the change.
            relations.SetPending(0, 1, Stance.Peace);
            if (relations.TryGetPending(0, 1, out _))
                failures.Add("StancesTurnOverAtMidnight: a withdrawn change survived.");

            relations.SetPending(0, 1, Stance.Truce);
            relations.ApplyPending();
            if (relations.StanceBetween(0, 1) != Stance.Truce ||
                relations.TryGetPending(0, 1, out _))
                failures.Add("StancesTurnOverAtMidnight: the commit did not turn the stance.");

            // A STANCE BELONGS TO THE PAIR. Asked from either side it is one answer.
            if (relations.StanceBetween(1, 0) != Stance.Truce)
                failures.Add("StancesTurnOverAtMidnight: the two houses disagree about " +
                             "their own truce.");
        }

        static void TurfIsHeldPerBuilding(List<string> failures)
        {
            // Day one, as the markers would present it: each family holds exactly its
            // own front premise - one BUILDING, never the block around it.
            var holdings = new List<Turf.Holding>
            {
                new Turf.Holding(0, 12),   // the player's front
                new Turf.Holding(1, 30),
                new Turf.Holding(2, 4),
            };

            for (var gang = 0; gang <= 2; gang++)
                if (Turf.CountOf(holdings, gang) != 1)
                    failures.Add($"TurfIsHeldPerBuilding: gang {gang} holds " +
                                 $"{Turf.CountOf(holdings, gang)} buildings day one.");

            if (Turf.DominantIn(holdings, 12) != 0 || Turf.DominantIn(holdings, 30) != 1)
                failures.Add("TurfIsHeldPerBuilding: a front premise answers for the wrong family.");
            if (Turf.DominantIn(holdings, 7) != -1)
                failures.Add("TurfIsHeldPerBuilding: empty ground found a controller.");

            // The takeover arithmetic ahead of its mechanic: premises are counted one
            // by one, two beat one, and a shared lead is contested - no controller.
            holdings.Add(new Turf.Holding(1, 12));
            holdings.Add(new Turf.Holding(1, 12));
            if (Turf.CountIn(holdings, 12, 1) != 2 || Turf.DominantIn(holdings, 12) != 1)
                failures.Add("TurfIsHeldPerBuilding: two premises did not out-hold one.");

            holdings.Add(new Turf.Holding(0, 12));
            if (Turf.DominantIn(holdings, 12) != -1)
                failures.Add("TurfIsHeldPerBuilding: contested ground found a controller.");
        }

        static void StanceWordingIsExhaustive(List<string> failures)
        {
            foreach (Stance stance in System.Enum.GetValues(typeof(Stance)))
            {
                if (LedgerText.StanceLabel(stance).Length == 0)
                    failures.Add($"StanceWordingIsExhaustive: {stance} has no label.");
                if (LedgerText.StanceEffect(stance).Length == 0)
                    failures.Add($"StanceWordingIsExhaustive: {stance} has no effect line.");
            }
            if (LedgerText.StanceTakesEffect.Length == 0 ||
                LedgerText.StrengthUnknown.Length == 0)
                failures.Add("StanceWordingIsExhaustive: a diplomacy line is empty.");
        }

        static void PurchaseGateDebitsAndBooks(List<string> failures)
        {
            var accounts = new Accounts();
            accounts.Open(1);

            // Docs/economy-prices.md §9: the safe opens at $25,000, not a million. A
            // million bought the whole price list before a shop had been leaned on.
            if (accounts.Safe != 25_000)
                failures.Add("PurchaseGateDebitsAndBooks: the starting safe is not $25,000.");

            if (BalanceMath.TryPurchase(accounts, 750) != null)
                failures.Add("PurchaseGateDebitsAndBooks: an affordable buy refused.");
            if (accounts.Safe != Accounts.StartingSafe - 750 ||
                accounts.Current.Purchases != 750)
                failures.Add("PurchaseGateDebitsAndBooks: the safe and the sheet disagree.");

            var refusal = BalanceMath.TryPurchase(accounts, 999_999);
            if (refusal == null || refusal.Length == 0)
                failures.Add("PurchaseGateDebitsAndBooks: short money bought anyway.");
            if (accounts.Safe != Accounts.StartingSafe - 750 ||
                accounts.Current.Purchases != 750)
                failures.Add("PurchaseGateDebitsAndBooks: a refusal touched the books.");
        }

        static void CataloguePricesMatchTheSheet(List<string> failures)
        {
            var expected = new Dictionary<string, int>
            {
                // Weakest to strongest, priced in that order (the plated pieces are gone).
                // Docs/economy-prices.md §5, anchored to 1987 street prices: handguns
                // under a hundred retail, a MAC-10 about six hundred right after the '86
                // ban, and a transferable full-auto two thousand and up.
                { "Twin Pack Pistols", 150 }, { "Shotgun", 300 },
                { "Machine Pistol", 600 }, { "Rifle", 800 }, { "Tommy Gun", 2000 },
            };

            foreach (var item in ArmoryCatalog.Weapons)
            {
                // Every gun names the body it photographs - the sheet has kinds that
                // may share a body (the twin pack is two revolvers), so the name is the key.
                if (string.IsNullOrEmpty(item.ModelName))
                    failures.Add($"CataloguePricesMatchTheSheet: {item.DisplayName} " +
                                 "names no model.");

                if (!expected.TryGetValue(item.DisplayName, out var price))
                    failures.Add($"CataloguePricesMatchTheSheet: unexpected {item.DisplayName}.");
                else if (item.Price != price)
                    failures.Add($"CataloguePricesMatchTheSheet: {item.DisplayName} at " +
                                 $"{item.Price}.");
                if (item.Note.Length == 0)
                    failures.Add($"CataloguePricesMatchTheSheet: {item.DisplayName} " +
                                 "has no note.");
            }
            if (ArmoryCatalog.Weapons.Length != expected.Count)
                failures.Add("CataloguePricesMatchTheSheet: weapon count drifted.");
            if (ArmoryCatalog.Vehicles.Length == 0)
                failures.Add("CataloguePricesMatchTheSheet: no vehicles for sale.");
        }

        /// <summary>The counter's third shelf. Four machines, every one of them wheels
        /// rather than a gun, and every one of them
        /// named so that the body the catalogue photographs is the body that turns up at
        /// the kerb (PortraitStudio.VehicleModelFor is the single table, and CrewCars
        /// reads it too).</summary>
        static void MotorcyclesAreOnTheCounter(List<string> failures)
        {
            // Four: the outfit's black tourer, the pack's motorbike, the enduro and the
            // boxless moped. The scooter is NOT among them - it was measured off the pack
            // and taken off the shelf (ArmoryCatalog.Motorcycles says why). A count, so a
            // listing cannot be added or lost by accident without this saying so.
            if (ArmoryCatalog.Motorcycles.Length != 4)
                failures.Add("MotorcyclesAreOnTheCounter: the shelf is not four deep.");

            var seen = new List<string>();
            foreach (var item in ArmoryCatalog.Motorcycles)
            {
                if (item.Kind != EquipmentKind.Motorcycle)
                    failures.Add($"MotorcyclesAreOnTheCounter: {item.DisplayName} is not " +
                                 "a motorcycle.");
                // Priced by what the machine actually cost in 1987, not by a rule of
                // thumb: a new Harley tourer listed at $8,545 while a clean used sedan
                // went for four thousand, and the counter says so (economy-prices §4).
                if (item.Price <= 0)
                    failures.Add($"MotorcyclesAreOnTheCounter: {item.DisplayName} is free.");
                if (item.Note.Length == 0)
                    failures.Add($"MotorcyclesAreOnTheCounter: {item.DisplayName} has no note.");
                if (seen.Contains(item.DisplayName))
                    failures.Add($"MotorcyclesAreOnTheCounter: {item.DisplayName} twice - " +
                                 "the display name is the key every lookup uses.");
                seen.Add(item.DisplayName);

                // The body table answers by DISPLAY NAME, and its fallback is a sedan:
                // a listing the table has never heard of turns up at the kerb as a car,
                // which is the one failure that looks like nothing at all.
                var body = LivingCity.UI.PortraitStudio.VehicleModelFor(item.DisplayName);
                if (string.IsNullOrEmpty(body) || body == "SM_Veh_Sedan_01")
                    failures.Add($"MotorcyclesAreOnTheCounter: {item.DisplayName} falls " +
                                 "back to the sedan body.");
                if (LivingCity.Gameplay.VehicleCatalog.IsMarkedService(body))
                    failures.Add($"MotorcyclesAreOnTheCounter: {item.DisplayName} " +
                                 $"photographs the law's own {body}.");
            }
        }

        /// <summary>The counter's first shelf of wheels. Every listing must name a body
        /// the tables can actually answer for, and the armoured wagon - the one car on
        /// the shelf this project built rather than imported - must be dearest, because
        /// its whole point is that it is a decision against everything else money buys.</summary>
        static void CarsAreOnTheCounter(List<string> failures)
        {
            var dearest = 0;
            var dearestName = "";
            var seen = new List<string>();

            foreach (var car in ArmoryCatalog.Vehicles)
            {
                if (car.Kind != EquipmentKind.Vehicle)
                    failures.Add($"CarsAreOnTheCounter: {car.DisplayName} is not a vehicle.");
                if (car.Price <= 0)
                    failures.Add($"CarsAreOnTheCounter: {car.DisplayName} is free.");
                if (car.Note.Length == 0)
                    failures.Add($"CarsAreOnTheCounter: {car.DisplayName} has no note.");
                if (seen.Contains(car.DisplayName))
                    failures.Add($"CarsAreOnTheCounter: {car.DisplayName} twice - the " +
                                 "display name is the key every lookup uses.");
                seen.Add(car.DisplayName);

                // Same trap as the bikes: the body table's fallback IS a sedan, so a
                // listing it has never heard of fails silently as a plain car. Only the
                // sedan itself may answer with that body.
                var body = LivingCity.UI.PortraitStudio.VehicleModelFor(car.DisplayName);
                if (string.IsNullOrEmpty(body) ||
                    (body == "SM_Veh_Sedan_01" && car.DisplayName != "Sedan"))
                    failures.Add($"CarsAreOnTheCounter: {car.DisplayName} falls back to " +
                                 "the sedan body.");
                if (LivingCity.Gameplay.VehicleCatalog.IsMarkedService(body))
                    failures.Add($"CarsAreOnTheCounter: {car.DisplayName} drives the " +
                                 $"law's own {body}.");

                if (car.Price > dearest)
                {
                    dearest = car.Price;
                    dearestName = car.DisplayName;
                }
            }

            if (dearestName != "Armoured Wagon")
                failures.Add("CarsAreOnTheCounter: the armoured wagon is not the dearest " +
                             $"car on the shelf - {dearestName} at {dearest} is.");
        }

        static void NewStockEntersThePoolUnheld(List<string> failures)
        {
            var roster = RosterSeeder.GenerateStaffed(42);
            var before = roster.Equipment.Count;
            var assetsBefore = BalanceMath.AssetsOf(roster);

            var item = RosterOps.AddEquipment(roster, EquipmentKind.TommyGun,
                "Tommy Gun", 2000);

            if (roster.Equipment.Count != before + 1 ||
                item.HolderId != RosterEquipment.Unheld)
                failures.Add("NewStockEntersThePoolUnheld: the buy did not pool unheld.");
            if (BalanceMath.AssetsOf(roster) != assetsBefore + 2000)
                failures.Add("NewStockEntersThePoolUnheld: assets missed the book value.");

            // The exclusivity rules apply to bought stock like seeded stock - and a
            // weapon only lands on a lieutenant now, so the two would-be holders are
            // two crews' heads (the second promoted out of the pool for the occasion).
            var a = roster.Find(roster.Crews[0].LieutenantId);
            var pool = new List<int>();
            roster.PoolIds(pool);
            if (pool.Count == 0 || !RosterOps.Promote(roster, pool[0], out _).Ok)
            {
                failures.Add("NewStockEntersThePoolUnheld: no second lieutenant to " +
                             "test exclusivity with.");
                return;
            }
            var b = roster.Find(pool[0]);
            RosterOps.GiveEquipment(roster, item.Id, a.Id);
            if (RosterOps.GiveEquipment(roster, item.Id, b.Id).Ok)
                failures.Add("NewStockEntersThePoolUnheld: one tommy gun, two holders.");
        }

        static void CalendarDerivesYear(List<string> failures)
        {
            // The day is the ONLY counter. The weekday name still cycles, because a
            // 1987 calendar had weekdays - but nothing is owed to one.
            var campaign = new Campaign { Day = 1 };
            if (campaign.Year != Campaign.StartYear || campaign.DayOfWeek != 0)
                failures.Add("CalendarDerivesYear: day 1 misreads.");

            campaign.Day = 7;
            if (campaign.DayOfWeek != 6)
                failures.Add("CalendarDerivesYear: the weekday name runs short.");

            campaign.Day = 8;
            if (campaign.DayOfWeek != 0)
                failures.Add("CalendarDerivesYear: the weekday cycle does not wrap.");

            campaign.Day = Campaign.DaysPerYear;
            if (campaign.Year != Campaign.StartYear)
                failures.Add("CalendarDerivesYear: the last day of the year misreads.");

            campaign.Day = Campaign.DaysPerYear + 1;
            if (campaign.Year != Campaign.StartYear + 1)
                failures.Add("CalendarDerivesYear: the year does not roll.");

            // The books settle every day but the first - an outfit does not pay wages
            // before anybody has worked a day.
            if (Campaign.Settles(1) || !Campaign.Settles(2) || !Campaign.Settles(8))
                failures.Add("CalendarDerivesYear: the books settle on the wrong day.");

            // A day the field should never hold must still not throw a name.
            campaign.Day = 0;
            if (campaign.DayName.Length == 0)
                failures.Add("CalendarDerivesYear: day zero has no name.");
        }

        static void WagesDeriveFromTheRoster(List<string> failures)
        {
            var roster = RosterSeeder.GenerateStaffed(42);
            var expected = 0;
            foreach (var member in roster.Members)
                expected += Wages.WageFor(member);

            if (Wages.DailyPayroll(roster) != expected || expected <= 0)
                failures.Add("WagesDeriveFromTheRoster: payroll is not the member sum.");

            // The dead come off the books; the jailed stay on them.
            roster.Members[1].Status = CharacterStatus.Dead;
            var afterDeath = Wages.DailyPayroll(roster);
            roster.Members[2].Status = CharacterStatus.Jailed;
            var afterJail = Wages.DailyPayroll(roster);

            if (afterDeath >= expected)
                failures.Add("WagesDeriveFromTheRoster: a dead man is still paid.");
            if (afterJail != afterDeath)
                failures.Add("WagesDeriveFromTheRoster: jail changed the wage bill.");
        }

        static void HiringMovesThePayroll(List<string> failures)
        {
            var roster = RosterSeeder.GenerateStaffed(7);
            var before = Wages.DailyPayroll(roster);

            var recruit = new Character { Id = roster.NextCharacterId() };
            for (var a = 0; a < AttributeScale.Count; a++)
                recruit.SetHalfSteps((CharacterAttribute)a, 6);
            roster.Members.Add(recruit);

            if (Wages.DailyPayroll(roster) <= before)
                failures.Add("HiringMovesThePayroll: a recruit did not raise the bill.");
        }

        static void BalanceArithmetic(List<string> failures)
        {
            var sheet = new DaySheet
            {
                Day = 3,
                LegalIncome = 1000,
                IllegalIncome = 2500,
                JobIncome = 750,
                SalesIncome = 500,
                Bribes = 300,
                Purchases = 750,
                OtherCosts = 50,
            };

            var report = FinanceReport.For(sheet, liveWages: 900, safe: 4200,
                riskyMoney: 0, assets: 1800);

            if (report.TotalIncome != 4750)
                failures.Add($"BalanceArithmetic: income {report.TotalIncome}.");
            if (report.TotalOutgoings != 2000)
                failures.Add($"BalanceArithmetic: outgoings {report.TotalOutgoings}.");
            if (report.Profit != 2750)
                failures.Add($"BalanceArithmetic: profit {report.Profit}.");
            if (report.TaxDue != 825)
                failures.Add($"BalanceArithmetic: tax due {report.TaxDue}.");
            if (report.TotalProfit != 2750)
                failures.Add($"BalanceArithmetic: total profit {report.TotalProfit} " +
                             "(no tax paid yet).");
            if (report.JobIncome != 750 || sheet.DirtyIncome != 3250)
                failures.Add("BalanceArithmetic: Jobs did not stay separate from Protection.");
            if (report.TotalWealth != 6000)
                failures.Add($"BalanceArithmetic: wealth {report.TotalWealth}.");
        }

        static void RecoveredBagBooksAsAJob(List<string> failures)
        {
            var runner = Runner(out _);
            var safe = runner.Accounts.Safe;
            var risky = runner.Accounts.RiskyMoney;
            runner.BankTake(475);
            if (runner.Accounts.Safe != safe + 475 ||
                runner.Accounts.RiskyMoney != risky + 475 ||
                runner.Accounts.Current.JobIncome != 475 ||
                runner.Accounts.Current.IllegalIncome != 0)
                failures.Add("RecoveredBagBooksAsAJob: the take landed under Protection.");
        }

        static void BagBranchShapeFollowsTheNode(List<string> failures)
        {
            var crew = new Crew { Id = 27, LieutenantId = 4 };
            if (PersonnelAlmanac.HasBagBranch(crew) ||
                PersonnelAlmanac.BagBranchLeaves(crew) != 0 ||
                PersonnelAlmanac.BagBranchEmptyPlaces(crew) != 0)
                failures.Add("BAG-003: an empty crew gathered a THE BAG branch.");

            crew.BagId = 8;
            if (!PersonnelAlmanac.HasBagBranch(crew) ||
                PersonnelAlmanac.BagBranchLeaves(crew) != 0 ||
                PersonnelAlmanac.BagBranchEmptyPlaces(crew) != Crew.MaxEscorts)
                failures.Add("BAG-003: the collector was repeated as his own escort leaf.");

            crew.EscortIds.Add(9);
            if (!PersonnelAlmanac.HasBagBranch(crew) ||
                PersonnelAlmanac.BagBranchLeaves(crew) != 1 ||
                PersonnelAlmanac.BagBranchEmptyPlaces(crew) != 1)
                failures.Add("BAG-003: escort and empty place shaped the wrong branch.");
        }

        /// <summary>
        /// The HIRE A MAN key on a branch head says what the filing office would say.
        /// The bag is the case that bit: an escort joins the CREW before he takes his
        /// place beside the collector, so a crew at its manpower cap cannot take one
        /// however many escort places stand empty.
        /// </summary>
        static void HireKeyDiesWhenTheBranchIsFull(List<string> failures)
        {
            var room = new CapacityMeasure(3, 6);
            var full = new CapacityMeasure(6, 6);

            if (!PersonnelAlmanac.BranchTakesAnotherMan(false, 0, room))
                failures.Add("HIRE-001: a branch with room drew a dead key.");
            if (PersonnelAlmanac.BranchTakesAnotherMan(false, 0, full))
                failures.Add("HIRE-001: a branch at its cap offered to hire.");
            if (!PersonnelAlmanac.BranchTakesAnotherMan(true, 0, room))
                failures.Add("HIRE-001: an empty bag under a crew with room drew a dead key.");
            if (PersonnelAlmanac.BranchTakesAnotherMan(true, Crew.MaxEscorts, room))
                failures.Add("HIRE-001: a full escort offered another man.");
            if (PersonnelAlmanac.BranchTakesAnotherMan(true, 0, full))
                failures.Add("HIRE-001: an empty escort place under a crew at its cap " +
                             "offered a man the office would refuse.");
        }

        static void TaxOnlyOnProfit(List<string> failures)
        {
            if (BalanceMath.TaxDue(-500) != 0)
                failures.Add("TaxOnlyOnProfit: a losing week owed tax.");
            if (BalanceMath.TaxDue(1000) != 1000 * BalanceMath.TaxRatePercent / 100)
                failures.Add("TaxOnlyOnProfit: the rate is wrong.");
        }

        static void RiskThresholds(List<string> failures)
        {
            if (BalanceMath.RiskFor(0) != RiskRating.None ||
                BalanceMath.RiskFor(BalanceMath.RiskLowCeiling - 1) != RiskRating.Low ||
                BalanceMath.RiskFor(BalanceMath.RiskLowCeiling) != RiskRating.Moderate ||
                BalanceMath.RiskFor(BalanceMath.RiskModerateCeiling) != RiskRating.High)
                failures.Add("RiskThresholds: a boundary lands in the wrong band.");
        }

        static void AssetsAreBookValue(List<string> failures)
        {
            var roster = RosterSeeder.GenerateStaffed(42);
            // Seeded stock: the $1,500 car - the men's .38s are their own, not stock.
            if (BalanceMath.AssetsOf(roster) != 1500)
                failures.Add($"AssetsAreBookValue: {BalanceMath.AssetsOf(roster)}.");
        }

        static void ReportUsesFrozenWagesWhenClosed(List<string> failures)
        {
            var open = new DaySheet { Day = 1 };
            var closed = new DaySheet { Day = 1, Closed = true, WagesPaid = 640 };

            if (FinanceReport.For(open, 555, 0, 0, 0).Wages != 555)
                failures.Add("ReportUsesFrozenWagesWhenClosed: open sheet ignores live wages.");
            if (FinanceReport.For(closed, 555, 0, 0, 0).Wages != 640)
                failures.Add("ReportUsesFrozenWagesWhenClosed: closed sheet re-derives.");
        }

        static void CashFormatsExactly(List<string> failures)
        {
            if (LedgerText.Cash(0) != "$0" ||
                LedgerText.Cash(850) != "$850" ||
                LedgerText.Cash(1247) != "$1,247" ||
                LedgerText.Cash(-300) != "-$300" ||
                LedgerText.Cash(1250000) != "$1,250,000")
                failures.Add("CashFormatsExactly: the exact formatter rounds or misplaces.");
        }
    }
}
