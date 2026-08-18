using System.Collections.Generic;
using UnityEngine;
using LivingCity.Generation;

namespace LivingCity.Tests
{
    /// <summary>
    /// What YardParkingPlan promises about the lot it lays out: it stands on free ground,
    /// it stays on the pad, every row is deep enough to hold a whole car with padding at
    /// both ends, a second row only ever appears with an aisle and a drive to reach it,
    /// and the side it picked is the best of the four rather than the first one that
    /// fitted.
    ///
    /// Same discipline as HedgeLayoutTests and BankFrontageTests - a plain static class
    /// holding no UnityEngine.Object, returning failures as data rather than logging them,
    /// so it can be loaded into a bare .NET host and called by reflection with no Editor
    /// and no Play mode. That works here for the same reason: the plan is arithmetic over
    /// a bool grid and instantiates nothing.
    ///
    /// The assertions are written against the properties - free, on the pad, deep enough,
    /// reachable, best available - and not against 6.4 or 10, so retuning the row band or
    /// the car length does not require editing them.
    /// </summary>
    public static class YardParkingTests
    {
        const float Cell = 1f;

        /// <summary>Runs every check. An empty list means everything passed.</summary>
        public static List<string> Run()
        {
            var failures = new List<string>();

            FullYardTakesNoseBays(failures);
            ChosenStripIsFreeGround(failures);
            ShallowSetbackFallsBackToKerbside(failures);
            NothingIsPlacedWithNoRoom(failures);
            DeepestSideWins(failures);
            NoseBeatsALongerKerb(failures);
            AFullPadIsNotParking(failures);
            EveryRowHoldsAWholeCar(failures);
            TheLotStaysInsideTheStrip(failures);
            ADeepYardTakesTwoRows(failures);
            ASecondRowAlwaysHasADriveToReachIt(failures);
            TheDriveNeverStandsOnABay(failures);
            AShortFrontageDropsTheDriveNotTheRow(failures);
            TheDriveOpensAtTheStreetCorner(failures);
            TheAisleServesBothRows(failures);
            ASingleRowIsEnteredOverTheKerb(failures);
            OnlyAServedLotClosesItsFrontage(failures);

            return failures;
        }

        // ------------------------------------------------------------------ fixtures

        /// <summary>A pad with one rectangular building on it, given in metres from the
        /// pad's minimum corner.</summary>
        static bool[,] Yard(float width, float depth, params Rect[] buildings)
        {
            var grid = new bool[Mathf.FloorToInt(width / Cell), Mathf.FloorToInt(depth / Cell)];
            foreach (var b in buildings)
                for (var i = Mathf.FloorToInt(b.xMin / Cell); i < Mathf.CeilToInt(b.xMax / Cell); i++)
                    for (var j = Mathf.FloorToInt(b.yMin / Cell); j < Mathf.CeilToInt(b.yMax / Cell); j++)
                        if (i >= 0 && j >= 0 && i < grid.GetLength(0) && j < grid.GetLength(1))
                            grid[i, j] = true;
            return grid;
        }

        /// <summary>A yard clear all the way across, the one fixture where the plan has to
        /// choose on depth alone.</summary>
        static bool[,] OpenYard(float width, float depth) => Yard(width, depth);

        // --------------------------------------------------------------------- checks

        /// <summary>A block whose building leaves 12 m of yard on its south side parks in
        /// it, nose-in, on that side.</summary>
        static void FullYardTakesNoseBays(List<string> failures)
        {
            var grid = Yard(70f, 50f, new Rect(0f, 12f, 70f, 38f));
            var plan = YardParkingPlan.Choose(grid, Cell);

            if (plan.Kind != YardParkingPlan.Kind.Nose)
                failures.Add($"12 m of clear yard should take nose-in bays, got {plan.Kind}");
            if (plan.Side != YardParkingPlan.Side.South)
                failures.Add($"the free strip is on the south side, the plan says {plan.Side}");
            if (plan.Bays < YardParkingPlan.BaysPerTile)
                failures.Add($"70 m of frontage should hold more than one tile, got {plan.Units}");
        }

        /// <summary>Every cell of the chosen rectangle is free, and inside the grid. This is
        /// the one that matters: a strip overlapping the building would put a car through a
        /// wall.</summary>
        static void ChosenStripIsFreeGround(List<string> failures)
        {
            // An L of building, so three sides are partly blocked and the survey has to
            // find the run rather than take a whole side.
            var grid = Yard(100f, 70f,
                            new Rect(0f, 0f, 40f, 70f),
                            new Rect(40f, 20f, 60f, 50f));

            var plan = YardParkingPlan.Choose(grid, Cell);
            if (plan.Kind == YardParkingPlan.Kind.None)
            {
                failures.Add("an L-shaped building leaves a 20 m deep strip and got no plan");
                return;
            }

            var nx = grid.GetLength(0);
            var nz = grid.GetLength(1);
            var deep = Mathf.CeilToInt(plan.Depth / Cell) - 1;

            for (var a = Mathf.FloorToInt(plan.Start / Cell);
                 a < Mathf.CeilToInt((plan.Start + plan.Length) / Cell); a++)
                for (var d = 0; d <= deep; d++)
                {
                    YardParkingPlan.Cell(plan.Side, a, d, nx, nz, out var i, out var j);
                    if (i < 0 || j < 0 || i >= nx || j >= nz)
                    {
                        failures.Add($"the {plan.Side} strip reaches cell ({i},{j}), off the pad");
                        return;
                    }
                    if (grid[i, j])
                    {
                        failures.Add($"the {plan.Side} strip stands on the building at ({i},{j})");
                        return;
                    }
                }
        }

        /// <summary>A setback too shallow to nose into still parks, along the kerb.</summary>
        static void ShallowSetbackFallsBackToKerbside(List<string> failures)
        {
            // Clear of the cell the building's own edge falls in: the grid blocks whole
            // cells, so a setback has to be a cell deeper than the metres it needs.
            var setback = YardParkingPlan.ParallelDepth + Cell;
            var grid = Yard(70f, 50f, new Rect(0f, setback, 70f, 50f));
            var plan = YardParkingPlan.Choose(grid, Cell);

            if (plan.Kind != YardParkingPlan.Kind.Parallel)
                failures.Add($"a {setback:F1} m setback holds a car lengthways, got {plan.Kind}");
            if (plan.Bays < 2)
                failures.Add("70 m of kerb holds more than one car, got " + plan.Bays);
            if (plan.Rows != 1)
                failures.Add("kerbside parking is one row of cars, the plan says " + plan.Rows);
            if (plan.HasDrive)
                failures.Add("kerbside parking has no lot and no entrance drive");
        }

        /// <summary>A strip too shallow even for that gets nothing at all - the pass has to
        /// be able to say no, or every block sprouts a car in its hedge.</summary>
        static void NothingIsPlacedWithNoRoom(List<string> failures)
        {
            var grid = Yard(70f, 50f, new Rect(0f, 2f, 70f, 50f));
            var plan = YardParkingPlan.Choose(grid, Cell);

            if (plan.Kind != YardParkingPlan.Kind.None)
                failures.Add($"a 2 m setback cannot hold a car, the plan says {plan.Kind}");
            if (plan.Bays != 0)
                failures.Add("a plan with no kind still promises " + plan.Bays + " bays");
        }

        /// <summary>Two sides deep enough, one deeper: the deeper one is chosen, because it
        /// is the one that can stack two rows.</summary>
        static void DeepestSideWins(List<string> failures)
        {
            // Building pushed into the north-east corner: 8 m of yard on the south side,
            // 20 m on the west, both the full length of their side.
            var grid = Yard(60f, 60f, new Rect(20f, 8f, 60f, 60f));
            var plan = YardParkingPlan.Choose(grid, Cell);

            if (plan.Side != YardParkingPlan.Side.West)
                failures.Add($"the west strip is 20 m deep against the south's 8, chose {plan.Side}");
        }

        /// <summary>A kerb long enough for ten cars does not beat bays off the street:
        /// nose-in is the thing being asked for.</summary>
        static void NoseBeatsALongerKerb(List<string> failures)
        {
            // One tile's worth of deep corner on the south side, against a 4 m setback
            // running the whole hundred metres of the north side - thirteen kerb slots
            // against three bays.
            var grid = Yard(100f, 40f,
                            new Rect(0f, 12f, 100f, 24f),
                            new Rect(12f, 0f, 88f, 12f));

            var plan = YardParkingPlan.Choose(grid, Cell);
            if (plan.Kind != YardParkingPlan.Kind.Nose)
                failures.Add($"a 12 m x 12 m corner takes bays before a kerb does, got {plan.Kind}");
        }

        /// <summary>A pad with no free ground at all is not a car park.</summary>
        static void AFullPadIsNotParking(List<string> failures)
        {
            var grid = Yard(70f, 50f, new Rect(0f, 0f, 70f, 50f));
            if (YardParkingPlan.Choose(grid, Cell).Kind != YardParkingPlan.Kind.None)
                failures.Add("a pad covered by its building was given parking");
        }

        /// <summary>
        /// The complaint this layout exists to answer: a row band has to hold the longest
        /// car the parking pass will park, WHOLE, with daylight at each end of it - and the
        /// pass filters its cars by ParkingLayout.StallDepth, so that is the length to
        /// check against.
        /// </summary>
        static void EveryRowHoldsAWholeCar(List<string> failures)
        {
            foreach (var depth in new[] { 9f, 14f, 22f, 40f })
            {
                var grid = OpenYard(80f, depth);
                var plan = YardParkingPlan.Choose(grid, Cell);
                if (plan.Kind != YardParkingPlan.Kind.Nose)
                {
                    failures.Add($"a clear {depth:F0} m yard should take nose-in bays, got {plan.Kind}");
                    continue;
                }

                for (var row = 0; row < plan.Rows; row++)
                {
                    if (plan.RowBack(row) - plan.RowFront(row) < ParkingLayout.StallDepth)
                        failures.Add($"row {row} of the {depth:F0} m yard is only " +
                                     $"{plan.RowBack(row) - plan.RowFront(row):F1} m deep");
                    if (plan.RowFront(row) < YardParkingPlan.MinApron)
                        failures.Add($"row {row} of the {depth:F0} m yard stands " +
                                     $"{plan.RowFront(row):F1} m off the kerb, inside the apron");
                }
            }
        }

        /// <summary>Nothing the plan lays out reaches past the free depth it was given, or
        /// past the frontage it was found on. The apron behind the last row counts: a bay
        /// pushed up against a wall is the padding complaint from the other end.</summary>
        static void TheLotStaysInsideTheStrip(List<string> failures)
        {
            foreach (var depth in new[] { 8f, 9f, 12f, 20f, 21f, 30f, 60f })
                foreach (var width in new[] { 12f, 16f, 24f, 37f, 70f, 100f })
                {
                    var plan = YardParkingPlan.Choose(OpenYard(width, depth), Cell);
                    if (plan.Kind != YardParkingPlan.Kind.Nose)
                        continue;

                    var what = $"a {width:F0} x {depth:F0} m yard";

                    if (plan.Reach > plan.Depth + 0.001f)
                        failures.Add($"{what}: the lot reaches {plan.Reach:F1} m into " +
                                     $"{plan.Depth:F1} m of free depth");
                    if (plan.Apron < YardParkingPlan.MinApron - 0.001f)
                        failures.Add($"{what}: {plan.Apron:F2} m of apron, less than the " +
                                     $"{YardParkingPlan.MinApron:F1} m minimum");
                    if (plan.Apron > YardParkingPlan.Apron + 0.001f)
                        failures.Add($"{what}: {plan.Apron:F2} m of apron, more than the " +
                                     $"{YardParkingPlan.Apron:F1} m it asks for");
                    if (plan.Units * YardParkingPlan.TileFrontage > plan.BayLength + 0.001f)
                        failures.Add($"{what}: {plan.Units} tiles do not fit the " +
                                     $"{plan.BayLength:F1} m left over for them");
                    if (plan.BayStart < plan.Start - 0.001f ||
                        plan.BayStart + plan.BayLength > plan.Start + plan.Length + 0.001f)
                        failures.Add($"{what}: the bays run outside the free frontage");
                }
        }

        /// <summary>A yard deep enough for two bands and an aisle gets two rows - the second
        /// half of "two rows of cars if there is room".</summary>
        static void ADeepYardTakesTwoRows(List<string> failures)
        {
            var plan = YardParkingPlan.Choose(OpenYard(60f, YardParkingPlan.TwoRowDepth + 4f), Cell);

            if (plan.Rows != 2)
                failures.Add($"a {YardParkingPlan.TwoRowDepth + 4f:F0} m deep yard holds two " +
                             $"rows, the plan lays {plan.Rows}");
            if (plan.Rows == 2 &&
                plan.RowFront(1) - plan.RowBack(0) < YardParkingPlan.Aisle - 0.001f)
                failures.Add($"the two rows are {plan.RowFront(1) - plan.RowBack(0):F1} m apart, " +
                             $"less than the {YardParkingPlan.Aisle:F0} m aisle they need");

            // ... and a yard that is deep but narrow cannot: the drive would eat the only
            // tile the frontage holds.
            var narrow = YardParkingPlan.Choose(
                OpenYard(YardParkingPlan.TileFrontage + 2f, YardParkingPlan.TwoRowDepth + 4f), Cell);
            if (narrow.Rows > 1)
                failures.Add("a frontage of one tile cannot carry two rows AND their drive");
        }

        /// <summary>The rear row is reached through the drive and nothing else, so a plan
        /// with two rows and no drive is a row of bays nobody can get to.</summary>
        static void ASecondRowAlwaysHasADriveToReachIt(List<string> failures)
        {
            for (var width = 10f; width <= 120f; width += 1f)
                for (var depth = 8f; depth <= 40f; depth += 1f)
                {
                    var plan = YardParkingPlan.Choose(OpenYard(width, depth), Cell);
                    if (plan.Rows > 1 && !plan.HasDrive)
                    {
                        failures.Add($"a {width:F0} x {depth:F0} m yard was given {plan.Rows} " +
                                     "rows and no way in");
                        return;
                    }
                    if (plan.Rows > YardParkingPlan.MaxRows)
                    {
                        failures.Add($"a {width:F0} x {depth:F0} m yard was given {plan.Rows} rows");
                        return;
                    }
                }
        }

        /// <summary>The drive is frontage the tiles may not use, at one end of the run - so
        /// the bays start after it or stop before it, and never overlap it.</summary>
        static void TheDriveNeverStandsOnABay(List<string> failures)
        {
            foreach (var width in new[] { 16f, 17f, 26f, 41f, 70f, 100f })
            {
                var plan = YardParkingPlan.Choose(OpenYard(width, 12f), Cell);
                if (!plan.HasDrive)
                {
                    failures.Add($"a clear {width:F0} m frontage has room for a drive, got none");
                    continue;
                }

                var driveEnd = plan.DriveStart + YardParkingPlan.DriveWidth;
                if (plan.BayStart < driveEnd - 0.001f && plan.BayStart + plan.BayLength > plan.DriveStart + 0.001f)
                    failures.Add($"a {width:F0} m frontage: the drive at " +
                                 $"{plan.DriveStart:F1}-{driveEnd:F1} m overlaps the bays at " +
                                 $"{plan.BayStart:F1}-{plan.BayStart + plan.BayLength:F1} m");
                if (plan.DriveStart < plan.Start - 0.001f || driveEnd > plan.Start + plan.Length + 0.001f)
                    failures.Add($"a {width:F0} m frontage: the drive is off the free run");
                if (plan.DriveReach > plan.Depth + 0.001f)
                    failures.Add($"a {width:F0} m frontage: the drive runs " +
                                 $"{plan.DriveReach:F1} m into {plan.Depth:F1} m of depth");
            }
        }

        /// <summary>A frontage with room for either a drive or a tile but not both keeps the
        /// tile: a single row nosed into off the street is a real thing, a row nobody can
        /// reach is not.</summary>
        static void AShortFrontageDropsTheDriveNotTheRow(List<string> failures)
        {
            var plan = YardParkingPlan.Choose(OpenYard(YardParkingPlan.TileFrontage + 2f, 12f), Cell);

            if (plan.Kind != YardParkingPlan.Kind.Nose)
                failures.Add($"a {YardParkingPlan.TileFrontage + 2f:F0} m frontage still holds " +
                             $"one tile of bays, got {plan.Kind}");
            if (plan.Units < 1)
                failures.Add("the tile was dropped to make room for the drive");
            if (plan.HasDrive)
                failures.Add("a frontage this short cannot hold a drive as well as a tile");
        }

        /// <summary>
        /// Two rows share the aisle between them: each row's OPEN end is on the aisle and
        /// its closed end - the wheel stops - is away from it. That is what lets the street
        /// frontage be fenced, so it is asserted rather than assumed.
        /// </summary>
        static void TheAisleServesBothRows(List<string> failures)
        {
            var plan = YardParkingPlan.Choose(OpenYard(60f, YardParkingPlan.TwoRowDepth + 6f), Cell);
            if (plan.Rows != 2)
            {
                failures.Add($"expected a two-row lot to test the aisle with, got {plan.Rows} row(s)");
                return;
            }

            if (plan.RowHead(0) != plan.RowFront(0))
                failures.Add("the front row of a two-row lot is closed at the kerb end");
            if (plan.RowHead(1) != plan.RowBack(1))
                failures.Add("the back row of a two-row lot is closed at the far end");
            if (!plan.FrontCanBeFenced)
                failures.Add("a lot whose aisle serves both rows can be fenced along the street");
        }

        /// <summary>One row has no aisle - the street is it - so the bays are closed at the
        /// far end and the frontage has to stay open for a car to turn into one.</summary>
        static void ASingleRowIsEnteredOverTheKerb(List<string> failures)
        {
            var plan = YardParkingPlan.Choose(OpenYard(60f, 12f), Cell);
            if (plan.Rows != 1)
            {
                failures.Add($"expected a one-row lot, got {plan.Rows} rows");
                return;
            }

            if (plan.HeadAtKerb(0))
                failures.Add("a street-served row cannot be closed at the kerb");
            if (plan.FrontCanBeFenced)
                failures.Add("fencing the frontage of a street-served row walls its cars in");
        }

        /// <summary>And nothing anywhere in the range says otherwise.</summary>
        static void OnlyAServedLotClosesItsFrontage(List<string> failures)
        {
            for (var width = 10f; width <= 120f; width += 2f)
                for (var depth = 8f; depth <= 40f; depth += 2f)
                {
                    var plan = YardParkingPlan.Choose(OpenYard(width, depth), Cell);
                    if (!plan.FrontCanBeFenced)
                        continue;
                    if (plan.Rows < 2 || !plan.HasDrive || plan.Kind != YardParkingPlan.Kind.Nose)
                    {
                        failures.Add($"a {width:F0} x {depth:F0} m yard would fence its frontage " +
                                     $"with {plan.Rows} row(s), drive {plan.HasDrive}, {plan.Kind}");
                        return;
                    }
                }
        }

        /// <summary>The mouth goes at the end of the run nearer a corner of the pad, which
        /// is where the cross street is - not into the middle of a block face.</summary>
        static void TheDriveOpensAtTheStreetCorner(List<string> failures)
        {
            // A building in the middle of the pad leaves the whole south side free, so the
            // run spans it and either end is a corner: the low end is chosen.
            var whole = YardParkingPlan.Choose(OpenYard(70f, 14f), Cell);
            if (whole.HasDrive && !whole.DriveAtStart)
                failures.Add("a run spanning the whole side puts its drive at the low end");

            // Free frontage pushed against the high corner: the drive follows it there.
            var grid = Yard(100f, 14f, new Rect(0f, 0f, 55f, 14f));
            var plan = YardParkingPlan.Choose(grid, Cell);
            if (plan.Kind != YardParkingPlan.Kind.Nose)
            {
                failures.Add($"45 m of clear yard should take nose-in bays, got {plan.Kind}");
                return;
            }
            if (!plan.HasDrive)
                failures.Add("45 m of frontage has room for a drive");
            else if (plan.DriveAtStart)
                failures.Add("the free frontage ends at the high corner, and so should the drive");
        }
    }
}
