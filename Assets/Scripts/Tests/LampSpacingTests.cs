using System.Collections.Generic;
using UnityEngine;
using LivingCity.Data;
using LivingCity.Generation;

namespace LivingCity.Tests
{
    /// <summary>
    /// The one property the street lamps exist to have: an even pitch.
    ///
    /// Lamps used to be gated by (x + z) % 2 - a checkerboard over the whole MAP, while
    /// CityGenerator.Subdivide is a BSP that gives blocks frontages of one to three cells
    /// starting at arbitrary coordinates. The phase therefore landed differently on every block:
    /// a one-cell frontage got a lamp or got none, a three-cell frontage got two or got one.
    /// Nothing about that is visible in a screenshot of a single street, which is exactly why it
    /// survived - it only reads as wrong across a whole city, and only as "looks random".
    ///
    /// So the check has to be over real generated grids rather than a hand-built fixture, and it
    /// has to be about the SET of lamps rather than any one of them.
    ///
    /// Same discipline as BankFrontageTests and HedgeLayoutTests - no UnityEngine.Object anywhere,
    /// failures returned as data rather than logged - so a bare .NET host can call Run() by
    /// reflection with no Editor. StreetPropPlacer.LampSites is pure for this reason: it takes a
    /// car-park predicate instead of the PrefabDatabase, because a test cannot build a
    /// ScriptableObject.
    /// </summary>
    public static class LampSpacingTests
    {
        const float Eps = 1e-3f;

        /// <summary>How many seeds each sweep walks. Layout varies a lot seed to seed.</summary>
        const int Seeds = 200;

        /// <summary>Runs every check. An empty list means everything passed.</summary>
        public static List<string> Run()
        {
            var failures = new List<string>();

            EveryDressedEdgeIsLitAtBothSeams(failures);
            NoSeamIsLitTwice(failures);
            EveryLampSitsOnASeam(failures);
            JunctionsAreNotDarkHoles(failures);
            NoLampStandsOnTheMapOutline(failures);
            LampsClearTheCarriagewayAndTheBuildingLine(failures);
            PlacementIsDeterministic(failures);

            return failures;
        }

        // ------------------------------------------------------------------ fixtures

        /// <summary>
        /// A grid from the real generator. CityConfig is a ScriptableObject and cannot be newed
        /// outside Unity, so it is allocated uninitialised and its plain fields set directly -
        /// CityGenerator.Generate reads nothing else.
        ///
        /// Note the guard style: such an object reports itself as null through UnityEngine's
        /// overloaded ==, so anything checking it must use `is null`. Nothing here does.
        /// </summary>
        static CityGrid Grid(int seed, int width = 16, int height = 7)
        {
            var config = (CityConfig)System.Runtime.CompilerServices.RuntimeHelpers
                .GetUninitializedObject(typeof(CityConfig));

            config.gridWidth = width;
            config.gridHeight = height;
            config.minArterialSpacing = 2;
            config.maxArterialSpacing = 4;
            // Set explicitly - GetUninitializedObject skips field initializers, and 0 would
            // mean a city with no boulevard and no main-verge coverage in the sweep.
            config.minBoulevards = 1;
            config.maxBoulevards = 2;
            config.seed = seed;

            return CityGenerator.Generate(config);
        }

        /// <summary>No car parks. Zoning needs a PrefabDatabase, which a bare host cannot build.</summary>
        static readonly System.Func<int, int, bool> NoCarParks = (_, _) => false;

        /// <summary>The four sides in StreetPropPlacer's own order: N, E, S, W.</summary>
        static readonly Vector2Int[] Steps =
        {
            new(0, 1), new(1, 0), new(0, -1), new(-1, 0),
        };

        static Vector3 Outward(int side) => side switch
        {
            0 => Vector3.forward,
            1 => Vector3.right,
            2 => Vector3.back,
            _ => Vector3.left,
        };

        static Vector3 Along(int side)
        {
            var outward = Outward(side);
            return new Vector3(-outward.z, 0f, outward.x);
        }

        /// <summary>
        /// The placer's own dressing rule, restated so the tests assert against the SPECIFICATION
        /// rather than against whatever the implementation currently does.
        /// </summary>
        static bool Dressed(CityGrid grid, Vector2Int cell, int side)
        {
            var n = cell + Steps[side];
            return !grid.IsRoad(n.x, n.y) && grid.InBounds(n.x, n.y);
        }

        static float Verge(CityGrid grid, Vector2Int cell) =>
            grid.IsMainRoad(cell.x, cell.y)
                ? 8.5f * CityGrid.TileScale
                : 5.5f * CityGrid.TileScale;

        static List<StreetPropPlacer.LampSite> Lamps(CityGrid grid) =>
            StreetPropPlacer.LampSites(grid, NoCarParks);

        /// <summary>Positions as a set, at millimetre resolution - these are lattice points.</summary>
        static HashSet<Vector3Int> Key(List<StreetPropPlacer.LampSite> sites)
        {
            var keys = new HashSet<Vector3Int>();
            foreach (var site in sites)
                keys.Add(Key(site.Position));
            return keys;
        }

        static Vector3Int Key(Vector3 p) => new(
            Mathf.RoundToInt(p.x * 1000f),
            Mathf.RoundToInt(p.y * 1000f),
            Mathf.RoundToInt(p.z * 1000f));

        // ------------------------------------------------------------------ the spacing rule

        /// <summary>
        /// THE test. Every edge that faces a block carries a lamp at BOTH of its seams, on every
        /// seed. That is what "evenly spaced" reduces to: the pitch cannot depend on where a
        /// frontage happens to start, because every boundary is lit.
        ///
        /// Under the old parity this fails on the first seed and by a wide margin - roughly two
        /// edges in five were unlit, and which ones depended on the block's absolute position.
        /// </summary>
        static void EveryDressedEdgeIsLitAtBothSeams(List<string> failures)
        {
            for (var seed = 0; seed < Seeds; seed++)
            {
                var grid = Grid(seed);
                var lit = Key(Lamps(grid));

                foreach (var cell in grid.RoadCells())
                for (var side = 0; side < 4; side++)
                {
                    if (!Dressed(grid, cell, side)) continue;

                    var centre = grid.CellToWorld(cell);
                    var outward = Outward(side);
                    var along = Along(side);
                    var verge = grid.IsMainRoad(cell.x, cell.y)
                        ? 8.5f * CityGrid.TileScale
                        : 5.5f * CityGrid.TileScale;

                    var step = new Vector2Int(-Steps[side].y, Steps[side].x);

                    for (var end = -1; end <= 1; end += 2)
                    {
                        // The map outline is the one seam that is deliberately dark - a lamp
                        // there would hang a lantern off the end of the road tile.
                        var far = cell + step * end;
                        if (!grid.InBounds(far.x, far.y)) continue;

                        var seam = centre + outward * verge
                                          + along * (CityGrid.CellSize * 0.5f * end);

                        if (lit.Contains(Key(seam))) continue;

                        failures.Add(
                            $"seed {seed}: cell ({cell.x},{cell.y}) side {side} end {end} " +
                            $"faces a block but its seam at {seam} has no lamp");
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// A seam belongs to two cells and both reach it, so the dedupe is doing real work. Two
        /// lamps in one spot would z-fight and, less visibly, double that spot's light.
        /// </summary>
        static void NoSeamIsLitTwice(List<string> failures)
        {
            for (var seed = 0; seed < Seeds; seed++)
            {
                var sites = Lamps(Grid(seed));
                var seen = new HashSet<Vector3Int>();

                foreach (var site in sites)
                    if (!seen.Add(Key(site.Position)))
                    {
                        failures.Add($"seed {seed}: two lamps at {site.Position}");
                        return;
                    }
            }
        }

        /// <summary>
        /// The converse of the first test: no lamp anywhere OFF a seam. Together the two pin the
        /// set exactly, so a future change cannot satisfy one by over-placing.
        ///
        /// A seam is a half-cell offset from a centre, so the along-coordinate divided by CellSize
        /// must land on a half-integer.
        /// </summary>
        static void EveryLampSitsOnASeam(List<string> failures)
        {
            for (var seed = 0; seed < Seeds; seed++)
            {
                var grid = Grid(seed);

                foreach (var site in Lamps(grid))
                {
                    // One axis is the verge (off-lattice), the other is the run. The run axis is
                    // the one whose distance to a half-cell multiple is zero.
                    var x = site.Position.x / CityGrid.CellSize;
                    var z = site.Position.z / CityGrid.CellSize;

                    if (IsHalfInteger(x) || IsHalfInteger(z)) continue;

                    failures.Add(
                        $"seed {seed}: lamp at {site.Position} is on no cell seam " +
                        $"(x/cell {x}, z/cell {z})");
                    return;
                }
            }
        }

        static bool IsHalfInteger(float v)
        {
            var doubled = v * 2f;
            return Mathf.Abs(doubled - Mathf.Round(doubled)) < Eps
                   && Mathf.Abs(v - Mathf.Round(v)) > 0.25f;
        }

        /// <summary>
        /// The gap the old scheme could not close even without the parity bug. A junction cell has
        /// road on all four sides, so it is never dressed and never carries a lamp of its own.
        /// With lamps at cell CENTRES that leaves two cells of darkness across every crossing -
        /// double the pitch, at the one place a city is normally brightest.
        ///
        /// On seams it closes itself: the runs either side of the junction each end ON their
        /// boundary with it, which puts those two lamps exactly one cell apart.
        /// </summary>
        static void JunctionsAreNotDarkHoles(List<string> failures)
        {
            var checkedAny = false;

            for (var seed = 0; seed < Seeds; seed++)
            {
                var grid = Grid(seed);
                var lit = Key(Lamps(grid));

                foreach (var cell in grid.RoadCells())
                {
                    var mask = grid.GetNeighborMask(cell.x, cell.y);
                    var northSouth = (mask & (Sides.North | Sides.South)) != 0;
                    var eastWest = (mask & (Sides.East | Sides.West)) != 0;

                    if (!northSouth || !eastWest) continue;   // not a junction

                    for (var side = 0; side < 4; side++)
                    {
                        // Only the case that used to go dark: the junction itself carries no
                        // lamp on this side, but the frontage picks up again either side of it.
                        if (Dressed(grid, cell, side)) continue;

                        var along = Along(side);
                        var step = new Vector2Int(-Steps[side].y, Steps[side].x);
                        var before = cell - step;
                        var after = cell + step;

                        if (!grid.IsRoad(before.x, before.y) || !Dressed(grid, before, side)) continue;
                        if (!grid.IsRoad(after.x, after.y) || !Dressed(grid, after, side)) continue;

                        var outward = Outward(side);
                        var half = CityGrid.CellSize * 0.5f;

                        // Each of these seams has exactly one claimant - the junction is not
                        // dressed here - so each takes its own cell's verge.
                        var near = grid.CellToWorld(before) + outward * Verge(grid, before)
                                                            + along * half;
                        var far = grid.CellToWorld(after) + outward * Verge(grid, after)
                                                          - along * half;

                        checkedAny = true;

                        if (!lit.Contains(Key(near)) || !lit.Contains(Key(far)))
                        {
                            failures.Add(
                                $"seed {seed}: junction ({cell.x},{cell.y}) side {side} has a " +
                                "frontage either side of it but one flanking seam is unlit");
                            return;
                        }

                        // The whole point: one cell apart along the street, not two.
                        var gap = Mathf.Abs(Vector3.Dot(far - near, along));
                        if (Mathf.Abs(gap - CityGrid.CellSize) < Eps) continue;

                        failures.Add(
                            $"seed {seed}: junction ({cell.x},{cell.y}) side {side} leaves a " +
                            $"{gap}m gap, not one cell ({CityGrid.CellSize}m)");
                        return;
                    }
                }
            }

            if (!checkedAny)
                failures.Add("no junction with a continuing frontage was found in any seed - " +
                             "the junction check asserted nothing");
        }

        /// <summary>
        /// The one seam deliberately left dark. A road tile spans exactly half a cell either side
        /// of its centre, so the seam at the edge of the map IS the tile's outer edge - and the
        /// double lamp's lanterns hang at local +/-3.10 along the street, which would put one of
        /// them and its pool over nothing at all. Every street that runs to the boundary would
        /// show it, so the miss is worth asserting rather than leaving to be re-noticed.
        /// </summary>
        static void NoLampStandsOnTheMapOutline(List<string> failures)
        {
            const float Half = CityGrid.CellSize * 0.5f;

            for (var seed = 0; seed < Seeds; seed++)
            {
                var grid = Grid(seed);

                var xMax = (grid.Width - 1) * CityGrid.CellSize + Half;
                var zMax = (grid.Height - 1) * CityGrid.CellSize + Half;

                foreach (var site in Lamps(grid))
                {
                    var p = site.Position;

                    if (p.x > -Half + Eps && p.x < xMax - Eps
                                          && p.z > -Half + Eps && p.z < zMax - Eps) continue;

                    failures.Add(
                        $"seed {seed}: lamp at {p} stands on the map outline " +
                        $"(x 0..{xMax}, z 0..{zMax} plus a half cell) - one lantern hangs off " +
                        "the end of the road tile");
                    return;
                }
            }
        }

        /// <summary>
        /// A lamp has to stand between the kerb and the wall: outside the carriageway it lights,
        /// inside the building line. The boulevard's cross-section is wider throughout, so its
        /// lamps stand further out - and a seam where a boulevard meets an ordinary street must
        /// not drag the ordinary street's lamp out past its own building line.
        /// </summary>
        static void LampsClearTheCarriagewayAndTheBuildingLine(List<string> failures)
        {
            const float street = 5.5f * CityGrid.TileScale;
            const float boulevard = 8.5f * CityGrid.TileScale;

            for (var seed = 0; seed < Seeds; seed++)
            {
                var grid = Grid(seed);

                foreach (var site in Lamps(grid))
                {
                    // One axis is the seam (a half cell, 19.5) and the other is the verge, which
                    // is always the SMALLER of the two - so min picks the verge without having
                    // to know which way the street runs.
                    var dx = Offset(site.Position.x);
                    var dz = Offset(site.Position.z);
                    var verge = Mathf.Min(dx, dz);

                    if (Mathf.Abs(verge - street) < Eps) continue;
                    if (Mathf.Abs(verge - boulevard) < Eps) continue;

                    failures.Add(
                        $"seed {seed}: lamp at {site.Position} stands {verge} from the centreline, " +
                        $"which is neither the street line ({street}) nor the boulevard's ({boulevard})");
                    return;
                }
            }

            static float Offset(float world)
            {
                var cells = world / CityGrid.CellSize;
                return Mathf.Abs(cells - Mathf.Round(cells)) * CityGrid.CellSize;
            }
        }

        /// <summary>
        /// Same grid, same lamps. LampSites walks a HashSet, and a set that ever leaked into the
        /// ORDER of the output would make the city's lighting depend on enumeration order.
        /// </summary>
        static void PlacementIsDeterministic(List<string> failures)
        {
            for (var seed = 0; seed < 20; seed++)
            {
                var first = Lamps(Grid(seed));
                var second = Lamps(Grid(seed));

                if (first.Count != second.Count)
                {
                    failures.Add($"seed {seed}: {first.Count} lamps then {second.Count}");
                    return;
                }

                for (var i = 0; i < first.Count; i++)
                {
                    if (Key(first[i].Position) == Key(second[i].Position)
                        && Mathf.Abs(first[i].Yaw - second[i].Yaw) < Eps) continue;

                    failures.Add(
                        $"seed {seed}: lamp {i} moved between runs - " +
                        $"{first[i].Position}/{first[i].Yaw} then {second[i].Position}/{second[i].Yaw}");
                    return;
                }
            }
        }
    }
}
