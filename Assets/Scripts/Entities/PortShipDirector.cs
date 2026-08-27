using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using LivingCity.Data;
using LivingCity.Generation;

namespace LivingCity.Entities
{
    /// <summary>
    /// Owns the port's shipping: at every berth along the waterfront, cargo ships that sail in
    /// up the coast, lie alongside, get worked by a forklift, and sail out again - plus the
    /// passers-by, ships and small craft that cross the horizon without ever docking, because
    /// a sea with traffic ONLY for its own berths reads as a stage set. Modelled on the other
    /// directors: everything spawned here, parented here, outside every spawner's counts,
    /// standing down by itself on a landlocked seed.
    ///
    /// The ships are pure transforms driven by coroutines - no CarBehavior, no physics, no
    /// path network. Each live vessel is a bare root the glide legs move, with the model as a
    /// child carrying ShipBob - the heave and roll live on the child so the two writers can
    /// never fight.
    ///
    /// Collision between ships is prevented by CONSTRUCTION, not by avoidance: every berth
    /// owns its own sailing lane, the passers get two farther lanes (one per direction), and
    /// all berth traffic runs the coast the same way - in from the low end, out at the high
    /// end. Two ships can only ever pass on parallel lanes.
    ///
    /// The berth cycles open STAGGERED - the first berth empty with its ship minutes... no:
    /// SECONDS away, the rest alongside at different points of their stay - so something on
    /// the water is visibly moving from the first moments of Play rather than after the first
    /// full dwell expires, which on the first cut was ninety motionless seconds and read as
    /// "the ships do not move".
    ///
    /// The editor bakes a static preview ship at each berth (port_ship_preview_*, see
    /// PortDresser). At Play this director destroys the previews; the staggering above means
    /// most berths re-open with a live ship in the preview's pose, so the handoff still
    /// reads as the same worked waterfront.
    ///
    /// Wired by CitySceneSetup, and ALSO self-bootstrapped by PortDirector on any scene saved
    /// before this component existed - a regenerated city keeps its old Spawners object, and
    /// shipping that silently required a re-run of Set Up Scene was exactly the bug that
    /// shipped first.
    /// </summary>
    public sealed class PortShipDirector : MonoBehaviour
    {
        [SerializeField] CityConfig config;
        [SerializeField] PrefabDatabase prefabs;

        /// <summary>Cruise on the coast run, and the crawl of the berthing legs.</summary>
        const float SailSpeed = 9f;
        const float SlideSpeed = 1.6f;

        /// <summary>First sailing lane's offset seaward of the berth line, and the step per
        /// berth. Three berths put the outermost hull edge ~56m out, inside the 78m sea.</summary>
        const float LaneOffset = 16f;
        const float LaneStep = 10f;

        /// <summary>The passers' two lanes, beyond every berth lane - one per direction, so
        /// the through traffic can never meet anything head-on, itself included.</summary>
        const float PasserLaneMargin = 8f;
        const float PasserLaneGap = 9f;

        /// <summary>Seconds between one passer clearing the coast and the next setting out.</summary>
        const float PasserGapMin = 25f;
        const float PasserGapMax = 70f;

        /// <summary>How far past the last quay the coast run continues before a ship is
        /// spawned or retired - just off the map corner, over the corner square's water.</summary>
        const float CoastSlack = 30f;

        /// <summary>The forklift's shuttle: speed, and the pause at each end of a run.</summary>
        const float ForkliftSpeed = 4f;
        const float ForkliftPause = 1.6f;

        System.Random rng;
        bool started;

        /// <summary>Every forklift body still on the crowd registry. A destroyed director
        /// stops its coroutines before any of them reaches its own Unregister, so the
        /// bodies are let go here instead of leaking as phantom obstacles.</summary>
        readonly List<PedestrianBody> forkliftBodies = new List<PedestrianBody>();

        /// <summary>For the self-bootstrap path - PortDirector hands its own references over
        /// because an AddComponent'd instance has nothing serialized.</summary>
        public void Configure(CityConfig cityConfig, PrefabDatabase database)
        {
            config = cityConfig;
            prefabs = database;
        }

        void OnDestroy()
        {
            foreach (var body in forkliftBodies)
                PedestrianRegistry.Unregister(body);
            forkliftBodies.Clear();
        }

        IEnumerator Start()
        {
            // Both the scene-wired instance and a self-bootstrap can exist briefly; whichever
            // Start runs second finds the flag set on the component that matters. One
            // component only ever runs one shipping layer.
            if (started)
                yield break;
            started = true;

            if (!config || !prefabs)
            {
                Debug.LogWarning("[PortShips] Needs a CityConfig and a PrefabDatabase.", this);
                yield break;
            }

            rng = new System.Random(config.seed + SeedOffsets.PortShips);

            yield return null;

            var markers = FindObjectsByType<PortMarker>();
            if (markers.Length == 0)
                yield break;   // Landlocked seed - PortDirector already said so.

            // Deterministic order whatever Find returns in: lane assignment and every rng
            // hand-off below follow marker order, and BlockId is the stable name.
            System.Array.Sort(markers, (a, b) => a.BlockId.CompareTo(b.BlockId));

            var palette = prefabs.PaletteFor(BlockZone.Port);
            if (palette == null || palette.portShips == null || palette.portShips.Length == 0)
            {
                Debug.LogWarning("[PortShips] No ship prefabs on the Port palette - " +
                                 "run the asset bootstrap.", this);
                yield break;
            }

            // The coast: one axis shared by every berth. Its extent is the union of the quay
            // lines plus slack over the corner squares - the run a ship enters and leaves on.
            var along = markers[0].AlongQuay;
            var low = float.MaxValue;
            var high = float.MinValue;
            foreach (var marker in markers)
            {
                low = Mathf.Min(low, Along(marker.QuayFrom, along), Along(marker.QuayTo, along));
                high = Mathf.Max(high, Along(marker.QuayFrom, along), Along(marker.QuayTo, along));
            }
            low -= CoastSlack;
            high += CoastSlack;

            var berths = 0;
            foreach (var marker in markers)
            {
                if (!marker.HasBerth)
                    continue;

                var preview = GameObject.Find($"port_ship_preview_{marker.BlockId}");
                if (preview)
                    Destroy(preview);

                StartCoroutine(BerthCycle(marker, palette, along, low, high,
                                          laneIndex: berths, openEmpty: berths == 0,
                                          seed: rng.Next()));
                berths++;
            }

            if (berths > 0)
                StartCoroutine(PasserTraffic(markers[0], palette, along, low, high,
                                             farLane: LaneOffset + berths * LaneStep + PasserLaneMargin,
                                             seed: rng.Next()));
        }

        /// <summary>
        /// One berth's life, forever: a ship alongside being worked, out up the coast, empty
        /// water, the next one in from down it. <paramref name="openEmpty"/> staggers the
        /// opening frame - the first berth starts with its ship seconds out so the water is
        /// alive immediately; the rest start mid-stay at different points of the dwell.
        /// </summary>
        IEnumerator BerthCycle(
            PortMarker marker, PrefabDatabase.ZonePalette palette,
            Vector3 along, float coastLow, float coastHigh,
            int laneIndex, bool openEmpty, int seed)
        {
            var berthRng = new System.Random(seed);
            var seaward = marker.Seaward;
            var berth = marker.BerthCentre;
            var lanePoint = berth + seaward * (LaneOffset + laneIndex * LaneStep);
            var yaw = Quaternion.LookRotation(along, Vector3.up);

            var working = new WorkingFlag();
            SpawnForklift(marker, berthRng, working);

            GameObject ship = null;
            var firstStay = 0f;

            if (openEmpty)
            {
                // The one berth that opens empty: its ship appears up the coast within
                // seconds, so the first thing a player sees on the water is an arrival.
                yield return new WaitForSeconds(2f + (float)berthRng.NextDouble() * 4f);
            }
            else
            {
                ship = Moor(palette, berthRng, berth, yaw);
                firstStay = Range(config.portShipStayRange, berthRng)
                          * (0.25f + 0.6f * (float)berthRng.NextDouble());
            }

            while (true)
            {
                if (ship)
                {
                    // Alongside: the forklift works for as long as the ship is in.
                    working.Value = true;
                    yield return new WaitForSeconds(
                        firstStay > 0f ? firstStay : Range(config.portShipStayRange, berthRng));
                    firstStay = 0f;
                    working.Value = false;

                    // Out: slide to the lane, then run the coast to the high end and retire.
                    yield return Glide(ship.transform, lanePoint, SlideSpeed);
                    var exit = lanePoint + along * (coastHigh - Along(lanePoint, along));
                    yield return Glide(ship.transform, exit, SailSpeed);
                    Destroy(ship);
                    ship = null;

                    yield return new WaitForSeconds(Range(config.portShipGapRange, berthRng));
                }

                // In: appear off the low corner on this berth's own lane, run up the coast to
                // abeam of the berth, and slide alongside.
                var entry = lanePoint + along * (coastLow - Along(lanePoint, along));
                ship = Moor(palette, berthRng, entry, yaw);
                if (!ship)
                {
                    yield return new WaitForSeconds(Range(config.portShipGapRange, berthRng));
                    continue;
                }

                yield return Glide(ship.transform, lanePoint, SailSpeed);
                yield return Glide(ship.transform, berth, SlideSpeed);
            }
        }

        /// <summary>
        /// The horizon traffic: every half-minute or so a vessel - a cargo ship or one of the
        /// small craft - crosses the whole coast on a far lane and is gone. It never docks
        /// and nothing depends on it; it exists because an ocean whose every ship has
        /// business with this one town reads as a model railway.
        /// </summary>
        IEnumerator PasserTraffic(
            PortMarker anyMarker, PrefabDatabase.ZonePalette palette,
            Vector3 along, float coastLow, float coastHigh, float farLane, int seed)
        {
            var passerRng = new System.Random(seed);
            var seaward = anyMarker.Seaward;
            var reference = anyMarker.BerthCentre;

            // First passer almost immediately - the opening frame should have distant motion.
            yield return new WaitForSeconds(1f + (float)passerRng.NextDouble() * 3f);

            while (true)
            {
                var eastbound = passerRng.Next(2) == 0;
                var lane = farLane + (eastbound ? 0f : PasserLaneGap);
                var lanePoint = reference + seaward * lane;
                lanePoint.y = -PortLayout.WaterDrop;

                var prefab = PickPasser(palette, passerRng);
                if (prefab)
                {
                    var fromA = eastbound ? coastLow : coastHigh;
                    var toA = eastbound ? coastHigh : coastLow;
                    var heading = eastbound ? along : -along;

                    var start = lanePoint + along * (fromA - Along(lanePoint, along));
                    var end = lanePoint + along * (toA - Along(lanePoint, along));

                    var vessel = Launch(prefab, start,
                                        Quaternion.LookRotation(heading, Vector3.up),
                                        passerRng, bobScale: 1.4f);

                    // Small craft hurry, ships do not.
                    var speed = prefab.name.StartsWith("ship") ? SailSpeed : SailSpeed * 1.8f;
                    yield return Glide(vessel.transform, end, speed);
                    Destroy(vessel);
                }

                yield return new WaitForSeconds(
                    PasserGapMin + (float)passerRng.NextDouble() * (PasserGapMax - PasserGapMin));
            }
        }

        static GameObject PickPasser(PrefabDatabase.ZonePalette palette, System.Random source)
        {
            // Two-thirds small craft: a distant speedboat is period colour, a wall of cargo
            // ships is a blockade.
            var boats = palette.portBoats;
            if (boats != null && boats.Length > 0 && source.Next(3) != 0)
                return boats[source.Next(boats.Length)];

            return palette.portShips[source.Next(palette.portShips.Length)];
        }

        GameObject Moor(
            PrefabDatabase.ZonePalette palette, System.Random berthRng,
            Vector3 position, Quaternion yaw)
        {
            var prefab = palette.portShips[berthRng.Next(palette.portShips.Length)];
            return prefab ? Launch(prefab, position, yaw, berthRng, bobScale: 1f) : null;
        }

        /// <summary>
        /// A live vessel: bare root the glides move, model child carrying the bob - see
        /// ShipBob for why the two must be different transforms.
        /// </summary>
        GameObject Launch(
            GameObject prefab, Vector3 position, Quaternion yaw,
            System.Random source, float bobScale)
        {
            var root = new GameObject("port_ship_live");
            root.transform.SetParent(transform, false);
            root.transform.SetPositionAndRotation(position, yaw);

            var model = Instantiate(prefab, root.transform);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.AddComponent<ShipBob>()
                 .Configure((float)source.NextDouble() * 20f, bobScale);

            return root;
        }

        /// <summary>
        /// Constant-heading glide with eased ends. Rotation is NOT turned toward the target:
        /// a ship slides laterally into its berth crabwise, engines and tugs implied, which
        /// is how a berthing actually looks from above - a ship that yawed to point at the
        /// quay would read as ramming it.
        /// </summary>
        static IEnumerator Glide(Transform ship, Vector3 target, float speed)
        {
            if (!ship)
                yield break;

            var from = ship.position;
            var distance = (target - from).magnitude;
            if (distance < 0.01f)
                yield break;

            var duration = distance / speed;
            for (var t = 0f; t < duration; t += Time.deltaTime)
            {
                if (!ship)
                    yield break;

                ship.position = Vector3.Lerp(from, target, Mathf.SmoothStep(0f, 1f, t / duration));
                yield return null;
            }

            if (ship)
                ship.position = target;
        }

        // ------------------------------------------------------------------ the forklift

        /// <summary>Shared mutable flag between a berth cycle and its forklift loop.</summary>
        sealed class WorkingFlag { public bool Value; }

        /// <summary>
        /// One forklift per compound, shuttling the quay lane between the two work points
        /// nearest the berth while a ship is in, parked at the first of them while not.
        /// Registered as a pedestrian body so the shift walks around it rather than through.
        /// </summary>
        void SpawnForklift(PortMarker marker, System.Random berthRng, WorkingFlag working)
        {
            var prefab = ForkliftPrefab();
            if (!prefab)
                return;

            var ends = ShuttleEnds(marker);
            if (ends == null)
                return;

            var (parkAt, farEnd) = ends.Value;
            var forklift = Instantiate(prefab, parkAt,
                                       Quaternion.LookRotation(farEnd - parkAt, Vector3.up),
                                       transform);
            forklift.name = "port_forklift";

            var body = PedestrianRegistry.Register(forklift.transform);
            if (body != null)
                forkliftBodies.Add(body);
            StartCoroutine(ForkliftLoop(forklift.transform, body, parkAt, farEnd, working,
                                        new System.Random(berthRng.Next())));
        }

        GameObject ForkliftPrefab()
        {
            var palette = prefabs.PaletteFor(BlockZone.Port);
            if (palette?.parkedCars == null)
                return null;

            foreach (var bucket in palette.parkedCars)
            {
                if (bucket?.prefabs == null)
                    continue;
                foreach (var candidate in bucket.prefabs)
                    if (candidate && candidate.name.ToLowerInvariant().Contains("forklift"))
                        return candidate;
            }

            return null;
        }

        /// <summary>The two quay-lane work points nearest the berth - ship side and yard side.</summary>
        (Vector3, Vector3)? ShuttleEnds(PortMarker marker)
        {
            var points = new List<Vector3>();
            foreach (var point in marker.WorkPoints)
                if (point.Lane == PortMarker.WorkLane.Quay)
                    points.Add(point.Position);

            if (points.Count < 2)
                return null;

            points.Sort((a, b) =>
                (a - marker.BerthCentre).sqrMagnitude.CompareTo((b - marker.BerthCentre).sqrMagnitude));

            return (points[0], points[1]);
        }

        IEnumerator ForkliftLoop(
            Transform forklift, PedestrianBody body,
            Vector3 home, Vector3 far, WorkingFlag working, System.Random loopRng)
        {
            var poll = new WaitForSeconds(0.6f);
            var toFar = true;

            while (forklift)
            {
                if (!working.Value)
                {
                    if (body != null) { body.Stationary = true; body.SpeedMs = 0f; }
                    yield return poll;
                    continue;
                }

                var target = toFar ? far : home;
                toFar = !toFar;

                // Turn, then drive - a forklift pivots on the spot.
                var heading = target - forklift.position;
                heading.y = 0f;
                if (heading.sqrMagnitude > 0.01f)
                {
                    var wanted = Quaternion.LookRotation(heading, Vector3.up);
                    while (forklift && Quaternion.Angle(forklift.rotation, wanted) > 2f)
                    {
                        forklift.rotation = Quaternion.RotateTowards(
                            forklift.rotation, wanted, 220f * Time.deltaTime);
                        yield return null;
                    }
                }

                if (body != null)
                    body.Stationary = false;

                while (forklift)
                {
                    var remaining = target - forklift.position;
                    remaining.y = 0f;
                    if (remaining.magnitude < 0.3f)
                        break;

                    forklift.position += remaining.normalized
                                       * Mathf.Min(ForkliftSpeed * Time.deltaTime, remaining.magnitude);
                    if (body != null)
                        body.SpeedMs = ForkliftSpeed;
                    yield return null;
                }

                if (body != null) { body.Stationary = true; body.SpeedMs = 0f; }
                yield return new WaitForSeconds(
                    ForkliftPause + (float)loopRng.NextDouble() * ForkliftPause);
            }

            PedestrianRegistry.Unregister(body);
            forkliftBodies.Remove(body);
        }

        // ------------------------------------------------------------------ small maths

        static float Along(Vector3 point, Vector3 axis) => Vector3.Dot(point, axis);

        static float Range(Vector2 range, System.Random source) =>
            range.x + (float)source.NextDouble() * Mathf.Max(0f, range.y - range.x);
    }
}
