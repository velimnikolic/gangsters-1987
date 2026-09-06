using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>Natural mouths and a sheltered bay around the existing navigable reservations.</summary>
    public sealed class IslandWaters
    {
        readonly List<Rect> _shipping = new List<Rect>();
        readonly List<Rect> _harbors = new List<Rect>();
        readonly Rect _river;
        readonly float _phase, _outward;

        public IslandWaters(Rect city, DistrictReservations reservations, CityEdge harborSide, float phase, Rect? developedRegion = null)
        {
            _phase = phase; _outward = harborSide == CityEdge.West ? -1f : 1f;
            for (int i = 0; i < reservations.Water.Count; i++)
            {
                var water = reservations.Water[i];
                if (water.xMin > city.xMin && water.xMax < city.xMax && water.yMin < city.yMin && water.yMax > city.yMax)
                {
                    // Keep the engineered channel straight through every regional bridge
                    // and suburb; natural meanders begin beyond the developed region.
                    var region = developedRegion ?? city;
                    water.yMin = Mathf.Min(water.yMin, region.yMin - 250f);
                    water.yMax = Mathf.Max(water.yMax, region.yMax + 250f);
                    _river = water;
                }
                _shipping.Add(water);
                if (i < reservations.WaterOpens.Count && reservations.WaterOpens[i]) _harbors.Add(water);
            }
        }

        public float Distance(float x, float z)
        {
            float closest = float.MaxValue;
            foreach (var water in _shipping) closest = Mathf.Min(closest, IslandLandform.Distance(water, x, z));
            if (_river.width > 0f && (z < _river.yMin || z > _river.yMax))
            {
                RiverBanks(z, out var banks);
                closest = Mathf.Min(closest, Mathf.Max(0f, banks.x - x, x - banks.y));
            }
            foreach (var harbor in _harbors)
            {
                float inland = _outward < 0f ? harbor.xMax : harbor.xMin;
                float along = (x - inland) * _outward;
                float t = Mathf.Max(0f, along - 180f) / 700f;
                float flare = Mathf.SmoothStep(0f, 1f, t);
                float middle = harbor.center.y + 80f * flare * Mathf.Sin(t * 1.2f + _phase);
                float half = harbor.height * 0.5f + flare * (180f + 80f * Mathf.Sin(t * 1.7f - _phase));
                float dx = Mathf.Max(0f, -along), dz = Mathf.Max(0f, Mathf.Abs(z - middle) - half);
                closest = Mathf.Min(closest, Mathf.Sqrt(dx * dx + dz * dz));
            }
            return closest;
        }

        public bool RiverBanks(float z, out Vector2 banks)
        {
            banks = Vector2.zero;
            if (_river.width <= 0f) return false;
            float side = z < _river.yMin ? -1f : 1f;
            float reach = Mathf.Max(0f, _river.yMin - z, z - _river.yMax), t = reach / 1100f;
            float bend = Mathf.SmoothStep(0f, 1f, t) *
                (180f * Mathf.Sin(t * 2.1f + _phase * side) + 80f * Mathf.Sin(t * 4.3f));
            float half = _river.width * 0.5f + Mathf.Min(260f, reach * 0.17f);
            banks = new Vector2(_river.center.x + bend - half, _river.center.x + bend + half);
            return true;
        }
    }
}
