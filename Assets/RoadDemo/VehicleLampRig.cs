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
        MaterialPropertyBlock _properties;
        float _level = -1f;

        public void SetRunningLights(float level)
        {
            level = isActiveAndEnabled ? Mathf.Clamp01(level) : 0f;
            if (!lenses || Mathf.Approximately(level, _level)) return;
            _level = level;
            if (_properties == null) _properties = new MaterialPropertyBlock();
            lenses.GetPropertyBlock(_properties);
            _properties.SetColor(Emission, Color.white * (level * 2.5f));
            lenses.SetPropertyBlock(_properties);
        }

        void OnDisable() => SetRunningLights(0f);
    }
}
