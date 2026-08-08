using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using PolyPerfect.City;
using LivingCity.Data;
using LivingCity.Generation;

namespace LivingCity.Entities
{
    /// <summary>
    /// Keeps a trickle of customers arriving at the bank. The bank's forecourt used to be a row
    /// of static cars and nothing else - and, because landmarkCars is a property of the PALETTE
    /// rather than of the landmark, the cars it got were the police station's. Civilian bakes
    /// fixed what stood there; this fixes that nothing ever happened there.
    ///
    /// Modelled on PoliceDirector and deliberately NOT on VehicleSpawner: visitors are spawned
    /// here, parented here and counted here, outside config.carCount and outside the spawner's
    /// population, sweeps and replacement bookkeeping. The two systems would otherwise both
    /// believe they owned the same car.
    ///
    /// Where they DO agree is how a car may enter: through a gap in the map's outline, never
    /// materialising mid-street. A visitor is a whole trip - in off the boundary, into the bay,
    /// out again and gone - so the car is a different model each time rather than a fixed cast
    /// circling forever, which is the difference between customers and a car park.
    ///
    /// Home is the BankForecourt marker BlockBuilder attached at generation. A scene generated
    /// before that marker existed - or a seed whose bank drew its own block, where the parking
    /// is the block's perimeter rather than a forecourt - has none, and the system stands down
    /// with one warning rather than guessing.
    /// </summary>
    public sealed class BankVisitorDirector : MonoBehaviour
    {
        [SerializeField] CityConfig config;
        [SerializeField] PrefabDatabase prefabs;

        /// <summary>Footprint checked before spawning into a gate - VehicleSpawner's, for
        /// VehicleSpawner's reason: the arriving car needs somewhere to accelerate into.</summary>
        const float GateHalfLength = 5f;
        const float GateHalfWidth = 1.5f;

        const int GateAttempts = 6;

        readonly List<BankVisitorAgent> visitors = new();

        BankForecourt forecourt;
        MapEdgeGates gates;
        VehiclePicker picker;
        System.Random rng;
        Camera view;
        Vector3 kerbPos;
        Vector3 kerbDir;

        IEnumerator Start()
        {
            if (!config || !prefabs)
            {
                Debug.LogWarning("[BankVisitors] Needs a CityConfig and a PrefabDatabase.", this);
                yield break;
            }

            if (config.bankVisitorCount <= 0)
                yield break;

            rng = new System.Random(config.seed + SeedOffsets.BankVisitors);
            picker = new VehiclePicker(prefabs.aiCarGroups, rng);
            if (picker.IsEmpty)
            {
                Debug.LogWarning("[BankVisitors] The PrefabDatabase has no usable AI car groups.", this);
                yield break;
            }

            // The same one-frame wait as every other spawner: Tile.Start() links the path
            // graphs, and nothing here may touch a lane before that.
            yield return null;

            forecourt = FindFirstObjectByType<BankForecourt>();
            if (!forecourt)
            {
                Debug.LogWarning(
                    "[BankVisitors] No BankForecourt marker in the scene - this seed's bank has " +
                    "no forecourt, or the city predates the marker. Visitors stand down; " +
                    "regenerate the city (Tools/City/Set Up Scene) to fix.", this);
                yield break;
            }

            if (forecourt.StallCount <= 0)
            {
                // Every bay took a static bake, or every bay collided with something already
                // placed. Nothing is broken - there is simply nowhere to park.
                Debug.Log("[BankVisitors] The bank's forecourt has no free bays - no visitors.", this);
                yield break;
            }

            if (!ForecourtKerb.TryFind(
                    ForecourtKerb.FocusFor(forecourt, forecourt.transform.position),
                    out kerbPos, out kerbDir))
            {
                Debug.LogWarning("[BankVisitors] No road lane found near the bank's forecourt - " +
                                 "visitors stand down.", this);
                yield break;
            }

            view = Camera.main;
            gates = FindFirstObjectByType<MapEdgeGates>();
            if (gates && !gates.HasGates)
                gates = null;

            StartCoroutine(TopUpRoutine());
        }

        /// <summary>
        /// Holds the population at bankVisitorCount. A poll rather than an event chain: a
        /// visitor can leave through the boundary, be destroyed on a failed path, or simply
        /// never find a free bay, and one loop that asks "how many are there" covers all three
        /// without any of them having to report in.
        /// </summary>
        IEnumerator TopUpRoutine()
        {
            while (true)
            {
                for (var i = visitors.Count - 1; i >= 0; i--)
                    if (!visitors[i])
                        visitors.RemoveAt(i);

                if (visitors.Count < config.bankVisitorCount)
                    Spawn();

                yield return new WaitForSeconds(Range(config.bankVisitorGapRange));
            }
        }

        void Spawn()
        {
            var prefab = picker.Next();
            if (!prefab)
                return;

            if (!Entry(out var position, out var rotation, out var snap))
                return;

            var car = Instantiate(prefab, position, rotation, transform);

            var behaviour = car.GetComponent<CarBehavior>();
            if (!behaviour)
            {
                Destroy(car);
                return;
            }

            // CarBehavior.Start() has not run yet - Instantiate only fires Awake and OnEnable -
            // so the tuning still lands. Same per-spawn overrides VehicleSpawner and
            // PoliceDirector apply; the 23 pack prefabs are never forked to retune traffic.
            if (config.carMinTravelDistance > 0f)
                behaviour.minDistance = config.carMinTravelDistance;
            if (config.carMaxSpeed > 0f)
                behaviour.maxspeed = config.carMaxSpeed;
            behaviour.headway = config.carHeadway;
            behaviour.snapToPathStart = snap;

            var visitor = car.AddComponent<BankVisitorAgent>();
            visitor.Bind(config, forecourt, gates, this, kerbPos, kerbDir, rng.Next());
            visitors.Add(visitor);
        }

        /// <summary>
        /// Through a gate if the map has any, on a random road tile otherwise - VehicleSpawner's
        /// two entrances, and its retry loop for an occupied gate: an occupied gate means two
        /// cars in the same metre of road, at the map edge, in plain view.
        /// </summary>
        bool Entry(out Vector3 position, out Quaternion rotation, out bool snap)
        {
            if (gates)
            {
                for (var attempt = 0; attempt < GateAttempts; attempt++)
                {
                    if (!gates.TryPickEntry(view, rng, out var gate))
                        break;

                    if (!TrafficRegistry.IsClear(gate.Point, gate.Direction, GateHalfLength, GateHalfWidth))
                        continue;

                    position = gate.Point;
                    rotation = Quaternion.LookRotation(gate.Direction);
                    snap = true;
                    return true;
                }
            }

            var tile = RandomRoadTile();
            if (!tile)
            {
                position = Vector3.zero;
                rotation = Quaternion.identity;
                snap = false;
                return false;
            }

            position = tile.transform.position;
            rotation = Quaternion.identity;
            snap = false;
            return true;
        }

        Tile RandomRoadTile()
        {
            if (Tile.Tiles.Count == 0)
                return null;

            for (var attempt = 0; attempt < 20; attempt++)
            {
                var tile = Tile.Tiles[rng.Next(Tile.Tiles.Count)];
                if (tile && (tile.tileType == Tile.TileType.Road ||
                             tile.tileType == Tile.TileType.RoadAndRail))
                    return tile;
            }

            return null;
        }

        /// <summary>Called from the agent's OnDestroy. The top-up loop prunes dead entries
        /// anyway - this only keeps the list from carrying a destroyed car until it does.</summary>
        public void Departed(BankVisitorAgent visitor) => visitors.Remove(visitor);

        float Range(Vector2 range) =>
            range.x + (float)rng.NextDouble() * Mathf.Max(0f, range.y - range.x);
    }
}
