using System.Collections.Generic;
using UnityEngine;
using LivingCity.Data;

namespace LivingCity.Generation
{
    /// <summary>
    /// Builds Chicago-style blocks: an almost unbroken street wall around each lot, with
    /// service alleys cutting through so buildings stand back-to-back in shallow rows rather
    /// than enclosing a dead courtyard.
    ///
    /// The package's building-block prefabs are modular terrace parts - the -front, -back,
    /// -corner and -short suffixes are the pieces of exactly this. That drives four rules:
    ///
    /// 1. Buildings run right up to the pavement. A road tile is 30 units wide but the
    ///    carriageway and pavements occupy only the middle +/-4; the rest is the tile's verge.
    ///    So the buildable rectangle is EXPANDED into the neighbouring road tiles.
    /// 2. Lots are sized at roughly twice a building's depth and separated by alleys, so the
    ///    two rows nearly meet and the leftover interior is a light-well, not a field.
    /// 3. Street-facing runs use the detailed elevations; alley-facing runs use the -back
    ///    pieces. Picking uniformly would put blank walls on the pavement.
    /// 4. Each run starts with a -corner piece and stops short of the next corner, so the four
    ///    sides interlock instead of colliding.
    ///
    /// What a block is FOR comes from CityGrid.ZoneOf, decided in advance by ZonePlanner. The
    /// zone selects a palette, and everything below - which kits are in play, how much yard sits
    /// between buildings, what the landmark is, what litters the interior - comes from it. The
    /// previous design rolled one kit per block instead, which is why a block used to come out
    /// uniformly 4-storey or uniformly 5-storey; see LotKit for what replaced it.
    /// </summary>
    public static class BlockBuilder
    {
        /// <summary>
        /// Closest a building may sit to the road centreline. Pavements are at 4 and street
        /// props at 5.5, so 7.6667 puts walls behind the lamps without overlapping them - the
        /// odd figure is the 40%-wider kerb-to-wall band, see CityConfig.sidewalkWidth.
        ///
        /// This is the DEFAULT, not the live value - ClearanceFor is what BlockBuilder and
        /// GroundPlacer actually hand to BlockRect, and it answers CityConfig.sidewalkWidth for
        /// every zone but the car park. StreetPropPlacer's prop line (5.5) is an offset from the
        /// road centreline and does not depend on it; moving the building line only widens the
        /// gap between the props and the wall, which GroundPlacer's apron paves either way.
        /// </summary>
        public const float SidewalkClearance = 7.6667f * CityGrid.TileScale;

        /// <summary>
        /// The same default for a side facing the dual carriageway. Its pavements are at 7.25
        /// and its props at 8.5, so 10.625 stands in the same relation to them that 7.6667
        /// does to a street's 4 and 5.5. Mirrors CityConfig.mainSidewalkWidth, the live value.
        /// </summary>
        public const float MainSidewalkClearance = 10.625f * CityGrid.TileScale;

        /// <summary>
        /// Gap left in a whole-block street wall so the courtyard is reachable. A subdivided
        /// block gets its access from the alleys between lots; a single perimeter ring would
        /// otherwise seal its yard completely.
        /// </summary>
        const float PassageWidth = 5f;

        /// <summary>
        /// Shortest buildable street run worth breaching with a passage. 40 lets a 2-cell
        /// block's ~50m run qualify while a 1-cell block's ~20m run keeps its unbroken wall.
        /// </summary>
        const float PassageMinRun = 40f;

        /// <summary>
        /// Frontage one street-side car park takes out of a terrace. The bays inside it are
        /// ParkingLayout's business - see there for why they are 2.7 x 5.6 and not the 3.2 x 11
        /// they used to be.
        /// </summary>
        const float ParkingLotWidth = 24f;

        /// <summary>
        /// Share of bays left empty. Down from 0.3: the old layout needed the gaps to disguise how
        /// far apart the rows were, and a real lot in a working city is close to full.
        /// </summary>
        const float EmptyBayChance = 0.15f;

        /// <summary>
        /// Cars in a landmark's forecourt bay. The bay itself is as wide as the building - a
        /// half-scale station fronts four or five stalls - but two or three patrol cars say
        /// "police station"; a full rank says "impound lot".
        /// </summary>
        const int LandmarkForecourtMaxCars = 3;

        /// <summary>
        /// The school's share of the same idea, and lower because its yard is smaller in the
        /// only way that counts: the bus berth takes 11.5m of a 24.9m frontage, leaving four
        /// bays rather than the bank's nine. One parked car says "people drive here" and still
        /// leaves three bays for the parents SchoolParentDirector actually drives in.
        /// </summary>
        const int SchoolForecourtMaxCars = 1;

        /// <summary>
        /// The dealership's showroom stand-in. A name test like the station's and the bank's,
        /// but a local const rather than an Entities marker: the forecourt is static stock,
        /// nothing at runtime ever needs to find it.
        /// </summary>
        const string SalonPrefabName = "building-carwash";

        /// <summary>Pavement between the forecourt bays and the recessed landmark's door.
        /// Public for PoliceDirector, which reconstructs the forecourt band's depth to aim
        /// its kerb-point search past it.</summary>
        public const float LandmarkForecourtWalkway = 1f;

        /// <summary>
        /// Chance the slot beside a street corner draws from a cornerPreferred group - the
        /// tavern or the corner store. High on purpose: it is what a period block looks like,
        /// and there are only four corners to a lot.
        /// </summary>
        const float CornerRetailChance = 0.6f;

        /// <summary>
        /// Chance one slot borrows the palette of a block across the street instead of its own.
        /// A shop at the end of a housing terrace, a lone house wedged between works - this is
        /// what stops zone boundaries falling exactly on the kerb, which is the other way a
        /// generated city gives itself away.
        /// </summary>
        const float NeighbourBleedChance = 0.15f;

        /// <summary>
        /// Frontage one parking bay takes out of an ALLEY-facing run, as opposed to the 24m one
        /// that fronts a street. Half the size because it is a gap in a back elevation, not a
        /// forecourt: at 2.7m a stall this is four cars, which is what fits behind a block.
        /// </summary>
        const float AlleyParkingWidth = 12f;

        /// <summary>
        /// Pitch of the grid BuildInterior walks over whatever ground the lots left. Roughly the
        /// widest piece of alley furniture, so consecutive cells do not fight each other for the
        /// same square metre and Place's rejection stays the exception.
        /// </summary>
        const float AlleyPropStep = 3.5f;

        /// <summary>
        /// Chance a free cell of that grid actually gets something. Still under 1 - filling
        /// every cell at AlleyPropStep turns the alley into a solid wall of bins - but up from
        /// 0.32, which with the reweighted AlleyKit is what doubles the rubbish behind the
        /// blocks rather than merely reshuffling which prop stands where.
        ///
        /// Global rather than per-palette, so the Hospital and Bank interiors - which draw
        /// PocketParkKit through the same field - get correspondingly more benches and trees.
        /// That is a bench every other cell of a courtyard, which is what a courtyard looks
        /// like, so it is accepted rather than worked around.
        /// </summary>
        const float AlleyPropChance = 0.50f;

        /// <summary>
        /// How close a placed box must start to an edge of the block rect to count as part of
        /// the ring on that side. One party wall plus a little - the pass-two edge spread moves
        /// buildings ALONG a run, never across it, so the outward face stays flush.
        /// </summary>
        const float RingEdgeTolerance = 1.5f;

        /// <summary>
        /// Smallest run leftover worth turning into a parking bay instead of spreading through
        /// the joints - two stalls and change. Anything under this disappears into the joints;
        /// anything over it would need more than maxFillerGap per joint and start reading as
        /// gaps in the wall.
        /// </summary>
        const float MinResidueBay = 6f;

        /// <summary>
        /// Chance one cell of a pocket park's grid gets a piece of furniture. Denser than the
        /// alley grid - a park is furnished on purpose, an alley by accretion.
        /// </summary>
        const float PocketParkPropChance = 0.45f;

        /// <summary>
        /// One scatter attempt per this much ground, before density scales it. Attempts, not
        /// placements: anything landing on a building is dropped.
        /// </summary>
        const float ScatterAreaPerAttempt = 25f;

        public static List<GameObject> Build(
            CityGrid grid,
            PrefabDatabase prefabs,
            CityConfig config,
            Transform parent,
            SpawnPrefab spawn = null,
            List<BuildingTinter.Target> tintTargets = null,
            List<Bounds> gateKeepOuts = null,
            ParkConfig parkConfig = null,
            IReadOnlyDictionary<Vector2Int, GameObject> roadTilesByCell = null)
        {
            var placed = new List<GameObject>();
            spawn ??= RoadNetworkBuilder.RuntimeSpawn;

            // Buildings only, with the tier their group put them in. `placed` cannot serve:
            // it collects everything the block placer spawns - parked cars, trees, kiosks,
            // bins - and most of the pack shares the atlas material, so a tinter fed the flat
            // list paints the trees too. Optional so callers that never tint need not care.
            tintTargets ??= new List<BuildingTinter.Target>();

            // The approaches of every compound gate in the city, published for the passes that
            // run AFTER the blocks - StreetPropPlacer reads it so no tree stands in the one
            // hole a wall has. Optional for the same reason as tintTargets.
            gateKeepOuts ??= new List<Bounds>();

            if (prefabs.zonePalettes == null || prefabs.zonePalettes.Length == 0)
            {
                Debug.LogWarning("[BlockBuilder] No zone palettes in the PrefabDatabase - run " +
                                 "Tools/City/Create or Refresh Config Assets.");
                return placed;
            }

            var rng = new System.Random(config.seed + SeedOffsets.Buildings);

            // The only per-CITY state in the build, and the only thing that keeps the city to one
            // post office - see UniqueBuildings for why it is neither per-block nor per-lot. Reset
            // here rather than at declaration because the set outlives a build.
            UniqueBuildings.Reset(prefabs);

            // Every parked vehicle in the city now comes from here - kerbside parking is gone, so
            // PrefabDatabase.parkedCarGroups feeds marked bays only, off this block's own stream.
            //
            // The filter that keeps the camper rare, built ONCE and handed to every picker in the
            // build for the reason the tinter below is: made per lot or per landmark it would
            // restart its stream and deal every forecourt the same first roll. It owns that
            // stream (SeedOffsets.RareVehicles) rather than drawing from `rng` for the same
            // reason the tinter owns VehicleTints - see the comment on it.
            var rare = new RareVehicleFilter(prefabs, config);
            var parking = new VehiclePicker(prefabs.parkedCarGroups, rng, rare);

            // The paint on those vehicles. Built here, once, and carried alongside the picker to
            // every bay in the city so that one stream colours them all - a tinter made per lot
            // would restart its sequence on every block and give each car park the same row of
            // colours. It owns that stream rather than drawing from `rng`: this one is the shared
            // Buildings stream, and a colour roll taken from it would move every building placed
            // after it. See SeedOffsets.VehicleTints.
            var tinter = new VehicleTinter(prefabs, config);

            for (var blockId = 0; blockId < grid.BlockCount; blockId++)
            {
                var cells = new List<Vector2Int>(grid.CellsInBlock(blockId));
                if (cells.Count == 0)
                    continue;

                var zone = grid.ZoneOf(blockId);
                var palette = prefabs.PaletteFor(zone);
                if (palette == null)
                {
                    Debug.LogWarning($"[BlockBuilder] No palette for zone {zone} - block {blockId} left bare.");
                    continue;
                }

                var bleed = PickNeighbourPalette(grid, prefabs, blockId, zone, rng);

                BuildBlock(grid, cells, blockId, palette, bleed, parking, tinter, prefabs, config,
                           parent, spawn, rng, placed, tintTargets, gateKeepOuts,
                           parkConfig, roadTilesByCell);
            }

            return placed;
        }

        /// <summary>
        /// The buildable footprint of a block in world XZ, expanded out of the block cells and
        /// into the surrounding road tiles as far as SidewalkClearance.
        ///
        /// Shared with GroundPlacer so the concrete slab lands under exactly the same area the
        /// buildings occupy - if the two computed it separately they would drift apart and
        /// leave a strip of the road tile's grass verge showing along the street wall. Both
        /// callers must therefore pass the SAME sidewalkWidth; both read it off CityConfig.
        ///
        /// The clearance is resolved PER SIDE, because a side facing the dual carriageway needs
        /// a deeper one - the avenue's pavements sit at 7.25 from its centreline against a
        /// street's 4, so a facade set back by the ordinary 7 would stand on it. A block can
        /// face the avenue on one side and side streets on the other three, so this cannot be
        /// one value per block.
        ///
        /// Deciding it in here rather than at the two call sites is what keeps the contract
        /// above honest: both callers pass the same PAIR, and the per-side choice is then made
        /// once, in one place, from the same grid. Two callers each deriving "is this side the
        /// avenue?" separately is exactly the drift this function exists to prevent.
        /// </summary>
        public static (Vector2 min, Vector2 max) BlockRect(
            CityGrid grid, List<Vector2Int> cells,
            float sidewalkWidth = SidewalkClearance,
            float mainSidewalkWidth = MainSidewalkClearance)
        {
            var half = CityGrid.CellSize * 0.5f;

            var minCell = new Vector2Int(int.MaxValue, int.MaxValue);
            var maxCell = new Vector2Int(int.MinValue, int.MinValue);
            foreach (var cell in cells)
            {
                minCell = Vector2Int.Min(minCell, cell);
                maxCell = Vector2Int.Max(maxCell, cell);
            }

            var min = new Vector2(grid.CellToWorld(minCell).x - half, grid.CellToWorld(minCell).z - half);
            var max = new Vector2(grid.CellToWorld(maxCell).x + half, grid.CellToWorld(maxCell).z + half);

            float Reach(int x, int z) =>
                half - (grid.IsMainRoad(x, z) ? mainSidewalkWidth : sidewalkWidth);

            if (grid.IsRoad(minCell.x - 1, minCell.y)) min.x -= Reach(minCell.x - 1, minCell.y);
            if (grid.IsRoad(maxCell.x + 1, maxCell.y)) max.x += Reach(maxCell.x + 1, maxCell.y);
            if (grid.IsRoad(minCell.x, minCell.y - 1)) min.y -= Reach(minCell.x, minCell.y - 1);
            if (grid.IsRoad(maxCell.x, maxCell.y + 1)) max.y += Reach(maxCell.x, maxCell.y + 1);

            return (min, max);
        }

        /// <summary>
        /// Which sides of a block actually face a street.
        ///
        /// Since the map boundary cuts the outermost streets, a block can sit against the edge
        /// of the world with no road on that side at all. BlockLots alone cannot tell - it only
        /// knows a lot's position within the block - so without this the buildings along that
        /// side would be given shopfront elevations facing empty space, which is exactly the
        /// "windows must face the street" rule inverted.
        ///
        /// Probes the same four neighbours as BlockRect, and for the same reason they agree: a
        /// side of a block is entirely street or entirely map edge, never part of each, so one
        /// corner probe settles the whole side. That is not because streets run the full width
        /// of the map - since CityGenerator.Subdivide they do not - but because whatever lies
        /// across a side is a single cut that spanned the whole of the rectangle this block was
        /// carved out of, and therefore covers this block's side completely.
        /// </summary>
        public static Sides RoadSides(CityGrid grid, List<Vector2Int> cells)
        {
            var minCell = new Vector2Int(int.MaxValue, int.MaxValue);
            var maxCell = new Vector2Int(int.MinValue, int.MinValue);
            foreach (var cell in cells)
            {
                minCell = Vector2Int.Min(minCell, cell);
                maxCell = Vector2Int.Max(maxCell, cell);
            }

            var sides = Sides.None;
            if (grid.IsRoad(minCell.x - 1, minCell.y)) sides |= Sides.West;
            if (grid.IsRoad(maxCell.x + 1, maxCell.y)) sides |= Sides.East;
            if (grid.IsRoad(minCell.x, minCell.y - 1)) sides |= Sides.South;
            if (grid.IsRoad(maxCell.x, maxCell.y + 1)) sides |= Sides.North;
            return sides;
        }

        /// <summary>
        /// Which sides of a block lie against the map boundary - the exact complement question
        /// to RoadSides, answered by the same four corner probes for the same single-cut
        /// reason: whatever lies across a side covers the whole side, so off-grid on one
        /// corner is off-grid all the way along. The port is the caller: its water goes on
        /// these sides and its wall on the others.
        /// </summary>
        public static Sides EdgeSides(CityGrid grid, List<Vector2Int> cells)
        {
            var minCell = new Vector2Int(int.MaxValue, int.MaxValue);
            var maxCell = new Vector2Int(int.MinValue, int.MinValue);
            foreach (var cell in cells)
            {
                minCell = Vector2Int.Min(minCell, cell);
                maxCell = Vector2Int.Max(maxCell, cell);
            }

            var sides = Sides.None;
            if (!grid.InBounds(minCell.x - 1, minCell.y)) sides |= Sides.West;
            if (!grid.InBounds(maxCell.x + 1, maxCell.y)) sides |= Sides.East;
            if (!grid.InBounds(minCell.x, minCell.y - 1)) sides |= Sides.South;
            if (!grid.InBounds(maxCell.x, maxCell.y + 1)) sides |= Sides.North;
            return sides;
        }

        /// <summary>
        /// Which of those sides face the dual carriageway rather than an ordinary street. Always
        /// a subset of RoadSides - the boulevard is a road to every probe in the file.
        ///
        /// Same four corner probes for the same reason RoadSides gives, and read only as a
        /// tie-break in ChooseLandmarkFront: the front a landmark wants is the longest one, and
        /// this settles which of two equally long ones it takes. Deliberately not a first-order
        /// term. A short stub on the avenue is a worse address than a full block face on a
        /// through street, and ranking the boulevard above length would pick the stub.
        /// </summary>
        public static Sides MainRoadSides(CityGrid grid, List<Vector2Int> cells)
        {
            var minCell = new Vector2Int(int.MaxValue, int.MaxValue);
            var maxCell = new Vector2Int(int.MinValue, int.MinValue);
            foreach (var cell in cells)
            {
                minCell = Vector2Int.Min(minCell, cell);
                maxCell = Vector2Int.Max(maxCell, cell);
            }

            var sides = Sides.None;
            if (grid.IsMainRoad(minCell.x - 1, minCell.y)) sides |= Sides.West;
            if (grid.IsMainRoad(maxCell.x + 1, maxCell.y)) sides |= Sides.East;
            if (grid.IsMainRoad(minCell.x, minCell.y - 1)) sides |= Sides.South;
            if (grid.IsMainRoad(maxCell.x, maxCell.y + 1)) sides |= Sides.North;
            return sides;
        }

        /// <summary>
        /// How far in from the road centreline a block's own surface stops - what BlockRect is
        /// handed as its sidewalkWidth.
        ///
        /// CityConfig.sidewalkWidth (7) for anything with a street wall: the band between the
        /// pavement edge and the building line is where StreetPropPlacer stands its lamps and
        /// trees. It used to be left as the road tile's grass and read as a verge; it is now
        /// paved by GroundPlacer's apron, so what this value sets is only where the WALL starts,
        /// not where the paving stops.
        ///
        /// A car park has no street wall. Its asphalt and its fence run out to the pavement edge
        /// instead, which is why StreetPropPlacer skips its kerb rather than stand props inside
        /// the lot - and why the apron has nothing to add there.
        ///
        /// BlockRect's contract is unchanged: BlockBuilder and GroundPlacer must pass the SAME
        /// value for a given block, which is why this is one function and not two constants.
        /// </summary>
        public static float ClearanceFor(PrefabDatabase.ZonePalette palette, CityConfig config) =>
            palette != null && palette.carRows ? CityGrid.SidewalkOffset : config.sidewalkWidth;

        /// <summary>
        /// The same answer for a side facing the dual carriageway - BlockRect's second clearance.
        ///
        /// The car park still runs out to the pavement, it is just a wider pavement there
        /// (7.25 against 4), so the shape of the rule is identical and only the two constants
        /// change. Kept as its own function for the same reason as ClearanceFor: both callers
        /// have to arrive at the same number, and one function is how that is guaranteed.
        /// </summary>
        public static float MainClearanceFor(PrefabDatabase.ZonePalette palette, CityConfig config) =>
            palette != null && palette.carRows ? CityGrid.MainSidewalkOffset : config.mainSidewalkWidth;

        /// <summary>
        /// True when this cell belongs to a block the car-park palette builds. False for a road
        /// cell or one off the map - BlockIdAt returns NoBlock for both.
        ///
        /// Cell-level rather than block-level because the two callers that need it walk ROAD
        /// cells and ask what lies across each kerb, not blocks.
        /// </summary>
        public static bool IsCarParkAt(CityGrid grid, PrefabDatabase prefabs, int x, int z)
        {
            var blockId = grid.BlockIdAt(x, z);
            if (blockId < 0)
                return false;

            var palette = prefabs.PaletteFor(grid.ZoneOf(blockId));
            return palette != null && palette.carRows;
        }

        /// <summary>
        /// A palette from across the street, for the occasional borrowed slot. Null when the
        /// block has no neighbours with buildings of their own to lend - a park has nothing to
        /// contribute to the terrace next door.
        /// </summary>
        static PrefabDatabase.ZonePalette PickNeighbourPalette(
            CityGrid grid, PrefabDatabase prefabs, int blockId, BlockZone ownZone, System.Random rng)
        {
            var candidates = new List<PrefabDatabase.ZonePalette>();

            foreach (var neighbour in grid.NeighbourBlocks(blockId))
            {
                var zone = grid.ZoneOf(neighbour);
                if (zone == ownZone)
                    continue;

                var palette = prefabs.PaletteFor(zone);
                if (palette != null && palette.BuildsPerimeter)
                    candidates.Add(palette);
            }

            return candidates.Count == 0 ? null : candidates[rng.Next(candidates.Count)];
        }

        /// <summary>
        /// Carried down through the lots so a landmark is placed at most once per block. A
        /// mutable holder rather than a return value because the walk that consumes it is four
        /// levels down and any lot may be the one that has room.
        /// </summary>
        sealed class BlockState
        {
            public GameObject Landmark;
            public float LandmarkScale = 1f;

            // The frontage the landmark is RESERVED, chosen for the whole block before a single
            // lot is built - see ChooseLandmarkFront. Lot index and side index (0 South, 1 East,
            // 2 North, 3 West, matching BuildLot's sides array), or -1 for no reservation.
            //
            // Without it the landmark went to the first street side of the first lot that could
            // hold it, walked S-E-N-W over lots ordered column-major from the block's minimum -
            // so on a subdivided block it always took the south-west lot's south run, whatever
            // else the block fronted. That is how the city's one bank ended up addressing a side
            // street with its back to the frontage the block was actually built around.
            public int LandmarkLot = -1;
            public int LandmarkSide = -1;

            // Perimeter buildings this block may still take, the landmark excluded. Spent in
            // walk order across every lot and side, which is what maxPerimeterBuildings needs
            // to mean "per block" rather than "per run".
            public int PerimeterBudget;
        }

        /// <summary>
        /// Rejected BUILDING placements of the block currently being built - see the reset in
        /// BuildBlock for what counts. A static rather than a parameter because Place sits
        /// under every placement path and threading a counter through all of them buys nothing;
        /// generation is single-threaded.
        /// </summary>
        static int placeRejections;

        static void BuildBlock(
            CityGrid grid,
            List<Vector2Int> cells,
            int blockId,
            PrefabDatabase.ZonePalette palette,
            PrefabDatabase.ZonePalette bleedPalette,
            VehiclePicker parking,
            VehicleTinter tinter,
            PrefabDatabase prefabs,
            CityConfig config,
            Transform parent,
            SpawnPrefab spawn,
            System.Random rng,
            List<GameObject> placed,
            List<BuildingTinter.Target> tints,
            List<Bounds> gateKeepOuts,
            ParkConfig parkConfig,
            IReadOnlyDictionary<Vector2Int, GameObject> roadTilesByCell)
        {
            var (min, max) = BlockRect(grid, cells,
                                       ClearanceFor(palette, config),
                                       MainClearanceFor(palette, config));

            if (max.x <= min.x || max.y <= min.y)
                return;

            // A zone may keep its own vehicles - the police car outside the police station is
            // what identifies the building at a glance. Falls back to the city-wide list.
            if (palette.HasOwnParkedCars)
                parking = new VehiclePicker(palette.parkedCars, rng, parking.Rare);

            // Block-scoped rather than per-lot so the scatter pass can see every building, and
            // so two lots either side of an alley cannot overlap each other's corners.
            var occupied = new List<Bounds>();

            // Every stripe of paint on this block, wherever it comes from - the whole-block car
            // park or the short bays cut into a terrace - collected here and drawn as ONE mesh at
            // the end. Threaded down the same way occupied and placed already are, because the
            // walk that produces the street bays is four levels below this.
            var markings = new List<ParkingLayout.Line>();

            var roadSides = RoadSides(grid, cells);

            // A works is laid out, not built up. Same fork as the park below, and taken earlier
            // because it replaces the WHOLE block: no lots, no feature strip, no interior, no
            // scatter. Those passes are all about wrapping buildings round a lot and furnishing
            // what is left, and a factory site is the other way round - the roads come first and
            // the halls stand along them. GroundPlacer forks on the same flag.
            if (palette.industrialYard)
            {
                IndustrialDresser.Build(min, max, roadSides, blockId, palette, prefabs, config,
                                        parking, tinter, parent, spawn, rng, occupied, markings,
                                        placed, tints, gateKeepOuts);

                var works = ParkingMarkings.Emit(markings, prefabs.lineMaterial,
                                                 $"parking_lines_{grid.ZoneOf(blockId)}_{blockId}", parent);
                if (works)
                    placed.Add(works);

                return;
            }

            // A port is laid out the same way - the third whole-block replacement, after the
            // works and the park. GroundPlacer forks on the same flag to sink the water and
            // lay the quay on the rectangles PortLayout replays. The quay side and the water
            // seams are grid facts, computed here and passed down, because the dresser has
            // no grid of its own.
            if (palette.portYard)
            {
                var quaySide = ZonePlanner.PortSideOf(grid);
                var continuation = quaySide != Sides.None
                    ? PortLayout.ContinuationFor(grid, cells, quaySide,
                                                 config.sidewalkWidth, config.mainSidewalkWidth)
                    : default;

                PortDresser.Build(min, max, roadSides, EdgeSides(grid, cells), blockId,
                                  palette, prefabs, config, parking, parent, spawn, rng,
                                  occupied, markings, placed, tints, gateKeepOuts,
                                  quaySide, continuation);
                return;
            }

            // The strip comes off the rect BEFORE the lots are planned in it, so every pass
            // below - lots, interior, scatter - already works to the pulled-in building line.
            // GroundPlacer shrinks by the same strip, from the same seed; see FeatureStrip.
            var strip = FeatureStrip.For(min, max, roadSides,
                                         palette.featureStrip && palette.BuildsPerimeter,
                                         config, blockId);
            var buildMin = min;
            var buildMax = max;
            FeatureStrip.Shrink(strip, ref buildMin, ref buildMax);

            if (palette.BuildsPerimeter)
            {
                var state = new BlockState
                {
                    Landmark = PickLandmark(palette, grid.ForcedLandmarkOf(blockId), rng),
                    LandmarkScale = palette.landmarkScale > 0f ? palette.landmarkScale : 1f,
                    PerimeterBudget = palette.maxPerimeterBuildings > 0
                        ? palette.maxPerimeterBuildings
                        : int.MaxValue,
                };

                // Counted across the ring passes only. The interior and scatter passes reject
                // by DESIGN - that is how a flat grid covers an arbitrary interior - but a
                // rejected BUILDING means the packer and the occupancy test disagreed about the
                // same wall, and each one is a hole in a street elevation.
                placeRejections = 0;

                // Not drawn from this block's rng: GroundPlacer lays its mosaic on exactly these
                // rectangles and has no way to replay the draws spent above. See BlockLots.
                var lots = BlockLots.Plan(buildMin, buildMax, roadSides, palette.maxLotsPerAxis,
                                          config.alleyWidth, config.seed, blockId);

                // Which frontage the landmark gets, decided across the WHOLE block before any of
                // it is built - a lot can only see its own rectangle, and the answer depends on
                // all of them. Draws no rng, so it cannot move a building.
                if (state.Landmark)
                    ChooseLandmarkFront(lots, MainRoadSides(grid, cells),
                                        out state.LandmarkLot, out state.LandmarkSide);

                for (var i = 0; i < lots.Count; i++)
                    BuildLot(i, lots[i], palette, bleedPalette, state, parking, tinter, prefabs,
                             config, parent, spawn, rng, occupied, markings, placed, tints);

                if (placeRejections > 0)
                    Debug.LogWarning($"[BlockBuilder] Block {blockId} ({grid.ZoneOf(blockId)}): " +
                                     $"{placeRejections} building placement(s) rejected for overlap - " +
                                     "each one is a gap in a run that expected a building there.");

                // After the buildings, so the pocket park's furniture and the stall occupancy
                // tests see the finished rows beside the band.
                if (strip.Has)
                    BuildFeatureStrip(min, max, buildMin, buildMax, strip, palette, parking, tinter,
                                      prefabs, config, parent, spawn, rng, occupied, markings, placed);
            }

            if (palette.carRows)
                BuildCarPark(min, max, roadSides, palette, parking, tinter, prefabs,
                             parent, spawn, rng, occupied, markings, placed);

            // Before the scatter, so the alley furniture gets first claim on the interior and the
            // trees and mailboxes fall into whatever is left rather than into the alley itself.
            if (palette.BuildsPerimeter)
                BuildInterior(buildMin, buildMax, palette, prefabs, config,
                              parent, spawn, rng, occupied, placed);

            // A park is laid out, not scattered - the fourth whole-block layout, planned by
            // ParkLayout on its own SeedOffsets.Park stream. It takes NOTHING from this block's
            // shared rng, so retuning the park cannot move a building anywhere else. Every other
            // zone gets the uniform scatter, which is right for a yard and wrong for a park.
            if (palette.groundIsTilePerCell)
                ParkDresser.Build(grid, cells, blockId, palette, prefabs, config, parkConfig,
                                  roadTilesByCell, parent, spawn, occupied, placed, gateKeepOuts);
            else
                BuildScatter(buildMin, buildMax, palette, parent, spawn, rng, occupied, placed);

            var paint = ParkingMarkings.Emit(markings, prefabs.lineMaterial,
                                             $"parking_lines_{grid.ZoneOf(blockId)}_{blockId}", parent);
            if (paint)
                placed.Add(paint);
        }

        static GameObject PickLandmark(
            PrefabDatabase.ZonePalette palette, int forcedLandmark, System.Random rng)
        {
            if (palette.landmarks == null || palette.landmarks.Length == 0)
                return null;

            // One block per city can be TOLD which landmark it builds instead of being asked.
            // ZonePlanner marks it when the city owes itself a particular building and a
            // probability will not do - see ZonePalette.requiredLandmark. Deliberately ahead of
            // the chance roll and of the draw from the bag, because a required landmark that
            // still had to survive landmarkChance would not be required.
            if (forcedLandmark >= 0 && forcedLandmark < palette.landmarks.Length)
            {
                var required = palette.landmarks[forcedLandmark];
                return UniqueBuildings.IsSpent(required) ? null : required;
            }

            if (rng.NextDouble() >= palette.landmarkChance)
                return null;

            // Drawn first and tested after, so a capped landmark that is already built costs the
            // same two rng draws as one that is not and the seed's sequence does not depend on
            // what the city happens to contain yet.
            //
            // Redundant today - each civic zone is one block by quota, and one block places its
            // landmark once - and kept anyway, because it puts the ceiling on the PREFAB. Raise a
            // maxBlocks, or list one building as the landmark of two palettes, and the zoning
            // rule quietly stops holding while this one still does.
            var index = rng.Next(palette.landmarks.Length);

            // The guaranteed landmark is placed ONLY by ZonePlanner's forced marks, whose count
            // and spread are the whole policy - see FulfilGuaranteedLandmarks. Letting the
            // random draw deliver it too would spend the allowance in build order, which is
            // flood-fill order, which is one corner of the map. Refused after the draw, so the
            // rng sequence is the same whether the index is guaranteed or not.
            if (index == palette.guaranteedLandmark)
                return null;

            var pick = palette.landmarks[index];
            return UniqueBuildings.IsSpent(pick) ? null : pick;
        }

        /// <summary>
        /// Reserves the frontage the block's landmark will stand on: the LONGEST run of lot edge
        /// that faces a public street, anywhere on the block.
        ///
        /// The candidates are the lot street flags and nothing else, which is what keeps the
        /// building off a back elevation for free - BlockLots leaves South/East/North/West false
        /// for an internal alley and for a side the map boundary cut off, so neither can ever be
        /// picked here however long it is. That is the whole of the "never in an alley" rule; it
        /// needs no test of its own because there is no code path that could break it.
        ///
        /// Length first because frontage is what a landmark is FOR - it is the one building on
        /// the block a passer-by is meant to read at a distance, and a 20m run wedged between two
        /// corner pieces reads as a gap in the terrace instead. The boulevard breaks ties, then
        /// the lot and side indices, so the answer is a pure function of the lot plan and the
        /// same seed keeps producing the same city.
        ///
        /// Measured on the RAW lot side, not on the run left after the corner pieces: those are
        /// sized inside BuildLot from draws this pass must not spend, and a pre-pass that made
        /// them would move every building on the block. It is a proxy, and PlaceLandmark's own
        /// width check is what catches the case where it was the wrong one - see BuildLot for
        /// the fallback that keeps the city's one bank standing when it is.
        /// </summary>
        public static void ChooseLandmarkFront(
            List<BlockLots.Lot> lots, Sides mainSides, out int bestLot, out int bestSide)
        {
            bestLot = -1;
            bestSide = -1;

            var bestLength = 0f;
            var bestMain = false;

            for (var index = 0; index < lots.Count; index++)
            {
                var lot = lots[index];

                for (var side = 0; side < 4; side++)
                {
                    // Same order as BuildLot's sides array: South, East, North, West.
                    var isStreet = side switch
                    {
                        0 => lot.South,
                        1 => lot.East,
                        2 => lot.North,
                        _ => lot.West,
                    };
                    if (!isStreet)
                        continue;

                    var length = side == 0 || side == 2
                        ? lot.Max.x - lot.Min.x
                        : lot.Max.y - lot.Min.y;

                    var main = mainSides.HasFlag(side switch
                    {
                        0 => Sides.South,
                        1 => Sides.East,
                        2 => Sides.North,
                        _ => Sides.West,
                    });

                    // Strictly better on length, or equal on length and better on the avenue.
                    // Never equal on both - the first candidate reached wins, which is the lot
                    // and side order the loops already impose.
                    if (bestLot >= 0
                        && !(length > bestLength
                             || (Mathf.Approximately(length, bestLength) && main && !bestMain)))
                        continue;

                    bestLot = index;
                    bestSide = side;
                    bestLength = length;
                    bestMain = main;
                }
            }
        }

        static void BuildLot(
            int lotIndex,
            BlockLots.Lot lot,
            PrefabDatabase.ZonePalette palette,
            PrefabDatabase.ZonePalette bleedPalette,
            BlockState state,
            VehiclePicker parking,
            VehicleTinter tinter,
            PrefabDatabase prefabs,
            CityConfig config,
            Transform parent,
            SpawnPrefab spawn,
            System.Random rng,
            List<Bounds> occupied,
            List<ParkingLayout.Line> markings,
            List<GameObject> placed,
            List<BuildingTinter.Target> tints)
        {
            // Fresh kits per lot, not per block. Sharing one set of bags across a whole block
            // made its lots march through the same sequence of pieces; rebuilding them here is
            // what makes two lots of the same block look like different developments.
            var kit = new LotKit(palette.groups, rng);
            if (kit.IsEmpty)
                return;

            var bleed = bleedPalette == null ? null : new LotKit(bleedPalette.groups, rng);
            if (bleed != null && bleed.IsEmpty)
                bleed = null;

            var min = lot.Min;
            var max = lot.Max;

            // Walked as one continuous loop so every side starts where the last one ended.
            var sides = new[]
            {
                (origin: new Vector3(min.x, 0f, min.y), along: Vector3.right,   outward: Vector3.back,    length: max.x - min.x, isStreet: lot.South, isOpen: lot.SouthOpen),
                (origin: new Vector3(max.x, 0f, min.y), along: Vector3.forward, outward: Vector3.right,   length: max.y - min.y, isStreet: lot.East,  isOpen: lot.EastOpen),
                (origin: new Vector3(max.x, 0f, max.y), along: Vector3.left,    outward: Vector3.forward, length: max.x - min.x, isStreet: lot.North, isOpen: lot.NorthOpen),
                (origin: new Vector3(min.x, 0f, max.y), along: Vector3.back,    outward: Vector3.left,    length: max.y - min.y, isStreet: lot.West,  isOpen: lot.WestOpen),
            };

            // Corners first, so each run knows how much room the corners at its two ends take.
            // Only terrace kits have corner pieces at all - a detached model is finished on all
            // four elevations and has no corner variant, so those zones simply start at zero.
            //
            // Chosen in two phases rather than placed on sight. Corner i joins sides[i] and
            // sides[(i+3)%4], and it earns a piece by rank: two streets is mandatory, a
            // street/alley junction is preferred - the building beside the mouth of an alley
            // still stands on the street and shows the passer-by its second elevation - and an
            // alley/alley corner gets none, back rows meet by recession instead. Anything
            // touching the map boundary also gets none; there is nobody out there to see it.
            // The ranking exists because small lots cannot afford every corner they qualify
            // for: two corner pieces eat 23.5-28.5m of frontage and the narrowest street piece
            // is 8.1m wide, so on a ~31m side something has to give, and it should be the
            // preferred corner, not the mandatory one.
            var cornerRank = new int[4];
            var cornerGroup = new int[4];
            var cornerPrefab = new GameObject[4];
            var cornerYaw = new float[4];
            var cornerWidth = new float[4];
            var cornerDepth = new float[4];

            for (var i = 0; i < 4; i++)
            {
                var a = sides[i];
                var b = sides[(i + 3) % 4];

                cornerRank[i] = a.isOpen || b.isOpen ? -1
                              : a.isStreet && b.isStreet ? 2
                              : a.isStreet || b.isStreet ? 1
                              : -1;
                if (cornerRank[i] < 0)
                    continue;

                // The draw happens for every RANKED corner whether or not it survives the
                // pruning below - candidacy depends only on the lot plan, so the rng sequence
                // stays a function of the seed alone.
                var group = kit.PickCornerGroup();
                var prefab = group < 0 ? null : kit.PeekCorner(group);

                // A corner piece the city has already used its one of retires the corner, exactly
                // as an empty bag does. Nothing capped is a corner piece today - the Shops group
                // that carries the post office ships none, so PickCornerGroup cannot even return
                // it - and the guard is here so that stays a fact about the database rather than
                // a hole waiting for the first corner variant somebody adds.
                if (!prefab || UniqueBuildings.IsSpent(prefab))
                {
                    cornerRank[i] = -1;
                    continue;
                }

                // One number covers all four corners of the lot, and that is worth spelling out
                // because it is not obvious. The two sides meeting here are sides[i] and
                // sides[i-1], and sides[i-1].outward is always sides[i].outward turned +90 in the
                // same sense, so the bisector of the pair is always YawFor(side.outward) + 45.
                // This line is therefore "bisector - 45", and aligning a piece whose own outer
                // quadrant bisects at beta needs ExtraYawFor = 45 - beta - a per-prefab constant,
                // the same at every corner. What it is NOT is the flat pieces' "turn the front
                // toward the street"; read it as the flat convention and the answer comes out a
                // quarter turn off, which shows as one elevation on its street and one blank.
                //
                // It goes into the footprint measurement too: a quarter-turn swaps x and z.
                var yaw = YawFor(a.outward) + prefabs.ExtraYawFor(prefab);
                var footprint = PrefabBounds.FootprintXZ(prefab, yaw);

                cornerGroup[i] = group;
                cornerPrefab[i] = prefab;
                cornerYaw[i] = yaw;
                cornerWidth[i] = Extent(footprint, a.along);
                cornerDepth[i] = Extent(footprint, a.outward);
            }

            PruneCorners(sides, cornerRank, cornerPrefab, cornerWidth);

            for (var i = 0; i < 4; i++)
            {
                if (!cornerPrefab[i])
                    continue;

                var side = sides[i];
                var centre = side.origin + side.along * (cornerWidth[i] * 0.5f)
                                         - side.outward * (cornerDepth[i] * 0.5f);
                var corner = Place(cornerPrefab[i], centre, cornerYaw[i], cornerWidth[i], cornerDepth[i],
                                   side.along, side.outward, config.partyWallGap, spawn, parent, occupied, placed, prefabs);
                if (corner)
                {
                    UniqueBuildings.Spend(cornerPrefab[i]);
                    tints.Add(new BuildingTinter.Target(corner, kit.GroupAt(cornerGroup[i]).commercial));
                }

                kit.AdvanceCorner(cornerGroup[i]);
            }

            // Where two runs share a corner WITHOUT a piece, they used to both build to the lot
            // corner, overlap by a building's footprint, and Place silently dropped whichever
            // came second - a hole, at the same place on every such lot. One side has to give
            // way: the more public one holds the corner (street beats alley beats map edge) and
            // the other starts behind its end piece, as deep as any piece in the kit can reach.
            var startInset = new float[4];
            var endInset = new float[4];
            for (var i = 0; i < 4; i++)
            {
                startInset[i] = cornerWidth[i];
                endInset[i] = cornerWidth[(i + 1) % 4];
            }

            var recession = kit.DeepestPiece + config.partyWallGap;
            for (var i = 0; i < 4; i++)
            {
                if (cornerPrefab[i])
                    continue;

                var starting = i;              // sides[i] starts at this corner
                var ending = (i + 3) % 4;      // sides[i-1] ends at it

                if (Publicity(sides[starting]) > Publicity(sides[ending]))
                    endInset[ending] = Mathf.Max(endInset[ending], recession);
                else
                    startInset[starting] = Mathf.Max(startInset[starting], recession);
            }

            // A ring that is the whole block seals its yard, so one street side gives up
            // ~5m of wall for a passage. Subdivided blocks skip this - their alleys already
            // run open-ended to the streets.
            var passageSide = -1;
            if (lot.WholeBlock)
            {
                var eligible = new List<int>(4);
                for (var i = 0; i < 4; i++)
                    if (sides[i].isStreet
                        && sides[i].length - startInset[i] - endInset[i] >= PassageMinRun)
                        eligible.Add(i);
                if (eligible.Count > 0)
                    passageSide = eligible[rng.Next(eligible.Count)];
            }

            // ---- The landmark goes up BEFORE any run is walked, on the frontage
            // ChooseLandmarkFront reserved for it, and both halves of that matter.
            //
            // Which side: first-fit over S-E-N-W meant the block's most conspicuous building took
            // whichever street side the walk happened to reach first, and its yaw follows the side
            // (YawFor(outward)), so the choice IS the facing.
            //
            // Before the runs: sides walked ahead of it leave their parking bays in `occupied`,
            // and a bay that reached round the corner could reject the landmark outright - the
            // block would then either lose it or hand it to a side nobody chose. Standing it
            // first also puts its box in the list before any bay beside it is surveyed, which is
            // what stops a stall being painted underneath it.
            var runStart = (float[])startInset.Clone();

            if (state.Landmark && lotIndex == state.LandmarkLot)
            {
                var i = state.LandmarkSide;
                var end = sides[i].length - endInset[i];

                if (sides[i].isStreet && end > startInset[i])
                    runStart[i] = PlaceLandmark(state, sides[i].origin, sides[i].along, sides[i].outward,
                                                startInset[i], end, config.partyWallGap, palette,
                                                parking.Rare, tinter, prefabs,
                                                spawn, parent, rng, occupied, markings, placed, tints);

                // Spent whatever happened. The reserved run is a proxy measured before the corner
                // pieces were sized, so it can turn out too short for the prefab after all - and
                // the city has exactly one bank, which has to stand somewhere. Clearing the
                // reservation drops the landmark back to the old first-fit below, which is now
                // only ever a fallback.
                state.LandmarkLot = -1;
            }

            for (var i = 0; i < 4; i++)
            {
                var side = sides[i];
                var start = runStart[i];
                var end = side.length - endInset[i];
                if (end <= start)
                    continue;

                // Fallback only - see above. Still street-only, so a landmark that missed its
                // reserved front lands on another street run rather than in the alley.
                if (side.isStreet && state.Landmark)
                    start = PlaceLandmark(state, side.origin, side.along, side.outward,
                                          start, end, config.partyWallGap, palette, parking.Rare,
                                          tinter, prefabs,
                                          spawn, parent, rng, occupied, markings, placed, tints);

                // Placed after the landmark has claimed its head, in the middle half of what
                // is left, so it cannot collide with either corner.
                var passageAt = -1f;
                if (i == passageSide && end - start >= PassageMinRun)
                    passageAt = start + (0.35f + 0.3f * (float)rng.NextDouble()) * (end - start - PassageWidth);

                WalkSide(side.origin, side.along, side.outward, start, end, side.isStreet,
                         kit, bleed, state, parking, tinter, palette, prefabs, config, parent, spawn,
                         rng, occupied, markings, placed, tints, passageAt);
            }
        }

        /// <summary>
        /// How much a side matters when two runs contest a cornerless corner. A street run is
        /// seen by everyone, an alley run by whoever walks the alley, a map-edge run by nobody.
        /// </summary>
        static int Publicity(
            (Vector3 origin, Vector3 along, Vector3 outward, float length, bool isStreet, bool isOpen) side)
            => side.isStreet ? 2 : side.isOpen ? 0 : 1;

        /// <summary>
        /// Shortest run worth keeping between two corner pieces - just over the narrowest
        /// street piece (8.1m), so whatever survives the corners can still be closed with a
        /// building rather than left as a hole.
        /// </summary>
        const float MinRunAfterCorners = 9f;

        /// <summary>
        /// Drops corner candidates until every side can hold at least one piece between its two
        /// corners. The preferred (street/alley) corners go first; a mandatory street/street
        /// pair is only broken when the two pieces physically overlap, because a blank side
        /// wall on a true street corner costs more than a short run does.
        /// </summary>
        static void PruneCorners(
            (Vector3 origin, Vector3 along, Vector3 outward, float length, bool isStreet, bool isOpen)[] sides,
            int[] rank,
            GameObject[] prefab,
            float[] width)
        {
            // At most four corners can be dropped, so the guard is generous, not load-bearing.
            for (var guard = 0; guard < 8; guard++)
            {
                var drop = -1;

                for (var i = 0; i < 4 && drop < 0; i++)
                {
                    var a = i;                 // corner at the start of side i
                    var b = (i + 1) % 4;       // corner at its end
                    if (!prefab[a] && !prefab[b])
                        continue;

                    var run = sides[i].length - width[a] - width[b];
                    if (run >= MinRunAfterCorners)
                        continue;

                    // A merely short run gives up a preferred corner only; overlapping pieces
                    // must resolve whatever their rank, or Place drops one of them silently.
                    var limit = run < 0.5f ? 2 : 1;

                    if (prefab[a] && rank[a] <= limit)
                        drop = a;
                    if (prefab[b] && rank[b] <= limit
                        && (drop < 0 || rank[b] < rank[drop]
                            || (rank[b] == rank[drop] && width[b] > width[drop])))
                        drop = b;
                }

                if (drop < 0)
                    return;

                prefab[drop] = null;
                width[drop] = 0f;
                rank[drop] = -1;
            }
        }

        /// <summary>
        /// Puts the block's landmark at the head of a street run and returns the new start of
        /// the terrace behind it. Returns <paramref name="start"/> unchanged when it will not
        /// fit, leaving the landmark for a later side.
        /// </summary>
        static float PlaceLandmark(
            BlockState state,
            Vector3 origin,
            Vector3 along,
            Vector3 outward,
            float start,
            float end,
            float partyWallGap,
            PrefabDatabase.ZonePalette palette,
            RareVehicleFilter rare,
            VehicleTinter tinter,
            PrefabDatabase prefabs,
            SpawnPrefab spawn,
            Transform parent,
            System.Random rng,
            List<Bounds> occupied,
            List<ParkingLayout.Line> markings,
            List<GameObject> placed,
            List<BuildingTinter.Target> tints)
        {
            // The facade correction was missing here, alone among the placement paths. Harmless
            // so far only because none of the five landmarks is in the table - which is luck, not
            // a reason, and the block's most conspicuous building is the worst one to have face
            // its own back yard.
            var yaw = YawFor(outward) + prefabs.ExtraYawFor(state.Landmark);
            var scale = state.LandmarkScale;
            var footprint = PrefabBounds.FootprintXZ(state.Landmark, yaw) * scale;
            var width = Extent(footprint, along);
            var depth = Extent(footprint, outward);

            if (width > end - start)
                return start;

            // A palette with landmark cars wants them IN FRONT of the building, so the building
            // gives up the frontage: recessed one stall depth plus a pavement, with the bay in
            // the band it vacated. The hospital has no landmarkCars and stays flush.
            var forecourt = palette.HasLandmarkCars;
            var setback = forecourt ? ParkingLayout.StallDepth + LandmarkForecourtWalkway : 0f;

            var centre = origin + along * (start + width * 0.5f)
                       - outward * (depth * 0.5f + setback);
            var landmark = Place(state.Landmark, centre, yaw, width, depth, along, outward,
                                 partyWallGap, spawn, parent, occupied, placed, prefabs, scale);

            if (!landmark && forecourt)
            {
                // Recessing costs 6.6m of depth the flush placement did not need, and the lot
                // behind may not have it - a perimeter building or a neighbouring side's bay can
                // be standing exactly there. Give up the forecourt rather than the landmark.
                //
                // This matters most for the school, which is the one zone the city PROMISES: a
                // seed that placed no school has no SchoolMarker, and with no marker there is no
                // bus, no stops and no schoolchildren at all. A school flush against the street
                // with the bus back at the kerb is a far smaller loss than that. Place has no
                // side effects on failure, so the retry starts from an untouched occupied list.
                forecourt = false;
                setback = 0f;
                centre = origin + along * (start + width * 0.5f) - outward * (depth * 0.5f);
                landmark = Place(state.Landmark, centre, yaw, width, depth, along, outward,
                                 partyWallGap, spawn, parent, occupied, placed, prefabs, scale);
            }

            if (!landmark)
                return start;

            // The mild tier: a civic landmark is a building, not a shopfront.
            tints.Add(new BuildingTinter.Target(landmark, commercial: false));

            // On a successful Place, not on the pick: the pick can be handed to a later side and
            // a landmark spent for a building that never stood would be the whole city's one.
            UniqueBuildings.Spend(state.Landmark);

            var isSchool = landmark.name.StartsWith(Entities.SchoolMarker.PrefabName);

            // Attached OUTSIDE the forecourt branch, and it has to stay that way: the marker is
            // the only thing SchoolBusDirector can find in a saved scene, and the branch does
            // not run on a seed where the recessed placement failed and the school stood flush.
            // The bays and the bus berth are added to it from inside the branch when there are
            // any - see SchoolMarker.HasBusStall for what the director does when there are not.
            var school = isSchool ? MarkSchool(landmark) : null;

            if (forecourt)
            {
                // The bay spans exactly the frontage the recessed building vacated, so the
                // terrace on either side continues at the normal facade line and the notch
                // reads as the station's own yard. FillStalls reserves every bay whether or
                // not a car stands in it, which keeps the scatter pass off the forecourt.
                //
                // The police station is the one landmark whose bays get NO static cars: its
                // forecourt is the patrol fleet's parking, and a baked car would be a car
                // the real fleet can never move. maxCars 0 still paints the lines and still
                // reserves every bay - only the bakes are withheld - and the stall and door
                // geometry ride out on a PoliceStation marker for PoliceDirector to find.
                //
                // The bank KEEPS its bakes and gets a marker as well, over the bays those bakes
                // did not take, so its customers have somewhere to park. Both markers are still
                // recorded from the pre-fill survey below, never from the post-fill state: the
                // bays abut exactly (centres one StallWidth apart, bounds one StallWidth wide)
                // and Bounds.Intersects counts touching as intersecting, so once a bay has been
                // reserved the survey no longer answers the same question.
                var isStation = landmark.name.StartsWith(Entities.PoliceStation.PrefabName);
                var isBank = landmark.name.StartsWith(Entities.BankForecourt.PrefabName);
                var isSalon = landmark.name.StartsWith(SalonPrefabName);

                // The school gives the first stretch of its yard to the bus, which needs a berth
                // no row of car bays can hold - see ParkingLayout.BusStallLength. The car bays
                // then start where it ends, so the two never overlap by construction and the
                // school's 24.9m of frontage still leaves four of them.
                var carStart = start;
                var carWidth = width;
                ParkingLayout.Layout busLayout = null;
                var busBounds = new Bounds();

                if (isSchool && width >= ParkingLayout.BusStallLength + ParkingLayout.StallWidth)
                {
                    var candidate = ParkingLayout.ForBusBay(
                        origin, along, outward, start, ParkingLayout.BusStallLength);

                    busBounds = ParkingLayout.BayBounds(
                        candidate.Stalls[0].Centre, along, outward,
                        ParkingLayout.BusStallLength, ParkingLayout.StallDepth);

                    // Surveyed here and RESERVED further down, after the car bays have been
                    // surveyed too - see below.
                    if (!Blocked(busBounds, occupied))
                    {
                        busLayout = candidate;
                        carStart += ParkingLayout.BusStallLength;
                        carWidth -= ParkingLayout.BusStallLength;
                    }
                }

                var layout = ParkingLayout.ForStreetBay(origin, along, outward, carStart, carWidth,
                                                        paint: false);

                // The bays that are genuinely clear, surveyed BEFORE anything reserves one. Run
                // for every forecourt now, not just the ones that carry markers, because the paint
                // is drawn from it as well: a bay the survey rejects is one something else is
                // standing in, and it must not be painted round.
                //
                // This is also why the bus bay is not put into `occupied` the moment it is
                // surveyed: it abuts the first car bay exactly, and Bounds.Intersects counts
                // touching as intersecting, so reserving it first would report that bay blocked
                // by the bus's own berth.
                var free = FreeStalls(layout, occupied);

                ParkingLayout.PaintStreetBay(layout, along, outward, free);
                markings.AddRange(layout.Markings);

                if (busLayout != null)
                {
                    markings.AddRange(busLayout.Markings);
                    occupied.Add(busBounds);

                    var busStall = busLayout.Stalls[0];
                    school.SetBusStall(
                        landmark.transform.InverseTransformPoint(busStall.Centre),
                        Mathf.DeltaAngle(landmark.transform.eulerAngles.y, busStall.Yaw));
                }

                if (isStation)
                    MarkPoliceStation(landmark, layout, free);

                // The police station is the one landmark whose bays get NO static cars: its
                // forecourt is the patrol fleet's parking, and a baked car would be a car the
                // real fleet can never move. The school keeps ONE bake rather than the bank's
                // three, because four bays minus the bus's berth is all it has and the parents
                // arriving through the day are what should be filling them. The salon is the
                // opposite extreme - every bay filled and no empty-bay roll, because its
                // forecourt is the showroom floor.
                var maxCars = isStation ? 0
                            : isSchool ? SchoolForecourtMaxCars
                            : isSalon ? int.MaxValue
                            : LandmarkForecourtMaxCars;

                var baked = isBank || isSchool ? new HashSet<int>() : null;
                FillStalls(layout, new VehiclePicker(palette.landmarkCars, rng, rare), tinter, spawn,
                           parent, rng, occupied, placed, maxCars, baked,
                           emptyChance: isSalon ? 0f : EmptyBayChance);

                if (isBank || isSchool)
                {
                    // Painted, clear, and with no static car standing in it.
                    free.RemoveAll(baked.Contains);
                    var stalls = StallLocals(landmark.transform, layout, free, out var hostYaw);

                    if (isBank)
                        landmark.AddComponent<Entities.BankForecourt>().SetStalls(stalls, hostYaw);
                    else
                        school.SetStalls(stalls, hostYaw);
                }
            }

            state.Landmark = null;
            return start + width + 2f;
        }

        /// <summary>
        /// Rides the station instance into the saved scene carrying its forecourt stalls and
        /// its door, all in the station's own space - see StallHost for why local. The door is
        /// derived exactly the way InteractionMarkers derives every other door (local +Z facade
        /// centre at ground level) rather than waiting for the name sweep, because the recessed
        /// facade can fail BuildingDoorRule's road-cell test and the station must have a door
        /// regardless - the beat officers live there.
        /// </summary>
        static void MarkPoliceStation(
            GameObject landmark, ParkingLayout.Layout layout, List<int> free)
        {
            var tf = landmark.transform;
            var stalls = StallLocals(tf, layout, free, out var stallYaw);

            var mesh = InteractionMarkers.LocalBounds(tf);
            var door = new Vector3(mesh.center.x, 0f, mesh.max.z);

            landmark.AddComponent<Entities.PoliceStation>()
                    .SetLayout(stalls, stallYaw, door);
        }

        /// <summary>
        /// Rides the school instance into the saved scene carrying the door its pupils use.
        /// The same derivation MarkPoliceStation applies, for the same reason and with one
        /// extra: the school is FLUSH against the street wall, so its facade is the pavement
        /// edge and BuildingDoorRule would in principle grant it a door of its own - but a
        /// rule that can decline is no basis for a population that has to arrive here every
        /// morning. See SchoolMarker.
        /// </summary>
        static Entities.SchoolMarker MarkSchool(GameObject landmark)
        {
            var mesh = InteractionMarkers.LocalBounds(landmark.transform);
            var marker = landmark.AddComponent<Entities.SchoolMarker>();
            marker.SetDoor(new Vector3(mesh.center.x, 0f, mesh.max.z));
            return marker;
        }

        /// <summary>
        /// Which bays of a fresh layout stand clear of everything already placed - FillStalls'
        /// own test, run before it has reserved anything, which is the only moment it gives a
        /// stable answer. Bays abut exactly and Bounds.Intersects counts touching as
        /// intersecting, so a bay surveyed after its neighbour was reserved reads as blocked by
        /// a car park rather than by an obstacle.
        /// </summary>
        static List<int> FreeStalls(ParkingLayout.Layout layout, List<Bounds> occupied)
        {
            var free = new List<int>(layout.Stalls.Count);

            for (var index = 0; index < layout.Stalls.Count; index++)
                if (!Blocked(StallBounds(layout.Stalls[index]), occupied))
                    free.Add(index);

            return free;
        }

        /// <summary>
        /// The bay as the obstacle it reserves. Every stall yaw is a quarter turn - the lot
        /// frame and the four lot sides are both axis-aligned - so an odd quarter simply swaps
        /// depth and width.
        /// </summary>
        static Bounds StallBounds(ParkingLayout.Stall stall)
        {
            var quarter = Mathf.RoundToInt(stall.Yaw / 90f) & 1;
            return new Bounds(
                stall.Centre,
                quarter == 0
                    ? new Vector3(ParkingLayout.StallWidth, 1f, ParkingLayout.StallDepth)
                    : new Vector3(ParkingLayout.StallDepth, 1f, ParkingLayout.StallWidth));
        }

        /// <summary>Is anything already standing in this box? The survey test, pulled out so the
        /// school's bus berth - which is not StallWidth x StallDepth and so cannot go through
        /// FreeStalls - asks exactly the same question in exactly the same way.</summary>
        static bool Blocked(Bounds bounds, List<Bounds> occupied)
        {
            foreach (var existing in occupied)
                if (existing.Intersects(bounds))
                    return true;

            return false;
        }

        /// <summary>
        /// The chosen bays in the building's own space, plus the stall yaw relative to it. Both
        /// forecourt markers store their geometry this way, so that an instance which is ever
        /// moved carries its parking with it - see StallHost.
        /// </summary>
        static Vector3[] StallLocals(
            Transform tf, ParkingLayout.Layout layout, List<int> stalls, out float localYaw)
        {
            localYaw = layout.Stalls.Count > 0
                ? Mathf.DeltaAngle(tf.eulerAngles.y, layout.Stalls[0].Yaw)
                : 0f;

            var locals = new Vector3[stalls.Count];
            for (var i = 0; i < stalls.Count; i++)
                locals[i] = tf.InverseTransformPoint(layout.Stalls[stalls[i]].Centre);

            return locals;
        }

        /// <summary>
        /// One packed position along a run - a building, or a stretch of frontage given over to
        /// something that is not one (the courtyard passage, a surface car park).
        /// </summary>
        struct Slot
        {
            public GameObject Prefab;   // null on a non-building slot
            public float Yaw;
            public float Width;         // frontage this slot consumes
            public float Depth;
            public float Setback;
            public float Trailing;      // group gap + party wall, owed AFTER this slot
            public bool IsParking;
            public bool Build;          // blockFillRatio roll - false leaves the slot reserved but empty
            public bool Commercial;     // its group's flag, carried to the tinter's palette choice
        }

        /// <summary>
        /// Lays a terrace along one edge between <paramref name="start"/> and
        /// <paramref name="end"/>, every building fronting <paramref name="outward"/> with its
        /// street face flush to the edge.
        ///
        /// Two passes, and the split is the point. A single greedy pass stops as soon as nothing
        /// in the kit fits what is left, and that remainder - up to a whole building's width -
        /// stays as one hole at the far end of the run. Every block in the city then has a notch
        /// at the same corner, which reads as broken geometry rather than as variety. So pass one
        /// only chooses, pass two measures what is actually left over and spreads it across every
        /// joint at once.
        /// </summary>
        static void WalkSide(
            Vector3 origin,
            Vector3 along,
            Vector3 outward,
            float start,
            float end,
            bool isStreet,
            LotKit kit,
            LotKit bleed,
            BlockState state,
            VehiclePicker parking,
            VehicleTinter tinter,
            PrefabDatabase.ZonePalette palette,
            PrefabDatabase prefabs,
            CityConfig config,
            Transform parent,
            SpawnPrefab spawn,
            System.Random rng,
            List<Bounds> occupied,
            List<ParkingLayout.Line> markings,
            List<GameObject> placed,
            List<BuildingTinter.Target> tints,
            float passageAt = -1f)
        {
            var yaw = YawFor(outward);

            // Rolled ONCE for the whole run when the palette asks for it. The per-slot roll
            // below is what mixed 1-3m-gapped detached blocks into the middle of a flush
            // terrace; a run-level choice keeps LotKit's variety BETWEEN runs and ShuffleBag's
            // 4/5-floor alternation WITHIN one, which is the stepped silhouette the kit was
            // authored for. Corner retail, bleed slots and alley sides keep their per-slot roll.
            var runGroup = -1;
            if (isStreet && palette.uniformStreetRuns)
                runGroup = kit.PickGroup(facesStreet: true, preferCorner: false);

            // ---- PASS ONE: choose, do not place. Every rng draw of the run happens here, in
            // this order, so the layout stays reproducible for a given seed.
            var slots = new List<Slot>(16);
            var content = 0f;    // frontage the chosen slots occupy
            var mandatory = 0f;  // gap the chosen slots demand between themselves
            var cursor = start;
            var atCorner = true;

            while (cursor < end)
            {
                // The piece straddling the target finishes first, so the gap lands on the next
                // natural slot boundary as a clean full-width break; runGroup survives across
                // it, keeping a uniform run one development on both sides of its passage.
                if (passageAt >= 0f && cursor >= passageAt)
                {
                    slots.Add(new Slot { Width = PassageWidth });
                    content += PassageWidth;
                    cursor += PassageWidth;
                    passageAt = -1f;
                    atCorner = false;
                    continue;
                }

                var remaining = end - cursor;

                // A car park breaks up the wall the way a real street does. It happens on alley
                // runs too, and more often than on the street - a gap in a back elevation is a
                // yard with cars in it, which is exactly what the back of a block looks like and
                // what puts the "sometimes a parking lot instead of a building" variation into
                // the interior rows. The bay is half the width there; see AlleyParkingWidth.
                var bayWidth = isStreet ? ParkingLotWidth : AlleyParkingWidth;
                var bayChance = isStreet ? palette.parkingChance : palette.alleyParkingChance;

                if (remaining > bayWidth && rng.NextDouble() < bayChance)
                {
                    slots.Add(new Slot { Width = bayWidth, IsParking = true });
                    content += bayWidth;
                    cursor += bayWidth;
                    atCorner = false;
                    continue;
                }

                // After the bay roll, so a parking bay can still land on a run whose building
                // budget is spent; before the source draw, so an exhausted budget stops the
                // walk instead of spinning without moving the cursor.
                if (state.PerimeterBudget <= 0)
                    break;

                // Most slots come from this block's own palette; a few borrow from across the
                // street so the zone boundary does not land exactly on the kerb.
                //
                // Never on a uniform street run, though. There the whole point is that one
                // elevation is one development, and a borrowed slot is a detached house dropped
                // into the middle of a flush 4/5-storey wall - which is precisely the "houses
                // among them" this pass exists to remove. Alley runs still bleed: the softening
                // is worth having where it cannot break a terrace.
                var source = kit;
                if (bleed != null && runGroup < 0 && rng.NextDouble() < NeighbourBleedChance)
                    source = bleed;

                var preferCorner = isStreet && atCorner
                                && source.HasCornerPreferred
                                && rng.NextDouble() < CornerRetailChance;

                // Find a piece that fits the space left. The -short variants exist precisely to
                // close out a run, so give the search several tries before giving up.
                GameObject prefab = null;
                PrefabDatabase.WeightedGroup group = null;
                var chosen = -1;
                float width = 0f, depth = 0f;
                var chosenYaw = yaw;

                var attempts = Mathf.Max(8, source.TotalPieces);
                for (var tries = 0; tries < attempts; tries++)
                {
                    var index = runGroup >= 0 && source == kit && !preferCorner
                              ? runGroup
                              : source.PickGroup(isStreet, preferCorner);
                    if (index < 0)
                        break;

                    var candidate = source.Peek(index, isStreet);
                    if (!candidate)
                    {
                        source.Advance(index, isStreet);
                        continue;
                    }

                    // The city already has its one of these. Advanced exactly like the too-wide
                    // case below, and for a sharper reason: a bag Peeked without being Advanced
                    // offers the same piece on every try, so skipping without it would spend the
                    // whole attempt budget on one prefab and end the run early - a hole in the
                    // street wall, not a different building.
                    if (UniqueBuildings.IsSpent(candidate))
                    {
                        source.Advance(index, isStreet);
                        continue;
                    }

                    // Per-prefab facade correction folded in before measuring: for the plain
                    // 5floor it is a half-turn (windows to the pavement, blank wall inward)
                    // and the footprint is unchanged, so packing stays identical.
                    var candidateYaw = yaw + prefabs.ExtraYawFor(candidate);
                    var footprint = PrefabBounds.FootprintXZ(candidate, candidateYaw);
                    var candidateWidth = Extent(footprint, along);

                    if (candidateWidth <= remaining)
                    {
                        prefab = candidate;
                        group = source.GroupAt(index);
                        chosen = index;
                        width = candidateWidth;
                        depth = Extent(footprint, outward);
                        chosenYaw = candidateYaw;
                        break;
                    }

                    source.Advance(index, isStreet);

                    // A corner shop that does not fit must not stop the run - drop the
                    // preference and take whatever the rest of the kit offers.
                    preferCorner = false;
                }

                if (!prefab)
                    break;

                var gap = group.GapFor(rng);

                // blockFillRatio doubles as the chance a slot is built. Keep it high - a city
                // block should read as a continuous street wall. An unbuilt slot still holds its
                // frontage, so the neighbours either side do not slide together over it.
                var build = rng.NextDouble() < config.blockFillRatio;
                var setback = build ? group.SetbackFor(rng) : 0f;

                // Advanced on acceptance rather than on a successful Place: the bag is what
                // stops the same prefab repeating, and pass one is where the choosing happens.
                source.Advance(chosen, isStreet);

                // Spent on the same reasoning - but only when the slot will actually be built.
                // PerimeterBudget below is spent either way because an unbuilt slot still holds
                // its frontage; a cap on how many of a building EXIST has nothing to hold, and
                // spending it here would cost the city its only post office to build nothing.
                if (build)
                    UniqueBuildings.Spend(prefab);

                var trailing = gap + config.partyWallGap;
                slots.Add(new Slot
                {
                    Prefab = prefab,
                    Yaw = chosenYaw,
                    Width = width,
                    Depth = depth,
                    Setback = setback,
                    Trailing = trailing,
                    Build = build,
                    Commercial = group.commercial,
                });

                // Spent even when the Build roll came up empty - the slot holds its frontage
                // either way, and a budget that depends on later rolls stops being a cap.
                state.PerimeterBudget--;

                content += width;
                mandatory += trailing;
                cursor += width + trailing;
                atCorner = false;
            }

            // ---- CLOSERS: the loop above stops at the first width a random draw cannot fit,
            // which can leave most of a building's width unfilled. Terrace zones sweep the
            // whole kit instead for the widest piece that still fits, repeatedly - the -short
            // variants exist precisely to close a run. Detached zones skip this; their spacing
            // is gaps by design.
            if (palette.uniformStreetRuns)
            {
                while (state.PerimeterBudget > 0)
                {
                    var closer = BestCloser(kit, isStreet, yaw, prefabs, along, outward,
                                            end - cursor, out var closerWidth,
                                            out var closerDepth, out var closerYaw,
                                            out var closerCommercial);
                    if (!closer)
                        break;

                    slots.Add(new Slot
                    {
                        Prefab = closer,
                        Yaw = closerYaw,
                        Width = closerWidth,
                        Depth = closerDepth,
                        Trailing = config.partyWallGap,
                        Build = true,
                        Commercial = closerCommercial,
                    });
                    state.PerimeterBudget--;
                    content += closerWidth;
                    mandatory += config.partyWallGap;
                    cursor += closerWidth + config.partyWallGap;
                }
            }

            if (slots.Count == 0)
                return;

            // ---- RESIDUE BAY: what still remains is narrower than the narrowest piece, and
            // on a terrace it is too wide to hide in the joints - so it becomes a small
            // parking bay at the end of the run, the street-scale cousin of the feature strip.
            // Below two stalls' width it goes to the joints as before.
            if (palette.uniformStreetRuns)
            {
                var residue = (end - start) - content - mandatory;
                if (residue >= MinResidueBay)
                {
                    slots.Add(new Slot { Width = residue, IsParking = true });
                    content += residue;
                }
            }

            // ---- PASS TWO: spread what is left over, then place.
            // The final slot's trailing gap is not a joint - nothing follows it - so it is not
            // owed and goes back into the leftover.
            mandatory -= slots[slots.Count - 1].Trailing;

            var leftover = Mathf.Max(0f, (end - start) - content - mandatory);
            var joints = slots.Count - 1;
            var extra = joints > 0 ? Mathf.Min(leftover / joints, config.maxFillerGap) : 0f;

            // Whatever the cap refuses is split between the two ENDS of the run rather than
            // widened into the joints. Pushed into the joints it stops being a terrace; left at
            // one end it lands at the same corner of every block in the city.
            var edge = (leftover - extra * joints) * 0.5f;

            cursor = start + edge;

            for (var i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];

                if (slot.IsParking)
                {
                    PlaceParking(origin, along, outward, cursor, slot.Width, parking, tinter, spawn,
                                 parent, rng, occupied, markings, placed);
                }
                else if (slot.Prefab && slot.Build)
                {
                    var centre = origin
                               + along * (cursor + slot.Width * 0.5f)
                               - outward * (slot.Depth * 0.5f + slot.Setback);

                    var building = Place(slot.Prefab, centre, slot.Yaw, slot.Width, slot.Depth,
                                         along, outward, config.partyWallGap, spawn, parent,
                                         occupied, placed, prefabs);
                    if (building)
                        tints.Add(new BuildingTinter.Target(building, slot.Commercial));
                }

                cursor += slot.Width + slot.Trailing + extra;
            }
        }

        /// <summary>
        /// The widest piece in the kit that still fits the given space, measured in the run's
        /// frame with its facade fix folded in - the same arithmetic the pass-one search uses.
        /// Sweeps the raw group lists rather than the shuffle bags: a closer is chosen for its
        /// width, and cycling a bag to find it would reorder every draw after it.
        /// </summary>
        static GameObject BestCloser(
            LotKit kit,
            bool facesStreet,
            float baseYaw,
            PrefabDatabase prefabs,
            Vector3 along,
            Vector3 outward,
            float space,
            out float width,
            out float depth,
            out float yaw,
            out bool commercial)
        {
            GameObject best = null;
            width = 0f;
            depth = 0f;
            yaw = baseYaw;
            commercial = false;

            for (var group = 0; group < kit.GroupCount; group++)
                foreach (var piece in kit.GroupAt(group).PiecesFor(facesStreet))
                {
                    if (!piece)
                        continue;

                    var candidateYaw = baseYaw + prefabs.ExtraYawFor(piece);
                    var footprint = PrefabBounds.FootprintXZ(piece, candidateYaw);
                    var candidateWidth = Extent(footprint, along);
                    if (candidateWidth > space || candidateWidth <= width)
                        continue;

                    best = piece;
                    width = candidateWidth;
                    depth = Extent(footprint, outward);
                    yaw = candidateYaw;
                    commercial = kit.GroupAt(group).commercial;
                }

            return best;
        }

        /// <summary>Nose-in parking stalls filling one bay cut out of a run.</summary>
        static void PlaceParking(
            Vector3 origin,
            Vector3 along,
            Vector3 outward,
            float cursor,
            float width,
            VehiclePicker parking,
            VehicleTinter tinter,
            SpawnPrefab spawn,
            Transform parent,
            System.Random rng,
            List<Bounds> occupied,
            List<ParkingLayout.Line> markings,
            List<GameObject> placed)
        {
            if (parking.IsEmpty)
                return;

            // Cars sit perpendicular to the street, front toward it - ForStreetBay keeps that
            // facing. What it does not keep is the old 11m bay depth, which reached halfway back
            // into the lot for a car under 5m long.
            //
            // Surveyed before it is painted. The bay is cut out of a run on ONE side of the lot,
            // but it reaches 5.6m inward, which is far enough to meet whatever a neighbouring
            // side put in the same corner - a landmark most visibly. FillStalls has always
            // skipped a stall that meets something; the paint went down regardless, so the
            // obstacle ended up standing on stripes.
            var layout = ParkingLayout.ForStreetBay(origin, along, outward, cursor, width, paint: false);
            var free = FreeStalls(layout, occupied);
            ParkingLayout.PaintStreetBay(layout, along, outward, free);
            markings.AddRange(layout.Markings);

            FillStalls(layout, parking, tinter, spawn, parent, rng, occupied, placed);
        }

        /// <summary>
        /// The whole-block car park - the Parking zone. Bays either side of long aisles, marked
        /// out, fenced, with a booth at the gate.
        ///
        /// The previous version was three rows of cars on bare asphalt with nothing else on the
        /// block: 11m-deep rows at a 15m pitch for cars under 5m long, so two thirds of the
        /// surface was empty even where a car stood. ParkingLayout owns the geometry now, and
        /// owns it for the paint as well, so a bay and its lines cannot drift apart.
        /// </summary>
        static void BuildCarPark(
            Vector2 min,
            Vector2 max,
            Sides roadSides,
            PrefabDatabase.ZonePalette palette,
            VehiclePicker parking,
            VehicleTinter tinter,
            PrefabDatabase prefabs,
            Transform parent,
            SpawnPrefab spawn,
            System.Random rng,
            List<Bounds> occupied,
            List<ParkingLayout.Line> markings,
            List<GameObject> placed)
        {
            var layout = ParkingLayout.ForBlock(min, max, roadSides);
            markings.AddRange(layout.Markings);

            // Fence and booth before the cars, so their footprints are already in the occupancy
            // list when the bays are tested against it.
            ParkingLotDresser.Build(min, max, layout, palette, prefabs.lineMaterial,
                                    parent, spawn, occupied, placed);

            if (!parking.IsEmpty)
                FillStalls(layout, parking, tinter, spawn, parent, rng, occupied, placed);
        }

        /// <summary>
        /// Fills the band a feature strip took off the block - the `*` column of the block
        /// sketch, along one whole street side. Parking gets the street-bay treatment over the
        /// full length; a pocket park gets a kiosk on the pavement line and a coarse grid of
        /// benches and trees, everything facing the street it fronts.
        /// </summary>
        static void BuildFeatureStrip(
            Vector2 min,
            Vector2 max,
            Vector2 buildMin,
            Vector2 buildMax,
            FeatureStrip.Strip strip,
            PrefabDatabase.ZonePalette palette,
            VehiclePicker parking,
            VehicleTinter tinter,
            PrefabDatabase prefabs,
            CityConfig config,
            Transform parent,
            SpawnPrefab spawn,
            System.Random rng,
            List<Bounds> occupied,
            List<ParkingLayout.Line> markings,
            List<GameObject> placed)
        {
            // The same frame BuildLot walks its sides in: origin at the run's start corner,
            // outward pointing at the street the strip fronts. The band is the difference
            // between the full rect and the shrunk one.
            Vector3 origin, along, outward;
            float length;
            Vector2 bandMin, bandMax;

            switch (strip.Side)
            {
                case Sides.South:
                    origin = new Vector3(min.x, 0f, min.y);
                    along = Vector3.right;
                    outward = Vector3.back;
                    length = max.x - min.x;
                    bandMin = min;
                    bandMax = new Vector2(max.x, buildMin.y);
                    break;
                case Sides.East:
                    origin = new Vector3(max.x, 0f, min.y);
                    along = Vector3.forward;
                    outward = Vector3.right;
                    length = max.y - min.y;
                    bandMin = new Vector2(buildMax.x, min.y);
                    bandMax = max;
                    break;
                case Sides.North:
                    origin = new Vector3(max.x, 0f, max.y);
                    along = Vector3.left;
                    outward = Vector3.forward;
                    length = max.x - min.x;
                    bandMin = new Vector2(min.x, buildMax.y);
                    bandMax = max;
                    break;
                default:
                    origin = new Vector3(min.x, 0f, max.y);
                    along = Vector3.back;
                    outward = Vector3.left;
                    length = max.y - min.y;
                    bandMin = min;
                    bandMax = new Vector2(buildMin.x, max.y);
                    break;
            }

            if (strip.IsPark)
            {
                PlacePocketPark(bandMin, bandMax, along, outward, palette, prefabs, config,
                                parent, spawn, rng, occupied, placed);
                return;
            }

            // Painted only where the bays are actually clear, as in PlaceParking. This one runs
            // the FULL length of the side rather than a slot cut out of a run, so it is the most
            // exposed of the three: whatever the corner of the block already holds is inside it.
            var layout = ParkingLayout.ForStreetBay(origin, along, outward, 0f, length, paint: false);
            var free = FreeStalls(layout, occupied);
            ParkingLayout.PaintStreetBay(layout, along, outward, free);
            markings.AddRange(layout.Markings);
            FillStalls(layout, parking, tinter, spawn, parent, rng, occupied, placed);
        }

        /// <summary>
        /// A paved corner plaza on a feature strip: the kiosk stood at the pavement facing the
        /// street, and benches, trees and a lamp behind it on the same coarse grid the alleys
        /// use - only denser, and all of it turned to the street rather than away from a wall,
        /// because a park fronts the pavement the way a shop does.
        /// </summary>
        static void PlacePocketPark(
            Vector2 min,
            Vector2 max,
            Vector3 along,
            Vector3 outward,
            PrefabDatabase.ZonePalette palette,
            PrefabDatabase prefabs,
            CityConfig config,
            Transform parent,
            SpawnPrefab spawn,
            System.Random rng,
            List<Bounds> occupied,
            List<GameObject> placed)
        {
            var streetYaw = YawFor(outward);

            var kiosks = palette.kioskPrefabs;
            if (kiosks != null && kiosks.Length > 0)
            {
                // Both draws happen before the null check, the usual discipline: a missing
                // prefab in the list must not reshuffle everything placed after it.
                var kiosk = kiosks[rng.Next(kiosks.Length)];
                var at = 0.2f + 0.6f * (float)rng.NextDouble();

                if (kiosk)
                {
                    var yaw = streetYaw + prefabs.ExtraYawFor(kiosk);
                    var footprint = PrefabBounds.FootprintXZ(kiosk, yaw);
                    var width = Extent(footprint, along);
                    var depth = Extent(footprint, outward);

                    var centre2 = new Vector3((min.x + max.x) * 0.5f, 0f, (min.y + max.y) * 0.5f);
                    var outHalf = Mathf.Abs(outward.x) > 0.5f ? (max.x - min.x) * 0.5f : (max.y - min.y) * 0.5f;
                    var alongHalf = Mathf.Abs(along.x) > 0.5f ? (max.x - min.x) * 0.5f : (max.y - min.y) * 0.5f;

                    var centre = centre2
                               + outward * (outHalf - depth * 0.5f - 0.5f)
                               + along * ((at - 0.5f) * 2f * Mathf.Max(0f, alongHalf - width));

                    Place(kiosk, centre, yaw, width, depth, along, outward,
                          config.partyWallGap, spawn, parent, occupied, placed, prefabs);
                }
            }

            var props = palette.pocketParkProps != null && palette.pocketParkProps.Length > 0
                      ? palette.pocketParkProps
                      : palette.alleyProps;
            if (props == null || props.Length == 0)
                return;

            var bandWidth = max.x - min.x;
            var bandDepth = max.y - min.y;
            if (bandWidth < AlleyPropStep || bandDepth < AlleyPropStep)
                return;

            // Cells tile the band exactly, for the reason BuildInterior gives.
            var columns = Mathf.Max(1, Mathf.FloorToInt(bandWidth / AlleyPropStep));
            var rows = Mathf.Max(1, Mathf.FloorToInt(bandDepth / AlleyPropStep));
            var stepX = bandWidth / columns;
            var stepZ = bandDepth / rows;

            var bag = new ShuffleBag(props, rng);

            for (var row = 0; row < rows; row++)
            {
                for (var column = 0; column < columns; column++)
                {
                    if (rng.NextDouble() >= PocketParkPropChance)
                        continue;

                    var prefab = bag.Peek();
                    bag.Advance();
                    if (!prefab)
                        continue;

                    var position = new Vector3(min.x + (column + 0.5f) * stepX, 0f,
                                               min.y + (row + 0.5f) * stepZ);

                    var yaw = streetYaw + prefabs.ExtraYawFor(prefab);
                    var footprint = PrefabBounds.FootprintXZ(prefab, yaw);

                    Place(prefab, position, yaw, footprint.x, footprint.y,
                          Vector3.right, Vector3.forward, config.partyWallGap,
                          spawn, parent, occupied, placed, prefabs);
                }
            }
        }

        /// <summary>
        /// Puts a vehicle in each bay of a layout, leaving a few empty, and RESERVES every bay it
        /// fills. The reservation is the part that used to be missing: the old car-park pass read
        /// the occupancy list and never wrote to it, so BuildScatter could drop a lamp post or a
        /// wheelie bin inside a car.
        ///
        /// maxLength is the bay depth, which excludes the pack's 6.25m lorry. That is the right
        /// answer rather than a compromise - a lorry does not fit a marked car bay - and
        /// VehiclePicker re-rolls up to eight times, so a lorry draw becomes some other vehicle
        /// instead of an empty space.
        ///
        /// <paramref name="baked"/> collects the indices of bays a static car ended up in, for
        /// the bank's forecourt: what its customers may drive into is the surveyed-clear set
        /// (FreeStalls) minus this one. Reported from here because it is the only place that
        /// knows - the cap, the empty-bay roll and a picker that ran out of vehicles that fit
        /// all leave a painted bay standing empty, and none of them is visible afterwards.
        /// </summary>
        static void FillStalls(
            ParkingLayout.Layout layout,
            VehiclePicker parking,
            VehicleTinter tinter,
            SpawnPrefab spawn,
            Transform parent,
            System.Random rng,
            List<Bounds> occupied,
            List<GameObject> placed,
            int maxCars = int.MaxValue,
            HashSet<int> baked = null,
            float emptyChance = EmptyBayChance)
        {
            var cars = 0;
            for (var index = 0; index < layout.Stalls.Count; index++)
            {
                var stall = layout.Stalls[index];
                var bounds = StallBounds(stall);

                var blocked = false;
                foreach (var existing in occupied)
                    if (existing.Intersects(bounds))
                    {
                        blocked = true;
                        break;
                    }

                if (blocked)
                    continue;

                // Reserved whether or not a car ends up in it: an empty bay is still a bay, and a
                // bin standing in one reads worse than the gap does.
                occupied.Add(bounds);

                // Past the cap the remaining bays stay marked and reserved, just empty - the
                // forecourt's spare stalls, not a hole in the layout.
                if (cars >= maxCars)
                    continue;

                // The roll is drawn even when emptyChance is 0 (the salon), so the rng stream
                // consumes the same count per stall on every branch.
                if (rng.NextDouble() < emptyChance)
                    continue;

                var prefab = parking.Next(
                    maxLength: ParkingLayout.StallDepth,
                    maxWidth: ParkingLayout.StallWidth - 0.3f);

                if (!prefab)
                    continue;

                var car = spawn(prefab, stall.Centre, Quaternion.Euler(0f, stall.Yaw, 0f), parent);

                // Painted here rather than in a pass over the finished city, because these cars
                // are flagged batching-static afterwards (CityEditorUtils.MarkStaticForBatching)
                // and a material swap has to land before that.
                tinter?.Paint(car, prefab);

                placed.Add(car);
                baked?.Add(index);
                cars++;
            }
        }

        /// <summary>
        /// Furnishes the back alleys - whatever ground the lots left over, dressed with the light
        /// stuff that actually collects behind a block of flats: a bin against the wall, a
        /// dumpster, a bench, a lamp, the odd tree pushing through the tarmac.
        ///
        /// This used to build the alley ITSELF - a carriageway down the long axis with rows of
        /// garages and parked cars either side - because the midrise palettes ran at
        /// maxLotsPerAxis 1 and so enclosed one big empty yard. They no longer do: Subdivide
        /// splits a big block into two or three ringed lots with a real alley between them, and
        /// rows of buildings are what fills the interior now. What is left for this pass is the
        /// alley surface and the light-wells inside each lot, so it walks a coarse grid over the
        /// interior and drops one prop per cell at AlleyPropChance. Place rejects anything that
        /// lands on a building, which is what lets one flat grid cover an interior of any shape
        /// without this pass knowing where the lots ended up.
        ///
        /// The interior is measured off the ring that was actually built, not off the block rect
        /// minus an assumed depth: the terrace kit mixes 13.4m and 15.7m pieces and a corner
        /// piece is deeper again, so the four sides genuinely differ.
        /// </summary>
        static void BuildInterior(
            Vector2 min,
            Vector2 max,
            PrefabDatabase.ZonePalette palette,
            PrefabDatabase prefabs,
            CityConfig config,
            Transform parent,
            SpawnPrefab spawn,
            System.Random rng,
            List<Bounds> occupied,
            List<GameObject> placed)
        {
            if (palette.alleyProps == null || palette.alleyProps.Length == 0 || occupied.Count == 0)
                return;

            RingInsets(min, max, occupied, out var west, out var east, out var south, out var north);

            var iMin = new Vector2(min.x + west, min.y + south);
            var iMax = new Vector2(max.x - east, max.y - north);

            var width = iMax.x - iMin.x;
            var depth = iMax.y - iMin.y;
            if (width < AlleyPropStep || depth < AlleyPropStep)
                return;

            // Cells sized to divide the interior exactly rather than left at AlleyPropStep with a
            // remainder - a leftover strip along two edges is where the props would stop, and it
            // would always be the same two edges of every block.
            var columns = Mathf.FloorToInt(width / AlleyPropStep);
            var rows = Mathf.FloorToInt(depth / AlleyPropStep);
            var stepX = width / columns;
            var stepZ = depth / rows;

            var bag = new ShuffleBag(palette.alleyProps, rng);

            // Row-major, and the chance is rolled for every cell whether or not the cell is
            // buildable. Deciding the roll on what fitted would make the rng sequence depend on
            // the geometry, and the same seed would then stop reproducing the same city.
            for (var row = 0; row < rows; row++)
            {
                for (var column = 0; column < columns; column++)
                {
                    if (rng.NextDouble() >= AlleyPropChance)
                        continue;

                    var prefab = bag.Peek();
                    bag.Advance();
                    if (!prefab)
                        continue;

                    var position = new Vector3(iMin.x + (column + 0.5f) * stepX, 0f,
                                               iMin.y + (row + 0.5f) * stepZ);

                    var yaw = FaceOffNearestWall(position, occupied) + prefabs.ExtraYawFor(prefab);
                    var footprint = PrefabBounds.FootprintXZ(prefab, yaw);

                    Place(prefab, position, yaw, footprint.x, footprint.y,
                          Vector3.right, Vector3.forward, config.partyWallGap,
                          spawn, parent, occupied, placed, prefabs);
                }
            }
        }

        /// <summary>
        /// Yaw that turns a prop's front (+Z, the pack's convention) AWAY from the nearest wall,
        /// so a dumpster stands with its back to the brickwork and its lid to the alley instead
        /// of at whatever angle a random draw produced. That difference is most of what separates
        /// an alley from a scatter pass.
        /// </summary>
        static float FaceOffNearestWall(Vector3 position, List<Bounds> occupied)
        {
            var nearest = float.MaxValue;
            var away = Vector3.forward;

            foreach (var box in occupied)
            {
                var closest = box.ClosestPoint(position);
                var delta = new Vector3(position.x - closest.x, 0f, position.z - closest.z);

                var distance = delta.sqrMagnitude;
                if (distance >= nearest || distance <= 0.0001f)
                    continue;

                nearest = distance;
                away = delta;
            }

            // Snapped to the quarter turn everything else on the block is built on. Three degrees
            // off the wall it leans against reads as dropped rather than placed, and the pack's
            // props are modelled square.
            var yaw = Mathf.Atan2(away.x, away.z) * Mathf.Rad2Deg;
            return Mathf.Round(yaw / 90f) * 90f;
        }

        /// <summary>
        /// How far the built ring actually reaches in from each edge of the block rect, taken
        /// from the boxes already standing rather than from the palette.
        /// </summary>
        static void RingInsets(
            Vector2 min, Vector2 max, List<Bounds> occupied,
            out float west, out float east, out float south, out float north)
        {
            west = east = south = north = 0f;

            foreach (var box in occupied)
            {
                // A box counts toward a side only when it starts ON that side. Anything standing
                // free of all four - a car park bay cut into a run - leaves the insets alone.
                if (box.min.x - min.x <= RingEdgeTolerance) west = Mathf.Max(west, box.max.x - min.x);
                if (max.x - box.max.x <= RingEdgeTolerance) east = Mathf.Max(east, max.x - box.min.x);
                if (box.min.z - min.y <= RingEdgeTolerance) south = Mathf.Max(south, box.max.z - min.y);
                if (max.y - box.max.z <= RingEdgeTolerance) north = Mathf.Max(north, max.y - box.min.z);
            }
        }

        /// <summary>
        /// Drops the palette's yard props over whatever ground the buildings left - timber and
        /// brick stacks for a works yard, bins behind a terrace.
        ///
        /// Rejection sampling against the same occupancy list the buildings filled, which is
        /// why this runs last.
        ///
        /// Parks do NOT come through here. Uniform noise over a tile that already carries a
        /// designed walk layout is exactly what made them read as a lawn with objects dropped on
        /// it, so they go to ParkDresser instead - see BuildBlock.
        /// </summary>
        static void BuildScatter(
            Vector2 min,
            Vector2 max,
            PrefabDatabase.ZonePalette palette,
            Transform parent,
            SpawnPrefab spawn,
            System.Random rng,
            List<Bounds> occupied,
            List<GameObject> placed)
        {
            if (palette.scatter == null || palette.scatter.Length == 0 || palette.scatterDensity <= 0f)
                return;

            var area = (max.x - min.x) * (max.y - min.y);
            var attempts = Mathf.RoundToInt(area / ScatterAreaPerAttempt * palette.scatterDensity);

            var bag = new ShuffleBag(palette.scatter, rng);

            for (var i = 0; i < attempts; i++)
            {
                var point = new Vector3(
                    min.x + (float)rng.NextDouble() * (max.x - min.x),
                    0f,
                    min.y + (float)rng.NextDouble() * (max.y - min.y));

                var prefab = bag.Peek();
                if (!prefab)
                {
                    bag.Advance();
                    continue;
                }

                var yaw = (float)rng.NextDouble() * 360f;
                var footprint = PrefabBounds.FootprintXZ(prefab, yaw);
                var bounds = new Bounds(point, new Vector3(footprint.x, 1f, footprint.y));

                var blocked = false;
                foreach (var existing in occupied)
                    if (existing.Intersects(bounds))
                    {
                        blocked = true;
                        break;
                    }

                if (blocked)
                    continue;

                var rotation = Quaternion.Euler(0f, yaw, 0f);
                var localCentre = PrefabBounds.Get(prefab).center;
                var offset = rotation * new Vector3(localCentre.x, 0f, localCentre.z);

                placed.Add(spawn(prefab, new Vector3(point.x - offset.x, 0f, point.z - offset.z), rotation, parent));
                occupied.Add(bounds);
                bag.Advance();
            }
        }

        /// <summary>
        /// Instantiates one building, rejecting it if it would overlap something already on the
        /// block.
        ///
        /// The overlap test runs against a box shrunk by <paramref name="partyWallGap"/> on every
        /// side, and this is not cosmetic. Bounds.Intersects compares INCLUSIVELY
        /// (min &lt;= other.max &amp;&amp; max &gt;= other.min), so two terrace pieces laid flush
        /// report as colliding and the second is dropped - while the caller's cursor has already
        /// advanced past it, leaving a hole exactly one building wide. Measured over a sweep of
        /// realistic widths, origins and all four sides: 76% of flush pairs were being rejected
        /// this way. It is not a clean 50/50 because (origin + cursor + w/2) - w/2 does not
        /// round-trip in float, and an exact tie also counts as a collision.
        ///
        /// The FULL box is what goes into <paramref name="occupied"/>, so BuildScatter still
        /// refuses to plant a tree inside a wall.
        ///
        /// Returns the spawned instance, null on rejection - GameObject's implicit bool keeps
        /// the callers that only care whether it stood reading as before, while the building
        /// paths hand the instance on to the tinter.
        /// </summary>
        static GameObject Place(
            GameObject prefab,
            Vector3 centre,
            float yaw,
            float width,
            float depth,
            Vector3 along,
            Vector3 outward,
            float partyWallGap,
            SpawnPrefab spawn,
            Transform parent,
            List<Bounds> occupied,
            List<GameObject> placed,
            PrefabDatabase prefabs,
            float scale = 1f)
        {
            var bounds = new Bounds(
                new Vector3(centre.x, 0f, centre.z),
                new Vector3(
                    Mathf.Abs(along.x) * width + Mathf.Abs(outward.x) * depth,
                    1f,
                    Mathf.Abs(along.z) * width + Mathf.Abs(outward.z) * depth));

            // Y is left alone - every box is 1 tall and centred on 0, and deflating it would
            // make the test pass for anything, not just for flush neighbours.
            var probe = bounds;
            probe.size = new Vector3(
                Mathf.Max(0.01f, probe.size.x - 2f * partyWallGap),
                probe.size.y,
                Mathf.Max(0.01f, probe.size.z - 2f * partyWallGap));

            foreach (var existing in occupied)
                if (existing.Intersects(probe))
                {
                    placeRejections++;
                    return null;
                }

            // The mesh is not necessarily centred on its pivot, so offset by the rotated local
            // bounds centre to land the geometry where we actually want it. The offset scales
            // with the mesh - a scaled instance moves its geometry centre towards the pivot.
            var rotation = Quaternion.Euler(0f, yaw, 0f);
            var localCentre = PrefabBounds.Get(prefab).center;
            var offset = rotation * new Vector3(localCentre.x * scale, 0f, localCentre.z * scale);

            var instance = spawn(prefab, new Vector3(centre.x - offset.x, 0f, centre.z - offset.z),
                                 rotation, parent);
            if (!Mathf.Approximately(scale, 1f))
                instance.transform.localScale *= scale;

            // Every building on a block funnels through here, so this is the one place a terrace
            // flue has to be stamped - the corner pieces and the -back carry the same chimney as
            // the street elevations. A no-op for everything with no measured mouth, which is most
            // of what Place ever sees: kiosks, props, the landmarks.
            Ambient.SmokeVent.Mark(instance, prefab, prefabs, Ambient.VentKind.House);

            placed.Add(instance);
            occupied.Add(bounds);
            return instance;
        }

        static float YawFor(Vector3 outward) => Mathf.Atan2(outward.x, outward.z) * Mathf.Rad2Deg;

        /// <summary>Size of an XZ footprint measured along an axis-aligned direction.</summary>
        static float Extent(Vector2 footprint, Vector3 direction) =>
            Mathf.Abs(direction.x) > 0.5f ? footprint.x : footprint.y;
    }
}
