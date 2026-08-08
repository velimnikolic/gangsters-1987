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

            return failures;
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
