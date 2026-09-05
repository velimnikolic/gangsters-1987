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
            float t = (RoadCarSimulation.Now + _offset) % Cycle;
            return northSouth ? t : (t + HalfCycle) % Cycle;
        }

        public bool GreenFor(bool northSouth) => AxisTime(northSouth) < Green;

        /// <summary>Time until the opposing axis receives green, including the
        /// yellow and clearance interval at the end of this axis's turn.</summary>
        public float ClearanceRemaining(bool northSouth) =>
            Mathf.Max(0f, HalfCycle - AxisTime(northSouth));

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

        // the phase the bulbs were last set to, per axis: 0 red, 1 yellow, 2 green,
        // -1 never - so a material is written when the light changes, not every frame
        int _shownNS = -1, _shownEW = -1;

        public void UpdateBulbs(SignalMaterials mats)
        {
            if (_bulbs.Count == 0) return;
            for (int axis = 0; axis < 2; axis++)
            {
                bool ns = axis == 0;
                bool g = GreenFor(ns);
                bool y = YellowFor(ns);
                int phase = g ? 2 : y ? 1 : 0;
                if ((ns ? _shownNS : _shownEW) == phase) continue;
                if (ns) _shownNS = phase; else _shownEW = phase;
                for (int i = 0; i < _bulbs.Count; i++)
                {
                    var b = _bulbs[i];
                    if (b.NorthSouth != ns) continue;
                    b.R.sharedMaterial = !g && !y ? mats.RedOn : mats.RedOff;
                    b.Y.sharedMaterial = y ? mats.YellowOn : mats.YellowOff;
                    b.G.sharedMaterial = g ? mats.GreenOn : mats.GreenOff;
                }
            }
        }
    }

    public class SignalMaterials
    {
        public Material RedOn, RedOff, YellowOn, YellowOff, GreenOn, GreenOff;
    }
}
