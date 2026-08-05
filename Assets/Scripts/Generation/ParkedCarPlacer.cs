using System.Collections.Generic;
using UnityEngine;
using LivingCity.Data;

namespace LivingCity.Generation
{
    /// <summary>
    /// Static (non-AI) cars along the kerb.
    ///
    /// Measured tile geometry (tile-road-straight, resolved through the prefab hierarchy):
    ///
    ///   driving lanes   x = +/-1.5
    ///   sidewalk paths  x = +/-4
    ///   street props    x = +/-5.5   (StreetPropPlacer.VergeOffset - lamps, trees)
    ///   building face   x = +/-7     (BlockBuilder.SidewalkClearance)
    ///   tile edge       x = +/-15
    ///
    /// So a parked car has exactly one band to live in: between the walking line and the wall.
    /// The offset used to be 7.5, which is BEHIND the building face - a car two units wide sat
    /// half inside the masonry, which is what made buses look parked inside shopfronts.
    ///
    /// That band is also where the lamps and trees are, so this runs after StreetPropPlacer and
    /// rejects any slot already occupied. If AI cars are ever seen clipping parked ones, raise
    /// KerbOffset - but not past the building face, and check the props still clear.
    /// </summary>
    public static class ParkedCarPlacer
    {
        /// <summary>Between the pavement (4) and the building line (7). A car is about 2 wide.</summary>
        const float KerbOffset = 5.6f;

        /// <summary>
        /// Deliberately sparse - a solid rank of cars down both sides of every straight reads as
        /// a car park, not a street. The effective rate is lower than this: slots blocked by a
        /// tree canopy are dropped afterwards, which is roughly half of them. This is the one
        /// number to turn if the street looks too empty or too full.
        /// </summary>
        const float SpawnChancePerSlot = 0.3f;

        /// <summary>
        /// Longest vehicle that fits a slot. StreetPropPlacer puts lamps and trees at -9, 0 and
        /// +9 along the same edge, leaving about 9 units clear either side of centre.
        /// </summary>
        const float MaxParkedLength = 6.5f;

        /// <summary>Clearance kept from lamps and trees when testing a slot.</summary>
        const float PropClearance = 0.4f;

        /// <summary>
        /// Centred in the gaps between StreetPropPlacer's -9 / 0 / +9 prop positions. Parking on
        /// top of the props and relying on the overlap test to reject would work, but it throws
        /// away most slots; sitting in the gaps means the test only catches wide canopies.
        /// </summary>
        static readonly float[] SlotOffsets = { -4.5f, 4.5f };

        public static List<GameObject> Build(
            CityGrid grid,
            PrefabDatabase prefabs,
            CityConfig config,
            Transform parent,
            SpawnPrefab spawn = null,
            IReadOnlyList<GameObject> obstacles = null)
        {
            var placed = new List<GameObject>();
            spawn ??= RoadNetworkBuilder.RuntimeSpawn;

            var rng = new System.Random(config.seed + SeedOffsets.ParkedCars);
            var picker = new VehiclePicker(prefabs.parkedCarGroups, rng);

            if (picker.IsEmpty)
                return placed;

            var blocked = FootprintsOf(obstacles);

            foreach (var cell in grid.RoadCells())
            {
                // Only park along straight stretches. Junctions and curves have crossing
                // paths and turning traffic that a parked car would foul.
                var sides = grid.GetNeighborMask(cell.x, cell.y);
                var northSouth = sides == (Sides.North | Sides.South);
                var eastWest = sides == (Sides.East | Sides.West);
                if (!northSouth && !eastWest)
                    continue;

                var centre = grid.CellToWorld(cell);
                var along = northSouth ? Vector3.forward : Vector3.right;
                var outward = northSouth ? Vector3.right : Vector3.forward;

                for (var sign = -1; sign <= 1; sign += 2)
                {
                    var side = new Vector2Int(
                        cell.x + (northSouth ? sign : 0),
                        cell.y + (northSouth ? 0 : sign));

                    // Only park where the kerb backs onto a block.
                    if (grid.IsRoad(side.x, side.y) || !grid.InBounds(side.x, side.y))
                        continue;

                    foreach (var slot in SlotOffsets)
                    {
                        if (rng.NextDouble() > SpawnChancePerSlot)
                            continue;

                        var prefab = picker.Next(maxLength: MaxParkedLength);
                        if (!prefab)
                            continue;

                        var position = centre + outward * (KerbOffset * sign) + along * slot;

                        // Parallel to the road, facing either way down the street.
                        var yaw = Mathf.Atan2(along.x, along.z) * Mathf.Rad2Deg + (rng.Next(2) == 0 ? 0f : 180f);

                        if (Overlaps(blocked, prefab, position, yaw))
                            continue;

                        placed.Add(spawn(prefab, position, Quaternion.Euler(0f, yaw, 0f), parent));
                    }
                }
            }

            return placed;
        }

        /// <summary>
        /// World-space XZ footprints of everything already standing in the verge. Read off live
        /// Renderers rather than PrefabBounds because these are instances, and both the play-mode
        /// and editor generation paths instantiate synchronously - the bounds are valid by now.
        /// </summary>
        static List<Bounds> FootprintsOf(IReadOnlyList<GameObject> obstacles)
        {
            var bounds = new List<Bounds>();
            if (obstacles == null)
                return bounds;

            foreach (var obstacle in obstacles)
            {
                if (!obstacle)
                    continue;

                var renderers = obstacle.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0)
                    continue;

                var combined = renderers[0].bounds;
                for (var i = 1; i < renderers.Length; i++)
                    combined.Encapsulate(renderers[i].bounds);

                bounds.Add(combined);
            }

            return bounds;
        }

        /// <summary>
        /// XZ-only overlap test. Height is ignored on purpose: a tree's canopy sits well above a
        /// car, but a car parked under it still looks wrong because the trunk is in the way.
        /// </summary>
        static bool Overlaps(List<Bounds> blocked, GameObject prefab, Vector3 position, float yaw)
        {
            if (blocked.Count == 0)
                return false;

            // FootprintXZ swaps the axes for the quarter-turns, which is all a kerbside car ever
            // gets - it is always parallel to a grid-aligned street.
            var half = PrefabBounds.FootprintXZ(prefab, yaw) * 0.5f
                     + new Vector2(PropClearance, PropClearance);

            var min = new Vector2(position.x - half.x, position.z - half.y);
            var max = new Vector2(position.x + half.x, position.z + half.y);

            foreach (var other in blocked)
            {
                if (other.min.x < max.x && other.max.x > min.x &&
                    other.min.z < max.y && other.max.z > min.y)
                    return true;
            }

            return false;
        }
    }
}
