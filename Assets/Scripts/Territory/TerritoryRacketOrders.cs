using System.Collections.Generic;

namespace LivingCity.Territory
{
    /// <summary>What a family can put to a shopkeeper.</summary>
    public enum TerritoryRacketIntent
    {
        /// <summary>Send the men to his door. Intent only - the demand comes after.</summary>
        Approach,

        /// <summary>Ask him, with the men standing there.</summary>
        Demand,

        /// <summary>Lean on him, and ask again.</summary>
        Threaten,

        /// <summary>Collect what he owes - the round: door to door, the take carried
        /// home (ECON-004). Only a paying shop has anything to collect.</summary>
        Collect,
    }

    /// <summary>
    /// One row on whichever surface is asking: what it says, why it cannot be given when
    /// it cannot, and which intent it carries. A row STANDS even when it is not available -
    /// the street card's own rule - because a player who is told nothing learns nothing.
    /// </summary>
    public readonly struct TerritoryRacketOrder
    {
        public TerritoryRacketOrder(
            TerritoryRacketIntent intent, string label, string note, bool available)
        {
            Intent = intent;
            Label = label;
            Note = note ?? "";
            Available = available;
        }

        public TerritoryRacketIntent Intent { get; }
        public string Label { get; }

        /// <summary>What the row is for, or - when it is faded - why it is.</summary>
        public string Note { get; }

        public bool Available { get; }
    }

    /// <summary>
    /// The one list of what can be ordered against a shop, so the street card, the paper
    /// map and the ledger all offer the same rows and none of them decides for itself what
    /// is possible. It is pure: it reads a standing and two facts about the crew, and it
    /// never submits anything - the surfaces do that through the command gateway.
    ///
    /// Adding a fourth thing to do to a shopkeeper is one entry here and no change in any
    /// surface, which is the whole point of the type existing.
    /// </summary>
    public static class TerritoryRacketOrders
    {
        public const string ApproachLabel = "GO TO THE DOOR";
        public const string DemandLabel = "DEMAND PROTECTION";
        public const string ThreatenLabel = "THREATEN THE OWNER";
        public const string CollectLabel = "COLLECT THE TAKE";

        /// <summary>
        /// The rows for this shop, given where it stands with the asking family and
        /// whether that family has men at its door.
        /// </summary>
        /// <param name="racketable">False for a place that carries no business at all -
        /// a civic building, a block with no records. Then there is nothing to ask.</param>
        /// <param name="hasCrew">Whether a crew is selected to send at all.</param>
        /// <param name="atDoor">Whether that crew's men are actually standing there.</param>
        public static void For(
            TerritoryProtectionState standing,
            bool racketable,
            bool hasCrew,
            bool atDoor,
            List<TerritoryRacketOrder> into)
        {
            if (into == null)
                return;
            into.Clear();
            if (!racketable)
                return;

            if (!hasCrew)
            {
                into.Add(new TerritoryRacketOrder(
                    TerritoryRacketIntent.Approach, ApproachLabel,
                    "nobody is picked to send", false));
                return;
            }

            // Walking up to a place you already have an arrangement with, or one that has
            // already told you no, is not an order - the men are not going to learn
            // anything by standing there again.
            var worthApproaching = !atDoor && standing != TerritoryProtectionState.Compliant;
            into.Add(new TerritoryRacketOrder(
                TerritoryRacketIntent.Approach, ApproachLabel,
                atDoor ? "the men are already at his door" : "send the men to his door",
                worthApproaching));

            if (standing == TerritoryProtectionState.Compliant)
            {
                // A paying shop is not demanded from - it is COLLECTED from: the crew
                // walks the block's paying doors and carries the take home (ECON-004).
                into.Add(new TerritoryRacketOrder(
                    TerritoryRacketIntent.Collect, CollectLabel,
                    "walk the round and carry it home", true));
                return;
            }

            // From range the demand is still one order: the men walk there and put it
            // to him when they arrive (the approach carries the intent). At the door it
            // is the conversation itself.
            into.Add(new TerritoryRacketOrder(
                TerritoryRacketIntent.Demand, DemandLabel,
                atDoor
                    ? Ask(standing)
                    : "they walk to his door and put it to him",
                true));

            into.Add(new TerritoryRacketOrder(
                TerritoryRacketIntent.Threaten, ThreatenLabel,
                atDoor
                    ? "lean on him, then ask again"
                    : "they walk to his door and lean on him",
                true));
        }

        static string Ask(TerritoryProtectionState standing)
        {
            switch (standing)
            {
                case TerritoryProtectionState.Defiant: return "put it to him again";
                case TerritoryProtectionState.Hesitant:
                case TerritoryProtectionState.Intimidated:
                    return "he is wavering - press him";
                default: return "tell him how it works around here";
            }
        }
    }
}
