using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// The core's own scene: one thin component that hands <see cref="CoreDistrict"/>
    /// to the game's <see cref="RoadDemoBuilder"/> as its city structure. The structure
    /// is Core's; traffic, people, police, crews, combat, day/night, audio and the map
    /// come from the shared RoadDemo runtime rather than a demo-only fork.
    ///
    /// The fields are the district's own, out on the inspector for trying things - and
    /// for the play harness, which writes them before the scene wakes up.
    /// </summary>
    public class CoreDemoBuilder : MonoBehaviour
    {
        public int seed = 1987;

        [Header("Traffic")]
        public int carCount = 24;
        public float streetSpeed = 9f;
        public float boulevardSpeed = 13f;
        public float alleySpeed = 5f;

        [Header("Life (shared game systems)")]
        [Min(0)] public int bikeCount = 4;
        [Min(0)] public int pedestrianCount = 100;
        public bool police = true;
        [Min(0)] public int policeBeatPairs = 3;
        [Range(0, 20)] public int rivalCrews = 6;
        [Range(0, 4)] public int rivalHoods = 3;

        [Header("Day")]
        [Range(0f, 24f)] public float startHour = 16f;
        [Tooltip("Real seconds for one game hour. 600 means one game minute lasts 10 real seconds.")]
        public float realSecondsPerGameHour = 600f;

        [Header("Round the core")]
        [Min(20f)] public float greenBelt = 140f;

        [Header("Generated amenities")]
        [Tooltip("Independent parking parcels kept in the generated core. The remaining " +
                 "block-sized parcels become residential or complete park blocks.")]
        [Range(0, 8)] public int parkingLots = 3;
        [Tooltip("Live ParkingDemo cars cycling through each retained lot.")]
        [Range(0, 12)] public int parkingCarsPerLot = 5;
        [Tooltip("PumpDemo filling stations placed on suitable former parking parcels. " +
                 "Each station contains the demo's two-pump forecourt.")]
        [Range(0, 8)] public int fuelStations = 5;

        void Awake()
        {
#if UNITY_EDITOR
            // a sketch left in the scene from the editor menu would stand under the quarter
            foreach (var root in gameObject.scene.GetRootGameObjects())
                if (root.name == CoreLayout.SketchRoot) Destroy(root);

            var district = new CoreDistrict
            {
                // The district supplies roads, never its own copy of the traffic.
                // RoadDemoBuilder.SpawnCars is the one car spawner in both scenes.
                carCount = 0,
                streetSpeed = streetSpeed,
                boulevardSpeed = boulevardSpeed,
                alleySpeed = alleySpeed,
                parkingLotCount = Mathf.Max(0, parkingLots),
                parkingCarsPerLot = Mathf.Max(0, parkingCarsPerLot),
                fuelStationCount = Mathf.Max(0, fuelStations),
            };

            // Inactive while it is configured: RoadDemoBuilder.Awake must see Core as
            // the primary structure before it chooses its build pass sequence.
            var runtimeObject = new GameObject("Game Runtime (Core structure)");
            runtimeObject.SetActive(false);
            var runtime = runtimeObject.AddComponent<RoadDemoBuilder>();
            runtime.ConfigurePrimaryStructure(district, seed);

            runtime.citySeed = seed;
            runtime.spacingSeed = seed;
            runtime.cityLayoutSeed = seed;
            runtime.carCount = Mathf.Max(0, carCount);
            runtime.bikeCount = Mathf.Max(0, bikeCount);
            runtime.pedestrianCount = Mathf.Max(0, pedestrianCount);
            runtime.insideAtStart = 0f; // Core does not publish building doors yet
            runtime.policeCarCount = 0; // no police forecourt in the structural core yet
            runtime.policeOfficerCount = 0;
            runtime.policeBeatPairs = police ? Mathf.Max(0, policeBeatPairs) : 0;
            runtime.rivalCrewsInCity = Mathf.Max(0, rivalCrews);
            runtime.rivalHoodsInCity = Mathf.Max(0, rivalHoods);
            runtime.scaleLifeToCity = false;
            runtime.updateProfile = false;

            runtime.startHour = startHour;
            runtime.realSecondsPerGameHour = realSecondsPerGameHour;
            runtime.rollShoreline = false;
            runtime.mainlandEdge = CityEdge.None;
            float belt = Mathf.Max(20f, greenBelt);
            runtime.islandWest = belt;
            runtime.islandEast = belt;
            runtime.islandNorth = belt;
            runtime.islandSouth = belt;
            runtime.coastWander = belt * 0.3f;
            runtime.treesPerHectare = 14f;

            runtimeObject.SetActive(true);

            var rig = FindFirstObjectByType<DemoCamera>();
            if (rig != null)
            {
                var bounds = runtime.PrimaryWorldBounds;
                rig.FrameSpan(Mathf.Max(bounds.width, bounds.height), 0.95f);
                rig.yaw = 20f;
                rig.pitch = 55f;
                rig.showHint = false;
            }
#else
            Debug.LogError("[CoreDemo] The core loads Synty prefabs through the AssetDatabase and only runs in the editor.");
#endif
        }
    }
}
