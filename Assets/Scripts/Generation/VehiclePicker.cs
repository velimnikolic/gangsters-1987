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

        /// <summary>
        /// Exposed so an inner picker - the zone override at BuildBlock, the landmark forecourt
        /// at PlaceLandmark - can inherit the OUTER picker's filter rather than build its own.
        /// One filter per city build, or every landmark's first camper shares one roll - the
        /// per-lot-tinter disease VehicleTinter's header describes.
        /// </summary>
        public RareVehicleFilter Rare { get; }

        public VehiclePicker(PrefabDatabase.WeightedPrefabs[] source, System.Random rng,
                             RareVehicleFilter rare = null)
        {
            this.rng = rng;
            Rare = rare;

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
                    return Rare == null
                        ? prefab
                        : Rare.Apply(prefab, groups[index].prefabs, maxLength, maxWidth);
                }

                bag.Advance();
            }

            return null;
        }

        internal static bool Fits(GameObject prefab, float maxLength, float maxWidth)
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

    /// <summary>
    /// Demotes the camper: a ShuffleBag guarantees every member once per cycle, which made
    /// car-caravan-small ~12% of all placed parked cars, and neither removing it nor duplicating
    /// its neighbours is on the table - the bags draw from BlockBuilder's shared Buildings stream
    /// and any length change re-lays the whole city (see PrefabDatabase.paintableVehicles for
    /// the same constraint stated first). So the bag deals exactly as before, draw for draw, and
    /// this filter afterwards swaps a rare deal for an ordinary body from the same group, keeping
    /// it only rareVehicleKeepChance of the time.
    ///
    /// The rolls come off their own stream (SeedOffsets.RareVehicles), the VehicleTints move:
    /// alongside a picker on the Buildings stream, a rarity roll taken from THAT stream would
    /// move every building placed after it. One instance per city build, shared by every picker
    /// through VehiclePicker.Rare - a filter made per landmark would restart the sequence and
    /// hand every forecourt the same first roll.
    /// </summary>
    public sealed class RareVehicleFilter
    {
        readonly HashSet<string> names = new();
        readonly float keepChance;
        readonly System.Random rng;

        public RareVehicleFilter(PrefabDatabase prefabs, CityConfig config)
        {
            rng = new System.Random(config.seed + SeedOffsets.RareVehicles);

            if (!prefabs)
                return;

            keepChance = prefabs.rareVehicleKeepChance;

            if (prefabs.rareVehicleNames == null)
                return;

            foreach (var name in prefabs.rareVehicleNames)
                if (!string.IsNullOrEmpty(name))
                    names.Add(name);
        }

        /// <summary>
        /// Called on every ACCEPTED pick, rare or not, and both draws happen unconditionally -
        /// VehicleTinter's discipline: the list and the chance decide what is done with the
        /// samples, never how many are taken, so adding a second rare name cannot re-roll the
        /// campers already placed. The substitute faces the same bay limits as the deal it
        /// replaces; a group with no ordinary body that fits keeps the rare one rather than
        /// returning an empty bay the layout did not ask for.
        /// </summary>
        public GameObject Apply(GameObject picked, GameObject[] group, float maxLength, float maxWidth)
        {
            var keepRoll = rng.NextDouble();
            var slotRoll = rng.NextDouble();

            if (!picked || names.Count == 0 || !names.Contains(picked.name) || keepRoll < keepChance)
                return picked;

            List<GameObject> ordinary = null;
            if (group != null)
                foreach (var candidate in group)
                {
                    if (!candidate || names.Contains(candidate.name)
                        || !VehiclePicker.Fits(candidate, maxLength, maxWidth))
                        continue;

                    ordinary ??= new List<GameObject>();
                    ordinary.Add(candidate);
                }

            if (ordinary == null)
                return picked;

            // slotRoll is strictly below 1, so the index is strictly below Count.
            return ordinary[(int)(slotRoll * ordinary.Count)];
        }
    }
}
