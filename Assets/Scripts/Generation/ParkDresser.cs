using System.Collections.Generic;
using UnityEngine;
using LivingCity.Data;

namespace LivingCity.Generation
{
    /// <summary>
    /// Lays out a park block: the plaza centrepiece, its lamps, the benches along the walks, the
    /// tree rows through the quadrants, the low planting between them and the hedge round the
    /// edge.
    ///
    /// This replaces BlockBuilder.BuildScatter for parks, and the reason is that the park is the
    /// one zone whose layout is already decided before the generator gets there. tile-park is not
    /// a blank lawn - measured out of the prefab with the full parent rotation chain applied, its
    /// twelve Path children resolve to a CROSS of walks running to the four edge midpoints
    /// (+/-15, 0) and (0, +/-15), meeting a CIRCULAR PLAZA of radius ~4 at the cell centre (the
    /// node ring sits at 3.5-4.4). Scattering uniform noise over that design is what made the
    /// park read as a lawn with objects dropped on it.
    ///
    /// Three measurements drove the rest of it:
    ///
    ///   fountain is 5.98 x 5.98. It was authored for that plaza and fits it with 1m to spare.
    ///   The old code could never put it there - BuildScatter rejected everything within 6 of
    ///   either axis, so the one place the fountain belonged was the one place it could not go,
    ///   and it ended up in a corner at a random yaw while the plaza stayed empty.
    ///
    ///   The walks are about 2m wide, not 12. The old ParkPathClearance of 6 blanked 64% of every
    ///   cell and left four separated 9x9 corner patches. 2.5 keeps a half-metre either side of
    ///   the paving and gives back an 11x11 quadrant that can hold an actual row of trees.
    ///
    ///   bench-forest is 2.93 x 0.58 and bench-old 1.67 x 0.71 - both longer on local X, which
    ///   under the pack's front = local +Z convention means LookRotation(towards the path) lands
    ///   the seat facing the walk and the length along it in one step, with no axis correction.
    ///
    /// Everything is placed at GroundPlacer.BlockLift rather than 0, because that is where the
    /// park tile's surface is; the old scatter sat every tree 2cm under its own grass.
    ///
    /// Nothing is PLANTED outside the block's own cells. The 8m strip between the park tiles and
    /// the pavement belongs to StreetPropPlacer, which already fills it at 45% a slot - the old
    /// scatter sampled the expanded block rect as well and double-planted the verge. The hedge is
    /// the one thing that does cross into it, deliberately: see BuildHedge.
    /// </summary>
    public static class ParkDresser
    {
        /// <summary>Measured radius of the paved roundel where the four walks meet.</summary>
        const float PlazaRadius = 4f;

        /// <summary>
        /// Half-width kept clear either side of a walk. The paving is about 2m across, so this
        /// leaves roughly half a metre of grass before anything is planted.
        /// </summary>
        const float PathHalfWidth = 2.5f;

        /// <summary>
        /// Keeps a crown from overhanging the tile edge. tree-lime is 7m across, so a tree
        /// planted flush with the boundary would hang 3.5m over the pavement next door.
        /// </summary>
        const float EdgeInset = 1.5f;

        const float CellHalf = CityGrid.CellSize * 0.5f;

        /// <summary>Lateral distance from a walk's centreline to the row of trees beside it.</summary>
        const float RowOffset = 4.5f;

        /// <summary>
        /// Along-walk spacing within a row, and where a row starts. It used to stop at 13.5 as
        /// well, which with 5.5 spacing meant exactly two trees a row - the row was a pair. The
        /// end now comes from ParkPlot, so a row runs the whole way out to the hedge and reads as
        /// an avenue leading to the gate, and the spacing is tight enough to fill it: at 4.5 a
        /// row on a street-facing side takes four, against the old two.
        /// </summary>
        const float TreeSpacing = 4.5f;
        const float RowStart = 6f;

        /// <summary>Trees are jittered off the row so it reads as planted, not as surveyed.</summary>
        const float RowJitterAlong = 0.8f;
        const float RowJitterAcross = 0.5f;

        const float TreeScaleMin = 0.85f;
        const float TreeScaleMax = 1.15f;
        const float PlantScaleMin = 0.8f;
        const float PlantScaleMax = 1.3f;

        /// <summary>Ring the lamps sit on - clear of the plaza, and on the diagonals so they
        /// never stand in a walk.</summary>
        const float LampRadius = 5.5f;

        /// <summary>Where a bench may sit along a walk, measured from the cell centre.</summary>
        static readonly float[] BenchStations = { 7.5f, 11.5f };
        const float BenchChance = 0.4f;

        /// <summary>Clear gap between the bench back and the paving.</summary>
        const float BenchClearance = 0.3f;

        /// <summary>
        /// Undergrowth clusters per quadrant. Up from 2 with the plot: a quadrant that reached
        /// 13.5 now reaches the hedge, so two clusters that used to fill it now dot it.
        /// </summary>
        const int ClustersPerQuadrant = 4;
        const int ClusterMin = 2;
        const int ClusterMax = 5;
        const float ClusterRadius = 1.8f;

        /// <summary>
        /// Stands of trees scattered over the whole plot, on top of the formal rows.
        ///
        /// The rows answer to tile-park's baked walk cross and stop where the quadrant does; they
        /// are an avenue, and an avenue is not a wood. These are the wood: a seed point anywhere
        /// in the plot, one species group for the whole stand - the same rule the rows follow,
        /// and for the same reason, since a real thicket is one species that seeded itself - and
        /// two to four trees within a few metres of it.
        ///
        /// Most of the room they have to work with is the ring beyond the cell boundary that
        /// ParkPlot opened up, which is why this arrived with that and not before it.
        /// </summary>
        const int GroveStands = 6;
        const float GroveRadius = 3.5f;
        const int GroveMin = 2;
        const int GroveMax = 5;

        /// <summary>
        /// Grassy knolls. tile-plain-hump is the pack's only centred mound - measured, a 30m dome
        /// peaking at 5.80m with all four edges flush at zero, so it drops onto the lawn with no
        /// skirt to hide and no seam to match. Scaled to a quarter or so it is a 7.5-13.5m knoll
        /// rising 1.4-2.6m, which is a park mound rather than a hill.
        ///
        /// It is landform, so it goes down BEFORE the planting and takes its place in the
        /// occupancy list: trees then grow around its foot instead of out of its side. The one
        /// thing it must not do is cover a walk, and its own radius - not its centre - is what
        /// that test has to use.
        ///
        /// Raising a whole CELL was the alternative and it is not available: tile-park carries
        /// the pedestrian path nodes, and lifting it 6m puts them outside the 1.5m link tolerance
        /// to the pavements, which quietly shuts the park to pedestrians.
        /// </summary>
        /// <summary>
        /// Attempts, not knolls. A seed has to land far enough from both walks to clear the
        /// dome's radius and far enough from the hedge to fit inside it, which on a street-facing
        /// cell leaves roughly half of each axis usable - so about three attempts in ten place
        /// one. Two attempts left half the cells flat; four averages a bit over one knoll a cell.
        /// </summary>
        const int MoundAttempts = 4;
        const float MoundScaleMin = 0.25f;
        const float MoundScaleMax = 0.45f;

        /// <summary>Clears the dome's rim off the flat tile it lands on. See PlaceMounds.</summary>
        const float MoundLift = 0.01f;

        /// <summary>
        /// Boulders, stumps and deadwood - the pack's Stones_T folder had a named constant in the
        /// bootstrap and no code path behind it, and tree-dead / tree-dry / stump / stump-small
        /// were never referenced at all. They go down LAST, so they fill what the planting left
        /// rather than displacing it.
        /// </summary>
        const int FeatureAttempts = 3;
        const float FeatureScaleMin = 0.8f;
        const float FeatureScaleMax = 1.25f;

        /// <summary>
        /// Patches of lawn laid over the park tile in a different shade, one per quadrant.
        ///
        /// The park's ground had no variation at all: GroundPlacer's per-cell branch takes no
        /// shade and no patchwork, deliberately, because a park is not a surfaced yard. That left
        /// every cell the identical green. A patch is the same grass tile the apron uses, tinted
        /// off the shared groundTints, so the lawn reads as mown in places and dry in others
        /// without any of the mosaic machinery a built-up block needs.
        ///
        /// It sits 1cm above the park tile and stays inside its quadrant, which is what keeps it
        /// off the baked walks - a rectangle cannot dodge them the way a scattered prop can.
        /// </summary>
        const float PatchHalfMin = 3f;
        const float PatchHalfMax = 7f;
        const float LawnPatchLift = 0.03f;

        /// <summary>
        /// Hedge line in from the tile edge, on a side with no road across it. Every side that
        /// does face a road takes the block line instead - see BuildHedge - so this is now the
        /// map-edge case alone, where there is no road tile to stand the hedge on.
        /// </summary>
        const float HedgeInset = 0.8f;

        /// <summary>Half the opening left in the hedge where a walk leaves the park.</summary>
        const float HedgeGateHalf = 3f;

        /// <summary>The four walks, as unit directions in XZ.</summary>
        static readonly Vector2[] Legs =
        {
            new(1f, 0f), new(0f, 1f), new(-1f, 0f), new(0f, -1f),
        };

        public static void Build(
            CityGrid grid,
            List<Vector2Int> cells,
            PrefabDatabase.ZonePalette palette,
            PrefabDatabase prefabs,
            CityConfig config,
            Transform parent,
            SpawnPrefab spawn,
            System.Random rng,
            List<Bounds> occupied,
            List<GameObject> placed)
        {
            if (cells == null || cells.Count == 0)
                return;

            var trees = UsableTreeGroups(palette, prefabs);
            var undergrowth = Undergrowth(palette);

            // The plot each cell may plant runs out to the hedge, not to the cell boundary - see
            // ParkPlot. Resolved off the same two clearances the hedge and the street walls use.
            var inBlock = new HashSet<Vector2Int>(cells);
            var clearance = BlockBuilder.ClearanceFor(palette, config);
            var mainClearance = BlockBuilder.MainClearanceFor(palette, config);

            // One monument per park, not one per cell - a 3x3 park with nine fountains reads as
            // a showroom. The cell nearest the block's own centre gets it; every other cell gets
            // a flower bed on its plaza instead, which keeps the roundel a destination without
            // repeating the landmark.
            var centrepieceCell = MostCentralCell(grid, cells);

            foreach (var cell in cells)
            {
                var centre = grid.CellToWorld(cell);
                centre.y = GroundPlacer.BlockLift;

                var isCentrepiece = cell == centrepieceCell;
                var plot = ParkPlot.For(grid, inBlock, cell,
                                        clearance, mainClearance, CellHalf - HedgeInset, EdgeInset);

                // Ground first, then the fixed furniture, then the landform, then everything
                // that has to grow around all of it. The order is also the draw order, so it is
                // fixed for the same reason every placer's is: moving a pass moves the city.
                LayLawn(centre, plot, palette, prefabs, parent, spawn, rng, placed);
                DressPlaza(centre, isCentrepiece, palette, undergrowth, plot, parent, spawn, rng, occupied, placed);
                PlaceLamps(centre, isCentrepiece, prefabs, parent, spawn, rng, occupied, placed);
                PlaceBenches(centre, palette, parent, spawn, rng, occupied, placed);
                PlaceMounds(centre, plot, palette, parent, spawn, rng, occupied, placed);
                PlantRows(centre, plot, trees, parent, spawn, rng, occupied, placed);
                PlantGrove(centre, plot, trees, parent, spawn, rng, occupied, placed);
                PlantUndergrowth(centre, plot, undergrowth, parent, spawn, rng, occupied, placed);
                PlaceFeatures(centre, plot, palette, parent, spawn, rng, occupied, placed);
            }

            BuildHedge(grid, cells, palette, config, parent, spawn, placed);
        }

        // ------------------------------------------------------------------ plaza

        /// <summary>
        /// The roundel at the centre of the cell. The landmark goes down at the exact cell
        /// centre - not jittered, because a fountain off-centre in its own circle looks like a
        /// mistake rather than like variety - and its yaw is snapped to a quarter turn so its
        /// own symmetry stays lined up with the walks.
        /// </summary>
        static void DressPlaza(
            Vector3 centre,
            bool isCentrepiece,
            PrefabDatabase.ZonePalette palette,
            GameObject[] undergrowth,
            ParkPlot.Extent plot,
            Transform parent,
            SpawnPrefab spawn,
            System.Random rng,
            List<Bounds> occupied,
            List<GameObject> placed)
        {
            if (isCentrepiece && palette.landmarks != null && palette.landmarks.Length > 0)
            {
                var landmark = palette.landmarks[rng.Next(palette.landmarks.Length)];
                if (landmark)
                {
                    Spawn(landmark, centre, 90f * rng.Next(4), 1f, parent, spawn, occupied, placed);
                    return;
                }
            }

            // A bed rather than a monument, and deliberately ON the roundel - the walks curve
            // around a ring at about 3.5 (measured: the turn nodes sit at +/-2.5, +/-2.5), so a
            // bed of 1.4 at the middle sits inside that ring and leaves the route open.
            Cluster(centre, centre, ClusterRadius * 0.8f, false, plot,
                    undergrowth, parent, spawn, rng, occupied, placed);
        }

        /// <summary>
        /// Lamps on the diagonals, just outside the plaza. The diagonal is the only direction
        /// that is clear of all four walks at once, and at 9.46m lamp-road-double is the tallest
        /// thing in the park - putting one in a walk would be the most visible obstruction
        /// available.
        /// </summary>
        static void PlaceLamps(
            Vector3 centre,
            bool isCentrepiece,
            PrefabDatabase prefabs,
            Transform parent,
            SpawnPrefab spawn,
            System.Random rng,
            List<Bounds> occupied,
            List<GameObject> placed)
        {
            if (prefabs.streetLamps == null || prefabs.streetLamps.Length == 0)
                return;

            // Four round the landmark so it is lit from every side; one opposite pair elsewhere,
            // so a large park does not turn into a forest of lamp posts.
            var start = isCentrepiece ? 0 : rng.Next(2);
            var step = isCentrepiece ? 1 : 2;

            for (var i = start; i < 4; i += step)
            {
                var angle = 45f + 90f * i;
                var offset = new Vector3(
                    Mathf.Sin(angle * Mathf.Deg2Rad) * LampRadius, 0f,
                    Mathf.Cos(angle * Mathf.Deg2Rad) * LampRadius);

                var lamp = prefabs.streetLamps[rng.Next(prefabs.streetLamps.Length)];
                Spawn(lamp, centre + offset, angle + 180f, 1f, parent, spawn, occupied, placed);
            }
        }

        // ------------------------------------------------------------------ benches

        /// <summary>
        /// Benches beside the walks, facing them.
        ///
        /// This is the change that does the most for how the park reads. A bench has a front,
        /// and the old scatter gave every prop a uniform 0-360 yaw, so benches sat at arbitrary
        /// angles in the middle of the lawn facing nothing. Here each one is set back just off
        /// the paving and turned to look across it, the same way StreetPropPlacer turns kerbside
        /// props back toward the road they serve.
        ///
        /// Both pack benches measure longer on local X, and the pack's convention is front =
        /// local +Z; a yaw-only LookRotation therefore puts the seat toward the walk and the
        /// length along it at the same time, with no axis correction to get wrong.
        /// </summary>
        static void PlaceBenches(
            Vector3 centre,
            PrefabDatabase.ZonePalette palette,
            Transform parent,
            SpawnPrefab spawn,
            System.Random rng,
            List<Bounds> occupied,
            List<GameObject> placed)
        {
            var benches = palette.parkBenches;
            if (benches == null || benches.Length == 0)
                return;

            foreach (var leg in Legs)
            {
                var side = new Vector2(-leg.y, leg.x);

                foreach (var station in BenchStations)
                {
                    for (var s = -1; s <= 1; s += 2)
                    {
                        if (rng.NextDouble() >= BenchChance)
                            continue;

                        var bench = benches[rng.Next(benches.Length)];
                        if (!bench)
                            continue;

                        // Set back by its own depth, so the seat edge - not the pivot - clears
                        // the paving whichever bench was drawn. The short side IS the depth:
                        // bench-forest measures 2.93 x 0.58 and bench-old 1.67 x 0.71, both
                        // longer along the seat than across it.
                        var footprint = PrefabBounds.FootprintXZ(bench, 0f);
                        var depth = Mathf.Min(footprint.x, footprint.y);
                        var lateral = PathHalfWidth + depth * 0.5f + BenchClearance;

                        var offset = leg * station + side * (s * lateral);
                        var position = centre + new Vector3(offset.x, 0f, offset.y);

                        // Look back across the walk: the outward normal is s * side, so the
                        // facing is its negation.
                        var face = side * -s;
                        var yaw = Mathf.Atan2(face.x, face.y) * Mathf.Rad2Deg;

                        Spawn(bench, position, yaw, 1f, parent, spawn, occupied, placed);
                    }
                }
            }
        }

        // ------------------------------------------------------------------ trees

        /// <summary>
        /// Two rows of trees flanking each walk - eight rows to a cell, meeting as an L in each
        /// quadrant and leaving the middle of the quadrant open as lawn.
        ///
        /// The species is drawn ONCE PER ROW and every tree in that row comes from it. Drawing
        /// per tree is what the old scatter did and it is why the park never read as planted:
        /// a real avenue is one species, and the variety belongs between rows, not within one.
        /// The draw is weighted because the species are not interchangeable in size - tree-lime
        /// is a 7.07m crown against tree-poplar's 2.21m, so an even draw puts three limes in an
        /// 11m quadrant and nothing else fits.
        /// </summary>
        static void PlantRows(
            Vector3 centre,
            ParkPlot.Extent plot,
            List<PrefabDatabase.WeightedPrefabs> trees,
            Transform parent,
            SpawnPrefab spawn,
            System.Random rng,
            List<Bounds> occupied,
            List<GameObject> placed)
        {
            if (trees == null || trees.Count == 0)
                return;

            var total = 0f;
            foreach (var group in trees)
                total += group.weight;

            foreach (var leg in Legs)
            {
                var side = new Vector2(-leg.y, leg.x);

                // The row runs to this cell's own boundary on this side, which is the hedge on a
                // street-facing side and the cell edge where the park carries on into the next
                // cell - there the neighbour's own row picks the avenue up.
                var reach = plot.Toward(leg);

                for (var s = -1; s <= 1; s += 2)
                {
                    var group = WeightedRoll.Pick(trees, total, rng);
                    if (group == null || group.prefabs.Length == 0)
                        continue;

                    for (var along = RowStart; along <= reach; along += TreeSpacing)
                    {
                        var prefab = group.prefabs[rng.Next(group.prefabs.Length)];
                        if (!prefab)
                            continue;

                        var jitterAlong = along + Jitter(rng, RowJitterAlong);
                        var jitterAcross = s * (RowOffset + Jitter(rng, RowJitterAcross));

                        var offset = leg * jitterAlong + side * jitterAcross;
                        if (OnPathOrPlaza(offset) || !plot.Contains(offset))
                            continue;

                        var position = centre + new Vector3(offset.x, 0f, offset.y);
                        var scale = Range(rng, TreeScaleMin, TreeScaleMax);

                        Spawn(prefab, position, (float)rng.NextDouble() * 360f, scale,
                              parent, spawn, occupied, placed);
                    }
                }
            }
        }

        // ------------------------------------------------------------------ undergrowth

        /// <summary>
        /// Shrubs, flowers and grass tufts filling the lawn, in small clusters rather than
        /// spread evenly. Clustering is the whole point: evenly spread low planting is
        /// indistinguishable from noise, whereas three tufts within a couple of metres of each
        /// other read as a bed someone put there.
        /// </summary>
        static void PlantUndergrowth(
            Vector3 centre,
            ParkPlot.Extent plot,
            GameObject[] undergrowth,
            Transform parent,
            SpawnPrefab spawn,
            System.Random rng,
            List<Bounds> occupied,
            List<GameObject> placed)
        {
            if (undergrowth == null || undergrowth.Length == 0)
                return;

            var inner = PathHalfWidth + 1f;

            for (var qx = -1; qx <= 1; qx += 2)
            for (var qz = -1; qz <= 1; qz += 2)
            {
                // Each quadrant reaches as far as its own two sides allow, which on a corner cell
                // is two different numbers - the old single 'outer' could only ever describe a
                // square, and the plot stopped being one when the hedge moved out.
                var outerX = plot.Toward(new Vector2(qx, 0f));
                var outerZ = plot.Toward(new Vector2(0f, qz));

                for (var i = 0; i < ClustersPerQuadrant; i++)
                {
                    var seed = new Vector2(
                        qx * Range(rng, inner, outerX),
                        qz * Range(rng, inner, outerZ));

                    var position = centre + new Vector3(seed.x, 0f, seed.y);
                    Cluster(centre, position, ClusterRadius, true, plot,
                            undergrowth, parent, spawn, rng, occupied, placed);
                }
            }
        }

        /// <summary>
        /// A handful of low pieces within a small radius of one point.
        ///
        /// <paramref name="keepClear"/> is what stops a lawn cluster spilling onto the paving.
        /// The seed point is drawn from the plantable band, but the members spread up to
        /// <paramref name="radius"/> around it, so a cluster seeded at the inner corner of that
        /// band reaches back over the walk and into the plaza, and one seeded at the outer edge
        /// hangs over the tile boundary. Each member is therefore tested in its own right rather
        /// than trusting the seed. The plaza bed passes false: that one is meant to be on the
        /// roundel, inside the ring the walks curve around.
        /// </summary>
        static void Cluster(
            Vector3 cellCentre,
            Vector3 position,
            float radius,
            bool keepClear,
            ParkPlot.Extent plot,
            GameObject[] options,
            Transform parent,
            SpawnPrefab spawn,
            System.Random rng,
            List<Bounds> occupied,
            List<GameObject> placed)
        {
            if (options == null || options.Length == 0)
                return;

            var count = rng.Next(ClusterMin, ClusterMax);

            for (var i = 0; i < count; i++)
            {
                var prefab = options[rng.Next(options.Length)];
                if (!prefab)
                    continue;

                var angle = (float)rng.NextDouble() * Mathf.PI * 2f;
                var distance = (float)rng.NextDouble() * radius;
                var point = position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance;

                if (keepClear)
                {
                    var local = new Vector2(point.x - cellCentre.x, point.z - cellCentre.z);
                    if (OnPathOrPlaza(local) || !plot.Contains(local))
                        continue;
                }

                // A bush has no front, so a free yaw is honest variety here rather than the
                // tell it is on a bench.
                Spawn(prefab, point, (float)rng.NextDouble() * 360f,
                      Range(rng, PlantScaleMin, PlantScaleMax), parent, spawn, occupied, placed);
            }
        }

        // ------------------------------------------------------------------ lawn

        /// <summary>
        /// One tinted patch of grass per quadrant, laid over the park tile.
        ///
        /// This is the park's entire answer to ground variation, and it is a small one on
        /// purpose. A built-up block gets GroundPlacer's mosaic - a surface and a shade rolled per
        /// patch, cut along the lot plan - and a park deliberately gets none of it, because the
        /// seams that read as paving bays in a yard read as mistakes on a lawn. What a lawn can
        /// carry is tone: the same grass, mown in places and dry in others.
        ///
        /// Rectangles cannot dodge the baked walks the way a scattered prop can, so each patch is
        /// confined to its quadrant and sized to fit inside it. A quadrant too tight to hold the
        /// smallest patch is skipped - after its draws are taken, not before.
        /// </summary>
        static void LayLawn(
            Vector3 centre,
            ParkPlot.Extent plot,
            PrefabDatabase.ZonePalette palette,
            PrefabDatabase prefabs,
            Transform parent,
            SpawnPrefab spawn,
            System.Random rng,
            List<GameObject> placed)
        {
            var prefab = palette.parkLawnPatch;
            if (!prefab)
                return;

            var footprint = PrefabBounds.FootprintXZ(prefab, 0f);
            if (footprint.x < 0.1f || footprint.y < 0.1f)
                return;

            var tints = prefabs.groundTints;
            var localCentre = PrefabBounds.Get(prefab).center;

            for (var qx = -1; qx <= 1; qx += 2)
            for (var qz = -1; qz <= 1; qz += 2)
            {
                // Every draw before the first test. Skipping them for a quadrant that turns out
                // too small would make the SIZE of one park reshuffle every block drawn after it,
                // which is the discipline LayPatches and BuildingTinter both keep.
                var halfX = Range(rng, PatchHalfMin, PatchHalfMax);
                var halfZ = Range(rng, PatchHalfMin, PatchHalfMax);
                var alongX = (float)rng.NextDouble();
                var alongZ = (float)rng.NextDouble();
                var tint = tints is { Length: > 0 } ? tints[rng.Next(tints.Length)] : null;

                var minX = PathHalfWidth + halfX;
                var maxX = plot.Toward(new Vector2(qx, 0f)) - halfX;
                var minZ = PathHalfWidth + halfZ;
                var maxZ = plot.Toward(new Vector2(0f, qz)) - halfZ;

                if (maxX <= minX || maxZ <= minZ)
                    continue;

                var scaleX = halfX * 2f / footprint.x;
                var scaleZ = halfZ * 2f / footprint.y;

                var position = new Vector3(
                    centre.x + qx * Mathf.Lerp(minX, maxX, alongX) - localCentre.x * scaleX,
                    LawnPatchLift,
                    centre.z + qz * Mathf.Lerp(minZ, maxZ, alongZ) - localCentre.z * scaleZ);

                var instance = spawn(prefab, position, Quaternion.identity, parent);

                var scale = instance.transform.localScale;
                scale.x *= scaleX;
                scale.z *= scaleZ;
                instance.transform.localScale = scale;
                instance.name = $"park_lawn_{qx}_{qz}";

                if (tint)
                    MaterialTint.Repaint(instance, prefabs.buildingBaseMaterial, tint);

                placed.Add(instance);
            }
        }

        // ------------------------------------------------------------------ landform

        /// <summary>
        /// Grassy knolls on the lawn - the park's only elevation, and the only kind available.
        ///
        /// The pack ships a full 6m terracing kit (tile-hill, -corner, -valley, -askew, -curve),
        /// all of it pure geometry with no Tile component, so any of it could be dropped in. None
        /// of it can be used to raise a park CELL, because the cell is tile-park and tile-park
        /// carries the pedestrian path nodes: 6m up puts them outside the 1.5m link tolerance to
        /// the pavements and the park quietly closes to pedestrians. The hill pieces also rise at
        /// an EDGE and hang a 6m skirt below it, so a lone one leaves a cliff on three sides.
        ///
        /// tile-plain-hump is the exception and the reason this works: measured, a 30m dome
        /// peaking at 5.80m with all four edges flush at zero. Scaled to a quarter it is a
        /// 7.5-13.5m knoll rising 1.4-2.6m that meets the lawn on every side with nothing to
        /// match. Landform goes down before the planting, so the trees grow around its foot.
        /// </summary>
        static void PlaceMounds(
            Vector3 centre,
            ParkPlot.Extent plot,
            PrefabDatabase.ZonePalette palette,
            Transform parent,
            SpawnPrefab spawn,
            System.Random rng,
            List<Bounds> occupied,
            List<GameObject> placed)
        {
            var mounds = palette.parkMounds;
            if (mounds == null || mounds.Length == 0)
                return;

            for (var i = 0; i < MoundAttempts; i++)
            {
                var prefab = mounds[rng.Next(mounds.Length)];
                var scale = Range(rng, MoundScaleMin, MoundScaleMax);
                var yaw = 90f * rng.Next(4);
                var seed = new Vector2(
                    Range(rng, -plot.West, plot.East),
                    Range(rng, -plot.South, plot.North));

                if (!prefab)
                    continue;

                // Its RADIUS is what has to clear the walks and the boundary. Testing the centre
                // the way a shrub is tested would lay a 10m dome across a 2m path and call it
                // clear, because the centre really would be 6m off the paving.
                var radius = CellHalf * scale;

                if (Mathf.Abs(seed.x) - radius < PathHalfWidth ||
                    Mathf.Abs(seed.y) - radius < PathHalfWidth)
                    continue;

                if (!plot.Contains(seed, radius))
                    continue;

                // A hair above the lawn: the dome's rim and the flat tile are otherwise exactly
                // coplanar, and 1cm of buried skirt is cheaper than a shimmering ring.
                var position = centre + new Vector3(seed.x, MoundLift, seed.y);

                Spawn(prefab, position, yaw, scale, parent, spawn, occupied, placed);
            }
        }

        // ------------------------------------------------------------------ grove

        /// <summary>
        /// Stands of trees anywhere in the plot, on top of the formal rows.
        ///
        /// The rows are an avenue: they answer to tile-park's walk cross, flank it at a fixed
        /// offset and leave the middle of each quadrant open. That is what a designed park looks
        /// like near its paths and it is not what a wood looks like anywhere. These fill the rest
        /// - most of it the ring beyond the cell boundary that ParkPlot opened up.
        ///
        /// One species group per STAND, drawn once, exactly as a row draws one. Mixing species
        /// within a clump is the tell that it was scattered rather than grown; a real thicket is
        /// one species that seeded itself and then spread.
        /// </summary>
        static void PlantGrove(
            Vector3 centre,
            ParkPlot.Extent plot,
            List<PrefabDatabase.WeightedPrefabs> trees,
            Transform parent,
            SpawnPrefab spawn,
            System.Random rng,
            List<Bounds> occupied,
            List<GameObject> placed)
        {
            if (trees == null || trees.Count == 0)
                return;

            var total = 0f;
            foreach (var group in trees)
                total += group.weight;

            for (var stand = 0; stand < GroveStands; stand++)
            {
                var seed = new Vector2(
                    Range(rng, -plot.West, plot.East),
                    Range(rng, -plot.South, plot.North));

                var group = WeightedRoll.Pick(trees, total, rng);
                if (group == null || group.prefabs.Length == 0)
                    continue;

                var count = rng.Next(GroveMin, GroveMax);

                for (var i = 0; i < count; i++)
                {
                    var prefab = group.prefabs[rng.Next(group.prefabs.Length)];
                    var angle = (float)rng.NextDouble() * Mathf.PI * 2f;
                    var distance = (float)rng.NextDouble() * GroveRadius;
                    var scale = Range(rng, TreeScaleMin, TreeScaleMax);
                    var yaw = (float)rng.NextDouble() * 360f;

                    var offset = seed + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;

                    if (!prefab || OnPathOrPlaza(offset) || !plot.Contains(offset))
                        continue;

                    Spawn(prefab, centre + new Vector3(offset.x, 0f, offset.y), yaw, scale,
                          parent, spawn, occupied, placed);
                }
            }
        }

        // ------------------------------------------------------------------ features

        /// <summary>
        /// Boulders, stumps and standing deadwood, scattered singly.
        ///
        /// Last of all, so they take what the planting left rather than displacing it - the
        /// occupancy list is append-only and every earlier pass has already claimed its ground.
        /// Single pieces rather than clusters: a lone boulder reads as one, and three in a clump
        /// reads as a delivery.
        /// </summary>
        static void PlaceFeatures(
            Vector3 centre,
            ParkPlot.Extent plot,
            PrefabDatabase.ZonePalette palette,
            Transform parent,
            SpawnPrefab spawn,
            System.Random rng,
            List<Bounds> occupied,
            List<GameObject> placed)
        {
            var features = palette.parkFeatures;
            if (features == null || features.Length == 0)
                return;

            for (var i = 0; i < FeatureAttempts; i++)
            {
                var prefab = features[rng.Next(features.Length)];
                var scale = Range(rng, FeatureScaleMin, FeatureScaleMax);
                var yaw = (float)rng.NextDouble() * 360f;
                var seed = new Vector2(
                    Range(rng, -plot.West, plot.East),
                    Range(rng, -plot.South, plot.North));

                if (!prefab || OnPathOrPlaza(seed) || !plot.Contains(seed))
                    continue;

                Spawn(prefab, centre + new Vector3(seed.x, 0f, seed.y), yaw, scale,
                      parent, spawn, occupied, placed);
            }
        }

        // ------------------------------------------------------------------ hedge

        /// <summary>
        /// A hedge along the outer boundary of the park, broken where a walk leaves it.
        ///
        /// The line and the mitred corners are HedgeLayout's, which is where the reasoning lives;
        /// this only spawns what it returns. In short: the hedge stands on the BLOCK LINE, the
        /// same 7 from a road centreline (10 on the avenue) that every facade in the city stands
        /// on, and not on the cell boundary 8m behind it where the park's own tiles stop. What
        /// made the cell boundary right before was that the band beyond it was the road tile's
        /// bare verge, and a hedge out there would have floated on grass with grass on both sides
        /// of it. GroundPlacer's apron surfaces that band now, in the park's own grass.
        ///
        /// StreetPropPlacer's lamps and trees sit at 5.5, so they stay OUTSIDE the hedge, on the
        /// strip of apron between it and the kerb. ParkDresser's own lamps are 5.5 from the CELL
        /// CENTRE, which is a different origin entirely and 17m clear of this line.
        /// </summary>
        static void BuildHedge(
            CityGrid grid,
            List<Vector2Int> cells,
            PrefabDatabase.ZonePalette palette,
            CityConfig config,
            Transform parent,
            SpawnPrefab spawn,
            List<GameObject> placed)
        {
            if (!palette.fenceSegment)
                return;

            // The same two functions BlockBuilder hands BlockRect, so the hedge and the street
            // walls around it are read off one source per side - including the deeper clearance
            // the dual carriageway needs.
            var runs = HedgeLayout.Plan(
                grid, cells,
                BlockBuilder.ClearanceFor(palette, config),
                BlockBuilder.MainClearanceFor(palette, config),
                CellHalf - HedgeInset,
                HedgeGateHalf,
                GroundPlacer.BlockLift);

            foreach (var run in runs)
                FenceRun.Lay(palette.fenceSegment, run.Origin, run.Along, run.From, run.To,
                             parent, spawn, placed);
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>
        /// Whether a cell-local offset falls on the walks or the roundel. Both tests are needed:
        /// the plaza is wider than the walks that feed it, so the corridor test alone would leave
        /// a ring of the paved circle plantable.
        /// </summary>
        static bool OnPathOrPlaza(Vector2 offset) =>
            offset.sqrMagnitude < PlazaRadius * PlazaRadius ||
            Mathf.Abs(offset.x) < PathHalfWidth ||
            Mathf.Abs(offset.y) < PathHalfWidth;

        /// <summary>
        /// Instantiates one park piece, rejecting it if it would sit inside something already
        /// placed. The footprint is measured at the yaw and scale it will actually be built at,
        /// so a rotated 7m lime is tested as the 7m obstacle it is.
        ///
        /// Public because ChurchDresser lays its garden through the same test; returns the
        /// instance (null on rejection) so a caller that needs the object - the church goes on
        /// to the tinter - can have it without a second lookup.
        /// </summary>
        public static GameObject Spawn(
            GameObject prefab,
            Vector3 position,
            float yaw,
            float scale,
            Transform parent,
            SpawnPrefab spawn,
            List<Bounds> occupied,
            List<GameObject> placed)
        {
            if (!prefab)
                return null;

            var footprint = PrefabBounds.FootprintXZ(prefab, yaw) * scale;
            var bounds = new Bounds(new Vector3(position.x, 0f, position.z),
                                    new Vector3(footprint.x, 1f, footprint.y));

            foreach (var existing in occupied)
                if (existing.Intersects(bounds))
                    return null;

            // The mesh is not necessarily centred on its pivot, so offset by the rotated - and
            // scaled - local bounds centre to land the geometry where it was asked for.
            var rotation = Quaternion.Euler(0f, yaw, 0f);
            var localCentre = PrefabBounds.Get(prefab).center;
            var offset = rotation * new Vector3(localCentre.x, 0f, localCentre.z) * scale;

            var instance = spawn(prefab,
                                 new Vector3(position.x - offset.x, position.y, position.z - offset.z),
                                 rotation, parent);

            if (!Mathf.Approximately(scale, 1f))
                instance.transform.localScale *= scale;

            occupied.Add(bounds);
            placed.Add(instance);
            return instance;
        }

        /// <summary>
        /// The cell closest to the block's centre of mass, so the landmark lands in the middle of
        /// the park rather than in whichever cell the flood fill happened to visit first.
        /// Resolved on cell coordinates and settled by cell order, which keeps it deterministic
        /// without consuming a draw.
        /// </summary>
        static Vector2Int MostCentralCell(CityGrid grid, List<Vector2Int> cells)
        {
            var sum = Vector2.zero;
            foreach (var cell in cells)
                sum += new Vector2(cell.x, cell.y);

            var mean = sum / cells.Count;

            var best = cells[0];
            var bestDistance = float.MaxValue;

            foreach (var cell in cells)
            {
                var distance = (new Vector2(cell.x, cell.y) - mean).sqrMagnitude;
                if (distance >= bestDistance)
                    continue;

                bestDistance = distance;
                best = cell;
            }

            return best;
        }

        /// <summary>
        /// The tree groups worth rolling. Falls back to the database's kerbside tree list when a
        /// palette has none, so a PrefabDatabase saved before parkTrees existed still produces a
        /// planted park instead of a bare lawn - the asset is only rewritten when someone runs
        /// Tools/City/Create or Refresh Config Assets.
        /// </summary>
        static List<PrefabDatabase.WeightedPrefabs> UsableTreeGroups(
            PrefabDatabase.ZonePalette palette, PrefabDatabase prefabs)
        {
            var usable = new List<PrefabDatabase.WeightedPrefabs>();

            if (palette.parkTrees != null)
                foreach (var group in palette.parkTrees)
                    if (group != null && group.IsUsable)
                        usable.Add(group);

            if (usable.Count == 0 && prefabs.trees is { Length: > 0 })
                usable.Add(new PrefabDatabase.WeightedPrefabs
                {
                    label = "fallback",
                    weight = 1f,
                    prefabs = prefabs.trees,
                });

            return usable;
        }

        /// <summary>
        /// Low planting. Deliberately NOT falling back to the palette's old scatter list the way
        /// the trees fall back to the kerbside list: that list is the flat 18-entry bag this
        /// whole class replaces, and it holds the fountain, two benches and three boulders
        /// alongside the shrubs. Fed to the cluster pass it would plant clumps of fountains, and
        /// a bare lawn is a better unmigrated state than a wrong one.
        /// </summary>
        static GameObject[] Undergrowth(PrefabDatabase.ZonePalette palette) => palette.parkUndergrowth;

        static float Jitter(System.Random rng, float amount) =>
            ((float)rng.NextDouble() * 2f - 1f) * amount;

        static float Range(System.Random rng, float min, float max) =>
            min + (float)rng.NextDouble() * (max - min);
    }
}
