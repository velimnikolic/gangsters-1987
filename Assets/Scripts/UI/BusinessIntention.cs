using System.Globalization;
using LivingCity.Gameplay;

namespace LivingCity.UI
{
    /// <summary>
    /// The words of the business overlay: what an establishment calls itself and the one line
    /// that says who runs it and what it takes. Pure, no UnityEngine.Object - PoliceIntention's
    /// rule - so the format can be proven headless and a future economy changes numbers, not
    /// popup code.
    ///
    /// Titles take a roll rather than a Random so the CALLER owns the draw order - the whole
    /// determinism of the ownership pass rests on PropertyDirector drawing in one fixed
    /// sequence, and a words helper that drew for itself would couple that sequence to table
    /// sizes.
    /// </summary>
    public static class BusinessIntention
    {
        static readonly string[] CafeNames =
        {
            "Bella Notte Café",
            "Caffè Sorrento",
            "Luna Café",
            "Café Roma",
            "The Espresso Bar",
            "Corner Perk Café",
        };

        static readonly string[] RestaurantNames =
        {
            "Trattoria Marchetti",
            "Mama Rosa's",
            "The Golden Fork",
            "Blue Moon Diner",
            "Little Napoli",
            "The Brass Rail",
        };

        static readonly string[] BurgerNames =
        {
            "Big Al's Burgers",
            "Charcoal King",
            "The Patty Wagon",
            "Star Burger Drive-In",
            "Duke's Char-Grill",
            "Boulevard Burgers",
        };

        /// <summary>One casino per city (uniqueBuildings' ceiling), so the table buys
        /// variety ACROSS campaigns rather than across town.</summary>
        static readonly string[] CasinoNames =
        {
            "The Golden Ace",
            "Lucky Seven Casino",
            "The Roulette Room",
            "Club Paradiso",
            "The Velvet Dice",
            "Starlight Casino",
        };

        /// <summary>Longest prefix first, so "industry-factory" cannot swallow the hall and
        /// the old works before they are told apart.</summary>
        static readonly (string prefix, string word)[] IndustrialWords =
        {
            ("industry-factory-old", "Old Factory"),
            ("industry-factory-hall", "Factory Hall"),
            ("industry-factory", "Factory"),
            ("industry-warehouse", "Warehouse"),
            ("industry-storage", "Storage Yard"),
            ("industry-refinery", "Refinery"),
            ("industry-building", "Works"),
        };

        public static string IndustrialTitle(string prefabName, int blockId)
        {
            var word = "Works";
            foreach (var (prefix, candidate) in IndustrialWords)
            {
                if (!prefabName.StartsWith(prefix))
                    continue;

                word = candidate;
                break;
            }

            return blockId < 0 ? word : $"{word} — Block {blockId}";
        }

        public static string CommercialTitle(string prefabName, int roll)
        {
            if (prefabName.StartsWith("building-cafe"))
                return CafeNames[roll % CafeNames.Length];
            if (prefabName.StartsWith("building-restaurant"))
                return RestaurantNames[roll % RestaurantNames.Length];
            if (prefabName.StartsWith("building-burger-joint"))
                return BurgerNames[roll % BurgerNames.Length];
            if (prefabName.StartsWith("building-casino"))
                return CasinoNames[roll % CasinoNames.Length];

            return "Post Office";
        }

        public static string PortTitle() => "City Docks";

        /// <summary>The popup's one sentence: the gazda, the takings, and whether anyone is
        /// paying for peace yet. No "Owner:" label - the longest boss name plus one would
        /// overflow the popup's 280px, and a name leading the line under a business title
        /// reads as the owner anyway. Middot separators are the player line's idiom. Null
        /// owner reads as Unknown rather than crashing - the marker is registered a frame
        /// before the director names anyone.</summary>
        public static string Line(string owner, int weeklyIncome, bool isProtected) =>
            $"{owner ?? "Unknown owner"} · {Money(weeklyIncome)}/wk · " +
            (isProtected ? "Protected" : "Unprotected");

        /// <summary>A gang front's line: the gang name REPLACES the protection word rather
        /// than joining it - the fourth clause would blow the 44-char budget, and a family
        /// name standing where "Protected" stands says the same thing better.</summary>
        public static string Line(string owner, int weeklyIncome, bool isProtected, string gang) =>
            string.IsNullOrEmpty(gang)
                ? Line(owner, weeklyIncome, isProtected)
                : $"{owner ?? "Unknown owner"} · {Money(weeklyIncome)}/wk · {gang}";

        /// <summary>$850, then $1.2k from a thousand up. Invariant culture: a popup that says
        /// $1,2k on one machine and $1.2k on another is the same bug twice.</summary>
        public static string Money(int perWeek) =>
            perWeek >= 1000
                ? "$" + (perWeek / 1000f).ToString("0.#", CultureInfo.InvariantCulture) + "k"
                : "$" + perWeek.ToString(CultureInfo.InvariantCulture);
    }
}
