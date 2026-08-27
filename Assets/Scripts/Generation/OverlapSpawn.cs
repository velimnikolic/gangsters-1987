using System.Collections.Generic;
using UnityEngine;

namespace LivingCity.Generation
{
    /// <summary>
    /// Overlap-checked placement, the one copy of what the park, works and port dressers each
    /// used to keep line for line.
    ///
    /// Instantiates one piece, rejecting it if it would sit inside something already placed.
    /// The footprint is measured at the yaw and scale it will actually be built at, so a
    /// rotated 7m lime is tested as the 7m obstacle it is.
    ///
    /// Returns the instance rather than a bool (null on rejection) so a caller that needs the
    /// object - to hand it to the tinter, say - can have it without a second lookup.
    ///
    /// Draws nothing from any stream, so a dresser's rng order is the same through here as it
    /// was through its own copy.
    /// </summary>
    public static class OverlapSpawn
    {
        /// <param name="obstacles">
        /// A second list the footprint is also recorded in, for a caller that publishes what it
        /// stood as obstacles to a later pass. Optional.
        /// </param>
        public static GameObject Place(
            GameObject prefab,
            Vector3 position,
            float yaw,
            float scale,
            Transform parent,
            SpawnPrefab spawn,
            List<Bounds> occupied,
            List<GameObject> placed,
            List<Bounds> obstacles = null)
        {
            if (!prefab)
                return null;

            var footprint = PrefabBounds.FootprintXZ(prefab, yaw) * scale;
            var bounds = new Bounds(new Vector3(position.x, 0f, position.z),
                                    new Vector3(footprint.x, 1f, footprint.y));

            foreach (var existing in occupied)
                if (existing.Intersects(bounds))
                    return null;

            // The mesh is not necessarily centred on its pivot, so offset by the rotated - and
            // scaled - local bounds centre to land the geometry where it was asked for.
            var rotation = Quaternion.Euler(0f, yaw, 0f);
            var localCentre = PrefabBounds.Get(prefab).center;
            var offset = rotation * new Vector3(localCentre.x, 0f, localCentre.z) * scale;

            var instance = spawn(prefab,
                                 new Vector3(position.x - offset.x, position.y, position.z - offset.z),
                                 rotation, parent);

            if (!Mathf.Approximately(scale, 1f))
                instance.transform.localScale *= scale;

            occupied.Add(bounds);
            obstacles?.Add(bounds);
            placed.Add(instance);
            return instance;
        }
    }
}
