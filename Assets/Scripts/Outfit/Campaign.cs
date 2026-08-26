namespace LivingCity.Outfit
{
    /// <summary>
    /// The campaign calendar. The game runs in REAL TIME - the city clock turns, days
    /// pass, and nothing waits for the player to commit anything - so the DAY is the
    /// unit this class counts and every coarser figure is derived from it: the week a
    /// balance sheet covers, the year the newspaper prints, the payday that falls due.
    /// Pure data, free of UnityEngine (the Personnel discipline); the clock that moves
    /// <see cref="Day"/> lives in the scene and pushes through OutfitDirector.
    ///
    /// A week survives here as a DERIVED period, not as a turn: wages are still a
    /// Friday envelope and the books are still kept in weekly sheets, because that is
    /// how a 1987 outfit paid its men - but time never stops for either.
    /// </summary>
    public sealed class Campaign
    {
        /// <summary>The era's opening year - 1987, the setting the whole game is written
        /// to (see Docs/1987-period-reference.md); the newspaper's calendar agrees.</summary>
        public const int StartYear = 1987;

        public const int DaysPerWeek = 7;

        /// <summary>Kept here as well as on the clocks so the pure layer can turn a
        /// clock reading into one number without referencing UnityEngine.</summary>
        public const float HoursPerDay = 24f;

        public const int WeeksPerYear = 52;

        /// <summary>364 - a calendar of whole weeks, so a week never straddles a year
        /// and the payday cycle never has to special-case a stub week.</summary>
        public const int DaysPerYear = DaysPerWeek * WeeksPerYear;

        static readonly string[] DayNames =
            { "MONDAY", "TUESDAY", "WEDNESDAY", "THURSDAY", "FRIDAY", "SATURDAY", "SUNDAY" };

        /// <summary>1-based; day 1 is the campaign's first morning.</summary>
        public int Day = 1;

        public int Week => (Day - 1) / DaysPerWeek + 1;

        public int Year => StartYear + (Day - 1) / DaysPerYear;

        public int WeekOfYear => (Week - 1) % WeeksPerYear + 1;

        /// <summary>0 is Monday - day one of the campaign opens the working week. Floored
        /// at day one rather than trusting the field: <see cref="Day"/> is public data a
        /// save or a debug key can set, and a zero would index the day names at -1.</summary>
        public int DayOfWeek => (Day > 1 ? Day - 1 : 0) % DaysPerWeek;

        public string DayName => DayNames[DayOfWeek];

        /// <summary>True when this day opens a fresh week: the sheet just closed, the
        /// men were paid, the books turned over. Day one opens nothing - it IS the
        /// first week, and an outfit does not pay wages before anybody has worked.</summary>
        public static bool OpensWeek(int day) => day > 1 && (day - 1) % DaysPerWeek == 0;

        public static int WeekOf(int day) => (day - 1) / DaysPerWeek + 1;
    }
}
