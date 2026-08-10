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
        public const string ReasonAlreadyFront = "He already runs the front.";
        public const string ReasonAlreadyHolds = "He already holds it.";
        public const string ReasonNotHeld = "Nobody holds it.";
    }
}
