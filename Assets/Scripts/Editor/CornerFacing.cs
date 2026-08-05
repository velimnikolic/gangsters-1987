using UnityEngine;
using LivingCity.Generation;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// Works out which way a terrace CORNER piece has to be turned, by measuring the mesh.
    ///
    /// A corner piece is the one kind of prefab for which "which local direction is the front"
    /// is not a well-formed question: it has TWO finished elevations and two blind party walls,
    /// so what has to be aligned is a QUADRANT, not a direction. An oracle that looks for the
    /// nearest road - which is how the rest of PrefabDatabase.facadeYawFixes was derived - picks
    /// one of the two streets arbitrarily and so returns the right answer or the right answer
    /// plus or minus 90 degrees. That is exactly the failure it produced: one elevation facing
    /// its street and the other showing a blank wall.
    ///
    /// Measuring instead of guessing also settles a question that reasoning cannot. FBX is
    /// right-handed and Unity is left-handed, and the importer negates X, so the quadrant read
    /// off the raw FBX vertex arrays is the MIRROR of the quadrant the prefab actually has. That
    /// flip is invisible on every other entry in the table - they are all decided by a Z face, or
    /// are symmetric in X - and visible only on the corner pieces, which are the only entries
    /// that depend on a quadrant. Reading the mesh through the prefab's own transforms, as this
    /// does, is in Unity's axes by construction and the question cannot arise.
    /// </summary>
    public static class CornerFacing
    {
        /// <summary>
        /// Bin width for the plane histogram. Small enough to separate a wall from the roof
        /// cornice in front of it, which is the whole reason this is a histogram rather than a
        /// slab test - see OuterFraction.
        /// </summary>
        const float PlaneBin = 0.1f;

        /// <summary>
        /// How much of the footprint, measured in from each side, the wall is looked for in.
        /// A third rather than a thin slab against the bounding box: the 5-floor corner's walls
        /// stand about 1.2m inside its own bounds, because the roof cornice is what reaches the
        /// AABB. Sampling at the AABB plane profiles the cornice and gets the answer backwards.
        /// </summary>
        const float OuterFraction = 1f / 3f;

        /// <summary>
        /// Least ratio between the weaker facade and the stronger party wall for the reading to
        /// count. The two pieces in the pack come out at 6:1 and 10:1, so 2 rejects a piece that
        /// is not really a corner rather than trimming a real one.
        /// </summary>
        const float DecisiveRatio = 2f;

        /// <summary>
        /// The extraYaw a corner piece needs, or null when the mesh does not answer clearly and
        /// the caller should keep whatever it already had.
        ///
        /// The value is the same for all four corners of a lot, and that is not a coincidence:
        /// the two sides meeting at corner i are sides[i] and sides[i-1], whose outward
        /// directions are always 90 degrees apart in the same sense, so the bisector of the pair
        /// is always YawFor(sides[i].outward) + 45. BlockBuilder rotates by YawFor(outward) +
        /// extraYaw, which is therefore "bisector - 45"; aligning the piece's own quadrant
        /// bisector beta to it gives extraYaw = 45 - beta.
        /// </summary>
        public static float? Measure(GameObject prefab, out string report)
        {
            report = "no mesh";
            if (!prefab)
                return null;

            var points = LocalPoints(prefab);
            if (points == null || points.Count < 32)
                return null;

            var bounds = PrefabBounds.Get(prefab);

            // Score per outward direction, in the order BlockBuilder thinks in: +X, +Z, -X, -Z.
            // Adjacent in this array means adjacent in space, which is what lets the pair search
            // below be a plain look at consecutive entries.
            var score = new float[4];
            score[0] = WallDensity(points, axis: 0, positive: true, bounds);
            score[1] = WallDensity(points, axis: 2, positive: true, bounds);
            score[2] = WallDensity(points, axis: 0, positive: false, bounds);
            score[3] = WallDensity(points, axis: 2, positive: false, bounds);

            // The finished pair is the adjacent pair whose WEAKER member is strongest - not the
            // pair with the best sum. A sum lets one very dense elevation carry a blank wall next
            // to it into the answer.
            var best = -1;
            var bestWeak = -1f;
            for (var i = 0; i < 4; i++)
            {
                var weak = Mathf.Min(score[i], score[(i + 1) & 3]);
                if (weak <= bestWeak)
                    continue;

                bestWeak = weak;
                best = i;
            }

            // The other two are the party walls. Compare against the STRONGER of them, so a piece
            // with three lively faces is rejected rather than resolved by a hair.
            var blank = Mathf.Max(score[(best + 2) & 3], score[(best + 3) & 3]);

            // Directions of the chosen pair, and the yaw of their bisector in the prefab's own
            // frame. Summing the two unit vectors is the bisector; no special cases for wrap.
            var a = Direction(best);
            var b = Direction((best + 1) & 3);
            var beta = Mathf.Atan2(a.x + b.x, a.z + b.z) * Mathf.Rad2Deg;

            var extraYaw = Mathf.Round((45f - beta) / 90f) * 90f;
            extraYaw = Mathf.Repeat(extraYaw, 360f);

            report = $"+X {score[0]:F0}  +Z {score[1]:F0}  -X {score[2]:F0}  -Z {score[3]:F0}" +
                     $"  ->  facades on {Label(best)}/{Label((best + 1) & 3)}" +
                     $", beta {Mathf.Repeat(beta, 360f):F0}, extraYaw {extraYaw:F0}" +
                     $"  (facade/wall {(blank > 0f ? bestWeak / blank : 999f):F1}:1)";

            if (bestWeak < blank * DecisiveRatio)
            {
                report += "  - REJECTED, not decisively a corner piece";
                return null;
            }

            return extraYaw;
        }

        static Vector3 Direction(int index) => index switch
        {
            0 => Vector3.right,
            1 => Vector3.forward,
            2 => Vector3.left,
            _ => Vector3.back,
        };

        static string Label(int index) => index switch
        {
            0 => "+X",
            1 => "+Z",
            2 => "-X",
            _ => "-Z",
        };

        /// <summary>
        /// Vertex count of the densest plane in the outer third of one side - the wall.
        ///
        /// A finished elevation carries window and door geometry and so puts hundreds of vertices
        /// on one plane; a party wall is a flat quad and puts a handful. Taking the densest plane
        /// rather than everything in the slab is what keeps a deep porch or a cornice from
        /// outvoting the wall behind it.
        /// </summary>
        static float WallDensity(System.Collections.Generic.List<Vector3> points,
                                 int axis, bool positive, Bounds bounds)
        {
            var min = bounds.min[axis];
            var max = bounds.max[axis];
            var span = max - min;
            if (span <= 0f)
                return 0f;

            var limit = positive ? max - span * OuterFraction : min + span * OuterFraction;

            var histogram = new System.Collections.Generic.Dictionary<int, int>();
            var densest = 0;

            foreach (var point in points)
            {
                var v = point[axis];
                if (positive ? v < limit : v > limit)
                    continue;

                var bin = Mathf.RoundToInt(v / PlaneBin);
                histogram.TryGetValue(bin, out var count);
                histogram[bin] = ++count;

                if (count > densest)
                    densest = count;
            }

            return densest;
        }

        /// <summary>
        /// Every mesh vertex in the prefab root's local space, composed the same way
        /// PrefabBounds.Get composes its corners so the two cannot disagree about what "local"
        /// means. Returns null when the mesh data is unreachable.
        /// </summary>
        static System.Collections.Generic.List<Vector3> LocalPoints(GameObject prefab)
        {
            var toLocal = prefab.transform.worldToLocalMatrix;
            var points = new System.Collections.Generic.List<Vector3>(4096);

            foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>())
            {
                var mesh = filter.sharedMesh;
                if (!mesh)
                    continue;

                Vector3[] vertices;
                try
                {
                    // Legal here and not at runtime: the Editor keeps full mesh data for an
                    // imported asset whether or not Read/Write is enabled on the importer.
                    vertices = mesh.vertices;
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[CornerFacing] {prefab.name}: mesh unreadable ({e.Message})");
                    return null;
                }

                var matrix = toLocal * filter.transform.localToWorldMatrix;
                foreach (var vertex in vertices)
                    points.Add(matrix.MultiplyPoint3x4(vertex));
            }

            return points;
        }
    }
}
