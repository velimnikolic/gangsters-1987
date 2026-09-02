using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// The black box. When a run is being watched (the play harness switches it on,
    /// nothing else does), every driver, every man on foot and every shot writes a
    /// line here - one JSON object a line, sim clock first - and the run is read
    /// afterwards by Tools/play/analyze.py rather than by watching the scene.
    ///
    /// Off it costs one static bool test at each site, which is why the calls can
    /// sit in the middle of the driving code.
    ///
    /// Sim clock, not wall clock: the harness steps time in fixed slices, so two
    /// runs of the same seed line up row for row and a fix can be read as a
    /// difference rather than an impression.
    /// </summary>
    public static class DriveTrace
    {
        /// <summary>Anything written at all. Off in a normal Play session.</summary>
        public static bool On;

        /// <summary>The sim clock the rows are stamped with, driven by the harness.</summary>
        public static float Now;

        /// <summary>Seconds between the per-frame samples of one car / one walker.
        /// Events are never sampled - they all go down.</summary>
        public static float SampleEvery = 0.1f;

        static StreamWriter _w;
        static readonly StringBuilder Sb = new StringBuilder(512);
        static int _rows, _dropped;
        static int _cap = 4000000;

        public static int Rows => _rows;

        public static void Open(string path, float sampleEvery = 0.1f)
        {
            Close();
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            _w = new StreamWriter(path, false, new UTF8Encoding(false), 1 << 20);
            SampleEvery = Mathf.Max(0.01f, sampleEvery);
            _rows = _dropped = 0;
            Now = 0f;
            On = true;
        }

        public static void Close()
        {
            if (_w == null) return;
            _w.Flush();
            _w.Dispose();
            _w = null;
            On = false;
        }

        public static void Flush() => _w?.Flush();

        // ------------------------------------------------------------------ writing

        /// <summary>One row: {"t":12.30,"k":"car",...fields...}. The fields are written
        /// by the caller into the shared builder through the Put helpers, so a row
        /// costs no allocation beyond the string itself.</summary>
        public static void Row(string kind, string fields)
        {
            if (!On || _w == null) return;
            if (_rows >= _cap) { _dropped++; return; }
            _rows++;
            _w.Write("{\"t\":");
            _w.Write(Now.ToString("F3", CultureInfo.InvariantCulture));
            _w.Write(",\"k\":\"");
            _w.Write(kind);
            _w.Write('"');
            if (!string.IsNullOrEmpty(fields)) { _w.Write(','); _w.Write(fields); }
            _w.Write("}\n");
        }

        /// <summary>An out-of-band thing that happened: a hard brake, a belt refusal, a
        /// shot, a stall. Always written.</summary>
        public static void Event(string kind, string who, string what, string fields = null)
        {
            if (!On) return;
            var sb = Take();
            Str(sb, "who", who);
            Str(sb, "what", what);
            if (!string.IsNullOrEmpty(fields)) { if (sb.Length > 0) sb.Append(','); sb.Append(fields); }
            Row(kind, sb.ToString());
        }

        /// <summary>
        /// ONE FAMILY'S TURN OF MIND (RIVAL-005). What it decided, which tier decided it,
        /// what it was reasoning from and what it can afford - the row the underworld
        /// tally reads to say whether twenty houses played the game or stood still.
        /// </summary>
        public static void House(int gang, int tier, string intent, string reason,
            int safe, int payroll, float milliseconds = 0f)
        {
            if (!On) return;
            var sb = Take();
            Int(sb, "gang", gang);
            Int(sb, "tier", tier);
            Str(sb, "intent", intent);
            Str(sb, "why", reason);
            Int(sb, "safe", safe);
            Int(sb, "payroll", payroll);
            // What the turn of mind COST. Twenty families thinking is a budget, and a
            // budget nobody measures is a wish (RIVAL-008).
            Num(sb, "ms", milliseconds, "F3");
            Row("house", sb.ToString());
        }

        // -------- the shared builder, so a row is one string and no garbage per field

        public static StringBuilder Take() { Sb.Clear(); return Sb; }

        public static void Num(StringBuilder sb, string key, float v, string fmt = "F2")
        {
            if (sb.Length > 0) sb.Append(',');
            sb.Append('"').Append(key).Append("\":").Append(v.ToString(fmt, CultureInfo.InvariantCulture));
        }

        public static void Int(StringBuilder sb, string key, int v)
        {
            if (sb.Length > 0) sb.Append(',');
            sb.Append('"').Append(key).Append("\":").Append(v.ToString(CultureInfo.InvariantCulture));
        }

        public static void Bool(StringBuilder sb, string key, bool v)
        {
            if (sb.Length > 0) sb.Append(',');
            sb.Append('"').Append(key).Append("\":").Append(v ? "true" : "false");
        }

        public static void Str(StringBuilder sb, string key, string v)
        {
            if (v == null) return;
            if (sb.Length > 0) sb.Append(',');
            sb.Append('"').Append(key).Append("\":\"");
            Escape(sb, v);
            sb.Append('"');
        }

        public static void Vec(StringBuilder sb, string key, Vector3 v)
        {
            if (sb.Length > 0) sb.Append(',');
            sb.Append('"').Append(key).Append("\":[")
              .Append(v.x.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
              .Append(v.z.ToString("F2", CultureInfo.InvariantCulture)).Append(']');
        }

        static void Escape(StringBuilder sb, string v)
        {
            foreach (char c in v)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c == '\n' || c == '\r' || c == '\t') sb.Append(' ');
                else if (c < 0x20) sb.Append(' ');
                else sb.Append(c);
            }
        }
    }
}
