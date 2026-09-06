using AirportDemo;
using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>Scenic approach valley below the airport's existing flying circuit.</summary>
    public sealed class IslandAirfield
    {
        readonly DistrictFrame _frame;
        struct Corridor { public Vector2 A, B; public float HA, HB, Half; }
        readonly List<Corridor> _corridors = new List<Corridor>();

        public IslandAirfield(AirportDistrict airport)
        {
            _frame = new DistrictFrame
            {
                yaw = airport.Frame.yaw,
                origin = airport.Frame.ToWorld(new Vector3(
                    -AirportSpec.ApproachX, 0f, -AirportDistrict.BoundaryZ))
            };
            float half = airport.RunwayHalf;
            foreach (float side in new[] { -1f, 1f })
            {
                // Final and initial climb share the extended centreline.
                Add(new Vector2(side * (half + 70f), 0f), 0.2f,
                    new Vector2(side * (half + 305f), 0f), 0.2f, 125f);
                Add(new Vector2(side * (half + 305f), 0f), 0.2f,
                    new Vector2(side * (half + 630f), 0f), 36f, 125f);
                Add(new Vector2(side * (half + 630f), 0f), 36f,
                    new Vector2(side * (half + AirportSpec.FinalLength), 0f), 91f, 125f);
                // Protect either wind direction's downwind and base legs. These
                // high corridors trim peaks only; they do not flatten the landscape.
                float pattern = -side * AirportSpec.PatternWidth;
                Add(new Vector2(-side * (half + 600f), pattern), 190f,
                    new Vector2(side * (half + 500f), pattern), 190f, 145f);
                Add(new Vector2(side * (half + 500f), pattern), 190f,
                    new Vector2(side * (half + AirportSpec.FinalLength + 150f), pattern * 0.45f), 130f, 145f);
                Add(new Vector2(side * (half + AirportSpec.FinalLength + 150f), pattern * 0.45f), 130f,
                    new Vector2(side * (half + AirportSpec.FinalLength), 0f), 91f, 125f);
            }
        }

        void Add(Vector2 a, float ha, Vector2 b, float hb, float half)
            => _corridors.Add(new Corridor { A = a, B = b, HA = ha, HB = hb, Half = half });

        public float Shape(float x, float z, float height)
        {
            var local = _frame.ToLocal(new Vector3(x, 0f, z));
            var point = new Vector2(local.x, local.z);
            foreach (var corridor in _corridors)
            {
                var ab = corridor.B - corridor.A;
                float t = Mathf.Clamp01(Vector2.Dot(point - corridor.A, ab) /
                    Mathf.Max(0.001f, ab.sqrMagnitude));
                float distance = Vector2.Distance(point, corridor.A + ab * t);
                if (distance >= corridor.Half + 180f) continue;
                float weight = 1f - Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(corridor.Half, corridor.Half + 180f, distance));
                height = Mathf.Min(height, Mathf.Lerp(height,
                    Mathf.Lerp(corridor.HA, corridor.HB, t), weight));
            }
            return height;
        }
    }
}
