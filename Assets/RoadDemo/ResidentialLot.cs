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
        public const int Cell = CoreBlockMetrics.Cell;

        /// <summary>The pavement the block carries round itself, in cells. Residential
        /// blocks follow CoreDemo's shared ten-metre pavement rule.</summary>
        public const int Walk = CoreBlockMetrics.PavementTiles;

        /// <summary>An outdoor venue mixed into a residential block keeps one paved cell
        /// between its fence/props and every neighbouring building. Complete venue blocks
        /// use only the ordinary street pavement ring; see <see cref="YardClearance"/>.</summary>
        public const int AmenityClear = 1;

        public static int Clearance(ResidentialUnit unit) =>
            unit != null && unit.Kind == ResidentialKind.Amenity ? AmenityClear : 0;

        public enum Klass { Corner, Row, Block, Court }

        /// <summary>The job of each outside edge. The role is part of the deal, rather than
        /// inferred later from whichever gap happened to be left there.</summary>
        public enum EdgeRole { Closed, Main, Secondary, Service }

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
            Park,        // a harvested park/amenity stands here; amenities get a backing floor
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
            public int SideB = -1;          // the other street, if it was put on a corner
            public int AccessSide = -1;     // the active face used by pedestrians
            public int EntranceAt = -1;     // cell along AccessSide kept clear as its approach
            public bool Shop;               // it carries the block's shopfronts
            public override string ToString() => $"{Unit.Name}@{Yaw} ({I},{J}) {CW}x{CD}";
        }

        public sealed class Measures
        {
            public int Units, Doors, Shops, Cafes, Trees, Parks, Subways;
            public int Gaps, GapCells, Paved, Drives, Parking, AlleyCells, CourtCells, Verge;
            public int Empty, Pits, Repeats;
            public int MainFrontage, CourtEnclosure, VehicleEntries, PedestrianEntries;
            public int FunctionalGaps, RearParking;
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

        public sealed class Access
        {
            public int Side, At;
            public bool Vehicle;
            public string Purpose = "";
            public override string ToString() =>
                $"{(Vehicle ? "vehicle" : "pedestrian")} {Purpose} on {SideName[Side]} at {At}";
        }

        public sealed class Plan
        {
            public int W, D;                       // the whole block, pavement ring included
            public Klass Klass;
            /// <summary>This plan is one complete gym/car-yard/skatepark lot rather than a
            /// mixed residential block. Those lots keep only the street pavement ring.</summary>
            public bool YardBlock;
            public bool[] Street = new bool[4];    // which sides have a road along them
            public EdgeRole[] Role = new EdgeRole[4];
            public int Artery = -1;                // the side the shops look at
            public int Seed;
            /// <summary>A full diner reserved inside this mixed block, if requested by the
            /// demo. It still shares the block with houses; it is never a yard block.</summary>
            public string FeaturedDiner;
            public List<Spot> Spots = new List<Spot>();
            public List<Gap> Gaps = new List<Gap>();
            public List<Access> Accesses = new List<Access>();
            /// <summary>The gap the kit storefront stands in, if the block got one.</summary>
            public Gap Cafe;
            public List<Gap> Cafes = new List<Gap>();
            /// <summary>This block is one lone house and nothing else - see <see
            /// cref="Alone"/>.</summary>
            public bool Lone;
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

            /// <summary>
            /// The turned side's contiguous shopfront VISUAL runs. Each entry is
            /// (start cell, length) along the turned side. A wide authored mesh can span
            /// several equal 5 m premises; consumers must subdivide Len into physical bays
            /// instead of treating one source mesh as one business. Keeping the source run
            /// here lets stable IDs from the older site catalogue remain attached to one
            /// representative bay when that subdivision happens.
            /// A table older than the pane masks falls back to one full-facade run.
            /// (Plain tuples, no Unity types: Tools/CoreSim compiles this file too.)
            /// </summary>
            public void ShopRuns(int side, List<(int At, int Len)> into)
            {
                into.Clear();
                if (Shops(side) <= 0)
                    return;

                int us = From(side);
                int extent = side == 0 || side == 2 ? CW : CD;
                var lane = Unit.ShopCells != null && us < Unit.ShopCells.Length
                    ? Unit.ShopCells[us]
                    : null;
                if (string.IsNullOrEmpty(lane))
                {
                    into.Add((0, extent));
                    return;
                }

                int start = -1;
                char run = '0';
                for (int p = 0; p <= extent; p++)
                {
                    char pane = '0';
                    if (p < extent)
                    {
                        int i, j;
                        switch (side)
                        {
                            case 0: i = p; j = 0; break;
                            case 1: i = CW - 1; j = p; break;
                            case 2: i = p; j = CD - 1; break;
                            default: i = 0; j = p; break;
                        }
                        Back(i, j, out int u, out int v);
                        int at = us == 0 || us == 2 ? u : v;
                        if (at >= 0 && at < lane.Length) pane = lane[at];
                    }

                    if (start >= 0 && (pane == '0' || pane != run))
                    {
                        into.Add((start, p - start));
                        start = -1;
                    }
                    if (pane != '0' && start < 0)
                    {
                        start = p;
                        run = pane;
                    }
                }

                if (into.Count == 0)
                    into.Add((0, extent));
            }
        }

        // ------------------------------------------------------------------ the deal

        /// <summary>What size of block each class asks for, as the INNER rectangle - the
        /// ground inside the pavement ring. Straight out of the approved plan §2.2.</summary>
        public static bool Sized(Klass klass, int w, int d) => klass switch
        {
            // Compact urban infill can be only fifteen metres deep inside its pavement.
            // The harvested catalogue has several 2-3 cell-deep houses, and the same
            // placer/verdict is clean on these small corner plots.
            Klass.Corner => w >= 3 && w <= 9 && d >= 3 && d <= 8,
            Klass.Row => w >= 2 && w <= 6 && d >= 10,
            Klass.Block => w >= 10 && w <= 15 && d >= 11 && d <= 19,
            Klass.Court => w >= 14 && d >= 14,
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
        public static Plan Roll(int w, int d, int seed, int artery = 0, bool[] streets = null,
                                Klass? forced = null, string featuredDiner = null)
        {
            var plan = new Plan
            {
                W = w, D = d, Seed = seed, Artery = artery,
                FeaturedDiner = featuredDiner,
            };
            for (int s = 0; s < 4; s++) plan.Street[s] = streets == null || streets[s];
            Roles(plan);
            plan.Ground = new Use[w, d];

            var klass = forced ?? Classify(plan.Inner, plan.InnerD);
            if (klass == null)
            {
                plan.Faults.Add($"NoRecipe: {plan.Inner}x{plan.InnerD} cells " +
                                $"({plan.Inner * Cell}x{plan.InnerD * Cell} m) is no class this recipe knows");
                return plan;
            }
            if (forced != null && !Sized(forced.Value, plan.Inner, plan.InnerD) &&
                !Sized(forced.Value, plan.InnerD, plan.Inner))
            {
                plan.Faults.Add($"NoRecipe: forced {forced.Value} does not fit {plan.Inner}x{plan.InnerD} cells");
                return plan;
            }
            plan.Klass = klass.Value;

            for (int i = 0; i < w; i++)
                for (int j = 0; j < d; j++)
                    if (i < Walk || j < Walk || i >= w - Walk || j >= d - Walk)
                        plan.Ground[i, j] = Use.Walkway;

            // The same seed and the same buildable ground deal the same block even when the
            // city's pavement standard changes. The +2 preserves the sequence from the old
            // one-cell ring while making the ring itself irrelevant to the interior deal.
            int diceW = plan.Inner + 2, diceD = plan.InnerD + 2;
            int dice = unchecked(seed * 7919 + diceW * 104729 + diceD * 1299709);
            var rng = new Random(dice);
            // A featured diner is part of a mixed block, so this block cannot also take the
            // one-house-only programme.
            plan.Lone = string.IsNullOrEmpty(featuredDiner) &&
                        rng.NextDouble() < AloneOdds && Room(plan);

            Deal(plan, rng);
            // A DEAL THAT LEFT THE BLOCK BARE IS DEALT AGAIN. Two ways it happens: the lone
            // house fits the ground but faces the wrong way for every corner it was offered,
            // so nothing stands at all; or an ordinary deal puts two small houses on a big
            // block and calls it done, which is the paving-with-a-shed-on-it the user threw
            // out (2026-08-28). Deterministic retries also give a weak principal frontage
            // another deal; after that it stands as it is and the verdict says so.
            for (int again = 1; again <= 12; again++)
            {
                bool empty = plan.Lone && !Standing(plan);
                bool bare = BuiltCoverage(plan) < RequiredBuiltCoverage(plan);
                bool weakFront = (plan.Klass == Klass.Block || plan.Klass == Klass.Court) &&
                                 EdgeCoverage(plan, plan.Artery) < 65;
                bool strandedWay = Patches(plan).Any(patch => !patch.Out);
                bool missingDiner = !string.IsNullOrEmpty(plan.FeaturedDiner) &&
                                    !plan.Spots.Any(s => s.Unit.Name == plan.FeaturedDiner);
                if (!empty && !bare && !weakFront && !strandedWay && !missingDiner) break;
                if (empty) plan.Lone = false;
                Wipe(plan);
                Deal(plan, new Random(unchecked(dice * 31 + 17 * again)));
            }

            Measure(plan);
            Judge(plan);
            return plan;
        }

        /// <summary>One whole deal onto ground that is bare but for its pavement ring.</summary>
        static void Deal(Plan plan, Random rng)
        {
            // A full diner takes the middle programme of this mixed block. The compact demo
            // blocks cannot hold its measured venue, one-cell clear band, four corner houses
            // AND the normal through alley. The diner replaces only that alley; it does not
            // replace the surrounding residential buildings.
            if (string.IsNullOrEmpty(plan.FeaturedDiner)) Declare(plan);
            Corners(plan, rng);
            Diner(plan, rng);
            Edges(plan, rng);
            RearParking(plan, rng);
            Cafe(plan, rng);
            Subway(plan, rng);
            Parks(plan, rng);
            Inside(plan, rng);
        }

        /// <summary>
        /// Reserves one of the two complete Palm City diners in the middle of an otherwise
        /// ordinary residential block, after its four corner houses and before edge rows fill
        /// the remaining ground. Reserving it in the old leftover-lot pass was too late: in a
        /// thousand generated demo benches neither diner had one legal rectangle left.
        /// </summary>
        static void Diner(Plan plan, Random rng)
        {
            if (string.IsNullOrEmpty(plan.FeaturedDiner)) return;
            var unit = ResidentialUnits.All.FirstOrDefault(u =>
                u.Name == plan.FeaturedDiner && u.Kind == ResidentialKind.Amenity &&
                (u.Name == "dinner" || u.Name == "dinner2"));
            if (unit == null) return;

            Spot best = null;
            float bestScore = float.MaxValue;
            int clear = Clearance(unit);
            for (int yaw = 0; yaw < 360; yaw += 90)
            {
                var turn = Turn.Of(unit, yaw);
                for (int i = Walk; i + turn.CW <= plan.W - Walk; i++)
                    for (int j = Walk; j + turn.CD <= plan.D - Walk; j++)
                    {
                        if (!FitsLot(plan, turn, i, j, clear)) continue;
                        float score = Math.Abs(i + turn.CW * 0.5f - plan.W * 0.5f) +
                                      Math.Abs(j + turn.CD * 0.5f - plan.D * 0.5f) +
                                      (float)rng.NextDouble() * 0.1f;
                        if (score >= bestScore) continue;
                        bestScore = score;
                        best = new Spot
                        {
                            Unit = unit, Yaw = yaw, I = i, J = j,
                            CW = turn.CW, CD = turn.CD,
                        };
                    }
            }
            if (best == null) return;
            OccupyLot(plan, best);
            plan.M.Parks++;
        }

        /// <summary>Share of the buildable inner ground occupied by real programme:
        /// buildings, their forecourts, or a complete amenity lot. Paving is deliberately
        /// excluded; calling a large paved gap "used" is the empty-block regression.</summary>
        public static int BuiltCoverage(Plan plan)
        {
            if (plan?.Ground == null) return 0;
            int stands = 0;
            for (int i = Walk; i < plan.W - Walk; i++)
                for (int j = Walk; j < plan.D - Walk; j++)
                {
                    var use = plan.Ground[i, j];
                    if (use == Use.Building || use == Use.Forecourt || use == Use.Park) stands++;
                }
            return stands * 100 / Math.Max(1, plan.Inner * plan.InnerD);
        }

        /// <summary>Does any house stand on this plan?</summary>
        static bool Standing(Plan plan)
        {
            foreach (var spot in plan.Spots)
                if (!ResidentialUnits.IsLot(spot.Unit)) return true;
            return false;
        }

        /// <summary>Back to bare ground and its pavement ring: a deal undone.</summary>
        static void Wipe(Plan plan)
        {
            plan.Spots.Clear();
            plan.Gaps.Clear();
            plan.Accesses.Clear();
            plan.Cafes.Clear();
            plan.Faults.Clear();
            plan.Refused.Clear();
            plan.Cafe = null;
            plan.Subway = null;
            plan.SubwayAt = -1;
            plan.M = new Measures();
            Roles(plan);
            for (int i = 0; i < plan.W; i++)
                for (int j = 0; j < plan.D; j++)
                    plan.Ground[i, j] = i < Walk || j < Walk || i >= plan.W - Walk || j >= plan.D - Walk
                        ? Use.Walkway : Use.Empty;
        }

        /// <summary>
        /// A block that IS one lot: the skatepark, the beach gym or the car yard standing on
        /// its own ground with the block's pavement round it (the user, 2026-08-28). No
        /// houses, no alley, no car park - the lot in the middle of the plot and paving to
        /// the kerb, so the quarter reads it as the one thing it is.
        ///
        /// It is a <see cref="Plan"/> like any other, so <see cref="ResidentialBlocks"/>
        /// stands it, kerbs it, plants its palms and lights it with no work of its own.
        /// </summary>
        public static Plan Yard(int w, int d, int seed, string unitName, bool[] streets = null)
        {
            // the artery is a side, never -1: the street furniture reads plan.Street[Artery]
            // and an artery of -1 is an index off the end of it
            var plan = new Plan
            {
                W = w, D = d, Seed = seed, Artery = 0, Klass = Klass.Corner,
                YardBlock = true,
            };
            for (int s = 0; s < 4; s++) plan.Street[s] = streets == null || streets[s];
            Roles(plan);
            plan.Ground = new Use[w, d];
            for (int i = 0; i < w; i++)
                for (int j = 0; j < d; j++)
                    if (i < Walk || j < Walk || i >= w - Walk || j >= d - Walk)
                        plan.Ground[i, j] = Use.Walkway;

            var unit = ResidentialUnits.All.FirstOrDefault(u => u.Name == unitName);
            if (unit == null)
            {
                plan.Faults.Add($"NoUnit: nothing in the table is called {unitName}");
                return plan;
            }

            var rng = new Random(unchecked(seed * 7919 + w * 104729 + d * 1299709));
            // The turn that fits, centred in whatever ground remains. A yard-block amenity
            // meets the inner edge of its one-cell street pavement; mixed-block amenities
            // still keep their full clear band away from neighbouring houses.
            Spot best = null;
            int clear = YardClearance(unit);
            int parking = YardParkingDepth(unit);
            for (int yaw = 0; yaw < 360; yaw += 90)
            {
                var turn = Turn.Of(unit, yaw);
                if (turn.CW + 2 * clear > plan.Inner ||
                    turn.CD + 2 * clear + parking > plan.InnerD) continue;
                int i = Walk + clear + (plan.Inner - turn.CW - 2 * clear) / 2;
                int j = Walk + clear + (plan.InnerD - turn.CD - 2 * clear - parking) / 2;
                var spot = new Spot { Unit = unit, Yaw = yaw, I = i, J = j, CW = turn.CW, CD = turn.CD };
                if (best == null || rng.Next(2) == 0) best = spot;
            }
            if (best == null)
            {
                plan.Faults.Add($"TooBig: {unitName} ({unit.CW}x{unit.CD} cells plus " +
                                $"{clear}-cell clearance" +
                                (parking > 0 ? $" and {parking}-cell parking" : "") +
                                $") does not fit {plan.Inner}x{plan.InnerD}");
                return plan;
            }

            OccupyLot(plan, best);
            plan.M.Parks++;
            if (parking > 0)
            {
                CaryardParking(plan, best);
                CaryardVenueEntry(plan, best);
            }

            // everything the lot does not stand on is paving, not yard: this is a public
            // place, and its ground is walked on from every side
            for (int i = Walk; i < w - Walk; i++)
                for (int j = Walk; j < d - Walk; j++)
                    if (plan.Ground[i, j] == Use.Empty) plan.Ground[i, j] = Use.Paved;

            Measure(plan);
            Judge(plan);
            return plan;
        }

        /// <summary>Reserves the existing ParkingDemo attended-lot footprint along the
        /// caryard's north edge. This is only the paper plan; <c>ResidentialBlocks</c>
        /// transfers the real ParkingDemo composer into these cells.</summary>
        static void CaryardParking(Plan plan, Spot spot)
        {
            int firstJ = spot.J + spot.CD;
            int first = Walk;
            int run = plan.Inner;
            int depth = YardParkingDepth(spot.Unit);
            if (run < 2 || depth < 1 || firstJ < Walk || firstJ + depth > plan.D - Walk)
            {
                plan.Faults.Add("CaryardParking: no room for the ParkingDemo attended lot");
                return;
            }

            for (int i = first; i < first + run; i++)
                for (int j = firstJ; j < firstJ + depth; j++)
                    plan.Ground[i, j] = Use.Parking;

            // ParkingDemo's entrance is seven metres wide. Two five-metre grid columns
            // carry that central drive cleanly through the pavement and the whole lot.
            int entry = first + run / 2 - 1;
            for (int i = entry; i <= entry + 1; i++)
                for (int j = firstJ; j < firstJ + depth; j++)
                    plan.Ground[i, j] = Use.Drive;
            if (!Mouth(plan, 2, entry, "caryard parking"))
                plan.Faults.Add("CaryardParking: the parking aisle has no street mouth");
            if (!CutPavement(plan, 2, entry + 1))
                plan.Faults.Add("CaryardParking: the second parking entry cell is blocked");

            plan.Gaps.Add(new Gap { Side = 2, At = first, Run = run, Depth = depth, Use = Use.Parking });
            plan.M.Gaps++;
            plan.M.GapCells += run;
            plan.M.Parking++;
        }

        /// <summary>The car yard itself keeps the broad south gate drawn in the user's
        /// reference block, independently of the attended public lot on its north side.
        /// Two cells cut the pavement at the venue's centre; the ring composer turns the
        /// neighbouring kerbs into the two corners of that opening.</summary>
        static void CaryardVenueEntry(Plan plan, Spot spot)
        {
            if (spot?.Unit == null || spot.Unit.Name != "caryard") return;
            int first = Math.Max(Walk, Math.Min(spot.I + spot.CW / 2 - 1,
                                                plan.W - Walk - 2));
            if (!Mouth(plan, 0, first, "caryard venue"))
                plan.Faults.Add("CaryardEntry: the venue gate has no south street mouth");
            if (!CutPavement(plan, 0, first + 1))
                plan.Faults.Add("CaryardEntry: the second venue entry cell is blocked");
        }

        /// <summary>Does this unit drop below the ground anywhere - a pit, a sunken floor?</summary>
        public static bool Pitted(ResidentialUnit unit) =>
            unit.Plan.Any(row => row.IndexOf(':') >= 0 || row.IndexOf(',') >= 0);

        /// <summary>
        /// The most of a block's inner ground one unit's box may cover, percent.
        ///
        /// 100 IS NO LIMIT, and no limit is what the city is built with (2026-08-27: "zasto
        /// pravilo nijedna kuca veca od pola bloka, makni i to").
        ///
        /// It was 50, and unlike the shopfront rule this one WAS the user's - his word on the
        /// first drawing was "izbegavaj velike residential blokove, ovaj drugi je preogroman",
        /// and 50% is what I made of it. What it was holding off is a block that is one
        /// building and nothing else; what it cost is the big L units, which are the only
        /// thing in a catalogue of six that does not fit in a small block - so the small
        /// corner house won 532 of the sweep's 1360 corners. Turn it back to 50 and the
        /// Monolith fault comes back with it.
        /// </summary>
        public const int ShareMost = 100;

        /// <summary>The least of a block's inner ground that its buildings, their forecourts
        /// and its lot may stand on. Below this the block is paving with something in the
        /// middle of it, which is what a gym given a whole quarter cell looked like.</summary>
        public const int FillLeast = 30;

        /// <summary>
        /// A shallow row has almost no private middle to explain a large gap. Require half
        /// of its inner strip to be actual buildings/forecourts so a pair of small houses
        /// cannot leave most of an 85 x 35 m block as anonymous paving.
        ///
        /// A ten-metre band used to reach this by standing a wall of POLYGON City apartment
        /// modules along it. THE CITY STANDS ONLY WHAT THE USER BUILT (2026-09-03, looking
        /// at The Heights Block 29: "necu da imam modularne zidove nikad samo ono sto sam ja
        /// napravio"), so the band is now dealt out of the harvested catalogue like every
        /// other block - and where the catalogue has nothing shallow enough with a face on
        /// both long sides, this fault is the block asking for one. Twenty seeds of the
        /// bench: shallow rows fall from 91% to 71% mean built coverage and six in a hundred
        /// report Bare. The answer is another harvested house, never another module.
        /// </summary>
        public const int CompactRowFillLeast = 50;

        public static int RequiredBuiltCoverage(Plan plan) =>
            plan != null && plan.Klass == Klass.Row &&
            Math.Min(plan.Inner, plan.InnerD) <= 3 ? CompactRowFillLeast : FillLeast;

        static bool Modest(Plan plan, Turn turn) =>
            (plan.Klass != Klass.Block && plan.Klass != Klass.Court) ||
            turn.CW * turn.CD * 100 <= plan.Inner * plan.InnerD * ShareMost;

        static void Roles(Plan plan)
        {
            for (int side = 0; side < 4; side++)
                plan.Role[side] = !plan.Street[side] ? EdgeRole.Closed
                    : side == plan.Artery ? EdgeRole.Main : EdgeRole.Secondary;
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
            if (plan.Klass == Klass.Court)
            {
                DeclareCourtPassages(plan);
                return;
            }
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
            bool canEastWest = lo <= Hi(plan.D);
            bool canNorthSouth = lo <= Hi(plan.W);
            bool eastWest;
            if (canEastWest && canNorthSouth)
            {
                // Keep the principal frontage free of service mouths whenever the block's
                // proportions allow either through direction.
                bool arteryOnEastWestEnds = plan.Artery == 1 || plan.Artery == 3;
                eastWest = arteryOnEastWestEnds ? false
                         : plan.Artery == 0 || plan.Artery == 2 ? true
                         : plan.Inner >= plan.InnerD;
            }
            else eastWest = canEastWest;
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
                plan.Role[side] = side == plan.Artery ? EdgeRole.Main : EdgeRole.Service;
                Mouth(plan, side, mid, "through alley");
            }
        }

        static void DeclareCourtPassages(Plan plan)
        {
            for (int side = 0; side < 4; side++)
            {
                if (!plan.Street[side]) continue;
                int length = side == 0 || side == 2 ? plan.W : plan.D;
                int run = length >= 18 && side != plan.Artery ? 4 : 3;
                int at = (length - run) / 2;
                int depth = Math.Min(2, Depth(plan, side));
                for (int n = 0; n < run; n++)
                    for (int k = 0; k < depth; k++)
                    {
                        var (i, j) = Into(plan, side, at + n, k);
                        if (plan.Ground[i, j] == Use.Empty) plan.Ground[i, j] = Use.Paved;
                    }
                plan.Gaps.Add(new Gap { Side = side, At = at, Run = run, Depth = depth, Use = Use.Paved });
                plan.M.Gaps++;
                plan.M.GapCells += run;
                plan.M.Paved++;
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
        /// Whether a shopfront may still stand on this side of the block.
        ///
        /// ONE SHOPFRONT BUILDING TO A STREET, not one to a whole block. The block plan
        /// asked for a single shop corner, and with a catalogue of six houses - four of
        /// which carry shops at street level - that one line locked four of the six out of
        /// the entire block the moment the corner was dealt. The sweep's own tally on
        /// 2026-08-27: sixty-three per cent of every unit in the city was ONE building.
        /// Read per street the rule keeps its meaning - no street becomes a parade of shops
        /// - and all six houses can stand in the same block.
        /// </summary>
        static bool ShopRoom(Plan plan, int side) =>
            side < 0 || ShopsPerStreet <= 0 ||
            plan.Spots.Count(s => s.Shop && (s.Side == side || s.SideB == side)) < ShopsPerStreet;

        /// <summary>
        /// How many buildings with shops at street level one street of a block may carry.
        /// ZERO IS NO LIMIT, and no limit is what the city is built with.
        ///
        /// There was a limit, and it was never the user's: "radnja samo na cosku, i to na
        /// jednom" was written into the block plan by me on 2026-08-27 as a guess at what a
        /// residential block is, and judged as a fault (TwoShops). Four of the catalogue's
        /// six houses carry shops in their ground floor, so that one line locked four of the
        /// six out of the whole block the moment its corner was dealt: the sweep measured ONE
        /// building as 63% of every unit in the city, and two as 88%. Told about it the user
        /// took the rule out - "ma bez ogranicenja" (2026-08-27). The knob stays because the
        /// measurement is worth keeping: at 1 the top house is 63%, at 3 it is 39%, at no
        /// limit see the tally in Docs/residential-quarter-plan.md.
        /// </summary>
        public static int ShopsPerStreet = 0;

        /// <summary>What to divide a unit's weight in the draw by because it is already
        /// standing in this block. The same house twice over is what makes a quarter read
        /// as one block printed again and again; it is discouraged, not forbidden, because
        /// a long run with three houses to choose from has to put something down.</summary>
        static int Again(Plan plan, ResidentialUnit unit)
        {
            int used = 0;
            foreach (var s in plan.Spots) if (s.Unit == unit) used++;
            return 1 + 4 * used;
        }

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

                // THE CORNER IS DEALT FROM THE WHOLE CATALOGUE. It used to be dealt from
                // the houses with no shops in them, and there are two of those - one of
                // which faces two opposite ways and can never be a corner at all. One
                // building took eight hundred of the sweep's thirteen hundred corners
                // (2026-08-27). The shopfront rule is kept where it belongs - in Fit, per
                // street - and the artery's corner still asks for a shop first, because
                // that is where the traffic is.
                bool onArtery = corner.A == plan.Artery || corner.B == plan.Artery;
                bool room = ShopRoom(plan, corner.A) && ShopRoom(plan, corner.B);
                bool wantShop = onArtery && !shopTaken && room;
                var wants = wantShop ? Shops().ToList() : ResidentialUnits.Houses.ToList();

                var rest = order.Select(x => x.c).Where(c => c != corner &&
                                plan.Street[c.A] && plan.Street[c.B]).ToList();
                var spot = Fit(plan, wants, corner.I, corner.J, corner.A, corner.B, rng, rest, built - stood - 1);
                // nothing with a shopfront fits, or the street already has one: the corner
                // is dealt again from every house there is rather than stand blank
                if (spot == null && wantShop)
                    spot = Fit(plan, ResidentialUnits.Houses.ToList(), corner.I, corner.J,
                               corner.A, corner.B, rng, rest, built - stood - 1);
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
                    if (!Allowed(plan, unit)) continue;
                    var turn = Turn.Of(unit, yaw);
                    if (!turn.Face(a) || !turn.Face(b)) continue;
                    if (!Modest(plan, turn)) continue;
                    // a house with shops at street level may take a corner only where
                    // neither of its two streets carries a shopfront already
                    if (unit.Shops.Sum() > 0 && !(ShopRoom(plan, a) && ShopRoom(plan, b))) continue;

                    int i = ci == Walk ? ci : ci - turn.CW + 1;
                    int j = cj == Walk ? cj : cj - turn.CD + 1;
                    if (!Fits(plan, turn, i, j)) continue;

                    if (more > 0 && rest != null && !Leaves(plan, turn, i, j, rest, more)) continue;

                    spots.Add(new Spot
                    {
                        Unit = unit, Yaw = yaw, I = i, J = j, CW = turn.CW, CD = turn.CD,
                        Shop = unit.Shops.Sum() > 0, Side = a, SideB = b,
                    });
                }
            if (plan.Klass == Klass.Court)
            {
                int lengthA = a == 0 || a == 2 ? plan.Inner : plan.InnerD;
                int lengthB = b == 0 || b == 2 ? plan.Inner : plan.InnerD;
                var compact = spots.Where(spot =>
                    (a == 0 || a == 2 ? spot.CW : spot.CD) * 100 <= lengthA * 45 &&
                    (b == 0 || b == 2 ? spot.CW : spot.CD) * 100 <= lengthB * 45).ToList();
                if (compact.Count > 0) spots = compact;
            }
            if (spots.Count == 0) return null;

            var weight = new List<int>();
            int total = 0;
            foreach (var spot in spots)
            {
                int w = Math.Max(1, spot.CW * spot.CD / Again(plan, spot.Unit));
                if ((a == plan.Artery || b == plan.Artery) && spot.Unit.MaxH >= 17f) w *= 3;
                if (plan.Klass == Klass.Court)
                {
                    int alongA = a == 0 || a == 2 ? spot.CW : spot.CD;
                    int alongB = b == 0 || b == 2 ? spot.CW : spot.CD;
                    int lengthA = a == 0 || a == 2 ? plan.Inner : plan.InnerD;
                    int lengthB = b == 0 || b == 2 ? plan.Inner : plan.InnerD;
                    bool compact = alongA * 100 <= lengthA * 45 && alongB * 100 <= lengthB * 45;
                    w = compact ? w * 4 : Math.Max(1, w / 4);
                }
                weight.Add(w);
                total += w;
            }
            int draw = rng.Next(total);
            for (int k = 0; k < spots.Count; k++)
            {
                draw -= weight[k];
                if (draw < 0) return spots[k];
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
            return Fronts(plan, turn, i, j) && PrivateEnough(plan, turn, i, j);
        }

        /// <summary>Separate buildings are either joined on inactive party walls or have a
        /// useful passage between them. Two active facades looking at each other get the
        /// larger privacy distance.</summary>
        static bool PrivateEnough(Plan plan, Turn turn, int i, int j)
        {
            foreach (var otherSpot in plan.Spots)
            {
                if (ResidentialUnits.IsLot(otherSpot.Unit)) continue;
                var other = Turn.Of(otherSpot.Unit, otherSpot.Yaw);

                bool overlapZ = j < otherSpot.J + other.CD && otherSpot.J < j + turn.CD;
                if (overlapZ && (i + turn.CW <= otherSpot.I || otherSpot.I + other.CW <= i))
                {
                    bool newLeft = i + turn.CW <= otherSpot.I;
                    int gap = newLeft ? otherSpot.I - (i + turn.CW) : i - (otherSpot.I + other.CW);
                    int facing = newLeft ? 1 : 3, otherFacing = newLeft ? 3 : 1;
                    if (!ClearBetween(turn, facing, other, otherFacing, gap)) return false;
                }

                bool overlapX = i < otherSpot.I + other.CW && otherSpot.I < i + turn.CW;
                if (overlapX && (j + turn.CD <= otherSpot.J || otherSpot.J + other.CD <= j))
                {
                    bool newBelow = j + turn.CD <= otherSpot.J;
                    int gap = newBelow ? otherSpot.J - (j + turn.CD) : j - (otherSpot.J + other.CD);
                    int facing = newBelow ? 2 : 0, otherFacing = newBelow ? 0 : 2;
                    if (!ClearBetween(turn, facing, other, otherFacing, gap)) return false;
                }
            }
            return true;
        }

        static bool ClearBetween(Turn one, int side, Turn other, int otherSide, int gap)
        {
            bool activeOne = one.Face(side), activeOther = other.Face(otherSide);
            if (gap == 0) return !activeOne && !activeOther;
            if (gap < 3) return false;                 // 15 m functional passage
            if (activeOne && activeOther && gap < 4) return false; // 20 m front-to-front
            return true;
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
            PedestrianAccess(plan, spot, turn);
        }

        static void PedestrianAccess(Plan plan, Spot spot, Turn turn)
        {
            var offered = new[] { spot.Side, spot.SideB }
                .Where(side => side >= 0 && side < 4 && plan.Street[side] && turn.Face(side))
                .Distinct()
                .OrderByDescending(side => side == plan.Artery)
                .ThenByDescending(side => turn.Doors(side) + turn.Shops(side))
                .ToList();
            if (offered.Count == 0) return;

            int side = offered[0];
            int first = side == 0 || side == 2 ? spot.I : spot.J;
            int last = first + (side == 0 || side == 2 ? spot.CW : spot.CD) - 1;
            int centre = (first + last) / 2;
            int length = side == 0 || side == 2 ? plan.W : plan.D;
            int at = -1;
            for (int step = 0; step <= last - first && at < 0; step++)
                foreach (int candidate in step == 0 ? new[] { centre } : new[] { centre - step, centre + step })
                {
                    if (candidate < first || candidate > last || candidate < Walk || candidate >= length - Walk) continue;
                    var (i, j) = RingCell(plan, side, candidate);
                    if (plan.Ground[i, j] == Use.Walkway) { at = candidate; break; }
                }
            spot.AccessSide = side;
            spot.EntranceAt = at;
            if (at >= 0)
                plan.Accesses.Add(new Access { Side = side, At = at, Purpose = spot.Unit.Name });
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
                .OrderByDescending(s => plan.Role[s] == EdgeRole.Main)
                .ThenByDescending(s => plan.Role[s] == EdgeRole.Secondary)
                .ThenByDescending(s => s == 0 || s == 2 ? plan.W : plan.D)
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

        static (int, int) RingCell(Plan plan, int side, int at) => side switch
        {
            0 => (at, 0),
            2 => (at, plan.D - 1),
            1 => (plan.W - 1, at),
            _ => (0, at),
        };

        static int EdgeBuilt(Plan plan, int side)
        {
            int length = side == 0 || side == 2 ? plan.W : plan.D;
            int built = 0;
            for (int at = Walk; at < length - Walk; at++)
            {
                var (i, j) = EdgeCell(plan, side, at);
                if (plan.Ground[i, j] == Use.Building || plan.Ground[i, j] == Use.Forecourt) built++;
            }
            return built;
        }

        /// <summary>
        /// The houses along one free run of one side, with air between them.
        ///
        /// WALL TO WALL WAS WRONG. The row was packed tight from the corner unit to the far
        /// corner unit, so a block read as one terrace forty metres long - and where two of
        /// the units that carry shops met, their two shopfront corners were glued together
        /// into a shop that does not exist (the user, 2026-08-28: "previse gusto napakovanih
        /// zgrada... pakujes ih tik jedne uz druge a oni imaju radnje u coskovima koje
        /// zalepis jednu na drugu").
        ///
        /// So: a cell or two between neighbours and off whatever already stands at either
        /// end of the run, never the same house twice over, and never two shopfronts side by
        /// side. The gaps are not waste - <see cref="Programme"/> makes them the ways in, the
        /// paved gaps and the patio the cafe stands in, which is where the block's shops and
        /// its cafes come from in the first place.
        /// </summary>
        static void Units(Plan plan, int side, int at, int run, Random rng)
        {
            int length = side == 0 || side == 2 ? plan.W : plan.D;
            if (plan.Klass == Klass.Court)
            {
                int target = (length - 2 * Walk) * 80 / 100;
                run = Math.Min(run, Math.Max(0, target - EdgeBuilt(plan, side)));
            }
            // off the corner unit at either end of the run
            if (at > Walk && run > 0) { at++; run--; }
            if (at + run < length - Walk && run > 0) run--;

            Spot last = null;
            bool wide = false;              // one wider gap to a run: the room a shop needs
            while (run > 0)
            {
                var spot = Longest(plan, side, at, run, rng, last);
                if (spot == null) break;
                Place(plan, spot);
                int took = side == 0 || side == 2 ? spot.CW : spot.CD;
                at += took;
                run -= took;
                last = spot;

                // the gap to the next house: TWO cells, which is a paved gap - a cafe, a
                // subway stair, a patio. One cell is a driveway, and a driveway wants
                // something behind it to serve; left to the dice they came out as ways in
                // that could only be reached across the pavement (NoWayIn, one block in
                // three hundred), so the row leaves paving and lets Programme decide.
                //
                // ONE gap to a run is wider - four cells. Two cells is twenty metres of
                // paving between two walls, and a storefront that has to stand clear of
                // both its neighbours cannot fit in it: the cafes came out standing in the
                // houses either side of them (the user, 2026-08-28: "kafici se dodaju tik uz
                // zgrade... se preplicu uz zgrade").
                int space = 3;
                if (!wide && run >= 9) { space = 4; wide = true; }
                space = Math.Min(run, space);
                at += space;
                run -= space;
            }
        }

        /// <summary>The longest unit that fits this run facing this street. Longest first,
        /// so the leftovers gather into one gap worth a programme instead of three slivers.</summary>
        static Spot Longest(Plan plan, int side, int at, int run, Random rng, Spot last = null)
        {
            // ONE OF THE UNITS THAT FIT, drawn by lot and weighted by how much of the run it
            // takes - not simply the longest. The longest is the same house every time, and a
            // row dealt that way is the same row in every block of the quarter (the user,
            // 2026-08-27). Weighted, a long run still mostly takes long houses, so the
            // leftovers still gather into one gap rather than three slivers.
            var spots = new List<Spot>();
            foreach (var unit in ResidentialUnits.Houses)
            {
                // one shopfront building to a STREET - see ShopRoom
                if (unit.Shops.Sum() > 0 && !ShopRoom(plan, side)) continue;
                if (!Allowed(plan, unit)) continue;
                // never the same house next along the row, and never a second shopfront
                // beside the last one (the user, 2026-08-28)
                if (last != null && unit == last.Unit) continue;
                if (last != null && last.Shop && unit.Shops.Sum() > 0) continue;
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

            var weight = new List<int>();
            int total = 0;
            foreach (var spot in spots)
            {
                int along = side == 0 || side == 2 ? spot.CW : spot.CD;
                // the square: long houses still win most runs - divided by how many of this
                // house the block already carries, so the run does not repeat one of them
                int w = Math.Max(1, along * along / Again(plan, spot.Unit));
                if (side == plan.Artery && spot.Unit.MaxH >= 17f) w *= 3;
                if (plan.Klass == Klass.Court && Turn.Of(spot.Unit, spot.Yaw).Face((side + 2) % 4)) w *= 3;
                weight.Add(w);
                total += w;
            }
            int draw = rng.Next(total);
            for (int k = 0; k < spots.Count; k++)
            {
                draw -= weight[k];
                if (draw < 0) return spots[k];
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
            int entries = plan.Accesses.Count(a => a.Vehicle);
            bool throughAlley = plan.Ground.Cast<Use>().Any(value => value == Use.Alley);

            // Main streets keep a continuous building/pedestrian frontage. Blocks with a
            // through alley use that service route for their rear bays instead of punching
            // another driveway through a street row.
            if (use == Use.Parking && (side == plan.Artery || throughAlley || entries >= 2))
                use = Use.Paved;
            if (use == Use.Drive && entries >= 2) use = Use.Paved;
            if (use == Use.Drive && plan.Accesses.Any(a => !a.Vehicle && a.Side == side && a.At == at))
                use = Use.Paved;
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
                    if (!plan.Accesses.Any(a => !a.Vehicle && a.Side == side && a.At == at + n) &&
                        !Flanked(n, 0) && (depth < 2 || !Flanked(n, 1))) first = n;
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
            if (cut >= 0) Mouth(plan, side, cut, use == Use.Parking ? "parking" : "driveway");

            plan.Gaps.Add(new Gap { Side = side, At = at, Run = run, Depth = depth, Use = use });
            plan.M.Gaps++;
            plan.M.GapCells += run;
            if (use == Use.Paved) plan.M.Paved++;
            if (use == Use.Drive) plan.M.Drives++;
            if (use == Use.Parking) plan.M.Parking++;
        }

        /// <summary>Parking for a full block sits behind the street row and opens directly
        /// onto its through alley. It replaces a short piece of alley verge, so no third or
        /// fourth street mouth is needed and no bay occupies the principal frontage.</summary>
        static void RearParking(Plan plan, Random rng)
        {
            var alley = new List<(int I, int J)>();
            for (int i = Walk; i < plan.W - Walk; i++)
                for (int j = Walk; j < plan.D - Walk; j++)
                    if (plan.Ground[i, j] == Use.Alley) alley.Add((i, j));
            if (alley.Count < 4) return;

            bool eastWest = alley.Select(c => c.J).Distinct().Count() == 1;
            var runs = new List<List<(int I, int J)>>();
            foreach (int flank in new[] { -1, 1 }.OrderBy(_ => rng.Next()))
            {
                var run = new List<(int, int)>();
                foreach (var road in eastWest ? alley.OrderBy(c => c.I) : alley.OrderBy(c => c.J))
                {
                    int i = road.I + (eastWest ? 0 : flank);
                    int j = road.J + (eastWest ? flank : 0);
                    bool clear = i >= Walk && j >= Walk && i < plan.W - Walk && j < plan.D - Walk &&
                                 plan.Ground[i, j] == Use.Verge;
                    if (clear)
                        for (int side = 0; side < 4 && clear; side++)
                        {
                            int x = i + Step[side, 0], y = j + Step[side, 1];
                            if (x < 0 || y < 0 || x >= plan.W || y >= plan.D) continue;
                            if (plan.Ground[x, y] == Use.Building || plan.Ground[x, y] == Use.Forecourt)
                                clear = false;
                        }
                    if (clear) run.Add((i, j));
                    else if (run.Count > 0) { runs.Add(run); run = new List<(int, int)>(); }
                }
                if (run.Count > 0) runs.Add(run);
            }

            // Six cells on each side of the alley: three two-cell bay tiles per flank,
            // eighteen marked stalls in the composer. The earlier eight-cell budget left
            // one flank as a token pair and the rest as an unexplained paved strip.
            int budget = 12;
            foreach (var run in runs.Where(run => run.Count >= 2)
                                    .OrderByDescending(run => run.Count)
                                    .ThenBy(_ => rng.Next()))
            {
                int cells = Math.Min(budget, Math.Min(6, run.Count / 2 * 2));
                if (cells < 2) continue;
                int start = run.Count == cells ? 0 : rng.Next(run.Count - cells + 1);
                for (int n = 0; n < cells; n++)
                {
                    var cell = run[start + n];
                    plan.Ground[cell.I, cell.J] = Use.Parking;
                    plan.M.RearParking++;
                }
                budget -= cells;
                if (budget < 2) break;
            }
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
        /// <summary>How often a block gets the kit storefront in one of its gaps. It was one
        /// in three - and at that rate the user, walking the demo, saw none at all
        /// (2026-08-28: "ne vidim ni jedan kafic"). Three blocks in five now carry one.</summary>
        const double CafeOdds = 1.0;

        /// <summary>
        /// The brownstone's block, with nothing open on it.
        ///
        /// <c>residential-05</c> goes right through the block and carries no shopfront at
        /// all, so a block whose houses are it and its like is a wall of doors and nothing
        /// else. The user's rule (2026-08-28): "ako je samo residential-05 na placu uz njega
        /// mora da ide neki od kafica sa stolovima u cosku" - the cafe is not rolled for
        /// there, it is dealt, and it is dealt into a CORNER gap.
        ///
        /// Read as the letter says it - the brownstone and not one other house - it never
        /// fires: a block that fits the 20 x 45 m through-unit fits something else beside
        /// it. So it is read as what the user was looking at: the brownstone standing on a
        /// block that has no shopfront on it anywhere.
        /// </summary>
        static bool Blind(Plan plan)
        {
            bool brownstone = false, shop = false;
            foreach (var spot in plan.Spots)
            {
                if (ResidentialUnits.IsLot(spot.Unit)) continue;
                if (spot.Unit.Name == "residential-05") brownstone = true;
                if (spot.Unit.Shops.Sum() > 0) shop = true;
            }
            return brownstone && !shop;
        }

        /// <summary>Does this gap reach a corner of the block - the end of its own side?</summary>
        static bool AtCorner(Plan plan, Gap gap)
        {
            int length = gap.Side == 0 || gap.Side == 2 ? plan.W : plan.D;
            return gap.At == Walk || gap.At + gap.Run == length - Walk;
        }

        static void Cafe(Plan plan, Random rng)
        {
            bool must = Blind(plan);
            if (!must && rng.NextDouble() >= CafeOdds) return;
            var gaps = plan.Gaps
                .Where(g => g.Use == Use.Paved && g.Run >= 2 && g.Depth >= CafeDeep)
                // ROOM FIRST. The artery used to come first, and a two-cell gap on it beat
                // a five-cell gap on the side street - which is how the cafe came to stand
                // wedged between two houses with a quarter of the block empty behind it
                // (the user, 2026-08-28). A gap of three cells or more can hold a shop AND
                // the cell of air it has to keep off its neighbours.
                .OrderByDescending(g => g.Run >= 3)
                .ThenByDescending(g => must && AtCorner(plan, g))
                .ThenByDescending(g => g.Side == plan.Artery)
                .ThenByDescending(g => g.Run)
                .ThenBy(g => rng.Next())
                .ToList();

            int wanted = plan.Klass == Klass.Block || plan.Klass == Klass.Court ? 2 : 1;
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
                if (plan.Cafe == null) plan.Cafe = gap;
                plan.Cafes.Add(gap);
                plan.M.Cafes = plan.Cafes.Count;
                if (plan.Cafes.Count >= wanted) return;
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
                .Where(g => !plan.Cafes.Contains(g) && g.Use == Use.Paved && g.Run >= 2 && g.Depth >= SubwayDeep)
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
        /// <summary>
        /// The lots that are a BLOCK OF THEIR OWN and never stand in the ground another
        /// block's houses left over: the skatepark, the beach gym and the car yard (the
        /// user, 2026-08-28: "skatepark, gym, caryard nek budu svoj zaseban tip bloka koji
        /// se pojavljuju u residential kvartovima"). The little parks and the basketball
        /// court still take a corner of a block, which is what a mini park is.
        /// </summary>
        public static readonly string[] OwnBlock = { "skatepark", "gym", "caryard" };

        public static bool OwnBlockUnit(ResidentialUnit unit) =>
            unit != null && Array.IndexOf(OwnBlock, unit.Name) >= 0;

        /// <summary>The three complete venue blocks keep only the ordinary street pavement
        /// around their measured footprint. Amenities mixed with houses retain their extra
        /// one-cell safety band.</summary>
        public static int YardClearance(ResidentialUnit unit) =>
            OwnBlockUnit(unit) ? 0 : Clearance(unit);

        /// <summary>The caryard carries the shallow 15-metre ParkingDemo attended lot. The
        /// gym and skatepark need no vehicle strip.</summary>
        public static int YardParkingDepth(ResidentialUnit unit) =>
            unit != null && unit.Name == "caryard" ? 3 : 0;

        /// <summary>Thirty metres is the smallest ParkingDemo attended lot that keeps six
        /// real bays beside its central entrance instead of reading as a gate with no lot.</summary>
        public static int YardParkingWidth(ResidentialUnit unit) =>
            unit != null && unit.Name == "caryard" ? 6 : 0;

        public static void YardDimensions(ResidentialUnit unit, out int w, out int d)
        {
            if (unit == null) { w = d = 0; return; }
            int border = 2 * (Walk + YardClearance(unit));
            w = Math.Max(unit.CW, YardParkingWidth(unit)) + border;
            d = unit.CD + border + YardParkingDepth(unit);
        }

        /// <summary>The cells whose surface and furniture come from ParkingDemo rather than
        /// the residential pavement/bay-tile composer.</summary>
        public static bool CaryardParkingCell(Plan plan, int i, int j)
        {
            if (plan == null || !plan.YardBlock) return false;
            var spot = plan.Spots.FirstOrDefault(s => s.Unit != null && s.Unit.Name == "caryard");
            if (spot == null) return false;
            return i >= Walk && i < plan.W - Walk &&
                   j >= spot.J + spot.CD && j < plan.D - Walk;
        }

        static int LotClearance(Plan plan, ResidentialUnit unit) =>
            plan != null && plan.YardBlock ? YardClearance(unit) : Clearance(unit);

        /// <summary>The lots that ONLY ever stand on a block of their own. The gym is not one
        /// of them: it may turn up in the ground a block's houses left over as well as on its
        /// own plot, and it turns up oftener than the other two (the user, 2026-08-28: "gym
        /// moze da se potrefi u residential blokovima a moze i da ima svoj blok nek se desava
        /// cesce od caryard i skatepark").</summary>
        static readonly string[] OwnBlockOnly = { "skatepark", "caryard" };

        public static bool Standalone(ResidentialUnit unit) =>
            unit != null && Array.IndexOf(OwnBlockOnly, unit.Name) >= 0;

        /// <summary>
        /// The houses that stand ALONE on their block: the two the user will not have mixed
        /// in with the others (2026-08-28: "residential-04 i residential-05 ne bi trebalo da
        /// se slazu uz ostale residential zgrade, znaci ako blok sadrzi druge residential ova
        /// dva ne stavljas"). Number 4 is a house and its sunken garden and number 5 is the
        /// brownstone that goes right through the block; either one dealt into a row reads as
        /// a terrace with something else stuck to it.
        ///
        /// So a block is one or the other: it takes ONE of these and nothing more, or it is
        /// dealt from the rest of the catalogue and neither of them is in the pool.
        /// </summary>
        public static readonly string[] Alone = { "residential-04", "residential-05" };

        public static bool StandsAlone(ResidentialUnit unit) =>
            unit != null && Array.IndexOf(Alone, unit.Name) >= 0;

        /// <summary>How many blocks are dealt as one of the two lone houses when one of them
        /// fits. Not many: they are big, and a quarter of nothing but lone houses is a
        /// suburb.</summary>
        const double AloneOdds = 0.22;

        /// <summary>Is there room on this block for either of the lone houses? Asked before
        /// the deal, so a block is never sent down the lone road and left with nothing.</summary>
        static bool Room(Plan plan)
        {
            int inner = Math.Max(1, plan.Inner * plan.InnerD);
            foreach (var unit in ResidentialUnits.Houses)
            {
                if (!StandsAlone(unit)) continue;
                bool fits = (unit.CW <= plan.Inner && unit.CD <= plan.InnerD) ||
                            (unit.CD <= plan.Inner && unit.CW <= plan.InnerD);
                // and it has to FILL the block it is alone on: one house of 25 x 25 m on a
                // block of 60 x 65 is a house in a car park (see FillLeast)
                if (fits && unit.CW * unit.CD * 100 >= inner * FillLeast) return true;
            }
            return false;
        }

        /// <summary>
        /// May this house be dealt into this block at all?
        ///
        /// A lone block takes ONE of the two houses that stand alone and nothing after it;
        /// every other block is dealt without either of them in the pool.
        /// </summary>
        static bool Allowed(Plan plan, ResidentialUnit unit)
        {
            bool alone = StandsAlone(unit);
            if (!plan.Lone) return !alone;
            if (!alone) return false;
            // Room(plan) proves that at least one lone house fills this plot; the particular
            // house drawn must satisfy the same rule. Otherwise a small residential-04 can
            // be accepted merely because residential-05 would have fitted, leaving a bare
            // block which used to be disguised by squeezing the gym against its walls.
            int inner = Math.Max(1, plan.Inner * plan.InnerD);
            if (unit.CW * unit.CD * 100 < inner * FillLeast) return false;
            foreach (var spot in plan.Spots)
                if (!ResidentialUnits.IsLot(spot.Unit)) return false;
            return true;
        }

        /// <summary>An empty middle this big is not a yard, it is a hole: it gets a lot
        /// whatever the dice say, and twice that much ground gets two.</summary>
        const int BigYard = 16;

        /// <summary>
        /// The lots a block's leftover ground takes.
        ///
        /// It used to be one lot, on a roll of <see cref="ParkOdds"/>, and an 80 x 80 m block
        /// came out with eighty-seven cells of bare concrete in the middle of it while the
        /// cafe was squeezed into a three-cell gap in the row (the user, 2026-08-28: "vidis
        /// ovde kolko imas praznog prostora unutar bloka a ti si nabio kafic izmedju dva da
        /// se preplicu"). So the roll now decides only the SMALL leftovers: a big empty
        /// middle always takes a lot, and a very big one takes two.
        /// </summary>
        static void Parks(Plan plan, Random rng)
        {
            int patch = Biggest(plan);
            int want = patch >= BigYard * 2 ? 2
                     : patch >= BigYard || rng.NextDouble() < ParkOdds ? 1 : 0;
            for (int n = 0; n < want; n++)
                if (!Lot(plan, rng)) break;
        }

        /// <summary>The biggest run of empty ground on the block, in cells - what a lot has
        /// to fill.</summary>
        static int Biggest(Plan plan)
        {
            var seen = new bool[plan.W, plan.D];
            int most = 0;
            var todo = new Queue<(int, int)>();
            for (int i = Walk; i < plan.W - Walk; i++)
                for (int j = Walk; j < plan.D - Walk; j++)
                {
                    if (seen[i, j] || plan.Ground[i, j] != Use.Empty) continue;
                    int n = 0;
                    todo.Enqueue((i, j));
                    seen[i, j] = true;
                    while (todo.Count > 0)
                    {
                        var (x, y) = todo.Dequeue();
                        n++;
                        for (int s = 0; s < 4; s++)
                        {
                            int a = x + Step[s, 0], b = y + Step[s, 1];
                            if (a < Walk || b < Walk || a >= plan.W - Walk || b >= plan.D - Walk) continue;
                            if (seen[a, b] || plan.Ground[a, b] != Use.Empty) continue;
                            seen[a, b] = true;
                            todo.Enqueue((a, b));
                        }
                    }
                    if (n > most) most = n;
                }
            return most;
        }

        /// <summary>How often a lot participates in the draw once it fits. The two Palm City
        /// diners used to be effectively absent: one was an impossible two-cell storefront
        /// and the other lost a global visibility contest to smaller parks. They are complete
        /// venues with their own terraces, so they take part as lots and get a useful, finite
        /// weight here. Parks remain common and every other amenity remains occasional.</summary>
        static int LotWeight(ResidentialUnit unit) =>
            unit.Name == "dinner" || unit.Name == "dinner2" ? 3
          : unit.Kind == ResidentialKind.Amenity ? 1
          : 2;

        /// <summary>One lot into the ground the houses left, if one fits. A best visible
        /// position is found independently for every unit, then the unit is drawn by weight;
        /// a small park no longer wins merely because it had more candidate positions.</summary>
        static bool Lot(Plan plan, Random rng)
        {
            var choices = new List<(Spot Spot, int Weight)>();
            foreach (var unit in ResidentialUnits.Parks
                         .Where(u => !Standalone(u) && !plan.Spots.Any(x => x.Unit == u))
                         .OrderBy(u => rng.Next()))
            {
                Spot best = null;
                int bestScore = -1;
                int clear = Clearance(unit);
                for (int yaw = 0; yaw < 360; yaw += 90)
                {
                    var turn = Turn.Of(unit, yaw);
                    for (int i = Walk; i + turn.CW <= plan.W - Walk; i++)
                        for (int j = Walk; j + turn.CD <= plan.D - Walk; j++)
                        {
                            if (!FitsLot(plan, turn, i, j, clear)) continue;
                            int seen = Seen(plan, turn, i, j, clear);
                            if (seen < 0) continue;
                            int way = WayDistance(plan, turn, i, j);
                            bool active = unit.Name == "gym" || unit.Name == "kosarkaskiteren";
                            int score = seen * 4 + (active ? Math.Max(0, 10 - way) * 6 : way * 2) + rng.Next(4);
                            if (score <= bestScore) continue;
                            bestScore = score;
                            best = new Spot { Unit = unit, Yaw = yaw, I = i, J = j, CW = turn.CW, CD = turn.CD };
                        }
                }
                if (best != null) choices.Add((best, LotWeight(unit)));
            }
            if (choices.Count == 0) return false;

            int draw = rng.Next(choices.Sum(c => c.Weight));
            Spot picked = choices[choices.Count - 1].Spot;
            foreach (var choice in choices)
            {
                draw -= choice.Weight;
                if (draw >= 0) continue;
                picked = choice.Spot;
                break;
            }

            OccupyLot(plan, picked);
            plan.M.Parks++;
            return true;
        }

        /// <summary>An amenity owns its whole measured rectangle, including the gaps between
        /// its props, and reserves any required clear band immediately so a later park cannot
        /// be dealt into it. A true park keeps its measured, possibly irregular mask.</summary>
        static void OccupyLot(Plan plan, Spot spot)
        {
            var turn = Turn.Of(spot.Unit, spot.Yaw);
            bool amenity = spot.Unit.Kind == ResidentialKind.Amenity;
            for (int u = 0; u < turn.CW; u++)
                for (int v = 0; v < turn.CD; v++)
                    if (amenity || turn.Filled(u, v)) plan.Ground[spot.I + u, spot.J + v] = Use.Park;

            int clear = LotClearance(plan, spot.Unit);
            for (int i = spot.I - clear; i < spot.I + turn.CW + clear; i++)
                for (int j = spot.J - clear; j < spot.J + turn.CD + clear; j++)
                    if (i >= Walk && j >= Walk && i < plan.W - Walk && j < plan.D - Walk &&
                        plan.Ground[i, j] == Use.Empty) plan.Ground[i, j] = Use.Paved;
            plan.Spots.Add(spot);
        }

        /// <summary>A lot placement. Parks use their own occupied cells; amenities reserve a
        /// complete rectangular footprint plus their clear band, all on untouched ground.</summary>
        static bool FitsLot(Plan plan, Turn turn, int i, int j, int clear)
        {
            if (clear <= 0) return Fits(plan, turn, i, j);
            if (i - clear < Walk || j - clear < Walk ||
                i + turn.CW + clear > plan.W - Walk || j + turn.CD + clear > plan.D - Walk)
                return false;
            for (int x = i - clear; x < i + turn.CW + clear; x++)
                for (int y = j - clear; y < j + turn.CD + clear; y++)
                    if (plan.Ground[x, y] != Use.Empty) return false;
            return true;
        }

        static int WayDistance(Plan plan, Turn turn, int i, int j)
        {
            int best = plan.W + plan.D;
            for (int u = 0; u < turn.CW; u++)
                for (int v = 0; v < turn.CD; v++)
                {
                    if (!turn.Filled(u, v)) continue;
                    for (int x = 0; x < plan.W; x++)
                        for (int y = 0; y < plan.D; y++)
                            if (plan.Ground[x, y] == Use.Alley || plan.Ground[x, y] == Use.Drive ||
                                plan.Ground[x, y] == Use.Walkway)
                                best = Math.Min(best, Math.Abs(i + u - x) + Math.Abs(j + v - y));
            }
            return best;
        }

        /// <summary>How many of the park's edge cells look at a street or an alley verge -
        /// or -1 when any of them touches tarmac, which is not allowed.</summary>
        static int Seen(Plan plan, Turn turn, int i, int j, int clear)
        {
            if (clear > 0)
            {
                int seenThroughBand = 0;
                int x0 = i - clear, x1 = i + turn.CW + clear - 1;
                int y0 = j - clear, y1 = j + turn.CD + clear - 1;
                for (int x = x0; x <= x1; x++)
                    for (int y = y0; y <= y1; y++)
                    {
                        if (x != x0 && x != x1 && y != y0 && y != y1) continue;
                        for (int s = 0; s < 4; s++)
                        {
                            int a = x + Step[s, 0], b = y + Step[s, 1];
                            if (a >= x0 && a <= x1 && b >= y0 && b <= y1) continue;
                            if (a < 0 || b < 0 || a >= plan.W || b >= plan.D) continue;
                            var use = plan.Ground[a, b];
                            if (use == Use.Walkway || use == Use.Verge) seenThroughBand++;
                        }
                    }
                return seenThroughBand;
            }

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

        /// <summary>Cuts every pavement cell between the block interior and the street.
        /// A ten-metre pavement must not leave its outer kerb standing across a drive.</summary>
        static bool CutPavement(Plan plan, int side, int at)
        {
            var (i, j) = EdgeCell(plan, side, at);
            for (int step = 1; step <= Walk; step++)
            {
                int x = i + Step[side, 0] * step;
                int y = j + Step[side, 1] * step;
                if (x < 0 || y < 0 || x >= plan.W || y >= plan.D) return false;
                var use = plan.Ground[x, y];
                if (use != Use.Walkway && use != Use.Drive) return false;
            }
            for (int step = 1; step <= Walk; step++)
                plan.Ground[i + Step[side, 0] * step,
                            j + Step[side, 1] * step] = Use.Drive;
            return true;
        }

        /// <summary>Cuts the block's full pavement band at one cell of one side, so the way
        /// behind it opens straight onto the street.</summary>
        static bool Mouth(Plan plan, int side, int at, string purpose)
        {
            if (plan.Accesses.Any(a => !a.Vehicle && a.Side == side && a.At == at)) return false;
            if (!CutPavement(plan, side, at)) return false;
            if (!plan.Accesses.Any(a => a.Vehicle && a.Side == side && a.At == at))
                plan.Accesses.Add(new Access { Side = side, At = at, Vehicle = true, Purpose = purpose });
            if (side != plan.Artery) plan.Role[side] = EdgeRole.Service;
            return true;
        }

        /// <summary>How deep the inner ground is from this side.</summary>
        static int Depth(Plan plan, int side) => side == 0 || side == 2 ? plan.InnerD : plan.Inner;

        /// <summary>
        /// The one mapping from (side, along, depth) to a block cell, published so that a
        /// reader outside the generator - the business site provider, which has to measure a
        /// gap's ground without composing it - uses this and never a second copy of it.
        /// </summary>
        public static (int i, int j) GapCell(Plan plan, int side, int at, int k) =>
            Into(plan, side, at, k);

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
            m.VehicleEntries = plan.Accesses.Count(a => a.Vehicle);
            m.PedestrianEntries = plan.Accesses.Count(a => !a.Vehicle);
            m.FunctionalGaps = plan.Gaps.Count(g => g.Use == Use.Paved || g.Use == Use.Drive ||
                                                       g.Use == Use.Parking || g.Use == Use.Cafe ||
                                                       g.Use == Use.Subway);
            m.MainFrontage = EdgeCoverage(plan, plan.Artery);
            if (plan.Klass == Klass.Court)
            {
                int covered = 0, length = 0;
                for (int side = 0; side < 4; side++)
                {
                    if (!plan.Street[side]) continue;
                    int along = side == 0 || side == 2 ? plan.Inner : plan.InnerD;
                    covered += EdgeCoverage(plan, side) * along;
                    length += along;
                }
                m.CourtEnclosure = length == 0 ? 0 : covered / length;
            }
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

        static int EdgeCoverage(Plan plan, int side)
        {
            if (side < 0 || side >= 4 || !plan.Street[side]) return 0;
            int length = side == 0 || side == 2 ? plan.W : plan.D;
            int built = 0;
            for (int at = Walk; at < length - Walk; at++)
            {
                var (i, j) = EdgeCell(plan, side, at);
                var use = plan.Ground[i, j];
                if (use == Use.Building || use == Use.Forecourt || use == Use.Cafe) built++;
            }
            return built * 100 / Math.Max(1, length - 2 * Walk);
        }

        static void Judge(Plan plan)
        {
            if (!string.IsNullOrEmpty(plan.FeaturedDiner) &&
                !plan.Spots.Any(s => s.Unit.Name == plan.FeaturedDiner))
                plan.Faults.Add($"MissingDiner: {plan.FeaturedDiner} did not fit beside the residential buildings");

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

                // the back of a residential unit never looks at a street; an amenity lot
                // may have a fenced or service side and is judged by its own access.
                for (int s = 0; s < 4 && !ResidentialUnits.IsLot(spot.Unit); s++)
                {
                    if (!plan.Street[s] || !Back(turn, s)) continue;
                    if (!Along(plan, spot, turn, s)) continue;
                    plan.Faults.Add($"BackToStreet: {spot.Unit.Name} shows its {SideName[s]} back to the street");
                }

                if (!ResidentialUnits.IsLot(spot.Unit))
                {
                    if (spot.AccessSide < 0 || spot.EntranceAt < 0)
                        plan.Faults.Add($"NoPedAccess: {spot.Unit.Name} has no clear public approach");
                    else if (!turn.Face(spot.AccessSide))
                        plan.Faults.Add($"WrongFacade: {spot.Unit.Name} uses its {SideName[spot.AccessSide]} side as an entrance");
                    else
                    {
                        var (x, y) = RingCell(plan, spot.AccessSide, spot.EntranceAt);
                        if (plan.Ground[x, y] != Use.Walkway)
                            plan.Faults.Add($"MixedAccess: {spot.Unit.Name}'s pedestrian approach is used by vehicles");
                    }
                }
            }

            // An amenity owns a complete backed rectangle. Mixed-block amenities also own a
            // full paved cell round it; the three complete yard blocks intentionally meet
            // the inner edge of their ordinary street pavement.
            foreach (var spot in plan.Spots.Where(s => s.Unit.Kind == ResidentialKind.Amenity))
            {
                var turn = Turn.Of(spot.Unit, spot.Yaw);
                for (int i = spot.I; i < spot.I + turn.CW; i++)
                    for (int j = spot.J; j < spot.J + turn.CD; j++)
                        if (plan.Ground[i, j] != Use.Park)
                            plan.Faults.Add($"AmenityFloor: {spot.Unit.Name} has no reserved floor at ({i},{j})");

                int clear = LotClearance(plan, spot.Unit);
                for (int i = spot.I - clear; i < spot.I + turn.CW + clear; i++)
                    for (int j = spot.J - clear; j < spot.J + turn.CD + clear; j++)
                    {
                        bool foot = i >= spot.I && i < spot.I + turn.CW &&
                                    j >= spot.J && j < spot.J + turn.CD;
                        if (foot) continue;
                        if (i < Walk || j < Walk || i >= plan.W - Walk || j >= plan.D - Walk)
                        {
                            plan.Faults.Add($"AmenityEdge: {spot.Unit.Name} reaches the pavement at ({i},{j})");
                            continue;
                        }
                        var use = plan.Ground[i, j];
                        if (use == Use.Building || use == Use.Forecourt || use == Use.Walkway ||
                            use == Use.Park || Drives(use))
                            plan.Faults.Add($"AmenityClearance: {spot.Unit.Name} touches {use} at ({i},{j})");
                    }
            }

            // A SHOPFRONT IS NOT A FAULT. It was judged one - at first one to a block, then
            // one to a street - and neither line was ever the user's; both were mine, and
            // both cost the quarter its variety (see ShopsPerStreet). Off by default, and
            // still judged if anyone turns the limit back on.
            for (int s = 0; s < 4 && ShopsPerStreet > 0; s++)
            {
                int shops = plan.Spots.Count(x => x.Shop && (x.Side == s || x.SideB == s));
                if (shops > ShopsPerStreet)
                    plan.Faults.Add($"TwoShops: {shops} units carry shopfronts on the {SideName[s]} street");
            }

            if ((plan.Klass == Klass.Block || plan.Klass == Klass.Court) && plan.M.MainFrontage < 65)
                plan.Faults.Add($"WeakFrontage: the {SideName[plan.Artery]} main edge is only " +
                                $"{plan.M.MainFrontage}% built (target 70-85%)");
            if (plan.Klass == Klass.Court && plan.M.CourtEnclosure < 55)
                plan.Faults.Add($"OpenCourt: the building band encloses only {plan.M.CourtEnclosure}% of the court");

            if (plan.M.VehicleEntries > 2)
                plan.Faults.Add($"TooManyEntries: {plan.M.VehicleEntries} vehicle mouths fragment the frontage");
            int alleyCells = plan.Ground.Cast<Use>().Count(use => use == Use.Alley);
            int alleyMouths = plan.Accesses.Count(a => a.Vehicle && a.Purpose == "through alley");
            if (alleyCells > 0 && alleyMouths != 2)
                plan.Faults.Add($"BlindAlley: the service alley has {alleyMouths} street connection(s), not two");

            if (plan.Artery >= 0)
            {
                int length = plan.Artery == 0 || plan.Artery == 2 ? plan.W : plan.D;
                for (int at = Walk; at < length - Walk; at++)
                {
                    var (i, j) = EdgeCell(plan, plan.Artery, at);
                    if (plan.Ground[i, j] == Use.Parking)
                        plan.Faults.Add($"MainFrontParking: a bay occupies the main frontage at {at}");
                }
            }

            foreach (var gap in plan.Gaps)
                if (gap.Use != Use.Paved && gap.Use != Use.Drive && gap.Use != Use.Parking &&
                    gap.Use != Use.Cafe && gap.Use != Use.Subway)
                    plan.Faults.Add($"AnonymousGap: {gap}");

            // no one building is the block: the placer's share rule, measured back
            if ((plan.Klass == Klass.Block || plan.Klass == Klass.Court) && plan.M.Share > ShareMost)
                plan.Faults.Add($"Monolith: one unit's box covers {plan.M.Share}% of the inner ground");

            // AND NOTHING STANDS ON IT IS A FAULT TOO. A gym on a quarter's whole cell was
            // one small thing and paving to the kerb in every direction (the user,
            // 2026-08-28: "izgleda preglupo... dodaj pravilo da ne smes ovako da punis
            // blokove"). What stands on a block - its houses, their forecourts, its lot -
            // has to be a real share of the ground inside its pavement.
            int fill = BuiltCoverage(plan);
            int requiredFill = RequiredBuiltCoverage(plan);
            if (fill < requiredFill)
                plan.Faults.Add($"Bare: what stands on the block covers {fill}% of its inner ground, " +
                                $"below the {requiredFill}% {plan.Klass} minimum");

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
                // a lone house cannot man four corners, and is not asked to: its block is
                // one house and its garden by construction (see Alone)
                if (!plan.Lone && (plan.Klass == Klass.Block || plan.Klass == Klass.Court))
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
            sb.Append($"main frontage {m.MainFrontage}%, access {m.PedestrianEntries} ped/{m.VehicleEntries} vehicle, ");
            sb.Append($"alley {m.AlleyCells}, rear parking {m.RearParking}, verge {m.Verge}, ");
            sb.Append($"court {m.CourtCells} ({m.CourtEnclosure}% enclosed), empty {m.Empty}");
            if (m.Empty > 0) sb.Append($" at {m.EmptyAt}");
            if (m.Repeats > 0) sb.Append($", {m.Repeats} repeat(s)");
            foreach (var gap in plan.Gaps) sb.Append($"\n    gap: {gap}, {gap.Depth} deep");
            for (int side = 0; side < 4; side++)
                if (plan.Role[side] != EdgeRole.Closed)
                    sb.Append($"\n    edge {SideName[side]}: {plan.Role[side]}");
            foreach (var refused in plan.Refused) sb.Append($"\n    REFUSED: {refused}");
            foreach (var fault in plan.Faults) sb.Append($"\n    FAULT: {fault}");
            return sb.ToString();
        }
    }
}
