using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>Graph integration for district-owned roads. Keeps the actual carriageways
    /// and their occupants when a raster district joins a larger network.</summary>
    public static class RegionalRoads
    {
        public static void Join(LaneNet net, IReadOnlyList<RoadEdge> edges)
        {
            for (int i = 0; i < edges.Count; i++)
            {
                var edge = edges[i];
                if (edge == null) continue;
                if (edge.Road == null) net.Adopt(edge);
                else
                {
                    if (!net.Roads.Contains(edge.Road))
                    {
                        edge.Road.Index = net.Roads.Count;
                        edge.Road.Net = net;
                        net.Roads.Add(edge.Road);
                    }
                    if (!net.Edges.Contains(edge)) net.Edges.Add(edge);
                    if (edge.From != null && !net.Nodes.Contains(edge.From)) net.Nodes.Add(edge.From);
                    if (edge.To != null && !net.Nodes.Contains(edge.To)) net.Nodes.Add(edge.To);
                }
            }
        }

        public static void Link(LaneNet net, RoadNode a, RoadNode b, Vector3 from, Vector3 to, float speed)
        {
            if (a == null || b == null || (to - from).sqrMagnitude < 1f) return;
            var road = net.AddRoad(from, to, StreetKit.StreetHalf, new[] { 2.5f }, speed, a, b,
                Mathf.Abs(to.z - from.z) > 0.5f);
            road.ParkingA = road.ParkingB = false;
        }
    }
}
