using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>Thin scene component for the parking generator review scene.</summary>
    public sealed class ParkingDemoBuilder : MonoBehaviour
    {
        public int seed = 1987;
        [Range(1, 8)] public int carsPerLot = 3;

        void Awake()
        {
#if UNITY_EDITOR
            var host = gameObject.AddComponent<StandaloneDistrictHost>();
            host.cameraDistance = 180f;
            host.cameraYaw = 18f;
            host.cameraPitch = 55f;
            host.cameraFar = 800f;
            host.cameraPivot = new Vector3(105f, 0f, 18f);
            host.hint = "WASD/arrows: move   Q/E or right-drag: rotate   wheel: zoom   " +
                        "Space: pause   , . : slower/faster";
            host.HostSeeded(new ParkingDemoDistrict { carsPerLot = carsPerLot }, seed);
#else
            Debug.LogError("[ParkingDemo] The demo loads Synty prefabs through the AssetDatabase and only runs in the editor.");
#endif
        }
    }

    /// <summary>
    /// Review ground for the parking generator: a full independent lot, a shallow pocket in
    /// front of a building, and an L-shaped lot cut around a building footprint. All three
    /// connect to the same public street and run the same parking-car lifecycle.
    /// </summary>
    public sealed class ParkingDemoDistrict : IDistrict
    {
        const float RoadHalf = 7.5f;
        const float RoadSpeed = 9f;
        const float Cell = 5f;
        const string CityEnvironment = "Assets/Synty/PolygonCity/Prefabs/Environments/";

        static readonly Rect Independent = new Rect(0f, RoadHalf, 60f, 40f);
        static readonly Rect Pocket = new Rect(75f, RoadHalf, 55f, 18f);
        static readonly Rect PocketBuilding = new Rect(75f, RoadHalf + 18f, 55f, 22f);
        static readonly Rect Wrapped = new Rect(145f, RoadHalf, 65f, 40f);
        static readonly Rect WrappedHole = new Rect(0f, 20f, 25f, 20f);

        readonly List<DistrictPortal> _portals = new List<DistrictPortal>();
        readonly List<RoadEdge> _edges = new List<RoadEdge>();
        readonly List<ParkingLot> _lots = new List<ParkingLot>();
        int _seed;
        Transform _quarter;

        public int carsPerLot = 3;
        public string Name => "Parking Demo";
        public DistrictFrame Frame { get; set; } = DistrictFrame.Identity;
        public Rect LocalBounds => new Rect(-15f, -RoadHalf, 240f, 65f);
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

            var independent = ParkingBlockSite.Build(
                Independent, ParkingEntrySide.South, _quarter, stand);
            independent.Root.name = "OPTION 1 - Independent full lot";

            var pocket = ParkingBlockSite.Build(
                Pocket, ParkingEntrySide.South, _quarter, stand);
            pocket.Root.name = "OPTION 2 - Embedded parking strip";
            LayGrass(PocketBuilding, _quarter, stand);
            StandBuilding("Assets/CityKit/Buildings/building-diner.prefab",
                          PocketBuilding, "Embedded block building", _quarter, stand);

            var wrapped = ParkingBlockSite.Build(
                Wrapped, ParkingEntrySide.South, _quarter, stand,
                new[] { WrappedHole });
            wrapped.Root.name = "OPTION 3 - L-shaped parking around building";
            var wrappedBuilding = new Rect(
                Wrapped.xMin + WrappedHole.xMin, Wrapped.yMin + WrappedHole.yMin,
                WrappedHole.width, WrappedHole.height);
            LayGrass(wrappedBuilding, _quarter, stand);
            StandBuilding("Assets/CityKit/Buildings/building-coffeeshop.prefab",
                          wrappedBuilding, "Wrapped block building", _quarter, stand);

            _quarter.SetPositionAndRotation(Frame.origin, Frame.Rotation);
            BuildRoadGraph();

            var live = host.LiveRoot("Parking Traffic");
            AddLot(independent, carsPerLot, live, 0);
            AddLot(pocket, carsPerLot, live, 1);
            AddLot(wrapped, carsPerLot, live, 2);
            host.RegisterRoads(_edges);

            Debug.Log($"[ParkingDemo] 3 shapes, " +
                      $"{independent.Plan.Stalls.Count}/{pocket.Plan.Stalls.Count}/{wrapped.Plan.Stalls.Count} bays, " +
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

            float from = -15f, to = 225f;
            for (float x = from; x < to; x += Cell)
            {
                Tile(bare, x, -RoadHalf, 90, Cell, 2.5f);
                Tile(roadHalf, x, -5f, 270, Cell, Cell);
                Tile(roadHalf, x, 0f, 90, Cell, Cell);
                Tile(bare, x, 5f, 90, Cell, 2.5f);
            }
        }

        static void LayGrass(
            Rect box, Transform parent, System.Func<GameObject, Transform, GameObject> stand)
        {
            var grass = DemoAssetLoad.Load<GameObject>(
                CityEnvironment + "SM_Env_Grass_01.prefab");
            if (grass == null) return;
            for (float x = box.xMin; x < box.xMax - 0.01f; x += Cell)
                for (float z = box.yMin; z < box.yMax - 0.01f; z += Cell)
                {
                    float sx = Mathf.Min(Cell, box.xMax - x);
                    float sz = Mathf.Min(Cell, box.yMax - z);
                    var go = stand(grass, parent);
                    go.transform.localPosition = new Vector3(x + sx, -0.02f, z + sz);
                    go.transform.localScale = new Vector3(sx / Cell, 1f, sz / Cell);
                }
        }

        static void StandBuilding(
            string path, Rect target, string name, Transform parent,
            System.Func<GameObject, Transform, GameObject> stand)
        {
            var prefab = DemoAssetLoad.Load<GameObject>(path);
            if (prefab == null) return;
            var go = stand(prefab, parent);
            go.name = name;

            if (!TryBounds(go, out var bounds))
            {
                go.transform.localPosition = new Vector3(target.center.x, 0f, target.center.y);
                return;
            }

            float scale = Mathf.Min(
                (target.width - 2f) / Mathf.Max(1f, bounds.size.x),
                (target.height - 2f) / Mathf.Max(1f, bounds.size.z));
            scale = Mathf.Clamp(scale, 0.45f, 1.15f);
            go.transform.localScale *= scale;
            TryBounds(go, out bounds);
            var wanted = new Vector3(target.center.x, 0f, target.center.y);
            go.transform.position += wanted - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        }

        static bool TryBounds(GameObject go, out Bounds bounds)
        {
            bounds = new Bounds();
            bool any = false;
            foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                if (!any) { bounds = renderer.bounds; any = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            return any;
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
