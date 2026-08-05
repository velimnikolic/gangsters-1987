using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using PolyPerfect.City;
using LivingCity.Data;
using LivingCity.Generation;

namespace LivingCity.Entities
{
    /// <summary>
    /// Keeps a population of AI cars alive on the road network, entering and leaving at the edge
    /// of the map.
    ///
    /// Two different jobs, and they want opposite things:
    ///
    /// The FIRST population is scattered across the whole road network in a single frame, before
    /// anything has been drawn. A city is supposed to already have traffic in it when you arrive;
    /// watching thirty cars file in from the boundary would say the opposite, and would leave the
    /// streets visibly empty for the first half minute.
    ///
    /// Every REPLACEMENT after that arrives through a gap in the map's outline (see MapEdgeGates)
    /// and, wherever possible, one that is off screen. A car that pops into existence in the
    /// middle of a street is the single most artificial thing about the scene, and it is entirely
    /// avoidable: the streets already run off the edge of the map, so there is somewhere for
    /// traffic to come from.
    ///
    /// No object pool. CarBehavior.SetNewPath() is public so pooling is technically possible,
    /// but a recycled car re-enters carrying a stale trajectory, and at roughly one replacement a
    /// second the churn does not justify the complexity.
    ///
    /// The sweep exists because CarBehavior deactivates itself when it runs out of pathfinding
    /// retries. The Phase 0.2 patch stops that happening through slow attrition, but a genuine
    /// dead end can still take a car out - treating a deactivated car as "gone, replace it"
    /// makes the population self-healing either way.
    /// </summary>
    public sealed class VehicleSpawner : MonoBehaviour
    {
        [SerializeField] CityConfig config;
        [SerializeField] PrefabDatabase prefabs;

        [Tooltip("How often to check for cars that deactivated themselves.")]
        [SerializeField, Min(1f)] float sweepInterval = 5f;

        [Tooltip("Put the whole starting population on the road in one frame instead of " +
                 "staggering it by entitySpawnInterval. On by default: the pre-fill happens " +
                 "before the first frame is drawn, so the city simply has traffic in it rather " +
                 "than being seen to fill up. Turn it off to watch the population build.")]
        [SerializeField] bool fillInstantly = true;

        readonly List<GameObject> active = new();
        System.Random rng;
        Generation.VehiclePicker picker;
        MapEdgeGates gates;
        Camera view;

        IEnumerator Start()
        {
            if (!config || !prefabs)
            {
                Debug.LogWarning("[VehicleSpawner] Needs a CityConfig and a PrefabDatabase.", this);
                yield break;
            }

            rng = new System.Random(config.seed + Generation.SeedOffsets.Vehicles);
            picker = new Generation.VehiclePicker(prefabs.aiCarGroups, rng);

            if (picker.IsEmpty)
            {
                Debug.LogWarning("[VehicleSpawner] The PrefabDatabase has no usable AI car groups.", this);
                yield break;
            }

            // Let every Tile.Start() run first - that is what links the paths together, and a
            // car spawned before it happens would pathfind against an unconnected graph.
            yield return null;

            view = Camera.main;
            gates = FindFirstObjectByType<MapEdgeGates>();

            // A scene assembled by hand, or one whose city predates MapEdgeGates, has no gates.
            // Traffic still has to work there, so everything below falls back to the original
            // behaviour: spawn anywhere on the network, and never route a car off the map.
            if (gates && !gates.HasGates)
                gates = null;

            if (!gates)
                Debug.LogWarning("[VehicleSpawner] No map-edge gates found - cars will spawn on " +
                                 "random road tiles and stay in the city, as they did before.", this);

            for (var i = 0; i < config.carCount; i++)
            {
                SpawnOnNetwork();
                if (!fillInstantly && config.entitySpawnInterval > 0f)
                    yield return new WaitForSeconds(config.entitySpawnInterval);
            }

            StartCoroutine(SweepRoutine());
        }

        IEnumerator SweepRoutine()
        {
            var wait = new WaitForSeconds(sweepInterval);
            while (true)
            {
                yield return wait;

                for (var i = active.Count - 1; i >= 0; i--)
                {
                    var car = active[i];
                    if (car && car.activeSelf)
                        continue;

                    if (car)
                        Destroy(car);

                    active.RemoveAt(i);
                }

                while (active.Count < config.carCount)
                {
                    if (!SpawnReplacement())
                        break;
                }
            }
        }

        /// <summary>
        /// Takes a car that has driven off the map out of the population and brings another one
        /// in to hold the count. Called from TrafficAgent the moment a car finishes its exit
        /// route, standing on the outline of the map.
        /// </summary>
        public void Replace(GameObject car)
        {
            active.Remove(car);
            Destroy(car);
            SpawnReplacement();
        }

        /// <summary>Through a gate if the map has any, on a random road tile otherwise.</summary>
        bool SpawnReplacement() => SpawnAtGate() || SpawnOnNetwork();

        /// <summary>
        /// Brings a car in through a gap in the map's outline, already pointing down the lane.
        /// CarBehavior then places it exactly at the head of that lane - see snapToPathStart -
        /// so the car is on the road surface rather than merely near it.
        /// </summary>
        bool SpawnAtGate()
        {
            if (!gates || !gates.TryPickEntry(view, rng, out var gate))
                return false;

            var car = Spawn(gate.Point, Quaternion.LookRotation(gate.Direction));
            if (!car)
                return false;

            var behaviour = car.GetComponent<CarBehavior>();
            if (behaviour)
                behaviour.snapToPathStart = true;

            return true;
        }

        /// <summary>
        /// The original placement: a random road tile, with CarBehavior's own snap putting the
        /// car onto one of that tile's lanes. Used for the opening population and as the fallback
        /// when a scene has no gates.
        /// </summary>
        bool SpawnOnNetwork()
        {
            var tile = RandomRoadTile();
            return tile && Spawn(tile.transform.position, Quaternion.identity);
        }

        GameObject Spawn(Vector3 position, Quaternion rotation)
        {
            var prefab = picker.Next();
            if (!prefab)
                return null;

            var car = Instantiate(prefab, position, rotation, transform);

            // CarBehavior.Start() has not run yet - Instantiate only fires Awake and OnEnable -
            // so both the travel distance and the first destination can still be set.
            var behaviour = car.GetComponent<CarBehavior>();
            if (behaviour && config.carMinTravelDistance > 0f)
                behaviour.minDistance = config.carMinTravelDistance;

            if (gates && behaviour)
            {
                var routes = config.wanderRoutesBeforeExit;
                var wander = rng.Next(Mathf.Min(routes.x, routes.y), Mathf.Max(routes.x, routes.y) + 1);
                car.AddComponent<TrafficAgent>().Bind(this, gates, rng, wander);
            }

            active.Add(car);
            return car;
        }

        /// <summary>
        /// Reads Tile.Tiles rather than the CityGrid: when the city is generated in the editor
        /// and saved into the scene, there is no grid object at runtime, but every Tile has
        /// registered itself in OnEnable by the time Start runs.
        /// </summary>
        Tile RandomRoadTile()
        {
            if (Tile.Tiles.Count == 0)
                return null;

            for (var attempt = 0; attempt < 20; attempt++)
            {
                var tile = Tile.Tiles[rng.Next(Tile.Tiles.Count)];
                if (tile && (tile.tileType == Tile.TileType.Road || tile.tileType == Tile.TileType.RoadAndRail))
                    return tile;
            }

            return null;
        }
    }
}
