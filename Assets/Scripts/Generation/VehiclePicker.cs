using System.Collections.Generic;
using UnityEngine;
using LivingCity.Data;

namespace LivingCity.Generation
{
    /// <summary>
    /// Chooses a vehicle prefab: weighted roll for the group, then a ShuffleBag within it.
    ///
    /// Three call sites used to draw uniformly from one flat array each - kerbside parking,
    /// the block car parks and the AI traffic spawner - which is why a crawler crane could
    /// end up parked outside a flat. The weighting lives in the PrefabDatabase so the mix is
    /// tuned in the inspector rather than in code.
    ///
    /// The RNG is supplied by the caller, never created here: each subsystem owns a stream
    /// seeded from CityConfig.seed + its SeedOffsets entry, and that is what keeps a seed
    /// reproducible.
    /// </summary>
    public sealed class VehiclePicker
    {
        /// <summary>
        /// Rolls before giving up on finding something that fits. A rejected candidate is
        /// discarded from its bag first, so this converges rather than re-testing one oversized
        /// prefab eight times.
        /// </summary>
        const int FitAttempts = 8;

        readonly List<PrefabDatabase.WeightedPrefabs> groups = new();
        readonly List<ShuffleBag> bags = new();
        readonly System.Random rng;
        readonly float totalWeight;

        public VehiclePicker(PrefabDatabase.WeightedPrefabs[] source, System.Random rng)
        {
            this.rng = rng;

            if (source == null)
                return;

            foreach (var group in source)
            {
                if (group == null || !group.IsUsable)
                    continue;

                groups.Add(group);
                bags.Add(new ShuffleBag(group.prefabs, rng));
                totalWeight += group.weight;
            }
        }

        public bool IsEmpty => groups.Count == 0;

        /// <summary>
        /// Next vehicle, or null when nothing in the catalogue fits the space on offer.
        ///
        /// Limits are measured off the prefab's own bounds as longest-vs-shortest ground axis
        /// rather than a fixed X or Z, because the packs do not agree on which way a model
        /// faces - and a road vehicle is always longer than it is wide, so the mapping holds.
        /// </summary>
        public GameObject Next(
            float maxLength = float.PositiveInfinity,
            float maxWidth = float.PositiveInfinity)
        {
            if (groups.Count == 0)
                return null;

            var unbounded = float.IsPositiveInfinity(maxLength) && float.IsPositiveInfinity(maxWidth);

            for (var attempt = 0; attempt < FitAttempts; attempt++)
            {
                var index = PickGroupIndex();
                var bag = bags[index];

                var prefab = bag.Peek();
                if (!prefab)
                {
                    bag.Advance();
                    continue;
                }

                if (unbounded || Fits(prefab, maxLength, maxWidth))
                {
                    bag.Advance();
                    return prefab;
                }

                bag.Advance();
            }

            return null;
        }

        static bool Fits(GameObject prefab, float maxLength, float maxWidth)
        {
            var size = PrefabBounds.Get(prefab).size;
            return Mathf.Max(size.x, size.z) <= maxLength
                && Mathf.Min(size.x, size.z) <= maxWidth;
        }

        /// <summary>Index rather than the group itself, because the bag sits in a parallel list.</summary>
        int PickGroupIndex() => groups.IndexOf(PickGroup(groups, totalWeight, rng));

        /// <summary>
        /// Weighted roll shared with BlockBuilder's building kits. Forwards to WeightedRoll so
        /// there is exactly one implementation - see the note there about why that matters.
        /// </summary>
        public static T PickGroup<T>(List<T> groups, float totalWeight, System.Random rng)
            where T : PrefabDatabase.WeightedPrefabs =>
            WeightedRoll.Pick(groups, totalWeight, rng);
    }
}
