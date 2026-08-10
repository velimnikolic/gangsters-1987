using System.Collections.Generic;
using LivingCity.Personnel;

namespace LivingCity.Outfit
{
    public enum OrderCategory
    {
        Extortion,
        Violence,
        Defense,
        Business,
        Influence,
    }

    public enum TargetMode
    {
        /// <summary>Drag a box; every eligible block inside becomes a target.</summary>
        Area,

        /// <summary>One click on one building or one man.</summary>
        Point,
    }

    public enum OrderType
    {
        // Extortion & Territory
        Extort,
        Intimidate,
        CollectProtection,
        AdjustProtection,

        // Violence
        Assault,
        SmashUp,
        Raid,
        Torch,
        Bomb,
        Kill,
        Kidnap,

        // Defense & Reconnaissance
        Patrol,
        Guard,
        Ambush,
        Explore,

        // Business
        BuyPremises,
        SetUpBusiness,
        RunBusiness,
        Audit,

        // Personnel & Influence
        Recruit,
        Bribe,
        EmployPolice,
        Donate,
    }

    /// <summary>One order type's fixed facts: how it targets, what it costs in labour,
    /// and the attribute the job card checks (warn-but-permit, the house rule).</summary>
    public readonly struct OrderSpec
    {
        public readonly OrderType Type;
        public readonly OrderCategory Category;
        public readonly TargetMode Mode;

        /// <summary>Area orders: blocks one man covers in a travel-free week.</summary>
        public readonly float BlocksPerManWeek;

        /// <summary>Point orders: man-weeks of work at the door.</summary>
        public readonly float PointCost;

        /// <summary>The stat the job lives by; floor 0 = no requirement.</summary>
        public readonly CharacterAttribute PrimaryAttribute;
        public readonly int PrimaryFloorHalfSteps;

        public OrderSpec(OrderType type, OrderCategory category, TargetMode mode,
            float blocksPerManWeek, float pointCost,
            CharacterAttribute primaryAttribute, int primaryFloorHalfSteps)
        {
            Type = type;
            Category = category;
            Mode = mode;
            BlocksPerManWeek = blocksPerManWeek;
            PointCost = pointCost;
            PrimaryAttribute = primaryAttribute;
            PrimaryFloorHalfSteps = primaryFloorHalfSteps;
        }
    }

    /// <summary>The full order table from the reference sheet, grouped by category.
    /// Floors are in half-steps (6 = 3.0 stars, 7 = 3.5).</summary>
    public static class OrderTable
    {
        public static readonly OrderSpec[] Specs =
        {
            // Extortion & Territory
            new OrderSpec(OrderType.Extort, OrderCategory.Extortion, TargetMode.Area,
                2.5f, 0f, CharacterAttribute.Intimidation, 6),
            new OrderSpec(OrderType.Intimidate, OrderCategory.Extortion, TargetMode.Point,
                0f, 0.4f, CharacterAttribute.Intimidation, 7),
            new OrderSpec(OrderType.CollectProtection, OrderCategory.Extortion,
                TargetMode.Area, 8f, 0f, CharacterAttribute.Intelligence, 0),
            new OrderSpec(OrderType.AdjustProtection, OrderCategory.Extortion,
                TargetMode.Point, 0f, 0.3f, CharacterAttribute.Intimidation, 0),

            // Violence
            new OrderSpec(OrderType.Assault, OrderCategory.Violence, TargetMode.Point,
                0f, 0.5f, CharacterAttribute.Fists, 7),
            new OrderSpec(OrderType.SmashUp, OrderCategory.Violence, TargetMode.Point,
                0f, 0.5f, CharacterAttribute.Fists, 6),
            new OrderSpec(OrderType.Raid, OrderCategory.Violence, TargetMode.Point,
                0f, 0.7f, CharacterAttribute.Firearms, 6),
            new OrderSpec(OrderType.Torch, OrderCategory.Violence, TargetMode.Point,
                0f, 0.6f, CharacterAttribute.Arson, 6),
            new OrderSpec(OrderType.Bomb, OrderCategory.Violence, TargetMode.Point,
                0f, 0.8f, CharacterAttribute.Explosives, 6),
            new OrderSpec(OrderType.Kill, OrderCategory.Violence, TargetMode.Point,
                0f, 0.7f, CharacterAttribute.Firearms, 6),
            new OrderSpec(OrderType.Kidnap, OrderCategory.Violence, TargetMode.Point,
                0f, 0.8f, CharacterAttribute.Fists, 6),

            // Defense & Reconnaissance
            new OrderSpec(OrderType.Patrol, OrderCategory.Defense, TargetMode.Area,
                4f, 0f, CharacterAttribute.Firearms, 6),
            new OrderSpec(OrderType.Guard, OrderCategory.Defense, TargetMode.Point,
                0f, 1f, CharacterAttribute.Firearms, 6),
            new OrderSpec(OrderType.Ambush, OrderCategory.Defense, TargetMode.Area,
                3f, 0f, CharacterAttribute.Firearms, 7),
            new OrderSpec(OrderType.Explore, OrderCategory.Defense, TargetMode.Area,
                6f, 0f, CharacterAttribute.Stealth, 0),

            // Business
            new OrderSpec(OrderType.BuyPremises, OrderCategory.Business, TargetMode.Point,
                0f, 0.3f, CharacterAttribute.Business, 0),
            new OrderSpec(OrderType.SetUpBusiness, OrderCategory.Business, TargetMode.Point,
                0f, 1f, CharacterAttribute.Business, 6),
            new OrderSpec(OrderType.RunBusiness, OrderCategory.Business, TargetMode.Point,
                0f, 1f, CharacterAttribute.Business, 6),
            new OrderSpec(OrderType.Audit, OrderCategory.Business, TargetMode.Point,
                0f, 0.5f, CharacterAttribute.Intelligence, 6),

            // Personnel & Influence
            new OrderSpec(OrderType.Recruit, OrderCategory.Influence, TargetMode.Point,
                0f, 0.5f, CharacterAttribute.Intelligence, 7),
            new OrderSpec(OrderType.Bribe, OrderCategory.Influence, TargetMode.Point,
                0f, 0.3f, CharacterAttribute.Intelligence, 6),
            new OrderSpec(OrderType.EmployPolice, OrderCategory.Influence, TargetMode.Point,
                0f, 0.4f, CharacterAttribute.Intelligence, 6),
            new OrderSpec(OrderType.Donate, OrderCategory.Influence, TargetMode.Point,
                0f, 0.2f, CharacterAttribute.Business, 0),
        };

        public static OrderSpec SpecOf(OrderType type)
        {
            for (var i = 0; i < Specs.Length; i++)
                if (Specs[i].Type == type)
                    return Specs[i];
            return Specs[0];
        }
    }

    /// <summary>A confirmed order in a lieutenant's queue. Draft orders never become
    /// one of these - the unconfirmed state lives only on the job card, which is the
    /// whole point of the confirm step.</summary>
    public sealed class PlannedOrder
    {
        public int Id;
        public int CrewId;
        public OrderType Type;

        /// <summary>Area targets - block ids.</summary>
        public readonly List<int> BlockTargets = new List<int>();

        /// <summary>Point target: where it is and what to call it.</summary>
        public int TargetBlockId = -1;
        public float TargetX;
        public float TargetZ;
        public string TargetLabel = "";

        public int Men = 1;

        public int TargetCount => BlockTargets.Count > 0 ? BlockTargets.Count : 1;
    }

    /// <summary>This week's confirmed orders, in execution priority order.</summary>
    public sealed class WeekPlan
    {
        public readonly List<PlannedOrder> Confirmed = new List<PlannedOrder>();

        int nextOrderId;

        public int NextOrderId() => nextOrderId++;

        public int CommittedMen(int crewId)
        {
            var total = 0;
            for (var i = 0; i < Confirmed.Count; i++)
                if (Confirmed[i].CrewId == crewId)
                    total += Confirmed[i].Men;
            return total;
        }
    }

    public enum OrderOutcome
    {
        Completed,
        Failed,
        NeverReached,
    }

    /// <summary>Last week's record - a snapshot, because it is a RECORD: the fact of
    /// what was ordered and what came of it, never re-derived. Failure rows carry no
    /// reason on purpose; running out of week is labelled apart from failing.</summary>
    public sealed class OrderRecord
    {
        public string Lieutenant = "";
        public OrderType Type;
        public string TargetSummary = "";
        public int Men;
        public OrderOutcome Outcome;
    }

    /// <summary>
    /// The capacity arithmetic - the game's central tension, and deliberately NOT a
    /// flat cap. A crew's week is its men; travel eats a fraction of every assigned
    /// man's week before work starts, so distance and the vehicle genuinely drive what
    /// a crew can take on. All floats derived at read, displayed rounded.
    /// </summary>
    public static class OrderMath
    {
        /// <summary>Metres of round-trip reach a man on foot spends a whole week on -
        /// on foot the outfit works its own neighbourhood, which is the design.</summary>
        public const float FootWeekRange = 1200f;

        /// <summary>A car makes the whole city a neighbourhood - the first vehicle is
        /// the purchase that changes the game's shape.</summary>
        public const float VehicleWeekRange = 6000f;

        public const float MaxTravelFraction = 0.9f;

        public static float TravelFraction(float distanceMeters, bool hasVehicle)
        {
            var range = hasVehicle ? VehicleWeekRange : FootWeekRange;
            var fraction = distanceMeters / range;
            return fraction > MaxTravelFraction ? MaxTravelFraction
                : fraction < 0f ? 0f
                : fraction;
        }

        /// <summary>Man-weeks the job itself needs, before anyone walks anywhere.</summary>
        public static float WorkRequired(in OrderSpec spec, int targetCount) =>
            spec.Mode == TargetMode.Area
                ? targetCount / (spec.BlocksPerManWeek > 0f ? spec.BlocksPerManWeek : 1f)
                : spec.PointCost;

        /// <summary>How many men finish the job within the week, travel included.</summary>
        public static int MenNeeded(in OrderSpec spec, int targetCount,
            float travelFraction)
        {
            var perMan = 1f - travelFraction;
            if (perMan < 0.1f)
                perMan = 0.1f;
            var men = (int)System.Math.Ceiling(WorkRequired(spec, targetCount) / perMan);
            return men < 1 ? 1 : men;
        }

        /// <summary>True when the assigned men cannot finish inside the week.</summary>
        public static bool Undermanned(in OrderSpec spec, int targetCount,
            float travelFraction, int men) =>
            men * (1f - travelFraction) + 0.0001f < WorkRequired(spec, targetCount);

        /// <summary>
        /// Which confirmed orders a crew never reaches: walking the queue in priority
        /// order, every order whose running men total exceeds the crew's size is past
        /// the line. Over-assignment is allowed - this is the line the player crossed,
        /// marked, never enforced.
        /// </summary>
        public static void PastTheLine(WeekPlan plan, int crewId, int crewSize,
            List<int> pastOrderIds)
        {
            pastOrderIds.Clear();
            var running = 0;
            for (var i = 0; i < plan.Confirmed.Count; i++)
            {
                var order = plan.Confirmed[i];
                if (order.CrewId != crewId)
                    continue;
                running += order.Men;
                if (running > crewSize)
                    pastOrderIds.Add(order.Id);
            }
        }
    }

    /// <summary>Crew-side lookups the job card needs, kept pure.</summary>
    public static class CrewKit
    {
        /// <summary>The crew rides if ANY member - the lieutenant included - holds a
        /// vehicle from the stock. One car serves the crew; who signed it out is a
        /// personnel matter, not a logistics one.</summary>
        public static bool HasVehicle(Roster roster, Crew crew)
        {
            if (roster == null || crew == null)
                return false;

            for (var i = 0; i < roster.Equipment.Count; i++)
            {
                var item = roster.Equipment[i];
                if (item.Kind != EquipmentKind.Vehicle ||
                    item.HolderId == RosterEquipment.Unheld)
                    continue;
                if (crew.LieutenantId == item.HolderId ||
                    crew.HoodIds.Contains(item.HolderId))
                    return true;
            }
            return false;
        }

        /// <summary>Crew size as labour: the lieutenant works too.</summary>
        public static int MenOf(Crew crew) => crew == null ? 0 : 1 + crew.HoodIds.Count;

        /// <summary>The crew's best half-steps at a stat, dead men excluded.</summary>
        public static int BestAt(Roster roster, Crew crew, CharacterAttribute attribute)
        {
            if (roster == null || crew == null)
                return 0;

            var best = 0;
            void Consider(int id)
            {
                var member = roster.Find(id);
                if (member == null || member.Status == CharacterStatus.Dead)
                    return;
                var value = member.GetHalfSteps(attribute);
                if (value > best)
                    best = value;
            }

            Consider(crew.LieutenantId);
            foreach (var id in crew.HoodIds)
                Consider(id);
            return best;
        }
    }
}
