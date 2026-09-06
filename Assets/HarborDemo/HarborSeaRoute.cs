using System.Collections.Generic;
using UnityEngine;

namespace HarborDemo
{
    /// <summary>Open-sea entrances through the harbor mouth. Concentric bends preserve
    /// the separation of the coastal lanes, including traffic sailing the other way.</summary>
    public sealed class HarborSeaRoute
    {
        public readonly float Run, Depth, BendDepth;
        public readonly Rect Water;
        const float QuarterCircle = 0.55228475f;

        public HarborSeaRoute(float run, float depth, int berths)
        {
            Run = run;
            // Long hulls sweep outside a curve's centreline. A 300 m inner radius
            // keeps even their rectangular envelopes inside the 18 m lane spacing.
            BendDepth = -HarborShipping.PassingLaneZ(berths, false) + 300f;
            Depth = Mathf.Max(depth, BendDepth + 100f);
            float radius = Radius(-HarborShipping.LaneOffset);
            // Hull length and the terrain's 20 m triangles need space beyond the path.
            const float Margin = 64f;
            Water = Rect.MinMaxRect(-Run - radius - Margin, -Depth - Margin,
                Run + radius + Margin, -1f);
        }

        float Radius(float laneZ) => BendDepth + laneZ;
        static Vector3 At(float x, float z, bool eastbound) =>
            new Vector3(eastbound ? x : -x, HarborDistrict.WaterY, z);

        public Vector3 Entry(float laneZ, bool eastbound = true) => At(-Run - Radius(laneZ), -Depth, eastbound);

        public IEnumerable<HarborShip.Leg> Inlet(float laneZ, float speed, bool eastbound = true)
        {
            float radius = Radius(laneZ), tangent = QuarterCircle * radius;
            var a = Entry(laneZ, eastbound);
            var b = At(-Run - radius, -BendDepth, eastbound);
            var c = At(-Run, laneZ, eastbound);
            yield return HarborShip.Leg.Straight(a, b, speed, speed);
            yield return HarborShip.Leg.Curve(b, At(-Run - radius, -BendDepth + tangent, eastbound),
                At(-Run - tangent, laneZ, eastbound), c, speed, speed);
        }

        public IEnumerable<HarborShip.Leg> Outlet(float laneZ, float speed, bool eastbound = true)
        {
            float radius = Radius(laneZ), tangent = QuarterCircle * radius;
            var a = At(Run, laneZ, eastbound);
            var b = At(Run + radius, -BendDepth, eastbound);
            var c = At(Run + radius, -Depth, eastbound);
            yield return HarborShip.Leg.Curve(a, At(Run + tangent, laneZ, eastbound),
                At(Run + radius, -BendDepth + tangent, eastbound), b, speed, speed);
            yield return HarborShip.Leg.Straight(b, c, speed, speed);
        }

        public IEnumerable<HarborShip.Leg> Crossing(float laneZ, float speed, bool eastbound = true)
        {
            foreach (var leg in Inlet(laneZ, speed, eastbound)) yield return leg;
            yield return HarborShip.Leg.Straight(At(-Run, laneZ, eastbound), At(Run, laneZ, eastbound), speed, speed);
            foreach (var leg in Outlet(laneZ, speed, eastbound)) yield return leg;
        }

        public float CrossingLength(float laneZ)
        {
            float length = 0f;
            foreach (var leg in Crossing(laneZ, 1f)) length += leg.Length;
            return length;
        }
    }
}
