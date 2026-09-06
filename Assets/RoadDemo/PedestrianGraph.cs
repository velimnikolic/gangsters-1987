using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    public class PedNode
    {
        public Vector3 Pos;
        public readonly List<PedLink> Links = new List<PedLink>();
    }

    // One walkable direction between two pedestrian nodes. Gated links are zebra
    // crossings: they may only be entered while the blocking car axis shows red
    // and enough red time remains to finish (or reach the median refuge).
    public class PedLink
    {
        public PedNode From, To;
        public float Length;
        public bool Gated;
        public bool BlocksNorthSouth; // axis of the cars driving over this crossing
        public TrafficSignal Signal;

        // ---------------------------------------------------- what is left to walk on
        // The pavement this stretch runs down carries palms, bins, benches and
        // hedges, and a walker has to fit between them. The builder samples the
        // laid props once (RoadDemoBuilder.BuildWalkClearance) into one bitmask
        // per station along the stretch: bit k set means a walker's shoulders
        // clear everything at that station on lateral slot k.

        public const float SlotStep = 0.25f;
        public const int Slots = 17;      // +- 2 m off the centre line of the walk
        public const float Station = 1.5f;

        /// <summary>Free slots per station, or null where nothing was sampled.</summary>
        public int[] Free;
        internal int LiveClearanceVersion = -1;

        public static float SlotLateral(int k) => (k - (Slots - 1) / 2) * SlotStep;

        // one rank of lateral lines, reused: SampleClearance runs at build only,
        // and on one thread
        static readonly Vector2[] _rank = new Vector2[Slots];

        // one list, reused: sampling runs at build only and on one thread
        static readonly List<SidewalkPlan> _one = new List<SidewalkPlan>(1);

        /// <summary>Read the laid props into this stretch: which lateral slots a
        /// walker of <paramref name="radius"/> can hold at each station. A station
        /// owns the half-interval to either side of it and a slot is free only if
        /// it is free over the WHOLE interval - sampled every quarter metre, so a
        /// lamp post or a parking meter between two stations is seen and not
        /// walked through. Done once at build; the crowd pays nothing for it.</summary>
        public void SampleClearance(SidewalkPlan plan, float radius)
        {
            if (plan == null) { Free = null; return; }
            _one.Clear();
            _one.Add(plan);
            SampleClearance(_one, radius);
        }

        /// <summary>The same, read against SEVERAL plans at once - a slot is free only
        /// if every one of them leaves it free.
        ///
        /// A walk laid across ground that more than one pass has furnished needs this:
        /// the street kit knows where its own lamps and bins are, and knows nothing about
        /// the shop wall, the hedge or the parked car that another pass blocked off. Read
        /// against the kit alone, the crowd walks through all three.</summary>
        public void SampleClearance(List<SidewalkPlan> plans, float radius)
        {
            var span = To.Pos - From.Pos;
            span.y = 0f;
            float len = span.magnitude;
            if (len < 0.01f || plans == null || plans.Count == 0) { Free = null; return; }
            var dir = span / len;
            var right = new Vector3(dir.z, 0f, -dir.x);

            const float Sub = 0.25f;
            int stations = Mathf.CeilToInt(len / Station) + 1;
            var free = new int[stations];
            for (int s = 0; s < stations; s++)
            {
                float from = Mathf.Max(0f, s * Station - Station * 0.5f);
                float to = Mathf.Min(len, s * Station + Station * 0.5f);
                int mask = (1 << Slots) - 1;
                for (float u = from; u <= to + 0.001f; u += Sub)
                {
                    var at = From.Pos + dir * Mathf.Min(u, len);
                    // the whole rank at once. Asked line by line this was seventeen
                    // separate walks of the plan's grid over four metres of ground that
                    // buckets the same either way - and at four samples to the metre over
                    // every stretch in the city it was the single dearest thing in the
                    // load. FreeSlots walks the union of those windows once, which is the
                    // same boxes and so the same answer.
                    for (int k = 0; k < Slots; k++)
                    {
                        var p = at + right * SlotLateral(k);
                        _rank[k] = new Vector2(p.x, p.z);
                    }
                    for (int p = 0; p < plans.Count && mask != 0; p++)
                        if (plans[p] != null) mask = plans[p].FreeSlots(_rank, Slots, radius, mask);
                    if (mask == 0) break;
                }
                free[s] = mask;
            }
            Free = free;
        }

        /// <summary>The nearest line to <paramref name="want"/> a walker can hold
        /// from here to <paramref name="ahead"/> metres on - every station over
        /// that span at once, so he holds the line PAST the bin instead of cutting
        /// back into it the moment its station is behind him. Returns what was
        /// asked for where nothing is known or nothing is free.</summary>
        public float FreeLine(float t, float ahead, float want)
        {
            if (Free == null || Free.Length == 0) return want;
            // each station owns the half-interval either side of it (SampleClearance):
            // the station he is in, to the one that reaches past the look-ahead
            int s0 = Mathf.Clamp(Mathf.FloorToInt((t + Station * 0.5f) / Station), 0, Free.Length - 1);
            int s1 = Mathf.Clamp(Mathf.CeilToInt((t + ahead - Station * 0.5f) / Station), s0, Free.Length - 1);

            int mask = -1;
            for (int s = s0; s <= s1; s++) mask &= Free[s];
            // no single line clears the whole span (a tree at one station, a bin at
            // the next): take the one he is walking into, then the one he is on
            if (mask == 0) mask = Free[s1];
            if (mask == 0) mask = Free[s0];
            if (mask == 0) return want;

            int centre = (Slots - 1) / 2;
            int wanted = Mathf.Clamp(Mathf.RoundToInt(want / SlotStep) + centre, 0, Slots - 1);

            // a slot with its neighbours free as well: room to walk down, not a
            // gap to squeeze through with a shoulder in the hedge
            int roomy = mask & (mask << 1) & (mask >> 1);
            if (roomy != 0)
            {
                if ((roomy & (1 << wanted)) != 0) return want;
                for (int d = 1; d < Slots; d++)
                {
                    int a = wanted - d, b = wanted + d;
                    if (a >= 0 && (roomy & (1 << a)) != 0) return SlotLateral(a);
                    if (b < Slots && (roomy & (1 << b)) != 0) return SlotLateral(b);
                }
            }

            if ((mask & (1 << wanted)) != 0) return want;
            for (int d = 1; d < Slots; d++)
            {
                int a = wanted - d, b = wanted + d;
                if (a >= 0 && (mask & (1 << a)) != 0) return SlotLateral(a);
                if (b < Slots && (mask & (1 << b)) != 0) return SlotLateral(b);
            }
            return want;
        }
    }

}
