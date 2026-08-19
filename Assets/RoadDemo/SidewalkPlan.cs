using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>The ground a prop stands on, measured off the prefab's own meshes
    /// once and kept. What blocks a walker is what a prop occupies at HIS height,
    /// so the footprint is taken from the slice between the paving and shoulder
    /// height: a palm is its trunk and not its canopy, a street lamp is its post
    /// and not the arm hung out over the road, a grate is nothing at all.</summary>
    public static class PropFootprint
    {
        const float SliceLow = 0.06f;
        const float SliceHigh = 1.9f;
        const float FlatTop = 0.22f;   // below this a prop is paving, not an obstacle
        const float TallFrom = 2.6f;   // above this the shape must be sliced, not boxed
        const float PostHalf = 0.5f;   // an unsliceable tall thing is taken for a post

        public struct Foot
        {
            public bool Known;
            public Vector2 Centre, Half;  // prefab-local XZ
            public bool Solid;            // stands high enough to stop a walker
        }

        static readonly Dictionary<GameObject, Foot> Cache = new Dictionary<GameObject, Foot>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Forget() => Cache.Clear();

        public static Foot Of(GameObject prefab)
        {
            if (prefab == null) return default;
            if (Cache.TryGetValue(prefab, out var foot)) return foot;
            foot = Measure(prefab);
            Cache[prefab] = foot;
            return foot;
        }

        static Foot Measure(GameObject prefab)
        {
            var toRoot = prefab.transform.worldToLocalMatrix;
            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            bool any = false;

            var meshes = new List<(Mesh mesh, Matrix4x4 m)>();
            foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null)
                    meshes.Add((mf.sharedMesh, toRoot * mf.transform.localToWorldMatrix));
            foreach (var sk in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (sk.sharedMesh != null)
                    meshes.Add((sk.sharedMesh, toRoot * sk.transform.localToWorldMatrix));

            foreach (var (mesh, m) in meshes)
            {
                var b = mesh.bounds;
                for (int c = 0; c < 8; c++)
                {
                    var corner = new Vector3(
                        (c & 1) == 0 ? b.min.x : b.max.x,
                        (c & 2) == 0 ? b.min.y : b.max.y,
                        (c & 4) == 0 ? b.min.z : b.max.z);
                    var p = m.MultiplyPoint3x4(corner);
                    min = Vector3.Min(min, p);
                    max = Vector3.Max(max, p);
                    any = true;
                }
            }
            if (!any) return default;

            var scale = prefab.transform.localScale;
            float sx = Mathf.Abs(scale.x), sz = Mathf.Abs(scale.z);
            var lo = new Vector2(min.x, min.z);
            var hi = new Vector2(max.x, max.z);

            // a tall prop is only its trunk down where the people walk: slice it if
            // the meshes will give up their vertices, else take it for a post. The
            // post stands at the PIVOT, not at the middle of the bounds: a palm leans,
            // a lamp hangs its head a metre and a half out over the road, and the
            // bounds' centre is out under the canopy, where nobody has to step round
            // anything - while the trunk itself, on the origin, would be walked through.
            if (max.y > TallFrom)
            {
                if (Slice(meshes, out var slo, out var shi)) { lo = slo; hi = shi; }
                else
                {
                    lo = new Vector2(Mathf.Max(lo.x, -PostHalf), Mathf.Max(lo.y, -PostHalf));
                    hi = new Vector2(Mathf.Min(hi.x, PostHalf), Mathf.Min(hi.y, PostHalf));
                    if (hi.x <= lo.x || hi.y <= lo.y)
                    {
                        lo = new Vector2(-PostHalf, -PostHalf);
                        hi = new Vector2(PostHalf, PostHalf);
                    }
                }
            }

            return new Foot
            {
                Known = true,
                Centre = new Vector2((lo.x + hi.x) * 0.5f * scale.x, (lo.y + hi.y) * 0.5f * scale.z),
                Half = new Vector2((hi.x - lo.x) * 0.5f * sx, (hi.y - lo.y) * 0.5f * sz),
                Solid = max.y * Mathf.Abs(scale.y) > FlatTop,
            };
        }

        /// The XZ span of whatever sits between ankle and shoulder height. False
        /// when no mesh will hand over its vertices (Read/Write off) or nothing of
        /// the prop is in that band at all.
        static bool Slice(List<(Mesh mesh, Matrix4x4 m)> meshes, out Vector2 lo, out Vector2 hi)
        {
            lo = new Vector2(float.MaxValue, float.MaxValue);
            hi = new Vector2(float.MinValue, float.MinValue);
            bool readAny = false, hit = false;
            foreach (var (mesh, m) in meshes)
            {
                if (!mesh.isReadable) continue;
                readAny = true;
                var verts = mesh.vertices;
                for (int v = 0; v < verts.Length; v++)
                {
                    var p = m.MultiplyPoint3x4(verts[v]);
                    if (p.y < SliceLow || p.y > SliceHigh) continue;
                    lo = Vector2.Min(lo, new Vector2(p.x, p.z));
                    hi = Vector2.Max(hi, new Vector2(p.x, p.z));
                    hit = true;
                }
            }
            return readAny && hit;
        }
    }

    /// <summary>What the pavement is already spoken for. Every prop the city lays
    /// down claims the box it stands in; the dressing asks before it stands the
    /// next one, and the walkers ask before they choose the line they walk. Boxes
    /// are oriented (a hedge along the road is not a square), tested by separating
    /// axis, and bucketed on a coarse grid so a city's worth of props costs
    /// nothing to query.</summary>
    public sealed class SidewalkPlan
    {
        public struct Box
        {
            public Vector2 C;        // centre, world XZ
            public Vector2 H;        // half extents in the box's own frame
            public Vector2 Ax, Az;   // the box's own axes, world XZ
            public bool Solid;       // stops a walker
            public bool KeepClear;   // a reservation, not a prop: refuses props, blocks nobody
        }

        const float Cell = 4f;

        readonly List<Box> _boxes = new List<Box>();
        readonly Dictionary<long, List<int>> _grid = new Dictionary<long, List<int>>();

        // ------------------------------------------------------------- making boxes

        public static Box Make(Vector2 centre, float yaw, Vector2 half, bool solid)
        {
            float r = yaw * Mathf.Deg2Rad;
            float cos = Mathf.Cos(r), sin = Mathf.Sin(r);
            return new Box
            {
                C = centre, H = half,
                Ax = new Vector2(cos, -sin),   // the prop's local +X, in world XZ
                Az = new Vector2(sin, cos),    // its local +Z
                Solid = solid,
            };
        }

        /// <summary>The box a prefab would stand in at this spot. False when the
        /// prefab carries no mesh to measure.</summary>
        public static bool Footprint(GameObject prefab, Vector3 pos, float yaw, out Box box)
        {
            box = default;
            var foot = PropFootprint.Of(prefab);
            if (!foot.Known) return false;
            float r = yaw * Mathf.Deg2Rad;
            float cos = Mathf.Cos(r), sin = Mathf.Sin(r);
            var off = new Vector2(
                foot.Centre.x * cos + foot.Centre.y * sin,
                -foot.Centre.x * sin + foot.Centre.y * cos);
            box = Make(new Vector2(pos.x, pos.z) + off, yaw, foot.Half, foot.Solid);
            return true;
        }

        /// <summary>How far a prefab's footprint reaches along a world direction -
        /// what the dressing needs to seat a prop against the kerb or the wall.</summary>
        public static float Reach(GameObject prefab, float yaw, Vector3 axis, out float centreOffset)
        {
            centreOffset = 0f;
            var foot = PropFootprint.Of(prefab);
            if (!foot.Known) return 0f;
            float r = yaw * Mathf.Deg2Rad;
            float cos = Mathf.Cos(r), sin = Mathf.Sin(r);
            var ax = new Vector2(cos, -sin);
            var az = new Vector2(sin, cos);
            var u = new Vector2(axis.x, axis.z).normalized;
            centreOffset = Vector2.Dot(new Vector2(
                foot.Centre.x * cos + foot.Centre.y * sin,
                -foot.Centre.x * sin + foot.Centre.y * cos), u);
            return Mathf.Abs(Vector2.Dot(ax, u)) * foot.Half.x +
                   Mathf.Abs(Vector2.Dot(az, u)) * foot.Half.y;
        }

        // ------------------------------------------------------------- the register

        public int Count => _boxes.Count;

        public void Take(in Box box)
        {
            int id = _boxes.Count;
            _boxes.Add(box);
            Index(box, id, true);
        }

        /// <summary>Ground props may not stand on at all - a crossing's mouth, the
        /// turning room on a corner. Claims nothing from the walkers.</summary>
        public void Reserve(Vector3 centre, float yaw, Vector2 half)
        {
            var box = Make(new Vector2(centre.x, centre.z), yaw, half, false);
            box.KeepClear = true;
            Take(box);
        }

        /// <summary>Drop the last box taken - the dressing standing a prop, finding
        /// it leaves no pavement to walk on, and putting it back.</summary>
        public void Pop()
        {
            int id = _boxes.Count - 1;
            if (id < 0) return;
            Index(_boxes[id], id, false);
            _boxes.RemoveAt(id);
        }

        /// <summary>Is this box's ground still free (props and reservations alike)?</summary>
        public bool Free(in Box box, float pad)
        {
            foreach (int id in Near(box))
                if (Overlap(box, _boxes[id], pad)) return false;
            return true;
        }

        /// <summary>Would a walker of this radius foul something standing here?</summary>
        public bool Occupied(Vector2 p, float radius)
        {
            int cx = Mathf.FloorToInt(p.x / Cell), cz = Mathf.FloorToInt(p.y / Cell);
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (!_grid.TryGetValue(Key(cx + dx, cz + dz), out var bucket)) continue;
                    for (int k = 0; k < bucket.Count; k++)
                    {
                        var b = _boxes[bucket[k]];
                        if (!b.Solid || b.KeepClear) continue;
                        var d = p - b.C;
                        float ox = Mathf.Max(0f, Mathf.Abs(Vector2.Dot(d, b.Ax)) - b.H.x);
                        float oz = Mathf.Max(0f, Mathf.Abs(Vector2.Dot(d, b.Az)) - b.H.y);
                        if (ox * ox + oz * oz <= radius * radius) return true;
                    }
                }
            return false;
        }

        // ------------------------------------------------------------- the grid

        static long Key(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;

        void Bounds(in Box b, out int x0, out int z0, out int x1, out int z1)
        {
            float hx = Mathf.Abs(b.Ax.x) * b.H.x + Mathf.Abs(b.Az.x) * b.H.y;
            float hz = Mathf.Abs(b.Ax.y) * b.H.x + Mathf.Abs(b.Az.y) * b.H.y;
            x0 = Mathf.FloorToInt((b.C.x - hx) / Cell);
            x1 = Mathf.FloorToInt((b.C.x + hx) / Cell);
            z0 = Mathf.FloorToInt((b.C.y - hz) / Cell);
            z1 = Mathf.FloorToInt((b.C.y + hz) / Cell);
        }

        void Index(in Box b, int id, bool add)
        {
            Bounds(b, out int x0, out int z0, out int x1, out int z1);
            for (int x = x0; x <= x1; x++)
                for (int z = z0; z <= z1; z++)
                {
                    long key = Key(x, z);
                    if (add)
                    {
                        if (!_grid.TryGetValue(key, out var bucket))
                            _grid[key] = bucket = new List<int>();
                        bucket.Add(id);
                    }
                    else if (_grid.TryGetValue(key, out var bucket))
                        bucket.Remove(id);
                }
        }

        static readonly List<int> Scratch = new List<int>();

        List<int> Near(in Box b)
        {
            Scratch.Clear();
            Bounds(b, out int x0, out int z0, out int x1, out int z1);
            for (int x = x0 - 1; x <= x1 + 1; x++)
                for (int z = z0 - 1; z <= z1 + 1; z++)
                {
                    if (!_grid.TryGetValue(Key(x, z), out var bucket)) continue;
                    for (int k = 0; k < bucket.Count; k++)
                        if (!Scratch.Contains(bucket[k])) Scratch.Add(bucket[k]);
                }
            return Scratch;
        }

        // ------------------------------------------------------------- separating axis

        static bool Overlap(in Box a, in Box b, float pad)
        {
            var d = b.C - a.C;
            return Hits(d, a.Ax, a, b, pad) && Hits(d, a.Az, a, b, pad) &&
                   Hits(d, b.Ax, a, b, pad) && Hits(d, b.Az, a, b, pad);
        }

        static bool Hits(Vector2 d, Vector2 axis, in Box a, in Box b, float pad)
        {
            float ra = Mathf.Abs(Vector2.Dot(a.Ax, axis)) * a.H.x + Mathf.Abs(Vector2.Dot(a.Az, axis)) * a.H.y;
            float rb = Mathf.Abs(Vector2.Dot(b.Ax, axis)) * b.H.x + Mathf.Abs(Vector2.Dot(b.Az, axis)) * b.H.y;
            return Mathf.Abs(Vector2.Dot(d, axis)) <= ra + rb + pad;
        }
    }
}
