using System.Collections.Generic;
using System.Text;
using UnityEngine;
using LivingCity.Data;

namespace LivingCity.Generation
{
    /// <summary>
    /// Decides what each block is FOR, before anything is built.
    ///
    /// Runs as its own pass rather than inside BlockBuilder because the ground needs the answer
    /// too - a park is grass and a car park is asphalt - and two placers deriving the zone
    /// independently would eventually disagree and pave a park.
    ///
    /// Three things shape the result, and all three exist because a flat random roll produced a
    /// bad city:
    ///
    /// 1. A radial bias. Rolling uniformly scatters factories through the middle of town and
    ///    leaves the centre no denser than the rim. Chicago put its works out by the rail and
    ///    its density in the Loop, so downtown weights rise toward the centre and industry and
    ///    houses rise toward the edge.
    /// 2. Quotas. On a 9x7 map there are only about twelve blocks. Without a cap an unlucky
    ///    seed gives a city four hospitals and no housing.
    /// 3. A shuffled visit order. Quotas are consumed as blocks are assigned, so walking the
    ///    blocks in id order would hand every scarce zone to the low-numbered corner of the map.
    /// </summary>
    public static class ZonePlanner
    {
        /// <summary>
        /// Zones that are one landmark building and its yard. Capped together as well as
        /// individually - one hospital in a twelve-block city is a landmark, four civic blocks
        /// is a government district nobody asked for.
        /// </summary>
        static readonly BlockZone[] SingleLandmarkZones =
        {
            BlockZone.Police,
            BlockZone.Hospital,
            BlockZone.School,
            BlockZone.FireStation,
        };

        /// <summary>Blocks per single-landmark zone allowed. 12 blocks -> 2.</summary>
        const int BlocksPerLandmarkZone = 6;

        public static void Assign(CityGrid grid, PrefabDatabase prefabs, CityConfig config)
        {
            if (grid.BlockCount == 0)
                return;

            // Debugging aid: one palette on every block, so a layout change to it is visible
            // everywhere at once. A warning, not a log - a city generated with this left on
            // looks plausible enough to ship by accident.
            if (config.debugSingleZone)
            {
                for (var blockId = 0; blockId < grid.BlockCount; blockId++)
                    grid.SetZone(blockId, config.debugZone);

                Debug.LogWarning($"[ZonePlanner] Debug Single Zone is ON - every block is " +
                                 $"{config.debugZone}. Toggle it off under Tools/City.");
                return;
            }

            var palettes = new List<PrefabDatabase.ZonePalette>();
            if (prefabs.zonePalettes != null)
                foreach (var palette in prefabs.zonePalettes)
                    if (palette != null && palette.weight > 0f)
                        palettes.Add(palette);

            if (palettes.Count == 0)
            {
                Debug.LogWarning("[ZonePlanner] No zone palettes in the PrefabDatabase - every " +
                                 "block falls back to ResidentialHigh.");
                return;
            }

            var rng = new System.Random(config.seed + SeedOffsets.Zoning);

            var centroids = Centroids(grid);

            // Cached rather than queried per roll: the neighbour probe walks the whole grid, and
            // it is asked for once per block per candidate palette.
            var adjacency = new List<int>[grid.BlockCount];
            for (var blockId = 0; blockId < grid.BlockCount; blockId++)
                adjacency[blockId] = new List<int>(grid.NeighbourBlocks(blockId));

            // Two caps, and the difference matters. maxShare scales with the city, which is what
            // parks and works want - a map twice the size should have twice the parkland.
            // maxBlocks does not, which is what a hospital wants: a city has one however big it
            // gets. Expressing the hospital as a share was a real bug - 9% of a twelve-block map
            // rounds to one, and 9% of a forty-block map to three.
            var remaining = new int[palettes.Count];
            for (var i = 0; i < palettes.Count; i++)
            {
                var byShare = palettes[i].maxShare >= 1f
                    ? int.MaxValue
                    : Mathf.Max(1, Mathf.RoundToInt(grid.BlockCount * palettes[i].maxShare));

                remaining[i] = palettes[i].maxBlocks > 0
                    ? Mathf.Min(byShare, palettes[i].maxBlocks)
                    : byShare;
            }

            var landmarkBudget = Mathf.Max(1, grid.BlockCount / BlocksPerLandmarkZone);

            var order = ShuffledBlockOrder(grid.BlockCount, rng);
            var weights = new float[palettes.Count];

            foreach (var blockId in order)
            {
                var radial = RadialPosition(centroids[blockId], grid);

                var total = 0f;
                for (var i = 0; i < palettes.Count; i++)
                {
                    var palette = palettes[i];

                    var allowed = remaining[i] > 0
                        && (!IsSingleLandmark(palette.zone) || landmarkBudget > 0)
                        && !ClashesWithNeighbour(palette.zone, blockId, adjacency, grid);

                    weights[i] = allowed ? palette.weight * RadialBias(palette.zone, radial) : 0f;
                    total += weights[i];
                }

                var chosen = WeightedRoll.Index(weights, total, rng);

                // Every candidate retired. Rather than leave the block at the enum default and
                // pretend, fall back explicitly to the city's connective tissue.
                if (chosen < 0)
                {
                    grid.SetZone(blockId, BlockZone.ResidentialHigh);
                    continue;
                }

                var zone = palettes[chosen].zone;
                grid.SetZone(blockId, zone);

                if (remaining[chosen] != int.MaxValue)
                    remaining[chosen]--;
                if (IsSingleLandmark(zone))
                    landmarkBudget--;
            }
        }

        /// <summary>Zone counts, highest first - the one line that makes a generated layout checkable.</summary>
        public static string Describe(CityGrid grid)
        {
            var counts = new Dictionary<BlockZone, int>();
            for (var blockId = 0; blockId < grid.BlockCount; blockId++)
            {
                var zone = grid.ZoneOf(blockId);
                counts.TryGetValue(zone, out var count);
                counts[zone] = count + 1;
            }

            var ordered = new List<KeyValuePair<BlockZone, int>>(counts);
            ordered.Sort((a, b) => b.Value != a.Value
                ? b.Value.CompareTo(a.Value)
                : a.Key.CompareTo(b.Key));

            var text = new StringBuilder();
            foreach (var entry in ordered)
            {
                if (text.Length > 0) text.Append(", ");
                text.Append(entry.Key).Append(' ').Append(entry.Value);
            }
            return text.ToString();
        }

        static bool IsSingleLandmark(BlockZone zone)
        {
            foreach (var candidate in SingleLandmarkZones)
                if (candidate == zone)
                    return true;
            return false;
        }

        /// <summary>
        /// Two parks side by side read as one big park with a road through it, which wastes the
        /// scarcest zone in the city. Nothing else cares who its neighbours are.
        /// </summary>
        static bool ClashesWithNeighbour(
            BlockZone zone, int blockId, List<int>[] adjacency, CityGrid grid)
        {
            if (zone != BlockZone.Park)
                return false;

            foreach (var neighbour in adjacency[blockId])
                if (grid.ZoneOf(neighbour) == BlockZone.Park)
                    return true;

            return false;
        }

        /// <summary>
        /// 0 at the centre of the map, 1 at the corner. Normalised against the half-diagonal so
        /// the value reaches 1 on a rectangular map rather than only on a square one.
        /// </summary>
        static float RadialPosition(Vector2 centroid, CityGrid grid)
        {
            var centre = new Vector2((grid.Width - 1) * 0.5f, (grid.Height - 1) * 0.5f);
            var halfDiagonal = centre.magnitude;
            return halfDiagonal <= 0f ? 0f : Mathf.Clamp01((centroid - centre).magnitude / halfDiagonal);
        }

        /// <summary>
        /// Multiplier on a zone's weight given how far out the block sits. Never returns zero:
        /// a factory near the centre should be unlikely, not impossible, or every seed produces
        /// the same tidy concentric city.
        /// </summary>
        static float RadialBias(BlockZone zone, float radial) => zone switch
        {
            BlockZone.Downtown => Mathf.Lerp(1.8f, 0.15f, radial),
            BlockZone.Chinatown => Mathf.Lerp(1.4f, 0.4f, radial),
            BlockZone.Industrial => Mathf.Lerp(0.2f, 1.8f, radial),
            BlockZone.ResidentialLow => Mathf.Lerp(0.35f, 1.6f, radial),
            _ => 1f,
        };

        static Vector2[] Centroids(CityGrid grid)
        {
            var sums = new Vector2[grid.BlockCount];
            var counts = new int[grid.BlockCount];

            for (var x = 0; x < grid.Width; x++)
            for (var z = 0; z < grid.Height; z++)
            {
                var blockId = grid.BlockIdAt(x, z);
                if (blockId < 0) continue;

                sums[blockId] += new Vector2(x, z);
                counts[blockId]++;
            }

            for (var i = 0; i < sums.Length; i++)
                if (counts[i] > 0)
                    sums[i] /= counts[i];

            return sums;
        }

        /// <summary>
        /// Fisher-Yates over the block ids. Quotas are spent as blocks are assigned, so without
        /// this the scarce zones would all land in the low-numbered corner the flood fill
        /// happened to start from.
        /// </summary>
        static int[] ShuffledBlockOrder(int blockCount, System.Random rng)
        {
            var order = new int[blockCount];
            for (var i = 0; i < blockCount; i++)
                order[i] = i;

            for (var i = blockCount - 1; i > 0; i--)
            {
                var j = rng.Next(i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }

            return order;
        }
    }
}
