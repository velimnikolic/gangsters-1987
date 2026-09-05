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

        /// <summary>A proposal to another house, through HouseOps.Propose - the same
        /// call the ledger's TABLE makes (EPIC 42).</summary>
        Propose,

        /// <summary>An answer to a proposal in our inbox, through HouseOps.Reply.
        /// </summary>
        Reply,

        // Appended for EPIC 40 (The Connection), so a saved intent keeps its number.

        /// <summary>A flat on the house's deed, through the same Apartments.Buy the
        /// blueprint form calls (PRE-001). The unit is named by the carrier - the
        /// scene edge picks the first vacant room on a block the house holds - and
        /// the role and the keeper ride on the intent.</summary>
        Lease,

        /// <summary>A held flat turned to a use, fit-out paid (PRE-001).</summary>
        FitOut,

        /// <summary>A man put in a held flat (PRE-001).</summary>
        SetKeeper,

        /// <summary>A man signed off a card - the connection's man - into a
        /// lieutenant's crew, the twin of Retain with a crew named (CONN-001).</summary>
        Sign,

        /// <summary>A choice on a pending card (STREET-003). The carrier records the
        /// answer on the book and carries the choice's own intent through the same
        /// switch, so a card can never do what a button cannot.</summary>
        Card,

        /// <summary>The supplier's terms accepted (CONN-004).</summary>
        AcceptTerms,

        /// <summary>Every kilo in the room sold to the buyer, flat (CONN-004).</summary>
        Sell,
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
            Stance stance = Stance.Peace, Proposal proposal = null, int proposalId = -1,
            bool accept = false, Property.UnitRole role = Property.UnitRole.Empty,
            EventCard card = null)
        {
            Role = role;
            Card = card;
            Proposal = proposal;
            ProposalId = proposalId;
            Accept = accept;
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

        /// <summary>What a leased room is turned to (Lease, FitOut). For Lease the
        /// unit itself is named by the carrier; for FitOut and SetKeeper it rides in
        /// Listing as the unit's own string.</summary>
        public Property.UnitRole Role { get; }

        /// <summary>The card a Card intent answers, or the card whose man a Sign
        /// intent signs. Memory only: a card is re-dealt from its day on a load.</summary>
        public EventCard Card { get; }

        /// <summary>What is being proposed (Propose), or which proposal is being
        /// answered and how (Reply). EPIC 42.</summary>
        public Proposal Proposal { get; }
        public int ProposalId { get; }
        public bool Accept { get; }

        /// <summary>A proposal to another house. Its To is the other family.</summary>
        public static HouseIntent Propose(Proposal proposal, int tier, string reason) =>
            new HouseIntent(HouseIntentKind.Propose, tier, reason, HouseOrder.None, null,
                -1, -1, default, default, TerritoryRacketIntent.Approach, Duty.None,
                CrewPolicy.Normal, EquipmentKind.Pistol, "", 0,
                new TerritoryGangId(proposal != null ? proposal.To : -1), Stance.Peace,
                proposal);

        /// <summary>An answer to a proposal in the inbox.</summary>
        public static HouseIntent Reply(int proposalId, bool accept, int tier,
            string reason) =>
            new HouseIntent(HouseIntentKind.Reply, tier, reason, HouseOrder.None, null,
                -1, -1, default, default, TerritoryRacketIntent.Approach, Duty.None,
                CrewPolicy.Normal, EquipmentKind.Pistol, "", 0, default, Stance.Peace,
                null, proposalId, accept);

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

        /// <summary>A flat rented and fitted out, with its keeper named. The carrier
        /// picks the room (PRE-001: the first vacant unit in a building on a block the
        /// house holds, nearest its front); the mind only says what it is for.</summary>
        public static HouseIntent Lease(Property.UnitRole role, int keeperId, int tier,
            string reason) =>
            new HouseIntent(HouseIntentKind.Lease, tier, reason, HouseOrder.None, null,
                keeperId, -1, default, default, TerritoryRacketIntent.Approach,
                Duty.None, CrewPolicy.Normal, EquipmentKind.Pistol, "", 0, default,
                Stance.Peace, null, -1, false, role);

        /// <summary>A held room turned to a use. The unit rides in Listing.</summary>
        public static HouseIntent FitOut(string unit, Property.UnitRole role, int tier,
            string reason) =>
            new HouseIntent(HouseIntentKind.FitOut, tier, reason, HouseOrder.None, null,
                -1, -1, default, default, TerritoryRacketIntent.Approach, Duty.None,
                CrewPolicy.Normal, EquipmentKind.Pistol, unit ?? "", 0, default,
                Stance.Peace, null, -1, false, role);

        /// <summary>A man put in a held room. The unit rides in Listing.</summary>
        public static HouseIntent Keep(string unit, int keeperId, int tier, string reason) =>
            new HouseIntent(HouseIntentKind.SetKeeper, tier, reason, HouseOrder.None,
                null, keeperId, -1, default, default, TerritoryRacketIntent.Approach,
                Duty.None, CrewPolicy.Normal, EquipmentKind.Pistol, unit ?? "");

        /// <summary>The connection's man signed off his card into a lieutenant's crew
        /// (CONN-001). The card carries the man; the crew is the speaker's.</summary>
        public static HouseIntent Sign(EventCard card, int crewId, int price, int tier,
            string reason) =>
            new HouseIntent(HouseIntentKind.Sign, tier, reason, HouseOrder.None, null,
                card != null ? card.ManId : -1, crewId, default, default,
                TerritoryRacketIntent.Approach, Duty.None, CrewPolicy.Normal,
                EquipmentKind.Pistol, "the man", price, default, Stance.Peace, null, -1,
                false, Property.UnitRole.Empty, card);

        /// <summary>A choice on a card (STREET-003). The index rides in CharacterId's
        /// seat and the label in Listing, so the trace prints "Card:TestBuy/PAY".
        /// </summary>
        public static HouseIntent Choose(EventCard card, int choiceIndex, int tier,
            string reason) =>
            new HouseIntent(HouseIntentKind.Card, tier, reason, HouseOrder.None, null,
                choiceIndex, -1, default, default, TerritoryRacketIntent.Approach,
                Duty.None, CrewPolicy.Normal, EquipmentKind.Pistol,
                card != null ? card.Id + "/" + card.LabelOf(choiceIndex) : "?",
                card != null ? card.CostOf(choiceIndex) : 0, default, Stance.Peace,
                null, -1, false, Property.UnitRole.Empty, card);

        /// <summary>The supplier's terms taken (CONN-004).</summary>
        public static HouseIntent AcceptTerms(int tier, string reason) =>
            new HouseIntent(HouseIntentKind.AcceptTerms, tier, reason, HouseOrder.None,
                null, -1, -1, default, default, TerritoryRacketIntent.Approach,
                Duty.None, CrewPolicy.Normal, EquipmentKind.Pistol, "terms");

        /// <summary>Every kilo in the room to the buyer (CONN-004).</summary>
        public static HouseIntent SellKilos(int tier, string reason) =>
            new HouseIntent(HouseIntentKind.Sell, tier, reason, HouseOrder.None, null,
                -1, -1, default, default, TerritoryRacketIntent.Approach, Duty.None,
                CrewPolicy.Normal, EquipmentKind.Pistol, "kilos");

        /// <summary>One line for the trace and the family's own book.</summary>
        public override string ToString() =>
            Kind == HouseIntentKind.Command
                ? Order.ToString()
                : Kind == HouseIntentKind.Job
                    ? "Job " + (Job != null ? Job.Type.ToString() : "?")
                    : Kind == HouseIntentKind.Propose
                        ? "Propose " + (Proposal != null ? Proposal.Kind.ToString() : "?")
                        : Kind == HouseIntentKind.Card
                            ? "Card:" + Listing
                            : Kind == HouseIntentKind.Lease
                                ? "Lease:" + Role
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
                    case HouseIntentKind.Lease:
                        return "lease:" + Role;
                    case HouseIntentKind.FitOut:
                        return "fitout:" + Listing + ":" + Role;
                    case HouseIntentKind.SetKeeper:
                        return "keeper:" + Listing + ":" + CharacterId;
                    case HouseIntentKind.Sign:
                        return "sign:" + CrewId;
                    case HouseIntentKind.Card:
                        return "card:" + Listing;
                    case HouseIntentKind.AcceptTerms:
                        return "terms";
                    case HouseIntentKind.Sell:
                        return "sell";
                    case HouseIntentKind.Propose:
                        return "propose:" + Other.Value + ":" +
                               (Proposal != null ? Proposal.Kind.ToString() : "?");
                    case HouseIntentKind.Reply:
                        return "reply:" + ProposalId + ":" + Accept;
                    default:
                        return Kind + ":" + CharacterId + ":" + CrewId + ":" + Duty +
                               ":" + Policy;
                }
            }
        }
    }
}
