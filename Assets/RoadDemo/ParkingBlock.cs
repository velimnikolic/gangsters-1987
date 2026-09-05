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
        // Measured against the city's parked-car catalogue: the widest ordinary car is
        // 2.26 m. A 2.7 m bay leaves useful door clearance without turning the lot into the
        // sparse 3.2 m grid the first review scene used.
        public const float StallWidth = 2.7f;
        public const float StallDepth = 5.6f;
        public const float AisleWidth = 8f;
        public const float GateWidth = 7f;
        public const float EdgeMargin = 2.5f;
        const float RearMargin = 1f;

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
        // The street turn is completed outside the equipment. From here to GateInside the
        // vehicle travels straight, centred between the boom pedestal and entrance furniture.
        public Vector3 GateOutside => new Vector3(Width * 0.5f, 0f, -2f);
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
            // Keep a manoeuvring aisle wide enough for the admitted long sedans,
            // including their traffic clearance, even between two occupied bays.
            while (cursor + AisleWidth + StallDepth <= plan.Depth - RearMargin)
            {
                float nearAisle = cursor + AisleWidth * 0.5f;
                float nearRow = cursor + AisleWidth + StallDepth * 0.5f;
                EmitRow(plan, start, across, nearRow, Vector3.back, nearAisle);

                float farAisle = cursor + AisleWidth + 2f * StallDepth + AisleWidth * 0.5f;
                if (farAisle + AisleWidth * 0.5f > plan.Depth - RearMargin)
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

    /// <summary>
    /// Real parking types, not building types. The surface plan and the traffic stay identical;
    /// only the perimeter and entrance equipment change.
    /// </summary>
    public enum ParkingBlockStyle
    {
        /// <summary>Fenced public lot with an attendant, pay equipment and a working boom.</summary>
        Attended,
        /// <summary>A whole city block with CoreDemo's standard pavement around the surface.</summary>
        UrbanBlock,
        /// <summary>Fenced long-stay/employee lot with an automated barrier and pay station.</summary>
        LongStay,
    }

    /// <summary>A generated block and the transform from its entrance-south plan to the district.</summary>
    public sealed class ParkingBlockSite
    {
        public readonly ParkingBlockPlan Plan;
        public readonly Transform Root;
        public readonly Rect Box;
        public readonly ParkingEntrySide Entry;
        public readonly ParkingBlockStyle Style;
        public readonly TollArm GateArm;
        public readonly Transform GateRoot;

        ParkingBlockSite(
            ParkingBlockPlan plan, Transform root, Rect box, ParkingEntrySide entry,
            ParkingBlockStyle style, TollArm gateArm, Transform gateRoot)
        {
            Plan = plan;
            Root = root;
            Box = box;
            Entry = entry;
            Style = style;
            GateArm = gateArm;
            GateRoot = gateRoot;
        }

        public static ParkingBlockSite Build(
            Rect box, ParkingEntrySide entry, Transform parent,
            System.Func<GameObject, Transform, GameObject> stand,
            IEnumerable<Rect> exclusions = null,
            ParkingBlockStyle style = ParkingBlockStyle.Attended)
        {
            // The box of an urban lot is the BLOCK. Its parking surface starts the shared
            // CoreDemo pavement width in from every edge; the south band is cut by the drive.
            var surface = Surface(box, style);

            bool verticalSide = entry == ParkingEntrySide.East || entry == ParkingEntrySide.West;
            float width = verticalSide ? surface.height : surface.width;
            float depth = verticalSide ? surface.width : surface.height;
            var plan = ParkingBlockPlan.Generate(width, depth, exclusions);

            var root = new GameObject("Functional Parking Block").transform;
            root.SetParent(parent, false);
            switch (entry)
            {
                case ParkingEntrySide.North:
                    root.localPosition = new Vector3(surface.xMax, 0f, surface.yMax);
                    root.localRotation = Quaternion.Euler(0f, 180f, 0f);
                    break;
                case ParkingEntrySide.West:
                    root.localPosition = new Vector3(surface.xMin, 0f, surface.yMax);
                    root.localRotation = Quaternion.Euler(0f, 90f, 0f);
                    break;
                case ParkingEntrySide.East:
                    root.localPosition = new Vector3(surface.xMax, 0f, surface.yMin);
                    root.localRotation = Quaternion.Euler(0f, 270f, 0f);
                    break;
                default:
                    root.localPosition = new Vector3(surface.xMin, 0f, surface.yMin);
                    root.localRotation = Quaternion.identity;
                    break;
            }

            var gateArm = ParkingBlockView.Compose(plan, root, stand, style);
            var gateRoot = root.Find("parking payment barrier");
            return new ParkingBlockSite(plan, root, box, entry, style, gateArm, gateRoot);
        }

        /// <summary>The vehicle surface inside a block. Public so the paper-side tests and
        /// other district composers use the same inset as the visual composer.</summary>
        public static Rect Surface(Rect box, ParkingBlockStyle style)
        {
            if (style != ParkingBlockStyle.UrbanBlock) return box;
            float pavement = CoreBlockMetrics.PavementWidth;
            return new Rect(box.xMin + pavement, box.yMin + pavement,
                            Mathf.Max(0f, box.width - 2f * pavement),
                            Mathf.Max(0f, box.height - 2f * pavement));
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
        const float Cell = CoreBlockMetrics.Cell;
        const float KerbDepth = 1f;
        const string CityEnvironment = "Assets/Synty/PolygonCity/Prefabs/Environments/";
        const string CityProps = "Assets/Synty/PolygonCity/Prefabs/Props/";
        const string PalmProps = "Assets/Synty/PolygonPalmCity/Prefabs/Props/";
        const string BoothPath = "Assets/CityKit/Airport/airport-guard-booth.prefab";
        const string Asphalt = "SM_Env_Road_Bare_01";
        const string ParkingLines = "SM_Env_Road_ParkingLines_01";
        const string Paving = "SM_Env_Sidewalk_01";
        const string Kerb = "SM_Env_Sidewalk_Straight_01";
        const string KerbCorner = "SM_Env_Sidewalk_Corner_01";
        const string Arrow = "SM_Env_Road_Arrow_01";
        const string Sign = "SM_Prop_Sign_Parking_01";
        const string Fence = "SM_Env_Fence_01";
        const string LampPath = PalmProps + "SM_Prop_Street_Lamp_03.prefab";
        const string BoomPath = PalmProps + "SM_Prop_Barrier_Gate_01.prefab";
        const string PayPath = PalmProps + "SM_Prop_Parking_Stand_01.prefab";
        const string ConsolePath = PalmProps + "SM_Prop_Parking_Console_01.prefab";
        const string BollardPath = PalmProps + "SM_Prop_Bollard_01.prefab";
        static Material _whitePaint;

        static Material WhitePaint
            => _whitePaint != null
                ? _whitePaint
                : _whitePaint = ForecourtSet.WhitePaint();

        public static TollArm Compose(
            ParkingBlockPlan plan, Transform parent,
            System.Func<GameObject, Transform, GameObject> stand,
            ParkingBlockStyle style)
        {
            if (plan == null || parent == null || stand == null) return null;
            var cache = new Dictionary<string, GameObject>();

            GameObject LoadPath(string path)
            {
                if (!cache.TryGetValue(path, out var prefab))
                {
                    prefab = DemoAssetLoad.Load<GameObject>(path);
                    cache[path] = prefab;
                }
                return prefab;
            }

            GameObject Load(string name, bool prop = false)
                => LoadPath((prop ? CityProps : CityEnvironment) + name + ".prefab");

            GameObject PiecePath(string path, Vector3 at, int yaw, string name = null)
            {
                var prefab = LoadPath(path);
                if (prefab == null) return null;
                var go = stand(prefab, parent);
                if (go != null)
                {
                    go.transform.localPosition = at;
                    go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
                    if (!string.IsNullOrEmpty(name)) go.name = name;
                }
                return go;
            }

            GameObject Piece(string name, Vector3 at, int yaw, bool prop = false)
                => PiecePath((prop ? CityProps : CityEnvironment) + name + ".prefab",
                             at, yaw);

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

            float gateLeft = plan.Width * 0.5f - ParkingBlockPlan.GateWidth * 0.5f;
            float gateRight = plan.Width * 0.5f + ParkingBlockPlan.GateWidth * 0.5f;

            void FenceRun(Vector3 a, Vector3 b)
            {
                var delta = b - a;
                float length = delta.magnitude;
                if (length < 0.1f) return;
                var direction = delta / length;
                int yaw = Mathf.RoundToInt(Mathf.Atan2(-direction.z, direction.x) * Mathf.Rad2Deg);
                for (float at = 0f; at < length - 0.01f; at += Cell)
                {
                    float pieceLength = Mathf.Min(Cell, length - at);
                    var go = Piece(Fence, a + direction * at + Vector3.up * 0.04f, yaw);
                    if (go != null)
                        go.transform.localScale = new Vector3(pieceLength / Cell, 1f, 1f);
                }
            }

            void PerimeterFence()
            {
                FenceRun(new Vector3(0f, 0f, 0.55f), new Vector3(gateLeft, 0f, 0.55f));
                FenceRun(new Vector3(gateRight, 0f, 0.55f),
                         new Vector3(plan.Width, 0f, 0.55f));
                FenceRun(new Vector3(0f, 0f, plan.Depth - 0.55f),
                         new Vector3(plan.Width, 0f, plan.Depth - 0.55f));
                FenceRun(new Vector3(0.55f, 0f, 0.55f),
                         new Vector3(0.55f, 0f, plan.Depth - 0.55f));
                FenceRun(new Vector3(plan.Width - 0.55f, 0f, 0.55f),
                         new Vector3(plan.Width - 0.55f, 0f, plan.Depth - 0.55f));
            }

            void ThinLotKerb()
            {
                if (gateLeft > 0.1f) Tile(Kerb, 0f, 0f, 180, gateLeft, KerbDepth, 0.02f);
                if (plan.Width - gateRight > 0.1f)
                    Tile(Kerb, gateRight, 0f, 180, plan.Width - gateRight, KerbDepth, 0.02f);
                Tile(Kerb, 0f, plan.Depth - KerbDepth, 0, plan.Width, KerbDepth, 0.02f);
                Tile(Kerb, 0f, 0f, 270, KerbDepth, plan.Depth, 0.02f);
                Tile(Kerb, plan.Width - KerbDepth, 0f, 90, KerbDepth, plan.Depth, 0.02f);
            }

            void UrbanPavement()
            {
                // The block owns the shared ten-metre pavement band. Only its outside row is
                // kerbed; the row next to the parking surface is flat paving. Both rows of the
                // central driveway are road all the way to the carriageway.
                float driveWidth = Mathf.Ceil(ParkingBlockPlan.GateWidth / Cell) * Cell;
                float driveLeft = Mathf.Floor((plan.Width - driveWidth) * 0.5f / Cell) * Cell;
                float driveRight = driveLeft + driveWidth;

                float pavement = CoreBlockMetrics.PavementWidth;
                float x0 = -pavement, x1 = plan.Width + pavement;
                float z0 = -pavement, z1 = plan.Depth + pavement;
                for (float x = x0; x < x1 - 0.01f; x += Cell)
                    for (float z = z0; z < z1 - 0.01f; z += Cell)
                    {
                        if (x >= 0f && x < plan.Width && z >= 0f && z < plan.Depth) continue;
                        bool west = x <= x0 + 0.01f, east = x + Cell >= x1 - 0.01f;
                        bool south = z <= z0 + 0.01f, north = z + Cell >= z1 - 0.01f;
                        bool drive = z < 0f && x + Cell > driveLeft + 0.01f &&
                                     x < driveRight - 0.01f;
                        if (drive)
                        {
                            Tile(Asphalt, x, z, 0, Cell, Cell);
                            continue;
                        }
                        if ((west || east) && (south || north))
                        {
                            int yaw = north ? (east ? 0 : 270) : (east ? 90 : 180);
                            Tile(KerbCorner, x, z, yaw, Cell, Cell, 0.02f);
                        }
                        else if (south || north || west || east)
                        {
                            int yaw = north ? 0 : east ? 90 : south ? 180 : 270;
                            Tile(Kerb, x, z, yaw, Cell, Cell, 0.02f);
                        }
                        else Tile(Paving, x, z, 0, Cell, Cell);
                    }

                float kerbLane = -pavement + Cell * 0.5f;
                PiecePath(BollardPath, new Vector3(driveLeft - 0.75f, 0.1f, kerbLane), 0,
                          "driveway bollard");
                PiecePath(BollardPath, new Vector3(driveRight + 0.75f, 0.1f, kerbLane), 0,
                          "driveway bollard");
                for (float x = 7.5f; x < plan.Width - 5f; x += 15f)
                    Piece("SM_Prop_ParkingMeter_01",
                          new Vector3(x, 0.1f, plan.Depth + pavement - 2.7f), 180, prop: true);
            }

            void Lights()
            {
                if (style == ParkingBlockStyle.UrbanBlock)
                {
                    float lane = CoreBlockMetrics.PavementWidth - Cell * 0.5f;
                    PiecePath(LampPath, new Vector3(-lane, 0.1f, plan.Depth * 0.3f), 90,
                              "parking light");
                    PiecePath(LampPath, new Vector3(plan.Width + lane, 0.1f,
                                                    plan.Depth * 0.7f), 270,
                              "parking light");
                    PiecePath(LampPath, new Vector3(5f, 0.1f, -lane), 180,
                              "parking light");
                    PiecePath(LampPath, new Vector3(plan.Width - 5f, 0.1f,
                                                    plan.Depth + lane), 0,
                              "parking light");
                    return;
                }

                PiecePath(LampPath, new Vector3(2f, 0.1f, plan.Depth - 2f), 0,
                          "parking light");
                PiecePath(LampPath, new Vector3(plan.Width - 2f, 0.1f,
                                                plan.Depth - 2f), 180,
                          "parking light");
                if (plan.Depth > 30f)
                    PiecePath(LampPath, new Vector3(plan.Width * 0.5f, 0.1f,
                                                    plan.Depth - 2f), 180,
                              "parking light");
            }

            void PaymentBooth()
            {
                // Five metres of raised concrete keep the booth physically and visually out
                // of the swept vehicle envelope. The old position sat beside the turn itself.
                float boothX = gateRight + 4.5f;
                Tile(Kerb, boothX - 2.5f, 1.2f, 0, 5f, 5f, 0.04f);
                PiecePath(BoothPath, new Vector3(boothX, 0.08f, 3.8f), 270,
                          "parking payment booth");
                PiecePath(PayPath, new Vector3(gateLeft - 1.1f, 0f, 2f), 180,
                          "ticket dispenser");
                PiecePath(ConsolePath, new Vector3(gateRight + 1.2f, 0f, 1.5f), 180,
                          "payment console");
                for (int i = 0; i < 3; i++)
                    PiecePath(BollardPath,
                              new Vector3(boothX - 2.1f + i * 2.1f, 0.08f, 6f), 0,
                              "booth bollard");
            }

            TollArm PaymentBarrier()
            {
                var prefab = LoadPath(BoomPath);
                var go = PiecePath(BoomPath, new Vector3(gateRight, 0f, 1.05f), 0,
                                   "parking payment barrier");
                if (prefab == null || go == null) return null;

                var bounds = FreewayKit.Measure(prefab);
                bool armAlongX = Mathf.Abs(bounds.center.x) >= Mathf.Abs(bounds.center.z);
                var axis = armAlongX ? Vector3.forward : Vector3.right;
                float lift = armAlongX
                    ? (bounds.center.x >= 0f ? 75f : -75f)
                    : (bounds.center.z >= 0f ? -75f : 75f);
                var arm = FreewayKit.BoomOf(go.transform);
                return arm == null
                    ? new TollArm(go.transform, axis, lift)
                    : new TollArm(arm, Quaternion.Inverse(arm.localRotation) * axis, lift);
            }

            // Surface first, then paint, then raised entrance furniture.
            for (float x = 0f; x < plan.Width - 0.01f; x += Cell)
                for (float z = 0f; z < plan.Depth - 0.01f; z += Cell)
                {
                    float sx = Mathf.Min(Cell, plan.Width - x);
                    float sz = Mathf.Min(Cell, plan.Depth - z);
                    if (!plan.ContainsSurface(new Vector2(x + sx * 0.5f, z + sz * 0.5f)))
                        continue;
                    Tile(Asphalt, x, z, 0, sx, sz);
                }

            plan.Markings.Add(new ParkingBlockPlan.Stripe(
                new Vector2(gateLeft + 0.4f, 1.4f),
                new Vector2(gateRight - 0.4f, 1.4f)));

            var linePrefab = Load(ParkingLines);
            var lineRenderer = linePrefab != null
                ? linePrefab.GetComponentInChildren<Renderer>(true)
                : null;
            var lineMaterial = WhitePaint != null
                ? WhitePaint
                : lineRenderer != null ? lineRenderer.sharedMaterial : null;
            EmitLines(plan, lineMaterial, parent);

            if (style == ParkingBlockStyle.UrbanBlock) UrbanPavement();
            else ThinLotKerb();

            if (style != ParkingBlockStyle.UrbanBlock) PerimeterFence();
            Lights();

            var arrow = Piece(Arrow,
                new Vector3(plan.Width * 0.5f + 2.5f, 0.08f, 5f), 0);
            if (arrow != null) arrow.name = "parking entry arrow";

            float signZ = style == ParkingBlockStyle.UrbanBlock
                ? -CoreBlockMetrics.PavementWidth + Cell * 0.5f
                : 1.2f;
            var sign = Piece(Sign,
                new Vector3(gateRight + 1.2f, 0f, signZ), 180, prop: true);
            if (sign != null) sign.name = "parking sign";

            PaymentBooth();
            Piece("SM_Prop_Trashbin_01",
                  new Vector3(gateRight + 2.4f, 0f,
                              style == ParkingBlockStyle.UrbanBlock ? signZ + 0.1f : 2.2f),
                  0, prop: true);

            return PaymentBarrier();
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
