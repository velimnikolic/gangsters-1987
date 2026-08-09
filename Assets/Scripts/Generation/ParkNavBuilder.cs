using System.Collections.Generic;
using UnityEngine;
using PolyPerfect.City;

namespace LivingCity.Generation
{
    /// <summary>
    /// The scene half of the park's pedestrian nav: reads the REAL sidewalk endpoints off the
    /// neighbouring road tile instances (the anchors ParkLayout plans its entrances against),
    /// and builds one nav Tile per park cell from the polylines ParkNavPlan routes.
    ///
    /// The anchors are read live rather than assumed because only the instance knows whether a
    /// straight was rolled as a crosswalk - the 20% prefab swap inside RoadTileKind.Straight.
    /// A crosswalk's crossing walk ends exactly on the shared cell boundary, which is the one
    /// linkable point mid-side; a plain straight offers only its corner endpoints.
    ///
    /// The Tile lifecycle is the trap the whole builder is shaped around. Tile.Awake is the
    /// only collector of Path children into tile.paths/sidewalkPaths - and in the editor spawn
    /// path Awake never runs, so the lists are filled here explicitly and serialize into the
    /// saved scene. At Play the scene-load Awake re-collects the same children into the same
    /// lists, and Start's UpdatePaths relinks. Everything is built on an INACTIVE root with the
    /// Tile added last, so the runtime path - where AddComponent fires Awake immediately -
    /// converges on the identical state.
    /// </summary>
    public static class ParkNavBuilder
    {
        /// <summary>Authored cell size, what the collider and node locals are expressed in.</summary>
        const float Authored = CityGrid.AuthoredCellSize;

        /// <summary>A road-tile path endpoint within this of the shared boundary is a crossing end.</summary>
        const float BoundaryTolerance = 1f;

        /// <summary>
        /// The road tiles by grid cell, recovered from the names RoadNetworkBuilder stamps.
        /// Built once per city build and handed to every park block.
        /// </summary>
        public static Dictionary<Vector2Int, GameObject> IndexRoadTiles(List<GameObject> roadTiles)
        {
            var byCell = new Dictionary<Vector2Int, GameObject>();
            if (roadTiles == null)
                return byCell;

            foreach (var tile in roadTiles)
            {
                if (!tile || !tile.name.StartsWith("tile_", System.StringComparison.Ordinal))
                    continue;
                var parts = tile.name.Split('_');
                if (parts.Length < 4)
                    continue;
                if (!int.TryParse(parts[1], out var x) || !int.TryParse(parts[2], out var z))
                    continue;
                byCell[new Vector2Int(x, z)] = tile;
            }

            return byCell;
        }

        /// <summary>
        /// Every linkable sidewalk endpoint on the road tiles facing the park, classified: a
        /// crossing end ON the shared boundary (crosswalk - the preferred entrance), or a
        /// corner node a sidewalk-offset in from the kerb. Falls back to nothing on a side
        /// whose tile cannot be read; ParkLayout then uses its measured table.
        /// </summary>
        public static List<ParkLayout.EntranceAnchor> Anchors(
            CityGrid grid, List<Vector2Int> cells,
            IReadOnlyDictionary<Vector2Int, GameObject> roadTilesByCell,
            float clearance, float mainClearance)
        {
            var anchors = new List<ParkLayout.EntranceAnchor>();
            if (roadTilesByCell == null || roadTilesByCell.Count == 0)
                return anchors;

            var inBlock = new HashSet<Vector2Int>(cells);
            var legs = new[]
            {
                new Vector2Int(1, 0), new Vector2Int(0, 1),
                new Vector2Int(-1, 0), new Vector2Int(0, -1),
            };

            foreach (var cell in cells)
            {
                for (var side = 0; side < 4; side++)
                {
                    var neighbour = cell + legs[side];
                    if (inBlock.Contains(neighbour) || !grid.IsRoad(neighbour.x, neighbour.y))
                        continue;
                    if (!roadTilesByCell.TryGetValue(neighbour, out var road) || !road)
                        continue;
                    var tile = road.GetComponent<Tile>();
                    if (!tile || tile.sidewalkPaths == null)
                        continue;

                    var avenue = grid.IsMainRoad(neighbour.x, neighbour.y);

                    // The shared boundary, as a world line on this side's axis.
                    var cellCentre = grid.CellToWorld(cell);
                    var axis = side % 2 == 0 ? cellCentre.x : cellCentre.z;
                    var boundary = axis + (side < 2 ? 1f : -1f) * CityGrid.CellSize * 0.5f;

                    foreach (var path in tile.sidewalkPaths)
                    {
                        if (!path || path.pathPositions == null || path.pathPositions.Count == 0)
                            continue;
                        foreach (var node in new[]
                                 {
                                     path.pathPositions[0],
                                     path.pathPositions[path.pathPositions.Count - 1],
                                 })
                        {
                            if (!node)
                                continue;
                            var p = node.position;
                            var pos = new Vector2(p.x, p.z);
                            var at = side % 2 == 0 ? pos.x : pos.y;
                            var toBoundary = (at - boundary) * (side < 2 ? 1f : -1f);

                            // On the boundary: a crossing end pointing into the park. Anything
                            // deeper into the road tile than the pavement band is the far side
                            // of the street, not an entrance anchor.
                            bool onBoundary = Mathf.Abs(toBoundary) < BoundaryTolerance;
                            var pavementBand = toBoundary > 2f
                                && toBoundary < CityGrid.CellSize * 0.5f;
                            if (!onBoundary && !pavementBand)
                                continue;

                            if (Contains(anchors, pos))
                                continue;
                            anchors.Add(new ParkLayout.EntranceAnchor
                            {
                                Pos = pos,
                                Side = side,
                                OnBoundary = onBoundary,
                                Avenue = avenue,
                            });
                        }
                    }
                }
            }

            return anchors;
        }

        static bool Contains(List<ParkLayout.EntranceAnchor> anchors, Vector2 pos)
        {
            foreach (var anchor in anchors)
                if ((anchor.Pos - pos).sqrMagnitude < 0.04f)
                    return true;
            return false;
        }

        /// <summary>
        /// One nav Tile per park cell that has paths, plus a Relink batch that includes the
        /// facing road tiles - THEIR nextPaths were built before the park existed and must be
        /// rebuilt to see the new first nodes.
        /// </summary>
        public static List<GameObject> Build(
            ParkLayout.Plan plan, CityGrid grid, List<Vector2Int> cells,
            IReadOnlyDictionary<Vector2Int, GameObject> roadTilesByCell,
            Transform parent, List<GameObject> placed, List<string> warnings)
        {
            var navTiles = new List<GameObject>();
            var cellPaths = ParkNavPlan.ForPlan(plan, grid, cells, warnings);
            if (cellPaths.Count == 0)
                return navTiles;

            foreach (var entry in cellPaths)
            {
                var centre = grid.CellToWorld(entry.Cell);
                var root = new GameObject($"parknav_{entry.Cell.x}_{entry.Cell.y}");

                // Inactive before ANY component work - see the class doc. Reactivated after
                // the Tile is complete, so a runtime Awake sees the finished children.
                root.SetActive(false);
                root.transform.SetParent(parent, false);
                root.transform.SetPositionAndRotation(
                    new Vector3(centre.x, 0f, centre.z), Quaternion.identity);
                root.transform.localScale = Vector3.one * CityGrid.TileScale;
                root.layer = 10;

                // What the neighbours' OverlapBox probe hits. Thin and slightly sunk so it
                // duplicates the grass tile's collider without adding a walkable step.
                var collider = root.AddComponent<BoxCollider>();
                collider.size = new Vector3(Authored, 0.4f, Authored);
                collider.center = new Vector3(0f, -0.3f, 0f);

                var sidewalks = new List<Path>(entry.Paths.Count);
                for (var p = 0; p < entry.Paths.Count; p++)
                {
                    var points = entry.Paths[p];
                    var pathObject = new GameObject($"walk_{p}");
                    pathObject.transform.SetParent(root.transform, false);
                    var path = pathObject.AddComponent<Path>();
                    path.pathType = PathType.Sidewalk;
                    path.speed = 0;

                    for (var i = 0; i < points.Length; i++)
                    {
                        var node = new GameObject($"n{i}");
                        node.transform.SetParent(pathObject.transform, false);
                        var world = new Vector3(points[i].x, 0f, points[i].y);
                        node.transform.localPosition =
                            (world - root.transform.position) / CityGrid.TileScale;

                        // The rotation is data: HumanBehavior scatters walkers along each
                        // node's RIGHT, so right must be the walk's normal.
                        var ahead = i + 1 < points.Length
                            ? points[i + 1] - points[i]
                            : points[i] - points[i - 1];
                        if (ahead.sqrMagnitude > 1e-6f)
                            node.transform.localRotation = Quaternion.LookRotation(
                                new Vector3(ahead.x, 0f, ahead.y));
                        path.pathPositions.Add(node.transform);
                    }
                    sidewalks.Add(path);
                }

                // The Tile LAST, lists filled by hand - the editor path never runs Awake.
                var tile = root.AddComponent<Tile>();
                tile.tileType = Tile.TileType.OnlyPathwalk;
                tile.tileShape = Tile.TileShape.Cross;
                tile.paths = new List<Path>();
                tile.sidewalkPaths = sidewalks;

                root.SetActive(true);
                navTiles.Add(root);
                placed?.Add(root);
            }

            // Relink the batch AND the facing road tiles, whose outgoing links predate the park.
            var relink = new List<GameObject>(navTiles);
            if (roadTilesByCell != null)
            {
                var inBlock = new HashSet<Vector2Int>(cells);
                var legs = new[]
                {
                    new Vector2Int(1, 0), new Vector2Int(0, 1),
                    new Vector2Int(-1, 0), new Vector2Int(0, -1),
                };
                var seen = new HashSet<GameObject>();
                foreach (var cell in cells)
                foreach (var leg in legs)
                {
                    var neighbour = cell + leg;
                    if (inBlock.Contains(neighbour))
                        continue;
                    if (roadTilesByCell.TryGetValue(neighbour, out var road)
                        && road && seen.Add(road))
                        relink.Add(road);
                }
            }
            RoadNetworkBuilder.Relink(relink);

            return navTiles;
        }
    }
}
