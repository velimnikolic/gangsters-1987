using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>A short walking detour around parked vehicles. Moving traffic still
    /// belongs to local steering; a parked body needs corners that can lead away
    /// from the destination before turning back toward it.</summary>
    internal static class ParkedCarWalkRoute
    {
        const int MaxCars = 12;
        const float CornerAir = 0.15f;
        static readonly List<Vector3> Points = new List<Vector3>();
        static readonly List<SidewalkPlan.Box> Bodies = new List<SidewalkPlan.Box>();
        static readonly SidewalkPlan Plan = new SidewalkPlan();
        static readonly float[] Costs = new float[2 + MaxCars * 4];
        static readonly int[] Previous = new int[2 + MaxCars * 4];
        static readonly bool[] Closed = new bool[2 + MaxCars * 4];

        public static bool TryPlan(Vector3 from, Vector3 to, List<Vector3> into)
        {
            into.Clear();
            var middle = (from + to) * 0.5f;
            float reach = Vector3.Distance(from, to) * 0.5f + 12f;
            Bodies.Clear();
            foreach (var car in RoadCar.All)
            {
                if (car.Gone || car.Tf == null || !car.Parked ||
                    Vector3.Distance(car.RoadPosition, middle) > reach) continue;
                if (Bodies.Count == MaxCars) return false;
                var forward = car.RoadForward;
                var box = SidewalkPlan.Make(new Vector2(car.RoadPosition.x, car.RoadPosition.z),
                    Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg,
                    new Vector2(car.HalfWidth, car.HalfLength), true);
                Bodies.Add(box);
            }
            return PlanAround(from, to, Bodies, into);
        }

        internal static bool PlanAround(Vector3 from, Vector3 to,
            IReadOnlyList<SidewalkPlan.Box> bodies, List<Vector3> into)
        {
            into.Clear();
            while (Plan.Count > 0) Plan.Pop();
            if (bodies.Count == 0 || bodies.Count > MaxCars) return false;
            foreach (var body in bodies) Plan.Take(body);
            var a = new Vector2(from.x, from.z);
            var b = new Vector2(to.x, to.z);
            float radius = WalkObstacles.Radius;
            if (!Plan.Obstructs(a, b, radius) || Plan.Occupied(a, radius) ||
                Plan.Occupied(b, radius)) return false;
            Points.Clear();
            Points.Add(from);
            Points.Add(to);
            foreach (var body in bodies)
            {
                var half = body.H + Vector2.one * (radius + CornerAir);
                for (int x = -1; x <= 1; x += 2)
                for (int z = -1; z <= 1; z += 2)
                {
                    var corner = body.C + body.Ax * (x * half.x) + body.Az * (z * half.y);
                    var point = new Vector3(corner.x, from.y, corner.y);
                    if (WalkRoute.ChordClear(point, point) && !Plan.Occupied(corner, radius))
                        Points.Add(point);
                }
            }
            for (int i = 0; i < Points.Count; i++)
            {
                Costs[i] = i == 0 ? 0f : float.PositiveInfinity;
                Previous[i] = -1;
                Closed[i] = false;
            }
            for (int count = 0; count < Points.Count; count++)
            {
                int current = -1;
                for (int i = 0; i < Points.Count; i++)
                    if (!Closed[i] && (current < 0 || Costs[i] < Costs[current])) current = i;
                if (current < 0 || float.IsPositiveInfinity(Costs[current])) return false;
                if (current == 1)
                {
                    for (int at = 1; at != 0; at = Previous[at]) into.Add(Points[at]);
                    into.Reverse();
                    return true;
                }
                Closed[current] = true;
                for (int next = 0; next < Points.Count; next++)
                {
                    if (Closed[next]) continue;
                    var start = Points[current];
                    var end = Points[next];
                    float cost = Costs[current] + Vector3.Distance(start, end);
                    if (cost >= Costs[next] || !WalkRoute.ChordClear(start, end) ||
                        Plan.Obstructs(new Vector2(start.x, start.z),
                            new Vector2(end.x, end.z), radius)) continue;
                    Costs[next] = cost;
                    Previous[next] = current;
                }
            }
            return false;
        }
    }
}
