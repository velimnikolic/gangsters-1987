using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// Where the regional expressway's ring runs round the belt, and how far outside it
    /// the outer collector stands. The plan draws the ring from this; the region reads
    /// it back so a district is never stood on the collector when a river mouth has
    /// pushed the ring's rounded corner past the city rectangle.
    /// </summary>
    public static class RegionalRing
    {
        public const float Band = 166f, Terminal = 90f;
        /// <summary>The local collectors sit this far behind the ramp terminals.</summary>
        public const float Collector = 40f;

        /// <summary>The ring rectangle the mainline is threaded round, and its corner
        /// radius, for a belt, its river and the seed.</summary>
        public static Rect Of(Rect city, Rect river, int seed, out float radius)
        {
            var ring = Rect.MinMaxRect(city.xMin - Band, city.yMin - Band, city.xMax + Band, city.yMax + Band);
            radius = Mathf.Min(240f + new System.Random(seed ^ 0x45585052).Next(5) * 15f,
                Mathf.Min(ring.width, ring.height) * 0.24f);
            // The complete river span and its abutments must lie on a straight deck.
            // Widen the ring when the river is near a rounded corner.
            if (river.width > 0f)
            {
                ring.xMin = Mathf.Min(ring.xMin, river.xMin - radius - 160f);
                ring.xMax = Mathf.Max(ring.xMax, river.xMax + radius + 160f);
            }
            return ring;
        }

        /// <summary>How far past the belt a district on this edge must stand so its
        /// approach road leaves the outer collector with room to be a road.</summary>
        public static float ClearOf(Rect city, Rect river, int seed, CityEdge edge)
        {
            var ring = Of(city, river, seed, out _);
            const float road = 40f;
            switch (edge)
            {
                case CityEdge.East: return ring.xMax + Terminal + Collector + road - city.xMax;
                case CityEdge.West: return city.xMin - (ring.xMin - Terminal - Collector - road);
                case CityEdge.North: return ring.yMax + Terminal + Collector + road - city.yMax;
                default: return city.yMin - (ring.yMin - Terminal - Collector - road);
            }
        }
    }
}
