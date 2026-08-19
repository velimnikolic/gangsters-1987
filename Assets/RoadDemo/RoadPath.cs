using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// What a car needs to know about the road it is on - where the lanes run for a
    /// heading, where the kerb is, whether a point is on the carriageway - and how the
    /// road's own two coordinates (along it, across it) stand in the world, so that one
    /// body of driving code works on a street whichever way it runs. A car changes
    /// streets by changing this (CrewCar.TakeCorner); the road demo's grid of
    /// RoadNode/RoadEdge answers the same questions edge by edge later, and the car
    /// controller does not change. Directions are along the street: +1 / -1.
    /// </summary>
    public interface IRoadModel
    {
        /// <summary>Lateral position (the road's own "z") of the lane centre for cars
        /// heading <paramref name="dir"/> - right-hand traffic.</summary>
        float LaneZ(float dir);

        /// <summary>Where a car of this half-width parks against the kerb on the side
        /// that traffic heading <paramref name="dir"/> keeps to.</summary>
        float KerbZ(float dir, float halfWidth);

        /// <summary>The road's centre line and half width, kerb to kerb.</summary>
        float CentreZ { get; }
        float HalfRoad { get; }

        /// <summary>The ends of the street along itself.</summary>
        float XMin { get; }
        float XMax { get; }

        /// <summary>The nearest lateral position inside the carriageway for a body of
        /// this half-width (the safety belt: whatever the steering did, in the road).</summary>
        float ClampZ(float z, float halfWidth);

        // ---------------------------------------------------------------- the frame
        //
        // A road has its own two coordinates - metres ALONG it ("x" everywhere a car
        // plans) and metres ACROSS it ("z") - and every plan is laid in those. For the
        // street that runs along world X they are the world's own, which is what they
        // always were; for one running along world Z they are not, and the pair below
        // is what keeps one body of driving code working on either.

        /// <summary>The world point <paramref name="along"/> metres up the road and
        /// <paramref name="across"/> metres across it, at world height y.</summary>
        Vector3 ToWorld(float along, float across, float y);

        /// <summary>A world point in the road's own coordinates: x along, z across,
        /// the world height kept in y.</summary>
        Vector3 ToFrame(Vector3 world);

        /// <summary>A direction (or any free vector) turned into the road's
        /// coordinates, and back out of them.</summary>
        Vector3 DirToFrame(Vector3 world);
        Vector3 DirToWorld(Vector3 frame);
    }

    /// <summary>A plain two-way street: two lanes either side of its centre line, kerbs
    /// at +/- the carriageway's half width, right-hand traffic (a car heading "up" the
    /// street keeps to the right of it). It runs along world X or along world Z, and the
    /// difference is kept entirely inside its frame - along the street is always "x" and
    /// across it always "z" to whoever is driving on it.</summary>
    public sealed class StraightStreetModel : IRoadModel
    {
        public const float LaneOffset = 2.5f;
        public const float HalfWidth = StreetKit.StreetHalf;

        readonly float _centreZ, _xMin, _xMax;
        readonly Vector3 _along, _left;

        /// <summary>A street along world X on centre line z = <paramref name="centreZ"/>:
        /// the road's coordinates are the world's (x along, z across).</summary>
        public StraightStreetModel(float centreZ, float xMin, float xMax)
            : this(Vector3.right, Vector3.forward, centreZ, xMin, xMax) { }

        StraightStreetModel(Vector3 along, Vector3 left, float centreZ, float xMin, float xMax)
        {
            _along = along;
            _left = left;
            _centreZ = centreZ;
            _xMin = xMin;
            _xMax = xMax;
        }

        /// <summary>A street along world Z on centre line x = <paramref name="centreX"/>,
        /// from zFrom to zTo. Up the street is +Z, so its left hand is -X - and the
        /// centre line sits at "z" = -centreX in the road's own coordinates, which is
        /// nobody's business but this class's.</summary>
        public static StraightStreetModel AlongZ(float centreX, float zFrom, float zTo)
            => new StraightStreetModel(Vector3.forward, Vector3.left, -centreX, zFrom, zTo);

        /// <summary>Which way, in the world, "up the street" points.</summary>
        public Vector3 Along => _along;

        public float LaneZ(float dir) => _centreZ - Mathf.Sign(dir) * LaneOffset;
        public float KerbZ(float dir, float halfWidth) =>
            _centreZ - Mathf.Sign(dir) * (HalfWidth - halfWidth + 0.38f); // flank on the stone, mirror over it
        public float CentreZ => _centreZ;
        public float HalfRoad => HalfWidth;
        public float XMin => _xMin;
        public float XMax => _xMax;
        public float ClampZ(float z, float halfWidth)
        {
            float edge = HalfWidth - halfWidth + 0.45f;
            return Mathf.Clamp(z, _centreZ - edge, _centreZ + edge);
        }

        public Vector3 ToWorld(float along, float across, float y)
        {
            var p = _along * along + _left * across;
            p.y = y;
            return p;
        }

        public Vector3 ToFrame(Vector3 world) =>
            new Vector3(Vector3.Dot(world, _along), world.y, Vector3.Dot(world, _left));

        public Vector3 DirToFrame(Vector3 world) =>
            new Vector3(Vector3.Dot(world, _along), world.y, Vector3.Dot(world, _left));

        public Vector3 DirToWorld(Vector3 frame)
        {
            var v = _along * frame.x + _left * frame.z;
            v.y = frame.y;
            return v;
        }
    }

    /// <summary>One point of a car's path: where, which way, how far along, how bent,
    /// and how fast a car may be here.</summary>
    public struct PathSample
    {
        public Vector3 P;
        public Vector3 Dir;
        public float S;
        public float Curvature; // 1/m, unsigned
        public float VMax;
    }

    /// <summary>
    /// A car's path: dense samples (half a metre apart) with a speed profile - the
    /// curvature's, the parking stop's - and, when the path carries a manoeuvre that
    /// needs a gap in the traffic (a U-turn, a lane change), the sample where that
    /// manoeuvre begins and the stretch of road it sweeps, so the car can hold short
    /// of it until the road is clear.
    /// </summary>
    public sealed class RoadPath
    {
        public readonly List<PathSample> Samples = new List<PathSample>();
        public float Length => Samples.Count > 0 ? Samples[Samples.Count - 1].S : 0f;

        /// <summary>Index of the first sample of a manoeuvre needing a gap, or -1.</summary>
        public int DecisionIndex = -1;
        /// <summary>The samples the manoeuvre sweeps (from DecisionIndex), for the gap check.</summary>
        public int ManoeuvreEnd = -1;
        /// <summary>The road is clear and the car is going: set once, never unset.</summary>
        public bool Committed;

        /// <summary>The path ends in a stop (parking) rather than a hand-over to the next.</summary>
        public bool EndsInStop;

        public PathSample this[int i] => Samples[Mathf.Clamp(i, 0, Samples.Count - 1)];

        /// <summary>The sample at or past this distance along the path.</summary>
        public int IndexAt(float s, int from = 0)
        {
            for (int i = Mathf.Max(0, from); i < Samples.Count; i++)
                if (Samples[i].S >= s) return i;
            return Samples.Count - 1;
        }
    }

    /// <summary>
    /// Lays a path out of primitives on a road model - lane runs, lane changes, the
    /// pull-in to a kerb, the U-turn inside the carriageway - as raw points, then
    /// samples them: distance along, direction, curvature (from the turn between
    /// neighbours), and the speed profile: cruise, less through a bend (v <= sqrt(aLat
    /// R)), a crawl round the U-turn, and for a path that ends in a stop, a run-down
    /// to nought at the end at the parking deceleration. Every primitive stays inside
    /// the kerbs by construction; the controller never has to rescue it.
    /// </summary>
    public sealed class PathBuilder
    {
        const float Step = 0.5f;
        readonly IRoadModel _road;
        readonly float _y;
        readonly List<Vector3> _pts = new List<Vector3>();
        readonly List<float> _caps = new List<float>();   // a speed cap per point, from Slow()
        float _cap = float.MaxValue;
        int _decisionAt = -1, _manoeuvreEnd = -1;

        public PathBuilder(IRoadModel road, float y)
        {
            _road = road;
            _y = y;
        }

        // the road's own coordinates - x along the street, z across it - put back into
        // the world, which is where a path is followed
        Vector3 At(float x, float z) =>
            _road != null ? _road.ToWorld(x, z, _y) : new Vector3(x, _y, z);

        void Add(Vector3 p)
        {
            if (_pts.Count > 0 && (p - _pts[_pts.Count - 1]).sqrMagnitude < 0.01f) return;
            _pts.Add(p);
            _caps.Add(_cap);
        }

        /// <summary>Everything laid from here on is held to this speed at most (the
        /// pull-in to a kerb, a squeeze past something) - the cruise still applies.</summary>
        public PathBuilder Slow(float vmax)
        {
            _cap = vmax;
            return this;
        }

        /// <summary>Straight along the street at lateral z, from x0 to x1.</summary>
        public PathBuilder LaneRun(float x0, float x1, float z)
        {
            float dir = Mathf.Sign(x1 - x0);
            float len = Mathf.Abs(x1 - x0);
            int n = Mathf.Max(1, Mathf.CeilToInt(len / Step));
            for (int i = 0; i <= n; i++)
                Add(At(x0 + dir * len * i / n, z));
            return this;
        }

        /// <summary>A shallow S from lateral z0 to z1 while going x0 -> x1 - a lane
        /// change, or the pull-in to the kerb; the sideways ease is a smoothstep so the
        /// car turns in and straightens out with no kink at either end.</summary>
        public PathBuilder LaneChange(float x0, float x1, float z0, float z1)
        {
            float dir = Mathf.Sign(x1 - x0);
            float len = Mathf.Abs(x1 - x0);
            int n = Mathf.Max(2, Mathf.CeilToInt(len / Step));
            for (int i = 0; i <= n; i++)
            {
                float t = i / (float)n;
                Add(At(x0 + dir * len * t, Mathf.Lerp(z0, z1, Mathf.SmoothStep(0f, 1f, t))));
            }
            return this;
        }

        /// <summary>The way a car is parked: a long shallow slant from the lane to the
        /// kerb, then a straight run along it, ending at x1.</summary>
        public PathBuilder PullIn(float xFrom, float xTo, float zLane, float zKerb)
        {
            float dir = Mathf.Sign(xTo - xFrom);
            float len = Mathf.Abs(xTo - xFrom);
            float slant = Mathf.Min(18f, len * 0.7f);
            LaneChange(xFrom, xFrom + dir * slant, zLane, zKerb);
            LaneRun(xFrom + dir * slant, xTo, zKerb);
            return this;
        }

        /// <summary>The U-turn: a half circle inside the carriageway about the centre
        /// line, from lateral z (a car heading <paramref name="dir"/>) to the mirror
        /// point across it, bulging forward, ending headed the other way. Marked as the
        /// manoeuvre the car must have a gap for.</summary>
        public PathBuilder UTurn(float x, float z, float dir)
        {
            float cz = _road.CentreZ;
            float r = Mathf.Clamp(Mathf.Abs(z - cz), 2.2f, _road.HalfRoad - 0.9f);
            float side = z >= cz ? 1f : -1f;
            int n = 14;
            _decisionAt = Mathf.Max(0, _pts.Count - 1);
            for (int i = 0; i <= n; i++)
            {
                float t = i / (float)n;
                Add(At(x + dir * r * Mathf.Sin(Mathf.PI * t), cz + side * r * Mathf.Cos(Mathf.PI * t)));
            }
            _manoeuvreEnd = _pts.Count - 1;
            return this;
        }

        /// <summary>Marks the stretch laid so far as the manoeuvre needing a gap
        /// (a lane change into traffic).</summary>
        public PathBuilder NeedsGapFrom(int firstPointIndex)
        {
            _decisionAt = Mathf.Clamp(firstPointIndex, 0, Mathf.Max(0, _pts.Count - 1));
            _manoeuvreEnd = _pts.Count - 1;
            return this;
        }

        /// <summary>A point of the path given in the world, for a stretch that is not
        /// laid in any one street's coordinates - the corner through a junction, which
        /// begins on the street the car is leaving and ends on the one it is taking.</summary>
        public PathBuilder Point(Vector3 world)
        {
            Add(new Vector3(world.x, _y, world.z));
            return this;
        }

        /// <summary>The corner through a junction: a quadratic Bezier from <paramref name="from"/>,
        /// where the car is and headed <paramref name="fromDir"/>, to <paramref name="to"/>,
        /// where it comes out on the next street - the control point being where the two
        /// lines cross, which is what makes the curve leave and arrive along them.</summary>
        public PathBuilder Corner(Vector3 from, Vector3 fromDir, Vector3 to)
        {
            fromDir.y = 0f;
            if (fromDir.sqrMagnitude < 1e-6f) return Point(to);
            fromDir.Normalize();
            var control = from + fromDir * Vector3.Dot(to - from, fromDir);
            control.y = _y;
            int n = Mathf.Max(6, Mathf.CeilToInt(Vector3.Distance(from, to) / Step));
            for (int i = 0; i <= n; i++)
            {
                float t = i / (float)n;
                float u = 1f - t;
                Point(u * u * from + 2f * u * t * control + t * t * to);
            }
            return this;
        }

        public int PointCount => _pts.Count;

        /// <summary>Samples the points: S, direction, curvature, VMax. A path that ends
        /// in a stop runs its speed down to nought at the end; nowhere does the profile
        /// ask more than the car can shed at <paramref name="brake"/> before the next sample.</summary>
        public RoadPath Build(float cruise, float aLat, bool endsInStop, float parkDecel = 2.8f, float brake = 6f)
        {
            var path = new RoadPath { EndsInStop = endsInStop };
            if (_pts.Count == 0) return path;
            if (_pts.Count == 1) { _pts.Add(_pts[0] + Vector3.right * 0.1f); _caps.Add(_caps[0]); }

            float s = 0f;
            for (int i = 0; i < _pts.Count; i++)
            {
                var prev = _pts[Mathf.Max(0, i - 1)];
                var next = _pts[Mathf.Min(_pts.Count - 1, i + 1)];
                if (i > 0) s += Vector3.Distance(_pts[i], prev);
                var dir = next - prev;
                dir.y = 0f;
                if (dir.sqrMagnitude < 1e-6f) dir = i > 0 ? path.Samples[i - 1].Dir : Vector3.right;
                dir.Normalize();
                // curvature: the turn between the two half-segments, over their length
                float curv = 0f;
                if (i > 0 && i < _pts.Count - 1)
                {
                    var a = (_pts[i] - prev); a.y = 0f;
                    var b = (next - _pts[i]); b.y = 0f;
                    float la = a.magnitude, lb = b.magnitude;
                    if (la > 1e-4f && lb > 1e-4f)
                    {
                        float ang = Vector3.Angle(a, b) * Mathf.Deg2Rad;
                        curv = ang / (0.5f * (la + lb));
                    }
                }
                float vmax = Mathf.Min(cruise, _caps[i]);
                if (curv > 1e-4f) vmax = Mathf.Min(vmax, Mathf.Sqrt(aLat / curv));
                path.Samples.Add(new PathSample { P = _pts[i], Dir = dir, S = s, Curvature = curv, VMax = vmax });
            }

            // the manoeuvre's flag
            path.DecisionIndex = _decisionAt;
            path.ManoeuvreEnd = _manoeuvreEnd;

            // smooth the profile: no sample may ask more speed than the next lets it
            // shed at the brake, nor - at the end of a parking run - than the stop allows
            float length = path.Length;
            for (int i = path.Samples.Count - 1; i >= 0; i--)
            {
                var smp = path.Samples[i];
                if (endsInStop)
                    smp.VMax = Mathf.Min(smp.VMax, Mathf.Sqrt(2f * parkDecel * Mathf.Max(0f, length - smp.S)));
                if (i < path.Samples.Count - 1)
                {
                    var nxt = path.Samples[i + 1];
                    float ds = nxt.S - smp.S;
                    smp.VMax = Mathf.Min(smp.VMax, Mathf.Sqrt(nxt.VMax * nxt.VMax + 2f * brake * ds));
                }
                path.Samples[i] = smp;
            }
            return path;
        }
    }
}
