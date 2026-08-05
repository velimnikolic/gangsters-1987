using System.Collections.Generic;
using UnityEngine;

namespace LivingCity.Generation
{
    /// <summary>
    /// One straight length of boundary - railing, hedge, picket - tiled between two parameters
    /// along a line.
    ///
    /// The segment count is rounded and the spacing derived from it, then each instance is
    /// stretched to match. A run is never an exact multiple of the prefab, and the two
    /// alternatives are both worse than a one-or-two-percent stretch on a 2m piece: leaving the
    /// remainder open puts a hole in the fence, and overlapping the last piece doubles the posts
    /// where it lands.
    ///
    /// Shared by the car park boundary (ParkingLotDresser) and the park hedge (ParkDresser).
    /// They differ only in what they hand it: fence-classic railing at 2.0m against fence-shrub
    /// hedge at 2.09m, and where the gaps go.
    /// </summary>
    public static class FenceRun
    {
        public static void Lay(
            GameObject prefab,
            Vector3 origin,
            Vector3 along,
            float from,
            float to,
            Transform parent,
            SpawnPrefab spawn,
            List<GameObject> placed)
        {
            if (!prefab)
                return;

            var length = to - from;
            if (length <= 0.5f)
                return;

            // Measured, not assumed - GroundPlacer takes the same care with its slab tile, and for
            // the same reason: a different fence prefab dropped into the palette must not silently
            // come out at the wrong scale or lying across the run.
            var footprint = PrefabBounds.FootprintXZ(prefab, 0f);
            var lengthAxisIsX = footprint.x >= footprint.y;
            var segment = lengthAxisIsX ? footprint.x : footprint.y;
            if (segment < 0.1f)
                return;

            var count = Mathf.Max(1, Mathf.RoundToInt(length / segment));
            var spacing = length / count;
            var stretch = spacing / segment;

            // The prefab's long ground axis has to end up lying along the run. Ry(-90) sends local
            // +X to +Z, which LookRotation then sends to 'along'; a prefab already built along its
            // local +Z needs no such correction.
            var rotation = lengthAxisIsX
                ? Quaternion.LookRotation(along) * Quaternion.Euler(0f, -90f, 0f)
                : Quaternion.LookRotation(along);

            for (var i = 0; i < count; i++)
            {
                var instance = spawn(prefab, origin + along * (from + spacing * (i + 0.5f)), rotation, parent);

                var scale = instance.transform.localScale;
                if (lengthAxisIsX)
                    scale.x = stretch;
                else
                    scale.z = stretch;
                instance.transform.localScale = scale;

                placed.Add(instance);
            }
        }
    }
}
