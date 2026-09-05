using System;
using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>Planted borders follow the finished block's kerbs and courtyard edges.
    /// The same pass serves streamed composition and additions to an authored review block.</summary>
    public static class ResidentialLandscaping
    {
        public const string GroupName = "Residential planting";
        public const string Folder = "Assets/Prefabs/Residential/Landscaping/";
        public static readonly string[] Prefabs = { "StreetBed", "BrickBed", "GardenIsland", "StreetBorder", "CourtBorder" };
        static readonly Vector2[] Sizes = { new Vector2(4.4f, 1.55f), new Vector2(3.1f, 1.45f), new Vector2(5.6f, 3.2f), new Vector2(9f, 1.55f), new Vector2(9f, 2.4f) };

        public static Vector2 Footprint(int variant) => Sizes[variant];

        readonly struct Border
        {
            public readonly Vector2 At;
            public readonly bool AlongX, Courtyard;
            public readonly int Edge;
            public readonly float Run;
            public int Variant => Courtyard ? 4 : 3;
            public Border(Vector2 at, bool alongX, bool courtyard, int edge, float run)
            { At = at; AlongX = alongX; Courtyard = courtyard; Edge = edge; Run = run; }
            public Rect Area => new Rect(At - Size * .5f, Size);
            Vector2 Size => AlongX ? Sizes[Variant] : new Vector2(Sizes[Variant].y, Sizes[Variant].x);
        }

        public static IEnumerable<int> Compose(ResidentialLot.Plan plan, Transform block,
            Func<GameObject, Transform, GameObject> raise)
        {
            if (plan.YardBlock) yield break;
            var obstacles = Obstacles(block);
            var lanes = ResidentialBlocks.BusinessAccessLanes(plan);
            foreach (var front in block.GetComponentsInChildren<Storefront>(true))
                if (front.LeafCount > 0)
                    lanes.Add(ResidentialBlocks.BusinessAccessLane(block.InverseTransformPoint(front.DoorWorld),
                        block.InverseTransformDirection(front.OutwardWorld)));
            var borders = new List<Border>();
            float width = plan.W * 5f, depth = plan.D * 5f;
            // One consistent setback per frontage. The walking line at five metres stays open.
            for (int side = 0; side < 4; side++)
            {
                if (!plan.Street[side]) continue;
                bool alongX = side % 2 == 0;
                float line = side == 0 || side == 3 ? 2.4f : (alongX ? depth : width) - 2.4f;
                AddRuns(plan, borders, obstacles, lanes, alongX, line, 7.5f,
                    (alongX ? width : depth) - 7.5f, false, side);
            }
            // Trace actual courtyard boundaries instead of filling random interior cells.
            // Adjacent cells merge into a single edge; doors and furniture split that edge
            // into usable stretches. Planting is centred on a stretch, never on its doorway.
            int edge = 4;
            for (int direction = 0; direction < 4; direction++)
            {
                bool alongX = direction % 2 == 0;
                int rows = alongX ? plan.D : plan.W, columns = alongX ? plan.W : plan.D;
                int dx = direction == 1 ? 1 : direction == 3 ? -1 : 0;
                int dz = direction == 2 ? 1 : direction == 0 ? -1 : 0;
                for (int row = 0; row < rows; row++)
                {
                    int start = -1;
                    for (int col = 0; col <= columns; col++)
                    {
                        int x = alongX ? col : row, z = alongX ? row : col;
                        bool boundary = col < columns && Court(plan, x, z) && !Court(plan, x + dx, z + dz);
                        if (boundary && start < 0) start = col;
                        if (boundary || start < 0) continue;
                        float line = row * 5f + (direction == 0 || direction == 3 ? 2f : 3f);
                        AddRuns(plan, borders, obstacles, lanes, alongX, line,
                            start * 5f + .6f, col * 5f - .6f, true, edge++);
                        start = -1;
                    }
                }
            }
            // Longest uninterrupted edges establish the layout. Stable ties keep eager and
            // streamed composition identical without random offsets or mixed planter styles.
            borders.Sort((a, b) => { int n = b.Run.CompareTo(a.Run); if (n != 0) return n;
                n = a.Edge.CompareTo(b.Edge); return n != 0 ? n : a.At.x != b.At.x ? a.At.x.CompareTo(b.At.x) : a.At.y.CompareTo(b.At.y); });
            var usedEdges = new HashSet<int>();
            var planted = new List<Rect>();
            int courts = 0;
            Transform garden = null;
            foreach (var border in borders)
            {
                if (usedEdges.Contains(border.Edge) || (border.Courtyard && courts >= 2)) continue;
                if (Intersects(Expand(border.Area, 2f), planted)) continue;
                var prefab = DemoAssetLoad.Load<GameObject>(Folder + Prefabs[border.Variant] + ".prefab");
                if (prefab == null) { Debug.LogWarning("Missing residential planting prefab: " + Prefabs[border.Variant]); continue; }
                if (garden == null) { garden = new GameObject(GroupName).transform; garden.SetParent(block, false); }
                var bed = raise(prefab, garden);
                if (bed == null) continue;
                bed.transform.localPosition = new Vector3(border.At.x, 0f, border.At.y);
                bed.transform.localRotation = Quaternion.Euler(0f, border.AlongX ? 0f : 90f, 0f);
                bed.name = Prefabs[border.Variant];
                usedEdges.Add(border.Edge); planted.Add(border.Area);
                if (border.Courtyard) courts++;
                yield return 1;
            }
        }

        static Rect Expand(Rect area, float margin) =>
            new Rect(area.position - Vector2.one * margin, area.size + Vector2.one * (2f * margin));

        static void AddRuns(ResidentialLot.Plan plan, List<Border> output, List<Rect> obstacles,
            List<Rect> lanes, bool alongX, float line, float start, float end, bool court, int edge)
        {
            const float clearance = .6f;
            float halfDepth = Sizes[court ? 4 : 3].y * .5f + clearance;
            var spans = new List<Vector2> { new Vector2(start, end) };
            Cut(obstacles); Cut(lanes);
            foreach (var span in spans)
            {
                if (span.y - span.x < Sizes[court ? 4 : 3].x + clearance * 2f) continue;
                float middle = (span.x + span.y) * .5f;
                var border = new Border(alongX ? new Vector2(middle, line) : new Vector2(line, middle),
                    alongX, court, edge, span.y - span.x);
                if (Pavement(plan, Expand(border.Area, clearance), court)) output.Add(border);
            }
            void Cut(List<Rect> blockers)
            {
                foreach (var b in blockers)
                {
                    float acrossMin = alongX ? b.yMin : b.xMin, acrossMax = alongX ? b.yMax : b.xMax;
                    if (acrossMin >= line + halfDepth || acrossMax <= line - halfDepth) continue;
                    float lo = (alongX ? b.xMin : b.yMin) - clearance;
                    float hi = (alongX ? b.xMax : b.yMax) + clearance;
                    for (int i = spans.Count - 1; i >= 0; i--)
                    {
                        var span = spans[i]; if (lo >= span.y || hi <= span.x) continue;
                        spans.RemoveAt(i);
                        if (lo > span.x) spans.Add(new Vector2(span.x, lo));
                        if (hi < span.y) spans.Add(new Vector2(hi, span.y));
                    }
                }
            }
        }

        static bool Court(ResidentialLot.Plan plan, int i, int j) =>
            i >= 0 && j >= 0 && i < plan.W && j < plan.D &&
            (plan.Ground[i, j] == ResidentialLot.Use.Court || plan.Ground[i, j] == ResidentialLot.Use.Paved ||
             plan.Ground[i, j] == ResidentialLot.Use.Cafe || plan.Ground[i, j] == ResidentialLot.Use.Yard);

        static bool Pavement(ResidentialLot.Plan plan, Rect area, bool court)
        {
            int x0 = Mathf.FloorToInt(area.xMin / 5f), x1 = Mathf.FloorToInt(area.xMax / 5f);
            int z0 = Mathf.FloorToInt(area.yMin / 5f), z1 = Mathf.FloorToInt(area.yMax / 5f);
            for (int x = x0; x <= x1; x++) for (int z = z0; z <= z1; z++)
            {
                if (x < 0 || z < 0 || x >= plan.W || z >= plan.D) return false;
                if (court ? !Court(plan, x, z) : plan.Ground[x, z] != ResidentialLot.Use.Walkway) return false;
            }
            return true;
        }

        static bool Intersects(Rect area, List<Rect> others)
        { foreach (var other in others) if (area.Overlaps(other)) return true; return false; }

        static List<Rect> Obstacles(Transform block)
        {
            var result = new List<Rect>();
            foreach (var renderer in block.GetComponentsInChildren<Renderer>())
            {
                string name = renderer.name;
                // Kerb meshes include their buried side walls in their bounds.
                if ((name.StartsWith("SM_Env_Sidewalk") && !name.Contains("Construction")) ||
                    name.StartsWith("SM_Env_Road") || name.StartsWith("SM_Prop_Manhole") ||
                    name.StartsWith("SM_Env_Plant_Grate")) continue;
                var box = renderer.bounds;
                // Flat paving, paint and overhead signs/canopies leave the walking surface free.
                if (box.size.y < .13f || box.min.y > block.position.y + 1.8f) continue;
                if (renderer.name.Contains("Tree_Palm"))
                {
                    var at = block.InverseTransformPoint(renderer.transform.position);
                    result.Add(new Rect(at.x - 1.2f, at.z - 1.2f, 2.4f, 2.4f));
                    continue;
                }
                Vector3 lo = Vector3.positiveInfinity, hi = Vector3.negativeInfinity;
                for (int n = 0; n < 8; n++)
                {
                    var world = box.center + Vector3.Scale(box.extents,
                        new Vector3((n & 1) == 0 ? -1 : 1, (n & 2) == 0 ? -1 : 1, (n & 4) == 0 ? -1 : 1));
                    var p = block.InverseTransformPoint(world); lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p);
                }
                result.Add(Rect.MinMaxRect(lo.x, lo.z, hi.x, hi.z));
            }
            return result;
        }

    }
}
