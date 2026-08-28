using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>Thin scene component for the parking generator review scene.</summary>
    public sealed class ParkingDemoBuilder : MonoBehaviour
    {
        public int seed = 1987;
        [Range(1, 20)] public int carsPerLot = 12;

        void Awake()
        {
#if UNITY_EDITOR
            var host = gameObject.AddComponent<StandaloneDistrictHost>();
            host.cameraDistance = 210f;
            host.cameraYaw = 18f;
            host.cameraPitch = 55f;
            host.cameraFar = 800f;
            host.cameraPivot = new Vector3(120f, 0f, 22f);
            host.hint = "WASD/arrows: move   Q/E or right-drag: rotate   wheel: zoom   " +
                        "Space: pause   , . : slower/faster";
            host.HostSeeded(new ParkingDemoDistrict { carsPerLot = carsPerLot }, seed);
#else
            Debug.LogError("[ParkingDemo] The demo loads Synty prefabs through the AssetDatabase and only runs in the editor.");
#endif
        }
    }

    /// <summary>
    /// Review ground for three actual parking programmes: an attended pay lot, a whole urban
    /// block with its own pavement, and a fenced long-stay lot.
    /// All three connect to the same public street and run the same parking-car lifecycle.
    /// </summary>
    public sealed class ParkingDemoDistrict : IDistrict
    {
        const float RoadHalf = 7.5f;
        const float RoadSpeed = 9f;
        const float Cell = 5f;
        const string CityEnvironment = "Assets/Synty/PolygonCity/Prefabs/Environments/";

        static readonly Rect Attended = new Rect(0f, RoadHalf, 65f, 45f);
        static readonly Rect UrbanBlock = new Rect(80f, RoadHalf, 80f, 60f);
        static readonly Rect LongStay = new Rect(175f, RoadHalf, 70f, 45f);
        readonly List<DistrictPortal> _portals = new List<DistrictPortal>();
        readonly List<RoadEdge> _edges = new List<RoadEdge>();
        readonly List<ParkingLot> _lots = new List<ParkingLot>();
        int _seed;
        Transform _quarter;

        public int carsPerLot = 12;
        public string Name => "Parking Demo";
        public DistrictFrame Frame { get; set; } = DistrictFrame.Identity;
        public Rect LocalBounds => new Rect(-15f, -RoadHalf, 265f, 70f);
        public IReadOnlyList<DistrictPortal> Portals => _portals;
        public LaneNet Net { get; private set; }

        public void Plan(float[] links, int seed) => _seed = seed;

        public void Reserve(DistrictReservations into)
        {
            var world = Frame.ToWorldRect(LocalBounds);
            into.Pave(world);
            into.Level(world, RoadDemoBuilder.RoadBed);
            into.NoFlora(world);
        }

        public void Build(IDistrictHost host)
        {
            _quarter = new GameObject("Parking Generator Options").transform;
            _quarter.SetParent(host.StaticRoot("Parking Demo"), false);
            System.Func<GameObject, Transform, GameObject> stand =
                (prefab, parent) => Object.Instantiate(prefab, parent);

            LayStreet(_quarter, stand);

            var attended = ParkingBlockSite.Build(
                Attended, ParkingEntrySide.South, _quarter, stand,
                style: ParkingBlockStyle.Attended);
            attended.Root.name = "OPTION 1 - Attended public parking";

            var urban = ParkingBlockSite.Build(
                UrbanBlock, ParkingEntrySide.South, _quarter, stand,
                style: ParkingBlockStyle.UrbanBlock);
            urban.Root.name = "OPTION 2 - Full urban block with sidewalk";

            var longStay = ParkingBlockSite.Build(
                LongStay, ParkingEntrySide.South, _quarter, stand,
                style: ParkingBlockStyle.LongStay);
            longStay.Root.name = "OPTION 3 - Fenced long-stay parking";

            _quarter.SetPositionAndRotation(Frame.origin, Frame.Rotation);
            BuildRoadGraph();

            var live = host.LiveRoot("Parking Traffic");
            // Gate booms are the non-static parts of the lots. Move all of them out before the
            // host's static merge so TollArm rotates visible meshes rather than baked sources.
            void KeepGateLive(ParkingBlockSite site)
            {
                if (site.GateRoot != null) site.GateRoot.SetParent(live, true);
            }
            KeepGateLive(attended);
            KeepGateLive(urban);
            KeepGateLive(longStay);
            AddLot(attended, carsPerLot, live, 0);
            AddLot(urban, carsPerLot, live, 1);
            AddLot(longStay, carsPerLot, live, 2);
            host.RegisterRoads(_edges);

            Debug.Log($"[ParkingDemo] attended/urban-block/long-stay, " +
                      $"{attended.Plan.Stalls.Count}/{urban.Plan.Stalls.Count}/{longStay.Plan.Stalls.Count} bays, " +
                      $"{_lots.Count} live lots, {_lotsCars()} cars.");
        }

        void AddLot(ParkingBlockSite site, int count, Transform live, int salt)
        {
            var lot = new ParkingLot(site, Net, count, unchecked(_seed * 7919 + salt * 104729), live);
            if (lot.CarCount > 0) _lots.Add(lot);
        }

        int _lotsCars()
        {
            int total = 0;
            for (int i = 0; i < _lots.Count; i++) total += _lots[i].CarCount;
            return total;
        }

        void BuildRoadGraph()
        {
            Net = new LaneNet();
            var localA = new Vector3(LocalBounds.xMin, 0f, 0f);
            var localB = new Vector3(LocalBounds.xMax, 0f, 0f);
            var a = Frame.ToWorld(localA);
            var b = Frame.ToWorld(localB);
            var nodeA = Net.AddNode(a.x, a.z, 4f, RoadHalf);
            var nodeB = Net.AddNode(b.x, b.z, 4f, RoadHalf);
            Net.AddRoad(a, b, RoadHalf, new[] { 2.5f }, RoadSpeed,
                        nodeA, nodeB, northSouth: false);
            Net.Finish();
            LaneNet.Active = Net;
            _edges.Clear();
            _edges.AddRange(Net.Edges);
        }

        static void LayStreet(
            Transform parent, System.Func<GameObject, Transform, GameObject> stand)
        {
            var roadHalf = DemoAssetLoad.Load<GameObject>(
                CityEnvironment + "SM_Env_Road_YellowLines_02.prefab");
            var bare = DemoAssetLoad.Load<GameObject>(
                CityEnvironment + "SM_Env_Road_Bare_01.prefab");
            if (roadHalf == null || bare == null) return;

            void Tile(GameObject prefab, float x, float z, int yaw, float sx, float sz)
            {
                Vector3 pivot;
                Vector3 scale;
                if (yaw == 90)
                {
                    pivot = new Vector3(x + sx, 0f, z);
                    scale = new Vector3(sz / Cell, 1f, sx / Cell);
                }
                else if (yaw == 270)
                {
                    pivot = new Vector3(x, 0f, z + sz);
                    scale = new Vector3(sz / Cell, 1f, sx / Cell);
                }
                else
                {
                    pivot = new Vector3(x + sx, 0f, z + sz);
                    scale = new Vector3(sx / Cell, 1f, sz / Cell);
                }
                var go = stand(prefab, parent);
                go.transform.localPosition = pivot;
                go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
                go.transform.localScale = scale;
            }

            float from = -15f, to = 250f;
            for (float x = from; x < to; x += Cell)
            {
                Tile(bare, x, -RoadHalf, 90, Cell, 2.5f);
                Tile(roadHalf, x, -5f, 270, Cell, Cell);
                Tile(roadHalf, x, 0f, 90, Cell, Cell);
                Tile(bare, x, 5f, 90, Cell, 2.5f);
            }
        }

        public void Tick(float dt)
        {
            for (int i = 0; i < _lots.Count; i++) _lots[i].Tick(dt);
        }

        public void Dispose()
        {
            for (int i = 0; i < _lots.Count; i++) _lots[i].Dispose();
            _lots.Clear();
            if (ReferenceEquals(LaneNet.Active, Net)) LaneNet.Active = null;
        }
    }
}
