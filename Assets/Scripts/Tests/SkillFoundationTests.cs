using System;
using System.Collections.Generic;
using LivingCity.Personnel;

namespace LivingCity.Tests
{
    /// <summary>
    /// EPIC 11's contract: the eleven general skills, the hidden ceiling every man is
    /// dealt, the growth curve that prices its last half-step out of reach, and the
    /// aging that takes the field trades back off him.
    ///
    /// Same discipline as <see cref="PersonnelTests"/>: a plain static class, failures
    /// returned as data, no UnityEngine anywhere, so the whole thing runs in a bare
    /// .NET host as well as inside the editor. Failures name the man, the skill and
    /// the day - "expected true" costs an afternoon to chase.
    /// </summary>
    public static class SkillFoundationTests
    {
        public static List<string> Run()
        {
            var failures = new List<string>();

            SameSeedSameCeilings(failures);
            NeighboursDoNotShareARoll(failures);
            NobodyStartsAboveHisCeiling(failures);
            CeilingsSitInTheBandAndCentreOnFifty(failures);
            NoPageCanReachTheCeiling(failures);
            TheValueConventionRoundsUpAtTheHalf(failures);

            return failures;
        }

        // ------------------------------------------------------------- determinism

        static void SameSeedSameCeilings(List<string> failures)
        {
            var first = RosterSeeder.Generate(1987);
            var second = RosterSeeder.Generate(1987);

            for (var i = 0; i < first.Members.Count; i++)
            {
                var a = first.Members[i];
                var b = second.Members[i];
                if (a.Id != b.Id || a.FullName != b.FullName)
                {
                    failures.Add($"SameSeedSameCeilings: member {i} is a different man.");
                    return;
                }

                for (var s = 0; s < AttributeScale.Count; s++)
                {
                    var skill = (CharacterAttribute)s;
                    if (a.PotentialValue(skill) != b.PotentialValue(skill))
                        failures.Add($"SameSeedSameCeilings: {a.FullName}'s {skill} " +
                                     $"ceiling was {a.PotentialValue(skill)} then " +
                                     $"{b.PotentialValue(skill)}.");
                }
            }

            // A constant seed inside the roll would pass everything above and still be
            // a bug, so a different campaign must deal different ceilings.
            var other = RosterSeeder.Generate(1988);
            var identical = true;
            for (var i = 0; i < first.Members.Count && identical; i++)
                for (var s = 0; s < AttributeScale.Count; s++)
                    if (first.Members[i].PotentialValue((CharacterAttribute)s) !=
                        other.Members[i].PotentialValue((CharacterAttribute)s))
                    {
                        identical = false;
                        break;
                    }
            if (identical)
                failures.Add("SameSeedSameCeilings: seed 1988 dealt seed 1987's ceilings.");
        }

        static void NeighboursDoNotShareARoll(List<string> failures)
        {
            // Two men in a row sharing a stream shows up as the same eleven numbers.
            var roster = RosterSeeder.GenerateLarge(1987, 60);
            for (var i = 1; i < roster.Members.Count; i++)
            {
                var previous = roster.Members[i - 1];
                var member = roster.Members[i];
                if (previous.Rank == Rank.Boss || member.Rank == Rank.Boss)
                    continue;

                var same = true;
                for (var s = 0; s < AttributeScale.Count && same; s++)
                    if (previous.PotentialValue((CharacterAttribute)s) !=
                        member.PotentialValue((CharacterAttribute)s))
                        same = false;
                if (same)
                    failures.Add($"NeighboursDoNotShareARoll: {previous.FullName} and " +
                                 $"{member.FullName} were dealt the same eleven ceilings.");
            }
        }

        // ------------------------------------------------------------- the ceiling

        static void NobodyStartsAboveHisCeiling(List<string> failures)
        {
            var roster = RosterSeeder.GenerateLarge(1987, 1_000);
            for (var i = 0; i < roster.Members.Count; i++)
            {
                var member = roster.Members[i];
                for (var s = 0; s < AttributeScale.Count; s++)
                {
                    var skill = (CharacterAttribute)s;
                    if (member.GetHalfSteps(skill) > member.PotentialHalfSteps(skill))
                        failures.Add($"NobodyStartsAboveHisCeiling: {member.FullName} " +
                                     $"was dealt {skill} at {member.GetHalfSteps(skill)} " +
                                     $"against a ceiling of {member.PotentialHalfSteps(skill)}.");
                }
            }
        }

        static void CeilingsSitInTheBandAndCentreOnFifty(List<string> failures)
        {
            var roster = RosterSeeder.GenerateLarge(1987, 1_000);
            var inBand = 0;
            var counted = 0;

            for (var i = 0; i < roster.Members.Count; i++)
            {
                var member = roster.Members[i];
                // The Don's numbers are scripted, ceiling and all - he is not a roll.
                if (member.Rank == Rank.Boss)
                    continue;

                for (var s = 0; s < AttributeScale.Count; s++)
                {
                    var value = member.PotentialValue((CharacterAttribute)s);
                    if (value < Potential.MinRoll || value > Potential.MaxRoll)
                        failures.Add($"CeilingsSitInTheBand: {member.FullName} rolled " +
                                     $"{(CharacterAttribute)s} at {value}, outside " +
                                     $"{Potential.MinRoll}-{Potential.MaxRoll}.");
                    counted++;
                    if (value >= 40 && value <= 60)
                        inBand++;
                }
            }

            // The spec's shape: most hoods, most skills, land between 40 and 60.
            if (counted == 0 || inBand * 2 <= counted)
                failures.Add($"CeilingsCentreOnFifty: only {inBand} of {counted} " +
                             "ceilings landed in 40-60; the roll has lost its shape.");
        }

        static void NoPageCanReachTheCeiling(List<string> failures)
        {
            // The rule is that no page can PRINT a ceiling, and the way that is kept
            // true is that no field or property carries one out of the character: a
            // painter iterates properties and rows, never methods it has to know to
            // call by name.
            foreach (var field in typeof(Character).GetFields())
                if (field.Name.IndexOf("potential", StringComparison.OrdinalIgnoreCase) >= 0)
                    failures.Add($"NoPageCanReachTheCeiling: Character.{field.Name} is " +
                                 "a public field.");
            foreach (var property in typeof(Character).GetProperties())
                if (property.Name.IndexOf("potential", StringComparison.OrdinalIgnoreCase) >= 0)
                    failures.Add($"NoPageCanReachTheCeiling: Character.{property.Name} " +
                                 "is a public property.");

            // The two rows the ledger actually paints from carry ids and half-steps,
            // and must never grow a ceiling channel.
            foreach (var field in typeof(LedgerRow).GetFields())
                if (field.Name.IndexOf("potential", StringComparison.OrdinalIgnoreCase) >= 0)
                    failures.Add($"NoPageCanReachTheCeiling: LedgerRow.{field.Name}.");
            foreach (var field in typeof(Improvement).GetFields())
                if (field.Name.IndexOf("potential", StringComparison.OrdinalIgnoreCase) >= 0)
                    failures.Add($"NoPageCanReachTheCeiling: Improvement.{field.Name}.");
        }

        static void TheValueConventionRoundsUpAtTheHalf(List<string> failures)
        {
            // The whole spec is written on the 0-100 scale and the roster is stored in
            // half-steps; the two meet here, and "at least 55" has to mean six.
            if (AttributeScale.ValueOf(6) != 60 || AttributeScale.ValueOf(10) != 100)
                failures.Add("TheValueConvention: half-steps stopped reading x10.");
            if (AttributeScale.HalfStepsFor(55) != 6 ||
                AttributeScale.HalfStepsFor(54) != 5 ||
                AttributeScale.HalfStepsFor(60) != 6)
                failures.Add("TheValueConvention: the midpoint stopped rounding up.");
            if (AttributeScale.HalfStepsFor(0) != AttributeScale.MinHalfSteps ||
                AttributeScale.HalfStepsFor(1_000) != AttributeScale.MaxHalfSteps)
                failures.Add("TheValueConvention: the conversion stopped clamping.");
        }
    }
}
