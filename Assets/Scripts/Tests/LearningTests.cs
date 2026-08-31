using System;
using System.Collections.Generic;
using LivingCity.Outfit;
using LivingCity.Personnel;

namespace LivingCity.Tests
{
    /// <summary>
    /// EPIC 12's contract: doing the job is the only way anybody improves. The table
    /// that says what work teaches, the passive drip a commander earns by holding the
    /// chair, and the law that keeps the two honest - what teaches most must cost most.
    ///
    /// Pure C#, no UnityEngine, failures returned as data - the same discipline as
    /// <see cref="SkillFoundationTests"/>, and the same stale-binary tell.
    /// </summary>
    public static class LearningTests
    {
        static readonly (string Name, Action<List<string>> Check)[] Contracts =
        {
            ("EveryActivityTeachesSomething", EveryActivityTeachesSomething),
            ("NoSkillIsAnOrphan", NoSkillIsAnOrphan),
            ("EveryOrderIsSomeKindOfWork", EveryOrderIsSomeKindOfWork),
            ("PayIsOrderedByDanger", PayIsOrderedByDanger),
            ("FailingAtHardWorkBeatsSucceedingAtEasy", FailingAtHardWorkBeatsSucceedingAtEasy),
            ("AwardBanksAgainstEverySkillItTrains", AwardBanksAgainstEverySkillItTrains),
            ("TheDeadLearnNothing", TheDeadLearnNothing),
            ("AQuietCommanderStillLearns", AQuietCommanderStillLearns),
            ("AnEmptyCrewTeachesNobody", AnEmptyCrewTeachesNobody),
            ("ACommanderInACellStopsDripping", ACommanderInACellStopsDripping),
            ("TheDripLandsBeforeTheBooksTurn", TheDripLandsBeforeTheBooksTurn),
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

        // ------------------------------------------------------------------ the table

        static void EveryActivityTeachesSomething(List<string> failures)
        {
            foreach (Activity activity in Enum.GetValues(typeof(Activity)))
            {
                var row = ActivityXp.RowOf(activity);
                if (row.Activity != activity)
                {
                    failures.Add($"EveryActivityTeachesSomething: {activity} has no row " +
                                 "in the table at all.");
                    continue;
                }
                if (row.Trains == null || row.Trains.Length == 0)
                    failures.Add($"EveryActivityTeachesSomething: {activity} trains " +
                                 "nobody in anything.");
                if (row.BaseXp <= 0)
                    failures.Add($"EveryActivityTeachesSomething: {activity} pays " +
                                 $"{row.BaseXp}.");
            }
        }

        static void NoSkillIsAnOrphan(List<string> failures)
        {
            // A skill nothing trains is a row on the ledger card that can never move,
            // which is the §3 rule wearing a different hat.
            for (var s = 0; s < AttributeScale.Count; s++)
            {
                var skill = (CharacterAttribute)s;
                var active = false;
                var passiveOnly = false;

                for (var r = 0; r < ActivityXp.Rows.Length; r++)
                {
                    var row = ActivityXp.Rows[r];
                    for (var t = 0; t < row.Trains.Length; t++)
                    {
                        if (row.Trains[t] != skill)
                            continue;
                        if (row.Passive)
                            passiveOnly = true;
                        else
                            active = true;
                    }
                }

                if (!active && !passiveOnly)
                    failures.Add($"NoSkillIsAnOrphan: nothing anybody can be sent to do " +
                                 $"trains {skill}.");
            }
        }

        static void EveryOrderIsSomeKindOfWork(List<string> failures)
        {
            // The map has a default arm, which is exactly how a new order type gets
            // silently filed as a patrol. Every type is named here so adding one to the
            // book fails this contract until somebody decides what it teaches.
            foreach (OrderType type in Enum.GetValues(typeof(OrderType)))
            {
                var activity = OrderTable.ActivityOf(type);
                var row = ActivityXp.RowOf(activity);
                if (row.Trains.Length == 0)
                    failures.Add($"EveryOrderIsSomeKindOfWork: {type} maps to " +
                                 $"{activity}, which teaches nothing.");

                if (activity == Activity.BlockPatrol && type != OrderType.Patrol &&
                    type != OrderType.Guard)
                    failures.Add($"EveryOrderIsSomeKindOfWork: {type} fell through to " +
                                 "the default arm and is being filed as a patrol.");
            }
        }

        static void PayIsOrderedByDanger(List<string> failures)
        {
            // The balance law: nothing may teach more than something more dangerous
            // than it. Passive command is exempt - it is the one row that is meant to
            // pay without exposing anybody.
            for (var a = 0; a < ActivityXp.Rows.Length; a++)
                for (var b = 0; b < ActivityXp.Rows.Length; b++)
                {
                    var first = ActivityXp.Rows[a];
                    var second = ActivityXp.Rows[b];
                    if (first.Passive || second.Passive)
                        continue;

                    if (first.BaseXp > second.BaseXp && first.Risk < second.Risk)
                        failures.Add($"PayIsOrderedByDanger: {first.Activity} pays " +
                                     $"{first.BaseXp} at risk {first.Risk} while " +
                                     $"{second.Activity} pays {second.BaseXp} at risk " +
                                     $"{second.Risk} - grinding just got cheaper.");
                }
        }

        static void FailingAtHardWorkBeatsSucceedingAtEasy(List<string> failures)
        {
            var botchedRaid = ActivityXp.Points(Activity.AttackOnARival, XpOutcome.Failed);
            var cleanPatrol = ActivityXp.Points(Activity.BlockPatrol, XpOutcome.Completed);
            if (botchedRaid <= cleanPatrol)
                failures.Add($"FailingAtHardWork: a botched attack teaches {botchedRaid} " +
                             $"against {cleanPatrol} for a clean patrol - failing on " +
                             "purpose is now the cheapest training there is.");

            foreach (Activity activity in Enum.GetValues(typeof(Activity)))
            {
                var completed = ActivityXp.Points(activity, XpOutcome.Completed);
                var partial = ActivityXp.Points(activity, XpOutcome.Partial);
                var failed = ActivityXp.Points(activity, XpOutcome.Failed);
                if (completed < partial || partial < failed || failed <= 0)
                    failures.Add($"FailingAtHardWork: {activity} pays {completed}/" +
                                 $"{partial}/{failed} for done/half/botched.");
            }
        }

        // ------------------------------------------------------------------ the door

        static void AwardBanksAgainstEverySkillItTrains(List<string> failures)
        {
            var roster = new Roster();
            var man = new Character { Id = roster.NextCharacterId(), FirstName = "Sal" };
            roster.Members.Add(man);

            var points = ActivityXp.Award(man, Activity.Scouting, XpOutcome.Completed);
            var row = ActivityXp.RowOf(Activity.Scouting);

            for (var t = 0; t < row.Trains.Length; t++)
                if (man.GetPractice(row.Trains[t]) != points)
                    failures.Add($"AwardBanks: a night scouting banked " +
                                 $"{man.GetPractice(row.Trains[t])} against " +
                                 $"{row.Trains[t]}, not {points}.");

            // And against nothing else. A night out watching a street teaches him
            // nothing about driving.
            for (var s = 0; s < AttributeScale.Count; s++)
            {
                var skill = (CharacterAttribute)s;
                var trained = false;
                for (var t = 0; t < row.Trains.Length; t++)
                    if (row.Trains[t] == skill)
                        trained = true;
                if (!trained && man.GetPractice(skill) != 0)
                    failures.Add($"AwardBanks: scouting taught him {skill} as well.");
            }

            // The command drip scales with the span, and sub-linearly.
            var small = ActivityXp.AwardCommand(
                new Character { Id = 1 }, 4);
            var large = ActivityXp.AwardCommand(
                new Character { Id = 2 }, 50);
            if (large <= small)
                failures.Add("AwardBanks: commanding fifty taught no more than four.");
            if (large > small * 50 / 4)
                failures.Add($"AwardBanks: the command drip scaled {small} to {large} - " +
                             "that is not sub-linear.");
            if (ActivityXp.AwardCommand(new Character { Id = 3 }, 0) != 0)
                failures.Add("AwardBanks: a lieutenant with nobody under him still " +
                             "drew a command drip.");
        }

        // ------------------------------------------------------------------ the drip

        /// <summary>A crew and nothing else: one lieutenant, some hoods, no ceilings on
        /// anybody, no dates of birth so the years never touch them.</summary>
        static Character MakeCrew(Roster roster, int hoods, out List<Character> men)
        {
            var lieutenant = new Character
            {
                Id = roster.NextCharacterId(),
                FirstName = "Rocco",
                Surname = "Vale",
                Rank = Rank.Lieutenant,
            };
            roster.Members.Add(lieutenant);

            var crew = new Crew { Id = roster.NextCrewId(), LieutenantId = lieutenant.Id };
            men = new List<Character>();
            for (var i = 0; i < hoods; i++)
            {
                var hood = new Character
                {
                    Id = roster.NextCharacterId(),
                    FirstName = "Hood" + i,
                    Surname = "Ferri",
                };
                roster.Members.Add(hood);
                crew.HoodIds.Add(hood.Id);
                men.Add(hood);
            }
            roster.Crews.Add(crew);
            return lieutenant;
        }

        /// <summary>How far along a skill he is, as one number that only ever goes up.
        /// Practice alone falls when it is spent on a half-step, so a test that watched
        /// practice would read a promotion as a loss.</summary>
        static int Progress(Character man, CharacterAttribute skill) =>
            man.GetHalfSteps(skill) * 10_000 + man.GetPractice(skill);

        static int CommandPractice(Character man)
        {
            var row = ActivityXp.RowOf(Activity.CommandingACrew);
            var total = 0;
            for (var i = 0; i < row.Trains.Length; i++)
                total += man.GetHalfSteps(row.Trains[i]);
            return total;
        }

        static void AQuietCommanderStillLearns(List<string> failures)
        {
            var roster = new Roster();
            var lieutenant = MakeCrew(roster, 3, out var hoods);
            var runner = new CampaignRunner();

            var before = CommandPractice(lieutenant);
            var combatBefore = lieutenant.GetHalfSteps(CharacterAttribute.Combat);

            for (var day = 0; day < 30; day++)
                runner.DayTick(roster);

            if (CommandPractice(lieutenant) <= before)
                failures.Add("AQuietCommanderStillLearns: thirty days holding a crew and " +
                             "he is exactly the man he was.");
            if (lieutenant.GetHalfSteps(CharacterAttribute.Combat) != combatBefore)
                failures.Add("AQuietCommanderStillLearns: sitting in the chair taught " +
                             "him to shoot.");

            var row = ActivityXp.RowOf(Activity.CommandingACrew);
            for (var i = 1; i < row.Trains.Length; i++)
                if (lieutenant.GetHalfSteps(row.Trains[i]) !=
                    lieutenant.GetHalfSteps(row.Trains[0]))
                    failures.Add($"AQuietCommanderStillLearns: {row.Trains[i]} did not " +
                                 $"move with {row.Trains[0]}.");

            for (var i = 0; i < hoods.Count; i++)
                for (var s = 0; s < AttributeScale.Count; s++)
                    if (hoods[i].GetPractice((CharacterAttribute)s) != 0)
                        failures.Add($"AQuietCommanderStillLearns: {hoods[i].FullName} " +
                                     "learned something by being commanded at.");
        }

        static void AnEmptyCrewTeachesNobody(List<string> failures)
        {
            var roster = new Roster();
            var lieutenant = MakeCrew(roster, 0, out _);
            var runner = new CampaignRunner();

            for (var day = 0; day < 30; day++)
                runner.DayTick(roster);

            for (var s = 0; s < AttributeScale.Count; s++)
                if (lieutenant.GetPractice((CharacterAttribute)s) != 0 ||
                    lieutenant.GetHalfSteps((CharacterAttribute)s) !=
                    AttributeScale.MinHalfSteps)
                    failures.Add("AnEmptyCrewTeachesNobody: a lieutenant with nobody " +
                                 "under him spent a month getting better at commanding " +
                                 "nobody.");
        }

        static void ACommanderInACellStopsDripping(List<string> failures)
        {
            var roster = new Roster();
            var lieutenant = MakeCrew(roster, 4, out _);
            var runner = new CampaignRunner();

            var start = Progress(lieutenant, CharacterAttribute.Leadership);
            runner.DayTick(roster);
            var afterOneDay = Progress(lieutenant, CharacterAttribute.Leadership) - start;

            RosterOps.Jail(roster, lieutenant.Id, backOnDay: runner.Campaign.Day + 5);
            var inside = Progress(lieutenant, CharacterAttribute.Leadership);
            for (var day = 0; day < 4; day++)
                runner.DayTick(roster);

            var stillInside = Progress(lieutenant, CharacterAttribute.Leadership);
            if (stillInside != inside)
                failures.Add("ACommanderInACellStopsDripping: he ran the crew from a " +
                             "cell and got better at it.");
            if (afterOneDay <= 0)
                failures.Add("ACommanderInACellStopsDripping: he never dripped at all, " +
                             "so the cell proves nothing.");

            // Out on his day, and back on the books.
            for (var day = 0; day < 4; day++)
                runner.DayTick(roster);
            if (lieutenant.Status != CharacterStatus.Active)
                failures.Add("ACommanderInACellStopsDripping: he never got out.");
            var after = Progress(lieutenant, CharacterAttribute.Leadership);
            if (after <= stillInside)
                failures.Add("ACommanderInACellStopsDripping: he came out and never " +
                             "started learning again.");
        }

        static void TheDripLandsBeforeTheBooksTurn(List<string> failures)
        {
            // Tick ordering is invisible in play and obvious here: bank one day short
            // of the next half-step, and the day he commands must be the day he buys it.
            var roster = new Roster();
            var lieutenant = MakeCrew(roster, 3, out _);
            var runner = new CampaignRunner();

            var drip = ActivityXp.Points(Activity.CommandingACrew, XpOutcome.Completed);
            var cost = Practice.NextCost(lieutenant, CharacterAttribute.Leadership);
            if (cost <= drip)
            {
                failures.Add($"TheDripLandsBeforeTheBooksTurn: a half-step costs {cost} " +
                             $"and a day of command pays {drip} - the test cannot tell " +
                             "the ordering apart any more.");
                return;
            }

            lieutenant.AddPractice(CharacterAttribute.Leadership, cost - drip);
            var before = lieutenant.GetHalfSteps(CharacterAttribute.Leadership);
            runner.DayTick(roster);

            if (lieutenant.GetHalfSteps(CharacterAttribute.Leadership) != before + 1)
                failures.Add("TheDripLandsBeforeTheBooksTurn: today's command day was " +
                             "banked after the books turned, so it counted tomorrow.");
        }

        static void TheDeadLearnNothing(List<string> failures)
        {
            var dead = new Character { Id = 0, Status = CharacterStatus.Dead };
            var gone = new Character { Id = 1, Status = CharacterStatus.Deserted };

            if (ActivityXp.Award(dead, Activity.AttackOnARival, XpOutcome.Completed) != 0 ||
                ActivityXp.Award(gone, Activity.BlockPatrol, XpOutcome.Completed) != 0)
                failures.Add("TheDeadLearnNothing: a man off the books banked practice.");
            if (dead.GetPractice(CharacterAttribute.Combat) != 0)
                failures.Add("TheDeadLearnNothing: the dead man's sheet moved.");
            if (ActivityXp.AwardCommand(dead, 10) != 0)
                failures.Add("TheDeadLearnNothing: the dead man drew a command drip.");
        }
    }
}
