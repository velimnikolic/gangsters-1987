using LivingCity.Personnel;
using LivingCity.Territory;

namespace LivingCity.Outfit
{
    /// <summary>What kind of thing a mind wants done. Each one has a door in the game
    /// that the player uses too; a mind never gets one of its own.</summary>
    public enum HouseIntentKind
    {
        None,

        /// <summary>A territory order, through the command gateway.</summary>
        Command,

        /// <summary>A job for a lieutenant's book, through Underworld.Issue.</summary>
        Job,

        SetDuty,
        AssignToCrew,
        Promote,
        Demote,
        SetPolicy,
        AssignBlock,

        /// <summary>Buy a thing out of the safe, through HouseOps - a car for a crew, a
        /// gun for a man.</summary>
        Buy,

        /// <summary>Where we mean to stand with another family from midnight.</summary>
        SetStance,

        /// <summary>A word to another family, printed in both books.</summary>
        Warn,

        /// <summary>A job taken off a lieutenant's book, through CampaignRunner.Cancel -
        /// the call the player's own key already makes. The guard comes off (AI-001).
        /// </summary>
        Cancel,

        /// <summary>A man of ours out of a cell on the house's money, through the same
        /// pipe the ledger's POST BAIL row uses (AI-005 P1).</summary>
        Bail,

        /// <summary>Counsel on retainer, off the same market the classified column
        /// deals from and at the same price (AI-005 P1).</summary>
        Retain,
    }

    /// <summary>
    /// Which territory order. The gateway takes one struct per order and a mind is pure,
    /// so it NAMES the order and the runtime builds it - the same struct the player's own
    /// key builds, submitted through the same door.
    /// </summary>
    public enum HouseOrder
    {
        None,
        OperateInBlock,
        ApproachBusiness,
        LeanOnHoldouts,
        ShakeDownBlock,
        CollectDues,
    }

    /// <summary>
    /// ONE THING A HOUSE WANTS DONE, and why.
    ///
    /// A mind emits these and touches nothing. The runtime executes them through the
    /// SAME entry points the player's own buttons use - the command gateway,
    /// <see cref="Underworld.Issue"/>, <see cref="HouseOps"/> - so a family can never do
    /// anything the player could not, and a refusal refuses them both alike.
    /// </summary>
    public readonly struct HouseIntent
    {
        HouseIntent(HouseIntentKind kind, int tier, string reason, HouseOrder order,
            Job job, int characterId, int crewId, TerritoryBlockId blockId,
            TerritoryBusinessId businessId, TerritoryRacketIntent followUp, Duty duty,
            CrewPolicy policy, EquipmentKind kit = EquipmentKind.Pistol,
            string listing = "", int price = 0, TerritoryGangId other = default,
            Stance stance = Stance.Peace)
        {
            Kit = kit;
            Listing = listing ?? "";
            Price = price;
            Other = other;
            Stance = stance;
            Kind = kind;
            Tier = tier;
            Reason = reason ?? "";
            Order = order;
            Job = job;
            CharacterId = characterId;
            CrewId = crewId;
            BlockId = blockId;
            BusinessId = businessId;
            FollowUp = followUp;
            Duty = duty;
            Policy = policy;
        }

        public HouseIntentKind Kind { get; }

        /// <summary>Which priority tier asked for it (D8). The trace prints it.</summary>
        public int Tier { get; }

        /// <summary>One line, in the family's own words, for the trace and the book.
        /// </summary>
        public string Reason { get; }

        public HouseOrder Order { get; }
        public Job Job { get; }
        public int CharacterId { get; }
        public int CrewId { get; }
        public TerritoryBlockId BlockId { get; }
        public TerritoryBusinessId BusinessId { get; }
        public TerritoryRacketIntent FollowUp { get; }
        public Duty Duty { get; }
        public CrewPolicy Policy { get; }

        /// <summary>What is being bought, what the dealer calls it, and what it costs.
        /// </summary>
        public EquipmentKind Kit { get; }
        public string Listing { get; }
        public int Price { get; }

        /// <summary>The other family, for the intents that are about one.</summary>
        public TerritoryGangId Other { get; }
        public Stance Stance { get; }

        public static HouseIntent Block(
            HouseOrder order, int crewId, TerritoryBlockId blockId, int tier,
            string reason) =>
            new HouseIntent(HouseIntentKind.Command, tier, reason, order, null, -1, crewId,
                blockId, default, TerritoryRacketIntent.Approach, Duty.None,
                CrewPolicy.Normal);

        public static HouseIntent Door(
            int crewId, TerritoryBusinessId businessId, TerritoryRacketIntent followUp,
            int tier, string reason) =>
            new HouseIntent(HouseIntentKind.Command, tier, reason,
                HouseOrder.ApproachBusiness, null, -1, crewId, default, businessId,
                followUp, Duty.None, CrewPolicy.Normal);

        public static HouseIntent Work(Job job, int tier, string reason) =>
            new HouseIntent(HouseIntentKind.Job, tier, reason, HouseOrder.None, job, -1,
                job != null ? job.CrewId : -1, default, default,
                TerritoryRacketIntent.Approach, Duty.None, CrewPolicy.Normal);

        public static HouseIntent MarkDuty(
            int characterId, Duty duty, int tier, string reason) =>
            new HouseIntent(HouseIntentKind.SetDuty, tier, reason, HouseOrder.None, null,
                characterId, -1, default, default, TerritoryRacketIntent.Approach, duty,
                CrewPolicy.Normal);

        public static HouseIntent MoveToCrew(
            int characterId, int crewId, int tier, string reason) =>
            new HouseIntent(HouseIntentKind.AssignToCrew, tier, reason, HouseOrder.None,
                null, characterId, crewId, default, default,
                TerritoryRacketIntent.Approach, Duty.None, CrewPolicy.Normal);

        public static HouseIntent Raise(int characterId, int tier, string reason) =>
            new HouseIntent(HouseIntentKind.Promote, tier, reason, HouseOrder.None, null,
                characterId, -1, default, default, TerritoryRacketIntent.Approach,
                Duty.None, CrewPolicy.Normal);

        public static HouseIntent Break(int characterId, int tier, string reason) =>
            new HouseIntent(HouseIntentKind.Demote, tier, reason, HouseOrder.None, null,
                characterId, -1, default, default, TerritoryRacketIntent.Approach,
                Duty.None, CrewPolicy.Normal);

        public static HouseIntent Orders(
            int crewId, CrewPolicy policy, int tier, string reason) =>
            new HouseIntent(HouseIntentKind.SetPolicy, tier, reason, HouseOrder.None,
                null, -1, crewId, default, default, TerritoryRacketIntent.Approach,
                Duty.None, policy);

        public static HouseIntent GiveBlock(
            int lieutenantId, TerritoryBlockId blockId, int tier, string reason) =>
            new HouseIntent(HouseIntentKind.AssignBlock, tier, reason, HouseOrder.None,
                null, lieutenantId, -1, blockId, default,
                TerritoryRacketIntent.Approach, Duty.None, CrewPolicy.Normal);

        /// <summary>Where we mean to stand with them from the next midnight.</summary>
        public static HouseIntent Stand(
            TerritoryGangId other, Stance stance, int tier, string reason) =>
            new HouseIntent(HouseIntentKind.SetStance, tier, reason, HouseOrder.None,
                null, -1, -1, default, default, TerritoryRacketIntent.Approach,
                Duty.None, CrewPolicy.Normal, EquipmentKind.Pistol, "", 0, other,
                stance);

        /// <summary>A word: a warning, a threat, or a bill.</summary>
        public static HouseIntent Word(
            TerritoryGangId other, string text, int price, int tier, string reason) =>
            new HouseIntent(HouseIntentKind.Warn, tier, reason, HouseOrder.None, null,
                -1, -1, default, default, TerritoryRacketIntent.Approach, Duty.None,
                CrewPolicy.Normal, EquipmentKind.Pistol, text, price, other,
                Stance.Peace);

        public static HouseIntent Buy(
            EquipmentKind kind, string listing, int price, int characterId, int crewId,
            int tier, string reason) =>
            new HouseIntent(HouseIntentKind.Buy, tier, reason, HouseOrder.None, null,
                characterId, crewId, default, default, TerritoryRacketIntent.Approach,
                Duty.None, CrewPolicy.Normal, kind, listing, price);

        /// <summary>A job called off. <paramref name="jobId"/> rides in CharacterId's
        /// seat - the struct has no job-number field of its own, and a second int for
        /// one intent kind is not worth a wider struct.</summary>
        public static HouseIntent CallOff(int jobId, int crewId, int tier, string reason) =>
            new HouseIntent(HouseIntentKind.Cancel, tier, reason, HouseOrder.None, null,
                jobId, crewId, default, default, TerritoryRacketIntent.Approach,
                Duty.None, CrewPolicy.Normal);

        /// <summary>Post bail for a man of ours. The price is what the court asked and
        /// is repeated here so the trace can print what the house was ready to pay.
        /// </summary>
        public static HouseIntent PostBail(int characterId, int price, int tier,
            string reason) =>
            new HouseIntent(HouseIntentKind.Bail, tier, reason, HouseOrder.None, null,
                characterId, -1, default, default, TerritoryRacketIntent.Approach,
                Duty.None, CrewPolicy.Normal, EquipmentKind.Pistol, "", price);

        /// <summary>Retain counsel, at the market's own price.</summary>
        public static HouseIntent RetainCounsel(int price, int tier, string reason) =>
            new HouseIntent(HouseIntentKind.Retain, tier, reason, HouseOrder.None, null,
                -1, -1, default, default, TerritoryRacketIntent.Approach, Duty.None,
                CrewPolicy.Normal, EquipmentKind.Pistol, "counsel", price);

        /// <summary>One line for the trace and the family's own book.</summary>
        public override string ToString() =>
            Kind == HouseIntentKind.Command
                ? Order.ToString()
                : Kind == HouseIntentKind.Job
                    ? "Job " + (Job != null ? Job.Type.ToString() : "?")
                    : Kind.ToString();

        /// <summary>
        /// WHAT THIS INTENT IS, AND WHAT IT IS AIMED AT - the key a refusal is
        /// remembered under (<see cref="HouseBackoffs"/>). Two intents with the same
        /// key are the same request; a refused bail for one man must not silence a
        /// bail for another, and a refused walk on one block must not silence the
        /// next block over.
        /// </summary>
        public string Key
        {
            get
            {
                switch (Kind)
                {
                    case HouseIntentKind.Command:
                        return "cmd:" + Order + ":" + CrewId + ":" +
                               (BusinessId.IsValid ? BusinessId.Value : "") + ":" +
                               (BlockId.IsValid ? BlockId.Value : "") + ":" + FollowUp;
                    case HouseIntentKind.Job:
                        return Job == null
                            ? "job:?"
                            : "job:" + Job.Type + ":" + Job.CrewId + ":" +
                              (Job.TargetBusinessId ?? "") + ":" + (Job.TargetLabel ?? "");
                    case HouseIntentKind.Buy:
                        return "buy:" + Kit + ":" + CharacterId + ":" + CrewId;
                    case HouseIntentKind.SetStance:
                        return "stance:" + Other.Value + ":" + Stance;
                    case HouseIntentKind.Warn:
                        return "warn:" + Other.Value + ":" + Listing;
                    case HouseIntentKind.AssignBlock:
                        return "block:" + CharacterId + ":" +
                               (BlockId.IsValid ? BlockId.Value : "");
                    case HouseIntentKind.Bail:
                        return "bail:" + CharacterId + ":" + Price;
                    case HouseIntentKind.Retain:
                        return "retain";
                    default:
                        return Kind + ":" + CharacterId + ":" + CrewId + ":" + Duty +
                               ":" + Policy;
                }
            }
        }
    }
}
