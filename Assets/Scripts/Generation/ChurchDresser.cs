using System.Collections.Generic;
using UnityEngine;
using LivingCity.Data;

namespace LivingCity.Generation
{
    /// <summary>
    /// Lays out the church block: the church in the middle of a walled garden, a stone fence
    /// with one gate onto the street, a painted walk from the gate to the door, benches along
    /// the walk, a market stand beside the entrance and trees in the corners.
    ///
    /// This is a sibling of ParkDresser, not a client of it, and the reason is the ground. The
    /// park is laid on tile-park, whose baked Path components run a cross of walks through the
    /// cell centre - exactly where a 21.4 x 15.9m church has to stand. Reusing that tile would
    /// have pedestrians pathing through the nave, so the church block takes the plain grass
    /// slab route instead (groundIsTilePerCell false), which carries no Path components at all:
    /// nobody navigates into the churchyard, the same as every other walled civic yard. The
    /// walk up to the door is therefore GroundPaint, not paving with nodes - purely visual.
    ///
    /// Everything from the fence in is arranged off ONE line, the gate-to-door axis: the walk
    /// paints it, the benches flank it, the stand sits beside its start and the planting keeps
    /// out of its corridor. That is what makes the block read as grounds rather than as a lawn
    /// with objects on it - the same lesson ParkDresser's rewrite learned from the old scatter.
    ///
    /// The church is building-museum, not scaled: measured at 21.41 x 15.90 and 12.89m tall,
    /// it fits the single-cell block this zone is capped to (46m across) with ~12m of garden
    /// each side, and it is SHORTER than the 23.8m terraces around it. The civic three take
    /// landmarkScale 0.5 because their buildings tower from one cell; the museum does not.
    /// </summary>
    public static class ChurchDresser
    {
        /// <summary>Fence line in from the block rect, so the corner piers do not overhang the
        /// pavement - same value, same reason as ParkingLotDresser.</summary>
        const float FenceInset = 0.6f;

        /// <summary>Opening in the fence. The park's hedge gates are 6m too, and the widest
        /// thing that has to pass visual muster beside it is the 3.06m market stand.</summary>
        const float GateWidth = 6f;

        /// <summary>Painted width of the walk - GroundPlacer's footpath width.</summary>
        const float WalkWidth = 2.5f;

        /// <summary>Half-width kept clear of planting either side of the walk's centreline:
        /// the paving is 1.25 each side, plus grass enough that a crown at PlantScaleMax does
        /// not lean over the route.</summary>
        const float WalkHalfClear = 2f;

        /// <summary>The stand, in from the fence line - ParkingLotDresser sets its booth at 3
        /// and the stand is a shallower prop (1.74 deep).</summary>
        const float StandSetback = 2.5f;

        /// <summary>Lateral gap between the stand and the edge of the opening, so it flanks
        /// the way in rather than standing in it.</summary>
        const float StandClearance = 2f;

        /// <summary>Where a bench may sit along the walk, measured in from the gate.</summary>
        static readonly float[] BenchStations = { 6f, 11f };

        /// <summary>Clear gap between the bench back and the paving.</summary>
        const float BenchClearance = 0.3f;

        /// <summary>Planting in from the fence line, so a crown does not hang over the wall.
        /// Deeper than ParkDresser's 1.5 because the wall is solid stone: a shrub clipping
        /// through masonry shows in a way one poking through a hedge does not.</summary>
        const float EdgeInset = 2f;

        /// <summary>How far a corner tree may wander in from its corner, on each axis.</summary>
        const float CornerSpread = 6f;
        const int TreesPerCorner = 2;

        const int ClusterCount = 5;
        const int ClusterMin = 2;
        const int ClusterMax = 5;
        const float ClusterRadius = 1.8f;

        const float TreeScaleMin = 0.85f;
        const float TreeScaleMax = 1.15f;
        const float PlantScaleMin = 0.8f;
        const float PlantScaleMax = 1.3f;

        public static void Build(
            CityGrid grid,
            List<Vector2Int> cells,
            Vector2 min,
            Vector2 max,
            PrefabDatabase.ZonePalette palette,
            PrefabDatabase prefabs,
            CityConfig config,
            Transform parent,
            SpawnPrefab spawn,
            System.Random rng,
            List<Bounds> occupied,
            List<GameObject> placed,
            List<BuildingTinter.Target> tints,
            List<Bounds> gateKeepOuts)
        {
            if (cells == null || cells.Count == 0)
                return;

            var outward = EntranceOutward(grid, cells, rng);
            var along = Vector3.Cross(Vector3.up, outward);

            var fenceMin = new Vector2(min.x + FenceInset, min.y + FenceInset);
            var fenceMax = new Vector2(max.x - FenceInset, max.y - FenceInset);
            var gateCentre = GateCentre(fenceMin, fenceMax, outward);

            var gate = new PerimeterFence.Gate
            {
                Has = true,
                Centre = gateCentre,
                Outward = outward,
                Width = GateWidth,
            };

            PerimeterFence.Build(fenceMin, fenceMax, gate,
                palette.fenceSegment, palette.fencePost, parent, spawn, placed);

            // The gateway is the one way through the churchyard wall - keep the verge in
            // front of it clear of street trees, same as a works gate.
            gateKeepOuts?.Add(PerimeterFence.Approach(gate));

            var centre = new Vector3((min.x + max.x) * 0.5f, GroundPlacer.BlockLift,
                                     (min.y + max.y) * 0.5f);

            var churchFront = PlaceChurch(centre, outward, palette, prefabs,
                                          parent, spawn, rng, occupied, placed, tints);

            PaintWalk(gateCentre, churchFront, prefabs, parent, placed);
            PlaceStand(gateCentre, outward, along, palette, prefabs,
                       parent, spawn, rng, occupied, placed);
            PlaceBenches(gateCentre, outward, along, palette,
                         parent, spawn, rng, occupied, placed);
            PlantCorners(fenceMin, fenceMax, gateCentre, churchFront, palette,
                         parent, spawn, rng, occupied, placed);
            PlantUndergrowth(fenceMin, fenceMax, gateCentre, churchFront, palette,
                             parent, spawn, rng, occupied, placed);
        }

        // ------------------------------------------------------------------ entrance

        /// <summary>
        /// Which way the gate faces. The avenue if the block fronts it - a church addresses the
        /// grandest street it can - else any street side, else south for the map-edge block
        /// that fronts nothing (RoadSides explains how that happens). One draw either way, from
        /// a list built first, so the rng sequence does not depend on what the probes found.
        /// </summary>
        static Vector3 EntranceOutward(CityGrid grid, List<Vector2Int> cells, System.Random rng)
        {
            var minCell = new Vector2Int(int.MaxValue, int.MaxValue);
            var maxCell = new Vector2Int(int.MinValue, int.MinValue);
            foreach (var cell in cells)
            {
                minCell = Vector2Int.Min(minCell, cell);
                maxCell = Vector2Int.Max(maxCell, cell);
            }

            var roadSides = BlockBuilder.RoadSides(grid, cells);

            // The same four probes as BlockRect, so the side this reads as the avenue is the
            // side whose clearance was already cut deeper.
            var candidates = new List<(Sides side, Vector3 outward, bool main)>(4);
            void Consider(Sides side, Vector3 outward, int x, int z)
            {
                if ((roadSides & side) != 0)
                    candidates.Add((side, outward, grid.IsMainRoad(x, z)));
            }

            Consider(Sides.West, Vector3.left, minCell.x - 1, minCell.y);
            Consider(Sides.East, Vector3.right, maxCell.x + 1, maxCell.y);
            Consider(Sides.South, Vector3.back, minCell.x, minCell.y - 1);
            Consider(Sides.North, Vector3.forward, maxCell.x, maxCell.y + 1);

            if (candidates.Count == 0)
                return Vector3.back;

            var mains = candidates.FindAll(c => c.main);
            var pool = mains.Count > 0 ? mains : candidates;
            return pool[rng.Next(pool.Count)].outward;
        }

        /// <summary>Midpoint of the fence side the gate opens through, at y 0 - the fence's own
        /// plane, per PerimeterFence's convention.</summary>
        static Vector3 GateCentre(Vector2 fenceMin, Vector2 fenceMax, Vector3 outward)
        {
            var centre = new Vector3((fenceMin.x + fenceMax.x) * 0.5f, 0f,
                                     (fenceMin.y + fenceMax.y) * 0.5f);
            var half = new Vector3((fenceMax.x - fenceMin.x) * 0.5f, 0f,
                                   (fenceMax.y - fenceMin.y) * 0.5f);
            return centre + Vector3.Scale(outward, half);
        }

        // ------------------------------------------------------------------ church

        /// <summary>
        /// The church at the exact centre of the block, facing the gate. Returns where its
        /// front wall meets the walk, which is the far end of everything else's axis.
        /// Front is the pack's local +Z, corrected per prefab through ExtraYawFor - the same
        /// pair of rules every landmark placement in BlockBuilder follows.
        /// </summary>
        static Vector3 PlaceChurch(
            Vector3 centre,
            Vector3 outward,
            PrefabDatabase.ZonePalette palette,
            PrefabDatabase prefabs,
            Transform parent,
            SpawnPrefab spawn,
            System.Random rng,
            List<Bounds> occupied,
            List<GameObject> placed,
            List<BuildingTinter.Target> tints)
        {
            if (palette.landmarks == null || palette.landmarks.Length == 0)
            {
                Debug.LogWarning("[ChurchDresser] Church palette has no landmarks - the block " +
                                 "gets its garden but no church. Re-run Tools/City/Create or " +
                                 "Refresh Config Assets.");
                return centre;
            }

            var prefab = palette.landmarks[rng.Next(palette.landmarks.Length)];
            if (!prefab)
                return centre;

            var yaw = Mathf.Atan2(outward.x, outward.z) * Mathf.Rad2Deg
                    + prefabs.ExtraYawFor(prefab);
            var scale = palette.landmarkScale > 0f ? palette.landmarkScale : 1f;

            var church = ParkDresser.Spawn(prefab, centre, yaw, scale,
                                           parent, spawn, occupied, placed);
            if (church)
                tints.Add(new BuildingTinter.Target(church, commercial: false));

            // Half the footprint along the entrance axis, measured at the yaw and scale it was
            // built at - the walk must stop at the door, not at the block centre inside it.
            var footprint = PrefabBounds.FootprintXZ(prefab, yaw) * scale;
            var depth = Mathf.Abs(outward.x) > 0.5f ? footprint.x : footprint.y;
            return centre + outward * (depth * 0.5f);
        }

        // ------------------------------------------------------------------ walk

        /// <summary>
        /// One painted stroke, gate to door. Paint rather than paving because the slab under it
        /// is a stretched ground tile with no Path nodes - see the class comment - so the walk
        /// only has to LOOK walked, and GroundPaint is how this project draws flat marks
        /// without a decal pass.
        /// </summary>
        static void PaintWalk(
            Vector3 gateCentre,
            Vector3 churchFront,
            PrefabDatabase prefabs,
            Transform parent,
            List<GameObject> placed)
        {
            var strokes = new List<GroundPaint.Stroke>
            {
                new(new Vector2(gateCentre.x, gateCentre.z),
                    new Vector2(churchFront.x, churchFront.z),
                    WalkWidth),
            };

            var walk = GroundPaint.Emit(strokes, prefabs.paintLightMaterial,
                                        GroundPaint.PaintLift, "church_walk", parent);
            if (walk)
                placed.Add(walk);
        }

        // ------------------------------------------------------------------ stand

        /// <summary>
        /// The market stand, just inside the gate and beside the opening, its counter facing
        /// the way in - ParkingLotDresser's booth placement with the boom left off. Always
        /// placed: the stand outside the church is part of what the block IS, not an optional
        /// garnish, so there is no chance roll to fail.
        /// </summary>
        static void PlaceStand(
            Vector3 gateCentre,
            Vector3 outward,
            Vector3 along,
            PrefabDatabase.ZonePalette palette,
            PrefabDatabase prefabs,
            Transform parent,
            SpawnPrefab spawn,
            System.Random rng,
            List<Bounds> occupied,
            List<GameObject> placed)
        {
            if (palette.kioskPrefabs == null || palette.kioskPrefabs.Length == 0)
                return;

            var prefab = palette.kioskPrefabs[rng.Next(palette.kioskPrefabs.Length)];
            if (!prefab)
                return;

            var side = rng.Next(2) == 0 ? -1f : 1f;
            var position = gateCentre
                         - outward * StandSetback
                         + along * (side * (GateWidth * 0.5f + StandClearance));
            position.y = GroundPlacer.BlockLift;

            var facing = gateCentre - position;
            facing.y = 0f;
            var yaw = Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg
                    + prefabs.ExtraYawFor(prefab);

            ParkDresser.Spawn(prefab, position, yaw, 1f, parent, spawn, occupied, placed);
        }

        // ------------------------------------------------------------------ benches

        /// <summary>
        /// Benches flanking the walk, facing it - ParkDresser's bench discipline on this
        /// block's one axis. Four candidates go in and the occupancy test throws out any that
        /// reach the church, so a short walk simply seats fewer.
        /// </summary>
        static void PlaceBenches(
            Vector3 gateCentre,
            Vector3 outward,
            Vector3 along,
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

            foreach (var station in BenchStations)
            {
                for (var s = -1; s <= 1; s += 2)
                {
                    var bench = benches[rng.Next(benches.Length)];
                    if (!bench)
                        continue;

                    // Set back by its own depth so the seat edge clears the paint whichever
                    // bench was drawn - the short side IS the depth, both pack benches being
                    // longer along the seat (2.93 x 0.58 and 1.67 x 0.71).
                    var footprint = PrefabBounds.FootprintXZ(bench, 0f);
                    var depth = Mathf.Min(footprint.x, footprint.y);
                    var lateral = WalkWidth * 0.5f + depth * 0.5f + BenchClearance;

                    var position = gateCentre
                                 - outward * station
                                 + along * (s * lateral);
                    position.y = GroundPlacer.BlockLift;

                    // Look back across the walk.
                    var face = along * -s;
                    var yaw = Mathf.Atan2(face.x, face.z) * Mathf.Rad2Deg;

                    ParkDresser.Spawn(bench, position, yaw, 1f, parent, spawn, occupied, placed);
                }
            }
        }

        // ------------------------------------------------------------------ planting

        /// <summary>
        /// Trees in the four corners of the garden, one species per corner drawn from the same
        /// weighted groups the park plants from. Per-corner rather than per-tree for
        /// ParkDresser's reason: the variety belongs between plantings, not within one.
        /// </summary>
        static void PlantCorners(
            Vector2 fenceMin,
            Vector2 fenceMax,
            Vector3 gateCentre,
            Vector3 churchFront,
            PrefabDatabase.ZonePalette palette,
            Transform parent,
            SpawnPrefab spawn,
            System.Random rng,
            List<Bounds> occupied,
            List<GameObject> placed)
        {
            var groups = UsableTreeGroups(palette);
            if (groups.Count == 0)
                return;

            var total = 0f;
            foreach (var group in groups)
                total += group.weight;

            for (var cx = 0; cx <= 1; cx++)
            for (var cz = 0; cz <= 1; cz++)
            {
                var group = WeightedRoll.Pick(groups, total, rng);
                if (group == null || group.prefabs.Length == 0)
                    continue;

                var corner = new Vector2(cx == 0 ? fenceMin.x : fenceMax.x,
                                         cz == 0 ? fenceMin.y : fenceMax.y);
                var inward = new Vector2(cx == 0 ? 1f : -1f, cz == 0 ? 1f : -1f);

                for (var i = 0; i < TreesPerCorner; i++)
                {
                    var prefab = group.prefabs[rng.Next(group.prefabs.Length)];
                    if (!prefab)
                        continue;

                    var point = corner + new Vector2(
                        inward.x * (EdgeInset + (float)rng.NextDouble() * CornerSpread),
                        inward.y * (EdgeInset + (float)rng.NextDouble() * CornerSpread));

                    if (InWalkCorridor(point, gateCentre, churchFront))
                        continue;

                    var position = new Vector3(point.x, GroundPlacer.BlockLift, point.y);
                    var scale = Range(rng, TreeScaleMin, TreeScaleMax);

                    ParkDresser.Spawn(prefab, position, (float)rng.NextDouble() * 360f, scale,
                                      parent, spawn, occupied, placed);
                }
            }
        }

        /// <summary>
        /// Shrubs, flowers and grass tufts in small clusters over the lawn - ParkDresser's
        /// clustering argument holds unchanged: three tufts near each other read as a bed
        /// someone planted, an even sprinkle reads as noise. Each member is corridor- and
        /// boundary-tested in its own right, because a cluster seeded near the walk reaches
        /// over it; the church and the furniture reject through the occupancy test.
        /// </summary>
        static void PlantUndergrowth(
            Vector2 fenceMin,
            Vector2 fenceMax,
            Vector3 gateCentre,
            Vector3 churchFront,
            PrefabDatabase.ZonePalette palette,
            Transform parent,
            SpawnPrefab spawn,
            System.Random rng,
            List<Bounds> occupied,
            List<GameObject> placed)
        {
            var options = palette.parkUndergrowth;
            if (options == null || options.Length == 0)
                return;

            for (var i = 0; i < ClusterCount; i++)
            {
                var seed = new Vector2(
                    Range(rng, fenceMin.x + EdgeInset, fenceMax.x - EdgeInset),
                    Range(rng, fenceMin.y + EdgeInset, fenceMax.y - EdgeInset));

                var count = rng.Next(ClusterMin, ClusterMax);
                for (var k = 0; k < count; k++)
                {
                    var prefab = options[rng.Next(options.Length)];
                    if (!prefab)
                        continue;

                    var angle = (float)rng.NextDouble() * Mathf.PI * 2f;
                    var distance = (float)rng.NextDouble() * ClusterRadius;
                    var point = seed + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;

                    if (point.x < fenceMin.x + EdgeInset || point.x > fenceMax.x - EdgeInset ||
                        point.y < fenceMin.y + EdgeInset || point.y > fenceMax.y - EdgeInset)
                        continue;
                    if (InWalkCorridor(point, gateCentre, churchFront))
                        continue;

                    ParkDresser.Spawn(prefab,
                                      new Vector3(point.x, GroundPlacer.BlockLift, point.y),
                                      (float)rng.NextDouble() * 360f,
                                      Range(rng, PlantScaleMin, PlantScaleMax),
                                      parent, spawn, occupied, placed);
                }
            }
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>
        /// Whether a point stands in the walk's corridor - within WalkHalfClear of the
        /// gate-to-door segment, with a little run-off past each end so nothing crowds the
        /// gate opening or the doorstep.
        /// </summary>
        static bool InWalkCorridor(Vector2 point, Vector3 gateCentre, Vector3 churchFront)
        {
            var a = new Vector2(gateCentre.x, gateCentre.z);
            var b = new Vector2(churchFront.x, churchFront.z);

            var axis = b - a;
            var length = axis.magnitude;
            if (length < 0.001f)
                return (point - a).magnitude < WalkHalfClear;

            axis /= length;
            var local = point - a;
            var alongWalk = Vector2.Dot(local, axis);

            if (alongWalk < -1f || alongWalk > length + 1f)
                return false;

            var lateral = Mathf.Abs(local.x * axis.y - local.y * axis.x);
            return lateral < WalkHalfClear;
        }

        /// <summary>
        /// The palette's tree groups, filtered to the usable. No kerbside fallback here,
        /// unlike ParkDresser's: an unmigrated asset has no Church palette at all and never
        /// reaches this class, so an empty list means someone edited the palette and a bare
        /// lawn is the honest result.
        /// </summary>
        static List<PrefabDatabase.WeightedPrefabs> UsableTreeGroups(
            PrefabDatabase.ZonePalette palette)
        {
            var usable = new List<PrefabDatabase.WeightedPrefabs>();
            if (palette.parkTrees != null)
                foreach (var group in palette.parkTrees)
                    if (group != null && group.IsUsable)
                        usable.Add(group);
            return usable;
        }

        static float Range(System.Random rng, float min, float max) =>
            min + (float)rng.NextDouble() * (max - min);
    }
}
