using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace LivingCity.UI
{
    /// <summary>Which of the two books the register is showing.</summary>
    public enum WireScope { Both, OurMen, OurDoors }

    /// <summary>The five pens, as a thing that can be switched on and off. The colour
    /// itself is the design's; this is only its index in the filter.</summary>
    public enum WirePen { Red, Blue, Amber, Green, Plain }

    /// <summary>What the register is narrowed to. Everything here is the reader's
    /// choice; nothing in it is state the wire itself owns.</summary>
    public struct WireNarrow
    {
        public WireScope Book;

        /// <summary>A bit per <see cref="WirePen"/>. Nothing set means every pen -
        /// "all" is the absence of a choice, never five choices made at once.</summary>
        public int Pens;

        /// <summary>One origin, or empty for every origin.</summary>
        public string Source;

        /// <summary>A substring over tag, body, origin and stamp.</summary>
        public string Query;

        /// <summary>One day, or -1. Set by double-clicking the day rail, which is the
        /// only day control there is.</summary>
        public int DayOnly;

        public static WireNarrow Open =>
            new WireNarrow { Book = WireScope.Both, Pens = 0, Source = "", Query = "", DayOnly = -1 };

        public bool Narrowed =>
            Book != WireScope.Both || Pens != 0 ||
            !string.IsNullOrEmpty(Source) || !string.IsNullOrEmpty(Query) || DayOnly >= 0;

        // The narrowing rules live with the model rather than with the strip that draws
        // them: which pen switches off, what "all pens" means, and that a day already
        // isolated is released by asking for it again.

        public WireNarrow WithBook(WireScope book)
        {
            var next = this;
            next.Book = book;
            return next;
        }

        public WireNarrow WithPen(WirePen pen)
        {
            var next = this;
            next.Pens ^= 1 << (int)pen;
            return next;
        }

        public WireNarrow WithEveryPen()
        {
            var next = this;
            next.Pens = 0;
            return next;
        }

        public WireNarrow WithSource(string source)
        {
            var next = this;
            next.Source = source ?? "";
            return next;
        }

        public WireNarrow WithQuery(string query)
        {
            var next = this;
            next.Query = query ?? "";
            return next;
        }

        public WireNarrow WithDay(int day)
        {
            var next = this;
            next.DayOnly = next.DayOnly == day ? -1 : day;
            return next;
        }
    }

    /// <summary>The three things the register prints, in the order it prints them.</summary>
    public enum WireItemKind { Day, Run, Line }

    /// <summary>One thing standing at one y in the register's single scroll.</summary>
    public readonly struct WireItem
    {
        public readonly WireItemKind Kind;
        public readonly float Y, H;

        /// <summary>Index into <see cref="WireRegister.Kept"/> on a Line; the day on a
        /// Day band; unused on a run divider.</summary>
        public readonly int Index;
        public readonly int Day;

        /// <summary>A run divider's label, and the origin a line prints - empty when
        /// the line before it came from the same place, which is how a register is
        /// kept: the account name is written only when it changes.</summary>
        public readonly string Label;
        public readonly bool Banded;

        public WireItem(WireItemKind kind, float y, float h, int index, int day,
            string label, bool banded)
        {
            Kind = kind;
            Y = y;
            H = h;
            Index = index;
            Day = day;
            Label = label;
            Banded = banded;
        }
    }

    /// <summary>One campaign day as the register and the day rail read it.</summary>
    public sealed class WireDay
    {
        public int Day;

        /// <summary>Slips in scope on that day, and where its band stands in the
        /// scroll. -1 when the day carries nothing in scope.</summary>
        public int Count;
        public float Top = -1f;

        public int Men, Doors, Heat, Taken;
        public readonly int[] Pens = new int[5];

        /// <summary>The day's lines, by book, as indices into the kept run. Bucketed
        /// while the archive is narrowed so the layout never walks two thousand slips
        /// again for every day it prints.</summary>
        public readonly List<int> MenLines = new List<int>();
        public readonly List<int> DoorLines = new List<int>();

        /// <summary>The day's own arithmetic, in words. Nothing here is composed about
        /// an EVENT - it is the count of the lines under the band and the sum of the
        /// figures they carry, and it is written once, here.</summary>
        public string Counts = "";

        /// <summary>Lines written in the two pens that mean blood or hands laid on -
        /// the share the rail draws in red so a bad night is visible before it is
        /// read.</summary>
        public int Hard;
    }

    /// <summary>
    /// The register's arithmetic, with no Unity in it.
    ///
    /// It takes the run <see cref="WireBook"/> collected, narrows it to what the reader
    /// asked for, and lays the result out as a flat list of bands, run dividers and
    /// lines with a y on each - one continuous scroll, no pages. The sheet then draws
    /// only the window around its scroll offset, which is what makes two thousand slips
    /// a list a reader can walk end to end.
    ///
    /// Nothing here composes a sentence about an event. Every body, tag and stamp was
    /// written the day it happened.
    /// </summary>
    public sealed class WireRegister
    {
        public const float LineH = 26f;
        public const float DayH = 34f;
        public const float RunH = 22f;

        readonly List<WireLine> all = new List<WireLine>();
        readonly List<string> hay = new List<string>();
        readonly List<WireLine> kept = new List<WireLine>();
        readonly List<WireItem> items = new List<WireItem>();
        readonly List<WireDay> days = new List<WireDay>();
        readonly List<string> sources = new List<string>();
        readonly Dictionary<int, WireDay> byDay = new Dictionary<int, WireDay>();

        public IReadOnlyList<WireLine> Kept => kept;
        public IReadOnlyList<WireItem> Items => items;

        /// <summary>Every day from the newest filed down to day one, newest first -
        /// the rail draws a tick for a quiet day too.</summary>
        public IReadOnlyList<WireDay> Days => days;

        /// <summary>The origins present in the whole archive, for the source list.</summary>
        public IReadOnlyList<string> Sources => sources;

        public float Height { get; private set; }
        public int Total => all.Count;
        public int Count => kept.Count;
        public int Men { get; private set; }
        public int Doors { get; private set; }
        public int DaysOnFile { get; private set; }
        public int Busiest { get; private set; }
        public readonly int[] PenTally = new int[5];

        /// <summary>The pen a line is written in, as an index. The colours are
        /// <see cref="LedgerStyle"/>'s own; nothing here invents one.</summary>
        public static WirePen PenOf(Color ink)
        {
            if (ink == LedgerStyle.RedPen)
                return WirePen.Red;
            if (ink == LedgerStyle.Ballpoint)
                return WirePen.Blue;
            if (ink == LedgerStyle.PenAmber)
                return WirePen.Amber;
            if (ink == LedgerStyle.GreenOk)
                return WirePen.Green;
            return WirePen.Plain;
        }

        public static Color InkOf(WirePen pen)
        {
            switch (pen)
            {
                case WirePen.Red: return LedgerStyle.RedPen;
                case WirePen.Blue: return LedgerStyle.Ballpoint;
                case WirePen.Amber: return LedgerStyle.PenAmber;
                case WirePen.Green: return LedgerStyle.GreenOk;
                default: return LedgerStyle.TelexPlain;
            }
        }

        public static string PenMeaning(WirePen pen)
        {
            switch (pen)
            {
                case WirePen.Red: return "Blood and loss";
                case WirePen.Blue: return "Hands laid on";
                case WirePen.Amber: return "Money asked for";
                case WirePen.Green: return "Good news";
                default: return "Everything else";
            }
        }

        /// <summary>
        /// Take the archive as the books hold it. The haystack every search reads is
        /// built ONCE here rather than at every keystroke: two thousand lowercased
        /// strings per typed letter is a stutter the reader can feel.
        /// </summary>
        public void Take(List<WireLine> lines)
        {
            all.Clear();
            hay.Clear();
            sources.Clear();
            all.AddRange(lines);
            for (var i = 0; i < all.Count; i++)
            {
                var line = all[i];
                hay.Add((line.Tag + " " + line.Body + " " + line.Origin + " " +
                    line.Stamp).ToLowerInvariant());
                var origin = line.Origin;
                if (origin.Length > 0 && !sources.Contains(origin))
                    sources.Add(origin);
            }
            sources.Sort(System.StringComparer.Ordinal);
        }

        /// <summary>Narrow the archive and lay it out. Newest day first, and inside a
        /// day each book keeps its own order under its own divider - they are counted
        /// on different clocks and nothing here invents an order across them.</summary>
        public void Build(WireNarrow narrow)
        {
            kept.Clear();
            items.Clear();
            days.Clear();
            byDay.Clear();
            for (var i = 0; i < PenTally.Length; i++)
                PenTally[i] = 0;
            Men = Doors = DaysOnFile = 0;
            Busiest = 0;
            Height = 0f;

            var query = string.IsNullOrEmpty(narrow.Query)
                ? "" : narrow.Query.Trim().ToLowerInvariant();
            var newest = 0;
            for (var i = 0; i < all.Count; i++)
            {
                var line = all[i];
                if (line.Day > newest)
                    newest = line.Day;
                if (!Wanted(line, hay[i], narrow, query))
                    continue;
                kept.Add(line);
                var pen = (int)PenOf(line.Ink);
                PenTally[pen]++;
                if (line.FromDoor)
                    Doors++;
                else
                    Men++;
                var day = DayOf(line.Day);
                (line.FromDoor ? day.DoorLines : day.MenLines).Add(kept.Count - 1);
                day.Count++;
                day.Pens[pen]++;
                if (line.FromDoor)
                    day.Doors++;
                else
                    day.Men++;
                day.Heat += line.Heat;
                if (line.MoneyIn)
                    day.Taken += line.Money;
                if (pen == (int)WirePen.Red || pen == (int)WirePen.Blue)
                    day.Hard++;
            }

            for (var day = newest; day >= 1; day--)
                days.Add(byDay.TryGetValue(day, out var counted) ? counted : Blank(day));

            var y = 0f;
            var banded = false;
            for (var d = 0; d < days.Count; d++)
            {
                var day = days[d];
                if (day.Count == 0)
                    continue;
                DaysOnFile++;
                if (day.Count > Busiest)
                    Busiest = day.Count;
                day.Counts = Words(day);
                day.Top = y;
                items.Add(new WireItem(WireItemKind.Day, y, DayH, day.Count, day.Day,
                    "", false));
                y += DayH;

                var both = day.Men > 0 && day.Doors > 0;
                for (var pass = 0; pass < 2; pass++)
                {
                    var run = pass == 1 ? day.DoorLines : day.MenLines;
                    if (run.Count == 0)
                        continue;
                    if (both)
                    {
                        items.Add(new WireItem(WireItemKind.Run, y, RunH, -1, day.Day,
                            pass == 1 ? "AT OUR DOORS - THEIR OWN CLOCK"
                                      : "ON OUR MEN - THEIR OWN CLOCK", false));
                        y += RunH;
                    }

                    // The origin is printed only when it CHANGES, the way an account
                    // name is written in a register, and the run divider and the day
                    // band both start the practice over.
                    var lastSource = "";
                    for (var i = 0; i < run.Count; i++)
                    {
                        var index = run[i];
                        var origin = kept[index].Origin;
                        var shown = origin != lastSource ? origin : "";
                        lastSource = origin;
                        items.Add(new WireItem(WireItemKind.Line, y, LineH, index,
                            day.Day, shown, banded));
                        banded = !banded;
                        y += LineH;
                    }
                }
            }
            Height = y;
        }

        static bool Wanted(WireLine line, string straw, WireNarrow narrow, string query)
        {
            if (narrow.Book == WireScope.OurMen && line.FromDoor)
                return false;
            if (narrow.Book == WireScope.OurDoors && !line.FromDoor)
                return false;
            if (narrow.Pens != 0 && (narrow.Pens & (1 << (int)PenOf(line.Ink))) == 0)
                return false;
            if (!string.IsNullOrEmpty(narrow.Source) && line.Origin != narrow.Source)
                return false;
            if (narrow.DayOnly >= 0 && line.Day != narrow.DayOnly)
                return false;
            return query.Length == 0 || straw.Contains(query);
        }

        WireDay DayOf(int day)
        {
            if (byDay.TryGetValue(day, out var found))
                return found;
            found = new WireDay { Day = day };
            byDay[day] = found;
            return found;
        }

        static WireDay Blank(int day) => new WireDay { Day = day };

        static string Words(WireDay day)
        {
            var words = day.Count + (day.Count == 1 ? " ENTRY" : " ENTRIES");
            words += " · " + day.Men + " ON OUR MEN · " + day.Doors + " AT OUR DOORS";
            if (day.Heat > 0)
                words += " · " + day.Heat + " HEAT DRAWN";
            if (day.Taken > 0)
                words += " · $" + day.Taken.ToString("N0", CultureInfo.InvariantCulture) +
                    " TAKEN AT THE DOORS";
            return words;
        }

        /// <summary>
        /// What is in scope, counted: the two books, the five pens and the days on file.
        /// The page prints these against dotted leaders when no slip is drawn; every
        /// figure is a count of the lines in front of the reader and none of them is
        /// composed anywhere else.
        /// </summary>
        public void Tally(List<string> labels, List<string> figures, List<int> pens)
        {
            labels.Clear();
            figures.Clear();
            pens.Clear();
            void Row(string label, int figure, int pen)
            {
                labels.Add(label);
                figures.Add(figure.ToString("N0", CultureInfo.InvariantCulture));
                pens.Add(pen);
            }
            Row("ON OUR MEN", Men, -1);
            Row("AT OUR DOORS", Doors, -1);
            for (var i = 0; i < PenTally.Length; i++)
                Row(PenMeaning((WirePen)i).ToUpperInvariant(), PenTally[i], i);
            Row("DAYS ON FILE", DaysOnFile, -1);
        }

        /// <summary>Where a day's band stands, or -1 when that day is not in scope.</summary>
        public float TopOf(int day) =>
            byDay.TryGetValue(day, out var found) ? found.Top : -1f;

        /// <summary>The day the reader is standing in: the last band at or above the
        /// scroll offset.</summary>
        public int DayAt(float scroll)
        {
            var current = -1;
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item.Kind != WireItemKind.Day)
                    continue;
                if (item.Y <= scroll + 8f)
                    current = item.Day;
                else
                    break;
            }
            if (current >= 0)
                return current;
            for (var i = 0; i < items.Count; i++)
                if (items[i].Kind == WireItemKind.Day)
                    return items[i].Day;
            return -1;
        }

        /// <summary>The next day with anything in scope, walking newer (-1) or older
        /// (+1) from the day the reader is standing in.</summary>
        public int Step(int from, int direction)
        {
            var seen = false;
            var last = -1;
            for (var i = 0; i < days.Count; i++)
            {
                var day = days[i];
                if (day.Count == 0)
                    continue;
                if (direction < 0)
                {
                    if (day.Day == from)
                        return last >= 0 ? last : from;
                    last = day.Day;
                }
                else
                {
                    if (seen)
                        return day.Day;
                    if (day.Day == from)
                        seen = true;
                }
            }
            return from;
        }

        /// <summary>
        /// Where a slip stands in a run, matched on what it SAYS.
        ///
        /// How many arrived above the reader is this figure's movement and nothing else.
        /// Counting the run in front of the old head instead misses a door slip filed
        /// under a day an incident already leads: the two books tie on the day, and the
        /// tie keeps the incident first.
        /// </summary>
        public static int FiledAt(IReadOnlyList<WireLine> run, WireLine line)
        {
            for (var i = 0; run != null && i < run.Count; i++)
            {
                var row = run[i];
                if (row.Day == line.Day && row.Stamp == line.Stamp &&
                    row.Tag == line.Tag && row.Body == line.Body)
                    return i;
            }
            return -1;
        }

        /// <summary>The index of a line in the run, matched on what the slip IS rather
        /// than on a reference - the books are rebuilt under this page.</summary>
        public int IndexOf(WireLine line)
        {
            for (var i = 0; i < kept.Count; i++)
            {
                var row = kept[i];
                if (row.Day == line.Day && row.Stamp == line.Stamp &&
                    row.Tag == line.Tag && row.Body == line.Body)
                    return i;
            }
            return -1;
        }

        /// <summary>The item index of the line at <paramref name="index"/>, for a
        /// caller walking the register with the arrow keys.</summary>
        public int ItemOfLine(int index)
        {
            for (var i = 0; i < items.Count; i++)
                if (items[i].Kind == WireItemKind.Line && items[i].Index == index)
                    return i;
            return -1;
        }
    }
}
