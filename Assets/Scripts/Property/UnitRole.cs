using LivingCity.Personnel;

namespace LivingCity.Property
{
    /// <summary>What a flat is USED for. A flat earns nothing on its own; the role is what
    /// the outfit turns the room into, and every role needs a keeper.</summary>
    public enum UnitRole
    {
        Empty = 0,
        Armory,
        CashStash,
        Safehouse,
        Infirmary,
        Garage,
        CardRoom,
        Brothel,

        /// <summary>Holds the kilos (EPIC 40). Its heat is read off what is in it.</summary>
        Stash,
    }

    /// <summary>One role's paper: what fitting it out costs, what it draws in police
    /// attention while the door is open, and what it takes in a day.</summary>
    public readonly struct UnitRoleSpec
    {
        public UnitRoleSpec(
            UnitRole role, string label, string shortLabel, int fitOut, int heat, int earn,
            bool needsBank, CharacterAttribute wants, string what)
        {
            Role = role;
            Label = label;
            ShortLabel = shortLabel;
            FitOut = fitOut;
            Heat = heat;
            Earn = earn;
            NeedsBank = needsBank;
            Wants = wants;
            What = what;
        }

        public UnitRole Role { get; }
        public string Label { get; }
        public string ShortLabel { get; }

        /// <summary>Cash on the table to fit the room out, paid once.</summary>
        public int FitOut { get; }

        /// <summary>Police attention a day while the door is open. FLAT-004 decides how it
        /// is deposited - the block's pool DECAYS on an eight-hour half-life, so a daily
        /// lump of four never moves the gauge.</summary>
        public int Heat { get; }

        /// <summary>What it takes in a day while open. Only the card room and the brothel
        /// take anything: the rest hold goods or people. Their money is ILLEGAL income and
        /// needs washing like everything illegal.</summary>
        public int Earn { get; }

        /// <summary>The card room needs money behind the table or it is shut.</summary>
        public bool NeedsBank { get; }

        /// <summary>The skill the keeper is judged on for this room.</summary>
        public CharacterAttribute Wants { get; }

        /// <summary>One line of what the room does, for the picker.</summary>
        public string What { get; }
    }

    public static class UnitRoles
    {
        /// <summary>What one girl takes in a night, and what she is paid for it. The
        /// house's cut is the difference, which is why a brothel with nobody in it takes
        /// nothing at all (FLAT-007).</summary>
        public const int BrothelTakePerGirl = 210;
        public const int BrothelWagePerGirl = 70;

        /// <summary>The most a room can hold.</summary>
        public const int BrothelGirls = 4;

        /// <summary>A doctor on a day rate. He halves nothing on his own - he makes the
        /// bed shorter, which the day tick spends.</summary>
        public const int DoctorWagePerDay = 95;
        public const int DoctorDays = 1;

        /// <summary>Which roles hire anybody at all, and how many.</summary>
        public static int StaffCeiling(UnitRole role) => role switch
        {
            UnitRole.Brothel => BrothelGirls,
            UnitRole.Infirmary => 1,
            _ => 0,
        };

        public static int StaffWage(UnitRole role) => role switch
        {
            UnitRole.Brothel => BrothelWagePerGirl,
            UnitRole.Infirmary => DoctorWagePerDay,
            _ => 0,
        };

        public static string StaffWord(UnitRole role) => role switch
        {
            UnitRole.Brothel => "GIRLS WORKING",
            UnitRole.Infirmary => "THE DOCTOR",
            _ => "",
        };

        /// <summary>Every role in the order the picker lists them. Costs and heat are the
        /// design brief's table (Docs/design-briefs/apartments-brief.md §3); the income
        /// figures are proposals until FLAT-009 balances them.</summary>
        public static readonly UnitRoleSpec[] All =
        {
            new UnitRoleSpec(UnitRole.Armory, "ARMORY", "ARMORY", 5_000, 1, 0, false,
                CharacterAttribute.Awareness,
                "holds the guns; a lieutenant draws from the nearest one"),
            new UnitRoleSpec(UnitRole.CashStash, "CASH STASH", "STASH", 3_000, 1, 0, false,
                CharacterAttribute.Organization,
                "takes money off the front, out of the declared sheet"),
            new UnitRoleSpec(UnitRole.Safehouse, "SAFEHOUSE", "SAFE", 2_000, 0, 0, false,
                CharacterAttribute.Stealth,
                "a wanted man lies low here until the heat comes off him"),
            new UnitRoleSpec(UnitRole.Infirmary, "INFIRMARY", "INFIRM", 8_000, 1, 0, false,
                CharacterAttribute.Awareness,
                "a hurt man heals here with no police report on it"),
            new UnitRoleSpec(UnitRole.Garage, "GARAGE", "GARAGE", 6_000, 1, 0, false,
                CharacterAttribute.Driving,
                "a car used in a job cools here; bombs are fitted here"),
            new UnitRoleSpec(UnitRole.CardRoom, "CARD ROOM", "CARD", 15_000, 3, 450, true,
                CharacterAttribute.Organization,
                "a game every night, and the house keeps its cut"),
            new UnitRoleSpec(UnitRole.Brothel, "BROTHEL", "BROTHEL", 10_000, 4,
                BrothelTakePerGirl, false,
                CharacterAttribute.Persuasion,
                "girls work the rooms and the house takes its half"),
            new UnitRoleSpec(UnitRole.Stash, "STASH", "STASH",
                LivingCity.Outfit.EconomyPrices.StashFitOut, 1, 0, false,
                CharacterAttribute.Stealth,
                "holds the kilos; a raid takes them and seals the room, no case"),
        };

        public static UnitRoleSpec Of(UnitRole role)
        {
            for (var i = 0; i < All.Length; i++)
                if (All[i].Role == role)
                    return All[i];
            return new UnitRoleSpec(UnitRole.Empty, "NO ROLE", "NONE", 0, 0, 0, false,
                CharacterAttribute.Organization, "nothing is run out of it yet");
        }

        public static string Label(UnitRole role) =>
            role == UnitRole.Empty ? "NO ROLE" : Of(role).Label;

        /// <summary>Storage roles hold goods or people and take no nightly cut. Said once
        /// here so the sheet and the day tick cannot disagree about which do.</summary>
        public static bool Earns(UnitRole role) => Of(role).Earn > 0;
    }
}
