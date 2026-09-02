using System.Collections.Generic;
using UnityEngine;
using LivingCity.Personnel;
using LivingCity.Territory;

namespace LivingCity.Gameplay
{
    /// <summary>
    /// The scene's host of HOUSE 0 - the player's own book. It no longer deals a roster:
    /// the <see cref="Outfit.Underworld"/> deals all twenty-one at once and this class
    /// adopts the player's, then routes every mutation the almanac makes through thin
    /// wrappers that bump <see cref="Version"/> on success - the dirty key the UI
    /// repaints on, same convention as OverlayRegistry and PropertyRegistry.
    ///
    /// The UI never calls RosterOps directly: a mutation that skipped this class would
    /// change the books without moving Version, and the almanac would sit on a stale page
    /// until the next unrelated click. Routing everything here is what makes the
    /// versioned-repaint convention safe rather than merely customary.
    ///
    /// The rules themselves live one layer down in <see cref="Outfit.HouseOps"/>, where
    /// a rival's mind reaches them through the same call. Nothing here is a rule the
    /// player has and a family does not.
    /// </summary>
    public sealed class PersonnelDirector : MonoBehaviour
    {
        public static PersonnelDirector Instance { get; private set; }

        [Header("Outfit organization")]
        [SerializeField] OrganizationCapacityConfig organizationCapacity =
            new OrganizationCapacityConfig();

        public Roster Roster { get; private set; }
        public int Version { get; private set; }
        public IOrganizationQuery Organization => organizationQuery;

        /// <summary>What a signature costs, through this door and every other one
        /// (<see cref="Outfit.EconomyPrices.RecruitSigning"/>). There used to be a
        /// cheaper price over the counter than out on the corner; there is one price
        /// now, and it is not the ledger's to set.</summary>
        public int HoodRecruitmentCost => Outfit.EconomyPrices.RecruitSigning;

        /// <summary>The player's own book, out of the underworld's twenty-one.</summary>
        public Outfit.House House { get; private set; }

        readonly HashSet<TerritoryBlockId> knownOrganizationBlocks =
            new HashSet<TerritoryBlockId>();
        OrganizationQuery organizationQuery;
        IOrganizationPhysicalSource physicalSource;

        /// <summary>The city seed the roster was dealt from - the ledger's newspaper
        /// prints its editions off the same number, so the paper is as deterministic
        /// as the men it writes about.</summary>
        public int Seed => seed;

        void Awake()
        {
            if (Instance && Instance != this)
                return;
            Instance = this;
            organizationQuery = new OrganizationQuery();
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        // Static state outlives Play when domain reload is off - same fix as OverlayRegistry.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay() => Instance = null;

        void Start()
        {
            // The city builder has usually dealt the underworld already (its Awake runs
            // a frame before this); when it has not - the standalone Ledger, a bench
            // scene - this is the call that deals it. Either way there is one deal.
            seed = UnderworldHost.SeedForScene(quiet: false, context: this);
            House = Outfit.Underworld.Ensure(seed).Player;
            Roster = House.Roster;

            // The one thing still the player's own: the capacity knob on this component.
            // The deal gave every house the canonical limits; a scene that overrides
            // them overrides them for the outfit, which is what an inspector field on
            // the player's own director means.
            RosterOps.ConfigureOrganization(Roster,
                organizationCapacity?.Snapshot() ?? OrganizationLimits.Default);
            organizationQuery ??= new OrganizationQuery();
            organizationQuery.Bind(Roster);
            organizationQuery.BindPhysical(physicalSource);
            Version++;
        }

        int seed = UnderworldHost.FallbackSeed;

        /// <summary>
        /// THE DON IS ON HIS OWN STREET. The street stands a body for every crew in the
        /// books and the Boss led none, so the one man the whole game is about was the
        /// one man not in the city. His detail is that crew (Bodyguards: the detail is a
        /// Crew whose lieutenant IS the Boss), so standing it up on day one puts him
        /// outside his own front with the lieutenants - selectable, orderable, and able
        /// to be taken inside the headquarters like any other crew (CrewQuarters).
        ///
        /// And the men who already answer directly to him fall in behind him, so he does
        /// not walk out of his own front alone (Bodyguards.FallIn). Who guards him after
        /// that stays the player's decision - a thin detail is what lets a round reach
        /// him (RANK-003) - and this only gives that decision somewhere to happen.
        ///
        /// Not in RosterSeeder: the seeded roster is a fixture the pure tests measure
        /// (one crew, one lieutenant, six men), and the Don taking the field is the
        /// GAME's arrangement rather than a change to the books he starts with. Every
        /// house's Don gets it in <see cref="Outfit.Underworld.Deal"/> now; this stays
        /// for the debug roster, which deals a new book under a standing director.
        /// </summary>
        void StandTheBossUp() => Bodyguards.FallIn(Roster);

        /// <summary>
        /// Swaps in the sixty-man scale roster (F2 in the almanac). Debug-only by
        /// nature but shipped in the build path on purpose: the ledger is specified to
        /// stay usable at sixty men, and a reviewer must be able to see that in Play
        /// without editor wiring. Deterministic off the same city seed.
        /// </summary>
        public void DebugSeedLarge(int memberCount)
        {
            if (House == null)
                return;
            // Under the HOUSE, not beside it: a book swapped here and nowhere else
            // would leave the ledger reading sixty men while every rule wrote into the
            // seven the house still held.
            House.Restock(RosterSeeder.GenerateLarge(seed, memberCount));
            Roster = House.Roster;
            RosterOps.ConfigureOrganization(Roster,
                organizationCapacity?.Snapshot() ?? OrganizationLimits.Default);
            StandTheBossUp();
            RosterOps.NormalizeArms(Roster);
            organizationQuery ??= new OrganizationQuery();
            organizationQuery.Bind(Roster);
            organizationQuery.BindPhysical(physicalSource);
            Version++;
            Debug.Log("[Personnel] Debug roster: " + memberCount + " men on the books.");
        }

        // ------------------------------------------------------------------ mutations

        /// <summary>
        /// The ledger's HIRE A MAN: the signing money out of the house's own safe, then
        /// one randomized Hood in the unassigned pool reporting directly to the Boss.
        /// The UI never receives a mutable Character.
        ///
        /// The rule is <see cref="Outfit.HouseOps.Recruit"/> - the same call at the same
        /// price a rival's mind makes.
        /// </summary>
        public OpResult RecruitHood(out int newId)
        {
            newId = -1;
            if (House == null || Roster == null)
                return OpResult.Fail(LivingCity.UI.LedgerText.ReasonNoSuchMember);

            var result = Outfit.HouseOps.Recruit(House, out var member);
            if (!result.Ok)
                return result;

            newId = member.Id;
            return Commit(result, "recruited and left available for assignment", member.Id);
        }

        // ------------------------------------------------------------ the classified

        /// <summary>ONE newspaper in town. The column belongs to the underworld, not
        /// to this director: a man who advertises this morning advertises to every
        /// family, and the first house to sign him takes him off the page for all of
        /// them.</summary>
        Outfit.HireMarket Market =>
            Outfit.Underworld.Current?.Column ??
            Outfit.Underworld.Ensure(seed).Column;

        /// <summary>
        /// This morning's classified column, set for the campaign day the outfit is
        /// standing in. A METHOD and not a property because it does work the first time
        /// it is asked each day: the paper only has to exist when somebody opens it, and
        /// dealing it costs a name draw and eleven rolls per ad that no frame should pay
        /// for a page nobody turned to.
        /// </summary>
        public Outfit.HireMarket ColumnToday()
        {
            var day = OutfitDirector.Instance != null
                ? OutfitDirector.Instance.Campaign.Day
                : 1;
            var column = Market;
            column.EnsureDealt(Roster, seed, day);
            return column;
        }

        /// <summary>
        /// Signs a man out of the newspaper: his signing money out of the safe through
        /// the outfit's one purchase gate, then onto the books with a crew of his own -
        /// the column advertises lieutenants, and a lieutenant is a man with a crew.
        ///
        /// The ad comes off the column BEFORE the money moves and goes back on if the
        /// safe refuses: <see cref="Outfit.HireMarket.Take"/> answers false for an ad
        /// already gone, which is what keeps a double click from paying twice for one
        /// man - and a refused purchase from quietly losing him off the page.
        /// </summary>
        public OpResult HireFromAd(Outfit.HireAd ad, out int newId)
        {
            newId = -1;
            if (Roster == null || ad == null || ad.Man == null)
                return OpResult.Fail(LivingCity.UI.LedgerText.ReasonNoSuchMember);

            // Off the column before a dollar moves - and if he is already gone, nothing
            // was paid and nothing is refunded.
            var price = ad.Down;
            var column = Market;
            if (!column.Take(ad))
                return OpResult.Fail(LivingCity.UI.LedgerText.ReasonNoSuchMember);

            var outfit = OutfitDirector.Instance;
            if (outfit != null)
            {
                var paid = outfit.Purchase(price, "a man out of the paper");
                if (!paid.Ok)
                {
                    // The money was refused, so the ad was never taken: put it back in
                    // the column rather than quietly losing the man off the page.
                    column.Restore(ad);
                    return paid;
                }
            }

            // He comes on as a hood for exactly as long as it takes to promote him:
            // RosterOps.Promote is the ONE door a crew forms through, and a crew that
            // formed any other way would be a crew the roster's rules never saw.
            var man = ad.Man;
            man.Id = Roster.NextCharacterId();
            man.Rank = Rank.Hood;
            Roster.Members.Add(man);
            Career.Joined(man, Roster.Day, "the classified column");

            var ask = ad.Daily;
            var result = RosterOps.Promote(Roster, man.Id, out _, Feed);
            if (!result.Ok)
            {
                // The roster refused him after the safe paid: the man goes back on the
                // page and the money goes back where it came from, Purchases line included.
                Roster.Members.Remove(man);
                man.Id = -1;
                column.Restore(ad);
                if (outfit != null)
                    outfit.Refund(price, "a man out of the paper");
                return result;
            }

            // The bargain, re-stamped AFTER the promotion: a new rank is a new bargain
            // and RosterOps.Promote tears the old one up (WAGE-002), so the price the
            // paper quoted has to be written back on him here or he would quietly drop
            // onto the house scale the moment he signed. The figure is the ad's own
            // Daily, read before the promotion, so the column and the books agree to
            // the dollar.
            man.WageAsked = ask;

            newId = man.Id;
            return Commit(result, "signed out of the paper", man.Id);
        }

        /// <summary>
        /// Marks the roster changed by a hand that is not one of the wrappers below -
        /// the day tick's rises and discharges, which are the outfit's business and not
        /// a click. The alternative is routing every strategic mutation through this
        /// class as a wrapper of its own, which would put the campaign calendar's
        /// vocabulary into the personnel director for no gain.
        /// </summary>
        public void Touch() => Version++;

        public PromoteCheck CheckPromote(int id) =>
            Roster == null
                ? new PromoteCheck(false, false, LivingCity.UI.LedgerText.ReasonNoSuchMember)
                : RosterOps.CheckPromote(Roster, id);

        /// <summary>
        /// The day's feed, when there is a campaign running. A promotion and a
        /// demotion are both events the paper carries and both go on the man's own
        /// file, and neither has anywhere to be written in a demo scene with no
        /// campaign in it - hence the null, which the roster rules accept.
        /// </summary>
        static List<Incident> Feed => OutfitDirector.Instance != null
            ? OutfitDirector.Instance.Incidents
            : null;

        /// <summary>
        /// The day's record of every trait that moved and why. Same reasoning as
        /// <see cref="Feed"/>: a nudge made from a click is a fact about the man, and
        /// it belongs on the campaign's own list beside the ones the midnight pass
        /// makes - a succession, a transfer and a demotion must not be the three
        /// movements nobody can account for.
        /// </summary>
        static List<PersonalityChange> Changes => OutfitDirector.Instance != null
            ? OutfitDirector.Instance.Runner.CharacterChanges
            : null;

        public OpResult Promote(int id, out int newCrewId)
        {
            newCrewId = -1;
            return House == null
                ? OpResult.Fail(LivingCity.UI.LedgerText.ReasonNoSuchMember)
                : Commit(Outfit.HouseOps.Promote(House, id, out newCrewId, Feed),
                    "promoted", id);
        }

        public OpResult Demote(int id) =>
            House == null
                ? OpResult.Fail(LivingCity.UI.LedgerText.ReasonNoSuchMember)
                : Commit(Outfit.HouseOps.Demote(House, id, Feed), "demoted", id);

        /// <summary>The street reports a man shot dead: struck through, his gear pooled,
        /// his crew passed on. Version moves so every book and bar re-deals.</summary>
        public OpResult Kill(int id) =>
            House == null
                ? OpResult.Fail(LivingCity.UI.LedgerText.ReasonNoSuchMember)
                : Commit(Outfit.HouseOps.Kill(House, id, Changes), "shot dead", id);

        /// <summary>The street reports a man who ran from the fight and kept running:
        /// struck off as a deserter, his gear pooled, his post passed on.</summary>
        public OpResult Desert(int id) =>
            House == null
                ? OpResult.Fail(LivingCity.UI.LedgerText.ReasonNoSuchMember)
                : Commit(Outfit.HouseOps.Desert(House, id, "", 0, Changes), "deserted", id);

        public OpResult AssignToPool(int id) =>
            House == null
                ? OpResult.Fail(LivingCity.UI.LedgerText.ReasonNoSuchMember)
                : Commit(Outfit.HouseOps.AssignToPool(House, id), "sent to the pool", id);

        public OpResult AssignToFront(int id) =>
            House == null
                ? OpResult.Fail(LivingCity.UI.LedgerText.ReasonNoSuchMember)
                : Commit(Outfit.HouseOps.AssignToFront(House, id), "put on the front", id);

        public OpResult AssignToCrew(int id, int crewId)
        {
            if (House == null)
                return OpResult.Fail(LivingCity.UI.LedgerText.ReasonNoSuchMember);
            return Commit(Outfit.HouseOps.AssignToCrew(House, id, crewId, Changes),
                "reassigned", id);
        }

        /// <summary>How a crew runs its rounds (ECON-005) - the player's one lever
        /// over collection. A word on the branch card, cycled there.</summary>
        public OpResult SetCrewPolicy(int crewId, CrewPolicy policy)
        {
            if (House == null)
                return OpResult.Fail(LivingCity.UI.LedgerText.ReasonNoSuchMember);
            var result = Outfit.HouseOps.SetPolicy(House, crewId, policy);
            if (result.Ok)
                Touch();
            return result;
        }

        public OpResult AssignToBoss(int id, int bossId)
        {
            if (House == null || bossId != Roster.BossId)
                return OpResult.Fail(LivingCity.UI.LedgerText.ReasonNoBoss);
            return Commit(Outfit.HouseOps.AssignToBoss(House, id),
                "assigned directly to the boss", id);
        }

        // ------------------------------------------------------------- the detail

        /// <summary>
        /// The men who stand between the Boss and the street (RANK-003). Not a second
        /// structure: the detail is a Crew the Boss leads himself, so putting a man on
        /// it is the ordinary crew assignment and every rule that already governs a
        /// crew - his cap, his wages, the street's follow - governs this one unchanged.
        /// </summary>
        public OpResult AssignToDetail(int id)
        {
            if (Roster == null)
                return OpResult.Fail(LivingCity.UI.LedgerText.ReasonNoSuchMember);

            var detail = Bodyguards.FormDetail(Roster);
            if (detail == null)
                return OpResult.Fail(LivingCity.UI.LedgerText.ReasonNoSuchMember);

            return Commit(RosterOps.AssignToCrew(Roster, id, detail.Id),
                "put on the Boss's detail", id);
        }

        /// <summary>The Boss's detail, or null while nobody stands with him.</summary>
        public Crew BodyguardDetail() => Bodyguards.DetailOf(Roster);

        // ------------------------------------------------------------- the bargain

        /// <summary>
        /// Yes to a man who asked for the rate (PSY-003). His envelope moves to what he
        /// asked and the asking stops - the one answer that closes a pay gap for good.
        /// </summary>
        public OpResult GrantRaise(int id) =>
            Apply(RosterOps.GrantRaise, id, "granted the rate he asked for");

        /// <summary>
        /// No. He draws what he drew and he remembers being told: the demand clears,
        /// his loyalty takes the hit, and the underpaid clock is NOT reset - the ladder
        /// goes on from where it was.
        /// </summary>
        public OpResult RefuseRaise(int id)
        {
            if (Roster == null)
                return OpResult.Fail(LivingCity.UI.LedgerText.ReasonNoSuchMember);

            // The nudge is a fact about the man, and the feed prints facts: route it
            // onto the campaign's own change list so the ledger and the wire say the
            // same thing about why his loyalty moved.
            return Commit(RosterOps.RefuseRaise(Roster, id, Changes),
                "was refused the rate", id);
        }

        public OpResult AssignToLieutenant(int id, int lieutenantId)
        {
            if (Roster == null)
                return OpResult.Fail(LivingCity.UI.LedgerText.ReasonNoSuchMember);
            var lieutenant = Roster.Find(lieutenantId);
            var crew = lieutenant != null ? Roster.CrewOf(lieutenantId) : null;
            if (lieutenant == null || lieutenant.Rank != Rank.Lieutenant ||
                crew == null || crew.LieutenantId != lieutenantId)
                return OpResult.Fail(LivingCity.UI.LedgerText.ReasonInvalidCommandParent);
            return Commit(RosterOps.AssignToCrew(Roster, id, crew.Id),
                "transferred in the command chain", id);
        }

        /// <summary>The geography authority registers the canonical IDs it owns. The
        /// organization keeps responsibility only for IDs in this catalogue.</summary>
        public void RegisterOrganizationBlocks(IEnumerable<TerritoryBlockId> blockIds)
        {
            knownOrganizationBlocks.Clear();
            if (blockIds != null)
                foreach (var blockId in blockIds)
                    if (blockId.IsValid)
                        knownOrganizationBlocks.Add(blockId);
            Version++;
        }

        public OpResult AssignBlockResponsibility(TerritoryBlockId blockId, int leaderId)
        {
            if (Roster == null)
                return OpResult.Fail(LivingCity.UI.LedgerText.ReasonNoSuchMember);
            return Commit(
                RosterOps.AssignBlockResponsibility(
                    Roster, blockId, leaderId, knownOrganizationBlocks.Contains(blockId)),
                "made responsible for " + blockId, leaderId);
        }

        public OpResult RemoveBlockResponsibility(
            TerritoryBlockId blockId, int expectedLeaderId = -1)
        {
            if (Roster == null)
                return OpResult.Fail(LivingCity.UI.LedgerText.ReasonNoSuchMember);
            var result = RosterOps.RemoveBlockResponsibility(
                Roster, blockId, expectedLeaderId);
            if (result.Ok)
            {
                Version++;
                Debug.Log("[Personnel] Responsibility removed from " + blockId + ".");
            }
            return result;
        }

        public void SetOrganizationPhysicalSource(IOrganizationPhysicalSource source)
        {
            if (ReferenceEquals(physicalSource, source))
                return;
            physicalSource = source;
            organizationQuery ??= new OrganizationQuery(Roster);
            organizationQuery.BindPhysical(source);
        }

        public void ValidateOrganization(List<string> failures)
        {
            OrganizationValidator.Validate(
                Roster, knownOrganizationBlocks, physicalSource, failures);
        }

        public OpResult GiveEquipment(int itemId, int id)
        {
            if (Roster == null)
                return OpResult.Fail(LivingCity.UI.LedgerText.ReasonNoSuchMember);
            return Commit(RosterOps.GiveEquipment(Roster, itemId, id), "armed", id);
        }

        /// <summary>The street's GIVE: this item to this lieutenant, whoever had it
        /// before (RosterOps.MoveEquipment). The crew that lost it closes ranks in the
        /// same normalize pass, and the version bump is what puts the car outside the
        /// front under its new colours.</summary>
        public OpResult MoveEquipment(int itemId, int id)
        {
            if (Roster == null)
                return OpResult.Fail(LivingCity.UI.LedgerText.ReasonNoSuchItem);
            return Commit(RosterOps.MoveEquipment(Roster, itemId, id), "handed the keys", id);
        }

        /// <summary>The purchase path's roster half - the OutfitDirector's Purchase
        /// gate has already moved the money by the time this runs.</summary>
        public RosterEquipment AddEquipment(Personnel.EquipmentKind kind,
            string displayName, int value)
        {
            if (Roster == null)
                return null;

            var item = RosterOps.AddEquipment(Roster, kind, displayName, value);
            Version++;
            Debug.Log("[Personnel] " + displayName + " added to the armory.");
            return item;
        }

        /// <summary>The front card's GIVE: gear into the headquarters locker, dealt
        /// out to the desk's guards by the normalize pass right after.</summary>
        public OpResult GiveEquipmentToFront(int itemId)
        {
            if (Roster == null)
                return OpResult.Fail(LivingCity.UI.LedgerText.ReasonNoSuchItem);
            var result = RosterOps.GiveEquipmentToFront(Roster, itemId);
            if (result.Ok)
            {
                RosterOps.NormalizeArms(Roster);
                Version++;
                Debug.Log("[Personnel] Gear dumped at the front.");
            }
            return result;
        }

        public OpResult ReturnEquipment(int itemId)
        {
            if (Roster == null)
                return OpResult.Fail(LivingCity.UI.LedgerText.ReasonNoSuchItem);
            var result = RosterOps.ReturnEquipment(Roster, itemId);
            if (result.Ok)
            {
                // The crew the item left closes ranks over what remains.
                RosterOps.NormalizeArms(Roster);
                Version++;
            }
            return result;
        }

        OpResult Apply(System.Func<Roster, int, OpResult> op, int id, string verb)
        {
            if (Roster == null)
                return OpResult.Fail(LivingCity.UI.LedgerText.ReasonNoSuchMember);
            return Commit(op(Roster, id), verb, id);
        }

        OpResult Commit(OpResult result, string verb, int id)
        {
            if (!result.Ok)
                return result;

            // Every successful mutation can move men or guns across crew lines -
            // the lieutenants re-deal their crews' arms before the ledger repaints.
            RosterOps.NormalizeArms(Roster);
            Version++;
            var member = Roster.Find(id);
            Debug.Log("[Personnel] " + (member != null ? member.FullName : "#" + id) +
                      " " + verb + ".");
            return result;
        }
    }
}
