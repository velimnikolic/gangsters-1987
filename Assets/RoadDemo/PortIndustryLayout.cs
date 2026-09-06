using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>Packs the regional works along the landward edge of the port.
    /// All positions are planned before terrain, access roads or district views exist.</summary>
    public static class PortIndustryLayout
    {
        const float PlotGap = 30f;
        const float AccessHalf = 20f;
        // Leave room for the collector even where a river mouth pushes the
        // expressway's rounded corner beyond the original city rectangle.
        const float InlandStrip = 700f;

        public static void Arrange(List<CoreRegion.Quarter> quarters, List<CoreRegion.Connection> connections)
        {
            var port = quarters.Find(q => q.District is HarborDemo.HarborDistrict);
            if (port == null) return;
            var gate = connections.Find(c => c.Quarter == port && c.Portal == 0);
            var shore = DistrictFrame.At(0f, 0f, RasterGateways.Yaw(port.Slot.edge));
            float sign = CoreRegion.Across(port.Slot.edge, shore.ToWorldDir(Vector3.right));
            var occupied = new List<Vector2>();
            foreach (var c in connections)
            {
                if (c.Edge != port.Slot.edge || c.Quarter?.District is IndustrialDistrict) continue;
                float x = (c.Across - gate.Across) * sign;
                occupied.Add(new Vector2(x - AccessHalf, x + AccessHalf));
            }

            var works = quarters.FindAll(q => q.District is IndustrialDistrict);
            float depth = 0f;
            foreach (var q in works) depth = Mathf.Max(depth, RelativeBounds(q).height);
            port.Slot.strip = InlandStrip + depth + PlotGap;
            var frontage = port.District.LocalBounds;
            foreach (var q in works)
            {
                var box = RelativeBounds(q);
                float best = 0f, score = float.PositiveInfinity;
                Consider(frontage.center.x - box.width * 0.5f);
                foreach (var span in occupied)
                {
                    Consider(span.x - PlotGap - box.width);
                    Consider(span.y + PlotGap);
                }
                occupied.Add(new Vector2(best, best + box.width));
                var access = connections.Find(c => c.Quarter == q);
                access.Across = gate.Across + (best - box.xMin) * sign;
                // Every estate backs onto the same landward port edge, even when their
                // different depths put the city-facing gateways at different distances.
                q.Slot.strip = port.Slot.strip - PlotGap + box.yMin;

                void Consider(float x)
                {
                    foreach (var span in occupied)
                        if (x < span.y + PlotGap - 0.01f && x + box.width > span.x - PlotGap + 0.01f)
                            return;
                    float outside = Mathf.Max(0f, frontage.xMin - x) +
                                    Mathf.Max(0f, x + box.width - frontage.xMax);
                    float candidate = outside * 10f + Mathf.Abs(x + box.width * 0.5f - frontage.center.x);
                    if (candidate >= score) return;
                    best = x; score = candidate;
                }
            }
        }

        /// <summary>The envelope of the industrial area, for bounds queries.</summary>
        public static Rect GroundBounds(List<CoreRegion.Quarter> quarters)
        {
            var port = quarters.Find(q => q.District is HarborDemo.HarborDistrict);
            var works = quarters.FindAll(q => q.District is IndustrialDistrict);
            if (port == null || works.Count == 0) return Rect.zero;
            var box = works[0].World;
            foreach (var q in works)
            {
                box.xMin = Mathf.Min(box.xMin, q.World.xMin); box.xMax = Mathf.Max(box.xMax, q.World.xMax);
                box.yMin = Mathf.Min(box.yMin, q.World.yMin); box.yMax = Mathf.Max(box.yMax, q.World.yMax);
            }
            var edge = port.District.Frame.origin;
            switch (port.Slot.edge)
            {
                case CityEdge.West: box.xMin = edge.x; break;
                case CityEdge.East: box.xMax = edge.x; break;
                case CityEdge.South: box.yMin = edge.z; break;
                case CityEdge.North: box.yMax = edge.z; break;
            }
            return box;
        }

        /// <summary>Keep the plot setbacks clear while retaining each district's
        /// terrain levels. Unequal estate depths retain their individual outlines.</summary>
        public static void ReserveGround(List<CoreRegion.Quarter> quarters, DistrictReservations reservations)
        {
            var port = quarters.Find(q => q.District is HarborDemo.HarborDistrict);
            if (port == null) return;
            var edge = port.District.Frame.origin;
            foreach (var q in quarters)
            {
                if (!(q.District is IndustrialDistrict)) continue;
                var plot = q.World;
                float margin = PlotGap * 0.5f;
                plot = Rect.MinMaxRect(plot.xMin - margin, plot.yMin - margin,
                    plot.xMax + margin, plot.yMax + margin);
                switch (port.Slot.edge)
                {
                    case CityEdge.West: plot.xMin = edge.x; break;
                    case CityEdge.East: plot.xMax = edge.x; break;
                    case CityEdge.South: plot.yMin = edge.z; break;
                    case CityEdge.North: plot.yMax = edge.z; break;
                }
                reservations.NoFlora(plot);
            }
            // Join the whole row along the port face, including the gate corridors.
            var frontage = GroundBounds(quarters);
            if (frontage.width <= 0f || frontage.height <= 0f) return;
            switch (port.Slot.edge)
            {
                case CityEdge.West: frontage.xMax = edge.x + PlotGap; break;
                case CityEdge.East: frontage.xMin = edge.x - PlotGap; break;
                case CityEdge.South: frontage.yMax = edge.z + PlotGap; break;
                case CityEdge.North: frontage.yMin = edge.z - PlotGap; break;
            }
            reservations.NoFlora(frontage);
        }

        static Rect RelativeBounds(CoreRegion.Quarter q)
        {
            var box = q.District.LocalBounds;
            box.x -= q.IndustryOrigin.x;
            box.y -= q.IndustryOrigin.z;
            return DistrictFrame.At(0f, 0f, 90).ToWorldRect(box);
        }
    }
}
