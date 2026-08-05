using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using PolyPerfect.City;
using LivingCity.Data;
using LivingCity.Generation;

namespace LivingCity.Entities
{
    /// <summary>
    /// Populates the pavements. Spawns onto the sidewalk line rather than the tile centre so
    /// HumanBehavior's initial GetClocestPoint() snaps to a sensible checkpoint instead of
    /// dragging the character across the carriageway on its first step.
    /// </summary>
    public sealed class PedestrianSpawner : MonoBehaviour
    {
        [SerializeField] CityConfig config;
        [SerializeField] PrefabDatabase prefabs;

        [Header("Idle behaviour")]
        [Tooltip("Fraction of pedestrians that occasionally stop and idle.")]
        [SerializeField, Range(0f, 1f)] float idlerFraction = 0.35f;

        readonly List<GameObject> active = new();
        System.Random rng;

        IEnumerator Start()
        {
            if (!config || !prefabs || prefabs.aiPedestrians == null || prefabs.aiPedestrians.Length == 0)
            {
                Debug.LogWarning("[PedestrianSpawner] Needs a CityConfig and a PrefabDatabase with AI people.", this);
                yield break;
            }

            rng = new System.Random(config.seed + SeedOffsets.Pedestrians);

            // Same reason as VehicleSpawner: sidewalk paths are linked in Tile.Start().
            yield return null;

            for (var i = 0; i < config.pedestrianCount; i++)
            {
                SpawnOne();
                if (config.entitySpawnInterval > 0f)
                    yield return new WaitForSeconds(config.entitySpawnInterval);
            }
        }

        void SpawnOne()
        {
            var tile = RandomWalkableTile();
            if (!tile)
                return;

            // Offset onto one of the two sidewalk lines (x = +/-4 in tile-local space),
            // then a little along the street so pedestrians do not stack up on tile centres.
            var side = rng.Next(2) == 0 ? 1f : -1f;
            var localOffset = new Vector3(
                CityGrid.SidewalkOffset * side,
                0f,
                ((float)rng.NextDouble() - 0.5f) * CityGrid.CellSize * 0.6f);

            var position = tile.transform.position + tile.transform.rotation * localOffset;

            var prefab = prefabs.aiPedestrians[rng.Next(prefabs.aiPedestrians.Length)];
            var person = Instantiate(prefab, position, Quaternion.Euler(0f, rng.Next(4) * 90f, 0f), transform);

            var behaviour = person.GetComponent<HumanBehavior>();
            if (behaviour)
                behaviour.randomDestination = true;

            if (behaviour && rng.NextDouble() < idlerFraction)
                person.AddComponent<PedestrianIdler>();

            active.Add(person);
        }

        Tile RandomWalkableTile()
        {
            if (Tile.Tiles.Count == 0)
                return null;

            for (var attempt = 0; attempt < 20; attempt++)
            {
                var tile = Tile.Tiles[rng.Next(Tile.Tiles.Count)];
                if (tile && (tile.tileType == Tile.TileType.Road || tile.tileType == Tile.TileType.OnlyPathwalk))
                    return tile;
            }

            return null;
        }
    }
}
