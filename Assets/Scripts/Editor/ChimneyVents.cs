using System.Collections.Generic;
using UnityEngine;
using LivingCity.Generation;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// Finds the chimney mouths on a works building by measuring the mesh.
    ///
    /// The pack gives nothing to hang smoke off. Every industrial prefab is ONE GameObject with
    /// one MeshFilter pointing at an FBX - there is no child transform called "chimney", no empty
    /// at the mouth, nothing named at all. So the mouths have to be found in the geometry.
    ///
    /// The naive test - "a narrow cluster of vertices near the top" - does not work, and failing
    /// to notice that is how a warehouse ends up smoking from five points along its ridge.
    /// industry-factory-hall and industry-building are topped by rows of roof lanterns which are
    /// every bit as narrow as a chimney. What separates them is not width but RISE: a chimney
    /// stands metres above the roof around it, and a lantern sits on it. So each candidate is
    /// measured against the roof in its own neighbourhood, and only the ones that clear it by
    /// MinRise survive.
    ///
    /// Measured in Unity space through the prefab's own transforms, exactly as CornerFacing does,
    /// and for the reason spelled out there: the FBX importer negates X, so a mouth read off the
    /// raw vertex arrays is mirrored. Most of the catalogue hides that - industry-factory's twin
    /// stacks are symmetric at x = +/-4.22 and industry-factory-old's sits at x = 0 - but
    /// industry-refinery has a single stack off to one side, and it is the case that would have
    /// put the smoke at the wrong end of the building.
    /// </summary>
    public static class ChimneyVents
    {
        /// <summary>
        /// How far down from the apex to look for mouths. Deep enough to catch a shorter second
        /// stack, shallow enough that a whole pitched roof does not qualify.
        /// </summary>
        const float TopBand = 3f;

        /// <summary>Two vertices join the same cluster within this distance in XZ.</summary>
        const float ClusterLink = 2f;

        /// <summary>
        /// Widest a chimney mouth may be, as a radius. industry-factory's stacks measure about
        /// 3.2 including the flare at the base of the band, so this admits them and rejects a
        /// roof ridge.
        /// </summary>
        const float MaxRadius = 3.6f;

        /// <summary>
        /// How far a cluster must stand above the roof around it. This is the test that matters -
        /// see the class note.
        ///
        /// Not a guess. Measured across the whole works catalogue, the real stacks clear their
        /// roofs by 5.6m (industry-refinery), 8.6m (industry-factory's twins), 14.6m
        /// (industry-factory-old) and 18.4m (chimney-big); the roof furniture that has to be
        /// rejected - the lantern rows on industry-factory-hall and industry-building - clears
        /// its roof by 3.1 to 3.2m. 4.5 sits between the two populations with over a metre of
        /// margin either side. At 3 the lanterns pass and a warehouse smokes from seven points.
        /// </summary>
        const float MinRise = 4.5f;

        /// <summary>Radius around a candidate that counts as "the roof around it".</summary>
        const float NeighbourhoodRadius = 9f;

        /// <summary>XZ cell the roof height is sampled in. See RoofAround.</summary>
        const float RoofBin = 1f;

        /// <summary>Fewest vertices for a cluster to be geometry rather than noise.</summary>
        const int MinVertices = 8;

        /// <summary>
        /// Chimney mouths on a prefab, in its local space. Empty is the normal answer for most of
        /// the catalogue - a warehouse has no chimney - and not a failure.
        /// </summary>
        public static List<Vector3> Measure(GameObject prefab, out string report)
        {
            var mouths = new List<Vector3>();
            report = "no mesh";

            if (!prefab)
                return mouths;

            var points = LocalPoints(prefab);
            if (points == null || points.Count < 32)
                return mouths;

            var apex = float.MinValue;
            foreach (var point in points)
                if (point.y > apex)
                    apex = point.y;

            var top = new List<Vector3>();
            foreach (var point in points)
                if (point.y >= apex - TopBand)
                    top.Add(point);

            var rejected = 0;
            var lines = new List<string>();

            foreach (var cluster in Cluster(top))
            {
                if (cluster.Count < MinVertices)
                    continue;

                var centre = Vector3.zero;
                var head = float.MinValue;
                foreach (var point in cluster)
                {
                    centre += point;
                    if (point.y > head)
                        head = point.y;
                }
                centre /= cluster.Count;

                var radius = 0f;
                foreach (var point in cluster)
                {
                    var d = new Vector2(point.x - centre.x, point.z - centre.z).magnitude;
                    if (d > radius)
                        radius = d;
                }

                if (radius > MaxRadius)
                    continue;

                var roof = RoofAround(points, centre);
                var rise = head - roof;

                if (rise < MinRise)
                {
                    rejected++;
                    continue;
                }

                var mouth = new Vector3(centre.x, head, centre.z);
                mouths.Add(mouth);
                lines.Add($"({mouth.x:F2}, {mouth.z:F2}) top {mouth.y:F2} r {radius:F2} rise {rise:F1}");
            }

            // Sorted so the table a bootstrap writes does not depend on cluster discovery order,
            // which depends on vertex order and is not something to build a diff on.
            mouths.Sort((a, b) =>
            {
                var byX = a.x.CompareTo(b.x);
                return byX != 0 ? byX : a.z.CompareTo(b.z);
            });

            report = mouths.Count == 0
                ? $"no chimney ({rejected} cluster(s) rejected as roof furniture)"
                : $"{mouths.Count} vent(s): {string.Join("; ", lines)}" +
                  (rejected > 0 ? $"  ({rejected} rejected as roof furniture)" : string.Empty);

            return mouths;
        }

        /// <summary>
        /// Height of the roof surrounding a candidate: the highest point in each 1m cell of an
        /// annulus around it, then the MEDIAN across those cells.
        ///
        /// Both halves of that matter. Per-cell maxima are needed because a plain average over
        /// raw vertices drags in the walls below and reports the roof as far lower than it is,
        /// which lets every lantern clear MinRise. And the median across cells rather than the
        /// maximum is what makes a TWIN-stacked building work: industry-factory carries two
        /// chimneys 8.4m apart, both inside the other's neighbourhood and both the same height,
        /// so a max-based reading has each one report the other as its roof, measure a rise of
        /// 8cm, and reject itself. The pack's most recognisable factory then produced no smoke
        /// at all. A second stack occupies a handful of cells; the roof occupies the rest, and
        /// the median follows the roof.
        /// </summary>
        static float RoofAround(List<Vector3> points, Vector3 centre)
        {
            var cells = new Dictionary<(int, int), float>();

            foreach (var point in points)
            {
                var d = new Vector2(point.x - centre.x, point.z - centre.z).magnitude;
                if (d < MaxRadius || d > NeighbourhoodRadius)
                    continue;

                var key = (Mathf.FloorToInt(point.x / RoofBin), Mathf.FloorToInt(point.z / RoofBin));
                if (!cells.TryGetValue(key, out var top) || point.y > top)
                    cells[key] = point.y;
            }

            // Nothing around it at all - a free-standing chimney like chimney-big. Its own base
            // is the reference, and it clears it by its whole height.
            if (cells.Count == 0)
                return 0f;

            var tops = new List<float>(cells.Values);
            tops.Sort();
            return tops[tops.Count / 2];
        }

        /// <summary>Single-link clustering in XZ. The counts here are hundreds, so O(n^2) is fine.</summary>
        static List<List<Vector3>> Cluster(List<Vector3> points)
        {
            var clusters = new List<List<Vector3>>();
            var taken = new bool[points.Count];

            for (var i = 0; i < points.Count; i++)
            {
                if (taken[i])
                    continue;

                var group = new List<Vector3> { points[i] };
                taken[i] = true;

                // Breadth-first over the link radius: a chimney is a column of rings and a
                // single pass would split it into one cluster per ring.
                for (var head = 0; head < group.Count; head++)
                {
                    var from = group[head];

                    for (var j = 0; j < points.Count; j++)
                    {
                        if (taken[j])
                            continue;

                        var d = new Vector2(points[j].x - from.x, points[j].z - from.z).magnitude;
                        if (d > ClusterLink)
                            continue;

                        taken[j] = true;
                        group.Add(points[j]);
                    }
                }

                clusters.Add(group);
            }

            return clusters;
        }

        /// <summary>
        /// Every mesh vertex in the prefab root's local space, composed the way PrefabBounds
        /// composes its corners so the two cannot disagree about what "local" means. Lifted from
        /// CornerFacing, which needs the identical thing.
        /// </summary>
        static List<Vector3> LocalPoints(GameObject prefab)
        {
            var toLocal = prefab.transform.worldToLocalMatrix;
            var points = new List<Vector3>(4096);

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
                    Debug.LogWarning($"[ChimneyVents] {prefab.name}: mesh unreadable ({e.Message})");
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
