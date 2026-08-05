using System.Collections.Generic;
using UnityEngine;
using LivingCity.Data;

namespace LivingCity.Generation
{
    /// <summary>
    /// Lays a ground slab under each block.
    ///
    /// Without it the ground between the buildings is whatever the neighbouring road tiles
    /// happen to show, which is their green verge - so courtyards and alleys read as lawns
    /// rather than as a city. The slab covers the same expanded rectangle the buildings use,
    /// which means it also paves the strip of verge between the pavement and the street wall.
    ///
    /// The slab is no longer one concrete tile everywhere: a palette may carry weighted
    /// per-block choices (concrete, asphalt, packed dirt), rolled per block off this placer's
    /// own seed stream, and a courtyard tile laid over the block interior behind the rear
    /// walls - so the yard a terrace ring encloses reads as a yard, not as more street.
    ///
    /// tile-plain_concrete is safe to scale and repeat: unlike the road tiles it carries no
    /// Tile component, so it never registers in Tile.Tiles and can never be picked as a
    /// pathfinding destination. It is pure geometry with a mesh collider. The same holds for
    /// the other tile-plain_* grounds.
    ///
    /// tile-park is the exception, and the reason ZonePalette.groundIsTilePerCell exists. It
    /// DOES carry a Tile - tileType OnlyPathwalk, with sidewalk paths whose nodes run to +/-15,
    /// exactly the tile boundary the road tiles use. Laid unscaled at a cell centre it therefore
    /// links straight into the pavement network and pedestrians walk through the park, while
    /// cars stay out because the tile carries no road paths. Stretching one park tile over the
    /// whole block would drag those nodes off the 30m grid, put them out of the 1.5m link
    /// tolerance, and silently sever the connection - so parks are laid per cell instead, and
    /// the per-cell path never rolls grounds or courtyards.
    /// </summary>
    public static class GroundPlacer
    {
        /// <summary>
        /// Lifted fractionally so it wins the depth test against the road tile's verge where
        /// the two overlap. Well below the 4-unit pavement line, so it never buries the kerb.
        /// </summary>
        const float GroundLift = 0.02f;

        /// <summary>
        /// How far the courtyard rect sits in from the block rect - one building depth. The
        /// 4- and 5-floor terrace pieces measure 13.4 and 15.7m deep, so 16 clears both and
        /// the courtyard tile starts just behind the rear walls.
        /// </summary>
        const float CourtyardInset = 16f;

        /// <summary>
        /// Smallest courtyard worth drawing - below this it is a sliver between rear walls,
        /// not a yard.
        /// </summary>
        const float MinCourtyardSize = 8f;

        public static List<GameObject> Build(
            CityGrid grid,
            PrefabDatabase prefabs,
            CityConfig config,
            Transform parent,
            SpawnPrefab spawn = null)
        {
            var placed = new List<GameObject>();
            spawn ??= RoadNetworkBuilder.RuntimeSpawn;

            if (!prefabs.groundTile)
            {
                Debug.LogWarning("[GroundPlacer] No ground tile assigned - blocks will show the " +
                                 "road tiles' grass verge instead of concrete.");
                return placed;
            }

            // Own stream, iterated in blockId order like every other placer, so the ground mix
            // is deterministic per seed and never shifts the building or zoning draws.
            var rng = new System.Random(config.seed + SeedOffsets.Ground);

            for (var blockId = 0; blockId < grid.BlockCount; blockId++)
            {
                var cells = new List<Vector2Int>(grid.CellsInBlock(blockId));
                if (cells.Count == 0)
                    continue;

                var zone = grid.ZoneOf(blockId);
                var palette = prefabs.PaletteFor(zone);

                if (palette != null && palette.groundIsTilePerCell)
                {
                    var perCell = palette.ground ? palette.ground : prefabs.groundTile;
                    LayPerCell(grid, cells, perCell, blockId, zone, parent, spawn, placed);
                    continue;
                }

                // Slab first, courtyard second - a fixed draw order per block is what keeps
                // the same seed producing the same city.
                var tile = PickGround(palette, prefabs, rng);
                LaySlab(grid, cells, tile, blockId, zone, parent, spawn, placed);

                if (palette != null && palette.courtyardGrounds != null && palette.courtyardGrounds.Length > 0)
                    LayCourtyard(grid, cells, palette, blockId, zone, parent, spawn, rng, placed);
            }

            return placed;
        }

        /// <summary>
        /// Weighted per-block choice from the palette's grounds; falls back to the palette's
        /// single ground, then the shared concrete. Zones without the list cost no rng draws,
        /// so adding grounds to one palette cannot reshuffle another's.
        /// </summary>
        static GameObject PickGround(PrefabDatabase.ZonePalette palette, PrefabDatabase prefabs, System.Random rng)
        {
            if (palette?.grounds != null && palette.grounds.Length > 0)
            {
                var usable = new List<PrefabDatabase.WeightedPrefabs>();
                var total = 0f;
                foreach (var ground in palette.grounds)
                    if (ground != null && ground.IsUsable)
                    {
                        usable.Add(ground);
                        total += ground.weight;
                    }

                var pick = WeightedRoll.Pick(usable, total, rng);
                if (pick != null)
                    return pick.prefabs[rng.Next(pick.prefabs.Length)];
            }

            return palette != null && palette.ground ? palette.ground : prefabs.groundTile;
        }

        /// <summary>One stretched tile over the whole block - the default, and the cheapest.</summary>
        static void LaySlab(
            CityGrid grid,
            List<Vector2Int> cells,
            GameObject tile,
            int blockId,
            BlockZone zone,
            Transform parent,
            SpawnPrefab spawn,
            List<GameObject> placed)
        {
            var (min, max) = BlockBuilder.BlockRect(grid, cells);
            if (max.x <= min.x || max.y <= min.y)
                return;

            var slab = LayRect(tile, min, max, GroundLift, $"ground_{zone}_{blockId}", parent, spawn);
            if (slab)
                placed.Add(slab);
        }

        /// <summary>
        /// A second, smaller slab over the block interior, one building depth in from every
        /// edge, so the space a terrace ring encloses reads as a yard instead of the same
        /// surface as the street. Even the smallest block earns one: a 1-cell rect is 46m, so
        /// the yard is 14m across - the light-well the class comment describes.
        /// </summary>
        static void LayCourtyard(
            CityGrid grid,
            List<Vector2Int> cells,
            PrefabDatabase.ZonePalette palette,
            int blockId,
            BlockZone zone,
            Transform parent,
            SpawnPrefab spawn,
            System.Random rng,
            List<GameObject> placed)
        {
            var (min, max) = BlockBuilder.BlockRect(grid, cells);

            var innerMin = new Vector2(min.x + CourtyardInset, min.y + CourtyardInset);
            var innerMax = new Vector2(max.x - CourtyardInset, max.y - CourtyardInset);

            if (innerMax.x - innerMin.x < MinCourtyardSize || innerMax.y - innerMin.y < MinCourtyardSize)
                return;

            var tile = palette.courtyardGrounds[rng.Next(palette.courtyardGrounds.Length)];
            if (!tile)
                return;

            // Twice the slab lift: above the base slab, still far below the 4-unit pavement.
            var court = LayRect(tile, innerMin, innerMax, GroundLift * 2f,
                                $"ground_court_{zone}_{blockId}", parent, spawn);
            if (court)
                placed.Add(court);
        }

        /// <summary>Stretches one ground tile over a world-space rectangle.</summary>
        static GameObject LayRect(
            GameObject tile,
            Vector2 min,
            Vector2 max,
            float lift,
            string name,
            Transform parent,
            SpawnPrefab spawn)
        {
            // Measure the source tile rather than assuming it is 30x30, so a different ground
            // prefab can be dropped in without the scale silently going wrong.
            var tileSize = PrefabBounds.FootprintXZ(tile, 0f);
            if (tileSize.x <= 0.01f || tileSize.y <= 0.01f)
            {
                Debug.LogWarning($"[GroundPlacer] Ground tile '{tile.name}' has no measurable footprint.");
                return null;
            }

            var centre = new Vector3((min.x + max.x) * 0.5f, lift, (min.y + max.y) * 0.5f);
            var slab = spawn(tile, centre, Quaternion.identity, parent);

            slab.transform.localScale = new Vector3(
                (max.x - min.x) / tileSize.x,
                1f,
                (max.y - min.y) / tileSize.y);

            slab.name = name;
            return slab;
        }

        /// <summary>
        /// One unscaled tile per cell. For tile-park, whose Tile component carries sidewalk paths
        /// authored to the 30m cell - see the note on this class. The tiles cover only the
        /// block's own cells, not the expanded rect the buildings use, so an 8m strip of the road
        /// tile's grass verge shows between the park and the pavement. For a park that reads
        /// correctly, which is why no attempt is made to fill it.
        /// </summary>
        static void LayPerCell(
            CityGrid grid,
            List<Vector2Int> cells,
            GameObject tile,
            int blockId,
            BlockZone zone,
            Transform parent,
            SpawnPrefab spawn,
            List<GameObject> placed)
        {
            foreach (var cell in cells)
            {
                var centre = grid.CellToWorld(cell);
                centre.y = GroundLift;

                var instance = spawn(tile, centre, Quaternion.identity, parent);
                instance.name = $"ground_{zone}_{blockId}_{cell.x}_{cell.y}";
                placed.Add(instance);
            }
        }
    }
}
