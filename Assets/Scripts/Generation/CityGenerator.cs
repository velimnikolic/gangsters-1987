using System.Collections.Generic;
using UnityEngine;
using LivingCity.Data;

namespace LivingCity.Generation
{
    /// <summary>
    /// Per-subsystem seed offsets. UnityEngine.Random is global mutable state - the moment
    /// any third-party script draws from it, seed reproducibility is gone. Every subsystem
    /// instead owns a System.Random seeded from (config.seed + its own offset), so a change
    /// in how many numbers one subsystem draws cannot shift any other subsystem's output.
    /// </summary>
    public static class SeedOffsets
    {
        public const int Roads = 0;
        public const int Buildings = 1_000;
        public const int Props = 2_000;
        public const int ParkedCars = 3_000;
        public const int Vehicles = 4_000;
        public const int Pedestrians = 5_000;
        public const int Ambient = 6_000;
        public const int Zoning = 7_000;
        public const int BuildingTints = 8_000;
        public const int Ground = 9_000;
    }

    /// <summary>
    /// Produces the city layout as pure data. Deterministic for a given config.seed.
    /// </summary>
    public static class CityGenerator
    {
        public static CityGrid Generate(CityConfig config)
        {
            var rng = new System.Random(config.seed + SeedOffsets.Roads);

            // The grid IS the city - no margin, no overhang. Every street runs to the outer
            // edge and stops there, so the outline stays a clean rectangle. Roads that carried
            // on past the edge were tried and rejected: the stubs read as an unfinished map.
            var grid = new CityGrid(config.gridWidth, config.gridHeight);

            // Everything starts buildable; the arterials then carve roads back out of it.
            for (var x = 0; x < grid.Width; x++)
            for (var z = 0; z < grid.Height; z++)
                grid[x, z] = CellType.Block;

            var columns = PickInteriorArterials(config.gridWidth, config.minArterialSpacing, config.maxArterialSpacing, rng);
            var rows = PickInteriorArterials(config.gridHeight, config.minArterialSpacing, config.maxArterialSpacing, rng);

            foreach (var column in columns)
                for (var z = 0; z < grid.Height; z++)
                    grid[column, z] = CellType.Road;

            foreach (var row in rows)
                for (var x = 0; x < grid.Width; x++)
                    grid[x, row] = CellType.Road;

            grid.AssignBlockIds();

            // A full arterial lattice is connected by construction, so this should never
            // fire. It is kept because an unreachable road cell produces a stream of
            // "Path not found" warnings that looks exactly like a tile rotation bug.
            if (!grid.RoadsAreConnected())
                Debug.LogError("[CityGenerator] Road network has unreachable cells - cars will fail to path.");

            return grid;
        }

        /// <summary>
        /// Arterial line indices across one axis, all of them strictly inside the map.
        ///
        /// Forcing a line onto index 0 and index size-1 - which is what PickArterials does on
        /// its own - paves the entire outer ring, and that ring is the single most visible
        /// thing about the city: a closed loop of asphalt running right round the outside.
        /// The city has to read as a rectangle that the streets run out of, not as an island
        /// with a bypass around it.
        ///
        /// The trick is to ask for a partition of a span two cells LONGER than the map and
        /// then shift it left by one, so the outermost two lines land at -1 and size - just
        /// outside the map, where they are dropped. Those two phantom lines are what makes the
        /// edge blocks behave: every block, including the ones against the map boundary, is
        /// still bounded by an arterial on both sides, so its width falls in the same
        /// [minSpacing-1, maxSpacing-1] range as every interior block. The map edge acts as
        /// one more street that simply is not drawn.
        ///
        /// Guaranteed by construction: the first surviving line is at least 1 and the last is
        /// at most size-2, so row 0, row height-1, column 0 and column width-1 are never road.
        /// The ring is impossible, not merely unlikely.
        /// </summary>
        static List<int> PickInteriorArterials(int size, int minSpacing, int maxSpacing, System.Random rng)
        {
            var interior = new List<int>();
            foreach (var line in PickArterials(size + 2, minSpacing, maxSpacing, rng))
            {
                var index = line - 1;
                if (index > 0 && index < size - 1)
                    interior.Add(index);
            }

            // A map too small to hold even one interior arterial would generate as a single
            // solid block with no streets at all. CityConfig.MinGridSize keeps us clear of it.
            if (interior.Count == 0)
                Debug.LogError($"[CityGenerator] No arterial fits inside a {size}-cell axis - " +
                               "the city would have no streets on it. Raise the grid size.");

            return interior;
        }

        /// <summary>
        /// Chooses arterial line indices across one axis. Always includes both edges, and
        /// every gap lands inside [minSpacing, maxSpacing].
        ///
        /// This partitions the span into randomly-sized segments rather than spacing lines
        /// evenly and nudging them afterwards. Nudging does not work: on a 9-wide grid with
        /// spacing 3-4 the only partition of 8 that respects both bounds is 4+4, so every
        /// jittered candidate gets rejected and every seed yields an identical city. Choosing
        /// the segment COUNT first, then distributing the slack, is what produces variety.
        /// </summary>
        static List<int> PickArterials(int size, int minSpacing, int maxSpacing, System.Random rng)
        {
            var span = size - 1;
            var lines = new List<int> { 0 };
            if (span <= 0)
                return lines;

            // A partition into n segments exists exactly when n*min <= span <= n*max.
            var minSegments = Mathf.CeilToInt(span / (float)maxSpacing);
            var maxSegments = span / minSpacing;

            if (maxSegments < minSegments)
            {
                // The bounds cannot tile this span exactly - fall back to an even split.
                var count = Mathf.Max(1, minSegments);
                for (var i = 1; i <= count; i++)
                    lines.Add(Mathf.RoundToInt(i * span / (float)count));
                return lines;
            }

            // Average two draws so the segment count leans toward the middle of the range.
            // A flat draw hits the extremes too often, and both extremes are degenerate: the
            // maximum makes every block the minimum width (a uniform grid of thin strips) and
            // the minimum makes them all maximum width.
            var segments = (rng.Next(minSegments, maxSegments + 1)
                          + rng.Next(minSegments, maxSegments + 1)) / 2;

            var parts = new int[segments];
            for (var i = 0; i < segments; i++)
                parts[i] = minSpacing;

            // Hand the leftover cells out in a shuffled order so the wide blocks are not
            // always at the same end of the street.
            var order = new int[segments];
            for (var i = 0; i < segments; i++)
                order[i] = i;
            for (var i = segments - 1; i > 0; i--)
            {
                var j = rng.Next(i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }

            var remainder = span - segments * minSpacing;
            var cursor = 0;
            while (remainder > 0)
            {
                var index = order[cursor % segments];
                if (parts[index] < maxSpacing)
                {
                    parts[index]++;
                    remainder--;
                }
                cursor++;
            }

            var position = 0;
            foreach (var part in parts)
            {
                position += part;
                lines.Add(position);
            }

            return lines;
        }
    }
}
