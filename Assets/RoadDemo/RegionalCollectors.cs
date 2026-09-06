using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>Round two-arm collector bends; intersections retain their junction owner.</summary>
    public static class RegionalCollectors
    {
        public static void Round(RegionalExpresswayPlan plan, LaneNet net)
        {
            var junctions = new List<RoadNode>(plan.Junctions);
            foreach (var node in junctions)
            {
                if (plan.Ramps.Exists(r => r.A == node || r.B == node)) continue;
                var arms = plan.Ground.FindAll(r => r.A == node || r.B == node);
                if (arms.Count != 2) continue;
                var centre = new Vector3(node.X, 0f, node.Z);
                var a = arms[0]; var b = arms[1];
                var farA = a.A == node ? a.To : a.From;
                var farB = b.A == node ? b.To : b.From;
                var da = (farA - centre).normalized; var db = (farB - centre).normalized;
                if (Mathf.Abs(Vector3.Dot(da, db)) > 0.01f) continue;
                float radius = Mathf.Min(50f, Room(plan, a, node, centre, farA), Room(plan, b, node, centre, farB));
                if (radius < StreetKit.OuterHalf + 1f) continue;
                var p = centre + da * radius; var q = centre + db * radius;
                var na = net.AddNode(p.x, p.z, 0.1f, 0.1f, 0f); na.Seam = true;
                var nb = net.AddNode(q.x, q.z, 0.1f, 0.1f, 0f); nb.Seam = true;
                Trim(a, node, na, p); Trim(b, node, nb, q);
                var curve = RoadLine.Bezier(p, -da, q, db, 0.3905243f, 0.3905243f);
                plan.Ground.Add(new RegionalExpresswayPlan.GroundRoad { A = na, B = nb, From = p, To = q,
                    Path = RoadLine.Through(curve.Pts, -da, db) });
                plan.Junctions.Remove(node); net.Nodes.Remove(node);
            }
        }

        static float Room(RegionalExpresswayPlan plan, RegionalExpresswayPlan.GroundRoad road,
            RoadNode node, Vector3 centre, Vector3 far)
        {
            var other = road.A == node ? road.B : road.A;
            bool sharesBend = !other.Seam && plan.Junctions.Contains(other) &&
                plan.Ground.FindAll(r => r.A == other || r.B == other).Count == 2 &&
                !plan.Ramps.Exists(r => r.A == other || r.B == other);
            float length = Vector3.Distance(centre, far);
            return sharesBend ? length * 0.45f : length - 8f;
        }

        static void Trim(RegionalExpresswayPlan.GroundRoad road, RoadNode old, RoadNode seam, Vector3 point)
        {
            if (road.A == old) { road.A = seam; road.From = point; }
            else { road.B = seam; road.To = point; }
        }
    }
}
