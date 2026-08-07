using System.Collections;
using UnityEngine;
using PolyPerfect.City;
using LivingCity.Data;
using LivingCity.Generation;

namespace LivingCity.Entities
{
    /// <summary>
    /// Owns the city's police: a fixed fleet of patrol cars homed on the station's forecourt
    /// and a fixed handful of beat officers homed on its door. Everything police is spawned
    /// here, parented here and persistent - none of it passes through VehicleSpawner or
    /// PedestrianSpawner's populations, counts or sweeps, and none of it ever despawns.
    ///
    /// The session opens MID-SHIFT on purpose: most of the fleet is already out on random
    /// roads and most officers already mid-beat on random pavements, with every timer and
    /// route budget drawn independently, so the city arrives looking like the patrol has
    /// been running for hours rather than like a depot emptying. How many start at base is
    /// config (policeCarsStartAtStation / policeOfficersStartAtStation).
    ///
    /// Home is the PoliceStation marker BlockBuilder attached at generation. A scene
    /// generated before the marker existed - or a pathological seed whose station never fit
    /// - has none, and the whole system stands down with one warning rather than guessing:
    /// regenerating the city (Tools/City) is the fix.
    /// </summary>
    public sealed class PoliceDirector : MonoBehaviour
    {
        [SerializeField] CityConfig config;
        [SerializeField] PrefabDatabase prefabs;

        PoliceStation station;
        System.Random rng;

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

            station = FindFirstObjectByType<PoliceStation>();
            if (!station)
            {
                Debug.LogWarning(
                    "[PoliceDirector] No PoliceStation marker in the scene - the city predates " +
                    "the police rework or this seed's station never fit. Police stand down; " +
                    "regenerate the city (Tools/City/Set Up Scene) to fix.", this);
                yield break;
            }

            if (!FindKerb(out var kerbPos, out var kerbDir))
            {
                Debug.LogWarning("[PoliceDirector] No road lane found near the station's " +
                                 "forecourt - police stand down.", this);
                yield break;
            }

            SpawnFleet(kerbPos, kerbDir);
            SpawnOfficers();
        }

        /// <summary>
        /// The fleet's junction with the lane graph: the road waypoint nearest the forecourt,
        /// and the direction of its lane segment there. Returning cars route to it, docking
        /// curves start from it, undocking cars pull out onto it.
        /// </summary>
        bool FindKerb(out Vector3 kerbPos, out Vector3 kerbDir)
        {
            // Out from the door, past the forecourt band, is where the street must be.
            var focus = station.DoorWorld
                      + station.Facing * (ParkingLayout.StallDepth + BlockBuilder.LandmarkForecourtWalkway);

            kerbPos = Vector3.zero;
            kerbDir = Vector3.forward;
            var best = float.MaxValue;

            foreach (var tile in Tile.Tiles)
            {
                if (!tile || tile.paths == null)
                    continue;
                if ((tile.transform.position - focus).sqrMagnitude > CityGrid.CellSize * CityGrid.CellSize * 4f)
                    continue;

                foreach (var path in tile.paths)
                {
                    if (!path || path.pathType != PathType.Road || path.pathPositions == null)
                        continue;

                    for (var i = 1; i < path.pathPositions.Count; i++)
                    {
                        var node = path.pathPositions[i];
                        var previous = path.pathPositions[i - 1];
                        if (!node || !previous)
                            continue;

                        var distance = (node.position - focus).sqrMagnitude;
                        if (distance >= best)
                            continue;

                        var segment = node.position - previous.position;
                        if (segment.sqrMagnitude < 1e-4f)
                            continue;

                        best = distance;
                        kerbPos = node.position;
                        kerbDir = segment.normalized;
                    }
                }
            }

            return best < float.MaxValue;
        }

        void SpawnFleet(Vector3 kerbPos, Vector3 kerbDir)
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
                       .Bind(config, station, kerbPos, kerbDir, rng.Next(), stall);
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
                       .Bind(config, station, kerbPos, kerbDir, rng.Next(), -1);
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

        void SpawnOfficers()
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

                    var side = rng.Next(2) == 0 ? 1f : -1f;
                    var along = ((float)rng.NextDouble() - 0.5f) * CityGrid.CellSize * 0.6f;
                    position = PedestrianSpawner.SidewalkPoint(tile, side, along);
                    rotation = Quaternion.Euler(0f, rng.Next(4) * 90f, 0f);
                }

                var person = Instantiate(prefabs.policeOfficerPrefab, position, rotation, transform);
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
                      .Configure(config, station, rng.Next(), startInside);
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
