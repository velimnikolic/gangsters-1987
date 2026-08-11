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
        // Spacing wraps a residentialblock1 bake per interior: 70 m (X) by 50 m (Z)
        // between the inner sidewalk edges, so the block sits flush with the kerbs.
        public float[] verticalRoadX = { 0f, 100f, 200f, 300f, 400f };
        public bool[] verticalIsBoulevard = { false, true, false, true, false };
        public float[] horizontalRoadZ = { 0f, 80f, 160f, 230f };
        public bool[] horizontalIsBoulevard = { false, true, false, false };

        [Header("Traffic")]
        public int carCount = 100;
        public float streetSpeed = 9f;
        public float boulevardSpeed = 13f;
        public int pedestrianCount = 170;

        [Header("Day/night")]
        [Tooltip("Real seconds per game hour. 15 runs a whole day in 6 minutes; " +
                 "the city's own default of 60 takes 24.")]
        public float realSecondsPerGameHour = 15f;
        [Tooltip("Hour the demo starts at. 16 puts dusk about 40 seconds in.")]
        [Range(0f, 24f)] public float startHour = 16f;

        [Header("Police patrol")]
        public int policeCarCount = 3;
        public int policeOfficerCount = 2;
        public Vector2 policeRestSeconds = new Vector2(6f, 16f);
        // waypoints per patrol, drawn across the whole map (cars) or the beat
        // radius (officers) - each one is a routed trip, not a wandered block
        public Vector2Int policePatrolWaypoints = new Vector2Int(2, 4);

        [Header("City life")]
        [Tooltip("Share of the crowd that starts indoors and streams out of the doors over the first minute.")]
        [Range(0f, 1f)] public float insideAtStart = 0.45f;
        [Tooltip("Chance to slip into a door being walked past; entering despawns until the stay ends.")]
        [Range(0f, 1f)] public float enterChance = 0.3f;
        [Range(0f, 1f)] public float sitChance = 0.45f;
        public Vector2 insideSeconds = new Vector2(10f, 45f);
        public Vector2 sitSeconds = new Vector2(10f, 35f);
        public Vector2 chatSeconds = new Vector2(6f, 14f);

        [Header("Organic layout")]
        [Tooltip("Share of ordinary-street segments the layout tries to close, so a street " +
                 "stops at a block instead of running the width of the map. Boulevards are " +
                 "never cut, and a cut is rolled back if it would leave a dead end or split " +
                 "the network - so the share that actually lands is a little under this.")]
        [Range(0f, 0.6f)] public float streetGapChance = 0.3f;

        [Tooltip("Which set of gaps gets drawn. Same seed, same street plan.")]
        public int layoutSeed = 7;

        const float Cell = 5f;
        const float StreetHalf = 5f;     // carriageway half width: 2 lanes
        const float BoulevardHalf = 15f; // 2+2 lanes plus a 10 m median

        const string BlockPrefabPath = "Assets/CityKit/Blocks/residentialblock1.prefab";
        const string BlocksDir = "Assets/CityKit/Blocks/";
        const string PoliceStationPath = "Assets/CityKit/Buildings/building-policestation.prefab";
        const string CityEnv = "Assets/Synty/PolygonCity/Prefabs/Environments/";
        const string CityProps = "Assets/Synty/PolygonCity/Prefabs/Props/";
        const string PalmEnv = "Assets/Synty/PolygonPalmCity/Prefabs/Environment/";
        const string PalmVeh = "Assets/Synty/PolygonPalmCity/Prefabs/Vehicles/";

        GameObject _roadWest, _roadEast;    // YellowLines halves of a two-way street
        GameObject _laneEdge, _laneDash;    // boulevard kerb lane / inner dashed lane
        GameObject _median, _bare, _crossing;
        GameObject _bareCracked, _roadPatch; // block-floor variation tiles
        GameObject _swStraight, _swCorner, _divider;
        GameObject _poleBase, _poleArm, _poleLights;
        readonly List<GameObject> _palms = new List<GameObject>();
        readonly List<GameObject> _carPrefabs = new List<GameObject>();
        readonly List<GameObject> _pedPrefabs = new List<GameObject>();
        AnimationClip _walkClip, _idleClip;
        AnimationClip _sitDownClip, _sitLoopClip, _standUpClip, _talkClip, _shoutClip;

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
        readonly List<GameObject> _leaves = new List<GameObject>();
        GameObject _grassClump;
        GameObject _bag, _bagOpen, _bollard, _hydrant, _mailbox, _newsstand, _powerpole;
        GameObject _bikeStand, _signPole, _manhole;
        GameObject _pave;              // PalmCity 2.5 m concrete plate, the demo's court floor
        bool _paveMeasured;
        Vector3 _paveSize, _paveOffset;
        float _paveTop;
        GameObject _policeCarPrefab;
        readonly List<GameObject> _officerPrefabs = new List<GameObject>();
        GameObject _policeStation;     // the packed station instance, found at placement
        bool _forecourtPlanned;
        Vector3 _stallCentre, _stallOut, _stallAlong;
        float _stallRowHalf, _stallLift;
        readonly List<PolicePatrolCar> _policeCars = new List<PolicePatrolCar>();
        readonly List<PoliceFootPatrol> _policeOfficers = new List<PoliceFootPatrol>();
        GameObject _blockPrefab;
        readonly List<GameObject> _featureBlocks = new List<GameObject>();
        readonly Dictionary<GameObject, Bounds> _prefabBoundsCache = new Dictionary<GameObject, Bounds>();

        // Which stretches of each road are actually built. A closed segment is not a
        // road at all - no carriageway, no lane graph, no crossings - and the strip
        // it would have taken becomes a court shared by the two block interiors
        // either side, which is what merges them into one bigger block.
        //   _vSeg[i, j] - vertical road i, between horizontal roads j and j+1
        //   _hSeg[j, i] - horizontal road j, between vertical roads i and i+1
        bool[,] _vSeg, _hSeg;
        string _layoutKey;   // what the cached plan was drawn for (the editor sketch)

        readonly HashSet<long> _cells = new HashSet<long>();
        RoadNode[,] _nodes;
        readonly List<RoadEdge> _edges = new List<RoadEdge>();
        readonly List<TrafficSignal> _signals = new List<TrafficSignal>();
        readonly List<DemoVehicle> _vehicles = new List<DemoVehicle>();
        readonly List<CivilianAgent> _pedestrians = new List<CivilianAgent>();
        readonly List<PedLink> _pedLinks = new List<PedLink>();
        SignalMaterials _signalMats;

        // street-life bookkeeping: facade doors and bench spots noted while the
        // geometry goes down, wired to the sidewalk graph once it exists
        readonly List<(Vector3 pos, Vector3 outward)> _pendingDoors =
            new List<(Vector3, Vector3)>();
        readonly List<(Vector3 pos, float yaw)> _pendingBenches =
            new List<(Vector3, float)>();
        CityLife _life;
        float _chatScan;

        Transform _geometry, _flora, _traffic, _cars, _blocks;

        void Awake()
        {
#if UNITY_EDITOR
            if (!LoadPrefabs()) return;
            _geometry = new GameObject("Geometry").transform;
            // palms live outside the static-batched root: their wind shader displaces
            // vertices in object space, and a combined mesh would swing them around
            // the batch pivot instead of their own trunks
            _flora = new GameObject("Flora").transform;
            // block bakes carry PalmCity palms/bushes whose wind shader displaces
            // vertices in object space — the root must stay out of static batching
            _blocks = new GameObject("Blocks").transform;
            _traffic = new GameObject("Traffic").transform;
            _cars = new GameObject("Cars").transform;

            EnsureLayout();
            BuildNodes();
            BuildRoadsAndSidewalks();
            BuildBlocks();
            DressStreets();
            BuildGraph();
            BuildSignals();
            BuildPedGraph();
            BuildCityLife();
            SpawnCars();
            SpawnPolice();
            SpawnPedestrians();
            BuildEnvironment();
            BuildDayNight();

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
            for (int i = 0; i < _policeCars.Count; i++) _policeCars[i].TickPatrol(dt);
            for (int i = 0; i < _pedestrians.Count; i++) _pedestrians[i].TickCivilian(dt);
            for (int i = 0; i < _policeOfficers.Count; i++) _policeOfficers[i].TickPatrol(dt);

            // two civilians meeting head-on may stop for a word; scanned on a
            // slow throttle, not per frame
            _chatScan -= dt;
            if (_chatScan <= 0f && _life != null && _life.CanChat)
            {
                _chatScan = 1.5f;
                CivilianAgent.PairChats(_pedestrians, chatSeconds);
            }
        }

        void OnDestroy()
        {
            for (int i = 0; i < _pedestrians.Count; i++) _pedestrians[i].Dispose();
            for (int i = 0; i < _policeOfficers.Count; i++) _policeOfficers[i].Dispose();
        }

        // Edit-mode sketch of the network: the real geometry only exists after
        // pressing Play, so the Scene view shows the planned layout instead.
        void OnDrawGizmos()
        {
            if (Application.isPlaying) return;
            if (verticalRoadX == null || horizontalRoadZ == null ||
                verticalRoadX.Length == 0 || horizontalRoadZ.Length == 0) return;

            int nv = verticalRoadX.Length, nh = horizontalRoadZ.Length;
            if (verticalIsBoulevard == null || verticalIsBoulevard.Length < nv ||
                horizontalIsBoulevard == null || horizontalIsBoulevard.Length < nh) return;

            EnsureLayout();

            var street = new Color(1f, 1f, 1f, 0.35f);
            var avenue = new Color(1f, 0.8f, 0.2f, 0.5f);
            var court = new Color(0.35f, 0.8f, 0.4f, 0.28f);   // what a closed segment becomes

            for (int i = 0; i < nv; i++)
                for (int j = 0; j < nh; j++)
                {
                    Gizmos.color = verticalIsBoulevard[i] || horizontalIsBoulevard[j] ? avenue : street;
                    Gizmos.DrawCube(new Vector3(verticalRoadX[i], 0f, horizontalRoadZ[j]),
                        new Vector3(VHalf(i) * 2f, 0.1f, HHalf(j) * 2f));
                }

            for (int i = 0; i < nv; i++)
                for (int j = 0; j + 1 < nh; j++)
                {
                    float a = horizontalRoadZ[j] + HHalf(j), b = horizontalRoadZ[j + 1] - HHalf(j + 1);
                    Gizmos.color = !_vSeg[i, j] ? court
                        : verticalIsBoulevard[i] ? avenue : street;
                    Gizmos.DrawCube(new Vector3(verticalRoadX[i], 0f, (a + b) * 0.5f),
                        new Vector3(VHalf(i) * 2f, 0.1f, b - a));
                }

            for (int j = 0; j < nh; j++)
                for (int i = 0; i + 1 < nv; i++)
                {
                    float a = verticalRoadX[i] + VHalf(i), b = verticalRoadX[i + 1] - VHalf(i + 1);
                    Gizmos.color = !_hSeg[j, i] ? court
                        : horizontalIsBoulevard[j] ? avenue : street;
                    Gizmos.DrawCube(new Vector3((a + b) * 0.5f, 0f, horizontalRoadZ[j]),
                        new Vector3(b - a, 0.1f, HHalf(j) * 2f));
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
            _bareCracked = Load(CityEnv + "SM_Env_Road_03.prefab");
            _roadPatch = Load(CityEnv + "SM_Env_Road_Patch_01.prefab");
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

            // the patrol fleet gets the marked cruiser to itself; civilian traffic
            // should not be driving black-and-whites around
            _carPrefabs.RemoveAll(p => p.name.ToLowerInvariant().Contains("police"));
            _policeCarPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Synty/PolygonCity/Prefabs/Vehicles/SM_Veh_Car_Police_01.prefab");
            if (_policeCarPrefab == null)
                Debug.LogWarning("[RoadDemo] SM_Veh_Car_Police_01 missing; police patrol disabled");

            // the patrol overlay dresses itself out of DemoUi - the demo's one
            // wardrobe, so its dot and popup match the top bar and the ledger

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
                // uniformed officers walk the beat for the station, not the crowd
                if (System.IO.Path.GetFileName(path).StartsWith("SM_Chr_Officer"))
                    _officerPrefabs.Add(chr);
                else
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
            // only the models StreetLampLights knows the bulb points of - a Lamp_02
            // would stand dark all night while its neighbours burn
            Bag(_lamps, "SM_Prop_Street_Lamp_01", "SM_Prop_Street_Lamp_08");
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
            Bag(_leaves, "SM_Env_Leaves_01", "SM_Env_Leaves_02");
            _grassClump = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                PalmEnv + "SM_Env_Grass_Clump_01.prefab");
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
            _pave = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PalmEnv + "SM_Env_Sidewalk_01.prefab");
            if (_pave == null) Debug.LogWarning("[RoadDemo] SM_Env_Sidewalk_01 missing; courts fall back to asphalt");

            _blockPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(BlockPrefabPath);
            if (_blockPrefab == null)
                Debug.LogWarning("[RoadDemo] block bake missing (" + BlockPrefabPath + "); interiors stay empty");

            // feature interiors: the auto-extracted palm block bakes plus the
            // police station from the building catalog
            for (int i = 2; i <= 8; i++)
            {
                var block = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                    BlocksDir + "PalmBlock_0" + i + ".prefab");
                if (block != null) _featureBlocks.Add(block);
                else Debug.LogWarning("[RoadDemo] missing block bake: PalmBlock_0" + i);
            }
            var police = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PoliceStationPath);
            if (police != null) _featureBlocks.Add(police);
            else Debug.LogWarning("[RoadDemo] missing prefab: " + PoliceStationPath);

            AnimationClip PeopleClip(string name) =>
                UnityEditor.AssetDatabase.LoadAssetAtPath<AnimationClip>(
                    "Assets/Animations/People/" + name + ".anim");
            _walkClip = PeopleClip("Standard Walk");
            _idleClip = PeopleClip("Breathing Idle");
            if (_walkClip == null || _idleClip == null || _pedPrefabs.Count == 0)
                Debug.LogWarning("[RoadDemo] pedestrian assets missing; spawning without people");

            // street life: the sit chain (down / loop / up) and the chat loops.
            // All optional - a missing clip just switches that behaviour off.
            _sitDownClip = PeopleClip("Idle-Sitting_Bench");
            _sitLoopClip = PeopleClip("Sitting_Bench_Idle");
            _standUpClip = PeopleClip("Sitting-Idle");
            _talkClip = PeopleClip("Standing_Talking");
            _shoutClip = PeopleClip("Standing_Shouting");
            if (_sitDownClip == null || _sitLoopClip == null || _standUpClip == null)
                Debug.LogWarning("[RoadDemo] bench sit clips missing; nobody will sit down");
            if (_talkClip == null)
                Debug.LogWarning("[RoadDemo] Standing_Talking missing; nobody will stop to chat");

            return _roadWest && _roadEast && _laneEdge && _laneDash && _median && _bare &&
                   _crossing && _swStraight && _swCorner && _divider &&
                   _poleBase && _poleArm && _poleLights && _carPrefabs.Count > 0;
        }
#endif

        // ------------------------------------------------------------ layout plan

        bool NorthOpen(int i, int j) => j + 1 < horizontalRoadZ.Length && _vSeg[i, j];
        bool SouthOpen(int i, int j) => j > 0 && _vSeg[i, j - 1];
        bool EastOpen(int i, int j) => i + 1 < verticalRoadX.Length && _hSeg[j, i];
        bool WestOpen(int i, int j) => i > 0 && _hSeg[j, i - 1];

        /// <summary>How many roads still meet at this junction: 4 is a crossroads,
        /// 3 a T, 2 a bend or a through-stretch.</summary>
        int Degree(int i, int j)
        {
            int d = 0;
            if (NorthOpen(i, j)) d++;
            if (SouthOpen(i, j)) d++;
            if (EastOpen(i, j)) d++;
            if (WestOpen(i, j)) d++;
            return d;
        }

        /// Rebuilds the plan only when what it was drawn for has changed - the edit
        /// mode sketch asks for it every frame.
        void EnsureLayout()
        {
            string key = verticalRoadX.Length + "x" + horizontalRoadZ.Length +
                         ":" + layoutSeed + ":" + streetGapChance;
            if (_vSeg != null && _layoutKey == key) return;
            PlanLayout();
            _layoutKey = key;
        }

        // Draws the segment mask - which is the whole of "streets go around blocks
        // rather than through them". Two rules keep the result drivable and both
        // are hard:
        //
        //   No junction may drop below TWO open segments. DemoVehicle forbids
        //   U-turns, so a dead end is not a cul-de-sac, it is a car that reaches
        //   the end of its lane with nowhere to go and drives on into open ground.
        //
        //   The network must stay in one piece, or the cars that spawned in a
        //   severed corner circle it for the rest of the session while the rest of
        //   the city stands empty.
        //
        // Cuts are drawn in a shuffled order and each is rolled back if it breaks
        // either rule, so streetGapChance is an upper bound, not a quota - a dense
        // grid grants most of it, a sparse one almost none.
        void PlanLayout()
        {
            int nv = verticalRoadX.Length, nh = horizontalRoadZ.Length;
            _vSeg = new bool[nv, Mathf.Max(1, nh - 1)];
            _hSeg = new bool[nh, Mathf.Max(1, nv - 1)];
            for (int i = 0; i < nv; i++)
                for (int j = 0; j + 1 < nh; j++) _vSeg[i, j] = true;
            for (int j = 0; j < nh; j++)
                for (int i = 0; i + 1 < nv; i++) _hSeg[j, i] = true;

            // its own generator rather than UnityEngine.Random: the street plan must
            // not shift because some later pass drew one more bush
            var rng = new System.Random(layoutSeed);

            // boulevards are the arteries and stay whole - cutting one strands half
            // the through traffic and reads as a missing road, not as a block
            var cuts = new List<(bool vertical, int road, int seg)>();
            for (int i = 0; i < nv; i++)
                if (!verticalIsBoulevard[i])
                    for (int j = 0; j + 1 < nh; j++) cuts.Add((true, i, j));
            for (int j = 0; j < nh; j++)
                if (!horizontalIsBoulevard[j])
                    for (int i = 0; i + 1 < nv; i++) cuts.Add((false, j, i));

            for (int k = cuts.Count - 1; k > 0; k--)
            {
                int swap = rng.Next(k + 1);
                (cuts[k], cuts[swap]) = (cuts[swap], cuts[k]);
            }

            int want = Mathf.RoundToInt(cuts.Count * Mathf.Clamp01(streetGapChance));
            int closed = 0;
            foreach (var cut in cuts)
            {
                if (closed >= want) break;

                if (cut.vertical) _vSeg[cut.road, cut.seg] = false;
                else _hSeg[cut.road, cut.seg] = false;

                if (LayoutHolds())
                {
                    closed++;
                }
                else if (cut.vertical) _vSeg[cut.road, cut.seg] = true;
                else _hSeg[cut.road, cut.seg] = true;
            }

            // Only in Play: the edit-mode sketch re-plans on every slider tick, and
            // one line per tick buries the console.
            if (Application.isPlaying)
                Debug.Log($"[RoadDemo] street plan: {closed} of {cuts.Count} segments closed " +
                          $"(asked for {want}), seed {layoutSeed}.");
        }

        bool LayoutHolds()
        {
            int nv = verticalRoadX.Length, nh = horizontalRoadZ.Length;

            for (int i = 0; i < nv; i++)
                for (int j = 0; j < nh; j++)
                    if (Degree(i, j) < 2) return false;

            var seen = new bool[nv, nh];
            var queue = new Queue<Vector2Int>();
            int reached = 1;
            seen[0, 0] = true;
            queue.Enqueue(new Vector2Int(0, 0));

            void Visit(int i, int j)
            {
                if (seen[i, j]) return;
                seen[i, j] = true;
                reached++;
                queue.Enqueue(new Vector2Int(i, j));
            }

            while (queue.Count > 0)
            {
                var p = queue.Dequeue();
                if (NorthOpen(p.x, p.y)) Visit(p.x, p.y + 1);
                if (SouthOpen(p.x, p.y)) Visit(p.x, p.y - 1);
                if (EastOpen(p.x, p.y)) Visit(p.x + 1, p.y);
                if (WestOpen(p.x, p.y)) Visit(p.x - 1, p.y);
            }

            return reached == nv * nh;
        }

        // ------------------------------------------------------------------ layout

        float VHalf(int i) => verticalIsBoulevard[i] ? BoulevardHalf : StreetHalf;
        float HHalf(int j) => horizontalIsBoulevard[j] ? BoulevardHalf : StreetHalf;

        static long CellKey(float mx, float mz)
            => ((long)Mathf.RoundToInt(mx / Cell) << 32) ^ (uint)Mathf.RoundToInt(mz / Cell);

        void PlaceCell(GameObject prefab, float mx, float mz, int yaw, float y = 0f)
        {
            Vector3 pivot;
            switch (yaw)
            {
                case 0: pivot = new Vector3(mx + Cell, y, mz + Cell); break;
                case 90: pivot = new Vector3(mx + Cell, y, mz); break;
                case 180: pivot = new Vector3(mx, y, mz); break;
                default: pivot = new Vector3(mx, y, mz + Cell); break;
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
            // "has" means a road actually continues that way - the map edge and a
            // closed segment read the same here, and both get the sidewalk cap that
            // turns this junction into a T or a bend
            bool north = NorthOpen(n.I, n.J);
            bool south = SouthOpen(n.I, n.J);
            bool east = EastOpen(n.I, n.J);
            bool west = WestOpen(n.I, n.J);
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
                    if (_vSeg[i, j])
                        FillVerticalSegment(i, _nodes[i, j], _nodes[i, j + 1]);

            for (int j = 0; j < horizontalRoadZ.Length; j++)
                for (int i = 0; i + 1 < verticalRoadX.Length; i++)
                    if (_hSeg[j, i])
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

        // --------------------------------------------------------- block interiors

        // The residentialblock1 bake's pivot is the midpoint of its two cluster
        // pivots, not the footprint centre: the building AABB (measured from the
        // bake) spans X[-36.15, 34.16], Z[-30.11, 20.34], so its centre sits at
        // this offset from the pivot and placement has to compensate.
        static readonly Vector3 BlockPivotToCentre = new Vector3(-0.993f, 0f, -4.882f);

        const float BlockAlley = 6f; // service gap between two packed blocks

        float _floorLevel = -1f;

        // Interior floors run flush with the street sidewalk's top surface, the
        // way the PalmCity demo's blocks continue at pavement level — a court
        // left at carriageway height reads as a sunken pit behind the kerb.
        float FloorLevel()
        {
            if (_floorLevel < 0f) _floorLevel = PrefabBoundsOf(_swStraight).max.y;
            return _floorLevel;
        }

        void BuildBlocks()
        {
            int slot = 0, feature = 0;
            for (int i = 0; i + 1 < verticalRoadX.Length; i++)
                for (int j = 0; j + 1 < horizontalRoadZ.Length; j++)
                {
                    float xMin = verticalRoadX[i] + VHalf(i) + Cell;
                    float xMax = verticalRoadX[i + 1] - VHalf(i + 1) - Cell;
                    float zMin = horizontalRoadZ[j] + HHalf(j) + Cell;
                    float zMax = horizontalRoadZ[j + 1] - HHalf(j + 1) - Cell;

                    var centre = new Vector3((xMin + xMax) * 0.5f, FloorLevel() + 0.02f, (zMin + zMax) * 0.5f);

                    // every third interior keeps the residential bake, so each
                    // feature block (PalmBlock_02..08, police station) stands in
                    // the same column as a residentialblock1
                    bool residential = slot % 3 == 0 || feature >= _featureBlocks.Count;
                    slot++;
                    if (residential)
                    {
                        BuildBlockFloor(xMin, xMax, zMin, zMax, null, false);
                        if (_blockPrefab == null) continue;

                        // both facade rows front the E-W streets, so a half-turn is a
                        // valid orientation — alternate it to break up the cloning
                        float yaw = (i + j) % 2 == 0 ? 0f : 180f;
                        var rot = Quaternion.Euler(0f, yaw, 0f);
                        var block = Instantiate(_blockPrefab, centre - rot * BlockPivotToCentre, rot, _blocks);

                        // street doors for the crowd: the bake's two terrace rows
                        // front the E-W streets, so the north and south faces of
                        // its AABB are facade planes - two doorways per row keeps
                        // people coming and going along the whole frontage
                        var bb = BoundsOf(block);
                        foreach (float fx in new[] { 0.3f, 0.7f })
                        {
                            float dx = Mathf.Lerp(bb.min.x, bb.max.x, fx);
                            _pendingDoors.Add((new Vector3(dx, centre.y, bb.min.z), Vector3.back));
                            _pendingDoors.Add((new Vector3(dx, centre.y, bb.max.z), Vector3.forward));
                        }
                    }
                    else
                    {
                        // blocks first: a bake that digs below street level (the
                        // skatepark bowl reaches -2 m) must get NO floor beneath it.
                        // Courts follow the PalmCity demo's own floor: concrete
                        // plate carpets, with the worn-asphalt lot kept as an
                        // occasional variant — and forced whenever the next bake
                        // digs below ground, since plates cannot ring a bowl.
                        bool digs = feature < _featureBlocks.Count &&
                            PrefabBoundsOf(_featureBlocks[feature]).min.y < -0.2f;
                        bool paved = !digs && _pave != null && Random.value < 0.8f;
                        float floorTop = FloorLevel();
                        bool northRow = (i + j) % 2 == 1;
                        var holes = new List<Rect>();
                        var occupied = PackFeatureBlocks(ref feature, xMin, xMax, zMin, zMax,
                            holes, floorTop, paved, northRow);
                        // the station's forecourt driveway must stay clear of
                        // props and lawns - reserve it first
                        if (_policeStation != null && !_forecourtPlanned)
                            occupied.Add(PlanForecourt(xMin, xMax, zMin, zMax, floorTop));
                        BuildBlockFloor(xMin, xMax, zMin, zMax, holes, paved);
                        FillCourtyard(xMin, xMax, zMin, zMax, occupied, floorTop);
                    }
                }

            BuildGapCourts();
        }

        // Where a segment was closed, the strip the street would have taken - both
        // sidewalks and the carriageway between them - is left bare ground by every
        // pass above, because they all skip a closed segment. It belongs to the
        // blocks either side now, so it gets the same floor and the same courtyard
        // dressing they do: the two interiors read as one big block with a garden
        // down the middle, which is the whole point of cutting the street.
        void BuildGapCourts()
        {
            int nv = verticalRoadX.Length, nh = horizontalRoadZ.Length;

            for (int i = 0; i < nv; i++)
                for (int j = 0; j + 1 < nh; j++)
                {
                    if (_vSeg[i, j]) continue;
                    GapCourt(verticalRoadX[i] - VHalf(i) - Cell,
                             verticalRoadX[i] + VHalf(i) + Cell,
                             horizontalRoadZ[j] + HHalf(j) + Cell,
                             horizontalRoadZ[j + 1] - HHalf(j + 1) - Cell, true);
                }

            for (int j = 0; j < nh; j++)
                for (int i = 0; i + 1 < nv; i++)
                {
                    if (_hSeg[j, i]) continue;
                    GapCourt(verticalRoadX[i] + VHalf(i) + Cell,
                             verticalRoadX[i + 1] - VHalf(i + 1) - Cell,
                             horizontalRoadZ[j] - HHalf(j) - Cell,
                             horizontalRoadZ[j] + HHalf(j) + Cell, false);
                }
        }

        /// Half width kept clear either side of a footpath's centre line.
        const float GapPathHalf = 1.9f;

        void GapCourt(float xMin, float xMax, float zMin, float zMax, bool vertical)
        {
            if (xMax - xMin < Cell || zMax - zMin < Cell) return;

            // The two footpaths sit where the sidewalks did - the middle of the
            // corner slab at either end, which is exactly the line BuildPedGraph
            // links along. Handed to FillCourtyard as occupied ground so nothing
            // gets planted in the walking lane.
            var paths = new List<Rect>();
            if (vertical)
            {
                float d = zMax - zMin;
                paths.Add(new Rect(xMin + Cell * 0.5f - GapPathHalf, zMin, GapPathHalf * 2f, d));
                paths.Add(new Rect(xMax - Cell * 0.5f - GapPathHalf, zMin, GapPathHalf * 2f, d));
            }
            else
            {
                float w = xMax - xMin;
                paths.Add(new Rect(xMin, zMin + Cell * 0.5f - GapPathHalf, w, GapPathHalf * 2f));
                paths.Add(new Rect(xMin, zMax - Cell * 0.5f - GapPathHalf, w, GapPathHalf * 2f));
            }

            bool paved = _pave != null && Random.value < 0.65f;
            BuildBlockFloor(xMin, xMax, zMin, zMax, null, paved);
            FillCourtyard(xMin, xMax, zMin, zMax, paths, FloorLevel());
        }

        // Frontage dictation: extra yaw that turns a feature bake's entrance row
        // to face world -Z. The bakes keep their PalmCity-demo orientation baked
        // into the mesh, so which way the doors point cannot be computed — it is
        // read off the scene per block, and a correction lands here by name.
        static readonly Dictionary<string, float> FrontageYaw = new Dictionary<string, float>();

        static float FrontageOf(string name)
            => FrontageYaw.TryGetValue(name, out var y) ? y : 0f;

        // Feature bakes carry varying pivots and footprints, so one bake rarely
        // fills the 70x50 interior on its own. Pack a row instead: measure each
        // candidate's renderer AABB and lay blocks west to east along the street
        // frontage with a service alley between them until the row is full.
        // Every interior edge borders a street, and rows alternate between the
        // south and north frontage per interior; each bake is turned so its
        // entrance row (FrontageYaw) faces the street it is packed against.
        // A quarter turn is used only when it saves the fit AND the doors still
        // land on a kerb — flush against the west side street at the row start,
        // or the east one as the row's closer. A block that no longer fits the
        // remaining width stays in the pool for the next feature interior.
        // Footprints of blocks whose geometry dips below street level
        // (the skatepark bowl) are reported via holes so the floor pass leaves
        // the ground under them open.
        List<Rect> PackFeatureBlocks(ref int feature, float xMin, float xMax, float zMin, float zMax,
            List<Rect> holes, float floorTop, bool paved, bool northRow)
        {
            float depth = zMax - zMin;
            var rects = new List<Rect>();
            var placed = new List<GameObject>();
            var sunken = new List<int>();
            float cursor = xMin;

            while (feature < _featureBlocks.Count)
            {
                float remaining = xMax - cursor;
                if (remaining < 15f) break;

                var prefab = _featureBlocks[feature];
                float baseYaw = FrontageOf(prefab.name);
                float yaw = baseYaw + (northRow ? 180f : 0f);
                var go = Instantiate(prefab, Vector3.zero, Quaternion.Euler(0f, yaw, 0f), _blocks);
                var b = BoundsOf(go);
                bool eastEnd = false;
                Vector3 doorOut = northRow ? Vector3.forward : Vector3.back;
                if (b.size.x > remaining || b.size.z > depth)
                {
                    // a quarter turn saves the fit WITHOUT taking the doors off a
                    // kerb only because the side edges are streets too: the turned
                    // block stands flush against the west side street at the row
                    // start (doors west), or closes the row against the east one
                    // (doors east)
                    bool startOfRow = cursor <= xMin + 0.01f;
                    if (b.size.z <= remaining && b.size.x <= depth)
                    {
                        eastEnd = !startOfRow;
                        yaw = baseYaw + (eastEnd ? 270f : 90f);
                        doorOut = eastEnd ? Vector3.right : Vector3.left;
                        go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                        b = BoundsOf(go);
                    }
                    else if (placed.Count > 0) { Destroy(go); break; }
                    else Debug.LogWarning("[RoadDemo] " + prefab.name + " overflows its interior ("
                        + b.size.x.ToString("F0") + "x" + b.size.z.ToString("F0")
                        + " m into " + (xMax - xMin) + "x" + depth + " m)");
                }

                // flush against the street frontage: footprint edge on the interior
                // boundary so the bake meets the kerb instead of floating mid-lot
                float xStart = eastEnd ? xMax - b.size.x : cursor;
                float zStart = northRow ? zMax - b.size.z : zMin;
                var target = new Vector3(xStart + b.size.x * 0.5f, 0f, zStart + b.size.z * 0.5f);
                var shift = target - b.center;
                // a bake that digs below ground (the skatepark bowl) can never
                // stand on a plate court — the carpet cannot ring a bowl — so on
                // a paved court it goes back in the pool for the next, asphalt
                // interior; there its floor hole keeps the ground open below.
                bool digs = b.min.y < -0.2f;
                if (digs && paved) { Destroy(go); break; }
                go.transform.position += new Vector3(shift.x, floorTop + 0.02f, shift.z);
                if (digs) sunken.Add(rects.Count);
                rects.Add(new Rect(xStart, zStart, b.size.x, b.size.z));
                placed.Add(go);
                // PalmBlock_07 bakes in the Synty ferris wheel as dead geometry;
                // its rotate pivot gets the demo's own spin
                foreach (var t in go.GetComponentsInChildren<Transform>())
                    if (t.name.Contains("Ferris") && t.name.Contains("_Rotate"))
                        t.gameObject.AddComponent<DemoFerrisWheel>();
                if (prefab.name.StartsWith("building-policestation")) _policeStation = go;
                // a civilian door on whichever face fronts this bake's street.
                // The station keeps its own door (the beat officers'), and a
                // sunken bake (skatepark) has no facade on the frontage line.
                else if (!digs)
                {
                    float doorY = floorTop + 0.02f;
                    Vector3 doorPos;
                    if (doorOut == Vector3.back)
                        doorPos = new Vector3(xStart + b.size.x * 0.5f, doorY, zStart);
                    else if (doorOut == Vector3.forward)
                        doorPos = new Vector3(xStart + b.size.x * 0.5f, doorY, zStart + b.size.z);
                    else if (doorOut == Vector3.left)
                        doorPos = new Vector3(xStart, doorY, zStart + b.size.z * 0.5f);
                    else
                        doorPos = new Vector3(xStart + b.size.x, doorY, zStart + b.size.z * 0.5f);
                    _pendingDoors.Add((doorPos, doorOut));
                }
                cursor = eastEnd ? xMax : cursor + b.size.x + BlockAlley;
                feature++;
            }

            foreach (int k in sunken) holes.Add(rects[k]);
            return rects;
        }

        // Renderer AABB of a prefab measured at the origin (so min.y is relative
        // to the pivot); instantiated once, cached, destroyed before render.
        Bounds PrefabBoundsOf(GameObject prefab)
        {
            if (!_prefabBoundsCache.TryGetValue(prefab, out var b))
            {
                var tmp = Instantiate(prefab, Vector3.zero, Quaternion.identity);
                b = BoundsOf(tmp);
                _prefabBoundsCache[prefab] = b;
                Destroy(tmp);
            }
            return b;
        }

        void EnsurePaveMeasured()
        {
            if (_paveMeasured || _pave == null) return;
            var b = PrefabBoundsOf(_pave);
            _paveSize = b.size;
            _paveOffset = b.center;
            _paveTop = b.max.y;
            _paveMeasured = true;
        }


        Material _lawnMat;

        // Leftover asphalt around the packed blocks gets the PalmCity courtyard
        // vocabulary — lawn rectangles first (flat Synty-green quads a hair above
        // the pad), then palms in pavement grates, bushes, hedges, planters,
        // benches, leaf piles and grass clumps — so a part-filled interior reads
        // as a pocket court, not a vacant lot.
        void FillCourtyard(float xMin, float xMax, float zMin, float zMax, List<Rect> occupied,
            float floorTop)
        {
            bool Free(float x, float z)
            {
                foreach (var r in occupied)
                    if (x > r.xMin - 2f && x < r.xMax + 2f && z > r.yMin - 2f && z < r.yMax + 2f)
                        return false;
                return true;
            }

            if (_lawnMat == null)
            {
                _lawnMat = new Material(Shader.Find("Universal Render Pipeline/Lit"))
                    { color = new Color(0.52f, 0.66f, 0.32f) };
                _lawnMat.SetFloat("_Smoothness", 0.05f);
            }
            var lawns = new List<Rect>();
            int wanted = Random.Range(1, 3);
            for (int attempt = 0; attempt < 12 && lawns.Count < wanted; attempt++)
            {
                float lw = Random.Range(10f, 18f), ld = Random.Range(8f, 14f);
                if (xMax - xMin < lw + 4f || zMax - zMin < ld + 4f) break;
                float lx = Random.Range(xMin + 2f, xMax - 2f - lw);
                float lz = Random.Range(zMin + 2f, zMax - 2f - ld);
                if (!Free(lx, lz) || !Free(lx + lw, lz) || !Free(lx, lz + ld) ||
                    !Free(lx + lw, lz + ld) || !Free(lx + lw * 0.5f, lz + ld * 0.5f))
                    continue;
                bool clash = false;
                foreach (var other in lawns)
                    if (lx < other.xMax + 3f && lx + lw > other.xMin - 3f &&
                        lz < other.yMax + 3f && lz + ld > other.yMin - 3f) { clash = true; break; }
                if (clash) continue;

                var lawn = GameObject.CreatePrimitive(PrimitiveType.Plane);
                lawn.name = "Lawn";
                Destroy(lawn.GetComponent<Collider>());
                lawn.transform.SetParent(_geometry, false);
                lawn.transform.position = new Vector3(lx + lw * 0.5f, floorTop + 0.012f, lz + ld * 0.5f);
                lawn.transform.localScale = new Vector3(lw / 10f, 1f, ld / 10f);
                lawn.GetComponent<MeshRenderer>().sharedMaterial = _lawnMat;
                lawns.Add(new Rect(lx, lz, lw, ld));
            }

            // the demo tiles its lawns with grass-clump carpets, not bare colour
            if (_grassClump != null)
                foreach (var l in lawns)
                    for (float gx = l.xMin + 0.9f; gx < l.xMax - 0.5f; gx += 1.8f)
                        for (float gz = l.yMin + 0.9f; gz < l.yMax - 0.5f; gz += 1.8f)
                            Prop(_grassClump,
                                new Vector3(gx + Random.Range(-0.5f, 0.5f), floorTop + 0.015f,
                                    gz + Random.Range(-0.5f, 0.5f)),
                                Random.value * 360f, _flora);

            bool OnLawn(float x, float z)
            {
                foreach (var l in lawns)
                    if (x > l.xMin + 0.7f && x < l.xMax - 0.7f && z > l.yMin + 0.7f && z < l.yMax - 0.7f)
                        return true;
                return false;
            }

            for (float x = xMin + 3f; x < xMax - 2.5f; x += 6f)
                for (float z = zMin + 3f; z < zMax - 2.5f; z += 6f)
                {
                    float px = x + Random.Range(-1.4f, 1.4f);
                    float pz = z + Random.Range(-1.4f, 1.4f);
                    if (!Free(px, pz)) continue;
                    var pos = new Vector3(px, floorTop + 0.02f, pz);

                    // lawns carry greenery only: palms straight out of the grass
                    // and bushes — the clump carpet is already down, and no street
                    // furniture stands on turf
                    if (OnLawn(px, pz))
                    {
                        float g = Random.value;
                        if (g < 0.35f && _palms.Count > 0)
                            Prop(Pick(_palms), pos, Random.value * 360f, _flora);
                        else if (g < 0.65f && _bushes.Count > 0)
                            Prop(Pick(_bushes), pos, Random.value * 360f, _flora);
                        continue;
                    }

                    float r = Random.value;
                    if (r < 0.18f && _grates.Count > 0 && _palms.Count > 0)
                    {
                        Prop(Pick(_grates), pos, Random.Range(0, 4) * 90f, _geometry);
                        Prop(Pick(_palms), pos, Random.value * 360f, _flora);
                    }
                    else if (r < 0.32f && _bushes.Count > 0)
                        Prop(Pick(_bushes), pos, Random.value * 360f, _flora);
                    else if (r < 0.44f && _hedges.Count > 0)
                    {
                        Prop(Pick(_hedges), pos, 0f, _flora);
                        Prop(Pick(_hedges), pos + Vector3.right * 2.75f, 0f, _flora);
                    }
                    else if (r < 0.54f && _planters.Count > 0)
                        Prop(Pick(_planters), pos, Random.Range(0, 2) * 90f, _flora);
                    else if (r < 0.62f && _benches.Count > 0)
                        PlaceBench(pos, Random.Range(0, 4) * 90f);
                    else if (r < 0.7f && _saplings.Count > 0)
                        Prop(Pick(_saplings), pos, Random.value * 360f, _flora);
                    else if (r < 0.78f && _leaves.Count > 0)
                        Prop(Pick(_leaves), pos, Random.value * 360f, _geometry);
                }
        }

        static Bounds BoundsOf(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            return b;
        }

        // Asphalt pad under the whole interior, following the pack demo's own
        // recipe for large paved areas: Road_Bare_01 at random yaws with the
        // cracked Road_03 mixed in, tar patches dropped at free positions and
        // sunk a few centimetres so only the raised blob shows, plus a couple
        // of manholes. The wear level rolls per interior — some pads come out
        // nearly clean, some badly cracked and patched — so neighbouring blocks
        // stop sharing one uniform grey. Cells fully inside a hole rect (sunken
        // bakes like the skatepark bowl) get no floor at all. Paved interiors
        // follow the PalmCity demo's court floor instead: a carpet of 2.5 m
        // SM_Env_Sidewalk_01 concrete plates at random quarter-turns.
        void BuildBlockFloor(float xMin, float xMax, float zMin, float zMax, List<Rect> holes,
            bool paved)
        {
            bool InHole(float x, float z, float w, float d)
            {
                if (holes == null) return false;
                foreach (var h in holes)
                    if (x >= h.xMin - 0.01f && x + w <= h.xMax + 0.01f &&
                        z >= h.yMin - 0.01f && z + d <= h.yMax + 0.01f) return true;
                return false;
            }

            if (paved && _pave != null)
            {
                EnsurePaveMeasured();
                float baseY = FloorLevel() - _paveTop; // plate tops flush with the kerb
                float sx = Mathf.Max(_paveSize.x, 0.5f), sz = Mathf.Max(_paveSize.z, 0.5f);
                bool square = Mathf.Abs(sx - sz) < 0.01f;
                for (float mx = xMin; mx < xMax - 0.1f; mx += sx)
                    for (float mz = zMin; mz < zMax - 0.1f; mz += sz)
                    {
                        if (InHole(mx, mz, sx, sz)) continue;
                        var cover = new Vector3(mx + sx * 0.5f, baseY, mz + sz * 0.5f);
                        var tile = Instantiate(_pave,
                            cover - new Vector3(_paveOffset.x, 0f, _paveOffset.z),
                            Quaternion.identity, _geometry);
                        if (square)
                            tile.transform.RotateAround(cover, Vector3.up, 90f * Random.Range(0, 4));
                    }
                return;
            }

            float wear = Random.Range(0.04f, 0.4f);
            float lotY = FloorLevel() - PrefabBoundsOf(_bare).max.y; // lot flush with the kerb too
            for (float mx = xMin; mx < xMax - 0.1f; mx += Cell)
                for (float mz = zMin; mz < zMax - 0.1f; mz += Cell)
                {
                    if (InHole(mx, mz, Cell, Cell)) continue;
                    var tile = _bareCracked != null && Random.value < wear ? _bareCracked : _bare;
                    PlaceCell(tile, mx, mz, 90 * Random.Range(0, 4), lotY);
                }

            if (_roadPatch != null)
            {
                int patches = Random.Range(4, 4 + (int)(wear * 50f));
                for (int p = 0; p < patches; p++)
                {
                    var pos = new Vector3(Random.Range(xMin + 1.5f, xMax - 1.5f),
                        FloorLevel() + Random.Range(-0.05f, -0.02f),
                        Random.Range(zMin + 1.5f, zMax - 1.5f));
                    if (InHole(pos.x, pos.z, 0f, 0f)) continue;
                    Instantiate(_roadPatch, pos,
                        Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), _geometry);
                }
            }

            for (int p = 0; p < 3; p++)
            {
                var pos = new Vector3(Random.Range(xMin + 2f, xMax - 2f), FloorLevel() + 0.02f,
                    Random.Range(zMin + 2f, zMax - 2f));
                if (InHole(pos.x, pos.z, 0f, 0f)) continue;
                Prop(_manhole, pos, Random.value * 360f, _geometry);
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

        // A bench that people can actually use: placed like any prop, and noted
        // for the street-life wiring. Courtyard benches register too and drop
        // out later - only spots near a sidewalk link get sitters.
        void PlaceBench(Vector3 pos, float yaw)
        {
            Prop(Pick(_benches), pos, yaw, _geometry);
            _pendingBenches.Add((pos, yaw));
        }

        void DressStreets()
        {
            for (int i = 0; i < verticalRoadX.Length; i++)
                for (int j = 0; j + 1 < horizontalRoadZ.Length; j++)
                {
                    if (!_vSeg[i, j]) continue;   // a court dresses itself
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
                    if (!_hSeg[j, i]) continue;
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
                        PlaceBench(At(bt, half + 3.4f), faceRoad);
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
                    PlaceBench(pos, faceRoad);
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
                    if (!_vSeg[i, j]) continue;
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
                    if (!_hSeg[j, i]) continue;
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
        //
        // Run by run, not road by road: a closed segment breaks the line, and a
        // single span stretched over the court in its place would be a 100 m wire
        // hanging across a garden with nothing holding it up.
        void PowerlinePass()
        {
            if (_powerpole == null || _wires.Count == 0) return;

            int nv = verticalRoadX.Length, nh = horizontalRoadZ.Length;

            for (int i = 0; i < nv; i++)
            {
                if (verticalIsBoulevard[i]) continue;
                float x = verticalRoadX[i] + StreetHalf + 4.3f;
                for (int j = 0; j + 1 < nh; )
                {
                    if (!_vSeg[i, j]) { j++; continue; }
                    int end = j;
                    while (end + 1 < nh && _vSeg[i, end]) end++;
                    PoleRun(PoleSpots(_nodes[i, j].ZMax + 2f, _nodes[i, end].ZMin - 2f,
                                      z => InsideNodeZoneZ(i, z)),
                            x, true);
                    j = end;
                }
            }
            for (int j = 0; j < nh; j++)
            {
                if (horizontalIsBoulevard[j]) continue;
                float z = horizontalRoadZ[j] - StreetHalf - 4.3f;
                for (int i = 0; i + 1 < nv; )
                {
                    if (!_hSeg[j, i]) { i++; continue; }
                    int end = i;
                    while (end + 1 < nv && _hSeg[j, end]) end++;
                    PoleRun(PoleSpots(_nodes[i, j].XMax + 2f, _nodes[end, j].XMin - 2f,
                                      x => InsideNodeZoneX(j, x)),
                            z, false);
                    i = end;
                }
            }
        }

        /// One unbroken line of poles: spots are positions ALONG the run, lateral
        /// is the fixed road-side offset on the other axis.
        void PoleRun(List<float> spots, float lateral, bool vertical)
        {
            const float WireLen = 7.696f;
            const float WireY = 8.33f;
            float[] strand = { -0.85f, 0f, 0.85f };
            float yaw = vertical ? 0f : 90f;

            Vector3 At(float along, float side) => vertical
                ? new Vector3(lateral + side, 0.1f, along)
                : new Vector3(along, 0.1f, lateral + side);

            foreach (float along in spots)
                Prop(_powerpole, At(along, 0f), yaw, _geometry);

            for (int k = 0; k + 1 < spots.Count; k++)
                foreach (float off in strand)
                {
                    var seat = At(spots[k], off);
                    var wire = Instantiate(Pick(_wires),
                        new Vector3(seat.x, WireY, seat.z),
                        Quaternion.Euler(0f, yaw, 0f), _geometry);
                    wire.transform.localScale =
                        new Vector3(1f, 1f, (spots[k + 1] - spots[k]) / WireLen);
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
                    if (!_vSeg[i, j]) continue;   // court, not carriageway
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
                    if (!_hSeg[j, i]) continue;   // court, not carriageway
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
                // A bend or a through-stretch gets no lights: with one way in and
                // one way out there is nothing to take turns over, and a signal
                // there only holds cars for a road that is not there. Signal stays
                // null, which both the drivers and the walkers already read as
                // "cross when you like" - see DemoVehicle.Tick and PedestrianAgent.
                if (Degree(n.I, n.J) < 3) continue;

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
                    AddCrossing(_corners[i, j, NW], _corners[i, j, NE], NorthOpen(i, j), vBlvd, true,
                        new Vector3(n.X, 0.02f, n.ZMax + Off), n.Signal);
                    AddCrossing(_corners[i, j, SW], _corners[i, j, SE], SouthOpen(i, j), vBlvd, true,
                        new Vector3(n.X, 0.02f, n.ZMin - Off), n.Signal);
                    AddCrossing(_corners[i, j, NE], _corners[i, j, SE], EastOpen(i, j), hBlvd, false,
                        new Vector3(n.XMax + Off, 0.02f, n.Z), n.Signal);
                    AddCrossing(_corners[i, j, NW], _corners[i, j, SW], WestOpen(i, j), hBlvd, false,
                        new Vector3(n.XMin - Off, 0.02f, n.Z), n.Signal);
                }

            // The pavement runs the length of every segment INCLUDING the closed
            // ones. Where a street was cut the two sidewalk lines survive as
            // footpaths across the court that replaced it, so people cut through
            // the garden instead of walking the long way round - and the court's
            // planting is told to keep off those two lanes (see GapCourt).
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

        // ------------------------------------------------------------- city life

        // Bench seat geometry from the city's measured table (InteractionMarkers):
        // Seat_01/02 - the two placed here - share a 0.50 seat top, sitters at
        // x +-0.55 along the slats, and the root 0.258 in front of the bench
        // origin (SitPelvisBack 0.438 less the backed slab's -0.18 pelvis Z).
        static readonly Vector3[] BenchSeatOffsets =
        {
            new Vector3(-0.55f, 0.50f, 0.258f),
            new Vector3(0.55f, 0.50f, 0.258f),
        };

        // Wire every noted door and bench to the sidewalk graph: nearest
        // non-gated link, joined mid-stretch - the same join the beat officers
        // use for the station forecourt. Whatever lands too far from a sidewalk
        // (courtyard benches deep in an interior) simply stays decorative.
        void BuildCityLife()
        {
            _life = new CityLife
            {
                SitChance = sitChance,
                EnterChance = enterChance,
                InsideSeconds = insideSeconds,
                SitSeconds = sitSeconds,
                CanSit = _sitDownClip != null && _sitLoopClip != null && _standUpClip != null,
                CanChat = _talkClip != null,
            };

            bool NearestLink(Vector3 p, float maxDist, out PedLink fwd, out float t)
            {
                fwd = null;
                t = 0f;
                float bestD = maxDist * maxDist;
                foreach (var l in _pedLinks)
                {
                    if (l.Gated || l.Length < 6f) continue;
                    var dir = (l.To.Pos - l.From.Pos) / l.Length;
                    float s = Mathf.Clamp(Vector3.Dot(p - l.From.Pos, dir), 2f, l.Length - 2f);
                    var q = l.From.Pos + dir * s;
                    float dx = q.x - p.x, dz = q.z - p.z;
                    float d = dx * dx + dz * dz;
                    if (d < bestD) { bestD = d; fwd = l; t = s; }
                }
                return fwd != null;
            }

            PedLink Reverse(PedLink l)
            {
                foreach (var r in l.To.Links)
                    if (r.To == l.From) return r;
                return null;
            }

            foreach (var (pos, outward) in _pendingDoors)
            {
                if (!NearestLink(pos, 14f, out var fwd, out var t)) continue;
                var back = Reverse(fwd);
                if (back == null) continue;
                var door = new DemoDoor
                {
                    Pos = pos, Outward = outward, LinkFwd = fwd, LinkBack = back, EntryT = t,
                    EntryPos = Vector3.Lerp(fwd.From.Pos, fwd.To.Pos, t / fwd.Length),
                };
                _life.Doors.Add(door);
                _life.AddStop(fwd, t, door, null);
                _life.AddStop(back, fwd.Length - t, door, null);
            }

            foreach (var (pos, yaw) in _pendingBenches)
            {
                if (!NearestLink(pos, 5f, out var fwd, out var t)) continue;
                var back = Reverse(fwd);
                if (back == null) continue;
                var rot = Quaternion.Euler(0f, yaw, 0f);
                var bench = new DemoBench
                {
                    SeatTops = new[]
                    {
                        pos + rot * BenchSeatOffsets[0],
                        pos + rot * BenchSeatOffsets[1],
                    },
                    Facing = rot * Vector3.forward,
                    GroundY = pos.y,
                };
                _life.AddStop(fwd, t, null, bench);
                _life.AddStop(back, fwd.Length - t, null, bench);
            }

            _life.SortStops();
        }

        void SpawnPedestrians()
        {
            if (_walkClip == null || _idleClip == null || _pedPrefabs.Count == 0) return;
            var root = new GameObject("People").transform;
            var sidewalks = _pedLinks.FindAll(l => !l.Gated);
            if (sidewalks.Count == 0) return;

            var clips = new PedClips
            {
                Walk = _walkClip, Idle = _idleClip,
                SitDown = _sitDownClip, SitLoop = _sitLoopClip, StandUp = _standUpClip,
                Talk = _talkClip, Shout = _shoutClip,
            };

            // the doorstep share starts indoors and streams out over the first
            // minute - the city fills from its buildings, not from thin air
            int fromDoors = _life != null && _life.Doors.Count > 0
                ? Mathf.RoundToInt(pedestrianCount * insideAtStart)
                : 0;

            for (int k = 0; k < pedestrianCount; k++)
            {
                var link = sidewalks[Random.Range(0, sidewalks.Count)];
                var prefab = _pedPrefabs[Random.Range(0, _pedPrefabs.Count)];
                var go = Instantiate(prefab, root);
                foreach (var col in go.GetComponentsInChildren<Collider>()) Destroy(col);
                foreach (var rb in go.GetComponentsInChildren<Rigidbody>()) Destroy(rb);

                var agent = new CivilianAgent { Speed = Random.Range(1.25f, 1.85f) };
                agent.Init(go.transform, clips, link, Random.value * link.Length * 0.9f);
                agent.Setup(_life);
                if (k < fromDoors)
                    agent.SpawnInside(Random.Range(2f, 60f));
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

        // ---------------------------------------------------------------- police

        const float StallSetback = 3.4f; // stall centre off the station face
        const float StallSpacing = 4f;   // bay pitch along the face

        // Called by BuildBlocks the moment the station lands in an interior: pick
        // the forecourt side (the widest strip of court between the building and
        // its street), lay the stall row against the face there, and reserve the
        // whole corridor out to the interior edge so frontage fillers, props and
        // lawns keep out of the driveway.
        Rect PlanForecourt(float xMin, float xMax, float zMin, float zMax, float floorTop)
        {
            var b = BoundsOf(_policeStation);
            var sides = new (float gap, Vector3 outDir, float face, float edge)[]
            {
                (xMax - b.max.x, Vector3.right, b.max.x, xMax),
                (b.min.x - xMin, Vector3.left, b.min.x, xMin),
                (zMax - b.max.z, Vector3.forward, b.max.z, zMax),
                (b.min.z - zMin, Vector3.back, b.min.z, zMin),
            };
            var best = sides[0];
            for (int k = 1; k < sides.Length; k++)
                if (sides[k].gap > best.gap) best = sides[k];

            _stallOut = best.outDir;
            _stallAlong = Vector3.Cross(Vector3.up, best.outDir);
            _stallLift = floorTop > 0f ? floorTop + 0.02f : 0.02f;
            _stallRowHalf = Mathf.Max(1, policeCarCount) * StallSpacing * 0.5f;

            var centre = b.center;
            _stallCentre = new Vector3(
                best.outDir.x != 0f ? best.face + best.outDir.x * StallSetback : centre.x,
                _stallLift,
                best.outDir.z != 0f ? best.face + best.outDir.z * StallSetback : centre.z);
            _forecourtPlanned = true;

            // corridor: stall row width (plus the officers' door lane) from the
            // building face all the way to the interior boundary
            float span = _stallRowHalf + 6f;
            float depth = Mathf.Abs(best.edge - best.face);
            var faceCentre = _stallCentre - _stallOut * StallSetback;
            var a = faceCentre - _stallAlong * span;
            var c = faceCentre + _stallAlong * span + _stallOut * depth;
            return Rect.MinMaxRect(
                Mathf.Min(a.x, c.x), Mathf.Min(a.z, c.z),
                Mathf.Max(a.x, c.x), Mathf.Max(a.z, c.z));
        }

        void SpawnPolice()
        {
            if (_policeStation == null || !_forecourtPlanned) return;

            var policeRoot = new GameObject("Police").transform;
            var markers = new List<IPatrolMarker>();

            SpawnPatrolCars(policeRoot, markers);
            SpawnFootPatrols(policeRoot, markers);

            if (markers.Count == 0) return;
            gameObject.AddComponent<PolicePatrolOverlay>().Init(markers);
        }

        void SpawnPatrolCars(Transform parent, List<IPatrolMarker> markers)
        {
            if (_policeCarPrefab == null || policeCarCount <= 0) return;

            // the kerb: the nearest lane point, where the fleet undocks onto the
            // graph and rolls to a stop coming home
            RoadEdge home = null;
            float homeS = 0f, bestD = float.MaxValue;
            foreach (var e in _edges)
            {
                if (e.Length < 26f) continue;
                float s = Mathf.Clamp(Vector3.Dot(_stallCentre - e.Start, e.Dir), 10f, e.Length - 14f);
                var p = e.Start + e.Dir * s;
                float d = (p - _stallCentre).sqrMagnitude;
                if (d < bestD) { bestD = d; home = e; homeS = s; }
            }
            if (home == null)
            {
                Debug.LogWarning("[RoadDemo] no lane near the police forecourt; fleet stays parked");
                return;
            }

            var routeHome = PolicePatrolCar.RouteToward(_edges, home);
            var stallRot = Quaternion.LookRotation(_stallOut);

            for (int i = 0; i < policeCarCount; i++)
            {
                var stall = _stallCentre + _stallAlong * ((i - (policeCarCount - 1) * 0.5f) * StallSpacing);
                var go = Instantiate(_policeCarPrefab, stall, Quaternion.identity, parent);
                go.name = "Patrol Car " + (i + 1);
                foreach (var rb in go.GetComponentsInChildren<Rigidbody>()) Destroy(rb);
                foreach (var col in go.GetComponentsInChildren<Collider>()) Destroy(col);

                // half length measured at identity yaw, before the stall rotation
                var bounds = new Bounds(go.transform.position, Vector3.zero);
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                    bounds.Encapsulate(r.bounds);
                go.transform.rotation = stallRot;

                var car = new PolicePatrolCar
                    { Tf = go.transform, HalfLen = bounds.extents.z + 0.3f, UnitNumber = i + 1 };
                car.InitParked(stall, stallRot, home, homeS, _edges, routeHome,
                    policeRestSeconds, policePatrolWaypoints, Random.Range(3f, 8f) + i * 5f);
                _policeCars.Add(car);
                markers.Add(car);
            }
        }

        void SpawnFootPatrols(Transform parent, List<IPatrolMarker> markers)
        {
            if (_officerPrefabs.Count == 0 || policeOfficerCount <= 0 ||
                _walkClip == null || _idleClip == null) return;

            // the station door: on the forecourt face, past the end of the stall
            // row so the walk out does not thread between parked cars
            var faceCentre = _stallCentre - _stallOut * StallSetback;
            var door = faceCentre + _stallAlong * (_stallRowHalf + 2.5f) + _stallOut * 0.8f;
            door.y = _stallLift;

            // the home stretch: the nearest sidewalk link, joined mid-way exactly
            // like the cars join their kerb lane
            PedLink homeFwd = null;
            float entryT = 0f, bestD = float.MaxValue;
            foreach (var l in _pedLinks)
            {
                if (l.Gated || l.Length < 6f) continue;
                var ab = l.To.Pos - l.From.Pos;
                var dir = ab.normalized;
                float t = Mathf.Clamp(Vector3.Dot(door - l.From.Pos, dir), 2f, l.Length - 2f);
                var p = l.From.Pos + dir * t;
                float d = (p - door).sqrMagnitude;
                if (d < bestD) { bestD = d; homeFwd = l; entryT = t; }
            }
            if (homeFwd == null) return;

            PedLink homeBack = null;
            foreach (var l in homeFwd.To.Links)
                if (l.To == homeFwd.From) { homeBack = l; break; }
            if (homeBack == null) return;

            var routeHome = BuildFootRouteHome(homeFwd);

            // every walkable corner, the officers' waypoint pool
            var nodeSet = new HashSet<PedNode>();
            foreach (var l in _pedLinks) { nodeSet.Add(l.From); nodeSet.Add(l.To); }
            var nodes = new List<PedNode>(nodeSet);

            for (int i = 0; i < policeOfficerCount; i++)
            {
                var prefab = _officerPrefabs[i % _officerPrefabs.Count];
                var go = Instantiate(prefab, door, Quaternion.identity, parent);
                go.name = "Beat Officer " + (i + 1);
                foreach (var col in go.GetComponentsInChildren<Collider>()) Destroy(col);
                foreach (var rb in go.GetComponentsInChildren<Rigidbody>()) Destroy(rb);

                var officer = new PoliceFootPatrol
                    { Speed = Random.Range(1.3f, 1.5f), UnitNumber = i + 1 };
                officer.Init(go.transform, _walkClip, _idleClip, homeFwd, entryT);
                officer.Configure(door, homeFwd, homeBack, entryT, nodes, routeHome,
                    policeRestSeconds, policePatrolWaypoints, Random.Range(4f, 10f) + i * 6f);
                _policeOfficers.Add(officer);
                markers.Add(officer);
            }
        }

        // The foot mirror: BFS from both ends of the home stretch over the ped
        // graph, then the link toward the nearer neighbour per node.
        Dictionary<PedNode, PedLink> BuildFootRouteHome(PedLink home)
        {
            var dist = new Dictionary<PedNode, int> { [home.From] = 0, [home.To] = 0 };
            var queue = new Queue<PedNode>();
            queue.Enqueue(home.From);
            queue.Enqueue(home.To);
            while (queue.Count > 0)
            {
                var n = queue.Dequeue();
                foreach (var l in n.Links)
                {
                    if (dist.ContainsKey(l.To)) continue;
                    dist[l.To] = dist[n] + 1;
                    queue.Enqueue(l.To);
                }
            }

            var next = new Dictionary<PedNode, PedLink>();
            foreach (var kv in dist)
            {
                PedLink best = null;
                int bestD = int.MaxValue;
                foreach (var l in kv.Key.Links)
                    if (dist.TryGetValue(l.To, out int d) && d < bestD) { bestD = d; best = l; }
                if (best != null) next[kv.Key] = best;
            }
            return next;
        }

        // ------------------------------------------------------------ environment

        void BuildEnvironment()
        {
            float minX = verticalRoadX[0], maxX = verticalRoadX[verticalRoadX.Length - 1];
            float minZ = horizontalRoadZ[0], maxZ = horizontalRoadZ[horizontalRoadZ.Length - 1];
            var centre = new Vector3((minX + maxX) * 0.5f, 0f, (minZ + maxZ) * 0.5f);

            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var sandMat = new Material(lit) { color = new Color(0.98f, 0.95f, 0.88f) };
#if UNITY_EDITOR
            var sandTex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/Synty/PolygonPalmCity/Textures/Terrain/Sand_01.png");
            if (sandTex != null)
                sandMat.mainTexture = sandTex;
            else
                sandMat.color = new Color(0.76f, 0.72f, 0.58f);
#endif
            sandMat.SetFloat("_Smoothness", 0.08f);

            // The grid rectangle is fully tiled by carriageways, sidewalks and the
            // interior pads, so the sand only needs to fringe it. A single plane
            // under everything would also slice through bakes that dig below
            // street level (the skatepark bowl reaches -2 m) — so the middle
            // stays open and the sand is four border strips instead.
            float gx0 = verticalRoadX[0] - VHalf(0) - Cell;
            float gx1 = verticalRoadX[verticalRoadX.Length - 1] + VHalf(verticalRoadX.Length - 1) + Cell;
            float gz0 = horizontalRoadZ[0] - HHalf(0) - Cell;
            float gz1 = horizontalRoadZ[horizontalRoadZ.Length - 1] + HHalf(horizontalRoadZ.Length - 1) + Cell;
            const float Fringe = 90f;
            void SandStrip(float x0, float x1, float z0, float z1)
            {
                var strip = GameObject.CreatePrimitive(PrimitiveType.Plane);
                strip.name = "Sand";
                Destroy(strip.GetComponent<Collider>());
                strip.transform.position = new Vector3((x0 + x1) * 0.5f, -0.06f, (z0 + z1) * 0.5f);
                strip.transform.localScale = new Vector3((x1 - x0) / 10f, 1f, (z1 - z0) / 10f);
                var mat = new Material(sandMat);
                if (mat.mainTexture != null)
                    mat.mainTextureScale = new Vector2((x1 - x0) / 12f, (z1 - z0) / 12f);
                strip.GetComponent<MeshRenderer>().sharedMaterial = mat;
            }
            SandStrip(gx0 - Fringe, gx1 + Fringe, gz0 - Fringe, gz0);
            SandStrip(gx0 - Fringe, gx1 + Fringe, gz1, gz1 + Fringe);
            SandStrip(gx0 - Fringe, gx0, gz0, gz1);
            SandStrip(gx1, gx1 + Fringe, gz0, gz1);

            var sunGo = new GameObject("Sun");
            _sun = sunGo.AddComponent<Light>();
            _sun.type = LightType.Directional;
            _sun.intensity = 1.25f;
            _sun.color = new Color(1f, 0.96f, 0.87f);
            _sun.shadows = LightShadows.Soft;
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

            // catalog-style building card on click; only the block bakes answer,
            // the street kit's own colliders stay mute
            var picker = camGo.AddComponent<LivingCity.CameraRig.BuildingCardPicker>();
            picker.pickRoot = _blocks;
        }

        // ------------------------------------------------------------- day/night

        Light _sun;

        // The demo's own day/night stack, self-contained in this folder: DemoClock
        // advances the hour and owns pause/speed, DemoSky swings the sun and moon
        // under a procedural skybox with the PalmCity cloud ring, DemoStreetLamps
        // and DemoHeadlights light the street after dark, DemoNightWindows lights
        // a share of the windows behind the facades, and DemoTopBar puts the clock
        // and the time controls across the top of the screen.
        void BuildDayNight()
        {
            var go = new GameObject("DayNight");

            var clock = go.AddComponent<DemoClock>();
            clock.secondsPerGameHour = Mathf.Max(0.02f, realSecondsPerGameHour);
            clock.startHour = startHour;

            var sky = go.AddComponent<DemoSky>();
            sky.clock = clock;
            sky.sun = _sun;

#if UNITY_EDITOR
            // the PalmCity cloud ring, at its own demo scene's height and scale
            var cloudFbx = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Synty/PolygonPalmCity/Models/SM_Env_Cloud_Ring_01.fbx");
            if (cloudFbx != null)
            {
                float ccx = (verticalRoadX[0] + verticalRoadX[verticalRoadX.Length - 1]) * 0.5f;
                float ccz = (horizontalRoadZ[0] + horizontalRoadZ[horizontalRoadZ.Length - 1]) * 0.5f;
                var clouds = Instantiate(cloudFbx, new Vector3(ccx, -67.3f, ccz),
                    Quaternion.identity);
                clouds.name = "Clouds";
                clouds.transform.localScale = new Vector3(3.98f, 4.26f, 3.98f);
                sky.cloudRing = clouds.transform;
                sky.cloudRenderer = clouds.GetComponentInChildren<Renderer>();
            }
#endif

            var lamps = go.AddComponent<DemoStreetLamps>();
            lamps.clock = clock;

            var windows = go.AddComponent<DemoNightWindows>();
            windows.clock = clock;

            var headlights = go.AddComponent<DemoHeadlights>();
            headlights.clock = clock;
            foreach (var v in _vehicles)
                headlights.Register(v.Tf, v.HalfLen);

            var barGo = new GameObject("TopBar");
            var bar = barGo.AddComponent<DemoTopBar>();
            bar.clock = clock;
        }
    }
}
