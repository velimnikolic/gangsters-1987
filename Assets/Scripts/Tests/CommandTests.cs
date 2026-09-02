using System;
using System.Collections.Generic;
using LivingCity.Personnel;
using LivingCity.Territory;

namespace LivingCity.Tests
{
    /// <summary>
    /// EPIC 14's contract: how many men one man can actually hold, and what happens
    /// when the outfit grows past it. This is the engine of the whole design - growth
    /// has to force the player to promote somebody, and promoting somebody means
    /// trusting a man he may not be able to trust.
    ///
    /// Pure C#, no UnityEngine, failures returned as data.
    /// </summary>
    public static class CommandTests
    {
        static readonly (string Name, Action<List<string>> Check)[] Contracts =
        {
            ("TheCapRisesWithLeadership", TheCapRisesWithLeadership),
            ("ACrewRefusesTheManItCannotHold", ACrewRefusesTheManItCannotHold),
            ("TheLedgerReadsTheSameCapAsTheRules", TheLedgerReadsTheSameCapAsTheRules),
            ("GroundIsRefusedBeyondWhatHeCanCarry", GroundIsRefusedBeyondWhatHeCanCarry),
            ("NineBlocksNeedThreeLieutenants", NineBlocksNeedThreeLieutenants),
            ("OverCapacityIsFlaggedNeverFixed", OverCapacityIsFlaggedNeverFixed),
            ("TheSpanOfControlBindsPromotion", TheSpanOfControlBindsPromotion),
            ("TheDonsDeathEndsIt", TheDonsDeathEndsIt),
            ("DeathReachesTheRosterByOnePathOnly", DeathReachesTheRosterByOnePathOnly),
            ("ADetailStandsBetweenHimAndIt", ADetailStandsBetweenHimAndIt),
            ("HisOwnMenFallInBehindHim", HisOwnMenFallInBehindHim),
            ("ADetailOfCowardsIsNoDetailAtAll", ADetailOfCowardsIsNoDetailAtAll),
            ("GuardsCostWagesAndLearnLittle", GuardsCostWagesAndLearnLittle),
            ("TheSameMenHoldMoreUnderABetterMan", TheSameMenHoldMoreUnderABetterMan),
            ("NothingIsSetUpYet", NothingIsSetUpYet),
        };

        public static List<string> Run()
        {
            var failures = new List<string>();
            for (var i = 0; i < Contracts.Length; i++)
                Contracts[i].Check(failures);
            return failures;
        }

        public static string[] ContractNames()
        {
            var names = new string[Contracts.Length];
            for (var i = 0; i < Contracts.Length; i++)
                names[i] = Contracts[i].Name;
            return names;
        }

        static Character MakeLieutenant(Roster roster, int leadershipHalfSteps,
            out Crew crew)
        {
            var man = new Character
            {
                Id = roster.NextCharacterId(), FirstName = "Lt",
                Surname = "N" + roster.Members.Count, Rank = Rank.Lieutenant,
            };
            man.SetHalfSteps(CharacterAttribute.Leadership, leadershipHalfSteps);
            roster.Members.Add(man);
            crew = new Crew { Id = roster.NextCrewId(), LieutenantId = man.Id };
            roster.Crews.Add(crew);
            return man;
        }

        static Character MakeHood(Roster roster)
        {
            var hood = new Character
            {
                Id = roster.NextCharacterId(), FirstName = "H",
                Surname = "N" + roster.Members.Count,
            };
            roster.Members.Add(hood);
            return hood;
        }

        // -------------------------------------------------------------- the function

        static void TheCapRisesWithLeadership(List<string> failures)
        {
            var limits = OrganizationLimits.Default;
            var roster = new Roster();
            var last = -1;

            for (var half = AttributeScale.MinHalfSteps;
                 half <= AttributeScale.MaxHalfSteps; half++)
            {
                var man = MakeLieutenant(roster, half, out _);
                var cap = Command.ManCap(man, limits);

                if (cap < Command.FloorMen)
                    failures.Add($"TheCapRisesWithLeadership: Leadership " +
                                 $"{AttributeScale.ValueOf(half)} holds {cap}, under the " +
                                 $"floor of {Command.FloorMen}.");
                if (cap > limits.LieutenantManpower)
                    failures.Add($"TheCapRisesWithLeadership: Leadership " +
                                 $"{AttributeScale.ValueOf(half)} holds {cap}, over the " +
                                 $"ceiling of {limits.LieutenantManpower}.");
                if (cap < last)
                    failures.Add($"TheCapRisesWithLeadership: it went DOWN at " +
                                 $"Leadership {AttributeScale.ValueOf(half)}.");
                last = cap;
            }

            // The design's own two readings: a poor commander holds a handful, and only
            // the very best approach the ceiling.
            var poor = MakeLieutenant(roster, AttributeScale.HalfStepsFor(25), out _);
            if (Command.ManCap(poor, limits) > 10)
                failures.Add($"TheCapRisesWithLeadership: Leadership 25 holds " +
                             $"{Command.ManCap(poor, limits)} - that is a crew, not a " +
                             "handful.");

            var best = MakeLieutenant(roster, AttributeScale.MaxHalfSteps, out _);
            if (Command.ManCap(best, limits) != limits.LieutenantManpower)
                failures.Add("TheCapRisesWithLeadership: a five-star commander does not " +
                             "reach the ceiling at all.");
        }

        // ------------------------------------------------------------ the enforcement

        static void ACrewRefusesTheManItCannotHold(List<string> failures)
        {
            var roster = new Roster();
            var lieutenant = MakeLieutenant(roster, AttributeScale.MinHalfSteps,
                out var crew);
            var cap = Command.ManCap(lieutenant, roster.Organization.Limits);

            for (var i = 0; i < cap; i++)
            {
                var hood = MakeHood(roster);
                var result = RosterOps.AssignToCrew(roster, hood.Id, crew.Id);
                if (!result.Ok)
                    failures.Add($"ACrewRefusesTheManItCannotHold: man {i + 1} of {cap} " +
                                 $"was refused - \"{result.Reason}\"");
            }

            var overflow = MakeHood(roster);
            var refused = RosterOps.AssignToCrew(roster, overflow.Id, crew.Id);
            if (refused.Ok)
                failures.Add($"ACrewRefusesTheManItCannotHold: a lieutenant who can hold " +
                             $"{cap} was handed a {cap + 1}th.");
            if (!refused.Ok && (refused.Reason.Length == 0 ||
                                !refused.Reason.Contains(lieutenant.FullName)))
                failures.Add("ACrewRefusesTheManItCannotHold: the refusal does not name " +
                             "the man who cannot hold him.");

            // A dead man in the crew is not a man he is holding, so the seat frees up.
            var casualty = roster.Find(crew.HoodIds[0]);
            casualty.Status = CharacterStatus.Dead;
            if (!RosterOps.AssignToCrew(roster, overflow.Id, crew.Id).Ok)
                failures.Add("ACrewRefusesTheManItCannotHold: a dead man is still taking " +
                             "up a place in the crew.");
        }

        static void TheLedgerReadsTheSameCapAsTheRules(List<string> failures)
        {
            // One function, one answer. A page that worked its own cap out would drift
            // from the rule the moment either changed.
            var roster = RosterSeeder.GenerateLarge(1987, 60);
            var query = new OrganizationQuery(roster);

            for (var i = 0; i < roster.Members.Count; i++)
            {
                var member = roster.Members[i];
                if (member.Rank != Rank.Lieutenant && member.Rank != Rank.Boss)
                    continue;

                var view = query.CapacityOf(member.Id);
                var expected = Command.ManCap(member, roster.Organization.Limits);
                if (view.Manpower.Maximum != expected)
                    failures.Add($"TheLedgerReadsTheSameCap: the page shows " +
                                 $"{view.Manpower.Maximum} for {member.FullName} and the " +
                                 $"rule says {expected}.");
                if (view.Blocks.Maximum !=
                    Command.BlockCap(member, roster.Organization.Limits))
                    failures.Add($"TheLedgerReadsTheSameCap: the block cap disagrees for " +
                                 $"{member.FullName}.");
            }
        }

        static void GroundIsRefusedBeyondWhatHeCanCarry(List<string> failures)
        {
            var roster = new Roster();
            var boss = new Character
            {
                Id = roster.NextCharacterId(), FirstName = "Don", Rank = Rank.Boss,
            };
            roster.Members.Add(boss);
            roster.Organization.BossId = boss.Id;

            var lieutenant = MakeLieutenant(roster, AttributeScale.MaxHalfSteps, out _);
            var cap = Command.BlockCap(lieutenant, roster.Organization.Limits);

            for (var i = 0; i < cap; i++)
            {
                var result = RosterOps.AssignBlockResponsibility(
                    roster, new TerritoryBlockId("block-" + i), lieutenant.Id, true);
                if (!result.Ok)
                    failures.Add($"GroundIsRefused: block {i + 1} of {cap} was refused - " +
                                 $"\"{result.Reason}\"");
            }

            var over = RosterOps.AssignBlockResponsibility(
                roster, new TerritoryBlockId("block-over"), lieutenant.Id, true);
            if (over.Ok)
                failures.Add($"GroundIsRefused: he answers for {cap} blocks and was " +
                             "handed another.");
            if (!over.Ok && !over.Reason.Contains(lieutenant.FullName))
                failures.Add("GroundIsRefused: the refusal does not name him.");
        }

        static void NineBlocksNeedThreeLieutenants(List<string> failures)
        {
            // The spec's own arithmetic, by construction: three blocks to a lieutenant
            // means nine blocks cannot be held by two.
            var roster = new Roster();
            var boss = new Character
            {
                Id = roster.NextCharacterId(), FirstName = "Don", Rank = Rank.Boss,
            };
            roster.Members.Add(boss);
            roster.Organization.BossId = boss.Id;

            var lieutenants = new List<Character>();
            for (var i = 0; i < 2; i++)
                lieutenants.Add(MakeLieutenant(roster, AttributeScale.MaxHalfSteps, out _));

            var placed = 0;
            for (var b = 0; b < 9; b++)
            {
                var taken = false;
                for (var l = 0; l < lieutenants.Count && !taken; l++)
                    taken = RosterOps.AssignBlockResponsibility(
                        roster, new TerritoryBlockId("b" + b), lieutenants[l].Id, true).Ok;
                if (taken)
                    placed++;
            }

            var capEach = Command.BlockCap(lieutenants[0], roster.Organization.Limits);
            if (placed != capEach * 2)
                failures.Add($"NineBlocksNeedThreeLieutenants: two lieutenants took " +
                             $"{placed} blocks; {capEach} each is all there should be.");
            if (placed >= 9)
                failures.Add("NineBlocksNeedThreeLieutenants: two men held nine blocks, " +
                             "so growth never forces a promotion.");
        }

        // ------------------------------------------------------------------- the Boss

        static Character MakeBoss(Roster roster, int leadershipHalf, int authorityHalf)
        {
            var boss = new Character
            {
                Id = roster.NextCharacterId(), FirstName = "Don", Surname = "Ricci",
                Rank = Rank.Boss,
            };
            boss.SetHalfSteps(CharacterAttribute.Leadership, leadershipHalf);
            boss.SetHalfSteps(CharacterAttribute.StreetAuthority, authorityHalf);
            boss.SetHalfSteps(CharacterAttribute.Awareness, AttributeScale.MaxHalfSteps);
            boss.SetHalfSteps(CharacterAttribute.Organization, AttributeScale.MaxHalfSteps);
            roster.Members.Add(boss);
            roster.Organization.BossId = boss.Id;
            return boss;
        }

        static void TheSpanOfControlBindsPromotion(List<string> failures)
        {
            var roster = new Roster();
            // A Boss the street has never heard of: he holds one branch and has to do
            // the rest himself.
            var boss = MakeBoss(roster, AttributeScale.MinHalfSteps,
                AttributeScale.MinHalfSteps);
            if (Command.LieutenantCap(boss) != Command.FloorLieutenants)
                failures.Add($"TheSpanOfControlBindsPromotion: an unknown Boss holds " +
                             $"{Command.LieutenantCap(boss)} branches.");

            var first = MakeHood(roster);
            if (!RosterOps.Promote(roster, first.Id, out _).Ok)
                failures.Add("TheSpanOfControlBindsPromotion: he could not make even one.");

            var second = MakeHood(roster);
            var refused = RosterOps.Promote(roster, second.Id, out _);
            if (refused.Ok)
                failures.Add("TheSpanOfControlBindsPromotion: an outfit nobody has heard " +
                             "of grew a second branch.");
            if (!refused.Ok && !refused.Reason.Contains(boss.FullName))
                failures.Add("TheSpanOfControlBindsPromotion: the refusal does not name " +
                             "the man whose span it is.");

            // The span GROWS with him - which is what the command drip is for.
            boss.SetHalfSteps(CharacterAttribute.Leadership, AttributeScale.MaxHalfSteps);
            boss.SetHalfSteps(CharacterAttribute.StreetAuthority, AttributeScale.MaxHalfSteps);
            if (Command.LieutenantCap(boss) != Command.MaxLieutenants)
                failures.Add("TheSpanOfControlBindsPromotion: a Boss at five stars in " +
                             "both does not reach the ceiling.");
            if (!RosterOps.Promote(roster, second.Id, out _).Ok)
                failures.Add("TheSpanOfControlBindsPromotion: the span grew and the " +
                             "promotion was still refused.");
        }

        static void TheDonsDeathEndsIt(List<string> failures)
        {
            var roster = new Roster();
            var boss = MakeBoss(roster, 8, 8);
            var runner = new Outfit.CampaignRunner();

            var announced = 0;
            runner.BossFell += () => announced++;

            runner.DayTick(roster);
            var dayBefore = runner.Campaign.Day;
            if (runner.Fallen)
                failures.Add("TheDonsDeathEndsIt: it ended before anybody died.");

            RosterOps.Kill(roster, boss.Id);

            // The very next tick observes it, and nothing advances.
            runner.DayTick(roster);
            if (!runner.Fallen)
                failures.Add("TheDonsDeathEndsIt: the Don is dead and the campaign " +
                             "carried on.");
            if (runner.Campaign.Day != dayBefore)
                failures.Add($"TheDonsDeathEndsIt: the calendar moved to day " +
                             $"{runner.Campaign.Day} after the end.");
            if (runner.FallenOnDay != dayBefore)
                failures.Add("TheDonsDeathEndsIt: it is not recorded which day it was.");
            if (announced != 1)
                failures.Add($"TheDonsDeathEndsIt: the end was announced {announced} " +
                             "times.");

            // The other door time comes through is shut too, and stays shut.
            if (runner.AdvanceHours(roster, 8f))
                failures.Add("TheDonsDeathEndsIt: hours still passed after the end.");
            runner.DayTick(roster);
            runner.DayTick(roster);
            if (runner.Campaign.Day != dayBefore || announced != 1)
                failures.Add("TheDonsDeathEndsIt: the end kept happening.");
        }

        static void DeathReachesTheRosterByOnePathOnly(List<string> failures)
        {
            // The game-over check watches the Boss's status, and that is only safe
            // while there is ONE way for a status to become Dead. This asserts the
            // observable half: whatever route a death took, the check sees it.
            var roster = new Roster();
            var boss = MakeBoss(roster, 8, 8);
            var runner = new Outfit.CampaignRunner();

            boss.Status = CharacterStatus.Dead;
            runner.DayTick(roster);
            if (!runner.Fallen)
                failures.Add("DeathReachesTheRosterByOnePathOnly: a Boss whose status " +
                             "was set dead by some other route did not end the campaign.");

            // A dead LIEUTENANT is not the end of anything.
            var second = new Roster();
            MakeBoss(second, 8, 8);
            var lieutenant = MakeLieutenant(second, 8, out _);
            var quiet = new Outfit.CampaignRunner();
            RosterOps.Kill(second, lieutenant.Id);
            quiet.DayTick(second);
            if (quiet.Fallen)
                failures.Add("DeathReachesTheRosterByOnePathOnly: losing a lieutenant " +
                             "ended the campaign.");
        }

        // -------------------------------------------------------------- the detail

        /// <summary>A Boss with a detail of n steady men, and the outcome of one
        /// attempt on his life off a fixed stream.</summary>
        static AssassinationOutcome Attempt(int guards, int courage, int seed,
            out Roster roster, out Character boss, List<Incident> incidents = null)
        {
            roster = new Roster();
            boss = MakeBoss(roster, 8, 8);
            var detail = Bodyguards.FormDetail(roster);

            for (var i = 0; i < guards; i++)
            {
                var guard = MakeHood(roster);
                guard.SetHalfSteps(CharacterAttribute.Combat, 6);
                Personality.Set(guard, PersonalityTrait.Courage, courage);
                detail.HoodIds.Add(guard.Id);
            }

            return Bodyguards.Attempt(roster, new System.Random(seed), 10,
                "Pearl Street", incidents);
        }

        static void ADetailStandsBetweenHimAndIt(List<string> failures)
        {
            // Nobody in front of him: it gets through.
            var bare = Attempt(0, 90, 7, out _, out _);
            if (!bare.ReachedTheBoss)
                failures.Add("ADetailStandsBetweenHimAndIt: a Don with no detail was " +
                             "not reached.");

            // Two steady men in front of him: it does not.
            var incidents = new List<Incident>();
            var held = Attempt(2, 95, 7, out var roster, out var boss, incidents);
            if (held.ReachedTheBoss)
                failures.Add("ADetailStandsBetweenHimAndIt: two steady guards and it " +
                             "still got through.");
            if (held.GuardsSpent != 1)
                failures.Add($"ADetailStandsBetweenHimAndIt: {held.GuardsSpent} men were " +
                             "spent stopping one attempt; it should cost exactly one.");
            if (boss.Gone)
                failures.Add("ADetailStandsBetweenHimAndIt: the Don was struck off by an " +
                             "attempt his men stopped.");

            // The man who took it is really gone from the day's work - dead or in a bed -
            // and the paper has a line about it.
            var spentMan = 0;
            for (var i = 0; i < roster.Members.Count; i++)
                if (roster.Members[i].Status == CharacterStatus.Dead ||
                    roster.Members[i].Status == CharacterStatus.Hospitalized)
                    spentMan++;
            if (spentMan != 1)
                failures.Add($"ADetailStandsBetweenHimAndIt: {spentMan} men actually paid " +
                             "for it - putting a man in front of a gun has to cost one.");
            if (incidents.Count == 0)
                failures.Add("ADetailStandsBetweenHimAndIt: nothing was printed about a " +
                             "man taking a bullet for the Don.");
        }

        /// <summary>
        /// The Don does not walk out of his own front alone: the men who already answer
        /// directly to him stand with him, as many as a crew can stand. It is a MOVE and
        /// not a new posting - he was their man before and after - so nothing about them
        /// is re-aimed, and nobody is taken off a lieutenant's branch to do it.
        /// </summary>
        static void HisOwnMenFallInBehindHim(List<string> failures)
        {
            const int Steady = 71;
            var roster = new Roster();
            var boss = MakeBoss(roster, 8, 8);

            var direct = new List<int>();
            for (var i = 0; i < Crew.MaxTacticalHoods + 2; i++)
            {
                var man = MakeHood(roster);
                man.Loyalty = Steady;
                roster.Organization.BossHoodIds.Add(man.Id);
                direct.Add(man.Id);
            }

            MakeLieutenant(roster, 8, out var branch);
            var his = MakeHood(roster);
            branch.HoodIds.Add(his.Id);

            // And the man on the front desk, who the books also list under the Boss
            // because he is on nobody's branch. He is posted, not free.
            var desk = MakeHood(roster);
            roster.Organization.BossHoodIds.Add(desk.Id);
            roster.FrontId = desk.Id;

            var fell = Bodyguards.FallIn(roster);
            var detail = Bodyguards.DetailOf(roster);
            if (detail == null || detail.LieutenantId != boss.Id)
            {
                failures.Add("HisOwnMenFallInBehindHim: the Don leads no detail.");
                return;
            }

            if (fell != Crew.MaxTacticalHoods ||
                detail.HoodIds.Count != Crew.MaxTacticalHoods)
                failures.Add($"HisOwnMenFallInBehindHim: {detail.HoodIds.Count} men stand " +
                             $"with him; a crew stands {Crew.MaxTacticalHoods}.");

            for (var i = 0; i < detail.HoodIds.Count; i++)
            {
                var guard = roster.Find(detail.HoodIds[i]);
                if (guard != null && guard.Loyalty != Steady)
                    failures.Add("HisOwnMenFallInBehindHim: a man's loyalty was re-aimed " +
                                 "by standing him in front of the man he already answered to.");
                if (roster.Organization.BossHoodIds.Contains(detail.HoodIds[i]))
                    failures.Add("HisOwnMenFallInBehindHim: a guard is on the books twice.");
            }

            if (!branch.HoodIds.Contains(his.Id))
                failures.Add("HisOwnMenFallInBehindHim: a lieutenant's man was taken for " +
                             "the detail.");
            if (detail.HoodIds.Contains(desk.Id) || roster.FrontId != desk.Id)
                failures.Add("HisOwnMenFallInBehindHim: the man on the front desk was " +
                             "marched off it to stand with the Don.");

            // The rest of them stay where they were, and asking twice changes nothing.
            // (The desk man is still under the Boss on paper, so he counts here.)
            if (roster.Organization.BossHoodIds.Count !=
                direct.Count + 1 - Crew.MaxTacticalHoods)
                failures.Add("HisOwnMenFallInBehindHim: the men who did not fit did not " +
                             "stay under him.");
            if (Bodyguards.FallIn(roster) != 0 ||
                detail.HoodIds.Count != Crew.MaxTacticalHoods)
                failures.Add("HisOwnMenFallInBehindHim: standing the detail up twice " +
                             "doubled it.");
        }

        static void ADetailOfCowardsIsNoDetailAtAll(List<string> failures)
        {
            // Six men who will not stand are six men who are not there. Run it on a
            // few streams so the assertion is about the rule and not about one roll.
            var gotThrough = 0;
            for (var seed = 0; seed < 8; seed++)
                if (Attempt(6, 0, seed, out _, out _).ReachedTheBoss)
                    gotThrough++;

            if (gotThrough == 0)
                failures.Add("ADetailOfCowardsIsNoDetailAtAll: six men with no nerve at " +
                             "all stopped every single attempt.");

            // And the same six, steady, stop all of them.
            var stopped = 0;
            for (var seed = 0; seed < 8; seed++)
                if (!Attempt(6, 95, seed, out _, out _).ReachedTheBoss)
                    stopped++;
            if (stopped != 8)
                failures.Add($"ADetailOfCowardsIsNoDetailAtAll: six steady men stopped " +
                             $"only {stopped} of 8 - a detail that deep should be a wall.");
        }

        static void GuardsCostWagesAndLearnLittle(List<string> failures)
        {
            var roster = new Roster();
            MakeBoss(roster, 8, 8);
            var detail = Bodyguards.FormDetail(roster);
            var guard = MakeHood(roster);
            detail.HoodIds.Add(guard.Id);

            if (Outfit.Wages.WageFor(guard) <= 0)
                failures.Add("GuardsCostWagesAndLearnLittle: a man on the detail draws " +
                             "nothing, so a detail is free.");

            var runner = new Outfit.CampaignRunner();
            for (var day = 0; day < 20; day++)
                runner.DayTick(roster);

            var row = ActivityXp.RowOf(Activity.BodyguardDuty);
            var moved = false;
            for (var i = 0; i < row.Trains.Length; i++)
                if (guard.GetHalfSteps(row.Trains[i]) > AttributeScale.MinHalfSteps ||
                    guard.GetPractice(row.Trains[i]) > 0)
                    moved = true;
            if (!moved)
                failures.Add("GuardsCostWagesAndLearnLittle: twenty days on the detail " +
                             "taught him nothing at all.");

            // But only what the detail teaches - standing behind the Don is not driving.
            if (guard.GetPractice(CharacterAttribute.Driving) != 0)
                failures.Add("GuardsCostWagesAndLearnLittle: guard duty taught him to " +
                             "drive.");

            // The detail counts against the Boss's own cap, because they are his men.
            var view = new OrganizationQuery(roster).CapacityOf(roster.BossId);
            if (view.Manpower.Current < 1)
                failures.Add("GuardsCostWagesAndLearnLittle: the guard does not count " +
                             "against the Don's own capacity, so a detail is free there " +
                             "too.");
        }

        // ----------------------------------------------------------- what he extracts

        static void TheSameMenHoldMoreUnderABetterMan(List<string> failures)
        {
            var roster = new Roster();
            var poor = MakeLieutenant(roster, AttributeScale.MinHalfSteps, out var poorCrew);
            poor.SetHalfSteps(CharacterAttribute.Organization, AttributeScale.MinHalfSteps);
            var great = MakeLieutenant(roster, AttributeScale.MaxHalfSteps, out var greatCrew);
            great.SetHalfSteps(CharacterAttribute.Organization, AttributeScale.MaxHalfSteps);

            if (Command.PresenceFactor(poor) != Command.WorstPresenceFactor)
                failures.Add($"TheSameMenHoldMoreUnderABetterMan: the worst commander " +
                             $"extracts {Command.PresenceFactor(poor)}, not " +
                             $"{Command.WorstPresenceFactor}.");
            if (Command.PresenceFactor(great) != Command.BestPresenceFactor)
                failures.Add($"TheSameMenHoldMoreUnderABetterMan: the best extracts " +
                             $"{Command.PresenceFactor(great)}, not " +
                             $"{Command.BestPresenceFactor}.");

            // The same man, moved between them, is worth what his commander makes of
            // him - and the change follows him the moment he is recrewed.
            var hood = MakeHood(roster);
            poorCrew.HoodIds.Add(hood.Id);
            var under = Command.PresenceFactorFor(roster, hood.Id);
            if (under != Command.WorstPresenceFactor)
                failures.Add($"TheSameMenHoldMoreUnderABetterMan: a man under the worst " +
                             $"commander is worth {under}.");

            poorCrew.HoodIds.Remove(hood.Id);
            greatCrew.HoodIds.Add(hood.Id);
            var moved = Command.PresenceFactorFor(roster, hood.Id);
            if (moved <= under)
                failures.Add($"TheSameMenHoldMoreUnderABetterMan: he moved to a better " +
                             $"crew and is worth {moved} against {under}.");

            // Five men under each: the better crew holds measurably more ground with
            // exactly the same headcount, and not so much more that headcount stops
            // mattering.
            var poorBlock = 5 * Command.PresenceFactor(poor);
            var greatBlock = 5 * Command.PresenceFactor(great);
            if (greatBlock <= poorBlock)
                failures.Add("TheSameMenHoldMoreUnderABetterMan: command quality does " +
                             "not move a block at all.");
            if (greatBlock >= poorBlock * 2f)
                failures.Add($"TheSameMenHoldMoreUnderABetterMan: five men under a good " +
                             $"commander are worth {greatBlock} against {poorBlock} - " +
                             "that replaces headcount instead of weighting it.");

            // Nobody's man, and nobody at all, stand at exactly what they are worth.
            var loose = MakeHood(roster);
            if (Command.PresenceFactorFor(roster, loose.Id) != 1f)
                failures.Add("TheSameMenHoldMoreUnderABetterMan: a pooled man is not " +
                             "worth his own weight.");
            if (Command.PresenceFactorFor(roster, -3) != 1f)
                failures.Add("TheSameMenHoldMoreUnderABetterMan: a rival body, on " +
                             "nobody's books, was weighted by our command.");
            if (Command.PresenceFactorFor(null, 0) != 1f)
                failures.Add("TheSameMenHoldMoreUnderABetterMan: no roster at all did " +
                             "not read as neutral.");
        }

        static void OverCapacityIsFlaggedNeverFixed(List<string> failures)
        {
            // The player is refused; the WORLD is not. A recruit who comes back to an
            // overloaded lieutenant is still a man on the books, and the overload is
            // something the ledger SHOWS rather than something the sim quietly fixes by
            // losing him.
            var roster = new Roster();
            var lieutenant = MakeLieutenant(roster, AttributeScale.MinHalfSteps,
                out var crew);
            var cap = Command.ManCap(lieutenant, roster.Organization.Limits);

            for (var i = 0; i < cap + 3; i++)
                crew.HoodIds.Add(MakeHood(roster).Id);

            var view = new OrganizationQuery(roster).CapacityOf(lieutenant.Id);
            if (!view.IsOverCapacity)
                failures.Add("OverCapacityIsFlaggedNeverFixed: three men over and the " +
                             "ledger says nothing.");
            if (view.Manpower.Overage != 3)
                failures.Add($"OverCapacityIsFlaggedNeverFixed: the excess reads " +
                             $"{view.Manpower.Overage}, not 3.");
            if (crew.HoodIds.Count != cap + 3)
                failures.Add("OverCapacityIsFlaggedNeverFixed: somebody was quietly " +
                             "struck off to make the numbers work.");
        }

        /// <summary>
        /// SET UP BUSINESS is on the sheet, charges the fit-out and opens nothing -
        /// there is no case for it anywhere the world is written. Until there is, the
        /// counter refuses it by name and the safe does not move. The row itself stays
        /// where it is: a refused order keeps its place and says why.
        /// </summary>
        static void NothingIsSetUpYet(List<string> failures)
        {
            var roster = new Roster();
            MakeLieutenant(roster, AttributeScale.HalfStepsFor(50), out var crew);
            crew.HoodIds.Add(MakeHood(roster).Id);
            crew.HoodIds.Add(MakeHood(roster).Id);

            var runner = new Outfit.CampaignRunner { Seed = 1987, DistanceOf = _ => 400f };
            runner.OpenFirstSheet();
            var before = runner.Accounts.Safe;

            var job = new Outfit.Job
            {
                CrewId = crew.Id,
                Type = Outfit.OrderType.SetUpBusiness,
                Men = 2,
                TargetBlockId = 3,
                TargetLabel = "an empty storefront on Kirby Street",
            };

            var result = runner.Issue(roster, job);
            if (result.Ok)
                failures.Add("NothingIsSetUpYet: the counter took the order.");
            if (result.Reason != UI.LedgerText.ReasonNotBuiltYet)
                failures.Add("NothingIsSetUpYet: the refusal reads \"" + result.Reason +
                             "\", not the one reason there is.");
            if (runner.Book.Jobs.Count != 0)
                failures.Add("NothingIsSetUpYet: the refused order went on the book.");

            // A month of hours over it: nothing to work, so nothing to pay for.
            runner.AdvanceHours(roster, 200f);
            if (runner.Accounts.Safe != before)
                failures.Add($"NothingIsSetUpYet: the safe moved from {before} to " +
                             $"{runner.Accounts.Safe} on an order that was refused.");
            if (runner.Records.Count != 0)
                failures.Add("NothingIsSetUpYet: a record was written for work nobody " +
                             "was sent on.");

            // The lieutenant is not busy afterwards - a refusal is not an errand.
            if (runner.Book.CurrentFor(crew.Id) != null)
                failures.Add("NothingIsSetUpYet: the crew came away busy.");
        }
    }
}
