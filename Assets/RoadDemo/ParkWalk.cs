using System;
using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// The plan of a park, in cells: where the walk runs, where it meets the pavement, and
    /// what the ground it leaves over is given over to.
    ///
    /// Pure arithmetic, no prefabs and no scene - the same office <see cref="CoreLayout"/>
    /// does for the core and <see cref="IndustrialLayout"/> for the quarter, and for the same
    /// reason: <c>Tools/CoreSim</c> compiles this and can deal a thousand parks in a second,
    /// so the shape is argued about before anything is ever stood up.
    ///
    /// WHAT A PARK IS, read off the two the POLYGON City artists drew (block-08 and the
    /// courtyard of block-12, measured 2026-08-26): fenced grass with a walk through it, on
    /// the pack's 5 m module, one tile to a cell. The walk enters from the pavement on two or
    /// three sides, breaks, and every tile of it is chosen by its NEIGHBOURS - one neighbour
    /// is an end, two in line a straight, two at an angle a corner, three a T, four a
    /// junction. That single rule covers every 4-connected walk the pack can lay, which is
    /// why the walk here is a SET OF CELLS and not a spline: the tiles fall out of it.
    ///
    /// Measured off block-08's own six walk tiles, which this reproduces exactly: three
    /// mouths (west, north, east), a T where they meet, and no tile of the walk touching the
    /// fence except at a mouth.
    ///
    /// THE ONE THING THE OLD PARK GENERATOR GOT WRONG and this does not: a big park is not a
    /// small park stretched. Assets/Scripts/Generation/ParkLayout.cs scales density and puts
    /// one fountain in the middle however large the ground is, so a 120 m park came out as a
    /// bald field with a feature in it. Here the ground the walk leaves over is cut into
    /// ROOMS (<see cref="Room"/>), none bigger than <see cref="RoomMax"/>, and each room gets
    /// exactly one programme. A park that is four times the size gets four times the reasons
    /// to walk into it.
    /// </summary>
    public static class ParkWalk
    {
        /// <summary>The pack's module, and the city's.</summary>
        public const float Cell = CoreBlockMetrics.Cell;

        /// <summary>
        /// The biggest a room may be before the walk is asked to cut it in two, in cells.
        ///
        /// Fifty metres is about as far as a lawn can run before it stops reading as a lawn
        /// and starts reading as ground nobody has done anything with - which is exactly the
        /// complaint the industrial quarter got about its yards ("preveliku su, necu tolka
        /// prazna dvorista"), arrived at from the same direction.
        /// </summary>
        public const int RoomMax = 10;

        /// <summary>The furthest a person standing on the grass may be from a path, in cells.
        /// Twenty-five metres: past that the ground stops reading as part of the park and
        /// starts reading as ground the park happens to own.</summary>
        public const int ReachMax = 5;

        /// <summary>How thick the pavement ring is, in cells. CoreDemo uses the same
        /// ten-metre band around parks as it does around every other generated block.</summary>
        public const int Band = CoreBlockMetrics.PavementTiles;

        // ------------------------------------------------------------------- the pieces

        /// <summary>Which side of the park, in the park's own frame. A park is composed
        /// facing nowhere in particular - unlike an industrial parcel it has no front - so
        /// this is simply the compass.</summary>
        public enum Side { South, North, West, East }

        /// <summary>What runs along a side: the park's own kerb with a street beyond it, or
        /// a boundary shared with whatever is next door (the belt's parks meet each other,
        /// and there is no pavement in between).</summary>
        public enum Rim { Kerb, Party }

        /// <summary>
        /// One side of a park: what runs along it, whether this park lays the shared fence,
        /// and where the crossing is if the street outside has one.
        ///
        /// The crossing is the whole reason a mouth is not simply put in the middle of a
        /// side. A gate opposite a zebra is a gate people walk to; a gate five metres along
        /// from one is a gate they walk past. Block-08's own north mouth sits on the cell the
        /// crossing lands on.
        /// </summary>
        public struct Edge
        {
            public Rim Rim;
            public bool Lays;
            /// <summary>Cell along this side the street's crossing lands on, counted in the
            /// park's own cell indices (i for a south or north side, j for west or east), or
            /// -1 for no crossing.</summary>
            public int Crossing;

            public Edge(Rim rim, bool lays = true, int crossing = -1)
            {
                Rim = rim;
                Lays = lays;
                Crossing = crossing;
            }

            /// <summary>Every side the park's own, kerb and fence alike: a park standing on
            /// its own in the middle of nothing, which is what a core block is.</summary>
            public static Edge[] Alone()
            {
                var sides = new Edge[4];
                for (int k = 0; k < 4; k++) sides[k] = new Edge(Rim.Kerb);
                return sides;
            }
        }

        /// <summary>What one cell of the park is. The kerb ring belongs to the park too - it
        /// carries the block's lamps and bins, and the park lays it itself.</summary>
        public enum Ground { Kerb, Grass, Walk, Plaza }

        /// <summary>
        /// What a room is given over to.
        ///
        /// Every one of these is a piece the project actually has, measured 2026-08-26; there
        /// is no lake, no bandstand and no memorial because there is no model for them and a
        /// park is not the place to start guessing (leave-it-empty-dont-guess).
        /// </summary>
        public enum Programme
        {
            Lawn, Grove, Fountain, Playground, Courts, Pavilion, Statue, Skatepark, Toilet
        }

        /// <summary>How big a room a programme needs, in cells - read off the measured
        /// pieces plus the room to walk round them, not chosen:
        ///
        ///  * the fountain is 4.8 m across and wants its 3 x 3 paved apron: 4 x 4;
        ///  * two tennis courts are 7.5 x 12.5 m each, ten metres apart as the palm city's
        ///    own demo lays them - 17.5 x 12.5 m of court, so 5 x 4 with a margin;
        ///  * the playground's five pieces (fort, swing, roundabout, slide, sandpit) fit a
        ///    15 m square inside its wooden fence: 4 x 4;
        ///  * the pavilion is 9.5 x 8.4: 3 x 3;
        ///  * the skatepark is 40.5 x 31.5 m - a whole city block of a thing, and 9 x 7 cells
        ///    (the plan first guessed 6 x 6, which would have had it standing on the fence);
        ///  * the toilet block is 10.6 x 5.5: 3 x 2, with its back to the fence.
        /// </summary>
        public static void Smallest(Programme what, out int w, out int d)
        {
            switch (what)
            {
                case Programme.Grove: w = 4; d = 4; break;
                case Programme.Fountain: w = 4; d = 4; break;
                case Programme.Playground: w = 4; d = 4; break;
                case Programme.Courts: w = 5; d = 4; break;
                case Programme.Pavilion: w = 3; d = 3; break;
                case Programme.Statue: w = 3; d = 3; break;
                case Programme.Skatepark: w = 9; d = 7; break;
                case Programme.Toilet: w = 3; d = 2; break;
                default: w = 1; d = 1; break;                    // the lawn takes what is left
            }
        }

        /// <summary>
        /// The letter a programme takes on the map.
        ///
        /// Spelled out rather than taken from the first letter of its name, which is how the
        /// playground and the pavilion both came out as 'p' and a map of a park with one of
        /// each could not be read at all.
        /// </summary>
        public static char Mark(Programme what)
        {
            switch (what)
            {
                case Programme.Grove: return 'g';
                case Programme.Fountain: return 'f';
                case Programme.Playground: return 'P';
                case Programme.Courts: return 'c';
                case Programme.Pavilion: return 'v';
                case Programme.Statue: return 'm';
                case Programme.Skatepark: return 'k';
                case Programme.Toilet: return 't';
                default: return '.';
            }
        }

        /// <summary>What a programme is called on a card, in words rather than in the enum's
        /// shorthand.</summary>
        public static string Words(Programme what)
        {
            switch (what)
            {
                case Programme.Grove: return "grove";
                case Programme.Fountain: return "fountain";
                case Programme.Playground: return "playground";
                case Programme.Courts: return "courts";
                case Programme.Pavilion: return "pavilion";
                case Programme.Statue: return "monument";
                case Programme.Skatepark: return "skatepark";
                case Programme.Toilet: return "toilet block";
                default: return "lawn";
            }
        }

        /// <summary>
        /// How big a park is, which is not chosen but READ OFF THE GROUND it was given.
        ///
        /// A park does not get to decide how large it is - the quarter deals it a rectangle
        /// and the recipe has to fill it, the same bargain the industrial parcel struck. What
        /// the class decides is how much is asked of the ground: a pocket is block-08 and
        /// wants nothing but a walk and some benches; a square carries one programme; a park
        /// carries several; a strip is a park that is much longer than it is wide, which is
        /// what the belt around the core is made of.
        /// </summary>
        public enum Klass { Pocket, Square, Park, Strip }

        /// <summary>One cell, by index. Not Vector2Int: this is arithmetic on a grid and a
        /// pair of ints says so.</summary>
        public readonly struct Spot : IEquatable<Spot>
        {
            public readonly int I, J;
            public Spot(int i, int j) { I = i; J = j; }
            public bool Equals(Spot other) => other.I == I && other.J == J;
            public override bool Equals(object o) => o is Spot s && Equals(s);
            public override int GetHashCode() => I * 73856093 ^ J * 19349663;
            public override string ToString() => $"({I}, {J})";
        }

        /// <summary>Where the walk meets the pavement: the cell of the park it leaves by,
        /// and which side that is. The fence has its gap here and nowhere else.</summary>
        public sealed class Mouth
        {
            public Side Side;
            public Spot At;
            /// <summary>Was it put opposite a crossing, or in the middle of the side for want
            /// of one?</summary>
            public bool OnCrossing;
        }

        /// <summary>
        /// A piece of ground the walk left over, and the one thing that stands on it.
        ///
        /// Rooms are found rather than laid out: whatever the walk does not take is cut into
        /// 4-connected pieces, and any piece too big to read as one place is cut again by a
        /// walk through the middle of it.
        /// </summary>
        public sealed class Room
        {
            public int I0, J0, W, D;               // its bounding rectangle, in cells
            public readonly List<Spot> Cells = new List<Spot>();
            public Programme Programme = Programme.Lawn;
            /// <summary>Does it touch the fence? A toilet block wants to, a fountain does
            /// not.</summary>
            public bool OnFence;
            /// <summary>The walk was asked to cut this room in two and could not. Kept so the
            /// search moves on - and so the verdict still sees the room at its real size,
            /// which quietly shrinking it did not.</summary>
            public bool Uncut;
            public int Area => Cells.Count;

            /// <summary>How far the furthest corner of this room is from the nearest walk, in
            /// cells. Set when the rooms are found.</summary>
            public int Reach;

            /// <summary>
            /// The biggest rectangle of cells lying WHOLLY inside the room, and where it sits.
            ///
            /// This, and not <see cref="W"/> x <see cref="D"/>, is what a programme has to
            /// fit in - and the difference is the whole game on a park with a loop round it.
            /// The grass between the loop and the fence is one ring-shaped room; its bounding
            /// rectangle is the entire park, so judged by that it looked big enough for a
            /// fountain, a playground and a set of courts. It is two cells wide everywhere.
            /// Cast that way, every programme was handed the ring, every one of them was
            /// quietly refused when it came to stand, and an 80 x 70 m park came out with
            /// seven named rooms and nothing whatever in it.
            /// </summary>
            public int InnerI, InnerJ, InnerW, InnerD;

            /// <summary>
            /// Too big to read as one place?
            ///
            /// NOT the size of its rectangle, which was the first answer and the wrong one: a
            /// park with a loop round it has one ring-shaped room of grass between the loop
            /// and the fence, and that ring's bounding rectangle is the whole park. Judged by
            /// the rectangle, every park with a loop failed - though nowhere in that ring is
            /// more than ten metres from a path.
            ///
            /// What actually matters is how far you have to walk over grass to reach a path.
            /// Twenty-five metres is the limit here, which is a lawn you cross rather than a
            /// field you set out over - and it makes the belt of trees legal (two cells) while
            /// still cutting up the middle of a big park.
            /// </summary>
            public bool TooBig => !Uncut && (Reach > ReachMax || Corridor);

            /// <summary>
            /// Twice as long as it is wide, and wide enough to matter - a corridor of grass
            /// rather than a room.
            ///
            /// Worth cutting on its own account, because a programme does not stretch: the
            /// playground is fifteen metres square whatever it is put in, and dropped in a
            /// room of 30 x 60 m it sits in one end with half a tennis court of empty grass
            /// behind it. The industrial quarter learnt the same thing the same way - the
            /// answer to an empty yard is a smaller yard, not more barrels.
            ///
            /// A band of two cells or less is exempt: that is the belt against the fence,
            /// where the trees go, and it is meant to run.
            /// </summary>
            public bool Corridor => Mathf.Min(W, D) > 2 && Mathf.Max(W, D) >= 2 * Mathf.Min(W, D);
            public Rect Box => new Rect(I0 * Cell, J0 * Cell, W * Cell, D * Cell);
            public Spot Middle => new Spot(I0 + W / 2, J0 + D / 2);
        }

        /// <summary>Everything a park is, before a single prefab is loaded.</summary>
        public sealed class Plan
        {
            public int NX, NZ;                     // the whole block, kerb ring and all
            public int I0, J0, I1, J1;             // the ground inside the ring, inclusive
            public Klass Klass;
            public int Seed;
            public Ground[,] Cells;
            public Edge[] Sides;
            public readonly List<Mouth> Mouths = new List<Mouth>();
            public readonly List<Room> Rooms = new List<Room>();
            /// <summary>What the ground refused - a programme that would not fit, a room too
            /// big to cut. Not faults: the one thing a half-empty park would otherwise never
            /// say.</summary>
            public readonly List<string> Notes = new List<string>();

            public int W => I1 - I0 + 1;
            public int D => J1 - J0 + 1;
            public float Wide => NX * Cell;
            public float Deep => NZ * Cell;

            public bool In(int i, int j) => i >= 0 && j >= 0 && i < NX && j < NZ;
            public bool Inside(int i, int j) => i >= I0 && j >= J0 && i <= I1 && j <= J1;
            public bool Inside(Spot s) => Inside(s.I, s.J);
            public bool Walked(int i, int j) =>
                In(i, j) && (Cells[i, j] == Ground.Walk || Cells[i, j] == Ground.Plaza);
            public bool Walked(Spot s) => Walked(s.I, s.J);

            /// <summary>Is this cell on the inside edge - the ring of ground against the
            /// fence? The walk may only stand here at a mouth.</summary>
            public bool OnFence(int i, int j) =>
                Inside(i, j) && (i == I0 || i == I1 || j == J0 || j == J1);

            public string Name => $"park-{Klass.ToString().ToLowerInvariant()}-{NX * 5}x{NZ * 5}";

            /// <summary>The park drawn out, one character to a cell, north row first - the
            /// probe's view of a plan, and how a walk that went wrong is argued about.</summary>
            public string Map
            {
                get
                {
                    var sb = new System.Text.StringBuilder();
                    var mouths = new HashSet<Spot>();
                    foreach (var mouth in Mouths) mouths.Add(mouth.At);
                    var programme = new Dictionary<Spot, char>();
                    foreach (var room in Rooms)
                    {
                        char mark = Mark(room.Programme);
                        foreach (var cell in room.Cells) programme[cell] = mark;
                    }
                    for (int j = NZ - 1; j >= 0; j--)
                    {
                        for (int i = 0; i < NX; i++)
                        {
                            var spot = new Spot(i, j);
                            char c;
                            switch (Cells[i, j])
                            {
                                case Ground.Kerb: c = '#'; break;
                                case Ground.Plaza: c = '+'; break;
                                case Ground.Walk: c = mouths.Contains(spot) ? 'o' : '='; break;
                                default: c = programme.TryGetValue(spot, out var mark) ? mark : ','; break;
                            }
                            sb.Append(c);
                        }
                        sb.Append('\n');
                    }
                    return sb.ToString();
                }
            }
        }

        // -------------------------------------------------------------------- the deal

        static readonly Spot[] Steps =
        {
            new Spot(-1, 0), new Spot(1, 0), new Spot(0, -1), new Spot(0, 1),
        };

        static Spot Step(Spot from, Spot by) => new Spot(from.I + by.I, from.J + by.J);

        /// <summary>
        /// Lays out a park on a rectangle of cells.
        ///
        /// <paramref name="nx"/> and <paramref name="nz"/> are the WHOLE block, pavement ring
        /// and all, because that is what a quarter deals: a block of ground with roads round
        /// it. The ring uses the shared CoreDemo pavement width on every side that has a kerb.
        /// </summary>
        public static Plan Lay(int nx, int nz, Edge[] sides, System.Random rng)
        {
            sides = sides ?? Edge.Alone();
            var plan = new Plan
            {
                NX = Mathf.Max(3, nx),
                NZ = Mathf.Max(3, nz),
                Sides = sides,
            };
            plan.Cells = new Ground[plan.NX, plan.NZ];

            plan.I0 = sides[(int)Side.West].Rim == Rim.Kerb ? Band : 0;
            plan.J0 = sides[(int)Side.South].Rim == Rim.Kerb ? Band : 0;
            plan.I1 = plan.NX - 1 - (sides[(int)Side.East].Rim == Rim.Kerb ? Band : 0);
            plan.J1 = plan.NZ - 1 - (sides[(int)Side.North].Rim == Rim.Kerb ? Band : 0);

            for (int i = 0; i < plan.NX; i++)
                for (int j = 0; j < plan.NZ; j++)
                    plan.Cells[i, j] = plan.Inside(i, j) ? Ground.Grass : Ground.Kerb;

            plan.Klass = Classify(plan.W, plan.D);
            Mouths(plan, rng);
            Loop(plan, rng);
            Spine(plan, rng);
            Rooms(plan);
            Cut(plan, rng);
            Cast(plan, rng);
            Plaza(plan, rng);
            return plan;
        }

        /// <summary>
        /// Which class a rectangle falls into. Read off the SHORT side, plus the ratio: a
        /// park thirty metres wide and three hundred long is a strip whatever its area says,
        /// and the belt round the core is made of nothing else.
        /// </summary>
        public static Klass Classify(int w, int d)
        {
            int least = Mathf.Min(w, d), most = Mathf.Max(w, d);
            if (least > 0 && most >= least * 5 / 2 && most >= 10) return Klass.Strip;
            if (least <= 5) return Klass.Pocket;
            if (least <= 10) return Klass.Square;
            return Klass.Park;
        }

        // ------------------------------------------------------------------- the mouths

        /// <summary>
        /// Where the walk meets the street.
        ///
        /// One to a side at most and never on a corner cell, which is the pack's own rule -
        /// block-08 has three, on three different sides, and every one of them is a cell in
        /// from the corner. A side with no kerb (the boundary the belt's parks share) has no
        /// mouth at all: there is no pavement out there to arrive from.
        /// </summary>
        static void Mouths(Plan plan, System.Random rng)
        {
            var open = new List<Side>();
            for (int k = 0; k < 4; k++)
            {
                var side = (Side)k;
                if (plan.Sides[k].Rim != Rim.Kerb) continue;
                if (Along(plan, side) < 3) continue;             // no room for a gate off the corner
                open.Add(side);
            }
            if (open.Count == 0) { plan.Notes.Add("no side has a street: the park has no way in"); return; }

            int want = Wanted(plan, rng);
            Dice.Shuffle(open, rng);
            // a strip takes its first two gates at the ENDS, because the walk between them is
            // what runs the length of it. Given the long sides first, a two hundred metre
            // strip was joined gate to gate across the middle and the far half of it never
            // saw a path.
            if (plan.Klass == Klass.Strip)
                open.Sort((one, other) => Along(plan, one).CompareTo(Along(plan, other)));

            for (int k = 0; k < open.Count && plan.Mouths.Count < want; k++)
            {
                var side = open[k];
                int lo = Low(plan, side) + 1, hi = High(plan, side) - 1;
                if (hi < lo) continue;

                int crossing = plan.Sides[(int)side].Crossing;
                bool onCrossing = crossing >= lo && crossing <= hi;
                int at = onCrossing ? crossing : Middle(lo, hi, rng);

                plan.Mouths.Add(new Mouth { Side = side, At = Face(plan, side, at), OnCrossing = onCrossing });
            }
        }

        /// <summary>How many ways in. Two or three for the small ones - block-08 has three on
        /// thirty metres - and a strip gets one every dozen cells, because a two hundred
        /// metre park with two gates is a fence with a park behind it.</summary>
        static int Wanted(Plan plan, System.Random rng)
        {
            switch (plan.Klass)
            {
                case Klass.Pocket: return 2 + rng.Next(2);
                case Klass.Square: return 2 + rng.Next(2);
                case Klass.Park: return 3 + rng.Next(2);
                default: return Mathf.Clamp(2 + Mathf.Max(plan.W, plan.D) / 12, 2, 4);
            }
        }

        /// <summary>How many cells long a side is, along the ground inside the fence.</summary>
        static int Along(Plan plan, Side side) =>
            side == Side.South || side == Side.North ? plan.W : plan.D;

        static int Low(Plan plan, Side side) =>
            side == Side.South || side == Side.North ? plan.I0 : plan.J0;

        static int High(Plan plan, Side side) =>
            side == Side.South || side == Side.North ? plan.I1 : plan.J1;

        /// <summary>The cell a mouth on this side stands in, given how far along it is.</summary>
        static Spot Face(Plan plan, Side side, int along)
        {
            switch (side)
            {
                case Side.South: return new Spot(along, plan.J0);
                case Side.North: return new Spot(along, plan.J1);
                case Side.West: return new Spot(plan.I0, along);
                default: return new Spot(plan.I1, along);
            }
        }

        /// <summary>The middle of a run, give or take a cell - a gate dead on centre every
        /// time reads as a plan drawn with a ruler.</summary>
        static int Middle(int lo, int hi, System.Random rng)
        {
            int mid = (lo + hi) / 2;
            int jitter = hi > lo ? rng.Next(3) - 1 : 0;
            return Mathf.Clamp(mid + jitter, lo, hi);
        }

        // --------------------------------------------------------------------- the loop

        /// <summary>How far inside the fence the perimeter walk runs, in cells. One or two -
        /// a belt of trees five metres deep or ten, chosen per side so the loop is not the
        /// same rectangle in every park.</summary>
        const int LoopIn = 1, LoopOut = 2;

        /// <summary>The smallest park that gets a loop, in cells inside the fence. Under this
        /// the loop and the belt would eat the whole park and leave nothing to walk to.</summary>
        const int LoopLeast = 9;

        /// <summary>
        /// The walk round the park, a cell or two inside the fence.
        ///
        /// THE ONE THING THAT MAKES A BIG PARK WORK, and it took a sweep to see why. A walk
        /// that is a tree - mouths joined to each other and nothing more - leaves rooms that
        /// touch the fence on one side, and a room against the fence cannot be cut in two:
        /// every line through it runs into the railings at one end, and a path that stops at
        /// a railing is a path nobody would have built. So a 110 x 90 m park came out with one
        /// walk across it and fifty-five by eighty metres of nothing beside it.
        ///
        /// A loop closes that. Every room is then bounded by walk on the outside as well as
        /// the inside, so <see cref="Cut"/> can always halve it, and the ground left between
        /// the loop and the fence is the belt the trees stand in - which is where the pack's
        /// own parks put them. It is also simply what a city park of the period IS: a
        /// perimeter path with the ground inside given over to things.
        /// </summary>
        static void Loop(Plan plan, System.Random rng)
        {
            if (plan.Klass != Klass.Park && plan.Klass != Klass.Strip) return;
            if (Mathf.Min(plan.W, plan.D) < LoopLeast) return;

            // an inset per side, so the loop is a rectangle of its own rather than the fence
            // line moved in by a fixed amount
            int west = plan.I0 + rng.Next(LoopIn, LoopOut + 1);
            int east = plan.I1 - rng.Next(LoopIn, LoopOut + 1);
            int south = plan.J0 + rng.Next(LoopIn, LoopOut + 1);
            int north = plan.J1 - rng.Next(LoopIn, LoopOut + 1);
            if (east - west < 2 || north - south < 2) return;

            for (int i = west; i <= east; i++)
            {
                Walk(plan, new Spot(i, south));
                Walk(plan, new Spot(i, north));
            }
            for (int j = south; j <= north; j++)
            {
                Walk(plan, new Spot(west, j));
                Walk(plan, new Spot(east, j));
            }
        }

        // -------------------------------------------------------------------- the spine

        /// <summary>
        /// The walk: the first two mouths joined through the middle, and every mouth after
        /// that joined to the nearest walk already laid.
        ///
        /// Nothing here draws a straight line from edge to edge, because the artists' own
        /// parks do not: block-08's walk comes in from the west, turns at a T in the middle,
        /// and goes out north and east. A line straight across reads as a path somebody had
        /// to make rather than one they chose.
        /// </summary>
        static void Spine(Plan plan, System.Random rng)
        {
            if (plan.Mouths.Count == 0) return;

            // where a loop is already round the park every gate simply joins it, and the
            // ground inside is cut up afterwards. Without one the first two gates are joined
            // to each other and become the spine everything else hangs off.
            if (Laid(plan))
            {
                foreach (var mouth in plan.Mouths)
                {
                    Walk(plan, mouth.At);
                    var onto = Nearest(plan, mouth.At, true);
                    if (onto.HasValue) Join(plan, mouth.At, onto.Value, rng);
                }
                return;
            }

            Walk(plan, plan.Mouths[0].At);
            if (plan.Mouths.Count == 1)
            {
                // one way in is a dead end by definition, and the pocket park is allowed
                // exactly one of those - block-08 has a stub of its own. It runs to the
                // middle and stops.
                var heart = new Spot((plan.I0 + plan.I1) / 2, (plan.J0 + plan.J1) / 2);
                Join(plan, plan.Mouths[0].At, heart, rng);
                return;
            }

            Join(plan, plan.Mouths[0].At, plan.Mouths[1].At, rng);
            for (int k = 2; k < plan.Mouths.Count; k++)
            {
                var at = plan.Mouths[k].At;
                Walk(plan, at);
                var near = Nearest(plan, at);
                if (near.HasValue) Join(plan, at, near.Value, rng);
            }
        }

        /// <summary>Is any walk down yet?</summary>
        static bool Laid(Plan plan)
        {
            for (int i = plan.I0; i <= plan.I1; i++)
                for (int j = plan.J0; j <= plan.J1; j++)
                    if (plan.Cells[i, j] == Ground.Walk) return true;
            return false;
        }

        /// <summary>
        /// The walk cell already laid that is closest to here, by the way a person would walk
        /// it rather than as the crow flies.
        ///
        /// <paramref name="paths"/> asks for a cell of WALK and not of paved apron, which
        /// matters more than it sounds: an apron counts as walkable, so a fountain looking for
        /// the way to the nearest walk found the paving under its own feet, joined itself to
        /// itself, and left the apron an island the walk never reached (346 of 676 sizes in the
        /// first sweep).
        /// </summary>
        static Spot? Nearest(Plan plan, Spot from, bool paths = false)
        {
            Spot best = default;
            int least = int.MaxValue;
            for (int i = plan.I0; i <= plan.I1; i++)
                for (int j = plan.J0; j <= plan.J1; j++)
                {
                    if (paths ? plan.Cells[i, j] != Ground.Walk : !plan.Walked(i, j)) continue;
                    if (i == from.I && j == from.J) continue;
                    int span = Mathf.Abs(i - from.I) + Mathf.Abs(j - from.J);
                    if (span >= least) continue;
                    least = span;
                    best = new Spot(i, j);
                }
            return least == int.MaxValue ? (Spot?)null : best;
        }

        /// <summary>
        /// Joins two cells with a walk, and refuses to do it along the fence.
        ///
        /// Every route offered is a Manhattan one - one turn where the two share neither row
        /// nor column, two where a dogleg is the only way round (two mouths facing each other
        /// on opposite sides, a cell apart, cannot be joined by a single turn without running
        /// the whole length of one fence). The routes are tried in a shuffled order and the
        /// first one that keeps off the fence wins, so the same park is the same park and two
        /// parks are not the same shape.
        /// </summary>
        static void Join(Plan plan, Spot from, Spot to, System.Random rng)
        {
            var routes = new List<List<Spot>>();
            routes.Add(Elbow(from, to, true));
            routes.Add(Elbow(from, to, false));

            // the doglegs: turn early, run across, turn back. Offered at every line between
            // the two, which is what lets a route dodge the fence a plain elbow would hug
            for (int k = plan.I0 + 1; k <= plan.I1 - 1; k++) routes.Add(Dogleg(from, to, k, true));
            for (int k = plan.J0 + 1; k <= plan.J1 - 1; k++) routes.Add(Dogleg(from, to, k, false));

            Dice.Shuffle(routes, rng);
            // a route that never leaves the middle of the park beats one that only just
            // clears the fence, and the shuffle above decides between equals
            List<Spot> chosen = null;
            foreach (var route in routes)
            {
                if (route == null || !Clear(plan, route, from, to)) continue;
                chosen = route;
                break;
            }
            if (chosen == null)
            {
                // Nothing kept off the fence: the ground is too thin to route through (a
                // three-cell strip, where the middle IS the fence line). The plain elbow goes
                // down anyway - a walk against the fence is a fault worth seeing in the map,
                // and far better than a park with no way through it.
                chosen = Elbow(from, to, true);
                plan.Notes.Add($"the walk from {from} to {to} could not keep off the fence");
            }
            foreach (var cell in chosen) Walk(plan, cell);
        }

        /// <summary>One turn: along x then along z, or the other way about.</summary>
        static List<Spot> Elbow(Spot from, Spot to, bool acrossFirst)
        {
            var cells = new List<Spot>();
            var at = from;
            cells.Add(at);
            if (acrossFirst)
            {
                while (at.I != to.I) { at = new Spot(at.I + Sign(to.I - at.I), at.J); cells.Add(at); }
                while (at.J != to.J) { at = new Spot(at.I, at.J + Sign(to.J - at.J)); cells.Add(at); }
            }
            else
            {
                while (at.J != to.J) { at = new Spot(at.I, at.J + Sign(to.J - at.J)); cells.Add(at); }
                while (at.I != to.I) { at = new Spot(at.I + Sign(to.I - at.I), at.J); cells.Add(at); }
            }
            return cells;
        }

        /// <summary>Two turns: out to a line, along it, and in again.</summary>
        static List<Spot> Dogleg(Spot from, Spot to, int at, bool acrossFirst)
        {
            var cells = new List<Spot>();
            var here = from;
            cells.Add(here);
            if (acrossFirst)
            {
                if ((at - from.I) * (at - to.I) > 0) return null;     // the line is not between them
                while (here.I != at) { here = new Spot(here.I + Sign(at - here.I), here.J); cells.Add(here); }
                while (here.J != to.J) { here = new Spot(here.I, here.J + Sign(to.J - here.J)); cells.Add(here); }
                while (here.I != to.I) { here = new Spot(here.I + Sign(to.I - here.I), here.J); cells.Add(here); }
            }
            else
            {
                if ((at - from.J) * (at - to.J) > 0) return null;
                while (here.J != at) { here = new Spot(here.I, here.J + Sign(at - here.J)); cells.Add(here); }
                while (here.I != to.I) { here = new Spot(here.I + Sign(to.I - here.I), here.J); cells.Add(here); }
                while (here.J != to.J) { here = new Spot(here.I, here.J + Sign(to.J - here.J)); cells.Add(here); }
            }
            return cells;
        }

        static int Sign(int of) => of > 0 ? 1 : of < 0 ? -1 : 0;

        /// <summary>Does this route keep off the fence? Its ENDS are allowed there - they are
        /// the mouths, or a cell of walk already standing - and nothing in between is.</summary>
        static bool Clear(Plan plan, List<Spot> route, Spot from, Spot to)
        {
            foreach (var cell in route)
            {
                if (!plan.Inside(cell)) return false;
                if (cell.Equals(from) || cell.Equals(to)) continue;
                if (plan.OnFence(cell.I, cell.J) && !plan.Walked(cell)) return false;
            }
            return true;
        }

        static void Walk(Plan plan, Spot at)
        {
            if (!plan.Inside(at)) return;
            if (plan.Cells[at.I, at.J] == Ground.Grass) plan.Cells[at.I, at.J] = Ground.Walk;
        }

        // --------------------------------------------------------------------- the rooms

        /// <summary>Whatever the walk left over, cut into 4-connected pieces. Found, not laid
        /// out: a room is a piece of ground you can stand in and see the edges of, and that
        /// is exactly what the walk leaves behind.</summary>
        static void Rooms(Plan plan)
        {
            plan.Rooms.Clear();
            var far = Reaches(plan);
            var seen = new bool[plan.NX, plan.NZ];
            for (int i = plan.I0; i <= plan.I1; i++)
                for (int j = plan.J0; j <= plan.J1; j++)
                {
                    if (seen[i, j] || plan.Cells[i, j] != Ground.Grass) continue;
                    var room = new Room { I0 = i, J0 = j, W = 1, D = 1 };
                    var queue = new Queue<Spot>();
                    queue.Enqueue(new Spot(i, j));
                    seen[i, j] = true;
                    int iMin = i, iMax = i, jMin = j, jMax = j;
                    while (queue.Count > 0)
                    {
                        var at = queue.Dequeue();
                        room.Cells.Add(at);
                        if (plan.OnFence(at.I, at.J)) room.OnFence = true;
                        int reach = far[at.I, at.J];
                        room.Reach = reach < 0 ? int.MaxValue / 2 : Mathf.Max(room.Reach, reach);
                        iMin = Mathf.Min(iMin, at.I); iMax = Mathf.Max(iMax, at.I);
                        jMin = Mathf.Min(jMin, at.J); jMax = Mathf.Max(jMax, at.J);
                        foreach (var step in Steps)
                        {
                            var next = Step(at, step);
                            if (!plan.Inside(next) || seen[next.I, next.J]) continue;
                            if (plan.Cells[next.I, next.J] != Ground.Grass) continue;
                            seen[next.I, next.J] = true;
                            queue.Enqueue(next);
                        }
                    }
                    room.I0 = iMin; room.J0 = jMin;
                    room.W = iMax - iMin + 1; room.D = jMax - jMin + 1;
                    Inner(room);
                    plan.Rooms.Add(room);
                }
        }

        /// <summary>
        /// The biggest rectangle of cells lying wholly inside a room.
        ///
        /// The standard histogram sweep: for each row, how many cells of the room stand
        /// unbroken above each column, then the largest rectangle under that skyline. Rooms
        /// here are a few hundred cells at most, so the cost of it does not signify - and
        /// nothing cheaper is honest about a ring or an L.
        /// </summary>
        static void Inner(Room room)
        {
            var has = new bool[room.W, room.D];
            foreach (var cell in room.Cells) has[cell.I - room.I0, cell.J - room.J0] = true;

            var high = new int[room.W];
            int best = 0;
            for (int j = 0; j < room.D; j++)
            {
                for (int i = 0; i < room.W; i++) high[i] = has[i, j] ? high[i] + 1 : 0;

                // the largest rectangle under this row's skyline, by the usual stack sweep
                var stack = new Stack<int>();
                for (int i = 0; i <= room.W; i++)
                {
                    int tall = i == room.W ? 0 : high[i];
                    while (stack.Count > 0 && high[stack.Peek()] >= tall)
                    {
                        int top = stack.Pop();
                        int from = stack.Count == 0 ? 0 : stack.Peek() + 1;
                        int wide = i - from, deep = high[top];
                        if (wide * deep <= best) continue;
                        best = wide * deep;
                        room.InnerI = room.I0 + from;
                        room.InnerJ = room.J0 + j - deep + 1;
                        room.InnerW = wide;
                        room.InnerD = deep;
                    }
                    stack.Push(i);
                }
            }
        }

        /// <summary>
        /// How far every cell of grass is from the nearest walk, in cells - one sweep over
        /// the whole park, so a room only has to read off its own worst.
        ///
        /// -1 where no walk can be reached at all, which is a park with no path in it.
        /// </summary>
        static int[,] Reaches(Plan plan)
        {
            var far = new int[plan.NX, plan.NZ];
            for (int i = 0; i < plan.NX; i++)
                for (int j = 0; j < plan.NZ; j++) far[i, j] = -1;

            var queue = new Queue<Spot>();
            for (int i = plan.I0; i <= plan.I1; i++)
                for (int j = plan.J0; j <= plan.J1; j++)
                {
                    if (!plan.Walked(i, j)) continue;
                    far[i, j] = 0;
                    queue.Enqueue(new Spot(i, j));
                }
            while (queue.Count > 0)
            {
                var at = queue.Dequeue();
                foreach (var step in Steps)
                {
                    var next = Step(at, step);
                    if (!plan.Inside(next) || far[next.I, next.J] >= 0) continue;
                    far[next.I, next.J] = far[at.I, at.J] + 1;
                    queue.Enqueue(next);
                }
            }
            return far;
        }

        /// <summary>
        /// Cuts a room too big to read as one place, by running a walk through the middle of
        /// it from one piece of walk to another.
        ///
        /// The cut has to LAND on the walk at both ends. A cut that ends at the fence is a
        /// dead end, and a park where a path stops at a railing is a park somebody built
        /// wrong; a cut that ends nowhere leaves the room in two halves with no way between
        /// them. Where no line through the room reaches walk at both ends the room is left
        /// alone and said so - the note is what a big empty lawn would otherwise never say.
        /// </summary>
        static void Cut(Plan plan, System.Random rng)
        {
            // as many cuts as the ground can want, not a flat forty: a park of 270 m comes out
            // as forty-odd rooms and spent the whole allowance before it reached the last two,
            // which then failed the verdict for being uncut. One cut can only ever make one
            // more room, so the ground's own cell count is the honest ceiling.
            int allowed = Mathf.Max(40, plan.W * plan.D / 8);
            for (int guard = 0; guard < allowed; guard++)
            {
                Room worst = null;
                foreach (var room in plan.Rooms)
                {
                    if (!room.TooBig) continue;
                    if (worst == null || room.Area > worst.Area) worst = room;
                }
                if (worst == null) return;
                if (!Halve(plan, worst, rng))
                {
                    plan.Notes.Add($"a room of {worst.W * 5} x {worst.D * 5} m could not be cut " +
                                   "in two: no line through it reaches the walk at both ends");
                    // marked so the search moves on rather than trying it for ever - and the
                    // mark is a flag rather than a smaller size, so the verdict still sees
                    // the room for what it is
                    worst.Uncut = true;
                    continue;
                }
                var uncut = new List<Room>();
                foreach (var room in plan.Rooms) if (room.Uncut) uncut.Add(room);
                Rooms(plan);
                // a room already given up on keeps its mark through the re-reckoning: the
                // cells are the same ground, so the same piece is found again
                foreach (var room in plan.Rooms)
                    foreach (var older in uncut)
                        if (room.Cells.Count == older.Cells.Count && room.I0 == older.I0 &&
                            room.J0 == older.J0) room.Uncut = true;
            }
        }

        /// <summary>Runs one walk across a room, along whichever axis is longer, at whichever
        /// line nearest the middle reaches walk at both ends.</summary>
        static bool Halve(Plan plan, Room room, System.Random rng)
        {
            bool across = room.W >= room.D;                  // cut the long way
            for (int pass = 0; pass < 2; pass++, across = !across)
            {
                int lo = across ? room.I0 : room.J0;
                int hi = across ? room.I0 + room.W - 1 : room.J0 + room.D - 1;
                int mid = (lo + hi) / 2;
                for (int step = 0; step <= (hi - lo) / 2; step++)
                    for (int way = 0; way < 2; way++)
                    {
                        int line = way == 0 ? mid - step : mid + step;
                        if (line <= lo || line >= hi) continue;
                        var cut = Slice(plan, room, line, across, out var gates);
                        if (cut == null) continue;
                        foreach (var cell in cut) Walk(plan, cell);
                        // the gates go in only now the whole cut is going down. Opened as
                        // each end was worked out, a cut whose FAR end then failed left a
                        // gate behind with one cell of walk at it and nothing joined to it
                        foreach (var gate in gates) { plan.Mouths.Add(gate); Walk(plan, gate.At); }
                        return true;
                    }
            }
            return false;
        }

        /// <summary>
        /// One line through a room, grown outwards from the room until it meets walk on both
        /// sides. Null if either end runs into the fence instead.
        /// </summary>
        static List<Spot> Slice(Plan plan, Room room, int line, bool across, out List<Mouth> gates)
        {
            gates = new List<Mouth>();
            var cells = new List<Spot>();
            int from = across ? room.J0 : room.I0;
            int to = across ? room.J0 + room.D - 1 : room.I0 + room.W - 1;

            for (int k = from; k <= to; k++)
            {
                var at = across ? new Spot(line, k) : new Spot(k, line);
                if (!plan.Inside(at)) return null;
                if (plan.Cells[at.I, at.J] == Ground.Grass) cells.Add(at);
                else if (!plan.Walked(at)) return null;
            }
            if (cells.Count == 0) return null;

            // and out of the room at both ends, until it reaches the walk
            for (int way = 0; way < 2; way++)
            {
                int step = way == 0 ? -1 : 1;
                // the walk out of the room starts at the room's own last cell, not past it:
                // a room that already reaches the fence has nowhere to step to, and reading
                // the gate off the cell OUTSIDE the park refused every one of them
                var at = across ? new Spot(line, way == 0 ? from : to)
                                : new Spot(way == 0 ? from : to, line);
                bool met = false;
                Spot last = at;
                for (int guard = 0; guard < 64; guard++)
                {
                    if (!plan.Inside(at)) break;                  // ran into the fence
                    if (plan.Walked(at)) { met = true; break; }
                    if (!cells.Contains(at)) cells.Add(at);
                    last = at;
                    at = across ? new Spot(line, at.J + step) : new Spot(at.I + step, line);
                }
                // A CUT THAT RUNS INTO THE FENCE MAY PUT A GATE THERE, which is how a real
                // park solves the same problem: a lawn against the railings gets its own way
                // in rather than being left as ground nobody crosses. Without this, every
                // room with its back to the fence was uncuttable and 68 of 676 sizes came out
                // with an empty quarter.
                // ONE new gate to a cut, and no more. A cut that opened a gate at BOTH ends
                // is a perfectly good path from one street to another - and it never touches
                // the rest of the walk, so the park comes out with two separate paths in it
                // that a person can only get between by leaving. The far end has to reach
                // walk that is already there, which is what keeps the whole thing one piece.
                if (!met && gates.Count == 0)
                {
                    var gate = Gate(plan, last, across ? (step < 0 ? Side.South : Side.North)
                                                       : (step < 0 ? Side.West : Side.East), gates);
                    if (gate != null) { gates.Add(gate); met = true; }
                }
                if (!met) return null;
            }
            return cells;
        }

        /// <summary>How far apart two gates on the same side have to be, in cells. Block-08
        /// has one to a side, but block-08 is thirty metres square; a park of a hundred and
        /// fifty metres with one gate a side is a park people walk round rather than
        /// through.</summary>
        const int GateApart = 4;

        /// <summary>
        /// The gate a cut could open at this end of the fence, or null: it has to be a side
        /// with a street outside it, off the corner, and clear of the gates already there -
        /// the ones the park has and the ones this same cut is opening at its other end.
        ///
        /// It only WORKS OUT the gate. Opening it is the caller's, once the whole cut is
        /// known to be going down.
        /// </summary>
        static Mouth Gate(Plan plan, Spot at, Side side, List<Mouth> pending)
        {
            if (!plan.Inside(at) || !plan.OnFence(at.I, at.J)) return null;
            if (plan.Sides[(int)side].Rim != Rim.Kerb) return null;

            // it has to be ON that side, not merely near it - a cut running south ends on the
            // south fence, and nowhere else
            var mine = Face(plan, side, side == Side.South || side == Side.North ? at.I : at.J);
            if (!mine.Equals(at)) return null;

            int along = side == Side.South || side == Side.North ? at.I : at.J;
            if (along <= Low(plan, side) || along >= High(plan, side)) return null;   // the corner

            for (int k = 0; k < plan.Mouths.Count + pending.Count; k++)
            {
                var mouth = k < plan.Mouths.Count ? plan.Mouths[k] : pending[k - plan.Mouths.Count];
                if (mouth.Side != side) continue;
                int other = side == Side.South || side == Side.North ? mouth.At.I : mouth.At.J;
                if (Mathf.Abs(other - along) < GateApart) return null;
            }
            return new Mouth { Side = side, At = at, OnCrossing = false };
        }

        // ------------------------------------------------------------------- the casting

        /// <summary>
        /// What goes in each room.
        ///
        /// A rule, not a roll, and in this order: the lawn takes the biggest room, because a
        /// park with nowhere to lie down is a plaza; then the loud programme - the fountain,
        /// the playground, the courts - takes the room by the busiest mouth, where people
        /// arriving can see it; then everything else fills what is left, biggest room first,
        /// and anything that will not fit is left out and reported.
        ///
        /// A pocket park gets no programme at all. Block-08 is thirty metres square with a
        /// walk, some benches and a picnic table on it, and that is the whole of it.
        /// </summary>
        static void Cast(Plan plan, System.Random rng)
        {
            if (plan.Rooms.Count == 0) return;

            var rooms = new List<Room>(plan.Rooms);
            rooms.Sort((one, other) => other.Area.CompareTo(one.Area));
            foreach (var room in rooms) room.Programme = Programme.Lawn;

            if (plan.Klass == Klass.Pocket) return;

            // the lawn keeps the biggest room, and it is spent - so the loud programme lands
            // somewhere else, which is what stops a park being one enormous feature and
            // nothing to look at it from
            int first = rooms.Count > 1 ? 1 : 0;

            var wanted = new List<Programme>();
            var loud = new[] { Programme.Fountain, Programme.Playground, Programme.Courts };
            wanted.Add(loud[rng.Next(loud.Length)]);
            if (plan.Klass != Klass.Square)
            {
                foreach (var other in loud) if (!wanted.Contains(other)) wanted.Add(other);
                wanted.Add(Programme.Grove);
                wanted.Add(Programme.Pavilion);
                wanted.Add(Programme.Statue);
                wanted.Add(Programme.Grove);
                wanted.Add(Programme.Toilet);
            }

            // THE LAWN IS WHAT IS LEFT OVER, not what is dealt first. Giving it the biggest
            // room up front sounded right and was not: on an 80 x 70 m park the biggest room
            // is the only one a fountain or a set of courts would fit in, so the park came
            // out with a pavilion, a monument, a toilet block and four lawns. A park's open
            // grass is its belt and its corners, which no programme wants anyway - and one
            // room is always held back below, so there is never a park without any.
            var spent = new HashSet<Room>();

            foreach (var want in wanted)
            {
                // one room always stays grass, however many programmes are queued
                if (spent.Count >= rooms.Count - 1) break;
                Smallest(want, out int w, out int d);
                // THE SMALLEST ROOM IT FITS IN, not the next one down the list. Handed the
                // rooms in order of size, a toilet block 10 x 5 m took a room of 10 x 75 m
                // and the courts had nowhere left to go; a programme wants the ground it
                // needs and the big rooms want to stay big, for the lawns and the groves.
                Room best = null;
                foreach (var room in rooms)
                {
                    if (spent.Contains(room)) continue;
                    // measured against the ground the room actually OFFERS - the biggest
                    // rectangle inside it - and sideways as well, because a room is a piece
                    // of ground and not a plan facing one way
                    bool fits = (room.InnerW >= w && room.InnerD >= d) ||
                                (room.InnerW >= d && room.InnerD >= w);
                    if (!fits) continue;
                    if (best == null || room.Area < best.Area) best = room;
                }
                if (best == null)
                {
                    // a park with every room already spoken for is a FULL park, not a park
                    // that refused something - only a park with ground going spare and
                    // nothing that fits it is worth saying out loud
                    if (spent.Count < rooms.Count)
                        plan.Notes.Add($"no room for the {Words(want)}: the ground left over " +
                                       "is the wrong shape for it");
                    continue;
                }
                best.Programme = want;
                spent.Add(best);
            }
        }

        /// <summary>
        /// The paved ground a fountain or a monument stands on, and the walk that reaches it.
        ///
        /// Three cells by three, in the middle of its room, and if the room's middle does not
        /// touch the walk a few cells of walk are laid to it - a fountain you cannot get to
        /// dry-shod is an ornament in a field.
        /// </summary>
        static void Plaza(Plan plan, System.Random rng)
        {
            foreach (var room in plan.Rooms)
            {
                if (room.Programme != Programme.Fountain && room.Programme != Programme.Statue) continue;

                var middle = room.Middle;
                var reach = Nearest(plan, middle, true);
                // the fountain is nearly five metres across and wants a 15 m apron with room
                // to sit round it; a monument is one metre and would stand in the middle of a
                // stone square three times its own height across
                int half = room.Programme == Programme.Fountain ? 1 : 0;
                var paved = new List<Spot>();
                for (int i = middle.I - half; i <= middle.I + half; i++)
                    for (int j = middle.J - half; j <= middle.J + half; j++)
                    {
                        if (!plan.Inside(i, j) || plan.Cells[i, j] != Ground.Grass) continue;
                        if (plan.OnFence(i, j)) continue;
                        paved.Add(new Spot(i, j));
                    }
                if (paved.Count == 0) continue;
                foreach (var cell in paved) plan.Cells[cell.I, cell.J] = Ground.Plaza;

                // and a way to it, if the apron does not already touch the walk. The walk it
                // is joined to is the one found BEFORE the apron went down - paving counts as
                // walkable, so afterwards the nearest walk to a fountain is the fountain
                if (Touches(plan, paved) || !reach.HasValue) continue;
                Join(plan, middle, reach.Value, rng);
            }
        }

        static bool Touches(Plan plan, List<Spot> cells)
        {
            foreach (var cell in cells)
                foreach (var step in Steps)
                {
                    var next = Step(cell, step);
                    if (plan.Inside(next) && plan.Cells[next.I, next.J] == Ground.Walk) return true;
                }
            return false;
        }

        // ------------------------------------------------------------------- the verdict

        /// <summary>
        /// What is wrong with the plan, counted rather than eyeballed - the composer's
        /// counterpart to the raster's report on the roads, and nought is the only passing
        /// answer.
        ///
        ///   * a walk that is not all one piece, so part of the park cannot be reached;
        ///   * a mouth the walk never reached;
        ///   * a dead end that is not a mouth (the pocket park's single stub excepted);
        ///   * walk standing against the fence anywhere but at a mouth;
        ///   * a room the walk never got near enough to, or never cut in two.
        /// </summary>
        public static string Report(Plan plan, out int faults)
        {
            var trouble = new List<string>();
            faults = 0;

            var walk = new List<Spot>();
            for (int i = plan.I0; i <= plan.I1; i++)
                for (int j = plan.J0; j <= plan.J1; j++)
                    if (plan.Walked(i, j)) walk.Add(new Spot(i, j));

            if (walk.Count == 0)
            {
                trouble.Add("there is no walk at all");
                faults++;
            }
            else
            {
                int reached = Flood(plan, walk[0]);
                if (reached != walk.Count)
                {
                    trouble.Add($"the walk is in pieces: {reached} of {walk.Count} cells are joined up");
                    faults++;
                }
            }

            var mouths = new HashSet<Spot>();
            foreach (var mouth in plan.Mouths)
            {
                mouths.Add(mouth.At);
                if (plan.Walked(mouth.At)) continue;
                trouble.Add($"the mouth on the {mouth.Side.ToString().ToLowerInvariant()} at " +
                            $"{mouth.At} has no walk on it");
                faults++;
            }

            int stubs = 0, onFence = 0;
            foreach (var cell in walk)
            {
                // AN APRON IS A DESTINATION, not a dead end. A path that runs to a monument
                // and stops there is a path that arrived; counted as a stub, a one-cell apron
                // failed 128 of 676 sizes for doing exactly what it is for.
                if (plan.Cells[cell.I, cell.J] == Ground.Plaza) continue;
                int neighbours = 0;
                foreach (var step in Steps) if (plan.Walked(Step(cell, step))) neighbours++;
                if (neighbours <= 1 && !mouths.Contains(cell)) stubs++;
                if (plan.OnFence(cell.I, cell.J) && !mouths.Contains(cell)) onFence++;
            }
            // the pocket park is allowed exactly one stub, which is what block-08 has
            int allowed = plan.Klass == Klass.Pocket ? 1 : 0;
            if (stubs > allowed)
            {
                trouble.Add($"{stubs} dead end(s) in the walk that are not a way out");
                faults += stubs - allowed;
            }
            if (onFence > 0)
            {
                trouble.Add($"{onFence} cell(s) of walk stand against the fence away from a gate");
                faults += onFence;
            }

            int big = 0;
            foreach (var room in plan.Rooms)
                if (room.TooBig || room.Uncut) big++;
            if (big > 0)
            {
                trouble.Add($"{big} room(s) with ground more than {ReachMax * 5} m from a path, " +
                            "or twice as long as they are wide, were never cut");
                faults += big;
            }

            var sb = new System.Text.StringBuilder();
            sb.Append($"{plan.Name}: {plan.Mouths.Count} way(s) in, {walk.Count} cell(s) of walk, " +
                      $"{plan.Rooms.Count} room(s) ({Cast(plan)})");
            foreach (var line in trouble) sb.Append('\n').Append("   WARNING: ").Append(line);
            foreach (var note in plan.Notes) sb.Append('\n').Append("   note: ").Append(note);
            return sb.ToString();
        }

        /// <summary>How many cells of walk are joined to this one.</summary>
        static int Flood(Plan plan, Spot from)
        {
            var seen = new HashSet<Spot>();
            var queue = new Queue<Spot>();
            queue.Enqueue(from);
            seen.Add(from);
            while (queue.Count > 0)
            {
                var at = queue.Dequeue();
                foreach (var step in Steps)
                {
                    var next = Step(at, step);
                    if (!plan.Walked(next) || seen.Contains(next)) continue;
                    seen.Add(next);
                    queue.Enqueue(next);
                }
            }
            return seen.Count;
        }

        /// <summary>How the park was cast, for the log: how many rooms of each programme.</summary>
        public static string Cast(Plan plan)
        {
            var count = new Dictionary<Programme, int>();
            foreach (var room in plan.Rooms)
                count[room.Programme] = count.TryGetValue(room.Programme, out var c) ? c + 1 : 1;
            var parts = new List<string>();
            foreach (Programme what in Enum.GetValues(typeof(Programme)))
                if (count.TryGetValue(what, out var many)) parts.Add($"{many} {Words(what)}");
            return parts.Count == 0 ? "nothing" : string.Join(", ", parts);
        }

        // --------------------------------------------------------------------- the tiles

        /// <summary>Which piece of the pack's path set a cell of walk wants, and how it is
        /// turned. Measured off block-08's own six tiles: a straight along x is at 90, a
        /// straight along z at 0, the corner joining north and east at 90, and the T missing
        /// its east arm at 270. Everything else follows by quarter turns.</summary>
        public enum Piece { Straight, Corner, Tee, Junction }

        /// <summary>
        /// The tile for a cell of walk, from its neighbours alone.
        ///
        /// A MOUTH COUNTS AS A NEIGHBOUR - the walk carries on out onto the pavement, so the
        /// tile at a gate is a straight and not an end. This is what makes the whole walk
        /// layable with four pieces: the pack has no end cap, and block-08 does not use one.
        /// </summary>
        public static void Tile(Plan plan, Spot at, out Piece piece, out int yaw)
        {
            bool north = Linked(plan, at, 0, 1), south = Linked(plan, at, 0, -1);
            bool east = Linked(plan, at, 1, 0), west = Linked(plan, at, -1, 0);
            int arms = (north ? 1 : 0) + (south ? 1 : 0) + (east ? 1 : 0) + (west ? 1 : 0);

            if (arms >= 4) { piece = Piece.Junction; yaw = 0; return; }
            if (arms == 3)
            {
                piece = Piece.Tee;
                // the T at yaw 0 is missing its SOUTH arm; a quarter turn takes the gap round
                yaw = !south ? 0 : !west ? 90 : !north ? 180 : 270;
                return;
            }
            if (arms == 2 && north == south && east == west)
            {
                // two arms in line: a straight, along z at 0 and along x at 90
                piece = Piece.Straight;
                yaw = north ? 0 : 90;
                return;
            }
            if (arms == 2)
            {
                piece = Piece.Corner;
                // the corner at yaw 0 joins WEST and NORTH; +90 takes each arm round one
                yaw = west && north ? 0 : north && east ? 90 : east && south ? 180 : 270;
                return;
            }
            // one arm or none: a straight lying the way the arm points, which is what the
            // pack's own dead end is
            piece = Piece.Straight;
            yaw = north || south ? 0 : 90;
        }

        /// <summary>
        /// Which ways the walk leaves a cell - what the tile is chosen from, and what anything
        /// wanting to keep OFF the path has to know.
        ///
        /// The cell is five metres and the path through it is not: the pack's tile is grass
        /// with a made way up the middle of it, which is why block-08 has a lamp and a dozen
        /// flowers standing on cells that are walk. Whoever is booking ground reads the arms
        /// and books the way, not the cell.
        /// </summary>
        public static void Arms(Plan plan, Spot at, out bool north, out bool south,
                                out bool east, out bool west)
        {
            north = Linked(plan, at, 0, 1);
            south = Linked(plan, at, 0, -1);
            east = Linked(plan, at, 1, 0);
            west = Linked(plan, at, -1, 0);
        }

        /// <summary>How wide the made way is through a cell of walk, in metres - measured off
        /// the pack's own path tile.</summary>
        public const float WalkWide = 2.6f;

        /// <summary>Does the walk carry on this way - into another cell of walk, or out
        /// through a gate?</summary>
        static bool Linked(Plan plan, Spot at, int di, int dj)
        {
            var next = new Spot(at.I + di, at.J + dj);
            if (plan.Walked(next)) return true;
            if (plan.Inside(next)) return false;
            foreach (var mouth in plan.Mouths)
                if (mouth.At.Equals(at)) return Leaves(mouth.Side, di, dj);
            return false;
        }

        static bool Leaves(Side side, int di, int dj) =>
            side == Side.South ? dj < 0 : side == Side.North ? dj > 0
          : side == Side.West ? di < 0 : di > 0;
    }
}
