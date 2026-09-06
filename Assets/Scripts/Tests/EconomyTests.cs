using System.Collections.Generic;
using LivingCity.Outfit;
using LivingCity.Personnel;
using LivingCity.Territory;

namespace LivingCity.Tests
{
    /// <summary>
    /// Headless contracts for EPIC 9 (ECON-001..007): the dues meter, the owners, the
    /// payment roll, the round planner, policy and archetype tables, and the tier
    /// guard. The physical half of a round - crews walking, wipes, banking at the
    /// front - runs on DemoCrews and is exercised in Play; everything that decides
    /// MONEY is pure and asserted here.
    /// </summary>
    public static class EconomyTests
    {
        static readonly TerritoryBusinessId Shop =
            new TerritoryBusinessId("core:test:biz:corner");
        static readonly TerritoryGangId Us = new TerritoryGangId(0);

        public static List<string> Run()
        {
            var failures = new List<string>();
            DuesAccrueExactlyAWeekOverSevenDays(failures);
            SettleClearsWhatWasPaidAndCountsMisses(failures);
            ADroppedAccountStopsTheMeter(failures);
            OwnersAreDealtOnceAndDeterministically(failures);
            OwnerAndTierShiftVerdictsInTheDocumentedDirection(failures);
            PaymentRollIsDeterministic(failures);
            TerrifiedOwnerWithAFatTillNeverMisses(failures);
            AGreedyOwnerOnAQuietStreetMissesOften(failures);
            TwoMissesRunningLapseTheStanding(failures);
            TheRoundFollowsTheStreetNotTheIdList(failures);
            PolicyTableIsMonotone(failures);
            ArchetypeIsStableAndTotal(failures);
            EveryBlockHasItsOwnCollectionDay(failures);
            ARoundGoesOutOnlyWhenEveryConditionHolds(failures);
            ADeferredRoundFilesOnlyAfterConfirmation(failures);
            ADoorIsLateOnAWeeksMoneyOrAWeeksSilence(failures);
            JobAndProtectionIncomeStaySeparate(failures);
            return failures;
        }

        static void JobAndProtectionIncomeStaySeparate(List<string> failures)
        {
            var runner = new CampaignRunner();
            runner.OpenFirstSheet();
            var raid = OrderTable.SpecOf(OrderType.Raid);
            runner.BookMoney(raid, EconomyPrices.Raid, 0);
            runner.BankCollection(325);

            var sheet = runner.Accounts.Current;
            if (sheet.JobIncome != EconomyPrices.Raid || sheet.IllegalIncome != 325 ||
                sheet.DirtyIncome != EconomyPrices.Raid + 325 ||
                BalanceMath.TotalIncome(sheet) != sheet.DirtyIncome)
                failures.Add("BAG-001: Raid and Protection did not keep separate rows " +
                             "inside the same dirty total.");
        }

        // ------------------------------------------------------------- the schedule

        /// <summary>
        /// A block's collection day is its own and never moves: the same city deals the
        /// same weekday every session, or the arrangement is one nobody can plan around.
        /// And the week is used, not one favourite day - a hash that piles half the city
        /// onto Tuesday is not a schedule.
        /// </summary>
        static void EveryBlockHasItsOwnCollectionDay(List<string> failures)
        {
            var counts = new int[TerritoryCollectionSchedule.DaysInWeek];
            for (var i = 0; i < 200; i++)
            {
                var block = new TerritoryBlockId("block-" + i);
                var day = TerritoryCollectionSchedule.DayOf(block);
                if (day < 0 || day >= TerritoryCollectionSchedule.DaysInWeek)
                {
                    failures.Add("SCHEDULE: block " + i + " fell on day " + day + ".");
                    return;
                }
                if (TerritoryCollectionSchedule.DayOf(block) != day)
                {
                    failures.Add("SCHEDULE: the same block dealt two days.");
                    return;
                }
                counts[day]++;
            }

            for (var d = 0; d < counts.Length; d++)
                if (counts[d] > 100)
                    failures.Add("SCHEDULE: " + counts[d] + " of 200 blocks fell on one " +
                                 "day - the hash is not spreading them.");

            if (TerritoryCollectionSchedule.WordOfDay(3) != "Thursdays")
                failures.Add("SCHEDULE: day 3 is not Thursday.");
        }

        /// <summary>Six conditions, and a round goes out only when every one of them
        /// holds. Each on its own is enough to keep the men where they are.</summary>
        static void ARoundGoesOutOnlyWhenEveryConditionHolds(List<string> failures)
        {
            var block = new TerritoryBlockId("collection-day");
            var day = TerritoryCollectionSchedule.DayOf(block);
            const int open = TerritoryCollectionSchedule.OpeningHour;

            if (!TerritoryCollectionSchedule.ShouldSend(day, open, block, 40, true, false, false))
                failures.Add("SCHEDULE: everything held and nobody went.");

            if (TerritoryCollectionSchedule.ShouldSend((day + 1) % 7, open, block, 40, true, false, false))
                failures.Add("SCHEDULE: a round went out on the wrong day.");
            if (TerritoryCollectionSchedule.ShouldSend(day, open - 1, block, 40, true, false, false))
                failures.Add("SCHEDULE: a collector knocked before the shops opened.");
            if (TerritoryCollectionSchedule.ShouldSend(day, open, block, 0, true, false, false))
                failures.Add("SCHEDULE: a round went out for nothing owed.");
            if (TerritoryCollectionSchedule.ShouldSend(day, open, block, 40, false, false, false))
                failures.Add("SCHEDULE: a round went out with nobody on the bag.");
            if (TerritoryCollectionSchedule.ShouldSend(day, open, block, 40, true, true, false))
                failures.Add("SCHEDULE: a second round went out over the first.");
            if (TerritoryCollectionSchedule.ShouldSend(day, open, block, 40, true, false, true))
                failures.Add("SCHEDULE: the same block was collected twice in a day.");
            if (TerritoryCollectionSchedule.ShouldSend(day, open, default, 40, true, false, false))
                failures.Add("SCHEDULE: a round went out to no block at all.");
        }

        /// <summary>Accepting an intent to leave the headquarters is not the same as
        /// opening its round. The scheduler keeps asking until the street confirms an
        /// actual round, then files and suppresses it exactly once for that day.</summary>
        static void ADeferredRoundFilesOnlyAfterConfirmation(List<string> failures)
        {
            // Rival books open staffed; the player's campaign deliberately opens with
            // Don Salvatore alone and therefore cannot be this pure scheduler fixture.
            var house = Underworld.Deal(1987).Of(1);
            if (house?.Roster == null || house.Roster.Crews.Count == 0)
            {
                failures.Add("SCHEDULE fixture: the staffed house has no crew.");
                return;
            }

            var block = new TerritoryBlockId("schedule:deferred-door");
            Crew crew = null;
            for (var i = 0; i < house.Roster.Crews.Count; i++)
            {
                var candidate = house.Roster.Crews[i];
                if (CollectorChoice.Pick(house.Roster, candidate) < 0 ||
                    !RosterOps.AssignBlockResponsibility(
                        house.Roster, block, candidate.LieutenantId, true).Ok)
                    continue;
                RosterOps.TendCrewBag(house.Roster, candidate);
                if (RosterOps.CollectorOf(house.Roster, candidate.Id) < 0)
                    continue;
                crew = candidate;
                break;
            }
            if (crew == null)
            {
                failures.Add("SCHEDULE fixture: no crew could name a collector.");
                return;
            }
            var scheduler = new TerritoryRoundScheduler
            {
                Owed = (_, _) => 40,
                StopsOwing = (_, _) => 1,
            };
            var filed = 0;
            scheduler.Filed = (_, _, _, _, _) => filed++;
            var rounds = new TerritoryRoundLedger(
                new TerritoryRacketLedger(), new TerritoryDuesLedger());
            var attempts = 0;
            const int campaignDay = 8;
            var weekday = TerritoryCollectionSchedule.DayOf(block);

            scheduler.Tend(house, campaignDay, weekday,
                TerritoryCollectionSchedule.OpeningHour, rounds,
                (_, _, _) => { attempts++; return false; });
            if (attempts != 1 || filed != 0)
                failures.Add("SCHEDULE: a detail at the door was filed as a round out.");

            scheduler.Tend(house, campaignDay, weekday,
                TerritoryCollectionSchedule.OpeningHour, rounds,
                (_, _, _) => { attempts++; return false; });
            if (attempts != 2 || filed != 0)
                failures.Add("SCHEDULE: a deferred round consumed the day's retry.");

            if (!scheduler.Confirm(house, crew, block, campaignDay) || filed != 1)
                failures.Add("SCHEDULE: an opened deferred round was not filed once.");

            scheduler.Tend(house, campaignDay, weekday,
                TerritoryCollectionSchedule.OpeningHour, rounds,
                (_, _, _) => { attempts++; return true; });
            if (attempts != 2 || filed != 1 ||
                scheduler.Confirm(house, crew, block, campaignDay))
                failures.Add("SCHEDULE: a confirmed round was sent or filed twice.");
        }

        /// <summary>A door is late on a week's money OR a week's silence, and on neither
        /// otherwise. The boundaries are the whole rule.</summary>
        static void ADoorIsLateOnAWeeksMoneyOrAWeeksSilence(List<string> failures)
        {
            if (TerritoryCollectionSchedule.IsLate(39, 40, 10, 9))
                failures.Add("LATE: a door short of a week's money read as late.");
            if (!TerritoryCollectionSchedule.IsLate(40, 40, 10, 9))
                failures.Add("LATE: a full week's money owed did not read as late.");
            if (TerritoryCollectionSchedule.IsLate(10, 40, 17, 10))
                failures.Add("LATE: exactly seven days of silence read as late.");
            if (!TerritoryCollectionSchedule.IsLate(10, 40, 18, 10))
                failures.Add("LATE: eight days of silence did not read as late.");
            if (TerritoryCollectionSchedule.IsLate(10, 40, 30, -1))
                failures.Add("LATE: a door nobody has ever collected read as late.");
            if (TerritoryCollectionSchedule.DaysLate(18, 10) != 1)
                failures.Add("LATE: the days-late count is wrong at the boundary.");
        }

        // ------------------------------------------------------------------ ECON-001

        static void DuesAccrueExactlyAWeekOverSevenDays(List<string> failures)
        {
            var dues = new TerritoryDuesLedger();
            for (var day = 0; day < 7; day++)
                dues.AccrueDay(Shop, Us, 100);
            if (dues.OwedOf(Shop, Us) != 100)
                failures.Add("ECON-001: a week of days did not sum to the weekly rate " +
                             "(owed " + dues.OwedOf(Shop, Us) + " of 100).");

            // A casino's week, same arithmetic, no drift.
            var fat = new TerritoryDuesLedger();
            for (var day = 0; day < 7; day++)
                fat.AccrueDay(Shop, Us, 10_000);
            if (fat.OwedOf(Shop, Us) != 10_000)
                failures.Add("ECON-001: the tier-4 week drifted (" +
                             fat.OwedOf(Shop, Us) + " of 10000).");
        }

        static void SettleClearsWhatWasPaidAndCountsMisses(List<string> failures)
        {
            var dues = new TerritoryDuesLedger();
            for (var day = 0; day < 14; day++)
                dues.AccrueDay(Shop, Us, 700);
            if (dues.OwedOf(Shop, Us) != 1400)
                failures.Add("ECON-001: two weeks did not owe two weeks.");

            dues.Settle(Shop, Us, 14, 1000, missed: false);
            if (dues.OwedOf(Shop, Us) != 400)
                failures.Add("ECON-001: a part payment did not leave the remainder.");
            if (!dues.TryGet(Shop, out var account) || account.LastCollectedDay != 14)
                failures.Add("ECON-001: the last-collected day was not written.");

            if (dues.Settle(Shop, Us, 15, 0, missed: true) != 1 ||
                dues.Settle(Shop, Us, 16, 0, missed: true) != 2)
                failures.Add("ECON-003: misses in a row did not count.");
            if (dues.Settle(Shop, Us, 17, 400, missed: false) != 0)
                failures.Add("ECON-003: a payment did not clear the missed run.");
        }

        static void ADroppedAccountStopsTheMeter(List<string> failures)
        {
            var dues = new TerritoryDuesLedger();
            dues.AccrueDay(Shop, Us, 100);
            dues.Drop(Shop);
            if (dues.OwedOf(Shop, Us) != 0 || dues.TryGet(Shop, out _))
                failures.Add("ECON-001: a lapsed arrangement kept its meter running.");
        }

        // ------------------------------------------------------------------ ECON-002

        static void OwnersAreDealtOnceAndDeterministically(List<string> failures)
        {
            var first = TerritoryOwnerProfile.Deal(1987, Shop);
            var again = TerritoryOwnerProfile.Deal(1987, Shop);
            if (first.Trait != again.Trait || first.Nerve != again.Nerve ||
                first.Greed != again.Greed || first.Connections != again.Connections)
                failures.Add("ECON-002: the same seed dealt two different owners.");

            // The city is not one man cloned: over a street of ids the traits spread.
            var traits = new HashSet<TerritoryOwnerTrait>();
            for (var i = 0; i < 60; i++)
                traits.Add(TerritoryOwnerProfile.Deal(
                    1987, new TerritoryBusinessId("core:test:biz:" + i)).Trait);
            if (traits.Count < 4)
                failures.Add("ECON-002: sixty owners dealt fewer than four traits.");
        }

        static void OwnerAndTierShiftVerdictsInTheDocumentedDirection(List<string> failures)
        {
            var config = TerritoryRacketConfig.Default;
            // A street standing worth a score right between hesitate and accept, DERIVED
            // from the bars rather than written down: the accept bar is a tuning dial
            // (it moved from 40 to 30 when a wrecked front could not carry a demand),
            // and a fixture with the old number baked in fails for no better reason than
            // that the dial moved.
            const float presence = 6f;
            var midway = (config.HesitateAt + config.AcceptAt) * 0.5f;
            var inputs = new TerritoryComplianceInputs(
                fearOfAsker: (midway - config.PresenceWeight * presence) / config.FearWeight,
                presenceOfAsker: presence, blockTrouble: 0f,
                strongestRival: 0f, protectorStanding: 0f, alreadyProtectedByAsker: false);
            var neutral = TerritoryComplianceEvaluation.Evaluate(inputs, config);
            if (neutral.Verdict != TerritoryComplianceVerdict.Hesitate)
                failures.Add("ECON-002 fixture drifted: the neutral case is not a Hesitate.");

            var cowardly = TerritoryComplianceEvaluation.Evaluate(inputs, config, -10f, 0f);
            if (cowardly.Verdict != TerritoryComplianceVerdict.Accept)
                failures.Add("ECON-002: a cowardly owner did not fold a step earlier.");

            var proud = TerritoryComplianceEvaluation.Evaluate(inputs, config, 10f, 0f);
            if (proud.Verdict == TerritoryComplianceVerdict.Accept)
                failures.Add("ECON-002: a proud owner folded as easily as a neutral one.");

            // ECON-007: the same standing that takes a shopfront cannot take a casino.
            var tierFour = TerritoryComplianceEvaluation.Evaluate(
                inputs, config, 0f, TerritoryTierGuard.AcceptBar(4));
            if (tierFour.Verdict == TerritoryComplianceVerdict.Accept)
                failures.Add("ECON-007: a day-one standing made a casino pay.");
            if (TerritoryTierGuard.AcceptBar(1) != 0f ||
                TerritoryTierGuard.AcceptBar(4) <= TerritoryTierGuard.AcceptBar(3) ||
                TerritoryTierGuard.AcceptBar(3) <= TerritoryTierGuard.AcceptBar(2))
                failures.Add("ECON-007: the tier bars are not ordered by tier.");
        }

        // ------------------------------------------------------------------ ECON-003

        static void PaymentRollIsDeterministic(List<string> failures)
        {
            var owner = TerritoryOwnerProfile.Deal(1987, Shop);
            var one = TerritoryPaymentRoll.Roll(400, owner, 30f, 10f, 0.5f, 1987, 12, Shop);
            var two = TerritoryPaymentRoll.Roll(400, owner, 30f, 10f, 0.5f, 1987, 12, Shop);
            if (one.Outcome != two.Outcome || one.Paid != two.Paid ||
                one.Excuse != two.Excuse || one.ExcuseTruthful != two.ExcuseTruthful)
                failures.Add("ECON-003: the same door on the same day answered twice.");
        }

        static void TerrifiedOwnerWithAFatTillNeverMisses(List<string> failures)
        {
            var owner = new TerritoryOwnerProfile(TerritoryOwnerTrait.Careful, 0.5f, 0.5f, 0.5f);
            for (var day = 0; day < 40; day++)
            {
                var result = TerritoryPaymentRoll.Roll(
                    2000, owner, 80f, 0f, 0.5f, 1987, day, Shop);
                if (result.Outcome != TerritoryPaymentOutcome.Paid || result.Paid != 2000)
                {
                    failures.Add("ECON-003: a terrified owner with a fat till missed on day " +
                                 day + ".");
                    return;
                }
            }
        }

        static void AGreedyOwnerOnAQuietStreetMissesOften(List<string> failures)
        {
            var owner = new TerritoryOwnerProfile(TerritoryOwnerTrait.Greedy, 0.8f, 0.9f, 0.5f);
            var missed = 0;
            for (var day = 0; day < 50; day++)
                if (TerritoryPaymentRoll.Roll(400, owner, 5f, 0f, 0.5f, 1987, day, Shop)
                        .Outcome == TerritoryPaymentOutcome.Missed)
                    missed++;
            if (missed < 10)
                failures.Add("ECON-003: a greedy owner on a street that fears nobody " +
                             "missed only " + missed + " of 50.");
        }

        static void TwoMissesRunningLapseTheStanding(List<string> failures)
        {
            var racket = new TerritoryRacketLedger();
            var inputs = new TerritoryComplianceInputs(90f, 40f, 0f, 0f, 0f, false);
            racket.Demand(Shop, Us, inputs, 10.0, out _);
            if (racket.StateOf(Shop, Us) != TerritoryProtectionState.Compliant)
            {
                failures.Add("ECON-003 fixture drifted: the demand did not take.");
                return;
            }

            if (!racket.Lapse(Shop, Us, 40.0) ||
                racket.StateOf(Shop, Us) != TerritoryProtectionState.Hesitant)
                failures.Add("ECON-003: the lapse did not slide the shop back to Hesitant.");
            if (racket.Lapse(Shop, Us, 41.0))
                failures.Add("ECON-003: a shop that pays nobody lapsed a second time.");
        }

        // ------------------------------------------------------------------ ECON-004

        static void TheRoundFollowsTheStreetNotTheIdList(List<string> failures)
        {
            // Ids in one order, doors in another: the walk must follow the doors.
            var stops = new List<TerritoryRoundStopSeed>
            {
                new TerritoryRoundStopSeed("a-far", 100f, 0f),
                new TerritoryRoundStopSeed("b-near", 10f, 0f),
                new TerritoryRoundStopSeed("c-mid", 50f, 0f),
            };
            var order = new List<int>();
            TerritoryRoundPlanner.Order(stops, 0f, 0f, order);
            if (order.Count != 3 || order[0] != 1 || order[1] != 2 || order[2] != 0)
                failures.Add("ECON-004: the round walked the id list, not the street.");

            var again = new List<int>();
            TerritoryRoundPlanner.Order(stops, 0f, 0f, again);
            if (again.Count != order.Count || again[0] != order[0] ||
                again[1] != order[1] || again[2] != order[2])
                failures.Add("ECON-004: the same doors walked two different orders.");
        }

        // ------------------------------------------------------------------ ECON-005

        static void PolicyTableIsMonotone(List<string> failures)
        {
            for (var level = 1; level < 4; level++)
            {
                var softer = TerritoryCollectionStyle.OfPolicy(level - 1);
                var harder = TerritoryCollectionStyle.OfPolicy(level);
                if (harder.ShortAcceptedShare < softer.ShortAcceptedShare ||
                    harder.FearLeft < softer.FearLeft ||
                    harder.HeatLeft < softer.HeatLeft)
                {
                    failures.Add("ECON-005: the policy ladder is not monotone at level " +
                                 level + ".");
                    return;
                }
            }

            // Brutal collects more today and burns the street tomorrow: the top rung
            // must beat Lenient on the share and pay for it in fear and heat.
            var lenient = TerritoryCollectionStyle.OfPolicy(0);
            var brutal = TerritoryCollectionStyle.OfPolicy(3);
            if (brutal.ShortAcceptedShare <= lenient.ShortAcceptedShare ||
                brutal.FearLeft <= lenient.FearLeft || brutal.HeatLeft <= lenient.HeatLeft)
                failures.Add("ECON-005: Brutal is not a trade-off, it is a free lunch.");
        }

        static void ArchetypeIsStableAndTotal(List<string> failures)
        {
            if (LieutenantArchetypes.Of(8, 4, 4, 4, 7, 80, 30) !=
                LieutenantArchetype.Psychopath)
                failures.Add("ECON-005: the psychopath did not read as one.");
            if (LieutenantArchetypes.Of(8, 4, 4, 4, 7, 40, 70) !=
                LieutenantArchetype.Enforcer)
                failures.Add("ECON-005: the enforcer did not read as one.");
            if (LieutenantArchetypes.Of(4, 4, 4, 8, 5, 40, 70) !=
                LieutenantArchetype.Negotiator)
                failures.Add("ECON-005: the negotiator did not read as one.");
            if (LieutenantArchetypes.Of(4, 8, 7, 4, 4, 40, 70) !=
                LieutenantArchetype.Earner)
                failures.Add("ECON-005: the earner did not read as one.");
            if (LieutenantArchetypes.Of(3, 4, 8, 4, 4, 40, 70) !=
                LieutenantArchetype.Administrator)
                failures.Add("ECON-005: the administrator did not read as one.");
            if (LieutenantArchetypes.Of(5, 4, 4, 4, 4, 40, 70) !=
                LieutenantArchetype.Soldier)
                failures.Add("ECON-005: the plain soldier did not read as one.");

            // The same man reads the same word every time.
            if (LieutenantArchetypes.Of(8, 4, 4, 4, 7, 80, 30) !=
                LieutenantArchetypes.Of(8, 4, 4, 4, 7, 80, 30))
                failures.Add("ECON-005: the archetype read is not stable.");

            // Brutal on a Psychopath is the worst long-run choice: his scales push the
            // most fear and heat of any archetype, on top of the policy's own worst.
            TerritoryCollectionStyle.ArchetypeScales(
                (int)LieutenantArchetype.Psychopath, out _, out var psychoFear,
                out var psychoHeat);
            for (var i = 0; i < 6; i++)
            {
                if (i == (int)LieutenantArchetype.Psychopath)
                    continue;
                TerritoryCollectionStyle.ArchetypeScales(i, out _, out var fear, out var heat);
                if (fear > psychoFear || heat > psychoHeat)
                {
                    failures.Add("ECON-005: somebody burns a street worse than the psychopath.");
                    return;
                }
            }
        }
    }
}
