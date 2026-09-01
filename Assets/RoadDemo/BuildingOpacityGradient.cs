using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// A reversible visual-only opacity treatment for one logical building.
    ///
    /// The building keeps its original renderers, colliders and full shadow. While the
    /// effect is active, only its material set is swapped for the transparent gradient
    /// shader. At zero effect the exact original shared materials are restored, so the
    /// ordinary city rendering path pays nothing while the building is not an occluder.
    ///
    /// Pooled residential roots retain this component. A recycled bind refreshes only
    /// renderer/material references and world bounds; gradient variants are cached per
    /// source material for the lifetime of that pooled instance and are not recreated on
    /// every block bind.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BuildingOpacityGradient : MonoBehaviour
    {
        public enum Profile
        {
            Uniform,
            Vertical,
        }

        const string ShaderName = "LivingCity/Occlusion Gradient";
        const float ActiveThreshold = 0.001f;
        public const float DefaultGradientStartHeight = 5f;

        static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int Cutoff = Shader.PropertyToID("_Cutoff");
        static readonly int FadeAmount = Shader.PropertyToID("_FadeAmount");
        static readonly int GradientMode = Shader.PropertyToID("_GradientMode");
        static readonly int GradientStartHeight = Shader.PropertyToID("_GradientStartHeight");
        static readonly int BoundsMinY = Shader.PropertyToID("_BoundsMinY");
        static readonly int BoundsHeight = Shader.PropertyToID("_BoundsHeight");
        static readonly int BoundsCenter = Shader.PropertyToID("_BoundsCenter");
        static Shader _sharedShader;

        sealed class RendererState
        {
            public MeshRenderer Renderer;
            public Material[] Original;
            public Material[] Gradient;
        }

        readonly List<RendererState> _states = new List<RendererState>();
        readonly HashSet<Renderer> _renderers = new HashSet<Renderer>();
        readonly Dictionary<Material, Material> _variants =
            new Dictionary<Material, Material>();
        readonly List<Material> _materialScratch = new List<Material>();

        Shader _shader;
        Material _nullSourceVariant;
        bool _prepared;
        bool _gradientMaterialsActive;
        bool _loggedShaderError;
        bool _loggedNoRenderers;
        float _boundsMinY;
        float _boundsHeight = 1f;
        Vector3 _boundsCenter;
        float _lastAmount = -1f;
        float _lastGradientStartHeight = -1f;
        Profile _lastProfile;

        public bool Ready => _prepared && _states.Count > 0;
        public bool GradientMaterialsActive => _gradientMaterialsActive;
        public float Amount => Mathf.Max(0f, _lastAmount);
        public Profile CurrentProfile => _lastProfile;

        /// <summary>
        /// Captures every mesh renderer below this logical building and prepares its
        /// gradient-material counterpart. The demo uses this path; recycled city buildings
        /// pass the renderer list already collected by <see cref="BuildingCutaway"/>.
        /// </summary>
        public bool Prepare()
        {
            if (_prepared)
                return _states.Count > 0;

            var renderers = GetComponentsInChildren<Renderer>(includeInactive: true);
            return Bind(renderers);
        }

        /// <summary>
        /// Refresh a pooled building after it has been positioned and colourised for its
        /// next block. Existing gradient variants survive the bind; only newly encountered
        /// source materials create another variant.
        /// </summary>
        internal bool PrepareForRecycledBinding(IReadOnlyList<Renderer> renderers)
        {
            if (_gradientMaterialsActive)
                RestoreOriginals();
            return Bind(renderers);
        }

        internal bool Handles(Renderer renderer) =>
            renderer != null && _renderers.Contains(renderer);

        /// <summary>Copies the untouched material set to an offscreen duplicate. The
        /// ledger photographs a staged copy of the block, which must not inherit the
        /// street camera's temporary gradient materials.</summary>
        internal bool CopyOriginalMaterials(Renderer source, Renderer target)
        {
            if (source == null || target == null)
                return false;
            for (int i = 0; i < _states.Count; i++)
            {
                var state = _states[i];
                if (state.Renderer != source || state.Original == null)
                    continue;
                target.sharedMaterials = state.Original;
                return true;
            }
            return false;
        }

        /// <summary>Refresh world-space bounds immediately before a cut. Recycler binding
        /// composes at the origin and moves the completed holder afterwards, so the final
        /// block position is intentionally read here rather than assumed at prepare time.</summary>
        internal bool RefreshBounds()
        {
            if (!_prepared || _states.Count == 0)
                return false;

            Bounds bounds = default;
            bool hasBounds = false;
            for (int i = 0; i < _states.Count; i++)
            {
                var renderer = _states[i].Renderer;
                if (renderer == null) continue;
                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else bounds.Encapsulate(renderer.bounds);
            }
            if (!hasBounds)
                return false;

            _boundsMinY = bounds.min.y;
            _boundsHeight = Mathf.Max(0.01f, bounds.size.y);
            _boundsCenter = bounds.center;
            ApplyBoundsToVariants();
            return true;
        }

        bool Bind(IReadOnlyList<Renderer> renderers)
        {
            if (!EnsureShader())
                return false;
            if (!TryBounds(renderers, out Bounds bounds, out int meshRendererCount))
            {
                _prepared = false;
                if (!_loggedNoRenderers)
                {
                    Debug.LogWarning("[BuildingOpacityGradient] The building has no mesh renderers.", this);
                    _loggedNoRenderers = true;
                }
                return false;
            }

            _boundsMinY = bounds.min.y;
            _boundsHeight = Mathf.Max(0.01f, bounds.size.y);
            _boundsCenter = bounds.center;

            if (!SameTopology(renderers, meshRendererCount))
                RebuildRendererStates(renderers);

            for (int i = 0; i < _states.Count; i++)
                CaptureMaterials(_states[i]);
            ApplyBoundsToVariants();

            _prepared = _states.Count > 0;
            _lastAmount = -1f;
            _lastGradientStartHeight = -1f;
            return _prepared;
        }

        bool EnsureShader()
        {
            if (_shader != null)
                return true;

            if (_sharedShader == null)
                _sharedShader = Shader.Find(ShaderName);
            _shader = _sharedShader;
            if (_shader != null)
                return true;

            if (!_loggedShaderError)
            {
                Debug.LogError($"[BuildingOpacityGradient] Shader '{ShaderName}' was not found.", this);
                _loggedShaderError = true;
            }
            return false;
        }

        static bool TryBounds(IReadOnlyList<Renderer> renderers, out Bounds bounds,
            out int meshRendererCount)
        {
            bounds = default;
            meshRendererCount = 0;
            bool hasBounds = false;
            if (renderers == null)
                return false;

            for (int i = 0; i < renderers.Count; i++)
            {
                if (!(renderers[i] is MeshRenderer renderer) || renderer == null)
                    continue;
                meshRendererCount++;
                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else bounds.Encapsulate(renderer.bounds);
            }
            return hasBounds;
        }

        bool SameTopology(IReadOnlyList<Renderer> renderers, int meshRendererCount)
        {
            if (_states.Count != meshRendererCount)
                return false;

            int stateIndex = 0;
            for (int i = 0; i < renderers.Count; i++)
            {
                if (!(renderers[i] is MeshRenderer renderer) || renderer == null)
                    continue;
                if (_states[stateIndex++].Renderer != renderer)
                    return false;
            }
            return true;
        }

        void RebuildRendererStates(IReadOnlyList<Renderer> renderers)
        {
            _states.Clear();
            _renderers.Clear();
            for (int i = 0; i < renderers.Count; i++)
            {
                if (!(renderers[i] is MeshRenderer renderer) || renderer == null)
                    continue;
                _states.Add(new RendererState { Renderer = renderer });
                _renderers.Add(renderer);
            }
        }

        void CaptureMaterials(RendererState state)
        {
            var renderer = state.Renderer;
            if (renderer == null)
                return;

            _materialScratch.Clear();
            renderer.GetSharedMaterials(_materialScratch);
            int count = _materialScratch.Count;
            if (state.Original == null || state.Original.Length != count)
                state.Original = new Material[count];
            if (state.Gradient == null || state.Gradient.Length != count)
                state.Gradient = new Material[count];

            for (int i = 0; i < count; i++)
            {
                var source = _materialScratch[i];
                state.Original[i] = source;
                state.Gradient[i] = Variant(source);
            }
            _materialScratch.Clear();
        }

        Material Variant(Material source)
        {
            if (source == null)
            {
                if (_nullSourceVariant == null)
                    _nullSourceVariant = GradientMaterial(null, _shader);
                return _nullSourceVariant;
            }

            if (_variants.TryGetValue(source, out var variant) && variant != null)
                return variant;

            variant = GradientMaterial(source, _shader);
            _variants[source] = variant;
            return variant;
        }

        /// <summary>
        /// Sets the visual treatment. Amount zero is the untouched opaque building.
        /// In Vertical mode, the shell stays opaque below <paramref name="gradientStartHeight"/>;
        /// amount one (100%) then grades from alpha 1 there to alpha 0 at the roof. Amount
        /// two (200%) continues the wipe until the whole shell is clear.
        /// The camera-opposite half is fully clear from 100% onward. Uniform uses the same
        /// 0..200% range but fades the whole shell evenly.
        /// </summary>
        public bool Set(float amount, Profile profile = Profile.Vertical,
                        float gradientStartHeight = DefaultGradientStartHeight)
        {
            if (!Prepare())
                return false;

            amount = Mathf.Clamp(amount, 0f, 2f);
            gradientStartHeight = Mathf.Clamp(gradientStartHeight, 0f,
                Mathf.Max(0f, _boundsHeight - 0.01f));
            bool profileChanged = profile != _lastProfile;

            if (amount <= ActiveThreshold)
            {
                _lastProfile = profile;
                _lastAmount = 0f;
                _lastGradientStartHeight = gradientStartHeight;
                RestoreOriginals();
                return true;
            }

            if (!_gradientMaterialsActive)
                UseGradientMaterials();

            if (Mathf.Abs(amount - _lastAmount) < 0.0001f &&
                Mathf.Abs(gradientStartHeight - _lastGradientStartHeight) < 0.0001f &&
                !profileChanged)
                return true;

            _lastProfile = profile;
            _lastAmount = amount;
            _lastGradientStartHeight = gradientStartHeight;
            float vertical = profile == Profile.Vertical ? 1f : 0f;
            ApplyValuesToVariants(amount, vertical, gradientStartHeight);
            return true;
        }

        void ApplyValuesToVariants(float amount, float vertical, float gradientStartHeight)
        {
            foreach (var pair in _variants)
                SetValues(pair.Value, amount, vertical, gradientStartHeight);
            SetValues(_nullSourceVariant, amount, vertical, gradientStartHeight);
        }

        static void SetValues(Material material, float amount, float vertical,
                              float gradientStartHeight)
        {
            if (material == null)
                return;
            material.SetFloat(FadeAmount, amount);
            material.SetFloat(GradientMode, vertical);
            material.SetFloat(GradientStartHeight, gradientStartHeight);
        }

        void UseGradientMaterials()
        {
            for (int i = 0; i < _states.Count; i++)
            {
                var state = _states[i];
                if (state.Renderer != null)
                    state.Renderer.sharedMaterials = state.Gradient;
            }
            _gradientMaterialsActive = true;
        }

        void RestoreOriginals()
        {
            if (!_gradientMaterialsActive)
                return;

            for (int i = 0; i < _states.Count; i++)
            {
                var state = _states[i];
                if (state.Renderer != null)
                    state.Renderer.sharedMaterials = state.Original;
            }
            _gradientMaterialsActive = false;
        }

        Material GradientMaterial(Material source, Shader shader)
        {
            var material = new Material(shader)
            {
                name = source ? source.name + " (Occlusion Gradient)" : "Occlusion Gradient",
                hideFlags = HideFlags.DontSave,
                enableInstancing = source && source.enableInstancing,
                doubleSidedGI = source && source.doubleSidedGI,
            };

            string textureProperty = TextureProperty(source);
            if (source && textureProperty != null)
            {
                material.SetTexture(BaseMap, source.GetTexture(textureProperty));
                material.SetTextureScale("_BaseMap", source.GetTextureScale(textureProperty));
                material.SetTextureOffset("_BaseMap", source.GetTextureOffset(textureProperty));
            }

            Color colour = Color.white;
            if (source)
            {
                if (source.HasProperty("_BaseColor")) colour = source.GetColor("_BaseColor");
                else if (source.HasProperty("_Color")) colour = source.GetColor("_Color");
            }
            material.SetColor(BaseColor, colour);

            float cutoff = 0.01f;
            if (source)
            {
                if (source.HasProperty("_Cutoff")) cutoff = source.GetFloat("_Cutoff");
                else if (source.HasProperty("_Alpha_Clip_Threshold"))
                    cutoff = source.GetFloat("_Alpha_Clip_Threshold");
            }
            material.SetFloat(Cutoff, Mathf.Clamp01(cutoff));
            SetBounds(material);
            material.SetFloat(FadeAmount, 0f);
            material.SetFloat(GradientMode, 1f);
            material.SetFloat(GradientStartHeight, DefaultGradientStartHeight);
            return material;
        }

        void ApplyBoundsToVariants()
        {
            foreach (var pair in _variants)
                SetBounds(pair.Value);
            SetBounds(_nullSourceVariant);
        }

        void SetBounds(Material material)
        {
            if (material == null)
                return;
            material.SetFloat(BoundsMinY, _boundsMinY);
            material.SetFloat(BoundsHeight, _boundsHeight);
            material.SetVector(BoundsCenter, new Vector4(_boundsCenter.x, _boundsCenter.y,
                _boundsCenter.z, 1f));
        }

        static string TextureProperty(Material source)
        {
            if (!source) return null;
            if (source.HasProperty("_BaseMap")) return "_BaseMap";
            if (source.HasProperty("_Albedo_Map")) return "_Albedo_Map";
            if (source.HasProperty("_MainTex")) return "_MainTex";
            return null;
        }

        void OnDisable() => RestoreOriginals();

        void OnDestroy()
        {
            RestoreOriginals();
            foreach (var pair in _variants)
                DestroyVariant(pair.Value);
            DestroyVariant(_nullSourceVariant);
            _variants.Clear();
            _renderers.Clear();
            _states.Clear();
        }

        static void DestroyVariant(Material material)
        {
            if (material == null)
                return;
            if (Application.isPlaying) Destroy(material);
            else DestroyImmediate(material);
        }
    }
}
