using UnityEngine;
using LivingCity.Generation;
using LivingCity.Personnel;
using RoadDemo;

namespace LivingCity.Gameplay
{
    /// <summary>
    /// The scene's one owner of the outfit's roster. Seeds it from the city's seed at
    /// Start, then routes every mutation the almanac (and later, the weekly order system)
    /// makes through thin wrappers that bump <see cref="Version"/> on success - the dirty
    /// key the UI repaints on, same convention as OverlayRegistry and PropertyRegistry.
    ///
    /// The UI never calls RosterOps directly: a mutation that skipped this class would
    /// change the books without moving Version, and the almanac would sit on a stale page
    /// until the next unrelated click. Routing everything here is what makes the
    /// versioned-repaint convention safe rather than merely customary.
    /// </summary>
    public sealed class PersonnelDirector : MonoBehaviour
    {
        public static PersonnelDirector Instance { get; private set; }

        /// <summary>The scene the demo scenes get: no CityBuilder, no config, still a
        /// deterministic roster rather than a null one.</summary>
        const int FallbackSeed = 42;

        public Roster Roster { get; private set; }
        public int Version { get; private set; }

        /// <summary>The city seed the roster was dealt from - the ledger's newspaper
        /// prints its editions off the same number, so the paper is as deterministic
        /// as the men it writes about.</summary>
        public int Seed => seed;

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

        void Start()
        {
            var builder = FindAnyObjectByType<CityBuilder>();
            var roadDemo = FindAnyObjectByType<RoadDemoBuilder>();
            seed = builder && builder.Config ? builder.Config.seed
                : roadDemo ? roadDemo.BuiltFromSeed
                : FallbackSeed;
            // In the standalone Ledger menu the missing city is the DESIGN, not a
            // fault - the warning would cry wolf on every single Play there.
            if ((!builder || !builder.Config) && !roadDemo &&
                !FindAnyObjectByType<UI.LedgerMenuScene>())
                Debug.LogWarning("[Personnel] No city generator in the scene - the " +
                                 "roster runs on the fallback seed.", this);

            Roster = RosterSeeder.Generate(seed);
            RosterOps.NormalizeArms(Roster);
            Version++;
        }

        int seed = FallbackSeed;

        /// <summary>
        /// Swaps in the sixty-man scale roster (F2 in the almanac). Debug-only by
        /// nature but shipped in the build path on purpose: the ledger is specified to
        /// stay usable at sixty men, and a reviewer must be able to see that in Play
        /// without editor wiring. Deterministic off the same city seed.
        /// </summary>
        public void DebugSeedLarge(int memberCount)
        {
            Roster = RosterSeeder.GenerateLarge(seed, memberCount);
            RosterOps.NormalizeArms(Roster);
            Version++;
            Debug.Log("[Personnel] Debug roster: " + memberCount + " men on the books.");
        }

        // ------------------------------------------------------------------ mutations

        /// <summary>What a new man costs to bring in - the signing money, before wages.</summary>
        public const int RecruitPrice = 500;

        System.Random recruitRng;

        /// <summary>
        /// Brings a new hood onto the books and straight into this crew: the money
        /// through the outfit's one purchase gate (refused with the shortfall spelled
        /// out), the man dealt by RosterSeeder off the city seed, the crew's cap
        /// respected before a dollar moves. The street bar's empty chip.
        /// </summary>
        public OpResult Recruit(int crewId, out int newId)
        {
            newId = -1;
            if (Roster == null)
                return OpResult.Fail(LivingCity.UI.LedgerText.ReasonNoSuchCrew);
            var crew = Roster.FindCrew(crewId);
            if (crew == null)
                return OpResult.Fail(LivingCity.UI.LedgerText.ReasonNoSuchCrew);
            if (crew.HoodIds.Count >= Crew.MaxHoods)
                return OpResult.Fail(LivingCity.UI.LedgerText.ReasonCrewFull);

            var outfit = OutfitDirector.Instance;
            if (outfit != null)
            {
                var paid = outfit.Purchase(RecruitPrice, "a new man");
                if (!paid.Ok)
                    return paid;
            }

            recruitRng ??= new System.Random(seed * 31 + 7);
            var member = RosterSeeder.Recruit(Roster, recruitRng);
            newId = member.Id;
            return Commit(RosterOps.AssignToCrew(Roster, member.Id, crewId), "recruited", member.Id);
        }

        // ------------------------------------------------------------ the classified

        readonly Outfit.HireMarket market = new Outfit.HireMarket();

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
            market.EnsureDealt(Roster, seed, day);
            return market;
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
            if (!market.Take(ad))
                return OpResult.Fail(LivingCity.UI.LedgerText.ReasonNoSuchMember);

            var outfit = OutfitDirector.Instance;
            if (outfit != null)
            {
                var paid = outfit.Purchase(price, "a man out of the paper");
                if (!paid.Ok)
                {
                    // The money was refused, so the ad was never taken: put it back in
                    // the column rather than quietly losing the man off the page.
                    market.Restore(ad);
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

            var result = RosterOps.Promote(Roster, man.Id, out _);
            if (!result.Ok)
            {
                // The roster refused him after the safe paid: the man goes back on the
                // page and the money goes back where it came from, Purchases line included.
                Roster.Members.Remove(man);
                man.Id = -1;
                market.Restore(ad);
                if (outfit != null)
                    outfit.Refund(price, "a man out of the paper");
                return result;
            }

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

        public OpResult Promote(int id, out int newCrewId)
        {
            newCrewId = -1;
            return Roster == null
                ? OpResult.Fail(LivingCity.UI.LedgerText.ReasonNoSuchMember)
                : Commit(RosterOps.Promote(Roster, id, out newCrewId), "promoted", id);
        }

        public OpResult Demote(int id) =>
            Apply(RosterOps.Demote, id, "demoted");

        /// <summary>The street reports a man shot dead: struck through, his gear pooled,
        /// his crew passed on. Version moves so every book and bar re-deals.</summary>
        public OpResult Kill(int id) =>
            Apply(RosterOps.Kill, id, "shot dead");

        /// <summary>The street reports a man who ran from the fight and kept running:
        /// struck off as a deserter, his gear pooled, his post passed on.</summary>
        public OpResult Desert(int id) =>
            Apply(RosterOps.Desert, id, "deserted");

        public OpResult AssignToPool(int id) =>
            Apply(RosterOps.AssignToPool, id, "sent to the pool");

        public OpResult AssignToFront(int id) =>
            Apply(RosterOps.AssignToFront, id, "put on the front");

        public OpResult AssignToCrew(int id, int crewId)
        {
            if (Roster == null)
                return OpResult.Fail(LivingCity.UI.LedgerText.ReasonNoSuchMember);
            return Commit(RosterOps.AssignToCrew(Roster, id, crewId), "reassigned", id);
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
