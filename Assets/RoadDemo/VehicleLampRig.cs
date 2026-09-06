using UnityEngine;

namespace RoadDemo
{
    /// <summary>Authored beam positions and lamp glass. DemoHeadlights owns when
    /// these run; the mesh's emission map supplies each lens's colour.</summary>
    public sealed class VehicleLampRig : MonoBehaviour
    {
        public Vector3 leftHeadlight, rightHeadlight;
        public Renderer lenses;

        static readonly int Emission = Shader.PropertyToID("_EmissionColor");
        static readonly int SyntyEmission = Shader.PropertyToID("_Emission_Color");
        MaterialPropertyBlock _properties;
        int _emissionProperty;
        float _level = -1f;

        public void SetRunningLights(float level)
        {
            level = isActiveAndEnabled ? Mathf.Clamp01(level) : 0f;
            if (!lenses || Mathf.Approximately(level, _level)) return;
            _level = level;
            if (_properties == null)
            {
                _properties = new MaterialPropertyBlock();
                var material = lenses.sharedMaterial;
                _emissionProperty = material && material.HasProperty(SyntyEmission) ? SyntyEmission : Emission;
            }
            lenses.GetPropertyBlock(_properties);
            _properties.SetColor(_emissionProperty, Color.white * (level * (_emissionProperty == SyntyEmission ? 1f : 2.5f)));
            lenses.SetPropertyBlock(_properties);
        }

        void OnEnable() => SetRunningLights(0f);
        void OnDisable() => SetRunningLights(0f);
    }
}
