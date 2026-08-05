using UnityEngine;

namespace LivingCity.Generation
{
    /// <summary>
    /// Swaps one material for another across an instance's renderers.
    ///
    /// The LPEC pack bakes colour into UVs, not into materials: everything points at swatches
    /// inside a single palette atlas and shares one material. So the only lever on colour is to
    /// replace that material with a variant of itself carrying a different _BaseColor, which the
    /// shader MULTIPLIES with the atlas. Both BuildingTinter and GroundPlacer need exactly that,
    /// on the same base material, which is why it lives here rather than in either of them.
    /// </summary>
    public static class MaterialTint
    {
        /// <summary>
        /// One colour per INSTANCE, not per renderer - a terrace piece is several meshes and
        /// picking per renderer would make one facade shade into another mid-wall.
        ///
        /// Returns false when the instance carried the base material nowhere, which is how a
        /// caller can tell a genuinely untintable prefab from a successful repaint.
        /// </summary>
        public static bool Repaint(GameObject target, Material baseMaterial, Material tint)
        {
            if (!target || !baseMaterial || !tint)
                return false;

            var painted = false;

            foreach (var renderer in target.GetComponentsInChildren<MeshRenderer>(true))
            {
                // sharedMaterials, never materials: reading .materials instantiates a private
                // copy per renderer, which leaks and drops the renderer out of batching. That
                // is the existing bug in the vendor pack's TrafficLightsControl, and here it
                // would hit every building in the city rather than a handful of traffic lights.
                var slots = renderer.sharedMaterials;
                var changed = false;

                for (var i = 0; i < slots.Length; i++)
                {
                    if (slots[i] != baseMaterial)
                        continue;   // Glass uses atlas-transparent-LPEC - leave it transparent.

                    slots[i] = tint;
                    changed = true;
                }

                if (!changed)
                    continue;

                renderer.sharedMaterials = slots;
                painted = true;
            }

            return painted;
        }
    }
}
