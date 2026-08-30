using System.Collections.Generic;
using LivingCity.Gangs;
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
            TurfMapProxyHeightProjection(failures);
            CompactCoreInfillHasBuildings(failures);
            CoreBlocksHaveStableNamesAndQuarters(failures);
            CoreRiverRunsFullCity(failures);
            CoreShopfrontsCanSeatEveryGang(failures);
            StorefrontDecorationIsDeterministicAndCoversOpenFacades(failures);
            CornerStorefrontCoversBothFacesAndKeepsEntranceClear(failures);
            TerritoryStateIsSeparateFromThePlan(failures);
            return failures;
        }

        static void StorefrontDecorationIsDeterministicAndCoversOpenFacades(
            List<string> failures)
        {
            const int openings = 9;
            var first = ResidentialBlocks.PlanStorefronts(openings, 1987);
            var repeat = ResidentialBlocks.PlanStorefronts(openings, 1987);

            if (first.ClosedMask != repeat.ClosedMask ||
                first.Styles.Length != repeat.Styles.Length)
            {
                failures.Add("storefront dressing changed for the same seed");
                return;
            }

            int props = 0, open = 0;
            for (int i = 0; i < first.Styles.Length; i++)
            {
                if (first.Styles[i] != repeat.Styles[i])
                    failures.Add($"storefront opening {i} changed style for the same seed");
                if (first.Styles[i] >= 0) props++;
                if ((first.ClosedMask & (1 << i)) == 0) open++;
            }

            if (open == 0) failures.Add("storefront dressing closed every opening");
            if (props != open)
                failures.Add($"storefront dressing covered {props} of {open} open facades");
        }

        static void CornerStorefrontCoversBothFacesAndKeepsEntranceClear(
            List<string> failures)
        {
            var corner = new[]
            {
                new ResidentialStorefrontOpening(
                    Vector3.zero, Vector3.right, Vector3.back, 4.4f, 2.5f, 7,
                    corner: true),
                new ResidentialStorefrontOpening(
                    Vector3.zero, Vector3.forward, Vector3.right, 4.4f, 2.5f, 7,
                    corner: true),
                new ResidentialStorefrontOpening(
                    Vector3.zero, new Vector3(1f, 0f, 1f).normalized,
                    new Vector3(1f, 0f, -1f).normalized, 1.4f, 2.5f, 7,
                    entrance: true, corner: true),
            };
            var plan = ResidentialBlocks.PlanStorefronts(corner, 1987);
            if (plan.ClosedMask != 0)
                failures.Add("the only corner business was left closed");
            if (plan.Styles[0] < 0 || plan.Styles[1] < 0)
                failures.Add("a two-sided corner shop did not dress both main facades");
            if (plan.Styles[2] >= 0)
                failures.Add("a corner shop put a prop in its diagonal entrance");
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

        static void TurfMapProxyHeightProjection(List<string> failures)
        {
            const float height = 23f;
            var pitches = new[] { 18f, 52f, 82f };
            var headings = new[] { 0f, 47f, 133f, 271f };
            for (int p = 0; p < pitches.Length; p++)
                for (int h = 0; h < headings.Length; h++)
                {
                    float pitch = pitches[p];
                    float heading = headings[h];
                    var local = TurfMapBuildingLayer.HeightOffsetWorld(
                        height, pitch, heading);
                    var projected = TurfMapHud.ApplyTilt(
                        TurfMapHud.RotateForHeading(local, heading),
                        TurfMapHud.PitchTilt(pitch));
                    var expected = new Vector2(
                        0f, height * Mathf.Cos(pitch * Mathf.Deg2Rad));
                    if ((projected - expected).sqrMagnitude <= 0.000001f)
                        continue;
                    failures.Add($"TurfMap height projection drifted at pitch {pitch}, " +
                                 $"heading {heading}: {projected} instead of {expected}");
                }
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

        static void CoreRiverRunsFullCity(List<string> failures)
        {
            var plan = verifiedCorePlan;
            if (plan == null || plan.Residential.Count == 0)
            {
                failures.Add("Core river contract has no verified residential city plan");
                return;
            }

            float cityZ0 = float.MaxValue, cityZ1 = float.MinValue;
            for (int i = 0; i < plan.Residential.Count; i++)
            {
                cityZ0 = Mathf.Min(cityZ0, plan.Residential[i].Box.yMin);
                cityZ1 = Mathf.Max(cityZ1, plan.Residential[i].Box.yMax);
            }
            if (plan.River.Z0 > cityZ0 + 0.1f || plan.River.Z1 < cityZ1 - 0.1f)
                failures.Add($"Core river covers z {plan.River.Z0:F0}..{plan.River.Z1:F0}, " +
                             $"but the city covers {cityZ0:F0}..{cityZ1:F0}");

            float span = plan.River.Z1 - plan.River.Z0;
            float southThird = plan.River.Z0 + span / 3f;
            float northThird = plan.River.Z1 - span / 3f;
            bool southBridge = false, northBridge = false;
            float cut = 0f;
            for (int i = 0; i < plan.Bridges.Count; i++)
            {
                var band = plan.Bridges[i].Band;
                southBridge |= band.center.y < southThird;
                northBridge |= band.center.y > northThird;
                cut += band.height;
            }
            if (!southBridge || !northBridge)
                failures.Add($"Core river has no bridge in the " +
                             $"{(!southBridge ? "south" : "north")} riverside reach");

            float promenade = 0f;
            for (int i = 0; i < plan.Quays.Count; i++)
                promenade += plan.Quays[i].Box.height;
            if (Mathf.Abs(promenade + cut - span) > CoreLayout.Cell * 0.1f)
                failures.Add($"Core promenade and bridge cuts cover {promenade + cut:F0} m " +
                             $"of the river's {span:F0} m");
        }

        static void CoreShopfrontsCanSeatEveryGang(List<string> failures)
        {
            const int seed = 1987;
            var core = new CoreDistrict();
            core.Plan(System.Array.Empty<float>(), seed);
            var sites = CoreResidentialFronts.Collect(
                core.ResidentialBlocks, DistrictFrame.Identity);

            if (sites.Count < GangCatalog.GangCount)
            {
                failures.Add($"Core has {sites.Count} reachable residential shopfront " +
                             $"candidates for {GangCatalog.GangCount} gangs");
                core.Dispose();
                return;
            }

            var candidates = new List<GangFronts.FrontCandidate>(sites.Count);
            for (var i = 0; i < sites.Count; i++)
                candidates.Add(new GangFronts.FrontCandidate(
                    sites[i].BlockId, sites[i].Door.x, sites[i].Door.z));

            var picks = GangFronts.Select(candidates,
                GangSeeder.Generate(seed, null)[GangCatalog.PlayerGangId].FrontRoll,
                GangCatalog.GangCount);
            var occupied = new HashSet<int>();
            for (var gang = 0; gang < picks.Length; gang++)
            {
                if (picks[gang] < 0)
                    failures.Add($"Core left gang {gang} without a residential outfit");
                else if (!occupied.Add(picks[gang]))
                    failures.Add($"Core assigned shopfront {picks[gang]} to two gangs");
            }

            core.Dispose();
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
