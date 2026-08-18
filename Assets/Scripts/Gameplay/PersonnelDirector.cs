using UnityEngine;
using LivingCity.Generation;
using LivingCity.Personnel;

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
            seed = builder && builder.Config ? builder.Config.seed : FallbackSeed;
            // In the standalone Ledger menu the missing city is the DESIGN, not a
            // fault - the warning would cry wolf on every single Play there.
            if ((!builder || !builder.Config) &&
                !FindAnyObjectByType<UI.LedgerMenuScene>())
                Debug.LogWarning("[Personnel] No CityBuilder config in the scene - the " +
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
