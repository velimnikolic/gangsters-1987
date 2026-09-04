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
        static readonly long[] Bytes = new long[Slots];
        static readonly string[] Names = new string[Slots];
        static long _mark;
        static long _byteMark;
        static int _frames;
        static float _since;

        /// <summary>Clear the accumulated section sample. The people census uses this
        /// before and after its preview-scene curve so its figures are the same sections
        /// the live update report labels, without leaking a partial sample into Play.</summary>
        public static void Reset()
        {
            for (int i = 0; i < Slots; i++)
            {
                Ticks[i] = 0;
                Bytes[i] = 0;
                Names[i] = null;
            }
            _mark = 0;
            _byteMark = 0;
            _frames = 0;
            _since = 0f;
        }

        /// <summary>Average milliseconds in one marked section of the current sample.</summary>
        public static double MillisecondsPerFrame(int slot)
        {
            if (slot < 0 || slot >= Slots || _frames == 0) return 0.0;
            return Ticks[slot] * (1000.0 / System.Diagnostics.Stopwatch.Frequency) / _frames;
        }

        /// <summary>A frame begins: the clock starts at the first section.</summary>
        public static void Frame()
        {
            _mark = System.Diagnostics.Stopwatch.GetTimestamp();
            // mono heap used so far: the delta across a section is what that section put
            // on the heap (GC.GetAllocatedBytesForCurrentThread is a stub in this Mono
            // and reads a constant, so it cannot be used). A collection inside a section
            // shows as a fall, so only the rises are counted below - the garbage a
            // section makes, which is what the collector will later have to walk.
            _byteMark = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
            _frames++;
        }

        /// <summary>The section that ended here took everything since the last mark.</summary>
        public static void Mark(int slot, string name)
        {
            if (slot < 0 || slot >= Slots) return;
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            Ticks[slot] += now - _mark;
            _mark = now;
            long nowB = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
            long grew = nowB - _byteMark;
            if (grew > 0) Bytes[slot] += grew;   // a fall is a collection, not this section's work
            _byteMark = nowB;
            Names[slot] = name;
        }

        /// <summary>Every few seconds, what the sections have cost per frame.</summary>
        public static void Report(bool on, float dt, string counts)
        {
            if (!on) { _since = 0f; return; }
            _since += Mathf.Max(dt, 0f);
            if (_since < 5f || _frames == 0) return;

            double perTick = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            double total = 0;
            long totalBytes = 0;
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < Slots; i++)
            {
                if (Names[i] == null || Ticks[i] == 0) continue;
                double ms = Ticks[i] * perTick / _frames;
                total += ms;
                totalBytes += Bytes[i];
                double kb = Bytes[i] / 1024.0 / _frames;
                sb.Append($"  {Names[i]} {ms:F1}ms/{kb:F1}KB");
            }
            Debug.Log($"[Tick] {total:F1} ms/frame, {totalBytes / 1024.0 / _frames:F1} KB/frame " +
                      $"over {_frames} frames ({counts}):{sb}");

            Reset();
        }
    }
}
