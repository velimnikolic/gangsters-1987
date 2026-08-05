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
    /// Nothing is sampled outside the block's own cells. The 8m strip between the park tiles and
    /// the pavement belongs to StreetPropPlacer, which already plants it at 45% a slot - the old
    /// scatter sampled the expanded block rect as well and double-planted the verge.
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

        /// <summary>Along-walk spacing within a row, and the stretch of walk a row covers.</summary>
        const float TreeSpacing = 5.5f;
        const float RowStart = 6f;
        const float RowEnd = 13.5f;

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

        const int ClustersPerQuadrant = 2;
        const int ClusterMin = 2;
        const int ClusterMax = 5;
        const float ClusterRadius = 1.8f;

        /// <summary>Hedge line, in from the tile edge.</summary>
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

                DressPlaza(centre, isCentrepiece, palette, undergrowth, parent, spawn, rng, occupied, placed);
                PlaceLamps(centre, isCentrepiece, prefabs, parent, spawn, rng, occupied, placed);
                PlaceBenches(centre, palette, parent, spawn, rng, occupied, placed);
                PlantRows(centre, trees, parent, spawn, rng, occupied, placed);
                PlantUndergrowth(centre, undergrowth, parent, spawn, rng, occupied, placed);
            }

            BuildHedge(grid, cells, palette, parent, spawn, placed);
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
            Cluster(centre, centre, ClusterRadius * 0.8f, false,
                    undergrowth, parent, spawn, rng, occupied, placed);
        }

        /// <summary>
        /// Lamps on the diagonals, just outside the plaza. The diagonal is the only direction
        /// that is clear of all four walks at once, and at 6.69m lamp-city is the tallest thing
        /// in the park - putting one in a walk would be the most visible obstruction available.
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

                for (var s = -1; s <= 1; s += 2)
                {
                    var group = WeightedRoll.Pick(trees, total, rng);
                    if (group == null || group.prefabs.Length == 0)
                        continue;

                    for (var along = RowStart; along <= RowEnd; along += TreeSpacing)
                    {
                        var prefab = group.prefabs[rng.Next(group.prefabs.Length)];
                        if (!prefab)
                            continue;

                        var jitterAlong = along + Jitter(rng, RowJitterAlong);
                        var jitterAcross = s * (RowOffset + Jitter(rng, RowJitterAcross));

                        var offset = leg * jitterAlong + side * jitterAcross;
                        if (OnPathOrPlaza(offset) || OutsideCell(offset))
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
            var outer = CellHalf - EdgeInset;

            for (var qx = -1; qx <= 1; qx += 2)
            for (var qz = -1; qz <= 1; qz += 2)
            for (var i = 0; i < ClustersPerQuadrant; i++)
            {
                var seed = new Vector2(
                    qx * Range(rng, inner, outer),
                    qz * Range(rng, inner, outer));

                var position = centre + new Vector3(seed.x, 0f, seed.y);
                Cluster(centre, position, ClusterRadius, true,
                        undergrowth, parent, spawn, rng, occupied, placed);
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
                    if (OnPathOrPlaza(local) || OutsideCell(local))
                        continue;
                }

                // A bush has no front, so a free yaw is honest variety here rather than the
                // tell it is on a bench.
                Spawn(prefab, point, (float)rng.NextDouble() * 360f,
                      Range(rng, PlantScaleMin, PlantScaleMax), parent, spawn, occupied, placed);
            }
        }

        // ------------------------------------------------------------------ hedge

        /// <summary>
        /// A hedge along the outer boundary of the park's cells, broken where a walk leaves the
        /// park.
        ///
        /// It runs the CELL boundary, not the expanded block rect, for the same reason the ground
        /// does: the park tiles cover the cells only, and a hedge out on the block rect would
        /// float on the road tile's verge with grass showing on both sides of it. Internal edges
        /// between two park cells get nothing - the park is one space, not a set of walled
        /// compartments.
        /// </summary>
        static void BuildHedge(
            CityGrid grid,
            List<Vector2Int> cells,
            PrefabDatabase.ZonePalette palette,
            Transform parent,
            SpawnPrefab spawn,
            List<GameObject> placed)
        {
            if (!palette.fenceSegment)
                return;

            var inBlock = new HashSet<Vector2Int>(cells);

            foreach (var cell in cells)
            {
                foreach (var leg in Legs)
                {
                    var step = new Vector2Int(Mathf.RoundToInt(leg.x), Mathf.RoundToInt(leg.y));
                    if (inBlock.Contains(cell + step))
                        continue;

                    var centre = grid.CellToWorld(cell);
                    var side = new Vector2(-leg.y, leg.x);

                    // Walk the side from one corner to the other, starting inside the corner so
                    // two sides meeting there do not stack a doubled segment on the diagonal.
                    var origin = new Vector3(
                        centre.x + leg.x * (CellHalf - HedgeInset) - side.x * CellHalf,
                        GroundPlacer.BlockLift,
                        centre.z + leg.y * (CellHalf - HedgeInset) - side.y * CellHalf);

                    var along = new Vector3(side.x, 0f, side.y);
                    var gap = CellHalf;

                    FenceRun.Lay(palette.fenceSegment, origin, along,
                                 HedgeInset, gap - HedgeGateHalf, parent, spawn, placed);
                    FenceRun.Lay(palette.fenceSegment, origin, along,
                                 gap + HedgeGateHalf, CityGrid.CellSize - HedgeInset,
                                 parent, spawn, placed);
                }
            }
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

        static bool OutsideCell(Vector2 offset) =>
            Mathf.Abs(offset.x) > CellHalf - EdgeInset ||
            Mathf.Abs(offset.y) > CellHalf - EdgeInset;

        /// <summary>
        /// Instantiates one park piece, rejecting it if it would sit inside something already
        /// placed. The footprint is measured at the yaw and scale it will actually be built at,
        /// so a rotated 7m lime is tested as the 7m obstacle it is.
        /// </summary>
        static void Spawn(
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
                return;

            var footprint = PrefabBounds.FootprintXZ(prefab, yaw) * scale;
            var bounds = new Bounds(new Vector3(position.x, 0f, position.z),
                                    new Vector3(footprint.x, 1f, footprint.y));

            foreach (var existing in occupied)
                if (existing.Intersects(bounds))
                    return;

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
