using System;
using System.Collections.Generic;
using LivingCity.Outfit;
using LivingCity.Personnel;

namespace LivingCity.Tests
{
    /// <summary>
    /// EPIC 13's contract: what a man is LIKE, as opposed to what he is good at.
    /// Rolled once, never practised, never shown as a number, and never moved by
    /// anything that cannot say why.
    ///
    /// Pure C#, no UnityEngine, failures returned as data.
    /// </summary>
    public static class PersonalityTests
    {
        static readonly (string Name, Action<List<string>> Check)[] Contracts =
        {
            ("SameSeedSameCharacters", SameSeedSameCharacters),
            ("NeighboursAreNotTheSameMan", NeighboursAreNotTheSameMan),
            ("TheSpreadIsWideAndHasItsExtremes", TheSpreadIsWideAndHasItsExtremes),
            ("LoyaltyIsNotRolledTwice", LoyaltyIsNotRolledTwice),
            ("EveryTraitHasAWordAtEveryValue", EveryTraitHasAWordAtEveryValue),
            ("NothingMovesWithoutAReason", NothingMovesWithoutAReason),
            ("TheScaleHoldsAtBothEnds", TheScaleHoldsAtBothEnds),
            ("TheCowardFreezesAndSometimesRuns", TheCowardFreezesAndSometimesRuns),
            ("TheSteadyManNeverBreaks", TheSteadyManNeverBreaks),
            ("TheHotHeadTurnsACollectionIntoAShooting", TheHotHeadTurnsACollectionIntoAShooting),
            ("TheUndisciplinedTakeTheirOwnChoices", TheUndisciplinedTakeTheirOwnChoices),
            ("OneNightOneStoryAboutAMan", OneNightOneStoryAboutAMan),
            ("EveryIncidentIsReadyForThePaper", EveryIncidentIsReadyForThePaper),
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

        // ------------------------------------------------------------- the seeding

        static void SameSeedSameCharacters(List<string> failures)
        {
            var first = RosterSeeder.Generate(1987);
            var second = RosterSeeder.Generate(1987);

            for (var i = 0; i < first.Members.Count; i++)
                for (var t = 0; t < Personality.All.Length; t++)
                {
                    var trait = Personality.All[t];
                    var a = Personality.Get(first.Members[i], trait);
                    var b = Personality.Get(second.Members[i], trait);
                    if (a != b)
                        failures.Add($"SameSeedSameCharacters: {first.Members[i].FullName}" +
                                     $"'s {trait} was {a} then {b}.");
                }

            var other = RosterSeeder.Generate(1988);
            var identical = true;
            for (var i = 0; i < first.Members.Count && identical; i++)
                for (var t = 0; t < Personality.All.Length; t++)
                    if (Personality.Get(first.Members[i], Personality.All[t]) !=
                        Personality.Get(other.Members[i], Personality.All[t]))
                    {
                        identical = false;
                        break;
                    }
            if (identical)
                failures.Add("SameSeedSameCharacters: seed 1988 dealt seed 1987's men.");
        }

        static void NeighboursAreNotTheSameMan(List<string> failures)
        {
            var roster = RosterSeeder.GenerateLarge(1987, 60);
            for (var i = 1; i < roster.Members.Count; i++)
            {
                var previous = roster.Members[i - 1];
                var member = roster.Members[i];
                if (previous.Rank == Rank.Boss || member.Rank == Rank.Boss)
                    continue;

                var same = true;
                for (var t = 0; t < Personality.All.Length && same; t++)
                {
                    var trait = Personality.All[t];
                    if (trait == PersonalityTrait.Loyalty)
                        continue;
                    if (Personality.Get(previous, trait) != Personality.Get(member, trait))
                        same = false;
                }
                if (same)
                    failures.Add($"NeighboursAreNotTheSameMan: {previous.FullName} and " +
                                 $"{member.FullName} came out of the same stream.");
            }
        }

        static void TheSpreadIsWideAndHasItsExtremes(List<string> failures)
        {
            var roster = RosterSeeder.GenerateLarge(1987, 1_000);
            var counted = 0;
            var outsideTheBand = 0;

            for (var i = 0; i < roster.Members.Count; i++)
            {
                var member = roster.Members[i];
                if (member.Rank == Rank.Boss)
                    continue;

                for (var t = 0; t < Personality.All.Length; t++)
                {
                    var trait = Personality.All[t];
                    if (trait == PersonalityTrait.Loyalty)
                        continue;

                    var value = Personality.Get(member, trait);
                    if (value < 0 || value > 100)
                        failures.Add($"TheSpreadIsWide: {member.FullName}'s {trait} is " +
                                     $"{value}, off the scale entirely.");
                    counted++;
                    if (value < Personality.MinRoll || value > Personality.MaxRoll)
                        outsideTheBand++;
                }
            }

            // Personality is where men differ most, and an outfit with nobody in it who
            // is either a coward or a maniac is an outfit with no stories in it.
            var percent = counted > 0 ? outsideTheBand * 100 / counted : 0;
            if (percent < 2 || percent > 12)
                failures.Add($"TheSpreadIsWide: {percent}% of traits landed outside " +
                             $"{Personality.MinRoll}-{Personality.MaxRoll}, and the roll " +
                             $"asks for about {Personality.ExtremePercent}%.");
        }

        static void LoyaltyIsNotRolledTwice(List<string> failures)
        {
            // The seeder dealt Loyalty in its own narrower band before this system
            // existed, and re-rolling it here would re-deal every campaign's six.
            var roster = RosterSeeder.Generate(1987);
            for (var i = 0; i < roster.Members.Count; i++)
            {
                var member = roster.Members[i];
                if (member.Rank == Rank.Boss)
                    continue;
                if (member.Loyalty < 35 || member.Loyalty > 85)
                    failures.Add($"LoyaltyIsNotRolledTwice: {member.FullName} is on " +
                                 $"{member.Loyalty}, outside the seeder's own band - " +
                                 "something re-rolled him.");
            }
        }

        // -------------------------------------------------------------- the reading

        static void EveryTraitHasAWordAtEveryValue(List<string> failures)
        {
            foreach (PersonalityTrait trait in Enum.GetValues(typeof(PersonalityTrait)))
            {
                if (Personality.Label(trait).Length == 0)
                    failures.Add($"EveryTraitHasAWord: {trait} has no label.");

                var seen = new HashSet<string>();
                for (var value = 0; value <= 100; value++)
                {
                    var word = Personality.Band(trait, value);
                    if (word.Length == 0)
                        failures.Add($"EveryTraitHasAWord: {trait} at {value} has no word.");
                    seen.Add(word);
                }
                if (seen.Count < 5)
                    failures.Add($"EveryTraitHasAWord: {trait} only ever says " +
                                 $"{seen.Count} different things across the whole scale.");
            }
        }

        // -------------------------------------------------------------- the one door

        static void NothingMovesWithoutAReason(List<string> failures)
        {
            var man = new Character { Id = 7, FirstName = "Enzo", Surname = "Bardi" };
            var log = new List<PersonalityChange>();

            var before = man.Temper;
            var change = RosterOps.NudgePersonality(man, PersonalityTrait.Temper, 12,
                "watched his brother go down on the corner", log);

            if (man.Temper != before + 12)
                failures.Add($"NothingMovesWithoutAReason: Temper went {before} -> " +
                             $"{man.Temper} on a nudge of 12.");
            if (log.Count != 1 || log[0].Reason.Length == 0 ||
                log[0].CharacterId != man.Id || log[0].Delta != 12)
                failures.Add("NothingMovesWithoutAReason: the movement was not recorded " +
                             "with its reason.");
            if (change.To != man.Temper)
                failures.Add("NothingMovesWithoutAReason: the returned record disagrees " +
                             "with the man.");

            // A nudge that moves nothing records nothing: the feed prints what
            // happened, not what was attempted.
            RosterOps.NudgePersonality(man, PersonalityTrait.Temper, 0, "nothing at all",
                log);
            if (log.Count != 1)
                failures.Add("NothingMovesWithoutAReason: a nudge of zero got a line in " +
                             "the paper.");

            // Loyalty moves through the same door and is not a second field.
            var loyaltyBefore = man.Loyalty;
            RosterOps.NudgePersonality(man, PersonalityTrait.Loyalty, -5, "paid late", log);
            if (man.Loyalty != loyaltyBefore - 5)
                failures.Add("NothingMovesWithoutAReason: the nudge did not reach the " +
                             "one Loyalty field.");
        }

        // ----------------------------------------------------------- the checks fire

        /// <summary>A crew of men all built the same way, and a job to send them on.
        /// Returns the incidents of running that job a scripted number of times, on a
        /// fresh crew each time so a desertion cannot empty the fixture.</summary>
        static List<Incident> RunJobs(OrderType type, PersonalityTrait trait, int value,
            int times, out int deserted)
        {
            var incidents = new List<Incident>();
            deserted = 0;

            for (var run = 0; run < times; run++)
            {
                var roster = new Roster();
                var lieutenant = new Character
                {
                    Id = roster.NextCharacterId(), FirstName = "Lt", Surname = "Bruno",
                    Rank = Rank.Lieutenant,
                };
                roster.Members.Add(lieutenant);
                var crew = new Crew
                {
                    Id = roster.NextCrewId(), LieutenantId = lieutenant.Id,
                };
                var hood = new Character
                {
                    Id = roster.NextCharacterId(), FirstName = "Man", Surname = "Ferri",
                };
                roster.Members.Add(hood);
                crew.HoodIds.Add(hood.Id);
                roster.Crews.Add(crew);

                // Everybody steady except the one trait under test, so only that trait
                // can fire and the count means what it says.
                foreach (var man in new[] { lieutenant, hood })
                {
                    Personality.Set(man, PersonalityTrait.Courage, 90);
                    Personality.Set(man, PersonalityTrait.Temper, 10);
                    Personality.Set(man, PersonalityTrait.Discipline, 90);
                    Personality.Set(man, trait, value);
                }

                var job = new Job
                {
                    Id = run, CrewId = crew.Id, Type = type, Men = 2,
                    IssuedDay = 10 + run, TargetLabel = "Pearl Street",
                };
                var rng = new System.Random(1987 + run);
                OrderResolution.Resolve(OrderTable.SpecOf(type), job, roster, crew, rng,
                    null, incidents);

                for (var i = 0; i < roster.Members.Count; i++)
                    if (roster.Members[i].Status == CharacterStatus.Deserted)
                        deserted++;
            }

            return incidents;
        }

        static void TheCowardFreezesAndSometimesRuns(List<string> failures)
        {
            var incidents = RunJobs(OrderType.Raid, PersonalityTrait.Courage, 0, 60,
                out var deserted);

            var froze = 0;
            var fled = 0;
            for (var i = 0; i < incidents.Count; i++)
            {
                if (incidents[i].Kind == IncidentKind.Froze) froze++;
                if (incidents[i].Kind == IncidentKind.Fled) fled++;
            }

            if (froze == 0)
                failures.Add("TheCowardFreezesAndSometimesRuns: sixty raids and a man " +
                             "with no nerve at all never once went to pieces.");
            if (fled == 0)
                failures.Add("TheCowardFreezesAndSometimesRuns: nobody ever ran.");
            if (deserted != fled)
                failures.Add($"TheCowardFreezesAndSometimesRuns: {fled} men ran and " +
                             $"{deserted} were struck off - a man who runs is off the " +
                             "books.");
        }

        static void TheSteadyManNeverBreaks(List<string> failures)
        {
            var incidents = RunJobs(OrderType.Raid, PersonalityTrait.Courage, 90, 60,
                out var deserted);
            for (var i = 0; i < incidents.Count; i++)
                if (incidents[i].Kind == IncidentKind.Froze ||
                    incidents[i].Kind == IncidentKind.Fled)
                    failures.Add("TheSteadyManNeverBreaks: a man well over the floor was " +
                                 "still rolled for, and broke.");
            if (deserted != 0)
                failures.Add("TheSteadyManNeverBreaks: a steady man deserted.");
        }

        static void TheHotHeadTurnsACollectionIntoAShooting(List<string> failures)
        {
            var incidents = RunJobs(OrderType.CollectProtection, PersonalityTrait.Temper,
                100, 60, out _);

            var escalated = 0;
            for (var i = 0; i < incidents.Count; i++)
                if (incidents[i].Kind == IncidentKind.Escalated)
                {
                    escalated++;
                    if (incidents[i].Heat <= 0)
                        failures.Add("TheHotHead: a shooting nobody ordered drew no " +
                                     "police attention at all.");
                }
            if (escalated == 0)
                failures.Add("TheHotHead: sixty collections by a man with no temper " +
                             "control and not one of them turned.");

            // And the placid man never escalates.
            var calm = RunJobs(OrderType.CollectProtection, PersonalityTrait.Temper, 10,
                60, out _);
            for (var i = 0; i < calm.Count; i++)
                if (calm[i].Kind == IncidentKind.Escalated)
                    failures.Add("TheHotHead: a placid man started a gunfight.");
        }

        static void TheUndisciplinedTakeTheirOwnChoices(List<string> failures)
        {
            var incidents = RunJobs(OrderType.CollectProtection,
                PersonalityTrait.Discipline, 0, 60, out _);

            var deviated = 0;
            for (var i = 0; i < incidents.Count; i++)
                if (incidents[i].Kind == IncidentKind.Deviated)
                    deviated++;
            if (deviated == 0)
                failures.Add("TheUndisciplinedTakeTheirOwnChoices: a man with no " +
                             "discipline did sixty jobs exactly as written.");

            var exact = RunJobs(OrderType.CollectProtection, PersonalityTrait.Discipline,
                90, 60, out _);
            for (var i = 0; i < exact.Count; i++)
                if (exact[i].Kind == IncidentKind.Deviated)
                    failures.Add("TheUndisciplinedTakeTheirOwnChoices: an exact man " +
                                 "improvised.");
        }

        static void OneNightOneStoryAboutAMan(List<string> failures)
        {
            // A man who went to pieces is not then also written up for improvising.
            var incidents = RunJobs(OrderType.Raid, PersonalityTrait.Courage, 0, 60,
                out _);
            var seen = new Dictionary<string, int>();
            for (var i = 0; i < incidents.Count; i++)
            {
                var key = incidents[i].Day + ":" + incidents[i].CharacterId;
                seen.TryGetValue(key, out var count);
                seen[key] = count + 1;
                if (count + 1 > 1)
                    failures.Add($"OneNightOneStoryAboutAMan: {incidents[i].Name} got " +
                                 $"{count + 1} write-ups out of one job on day " +
                                 $"{incidents[i].Day}.");
            }
        }

        static void EveryIncidentIsReadyForThePaper(List<string> failures)
        {
            foreach (IncidentKind kind in Enum.GetValues(typeof(IncidentKind)))
            {
                var line = PersonalityChecks.Line(kind, "Rocco Vale", "Pearl Street");
                if (line.Length == 0)
                    failures.Add($"EveryIncidentIsReadyForThePaper: {kind} has no line.");
                if (!line.Contains("Rocco Vale"))
                    failures.Add($"EveryIncidentIsReadyForThePaper: {kind}'s line does " +
                                 "not name the man it is about.");
                if (!line.Contains("Pearl Street"))
                    failures.Add($"EveryIncidentIsReadyForThePaper: {kind}'s line does " +
                                 "not say where.");

                // And it still reads when nobody could say where.
                if (PersonalityChecks.Line(kind, "Rocco Vale", "").Length == 0)
                    failures.Add($"EveryIncidentIsReadyForThePaper: {kind} has no line " +
                                 "when the place is unknown.");
            }
        }

        static void TheScaleHoldsAtBothEnds(List<string> failures)
        {
            var man = new Character { Id = 1 };
            var log = new List<PersonalityChange>();

            RosterOps.NudgePersonality(man, PersonalityTrait.Courage, 500, "test", log);
            if (man.Courage != 100)
                failures.Add($"TheScaleHoldsAtBothEnds: Courage went to {man.Courage}.");

            RosterOps.NudgePersonality(man, PersonalityTrait.Courage, -500, "test", log);
            if (man.Courage != 0)
                failures.Add($"TheScaleHoldsAtBothEnds: Courage went to {man.Courage}.");

            // Already at the floor: nothing moves and nothing is printed.
            var lines = log.Count;
            RosterOps.NudgePersonality(man, PersonalityTrait.Courage, -10, "test", log);
            if (log.Count != lines)
                failures.Add("TheScaleHoldsAtBothEnds: a man already at zero got a line " +
                             "about getting more frightened.");
        }
    }
}
