using System.Collections.Generic;
using LivingCity.Data;

namespace LivingCity.Generation
{
    /// <summary>
    /// The weighted roll, in one place.
    ///
    /// Three subsystems need it in two different shapes - vehicles and building kits roll over
    /// a list of objects that carry their own weight, while zoning rolls over a float array it
    /// has just adjusted per block. Keeping both here stops the two copies drifting apart, which
    /// matters because a subtly different roll would silently change what a seed produces.
    ///
    /// The RNG is always supplied by the caller, never created here: each subsystem owns a
    /// stream seeded from CityConfig.seed plus its SeedOffsets entry, and that is what keeps a
    /// seed reproducible.
    /// </summary>
    public static class WeightedRoll
    {
        /// <summary>
        /// Index into <paramref name="weights"/>, chosen in proportion to them. Returns -1 when
        /// every weight is zero - which is a real case for zoning, where quotas can retire every
        /// remaining candidate, and must not be papered over by returning the last index.
        /// </summary>
        public static int Index(IReadOnlyList<float> weights, float totalWeight, System.Random rng)
        {
            if (weights == null || weights.Count == 0 || totalWeight <= 0f)
                return -1;

            var roll = (float)rng.NextDouble() * totalWeight;
            for (var i = 0; i < weights.Count; i++)
            {
                roll -= weights[i];
                if (roll <= 0f)
                    return i;
            }

            // Only reachable through float rounding at the very top of the range.
            return weights.Count - 1;
        }

        /// <summary>The same roll over items that carry their own weight.</summary>
        public static T Pick<T>(List<T> groups, float totalWeight, System.Random rng)
            where T : PrefabDatabase.WeightedPrefabs
        {
            if (groups == null || groups.Count == 0)
                return null;

            var roll = (float)rng.NextDouble() * totalWeight;
            foreach (var group in groups)
            {
                roll -= group.weight;
                if (roll <= 0f)
                    return group;
            }

            return groups[groups.Count - 1];
        }
    }
}
