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
    /// ordinary city batching path pays nothing while the building is not an occluder.
    ///
    /// This component deliberately does not decide which building occludes a subject;
    /// StreetCutaway (or another shared visibility policy) owns that decision. The
    /// OcclusionDemo exercises this rendering primitive before it is wired into the city.
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

        static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int Cutoff = Shader.PropertyToID("_Cutoff");
        static readonly int FadeAmount = Shader.PropertyToID("_FadeAmount");
        static readonly int GradientMode = Shader.PropertyToID("_GradientMode");
        static readonly int OpaqueFloor = Shader.PropertyToID("_OpaqueFloor");
        static readonly int BoundsMinY = Shader.PropertyToID("_BoundsMinY");
        static readonly int BoundsInvHeight = Shader.PropertyToID("_BoundsInvHeight");

        sealed class RendererState
        {
            public MeshRenderer Renderer;
            public Material[] Original;
            public Material[] Gradient;
        }

        readonly List<RendererState> _states = new List<RendererState>();

        bool _prepared;
        bool _gradientMaterialsActive;
        float _lastAmount = -1f;
        float _lastOpaqueFloor = -1f;
        Profile _lastProfile;

        public bool Ready => _prepared && _states.Count > 0;
        public bool GradientMaterialsActive => _gradientMaterialsActive;
        public float Amount => Mathf.Max(0f, _lastAmount);
        public Profile CurrentProfile => _lastProfile;

        /// <summary>
        /// Captures every mesh renderer below this logical building and prepares its
        /// gradient-material counterpart. This does not touch colliders or current visuals.
        /// </summary>
        public bool Prepare()
        {
            if (_prepared)
                return _states.Count > 0;

            _prepared = true;
            var shader = Shader.Find(ShaderName);
            if (!shader)
            {
                Debug.LogError($"[BuildingOpacityGradient] Shader '{ShaderName}' was not found.", this);
                return false;
            }

            var renderers = GetComponentsInChildren<MeshRenderer>(includeInactive: true);
            if (renderers.Length == 0)
            {
                Debug.LogWarning("[BuildingOpacityGradient] The building has no mesh renderers.", this);
                return false;
            }

            Bounds bounds = default;
            bool hasBounds = false;
            foreach (var renderer in renderers)
            {
                if (!renderer) continue;
                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else bounds.Encapsulate(renderer.bounds);
            }

            if (!hasBounds)
                return false;

            float minY = bounds.min.y;
            float invHeight = 1f / Mathf.Max(0.01f, bounds.size.y);
            foreach (var renderer in renderers)
            {
                if (!renderer) continue;
                var original = renderer.sharedMaterials;
                var gradient = new Material[original.Length];
                for (var i = 0; i < original.Length; i++)
                    gradient[i] = GradientMaterial(original[i], shader, minY, invHeight);

                _states.Add(new RendererState
                {
                    Renderer = renderer,
                    Original = original,
                    Gradient = gradient,
                });
            }

            return _states.Count > 0;
        }

        /// <summary>
        /// Sets the visual treatment. Amount zero is the untouched opaque building.
        /// Amount one is either fully invisible (Uniform) or alpha 1 at the base grading
        /// continuously to alpha 0 at the roof (Vertical).
        /// </summary>
        public bool Set(float amount, Profile profile = Profile.Vertical, float opaqueFloor = 0.08f)
        {
            if (!Prepare())
                return false;

            amount = Mathf.Clamp01(amount);
            opaqueFloor = Mathf.Clamp(opaqueFloor, 0f, 0.45f);
            bool profileChanged = profile != _lastProfile;

            if (amount <= ActiveThreshold)
            {
                _lastProfile = profile;
                _lastAmount = 0f;
                _lastOpaqueFloor = opaqueFloor;
                RestoreOriginals();
                return true;
            }

            if (!_gradientMaterialsActive)
                UseGradientMaterials();

            if (Mathf.Abs(amount - _lastAmount) < 0.0001f &&
                Mathf.Abs(opaqueFloor - _lastOpaqueFloor) < 0.0001f &&
                !profileChanged)
                return true;

            _lastProfile = profile;
            _lastAmount = amount;
            _lastOpaqueFloor = opaqueFloor;
            float vertical = profile == Profile.Vertical ? 1f : 0f;
            foreach (var state in _states)
            {
                foreach (var material in state.Gradient)
                {
                    if (!material) continue;
                    material.SetFloat(FadeAmount, amount);
                    material.SetFloat(GradientMode, vertical);
                    material.SetFloat(OpaqueFloor, opaqueFloor);
                }
            }

            return true;
        }

        void UseGradientMaterials()
        {
            foreach (var state in _states)
                if (state.Renderer) state.Renderer.sharedMaterials = state.Gradient;
            _gradientMaterialsActive = true;
        }

        void RestoreOriginals()
        {
            if (!_gradientMaterialsActive)
                return;

            foreach (var state in _states)
                if (state.Renderer) state.Renderer.sharedMaterials = state.Original;
            _gradientMaterialsActive = false;
        }

        static Material GradientMaterial(Material source, Shader shader, float minY, float invHeight)
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
            material.SetFloat(BoundsMinY, minY);
            material.SetFloat(BoundsInvHeight, invHeight);
            material.SetFloat(FadeAmount, 0f);
            material.SetFloat(GradientMode, 1f);
            material.SetFloat(OpaqueFloor, 0.08f);
            return material;
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
            foreach (var state in _states)
            {
                foreach (var material in state.Gradient)
                {
                    if (!material) continue;
                    if (Application.isPlaying) Destroy(material);
                    else DestroyImmediate(material);
                }
            }
            _states.Clear();
        }
    }
}
