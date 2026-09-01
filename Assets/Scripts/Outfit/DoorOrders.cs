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
