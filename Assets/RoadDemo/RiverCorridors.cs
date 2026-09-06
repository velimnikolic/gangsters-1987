using System;
using System.Collections.Generic;

namespace RoadDemo
{
    /// <summary>Channel exclusions read from the active city structure.</summary>
    public static class RiverCorridors
    {
        public static List<(float lo, float hi)> Crossings(CoreDistrict core, IEnumerable<Seam> seams,
            bool alongZ, Func<Seam, (float lo, float hi)> span, float bank)
        {
            var cuts = new List<(float lo, float hi)>();
            if (core != null)
            {
                var river = core.Frame.ToWorldRect(core.Layout.Water);
                if (!alongZ && river.width > 0f) cuts.Add((river.xMin, river.xMax));
                return cuts;
            }
            if (seams == null) return cuts;
            foreach (var seam in seams)
            {
                if (seam == null || seam.kind != SeamKind.River || seam.vertical == alongZ) continue;
                var water = span(seam);
                cuts.Add((water.lo - bank, water.hi + bank));
            }
            cuts.Sort((a, b) => a.lo.CompareTo(b.lo));
            return cuts;
        }
    }
}
