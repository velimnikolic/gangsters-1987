using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    // Two-phase intersection signal: north-south flow, then east-west,
    // each with a yellow tail and an all-red gap.
    public class TrafficSignal
    {
        public const float Green = 9f;
        public const float Yellow = 2.5f;
        public const float AllRed = 1.5f;
        public const float HalfCycle = Green + Yellow + AllRed;
        public const float Cycle = HalfCycle * 2f;

        readonly float _offset;

        public TrafficSignal(float offset)
        {
            _offset = offset;
        }

        float AxisTime(bool northSouth)
        {
            float t = (Time.time + _offset) % Cycle;
            return northSouth ? t : (t + HalfCycle) % Cycle;
        }

        public bool GreenFor(bool northSouth) => AxisTime(northSouth) < Green;

        public bool YellowFor(bool northSouth)
        {
            float t = AxisTime(northSouth);
            return t >= Green && t < Green + Yellow;
        }

        // Seconds of red left for the given car axis; 0 while it is green or yellow.
        // Pedestrians crossing that axis use this to decide whether they can make it.
        public float RedRemaining(bool northSouth)
        {
            float t = AxisTime(northSouth);
            return t < Green + Yellow ? 0f : Cycle - t;
        }

        public struct BulbSet
        {
            public bool NorthSouth;
            public MeshRenderer R, Y, G;
        }

        readonly List<BulbSet> _bulbs = new List<BulbSet>();

        public void AddBulbs(BulbSet set) => _bulbs.Add(set);

        public void UpdateBulbs(SignalMaterials mats)
        {
            for (int i = 0; i < _bulbs.Count; i++)
            {
                var b = _bulbs[i];
                bool g = GreenFor(b.NorthSouth);
                bool y = YellowFor(b.NorthSouth);
                b.R.sharedMaterial = !g && !y ? mats.RedOn : mats.RedOff;
                b.Y.sharedMaterial = y ? mats.YellowOn : mats.YellowOff;
                b.G.sharedMaterial = g ? mats.GreenOn : mats.GreenOff;
            }
        }
    }

    public class SignalMaterials
    {
        public Material RedOn, RedOff, YellowOn, YellowOff, GreenOn, GreenOff;
    }
}
