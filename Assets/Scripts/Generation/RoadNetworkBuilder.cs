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

            // The avenue degrades to an ordinary street rather than stopping the build - see
            // PrefabDatabase.ValidateMainRoadTiles. Resolved once: the warning belongs to the
            // asset, not to each of the dozen-odd cells that would repeat it.
            var buildBoulevard = grid.HasMainRoad;
            if (buildBoulevard && !prefabs.ValidateMainRoadTiles(out var missingMain))
            {
                Debug.LogWarning($"[RoadNetworkBuilder] PrefabDatabase is missing dual carriageway " +
                                 $"tiles: {missingMain}. The boulevard is drawn as an ordinary " +
                                 "street - run Tools/City/Create or Refresh Config Assets.");
                buildBoulevard = false;
            }

            foreach (var cell in grid.RoadCells())
            {
                var sides = grid.GetNeighborMask(cell.x, cell.y);
                var onBoulevard = buildBoulevard && grid.IsMainRoad(cell.x, cell.y);

                var placement = onBoulevard
                    ? RoadTileTable.LookupMain(sides, grid.MainRoadAxisAt(cell.x, cell.y))
                    : RoadTileTable.Lookup(sides);

                if (!placement.IsValid)
                {
                    Debug.LogWarning($"[RoadNetworkBuilder] Isolated road cell at {cell} has no connections - skipped.");
                    continue;
                }

                var prefab = prefabs.GetRoadTile(placement.Kind);

                // Crosswalk straights give pedestrians a marked place to cross. The prefab's
                // tileShape is Cross so it probes all four sides, but a straight run has no
                // side neighbours to match against, so it behaves as a plain straight.
                //
                // The avenue rolls for one too, and on the same draw. A boulevard cell was a
                // Straight before it was a MainStraight, so keeping the roll here - rather than
                // skipping it or moving it - leaves the random stream identical and every
                // existing seed's crosswalk pattern on the minor streets exactly where it was.
                if (placement.Kind == RoadTileKind.Straight
                    && prefabs.straightCrosswalk
                    && rng.NextDouble() < crosswalkChance)
                {
                    prefab = prefabs.straightCrosswalk;
                }
                //
                // Note the operand order: the roll comes BEFORE the null check, so a database
                // with no mainStraightCrosswalk still consumes its draw. Written the other way
                // round it would silently skip one and shift every later cell's crosswalk.
                else if (placement.Kind == RoadTileKind.MainStraight
                         && rng.NextDouble() < crosswalkChance
                         && prefabs.mainStraightCrosswalk)
                {
                    prefab = prefabs.mainStraightCrosswalk;
                }

                var tile = spawn(prefab, grid.CellToWorld(cell), placement.Rotation, parent);
                tile.name = $"tile_{cell.x}_{cell.y}_{placement.Kind}";

                // The stretch, and the only place it is applied to a road. Tile caches its
                // neighbours in Awake and links its lanes in Start, both AFTER this returns, so
                // the scale is already on the transform when the probe distance and the link
                // tolerance are computed - and both are ratios of lossyScale, so the network
                // comes out identical to an unscaled one. Setting it any later would relink a
                // network that had already been built at the wrong size.
                Rescale(tile);
                result.Tiles.Add(tile);

                var crossroads = placement.Kind == RoadTileKind.Cross
                                 || placement.Kind == RoadTileKind.MainCross
                                 || placement.Kind == RoadTileKind.MainMainCross;

                // The gantry set on the avenue, the post elsewhere; either falls back to the
                // other so a half-filled asset still lights its junctions.
                var mainJunction = placement.Kind == RoadTileKind.MainCross
                                   || placement.Kind == RoadTileKind.MainMainCross;
                var lightsPrefab = mainJunction && prefabs.mainTrafficLights
                    ? prefabs.mainTrafficLights
                    : prefabs.trafficLights;

                if (crossroads && lightsPrefab)
                {
                    var lights = spawn(lightsPrefab, grid.CellToWorld(cell), Quaternion.identity, parent);
                    lights.name = $"lights_{cell.x}_{cell.y}";

                    // The gantry has to grow with the road it spans, and its stop-trigger boxes
                    // are children, so they scale with it and keep meeting cars at the same place
                    // in the tile. A light left at authored size would hang its arms over a lane
                    // that is no longer there.
                    Rescale(lights);
                    result.TrafficLights.Add(lights);
                }
            }

            // THE SCALE IS NOT LIVE UNTIL THIS RUNS. Object.Instantiate is synchronous and runs
            // Awake before it returns, so every Tile above cached its neighbours at scale 1 - and
            // Tile.GetNeighborTiles probes 18 * lossyScale.z with a half-extent of 2 * lossyScale,
            // which at scale 1 is a box covering 16..20 while the neighbour's collider now starts
            // at CellSize/2 = 19.5. Half a metre of overlap, hit or miss per tile, so the cache
            // came out partial and the lane graph with it. Cars then find routes that stop in
            // mid-air or resolve their destination back onto their own tile, which is the
            // unguarded null in PathFinding.GetPath.
            //
            // Re-cached rather than avoided, because the only way to beat Awake is to instantiate
            // into a deactivated parent, and the parent is the caller's. Doing it here, after the
            // whole batch, is also strictly better than what Awake could have managed: a tile
            // placed early cannot see one placed later, so even at scale 1 the first tiles of the
            // loop were caching an incomplete neighbourhood.
            Relink(result.Tiles);

            return result;
        }

        /// <summary>
        /// Puts one placed tile on the city's stretch. Uniform on purpose: a tile scaled on one
        /// axis only would tear the lattice apart at the first junction - see CityGrid.TileScale.
        /// </summary>
        static void Rescale(GameObject instance)
        {
            if (instance)
                instance.transform.localScale = Vector3.one * CityGrid.TileScale;
        }

        /// <summary>
        /// Rebuilds every tile's neighbour cache and lane links, once the whole batch is placed
        /// and scaled.
        ///
        /// Physics.SyncTransforms first, and it is not optional: GetNeighborTiles asks the physics
        /// scene for colliders, and a transform scaled this frame does not reach it until the sync
        /// (autoSyncTransforms is off in this project). Without it every probe would still be
        /// measuring the unscaled boxes and this pass would faithfully reproduce the bug.
        ///
        /// UpdateNeighbors also re-runs UpdatePaths, which ASSIGNS path.nextPaths rather than
        /// appending, so it is idempotent - Tile.Start() running it again later is harmless.
        /// </summary>
        public static void Relink(List<GameObject> tiles)
        {
            if (tiles == null || tiles.Count == 0)
                return;

            Physics.SyncTransforms();

            foreach (var instance in tiles)
            {
                if (!instance)
                    continue;

                var tile = instance.GetComponent<PolyPerfect.City.Tile>();
                if (tile)
                    tile.UpdateNeighbors();
            }
        }
    }
}
