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
        public const int Vehicles = 4_000;
        public const int Pedestrians = 5_000;
        public const int Ambient = 6_000;
        public const int Zoning = 7_000;
        public const int BuildingTints = 8_000;
        public const int Ground = 9_000;

        /// <summary>
        /// Read by BlockLots, which both BlockBuilder and GroundPlacer call. Its own offset
        /// rather than either caller's because the lot rectangles have to come out identical
        /// no matter which of them asks first - see BlockLots.
        /// </summary>
        public const int Lots = 10_000;

        /// <summary>
        /// Read by FeatureStrip, shared by the same two callers for the same reason: the strip
        /// moves the rectangle the lots are planned in, so both must agree on it exactly.
        /// </summary>
        public const int FeatureStrips = 11_000;

        /// <summary>
        /// Read by CityWeather. Its own offset so that changing the weather roll cannot shift
        /// any other subsystem's stream - a seed has to keep producing the same city whatever
        /// the sky over it is doing.
        /// </summary>
        public const int Weather = 12_000;

        /// <summary>
        /// Read by IndustrialLayout, shared by BlockBuilder and GroundPlacer for the third time
        /// in this list and for the third identical reason: the works plans its own rows and
        /// carriageways, and the ground has to lay asphalt on exactly the carriageways the halls
        /// were arranged around.
        /// </summary>
        public const int Industrial = 13_000;

        /// <summary>
        /// Read by PedestrianInteractionDirector for the chat pairing rolls. Its own offset so
        /// that tuning conversation odds cannot shift what any per-agent System.Random draws -
        /// the agents themselves are seeded through the Pedestrians stream via the spawner.
        /// </summary>
        public const int PedestrianLife = 14_000;

        /// <summary>
        /// Read by PoliceDirector for the patrol fleet and foot-patrol officers: initial
        /// mid-shift distribution, per-unit patrol budgets and rest timers. Its own offset so
        /// that tuning police counts cannot shift what traffic or civilians draw.
        /// </summary>
        public const int Police = 15_000;
    }

    /// <summary>
    /// Produces the city layout as pure data. Deterministic for a given config.seed.
    /// </summary>
    public static class CityGenerator
    {
        public static CityGrid Generate(CityConfig config)
        {
            var rng = new System.Random(config.seed + SeedOffsets.Roads);

            // The grid IS the city - no margin, no overhang. A street that reaches the outer
            // edge stops there, so the outline stays a clean rectangle. Roads that carried on
            // past the edge were tried and rejected: the stubs read as an unfinished map.
            var grid = new CityGrid(config.gridWidth, config.gridHeight);

            // Everything starts buildable; Subdivide then carves the streets back out of it.
            for (var x = 0; x < grid.Width; x++)
            for (var z = 0; z < grid.Height; z++)
                grid[x, z] = CellType.Block;

            // A gap of s cells between two parallel streets leaves a block s-1 cells wide -
            // the meaning the config has always carried, kept so the existing asset values
            // still mean what their tooltip says.
            var minBlock = Mathf.Max(1, config.minArterialSpacing - 1);
            var maxBlock = Mathf.Max(minBlock, config.maxArterialSpacing - 1);

            Subdivide(grid, 0, 0, grid.Width - 1, grid.Height - 1, minBlock, maxBlock, rng, depth: 0);

            grid.AssignBlockIds();

            // A map too small to hold even one street subdivides into nothing and comes out as
            // a single solid block with no roads at all - which then trips every check below
            // for a reason that has nothing to do with any of them. Caught here first so the
            // message names the actual cause.
            if (grid.BlockCount <= 1)
            {
                Debug.LogError($"[CityGenerator] No street fits in a {config.gridWidth}x{config.gridHeight} " +
                               $"map at a minimum block of {minBlock} cells - the city would be one " +
                               "solid block. Raise the grid size or lower Min Arterial Spacing.");
                return grid;
            }

            // Connected by construction - see Subdivide - so this should never fire. It is kept
            // because an unreachable road cell produces a stream of "Path not found" warnings
            // that looks exactly like a tile rotation bug.
            if (!grid.RoadsAreConnected())
                Debug.LogError("[CityGenerator] Road network has unreachable cells - cars will fail to path.");

            return grid;
        }

        /// <summary>
        /// Cuts one rectangle of buildable land in two with a street, then cuts each half
        /// independently, until every piece left is a block.
        ///
        /// This replaced a lattice - a set of columns each paved the full height of the map and
        /// a set of rows each paved the full width. That is an outer product, so a block's width
        /// depended only on which column strip it was in and its depth only on which row strip,
        /// and the city came out as a spreadsheet: every block in a column exactly as wide as
        /// every other, every block in a row exactly as deep. No spacing value could fix it,
        /// because the uniformity was the SHAPE of the algorithm rather than a parameter of it.
        /// The fix is streets that stop. A cut made in the left half is not continued into the
        /// right half, so the blocks either side of it do not line up.
        ///
        /// Four properties the rest of the pipeline depends on, all guaranteed by the recursion
        /// rather than checked afterwards:
        ///
        /// 1. Every block is a rectangle. BlockBuilder.BlockRect and GroundPlacer reduce a block
        ///    to its bounding box, which is only exact while that holds.
        /// 2. Each side of a block is ENTIRELY street or entirely map edge, never part of each.
        ///    The neighbour across any side is a single cut that spanned the whole of the
        ///    rectangle this block was carved out of, so it covers this block's side completely.
        ///    BlockBuilder.RoadSides relies on this - it probes one corner and generalises.
        /// 3. The street network is connected. A cut runs the full width or height of its
        ///    rectangle, so both its ends land on that rectangle's boundary, and after the very
        ///    first cut every rectangle has at least one street on its boundary.
        /// 4. A road cell with only ONE connection occurs only on the map boundary. That case is
        ///    RoadTileTable's "the map edge sliced this street", drawn as a straight running off
        ///    the map; anywhere inside the city it would read as a road that simply stops. By (3)
        ///    a cut's ends are on street or on the map edge, so no stub can form in the middle.
        ///
        /// The cut position is drawn from [x0 + minBlock, x1 - minBlock], which is never 0 or
        /// size-1. So the outer ring stays unpaved - the city has to read as a rectangle that
        /// the streets run out of, not as an island with a bypass around it - and the four
        /// corner cells in particular are never road, which MapEdgeGates needs because a lane
        /// standing on two edges at once cannot be classified as an entry or an exit.
        ///
        /// THE FIRST CUT IS THE BOULEVARD (depth 0). It is the only cut that spans the whole
        /// map, which is exactly what the city's main axis has to do, so it costs nothing to
        /// take: no extra draw from rng, no change to where anything lands. Every existing seed
        /// produces the identical layout it did before and simply gains a dual carriageway on
        /// the line it was already cutting.
        ///
        /// That choice also CLOSES the set of tile shapes the boulevard can need, which is why
        /// RoadTileTable.LookupMain has no curve or taper case:
        ///
        /// - It spans the map, so a boulevard cell always has both of its along-axis neighbours
        ///   except at the two ends, where property (4) above applies - the map edge slices it.
        /// - No later cut can be parallel-adjacent to it: every recursion happens strictly
        ///   inside one of the two halves, and a cut needs minBlock >= 1 cells of land before it.
        /// - No later cut can touch its end cells either, since no cut lands at index 0 or
        ///   size-1. So an end cell has no side street, and the boulevard never has to turn.
        /// </summary>
        static void Subdivide(CityGrid grid, int x0, int z0, int x1, int z1,
                              int minBlock, int maxBlock, System.Random rng, int depth)
        {
            var width = x1 - x0 + 1;
            var height = z1 - z0 + 1;

            // A cut needs a block's worth of land on both sides of it plus the cell it occupies.
            var canSplitX = width >= 2 * minBlock + 1;
            var canSplitZ = height >= 2 * minBlock + 1;

            // Oversized land must be cut; land already within the target is left alone. Stopping
            // at the first legal size rather than always cutting to the minimum is what leaves
            // the wide blocks wide.
            var mustSplit = width > maxBlock || height > maxBlock;

            if ((!canSplitX && !canSplitZ) || !mustSplit)
                return;

            // Forced whenever only one axis can be cut, or only one is over size. Otherwise the
            // longer axis is the likelier cut, which keeps blocks from degenerating into long
            // splinters while still leaving the occasional narrow one.
            bool splitX;
            if (!canSplitZ) splitX = true;
            else if (!canSplitX) splitX = false;
            else if (width > maxBlock && height <= maxBlock) splitX = true;
            else if (height > maxBlock && width <= maxBlock) splitX = false;
            else splitX = rng.Next(width + height) < width;

            if (splitX)
            {
                // Uniform over every legal position, so the two halves are rarely equal - that
                // draw is the whole source of "one wide, one narrow".
                var column = rng.Next(x0 + minBlock, x1 - minBlock + 1);
                for (var z = z0; z <= z1; z++)
                {
                    grid[column, z] = CellType.Road;
                    if (depth == 0) grid.SetMainRoad(column, z, northSouth: true);
                }

                Subdivide(grid, x0, z0, column - 1, z1, minBlock, maxBlock, rng, depth + 1);
                Subdivide(grid, column + 1, z0, x1, z1, minBlock, maxBlock, rng, depth + 1);
            }
            else
            {
                var row = rng.Next(z0 + minBlock, z1 - minBlock + 1);
                for (var x = x0; x <= x1; x++)
                {
                    grid[x, row] = CellType.Road;
                    if (depth == 0) grid.SetMainRoad(x, row, northSouth: false);
                }

                Subdivide(grid, x0, z0, x1, row - 1, minBlock, maxBlock, rng, depth + 1);
                Subdivide(grid, x0, row + 1, x1, z1, minBlock, maxBlock, rng, depth + 1);
            }
        }
    }
}
