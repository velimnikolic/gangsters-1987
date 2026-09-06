using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>Immutable swept road footprints sampled from the driving network.</summary>
    public sealed class TurfRoadGeometry
    {
        readonly List<Vector2[]> _ribbons = new List<Vector2[]>();
        public static bool Swept(Carriageway road) => road.Path != null ||
            Mathf.Min(Mathf.Abs(road.B.x - road.A.x), Mathf.Abs(road.B.z - road.A.z)) > 0.1f;

        public void Collect(LaneNet net)
        {
            _ribbons.Clear();
            if (net == null) return;
            foreach (var road in net.Roads)
            {
                if (!Swept(road)) continue;
                int count = Mathf.Max(1, Mathf.CeilToInt(road.Length / 4f));
                var points = new Vector2[(count + 1) * 2];
                for (int i = 0; i <= count; i++)
                {
                    float s = road.Length * i / count;
                    var a = road.Pose(s, road.EdgeLo); var b = road.Pose(s, road.EdgeHi);
                    points[i * 2] = new Vector2(a.x, a.z); points[i * 2 + 1] = new Vector2(b.x, b.z);
                }
                _ribbons.Add(points);
            }
        }

        public void Ink(TurfProjection plan, byte[] mask, byte[] count, byte[] major)
        {
            foreach (var ribbon in _ribbons)
                for (int i = 2; i < ribbon.Length; i += 2)
                {
                    var a = plan.ToPlan(ribbon[i - 2]) * TurfPlate.S;
                    var b = plan.ToPlan(ribbon[i - 1]) * TurfPlate.S;
                    var c = plan.ToPlan(ribbon[i + 1]) * TurfPlate.S;
                    var d = plan.ToPlan(ribbon[i]) * TurfPlate.S;
                    Readable(ref a, ref b); Readable(ref d, ref c);
                    int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, b.x, c.x, d.x)));
                    int x1 = Mathf.Min(TurfPlate.RW - 1, Mathf.CeilToInt(Mathf.Max(a.x, b.x, c.x, d.x)));
                    int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, b.y, c.y, d.y)));
                    int y1 = Mathf.Min(TurfPlate.RH - 1, Mathf.CeilToInt(Mathf.Max(a.y, b.y, c.y, d.y)));
                    for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
                    {
                        var p = new Vector2(x + 0.5f, y + 0.5f);
                        if (!Triangle(p, a, b, c) && !Triangle(p, a, c, d)) continue;
                        int at = y * TurfPlate.RW + x;
                        mask[at] = 1; count[at] = 1; major[at] = 1;
                    }
                }
        }

        static void Readable(ref Vector2 a, ref Vector2 b)
        {
            var centre = (a + b) * 0.5f; var half = (b - a) * 0.5f;
            if (half.sqrMagnitude >= 0.49f || half.sqrMagnitude < 0.000001f) return;
            half = half.normalized * 0.7f; a = centre - half; b = centre + half;
        }

        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
        static bool Triangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float u = Cross(b - a, p - a), v = Cross(c - b, p - b), w = Cross(a - c, p - c);
            return (u >= 0f && v >= 0f && w >= 0f) || (u <= 0f && v <= 0f && w <= 0f);
        }
    }
}
