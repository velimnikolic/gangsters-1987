using System.Globalization;
using LivingCity.Personnel;

namespace LivingCity.UI
{
    /// <summary>
    /// Every string the personnel ledger shows - the intention-helper discipline
    /// (PoliceIntention et al.): pure, free of UnityEngine.Object, so the headless suite
    /// asserts exhaustiveness and column budgets here instead of the player finding an
    /// empty cell on screen. RosterOps' refusal reasons live here too, so a failed
    /// operation explains itself in the ledger's own voice.
    ///
    /// No star or half glyphs anywhere: neither U+2605 nor U+00BD is trusted to be in the
    /// LiberationSans atlas, so stars are drawn as Images and numbers read "3.5".
    /// </summary>
    public static class LedgerText
    {
        // ---------------------------------------------------------------- labels

        public static string AttributeLabel(CharacterAttribute attribute) => attribute switch
        {
            CharacterAttribute.Intelligence => "Intelligence",
            CharacterAttribute.Organization => "Organization",
            CharacterAttribute.Business => "Business",
            CharacterAttribute.Firearms => "Firearms",
            CharacterAttribute.Fists => "Fists",
            CharacterAttribute.Knives => "Knives",
            CharacterAttribute.Arson => "Arson",
            CharacterAttribute.Explosives => "Explosives",
            CharacterAttribute.Intimidation => "Intimidation",
            CharacterAttribute.Driving => "Driving",
            CharacterAttribute.Stealth => "Stealth",
            _ => "",
        };

        /// <summary>The hover note over a card's attribute row - what the stat is FOR,
        /// in the order table's own terms, so the tooltip teaches the system the number
        /// feeds instead of restating the label.</summary>
        public static string AttributeNote(CharacterAttribute attribute) => attribute switch
        {
            CharacterAttribute.Intelligence =>
                "Brain work: collecting protection, audits, recruiting, bribes and " +
                "police contacts. A lieutenancy leans on it.",
            CharacterAttribute.Organization =>
                "How much of the armory a lieutenant actually gets into his crew's " +
                "hands. A lieutenancy leans on it.",
            CharacterAttribute.Business =>
                "Buying premises, setting up and running businesses, and knowing " +
                "where a donation does the most good.",
            CharacterAttribute.Firearms =>
                "Gun work: raids, kills, patrols, guard duty, ambushes. The best " +
                "shots draw the guns when gear is dealt.",
            CharacterAttribute.Fists =>
                "Muscle: assaults, smash-ups and kidnappings.",
            CharacterAttribute.Knives =>
                "Quiet blade work, for the jobs that turn close and personal.",
            CharacterAttribute.Arson =>
                "Torch jobs - burning the place without burning the block.",
            CharacterAttribute.Explosives =>
                "Bomb jobs - a charge that goes off once, where it was meant to.",
            CharacterAttribute.Intimidation =>
                "The lean: extortion, threats and raising protection rates.",
            CharacterAttribute.Driving =>
                "Behind the wheel. The best drivers draw the cars when gear is dealt.",
            CharacterAttribute.Stealth =>
                "Moving unseen - exploring and scouting another outfit's turf.",
            _ => "",
        };

        public static string RankLabel(Rank rank) => rank switch
        {
            Rank.Hood => "Hood",
            Rank.Lieutenant => "Lieutenant",
            _ => "",
        };

        public static string SpecialtyLabel(Specialty specialty) => specialty switch
        {
            Specialty.None => "",
            Specialty.Accountant => "Accountant",
            Specialty.Lawyer => "Lawyer",
            _ => "",
        };

        public static string StatusLabel(CharacterStatus status) => status switch
        {
            CharacterStatus.Active => "Active",
            CharacterStatus.Jailed => "Jailed",
            CharacterStatus.Hospitalized => "Hospitalized",
            CharacterStatus.Dead => "Dead",
            _ => "",
        };

        public static string EquipmentLabel(EquipmentKind kind) => kind switch
        {
            EquipmentKind.Pistol => "Pistol",
            EquipmentKind.Vehicle => "Vehicle",
            EquipmentKind.Shotgun => "Shotgun",
            EquipmentKind.Rifle => "Rifle",
            EquipmentKind.TommyGun => "Tommy Gun",
            EquipmentKind.TwinPistols => "Twin Pistols",
            EquipmentKind.MachinePistol => "Machine Pistol",
            _ => "",
        };

        /// <summary>
        /// The balance sheet's exact figure: "$1,247", "-$300", "$0". Deliberately NOT
        /// BusinessIntention.Money, which abbreviates to "$1.2k" for 280px popups - an
        /// accounting column that rounds is worse than no column.
        /// </summary>
        public static string Cash(int amount)
        {
            var magnitude = amount < 0 ? -amount : amount;
            var figure = "$" + magnitude.ToString("N0", CultureInfo.InvariantCulture);
            return amount < 0 ? "-" + figure : figure;
        }

        public static string RiskLabel(Outfit.RiskRating risk) => risk switch
        {
            Outfit.RiskRating.None => "None",
            Outfit.RiskRating.Low => "Low",
            Outfit.RiskRating.Moderate => "Moderate",
            Outfit.RiskRating.High => "High",
            _ => "",
        };

        public static string InsufficientFunds(int price, int safe) =>
            "The safe holds " + Cash(safe) + "; that costs " + Cash(price) + ".";

        /// <summary>"3" or "3.5" - halves only when earned, invariant culture so the
        /// ledger reads the same whatever the machine's locale.</summary>
        public static string Stars(int halfSteps)
        {
            var whole = halfSteps / 2;
            return (halfSteps & 1) == 0
                ? whole.ToString(CultureInfo.InvariantCulture)
                : whole.ToString(CultureInfo.InvariantCulture) + ".5";
        }

        // ----------------------------------------------------------- composed lines

        public static string CrewName(string lieutenantSurname) =>
            "CREW OF " + lieutenantSurname.ToUpperInvariant();

        public static string AssignmentLine(Assignment assignment, string crewName) =>
            assignment.Kind switch
            {
                AssignmentKind.Crew => crewName,
                AssignmentKind.Front => "Runs the front",
                AssignmentKind.Specialist => "On retainer",
                _ => "Unassigned",
            };

        public static string HeldByLine(string holderName) => "held by " + holderName;

        public static string MemberCount(int count) =>
            count == 1 ? "1 MAN ON THE BOOKS" : count + " MEN ON THE BOOKS";

        // ------------------------------------------------------- warnings and confirms

        public static string PromoteWarning(string name) =>
            name + " is short on brains or order for a lieutenant's job. Promote anyway?";

        public static string TommyGunWarning(string name) =>
            name + " is a poor shot - in his hands the tommy gun sprays the street.";

        // ---------------------------------------------------------------- diplomacy

        public static string StanceLabel(Outfit.Stance stance) => stance switch
        {
            Outfit.Stance.Peace => "Peace",
            Outfit.Stance.Truce => "Truce",
            Outfit.Stance.War => "War",
            _ => "",
        };

        /// <summary>The system most likely to kill a crew unexplained - so the page
        /// says outright what each stance does, in one sentence each.</summary>
        public static string StanceEffect(Outfit.Stance stance) => stance switch
        {
            Outfit.Stance.Peace =>
                "PEACE — no engagement. Your men and theirs pass in the street, " +
                "claimed ground or not.",
            Outfit.Stance.Truce =>
                "TRUCE — territorial. Their men engage yours caught inside THEIR " +
                "territory, and yours engage theirs on YOURS. Neutral ground stays quiet.",
            Outfit.Stance.War =>
                "WAR — on sight. Their men engage yours anywhere in the city, and " +
                "yours theirs.",
            _ => "",
        };

        public const string StanceTakesEffect =
            "Stance changes take effect when the week is committed - never mid-plan.";

        public const string StrengthUnknown = "Unknown — no eyes inside";

        public const string ReasonOwnOutfit =
            "You cannot take a stance toward your own outfit.";

        // -------------------------------------------------------------------- orders

        public static string OrderLabel(Outfit.OrderType type) => type switch
        {
            Outfit.OrderType.Extort => "Extort",
            Outfit.OrderType.Intimidate => "Intimidate",
            Outfit.OrderType.CollectProtection => "Collect Protection",
            Outfit.OrderType.AdjustProtection => "Adjust Protection",
            Outfit.OrderType.Assault => "Assault",
            Outfit.OrderType.SmashUp => "Smash Up",
            Outfit.OrderType.Raid => "Raid",
            Outfit.OrderType.Torch => "Torch",
            Outfit.OrderType.Bomb => "Bomb",
            Outfit.OrderType.Kill => "Kill",
            Outfit.OrderType.Kidnap => "Kidnap",
            Outfit.OrderType.Patrol => "Patrol",
            Outfit.OrderType.Guard => "Guard",
            Outfit.OrderType.Ambush => "Ambush",
            Outfit.OrderType.Explore => "Explore",
            Outfit.OrderType.BuyPremises => "Buy Premises",
            Outfit.OrderType.SetUpBusiness => "Set Up Business",
            Outfit.OrderType.RunBusiness => "Run Business",
            Outfit.OrderType.Audit => "Audit",
            Outfit.OrderType.Recruit => "Recruit",
            Outfit.OrderType.Bribe => "Bribe",
            Outfit.OrderType.EmployPolice => "Employ Police",
            Outfit.OrderType.Donate => "Donate",
            _ => "",
        };

        public static string CategoryLabel(Outfit.OrderCategory category) => category switch
        {
            Outfit.OrderCategory.Extortion => "Extortion & Territory",
            Outfit.OrderCategory.Violence => "Violence",
            Outfit.OrderCategory.Defense => "Defense & Recon",
            Outfit.OrderCategory.Business => "Business",
            Outfit.OrderCategory.Influence => "Personnel & Influence",
            _ => "",
        };

        public static string TargetModeHint(Outfit.TargetMode mode) =>
            mode == Outfit.TargetMode.Area
                ? "Drag a box on the map - every eligible block inside becomes a target."
                : "Click one building or one man on the map.";

        public static string RequirementLine(CharacterAttribute attribute, int floorHalfSteps) =>
            floorHalfSteps <= 0
                ? "No particular talent required."
                : "Wants " + AttributeLabel(attribute) + " " + Stars(floorHalfSteps) + "+.";

        public static string OutcomeLabel(Outfit.OrderOutcome outcome) => outcome switch
        {
            Outfit.OrderOutcome.Completed => "Completed",
            Outfit.OrderOutcome.Failed => "Failed",
            Outfit.OrderOutcome.NeverReached => "Never reached",
            _ => "",
        };

        public static string CommittedLine(int committed, int available) =>
            committed + " of " + available + " men committed";

        public const string ReasonNoTargets = "No targets picked.";
        public const string ReasonNoSuchOrder = "No such order in the queue.";
        public const string ReasonNoCrewSelected = "Pick a lieutenant first.";

        public static string DemoteConfirm(string name, int hoodCount) => hoodCount switch
        {
            0 => "Disband " + name + "'s crew? It has no men.",
            1 => "Disband " + name + "'s crew? One man returns to the pool.",
            _ => "Disband " + name + "'s crew? " + hoodCount + " men return to the pool.",
        };

        // -------------------------------------------------------------- op refusals

        public const string ReasonNoSuchMember = "No such man on the books.";
        public const string ReasonNoSuchCrew = "No such crew.";
        public const string ReasonNoSuchItem = "No such item in the stock.";
        public const string ReasonDead = "The man is dead.";
        public const string ReasonSpecialist = "A specialist stays on retainer.";
        public const string ReasonAlreadyLieutenant = "He already runs a crew.";
        public const string ReasonNotLieutenant = "He does not run a crew.";
        public const string ReasonLieutenantMoves = "A lieutenant is demoted, not reassigned.";
        public const string ReasonAlreadyInCrew = "He is already in that crew.";
        public const string ReasonCrewFull = "A lieutenant runs four men at most.";
        public const string ReasonAlreadyFront = "He already runs the front.";
        public const string ReasonAlreadyHolds = "He already holds it.";
        public const string ReasonNotHeld = "Nobody holds it.";
        public const string ReasonGearViaLieutenant =
            "Gear goes to a lieutenant - his crew draws from him.";
    }
}
