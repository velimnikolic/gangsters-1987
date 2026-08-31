using System;
using System.Collections.Generic;
using LivingCity.Personnel;

namespace LivingCity.Outfit
{
    /// <summary>Where one filed order stands with the outfit's office.</summary>
    public enum FilingStatus
    {
        Filed,
        Granted,
        Refused,
    }

    /// <summary>The office's answer to one filing: what it decided, and the line it
    /// writes in the margin explaining the decision.</summary>
    public readonly struct FilingRuling
    {
        FilingRuling(FilingStatus status, string ruling)
        {
            Status = status;
            Ruling = ruling ?? "";
        }

        public FilingStatus Status { get; }
        public string Ruling { get; }

        public static FilingRuling Grant(string ruling) =>
            new FilingRuling(FilingStatus.Granted, ruling);

        public static FilingRuling Refuse(string reason) =>
            new FilingRuling(FilingStatus.Refused, reason);
    }

    /// <summary>
    /// One order asked of the outfit. The order is a REQUEST while it stands at Filed:
    /// nothing in the roster or on the territory has moved yet. The mutation the caller
    /// wants happens inside the resolver, at ruling time, and never at the click.
    /// </summary>
    public sealed class Filing
    {
        internal Filing(int id, string stamp, string text, Func<FilingRuling> resolver)
        {
            Id = id;
            Stamp = stamp ?? "";
            Text = text ?? "";
            Resolver = resolver;
            Status = FilingStatus.Filed;
            Ruling = "awaiting the outfit's ruling";
        }

        public int Id { get; }
        public string Stamp { get; }
        public string Text { get; }
        public FilingStatus Status { get; private set; }
        public string Ruling { get; private set; }
        public bool Awaiting => Status == FilingStatus.Filed;

        internal float SecondsLeft;
        internal Func<FilingRuling> Resolver;

        internal void Settle(FilingRuling ruling)
        {
            Status = ruling.Status;
            Ruling = ruling.Ruling;
            Resolver = null;
        }
    }

    /// <summary>
    /// The outfit's filing office: the paper trail between asking for something and it
    /// being so. Every organizational verb the ledger offers is filed here, stands for
    /// a moment as an unanswered request, and is then granted or refused - the refusal
    /// being the only place capacity is HARD. The roster's own mechanics stay soft
    /// (RosterOps will happily carry an overage a fight or a promotion created); what
    /// this office will not do is FILE a new order that puts a man over his limit.
    ///
    /// Pure: no Unity types, no clock of its own. Whoever owns it ticks it in real
    /// seconds, which is what the ruling delay is measured in.
    /// </summary>
    public sealed class OutfitFilings
    {
        /// <summary>How long the office sits on an order before it answers.</summary>
        public const float DefaultRulingSeconds = 1.4f;

        /// <summary>The sheet shows the most recent handful; the rest is archive that
        /// nothing reads, so it is dropped rather than grown without bound.</summary>
        const int HistoryLimit = 40;

        readonly List<Filing> filings = new List<Filing>();
        int nextId;

        public float RulingSeconds { get; set; } = DefaultRulingSeconds;

        /// <summary>Newest first - the order the sheet prints them in.</summary>
        public IReadOnlyList<Filing> All => filings;

        /// <summary>Bumped whenever a filing is added or answered, so a versioned
        /// repaint notices the office without polling every field.</summary>
        public int Version { get; private set; }

        public int AwaitingCount
        {
            get
            {
                var count = 0;
                for (var i = 0; i < filings.Count; i++)
                    if (filings[i].Awaiting)
                        count++;
                return count;
            }
        }

        /// <summary>Files one order. The resolver runs when the office answers, on the
        /// thread that ticks - it is where the state change belongs, not here.</summary>
        public Filing File(string stamp, string text, Func<FilingRuling> resolver)
        {
            var filing = new Filing(++nextId, stamp, text, resolver)
            {
                SecondsLeft = Math.Max(0f, RulingSeconds),
            };
            filings.Insert(0, filing);
            while (filings.Count > HistoryLimit)
                filings.RemoveAt(filings.Count - 1);
            Version++;
            return filing;
        }

        /// <summary>Answers everything whose delay has run out. Returns true when any
        /// filing changed, so the owner can bump its own version once.</summary>
        public bool Tick(float seconds)
        {
            if (seconds <= 0f)
                return false;

            var changed = false;
            // Oldest first: two orders filed in the same frame are answered in the
            // order they were asked, which is the only ordering a paper office has.
            for (var i = filings.Count - 1; i >= 0; i--)
            {
                var filing = filings[i];
                if (!filing.Awaiting)
                    continue;
                filing.SecondsLeft -= seconds;
                if (filing.SecondsLeft > 0f)
                    continue;

                var resolver = filing.Resolver;
                filing.Settle(resolver != null
                    ? resolver()
                    : FilingRuling.Refuse("the order was lost in the office"));
                changed = true;
            }

            if (changed)
                Version++;
            return changed;
        }

        public void Clear()
        {
            if (filings.Count == 0)
                return;
            filings.Clear();
            Version++;
        }
    }

    /// <summary>
    /// What the filing office will and will not put on paper. Separate from RosterOps
    /// on purpose: the roster's capacity is SOFT (a lieutenant can end up over his
    /// limit and stay there), and this is the one place that is hard - the office
    /// refuses to file the order that would create the overage in the first place.
    /// </summary>
    public static class OutfitFilingRules
    {
        public static bool AcceptsAnotherMan(in CapacityMeasure manpower) =>
            manpower.Current < manpower.Maximum;

        public static bool AcceptsAnotherBlock(in CapacityMeasure blocks) =>
            blocks.Current < blocks.Maximum;

        public static string ManRefusal(string leaderName, in CapacityMeasure manpower) =>
            leaderName + " cannot hold another man (" +
            manpower.Current + "/" + manpower.Maximum + ")";

        public static string BlockRefusal(string leaderName, in CapacityMeasure blocks) =>
            leaderName + " already answers for " +
            blocks.Current + "/" + blocks.Maximum + " blocks";
    }
}
