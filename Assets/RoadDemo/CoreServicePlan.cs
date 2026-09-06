using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>Seeded land use for neighbourhood services, before housing views exist.</summary>
    public sealed class CoreServicePlan
    {
        public sealed class Site
        {
            public bool Police;
            public CoreAmenityLayout.Site Parcel;
            public Rect Source;
            public CoreQuarterId Quarter;
        }

        public readonly List<Site> Sites = new List<Site>();
        public int FireCount => Sites.FindAll(s => !s.Police).Count;
        public int PoliceCount => Sites.FindAll(s => s.Police).Count;

        public bool Replaces(Rect box) => Sites.Exists(s => s.Source == box);
        public bool SurfaceAt(Vector2 point) => Sites.Exists(s => s.Parcel.Box.Contains(point));

        public void Plan(CoreLayout.Plan city, CoreRoads.Raster raster,
                         List<CoreAmenityLayout.Site> development, int seed, List<Rect> repurposed = null)
        {
            Sites.Clear();
            var candidates = new List<CoreAmenityLayout.Site>(development);
            // Existing generated residential blocks are also eligible. Their block ids and
            // territory remain stable; only their housing recipe is replaced by a service.
            foreach (var block in city.Residential)
                if (repurposed == null || !repurposed.Contains(block.Box))
                    candidates.Add(new CoreAmenityLayout.Site(block.Box, ParkingEntrySide.South, 0));
            var quarters = new HashSet<CoreQuarterId>();
            foreach (var block in city.Residential) quarters.Add(block.QuarterId);
            int fire = Mathf.Clamp(quarters.Count, 1, 5);
            int police = Mathf.Clamp((quarters.Count + 1) / 2, 1, 3);
            Pick(false, fire, 300f);
            Pick(true, police, 550f);

            foreach (var site in Sites)
            {
                development.RemoveAll(s => s.Box == site.Source);
                // Let CoreRoads cover the balance of an oversized block with ordinary
                // parking. The exact service footprint is skipped by ComposedSurfaceAt.
                CoreAmenityLayout.MarkParking(raster, site.Source);
            }

            void Pick(bool isPolice, int count, float spacing)
            {
                Rect template = isPolice ? PolicePrecinctBlock.PreviewBounds : FireStationBlock.BlockBounds;
                for (int n = 0; n < count; n++)
                {
                    Site best = null;
                    double bestScore = double.MinValue;
                    foreach (var candidate in candidates)
                    {
                        if (Sites.Exists(s => s.Source.Overlaps(candidate.Box))) continue;
                        var quarter = city.Territory?.QuarterAt(candidate.Box.center);
                        if (!quarter.HasValue || quarter.Value == CoreQuarterId.Downtown) continue;
                        foreach (ParkingEntrySide entry in System.Enum.GetValues(typeof(ParkingEntrySide)))
                        {
                            // Compact precinct parking opens east, and its public door
                            // opens north: both streets must exist before it can be used.
                            float width = isPolice ? template.height : template.width;
                            float depth = isPolice ? template.width : template.height;
                            if (!TryFrontage(raster, candidate.Box, entry, width, depth,
                                             out var crop, out int roadWidth)) continue;
                            if (isPolice && !HasPublicFront(raster, crop, entry)) continue;
                            float nearest = 1500f;
                            bool covered = false;
                            foreach (var other in Sites)
                            {
                                if (other.Police != isPolice) continue;
                                nearest = Mathf.Min(nearest, Vector2.Distance(crop.center, other.Parcel.Box.center));
                                covered |= other.Quarter == quarter.Value;
                            }
                            if (nearest < spacing) continue;
                            double score = (covered ? 0 : 10000000) + nearest * 1000 + roadWidth * 5000;
                            score -= candidate.Box.width * candidate.Box.height;
                            uint tie = unchecked((uint)(seed * 486187739 ^
                                Mathf.RoundToInt(crop.xMin) * 73856093 ^ Mathf.RoundToInt(crop.yMin) * 19349663));
                            score += tie / (double)uint.MaxValue * 1000;
                            if (score <= bestScore) continue;
                            bestScore = score;
                            best = new Site { Police = isPolice, Source = candidate.Box, Quarter = quarter.Value,
                                Parcel = new CoreAmenityLayout.Site(crop, entry, 0) };
                        }
                    }
                    if (best == null) break;
                    Sites.Add(best);
                }
            }
        }

        static bool HasPublicFront(CoreRoads.Raster raster, Rect box, ParkingEntrySide drive)
        {
            // Turning the authored east driveway onto the selected street also turns
            // the north public entrance onto the adjacent side clockwise from it.
            var door = drive == ParkingEntrySide.East ? ParkingEntrySide.North
                     : drive == ParkingEntrySide.North ? ParkingEntrySide.West
                     : drive == ParkingEntrySide.West ? ParkingEntrySide.South : ParkingEntrySide.East;
            return RoadWidth(raster, box, door) >= 2;
        }

        public static bool TryFrontage(CoreRoads.Raster raster, Rect source, ParkingEntrySide entry,
                                       float frontage, float depth, out Rect crop, out int roadWidth)
        {
            bool horizontal = entry == ParkingEntrySide.South || entry == ParkingEntrySide.North;
            crop = default;
            roadWidth = 0;
            if ((horizontal ? source.width : source.height) < frontage ||
                (horizontal ? source.height : source.width) < depth) return false;
            // Scan whole cells along the edge. This permits a service at a street corner
            // instead of demanding that a short road serve an entire oversized parcel.
            float length = horizontal ? source.width : source.height;
            for (float along = 0f; along <= length - frontage + 0.01f; along += CoreRoads.Cell)
            {
                Rect box = entry == ParkingEntrySide.North
                    ? new Rect(source.xMin + along, source.yMax - depth, frontage, depth)
                    : entry == ParkingEntrySide.South
                    ? new Rect(source.xMin + along, source.yMin, frontage, depth)
                    : entry == ParkingEntrySide.East
                    ? new Rect(source.xMax - depth, source.yMin + along, depth, frontage)
                    : new Rect(source.xMin, source.yMin + along, depth, frontage);
                int width = RoadWidth(raster, box, entry);
                if (width < 3 || width <= roadWidth) continue;
                crop = box;
                roadWidth = width;
            }
            return roadWidth >= 3;
        }

        public static int RoadWidth(CoreRoads.Raster raster, Rect box, ParkingEntrySide entry)
        {
            bool horizontal = entry == ParkingEntrySide.South || entry == ParkingEntrySide.North;
            float from = horizontal ? box.xMin : box.yMin;
            float to = horizontal ? box.xMax : box.yMax;
            int width = 7;
            for (float a = from + 2.5f; a < to; a += CoreRoads.Cell)
            {
                float x = horizontal ? a : entry == ParkingEntrySide.East ? box.xMax + 2.5f : box.xMin - 2.5f;
                float z = !horizontal ? a : entry == ParkingEntrySide.North ? box.yMax + 2.5f : box.yMin - 2.5f;
                var kind = raster.At(Mathf.FloorToInt((x - raster.X0) / CoreRoads.Cell),
                                     Mathf.FloorToInt((z - raster.Z0) / CoreRoads.Cell));
                int w = kind == (horizontal ? CoreRoads.Kind.BlvdEW : CoreRoads.Kind.BlvdNS) ? 7
                      : kind == (horizontal ? CoreRoads.Kind.StreetEW : CoreRoads.Kind.StreetNS) ? 3
                      : kind == (horizontal ? CoreRoads.Kind.NarrowEW : CoreRoads.Kind.NarrowNS) ? 2 : 0;
                width = Mathf.Min(width, w);
            }
            return width;
        }
    }
}
