using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using LivingCity.City;
using LivingCity.Data;
using LivingCity.Generation;

namespace LivingCity.Entities
{
    /// <summary>
    /// Keeps a trickle of drivers arriving at a landmark's forecourt: in off the map's outline,
    /// into a bay, a while inside, out again and gone. The bank's customers were the first, the
    /// school's parents the second, and the two differ only in which building, how long they
    /// stay, how many at once, and one word in the popup - so the trip itself lives here and
    /// each subclass answers those four questions.
    ///
    /// Modelled on PoliceDirector and deliberately NOT on VehicleSpawner: visitors are spawned
    /// here, parented here and counted here, outside config.carCount and outside the spawner's
    /// population, sweeps and replacement bookkeeping. The two systems would otherwise both
    /// believe they owned the same car.
    ///
    /// Where they DO agree is how a car may enter: through a gap in the map's outline, never
    /// materialising mid-street. A visitor is a whole trip, so the car is a different model each
    /// time rather than a fixed cast circling forever - which is the difference between
    /// customers and a car park.
    ///
    /// The config and prefabs fields live HERE, on the base, and are still found by name: the
    /// editor wires them with SerializedObject.FindProperty, which flattens inherited fields, and
    /// a BankVisitorDirector already baked into grad.unity keeps its references for the same
    /// reason. Renaming either field would break both.
    /// </summary>
    public abstract class ForecourtVisitorDirector : MonoBehaviour
    {
        [SerializeField] protected CityConfig config;
        [SerializeField] protected PrefabDatabase prefabs;

        /// <summary>Footprint checked before spawning into a gate - VehicleSpawner's, for
        /// VehicleSpawner's reason: the arriving car needs somewhere to accelerate into.</summary>
        const float GateHalfLength = 5f;
        const float GateHalfWidth = 1.5f;

        const int GateAttempts = 6;

        readonly List<ForecourtVisitorAgent> visitors = new();

        StallHost forecourt;
        MapEdgeGates gates;
        VehiclePicker picker;
        VehicleTinter tinter;
        System.Random rng;
        Camera view;
        Vector3 kerbPos;
        Vector3 kerbDir;

        // ------------------------------------------------------------------ the subclass

        /// <summary>Prefix on this director's log lines - "[BankVisitors]".</summary>
        protected abstract string LogTag { get; }

        /// <summary>Which stream of the city seed this director draws from.</summary>
        protected abstract int SeedOffset { get; }

        /// <summary>How many of these drivers are in the city at once. Zero turns the whole
        /// system off, which is a setting rather than a fault - PoliceDirector's rule.</summary>
        protected abstract int Population { get; }

        /// <summary>Seconds between arrivals, drawn uniformly.</summary>
        protected abstract Vector2 GapRange { get; }

        /// <summary>Seconds parked, drawn uniformly.</summary>
        protected abstract Vector2 StayRange { get; }

        /// <summary>What the popup calls this trip.</summary>
        protected abstract UI.ForecourtErrand Errand { get; }

        /// <summary>The building whose bays these drivers use. Null is an ordinary outcome -
        /// the seed drew no such landmark, or it drew one with no forecourt - and the caller
        /// says so once and stands down.</summary>
        protected abstract StallHost FindForecourt();

        /// <summary>Where the kerb search starts from. The building's own position for a bank;
        /// its door for a school, which is not centred on its facade.</summary>
        protected abstract Vector3 KerbFocusFrom(StallHost host);

        /// <summary>Said once when there is no forecourt to serve. Subclasses word it, because
        /// the reasons differ and a generic sentence would help nobody debug either.</summary>
        protected abstract string MissingForecourtMessage { get; }

        /// <summary>
        /// Hands this director to the building it serves, so the building's overlay popup can
        /// report how many drivers are here. Done this way round because the marker is a
        /// saved-scene object and the director is not: the marker cannot go looking.
        /// </summary>
        protected virtual void BindToForecourt(StallHost host)
        {
        }

        // ------------------------------------------------------------------ the trip

        /// <summary>
        /// The whole start-up. Named Run rather than Start, and each subclass declares its own
        /// one-line Start that calls it: an inherited Unity message is a thing to have to be
        /// sure about, and being sure costs one line per subclass.
        /// </summary>
        protected IEnumerator Run()
        {
            if (!config || !prefabs)
            {
                Debug.LogWarning($"{LogTag} Needs a CityConfig and a PrefabDatabase.", this);
                yield break;
            }

            if (Population <= 0)
                yield break;

            rng = new System.Random(config.seed + SeedOffset);
            picker = new VehiclePicker(prefabs.aiCarGroups, rng);

            // Its own stream rather than this director's, so that retuning car paint cannot
            // change which visitors arrive or when. See SeedOffsets.VehicleTints.
            tinter = new VehicleTinter(prefabs, config);
            if (picker.IsEmpty)
            {
                Debug.LogWarning($"{LogTag} The PrefabDatabase has no usable AI car groups.", this);
                yield break;
            }

            // The same one-frame wait as every other spawner: Tile.Start() links the path
            // graphs, and nothing here may touch a lane before that.
            yield return null;

            forecourt = FindForecourt();
            if (!forecourt)
            {
                Debug.LogWarning($"{LogTag} {MissingForecourtMessage}", this);
                yield break;
            }

            BindToForecourt(forecourt);

            if (forecourt.StallCount <= 0)
            {
                // Every bay took a static bake, or every bay collided with something already
                // placed. Nothing is broken - there is simply nowhere to park.
                Debug.Log($"{LogTag} The forecourt has no free bays - no visitors.", this);
                yield break;
            }

            if (!ForecourtKerb.TryFind(
                    ForecourtKerb.FocusFor(forecourt, KerbFocusFrom(forecourt)),
                    out kerbPos, out kerbDir))
            {
                Debug.LogWarning($"{LogTag} No road lane found near the forecourt - visitors " +
                                 "stand down.", this);
                yield break;
            }

            view = Camera.main;
            gates = FindFirstObjectByType<MapEdgeGates>();
            if (gates && !gates.HasGates)
                gates = null;

            StartCoroutine(TopUpRoutine());
        }

        /// <summary>
        /// Holds the population at <see cref="Population"/>. A poll rather than an event chain:
        /// a visitor can leave through the boundary, be destroyed on a failed path, or simply
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

                if (visitors.Count < Population)
                    Spawn();

                yield return new WaitForSeconds(Range(GapRange));
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
            tinter?.Paint(car, prefab);

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

            var visitor = car.AddComponent<ForecourtVisitorAgent>();
            visitor.Bind(forecourt, gates, this, kerbPos, kerbDir, StayRange, Errand, rng.Next());
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
        public void Departed(ForecourtVisitorAgent visitor) => visitors.Remove(visitor);

        /// <summary>How many are parked or on their way right now - the number the landmark's
        /// own popup reports.</summary>
        public int VisitorCount
        {
            get
            {
                var live = 0;
                for (var i = 0; i < visitors.Count; i++)
                    if (visitors[i])
                        live++;

                return live;
            }
        }

        float Range(Vector2 range) =>
            range.x + (float)rng.NextDouble() * Mathf.Max(0f, range.y - range.x);
    }
}
