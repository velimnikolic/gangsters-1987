using System.Collections.Generic;

namespace LivingCity.News
{
    /// <summary>Composes the immutable morning edition from the public book and the
    /// existing period wire. The edition window is previous 06:00 through this 06:00.</summary>
    public static class Edition
    {
        public const float PressHour = 6f;
        public const int LeadWeight = 60;

        public static Headline[] Compose(int seed, NewsDate date, int day,
            IReadOnlyList<PressRecord> book, IReadOnlyList<string> districts = null,
            int count = HeadlineGenerator.FrontPageSize)
        {
            if (count < 1) count = 1;
            if (count > 12) count = 12;

            var publicRecords = Window(day, book);
            publicRecords.Sort(CompareRecords);

            var regular = new List<PressRecord>();
            var blotter = new List<PressRecord>();
            for (var i = 0; i < publicRecords.Count; i++)
            {
                if (PressText.IsBlotter(publicRecords[i])) blotter.Add(publicRecords[i]);
                else regular.Add(publicRecords[i]);
            }

            // Ask for exactly this page's size. Besides doing less work, this preserves
            // the generator's original random stream (including its photo desk) when
            // the public book is empty.
            var filler = HeadlineGenerator.FrontPage(seed, date, districts, count);
            var page = new List<Headline>(count);

            // A sufficiently heavy city record displaces the wire lead. A historical
            // calendar item remains the first brief; otherwise the ordinary generator
            // keeps its old lead and draw order.
            if (regular.Count > 0 && regular[0].Weight >= LeadWeight)
            {
                page.Add(PressText.Story(regular[0]));
                regular.RemoveAt(0);
            }
            else
            {
                page.Add(filler[0]);
            }

            for (var i = 0; i < regular.Count && page.Count < count; i++)
                page.Add(PressText.Story(regular[i]));

            if (blotter.Count > 0 && page.Count < count)
            {
                page.Add(new Headline
                {
                    Desk = HeadlineDesk.City,
                    Text = "POLICE BLOTTER",
                    Blurb = PressText.Blotter(blotter),
                    Story = blotter[0],
                });
            }

            for (var i = 0; i < filler.Length && page.Count < count; i++)
            {
                // filler[0] was already used when the city did not lead. When it was
                // displaced it is deliberately eligible here: that is how a pinned
                // historical date becomes the first brief.
                if (ReferenceEquals(page[0], filler[i]))
                    continue;
                var duplicate = false;
                for (var p = 0; p < page.Count; p++)
                    if (page[p].Text == filler[i].Text)
                    {
                        duplicate = true;
                        break;
                    }
                if (!duplicate)
                    page.Add(filler[i]);
            }

            return page.ToArray();
        }

        public static bool InWindow(PressRecord record, int editionDay)
        {
            if (record == null || editionDay < 1)
                return false;
            var opened = (record.Day - 1) * 24d + record.Hour;
            var closes = (editionDay - 1) * 24d + PressHour;
            return opened >= closes - 24d && opened < closes;
        }

        public static List<PressRecord> Window(int editionDay,
            IReadOnlyList<PressRecord> book)
        {
            var result = new List<PressRecord>();
            if (book == null)
                return result;
            for (var i = 0; i < book.Count; i++)
                if (InWindow(book[i], editionDay))
                    result.Add(book[i]);
            return result;
        }

        static int CompareRecords(PressRecord a, PressRecord b)
        {
            var order = b.Weight.CompareTo(a.Weight);
            if (order != 0) return order;
            order = a.Day.CompareTo(b.Day);
            if (order != 0) return order;
            order = a.Hour.CompareTo(b.Hour);
            if (order != 0) return order;
            order = a.Kind.CompareTo(b.Kind);
            if (order != 0) return order;
            return a.CaseId.CompareTo(b.CaseId);
        }
    }
}
