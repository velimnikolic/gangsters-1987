namespace LivingCity.Outfit
{
    /// <summary>
    /// The campaign calendar. The game runs in REAL TIME - the city clock turns, days
    /// pass, and nothing waits for the player to commit anything - so the DAY is the
    /// unit this class counts, and it is the ONLY accounting period there is: the books
    /// close every midnight, the men are paid every midnight, and nothing anywhere is
    /// owed to a week.
    ///
    /// A week survives here as a CALENDAR fact and nothing else - Monday follows Sunday
    /// because 1987 had weekdays, not because anything falls due on one. There is no
    /// turn to end, no sheet to commit, no envelope to wait for. Anything that used to
    /// count weeks now counts days; anything that used to wait for a boundary happens
    /// as it happens.
    ///
    /// Pure data, free of UnityEngine (the Personnel discipline); the clock that moves
    /// <see cref="Day"/> lives in the scene and pushes through OutfitDirector.
    /// </summary>
    public sealed class Campaign
    {
        /// <summary>The era's opening year - 1987, the setting the whole game is written
        /// to (see Docs/1987-period-reference.md); the newspaper's calendar agrees.</summary>
        public const int StartYear = 1987;

        /// <summary>The calendar's week: seven names in a cycle. It dates a sheet and
        /// nothing more - no money, no discharge and no countdown reads it.</summary>
        public const int DaysInCalendarWeek = 7;

        /// <summary>Kept here as well as on the clocks so the pure layer can turn a
        /// clock reading into one number without referencing UnityEngine.</summary>
        public const float HoursPerDay = 24f;

        /// <summary>364 - a calendar of whole weeks, so the weekday a date falls on is
        /// the same every year and the newspaper's masthead never has to special-case
        /// a stub. Purely a dating convenience; nothing is paid on it.</summary>
        public const int DaysPerYear = DaysInCalendarWeek * 52;

        /// <summary>How many days of closed sheets the books show at once - the
        /// finances page's "LAST SEVEN DAYS". A window over the record, not a period
        /// the record is divided into: the player pages one day at a time through it.</summary>
        public const int BooksWindow = 7;

        static readonly string[] DayNames =
            { "MONDAY", "TUESDAY", "WEDNESDAY", "THURSDAY", "FRIDAY", "SATURDAY", "SUNDAY" };

        /// <summary>1-based; day 1 is the campaign's first morning.</summary>
        public int Day = 1;

        public int Year => StartYear + (Day - 1) / DaysPerYear;

        /// <summary>0 is Monday - day one of the campaign opens on one. Floored at day
        /// one rather than trusting the field: <see cref="Day"/> is public data a save
        /// or a debug key can set, and a zero would index the day names at -1.</summary>
        public int DayOfWeek => (Day > 1 ? Day - 1 : 0) % DaysInCalendarWeek;

        public string DayName => DayNames[DayOfWeek];

        /// <summary>True when this day closes a sheet and pays the men: EVERY day but
        /// the first. Day one settles nothing - it IS the first day, and an outfit does
        /// not pay wages before anybody has worked one.</summary>
        public static bool Settles(int day) => day > 1;
    }
}
