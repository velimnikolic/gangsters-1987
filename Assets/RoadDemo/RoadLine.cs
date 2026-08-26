using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    // A road that BENDS.
    //
    // Every carriageway in this city used to be a straight run from A to B with one
    // axis and one right - which is all a street ever is, and is why the freeways
    // built on it came out the shape they did: a ring with four square corners, ramps
    // that were straight boards leaned against a deck, and a "curve" that could only
    // ever be a chain of straights with a JUNCTION at every kink, because a junction
    // is the only thing the graph had that could join two carriageways.
    //
    // A RoadLine is the frame a bending road needs: a polyline with its arc length,
    // a smooth tangent at every point, and the three questions a driver asks of the
    // road under him - where is s, which way does it run there, how tight is it - all
    // answered at any s along it. The carriageway keeps its (s, d): s is metres along
    // the path, d is metres to the right of it, and everything else in the driving
    // (the occupants' bands, the following gaps, the claims) is unchanged.
    //
    // The point at s is a cubic Hermite through the samples on their tangents, and the
    // heading is that curve's own derivative - the same arithmetic a junction connector
    // uses. Sampling a chord and reading the tangent off the neighbours instead would
    // put the body a few degrees across the line it is on: a car crabbing down a bend.
    public sealed class RoadLine
    {
        public readonly Vector3[] Pts;      // y = 0
        public readonly float[] Cum;        // arc length at each point
        public readonly Vector3[] Tan;      // unit tangent at each point
        readonly float[] _radius;           // the turn radius at each point (MaxValue: straight)
        readonly Vector3 _lo, _hi;          // the box the whole path lies in

        public float Length => Cum[Cum.Length - 1];
        public Vector3 Start => Pts[0];
        public Vector3 End => Pts[Pts.Length - 1];
        public Vector3 StartDir => Tan[0];
        public Vector3 EndDir => Tan[Tan.Length - 1];

        RoadLine(Vector3[] pts, Vector3? startDir = null, Vector3? endDir = null)
        {
            Pts = pts;
            int n = pts.Length;
            Cum = new float[n];
            Tan = new Vector3[n];
            _radius = new float[n];
            var lo = pts[0]; var hi = pts[0];
            for (int i = 1; i < n; i++)
            {
                Cum[i] = Cum[i - 1] + Vector3.Distance(pts[i - 1], pts[i]);
                lo = Vector3.Min(lo, pts[i]);
                hi = Vector3.Max(hi, pts[i]);
            }
            _lo = lo; _hi = hi;
            for (int i = 0; i < n; i++)
            {
                Vector3 d;
                if (n == 1) d = Vector3.forward;
                // The ends are TOLD, where the caller knows them. A stretch cut out of a
                // longer road (Sub) reads its own first tangent off its first chord, and
                // a chord is not the tangent: the piece leaves its parent's line by a
                // degree or two, and at every seam of a motorway - which is a place two
                // such pieces meet - the car crossing it is handed a road pointing
                // somewhere slightly else. That is a kink, and a kink at 23 m/s is a
                // jump in the trace and a wheel wound over in the black box.
                else if (i == 0) d = startDir ?? (pts[1] - pts[0]);
                else if (i == n - 1) d = endDir ?? (pts[n - 1] - pts[n - 2]);
                else d = pts[i + 1] - pts[i - 1];
                d.y = 0f;
                Tan[i] = d.sqrMagnitude > 1e-8f ? d.normalized : (i > 0 ? Tan[i - 1] : Vector3.forward);
            }
            for (int i = 0; i < n; i++)
            {
                if (i == 0 || i == n - 1) { _radius[i] = float.MaxValue; continue; }
                var a = pts[i] - pts[i - 1];
                var b = pts[i + 1] - pts[i];
                a.y = b.y = 0f;
                float la = a.magnitude, lb = b.magnitude;
                if (la < 1e-4f || lb < 1e-4f) { _radius[i] = float.MaxValue; continue; }
                float turn = Vector3.Angle(a, b) * Mathf.Deg2Rad;     // radians turned over (la+lb)/2
                _radius[i] = turn < 1e-4f ? float.MaxValue : (la + lb) * 0.5f / turn;
            }
        }

        // ------------------------------------------------------------------ making

        /// <summary>A path through these points (duplicates dropped). Two points is a
        /// straight, which is what nearly every street is.</summary>
        public static RoadLine Through(IList<Vector3> points, Vector3? startDir = null, Vector3? endDir = null)
        {
            var pts = new List<Vector3>(points.Count);
            foreach (var p in points)
            {
                var q = new Vector3(p.x, 0f, p.z);
                if (pts.Count > 0 && (pts[pts.Count - 1] - q).sqrMagnitude < 1e-4f) continue;
                pts.Add(q);
            }
            if (pts.Count == 0) pts.Add(Vector3.zero);
            if (pts.Count == 1) pts.Add(pts[0] + Vector3.forward);
            return new RoadLine(pts.ToArray(), startDir, endDir);
        }

        public static RoadLine Straight(Vector3 a, Vector3 b) => Through(new[] { a, b });

        /// <summary>The stretch between two stations, ends included.</summary>
        public RoadLine Sub(float s0, float s1)
        {
            s0 = Mathf.Clamp(s0, 0f, Length);
            s1 = Mathf.Clamp(s1, 0f, Length);
            if (s1 < s0) { var t = s0; s0 = s1; s1 = t; }
            var pts = new List<Vector3> { PointAt(s0) };
            for (int i = 0; i < Pts.Length; i++)
                if (Cum[i] > s0 + 0.01f && Cum[i] < s1 - 0.01f) pts.Add(Pts[i]);
            pts.Add(PointAt(s1));
            // and the parent's own headings at the cut, so the piece leaves and arrives
            // pointing exactly where the road it was cut from does
            return Through(pts, DirAt(s0), DirAt(s1));
        }

        /// <summary>The same road shifted sideways: the line d metres to its right.
        /// A parallel curve, which is what a second carriageway of a dual is.</summary>
        public RoadLine Offset(float d)
        {
            var pts = new Vector3[Pts.Length];
            for (int i = 0; i < Pts.Length; i++)
                pts[i] = Pts[i] + new Vector3(Tan[i].z, 0f, -Tan[i].x) * d;
            // a parallel curve runs the same way its parent does, everywhere
            return Through(pts, StartDir, EndDir);
        }

        /// <summary>The same road the other way round: what the second deck of a dual
        /// carriageway runs down.</summary>
        public RoadLine Reversed()
        {
            var pts = new Vector3[Pts.Length];
            for (int i = 0; i < Pts.Length; i++) pts[i] = Pts[Pts.Length - 1 - i];
            return Through(pts, -EndDir, -StartDir);
        }

        /// <summary>Points along a circular arc: centre, radius, and the two angles
        /// (radians, x = cos, z = sin), sampled fine enough that the chord never sags
        /// more than a hand off the true curve.</summary>
        public static void Arc(List<Vector3> into, Vector3 centre, float radius, float from, float to, float y = 0f)
        {
            float sweep = to - from;
            float step = Mathf.Max(0.02f, Mathf.Sqrt(8f * 0.05f / Mathf.Max(1f, radius)));  // sagitta 5 cm
            int n = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(sweep) / step));
            n = Mathf.Min(n, 400);
            for (int i = 0; i <= n; i++)
            {
                float a = from + sweep * i / n;
                var p = new Vector3(centre.x + radius * Mathf.Cos(a), y, centre.z + radius * Mathf.Sin(a));
                if (into.Count > 0 && (into[into.Count - 1] - p).sqrMagnitude < 1e-4f) continue;
                into.Add(p);
            }
        }

        /// <summary>A corner: in along <paramref name="dirIn"/> to the corner point, an
        /// arc of this radius, out along <paramref name="dirOut"/>. The tangent points
        /// stand R·tan(θ/2) back from the corner - so the caller wanting to know where
        /// its road really begins asks <see cref="CornerTangent"/> first.</summary>
        public static void Corner(List<Vector3> into, Vector3 corner, Vector3 dirIn, Vector3 dirOut, float radius)
        {
            dirIn.y = dirOut.y = 0f;
            dirIn.Normalize(); dirOut.Normalize();
            float turn = Vector3.SignedAngle(dirIn, dirOut, Vector3.up) * Mathf.Deg2Rad;
            if (Mathf.Abs(turn) < 1e-3f) { into.Add(corner); return; }
            float back = radius * Mathf.Tan(Mathf.Abs(turn) * 0.5f);
            var t0 = corner - dirIn * back;
            // the centre stands off the tangent point, on the inside of the turn
            var inward = turn > 0f ? new Vector3(dirIn.z, 0f, -dirIn.x) : new Vector3(-dirIn.z, 0f, dirIn.x);
            var centre = t0 + inward * radius;
            float a0 = Mathf.Atan2(t0.z - centre.z, t0.x - centre.x);
            // the arc is swept the OTHER way round from the yaw: a left turn in the
            // world (a falling yaw) runs anticlockwise through atan2's angles
            float a1 = a0 - turn;
            if (into.Count == 0 || (into[into.Count - 1] - t0).sqrMagnitude > 1e-4f) into.Add(t0);
            Arc(into, centre, radius, a0, a1, corner.y);
        }

        // ------------------------------------------------------------------ asking

        void Locate(float s, out int i, out float t, out float seg)
        {
            int n = Pts.Length;
            s = Mathf.Clamp(s, 0f, Length);
            i = 1;
            while (i < n - 1 && Cum[i] < s) i++;
            seg = Cum[i] - Cum[i - 1];
            t = seg > 1e-5f ? (s - Cum[i - 1]) / seg : 0f;
        }

        /// <summary>Where s is, and which way the road runs there.</summary>
        public void Pose(float s, out Vector3 pos, out Vector3 dir)
        {
            if (Pts.Length < 2) { pos = Pts[0]; dir = Vector3.forward; return; }
            // off either end: the road carries straight on, so a car overshooting a
            // node still has a frame under it
            if (s < 0f) { pos = Pts[0] + Tan[0] * s; dir = Tan[0]; return; }
            if (s > Length) { pos = Pts[Pts.Length - 1] + Tan[Tan.Length - 1] * (s - Length); dir = Tan[Tan.Length - 1]; return; }
            Locate(s, out int i, out float t, out float seg);
            var p0 = Pts[i - 1]; var p1 = Pts[i];
            var m0 = Tan[i - 1] * seg; var m1 = Tan[i] * seg;
            float t2 = t * t, t3 = t2 * t;
            pos = p0 * (2f * t3 - 3f * t2 + 1f) + m0 * (t3 - 2f * t2 + t) + p1 * (-2f * t3 + 3f * t2) + m1 * (t3 - t2);
            var d = p0 * (6f * t2 - 6f * t) + m0 * (3f * t2 - 4f * t + 1f) + p1 * (-6f * t2 + 6f * t) + m1 * (3f * t2 - 2f * t);
            d.y = 0f;
            dir = d.sqrMagnitude > 1e-8f ? d.normalized : Tan[i];
        }

        public Vector3 PointAt(float s) { Pose(s, out var p, out _); return p; }
        public Vector3 DirAt(float s) { Pose(s, out _, out var d); return d; }
        public Vector3 RightAt(float s) { var d = DirAt(s); return new Vector3(d.z, 0f, -d.x); }

        /// <summary>A point in the road's own frame: s along, d to the right.</summary>
        public Vector3 Pose(float s, float d)
        {
            Pose(s, out var p, out var dir);
            return p + new Vector3(dir.z, 0f, -dir.x) * d;
        }

        /// <summary>The turn radius under s - what a driver reads to know how fast the
        /// bend may be taken. A straight answers float.MaxValue.</summary>
        public float RadiusAt(float s)
        {
            if (Pts.Length < 3) return float.MaxValue;
            Locate(s, out int i, out float t, out _);
            float a = _radius[Mathf.Max(0, i - 1)], b = _radius[i];
            return t < 0.5f ? Mathf.Min(a, b) : Mathf.Min(b, _radius[Mathf.Min(_radius.Length - 1, i + 1)]);
        }

        /// <summary>Where a point falls on the road: s along it and d across. Walks the
        /// segments - the path knows its own box, so a road nowhere near the point costs
        /// one comparison.</summary>
        public void Project(Vector3 p, out float s, out float d)
        {
            p.y = 0f;
            s = 0f; d = float.MaxValue;
            float best = float.MaxValue;
            for (int i = 1; i < Pts.Length; i++)
            {
                var a = Pts[i - 1]; var b = Pts[i];
                var ab = b - a;
                float len2 = ab.sqrMagnitude;
                float t = len2 > 1e-6f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2) : 0f;
                var q = a + ab * t;
                float dist = (q - p).sqrMagnitude;
                if (dist >= best) continue;
                best = dist;
                s = Cum[i - 1] + t * (Cum[i] - Cum[i - 1]);
                var right = new Vector3(ab.z, 0f, -ab.x).normalized;
                d = Vector3.Dot(p - q, right);
            }
        }

        /// <summary>Is this point anywhere near the road at all (its box, grown by
        /// <paramref name="slack"/>)? The cheap half of Project.</summary>
        public bool Near(Vector3 p, float slack)
            => p.x >= _lo.x - slack && p.x <= _hi.x + slack && p.z >= _lo.z - slack && p.z <= _hi.z + slack;
    }
}
