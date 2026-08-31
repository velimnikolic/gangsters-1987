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
            TheCurveSteepensTowardTheCeiling(failures);
            TheClimbSlowsAndThenStops(failures);
            NothingIsBankedPastTheCeiling(failures);

            return failures;
        }

        /// <summary>A man with one ceiling and nothing else on him.</summary>
        static Character Capped(Roster roster, string name, int potentialValue,
            CharacterAttribute skill = CharacterAttribute.Combat)
        {
            var man = new Character { Id = roster.NextCharacterId(), FirstName = name };
            for (var s = 0; s < AttributeScale.Count; s++)
                man.SetPotential((CharacterAttribute)s, potentialValue);
            for (var s = 0; s < AttributeScale.Count; s++)
                man.SetHalfSteps((CharacterAttribute)s, AttributeScale.MinHalfSteps);
            roster.Members.Add(man);
            return man;
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

        // ----------------------------------------------------------------- the curve

        static void TheCurveSteepensTowardTheCeiling(List<string> failures)
        {
            foreach (var value in new[] { 50, 70, 95 })
            {
                var ceiling = AttributeScale.HalfStepsFor(value);
                var climb = 0;
                for (var step = AttributeScale.MinHalfSteps + 1; step < ceiling; step++)
                    climb += Practice.CostOf(step, ceiling);
                var last = Practice.CostOf(ceiling, ceiling);

                if (last <= climb)
                    failures.Add($"TheCurveSteepens: at ceiling {value} the last " +
                                 $"half-step costs {last} against {climb} for the whole " +
                                 "climb before it - the last one is meant to be a career.");
                if (Practice.CostOf(ceiling + 1, ceiling) != 0)
                    failures.Add($"TheCurveSteepens: ceiling {value} still sells a step " +
                                 "above itself.");

                // Every step costs more than the one before it, all the way up.
                for (var step = AttributeScale.MinHalfSteps + 2; step <= ceiling; step++)
                    if (Practice.CostOf(step, ceiling) <=
                        Practice.CostOf(step - 1, ceiling))
                        failures.Add($"TheCurveSteepens: at ceiling {value}, half-step " +
                                     $"{step} is not dearer than {step - 1}.");
            }

            // The spec's bands, on a man who could reach five stars: 1 to 2.5 stars is
            // a season's work, 2.5 to 3.5 several times that.
            var early = 0;
            for (var step = 3; step <= 5; step++)
                early += Practice.CostOf(step, 10);
            var middle = 0;
            for (var step = 6; step <= 7; step++)
                middle += Practice.CostOf(step, 10);
            if (middle < early * 2)
                failures.Add($"TheCurveSteepens: the 50-75 band cost {middle} against " +
                             $"{early} for 20-50; it is meant to be several times as dear.");
        }

        static void TheClimbSlowsAndThenStops(List<string> failures)
        {
            // Twenty points a day, every day, for four scripted years. A man capped at
            // two and a half stars must stop there and stay stopped; a man who could
            // reach five must still be climbing when the other has been finished for
            // years.
            const int daily = 20;
            const int days = 1_460;

            var roster = new Roster();
            var short_ = Capped(roster, "Short", 50);
            var tall = Capped(roster, "Tall", 95);

            var stoppedOn = -1;
            var rises = new List<Improvement>();
            for (var day = 1; day <= days; day++)
            {
                short_.AddPractice(CharacterAttribute.Combat, daily);
                tall.AddPractice(CharacterAttribute.Combat, daily);
                rises.Clear();
                Practice.Convert(roster, rises);

                for (var i = 0; i < rises.Count; i++)
                    if (rises[i].CharacterId == short_.Id)
                        stoppedOn = day;
            }

            if (short_.GetHalfSteps(CharacterAttribute.Combat) != 5)
                failures.Add("TheClimbSlowsAndThenStops: the 50-ceiling man finished on " +
                             $"{short_.GetHalfSteps(CharacterAttribute.Combat)} half-steps, " +
                             "not 5.");
            if (tall.GetHalfSteps(CharacterAttribute.Combat) <= 5)
                failures.Add("TheClimbSlowsAndThenStops: the 95-ceiling man is no better " +
                             "off than the 50-ceiling one after four years.");
            if (stoppedOn < 0 || stoppedOn > days / 4)
                failures.Add($"TheClimbSlowsAndThenStops: the short man's last rise was " +
                             $"on day {stoppedOn}; a low ceiling is meant to be reached " +
                             "early and then held for the rest of his life.");
        }

        static void NothingIsBankedPastTheCeiling(List<string> failures)
        {
            var roster = new Roster();
            var man = Capped(roster, "Done", 50);

            var rises = new List<Improvement>();
            man.AddPractice(CharacterAttribute.Combat, 100_000);
            Practice.Convert(roster, rises);

            if (man.GetHalfSteps(CharacterAttribute.Combat) != 5)
                failures.Add("NothingIsBankedPastTheCeiling: a big enough bank bought " +
                             "its way past the ceiling.");
            if (Practice.NextCost(man, CharacterAttribute.Combat) != 0)
                failures.Add("NothingIsBankedPastTheCeiling: a finished man still has " +
                             "a price on his next star.");
            if (man.GetPractice(CharacterAttribute.Combat) != 0)
                failures.Add($"NothingIsBankedPastTheCeiling: he is still carrying " +
                             $"{man.GetPractice(CharacterAttribute.Combat)} points he " +
                             "can never spend.");
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
