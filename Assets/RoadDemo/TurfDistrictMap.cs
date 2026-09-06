using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>Prepared district surfaces shared by the full survey and minimap.
    /// Collection touches district adapters; drawing reads only copied value types.</summary>
    public sealed class TurfDistrictMap
    {
        readonly List<DistrictMapGeometry.Surface> _surfaces = new List<DistrictMapGeometry.Surface>();

        public void Collect(RoadDemoBuilder city)
        {
            var surfaces = new List<DistrictMapGeometry.Surface>();
            foreach (var district in city.DistrictPlans)
            {
                if (district.Kind != DistrictKind.Pad ||
                    (city.HasPrimaryStructure && district.Name == city.PrimaryCore?.Name)) continue;
                surfaces.Add(new DistrictMapGeometry.Surface(district.World, TurfInk.Concrete, -1000f));
            }
            foreach (var district in city.BuiltDistricts)
                if (district is IDistrictMapSource source)
                    surfaces.AddRange(source.MapGeometry.Surfaces);
            Collect(surfaces);
        }

        public void Collect(IEnumerable<DistrictMapGeometry.Surface> surfaces)
        {
            _surfaces.Clear();
            _surfaces.AddRange(surfaces);
            // Preserve composition order for coplanar surfaces. Raised paint stays on
            // top even when another part of the district was composed later.
            var ordered = new List<(DistrictMapGeometry.Surface surface, int index)>();
            for (int i = 0; i < _surfaces.Count; i++) ordered.Add((_surfaces[i], i));
            ordered.Sort((a, b) => {
                int height = a.surface.Height.CompareTo(b.surface.Height);
                return height != 0 ? height : a.index.CompareTo(b.index);
            });
            for (int i = 0; i < ordered.Count; i++) _surfaces[i] = ordered[i].surface;
        }

        public void Draw(TurfProjection plan, TurfPlate ground, bool[] water)
        {
            for (int i = 0; i < _surfaces.Count; i++) Draw(_surfaces[i], plan, ground, water);
        }

        public static void Draw(DistrictMapGeometry.Surface surface, TurfProjection plan,
                                TurfPlate ground, bool[] water)
        {
            Rect box = plan.ToPlan(surface.World);
            int x0 = Mathf.Max(0, Mathf.FloorToInt(box.xMin * TurfPlate.S));
            int x1 = Mathf.Min(TurfPlate.RW, Mathf.CeilToInt(box.xMax * TurfPlate.S));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(box.yMin * TurfPlate.S));
            int y1 = Mathf.Min(TurfPlate.RH, Mathf.CeilToInt(box.yMax * TurfPlate.S));
            if (x0 >= x1 || y0 >= y1) return;
            Vector2 a = plan.ToPlan(surface.A) * TurfPlate.S;
            Vector2 b = plan.ToPlan(surface.B) * TurfPlate.S;
            Vector2 c = plan.ToPlan(surface.C) * TurfPlate.S;
            if (surface.Triangle && Mathf.Abs(Cross(b - a, c - a)) < 0.00001f) return;
            for (int y = y0; y < y1; y++) for (int x = x0; x < x1; x++)
            {
                if (surface.Triangle && !Inside(new Vector2(x + 0.5f, y + 0.5f), a, b, c)) continue;
                ground.Dot(x, y, surface.Ink);
                // Concrete piers can cover sampled sea; ownership must see the same
                // dry surface as the player, without filling the basin around it.
                water[y * TurfPlate.RW + x] = false;
            }
        }

        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
        static bool Inside(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float u = Cross(b - a, p - a), v = Cross(c - b, p - b), w = Cross(a - c, p - c);
            return (u >= 0f && v >= 0f && w >= 0f) || (u <= 0f && v <= 0f && w <= 0f);
        }
    }
}
