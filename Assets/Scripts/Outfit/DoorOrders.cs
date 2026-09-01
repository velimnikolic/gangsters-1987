namespace LivingCity.Outfit
{
    /// <summary>
    /// Where one door stands with the player's family, as every order sheet has to read
    /// it before it offers anything against that door.
    /// </summary>
    public enum DoorTenure
    {
        /// <summary>On our own paper - a bought premises, a front, the headquarters.</summary>
        Ours,

        /// <summary>Not ours, but it pays us for peace.</summary>
        Paying,

        /// <summary>Another house holds it, by deed, by front or by its own racket.</summary>
        Rival,

        /// <summary>Nobody holds it. The whole point of the racket is that this changes.</summary>
        Open,
    }

    /// <summary>
    /// The one table of what may be ordered against a door, given who holds it.
    ///
    /// This exists because the game asks the question in two places - the block file's
    /// key row and the order book's map pick - and they disagreed: the sheet has never
    /// offered ROB IT against a shop that pays us, while the map's planner checked only
    /// the deed book and would happily send a crew to rob, wreck, torch or bomb the very
    /// premises whose tribute the outfit collects every week. The block file is the
    /// authority on what may be done to a door; this class is that authority written
    /// down once so the map obeys the same rules.
    ///
    /// Pure and headlessly testable like the rest of the Outfit namespace: the caller
    /// reads the tenure from the world and passes it in.
    /// </summary>
    public static class DoorOrders
    {
        /// <summary>An order that means nothing without a business door under it.</summary>
        public static bool NeedsDoor(OrderType type) =>
            type == OrderType.SmashUp ||
            type == OrderType.Raid ||
            type == OrderType.Torch ||
            type == OrderType.Bomb ||
            type == OrderType.BuyPremises ||
            type == OrderType.SetUpBusiness ||
            type == OrderType.RunBusiness ||
            type == OrderType.AdjustProtection;

        /// <summary>Work that is done TO the door rather than for it, so none of it is
        /// ordered against a door the outfit already owns.</summary>
        public static bool IsHostile(OrderType type) =>
            IsViolence(type) ||
            type == OrderType.BuyPremises ||
            type == OrderType.AdjustProtection;

        /// <summary>The four that land on the premises itself - the register emptied, the
        /// front boarded, the place burnt, the place blown.</summary>
        public static bool IsViolence(OrderType type) =>
            type == OrderType.Raid ||
            type == OrderType.SmashUp ||
            type == OrderType.Torch ||
            type == OrderType.Bomb;

        /// <summary>
        /// How heavily one act lands on the man it was done to. The fear ledger knows
        /// only the CATEGORY - a bat through the window and a firebomb are both property
        /// damage - and they are not the same argument to a man who has to open again
        /// tomorrow.
        ///
        /// These numbers exist because the ladder did not terminate: an owner could be
        /// leaned on, smashed and burnt out and still only waver, because a wrecked front
        /// was worth a fifth of what a demand needs and a torch was worth exactly the
        /// same as a bat. The rule they encode is the one the game is about - VIOLENCE
        /// FRIGHTENS, PRESENCE COLLECTS: a wrecked front is enough to fold an owner while
        /// the family that wrecked it is standing on his street, and not enough on its
        /// own when nobody is there to pay.
        /// </summary>
        public static float ViolenceSeverity(OrderType type)
        {
            switch (type)
            {
                case OrderType.SmashUp: return 2.5f;
                case OrderType.Torch: return 4f;
                case OrderType.Bomb: return 5f;
                // A raid files as an ASSAULT rather than property damage, and that
                // category's own impact is already heavier, so it needs less on top -
                // but it still has to CARRY, because a man robbed by the family whose
                // men are standing outside has been given the whole argument at once.
                case OrderType.Raid: return 1.5f;
                default: return 1f;
            }
        }

        /// <summary>
        /// Why this order cannot be given against this door, in the planner's own voice,
        /// or null when it can. The planner explains; the report never does.
        /// </summary>
        public static string Refusal(OrderType type, DoorTenure tenure)
        {
            if (tenure == DoorTenure.Ours && IsHostile(type))
                return "That door is on our own paper - only a guard can be put on it.";

            // A shop that pays us for peace is the outfit's own income. Robbing it takes
            // a week's tribute out of the till it comes from, and wrecking it takes the
            // till; either way the family is charging protection against itself.
            if (tenure == DoorTenure.Paying && IsViolence(type))
                return "That door pays us for peace - we do not rob the takings we collect.";

            return null;
        }
    }
}
