namespace LivingCity.Gangs
{
    /// <summary>
    /// The fixed facts of the five gangs: who exists, what they are called, and what
    /// their people wear. Engine-free tables - the headless suite asserts they stay
    /// aligned and inside the popup budgets.
    ///
    /// Model names resolve against the shipped PrefabDatabase.pedestrianGroups by NAME
    /// ONLY (GangDirector's read-only scan) - every entry below is already wired into
    /// the groups, so no AuthorPedestrians menu re-run is needed. The lieutenant always
    /// wears a different model from his soldiers, so rank reads at a glance.
    /// </summary>
    public static class GangCatalog
    {
        public const int GangCount = 5;
        public const int PlayerGangId = 0;

        /// <summary>Names are budgeted: they stand where "Protected" stands in the
        /// business popup line (see BusinessIntention.Line's gang overload).</summary>
        public static readonly string[] Names =
        {
            "The Outfit",   // 0 - the player's organisation
            "Falcone",
            "Santoro",
            "Lucchese",
            "DeMarco",
        };

        /// <summary>Indexed by gang id.</summary>
        public static readonly string[] SoldierModels =
        {
            "man-mafia_AI",        // the classic crew in suits and fedoras
            "man_business_AI",     // Falcone - grey-suit corporate mob
            "man_coat_winter_AI",  // Santoro - long-coat muscle
            "man_punk_AI",         // Lucchese - a 1980s street crew gone made
            "man-shirt_AI",        // DeMarco - open-collar working outfit
        };

        /// <summary>Indexed by gang id.</summary>
        public static readonly string[] LieutenantModels =
        {
            "man-tie_AI",
            "man-mafia_AI",
            "man_business_AI",
            "man_coat_winter_AI",
            "man-casual_AI",
        };
    }
}
