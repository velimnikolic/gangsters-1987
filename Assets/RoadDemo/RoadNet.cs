using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// The streets of a scene as a network: a handful of straight ones that cross each
    /// other, and the two things a driver wants of them - which street a place is on,
    /// and which corner to take next on the way there. Nothing here drives or reserves
    /// anything; it only answers "left at the next junction, then it is the second on
    /// your right", and the car does the rest on the street it is handed (CrewCar).
    ///
    /// The route is the shortest by METRES DRIVEN, not by junctions crossed: from where
    /// the car stands, down its street to a crossing, along that street to the next, and
    /// so on - which is what makes a car go the near way round a block rather than the
    /// far way round it. A demo's net is four streets; the same code answers for the
    /// city's grid until the lane graph of Docs/vehicle-movement-plan.md replaces it.
    /// </summary>
    public sealed class RoadNet
    {
        /// <summary>How far off a street's centre line a point still counts as being on
        /// it: the carriageway, its pavements, and a little of the frontage behind them -
        /// so a click on a shopfront routes to the street it fronts.</summary>
        public const float Reach = 22f;

        public readonly List<IRoadModel> Streets = new List<IRoadModel>();

        public void Add(IRoadModel street)
        {
            if (street != null && !Streets.Contains(street)) Streets.Add(street);
        }

        static Vector3 AlongOf(IRoadModel s) => s.DirToWorld(Vector3.right);

        /// <summary>The street a place is on - the one whose centre line runs nearest it,
        /// of those that reach that far along themselves. Null: nowhere near any of them.</summary>
        public IRoadModel StreetOf(Vector3 world, float reach = Reach)
        {
            IRoadModel best = null;
            float bestOff = float.MaxValue;
            foreach (var s in Streets)
            {
                if (!Offset(s, world, out float off, reach) || off >= bestOff) continue;
                bestOff = off;
                best = s;
            }
            return best;
        }

        /// <summary>How far off this street's centre line a place lies - false when the
        /// street does not run far enough along itself to reach it, or when it is a
        /// street away.</summary>
        public bool Offset(IRoadModel street, Vector3 world, out float off, float reach = Reach)
        {
            off = float.MaxValue;
            if (street == null) return false;
            var f = street.ToFrame(world);
            if (f.x < street.XMin - 2f || f.x > street.XMax + 2f) return false;
            off = Mathf.Abs(f.z - street.CentreZ);
            return off <= reach;
        }

        /// <summary>Where two streets cross, when they are square to each other and both
        /// reach the crossing.</summary>
        public bool Crossing(IRoadModel a, IRoadModel b, out Vector3 at)
        {
            at = Vector3.zero;
            if (a == null || b == null || ReferenceEquals(a, b)) return false;
            var alongA = AlongOf(a);
            if (Mathf.Abs(Vector3.Dot(alongA, AlongOf(b))) > 0.5f) return false;  // parallel: never

            // a point on each centre line; the crossing is the one point of a's line that
            // is also on b's, and since the two are square that is a plain projection
            var pa = a.ToWorld(0f, a.CentreZ, 0f);
            var pb = b.ToWorld(0f, b.CentreZ, 0f);
            at = pa + alongA * Vector3.Dot(pb - pa, alongA);

            float sa = a.ToFrame(at).x, sb = b.ToFrame(at).x;
            return sa >= a.XMin - 1f && sa <= a.XMax + 1f && sb >= b.XMin - 1f && sb <= b.XMax + 1f;
        }

        /// <summary>The next corner on the way from <paramref name="at"/> (on street
        /// <paramref name="from"/>) to <paramref name="target"/>: the street to take, the
        /// junction to take it at, and the place to make for once round it - the target
        /// itself, or the junction after this one. False when the target is on this
        /// street already, or no street leads to it.</summary>
        public bool NextTurn(IRoadModel from, Vector3 at, Vector3 target,
                             out IRoadModel next, out Vector3 junction, out Vector3 waypoint)
        {
            next = null;
            junction = Vector3.zero;
            waypoint = target;

            var goal = StreetOf(target);
            if (from == null || goal == null) return false;
            // the car's own street counts as reaching a place unless another is a good
            // deal nearer it - nobody turns a corner for two metres of pavement, and at
            // a junction every place is on two streets at once
            if (!ReferenceEquals(goal, from) && Offset(from, target, out float mine))
            {
                Offset(goal, target, out float theirs);
                if (mine <= theirs + 6f) goal = from;
            }
            if (ReferenceEquals(goal, from)) return false;
            int start = Streets.IndexOf(from), end = Streets.IndexOf(goal);
            if (start < 0 || end < 0) return false;

            // Dijkstra over the streets, the cost of a street being the metres driven
            // down it from where the car came onto it to where it leaves it
            int n = Streets.Count;
            var cost = new float[n];
            var entry = new Vector3[n];
            var prev = new int[n];
            var done = new bool[n];
            for (int i = 0; i < n; i++) { cost[i] = float.MaxValue; prev[i] = -1; }
            cost[start] = 0f;
            entry[start] = at;

            while (true)
            {
                int u = -1;
                float bestCost = float.MaxValue;
                for (int i = 0; i < n; i++)
                    if (!done[i] && cost[i] < bestCost) { bestCost = cost[i]; u = i; }
                if (u < 0) return false;      // nothing left reachable: no way through
                if (u == end) break;
                done[u] = true;
                for (int v = 0; v < n; v++)
                {
                    if (done[v] || !Crossing(Streets[u], Streets[v], out var cross)) continue;
                    float leg = Mathf.Abs(Streets[u].ToFrame(cross).x - Streets[u].ToFrame(entry[u]).x);
                    if (cost[u] + leg >= cost[v]) continue;
                    cost[v] = cost[u] + leg;
                    prev[v] = u;
                    entry[v] = cross;
                }
            }

            // back down the route to the first street off this one
            int hop = end;
            while (prev[hop] >= 0 && prev[hop] != start) hop = prev[hop];
            if (prev[hop] != start) return false;
            next = Streets[hop];
            junction = entry[hop];

            // and where to make for once round that corner: the junction after it when
            // the route turns again, else the target itself
            for (int i = end; i >= 0 && i != hop; i = prev[i])
                if (prev[i] == hop) { waypoint = entry[i]; break; }
            return true;
        }

        /// <summary>Where a turn from one street into another begins and ends: the lane
        /// come in on and the lane gone out on are two lines that cross inside the
        /// junction, and the corner is swung from <paramref name="mouth"/>, a radius back
        /// down the first, to <paramref name="exit"/>, a radius on down the second. A
        /// turn INTO the near lane is swung tight (<paramref name="rightRadius"/>); one
        /// that has to cross the far lane is given room (<paramref name="leftRadius"/>).
        /// The radii are what keep the swing off the corner's pavement: the wider the
        /// swing, the nearer its belly comes to the inside kerb.</summary>
        public static void CornerPoints(IRoadModel from, float dirIn, IRoadModel to, float dirOut,
                                        Vector3 junction, float y, float rightRadius, float leftRadius,
                                        out Vector3 mouth, out Vector3 exit)
        {
            var alongIn = from.DirToWorld(Vector3.right) * Mathf.Sign(dirIn);
            var alongOut = to.DirToWorld(Vector3.right) * Mathf.Sign(dirOut);
            var pa = from.ToWorld(from.ToFrame(junction).x, from.LaneZ(dirIn), y);
            var pb = to.ToWorld(to.ToFrame(junction).x, to.LaneZ(dirOut), y);
            var cross = pa + alongIn * Vector3.Dot(pb - pa, alongIn);   // the two lanes meet here
            float radius = Vector3.Cross(alongIn, alongOut).y > 0f ? rightRadius : leftRadius;
            mouth = cross - alongIn * radius;
            exit = cross + alongOut * radius;
        }
    }
}
