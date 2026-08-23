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

        /// <summary>The corners of everything blocked off so far - the ground a man on
        /// foot has to find his way across. Empty until something is blocked, which is
        /// why Max is below Min to start with.</summary>
        public static Vector2 Min = new Vector2(float.MaxValue, float.MaxValue);
        public static Vector2 Max = new Vector2(float.MinValue, float.MinValue);

        /// <summary>Bumped every time the ground changes, so anything that reads the
        /// map (WalkRoute) can tell its own copy is out of date without comparing it.
        /// </summary>
        public static int Version;

        /// <summary>The ground that is the CITY, rectangle by rectangle: the street
        /// grid and every quarter hung off it. This is a fence, not an obstacle - the
        /// walls above stop a man walking THROUGH something, and this stops him walking
        /// OUT of the town altogether, off into the wilderness and down to the sea,
        /// which past the last road is all there is. Only whoever chooses where to go
        /// asks it; the crowd never leaves the graph, so the graph already answers for
        /// them. Empty means no fence was ever set - a lab scene with one block in it -
        /// and then everywhere is in, which is the only sensible reading of "nobody said
        /// where the town ends".</summary>
        public static readonly List<Rect> City = new List<Rect>();

        /// <summary>Is this ground the city's at all?</summary>
        public static bool InCity(Vector3 p)
        {
            if (City.Count == 0) return true;
            for (int i = 0; i < City.Count; i++)
            {
                var r = City[i];
                if (p.x >= r.xMin && p.x <= r.xMax && p.z >= r.yMin && p.z <= r.yMax) return true;
            }
            return false;
        }

        /// <summary>The nearest ground that IS the city's - a point ordered out in the
        /// wilderness (a click past the last street, a spot reckoned off the hem) is
        /// pulled back to the fence, half a metre inside it so the man stood there is
        /// stood ON the floor and not balanced on its edge.</summary>
        public static Vector3 ClampToCity(Vector3 p)
        {
            if (City.Count == 0 || InCity(p)) return p;
            var best = p;
            float bestD = float.MaxValue;
            for (int i = 0; i < City.Count; i++)
            {
                var r = City[i];
                var q = new Vector3(
                    Mathf.Clamp(p.x, r.xMin + 0.5f, r.xMax - 0.5f), p.y,
                    Mathf.Clamp(p.z, r.yMin + 0.5f, r.yMax - 0.5f));
                float d = (q.x - p.x) * (q.x - p.x) + (q.z - p.z) * (q.z - p.z);
                if (d < bestD) { bestD = d; best = q; }
            }
            return best;
        }

        static void Grew(float xMin, float xMax, float zMin, float zMax)
        {
            Min.x = Mathf.Min(Min.x, xMin); Min.y = Mathf.Min(Min.y, zMin);
            Max.x = Mathf.Max(Max.x, xMax); Max.y = Mathf.Max(Max.y, zMax);
            Version++;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Forget()
        {
            Props.Clear();
            _solids = new SidewalkPlan();
            Near.Clear();
            City.Clear();
            Min = new Vector2(float.MaxValue, float.MaxValue);
            Max = new Vector2(float.MinValue, float.MinValue);
            Version++;
        }

        // ------------------------------------------------------------------ the solids

        /// <summary>Block off this ground - a building's footprint, world axes.</summary>
        public static void Block(float xMin, float xMax, float zMin, float zMax)
        {
            if (xMax <= xMin || zMax <= zMin) return;
            Grew(xMin, xMax, zMin, zMax);
            _solids.Take(SidewalkPlan.Make(
                new Vector2((xMin + xMax) * 0.5f, (zMin + zMax) * 0.5f), 0f,
                new Vector2((xMax - xMin) * 0.5f, (zMax - zMin) * 0.5f), solid: true));
        }

        /// <summary>Block off the ground under these world bounds.</summary>
        public static void Block(Bounds b) => Block(b.min.x, b.max.x, b.min.z, b.max.z);

        /// <summary>Block off an oriented box: centre, yaw in degrees, half extents
        /// in its own frame.</summary>
        public static void Block(Vector3 centre, float yaw, Vector2 half)
        {
            float reach = half.magnitude;
            Grew(centre.x - reach, centre.x + reach, centre.z - reach, centre.z + reach);
            _solids.Take(SidewalkPlan.Make(new Vector2(centre.x, centre.z), yaw, half, solid: true));
        }

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

        static bool OnGround(Vector2 q, float radius) => OnGround(q, radius, 0f);

        static bool OnGround(Vector2 q, float radius, float tallBerth)
        {
            if (_solids.Occupied(q, radius, tallBerth)) return true;
            for (int i = 0; i < Props.Count; i++)
                if (Props[i].Occupied(q, radius, tallBerth)) return true;
            return false;
        }

        /// <summary>Would a man of this radius stood here be inside something that does
        /// not move - a wall, a lot, a bin? The traffic is left out on purpose: this is
        /// the map a way across the city is worked out on, and a way that went round
        /// wherever the cars happened to be standing when it was drawn would be a way
        /// round nothing a moment later. The cars are what the walking itself steers
        /// past, frame by frame (Steer).</summary>
        public static bool Standing(Vector3 p, float radius) =>
            OnGround(new Vector2(p.x, p.z), radius);

        /// <summary>Is there a WALL here - a building face, a lot's edge - as opposed
        /// to a piece of furniture? The blocks laid by the builder, and nothing out of
        /// any sidewalk plan.
        ///
        /// This exists for the one thing that cares about the difference: a man
        /// putting his BACK against something. A lean is authored against a flat
        /// vertical face at a fixed distance, and played with a bin or a car boot
        /// behind him instead the pose has nothing to rest on - he reads as a man
        /// sagging backwards into a squat. Walking round a bin and leaning on a bin
        /// are not the same question, so they do not share an answer.</summary>
        public static bool WallAt(Vector3 p, float radius) =>
            _solids.Occupied(new Vector2(p.x, p.z), radius, 0f);

        /// <summary>Would a man of this radius stood here be inside something -
        /// furniture, a wall, a car?</summary>
        public static bool Occupied(Vector3 p, float radius) => Occupied(p, radius, 0f);

        /// <summary>The same, giving the TALL props the berth their canopies want.
        /// Anything that CHOOSES a spot for a man to stand at - where he is dealt in,
        /// where he is sent to get behind something - asks with
        /// <see cref="CanopyBerth"/>; anything asking whether he can walk THROUGH a
        /// point asks without, because walking under a palm is free and always was.
        /// A trunk's box is the trunk, so without the berth a man can be stood a
        /// hand's width from the bark with two metres of fronds over his head, and
        /// what the player sees is a man who has been posted inside a tree.</summary>
        public static bool Occupied(Vector3 p, float radius, float tallBerth)
        {
            var q = new Vector2(p.x, p.z);
            GatherRoad(q, radius);
            return OnGround(q, radius, tallBerth) || InRoad(q, radius);
        }

        /// <summary>How wide a berth a man being STOOD somewhere gives a tall prop,
        /// over and above his own shoulders. A palm's footprint is its trunk, because
        /// that is all there is of it where a walker's shoulders are - but the fronds
        /// come down to head height a metre and a half out, and a man dealt under them
        /// is a man standing in a tree as far as anybody watching is concerned. Walking
        /// past one is still free: this is asked when a spot is CHOSEN, not stepped
        /// through.</summary>
        public const float CanopyBerth = 1.2f;

        /// <summary>The nearest ground to <paramref name="wanted"/> that a man of this
        /// radius can actually be left standing on: the point itself when it is clear,
        /// else the closest clear one within <paramref name="reach"/>, else the point
        /// given back unchanged because there is nothing better to offer.
        ///
        /// This exists because a spot is not a step. A walker who is dealt inside a
        /// bin walks out of it (Steer lets a man inside something leave it), but a man
        /// dealt inside one and told to STAND there stands there, shoulders in the
        /// tin, for as long as the scene lasts - and the tall props are worse, because
        /// their box is a trunk and their canopy is not, so he is not even blocked, he
        /// merely looks planted. Every crew, mob and squad in every scene is stood up
        /// through DemoCrews, so this is asked once, there.
        ///
        /// The rings are walked nearest first and the headings are staggered from ring
        /// to ring so a man pushed off a kerb by one prop does not land on the next
        /// one out along the same line. NOTHING HERE DRAWS A RANDOM NUMBER: the arena
        /// shares one stream and a spawn-time draw would relay every seed in the lab.
        /// A spot in the carriageway is taken only when the pavement offers none -
        /// men stood in a live lane are their own kind of stupid scene.</summary>
        public static Vector3 FreeSpot(Vector3 wanted, float radius, float reach = 4f)
        {
            int wantedGrade = Grade(wanted, radius);
            if (wantedGrade == 0) return wanted;

            // Nearest first, and of two spots at the same distance the better-graded
            // one - but a grade is only settled for by finishing the search, so each
            // grade keeps its first (nearest) find and grade 0 returns at once. The
            // spot asked for is entered as its own grade's first find, so a man who is
            // merely under a canopy is not walked across the pavement to another spot
            // just as good: he is moved for a better one or not at all.
            var found = new Vector3[3];
            var have = new bool[3];
            if (wantedGrade > 0) { found[wantedGrade] = wanted; have[wantedGrade] = true; }
            const int Headings = 12;
            for (float r = 0.5f; r <= reach + 1e-3f; r += 0.5f)
                for (int i = 0; i < Headings; i++)
                {
                    float a = (i * (360f / Headings) + r * 23f) * Mathf.Deg2Rad;
                    var p = wanted + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                    int grade = Grade(p, radius);
                    if (grade == 0) return p;
                    if (grade < 0) continue;
                    if (!have[grade]) { found[grade] = p; have[grade] = true; }
                }
            for (int grade = 1; grade < 3; grade++)
                if (have[grade]) return found[grade];
            return wanted;
        }

        // How good a spot is to be left standing on, best first:
        //   0  clear pavement, and clear of the canopies
        //   1  clear pavement, under a canopy - he is not blocked, he only looks it,
        //      and looking planted beats standing in a live lane
        //   2  clear ground, but in the carriageway
        //  -1  no good at all: inside furniture, a wall, or a car
        // The order matters and is not obvious. Pushing a man off a kerb because a
        // palm's fronds reach over it would swap a cosmetic fault for the one the
        // player already complained about - enemies out in the road, jamming the
        // traffic. The berth is a preference; the pavement is not.
        static int Grade(Vector3 p, float radius)
        {
            // off the floor altogether is no spot at all - nobody is ever CHOSEN a
            // place to stand out in the wilderness, whatever asked
            if (!InCity(p)) return -1;
            var q = new Vector2(p.x, p.z);
            GatherRoad(q, radius);
            if (OnGround(q, radius, 0f) || InRoad(q, radius)) return -1;
            if (InCarriageway(p)) return 2;
            return OnGround(q, radius, CanopyBerth) ? 1 : 0;
        }

        static bool InCarriageway(Vector3 p)
        {
            var net = LaneNet.Active;
            if (net == null) return false;
            var road = net.Locate(p, out _, out float d, 6f);
            return road != null && Mathf.Abs(d) < road.HalfRoad;
        }

        /// <summary>The solid furniture within reach of a point - the PROPS only. The
        /// walls are left out on purpose (a building face is not something a man gets
        /// behind and shoots over), and so is the traffic, which whoever wants a car's
        /// flank asks StreetTraffic for itself. What DemoCrews.CoverNear offers a
        /// pressed man beyond the parked cars.</summary>
        public static void PropsNear(Vector3 p, float reach, List<SidewalkPlan.Box> into)
        {
            into.Clear();
            var q = new Vector2(p.x, p.z);
            for (int i = 0; i < Props.Count; i++) Props[i].SolidNear(q, reach, into);
        }

        /// <summary>The same run as <see cref="Clear"/>, but past the FIXED things only -
        /// no traffic. What a way across the city is drawn against (WalkRoute): a line
        /// that dodged wherever the cars happened to be standing would be a line round
        /// nothing a moment later.</summary>
        public static float ClearStanding(Vector3 from, Vector3 dir, float radius, float ahead)
        {
            var p = new Vector2(from.x, from.z);
            var h = new Vector2(dir.x, dir.z);
            if (h.sqrMagnitude < 1e-6f) return 0f;
            h.Normalize();
            Near.Clear();          // the road is nobody's business here
            return Run(p, h, radius, ahead);
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
