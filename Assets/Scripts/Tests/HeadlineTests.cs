using System.Collections.Generic;
using LivingCity.News;

namespace LivingCity.Tests
{
    /// <summary>
    /// The news layer's model: page determinism, slot hygiene, the text budget, the
    /// 1987 calendar's dates, and the masthead's weekday anchor. GateTests' discipline:
    /// a plain static class, failures returned as data, no UnityEngine - the News core
    /// is engine-free on purpose.
    /// </summary>
    public static class HeadlineTests
    {
        public static List<string> Run()
        {
            var failures = new List<string>();

            SameSeedSamePage(failures);
            DifferentSeedsDiffer(failures);
            FullYearSweepIsClean(failures);
            QuietDayCoversEveryDesk(failures);
            HistoricalEventLeadsItsDay(failures);
            QuietDayHasNoHistorical(failures);
            CalendarDatesAreValid(failures);
            MastheadAnchorsHold(failures);

            return failures;
        }

        // ------------------------------------------------------------------ determinism

        static void SameSeedSamePage(List<string> failures)
        {
            var date = NewsDate.FromClockDay(40);
            var a = HeadlineGenerator.FrontPage(1234, date);
            var b = HeadlineGenerator.FrontPage(1234, date);

            if (a.Length != b.Length)
            {
                failures.Add("FrontPage: same seed, different story counts.");
                return;
            }
            for (var i = 0; i < a.Length; i++)
                if (a[i].Text != b[i].Text || a[i].Desk != b[i].Desk)
                    failures.Add($"FrontPage: same seed, story {i} differs.");
        }

        static void DifferentSeedsDiffer(List<string> failures)
        {
            var date = NewsDate.FromClockDay(40);
            var a = HeadlineGenerator.FrontPage(1, date);
            var b = HeadlineGenerator.FrontPage(2, date);

            var identical = a.Length == b.Length;
            if (identical)
                for (var i = 0; i < a.Length; i++)
                    if (a[i].Text != b[i].Text)
                    {
                        identical = false;
                        break;
                    }
            if (identical)
                failures.Add("FrontPage: seeds 1 and 2 printed the same paper.");
        }

        // ------------------------------------------------------------------ hygiene

        /// <summary>Every day of the campaign year, two seeds, max-size pages: no
        /// unfilled slot, nothing over budget, no duplicate story on one page.</summary>
        static void FullYearSweepIsClean(List<string> failures)
        {
            foreach (var seed in new[] { 11, 1987 })
                for (var day = 0; day <= 360; day++)
                {
                    var page = HeadlineGenerator.FrontPage(seed, NewsDate.FromClockDay(day), 12);
                    var seen = new HashSet<string>();
                    foreach (var story in page)
                    {
                        if (string.IsNullOrEmpty(story.Text))
                            failures.Add($"Sweep: empty headline (seed {seed}, day {day}).");
                        else
                        {
                            if (story.Text.IndexOf('{') >= 0 || story.Text.IndexOf('}') >= 0)
                                failures.Add($"Sweep: unfilled slot in \"{story.Text}\" (seed {seed}, day {day}).");
                            if (story.Text.Length > HeadlineGenerator.TextBudget)
                                failures.Add($"Sweep: over budget ({story.Text.Length}): \"{story.Text}\".");
                            if (!seen.Add(story.Text))
                                failures.Add($"Sweep: duplicate \"{story.Text}\" (seed {seed}, day {day}).");
                        }
                    }
                    if (failures.Count > 8)
                        return; // one broken template floods the sweep; the first few name it
                }
        }

        static void QuietDayCoversEveryDesk(List<string> failures)
        {
            // February 10th has no calendar entry, so six stories = six desks.
            var page = HeadlineGenerator.FrontPage(7, new NewsDate(2, 10));
            var desks = new HashSet<HeadlineDesk>();
            foreach (var story in page)
                desks.Add(story.Desk);
            if (desks.Count != 6)
                failures.Add($"Quiet day: {desks.Count} desks on a six-story page, want all 6.");
        }

        // ------------------------------------------------------------------ calendar

        static void HistoricalEventLeadsItsDay(List<string> failures)
        {
            var page = HeadlineGenerator.FrontPage(5, new NewsDate(10, 19));
            if (page.Length == 0 || !page[0].Historical)
                failures.Add("Calendar: October 19th page does not lead with history.");
            else if (!page[0].Text.Contains("BLACK MONDAY"))
                failures.Add($"Calendar: October 19th lead is \"{page[0].Text}\".");
        }

        static void QuietDayHasNoHistorical(List<string> failures)
        {
            var page = HeadlineGenerator.FrontPage(5, new NewsDate(2, 10));
            foreach (var story in page)
                if (story.Historical)
                    failures.Add($"Calendar: phantom event on February 10th: \"{story.Text}\".");
        }

        static void CalendarDatesAreValid(List<string> failures)
        {
            var monthLengths = new[] { 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };
            var dates = new HashSet<int>();
            foreach (var e in NewsCalendar.All)
            {
                if (e.Month < 1 || e.Month > 12 || e.Day < 1 || e.Day > monthLengths[e.Month - 1])
                    failures.Add($"Calendar: impossible date {e.Month}/{e.Day}.");
                else if (!dates.Add(e.Month * 100 + e.Day))
                    failures.Add($"Calendar: two entries share {e.Month}/{e.Day}; TryGet returns one.");
                if (string.IsNullOrEmpty(e.Text) || e.Text.Length > HeadlineGenerator.TextBudget)
                    failures.Add($"Calendar: entry {e.Month}/{e.Day} breaks the text budget.");
            }
        }

        // ------------------------------------------------------------------ masthead

        static void MastheadAnchorsHold(List<string> failures)
        {
            // The campaign opens Monday, January 5th 1987 - a real Monday.
            var opening = NewsDate.FromClockDay(0).Masthead();
            if (opening != "MONDAY, JANUARY 5, 1987")
                failures.Add($"Masthead: day 0 prints \"{opening}\".");

            // Black Monday fell on a Monday, 287 clock days in (Jan 5 -> Oct 19).
            var crash = NewsDate.FromClockDay(287);
            if (crash.Month != 10 || crash.Day != 19)
                failures.Add($"Masthead: day 287 lands on {crash.Month}/{crash.Day}, want 10/19.");
            else if (!crash.Masthead().StartsWith("MONDAY"))
                failures.Add($"Masthead: Black Monday prints \"{crash.Masthead()}\".");

            // Past New Year's Eve the date clamps rather than wrapping into 1988.
            var clamped = NewsDate.FromClockDay(400).Masthead();
            if (clamped != "THURSDAY, DECEMBER 31, 1987")
                failures.Add($"Masthead: day 400 prints \"{clamped}\".");
        }
    }
}
