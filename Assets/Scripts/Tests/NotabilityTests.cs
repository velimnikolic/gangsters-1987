using System;
using System.Collections.Generic;
using LivingCity.Personnel;

namespace LivingCity.Tests
{
    /// <summary>
    /// EPIC 16's contract: every man keeps his name, and the ledger rations the
    /// player's ATTENTION instead.
    ///
    /// Two properties carry the whole epic. The first is that a score is a fold - the
    /// same campaign history produces the same ordering, forever, with no counter
    /// anywhere that a save or a replay could desync. The second is that a file is a
    /// life read forward and never grows without bound: what a five-year career keeps
    /// is what mattered in it.
    ///
    /// Pure C#, no UnityEngine, failures returned as data.
    /// </summary>
    public static class NotabilityTests
    {
        static readonly (string Name, Action<List<string>> Check)[] Contracts =
        {
            ("TheQuietManSinksAndTheLoudManRises", TheQuietManSinksAndTheLoudManRises),
            ("OldNewsFadesToNothing", OldNewsFadesToNothing),
            ("AStandingFlagHoldsHisFloor", AStandingFlagHoldsHisFloor),
            ("TheSameHistoryScoresTheSameTwice", TheSameHistoryScoresTheSameTwice),
            ("TheScoreNeverReadsTheFuture", TheScoreNeverReadsTheFuture),
            ("TheRollSortsByWhatHappened", TheRollSortsByWhatHappened),
            ("TheGroupingSurvivesTheSort", TheGroupingSurvivesTheSort),
            ("ARollWithNoBoardIsRosterOrder", ARollWithNoBoardIsRosterOrder),
            ("ACareerReadsForward", ACareerReadsForward),
            ("AFileNeverGrowsWithoutBound", AFileNeverGrowsWithoutBound),
            ("ARankChangeIsNeverCulled", ARankChangeIsNeverCulled),
            ("TheCullKeepsWhatMattered", TheCullKeepsWhatMattered),
            ("EveryLineCameOffARealRecord", EveryLineCameOffARealRecord),
            ("TheFileAndThePaperAgree", TheFileAndThePaperAgree),
            ("NothingHereWritesOnAnybody", NothingHereWritesOnAnybody),
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

        // ------------------------------------------------------------- the fixtures

        static Character Man(int id, string surname)
        {
            var man = new Character
            {
                Id = id, FirstName = "Man", Surname = surname, Loyalty = 60,
            };
            Personality.Set(man, PersonalityTrait.Ambition, 40);
            Personality.Set(man, PersonalityTrait.Courage, 40);
            Personality.Set(man, PersonalityTrait.Discipline, 40);
            return man;
        }

        static void Happened(Character man, IncidentKind kind, int day, string where = "")
        {
            var incident = new Incident(man.Id, man.FullName, kind, day, where, 0,
                IncidentText.Line(kind, man.FullName, where));
            Career.FromIncident(man, incident);
        }

        // ---------------------------------------------------- NOTE-001, the score

        static void TheQuietManSinksAndTheLoudManRises(List<string> failures)
        {
            var loud = Man(1, "Loud");
            var quiet = Man(2, "Quiet");

            Happened(loud, IncidentKind.StoppedIt, 100);
            Happened(quiet, IncidentKind.Deviated, 100);

            if (Notability.Of(loud, 100) <= Notability.Of(quiet, 100))
                failures.Add("TheQuietManSinksAndTheLoudManRises: taking a bullet for " +
                             "the Don scored no higher than doing a job untidily.");
            if (!Notability.Marked(loud, 100))
                failures.Add("TheQuietManSinksAndTheLoudManRises: the man who took a " +
                             "bullet for the Don is not marked.");
            if (!Notability.Fresh(loud, 100))
                failures.Add("TheQuietManSinksAndTheLoudManRises: today's event is not " +
                             "this week's news.");
            if (Notability.Fresh(loud, 100 + Notability.FreshDays))
                failures.Add("TheQuietManSinksAndTheLoudManRises: a week-old event is " +
                             "still ticked as new.");

            var nobody = Man(3, "Nobody");
            if (Notability.Of(nobody, 100) != 0)
                failures.Add("TheQuietManSinksAndTheLoudManRises: a man nothing has " +
                             "ever happened to scores " + Notability.Of(nobody, 100) + ".");
        }

        static void OldNewsFadesToNothing(List<string> failures)
        {
            var man = Man(1, "Faded");
            Happened(man, IncidentKind.Escalated, 10);

            var fresh = Notability.Of(man, 10);
            var half = Notability.Of(man, 10 + Notability.FadesInDays / 2);
            var gone = Notability.Of(man, 10 + Notability.FadesInDays);

            if (half >= fresh)
                failures.Add($"OldNewsFadesToNothing: half a fade later he still read " +
                             $"{half} against {fresh}.");
            if (gone != 0)
                failures.Add($"OldNewsFadesToNothing: a fully faded event still scores " +
                             $"{gone}.");
            if (Notability.Marked(man, 10 + Notability.FadesInDays))
                failures.Add("OldNewsFadesToNothing: a quiet man is still marked.");
        }

        static void AStandingFlagHoldsHisFloor(List<string> failures)
        {
            var man = Man(1, "Officer");
            man.SetHalfSteps(CharacterAttribute.Leadership,
                AttributeScale.HalfStepsFor(ManFlags.LeadershipForCrew));
            man.SetHalfSteps(CharacterAttribute.Organization,
                AttributeScale.HalfStepsFor(ManFlags.OrganizationForCrew));
            man.SetHalfSteps(CharacterAttribute.StreetAuthority,
                AttributeScale.HalfStepsFor(ManFlags.StreetAuthorityForCrew));

            Happened(man, IncidentKind.Deviated, 1);
            var long_after = Notability.Of(man, 1 + Notability.FadesInDays * 2);
            if (long_after != Notability.LieutenantMaterialFloor)
                failures.Add("AStandingFlagHoldsHisFloor: lieutenant material sank to " +
                             $"{long_after}, not the floor " +
                             $"{Notability.LieutenantMaterialFloor}.");

            // And the floor is a floor, never a bonus stacked on real news.
            Happened(man, IncidentKind.DiedOnTheDetail, 200);
            var loud = Notability.Of(man, 200);
            var expected = Notability.WeightOf(IncidentKind.DiedOnTheDetail);
            if (loud != expected)
                failures.Add($"AStandingFlagHoldsHisFloor: a flagged man's real news " +
                             $"read {loud} rather than {expected} - the floor was added " +
                             "rather than applied.");
        }

        static void TheSameHistoryScoresTheSameTwice(List<string> failures)
        {
            var first = Man(1, "Twice");
            var second = Man(1, "Twice");
            var kinds = new[]
            {
                IncidentKind.Froze, IncidentKind.Escalated, IncidentKind.Fled,
                IncidentKind.CaughtSkimming, IncidentKind.StoppedIt,
            };

            for (var i = 0; i < kinds.Length; i++)
            {
                Happened(first, kinds[i], 10 + i * 3);
                Happened(second, kinds[i], 10 + i * 3);
            }
            Career.RankChanged(first, 20, Rank.Lieutenant, "given a crew");
            Career.RankChanged(second, 20, Rank.Lieutenant, "given a crew");

            for (var day = 10; day < 10 + Notability.FadesInDays + 5; day++)
                if (Notability.Of(first, day) != Notability.Of(second, day))
                {
                    failures.Add($"TheSameHistoryScoresTheSameTwice: day {day} read " +
                                 $"{Notability.Of(first, day)} then " +
                                 $"{Notability.Of(second, day)}.");
                    break;
                }
        }

        static void TheScoreNeverReadsTheFuture(List<string> failures)
        {
            var man = Man(1, "Ahead");
            Happened(man, IncidentKind.DiedOnTheDetail, 100);

            if (Notability.Of(man, 50) != 0)
                failures.Add("TheScoreNeverReadsTheFuture: a fold run in the spring " +
                             "counted something that happens in the summer.");
            if (Notability.Fresh(man, 50))
                failures.Add("TheScoreNeverReadsTheFuture: an event yet to happen is " +
                             "this week's news.");
        }

        // ----------------------------------------------------- NOTE-001, the sort

        static Roster ThreeInACrew(out Character quiet, out Character loud,
            out Character middling)
        {
            var roster = new Roster();
            var lieutenant = new Character
            {
                Id = roster.NextCharacterId(), FirstName = "Lt", Surname = "Bruno",
                Rank = Rank.Lieutenant,
            };
            roster.Members.Add(lieutenant);
            var crew = new Crew { Id = roster.NextCrewId(), LieutenantId = lieutenant.Id };
            roster.Crews.Add(crew);

            quiet = Man(roster.NextCharacterId(), "Quiet");
            loud = Man(roster.NextCharacterId(), "Loud");
            middling = Man(roster.NextCharacterId(), "Middling");
            foreach (var man in new[] { quiet, loud, middling })
            {
                roster.Members.Add(man);
                crew.HoodIds.Add(man.Id);
            }

            // A defection is deliberately NOT used as the loud event here: it reaches a
            // file through the strike-off door rather than the incident mirror, and
            // LoyaltyTests owns that path.
            Happened(loud, IncidentKind.DiedOnTheDetail, 100);
            Happened(middling, IncidentKind.Escalated, 100);
            return roster;
        }

        static void TheRollSortsByWhatHappened(List<string> failures)
        {
            var roster = ThreeInACrew(out var quiet, out var loud, out var middling);
            var board = new NotabilityBoard();
            board.Rebuild(roster, 100);

            var rows = new List<LedgerRow>();
            RosterView.Build(roster,
                new ViewOptions { Sort = SortKey.Notability, Board = board }, rows);

            var order = new List<int>();
            for (var i = 0; i < rows.Count; i++)
                if (rows[i].Kind == RowKind.Character)
                    order.Add(rows[i].CharacterId);

            if (order.Count != 3)
            {
                failures.Add($"TheRollSortsByWhatHappened: {order.Count} men on the " +
                             "roll, not three.");
                return;
            }
            if (order[0] != loud.Id || order[1] != middling.Id || order[2] != quiet.Id)
                failures.Add("TheRollSortsByWhatHappened: the roll read " +
                             string.Join(", ", order) + " rather than loud, middling, " +
                             "quiet.");

            if (board.ScoreOf(loud.Id) != Notability.Of(loud, 100))
                failures.Add("TheRollSortsByWhatHappened: the board and the fold " +
                             "disagree about the same man.");
            if (!board.Marked(loud.Id) || board.Marked(quiet.Id))
                failures.Add("TheRollSortsByWhatHappened: the wrong men are marked.");
        }

        static void TheGroupingSurvivesTheSort(List<string> failures)
        {
            var roster = ThreeInACrew(out _, out var loud, out _);
            // One more man, outside the crew and louder than anybody in it.
            var pooled = Man(roster.NextCharacterId(), "Pooled");
            roster.Members.Add(pooled);
            Happened(pooled, IncidentKind.DiedOnTheDetail, 100);
            Happened(pooled, IncidentKind.StoppedIt, 100);

            var board = new NotabilityBoard();
            board.Rebuild(roster, 100);

            var rows = new List<LedgerRow>();
            RosterView.Build(roster,
                new ViewOptions { Sort = SortKey.Notability, Board = board }, rows);

            // The crew's header still comes first, and the loudest man in the CITY does
            // not jump out of the pool into it.
            if (rows.Count == 0 || rows[0].Kind != RowKind.CrewHeader)
                failures.Add("TheGroupingSurvivesTheSort: the roll no longer opens on a " +
                             "crew.");

            var seenPoolHeader = false;
            for (var i = 0; i < rows.Count; i++)
            {
                if (rows[i].Kind == RowKind.PoolHeader)
                    seenPoolHeader = true;
                if (rows[i].Kind == RowKind.Character && rows[i].CharacterId == pooled.Id
                    && !seenPoolHeader)
                    failures.Add("TheGroupingSurvivesTheSort: a pooled man was sorted " +
                                 "up into a crew.");
                if (rows[i].Kind == RowKind.Character && rows[i].CharacterId == loud.Id
                    && seenPoolHeader)
                    failures.Add("TheGroupingSurvivesTheSort: a crewman was sorted down " +
                                 "into the pool.");
            }
        }

        static void ARollWithNoBoardIsRosterOrder(List<string> failures)
        {
            var roster = ThreeInACrew(out var quiet, out var loud, out _);

            var sorted = new List<LedgerRow>();
            RosterView.Build(roster, new ViewOptions { Sort = SortKey.Notability },
                sorted);
            var plain = new List<LedgerRow>();
            RosterView.Build(roster, new ViewOptions { Sort = SortKey.Roster }, plain);

            if (sorted.Count != plain.Count)
            {
                failures.Add("ARollWithNoBoardIsRosterOrder: the two rolls are different " +
                             "lengths.");
                return;
            }
            for (var i = 0; i < sorted.Count; i++)
                if (sorted[i].CharacterId != plain[i].CharacterId)
                {
                    failures.Add("ARollWithNoBoardIsRosterOrder: a page with no campaign " +
                                 "behind it did not fall back to the roster's own order.");
                    break;
                }

            if (quiet == null || loud == null)
                failures.Add("ARollWithNoBoardIsRosterOrder: the fixture lost a man.");
        }

        // -------------------------------------------------- NOTE-002, the history

        static void ACareerReadsForward(List<string> failures)
        {
            var man = Man(1, "Career");
            Career.Joined(man, 1, "Rossi");
            Happened(man, IncidentKind.Escalated, 20, "Pearl Street");
            Career.WentDown(man, 30, CharacterStatus.Hospitalized, "two ribs and a wrist");
            Career.RankChanged(man, 60, Rank.Lieutenant, "given a crew");

            if (man.Career.Count != 4)
            {
                failures.Add($"ACareerReadsForward: {man.Career.Count} lines on his " +
                             "file, not four.");
                return;
            }
            for (var i = 1; i < man.Career.Count; i++)
                if (man.Career[i].Day < man.Career[i - 1].Day)
                    failures.Add("ACareerReadsForward: the file does not read forward.");

            if (man.Career[0].Kind != CareerKind.Joined)
                failures.Add("ACareerReadsForward: the file does not open on the day he " +
                             "came on.");
            if (man.Career[0].Line.IndexOf("Rossi", StringComparison.Ordinal) < 0)
                failures.Add("ACareerReadsForward: the file does not say who brought " +
                             "him in.");
            if (man.Career[3].Kind != CareerKind.Rank)
                failures.Add("ACareerReadsForward: the promotion is not on his file.");
            if (man.Career[1].Where != "Pearl Street")
                failures.Add("ACareerReadsForward: the file lost the street it " +
                             "happened on.");
        }

        static void AFileNeverGrowsWithoutBound(List<string> failures)
        {
            var man = Man(1, "Veteran");
            Career.Joined(man, 1, "Rossi");

            // Five years of a busy man: a job's worth of incident every third day.
            for (var day = 3; day < 1800; day += 3)
                Happened(man, IncidentKind.Deviated, day);

            if (man.Career.Count > Career.Kept + 1)
                failures.Add($"AFileNeverGrowsWithoutBound: {man.Career.Count} lines " +
                             $"after five years, against a cap of {Career.Kept}.");
            if (man.Career.Count == 0)
                failures.Add("AFileNeverGrowsWithoutBound: the cull emptied the file.");

            for (var i = 1; i < man.Career.Count; i++)
                if (man.Career[i].Day < man.Career[i - 1].Day)
                    failures.Add("AFileNeverGrowsWithoutBound: the cull disturbed the " +
                                 "order.");
        }

        static void ARankChangeIsNeverCulled(List<string> failures)
        {
            var man = Man(1, "Made");
            Career.RankChanged(man, 5, Rank.Lieutenant, "given a crew");
            Career.RankChanged(man, 9, Rank.Hood, "his crew broken up");

            for (var day = 20; day < 2000; day += 2)
                Happened(man, IncidentKind.Deviated, day);

            var ranks = 0;
            for (var i = 0; i < man.Career.Count; i++)
                if (man.Career[i].Kind == CareerKind.Rank)
                    ranks++;

            if (ranks != 2)
                failures.Add($"ARankChangeIsNeverCulled: {ranks} rank lines survived " +
                             "five years, not both.");
        }

        static void TheCullKeepsWhatMattered(List<string> failures)
        {
            var man = Man(1, "Sifted");
            // One loud night, early, then a long dull career that overflows the file.
            Happened(man, IncidentKind.StoppedIt, 2);
            for (var day = 10; day < 400; day += 2)
                Happened(man, IncidentKind.Deviated, day);

            var keptTheNight = false;
            for (var i = 0; i < man.Career.Count; i++)
                if (man.Career[i].Weight == Notability.WeightOf(IncidentKind.StoppedIt))
                    keptTheNight = true;

            if (!keptTheNight)
                failures.Add("TheCullKeepsWhatMattered: the night he took a bullet for " +
                             "the Don was culled and a hundred untidy jobs were kept.");
        }

        static void EveryLineCameOffARealRecord(List<string> failures)
        {
            var man = Man(1, "Sourced");
            Career.Joined(man, 1, "Rossi");
            Career.Posted(man, 2, "Bruno");
            Career.Improved(man, 3, new Improvement(man.Id, man.FullName,
                CharacterAttribute.Combat, 6));
            Career.WentDown(man, 4, CharacterStatus.Jailed, "held at Rikers");
            Career.StruckOff(man, 5, CharacterStatus.Dead);
            Happened(man, IncidentKind.Froze, 6);

            for (var i = 0; i < man.Career.Count; i++)
            {
                var entry = man.Career[i];
                if (entry.Line.Length == 0)
                    failures.Add("EveryLineCameOffARealRecord: an entry carries no line.");
                if (entry.Weight <= 0)
                    failures.Add("EveryLineCameOffARealRecord: an entry counts for " +
                                 "nothing, so nothing could ever cull it fairly.");
                if (entry.Day <= 0)
                    failures.Add("EveryLineCameOffARealRecord: an entry has no day.");
            }

            // A half-step is not a story; a whole star is.
            var half = Man(2, "Half");
            Career.Improved(half, 3, new Improvement(half.Id, half.FullName,
                CharacterAttribute.Combat, 7));
            if (half.Career.Count != 0)
                failures.Add("EveryLineCameOffARealRecord: a half-step wrote a line.");
        }

        static void TheFileAndThePaperAgree(List<string> failures)
        {
            var man = Man(1, "Agreed");
            var incident = new Incident(man.Id, man.FullName, IncidentKind.Escalated, 12,
                "Pearl Street", 3,
                IncidentText.Line(IncidentKind.Escalated, man.FullName, "Pearl Street"));
            Career.FromIncident(man, incident);

            if (man.Career.Count != 1)
            {
                failures.Add("TheFileAndThePaperAgree: the incident did not reach the " +
                             "file.");
                return;
            }
            if (man.Career[0].Line != incident.Line)
                failures.Add("TheFileAndThePaperAgree: the file re-worded the paper's " +
                             "own sentence.");

            // A rank change is told once, by the entry that may never be culled - the
            // paper's line about it must not land on the file underneath.
            var made = Man(2, "Made");
            Career.RankChanged(made, 20, Rank.Lieutenant, "given a crew");
            Happened(made, IncidentKind.Promoted, 20);
            if (made.Career.Count != 1)
                failures.Add($"TheFileAndThePaperAgree: a promotion wrote " +
                             $"{made.Career.Count} lines, not one.");
        }

        static void NothingHereWritesOnAnybody(List<string> failures)
        {
            var man = Man(1, "Untouched");
            man.SetHalfSteps(CharacterAttribute.Leadership, 7);
            Happened(man, IncidentKind.StoppedIt, 10);

            var loyalty = man.Loyalty;
            var leadership = man.GetHalfSteps(CharacterAttribute.Leadership);
            var status = man.Status;
            var lines = man.Career.Count;

            for (var day = 10; day < 90; day++)
            {
                Notability.Of(man, day);
                Notability.Marked(man, day);
                Notability.Fresh(man, day);
            }

            var roster = new Roster();
            roster.Members.Add(man);
            new NotabilityBoard().Rebuild(roster, 40);

            if (man.Loyalty != loyalty || man.Status != status ||
                man.GetHalfSteps(CharacterAttribute.Leadership) != leadership ||
                man.Career.Count != lines)
                failures.Add("NothingHereWritesOnAnybody: reading the score changed the " +
                             "man.");
        }
    }
}
