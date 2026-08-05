using System.Collections.Generic;
using UnityEngine;
using PolyPerfect.City;

namespace LivingCity.Generation
{
    /// <summary>
    /// Gives every path that leads nowhere a U-turn back the way it came.
    ///
    /// Why this is needed at all: the city is a rectangle whose streets are cut by the map
    /// boundary (see RoadTileTable's one-connection cases). On a boundary tile one traffic lane
    /// and one pavement lane run off the edge and stop. Tile.GetNextPaths only ever links a
    /// path to paths on NEIGHBOUR tiles, and past the boundary there is no neighbour, so those
    /// lanes end with an empty nextPaths.
    ///
    /// That is not cosmetic. CarBehavior picks a random tile as its destination; roughly a
    /// fifth of the tiles are boundary tiles, so cars land on a dead-end lane often. Arriving
    /// there, SetNewPath finds no route out, and after ten such misses the car deactivates
    /// itself. Traffic would visibly drain toward the edges of the map.
    ///
    /// The fix is to link the dead end to the lane running the other way in the same tile.
    /// The package cannot do this itself: its own linking tolerance is 1.5m and the opposite
    /// carriageway lane is 3m away (x = +1.5 against x = -1.5). Pavements are easier - the two
    /// directions share endpoints exactly - but they are never linked either, for the same
    /// neighbours-only reason.
    ///
    /// Written as "heal any dead end" rather than "handle boundary tiles" deliberately. It also
    /// covers tile-road-straight-crosswalk, whose tileShape is Cross so it probes all four
    /// directions and leaves some paths unmatched inside a straight run, and it will cover
    /// whatever tiles get added later without needing to know about them.
    ///
    /// Timing: DefaultExecutionOrder(1000) puts this Start() after every Tile.Start(), which is
    /// where the links are built (Tile has no execution order of its own and the project sets
    /// none). Spawners wait a frame before their first entity, so nothing is moving yet.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public sealed class DeadEndStitcher : MonoBehaviour
    {
        /// <summary>
        /// How far the returning lane's start may be from the dead end. Carriageway lanes sit
        /// 3m apart and pavement lanes share a point, so 6m covers both with room to spare
        /// while staying well inside one 30m tile - a neighbouring tile's paths are never this
        /// close, so this can only ever match within the tile.
        /// </summary>
        const float LinkRadius = 6f;

        /// <summary>
        /// How head-on the return has to be. -0.5 is a 120-degree turn or sharper, which
        /// admits the straight 180 while rejecting a lane that merely passes nearby.
        /// </summary>
        const float OppositeThreshold = -0.5f;

        [Tooltip("Logs how many U-turns were added. Expect roughly two per boundary tile - " +
                 "one carriageway lane and one pavement.")]
        [SerializeField] bool logSummary = true;

        void Start()
        {
            var stitched = Stitch();

            if (logSummary)
                Debug.Log($"[DeadEndStitcher] Added {stitched} U-turn link(s) at map-edge dead ends.", this);
        }

        /// <summary>Returns how many links were added.</summary>
        public int Stitch()
        {
            var stitched = 0;

            foreach (var tile in Tile.Tiles)
            {
                if (!tile)
                    continue;

                stitched += StitchLanes(tile.paths);
                stitched += StitchLanes(tile.sidewalkPaths);
            }

            return stitched;
        }

        /// <summary>
        /// Repairs dead ends within one tile's set of same-type lanes. Road and pavement lanes
        /// are passed separately so a car can never be handed a pavement to drive down.
        /// </summary>
        static int StitchLanes(List<Path> lanes)
        {
            if (lanes == null)
                return 0;

            var stitched = 0;

            foreach (var lane in lanes)
            {
                if (!lane || lane.nextPaths == null || lane.nextPaths.Count > 0)
                    continue;
                if (!MapEdgeGates.TryGetLaneExit(lane, out var end, out var heading))
                    continue;

                var best = default(Path);
                var bestDistance = LinkRadius;

                foreach (var candidate in lanes)
                {
                    if (candidate == lane || !candidate)
                        continue;
                    if (!MapEdgeGates.TryGetLaneEntry(candidate, out var start, out var entry))
                        continue;

                    var distance = Vector3.Distance(start, end);
                    if (distance > bestDistance)
                        continue;
                    if (Vector3.Dot(entry, heading) > OppositeThreshold)
                        continue;

                    best = candidate;
                    bestDistance = distance;
                }

                if (!best)
                    continue;

                lane.nextPaths.Add(best);
                stitched++;
            }

            return stitched;
        }

        // Where a lane starts and ends, and which way it is going at each - MapEdgeGates asks the
        // same two questions of the same lanes, so the pair lives there and is shared.
    }
}
