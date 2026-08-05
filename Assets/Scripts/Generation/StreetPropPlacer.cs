using System.Collections.Generic;
using UnityEngine;
using LivingCity.Data;

namespace LivingCity.Generation
{
    /// <summary>
    /// Dresses the verge between the pavement and the block edge.
    ///
    /// Measured tile geometry (tile-road-straight, resolved through the prefab hierarchy):
    ///   driving lanes  x = +/-1.5
    ///   sidewalk paths x = +/-4
    ///   tile edge      x = +/-15
    /// So anything from about 5.5 outwards is clear of both the lanes and the walking route.
    /// </summary>
    public static class StreetPropPlacer
    {
        /// <summary>
        /// Distance from the road centreline to the prop line. Sits in the gap between the
        /// pavement (4) and the building line (BlockBuilder.SidewalkClearance, 7), so lamps and
        /// trees stand between the kerb and the walls rather than inside either.
        /// </summary>
        const float VergeOffset = 5.5f;

        /// <summary>Lamps every other tile, i.e. every 60 units.</summary>
        const int LampTileInterval = 2;

        static readonly float[] AlongOffsets = { -9f, 0f, 9f };

        public static List<GameObject> Build(
            CityGrid grid,
            PrefabDatabase prefabs,
            CityConfig config,
            Transform parent,
            SpawnPrefab spawn = null)
        {
            var placed = new List<GameObject>();
            spawn ??= RoadNetworkBuilder.RuntimeSpawn;
            var rng = new System.Random(config.seed + SeedOffsets.Props);

            foreach (var cell in grid.RoadCells())
            {
                var centre = grid.CellToWorld(cell);

                foreach (var side in Sides4)
                {
                    var neighbour = new Vector2Int(cell.x + side.step.x, cell.y + side.step.y);

                    // Only dress edges that face a block - not edges facing another road.
                    if (grid.IsRoad(neighbour.x, neighbour.y)) continue;
                    if (!grid.InBounds(neighbour.x, neighbour.y)) continue;

                    var outward = side.direction;
                    var along = new Vector3(-outward.z, 0f, outward.x);

                    // Props face back toward the road they serve.
                    var yaw = Mathf.Atan2(-outward.x, -outward.z) * Mathf.Rad2Deg;
                    var rotation = Quaternion.Euler(0f, yaw, 0f);

                    var lampHere = (cell.x + cell.y) % LampTileInterval == 0;

                    for (var i = 0; i < AlongOffsets.Length; i++)
                    {
                        var position = centre + outward * VergeOffset + along * AlongOffsets[i];
                        var middle = i == 1;

                        GameObject prefab = null;

                        if (middle && lampHere)
                            prefab = Pick(prefabs.streetLamps, rng);
                        else if (!middle && rng.NextDouble() < 0.45)
                            prefab = Pick(prefabs.trees, rng);
                        else if (rng.NextDouble() < 0.18)
                            prefab = Pick(prefabs.smallProps, rng);

                        if (!prefab) continue;

                        var instance = spawn(prefab, position, rotation, parent);
                        placed.Add(instance);
                    }
                }
            }

            return placed;
        }

        static GameObject Pick(GameObject[] options, System.Random rng) =>
            options == null || options.Length == 0 ? null : options[rng.Next(options.Length)];

        struct Side
        {
            public Vector2Int step;
            public Vector3 direction;
        }

        static readonly Side[] Sides4 =
        {
            new() { step = new Vector2Int(0, 1), direction = Vector3.forward },
            new() { step = new Vector2Int(1, 0), direction = Vector3.right },
            new() { step = new Vector2Int(0, -1), direction = Vector3.back },
            new() { step = new Vector2Int(-1, 0), direction = Vector3.left },
        };
    }
}
