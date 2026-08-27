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
            Forecourt,   // a unit's own ground - stoop, pit, garden: nothing is laid on it
            Yard,        // back yard: concrete, fences, washing lines (no grass: the user, 2026-08-27)
            Paved,       // a gap in the row, paved - never grass (the user's call, 2026-08-26)
            Verge,       // pavement INSIDE the block, edging a way cars use
            Drive,       // tarmac a car uses: the way in off the street, the car park's aisle
            Parking,     // painted bays, nosed to an aisle
            Alley,       // 5 m one-way, bins on the verge against the backs
            Court,       // the paved court a big block keeps in the middle
            Cafe,        // a kit storefront in a gap, fronting the street
            Park,        // a harvested park unit stands here: it brings its own grass
            Subway,      // the subway entrance's column: its stair, nothing laid over it
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
            public int Units, Doors, Shops, Cafes, Trees, Parks, Subways;
            public int Gaps, GapCells, Paved, Drives, Parking, AlleyCells, CourtCells, Verge;
            public int Empty, Pits, Repeats;
            /// <summary>The biggest unit's box as a share of the inner ground, percent.</summary>
            public int Share;
            public double DoorsPerHa;
            public string EmptyAt = "";
        }

        /// <summary>A gap in the row and what it was made: where it starts along its side,
        /// how long it runs and how deep it goes.</summary>
        public sealed class Gap
        {
            public int Side, At, Run, Depth;
            public Use Use;
            public override string ToString() => $"{Use} {Run} cell(s) on {SideName[Side]} at {At}";
        }

        public sealed class Plan
        {
            public int W, D;                       // the whole block, pavement ring included
            public Klass Klass;
            public bool[] Street = new bool[4];    // which sides have a road along them
            public int Artery = -1;                // the side the shops look at
            public int Seed;
            public List<Spot> Spots = new List<Spot>();
            public List<Gap> Gaps = new List<Gap>();
            /// <summary>The gap the kit storefront stands in, if the block got one.</summary>
            public Gap Cafe;
            /// <summary>The gap the subway entrance goes down in, and which column of it.</summary>
            public Gap Subway;
            public int SubwayAt = -1;
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
            Cafe(plan, rng);
            Subway(plan, rng);
            Parks(plan, rng);
            Inside(plan, rng);
            Measure(plan);
            Judge(plan);
            return plan;
        }

        /// <summary>Does this unit drop below the ground anywhere - a pit, a sunken floor?</summary>
        public static bool Pitted(ResidentialUnit unit) =>
            unit.Plan.Any(row => row.IndexOf(':') >= 0 || row.IndexOf(',') >= 0);

        /// <summary>
        /// A unit's box may cover at most this much of the inner ground, percent - on the
        /// classes built on all four corners. The 50 x 45 m L stood on a 50 x 60 m block WAS
        /// the block: one building with a row stuck on the side (the user, 2026-08-27: "ovaj
        /// drugi je preogroman"). A corner block is one house and its garden and a row block
        /// is the through-row by definition, so the rule is not asked of those.
        /// </summary>
        public const int ShareMost = 50;

        static bool Modest(Plan plan, Turn turn) =>
            (plan.Klass != Klass.Block && plan.Klass != Klass.Court) ||
            turn.CW * turn.CD * 100 <= plan.Inner * plan.InnerD * ShareMost;

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
            int lo = Walk + least + 1;
            int Hi(int across) => across - Walk - 2 - least;

            // The alley runs the long way if it can, and the short way if only that fits:
            // a 65 x 75 m block is too narrow for a north-south alley with a row of houses
            // either side, but an east-west one across its depth fits - and the alley is
            // what makes the block class a block (a third of the first sweep's blocks gave
            // it up without trying the other way)
            bool eastWest = plan.Inner >= plan.InnerD;
            if (lo > Hi(eastWest ? plan.D : plan.W) && lo <= Hi(eastWest ? plan.W : plan.D))
                eastWest = !eastWest;
            int across = eastWest ? plan.D : plan.W;
            int hi = Hi(across);
            if (lo > hi)
            {
                plan.Refused.Add($"alley: {plan.Inner}x{plan.InnerD} cells leaves no room for " +
                                 "a kerbed way and a row of houses either side, either way");
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

        // The corners and the rows are dealt from the HOUSES. A park is not a house and a
        // storefront is not a house: the park goes in the ground the houses leave, and the
        // storefront in a gap in the row, each in its own pass.
        static IEnumerable<ResidentialUnit> Shops() =>
            ResidentialUnits.Houses.Where(u => u.Shops.Sum() > 0);

        static IEnumerable<ResidentialUnit> Stoops() =>
            ResidentialUnits.Houses.Where(u => u.Shops.Sum() == 0 && u.Doors.Sum() > 0);

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
                // and every corner of a block or a court carries one: a blank corner is a
                // fault by the approved plan, and leaving one open to vary the drawing was
                // tried on 2026-08-27 and duly failed fifteen blocks in thirty
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
            // EVERY UNIT THAT FITS, and then one of them by lot - not the biggest.
            //
            // The biggest that fits is the SAME unit every time, and a quarter of a hundred
            // blocks of much the same size came out as a hundred copies of one block (the
            // user, 2026-08-27: "vrtis i dalje iste blokove"). The lot is weighted by area, so
            // a big corner still usually takes a big house and the small ones are not crowded
            // out - but not always, which is the whole difference between a street of houses
            // and a street of one house printed over and over.
            var spots = new List<Spot>();
            foreach (var unit in units)
                for (int yaw = 0; yaw < 360; yaw += 90)
                {
                    var turn = Turn.Of(unit, yaw);
                    if (!turn.Face(a) || !turn.Face(b)) continue;
                    if (!Modest(plan, turn)) continue;

                    int i = ci == Walk ? ci : ci - turn.CW + 1;
                    int j = cj == Walk ? cj : cj - turn.CD + 1;
                    if (!Fits(plan, turn, i, j)) continue;

                    if (more > 0 && rest != null && !Leaves(plan, turn, i, j, rest, more)) continue;

                    spots.Add(new Spot
                    {
                        Unit = unit, Yaw = yaw, I = i, J = j, CW = turn.CW, CD = turn.CD,
                        Shop = unit.Shops.Sum() > 0, Side = a,
                    });
                }
            if (spots.Count == 0) return null;

            int total = 0;
            foreach (var spot in spots) total += spot.CW * spot.CD;
            int draw = rng.Next(total);
            foreach (var spot in spots)
            {
                draw -= spot.CW * spot.CD;
                if (draw < 0) return spot;
            }
            return spots[spots.Count - 1];
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
            bool pitted = Pitted(spot.Unit);
            for (int u = 0; u < turn.CW; u++)
                for (int v = 0; v < turn.CD; v++)
                {
                    int i = spot.I + u, j = spot.J + v;
                    if (turn.Filled(u, v))
                    {
                        plan.Ground[i, j] = turn.Wall(u, v) ? Use.Building : Use.Forecourt;
                        continue;
                    }
                    // The empty cells inside a SUNKEN unit's box are its own ground too: the
                    // garden in the brownstone's L is fenced at zero and drops into the pit
                    // beside it, and a slab laid there roofs the pit (the user, 2026-08-27:
                    // "ispod residential4 ne smes da crtas pod"). An L that keeps its floor at
                    // zero leaves the block the ground inside its arms, as before
                    if (pitted && plan.Ground[i, j] == Use.Empty) plan.Ground[i, j] = Use.Forecourt;
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
            // ONE OF THE UNITS THAT FIT, drawn by lot and weighted by how much of the run it
            // takes - not simply the longest. The longest is the same house every time, and a
            // row dealt that way is the same row in every block of the quarter (the user,
            // 2026-08-27). Weighted, a long run still mostly takes long houses, so the
            // leftovers still gather into one gap rather than three slivers.
            var spots = new List<Spot>();
            foreach (var unit in ResidentialUnits.Houses)
            {
                // one shopfront to a block: the corner already took it
                if (unit.Shops.Sum() > 0 && plan.Spots.Any(s => s.Shop)) continue;
                for (int yaw = 0; yaw < 360; yaw += 90)
                {
                    var turn = Turn.Of(unit, yaw);
                    if (!turn.Face(side)) continue;
                    if (!Modest(plan, turn)) continue;
                    int along = side == 0 || side == 2 ? turn.CW : turn.CD;
                    int deep = side == 0 || side == 2 ? turn.CD : turn.CW;
                    if (along > run) continue;

                    int i, j;
                    if (side == 0) { i = at; j = Walk; }
                    else if (side == 2) { i = at; j = plan.D - Walk - deep; }
                    else if (side == 1) { i = plan.W - Walk - deep; j = at; }
                    else { i = Walk; j = at; }
                    if (!Fits(plan, turn, i, j)) continue;

                    spots.Add(new Spot
                    {
                        Unit = unit, Yaw = yaw, I = i, J = j,
                        CW = turn.CW, CD = turn.CD, Side = side,
                        Shop = unit.Shops.Sum() > 0,
                    });
                }
            }
            if (spots.Count == 0) return null;

            int total = 0;
            foreach (var spot in spots)
            {
                int along = side == 0 || side == 2 ? spot.CW : spot.CD;
                total += along * along;                       // the square: long houses still win most runs
            }
            int draw = rng.Next(total);
            foreach (var spot in spots)
            {
                int along = side == 0 || side == 2 ? spot.CW : spot.CD;
                draw -= along * along;
                if (draw < 0) return spot;
            }
            return spots[spots.Count - 1];
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
            // A car park is one row of bays behind the pavement and its aisle - two cells.
            // Three deep put a second row of bays with its noses against the backs of the
            // houses, and a car is longer than its stall (the user, 2026-08-27: "parking
            // mora bude manji, preplicu se auta sa zgradama").
            if (use == Use.Parking) depth = Math.Min(2, depth);

            // A way in is for getting somewhere. Run it only as far as the thing it serves -
            // the alley, or the car park behind the row - and if there is nothing back there
            // to serve, it is not a drive at all but a paved gap between two houses. Left to
            // run as deep as it could, a single-cell gap laid thirty metres of tarmac through
            // the middle of the yards to reach an alley the block already had two mouths on.
            if (use == Use.Drive)
            {
                int reach = Serves(plan, side, at);
                // and only if what it reaches has no way out yet: a car park with its own
                // mouth does not need a second lane cut through the block beside the
                // house's shopfront (the user, 2026-08-27, of the corner block: the tarmac
                // ran the length of residential-01's awnings from street to street)
                if (reach >= 0)
                {
                    var (ri, rj) = Into(plan, side, at, reach);
                    if (HasWayOut(plan, ri, rj)) reach = -1;
                }
                if (reach < 0) { use = Use.Paved; depth = Math.Min(2, depth); }
                else depth = reach;
            }

            // A car park keeps a cell of pavement between its tarmac and the houses beside
            // it - beside the bays and beside the aisle alike: the shop corner hangs its
            // awnings two metres past its wall, and tarmac run up to that wall put the
            // shopfront on the car park (the user, 2026-08-27: "residential-01 uvek prelazi
            // na parking", "zgrada i parking ne smeju da se preplicu"). The way in moves to
            // the first column that is not that pavement.
            bool Flanked(int n, int k)
            {
                foreach (int beside in new[] { at + n - 1, at + n + 1 })
                {
                    var (x, y) = Into(plan, side, beside, k);
                    if (x < 0 || y < 0 || x >= plan.W || y >= plan.D) continue;
                    var near = plan.Ground[x, y];
                    if (near == Use.Building || near == Use.Forecourt) return true;
                }
                return false;
            }
            // The way in leads straight into the aisle, so the entrance column is one where
            // neither the mouth cell nor the aisle cell behind it is that pavement. A tooth
            // with no such column is no car park: it is paved.
            int first = -1;
            if (use == Use.Parking)
            {
                for (int n = 0; n < run && first < 0; n++)
                    if (!Flanked(n, 0) && (depth < 2 || !Flanked(n, 1))) first = n;
                if (first < 0) { use = Use.Paved; depth = Math.Min(2, depth); }
            }

            int cut = -1;
            for (int n = 0; n < run; n++)
                for (int k = 0; k < depth; k++)
                {
                    var (i, j) = Into(plan, side, at + n, k);
                    if (i < Walk || j < Walk || i >= plan.W - Walk || j >= plan.D - Walk) continue;
                    if (plan.Ground[i, j] != Use.Empty) break;
                    var cell = use;
                    if (use == Use.Parking)
                    {
                        // A CAR PARK, NOT A FIELD OF LINES. The first one tiled painted bays
                        // over every cell of the tooth, and the lines chained into stripes
                        // the length of the lot (the user, 2026-08-27: "zna se kako parking
                        // izgleda"). So: the way in at the mouth, an aisle along the row
                        // behind the pavement, and the bays either side of the aisle, nosed
                        // to it. Two cells deep is one row of bays and the aisle; three is
                        // bays on both sides; one is a lay-by
                        bool entrance = n == first && k == 0;
                        bool aisle = k == 1 || depth == 1;
                        cell = Flanked(n, k) ? Use.Paved : entrance || aisle ? Use.Drive : Use.Parking;
                    }
                    plan.Ground[i, j] = cell;
                    if (k == 0 && cut < 0 && Drives(cell)) cut = at + n;
                }

            // the mouth is cut where the ENTRANCE is, not at the first painted bay: a bay
            // in front of a wing that leaves no room for the aisle is pavement (below), and
            // a mouth cut for it opened onto that pavement
            if (use == Use.Parking) cut = at + first;

            // A bay is a bay because a car backs out of it into the aisle. Where the aisle
            // could not be laid behind it - a wing of the house stands there - the bay would
            // have the house at its tail and its tile run along the house's wall (the user,
            // 2026-08-27: "pola zgrade prelazi preko parkinga"), so that cell is pavement.
            if (use == Use.Parking && depth >= 2)
                for (int n = 0; n < run; n++)
                {
                    var (i, j) = Into(plan, side, at + n, 0);
                    var (x, y) = Into(plan, side, at + n, 1);
                    if (i < 0 || j < 0 || i >= plan.W || j >= plan.D) continue;
                    if (plan.Ground[i, j] != Use.Parking) continue;
                    bool aisle = x >= 0 && y >= 0 && x < plan.W && y < plan.D && plan.Ground[x, y] == Use.Drive;
                    if (!aisle) plan.Ground[i, j] = Use.Paved;
                }

            // the mouth: the ring cell in front of it stops being pavement and becomes road,
            // so a car comes off the street onto the block without ever crossing a kerb
            if (cut >= 0) Mouth(plan, side, cut);

            plan.Gaps.Add(new Gap { Side = side, At = at, Run = run, Depth = depth, Use = use });
            plan.M.Gaps++;
            plan.M.GapCells += run;
            if (use == Use.Paved) plan.M.Paved++;
            if (use == Use.Drive) plan.M.Drives++;
            if (use == Use.Parking) plan.M.Parking++;
        }

        // ------------------------------------------------------------------ the cafe

        /// <summary>How deep the kit storefront's ground goes: two cells, the depth of the
        /// diner and of the coffee shop alike.</summary>
        public const int CafeDeep = 2;

        /// <summary>
        /// The kit storefront: one to a block, in a paved gap of two cells or more, on the
        /// artery when the artery has such a gap and on any street when it has not.
        ///
        /// The approved plan (§7.2) allowed one and put it on the artery only, and the first
        /// drawing had none on any block (the user, 2026-08-27: "ne vidim da stavljas kafice
        /// i to igde") - a gap on the artery is luck, and half the blocks had none. Which
        /// building stands in it is the composer's, by the length of the gap: a coffee shop
        /// in two or three cells, a diner in four.
        /// </summary>
        /// <summary>How often a block gets the kit storefront in one of its gaps. It used to
        /// be every block that had room for one, which in a quarter of a hundred blocks is a
        /// hundred cafes with the same red umbrellas outside (the user, 2026-08-27). One in
        /// three is a corner shop; every one is a high street.</summary>
        const double CafeOdds = 0.34;

        static void Cafe(Plan plan, Random rng)
        {
            if (rng.NextDouble() >= CafeOdds) return;
            var gaps = plan.Gaps
                .Where(g => g.Use == Use.Paved && g.Run >= 2 && g.Depth >= CafeDeep)
                .OrderByDescending(g => g.Side == plan.Artery)
                .ThenByDescending(g => g.Run)
                .ThenBy(g => rng.Next())
                .ToList();

            foreach (var gap in gaps)
            {
                // the whole gap, CafeDeep in, has to be paved ground - a gap's columns stop
                // short where they met something already built
                bool whole = true;
                for (int n = 0; n < gap.Run && whole; n++)
                    for (int k = 0; k < CafeDeep && whole; k++)
                    {
                        var (i, j) = Into(plan, gap.Side, gap.At + n, k);
                        if (plan.Ground[i, j] != Use.Paved) whole = false;
                    }
                if (!whole) continue;

                for (int n = 0; n < gap.Run; n++)
                    for (int k = 0; k < CafeDeep; k++)
                    {
                        var (i, j) = Into(plan, gap.Side, gap.At + n, k);
                        plan.Ground[i, j] = Use.Cafe;
                    }
                plan.Cafe = gap;
                plan.M.Cafes = 1;
                return;
            }
        }

        // ------------------------------------------------------------------ the subway

        /// <summary>How deep the subway entrance's column goes: three cells. The pack's
        /// entrance (SM_Env_SubwayEntrance_01) is 5 m wide and its canopy runs 12 m back
        /// from the mouth over the stair, with the stair's foot 3 m further - measured
        /// 2026-08-27. Nothing is laid over any of it.</summary>
        public const int SubwayDeep = 3;

        /// <summary>How many blocks with room for one get a subway entrance. One in four:
        /// a stair down on every block is a subway map, not a neighbourhood.</summary>
        const double SubwayOdds = 0.25;

        /// <summary>
        /// A subway entrance in a paved gap that is deep enough - one column of the gap,
        /// at one end of it, mouth on the pavement line. The cafe had first pick of the
        /// gaps; this takes another, so a block never has its stair in its cafe's patio.
        /// </summary>
        static void Subway(Plan plan, Random rng)
        {
            if (rng.NextDouble() >= SubwayOdds) return;
            var gaps = plan.Gaps
                .Where(g => g != plan.Cafe && g.Use == Use.Paved && g.Run >= 2 && g.Depth >= SubwayDeep)
                .OrderBy(g => rng.Next())
                .ToList();
            foreach (var gap in gaps)
            {
                var ends = rng.Next(2) == 0
                    ? new[] { gap.At, gap.At + gap.Run - 1 }
                    : new[] { gap.At + gap.Run - 1, gap.At };
                foreach (int at in ends)
                {
                    bool whole = true;
                    for (int k = 0; k < SubwayDeep && whole; k++)
                    {
                        var (i, j) = Into(plan, gap.Side, at, k);
                        if (plan.Ground[i, j] != Use.Paved) whole = false;
                    }
                    if (!whole) continue;
                    for (int k = 0; k < SubwayDeep; k++)
                    {
                        var (i, j) = Into(plan, gap.Side, at, k);
                        plan.Ground[i, j] = Use.Subway;
                    }
                    plan.Subway = gap;
                    plan.SubwayAt = at;
                    plan.M.Subways = 1;
                    return;
                }
            }
        }

        // ------------------------------------------------------------------ the parks

        /// <summary>How many blocks with room for a park get one. Not every one: a park
        /// behind every row is a garden suburb, and this is not that.</summary>
        const double ParkOdds = 0.6;

        /// <summary>
        /// A park in the ground the houses left - the user's word on the second drawing
        /// (2026-08-27: "neki mini park isto bi mogao da se stavi"), out of the parks he
        /// laid in the harvest scene. One to a block, whole, never cut, and never against
        /// a way cars use: the verge stays between the fence and the tarmac, as it does
        /// for every yard. Of the places it fits, the one that shows most of itself to a
        /// street or an alley is taken - a park nobody can see is a lawn.
        /// </summary>
        static void Parks(Plan plan, Random rng)
        {
            if (rng.NextDouble() >= ParkOdds) return;
            Spot best = null;
            int bestScore = -1;
            foreach (var unit in ResidentialUnits.Parks.OrderBy(u => rng.Next()))
                for (int yaw = 0; yaw < 360; yaw += 90)
                {
                    var turn = Turn.Of(unit, yaw);
                    for (int i = Walk; i + turn.CW <= plan.W - Walk; i++)
                        for (int j = Walk; j + turn.CD <= plan.D - Walk; j++)
                        {
                            if (!Fits(plan, turn, i, j)) continue;
                            int seen = Seen(plan, turn, i, j);
                            if (seen < 0) continue;
                            int score = seen * 4 + rng.Next(4);
                            if (score <= bestScore) continue;
                            bestScore = score;
                            best = new Spot { Unit = unit, Yaw = yaw, I = i, J = j, CW = turn.CW, CD = turn.CD };
                        }
                }
            if (best == null) return;

            var t = Turn.Of(best.Unit, best.Yaw);
            for (int u = 0; u < t.CW; u++)
                for (int v = 0; v < t.CD; v++)
                    if (t.Filled(u, v)) plan.Ground[best.I + u, best.J + v] = Use.Park;
            plan.Spots.Add(best);
            plan.M.Parks++;
        }

        /// <summary>How many of the park's edge cells look at a street or an alley verge -
        /// or -1 when any of them touches tarmac, which is not allowed.</summary>
        static int Seen(Plan plan, Turn turn, int i, int j)
        {
            int seen = 0;
            for (int u = 0; u < turn.CW; u++)
                for (int v = 0; v < turn.CD; v++)
                {
                    if (!turn.Filled(u, v)) continue;
                    for (int s = 0; s < 4; s++)
                    {
                        int x = i + u + Step[s, 0], y = j + v + Step[s, 1];
                        if (x < 0 || y < 0 || x >= plan.W || y >= plan.D) continue;
                        var use = plan.Ground[x, y];
                        if (Drives(use)) return -1;
                        if (use == Use.Walkway || use == Use.Verge) seen++;
                    }
                }
            return seen;
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

        /// <summary>Can a car already drive from this tarmac cell off the block - is there
        /// a mouth on the run of tarmac it belongs to?</summary>
        static bool HasWayOut(Plan plan, int i, int j)
        {
            if (!Drives(plan.Ground[i, j])) return false;
            var seen = new bool[plan.W, plan.D];
            var todo = new Queue<(int, int)>();
            todo.Enqueue((i, j));
            seen[i, j] = true;
            while (todo.Count > 0)
            {
                var (x, y) = todo.Dequeue();
                if (x == 0 || y == 0 || x == plan.W - 1 || y == plan.D - 1) return true;
                for (int s = 0; s < 4; s++)
                {
                    int u = x + Step[s, 0], v = y + Step[s, 1];
                    if (u < 0 || v < 0 || u >= plan.W || v >= plan.D) continue;
                    if (seen[u, v] || !Drives(plan.Ground[u, v])) continue;
                    seen[u, v] = true;
                    todo.Enqueue((u, v));
                }
            }
            return false;
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
            int inner = Math.Max(1, plan.Inner * plan.InnerD);
            m.Share = plan.Spots.Count == 0 ? 0
                    : plan.Spots.Max(s => s.CW * s.CD * 100 / inner);

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

            // no one building is the block: the placer's share rule, measured back
            if ((plan.Klass == Klass.Block || plan.Klass == Klass.Court) && plan.M.Share > ShareMost)
                plan.Faults.Add($"Monolith: one unit's box covers {plan.M.Share}% of the inner ground");

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
            Use.Park => 'g',
            Use.Subway => 'u',
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
            sb.Append($"{m.Units} unit(s) (biggest {m.Share}%), {m.Doors} door(s) ({m.DoorsPerHa:F0}/ha), ");
            sb.Append($"{m.Shops} shop(s), {m.Cafes} cafe(s), {m.Parks} park(s), {m.Subways} subway, ");
            sb.Append($"{m.Gaps} gap(s) over {m.GapCells} cell(s), {m.Trees} tree(s), ");
            sb.Append($"alley {m.AlleyCells}, verge {m.Verge}, court {m.CourtCells}, empty {m.Empty}");
            if (m.Empty > 0) sb.Append($" at {m.EmptyAt}");
            if (m.Repeats > 0) sb.Append($", {m.Repeats} repeat(s)");
            foreach (var gap in plan.Gaps) sb.Append($"\n    gap: {gap}, {gap.Depth} deep");
            foreach (var refused in plan.Refused) sb.Append($"\n    REFUSED: {refused}");
            foreach (var fault in plan.Faults) sb.Append($"\n    FAULT: {fault}");
            return sb.ToString();
        }
    }
}
