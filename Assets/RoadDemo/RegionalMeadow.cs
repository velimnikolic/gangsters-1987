using UnityEngine;

namespace RoadDemo
{
    /// <summary>A district lawn uses the island shader's world-space meadow, independent of kit UVs.</summary>
    public sealed class RegionalMeadow : MonoBehaviour
    {
        Material _material;
        public static Material For(Transform root)
        {
            var owner = root.GetComponent<RegionalMeadow>() ?? root.gameObject.AddComponent<RegionalMeadow>();
            if (owner._material == null)
            {
                var meadow = DemoAssetLoad.Load<Shader>("Assets/Shaders/IslandTerrain.shader");
                var shader = meadow != null ? meadow : Shader.Find("Universal Render Pipeline/Lit");
                owner._material = new Material(shader) { name = "Shared regional meadow" };
                // Alpha selects meadow only; terrain vertices separately blend rock and sand.
                owner._material.SetColor("_BaseColor", meadow != null ? new Color(1f, 1f, 1f, 0f) : new Color(.26f, .32f, .22f));
            }
            return owner._material;
        }
        void OnDestroy() { if (_material != null) Destroy(_material); }
    }
}
