using System.Collections.Generic;

namespace LivingCity.Territory
{
    /// <summary>
    /// Who holds a block, read premise by premise. Ground is taken building by building
    /// (Outfit.Turf says so, and BusinessMarker.GangId is the single source of the deed),
    /// so a block's control is a READING of the deeds standing on it - there is no owner
    /// field, no capture timer and no stored progress to drift out of step with them.
    ///
    /// Pure and allocation-free per call: the DerivedControl tick runs it over the whole
    /// city, so the tally objects are reused and nothing here allocates on a normal pass.
    /// </summary>
    public static class TerritoryControlDerivation
    {
        /// <summary>One block's premises, counted by family.</summary>
        public sealed class Tally
        {
            readonly List<int> gangIds = new List<int>();
            readonly List<int> counts = new List<int>();

            public int Total { get; private set; }
            public IReadOnlyList<int> GangIds => gangIds;
            public IReadOnlyList<int> Counts => counts;

            public void Clear()
            {
                Total = 0;
                gangIds.Clear();
                counts.Clear();
            }

            public void Add(int gangId)
            {
                if (gangId < 0)
                    return;
                Total++;
                var index = gangIds.IndexOf(gangId);
                if (index < 0)
                {
                    gangIds.Add(gangId);
                    counts.Add(1);
                }
                else
                {
                    counts[index]++;
                }
            }

            public int CountOf(int gangId)
            {
                var index = gangIds.IndexOf(gangId);
                return index < 0 ? 0 : counts[index];
            }
        }

        /// <summary>
        /// One family on the block holds it. Two or more and it is contested, whoever is
        /// ahead - a house with a premise on your street is a house pushing on it.
        /// Nobody's deeds, nobody's block.
        /// </summary>
        public static TerritoryControlState Read(Tally tally, out int leadingGangId)
        {
            leadingGangId = -1;
            if (tally == null || tally.Total == 0)
                return TerritoryControlState.Uncontrolled;

            var bestCount = 0;
            var tied = false;
            for (var i = 0; i < tally.GangIds.Count; i++)
            {
                if (tally.Counts[i] > bestCount)
                {
                    bestCount = tally.Counts[i];
                    leadingGangId = tally.GangIds[i];
                    tied = false;
                }
                else if (tally.Counts[i] == bestCount)
                {
                    tied = true;
                }
            }

            if (tied)
                leadingGangId = -1;
            return tally.GangIds.Count == 1
                ? TerritoryControlState.Controlled
                : TerritoryControlState.Contested;
        }

        /// <summary>How much of the block a family's deeds are, as a percentage.</summary>
        public static float ShareOf(Tally tally, int gangId) =>
            tally == null || tally.Total == 0
                ? 0f
                : tally.CountOf(gangId) * 100f / tally.Total;

        /// <summary>
        /// The block's signals as the deeds read them, carrying forward every signal this
        /// derivation does not own - fear and business compliance belong to their own
        /// tickets and must not be wiped by a control pass.
        /// </summary>
        public static TerritoryBlockSignals Signals(
            Tally tally, TerritoryBlockSignals previous, List<TerritoryGangSignals> scratch)
        {
            scratch ??= new List<TerritoryGangSignals>();
            scratch.Clear();
            if (tally != null)
                for (var i = 0; i < tally.GangIds.Count; i++)
                {
                    var share = tally.Counts[i] * 100f / tally.Total;
                    scratch.Add(new TerritoryGangSignals(
                        new TerritoryGangId(tally.GangIds[i]), share, share));
                }

            var control = Read(tally, out var leading);
            previous ??= TerritoryBlockSignals.Empty;
            return new TerritoryBlockSignals(
                previous.LocalFear,
                previous.BusinessCompliance,
                previous.CompliantBusinesses,
                previous.TotalBusinesses,
                control,
                leading >= 0 ? new TerritoryGangId(leading) : default,
                scratch);
        }

        /// <summary>True when a rewrite would say exactly what the block already says -
        /// the guard that keeps a quarter-hour control pass from bumping the state
        /// version of every block in the city on every tick.</summary>
        public static bool Same(TerritoryBlockSignals left, TerritoryBlockSignals right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left == null || right == null)
                return false;
            if (left.Control != right.Control ||
                left.LeadingGangId != right.LeadingGangId ||
                left.Gangs.Count != right.Gangs.Count)
                return false;

            for (var i = 0; i < left.Gangs.Count; i++)
            {
                var a = left.Gangs[i];
                var b = right.Gangs[i];
                if (a.GangId != b.GangId ||
                    !Mathf.Approximately(a.Presence, b.Presence) ||
                    !Mathf.Approximately(a.Influence, b.Influence))
                    return false;
            }
            return true;
        }

        /// <summary>The one float comparison this file needs, so the derivation stays a
        /// plain class the headless suite can run without a UnityEngine reference.</summary>
        static class Mathf
        {
            public static bool Approximately(float a, float b) =>
                System.Math.Abs(a - b) < 0.0001f;
        }
    }
}
