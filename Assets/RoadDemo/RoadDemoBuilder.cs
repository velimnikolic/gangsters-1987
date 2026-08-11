using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    // Self-contained road-network demo. Everything is built at Play time from the
    // Synty POLYGON City road kit (5 m lattice pieces) and POLYGON Palm City
    // vehicles: a grid of boulevards and ordinary two-way streets with sidewalks,
    // signalised intersections and cars driving the lane graph.
    //
    // Piece convention (measured against the pack's own demo scene): every kit
    // piece has its pivot on a corner and covers the 5x5 m square towards local
    // -X/-Z, so a cell (min corner mx,mz) is filled by pivots:
    //   yaw 0 -> (mx+5, mz+5), yaw 90 -> (mx+5, mz), yaw 180 -> (mx, mz),
    //   yaw 270 -> (mx, mz+5).
    public class RoadDemoBuilder : MonoBehaviour
    {
        [Header("Grid (centreline positions, multiples of 5)")]
        public float[] verticalRoadX = { 0f, 70f, 140f, 210f, 280f };
        public bool[] verticalIsBoulevard = { false, true, false, true, false };
        public float[] horizontalRoadZ = { 0f, 70f, 140f, 210f };
        public bool[] horizontalIsBoulevard = { false, true, false, false };

        [Header("Traffic")]
        public int carCount = 70;
        public float streetSpeed = 9f;
        public float boulevardSpeed = 13f;
        public int pedestrianCount = 120;

        const float Cell = 5f;
        const float StreetHalf = 5f;     // carriageway half width: 2 lanes
        const float BoulevardHalf = 15f; // 2+2 lanes plus a 10 m median

        const string CityEnv = "Assets/Synty/PolygonCity/Prefabs/Environments/";
        const string CityProps = "Assets/Synty/PolygonCity/Prefabs/Props/";
        const string PalmEnv = "Assets/Synty/PolygonPalmCity/Prefabs/Environment/";
        const string PalmVeh = "Assets/Synty/PolygonPalmCity/Prefabs/Vehicles/";

        GameObject _roadWest, _roadEast;    // YellowLines halves of a two-way street
        GameObject _laneEdge, _laneDash;    // boulevard kerb lane / inner dashed lane
        GameObject _median, _bare, _crossing;
        GameObject _swStraight, _swCorner, _divider;
        GameObject _poleBase, _poleArm, _poleLights;
        readonly List<GameObject> _palms = new List<GameObject>();
        readonly List<GameObject> _carPrefabs = new List<GameObject>();
        readonly List<GameObject> _pedPrefabs = new List<GameObject>();
        AnimationClip _walkClip, _idleClip;

        // street dressing (PalmCity prop vocabulary, mined from its demo scene)
        readonly List<GameObject> _grates = new List<GameObject>();
        readonly List<GameObject> _lamps = new List<GameObject>();
        readonly List<GameObject> _bins = new List<GameObject>();
        readonly List<GameObject> _benches = new List<GameObject>();
        readonly List<GameObject> _planters = new List<GameObject>();
        readonly List<GameObject> _powerboxes = new List<GameObject>();
        readonly List<GameObject> _bushes = new List<GameObject>();
        readonly List<GameObject> _saplings = new List<GameObject>();
        readonly List<GameObject> _wires = new List<GameObject>();
        readonly List<GameObject> _hedges = new List<GameObject>();
        readonly List<GameObject> _topiary = new List<GameObject>();
        readonly List<GameObject> _chairs = new List<GameObject>();
        readonly List<GameObject> _tables = new List<GameObject>();
        readonly List<GameObject> _umbrellas = new List<GameObject>();
        GameObject _bag, _bagOpen, _bollard, _hydrant, _mailbox, _newsstand, _powerpole;
        GameObject _bikeStand, _signPole, _manhole;

        readonly HashSet<long> _cells = new HashSet<long>();
        RoadNode[,] _nodes;
        readonly List<RoadEdge> _edges = new List<RoadEdge>();
        readonly List<TrafficSignal> _signals = new List<TrafficSignal>();
        readonly List<DemoVehicle> _vehicles = new List<DemoVehicle>();
        readonly List<PedestrianAgent> _pedestrians = new List<PedestrianAgent>();
        readonly List<PedLink> _pedLinks = new List<PedLink>();
        SignalMaterials _signalMats;

        Transform _geometry, _flora, _traffic, _cars;

        void Awake()
        {
#if UNITY_EDITOR
            if (!LoadPrefabs()) return;
            _geometry = new GameObject("Geometry").transform;
            // palms live outside the static-batched root: their wind shader displaces
            // vertices in object space, and a combined mesh would swing them around
            // the batch pivot instead of their own trunks
            _flora = new GameObject("Flora").transform;
            _traffic = new GameObject("Traffic").transform;
            _cars = new GameObject("Cars").transform;

            BuildNodes();
            BuildRoadsAndSidewalks();
            DressStreets();
            BuildGraph();
            BuildSignals();
            BuildPedGraph();
            SpawnCars();
            SpawnPedestrians();
            BuildEnvironment();

            StaticBatchingUtility.Combine(_geometry.gameObject);
#else
            Debug.LogError("[RoadDemo] This demo loads Synty prefabs through the AssetDatabase and only runs in the editor.");
#endif
        }

        void Update()
        {
            for (int i = 0; i < _signals.Count; i++) _signals[i].UpdateBulbs(_signalMats);
            float dt = Time.deltaTime;
            for (int i = 0; i < _vehicles.Count; i++) _vehicles[i].Tick(dt);
            for (int i = 0; i < _pedestrians.Count; i++) _pedestrians[i].Tick(dt);
        }

        void OnDestroy()
        {
            for (int i = 0; i < _pedestrians.Count; i++) _pedestrians[i].Dispose();
        }

        // Edit-mode sketch of the network: the real geometry only exists after
        // pressing Play, so the Scene view shows the planned layout instead.
        void OnDrawGizmos()
        {
            if (Application.isPlaying) return;
            if (verticalRoadX == null || horizontalRoadZ == null ||
                verticalRoadX.Length == 0 || horizontalRoadZ.Length == 0) return;

            float z0 = horizontalRoadZ[0] - 20f, z1 = horizontalRoadZ[horizontalRoadZ.Length - 1] + 20f;
            float x0 = verticalRoadX[0] - 20f, x1 = verticalRoadX[verticalRoadX.Length - 1] + 20f;

            for (int i = 0; i < verticalRoadX.Length; i++)
            {
                bool blvd = i < verticalIsBoulevard.Length && verticalIsBoulevard[i];
                Gizmos.color = blvd ? new Color(1f, 0.8f, 0.2f, 0.5f) : new Color(1f, 1f, 1f, 0.35f);
                Gizmos.DrawCube(new Vector3(verticalRoadX[i], 0f, (z0 + z1) * 0.5f),
                    new Vector3((blvd ? BoulevardHalf : StreetHalf) * 2f, 0.1f, z1 - z0));
            }
            for (int j = 0; j < horizontalRoadZ.Length; j++)
            {
                bool blvd = j < horizontalIsBoulevard.Length && horizontalIsBoulevard[j];
                Gizmos.color = blvd ? new Color(1f, 0.8f, 0.2f, 0.5f) : new Color(1f, 1f, 1f, 0.35f);
                Gizmos.DrawCube(new Vector3((x0 + x1) * 0.5f, 0f, horizontalRoadZ[j]),
                    new Vector3(x1 - x0, 0.1f, (blvd ? BoulevardHalf : StreetHalf) * 2f));
            }
        }

#if UNITY_EDITOR
        static GameObject Load(string path)
        {
            var go = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go == null) Debug.LogError("[RoadDemo] missing prefab: " + path);
            return go;
        }

        static List<string> ScanPrefabPaths(string[] folders, string[] denySubstrings)
        {
            var paths = new List<string>();
            foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:Prefab", folders))
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                string low = path.ToLowerInvariant();
                bool denied = false;
                foreach (var deny in denySubstrings)
                    if (low.Contains(deny)) { denied = true; break; }
                if (!denied) paths.Add(path);
            }
            return paths;
        }

        bool LoadPrefabs()
        {
            _roadWest = Load(CityEnv + "SM_Env_Road_YellowLines_02.prefab");
            _roadEast = Load(CityEnv + "SM_Env_Road_YellowLines_01.prefab");
            _laneEdge = Load(CityEnv + "SM_Env_Road_02.prefab");
            _laneDash = Load(CityEnv + "SM_Env_Road_Lines_01.prefab");
            _median = Load(CityEnv + "SM_Env_Road_Median_01.prefab");
            _bare = Load(CityEnv + "SM_Env_Road_Bare_01.prefab");
            _crossing = Load(CityEnv + "SM_Env_Road_Crossing_01.prefab");
            _swStraight = Load(CityEnv + "SM_Env_Sidewalk_Straight_01.prefab");
            _swCorner = Load(CityEnv + "SM_Env_Sidewalk_Corner_01.prefab");
            _divider = Load(CityEnv + "SM_Env_Street_Divider_01.prefab");
            _poleBase = Load(CityProps + "SM_Prop_LightPole_Base_01.prefab");
            _poleArm = Load(CityProps + "SM_Prop_LightPole_Arm_01.prefab");
            _poleLights = Load(CityProps + "SM_Prop_LightPole_Lights_01.prefab");

            for (int i = 1; i <= 6; i++)
            {
                var palm = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                    PalmEnv + "SM_Env_Tree_Palm_0" + i + ".prefab");
                if (palm != null) _palms.Add(palm);
            }

            // road vehicles from every Synty pack in the project; boats, aircraft,
            // two-wheelers and attachment parts are filtered out by name
            string[] vehicleFolders =
            {
                "Assets/Synty/PolygonPalmCity/Prefabs/Vehicles",
                "Assets/Synty/PolygonCity/Prefabs/Vehicles",
                "Assets/Synty/PolygonPoliceStation/Prefabs/Vehicles",
            };
            string[] vehicleDeny =
            {
                "boat", "yacht", "jetski", "helicopter", "plane", "cart", "scooter",
                "bike", "moped", "bot", "steering", "wheel", "trailer", "monster",
                "quad", "attach",
            };
            foreach (var path in ScanPrefabPaths(vehicleFolders, vehicleDeny))
            {
                if (!System.IO.Path.GetFileName(path).StartsWith("SM_Veh")) continue;
                var v = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (v != null) _carPrefabs.Add(v);
            }

            // people from every Synty pack; only humanoid-rigged prefabs qualify
            // (the walk clip retargets onto any humanoid avatar)
            string[] characterFolders =
            {
                "Assets/Synty/PolygonPalmCity/Prefabs/Characters",
                "Assets/Synty/PolygonCity/Prefabs/Characters",
                "Assets/Synty/PolygonPoliceStation/Prefabs/Characters",
                "Assets/Synty/PolygonGeneric/Prefabs/Characters",
            };
            string[] characterDeny = { "attach", "charred", "skeleton", "robot", "space", "underwear" };
            foreach (var path in ScanPrefabPaths(characterFolders, characterDeny))
            {
                var chr = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (chr == null) continue;
                var animator = chr.GetComponentInChildren<Animator>();
                if (animator == null || animator.avatar == null || !animator.avatar.isHuman) continue;
                _pedPrefabs.Add(chr);
            }
            const string PalmProps = "Assets/Synty/PolygonPalmCity/Prefabs/Props/";
            void Bag(List<GameObject> into, params string[] names)
            {
                foreach (var n in names)
                {
                    var g = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PalmProps + n + ".prefab");
                    if (g == null)
                        g = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PalmEnv + n + ".prefab");
                    if (g != null) into.Add(g);
                }
            }
            Bag(_grates, "SM_Env_Plant_Grate_01", "SM_Env_Plant_Grate_02");
            Bag(_lamps, "SM_Prop_Street_Lamp_01", "SM_Prop_Street_Lamp_02", "SM_Prop_Street_Lamp_08");
            Bag(_bins, "SM_Prop_Trash_Bin_01", "SM_Prop_Trash_Bin_02", "SM_Prop_Trash_Bin_03", "SM_Prop_Trash_Bin_04");
            Bag(_benches, "SM_Prop_Bench_Seat_01", "SM_Prop_Bench_Seat_02");
            Bag(_planters, "SM_Prop_Planter_01", "SM_Prop_Planter_02", "SM_Prop_Planter_03", "SM_Prop_Planter_04");
            Bag(_powerboxes, "SM_Prop_Powerbox_01", "SM_Prop_PowerBox_02", "SM_Prop_PowerBoxes_02", "SM_Prop_PowerBoxes_03");
            Bag(_bushes, "SM_Env_Bush_01", "SM_Env_Bush_02", "SM_Env_Bush_03");
            Bag(_saplings, "SM_Env_Tree_Palm_Sapling_01", "SM_Env_Tree_Palm_Sapling_02", "SM_Env_Tree_Palm_Sapling_03",
                "SM_Env_Tree_Palm_Small_01", "SM_Env_Tree_Palm_Small_03", "SM_Env_Tree_Palm_Small_04", "SM_Env_Tree_Palm_Small_05");
            Bag(_wires, "SM_Prop_Powerline_02", "SM_Prop_Powerline_03");
            Bag(_hedges, "SM_Env_Hedge_02", "SM_Env_Hedge_03", "SM_Env_Hedge_04");
            Bag(_topiary, "SM_Env_Hedge_Topiary_02", "SM_Env_Hedge_Topiary_04", "SM_Env_Hedge_Topiary_05", "SM_Env_Hedge_Topiary_06");
            Bag(_chairs, "SM_Prop_Chair_01", "SM_Prop_Chair_03", "SM_Prop_Chair_04");
            Bag(_tables, "SM_Prop_Table_01", "SM_Prop_Table_Outdoor_01");
            Bag(_umbrellas, "SM_Prop_Umbrella_01", "SM_Prop_Umbrella_02", "SM_Prop_Umbrella_03");
            _bag = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PalmProps + "SM_Prop_Trash_Bag_01.prefab");
            _bagOpen = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PalmProps + "SM_Prop_Trash_Bag_Open_01.prefab");
            _bollard = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PalmProps + "SM_Prop_Bollard_02.prefab");
            _hydrant = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PalmProps + "SM_Prop_Fire_Hydrant_01.prefab");
            _mailbox = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PalmProps + "SM_Prop_Mailbox_01.prefab");
            _newsstand = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PalmProps + "SM_Prop_Newspaper_Stand_01.prefab");
            _powerpole = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PalmProps + "SM_Prop_Powerpole_01.prefab");
            _bikeStand = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PalmProps + "SM_Prop_Bike_Stand_02.prefab");
            _signPole = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PalmProps + "SM_Prop_Sign_Pole_02.prefab");
            _manhole = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PalmProps + "SM_Prop_Manhole_01.prefab");

            _walkClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AnimationClip>(
                "Assets/Animations/People/Standard Walk.anim");
            _idleClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AnimationClip>(
                "Assets/Animations/People/Breathing Idle.anim");
            if (_walkClip == null || _idleClip == null || _pedPrefabs.Count == 0)
                Debug.LogWarning("[RoadDemo] pedestrian assets missing; spawning without people");

            return _roadWest && _roadEast && _laneEdge && _laneDash && _median && _bare &&
                   _crossing && _swStraight && _swCorner && _divider &&
                   _poleBase && _poleArm && _poleLights && _carPrefabs.Count > 0;
        }
#endif

        // ------------------------------------------------------------------ layout

        float VHalf(int i) => verticalIsBoulevard[i] ? BoulevardHalf : StreetHalf;
        float HHalf(int j) => horizontalIsBoulevard[j] ? BoulevardHalf : StreetHalf;

        static long CellKey(float mx, float mz)
            => ((long)Mathf.RoundToInt(mx / Cell) << 32) ^ (uint)Mathf.RoundToInt(mz / Cell);

        void PlaceCell(GameObject prefab, float mx, float mz, int yaw)
        {
            Vector3 pivot;
            switch (yaw)
            {
                case 0: pivot = new Vector3(mx + Cell, 0f, mz + Cell); break;
                case 90: pivot = new Vector3(mx + Cell, 0f, mz); break;
                case 180: pivot = new Vector3(mx, 0f, mz); break;
                default: pivot = new Vector3(mx, 0f, mz + Cell); break;
            }
            Instantiate(prefab, pivot, Quaternion.Euler(0f, yaw, 0f), _geometry);
        }

        void PlaceCellOnce(GameObject prefab, float mx, float mz, int yaw)
        {
            if (_cells.Add(CellKey(mx, mz))) PlaceCell(prefab, mx, mz, yaw);
        }

        static float[] LaneRows(bool boulevard)
            => boulevard ? new[] { -15f, -10f, 5f, 10f } : new[] { -5f, 0f };

        void BuildNodes()
        {
            _nodes = new RoadNode[verticalRoadX.Length, horizontalRoadZ.Length];
            for (int i = 0; i < verticalRoadX.Length; i++)
                for (int j = 0; j < horizontalRoadZ.Length; j++)
                {
                    var n = new RoadNode
                    {
                        I = i, J = j,
                        X = verticalRoadX[i], Z = horizontalRoadZ[j],
                        XMin = verticalRoadX[i] - VHalf(i), XMax = verticalRoadX[i] + VHalf(i),
                        ZMin = horizontalRoadZ[j] - HHalf(j), ZMax = horizontalRoadZ[j] + HHalf(j),
                    };
                    _nodes[i, j] = n;
                    BuildNodeGeometry(n);
                }
        }

        void BuildNodeGeometry(RoadNode n)
        {
            bool north = n.J < horizontalRoadZ.Length - 1;
            bool south = n.J > 0;
            bool east = n.I < verticalRoadX.Length - 1;
            bool west = n.I > 0;
            bool vBlvd = verticalIsBoulevard[n.I];
            bool hBlvd = horizontalIsBoulevard[n.J];

            for (float mx = n.XMin; mx < n.XMax - 0.1f; mx += Cell)
                for (float mz = n.ZMin; mz < n.ZMax - 0.1f; mz += Cell)
                    PlaceCellOnce(_bare, mx, mz, 0);

            // north / south: zebra across the vertical road, or a sidewalk cap
            foreach (var side in new[] { (edge: n.ZMax, has: north, capYaw: 180), (edge: n.ZMin - Cell, has: south, capYaw: 0) })
            {
                if (side.has)
                {
                    foreach (float off in LaneRows(vBlvd))
                        PlaceCellOnce(_crossing, n.X + off, side.edge, 90);
                    if (vBlvd)
                    {
                        PlaceCellOnce(_median, n.X - Cell, side.edge, 180);
                        PlaceCellOnce(_median, n.X, side.edge, 0);
                    }
                }
                else
                {
                    for (float mx = n.XMin; mx < n.XMax - 0.1f; mx += Cell)
                        PlaceCellOnce(_swStraight, mx, side.edge, side.capYaw);
                }
            }

            // east / west: zebra across the horizontal road, or a sidewalk cap
            foreach (var side in new[] { (edge: n.XMax, has: east, capYaw: 270), (edge: n.XMin - Cell, has: west, capYaw: 90) })
            {
                if (side.has)
                {
                    foreach (float off in LaneRows(hBlvd))
                        PlaceCellOnce(_crossing, side.edge, n.Z + off, 0);
                    if (hBlvd)
                    {
                        PlaceCellOnce(_median, side.edge, n.Z - Cell, 90);
                        PlaceCellOnce(_median, side.edge, n.Z, 270);
                    }
                }
                else
                {
                    for (float mz = n.ZMin; mz < n.ZMax - 0.1f; mz += Cell)
                        PlaceCellOnce(_swStraight, side.edge, mz, side.capYaw);
                }
            }

            // corner slabs, kerb turned towards the intersection centre
            PlaceCellOnce(_swCorner, n.XMin - Cell, n.ZMin - Cell, 0);
            PlaceCellOnce(_swCorner, n.XMin - Cell, n.ZMax, 90);
            PlaceCellOnce(_swCorner, n.XMax, n.ZMax, 180);
            PlaceCellOnce(_swCorner, n.XMax, n.ZMin - Cell, 270);
        }

        void BuildRoadsAndSidewalks()
        {
            for (int i = 0; i < verticalRoadX.Length; i++)
                for (int j = 0; j + 1 < horizontalRoadZ.Length; j++)
                    FillVerticalSegment(i, _nodes[i, j], _nodes[i, j + 1]);

            for (int j = 0; j < horizontalRoadZ.Length; j++)
                for (int i = 0; i + 1 < verticalRoadX.Length; i++)
                    FillHorizontalSegment(j, _nodes[i, j], _nodes[i + 1, j]);
        }

        void FillVerticalSegment(int i, RoadNode a, RoadNode b)
        {
            float cx = verticalRoadX[i];
            bool blvd = verticalIsBoulevard[i];
            for (float mz = a.ZMax; mz < b.ZMin - 0.1f; mz += Cell)
            {
                if (blvd)
                {
                    PlaceCellOnce(_swStraight, cx - 20f, mz, 90);
                    PlaceCellOnce(_laneEdge, cx - 15f, mz, 180);
                    PlaceCellOnce(_laneDash, cx - 10f, mz, 180);
                    PlaceCellOnce(_median, cx - 5f, mz, 180);
                    PlaceCellOnce(_median, cx, mz, 0);
                    PlaceCellOnce(_laneDash, cx + 5f, mz, 0);
                    PlaceCellOnce(_laneEdge, cx + 10f, mz, 0);
                    PlaceCellOnce(_swStraight, cx + 15f, mz, 270);
                }
                else
                {
                    PlaceCellOnce(_swStraight, cx - 10f, mz, 90);
                    PlaceCellOnce(_roadWest, cx - 5f, mz, 0);
                    PlaceCellOnce(_roadEast, cx, mz, 0);
                    PlaceCellOnce(_swStraight, cx + 5f, mz, 270);
                }
            }

            if (!blvd) return;
            int step = 0;
            for (float mz = a.ZMax + 2f * Cell; mz <= b.ZMin - 3f * Cell; mz += Cell, step++)
            {
                Instantiate(_divider, new Vector3(cx, 0f, mz), Quaternion.identity, _geometry);
                if (step % 3 == 1 && _palms.Count > 0)
                    Instantiate(_palms[step % _palms.Count],
                        new Vector3(cx, 0.18f, mz + 2.5f), Quaternion.Euler(0f, step * 77f, 0f), _flora);
                else if (step % 3 == 2 && _bushes.Count > 0)
                    Instantiate(_bushes[step % _bushes.Count],
                        new Vector3(cx + Random.Range(-0.4f, 0.4f), 0.15f, mz + 2.5f),
                        Quaternion.Euler(0f, step * 53f, 0f), _flora);
            }
            for (float mz = a.ZMax + 2f * Cell; mz <= b.ZMin - 2f * Cell; mz += 4f * Cell)
                if (_palms.Count > 0)
                {
                    Instantiate(_palms[(int)(mz / Cell) % _palms.Count],
                        new Vector3(cx - 18.9f, 0.1f, mz), Quaternion.Euler(0f, mz * 13f, 0f), _flora);
                    Instantiate(_palms[(int)(mz / Cell + 3) % _palms.Count],
                        new Vector3(cx + 18.9f, 0.1f, mz), Quaternion.Euler(0f, mz * 29f, 0f), _flora);
                }
        }

        void FillHorizontalSegment(int j, RoadNode a, RoadNode b)
        {
            float cz = horizontalRoadZ[j];
            bool blvd = horizontalIsBoulevard[j];
            for (float mx = a.XMax; mx < b.XMin - 0.1f; mx += Cell)
            {
                if (blvd)
                {
                    PlaceCellOnce(_swStraight, mx, cz - 20f, 0);
                    PlaceCellOnce(_laneEdge, mx, cz - 15f, 90);
                    PlaceCellOnce(_laneDash, mx, cz - 10f, 90);
                    PlaceCellOnce(_median, mx, cz - 5f, 90);
                    PlaceCellOnce(_median, mx, cz, 270);
                    PlaceCellOnce(_laneDash, mx, cz + 5f, 270);
                    PlaceCellOnce(_laneEdge, mx, cz + 10f, 270);
                    PlaceCellOnce(_swStraight, mx, cz + 15f, 180);
                }
                else
                {
                    PlaceCellOnce(_swStraight, mx, cz - 10f, 0);
                    PlaceCellOnce(_roadEast, mx, cz - 5f, 90);
                    PlaceCellOnce(_roadWest, mx, cz, 90);
                    PlaceCellOnce(_swStraight, mx, cz + 5f, 180);
                }
            }

            if (!blvd) return;
            int step = 0;
            for (float mx = a.XMax + 2f * Cell; mx <= b.XMin - 3f * Cell; mx += Cell, step++)
            {
                Instantiate(_divider, new Vector3(mx, 0f, cz), Quaternion.Euler(0f, 90f, 0f), _geometry);
                if (step % 3 == 1 && _palms.Count > 0)
                    Instantiate(_palms[step % _palms.Count],
                        new Vector3(mx + 2.5f, 0.18f, cz), Quaternion.Euler(0f, step * 61f, 0f), _flora);
                else if (step % 3 == 2 && _bushes.Count > 0)
                    Instantiate(_bushes[step % _bushes.Count],
                        new Vector3(mx + 2.5f, 0.15f, cz + Random.Range(-0.4f, 0.4f)),
                        Quaternion.Euler(0f, step * 53f, 0f), _flora);
            }
            for (float mx = a.XMax + 2f * Cell; mx <= b.XMin - 2f * Cell; mx += 4f * Cell)
                if (_palms.Count > 0)
                {
                    Instantiate(_palms[(int)(mx / Cell) % _palms.Count],
                        new Vector3(mx, 0.1f, cz - 18.9f), Quaternion.Euler(0f, mx * 13f, 0f), _flora);
                    Instantiate(_palms[(int)(mx / Cell + 3) % _palms.Count],
                        new Vector3(mx, 0.1f, cz + 18.9f), Quaternion.Euler(0f, mx * 29f, 0f), _flora);
                }
        }

        // --------------------------------------------------------- street dressing
        // Densities and the prop mix follow the PalmCity demo scene: palm-in-grate
        // kerb planting, benches with bins, trash-bag clusters, planters, power
        // boxes, bollarded corners and powerline poles carrying scaled wire spans.

        static float YawOf(Vector3 d) => Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;

        static T Pick<T>(List<T> l) => l[Random.Range(0, l.Count)];

        void Prop(GameObject prefab, Vector3 pos, float yaw, Transform parent)
        {
            if (prefab != null)
                Instantiate(prefab, pos, Quaternion.Euler(0f, yaw, 0f), parent);
        }

        void DressStreets()
        {
            for (int i = 0; i < verticalRoadX.Length; i++)
                for (int j = 0; j + 1 < horizontalRoadZ.Length; j++)
                {
                    var a = _nodes[i, j];
                    var b = _nodes[i, j + 1];
                    var start = new Vector3(verticalRoadX[i], 0f, a.ZMax);
                    float len = b.ZMin - a.ZMax;
                    DressSide(start, Vector3.forward, len, Vector3.right, verticalIsBoulevard[i]);
                    DressSide(start, Vector3.forward, len, Vector3.left, verticalIsBoulevard[i]);
                }
            for (int j = 0; j < horizontalRoadZ.Length; j++)
                for (int i = 0; i + 1 < verticalRoadX.Length; i++)
                {
                    var a = _nodes[i, j];
                    var b = _nodes[i + 1, j];
                    var start = new Vector3(a.XMax, 0f, horizontalRoadZ[j]);
                    float len = b.XMin - a.XMax;
                    DressSide(start, Vector3.right, len, Vector3.forward, horizontalIsBoulevard[j]);
                    DressSide(start, Vector3.right, len, Vector3.back, horizontalIsBoulevard[j]);
                }

            PowerlinePass();
            CornerProps();
            ManholePass();
        }

        void DressSide(Vector3 start, Vector3 dir, float len, Vector3 outward, bool boulevard)
        {
            float half = boulevard ? BoulevardHalf : StreetHalf;
            float faceRoad = YawOf(-outward);
            float alongRoad = YawOf(dir);
            Vector3 At(float t, float lat) => start + dir * t + outward * lat + Vector3.up * 0.1f;

            if (!boulevard)
            {
                // kerb strip: palms in pavement grates, street lamps, saplings
                for (float t = 5f; t < len - 5f; t += 7f)
                {
                    var pos = At(t + Random.Range(-1.2f, 1.2f), half + 1.15f);
                    float r = Random.value;
                    if (r < 0.4f && _grates.Count > 0 && _palms.Count > 0)
                    {
                        Prop(Pick(_grates), pos, Random.Range(0, 4) * 90f, _geometry);
                        Prop(Pick(_palms), pos, Random.value * 360f, _flora);
                    }
                    else if (r < 0.6f && _lamps.Count > 0)
                        Prop(Pick(_lamps), pos, faceRoad, _geometry);
                    else if (r < 0.72f && _saplings.Count > 0)
                        Prop(Pick(_saplings), pos, Random.value * 360f, _flora);
                }
            }
            else
            {
                // boulevard: lamps on the kerb strip, benches set back near the palm
                // row (half+3.4) so they do not crowd the kerb; the palms out there
                // repeat every 20 m starting 10 m in, so benches dodge those spots
                int slot = 0;
                for (float t = 6f; t < len - 6f; t += 12.5f, slot++)
                {
                    if (slot % 2 == 0 && _lamps.Count > 0)
                    {
                        Prop(Pick(_lamps), At(t, half + 1.2f), faceRoad, _geometry);
                    }
                    else if (_benches.Count > 0)
                    {
                        float bt = t;
                        float nearestPalm = Mathf.Round((bt - 10f) / 20f) * 20f + 10f;
                        if (Mathf.Abs(bt - nearestPalm) < 2.5f)
                            bt = nearestPalm + (bt >= nearestPalm ? 3f : -3f);
                        Prop(Pick(_benches), At(bt, half + 3.4f), faceRoad, _geometry);
                        if (Random.value < 0.7f && _bins.Count > 0)
                            Prop(Pick(_bins), At(bt + 2.2f, half + 3.4f), faceRoad, _geometry);
                    }
                }
                return; // outer strip is the palm row; median carries the greenery
            }

            // outer strip of ordinary streets: furniture, junk and utilities
            for (float t = 4f; t < len - 4f; t += 9f)
            {
                var pos = At(t + Random.Range(-2f, 2f), half + 4.1f);
                float r = Random.value;
                if (r < 0.16f && _benches.Count > 0)
                {
                    Prop(Pick(_benches), pos, faceRoad, _geometry);
                    if (Random.value < 0.6f && _bins.Count > 0)
                        Prop(Pick(_bins), pos + dir * 2.1f, faceRoad, _geometry);
                }
                else if (r < 0.3f && _bins.Count > 0)
                {
                    Prop(Pick(_bins), pos, faceRoad + Random.Range(-25f, 25f), _geometry);
                    int bags = Random.Range(0, 3);
                    for (int k = 0; k < bags; k++)
                        Prop(Random.value < 0.7f ? _bag : _bagOpen,
                            pos + new Vector3(Random.Range(-0.9f, 0.9f), 0f, Random.Range(-0.9f, 0.9f)),
                            Random.value * 360f, _geometry);
                }
                else if (r < 0.45f)
                {
                    int bags = Random.Range(1, 4);
                    for (int k = 0; k < bags; k++)
                        Prop(Random.value < 0.7f ? _bag : _bagOpen,
                            pos + new Vector3(Random.Range(-1.1f, 1.1f), 0f, Random.Range(-1.1f, 1.1f)),
                            Random.value * 360f, _geometry);
                }
                else if (r < 0.55f && _planters.Count > 0)
                    Prop(Pick(_planters), pos, alongRoad, _flora);
                else if (r < 0.66f && _bushes.Count > 0)
                    Prop(Pick(_bushes), pos, Random.value * 360f, _flora);
                else if (r < 0.73f && _tables.Count > 0 && _chairs.Count > 0)
                {
                    // sidewalk cafe: table, a few chairs facing it, often an umbrella
                    Prop(Pick(_tables), pos, faceRoad + Random.Range(-20f, 20f), _geometry);
                    int chairs = Random.Range(2, 4);
                    for (int k = 0; k < chairs; k++)
                    {
                        float ang = Random.value * 360f;
                        var cpos = pos + Quaternion.Euler(0f, ang, 0f) * Vector3.forward * 1.15f;
                        Prop(Pick(_chairs), cpos, ang + 180f, _geometry);
                    }
                    if (Random.value < 0.6f && _umbrellas.Count > 0)
                        Prop(Pick(_umbrellas), pos, Random.value * 360f, _flora);
                }
                else if (r < 0.79f && _bikeStand != null)
                {
                    int hoops = Random.Range(2, 4);
                    for (int k = 0; k < hoops; k++)
                        Prop(_bikeStand, pos + dir * (k * 0.9f), alongRoad, _geometry);
                }
                else if (r < 0.85f && _hedges.Count > 0)
                {
                    Prop(Pick(_hedges), pos, alongRoad, _flora);
                    Prop(Pick(_hedges), pos + dir * 2.75f, alongRoad, _flora);
                }
                else if (r < 0.89f && _topiary.Count > 0)
                    Prop(Pick(_topiary), pos, Random.value * 360f, _flora);
                else if (r < 0.93f && _powerboxes.Count > 0)
                    Prop(Pick(_powerboxes), pos, faceRoad, _geometry);
                else if (r < 0.96f)
                    Prop(_mailbox, pos, faceRoad, _geometry);
                else
                    Prop(_newsstand, pos, faceRoad, _geometry);
            }
        }

        // manhole covers scattered over the carriageways
        void ManholePass()
        {
            if (_manhole == null) return;
            for (int i = 0; i < verticalRoadX.Length; i++)
                for (int j = 0; j + 1 < horizontalRoadZ.Length; j++)
                {
                    var a = _nodes[i, j];
                    var b = _nodes[i, j + 1];
                    int count = verticalIsBoulevard[i] ? 3 : Random.Range(1, 3);
                    for (int k = 0; k < count; k++)
                    {
                        float lat = verticalIsBoulevard[i]
                            ? (Random.value < 0.5f ? -1f : 1f) * Random.Range(6f, 13f)
                            : Random.Range(-3f, 3f);
                        Prop(_manhole, new Vector3(verticalRoadX[i] + lat, 0.02f,
                            Random.Range(a.ZMax + 4f, b.ZMin - 4f)), Random.value * 360f, _geometry);
                    }
                }
            for (int j = 0; j < horizontalRoadZ.Length; j++)
                for (int i = 0; i + 1 < verticalRoadX.Length; i++)
                {
                    var a = _nodes[i, j];
                    var b = _nodes[i + 1, j];
                    int count = horizontalIsBoulevard[j] ? 3 : Random.Range(1, 3);
                    for (int k = 0; k < count; k++)
                    {
                        float lat = horizontalIsBoulevard[j]
                            ? (Random.value < 0.5f ? -1f : 1f) * Random.Range(6f, 13f)
                            : Random.Range(-3f, 3f);
                        Prop(_manhole, new Vector3(Random.Range(a.XMax + 4f, b.XMin - 4f), 0.02f,
                            horizontalRoadZ[j] + lat), Random.value * 360f, _geometry);
                    }
                }
        }


        // One line of poles along each ordinary street (east / south side), wires
        // scaled to whatever span the pole spacing produces. Pole positions inside
        // intersection or zebra zones are skipped, so spans stretch across them.
        void PowerlinePass()
        {
            if (_powerpole == null || _wires.Count == 0) return;
            const float WireLen = 7.696f;
            const float WireY = 8.33f;
            float[] strand = { -0.85f, 0f, 0.85f };

            for (int i = 0; i < verticalRoadX.Length; i++)
            {
                if (verticalIsBoulevard[i]) continue;
                float x = verticalRoadX[i] + StreetHalf + 4.3f;
                var spots = PoleSpots(
                    _nodes[i, 0].ZMax + 2f, _nodes[i, horizontalRoadZ.Length - 1].ZMin - 2f,
                    z => InsideNodeZoneZ(i, z));
                foreach (var z in spots)
                    Prop(_powerpole, new Vector3(x, 0.1f, z), 0f, _geometry);
                for (int k = 0; k + 1 < spots.Count; k++)
                    foreach (float off in strand)
                    {
                        var wire = Instantiate(Pick(_wires),
                            new Vector3(x + off, WireY, spots[k]), Quaternion.identity, _geometry);
                        wire.transform.localScale = new Vector3(1f, 1f, (spots[k + 1] - spots[k]) / WireLen);
                    }
            }
            for (int j = 0; j < horizontalRoadZ.Length; j++)
            {
                if (horizontalIsBoulevard[j]) continue;
                float z = horizontalRoadZ[j] - StreetHalf - 4.3f;
                var spots = PoleSpots(
                    _nodes[0, j].XMax + 2f, _nodes[verticalRoadX.Length - 1, j].XMin - 2f,
                    x => InsideNodeZoneX(j, x));
                foreach (var x in spots)
                    Prop(_powerpole, new Vector3(x, 0.1f, z), 90f, _geometry);
                for (int k = 0; k + 1 < spots.Count; k++)
                    foreach (float off in strand)
                    {
                        var wire = Instantiate(Pick(_wires),
                            new Vector3(spots[k], WireY, z + off), Quaternion.Euler(0f, 90f, 0f), _geometry);
                        wire.transform.localScale = new Vector3(1f, 1f, (spots[k + 1] - spots[k]) / WireLen);
                    }
            }
        }

        List<float> PoleSpots(float from, float to, System.Func<float, bool> blocked)
        {
            var spots = new List<float>();
            float p = from;
            while (p < to)
            {
                if (blocked(p)) { p += 3f; continue; }
                spots.Add(p);
                p += 21f;
            }
            return spots;
        }

        bool InsideNodeZoneZ(int i, float z)
        {
            for (int j = 0; j < horizontalRoadZ.Length; j++)
                if (z > _nodes[i, j].ZMin - 7f && z < _nodes[i, j].ZMax + 7f) return true;
            return false;
        }

        bool InsideNodeZoneX(int j, float x)
        {
            for (int i = 0; i < verticalRoadX.Length; i++)
                if (x > _nodes[i, j].XMin - 7f && x < _nodes[i, j].XMax + 7f) return true;
            return false;
        }

        void CornerProps()
        {
            foreach (var n in _nodes)
                foreach (var (sx, sz) in new[] { (1f, 1f), (-1f, 1f), (-1f, -1f), (1f, -1f) })
                {
                    float bx = sx > 0f ? n.XMax : n.XMin;
                    float bz = sz > 0f ? n.ZMax : n.ZMin;
                    Vector3 C(float ox, float oz) => new Vector3(bx + sx * ox, 0.1f, bz + sz * oz);
                    float faceIn = YawOf(new Vector3(-sx, 0f, -sz));

                    if (Random.value < 0.7f && _bollard != null)
                    {
                        Prop(_bollard, C(0.9f, 2.6f), 0f, _geometry);
                        Prop(_bollard, C(2.6f, 0.9f), 0f, _geometry);
                    }
                    if (Random.value < 0.4f && _signPole != null)
                        Prop(_signPole, C(1.1f, 3.4f), faceIn + Random.Range(-10f, 10f), _geometry);
                    float r = Random.value;
                    if (r < 0.25f)
                        Prop(_hydrant, C(3.6f, 3.6f), Random.value * 360f, _geometry);
                    else if (r < 0.5f && _bins.Count > 0)
                        Prop(Pick(_bins), C(3.8f, 1.2f), faceIn, _geometry);
                    else if (r < 0.62f)
                        Prop(_newsstand, C(1.2f, 3.8f), faceIn, _geometry);
                    else if (r < 0.75f && _powerboxes.Count > 0)
                        Prop(Pick(_powerboxes), C(3.8f, 3.8f), faceIn, _geometry);
                }
        }

        // ------------------------------------------------------------------ graph

        static float[] LaneOffsets(bool boulevard)
            => boulevard ? new[] { 7.5f, 12.5f } : new[] { 2.5f };

        void AddEdge(RoadNode from, RoadNode to, Vector3 start, Vector3 end, bool ns, float limit)
        {
            var e = new RoadEdge
            {
                From = from, To = to, Start = start, End = end,
                Dir = (end - start).normalized,
                Length = (end - start).magnitude,
                NorthSouth = ns, SpeedLimit = limit,
            };
            from.Outgoing.Add(e);
            to.Incoming.Add(e);
            _edges.Add(e);
        }

        void BuildGraph()
        {
            for (int i = 0; i < verticalRoadX.Length; i++)
            {
                float cx = verticalRoadX[i];
                bool blvd = verticalIsBoulevard[i];
                float limit = blvd ? boulevardSpeed : streetSpeed;
                for (int j = 0; j + 1 < horizontalRoadZ.Length; j++)
                {
                    var a = _nodes[i, j];
                    var b = _nodes[i, j + 1];
                    foreach (float off in LaneOffsets(blvd))
                    {
                        AddEdge(a, b, new Vector3(cx + off, 0f, a.ZMax),
                            new Vector3(cx + off, 0f, b.ZMin), true, limit);   // northbound
                        AddEdge(b, a, new Vector3(cx - off, 0f, b.ZMin),
                            new Vector3(cx - off, 0f, a.ZMax), true, limit);   // southbound
                    }
                }
            }
            for (int j = 0; j < horizontalRoadZ.Length; j++)
            {
                float cz = horizontalRoadZ[j];
                bool blvd = horizontalIsBoulevard[j];
                float limit = blvd ? boulevardSpeed : streetSpeed;
                for (int i = 0; i + 1 < verticalRoadX.Length; i++)
                {
                    var a = _nodes[i, j];
                    var b = _nodes[i + 1, j];
                    foreach (float off in LaneOffsets(blvd))
                    {
                        AddEdge(a, b, new Vector3(a.XMax, 0f, cz - off),
                            new Vector3(b.XMin, 0f, cz - off), false, limit);  // eastbound
                        AddEdge(b, a, new Vector3(b.XMin, 0f, cz + off),
                            new Vector3(a.XMax, 0f, cz + off), false, limit);  // westbound
                    }
                }
            }
        }

        // ---------------------------------------------------------------- signals

        void BuildSignals()
        {
            _signalMats = new SignalMaterials();
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            Material Mk(float r, float g, float b)
                => new Material(unlit) { color = new Color(r, g, b) };
            _signalMats.RedOn = Mk(1f, 0.1f, 0.08f);
            _signalMats.RedOff = Mk(0.2f, 0.04f, 0.04f);
            _signalMats.YellowOn = Mk(1f, 0.8f, 0.12f);
            _signalMats.YellowOff = Mk(0.22f, 0.17f, 0.04f);
            _signalMats.GreenOn = Mk(0.2f, 1f, 0.3f);
            _signalMats.GreenOff = Mk(0.04f, 0.2f, 0.07f);

            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var housing = new Material(lit) { color = new Color(0.16f, 0.16f, 0.17f) };

            foreach (var n in _nodes)
            {
                var sig = new TrafficSignal(((n.I * 31 + n.J * 17) % 13) / 13f * TrafficSignal.Cycle);
                n.Signal = sig;
                _signals.Add(sig);

                var dirs = new HashSet<Vector3>();
                foreach (var e in n.Incoming) dirs.Add(e.Dir);
                foreach (var d in dirs) BuildApproachPole(n, d, sig, housing);
            }
        }

        void BuildApproachPole(RoadNode n, Vector3 d, TrafficSignal sig, Material housing)
        {
            float vh = VHalf(n.I), hh = HHalf(n.J);
            Vector3 pos;
            int yaw;
            if (d.z > 0.5f) { pos = new Vector3(n.X + vh + 1.6f, 0.1f, n.ZMin - 1.6f); yaw = 270; }
            else if (d.z < -0.5f) { pos = new Vector3(n.X - vh - 1.6f, 0.1f, n.ZMax + 1.6f); yaw = 90; }
            else if (d.x > 0.5f) { pos = new Vector3(n.XMin - 1.6f, 0.1f, n.Z - hh - 1.6f); yaw = 0; }
            else { pos = new Vector3(n.XMax + 1.6f, 0.1f, n.Z + hh + 1.6f); yaw = 180; }

            var rot = Quaternion.Euler(0f, yaw, 0f);
            var pole = new GameObject("Signal " + d).transform;
            pole.SetParent(_traffic, false);
            pole.SetPositionAndRotation(pos, rot);
            Instantiate(_poleBase, pole);
            Instantiate(_poleArm, pole);
            Instantiate(_poleLights, pole);

            // bulb head hanging from the arm, facing the approaching cars
            Vector3 armDir = rot * Vector3.forward;
            Vector3 face = -d;
            Vector3 head = pos + armDir * 4.6f + Vector3.up * 4.05f;

            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = "Head";
            box.transform.SetParent(pole, false);
            box.transform.SetPositionAndRotation(head, Quaternion.LookRotation(face));
            box.transform.localScale = new Vector3(0.55f, 1.55f, 0.22f);
            Destroy(box.GetComponent<Collider>());
            box.GetComponent<MeshRenderer>().sharedMaterial = housing;

            var set = new TrafficSignal.BulbSet { NorthSouth = Mathf.Abs(d.z) > 0.5f };
            for (int k = 0; k < 3; k++)
            {
                var bulb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                bulb.name = k == 0 ? "Red" : k == 1 ? "Yellow" : "Green";
                bulb.transform.SetParent(pole, false);
                bulb.transform.position = head + Vector3.up * (0.45f - 0.45f * k) + face * 0.16f;
                bulb.transform.localScale = Vector3.one * 0.34f;
                Destroy(bulb.GetComponent<Collider>());
                var r = bulb.GetComponent<MeshRenderer>();
                if (k == 0) set.R = r; else if (k == 1) set.Y = r; else set.G = r;
            }
            sig.AddBulbs(set);
        }

        // ------------------------------------------------------------- pedestrians

        PedNode[,,] _corners; // per intersection: NE, NW, SW, SE

        void AddPedLink(PedNode a, PedNode b, bool gated, bool blocksNS, TrafficSignal sig)
        {
            float len = (b.Pos - a.Pos).magnitude;
            var ab = new PedLink { From = a, To = b, Length = len, Gated = gated, BlocksNorthSouth = blocksNS, Signal = sig };
            var ba = new PedLink { From = b, To = a, Length = len, Gated = gated, BlocksNorthSouth = blocksNS, Signal = sig };
            a.Links.Add(ab);
            b.Links.Add(ba);
            _pedLinks.Add(ab);
            _pedLinks.Add(ba);
        }

        // A zebra between two corners; boulevards get a median refuge halfway so
        // each half is short enough to fit inside one red window. Where no road
        // continues past this side, the same span is just sidewalk (the cap band).
        void AddCrossing(PedNode a, PedNode b, bool roadExists, bool boulevard,
            bool blocksNS, Vector3 refugePos, TrafficSignal sig)
        {
            if (!roadExists)
            {
                AddPedLink(a, b, false, false, null);
            }
            else if (boulevard)
            {
                var refuge = new PedNode { Pos = refugePos };
                AddPedLink(a, refuge, true, blocksNS, sig);
                AddPedLink(refuge, b, true, blocksNS, sig);
            }
            else
            {
                AddPedLink(a, b, true, blocksNS, sig);
            }
        }

        void BuildPedGraph()
        {
            const int NE = 0, NW = 1, SW = 2, SE = 3;
            const float Off = 2.5f;   // middle of the corner slab
            const float WalkY = 0.1f; // sidewalk surface

            int nv = verticalRoadX.Length, nh = horizontalRoadZ.Length;
            _corners = new PedNode[nv, nh, 4];
            for (int i = 0; i < nv; i++)
                for (int j = 0; j < nh; j++)
                {
                    var n = _nodes[i, j];
                    _corners[i, j, NE] = new PedNode { Pos = new Vector3(n.XMax + Off, WalkY, n.ZMax + Off) };
                    _corners[i, j, NW] = new PedNode { Pos = new Vector3(n.XMin - Off, WalkY, n.ZMax + Off) };
                    _corners[i, j, SW] = new PedNode { Pos = new Vector3(n.XMin - Off, WalkY, n.ZMin - Off) };
                    _corners[i, j, SE] = new PedNode { Pos = new Vector3(n.XMax + Off, WalkY, n.ZMin - Off) };
                }

            for (int i = 0; i < nv; i++)
                for (int j = 0; j < nh; j++)
                {
                    var n = _nodes[i, j];
                    bool vBlvd = verticalIsBoulevard[i], hBlvd = horizontalIsBoulevard[j];
                    AddCrossing(_corners[i, j, NW], _corners[i, j, NE], j < nh - 1, vBlvd, true,
                        new Vector3(n.X, 0.02f, n.ZMax + Off), n.Signal);
                    AddCrossing(_corners[i, j, SW], _corners[i, j, SE], j > 0, vBlvd, true,
                        new Vector3(n.X, 0.02f, n.ZMin - Off), n.Signal);
                    AddCrossing(_corners[i, j, NE], _corners[i, j, SE], i < nv - 1, hBlvd, false,
                        new Vector3(n.XMax + Off, 0.02f, n.Z), n.Signal);
                    AddCrossing(_corners[i, j, NW], _corners[i, j, SW], i > 0, hBlvd, false,
                        new Vector3(n.XMin - Off, 0.02f, n.Z), n.Signal);
                }

            for (int i = 0; i < nv; i++)
                for (int j = 0; j + 1 < nh; j++)
                {
                    AddPedLink(_corners[i, j, NE], _corners[i, j + 1, SE], false, false, null);
                    AddPedLink(_corners[i, j, NW], _corners[i, j + 1, SW], false, false, null);
                }
            for (int j = 0; j < nh; j++)
                for (int i = 0; i + 1 < nv; i++)
                {
                    AddPedLink(_corners[i, j, NE], _corners[i + 1, j, NW], false, false, null);
                    AddPedLink(_corners[i, j, SE], _corners[i + 1, j, SW], false, false, null);
                }
        }

        void SpawnPedestrians()
        {
            if (_walkClip == null || _idleClip == null || _pedPrefabs.Count == 0) return;
            var root = new GameObject("People").transform;
            var sidewalks = _pedLinks.FindAll(l => !l.Gated);
            if (sidewalks.Count == 0) return;

            for (int k = 0; k < pedestrianCount; k++)
            {
                var link = sidewalks[Random.Range(0, sidewalks.Count)];
                var prefab = _pedPrefabs[Random.Range(0, _pedPrefabs.Count)];
                var go = Instantiate(prefab, root);
                foreach (var col in go.GetComponentsInChildren<Collider>()) Destroy(col);
                foreach (var rb in go.GetComponentsInChildren<Rigidbody>()) Destroy(rb);

                var agent = new PedestrianAgent { Speed = Random.Range(1.25f, 1.85f) };
                agent.Init(go.transform, _walkClip, _idleClip, link, Random.value * link.Length * 0.9f);
                _pedestrians.Add(agent);
            }
        }

        // ------------------------------------------------------------------- cars

        void SpawnCars()
        {
            int placed = 0;
            for (int round = 0; placed < carCount && round < 40; round++)
            {
                bool any = false;
                foreach (var e in _edges)
                {
                    if (placed >= carCount) break;
                    float s = 6f + round * 18f;
                    if (s > e.Length - 12f) continue;
                    any = true;

                    var prefab = _carPrefabs[Random.Range(0, _carPrefabs.Count)];
                    var go = Instantiate(prefab, Vector3.zero, Quaternion.identity, _cars);
                    foreach (var rb in go.GetComponentsInChildren<Rigidbody>()) Destroy(rb);
                    foreach (var col in go.GetComponentsInChildren<Collider>()) Destroy(col);

                    var bounds = new Bounds(go.transform.position, Vector3.zero);
                    foreach (var r in go.GetComponentsInChildren<Renderer>())
                        bounds.Encapsulate(r.bounds);

                    var v = new DemoVehicle { Tf = go.transform, HalfLen = bounds.extents.z + 0.3f };
                    v.Spawn(e, s);
                    _vehicles.Add(v);
                    placed++;
                }
                if (!any) break;
            }
        }

        // ------------------------------------------------------------ environment

        void BuildEnvironment()
        {
            float minX = verticalRoadX[0], maxX = verticalRoadX[verticalRoadX.Length - 1];
            float minZ = horizontalRoadZ[0], maxZ = horizontalRoadZ[horizontalRoadZ.Length - 1];
            var centre = new Vector3((minX + maxX) * 0.5f, 0f, (minZ + maxZ) * 0.5f);

            float sizeX = maxX - minX + 180f;
            float sizeZ = maxZ - minZ + 180f;
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.position = centre + Vector3.down * 0.06f;
            ground.transform.localScale = new Vector3(sizeX / 10f, 1f, sizeZ / 10f);

            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var sandMat = new Material(lit) { color = new Color(0.98f, 0.95f, 0.88f) };
#if UNITY_EDITOR
            var sandTex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/Synty/PolygonPalmCity/Textures/Terrain/Sand_01.png");
            if (sandTex != null)
            {
                sandMat.mainTexture = sandTex;
                sandMat.mainTextureScale = new Vector2(sizeX / 12f, sizeZ / 12f);
            }
            else
            {
                sandMat.color = new Color(0.76f, 0.72f, 0.58f);
            }
#endif
            sandMat.SetFloat("_Smoothness", 0.08f);
            ground.GetComponent<MeshRenderer>().sharedMaterial = sandMat;

            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.25f;
            sun.color = new Color(1f, 0.96f, 0.87f);
            sun.shadows = LightShadows.Soft;
            sunGo.transform.rotation = Quaternion.Euler(52f, 38f, 0f);

            var camGo = new GameObject("Demo Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 45f;
            cam.farClipPlane = 1600f;
            camGo.AddComponent<AudioListener>();
            var dc = camGo.AddComponent<DemoCamera>();
            dc.pivot = centre;
            dc.distance = 190f;
            dc.yaw = 33f;
            dc.pitch = 52f;
        }
    }
}
