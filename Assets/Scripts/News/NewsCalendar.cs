namespace LivingCity.News
{
    /// <summary>
    /// The real events of 1987, pinned to their dates. When the paper's date lands on
    /// one, it leads the front page; every other day the generator fills the page
    /// alone. Source material lives in Docs/1987-period-reference.md - anything added
    /// here should trace back to that file.
    ///
    /// A flat table searched linearly: sixteen entries, read once per in-game day.
    /// </summary>
    public static class NewsCalendar
    {
        public readonly struct Entry
        {
            public readonly int Month;
            public readonly int Day;
            public readonly HeadlineDesk Desk;
            public readonly string Text;

            public Entry(int month, int day, HeadlineDesk desk, string text)
            {
                Month = month;
                Day = day;
                Desk = desk;
                Text = text;
            }
        }

        /// <summary>Public so the headless suite can proof every entry against the
        /// same budgets as the generated pools.</summary>
        public static readonly Entry[] All =
        {
            new Entry(1, 13, HeadlineDesk.Nation,   "COMMISSION BOSSES GET 100 YEARS APIECE"),
            new Entry(3,  2, HeadlineDesk.Nation,   "PIZZA CONNECTION JURY CONVICTS HEROIN RING"),
            new Entry(3,  6, HeadlineDesk.World,    "ZEEBRUGGE FERRY CAPSIZES; SCORES DEAD"),
            new Entry(3, 13, HeadlineDesk.Crime,    "GOTTI WALKS: JURY CLEARS THE 'TEFLON DON'"),
            new Entry(5,  5, HeadlineDesk.Nation,   "IRAN-CONTRA HEARINGS OPEN ON CAPITOL HILL"),
            new Entry(6,  3, HeadlineDesk.Culture,  "'THE UNTOUCHABLES' OPENS: NESS VS CAPONE"),
            new Entry(6, 12, HeadlineDesk.World,    "REAGAN IN BERLIN: 'TEAR DOWN THIS WALL'"),
            new Entry(7,  7, HeadlineDesk.Nation,   "OLIVER NORTH TAKES THE STAND IN UNIFORM"),
            new Entry(7, 21, HeadlineDesk.Culture,  "GUNS N' ROSES DROP 'APPETITE FOR DESTRUCTION'"),
            new Entry(8, 31, HeadlineDesk.Culture,  "MICHAEL JACKSON'S 'BAD' HITS THE SHELVES"),
            new Entry(10, 19, HeadlineDesk.Business, "BLACK MONDAY: DOW PLUNGES 508 POINTS"),
            new Entry(10, 20, HeadlineDesk.Business, "STREET REELS AFTER WORST CRASH IN HISTORY"),
            new Entry(11, 18, HeadlineDesk.World,   "KING'S CROSS FIRE KILLS 31 IN LONDON TUBE"),
            new Entry(12,  8, HeadlineDesk.World,   "REAGAN, GORBACHEV SIGN INF TREATY"),
            new Entry(12, 11, HeadlineDesk.Culture, "'WALL STREET' PREMIERES: 'GREED IS GOOD'"),
        };

        public static bool TryGet(NewsDate date, out Entry entry)
        {
            foreach (var e in All)
            {
                if (e.Month == date.Month && e.Day == date.Day)
                {
                    entry = e;
                    return true;
                }
            }
            entry = default;
            return false;
        }
    }
}
