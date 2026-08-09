using System.Collections.Generic;
using UnityEngine;
using LivingCity.Data;
using LivingCity.Generation;

namespace LivingCity.Tests
{
    /// <summary>
    /// The park pipeline against REAL generated grids - the acceptance sweep's offline half.
    /// The synthetic ring fixtures in ParkLayoutTests prove the invariants; this proves them on
    /// the geometry CityGenerator actually produces, where a block can hug the map edge, face
    /// the avenue on one side and a dead-end street on another. Every block of every seed is
    /// planned as if it were the park, which is a superset of what any one city builds.
    ///
    /// The CityConfig is a RuntimeHelpers.GetUninitializedObject shell with only the fields
    /// CityGenerator reads - the established recipe for exercising ScriptableObject consumers
    /// in the bare host. Note `is null`, never `== null`: the Unity equality operator reports
    /// an uninitialized ScriptableObject as null while the reference is perfectly live.
    /// </summary>
    public static class ParkRealGridSweepTests
    {
        const float Clearance = 7f * CityGrid.TileScale;
        const float MainClearance = 10f * CityGrid.TileScale;
        const float MapEdge = CityGrid.CellSize * 0.5f - 0.8f;

        public static List<string> Run()
        {
            var failures = new List<string>();

            var config = (CityConfig)System.Runtime.CompilerServices.RuntimeHelpers
                .GetUninitializedObject(typeof(CityConfig));
            typeof(CityConfig).GetField("gridWidth").SetValue(config, 12);
            typeof(CityConfig).GetField("gridHeight").SetValue(config, 10);
            typeof(CityConfig).GetField("minArterialSpacing").SetValue(config, 2);
            typeof(CityConfig).GetField("maxArterialSpacing").SetValue(config, 4);

            for (var seed = 1; seed <= 8; seed++)
            {
                typeof(CityConfig).GetField("seed").SetValue(config, seed);
                var grid = CityGenerator.Generate(config);

                for (var blockId = 0; blockId < grid.BlockCount; blockId++)
                {
                    var cells = new List<Vector2Int>(grid.CellsInBlock(blockId));
                    if (cells.Count == 0)
                        continue;

                    ParkLayout.Plan plan;
                    List<ParkNavPlan.CellPaths> nav;
                    try
                    {
                        plan = ParkLayout.ForBlock(grid, cells, Clearance, MainClearance,
                            MapEdge, null, 4, seed, blockId, ParkLayout.Tuning.Default);
                        nav = ParkNavPlan.ForPlan(plan, grid, cells, null);
                    }
                    catch (System.Exception e)
                    {
                        failures.Add($"seed {seed} block {blockId}: threw {e.GetType().Name}: {e.Message}");
                        continue;
                    }

                    Check(failures, seed, blockId, grid, cells, plan, nav);
                    if (failures.Count > 8)
                        return failures;   // one broken invariant floods; the first few tell the story
                }
            }

            return failures;
        }

        static void Check(
            List<string> failures, int seed, int blockId, CityGrid grid,
            List<Vector2Int> cells, ParkLayout.Plan plan, List<ParkNavPlan.CellPaths> nav)
        {
            var where = $"seed {seed} block {blockId} ({cells.Count} cells)";

            // Any block with a road beside it must open at least one entrance.
            var facesRoad = false;
            foreach (var cell in cells)
            {
                if (grid.IsRoad(cell.x + 1, cell.y) || grid.IsRoad(cell.x - 1, cell.y)
                    || grid.IsRoad(cell.x, cell.y + 1) || grid.IsRoad(cell.x, cell.y - 1))
                    facesRoad = true;
            }
            if (facesRoad && plan.Entrances.Count == 0)
            {
                failures.Add($"{where}: road-facing park planned no entrances");
                return;
            }

            // The overlap sweep's promise, on real ground.
            for (var i = 0; i < plan.Stations.Count; i++)
            for (var j = i + 1; j < plan.Stations.Count; j++)
            {
                var a = plan.Stations[i];
                var b = plan.Stations[j];
                if ((a.Pos - b.Pos).magnitude < a.Radius + b.Radius - 1e-3f)
                {
                    failures.Add($"{where}: {a.Kind} overlaps {b.Kind}");
                    return;
                }
            }

            // Every nav end must be linkable - the stranded-walker check.
            var inBlock = new HashSet<Vector2Int>(cells);
            foreach (var entry in nav)
            foreach (var path in entry.Paths)
            foreach (var end in new[] { path[0], path[^1] })
            {
                var linkable = false;
                foreach (var entrance in plan.Entrances)
                    if ((entrance.Anchor - end).magnitude < 0.05f)
                        linkable = true;
                if (!linkable)
                    foreach (var cell in cells)
                    {
                        var half = CityGrid.CellSize * 0.5f;
                        var cx = cell.x * CityGrid.CellSize;
                        var cz = cell.y * CityGrid.CellSize;
                        if (inBlock.Contains(cell + new Vector2Int(1, 0))
                            && Mathf.Abs(end.x - (cx + half)) < 1e-3f)
                            linkable = true;
                        if (inBlock.Contains(cell + new Vector2Int(0, 1))
                            && Mathf.Abs(end.y - (cz + half)) < 1e-3f)
                            linkable = true;
                    }
                if (!linkable)
                {
                    failures.Add(
                        $"{where}: nav path ends unlinkable at ({end.x:0.#},{end.y:0.#})");
                    return;
                }
            }
        }
    }
}
