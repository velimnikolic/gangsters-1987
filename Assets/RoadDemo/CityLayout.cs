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
        [Tooltip("What the place is called. Every quarter is a place of its own - the map " +
                 "prints this across it - so no two of them share a name.")]
        public string name = "";
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
        [Tooltip("Push the quarter out until its far edge stands on the island's own coast, " +
                 "whatever the strip says: a port has to open onto the sea, not onto a pond " +
                 "cut out of the middle of the wilderness.")]
        public bool toCoast;
        [Tooltip("The district's own seed: its houses, its blocks, its ships.")]
        public int seed = 1987;
        [Tooltip("How many blocks across and deep a suburb runs, how many berths a port has. " +
                 "Zero leaves the district's own default.")]
        public int sizeAcross;
        public int sizeDeep;

        public override string ToString()
            => $"{kind} \"{name}\" on the {edge}, lines [{string.Join(",", pinLines)}], strip {strip:F0} m, seed {seed}";
    }

    /// <summary>
    /// Where the quarters go. The city is not the same twice: the port takes one shore
    /// and the suburbs the rest of the island, out of the city seed, and every roll is
    /// written into the builder's districts array so it can be read, frozen or edited.
    ///
    /// The island is not a rim one quarter deep. A town of 1987 is mostly its suburbs,
    /// so they are rolled in RINGS: a first row of villages just past the last city
    /// street, a second out beyond their woods, a third where the shore is wide enough -
    /// each one its own place with its own name, each hung off a city street by an
    /// approach road no other quarter may stand across.
    ///
    /// The rules a roll may not break:
    ///   - the line that carries a PORT or a FIELD is an ordinary street, never a
    ///     boulevard and never one a seam runs out along; a village will take any line
    ///     but the outermost of the grid (that is the corner of the island), because a
    ///     shore of this grid has three ordinary lines on it and a town wants ten
    ///     suburbs (IsOrdinary);
    ///   - nothing sits where a river leaves the grid - neither pinned to its line nor
    ///     laid across the channel it runs down to the sea;
    ///   - one port to a city, and one airport, which takes a whole shore;
    ///   - no quarter stands on another, nor across another's approach road;
    ///   - and the port and the field stand at the island's END: the port with its basin
    ///     open to the sea, the field with its far threshold on the coast.
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

            /// <summary>Where a river leaves the grid across a shore, as spans of that
            /// shore's own axis. Keeping a quarter off the PIN LINE was never enough:
            /// a suburb eight blocks wide is seven hundred metres of shore and its pins
            /// sit somewhere in the middle of that, so it was being laid straight across
            /// the channel the river runs down to the sea - houses in the water, its
            /// streets crossing the river with no bridge under them, and the ground
            /// under the quarter held flat, which dammed the river besides.</summary>
            public System.Func<bool, List<Vector2>> Rivers;

            /// <summary>Metres of wild ground from the grid's outer kerb to the mean
            /// waterline on that shore: how much island there is to put things on.</summary>
            public System.Func<CityEdge, float> Shore;

            /// <summary>The town's own name, for the quarters named after it - the field,
            /// the docks. Null falls back to the pool's own names.</summary>
            public string CityName;

            public float[] Axis(bool vertical) => vertical ? Vx : Hz;
            public bool[] Boulevard(bool vertical) => vertical ? VBoulevard : HBoulevard;
            public float ShoreOf(CityEdge edge) => Shore != null ? Shore(edge) : 900f;
        }

        // --------------------------------------------------------------- measures

        /// <summary>How much shore an airport takes either side of its approach road:
        /// the field's own grass, runway end to runway end (AirportSpec.MapX0/X1).</summary>
        public const float AirportFlank = 1050f;
        /// <summary>The field from its approach road to the far side of the runway.</summary>
        public const float AirportDepth = 810f;
        /// <summary>The port's own ground, from the street behind it to the quay wall.</summary>
        public const float HarborDepth = 145f;
        /// <summary>Water off the quay a freighter needs to come alongside.</summary>
        public const float HarborBasin = 200f;
        /// <summary>How much shore a port takes either side of its gates.</summary>
        const float HarborFlank = 130f;
        /// <summary>Metres of shore a suburb counts as one column of blocks - the same
        /// figure SuburbDistrict lays its own lattice on - and one row deep.</summary>
        const float SuburbBlock = 90f, SuburbRow = 70f;

        /// <summary>How far short of the mean waterline a quarter at the island's end
        /// stops. The coast wanders either side of that line and the island reaches out
        /// past whatever ground a district holds, so this is a look and not a clearance.</summary>
        public const float CoastMargin = 70f;

        /// <summary>Metres two quarters keep between them - woods, and room for the
        /// island's own ground to come back up between two flat places - and how near a
        /// quarter another one's approach road may run. These are the PORT's and the
        /// FIELD's clearances; a village's come off the squeeze it was placed at.</summary>
        const float Gap = 45f, RoadGap = 12f;

        /// <summary>How hard the island is being packed. A town is meant to have ten
        /// villages round it and must not come out with two, but the first pass is laid
        /// GENEROUSLY - wide places, woods between them, and everybody's approach road
        /// well clear of everybody's fence, because a road shaved past a village reads as
        /// a carriageway driven through it. Only if the island will not carry the minimum
        /// that way is it packed tighter: smaller places, narrower woods, roads closer.</summary>
        readonly struct Squeeze
        {
            public readonly float Gap, RoadGap;
            public readonly int[] Widths;
            public readonly float[] Steps;
            public Squeeze(float gap, float roadGap, int[] widths, float[] steps)
            { Gap = gap; RoadGap = roadGap; Widths = widths; Steps = steps; }
        }

        static readonly Squeeze[] Squeezes =
        {
            new Squeeze(60f, 28f, new[] { 4, 3 }, new[] { 0f, 90f, 180f }),
            new Squeeze(40f, 18f, new[] { 3, 2 }, new[] { 0f, 60f, 120f, 190f }),
            new Squeeze(24f, 12f, new[] { 2 },    new[] { 0f, 45f, 90f, 140f, 190f, 240f }),
        };

        /// <summary>The fewest villages a town may come out with. Below this the island is
        /// packed tighter rather than left half empty.</summary>
        public const int MinSuburbs = 6;
        /// <summary>Half the ground an approach road keeps to itself across the wild.</summary>
        const float CorridorHalf = 20f;

        /// <summary>The rings of suburbs: how far out of town the first row of villages
        /// stands, and how much further out each row beyond it (a village deep, and its
        /// woods). Three rings of small places fill an island a good deal better than one
        /// ring of big ones, and read as a county rather than a rim.</summary>
        const float NearStrip = 110f, RingStep = 330f;
        const int Rings = 3;

        /// <summary>How deep a village runs, in rows of ~70 m.</summary>
        const int SuburbRows = 3;

        /// <summary>How much shore one ring of villages takes, road and woods and all:
        /// what a coast has to have to spare beyond a port or a field standing at its end
        /// if it is to carry houses as well.</summary>
        public const float SuburbRing = NearStrip + SuburbRows * SuburbRow + 50f + 2f * Gap;

        /// <summary>How many blocks of the grid a port's two gates may stand apart.</summary>
        static readonly int[] HarborGateSpans = { 2, 3 };

        // ------------------------------------------------------------------ roll

        public static List<DistrictSlot> Roll(Grid grid, int seed, int suburbsMin, int suburbsMax, bool wantHarbor)
            => Roll(grid, seed, suburbsMin, suburbsMax, wantHarbor, false);

        public static List<DistrictSlot> Roll(Grid grid, int seed, int suburbsMin, int suburbsMax, bool wantHarbor, bool wantAirport)
        {
            var rng = new System.Random(seed * 7919 + 13);
            var slots = new List<DistrictSlot>();
            // every foot of island already spoken for: a quarter's own ground, and the
            // corridor its approach road runs down
            var taken = new List<Held>();
            var names = new Names(rng, grid.CityName);

            var edges = new List<CityEdge> { CityEdge.South, CityEdge.West, CityEdge.North, CityEdge.East };
            Shuffle(edges, rng);

            // the airport first: it takes a whole shore (a mile of runway, then the grass
            // round it), by choice one of the long ones, and nothing else stands there
            if (wantAirport)
            {
                var ordered = new List<CityEdge>();
                foreach (var e in edges) if (e == CityEdge.South || e == CityEdge.North) ordered.Add(e);
                foreach (var e in edges) if (e == CityEdge.West || e == CityEdge.East) ordered.Add(e);
                foreach (var edge in ordered)
                {
                    var slot = TryAirport(grid, edge, rng);
                    if (slot == null || !Fits(grid, slot, taken)) continue;
                    slot.name = names.Airport();
                    Commit(grid, slot, slots, taken);
                    break;
                }
            }

            if (wantHarbor)
            {
                foreach (var edge in edges)
                {
                    var slot = TryHarbor(grid, edge, rng, taken);
                    if (slot == null) continue;
                    slot.name = names.Harbor();
                    Commit(grid, slot, slots, taken);
                    break;
                }
            }

            int want = suburbsMin + rng.Next(Mathf.Max(1, suburbsMax - suburbsMin + 1));
            int made = 0;
            // Generously first - wide places, woods between them, everybody's approach
            // road well clear of everybody's fence - and then, for whatever the town is
            // still short of, tighter passes that fill the gaps the first one left. The
            // good stretches of coast are taken at the generous spacing, so the crowding
            // only ever happens where there was room going spare; and MinSuburbs villages
            // are reached this way even on an island with nothing but corners left.
            for (int level = 0; level < Squeezes.Length && made < want; level++)
            {
                var how = Squeezes[level];
                // ring by ring INWARD, the far row of villages first. A quarter in the
                // second or third ring has to run its approach road out past everything
                // nearer town, and there are only so many lanes between the near villages
                // for it to thread through; laid nearest-first, the first ring took the
                // shore and the rings behind it had nowhere to come in, which is why a
                // town of ten suburbs kept coming out with four. The far rings claim
                // their lanes first and the near ones - which have the most lines to
                // choose from - fill in round them.
                for (int ring = Rings - 1; ring >= 0 && made < want; ring--)
                {
                    Shuffle(edges, rng);
                    foreach (var edge in edges)
                    {
                        if (made >= want) break;
                        bool vertical = edge == CityEdge.South || edge == CityEdge.North;
                        var pool = Ordinary(grid, vertical, forSuburb: true);
                        Shuffle(pool, rng);
                        while (pool.Count > 0 && made < want)
                        {
                            // the emptiest stretch of this shore first. Taking the lines
                            // in a shuffled order left villages shoulder to shoulder at
                            // one end and half the coast bare, and every quarter placed
                            // rules out its neighbours' lines, so the shore ran out at
                            // two or three.
                            int line = Loneliest(grid, vertical, pool, taken);
                            pool.Remove(line);
                            var slot = TrySuburb(grid, edge, line, ring, rng, seed, made, taken, how);
                            if (slot == null) continue;
                            slot.name = names.Suburb();
                            Commit(grid, slot, slots, taken);
                            made++;
                        }
                    }
                }
            }
            Tighten(grid, slots);
            return slots;
        }

        /// <summary>Pull the villages back in. The rings are laid outermost first so the
        /// far ones can thread their approach roads through, which leaves a quarter out at
        /// the third ring on a shore whose first two rings nothing else ever wanted - a
        /// hamlet eight hundred metres up an empty road. Once everything stands, each one
        /// is offered the nearer rings in turn and takes the first that still fits.</summary>
        static void Tighten(Grid grid, List<DistrictSlot> slots)
        {
            var others = new List<Held>();
            foreach (var slot in slots)
            {
                if (slot.kind != DistrictKind.Suburb) continue;
                others.Clear();
                foreach (var o in slots)
                {
                    if (o == slot) continue;
                    others.Add(new Held(WorldRect(grid, o), false));
                    others.Add(new Held(CorridorRect(grid, o), true));
                }
                float was = slot.strip;
                for (int ring = 0; ring < Rings; ring++)
                {
                    float strip = NearStrip + ring * RingStep;
                    if (strip >= was) break;
                    slot.strip = strip;
                    if (Fits(grid, slot, others)) break;
                    slot.strip = was;
                }
            }
        }

        /// <summary>Ground a quarter holds: its own, or the corridor of its approach road.
        /// Two villages keep woods between them; a road may run past a fence.</summary>
        readonly struct Held
        {
            public readonly Rect Rect;
            public readonly bool Road;
            public Held(Rect rect, bool road) { Rect = rect; Road = road; }
        }

        static void Commit(Grid grid, DistrictSlot slot, List<DistrictSlot> slots, List<Held> taken)
        {
            slots.Add(slot);
            taken.Add(new Held(WorldRect(grid, slot), false));
            taken.Add(new Held(CorridorRect(grid, slot), true));
        }

        /// <summary>The line of this shore that stands furthest from anything already
        /// placed - measured along the shore's own axis, so the villages spread down the
        /// coast instead of crowding one end of it.</summary>
        static int Loneliest(Grid grid, bool vertical, List<int> pool, List<Held> taken)
        {
            var axis = grid.Axis(vertical);
            int best = pool[0];
            float bestRoom = -1f;
            foreach (int line in pool)
            {
                float at = axis[line];
                float room = float.MaxValue;
                foreach (var t in taken)
                {
                    float lo = vertical ? t.Rect.xMin : t.Rect.yMin;
                    float hi = vertical ? t.Rect.xMax : t.Rect.yMax;
                    float d = at < lo ? lo - at : (at > hi ? at - hi : 0f);
                    if (d < room) room = d;
                }
                if (room > bestRoom) { bestRoom = room; best = line; }
            }
            return best;
        }

        // ------------------------------------------------------------- the port

        static DistrictSlot TryHarbor(Grid grid, CityEdge edge, System.Random rng, List<Held> taken)
        {
            bool vertical = edge == CityEdge.South || edge == CityEdge.North;
            var lines = Ordinary(grid, vertical, forSuburb: true);
            var axis = grid.Axis(vertical);
            float middle = (axis[0] + axis[axis.Length - 1]) * 0.5f;
            // the pairs of lines the two gates could hang off, the ones at the END of the
            // shore first: a working dock is at the end of a town, not behind its high
            // street - and the approach road of a port at the island's end runs the whole
            // depth of the shore, so down the middle it would cut that coast's suburbs in
            // two and leave nowhere for the second and third rings of them
            var pairs = new List<(int a, int b)>();
            foreach (int a in lines)
                foreach (int span in HarborGateSpans)
                    if (IsOrdinary(grid, vertical, a + span, forSuburb: true)) pairs.Add((a, a + span));
            if (pairs.Count == 0) return null;
            Shuffle(pairs, rng);
            pairs.Sort((p, q) => Mathf.Abs((axis[q.a] + axis[q.b]) * 0.5f - middle)
                          .CompareTo(Mathf.Abs((axis[p.a] + axis[p.b]) * 0.5f - middle)));
            // out at the island's end, its quay wall on the coast and the open sea beyond:
            // a port anywhere else is a rectangular pond cut into the wilderness
            float strip = Mathf.Max(200f, grid.ShoreOf(edge) - HarborDepth - CoastMargin);
            foreach (var pair in pairs)
            {
                var slot = new DistrictSlot
                {
                    kind = DistrictKind.Harbor,
                    edge = edge,
                    pinLines = new[] { pair.a, pair.b },
                    strip = strip,
                    toCoast = true,
                    seed = 1987 + rng.Next(400),
                    sizeAcross = 2 + rng.Next(3),      // berths
                };
                if (!Fits(grid, slot, taken)) continue;
                return slot;
            }
            return null;
        }

        // ---------------------------------------------------------- the airport

        static DistrictSlot TryAirport(Grid grid, CityEdge edge, System.Random rng)
        {
            bool vertical = edge == CityEdge.South || edge == CityEdge.North;
            var lines = Ordinary(grid, vertical);
            if (lines.Count == 0) return null;
            // the approach road comes off a line near the middle of the shore - the field
            // is not to hang off a corner the way the port does - but never the middle
            // line itself: dead centre it sits square on the downtown and reads as the
            // city's own back yard rather than a place you drive out to
            var axis = grid.Axis(vertical);
            float mid = (axis[0] + axis[axis.Length - 1]) * 0.5f;
            lines.Sort((a, b) => Mathf.Abs(axis[a] - mid).CompareTo(Mathf.Abs(axis[b] - mid)));
            int pick = Mathf.Min(lines.Count - 1, 1 + rng.Next(2));
            return new DistrictSlot
            {
                kind = DistrictKind.Airport,
                edge = edge,
                pinLines = new[] { lines[pick] },
                // out at the end of the island: the far threshold on the coast, the
                // approach road the drive back into town
                strip = Mathf.Max(180f, grid.ShoreOf(edge) - AirportDepth - CoastMargin),
                toCoast = true,
                seed = 1987 + rng.Next(400),
            };
        }

        // ----------------------------------------------------------- the suburbs

        static DistrictSlot TrySuburb(Grid grid, CityEdge edge, int line, int ring, System.Random rng,
                                      int citySeed, int index, List<Held> taken, Squeeze how)
        {
            float shore = grid.ShoreOf(edge);
            float depth = SuburbRows * SuburbRow + 50f;
            var slot = new DistrictSlot
            {
                kind = DistrictKind.Suburb,
                edge = edge,
                pinLines = new[] { line },
                seed = citySeed * 977 + index * 131 + ring * 17 + 7,
                sizeDeep = SuburbRows,
            };
            // the ring says roughly how far out of town, not exactly: a village shoved a
            // hundred metres further out clears the corner of the one beside it often
            // enough to be worth trying, and the row comes out ragged rather than ruled,
            // which is how a county road picks up its hamlets. And as much shore as it
            // can have: the widest that clears whatever else stands on this coast.
            foreach (float step in how.Steps)
            {
                float strip = NearStrip + ring * RingStep + step + rng.Next(3) * 15f;
                if (strip + depth > shore - 40f) return null;   // it would stand in the water
                slot.strip = strip;
                foreach (int across in how.Widths)
                {
                    slot.sizeAcross = across;
                    if (Fits(grid, slot, taken, how.Gap, how.RoadGap)) return slot;
                }
            }
            return null;
        }

        // ---------------------------------------------------------------- rules

        static List<int> Ordinary(Grid grid, bool vertical, bool forSuburb = false)
        {
            var axis = grid.Axis(vertical);
            var list = new List<int>();
            for (int i = 1; i + 1 < axis.Length; i++)      // never the corner lines
                if (IsOrdinary(grid, vertical, i, forSuburb)) list.Add(i);
            return list;
        }

        /// <summary>Whether a road line may carry a quarter's approach road.
        ///
        /// A port and a field want a proper street of their own: two lanes, no seam
        /// beside it, or the approach comes off the edge of the park or down a river's
        /// bank. A VILLAGE is not so particular, and the shore of this grid has three
        /// such lines on it in total - which is why a town could only ever have two or
        /// three suburbs. So a suburb takes a boulevard as well (an arterial that
        /// narrows to two lanes at the town line is what every American suburb is
        /// reached by) and the streets that run alongside a park or the wild strip; only
        /// the river's own banks stay clear, and its mouth is kept clear besides by the
        /// span test in <see cref="Fits"/>.</summary>
        static bool IsOrdinary(Grid grid, bool vertical, int line, bool forSuburb = false)
        {
            var axis = grid.Axis(vertical);
            if (line <= 0 || line + 1 >= axis.Length) return false;
            if (forSuburb) return true;
            if (grid.Boulevard(vertical)[line]) return false;
            if (grid.Blocked != null && grid.Blocked(vertical, line)) return false;
            return true;
        }

        /// <summary>Whether the slot may stand where it says: off the river's way out of
        /// town, clear of every quarter already placed and clear of their approach roads.</summary>
        static bool Fits(Grid grid, DistrictSlot slot, List<Held> taken)
            => Fits(grid, slot, taken, Gap, RoadGap);

        static bool Fits(Grid grid, DistrictSlot slot, List<Held> taken, float gap, float roadGap)
        {
            bool vertical = slot.edge == CityEdge.South || slot.edge == CityEdge.North;
            var span = SpanOf(grid, slot);
            // never astride the river's way out: the channel is carved down to the seabed
            // the whole way to the coast, and nothing crosses it but on a bridge
            if (grid.Rivers != null)
                foreach (var r in grid.Rivers(vertical))
                    if (span.x < r.y + RiverGap && r.x < span.y + RiverGap) return false;

            var body = WorldRect(grid, slot);
            var road = CorridorRect(grid, slot);
            foreach (var t in taken)
            {
                // ground beside ground keeps its woods; ground beside somebody's approach
                // road only has to keep off the carriageway, and two roads crossing the
                // same wild is what a road junction is for
                if (Overlaps(body, t.Rect, t.Road ? roadGap : gap)) return false;
                if (!t.Road && Overlaps(road, t.Rect, roadGap)) return false;
            }
            return true;
        }

        static bool Overlaps(Rect a, Rect b, float by)
            => a.xMin < b.xMax + by && b.xMin < a.xMax + by &&
               a.yMin < b.yMax + by && b.yMin < a.yMax + by;

        /// <summary>Metres of shore a quarter keeps clear of a river's channel, on top of
        /// the river's own banks: room for the ground to come back up out of the channel
        /// before the first fence.</summary>
        const float RiverGap = 25f;

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
            // a port runs a berth past its gates and an airport its whole field past its
            // one approach road; a suburb is as wide as it was rolled, its pins somewhere
            // inside that width and the flanks carrying the rest either side
            float flank;
            if (slot.kind == DistrictKind.Harbor) flank = HarborFlank;
            else if (slot.kind == DistrictKind.Airport) flank = AirportFlank;
            else flank = Mathf.Max(60f, (SuburbWidth(slot) - (hi - lo)) * 0.5f);
            return new Vector2(lo - flank, hi + flank);
        }

        /// <summary>How wide and how deep a rolled suburb comes out, metres.</summary>
        static float SuburbWidth(DistrictSlot slot) => (slot.sizeAcross > 0 ? slot.sizeAcross : 5) * SuburbBlock;
        static float SuburbDepth(DistrictSlot slot) => (slot.sizeDeep > 0 ? slot.sizeDeep : SuburbRows) * SuburbRow + 50f;

        /// <summary>Roughly the ground a slot takes in the world: its span along the shore,
        /// from the strip out to the depth its kind runs - a port with the water its ships
        /// need off the quay, an airport its whole field.</summary>
        public static Rect WorldRect(Grid grid, DistrictSlot slot)
        {
            var span = SpanOf(grid, slot);
            float depth;
            switch (slot.kind)
            {
                case DistrictKind.Harbor: depth = HarborDepth + HarborBasin; break;
                case DistrictKind.Airport: depth = AirportDepth; break;
                default: depth = SuburbDepth(slot); break;
            }
            return OutwardRect(grid, slot.edge, span.x, span.y, slot.strip, slot.strip + depth);
        }

        /// <summary>The ground the quarter's approach road holds across the wild: from the
        /// city's kerb out to the quarter's own edge, a lane's width either side of the
        /// pin lines. Nothing else may stand on it, or the road runs through a front room.</summary>
        static Rect CorridorRect(Grid grid, DistrictSlot slot)
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
            if (lo > hi) return Rect.zero;
            return OutwardRect(grid, slot.edge, lo - CorridorHalf, hi + CorridorHalf, 0f, slot.strip);
        }

        /// <summary>A rectangle laid outward off one shore: <paramref name="lo"/> to
        /// <paramref name="hi"/> along that shore, <paramref name="from"/> to
        /// <paramref name="to"/> metres out from the grid's own kerb.</summary>
        static Rect OutwardRect(Grid grid, CityEdge edge, float lo, float hi, float from, float to)
        {
            bool vertical = edge == CityEdge.South || edge == CityEdge.North;
            var across = grid.Axis(!vertical);
            // the grid's outer edge along the other axis (its axes plus a road's half and a pavement)
            float edgeLo = across[0] - 15f, edgeHi = across[across.Length - 1] + 15f;
            switch (edge)
            {
                case CityEdge.South: return Rect.MinMaxRect(lo, edgeLo - to, hi, edgeLo - from);
                case CityEdge.North: return Rect.MinMaxRect(lo, edgeHi + from, hi, edgeHi + to);
                case CityEdge.West: return Rect.MinMaxRect(edgeLo - to, lo, edgeLo - from, hi);
                default: return Rect.MinMaxRect(edgeHi + from, lo, edgeHi + to, hi);
            }
        }

        static void Shuffle<T>(List<T> list, System.Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        // ---------------------------------------------------------------- names

        /// <summary>What the quarters are called. A town of 1987 does not have "Suburb"
        /// and "Suburb 2" round it: it has Fairview and Oak Hollow and Mill Creek, and the
        /// map prints them across their own ground. Drawn without replacement off the
        /// city's own seed, so a town keeps its places for as long as its seed stands.</summary>
        sealed class Names
        {
            static readonly string[] SuburbPool =
            {
                "Fairview", "Oak Hollow", "Mill Creek", "Glenwood", "Cedar Hills",
                "Riverton", "Ashbury", "Brookfield", "Elmhurst", "Maple Grove",
                "Northgate", "Pinecrest", "Silver Lake", "Westhaven", "Foxglen",
                "Bayview", "Lindenwood", "Rockaway", "Sunnyside", "Kellerton",
                "Cranberry Flats", "Old Mill", "Shady Bend", "Harlow Springs",
            };
            static readonly string[] HarborPool =
            {
                "Dockside", "Harbor Point", "Old Wharf", "Ironside Docks", "Cannery Row",
            };
            static readonly string[] FieldPool = { "Regional", "Municipal", "County Field" };

            readonly List<string> _suburbs;
            readonly System.Random _rng;
            readonly string _city;
            int _taken;

            public Names(System.Random rng, string city)
            {
                _rng = rng;
                _city = string.IsNullOrEmpty(city) ? null : city;
                _suburbs = new List<string>(SuburbPool);
                for (int i = _suburbs.Count - 1; i > 0; i--)
                {
                    int j = rng.Next(i + 1);
                    (_suburbs[i], _suburbs[j]) = (_suburbs[j], _suburbs[i]);
                }
            }

            public string Suburb() => _suburbs[_taken++ % _suburbs.Count];

            public string Harbor()
                => _city != null && _rng.NextDouble() < 0.4
                    ? _city + " Docks"
                    : HarborPool[_rng.Next(HarborPool.Length)];

            public string Airport()
            {
                string tail = FieldPool[_rng.Next(FieldPool.Length)];
                return _city != null ? _city + " " + tail : "Regional Airport";
            }
        }
    }
}
