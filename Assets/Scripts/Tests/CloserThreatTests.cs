using System;
using System.Collections.Generic;
using LivingCity.Personnel;
using RoadDemo;

namespace LivingCity.Tests
{
    /// <summary>
    /// EPIC 33's contract, offline: the margin a man needs before he turns onto a
    /// closer enemy, the dwell that advantage has to hold for, the hysteresis that
    /// keeps two nearly level men from flickering his aim, and the cone a missed round
    /// leaves the barrel in.
    ///
    /// Everything measured here is a PURE function of half-steps and metres
    /// (<see cref="CrewSkill"/>), which is the whole reason the policy was put there:
    /// no scene, no walker, no transform, no wall, so a run of this proves the rule
    /// with the editor idle and the Play verdict is left to judge only what the eye
    /// can judge - that the aim line switches once, that the man in cover stays in it,
    /// and that a one-star rifleman visibly misses wider than a five-star.
    ///
    /// Same discipline as the roster suites: failures come back as data and name the
    /// numbers, because "expected true" costs an afternoon to chase.
    /// </summary>
    public static class CloserThreatTests
    {
        static readonly (string Name, Action<List<string>> Check)[] Contracts =
        {
            ("TheTableIsWhatTheUserWroteDown", TheTableIsWhatTheUserWroteDown),
            ("TheHalfStarsSitBetweenTheStars", TheHalfStarsSitBetweenTheStars),
            ("TheBetterShotNoticesSoonerAndSmaller", TheBetterShotNoticesSoonerAndSmaller),
            ("NobodyIsReadOffTheStarScale", NobodyIsReadOffTheStarScale),
            ("FifteenAgainstThirteenIsNotEnough", FifteenAgainstThirteenIsNotEnough),
            ("TheDwellHasToBeServed", TheDwellHasToBeServed),
            ("SkillOrderingIsDeterministic", SkillOrderingIsDeterministic),
            ("TheMarginCannotBeBeatenBothWays", TheMarginCannotBeBeatenBothWays),
            ("ADipInsideTheMarginNeverTakesTheAim", ADipInsideTheMarginNeverTakesTheAim),
            ("TwoLevelEnemiesDoNotFlickerTheAim", TwoLevelEnemiesDoNotFlickerTheAim),
            ("TheConeComesOffTheGunsOwnAccuracy", TheConeComesOffTheGunsOwnAccuracy),
            ("TheConeIsWideForBadHandsAndTightForGood", TheConeIsWideForBadHandsAndTightForGood),
            ("AnAngleWidensWithRange", AnAngleWidensWithRange),
            ("ARiflemanAtTwentyFiveMetresReadsOnScreen", ARiflemanAtTwentyFiveMetresReadsOnScreen),
            ("NoRoundLeavesTheCone", NoRoundLeavesTheCone),
            ("TheConeIsFilledToItsEdge", TheConeIsFilledToItsEdge),
            ("TheSpreadIsNotPiledOnTheAimLine", TheSpreadIsNotPiledOnTheAimLine),
            ("APitchedRoundStaysUnderTheShoulderLine", APitchedRoundStaysUnderTheShoulderLine),
        };

        public static List<string> Run()
        {
            var failures = new List<string>();
            for (var i = 0; i < Contracts.Length; i++)
                Contracts[i].Check(failures);
            return failures;
        }

        /// <summary>What this build actually ran, in order. A stale assembly answers
        /// ALL PASS just as cheerfully as a fresh one; this list is what tells them
        /// apart at a glance.</summary>
        public static string[] ContractNames()
        {
            var names = new string[Contracts.Length];
            for (var i = 0; i < Contracts.Length; i++)
                names[i] = Contracts[i].Name;
            return names;
        }

        const int OneStar = 2;
        const int ThreeStars = 6;
        const int FiveStars = 10;

        // ------------------------------------------------------------------- the table

        static void TheTableIsWhatTheUserWroteDown(List<string> failures)
        {
            Same(failures, "margin at one star", CrewSkill.ThreatMargin(OneStar), 4.0f);
            Same(failures, "margin at three stars", CrewSkill.ThreatMargin(ThreeStars), 3.0f);
            Same(failures, "margin at five stars", CrewSkill.ThreatMargin(FiveStars), 2.0f);
            Same(failures, "dwell at one star", CrewSkill.ThreatDwell(OneStar), 0.90f);
            Same(failures, "dwell at three stars", CrewSkill.ThreatDwell(ThreeStars), 0.55f);
            Same(failures, "dwell at five stars", CrewSkill.ThreatDwell(FiveStars), 0.25f);
            Same(failures, "cone at one star", CrewSkill.MissCone(OneStar), 1.25f);
            Same(failures, "cone at three stars", CrewSkill.MissCone(ThreeStars), 1.00f);
            Same(failures, "cone at five stars", CrewSkill.MissCone(FiveStars), 0.75f);
        }

        static void TheHalfStarsSitBetweenTheStars(List<string> failures)
        {
            // two stars is read off the rows either side of it and nowhere else
            Between(failures, "margin at two stars", CrewSkill.ThreatMargin(4), 3.0f, 4.0f);
            Between(failures, "dwell at two stars", CrewSkill.ThreatDwell(4), 0.55f, 0.90f);
            Between(failures, "cone at four stars", CrewSkill.MissCone(8), 0.75f, 1.00f);
        }

        static void TheBetterShotNoticesSoonerAndSmaller(List<string> failures)
        {
            for (var hs = AttributeScale.MinHalfSteps; hs < AttributeScale.MaxHalfSteps; hs++)
            {
                if (CrewSkill.ThreatMargin(hs + 1) >= CrewSkill.ThreatMargin(hs))
                    failures.Add($"Closer threat: the margin at {hs + 1} half-steps " +
                                 $"({CrewSkill.ThreatMargin(hs + 1):F3} m) is not smaller " +
                                 $"than at {hs} ({CrewSkill.ThreatMargin(hs):F3} m).");
                if (CrewSkill.ThreatDwell(hs + 1) >= CrewSkill.ThreatDwell(hs))
                    failures.Add($"Closer threat: the dwell at {hs + 1} half-steps " +
                                 $"({CrewSkill.ThreatDwell(hs + 1):F3} s) is not shorter " +
                                 $"than at {hs} ({CrewSkill.ThreatDwell(hs):F3} s).");
                if (CrewSkill.MissCone(hs + 1) >= CrewSkill.MissCone(hs))
                    failures.Add($"Closer threat: the miss cone at {hs + 1} half-steps " +
                                 $"({CrewSkill.MissCone(hs + 1):F3}x) is not tighter " +
                                 $"than at {hs} ({CrewSkill.MissCone(hs):F3}x).");
            }
        }

        static void NobodyIsReadOffTheStarScale(List<string> failures)
        {
            // a man with no sheet at all, and one somebody typed 99 into
            Same(failures, "margin below the scale", CrewSkill.ThreatMargin(0), 4.0f);
            Same(failures, "margin above the scale", CrewSkill.ThreatMargin(99), 2.0f);
            Same(failures, "dwell below the scale", CrewSkill.ThreatDwell(-5), 0.90f);
            Same(failures, "dwell above the scale", CrewSkill.ThreatDwell(40), 0.25f);
            // and the police, who have no Combat sheet and fight at three stars by
            // default (SpawnAt leaves six half-steps): the law's own numbers
            Same(failures, "the law's margin", CrewSkill.ThreatMargin(6), 3.0f);
            Same(failures, "the law's dwell", CrewSkill.ThreatDwell(6), 0.55f);
        }

        // -------------------------------------------------------------- the user's rule

        static void FifteenAgainstThirteenIsNotEnough(List<string> failures)
        {
            // The rule, in the user's own numbers: A at 15 m, B at 13 m is a two-metre
            // advantage and a three-star shot does not turn; B at 12 m is three metres
            // and he does, once it has held.
            float served = CrewSkill.ThreatDwell(ThreeStars) + 0.01f;
            if (CrewSkill.ShouldSwitch(15f, 13f, ThreeStars, served))
                failures.Add("Closer threat: a three-star shot turned off a mark at 15 m " +
                             "for a man at 13 m, which is only a 2 m advantage against a " +
                             "3 m margin.");
            if (!CrewSkill.ShouldSwitch(15f, 12f, ThreeStars, served))
                failures.Add("Closer threat: a three-star shot did NOT turn off a mark at " +
                             "15 m for a man at 12 m, which is the whole 3 m margin held " +
                             $"for {served:F2} s.");
            // exactly the margin counts: the rule is "at least", not "more than"
            if (!CrewSkill.ShouldSwitch(15f, 15f - CrewSkill.ThreatMargin(ThreeStars),
                                        ThreeStars, served))
                failures.Add("Closer threat: an advantage of exactly the margin was refused.");
        }

        static void TheDwellHasToBeServed(List<string> failures)
        {
            float dwell = CrewSkill.ThreatDwell(ThreeStars);
            if (CrewSkill.ShouldSwitch(15f, 10f, ThreeStars, dwell - 0.01f))
                failures.Add($"Closer threat: a 5 m advantage took the aim after only " +
                             $"{dwell - 0.01f:F2} s against a {dwell:F2} s dwell.");
            if (!CrewSkill.ShouldSwitch(15f, 10f, ThreeStars, dwell))
                failures.Add($"Closer threat: a 5 m advantage held for the full {dwell:F2} s " +
                             "dwell was still refused.");
            if (CrewSkill.ShouldSwitch(15f, 10f, ThreeStars, 0f))
                failures.Add("Closer threat: the switch happened on the frame the " +
                             "advantage appeared - there is no dwell at all.");
        }

        static void SkillOrderingIsDeterministic(List<string> failures)
        {
            // ONE GEOMETRY, THREE MEN. The five-star turns, the three-star does not
            // (his margin is bigger), and the one-star does not either - every run,
            // because nothing here is rolled.
            const float mark = 15f, candidate = 12.5f;
            float served = 0.30f;
            if (!CrewSkill.ShouldSwitch(mark, candidate, FiveStars, served))
                failures.Add("Closer threat: a five-star shot did not take a 2.5 m " +
                             "advantage held for 0.30 s.");
            if (CrewSkill.ShouldSwitch(mark, candidate, ThreeStars, served))
                failures.Add("Closer threat: a three-star shot took a 2.5 m advantage " +
                             "against a 3 m margin.");
            if (CrewSkill.ShouldSwitch(mark, candidate, OneStar, served))
                failures.Add("Closer threat: a one-star shot took a 2.5 m advantage " +
                             "against a 4 m margin.");
            // and the same advantage, given time: the one-star gets there last
            float wide = mark - CrewSkill.ThreatMargin(OneStar);
            if (!CrewSkill.ShouldSwitch(mark, wide, OneStar, CrewSkill.ThreatDwell(OneStar)))
                failures.Add("Closer threat: a one-star shot refused an advantage that " +
                             "was his own whole margin, held for his own whole dwell.");
            if (CrewSkill.ShouldSwitch(mark, wide, OneStar, CrewSkill.ThreatDwell(FiveStars)))
                failures.Add("Closer threat: a one-star shot turned on a five-star's dwell.");
        }

        // ------------------------------------------------------------- the anti-flicker

        static void TheMarginCannotBeBeatenBothWays(List<string> failures)
        {
            // The margin is the hysteresis: if B beats A, A cannot also beat B. Walked
            // over the whole scale and a grid of distances rather than argued about.
            for (var hs = AttributeScale.MinHalfSteps; hs <= AttributeScale.MaxHalfSteps; hs++)
                for (var a = 1f; a <= 30f; a += 0.5f)
                    for (var b = 1f; b <= 30f; b += 0.5f)
                    {
                        if (!CrewSkill.ShouldSwitch(a, b, hs, 10f)) continue;
                        if (!CrewSkill.ShouldSwitch(b, a, hs, 10f)) continue;
                        failures.Add($"Closer threat: at {hs} half-steps, {a:F1} m and " +
                                     $"{b:F1} m beat one another - the aim would flicker.");
                        return;
                    }
        }

        static void ADipInsideTheMarginNeverTakesTheAim(List<string> failures)
        {
            // A candidate who crosses the margin for a fifth of a second at a time and
            // then falls back out of it: the dwell restarts every lapse, so however long
            // the fight runs, he never takes the aim. This is the held condition the
            // arena keeps for him (CrewWalker.WatchThreat / ForgetThreat), driven here
            // by hand at a fixed step so the answer is the same every run.
            const float step = 1f / 60f;
            float held = 0f;
            bool inside = true;
            float phase = 0f;
            for (var t = 0f; t < 20f; t += step)
            {
                phase += step;
                if (phase >= 0.2f) { phase = 0f; inside = !inside; }
                float candidate = inside ? 11.5f : 15.5f;
                if (!(candidate + CrewSkill.ThreatMargin(ThreeStars) <= 15f))
                {
                    held = 0f;
                    continue;
                }
                held += step;
                if (CrewSkill.ShouldSwitch(15f, candidate, ThreeStars, held))
                {
                    failures.Add($"Closer threat: a candidate who was only ever inside " +
                                 $"the margin for 0.20 s took the aim at t={t:F2} s " +
                                 $"against a {CrewSkill.ThreatDwell(ThreeStars):F2} s dwell.");
                    return;
                }
            }
        }

        static void TwoLevelEnemiesDoNotFlickerTheAim(List<string> failures)
        {
            // Two men oscillating a metre either side of level, twenty seconds of it,
            // and the aim is never taken off the first: neither ever beats the other by
            // the whole margin. The count is the assertion - "no ping-pong" is a rate,
            // not a single frame (acceptance 4).
            const float step = 1f / 60f;
            float mark = 13f;
            int switches = 0;
            float held = 0f;
            for (var t = 0f; t < 20f; t += step)
            {
                float candidate = 13f + (float)Math.Sin(t * 3.0) * 1.0f;
                if (candidate + CrewSkill.ThreatMargin(ThreeStars) <= mark)
                {
                    held += step;
                    if (CrewSkill.ShouldSwitch(mark, candidate, ThreeStars, held))
                    {
                        switches++;
                        // he is on the new man now, and the old one has to beat HIM
                        float swapped = mark;
                        mark = candidate;
                        candidate = swapped;
                        held = 0f;
                    }
                }
                else held = 0f;
            }
            if (switches != 0)
                failures.Add($"Closer threat: two enemies a metre either side of level " +
                             $"produced {switches} target switches in 20 s.");
        }

        // -------------------------------------------------------------------- the cone

        static void TheConeComesOffTheGunsOwnAccuracy(List<string> failures)
        {
            // The five guns in the game, read straight off their accuracy so a weapon
            // added tomorrow needs no new number.
            Near(failures, "pistol", CrewSkill.BaseMissConeDegrees(0.55f), 8.5f);
            Near(failures, "machine pistol", CrewSkill.BaseMissConeDegrees(0.30f), 11.0f);
            Near(failures, "Tommy gun", CrewSkill.BaseMissConeDegrees(0.35f), 10.5f);
            Near(failures, "rifle", CrewSkill.BaseMissConeDegrees(0.88f), 5.2f);
            Near(failures, "shotgun", CrewSkill.BaseMissConeDegrees(0.97f), 4.3f);
            // a three-star's cone IS the gun's own
            Near(failures, "the rifle at three stars",
                 CrewSkill.MissConeDegrees(0.88f, ThreeStars),
                 CrewSkill.BaseMissConeDegrees(0.88f));
            // and nothing inverts on an accuracy nobody should have typed
            Near(failures, "a gun of no accuracy", CrewSkill.BaseMissConeDegrees(-1f), 14f);
            Near(failures, "a gun that cannot miss", CrewSkill.BaseMissConeDegrees(2f), 4f);
        }

        static void TheConeIsWideForBadHandsAndTightForGood(List<string> failures)
        {
            float bad = CrewSkill.MissConeDegrees(0.88f, OneStar);
            float fair = CrewSkill.MissConeDegrees(0.88f, ThreeStars);
            float good = CrewSkill.MissConeDegrees(0.88f, FiveStars);
            if (!(bad > fair && fair > good))
                failures.Add($"Closer threat: rifle cones are not ordered by the hands " +
                             $"holding it - {bad:F2} / {fair:F2} / {good:F2} degrees.");
            // the man never turns a gun into a different gun: a five-star with a
            // machine pistol still scatters wider than a one-star with a rifle
            if (!(CrewSkill.MissConeDegrees(0.30f, FiveStars) >
                  CrewSkill.MissConeDegrees(0.88f, OneStar)))
                failures.Add("Closer threat: skill overtook the weapon - a five-star " +
                             "machine pistol scatters tighter than a one-star rifle.");
        }

        static void AnAngleWidensWithRange(List<string> failures)
        {
            // The reason the scatter is an angle and not an offset (D6): the same
            // shooter with the same gun puts his misses wider the farther off the man
            // is, with no range term anywhere in the table.
            float cone = CrewSkill.MissConeDegrees(0.88f, ThreeStars);
            float atTen = Wide(cone, 10f);
            float atTwentyFive = Wide(cone, 25f);
            if (!(atTwentyFive > atTen + 1f))
                failures.Add($"Closer threat: the same cone is not wider at 25 m " +
                             $"({atTwentyFive:F2} m) than at 10 m ({atTen:F2} m).");
        }

        static void ARiflemanAtTwentyFiveMetresReadsOnScreen(List<string> failures)
        {
            // The brief's own arithmetic, kept honest: a one-star rifleman at
            // twenty-five metres misses up to about 2.8 m wide and a five-star about
            // 1.7 m. If a later tuning pass moves these the number to change is in
            // CrewSkill and this row moves with it - but a five-star who misses as wide
            // as a one-star is the failure, and that is what this measures.
            float bad = Wide(CrewSkill.MissConeDegrees(0.88f, OneStar), 25f);
            float good = Wide(CrewSkill.MissConeDegrees(0.88f, FiveStars), 25f);
            Near(failures, "a one-star rifleman at 25 m", bad, 2.8f, 0.25f);
            Near(failures, "a five-star rifleman at 25 m", good, 1.7f, 0.25f);
            if (!(bad > good * 1.4f))
                failures.Add($"Closer threat: the gap between a one-star and a five-star " +
                             $"rifleman at 25 m is {bad:F2} m against {good:F2} m - too " +
                             "narrow to read on screen.");
        }

        // ---------------------------------------------------------------- the sampler

        /// <summary>The guns in the game, by the accuracy the cone is read off.</summary>
        static readonly (string Name, float Accuracy)[] Guns =
        {
            ("pistol", 0.55f), ("twin pistols", 0.45f), ("machine pistol", 0.30f),
            ("Tommy gun", 0.35f), ("rifle", 0.88f), ("shotgun", 0.97f),
        };

        static void NoRoundLeavesTheCone(List<string> failures)
        {
            // THE BOUND, SWEPT RATHER THAN ARGUED. Yaw and pitch drawn independently at
            // their own maxima put a corner round outside the cone the table advertises
            // and the trace reports; drawn as a point in a disc they cannot. Every gun,
            // every half-step, and the whole square of rolls including its corners.
            foreach (var gun in Guns)
                for (var hs = AttributeScale.MinHalfSteps; hs <= AttributeScale.MaxHalfSteps; hs++)
                {
                    float cone = CrewSkill.MissConeDegrees(gun.Accuracy, hs);
                    for (var r = 0; r <= 20; r++)
                        for (var a = 0; a <= 20; a++)
                        {
                            CrewSkill.MissAngles(cone, r / 20f, a / 20f,
                                                 out float yaw, out float pitch);
                            float off = CrewSkill.MissOffAxisDegrees(yaw, pitch);
                            if (off <= cone + 0.001f) continue;
                            failures.Add($"Closer threat: a {gun.Name} at {hs} half-steps " +
                                         $"threw a round {off:F3} deg off the aim line " +
                                         $"against a {cone:F3} deg cone (rolls " +
                                         $"{r / 20f:F2}/{a / 20f:F2}, yaw {yaw:F2}, " +
                                         $"pitch {pitch:F2}).");
                            return;
                        }
                }
            // and rolls nobody should have passed in are held inside the cone too
            CrewSkill.MissAngles(10f, -3f, 7f, out float wildYaw, out float wildPitch);
            if (CrewSkill.MissOffAxisDegrees(wildYaw, wildPitch) > 10.001f)
                failures.Add("Closer threat: a roll outside [0,1] left the cone.");
            // no cone at all is no scatter at all: a hit goes where it was aimed
            CrewSkill.MissAngles(0f, 0.7f, 0.3f, out float noYaw, out float noPitch);
            if (noYaw != 0f || noPitch != 0f)
                failures.Add($"Closer threat: a zero cone still turned the round " +
                             $"{noYaw:F3}/{noPitch:F3} degrees.");
        }

        static void TheConeIsFilledToItsEdge(List<string> failures)
        {
            // The other half of the bound: a sampler that never reaches its own edge has
            // quietly narrowed every shooter in the game, and the table would stop
            // meaning what it says.
            float cone = CrewSkill.MissConeDegrees(0.88f, OneStar);
            float widest = 0f;
            for (var r = 0; r <= 40; r++)
                for (var a = 0; a <= 40; a++)
                {
                    CrewSkill.MissAngles(cone, r / 40f, a / 40f,
                                         out float yaw, out float pitch);
                    float off = CrewSkill.MissOffAxisDegrees(yaw, pitch);
                    if (off > widest) widest = off;
                }
            if (widest < cone - 0.05f)
                failures.Add($"Closer threat: the widest round of a {cone:F2} deg cone " +
                             $"was only {widest:F2} deg off - the cone is not filled.");
        }

        static void TheSpreadIsNotPiledOnTheAimLine(List<string> failures)
        {
            // A radius drawn straight off a roll piles most rounds on the centre and
            // reads as a shooter with a flinch rather than a spread. The square root
            // spreads the draw evenly over the disc, and the mean lands past a third of
            // the cone.
            float cone = CrewSkill.MissConeDegrees(0.30f, ThreeStars);
            float total = 0f;
            var n = 0;
            for (var r = 0; r <= 40; r++)
                for (var a = 0; a <= 40; a++)
                {
                    CrewSkill.MissAngles(cone, r / 40f, a / 40f,
                                         out float yaw, out float pitch);
                    total += CrewSkill.MissOffAxisDegrees(yaw, pitch);
                    n++;
                }
            float mean = total / n;
            if (mean < cone * 0.3f)
                failures.Add($"Closer threat: the mean miss of a {cone:F2} deg cone is " +
                             $"{mean:F2} deg - the spread is piled on the aim line.");
            if (mean > cone * 0.75f)
                failures.Add($"Closer threat: the mean miss of a {cone:F2} deg cone is " +
                             $"{mean:F2} deg - almost every round is on the rim.");
        }

        static void APitchedRoundStaysUnderTheShoulderLine(List<string> failures)
        {
            // A round misses past a man's shoulder rather more often than over his head.
            float cone = CrewSkill.MissConeDegrees(0.55f, ThreeStars);
            float widestYaw = 0f, widestPitch = 0f;
            for (var r = 0; r <= 40; r++)
                for (var a = 0; a <= 40; a++)
                {
                    CrewSkill.MissAngles(cone, r / 40f, a / 40f,
                                         out float yaw, out float pitch);
                    widestYaw = Math.Max(widestYaw, Math.Abs(yaw));
                    widestPitch = Math.Max(widestPitch, Math.Abs(pitch));
                }
            if (widestPitch > widestYaw * 0.5f)
                failures.Add($"Closer threat: the cone climbs {widestPitch:F2} deg " +
                             $"against {widestYaw:F2} deg wide - rounds go over heads as " +
                             "readily as past shoulders.");
            Near(failures, "the pitch share of the cone",
                 widestPitch, cone * CrewSkill.MissPitchShare, 0.05f);
        }

        /// <summary>How far off the aim line the widest round of a cone lands, at this
        /// range. The measure the Play verdict is describing in words.</summary>
        static float Wide(float degrees, float metres) =>
            metres * (float)Math.Tan(degrees * Math.PI / 180.0);

        // ------------------------------------------------------------------- the rulers

        static void Same(List<string> failures, string what, float got, float want) =>
            Near(failures, what, got, want, 0.0005f);

        static void Near(List<string> failures, string what, float got, float want) =>
            Near(failures, what, got, want, 0.05f);

        static void Near(List<string> failures, string what, float got, float want,
                         float slack)
        {
            if (Math.Abs(got - want) <= slack) return;
            failures.Add($"Closer threat: {what} is {got:F3}, expected {want:F3}.");
        }

        static void Between(List<string> failures, string what, float got, float low,
                            float high)
        {
            if (got > low && got < high) return;
            failures.Add($"Closer threat: {what} is {got:F3}, which is not strictly " +
                         $"between {low:F3} and {high:F3}.");
        }
    }
}
