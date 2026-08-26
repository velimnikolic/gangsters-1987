using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RoadDemo
{
    /// <summary>
    /// How a residential block is divided: which unit stands on which corner, what fills the
    /// runs between them, and what the ground behind them is for.
    ///
    /// Plain C# with no Unity in it, so <c>Tools/CoreSim</c> can deal thirty seeds of every
    /// class and judge them with no editor open - the same bargain <see cref="ParkWalk"/>
    /// struck. What stands the plan up is <c>ResidentialBlocks</c>; what measures the
    /// buildings is <c>ResidentialHarvest</c>, and it writes <see cref="ResidentialUnits"/>.
    ///
    /// The rules are the approved plan (<c>Docs/residential-blocks-plan.md</c> §1):
    /// the corner carries a building, the edge between corners is a row WITH GAPS rather
    /// than a wall, a unit is never cut, and ground the recipe has nothing to put on is left
    /// empty AND REPORTED rather than filled with something invented.
    /// </summary>
    public static class ResidentialLot
    {
        public const int Cell = 5;

        /// <summary>The pavement the block carries round itself, in cells. One tile, 5 m -
        /// the user's call of 2026-08-26: a brownstone already keeps a garden behind its
        /// railings, and ten metres of pavement beside that is a boulevard.</summary>
        public const int Walk = 1;

        public enum Klass { Corner, Row, Block, Court }

        /// <summary>South, east, north, west.</summary>
        public static readonly int[,] Step = { { 0, -1 }, { 1, 0 }, { 0, 1 }, { -1, 0 } };
        public static readonly string[] SideName = { "S", "E", "N", "W" };

        public enum Use : byte
        {
            /// <summary>Nothing: the recipe had nothing for it. Counted and located.</summary>
            Empty,
            Walkway,     // the block's own pavement ring
            Building,    // a unit stands here
            Forecourt,   // a unit's own stoop, pit or garden
            Yard,        // back yard: grass, fences, washing lines
            Paved,       // a gap in the row, paved - never grass (the user's call, 2026-08-26)
            Verge,       // pavement INSIDE the block, edging a way cars use
            Drive,       // the way in off the street: asphalt, right through the kerb line
            Parking,     // a tooth pulled out of the row: bays behind a chain
            Alley,       // 5 m one-way, bins against the backs
            Court,       // the courtyard park a big block keeps in the middle
            Cafe,        // a kit storefront in a gap, fronting the artery
        }

        // ------------------------------------------------------------------ the plan

        public sealed class Spot
        {
            public ResidentialUnit Unit;
            public int Yaw;                 // 0, 90, 180, 270
            public int I, J;                // its SW cell in the block
            public int CW, CD;              // turned
            public int Side = -1;           // the street it fronts, if it was put on an edge
            public bool Shop;               // it carries the block's shopfronts
            public override string ToString() => $"{Unit.Name}@{Yaw} ({I},{J}) {CW}x{CD}";
        }

        public sealed class Measures
        {
            public int Units, Doors, Shops, Cafes, Trees;
            public int Gaps, GapCells, Paved, Drives, Parking, AlleyCells, CourtCells, Verge;
            public int Empty, Pits, Repeats;
            public double DoorsPerHa;
            public string EmptyAt = "";
        }

        public sealed class Plan
        {
            public int W, D;                       // the whole block, pavement ring included
            public Klass Klass;
            public bool[] Street = new bool[4];    // which sides have a road along them
            public int Artery = -1;                // the side the shops look at
            public int Seed;
            public List<Spot> Spots = new List<Spot>();
            public Use[,] Ground;
            public List<string> Faults = new List<string>();
            public List<string> Refused = new List<string>();
            public Measures M = new Measures();

            public int Inner => W - 2 * Walk;
            public int InnerD => D - 2 * Walk;
            public bool Clean => Faults.Count == 0;
        }

        // ------------------------------------------------------------------ turning a unit

        /// <summary>A unit read through a quarter turn: its footprint and its faces in the
        /// block's frame. Rotation is the only freedom a unit has - no mirroring (Synty
        /// mirrors its own tiles; we never do) and no scaling.</summary>
        public sealed class Turn
        {
            public ResidentialUnit Unit;
            public int Yaw, CW, CD;

            public static Turn Of(ResidentialUnit unit, int yaw)
            {
                yaw = ((yaw % 360) + 360) % 360;
                bool swap = yaw == 90 || yaw == 270;
                return new Turn
                {
                    Unit = unit,
                    Yaw = yaw,
                    CW = swap ? unit.CD : unit.CW,
                    CD = swap ? unit.CW : unit.CD,
                };
            }

            /// <summary>The cell of the unturned unit that lands on (i, j) of the turned one.
            /// A quarter turn clockwise carries the south-west corner to the north-west.
            ///
            /// CLOCKWISE, the way Unity's <c>Euler(0, yaw, 0)</c> turns the prefab when
            /// <c>ResidentialBlocks.Stand</c> stands it: north goes to east, so the turned
            /// cell (i, j) took the unturned (CW - 1 - j, i). The first cut had the two
            /// quarter turns the other way round - a plan that was consistent with itself
            /// and stood every L-wing at yaw 90 or 270 in the cells it had marked empty,
            /// with its street faces looking at the yard.</summary>
            void Back(int i, int j, out int u, out int v)
            {
                switch (Yaw)
                {
                    case 90: u = Unit.CW - 1 - j; v = i; break;
                    case 180: u = Unit.CW - 1 - i; v = Unit.CD - 1 - j; break;
                    case 270: u = j; v = Unit.CD - 1 - i; break;
                    default: u = i; v = j; break;
                }
            }

            public bool Filled(int i, int j) { Back(i, j, out int u, out int v); return Unit.Filled(u, v); }
            public bool Wall(int i, int j) { Back(i, j, out int u, out int v); return Unit.Wall(u, v); }
            public bool Pit(int i, int j) { Back(i, j, out int u, out int v); return Unit.Pit(u, v); }

            /// <summary>The side of the block this unit's side <paramref name="side"/> looks
            /// at once turned. Clockwise: south becomes west, east becomes south.</summary>
            int From(int side) => (side + Yaw / 90) % 4;

            public bool Face(int side) => Unit.Face[From(side)];
            public int Doors(int side) => Unit.Doors[From(side)];
            public int Shops(int side) => Unit.Shops[From(side)];
            public float Over(int side) => Unit.Over[From(side)];
        }

        // ------------------------------------------------------------------ the deal

        /// <summary>What size of block each class asks for, as the INNER rectangle - the
        /// ground inside the pavement ring. Straight out of the approved plan §2.2.</summary>
        public static bool Sized(Klass klass, int w, int d) => klass switch
        {
            Klass.Corner => w >= 6 && w <= 9 && d >= 5 && d <= 8,
            Klass.Row => w >= 4 && w <= 6 && d >= 10,
            Klass.Block => w >= 10 && w <= 15 && d >= 11 && d <= 19,
            Klass.Court => w >= 16 && d >= 16,
            _ => false,
        };

        public static Klass? Classify(int w, int d)
        {
            // the long way round either way: a row is a row whichever axis it lies on
            foreach (Klass k in Enum.GetValues(typeof(Klass)))
                if (Sized(k, w, d) || Sized(k, d, w)) return k;
            return null;
        }

        /// <summary>A block of this size, dealt from this seed. Streets on every side unless
        /// told otherwise; the artery is the side the shops are allowed to look at.</summary>
        public static Plan Roll(int w, int d, int seed, int artery = 0, bool[] streets = null)
        {
            var plan = new Plan { W = w, D = d, Seed = seed, Artery = artery };
            for (int s = 0; s < 4; s++) plan.Street[s] = streets == null || streets[s];
            plan.Ground = new Use[w, d];

            var klass = Classify(plan.Inner, plan.InnerD);
            if (klass == null)
            {
                plan.Faults.Add($"NoRecipe: {plan.Inner}x{plan.InnerD} cells " +
                                $"({plan.Inner * Cell}x{plan.InnerD * Cell} m) is no class this recipe knows");
                return plan;
            }
            plan.Klass = klass.Value;

            for (int i = 0; i < w; i++)
                for (int j = 0; j < d; j++)
                    if (i < Walk || j < Walk || i >= w - Walk || j >= d - Walk)
                        plan.Ground[i, j] = Use.Walkway;

            var rng = new Random(unchecked(seed * 7919 + w * 104729 + d * 1299709));

            Declare(plan);
            Corners(plan, rng);
            Edges(plan, rng);
            Inside(plan, rng);
            Measure(plan);
            Judge(plan);
            return plan;
        }

        /// <summary>
        /// The alley is declared before anything is built, right across the block.
        ///
        /// This is the core's own lesson, and the plan says it in as many words: a way
        /// through that is fitted around the buildings afterwards is a way through that
        /// ends in somebody's yard. Dealt last, eighteen of thirty blocks had to give
        /// their alley up; declared first, the buildings simply stand either side of it
        /// and both its mouths are on the pavement by construction.
        /// </summary>
        static void Declare(Plan plan)
        {
            if (plan.Klass != Klass.Block) return;

            bool eastWest = plan.Inner >= plan.InnerD;
            int across = eastWest ? plan.D : plan.W;

            // The line has to leave a row of houses either side of it. Put down the middle
            // regardless and a block one cell too shallow gets four cells on one side for a
            // five-cell house: five of thirty blocks lost a corner to exactly that.
            int least = ResidentialUnits.All
                .Where(u => u.Kind == ResidentialKind.Corner)
                // the DEEPEST a corner unit can be made to lie: a strip that only fits the
                // shallow ones is a strip where the shop corner fits and the stoop corner
                // does not, and the block loses the corner it was going to put a house on
                .Select(u => Math.Max(u.CW, u.CD))
                .DefaultIfEmpty(5).Min();
            // and the corridor is THREE cells: pavement, road, pavement. The user's rule
            // of 2026-08-26 - a way put in between must be kerbed on every side, and no car
            // may cross a pavement to reach it - so the verges are declared with the road
            // rather than hoped for afterwards, and nothing gets built on them.
            int lo = Walk + least + 1, hi = across - Walk - 2 - least;
            if (lo > hi)
            {
                plan.Refused.Add($"alley: {across - 2 * Walk} cells across leaves no room for " +
                                 "a kerbed way and a row of houses either side");
                return;
            }
            int mid = Math.Min(hi, Math.Max(lo, (across - 1) / 2));

            if (eastWest)
                for (int i = Walk; i < plan.W - Walk; i++)
                {
                    plan.Ground[i, mid] = Use.Alley; plan.M.AlleyCells++;
                    plan.Ground[i, mid - 1] = Use.Verge;
                    plan.Ground[i, mid + 1] = Use.Verge;
                }
            else
                for (int j = Walk; j < plan.D - Walk; j++)
                {
                    plan.Ground[mid, j] = Use.Alley; plan.M.AlleyCells++;
                    plan.Ground[mid - 1, j] = Use.Verge;
                    plan.Ground[mid + 1, j] = Use.Verge;
                }

            // both ends come out through the ring, as road
            for (int end = 0; end < 2; end++)
            {
                int side = eastWest ? (end == 0 ? 3 : 1) : (end == 0 ? 0 : 2);
                int at = mid;
                var (i, j) = EdgeCell(plan, side, at);
                var (x, y) = (i + Step[side, 0], j + Step[side, 1]);
                if (x >= 0 && y >= 0 && x < plan.W && y < plan.D && plan.Ground[x, y] == Use.Walkway)
                    plan.Ground[x, y] = Use.Drive;
            }
        }

        // ------------------------------------------------------------------ the corners

        static IEnumerable<ResidentialUnit> Shops() =>
            ResidentialUnits.All.Where(u => u.Shops.Sum() > 0);

        static IEnumerable<ResidentialUnit> Stoops() =>
            ResidentialUnits.All.Where(u => u.Shops.Sum() == 0 && u.Doors.Sum() > 0);

        /// <summary>
        /// A building on every corner that looks at two streets, and only ONE of them
        /// carrying shops - the corner on the artery.
        ///
        /// The corner is dealt first because it is the hardest fit: two sides fixed, and the
        /// unit has to face both. What is left of the edge is then a run between corners,
        /// and a run is easy.
        /// </summary>
        static void Corners(Plan plan, Random rng)
        {
            int lo = Walk, hi = plan.W - Walk - 1, bo = Walk, to = plan.D - Walk - 1;
            // (i, j) of the corner cell, and the two sides it looks at
            var corners = new (int I, int J, int A, int B)[]
            {
                (lo, bo, 0, 3), (hi, bo, 0, 1), (hi, to, 2, 1), (lo, to, 2, 3),
            };

            // the artery's corners first, so the shopfront lands where the traffic is
            var order = corners
                .Select((c, n) => (c, n))
                .OrderByDescending(x => x.c.A == plan.Artery || x.c.B == plan.Artery)
                .ThenBy(x => rng.Next())
                .ToList();

            // How many corners this class is FOR. A corner block is one building and its
            // garden - the approved plan §2.2 - so asking four corners of a 35 x 30 m block
            // is asking for a building that was never going to fit, and the first sweep duly
            // refused 182 of them. A row keeps its open ends. Only a block and a court are
            // built on all round.
            int built = plan.Klass switch
            {
                Klass.Corner => 1,
                Klass.Row => 2,
                _ => 4,
            };

            bool shopTaken = false;
            int stood = 0;
            foreach (var (corner, _) in order)
            {
                if (stood >= built) break;
                if (!plan.Street[corner.A] || !plan.Street[corner.B]) continue;

                bool wantShop = !shopTaken && (corner.A == plan.Artery || corner.B == plan.Artery);
                var wants = wantShop ? Shops().ToList() : Stoops().ToList();
                if (wants.Count == 0) wants = Stoops().Concat(Shops()).ToList();

                var rest = order.Select(x => x.c).Where(c => c != corner &&
                                plan.Street[c.A] && plan.Street[c.B]).ToList();
                var spot = Fit(plan, wants, corner.I, corner.J, corner.A, corner.B, rng, rest, built - stood - 1);
                if (spot == null && wantShop)
                    spot = Fit(plan, Stoops().ToList(), corner.I, corner.J, corner.A, corner.B, rng, rest, built - stood - 1);
                if (spot == null)
                {
                    plan.Refused.Add($"corner {SideName[corner.A]}{SideName[corner.B]}: " +
                                     "no unit faces both streets and fits");
                    continue;
                }
                if (spot.Shop) shopTaken = true;
                Place(plan, spot);
                stood++;
            }
        }

        /// <summary>The biggest unit of the ones offered that fits this corner facing both
        /// its streets. Biggest, because a corner half filled is a corner with a gap on a
        /// street, and the run beside it can take the slack.</summary>
        static Spot Fit(Plan plan, List<ResidentialUnit> units, int ci, int cj, int a, int b,
                        Random rng, List<(int I, int J, int A, int B)> rest = null, int more = 0)
        {
            Spot best = null;
            int bestCells = -1;
            foreach (var unit in units.OrderBy(u => rng.Next()))
                for (int yaw = 0; yaw < 360; yaw += 90)
                {
                    var turn = Turn.Of(unit, yaw);
                    if (!turn.Face(a) || !turn.Face(b)) continue;

                    int i = ci == Walk ? ci : ci - turn.CW + 1;
                    int j = cj == Walk ? cj : cj - turn.CD + 1;
                    if (!Fits(plan, turn, i, j)) continue;

                    if (more > 0 && rest != null && !Leaves(plan, turn, i, j, rest, more)) continue;

                    int cells = turn.CW * turn.CD;
                    if (cells <= bestCells) continue;
                    bestCells = cells;
                    best = new Spot
                    {
                        Unit = unit, Yaw = yaw, I = i, J = j, CW = turn.CW, CD = turn.CD,
                        Shop = unit.Shops.Sum() > 0, Side = a,
                    };
                }
            return best;
        }

        /// <summary>
        /// Would this placement leave the block's other corners room to be built on?
        ///
        /// The corners are dealt one at a time and the biggest unit that fits wins, so the
        /// first corner of a 60 x 65 m block would take the 50 x 45 m L and the other three
        /// corners had nowhere to stand. The sweep called that BlankCorner 34 times, which
        /// is the placer taking more than its share and the judge carrying the blame.
        ///
        /// The test is the smallest unit there is: if the corners still to come cannot hold
        /// even that, this one is too greedy.
        /// </summary>
        static bool Leaves(Plan plan, Turn turn, int i, int j,
                           List<(int I, int J, int A, int B)> rest, int more)
        {
            // the smallest square a CORNER unit needs. Reading it off every unit gave 4 -
            // the width of the through-row, which can never be a corner - and a shop corner
            // one cell too wide then left the next corner 4 free cells for a 5-cell house.
            int least = ResidentialUnits.All
                .Where(u => u.Kind == ResidentialKind.Corner)
                .Select(u => Math.Max(u.CW, u.CD))
                .DefaultIfEmpty(5).Min();
            int room = 0;
            foreach (var corner in rest)
            {
                bool free = true;
                for (int u = 0; u < least && free; u++)
                    for (int v = 0; v < least && free; v++)
                    {
                        int x = corner.I + (corner.I == Walk ? u : -u);
                        int y = corner.J + (corner.J == Walk ? v : -v);
                        if (x < Walk || y < Walk || x >= plan.W - Walk || y >= plan.D - Walk) { free = false; break; }
                        if (plan.Ground[x, y] != Use.Empty) { free = false; break; }
                        int du = x - i, dv = y - j;
                        if (du >= 0 && dv >= 0 && du < turn.CW && dv < turn.CD && turn.Filled(du, dv))
                            free = false;
                    }
                if (free) room++;
            }
            return room >= more;
        }

        /// <summary>
        /// Is this side of the unit its BACK - the side opposite a frontage?
        ///
        /// A side square to the faces is an END: the gable of a terrace, a party wall where
        /// the next house would have joined on. Those stand on cross streets all over the
        /// Synty demo and all over any real city, and refusing them left the through-row
        /// with nowhere to stand at all - nine row blocks came out with nothing built on
        /// them. What must never look at a street is the BACK: yards, bins, fire escapes.
        /// </summary>
        static bool Back(Turn turn, int side) => !turn.Face(side) && turn.Face((side + 2) % 4);

        static bool Fits(Plan plan, Turn turn, int i, int j)
        {
            if (i < Walk || j < Walk || i + turn.CW > plan.W - Walk || j + turn.CD > plan.D - Walk)
                return false;
            for (int u = 0; u < turn.CW; u++)
                for (int v = 0; v < turn.CD; v++)
                    if (turn.Filled(u, v) && plan.Ground[i + u, j + v] != Use.Empty)
                        return false;
            return Fronts(plan, turn, i, j);
        }

        /// <summary>
        /// Does this placement show a face to every street it touches?
        ///
        /// Asking only about the street a unit was chosen FOR is not enough: an L set along
        /// the south edge reaches the east pavement with its short wing, and if that wing is
        /// a party wall the block gets a blank flank on a street. The first sweep called
        /// that 47 times (BackToStreet), which is the judge doing the placer's work.
        /// </summary>
        static bool Fronts(Plan plan, Turn turn, int i, int j)
        {
            for (int side = 0; side < 4; side++)
            {
                if (!plan.Street[side] || !Back(turn, side)) continue;
                for (int u = 0; u < turn.CW; u++)
                    for (int v = 0; v < turn.CD; v++)
                    {
                        if (!turn.Filled(u, v)) continue;
                        int x = i + u + Step[side, 0], y = j + v + Step[side, 1];
                        if (x < 0 || y < 0 || x >= plan.W || y >= plan.D) continue;
                        if (plan.Ground[x, y] == Use.Walkway) return false;
                    }
            }
            return true;
        }

        static void Place(Plan plan, Spot spot)
        {
            var turn = Turn.Of(spot.Unit, spot.Yaw);
            for (int u = 0; u < turn.CW; u++)
                for (int v = 0; v < turn.CD; v++)
                {
                    if (!turn.Filled(u, v)) continue;
                    plan.Ground[spot.I + u, spot.J + v] =
                        turn.Wall(u, v) ? Use.Building : Use.Forecourt;
                }
            plan.Spots.Add(spot);
        }

        // ------------------------------------------------------------------ the edges

        /// <summary>
        /// The run between two corners, filled the way the plan says: whole units while they
        /// fit, and what is left over is a PROGRAMME, not a smaller building.
        ///
        /// 1 cell is the way in off the street, 2-4 is a side garden, 5 and more is a tooth
        /// pulled out of the row - bays behind a chain. Nothing is cut to fit.
        /// </summary>
        static void Edges(Plan plan, Random rng)
        {
            // Buildings on every edge first, and the longest edge first; the programmes
            // that fill what is left come after, in their own pass.
            //
            // One pass per edge, deciding as it went, cost a 20 x 50 m row block every
            // building it had: the short south edge could take nothing, so it was given a
            // garden three cells deep, and that garden then left the long west edge - the
            // one edge the through-row DOES fit - seven cells where it needed nine. A gap
            // is what is left over, so it cannot be dealt before the thing it is left over
            // from.
            var sides = Enumerable.Range(0, 4).Where(s => plan.Street[s])
                .OrderByDescending(s => s == 0 || s == 2 ? plan.W : plan.D)
                .ThenBy(s => rng.Next()).ToList();

            foreach (int side in sides) FreeRuns(plan, side, (at, run) => Units(plan, side, at, run, rng));
            foreach (int side in sides) FreeRuns(plan, side, (at, run) => Programme(plan, side, at, run, rng));
        }

        /// <summary>Every free run along this edge, in order.</summary>
        static void FreeRuns(Plan plan, int side, Action<int, int> what)
        {
            int length = side == 0 || side == 2 ? plan.W : plan.D;
            int at = Walk;
            while (at < length - Walk)
            {
                if (Occupied(plan, side, at)) { at++; continue; }
                int run = 0;
                while (at + run < length - Walk && !Occupied(plan, side, at + run)) run++;
                what(at, run);
                at += run;
            }
        }

        /// <summary>Is the cell on this edge already spoken for?</summary>
        static bool Occupied(Plan plan, int side, int at)
        {
            var (i, j) = EdgeCell(plan, side, at);
            return plan.Ground[i, j] != Use.Empty;
        }

        static (int, int) EdgeCell(Plan plan, int side, int at) => side switch
        {
            0 => (at, Walk),
            2 => (at, plan.D - Walk - 1),
            1 => (plan.W - Walk - 1, at),
            _ => (Walk, at),
        };

        static void Units(Plan plan, int side, int at, int run, Random rng)
        {
            while (run > 0)
            {
                var spot = Longest(plan, side, at, run, rng);
                if (spot == null) break;
                Place(plan, spot);
                int took = side == 0 || side == 2 ? spot.CW : spot.CD;
                at += took;
                run -= took;
            }
        }

        /// <summary>The longest unit that fits this run facing this street. Longest first,
        /// so the leftovers gather into one gap worth a programme instead of three slivers.</summary>
        static Spot Longest(Plan plan, int side, int at, int run, Random rng)
        {
            Spot best = null;
            int bestLong = 0;
            foreach (var unit in ResidentialUnits.All.OrderBy(u => rng.Next()))
            {
                // one shopfront to a block: the corner already took it
                if (unit.Shops.Sum() > 0 && plan.Spots.Any(s => s.Shop)) continue;
                for (int yaw = 0; yaw < 360; yaw += 90)
                {
                    var turn = Turn.Of(unit, yaw);
                    if (!turn.Face(side)) continue;
                    int along = side == 0 || side == 2 ? turn.CW : turn.CD;
                    int deep = side == 0 || side == 2 ? turn.CD : turn.CW;
                    if (along > run || along <= bestLong) continue;

                    int i, j;
                    if (side == 0) { i = at; j = Walk; }
                    else if (side == 2) { i = at; j = plan.D - Walk - deep; }
                    else if (side == 1) { i = plan.W - Walk - deep; j = at; }
                    else { i = Walk; j = at; }
                    if (!Fits(plan, turn, i, j)) continue;

                    bestLong = along;
                    best = new Spot
                    {
                        Unit = unit, Yaw = yaw, I = i, J = j,
                        CW = turn.CW, CD = turn.CD, Side = side,
                        Shop = unit.Shops.Sum() > 0,
                    };
                }
            }
            return best;
        }

        /// <summary>
        /// What a gap in the row is for, by how long it is.
        ///
        /// Never grass: the user's call of 2026-08-26, looking at the first drawing. One cell
        /// is the way in off the street, two to four is paving, five and more is a car park -
        /// and both of the last two are things CARS USE, so both are cut clean through the
        /// kerb line rather than reached across the pavement.
        /// </summary>
        static void Programme(Plan plan, int side, int at, int run, Random rng)
        {
            Use use = run == 1 ? Use.Drive : run <= 4 ? Use.Paved : Use.Parking;
            int depth = Math.Min(3, Depth(plan, side));

            // A way in is for getting somewhere. Run it only as far as the thing it serves -
            // the alley, or the car park behind the row - and if there is nothing back there
            // to serve, it is not a drive at all but a paved gap between two houses. Left to
            // run as deep as it could, a single-cell gap laid thirty metres of tarmac through
            // the middle of the yards to reach an alley the block already had two mouths on.
            if (use == Use.Drive)
            {
                int reach = Serves(plan, side, at);
                if (reach < 0) { use = Use.Paved; depth = Math.Min(2, depth); }
                else depth = reach;
            }

            int cut = -1;
            for (int n = 0; n < run; n++)
                for (int k = 0; k < depth; k++)
                {
                    var (i, j) = Into(plan, side, at + n, k);
                    if (i < Walk || j < Walk || i >= plan.W - Walk || j >= plan.D - Walk) continue;
                    if (plan.Ground[i, j] != Use.Empty) break;
                    plan.Ground[i, j] = use;
                    if (k == 0 && cut < 0 && Drives(use)) cut = at + n;
                }

            // the mouth: the ring cell in front of it stops being pavement and becomes road,
            // so a car comes off the street onto the block without ever crossing a kerb
            if (cut >= 0) Mouth(plan, side, cut);

            plan.M.Gaps++;
            plan.M.GapCells += run;
            if (use == Use.Paved) plan.M.Paved++;
            if (use == Use.Drive) plan.M.Drives++;
            if (use == Use.Parking) plan.M.Parking++;
        }

        /// <summary>How many cells in from this edge cell the nearest way a car uses lies,
        /// or -1 if there is nothing back there worth a drive.</summary>
        static int Serves(Plan plan, int side, int at)
        {
            int most = Math.Min(6, Depth(plan, side));
            for (int k = 0; k < most; k++)
            {
                var (i, j) = Into(plan, side, at, k);
                if (i < Walk || j < Walk || i >= plan.W - Walk || j >= plan.D - Walk) return -1;
                var use = plan.Ground[i, j];
                if (Drives(use)) return k;
                if (use != Use.Empty) return -1;         // a wall, a yard already spoken for
            }
            return -1;
        }

        /// <summary>Is this something a car drives on?</summary>
        public static bool Drives(Use use) =>
            use == Use.Drive || use == Use.Alley || use == Use.Parking;

        /// <summary>Cuts the block's pavement ring at one cell of one side, so the way behind
        /// it opens straight onto the street.</summary>
        static void Mouth(Plan plan, int side, int at)
        {
            var (i, j) = EdgeCell(plan, side, at);
            var (x, y) = (i + Step[side, 0], j + Step[side, 1]);
            if (x < 0 || y < 0 || x >= plan.W || y >= plan.D) return;
            if (plan.Ground[x, y] == Use.Walkway) plan.Ground[x, y] = Use.Drive;
        }

        /// <summary>How deep the inner ground is from this side.</summary>
        static int Depth(Plan plan, int side) => side == 0 || side == 2 ? plan.InnerD : plan.Inner;

        /// <summary>The cell <paramref name="k"/> in from this edge, at <paramref name="at"/>
        /// along it.</summary>
        static (int, int) Into(Plan plan, int side, int at, int k) => side switch
        {
            0 => (at, Walk + k),
            2 => (at, plan.D - Walk - 1 - k),
            1 => (plan.W - Walk - 1 - k, at),
            _ => (Walk + k, at),
        };

        // ------------------------------------------------------------------ the inside

        /// <summary>
        /// What is left in the middle. By the plan: two or three cells deep is back yards
        /// and an alley; six and more is the courtyard park.
        ///
        /// The alley is declared ACROSS THE WHOLE BLOCK and its mouths are cut through the
        /// row before anything else claims them - the core's lesson, that an L-shaped run of
        /// road locks the traffic that has to use it.
        /// </summary>
        static void Inside(Plan plan, Random rng)
        {
            if (plan.Klass == Klass.Court) Court(plan);

            for (int i = Walk; i < plan.W - Walk; i++)
                for (int j = Walk; j < plan.D - Walk; j++)
                    if (plan.Ground[i, j] == Use.Empty)
                        plan.Ground[i, j] = Use.Yard;

            Kerbs(plan);
        }

        /// <summary>
        /// A kerb wherever a way cars use meets soft ground.
        ///
        /// The user's rule, looking at the first drawing: asphalt dropped on the grass is not
        /// a street. Where the neighbour is a building the wall is the edge - a cell cannot be
        /// slipped under a house - and that is the one side this cannot give a pavement.
        /// </summary>
        static void Kerbs(Plan plan)
        {
            var verge = new List<(int, int)>();
            for (int i = Walk; i < plan.W - Walk; i++)
                for (int j = Walk; j < plan.D - Walk; j++)
                {
                    var use = plan.Ground[i, j];
                    if (use != Use.Yard && use != Use.Paved && use != Use.Court) continue;
                    for (int s = 0; s < 4; s++)
                    {
                        int x = i + Step[s, 0], y = j + Step[s, 1];
                        if (x < 0 || y < 0 || x >= plan.W || y >= plan.D) continue;
                        if (Drives(plan.Ground[x, y])) { verge.Add((i, j)); break; }
                    }
                }
            foreach (var (i, j) in verge) plan.Ground[i, j] = Use.Verge;

            for (int i = 0; i < plan.W; i++)
                for (int j = 0; j < plan.D; j++)
                    if (plan.Ground[i, j] == Use.Verge) plan.M.Verge++;
        }

        static void Court(Plan plan)
        {
            int i0 = Walk, i1 = plan.W - Walk - 1, j0 = Walk, j1 = plan.D - Walk - 1;
            // the empty middle, shrunk to what is actually free
            var free = new List<(int, int)>();
            for (int i = i0; i <= i1; i++)
                for (int j = j0; j <= j1; j++)
                    if (plan.Ground[i, j] == Use.Empty) free.Add((i, j));
            if (free.Count < 16) return;

            int minI = free.Min(c => c.Item1), maxI = free.Max(c => c.Item1);
            int minJ = free.Min(c => c.Item2), maxJ = free.Max(c => c.Item2);
            for (int i = minI; i <= maxI; i++)
                for (int j = minJ; j <= maxJ; j++)
                    if (plan.Ground[i, j] == Use.Empty)
                    {
                        plan.Ground[i, j] = Use.Court;
                        plan.M.CourtCells++;
                    }
        }

        // ------------------------------------------------------------------ the verdict

        static void Measure(Plan plan)
        {
            var m = plan.M;
            m.Units = plan.Spots.Count;
            foreach (var spot in plan.Spots)
            {
                var turn = Turn.Of(spot.Unit, spot.Yaw);
                m.Trees += spot.Unit.Trees;
                for (int s = 0; s < 4; s++)
                {
                    if (!plan.Street[s]) continue;
                    m.Doors += turn.Doors(s);
                    m.Shops += turn.Shops(s);
                }
            }
            m.Repeats = plan.Spots.Count - plan.Spots.Select(s => s.Unit.Name).Distinct().Count();

            var empties = new List<string>();
            for (int i = 0; i < plan.W; i++)
                for (int j = 0; j < plan.D; j++)
                {
                    if (plan.Ground[i, j] == Use.Empty)
                    {
                        m.Empty++;
                        if (empties.Count < 6) empties.Add($"({i},{j})");
                    }
                }
            m.EmptyAt = string.Join(" ", empties);

            foreach (var spot in plan.Spots)
            {
                var turn = Turn.Of(spot.Unit, spot.Yaw);
                for (int u = 0; u < turn.CW; u++)
                    for (int v = 0; v < turn.CD; v++)
                        if (turn.Pit(u, v)) m.Pits++;
            }

            double ha = plan.W * plan.D * Cell * Cell / 10000.0;
            m.DoorsPerHa = ha > 0 ? m.Doors / ha : 0;
        }

        static void Judge(Plan plan)
        {
            var seen = new Dictionary<(int, int), string>();
            foreach (var spot in plan.Spots)
            {
                var turn = Turn.Of(spot.Unit, spot.Yaw);

                if (spot.I < Walk || spot.J < Walk ||
                    spot.I + turn.CW > plan.W - Walk || spot.J + turn.CD > plan.D - Walk)
                    plan.Faults.Add($"OffBlock: {spot} runs off the block");

                for (int u = 0; u < turn.CW; u++)
                    for (int v = 0; v < turn.CD; v++)
                    {
                        if (!turn.Filled(u, v)) continue;
                        var key = (spot.I + u, spot.J + v);
                        if (seen.TryGetValue(key, out var other))
                            plan.Faults.Add($"Overlap: {spot.Unit.Name} and {other} share ({key.Item1},{key.Item2})");
                        else seen[key] = spot.Unit.Name;
                    }

                // the back of a unit never looks at a street; its end may
                for (int s = 0; s < 4; s++)
                {
                    if (!plan.Street[s] || !Back(turn, s)) continue;
                    if (!Along(plan, spot, turn, s)) continue;
                    plan.Faults.Add($"BackToStreet: {spot.Unit.Name} shows its {SideName[s]} back to the street");
                }
            }

            int shops = plan.Spots.Count(s => s.Shop);
            if (shops > 1) plan.Faults.Add($"TwoShops: {shops} units carry shopfronts");

            // Every corner a block of this class is built on carries a building. A corner
            // block is one building and its garden, a row keeps its open ends: asking all
            // four of those is asking for a fault that is really a misreading of the class.
            int lo = Walk, hi = plan.W - Walk - 1, bo = Walk, to = plan.D - Walk - 1;
            int built = 0, offered = 0;
            foreach (var (i, j, a, b) in new[]
                     { (lo, bo, 0, 3), (hi, bo, 0, 1), (hi, to, 2, 1), (lo, to, 2, 3) })
            {
                if (!plan.Street[a] || !plan.Street[b]) continue;
                offered++;
                bool on = plan.Ground[i, j] == Use.Building || plan.Ground[i, j] == Use.Forecourt;
                if (on) { built++; continue; }
                if (plan.Klass == Klass.Block || plan.Klass == Klass.Court)
                    plan.Faults.Add($"BlankCorner: the {SideName[a]}{SideName[b]} corner carries nothing");
            }
            if (plan.Spots.Count == 0)
                plan.Faults.Add("NoUnit: nothing was built on this block at all");

            // EVERY way a car uses has to reach the street ON TARMAC. This is the user's
            // rule of 2026-08-26 in one measurement - "ne smeju kola da idu preko pavementa,
            // nije logicno" - and it catches the dead alley too: an alley with no mouth is
            // simply a way with no way out.
            foreach (var patch in Patches(plan))
            {
                if (patch.Out) continue;
                var (i, j) = patch.At;
                plan.Faults.Add($"NoWayIn: the {patch.Use} at ({i},{j}) can only be reached " +
                                "across the pavement");
            }

            // nothing paved over a pit
            for (int i = 0; i < plan.W; i++)
                for (int j = 0; j < plan.D; j++)
                {
                    var use = plan.Ground[i, j];
                    if (use != Use.Parking && use != Use.Drive && use != Use.Alley) continue;
                    if (Sunk(plan, i, j))
                        plan.Faults.Add($"FloorOverPit: {use} laid over the pit at ({i},{j})");
                }
        }

        /// <summary>Does this side of the unit lie along the block's edge, with nothing
        /// between it and the pavement?</summary>
        static bool Along(Plan plan, Spot spot, Turn turn, int side)
        {
            for (int u = 0; u < turn.CW; u++)
                for (int v = 0; v < turn.CD; v++)
                {
                    if (!turn.Filled(u, v)) continue;
                    int i = spot.I + u + Step[side, 0], j = spot.J + v + Step[side, 1];
                    if (i < 0 || j < 0 || i >= plan.W || j >= plan.D) continue;
                    if (plan.Ground[i, j] == Use.Walkway) return true;
                }
            return false;
        }

        static bool Sunk(Plan plan, int i, int j)
        {
            foreach (var spot in plan.Spots)
            {
                var turn = Turn.Of(spot.Unit, spot.Yaw);
                int u = i - spot.I, v = j - spot.J;
                if (u < 0 || v < 0 || u >= turn.CW || v >= turn.CD) continue;
                if (turn.Pit(u, v)) return true;
            }
            return false;
        }

        /// <summary>
        /// Every connected run of tarmac in the block, and whether a car can drive off it
        /// onto the street without crossing anything else.
        ///
        /// "Off the block" means a cell on the outer ring: the ring is the block's pavement,
        /// so the only way a run of tarmac reaches the road is if the recipe cut the ring
        /// for it. That cut is the mouth.
        /// </summary>
        static IEnumerable<(Use Use, (int, int) At, bool Out)> Patches(Plan plan)
        {
            var seen = new bool[plan.W, plan.D];
            for (int i = 0; i < plan.W; i++)
                for (int j = 0; j < plan.D; j++)
                {
                    if (seen[i, j] || !Drives(plan.Ground[i, j])) continue;

                    var use = plan.Ground[i, j];
                    var todo = new Queue<(int I, int J)>();
                    todo.Enqueue((i, j));
                    seen[i, j] = true;
                    bool onto = false;
                    while (todo.Count > 0)
                    {
                        var c = todo.Dequeue();
                        if (c.I == 0 || c.J == 0 || c.I == plan.W - 1 || c.J == plan.D - 1) onto = true;
                        for (int s = 0; s < 4; s++)
                        {
                            int x = c.I + Step[s, 0], y = c.J + Step[s, 1];
                            if (x < 0 || y < 0 || x >= plan.W || y >= plan.D) continue;
                            if (seen[x, y] || !Drives(plan.Ground[x, y])) continue;
                            seen[x, y] = true;
                            todo.Enqueue((x, y));
                        }
                    }
                    yield return (use, (i, j), onto);
                }
        }

        // ------------------------------------------------------------------ saying it

        public static char Glyph(Use use) => use switch
        {
            Use.Walkway => '.',
            Use.Verge => ',',
            Use.Building => '#',
            Use.Forecourt => ':',
            Use.Yard => 'y',
            Use.Paved => 'p',
            Use.Drive => '=',
            Use.Parking => 'P',
            Use.Alley => '-',
            Use.Court => 'o',
            Use.Cafe => 'c',
            _ => '?',
        };

        public static string Map(Plan plan)
        {
            var sb = new StringBuilder();
            for (int j = plan.D - 1; j >= 0; j--)
            {
                sb.Append("    ");
                for (int i = 0; i < plan.W; i++) sb.Append(Glyph(plan.Ground[i, j]));
                sb.Append('\n');
            }
            return sb.ToString();
        }

        public static string Report(Plan plan)
        {
            var m = plan.M;
            var sb = new StringBuilder();
            sb.Append($"{plan.Klass} {plan.W}x{plan.D} cells ({plan.W * Cell}x{plan.D * Cell} m) seed {plan.Seed}: ");
            sb.Append($"{m.Units} unit(s), {m.Doors} door(s) ({m.DoorsPerHa:F0}/ha), {m.Shops} shop(s), ");
            sb.Append($"{m.Gaps} gap(s) over {m.GapCells} cell(s), {m.Trees} tree(s), ");
            sb.Append($"alley {m.AlleyCells}, verge {m.Verge}, court {m.CourtCells}, empty {m.Empty}");
            if (m.Empty > 0) sb.Append($" at {m.EmptyAt}");
            if (m.Repeats > 0) sb.Append($", {m.Repeats} repeat(s)");
            foreach (var refused in plan.Refused) sb.Append($"\n    REFUSED: {refused}");
            foreach (var fault in plan.Faults) sb.Append($"\n    FAULT: {fault}");
            return sb.ToString();
        }
    }
}
