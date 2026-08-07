using UnityEngine;

namespace LivingCity.Data
{
    /// <summary>
    /// Entity budgets and quality switches for the mobile pass.
    ///
    /// Deliberately absent: any distance-culling radius. The city is only a few hundred metres
    /// across and the camera reaches an orthographic size of 160, which frames the whole city
    /// at once - at wide zoom everything is on screen and a cull radius saves nothing by
    /// definition, there is nothing off screen to cull. At close zoom it would
    /// deactivate AI cars mid-route, freezing their trajectory state and popping them back
    /// into traffic incorrectly. Reduce the counts below instead.
    /// </summary>
    [CreateAssetMenu(fileName = "PerformanceConfig", menuName = "Living City/Performance Config")]
    public sealed class PerformanceConfig : ScriptableObject
    {
        [Header("Entity budgets")]
        [Min(0)] public int maxCars = 30;
        [Min(0)] public int maxPedestrians = 50;
        [Min(0)] public int maxClouds = 10;

        [Header("Tree sway")]
        [Tooltip("Sway moves the transform, which removes that tree from static batching. " +
                 "Cap how many get the component; beyond this, use shader vertex displacement.")]
        [Min(0)] public int maxSwayingTrees = 40;

        [Header("Rendering")]
        [Range(1, 4)] public int shadowCascades = 1;
        public bool softShadows;

        /// <summary>
        /// Applies the budgets onto a CityConfig so there is one place to tune for a device.
        /// </summary>
        public void ApplyTo(CityConfig config)
        {
            if (!config) return;
            config.carCount = maxCars;
            config.pedestrianCount = maxPedestrians;
        }
    }
}
