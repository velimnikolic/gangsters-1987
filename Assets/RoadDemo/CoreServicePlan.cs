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
            // Placement coverage; PoliceDispatch retains ownership of incident response.
            public readonly List<CoreQuarterId> Serves = new List<CoreQuarterId>();
        }

        public readonly List<Site> Sites = new List<Site>();
        public int FireCount => Sites.FindAll(s => !s.Police).Count;
        public int PoliceCount => Sites.FindAll(s => s.Police).Count;
        public int ExistingPoliceCount { get; private set; }
        public int PoliceTarget { get; private set; }
        public int TotalPoliceCount => ExistingPoliceCount + PoliceCount;

        public void Clear()
        {
            Sites.Clear();
            ExistingPoliceCount = PoliceTarget = 0;
        }

        public bool Replaces(Rect box) => Sites.Exists(s => s.Source == box);
        public bool SurfaceAt(Vector2 point) => Sites.Exists(s => s.Parcel.Box.Contains(point));

        public void Plan(CoreLayout.Plan city, CoreRoads.Raster raster,
                         List<CoreAmenityLayout.Site> development, int seed, List<Rect> repurposed = null)
        {
            Clear();
            var candidates = new List<CoreAmenityLayout.Site>(development);
            // Existing generated residential blocks are also eligible. Their block ids and
            // territory remain stable; only their housing recipe is replaced by a service.
            foreach (var block in city.Residential)
                if (repurposed == null || !repurposed.Contains(block.Box))
                    candidates.Add(new CoreAmenityLayout.Site(block.Box, ParkingEntrySide.South, 0));
            var quarters = new List<CoreQuarterDefinition>();
            foreach (var quarter in city.Territory.Quarters)
                if (quarter.BlockIds.Count > 0) quarters.Add(quarter);
            quarters.Sort((a, b) => a.Id.CompareTo(b.Id));
            PoliceTarget = (quarters.Count + 1) / 2;
            ExistingPoliceCount = 0;
            var uncovered = new List<CoreQuarterDefinition>(quarters);
            // The authored downtown station is already a working precinct. Count it and
            // its nearest quarter before reserving additional compact station blocks.
            foreach (var block in city.Territory.Blocks)
            {
                if (block.SourceName != "police-station-block") continue;
                ExistingPoliceCount++;
                uncovered.RemoveAll(q => q.Id == block.QuarterId);
                var neighbour = Nearest(uncovered, block.LocalBounds.center);
                if (neighbour != null) uncovered.Remove(neighbour);
            }
            // Reserve police corners first: they need streets on two sides. A fire
            // station needs only one, so it must not consume the last usable corner.
            while (uncovered.Count > 0 && TotalPoliceCount < PoliceTarget)
            {
                var first = uncovered[0];
                var partners = new List<CoreQuarterDefinition>(uncovered);
                partners.RemoveAt(0);
                partners.Sort((a, b) =>
                {
                    int distance = (a.LocalAnchor - first.LocalAnchor).sqrMagnitude.CompareTo(
                        (b.LocalAnchor - first.LocalAnchor).sqrMagnitude);
                    return distance != 0 ? distance : a.Id.CompareTo(b.Id);
                });
                if (partners.Count == 0) partners.Add(null);
                bool placed = false;
                foreach (var second in partners)
                {
                    if (!TryPick(true, first, second)) continue;
                    uncovered.Remove(first);
                    if (second != null) uncovered.Remove(second);
                    placed = true;
                    break;
                }
                // A failed pair stays unassigned while every other partner is tried.
                // Only a successfully reserved precinct consumes coverage.
                if (!placed) break;
            }
            foreach (var quarter in quarters)
                if (quarter.Id != CoreQuarterId.Downtown && !TryPick(false, quarter, null))
                    Debug.LogWarning($"[Core] no fire station parcel in {quarter.Name}.");
            if (TotalPoliceCount != PoliceTarget)
                Debug.LogWarning($"[Core] precinct coverage incomplete: {TotalPoliceCount}/{PoliceTarget} " +
                                 $"for {quarters.Count} quarters; no suitable street corner.");

            foreach (var site in Sites)
            {
                development.RemoveAll(s => s.Box == site.Source);
                // Let CoreRoads cover the balance of an oversized block with ordinary
                // parking. The exact service footprint is skipped by ComposedSurfaceAt.
                CoreAmenityLayout.MarkParking(raster, site.Source);
            }

            bool TryPick(bool isPolice, CoreQuarterDefinition first, CoreQuarterDefinition second)
            {
                Rect template = isPolice ? PolicePrecinctBlock.PreviewBounds : FireStationBlock.BlockBounds;
                Vector2 anchor = second == null ? first.LocalAnchor
                    : (first.LocalAnchor + second.LocalAnchor) * 0.5f;
                Site best = null;
                double bestScore = double.MinValue;
                foreach (var candidate in candidates)
                {
                    if (Sites.Exists(s => s.Source.Overlaps(candidate.Box))) continue;
                    var quarter = city.Territory.QuarterAt(candidate.Box.center);
                    // Downtown may host a replacement if a retained plan lost its
                    // authored station; it is otherwise already removed from coverage.
                    if (quarter != first.Id && (second == null || quarter != second.Id)) continue;
                    foreach (ParkingEntrySide entry in System.Enum.GetValues(typeof(ParkingEntrySide)))
                    {
                        // The east driveway and north public door both need a street.
                        float width = isPolice ? template.height : template.width;
                        float depth = isPolice ? template.width : template.height;
                        if (!TryFrontage(raster, candidate.Box, entry, width, depth,
                                         out var crop, out int roadWidth, isPolice)) continue;
                        // Keep facilities within the neighbourhood they serve, near its
                        // centre. Maximising distance from other stations pushed them out
                        // to the city's fringe; a hard spacing veto also dropped quotas.
                        double score = -(crop.center - anchor).sqrMagnitude;
                        score += roadWidth * 100;
                        score -= candidate.Box.width * candidate.Box.height * 0.01;
                        uint tie = unchecked((uint)(seed * 486187739 ^
                            Mathf.RoundToInt(crop.xMin) * 73856093 ^ Mathf.RoundToInt(crop.yMin) * 19349663));
                        score += tie / (double)uint.MaxValue;
                        if (score <= bestScore) continue;
                        bestScore = score;
                        best = new Site { Police = isPolice, Source = candidate.Box, Quarter = quarter.Value,
                            Parcel = new CoreAmenityLayout.Site(crop, entry, 0) };
                    }
                }
                if (best == null) return false;
                best.Serves.Add(first.Id);
                if (second != null) best.Serves.Add(second.Id);
                Sites.Add(best);
                return true;
            }
        }

        static CoreQuarterDefinition Nearest(List<CoreQuarterDefinition> quarters, Vector2 anchor)
        {
            CoreQuarterDefinition best = null;
            float distance = float.MaxValue;
            foreach (var quarter in quarters)
            {
                float next = (quarter.LocalAnchor - anchor).sqrMagnitude;
                if (next >= distance) continue;
                best = quarter;
                distance = next;
            }
            return best;
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
                                       float frontage, float depth, out Rect crop, out int roadWidth, bool publicFront = false)
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
                // Test every crop, not just the first equally wide driveway. The public
                // street can be at the opposite end of this source block.
                if (publicFront && !HasPublicFront(raster, box, entry)) continue;
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
