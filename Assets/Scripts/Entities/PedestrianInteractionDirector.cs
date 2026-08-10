using System.Collections.Generic;
using UnityEngine;
using LivingCity.Data;
using LivingCity.Generation;

namespace LivingCity.Entities
{
    /// <summary>
    /// The one piece of pedestrian life that needs a bird's-eye view: pairing walkers into
    /// conversations. Everything else - bench sits, shop visits, idling - is a local roll
    /// each agent makes for itself; only "you two, talk to each other" requires somebody who
    /// can see both and command both on the same tick, or one member stops for a partner who
    /// never got the message.
    ///
    /// Also drives those local rolls: agents used to roll in their own Update, but at crowd
    /// scale ten thousand per-instance callbacks that almost all early-out are pure overhead,
    /// so the director walks the agent list in slices instead - every agent visited about
    /// once per RollInterval, a bounded handful per frame.
    ///
    /// Also the runtime index of the world's interaction points. Benches, shop doors and the
    /// street doors of the buildings themselves are baked into the saved scene as marker
    /// components (see BenchSeats / ShopEntrance / BuildingDoor); collected once at Start by
    /// FindObjectsByType, the SmokeStackSystem pattern - and bucketed onto a coarse grid,
    /// because "nearest bench within 9m" was a full scan per roll and the props never move.
    ///
    /// Doors carry one query the other two do not: TryPickAnyDoor, a uniform pick over the
    /// whole city rather than a neighbourhood one. It is what somebody who walked into a
    /// building comes back out of, and picking it globally is the point - the person you see
    /// leaving a door is not the one you watched go in.
    /// </summary>
    public sealed class PedestrianInteractionDirector : MonoBehaviour
    {
        public static PedestrianInteractionDirector Instance { get; private set; }

        [SerializeField] CityConfig config;

        /// <summary>Pair scan cadence. chatChance in CityConfig is per pair per tick-second.</summary>
        const float TickInterval = 1f;

        /// <summary>How close two walkers must pass to count as within earshot.</summary>
        const float ChatRange = 3f;

        /// <summary>
        /// Prop-grid cell side. One ring of cells must cover the agents' OpportunityRange
        /// (9m), so 16 keeps every query a 3x3 visit.
        /// </summary>
        const float PropCellSize = 16f;

        /// <summary>
        /// Tries at picking a free door anywhere in the city for somebody to come out of. A
        /// handful is plenty: doors outnumber the people indoors by a wide margin, so a busy
        /// one is a coincidence, not a queue - and failing simply means walking back out of
        /// the door they went in.
        /// </summary>
        const int ExitDoorTries = 6;

        BenchSeats[] benches = System.Array.Empty<BenchSeats>();
        ShopEntrance[] shops = System.Array.Empty<ShopEntrance>();
        BuildingDoor[] doors = System.Array.Empty<BuildingDoor>();
        readonly Dictionary<long, List<int>> benchCells = new Dictionary<long, List<int>>();
        readonly Dictionary<long, List<int>> shopCells = new Dictionary<long, List<int>>();
        readonly Dictionary<long, List<int>> doorCells = new Dictionary<long, List<int>>();

        readonly List<PedestrianAgent> candidates = new List<PedestrianAgent>();
        readonly List<Vector3> candidatePositions = new List<Vector3>();
        readonly List<(int a, int b)> pairs = new List<(int a, int b)>();

        System.Random rng;
        float nextTickAt;
        int rollCursor;

        Ambient.CityClock clock;
        float nextClockSearchAt;

        /// <summary>
        /// Whether a CityClock exists to drive the daily routine. Without one the schedule
        /// layer is simply inert and the crowd lives its old timeless life. Lazy with a 1s
        /// throttle rather than a one-shot Start lookup: a clock that enters the scene
        /// after this director (a rebuilt city, a runtime-installed rig) must wake the
        /// routine up, not leave it dead for the session - and when there genuinely is no
        /// clock, a hundred agents asking per second must not each pay a scene scan.
        /// </summary>
        public bool HasClock
        {
            get
            {
                if (clock)
                    return true;
                if (Time.time < nextClockSearchAt)
                    return false;

                nextClockSearchAt = Time.time + 1f;
                clock = FindAnyObjectByType<Ambient.CityClock>();
                return clock;
            }
        }

        /// <summary>The city's hour, read once here so ten thousand agents do not each
        /// hold a scene lookup. Callers gate on HasClock.</summary>
        public float HourNow => clock ? clock.Hour : 0f;

        public int DayNow => clock ? clock.Day : 0;

        void Awake()
        {
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        void Start()
        {
            // Self-heal the wiring, the CityClock pattern: the editor menu assigns config
            // into the saved scene, but this component is also installed at Play by
            // GameplayBootstrap into a scene the menu never touched - and with a null
            // config Update() returns forever and the whole interaction layer is silently
            // dead. Borrow the builder's config rather than sit there being useless.
            if (!config)
            {
                var builder = FindAnyObjectByType<Generation.CityBuilder>();
                if (builder)
                    config = builder.Config;
            }

            benches = FindObjectsByType<BenchSeats>(FindObjectsSortMode.None);
            shops = FindObjectsByType<ShopEntrance>(FindObjectsSortMode.None);
            doors = FindObjectsByType<BuildingDoor>(FindObjectsSortMode.None);

            // Sorted by world position before anything indexes into them, because
            // FindObjectsByType's order is whatever the scene graph felt like -
            // DoorByHash assigns homes and workplaces off the index, and the same seed
            // must house the same person at the same door every session. The ownership
            // layer learned this rule first: sort by position, never child order.
            System.Array.Sort(doors, (a, b) =>
            {
                if (!a || !b)
                    return (a ? 1 : 0) - (b ? 1 : 0);
                var pa = a.transform.position;
                var pb = b.transform.position;
                var byX = pa.x.CompareTo(pb.x);
                return byX != 0 ? byX : pa.z.CompareTo(pb.z);
            });

            clock = FindAnyObjectByType<Ambient.CityClock>();

            for (var i = 0; i < benches.Length; i++)
                if (benches[i])
                    CellFor(benchCells, benches[i].transform.position).Add(i);
            for (var i = 0; i < shops.Length; i++)
                if (shops[i])
                    CellFor(shopCells, shops[i].StandWorld).Add(i);
            for (var i = 0; i < doors.Length; i++)
                if (doors[i])
                    CellFor(doorCells, doors[i].StandWorld).Add(i);

            rng = new System.Random((config ? config.seed : 0) + SeedOffsets.PedestrianLife);
        }

        void Update()
        {
            if (!config || !config.pedestrianInteractions)
                return;

            TickRolls();

            if (Time.time < nextTickAt)
                return;

            nextTickAt = Time.time + TickInterval;
            PairChats();
        }

        /// <summary>
        /// The per-frame slice of opportunity rolls: enough agents that everyone is visited
        /// about once per RollInterval, whatever the population or frame rate.
        /// </summary>
        void TickRolls()
        {
            var agents = PedestrianAgent.Agents;
            if (agents.Count == 0)
                return;

            var perFrame = Mathf.Clamp(
                Mathf.CeilToInt(agents.Count * Time.deltaTime / PedestrianAgent.RollInterval),
                1, agents.Count);

            for (var i = 0; i < perFrame; i++)
            {
                rollCursor++;
                if (rollCursor >= agents.Count)
                    rollCursor = 0;

                var agent = agents[rollCursor];
                if (agent)
                    agent.TickOpportunities();
            }
        }

        void PairChats()
        {
            candidates.Clear();
            candidatePositions.Clear();

            foreach (var agent in PedestrianAgent.Agents)
            {
                if (!agent || !agent.AvailableForChat)
                    continue;
                candidates.Add(agent);
                candidatePositions.Add(agent.transform.position);
            }

            if (candidates.Count < 2)
                return;

            InteractionPairing.Pairs(candidatePositions, ChatRange, pairs);

            foreach (var (a, b) in pairs)
            {
                if (rng.NextDouble() >= config.chatChance * TickInterval)
                    continue;

                var argue = rng.NextDouble() < config.argueFraction;
                var duration = config.chatDurationRange.x
                    + (float)rng.NextDouble()
                    * Mathf.Max(0f, config.chatDurationRange.y - config.chatDurationRange.x);

                // Both commanded from here, same tick, each handed the OTHER's position to
                // face - the whole reason pairing is central.
                candidates[a].BeginConversation(candidatePositions[b], duration, argue);
                candidates[b].BeginConversation(candidatePositions[a], duration, argue);
            }
        }

        /// <summary>
        /// Nearest bench within range that still has a free seat - and the seat comes back
        /// CLAIMED, so between this call and the agent's Release nobody else can be assigned
        /// it. Single-threaded, so claim-then-walk needs no further locking.
        /// </summary>
        public bool TryClaimSeat(Vector3 near, float range, out BenchSeats bench, out int seat)
        {
            bench = null;
            seat = -1;

            var bestSq = range * range;
            var cx = Mathf.FloorToInt(near.x / PropCellSize);
            var cz = Mathf.FloorToInt(near.z / PropCellSize);

            for (var dx = -1; dx <= 1; dx++)
            for (var dz = -1; dz <= 1; dz++)
            {
                if (!benchCells.TryGetValue(Key(cx + dx, cz + dz), out var cell))
                    continue;

                for (var i = 0; i < cell.Count; i++)
                {
                    var candidate = benches[cell[i]];
                    if (!candidate || !candidate.HasFreeSeat)
                        continue;

                    var delta = candidate.transform.position - near;
                    if (Mathf.Abs(delta.y) > 2f)
                        continue;

                    delta.y = 0f;
                    if (delta.sqrMagnitude > bestSq)
                        continue;

                    bench = candidate;
                    bestSq = delta.sqrMagnitude;
                }
            }

            return bench && bench.TryClaim(out seat);
        }

        public bool TryPickShop(Vector3 near, float range, out ShopEntrance shop)
        {
            shop = null;

            var bestSq = range * range;
            var cx = Mathf.FloorToInt(near.x / PropCellSize);
            var cz = Mathf.FloorToInt(near.z / PropCellSize);

            for (var dx = -1; dx <= 1; dx++)
            for (var dz = -1; dz <= 1; dz++)
            {
                if (!shopCells.TryGetValue(Key(cx + dx, cz + dz), out var cell))
                    continue;

                for (var i = 0; i < cell.Count; i++)
                {
                    var candidate = shops[cell[i]];
                    if (!candidate)
                        continue;

                    var delta = candidate.StandWorld - near;
                    if (Mathf.Abs(delta.y) > 2f)
                        continue;

                    delta.y = 0f;
                    if (delta.sqrMagnitude > bestSq)
                        continue;

                    shop = candidate;
                    bestSq = delta.sqrMagnitude;
                }
            }

            return shop;
        }

        /// <summary>
        /// The nearest street door with a free doorstep. Same 3x3 prop-grid visit as the
        /// shopfront query - doors are far denser than shopfronts, but they are indexed the
        /// same way and the range is the same 9m detour, so one ring still covers it.
        /// </summary>
        public bool TryPickDoor(Vector3 near, float range, out BuildingDoor door)
        {
            door = null;

            var bestSq = range * range;
            var cx = Mathf.FloorToInt(near.x / PropCellSize);
            var cz = Mathf.FloorToInt(near.z / PropCellSize);

            for (var dx = -1; dx <= 1; dx++)
            for (var dz = -1; dz <= 1; dz++)
            {
                if (!doorCells.TryGetValue(Key(cx + dx, cz + dz), out var cell))
                    continue;

                for (var i = 0; i < cell.Count; i++)
                {
                    var candidate = doors[cell[i]];
                    if (!candidate || !candidate.StepFree)
                        continue;

                    var delta = candidate.StandWorld - near;
                    if (Mathf.Abs(delta.y) > 2f)
                        continue;

                    delta.y = 0f;
                    if (delta.sqrMagnitude > bestSq)
                        continue;

                    door = candidate;
                    bestSq = delta.sqrMagnitude;
                }
            }

            return door;
        }

        /// <summary>
        /// A free door anywhere in the city, for somebody indoors to come out of. Flat and
        /// uniform over the whole array on purpose: this is the one thing that keeps the
        /// crowd from settling into whichever streets it happened to start on, and any bias
        /// here would slowly drain the city into one district.
        /// </summary>
        public bool TryPickAnyDoor(System.Random rng, out BuildingDoor door)
        {
            door = null;
            if (doors.Length == 0 || rng == null)
                return false;

            for (var attempt = 0; attempt < ExitDoorTries; attempt++)
            {
                var candidate = doors[rng.Next(doors.Length)];
                if (!candidate || !candidate.StepFree)
                    continue;

                door = candidate;
                return true;
            }

            return false;
        }

        /// <summary>
        /// A door picked by hash - the home and workplace assignment. Deterministic per
        /// city because the array is position-sorted at Start; a walker's hash lands on
        /// the same door every session of the same seed. Null only when the city has no
        /// doors at all.
        /// </summary>
        public BuildingDoor DoorByHash(int hash) =>
            doors.Length == 0 ? null : doors[(int)((uint)hash % (uint)doors.Length)];

        static List<int> CellFor(Dictionary<long, List<int>> cells, Vector3 position)
        {
            var key = Key(Mathf.FloorToInt(position.x / PropCellSize),
                          Mathf.FloorToInt(position.z / PropCellSize));
            if (!cells.TryGetValue(key, out var cell))
                cells[key] = cell = new List<int>();
            return cell;
        }

        static long Key(int cx, int cz) => ((long)cx << 32) | (uint)cz;
    }
}
