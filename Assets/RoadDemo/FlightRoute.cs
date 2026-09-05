using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>Choose a reachable turn out of the pursuer's sight. Distance alone
    /// sends a runner down the same open street until he reaches the city fence.</summary>
    public static class FlightRoute
    {
        static readonly float[] Angles = { 0f, 45f, -45f, 90f, -90f };
        static readonly List<(Vector3 point, float score)> Candidates = new();
        static readonly List<Vector3> Route = new();

        public static bool TryGoal(Vector3 start, Vector3 threat, out Vector3 goal)
        {
            goal = start;
            var away = start - threat;
            away.y = 0f;
            float initialGap = away.magnitude;
            away = initialGap > 0.1f ? away / initialGap : Vector3.forward;
            Candidates.Clear();
            foreach (float distance in new[] { 35f, 70f })
                foreach (float angle in Angles)
                {
                    var direction = Quaternion.Euler(0f, angle, 0f) * away;
                    var wanted = WalkObstacles.ClampToCity(start + direction * distance);
                    if (!WalkObstacles.TryClearStandingSpot(wanted, WalkObstacles.Radius,
                        start, out var point, 8f)) continue;
                    point.y = start.y;
                    var delta = point - start;
                    float travel = delta.magnitude;
                    if (travel < 12f || Vector3.Dot(delta, away) < -1f) continue;
                    float gain = Vector3.Distance(threat, point) - initialGap;
                    float score = gain - travel * 0.3f;
                    if (!WalkObstacles.Sees(threat, point)) score += 100f;
                    Candidates.Add((point, score));
                }
            Candidates.Sort((a, b) => b.score.CompareTo(a.score));
            foreach (var candidate in Candidates)
            {
                if (!WalkRoute.Plan(start, candidate.point, Route)) continue;
                // A destination beyond a wall is not an escape if its route first
                // turns back into the officer standing behind the crew.
                var previous = start;
                bool towardThreat = false;
                float travelled = 0f;
                foreach (var corner in Route)
                {
                    var leg = corner - previous;
                    float length = leg.magnitude;
                    float t = length > 0f ? Mathf.Clamp01(Vector3.Dot(threat - previous, leg) /
                        (length * length)) : 0f;
                    if (Vector3.Distance(previous + leg * t, threat) < Mathf.Min(initialGap - 1f, 15f))
                    { towardThreat = true; break; }
                    travelled += length;
                    previous = corner;
                }
                if (towardThreat || travelled > 160f) continue;
                goal = candidate.point;
                return true;
            }
            return false;
        }
    }
}
