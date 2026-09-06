using UnityEngine;

namespace RoadDemo
{
    /// <summary>Authored beam origins and independently controlled front/tail
    /// submeshes. Both slots use the original shared Synty lamp material.</summary>
    public sealed class VehicleLampRig : MonoBehaviour
    {
        public Vector3 leftHeadlight, rightHeadlight;
        public Renderer lenses;
        public Vector3[] auxiliaryHeadlights = System.Array.Empty<Vector3>();
        public int tailMaterialIndex = -1;
        static readonly int Emission = Shader.PropertyToID("_EmissionColor");
        static readonly int SyntyEmission = Shader.PropertyToID("_Emission_Color");
        MaterialPropertyBlock _properties;
        int _emissionProperty;
        float _level = -1f, _tailLevel = -1f;
        bool _braking;

        public void SetRunningLights(float level)
        {
            level = isActiveAndEnabled ? Mathf.Clamp01(level) : 0f;
            if (!Mathf.Approximately(level, _level))
            {
                _level = level;
                Apply(level, tailMaterialIndex < 0 ? -1 : 0);
            }
            Tail();
        }

        public void SetBrakeLights(bool braking)
        {
            _braking = braking && isActiveAndEnabled;
            Tail();
        }

        void Tail()
        {
            if (tailMaterialIndex < 0) return;
            float level = _braking ? 3.5f : Mathf.Max(0f, _level) * .18f;
            if (Mathf.Approximately(level, _tailLevel)) return;
            _tailLevel = level;
            Apply(level, tailMaterialIndex);
        }

        void Apply(float level, int slot)
        {
            if (!lenses) return;
            if (_properties == null)
            {
                _properties = new MaterialPropertyBlock();
                var material = lenses.sharedMaterial;
                _emissionProperty = material && material.HasProperty(SyntyEmission) ? SyntyEmission : Emission;
            }
            if (slot < 0) lenses.GetPropertyBlock(_properties);
            else lenses.GetPropertyBlock(_properties, slot);
            _properties.SetColor(_emissionProperty, Color.white * (level * (_emissionProperty == SyntyEmission ? 1f : 2.5f)));
            if (slot < 0) lenses.SetPropertyBlock(_properties);
            else lenses.SetPropertyBlock(_properties, slot);
        }

        void OnEnable() { _braking = false; _level = _tailLevel = -1f; SetRunningLights(0f); }
        void OnDisable() { _braking = false; SetRunningLights(0f); }
    }
}
