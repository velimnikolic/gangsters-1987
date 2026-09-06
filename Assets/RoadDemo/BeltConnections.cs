using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>One crossing per actual access line, shared by grid and raster city hosts.</summary>
    public static class BeltConnections
    {
        public static Dictionary<CityEdge, List<(float v, float strip)>> Collect(
            IReadOnlyList<DistrictSlot> slots, float[] vx, float[] hz, CoreRegion region)
        {
            var crossings = new Dictionary<CityEdge, List<(float, float)>>();
            foreach (CityEdge edge in System.Enum.GetValues(typeof(CityEdge)))
                crossings[edge] = new List<(float, float)>();
            foreach (var slot in slots)
            {
                var axis = CoreRegion.Vertical(slot.edge) ? vx : hz;
                foreach (int line in slot.pinLines)
                {
                    if (line < 0 || line >= axis.Length) continue;
                    float v = axis[line];
                    if (!crossings[slot.edge].Exists(other => Mathf.Abs(other.Item1 - v) < 0.5f))
                        crossings[slot.edge].Add((v, slot.strip));
                }
            }
            if (region != null)
                foreach (var c in region.Connections)
                {
                    var side = crossings[c.Edge];
                    int found = side.FindIndex(other => Mathf.Abs(other.Item1 - c.Across) < 0.5f);
                    float strip = c.Through ? c.Quarter.Slot.strip : 0f;
                    if (found < 0) side.Add((c.Across, strip));
                    else side[found] = (side[found].Item1, Mathf.Max(side[found].Item2, strip));
                }
            return crossings;
        }
    }
}
