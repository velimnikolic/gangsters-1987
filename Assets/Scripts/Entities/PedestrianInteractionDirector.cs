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
    /// Also the runtime index of the world's interaction points. Benches and shop doors are
    /// baked into the saved scene as marker components (see BenchSeats / ShopEntrance);
    /// collected once at Start by FindObjectsByType, the SmokeStackSystem pattern.
    /// </summary>
    public sealed class PedestrianInteractionDirector : MonoBehaviour
    {
        public static PedestrianInteractionDirector Instance { get; private set; }

        [SerializeField] CityConfig config;

        /// <summary>Pair scan cadence. chatChance in CityConfig is per pair per tick-second.</summary>
        const float TickInterval = 1f;

        /// <summary>How close two walkers must pass to count as within earshot.</summary>
        const float ChatRange = 3f;

        BenchSeats[] benches = System.Array.Empty<BenchSeats>();
        ShopEntrance[] shops = System.Array.Empty<ShopEntrance>();

        readonly List<PedestrianAgent> candidates = new List<PedestrianAgent>();
        readonly List<Vector3> candidatePositions = new List<Vector3>();
        readonly List<(int a, int b)> pairs = new List<(int a, int b)>();

        System.Random rng;
        float nextTickAt;

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
            benches = FindObjectsByType<BenchSeats>(FindObjectsSortMode.None);
            shops = FindObjectsByType<ShopEntrance>(FindObjectsSortMode.None);
            rng = new System.Random((config ? config.seed : 0) + SeedOffsets.PedestrianLife);
        }

        void Update()
        {
            if (!config || !config.pedestrianInteractions || Time.time < nextTickAt)
                return;

            nextTickAt = Time.time + TickInterval;
            PairChats();
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
            foreach (var candidate in benches)
            {
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

            return bench && bench.TryClaim(out seat);
        }

        public bool TryPickShop(Vector3 near, float range, out ShopEntrance shop)
        {
            shop = null;

            var bestSq = range * range;
            foreach (var candidate in shops)
            {
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

            return shop;
        }
    }
}
