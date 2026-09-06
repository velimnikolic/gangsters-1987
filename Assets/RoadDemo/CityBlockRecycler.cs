using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// Optional scene service a district can use for generated blocks. The immutable model
    /// is shared by the recycler and the map; holders remain only the recyclable visual
    /// representation. An older host may safely fall back to eager composition.
    /// </summary>
    public interface IStreamedDistrictHost
    {
        CityViewConfig ViewConfig { get; }
        Transform StreamRoot(string name);
        void RegisterResidentialModel(ResidentialBlockModel model, DistrictFrame frame);
        void RegisterBlockRecycler(CityBlockRecycler recycler);
    }

    /// <summary>Allocation-free 2D intersection helpers shared by the runtime and tests.</summary>
    public static class CityBlockVisibility
    {
        const float Epsilon = 0.0001f;

        public static Rect Expand(Rect rect, float by)
            => Rect.MinMaxRect(rect.xMin - by, rect.yMin - by, rect.xMax + by, rect.yMax + by);

        /// <summary>Does an axis-aligned block meet the camera's convex ground quad?</summary>
        public static bool Intersects(Vector2[] convex, Rect block, float padding = 0f)
        {
            if (convex == null || convex.Length < 3) return false;
            block = Expand(block, Mathf.Max(0f, padding));

            for (int i = 0; i < 4; i++)
                if (InsideConvex(convex, Corner(block, i))) return true;
            for (int i = 0; i < convex.Length; i++)
                if (block.Contains(convex[i])) return true;
            for (int a = 0; a < convex.Length; a++)
            {
                var a0 = convex[a];
                var a1 = convex[(a + 1) % convex.Length];
                for (int b = 0; b < 4; b++)
                    if (SegmentsMeet(a0, a1, Corner(block, b), Corner(block, (b + 1) % 4))) return true;
            }
            return false;
        }

        /// <summary>Does a block's conservative 3D volume meet the camera frustum?</summary>
        public static bool Intersects(Plane[] frustum, Rect block, float groundY,
                                      float height, float padding = 0f)
        {
            if (frustum == null || frustum.Length < 6) return false;
            block = Expand(block, Mathf.Max(0f, padding));
            float h = Mathf.Max(1f, height);
            var volume = new Bounds(
                new Vector3(block.center.x, groundY + h * 0.5f, block.center.y),
                new Vector3(block.width, h + 4f, block.height));
            return GeometryUtility.TestPlanesAABB(frustum, volume);
        }

        static bool InsideConvex(Vector2[] polygon, Vector2 point)
        {
            float sign = 0f;
            for (int i = 0; i < polygon.Length; i++)
            {
                var a = polygon[i];
                var b = polygon[(i + 1) % polygon.Length];
                float cross = Cross(b - a, point - a);
                if (Mathf.Abs(cross) <= Epsilon) continue;
                if (sign == 0f) sign = Mathf.Sign(cross);
                else if (Mathf.Sign(cross) != sign) return false;
            }
            return true;
        }

        static bool SegmentsMeet(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            if (Mathf.Max(a.x, b.x) + Epsilon < Mathf.Min(c.x, d.x) ||
                Mathf.Max(c.x, d.x) + Epsilon < Mathf.Min(a.x, b.x) ||
                Mathf.Max(a.y, b.y) + Epsilon < Mathf.Min(c.y, d.y) ||
                Mathf.Max(c.y, d.y) + Epsilon < Mathf.Min(a.y, b.y)) return false;
            float abC = Cross(b - a, c - a), abD = Cross(b - a, d - a);
            float cdA = Cross(d - c, a - c), cdB = Cross(d - c, b - c);
            return abC * abD <= Epsilon && cdA * cdB <= Epsilon;
        }

        static Vector2 Corner(Rect rect, int index) => index switch
        {
            0 => new Vector2(rect.xMin, rect.yMin),
            1 => new Vector2(rect.xMax, rect.yMin),
            2 => new Vector2(rect.xMax, rect.yMax),
            _ => new Vector2(rect.xMin, rect.yMax),
        };

        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
    }

    /// <summary>
    /// RecyclerView for generated city blocks. ResidentialBlockModel is the adapter data;
    /// Holder is the recycled view holder; Compose binds a holder only when its rectangle
    /// enters the camera's ground footprint. Recently left holders form a small LRU cache,
    /// while entering the existing 2D map evicts every 3D payload immediately.
    /// </summary>
    public sealed partial class CityBlockRecycler : MonoBehaviour
    {
        sealed class View
        {
            public GameObject Holder;
            public Transform Content;
            public Transform Merged;
            public ResidentialBlockRecipe Recipe;
            public ulong ContentKey;
            public ResidentialBlocks.IncrementalComposition Compose;
            public IEnumerator Merge;
            public ResidentialConditionView Condition;
            public bool Active;
            public bool Attached;
            public bool Attaching;
            public int AttachCursor;
            public int LastUsed;
            public int Objects;
            public int Renderers;
            public readonly List<GameObject> Parts = new List<GameObject>();
            public readonly List<Renderer> AttachRenderers = new List<Renderer>();
        }

        readonly struct Candidate
        {
            public readonly ResidentialBlockRecipe Recipe;
            public readonly float Priority;
            public Candidate(ResidentialBlockRecipe recipe, float priority)
            { Recipe = recipe; Priority = priority; }
        }

        static readonly List<CityBlockRecycler> Instances = new List<CityBlockRecycler>();
        static CityBlockRecycler BindingOwner;
        static readonly Vector3[] ViewCorners =
        {
            new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f),
            new Vector3(1f, 1f, 0f), new Vector3(0f, 1f, 0f),
        };

        readonly Dictionary<string, View> _resident = new Dictionary<string, View>(StringComparer.Ordinal);
        readonly Stack<View> _pool = new Stack<View>();
        readonly List<View> _scratchViews = new List<View>();
        readonly List<Candidate> _candidates = new List<Candidate>();
        readonly List<View> _attachments = new List<View>();
        readonly HashSet<ResidentialBlockRecipe> _invalid = new HashSet<ResidentialBlockRecipe>();
        readonly Dictionary<string, float> _retryAt = new Dictionary<string, float>(StringComparer.Ordinal);
        readonly Vector2[] _viewport = new Vector2[4];
        readonly List<Transform> _transformScratch = new List<Transform>();
        readonly List<Renderer> _rendererScratch = new List<Renderer>();

        ResidentialBlockModel _model;
        DistrictFrame _frame;
        CityViewConfig _config;
        ResidentialFallbackLayer _fallbacks;
        ResidentialPrefabPool _prefabPool;
        DemoCamera _rig;
        Camera _camera;
        DemoNightWindows _night;
        DemoStreetLamps _lamps;
        DemoParkedCarGlow _parkedGlow;
        bool _mapHandoff = true;
        bool _mapWasOut;
        bool _runtimePoolStarted;
        int _startupTargetViews;
        View _binding;
        long _bindingCpuMs;
        long _bindingWorstStepMs;
        int _mergeCursor;
        int _sourceObjects;
        int _sourceRenderers;
        int _built;
        int _evicted;
        int _merged;
        long _lastBuildMs;
        long _worstBuildMs;
        long _lastBuildStepMs;
        long _worstBuildStepMs;

        public int RecipeCount => _model?.Count ?? 0;
        // counted off the views: a tracked tally drifted and evicted fresh blocks (2026-09-06)
        public int ActiveViews
        {
            get { int n = 0; foreach (var pair in _resident) if (pair.Value.Active) n++; return n; }
        }
        public int CachedViews => _resident.Count - ActiveViews;
        public int PooledHolders => _pool.Count;
        public int PendingViews => _candidates.Count + (_binding != null ? 1 : 0);
        public int ComposingViews => _binding != null ? 1 : 0;
        public int AttachingViews => _attachments.Count;
        public int PendingRendererAttachments
        {
            get
            {
                int pending = 0;
                for (int i = 0; i < _attachments.Count; i++)
                {
                    var view = _attachments[i];
                    if (view != null)
                        pending += Mathf.Max(0, view.AttachRenderers.Count - view.AttachCursor);
                }
                return pending;
            }
        }
        public int SourceObjects => _sourceObjects;
        public int SourceRenderers => _sourceRenderers;
        public int BuiltViews => _built;
        public int EvictedViews => _evicted;
        public long LastBuildMs => _lastBuildMs;
        public long WorstBuildMs => _worstBuildMs;
        public long LastBuildStepMs => _lastBuildStepMs;
        public long WorstBuildStepMs => _worstBuildStepMs;
        public int PrefabPoolCapacity => _prefabPool?.Capacity ?? 0;
        public int AvailablePrefabParts => _prefabPool?.Available ?? 0;
        public int ReusedPrefabParts => _prefabPool?.Reused ?? 0;
        public int RuntimePrefabMisses => _prefabPool?.RuntimeMisses ?? 0;
        public int PrewarmedPrefabParts => _prefabPool?.Prewarmed ?? 0;
        public int PendingPrewarmParts => _prefabPool?.PendingPrewarmParts ?? 0;
        public int PendingPoolRetirements => _prefabPool?.PendingRetirements ?? 0;
        public int PendingAssetWarm => _prefabPool?.PendingAssetWarm ?? 0;
        public int RuntimePrefabMissTypes => _prefabPool?.RuntimeMissTypes ?? 0;
        public string LargestRuntimeMissPrefab => _prefabPool?.LargestRuntimeMissPrefab;
        public int LargestRuntimeMissRenderers => _prefabPool?.LargestRuntimeMissRenderers ?? 0;
        public string RuntimeMissSummary => _prefabPool?.RuntimeMissSummary ?? string.Empty;
        public int FallbackBlocks => _fallbacks?.BlockCount ?? 0;
        public int VisibleFallbackBlocks => _fallbacks?.VisibleBlocks ?? 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            Instances.Clear();
            BindingOwner = null;
            _holding = false;
        }

        public void Init(ResidentialBlockModel model, DistrictFrame frame, CityViewConfig config,
                         bool mapHandoff = true, ResidentialFallbackLayer fallbacks = null)
        {
            if (_model != null) _model.Changed -= OnModelChanged;
            ClearNavigation();
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _frame = frame;
            _config = CityViewConfig.Resolve(config);
            _fallbacks = fallbacks != null ? fallbacks : GetComponent<ResidentialFallbackLayer>();
            _prefabPool?.SetRetainedLimit(_config.PrewarmPartLimit);
            _mapHandoff = mapHandoff;
            _model.Changed += OnModelChanged;
        }

        public void SetCamera(DemoCamera rig)
        {
            _rig = rig;
            _camera = rig != null ? rig.GetComponent<Camera>() : null;
        }

        void Awake()
        {
            if (!Instances.Contains(this)) Instances.Add(this);
            _prefabPool = new ResidentialPrefabPool(transform);
        }

        void Start()
        {
            PrepareNavigation();
            ResolveSceneServices();
            if (_model == null || _rig == null || (_mapHandoff && _rig.MapOut)) return;
            RefreshDesired();
            // RecyclerView must not clone a twelve-view renderer inventory in Start.
            // Remember the initial window for diagnostics, then let ordinary Update bind
            // it with both a time budget and a hard composition-step cap.
            // The first visible window is startup work even when warmupViews is smaller.
            // Start runtime-miss accounting only after that whole window has had a chance
            // to bind; otherwise deliberately incremental startup clones are reported as
            // movement misses and hide the useful high-water signal.
            _startupTargetViews = _candidates.Count;
            BeginRuntimePoolWhenReady();
        }

        static readonly Unity.Profiling.ProfilerMarker updateMarker = new Unity.Profiling.ProfilerMarker("CityBlockRecycler.Update");

        void Update()
        {
            using var profile = updateMarker.Auto();
            if (_model == null) return;
            ResolveSceneServices();
            ProcessInvalidations();
            if (_rig == null || _camera == null) return;

            bool map = _mapHandoff && _rig.MapOut;
            if (map)
            {
                if (!_mapWasOut) SuspendForMap();
                _mapWasOut = true;
                _prefabPool.PrewarmStep(_config.PrewarmPartsPerFrame, 1);
                BeginRuntimePoolWhenReady();
                return;
            }
            if (_mapWasOut) _fallbacks?.SetSuppressed(false);
            _mapWasOut = false;

            var clock = Stopwatch.StartNew();
            RefreshDesired();
            MakeRoomForIncomingView();
            BuildCandidates(_config.BuildsPerFrame, clock, ignoreBudget: false);
            // Prefabs discovered by the incremental composer queue their texture
            // dependencies while their holder is still inactive. Resolve only a tiny
            // slice per frame and do not attach renderers until that queue has drained.
            _prefabPool.PrewarmStep(_config.PrewarmPartsPerFrame, 1);
            // Composition is allowed to consume the soft frame budget. Renderer
            // attachment has its own strict item cap, so do not let a 6 ms compose
            // starve an already-visible holder forever. Its separate watch still
            // supplies the same time safety net in addition to the configured item cap.
            if (_prefabPool.PendingAssetWarm == 0)
                PumpAttachments(Stopwatch.StartNew());
            PumpConditions(clock);
            PumpMerges(clock);
            BeginRuntimePoolWhenReady();
            TrimCache();
        }

        void BeginRuntimePoolWhenReady()
        {
            if (_runtimePoolStarted || _prefabPool == null ||
                _built < _startupTargetViews ||
                _prefabPool.PendingPrewarmParts > 0 ||
                _prefabPool.PendingAssetWarm > 0) return;
            // Do not report the first holders racing the deliberately gradual startup
            // queue as WASD misses. From this point on the counter means exactly what
            // the audit says: an unprepared prefab requested during runtime movement.
            _prefabPool.BeginRuntime();
            _runtimePoolStarted = true;
        }

        void ResolveSceneServices()
        {
            if (_rig == null) SetCamera(FindAnyObjectByType<DemoCamera>());
            if (_night == null) _night = FindAnyObjectByType<DemoNightWindows>();
            if (_lamps == null) _lamps = FindAnyObjectByType<DemoStreetLamps>();
            if (_parkedGlow == null) _parkedGlow = FindAnyObjectByType<DemoParkedCarGlow>();
            if (_config == null) _config = CityViewConfig.Resolve();
        }

        bool ReadViewport()
        {
            if (_camera == null || _rig == null) return false;
            float planeY = transform.position.y;
            float cap = Mathf.Max(40f, _rig.GroundRayDistance(planeY) * _config.GroundRayBooms);
            for (int i = 0; i < ViewCorners.Length; i++)
            {
                var ray = _camera.ViewportPointToRay(ViewCorners[i]);
                float t = Mathf.Abs(ray.direction.y) > 0.0001f
                    ? (planeY - ray.origin.y) / ray.direction.y
                    : -1f;
                if (t <= 0f || t > cap) t = cap;
                var point = ray.origin + ray.direction * t;
                _viewport[i] = new Vector2(point.x, point.z);
            }
            return true;
        }

        void RefreshDesired()
        {
            _candidates.Clear();
            if (!ReadViewport()) return;

            _scratchViews.Clear();
            foreach (var pair in _resident) _scratchViews.Add(pair.Value);
            for (int i = 0; i < _scratchViews.Count; i++)
            {
                var view = _scratchViews[i];
                if (!view.Active) continue;
                // RecyclerView distinction: a payload may stay bound in the prefetch/LRU
                // window without remaining attached to the renderer. Keeping the old
                // recycle margin here rendered several off-screen blocks and could flood
                // Unity's Graphics Ring Buffer during a fast pan.
                if (Visible(view.Recipe, _config.RenderHysteresis)) continue;
                Deactivate(view);
            }

            var blocks = _model.Blocks;
            for (int i = 0; i < blocks.Count; i++)
            {
                var recipe = blocks[i];
                if (!Visible(recipe, _config.Prefetch)) continue;
                if (_binding != null && _binding.Recipe != null &&
                    _binding.Recipe.Id == recipe.Id)
                {
                    if (_binding.ContentKey == recipe.ContentKey) continue;
                    CancelBinding();
                }
                if (_resident.TryGetValue(recipe.Id, out var resident))
                {
                    if (resident.ContentKey != recipe.ContentKey)
                    {
                        Evict(resident);
                        AddCandidate(recipe);
                    }
                    else if (Visible(recipe, resident.Active
                        ? _config.RenderHysteresis
                        : Mathf.Min(_config.Prefetch, _config.RenderHysteresis)))
                        Activate(resident);
                    else
                        Deactivate(resident);
                    continue;
                }
                if (_retryAt.TryGetValue(recipe.Id, out float retry) && Time.unscaledTime < retry) continue;
                AddCandidate(recipe);
            }
            _candidates.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        }

        /// <summary>Ground somebody outside the camera is looking at - the ledger's block
        /// file films a real block, and a block the streamer had put away is a block the
        /// file would have to draw a guess of. While it is held the block counts as
        /// visible: it composes if it is not standing, it stays activated, and the LRU
        /// never reaches it, because eviction only ever takes an INACTIVE view.</summary>
        static Rect _held;
        static bool _holding;

        public static void Hold(Rect worldRect)
        {
            _held = worldRect;
            _holding = worldRect.width > 0f && worldRect.height > 0f;
        }

        public static void Release() => _holding = false;

        /// <summary>
        /// Counts every change the streamer makes to what is STANDING in the city: a
        /// block bound, activated, put away, finished attaching its renderers, or
        /// evicted. Anyone photographing the city off-camera - the ledger's block file -
        /// watches this instead of guessing when the ground under its lens is finished:
        /// a block streams in over many frames and several seconds, and a still exposed
        /// while it was arriving is a photograph of an empty lot.
        /// </summary>
        public static int StreamRevision { get; private set; }

        static void Streamed() => StreamRevision++;

        /// <summary>Whether this recipe currently owns an active streamed hierarchy.
        /// Diagnostics use this to distinguish a missing binding in a standing block from
        /// the expected absence of views for the rest of the off-screen city.</summary>
        public static bool IsViewActive(string recipeId)
        {
            if (string.IsNullOrEmpty(recipeId)) return false;
            for (int i = Instances.Count - 1; i >= 0; i--)
            {
                var recycler = Instances[i];
                if (recycler == null)
                {
                    Instances.RemoveAt(i);
                    continue;
                }
                if (recycler._resident.TryGetValue(recipeId, out var view) &&
                    view != null && view.Active && view.Holder != null &&
                    view.Holder.activeInHierarchy)
                    return true;
            }
            return false;
        }

        /// <summary>Whether the streamed view covering this ground is fully attached and
        /// ready to be photographed. EVERY view over the ground has to be standing, not
        /// merely one of them: a block can straddle two recipes, and half a block is
        /// exactly the half-empty picture this answer exists to prevent. Ground owned by
        /// no recycler is eager scene content and is therefore ready already.</summary>
        public static bool HeldReady(Rect worldRect)
        {
            for (var i = 0; i < Instances.Count; i++)
            {
                var recycler = Instances[i];
                if (recycler == null || recycler._model == null)
                    continue;
                var blocks = recycler._model.Blocks;
                for (var b = 0; b < blocks.Count; b++)
                {
                    var recipe = blocks[b];
                    if (recipe == null)
                        continue;
                    var world = recycler._frame.ToWorldRect(recipe.LocalBounds);
                    if (!world.Overlaps(worldRect, allowInverse: true))
                        continue;
                    if (!recycler._resident.TryGetValue(recipe.Id, out var view) ||
                        view == null || !view.Active || !view.Attached)
                        return false;
                }
            }
            return true;
        }

        static bool Held(Rect world) =>
            _holding && world.Overlaps(_held, allowInverse: true);

        bool Visible(ResidentialBlockRecipe recipe, float padding)
        {
            if (recipe == null) return false;
            var world = _frame.ToWorldRect(recipe.LocalBounds);
            if (Held(world)) return true;
            // Pitch is locked in street mode, so the ground footprint is the bounded
            // visibility contract. Give it the horizontal projection of the tallest
            // facade: a roof may enter the image shortly before its ground rectangle,
            // but an infinite camera frustum must not materialise the next fifty blocks.
            float pitch = Mathf.Max(CityViewConfig.MinimumStreetPitch, _rig.pitch);
            float facadeLead = recipe.VisualHeight /
                               Mathf.Max(0.1f, Mathf.Tan(pitch * Mathf.Deg2Rad));
            return CityBlockVisibility.Intersects(
                _viewport, world, padding + Mathf.Min(60f, facadeLead));
        }

        void AddCandidate(ResidentialBlockRecipe recipe)
        {
            var world = _frame.ToWorldRect(recipe.LocalBounds);
            var centre = new Vector3(world.center.x,
                transform.position.y + recipe.VisualHeight * 0.5f, world.center.y);
            _candidates.Add(new Candidate(recipe,
                _camera != null
                    ? (centre - _camera.transform.position).sqrMagnitude
                    : (world.center - new Vector2(_rig.pivot.x, _rig.pivot.z)).sqrMagnitude));
        }

        void BuildCandidates(int limit, Stopwatch frame, bool ignoreBudget)
        {
            if (!ignoreBudget)
            {
                PumpBinding(frame);
                if (_binding != null || BindingOwner != null && BindingOwner != this) return;

                int started = 0;
                while (_candidates.Count > 0 && started < limit &&
                       frame.ElapsedMilliseconds < _config.BudgetMs)
                {
                    var candidate = _candidates[0];
                    _candidates.RemoveAt(0);
                    if (_resident.ContainsKey(candidate.Recipe.Id)) continue;
                    if (!StartBuild(candidate.Recipe)) break;
                    started++;
                }
                PumpBinding(frame);
                return;
            }

            int made = 0;
            while (_candidates.Count > 0 && made < limit)
            {
                var candidate = _candidates[0];
                _candidates.RemoveAt(0);
                if (_resident.ContainsKey(candidate.Recipe.Id)) continue;
                if (BuildImmediate(candidate.Recipe)) made++;
            }
        }

        View PrepareView(ResidentialBlockRecipe recipe, bool visible)
        {
            var view = AcquireHolder();
            view.Recipe = recipe;
            view.ContentKey = recipe.ContentKey;
            view.LastUsed = Time.frameCount;
            view.Active = false;
            view.Holder.name = $"View {recipe.Name}";
            var holder = view.Holder.transform;

            // ResidentialBlocks is an origin-space composer: like CoreDistrict's eager
            // path, it must stand a block at world identity and move the completed root
            // afterwards. Composing under an already-positioned ViewHolder makes its
            // world-space placements cancel the holder offset; the later merge then bakes
            // those pieces near world origin and leaves a water-shaped hole in the city.
            holder.SetParent(null, false);
            holder.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            holder.localScale = Vector3.one;
            // Composition always happens below an inactive holder. Even the synchronous
            // first-view warmup can contain thousands of MeshRenderers; registering them
            // at world origin while they are still being assembled fills Unity's graphics
            // command ring without producing a useful frame.
            view.Holder.SetActive(false);

            view.Content = new GameObject("Source").transform;
            view.Content.SetParent(holder, false);
            return view;
        }

        bool BuildImmediate(ResidentialBlockRecipe recipe)
        {
            var watch = Stopwatch.StartNew();
            var view = PrepareView(recipe, visible: false);
            try
            {
                var stood = recipe.Compose(view.Content,
                    (prefab, parent) => _prefabPool.Acquire(prefab, parent, view.Parts));
                watch.Stop();
                // Warmup is deliberately drained before the first rendered frame. Do not
                // credit its synchronous compose as a runtime incremental step.
                FinishBuild(view, stood, watch.ElapsedMilliseconds, 0L);
                return true;
            }
            catch (Exception error)
            {
                watch.Stop();
                FailBuild(view, error);
                return false;
            }
        }

        bool StartBuild(ResidentialBlockRecipe recipe)
        {
            if (BindingOwner != null && BindingOwner != this) return false;
            var view = PrepareView(recipe, visible: false);
            try
            {
                view.Compose = recipe.ComposeIncremental(view.Content,
                    (prefab, parent) => _prefabPool.Acquire(prefab, parent, view.Parts));
                _binding = view;
                _bindingCpuMs = 0;
                _bindingWorstStepMs = 0;
                BindingOwner = this;
                return true;
            }
            catch (Exception error)
            {
                FailBuild(view, error);
                return false;
            }
        }

        void PumpBinding(Stopwatch frame)
        {
            if (_binding == null || _binding.Compose == null) return;
            try
            {
                int steps = _config.CompositionStepsPerFrame;
                while (steps-- > 0 && frame.ElapsedMilliseconds < _config.BudgetMs)
                {
                    var step = Stopwatch.StartNew();
                    bool more = _binding.Compose.Step();
                    step.Stop();
                    long took = step.ElapsedMilliseconds;
                    _bindingCpuMs += took;
                    _bindingWorstStepMs = Math.Max(_bindingWorstStepMs, took);
                    if (more) continue;

                    var view = _binding;
                    var stood = view.Compose.Result;
                    view.Compose = null;
                    _binding = null;
                    if (BindingOwner == this) BindingOwner = null;
                    if (view.Recipe == null || view.ContentKey != view.Recipe.ContentKey)
                    {
                        DestroyPayload(view, countEviction: false);
                        ReturnHolder(view);
                        return;
                    }
                    FinishBuild(view, stood, _bindingCpuMs, _bindingWorstStepMs);
                    return;
                }
            }
            catch (Exception error)
            {
                var view = _binding;
                _binding = null;
                if (BindingOwner == this) BindingOwner = null;
                FailBuild(view, error);
            }
        }

        void FinishBuild(View view, ResidentialBlocks.Stood stood, long totalMs, long worstStepMs)
        {
            var finish = Stopwatch.StartNew();
            var recipe = view.Recipe;
            var holder = view.Holder.transform;

            // Binding is the RecyclerView placement step: once the origin-space
            // payload exists, carry the holder into this district and block slot.
            holder.SetParent(transform, false);
            holder.localPosition = new Vector3(recipe.LocalBounds.xMin, 0f, recipe.LocalBounds.yMin);
            holder.localRotation = Quaternion.identity;
            holder.localScale = Vector3.one;
            // The complete collision catalogue predates population and survives
            // visual eviction. Binding renderers never changes walkable ground.
            EnsureNavigation(recipe);
            // Begin attaching just outside the picture. At the configured fast-WASD
            // speed this lead covers the lower renderer budget before the kerb reaches
            // screen, so spreading graphics registration does not become visual pop-in.
            bool attached = Visible(recipe,
                Mathf.Min(_config.Prefetch, _config.RenderHysteresis));
            _night?.Register(view.Content);

            _transformScratch.Clear();
            view.Content.GetComponentsInChildren(true, _transformScratch);
            view.Objects = _transformScratch.Count;
            _transformScratch.Clear();
            _rendererScratch.Clear();
            view.Content.GetComponentsInChildren(true, _rendererScratch);
            view.Renderers = _rendererScratch.Count;
            view.AttachRenderers.Clear();
            for (int i = 0; i < _rendererScratch.Count; i++)
            {
                var renderer = _rendererScratch[i];
                // The shared perf rule does not let paving, decals and other flat
                // surfaces spend a second renderer command in the directional shadow
                // pass. Apply it before incremental attachment even when runtime mesh
                // merging is disabled; this is pixel-equivalent for an almost-flat
                // caster and materially reduces the Editor graphics ring pressure.
                if (renderer is MeshRenderer &&
                    renderer.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off &&
                    renderer.bounds.size.y < ScenePerf.FlatCasterHeight)
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (renderer != null && renderer.enabled) view.AttachRenderers.Add(renderer);
            }
            _rendererScratch.Clear();
            _sourceObjects += view.Objects;
            _sourceRenderers += view.Renderers;

            // Plan faults were already reported once when CoreDistrict produced the
            // recipe. Repeating them on every recycled bind allocates console strings and
            // stack traces while the camera moves; only a live composition failure is new.
            if (stood.Missing > 0)
                UnityEngine.Debug.LogWarning(
                    $"[BlockRecycler] {recipe.Name}: {stood.Missing} piece(s) missing", this);

            // The block's simulated businesses, bound to the pieces that just stood up.
            // Before the merge, because merging retires the individual unit roots and a
            // binding wants the shop's own mesh. Binding creates no business and changes
            // no record - see BusinessRuntime.BindBlockView.
            RoadDemoBuilder.BindBusinessViews(recipe.Id, view.Content);
            if (ConditionEligible(recipe.Name))
                view.Condition = new ResidentialConditionView(view.Content, recipe.Seed, _prefabPool, false);

            if (_config.mergeVisibleBlocks)
            {
                var roots = new[] { view.Content };
                ScenePerf.Optimise(roots, null, "BlockRecycler", log: false);
                ScenePerf.AssignCullLayers(roots, "BlockRecycler", log: false);
                view.Merged = new GameObject("Merged").transform;
                view.Merged.SetParent(view.Holder.transform, false);
                view.Merge = ScenePerf.MergeSteps(
                    new[] { ScenePerf.MergeRoot.Of(view.Content) }, view.Merged,
                    "BlockRecycler", releaseSourceCpu: false, log: false);
            }

            view.Active = false;
            view.Attached = false;
            _resident.Add(recipe.Id, view);
            if (attached) Activate(view);
            _built++;
            finish.Stop();
            totalMs += finish.ElapsedMilliseconds;
            worstStepMs = Math.Max(worstStepMs, finish.ElapsedMilliseconds);
            _lastBuildMs = totalMs;
            _worstBuildMs = Math.Max(_worstBuildMs, totalMs);
            _lastBuildStepMs = worstStepMs;
            _worstBuildStepMs = Math.Max(_worstBuildStepMs, worstStepMs);
        }

        void FailBuild(View view, Exception error)
        {
            UnityEngine.Debug.LogException(error, this);
            if (view?.Recipe != null) _retryAt[view.Recipe.Id] = Time.unscaledTime + 5f;
            if (view == null) return;
            DestroyPayload(view, countEviction: false);
            ReturnHolder(view);
        }

        void CancelBinding()
        {
            var view = _binding;
            _binding = null;
            _bindingCpuMs = 0;
            _bindingWorstStepMs = 0;
            if (BindingOwner == this) BindingOwner = null;
            if (view == null) return;
            DestroyPayload(view, countEviction: false);
            ReturnHolder(view);
        }

        View AcquireHolder()
        {
            if (_pool.Count > 0) return _pool.Pop();
            return new View { Holder = new GameObject("Block ViewHolder") };
        }

        void Activate(View view)
        {
            view.LastUsed = Time.frameCount;
            if (view.Active) return;
            view.Active = true;
            Streamed();
            BeginAttachment(view);
            _lamps?.Register(view.Content);
            _parkedGlow?.Register(view.Content);
        }

        void Deactivate(View view)
        {
            if (!view.Active) return;
            view.Active = false;
            Streamed();
            view.LastUsed = Time.frameCount;
            if (view.Recipe != null) _fallbacks?.ShowFallback(view.Recipe.Id);
            CancelAttachment(view);
            _lamps?.Unregister(view.Content);
            view.Holder.SetActive(false);
        }

        /// <summary>
        /// A cached holder is already bound, but a residential block may still own two
        /// thousand renderers. Keep the hierarchy active with its intended renderers
        /// disabled, then register only a bounded slice each frame. This is the graphics
        /// equivalent of RecyclerView attaching a holder after it has been bound.
        /// </summary>
        void BeginAttachment(View view)
        {
            CancelAttachment(view);
            if (view.Attached)
            {
                view.Holder.SetActive(true);
                if (view.Recipe != null) _fallbacks?.HideFallback(view.Recipe.Id);
                if (view.Merge == null && view.Recipe != null)
                    ShopDamage.RefreshPlanView(view.Recipe.Id);
                return;
            }
            if (view.Recipe != null) _fallbacks?.ShowFallback(view.Recipe.Id);
            view.AttachCursor = 0;
            for (int i = 0; i < view.AttachRenderers.Count; i++)
            {
                var renderer = view.AttachRenderers[i];
                if (renderer != null) renderer.enabled = false;
            }
            view.Holder.SetActive(true);
            if (view.AttachRenderers.Count == 0)
            {
                view.Attached = true;
                Streamed();
                if (view.Recipe != null) _fallbacks?.HideFallback(view.Recipe.Id);
                if (view.Merge == null && view.Recipe != null)
                    ShopDamage.RefreshPlanView(view.Recipe.Id);
                return;
            }
            view.Attaching = true;
            _attachments.Add(view);
        }

        void PumpAttachments(Stopwatch frame)
        {
            int left = _config.RendererAttachBudget;
            int index = 0;
            while (index < _attachments.Count && left > 0 &&
                   frame.ElapsedMilliseconds < _config.BudgetMs)
            {
                var view = _attachments[index];
                if (view == null || !view.Active || !view.Attaching)
                {
                    _attachments.RemoveAt(index);
                    continue;
                }
                while (view.AttachCursor < view.AttachRenderers.Count && left > 0 &&
                       frame.ElapsedMilliseconds < _config.BudgetMs)
                {
                    var renderer = view.AttachRenderers[view.AttachCursor++];
                    if (renderer != null) renderer.enabled = true;
                    left--;
                }
                if (view.AttachCursor >= view.AttachRenderers.Count)
                {
                    view.Attaching = false;
                    view.Attached = true;
                    Streamed();
                    if (view.Active && view.Recipe != null)
                        _fallbacks?.HideFallback(view.Recipe.Id);
                    if (view.Merge == null && view.Recipe != null)
                        ShopDamage.RefreshPlanView(view.Recipe.Id);
                    _attachments.RemoveAt(index);
                    continue;
                }
                index++;
            }
        }

        void CancelAttachment(View view)
        {
            if (view == null) return;
            if (view.Attaching)
            {
                _attachments.Remove(view);
                view.Attached = false;
            }
            view.Attaching = false;
            view.AttachCursor = 0;
        }

        int _conditionCursor;
        static bool ConditionEligible(string name) =>
            name.IndexOf("police", StringComparison.OrdinalIgnoreCase) < 0 &&
            name.IndexOf("nightclub", StringComparison.OrdinalIgnoreCase) < 0 &&
            name.IndexOf("discotheque", StringComparison.OrdinalIgnoreCase) < 0;

        void PumpConditions(Stopwatch frame)
        {
            if (frame.ElapsedMilliseconds >= _config.BudgetMs) return;
            _scratchViews.Clear();
            foreach (var pair in _resident)
                if (pair.Value.Active && pair.Value.Attached && pair.Value.Condition != null)
                    _scratchViews.Add(pair.Value);
            if (_scratchViews.Count == 0) return;
            int idle = 0, left = 96;
            while (left-- > 0 && idle < _scratchViews.Count && frame.ElapsedMilliseconds < _config.BudgetMs)
            {
                _conditionCursor %= _scratchViews.Count;
                var view = _scratchViews[_conditionCursor++];
                if (view.Condition.Step(view.Recipe.Neglect, CityDecorationSettings.Density)) idle = 0;
                else idle++;
            }
        }

        void PumpMerges(Stopwatch frame)
        {
            if (_resident.Count == 0 || frame.ElapsedMilliseconds >= _config.BudgetMs) return;
            _scratchViews.Clear();
            foreach (var pair in _resident)
                if (pair.Value.Active && pair.Value.Merge != null) _scratchViews.Add(pair.Value);
            if (_scratchViews.Count == 0) return;

            _mergeCursor %= _scratchViews.Count;
            int idle = 0;
            while (frame.ElapsedMilliseconds < _config.BudgetMs && idle < _scratchViews.Count)
            {
                var view = _scratchViews[_mergeCursor++ % _scratchViews.Count];
                if (view.Merge == null || (view.Condition != null && !view.Condition.Prepared)) { idle++; continue; }
                if (view.Merge.MoveNext()) { idle = 0; continue; }
                (view.Merge as IDisposable)?.Dispose();
                view.Merge = null;
                _merged++;
                if (view.Attached && view.Recipe != null)
                    ShopDamage.RefreshPlanView(view.Recipe.Id);
                idle++;
            }
        }

        void TrimCache()
        {
            // Retiring one complete holder can return hundreds of nested prefab roots.
            // RecyclerView releases one detached holder per frame; a while loop here
            // turned a camera jump or map return into one large hierarchy mutation.
            if (CachedViews > _config.CachedViews)
            {
                View oldest = null;
                foreach (var pair in _resident)
                {
                    var view = pair.Value;
                    if (view.Active || oldest != null && view.LastUsed >= oldest.LastUsed) continue;
                    oldest = view;
                }
                if (oldest != null) Evict(oldest);
            }
        }

        void MakeRoomForIncomingView()
        {
            if (_binding != null || _candidates.Count == 0 || CachedViews <= 0) return;
            // RecyclerView releases one detached holder before binding the incoming one.
            // Doing this after BuildCandidates made the composer instantiate a whole new
            // payload and only then return an equally useful cached payload to the pool.
            if (CachedViews < Mathf.Max(1, _config.CachedViews)) return;
            View oldest = null;
            foreach (var pair in _resident)
            {
                var view = pair.Value;
                if (view.Active || oldest != null && view.LastUsed >= oldest.LastUsed) continue;
                oldest = view;
            }
            if (oldest != null) Evict(oldest);
        }

        void OnModelChanged(ResidentialBlockRecipe recipe, ResidentialBlockChange change)
        {
            if (recipe == null)
            {
                ClearNavigation();
                CancelBinding();
                _scratchViews.Clear();
                foreach (var pair in _resident) _scratchViews.Add(pair.Value);
                for (int i = 0; i < _scratchViews.Count; i++) Evict(_scratchViews[i]);
                return;
            }
            // A navigation bake uses the same deterministic composer as a visual
            // bind, so retire an in-flight composition before opening another one.
            CancelBinding();
            if ((change & ResidentialBlockChange.Removed) != 0) RemoveNavigation(recipe.Id);
            else EnsureNavigation(recipe);
            _invalid.Add(recipe);
        }

        void ProcessInvalidations()
        {
            if (_invalid.Count == 0) return;
            foreach (var recipe in _invalid)
            {
                if (recipe != null && _binding?.Recipe?.Id == recipe.Id) CancelBinding();
                if (recipe != null && _resident.TryGetValue(recipe.Id, out var view)) Evict(view);
            }
            _invalid.Clear();
        }

        void EvictAllPayloads()
        {
            CancelBinding();
            _scratchViews.Clear();
            foreach (var pair in _resident) _scratchViews.Add(pair.Value);
            for (int i = 0; i < _scratchViews.Count; i++) Evict(_scratchViews[i]);
            _candidates.Clear();
        }

        /// <summary>
        /// The TurfMap owns the screen, not the residential payload memory. Breaking every
        /// prepared holder back into thousands of pooled prefab roots on the threshold frame
        /// bought no memory (the prefab pool retained them all) and caused the map-open hitch.
        /// Keep complete holders bound and disable their common ancestors. Returning to the
        /// street can then reactivate the same recipes; ordinary cache trimming retires at
        /// most one off-screen holder per later frame.
        /// </summary>
        void SuspendForMap()
        {
            _fallbacks?.SetSuppressed(true);
            _scratchViews.Clear();
            foreach (var pair in _resident) _scratchViews.Add(pair.Value);
            for (int i = 0; i < _scratchViews.Count; i++)
                if (_scratchViews[i].Active) Deactivate(_scratchViews[i]);
            _candidates.Clear();
        }

        void Evict(View view)
        {
            if (view == null) return;
            // through Deactivate first: its lamps leave the lighting ledger before they are destroyed
            if (view.Active) Deactivate(view);
            Streamed();
            if (view.Recipe != null) _resident.Remove(view.Recipe.Id);
            DestroyPayload(view, countEviction: true);
            ReturnHolder(view);
        }

        void DestroyPayload(View view, bool countEviction)
        {
            if (view?.Recipe != null) _fallbacks?.ShowFallback(view.Recipe.Id);
            CancelAttachment(view);
            // Disable one common ancestor before returning hundreds of nested prefab
            // roots. This turns renderer detachment into one hierarchy transition and
            // lets the pool retire surplus roots over later frames.
            if (view.Holder != null) view.Holder.SetActive(false);
            if (view.Compose != null)
            {
                view.Compose.Dispose();
                view.Compose = null;
            }
            if (view.Merge != null)
            {
                (view.Merge as IDisposable)?.Dispose();
                view.Merge = null;
            }
            if (view.Content != null)
            {
                _night?.Unregister(view.Content);
                _lamps?.Unregister(view.Content);
                _parkedGlow?.Unregister(view.Content);
                // Restore our material/density changes before any piece is leased to another block.
                view.Condition?.Dispose();
                view.Condition = null;
                _prefabPool?.ReleaseAll(view.Parts);
                view.Content.gameObject.SetActive(false);
                view.Content.SetParent(null, false);
                Destroy(view.Content.gameObject);
            }
            if (view.Merged != null)
            {
                foreach (var filter in view.Merged.GetComponentsInChildren<MeshFilter>(true))
                {
                    var mesh = filter.sharedMesh;
                    if (mesh != null && mesh.name.StartsWith("Merged ", StringComparison.Ordinal)) Destroy(mesh);
                }
                view.Merged.gameObject.SetActive(false);
                view.Merged.SetParent(null, false);
                Destroy(view.Merged.gameObject);
            }
            _sourceObjects = Mathf.Max(0, _sourceObjects - view.Objects);
            _sourceRenderers = Mathf.Max(0, _sourceRenderers - view.Renderers);
            if (countEviction) _evicted++;
            view.Content = null;
            view.Merged = null;
            view.Recipe = null;
            view.ContentKey = 0UL;
            view.Objects = 0;
            view.Renderers = 0;
            view.Active = false;
            view.Attached = false;
            view.AttachRenderers.Clear();
            view.Parts.Clear();
        }

        void ReturnHolder(View view)
        {
            if (view?.Holder == null) return;
            // NOTHING GOES BACK IN A POOL THAT IS BEING TAKEN AWAY. OnDestroy calls this
            // through CancelBinding, and re-parenting a holder onto a transform Unity is
            // already destroying is an ERROR - "Cannot set the parent of the GameObject
            // 'Block ViewHolder (pooled)' while its new parent is being destroyed" - and
            // the harness counts errors. It fired on the SECOND play of the core in one
            // editor session, when the old scene's recycler is torn down as the new one
            // loads, and it took a five-seed soak's third run down with it.
            if (_teardown)
            {
                view.Holder.SetActive(false);
                _pool.Push(view);
                return;
            }
            view.Holder.SetActive(false);
            view.Holder.name = "Block ViewHolder (pooled)";
            view.Holder.transform.SetParent(transform, false);
            view.Holder.transform.localPosition = Vector3.zero;
            view.Holder.transform.localRotation = Quaternion.identity;
            _pool.Push(view);
        }

        /// <summary>This recycler is going away: the pool is about to be destroyed with
        /// it, so nothing is re-parented back onto it. See <see cref="ReturnHolder"/>.
        /// </summary>
        bool _teardown;

        void OnDestroy()
        {
            _teardown = true;
            ClearNavigation();
            Instances.Remove(this);
            if (_model != null) _model.Changed -= OnModelChanged;
            CancelBinding();
            EvictAllPayloads();
            while (_pool.Count > 0)
            {
                var holder = _pool.Pop();
                if (holder?.Holder != null) Destroy(holder.Holder);
            }
            _prefabPool?.Dispose();
            _prefabPool = null;
            _attachments.Clear();
        }

        public static void AppendStats(StringBuilder into)
        {
            int recipes = 0, active = 0, cached = 0, pooled = 0, pending = 0, composing = 0;
            int objects = 0, renderers = 0, built = 0, evicted = 0, merged = 0;
            int fallbacks = 0, visibleFallbacks = 0;
            int partCapacity = 0, partReady = 0, partQueued = 0, partRetiring = 0;
            int partReused = 0, partMisses = 0;
            long last = 0, worst = 0, lastStep = 0, worstStep = 0;
            bool report = false;
            for (int i = Instances.Count - 1; i >= 0; i--)
            {
                var one = Instances[i];
                if (one == null) { Instances.RemoveAt(i); continue; }
                if (one._config != null && one._config.profileStreaming) report = true;
                recipes += one.RecipeCount; active += one.ActiveViews; cached += one.CachedViews;
                pooled += one.PooledHolders; pending += one.PendingViews; composing += one.ComposingViews;
                objects += one.SourceObjects; renderers += one.SourceRenderers;
                fallbacks += one.FallbackBlocks; visibleFallbacks += one.VisibleFallbackBlocks;
                built += one._built; evicted += one._evicted; merged += one._merged;
                partCapacity += one.PrefabPoolCapacity; partReady += one.AvailablePrefabParts;
                partQueued += one.PendingPrewarmParts;
                partRetiring += one.PendingPoolRetirements;
                partReused += one.ReusedPrefabParts; partMisses += one.RuntimePrefabMisses;
                last = Math.Max(last, one._lastBuildMs); worst = Math.Max(worst, one._worstBuildMs);
                lastStep = Math.Max(lastStep, one._lastBuildStepMs);
                worstStep = Math.Max(worstStep, one._worstBuildStepMs);
            }
            if (!report || recipes == 0) return;
            into.AppendLine($"block recycler  recipes {recipes}  active {active}  cached {cached}  pooled {pooled}  " +
                            $"pending {pending} composing {composing}  source objects {objects} renderers {renderers}  " +
                            $"fallbacks {visibleFallbacks}/{fallbacks} visible/total  " +
                            $"parts capacity/ready/queued/retiring/reused/missed {partCapacity}/{partReady}/{partQueued}/{partRetiring}/{partReused}/{partMisses}  " +
                            $"built/merged/evicted {built}/{merged}/{evicted}  " +
                            $"build ms total {last}/{worst} step {lastStep}/{worstStep}");
        }
    }
}
