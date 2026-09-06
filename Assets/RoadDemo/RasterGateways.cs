using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>Real junction mouths at the outside of a raster, never a line through a block.</summary>
    public static class RasterGateways
    {
        public readonly struct Gateway
        {
            public readonly int Junction;
            public readonly CityEdge Edge;
            public readonly Vector3 Face;
            public Gateway(int junction, CityEdge edge, Vector3 face)
            { Junction = junction; Edge = edge; Face = face; }
        }

        public static List<Gateway> Find(CoreRoads.Raster raster)
        {
            var result = new List<Gateway>();
            for (int n = 0; n < raster.Junctions.Count; n++)
            {
                var box = raster.Junctions[n];
                foreach (var edge in new[] { CityEdge.South, CityEdge.West, CityEdge.North, CityEdge.East })
                {
                    bool ns = edge == CityEdge.South || edge == CityEdge.North;
                    if ((ns ? box.width : box.height) < StreetKit.StreetHalf * 2f) continue;
                    float x = edge == CityEdge.West ? box.xMin : edge == CityEdge.East ? box.xMax : box.center.x;
                    float z = edge == CityEdge.South ? box.yMin : edge == CityEdge.North ? box.yMax : box.center.y;
                    float boundary = edge == CityEdge.South ? raster.Z0 : edge == CityEdge.North ? raster.Z(raster.NZ)
                                   : edge == CityEdge.West ? raster.X0 : raster.X(raster.NX);
                    float distance = Mathf.Abs(boundary - (ns ? z : x));
                    if (distance > CoreRoads.Cell * 2f) continue;
                    var direction = Outward(edge);
                    var right = new Vector3(direction.z, 0f, -direction.x);
                    bool clear = true;
                    for (float along = 1f; along < distance && clear; along += 2.5f)
                        for (float across = -7f; across <= 7f; across += 3.5f)
                        {
                            var at = new Vector3(x, 0f, z) + direction * along + right * across;
                            var kind = raster.At(Mathf.FloorToInt((at.x - raster.X0) / CoreRoads.Cell),
                                                 Mathf.FloorToInt((at.z - raster.Z0) / CoreRoads.Cell));
                            if (kind != CoreRoads.Kind.Outside && kind != CoreRoads.Kind.Bare &&
                                kind != CoreRoads.Kind.Spare) { clear = false; break; }
                        }
                    if (clear) result.Add(new Gateway(n, edge, new Vector3(x, 0f, z)));
                }
            }
            return result;
        }

        public static List<Gateway> Select(CoreRoads.Raster raster, int seed)
        {
            var all = Find(raster);
            var chosen = new List<Gateway>();
            var dice = new System.Random(seed ^ 0x47415445);
            foreach (var edge in new[] { CityEdge.South, CityEdge.West, CityEdge.North, CityEdge.East })
            {
                var options = all.FindAll(g => g.Edge == edge);
                if (options.Count > 0) chosen.Add(options[dice.Next(options.Count)]);
            }
            return chosen;
        }

        public static bool InMouth(IReadOnlyList<Gateway> gateways, Vector3 local)
        {
            if (gateways == null) return false;
            foreach (var gate in gateways)
            {
                var offset = local - gate.Face;
                var direction = Outward(gate.Edge);
                float along = Vector3.Dot(offset, direction);
                float across = Vector3.Dot(offset, new Vector3(direction.z, 0f, -direction.x));
                if (along >= 0f && along <= 20f && Mathf.Abs(across) < StreetKit.StreetHalf) return true;
            }
            return false;
        }

        public static Vector3 Outward(CityEdge edge) => edge == CityEdge.South ? Vector3.back
            : edge == CityEdge.North ? Vector3.forward : edge == CityEdge.West ? Vector3.left : Vector3.right;

        public static int Yaw(CityEdge edge) => edge == CityEdge.South ? 0 : edge == CityEdge.West ? 90
            : edge == CityEdge.North ? 180 : 270;

        public static Vector3 Face(RoadNode node, Vector3 outward) => new Vector3(
            outward.x < -0.5f ? node.XMin : outward.x > 0.5f ? node.XMax : node.X,
            0f, outward.z < -0.5f ? node.ZMin : outward.z > 0.5f ? node.ZMax : node.Z);
    }
}
