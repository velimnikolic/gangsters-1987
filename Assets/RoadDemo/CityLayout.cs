using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// One quarter hanging off the grid: which kind, which shore, which of the city's
    /// road lines run out to it, and how wide the band of wild ground between.
    /// Rolled by <see cref="CityLayout"/> from the city seed, then written back onto
    /// the builder so the plan can be read - and frozen, or hand-edited - in the
    /// inspector, exactly the way Respace writes the road lines back.
    /// </summary>
    [System.Serializable]
    public class DistrictSlot
    {
        public DistrictKind kind = DistrictKind.Suburb;
        [Tooltip("Which shore of the city it hangs off.")]
        public CityEdge edge = CityEdge.South;
        [Tooltip("Which of the city's road lines run out to it: indices into verticalRoadX " +
                 "for a district north or south, into horizontalRoadZ for one east or west. " +
                 "Every gap of the grid is 5k + 3 metres wide, so two lines carry the same 5 m " +
                 "lattice only when their indices are five apart - which is why a suburb's pins " +
                 "come in steps of five.")]
        public int[] pinLines = new int[0];
        [Tooltip("Metres of wild ground between the city's outer kerb and the district's edge.")]
        public float strip = 45f;
        [Tooltip("The district's own seed: its houses, its blocks, its ships.")]
        public int seed = 1987;
        [Tooltip("How many blocks across and deep a suburb runs, how many berths a port has. " +
                 "Zero leaves the district's own default.")]
        public int sizeAcross;
        public int sizeDeep;

        public override string ToString()
            => $"{kind} on the {edge}, lines [{string.Join(",", pinLines)}], strip {strip:F0} m, seed {seed}";
    }

    /// <summary>
    /// Where the quarters go. The city is not the same twice: the port takes one
    /// shore and the suburbs others, out of the city seed, and every roll is written
    /// into the builder's districts array so it can be read, frozen or edited.
    ///
    /// The rules a roll may not break:
    ///   - a road line that carries a district is an ORDINARY street, never a
    ///     boulevard (a four-lane road cannot narrow into a two-lane connector) and
    ///     never the outermost line of the grid (that is the corner of the island);
    ///   - a suburb's lines are five apart, because every gap of the grid measures
    ///     5k + 3 metres and only five of them come back onto the suburb's own 5 m
    ///     lattice;
    ///   - nothing sits where a river leaves the grid;
    ///   - one port to a city;
    ///   - two districts on one shore stand at least SideGap metres apart.
    /// </summary>
    public static class CityLayout
    {
        /// <summary>What the roll needs to know about the grid it hangs things off.</summary>
        public struct Grid
        {
            public float[] Vx;          // vertical road axes, west to east
            public bool[] VBoulevard;
            public float[] Hz;          // horizontal road axes, south to north
            public bool[] HBoulevard;
            /// <summary>Whether a river or another seam leaves the grid at this line, so
            /// nothing may be pinned to it (true = keep clear).</summary>
            public System.Func<bool, int, bool> Blocked;

            public float[] Axis(bool vertical) => vertical ? Vx : Hz;
            public bool[] Boulevard(bool vertical) => vertical ? VBoulevard : HBoulevard;
        }

        /// <summary>Metres two districts on the same shore keep between them.</summary>
        const float SideGap = 170f;

        /// <summary>How many blocks of the grid a port's two gates may stand apart.</summary>
        static readonly int[] HarborGateSpans = { 2, 3 };

        public static List<DistrictSlot> Roll(Grid grid, int seed, int suburbsMin, int suburbsMax, bool wantHarbor)
        {
            var rng = new System.Random(seed * 7919 + 13);
            var slots = new List<DistrictSlot>();
            // what each shore has already given away, as spans of the axis it runs along
            var taken = new Dictionary<CityEdge, List<Vector2>>();
            foreach (CityEdge e in System.Enum.GetValues(typeof(CityEdge))) taken[e] = new List<Vector2>();

            var edges = new List<CityEdge> { CityEdge.South, CityEdge.West, CityEdge.North, CityEdge.East };
            Shuffle(edges, rng);

            if (wantHarbor)
            {
                foreach (var edge in edges)
                {
                    var slot = TryHarbor(grid, edge, rng, taken[edge]);
                    if (slot == null) continue;
                    slots.Add(slot);
                    taken[edge].Add(SpanOf(grid, slot));
                    break;
                }
            }

            int suburbs = suburbsMin + rng.Next(Mathf.Max(1, suburbsMax - suburbsMin + 1));
            for (int k = 0; k < suburbs; k++)
            {
                Shuffle(edges, rng);
                DistrictSlot made = null;
                foreach (var edge in edges)
                {
                    made = TrySuburb(grid, edge, rng, taken[edge], seed, k);
                    if (made != null) { slots.Add(made); taken[edge].Add(SpanOf(grid, made)); break; }
                }
                if (made == null) break;   // the island has no shore left
            }
            return slots;
        }

        // ------------------------------------------------------------- the port

        static DistrictSlot TryHarbor(Grid grid, CityEdge edge, System.Random rng, List<Vector2> taken)
        {
            bool vertical = edge == CityEdge.South || edge == CityEdge.North;
            var lines = Ordinary(grid, vertical);
            Shuffle(lines, rng);
            foreach (int a in lines)
                foreach (int span in HarborGateSpans)
            {
                int b = a + span;
                if (!IsOrdinary(grid, vertical, b)) continue;
                var slot = new DistrictSlot
                {
                    kind = DistrictKind.Harbor,
                    edge = edge,
                    pinLines = new[] { a, b },
                    // a real approach road, not an alley: the port stands well off the
                    // grid and the island's wild ground runs between
                    strip = 130f + rng.Next(5) * 20f,
                    seed = 1987 + rng.Next(400),
                    sizeAcross = 2 + rng.Next(3),      // berths
                };
                if (Clashes(grid, slot, taken)) continue;
                return slot;
            }
            return null;
        }

        // ----------------------------------------------------------- the suburbs

        static DistrictSlot TrySuburb(Grid grid, CityEdge edge, System.Random rng, List<Vector2> taken,
                                      int citySeed, int index)
        {
            bool vertical = edge == CityEdge.South || edge == CityEdge.North;
            var lines = Ordinary(grid, vertical);
            Shuffle(lines, rng);
            // two ways in are better than one, so the lines that carry a second (and a
            // third) pin are tried first; a shore that has none still gets a suburb, on
            // one road in and out
            for (int pass = 0; pass < 2; pass++)
                foreach (int a in lines)
                {
                    var pins = new List<int> { a };
                    if (IsOrdinary(grid, vertical, a + 5))
                    {
                        pins.Add(a + 5);
                        if (rng.NextDouble() < 0.45 && IsOrdinary(grid, vertical, a + 10)) pins.Add(a + 10);
                    }
                    if (pass == 0 && pins.Count < 2) continue;

                    var slot = new DistrictSlot
                    {
                        kind = DistrictKind.Suburb,
                        edge = edge,
                        pinLines = pins.ToArray(),
                        // a drive out of town: hills and woods between the city and the
                        // suburb, so the quarter reads as its own place, not an annex
                        strip = 110f + rng.Next(7) * 20f,
                        seed = citySeed * 977 + index * 131 + 7,
                        sizeDeep = 2 + rng.Next(3),        // rows of blocks
                    };
                    if (Clashes(grid, slot, taken)) continue;
                    return slot;
                }
            return null;
        }

        // ---------------------------------------------------------------- rules

        static List<int> Ordinary(Grid grid, bool vertical)
        {
            var axis = grid.Axis(vertical);
            var list = new List<int>();
            for (int i = 1; i + 1 < axis.Length; i++)      // never the corner lines
                if (IsOrdinary(grid, vertical, i)) list.Add(i);
            return list;
        }

        static bool IsOrdinary(Grid grid, bool vertical, int line)
        {
            var axis = grid.Axis(vertical);
            if (line <= 0 || line + 1 >= axis.Length) return false;
            if (grid.Boulevard(vertical)[line]) return false;
            if (grid.Blocked != null && grid.Blocked(vertical, line)) return false;
            return true;
        }

        /// <summary>The stretch of shore a slot needs, along the shore's own axis.</summary>
        static Vector2 SpanOf(Grid grid, DistrictSlot slot)
        {
            bool vertical = slot.edge == CityEdge.South || slot.edge == CityEdge.North;
            var axis = grid.Axis(vertical);
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (int line in slot.pinLines)
            {
                if (line < 0 || line >= axis.Length) continue;
                lo = Mathf.Min(lo, axis[line]);
                hi = Mathf.Max(hi, axis[line]);
            }
            if (lo > hi) return Vector2.zero;
            // a suburb runs a block past its outer pins; a port a berth past its gates
            float flank = slot.kind == DistrictKind.Harbor ? 120f : 60f;
            return new Vector2(lo - flank, hi + flank);
        }

        static bool Clashes(Grid grid, DistrictSlot slot, List<Vector2> taken)
        {
            var span = SpanOf(grid, slot);
            foreach (var t in taken)
                if (span.x < t.y + SideGap && t.x < span.y + SideGap) return true;
            return false;
        }

        static void Shuffle<T>(List<T> list, System.Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
