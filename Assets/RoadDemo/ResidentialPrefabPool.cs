using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace RoadDemo
{
    /// <summary>
    /// The recyclable payload behind <see cref="CityBlockRecycler"/>. A ViewHolder alone
    /// is cheap; the hundreds of objects inside one residential prefab are not. This pool
    /// keeps those prefab roots alive when a block is rebound, restores every renderer the
    /// merge/night passes touched, and hands the hierarchy to the next recipe without an
    /// Object.Instantiate burst.
    /// </summary>
    internal sealed class ResidentialPrefabPool : IDisposable
    {
        readonly struct RendererState
        {
            public readonly Renderer Renderer;
            public readonly Material[] Materials;
            public readonly bool Enabled;
            public readonly bool ReceiveShadows;
            public readonly ShadowCastingMode Shadows;
            public readonly int Layer;

            public RendererState(Renderer renderer)
            {
                Renderer = renderer;
                Materials = renderer.sharedMaterials;
                Enabled = renderer.enabled;
                ReceiveShadows = renderer.receiveShadows;
                Shadows = renderer.shadowCastingMode;
                Layer = renderer.gameObject.layer;
            }
        }

        sealed class Entry
        {
            public GameObject Prefab;
            public GameObject Instance;
            public Vector3 LocalPosition;
            public Quaternion LocalRotation;
            public Vector3 LocalScale;
            public bool Active;
            public RendererState[] Renderers;
        }

        readonly Dictionary<GameObject, Stack<Entry>> _available =
            new Dictionary<GameObject, Stack<Entry>>();
        readonly Dictionary<GameObject, Entry> _leased =
            new Dictionary<GameObject, Entry>();
        readonly Dictionary<GameObject, int> _capacity =
            new Dictionary<GameObject, int>();
        readonly Dictionary<GameObject, int> _runtimeMissesByPrefab =
            new Dictionary<GameObject, int>();
        readonly Dictionary<GameObject, int> _scheduledTargets =
            new Dictionary<GameObject, int>();
        readonly Queue<GameObject> _scheduled = new Queue<GameObject>();
        readonly Queue<Entry> _retiring = new Queue<Entry>();
#if UNITY_EDITOR
        readonly Queue<GameObject> _assetScans = new Queue<GameObject>();
        readonly HashSet<GameObject> _assetScanSet = new HashSet<GameObject>();
        readonly Queue<Texture> _textureWarm = new Queue<Texture>();
        readonly HashSet<Texture> _textureWarmSet = new HashSet<Texture>();
        readonly List<Texture> _warmTextureReferences = new List<Texture>();
#endif
        readonly Transform _root;
        bool _disposed;
        bool _runtime;
        int _retainedLimit = int.MaxValue;

        public ResidentialPrefabPool(Transform owner)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            _root = new GameObject("Residential prefab pool").transform;
            _root.SetParent(owner, false);
            _root.gameObject.SetActive(false);
        }

        public int Capacity { get; private set; }
        public int Available { get; private set; }
        public int Reused { get; private set; }
        public int RuntimeMisses { get; private set; }
        public int Prewarmed { get; private set; }
        public int PendingPrewarmParts { get; private set; }
        public int PendingRetirements => _retiring.Count;
        public string LargestRuntimeMissPrefab { get; private set; }
        public int LargestRuntimeMissRenderers { get; private set; }
        public int RuntimeMissTypes => _runtimeMissesByPrefab.Count;
        public int PendingAssetWarm
        {
            get
            {
#if UNITY_EDITOR
                return _assetScans.Count + _textureWarm.Count;
#else
                return 0;
#endif
            }
        }

        /// <summary>Allocation is intentional here: this is only read by the explicit
        /// editor audit, never by the recycler's frame loop.</summary>
        public string RuntimeMissSummary
        {
            get
            {
                if (_runtimeMissesByPrefab.Count == 0) return string.Empty;
                var misses = new List<KeyValuePair<GameObject, int>>(_runtimeMissesByPrefab);
                misses.Sort((a, b) => b.Value.CompareTo(a.Value));
                var text = new StringBuilder();
                for (int i = 0; i < misses.Count && i < 12; i++)
                {
                    if (i > 0) text.Append(", ");
                    var prefab = misses[i].Key;
                    _capacity.TryGetValue(prefab, out int current);
                    int startup = Mathf.Max(0, current - misses[i].Value);
                    text.Append(prefab != null ? prefab.name : "<destroyed>");
                    text.Append(':').Append(misses[i].Value)
                        .Append(" miss / ").Append(startup).Append(" warm");
                }
                return text.ToString();
            }
        }

        /// <summary>
        /// Limit only inactive retained roots. A currently visible/cache holder is never
        /// torn apart to satisfy the cap; when that holder is recycled, surplus roots are
        /// placed on the gradual retirement queue instead of producing one Destroy burst.
        /// </summary>
        public void SetRetainedLimit(int maxParts)
        {
            _retainedLimit = Mathf.Max(500, maxParts);
        }

        /// <summary>
        /// Acquire one raised prefab for a recipe. A null parent is Composer's temporary
        /// measurement instance; it must remain ordinary because Composer destroys it
        /// immediately after reading its bounds.
        /// </summary>
        public GameObject Acquire(GameObject prefab, Transform parent, List<GameObject> lease)
        {
            if (prefab == null) return null;
            if (parent == null) return UnityEngine.Object.Instantiate(prefab);
            if (_disposed) throw new ObjectDisposedException(nameof(ResidentialPrefabPool));

            Entry entry = null;
            if (_available.TryGetValue(prefab, out var ready))
                while (ready.Count > 0 && entry == null)
                {
                    var candidate = ready.Pop();
                    Available = Mathf.Max(0, Available - 1);
                    if (candidate?.Instance != null) entry = candidate;
                    else Lose(prefab);
                }

            if (entry == null)
            {
                entry = Create(prefab);
                if (_runtime)
                {
                    RuntimeMisses++;
                    _runtimeMissesByPrefab.TryGetValue(prefab, out int misses);
                    _runtimeMissesByPrefab[prefab] = misses + 1;
                    int renderers = entry.Renderers?.Length ?? 0;
                    if (renderers > LargestRuntimeMissRenderers)
                    {
                        LargestRuntimeMissRenderers = renderers;
                        LargestRuntimeMissPrefab = prefab.name;
                    }
                }
            }
            else Reused++;

            Restore(entry);
            var instance = entry.Instance;
            instance.transform.SetParent(parent, false);
            instance.SetActive(entry.Active);
            _leased[instance] = entry;
            lease?.Add(instance);
            return instance;
        }

        /// <summary>Build capacity during scene startup, before camera input can bind.</summary>
        public void EnsureCapacity(GameObject prefab, int target)
        {
            if (_disposed || prefab == null) return;
            ScheduleAssetWarm(prefab);
            target = Mathf.Max(0, target);
            _capacity.TryGetValue(prefab, out int have);
            if (!_available.TryGetValue(prefab, out var ready))
                _available[prefab] = ready = new Stack<Entry>(Mathf.Max(1, target));
            while (have < target)
            {
                var entry = Create(prefab);
                entry.Instance.SetActive(false);
                entry.Instance.transform.SetParent(_root, false);
                ready.Push(entry);
                Available++;
                Prewarmed++;
                have++;
            }
        }

        /// <summary>
        /// Queue capacity instead of creating it in one editor frame. Unity uploads a
        /// prefab hierarchy's renderer state even below an inactive root; creating the
        /// whole headroom in Start can exhaust the Graphics Ring Buffer. The queue keeps
        /// the same eventual pool size but lets the render thread retire those uploads.
        /// </summary>
        public int ScheduleCapacity(GameObject prefab, int target, int totalLimit)
        {
            if (_disposed || prefab == null || totalLimit <= 0) return 0;
            ScheduleAssetWarm(prefab);
            _capacity.TryGetValue(prefab, out int have);
            _scheduledTargets.TryGetValue(prefab, out int scheduled);
            int baseline = Mathf.Max(have, scheduled);
            int room = Mathf.Max(0, totalLimit - (Capacity + PendingPrewarmParts));
            int accepted = Mathf.Min(Mathf.Max(0, target - baseline), room);
            if (accepted <= 0) return 0;
            if (!_scheduledTargets.ContainsKey(prefab)) _scheduled.Enqueue(prefab);
            _scheduledTargets[prefab] = baseline + accepted;
            PendingPrewarmParts += accepted;
            return accepted;
        }

        /// <summary>
        /// The first visible holders are a representative bill of materials for paving,
        /// kerbs and dressing. Scale that observed inventory to the configured holder
        /// window during startup; heavy unit types not present there are added separately
        /// from recipe data by CityBlockRecycler.
        /// </summary>
        public int ScaleObservedCapacity(int observedViews, int targetViews, int totalLimit)
        {
            if (_disposed || observedViews <= 0 || targetViews <= observedViews ||
                totalLimit <= Capacity) return 0;
            var observed = new List<KeyValuePair<GameObject, int>>(_capacity);
            observed.Sort((a, b) => b.Value.CompareTo(a.Value));
            int before = Capacity;
            for (int i = 0; i < observed.Count && Capacity < totalLimit; i++)
            {
                var pair = observed[i];
                int target = Mathf.CeilToInt(pair.Value * (targetViews / (float)observedViews));
                target = Mathf.Min(target, pair.Value + totalLimit - Capacity);
                EnsureCapacity(pair.Key, target);
            }
            return Capacity - before;
        }

        /// <summary>
        /// Spend any remaining startup allowance as proportional headroom across the
        /// prefabs the generator actually requested. Two blocks are enough to discover
        /// the common paving and dressing catalogue, but their particular mix is not a
        /// worst case. The headroom absorbs that mix variance without hard-coding prop
        /// names, so a future generator automatically benefits from the same policy.
        /// </summary>
        public int ScheduleObservedCapacity(int totalLimit)
        {
            if (_disposed || Capacity <= 0 || totalLimit <= Capacity + PendingPrewarmParts) return 0;
            var observed = new List<KeyValuePair<GameObject, int>>(_capacity);
            observed.Sort((a, b) => b.Value.CompareTo(a.Value));
            int before = PendingPrewarmParts;
            float scale = totalLimit / (float)Capacity;
            for (int i = 0; i < observed.Count && Capacity + PendingPrewarmParts < totalLimit; i++)
            {
                var pair = observed[i];
                int target = Mathf.CeilToInt(pair.Value * scale);
                ScheduleCapacity(pair.Key, target, totalLimit);
            }

            // Ceil distribution normally fills the allowance. If rounding left a few
            // slots, give them to the most frequent request types first.
            int guard = 0;
            while (observed.Count > 0 && Capacity + PendingPrewarmParts < totalLimit &&
                   guard++ < totalLimit)
            {
                var prefab = observed[(guard - 1) % observed.Count].Key;
                _capacity.TryGetValue(prefab, out int have);
                _scheduledTargets.TryGetValue(prefab, out int target);
                ScheduleCapacity(prefab, Mathf.Max(have, target) + 1, totalLimit);
            }
            return PendingPrewarmParts - before;
        }

        /// <summary>Warm a few queued roots inside a small CPU budget. Returns how many
        /// instances were created this call.</summary>
        public int PrewarmStep(int maxParts, int budgetMs)
        {
            if (_disposed || maxParts <= 0) return 0;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            int made = 0;
            while (made < maxParts && _retiring.Count > 0)
            {
                var entry = _retiring.Dequeue();
                if (entry?.Instance != null) UnityEngine.Object.Destroy(entry.Instance);
                made++;
                if (clock.ElapsedMilliseconds >= Mathf.Max(1, budgetMs)) break;
            }
            while (made < maxParts && _scheduled.Count > 0)
            {
                var prefab = _scheduled.Dequeue();
                if (prefab == null || !_scheduledTargets.TryGetValue(prefab, out int target))
                    continue;
                _capacity.TryGetValue(prefab, out int have);
                if (have < target)
                {
                    EnsureCapacity(prefab, have + 1);
                    made++;
                    _capacity.TryGetValue(prefab, out have);
                }
                if (have < target) _scheduled.Enqueue(prefab);
                else _scheduledTargets.Remove(prefab);
                if (made > 0 && clock.ElapsedMilliseconds >= Mathf.Max(1, budgetMs)) break;
            }
#if UNITY_EDITOR
            WarmAssetDependencies(maxParts, budgetMs, clock);
#endif
            return made;
        }

        void ScheduleAssetWarm(GameObject prefab)
        {
#if UNITY_EDITOR
            if (prefab != null && _assetScanSet.Add(prefab)) _assetScans.Enqueue(prefab);
#endif
        }

#if UNITY_EDITOR
        /// <summary>
        /// AssetDatabase resolves a prefab while its textures may remain lazy until the
        /// first renderer is enabled. Resolve and create the native texture handles in
        /// the same gradual startup queue, otherwise the first WASD visit pays the load
        /// and can fill the Editor graphics ring in one frame.
        /// </summary>
        void WarmAssetDependencies(int maxWork, int budgetMs,
                                   System.Diagnostics.Stopwatch clock)
        {
            int work = 0;
            int budget = Mathf.Max(1, budgetMs);
            while (work < maxWork && clock.ElapsedMilliseconds < budget)
            {
                if (_textureWarm.Count > 0)
                {
                    var texture = _textureWarm.Dequeue();
                    if (texture != null)
                    {
                        texture.GetNativeTexturePtr();
                        _warmTextureReferences.Add(texture);
                    }
                    work++;
                    continue;
                }

                if (_assetScans.Count == 0) break;
                var prefab = _assetScans.Dequeue();
                if (prefab != null)
                {
                    string path = UnityEditor.AssetDatabase.GetAssetPath(prefab);
                    if (!string.IsNullOrEmpty(path))
                        foreach (var dependency in UnityEditor.AssetDatabase.GetDependencies(path, true))
                        {
                            var texture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture>(dependency);
                            if (texture != null && _textureWarmSet.Add(texture))
                                _textureWarm.Enqueue(texture);
                        }
                }
                work++;
            }
        }
#endif

        public void ReleaseAll(List<GameObject> lease)
        {
            if (lease == null) return;
            // Children are sometimes raised under another raised prefab. Return them
            // first so reparenting the parent cannot carry a leased child into its pool.
            for (int i = lease.Count - 1; i >= 0; i--) Release(lease[i]);
            lease.Clear();
        }

        /// <summary>Only misses after startup are visible hitch risks.</summary>
        public void BeginRuntime()
        {
            _runtime = true;
            RuntimeMisses = 0;
            Reused = 0;
            LargestRuntimeMissPrefab = null;
            LargestRuntimeMissRenderers = 0;
            _runtimeMissesByPrefab.Clear();
        }

        void Release(GameObject instance)
        {
            if (instance == null || !_leased.TryGetValue(instance, out var entry)) return;
            _leased.Remove(instance);
            instance.SetActive(false);
            instance.transform.SetParent(_root, false);
            if (Capacity > _retainedLimit)
            {
                Lose(entry.Prefab);
                _retiring.Enqueue(entry);
                return;
            }
            // Do not restore hundreds of renderer/material states while evicting a
            // complete block. Acquire restores one prefab at a time under the
            // composer's hard step cap, while this inactive pooled entry is invisible.
            if (!_available.TryGetValue(entry.Prefab, out var ready))
                _available[entry.Prefab] = ready = new Stack<Entry>();
            ready.Push(entry);
            Available++;
        }

        Entry Create(GameObject prefab)
        {
            var instance = UnityEngine.Object.Instantiate(prefab, _root);
            MakeDynamic(instance.transform);
            var entry = new Entry
            {
                Prefab = prefab,
                Instance = instance,
                LocalPosition = prefab.transform.localPosition,
                LocalRotation = prefab.transform.localRotation,
                LocalScale = prefab.transform.localScale,
                Active = prefab.activeSelf,
            };
            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            entry.Renderers = new RendererState[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
                entry.Renderers[i] = new RendererState(renderers[i]);

            _capacity.TryGetValue(prefab, out int count);
            _capacity[prefab] = count + 1;
            Capacity++;
            if (_scheduledTargets.TryGetValue(prefab, out int target) && count < target)
                PendingPrewarmParts = Mathf.Max(0, PendingPrewarmParts - 1);
            return entry;
        }

        static void MakeDynamic(Transform root)
        {
            // A pooled hierarchy moves between holders. Keeping authoring-time Static
            // flags makes Unity rebuild static-batch meshes as the camera scrolls.
            root.gameObject.isStatic = false;
            for (int i = 0; i < root.childCount; i++) MakeDynamic(root.GetChild(i));
        }

        static void Restore(Entry entry)
        {
            var instance = entry.Instance;
            if (instance == null) return;
            instance.SetActive(false);
            instance.name = entry.Prefab != null ? entry.Prefab.name : "Residential pooled prefab";
            var transform = instance.transform;
            transform.localPosition = entry.LocalPosition;
            transform.localRotation = entry.LocalRotation;
            transform.localScale = entry.LocalScale;
            var renderers = entry.Renderers;
            for (int i = 0; renderers != null && i < renderers.Length; i++)
            {
                var state = renderers[i];
                var renderer = state.Renderer;
                if (renderer == null) continue;
                renderer.gameObject.layer = state.Layer;
                renderer.enabled = state.Enabled;
                renderer.receiveShadows = state.ReceiveShadows;
                renderer.shadowCastingMode = state.Shadows;
                renderer.sharedMaterials = state.Materials;
            }
        }

        void Lose(GameObject prefab)
        {
            if (_capacity.TryGetValue(prefab, out int count))
                _capacity[prefab] = Mathf.Max(0, count - 1);
            Capacity = Mathf.Max(0, Capacity - 1);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _available.Clear();
            _leased.Clear();
            _capacity.Clear();
            _runtimeMissesByPrefab.Clear();
            _scheduledTargets.Clear();
            _scheduled.Clear();
            _retiring.Clear();
#if UNITY_EDITOR
            _assetScans.Clear();
            _assetScanSet.Clear();
            _textureWarm.Clear();
            _textureWarmSet.Clear();
            _warmTextureReferences.Clear();
#endif
            Capacity = Available = PendingPrewarmParts = 0;
            if (_root != null) UnityEngine.Object.Destroy(_root.gameObject);
        }
    }
}
