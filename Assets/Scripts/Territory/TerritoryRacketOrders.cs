using System.Collections.Generic;
using LivingCity.Business;
using LivingCity.Outfit;

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
    /// Which book a door row goes into. The two are not the same kind of act: a racket
    /// row is men in a doorway, submitted through the territory gateway and answered by
    /// the owner on the spot, while a job row is work filed with the outfit - it queues
    /// behind a lieutenant's other orders and comes back a day later.
    /// </summary>
    public enum TerritoryDoorRowKind
    {
        Racket,
        Job,
        Repair,
    }

    /// <summary>The closure facts the shared menu needs, already projected from the
    /// business simulation. Default means the place is trading.</summary>
    public readonly struct TerritoryDoorClosure
    {
        public TerritoryDoorClosure(
            bool shut, string note, bool repairVisible,
            bool repairAvailable, int repairPrice,
            BusinessShutdownCause cause = BusinessShutdownCause.None)
        {
            Shut = shut;
            Note = note ?? "";
            RepairVisible = repairVisible;
            RepairAvailable = repairAvailable;
            RepairPrice = repairPrice;
            Cause = cause;
        }

        public bool Shut { get; }
        public string Note { get; }
        public bool RepairVisible { get; }
        public bool RepairAvailable { get; }
        public int RepairPrice { get; }
        public BusinessShutdownCause Cause { get; }
    }

    /// <summary>
    /// One row on whichever surface is asking: what it says, why it cannot be given when
    /// it cannot, and which act it carries. A row STANDS even when it is not available -
    /// the street card's own rule - because a player who is told nothing learns nothing.
    /// </summary>
    public readonly struct TerritoryRacketOrder
    {
        public TerritoryRacketOrder(
            TerritoryRacketIntent intent, string label, string note, bool available)
            : this(TerritoryDoorRowKind.Racket, intent, default, label, note, available, 0)
        {
        }

        public TerritoryRacketOrder(
            OrderType job, string label, string note, bool available, int cash = 0)
            : this(TerritoryDoorRowKind.Job, TerritoryRacketIntent.Approach, job, label,
                note, available, cash)
        {
        }

        public static TerritoryRacketOrder Repair(
            string label, string note, bool available, int cash) =>
            new TerritoryRacketOrder(
                TerritoryDoorRowKind.Repair, TerritoryRacketIntent.Approach, default,
                label, note, available, cash);

        TerritoryRacketOrder(
            TerritoryDoorRowKind kind, TerritoryRacketIntent intent, OrderType job,
            string label, string note, bool available, int cash)
        {
            Kind = kind;
            Intent = intent;
            Job = job;
            Label = label;
            Note = note ?? "";
            Available = available;
            Cash = cash;
        }

        public TerritoryDoorRowKind Kind { get; }

        /// <summary>Meaningful on a Racket row.</summary>
        public TerritoryRacketIntent Intent { get; }

        /// <summary>Meaningful on a Job row.</summary>
        public OrderType Job { get; }

        public string Label { get; }

        /// <summary>What the row is for, or - when it is faded - why it is.</summary>
        public string Note { get; }

        public bool Available { get; }

        /// <summary>What the row commits when it commits money - the asking price on the
        /// deed, nothing on the rest. Every surface prints it beside the same word.</summary>
        public int Cash { get; }
    }

    /// <summary>
    /// The one list of what can be ordered against a shop, so the street card, the paper
    /// map and the ledger's block file all offer the same rows and none of them decides
    /// for itself what is possible. It is pure: it reads a standing, who holds the door
    /// and two facts about the crew, and it never submits anything - the surfaces do that,
    /// through the command gateway for a racket row and the order book for a job row.
    ///
    /// Every row the game has against a door lives HERE. A surface that writes a key of
    /// its own is the bug this class exists to prevent: the block file used to add ROB IT,
    /// SIT ON IT and BUY IT OUTRIGHT itself, so the street and the map offered a shorter
    /// menu than the ledger did against the same shop.
    /// </summary>
    public static class TerritoryRacketOrders
    {
        public const string ApproachLabel = "GO TO THE DOOR";
        public const string DemandLabel = "DEMAND PROTECTION";
        public const string ThreatenLabel = "THREATEN THE OWNER";
        public const string CollectLabel = "COLLECT THE TAKE";
        public const string SmashLabel = "SMASH IT UP";
        public const string TorchLabel = "TORCH IT";
        public const string RobLabel = "ROB IT";
        public const string GuardLabel = "SIT ON IT";
        public const string BuyLabel = "BUY IT OUTRIGHT";
        public const string RepairLabel = "PAY FOR REPAIRS";

        /// <summary>
        /// The rows for this shop, given where it stands with the asking family, who holds
        /// the door and whether that family has men at it.
        /// </summary>
        /// <param name="racketable">False for a place that carries no business at all -
        /// a civic building, a block with no records. Then there is nothing to ask.</param>
        /// <param name="hasCrew">Whether a crew is selected to send at all.</param>
        /// <param name="atDoor">Whether that crew's men are actually standing there.</param>
        /// <param name="askingPrice">What the deed costs outright, or 0 when the door
        /// carries no price on the book.</param>
        public static void For(
            TerritoryProtectionState standing,
            DoorTenure tenure,
            bool racketable,
            bool hasCrew,
            bool atDoor,
            int askingPrice,
            List<TerritoryRacketOrder> into,
            bool collectionDue = true,
            string collectionNote = null,
            TerritoryDoorClosure closure = default)
        {
            if (into == null)
                return;
            into.Clear();
            if (!racketable)
                return;

            // Why the whole doorstep chain is closed, when it is. Our own premises are
            // not shaken down, and a crew has to be picked before anybody walks anywhere.
            var shut = !hasCrew ? "nobody is picked to send"
                : tenure == DoorTenure.Ours ? "the place is on our own paper"
                : null;
            var paying = standing == TerritoryProtectionState.Compliant;
            var open = shut == null;

            // Walking up to a place you already have an arrangement with is not an order -
            // the men are not going to learn anything by standing there again.
            into.Add(new TerritoryRacketOrder(
                TerritoryRacketIntent.Approach, ApproachLabel,
                shut ?? (atDoor ? "the men are already at his door"
                    : paying ? "he already pays us"
                    : "send the men to his door"),
                open && !atDoor && !paying));

            // From range the demand is still one order: the men walk there and put it
            // to him when they arrive (the approach carries the intent). At the door it
            // is the conversation itself.
            into.Add(new TerritoryRacketOrder(
                TerritoryRacketIntent.Demand, DemandLabel,
                shut ?? (paying ? "he already pays us · collect instead"
                    : atDoor ? Ask(standing)
                    : "they walk to his door and put it to him"),
                open && !paying));

            into.Add(new TerritoryRacketOrder(
                TerritoryRacketIntent.Threaten, ThreatenLabel,
                shut ?? (paying ? "he already pays us"
                    : atDoor ? "lean on him, then ask again"
                    : "they walk to his door and lean on him"),
                open && !paying));

            // A paying shop is not demanded from - it is COLLECTED from: the crew walks
            // the block's paying doors and carries the take home (ECON-004).
            into.Add(new TerritoryRacketOrder(
                TerritoryRacketIntent.Collect, CollectLabel,
                shut ?? (closure.Shut
                    ? closure.Note
                    : paying && !collectionDue
                    ? collectionNote ?? "nothing owed yet · dues accrue daily at midnight"
                    : paying ? collectionNote ?? "walk the round and carry it home"
                    : "he pays us nothing yet"),
                open && paying && collectionDue && !closure.Shut));

            // Repair is an owner's cash decision, not a crew job. It appears only on a
            // damaged premises that is actually on our deed; protecting somebody else's
            // door never grants authority to spend on his building.
            if (closure.Shut && closure.RepairVisible)
                into.Add(TerritoryRacketOrder.Repair(
                    RepairLabel,
                    closure.RepairAvailable
                        ? "reopen it now · skipped racket income is not recovered"
                        : "not enough cash in the safe",
                    closure.RepairAvailable,
                    closure.RepairPrice));

            // What may be done TO the door is the shared table's call, never a tenure
            // test written out a second time - the map's planner reads the same one.
            // The wrecking is part of the LADDER, not a separate trade: an owner who
            // only wavers under a threat is the man a smashed front is meant to settle,
            // so it stands open from the first visit.
            Door(OrderType.SmashUp, SmashLabel, "wreck the front · he rebuilds or he pays");
            Door(OrderType.Torch, TorchLabel, "burn him out");
            Door(OrderType.Raid, RobLabel, "empty the till · a one-night take, not a round");
            Door(OrderType.Guard, GuardLabel, "our men stand on his door");

            var deed = DoorOrders.Refusal(OrderType.BuyPremises, tenure);
            var buyRefusal = deed ?? (hasCrew ? null : "nobody is picked to send");
            into.Add(new TerritoryRacketOrder(
                OrderType.BuyPremises, BuyLabel,
                buyRefusal ?? (askingPrice > 0
                    ? "the deed, bought outright"
                    : "these premises carry no asking price on the book"),
                buyRefusal == null && askingPrice > 0, askingPrice));

            void Door(OrderType type, string label, string note)
            {
                // The deed's rule first - it is the one that explains the door - then the
                // damage already done to it, and last the plain fact that there is nobody
                // to send. Work FILED with the office is still men walking somewhere, so
                // a key with no crew behind it must fade here rather than be taken, sat
                // on for a second and refused where the reader never looks.
                var refusal = DoorOrders.Refusal(type, tenure)
                              ?? DamageRefusal(type, closure)
                              ?? (hasCrew ? null : "nobody is picked to send");
                into.Add(new TerritoryRacketOrder(
                    type, label, refusal ?? note, refusal == null));
            }
        }

        static string DamageRefusal(OrderType type, TerritoryDoorClosure closure)
        {
            if (!closure.Shut)
                return null;
            if (type == OrderType.SmashUp)
                return closure.Cause == BusinessShutdownCause.Arson
                    ? "the premises are burned out"
                    : "the premises are already smashed up";
            if (type == OrderType.Torch &&
                closure.Cause == BusinessShutdownCause.Arson)
                return "the premises are already torched";
            return null;
        }

        static string Ask(TerritoryProtectionState standing)
        {
            switch (standing)
            {
                case TerritoryProtectionState.Defiant: return "put it to him again";
                // A man whose windows are in is not merely undecided, and the row must
                // not say he is: the wrecking was the argument, and this is the moment
                // it is worth anything.
                case TerritoryProtectionState.Intimidated:
                    return "he is shaken - ask him now";
                case TerritoryProtectionState.Hesitant:
                    return "he is wavering - press him";
                default: return "tell him how it works around here";
            }
        }
    }
}
