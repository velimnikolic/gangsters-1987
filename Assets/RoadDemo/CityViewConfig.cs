using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// One device-facing set of limits for the street/map handoff and streamed block views.
    /// The gameplay model is deliberately absent: changing this asset changes how much of
    /// the already-planned city is materialised, never which city was generated.
    /// </summary>
    [CreateAssetMenu(fileName = "CityViewConfig", menuName = "Gangsters/City View Config")]
    public sealed class CityViewConfig : ScriptableObject
    {
        public const float DefaultMax3DDistance = 180f;
        public const float DefaultMinimapViewHeight = 360f;
        public const float DefaultStreetPitch = 55f;
        public const float MinimumStreetPitch = 22f;
        public const float MaximumStreetPitch = 82f;
        public const string AssetPath = "Assets/Configs/CityViewConfig.asset";

        [Header("Street to map")]
        [Tooltip("Camera boom in metres at which the existing 2D turf map takes over.")]
        [Min(40f)] public float max3DDistance = DefaultMax3DDistance;
        [Tooltip("Normal street-camera pitch. The shared Core/Game camera is held at this angle when freedom is zero.")]
        [Range(MinimumStreetPitch, MaximumStreetPitch)] public float streetPitch = DefaultStreetPitch;
        [Tooltip("Pitch movement allowed above and below the normal angle. Zero disables vertical right-drag while preserving yaw rotation.")]
        [Range(0f, 10f)] public float streetPitchFreedom = 0f;
        [Tooltip("Fade only camera-blocking buildings at close tactical zoom.")]
        public bool streetCutaway = true;
        [Tooltip("Camera boom in metres at which blocking buildings begin cutting away.")]
        [Min(10f)] public float cutawayEnterDistance = 55f;
        [Tooltip("Camera boom in metres at which full buildings return. Kept above the enter distance for hysteresis.")]
        [Min(10f)] public float cutawayExitDistance = 68f;
        [Tooltip("Absolute height in metres of the closed footprint left by a cut building.")]
        [Range(0.35f, 1.5f)] public float cutawayProxyHeight = 0.95f;
        [Tooltip("Opacity-gradient effect applied to an occluding building. 1.37 is the city-approved 137% cutaway.")]
        [Range(0f, 2f)] public float cutawayGradientAmount = 1.37f;
        [Tooltip("Seconds a building stays cut after the last sample met it, preventing edge flicker.")]
        [Range(0.2f, 1f)] public float cutawayRestoreDelay = 0.55f;
        [Tooltip("Visible crew samples checked immediately each frame before the slower street grid.")]
        [Range(0, 12)] public int cutawayCrewSamples = 6;
        [Tooltip("Keep the full building's shadow while its visual shell is cut away.")]
        public bool cutawayKeepShadows = true;

        [Header("Turf map")]
        [Tooltip("North-south metres shown by the corner minimap. The card follows the camera pivot; this is not the full-city map zoom.")]
        [Min(120f)] public float minimapViewHeight = DefaultMinimapViewHeight;

        [Header("Block recycler")]
        [Tooltip("Extra ground around the current camera footprint prepared before it enters the screen.")]
        [Min(0f)] public float prefetchMetres = 25f;
        [Tooltip("Extra margin retained after a block leaves the prefetch area, preventing edge thrash.")]
        [Min(0f)] public float recycleHysteresisMetres = 45f;
        [Tooltip("Screen-edge lead that keeps a prefetched holder attached while its renderers are registered incrementally.")]
        [Range(0f, 30f)] public float renderHysteresisMetres = 15f;
        [Tooltip("Initial views treated as startup warmup for diagnostics. They are still composed incrementally; Play never builds a whole block before the first frame.")]
        [Range(0, 12)] public int warmupViews = 3;
        [Tooltip("New block recipes materialised in one frame after warmup.")]
        [Range(1, 4)] public int maxBuildsPerFrame = 1;
        [Tooltip("Maximum generator yield steps advanced per frame. This is a hard item cap in addition to the millisecond budget, so a fast CPU cannot register thousands of prefab renderers in one frame.")]
        [Range(1, 128)] public int compositionStepsPerFrame = 12;
        [Tooltip("Renderer components attached to the render world per frame. A holder can contain thousands; attaching the whole hierarchy at once can exhaust Unity's Graphics Ring Buffer.")]
        [Range(16, 2048)] public int rendererAttachBudget = 64;
        [Tooltip("Soft budget for merge work. A single generator call cannot be interrupted mid-call.")]
        [Range(1, 24)] public int workBudgetMs = 6;
        [Tooltip("Recently left views kept ready for a quick pan back. Map mode always clears this cache.")]
        [Range(0, 24)] public int cachedViews = 4;
        [Tooltip("Legacy speculative-root warmup control. Keep at zero: real visible/cache holders establish the pool high-water mark without cloning invisible renderer hierarchies at startup.")]
        [Range(0, 20)] public int prewarmViewCapacity = 0;
        [Tooltip("Legacy content threshold retained for existing config compatibility. Asset dependencies are now warmed from actual incremental binds.")]
        [Range(0, 400)] public int heavyUnitPieceThreshold = 0;
        [Tooltip("Maximum retained prefab roots. Leased roots may temporarily exceed it; surplus roots are retired incrementally after a holder is recycled.")]
        [Range(500, 10000)] public int prewarmPartLimit = 5600;
        [Tooltip("Legacy speculative-variant reserve retained for existing config compatibility. No invisible variant roots are cloned at startup.")]
        [Range(0, 1000)] public int prewarmVariantReserve = 0;
        [Tooltip("Texture dependencies warmed or surplus pooled roots retired per frame.")]
        [Range(0, 16)] public int prewarmPartsPerFrame = 2;
        [Tooltip("At a near-horizontal pitch the top screen ray has no ground hit. Cap its prefetch reach to this many booms.")]
        [Range(1.25f, 4f)] public float groundRayBooms = 2.35f;
        [Tooltip("Fold each visible block into new runtime meshes. Off avoids mesh-allocation spikes; use authored block bakes/HLOD before enabling it again.")]
        public bool mergeVisibleBlocks = false;

        [Header("Diagnostics")]
        [Tooltip("Add recycler counts and build timings to the shared performance probe.")]
        public bool profileStreaming = true;

        public float Max3DDistance => Mathf.Max(40f, max3DDistance);
        public float StreetPitch => Mathf.Clamp(streetPitch, MinimumStreetPitch, MaximumStreetPitch);
        public float PitchFreedom => Mathf.Clamp(streetPitchFreedom, 0f, 10f);
        public bool StreetCutaway => streetCutaway;
        public float CutawayEnterDistance => Mathf.Max(10f, cutawayEnterDistance);
        public float CutawayExitDistance => Mathf.Max(CutawayEnterDistance + 1f, cutawayExitDistance);
        public float CutawayProxyHeight => Mathf.Clamp(cutawayProxyHeight, 0.35f, 1.5f);
        public float CutawayGradientAmount => Mathf.Clamp(cutawayGradientAmount, 0f, 2f);
        public float CutawayRestoreDelay => Mathf.Clamp(cutawayRestoreDelay, 0.2f, 1f);
        public int CutawayCrewSamples => Mathf.Clamp(cutawayCrewSamples, 0, 12);
        public bool CutawayKeepShadows => cutawayKeepShadows;
        public float MinimapViewHeight => Mathf.Max(120f, minimapViewHeight);
        public Vector2 StreetPitchRange => ResolvePitchRange(StreetPitch, PitchFreedom);
        public float Prefetch => Mathf.Max(0f, prefetchMetres);
        public float ReleasePadding => Prefetch + Mathf.Max(0f, recycleHysteresisMetres);
        public float RenderHysteresis => Mathf.Max(0f, renderHysteresisMetres);
        public int Warmup => Mathf.Max(0, warmupViews);
        public int BuildsPerFrame => Mathf.Max(1, maxBuildsPerFrame);
        public int CompositionStepsPerFrame => Mathf.Clamp(compositionStepsPerFrame, 1, 128);
        public int RendererAttachBudget => Mathf.Clamp(rendererAttachBudget, 16, 2048);
        public int BudgetMs => Mathf.Max(1, workBudgetMs);
        public int CachedViews => Mathf.Max(0, cachedViews);
        public int PrewarmViewCapacity => Mathf.Max(0, prewarmViewCapacity);
        public int HeavyUnitPieceThreshold => Mathf.Max(0, heavyUnitPieceThreshold);
        public int PrewarmPartLimit => Mathf.Max(500, prewarmPartLimit);
        public int PrewarmVariantReserve => Mathf.Clamp(prewarmVariantReserve, 0, PrewarmPartLimit);
        public int PrewarmPartsPerFrame => Mathf.Clamp(prewarmPartsPerFrame, 0, 16);
        public float GroundRayBooms => Mathf.Max(1.25f, groundRayBooms);

        /// <summary>Pure pitch policy used by runtime setup and editor contracts.</summary>
        public static Vector2 ResolvePitchRange(float centre, float freedom)
        {
            centre = Mathf.Clamp(centre, MinimumStreetPitch, MaximumStreetPitch);
            freedom = Mathf.Clamp(freedom, 0f, 10f);
            return new Vector2(
                Mathf.Clamp(centre - freedom, MinimumStreetPitch, MaximumStreetPitch),
                Mathf.Clamp(centre + freedom, MinimumStreetPitch, MaximumStreetPitch));
        }

        /// <summary>The shared project setting, or an in-memory default if the asset is absent.</summary>
        public static CityViewConfig Resolve(CityViewConfig configured = null)
        {
            if (configured != null) return configured;
            var asset = DemoAssetLoad.Load<CityViewConfig>(AssetPath);
            if (asset != null) return asset;

            var fallback = CreateInstance<CityViewConfig>();
            fallback.name = "CityViewConfig (defaults)";
            fallback.hideFlags = HideFlags.HideAndDontSave;
            return fallback;
        }
    }
}
