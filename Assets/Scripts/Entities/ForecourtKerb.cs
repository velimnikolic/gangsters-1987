using UnityEngine;
using PolyPerfect.City;
using LivingCity.Generation;

namespace LivingCity.Entities
{
    /// <summary>
    /// Where a forecourt meets the lane graph. A building set back behind its bays has no
    /// address on the road network - the stalls are off it - so anything that drives in or out
    /// needs one road waypoint to aim at: returning cars route to it, docking curves start from
    /// it, undocking cars pull out onto it.
    ///
    /// Written for the police station and now shared with the bank, which wants exactly the
    /// same answer about a forecourt laid by exactly the same code (ParkingLayout.ForStreetBay
    /// via BlockBuilder.PlaceLandmark). The only per-building part is where to look FROM, so
    /// that is the argument.
    /// </summary>
    public static class ForecourtKerb
    {
        /// <summary>
        /// The road waypoint nearest <paramref name="focus"/>, and the direction of its lane
        /// segment there. Searched over Tile.Tiles rather than the CityGrid for the reason the
        /// spawners give: a city generated in the editor and saved into the scene has no grid
        /// object at runtime, but every Tile has registered itself by the time Start runs.
        /// </summary>
        public static bool TryFind(Vector3 focus, out Vector3 kerbPos, out Vector3 kerbDir)
        {
            kerbPos = Vector3.zero;
            kerbDir = Vector3.forward;
            var best = float.MaxValue;

            foreach (var tile in Tile.Tiles)
            {
                if (!tile || tile.paths == null)
                    continue;
                if ((tile.transform.position - focus).sqrMagnitude > CityGrid.CellSize * CityGrid.CellSize * 4f)
                    continue;

                foreach (var path in tile.paths)
                {
                    if (!path || path.pathType != PathType.Road || path.pathPositions == null)
                        continue;

                    for (var i = 1; i < path.pathPositions.Count; i++)
                    {
                        var node = path.pathPositions[i];
                        var previous = path.pathPositions[i - 1];
                        if (!node || !previous)
                            continue;

                        var distance = (node.position - focus).sqrMagnitude;
                        if (distance >= best)
                            continue;

                        var segment = node.position - previous.position;
                        if (segment.sqrMagnitude < 1e-4f)
                            continue;

                        best = distance;
                        kerbPos = node.position;
                        kerbDir = segment.normalized;
                    }
                }
            }

            return best < float.MaxValue;
        }

        /// <summary>
        /// The point out in the street a forecourt's kerb is searched around: straight out of
        /// the building, past the bays and the walkway PlaceLandmark set it back by.
        /// </summary>
        public static Vector3 FocusFor(StallHost host, Vector3 from) =>
            from + host.Facing * (ParkingLayout.StallDepth + BlockBuilder.LandmarkForecourtWalkway);
    }
}
