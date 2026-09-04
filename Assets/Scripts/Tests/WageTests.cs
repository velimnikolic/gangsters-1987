using System.Collections.Generic;
using LivingCity.Outfit;
using LivingCity.Personnel;
using LivingCity.Territory;

namespace LivingCity.Tests
{
    /// <summary>
    /// EPIC 24, the crew economy: the house rate (WAGE-001), the life of a bargain
    /// (WAGE-002), the short envelope (WAGE-003), service pay (WAGE-004) and the
    /// yardstick the whole thing is balanced on (WAGE-005).
    ///
    /// Plain static class, failures as data, no UnityEngine - the Outfit core, the
    /// roster and the territory economy are all engine-free, which is what lets the
    /// balance of the game be an assertion rather than a belief.
    /// </summary>
    public static class WageTests
    {
        static readonly (string Name, System.Action<List<string>> Check)[] Contracts =
        {
            ("OneTablePaysAndPrices", OneTablePaysAndPrices),
            ("ALieutenantOutEarnsEveryHood", ALieutenantOutEarnsEveryHood),
            ("ACornerBoyCostsWhatACornerBoyCosts", ACornerBoyCostsWhatACornerBoyCosts),
            ("TheAdPricesOffTheHouseRate", TheAdPricesOffTheHouseRate),
            ("ARankChangeIsANewBargain", ARankChangeIsANewBargain),
            ("TheShortEnvelopeIsAnEvent", TheShortEnvelopeIsAnEvent),
            ("ThreeShortNightsAndTheHoodIsGone", ThreeShortNightsAndTheHoodIsGone),
            ("FiveShortNightsAndTheLieutenantGoesOver",
                FiveShortNightsAndTheLieutenantGoesOver),
            ("OnePaydayClearsTheRun", OnePaydayClearsTheRun),
            ("ANightOffThePayrollIsNotAMissedEnvelope",
                ANightOffThePayrollIsNotAMissedEnvelope),
            ("ABusTicketBuysAFortnightNotAFreeMan",
                ABusTicketBuysAFortnightNotAFreeMan),
            ("ServicePaysAndIsCapped", ServicePaysAndIsCapped),
            ("OneBlockCarriesOneCrew", OneBlockCarriesOneCrew),
        };

        public static string[] ContractNames()
        {
            var names = new string[Contracts.Length];
            for (var i = 0; i < Contracts.Length; i++)
                names[i] = Contracts[i].Name;
            return names;
        }

        public static List<string> Run()
        {
            var failures = new List<string>();
            for (var i = 0; i < Contracts.Length; i++)
                Contracts[i].Check(failures);
            return failures;
        }

        // ------------------------------------------------------------------ WAGE-001

        /// <summary>
        /// ONE table. What the house pays a man it raised IS what he is worth, at every
        /// rank, so nobody on the house scale is ever short - and a promotion is a rise
        /// by construction rather than by a rule somebody has to remember.
        /// </summary>
        static void OneTablePaysAndPrices(List<string> failures)
        {
            var roster = new Roster();

            var hood = Man(roster, 7);
            if (Wages.WorthOf(hood) != Wages.WageFor(hood) || Wages.PayGap(hood) != 0)
                failures.Add("OneTablePaysAndPrices: a hood on the house scale reads " +
                             Wages.PayGap(hood) + " short of it.");

            var lieutenant = Man(roster, AttributeScale.MaxHalfSteps);
            lieutenant.Rank = Rank.Lieutenant;
            if (Wages.WorthOf(lieutenant) != Wages.WageFor(lieutenant) ||
                Wages.PayGap(lieutenant) != 0)
                failures.Add("OneTablePaysAndPrices: a five-star lieutenant on the " +
                             "house scale is declared underpaid, which is the bug the " +
                             "whole epic exists to kill.");

            // The floors and the ceilings the two formulas promise each other.
            var floorHood = Man(roster, AttributeScale.MinHalfSteps);
            if (Wages.WageFor(floorHood) != Wages.HoodBase)
                failures.Add("OneTablePaysAndPrices: a one-star hood does not draw the " +
                             "corner-boy rate.");

            var topHood = Man(roster, AttributeScale.MaxHalfSteps);
            var hoodCeiling = Wages.WageFor(topHood);
            if (hoodCeiling >= Wages.LieutenantBase)
                failures.Add($"OneTablePaysAndPrices: the hood ceiling {hoodCeiling} " +
                             $"reaches the lieutenant floor {Wages.LieutenantBase}, so " +
                             "a promotion can cut a man's pay.");

            // A bargain under the rate is the ONLY thing that opens a gap.
            var bargained = Man(roster, 8);
            bargained.WageAsked = Wages.HouseRate(bargained) - 20;
            if (Wages.PayGap(bargained) != 20)
                failures.Add($"OneTablePaysAndPrices: a man $20 under the rate reads " +
                             $"{Wages.PayGap(bargained)} short.");
        }

        /// <summary>Over every seed the fixture is dealt on, the man running the crew
        /// draws more than every man in it. Not asserted for one hand-built pair: the
        /// rule has to hold for men the dealer actually produces.</summary>
        static void ALieutenantOutEarnsEveryHood(List<string> failures)
        {
            for (var seed = 1; seed <= 20; seed++)
            {
                var roster = RosterSeeder.GenerateStaffed(seed);
                var lieutenant = 0;
                var bestHood = 0;
                foreach (var member in roster.Members)
                {
                    var wage = Wages.WageFor(member, roster.Day);
                    if (member.Rank == Rank.Lieutenant && wage > lieutenant)
                        lieutenant = wage;
                    else if (member.Rank == Rank.Hood && wage > bestHood)
                        bestHood = wage;
                }

                if (lieutenant <= bestHood)
                {
                    failures.Add($"ALieutenantOutEarnsEveryHood: seed {seed} pays its " +
                                 $"lieutenant {lieutenant} and one of his hoods " +
                                 $"{bestHood}.");
                    return;   // one report, not twenty
                }
            }
        }

        /// <summary>
        /// The measured complaint WAGE-001 was written from: a man off the corner drew
        /// $158 a day, which is a made man's envelope. Two hundred draws, because one
        /// recruit proves nothing about a table.
        /// </summary>
        static void ACornerBoyCostsWhatACornerBoyCosts(List<string> failures)
        {
            var roster = RosterSeeder.GenerateStaffed(7);
            var rng = new System.Random(9);
            var total = 0;
            const int draws = 200;
            for (var i = 0; i < draws; i++)
                total += Wages.WageFor(RosterSeeder.Recruit(roster, rng));

            var average = total / draws;
            if (average >= 90)
                failures.Add($"ACornerBoyCostsWhatACornerBoyCosts: a recruit averages " +
                             $"${average} a day.");

            // And the founding payroll, which is what a campaign actually opens on.
            var opening = Wages.DailyPayroll(RosterSeeder.GenerateStaffed(1));
            if (opening < 500 || opening > 700)
                failures.Add($"ACornerBoyCostsWhatACornerBoyCosts: seed 1 opens on a " +
                             $"payroll of ${opening} a day, outside the 500-700 band " +
                             "the block yardstick is balanced against.");
        }

        // ------------------------------------------------------------------ WAGE-002

        /// <summary>
        /// The classified column prices off the one table with one market premium, and
        /// the ad and the books agree to the dollar. The opening safe has to survive
        /// the hire, too - a column nobody can afford to answer is not a column.
        /// </summary>
        static void TheAdPricesOffTheHouseRate(List<string> failures)
        {
            var roster = RosterSeeder.GenerateStaffed(1);
            var column = new HireMarket();
            column.EnsureDealt(roster, 1987, 1);

            if (column.Ads.Count == 0)
            {
                failures.Add("TheAdPricesOffTheHouseRate: the column printed nothing.");
                return;
            }

            for (var i = 0; i < column.Ads.Count; i++)
            {
                var ad = column.Ads[i];
                var house = Wages.HouseRateAs(ad.Man, Rank.Lieutenant);
                var expected = house * Wages.AskPremiumPercent / 100;

                if (ad.Daily != expected)
                    failures.Add($"TheAdPricesOffTheHouseRate: ad {i} asks {ad.Daily} " +
                                 $"where the house rate plus the premium is {expected}.");
                if (ad.Down != ad.Daily * Wages.DaysDown)
                    failures.Add($"TheAdPricesOffTheHouseRate: ad {i} wants {ad.Down} " +
                                 $"down, not {Wages.DaysDown} days of {ad.Daily}.");

                // What the books pay him once he is signed IS what the paper quoted:
                // the director re-stamps the ask after the promotion that brings him
                // on, and this is the figure it re-stamps.
                var signed = ad.Man;
                var wasRank = signed.Rank;
                signed.Rank = Rank.Lieutenant;
                var onTheBooks = Wages.WageFor(signed);
                signed.Rank = wasRank;
                if (onTheBooks != ad.Daily)
                    failures.Add($"TheAdPricesOffTheHouseRate: ad {i} quotes " +
                                 $"{ad.Daily} and the books pay {onTheBooks}.");

                if (Accounts.StartingSafe - ad.Down < 20_000)
                    failures.Add($"TheAdPricesOffTheHouseRate: signing ad {i} leaves " +
                                 $"{Accounts.StartingSafe - ad.Down} in the opening " +
                                 "safe, so the outfit cannot reach its first collection.");
            }
        }

        /// <summary>
        /// A new rank is a new bargain. The measured bug: a demoted paper lieutenant
        /// went on drawing his lieutenant's ask as a hood, forever.
        /// </summary>
        static void ARankChangeIsANewBargain(List<string> failures)
        {
            var roster = RosterSeeder.GenerateStaffed(3);
            roster.Day = 10;

            var paper = Man(roster, 8);
            paper.Rank = Rank.Lieutenant;
            paper.WageAsked = Wages.AskFor(paper);
            var crew = new Crew { Id = roster.NextCrewId(), LieutenantId = paper.Id };
            roster.Crews.Add(crew);

            if (Wages.WageFor(paper) != paper.WageAsked)
                failures.Add("ARankChangeIsANewBargain: his bargain is not what he draws.");

            var demoted = RosterOps.Demote(roster, paper.Id);
            if (!demoted.Ok)
            {
                failures.Add("ARankChangeIsANewBargain: refused - " + demoted.Reason);
                return;
            }

            if (paper.WageAsked != 0)
                failures.Add("ARankChangeIsANewBargain: the old bargain survived the " +
                             "demotion.");
            if (Wages.WageFor(paper, roster.Day) !=
                Wages.HouseRateAs(paper, Rank.Hood, roster.Day))
                failures.Add("ARankChangeIsANewBargain: a demoted man does not draw a " +
                             "hood's house rate.");

            // And back up: a promotion tears up a hood's bargain the same way.
            var hood = Man(roster, 8);
            hood.WageAsked = 45;
            var promoted = RosterOps.Promote(roster, hood.Id, out _);
            if (!promoted.Ok)
            {
                failures.Add("ARankChangeIsANewBargain: promotion refused - " +
                             promoted.Reason);
                return;
            }
            if (hood.WageAsked != 0 ||
                Wages.WageFor(hood, roster.Day) !=
                Wages.HouseRateAs(hood, Rank.Lieutenant, roster.Day))
                failures.Add("ARankChangeIsANewBargain: a promoted man kept the bargain " +
                             "he struck as a hood.");
        }

        // ------------------------------------------------------------------ WAGE-003

        /// <summary>
        /// The safe cannot cover the night: the men who go without are named, the sheet
        /// carries both halves of the bill, the feed carries the night, and the safe
        /// does NOT go below zero.
        /// </summary>
        static void TheShortEnvelopeIsAnEvent(List<string> failures)
        {
            var runner = ShortNight(out var roster, out var payroll);
            var safeBefore = runner.Accounts.Safe;

            runner.DayTick(roster, false);
            var sheet = LastClosed(runner);

            if (runner.Accounts.Safe < 0)
                failures.Add($"TheShortEnvelopeIsAnEvent: the safe went to " +
                             $"{runner.Accounts.Safe} paying wages.");
            if (sheet == null)
            {
                failures.Add("TheShortEnvelopeIsAnEvent: no sheet was closed.");
                return;
            }
            if (sheet.WagesPaid + sheet.WagesShort != payroll)
                failures.Add($"TheShortEnvelopeIsAnEvent: {sheet.WagesPaid} paid and " +
                             $"{sheet.WagesShort} short do not add up to the {payroll} " +
                             "bill.");
            if (sheet.WagesPaid > safeBefore)
                failures.Add("TheShortEnvelopeIsAnEvent: more went out than was in the " +
                             "safe.");
            if (sheet.WagesShort <= 0)
                failures.Add("TheShortEnvelopeIsAnEvent: a safe that cannot cover the " +
                             "payroll paid it in full anyway.");

            // The lieutenant is paid first; a hood is what goes without.
            var lieutenantUnpaid = false;
            var hoodUnpaid = false;
            foreach (var member in roster.Members)
            {
                if (member.UnpaidSince <= 0)
                    continue;
                if (member.Rank == Rank.Lieutenant) lieutenantUnpaid = true;
                if (member.Rank == Rank.Hood) hoodUnpaid = true;
            }
            if (lieutenantUnpaid)
                failures.Add("TheShortEnvelopeIsAnEvent: the lieutenant went unpaid " +
                             "before his men did.");
            if (!hoodUnpaid)
                failures.Add("TheShortEnvelopeIsAnEvent: nobody is marked unpaid.");

            var lines = 0;
            for (var i = 0; i < runner.Incidents.Count; i++)
                if (runner.Incidents[i].Kind == IncidentKind.PayrollShort)
                    lines++;
            if (lines != 1)
                failures.Add($"TheShortEnvelopeIsAnEvent: the feed carries {lines} " +
                             "lines about one short night, not one.");
        }

        /// <summary>Three nights with nothing in it and a hood stops turning up.</summary>
        static void ThreeShortNightsAndTheHoodIsGone(List<string> failures)
        {
            var runner = ShortNight(out var roster, out var payroll);

            // The street only re-reads the roster when the personnel version moves, and
            // the day tick's own RosterMoved calls all happen BEFORE the payroll. A man
            // struck off at midnight for not being paid has to raise it himself, or he
            // goes on walking the street - spawned, selectable and in the tactical
            // mapping - until some unrelated mutation happens to bump it.
            var toldTheStreet = 0;
            runner.RosterMoved = () => toldTheStreet++;

            // The same short envelope every night, not one short night followed by an
            // empty safe: the contract is about the men who KEEP going unpaid while
            // the men ahead of them keep being paid.
            var movedByPayroll = false;
            var foldedSameNight = false;
            for (var night = 0; night < Wages.DesertAfterUnpaidNights; night++)
            {
                runner.Accounts.Safe = payroll * 6 / 10;
                var before = CountDeserted(roster);
                var toldBefore = toldTheStreet;
                runner.DayTick(roster, false);
                if (CountDeserted(roster) <= before)
                    continue;

                if (toldTheStreet > toldBefore)
                    movedByPayroll = true;

                // And the day is read off the men AFTER the books close: the score
                // board is rebuilt at the end of the tick, so the night he was struck
                // off is already folded into it rather than a midnight late.
                foldedSameNight = true;
                foreach (var member in roster.Members)
                {
                    if (member.Status != CharacterStatus.Deserted)
                        continue;
                    if (runner.Notability.ScoreOf(member.Id) < Career.StruckWeight)
                        foldedSameNight = false;
                }
            }

            var deserted = 0;
            var paidStillHere = 0;
            foreach (var member in roster.Members)
            {
                if (member.Status == CharacterStatus.Deserted)
                    deserted++;
                else if (member.UnpaidSince <= 0 && member.Rank != Rank.Boss)
                    paidStillHere++;
            }

            if (deserted == 0)
                failures.Add("ThreeShortNightsAndTheHoodIsGone: nobody walked after " +
                             Wages.DesertAfterUnpaidNights + " unpaid nights.");
            if (paidStillHere == 0)
                failures.Add("ThreeShortNightsAndTheHoodIsGone: the men who WERE paid " +
                             "left too.");
            if (runner.Accounts.Safe < 0)
                failures.Add("ThreeShortNightsAndTheHoodIsGone: the safe went negative.");
            if (deserted > 0 && !movedByPayroll)
                failures.Add("ThreeShortNightsAndTheHoodIsGone: a man was struck off at " +
                             "the payroll and the street was never told, so the deserter " +
                             "goes on walking it.");
            if (deserted > 0 && !foldedSameNight)
                failures.Add("ThreeShortNightsAndTheHoodIsGone: the night he stopped " +
                             "coming was not folded into his score until the following " +
                             "midnight - the books are closing after the day is read " +
                             "off the men.");
        }

        static int CountDeserted(Roster roster)
        {
            var count = 0;
            foreach (var member in roster.Members)
                if (member.Status == CharacterStatus.Deserted)
                    count++;
            return count;
        }

        /// <summary>Five, and a lieutenant is over the breaking point - the existing
        /// defection pass takes him and his crew the next midnight, which is exactly
        /// what LOY-002 already does for a man who was merely disliked.</summary>
        static void FiveShortNightsAndTheLieutenantGoesOver(List<string> failures)
        {
            var runner = ShortNight(out var roster, out _);

            var lieutenantId = -1;
            foreach (var member in roster.Members)
                if (member.Rank == Rank.Lieutenant)
                {
                    lieutenantId = member.Id;
                    break;
                }

            // ENOUGH FOR ONE HOOD AND NEVER FOR HIM. The lieutenants are paid first,
            // so a safe holding the cheapest envelope in the house passes over him
            // every night and still hands somebody something.
            //
            // A safe at NOTHING no longer reaches the fifth night at all: three of
            // those in a row is the end of the outfit now (the user's word,
            // 2026-09-04, CommandTests.ThreeBrokeNightsCloseTheBooks), and a campaign
            // that is over pays nobody. The rule this contract is about is the one
            // that bites an outfit still trading, which is exactly this one.
            var cheapest = int.MaxValue;
            foreach (var member in roster.Members)
            {
                var wage = Wages.WageFor(member, roster.Day);
                if (wage > 0 && wage < cheapest)
                    cheapest = wage;
            }

            for (var night = 0; night <= Wages.DefectAfterUnpaidNights; night++)
            {
                runner.Accounts.Safe = cheapest;
                runner.DayTick(roster, false);
            }

            if (runner.Fallen)
                failures.Add("FiveShortNightsAndTheLieutenantGoesOver: the outfit was " +
                             "wound up for being broke while it was still paying a man.");

            var lieutenant = roster.Find(lieutenantId);
            if (lieutenant == null || lieutenant.Status != CharacterStatus.Deserted)
                failures.Add("FiveShortNightsAndTheLieutenantGoesOver: he is still on " +
                             "the books after " + Wages.DefectAfterUnpaidNights +
                             " unpaid nights.");
        }

        /// <summary>One full payday ends the run and stops the ladder it started.</summary>
        static void OnePaydayClearsTheRun(List<string> failures)
        {
            var runner = ShortNight(out var roster, out _);
            runner.DayTick(roster, false);

            var wasUnpaid = false;
            foreach (var member in roster.Members)
                if (member.UnpaidSince > 0)
                    wasUnpaid = true;
            if (!wasUnpaid)
            {
                failures.Add("OnePaydayClearsTheRun: nobody went unpaid to begin with.");
                return;
            }

            runner.Accounts.Safe = 100_000;
            runner.DayTick(roster, false);

            foreach (var member in roster.Members)
            {
                if (member.Gone || member.UnpaidSince <= 0)
                    continue;
                failures.Add("OnePaydayClearsTheRun: " + member.FullName +
                             " is still marked unpaid after a full payday.");
                break;
            }

            // And the ladder he was on only because of the empty envelope: one more
            // full day and the greed clock is clear.
            runner.DayTick(roster, false);
            foreach (var member in roster.Members)
            {
                if (member.Gone || member.UnderpaidSince <= 0 || member.WageAsked > 0)
                    continue;
                failures.Add("OnePaydayClearsTheRun: " + member.FullName +
                             " is on the house scale and still reads underpaid.");
                break;
            }
        }

        /// <summary>
        /// A night on which nothing was DUE is not a night he went unpaid. The run has
        /// to end when a man leaves the payroll, or it goes on ageing while he is off
        /// the books and the first genuinely empty envelope after he returns reads as
        /// however many weeks he was away - past the desertion threshold on the spot.
        /// </summary>
        static void ANightOffThePayrollIsNotAMissedEnvelope(List<string> failures)
        {
            var runner = ShortNight(out var roster, out _);
            runner.DayTick(roster, false);

            Character unpaid = null;
            foreach (var member in roster.Members)
                if (!member.Gone && member.Rank == Rank.Hood && member.UnpaidSince > 0)
                {
                    unpaid = member;
                    break;
                }
            if (unpaid == null)
            {
                failures.Add("ANightOffThePayrollIsNotAMissedEnvelope: nobody went " +
                             "unpaid to begin with.");
                return;
            }

            // Off the payroll, and the safe is full again so nothing else is short.
            unpaid.OutOfTown = true;
            runner.Accounts.Safe = 100_000;
            runner.DayTick(roster, false);

            if (unpaid.UnpaidSince > 0)
                failures.Add("ANightOffThePayrollIsNotAMissedEnvelope: " +
                             unpaid.FullName + " is still stamped unpaid on a night " +
                             "he was owed nothing.");
            if (unpaid.Gone)
                failures.Add("ANightOffThePayrollIsNotAMissedEnvelope: " +
                             unpaid.FullName + " was struck off while off the payroll.");
        }

        /// <summary>
        /// Sending a man out of the city is FOURTEEN DAYS, not a permanent state.
        /// Nothing ever put the flag down again: he came back on his feet, drew nothing
        /// for the rest of the campaign, and could never be sent away a second time.
        /// </summary>
        static void ABusTicketBuysAFortnightNotAFreeMan(List<string> failures)
        {
            var roster = RosterSeeder.GenerateStaffed(42);
            Character man = null;
            foreach (var member in roster.Members)
                if (!member.Gone && member.Rank == Rank.Hood &&
                    member.Specialty == Specialty.None)
                {
                    man = member;
                    break;
                }
            if (man == null)
            {
                failures.Add("ABusTicketBuysAFortnightNotAFreeMan: no hood on the books.");
                return;
            }

            var rate = Wages.WageFor(man);
            Police.WantedLevels.Mark(man, Police.WantedLevels.CopKiller, 5);
            if (!Police.WantedLevels.SendAway(man, 5))
            {
                failures.Add("ABusTicketBuysAFortnightNotAFreeMan: he could not be sent.");
                return;
            }
            if (Wages.WageFor(man) != 0)
                failures.Add("ABusTicketBuysAFortnightNotAFreeMan: a man in another " +
                             "state draws this one's payroll.");

            // The day his return falls due.
            RosterOps.Discharge(roster, man.BackOnDay);

            if (man.OutOfTown)
                failures.Add("ABusTicketBuysAFortnightNotAFreeMan: he is back on his " +
                             "feet and the books still say he is out of town.");
            if (Wages.WageFor(man) != rate)
                failures.Add("ABusTicketBuysAFortnightNotAFreeMan: he came home and " +
                             "draws " + Wages.WageFor(man) + " instead of " + rate + ".");
            if (!Police.WantedLevels.CanSendAway(man))
                failures.Add("ABusTicketBuysAFortnightNotAFreeMan: he can never be " +
                             "sent away again.");
        }

        // ------------------------------------------------------------------ WAGE-004

        /// <summary>Service pays, a little, and stops paying at the cap; and a man on a
        /// bargain draws his bargain however long he has stood there.</summary>
        static void ServicePaysAndIsCapped(List<string> failures)
        {
            var roster = new Roster();
            var fresh = Man(roster, 6);
            var old = Man(roster, 6);
            Career.Joined(fresh, 200, "the corner");
            Career.Joined(old, 1, "the corner");

            const int day = 210;
            var gained = Wages.WageFor(old, day) - Wages.WageFor(fresh, day);
            if (gained != Wages.TenurePerMonth * 6)
                failures.Add($"ServicePaysAndIsCapped: six months of service is worth " +
                             $"{gained}, not {Wages.TenurePerMonth * 6}.");

            // Against a man who signed THIS morning, so the older man's premium is
            // read at its ceiling rather than against another capped veteran.
            var today = Man(roster, 6);
            Career.Joined(today, 1_400, "the corner");
            var far = Wages.WageFor(old, 1_400) - Wages.WageFor(today, 1_400);
            if (far != Wages.TenureCap)
                failures.Add($"ServicePaysAndIsCapped: a lifetime of service is worth " +
                             $"{far}, not the {Wages.TenureCap} cap.");

            // A bargain was struck for the man he was: it draws no service premium,
            // and the gap under it widens as he serves.
            var bargained = Man(roster, 6);
            Career.Joined(bargained, 1, "the paper");
            bargained.WageAsked = 50;
            if (Wages.WageFor(bargained, day) != 50)
                failures.Add("ServicePaysAndIsCapped: a bargain grew a service premium.");
            if (Wages.PayGap(bargained, day) !=
                Wages.PayGap(bargained, 1) + Wages.TenurePerMonth * 6)
                failures.Add("ServicePaysAndIsCapped: service did not widen the gap " +
                             "under a bargain.");

            // Day one is the WAGE-001 figure exactly - no premium on the day a
            // campaign opens.
            var opening = RosterSeeder.GenerateStaffed(1);
            opening.Day = 1;
            var withDay = Wages.DailyPayroll(opening);
            opening.Day = 0;
            if (withDay != Wages.DailyPayroll(opening))
                failures.Add("ServicePaysAndIsCapped: the founding six drew a service " +
                             "premium on day one.");
        }

        // ------------------------------------------------------------------ WAGE-005

        /// <summary>
        /// THE YARDSTICK the whole territory economy is balanced against: one median
        /// block carries one crew. Built from the price table rather than a dealt city
        /// so the assertion is about the NUMBERS and not about one seed's luck - 41
        /// tier-1 doors and three tier-2 ones, which is what the seed-1987 audit
        /// measured the median block of 110 to be.
        ///
        /// A small crew holds a feared block with margin; a full crew needs the block
        /// properly frightened; and at fear 30 the block does NOT pay for a full crew -
        /// that last one is the game, not a failure, and it is asserted so nobody
        /// "fixes" it later.
        ///
        /// It reads EconomyPrices and Wages only, so retuning either side re-runs the
        /// yardstick.
        /// </summary>
        static void OneBlockCarriesOneCrew(List<string> failures)
        {
            const int days = 14;

            var twoHands = CrewCost(2) * days;
            var fullCrew = CrewCost(4) * days;

            var feared = BlockTake(70f, days);
            var pressed = BlockTake(50f, days);
            var quiet = BlockTake(30f, days);

            if (pressed <= twoHands)
                failures.Add($"OneBlockCarriesOneCrew: a lieutenant and two hoods cost " +
                             $"${twoHands} a fortnight and a median block at fear 50 " +
                             $"brings ${pressed}. A small crew must hold a block.");
            if (feared <= fullCrew)
                failures.Add($"OneBlockCarriesOneCrew: a full crew costs ${fullCrew} a " +
                             $"fortnight and a FEARED median block brings ${feared}.");
            if (quiet > fullCrew)
                failures.Add($"OneBlockCarriesOneCrew: a block at fear 30 brings " +
                             $"${quiet} against a full crew's ${fullCrew} - a block " +
                             "nobody is frightened of must not carry four men, or the " +
                             "fear layer is decoration.");

            // The negative control: the OLD table (hood 60 + 5 per half-step over all
            // eleven stats, lieutenant a flat 200) against the same block. A full crew
            // on it cost $12,180 a fortnight where a FEARED median block brings
            // $10,409 - it could not be carried by the best block in four, which is
            // the measured complaint this epic was written from and the proof the test
            // can tell the two tables apart.
            var oldFull = OldCrewCost(4) * days;
            if (feared > oldFull)
                failures.Add($"OneBlockCarriesOneCrew: the OLD wage table would have " +
                             $"passed this yardstick (${oldFull} against a feared " +
                             $"block's ${feared}), so the test cannot tell the two " +
                             "tables apart.");
        }

        /// <summary>What a lieutenant and this many hoods draw a day, off the fixture
        /// the rest of the roster suite measures.</summary>
        static int CrewCost(int hoods)
        {
            var roster = RosterSeeder.GenerateStaffed(1);
            var total = 0;
            var taken = 0;
            foreach (var member in roster.Members)
                if (member.Rank == Rank.Lieutenant)
                    total += Wages.WageFor(member, roster.Day);
            foreach (var member in roster.Members)
            {
                if (member.Rank != Rank.Hood || taken >= hoods)
                    continue;
                total += Wages.WageFor(member, roster.Day);
                taken++;
            }
            return total;
        }

        /// <summary>The same crew on the table this epic replaced: hood 60 + 5 a
        /// half-step over all eleven stats, lieutenant a flat 200.</summary>
        static int OldCrewCost(int hoods)
        {
            var roster = RosterSeeder.GenerateStaffed(1);
            var total = 0;
            var taken = 0;
            foreach (var member in roster.Members)
                if (member.Rank == Rank.Lieutenant)
                    total += 200;
            foreach (var member in roster.Members)
            {
                if (member.Rank != Rank.Hood || taken >= hoods)
                    continue;
                var above = member.TotalHalfSteps() -
                            AttributeScale.Count * AttributeScale.MinHalfSteps;
                total += 60 + 5 * (above > 0 ? above : 0);
                taken++;
            }
            return total;
        }

        /// <summary>
        /// A fortnight of daily rounds on the median block, through the door roll the
        /// racket actually uses (ECON-003) with the owners the city actually deals.
        /// One collection per door per day: the dues meter accrues a seventh of the
        /// weekly rate a day, so a daily round collects a seventh at a time.
        /// </summary>
        static int BlockTake(float fear, int days)
        {
            const int seed = 1987;
            var style = TerritoryCollectionStyle.OfPolicy(1);   // Normal
            var banked = 0;

            for (var day = 1; day <= days; day++)
            {
                for (var door = 0; door < MedianBlock.Length; door++)
                {
                    var id = new TerritoryBusinessId("yardstick-" + door);
                    var owed = MedianBlock[door] / 7;
                    if (owed <= 0)
                        continue;

                    var result = TerritoryPaymentRoll.Roll(
                        owed, TerritoryOwnerProfile.Deal(seed, id), fear, 0f,
                        style.ShortAcceptedShare, seed, day, id);
                    banked += result.Paid;
                }
            }

            return banked;
        }

        /// <summary>The median block of the seed-1987 audit, priced off the table: 41
        /// tier-1 doors and three tier-2 ones, about $5,600 a week owed at full
        /// compliance.</summary>
        static readonly int[] MedianBlock = BuildMedianBlock();

        static int[] BuildMedianBlock()
        {
            var doors = new List<int>();
            for (var i = 0; i < 41; i++)
                doors.Add(EconomyPrices.Unknown.ProtectionPerWeek);   // tier 1, $100
            doors.Add(300);
            doors.Add(400);
            doors.Add(500);
            return doors.ToArray();
        }

        // ------------------------------------------------------------------ fixtures

        static Character Man(Roster roster, int halfSteps)
        {
            var man = new Character { Id = roster.NextCharacterId() };
            for (var a = 0; a < AttributeScale.Count; a++)
                man.SetHalfSteps((CharacterAttribute)a, halfSteps);
            roster.Members.Add(man);
            return man;
        }

        /// <summary>A campaign whose safe cannot cover tonight's wages: the fixture
        /// roster, its books opened, and just over half a day's payroll in the safe.</summary>
        static CampaignRunner ShortNight(out Roster roster, out int payroll)
        {
            roster = RosterSeeder.GenerateStaffed(42);
            var runner = new CampaignRunner { Seed = 42, DistanceOf = _ => 800f };
            runner.OpenFirstSheet();
            payroll = Wages.DailyPayroll(roster);
            runner.Accounts.Safe = payroll * 6 / 10;
            return runner;
        }

        static DaySheet LastClosed(CampaignRunner runner)
        {
            var sheets = runner.Accounts.Sheets;
            for (var i = sheets.Count - 1; i >= 0; i--)
                if (sheets[i].Closed)
                    return sheets[i];
            return null;
        }
    }
}
