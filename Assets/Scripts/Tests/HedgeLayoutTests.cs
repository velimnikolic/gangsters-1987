using System.Collections.Generic;
using UnityEngine;
using LivingCity.Generation;

namespace LivingCity.Tests
{
    /// <summary>
    /// Properties of the park hedge that a notch at a corner, or a gate opening into a bush,
    /// would violate.
    ///
    /// Written under the same discipline as TrafficModelTests - a plain static class holding no
    /// UnityEngine.Object, returning failures as data rather than logging them, so it can be
    /// loaded into a bare .NET host and called by reflection with no Editor and no Play mode.
    /// That is only possible because HedgeLayout instantiates nothing and CityGrid is an ordinary
    /// class: a rule that spawned prefabs would need a scene to check, and this is precisely the
    /// rule worth checking, because when it is wrong it is wrong by eight metres.
    ///
    /// The assertions are written against the properties - the boundary closes, the corners
    /// mitre, a gate sits on its walk - and not against 23 or 14.2, so retuning
    /// CityConfig.sidewalkWidth does not require editing them. Only breaking the property does.
    /// </summary>
    public static class HedgeLayoutTests
    {
        // World distances, so they carry CityGrid.TileScale exactly as CityConfig.sidewalkWidth
        // and ParkDresser's own (CellHalf - HedgeInset) do. Written as the authored figure times
        // the scale rather than as 9.1, so stretching the city cannot silently orphan them.
        const float Clearance = 7f * CityGrid.TileScale;
        const float MainClearance = 10f * CityGrid.TileScale;
        const float MapEdge = CityGrid.CellSize * 0.5f - 0.8f;
        const float GateHalf = 3f;
        const float Lift = 0.02f;
        const float Eps = 1e-3f;

        /// <summary>Runs every check. An empty list means everything passed.</summary>
        public static List<string> Run()
        {
            var failures = new List<string>();

            SidesStandOnTheBlockLine(failures);
            BoundaryClosesWithMitredCorners(failures);
            GatesSitOnTheWalks(failures);
            NeighbouringCellsMeetExactly(failures);
            MapEdgeSideStaysInsideTheTile(failures);
            AvenueSideTakesTheDeeperClearance(failures);
            InteriorEdgesGetNothing(failures);

            return failures;
        }

        // ------------------------------------------------------------------ fixtures

        /// <summary>
        /// A grid that is road everywhere except the given cells, which is the situation a park
        /// is always in: CityGenerator's subdivision leaves every block bounded by streets.
        /// </summary>
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

        static List<Vector2Int> Cells(params Vector2Int[] cells) => new(cells);

        static List<HedgeLayout.Run> Plan(CityGrid grid, List<Vector2Int> cells) =>
            HedgeLayout.Plan(grid, cells, Clearance, MainClearance, MapEdge, GateHalf, Lift);

        /// <summary>Total hedge actually laid, gates excluded. FenceRun drops runs this short.</summary>
        static float Length(List<HedgeLayout.Run> runs)
        {
            var total = 0f;
            foreach (var run in runs)
                if (run.To - run.From > 0f)
                    total += run.To - run.From;
            return total;
        }

        static bool Same(Vector3 a, Vector3 b) =>
            Mathf.Abs(a.x - b.x) < Eps && Mathf.Abs(a.z - b.z) < Eps;

        // ------------------------------------------------------------------ checks

        /// <summary>
        /// The line itself: PAST the cell boundary, on the same sidewalkWidth-from-the-centreline
        /// that BlockBuilder gives every facade. This is the whole point of the change - on the
        /// cell boundary the park's edge sat metres behind the street wall of every block around it.
        /// </summary>
        static void SidesStandOnTheBlockLine(List<string> failures)
        {
            var grid = Roads(3, 3, new Vector2Int(1, 1));
            var line = HedgeLayout.SideOffset(grid, 2, 1, Clearance, MainClearance, MapEdge);
            var expected = CityGrid.CellSize - Clearance;

            if (Mathf.Abs(line - expected) > Eps)
                failures.Add($"SideOffset against a street is {line}, expected {expected} " +
                             "- the hedge is not on the block line the facades use.");

            if (line <= CityGrid.CellSize * 0.5f)
                failures.Add($"SideOffset {line} is inside the cell boundary " +
                             $"{CityGrid.CellSize * 0.5f} - the park is still set back.");
        }

        /// <summary>
        /// The boundary of a one-cell park closes: every run end lands on the rectangle the four
        /// lines describe, each corner is reached by exactly two runs, and the total laid length
        /// is the perimeter less the gates. A notch at a corner fails the length; a doubled
        /// segment on the diagonal fails the corner count.
        /// </summary>
        static void BoundaryClosesWithMitredCorners(List<string> failures)
        {
            var cell = new Vector2Int(1, 1);
            var grid = Roads(3, 3, cell);
            var runs = Plan(grid, Cells(cell));

            if (runs.Count != 8)
            {
                failures.Add($"A one-cell park produced {runs.Count} runs, expected 8 " +
                             "(four sides, each broken by one gate).");
                return;
            }

            var centre = grid.CellToWorld(cell);
            var line = CityGrid.CellSize - Clearance;
            var perimeter = 4f * (2f * line);
            var gates = 4f * 2f * GateHalf;
            var laid = Length(runs);

            if (Mathf.Abs(laid - (perimeter - gates)) > Eps)
                failures.Add($"Hedge length {laid}, expected {perimeter - gates}. Short means a " +
                             "notch at the corners; long means the runs overlap on the diagonal.");

            var corners = new[]
            {
                new Vector3(centre.x + line, Lift, centre.z + line),
                new Vector3(centre.x + line, Lift, centre.z - line),
                new Vector3(centre.x - line, Lift, centre.z + line),
                new Vector3(centre.x - line, Lift, centre.z - line),
            };

            foreach (var corner in corners)
            {
                var hits = 0;
                foreach (var run in runs)
                {
                    if (Same(run.Start, corner)) hits++;
                    if (Same(run.End, corner)) hits++;
                }

                if (hits != 2)
                    failures.Add($"Corner {corner.x},{corner.z} is reached by {hits} run ends, " +
                                 "expected exactly 2 - the two sides do not mitre there.");
            }
        }

        /// <summary>
        /// Every gap in the hedge is centred on the cell centre line, which is where tile-park's
        /// baked walk leaves the tile. The gate used to be placed at the run's midpoint, which is
        /// the same number only while both ends of the side are corners - so this is the check
        /// that catches a gate drifting off its walk on a multi-cell park.
        /// </summary>
        static void GatesSitOnTheWalks(List<string> failures)
        {
            var a = new Vector2Int(1, 1);
            var b = new Vector2Int(2, 1);
            var grid = Roads(4, 3, a, b);
            var runs = Plan(grid, Cells(a, b));

            // Runs come out in pairs: the stretch before each gate, then the stretch after it.
            for (var i = 0; i + 1 < runs.Count; i += 2)
            {
                var before = runs[i];
                var after = runs[i + 1];

                var gate = before.Origin + before.Along * (before.To + GateHalf);
                var far = after.Origin + after.Along * (after.From - GateHalf);

                if (!Same(gate, far))
                {
                    failures.Add($"Gate {i / 2} is not one opening: {gate.x},{gate.z} against " +
                                 $"{far.x},{far.z}.");
                    continue;
                }

                // It must lie on some cell's centre line, i.e. share a coordinate with a cell
                // centre on the axis the walk leaves along.
                var onAWalk = false;
                foreach (var cell in new[] { a, b })
                {
                    var centre = grid.CellToWorld(cell);
                    if (Mathf.Abs(gate.x - centre.x) < Eps || Mathf.Abs(gate.z - centre.z) < Eps)
                        onAWalk = true;
                }

                if (!onAWalk)
                    failures.Add($"Gate at {gate.x},{gate.z} is on no cell's centre line - it " +
                                 "opens into the hedge instead of onto a walk.");
            }
        }

        /// <summary>
        /// Where the park carries on along a side, one cell's run must END exactly where the
        /// next one's STARTS. Both are at the shared cell boundary, and the reason it works is
        /// that neither treats an interior join as a corner to mitre.
        /// </summary>
        static void NeighbouringCellsMeetExactly(List<string> failures)
        {
            var a = new Vector2Int(1, 1);
            var b = new Vector2Int(2, 1);
            var grid = Roads(4, 3, a, b);
            var runs = Plan(grid, Cells(a, b));

            var line = CityGrid.CellSize - Clearance;
            var west = grid.CellToWorld(a);
            var east = grid.CellToWorld(b);

            // Six exposed sides on a 1x2 park, so six gates.
            var perimeter = 2f * ((east.x - west.x + 2f * line) + 2f * line);
            var laid = Length(runs);
            var expected = perimeter - 6f * 2f * GateHalf;

            if (Mathf.Abs(laid - expected) > Eps)
                failures.Add($"Two-cell hedge length {laid}, expected {expected} - the join " +
                             "between the cells has a gap or an overlap in it.");

            // The join itself: the boundary between the two cells, on both long sides.
            var joinX = (west.x + east.x) * 0.5f;
            foreach (var z in new[] { west.z + line, west.z - line })
            {
                var join = new Vector3(joinX, Lift, z);
                var hits = 0;
                foreach (var run in runs)
                {
                    if (Same(run.Start, join)) hits++;
                    if (Same(run.End, join)) hits++;
                }

                if (hits != 2)
                    failures.Add($"The cells meet at {joinX},{z} with {hits} run ends, expected " +
                                 "2 - one run stops short of the other.");
            }
        }

        /// <summary>
        /// A side facing off the map has no road tile to stand on, and BlockRect applies no reach
        /// there either, so the hedge stays just inside the cell as it always did. Getting this
        /// wrong hangs the hedge over the edge of the world.
        /// </summary>
        static void MapEdgeSideStaysInsideTheTile(List<string> failures)
        {
            var cell = new Vector2Int(0, 0);
            var grid = Roads(3, 3, cell);

            var offMap = HedgeLayout.SideOffset(grid, -1, 0, Clearance, MainClearance, MapEdge);
            if (Mathf.Abs(offMap - MapEdge) > Eps)
                failures.Add($"Off-map side offset is {offMap}, expected {MapEdge}.");

            if (offMap >= CityGrid.CellSize * 0.5f)
                failures.Add($"Off-map hedge at {offMap} is outside the cell boundary " +
                             $"{CityGrid.CellSize * 0.5f} - it stands on nothing.");

            // And the run built from it still closes against the two street sides.
            var runs = Plan(grid, Cells(cell));
            if (runs.Count != 8)
                failures.Add($"Map-edge park produced {runs.Count} runs, expected 8.");
        }

        /// <summary>
        /// The dual carriageway's cross-section is wider throughout, so its facades sit at
        /// mainSidewalkWidth and the hedge has to follow - at the ordinary clearance it would
        /// stand in the avenue's outer lane.
        /// </summary>
        static void AvenueSideTakesTheDeeperClearance(List<string> failures)
        {
            var cell = new Vector2Int(1, 1);
            var grid = Roads(3, 3, cell);
            grid.SetMainRoad(2, 1, northSouth: true);

            var avenue = HedgeLayout.SideOffset(grid, 2, 1, Clearance, MainClearance, MapEdge);
            var street = HedgeLayout.SideOffset(grid, 0, 1, Clearance, MainClearance, MapEdge);

            if (Mathf.Abs(avenue - (CityGrid.CellSize - MainClearance)) > Eps)
                failures.Add($"Avenue side offset is {avenue}, expected " +
                             $"{CityGrid.CellSize - MainClearance}.");

            if (avenue >= street)
                failures.Add($"Avenue offset {avenue} is not set back from the street offset " +
                             $"{street} - a hedge there stands in the outer lane.");

            var edge = HedgeLayout.SideOffset(grid, -1, 1, Clearance, MainClearance, MapEdge);
            if (Mathf.Abs(edge - MapEdge) > Eps)
                failures.Add($"Hedge offset against the map edge is {edge}, expected {MapEdge}.");
        }

        /// <summary>
        /// The park is one space, not a set of walled compartments: an edge between two of its
        /// own cells produces no hedge at all.
        /// </summary>
        static void InteriorEdgesGetNothing(List<string> failures)
        {
            var a = new Vector2Int(1, 1);
            var b = new Vector2Int(2, 1);
            var grid = Roads(4, 3, a, b);

            var single = Plan(grid, Cells(a)).Count;
            var pair = Plan(grid, Cells(a, b)).Count;

            // Each cell alone has four exposed sides; together they share one, so the pair has
            // six sides, not eight.
            if (single != 8)
                failures.Add($"One cell of the pair produced {single} runs, expected 8.");

            if (pair != 12)
                failures.Add($"The pair produced {pair} runs, expected 12 - the shared edge is " +
                             "being walled off inside the park.");
        }
    }
}
