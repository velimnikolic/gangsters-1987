using System.Collections.Generic;
using UnityEngine;

namespace LivingCity.Generation
{
    /// <summary>
    /// Footprint of a prefab in its own local space, measured without instantiating it.
    /// Renderer.bounds needs a live instance, so this walks the MeshFilters and folds each
    /// mesh's local bounds into the prefab root's space via the relative transform.
    /// Results are cached - the block builder queries the same handful of prefabs repeatedly.
    /// </summary>
    public static class PrefabBounds
    {
        static readonly Dictionary<GameObject, Bounds> Cache = new();

        public static Bounds Get(GameObject prefab)
        {
            if (Cache.TryGetValue(prefab, out var cached))
                return cached;

            var toLocal = prefab.transform.worldToLocalMatrix;
            var bounds = new Bounds();
            var initialised = false;

            foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>())
            {
                var mesh = filter.sharedMesh;
                if (!mesh) continue;

                var matrix = toLocal * filter.transform.localToWorldMatrix;
                var meshBounds = mesh.bounds;
                var centre = meshBounds.center;
                var extents = meshBounds.extents;

                // Transform all eight corners - a rotated child would make an axis-aligned
                // extents scale wrong.
                for (var corner = 0; corner < 8; corner++)
                {
                    var offset = new Vector3(
                        (corner & 1) == 0 ? -extents.x : extents.x,
                        (corner & 2) == 0 ? -extents.y : extents.y,
                        (corner & 4) == 0 ? -extents.z : extents.z);

                    var point = matrix.MultiplyPoint3x4(centre + offset);

                    if (!initialised)
                    {
                        bounds = new Bounds(point, Vector3.zero);
                        initialised = true;
                    }
                    else
                    {
                        bounds.Encapsulate(point);
                    }
                }
            }

            if (!initialised)
                bounds = new Bounds(Vector3.zero, Vector3.one);

            Cache[prefab] = bounds;
            return bounds;
        }

        /// <summary>Footprint on the ground plane after a 90-degree-multiple Y rotation.</summary>
        public static Vector2 FootprintXZ(GameObject prefab, float yRotation)
        {
            var size = Get(prefab).size;
            var quarterTurns = Mathf.RoundToInt(yRotation / 90f) & 3;
            return (quarterTurns & 1) == 0
                ? new Vector2(size.x, size.z)
                : new Vector2(size.z, size.x);
        }

        public static void ClearCache() => Cache.Clear();
    }
}
