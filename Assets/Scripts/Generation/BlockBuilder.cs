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
        /// props at 5.5, so 7 puts walls just behind the lamps without overlapping them.
        /// </summary>
        public const float SidewalkClearance = 7f;

        /// <summary>
        /// Target size of one lot. Roughly twice a building's depth, so the two rows either
        /// side of an alley almost meet.
        /// </summary>
        const float TargetLotSize = 42f;

        /// <summary>Service alley between lots - wide enough to read as a passage, not a street.</summary>
        const float AlleyWidth = 5f;

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
        /// One scatter attempt per this much ground, before density scales it. Attempts, not
        /// placements: anything landing on a building is dropped.
        /// </summary>
        const float ScatterAreaPerAttempt = 25f;

        /// <summary>
        /// Half-width of the sidewalk cross kept clear inside a park cell. tile-park's paths run
        /// through the middle of the cell on both axes, so planting a tree there would put it in
        /// the middle of the route pedestrians actually walk.
        /// </summary>
        const float ParkPathClearance = 6f;

        public static List<GameObject> Build(
            CityGrid grid,
            PrefabDatabase prefabs,
            CityConfig config,
            Transform parent,
            SpawnPrefab spawn = null)
        {
            var placed = new List<GameObject>();
            spawn ??= RoadNetworkBuilder.RuntimeSpawn;

            if (prefabs.zonePalettes == null || prefabs.zonePalettes.Length == 0)
            {
                Debug.LogWarning("[BlockBuilder] No zone palettes in the PrefabDatabase - run " +
                                 "Tools/City/Create or Refresh Config Assets.");
                return placed;
            }

            var rng = new System.Random(config.seed + SeedOffsets.Buildings);

            // Car parks draw from the same curated groups as the kerbside cars, but off this
            // block's RNG stream rather than SeedOffsets.ParkedCars.
            var parking = new VehiclePicker(prefabs.parkedCarGroups, rng);

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

                BuildBlock(grid, cells, blockId, palette, bleed, parking, prefabs, config,
                           parent, spawn, rng, placed);
            }

            return placed;
        }

        /// <summary>
        /// The buildable footprint of a block in world XZ, expanded out of the block cells and
        /// into the surrounding road tiles as far as SidewalkClearance.
        ///
        /// Shared with GroundPlacer so the concrete slab lands under exactly the same area the
        /// buildings occupy - if the two computed it separately they would drift apart and
        /// leave a strip of the road tile's grass verge showing along the street wall.
        /// </summary>
        public static (Vector2 min, Vector2 max) BlockRect(CityGrid grid, List<Vector2Int> cells)
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

            var reach = half - SidewalkClearance;
            if (grid.IsRoad(minCell.x - 1, minCell.y)) min.x -= reach;
            if (grid.IsRoad(maxCell.x + 1, maxCell.y)) max.x += reach;
            if (grid.IsRoad(minCell.x, minCell.y - 1)) min.y -= reach;
            if (grid.IsRoad(maxCell.x, maxCell.y + 1)) max.y += reach;

            return (min, max);
        }

        /// <summary>
        /// Which sides of a block actually face a street.
        ///
        /// Since the map boundary cuts the outermost streets, a block can sit against the edge
        /// of the world with no road on that side at all. Subdivide alone cannot tell - it only
        /// knows a lot's position within the block - so without this the buildings along that
        /// side would be given shopfront elevations facing empty space, which is exactly the
        /// "windows must face the street" rule inverted.
        ///
        /// Probes the same four neighbours as BlockRect, and for the same reason they agree:
        /// arterials run the full width or height of the map, so if the cell beyond one corner
        /// is road then that whole side of the block is road.
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
        }

        static void BuildBlock(
            CityGrid grid,
            List<Vector2Int> cells,
            int blockId,
            PrefabDatabase.ZonePalette palette,
            PrefabDatabase.ZonePalette bleedPalette,
            VehiclePicker parking,
            PrefabDatabase prefabs,
            CityConfig config,
            Transform parent,
            SpawnPrefab spawn,
            System.Random rng,
            List<GameObject> placed)
        {
            var (min, max) = BlockRect(grid, cells);

            if (max.x <= min.x || max.y <= min.y)
                return;

            // A zone may keep its own vehicles - the police car outside the police station is
            // what identifies the building at a glance. Falls back to the city-wide list.
            if (palette.HasOwnParkedCars)
                parking = new VehiclePicker(palette.parkedCars, rng);

            // Block-scoped rather than per-lot so the scatter pass can see every building, and
            // so two lots either side of an alley cannot overlap each other's corners.
            var occupied = new List<Bounds>();

            // Every stripe of paint on this block, wherever it comes from - the whole-block car
            // park or the short bays cut into a terrace - collected here and drawn as ONE mesh at
            // the end. Threaded down the same way occupied and placed already are, because the
            // walk that produces the street bays is four levels below this.
            var markings = new List<ParkingLayout.Line>();

            var roadSides = RoadSides(grid, cells);

            if (palette.BuildsPerimeter)
            {
                var state = new BlockState { Landmark = PickLandmark(palette, rng) };

                foreach (var lot in Subdivide(min, max, roadSides, palette.maxLotsPerAxis))
                    BuildLot(lot, palette, bleedPalette, state, parking, prefabs, config,
                             parent, spawn, rng, occupied, markings, placed);
            }

            if (palette.carRows)
                BuildCarPark(min, max, roadSides, palette, parking, prefabs,
                             parent, spawn, rng, occupied, markings, placed);

            BuildScatter(grid, cells, min, max, palette, parent, spawn, rng, occupied, placed);

            var paint = ParkingMarkings.Emit(markings, prefabs.lineMaterial,
                                             $"parking_lines_{grid.ZoneOf(blockId)}_{blockId}", parent);
            if (paint)
                placed.Add(paint);
        }

        static GameObject PickLandmark(PrefabDatabase.ZonePalette palette, System.Random rng)
        {
            if (palette.landmarks == null || palette.landmarks.Length == 0)
                return null;
            if (rng.NextDouble() >= palette.landmarkChance)
                return null;

            return palette.landmarks[rng.Next(palette.landmarks.Length)];
        }

        struct Lot
        {
            public Vector2 Min;
            public Vector2 Max;
            /// <summary>
            /// Whether each side faces a public street. False for an internal alley, and also
            /// false where the map boundary cuts the block off - both get the blank -back
            /// pieces, because neither has anyone walking past to show a shopfront to.
            /// </summary>
            public bool South, East, North, West;

            /// <summary>
            /// True when this lot IS the whole block. Only such a lot seals its courtyard on
            /// all four sides, so only it earns a passage through the street wall.
            /// </summary>
            public bool WholeBlock;
        }

        static IEnumerable<Lot> Subdivide(Vector2 min, Vector2 max, Sides roadSides, int maxLotsPerAxis)
        {
            var width = max.x - min.x;
            var depth = max.y - min.y;

            var columns = Mathf.Max(1, Mathf.RoundToInt(width / TargetLotSize));
            var rows = Mathf.Max(1, Mathf.RoundToInt(depth / TargetLotSize));

            // Interior lots can only ever face the alleys, so a zone that wants every window
            // on a street caps the grid - at 1 the whole block is one ring round one yard.
            if (maxLotsPerAxis > 0)
            {
                columns = Mathf.Min(columns, maxLotsPerAxis);
                rows = Mathf.Min(rows, maxLotsPerAxis);
            }

            var lotWidth = (width - (columns - 1) * AlleyWidth) / columns;
            var lotDepth = (depth - (rows - 1) * AlleyWidth) / rows;

            if (lotWidth <= 1f || lotDepth <= 1f)
            {
                yield return new Lot
                {
                    Min = min,
                    Max = max,
                    South = roadSides.HasFlag(Sides.South),
                    East = roadSides.HasFlag(Sides.East),
                    North = roadSides.HasFlag(Sides.North),
                    West = roadSides.HasFlag(Sides.West),
                    WholeBlock = true,
                };
                yield break;
            }

            for (var column = 0; column < columns; column++)
            for (var row = 0; row < rows; row++)
            {
                var x = min.x + column * (lotWidth + AlleyWidth);
                var z = min.y + row * (lotDepth + AlleyWidth);

                yield return new Lot
                {
                    Min = new Vector2(x, z),
                    Max = new Vector2(x + lotWidth, z + lotDepth),
                    // An outer lot only fronts a street if there is actually a street there:
                    // against the map boundary there is not, so it falls back to alley pieces.
                    South = row == 0 && roadSides.HasFlag(Sides.South),
                    North = row == rows - 1 && roadSides.HasFlag(Sides.North),
                    West = column == 0 && roadSides.HasFlag(Sides.West),
                    East = column == columns - 1 && roadSides.HasFlag(Sides.East),
                    WholeBlock = columns == 1 && rows == 1,
                };
            }
        }

        static void BuildLot(
            Lot lot,
            PrefabDatabase.ZonePalette palette,
            PrefabDatabase.ZonePalette bleedPalette,
            BlockState state,
            VehiclePicker parking,
            PrefabDatabase prefabs,
            CityConfig config,
            Transform parent,
            SpawnPrefab spawn,
            System.Random rng,
            List<Bounds> occupied,
            List<ParkingLayout.Line> markings,
            List<GameObject> placed)
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
                (origin: new Vector3(min.x, 0f, min.y), along: Vector3.right,   outward: Vector3.back,    length: max.x - min.x, isStreet: lot.South),
                (origin: new Vector3(max.x, 0f, min.y), along: Vector3.forward, outward: Vector3.right,   length: max.y - min.y, isStreet: lot.East),
                (origin: new Vector3(max.x, 0f, max.y), along: Vector3.left,    outward: Vector3.forward, length: max.x - min.x, isStreet: lot.North),
                (origin: new Vector3(min.x, 0f, max.y), along: Vector3.back,    outward: Vector3.left,    length: max.y - min.y, isStreet: lot.West),
            };

            // Corners first, so each run knows how much room the corners at its two ends take.
            // Only terrace kits have corner pieces at all - a detached model is finished on all
            // four elevations and has no corner variant, so those zones simply start at zero.
            var cornerWidth = new float[4];
            for (var i = 0; i < 4; i++)
            {
                // A corner piece belongs only where two streets meet. sides[i].origin is the
                // end of sides[(i+3)%4], so this lot corner joins exactly those two sides;
                // alley and map-boundary corners keep cornerWidth[i] = 0 and the runs meet
                // flush instead. Checked before PickCornerGroup so a skipped corner consumes
                // neither an RNG draw nor a corner-bag entry.
                if (!sides[i].isStreet || !sides[(i + 3) % 4].isStreet)
                    continue;

                var group = kit.PickCornerGroup();
                if (group < 0)
                    continue;

                var side = sides[i];
                var prefab = kit.PeekCorner(group);
                if (!prefab)
                    continue;

                // The correction is per prefab, not a single global offset, because the 4- and
                // 5-floor corner kits are authored mirrored relative to each other - one number
                // could only ever fix one of them. It goes into the footprint measurement too:
                // a quarter-turn swaps the footprint's x and z.
                var yaw = YawFor(side.outward) + prefabs.ExtraYawFor(prefab);

                var footprint = PrefabBounds.FootprintXZ(prefab, yaw);
                var width = Extent(footprint, side.along);
                var depth = Extent(footprint, side.outward);
                cornerWidth[i] = width;

                var centre = side.origin + side.along * (width * 0.5f) - side.outward * (depth * 0.5f);
                Place(prefab, centre, yaw, width, depth, side.along, side.outward, spawn, parent, occupied, placed);
                kit.AdvanceCorner(group);
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
                        && sides[i].length - cornerWidth[i] - cornerWidth[(i + 1) % 4] >= PassageMinRun)
                        eligible.Add(i);
                if (eligible.Count > 0)
                    passageSide = eligible[rng.Next(eligible.Count)];
            }

            for (var i = 0; i < 4; i++)
            {
                var side = sides[i];
                var start = cornerWidth[i];
                var end = side.length - cornerWidth[(i + 1) % 4];
                if (end <= start)
                    continue;

                // The landmark takes the head of the first street run long enough to hold it.
                if (side.isStreet && state.Landmark)
                    start = PlaceLandmark(state, side.origin, side.along, side.outward,
                                          start, end, spawn, parent, occupied, placed);

                // Placed after the landmark has claimed its head, in the middle half of what
                // is left, so it cannot collide with either corner.
                var passageAt = -1f;
                if (i == passageSide && end - start >= PassageMinRun)
                    passageAt = start + (0.35f + 0.3f * (float)rng.NextDouble()) * (end - start - PassageWidth);

                WalkSide(side.origin, side.along, side.outward, start, end, side.isStreet,
                         kit, bleed, parking, palette, prefabs, config, parent, spawn, rng,
                         occupied, markings, placed, passageAt);
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
            SpawnPrefab spawn,
            Transform parent,
            List<Bounds> occupied,
            List<GameObject> placed)
        {
            var yaw = YawFor(outward);
            var footprint = PrefabBounds.FootprintXZ(state.Landmark, yaw);
            var width = Extent(footprint, along);
            var depth = Extent(footprint, outward);

            if (width > end - start)
                return start;

            var centre = origin + along * (start + width * 0.5f) - outward * (depth * 0.5f);
            if (!Place(state.Landmark, centre, yaw, width, depth, along, outward, spawn, parent, occupied, placed))
                return start;

            state.Landmark = null;
            return start + width + 2f;
        }

        /// <summary>
        /// Lays a terrace along one edge between <paramref name="start"/> and
        /// <paramref name="end"/>, every building fronting <paramref name="outward"/> with its
        /// street face flush to the edge.
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
            VehiclePicker parking,
            PrefabDatabase.ZonePalette palette,
            PrefabDatabase prefabs,
            CityConfig config,
            Transform parent,
            SpawnPrefab spawn,
            System.Random rng,
            List<Bounds> occupied,
            List<ParkingLayout.Line> markings,
            List<GameObject> placed,
            float passageAt = -1f)
        {
            var yaw = YawFor(outward);
            var cursor = start;
            var atCorner = true;

            // Rolled ONCE for the whole run when the palette asks for it. The per-slot roll
            // below is what mixed 1-3m-gapped detached blocks into the middle of a flush
            // terrace; a run-level choice keeps LotKit's variety BETWEEN runs and ShuffleBag's
            // 4/5-floor alternation WITHIN one, which is the stepped silhouette the kit was
            // authored for. Corner retail, bleed slots and alley sides keep their per-slot roll.
            var runGroup = -1;
            if (isStreet && palette.uniformStreetRuns)
                runGroup = kit.PickGroup(facesStreet: true, preferCorner: false);

            while (cursor < end)
            {
                // The piece straddling the target finishes first, so the gap lands on the next
                // natural slot boundary as a clean full-width break; runGroup survives across
                // it, keeping a uniform run one development on both sides of its passage.
                if (passageAt >= 0f && cursor >= passageAt)
                {
                    cursor += PassageWidth;
                    passageAt = -1f;
                    atCorner = false;
                    continue;
                }

                var remaining = end - cursor;

                // A surface car park breaks up the wall the way a real street does, and only
                // ever fronts a public street - an alley-side lot would never be one.
                if (isStreet && remaining > ParkingLotWidth && rng.NextDouble() < palette.parkingChance)
                {
                    PlaceParking(origin, along, outward, cursor, parking, spawn, parent, rng,
                                 occupied, markings, placed);
                    cursor += ParkingLotWidth;
                    atCorner = false;
                    continue;
                }

                // Most slots come from this block's own palette; a few borrow from across the
                // street so the zone boundary does not land exactly on the kerb.
                var source = kit;
                if (bleed != null && rng.NextDouble() < NeighbourBleedChance)
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
                // block should read as a continuous street wall.
                if (rng.NextDouble() < config.blockFillRatio)
                {
                    var setback = group.SetbackFor(rng);
                    var centre = origin
                               + along * (cursor + width * 0.5f)
                               - outward * (depth * 0.5f + setback);

                    if (Place(prefab, centre, chosenYaw, width, depth, along, outward, spawn, parent, occupied, placed))
                        source.Advance(chosen, isStreet);
                }

                cursor += width + gap;
                atCorner = false;
            }
        }

        /// <summary>Nose-in parking stalls fronting the street.</summary>
        static void PlaceParking(
            Vector3 origin,
            Vector3 along,
            Vector3 outward,
            float cursor,
            VehiclePicker parking,
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
            var layout = ParkingLayout.ForStreetBay(origin, along, outward, cursor, ParkingLotWidth);
            markings.AddRange(layout.Markings);

            FillStalls(layout, parking, spawn, parent, rng, occupied, placed);
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
                FillStalls(layout, parking, spawn, parent, rng, occupied, placed);
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
        /// </summary>
        static void FillStalls(
            ParkingLayout.Layout layout,
            VehiclePicker parking,
            SpawnPrefab spawn,
            Transform parent,
            System.Random rng,
            List<Bounds> occupied,
            List<GameObject> placed)
        {
            foreach (var stall in layout.Stalls)
            {
                // Every stall yaw is a quarter turn - the lot frame and the four lot sides are
                // both axis-aligned - so an odd quarter simply swaps depth and width.
                var quarter = Mathf.RoundToInt(stall.Yaw / 90f) & 1;
                var bounds = new Bounds(
                    stall.Centre,
                    quarter == 0
                        ? new Vector3(ParkingLayout.StallWidth, 1f, ParkingLayout.StallDepth)
                        : new Vector3(ParkingLayout.StallDepth, 1f, ParkingLayout.StallWidth));

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

                if (rng.NextDouble() < EmptyBayChance)
                    continue;

                var prefab = parking.Next(
                    maxLength: ParkingLayout.StallDepth,
                    maxWidth: ParkingLayout.StallWidth - 0.3f);

                if (!prefab)
                    continue;

                placed.Add(spawn(prefab, stall.Centre, Quaternion.Euler(0f, stall.Yaw, 0f), parent));
            }
        }

        /// <summary>
        /// Drops the palette's yard props over whatever ground the buildings left. Trees and
        /// benches for a park, timber and brick stacks for a works yard.
        ///
        /// Rejection sampling against the same occupancy list the buildings filled, which is
        /// why this runs last. In a park it also keeps clear of the sidewalk cross running
        /// through each cell - see ParkPathClearance.
        /// </summary>
        static void BuildScatter(
            CityGrid grid,
            List<Vector2Int> cells,
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

                if (palette.groundIsTilePerCell && OnParkPath(grid, cells, point))
                    continue;

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
        /// Whether a point lands on the walkway cross of the park cell it sits in. tile-park's
        /// sidewalk paths run through the middle of the cell on both axes, so the clear ground
        /// is the four corner quadrants.
        /// </summary>
        static bool OnParkPath(CityGrid grid, List<Vector2Int> cells, Vector3 point)
        {
            foreach (var cell in cells)
            {
                var centre = grid.CellToWorld(cell);
                var dx = Mathf.Abs(point.x - centre.x);
                var dz = Mathf.Abs(point.z - centre.z);

                var half = CityGrid.CellSize * 0.5f;
                if (dx > half || dz > half)
                    continue;

                return dx < ParkPathClearance || dz < ParkPathClearance;
            }

            // Outside every park cell - that is the expanded verge, and free to plant.
            return false;
        }

        static bool Place(
            GameObject prefab,
            Vector3 centre,
            float yaw,
            float width,
            float depth,
            Vector3 along,
            Vector3 outward,
            SpawnPrefab spawn,
            Transform parent,
            List<Bounds> occupied,
            List<GameObject> placed)
        {
            var bounds = new Bounds(
                new Vector3(centre.x, 0f, centre.z),
                new Vector3(
                    Mathf.Abs(along.x) * width + Mathf.Abs(outward.x) * depth,
                    1f,
                    Mathf.Abs(along.z) * width + Mathf.Abs(outward.z) * depth));

            foreach (var existing in occupied)
                if (existing.Intersects(bounds))
                    return false;

            // The mesh is not necessarily centred on its pivot, so offset by the rotated local
            // bounds centre to land the geometry where we actually want it.
            var rotation = Quaternion.Euler(0f, yaw, 0f);
            var localCentre = PrefabBounds.Get(prefab).center;
            var offset = rotation * new Vector3(localCentre.x, 0f, localCentre.z);

            placed.Add(spawn(prefab, new Vector3(centre.x - offset.x, 0f, centre.z - offset.z), rotation, parent));
            occupied.Add(bounds);
            return true;
        }

        static float YawFor(Vector3 outward) => Mathf.Atan2(outward.x, outward.z) * Mathf.Rad2Deg;

        /// <summary>Size of an XZ footprint measured along an axis-aligned direction.</summary>
        static float Extent(Vector2 footprint, Vector3 direction) =>
            Mathf.Abs(direction.x) > 0.5f ? footprint.x : footprint.y;
    }
}
