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
        [Header("Layout")]
        [Tooltip("Deal a new city whenever Play starts. Turn this off and enter the seed " +
                 "printed in Console to reproduce a reported layout.")]
        public bool newSeedEveryPlay = true;
        [Tooltip("Fixed/replay seed when New Seed Every Play is off. During Play this shows " +
                 "the seed currently used by the city.")]
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
        [Tooltip("Patrol cars docked at the station house's forecourt. The core stands a " +
                 "real station (the police-station-block the deal packs), so the fleet has " +
                 "a yard to undock from - but the scene as it shipped had none, and 0 " +
                 "leaves it exactly as it was.")]
        [Min(0)] public int policeCars;
        [Tooltip("Officers resting inside the station house, who come out when the wire " +
                 "calls them. 0 leaves the scene as it was.")]
        [Min(0)] public int policeOfficers;
        // EVERY FAMILY IS ON THE STREET (RIVAL-008, the user's word of 2026-09-03).
        // Six of them stood and fourteen ran on paper while the physical count was
        // waiting to be measured; all twenty stand now, and how many CORNERS they hold
        // between them is read from the size of the city (RoadDemoBuilder).
        [Range(0, 20)] public int rivalCrews = 20;
        [Range(0, 4)] public int rivalHoods = 3;

        [Header("Day")]
        [Range(0f, 24f)] public float startHour = 6f;
        [Tooltip("Real seconds for one game hour. 60 means one game minute lasts one real second.")]
        public float realSecondsPerGameHour = 60f;

        [Header("Reading the city")]
        [Tooltip("Click a building to open the catalog card - its name, footprint and " +
                 "height. Off in the core scenes: a click there is for the crew, the " +
                 "patrol and the premises, not for what a prefab measures.")]
        public bool buildingCards;

        [Header("Round the core")]
        [Min(20f)] public float greenBelt = 140f;

        [Header("Generated amenities")]
        [Tooltip("Independent parking parcels kept in the generated core. The remaining " +
                 "block-sized parcels become residential or complete park blocks.")]
        [Range(0, 8)] public int parkingLots = 3;
        [Tooltip("Live ParkingDemo cars cycling through each retained lot.")]
        [Range(0, 12)] public int parkingCarsPerLot = 5;
        [Tooltip("Maximum PumpDemo filling stations placed on suitable stand-alone former " +
                 "parking blocks. A station owns its whole block and never shares one with " +
                 "an existing building.")]
        [Range(0, 8)] public int fuelStations = 5;

        [Tooltip("Quarters built. 0 is the whole city; 2 is the test rig - the city is " +
                 "dealt whole and everything outside those quarters is taken back off it.")]
        [Min(0)] public int quarterBudget;

        // THE LAB'S PLAYER, ON THE CORE (EPIC 31 NIGHT-000). BlockDemo has had the mission
        // knobs since the first soak; the core - the scene the game is actually played on -
        // had none, so every judged run of the street happened on a rig nobody plays. The
        // knobs below are BlockDemoBuilder's own, name for name, and they attach the same
        // two components the same way. Every one of them defaults to off: a MiniCoreDemo
        // opened without them is the scene as it always was.
        [Header("The lab run")]
        [Tooltip("Sim seconds after the city is up before the outfit gets in its car and " +
                 "is sent at the rivals, one after another, and parks when they are down. " +
                 "0: nobody is sent anywhere - a normal Play session.")]
        public float missionAfter = 0f;
        [Tooltip("Seconds of drive-by passes at one crew before the men get out and " +
                 "finish it on foot.")]
        public float missionPasses = 45f;
        [Tooltip("The mission on foot: no car at all. The crew walks to the mob furthest " +
                 "from it - over the lots, across the roads, never mind the lights - and " +
                 "has it out with them there.")]
        public bool missionOnFoot;
        [Tooltip("The walkabout mission: no fight, no car - the crew walks corner to " +
                 "corner down the pavements and through the lights, and the crew audit " +
                 "judges the walk itself.")]
        public bool missionWalk;
        [Tooltip("Walkabout: corners to walk (0 leaves the mission's default 3).")]
        [Min(0)] public int missionWalkLegs = 0;
        [Tooltip("Walkabout: the HARD ceiling on one leg in seconds (0 leaves the default " +
                 "600). The core is a whole city and its legs are longer than the block " +
                 "lab's - raise this to judge the big scene fairly.")]
        [Min(0)] public float missionLegPatience = 0f;
        [Tooltip("The nerve lever: chance a man shot to his last hit breaks and runs. " +
                 "Below 0 leaves the game's own figure (0.4).")]
        public float panicChance = -1f;
        [Tooltip("The bomb mission: grenades at a rival crew, then a charge under a car.")]
        public bool missionBomb;
        [Tooltip("Bomb mission: grenades thrown at the rival before the plant test.")]
        [Min(1)] public int missionBombThrows = 3;
        [Tooltip("Bomb mission: swing the camera onto the action so a --shot frames it.")]
        public bool missionBombShot;
        [Tooltip("THE CAR BOMB: a charge under a rival's car, the crew walks clear, and " +
                 "the rival is sent for it. Overrides every other mission.")]
        public bool missionCarBomb;
        [Tooltip("Car bomb: metres the crew walks away from the charge before the rival " +
                 "is sent for his car.")]
        [Min(10f)] public float missionCarBombClearBy = 45f;
        [Tooltip("Car bomb: seconds any one leg may take before the run fails.")]
        [Min(10f)] public float missionCarBombPatience = 90f;
        [Tooltip("Car bomb: seconds to let the rest of the rival's crew climb in.")]
        [Min(0f)] public float missionCarBombSettle = 8f;
        [Tooltip("The mission on two wheels: one pass at a rival and home again, over and " +
                 "over. Needs outfitMotorcycle set.")]
        public bool missionMoto;
        [Tooltip("The car mission with a roadblock: the hunted mob is marched into the " +
                 "carriageway in front of the outfit's car every few seconds.")]
        public bool missionRoadblock;
        [Tooltip("Two wheels: how many passes to ride before the run is done.")]
        [Min(1)] public int missionPassesRidden = 3;
        [Tooltip("Two wheels: force one of the four endings on every pass - 2 the man on " +
                 "the back is shot, 3 the rider is shot, 4 the tank catches. 0 rides them " +
                 "as they fall.")]
        [Range(0, 4)] public int missionMotoAct;
        [Tooltip("A motorcycle bought off the armory counter and signed for by the " +
                 "outfit's first lieutenant, by listing name: Motorbike, Moped, Scooter.")]
        public string outfitMotorcycle = "";

        [Header("The outfit")]
        [Tooltip("Crews of the outfit sent out, one lieutenant each - the ledger is " +
                 "rewritten to match before anybody stands up. 0 leaves the seeded " +
                 "roster alone.")]
        [Min(0)] public int outfitLieutenants = 0;
        [Tooltip("Hoods behind each of those lieutenants. 0 sends them out alone.")]
        [Min(0)] public int outfitHoods = 0;
        [Tooltip("Every man on the street carrying his own piece instead of a crew " +
                 "holding five copies of one gun.")]
        public bool mixedArms = false;

        // THE FORCED SCENARIOS (EPIC 31 NIGHT-013). The night does not wait for a
        // scenario to happen by chance; it sets the city up so it MUST happen. These
        // reach systems the harness cannot -hSet, because RoadDemoBuilder and
        // TerritoryRuntime are made at runtime and have no component in the scene to
        // write to. Every one of them is off by default.
        [Header("Forced scenarios")]
        [Tooltip("Put the same word behind every counter in the city: Cowardly, Proud, " +
                 "Greedy, Connected, Stubborn, Careful. 'Connected' is the scenario where " +
                 "every owner rings the police. Empty deals the city as it deals itself.")]
        public string ownerTraitOverride = "";
        [Tooltip("What the player's safe holds when the city stands up. Below 0 leaves " +
                 "the ledger's own $25,000, which is what the scene has always started on.")]
        public int playerSafeAtStart = -1;
        [Tooltip("Game hours between one house's turns of mind. 0 leaves the model's own " +
                 "figure (4). Twenty houses think one at a time in rota, so a run that " +
                 "wants every family to have thought needs this short or the clock long.")]
        [Min(0f)] public float mindThinkEveryHours = 0f;

        void Awake()
        {
#if UNITY_EDITOR
            if (newSeedEveryPlay) seed = FreshPlaySeed();
            Debug.Log($"[CoreDemo] Play seed {seed}." +
                      (newSeedEveryPlay
                          ? " Disable New Seed Every Play and enter this number to replay it."
                          : " Fixed-seed replay is enabled."));

            // a sketch left in the scene from the editor menu would stand under the quarter
            foreach (var root in gameObject.scene.GetRootGameObjects())
                if (root.name == CoreLayout.SketchRoot) Destroy(root);

            // BEFORE THE CITY STANDS. The owner override is read the first time a
            // counter is asked about, and the mind's cadence the first time a house
            // thinks - both of which happen after this, so setting them here is the
            // whole of the wiring. An empty override restores the city's own deal, so a
            // scene played twice in one editor session does not inherit the last run's
            // scenario (the two statics outlive Play).
            TerritoryRuntime.OwnerTraitOverride =
                System.Enum.TryParse<LivingCity.Territory.TerritoryOwnerTrait>(
                    ownerTraitOverride, true, out var forcedTrait)
                    ? forcedTrait
                    : (LivingCity.Territory.TerritoryOwnerTrait?)null;
            if (!string.IsNullOrEmpty(ownerTraitOverride) &&
                TerritoryRuntime.OwnerTraitOverride == null)
                Debug.LogWarning($"[CoreDemo] '{ownerTraitOverride}' is not one of the six " +
                                 "owner traits; the city deals its own men.");
            TerritoryRuntime.MindThinkEveryHoursOverride = Mathf.Max(0f, mindThinkEveryHours);
            TerritoryRuntime.PlayerSafeAtStartOverride = playerSafeAtStart;

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
                quarterBudget = Mathf.Max(0, quarterBudget),
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
            // THE FLEET HAS A YARD AFTER ALL. The core's deal packs the station block
            // (police-station-block, which carries a building-policestation), and
            // RoadDemoBuilder.FindStationHouses sweeps the districts root for exactly
            // that name - so a car asked for here docks at a real forecourt. It stayed
            // at nought because nobody had asked; the scenarios ask now.
            runtime.policeCarCount = police ? Mathf.Max(0, policeCars) : 0;
            runtime.policeOfficerCount = police ? Mathf.Max(0, policeOfficers) : 0;
            runtime.policeBeatPairs = police ? Mathf.Max(0, policeBeatPairs) : 0;
            runtime.rivalCrewsInCity = Mathf.Max(0, rivalCrews);
            runtime.rivalHoodsInCity = Mathf.Max(0, rivalHoods);
            runtime.buildingCards = buildingCards;
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

            // the books first: the men who will be stood up are the men the ledger says
            // the outfit has, so the run's outfit is written before it deals
            if (outfitLieutenants > 0 || !string.IsNullOrEmpty(outfitMotorcycle))
            {
                var books = gameObject.AddComponent<BlockDemo.BlockDemoOutfit>();
                books.lieutenants = outfitLieutenants;
                books.hoodsEach = outfitHoods;
                books.mixedArms = mixedArms;
                books.armsSeed = seed;
                books.motorcycle = outfitMotorcycle;
            }

            // BlockDemoMission never referenced BlockDemoBuilder - it finds DemoCrews
            // itself - so the lab's player rides the core with no fork of its own.
            if (missionAfter > 0f)
            {
                var mission = gameObject.AddComponent<BlockDemo.BlockDemoMission>();
                mission.startAfter = missionAfter;
                mission.passesBefore = missionPasses;
                mission.onFoot = missionOnFoot;
                mission.walkabout = missionWalk;
                mission.panic = panicChance;
                mission.motoDriveBy = missionMoto;
                mission.roadblock = missionRoadblock;
                mission.passes = missionPassesRidden;
                mission.forceAct = missionMotoAct;
                mission.bombRun = missionBomb;
                mission.bombThrows = missionBombThrows;
                mission.bombShotCam = missionBombShot;
                mission.carBombRun = missionCarBomb;
                mission.carBombClearBy = missionCarBombClearBy;
                mission.carBombPatience = missionCarBombPatience;
                mission.carBombSettle = missionCarBombSettle;
                if (missionWalkLegs > 0) mission.walkLegs = missionWalkLegs;
                if (missionLegPatience > 0f) mission.legPatience = missionLegPatience;
            }

            var rig = FindFirstObjectByType<DemoCamera>();
            if (rig != null)
            {
                // No FrameSpan here any more: framing the whole quarter opened the game
                // above the map line, looking at a plan of a city rather than at the man
                // the game is about. The runtime already opens on the Don's own doorstep,
                // in the street (RoadDemoBuilder.BuildEnvironment).
                rig.yaw = 20f;
                rig.pitch = 55f;
                rig.showHint = false;
            }
#else
            Debug.LogError("[CoreDemo] The core loads Synty prefabs through the AssetDatabase and only runs in the editor.");
#endif
        }

        /// <summary>Independent of UnityEngine.Random so choosing the city's identity does
        /// not shift any ambient/global random sequence before the seeded systems start.</summary>
        static int FreshPlaySeed()
        {
            int value = System.BitConverter.ToInt32(System.Guid.NewGuid().ToByteArray(), 0) & int.MaxValue;
            return value == 0 ? 1 : value;
        }
    }
}
