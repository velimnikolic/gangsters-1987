using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// The city, looked over once by something that does not get bored: is there floor
    /// everywhere a person could stand, is anything standing in the air or sunk into the
    /// ground, and is anything inside anything else.
    ///
    /// It reads MESHES, not colliders. The town's ground is street kit and block plates
    /// and the merge takes their renderers off on the first frame - so the sweep works
    /// off MeshFilter.sharedMesh.bounds through each transform, which survives both. And
    /// it works on world AABBs, which OVER-cover a slanted or L-shaped piece: the test
    /// therefore misses holes it should catch (a gap under a big diagonal mesh) and
    /// never invents one. That bias is deliberate - a sweep that cries wolf over the
    /// whole city would be turned off by the second run.
    ///
    /// Everything it finds goes out as [audit] warnings and trace faults, the way the
    /// monkey's do, so one analyzer reads both.
    /// </summary>
    public sealed class CityAudit : MonoBehaviour
    {
        [Tooltip("Sim seconds to wait before the sweep - after the first-frame merge, so " +
                 "what is measured is the city as it will be played.")]
        public float after = 2f;

        [Tooltip("Metres a coverage cell is on a side. 4 is a pavement's width: a hole " +
                 "smaller than that is a seam, not a hole.")]
        public float cell = 4f;

        [Tooltip("Metres of ground a mesh may hang above before it is floating.")]
        public float floatSlack = 1.5f;

        [Tooltip("Metres a building may sink into the ground before it is buried.")]
        public float sinkSlack = 3f;

        public Transform blocks, geometry;

        bool _done;

        void Update()
        {
            if (_done || Time.timeSinceLevelLoad < after) return;
            _done = true;

            var clock = System.Diagnostics.Stopwatch.StartNew();
            var town = TownRect();
            Holes(town);
            Standing();
            Fronts();
            Debug.Log($"[audit] swept in {clock.ElapsedMilliseconds} ms");
        }

        // --------------------------------------------------------------- the ground

        Rect TownRect()
        {
            var city = FindAnyObjectByType<RoadDemoBuilder>();
            if (city == null || city.verticalRoadX == null || city.verticalRoadX.Length == 0 ||
                city.horizontalRoadZ == null || city.horizontalRoadZ.Length == 0)
                return new Rect(-50f, -50f, 100f, 100f);

            float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
            foreach (var x in city.verticalRoadX) { x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x); }
            foreach (var z in city.horizontalRoadZ) { z0 = Mathf.Min(z0, z); z1 = Mathf.Max(z1, z); }

            // the kerbs stand a little outside the outermost road centreline; a margin of
            // twenty metres takes in the pavement and the frontage behind it and stops
            // short of the wild ground, which is the island's business and not the town's
            const float margin = 20f;
            return Rect.MinMaxRect(x0 - margin, z0 - margin, x1 + margin, z1 + margin);
        }

        /// <summary>Cells of the town with no geometry over or under them at all. What a
        /// hole in the floor IS, from above.</summary>
        void Holes(Rect town)
        {
            var cols = Mathf.CeilToInt(town.width / cell);
            var rows = Mathf.CeilToInt(town.height / cell);
            if (cols <= 0 || rows <= 0 || (long)cols * rows > 4_000_000L)
            {
                Debug.LogWarning("[audit] the town rect is not sweepable");
                return;
            }

            var covered = new bool[cols * rows];
            var meshes = 0;

            foreach (var filter in FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
            {
                var mesh = filter.sharedMesh;
                if (mesh == null) continue;
                var tf = filter.transform;
                // The crowd, the traffic and the men are not floor, and a moving thing
                // must never cover a hole it happens to be standing over. They are told
                // apart by the root they hang under, which is what the builder names
                // them by (People, Crews, Cars, Traffic) - a walker is not a Component
                // and cannot be asked for with GetComponentInParent.
                var root = tf.root.name;
                if (root == "People" || root == "Crews" || root == "Cars" ||
                    root == "Traffic" || root == "~PlayHarness") continue;

                var b = mesh.bounds;
                var min = new Vector2(float.MaxValue, float.MaxValue);
                var max = new Vector2(float.MinValue, float.MinValue);
                for (var c = 0; c < 8; c++)
                {
                    var corner = tf.TransformPoint(new Vector3(
                        (c & 1) == 0 ? b.min.x : b.max.x,
                        (c & 2) == 0 ? b.min.y : b.max.y,
                        (c & 4) == 0 ? b.min.z : b.max.z));
                    min = Vector2.Min(min, new Vector2(corner.x, corner.z));
                    max = Vector2.Max(max, new Vector2(corner.x, corner.z));
                }
                meshes++;

                var c0 = Mathf.Clamp(Mathf.FloorToInt((min.x - town.xMin) / cell), 0, cols - 1);
                var c1 = Mathf.Clamp(Mathf.CeilToInt((max.x - town.xMin) / cell), 0, cols - 1);
                var r0 = Mathf.Clamp(Mathf.FloorToInt((min.y - town.yMin) / cell), 0, rows - 1);
                var r1 = Mathf.Clamp(Mathf.CeilToInt((max.y - town.yMin) / cell), 0, rows - 1);
                if (max.x < town.xMin || min.x > town.xMax || max.y < town.yMin || min.y > town.yMax)
                    continue;

                for (var r = r0; r <= r1; r++)
                    for (var c = c0; c <= c1; c++)
                        covered[r * cols + c] = true;
            }

            // the empty cells, gathered into patches so one gap is one line
            var seen = new bool[cols * rows];
            var patches = new List<(int cells, float x, float z)>();
            var queue = new Queue<int>();
            for (var i = 0; i < covered.Length; i++)
            {
                if (covered[i] || seen[i]) continue;
                queue.Clear();
                queue.Enqueue(i);
                seen[i] = true;
                var count = 0;
                double sx = 0, sz = 0;
                while (queue.Count > 0)
                {
                    var at = queue.Dequeue();
                    var c = at % cols;
                    var r = at / cols;
                    count++;
                    sx += town.xMin + (c + 0.5f) * cell;
                    sz += town.yMin + (r + 0.5f) * cell;
                    Step(c - 1, r); Step(c + 1, r); Step(c, r - 1); Step(c, r + 1);

                    void Step(int nc, int nr)
                    {
                        if (nc < 0 || nr < 0 || nc >= cols || nr >= rows) return;
                        var n = nr * cols + nc;
                        if (covered[n] || seen[n]) return;
                        seen[n] = true;
                        queue.Enqueue(n);
                    }
                }
                patches.Add((count, (float)(sx / count), (float)(sz / count)));
            }

            patches.Sort((a, b) => b.cells.CompareTo(a.cells));
            var area = cell * cell;
            Debug.Log($"[audit] ground: {meshes} meshes over {cols}x{rows} cells " +
                      $"({cell:F0} m), {patches.Count} uncovered patches");

            var reported = 0;
            foreach (var patch in patches)
            {
                // a single cell is a seam between two tiles, not a hole a man falls
                // through; four cells is sixty-four square metres of nothing
                if (patch.cells < 4) continue;
                if (++reported > 12) break;
                Fault("hole-in-the-ground",
                      $"({patch.x:F0}, {patch.z:F0})",
                      $"{patch.cells * area:F0} m2 with no geometry at all");
            }
            if (reported == 0)
                Debug.Log("[audit] ground: no patch bigger than four cells is bare");
        }

        // --------------------------------------------------------------- what stands

        /// <summary>Bakes hanging over the ground or sunk into it, and bakes standing
        /// inside one another.
        ///
        /// TOP-LEVEL BAKES ONLY - the block, or the single building where one stands
        /// alone. The first cut of this asked every collider in the city and reported a
        /// thousand buildings "inside another": a terrace's awning, its stair and its
        /// balcony all sit inside their own building's box, and an upper storey really
        /// does stand three metres over the ground. A check that cries wolf a thousand
        /// times is a check nobody reads. Two BLOCKS in one lot, though, is a fault
        /// every time.
        ///
        /// And height is measured against the ISLAND, not against zero: the town stands
        /// on a heightfield, so a bake on the rise is not floating.</summary>
        void Standing()
        {
            if (blocks == null) return;
            var city = FindAnyObjectByType<RoadDemoBuilder>();

            var found = new List<(Transform tf, Bounds b)>();
            foreach (Transform bake in blocks)
            {
                var has = false;
                var box = new Bounds();
                foreach (var filter in bake.GetComponentsInChildren<MeshFilter>())
                {
                    if (filter.sharedMesh == null) continue;
                    var mb = filter.sharedMesh.bounds;
                    for (var c = 0; c < 8; c++)
                    {
                        var corner = filter.transform.TransformPoint(new Vector3(
                            (c & 1) == 0 ? mb.min.x : mb.max.x,
                            (c & 2) == 0 ? mb.min.y : mb.max.y,
                            (c & 4) == 0 ? mb.min.z : mb.max.z));
                        if (!has) { box = new Bounds(corner, Vector3.zero); has = true; }
                        else box.Encapsulate(corner);
                    }
                }
                if (!has || box.size.y < 1.5f) continue;
                found.Add((bake, box));
            }

            var floating = 0;
            var buried = 0;
            foreach (var (tf, box) in found)
            {
                var land = city != null ? city.LandHeight(box.center.x, box.center.z) : 0f;
                if (box.min.y > land + floatSlack)
                {
                    if (floating++ < 6)
                        Fault("bake-in-the-air", tf.name,
                              $"its floor is {box.min.y - land:F1} m over the ground at " +
                              $"({box.center.x:F0}, {box.center.z:F0})");
                }
                else if (box.min.y < land - sinkSlack)
                {
                    if (buried++ < 6)
                        Fault("bake-buried", tf.name,
                              $"its floor is {land - box.min.y:F1} m under the ground at " +
                              $"({box.center.x:F0}, {box.center.z:F0})");
                }
            }

            // two bakes in one lot: footprints of real size that share most of one
            var inside = 0;
            for (var i = 0; i < found.Count; i++)
                for (var j = i + 1; j < found.Count; j++)
                {
                    var a = found[i].b;
                    var b = found[j].b;
                    var areaA = a.size.x * a.size.z;
                    var areaB = b.size.x * b.size.z;
                    if (areaA < 100f || areaB < 100f) continue;

                    var ox = Mathf.Min(a.max.x, b.max.x) - Mathf.Max(a.min.x, b.min.x);
                    var oz = Mathf.Min(a.max.z, b.max.z) - Mathf.Max(a.min.z, b.min.z);
                    if (ox <= 0.5f || oz <= 0.5f) continue;

                    var share = ox * oz / Mathf.Min(areaA, areaB);
                    if (share < 0.35f) continue;
                    if (++inside > 8) continue;
                    Fault("bakes-in-each-other",
                          found[i].tf.name + " / " + found[j].tf.name,
                          $"{share * 100f:F0}% of a footprint shared at " +
                          $"({a.center.x:F0}, {a.center.z:F0})");
                }

            Debug.Log($"[audit] {found.Count} bakes: {floating} in the air, " +
                      $"{buried} buried, {inside} inside another");
        }

        /// <summary>The families' premises: one door each, one building each, and a sign
        /// over it. A front nobody can find is the feature not working.</summary>
        void Fronts()
        {
            var fronts = GangFront.All;
            var buildings = new HashSet<Transform>();
            var gangs = new HashSet<int>();
            foreach (var front in fronts)
            {
                if (front == null) continue;
                if (!buildings.Add(front.transform))
                    Fault("two-fronts-one-building", front.GangName, front.name);
                if (!gangs.Add(front.GangId))
                    Fault("two-fronts-one-family", front.GangName, front.name);
                if (front.Books == null || string.IsNullOrEmpty(front.Books.Sign))
                    Fault("front-without-books", front.GangName, front.name);
                if (front.GetComponentInChildren<Collider>(true) == null)
                    Fault("front-cannot-be-clicked", front.GangName,
                          "no collider on the premises");
            }
            Debug.Log($"[audit] {fronts.Count} fronts standing");
        }

        static void Fault(string kind, string who, string detail)
        {
            Debug.LogWarning($"[audit] {kind}: {who} - {detail}");
            if (DriveTrace.On)
            {
                var sb = DriveTrace.Take();
                DriveTrace.Str(sb, "how", detail);
                DriveTrace.Event("fault", who, kind, sb.ToString());
            }
        }
    }
}
