using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using PolyPerfect.City;
using LivingCity.Data;
using LivingCity.Generation;

namespace LivingCity.Entities
{
    /// <summary>
    /// Owns the city's school run: one bus and a fixed roster of children who ride it to school
    /// in the morning and home again in the afternoon. Modelled on PoliceDirector - everything
    /// is spawned here, parented here and PERSISTENT, outside VehicleSpawner's and
    /// PedestrianSpawner's populations, counts and sweeps, and none of it ever despawns.
    ///
    /// Persistent rather than spawned per run, and that is the load-bearing decision: a run
    /// that materialised eight children at a kerb and destroyed them at the school door would
    /// be a spawn/despawn round trip, which is exactly what the door system already refuses to
    /// do (see PedestrianAgent.BuildingRoutine). The children are simply hidden when they are
    /// at home and hidden again when they are inside the school - the same SetHidden the beat
    /// officers use for the station.
    ///
    /// Home is the SchoolMarker BlockBuilder attached at generation. A school is a CEILING and
    /// not a promise (see BlockZone): four one-cell zones compete for a handful of one-cell
    /// blocks, so a seed with no school at all is ordinary rather than exceptional, and this
    /// system standing down is the common path rather than an error.
    ///
    /// The school's facade orientation is no longer an assumption: building-school is absent
    /// from BuildFacadeYawFixes because it does not need an entry. The pack's own City - Day
    /// demo stands its one school with local +Z pointing exactly at the nearest road tile
    /// (dot 1.000, 16m), and the building's Z bounds are asymmetric - -5.30 to +6.71 - because
    /// the entrance block projects on the +Z side. A vertex-density profile disagrees, and is
    /// the known false positive: the T prefabs paint their windows into the atlas, so a fully
    /// glazed elevation carries no more geometry than a blank one.
    ///
    /// Two things here still cannot be proved by the headless suite and want one look in Play:
    ///  - what the traffic behind a halted bus actually does in a full city;
    ///  - whether this seed's school fit its block with the forecourt setback, or fell back to
    ///    standing flush (which costs the bus its berth - see SchoolMarker.HasBusStall).
    /// </summary>
    public sealed class SchoolBusDirector : MonoBehaviour
    {
        [SerializeField] CityConfig config;
        [SerializeField] PrefabDatabase prefabs;

        /// <summary>
        /// One pickup stop: where the bus halts, which way the lane runs there, and where on
        /// the pavement the children queue.
        /// </summary>
        public readonly struct Stop
        {
            public readonly Vector3 Kerb;
            public readonly Vector3 Direction;
            public readonly Vector3 Queue;

            public Stop(Vector3 kerb, Vector3 direction, Vector3 queue)
            {
                Kerb = kerb;
                Direction = direction;
                Queue = queue;
            }
        }

        readonly List<Stop> stops = new();
        readonly List<SchoolChildAgent> roster = new();

        SchoolMarker school;
        SchoolBusAgent bus;
        System.Random rng;

        public SchoolMarker School => school;
        public IReadOnlyList<Stop> Stops => stops;
        public IReadOnlyList<SchoolChildAgent> Roster => roster;

        /// <summary>The one bus, for the school building's own popup - which reports where it
        /// is. Null until SpawnBus has run, and on any seed where it could not.</summary>
        public SchoolBusAgent Bus => bus;

        IEnumerator Start()
        {
            if (!config || !prefabs)
            {
                Debug.LogWarning("[SchoolBus] Needs a CityConfig and a PrefabDatabase.", this);
                yield break;
            }

            // A disabled feature is not a warning - PoliceDirector's rule.
            if (config.schoolChildCount <= 0)
                yield break;

            rng = new System.Random(config.seed + SeedOffsets.School);

            // The same one-frame wait as every other spawner: Tile.Start() links the path
            // graphs, and nothing here may touch a lane or a pavement before that.
            yield return null;

            school = FindFirstObjectByType<SchoolMarker>();
            if (!school)
            {
                Debug.LogWarning(
                    "[SchoolBus] No SchoolMarker in the scene - this seed's city has no school " +
                    "block, or its landmark never fit. A school is a ceiling, not a promise " +
                    "(see BlockZone). The school run stands down; regenerate the city " +
                    "(Tools/City/Set Up Scene) for another seed.", this);
                yield break;
            }

            // So the school's own overlay popup can report where its pupils and its bus are.
            // Handed over here rather than looked up there: the marker is a saved-scene object
            // and the director is not, so the marker cannot be the one that goes looking.
            school.BindDirector(this);

            // Past the forecourt to about where the near lane is, exactly as PoliceDirector
            // aims past the station's. FocusFor is that reconstruction (stall depth plus the
            // walkway), and it is right for the flush fallback too: 6.6m from a flush facade is
            // 0.6m further than the old hand-written reach and still lands in the carriageway on
            // any road the pack lays. ForecourtKerb answers the rest: nearest road waypoint,
            // and which way its lane runs there.
            if (!ForecourtKerb.TryFind(ForecourtKerb.FocusFor(school, school.DoorWorld),
                                       out var schoolKerb, out var schoolKerbDir))
            {
                Debug.LogWarning("[SchoolBus] No road lane found in front of the school - the " +
                                 "school run stands down.", this);
                yield break;
            }

            PlanStops(schoolKerb);
            if (stops.Count == 0)
            {
                Debug.LogWarning("[SchoolBus] No usable pickup stops near the school - every " +
                                 "candidate lane was a junction lane, an inner lane, or out of " +
                                 "range. The school run stands down.", this);
                yield break;
            }

            if (!SpawnBus(schoolKerb, schoolKerbDir))
                yield break;

            SpawnChildren();
        }

        // ------------------------------------------------------------------ planning

        /// <summary>
        /// Picks the pickup stops: road waypoints in a band around the school, far enough apart
        /// to read as a route, on a lane a child can board from without crossing live traffic,
        /// and clear of junctions - a bus halted in a junction approach is a citywide jam, not
        /// a local one.
        /// </summary>
        void PlanStops(Vector3 schoolKerb)
        {
            var near = Mathf.Min(config.schoolStopDistance.x, config.schoolStopDistance.y);
            var far = Mathf.Max(config.schoolStopDistance.x, config.schoolStopDistance.y);

            var candidates = new List<Stop>();

            foreach (var tile in Tile.Tiles)
            {
                if (!tile || tile.paths == null)
                    continue;

                var distance = Flat(tile.transform.position - schoolKerb).magnitude;
                if (distance < near - CityGrid.CellSize || distance > far + CityGrid.CellSize)
                    continue;

                var outermost = OutermostLanes(tile, out var positiveX, out var negativeX);
                if (!outermost)
                    continue;

                foreach (var path in tile.paths)
                {
                    if (!path || path.pathType != PathType.Road || path.pathPositions == null)
                        continue;

                    // A bus standing in a junction lane holds the box itself, which is the one
                    // place the traffic model has no way round.
                    if (JunctionMap.IsJunctionLane(path))
                        continue;

                    for (var i = 1; i < path.pathPositions.Count; i++)
                    {
                        var node = path.pathPositions[i];
                        var previous = path.pathPositions[i - 1];
                        if (!node || !previous)
                            continue;

                        var offset = Flat(node.position - schoolKerb).magnitude;
                        if (offset < near || offset > far)
                            continue;

                        var local = tile.transform.InverseTransformPoint(node.position);
                        var side = local.x >= 0f ? positiveX : negativeX;
                        if (!SchoolRun.IsBoardableLane(local.x, side))
                            continue;

                        var segment = node.position - previous.position;
                        if (segment.sqrMagnitude < 1e-4f)
                            continue;

                        // The pavement on the bus's own side, by the same widest-|x|-wins rule
                        // PedestrianSpawner uses - so a child never boards across a lane.
                        var queue = PedestrianSpawner.SidewalkPoint(
                            tile, Mathf.Sign(local.x), local.z);

                        candidates.Add(new Stop(node.position, segment.normalized, queue));
                    }
                }
            }

            if (candidates.Count == 0)
                return;

            // Shuffled by the school stream, then taken greedily under the spacing rule: the
            // shuffle is what makes two cities with the same road layout serve different
            // corners of it, and the greedy pass is what stops the run collapsing into three
            // halts on one street.
            for (var i = candidates.Count - 1; i > 0; i--)
            {
                var j = rng.Next(i + 1);
                (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
            }

            foreach (var candidate in candidates)
            {
                if (stops.Count >= config.schoolBusStops)
                    break;

                var spaced = true;
                foreach (var taken in stops)
                    if (Flat(taken.Kerb - candidate.Kerb).magnitude < SchoolRun.MinStopSpacing)
                    {
                        spaced = false;
                        break;
                    }

                if (spaced)
                    stops.Add(candidate);
            }

            // Nearest the school first, so the morning run works its way out and the afternoon
            // run - the exact reverse - works its way back.
            stops.Sort((a, b) => Flat(a.Kerb - schoolKerb).sqrMagnitude
                        .CompareTo(Flat(b.Kerb - schoolKerb).sqrMagnitude));
        }

        /// <summary>
        /// How far out the outermost road lane sits on each side of a tile, in the tile's own
        /// local X. Read off the lane graph rather than from a table of road types, the same
        /// way SidewalkPoint reads the pavement off the sidewalk graph, so it keeps working for
        /// any tile the pack adds later.
        /// </summary>
        static bool OutermostLanes(Tile tile, out float positiveX, out float negativeX)
        {
            positiveX = 0f;
            negativeX = 0f;

            foreach (var path in tile.paths)
            {
                if (!path || path.pathType != PathType.Road || path.pathPositions == null)
                    continue;

                foreach (var node in path.pathPositions)
                {
                    if (!node)
                        continue;

                    var x = tile.transform.InverseTransformPoint(node.position).x;
                    if (x > positiveX)
                        positiveX = x;
                    else if (x < negativeX)
                        negativeX = x;
                }
            }

            return positiveX > 1e-3f || negativeX < -1e-3f;
        }

        // ------------------------------------------------------------------ spawning

        bool SpawnBus(Vector3 schoolKerb, Vector3 schoolKerbDir)
        {
            if (!prefabs.schoolBusPrefab)
            {
                Debug.LogWarning("[SchoolBus] No school bus prefab in the database - run the " +
                                 "asset bootstrap.", this);
                return false;
            }

            // Which way round the bus stands in its berth. The bay lies ALONG the street, so the
            // geometry offers two directions and only the traffic decides: a bus must pull out
            // with the flow, not into it. BusStallOut(false) is the bay's authored direction;
            // flip when it disagrees with the lane.
            var berth = school.HasBusStall;
            var flip = berth && Vector3.Dot(school.BusStallOut(false), schoolKerbDir) < 0f;

            var spawnAt = berth ? school.BusStallWorld : schoolKerb;
            var spawnRot = berth
                ? school.BusStallRotation(flip)
                : Quaternion.LookRotation(schoolKerbDir, Vector3.up);

            var vehicle = Instantiate(prefabs.schoolBusPrefab, spawnAt, spawnRot, transform);

            var behaviour = vehicle.GetComponent<CarBehavior>();
            if (!behaviour)
            {
                Debug.LogWarning("[SchoolBus] The school bus prefab has no CarBehavior.", this);
                Destroy(vehicle);
                return false;
            }

            // A bus that starts in its berth is off the lane graph until its first undock, and
            // CarBehavior must not run before then: Start() calls SetNewPath, which would put
            // the parked bus on a lane. Disabled BEFORE the first frame, so Start never runs -
            // the patrol fleet's start-parked trick, and it must be paired with the enable in
            // SchoolBusAgent.Undock or the bus never moves again.
            if (berth)
                behaviour.enabled = false;

            // CarBehavior.Start() has not run yet - Instantiate only fires Awake and OnEnable -
            // so the tuning still lands. The same per-spawn overrides VehicleSpawner,
            // PoliceDirector and BankVisitorDirector apply; the pack prefabs are never forked.
            if (config.carMinTravelDistance > 0f)
                behaviour.minDistance = config.carMinTravelDistance;
            if (config.carMaxSpeed > 0f)
                behaviour.maxspeed = config.carMaxSpeed;
            behaviour.headway = config.carHeadway;

            bus = vehicle.AddComponent<SchoolBusAgent>();
            bus.Bind(config, this, schoolKerb, schoolKerbDir, berth, flip);
            return true;
        }

        void SpawnChildren()
        {
            if (prefabs.schoolChildPrefabs == null || prefabs.schoolChildPrefabs.Length == 0)
            {
                Debug.LogWarning("[SchoolBus] No child prefabs in the database - run the asset " +
                                 "bootstrap. The bus would drive an empty route.", this);
                return;
            }

            for (var i = 0; i < config.schoolChildCount; i++)
            {
                // Round-robin over the stops rather than a roll, so no stop draws empty and the
                // queues come out as even as the roster divides.
                var stop = stops[i % stops.Count];
                var placeInQueue = i / stops.Count;
                var slot = SchoolRun.QueueSlot(placeInQueue, stop.Queue, -stop.Direction);

                var prefab = prefabs.schoolChildPrefabs[rng.Next(prefabs.schoolChildPrefabs.Length)];
                if (!prefab)
                    continue;

                // Facing back up the lane - the way somebody waiting for a bus watches for it.
                var person = Instantiate(prefab, slot,
                                         Quaternion.LookRotation(-stop.Direction, Vector3.up),
                                         transform);
                PedestrianSpawner.SetLayerRecursively(person.transform,
                                                      PedestrianSpawner.PedestrianLayer);
                PedestrianLodSystem.Register(person);

                var human = person.GetComponent<HumanBehavior>();
                if (!human)
                {
                    Debug.LogWarning("[SchoolBus] A child prefab has no HumanBehavior - re-run " +
                                     "the asset bootstrap.", this);
                    Destroy(person);
                    return;
                }

                // Same instance-level controller swap as PedestrianSpawner and PoliceDirector:
                // the interaction controller carries the speed parameter the agent writes, and
                // the child's own Humanoid avatar stays, so the clips retarget onto the child rig.
                var animator = person.GetComponent<Animator>();
                if (animator && prefabs.pedestrianController)
                    animator.runtimeAnimatorController = prefabs.pedestrianController;

                var child = person.AddComponent<SchoolChildAgent>();
                child.Bind(this, i % stops.Count, i + 1);
                roster.Add(child);
            }
        }

        // ------------------------------------------------------------------ queries

        /// <summary>The children whose home stop is <paramref name="stop"/>, in queue order.</summary>
        public void ChildrenAt(int stop, List<SchoolChildAgent> into)
        {
            into.Clear();
            foreach (var child in roster)
                if (child && child.HomeStop == stop)
                    into.Add(child);
        }

        /// <summary>
        /// Force a run now, ignoring the clock. An inspector affordance for testing - at the
        /// project's own 1 real second per game hour, waiting for a window to come round is
        /// fine, but waiting for the RIGHT window while debugging one is not.
        /// </summary>
        [ContextMenu("Run the school run now")]
        void RunNow()
        {
            if (bus)
                bus.ForceRun();
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }
    }
}
