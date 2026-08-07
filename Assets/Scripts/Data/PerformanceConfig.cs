using UnityEngine;

namespace LivingCity.Data
{
    /// <summary>
    /// Entity budgets and quality switches for the mobile pass.
    ///
    /// Deliberately absent: any distance-culling radius. The camera is orthographic and at
    /// wide zoom frames the whole city, so a cull radius saves nothing by definition - there
    /// is nothing off screen to cull - and at close zoom it would deactivate AI cars
    /// mid-route, freezing their trajectory state and popping them back into traffic
    /// incorrectly. What DOES scale with zoom is per-entity fidelity, not existence: that is
    /// CityConfig's "Crowd performance" block (probe stride, animator LOD keyed on
    /// orthographic size, the re-path budget), applied by PedestrianLodSystem. Reduce the
    /// counts below for devices those levers cannot save.
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
