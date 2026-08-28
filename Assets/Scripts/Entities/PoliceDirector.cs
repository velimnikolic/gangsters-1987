using System.Collections;
using UnityEngine;
using LivingCity.City;
using LivingCity.Data;
using LivingCity.Generation;

namespace LivingCity.Entities
{
    /// <summary>
    /// Owns the city's police: a fixed fleet of patrol cars homed on each station's forecourt
    /// and a fixed handful of beat officers homed on its door. The city runs several stations
    /// now - ZonePlanner spreads one per ~40 blocks - and every one is a precinct of its own:
    /// its cars and officers are bound to IT and return to IT, so coverage follows the marks'
    /// spread rather than pooling at whichever station happened to be found first. Everything
    /// police is spawned here, parented here and persistent - none of it passes through
    /// VehicleSpawner or PedestrianSpawner's populations, counts or sweeps, and none of it
    /// ever despawns.
    ///
    /// The session opens MID-SHIFT on purpose: most of each fleet is already out on random
    /// roads and most officers already mid-beat on random pavements, with every timer and
    /// route budget drawn independently, so the city arrives looking like the patrol has
    /// been running for hours rather than like a depot emptying. How many start at base is
    /// config (policeCarsStartAtStation / policeOfficersStartAtStation), per station.
    ///
    /// Home is the PoliceStation marker BlockBuilder attached at generation. A scene
    /// generated before the marker existed - or a pathological seed whose stations never fit
    /// - has none, and the whole system stands down with one warning rather than guessing:
    /// regenerating the city (Tools/City) is the fix.
    /// </summary>
    public sealed class PoliceDirector : MonoBehaviour
    {
        [SerializeField] CityConfig config;
        [SerializeField] PrefabDatabase prefabs;

        /// <summary>The response force self-wires its officer/car prefabs from here at
        /// runtime, so a scene that predates PoliceResponseDirector still fields one.</summary>
        public PrefabDatabase Prefabs => prefabs;

        System.Random rng;

        // City-wide, not per-precinct: "Car 3" in the overlay must name one car however
        // many stations share the streets.
        int nextCarNumber = 1;
        int nextOfficerNumber = 1;

        IEnumerator Start()
        {
            if (!config || !prefabs)
            {
                Debug.LogWarning("[PoliceDirector] Needs a CityConfig and a PrefabDatabase.", this);
                yield break;
            }

            if (config.policeCarCount <= 0 && config.policeOfficerCount <= 0)
                yield break;

            rng = new System.Random(config.seed + SeedOffsets.Police);

            // Same one-frame wait as both spawners: Tile.Start() links the path graphs, and
            // nothing here may touch a lane or pavement before that.
            yield return null;

            var stations = FindObjectsByType<PoliceStation>(FindObjectsSortMode.None);
            if (stations.Length == 0)
            {
                Debug.LogWarning(
                    "[PoliceDirector] No PoliceStation marker in the scene - the city predates " +
                    "the police rework or this seed's stations never fit. Police stand down; " +
                    "regenerate the city (Tools/City/Set Up Scene) to fix.", this);
                yield break;
            }

            // Find order is engine whim, and one rng stream serves every precinct - sorted by
            // position so the same seed staffs the same stations with the same draws.
            System.Array.Sort(stations, (a, b) =>
            {
                var ax = Mathf.RoundToInt(a.transform.position.x * 10f);
                var bx = Mathf.RoundToInt(b.transform.position.x * 10f);
                return ax != bx
                    ? ax.CompareTo(bx)
                    : Mathf.RoundToInt(a.transform.position.z * 10f)
                        .CompareTo(Mathf.RoundToInt(b.transform.position.z * 10f));
            });

            foreach (var station in stations)
            {
                if (!FindKerb(station, out var kerbPos, out var kerbDir))
                {
                    Debug.LogWarning("[PoliceDirector] No road lane found near a station's " +
                                     "forecourt - that precinct stands down.", station);
                    continue;
                }

                SpawnFleet(station, kerbPos, kerbDir);
                SpawnOfficers(station);
            }
        }

        /// <summary>
        /// A fleet's junction with the lane graph. The search itself is ForecourtKerb's,
        /// shared with the bank; what is police here is only where to look from - out of the
        /// DOOR rather than off the building's origin, because that is the elevation the bays
        /// were cut in front of.
        /// </summary>
        static bool FindKerb(PoliceStation station, out Vector3 kerbPos, out Vector3 kerbDir) =>
            ForecourtKerb.TryFind(
                ForecourtKerb.FocusFor(station, station.DoorWorld), out kerbPos, out kerbDir);

        void SpawnFleet(PoliceStation station, Vector3 kerbPos, Vector3 kerbDir)
        {
            if (config.policeCarCount <= 0)
                return;

            if (!prefabs.policeCarPrefab)
            {
                Debug.LogWarning("[PoliceDirector] No police car prefab in the database - " +
                                 "run the asset bootstrap.", this);
                return;
            }

            var parked = Mathf.Min(config.policeCarsStartAtStation,
                                   config.policeCarCount, station.StallCount);

            for (var i = 0; i < config.policeCarCount; i++)
            {
                if (i < parked && station.TryClaimStall(out var stall))
                {
                    var car = SpawnCar(station.StallWorld(stall), station.StallRotation(stall));
                    if (!car)
                    {
                        station.ReleaseStall(stall);
                        continue;
                    }

                    // Held disabled so Start()'s SetNewPath cannot teleport the parked car
                    // onto a lane; the first undock enables it at the kerb. Registration is
                    // tied to enablement, which is right too: a car in a stall is off the
                    // road, and traffic has nothing to brake for.
                    car.GetComponent<CarBehavior>().enabled = false;
                    car.AddComponent<PolicePatrolAgent>()
                       .Bind(config, station, kerbPos, kerbDir, rng.Next(), stall, nextCarNumber++);
                }
                else
                {
                    var tile = RandomTile(roadForCars: true);
                    if (!tile)
                        continue;

                    var car = SpawnCar(tile.transform.position, Quaternion.identity);
                    if (!car)
                        continue;

                    car.AddComponent<PolicePatrolAgent>()
                       .Bind(config, station, kerbPos, kerbDir, rng.Next(), -1, nextCarNumber++);
                }
            }
        }

        /// <summary>Instantiate plus the same per-spawn overrides VehicleSpawner applies -
        /// the 23 pack prefabs are never forked for tuning, and neither is this one.</summary>
        GameObject SpawnCar(Vector3 position, Quaternion rotation)
        {
            var car = Instantiate(prefabs.policeCarPrefab, position, rotation, transform);

            var behaviour = car.GetComponent<CarBehavior>();
            if (!behaviour)
            {
                Debug.LogWarning("[PoliceDirector] The police car prefab has no CarBehavior.", this);
                Destroy(car);
                return null;
            }

            if (config.carMinTravelDistance > 0f)
                behaviour.minDistance = config.carMinTravelDistance;
            if (config.carMaxSpeed > 0f)
                behaviour.maxspeed = config.carMaxSpeed;
            behaviour.headway = config.carHeadway;

            return car;
        }

        void SpawnOfficers(PoliceStation station)
        {
            if (config.policeOfficerCount <= 0)
                return;

            if (!prefabs.policeOfficerPrefab)
            {
                Debug.LogWarning("[PoliceDirector] No police officer prefab in the database - " +
                                 "run the asset bootstrap.", this);
                return;
            }

            var inside = Mathf.Min(config.policeOfficersStartAtStation, config.policeOfficerCount);

            for (var i = 0; i < config.policeOfficerCount; i++)
            {
                var startInside = i < inside;

                Vector3 position;
                Quaternion rotation;
                if (startInside)
                {
                    position = station.DoorWorld;
                    rotation = Quaternion.LookRotation(station.Facing, Vector3.up);
                }
                else
                {
                    var tile = RandomTile(roadForCars: false);
                    if (!tile)
                        continue;

                    // SidewalkPoint works in the tile's LOCAL frame, so the distance along
                    // the street is an AUTHORED one - PedestrianSpawner's reason: CellSize
                    // carries TileScale and would put the officer off the end of the tile.
                    var side = rng.Next(2) == 0 ? 1f : -1f;
                    var along = ((float)rng.NextDouble() - 0.5f) * CityGrid.AuthoredCellSize * 0.6f;
                    position = PedestrianSpawner.SidewalkPoint(tile, side, along);
                    rotation = Quaternion.Euler(0f, rng.Next(4) * 90f, 0f);
                }

                var person = Instantiate(prefabs.policeOfficerPrefab, position, rotation, transform);
                PedestrianAnthropometry.Apply(
                    person,
                    PedestrianAnthropometry.Seed(config.seed, nextOfficerNumber, PedestrianAnthropometry.PoliceSalt),
                    PedestrianIdentity.IsFemale(prefabs.policeOfficerPrefab.name),
                    PedestrianAgeCohort.Adult,
                    prefabs.policeOfficerPrefab.name);

                PedestrianSpawner.SetLayerRecursively(person.transform, PedestrianSpawner.PedestrianLayer);
                PedestrianLodSystem.Register(person);

                var human = person.GetComponent<HumanBehavior>();
                if (!human)
                {
                    Debug.LogWarning("[PoliceDirector] The officer prefab has no HumanBehavior - " +
                                     "re-run the asset bootstrap.", this);
                    Destroy(person);
                    return;
                }

                human.randomDestination = true;

                // Same instance-level controller swap as PedestrianSpawner: the interaction
                // controller carries the speed parameter the agent writes; the officer's own
                // Humanoid avatar stays, so the clips retarget onto the police rig.
                var animator = person.GetComponent<Animator>();
                if (animator && prefabs.pedestrianController)
                    animator.runtimeAnimatorController = prefabs.pedestrianController;

                person.AddComponent<PoliceOfficerAgent>()
                      .Configure(config, station, rng.Next(), startInside, nextOfficerNumber++);
            }
        }

        Tile RandomTile(bool roadForCars)
        {
            if (Tile.Tiles.Count == 0)
                return null;

            for (var attempt = 0; attempt < 20; attempt++)
            {
                var tile = Tile.Tiles[rng.Next(Tile.Tiles.Count)];
                if (!tile)
                    continue;

                if (roadForCars
                    ? tile.tileType == Tile.TileType.Road || tile.tileType == Tile.TileType.RoadAndRail
                    : tile.tileType == Tile.TileType.Road || tile.tileType == Tile.TileType.OnlyPathwalk)
                    return tile;
            }

            return null;
        }
    }
}
