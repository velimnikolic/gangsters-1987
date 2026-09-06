using System;
using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>A short recovery path inside the current sidewalk/crossing corridor.
    /// Static collision remains the walker's GraphStepClear rule. No graph junction,
    /// traffic-light admission or arrival is bypassed by this local search.</summary>
    internal sealed class PedestrianGraphDetour
    {
        const float Pitch = 0.5f, Behind = 2f, Ahead = 4f;
        const int Columns = 17, MaxNodes = 13 * Columns;
        readonly Func<Vector3, Vector3, bool> _clear;
        readonly Func<PedLink, Vector3, Vector3?> _recover;
        readonly Func<Vector3, Vector3, bool> _escape;
        readonly List<Vector3> _path = new List<Vector3>();
        PedLink _link;
        Vector3 _expected;
        int _cursor;
        float _blockedSeconds;
        int _failedVersion = -1;
        bool _queued;
        Vector3? _recovered;
        public bool Pending => _link != null;

        // Searches run serially on the simulation thread. The scratch grid is shared;
        // only the few pulled corners belong to an individual walker.
        static readonly Vector3[] Points = new Vector3[MaxNodes];
        static readonly float[] Cost = new float[MaxNodes];
        static readonly int[] Parent = new int[MaxNodes];
        static readonly bool[] Closed = new bool[MaxNodes];
        static readonly WalkHeap Open = new WalkHeap();
        static readonly List<Vector3> Reverse = new List<Vector3>();

        const int PlansPerFrame = 2;
        static int BudgetFrame = -1, Remaining, FailureVersion = -1;
        static readonly Queue<WeakReference<PedestrianGraphDetour>> Waiting = new Queue<WeakReference<PedestrianGraphDetour>>();
        static readonly HashSet<(PedLink, Vector3)> Failures = new HashSet<(PedLink, Vector3)>();
#if UNITY_5_3_OR_NEWER
        static readonly Unity.Profiling.ProfilerMarker Marker = new Unity.Profiling.ProfilerMarker("Pedestrian.GraphDetour");
#endif

#if UNITY_5_3_OR_NEWER
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#endif
        internal static void ResetForPlay()
        {
            Waiting.Clear(); Failures.Clear();
            BudgetFrame = FailureVersion = -1; Remaining = 0;
        }

        public PedestrianGraphDetour(Func<Vector3, Vector3, bool> clear,
            Func<PedLink, Vector3, Vector3?> recover = null,
            Func<Vector3, Vector3, bool> escape = null)
        { _clear = clear; _recover = recover; _escape = escape; }

        void Queue()
        {
            if (_queued) return;
            _queued = true;
            Waiting.Enqueue(new WeakReference<PedestrianGraphDetour>(this));
        }

        static void Service(int frame, int version, float now)
        {
            if (BudgetFrame != frame) { BudgetFrame = frame; Remaining = PlansPerFrame; }
            if (FailureVersion != version) { FailureVersion = version; Failures.Clear(); }
            while (Remaining > 0 && Waiting.Count > 0)
            {
                if (!Waiting.Dequeue().TryGetTarget(out var walker) || !walker._queued || !walker.Pending) continue;
                walker._queued = false;
                var key = (walker._link, walker._expected);
                if (Failures.Contains(key)) { walker._failedVersion = version; continue; }
                Remaining--;
                walker.Plan(walker._link, walker._expected, Metre(walker._link, walker._expected));
                if (walker._path.Count > 0 || walker._recovered.HasValue) continue;
                walker._failedVersion = version;
                if (Failures.Count >= 256) Failures.Clear();
                Failures.Add(key);
            }
        }

        public void Begin(PedLink link, Vector3 from, float now, int frame, int version)
        {
            if (link == null) return;
            _link = link;
            _expected = from;
            _blockedSeconds = 0f;
            _failedVersion = -1;
            Queue();
            Service(frame, version, now);
        }

        public bool Step(PedLink link, Vector3 from, float budget, float now,
                         out Vector3 position, out bool turnBack, int frame, int version, float activeSeconds = 1f / 60f)
        {
            position = from;
            turnBack = false;
            // A new order/link or a bench/door/chat moved the walker: this path no
            // longer belongs to the feet. Normal graph movement chooses afresh.
            if (link == null || link != _link || FlatDistance(from, _expected) > 0.05f)
            { Reset(); return false; }
            if (budget <= 0f) return true; // crowd brake or animation still owns the stop
            _blockedSeconds += Mathf.Max(0f, activeSeconds);

            if (_cursor >= _path.Count && !_recovered.HasValue && _failedVersion != version) Queue();
            Service(frame, version, now);
            if (_recovered.HasValue)
            {
                // The same stride budget applies while leaving an invalid placement.
                // The shared obstacle query proves outward motion from every overlap
                // and a clear swept circle against every other solid, on every step.
                var target = _recovered.Value;
                float distance = FlatDistance(from, target);
                var next = Vector3.Lerp(from, target, distance <= budget ? 1f : budget / distance);
                if (_clear(from, next) || (_escape != null && _escape(from, next)))
                {
                    position = _expected = next;
                    _blockedSeconds = 0f;
                    if (distance <= budget) _recovered = null;
                    return true;
                }
                _recovered = null; _path.Clear(); _cursor = 0;
            }
            if (_cursor < _path.Count)
            {
                var target = _path[_cursor];
                float distance = FlatDistance(from, target);
                var next = Vector3.Lerp(from, target,
                    distance <= budget ? 1f : budget / distance);
                if (_clear(from, next))
                {
                    position = _expected = next;
                    _blockedSeconds = 0f;
                    if (distance <= budget)
                    {
                        _cursor++;
                        if (_cursor == _path.Count) Reset();
                    }
                    return true;
                }
                // A newly streamed prop can invalidate a planned segment. Never
                // commit that step; retry against live geometry at a bounded rate.
                _path.Clear();
                _cursor = 0;
            }
            if (_blockedSeconds >= 2f)
            { turnBack = true; Reset(); }
            return true;
        }

        void Reset() { _link = null; _path.Clear(); _cursor = 0; _queued = false; _recovered = null; }

        void Plan(PedLink link, Vector3 from, float t)
        {
#if UNITY_5_3_OR_NEWER
            using var marker = Marker.Auto();
#endif
            _path.Clear();
            _cursor = 0;
            if (link.Length <= 0.001f) return;
            if (!_clear(from, from))
            {
                var repaired = _recover?.Invoke(link, from);
                if (!repaired.HasValue) return;
                _recovered = from = repaired.Value;
                t = Metre(link, from);
            }
            float first = Mathf.Max(0f, t - (link.Gated ? 0f : Behind));
            float last = Mathf.Min(link.Length, t + Ahead);
            if (last - t < 0.01f) return;
            int rows = Mathf.Min(13, Mathf.CeilToInt((last - first) / Pitch) + 1);
            float half = link.Gated ? PedestrianAgent.ZebraSlip : PedestrianAgent.PavementLane;
            var direction = link.To.Pos - link.From.Pos;
            direction.y = 0f;
            if (direction.sqrMagnitude < 1e-6f) return;
            direction.Normalize();
            var right = new Vector3(direction.z, 0f, -direction.x);
            Open.Clear();
            for (int row = 0; row < rows; row++)
            {
                float metre = Mathf.Lerp(first, last, row / (float)(rows - 1));
                var centre = Vector3.Lerp(link.From.Pos, link.To.Pos, metre / link.Length);
                if (link.Gated) centre.y -= 0.08f * Mathf.Sin(Mathf.PI * metre / link.Length);
                for (int col = 0; col < Columns; col++)
                {
                    int i = row * Columns + col;
                    Points[i] = centre + right * Mathf.Lerp(-half, half, col / (float)(Columns - 1));
                    Cost[i] = float.PositiveInfinity;
                    Parent[i] = -1;
                    Closed[i] = false;
                    // Connect only a small neighbourhood of the true feet. Every
                    // connector and diagonal gets the same exact swept-circle proof.
                    float distance = FlatDistance(from, Points[i]);
                    if (distance > 0.8f || !_clear(from, Points[i])) continue;
                    Cost[i] = distance;
                    Open.Push(i, distance + last - metre);
                }
            }
            int goal = -1;
            while (Open.Count > 0)
            {
                int i = Open.Pop();
                if (Closed[i]) continue;
                Closed[i] = true;
                int row = i / Columns, col = i % Columns;
                if (row == rows - 1) { goal = i; break; }
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if ((dx == 0 && dz == 0) || (link.Gated && dz < 0)) continue;
                        int r = row + dz, c = col + dx;
                        if (r < 0 || r >= rows || c < 0 || c >= Columns) continue;
                        int n = r * Columns + c;
                        if (Closed[n]) continue;
                        float cost = Cost[i] + FlatDistance(Points[i], Points[n]);
                        if (cost >= Cost[n] || !_clear(Points[i], Points[n])) continue;
                        Cost[n] = cost;
                        Parent[n] = i;
                        float remaining = (rows - 1 - r) * (last - first) / (rows - 1);
                        Open.Push(n, cost + remaining);
                    }
            }
            if (goal < 0) return;
            Reverse.Clear();
            for (int i = goal; i >= 0; i = Parent[i]) Reverse.Add(Points[i]);
            var anchor = from;
            // Pull only proven chords; keep a near tangent if skipping it clips a prop.
            for (int i = Reverse.Count - 1; i >= 0;)
            {
                int next = i;
                for (int candidate = 0; candidate < i; candidate++)
                    if (_clear(anchor, Reverse[candidate])) { next = candidate; break; }
                anchor = Reverse[next];
                if (FlatDistance(_path.Count == 0 ? from : _path[_path.Count - 1], anchor) > 0.0001f)
                    _path.Add(anchor);
                i = next - 1;
            }
        }

        static float Metre(PedLink link, Vector3 p)
        {
            var direction = link.To.Pos - link.From.Pos;
            direction.y = 0f;
            return Mathf.Clamp(Vector3.Dot(p - link.From.Pos, direction.normalized), 0f, link.Length);
        }

        static float FlatDistance(Vector3 a, Vector3 b)
        { var d = a - b; d.y = 0f; return d.magnitude; }
    }
}
