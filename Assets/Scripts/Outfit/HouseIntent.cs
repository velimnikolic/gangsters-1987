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
            string listing = "", int price = 0)
        {
            Kit = kit;
            Listing = listing ?? "";
            Price = price;
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

        public static HouseIntent Buy(
            EquipmentKind kind, string listing, int price, int characterId, int crewId,
            int tier, string reason) =>
            new HouseIntent(HouseIntentKind.Buy, tier, reason, HouseOrder.None, null,
                characterId, crewId, default, default, TerritoryRacketIntent.Approach,
                Duty.None, CrewPolicy.Normal, kind, listing, price);

        /// <summary>One line for the trace and the family's own book.</summary>
        public override string ToString() =>
            Kind == HouseIntentKind.Command
                ? Order.ToString()
                : Kind == HouseIntentKind.Job
                    ? "Job " + (Job != null ? Job.Type.ToString() : "?")
                    : Kind.ToString();
    }
}
