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
        // Two frame slots keep the preceding completed Update available regardless
        // of whether the perf probe runs before or after the city's current Update.
        static readonly long[,] FrameTicks = new long[2, Slots];
        static readonly int[] FrameIds = { -1, -1 };
        static int _frameSlot;
        // the same sections as profiler markers, so the Profiler window and the raw
        // frame data name them ("city/cars") instead of one BehaviourUpdate lump
        static readonly Unity.Profiling.ProfilerMarker[] Markers =
        {
            new Unity.Profiling.ProfilerMarker("city/signals"), new Unity.Profiling.ProfilerMarker("city/cars"),
            new Unity.Profiling.ProfilerMarker("city/patrol cars"), new Unity.Profiling.ProfilerMarker("city/civilians"),
            new Unity.Profiling.ProfilerMarker("city/crowd"), new Unity.Profiling.ProfilerMarker("city/officers"),
            new Unity.Profiling.ProfilerMarker("city/districts"), new Unity.Profiling.ProfilerMarker("city/chats"),
        };
        static int _openMarker = -1;

        /// <summary>Clear the accumulated section sample. The people census uses this
        /// before and after its preview-scene curve so its figures are the same sections
        /// the live update report labels, without leaking a partial sample into Play.</summary>
        public static void Reset()
        {
            ClearTotals();
            FrameIds[0] = FrameIds[1] = -1;
            if (_openMarker >= 0) Markers[_openMarker].End();
            _openMarker = -1;
        }

        static void ClearTotals()
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
            _frameSlot = Time.frameCount & 1;
            FrameIds[_frameSlot] = Time.frameCount;
            for (int i = 0; i < Slots; i++) FrameTicks[_frameSlot, i] = 0;
            _mark = System.Diagnostics.Stopwatch.GetTimestamp();
            // mono heap used so far: the delta across a section is what that section put
            // on the heap (GC.GetAllocatedBytesForCurrentThread is a stub in this Mono
            // and reads a constant, so it cannot be used). A collection inside a section
            // shows as a fall, so only the rises are counted below - the garbage a
            // section makes, which is what the collector will later have to walk.
            _byteMark = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
            _frames++;
            if (_openMarker >= 0) Markers[_openMarker].End();
            _openMarker = 0;
            Markers[0].Begin();
        }

        /// <summary>The section that ended here took everything since the last mark.</summary>
        public static void Mark(int slot, string name)
        {
            if (slot < 0 || slot >= Slots) return;
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            Ticks[slot] += now - _mark;
            FrameTicks[_frameSlot, slot] += now - _mark;
            _mark = now;
            long nowB = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
            long grew = nowB - _byteMark;
            if (grew > 0) Bytes[slot] += grew;   // a fall is a collection, not this section's work
            _byteMark = nowB;
            Names[slot] = name;
            if (_openMarker >= 0) Markers[_openMarker].End();
            _openMarker = slot + 1 < Slots ? slot + 1 : -1;
            if (_openMarker >= 0) Markers[_openMarker].Begin();
        }

        /// <summary>Only an exact frame match can explain a recorded hitch.</summary>
        public static void AppendFrame(System.Text.StringBuilder into, int frame)
        {
            if (frame < 0) return;
            int row = frame & 1;
            if (FrameIds[row] != frame)
            {
                into.AppendLine($"      city/ no sample for frame {frame}");
                return;
            }
            double perTick = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            double total = 0;
            for (int i = 0; i < Slots; i++)
            {
                double ms = FrameTicks[row, i] * perTick;
                total += ms;
                if (ms <= .5) continue;
                into.AppendLine($"      city/{SectionName(i)} {ms:F2} ms");
            }
            into.AppendLine($"      city/ timed sections total {total:F2} ms " +
                "(signals through chats only; excludes merge and wayside watch)");
        }

        static string SectionName(int slot) => slot switch
        {
            0 => "signals", 1 => "cars", 2 => "patrol cars", 3 => "civilians",
            4 => "crowd", 5 => "officers", 6 => "districts", 7 => "chats", _ => "unknown",
        };

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

            ClearTotals();
        }
    }
}
