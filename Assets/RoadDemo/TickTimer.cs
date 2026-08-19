using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// Where the frame goes, section by section, for whoever is ticking a city.
    ///
    /// The perf probe says "scripts Update: 140 ms" and stops there, which is the one
    /// number that never tells you what to fix. This splits that 140 ms across the
    /// things a city ticks - the traffic, the crowd, the police, the districts - and
    /// prints the average every few seconds. It is a stopwatch and eight longs: it
    /// costs a handful of microseconds a frame, and it is left in because the counts
    /// go on changing.
    /// </summary>
    public static class TickTimer
    {
        const int Slots = 8;
        static readonly long[] Ticks = new long[Slots];
        static readonly string[] Names = new string[Slots];
        static long _mark;
        static int _frames;
        static float _since;

        /// <summary>A frame begins: the clock starts at the first section.</summary>
        public static void Frame()
        {
            _mark = System.Diagnostics.Stopwatch.GetTimestamp();
            _frames++;
        }

        /// <summary>The section that ended here took everything since the last mark.</summary>
        public static void Mark(int slot, string name)
        {
            if (slot < 0 || slot >= Slots) return;
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            Ticks[slot] += now - _mark;
            Names[slot] = name;
            _mark = now;
        }

        /// <summary>Every few seconds, what the sections have cost per frame.</summary>
        public static void Report(bool on, float dt, string counts)
        {
            if (!on) { _since = 0f; return; }
            _since += Mathf.Max(dt, 0f);
            if (_since < 5f || _frames == 0) return;

            double perTick = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            double total = 0;
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < Slots; i++)
            {
                if (Names[i] == null || Ticks[i] == 0) continue;
                double ms = Ticks[i] * perTick / _frames;
                total += ms;
                sb.Append($"  {Names[i]} {ms:F1}");
            }
            Debug.Log($"[Tick] {total:F1} ms/frame over {_frames} frames ({counts}):{sb}");

            for (int i = 0; i < Slots; i++) Ticks[i] = 0;
            _frames = 0;
            _since = 0f;
        }
    }
}
