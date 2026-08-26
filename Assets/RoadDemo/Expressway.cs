using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// Where the expressway runs. Not a ring: a TREE - a trunk that begins at one
    /// place worth driving to, runs round the outside of the grid on a viaduct, and
    /// comes down into the city at the other end, with branches hung off it at the
    /// interchanges. Every arm of it ends IN something (a district's own street, a
    /// junction of the grid); none of it ends in the air, and none of it crosses
    /// anything at grade.
    ///
    /// The numbers are the plan's (Docs/expressway-plan.md, checked by
    /// Docs/expressway.py) and they are all here, in one place, so the two can be
    /// read against one another.
    /// </summary>
    [System.Serializable]
    public class ExpresswayRoute
    {
        public bool on;

        [Header("Rolled, or named")]
        [Tooltip("Let the road pick its own line off the city that was rolled: which " +
                 "line the far branch runs up, which ones carry an interchange, which " +
                 "one it dies on. The city is a different city every seed - a road " +
                 "whose lines are typed into a scene is a road for ONE of them.")]
        public bool roll = true;

        [Header("The band")]
        [Tooltip("How far outside the grid's own kerb the trunk's centre line runs. " +
                 "The inner ramp terminal lands 58 m inside that, which leaves a short " +
                 "city block between it and the grid's edge junction.")]
        public float bandOffset = 120f;
        [Tooltip("The radius of the corner where the trunk turns from one side of the " +
                 "city to the next. 260 m is 50 mph at six per cent of superelevation.")]
        public float cornerRadius = 260f;
        [Tooltip("The deck's road surface over the city's own road level. Seven metres " +
                 "leaves 5.33 m under the beam, which is a lorry and a half a metre.")]
        public float deckY = 7f;

        [Header("The end away from town")]
        [Tooltip("Which north-south line of the grid the trunk comes off, at the far end " +
                 "from its terminus: it leaves that junction, turns on to the band, and " +
                 "climbs. Both ends of the road are junctions of the city.")]
        public int branchLine = 2;
        [Tooltip("Unused: the road no longer ends in open country.")]
        public float branchRun = 430f;
        public string branchName = "AIRPORT";

        [Header("The interchanges")]
        [Tooltip("North-south lines of the grid that carry a diamond: two ramps down " +
                 "to an arterial that runs from the grid's edge junction out to a gate " +
                 "beyond the motorway.")]
        public int[] diamonds = new int[0];
        [Tooltip("The line carrying the T interchange to the second branch (-1: none).")]
        public int trumpetLine = -1;
        [Tooltip("How far past the band the branch's arterial runs before it ends on a " +
                 "street, with the barrier half way along it.")]
        public float trumpetRun = 260f;
        public string trumpetName = "PORT";

        [Header("The end in town")]
        [Tooltip("Which east-west line of the grid the trunk comes down on to and ends " +
                 "at, in a signalled junction with the grid's edge street.")]
        public int terminusLine = 2;

        [Header("The toll")]
        [Tooltip("The motorway itself is paid for, on top of the barrier the branch " +
                 "always carries. A barrier on EVERY way on to it - each ramp up from an " +
                 "arterial and each of the trunk's own two ends - so a flat fare is paid " +
                 "once by everyone who joins and nobody can drive on at one interchange " +
                 "and off at the next for nothing. OFF: the road is free and only the " +
                 "branch takes money, which is what the causeways this city is drawn " +
                 "from did in 1987.")]
        public bool tollRoad = true;

        [Header("Dressing")]
        public bool guideSigns = true;
        public bool lamps = true;
        public bool billboards = true;
        [Tooltip("The ground under the deck: barrels, mattresses, shopping trolleys - " +
                 "and the people living in them.")]
        public bool underDeck = true;
        [Min(0)] public int vagrants = 20;

    }

    /// <summary>What kind of interchange hangs off the trunk at a station.</summary>
    public enum ExchangeKind
    {
        /// <summary>Two ramps a deck, down to a signalled arterial that crosses under.</summary>
        Diamond,
        /// <summary>Four ramps to a branch that leaves the trunk for good: free flowing,
        /// every one of them entered and left on the right, two of them passing under
        /// the deck to reach the far side.</summary>
        Branch,
    }

    /// <summary>One interchange, by where it stands along the trunk.</summary>
    public sealed class Exchange
    {
        public ExchangeKind Kind;
        public float S;                 // metres along the trunk
        public int Line;                // the grid line it hangs off
        public string Name;             // what the signs say
        public int Number;              // the exit number
        public float Run;               // how far the branch runs past the band (Branch only)
    }

    /// <summary>
    /// The trunk's line and its height, worked out once from the city's own grid, and
    /// the interchanges along it. Everything the builder lays - decks, ramps, piers,
    /// signs, the ground under it - is read off this.
    /// </summary>
    public sealed class ExpresswayLayout
    {
        /// <summary>The trunk's centre line, from the far end of its branch to the
        /// junction of the grid it dies in.</summary>
        public RoadLine Trunk;
        /// <summary>Where it climbs on to its piers, and where it comes back down.</summary>
        public float ClimbFrom, ClimbTo, DescentFrom, DescentTo;
        public float DeckY = 7f;
        public readonly List<Exchange> Exchanges = new List<Exchange>();
        /// <summary>Trunk-s of the last point before the road runs into the city's own
        /// junction: the level tail the terminus needs.</summary>
        public float TerminusFrom;
        /// <summary>The two lines of the grid the road's ends stand on - rolled, or
        /// named in the route. The builder hangs its terminal junctions off these.</summary>
        public int BranchLine, TerminusLine;
        public string Why = "";

        /// <summary>Each deck's centre off the trunk's line: the right-hand one carries
        /// the traffic running with s, and its own exits go to the right of that, which
        /// is the side of the grid.</summary>
        public const float DeckOff = 6.5f;
        public const float DeckHalf = 5.7f;
        /// <summary>Two lanes on a deck, and the auxiliary one outboard of them.</summary>
        public static readonly float[] DeckLanes = { -2.85f, 2.85f };
        public const float AuxOff = 6.65f, AuxHalf = 9.5f;
        /// <summary>The stations of an interchange, off its centre along the trunk:
        /// where the deck gains its auxiliary lane, where the exit leaves it, where the
        /// entrance joins, and where the lane is given up again.</summary>
        public const float AuxIn = 340f, Gore = 220f, AuxOut = 400f;
        /// <summary>The whole box an interchange wants along the trunk, and across it.</summary>
        public const float ExchangeAlong = 840f, ExchangeAcross = 140f;
        /// <summary>Where a diamond's ramps meet the arterial, off the trunk's line -
        /// the inner terminal toward the city, the outer one away from it.</summary>
        public const float TerminalOff = 58f;
        /// <summary>And where the branch's own divided road begins, off the trunk.</summary>
        public const float SpurOff = 320f;
        /// <summary>The ramp: one lane, a shoulder either side.</summary>
        public const float RampHalf = 3.65f;
        public const float RampSpeed = 13f, DeckSpeed = 24.6f, ArterialSpeed = 13f;
        /// <summary>What the drawn road stands at where it runs on the ground: a hand
        /// over the bed the island is held flat to, so that the slab is seen at all.</summary>
        public const float GradeY = 0.12f;
        /// <summary>And what the CITY's asphalt stands at, which is not the same number.
        /// The kit's road cell is a flat piece laid at nought, so a ramp that arrives at
        /// an arterial on the motorway's own grade arrives twelve centimetres up - over a
        /// kerb, at the one place on the road where a car crosses a kerb line sideways.
        /// A ramp comes down to the street, not to the motorway.</summary>
        public const float StreetY = 0f;
        /// <summary>How long the trunk takes to climb or fall its seven metres, and at
        /// what grade. Four and a half per cent, not four: the climb and the descent
        /// together cost the road seven hundred metres of its own length before a single
        /// interchange can stand on it, and on the cities this game actually rolls that
        /// was the difference between two interchanges and one. Four and a half is a
        /// short urban ramp up on to a viaduct, which is what it is.</summary>
        public const float TrunkGrade = 0.045f;
        public const float TrunkRamp = 287f;
        /// <summary>Level road wanted between the foot of the descent and the junction
        /// the road dies in, and the radius it turns into the city on.</summary>
        public const float TerminusTail = 150f, TerminusRadius = 90f;
        /// <summary>Centre to centre of two interchanges. Half a mile is the urban
        /// minimum a 1987 manual gives, and half a mile is 805 m: at 800 the entrance
        /// nose of one and the exit nose of the next still stand 360 m apart.</summary>
        public const float Spacing = 800f;

        /// <summary>The height of the road surface at s along the trunk.</summary>
        public float HeightAt(float s)
        {
            if (s <= ClimbFrom) return GradeY;
            if (s < ClimbTo) return Grade(GradeY, DeckY, ClimbFrom, ClimbTo, s);
            if (s <= DescentFrom) return DeckY;
            if (s < DescentTo) return Grade(DeckY, GradeY, DescentFrom, DescentTo, s);
            return GradeY;
        }

        /// <summary>Is the road off the ground here? (Piers, and what the trace calls a
        /// deck.)</summary>
        public bool Elevated(float s) => HeightAt(s) > 2.5f;

        /// <summary>A grade with a vertical curve at each end: a parabola into the
        /// slope, a straight down it, a parabola out. A straight ramp joined to a level
        /// road at both ends is a pair of kinks a car would leave the road on.</summary>
        public static float Grade(float y0, float y1, float s0, float s1, float s)
        {
            float len = Mathf.Max(1f, s1 - s0);
            float e = Mathf.Min(len * 0.3f, 90f);          // the curve at each end
            float h = y0 - y1;
            float g = h / Mathf.Max(1f, len - e);           // the straight grade between them
            float t = Mathf.Clamp(s - s0, 0f, len);
            if (t < e) return y0 - g * t * t / (2f * e);
            if (t > len - e) return y1 + g * (len - t) * (len - t) / (2f * e);
            return y0 - g * (t - e * 0.5f);
        }

        // ------------------------------------------------------------------ solving

        /// <summary>The trunk, worked out of the grid it runs round: up the branch line
        /// from beyond the city, round on to the north side of the band, along it, round
        /// the north-east corner, down the east side, and in to the grid on the terminus
        /// line. Every corner is a real radius, so the road bends instead of breaking.
        ///
        /// <paramref name="vx"/> and <paramref name="hz"/> are the grid's own road
        /// centre lines; <paramref name="kerb"/> is the rectangle of its outermost
        /// pavements.</summary>
        public static ExpresswayLayout Solve(ExpresswayRoute route, float[] vx, float[] hz, Rect kerb,
                                            int seed = 1987, bool[] vBoulevard = null, bool[] hBoulevard = null)
        {
            var lay = new ExpresswayLayout { DeckY = Mathf.Max(5.5f, route.deckY) };
            float d = Mathf.Max(60f, route.bandOffset);
            float r = Mathf.Max(80f, route.cornerRadius);

            int bl, tl;
            if (route.roll) Roll(vx, hz, vBoulevard, hBoulevard, seed, out bl, out tl);
            else { bl = Mathf.Clamp(route.branchLine, 0, vx.Length - 1); tl = Mathf.Clamp(route.terminusLine, 0, hz.Length - 1); }
            lay.BranchLine = bl; lay.TerminusLine = tl;
            float bandN = kerb.yMax + d;               // the north side of the band
            float bandE = kerb.xMax + d;               // and its east side
            float branchX = vx[bl];
            float terminusZ = hz[tl];

            var pts = new List<Vector3>();
            // 1. OUT OF THE CITY on the branch line, heading away from it. Both ends of
            // this road are junctions of the grid, and that is on purpose: an end that
            // runs out into open country has to be given streets of its own to die on,
            // and streets invented for a motorway to end in are streets nobody has ever
            // driven - the cars filled them, turned round in them and locked together in
            // them, five thousand refused steps at a stub nobody would have built.
            var start = new Vector3(branchX, 0f, kerb.yMax - 6.5f + 2f);
            pts.Add(start);
            // 2. round on to the north side of the band, heading east - on the tight
            // radius, because there are only 120 m between the kerb and the band
            RoadLine.Corner(pts, new Vector3(branchX, 0f, bandN), Vector3.forward, Vector3.right, TerminusRadius);
            // 3. round the north-east corner, heading south
            RoadLine.Corner(pts, new Vector3(bandE, 0f, bandN), Vector3.right, Vector3.back, r);
            // 4. and in to the city on the terminus line, heading west
            RoadLine.Corner(pts, new Vector3(bandE, 0f, terminusZ), Vector3.back, Vector3.left, TerminusRadius);
            // 5. up to the face of the grid's own edge junction. NOT past it: the deck
            // has to hand over to that junction across a few metres, not across thirty -
            // a car is inside a junction box from the moment it leaves the road until it
            // reaches the next one, and thirty metres of that is a car parked in the
            // middle of a crossroads for a second and a half.
            pts.Add(new Vector3(kerb.xMax - 6.5f + 2f, 0f, terminusZ));
            lay.Trunk = RoadLine.Through(pts);

            float len = lay.Trunk.Length;
            // level through the curve off the grid, then up
            lay.ClimbFrom = TerminusTail;
            lay.ClimbTo = lay.ClimbFrom + TrunkRamp;
            lay.TerminusFrom = len - TerminusTail;
            lay.DescentTo = lay.TerminusFrom;
            lay.DescentFrom = lay.DescentTo - TrunkRamp;

            // where each interchange falls along that line
            void Add(ExchangeKind kind, int line, string name, float run)
            {
                if (line < 0 || line >= vx.Length) return;
                var at = new Vector3(vx[line], 0f, bandN);
                lay.Trunk.Project(at, out float s, out float off);
                if (Mathf.Abs(off) > 25f) return;                       // not on the band's north side
                lay.Exchanges.Add(new Exchange { Kind = kind, S = s, Line = line, Name = name, Run = run });
            }

            if (route.roll)
            {
                // every line of the grid is offered; the room test below keeps the ones
                // that fit, half a mile apart, clear of the climb and of the descent.
                // Which of them there ARE is the city's business and changes every seed.
                for (int i = 0; i < vx.Length; i++)
                    if (i != bl) Add(ExchangeKind.Diamond, i, null, 0f);
            }
            else
            {
                if (route.diamonds != null)
                    foreach (int line in route.diamonds)
                        Add(ExchangeKind.Diamond, line, null, 0f);
                Add(ExchangeKind.Branch, route.trumpetLine, route.trumpetName, route.trumpetRun);
            }

            lay.Exchanges.Sort((a, b) => a.S.CompareTo(b.S));

            // and the ones there is no room for. An interchange wants its own 840 m of
            // trunk, clear of the climb, of the descent, and of its neighbours: a ramp
            // laid over another interchange's ramp is the freeway the city rolled before.
            // the first one's deceleration lane may not begin before the road is up on
            // its piers, the last one's acceleration lane may not run into the descent,
            // and no two of them may want the same stretch of deck
            float free = lay.ClimbTo;
            var kept = new List<Exchange>();
            var dropped = new List<string>();
            foreach (var ex in lay.Exchanges)
            {
                bool room = ex.S - AuxIn > free && ex.S + AuxOut < lay.DescentFrom - 40f;
                if (!room) { dropped.Add($"line {ex.Line}"); continue; }
                kept.Add(ex);
                // half a mile to the next one if there is room for it, and never less
                // than the two auxiliary lanes need between them
                free = Mathf.Max(ex.S + AuxOut + 20f, ex.S + Spacing - AuxIn);
            }
            lay.Exchanges.Clear();
            lay.Exchanges.AddRange(kept);

            // one of them is the T to the second branch: the LAST one, which stands
            // furthest from the branch at the other end and so gives the two of them the
            // length of the road between them. It only becomes one if there is a branch's
            // worth of open ground beyond the band there.
            if (route.roll && lay.Exchanges.Count >= 1)
            {
                var last = lay.Exchanges[lay.Exchanges.Count - 1];
                var at = lay.Trunk.Pose(last.S, -(SpurOff + route.trumpetRun + 40f));
                bool room = at.x > kerb.xMin - 900f && at.x < kerb.xMax + 900f &&
                            at.z > kerb.yMin - 900f && at.z < kerb.yMax + 900f;
                if (room)
                {
                    last.Kind = ExchangeKind.Branch;
                    last.Name = route.trumpetName;
                    last.Run = route.trumpetRun;
                }
            }
            for (int i = 0; i < lay.Exchanges.Count; i++)
            {
                var ex = lay.Exchanges[i];
                // numbered from the town end back, the way a state route is
                ex.Number = lay.Exchanges.Count - i;
                if (string.IsNullOrEmpty(ex.Name)) ex.Name = "EXIT " + ex.Number;
            }

            lay.Why = $"trunk {len:F0} m, deck {lay.ClimbTo:F0}..{lay.DescentFrom:F0} at {lay.DeckY:F1} m, " +
                      $"{lay.Exchanges.Count} interchange(s)" +
                      (dropped.Count > 0 ? $"; no room for {string.Join(", ", dropped)}" : "");
            return lay;
        }

        // ------------------------------------------------------------------- frames

        /// <summary>Which line the far branch runs up, and which one the road dies on -
        /// picked off the city that was rolled rather than typed into a scene.
        ///
        /// The branch wants to be near one end of the long side (so the trunk has the
        /// whole of that side to run down) and off the very edge line (a line on the edge
        /// of the grid has a city on one side of it and nothing on the other). The
        /// terminus wants an AVENUE if the city rolled one in the middle third, because
        /// what comes off a motorway there has to be carried away.</summary>
        static void Roll(float[] vx, float[] hz, bool[] vBoulevard, bool[] hBoulevard,
                         int seed, out int branchLine, out int terminusLine)
        {
            var rng = new System.Random(seed * 7919 + 31);
            int nv = vx.Length, nh = hz.Length;

            // the branch: in the first quarter of the grid, never the outermost line
            int lo = 1, hi = Mathf.Max(2, nv / 4);
            branchLine = lo + rng.Next(Mathf.Max(1, hi - lo + 1));
            branchLine = Mathf.Clamp(branchLine, 1, nv - 3);

            // the terminus: the middle third, an avenue if there is one there
            // the BOTTOM third: the trunk comes down the far side of the city and turns
            // in where this line is, so the further south it is the longer that side is -
            // and the length of it is what an interchange needs
            int mLo = 1, mHi = Mathf.Max(1, nh / 3);
            var picks = new List<int>();
            for (int j = mLo; j <= mHi; j++)
                if (hBoulevard != null && j < hBoulevard.Length && hBoulevard[j]) picks.Add(j);
            if (picks.Count == 0)
                for (int j = mLo; j <= mHi; j++) picks.Add(j);
            if (picks.Count == 0) picks.Add(Mathf.Clamp(nh / 2, 0, nh - 1));
            terminusLine = picks[rng.Next(picks.Count)];
        }

        /// <summary>Which way the trunk runs at a station, and across it.</summary>
        public Vector3 Dir(float s) => Trunk.DirAt(s);
        public Vector3 Right(float s) => Trunk.RightAt(s);
    }
}
