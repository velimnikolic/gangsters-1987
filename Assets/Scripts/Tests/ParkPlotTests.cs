using System.Collections.Generic;
using UnityEngine;
using LivingCity.Generation;

namespace LivingCity.Tests
{
    /// <summary>
    /// Properties of the park's plantable plot. The defect these exist for actually shipped: the
    /// hedge moved out to the block line and the planting did not follow, which left a 9.5m ring
    /// of bare lawn inside the park's own fence, all the way round.
    ///
    /// Same discipline as HedgeLayoutTests - no UnityEngine.Object, failures returned as data, so
    /// a bare .NET host can call Run() by reflection with no Editor.
    /// </summary>
    public static class ParkPlotTests
    {
        // World distances - see HedgeLayoutTests. Inset is NOT one of them: it is half a lime's
        // 7m crown, and trees keep their authored size while the streets stretch around them.
        const float Clearance = 7f * CityGrid.TileScale;
        const float MainClearance = 10f * CityGrid.TileScale;
        const float MapEdge = CityGrid.CellSize * 0.5f - 0.8f;
        const float Inset = 1.5f;
        const float Eps = 1e-3f;

        static float CellHalf => CityGrid.CellSize * 0.5f;

        public static List<string> Run()
        {
            var failures = new List<string>();

            StreetSideReachesTheHedge(failures);
            AvenueSideIsHeldFurtherBack(failures);
            MapEdgeSideStaysInsideTheCell(failures);
            InteriorEdgeStopsAtTheCellBoundary(failures);
            MarginKeepsAWideCrownInside(failures);
            TowardAgreesWithTheFourFields(failures);

            return failures;
        }

        static CityGrid Roads(int width, int height, params Vector2Int[] holes)
        {
            var grid = new CityGrid(width, height);

            for (var x = 0; x < width; x++)
            for (var z = 0; z < height; z++)
                grid[x, z] = CellType.Road;

            foreach (var hole in holes)
                grid[hole.x, hole.y] = CellType.Empty;

            return grid;
        }

        static ParkPlot.Extent Plot(CityGrid grid, Vector2Int cell, params Vector2Int[] block) =>
            ParkPlot.For(grid, new HashSet<Vector2Int>(block), cell,
                         Clearance, MainClearance, MapEdge, Inset);

        /// <summary>
        /// The whole point: a street-facing side plants out to the hedge, which stands PAST the
        /// cell boundary. Anything at or under the old 13.5 means the ring is still bare.
        /// </summary>
        static void StreetSideReachesTheHedge(List<string> failures)
        {
            var cell = new Vector2Int(1, 1);
            var plot = Plot(Roads(3, 3, cell), cell, cell);

            var expected = CityGrid.CellSize - Clearance - Inset;

            if (Mathf.Abs(plot.East - expected) > Eps)
                failures.Add($"Street-facing limit is {plot.East}, expected {expected}.");

            if (plot.East <= CellHalf)
                failures.Add($"Street-facing limit {plot.East} does not pass the cell boundary " +
                             $"{CellHalf} - the ring inside the hedge is still bare lawn.");

            // All four sides of a cell surrounded by ordinary streets are the same.
            if (Mathf.Abs(plot.Least - plot.Greatest) > Eps)
                failures.Add("A cell with four identical streets around it produced four " +
                             "different limits.");
        }

        /// <summary>
        /// The avenue's facades stand further back, so the hedge does, so the planting does. Get
        /// this wrong and the park's trees stand in the dual carriageway's outer lane.
        /// </summary>
        static void AvenueSideIsHeldFurtherBack(List<string> failures)
        {
            var cell = new Vector2Int(1, 1);
            var grid = Roads(3, 3, cell);
            grid.SetMainRoad(2, 1, northSouth: true);

            var plot = Plot(grid, cell, cell);

            var expected = CityGrid.CellSize - MainClearance - Inset;

            if (Mathf.Abs(plot.East - expected) > Eps)
                failures.Add($"Avenue-facing limit is {plot.East}, expected {expected}.");

            if (plot.East >= plot.West)
                failures.Add($"Avenue limit {plot.East} is not held back from the street limit " +
                             $"{plot.West}.");
        }

        /// <summary>
        /// No road across a side means no road tile to plant on, and BlockRect applies no reach
        /// there either - so the plot stops inside the cell, as it always did.
        /// </summary>
        static void MapEdgeSideStaysInsideTheCell(List<string> failures)
        {
            var cell = new Vector2Int(0, 0);
            var plot = Plot(Roads(3, 3, cell), cell, cell);

            var expected = MapEdge - Inset;

            if (Mathf.Abs(plot.West - expected) > Eps)
                failures.Add($"Off-map limit is {plot.West}, expected {expected}.");

            if (plot.West >= CellHalf)
                failures.Add($"Off-map limit {plot.West} reaches past the cell boundary " +
                             $"{CellHalf} - planting would hang over the edge of the world.");

            // The sides that DO face a street are unaffected by the one that does not.
            if (plot.East <= CellHalf)
                failures.Add("A map-edge cell lost its street-facing reach as well.");
        }

        /// <summary>
        /// Where the park carries on into its own next cell the plot stops at the boundary
        /// exactly - no inset, because there is no boundary to push a crown through, and no
        /// reach, because the neighbour plants that ground itself.
        /// </summary>
        static void InteriorEdgeStopsAtTheCellBoundary(List<string> failures)
        {
            var west = new Vector2Int(1, 1);
            var east = new Vector2Int(2, 1);
            var grid = Roads(4, 3, west, east);

            var plot = Plot(grid, west, west, east);

            if (Mathf.Abs(plot.East - CellHalf) > Eps)
                failures.Add($"Interior limit is {plot.East}, expected the cell boundary " +
                             $"{CellHalf} - two cells are both planting the strip between them.");

            if (plot.West <= CellHalf)
                failures.Add("The outward side of a two-cell park lost its reach to the hedge.");

            // And the far cell mirrors it, so the pair meets exactly once.
            var far = Plot(grid, east, west, east);
            if (Mathf.Abs(far.West - CellHalf) > Eps)
                failures.Add($"The neighbour's interior limit is {far.West}, expected {CellHalf}.");
        }

        /// <summary>
        /// A mound is placed by its centre and is ten metres wide; a lime has a 7m crown. The
        /// margin is what makes Contains answer for the whole object rather than for its pivot.
        /// </summary>
        static void MarginKeepsAWideCrownInside(List<string> failures)
        {
            var cell = new Vector2Int(1, 1);
            var plot = Plot(Roads(3, 3, cell), cell, cell);

            var onTheLine = new Vector2(plot.East, 0f);

            if (!plot.Contains(onTheLine))
                failures.Add("A point exactly on the limit is reported outside the plot.");

            if (plot.Contains(onTheLine, 1f))
                failures.Add("A point on the limit is reported inside even with a metre of " +
                             "margin - a wide crown would push straight through the hedge.");

            if (!plot.Contains(Vector2.zero, plot.Least))
                failures.Add("The cell centre with the tightest limit as margin is reported " +
                             "outside - Least does not describe a square that fits.");

            if (plot.Contains(new Vector2(0f, plot.North + 0.5f)))
                failures.Add("A point beyond the northern limit is reported inside.");
        }

        /// <summary>
        /// Toward is what the row and quadrant passes read, so it has to agree with the fields
        /// the containment test uses. A mismatch plants one side's rows against another's limit.
        /// </summary>
        static void TowardAgreesWithTheFourFields(List<string> failures)
        {
            var cell = new Vector2Int(1, 1);
            var grid = Roads(3, 3, cell);

            // Four different limits, so a swapped axis cannot pass by coincidence.
            grid.SetMainRoad(2, 1, northSouth: true);
            grid[1, 2] = CellType.Empty;   // no road north - map-edge rule
            var plot = Plot(grid, cell, cell);

            var checks = new (Vector2 direction, float expected, string name)[]
            {
                (new Vector2(1f, 0f), plot.East, "East"),
                (new Vector2(0f, 1f), plot.North, "North"),
                (new Vector2(-1f, 0f), plot.West, "West"),
                (new Vector2(0f, -1f), plot.South, "South"),
            };

            foreach (var (direction, expected, name) in checks)
                if (Mathf.Abs(plot.Toward(direction) - expected) > Eps)
                    failures.Add($"Toward({direction.x},{direction.y}) gave " +
                                 $"{plot.Toward(direction)}, expected {name} = {expected}.");

            if (Mathf.Abs(plot.North - (MapEdge - Inset)) > Eps)
                failures.Add($"The side facing a non-road cell is {plot.North}, expected the " +
                             $"map-edge rule {MapEdge - Inset}.");

            // The premise the four checks above rest on: both axes carry two different limits.
            if (Mathf.Abs(plot.East - plot.West) <= Eps || Mathf.Abs(plot.North - plot.South) <= Eps)
                failures.Add($"The fixture no longer distinguishes its axes (E {plot.East} / " +
                             $"W {plot.West}, N {plot.North} / S {plot.South}), so a swapped " +
                             "field would pass by coincidence.");
        }
    }
}
