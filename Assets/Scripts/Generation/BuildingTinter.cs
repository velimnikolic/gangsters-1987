using System.Collections.Generic;
using UnityEngine;
using LivingCity.Data;

namespace LivingCity.Generation
{
    /// <summary>
    /// Gives each building a slightly different facade colour.
    ///
    /// The LPEC pack bakes colour into UVs, not into materials: every building piece points at
    /// swatches inside a single 1024x1024 palette atlas and shares one material. So there is no
    /// "wall colour" to set. The only lever is _BaseColor on URP/Lit, which the shader
    /// MULTIPLIES with the atlas - it shifts the entire building at once, walls, roof and
    /// windows together. That is why the palette must stay near white: a saturated tint stops
    /// reading as a different render and starts reading as a monochrome toy.
    ///
    /// Runs as a post-pass over the list BlockBuilder already returns, so placement stays
    /// untouched - no extra argument threaded through the perimeter walk.
    /// </summary>
    public static class BuildingTinter
    {
        public static void Apply(List<GameObject> buildings, PrefabDatabase prefabs, CityConfig config)
        {
            if (buildings == null || buildings.Count == 0)
                return;

            // Reported rather than silently skipped: a half-configured database should surface
            // immediately, the same way CityAssetBootstrap reports prefabs it could not load.
            if (!prefabs.buildingBaseMaterial)
            {
                Debug.LogWarning("[BuildingTinter] PrefabDatabase.buildingBaseMaterial is unset - " +
                                 "buildings left untinted. Run Tools/City/Create or Refresh Config Assets.");
                return;
            }

            if (prefabs.buildingTints == null || prefabs.buildingTints.Length == 0)
                return;   // Palette deliberately empty - tinting is off, nothing wrong.

            var rng = new System.Random(config.seed + SeedOffsets.BuildingTints);
            var tinted = 0;

            foreach (var building in buildings)
            {
                // Both draws happen unconditionally. If the palette draw were skipped for
                // untinted buildings, moving the buildingTintChance slider would shift the
                // whole stream and repaint the entire city instead of only changing how many
                // buildings are painted.
                var wants = rng.NextDouble() < config.buildingTintChance;
                var tint = prefabs.buildingTints[rng.Next(prefabs.buildingTints.Length)];

                if (!building || !wants || !tint)
                    continue;

                if (Repaint(building, prefabs.buildingBaseMaterial, tint))
                    tinted++;
            }

            Debug.Log($"[BuildingTinter] Tinted {tinted} of {buildings.Count} buildings " +
                      $"from {prefabs.buildingTints.Length} variants (seed {config.seed}).");
        }

        /// <summary>
        /// Swaps the base material for a tint across one building's renderers. One colour per
        /// BUILDING, not per renderer - a terrace piece is several meshes and picking per
        /// renderer would make one facade shade into another mid-wall.
        /// </summary>
        static bool Repaint(GameObject building, Material baseMaterial, Material tint)
        {
            var painted = false;

            foreach (var renderer in building.GetComponentsInChildren<MeshRenderer>(true))
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
