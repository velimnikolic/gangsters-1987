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
            PaydayFallsEverySeventhDay(failures);
            AScriptedMonthIsRepeatable(failures);

            return failures;
        }

        // ------------------------------------------------------- the campaign running

        /// <summary>A runner over the day-one roster, with the job door a fixed
        /// distance away so travel is a known quantity rather than a scene's.</summary>
        static CampaignRunner Runner(out Roster roster, float metres = 800f)
        {
            roster = RosterSeeder.Generate(42);
            RosterOps.NormalizeArms(roster);
            var runner = new CampaignRunner { Seed = 42, DistanceOf = _ => metres };
            runner.OpenFirstSheet();
            return runner;
        }

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

            // And the men who did it learned the trade the job was made of.
            var lieutenant = roster.Find(roster.Crews[0].LieutenantId);
            if (lieutenant.GetPractice(donate.PrimaryAttribute) <= 0)
                failures.Add("AJobRunsItsCourse: nobody learned anything.");
        }

        static void AStandingWatchPaysDaily(List<string> failures)
        {
            var runner = Runner(out var roster);
            var job = JobFor(roster, OrderType.RunBusiness);
            runner.Issue(roster, job);
            runner.AdvanceHours(roster, 100f);

            if (job.Stage != JobStage.Working)
                failures.Add("AStandingWatchPaysDaily: they never got to work.");

            var before = runner.Accounts.Safe;
            var lieutenant = roster.Find(roster.Crews[0].LieutenantId);
            var learned = lieutenant.GetPractice(CharacterAttribute.Business);

            runner.DayTick(roster);

            if (job.DaysStood != 1 || !job.Live)
                failures.Add("AStandingWatchPaysDaily: the watch did not stand a day.");
            if (runner.Accounts.Safe <= before)
                failures.Add("AStandingWatchPaysDaily: a business that earns nothing.");
            if (runner.Accounts.Current.LegalIncome <= 0)
                failures.Add("AStandingWatchPaysDaily: the takings went on the wrong line.");
            if (lieutenant.GetPractice(CharacterAttribute.Business) <= learned)
                failures.Add("AStandingWatchPaysDaily: a day's work taught nobody.");

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

        static void PaydayFallsEverySeventhDay(List<string> failures)
        {
            var runner = Runner(out var roster);
            var payroll = Wages.WeeklyPayroll(roster);

            var paydays = 0;
            var paid = 0;
            for (var day = 0; day < Campaign.DaysPerWeek * 2; day++)
            {
                var wages = runner.DayTick(roster);
                if (wages <= 0)
                    continue;
                paydays++;
                paid += wages;
                if (runner.Campaign.DayOfWeek != 0)
                    failures.Add("PaydayFallsEverySeventhDay: paid mid-week.");
            }

            if (paydays != 2)
                failures.Add($"PaydayFallsEverySeventhDay: {paydays} paydays in a fortnight.");
            if (paid != payroll * 2)
                failures.Add("PaydayFallsEverySeventhDay: the envelope is the wrong size.");
            if (Accounts.StartingSafe - runner.Accounts.Safe != paid)
                failures.Add("PaydayFallsEverySeventhDay: the safe did not pay them.");

            // Each week gets its own sheet and the closed ones keep what was paid.
            if (runner.Accounts.Sheets.Count != 3)
                failures.Add("PaydayFallsEverySeventhDay: the books did not turn over.");
            if (!runner.Accounts.Sheets[0].Closed || runner.Accounts.Current.Closed)
                failures.Add("PaydayFallsEverySeventhDay: the wrong sheet is open.");
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
            if (OrderMath.WorkHours(guard, 1, 1) != 0f)
                failures.Add("OrderTableCoversEveryType: a standing watch owes hours.");
        }

        static void TravelIsHoursNotBudget(List<string> failures)
        {
            // 2,000m: a working morning on foot, an hour in a car.
            var foot = OrderMath.TravelHours(2_000f, hasVehicle: false, drivingHalfSteps: 6);
            var car = OrderMath.TravelHours(2_000f, hasVehicle: true, drivingHalfSteps: 6);
            if (foot != 5f)
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

            // Men divide the work; the calendar is the price of sending too few.
            var extort = OrderTable.SpecOf(OrderType.Extort);
            var alone = OrderMath.WorkHours(extort, 4, 1);
            if (OrderMath.WorkHours(extort, 4, 2) != alone / 2f)
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
            var roster = RosterSeeder.Generate(42);
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
            var loud = OrderResolution.HeatFor(kill, 1, stealthHalfSteps: 2, knivesHalfSteps: 2);
            if (loud != kill.Heat)
                failures.Add("QuietMenDrawNoHeat: a loud killing did not draw its heat.");

            // A careful crew works at half the noise.
            var careful = OrderResolution.HeatFor(OrderTable.SpecOf(OrderType.Torch), 1,
                OrderResolution.QuietHalfSteps, 2);
            if (careful != OrderTable.SpecOf(OrderType.Torch).Heat / 2)
                failures.Add("QuietMenDrawNoHeat: Stealth did not halve the noise.");

            // Knife and shadows together: nobody heard a shot, because there was none.
            if (OrderResolution.HeatFor(kill, 1, OrderResolution.QuietHalfSteps,
                    OrderResolution.QuietHalfSteps) != 0)
                failures.Add("QuietMenDrawNoHeat: the quiet kill was still heard.");
        }

        static void CrewKitReadsVehiclesAndSkill(List<string> failures)
        {
            var roster = RosterSeeder.Generate(42);
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

            var best = CrewKit.BestAt(roster, crew, CharacterAttribute.Firearms);
            var manual = 0;
            void Consider(int id)
            {
                var m = roster.Find(id);
                var v = m.GetHalfSteps(CharacterAttribute.Firearms);
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
            var relations = new GangRelations();

            if (relations.StanceWith(1) != Stance.Peace)
                failures.Add("StancesTurnOverAtMidnight: the outfit does not arrive quietly.");

            relations.SetPending(1, Stance.War);
            if (relations.StanceWith(1) != Stance.Peace)
                failures.Add("StancesTurnOverAtMidnight: war landed mid-week.");
            if (!relations.TryGetPending(1, out var pending) || pending != Stance.War)
                failures.Add("StancesTurnOverAtMidnight: the pending change vanished.");

            // "Never mind" - setting back to the current stance withdraws the change.
            relations.SetPending(1, Stance.Peace);
            if (relations.TryGetPending(1, out _))
                failures.Add("StancesTurnOverAtMidnight: a withdrawn change survived.");

            relations.SetPending(1, Stance.Truce);
            relations.ApplyPending();
            if (relations.StanceWith(1) != Stance.Truce ||
                relations.TryGetPending(1, out _))
                failures.Add("StancesTurnOverAtMidnight: the commit did not turn the stance.");
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
            accounts.Sheets.Add(new WeekSheet { Week = 1 });

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
                { "Twin Pack Pistols", 250 }, { "Shotgun", 750 },
                { "Machine Pistol", 1250 }, { "Rifle", 1750 }, { "Tommy Gun", 2000 },
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

        /// <summary>The counter's third shelf. Three machines, every one of them wheels
        /// rather than a gun, every one priced under the working car - what is bought
        /// here is a pass down a street, not a crew's transport - and every one of them
        /// named so that the body the catalogue photographs is the body that turns up at
        /// the kerb (PortraitStudio.VehicleModelFor is the single table, and CrewCars
        /// reads it too).</summary>
        static void MotorcyclesAreOnTheCounter(List<string> failures)
        {
            // Three: the outfit's black tourer, the pack's motorbike and the boxless
            // moped. The scooter is NOT among them - it was measured off the pack and
            // taken off the shelf (ArmoryCatalog.Motorcycles says why). A count, so a
            // listing cannot be added or lost by accident without this saying so.
            if (ArmoryCatalog.Motorcycles.Length != 3)
                failures.Add("MotorcyclesAreOnTheCounter: the shelf is not three deep.");

            var sedan = 0;
            foreach (var car in ArmoryCatalog.Vehicles)
                if (car.DisplayName == "Sedan")
                    sedan = car.Price;

            var seen = new List<string>();
            foreach (var item in ArmoryCatalog.Motorcycles)
            {
                if (item.Kind != EquipmentKind.Motorcycle)
                    failures.Add($"MotorcyclesAreOnTheCounter: {item.DisplayName} is not " +
                                 "a motorcycle.");
                if (item.Price <= 0 || item.Price >= sedan)
                    failures.Add($"MotorcyclesAreOnTheCounter: {item.DisplayName} at " +
                                 $"{item.Price} against the working car's {sedan}.");
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

            // A plated car the outfit cannot afford in its first week is the point; one
            // it can never afford at all is a listing nobody will ever click.
            if (dearest <= Accounts.StartingSafe / 4 || dearest >= Accounts.StartingSafe)
                failures.Add($"CarsAreOnTheCounter: the wagon at {dearest} against a " +
                             $"{Accounts.StartingSafe} safe is not a decision.");
        }

        static void NewStockEntersThePoolUnheld(List<string> failures)
        {
            var roster = RosterSeeder.Generate(42);
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
            // The day is the counter now; every coarser figure hangs off it.
            var campaign = new Campaign { Day = 1 };
            if (campaign.Year != Campaign.StartYear || campaign.WeekOfYear != 1 ||
                campaign.Week != 1 || campaign.DayOfWeek != 0)
                failures.Add("CalendarDerivesYear: day 1 misreads.");

            campaign.Day = 7;
            if (campaign.Week != 1 || campaign.DayOfWeek != 6)
                failures.Add("CalendarDerivesYear: the week turns a day early.");

            campaign.Day = 8;
            if (campaign.Week != 2 || campaign.DayOfWeek != 0)
                failures.Add("CalendarDerivesYear: the week does not turn on the eighth.");

            campaign.Day = Campaign.DaysPerYear;
            if (campaign.Year != Campaign.StartYear || campaign.WeekOfYear != 52)
                failures.Add("CalendarDerivesYear: the last day of the year misreads.");

            campaign.Day = Campaign.DaysPerYear + 1;
            if (campaign.Year != Campaign.StartYear + 1 || campaign.WeekOfYear != 1)
                failures.Add("CalendarDerivesYear: the year does not roll.");

            // Payday falls when a week opens, and never on the first morning - an
            // outfit does not pay wages before anybody has worked.
            if (Campaign.OpensWeek(1) || Campaign.OpensWeek(7) || !Campaign.OpensWeek(8))
                failures.Add("CalendarDerivesYear: payday falls on the wrong day.");

            // A day the field should never hold must still not throw a name.
            campaign.Day = 0;
            if (campaign.DayName.Length == 0)
                failures.Add("CalendarDerivesYear: day zero has no name.");
        }

        static void WagesDeriveFromTheRoster(List<string> failures)
        {
            var roster = RosterSeeder.Generate(42);
            var expected = 0;
            foreach (var member in roster.Members)
                expected += Wages.WageFor(member);

            if (Wages.WeeklyPayroll(roster) != expected || expected <= 0)
                failures.Add("WagesDeriveFromTheRoster: payroll is not the member sum.");

            // The dead come off the books; the jailed stay on them.
            roster.Members[1].Status = CharacterStatus.Dead;
            var afterDeath = Wages.WeeklyPayroll(roster);
            roster.Members[2].Status = CharacterStatus.Jailed;
            var afterJail = Wages.WeeklyPayroll(roster);

            if (afterDeath >= expected)
                failures.Add("WagesDeriveFromTheRoster: a dead man is still paid.");
            if (afterJail != afterDeath)
                failures.Add("WagesDeriveFromTheRoster: jail changed the wage bill.");
        }

        static void HiringMovesThePayroll(List<string> failures)
        {
            var roster = RosterSeeder.Generate(7);
            var before = Wages.WeeklyPayroll(roster);

            var recruit = new Character { Id = roster.NextCharacterId() };
            for (var a = 0; a < AttributeScale.Count; a++)
                recruit.SetHalfSteps((CharacterAttribute)a, 6);
            roster.Members.Add(recruit);

            if (Wages.WeeklyPayroll(roster) <= before)
                failures.Add("HiringMovesThePayroll: a recruit did not raise the bill.");
        }

        static void BalanceArithmetic(List<string> failures)
        {
            var sheet = new WeekSheet
            {
                Week = 3,
                LegalIncome = 1000,
                IllegalIncome = 2500,
                SalesIncome = 500,
                Bribes = 300,
                Purchases = 750,
                OtherCosts = 50,
            };

            var report = FinanceReport.For(sheet, liveWages: 900, safe: 4200,
                riskyMoney: 0, assets: 1800);

            if (report.TotalIncome != 4000)
                failures.Add($"BalanceArithmetic: income {report.TotalIncome}.");
            if (report.TotalOutgoings != 2000)
                failures.Add($"BalanceArithmetic: outgoings {report.TotalOutgoings}.");
            if (report.Profit != 2000)
                failures.Add($"BalanceArithmetic: profit {report.Profit}.");
            if (report.TaxDue != 600)
                failures.Add($"BalanceArithmetic: tax due {report.TaxDue}.");
            if (report.TotalProfit != 2000)
                failures.Add($"BalanceArithmetic: total profit {report.TotalProfit} " +
                             "(no tax paid yet).");
            if (report.TotalWealth != 6000)
                failures.Add($"BalanceArithmetic: wealth {report.TotalWealth}.");
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
            var roster = RosterSeeder.Generate(42);
            // Seeded stock: the $1,500 car - the men's .38s are their own, not stock.
            if (BalanceMath.AssetsOf(roster) != 1500)
                failures.Add($"AssetsAreBookValue: {BalanceMath.AssetsOf(roster)}.");
        }

        static void ReportUsesFrozenWagesWhenClosed(List<string> failures)
        {
            var open = new WeekSheet { Week = 1 };
            var closed = new WeekSheet { Week = 1, Closed = true, WagesPaid = 640 };

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
