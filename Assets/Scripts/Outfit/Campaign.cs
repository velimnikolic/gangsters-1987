namespace LivingCity.Outfit
{
    /// <summary>
    /// The campaign calendar: which week of the operation this is. Pure data, free of
    /// UnityEngine (the Personnel discipline). The week is the strategy layer's clock -
    /// CityClock's day/hour cycle stays the AMBIENT clock (light, commutes, the school
    /// run) and neither drives the other: a week ends when the player commits it at the
    /// planning table, never because a wall clock ticked.
    ///
    /// The year is DERIVED so every page reads the same date and no page can drift; the
    /// almanac's old hard-coded "1980" title is exactly the bug this class retires.
    /// </summary>
    public sealed class Campaign
    {
        /// <summary>The era's opening year - 1987, the setting the whole game is written
        /// to (see Docs/1987-period-reference.md); the newspaper's calendar agrees.</summary>
        public const int StartYear = 1987;

        public const int WeeksPerYear = 52;

        /// <summary>1-based; week 1 is the campaign's first planning table.</summary>
        public int Week = 1;

        public int Year => StartYear + (Week - 1) / WeeksPerYear;

        public int WeekOfYear => (Week - 1) % WeeksPerYear + 1;
    }
}
