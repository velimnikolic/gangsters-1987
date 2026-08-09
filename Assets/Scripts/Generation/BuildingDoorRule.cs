using UnityEngine;

namespace LivingCity.Generation
{
    /// <summary>
    /// Which placed buildings get a <see cref="Entities.BuildingDoor"/>, and why. Pure logic
    /// over CityGrid - which is a plain C# type, not a ScriptableObject - so the rule can be
    /// run headlessly over real generated cities in PedestrianModelTests instead of being
    /// eyeballed in Play.
    ///
    /// The hard part is not the door position (InteractionMarkers already derives that from
    /// mesh bounds) but "does this facade actually front a pavement". Answering it needs no
    /// new geometry, only the street cross-section the city already has:
    ///
    ///   distances from the road centreline - facades stand at 7m (10m on the avenue), while
    ///   the boundary between a road cell and a block cell is at CellSize/2 (15m authored, and
    ///   whatever CityGrid.TileScale makes of it - the rule is a ratio, so it does not care).
    ///
    /// So a facade that fronts the street stands INSIDE the road cell (blocks expand out of
    /// their own cells into the road tile by BlockBuilder's Reach), and a facade turned toward
    /// the interior of the block - the rear and alley pieces - stands in a block cell. One
    /// IsRoad test on the door itself separates them, with no constant of our own.
    ///
    /// The second test looks BEHIND the facade: 10m back from the door has to belong to a real
    /// numbered block. That rejects a wall standing in a road cell with nothing behind it -
    /// facing across the carriageway at another road, or off the edge of the map.
    ///
    /// One block id covers both of those, and covers Empty (the land outside the city that the
    /// exit roads run through) as well: AssignBlockIds numbers CellType.Block cells only, and
    /// leaves everything else at NoBlock. An IsRoad test behind the facade would be strictly
    /// weaker and strictly redundant - it was here until a mutation run showed no assertion
    /// could tell the two versions apart.
    /// </summary>
    public static class BuildingDoorRule
    {
        /// <summary>
        /// How far behind the door to sample for "there is a building's own block back here".
        /// Comfortably past the facade line (7m) and short of the far cell boundary.
        /// </summary>
        public const float BehindDepth = 10f;

        /// <summary>
        /// Prefab-name gate, run before any geometry. "building-" also does the work of
        /// excluding the whole industrial catalogue, every piece of which is named industry-*.
        /// </summary>
        public static bool NameQualifies(string name)
        {
            if (string.IsNullOrEmpty(name) || !name.StartsWith("building-"))
                return false;

            // A corner piece's forward aligns the quadrant bisector rather than a face, so its
            // "facade centre" would be inside a wall - the same exclusion InteractionMarkers
            // already makes for shopfronts.
            if (name.Contains("corner"))
                return false;

            // Vehicle doors, not people doors: building-policestation-garage is an Industrial
            // outbuilding and its opening is a ramp.
            if (name.Contains("garage"))
                return false;

            return true;
        }

        /// <summary>The grid cell a world point falls in. CellToWorld puts cell centres on multiples of CellSize.</summary>
        public static Vector2Int CellAt(Vector3 world) => new Vector2Int(
            Mathf.RoundToInt(world.x / CityGrid.CellSize),
            Mathf.RoundToInt(world.z / CityGrid.CellSize));

        /// <summary>
        /// Does this placed building front a pavement people can reach, in a zone people would
        /// walk into? <paramref name="facing"/> is the flattened facade normal (transform.forward).
        /// </summary>
        public static bool Eligible(CityGrid grid, string name, Vector3 doorWorld, Vector3 facing)
        {
            if (grid == null || !NameQualifies(name))
                return false;

            var flat = facing;
            flat.y = 0f;
            if (flat.sqrMagnitude < 1e-6f)
                return false;

            var front = CellAt(doorWorld);
            if (!grid.IsRoad(front.x, front.y))
                return false;

            var behind = CellAt(doorWorld - flat.normalized * BehindDepth);
            var blockId = grid.BlockIdAt(behind.x, behind.y);
            if (blockId < 0)
                return false;

            return ZoneAdmitsVisitors(grid.ZoneOf(blockId));
        }

        /// <summary>
        /// Nobody strolls home into a factory yard, a car park, or a walled container
        /// terminal. Everything else the city builds a street wall out of - the terraces, the
        /// shops mixed into them, the civic landmarks - is somewhere a person plausibly comes
        /// out of.
        /// </summary>
        public static bool ZoneAdmitsVisitors(BlockZone zone) =>
            zone != BlockZone.Industrial && zone != BlockZone.Parking && zone != BlockZone.Port;
    }
}
