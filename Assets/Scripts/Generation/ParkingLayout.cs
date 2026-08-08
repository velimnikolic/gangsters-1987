using System.Collections.Generic;
using UnityEngine;

namespace LivingCity.Generation
{
    /// <summary>
    /// Where the cars stand and where the paint goes. Pure geometry - this class instantiates
    /// nothing, which is the point: BlockBuilder spawns the vehicles and ParkingMarkings builds
    /// the line mesh, and both read the SAME Layout. Computing the two separately is the mistake
    /// GroundPlacer avoids by sharing BlockBuilder.BlockRect, and it bites harder here, because a
    /// few centimetres of drift between a bay and its paint is visible from the default camera
    /// and miserable to track down.
    ///
    /// The numbers below are measured against the pack's own vehicles rather than chosen. The
    /// parked-car catalogue runs 4.09m (jeep-open) to 6.25m (truck) long and 1.69 to 2.26 wide,
    /// so a 2.7 x 5.6 bay holds every one of them except the lorry - which is correct, a lorry
    /// does not fit a marked car bay either.
    ///
    /// This replaced a layout of 3.2m spacing on an 11m-deep row with a 4m aisle. An 11m row for
    /// a 4.7m car left six metres of bare asphalt around every vehicle BEFORE the aisle, and the
    /// row pitch of 15m fitted only three rows into a 46m block. That, not the car count, is why
    /// a car park read as an empty slab.
    /// </summary>
    public static class ParkingLayout
    {
        /// <summary>Widest vehicle in the catalogue is 2.26, so this leaves 0.44 of door room.</summary>
        public const float StallWidth = 2.7f;

        /// <summary>
        /// Admits car-tow-truck at 5.43 and rejects truck at 6.25. Passed to VehiclePicker.Next
        /// as maxLength, so the lorry is filtered out at the source rather than left to overhang.
        /// </summary>
        public const float StallDepth = 5.6f;

        /// <summary>Enough for a car to turn ninety degrees out of a bay.</summary>
        public const float AisleWidth = 6f;

        /// <summary>Fence to first aisle.</summary>
        public const float EdgeMargin = 2.5f;

        /// <summary>
        /// Cross lane at the gate end. Wider than EdgeMargin because it is the one strip that
        /// joins every aisle to the entrance - without it the gate opens into a single aisle and
        /// the rest of the lot is unreachable, which reads wrong even standing still.
        /// </summary>
        public const float GateMargin = 6f;

        /// <summary>Gap left in the fence. Two lanes' worth, in and out.</summary>
        public const float GateWidth = 7f;

        public struct Stall
        {
            public Vector3 Centre;
            public float Yaw;
        }

        /// <summary>Centreline of one painted stripe. Width and lift are ParkingMarkings' business.</summary>
        public struct Line
        {
            public Vector2 A;
            public Vector2 B;
        }

        public sealed class Layout
        {
            public readonly List<Stall> Stalls = new();
            public readonly List<Line> Markings = new();

            /// <summary>False when the block has no road on any side - nothing to open onto.</summary>
            public bool HasGate;

            /// <summary>Middle of the gap in the fence, on the fence line itself.</summary>
            public Vector3 GateCentre;

            /// <summary>Unit vector pointing out of the lot through the gate.</summary>
            public Vector3 GateOutward;
        }

        /// <summary>
        /// The whole-block car park: bays either side of long aisles, a cross lane at the gate.
        ///
        /// The aisle axis is taken from <paramref name="roadSides"/> rather than from which side
        /// of the block is longer. A block against the map boundary can have no road on its long
        /// axis at all - that is exactly what RoadSides exists to tell us - and aisles running
        /// into a dead edge would put the gate in a fence facing nothing. Deriving the axis from
        /// where the streets actually are means the entrance always opens straight into the grid,
        /// with no special case for edge blocks.
        /// </summary>
        public static Layout ForBlock(Vector2 min, Vector2 max, Sides roadSides)
        {
            var layout = new Layout();

            // East/West road -> aisles run along X and the gate is on an X end. Otherwise
            // North/South. A block with no roads at all still gets bays, just no opening.
            var alongX = (roadSides & (Sides.East | Sides.West)) != 0
                      || (roadSides & (Sides.North | Sides.South)) == 0;

            var gateAtMax = alongX
                ? (roadSides & Sides.East) != 0
                : (roadSides & Sides.North) != 0;

            layout.HasGate = roadSides != Sides.None;

            // (a, c) is the lot's own frame: a runs along the aisles, c stacks the rows.
            var aMin = alongX ? min.x : min.y;
            var aMax = alongX ? max.x : max.y;
            var cMin = alongX ? min.y : min.x;
            var cMax = alongX ? max.y : max.x;

            // The bay run gives up GateMargin at the entrance end and EdgeMargin at the other.
            var runMin = aMin + (gateAtMax ? EdgeMargin : GateMargin);
            var runMax = aMax - (gateAtMax ? GateMargin : EdgeMargin);

            var stalls = Mathf.FloorToInt((runMax - runMin) / StallWidth);
            if (stalls < 1)
                return layout;

            var runStart = runMin + ((runMax - runMin) - stalls * StallWidth) * 0.5f;
            var runEnd = runStart + stalls * StallWidth;

            // Counted before anything is placed, because the leftover depth is handed back to the
            // aisles - a car park that stops 8m short of its own fence looks like a bug, and the
            // alternative (padding the margins) just moves the empty strip to the edge.
            var budget = (cMax - EdgeMargin) - (cMin + EdgeMargin);
            var used = AisleWidth;
            var aisles = 1;
            var bands = 0;

            while (used + 2f * StallDepth + AisleWidth <= budget)
            {
                used += 2f * StallDepth + AisleWidth;
                bands++;
                aisles++;
            }

            // A single row can still be served by the aisle that precedes it.
            var tail = budget - used >= StallDepth;
            if (tail)
                used += StallDepth;

            if (bands == 0 && !tail)
                return layout;

            var aisle = AisleWidth + (budget - used) / aisles;

            var aisleCentres = new List<float>(aisles);
            var c = cMin + EdgeMargin;

            for (var band = 0; band < bands; band++)
            {
                aisleCentres.Add(c + aisle * 0.5f);
                c += aisle;

                // Two rows back to back, each nosing away from the aisle that serves it, so their
                // closed ends meet in the middle of the band and share one head line.
                EmitRow(layout, alongX, runStart, stalls, c, facesMax: true);
                EmitRow(layout, alongX, runStart, stalls, c + StallDepth, facesMax: false);

                EmitDividers(layout, alongX, runStart, stalls, c, c + 2f * StallDepth);
                EmitLine(layout, alongX, runStart, runEnd, c + StallDepth);

                c += 2f * StallDepth;
            }

            aisleCentres.Add(c + aisle * 0.5f);
            c += aisle;

            if (tail)
            {
                EmitRow(layout, alongX, runStart, stalls, c, facesMax: true);
                EmitDividers(layout, alongX, runStart, stalls, c, c + StallDepth);
                EmitLine(layout, alongX, runStart, runEnd, c + StallDepth);
            }

            if (layout.HasGate)
            {
                // On the aisle nearest the middle of the lot, so the entrance lines up with a
                // lane rather than with the end of a row of bays.
                var target = (cMin + cMax) * 0.5f;
                var gateC = aisleCentres[0];
                foreach (var candidate in aisleCentres)
                    if (Mathf.Abs(candidate - target) < Mathf.Abs(gateC - target))
                        gateC = candidate;

                var gateA = gateAtMax ? aMax : aMin;
                layout.GateCentre = World(alongX, gateA, gateC);
                layout.GateOutward = alongX
                    ? new Vector3(gateAtMax ? 1f : -1f, 0f, 0f)
                    : new Vector3(0f, 0f, gateAtMax ? 1f : -1f);
            }

            return layout;
        }

        /// <summary>
        /// The short run of bays that breaks up a street wall - see BlockBuilder.PlaceParking.
        /// Same bay size as the car park, so the two never disagree about what a stall is.
        ///
        /// <paramref name="origin"/> is the lot corner and <paramref name="outward"/> points at
        /// the street, which is the frame BuildLot already walks its four sides in. Bays run from
        /// the lot edge inward; the cars keep the existing facing, nose to the kerb.
        ///
        /// <paramref name="paint"/> false lays the bays out and leaves Markings empty, for a
        /// caller that has to survey them against what is already standing before it knows which
        /// ones it keeps - see PaintStreetBay. The bays a survey rejects used to be painted
        /// anyway: FillStalls tests each one and skips it, but the lines had already gone into
        /// the mesh, so an obstacle overlapping a bay left stripes under itself with no car in
        /// them and no warning anywhere.
        /// </summary>
        public static Layout ForStreetBay(
            Vector3 origin, Vector3 along, Vector3 outward, float cursor, float width,
            bool paint = true)
        {
            var layout = new Layout();

            var stalls = Mathf.FloorToInt(width / StallWidth);
            if (stalls < 1)
                return layout;

            var start = cursor + (width - stalls * StallWidth) * 0.5f;
            var yaw = Mathf.Atan2(outward.x, outward.z) * Mathf.Rad2Deg;

            for (var i = 0; i < stalls; i++)
                layout.Stalls.Add(new Stall
                {
                    Centre = origin
                           + along * (start + StallWidth * (i + 0.5f))
                           - outward * (StallDepth * 0.5f),
                    Yaw = yaw,
                });

            if (!paint)
                return layout;

            var all = new int[stalls];
            for (var i = 0; i < stalls; i++)
                all[i] = i;

            PaintStreetBay(layout, along, outward, all);
            return layout;
        }

        /// <summary>
        /// The paint for a street bay, restricted to the stalls the caller actually keeps: one
        /// divider per bay edge running from the lot line inward, plus a head line across the
        /// closed end of each unbroken run.
        ///
        /// Derived from the stall CENTRES rather than from the run arithmetic that produced
        /// them, which is the same discipline the class note asks for one level up - a bay and
        /// its lines cannot drift apart if only one of them is ever computed. Passing every
        /// index reproduces exactly what ForStreetBay used to emit inline, in the same order.
        ///
        /// Appends; it does not clear Markings first, so it composes with a layout that already
        /// carries some.
        /// </summary>
        public static void PaintStreetBay(
            Layout layout, Vector3 along, Vector3 outward, IReadOnlyList<int> stalls)
        {
            if (layout == null || layout.Stalls.Count == 0 || stalls == null)
                return;

            var kept = new bool[layout.Stalls.Count];
            foreach (var index in stalls)
                if (index >= 0 && index < kept.Length)
                    kept[index] = true;

            var half = along * (StallWidth * 0.5f);
            var depth = outward * StallDepth;

            // A divider shared by two kept bays is drawn ONCE - two co-linear quads laid end to
            // end show a seam down the middle, which is why EmitDividers exists for the
            // whole-block layout. So each kept bay emits its low edge, and its high edge only
            // when the bay above it is not kept and will therefore never emit that edge itself.
            for (var i = 0; i < kept.Length; i++)
            {
                if (!kept[i])
                    continue;

                var front = layout.Stalls[i].Centre + outward * (StallDepth * 0.5f);

                var low = front - half;
                layout.Markings.Add(new Line { A = Flat(low), B = Flat(low - depth) });

                if (i + 1 < kept.Length && kept[i + 1])
                    continue;

                var high = front + half;
                layout.Markings.Add(new Line { A = Flat(high), B = Flat(high - depth) });
            }

            // One head line per unbroken run rather than one for the whole bay: a gap in the
            // middle is a bay somebody else is standing in, and a line drawn straight through it
            // is the very thing this overload exists to stop.
            for (var i = 0; i < kept.Length; i++)
            {
                if (!kept[i])
                    continue;

                var last = i;
                while (last + 1 < kept.Length && kept[last + 1])
                    last++;

                var from = layout.Stalls[i].Centre + outward * (StallDepth * 0.5f) - half - depth;
                var to = layout.Stalls[last].Centre + outward * (StallDepth * 0.5f) + half - depth;
                layout.Markings.Add(new Line { A = Flat(from), B = Flat(to) });

                i = last;
            }
        }

        /// <summary>
        /// One row of bays whose open side is the aisle. facesMax means the aisle is on the low-c
        /// side, so the car drives in along +c and its nose ends at the far end of the bay.
        /// </summary>
        static void EmitRow(
            Layout layout, bool alongX, float runStart, int stalls, float c, bool facesMax)
        {
            // Forward is +c when facing max. In world terms c is Z for an X-aligned lot and X for
            // a Z-aligned one, which is the 0/180 versus 90/270 split below.
            var yaw = alongX
                ? (facesMax ? 0f : 180f)
                : (facesMax ? 90f : 270f);

            for (var i = 0; i < stalls; i++)
                layout.Stalls.Add(new Stall
                {
                    Centre = World(alongX, runStart + StallWidth * (i + 0.5f), c + StallDepth * 0.5f),
                    Yaw = yaw,
                });
        }

        /// <summary>
        /// Bay dividers across a whole band at once. Two back-to-back rows share their dividers -
        /// drawing one stripe per row instead would lay two co-linear quads end to end and show a
        /// seam down the middle of every band.
        /// </summary>
        static void EmitDividers(
            Layout layout, bool alongX, float runStart, int stalls, float from, float to)
        {
            for (var i = 0; i <= stalls; i++)
            {
                var a = runStart + StallWidth * i;
                layout.Markings.Add(new Line
                {
                    A = Flat(World(alongX, a, from)),
                    B = Flat(World(alongX, a, to)),
                });
            }
        }

        static void EmitLine(Layout layout, bool alongX, float from, float to, float c) =>
            layout.Markings.Add(new Line
            {
                A = Flat(World(alongX, from, c)),
                B = Flat(World(alongX, to, c)),
            });

        static Vector3 World(bool alongX, float a, float c) =>
            alongX ? new Vector3(a, 0f, c) : new Vector3(c, 0f, a);

        static Vector2 Flat(Vector3 point) => new(point.x, point.z);
    }
}
