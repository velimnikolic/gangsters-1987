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

            var underworld = Underworld.Ensure(UnderworldHost.SeedForScene());
            house = underworld.Player;
            runner = house.Runner;

            // The city sweep is CITY-WIDE - every gang's buildings, keyed by gang - so
            // one reading serves all twenty-one books, and each runner reads it against
            // its own GangId. Wiring it only to house zero left the rival runners
            // looking at an empty city, which is how a defector from house seven ended
            // up walking through the lowest id on the table.
            for (var gangId = 0; gangId < underworld.Count; gangId++)
            {
                var other = underworld.Of(gangId);
                if (other != null && other.Runner != null && other.Runner.HoldingsOf == null)
                    other.Runner.HoldingsOf = CollectHoldings;
            }

            runner.DistanceOf = DistanceFromHeadquarters;
            runner.JobResolved = OnJobResolved;
            runner.RosterMoved = () =>
            {
                if (PersonnelDirector.Instance)
                    PersonnelDirector.Instance.Touch();
            };
            runner.BossFell += () =>
            {
                Debug.LogWarning("[Outfit] " + Outfit.EndingText.Headline(Runner.Ending) +
                                 " - day " + Campaign.Day +
                                 ". The outfit is finished; nothing advances from here.");
                Version++;
                if (PersonnelDirector.Instance)
                    PersonnelDirector.Instance.Touch();
            };
            runner.OpenFirstSheet();
        }

        public Campaign Campaign => Runner.Campaign;
        public Accounts Accounts => Runner.Accounts;
        /// <summary>Where every house stands with every other - the city's one book,
        /// not the player's own.</summary>
        public HouseRelations Relations =>
            Underworld.Current != null ? Underworld.Current.Relations : null;

        /// <summary>Where WE stand with them, for the pages that only ever ask about
        /// the player's own side of a pair.</summary>
        public Stance StanceWith(int gangId) =>
            Relations != null
                ? Relations.StanceBetween(Gangs.GangCatalog.PlayerGangId, gangId)
                : Stance.Peace;

        public bool TryGetPendingStance(int gangId, out Stance stance)
        {
            stance = Stance.Peace;
            return Relations != null &&
                   Relations.TryGetPending(
                       Gangs.GangCatalog.PlayerGangId, gangId, out stance);
        }
        public OrderBook Book => Runner.Book;
        public List<OrderRecord> Records => Runner.Records;
        public List<Improvement> Rises => Runner.Rises;
        public List<Decline> Declines => Runner.Declines;
        /// <summary>The campaign is over. The scene edge presents it; the sim decided
        /// it, and <see cref="Ending"/> says which of the three ends it was.</summary>
        public bool Fallen => Runner.Fallen;
        public int FallenOnDay => Runner.FallenOnDay;
        public Outfit.OutfitEnding Ending => Runner.Ending;

        public List<Incident> Incidents => Runner.Incidents;
        public List<Incident> LastNight => Runner.LastNight;
        public List<Incident> IncidentBook => Runner.IncidentBook;

        /// <summary>FOLLOW-001. What the crews have to say - every character movement
        /// the sim made, folded by man and by day, newest day last.</summary>
        public List<ReasonLine> ReasonBook => Runner.ReasonBook;

        /// <summary>FOLLOW-002. Every lieutenant who went over and whose door he walked
        /// through.</summary>
        public List<DefectionRecord> Defections => Runner.Defections;
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
                blockId = fronts[i].BlockId;
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

        /// <summary>
        /// File work with the office.
        ///
        /// <paramref name="announce"/> is the voice, and it is on by default because this
        /// is where an order is filed from every sheet in the ledger - the order book, the
        /// block file, the door menu - and the desk answering is what tells the player the
        /// thing was taken. The street card turns it OFF: a crew standing at the shop says
        /// its own line in its lieutenant's voice, and the consigliere agreeing over the
        /// top of him is two answers to one click.
        /// </summary>
        public OpResult IssueOrder(Job job, bool announce = true)
        {
            if (job != null)
                job.GangId = Gangs.GangCatalog.PlayerGangId;
            Adopt();
            var result = Commit(Underworld.Current.Issue(job));
            if (announce && result.Ok && job != null)
                CrewVoice.Office(Data.VoiceLines.ForOrder(job.Type));
            return result;
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
            RegisterHeadquartersArmory();

            // The office answers in REAL seconds and before the clock is read: a
            // filing has to be ruled on whether or not a day clock exists in the scene.
            // The ledger deliberately pauses scaled world time while it is open, but
            // its filing desk must still answer without advancing the city simulation.
            if (Filings.Tick(Time.unscaledDeltaTime))
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
                // WHAT THE FLATS ASKED FOR (EPIC 27). The nightly pass is pure, so the
                // two things it cannot do itself are carried out here, at the outfit's
                // one scene edge: the heat a room put on its block, and the precinct
                // taking its keeper away.
                ApplyFlatNight();
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

                // THE DAY IS WRITTEN DOWN (D19). Every campaign midnight, over the same
                // file - a player who loses an afternoon to a crash loses an afternoon,
                // not a campaign. A refusal is logged and nothing else stops.
                var refusal = Save.CampaignSave.Write(Save.CampaignSave.AutosavePath);
                if (!string.IsNullOrEmpty(refusal))
                    Debug.LogWarning("[Outfit] The autosave did not write: " + refusal);
            }
        }

        /// <summary>
        /// The flats' half of the night that needs a city. The heat goes onto the block
        /// the building actually stands on, through the territory runtime's own pool -
        /// never a second heat number kept here - and a collared keeper goes into a cell
        /// through the same RosterOps door every other arrest uses.
        /// </summary>
        void ApplyFlatNight()
        {
            // EVERY HOUSE'S FLATS (EPIC 40, PRE-001), the way Underworld.DayTick is a
            // sweep: a rival's raided room jails its keeper, heats its block and reaches
            // the paper exactly as ours does. The player's house moves Version; a
            // rival's moves its own.
            var underworld = Underworld.Current;
            var runtime = RoadDemo.TerritoryRuntime.Instance;
            for (var g = 0; underworld != null && g < underworld.Count; g++)
            {
                var one = underworld.Of(g);
                if (one == null || one.Finished || one.Runner == null)
                    continue;
                var report = one.Runner.Flats;
                if (report == null)
                    continue;

                for (var i = 0; i < report.Heat.Count; i++)
                {
                    var deposit = report.Heat[i];
                    if (runtime == null ||
                        !Property.ApartmentBuildings.TryGet(deposit.Building, out var building))
                        continue;
                    runtime.AddPoliceAttention(building.CanonicalBlockId, deposit.Heat);
                }

                for (var i = 0; i < report.Raids.Count; i++)
                {
                    var raid = report.Raids[i];
                    if (raid.KeeperId < 0)
                        continue;
                    // The keeper is taken. The flat is already sealed by the pure
                    // pass; this is the man, through the same door a street collar uses.
                    var keeper = one.Roster?.Find(raid.KeeperId);
                    RoadDemo.PressDesk.Instance?.FlatRaid(raid, keeper, one.GangId);
                    Personnel.RosterOps.ClearKeeper(one.Roster, raid.KeeperId);
                    Personnel.RosterOps.Jail(one.Roster, raid.KeeperId,
                        one.Runner.Campaign.Day + Property.FlatDay.SealedDays);
                    Property.Apartments.SetKeeper(raid.Unit, -1);
                    one.Runner.RosterMoved?.Invoke();
                }

                if (report.Raids.Count > 0 || report.Heat.Count > 0)
                {
                    if (one.IsPlayer)
                        Version++;
                    else
                        one.Touch();
                }
            }

            // THE STREET'S MIDNIGHT (EPIC 40, PRE-002): one pass, every house, after
            // the books and the flats - the card is dealt at midnight and shown at the
            // six o'clock cut once the paper has closed.
            if (runtime != null && runtime.RunStreetEvents() > 0)
                Version++;
        }

        void RegisterHeadquartersArmory()
        {
            var personnel = PersonnelDirector.Instance;
            var runtime = RoadDemo.TerritoryRuntime.Instance;
            if (personnel == null)
                return;
            if (runtime?.Geography == null)
            {
                // Scene replacement tears the city edge down before rebuilding it.
                // During that gap yesterday's block must not remain a usable armory.
                personnel.ClearHeadquartersArmoryBlock();
                return;
            }

            if (TryGetHeadquarters(out _, out var legacy) && legacy >= 0 &&
                runtime.TryGetBlock(legacy, out var blockId))
            {
                personnel.SetHeadquartersArmoryBlock(blockId);
                return;
            }

            var fronts = RoadDemo.GangFront.All;
            for (var i = 0; i < fronts.Count; i++)
            {
                var front = fronts[i];
                if (front == null || front.GangId != Gangs.GangCatalog.PlayerGangId ||
                    !front.BusinessId.IsValid ||
                    !runtime.Geography.TryGetBusinessBlock(front.BusinessId, out blockId))
                    continue;
                personnel.SetHeadquartersArmoryBlock(blockId);
                return;
            }

            // No current front resolves to the city. Do not leave yesterday's address
            // accepting transfers after a scene/front replacement.
            personnel.ClearHeadquartersArmoryBlock();
        }

        // ------------------------------------------------------------------- the rest

        /// <summary>
        /// WAR IS DECLARED, TRUCE AND PEACE ARE OFFERED (EPIC 42, DIPL-002). A war goes
        /// pending for midnight as it always did; a truce or a peace is a proposal to
        /// the other house through the same door a mind's is, answered at its desk
        /// at once - a stance the player could impose alone was never diplomacy.
        /// </summary>
        public OpResult SetStance(int gangId, Stance stance)
        {
            if (gangId == Gangs.GangCatalog.PlayerGangId)
                return OpResult.Fail(UI.LedgerText.ReasonOwnOutfit);

            if (Relations == null)
                return OpResult.Fail(UI.LedgerText.ReasonFinanceUnavailable);
            if (stance == Stance.War)
            {
                Relations.SetPending(Gangs.GangCatalog.PlayerGangId, gangId, stance);
                Version++;
                return OpResult.Success;
            }

            var world = Underworld.Current;
            if (world?.Player == null)
                return OpResult.Fail(UI.LedgerText.ReasonFinanceUnavailable);
            var filed = HouseOps.Propose(world, world.Player, new Proposal
            {
                To = gangId,
                Kind = stance == Stance.Truce
                    ? ProposalKind.OfferTruce
                    : ProposalKind.OfferPeace,
            }, HouseOps.Look);
            Version++;
            return filed;
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

                BalanceMath.Receive(owner.Runner.Accounts, net, MoneyKind.Clean);
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

        /// <summary>A recovered ground bag reaches the same safe through the Jobs line.</summary>
        public void BankTake(int amount)
        {
            if (amount <= 0)
                return;
            Runner.BankTake(amount);
            Version++;
            Debug.Log("[Outfit] A fallen bag banked " + UI.LedgerText.Cash(amount) + ".");
        }

        /// <summary>
        /// What a finished job DID to the city. The campaign booked the money and the
        /// record; this is where its outcome lands on the world's own state - the deed
        /// book and the racket - through the seams those systems already own.
        /// </summary>
        void OnJobResolved(Job job, OrderOutcome outcome)
        {
            if (job == null || outcome != OrderOutcome.Completed)
                return;

            // The house that ORDERED it answers for it - the deed goes in their name,
            // the escalation is filed against them. This used to be the player's name
            // whoever gave the order.
            var whose = new Territory.TerritoryGangId(job.GangId);

            // A KILL NAMES A MAN, not a place (D16). It comes back before the rest,
            // because everything below is about a door.
            if (job.Type == OrderType.Kill && job.TargetCharacterId >= 0)
            {
                StrikeHimOff(job, whose);
                return;
            }

            // THEY HAVE HIM (RIVAL-009 step 6) is the books' own business since EPIC
            // 42 DIPL-005: CampaignRunner.Finish hands a completed kidnap to
            // Underworld.TakeHim, which takes the man, files the grudge and puts the
            // ransom on the table - so the paper city sees it too.
            if (job.Type == OrderType.Kidnap)
            {
                Version++;
                return;
            }

            // A STREET LOOKED OVER (RIVAL-009). Explore brings back what is on a
            // block - its doors and its fronts - for the house that sent the men, and
            // for nobody else. It is aimed at ground, not at a door.
            if (job.Type == OrderType.Explore)
            {
                Learned(job, whose);
                return;
            }

            if (string.IsNullOrEmpty(job.TargetBusinessId))
                return;

            var businessId = new Territory.TerritoryBusinessId(job.TargetBusinessId);
            switch (job.Type)
            {
                // The paperwork came back signed: the deed moves to the outfit, in the
                // simulation, so it survives the street being streamed out and back.
                case OrderType.BuyPremises:
                    Business.BusinessDeeds.SetGang(
                        businessId, whose.Value, job.TargetBlockId);
                    RoadDemo.PressDesk.Instance?.PremisesBought(businessId, whose.Value);
                    Version++;
                    break;

                // Violence that came off registers with the shop it landed on - the
                // RACK-011 seam - so a raided or smashed premises is frightened of the
                // family that did it, not merely poorer on the outfit's own sheet.
                case OrderType.Raid:
                    if (RoadDemo.TerritoryRuntime.Instance?.ResolveEscalation(
                        whose, businessId, Territory.TerritoryEscalationKind.Assault,
                        DoorOrders.ViolenceSeverity(job.Type)) == true)
                        RoadDemo.PressDesk.Instance?.BusinessAssault(
                            businessId, whose.Value);
                    break;

                case OrderType.SmashUp:
                    if (!ShutBusiness(
                            businessId, Business.BusinessShutdownCause.SmashUp))
                        break;
                    RoadDemo.TerritoryRuntime.Instance?.ResolveEscalation(
                        whose, businessId, Territory.TerritoryEscalationKind.PropertyDamage,
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
                        whose, businessId, Territory.TerritoryEscalationKind.PropertyDamage,
                        DoorOrders.ViolenceSeverity(job.Type));
                    // And a torched one burns: the full ShopFire, then the boards.
                    RoadDemo.ShopDamage.ScorchBusiness(businessId);
                    break;

                // POWDER SHUTS A SHOP FOR A WEEK (D12). Until now a bomb frightened the
                // street and left the shop trading, which made it a louder Torch with
                // none of the point.
                case OrderType.Bomb:
                    if (!ShutBusiness(businessId, Business.BusinessShutdownCause.Bomb))
                        break;
                    RoadDemo.TerritoryRuntime.Instance?.ResolveEscalation(
                        whose, businessId, Territory.TerritoryEscalationKind.PropertyDamage,
                        DoorOrders.ViolenceSeverity(job.Type));
                    RoadDemo.ShopDamage.ScorchBusiness(businessId);
                    break;

                // THE MAN, NOT THE WINDOWS. The telephone comes first while the beaten
                // proprietor's own standing is still the fact being read; then the
                // street remembers the assault, and only then does the counter go dark
                // for a day. A zero-price person closure cannot replace older premises
                // damage (BusinessShutdownLedger owns that rule).
                case OrderType.Beating:
                {
                    var runtime = RoadDemo.TerritoryRuntime.Instance;
                    var deed = RoadDemo.WitnessWatch.DeedForBeating(
                        businessId.Value, whose.Value);
                    runtime?.RingAbout(whose, businessId, deed, indoors: true);
                    runtime?.ResolveEscalation(
                        whose, businessId, Territory.TerritoryEscalationKind.Assault,
                        2.5f, Territory.TerritoryFearVisibility.Public,
                        Territory.TerritoryDoorNews.OwnerBeaten);
                    ShutBusiness(businessId, Business.BusinessShutdownCause.Beating);
                    break;
                }

                // The shot and its dead witnesses were already put onto StreetAlarm by
                // the strict inside callback. This is the persistent aftermath: three
                // dark days and the next deterministic proprietor behind the same door.
                case OrderType.KillOwner:
                    ShutBusiness(businessId, Business.BusinessShutdownCause.Death);
                    Business.BusinessRuntime.Instance?.AdvanceOwner(businessId);
                    Version++;
                    break;
            }
        }

        /// <summary>
        /// THE MAN IS STRUCK OFF (D16). It happened on paper - nobody's body was met -
        /// so the book does what the street would have: he is dead in HIS OWN family's
        /// roster, his street hears the killing, and the family that lost him holds it
        /// against the family that ordered it.
        /// </summary>
        void StrikeHimOff(Job job, Territory.TerritoryGangId whose)
        {
            var underworld = Underworld.Current;
            if (underworld == null)
                return;

            for (var g = 0; g < underworld.Count; g++)
            {
                var house = underworld.Of(g);
                var man = house?.Roster?.Find(job.TargetCharacterId);
                if (man == null || man.Gone)
                    continue;

                RoadDemo.PressDesk.Instance?.PaperKilling(
                    man, house.GangId, new Vector3(job.TargetX, 0f, job.TargetZ));
                HouseOps.Kill(house, man.Id);

                // His street hears it, and it is the ordering family's name on it.
                if (job.TargetBlockId >= 0)
                    RoadDemo.TerritoryRuntime.Instance?.RecordKilling(
                        whose, new Vector3(job.TargetX, 0f, job.TargetZ));

                if (house.GangId != whose.Value)
                {
                    underworld.Relations.Note(
                        house.GangId, whose.Value, GrievanceKind.ManKilled,
                        house.Runner.Campaign.Day);
                    house.Runner.NoteLoss(whose.Value);
                }
                Version++;
                return;
            }
        }

        /// <summary>What the men brought back: every door on that block, and every
        /// family front standing on it, now known to the house that sent them.</summary>
        void Learned(Job job, Territory.TerritoryGangId whose)
        {
            var runtime = RoadDemo.TerritoryRuntime.Instance;
            if (runtime?.Geography == null || !whose.IsValid)
                return;

            var blockId = runtime.BlockOfLegacy(job.TargetBlockId);
            if (!blockId.IsValid)
                return;

            var here = runtime.Geography.BusinessesOf(blockId);
            for (var i = 0; i < here.Count; i++)
                RoadDemo.TurfKnowledge.LearnDoor(here[i].BusinessId.Value, whose.Value);

            var fronts = RoadDemo.GangFront.All;
            for (var i = 0; i < fronts.Count; i++)
            {
                var front = fronts[i];
                if (front == null || !front.BusinessId.IsValid)
                    continue;
                if (runtime.Geography.TryGetBusinessBlock(
                        front.BusinessId, out var frontBlock) && frontBlock == blockId)
                    RoadDemo.TurfKnowledge.Learn(front, whose.Value);
            }

            Version++;
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
            return Purchase(price, what, out _);
        }

        public OpResult Purchase(int price, string what, out int dirtyPart)
        {
            var result = HouseOps.Purchase(House, price, out dirtyPart);
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
        public void Refund(int price, int dirtyPart, string what)
        {
            if (price <= 0)
                return;

            HouseOps.Refund(House, price, dirtyPart);
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
