using System.Collections.Generic;
using UnityEngine;
using LivingCity.City;

namespace LivingCity.Entities
{
    /// <summary>
    /// Which lanes belong to a junction, and where along a car's route the next junction begins
    /// and ends. This is the data behind the "don't block the box" rule in CarBehavior: a car
    /// must not commit to a junction whose exit it cannot clear, because cars standing inside
    /// the box are what mutual-block rings are made of - and a ring, once formed, is the one
    /// arrangement the pairwise right-of-way rule cannot drain.
    ///
    /// There is no junction data structure to read - the pack's intersections are just tiles
    /// whose authored lanes happen to cross - so junction membership is derived from the tile a
    /// lane is parented under, and cached: the road network is built once, synchronously, before
    /// any car moves (see RoadNetworkBuilder), so the answer for a given lane never changes.
    /// </summary>
    public static class JunctionMap
    {
        /// <summary>
        /// At most this many consecutive junction lanes are chased when locating the exit. One
        /// junction is one tile and a route crosses it in one or two lanes; anything longer is a
        /// stitching accident, and chasing it would push the "exit" so far downstream that the
        /// exit check quietly turns into "is the whole street ahead empty".
        /// </summary>
        const int MaxChainedJunctionLanes = 2;

        static readonly Dictionary<Path, bool> LaneCache = new Dictionary<Path, bool>();
        static readonly Dictionary<Tile, bool> TileCache = new Dictionary<Tile, bool>();

        /// <summary>Is this lane inside a junction box? Cached after the first ask.</summary>
        public static bool IsJunctionLane(Path path)
        {
            if (!path)
                return false;

            if (LaneCache.TryGetValue(path, out var cached))
                return cached;

            var tile = path.GetComponentInParent<Tile>();
            var result = tile && IsJunctionTile(tile);
            LaneCache[path] = result;
            return result;
        }

        /// <summary>
        /// The shape alone cannot be trusted: the straight-with-crosswalk prefab declares
        /// tileShape = Cross so that its neighbour probing covers all four sides, but it is a
        /// plain straight and treating it as a box would put a phantom junction on every third
        /// street. What settles it is the road network itself - a real junction is a tile with
        /// at least three road neighbours, and a mislabelled straight has two.
        /// </summary>
        static bool IsJunctionTile(Tile tile)
        {
            if (TileCache.TryGetValue(tile, out var cached))
                return cached;

            var result = false;

            switch (tile.tileShape)
            {
                case Tile.TileShape.T:
                case Tile.TileShape.Cross:
                case Tile.TileShape.Exit:
                case Tile.TileShape.ExitOneWay:
                case Tile.TileShape.BothSideExit:
                case Tile.TileShape.BothSideExitOneWay:
                    var roadNeighbors = 0;
                    var neighbors = tile.NeighborTiles;
                    if (neighbors != null)
                    {
                        for (var i = 0; i < neighbors.Length; i++)
                        {
                            var n = neighbors[i];
                            if (n && (n.tileType == Tile.TileType.Road || n.tileType == Tile.TileType.RoadAndRail))
                                roadNeighbors++;
                        }
                    }
                    result = roadNeighbors >= 3;
                    break;
            }

            TileCache[tile] = result;
            return result;
        }

        /// <summary>
        /// Is there a junction on this car's route within <paramref name="maxDistance"/> metres,
        /// and if so, how far away does it start and where does its far side lie?
        ///
        /// Distance is walked along the actual checkpoints, not measured as the crow flies -
        /// the route bends, and a straight-line answer would let a car around a corner think the
        /// junction is further than the road it must actually cover.
        ///
        /// Returns false when the car is ALREADY on a junction lane. That is the commit rule: a
        /// car inside the box must clear it, whatever is happening beyond - holding it there is
        /// exactly the arrangement this whole mechanism exists to prevent.
        /// </summary>
        public static bool TryFindJunctionAhead(List<Path> trajectory, int pathIndex, int checkpoint,
                                                Vector3 frontPosition, float maxDistance,
                                                out float distanceToEntry,
                                                out Vector3 exitPosition, out Vector3 exitDirection)
        {
            distanceToEntry = 0f;
            exitPosition = default;
            exitDirection = default;

            if (trajectory == null || pathIndex < 0 || pathIndex >= trajectory.Count)
                return false;

            if (IsJunctionLane(trajectory[pathIndex]))
                return false;

            var distance = 0f;
            var cursor = frontPosition;

            if (!Accumulate(trajectory[pathIndex], checkpoint, maxDistance, ref distance, ref cursor))
                return false;

            for (var p = pathIndex + 1; p < trajectory.Count; p++)
            {
                var path = trajectory[p];
                if (!path)
                    return false;

                if (IsJunctionLane(path))
                {
                    distanceToEntry = distance;
                    return TryDescribeExit(trajectory, p, out exitPosition, out exitDirection);
                }

                if (!Accumulate(path, 0, maxDistance, ref distance, ref cursor))
                    return false;
            }

            return false;
        }

        /// <summary>
        /// Adds the length of <paramref name="path"/> from <paramref name="firstCheckpoint"/> to
        /// its end onto the running total. False once the total passes the cap - the caller has
        /// walked far enough that nothing found beyond matters.
        /// </summary>
        static bool Accumulate(Path path, int firstCheckpoint, float maxDistance,
                               ref float distance, ref Vector3 cursor)
        {
            var points = path.pathPositions;
            if (points == null)
                return true;

            for (var i = Mathf.Max(0, firstCheckpoint); i < points.Count; i++)
            {
                if (!points[i])
                    continue;

                distance += Vector3.Distance(cursor, points[i].position);
                cursor = points[i].position;

                if (distance > maxDistance)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// The far side of the junction that starts at <paramref name="entryIndex"/>: the last
        /// checkpoint of the last consecutive junction lane, and the heading of the route there.
        /// This is where the exit-clearance probe places its virtual car.
        /// </summary>
        static bool TryDescribeExit(List<Path> trajectory, int entryIndex,
                                    out Vector3 exitPosition, out Vector3 exitDirection)
        {
            exitPosition = default;
            exitDirection = default;

            var last = entryIndex;
            while (last + 1 < trajectory.Count
                   && last - entryIndex + 1 < MaxChainedJunctionLanes
                   && IsJunctionLane(trajectory[last + 1]))
            {
                last++;
            }

            var points = trajectory[last] ? trajectory[last].pathPositions : null;
            if (points == null)
                return false;

            Transform end = null, beforeEnd = null;
            for (var i = points.Count - 1; i >= 0; i--)
            {
                if (!points[i])
                    continue;

                if (!end)
                {
                    end = points[i];
                }
                else
                {
                    beforeEnd = points[i];
                    break;
                }
            }

            if (!end || !beforeEnd)
                return false;

            exitPosition = end.position;
            exitDirection = (end.position - beforeEnd.position).normalized;
            return true;
        }
    }
}
