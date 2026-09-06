using System.Collections.Generic;
using RoadDemo;
using UnityEngine;

namespace HarborDemo
{
    /// <summary>The port's public back street, with paired lanes and usable loading kerbs.</summary>
    public static class HarborStreet
    {
        public static LaneNet Build(DistrictFrame frame, IReadOnlyList<float> stops, float z)
        {
            var net = new LaneNet();
            const float half = StreetKit.StreetHalf;
            foreach (float x in stops)
            {
                var p = frame.ToWorld(new Vector3(x, 0f, z));
                net.AddNode(p.x, p.z, half, half);
            }
            for (int i = 0; i + 1 < stops.Count; i++)
            {
                if (stops[i + 1] - stops[i] < half * 2f + 5f) continue;
                var a = frame.ToWorld(new Vector3(stops[i] + half, 0f, z));
                var b = frame.ToWorld(new Vector3(stops[i + 1] - half, 0f, z));
                net.AddRoad(a, b, half, new[] { 2.5f }, 11f, net.Nodes[i], net.Nodes[i + 1],
                    Mathf.Abs(b.z - a.z) > Mathf.Abs(b.x - a.x));
            }
            return net;
        }
    }
}
