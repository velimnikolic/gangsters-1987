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
        public List<Decline> Declines => Runner.Declines;
        /// <summary>The campaign is over - the Don is dead. The scene edge presents
        /// it; the sim decided it.</summary>
        public bool Fallen => Runner.Fallen;
        public int FallenOnDay => Runner.FallenOnDay;

        public List<Incident> Incidents => Runner.Incidents;
        public List<Incident> LastNight => Runner.LastNight;
        public List<Incident> IncidentBook => Runner.IncidentBook;
        public Tribute Tribute => Runner.Tribute;
        public int Heat => Runner.Heat;

        /// <summary>The outfit's filing office - every organizational order the ledger
        /// asks for stands here until it is granted or refused. It lives on the
        /// director rather than on the book so a ruling still lands while the ledger is
        /// shut.</summary>
        public OutfitFilings Filings { get; } = new OutfitFilings();

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

            // The deed book covers the streamed city, where a building the camera left
            // has no marker to sweep. A deed whose view IS bound was already counted
            // above - its marker carries the same gang - so only the unbound ones add.
            Business.BusinessDeeds.Collect(deedScratch);
            for (var i = 0; i < deedScratch.Count; i++)
            {
                var deed = deedScratch[i];
                if (deed.Value.GangId < 0 ||
                    Business.BusinessViewBindings.TryGet(deed.Key, out _))
                    continue;
                into.Add(new Turf.Holding(deed.Value.GangId, deed.Value.LegacyBlockId));
            }
        }

        readonly List<KeyValuePair<
            Territory.TerritoryBusinessId, Business.BusinessDeeds.Deed>> deedScratch =
            new List<KeyValuePair<
                Territory.TerritoryBusinessId, Business.BusinessDeeds.Deed>>();

        /// <summary>The outfit's front - its headquarters. False until the gang layer
        /// has seated the families.</summary>
        public bool TryGetHeadquarters(out Vector3 position, out int blockId)
        {
            position = Vector3.zero;
            blockId = -1;
            var front = Gangs.GangRegistry.FrontBusinessOf(Gangs.GangCatalog.PlayerGangId);
            if (front)
            {
                position = front.transform.position;
                blockId = front.BlockId;
                return true;
            }

            // The planned city seats fronts as GangFront doors, not as marker
            // businesses (GangDirector never runs there). Without this fallback the
            // outfit had no address: every job's travel collapsed to the minimum.
            var fronts = RoadDemo.GangFront.All;
            for (var i = 0; i < fronts.Count; i++)
            {
                if (fronts[i] == null ||
                    fronts[i].GangId != Gangs.GangCatalog.PlayerGangId)
                    continue;
                position = fronts[i].Door;
                return true;
            }

            return false;
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

        /// <summary>The street reports the men standing at the address.</summary>
        public void ReportArrived(int jobId) => Runner.ReportArrived(jobId);

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
            // The office answers in REAL seconds and before the clock is read: a
            // filing has to be ruled on whether or not a day clock exists in the scene.
            if (Filings.Tick(Time.deltaTime))
                Version++;

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
                // The day's business money lands on the sheet BEFORE midnight closes
                // it, so a shop's dollars and the day it earned them agree.
                SettleBusinessDay();
                var paid = Runner.DayTick(roster);
                Version++;

                if (paid > 0)
                    Debug.Log("[Outfit] Payday - day " + Campaign.Day + " opens, " +
                              UI.LedgerText.Cash(paid) + " out of the safe.");
                if (Rises.Count > 0)
                    Debug.Log("[Outfit] Day " + Campaign.Day + " - " + Rises.Count +
                              " rise(s) on the books.");
                if (Declines.Count > 0)
                    Debug.Log("[Outfit] Day " + Campaign.Day + " - " + Declines.Count +
                              " man-year(s) caught up with somebody.");
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
            Runner.JobResolved = OnJobResolved;
            Runner.RosterMoved = () =>
            {
                if (PersonnelDirector.Instance)
                    PersonnelDirector.Instance.Touch();
            };
            // The one thing the sim cannot do for itself: say so. The rule that the
            // campaign is over is the runner's; announcing it is the scene's.
            Runner.BossFell += () =>
            {
                Debug.LogWarning("[Outfit] THE DON IS DEAD - day " + Campaign.Day +
                                 ". The outfit is finished; nothing advances from here.");
                Version++;
                if (PersonnelDirector.Instance)
                    PersonnelDirector.Instance.Touch();
            };
            Runner.OpenFirstSheet();
            Version++;
        }

        /// <summary>
        /// The day's take off the city's doors: a premises on our deed pays its net, a
        /// shop the racket holds Compliant pays its week's protection a seventh at a
        /// time. Booked once per campaign day onto the closing sheet - the settlement
        /// the Block File's figures always promised. EPIC 9's collection rounds will
        /// replace this flat settle with money that physically walks; until then the
        /// dollars are at least real.
        /// </summary>
        void SettleBusinessDay()
        {
            var business = Business.BusinessRuntime.Instance;
            if (business == null || !business.Populated)
                return;

            // Only premises the outfit OWNS settle at midnight - a deed's net is a
            // till a manager runs for you. Protection money never moves here: it sits
            // on the dues ledger until a crew physically walks the round and banks it
            // (ECON-004 · TerritoryRuntime.Collection).
            var legal = 0;
            var rows = Business.CityBusinesses.All;
            for (var i = 0; i < rows.Count; i++)
            {
                var id = rows[i].Id;
                if (Business.BusinessDeeds.GangOf(id) != Gangs.GangCatalog.PlayerGangId)
                    continue;
                if (business.Directory.TryGet(id, out var record))
                    legal += EconomyPrices.NetPerDay(record.Archetype);
            }

            if (legal == 0)
                return;

            Accounts.Safe += legal;
            var sheet = Accounts.Current;
            if (sheet != null)
                sheet.LegalIncome += legal;
            Version++;
        }

        /// <summary>
        /// A collection round reached the front (ECON-004/007): the take enters the
        /// safe and today's sheet as illegal income - the ONLY door protection money
        /// has into the books.
        /// </summary>
        public void BankCollection(int amount)
        {
            if (amount <= 0)
                return;
            Accounts.Safe += amount;
            var sheet = Accounts.Current;
            if (sheet != null)
                sheet.IllegalIncome += amount;
            Version++;
            Debug.Log("[Outfit] A round banked " + UI.LedgerText.Cash(amount) + ".");
        }

        /// <summary>
        /// What a finished job DID to the city. The campaign booked the money and the
        /// record; this is where its outcome lands on the world's own state - the deed
        /// book and the racket - through the seams those systems already own.
        /// </summary>
        void OnJobResolved(Job job, OrderOutcome outcome)
        {
            if (job == null || outcome != OrderOutcome.Completed ||
                string.IsNullOrEmpty(job.TargetBusinessId))
                return;

            var businessId = new Territory.TerritoryBusinessId(job.TargetBusinessId);
            switch (job.Type)
            {
                // The paperwork came back signed: the deed moves to the outfit, in the
                // simulation, so it survives the street being streamed out and back.
                case OrderType.BuyPremises:
                    Business.BusinessDeeds.SetGang(
                        businessId, Gangs.GangCatalog.PlayerGangId, job.TargetBlockId);
                    Version++;
                    break;

                // Violence that came off registers with the shop it landed on - the
                // RACK-011 seam - so a raided or smashed premises is frightened of the
                // family that did it, not merely poorer on the outfit's own sheet.
                case OrderType.Raid:
                    RoadDemo.TerritoryRuntime.Instance?.ResolveEscalation(
                        new Territory.TerritoryGangId(Gangs.GangCatalog.PlayerGangId),
                        businessId, Territory.TerritoryEscalationKind.Assault,
                        DoorOrders.ViolenceSeverity(job.Type));
                    break;

                case OrderType.SmashUp:
                    if (!ShutBusiness(
                            businessId, Business.BusinessShutdownCause.SmashUp))
                        break;
                    RoadDemo.TerritoryRuntime.Instance?.ResolveEscalation(
                        new Territory.TerritoryGangId(Gangs.GangCatalog.PlayerGangId),
                        businessId, Territory.TerritoryEscalationKind.PropertyDamage,
                        DoorOrders.ViolenceSeverity(job.Type));
                    // The wreck is VISIBLE: punched-out panes in the ground floor and
                    // their glass across the pavement. Fire damage remains the distinct
                    // burn-then-board presentation below.
                    RoadDemo.ShopDamage.SmashBusiness(businessId);
                    break;

                case OrderType.Torch:
                    if (!ShutBusiness(
                            businessId, Business.BusinessShutdownCause.Arson))
                        break;
                    RoadDemo.TerritoryRuntime.Instance?.ResolveEscalation(
                        new Territory.TerritoryGangId(Gangs.GangCatalog.PlayerGangId),
                        businessId, Territory.TerritoryEscalationKind.PropertyDamage,
                        DoorOrders.ViolenceSeverity(job.Type));
                    // And a torched one burns: the full ShopFire, then the boards.
                    RoadDemo.ShopDamage.ScorchBusiness(businessId);
                    break;

                case OrderType.Bomb:
                    RoadDemo.TerritoryRuntime.Instance?.ResolveEscalation(
                        new Territory.TerritoryGangId(Gangs.GangCatalog.PlayerGangId),
                        businessId, Territory.TerritoryEscalationKind.PropertyDamage,
                        DoorOrders.ViolenceSeverity(job.Type));
                    RoadDemo.ShopDamage.ScorchBusiness(businessId);
                    break;
            }
        }

        bool ShutBusiness(
            Territory.TerritoryBusinessId businessId,
            Business.BusinessShutdownCause cause)
        {
            var business = Business.BusinessRuntime.Instance;
            if (business?.Shutdowns == null)
                return false;
            return business.Shutdowns.Shut(
                businessId, cause, business.CurrentGameHour);
        }

        /// <summary>Pay an early repair through the same accounts purchase seam used by
        /// every other asset. Only the gang on the deed may spend this money.</summary>
        public OpResult RepairBusiness(Territory.TerritoryBusinessId businessId)
        {
            var business = Business.BusinessRuntime.Instance;
            if (business?.Shutdowns == null)
                return OpResult.Fail("the business simulation is not running");

            var refusal = Business.BusinessRepair.Try(
                business.Shutdowns,
                businessId,
                Gangs.GangCatalog.PlayerGangId,
                Business.BusinessDeeds.GangOf(businessId),
                business.CurrentGameHour,
                Accounts,
                out var charged);
            if (refusal != null)
                return OpResult.Fail(refusal);

            Version++;
            Debug.Log("[Outfit] Repaired " + businessId.Value + " for " +
                      UI.LedgerText.Cash(charged) + "; safe at " +
                      UI.LedgerText.Cash(Accounts.Safe) + ".");
            return OpResult.Success;
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

        /// <summary>
        /// The purchase gate's undo: a purchase whose second half failed after the money
        /// moved (a hire the roster then refused) is unbooked exactly as it was booked -
        /// the safe refilled AND the open day's Purchases line reduced, so the Finances
        /// page never shows a purchase that bought nothing.
        /// </summary>
        public void Refund(int price, string what)
        {
            if (price <= 0)
                return;

            Accounts.Safe += price;
            if (Accounts.Current != null)
                Accounts.Current.Purchases -= price;
            Version++;
            Debug.Log("[Outfit] Refunded " + UI.LedgerText.Cash(price) + " for " + what +
                      "; safe at " + UI.LedgerText.Cash(Accounts.Safe) + ".");
        }

        void Awake()
        {
            if (Instance && Instance != this)
            {
                // A second director would advance the clock and pay the wages twice over
                // the one shared roster; it stays a dead component, not a silent double.
                enabled = false;
                return;
            }
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
