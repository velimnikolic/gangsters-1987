namespace LivingCity.News
{
    /// <summary>
    /// Which desk a headline came off. Drives page composition (one of each before any
    /// desk repeats) and lets a future newspaper UI style the sections differently -
    /// crime above the fold, culture at the bottom.
    /// </summary>
    public enum HeadlineDesk
    {
        Crime,      // the city's own blotter - gangs, bodies, raids
        DrugWar,    // crack corners, DEA, seizures
        Nation,     // RICO, Giuliani, Iran-Contra
        World,      // Gorbachev, Berlin, cartel homelands
        Business,   // the Dow, junk bonds, cash-only miracles
        Culture,    // what 1987 watched, wore and listened to
    }

    /// <summary>
    /// One headline. Pure data, free of UnityEngine (the Personnel discipline), so the
    /// headless suite can proof entire front pages without an Editor.
    /// </summary>
    public sealed class Headline
    {
        public HeadlineDesk Desk;
        public string Text = "";

        /// <summary>True for a real 1987 event pinned to today's date by NewsCalendar,
        /// false for generated filler. Historical entries lead the page - the UI can
        /// also badge them, the way period games wink at the player.</summary>
        public bool Historical;

        /// <summary>Which family the story names, or -1 when it names none. Set by the
        /// generator as it fills {GANG}, so the photo desk can print a man from the
        /// family the story is actually about rather than a stranger.</summary>
        public int GangId = -1;

        /// <summary>The press photo beside this story, or <see cref="NewsPhoto.None"/>
        /// for the text-only majority - a front page carries two or three pictures, not
        /// six. See <see cref="HeadlineGenerator.PhotosPerPage"/>.</summary>
        public NewsPhoto Photo = NewsPhoto.None;
    }

    /// <summary>
    /// A calendar day inside the fixed campaign year, 1987. Month and day only - the
    /// year never changes, so it is a constant here rather than a field. Maps from
    /// CityClock.Day so the paper's date advances with the city's.
    /// </summary>
    public readonly struct NewsDate
    {
        public const int Year = 1987;

        /// <summary>The campaign opens Monday, January 5th - the first working Monday
        /// of 1987, a week before the Commission sentencing lands as the first big
        /// historical front page the player sees.</summary>
        public const int CampaignStartMonth = 1;
        public const int CampaignStartDay = 5;

        /// <summary>1987 is not a leap year.</summary>
        static readonly int[] MonthLengths = { 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };

        static readonly string[] MonthNames =
        {
            "JANUARY", "FEBRUARY", "MARCH", "APRIL", "MAY", "JUNE",
            "JULY", "AUGUST", "SEPTEMBER", "OCTOBER", "NOVEMBER", "DECEMBER",
        };

        static readonly string[] WeekdayNames =
        {
            "MONDAY", "TUESDAY", "WEDNESDAY", "THURSDAY", "FRIDAY", "SATURDAY", "SUNDAY",
        };

        public readonly int Month;  // 1..12
        public readonly int Day;    // 1..MonthLengths[Month-1]

        public NewsDate(int month, int day)
        {
            Month = month;
            Day = day;
        }

        /// <summary>1..365. The seed mixer and the weekday both hang off this.</summary>
        public int DayOfYear
        {
            get
            {
                var doy = Day;
                for (var m = 0; m < Month - 1; m++)
                    doy += MonthLengths[m];
                return doy;
            }
        }

        /// <summary>
        /// The paper's date on CityClock day N (N is 0-based whole days since the
        /// campaign opened). Days past New Year's Eve clamp to December 31st rather
        /// than wrap - the year rolling over silently to a second identical 1987
        /// would be stranger than a long December.
        /// </summary>
        public static NewsDate FromClockDay(int clockDay)
        {
            var start = new NewsDate(CampaignStartMonth, CampaignStartDay);
            var doy = start.DayOfYear + (clockDay < 0 ? 0 : clockDay);
            if (doy > 365)
                doy = 365;

            var month = 1;
            while (doy > MonthLengths[month - 1])
            {
                doy -= MonthLengths[month - 1];
                month++;
            }
            return new NewsDate(month, doy);
        }

        /// <summary>"MONDAY, JANUARY 5, 1987" - ready for the masthead. January 1st,
        /// 1987 fell on a Thursday; everything else follows from that anchor.</summary>
        public string Masthead()
        {
            var weekday = WeekdayNames[(DayOfYear - 1 + 3) % 7];
            return weekday + ", " + MonthNames[Month - 1] + " " + Day + ", " + Year;
        }
    }
}
