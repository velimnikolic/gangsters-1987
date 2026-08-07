using System.Collections.Generic;
using UnityEngine;
using LivingCity.Data;

namespace LivingCity.Generation
{
    /// <summary>
    /// Lays the ground under each block.
    ///
    /// Without it the ground between the buildings is whatever the neighbouring road tiles
    /// happen to show, which is their green verge - so courtyards and alleys read as lawns
    /// rather than as a city.
    ///
    /// One stretched tile per block was what that used to mean, and it is why the floor read as
    /// empty. Every tile-plain_* prefab shares ONE material, atlas-LPEC; they differ only in
    /// which cell of the palette atlas their UVs point at, and those cells are flat colour. So
    /// stretching one over a 106m block does not tile anything - it produces a single uniform
    /// rectangle, at any size, with no repetition and no detail, under buildings and alley
    /// furniture that have plenty of both.
    ///
    /// What replaces it is a mosaic laid over the WHOLE block rect, cut along the lot plan:
    ///
    /// 1. A block slab underneath everything, one tile over the whole rect. Insurance, so a
    ///    rounding sliver between two patches can never show the road tile's verge through it.
    /// 2. A patchwork above it. BlockLots gives the same lot rectangles BlockBuilder wraps its
    ///    terraces around; the gaps between them are the service alleys. Both are walked here,
    ///    each divided into roughly CityConfig.groundPatchSize squares, and every patch rolls
    ///    its own surface and its own shade.
    ///
    /// Covering the whole rect rather than only the lot interiors is the point, and it took a
    /// measurement to see why. A 106m block subdivides into nine lots of about 30m, and the
    /// terrace kit is 13.4-15.7m deep - so a lot of that size is ENTIRELY building, front row
    /// backing onto rear row with nothing between. The ground a player actually sees in such a
    /// block is the alleys and the band out to the kerb, which is exactly the area a per-lot
    /// treatment would have skipped. Only the big single-lot blocks have a yard at all.
    ///
    /// 0. Before either of those, an apron: the block rect widened to the road tile's own kerb,
    ///    under everything else. See LayApron - it is what keeps any of the tile's grass from
    ///    showing beside a block, which is otherwise 2m on an ordinary block and 10m on a park.
    ///    Concrete everywhere except the park, which surfaces its own band in grass so the lawn
    ///    reaches the kerb instead of stopping 10m short of it.
    ///
    /// The shades come from PrefabDatabase.groundTints and may range far wider than the facade
    /// tints, because a floor is one flat patch of the atlas - there is no roof or window for
    /// _BaseColor to drag along with it.
    ///
    /// On top goes GroundPaint: a worn track down each alley, paving joints and a footpath
    /// across a yard where there is one, and faded repair patches on both. Flat colour on
    /// untextured materials, merged into one mesh per material per block.
    ///
    /// tile-plain_concrete is safe to scale and repeat: unlike the road tiles it carries no
    /// Tile component, so it never registers in Tile.Tiles and can never be picked as a
    /// pathfinding destination. It is pure geometry with a mesh collider. The same holds for
    /// the other tile-plain_* grounds.
    ///
    /// tile-park is the exception, and the reason ZonePalette.groundIsTilePerCell exists. It
    /// DOES carry a Tile - tileType OnlyPathwalk, with sidewalk paths whose nodes run to +/-15,
    /// exactly the tile boundary the road tiles use. Laid unscaled at a cell centre it therefore
    /// links straight into the pavement network and pedestrians walk through the park, while
    /// cars stay out because the tile carries no road paths. Stretching one park tile over the
    /// whole block would drag those nodes off the 30m grid, put them out of the 1.5m link
    /// tolerance, and silently sever the connection - so parks are laid per cell instead.
    /// NEITHER the patchwork nor the paint applies to that branch: a park is a park, not a
    /// surfaced yard.
    /// </summary>
    public static class GroundPlacer
    {
        /// <summary>
        /// The block slab, lifted fractionally so it wins the depth test against the road tile's
        /// verge where the two overlap. Well below the 4-unit pavement line, so it never buries
        /// the kerb.
        ///
        /// Public because it is also the height of a park tile's surface - the per-cell branch
        /// lays at this lift too - and ParkDresser stands its trees and benches on it. Placing
        /// park props at 0 instead put every one of them 2cm under its own grass.
        /// </summary>
        public const float BlockLift = 0.02f;

        /// <summary>
        /// The apron, under everything the block itself lays and over the road tile's grass at 0.
        ///
        /// Only 1cm below BlockLift because the road tile's pavement is at 0 and there is nowhere
        /// else to go. If this ever z-fights, raise BlockLift - do not lower this.
        /// </summary>
        const float ApronLift = 0.01f;

        /// <summary>The patchwork, above the block slab it sits on.</summary>
        const float PatchLift = 0.05f;

        // Paint goes above both, at GroundPaint.PaintLift (0.12). The steps are wide enough that
        // stacked layers do not z-fight at ground-level camera angles, and the whole stack still
        // clears the 4-unit pavement by a factor of thirty.

        /// <summary>
        /// How far a lot's yard sits in from the lot edge - one building depth. The 4- and
        /// 5-floor terrace pieces measure 13.4 and 15.7m deep, so 16 clears both and the yard
        /// starts just behind the rear walls.
        ///
        /// Only the paint uses this. The patchwork covers the lot whole, because a detached zone
        /// leaves its lot interior open and a terrace one hides it under buildings - and paying
        /// for a few covered patches is cheaper than working out which is which.
        /// </summary>
        const float CourtyardInset = 16f;

        /// <summary>
        /// Smallest yard worth painting - below this it is a sliver between rear walls, not a
        /// yard, and joints or a footpath on it would not be legible. A 106m block's 30m lots
        /// fall well under it and get no yard paint at all, which is correct: they have no yard.
        /// </summary>
        const float MinCourtyardSize = 8f;

        /// <summary>Spacing of the paving joint grid. Roughly the size of a real paving bay.</summary>
        const float JointSpacing = 6f;

        /// <summary>Thin enough to read as a joint between slabs rather than as a painted line.</summary>
        const float JointWidth = 0.2f;

        /// <summary>A footpath two people can pass on.</summary>
        const float PathWidth = 2.5f;

        /// <summary>
        /// Share of an alley's width taken by the track worn down the middle of it. Short of 1
        /// so the strip keeps a clean edge against the buildings either side.
        /// </summary>
        const float TrackShare = 0.55f;

        /// <summary>Most faded repair patches one region may get.</summary>
        const int MaxRepairs = 2;

        /// <summary>One span of a block's axis, and whether a lot or an alley occupies it.</summary>
        readonly struct Span
        {
            public readonly float Min;
            public readonly float Max;
            public readonly bool IsLot;

            public Span(float min, float max, bool isLot)
            {
                Min = min;
                Max = max;
                IsLot = isLot;
            }

            public float Size => Max - Min;
        }

        public static List<GameObject> Build(
            CityGrid grid,
            PrefabDatabase prefabs,
            CityConfig config,
            Transform parent,
            SpawnPrefab spawn = null)
        {
            var placed = new List<GameObject>();
            spawn ??= RoadNetworkBuilder.RuntimeSpawn;

            if (!prefabs.groundTile)
            {
                Debug.LogWarning("[GroundPlacer] No ground tile assigned - blocks will show the " +
                                 "road tiles' grass verge instead of concrete.");
                return placed;
            }

            // Own stream, iterated in blockId order like every other placer, so the ground mix
            // is deterministic per seed and never shifts the building or zoning draws.
            var rng = new System.Random(config.seed + SeedOffsets.Ground);

            for (var blockId = 0; blockId < grid.BlockCount; blockId++)
            {
                var cells = new List<Vector2Int>(grid.CellsInBlock(blockId));
                if (cells.Count == 0)
                    continue;

                var zone = grid.ZoneOf(blockId);
                var palette = prefabs.PaletteFor(zone);

                LayApron(grid, cells, palette, prefabs, config, blockId, zone,
                         parent, spawn, rng, placed);

                if (palette != null && palette.groundIsTilePerCell)
                {
                    var perCell = palette.ground ? palette.ground : prefabs.groundTile;
                    LayPerCell(grid, cells, perCell, blockId, zone, parent, spawn, rng, placed);
                    continue;
                }

                // Same clearance BlockBuilder gives this block, or the slab and the lot it is
                // supposed to sit under drift apart - see BlockRect and ClearanceFor.
                var (min, max) = BlockBuilder.BlockRect(grid, cells,
                                                        BlockBuilder.ClearanceFor(palette, config),
                                                        BlockBuilder.MainClearanceFor(palette, config));
                if (max.x <= min.x || max.y <= min.y)
                    continue;

                // Slab first, patchwork second - a fixed draw order per block is what keeps the
                // same seed producing the same city.
                var slab = LayRect(PickGround(palette, prefabs, rng), min, max, BlockLift,
                                   $"ground_{zone}_{blockId}",
                                   prefabs, config, parent, spawn, rng);
                if (slab)
                    placed.Add(slab);

                // A works has no lots either, but for the opposite reason to a car park: it is
                // NOT one continuous surface, it is a yard with carriageways cut through it. So
                // it keeps the mosaic - a works yard is genuinely mixed dirt, concrete and
                // asphalt - and then gets tarmac laid over the roads the halls were arranged
                // around. Replanned here from (seed, blockId) rather than passed across from
                // BlockBuilder, which is exactly what IndustrialLayout's determinism is for.
                if (palette != null && palette.industrialYard)
                {
                    LayServiceRoads(grid, cells, min, max, palette, prefabs, config,
                                    blockId, zone, parent, spawn, rng, placed);
                    continue;
                }

                // A palette that builds no perimeter has no lots and no alleys - a car park is
                // one continuous surface, and seams across it would mark boundaries that are
                // not there. It keeps the plain slab and its bay lines.
                if (palette == null || !palette.BuildsPerimeter)
                    continue;

                LayMosaic(grid, cells, min, max, palette, prefabs, config,
                          blockId, zone, parent, spawn, rng, placed);
            }

            return placed;
        }

        /// <summary>
        /// Pavement from the road tile's own kerb in to wherever this block's surface starts.
        ///
        /// The road tile is paved only out to CityGrid.PavementEdge (5); from there to its edge
        /// at 15 it is grass. A block's surface starts at BlockBuilder.ClearanceFor, so the band
        /// between the two showed that grass: 2m beside an ordinary block, and the full 10m
        /// beside a park, whose tiles cover its own cells and nothing else. That band used to be
        /// the verge - dressed with trees and parked cars, and reading as a lawn beside the park
        /// with cars standing on it. It is now paved end to end.
        ///
        /// Laid as one slab under everything the block itself puts down, rather than by widening
        /// BlockRect: BlockBuilder and GroundPlacer must hand BlockRect the SAME clearance or the
        /// mosaic seams drift off the lot plan, and the same rect feeds FeatureStrip.Shrink and
        /// BlockLots.Plan. An extra slab underneath moves none of that.
        ///
        /// The surface is prefabs.groundTile unless the palette names its own apronGround, and
        /// never shaded either way: the band reads as one continuous surface whatever the zone
        /// lays on top of it, and - see LayRect - it takes no draw from the ground stream.
        ///
        /// The park is the one palette that overrides it, with grass. Its 10m band is by far the
        /// widest, and paving it made the park read as set back behind a concrete ring while
        /// every building on the street stood 8m further forward. Green, the lawn runs to the
        /// kerb and the park lines up with its street. That does leave StreetPropPlacer's lamps
        /// and trees standing on grass rather than pavement beside a park, which is what a verge
        /// beside a park looks like anyway.
        /// </summary>
        static void LayApron(
            CityGrid grid,
            List<Vector2Int> cells,
            PrefabDatabase.ZonePalette palette,
            PrefabDatabase prefabs,
            CityConfig config,
            int blockId,
            BlockZone zone,
            Transform parent,
            SpawnPrefab spawn,
            System.Random rng,
            List<GameObject> placed)
        {
            // A car park already runs its own asphalt out to the pavement edge, so an apron
            // under it would be strictly smaller than the slab above and never show.
            if (BlockBuilder.ClearanceFor(palette, config) <= CityGrid.PavementEdge)
                return;

            var (min, max) = BlockBuilder.BlockRect(grid, cells,
                                                    CityGrid.PavementEdge,
                                                    CityGrid.MainPavementEdge);
            if (max.x <= min.x || max.y <= min.y)
                return;

            var surface = palette != null && palette.apronGround
                ? palette.apronGround
                : prefabs.groundTile;

            var apron = LayRect(surface, min, max, ApronLift,
                                $"apron_{zone}_{blockId}",
                                prefabs, config, parent, spawn, rng, shade: false);
            if (apron)
                placed.Add(apron);
        }

        /// <summary>
        /// Tarmac down each carriageway of a works compound.
        ///
        /// Laid at PatchLift rather than BlockLift so it sits ON the yard slab instead of
        /// z-fighting it, which is the same trick the repair patches use one function down.
        /// Unshaded: a road surface is one colour, and rolling a shade per carriageway would make
        /// the two halves of a compound look resurfaced at different times.
        ///
        /// The rectangles come from IndustrialLayout, called here with the same arguments
        /// BlockBuilder passed it. That is the whole reason that class takes (seed, blockId) and
        /// nothing from a caller's rng - lay this tarmac from a different plan and every works in
        /// the city has its halls standing in the road.
        /// </summary>
        static void LayServiceRoads(
            CityGrid grid,
            List<Vector2Int> cells,
            Vector2 min,
            Vector2 max,
            PrefabDatabase.ZonePalette palette,
            PrefabDatabase prefabs,
            CityConfig config,
            int blockId,
            BlockZone zone,
            Transform parent,
            SpawnPrefab spawn,
            System.Random rng,
            List<GameObject> placed)
        {
            var surface = palette.serviceRoadGround;
            if (!surface)
                return;

            var roadSides = BlockBuilder.RoadSides(grid, cells);

            var layout = IndustrialLayout.ForBlock(
                min, max, roadSides,
                config.serviceRoadWidth, config.industrialWallInset,
                config.seed, blockId);

            for (var i = 0; i < layout.Roads.Count; i++)
            {
                var road = layout.Roads[i];
                var tarmac = LayRect(surface, road.Min, road.Max, PatchLift,
                                     $"works_road_{zone}_{blockId}_{i}",
                                     prefabs, config, parent, spawn, rng, shade: false);
                if (tarmac)
                    placed.Add(tarmac);
            }
        }

        /// <summary>
        /// The patchwork and the paint, both cut along the block's lot plan.
        ///
        /// Paint is accumulated across the whole block and emitted once per material, for the
        /// reason GroundPaint gives - a block's joints alone run to a hundred strokes.
        /// </summary>
        static void LayMosaic(
            CityGrid grid,
            List<Vector2Int> cells,
            Vector2 min,
            Vector2 max,
            PrefabDatabase.ZonePalette palette,
            PrefabDatabase prefabs,
            CityConfig config,
            int blockId,
            BlockZone zone,
            Transform parent,
            SpawnPrefab spawn,
            System.Random rng,
            List<GameObject> placed)
        {
            var roadSides = BlockBuilder.RoadSides(grid, cells);

            // The strip band keeps the plain block slab - a car lot or a paved plaza IS one
            // continuous surface - so the mosaic and its paint stop at the pulled-in building
            // line. Same expression for `enabled` as BlockBuilder passes, or the two shrink
            // different rectangles and the seams drift off the alleys.
            var strip = FeatureStrip.For(min, max, roadSides,
                                         palette.featureStrip && palette.BuildsPerimeter,
                                         config, blockId);
            FeatureStrip.Shrink(strip, ref min, ref max);

            var lots = BlockLots.Plan(min, max, roadSides, palette.maxLotsPerAxis,
                                      config.alleyWidth, config.seed, blockId);
            if (lots.Count == 0)
                return;

            var columns = SpansOf(lots, min.x, max.x, true);
            var rows = SpansOf(lots, min.y, max.y, false);

            var dark = new List<GroundPaint.Stroke>();
            var light = new List<GroundPaint.Stroke>();

            for (var ci = 0; ci < columns.Count; ci++)
            for (var ri = 0; ri < rows.Count; ri++)
            {
                var column = columns[ci];
                var row = rows[ri];
                var isLot = column.IsLot && row.IsLot;

                LayPatches(column, row, isLot, palette, prefabs, config,
                           blockId, zone, ci, ri, parent, spawn, rng, placed);

                if (!config.groundPaint)
                    continue;

                // A corner where two alleys cross is a square, not a passage, so it gets no
                // track - a worn line through it would have to point somewhere, and there is no
                // "along" to point down.
                if (!isLot && (column.IsLot || row.IsLot))
                    PaintAlley(column, row, rng, dark);
            }

            if (config.groundPaint)
                foreach (var lot in lots)
                    PaintYard(lot, palette, rng, dark, light);

            var marks = GroundPaint.Emit(dark, prefabs.paintDarkMaterial, GroundPaint.PaintLift,
                                         $"ground_paint_dark_{zone}_{blockId}", parent);
            if (marks)
                placed.Add(marks);

            var paths = GroundPaint.Emit(light, prefabs.paintLightMaterial, GroundPaint.PaintLift,
                                         $"ground_paint_light_{zone}_{blockId}", parent);
            if (paths)
                placed.Add(paths);
        }

        /// <summary>
        /// Cuts one axis of the block into alternating lot and alley spans.
        ///
        /// The lots form a grid, so their distinct intervals along an axis ARE its columns (or
        /// rows); whatever lies between two of them is an alley. Read back off the plan rather
        /// than recomputed, so the spans cannot drift from the rectangles BlockBuilder built on.
        /// </summary>
        static List<Span> SpansOf(List<BlockLots.Lot> lots, float from, float to, bool xAxis)
        {
            var intervals = new List<Vector2>();

            foreach (var lot in lots)
            {
                var interval = xAxis
                    ? new Vector2(lot.Min.x, lot.Max.x)
                    : new Vector2(lot.Min.y, lot.Max.y);

                var seen = false;
                foreach (var existing in intervals)
                    if (Mathf.Abs(existing.x - interval.x) < 0.001f)
                    {
                        seen = true;
                        break;
                    }

                if (!seen)
                    intervals.Add(interval);
            }

            intervals.Sort((a, b) => a.x.CompareTo(b.x));

            var spans = new List<Span>(intervals.Count * 2 + 1);
            var cursor = from;

            foreach (var interval in intervals)
            {
                if (interval.x - cursor > 0.001f)
                    spans.Add(new Span(cursor, interval.x, false));

                spans.Add(new Span(interval.x, interval.y, true));
                cursor = interval.y;
            }

            if (to - cursor > 0.001f)
                spans.Add(new Span(cursor, to, false));

            return spans;
        }

        /// <summary>
        /// Divides one region into roughly groundPatchSize squares and surfaces each. The axes
        /// are split by BlockLots.SplitRun rather than evenly, for the reason given there: equal
        /// parts line up into a grid of their own and the block reads as a checkerboard.
        ///
        /// An alley is only alleyWidth across, so it rounds to a single patch down its width and
        /// is cut only along its length - which is what an alley surface looks like anyway.
        /// </summary>
        static void LayPatches(
            Span column,
            Span row,
            bool isLot,
            PrefabDatabase.ZonePalette palette,
            PrefabDatabase prefabs,
            CityConfig config,
            int blockId,
            BlockZone zone,
            int columnIndex,
            int rowIndex,
            Transform parent,
            SpawnPrefab spawn,
            System.Random rng,
            List<GameObject> placed)
        {
            var across = Mathf.Max(1, Mathf.RoundToInt(column.Size / config.groundPatchSize));
            var down = Mathf.Max(1, Mathf.RoundToInt(row.Size / config.groundPatchSize));

            var widths = BlockLots.SplitRun(column.Size, across, rng);
            var depths = BlockLots.SplitRun(row.Size, down, rng);

            // The whole region shares one surface unless a patch rolls its own, so a lot reads
            // as a lot rather than as noise, and the mosaic's largest features are the ones the
            // lot plan put there.
            var regionTile = isLot
                ? PickGround(palette, prefabs, rng)
                : PickYardGround(palette, prefabs, rng);

            var x = column.Min;
            for (var i = 0; i < across; i++)
            {
                var z = row.Min;
                for (var j = 0; j < down; j++)
                {
                    // Both draws happen unconditionally, the same discipline BuildingTinter
                    // keeps: skipping the surface roll for a patch that repeats its region would
                    // make groundPatchChance reshuffle the whole city instead of only changing
                    // how many patches differ.
                    var rerolled = isLot
                        ? PickYardGround(palette, prefabs, rng)
                        : PickGround(palette, prefabs, rng);

                    var tile = rng.NextDouble() < palette.groundPatchChance ? rerolled : regionTile;

                    var patch = LayRect(tile,
                                        new Vector2(x, z),
                                        new Vector2(x + widths[i], z + depths[j]),
                                        PatchLift,
                                        $"ground_patch_{zone}_{blockId}_{columnIndex}_{rowIndex}_{i}_{j}",
                                        prefabs, config, parent, spawn, rng);
                    if (patch)
                        placed.Add(patch);

                    z += depths[j];
                }

                x += widths[i];
            }
        }

        /// <summary>
        /// A worn track down the middle of a service alley, plus the odd patch of resurfacing.
        ///
        /// This is where the paint earns its place. In a subdivided block the alleys and the
        /// verge are the only ground anyone can see - the lots themselves are solid building -
        /// so a treatment that only ever touched yards would have been invisible in most of the
        /// city.
        /// </summary>
        static void PaintAlley(Span column, Span row, System.Random rng, List<GroundPaint.Stroke> dark)
        {
            // Exactly one of the two axes is a lot span here - the caller filtered out the
            // alley-crossing corners. The alley runs DOWN that axis and is thin across the other.
            var alongX = column.IsLot;

            var centreX = (column.Min + column.Max) * 0.5f;
            var centreZ = (row.Min + row.Max) * 0.5f;

            if (alongX)
                dark.Add(new GroundPaint.Stroke(new Vector2(column.Min, centreZ),
                                                new Vector2(column.Max, centreZ),
                                                row.Size * TrackShare));
            else
                dark.Add(new GroundPaint.Stroke(new Vector2(centreX, row.Min),
                                                new Vector2(centreX, row.Max),
                                                column.Size * TrackShare));

            Repairs(new Vector2(column.Min, row.Min), new Vector2(column.Max, row.Max), rng, dark);
        }

        /// <summary>
        /// Joints, a footpath and repairs across a lot's yard - the interior a perimeter ring
        /// encloses. Only the big single-lot blocks have one; see MinCourtyardSize.
        ///
        /// The joints are the treatment a zone opts into, because they are the one that asserts
        /// something: a grid of them says the ground was LAID. That is true of a forecourt
        /// downtown and behind a terrace, and false of a works yard, where it would read as
        /// wrong rather than as detailed.
        /// </summary>
        static void PaintYard(
            BlockLots.Lot lot,
            PrefabDatabase.ZonePalette palette,
            System.Random rng,
            List<GroundPaint.Stroke> dark,
            List<GroundPaint.Stroke> light)
        {
            var min = new Vector2(lot.Min.x + CourtyardInset, lot.Min.y + CourtyardInset);
            var max = new Vector2(lot.Max.x - CourtyardInset, lot.Max.y - CourtyardInset);

            if (max.x - min.x < MinCourtyardSize || max.y - min.y < MinCourtyardSize)
                return;

            if (palette.paveJoints)
            {
                for (var x = min.x + JointSpacing; x < max.x; x += JointSpacing)
                    dark.Add(new GroundPaint.Stroke(new Vector2(x, min.y),
                                                    new Vector2(x, max.y), JointWidth));

                for (var z = min.y + JointSpacing; z < max.y; z += JointSpacing)
                    dark.Add(new GroundPaint.Stroke(new Vector2(min.x, z),
                                                    new Vector2(max.x, z), JointWidth));
            }

            // A path down the yard's longer axis with one arm off it - enough to read as a route
            // between two doors rather than as a stripe.
            var centre = (min + max) * 0.5f;

            if (max.x - min.x >= max.y - min.y)
            {
                light.Add(new GroundPaint.Stroke(new Vector2(min.x, centre.y),
                                                 new Vector2(max.x, centre.y), PathWidth));

                var at = Mathf.Lerp(min.x, max.x, 0.3f + 0.4f * (float)rng.NextDouble());
                light.Add(new GroundPaint.Stroke(new Vector2(at, centre.y),
                                                 new Vector2(at, max.y), PathWidth));
            }
            else
            {
                light.Add(new GroundPaint.Stroke(new Vector2(centre.x, min.y),
                                                 new Vector2(centre.x, max.y), PathWidth));

                var at = Mathf.Lerp(min.y, max.y, 0.3f + 0.4f * (float)rng.NextDouble());
                light.Add(new GroundPaint.Stroke(new Vector2(centre.x, at),
                                                 new Vector2(max.x, at), PathWidth));
            }

            Repairs(min, max, rng, dark);
        }

        /// <summary>
        /// A few short, wide, dark strokes - patches of resurfacing rather than lines. Emitted as
        /// strokes because a stroke IS a rectangle once its width approaches its length.
        /// </summary>
        static void Repairs(Vector2 min, Vector2 max, System.Random rng, List<GroundPaint.Stroke> dark)
        {
            var count = rng.Next(MaxRepairs + 1);

            for (var i = 0; i < count; i++)
            {
                var length = 3f + 3f * (float)rng.NextDouble();
                var width = 1.5f + 1.5f * (float)rng.NextDouble();

                // Kept a half-extent in from every edge so a repair never hangs off its region
                // and up a wall. A region too small to hold one holds none of the rest either.
                var half = Mathf.Max(length, width) * 0.5f;
                if (max.x - min.x <= 2f * half || max.y - min.y <= 2f * half)
                    return;

                var cx = Mathf.Lerp(min.x + half, max.x - half, (float)rng.NextDouble());
                var cz = Mathf.Lerp(min.y + half, max.y - half, (float)rng.NextDouble());

                dark.Add(new GroundPaint.Stroke(new Vector2(cx - length * 0.5f, cz),
                                                new Vector2(cx + length * 0.5f, cz), width));
            }
        }

        /// <summary>
        /// Weighted per-slab choice from the palette's grounds; falls back to the palette's
        /// single ground, then the shared concrete. Zones without the list cost no rng draws,
        /// so adding grounds to one palette cannot reshuffle another's.
        /// </summary>
        static GameObject PickGround(PrefabDatabase.ZonePalette palette, PrefabDatabase prefabs, System.Random rng)
        {
            if (palette?.grounds != null && palette.grounds.Length > 0)
            {
                var usable = new List<PrefabDatabase.WeightedPrefabs>();
                var total = 0f;
                foreach (var ground in palette.grounds)
                    if (ground != null && ground.IsUsable)
                    {
                        usable.Add(ground);
                        total += ground.weight;
                    }

                var pick = WeightedRoll.Pick(usable, total, rng);
                if (pick != null)
                    return pick.prefabs[rng.Next(pick.prefabs.Length)];
            }

            return palette != null && palette.ground ? palette.ground : prefabs.groundTile;
        }

        /// <summary>
        /// The back-of-house surface. courtyardGrounds is the authored answer to "what lies
        /// behind the rear walls" - dirt or asphalt rather than more street - so it surfaces the
        /// alleys, and supplies the odd off-surface patch inside a lot. Zones that never had one
        /// fall back to the ordinary block surfaces.
        /// </summary>
        static GameObject PickYardGround(PrefabDatabase.ZonePalette palette, PrefabDatabase prefabs, System.Random rng)
        {
            if (palette?.courtyardGrounds != null && palette.courtyardGrounds.Length > 0)
            {
                var tile = palette.courtyardGrounds[rng.Next(palette.courtyardGrounds.Length)];
                if (tile)
                    return tile;
            }

            return PickGround(palette, prefabs, rng);
        }

        /// <summary>
        /// Stretches one ground tile over a world-space rectangle and shades it.
        ///
        /// shade:false is not merely "leave it plain" - Shade takes two draws off the stream
        /// whatever it decides, so a slab that opts out must skip the call entirely or every
        /// block after it re-rolls its surface. The apron is the one caller that does.
        /// </summary>
        static GameObject LayRect(
            GameObject tile,
            Vector2 min,
            Vector2 max,
            float lift,
            string name,
            PrefabDatabase prefabs,
            CityConfig config,
            Transform parent,
            SpawnPrefab spawn,
            System.Random rng,
            bool shade = true)
        {
            if (!tile)
                return null;

            // Measure the source tile rather than assuming it is 30x30, so a different ground
            // prefab can be dropped in without the scale silently going wrong.
            var tileSize = PrefabBounds.FootprintXZ(tile, 0f);
            if (tileSize.x <= 0.01f || tileSize.y <= 0.01f)
            {
                Debug.LogWarning($"[GroundPlacer] Ground tile '{tile.name}' has no measurable footprint.");
                return null;
            }

            var centre = new Vector3((min.x + max.x) * 0.5f, lift, (min.y + max.y) * 0.5f);
            var slab = spawn(tile, centre, Quaternion.identity, parent);

            slab.transform.localScale = new Vector3(
                (max.x - min.x) / tileSize.x,
                1f,
                (max.y - min.y) / tileSize.y);

            slab.name = name;

            if (shade)
                Shade(slab, prefabs, config, rng);

            return slab;
        }

        /// <summary>
        /// Gives one slab its shade.
        ///
        /// buildingBaseMaterial is not a misnomer here: atlas-LPEC is the single opaque material
        /// the whole pack shares, ground tiles included, and it is what a shade has to replace to
        /// take effect.
        ///
        /// Both draws happen whether or not the shade is applied. If the palette draw were
        /// skipped for unshaded slabs, moving the groundTintChance slider would shift the whole
        /// stream and re-surface the entire city instead of only changing how many slabs are
        /// shaded - the same trap BuildingTinter documents one system over.
        /// </summary>
        static void Shade(GameObject slab, PrefabDatabase prefabs, CityConfig config, System.Random rng)
        {
            if (prefabs.groundTints == null || prefabs.groundTints.Length == 0)
                return;   // Palette deliberately empty - shading is off, nothing wrong.

            var wants = rng.NextDouble() < config.groundTintChance;
            var tint = prefabs.groundTints[rng.Next(prefabs.groundTints.Length)];

            if (wants && tint)
                MaterialTint.Repaint(slab, prefabs.buildingBaseMaterial, tint);
        }

        /// <summary>
        /// One unscaled tile per cell. For tile-park, whose Tile component carries sidewalk paths
        /// authored to the 30m cell - see the note on this class. The tiles cover only the
        /// block's own cells, not the expanded rect the buildings use, which leaves the widest
        /// gap of any zone between the park and the pavement: the full 10m from the pavement edge
        /// at 5 out to the cell boundary at 15. LayApron fills it with the park palette's own
        /// grass, so the lawn carries on to the kerb and the gap stops reading as a setback. No
        /// shade, patch or stroke touches this branch.
        ///
        /// Each tile is turned by a random QUARTER turn. Unscaled is a hard requirement, but
        /// unrotated never was: measured out of the prefab with the parent rotation chain
        /// applied, tile-park's twelve Path children are four rotated copies of the same three
        /// walks, so the tile is exactly 4-fold symmetric and a quarter turn maps its path nodes
        /// onto the same set of positions. The +/-15 edge nodes stay on the edge midpoints and
        /// the link into the pavement network is untouched. What it does change is the grass -
        /// and since this branch takes no shade and no patches, that quarter turn is the only
        /// variety available to it. Without it a 3x3 park was visibly the same tile nine times.
        /// </summary>
        static void LayPerCell(
            CityGrid grid,
            List<Vector2Int> cells,
            GameObject tile,
            int blockId,
            BlockZone zone,
            Transform parent,
            SpawnPrefab spawn,
            System.Random rng,
            List<GameObject> placed)
        {
            foreach (var cell in cells)
            {
                var centre = grid.CellToWorld(cell);
                centre.y = BlockLift;

                var instance = spawn(tile, centre, Quaternion.Euler(0f, 90f * rng.Next(4), 0f), parent);
                instance.name = $"ground_{zone}_{blockId}_{cell.x}_{cell.y}";
                placed.Add(instance);
            }
        }
    }
}
