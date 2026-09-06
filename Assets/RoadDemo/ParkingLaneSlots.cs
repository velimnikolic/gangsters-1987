using UnityEngine;

namespace RoadDemo
{
    /// <summary>Admission of a stationary civilian car to a street's parking strip.
    /// Dimensions are the full visible body, before the traffic footprint is scaled.</summary>
    public static class ParkingLaneSlots
    {
        public const float JunctionClearance = 7f;
        public const float Gap = 4f;
        public const float KerbClearance = 0.15f;
        const float LaneHalfWidth = 2.5f;

        public static bool TryPose(Carriageway road, float s, int side,
            float halfLength, float halfWidth, out Vector3 position, out Vector3 forward)
        {
            position = Vector3.zero;
            forward = Vector3.forward;
            if (road == null || road.Path != null || road.Elevated ||
                (road.Class != RoadClass.Street && road.Class != RoadClass.Boulevard) ||
                (side != -1 && side != 1) || halfLength <= 0f || halfWidth <= 0f ||
                !(side > 0 ? road.ParkingB : road.ParkingA)) return false;
            if (s - halfLength < JunctionClearance ||
                s + halfLength > road.Length - JunctionClearance) return false;

            var lane = road.LaneFor(side, side * road.HalfRoad);
            if (lane == null) return false;
            float inner = Mathf.Abs(lane.Offset) + LaneHalfWidth;
            // A narrow two-way road has parking permission by default in older
            // graphs, but no physical strip. Wide vans must fit outside the lane too.
            if (road.HalfRoad - inner < LaneHalfWidth - 0.01f) return false;
            // Ambient cars stay wholly on asphalt, including when neighbouring
            // streamed furniture appears later. Reject bodies wider than the strip.
            float d = side * (road.HalfRoad - halfWidth - KerbClearance);
            if (Mathf.Abs(d) - halfWidth < inner || !road.Drivable(d, halfWidth)) return false;
            if (road.Busy(null, s - halfLength - Gap, s + halfLength + Gap,
                    d - halfWidth - 0.3f, d + halfWidth + 0.3f)) return false;

            position = road.Pose(s, d);
            position.y = road.SurfaceOn(s);
            forward = road.DirAt(s) * side;
            return true;
        }
    }
}
