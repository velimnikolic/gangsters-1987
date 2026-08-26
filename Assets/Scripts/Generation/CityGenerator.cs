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

        /// <summary>
        /// Read by BankVisitorDirector: which car each customer drives, which gate it comes in
        /// through, how long it stays. Its own offset so that tuning the bank's traffic cannot
        /// shift what the fleet or the ordinary population draws.
        /// </summary>
        public const int BankVisitors = 16_000;

        /// <summary>
        /// Read by IndustrialLotPlanner and IndustrialLotBuilder for the yard dressing: which
        /// ground is apron and which is bulk store, and what stands on it.
        ///
        /// Its own offset for a reason worth stating, because the obvious reading says to put
        /// this on Industrial with the rest of the works. The halls come out of BlockBuilder's
        /// SHARED Buildings stream, so a draw taken while dressing a yard would move the halls
        /// of every block after it. Tuning what stands in a works must not be able to move what
        /// the works is built of.
        /// </summary>
        public const int IndustrialLot = 17_000;

        /// <summary>
        /// Read by SchoolBusDirector: where the pickup stops fall, how the roster is split
        /// between them, and every per-child timer. Its own offset so that tuning the school
        /// run cannot shift what traffic, the crowd or the police draw - the run is the newest
        /// system in the city and the one most likely to be retuned.
        /// </summary>
        public const int School = 18_000;

        /// <summary>
        /// Read by SchoolParentDirector - the parents using the school's forecourt. Kept off the
        /// School stream above on purpose: the bus round and the parents share a building and
        /// nothing else, and a change to one must not relay the other's arrivals.
        /// </summary>
        public const int SchoolParents = 19_000;

        /// <summary>
        /// Read by StreetPropPlacer for the street lamps alone. The lamps used to draw from the
        /// Props stream, which meant the number of lamps in the city decided where every kerbside
        /// tree stood; retuning the lighting relaid the greenery. They are placed by geometry now
        /// rather than by chance, so the only draw left is which prefab out of the bag - but it
        /// gets its own stream anyway, because the point of this list is that one subsystem's
        /// draw count is never another's business.
        /// </summary>
        public const int Lamps = 20_000;

        /// <summary>
        /// Read by PortLayout, shared by BlockBuilder and GroundPlacer for the fourth time in
        /// this list and for the fourth identical reason: the port plans its own quay, water
        /// and pads, and the ground has to sink water exactly where the dresser moors the ship.
        /// </summary>
        public const int Port = 21_000;

        /// <summary>
        /// Read by PortDirector: which rig each docker is, where on the quay the shift starts,
        /// and every per-worker walk-and-idle timer. Its own offset so that tuning the shift
        /// cannot shift what the crowd, the police or the layout draw.
        /// </summary>
        public const int PortWorkers = 22_000;

        /// <summary>
        /// Read by VehicleTinter for the car paint roll, from every place a car appears - the
        /// block placer's marked bays, the works yard, the traffic spawner and the forecourt
        /// visitors.
        ///
        /// Its own offset for the reason the whole list exists, and here it is not a nicety: the
        /// parked-car picker draws from BlockBuilder's SHARED Buildings stream, so a colour roll
        /// taken alongside it would move every building placed after it. Retuning the palette
        /// must not be able to re-lay the city.
        /// </summary>
        public const int VehicleTints = 23_000;

        /// <summary>
        /// Read by ParkLayout - the park's archetype, entrances, path spines and every planting
        /// and prop station. Deterministic in (seed, blockId) alone, the BlockLots contract,
        /// because the dresser, the ground painter and the nav builder all read the SAME plan.
        /// Claiming its own stream also ends ParkDresser's old habit of draining BlockBuilder's
        /// shared Buildings stream, which meant retuning a shrub count re-laid every block built
        /// after the park. Landing that change reshuffles those blocks once, on purpose.
        /// </summary>
        public const int Park = 24_000;

        /// <summary>
        /// Read by PortShipDirector: which ship sails in, how long it lies alongside, the gap
        /// before the next one, and the forklift's little pauses. Kept off PortWorkers for the
        /// SchoolParents reason: the shift and the shipping share a quay and nothing else, and
        /// retuning one must not re-time the other.
        /// </summary>
        public const int PortShips = 25_000;

        /// <summary>
        /// Read by PropertyDirector: the boss name pool, which boss runs each block, and every
        /// business's flavour name and takings. Its own offset for the list's standing reason -
        /// and because this one runs at PLAY over the saved hierarchy, it draws in an order
        /// derived from sorted world positions, never from generation-time state it cannot see.
        /// </summary>
        public const int Ownership = 26_000;

        /// <summary>
        /// Read by RareVehicleFilter for the keep-or-substitute roll that makes the camper
        /// (car-caravan-small) rare. The filter rides along with pickers that deal off
        /// BlockBuilder's SHARED Buildings stream - the VehicleTints situation exactly - so
        /// a rarity roll taken from that stream would move every building placed after it.
        /// Retuning how rare the camper is must not be able to re-lay the city.
        /// </summary>
        public const int RareVehicles = 27_000;

        /// <summary>
        /// Read by RosterSeeder for the personnel ledger's starting six - names, all
        /// sixty-six attribute rolls, loyalties, and which car sits out back. Its own
        /// offset for the list's standing reason: retuning the outfit's opening hand must
        /// not be able to re-lay the city, and a new prop pass must not reshuffle the men.
        /// </summary>
        public const int Personnel = 28_000;

        /// <summary>
        /// Read by CityGenerator.CarveBoulevards - how many avenues the city gets, each one's
        /// axis and each one's position. Its own stream rather than Roads because the draw
        /// count varies with the config's boulevard range and with separation retries, and a
        /// varying draw count inside the Roads stream would re-lay every street in the city
        /// each time the range was retuned. (Introducing this stream at all re-laid every seed
        /// once - the boulevards now precede the BSP and reshape its input rectangles - which
        /// is the same one-time reshuffle SeedOffsets.Park records the precedent for.)
        /// </summary>
        public const int Boulevards = 29_000;

        /// <summary>
        /// Read by GangSeeder for the city's gangs: the player front's pick, each gang's
        /// child seed, and the AI crews' sizes and names. Its own offset for the list's
        /// standing reason - and each gang also draws a child seed up front, so deepening
        /// one gang later can never reshuffle another.
        /// </summary>
        public const int Gangs = 30_000;

        /// <summary>
        /// Read by OrderResolution for the outfit's job rolls. Its own offset for the
        /// list's standing reason, and one that bites harder here than anywhere above:
        /// the strategy layer draws on the SAME city seed the streets were laid from,
        /// so a roll that shared a stream with generation would mean the outcome of
        /// tonight's shakedown depended on how many buildings the city happened to
        /// have. Each job then mixes (seed, day, job id) - see OrderResolution.Mix.
        /// </summary>
        public const int Orders = 31_000;
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

            // The boulevards come first and from their OWN stream - see SeedOffsets.Boulevards.
            // They are full-map-span cuts, so to the BSP below they are simply cuts that have
            // already been made: it runs once per rectangle of land they leave behind.
            var boulevardRng = new System.Random(config.seed + SeedOffsets.Boulevards);
            CarveBoulevards(grid, config, minBlock, boulevardRng, out var columns, out var rows);

            foreach (var (x0, x1) in Intervals(columns, grid.Width))
            foreach (var (z0, z1) in Intervals(rows, grid.Height))
                Subdivide(grid, x0, z0, x1, z1, minBlock, maxBlock, rng);

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
        /// Parallel avenues closer than this read as a divided highway with a strip of city
        /// caught in the median, so positions are rejected against it. Structurally any gap of
        /// one cell is legal - this is an aesthetic floor, not a constraint the tiles need.
        /// </summary>
        const int MinBoulevardSeparation = 4;

        /// <summary>
        /// Carves the city's boulevards: full-map-span dual carriageways, a per-seed count drawn
        /// from config.minBoulevards..maxBoulevards, each on its own randomly chosen axis. Two
        /// on opposite axes cross, and the shared cell accumulates MainRoadAxis.Both - the cue
        /// for the main-by-main crossroads tile.
        ///
        /// Full-span is a load-bearing choice, not a flourish. Because every boulevard runs
        /// edge to edge, its ends are always the map-edge slice case, it can never curve or
        /// taper, and two boulevards can only ever meet at a full crossroads - which is what
        /// keeps RoadTileTable.LookupMain's closed shape set closed. (The single boulevard this
        /// replaces got the same guarantees by being the BSP's depth-0 cut; several boulevards
        /// cannot all be the first cut, so they are carved up front instead and the BSP fills
        /// in the rectangles they leave.)
        ///
        /// Positions are drawn from [minBlock, size-1-minBlock], the same bounds Subdivide uses
        /// for its cuts, so the outer ring stays unpaved and the four corner cells are never
        /// road - MapEdgeGates needs both, see Subdivide's doc.
        ///
        /// Every draw here is from the Boulevards stream, whose count MAY vary (it retries a
        /// position that lands too close to a parallel neighbour) - that is exactly why it is
        /// not the Roads stream, where a varying draw count would re-lay the whole city.
        /// </summary>
        static void CarveBoulevards(CityGrid grid, CityConfig config, int minBlock,
                                    System.Random rng, out List<int> columns, out List<int> rows)
        {
            columns = new List<int>();
            rows = new List<int>();

            var lo = Mathf.Max(0, config.minBoulevards);
            var count = rng.Next(lo, Mathf.Max(lo, config.maxBoulevards) + 1);

            for (var i = 0; i < count; i++)
            {
                var placed = false;
                for (var attempt = 0; attempt < 16 && !placed; attempt++)
                {
                    var northSouth = rng.Next(2) == 0;
                    var taken = northSouth ? columns : rows;
                    var size = northSouth ? grid.Width : grid.Height;

                    // No legal position on this axis at all - a map too narrow to keep the
                    // outer ring unpaved. The retry may still land on the other axis.
                    if (size - minBlock <= minBlock)
                        continue;

                    var position = rng.Next(minBlock, size - minBlock);

                    var clear = true;
                    foreach (var other in taken)
                        if (Mathf.Abs(other - position) < MinBoulevardSeparation)
                            clear = false;
                    if (!clear)
                        continue;

                    if (northSouth)
                        for (var z = 0; z < grid.Height; z++)
                        {
                            grid[position, z] = CellType.Road;
                            grid.SetMainRoad(position, z, northSouth: true);
                        }
                    else
                        for (var x = 0; x < grid.Width; x++)
                        {
                            grid[x, position] = CellType.Road;
                            grid.SetMainRoad(x, position, northSouth: false);
                        }

                    taken.Add(position);
                    placed = true;
                }

                if (!placed)
                    Debug.LogWarning($"[CityGenerator] Could not place boulevard {i + 1} of {count} - " +
                                     "no position clears the separation rule on this map. Carved " +
                                     $"{columns.Count + rows.Count} instead.");
            }
        }

        /// <summary>
        /// The maximal runs of un-carved indices between the sorted cuts - the rectangles of
        /// land, per axis, that the boulevards leave for the BSP. No cuts yields the whole
        /// [0, size-1], so a config with zero boulevards degenerates to the old single-call
        /// Subdivide over the full map.
        /// </summary>
        static IEnumerable<(int lo, int hi)> Intervals(List<int> cuts, int size)
        {
            cuts.Sort();

            var start = 0;
            foreach (var cut in cuts)
            {
                if (cut > start)
                    yield return (start, cut - 1);
                start = cut + 1;
            }

            if (start <= size - 1)
                yield return (start, size - 1);
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
        ///    rectangle, so both its ends land on that rectangle's boundary - which is map edge,
        ///    an earlier cut, or one of the boulevards CarveBoulevards laid before any of this
        ///    ran. A cut that only meets the map edge is still reached: the strips it leaves
        ///    are longer than maxBlock on the perpendicular axis, so mustSplit forces a
        ///    perpendicular cut whose ends meet both, and RoadsAreConnected backstops the lot.
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
        /// THE BOULEVARDS ARE ALREADY CARVED when this runs - CarveBoulevards lays them as
        /// full-map-span cuts first, and this recursion is then run once per rectangle of land
        /// they leave (the single boulevard used to be this recursion's own depth-0 cut, which
        /// worked because exactly one cut spans the whole map; several boulevards cannot all be
        /// that cut). To the four properties above the boulevards are simply cuts that happened
        /// earlier: each rectangle handed in here is bounded by boulevard, map edge or nothing
        /// beyond it, never part-way through one.
        ///
        /// That division of labour CLOSES the set of tile shapes a boulevard can need, which is
        /// why RoadTileTable.LookupMain has no curve or taper case:
        ///
        /// - A boulevard spans the map, so its cells always have both along-axis neighbours
        ///   except at the two ends, where property (4) above applies - the map edge slices it.
        /// - No cut made here can be parallel-adjacent to one: every recursion happens strictly
        ///   inside a rectangle the boulevards bound, and a cut needs minBlock >= 1 cells of
        ///   land before it.
        /// - No cut can touch a boulevard's end cells either, since neither cuts nor boulevards
        ///   land at index 0 or size-1. So an end cell has no side street and never turns.
        /// - Two boulevards can only meet at a full crossroads: both span the map and both sit
        ///   at least minBlock off every edge, so all four neighbours of the shared cell are
        ///   road. That cell carries MainRoadAxis.Both and is the ONE shape this recursion
        ///   never makes - the main-by-main cross.
        /// </summary>
        static void Subdivide(CityGrid grid, int x0, int z0, int x1, int z1,
                              int minBlock, int maxBlock, System.Random rng)
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
                    grid[column, z] = CellType.Road;

                Subdivide(grid, x0, z0, column - 1, z1, minBlock, maxBlock, rng);
                Subdivide(grid, column + 1, z0, x1, z1, minBlock, maxBlock, rng);
            }
            else
            {
                var row = rng.Next(z0 + minBlock, z1 - minBlock + 1);
                for (var x = x0; x <= x1; x++)
                    grid[x, row] = CellType.Road;

                Subdivide(grid, x0, z0, x1, row - 1, minBlock, maxBlock, rng);
                Subdivide(grid, x0, row + 1, x1, z1, minBlock, maxBlock, rng);
            }
        }
    }
}
