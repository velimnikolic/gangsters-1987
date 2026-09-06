using UnityEngine;

namespace RoadDemo
{
    /// <summary>Converts district pin lines into the common quarter-turn coordinate contract.</summary>
    public static class DistrictPlacement
    {
        /// <summary>A district faces the city at local +Z, with its body below local
        /// z=0. Links give local X street positions; the first link is at zero.</summary>
        public static bool Frame(DistrictSlot slot, float[] verticalRoadX, float[] horizontalRoadZ,
                                 Rect city, out DistrictFrame frame, out float[] links)
        {
            frame = DistrictFrame.Identity;
            links = null;
            if (slot.pinLines == null || slot.pinLines.Length == 0) return false;

            bool vertical = slot.edge == CityEdge.South || slot.edge == CityEdge.North;
            var axis = vertical ? verticalRoadX : horizontalRoadZ;
            foreach (int line in slot.pinLines)
                if (line < 0 || line >= axis.Length) return false;

            float gx0 = city.xMin, gx1 = city.xMax, gz0 = city.yMin, gz1 = city.yMax;

            int yaw;
            var pins = new Vector3[slot.pinLines.Length];
            switch (slot.edge)
            {
                case CityEdge.South:
                    yaw = 0;
                    for (int k = 0; k < pins.Length; k++) pins[k] = new Vector3(axis[slot.pinLines[k]], 0f, gz0 - slot.strip);
                    break;
                case CityEdge.North:
                    yaw = 180;
                    for (int k = 0; k < pins.Length; k++) pins[k] = new Vector3(axis[slot.pinLines[k]], 0f, gz1 + slot.strip);
                    break;
                case CityEdge.West:
                    yaw = 90;
                    for (int k = 0; k < pins.Length; k++) pins[k] = new Vector3(gx0 - slot.strip, 0f, axis[slot.pinLines[k]]);
                    break;
                default:
                    yaw = 270;
                    for (int k = 0; k < pins.Length; k++) pins[k] = new Vector3(gx1 + slot.strip, 0f, axis[slot.pinLines[k]]);
                    break;
            }

            // the district's origin is the westmost (in its own frame) of its pins, so
            // its links run from zero upward whichever shore it is on
            var probe = new DistrictFrame { origin = pins[0], yaw = yaw };
            float min = float.MaxValue;
            var local = new float[pins.Length];
            for (int k = 0; k < pins.Length; k++)
            {
                local[k] = probe.ToLocal(pins[k]).x;
                min = Mathf.Min(min, local[k]);
            }
            frame = new DistrictFrame { origin = probe.ToWorld(new Vector3(min, 0f, 0f)), yaw = yaw };
            links = new float[pins.Length];
            for (int k = 0; k < pins.Length; k++) links[k] = local[k] - min;
            System.Array.Sort(links);
            return true;
        }

    }
}
