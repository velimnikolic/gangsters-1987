using System.Collections.Generic;
using UnityEngine;
using LivingCity.Generation;

namespace LivingCity.Tests
{
    /// <summary>
    /// ParkNavPlan's contract - the invariants that make the park's pedestrian graph link. Same
    /// discipline as ParkLayoutTests: no UnityEngine.Object, no native Bounds calls, failures
    /// as data for the bare .NET host.
    ///
    /// The invariants mirror the pack's linking rules exactly, because they are what the scene
    /// cannot check for itself until a walker strands: paths hand over only at coincident
    /// points (node 0 of the next path is skipped in playback), and only across tiles.
    /// </summary>
    public static class ParkNavPlanTests
    {
        const float Clearance = 7f * CityGrid.TileScale;
        const float MainClearance = 10f * CityGrid.TileScale;
        const float MapEdge = CityGrid.CellSize * 0.5f - 0.8f;
        const float Eps = 1e-3f;

        public static List<string> Run()
        {
            var failures = new List<string>();
            EveryPathIsWalkable(failures);
            EveryEndIsLinkable(failures);
            EveryPathHasItsReverse(failures);
            EverySpineIsCovered(failures);
            BoundaryHandoversCoincide(failures);
            SamePlanSamePaths(failures);
            return failures;
        }

        // ------------------------------------------------------------------ fixtures

        static CityGrid Roads(int width, int height, params Vector2Int[] park)
        {
            var grid = new CityGrid(width, height);
            for (var x = 0; x < width; x++)
            for (var z = 0; z < height; z++)
                grid[x, z] = CellType.Road;
            foreach (var cell in park)
                grid[cell.x, cell.y] = CellType.Block;
            return grid;
        }

        static IEnumerable<(CityGrid Grid, List<Vector2Int> Cells, ParkLayout.Plan Plan,
            List<ParkNavPlan.CellPaths> Paths)> Sweep()
        {
            var shapes = new[]
            {
                new[] { new Vector2Int(2, 2) },
                new[] { new Vector2Int(2, 2), new Vector2Int(3, 2) },
                new[]
                {
                    new Vector2Int(2, 2), new Vector2Int(3, 2),
                    new Vector2Int(2, 3), new Vector2Int(3, 3),
                },
            };
            for (var seed = 1; seed <= 6; seed++)
            for (var shape = 0; shape < shapes.Length; shape++)
            {
                var cells = new List<Vector2Int>(shapes[shape]);
                var grid = Roads(6, 6, shapes[shape]);
                var plan = ParkLayout.ForBlock(grid, cells, Clearance, MainClearance, MapEdge,
                    null, 4, seed, seed * 5 + shape, ParkLayout.Tuning.Default);
                var paths = ParkNavPlan.ForPlan(plan, grid, cells, null);
                yield return (grid, cells, plan, paths);
            }
        }

        static bool NearAnyAnchor(ParkLayout.Plan plan, Vector2 p)
        {
            foreach (var entrance in plan.Entrances)
                if ((entrance.Anchor - p).magnitude < 0.05f)
                    return true;
            return false;
        }

        static bool OnInternalBoundary(List<Vector2Int> cells, Vector2 p)
        {
            var inBlock = new HashSet<Vector2Int>(cells);
            foreach (var cell in cells)
            {
                if (inBlock.Contains(cell + new Vector2Int(1, 0)))
                {
                    var x = cell.x * CityGrid.CellSize + CityGrid.CellSize * 0.5f;
                    if (Mathf.Abs(p.x - x) < Eps)
                        return true;
                }
                if (inBlock.Contains(cell + new Vector2Int(0, 1)))
                {
                    var z = cell.y * CityGrid.CellSize + CityGrid.CellSize * 0.5f;
                    if (Mathf.Abs(p.y - z) < Eps)
                        return true;
                }
            }
            return false;
        }

        // ------------------------------------------------------------------ tests

        static void EveryPathIsWalkable(List<string> failures)
        {
            foreach (var (_, _, _, cellPaths) in Sweep())
            foreach (var entry in cellPaths)
            foreach (var path in entry.Paths)
            {
                if (path.Length < 2)
                {
                    failures.Add("walkable: a path with fewer than 2 nodes");
                    return;
                }
                for (var i = 1; i < path.Length; i++)
                {
                    var spacing = (path[i] - path[i - 1]).magnitude;
                    if (spacing > ParkLayout.SampleStep + 1.6f)
                    {
                        failures.Add($"walkable: node gap {spacing:0.##} beyond the step");
                        return;
                    }
                    var interior = i > 1 && i < path.Length - 1;
                    if (interior && spacing < 0.9f)
                    {
                        failures.Add($"walkable: interior node spacing {spacing:0.##} under " +
                                     "the follower's arrival radius");
                        return;
                    }
                }
            }
        }

        static void EveryEndIsLinkable(List<string> failures)
        {
            foreach (var (_, cells, plan, cellPaths) in Sweep())
            foreach (var entry in cellPaths)
            foreach (var path in entry.Paths)
            foreach (var end in new[] { path[0], path[^1] })
            {
                if (NearAnyAnchor(plan, end))
                    continue;
                if (OnInternalBoundary(cells, end))
                    continue;
                failures.Add(
                    $"linkable: a path ends at ({end.x:0.#},{end.y:0.#}), which is neither a "
                    + "road anchor nor an internal cell boundary - a stranded dead end");
                return;
            }
        }

        static void EveryPathHasItsReverse(List<string> failures)
        {
            foreach (var (_, _, _, cellPaths) in Sweep())
            foreach (var entry in cellPaths)
            foreach (var path in entry.Paths)
            {
                var found = false;
                foreach (var other in entry.Paths)
                {
                    if (other.Length != path.Length)
                        continue;
                    var reversed = true;
                    for (var i = 0; i < path.Length && reversed; i++)
                        reversed = (other[i] - path[path.Length - 1 - i]).sqrMagnitude < 0.01f;
                    if (!reversed)
                        continue;
                    found = true;
                    break;
                }
                if (found)
                    continue;
                failures.Add("reverse: a directed path with no return direction");
                return;
            }
        }

        static void EverySpineIsCovered(List<string> failures)
        {
            foreach (var (_, _, plan, cellPaths) in Sweep())
            {
                if (plan.Entrances.Count == 0)
                    continue;
                foreach (var spine in plan.Spines)
                {
                    if (spine.Points == null || spine.Points.Length < 2)
                        continue;
                    var covered = 0;
                    foreach (var point in spine.Points)
                    {
                        var hit = false;
                        foreach (var entry in cellPaths)
                        {
                            foreach (var path in entry.Paths)
                                if (ParkLayout.DistanceToPolyline(point, path) < 1.8f)
                                {
                                    hit = true;
                                    break;
                                }
                            if (hit)
                                break;
                        }
                        if (hit)
                            covered++;
                    }
                    if (covered < spine.Points.Length * 0.5f)
                    {
                        failures.Add(
                            $"coverage: a {spine.Kind} walk only {covered}/{spine.Points.Length} "
                            + "covered by nav - pedestrians never take it");
                        return;
                    }
                }
            }
        }

        static void BoundaryHandoversCoincide(List<string> failures)
        {
            foreach (var (_, cells, plan, cellPaths) in Sweep())
            {
                if (cells.Count < 2)
                    continue;
                foreach (var entry in cellPaths)
                foreach (var path in entry.Paths)
                {
                    var end = path[^1];
                    if (NearAnyAnchor(plan, end) || !OnInternalBoundary(cells, end))
                        continue;

                    // The pack links my LAST node to a NEIGHBOUR tile's FIRST node within
                    // 1.95m; the plan promises coincidence, which is stricter and what the
                    // node-0-skip playback needs.
                    var handedOver = false;
                    foreach (var other in cellPaths)
                    {
                        if (other.Cell == entry.Cell)
                            continue;
                        foreach (var continuation in other.Paths)
                            if ((continuation[0] - end).sqrMagnitude < 0.01f)
                            {
                                handedOver = true;
                                break;
                            }
                        if (handedOver)
                            break;
                    }
                    if (!handedOver)
                    {
                        failures.Add(
                            $"handover: a path ends on the boundary at ({end.x:0.#},{end.y:0.#}) "
                            + "and no neighbouring cell starts a path there");
                        return;
                    }
                }
            }
        }

        static void SamePlanSamePaths(List<string> failures)
        {
            var cells = new List<Vector2Int> { new(2, 2), new(3, 2) };
            var grid = Roads(6, 5, cells.ToArray());
            var plan = ParkLayout.ForBlock(grid, cells, Clearance, MainClearance, MapEdge,
                null, 4, 9, 13, ParkLayout.Tuning.Default);
            var first = ParkNavPlan.ForPlan(plan, grid, cells, null);
            var second = ParkNavPlan.ForPlan(plan, grid, cells, null);

            if (first.Count != second.Count)
            {
                failures.Add("determinism: cell count differs between identical calls");
                return;
            }
            for (var c = 0; c < first.Count; c++)
            {
                if (first[c].Paths.Count != second[c].Paths.Count)
                {
                    failures.Add("determinism: path count differs between identical calls");
                    return;
                }
                for (var p = 0; p < first[c].Paths.Count; p++)
                for (var i = 0; i < first[c].Paths[p].Length; i++)
                    if ((first[c].Paths[p][i] - second[c].Paths[p][i]).sqrMagnitude > 0f)
                    {
                        failures.Add("determinism: a node moved between identical calls");
                        return;
                    }
            }
        }
    }
}
