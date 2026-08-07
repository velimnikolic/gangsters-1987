using System.Collections.Generic;
using UnityEngine;
using LivingCity.Data;

namespace LivingCity.Generation
{
    /// <summary>
    /// Gives each building a different facade colour - mild for housing, stronger for shops.
    ///
    /// The LPEC pack bakes colour into UVs, not into materials: every building piece points at
    /// swatches inside a single 1024x1024 palette atlas and shares one material. So there is no
    /// "wall colour" to set - the only lever is _BaseColor, which multiplies the whole atlas.
    /// The tint materials therefore sit on LivingCity/Facade Tint (URP/Lit with _BaseColor
    /// masked by the surface normal), so the multiply reaches walls and windows but not the
    /// roof - see Assets/Shaders/FacadeTint.shader. Windows are vertical and still ride
    /// along, which is what caps how saturated a tint can go; within that cap the palettes
    /// range deliberately wide (green, tan, brick, blue-grey), because near-white tints left
    /// every street reading as one brown.
    ///
    /// Two tiers: buildings from a WeightedGroup marked commercial draw from the stronger
    /// commercialTints palette at commercialTintChance; everything else draws from
    /// buildingTints at buildingTintChance. Which tier an instance belongs to is decided at
    /// placement - only BlockBuilder knows which spawns are buildings and which group each
    /// came from - and handed over as the Target list.
    /// </summary>
    public static class BuildingTinter
    {
        /// <summary>One placed building and the tier its group put it in.</summary>
        public readonly struct Target
        {
            public readonly GameObject Instance;
            public readonly bool Commercial;

            public Target(GameObject instance, bool commercial)
            {
                Instance = instance;
                Commercial = commercial;
            }
        }

        public static void Apply(List<Target> buildings, PrefabDatabase prefabs, CityConfig config)
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

            var residential = prefabs.buildingTints;
            if (residential == null || residential.Length == 0)
                return;   // Palette deliberately empty - tinting is off, nothing wrong.

            var commercial = prefabs.commercialTints;
            if (commercial == null || commercial.Length == 0)
            {
                // An asset from before the field existed - still tint, just without the tier.
                Debug.LogWarning("[BuildingTinter] PrefabDatabase.commercialTints is empty - shops " +
                                 "tinted from the residential palette. Run Tools/City/Create or " +
                                 "Refresh Config Assets to build the stronger set.");
                commercial = residential;
            }

            var rng = new System.Random(config.seed + SeedOffsets.BuildingTints);
            var tinted = 0;
            var shops = 0;

            foreach (var target in buildings)
            {
                // Both draws happen unconditionally, and the tier only decides which threshold
                // and which palette the SAME two draws are read against - never how many draws
                // happen. So moving either chance slider, or flipping a group's commercial
                // flag, changes that building without repainting the rest of the city.
                // (rng.Next(n) consumes one sample whatever n is, so the palettes may differ
                // in length without desynchronising the stream.)
                var roll = rng.NextDouble();
                var palette = target.Commercial ? commercial : residential;
                var tint = palette[rng.Next(palette.Length)];

                var chance = target.Commercial ? config.commercialTintChance : config.buildingTintChance;
                if (!target.Instance || roll >= chance || !tint)
                    continue;

                if (MaterialTint.Repaint(target.Instance, prefabs.buildingBaseMaterial, tint))
                {
                    tinted++;
                    if (target.Commercial)
                        shops++;
                }
            }

            Debug.Log($"[BuildingTinter] Tinted {tinted} of {buildings.Count} buildings " +
                      $"({shops} commercial, seed {config.seed}).");
        }
    }
}
