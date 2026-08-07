using System.Collections.Generic;
using UnityEngine;
using LivingCity.Data;

namespace LivingCity.Generation
{
    /// <summary>
    /// Chooses a pedestrian prefab: weighted roll for the group, then a ShuffleBag within it.
    ///
    /// PedestrianSpawner used to draw uniformly from one flat array of every prefab in
    /// People_AI_T, which made the pack's single gangster exactly as common as its lifeguard
    /// and its samurai. Vehicles hit the same wall first and VehiclePicker is the answer they
    /// arrived at; this is that shape with the vehicle-specific half removed. There is no fit
    /// test here - a person has no length to check a parking space against - so Next() cannot
    /// fail the way VehiclePicker.Next() can, and the retry loop goes with it.
    ///
    /// The weighting lives in the PrefabDatabase so the crowd is tuned in the inspector rather
    /// than in code, and the RNG is supplied by the caller, never created here: each subsystem
    /// owns a stream seeded from CityConfig.seed + its SeedOffsets entry, and that is what
    /// keeps a seed reproducible.
    /// </summary>
    public sealed class PedestrianPicker
    {
        readonly List<PrefabDatabase.WeightedPrefabs> groups = new();
        readonly List<ShuffleBag> bags = new();
        readonly System.Random rng;
        readonly float totalWeight;

        public PedestrianPicker(PrefabDatabase.WeightedPrefabs[] source, System.Random rng)
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
        /// The next pedestrian, or null only when the catalogue is empty.
        ///
        /// A bag can still hand back a null - a prefab reference that went stale after the
        /// database was written - so the draw advances past it and rolls again rather than
        /// handing a null to Instantiate. Bounded by the group count: a catalogue whose every
        /// entry is broken returns null instead of spinning.
        /// </summary>
        public GameObject Next()
        {
            for (var attempt = 0; attempt < groups.Count; attempt++)
            {
                var bag = bags[PickGroupIndex()];

                var prefab = bag.Peek();
                bag.Advance();

                if (prefab)
                    return prefab;
            }

            return null;
        }

        /// <summary>
        /// Roll across the groups by weight. Falls back to the first group rather than
        /// returning -1 when floating-point drift leaves the roll past the last threshold.
        /// </summary>
        int PickGroupIndex()
        {
            var roll = (float)rng.NextDouble() * totalWeight;

            for (var i = 0; i < groups.Count; i++)
            {
                roll -= groups[i].weight;
                if (roll <= 0f)
                    return i;
            }

            return 0;
        }
    }
}
