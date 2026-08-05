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
        ///
        /// This is the DEFAULT, not the live value - CityConfig.sidewalkWidth is what
        /// BlockBuilder and GroundPlacer actually hand to BlockRect. StreetPropPlacer's verge
        /// (5.5) and ParkedCarPlacer's kerb (5.6) are offsets from the road centreline and do
        /// not depend on it; moving the building line only widens the gap between them and the
        /// wall.
        /// </summary>
        public const float SidewalkClearance = 7f;

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
        /// Chance a free cell of that grid actually gets something. Well under 1: a back alley
        /// is mostly empty tarmac with a bin against the wall, and filling every cell turns it
        /// into a junkyard.
        /// </summary>
        const float AlleyPropChance = 0.32f;

        /// <summary>
        /// How close a placed box must start to an edge of the block rect to count as part of
        /// the ring on that side. One party wall plus a little - the pass-two edge spread moves
        /// buildings ALONG a run, never across it, so the outward face stays flush.
        /// </summary>
        const float RingEdgeTolerance = 1.5f;

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
        /// leave a strip of the road tile's grass verge showing along the street wall. Both
        /// callers must therefore pass the SAME sidewalkWidth; both read it off CityConfig.
        /// </summary>
        public static (Vector2 min, Vector2 max) BlockRect(
            CityGrid grid, List<Vector2Int> cells, float sidewalkWidth = SidewalkClearance)
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

            var reach = half - sidewalkWidth;
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
            var (min, max) = BlockRect(grid, cells, config.sidewalkWidth);

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

                // Not drawn from this block's rng: GroundPlacer lays its mosaic on exactly these
                // rectangles and has no way to replay the draws spent above. See BlockLots.
                foreach (var lot in BlockLots.Plan(min, max, roadSides, palette.maxLotsPerAxis,
                                                   config.alleyWidth, config.seed, blockId))
                    BuildLot(lot, palette, bleedPalette, state, parking, prefabs, config,
                             parent, spawn, rng, occupied, markings, placed);
            }

            if (palette.carRows)
                BuildCarPark(min, max, roadSides, palette, parking, prefabs,
                             parent, spawn, rng, occupied, markings, placed);

            // Before the scatter, so the alley furniture gets first claim on the interior and the
            // trees and mailboxes fall into whatever is left rather than into the alley itself.
            if (palette.BuildsPerimeter)
                BuildInterior(min, max, palette, prefabs, config,
                              parent, spawn, rng, occupied, placed);

            // A park is laid out, not scattered. groundIsTilePerCell is the exact precondition
            // ParkDresser needs - it means this zone's ground is tile-park, whose baked walk
            // cross and centre plaza are the geometry the whole layout hangs off. Every other
            // zone gets the uniform scatter, which is right for a yard and wrong for a park.
            if (palette.groundIsTilePerCell)
                ParkDresser.Build(grid, cells, palette, prefabs, parent, spawn, rng, occupied, placed);
            else
                BuildScatter(min, max, palette, parent, spawn, rng, occupied, placed);

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

        static void BuildLot(
            BlockLots.Lot lot,
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
                var yaw = YawFor(side.outward) + prefabs.ExtraYawFor(prefab);

                var footprint = PrefabBounds.FootprintXZ(prefab, yaw);
                var width = Extent(footprint, side.along);
                var depth = Extent(footprint, side.outward);
                cornerWidth[i] = width;

                var centre = side.origin + side.along * (width * 0.5f) - side.outward * (depth * 0.5f);
                Place(prefab, centre, yaw, width, depth, side.along, side.outward,
                      config.partyWallGap, spawn, parent, occupied, placed);
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
                                          start, end, config.partyWallGap, prefabs,
                                          spawn, parent, occupied, placed);

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
            float partyWallGap,
            PrefabDatabase prefabs,
            SpawnPrefab spawn,
            Transform parent,
            List<Bounds> occupied,
            List<GameObject> placed)
        {
            // The facade correction was missing here, alone among the placement paths. Harmless
            // so far only because none of the five landmarks is in the table - which is luck, not
            // a reason, and the block's most conspicuous building is the worst one to have face
            // its own back yard.
            var yaw = YawFor(outward) + prefabs.ExtraYawFor(state.Landmark);
            var footprint = PrefabBounds.FootprintXZ(state.Landmark, yaw);
            var width = Extent(footprint, along);
            var depth = Extent(footprint, outward);

            if (width > end - start)
                return start;

            var centre = origin + along * (start + width * 0.5f) - outward * (depth * 0.5f);
            if (!Place(state.Landmark, centre, yaw, width, depth, along, outward,
                       partyWallGap, spawn, parent, occupied, placed))
                return start;

            state.Landmark = null;
            return start + width + 2f;
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
                });

                content += width;
                mandatory += trailing;
                cursor += width + trailing;
                atCorner = false;
            }

            if (slots.Count == 0)
                return;

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
                    PlaceParking(origin, along, outward, cursor, slot.Width, parking, spawn,
                                 parent, rng, occupied, markings, placed);
                }
                else if (slot.Prefab && slot.Build)
                {
                    var centre = origin
                               + along * (cursor + slot.Width * 0.5f)
                               - outward * (slot.Depth * 0.5f + slot.Setback);

                    Place(slot.Prefab, centre, slot.Yaw, slot.Width, slot.Depth, along, outward,
                          config.partyWallGap, spawn, parent, occupied, placed);
                }

                cursor += slot.Width + slot.Trailing + extra;
            }
        }

        /// <summary>Nose-in parking stalls filling one bay cut out of a run.</summary>
        static void PlaceParking(
            Vector3 origin,
            Vector3 along,
            Vector3 outward,
            float cursor,
            float width,
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
            var layout = ParkingLayout.ForStreetBay(origin, along, outward, cursor, width);
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
                          spawn, parent, occupied, placed);
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
        /// </summary>
        static bool Place(
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
            List<GameObject> placed)
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
