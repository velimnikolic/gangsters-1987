using UnityEngine;

namespace RoadDemo
{
    /// <summary>Rounds due during one simulation step. A blocked trigger retains
    /// its cooldown, but never accumulates a backlog of shots while aiming.</summary>
    public readonly struct GunCadence
    {
        public readonly int Count;
        public readonly float First, Interval;

        GunCadence(int count, float first, float interval)
        { Count = count; First = first; Interval = interval; }

        public float At(int index) => First + index * Interval;

        public static GunCadence Advance(ref float timer, float dt, float interval, bool ready = true)
        {
            dt = Mathf.Max(0f, dt);
            interval = Mathf.Max(.01f, interval);
            float first = Mathf.Max(0f, timer);
            timer = first - dt;
            if (!ready) { timer = Mathf.Max(0f, timer); return default; }
            if (timer > .00001f) return default;
            int count = Mathf.FloorToInt((-timer + .00001f) / interval) + 1;
            timer = Mathf.Max(0f, timer + count * interval);
            return new GunCadence(count, first, interval);
        }
    }
}
