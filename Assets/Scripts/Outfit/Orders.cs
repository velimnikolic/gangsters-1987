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

    /// <summary>How a job is decided when the crew has done the hours.</summary>
    public enum JobResolution
    {
        /// <summary>A seeded roll against the crew's best man at the job's trade -
        /// there is no scene to play out, so the arithmetic IS the event.</summary>
        Roll,

        /// <summary>The street decides. The crew goes there and the sim plays it out;
        /// what the record says is what actually happened on the road.</summary>
        Street,

        /// <summary>It never finishes. The men stand it until they are called off,
        /// earning their practice a day at a time.</summary>
        Standing,
    }

    /// <summary>One order type's fixed facts: how it targets, how long it takes a man,
    /// what it pays and costs, and the attribute the job lives by.</summary>
    public readonly struct OrderSpec
    {
        public readonly OrderType Type;
        public readonly OrderCategory Category;
        public readonly TargetMode Mode;

        /// <summary>Game-hours of work ONE man owes per target, travel excluded. Two
        /// men on it halve the hours; a standing order ignores the figure.</summary>
        public readonly float HoursPerTarget;

        public readonly JobResolution Resolution;

        /// <summary>The stat the job lives by; floor 0 = no stated requirement, which
        /// still resolves against <see cref="OrderResolution.ImplicitFloorHalfSteps"/>
        /// so stars never stop mattering.</summary>
        public readonly CharacterAttribute PrimaryAttribute;
        public readonly int PrimaryFloorHalfSteps;

        /// <summary>Money the job pays per target when it comes off, before the
        /// attribute scaling in <see cref="OrderResolution"/>.</summary>
        public readonly int Payout;

        /// <summary>Money the attempt costs per target - paid whether or not it comes
        /// off, because a bribe that bought nothing is still a bribe that was paid.</summary>
        public readonly int Cost;

        /// <summary>Police attention one target generates. Nothing consumes it yet -
        /// the record carries it so the police layer can read the past when it lands.</summary>
        public readonly int Heat;

        public OrderSpec(OrderType type, OrderCategory category, TargetMode mode,
            float hoursPerTarget, JobResolution resolution,
            CharacterAttribute primaryAttribute, int primaryFloorHalfSteps,
            int payout = 0, int cost = 0, int heat = 0)
        {
            Type = type;
            Category = category;
            Mode = mode;
            HoursPerTarget = hoursPerTarget;
            Resolution = resolution;
            PrimaryAttribute = primaryAttribute;
            PrimaryFloorHalfSteps = primaryFloorHalfSteps;
            Payout = payout;
            Cost = cost;
            Heat = heat;
        }
    }

    /// <summary>
    /// The full order table, grouped by category. Floors are in half-steps (6 = 3.0
    /// stars, 7 = 3.5).
    ///
    /// The hours are the game's pacing dial and are written as hours ON PURPOSE rather
    /// than derived from an abstract work unit: a player watching his men cross town
    /// reads the clock, so the number that decides how long he waits should be the
    /// number a designer edits. At the city clock's default speed a game hour is a
    /// handful of real seconds, so a 16-hour shakedown is a coffee break and a 40-hour
    /// premises fit-out is an evening's play.
    /// </summary>
    public static class OrderTable
    {
        public static readonly OrderSpec[] Specs =
        {
            // Extortion & Territory
            new OrderSpec(OrderType.Extort, OrderCategory.Extortion, TargetMode.Area,
                16f, JobResolution.Roll, CharacterAttribute.Intimidation, 6,
                payout: EconomyPrices.Shakedown, heat: 2),
            new OrderSpec(OrderType.Intimidate, OrderCategory.Extortion, TargetMode.Point,
                16f, JobResolution.Roll, CharacterAttribute.Intimidation, 7, heat: 2),
            new OrderSpec(OrderType.CollectProtection, OrderCategory.Extortion,
                TargetMode.Area, 5f, JobResolution.Roll, CharacterAttribute.Awareness, 0,
                payout: 60, heat: 1),
            new OrderSpec(OrderType.AdjustProtection, OrderCategory.Extortion,
                TargetMode.Point, 4f, JobResolution.Roll, CharacterAttribute.Intimidation, 0),

            // Violence - the street decides these; the hours are getting there and
            // waiting for the mark, not the seconds the thing itself takes.
            new OrderSpec(OrderType.Assault, OrderCategory.Violence, TargetMode.Point,
                8f, JobResolution.Street, CharacterAttribute.Combat, 7, heat: 4),
            new OrderSpec(OrderType.SmashUp, OrderCategory.Violence, TargetMode.Point,
                8f, JobResolution.Street, CharacterAttribute.Combat, 6, heat: 5),
            new OrderSpec(OrderType.Raid, OrderCategory.Violence, TargetMode.Point,
                10f, JobResolution.Street, CharacterAttribute.Combat, 6,
                payout: EconomyPrices.Raid, heat: 8),
            new OrderSpec(OrderType.Torch, OrderCategory.Violence, TargetMode.Point,
                10f, JobResolution.Street, CharacterAttribute.Combat, 6, heat: 10),
            new OrderSpec(OrderType.Bomb, OrderCategory.Violence, TargetMode.Point,
                12f, JobResolution.Street, CharacterAttribute.Combat, 6, heat: 14),
            new OrderSpec(OrderType.Kill, OrderCategory.Violence, TargetMode.Point,
                12f, JobResolution.Street, CharacterAttribute.Combat, 6, heat: 12),
            new OrderSpec(OrderType.Kidnap, OrderCategory.Violence, TargetMode.Point,
                14f, JobResolution.Street, CharacterAttribute.Combat, 6,
                payout: EconomyPrices.KidnapCut, heat: 9),

            // Defense & Reconnaissance - a watch is stood, never finished.
            new OrderSpec(OrderType.Patrol, OrderCategory.Defense, TargetMode.Area,
                10f, JobResolution.Standing, CharacterAttribute.Combat, 6),
            new OrderSpec(OrderType.Guard, OrderCategory.Defense, TargetMode.Point,
                24f, JobResolution.Standing, CharacterAttribute.Combat, 6),
            new OrderSpec(OrderType.Ambush, OrderCategory.Defense, TargetMode.Area,
                12f, JobResolution.Standing, CharacterAttribute.Combat, 7),
            new OrderSpec(OrderType.Explore, OrderCategory.Defense, TargetMode.Area,
                7f, JobResolution.Roll, CharacterAttribute.Stealth, 0),

            // Business
            new OrderSpec(OrderType.BuyPremises, OrderCategory.Business, TargetMode.Point,
                6f, JobResolution.Roll, CharacterAttribute.Streetwise, 0,
                cost: EconomyPrices.EmptyStorefront),
            new OrderSpec(OrderType.SetUpBusiness, OrderCategory.Business, TargetMode.Point,
                40f, JobResolution.Roll, CharacterAttribute.Streetwise, 6,
                cost: EconomyPrices.SetUpBusiness),
            new OrderSpec(OrderType.RunBusiness, OrderCategory.Business, TargetMode.Point,
                24f, JobResolution.Standing, CharacterAttribute.Streetwise, 6, payout: 90),
            new OrderSpec(OrderType.Audit, OrderCategory.Business, TargetMode.Point,
                12f, JobResolution.Roll, CharacterAttribute.Awareness, 6),

            // Personnel & Influence
            // The signing money is the order's cost, so the one gate that moves money
            // moves it here too - the street bar's chip pays its own way separately.
            new OrderSpec(OrderType.Recruit, OrderCategory.Influence, TargetMode.Point,
                12f, JobResolution.Roll, CharacterAttribute.Awareness, 7, cost: 500),
            new OrderSpec(OrderType.Bribe, OrderCategory.Influence, TargetMode.Point,
                8f, JobResolution.Roll, CharacterAttribute.Awareness, 6,
                cost: EconomyPrices.Bribe),
            new OrderSpec(OrderType.EmployPolice, OrderCategory.Influence, TargetMode.Point,
                10f, JobResolution.Roll, CharacterAttribute.Awareness, 6,
                cost: EconomyPrices.PoliceOnThePad),
            new OrderSpec(OrderType.Donate, OrderCategory.Influence, TargetMode.Point,
                4f, JobResolution.Roll, CharacterAttribute.Streetwise, 0,
                cost: EconomyPrices.Donation),
        };

        public static OrderSpec SpecOf(OrderType type)
        {
            for (var i = 0; i < Specs.Length; i++)
                if (Specs[i].Type == type)
                    return Specs[i];
            return Specs[0];
        }

        /// <summary>
        /// Which work in the improvement table an order actually is. Several orders are
        /// the same lesson - a raid, a killing, an ambush and a kidnapping are all going
        /// at a rival, and a torching is leaning on somebody with fire - so the book's
        /// twenty-three types collapse onto far fewer rows. The map runs this way only:
        /// <see cref="Personnel.ActivityXp"/> knows nothing about order types, which is
        /// what lets the street layer bank practice for work nobody wrote in the book.
        /// </summary>
        public static Activity ActivityOf(OrderType type)
        {
            switch (type)
            {
                case OrderType.CollectProtection:
                case OrderType.AdjustProtection:
                    return Activity.RacketCollection;

                case OrderType.Extort:
                case OrderType.Intimidate:
                case OrderType.SmashUp:
                case OrderType.Torch:
                case OrderType.Bomb:
                    return Activity.Leaning;

                case OrderType.Assault:
                case OrderType.Raid:
                case OrderType.Kill:
                case OrderType.Kidnap:
                case OrderType.Ambush:
                    return Activity.AttackOnARival;

                case OrderType.Patrol:
                case OrderType.Guard:
                    return Activity.BlockPatrol;

                case OrderType.Explore:
                    return Activity.Scouting;

                case OrderType.BuyPremises:
                case OrderType.SetUpBusiness:
                case OrderType.RunBusiness:
                case OrderType.Audit:
                    return Activity.RunningABusiness;

                case OrderType.Recruit:
                    return Activity.Recruiting;

                case OrderType.Bribe:
                case OrderType.EmployPolice:
                case OrderType.Donate:
                    return Activity.Negotiation;

                default:
                    return Activity.BlockPatrol;
            }
        }
    }

    /// <summary>Where a job has got to. A crew works its queue one job at a time, so
    /// exactly one of a crew's jobs is ever past <see cref="Queued"/>.</summary>
    public enum JobStage
    {
        /// <summary>In the lieutenant's book, not yet started - the crew is busy.</summary>
        Queued,

        /// <summary>The men are on their way there.</summary>
        Travelling,

        /// <summary>The men are at the door, doing it.</summary>
        Working,

        /// <summary>Resolved and written into the record; dropped from the book on the
        /// next pass.</summary>
        Finished,
    }

    /// <summary>
    /// A job in a lieutenant's book. Issued from the ledger at any moment - there is no
    /// turn to submit it to - and worked in the running city: the men travel, they put
    /// in the hours, and the job resolves when the hours are done. Draft orders never
    /// become one of these; the unconfirmed state lives only on the job card.
    /// </summary>
    public sealed class Job
    {
        public int Id;
        public int CrewId;
        public OrderType Type;

        /// <summary>Area targets - block ids.</summary>
        public readonly List<int> BlockTargets = new List<int>();

        /// <summary>
        /// What the target itself is worth, when the caller knows: the week's protection
        /// from a shop of that kind, a day's net from one we own, the asking price of the
        /// premises. Zero means the caller could not say, and the order falls back on its
        /// book figure. This is how a barber and a casino stop paying the same money
        /// without every order growing a business lookup of its own.
        /// </summary>
        public int TargetWorth;

        /// <summary>Point target: where it is and what to call it.</summary>
        public int TargetBlockId = -1;
        public float TargetX;
        public float TargetZ;
        public string TargetLabel = "";

        /// <summary>The canonical business the job is against, when the caller knows -
        /// the raw id value, so this file owes the territory layer nothing. Empty for a
        /// job aimed at ground rather than at a door. It is what lets a finished buy
        /// transfer THAT deed and a finished raid frighten THAT shop, instead of the
        /// outcome evaporating into coordinates.</summary>
        public string TargetBusinessId = "";

        public int Men = 1;

        /// <summary>The campaign day it was issued - the record prints it and the roll
        /// is seeded off it, so the same day at the same seed decides the same way.</summary>
        public int IssuedDay = 1;

        public JobStage Stage = JobStage.Queued;

        /// <summary>Game-hours still to walk or drive before work can start.</summary>
        public float TravelHoursLeft;

        /// <summary>Game-hours of work still owed. A standing job holds its men and
        /// never counts down.</summary>
        public float WorkHoursLeft;

        /// <summary>Whole days the men have stood a standing job - what its practice
        /// is paid against.</summary>
        public int DaysStood;

        /// <summary>How many open jobs the lieutenant was carrying when this one came
        /// up. Frozen at that moment rather than read at resolution: the penalty is for
        /// the attention he had to spare while the work was being done, and a book that
        /// emptied afterwards does not retrospectively make the job go better.</summary>
        public int BookDepth;

        /// <summary>How the street answered a Violence job, once it has. Null until
        /// the sim reports, and the roll stands in for it if nothing ever does - a
        /// scene with no crew simulation still has to be able to play the game.</summary>
        public OrderOutcome? StreetOutcome;

        public bool Live => Stage != JobStage.Finished;

        /// <summary>Whether the target coordinates mean anything. A job issued from the
        /// map always carries them - the area orders take their blocks' centre - but a
        /// job built in a test does not, and a bare (0,0) would otherwise read as a
        /// point at the world origin and charge the crew a journey across the city.</summary>
        public bool HasPlace => TargetBlockId >= 0 || BlockTargets.Count > 0 ||
                                TargetLabel.Length > 0;

        public int TargetCount => BlockTargets.Count > 0 ? BlockTargets.Count : 1;
    }

    /// <summary>The outfit's open jobs, in the order the lieutenants took them.</summary>
    public sealed class OrderBook
    {
        public readonly List<Job> Jobs = new List<Job>();

        int nextJobId;

        public int NextJobId() => nextJobId++;

        /// <summary>The job a crew is actually on: the first of its jobs that is not
        /// finished. List order IS queue order, so moving a row moves the work.</summary>
        public Job CurrentFor(int crewId)
        {
            for (var i = 0; i < Jobs.Count; i++)
                if (Jobs[i].CrewId == crewId && Jobs[i].Live)
                    return Jobs[i];
            return null;
        }

        /// <summary>How many live jobs a crew is carrying, the one in hand included -
        /// the depth Organization is measured against.</summary>
        public int LiveCount(int crewId)
        {
            var count = 0;
            for (var i = 0; i < Jobs.Count; i++)
                if (Jobs[i].CrewId == crewId && Jobs[i].Live)
                    count++;
            return count;
        }

        /// <summary>Men a crew has out on the job right now - a queued job holds
        /// nobody, because nobody has left the front for it yet.</summary>
        public int MenOut(int crewId)
        {
            var current = CurrentFor(crewId);
            return current != null && current.Stage != JobStage.Queued ? current.Men : 0;
        }

        /// <summary>How far down the crew's book this job sits; 0 is the one in hand.</summary>
        public int DepthOf(Job job)
        {
            if (job == null)
                return 0;
            var depth = 0;
            for (var i = 0; i < Jobs.Count; i++)
            {
                var other = Jobs[i];
                if (other == job)
                    return depth;
                if (other.CrewId == job.CrewId && other.Live)
                    depth++;
            }
            return depth;
        }

        public void DropFinished()
        {
            for (var i = Jobs.Count - 1; i >= 0; i--)
                if (!Jobs[i].Live)
                    Jobs.RemoveAt(i);
        }
    }

    public enum OrderOutcome
    {
        Completed,
        Failed,

        /// <summary>Called off before it was done - the one way a live job leaves the
        /// book without an answer.</summary>
        CalledOff,
    }

    /// <summary>Last few days' record - a snapshot, because it is a RECORD: the fact of
    /// what was ordered and what came of it, never re-derived.</summary>
    public sealed class OrderRecord
    {
        public int Day;
        public string Lieutenant = "";
        public OrderType Type;
        public string TargetSummary = "";
        public int Men;
        public OrderOutcome Outcome;

        /// <summary>Money that moved on this job: payout less what the attempt cost.</summary>
        public int Money;

        public int Heat;
    }

    /// <summary>
    /// The realtime arithmetic: how long a job takes and how long getting there takes.
    /// The old man-week budget is gone with the weekly turn - what limits a crew now is
    /// that its men are genuinely somewhere, for as long as the work takes. Fewer men
    /// on a job does not make it impossible; it makes it slower, and the calendar is
    /// the price.
    /// </summary>
    public static class OrderMath
    {
        /// <summary>Metres a crew on foot covers in a game hour. Not a walking pace -
        /// it is a working pace, with the going there, the standing about and the
        /// coming back folded in. On foot the outfit works its own neighbourhood,
        /// which is the design.</summary>
        public const float FootMetresPerHour = 400f;

        /// <summary>A car makes the whole city a neighbourhood - the first vehicle is
        /// the purchase that changes the game's shape.</summary>
        public const float VehicleMetresPerHour = 2_000f;

        /// <summary>Nobody crosses town instantly, however good the driver.</summary>
        public const float MinTravelHours = 0.25f;

        public const float MaxTravelHours = 72f;

        /// <summary>A wheelman's stat on the road: 0.90 of the book speed at one star,
        /// 1.15 at five. Only a crew with a car gets it - a fast driver on foot is
        /// just a man walking.</summary>
        public static float DrivingScale(int halfSteps)
        {
            var t = (AttributeScale.Clamp(halfSteps) - AttributeScale.MinHalfSteps) /
                    (float)(AttributeScale.MaxHalfSteps - AttributeScale.MinHalfSteps);
            return 0.90f + 0.25f * t;
        }

        /// <summary>Game-hours to reach the job and be in a state to work.
        ///
        /// Two things scale the book speed and they are different things. The WHEELMAN
        /// is <see cref="DrivingScale"/> - what the man at the wheel is worth. The
        /// MACHINE is <paramref name="machineTop"/>, straight off the body the crew
        /// actually holds (CrewKit.MachineTopOf reads the armory listing through
        /// VehiclePerformance), and it is the same number the street uses to decide how
        /// fast that body drives - so a panel van is slow on the map for the same reason
        /// it is slow at a light, and the jalopy the player bought to save six hundred
        /// dollars costs him the difference in hours.
        ///
        /// A crew on foot gets neither: a fast driver with no car is a man walking, and
        /// so is a fast driver with a fast car he has not been given.</summary>
        public static float TravelHours(float distanceMeters, bool hasVehicle,
            int drivingHalfSteps, float machineTop = 1f)
        {
            if (distanceMeters < 0f)
                distanceMeters = 0f;
            if (machineTop <= 0f)
                machineTop = 1f;

            var speed = hasVehicle
                ? VehicleMetresPerHour * DrivingScale(drivingHalfSteps) * machineTop
                : FootMetresPerHour;
            var hours = distanceMeters / speed;

            if (hours < MinTravelHours)
                return MinTravelHours;
            return hours > MaxTravelHours ? MaxTravelHours : hours;
        }

        /// <summary>Game-hours of work the job itself owes, split across the men on it.
        /// A standing job returns 0 - it is never owed, only stood.</summary>
        public static float WorkHours(in OrderSpec spec, int targetCount, int men)
        {
            if (spec.Resolution == JobResolution.Standing)
                return 0f;
            if (targetCount < 1)
                targetCount = 1;
            if (men < 1)
                men = 1;
            return spec.HoursPerTarget * targetCount / men;
        }

        /// <summary>What the job card quotes before the player confirms.</summary>
        public static float TotalHours(in OrderSpec spec, int targetCount, int men,
            float distanceMeters, bool hasVehicle, int drivingHalfSteps,
            float machineTop = 1f) =>
            TravelHours(distanceMeters, hasVehicle, drivingHalfSteps, machineTop) +
            WorkHours(spec, targetCount, men);
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

        /// <summary>How fast the body the crew actually holds will go, as a share of the
        /// book speed (VehiclePerformance.Machine.Top) - 1 for a crew on foot, and 1 for
        /// a listing nobody has written a row for.
        ///
        /// The BEST of what they hold, for the same reason <see cref="HasVehicle"/> asks
        /// whether ANY of them holds one: a crew with two cars drives to the job in the
        /// quicker one, and which man signed it out is a personnel matter.
        ///
        /// Only cars. A motorcycle is not a crew's vehicle here and never was - it
        /// carries two men to a drive-by and counts as nobody's transport
        /// (ArmoryCatalog.Motorcycles says so), so a crew that owns a moped and nothing
        /// else still walks to work.</summary>
        public static float MachineTopOf(Roster roster, Crew crew)
        {
            var listing = VehicleOf(roster, crew);
            return listing.Length == 0
                ? 1f
                : LivingCity.Gameplay.VehiclePerformance.For(ArmoryCatalog.BodyFor(listing)).Top;
        }

        /// <summary>The armory listing of the car the crew would actually take - the
        /// quickest of what its men hold, or "" for a crew on foot. The job card names
        /// it, so a player reading eleven hours of travel can see it says "Panel Van".
        /// </summary>
        public static string VehicleOf(Roster roster, Crew crew)
        {
            if (roster == null || crew == null)
                return "";

            // The flag rather than the name, because a name may legitimately be empty and
            // "have I found one yet" is not the same question as "is it called anything".
            // Read off the name, a car with a blank listing would be passed over by every
            // later row whatever its pace - and MachineTopOf would then quote the book
            // speed for a crew that HasVehicle says is driving.
            var found = false;
            var bestName = "";
            var best = 0f;
            for (var i = 0; i < roster.Equipment.Count; i++)
            {
                var item = roster.Equipment[i];
                if (item.Kind != EquipmentKind.Vehicle ||
                    item.HolderId == RosterEquipment.Unheld)
                    continue;
                if (crew.LieutenantId != item.HolderId &&
                    !crew.HoodIds.Contains(item.HolderId))
                    continue;

                var top = LivingCity.Gameplay.VehiclePerformance
                    .For(ArmoryCatalog.BodyFor(item.DisplayName)).Top;
                if (found && top <= best)
                    continue;
                found = true;
                best = top;
                bestName = item.DisplayName ?? "";
            }
            return bestName;
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
                if (member == null || member.Gone)
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

        /// <summary>Fills the buffer with the ids of the men who go on a job: the
        /// lieutenant first, then his hoods in list order, up to the job's headcount.
        /// The dead and the deserted are skipped, so a job never books a ghost.</summary>
        public static void MenOnJob(Roster roster, Crew crew, int men, List<int> into)
        {
            into.Clear();
            if (roster == null || crew == null || men < 1)
                return;

            void Take(int id)
            {
                if (into.Count >= men)
                    return;
                var member = roster.Find(id);
                if (member != null && !member.Gone)
                    into.Add(id);
            }

            Take(crew.LieutenantId);
            foreach (var id in crew.HoodIds)
                Take(id);
        }
    }
}
