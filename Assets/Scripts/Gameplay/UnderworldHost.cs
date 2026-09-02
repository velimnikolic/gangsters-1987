using UnityEngine;
using LivingCity.Outfit;

namespace LivingCity.Gameplay
{
    /// <summary>
    /// What the engine owes the pure <see cref="Underworld"/>: a clean slate at Play,
    /// somewhere for a fault to be printed, and the one answer to "which seed was this
    /// city built from".
    ///
    /// The books themselves are engine-free; this is the three lines that cannot be.
    /// </summary>
    public static class UnderworldHost
    {
        /// <summary>The books a scene with no city runs on. Same number the personnel
        /// director has always fallen back to, so the standalone Ledger deals the same
        /// outfit it always did.</summary>
        public const int FallbackSeed = 42;

        /// <summary>Statics outlive Play when domain reload is off - the BusinessDeeds
        /// discipline. This runs before any scene's Awake, so the city builder's own
        /// Ensure lands on an empty slot.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            Underworld.ResetForPlay();
            Underworld.Fault = message => Debug.LogError(message);
        }

        /// <summary>
        /// The seed this scene's city was built from - the ONE derivation, so the two
        /// directors and the builder can never deal two different underworlds. A
        /// generated city carries it on its config; the street city carries the number
        /// it actually built from (which is not the inspector's when the seed is rolled
        /// each Play); a scene with no city at all falls back.
        ///
        /// <paramref name="quiet"/> silences the warning for callers that are not the
        /// ones responsible for saying so.
        /// </summary>
        public static int SeedForScene(bool quiet = true, Object context = null)
        {
            var builder = Object.FindAnyObjectByType<Generation.CityBuilder>();
            if (builder && builder.Config)
                return builder.Config.seed;

            var roadDemo = Object.FindAnyObjectByType<RoadDemo.RoadDemoBuilder>();
            if (roadDemo)
                return roadDemo.BuiltFromSeed;

            // In the standalone Ledger menu the missing city is the DESIGN, not a
            // fault - the warning would cry wolf on every single Play there.
            if (!quiet && !Object.FindAnyObjectByType<UI.LedgerMenuScene>())
                Debug.LogWarning("[Underworld] No city generator in the scene - the " +
                                 "houses run on the fallback seed.", context);
            return FallbackSeed;
        }
    }
}
