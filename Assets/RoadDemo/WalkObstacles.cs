using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>What a man on foot walks round when he is OFF the sidewalk graph:
    /// a crew's free stride over the demo floor, the dash across the road to a
    /// fight or away from one, the walk to the car door, the police coming up
    /// from their cruiser. The graph's walkers are kept off the furniture by the
    /// clearance sampled into their stretches (PedLink.Free); off the graph there
    /// is no stretch, so the ground is asked directly, and the answer is made of
    /// three things:
    ///
    ///   the pavement plans   - every prop a scene laid, as its measured footprint
    ///                          (the same SidewalkPlan the dressing wrote into);
    ///                          a scene hands its plan over once at build
    ///   the solids           - buildings, mostly: whatever a scene blocks off by
    ///                          hand, as a box on the ground
    ///   the road             - everyone on it this frame (StreetTraffic.Users:
    ///                          the traffic, the outfit's cars, the police), each
    ///                          the box he stands in, read live
    ///
    /// Kinematic like everything else here - boxes and circles, no physics, no
    /// colliders. A walker asks two things of it: may I stand here (Occupied), and
    /// which way may I go (Steer): the line he wants if it is clear, else the
    /// nearest line off it that is, to either side, and how far it runs.</summary>
    public static class WalkObstacles
    {
        /// <summary>Half a man, shoulder to shoulder - what must clear a thing.</summary>
        public const float Radius = SidewalkDressing.WalkRadius;

        /// <summary>The pavement plans in the scene: each prop's footprint, as laid.</summary>
        public static readonly List<SidewalkPlan> Props = new List<SidewalkPlan>();

        // the scene's own solids, kept in a plan of their own so they bucket the same way
        static SidewalkPlan _solids = new SidewalkPlan();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Forget()
        {
            Props.Clear();
            _solids = new SidewalkPlan();
            Near.Clear();
        }

        // ------------------------------------------------------------------ the solids

        /// <summary>Block off this ground - a building's footprint, world axes.</summary>
        public static void Block(float xMin, float xMax, float zMin, float zMax)
        {
            if (xMax <= xMin || zMax <= zMin) return;
            _solids.Take(SidewalkPlan.Make(
                new Vector2((xMin + xMax) * 0.5f, (zMin + zMax) * 0.5f), 0f,
                new Vector2((xMax - xMin) * 0.5f, (zMax - zMin) * 0.5f), solid: true));
        }

        /// <summary>Block off the ground under these world bounds.</summary>
        public static void Block(Bounds b) => Block(b.min.x, b.max.x, b.min.z, b.max.z);

        /// <summary>Block off an oriented box: centre, yaw in degrees, half extents
        /// in its own frame.</summary>
        public static void Block(Vector3 centre, float yaw, Vector2 half)
            => _solids.Take(SidewalkPlan.Make(new Vector2(centre.x, centre.z), yaw, half, solid: true));

        // ------------------------------------------------------------------ the road

        // A road user as the box he stands in, flat. Gathered once per query for
        // the few near enough to matter, so a city's worth of traffic is walked
        // through once and not once per probe.
        struct Box
        {
            public Vector2 C, F, R;
            public float HL, HW;
        }

        static readonly List<Box> Near = new List<Box>();

        static void GatherRoad(Vector2 around, float reach)
        {
            Near.Clear();
            var users = StreetTraffic.Users;
            for (int i = 0; i < users.Count; i++)
            {
                var u = users[i];
                var p = u.RoadPosition;
                var c = new Vector2(p.x, p.z);
                float span = reach + Mathf.Max(u.HalfLength, u.HalfWidth);
                if ((c - around).sqrMagnitude > span * span) continue;
                var f = u.RoadForward;
                f.y = 0f;
                var fwd = f.sqrMagnitude > 1e-4f ? new Vector2(f.x, f.z).normalized : Vector2.right;
                Near.Add(new Box
                {
                    C = c, F = fwd, R = new Vector2(fwd.y, -fwd.x),
                    HL = u.HalfLength, HW = u.HalfWidth,
                });
            }
        }

        static bool InRoad(Vector2 q, float radius)
        {
            for (int i = 0; i < Near.Count; i++)
            {
                var b = Near[i];
                var d = q - b.C;
                float ox = Mathf.Max(0f, Mathf.Abs(Vector2.Dot(d, b.F)) - b.HL);
                float oz = Mathf.Max(0f, Mathf.Abs(Vector2.Dot(d, b.R)) - b.HW);
                if (ox * ox + oz * oz <= radius * radius) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ asking

        static bool OnGround(Vector2 q, float radius)
        {
            if (_solids.Occupied(q, radius)) return true;
            for (int i = 0; i < Props.Count; i++)
                if (Props[i].Occupied(q, radius)) return true;
            return false;
        }

        /// <summary>Would a man of this radius stood here be inside something -
        /// furniture, a wall, a car?</summary>
        public static bool Occupied(Vector3 p, float radius)
        {
            var q = new Vector2(p.x, p.z);
            GatherRoad(q, radius);
            return OnGround(q, radius) || InRoad(q, radius);
        }

        /// <summary>How far a man of this radius can walk from <paramref name="from"/>
        /// along <paramref name="dir"/> before he is into something, up to
        /// <paramref name="ahead"/> metres.</summary>
        public static float Clear(Vector3 from, Vector3 dir, float radius, float ahead)
        {
            var p = new Vector2(from.x, from.z);
            var h = new Vector2(dir.x, dir.z);
            if (h.sqrMagnitude < 1e-6f) return 0f;
            h.Normalize();
            GatherRoad(p, ahead + radius + 0.5f);
            return Run(p, h, radius, ahead);
        }

        // the probe's pitch: circles of the walker's radius a third of a metre
        // apart overlap, so a lamp post between two of them is still seen
        const float Step = 0.35f;

        // Metres along h from p that are free, to ahead. Refines the last pitch
        // by halves so a man stopped short of a car stops AT it, not a stride off.
        static float Run(Vector2 p, Vector2 h, float radius, float ahead)
        {
            float u = Step;
            for (; u < ahead; u += Step)
                if (OnGround(p + h * u, radius) || InRoad(p + h * u, radius))
                    return Refine(p, h, radius, u - Step, u);
            if (OnGround(p + h * ahead, radius) || InRoad(p + h * ahead, radius))
                return Refine(p, h, radius, Mathf.Max(0f, ahead - Step), ahead);
            return ahead;
        }

        static float Refine(Vector2 p, Vector2 h, float radius, float free, float hit)
        {
            for (int i = 0; i < 3; i++)
            {
                float mid = (free + hit) * 0.5f;
                if (OnGround(p + h * mid, radius) || InRoad(p + h * mid, radius)) hit = mid;
                else free = mid;
            }
            return free;
        }

        // The lines tried off the wanted one, nearest first: out to the beam, and
        // the two behind it for a man boxed in at the front.
        static readonly float[] Angles = { 11f, 22f, 34f, 46f, 60f, 75f, 90f, 110f, 135f, 160f };

        /// <summary>The heading a man at <paramref name="from"/> who wants to go
        /// <paramref name="want"/> (flat) can actually take, looked
        /// <paramref name="ahead"/> metres down: the wanted line if it runs clear
        /// that far; else the nearest line off it that does, with two rules about
        /// which. He keeps his side: <paramref name="side"/> is his to hold between
        /// frames, and a man already going round something to his right tries every
        /// line to his right before the first one to his left, so he does not change
        /// his mind at every bin and dither in front of a car, and follows a long
        /// wall to its end instead of turning back half way. And he does not turn
        /// round if he can help it: a line that runs back against the way he is
        /// going (<paramref name="going"/>, zero when he is not yet) is taken only
        /// when nothing else is clear - so a man passing one car who finds another
        /// ahead goes round it the open way, not back the way he came. Boxed in,
        /// whichever line runs furthest. <paramref name="clear"/> is how far the
        /// line given runs before it hits something: the most he may step this
        /// frame. A man stood inside something already (dealt there, shoved there)
        /// is let walk straight out of it.</summary>
        public static Vector3 Steer(Vector3 from, Vector3 want, Vector3 going, float radius, float ahead,
            ref int side, out float clear)
        {
            var p = new Vector2(from.x, from.z);
            var w = new Vector2(want.x, want.z);
            if (w.sqrMagnitude < 1e-6f) { clear = 0f; return want; }
            w.Normalize();
            GatherRoad(p, ahead + radius + 0.5f);

            // inside something: out of it first, the way he was going
            if (OnGround(p, 0.1f) || InRoad(p, 0.1f))
            {
                clear = ahead;
                return new Vector3(w.x, 0f, w.y);
            }

            // the line he wants
            float straight = Run(p, w, radius, ahead);
            if (straight >= ahead - 1e-3f)
            {
                side = 0;
                clear = straight;
                return new Vector3(w.x, 0f, w.y);
            }

            var g = new Vector2(going.x, going.z);
            bool hasWay = g.sqrMagnitude > 1e-4f;
            if (hasWay) g.Normalize();

            Vector2 best = w;
            float bestClear = straight;
            int bestSide = 0;

            // two passes: lines that carry on roughly the way he is going, then any
            for (int pass = 0; pass < 2; pass++)
            {
                bool carryOn = pass == 0 && hasWay;
                if (side != 0)
                {
                    // committed: the whole of his side, nearest first, then the other
                    for (int s = 0; s < 2; s++)
                    {
                        int sign = s == 0 ? side : -side;
                        for (int i = 0; i < Angles.Length; i++)
                            if (Try(Angles[i], sign, carryOn)) goto Found;
                    }
                }
                else
                {
                    // no side yet: the nearer line either way, right first
                    for (int i = 0; i < Angles.Length; i++)
                        if (Try(Angles[i], 1, carryOn) || Try(Angles[i], -1, carryOn)) goto Found;
                }
                // nothing ahead runs clear to the horizon, but a good step of it does:
                // he takes that before he turns round - through the gap between two
                // cars, which no line at these angles threads end to end, he goes a
                // car's width at a time
                if (carryOn && bestClear >= Mathf.Min(1f, ahead * 0.5f)) break;
            }

            // nothing runs clear: the line that runs furthest; the side he was on
            // stays his unless a side did better
            if (bestSide != 0) side = bestSide;
            clear = Mathf.Max(0f, bestClear);
            return new Vector3(best.x, 0f, best.y);

            Found:
            side = bestSide;
            clear = bestClear;
            return new Vector3(best.x, 0f, best.y);

            // one line: true when it runs clear to the horizon (taken at once);
            // otherwise remembered if it runs further than anything yet. With
            // carryOn, a line turning back against the way he is going is skipped.
            bool Try(float a, int sign, bool carryOn)
            {
                var line = Turn(w, a * sign);
                if (carryOn && Vector2.Dot(line, g) < -0.25f) return false;
                float c = Run(p, line, radius, ahead);
                if (c >= ahead - 1e-3f) { best = line; bestClear = c; bestSide = sign; return true; }
                if (c > bestClear) { bestClear = c; best = line; bestSide = sign; }
                return false;
            }
        }

        // a flat heading turned by deg (positive = to its right, as a man turns)
        static Vector2 Turn(Vector2 v, float deg)
        {
            float r = deg * Mathf.Deg2Rad;
            float c = Mathf.Cos(r), s = Mathf.Sin(r);
            return new Vector2(v.x * c + v.y * s, -v.x * s + v.y * c);
        }
    }
}
