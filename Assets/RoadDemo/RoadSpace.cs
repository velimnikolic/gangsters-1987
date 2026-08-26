using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// The one rule of the road no driver may break: two cars are never in the same
    /// metres of it. Every driver here plans ahead - the path, the following gap, the
    /// way round what is stopped - and every plan is made on a picture of the street
    /// that is a frame old, so now and then two of them want the same patch of tarmac
    /// at the same moment (a car swings out as another comes past, a turn is begun as
    /// something rolls into its sweep). This is the belt under all of it: the step a
    /// car takes is cut short before its body enters anybody else's, and a car that
    /// has ended up inside one anyway is eased back out. It never steers and never
    /// plans - it only refuses the last few centimetres - so the driving stays the
    /// drivers' (CrewCar, StreetTraffic), and nothing is ever seen to interpenetrate.
    /// </summary>
    public static class RoadSpace
    {
        /// <summary>Metres of air kept between two bodies.</summary>
        public const float Air = 0.06f;

        /// <summary>The most a wedged car is eased out of another in one frame.</summary>
        const float UnwedgeStep = 0.05f;

        const float Probe = 0.25f;   // metres between the points a step is tested at

        /// <summary>How far a body of this shape reaches ACROSS the street when it is
        /// turned this way - the corner of a car at an angle, not its half width. What
        /// the kerb has to be measured against: a car swinging out is wider than its
        /// flank, and that is how one ends up on the pavement.</summary>
        public static float LateralExtent(Vector3 fwd, float halfLength, float halfWidth)
        {
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-6f) return halfWidth;
            fwd.Normalize();
            return Mathf.Abs(fwd.z) * halfLength + Mathf.Abs(fwd.x) * halfWidth;
        }

        /// <summary>Do these two boxes on the ground share any ground - and if they do,
        /// the shortest shove that takes the first clear of the second.</summary>
        /// <summary>Two roads this far apart in height pass OVER one another and share
        /// no ground at all. Without it the belt is a plan view: a slip road running
        /// under a viaduct is in the same square metre as the deck seven metres above
        /// it, every car on each refuses to move for a car on the other, and the pair
        /// stand there for the rest of the run. It cost ten thousand refused steps in
        /// one run of the expressway demo, all of them at the two places where a ramp
        /// crosses beneath the road it came off.</summary>
        public const float Storey = 3.2f;

        public static bool Overlap(Vector3 aP, Vector3 aF, float aHL, float aHW,
                                   Vector3 bP, Vector3 bF, float bHL, float bHW,
                                   float pad, out Vector3 push)
        {
            push = Vector3.zero;
            if (Mathf.Abs(aP.y - bP.y) > Storey) return false;
            Vector2 ac = new Vector2(aP.x, aP.z), bc = new Vector2(bP.x, bP.z);
            Vector2 af = Flat(aF), ar = new Vector2(-af.y, af.x);
            Vector2 bf = Flat(bF), br = new Vector2(-bf.y, bf.x);
            float ahl = aHL + pad * 0.5f, ahw = aHW + pad * 0.5f;
            float bhl = bHL + pad * 0.5f, bhw = bHW + pad * 0.5f;

            var d = ac - bc;
            float least = float.MaxValue;
            Vector2 axis = Vector2.zero;
            for (int i = 0; i < 4; i++)
            {
                var n = i == 0 ? af : i == 1 ? ar : i == 2 ? bf : br;
                float ra = Mathf.Abs(Vector2.Dot(n, af)) * ahl + Mathf.Abs(Vector2.Dot(n, ar)) * ahw;
                float rb = Mathf.Abs(Vector2.Dot(n, bf)) * bhl + Mathf.Abs(Vector2.Dot(n, br)) * bhw;
                float sep = Vector2.Dot(d, n);
                float over = ra + rb - Mathf.Abs(sep);
                if (over <= 0f) return false;                       // daylight on this axis: clear
                if (over < least) { least = over; axis = sep >= 0f ? n : -n; }
            }
            push = new Vector3(axis.x, 0f, axis.y) * least;
            return true;
        }

        static Vector2 Flat(Vector3 v)
        {
            var f = new Vector2(v.x, v.z);
            return f.sqrMagnitude < 1e-6f ? Vector2.right : f.normalized;
        }

        // ------------------------------------------------------------ the index
        //
        // Everyone on the road, binned by where he stands, built once a frame the
        // first time anybody asks: a car's step is tested against the few bodies in
        // its own and the neighbouring bins, not the whole city's.

        const float Cell = 12f;                    // metres; a body reaches at most ~7 m from its centre
        static readonly Dictionary<long, List<IRoadUser>> _bins = new Dictionary<long, List<IRoadUser>>();
        static readonly List<List<IRoadUser>> _pool = new List<List<IRoadUser>>();
        static int _builtFrame = -1;
        static int _builtCount = -1;

        static long Key(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;

        static void Build()
        {
            var users = StreetTraffic.Users;
            if (_builtFrame == Time.frameCount && _builtCount == users.Count) return;
            _builtFrame = Time.frameCount;
            _builtCount = users.Count;
            foreach (var kv in _bins) { kv.Value.Clear(); _pool.Add(kv.Value); }
            _bins.Clear();
            for (int i = 0; i < users.Count; i++)
            {
                var u = users[i];
                var p = u.RoadPosition;
                int cx = Mathf.FloorToInt(p.x / Cell), cz = Mathf.FloorToInt(p.z / Cell);
                long k = Key(cx, cz);
                if (!_bins.TryGetValue(k, out var list))
                {
                    if (_pool.Count > 0) { list = _pool[_pool.Count - 1]; _pool.RemoveAt(_pool.Count - 1); }
                    else list = new List<IRoadUser>(8);
                    _bins[k] = list;
                }
                list.Add(u);
            }
        }

        /// <summary>The bodies near this point: its bin and the eight round it.</summary>
        static readonly List<IRoadUser> _near = new List<IRoadUser>(32);
        static List<IRoadUser> Near(Vector3 at)
        {
            Build();
            _near.Clear();
            int cx = Mathf.FloorToInt(at.x / Cell), cz = Mathf.FloorToInt(at.z / Cell);
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                    if (_bins.TryGetValue(Key(cx + dx, cz + dz), out var list)) _near.AddRange(list);
            return _near;
        }

        /// <summary>Whoever this body would be inside of, stood here turned this way.</summary>
        public static IRoadUser Inside(IRoadUser self, Vector3 at, Vector3 fwd,
                                       float halfLength, float halfWidth, out Vector3 push)
        {
            push = Vector3.zero;
            IRoadUser worst = null;
            float deepest = 0f;
            var users = Near(at);
            for (int i = 0; i < users.Count; i++)
            {
                var u = users[i];
                if (ReferenceEquals(u, self)) continue;
                if (!Overlap(at, fwd, halfLength, halfWidth,
                             u.RoadPosition, u.RoadForward, u.HalfLength, u.HalfWidth, Air, out var p)) continue;
                float depth = p.magnitude;
                if (depth <= deepest) continue;
                deepest = depth;
                worst = u;
                push = p;
            }
            return worst;
        }

        /// <summary>The point a car may actually move to this frame: its own step, cut
        /// short at the last place its body is still clear of everybody else's. A car
        /// that is already inside one is eased out instead, a few centimetres a frame.
        /// <paramref name="hit"/> is whoever stopped it, or null when the whole step
        /// was taken.</summary>
        public static Vector3 Advance(IRoadUser self, Vector3 from, Vector3 to, Vector3 fwd,
                                      float halfLength, float halfWidth, out IRoadUser hit)
        {
            // inside somebody already - a car turned into us, one spawned on us, the
            // last frame's arithmetic: out of him first, and no step at all this frame
            hit = Inside(self, from, fwd, halfLength, halfWidth, out var push);
            if (hit != null)
            {
                float d = push.magnitude;
                var eased = from + (d > UnwedgeStep ? push * (UnwedgeStep / d) : push);
                eased.y = to.y;
                return eased;
            }

            var step = to - from;
            step.y = 0f;
            float len = step.magnitude;
            if (len < 1e-5f) return to;

            int n = Mathf.Clamp(Mathf.CeilToInt(len / Probe), 1, 16);
            var free = from;
            for (int i = 1; i <= n; i++)
            {
                var at = from + step * (i / (float)n);
                var blocker = Inside(self, at, fwd, halfLength, halfWidth, out _);
                if (blocker != null)
                {
                    hit = blocker;
                    free.y = to.y;
                    return free;
                }
                free = at;
            }
            return to;
        }
    }
}
