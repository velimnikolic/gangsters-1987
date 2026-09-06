using System;
using System.Collections.Generic;
using LivingCity.Personnel;

namespace LivingCity.Outfit
{
    /// <summary>What the outfit's office made of one order.</summary>
    public enum FilingStatus
    {
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
    /// One order asked of the outfit, and what came of it. The office answers at the
    /// counter: by the time a filing exists the mutation the caller wanted has already
    /// happened or been refused, so this is a RECEIPT the sheet prints - never a request
    /// standing unanswered while the player waits on it.
    /// </summary>
    public sealed class Filing
    {
        internal Filing(int id, string stamp, string text, FilingRuling ruling)
        {
            Id = id;
            Stamp = stamp ?? "";
            Text = text ?? "";
            Status = ruling.Status;
            Ruling = ruling.Ruling;
        }

        public int Id { get; }
        public string Stamp { get; }
        public string Text { get; }
        public FilingStatus Status { get; }
        public string Ruling { get; }
    }

    /// <summary>
    /// The outfit's filing office: the paper trail of every organizational verb the
    /// ledger offers. An order given here is carried out ON THE CLICK and granted or
    /// refused in the same breath - the refusal being the only place capacity is HARD.
    /// The roster's own mechanics stay soft (RosterOps will happily carry an overage a
    /// fight or a promotion created); what this office will not do is FILE an order that
    /// puts a man over his limit.
    ///
    /// Pure: no Unity types, no clock - the office keeps no time of its own because it
    /// never sits on anything.
    /// </summary>
    public sealed class OutfitFilings
    {
        /// <summary>The sheet shows the most recent handful; the rest is archive that
        /// nothing reads, so it is dropped rather than grown without bound.</summary>
        const int HistoryLimit = 40;

        readonly List<Filing> filings = new List<Filing>();
        int nextId;

        /// <summary>Newest first - the order the sheet prints them in.</summary>
        public IReadOnlyList<Filing> All => filings;

        /// <summary>Bumped whenever an order is answered, so a versioned repaint
        /// notices the office without polling every field.</summary>
        public int Version { get; private set; }

        /// <summary>Gives one order. The resolver runs HERE, on the caller's thread and
        /// at the moment of the click - the man is hired, promoted or moved before this
        /// returns - and what comes back is the receipt to print.</summary>
        public Filing File(string stamp, string text, Func<FilingRuling> resolver)
        {
            var ruling = resolver != null
                ? resolver()
                : FilingRuling.Refuse("the order was lost in the office");
            var filing = new Filing(++nextId, stamp, text, ruling);
            filings.Insert(0, filing);
            while (filings.Count > HistoryLimit)
                filings.RemoveAt(filings.Count - 1);
            Version++;
            return filing;
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
