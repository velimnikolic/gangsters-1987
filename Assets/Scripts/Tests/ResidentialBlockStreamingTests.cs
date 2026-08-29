using System.Collections.Generic;
using RoadDemo;
using UnityEngine;

namespace LivingCity.Tests
{
    /// <summary>Pure contracts behind streamed residential views. Nothing here enters Play
    /// mode or creates a UnityEngine.Object, so the same checks can run from the editor CLI.</summary>
    public static class ResidentialBlockStreamingTests
    {
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
            CameraPitchPolicy(failures);
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

        static void CameraPitchPolicy(List<string> failures)
        {
            Vector2 locked = CityViewConfig.ResolvePitchRange(55f, 0f);
            if (!Mathf.Approximately(locked.x, 55f) || !Mathf.Approximately(locked.y, 55f))
                failures.Add("zero pitch freedom did not lock the camera at its configured angle");

            Vector2 small = CityViewConfig.ResolvePitchRange(55f, 2f);
            if (!Mathf.Approximately(small.x, 53f) || !Mathf.Approximately(small.y, 57f))
                failures.Add("small pitch freedom did not produce a symmetric pitch range");
        }
    }
}
