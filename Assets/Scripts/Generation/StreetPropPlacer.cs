using System.Collections.Generic;
using UnityEngine;
using LivingCity.Data;

namespace LivingCity.Generation
{
    /// <summary>
    /// Dresses the band between the pavement and the block edge.
    ///
    /// Measured tile geometry (tile-road-straight, resolved through the prefab hierarchy):
    ///   driving lanes  x = +/-1.5
    ///   sidewalk paths x = +/-4
    ///   tile edge      x = +/-15
    /// So anything from about 5.5 outwards is clear of both the lanes and the walking route.
    ///
    /// That band used to be the road tile's grass verge. It is now surfaced end to end by
    /// GroundPlacer's apron, so these props stand on pavement - which is what a street lamp
    /// wants, and reads as a tree pit for the trees. Beside the park the apron is grass rather
    /// than concrete, so there the same props stand on a lawn that carries on past them into the
    /// park; a lamp on a park verge is right too, and the alternative was a 10m concrete ring
    /// that made the park look set back from its own street.
    /// </summary>
    public static class StreetPropPlacer
    {
        /// <summary>
        /// Distance from the road centreline to the prop line. Sits in the gap between the
        /// pavement (4) and the building line (BlockBuilder.SidewalkClearance, 7), so lamps and
        /// trees stand between the kerb and the walls rather than inside either.
        /// </summary>
        const float VergeOffset = 5.5f;

        /// <summary>
        /// The same line on the dual carriageway, whose cross-section is wider throughout:
        /// outer lanes at 4.75 and pavements at 7.25, against a street's 1.5 and 4. At 5.5 a
        /// lamp would stand in the avenue's outer LANE. 8.5 keeps the same relation - clear of
        /// the pavement, inside the building line at CityConfig.mainSidewalkWidth (10).
        /// </summary>
        const float MainVergeOffset = 8.5f;

        /// <summary>Lamps every other tile, i.e. every 60 units.</summary>
        const int LampTileInterval = 2;

        static readonly float[] AlongOffsets = { -9f, 0f, 9f };

        public static List<GameObject> Build(
            CityGrid grid,
            PrefabDatabase prefabs,
            CityConfig config,
            Transform parent,
            SpawnPrefab spawn = null,
            List<Bounds> gateKeepOuts = null)
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

                    // A car park is fenced at the pavement edge (BlockBuilder.ClearanceFor), so
                    // the prop line at 5.5 is INSIDE its lot - a lamp here would stand among the
                    // parked cars and a tree would grow through the tarmac. Its own scatter
                    // dresses the apron instead.
                    if (BlockBuilder.IsCarParkAt(grid, prefabs, neighbour.x, neighbour.y)) continue;

                    var outward = side.direction;
                    var along = new Vector3(-outward.z, 0f, outward.x);

                    // Props face back toward the road they serve.
                    var yaw = Mathf.Atan2(-outward.x, -outward.z) * Mathf.Rad2Deg;
                    var rotation = Quaternion.Euler(0f, yaw, 0f);

                    var lampHere = (cell.x + cell.y) % LampTileInterval == 0;
                    var verge = grid.IsMainRoad(cell.x, cell.y) ? MainVergeOffset : VergeOffset;

                    for (var i = 0; i < AlongOffsets.Length; i++)
                    {
                        var position = centre + outward * verge + along * AlongOffsets[i];
                        var middle = i == 1;

                        GameObject prefab = null;

                        if (middle && lampHere)
                            prefab = Pick(prefabs.streetLamps, rng);
                        else if (!middle && rng.NextDouble() < 0.45)
                            prefab = Pick(prefabs.trees, rng);
                        else if (rng.NextDouble() < 0.18)
                            prefab = Pick(prefabs.smallProps, rng);

                        if (!prefab) continue;

                        // A prop stands down for a gate: nothing between the kerb and a
                        // compound entrance, because a tree in front of a lorry gate blocks
                        // the one hole the wall has. Tested AFTER the draws so the rng stream
                        // is untouched - every other prop in the city stays exactly where it
                        // was, which keeps a before/after diff readable. Skipped, not nudged:
                        // a nudge under the 9m slot pitch just leans the tree on a gate pier.
                        if (Blocked(position, gateKeepOuts)) continue;

                        var instance = spawn(prefab, position, rotation, parent);
                        placed.Add(instance);
                    }
                }
            }

            return placed;
        }

        static GameObject Pick(GameObject[] options, System.Random rng) =>
            options == null || options.Length == 0 ? null : options[rng.Next(options.Length)];

        static bool Blocked(Vector3 position, List<Bounds> keepOuts)
        {
            if (keepOuts == null)
                return false;

            foreach (var keepOut in keepOuts)
                if (keepOut.Contains(position))
                    return true;

            return false;
        }

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
