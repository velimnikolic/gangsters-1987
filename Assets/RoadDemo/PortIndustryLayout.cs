using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>Stands the regional works behind the port, on the port's own road.
    /// All positions are planned before terrain, access roads or district views exist.</summary>
    public static class PortIndustryLayout
    {
        const float PlotGap = 30f;
        /// <summary>The zone's seaward blocks stand on the far edge of the port's back
        /// street pavement: the street's own half width, its pavement, and a metre.</summary>
        public const float Frontage = StreetKit.OuterHalf + 1f;
        /// <summary>How far the zone's kerb keeps from the centreline of the port's
        /// other gate road, which still runs past it to the expressway.</summary>
        const float RoadClear = 20f;
        // Leave room for the collector even where a river mouth pushes the
        // expressway's rounded corner beyond the original city rectangle.
        const float InlandStrip = 700f;

        /// <summary>
        /// One zone, two tiers deep either side of an artery that IS one of the port's
        /// gate roads: the artery's landward mouth takes the expressway approach, its
        /// seaward end and every street beside it run out onto the port's back street,
        /// and the gate's own approach is not laid (<see cref="CoreRegion.Connection.Via"/>).
        /// Of the port's gates the zone takes the one whose line keeps it inside the
        /// port's frontage and clear of the other gate's road.
        /// </summary>
        public static void Arrange(List<CoreRegion.Quarter> quarters, List<CoreRegion.Connection> connections)
        {
            var port = quarters.Find(q => q.District is HarborDemo.HarborDistrict);
            if (port == null) return;
            port.Slot.strip = InlandStrip + PlotGap;
            var works = quarters.Find(q => q.District is IndustrialDistrict);
            var gate0 = connections.Find(c => c.Quarter == port && c.Portal == 0);
            var access = connections.Find(c => c.Quarter == works);
            if (works == null || gate0 == null || access == null) return;

            var shore = DistrictFrame.At(0f, 0f, RasterGateways.Yaw(port.Slot.edge));
            float sign = CoreRegion.Across(port.Slot.edge, shore.ToWorldDir(Vector3.right));
            // the zone in the port's contract frame: x along the shore from the artery
            // line, positive toward the second gate; height inland
            var box = RelativeBounds(works);
            port.Slot.strip = InlandStrip + box.height + PlotGap;
            var frontage = port.District.LocalBounds;

            CoreRegion.Connection chosen = null;
            float best = float.PositiveInfinity;
            bool blocked = true;
            foreach (var gate in connections)
            {
                if (gate.Quarter != port) continue;
                float at = (gate.Across - gate0.Across) * sign;
                float lo = at + box.xMin, hi = at + box.xMax;
                bool clear = true;
                foreach (var c in connections)
                {
                    if (c == gate || c.Edge != port.Slot.edge || c.Quarter == null || c.Quarter == works) continue;
                    float x = (c.Across - gate0.Across) * sign;
                    if (x > lo - RoadClear && x < hi + RoadClear) clear = false;
                }
                float outside = Mathf.Max(0f, frontage.xMin - lo) + Mathf.Max(0f, hi - frontage.xMax);
                float score = (clear ? 0f : 1e6f) + outside;
                if (score >= best) continue;
                best = score; chosen = gate; blocked = !clear;
            }
            if (blocked)
                Debug.LogWarning($"[Region] The port works zone ({box.width:0} m along the shore) meets another gate road on every port gate line.");
            chosen.Via = works;
            access.Across = chosen.Across;
            // The zone's seaward blocks front the port's back street across its pavement.
            works.Slot.strip = port.Slot.strip - Frontage + box.yMin;
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
        /// terrain levels.</summary>
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
            // Join the zone to the port face, including the gate corridor.
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
