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
    }
}
