using System.Collections.Generic;
using UnityEngine;
using LivingCity.Data;

namespace LivingCity.Generation
{
    /// <summary>
    /// How a prefab gets into the scene. Runtime uses Object.Instantiate; the editor window
    /// passes a PrefabUtility-based version so prefab links survive into the saved scene.
    /// </summary>
    public delegate GameObject SpawnPrefab(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent);

    public sealed class RoadNetworkResult
    {
        public readonly List<GameObject> Tiles = new();
        public readonly List<GameObject> TrafficLights = new();
    }

    public static class RoadNetworkBuilder
    {
        public static GameObject RuntimeSpawn(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent)
            => Object.Instantiate(prefab, position, rotation, parent);

        /// <summary>
        /// Instantiates the whole road network.
        ///
        /// This MUST complete synchronously - no coroutine, no yield, no spreading across
        /// frames. Tile.Awake() caches its neighbours and Tile.Start() is what actually
        /// builds the path links between them. Start() does not run until the current batch
        /// of instantiation finishes, which is precisely what lets every tile see a complete
        /// network. Stagger the tiles across frames and the early ones link into a partial
        /// network and never recover.
        ///
        /// (Tile.OnEnable calls UpdateTile(), which looks like it would self-heal a late
        /// addition, but it early-outs: Awake already cached the same neighbour set, so
        /// "changed" is false and the existing neighbours are never notified. Adding a tile
        /// after startup requires calling UpdateNeighbors() on it AND on all four neighbours.)
        ///
        /// Gradual spawning applies to cars and pedestrians only - never to tiles.
        /// </summary>
        public static RoadNetworkResult Build(
            CityGrid grid,
            PrefabDatabase prefabs,
            CityConfig config,
            Transform parent,
            SpawnPrefab spawn = null,
            float crosswalkChance = 0.2f)
        {
            var result = new RoadNetworkResult();

            if (!prefabs.ValidateRoadTiles(out var missing))
            {
                Debug.LogError($"[RoadNetworkBuilder] PrefabDatabase is missing road tiles: {missing}. Nothing generated.");
                return result;
            }

            spawn ??= RuntimeSpawn;
            var rng = new System.Random(config.seed + SeedOffsets.Roads + 1);

            foreach (var cell in grid.RoadCells())
            {
                var sides = grid.GetNeighborMask(cell.x, cell.y);
                var placement = RoadTileTable.Lookup(sides);

                if (!placement.IsValid)
                {
                    Debug.LogWarning($"[RoadNetworkBuilder] Isolated road cell at {cell} has no connections - skipped.");
                    continue;
                }

                var prefab = prefabs.GetRoadTile(placement.Kind);

                // Crosswalk straights give pedestrians a marked place to cross. The prefab's
                // tileShape is Cross so it probes all four sides, but a straight run has no
                // side neighbours to match against, so it behaves as a plain straight.
                if (placement.Kind == RoadTileKind.Straight
                    && prefabs.straightCrosswalk
                    && rng.NextDouble() < crosswalkChance)
                {
                    prefab = prefabs.straightCrosswalk;
                }

                var tile = spawn(prefab, grid.CellToWorld(cell), placement.Rotation, parent);
                tile.name = $"tile_{cell.x}_{cell.y}_{placement.Kind}";
                result.Tiles.Add(tile);

                if (placement.Kind == RoadTileKind.Cross && prefabs.trafficLights)
                {
                    var lights = spawn(prefabs.trafficLights, grid.CellToWorld(cell), Quaternion.identity, parent);
                    lights.name = $"lights_{cell.x}_{cell.y}";
                    result.TrafficLights.Add(lights);
                }
            }

            return result;
        }
    }
}
