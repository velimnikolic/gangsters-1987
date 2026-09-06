using System.Collections.Generic;

namespace RoadDemo
{
    /// <summary>Map labels projected from the same region plan as the physical districts.</summary>
    public static class CoreRegionMap
    {
        public static void AddDistricts(CoreRegion region, List<TurfDistrict> districts)
        {
            if (region == null) return;
            foreach (var quarter in region.Quarters)
                districts.Add(new TurfDistrict { Name = quarter.Slot.name.ToUpperInvariant(), World = quarter.World });
        }
    }
}
