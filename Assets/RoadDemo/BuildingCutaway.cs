using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RoadDemo
{
    /// <summary>
    /// One logical building as the street camera understands it. A building may be one
    /// catalogue renderer or a root made from a base, upper floors, roof and signs; the
    /// cutaway must move all of those renderers together or it leaves pieces floating.
    ///
    /// The full building remains physically present. While it occludes the street its mesh
    /// renderers use the shared opacity gradient; unsupported renderers retain the older
    /// shadows-only cut. Navigation, bullets, cover, sight and the building's collider keep
    /// exactly the same answer. Runtime source meshes are never read or sliced.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BuildingCutaway : MonoBehaviour
    {
        struct RendererState
        {
            public Renderer Renderer;
            public bool Enabled;
            public ShadowCastingMode Shadows;
            public MergedChunk Chunk;
            public bool HideCompletely;
        }

        const string ProxyName = "Cutaway ground cap";
        const int IgnoreRaycastLayer = 2;
        const float GroundCapThickness = 0.08f;
        const float GroundCapSink = 0.01f;
        const float GroundCapOverhang = 0.04f;
        const float FallbackMinimumPlanSpan = 2f;
        public const float DeclaredHeight = 3.5f;
        public const float DefaultGradientAmount = 1.37f;

        static readonly Dictionary<Collider, BuildingCutaway> ByCollider =
            new Dictionary<Collider, BuildingCutaway>();
        static readonly Dictionary<Renderer, BuildingCutaway> ByRenderer =
            new Dictionary<Renderer, BuildingCutaway>();
        static Material _proxyMaterial;

        readonly List<Renderer> _renderers = new List<Renderer>();
        readonly List<bool> _enabledAtConfigure = new List<bool>();
        readonly List<Collider> _colliders = new List<Collider>();
        readonly List<RendererState> _states = new List<RendererState>();
        readonly List<MergedChunk> _heldChunks = new List<MergedChunk>();

        GameObject _proxy;
        BuildingOpacityGradient _opacity;
        bool _configured;
        bool _registered;
        bool _cut;
        bool _usingGradient;
        bool _keptShadows;
        float _minimumHeight = DeclaredHeight;
        float _proxyHeight = 0.95f;
        float _gradientAmount;

        public bool IsCut => _cut;
        public bool UsesGradient => _usingGradient;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            ByCollider.Clear();
            ByRenderer.Clear();
        }

        /// <summary>Declare a root to be one building. Safe to call again when a pooled
        /// prefab is rebound; its original prefab renderers are collected afresh.</summary>
        public static BuildingCutaway Prepare(GameObject root, float minimumHeight = DeclaredHeight,
                                               float proxyHeight = 0.95f)
        {
            if (root == null) return null;
            var cutaway = root.GetComponent<BuildingCutaway>();
            if (cutaway == null) cutaway = root.AddComponent<BuildingCutaway>();
            return cutaway.Configure(minimumHeight, proxyHeight) ? cutaway : null;
        }

        /// <summary>The group already registered for this collider, or a conservative
        /// one-renderer fallback for old/authored buildings that have no explicit root.</summary>
        internal static BuildingCutaway Resolve(Collider collider, float minimumHeight,
                                                 float proxyHeight)
        {
            if (collider == null) return null;
            if (ByCollider.TryGetValue(collider, out var registered))
            {
                if (registered != null && registered._configured) return registered;
                ByCollider.Remove(collider);
            }

            var parent = collider.GetComponentInParent<BuildingCutaway>();
            if (parent != null && parent._configured)
            {
                if (!parent.isActiveAndEnabled) return null;
                parent.Register();
                return parent;
            }

            // The catalogue contract is collider and renderer side by side. Do not climb
            // arbitrary district/card roots here: an industrial parcel can contain several
            // buildings and must not disappear as one merely because its card has a box.
            if (!collider.TryGetComponent<MeshRenderer>(out var renderer))
                return null;

            var size = renderer.bounds.size;
            // Height alone makes a lamp post, utility pole or narrow tree look like a
            // building. Explicitly composed buildings above already bypass this fallback;
            // an undeclared legacy candidate must also have a building-sized footprint.
            if (size.y < minimumHeight ||
                Mathf.Min(size.x, size.z) < FallbackMinimumPlanSpan)
                return null;

            return Prepare(collider.gameObject, minimumHeight, proxyHeight);
        }

        public static bool Invisible(Collider collider)
        {
            if (collider == null || !ByCollider.TryGetValue(collider, out var cutaway))
                return false;
            return cutaway != null && cutaway._cut;
        }

        internal static bool RegisteredTo(Collider collider, BuildingCutaway cutaway) =>
            collider != null && cutaway != null &&
            ByCollider.TryGetValue(collider, out var current) && current == cutaway;

        /// <summary>Whether this renderer belongs to an explicitly declared logical
        /// building. The merge uses it to leave a reverse receipt for short roof/base
        /// pieces as well as the one tall renderer that originally identified a building.</summary>
        internal static bool Owns(Renderer renderer)
        {
            if (renderer == null || !ByRenderer.TryGetValue(renderer, out var cutaway))
                return false;
            if (cutaway != null && cutaway._configured) return true;
            ByRenderer.Remove(renderer);
            return false;
        }

        /// <summary>Restores the authored visual state on an offscreen duplicate without
        /// touching the live street renderer. Ledger photographs are records of the full
        /// building, never of the street camera's current cutaway.</summary>
        internal static bool RestoreUncutCopy(Renderer source, Renderer copy)
        {
            if (source == null || copy == null ||
                !ByRenderer.TryGetValue(source, out var cutaway) || cutaway == null ||
                !cutaway._cut)
                return false;

            for (int i = 0; i < cutaway._states.Count; i++)
            {
                var state = cutaway._states[i];
                if (state.Renderer != source)
                    continue;
                copy.shadowCastingMode = state.Shadows;
                if (cutaway._opacity != null)
                    cutaway._opacity.CopyOriginalMaterials(source, copy);
                return true;
            }
            return false;
        }

        bool Configure(float minimumHeight, float proxyHeight)
        {
            if (_cut) Restore();
            Unregister();
            UnregisterRenderers();
            _minimumHeight = Mathf.Max(0.5f, minimumHeight);
            _proxyHeight = Mathf.Clamp(proxyHeight, 0.35f, 1.5f);

            _renderers.Clear();
            GetComponentsInChildren(true, _renderers);
            for (int i = _renderers.Count - 1; i >= 0; i--)
            {
                var renderer = _renderers[i];
                if (renderer == null || (_proxy != null &&
                    (renderer.transform == _proxy.transform ||
                     renderer.transform.IsChildOf(_proxy.transform))))
                    _renderers.RemoveAt(i);
            }
            _enabledAtConfigure.Clear();
            for (int i = 0; i < _renderers.Count; i++)
                _enabledAtConfigure.Add(_renderers[i] != null && _renderers[i].enabled);

            bool tall = false;
            for (int i = 0; i < _renderers.Count; i++)
                if (_renderers[i].bounds.size.y >= _minimumHeight)
                {
                    tall = true;
                    break;
                }

            _colliders.Clear();
            GetComponentsInChildren(true, _colliders);
            for (int i = _colliders.Count - 1; i >= 0; i--)
            {
                var collider = _colliders[i];
                if (collider == null || collider.isTrigger ||
                    (_proxy != null && (collider.transform == _proxy.transform ||
                                        collider.transform.IsChildOf(_proxy.transform))))
                    _colliders.RemoveAt(i);
            }

            _configured = tall && _renderers.Count > 0 && _colliders.Count > 0;
            if (_configured)
            {
                if (_opacity == null)
                {
                    _opacity = GetComponent<BuildingOpacityGradient>();
                    if (_opacity == null) _opacity = gameObject.AddComponent<BuildingOpacityGradient>();
                }
                _opacity.PrepareForRecycledBinding(_renderers);
                RegisterRenderers();
                if (isActiveAndEnabled) Register();
            }
            return _configured;
        }

        void OnEnable()
        {
            if (_configured) Register();
        }

        void OnDisable()
        {
            if (_cut) Restore();
            Unregister();
        }

        void OnDestroy()
        {
            if (_cut) Restore();
            Unregister();
            UnregisterRenderers();
        }

        void RegisterRenderers()
        {
            for (int i = 0; i < _renderers.Count; i++)
                if (_renderers[i] != null) ByRenderer[_renderers[i]] = this;
        }

        void UnregisterRenderers()
        {
            for (int i = 0; i < _renderers.Count; i++)
            {
                var renderer = _renderers[i];
                if (ReferenceEquals(renderer, null)) continue;
                if (ByRenderer.TryGetValue(renderer, out var owner) && owner == this)
                    ByRenderer.Remove(renderer);
            }
        }

        void Register()
        {
            if (!_configured || !isActiveAndEnabled) return;
            for (int i = 0; i < _colliders.Count; i++)
                if (_colliders[i] != null) ByCollider[_colliders[i]] = this;
            _registered = true;
        }

        void Unregister()
        {
            if (!_registered) return;
            for (int i = 0; i < _colliders.Count; i++)
            {
                var collider = _colliders[i];
                if (ReferenceEquals(collider, null)) continue;
                if (ByCollider.TryGetValue(collider, out var owner) && owner == this)
                    ByCollider.Remove(collider);
            }
            _registered = false;
        }

        /// <summary>Switch the visual state. Mesh renderers use the recyclable gradient;
        /// if it is unavailable, the former shadows-only treatment remains the fallback.
        /// False means the chunk merge has not finished and the caller should ask again on
        /// its next sweep.</summary>
        internal bool Cut(bool keepShadows, float proxyHeight,
                          float gradientAmount = DefaultGradientAmount)
        {
            if (!_configured) return false;
            proxyHeight = Mathf.Clamp(proxyHeight, 0.35f, 1.5f);
            gradientAmount = Mathf.Clamp(gradientAmount, 0f, 2f);
            if (_cut && _keptShadows == keepShadows &&
                Mathf.Abs(_proxyHeight - proxyHeight) < 0.001f &&
                Mathf.Abs(_gradientAmount - gradientAmount) < 0.001f)
                return true;
            if (_cut) Restore();

            _proxyHeight = proxyHeight;
            _states.Clear();
            _heldChunks.Clear();
            bool anyDrawable = false;

            for (int i = 0; i < _renderers.Count; i++)
            {
                var renderer = _renderers[i];
                if (renderer == null) continue;
                // Always take our own lease when this renderer belongs to a merged
                // chunk. Another cut building may already have made every source piece
                // visible; testing renderer.enabled here would then miss the shared
                // owner and let that other building's Restore fold this one back in.
                var chunk = MergedChunk.Of(renderer);
                // Recycler attachment deliberately disables a building's renderers in
                // slices. Do not snapshot that temporary half-attached state: once the
                // merge starts, restoring it would bake a hole into the block.
                if (i < _enabledAtConfigure.Count && _enabledAtConfigure[i] &&
                    !renderer.enabled && chunk == null)
                {
                    _states.Clear();
                    return false;
                }
                _states.Add(new RendererState
                {
                    Renderer = renderer,
                    Enabled = renderer.enabled,
                    Shadows = renderer.shadowCastingMode,
                    Chunk = chunk,
                    HideCompletely = ResidentialBlocks.IsGeneratedStorefrontVisual(
                        renderer.transform, transform),
                });
                anyDrawable |= renderer.enabled || chunk != null;
            }
            if (!anyDrawable)
            {
                _states.Clear();
                return false;
            }

            // A merged chunk must first stand back in its source pieces. Hold each chunk
            // once even when this building contributed several materials/renderers to it.
            for (int i = 0; i < _states.Count; i++)
            {
                var chunk = _states[i].Chunk;
                if (chunk == null || _heldChunks.Contains(chunk)) continue;
                if (!chunk.Hold())
                {
                    for (int j = _heldChunks.Count - 1; j >= 0; j--) _heldChunks[j].Release();
                    _heldChunks.Clear();
                    _states.Clear();
                    return false;
                }
                _heldChunks.Add(chunk);
            }

            bool useGradient = gradientAmount > 0.001f && _opacity != null &&
                               _opacity.Ready && _opacity.RefreshBounds() && _opacity.Set(
                                   gradientAmount, BuildingOpacityGradient.Profile.Vertical);
            for (int i = 0; i < _states.Count; i++)
            {
                var state = _states[i];
                var renderer = state.Renderer;
                if (renderer == null || (!state.Enabled && state.Chunk == null)) continue;
                if (state.HideCompletely)
                {
                    renderer.enabled = false;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    continue;
                }
                if (useGradient && _opacity.Handles(renderer))
                {
                    // An authored shadows-only renderer must not become a visible facade.
                    if (state.Shadows == ShadowCastingMode.ShadowsOnly)
                    {
                        renderer.enabled = keepShadows;
                        renderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                    }
                    else
                    {
                        renderer.enabled = true;
                        renderer.shadowCastingMode = keepShadows
                            ? state.Shadows
                            : ShadowCastingMode.Off;
                    }
                    continue;
                }
                if (keepShadows && state.Shadows != ShadowCastingMode.Off)
                {
                    renderer.enabled = true;
                    renderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                }
                else renderer.enabled = false;
            }

            _keptShadows = keepShadows;
            _gradientAmount = gradientAmount;
            _usingGradient = useGradient;
            _cut = true;
            // Residential cells deliberately omit paving beneath real buildings. Once the
            // facade fades that can expose the island water, so keep one pooled, very thin
            // opaque cap at the building's actual ground plane. It is visual-only and is
            // never included in the gradient renderer set or collider lookup.
            if (useGradient && EnsureProxy() && PlaceProxy()) _proxy.SetActive(true);
            else if (_proxy != null) _proxy.SetActive(false);
            return true;
        }

        internal void Restore()
        {
            if (_proxy != null) _proxy.SetActive(false);
            if (_opacity != null && _opacity.GradientMaterialsActive)
                _opacity.Set(0f);
            if (!_cut && _states.Count == 0) return;

            // Restore source state before releasing a chunk. Its last Release may turn all
            // source pieces off and the merged draw back on, which is exactly the final state.
            for (int i = 0; i < _states.Count; i++)
            {
                var state = _states[i];
                var renderer = state.Renderer;
                if (renderer == null) continue;
                renderer.shadowCastingMode = state.Shadows;
                if (state.Chunk == null) renderer.enabled = state.Enabled;
                else renderer.enabled = true;
            }
            for (int i = _heldChunks.Count - 1; i >= 0; i--)
                if (_heldChunks[i] != null) _heldChunks[i].Release();

            _heldChunks.Clear();
            _states.Clear();
            _usingGradient = false;
            _gradientAmount = 0f;
            _cut = false;
        }

        bool EnsureProxy()
        {
            if (_proxy != null) return true;
            var material = ProxyMaterial();
            if (material == null) return false;

            _proxy = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _proxy.name = ProxyName;
            _proxy.SetActive(false);
            _proxy.layer = IgnoreRaycastLayer;
            var collider = _proxy.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
                Destroy(collider);
            }
            var renderer = _proxy.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            _proxy.transform.SetParent(transform, true);
            return true;
        }

        bool PlaceProxy()
        {
            if (_proxy == null || !TryFootprint(out var bounds)) return false;
            float width = Mathf.Max(0.4f, bounds.size.x + GroundCapOverhang * 2f);
            float depth = Mathf.Max(0.4f, bounds.size.z + GroundCapOverhang * 2f);
            float ground = bounds.min.y;

            var proxy = _proxy.transform;
            proxy.SetParent(null, true);
            proxy.SetPositionAndRotation(
                new Vector3(bounds.center.x,
                    ground - GroundCapThickness * 0.5f - GroundCapSink,
                    bounds.center.z),
                Quaternion.identity);
            proxy.localScale = new Vector3(width, GroundCapThickness, depth);
            proxy.SetParent(transform, true);
            return true;
        }

        bool TryFootprint(out Bounds bounds)
        {
            bounds = default;
            bool any = false;

            // Catalogue buildings normally carry an exact lot collider on their root.
            // Prefer it over signs, fire escapes and storefront prop colliders below it;
            // composed modular buildings fall through to the combined-child bounds.
            for (int i = 0; i < _colliders.Count; i++)
            {
                var collider = _colliders[i];
                if (collider == null || !collider.enabled || collider.isTrigger ||
                    collider.transform != transform) continue;
                if (!any) { bounds = collider.bounds; any = true; }
                else bounds.Encapsulate(collider.bounds);
            }
            if (any) return bounds.size.x > 0.05f && bounds.size.z > 0.05f;

            for (int i = 0; i < _colliders.Count; i++)
            {
                var collider = _colliders[i];
                if (collider == null || !collider.enabled || collider.isTrigger) continue;
                if (!any) { bounds = collider.bounds; any = true; }
                else bounds.Encapsulate(collider.bounds);
            }
            if (any) return bounds.size.x > 0.05f && bounds.size.z > 0.05f;

            for (int i = 0; i < _renderers.Count; i++)
            {
                var renderer = _renderers[i];
                if (renderer == null) continue;
                if (!any) { bounds = renderer.bounds; any = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            return any && bounds.size.x > 0.05f && bounds.size.z > 0.05f;
        }

        static Material ProxyMaterial()
        {
            if (_proxyMaterial != null) return _proxyMaterial;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null) return null;
            _proxyMaterial = new Material(shader)
            {
                name = "Building Cutaway Ground Cap",
                color = new Color(0.19f, 0.19f, 0.175f, 1f),
                hideFlags = HideFlags.HideAndDontSave,
                enableInstancing = true,
            };
            if (_proxyMaterial.HasProperty("_BaseColor"))
                _proxyMaterial.SetColor("_BaseColor", _proxyMaterial.color);
            if (_proxyMaterial.HasProperty("_Smoothness"))
                _proxyMaterial.SetFloat("_Smoothness", 0.08f);
            return _proxyMaterial;
        }
    }
}
