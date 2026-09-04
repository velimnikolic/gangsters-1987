namespace LivingCity.Outfit
{
    /// <summary>
    /// HOW THE PLAYER'S CAMPAIGN ENDED. One latch, three doors, and the leaf the scene
    /// prints reads off this rather than guessing from the roster - by the time the end
    /// is on the screen the books have already moved on from the reason for it.
    ///
    /// The user's word of 2026-09-04, asked as three questions and answered as three:
    /// out of money, the Don shot, the Don sentenced. A raid, a lost hood and an empty
    /// turf map are NOT among them, and nothing else is to be added here without him.
    ///
    /// Only the player ends this way. A rival family is finished by
    /// <see cref="House.Finished"/> - its books empty or its chair empty - and by
    /// nothing else, so a bad fortnight never quietly wipes a house off the city.
    /// </summary>
    public enum OutfitEnding
    {
        /// <summary>Still playing.</summary>
        None,

        /// <summary>The Don is dead and no lieutenant was left to take the chair. The
        /// oldest rule, and the only one that also finishes a rival house.</summary>
        TheDonIsDead,

        /// <summary>The Don was convicted and is not coming out - a life sentence.
        /// A servable term is not the end: the house runs under his heir until he is
        /// discharged (the user's word, 2026-09-04).</summary>
        TheDonGoesDown,

        /// <summary>Three nights running with an empty safe and men going home with
        /// nothing. The night the first hoods desert is the night the books close.</summary>
        TheBooksAreClosed,
    }

    /// <summary>
    /// What the end is CALLED, in one place, so the warning in the console and the
    /// black leaf on the screen cannot drift apart. Text only - it decides nothing.
    /// </summary>
    public static class EndingText
    {
        /// <summary>The headline, set the way the morning paper would set it.</summary>
        public static string Headline(OutfitEnding ending) => ending switch
        {
            OutfitEnding.TheDonGoesDown => "THE DON GOES DOWN",
            OutfitEnding.TheBooksAreClosed => "THE BOOKS ARE CLOSED",
            _ => "THE DON IS DEAD",
        };

        /// <summary>The single line under the rule that names what happened, with the
        /// man's name in it where there is one to use.</summary>
        public static string Standfirst(OutfitEnding ending, string bossName, int day)
        {
            var who = string.IsNullOrEmpty(bossName)
                ? "The head of the family"
                : bossName;
            return ending switch
            {
                OutfitEnding.TheDonGoesDown =>
                    who + " was sentenced to life on day " + day + ".",
                OutfitEnding.TheBooksAreClosed =>
                    "The safe was empty for the third night running on day " + day + ".",
                _ => who + " was shot dead on day " + day + ".",
            };
        }

        /// <summary>The closing paragraph: why there is no next order.</summary>
        public static string Closing(OutfitEnding ending) => ending switch
        {
            OutfitEnding.TheDonGoesDown =>
                "He gives his orders to a wall now, and a life sentence is not a term " +
                "a family waits out. The books close where they stand: no round is " +
                "collected, no man is paid, and no street changes hands from here.",
            OutfitEnding.TheBooksAreClosed =>
                "Three nights the men went home with nothing, and on the third they " +
                "stopped coming back. There is no payroll, no signing money and no " +
                "outfit left to give an order to.",
            _ =>
                "There is nobody to give the next order. The books close where they " +
                "stand: no round is collected, no man is paid, and no street changes " +
                "hands from here.",
        };
    }
}
