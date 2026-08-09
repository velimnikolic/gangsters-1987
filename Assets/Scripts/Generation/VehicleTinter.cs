using System.Collections.Generic;
using UnityEngine;
using LivingCity.Data;

namespace LivingCity.Generation
{
    /// <summary>
    /// Gives each car a body colour instead of the one the artist baked in.
    ///
    /// Same lever as BuildingTinter and for the same reason: the LPEC pack bakes colour into UVs,
    /// so every car mesh samples a swatch out of one palette atlas and points at one shared
    /// atlas-LPEC material. There is no "body colour" to set - the only move is to swap in a
    /// variant of that material whose _BaseColor MULTIPLIES the atlas.
    ///
    /// Multiply can only darken, which is what decides who is eligible. Measured off
    /// atlas-albedo-LPEC.png through each car's own UVs, area-weighted per face, the paint on
    /// car-passenger is #dbdbda over 35% of the body - near-white, so a tint lands as a true hue
    /// and the palette may run at full strength where the facade tints cannot. The parts that
    /// must NOT take the colour need no mask: glass (#16252d), trim (#292929) and the 31% pure
    /// black are already dark, and dark stays dark under a multiply. The atlas luminance is the
    /// mask. What this cannot do is repaint a car that is already painted - yellow times blue is
    /// dark green - so the taxi, the police car, the school bus and the pack's three character
    /// cars are simply absent from PrefabDatabase.paintableVehicles.
    ///
    /// An instance rather than a static class: the generator, the traffic spawner and each
    /// forecourt director paint disjoint sets of cars and must own independent streams. Nobody
    /// may draw from BlockBuilder's shared Buildings stream for this - see SeedOffsets.VehicleTints.
    /// </summary>
    public sealed class VehicleTinter
    {
        readonly HashSet<GameObject> paintable = new();
        readonly Material[] palette;
        readonly Material baseMaterial;
        readonly float chance;
        readonly System.Random rng;

        int painted;
        int seen;

        public VehicleTinter(PrefabDatabase prefabs, CityConfig config)
        {
            rng = new System.Random(config.seed + SeedOffsets.VehicleTints);
            chance = config.vehicleTintChance;

            if (!prefabs)
                return;

            // The buildings' base material IS the cars': every car prefab in Cars_T and Cars_AI_T
            // references atlas-LPEC, the same asset CityAssetBootstrap stores here. The field name
            // is a wart left from the tinter that came first; a second field pointing at the same
            // asset would be worse, because the two could then drift apart.
            baseMaterial = prefabs.buildingBaseMaterial;
            palette = prefabs.vehicleTints;

            if (prefabs.paintableVehicles == null)
                return;

            foreach (var prefab in prefabs.paintableVehicles)
                if (prefab)
                    paintable.Add(prefab);
        }

        /// <summary>
        /// Nothing to do - no palette, no base material, or nothing eligible in the catalogue.
        /// Callers need not test it; Paint is a no-op either way. It exists so a caller that
        /// wants to skip the work entirely can.
        /// </summary>
        public bool IsEmpty =>
            !baseMaterial || palette == null || palette.Length == 0 || paintable.Count == 0;

        /// <summary>
        /// Paints one freshly spawned car. `source` is the prefab it came from - the instance
        /// cannot answer for itself at runtime, and it is what the eligibility test is against.
        ///
        /// Returns true only when the instance actually changed colour.
        /// </summary>
        public bool Paint(GameObject instance, GameObject source)
        {
            if (IsEmpty)
                return false;

            // Both draws happen unconditionally, and neither the roll nor the eligibility test may
            // decide how MANY draws happen - only what is done with them. So adding a model to
            // paintableVehicles, or moving vehicleTintChance, changes that car and leaves every
            // other car in the city exactly the colour it was. Same discipline as BuildingTinter
            // and GroundPlacer.Shade; rng.Next(n) consumes one sample whatever n is, so the
            // palette may be resized freely.
            var roll = rng.NextDouble();
            var tint = palette[rng.Next(palette.Length)];

            seen++;

            if (!instance || !source || roll >= chance || !tint)
                return false;

            if (!paintable.Contains(source))
                return false;

            if (!MaterialTint.Repaint(instance, baseMaterial, tint, SkipWheels))
                return false;

            painted++;
            return true;
        }

        /// <summary>
        /// Every car in the pack names its parts the same way - body is "car-veteran-base", wheels
        /// are "car-veteran-wheel_FL" and so on - so one name test covers the fleet. Trucks add
        /// -door_L and -door_R and pickups a -rear, all of which are bodywork and are meant to
        /// take the paint; only the wheels are excluded.
        /// </summary>
        static bool SkipWheels(Renderer renderer) =>
            renderer && renderer.name.IndexOf("wheel", System.StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>
        /// One line for the build log, in the shape BuildingTinter's is. `seen` counts every car
        /// offered, eligible or not, because "12 of 40" is the number that says whether the
        /// catalogue is doing what it should - a painted count on its own cannot.
        /// </summary>
        public void Report(string label)
        {
            if (seen == 0)
                return;

            Debug.Log($"[VehicleTinter] Painted {painted} of {seen} {label}.");
        }
    }
}
