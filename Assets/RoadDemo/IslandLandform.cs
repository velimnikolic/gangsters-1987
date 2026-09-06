using UnityEngine;

namespace RoadDemo
{
    /// <summary>One continuous seeded island envelope, with a mountain spine and protected urban pads.</summary>
    public sealed class IslandLandform
    {
        public readonly Rect Bounds;
        public readonly int Seed;
        public readonly IslandRoadCorridors Roads = new IslandRoadCorridors();
        readonly DistrictReservations _reservations;
        readonly Rect _city;
        public readonly Rect UrbanRiver;
        public const float QuayWidth = 12f;
        /// <summary>Below the shared StreetKit gutter's measured -0.2303 m minimum.</summary>
        public const float AccessRoadBed = -0.3f;
        readonly Vector2 _centre, _radius;
        readonly float _ridgeSide, _phase;
        readonly IslandWaters _water;
        readonly IslandAirfield _airfield;

        public IslandLandform(Rect city, Rect region, int seed, DistrictReservations reservations,
            RegionalExpresswayPlan expressway, CityEdge harborSide, AirportDemo.AirportDistrict airport = null, Rect? urbanRiver = null)
        {
            Seed = seed; _city = city; _reservations = reservations;
            UrbanRiver = urbanRiver ?? city;
            _centre = region.center;
            _radius = Radius(region);
            Bounds = BoundsFor(region);
            var random = new System.Random(seed ^ 0x49534c45);
            _phase = (float)random.NextDouble() * 6.28f;
            _ridgeSide = random.Next(2) == 0 ? -1f : 1f;
            _water = new IslandWaters(city, reservations, harborSide, _phase, region);
            if (airport != null) _airfield = new IslandAirfield(airport);
            if (expressway != null)
            {
                foreach (var deck in expressway.Decks) Roads.Add(deck.Line, 14f, deck.Height);
                foreach (var ramp in expressway.Ramps) Roads.Add(ramp.Line, 6f, ramp.Height);
                // The same street tiles as Core include a gutter 23 cm below asphalt.
                // Lower every terrain vertex that can contribute to a triangle beneath
                // the pavement; clearing only its centreline leaves meadow in the gutter.
                float meshPadding = RegionalIslandView.Step * Mathf.Sqrt(2f);
                foreach (var road in expressway.Ground)
                    Roads.Add(road.Line, StreetKit.OuterHalf, s => 0f, AccessRoadBed, meshPadding);
            }
        }

        static Vector2 Radius(Rect region) =>
            new Vector2((region.width * 0.5f + 850f) * 1.2f, (region.height * 0.5f + 950f) * 1.2f);

        /// <summary>The terrain envelope, available before districts reserve their water.</summary>
        public static Rect BoundsFor(Rect region)
        {
            var radius = Radius(region);
            return new Rect(region.center - radius - Vector2.one * 500f, radius * 2f + Vector2.one * 1000f);
        }

        public float Coast(float x, float z)
        {
            float dx = (x - _centre.x) / _radius.x, dz = (z - _centre.y) / _radius.y;
            float angle = Mathf.Atan2(dz, dx);
            float norm = Mathf.Pow(Mathf.Pow(Mathf.Abs(dx), 2.45f) + Mathf.Pow(Mathf.Abs(dz), 2.45f), 1f / 2.45f);
            float bays = 210f * Mathf.Sin(angle * 3f + _phase) + 105f * Mathf.Sin(angle * 7f - _phase)
                + 45f * Mathf.Sin(angle * 13f + _phase * 2f);
            return (1f - norm) * Mathf.Min(_radius.x, _radius.y) + bays;
        }

        public float WaterDistance(float x, float z) => _water.Distance(x, z);
        public bool RiverBanks(float z, out Vector2 banks) => _water.RiverBanks(z, out banks);
        public float QuayDistance(float x, float z)
        {
            if (z >= UrbanRiver.yMin && z <= UrbanRiver.yMax || !_water.RiverBanks(z, out var banks)) return float.MaxValue;
            return x < banks.x ? banks.x - x : x > banks.y ? x - banks.y : float.MaxValue;
        }
        public float DevelopedDistance(float x, float z)
        {
            float d = Distance(_city, x, z);
            foreach (var flat in _reservations.Flat) d = Mathf.Min(d, Distance(flat.area, x, z));
            return d;
        }
        public float Height(float x, float z)
        {
            float coast = Coast(x, z), water = WaterDistance(x, z);
            if (water <= 0f) return -14f;
            float developed = DevelopedDistance(x, z);
            float u = (x - _centre.x) / _radius.x, v = (z - _centre.y) / _radius.y;
            float ridgeZ = _ridgeSide * (0.58f + 0.08f * Mathf.Sin(u * 4.6f + _phase));
            float ridge = Mathf.Exp(-Mathf.Pow((v - ridgeZ) / 0.115f, 2f));
            float peaks = 0.48f + 0.52f * IslandNoise.At(x * 0.002f, z * 0.0015f, Seed);
            float eroded = 1f - Mathf.Abs(IslandNoise.At(x * 0.006f, z * 0.006f, Seed + 7) * 2f - 1f);
            float mountain = ridge * (150f + 210f * peaks) * (0.72f + eroded * 0.28f);
            float hills = 8f + 35f * IslandNoise.At(x * 0.0028f, z * 0.0028f, Seed + 19)
                + 4f * IslandNoise.At(x * 0.022f, z * 0.022f, Seed + 37);
            float relief = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(35f, 210f, developed));
            float shoreFade = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(30f, 220f, coast));
            float basinFade = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(25f, 200f, water));
            float height = RoadDemoBuilder.RoadBed + relief * (hills + mountain) * shoreFade * basinFade;
            float beach = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(65f, -110f, coast));
            height = Mathf.Lerp(height, -28f, beach);
            if (_reservations.FlatAt(x, z, 90f, out float level, out float weight))
                height = Mathf.Lerp(height, level, weight);
            if (_airfield != null) height = _airfield.Shape(x, z, height);
            // Dry pads are protected against the feathered bay, but never against
            // actual shipping water. The basin itself remains below the hulls.
            float bank = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(65f, 0f, water));
            if (!_reservations.InPaved(x, z)) height = Mathf.Lerp(height, -14f, bank);
            float quay = QuayDistance(x, z);
            if (coast > 10f && quay < QuayWidth + 65f)
                height = Mathf.Lerp(height, RoadDemoBuilder.RoadBed,
                    (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(QuayWidth + 10f, QuayWidth + 65f, quay))) *
                    Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(10f, 30f, coast)));
            // Shore/quay grading must not raise terrain back through a road's gutter.
            return Roads.Shape(x, z, height, out _);
        }
        public bool Roadside(float x, float z) { Roads.Shape(x, z, 0f, out bool road); return road || QuayDistance(x, z) < QuayWidth + 5f; }
        public static float Distance(Rect box, float x, float z)
        {
            float dx = Mathf.Max(box.xMin - x, x - box.xMax, 0f);
            float dz = Mathf.Max(box.yMin - z, z - box.yMax, 0f);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }
}
