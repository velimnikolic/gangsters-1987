using System.Collections.Generic;

namespace LivingCity.Personnel
{
    /// <summary>
    /// One man's day, folded into one line: what moved on him, how far it moved by
    /// the time the day was over, and every reason the clerk wrote for it.
    ///
    /// The reasons are carried VERBATIM, joined and never re-worded - the same rule
    /// <see cref="IncidentText"/> and <see cref="CareerText"/> keep. A page that
    /// re-phrased "has been exactly what he is for too long" would be a second author
    /// on the same sentence.
    /// </summary>
    public readonly struct ReasonLine
    {
        /// <summary>The campaign day the movements were written on.</summary>
        public readonly int Day;

        public readonly int CharacterId;
        public readonly string Name;
        public readonly PersonalityTrait Trait;

        /// <summary>The day's NET movement - never zero, because a day that gave a
        /// point and took it back is not news.</summary>
        public readonly int Delta;

        /// <summary>Where the day left him, on the 0-100 scale.</summary>
        public readonly int To;

        /// <summary>Every reason of the day, in the order they were written.</summary>
        public readonly string Reason;

        public ReasonLine(int day, int characterId, string name, PersonalityTrait trait,
            int delta, int to, string reason)
        {
            Day = day;
            CharacterId = characterId;
            Name = name ?? "";
            Trait = trait;
            Delta = delta;
            To = to;
            Reason = reason ?? "";
        }

        public bool Rising => Delta > 0;

        /// <summary>How big the swing was, whichever way it went - what the feed is
        /// ordered by.</summary>
        public int Size => Delta < 0 ? -Delta : Delta;
    }

    /// <summary>
    /// What the crews have to say, out of what the sim already wrote.
    ///
    /// EPIC 15 and 16 moved a man's character every week and printed the reason into
    /// <c>CampaignRunner.CharacterChanges</c>, which nothing read. This is the fold
    /// that turns that list into lines a page can set: one entry per man per trait per
    /// day, biggest swing first.
    ///
    /// Nothing here decides anything or writes on anybody. It reads a day's
    /// <see cref="PersonalityChange"/> records and folds them; hand it the same day
    /// twice and it produces the same lines in the same order, on any machine.
    ///
    /// Pure, integer, and free of UnityEngine.
    /// </summary>
    public static class ReasonFeed
    {
        /// <summary>What stands between two reasons for the same man on the same day.
        /// The book's own separator, the one the marks line already uses.</summary>
        public const string Between = " · ";

        /// <summary>
        /// A day's movements, folded and appended to <paramref name="into"/>.
        ///
        /// Folded by MAN and TRAIT rather than by movement: a midnight that took two
        /// off a man for being parked and gave one back for being paid is one thing
        /// that happened to him, not two, and sixty men drifting weekly would otherwise
        /// be a wall nobody reads to the bottom of.
        ///
        /// A net movement of nothing is dropped entirely - it costs the player nothing
        /// to not be told about a day that left a man exactly where it found him.
        /// </summary>
        public static void Fold(IReadOnlyList<PersonalityChange> changes, int day,
            List<ReasonLine> into)
        {
            if (changes == null || into == null)
                return;

            var first = into.Count;
            for (var i = 0; i < changes.Count; i++)
            {
                var change = changes[i];
                if (change.Delta == 0)
                    continue;

                var found = -1;
                for (var j = first; j < into.Count; j++)
                    if (into[j].CharacterId == change.CharacterId &&
                        into[j].Trait == change.Trait)
                    {
                        found = j;
                        break;
                    }

                if (found < 0)
                {
                    into.Add(new ReasonLine(day, change.CharacterId, change.Name,
                        change.Trait, change.Delta, change.To, change.Reason ?? ""));
                    continue;
                }

                var running = into[found];
                into[found] = new ReasonLine(day, running.CharacterId, running.Name,
                    running.Trait, running.Delta + change.Delta, change.To,
                    Joined(running.Reason, change.Reason));
            }

            // A day that gave a man a point and took it back said nothing about him.
            for (var i = into.Count - 1; i >= first; i--)
                if (into[i].Delta == 0)
                    into.RemoveAt(i);

            // The biggest swings first, so the +1s fall off the bottom of whatever run
            // the page has room for. The tie-breaks are total - id then trait - because
            // List.Sort is not stable and a feed that re-ordered itself between two
            // repaints of the same day would be a page arguing with itself.
            into.Sort(first, into.Count - first, Loudest.Instance);
        }

        /// <summary>
        /// The head of the book as a page reads it: the newest day first, and inside
        /// each day the loudest movement first.
        ///
        /// The two orders pull against each other and a page cannot get them both by
        /// walking the flat list. <see cref="Fold"/> appends each day loudest-first and
        /// the days land oldest-first, so a reader walking the list BACKWARDS to reach
        /// last night reaches it back to front - and a limited run then keeps the day's
        /// smallest movements and drops exactly the swings the feed exists to show. So
        /// the day is found by walking back and then read forwards.
        ///
        /// Days are contiguous because a fold appends a whole day at a time; nothing
        /// here sorts, and a book that was never folded reads out in its own order.
        /// </summary>
        public static void Latest(IReadOnlyList<ReasonLine> book, int limit,
            List<ReasonLine> into)
        {
            if (into == null)
                return;
            into.Clear();
            if (book == null || limit <= 0)
                return;

            var end = book.Count;
            while (end > 0 && into.Count < limit)
            {
                var day = book[end - 1].Day;
                var start = end - 1;
                while (start > 0 && book[start - 1].Day == day)
                    start--;

                for (var i = start; i < end && into.Count < limit; i++)
                    into.Add(book[i]);
                end = start;
            }
        }

        /// <summary>Two reasons on one line, and never the same reason twice: a man
        /// cannot be told off for the same thing twice in one midnight.</summary>
        static string Joined(string running, string next)
        {
            if (string.IsNullOrEmpty(next))
                return running;
            if (string.IsNullOrEmpty(running))
                return next;
            if (running == next ||
                running.StartsWith(next + Between, System.StringComparison.Ordinal) ||
                running.EndsWith(Between + next, System.StringComparison.Ordinal) ||
                running.Contains(Between + next + Between))
                return running;
            return running + Between + next;
        }

        sealed class Loudest : IComparer<ReasonLine>
        {
            public static readonly Loudest Instance = new Loudest();

            public int Compare(ReasonLine a, ReasonLine b)
            {
                var bySize = b.Size.CompareTo(a.Size);
                if (bySize != 0)
                    return bySize;
                var byId = a.CharacterId.CompareTo(b.CharacterId);
                return byId != 0 ? byId : ((int)a.Trait).CompareTo((int)b.Trait);
            }
        }
    }

    /// <summary>
    /// The words the feed's own line is set in, in ONE place - the same discipline
    /// <see cref="IncidentText"/> keeps. The REASON inside it is never touched: it was
    /// written by whoever moved the number and this only says which way it went.
    /// </summary>
    public static class ReasonText
    {
        /// <summary>"loyalty down 3" - what moved and how far.</summary>
        public static string Movement(PersonalityTrait trait, int delta)
        {
            var word = Personality.Label(trait).ToLowerInvariant();
            var size = delta < 0 ? -delta : delta;
            return word + (delta < 0 ? " down " : " up ") + size;
        }

        /// <summary>The whole line, for a reader that wants it as one string -
        /// "Rossi - loyalty down 3: has been exactly what he is for too long".</summary>
        public static string Line(in ReasonLine line) =>
            line.Name + " - " + Movement(line.Trait, line.Delta) +
            (line.Reason.Length > 0 ? ": " + line.Reason : "");

        /// <summary>What the section says on a day nobody moved. A page that went
        /// blank would read as a page that was broken.</summary>
        public const string Quiet = "Nothing moved. The crews have nothing to say today.";
    }
}
