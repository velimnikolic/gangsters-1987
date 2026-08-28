using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// What stands around the core: the belt of park on the land side, and the five
    /// residential quarters beyond it.
    ///
    /// THE CITY IS A THREE BY THREE OF CORE-SIZED CELLS (the user, 2026-08-27: "treba 5
    /// residental kvartova velicine core koji okruzuju ovo... oni treba da idu posle parka").
    /// The core with its belt is the middle cell; the river takes the whole column on its
    /// own side, which leaves exactly five cells for the quarters - three down the land
    /// column and one each above and below the core:
    ///
    ///     NW |  N  | river          the land column is on the west when the river is east,
    ///     ---+-----+-------         and on the east when it is west; the drawing is the
    ///      W | CORE| river          same either way, read in a mirror
    ///     ---+-----+-------
    ///     SW |  S  | river
    ///
    /// Nothing here is a district of its own. A quarter is a lattice of residential blocks
    /// dealt into the same <see cref="CoreLayout.Plan"/> as the core's own, so the road
    /// reader draws its streets, the lane graph carries its traffic and the same verdict
    /// judges it - which is the whole reason the residential block was made an elastic
    /// block rather than a quarter with a layout of its own
    /// (Docs/residential-quarter-plan.md §1.1).
    /// </summary>
    public static partial class CoreLayout
    {
        // ---------------------------------------------------------------- the land belt

        /// <summary>
        /// How the belt on the land side is broken so that the core is not sealed in.
        ///
        /// The north and south belts interrupt nothing - no street runs the whole height of
        /// the core - but the land-side belt lies across EVERY street the rows declare. Left
        /// whole it would leave the downtown with one way in from the land (the boulevard)
        /// and one from the water (the quay street), and the harness has already shown what
        /// a single gate does to traffic. So the boulevard crosses it, as it crosses the
        /// river, and one or two of the rows' own streets cross it too - chosen the way the
        /// bridges are, well apart and clear of the ends.
        /// </summary>
        const int GatesMin = 1, GatesMax = 2;
        const int GateApart = 12;

        /// <summary>The belt on the land side is an ordinary strip of green, 30-40 m: the one
        /// big park of the city is already on the north or the south (<see cref="Belt"/>).</summary>
        const int LandBeltMin = 6, LandBeltMax = 8;

        /// <summary>How deep a quarter's blocks are dealt, and how wide, in cells, street
        /// included. A block of 60-85 m across and 65-105 m deep is what the recipe's
        /// <c>Block</c> class is, and what an American residential block of the period is.
        /// One pitch for the whole city: it is what makes the drawing read as a grid.</summary>
        const int QuarterWidthMin = 15, QuarterWidthMax = 20;
        const int QuarterDepthMin = 16, QuarterDepthMax = 24;

        /// <summary>How many of a quarter's cells are given over to a park, in percent (the
        /// user, 2026-08-27: "10%"). Never two facing each other, and never in the ring of
        /// cells along the belt - that would be a park looking at a park across the park
        /// drive, which is the one thing the drawing may not do.</summary>
        const int ParkPercent = 10;

        /// <summary>How many of a quarter's cells are a YARD BLOCK - the skatepark, the beach
        /// gym, the car yard, each on a plot of its own (the user, 2026-08-28: "nek budu svoj
        /// zaseban tip bloka koji se pojavljuju u residential kvartovima ali ne sad
        /// precesto i treba lepo da su rasporedjeni, da ne budu jedan uz drugi"). One cell in
        /// sixteen, never facing another yard and never facing a park - two open plots side
        /// by side is a hole in the quarter, not a place.</summary>
        const int YardPercent = 6;

        /// <summary>How much likelier the gym is than the car yard or the skatepark when a
        /// yard block is dealt (the user, 2026-08-28: "nek se desava cesce od caryard i
        /// skatepark").</summary>
        const int GymShare = 3;

        /// <summary>
        /// Which lot stands on a plot cut out of a cell this size - the gym oftener than the
        /// other two, and only where the lot AND its pavement ring fit. Null when none of
        /// them fits, and then the cell stays a block of houses.
        /// </summary>
        static string YardFor(int w, int d, System.Random dice)
        {
            var pool = new List<string>();
            foreach (string name in ResidentialLot.OwnBlock)
                for (int n = 0; n < (name == "gym" ? GymShare : 1); n++) pool.Add(name);
            Dice.Shuffle(pool, dice);
            foreach (string name in pool)
                if (YardSize(name, w, d, out _, out _)) return name;
            return null;
        }

        /// <summary>
        /// How big the yard block itself is: the lot plus the pavement ring every block
        /// carries, AND NO MORE (the user, 2026-08-28: "to treba da bude mali blokcic sa
        /// standardnom sirinom pavementa"). A gym given a whole quarter cell was a paved
        /// field with a bench in the middle of it.
        /// </summary>
        static bool YardSize(string name, int w, int d, out int yw, out int yd)
        {
            yw = yd = 0;
            ResidentialUnit unit = null;
            foreach (var u in ResidentialUnits.All) if (u.Name == name) { unit = u; break; }
            if (unit == null) return false;
            int ring = 2 * ResidentialLot.Walk;
            if (unit.CW + ring <= w && unit.CD + ring <= d) { yw = unit.CW + ring; yd = unit.CD + ring; return true; }
            if (unit.CD + ring <= w && unit.CW + ring <= d) { yw = unit.CD + ring; yd = unit.CW + ring; return true; }
            return false;
        }

        /// <summary>
        /// The belt on the land side and the five quarters beyond it, dealt into the plan
        /// the core is already in.
        /// </summary>
        /// <param name="lo">the rows' west end, in cells</param>
        /// <param name="hi">the rows' east end, in cells</param>
        /// <param name="zSouth">the outer kerb of the southernmost street, in metres</param>
        /// <param name="zNorth">the outer kerb of the northernmost street, in metres</param>
        static void Ring(Plan plan, System.Random dice, bool riverEast, int lo, int hi,
                         float zSouth, float zNorth)
        {
            int beltDeep = dice.Next(LandBeltMin, LandBeltMax + 1);
            int dir = riverEast ? -1 : 1;               // outward, away from the river
            int edge = riverEast ? lo : hi;             // the rows' land end, in cells
            int kerb = edge + dir * StreetGap;          // the edge street's far kerb
            int beltFar = kerb + dir * beltDeep;        // the belt's far edge
            int drive = beltFar + dir * StreetGap;      // the park drive's far kerb: the core cell's edge

            int j0 = Mathf.RoundToInt(zSouth / Cell), j1 = Mathf.RoundToInt(zNorth / Cell);
            plan.Ring = new RingLine
            {
                LandWest = riverEast,
                Kerb = kerb, BeltFar = beltFar, Drive = drive,
                J0 = j0, J1 = j1,
                Width = Mathf.Abs(drive - (riverEast ? hi : lo)),
                Depth = j1 - j0,
            };

            Gates(plan, dice, riverEast, edge, drive, j0, j1);
            LandBelt(plan, riverEast, kerb, beltFar, j0 + StreetGap, j1 - StreetGap);
            Quarters(plan, dice, riverEast, lo, hi, drive, j0, j1);
        }

        /// <summary>Where the core's edges are once the belt is on, and how big a quarter
        /// is: everything the drawing beyond the core is measured from.</summary>
        public struct RingLine
        {
            public bool LandWest;                 // the land side is the west
            public int Kerb, BeltFar, Drive;      // in cells: the edge street's far kerb, the belt, the park drive's far kerb
            public int J0, J1;                    // the core cell, in cells along z
            public int Width, Depth;              // a quarter, in cells
        }

        /// <summary>
        /// Which of the core's streets cross the belt, and how far every street on the land
        /// side runs.
        ///
        /// A street that is not a gate stops at the edge street's far kerb - a T, with the
        /// belt beyond it. A gate runs on to the park drive's far kerb, which is the ring
        /// road round the belt and the quarters' own first street.
        /// </summary>
        static void Gates(Plan plan, System.Random dice, bool riverEast, int edge, int drive,
                          int j0, int j1)
        {
            float kerb = (edge + (riverEast ? -1 : 1) * StreetGap) * Cell;
            float far = drive * Cell;

            // the boulevard always crosses, being the city's main road
            var gates = new List<int> { 0 };
            var bands = new List<int>();
            for (int b = 1; b < plan.Bands.Count; b++)
            {
                var band = plan.Bands[b];
                if (band.width < band.height) continue;
                // the streets behind the two belt rows are the ring road round the belt, not
                // gates through it: they run out to the land park drive and meet it at the
                // corner, and without them the corner is ground with no road on it
                if (Mathf.Abs(band.yMin - j0 * Cell) < 0.1f || Mathf.Abs(band.yMax - j1 * Cell) < 0.1f)
                { gates.Add(b); continue; }
                // a gate: it has to keep clear of the ends so a stretch of belt is left either side
                if (band.yMin - j0 * Cell < GateApart * Cell || j1 * Cell - band.yMax < GateApart * Cell) continue;
                bands.Add(b);
            }
            Dice.Shuffle(bands, dice);
            int wanted = dice.Next(GatesMin, GatesMax + 1);
            foreach (int b in bands)
            {
                if (gates.Count - 1 >= wanted) break;
                var band = plan.Bands[b];
                bool clear = true;
                foreach (int other in gates)
                {
                    var box = plan.Bands[other];
                    if (band.yMax + GateApart * Cell > box.yMin && band.yMin - GateApart * Cell < box.yMax)
                    { clear = false; break; }
                }
                if (!clear) continue;
                gates.Add(b);
            }

            plan.Gates.Clear();
            foreach (int b in gates)
            {
                var band = plan.Bands[b];
                plan.Gates.Add(Rect.MinMaxRect(Mathf.Min(kerb, far), band.yMin, Mathf.Max(kerb, far), band.yMax));
            }

            // and now every band's land end: the gates to the park drive, the rest to the
            // edge street's far kerb. River() ran them a street's width past the edge street
            // into what is now the belt, which would be a road through a park
            for (int b = 0; b < plan.Bands.Count; b++)
            {
                var band = plan.Bands[b];
                // ONLY THE ROWS' OWN STREETS, which run east and west. The river declares two
                // of its own that run north and south - the quay street and the road along the
                // far bank - and giving one of those a land end turns it into a band across
                // the whole drawing: the first run of this laid road over the river itself
                if (band.width < band.height) continue;
                float land = gates.Contains(b) ? far : kerb;
                if (riverEast) plan.Bands[b] = Rect.MinMaxRect(land, band.yMin, band.xMax, band.yMax);
                else plan.Bands[b] = Rect.MinMaxRect(band.xMin, band.yMin, land, band.yMax);
            }
            // the edge street and the park drive, declared the whole height of the core cell
            // so that they are one straight street rather than what the rings happen to leave
            float edgeX = edge * Cell;
            plan.Bands.Add(Span(edgeX, kerb, j0 * Cell, j1 * Cell));
            plan.Bands.Add(Span(far, far - (riverEast ? -1 : 1) * StreetGap * Cell, j0 * Cell, j1 * Cell));
        }

        /// <summary>
        /// The belt on the land side: one park the height of the core, cut into stretches by
        /// the gates - the same shape as the promenade between its bridges. The stretches are
        /// one park in several pieces and are marked as such, so the run rule does not count
        /// them against each other.
        /// </summary>
        static void LandBelt(Plan plan, bool riverEast, int kerb, int beltFar, int j0, int j1)
        {
            var cuts = new List<Rect>(plan.Gates);
            cuts.Sort((one, other) => one.yMin.CompareTo(other.yMin));
            float from = j0 * Cell;
            float x0 = Mathf.Min(kerb, beltFar) * Cell, x1 = Mathf.Max(kerb, beltFar) * Cell;
            int deep = Mathf.Abs(beltFar - kerb);

            for (int k = 0; k <= cuts.Count; k++)
            {
                float to = k < cuts.Count ? cuts[k].yMin : j1 * Cell;
                int cells = Mathf.RoundToInt((to - from) / Cell);
                if (cells > 0)
                {
                    var park = Park(plan.Parks.Count + 1, deep, cells);
                    park.Pivot = new Vector2(x0, from);
                    plan.Parks.Add(park);
                    plan.BeltParks.Add(park);
                }
                if (k < cuts.Count) from = cuts[k].yMax;
            }
        }

        // ------------------------------------------------------------------ the quarters

        /// <summary>
        /// How wide a residential block is, in cells, its pavement ring included: 60-85 m,
        /// which is what the recipe's <c>Block</c> class takes and what a residential block of
        /// the period is. The widths within a quarter are NOT all the same - see
        /// <see cref="Deal"/>.
        /// </summary>
        const int BlockWideMin = 12, BlockWideMax = 17;

        /// <summary>
        /// The fine band a quarter's depth is dealt in, in cells: 30-40 m.
        ///
        /// A block takes one band or two. One is a terrace row - a shallow block with its back
        /// to the next street; two is an ordinary block of 75-95 m with a yard in the middle.
        /// The bands are dealt ONCE for a whole row of quarters and every column takes its own
        /// run of them, so a street across a quarter breaks where a column runs two bands
        /// together and goes through where it does not. That is the difference between a grid
        /// and a quarter: the lines are all there, but not every one of them is a street the
        /// whole way (the user, 2026-08-27: "treba randomnes u ulicama nesto izmedju kako core
        /// layoutuje ulice i blokove i ovog grid sistema").
        /// </summary>
        const int BandMin = 6, BandMax = 8;

        /// <summary>How often a column runs two bands together into one deeper block.</summary>
        const double DeepOdds = 0.5;

        /// <summary>
        /// The five quarters, each the size of the core cell, laid round it.
        ///
        /// The divisions are dealt ONCE PER AXIS and shared by the quarters that lie along it:
        /// the three down the land column share their widths, the two above and below the core
        /// share theirs, and each row of quarters shares its bands. Dealt quarter by quarter
        /// they would each be regular and none would line up with its neighbour - and where two
        /// streets meet a boundary a cell or two apart, the road reader runs their junctions
        /// together and calls it a fault.
        /// </summary>
        static void Quarters(Plan plan, System.Random dice, bool riverEast, int lo, int hi,
                             int drive, int j0, int j1)
        {
            int coreLo = riverEast ? drive : lo;          // the core cell, in cells along x
            int coreHi = riverEast ? hi : drive;
            int wide = coreHi - coreLo, deep = j1 - j0;
            if (wide < BlockWideMin || deep < BandMin) return;

            int landLo = riverEast ? coreLo - wide : coreHi;
            int landHi = riverEast ? coreLo : coreHi + wide;

            // the quarters above and below the core lay the street on the boundary with the
            // land column; every other boundary is a park drive already
            int midLo = riverEast ? coreLo + StreetGap : coreLo;
            int midHi = riverEast ? coreHi : coreHi - StreetGap;

            var colsLand = Deal(landHi - landLo, BlockWideMin, BlockWideMax, dice, out int skirtLand);
            var colsMid = Deal(midHi - midLo, BlockWideMin, BlockWideMax, dice, out int skirtMid);
            var bandsNorth = Deal(deep, BandMin, BandMax, dice, out int skirtNorth);
            var bandsSouth = Deal(deep, BandMin, BandMax, dice, out int skirtSouth);

            // THE BOULEVARD RUNS TO THE EDGE OF THE CITY (the user, 2026-08-27: "bulevar corea
            // treba da se nastavi do ivice grada"). It already crosses the belt as a gate and
            // the river as a bridge; here it goes on through the middle quarter of the land
            // column, which is dealt in two - the ground south of it and the ground north of
            // it - with the boulevard itself as the street between them.
            int boulLo = Mathf.RoundToInt(plan.MainRoad.x / Cell);
            int boulHi = Mathf.RoundToInt(plan.MainRoad.y / Cell);
            // the middle quarter of the land column lays the street on its own north and
            // south edges: the three quarters down that column meet each other, and the park
            // drives that separate the core from the ones above and below do not run out here
            var bandsBelow = Deal(boulLo - j0 - StreetGap, BandMin, BandMax, dice, out int skirtBelow);
            var bandsAbove = Deal(j1 - boulHi - StreetGap, BandMin, BandMax, dice, out int skirtAbove);

            int landWide = landHi - landLo, midWide = midHi - midLo;
            Lay(plan, dice, "NW", landLo, landWide, colsLand, skirtLand, j1, deep, bandsNorth, skirtNorth);
            Lay(plan, dice, "SW", landLo, landWide, colsLand, skirtLand, j0 - deep, deep, bandsSouth, skirtSouth);
            Lay(plan, dice, "W below the boulevard", landLo, landWide, colsLand, skirtLand,
                j0 + StreetGap, boulLo - j0 - StreetGap, bandsBelow, skirtBelow);
            Lay(plan, dice, "W above the boulevard", landLo, landWide, colsLand, skirtLand,
                boulHi, j1 - boulHi - StreetGap, bandsAbove, skirtAbove);
            Lay(plan, dice, "N", midLo, midWide, colsMid, skirtMid, j1, deep, bandsNorth, skirtNorth);
            Lay(plan, dice, "S", midLo, midWide, colsMid, skirtMid, j0 - deep, deep, bandsSouth, skirtSouth);

            var boulevard = plan.Bands[0];
            float edge = (riverEast ? landLo : landHi) * Cell;
            plan.Bands[0] = riverEast
                ? Rect.MinMaxRect(edge, boulevard.yMin, boulevard.xMax, boulevard.yMax)
                : Rect.MinMaxRect(boulevard.xMin, boulevard.yMin, edge, boulevard.yMax);
        }

        /// <summary>
        /// One quarter, or one part of one: a column of blocks for every width, each column
        /// taking the bands one or two at a time.
        /// </summary>
        static void Lay(Plan plan, System.Random dice, string name, int i0, int wide,
                        List<int> cols, int skirtX, int j0, int deep, List<int> bands, int skirtZ)
        {
            if (cols == null || bands == null || cols.Count == 0 || bands.Count == 0)
            {
                // ground no run of blocks divides exactly - a strip of fifty or sixty metres
                // between the boulevard and the park drive, say. A car park, which is what the
                // odd corner of an American city of the period is, rather than a guess or a
                // hole: a hole is a fault and the whole deal would be thrown away for it
                if (wide > 0 && deep > 0)
                    plan.Lots.Add(Rect.MinMaxRect(i0 * Cell, j0 * Cell, (i0 + wide) * Cell, (j0 + deep) * Cell));
                plan.Rows.Add($"{name}: {wide * 5}x{deep * 5} m divides into no run of blocks; a car park");
                return;
            }

            // the strips the blocks did not divide into: a row of parking along the near edge,
            // with a street between it and the first block
            if (skirtX > 0) plan.Lots.Add(Rect.MinMaxRect(i0 * Cell, j0 * Cell,
                                                          (i0 + skirtX) * Cell, (j0 + deep) * Cell));
            if (skirtZ > 0) plan.Lots.Add(Rect.MinMaxRect((i0 + skirtX) * Cell, j0 * Cell,
                                                          (i0 + wide) * Cell, (j0 + skirtZ) * Cell));
            if (skirtX > 0) i0 += skirtX + StreetGap;
            if (skirtZ > 0) j0 += skirtZ + StreetGap;

            var cells = new List<Rect>();
            var rim = new List<bool>();
            int at = i0;
            for (int c = 0; c < cols.Count; c++)
            {
                int down = j0, b = 0;
                while (b < bands.Count)
                {
                    int run = b + 1 < bands.Count && dice.NextDouble() < DeepOdds ? 2 : 1;
                    int d = 0;
                    for (int k = 0; k < run; k++) d += bands[b + k] + (k > 0 ? StreetGap : 0);
                    cells.Add(new Rect(at * Cell, down * Cell, cols[c] * Cell, d * Cell));
                    rim.Add(c == 0 || c == cols.Count - 1 || b == 0 || b + run >= bands.Count);
                    down += d + StreetGap;
                    b += run;
                }
                at += cols[c] + StreetGap;
            }

            // a tenth of the ground is park, never two of them facing each other and never on
            // the quarter's own edge - out there one would look at the belt across the park
            // drive, or at the park in the quarter across the street
            int want = cells.Count * ParkPercent / 100;
            var order = new List<int>();
            for (int k = 0; k < cells.Count; k++) if (!rim[k]) order.Add(k);
            Dice.Shuffle(order, dice);
            var green = new HashSet<int>();
            foreach (int k in order)
            {
                if (green.Count >= want) break;
                bool touches = false;
                foreach (int other in green) if (Faces(cells[k], cells[other])) { touches = true; break; }
                if (!touches) green.Add(k);
            }

            // the yard blocks: a few cells of the quarter given whole to one lot, spread out
            // - never facing each other and never facing a park. Which lot stands on which
            // is by SIZE: the skatepark is 40 x 35 m and wants a big plot, the gym and the
            // car yard fit a small one, and they are dealt in turn so a quarter does not get
            // three of the same
            int wantYards = cells.Count * YardPercent / 100;
            var yards = new Dictionary<int, string>();
            if (wantYards > 0)
            {
                var open = new List<int>();
                for (int k = 0; k < cells.Count; k++) if (!rim[k] && !green.Contains(k)) open.Add(k);
                Dice.Shuffle(open, dice);
                foreach (int k in open)
                {
                    if (yards.Count >= wantYards) break;
                    bool touches = false;
                    foreach (int other in yards.Keys) if (Faces(cells[k], cells[other])) { touches = true; break; }
                    foreach (int other in green) if (!touches && Faces(cells[k], cells[other])) touches = true;
                    if (touches) continue;
                    int w = Mathf.RoundToInt(cells[k].width / Cell), d = Mathf.RoundToInt(cells[k].height / Cell);
                    string unit = YardFor(w, d, dice);
                    if (unit == null) continue;
                    yards[k] = unit;
                }
            }

            int stood = 0, lots = 0;
            for (int k = 0; k < cells.Count; k++)
            {
                var box = cells[k];
                int w = Mathf.RoundToInt(box.width / Cell), d = Mathf.RoundToInt(box.height / Cell);
                if (yards.TryGetValue(k, out string yardUnit) && YardSize(yardUnit, w, d, out int yw, out int yd))
                {
                    // the little block itself in the cell's corner, and what the cell has
                    // left over is a car park - the same answer the deal gives every other
                    // odd corner of ground rather than leaving a hole in the quarter
                    var yard = CoreLayout.Yard(plan.Residential.Count + 1, yw, yd, yardUnit);
                    yard.Pivot = new Vector2(box.xMin, box.yMin);
                    plan.Residential.Add(yard);
                    stood++;
                    float xCut = box.xMin + yw * Cell, zCut = box.yMin + yd * Cell;
                    if (w - yw >= 2)
                    {
                        plan.Lots.Add(Rect.MinMaxRect(xCut, box.yMin, box.xMax, box.yMax));
                        lots++;
                    }
                    if (d - yd >= 2)
                    {
                        plan.Lots.Add(Rect.MinMaxRect(box.xMin, zCut, xCut, box.yMax));
                        lots++;
                    }
                    continue;
                }
                if (green.Contains(k))
                {
                    var park = Park(plan.Parks.Count + 1, w, d);
                    park.Pivot = new Vector2(box.xMin, box.yMin);
                    plan.Parks.Add(park);
                    continue;
                }
                if (ResidentialLot.Classify(w - 2 * ResidentialLot.Walk,
                                            d - 2 * ResidentialLot.Walk) != null)
                {
                    // WHICH SIDE THE SHOPS LOOK AT IS DEALT, and it is dealt per block. Given
                    // the same side every time, every block of a size put its shopfront on the
                    // same corner and worked round from there - a hundred and fifty copies of
                    // one block (the user, 2026-08-27: "vrtis i dalje iste blokove")
                    var block = Res(plan.Residential.Count + 1, w, d, dice.Next(4));
                    block.Pivot = new Vector2(box.xMin, box.yMin);
                    plan.Residential.Add(block);
                    stood++;
                }
                else
                {
                    // no class for this rectangle: a car park rather than a guess
                    plan.Lots.Add(box);
                    lots++;
                }
            }
            plan.Rows.Add($"{name}: {cols.Count} column(s), {cells.Count} block(s) - {stood} of houses, " +
                          $"{green.Count} green, {yards.Count} yard(s), {lots} car park(s)");
        }

        /// <summary>
        /// Divides a length into pieces between <paramref name="min"/> and
        /// <paramref name="max"/> cells with a street between each pair, and shakes them about
        /// so that they are not all of a size.
        ///
        /// The shaking is the point of it. Blocks all the same size read as an estate laid out
        /// by one hand in one afternoon; a city block is what was left between streets that
        /// were put down at different times.
        /// </summary>
        /// <summary>
        /// The same, but where the length divides into no run of blocks at all it gives up a
        /// strip at the near end to a car park and divides what is left.
        ///
        /// A hundred and sixty metres, say, is neither two bands nor three, and the first cut
        /// of this simply laid a car park of 450 x 160 m instead - which is not a car park, it
        /// is a runway. Twenty or thirty metres of parking along the edge and the rest in
        /// blocks is the honest answer, and it is what the edge of a quarter looks like.
        /// </summary>
        static List<int> Deal(int length, int min, int max, System.Random dice, out int skirt)
        {
            for (int cut = 0; cut <= 15; cut++)
            {
                if (cut > 0 && cut < 4) continue;         // a strip narrower than 20 m is no car park
                int usable = length - (cut == 0 ? 0 : cut + StreetGap);
                if (usable < min) break;
                var run = Deal(usable, min, max, dice);
                if (run == null) continue;
                skirt = cut;
                return run;
            }
            skirt = 0;
            return null;
        }

        static List<int> Deal(int length, int min, int max, System.Random dice)
        {
            if (length < min) return null;
            int most = (length + StreetGap) / (min + StreetGap);
            int least = Mathf.Max(1, (length + StreetGap) / (max + StreetGap));
            if (most < 1) return null;
            // a count near the one the dice asked for, searched OUTWARD in both directions:
            // counted down alone, a length that wanted MORE blocks than the throw asked for
            // never found any, and three quarters of the city went unbuilt for it
            int want = Mathf.Clamp(dice.Next(least, most + 1), 1, most);
            for (int spread = 0; spread <= most; spread++)
            for (int side = 0; side < 2; side++)
            {
                int n = side == 0 ? want + spread : want - spread;
                if (n < 1 || n > most || (spread == 0 && side == 1)) continue;
                int ground = length - (n - 1) * StreetGap;
                if (ground < n * min || ground > n * max) continue;
                int size = ground / n, over = ground - size * n;
                var sizes = new List<int>();
                for (int k = 0; k < n; k++) sizes.Add(size + (k < over ? 1 : 0));
                for (int k = 0; k < n * 6; k++)
                {
                    int a = dice.Next(n), b = dice.Next(n);
                    if (a == b) continue;
                    int move = dice.Next(1, 3);
                    if (sizes[a] - move < min || sizes[b] + move > max) continue;
                    sizes[a] -= move;
                    sizes[b] += move;
                }
                return sizes;
            }
            return null;
        }
    }
}
