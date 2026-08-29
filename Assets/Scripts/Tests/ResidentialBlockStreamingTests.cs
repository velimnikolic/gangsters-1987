using System.Collections.Generic;
using RoadDemo;
using UnityEngine;

namespace LivingCity.Tests
{
    /// <summary>Pure contracts behind streamed residential views. Nothing here enters Play
    /// mode or creates a UnityEngine.Object, so the same checks can run from the editor CLI.</summary>
    public static class ResidentialBlockStreamingTests
    {
        static CoreLayout.Plan verifiedCorePlan;

        public static List<string> Run()
        {
            var failures = new List<string>();
            BakedCoreCatalogMatchesTheDealer(failures);
            SamePlanHasSameContentKey(failures);
            AChangedPlanChangesTheHash(failures);
            InvalidationChangesOnlyTheRevision(failures);
            ModelForwardsRecipeChanges(failures);
            ViewportIntersectionAndPadding(failures);
            FrustumIntersectionAndHeight(failures);
            FallbackGeometryIsOpaqueResidential(failures);
            CameraPitchPolicy(failures);
            CompactCoreInfillHasBuildings(failures);
            ShallowCoreInfillIsApartmentFrontage(failures);
            CoreBlocksHaveStableNamesAndQuarters(failures);
            TerritoryStateIsSeparateFromThePlan(failures);
            return failures;
        }

        static void BakedCoreCatalogMatchesTheDealer(List<string> failures)
        {
            var blocks = CoreBlockCatalog.CreateBlocks();
            if (blocks.Count != CoreLayout.Blocks.Length)
                failures.Add($"catalog count {blocks.Count}, dealer count {CoreLayout.Blocks.Length}");
            for (int i = 0; i < Mathf.Min(blocks.Count, CoreLayout.Blocks.Length); i++)
            {
                var block = blocks[i];
                if (block.Name != CoreLayout.Blocks[i].Prefab)
                    failures.Add($"catalog[{i}] is {block.Name}, dealer wants {CoreLayout.Blocks[i].Prefab}");
                if (block.CW0 < 1 || block.CD0 < 1 || block.Mask0 == null || block.Cells < 1)
                    failures.Add($"{block.Name} has no usable baked footprint");
            }
        }

        static ResidentialLot.Plan Plan(int seed) => ResidentialLot.Roll(14, 15, seed, 0);

        static void SamePlanHasSameContentKey(List<string> failures)
        {
            var box = new Rect(10f, 20f, 70f, 75f);
            var a = new ResidentialBlockRecipe("a", "res-a", box, Plan(91), 91);
            var b = new ResidentialBlockRecipe("b", "res-b", box, Plan(91), 91);
            if (a.PlanHash != b.PlanHash || a.ContentKey != b.ContentKey)
                failures.Add("the same residential plan produced a different cache key");
        }

        static void AChangedPlanChangesTheHash(List<string> failures)
        {
            var box = new Rect(10f, 20f, 70f, 75f);
            var a = new ResidentialBlockRecipe("a", "res-a", box, Plan(91), 91);
            var changed = Plan(91);
            changed.Ground[3, 3] = changed.Ground[3, 3] == ResidentialLot.Use.Paved
                ? ResidentialLot.Use.Yard
                : ResidentialLot.Use.Paved;
            var b = new ResidentialBlockRecipe("b", "res-b", box, changed, 91);
            if (a.PlanHash == b.PlanHash)
                failures.Add("a changed residential decision shared the original plan hash");
        }

        static void InvalidationChangesOnlyTheRevision(List<string> failures)
        {
            var recipe = new ResidentialBlockRecipe("a", "res-a", new Rect(0, 0, 70, 75), Plan(91), 91);
            ulong plan = recipe.PlanHash, content = recipe.ContentKey;
            recipe.Invalidate();
            if (recipe.PlanHash != plan) failures.Add("visual invalidation changed immutable plan hash");
            if (recipe.ContentKey == content) failures.Add("visual invalidation did not invalidate cached content");
        }

        static void ModelForwardsRecipeChanges(List<string> failures)
        {
            var model = new ResidentialBlockModel();
            var recipe = new ResidentialBlockRecipe("a", "res-a", new Rect(0, 0, 70, 75), Plan(91), 91);
            int changes = 0;
            model.Changed += (_, __) => changes++;
            model.Add(recipe);
            recipe.Invalidate();
            model.Remove(recipe.Id);
            if (changes != 3) failures.Add($"model forwarded {changes} changes, expected add/invalidate/remove");
        }

        static void ViewportIntersectionAndPadding(List<string> failures)
        {
            var view = new[]
            {
                new Vector2(0, 0), new Vector2(100, 0),
                new Vector2(100, 80), new Vector2(0, 80),
            };
            if (!CityBlockVisibility.Intersects(view, new Rect(90, 70, 20, 20)))
                failures.Add("a block crossing the viewport corner was culled");
            if (CityBlockVisibility.Intersects(view, new Rect(111, 20, 10, 10)))
                failures.Add("a block outside the viewport was retained without prefetch");
            if (!CityBlockVisibility.Intersects(view, new Rect(111, 20, 10, 10), 12f))
                failures.Add("prefetch padding did not admit a near-edge block");
            if (CityBlockVisibility.Intersects(view, new Rect(150, 100, 10, 10), 12f))
                failures.Add("prefetch padding admitted a remote block");
        }

        static void FrustumIntersectionAndHeight(List<string> failures)
        {
            // An axis-aligned test frustum: x -10..10, y 0..20, z 0..100.
            var frustum = new[]
            {
                new Plane(Vector3.right, new Vector3(-10, 0, 0)),
                new Plane(Vector3.left, new Vector3(10, 0, 0)),
                new Plane(Vector3.up, Vector3.zero),
                new Plane(Vector3.down, new Vector3(0, 20, 0)),
                new Plane(Vector3.forward, Vector3.zero),
                new Plane(Vector3.back, new Vector3(0, 0, 100)),
            };
            if (!CityBlockVisibility.Intersects(frustum, new Rect(-5, 80, 10, 10), 0f, 12f))
                failures.Add("a distant block inside a tilted-camera frustum was culled");
            if (CityBlockVisibility.Intersects(frustum, new Rect(25, 80, 10, 10), 0f, 12f))
                failures.Add("a block outside the camera frustum was retained");

            var recipe = new ResidentialBlockRecipe(
                "height", "height", new Rect(0, 0, 70, 75), Plan(91), 91);
            if (recipe.VisualHeight < 24f)
                failures.Add("recipe visual height does not cover residential dressing");
        }

        static void FallbackGeometryIsOpaqueResidential(List<string> failures)
        {
            var bounds = new Rect(10f, 20f, 70f, 75f);
            var recipe = new ResidentialBlockRecipe("fallback", "fallback", bounds, Plan(91), 91);
            var fallback = ResidentialFallbackGeometry.Describe(recipe);
            if (fallback.Id != recipe.Id || fallback.LocalBounds != bounds)
                failures.Add("residential fallback lost its recipe identity or complete ground bounds");
            if (fallback.BuildingMasses.Count == 0)
                failures.Add("residential fallback contains no building mass");
            if (ResidentialFallbackGeometry.GroundY <= RiverBridge.WaterY)
                failures.Add("residential fallback ground lies on/below the city water plane");
            for (int i = 0; i < fallback.BuildingMasses.Count; i++)
            {
                var mass = fallback.BuildingMasses[i];
                if (mass.Height <= 0f || mass.LocalFootprint.xMin < 0f ||
                    mass.LocalFootprint.yMin < 0f || mass.LocalFootprint.xMax > bounds.width ||
                    mass.LocalFootprint.yMax > bounds.height)
                    failures.Add("residential fallback produced an invalid building mass");
            }
        }

        static void CameraPitchPolicy(List<string> failures)
        {
            Vector2 locked = CityViewConfig.ResolvePitchRange(55f, 0f);
            if (!Mathf.Approximately(locked.x, 55f) || !Mathf.Approximately(locked.y, 55f))
                failures.Add("zero pitch freedom did not lock the camera at its configured angle");

            Vector2 small = CityViewConfig.ResolvePitchRange(55f, 2f);
            if (!Mathf.Approximately(small.x, 53f) || !Mathf.Approximately(small.y, 57f))
                failures.Add("small pitch freedom did not produce a symmetric pitch range");
        }

        static void CompactCoreInfillHasBuildings(List<string> failures)
        {
            // These are the shallow former parking rectangles dealt by Core's outer
            // quarters. They are real 30-45 m urban plots, not empty map blocks.
            var sizes = new[]
            {
                new Vector2Int(17, 6),
                new Vector2Int(17, 7),
                new Vector2Int(7, 9),
                new Vector2Int(10, 7),
            };
            for (int n = 0; n < sizes.Length; n++)
                for (int seed = 1987; seed < 1997; seed++)
                {
                    var size = sizes[n];
                    var plan = ResidentialLot.Roll(size.x, size.y, seed, seed & 3);
                    if (plan.Spots.Count > 0 && plan.Clean) continue;
                    failures.Add($"Core residential infill {size.x}x{size.y} seed {seed} " +
                                 $"has {plan.Spots.Count} building(s): {string.Join("; ", plan.Faults)}");
                    break;
                }
        }

        static void ShallowCoreInfillIsApartmentFrontage(List<string> failures)
        {
            // The five seed-1987 hardstandings are 5-10 m deep. They cannot carry the
            // ordinary ten-metre pavement ring, but every cell must still become housing.
            foreach (var shape in new[]
            {
                (W: 6, D: 1, Side: 2),
                (W: 11, D: 1, Side: 0),
                (W: 12, D: 2, Side: 2),
                (W: 5, D: 1, Side: 2),
            })
            {
                var plan = ResidentialLot.Frontage(shape.W, shape.D, 1987, shape.Side);
                if (!plan.Clean || plan.Spots.Count != shape.W * shape.D)
                    failures.Add($"Core frontage {shape.W}x{shape.D} has " +
                                 $"{plan.Spots.Count} apartment cells: {string.Join("; ", plan.Faults)}");
                for (int i = 0; i < shape.W; i++)
                    for (int j = 0; j < shape.D; j++)
                        if (plan.Ground[i, j] != ResidentialLot.Use.Building)
                            failures.Add($"Core frontage {shape.W}x{shape.D} left ({i},{j}) non-residential");
                foreach (var spot in plan.Spots)
                    if (!ResidentialUnits.IsFrontage(spot.Unit) || spot.CW != 1 || spot.CD != 1)
                        failures.Add($"Core frontage {shape.W}x{shape.D} used a non-modular unit");
            }
        }

        static void CoreBlocksHaveStableNamesAndQuarters(List<string> failures)
        {
            const int seed = 1987;
            var source = CoreBlockCatalog.CreateBlocks();
            var plan = CoreLayout.Arrange(source, seed, out _);
            verifiedCorePlan = plan;
            var all = CoreLayout.WithGround(source, plan);
            var territory = plan.Territory;
            if (territory == null)
            {
                failures.Add("Core layout published no territory plan");
                return;
            }
            if (territory.Quarters.Count != 6)
                failures.Add($"Core published {territory.Quarters.Count} quarters, expected 6");
            if (territory.Blocks.Count != all.Count)
                failures.Add($"territory named {territory.Blocks.Count} blocks, layout contains {all.Count}");
            var ids = new HashSet<int>();
            var stable = new HashSet<string>();
            var names = new HashSet<string>();
            for (int i = 0; i < all.Count; i++)
            {
                var block = all[i];
                if (block.BlockId < 0 || !ids.Add(block.BlockId))
                    failures.Add($"{block.Name} has missing/duplicate runtime block id {block.BlockId}");
                if (string.IsNullOrEmpty(block.StableId) || !stable.Add(block.StableId))
                    failures.Add($"{block.Name} has missing/duplicate stable id '{block.StableId}'");
                if (string.IsNullOrEmpty(block.DisplayName) || !names.Add(block.DisplayName))
                    failures.Add($"{block.Name} has missing/duplicate display name '{block.DisplayName}'");
                if (block.QuarterId == CoreQuarterId.None)
                    failures.Add($"{block.DisplayName ?? block.Name} belongs to no quarter");
            }

            for (int i = 0; i < territory.Quarters.Count; i++)
            {
                var quarter = territory.Quarters[i];
                if (quarter.BlockIds.Count == 0)
                    failures.Add($"quarter {quarter.Id} contains no blocks");
                for (int n = 0; n < quarter.Neighbours.Count; n++)
                {
                    var other = territory.Quarter(quarter.Neighbours[n]);
                    if (other == null || !Contains(other.Neighbours, quarter.Id))
                        failures.Add($"quarter adjacency {quarter.Id} -> {quarter.Neighbours[n]} is not symmetric");
                }
            }

            if (territory.Blocks.Count > 0)
            {
                var expected = territory.Blocks[0];
                var found = territory.BlockAt(expected.LocalBounds.center);
                if (found == null || string.IsNullOrEmpty(found.Name) ||
                    found.QuarterId != expected.QuarterId)
                    failures.Add("territory could not resolve a named block from its position");
            }

            // Rebuilding identity over the same accepted geometry must not rename anything.
            var again = CoreTerritoryPlan.Build(seed, all);
            for (int i = 0; i < territory.Blocks.Count; i++)
            {
                var one = territory.Blocks[i];
                var two = again.Block(one.Id);
                if (two == null || two.StableId != one.StableId || two.Name != one.Name ||
                    two.QuarterId != one.QuarterId)
                    failures.Add($"block {one.Id} changed identity when the same plan was registered again");
            }

            if (plan.Residential.Count > 0)
            {
                const int i = 0;
                var block = plan.Residential[0];
                var recipe = new ResidentialBlockRecipe(
                    block.StableId, block.DisplayName, block.Box,
                    ResidentialLot.Roll(
                        Mathf.Max(3, Mathf.RoundToInt(block.Box.width / CoreLayout.Cell)),
                        Mathf.Max(3, Mathf.RoundToInt(block.Box.height / CoreLayout.Cell)),
                        seed + i, Mathf.Max(0, block.Artery)),
                    seed + i, block.BlockId, block.QuarterId);
                if (recipe.BlockId != block.BlockId || recipe.QuarterId != block.QuarterId ||
                    recipe.Name != block.DisplayName)
                    failures.Add($"residential recipe lost territory identity for {block.DisplayName}");
            }
        }

        static void TerritoryStateIsSeparateFromThePlan(List<string> failures)
        {
            const int seed = 1987;
            var plan = verifiedCorePlan;
            if (plan == null)
                plan = CoreLayout.Arrange(CoreBlockCatalog.CreateBlocks(), seed, out _);
            var registry = new CityTerritoryRegistry();
            registry.Load(plan.Territory, DistrictFrame.At(100f, 200f, 0));

            var before = registry.State(CoreQuarterId.Landward);
            if (before == null || before.OwnerGangId != -1)
                failures.Add("newly loaded quarter is not neutral");
            if (!registry.SetOwner(CoreQuarterId.Landward, 2) || before.OwnerGangId != 2)
                failures.Add("quarter owner did not change in runtime state");
            if (!registry.Contest(CoreQuarterId.Landward, 3, 0.4f) ||
                before.Conflict != QuarterConflictState.Contested ||
                !Mathf.Approximately(before.CaptureProgress, 0.4f))
                failures.Add("quarter contest state was not recorded");
            if (plan.Territory.Quarter(CoreQuarterId.Landward) == null ||
                plan.Territory.Quarter(CoreQuarterId.Landward).Name == null)
                failures.Add("changing runtime ownership damaged the immutable territory plan");
            if (!registry.AreNeighbours(CoreQuarterId.Landward, CoreQuarterId.Downtown) ||
                registry.AreNeighbours(CoreQuarterId.NorthLandward, CoreQuarterId.SouthRiverside))
                failures.Add("territory registry returned incorrect quarter adjacency");
        }

        static bool Contains(IReadOnlyList<CoreQuarterId> values, CoreQuarterId wanted)
        {
            for (int i = 0; i < values.Count; i++) if (values[i] == wanted) return true;
            return false;
        }
    }
}
