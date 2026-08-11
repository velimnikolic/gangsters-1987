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
            PhotosOnlyLeadThePage(failures);
            PhotosAreWellFormed(failures);
            CrimePhotoMatchesTheFamilyNamed(failures);
            NoFaceRunsTwiceOnAPage(failures);
            PictureDeskUsesOneDraw(failures);
            ScreenStaysInRange(failures);
            ScreenDarkensWithTheImage(failures);
            ScreenMakesDots(failures);

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

        // ------------------------------------------------------------------ picture desk

        static void PhotosOnlyLeadThePage(List<string> failures)
        {
            for (var day = 0; day < 40; day++)
            {
                var page = HeadlineGenerator.FrontPage(3, NewsDate.FromClockDay(day));
                for (var i = 0; i < page.Length; i++)
                {
                    var wanted = i < HeadlineGenerator.PhotosPerPage;
                    if (page[i].Photo.HasPicture != wanted)
                        failures.Add($"Photos: story {i} on day {day} " +
                                     (wanted ? "has no picture." : "carries a stray picture."));
                }
            }
        }

        static void PhotosAreWellFormed(List<string> failures)
        {
            foreach (var seed in new[] { 4, 1987 })
                for (var day = 0; day <= 360; day++)
                    foreach (var story in HeadlineGenerator.FrontPage(seed, NewsDate.FromClockDay(day)))
                    {
                        if (!story.Photo.HasPicture)
                            continue;

                        // The prefab name is a name the pack uses; a typo here surfaces
                        // in Play as a silent missing picture, so catch the shape now.
                        if (!story.Photo.ModelName.StartsWith("SM_"))
                            failures.Add($"Photos: \"{story.Photo.ModelName}\" is not a pack prefab name.");
                        if (string.IsNullOrEmpty(story.Photo.Caption))
                            failures.Add($"Photos: {story.Desk} picture has no caption.");
                        else if (story.Photo.Caption.Length > PictureDesk.CaptionBudget)
                            failures.Add($"Photos: caption over budget ({story.Photo.Caption.Length}): " +
                                         $"\"{story.Photo.Caption}\".");
                        if (failures.Count > 8)
                            return;
                    }
        }

        /// <summary>The paper prints a man from the family it just named - not a
        /// stranger, and not a soldier of a family the story never mentioned.</summary>
        static void CrimePhotoMatchesTheFamilyNamed(List<string> failures)
        {
            var checkedAny = false;

            for (var day = 0; day < 200; day++)
                foreach (var story in HeadlineGenerator.FrontPage(9, NewsDate.FromClockDay(day)))
                {
                    if (!story.Photo.HasPicture || story.Desk != HeadlineDesk.Crime || story.GangId < 0)
                        continue;

                    checkedAny = true;
                    var wanted = Gangs.GangCatalog.SoldierModels[story.GangId];
                    if (story.Photo.ModelName != wanted)
                        failures.Add($"Photos: \"{story.Text}\" ran a {story.Photo.ModelName} " +
                                     $"where gang {story.GangId} wears {wanted}.");
                }

            if (!checkedAny)
                failures.Add("Photos: 200 days produced no gang-named crime lead to check.");
        }

        /// <summary>
        /// Two desks drawing from the same table of suits used to run the same man
        /// twice on one front page. The exception is deliberate: two crime stories
        /// naming the SAME family both print that family's soldier.
        /// </summary>
        static void NoFaceRunsTwiceOnAPage(List<string> failures)
        {
            foreach (var seed in new[] { 6, 1987 })
                for (var day = 0; day <= 360; day++)
                {
                    var page = HeadlineGenerator.FrontPage(seed, NewsDate.FromClockDay(day));
                    var printed = new Dictionary<string, int>();

                    foreach (var story in page)
                    {
                        if (!story.Photo.HasPicture)
                            continue;

                        if (printed.TryGetValue(story.Photo.ModelName, out var firstGang))
                        {
                            var sameFamily = story.Desk == HeadlineDesk.Crime &&
                                             story.GangId >= 0 && story.GangId == firstGang;
                            if (!sameFamily)
                            {
                                failures.Add($"Photos: {story.Photo.ModelName} ran twice on " +
                                             $"day {day} (seed {seed}).");
                                return;
                            }
                        }
                        printed[story.Photo.ModelName] = story.GangId;
                    }
                }
        }

        /// <summary>
        /// The desk takes exactly one number from the stream whatever it decides, so
        /// changing a picture rule can never reshuffle the story after it. Two desks
        /// fed the same stream position must leave that stream at the same place.
        /// </summary>
        static void PictureDeskUsesOneDraw(List<string> failures)
        {
            foreach (HeadlineDesk desk in System.Enum.GetValues(typeof(HeadlineDesk)))
            {
                var rng = new System.Random(77);
                PictureDesk.For(desk, -1, rng);
                var after = rng.Next();

                var control = new System.Random(77);
                control.Next();
                if (after != control.Next())
                    failures.Add($"PictureDesk: the {desk} desk does not take exactly one draw.");
            }
        }

        // ------------------------------------------------------------------ newsprint

        static void ScreenStaysInRange(List<string> failures)
        {
            for (var step = 0; step <= 20; step++)
            {
                var luminance = step / 20f;
                for (var y = 0; y < 16; y++)
                    for (var x = 0; x < 16; x++)
                    {
                        var shade = Newsprint.Shade(luminance, x, y);
                        if (shade < 0f || shade > 1f || float.IsNaN(shade))
                        {
                            failures.Add($"Newsprint: Shade({luminance}, {x}, {y}) = {shade}.");
                            return;
                        }
                    }
            }
        }

        /// <summary>Darker in, more ink out - measured over a tile, since a single
        /// pixel may sit inside a dot at any tone.</summary>
        static void ScreenDarkensWithTheImage(List<string> failures)
        {
            var previous = -1f;
            for (var step = 0; step <= 10; step++)
            {
                var coverage = TileAverage(step / 10f);
                if (coverage < previous - 0.001f)
                {
                    failures.Add($"Newsprint: luminance {step / 10f:0.0} prints darker than the step below it.");
                    return;
                }
                previous = coverage;
            }

            if (TileAverage(0.05f) > 0.25f)
                failures.Add("Newsprint: near-black does not lay down solid ink.");
            if (TileAverage(0.95f) < 0.75f)
                failures.Add("Newsprint: near-white does not leave the paper bare.");
        }

        /// <summary>A flat mid-gray must come out as a dot pattern, not a flat field -
        /// that is the whole point of the screen.</summary>
        static void ScreenMakesDots(List<string> failures)
        {
            var min = 1f;
            var max = 0f;
            for (var y = 0; y < 24; y++)
                for (var x = 0; x < 24; x++)
                {
                    var shade = Newsprint.Shade(0.5f, x, y);
                    if (shade < min) min = shade;
                    if (shade > max) max = shade;
                }

            if (max - min < 0.5f)
                failures.Add($"Newsprint: mid-gray develops flat (spread {max - min:0.00}), no dot structure.");
        }

        static float TileAverage(float luminance)
        {
            var total = 0f;
            for (var y = 0; y < 24; y++)
                for (var x = 0; x < 24; x++)
                    total += Newsprint.Shade(luminance, x, y);
            return total / (24f * 24f);
        }
    }
}
