using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    // Preflight before sampling: failed/cyclic routes allocate no growing polyline.
    // Keep every road/connector endpoint; long routes use coarser interior samples.
    internal static class RoutePreviewBudget
    {
        internal const int MaxLegs = 1024, MaxPoints = 4096;

        internal static bool Fit(RoadEdge edge, float fromS, Carriageway goalRoad,
            int goalHeading, float goalS, Dictionary<RoadEdge, RoadEdge> route,
            Dictionary<RoadEdge, RoadEdge> shifts, int initialPoints, ref float spacing)
        {
            float distance = 0f;
            for (int leg = 0; leg < MaxLegs && edge?.Road != null; leg++)
            {
                var road = edge.Road;
                if (road == goalRoad && (edge.Heading == goalHeading || route == null))
                {
                    distance += Mathf.Abs(goalS - fromS);
                    // Up to two road sample spans and one connector per leg: each
                    // ceil needs one spare point. Reserve extra for the goal/entry.
                    int interiors = MaxPoints - initialPoints - 3 * (leg + 1) - 16;
                    if (interiors < 1) return false;
                    spacing = Mathf.Max(spacing, distance / interiors);
                    return true;
                }
                var routed = edge;
                if (shifts != null && shifts.TryGetValue(edge, out var shifted) &&
                    shifted != null && shifted.Road == road && shifted.Heading == edge.Heading)
                    routed = shifted;
                distance += Mathf.Abs(road.EndS(routed.Heading) - fromS);
                if (route == null || !route.TryGetValue(routed, out var next) || next == null) return false;
                var connector = routed.To?.ConnectorFor(routed, next);
                if (connector == null) return false;
                distance += connector.Length;
                edge = next;
                fromS = edge.S0;
            }
            return false;
        }
    }
}
