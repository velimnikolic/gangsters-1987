using System;
using System.Collections.Generic;
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
