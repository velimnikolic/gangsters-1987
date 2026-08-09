using System.Collections.Generic;
using UnityEngine;
using LivingCity.Generation;

namespace LivingCity.Tests
{
    /// <summary>
    /// The two rules that keep the city's landmark - the bank, most visibly - facing the street
    /// it was meant to face, with nothing painted underneath it.
    ///
    /// Both were broken in the same screenshot and neither is visible from a unit test of the
    /// generator as a whole, because the failure is a placement that looks perfectly legal: the
    /// bank stood on a real street run, just the wrong one, and the stripes under it came from a
    /// bay that was correctly rejected and incorrectly painted.
    ///
    /// Same discipline as HedgeLayoutTests and ParkPlotTests - no UnityEngine.Object anywhere,
    /// failures returned as data rather than logged - so a bare .NET host can call Run() by
    /// reflection with no Editor. That works here because both functions under test are pure:
    /// ChooseLandmarkFront reads a list of lot rectangles, PaintStreetBay reads stall centres,
    /// and neither instantiates anything or touches rng.
    /// </summary>
    public static class BankFrontageTests
    {
        const float Eps = 1e-3f;

        /// <summary>Runs every check. An empty list means everything passed.</summary>
        public static List<string> Run()
        {
            var failures = new List<string>();

            LongestStreetFrontWins(failures);
            AlleysAndMapEdgesAreNeverChosen(failures);
            BoulevardOnlyBreaksTies(failures);
            NoStreetAtAllIsRefused(failures);
            ChoiceIsDeterministic(failures);

            PaintingEveryStallMatchesTheOldInlineLayout(failures);
            ABlockedStallIsNotPaintedRound(failures);
            AGapBreaksTheHeadLine(failures);
            PaintingNothingDrawsNothing(failures);

            BusBayHoldsTheBus(failures);
            BusBayAndCarBaysNeverOverlap(failures);
            SchoolFrontageFitsABusAndParents(failures);
            BusBayIsPaintedAsAThreeSidedBox(failures);

            return failures;
        }

        // ------------------------------------------------------- the school's bus berth
        //
        // Measured inputs, not chosen ones: bus-school is 3.03 wide x 9.77 long and
        // building-school is 24.90 across its frontage, both read off the pack's own FBX with a
        // parser calibrated against tile-road-straight (30.00) and truck (6.25). If the pack is
        // ever swapped these are the numbers to re-measure, and these assertions are what will
        // say so.

        const float BusLength = 9.77f;
        const float BusWidth = 3.03f;
        const float SchoolFrontage = 24.90f;

        /// <summary>
        /// The berth is big enough for the vehicle that lives in it, in both axes. The whole
        /// reason it lies ALONG the kerb is that it is not: nosed in, 9.77m of bus against
        /// StallDepth leaves 4.17m of it standing in the carriageway.
        /// </summary>
        static void BusBayHoldsTheBus(List<string> failures)
        {
            if (ParkingLayout.BusStallLength < BusLength)
                failures.Add($"Bus bay: {ParkingLayout.BusStallLength}m of frontage cannot hold " +
                             $"a {BusLength}m bus.");

            if (ParkingLayout.StallDepth < BusWidth)
                failures.Add($"Bus bay: {ParkingLayout.StallDepth}m of depth cannot hold a " +
                             $"{BusWidth}m-wide bus lying along the kerb.");

            // And the reason it is not nose-in, stated as an assertion so that anyone who
            // "simplifies" it back into a car bay fails here rather than in a screenshot.
            if (ParkingLayout.StallDepth >= BusLength)
                failures.Add("Bus bay: a nose-in bay would now fit the bus - the parallel berth " +
                             "exists only because it does not, so re-derive it.");
        }

        /// <summary>
        /// The berth and the parents' bays share one frontage and must not share one metre of it.
        /// They abut exactly, and Bounds.Intersects counts touching as intersecting - which is
        /// exactly why BlockBuilder surveys both BEFORE it reserves either - so this checks the
        /// geometry with a hair of tolerance rather than through that test.
        /// </summary>
        static void BusBayAndCarBaysNeverOverlap(List<string> failures)
        {
            var origin = new Vector3(10f, 0f, -4f);
            var along = Vector3.right;
            var outward = Vector3.back;

            var bus = ParkingLayout.ForBusBay(origin, along, outward, 0f,
                                              ParkingLayout.BusStallLength);
            var cars = ParkingLayout.ForStreetBay(
                origin, along, outward, ParkingLayout.BusStallLength,
                SchoolFrontage - ParkingLayout.BusStallLength, paint: false);

            if (bus.Stalls.Count != 1)
            {
                failures.Add($"Bus bay: expected one berth, got {bus.Stalls.Count}.");
                return;
            }

            var berth = bus.Stalls[0].Centre;
            var berthHigh = Vector3.Dot(berth - origin, along) + ParkingLayout.BusStallLength * 0.5f;

            foreach (var stall in cars.Stalls)
            {
                var low = Vector3.Dot(stall.Centre - origin, along) - ParkingLayout.StallWidth * 0.5f;
                if (low < berthHigh - Eps)
                    failures.Add($"Bus bay: a car bay starts at {low:0.00}m, inside the berth " +
                                 $"that ends at {berthHigh:0.00}m.");
            }

            // Same band, same depth: the berth and the bays must share one head line, or the
            // forecourt reads as two schemes meeting in the middle.
            var berthDepth = Vector3.Dot(origin - berth, outward);
            foreach (var stall in cars.Stalls)
            {
                var stallDepth = Vector3.Dot(origin - stall.Centre, outward);
                if (Mathf.Abs(stallDepth - berthDepth) > Eps)
                    failures.Add($"Bus bay: berth sits {berthDepth:0.00}m in, car bays " +
                                 $"{stallDepth:0.00}m - they are not in the same band.");
            }

            // The berth lies ALONG the street; the car bays nose into it. Ninety degrees apart.
            var turn = Mathf.Abs(Mathf.DeltaAngle(bus.Stalls[0].Yaw, cars.Stalls[0].Yaw));
            if (Mathf.Abs(turn - 90f) > 0.5f)
                failures.Add($"Bus bay: berth and car bays are {turn:0.0} degrees apart, not 90.");
        }

        /// <summary>
        /// The school's real frontage has room for the bus AND enough bays to be worth painting.
        /// Three is the floor rather than one: SchoolForecourtMaxCars bakes a static car into
        /// one of them, and a forecourt with a single live bay is not a forecourt.
        /// </summary>
        static void SchoolFrontageFitsABusAndParents(List<string> failures)
        {
            var remaining = SchoolFrontage - ParkingLayout.BusStallLength;
            var bays = Mathf.FloorToInt(remaining / ParkingLayout.StallWidth);

            if (remaining < ParkingLayout.StallWidth)
                failures.Add($"School forecourt: the berth leaves {remaining:0.00}m, not even " +
                             "one car bay - BlockBuilder would give the bus no berth at all.");
            else if (bays < 3)
                failures.Add($"School forecourt: only {bays} car bays beside the berth; one " +
                             "takes a static bake, which leaves too few for the parents.");
        }

        /// <summary>
        /// Three lines, not four: two sides running in from the lot edge and one across the
        /// closed end. The fourth would be a stripe along the kerb, which no other bay in the
        /// city draws - see PaintStreetBay, which emits exactly the same three per run.
        /// </summary>
        static void BusBayIsPaintedAsAThreeSidedBox(List<string> failures)
        {
            var origin = new Vector3(-3f, 0f, 12f);
            var bus = ParkingLayout.ForBusBay(origin, Vector3.forward, Vector3.right, 0f,
                                              ParkingLayout.BusStallLength);

            if (bus.Markings.Count != 3)
            {
                failures.Add($"Bus bay: {bus.Markings.Count} lines painted, expected 3.");
                return;
            }

            // No line may run along the kerb line itself - that is the open side.
            foreach (var line in bus.Markings)
            {
                var onKerb = Mathf.Abs(line.A.x - origin.x) < Eps
                          && Mathf.Abs(line.B.x - origin.x) < Eps;
                if (onKerb)
                    failures.Add("Bus bay: a line is painted across the open side.");
            }

            // A degenerate berth paints nothing rather than a dot.
            if (ParkingLayout.ForBusBay(origin, Vector3.forward, Vector3.right, 0f, 0f)
                             .Stalls.Count != 0)
                failures.Add("Bus bay: a zero-length berth still produced a stall.");
        }

        // ------------------------------------------------------------------ fixtures

        /// <summary>
        /// One lot rectangle with its four street flags. Street order everywhere in this file is
        /// BuildLot's: 0 South, 1 East, 2 North, 3 West.
        /// </summary>
        static BlockLots.Lot Lot(
            float minX, float minZ, float maxX, float maxZ,
            bool south, bool east, bool north, bool west) =>
            new()
            {
                Min = new Vector2(minX, minZ),
                Max = new Vector2(maxX, maxZ),
                South = south,
                East = east,
                North = north,
                West = west,
            };

        static string SideName(int side) => side switch
        {
            0 => "South",
            1 => "East",
            2 => "North",
            3 => "West",
            _ => $"?{side}",
        };

        // ------------------------------------------------------------------ the front

        /// <summary>
        /// The block that produced the bug: a lot whose south run is short and whose east run is
        /// long, both genuine streets. First-fit walked S-E-N-W and took the south one, which is
        /// how the bank came to address a side street. Length decides now.
        /// </summary>
        static void LongestStreetFrontWins(List<string> failures)
        {
            var lots = new List<BlockLots.Lot>
            {
                Lot(0f, 0f, 20f, 60f, south: true, east: true, north: false, west: false),
            };

            BlockBuilder.ChooseLandmarkFront(lots, Sides.None, out var lot, out var side);

            if (lot != 0 || side != 1)
                failures.Add($"LongestStreetFront: expected lot 0 East (60m) but got " +
                             $"lot {lot} {SideName(side)}");

            // And across lots, not only within one - the landmark used to go to whichever lot the
            // column-major plan reached first, which on a subdivided block is always the
            // south-west corner however little frontage it has.
            var twoLots = new List<BlockLots.Lot>
            {
                Lot(0f, 0f, 18f, 18f, south: true, east: false, north: false, west: true),
                Lot(22f, 0f, 70f, 18f, south: true, east: true, north: false, west: false),
            };

            BlockBuilder.ChooseLandmarkFront(twoLots, Sides.None, out lot, out side);

            if (lot != 1 || side != 0)
                failures.Add($"LongestStreetFront: expected lot 1 South (48m) but got " +
                             $"lot {lot} {SideName(side)}");
        }

        /// <summary>
        /// The rule the whole change exists for. BlockLots leaves a side's street flag false for
        /// an internal alley and for a side the map boundary cut off, so a candidate set drawn
        /// from those flags cannot contain one - however much longer it is than the alternative.
        /// </summary>
        static void AlleysAndMapEdgesAreNeverChosen(List<string> failures)
        {
            // North is 200m of alley, South is 12m of street. The alley must lose.
            var lots = new List<BlockLots.Lot>
            {
                Lot(0f, 0f, 200f, 40f, south: false, east: false, north: false, west: false),
                Lot(0f, 44f, 12f, 60f, south: true, east: false, north: false, west: false),
            };

            BlockBuilder.ChooseLandmarkFront(lots, Sides.None, out var lot, out var side);

            if (lot != 1 || side != 0)
                failures.Add($"AlleysNeverChosen: expected the 12m street (lot 1 South) but got " +
                             $"lot {lot} {SideName(side)}");
        }

        /// <summary>
        /// The avenue is a tie-break and nothing more. Ranked above length it would take a stub
        /// on the boulevard over a full block face on a through street, which is the wrong
        /// address for a building whose whole job is to be read from across the street.
        /// </summary>
        static void BoulevardOnlyBreaksTies(List<string> failures)
        {
            // Equal runs, one of them on the avenue: the avenue wins.
            var square = new List<BlockLots.Lot>
            {
                Lot(0f, 0f, 40f, 40f, south: true, east: false, north: true, west: false),
            };

            BlockBuilder.ChooseLandmarkFront(square, Sides.North, out var lot, out var side);

            if (side != 2)
                failures.Add($"BoulevardBreaksTies: expected North (the avenue) but got {SideName(side)}");

            // Unequal runs, the avenue on the short one: length still wins.
            var oblong = new List<BlockLots.Lot>
            {
                Lot(0f, 0f, 60f, 20f, south: true, east: true, north: false, west: false),
            };

            BlockBuilder.ChooseLandmarkFront(oblong, Sides.East, out lot, out side);

            if (side != 0)
                failures.Add($"BoulevardBreaksTies: the 60m street should still beat the 20m " +
                             $"avenue, but got {SideName(side)}");
        }

        /// <summary>
        /// A block with no street on any side - possible against the map boundary - reserves
        /// nothing, and BuildLot's fallback is what then decides. -1 rather than 0 is what makes
        /// the two distinguishable.
        /// </summary>
        static void NoStreetAtAllIsRefused(List<string> failures)
        {
            var lots = new List<BlockLots.Lot>
            {
                Lot(0f, 0f, 40f, 40f, south: false, east: false, north: false, west: false),
            };

            BlockBuilder.ChooseLandmarkFront(lots, Sides.None, out var lot, out var side);

            if (lot != -1 || side != -1)
                failures.Add($"NoStreetAtAll: expected no reservation but got lot {lot} side {side}");
        }

        /// <summary>
        /// Same lot plan, same answer. The plan is a pure function of (seed, blockId) and this
        /// has to be a pure function of the plan, or the same seed stops producing the same city.
        /// </summary>
        static void ChoiceIsDeterministic(List<string> failures)
        {
            var lots = new List<BlockLots.Lot>
            {
                Lot(0f, 0f, 30f, 30f, south: true, east: true, north: false, west: true),
                Lot(34f, 0f, 64f, 30f, south: true, east: true, north: true, west: false),
            };

            BlockBuilder.ChooseLandmarkFront(lots, Sides.East, out var firstLot, out var firstSide);
            BlockBuilder.ChooseLandmarkFront(lots, Sides.East, out var againLot, out var againSide);

            if (firstLot != againLot || firstSide != againSide)
                failures.Add($"Deterministic: {firstLot}/{firstSide} then {againLot}/{againSide}");
        }

        // ------------------------------------------------------------------ the paint

        static ParkingLayout.Layout Bay(float width, bool paint) =>
            ParkingLayout.ForStreetBay(Vector3.zero, Vector3.right, Vector3.back, 0f, width, paint);

        static List<int> All(ParkingLayout.Layout layout)
        {
            var all = new List<int>(layout.Stalls.Count);
            for (var i = 0; i < layout.Stalls.Count; i++)
                all.Add(i);
            return all;
        }

        static bool Same(ParkingLayout.Line a, ParkingLayout.Line b) =>
            (a.A - b.A).sqrMagnitude < Eps && (a.B - b.B).sqrMagnitude < Eps;

        /// <summary>
        /// The refactor's own guard. The lines used to be emitted inline from the run arithmetic
        /// and are now derived from the stall centres; with every stall kept the two must agree
        /// exactly, or the change moved paint on every block in the city rather than only where
        /// a bay was blocked.
        /// </summary>
        static void PaintingEveryStallMatchesTheOldInlineLayout(List<string> failures)
        {
            var inline = Bay(16f, paint: true);
            var derived = Bay(16f, paint: false);
            ParkingLayout.PaintStreetBay(derived, Vector3.right, Vector3.back, All(derived));

            if (inline.Markings.Count != derived.Markings.Count)
            {
                failures.Add($"PaintMatchesInline: {inline.Markings.Count} lines inline against " +
                             $"{derived.Markings.Count} derived");
                return;
            }

            for (var i = 0; i < inline.Markings.Count; i++)
                if (!Same(inline.Markings[i], derived.Markings[i]))
                    failures.Add($"PaintMatchesInline: line {i} is " +
                                 $"{derived.Markings[i].A}->{derived.Markings[i].B}, " +
                                 $"expected {inline.Markings[i].A}->{inline.Markings[i].B}");
        }

        /// <summary>
        /// The defect itself: a bay something else is standing in must contribute NO paint. Under
        /// the old code FillStalls skipped the stall and the stripes went down anyway, which is
        /// how parking lines ended up running under a building.
        /// </summary>
        static void ABlockedStallIsNotPaintedRound(List<string> failures)
        {
            var layout = Bay(16f, paint: false);
            if (layout.Stalls.Count != 5)
            {
                failures.Add($"BlockedStall: fixture expected 5 stalls, got {layout.Stalls.Count}");
                return;
            }

            // Everything but the last bay - an obstacle in the corner, which is exactly the
            // geometry a landmark on the neighbouring side produces.
            ParkingLayout.PaintStreetBay(layout, Vector3.right, Vector3.back,
                                         new List<int> { 0, 1, 2, 3 });

            var lost = layout.Stalls[4].Centre;
            foreach (var line in layout.Markings)
            {
                // Nothing may be drawn strictly inside the blocked bay. Its own edges are fair
                // game - the low one is the kept neighbour's divider and has to stay.
                var inside = (line.A.x > lost.x - ParkingLayout.StallWidth * 0.5f + Eps
                              && line.A.x < lost.x + ParkingLayout.StallWidth * 0.5f - Eps)
                          || (line.B.x > lost.x - ParkingLayout.StallWidth * 0.5f + Eps
                              && line.B.x < lost.x + ParkingLayout.StallWidth * 0.5f - Eps);
                if (inside)
                    failures.Add($"BlockedStall: {line.A}->{line.B} runs through the blocked bay " +
                                 $"at x={lost.x}");
            }

            // Four bays: five dividers and one head line.
            if (layout.Markings.Count != 6)
                failures.Add($"BlockedStall: expected 6 lines for 4 kept bays, got " +
                             $"{layout.Markings.Count}");
        }

        /// <summary>
        /// A hole in the middle splits the closed end into two runs. One line drawn straight
        /// across would pass through the very bay the survey rejected, which is the same defect
        /// one dimension over.
        /// </summary>
        static void AGapBreaksTheHeadLine(List<string> failures)
        {
            var layout = Bay(16f, paint: false);
            ParkingLayout.PaintStreetBay(layout, Vector3.right, Vector3.back,
                                         new List<int> { 0, 1, 3, 4 });

            // outward points AT the street, so the bay runs INTO the lot: the closed end is at
            // +StallDepth, and Flat packs (x, z) so a Line's .y is the depth axis here.
            var back = ParkingLayout.StallDepth;
            var heads = 0;
            var dividers = 0;
            foreach (var line in layout.Markings)
                if (Mathf.Abs(line.A.y - back) < Eps && Mathf.Abs(line.B.y - back) < Eps)
                    heads++;
                else
                    dividers++;

            if (heads != 2)
                failures.Add($"GapBreaksHead: expected 2 head lines either side of the hole, got {heads}");

            // Two runs of two bays: three dividers each, none shared across the hole.
            if (dividers != 6)
                failures.Add($"GapBreaksHead: expected 6 dividers, got {dividers}");
        }

        /// <summary>Every bay blocked is a bay of bare asphalt, not an outline of one.</summary>
        static void PaintingNothingDrawsNothing(List<string> failures)
        {
            var layout = Bay(16f, paint: false);
            ParkingLayout.PaintStreetBay(layout, Vector3.right, Vector3.back, new List<int>());

            if (layout.Markings.Count != 0)
                failures.Add($"PaintNothing: {layout.Markings.Count} lines for no kept bays");
        }
    }
}
