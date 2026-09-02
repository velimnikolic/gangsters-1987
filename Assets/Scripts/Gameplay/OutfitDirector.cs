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

        /// <summary>
        /// The player's own campaign - house 0's runner out of the underworld's
        /// twenty-one. Public so the headless suite and the ledger read the same object
        /// the director drives.
        ///
        /// Asked for rather than owned: the first read deals the underworld if nobody
        /// has (the city builder normally has, a frame earlier) and wires this
        /// director's callbacks onto the player's runner. There is never a second,
        /// stand-in campaign for a page to read by mistake.
        /// </summary>
        public CampaignRunner Runner
        {
            get
            {
                Adopt();
                return runner;
            }
        }

        CampaignRunner runner;

        /// <summary>The player's house. Everything below that used to say "the outfit"
        /// says "house 0" now, and every rival has one of these.</summary>
        public House House
        {
            get
            {
                Adopt();
                return house;
            }
        }

        House house;

        /// <summary>
        /// Takes hold of house 0 - once. The seed comes from the one derivation
        /// (<see cref="UnderworldHost.SeedForScene"/>) so this director and the
        /// personnel director can never deal two different underworlds, whichever of
        /// their Starts runs first.
        /// </summary>
        void Adopt()
        {
            if (house != null)
                return;

            house = Underworld.Ensure(UnderworldHost.SeedForScene()).Player;
            runner = house.Runner;

            runner.DistanceOf = DistanceFromHeadquarters;
            runner.HoldingsOf = CollectHoldings;
            runner.JobResolved = OnJobResolved;
            runner.RosterMoved = () =>
            {
                if (PersonnelDirector.Instance)
                    PersonnelDirector.Instance.Touch();
            };
            runner.BossFell += () =>
            {
                Debug.LogWarning("[Outfit] THE DON IS DEAD - day " + Campaign.Day +
                                 ". The outfit is finished; nothing advances from here.");
                Version++;
                if (PersonnelDirector.Instance)
                    PersonnelDirector.Instance.Touch();
            };
            runner.OpenFirstSheet();
        }

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
        public void CollectHoldings(List<Turf.Holding> into) => Holdings(into, false);

        /// <summary>
        /// The same sweep, minus the doors the player has not seen. THE PAGE reads this
        /// one - the turf map, the rail, the FAMILIES card - because a deed is public
        /// the moment it is written and a rival's premises is meant to stay a rumour
        /// until a crew of ours has stood outside it (DoorHolder.Learned).
        ///
        /// The sim reads the other one: what a house actually holds is not a matter of
        /// what the player has noticed.
        /// </summary>
        public void CollectKnownHoldings(List<Turf.Holding> into) => Holdings(into, true);

        void Holdings(List<Turf.Holding> into, bool knownOnly)
        {
            into.Clear();
            foreach (var business in PropertyRegistry.Businesses)
                if (business && business.GangId >= 0 &&
                    (!knownOnly ||
                     DoorHolder.Learned(business.BusinessId, business.GangId)))
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
                if (knownOnly && !DoorHolder.Learned(deed.Key, deed.Value.GangId))
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

        public OpResult IssueOrder(Job job)
        {
            if (job != null)
                job.GangId = Gangs.GangCatalog.PlayerGangId;
            Adopt();
            return Commit(Underworld.Current.Issue(job));
        }

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

            // EVERY house works its book for those hours, not only the player's. One
            // sweep, one rule; whose orders they were is the only difference there is.
            Adopt();
            if (Underworld.Current.AdvanceHours(elapsed))
                Version++;

            // Clock days are 0-based and the campaign's are 1-based; a while rather
            // than an if so a frame that swallowed several days (a rebuild, a long
            // editor stall) still runs every day's books instead of skipping them.
            var today = clock.Day + 1;
            while (Campaign.Day < today)
            {
                var stood = Campaign.Day;
                // The day's business money lands on the sheet BEFORE midnight closes
                // it, so a shop's dollars and the day it earned them agree.
                SettleBusinessDay();
                var paid = Underworld.Current.DayTick();
                Version++;

                // A campaign that is over turns no more pages (CampaignRunner's
                // CampaignOver), and a clock that has run past a dead outfit must not
                // be caught up to for ever.
                if (Campaign.Day == stood)
                    break;

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
            // Reading the property is what takes hold of house 0 and wires the
            // callbacks onto its runner - see Adopt. Nothing else belongs here.
            Adopt();
            Version++;
        }

        /// <summary>
        /// The day's take off the city's doors, for EVERY house: a premises on a
        /// family's deed pays that family's safe its net. Booked once per campaign day
        /// onto the closing sheet - the settlement the Block File's figures always
        /// promised. Protection money never moves here: it sits on the dues ledger
        /// until a crew physically walks the round and banks it
        /// (ECON-004 · TerritoryRuntime.Collection).
        ///
        /// One pass over the city's doors rather than twenty-one: the deed says whose
        /// it is, and the money goes to that house's own safe.
        /// </summary>
        void SettleBusinessDay()
        {
            var business = Business.BusinessRuntime.Instance;
            if (business == null || !business.Populated)
                return;

            var underworld = Underworld.Current;
            if (underworld == null)
                return;

            var ours = 0;
            var rows = Business.CityBusinesses.All;
            for (var i = 0; i < rows.Count; i++)
            {
                var id = rows[i].Id;
                var owner = underworld.Of(Business.BusinessDeeds.GangOf(id));
                if (owner == null || owner.Extinct)
                    continue;
                if (!business.Directory.TryGet(id, out var record))
                    continue;

                var net = EconomyPrices.NetPerDay(record.Archetype);
                if (net <= 0)
                    continue;

                owner.Runner.Accounts.Safe += net;
                var sheet = owner.Runner.Accounts.Current;
                if (sheet != null)
                    sheet.LegalIncome += net;
                owner.Touch();
                if (owner.IsPlayer)
                    ours += net;
            }

            if (ours > 0)
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
            Runner.BankCollection(amount);
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
            // The house that ORDERED it answers for it - the deed goes in their name,
            // the escalation is filed against them. This used to be the player's name
            // whoever gave the order.
            var whose = new Territory.TerritoryGangId(job.GangId);
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
            var result = HouseOps.Purchase(House, price);
            if (!result.Ok)
                return result;

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

            HouseOps.Refund(House, price);
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
