using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RoadDemo
{
    /// <summary>
    /// Geometry shared by the standalone parking demo and a parking block embedded in a
    /// district. The plan is authored with its entrance on local south; ParkingBlockSite
    /// turns that local frame toward whichever street serves the real block.
    /// </summary>
    public sealed class ParkingBlockPlan
    {
        public const float StallWidth = 3.2f;
        public const float StallDepth = 5.6f;
        public const float AisleWidth = 6f;
        public const float GateWidth = 7f;
        public const float EdgeMargin = 2f;

        public readonly struct Stall
        {
            public readonly Vector3 Stand;
            public readonly Vector3 Forward;
            public readonly Vector3 Mouth;
            public readonly Vector3 Junction;

            public Stall(Vector3 stand, Vector3 forward, Vector3 mouth, Vector3 junction)
            {
                Stand = stand;
                Forward = forward;
                Mouth = mouth;
                Junction = junction;
            }
        }

        public readonly struct Stripe
        {
            public readonly Vector2 A, B;
            public Stripe(Vector2 a, Vector2 b) { A = a; B = b; }
        }

        public float Width { get; private set; }
        public float Depth { get; private set; }
        public Vector3 Gate => new Vector3(Width * 0.5f, 0f, 0f);
        public Vector3 GateInside => new Vector3(Width * 0.5f, 0f, AisleWidth * 0.5f);
        public readonly List<Stall> Stalls = new List<Stall>();
        public readonly List<Stripe> Markings = new List<Stripe>();
        /// <summary>Grid-aligned building/yard footprints cut out of this parking surface.</summary>
        public readonly List<Rect> Exclusions = new List<Rect>();

        public static ParkingBlockPlan Generate(
            float width, float depth, IEnumerable<Rect> exclusions = null)
        {
            var plan = new ParkingBlockPlan
            {
                Width = Mathf.Max(0f, width),
                Depth = Mathf.Max(0f, depth),
            };
            if (exclusions != null)
                foreach (var exclusion in exclusions)
                {
                    var clipped = Rect.MinMaxRect(
                        Mathf.Clamp(exclusion.xMin, 0f, plan.Width),
                        Mathf.Clamp(exclusion.yMin, 0f, plan.Depth),
                        Mathf.Clamp(exclusion.xMax, 0f, plan.Width),
                        Mathf.Clamp(exclusion.yMax, 0f, plan.Depth));
                    if (clipped.width > 0.01f && clipped.height > 0.01f)
                        plan.Exclusions.Add(clipped);
                }

            float run = plan.Width - 2f * EdgeMargin;
            int across = Mathf.FloorToInt(run / StallWidth);
            if (across < 2 || plan.Depth < AisleWidth + StallDepth)
                return plan;

            float start = EdgeMargin + (run - across * StallWidth) * 0.5f;
            float cursor = 0f;
            while (cursor + AisleWidth + StallDepth <= plan.Depth - EdgeMargin)
            {
                float nearAisle = cursor + AisleWidth * 0.5f;
                float nearRow = cursor + AisleWidth + StallDepth * 0.5f;
                EmitRow(plan, start, across, nearRow, Vector3.back, nearAisle);

                float farAisle = cursor + AisleWidth + 2f * StallDepth + AisleWidth * 0.5f;
                if (farAisle + AisleWidth * 0.5f > plan.Depth - EdgeMargin)
                    break;

                float farRow = cursor + AisleWidth + StallDepth * 1.5f;
                EmitRow(plan, start, across, farRow, Vector3.forward, farAisle);
                cursor += AisleWidth + 2f * StallDepth;
            }

            return plan;
        }

        static void EmitRow(
            ParkingBlockPlan plan, float start, int across, float z, Vector3 forward, float aisleZ)
        {
            float driveHalf = GateWidth * 0.5f + 0.5f;
            for (int i = 0; i < across; i++)
            {
                float x = start + StallWidth * (i + 0.5f);
                if (Mathf.Abs(x - plan.Width * 0.5f) < driveHalf + StallWidth * 0.5f)
                    continue;

                var stand = new Vector3(x, 0f, z);
                var mouth = new Vector3(x, 0f, aisleZ);
                var junction = new Vector3(plan.Width * 0.5f, 0f, aisleZ);
                var bay = Rect.MinMaxRect(x - StallWidth * 0.5f, z - StallDepth * 0.5f,
                                          x + StallWidth * 0.5f, z + StallDepth * 0.5f);
                var crossAisle = Rect.MinMaxRect(
                    Mathf.Min(x, junction.x) - 1.25f, aisleZ - 1.25f,
                    Mathf.Max(x, junction.x) + 1.25f, aisleZ + 1.25f);
                var centreDrive = Rect.MinMaxRect(
                    plan.Width * 0.5f - GateWidth * 0.5f, 0f,
                    plan.Width * 0.5f + GateWidth * 0.5f, aisleZ + 1.25f);
                if (!plan.Clear(bay) || !plan.Clear(crossAisle) || !plan.Clear(centreDrive))
                    continue;
                plan.Stalls.Add(new Stall(stand, forward, mouth, junction));

                float open = z + (forward.z > 0f ? StallDepth * 0.5f : -StallDepth * 0.5f);
                float closed = z - (forward.z > 0f ? StallDepth * 0.5f : -StallDepth * 0.5f);
                float left = x - StallWidth * 0.5f;
                float right = x + StallWidth * 0.5f;
                plan.Markings.Add(new Stripe(new Vector2(left, open), new Vector2(left, closed)));
                plan.Markings.Add(new Stripe(new Vector2(right, open), new Vector2(right, closed)));
                plan.Markings.Add(new Stripe(new Vector2(left, closed), new Vector2(right, closed)));
            }
        }

        public bool ContainsSurface(Vector2 point)
        {
            if (point.x < 0f || point.y < 0f || point.x > Width || point.y > Depth)
                return false;
            for (int i = 0; i < Exclusions.Count; i++)
                if (Exclusions[i].Contains(point)) return false;
            return true;
        }

        bool Clear(Rect area)
        {
            for (int i = 0; i < Exclusions.Count; i++)
                if (Exclusions[i].Overlaps(area)) return false;
            return true;
        }
    }

    public enum ParkingEntrySide { South, East, North, West }

    /// <summary>A generated block and the transform from its entrance-south plan to the district.</summary>
    public sealed class ParkingBlockSite
    {
        public readonly ParkingBlockPlan Plan;
        public readonly Transform Root;
        public readonly Rect Box;
        public readonly ParkingEntrySide Entry;

        ParkingBlockSite(ParkingBlockPlan plan, Transform root, Rect box, ParkingEntrySide entry)
        {
            Plan = plan;
            Root = root;
            Box = box;
            Entry = entry;
        }

        public static ParkingBlockSite Build(
            Rect box, ParkingEntrySide entry, Transform parent,
            System.Func<GameObject, Transform, GameObject> stand,
            IEnumerable<Rect> exclusions = null)
        {
            bool verticalSide = entry == ParkingEntrySide.East || entry == ParkingEntrySide.West;
            float width = verticalSide ? box.height : box.width;
            float depth = verticalSide ? box.width : box.height;
            var plan = ParkingBlockPlan.Generate(width, depth, exclusions);

            var root = new GameObject("Functional Parking Block").transform;
            root.SetParent(parent, false);
            switch (entry)
            {
                case ParkingEntrySide.North:
                    root.localPosition = new Vector3(box.xMax, 0f, box.yMax);
                    root.localRotation = Quaternion.Euler(0f, 180f, 0f);
                    break;
                case ParkingEntrySide.West:
                    root.localPosition = new Vector3(box.xMin, 0f, box.yMax);
                    root.localRotation = Quaternion.Euler(0f, 90f, 0f);
                    break;
                case ParkingEntrySide.East:
                    root.localPosition = new Vector3(box.xMax, 0f, box.yMin);
                    root.localRotation = Quaternion.Euler(0f, 270f, 0f);
                    break;
                default:
                    root.localPosition = new Vector3(box.xMin, 0f, box.yMin);
                    root.localRotation = Quaternion.identity;
                    break;
            }

            ParkingBlockView.Compose(plan, root, stand);
            return new ParkingBlockSite(plan, root, box, entry);
        }

        /// <summary>Chooses the side whose midpoint lies nearest a usable lane.</summary>
        public static ParkingEntrySide NearestEntry(Rect localBox, DistrictFrame frame, LaneNet net)
        {
            var sides = new[]
            {
                (ParkingEntrySide.South, new Vector3(localBox.center.x, 0f, localBox.yMin)),
                (ParkingEntrySide.East,  new Vector3(localBox.xMax, 0f, localBox.center.y)),
                (ParkingEntrySide.North, new Vector3(localBox.center.x, 0f, localBox.yMax)),
                (ParkingEntrySide.West,  new Vector3(localBox.xMin, 0f, localBox.center.y)),
            };

            var best = ParkingEntrySide.South;
            float bestDistance = float.MaxValue;
            foreach (var side in sides)
            {
                var world = frame.ToWorld(side.Item2);
                if (net == null) continue;
                var lane = net.NearestLane(world, out float progress, 20f);
                if (lane == null) continue;
                var point = lane.Start + lane.Dir * progress;
                float distance = (point - world).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = side.Item1;
                }
            }
            return best;
        }
    }

    /// <summary>Visual half of the generator. It has no traffic state and can be used as scenery.</summary>
    static class ParkingBlockView
    {
        const float Cell = 5f;
        const float KerbDepth = 1f;
        const string CityEnvironment = "Assets/Synty/PolygonCity/Prefabs/Environments/";
        const string CityProps = "Assets/Synty/PolygonCity/Prefabs/Props/";
        const string Asphalt = "SM_Env_Road_Bare_01";
        const string ParkingLines = "SM_Env_Road_ParkingLines_01";
        const string Kerb = "SM_Env_Sidewalk_Straight_01";
        const string Arrow = "SM_Env_Road_Arrow_01";
        const string Sign = "SM_Prop_Sign_Parking_01";

        public static void Compose(
            ParkingBlockPlan plan, Transform parent,
            System.Func<GameObject, Transform, GameObject> stand)
        {
            if (plan == null || parent == null || stand == null) return;
            var cache = new Dictionary<string, GameObject>();

            GameObject Load(string name, bool prop = false)
            {
                string key = (prop ? CityProps : CityEnvironment) + name + ".prefab";
                if (!cache.TryGetValue(key, out var prefab))
                {
                    prefab = DemoAssetLoad.Load<GameObject>(key);
                    cache[key] = prefab;
                }
                return prefab;
            }

            GameObject Piece(string name, Vector3 at, int yaw, bool prop = false)
            {
                var prefab = Load(name, prop);
                if (prefab == null) return null;
                var go = stand(prefab, parent);
                if (go != null)
                {
                    go.transform.localPosition = at;
                    go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
                }
                return go;
            }

            void Tile(string name, float x, float z, int yaw, float sx, float sz, float y = 0f)
            {
                Vector3 pivot, scale;
                switch (((yaw % 360) + 360) % 360)
                {
                    case 90:
                        pivot = new Vector3(x + sx, y, z);
                        scale = new Vector3(sz / Cell, 1f, sx / Cell);
                        break;
                    case 180:
                        pivot = new Vector3(x, y, z);
                        scale = new Vector3(sx / Cell, 1f, sz / Cell);
                        break;
                    case 270:
                        pivot = new Vector3(x, y, z + sz);
                        scale = new Vector3(sz / Cell, 1f, sx / Cell);
                        break;
                    default:
                        pivot = new Vector3(x + sx, y, z + sz);
                        scale = new Vector3(sx / Cell, 1f, sz / Cell);
                        break;
                }
                var go = Piece(name, pivot, yaw);
                if (go != null) go.transform.localScale = scale;
            }

            for (float x = 0f; x < plan.Width - 0.01f; x += Cell)
                for (float z = 0f; z < plan.Depth - 0.01f; z += Cell)
                {
                    float sx = Mathf.Min(Cell, plan.Width - x);
                    float sz = Mathf.Min(Cell, plan.Depth - z);
                    if (!plan.ContainsSurface(new Vector2(x + sx * 0.5f, z + sz * 0.5f)))
                        continue;
                    Tile(Asphalt, x, z, 0,
                         sx, sz);
                }

            var linePrefab = Load(ParkingLines);
            var lineRenderer = linePrefab != null ? linePrefab.GetComponentInChildren<Renderer>(true) : null;
            EmitLines(plan, lineRenderer != null ? lineRenderer.sharedMaterial : null, parent);

            float gateLeft = plan.Width * 0.5f - ParkingBlockPlan.GateWidth * 0.5f;
            float gateRight = plan.Width * 0.5f + ParkingBlockPlan.GateWidth * 0.5f;
            if (gateLeft > 0.1f) Tile(Kerb, 0f, 0f, 180, gateLeft, KerbDepth, 0.02f);
            if (plan.Width - gateRight > 0.1f)
                Tile(Kerb, gateRight, 0f, 180, plan.Width - gateRight, KerbDepth, 0.02f);
            Tile(Kerb, 0f, plan.Depth - KerbDepth, 0, plan.Width, KerbDepth, 0.02f);
            Tile(Kerb, 0f, 0f, 270, KerbDepth, plan.Depth, 0.02f);
            Tile(Kerb, plan.Width - KerbDepth, 0f, 90, KerbDepth, plan.Depth, 0.02f);

            var arrow = Piece(Arrow, new Vector3(plan.Width * 0.5f + 2.5f, 0.08f, 5f), 0);
            if (arrow != null) arrow.name = "entry arrow";
            var sign = Piece(Sign,
                new Vector3(gateRight + 1.2f, 0f, 1.2f), 180, prop: true);
            if (sign != null) sign.name = "parking sign";
        }

        static void EmitLines(ParkingBlockPlan plan, Material material, Transform parent)
        {
            if (material == null || plan.Markings.Count == 0) return;
            const float width = 0.14f;
            var vertices = new List<Vector3>(plan.Markings.Count * 4);
            var normals = new List<Vector3>(plan.Markings.Count * 4);
            var uv = new List<Vector2>(plan.Markings.Count * 4);
            var triangles = new List<int>(plan.Markings.Count * 6);
            foreach (var stripe in plan.Markings)
            {
                var direction = stripe.B - stripe.A;
                float length = direction.magnitude;
                if (length < 0.01f) continue;
                direction /= length;
                var side = new Vector2(-direction.y, direction.x) * (width * 0.5f);
                int v = vertices.Count;
                vertices.Add(new Vector3(stripe.A.x - side.x, 0.12f, stripe.A.y - side.y));
                vertices.Add(new Vector3(stripe.A.x + side.x, 0.12f, stripe.A.y + side.y));
                vertices.Add(new Vector3(stripe.B.x + side.x, 0.12f, stripe.B.y + side.y));
                vertices.Add(new Vector3(stripe.B.x - side.x, 0.12f, stripe.B.y - side.y));
                for (int i = 0; i < 4; i++) normals.Add(Vector3.up);
                uv.Add(Vector2.zero); uv.Add(Vector2.right); uv.Add(Vector2.one); uv.Add(Vector2.up);
                triangles.Add(v); triangles.Add(v + 1); triangles.Add(v + 2);
                triangles.Add(v); triangles.Add(v + 2); triangles.Add(v + 3);
            }

            if (vertices.Count == 0) return;
            var mesh = new Mesh { name = "parking markings" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();

            var go = new GameObject("parking markings");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }
    }
}
