using System.Linq;
using LivingCity.Tests;
using RoadDemo;
using Unity.Pipeline.Commands;
using UnityEngine;

namespace GangstersTools
{
    public static class StreamingPipelineCommands
    {
        [CliCommand("gangsters_streaming_stress",
                    "Pan CoreDemo through a deterministic route at the same speed as held WASD.",
                    MainThreadRequired = true, Tags = new[] { "gangsters", "performance" })]
        public static object Stress()
        {
            if (!Application.isPlaying) return new { started = false, reason = "Play Mode is not running." };
            var rig = Object.FindAnyObjectByType<DemoCamera>();
            if (rig == null) return new { started = false, reason = "No DemoCamera is live." };
            var prior = Object.FindAnyObjectByType<DemoCameraStreamingStress>();
            if (prior != null) Object.Destroy(prior.gameObject);
            var go = new GameObject("Streaming WASD stress (temporary)");
            var run = go.AddComponent<DemoCameraStreamingStress>();
            run.Begin(rig);
            return new { started = true, metresPerSecond = rig.distance * 0.55f };
        }

        [CliCommand("gangsters_streaming_audit",
                    "Run the pure block-recipe/catalog/viewport contracts and report live recycler counts when playing.",
                    MainThreadRequired = true, Tags = new[] { "gangsters", "performance" })]
        public static object Audit()
        {
            var failures = ResidentialBlockStreamingTests.Run();
            var config = CityViewConfig.Resolve();
            var rig = Object.FindAnyObjectByType<DemoCamera>();
            var live = Object.FindObjectsByType<CityBlockRecycler>()
                .Select(one => new
                {
                    one.RecipeCount,
                    one.ActiveViews,
                    one.CachedViews,
                    one.PooledHolders,
                    one.PendingViews,
                    one.ComposingViews,
                    one.AttachingViews,
                    one.PendingRendererAttachments,
                    one.SourceObjects,
                    one.SourceRenderers,
                    one.BuiltViews,
                    one.EvictedViews,
                    one.LastBuildMs,
                    one.WorstBuildMs,
                    one.LastBuildStepMs,
                    one.WorstBuildStepMs,
                    one.PrefabPoolCapacity,
                    one.AvailablePrefabParts,
                    one.PrewarmedPrefabParts,
                    one.PendingPrewarmParts,
                    one.PendingPoolRetirements,
                    one.PendingAssetWarm,
                    one.FallbackBlocks,
                    one.VisibleFallbackBlocks,
                    one.ReusedPrefabParts,
                    one.RuntimePrefabMisses,
                    one.RuntimePrefabMissTypes,
                    one.LargestRuntimeMissPrefab,
                    one.LargestRuntimeMissRenderers,
                    one.RuntimeMissSummary,
                }).ToArray();
            return new
            {
                passed = failures.Count == 0,
                failures = failures.ToArray(),
                generatorVersion = ResidentialBlockRecipe.GeneratorVersion,
                max3DDefault = CityViewConfig.DefaultMax3DDistance,
                max3DConfigured = config.Max3DDistance,
                max3DCamera = rig != null ? rig.mapAt : (float?)null,
                streetPitchConfigured = config.StreetPitch,
                pitchFreedomConfigured = config.PitchFreedom,
                streetCutawayConfigured = config.StreetCutaway,
                minimapViewHeightConfigured = config.MinimapViewHeight,
                prefetchConfigured = config.Prefetch,
                renderLeadConfigured = config.RenderHysteresis,
                compositionStepsPerFrameConfigured = config.CompositionStepsPerFrame,
                rendererAttachBudgetConfigured = config.RendererAttachBudget,
                prefabPoolLimitConfigured = config.PrewarmPartLimit,
                prefabVariantReserveConfigured = config.PrewarmVariantReserve,
                cameraPitch = rig != null ? rig.pitch : (float?)null,
                cameraPitchMinimum = rig != null ? rig.MinimumPitch : (float?)null,
                cameraPitchMaximum = rig != null ? rig.MaximumPitch : (float?)null,
                pitchLocked = rig != null ? rig.PitchLocked : (bool?)null,
                mapOut = rig != null ? rig.MapOut : (bool?)null,
                mapOpen = TurfMapHud.IsOpen,
                stressRunning = Object.FindAnyObjectByType<DemoCameraStreamingStress>() != null,
                coreBlocks = CoreBlockCatalog.Count,
                playing = Application.isPlaying,
                recyclers = live,
            };
        }

        [CliCommand("gangsters_turf_map_audit",
                    "Report shared TurfMap source coverage, names, model footprints and local minimap framing.",
                    MainThreadRequired = true, Tags = new[] { "gangsters", "map" })]
        public static object TurfMapAudit()
        {
            var builder = Object.FindAnyObjectByType<RoadDemoBuilder>();
            var hud = Object.FindAnyObjectByType<TurfMapHud>();
            var minimap = Object.FindAnyObjectByType<TurfMinimap>();
            var survey = hud != null ? hud.Survey : minimap != null ? minimap.Survey : null;
            var config = CityViewConfig.Resolve();

            int endpoints = 0, uncoveredEndpoints = 0, intersections = 0;
            if (builder != null && survey != null)
            {
                foreach (var road in builder.QuarterRoads)
                {
                    endpoints += 2;
                    if (!survey.Streets.Any(street => Covers(street.World, road.a)))
                        uncoveredEndpoints++;
                    if (!survey.Streets.Any(street => Covers(street.World, road.b)))
                        uncoveredEndpoints++;
                }

                foreach (var vertical in survey.Streets.Where(street => street.Vertical))
                    intersections += survey.Streets.Count(across =>
                        !across.Vertical && vertical.World.Overlaps(across.World));
            }

            var requested = minimap != null ? minimap.RequestedView : default;
            var city = survey != null ? survey.CityView : default;
            return new
            {
                ready = survey != null && survey.Ready,
                primaryStructure = builder != null && builder.HasPrimaryStructure,
                residentialSources = builder != null ? builder.ResidentialMapSources.Count : 0,
                residentialGeometryVersion = builder != null
                    ? builder.ResidentialGeometryVersion : -1,
                recipes = builder != null
                    ? builder.ResidentialMapSources.Sum(source => source.Model?.Count ?? 0) : 0,
                streets = survey?.Streets.Count ?? 0,
                namedStreets = survey?.Streets.Count(street =>
                    !string.IsNullOrEmpty(street.Name)) ?? 0,
                roadEndpoints = endpoints,
                uncoveredRoadEndpoints = uncoveredEndpoints,
                intersections,
                buildings = survey?.Buildings.Count ?? 0,
                landmarks = survey?.Landmarks.Count ?? 0,
                gyms = survey?.Landmarks.Count(mark => mark.Kind == TurfLandmarkKind.Gym) ?? 0,
                fuelStations = survey?.Landmarks.Count(mark =>
                    mark.Kind == TurfLandmarkKind.FuelStation) ?? 0,
                carYards = survey?.Landmarks.Count(mark =>
                    mark.Kind == TurfLandmarkKind.CarYard) ?? 0,
                skateparks = survey?.Landmarks.Count(mark =>
                    mark.Kind == TurfLandmarkKind.Skatepark) ?? 0,
                parking = survey?.Landmarks.Count(mark =>
                    mark.Kind == TurfLandmarkKind.Parking) ?? 0,
                cafes = survey?.Landmarks.Count(mark =>
                    mark.Kind == TurfLandmarkKind.Cafe) ?? 0,
                subways = survey?.Landmarks.Count(mark =>
                    mark.Kind == TurfLandmarkKind.Transit) ?? 0,
                residentialGreens = survey?.ResidentialGreenCount ?? 0,
                parkSurfaces = survey?.ParkSurfaceCount ?? 0,
                labels = survey?.Labels.Count ?? 0,
                minimapInstalled = minimap != null,
                minimapPrinted = minimap != null && minimap.Printed,
                minimapConfiguredHeight = config.MinimapViewHeight,
                minimapRequestedHeight = minimap != null ? requested.height : 0f,
                cityViewHeight = survey != null ? city.height : 0f,
                minimapZoom = minimap != null && requested.height > 0f
                    ? city.height / requested.height : 0f,
                playing = Application.isPlaying,
            };
        }

        [CliCommand("gangsters_turf_map_view",
                    "Move the shared camera above or below the 3D-to-map line for visual auditing.",
                    MainThreadRequired = true, Tags = new[] { "gangsters", "map" })]
        public static object TurfMapView(
            [CliArg("full", "True opens the full TurfMap; false returns to the street/minimap.")]
            bool full = true)
        {
            if (!Application.isPlaying)
                return new { changed = false, reason = "Play Mode is not running." };
            var rig = Object.FindAnyObjectByType<DemoCamera>();
            if (rig == null)
                return new { changed = false, reason = "No DemoCamera is live." };

            rig.distance = full
                ? Mathf.Min(rig.mapCeiling, rig.mapAt + 30f)
                : Mathf.Max(20f, rig.mapAt - 25f);
            return new { changed = true, full, distance = rig.distance, mapAt = rig.mapAt };
        }

        static bool Covers(Rect world, Vector2 point)
        {
            const float epsilon = 0.05f;
            return point.x >= world.xMin - epsilon && point.x <= world.xMax + epsilon &&
                   point.y >= world.yMin - epsilon && point.y <= world.yMax + epsilon;
        }
    }
}
