using System.Collections.Generic;
using UnityEngine;
using LivingCity.Outfit;
using LivingCity.Personnel;

namespace LivingCity.Gameplay
{
    /// <summary>
    /// The scene's one owner of the outfit's strategic state. Since the game went
    /// realtime the RULES of that state live in <see cref="CampaignRunner"/>, which is
    /// pure and headlessly testable like everything else in the Outfit namespace; what
    /// is left here is the four things only a scene can answer: what time it is, where
    /// the headquarters stands, what to write in the console, and when the ledger must
    /// repaint.
    ///
    /// Same contract as PersonnelDirector: the UI reads through this class and mutates
    /// through its wrappers, which bump <see cref="Version"/> - the dirty key the ledger
    /// repaints on. A mutation that skipped the director would change the books without
    /// moving Version and the page would sit stale.
    ///
    /// There is no commit button and no turn. A player who issues nothing still watches
    /// his wages fall due, which is the pressure the whole design runs on.
    /// </summary>
    public sealed class OutfitDirector : MonoBehaviour
    {
        public static OutfitDirector Instance { get; private set; }

        /// <summary>The campaign itself. Public so the headless suite and the ledger
        /// read the same object the director drives.</summary>
        public CampaignRunner Runner { get; } = new CampaignRunner();

        public Campaign Campaign => Runner.Campaign;
        public Accounts Accounts => Runner.Accounts;
        public GangRelations Relations => Runner.Relations;
        public OrderBook Book => Runner.Book;
        public List<OrderRecord> Records => Runner.Records;
        public List<Improvement> Rises => Runner.Rises;
        public Tribute Tribute => Runner.Tribute;
        public int Heat => Runner.Heat;

        public int Version { get; private set; }

        /// <summary>Game-hours the clock read last frame, as one number (day × 24 +
        /// hour) so a day rollover is not a special case in the subtraction.</summary>
        float lastClockHours;
        bool clockRead;

        /// <summary>
        /// The live holdings, one entry per gang-held BUILDING, read straight off the
        /// markers - BusinessMarker.GangId is the single source of ownership, and day
        /// one GangDirector stamps exactly one front premise per family. Derived on
        /// every call: nothing seeds and no cache can go stale when the takeover layer
        /// starts flipping GangId building by building.
        /// </summary>
        public void CollectHoldings(List<Turf.Holding> into)
        {
            into.Clear();
            foreach (var business in PropertyRegistry.Businesses)
                if (business && business.GangId >= 0)
                    into.Add(new Turf.Holding(business.GangId, business.BlockId));
        }

        /// <summary>The outfit's front - its headquarters. False until the gang layer
        /// has seated the families.</summary>
        public bool TryGetHeadquarters(out Vector3 position, out int blockId)
        {
            position = Vector3.zero;
            blockId = -1;
            var front = Gangs.GangRegistry.FrontBusinessOf(Gangs.GangCatalog.PlayerGangId);
            if (!front)
                return false;
            position = front.transform.position;
            blockId = front.BlockId;
            return true;
        }

        /// <summary>Metres from headquarters to a job's door - the one worldly fact the
        /// campaign cannot work out for itself, handed to it as a function.</summary>
        float DistanceFromHeadquarters(Job job)
        {
            if (job == null || !job.HasPlace || !TryGetHeadquarters(out var hq, out _))
                return 0f;
            var dx = job.TargetX - hq.x;
            var dz = job.TargetZ - hq.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        // ------------------------------------------------------------------- issuing

        public OpResult IssueOrder(Job job) => Commit(Runner.Issue(RosterOrNull(), job));

        public OpResult CancelOrder(int jobId) => Commit(Runner.Cancel(RosterOrNull(), jobId));

        public OpResult MoveOrder(int jobId, int direction) =>
            Commit(Runner.Move(jobId, direction));

        public void ReportStreetOutcome(int jobId, OrderOutcome outcome) =>
            Runner.ReportStreetOutcome(jobId, outcome);

        OpResult Commit(OpResult result)
        {
            if (result.Ok)
                Version++;
            return result;
        }

        static Roster RosterOrNull() =>
            PersonnelDirector.Instance ? PersonnelDirector.Instance.Roster : null;

        bool seedTaken;

        /// <summary>
        /// Takes the city seed the rolls are dealt from - once, and NOT in Start. Both
        /// directors are seated by the same installer, so which Start runs first is
        /// undefined; reading the seed in ours could catch PersonnelDirector's fallback
        /// 42 instead of the city's own number, and every campaign on every seed would
        /// then have rolled the same way. A dealt roster is the proof its Start has run.
        /// </summary>
        void TakeSeed()
        {
            if (seedTaken || !PersonnelDirector.Instance ||
                PersonnelDirector.Instance.Roster == null)
                return;
            Runner.Seed = PersonnelDirector.Instance.Seed;
            seedTaken = true;
        }

        // --------------------------------------------------------------------- clock

        void Update()
        {
            var clock = Ambient.DayClock.Current;
            if (clock == null)
                return;

            var now = clock.Day * Campaign.HoursPerDay + clock.Hour;
            if (!clockRead)
            {
                lastClockHours = now;
                clockRead = true;
                return;
            }

            var elapsed = now - lastClockHours;
            lastClockHours = now;
            // A clock that went backwards was reset, not rewound - the demo rebuilds
            // its day/night stack on a rebuild. Swallow the step rather than paying a
            // negative hour into every job in the book.
            if (elapsed <= 0f)
                return;

            var roster = RosterOrNull();
            TakeSeed();
            if (Runner.AdvanceHours(roster, elapsed))
                Version++;

            // Clock days are 0-based and the campaign's are 1-based; a while rather
            // than an if so a frame that swallowed several days (a rebuild, a long
            // editor stall) still runs every day's books instead of skipping them.
            var today = clock.Day + 1;
            while (Campaign.Day < today)
            {
                var paid = Runner.DayTick(roster);
                Version++;

                if (paid > 0)
                    Debug.Log("[Outfit] Payday - day " + Campaign.Day + " opens, " +
                              UI.LedgerText.Cash(paid) + " out of the safe.");
                if (Rises.Count > 0)
                    Debug.Log("[Outfit] Day " + Campaign.Day + " - " + Rises.Count +
                              " rise(s) on the books.");
            }
        }

        // ------------------------------------------------------------------- the rest

        /// <summary>Stores the change as pending - stances turn over at midnight, never
        /// under a plan the player is still reading.</summary>
        public OpResult SetStance(int gangId, Stance stance)
        {
            if (gangId == Gangs.GangCatalog.PlayerGangId)
                return OpResult.Fail(UI.LedgerText.ReasonOwnOutfit);

            Relations.SetPending(gangId, stance);
            Version++;
            return OpResult.Success;
        }

        void Start()
        {
            Runner.DistanceOf = DistanceFromHeadquarters;
            Runner.HoldingsOf = CollectHoldings;
            Runner.RosterMoved = () =>
            {
                if (PersonnelDirector.Instance)
                    PersonnelDirector.Instance.Touch();
            };
            Runner.OpenFirstSheet();
            Version++;
        }

        /// <summary>
        /// The one purchase gate: refuses with the shortfall spelled out, or moves the
        /// money and books it on the open week's Purchases line - so the Armory click
        /// and the Finances row can never disagree.
        /// </summary>
        public OpResult Purchase(int price, string what)
        {
            var refusal = BalanceMath.TryPurchase(Accounts, price);
            if (refusal != null)
                return OpResult.Fail(refusal);

            Version++;
            Debug.Log("[Outfit] Bought " + what + " for " + UI.LedgerText.Cash(price) +
                      "; safe at " + UI.LedgerText.Cash(Accounts.Safe) + ".");
            return OpResult.Success;
        }

        void Awake()
        {
            if (Instance && Instance != this)
                return;
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        // Static state outlives Play when domain reload is off - same fix as OverlayRegistry.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay() => Instance = null;
    }
}
