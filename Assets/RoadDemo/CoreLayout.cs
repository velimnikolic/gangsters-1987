using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// The city core's layout: the blocks harvested out of the POLYGON City demo scene,
    /// stood in ROWS either side of a boulevard, a street between every two. One source
    /// of truth for the drawing in the editor and the district in the game
    /// (Docs/core-district-plan.md, Docs/synty-demo-anatomy.md).
    ///
    /// A layout is a <see cref="Plan"/>: where every block stands and which way it is
    /// turned, which way each alley runs, and where the main road lies. Two plans exist.
    /// <see cref="Synty"/> is the demo's own arrangement with its 10 m streets opened to
    /// the city's by a list of CUTS - the one arrangement of these blocks that was known to
    /// work, kept as the reference. <see cref="Roll"/> deals a new one from a seed: the
    /// blocks shuffled into rows of a depth, a 15 m street or a 5 m alley between
    /// neighbours, the rows stacked out from the boulevard. A block shallower than its row
    /// keeps the ground behind it as its car park - the demo's own rule for a bay.
    ///
    /// Neither plan is trusted on its own word. <see cref="Arrange"/> draws the roads off
    /// the plan (<see cref="CoreRoads.Build"/>) and reads the verdict - ground left bare, a
    /// block with no road down one side, a stub of road between two junctions - and deals
    /// again from the next sub-seed until the verdict is clean. Same seed, same city.
    ///
    /// All measures are multiples of 5 m, so every gap is a whole number of cells. Positions
    /// are the core's own coordinates (metres; +X east, +Z north; the main road runs along
    /// z -10..25). A host that stands the core somewhere else in the world does so through
    /// its DistrictFrame, never by editing these numbers.
    /// </summary>
    public static partial class CoreLayout
    {
        public const float Cell = 5f;
        public const string BlocksDir = "Assets/Prefabs/CoreBlocks/";
        /// <summary>The root the editor's sketch draws the core under. A scene that still
        /// holds one at Play would show two cores on top of each other.</summary>
        public const string SketchRoot = "CORE CITY (sketch)";

        /// <summary>A block prefab and where its pivot stood in the demo. The pivot is the
        /// one the harvest gave the prefab (the middle of its ground box, on the 5 m beat);
        /// the demo positions were read back by matching every piece of every prefab to
        /// the original scene - all seventeen matched to the piece.</summary>
        public struct Stand
        {
            public string Prefab;
            public float X, Z;

            /// <summary>Did the demo stand this block? The sixteen the harvest took did.
            /// A block made since - one of the catalog's own buildings with the pavement
            /// grown round it - never stood anywhere, so it has no place in the demo's
            /// arrangement and <see cref="Arrange"/> leaves it out of that one plan. It
            /// deals with the rest under every other seed.</summary>
            public bool Demo;

            public Stand(string prefab, float x, float z) { Prefab = prefab; X = x; Z = z; Demo = true; }

            /// <summary>A block the demo never stood, and so has no position in it.</summary>
            public Stand(string prefab) { Prefab = prefab; X = 0f; Z = 0f; Demo = false; }
        }

        /// <summary>The blocks the city deals from. The first sixteen are the demo's own,
        /// with the pivot each stood at; city-hall-block is left out, being the tray-baked
        /// copy of the same block as block-03 with fewer pieces. The ones after them were
        /// made later out of the catalog's buildings and have no demo position - see
        /// <see cref="Stand.Demo"/>.</summary>
        public static readonly Stand[] Blocks =
        {
            new Stand("block-01", -90f, -55f),
            new Stand("block-02", -85f, 15f),
            new Stand("block-03", -55f, -95f),
            new Stand("block-04", -50f, -120f),
            new Stand("block-05", -40f, 35f),
            new Stand("block-06", -35f, -40f),
            new Stand("block-07", -20f, -30f),
            new Stand("block-08", -20f, -95f),
            new Stand("block-09", 35f, -40f),
            new Stand("block-10", 35f, 15f),
            new Stand("block-11", 50f, 120f),
            new Stand("block-12", 50f, 50f),
            new Stand("block-13", 75f, -40f),
            new Stand("block-14", 115f, -20f),
            new Stand("block-15", 120f, 125f),
            new Stand("block-16", 120f, 55f),

            // One building each, with the pavement grown round it - the catalog's warehouse
            // yard, police station and nightclub, baked through the trays by
            // CoreBuildingBlocks (Tools/City/Core/Buildings/...). Big enough to be a block
            // on their own, and no arrangement of the demo's to stand in.
            new Stand("warehouse-block"),
            new Stand("police-station-block"),
            new Stand("nightclub-block"),
        };

        /// <summary>
        /// A line the demo layout is opened along. Everything whose box lies wholly on the
        /// far side of the line (at or beyond <see cref="At"/> for a positive delta, at or
        /// short of it for a negative one) and overlaps the line's span moves by
        /// <see cref="Delta"/>. A block that straddles the line never moves, which is
        /// what keeps a cut from tearing a block in two.
        /// </summary>
        public struct Cut
        {
            public bool Vertical;      // a line x = At (moves things along x); else z = At (along z)
            public float At;
            public float From, To;     // the span of the line along the other axis
            public float Delta;
            public string Why;
            public Cut(bool vertical, float at, float from, float to, float delta, string why)
            { Vertical = vertical; At = at; From = from; To = to; Delta = delta; Why = why; }
        }

        const float Any = 100000f;

        /// <summary>The demo's 10 m streets opened to the city's 15, and the main road to
        /// the city's 35 m boulevard. In the demo's coordinates; a cut's effect is judged
        /// against the demo positions, so their order does not matter.</summary>
        public static readonly Cut[] Cuts =
        {
            new Cut(false, 0f, -Any, Any, 25f, "the main road z -10..0 to a boulevard"),
            new Cut(true, -75f, -110f, -10f, -5f, "street S1, x -75..-65, opened westward so S2 and S3 run straight across the boulevard"),
            new Cut(true, 5f, -Any, Any, 5f, "street S2, x -5..5 (the 15 m stretch north gets a strip)"),
            new Cut(true, 100f, -Any, -10f, 5f, "street S3 south, x 90..100"),
            new Cut(true, 100f, 0f, 105f, 5f, "street S3 middle, x 95..105"),
            new Cut(true, 105f, 110f, Any, 5f, "street S3 north, x 95..105"),
            new Cut(false, -80f, -65f, 90f, -5f, "street z -80..-70, opened southward"),
            // the street z -120..-110 runs through block-04's bay; the block's box reaches
            // z -105, so the line is drawn there
            new Cut(false, -105f, -95f, -5f, -5f, "street z -120..-110, opened southward"),
            // the street z 95..105 is a car park's edge plus a 5 m lane: ten metres more
            new Cut(false, 105f, -Any, Any, 10f, "street z 95..105 and the lane z 100..105"),
        };

        /// <summary>The main road, kerb to kerb (z): the demo's z -10..0 opened to the
        /// boulevard. It runs the whole width of the core, in every plan - the rolled rows
        /// stand against the same two kerbs, so the boulevard's centre line (z 7.5) is the
        /// line the quarter is pinned to in the city whichever plan built it.</summary>
        public static Vector2 MainRoad
        {
            get
            {
                var north = Shift(new Rect(-Any, 0f, 2f * Any, Cell));
                return new Vector2(-10f, 0f + north.y);
            }
        }

        /// <summary>A one-way lane of the demo: five metres of bare asphalt between two
        /// kerbs, an arrow at each mouth. Direction +1 runs north (vertical) or east.</summary>
        public struct Lane
        {
            public bool Vertical;
            public float At;           // the lane's west (vertical) or south (horizontal) kerb
            public float From, To;     // its run along its own axis
            public int Direction;
            public Lane(bool vertical, float at, float from, float to, int direction)
            { Vertical = vertical; At = at; From = from; To = to; Direction = direction; }
            public Rect Box => Vertical ? new Rect(At, From, Cell, To - From) : new Rect(From, At, To - From, Cell);

            /// <summary>The lane moved by an offset.</summary>
            public Lane Moved(Vector2 by) => new Lane(Vertical, At + (Vertical ? by.x : by.y),
                                                      From + (Vertical ? by.y : by.x), To + (Vertical ? by.y : by.x), Direction);

            /// <summary>The lane turned about the origin by a yaw of whole quarters, its
            /// direction turned with it.</summary>
            public Lane Turned(int yaw)
            {
                var box = Box;
                var a = Turn(new Vector2(box.xMin, box.yMin), yaw);
                var b = Turn(new Vector2(box.xMax, box.yMax), yaw);
                var min = Vector2.Min(a, b);
                var max = Vector2.Max(a, b);
                var way = Turn(Vertical ? new Vector2(0f, Direction) : new Vector2(Direction, 0f), yaw);
                bool vertical = Mathf.Abs(way.y) > Mathf.Abs(way.x);
                int direction = vertical ? (way.y > 0f ? 1 : -1) : (way.x > 0f ? 1 : -1);
                return vertical ? new Lane(true, min.x, min.y, max.y, direction)
                                : new Lane(false, min.y, min.x, max.x, direction);
            }
        }

        /// <summary>The demo's lanes and the way they run. The directions are read off the
        /// arrow tiles where the demo painted one and chosen to alternate where it did not.</summary>
        public static readonly Lane[] Lanes =
        {
            new Lane(true, -100f, -140f, -100f, +1),
            new Lane(true, -75f, 0f, 65f, -1),
            new Lane(true, -45f, -50f, -10f, +1),
            new Lane(true, 60f, -70f, -10f, +1),
            new Lane(true, 60f, 0f, 35f, +1),
            new Lane(true, 135f, 110f, 140f, -1),
            new Lane(false, 65f, -70f, -10f, -1),
            new Lane(false, 100f, 40f, 95f, -1),
            new Lane(false, 105f, 105f, 135f, +1),
            new Lane(false, -50f, -45f, -5f, -1),
            new Lane(false, -145f, -95f, -5f, +1),
            new Lane(false, 35f, 5f, 60f, -1),
            new Lane(true, -40f, -110f, -80f, -1),
        };

        /// <summary>How far the cuts move a box that stands at <paramref name="box"/> in
        /// the demo (x, z as Rect x, y).</summary>
        public static Vector2 Shift(Rect box)
        {
            float dx = 0f, dz = 0f;
            foreach (var cut in Cuts)
            {
                float lo = cut.Vertical ? box.xMin : box.yMin;
                float hi = cut.Vertical ? box.xMax : box.yMax;
                float spanLo = cut.Vertical ? box.yMin : box.xMin;
                float spanHi = cut.Vertical ? box.yMax : box.xMax;
                if (spanHi <= cut.From + 0.01f || spanLo >= cut.To - 0.01f) continue;
                bool beyond = cut.Delta > 0f ? lo >= cut.At - 0.01f : hi <= cut.At + 0.01f;
                if (!beyond) continue;
                if (cut.Vertical) dx += cut.Delta; else dz += cut.Delta;
            }
            return new Vector2(dx, dz);
        }

        /// <summary>A point turned about the origin by a yaw in degrees, the way Unity turns
        /// it: a yaw of 90 carries north round to east.</summary>
        public static Vector2 Turn(Vector2 p, int yaw)
        {
            switch (((yaw % 360) + 360) % 360)
            {
                case 90: return new Vector2(p.y, -p.x);
                case 180: return new Vector2(-p.x, -p.y);
                case 270: return new Vector2(-p.y, p.x);
                default: return p;
            }
        }

        // ------------------------------------------------------------------ the blocks

        /// <summary>A block as measured off its instance: its ground, its shape, its height.
        /// The measure is taken unturned (<see cref="Ground0"/>, <see cref="Mask0"/>) and
        /// read through the block's yaw (<see cref="Ground"/>, <see cref="Mask"/>).</summary>
        public sealed class Block
        {
            public string Name;
            public GameObject Go;
            public Vector2 Pivot;              // where the plan stood it
            public Vector2 Shift;              // what the demo cuts add (the Synty plan only)
            public int Yaw;                    // 0, 90, 180 or 270
            /// <summary>Ground the block keeps beyond its own box - the car park behind a
            /// block shallower than its row. Empty when it keeps none.</summary>
            public Rect Lot;
            /// <summary>For a block composed on the spot rather than harvested (a
            /// residential block): which of its sides the shops look at - the side facing
            /// the busiest street. South, east, north, west; -1 for none.</summary>
            public int Artery = -1;
            /// <summary>For a yard block: the harvested lot that stands on it, by name.</summary>
            public string Unit;
            public Bounds Ground;              // pivot-relative box of its paving, turned
            public int CW, CD;                 // the box in cells
            public bool[,] Mask;               // which cells of the box the block fills, [i along x, j along z]
            public int Cells;
            public Bounds Ground0;             // the same, as measured, unturned
            public int CW0, CD0;
            public bool[,] Mask0;
            public float MaxH, MeanH;
            public int Buildings, Pieces;

            /// <summary>The ground box in the demo's frame, before the cuts.</summary>
            public Rect DemoBox => new Rect(Pivot.x + Ground.min.x, Pivot.y + Ground.min.z, CW * Cell, CD * Cell);
            /// <summary>The ground box where the block stands now.</summary>
            public Rect Box => new Rect(Pivot.x + Shift.x + Ground.min.x, Pivot.y + Shift.y + Ground.min.z, CW * Cell, CD * Cell);
            public Vector3 Position => new Vector3(Pivot.x + Shift.x, 0f, Pivot.y + Shift.y);
            public Quaternion Rotation => Quaternion.Euler(0f, Yaw, 0f);

            /// <summary>The unturned ground box turned by a yaw, about the pivot.</summary>
            public Rect Footprint(int yaw)
            {
                var a = CoreLayout.Turn(new Vector2(Ground0.min.x, Ground0.min.z), yaw);
                var b = CoreLayout.Turn(new Vector2(Ground0.max.x, Ground0.max.z), yaw);
                var min = Vector2.Min(a, b);
                var max = Vector2.Max(a, b);
                return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            }

            /// <summary>Reads the block through a yaw: its box and its mask turned about
            /// the pivot, cell for cell.</summary>
            public void Turn(int yaw)
            {
                Yaw = ((yaw % 360) + 360) % 360;
                var box = Footprint(Yaw);
                Ground = new Bounds(new Vector3(box.center.x, Ground0.center.y, box.center.y),
                                    new Vector3(box.width, Ground0.size.y, box.height));
                CW = Mathf.Max(1, Mathf.RoundToInt(box.width / Cell));
                CD = Mathf.Max(1, Mathf.RoundToInt(box.height / Cell));
                Mask = new bool[CW, CD];
                Cells = 0;
                for (int i = 0; i < CW; i++)
                    for (int j = 0; j < CD; j++)
                    {
                        // the turned cell's centre, carried back to the unturned frame
                        var c = CoreLayout.Turn(new Vector2(box.xMin + (i + 0.5f) * Cell, box.yMin + (j + 0.5f) * Cell), -Yaw);
                        int i0 = Mathf.FloorToInt((c.x - Ground0.min.x) / Cell);
                        int j0 = Mathf.FloorToInt((c.y - Ground0.min.z) / Cell);
                        bool filled = i0 >= 0 && j0 >= 0 && i0 < CW0 && j0 < CD0 && Mask0[i0, j0];
                        Mask[i, j] = filled;
                        if (filled) Cells++;
                    }
            }

            /// <summary>Is the block's whole side along this edge kerb - no bay, no step?
            /// An alley wants a wall the length of it, and only such a side gives one.</summary>
            public bool Straight(bool west)
            {
                int i = west ? 0 : CW - 1;
                for (int j = 0; j < CD; j++) if (!Mask[i, j]) return false;
                return true;
            }
        }

        /// <summary>
        /// Reads a block off an instance of its prefab standing at the origin, unturned:
        /// its ground (the paving the demo gave it, which is its kerb line), what stands
        /// on that ground, and how tall it is. Pieces are the instance's direct children -
        /// the harvest laid every piece flat under the block's root. Where it stands is
        /// the plan's to say afterwards.
        /// </summary>
        public static Block Measure(string name, GameObject go)
        {
            var block = new Block { Name = name, Go = go };
            var cover = new List<Bounds>();
            bool anyGround = false;
            float sumHA = 0f, sumA = 0f;
            foreach (Transform t in go.transform)
            {
                block.Pieces++;
                if (!BoxOf(t.gameObject, out var box)) continue;
                string piece = t.name;
                // "SM_Bld_" is what the pack calls a building; "building-" is what our own
                // kit bakes are called, and an industrial block is made of those
                bool building = Starts(piece, "SM_Bld_") || Starts(piece, "building-");
                bool ground = box.size.y <= 1.5f &&
                              (Starts(piece, "SM_Env_Sidewalk") || Starts(piece, "SM_Env_Grass") ||
                               Starts(piece, "SM_Env_Road") || piece.StartsWith("floor patch"));
                if (ground)
                {
                    if (!anyGround) { block.Ground0 = box; anyGround = true; }
                    else block.Ground0.Encapsulate(box);
                }
                // environment pieces by their footprint, props by their foot: a lamp's box
                // reaches out over the road it lights
                if (ground || building || Starts(piece, "SM_Env_")) cover.Add(box);
                else cover.Add(new Bounds(t.position, Vector3.one * 0.5f));
                if (!building) continue;
                block.Buildings++;
                float h = box.max.y, a = Mathf.Max(1f, box.size.x * box.size.z);
                if (h > block.MaxH) block.MaxH = h;
                sumHA += h * a;
                sumA += a;
            }
            if (!anyGround)
            {
                bool any = false;
                foreach (var box in cover)
                {
                    if (!any) { block.Ground0 = box; any = true; }
                    else block.Ground0.Encapsulate(box);
                }
            }
            block.MeanH = sumA > 0f ? sumHA / sumA : 0f;
            block.CW0 = Mathf.Max(1, Mathf.RoundToInt(block.Ground0.size.x / Cell));
            block.CD0 = Mathf.Max(1, Mathf.RoundToInt(block.Ground0.size.z / Cell));
            Shape(block, cover);
            block.Turn(0);
            return block;
        }

        /// <summary>A block described rather than measured - its ground box (pivot-relative,
        /// metres) and its mask - for a test or a simulation with no scene to stand it in.</summary>
        public static Block Describe(string name, Vector2 groundMin, int cw, int cd, bool[,] mask, float maxH = 0f)
        {
            var block = new Block
            {
                Name = name,
                Ground0 = new Bounds(new Vector3(groundMin.x + cw * Cell * 0.5f, 0f, groundMin.y + cd * Cell * 0.5f),
                                     new Vector3(cw * Cell, 0f, cd * Cell)),
                CW0 = cw, CD0 = cd, Mask0 = mask, MaxH = maxH, MeanH = maxH,
            };
            block.Turn(0);
            return block;
        }

        // -------------------------------------------------------------------- the parks

        /// <summary>What a park block is called. The core has no prefab for one - it is
        /// composed on the spot by <see cref="ParkWalk"/> and <c>ParkBlocks</c> - so the name
        /// is how everything downstream tells it from a harvested block.</summary>
        public const string ParkPrefix = "park-";

        public static bool IsPark(Block block) => block != null && block.Name.StartsWith(ParkPrefix);

        /// <summary>
        /// How many parks a core gets, and how big.
        ///
        /// One or two. The artists' own demo has exactly one green block in sixteen, and a
        /// core of two hundred and fifty metres with three parks in it is a garden suburb.
        /// A pocket park is block-08's size; a square is the next size up and carries one
        /// programme (2026-08-26, the user's call: "pojma nemam radi sta mislis").
        /// </summary>
        /// <remarks>
        /// A park costs the deal nothing worth speaking of, ONCE IT IS IN THE RASTER: 30 of
        /// 30 seeds clean, 21 of them on the first deal, mean 1.33 deals against the 1.60 the
        /// core needed with no park at all.
        ///
        /// It was not always so, and the wrong lesson was nearly learnt here. With the parks
        /// dealt but left out of the list handed to <see cref="CoreRoads.Build"/>, clean
        /// deals fell from 72 % to 24 % - and the obvious reading, that a park is simply hard
        /// to place, was wrong. The deal was spacing a row for a park and then nothing was
        /// filling the ground, so the verdict rightly called it bare. Three plausible fixes
        /// were measured against that phantom before the cause was found, and every one of
        /// them made it worse: letting a park fit any row whatever its depth (14 %), dealing
        /// its depth from the row range rather than its class (21 %), keeping a park from
        /// being the unit that sets a row's depth (mean 5.5 deals). Tuning against a
        /// misdiagnosis moves numbers and fixes nothing.
        /// </remarks>
        const int ParksMin = 1, ParksMax = 2;
        const int ParkGrowth = 2 * (ParkWalk.Band - 1);
        const int PocketMin = 6 + ParkGrowth, PocketMax = 7 + ParkGrowth;
        const int SquareMin = 8 + ParkGrowth, SquareMax = 12 + ParkGrowth;
        const double PocketOdds = 0.7;

        /// <summary>
        /// A park block of a given size: all cells filled, nothing standing on it.
        ///
        /// Its ground is measured from the origin rather than centred, because a park has no
        /// pivot of its own - it is composed into whatever rectangle the deal gives it, and
        /// the deal works in cells from a corner.
        /// </summary>
        public static Block Park(int index, int cw, int cd)
        {
            var mask = new bool[cw, cd];
            for (int i = 0; i < cw; i++)
                for (int j = 0; j < cd; j++) mask[i, j] = true;
            return Describe($"{ParkPrefix}{index:00}", Vector2.zero, cw, cd, mask);
        }

        /// <summary>
        /// Re-cuts a park to a new size, keeping its corner.
        ///
        /// Only a park may do this, and it is the whole reason a park is dealt differently
        /// from a block: a harvested block is a fixed thing measured off a prefab, but a park
        /// is composed to fit. A park shallower than its row would otherwise be given the
        /// ground behind it as a CAR PARK, which is what happens to a shallow block - and a
        /// car park behind a park is nonsense. It grows to the row instead.
        /// </summary>
        public static void Reshape(Block block, int cw, int cd)
        {
            if (!IsPark(block) || cw < 1 || cd < 1) return;
            var mask = new bool[cw, cd];
            for (int i = 0; i < cw; i++)
                for (int j = 0; j < cd; j++) mask[i, j] = true;
            block.CW0 = cw;
            block.CD0 = cd;
            block.Mask0 = mask;
            block.Ground0 = new Bounds(new Vector3(cw * Cell * 0.5f, 0f, cd * Cell * 0.5f),
                                       new Vector3(cw * Cell, 0f, cd * Cell));
            block.Turn(block.Yaw);
        }

        // ------------------------------------------------------------- the residential

        /// <summary>What a residential block is called. Like a park it has no prefab: the
        /// deal gives it a rectangle and <c>ResidentialLot</c> divides it, so the name is
        /// how everything downstream tells it from a harvested block.</summary>
        public const string ResPrefix = "res-";

        public static bool IsRes(Block block) => block != null && block.Name.StartsWith(ResPrefix);

        // ------------------------------------------------------------------ the yards

        /// <summary>What a yard block is called: a block that is ONE LOT - the skatepark, the
        /// beach gym, the car yard - standing on its own plot in a residential quarter (the
        /// user, 2026-08-28). Like a park and a residential block it has no prefab: the deal
        /// gives it a rectangle and <c>ResidentialLot.Yard</c> lays it out.</summary>
        public const string YardPrefix = "yard-";

        public static bool IsYard(Block block) => block != null && block.Name.StartsWith(YardPrefix);

        /// <summary>A yard block of a given size, carrying the name of the unit that stands
        /// on it.</summary>
        public static Block Yard(int index, int cw, int cd, string unit)
        {
            var mask = new bool[cw, cd];
            for (int i = 0; i < cw; i++)
                for (int j = 0; j < cd; j++) mask[i, j] = true;
            var block = Describe($"{YardPrefix}{index:00}", Vector2.zero, cw, cd, mask);
            block.Unit = unit;
            return block;
        }

        /// <summary>A residential block of a given size, measured from the origin like a
        /// park. <paramref name="artery"/> is the side its shops look at.</summary>
        public static Block Res(int index, int cw, int cd, int artery)
        {
            var mask = new bool[cw, cd];
            for (int i = 0; i < cw; i++)
                for (int j = 0; j < cd; j++) mask[i, j] = true;
            var block = Describe($"{ResPrefix}{index:00}", Vector2.zero, cw, cd, mask);
            block.Artery = artery;
            return block;
        }

        /// <summary>
        /// The ground at a row's land end, made up so that every row runs the same length.
        ///
        /// It used to be a car park up to <see cref="LotMax"/> and a PARK beyond that, and
        /// the park is what the user threw out (2026-08-27: "ne smeju tri linije parka
        /// uzastopno, max 1"). A row whose end is made up with a park, the row behind it the
        /// same, and the belt of parks beyond them both, is three lines of green in a row -
        /// which is what the drawing of seed 1987 came out as.
        ///
        /// So the end is dealt like a small quarter instead: residential blocks of whatever
        /// class the ROW'S DEPTH can carry, with the row's own streets between them. They go
        /// in AS UNITS OF THE ROW rather than as ground laid beside it afterwards, and that
        /// is the whole of why this works: the row's machinery then gives them their streets,
        /// counts those streets among the row's own (<see cref="Streets"/>) and keeps them
        /// clear of the facing row's (<see cref="Order"/>). Laid afterwards, their streets
        /// were nobody's business and the drawing came out with junctions run together in
        /// four deals out of five.
        ///
        /// What the classes cannot fill exactly - never more than <see cref="Skirt"/> cells,
        /// because that is as far as the road reader looks through a block's own parking -
        /// is a car park at the outer end, where it reads as the ground the downtown peters
        /// out into. A row too shallow for any class at all (five cells, twenty-five metres)
        /// still takes a park, as it always did; the run rule then judges whether it may.
        /// </summary>
        static void Fill(Plan plan, Row row, int cells, bool riverEast, List<int> facing,
                         int lo, int hi, int pitch)
        {
            if (cells <= 0) return;
            var pad = new List<Unit>();

            // THE PAD'S STREETS STAND ON THE FACING ROW'S. Dealt to their own best fit they
            // landed a cell or two from the street across the way, and the raster ran the two
            // junction boxes into one - six hundred such boxes in two hundred and forty deals,
            // against a handful before the pad existed. A street either meets the one opposite
            // it square or keeps a whole block clear of it; nothing between.
            // what the classes cannot fill exactly - never more than Skirt cells, which is as
            // far as the road reader looks through a block's own parking - is a car park at
            // the outer end, where the downtown peters out into the ground beyond it
            List<int> widths = null;
            for (int skirt = 0; skirt <= Skirt && widths == null; skirt++)
            {
                widths = Split(cells - skirt, row.Depth, facing, riverEast, lo, hi, pitch);
                if (widths != null) row.Skirt = skirt;
            }
            if (widths == null)
            {
                // nothing the recipe knows stands in a row this shallow: the park it always was
                if (cells <= StreetGap + PadNarrow) { row.Skirt = cells; return; }
                var park = Park(plan.Parks.Count + 1, cells - StreetGap, row.Depth);
                plan.Parks.Add(park);
                pad.Add(Piece(park));
            }
            else
                foreach (int w in widths)
                {
                    var block = Res(plan.Residential.Count + 1, w, row.Depth, riverEast ? 1 : 3);
                    plan.Residential.Add(block);
                    pad.Add(Piece(block));
                }

            // the pad stands at the land end: at the head of the row when the river is east
            // and the row is laid west to east, at its tail when the river is west
            if (riverEast)
                for (int k = pad.Count - 1; k >= 0; k--)
                {
                    row.Units.Insert(0, pad[k]);
                    row.Gaps.Insert(0, StreetGap);
                }
            else
                foreach (var unit in pad)
                {
                    row.Gaps.Add(StreetGap);
                    row.Units.Add(unit);
                }
        }

        /// <summary>How wide a residential block of a made-up end is when there is no facing
        /// street to stand on - the first row of each side has nothing across the way. Nine to
        /// twelve cells with its street: a block of 30 to 45 m, which is what the recipe's row
        /// and corner classes are.</summary>
        const int PitchMin = 9, PitchMax = 12;

        /// <summary>Would the recipe give a block of this size a class? Its own answer, asked
        /// of the ground INSIDE the pavement ring it carries.</summary>
        static bool Fits(int w, int depth) =>
            w >= PadNarrow && ResidentialLot.Classify(w - 2 * ResidentialLot.Walk,
                                                      depth - 2 * ResidentialLot.Walk) != null;

        /// <summary>
        /// How a made-up end of this many cells is cut up: the widths of its residential
        /// blocks, from the outer edge in, each with a street beyond it, or null where nothing
        /// the recipe knows stands at this depth.
        ///
        /// The cuts are offered the facing row's street lines first and taken where the piece
        /// they would leave has a class. What is left over goes to the innermost piece, whose
        /// street is where the pad meets the row - the one street of a pad that cannot be
        /// placed, the row's own length having fixed it.
        /// </summary>
        static List<int> Split(int cells, int depth, List<int> facing, bool riverEast,
                               int lo, int hi, int pitch)
        {
            // every width the recipe gives a class at this depth; nothing else may be dealt
            var widths = new List<int>();
            for (int w = PadNarrow; w <= PadWide; w++) if (Fits(w, depth)) widths.Add(w);
            if (widths.Count == 0) return null;

            // the facing streets as distances from the pad's outer edge
            var across = new List<int>();
            if (facing != null)
                foreach (int street in facing)
                {
                    int d = riverEast ? street - lo : hi - (street + StreetGap);
                    if (d > 0 && d < cells) across.Add(d);
                }

            // A CUT WANTS THE STREET ACROSS THE WAY. Standing square on it, the two make one
            // junction; a few cells off, the raster runs their boxes together and calls it a
            // fault. So the fill is worked out for the whole pad at once rather than piece by
            // piece - a cut on a facing street is worth having, one near it is worth avoiding,
            // and the widths have to come out exactly right either way.
            int Worth(int cut)
            {
                int best = 0;
                foreach (int other in across)
                {
                    if (other == cut) return 3;
                    if (Mathf.Abs(other - cut) < StreetGap + StreetGap) best = -4;
                }
                // failing that, a cut on the pad's own pitch keeps the blocks even
                return best != 0 ? best : (cut % pitch == 0 ? 1 : 0);
            }

            var score = new int[cells + 1];
            var laid = new int[cells + 1];
            for (int t = 1; t <= cells; t++) { score[t] = int.MinValue; laid[t] = 0; }
            for (int t = 1; t <= cells; t++)
                foreach (int w in widths)
                {
                    int back = t - w - StreetGap;
                    if (back < 0 || (back > 0 && score[back] == int.MinValue)) continue;
                    // the last cut of all is where the pad meets the row: the row's own length
                    // put it there and it is not this code's to choose
                    int worth = score[back] + (t == cells ? 0 : Worth(t - StreetGap));
                    if (worth <= score[t]) continue;
                    score[t] = worth;
                    laid[t] = w;
                }
            if (score[cells] == int.MinValue) return null;

            var pieces = new List<int>();
            for (int at = cells; at > 0; at -= laid[at] + StreetGap) pieces.Add(laid[at]);
            pieces.Reverse();
            return pieces;
        }

        /// <summary>A unit of one composed block, standing square.</summary>
        static Unit Piece(Block block)
        {
            var unit = new Unit { Pad = true };
            unit.Members.Add(block);
            unit.Offsets.Add(Vector2.zero);
            unit.Turn(0);
            return unit;
        }

        /// <summary>The widest and the narrowest residential block a made-up end is dealt,
        /// in cells, ITS PAVEMENT RING INCLUDED. The classes are read off
        /// <c>ResidentialLot.Sized</c>, which measures the ground INSIDE the ring: the
        /// narrowest thing it knows is a row block 4 cells across (6 with the ring) and the
        /// widest a court at 16 and up (18). Nothing here decides what a block is - the
        /// recipe does, and this only asks it.</summary>
        const int PadNarrow = 6, PadWide = 21;

        /// <summary>How much of a made-up end may be left to a car park, in cells. Four:
        /// the road reader looks four cells through a block's own parking for its street
        /// (<c>CoreRoads.Served</c>), so a wider skirt would leave the block beside it with
        /// no road along that side - which is a fault, and rightly.</summary>
        const int Skirt = 4;

        // --------------------------------------------------------------------- the belt

        /// <summary>
        /// How many sides of the core the belt of parks takes, and how deep.
        ///
        /// TWO, and the user's reason is the one that matters: the belt is the JOIN to the
        /// residential quarters that come next, so it has to lie where they will ("msm da
        /// treba da okruzimo centar s makar dve strane da bi bio laksi prelaz na
        /// residential", 2026-08-26). One of the two is the city park proper - a hundred
        /// metres deep, the one park in the city with room for everything - and the other is
        /// an ordinary strip of green.
        ///
        /// A full ring was considered and is not what an American downtown of the period
        /// looks like: a ring belongs to Vienna and Savannah. A big park on one side, a
        /// parkway along another, and car parks on the rest, is the 1987 picture.
        /// </summary>
        const int BeltDeepMin = 6 + ParkGrowth, BeltDeepMax = 8 + ParkGrowth;
        const int BeltBigMin = 15 + ParkGrowth, BeltBigMax = 22 + ParkGrowth;
        /// <summary>
        /// THE BELT IS ONE PARK TO A SIDE, unbroken.
        ///
        /// It was first dealt as three or four parks of 50-80 m with streets between them, on
        /// the reasoning that a two hundred metre slab of grass is a thing nobody can cross.
        /// The user looked at the drawing and asked for the opposite (2026-08-26: "moze li
        /// ovo da je jedan neprekidan park sa strane umesto 3 4 mala"), and on the evidence
        /// they are right: a run of small parks separated by streets reads as leftover ground
        /// between blocks, while one unbroken green edge reads as the thing the city stops
        /// at - which is what the belt is FOR. Crossing it is the walk's business, not the
        /// street's, and the walk cuts a park of this size up on its own (ParkWalk.Cut).
        /// </summary>
        const int BeltParkMin = 10;

        /// <summary>
        /// The belt: a row of parks beyond the last row of blocks, on two sides.
        ///
        /// IT IS A ROW LIKE ANY OTHER, and that is the whole design. A belt built as its own
        /// thing would need its own streets, its own declaration of the road behind it, its
        /// own place in the raster and its own verdict; dealt as a row, it gets every one of
        /// those from the machinery that already stands the core - a street between each pair
        /// of parks, the street behind it declared the full width (which is the "park drive"
        /// the residential rows will front on to), and the same road reader judging it.
        ///
        /// The rows are stood north and south of the boulevard turn and turn about, and these
        /// two are added last, so they land on opposite sides and outermost - which is the
        /// two sides the belt is meant to take.
        /// </summary>
        static void Belt(Plan plan, List<Row> rows, System.Random dice)
        {
            if (rows.Count == 0) return;

            int span = 0;
            foreach (var row in rows) span = Mathf.Max(span, row.Length);
            if (span < BeltParkMin) return;

            // the big side first or second, by the toss - it is the one that lands on
            // whichever side the alternation gives it
            bool bigFirst = dice.Next(2) == 0;
            for (int k = 0; k < 2; k++)
            {
                bool big = k == 0 == bigFirst;
                int depth = big ? dice.Next(BeltBigMin, BeltBigMax + 1)
                                : dice.Next(BeltDeepMin, BeltDeepMax + 1);
                var belt = new Row { Depth = depth };
                var park = Park(plan.Parks.Count + 1, span, depth);
                plan.Parks.Add(park);
                // the north, south and land belts are ONE belt turning two corners: the run
                // rule counts them as one park, or the drawing would be refused for the very
                // shape it was asked for
                plan.BeltParks.Add(park);
                var unit = new Unit();
                unit.Members.Add(park);
                unit.Offsets.Add(Vector2.zero);
                unit.Turn(0);
                belt.Units.Add(unit);
                rows.Add(belt);
            }
        }

        // -------------------------------------------------------------------- the river

        /// <summary>What a stretch of the promenade is called, and the far bank's kerb. Like a
        /// park, neither has a prefab: the deal cuts them to size and <c>QuayBlocks</c>
        /// composes them on the spot.</summary>
        public const string QuayPrefix = "quay-";
        public const string ApronPrefix = "apron-";
        public const string BankName = "bank";

        public static bool IsQuay(Block block) => block != null && block.Name.StartsWith(QuayPrefix);
        public static bool IsApron(Block block) => block != null && block.Name.StartsWith(ApronPrefix);
        public static bool IsBank(Block block) => block != null && block.Name == BankName;

        /// <summary>
        /// THE CITY ENDS ON A STRAIGHT LINE (the user, 2026-08-26: "ceo city treba da se zavrsi
        /// uz tu liniju"). The river lies along the east of the core, square to the boulevard -
        /// the main street runs down to the water and over it, which is what an American
        /// downtown of the period does (Jacksonville's Main Street, Detroit's Woodward). Every
        /// row is stood with its east end on the same line, so the edge streets off the rows'
        /// east ends fall into ONE street the whole height of the core, the quay street; the
        /// rows' ragged ends go west, where the residential quarters will take them up.
        ///
        /// East of the quay street: the promenade, a strip of one depth (a promenade twenty
        /// metres deep at one row and sixty at the next reads as ground left over, the same
        /// lesson the belt taught); the quay wall; the water; the road along the far bank that
        /// the bridges land on, so that the traffic over one bridge has somewhere to go and
        /// comes back over another; and a kerb beyond it. Nothing else stands on the far
        /// bank until a quarter is dealt there (Docs/river-plan.md).
        /// </summary>
        /// <summary>The promenade, in cells across: 60 or 65 m. The pavement takes two, the
        /// walk along the wall two, and the room between keeps at least 40 m for PalmCityDemo's
        /// complete 37.7 m-deep Fairground block. Synty's ordinary city meets its water in
        /// 16-20 m, but that strip carries nothing; this one carries the fair and cafes.</summary>
        const int QuayDeepMin = 12, QuayDeepMax = 13;
        /// <summary>The water, in cells: 70 m. A bascule's two leaves open a channel of 40 m
        /// and the fixed approaches make up the rest; wider than this and every bridge is
        /// mostly approach, narrower and the far bank is the near one's pavement.</summary>
        public const int RiverCells = 14;
        /// <summary>The far bank's apron, in cells, between the water and the far road: a
        /// bridge's fixed approach is 15 m, and a queue at the far gate of an open bridge
        /// stood in the far road's junction box (the harness: three cars scraping there in
        /// one run of five); with the apron the queue has 30 m before the box.</summary>
        public const int FarApron = 3;
        /// <summary>Bridges besides the boulevard's, and how far apart they keep.</summary>
        const int BridgesMin = 1, BridgesMax = 2;
        const int BridgeApart = 12;
        /// <summary>The shortest stretch of promenade left between a bridge and the end of
        /// the line, in cells: room for a room.</summary>
        const int QuayEndMin = 4;

        /// <summary>
        /// Where the river's lines lie, from the core out to the far bank, in the core's
        /// metres. The river lies along the EAST or the WEST of the core as the seed says
        /// (the user, 2026-08-26: "sve treba da se generise random gde ce bude reka"), so
        /// the lines are named by what they are between, not by the compass: each is
        /// <see cref="Dir"/> times its width beyond the last.
        /// </summary>
        public struct RiverLine
        {
            public bool East;                  // the river along the east of the core, else the west
            public float QuayLand, QuayWater;  // the quay street, a street wide: its kerb on the core, its kerb on the promenade
            public float Wall;                 // the promenade's far edge: the quay wall, the water beyond
            public float FarWater;             // the far bank's wall, and its apron beyond
            public float FarRoad, FarLand;     // the road along the far bank
            public float BankEnd;              // the kerb beyond it, and the end of the drawing
            public float Z0, Z1;               // how far the line runs
            public int Depth;                  // the promenade, in cells
            /// <summary>+1 toward the water for a river on the east, -1 for one on the west.</summary>
            public int Dir => East ? 1 : -1;
        }

        /// <summary>Stands a stretch of promenade's root where the plan cut it: composed
        /// at the origin with the kerb on its x = 0 and the wall on its far x, it is turned
        /// about to face a river on the west.</summary>
        public static void PlaceQuay(Plan plan, Block quay, Transform root)
        {
            var box = quay.Box;
            if (plan.River.East) root.SetPositionAndRotation(new Vector3(box.xMin, 0f, box.yMin), Quaternion.identity);
            else root.SetPositionAndRotation(new Vector3(box.xMax, 0f, box.yMax), Quaternion.Euler(0f, 180f, 0f));
        }

        /// <summary>A street that goes on over the water: which band of road, and whether
        /// it is the boulevard's. Every bridge opens (the user's word, 2026-08-26): the
        /// boulevard's with a leaf over each of its two carriageways.</summary>
        public struct Bridge
        {
            public Rect Band;
            public bool Boulevard;
        }

        /// <summary>A stretch of promenade, or the far bank's kerb: all cells filled, nothing
        /// standing on it, measured from the origin like a park.</summary>
        static Block Ground(string name, int cw, int cd)
        {
            var mask = new bool[cw, cd];
            for (int i = 0; i < cw; i++)
                for (int j = 0; j < cd; j++) mask[i, j] = true;
            return Describe(name, Vector2.zero, cw, cd, mask);
        }

        /// <summary>
        /// The river, laid along the east of the stood rows: the quay street off their ends
        /// declared the whole height of the core, the promenade cut into stretches between
        /// the bridges, the water, the far road and its kerb. Every street the rows declared
        /// now stops at the quay street's far kerb - a T, not a stub into the promenade - and
        /// the ones chosen as bridges go on over the water to the far road instead.
        /// </summary>
        static Rect Span(float a, float b, float z0, float z1) =>
            Rect.MinMaxRect(Mathf.Min(a, b), z0, Mathf.Max(a, b), z1);

        static void River(Plan plan, System.Random dice, bool east, int edge, float z0, float z1)
        {
            var line = new RiverLine { East = east, Depth = dice.Next(QuayDeepMin, QuayDeepMax + 1), Z0 = z0, Z1 = z1 };
            int dir = line.Dir;
            line.QuayLand = edge * Cell;
            line.QuayWater = line.QuayLand + dir * StreetGap * Cell;
            line.Wall = line.QuayWater + dir * line.Depth * Cell;
            line.FarWater = line.Wall + dir * RiverCells * Cell;
            line.FarRoad = line.FarWater + dir * FarApron * Cell;
            line.FarLand = line.FarRoad + dir * StreetGap * Cell;
            line.BankEnd = line.FarLand + dir * Cell;
            plan.River = line;
            plan.Water = Span(line.Wall, line.FarWater, z0, z1);
            // the drawing ends at the far kerb, and at the promenade's two ends: the line
            // goes on through the city from there, not through this core
            float beyond = east ? Any : -Any;
            plan.Outside.Add(Span(line.BankEnd, beyond, -Any, Any));
            plan.Outside.Add(Span(line.QuayWater, beyond, -Any, z0));
            plan.Outside.Add(Span(line.QuayWater, beyond, z1, Any));

            // the bridges: the boulevard's always, and one or two streets kept well apart
            // from it and from each other, and clear of the line's ends. All of them open
            plan.Bridges.Clear();
            plan.Bridges.Add(new Bridge
            {
                Band = Span(line.QuayWater, line.FarLand, plan.MainRoad.x, plan.MainRoad.y),
                Boulevard = true,
            });
            var candidates = new List<int>();
            for (int b = 1; b < plan.Bands.Count; b++)
            {
                var band = plan.Bands[b];
                if (band.yMin - z0 < QuayEndMin * Cell || z1 - band.yMax < QuayEndMin * Cell) continue;
                candidates.Add(b);
            }
            Dice.Shuffle(candidates, dice);
            int wanted = dice.Next(BridgesMin, BridgesMax + 1);
            var crossing = new HashSet<int>();
            foreach (int b in candidates)
            {
                if (crossing.Count >= wanted) break;
                var band = plan.Bands[b];
                bool clear = true;
                foreach (var other in plan.Bridges)
                    if (band.yMax + BridgeApart * Cell > other.Band.yMin && band.yMin - BridgeApart * Cell < other.Band.yMax)
                    { clear = false; break; }
                if (!clear) continue;
                crossing.Add(b);
                plan.Bridges.Add(new Bridge { Band = Span(line.QuayWater, line.FarLand, band.yMin, band.yMax) });
            }
            // every street the rows declared runs a street's width past the edge street on
            // the land side, a dead end the drivers know, and on the river side stops at the
            // quay street's far kerb - or goes on over the water, if it is a bridge
            for (int b = 1; b < plan.Bands.Count; b++)
            {
                var band = plan.Bands[b];
                float land = east ? band.xMin - 2f * StreetGap * Cell : band.xMax + 2f * StreetGap * Cell;
                float water = crossing.Contains(b) ? line.FarLand : line.QuayWater;
                plan.Bands[b] = Span(land, water, band.yMin, band.yMax);
            }
            plan.Bands.Add(Span(line.QuayLand, line.QuayWater, z0 - 2f * StreetGap * Cell, z1 + 2f * StreetGap * Cell));
            plan.Bands.Add(Span(line.FarRoad, line.FarLand, z0, z1));

            // the promenade and the far bank's apron, a stretch of each between every pair
            // of bridges; and the far kerb
            plan.Bridges.Sort((one, other) => one.Band.yMin.CompareTo(other.Band.yMin));
            plan.Quays.Clear();
            plan.Aprons.Clear();
            float from = z0;
            for (int b = 0; b <= plan.Bridges.Count; b++)
            {
                float to = b < plan.Bridges.Count ? plan.Bridges[b].Band.yMin : z1;
                int cells = Mathf.RoundToInt((to - from) / Cell);
                if (cells > 0)
                {
                    var quay = Ground($"{QuayPrefix}{plan.Quays.Count + 1:00}", line.Depth, cells);
                    quay.Pivot = new Vector2(Mathf.Min(line.QuayWater, line.Wall), from);
                    plan.Quays.Add(quay);
                    var apron = Ground($"{ApronPrefix}{plan.Aprons.Count + 1:00}", FarApron, cells);
                    apron.Pivot = new Vector2(Mathf.Min(line.FarWater, line.FarRoad), from);
                    plan.Aprons.Add(apron);
                }
                if (b < plan.Bridges.Count) from = plan.Bridges[b].Band.yMax;
            }
            plan.Bank = Ground(BankName, 1, Mathf.RoundToInt((z1 - z0) / Cell));
            plan.Bank.Pivot = new Vector2(Mathf.Min(line.FarLand, line.BankEnd), z0);

            var said = new System.Text.StringBuilder();
            said.Append($"river on the {(east ? "east" : "west")}: quay street x {line.QuayLand:F0}..{line.QuayWater:F0}, " +
                        $"promenade {line.Depth * Cell:F0} m to the wall at x {line.Wall:F0}, water to x {line.FarWater:F0}, " +
                        $"apron to x {line.FarRoad:F0}, far road to x {line.FarLand:F0}; {plan.Quays.Count} stretches of promenade; bridges:");
            foreach (var bridge in plan.Bridges)
                said.Append($" z {bridge.Band.yMin:F0}..{bridge.Band.yMax:F0}{(bridge.Boulevard ? " (boulevard)" : "")}");
            plan.Rows.Add(said.ToString());
        }

        /// <summary>Stands the instance where the plan puts it, turned the way it says.</summary>
        public static void Place(Block block)
        {
            if (block.Go == null) return;
            block.Go.transform.SetPositionAndRotation(block.Position, block.Rotation);
        }

        /// <summary>
        /// The block's shape on its own 5 m grid: a cell is the block's when something
        /// stands on it, or when it is shut in by what stands - a courtyard is the block's.
        /// A cell open to the block's edge across bare ground is a bay the demo's kerb ran
        /// round - the demo's car parks, and the lanes and smaller blocks it tucked into
        /// the big blocks' corners.
        /// </summary>
        static void Shape(Block block, List<Bounds> cover)
        {
            int w = block.CW0, d = block.CD0;
            float x0 = block.Ground0.min.x, z0 = block.Ground0.min.z;
            var covered = new bool[w, d];
            foreach (var box in cover)
                for (int i = 0; i < w; i++)
                    for (int j = 0; j < d; j++)
                    {
                        float cx = x0 + (i + 0.5f) * Cell, cz = z0 + (j + 0.5f) * Cell;
                        if (cx > box.min.x && cx < box.max.x && cz > box.min.z && cz < box.max.z)
                            covered[i, j] = true;
                    }

            var open = new bool[w, d];
            var todo = new Queue<Vector2Int>();
            for (int i = 0; i < w; i++)
                for (int j = 0; j < d; j++)
                    if (!covered[i, j] && (i == 0 || j == 0 || i == w - 1 || j == d - 1))
                    {
                        open[i, j] = true;
                        todo.Enqueue(new Vector2Int(i, j));
                    }
            while (todo.Count > 0)
            {
                var c = todo.Dequeue();
                foreach (var n in new[] { new Vector2Int(c.x - 1, c.y), new Vector2Int(c.x + 1, c.y),
                                          new Vector2Int(c.x, c.y - 1), new Vector2Int(c.x, c.y + 1) })
                {
                    if (n.x < 0 || n.y < 0 || n.x >= w || n.y >= d) continue;
                    if (covered[n.x, n.y] || open[n.x, n.y]) continue;
                    open[n.x, n.y] = true;
                    todo.Enqueue(n);
                }
            }

            block.Mask0 = new bool[w, d];
            for (int i = 0; i < w; i++)
                for (int j = 0; j < d; j++)
                    if (!open[i, j]) block.Mask0[i, j] = true;
        }

        static bool BoxOf(GameObject go, out Bounds box)
        {
            box = new Bounds();
            bool any = false;
            foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                if (!any) { box = renderer.bounds; any = true; }
                else box.Encapsulate(renderer.bounds);
            }
            return any;
        }

        static bool Starts(string name, string prefix) =>
            name.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase);

        // ------------------------------------------------------------------ the plans

        /// <summary>Where everything stands: the blocks' stands are written onto the blocks
        /// themselves, and the plan keeps what the raster needs besides - the alleys and
        /// the way each runs, and the main road.</summary>
        public sealed class Plan
        {
            public string Name;
            public int Seed;               // the seed asked for; -1 is the Synty arrangement
            public int Attempt;            // which deal of that seed this is
            public Vector2 MainRoad;
            /// <summary>Roads declared by the plan rather than read off the blocks, as
            /// boxes (x, z): the main road the whole width of the core, and in a dealt plan
            /// the street between every two rows and the one behind the last, each as long
            /// as the rows beside it and a short dead end beyond their edge streets. A
            /// street that stopped flush with the edge street would end in a corner box
            /// with two legs, and that is a box the traffic locks up in (the play harness
            /// found it); run 15 m past it, the meeting is a T and the end a dead end the
            /// drivers know. Run to the edge of the drawing instead, it is a road into
            /// nothing.</summary>
            public readonly List<Rect> Bands = new List<Rect>();
            public readonly List<Lane> Lanes = new List<Lane>();
            /// <summary>The rows the blocks were dealt into, for the log.</summary>
            public readonly List<string> Rows = new List<string>();

            /// <summary>
            /// The park blocks this deal made, in the order it made them.
            ///
            /// They are in the block list too - the deal treats them like any other unit -
            /// but whoever stands the core needs to tell them apart, because a park has no
            /// prefab to instantiate: it is composed on the spot from its final size.
            /// </summary>
            public readonly List<Block> Parks = new List<Block>();

            /// <summary>
            /// The residential blocks this deal made: the made-up ground at the rows' land
            /// ends (<see cref="Pad"/>), and later the quarters outside the belt. Composed
            /// on the spot like a park - <c>ResidentialLot</c> divides the rectangle and
            /// <c>ResidentialBlocks</c> stands it - so they are kept apart from the
            /// harvested blocks the caller handed in.
            /// </summary>
            public readonly List<Block> Residential = new List<Block>();

            /// <summary>The stretches of the belt on the land side. They are parks like any
            /// other and are in <see cref="Parks"/> too, but the run rule does not count them
            /// against each other: they are ONE park, cut by the gates the streets need
            /// (<see cref="Ring"/>).</summary>
            public readonly HashSet<Block> BeltParks = new HashSet<Block>();

            /// <summary>Where a street of the core crosses the belt on the land side.</summary>
            public readonly List<Rect> Gates = new List<Rect>();

            /// <summary>Where the core's edges lie once the belt is on, and how big a quarter
            /// is. Empty in a plan with no ring (the Synty reference).</summary>
            public RingLine Ring;

            /// <summary>
            /// The longest run of parks facing each other across a street.
            ///
            /// ONE is the only answer a deal may give (the user, 2026-08-27: "ne smeju tri
            /// linije parka uzastopno, max 1"). Two parks across a street from one another
            /// read as one park with a road through it, three as a green quarter - and the
            /// core is not one. The belt counts as a park like any other; the stretches of
            /// the belt itself do not count against each other, being one park.
            /// </summary>
            public int ParkRuns;

            /// <summary>The river along the east of the core (<see cref="River"/>): the
            /// promenade in stretches between the bridges, the far bank's kerb, the water,
            /// the bridges, and where the drawing ends. Composed on the spot like the parks;
            /// a plan with no river (the Synty reference, the industrial quarter) has an
            /// empty water box and no stretches.</summary>
            public readonly List<Block> Quays = new List<Block>();
            /// <summary>The far bank's apron, a stretch between each pair of bridges.</summary>
            public readonly List<Block> Aprons = new List<Block>();
            public Block Bank;
            public Rect Water;
            public RiverLine River;
            public readonly List<Bridge> Bridges = new List<Bridge>();
            /// <summary>Ground beyond the edge of the drawing: no road is read there and no
            /// block wants one along it. The river line goes on through the city from here.</summary>
            public readonly List<Rect> Outside = new List<Rect>();
            /// <summary>Car parks that belong to no block: the ground at a short row's west
            /// end, filled so that every row runs the same length (<see cref="Roll"/>).</summary>
            public readonly List<Rect> Lots = new List<Rect>();
        }

        /// <summary>The seed that asks for the demo's own arrangement.</summary>
        public const int SyntySeed = -1;

        /// <summary>The demo's arrangement, its streets opened by the cuts.</summary>
        public static Plan Synty(List<Block> blocks)
        {
            var plan = new Plan { Name = "Synty", Seed = SyntySeed, MainRoad = MainRoad };
            plan.Bands.Add(Rect.MinMaxRect(-Any, plan.MainRoad.x, Any, plan.MainRoad.y));
            foreach (var block in blocks)
            {
                block.Turn(0);
                block.Pivot = Vector2.zero;
                block.Lot = Rect.zero;
                foreach (var stand in Blocks)
                    if (stand.Prefab == block.Name) block.Pivot = new Vector2(stand.X, stand.Z);
                block.Shift = Shift(block.DemoBox);
            }
            foreach (var lane in Lanes) plan.Lanes.Add(lane.Moved(Shift(lane.Box)));
            plan.Rows.Add("as Synty stood them");
            return plan;
        }

        // ------------------------------------------------------------------ the deal

        /// <summary>
        /// Two blocks the demo nested - the small one in the big one's bay, an alley between
        /// - stay nested: the bay is the small block's size for a reason. The offsets are
        /// the demo's (small pivot less big pivot) and the lanes are the alleys between
        /// the two, in the big block's own frame.
        /// </summary>
        sealed class Nest
        {
            public string Big, Small;
            public Vector2 Offset;
            public Lane[] Lanes;
        }

        static readonly Nest[] Nests =
        {
            new Nest
            {
                Big = "block-06", Small = "block-07", Offset = new Vector2(15f, 10f),
                Lanes = new[] { new Lane(true, -10f, -10f, 30f, +1), new Lane(false, -10f, -10f, 30f, -1) },
            },
            new Nest
            {
                Big = "block-12", Small = "block-10", Offset = new Vector2(-15f, -35f),
                Lanes = new[] { new Lane(true, 10f, -50f, -15f, +1), new Lane(false, -15f, -45f, 10f, -1) },
            },
        };

        /// <summary>What the packer deals: one block, or a nested pair, as one piece.</summary>
        sealed class Unit
        {
            public readonly List<Block> Members = new List<Block>();
            public readonly List<Vector2> Offsets = new List<Vector2>();   // each member's pivot less the anchor's
            public Lane[] Lanes = new Lane[0];
            public int Yaw;
            /// <summary>Dealt to make a row's land end up (<see cref="Fill"/>) rather than
            /// out of the pool. It stays at that end: reordering a row must not send a
            /// residential block into the middle of a downtown row.</summary>
            public bool Pad;
            public Rect Box;               // anchor-relative, metres, at the current yaw
            public int W => Mathf.RoundToInt(Box.width / Cell);
            public int D => Mathf.RoundToInt(Box.height / Cell);

            /// <summary>The unit's box at a yaw, without turning it.</summary>
            public Rect Footprint(int yaw)
            {
                bool any = false;
                var box = new Rect();
                for (int m = 0; m < Members.Count; m++)
                {
                    var foot = Members[m].Footprint(yaw);
                    var off = CoreLayout.Turn(Offsets[m], yaw);
                    var one = new Rect(foot.x + off.x, foot.y + off.y, foot.width, foot.height);
                    if (!any) { box = one; any = true; }
                    else box = Rect.MinMaxRect(Mathf.Min(box.xMin, one.xMin), Mathf.Min(box.yMin, one.yMin),
                                               Mathf.Max(box.xMax, one.xMax), Mathf.Max(box.yMax, one.yMax));
                }
                return box;
            }

            public void Turn(int yaw)
            {
                Yaw = yaw;
                foreach (var member in Members) member.Turn(yaw);
                Box = Footprint(yaw);
            }

            /// <summary>Is the unit's whole side along this edge a wall? Only the anchor
            /// block can make it one: a nested pair's bay is on the small block's side.</summary>
            public bool Straight(bool west)
            {
                if (Members.Count > 1) return false;
                return Members[0].Straight(west);
            }
        }

        sealed class Row
        {
            public readonly List<Unit> Units = new List<Unit>();
            public readonly List<int> Gaps = new List<int>();   // cells after each unit but the last: 1 alley, 3 street
            public int Depth;
            /// <summary>Cells of car park at the row's land end, beyond its units: what the
            /// classes could not fill exactly (<see cref="Fill"/>).</summary>
            public int Skirt;
            public int Length
            {
                get
                {
                    int n = 0;
                    foreach (var unit in Units) n += unit.W;
                    foreach (var gap in Gaps) n += gap;
                    return n;
                }
            }
        }

        /// <summary>A street between two neighbours in a row, in cells; and an alley.</summary>
        const int StreetGap = 3, AlleyGap = 1;
        /// <summary>How much shallower than its row a block may stand. The ground behind
        /// it is its car park; more than this and the car park is bigger than the block.</summary>
        const int MaxShallow = 4;
        /// <summary>The widest car park a row's west end is made up with, in cells; a
        /// bigger shortfall is a park.</summary>
        const int LotMax = 12;
        /// <summary>A row is dealt until it is this long, in cells.</summary>
        const int RowMin = 40, RowMax = 60;
        /// <summary>How often two neighbours that could share an alley do.</summary>
        const double AlleyOdds = 0.4;

        /// <summary>
        /// Deals the blocks into rows from a seed, and stands them. Nested pairs stay
        /// nested. Every unit is turned a random quarter; a row takes the depth of its first
        /// unit and then whatever fits it - the deepest turn of a unit no deeper than the row
        /// and no more than <see cref="MaxShallow"/> cells shallower - until it is long
        /// enough. Rows go north and south of the boulevard turn and turn about, each one a
        /// street behind the last, every one with its east end on the river
        /// (<see cref="River"/>). A block shallower than its row keeps the ground behind it.
        /// </summary>
        public static Plan Roll(List<Block> blocks, int seed)
        {
            var dice = new System.Random(seed);
            var plan = new Plan { Name = $"seed {seed}", Seed = seed, MainRoad = MainRoad };
            plan.Bands.Add(Rect.MinMaxRect(-Any, plan.MainRoad.x, Any, plan.MainRoad.y));

            // the units: the nested pairs, then everything else on its own
            var units = new List<Unit>();
            var taken = new HashSet<Block>();
            Block Named(string name)
            {
                foreach (var block in blocks) if (block.Name == name) return block;
                return null;
            }
            foreach (var nest in Nests)
            {
                var big = Named(nest.Big);
                var small = Named(nest.Small);
                if (big == null || small == null) continue;
                var unit = new Unit { Lanes = nest.Lanes };
                unit.Members.Add(big); unit.Offsets.Add(Vector2.zero);
                unit.Members.Add(small); unit.Offsets.Add(nest.Offset);
                units.Add(unit);
                taken.Add(big); taken.Add(small);
            }
            foreach (var block in blocks)
            {
                if (taken.Contains(block)) continue;
                var unit = new Unit();
                unit.Members.Add(block); unit.Offsets.Add(Vector2.zero);
                units.Add(unit);
            }

            // the parks, dealt like any other unit and composed later. They are made here
            // rather than handed in because nothing on disk describes them: a park is a size
            // and a seed, and the recipe fills whatever rectangle it ends up with.
            plan.Parks.Clear();
            int wanted = dice.Next(ParksMin, ParksMax + 1);
            for (int k = 0; k < wanted; k++)
            {
                bool pocket = dice.NextDouble() < PocketOdds;
                int w = pocket ? dice.Next(PocketMin, PocketMax + 1) : dice.Next(SquareMin, SquareMax + 1);
                int d = pocket ? dice.Next(PocketMin, PocketMax + 1) : dice.Next(SquareMin, SquareMax + 1);
                var park = Park(k + 1, w, d);
                plan.Parks.Add(park);
                var unit = new Unit();
                unit.Members.Add(park); unit.Offsets.Add(Vector2.zero);
                units.Add(unit);
            }
            Dice.Shuffle(units, dice);

            // the rows
            var rows = new List<Row>();
            var pool = new List<Unit>(units);
            while (pool.Count > 0)
            {
                var row = new Row();
                var first = pool[0];
                pool.RemoveAt(0);
                first.Turn(dice.Next(4) * 90);
                row.Depth = first.D;
                row.Units.Add(first);
                int target = dice.Next(RowMin, RowMax + 1);
                for (int i = 0; i < pool.Count && row.Length < target;)
                {
                    var unit = pool[i];
                    int best = -1, bestDepth = -1;
                    // A PARK FITS ANY ROW, because it is composed to the depth it is given
                    // rather than measured off a prefab. Held to the same test as a block -
                    // no deeper than the row, no more than MaxShallow shallower - a park of
                    // six cells was refused by every row deeper than ten, went off to start
                    // rows of its own, and the core needed four times as many deals to come
                    // out clean.
                    foreach (int yaw in Quarters(dice))
                    {
                        var foot = unit.Footprint(yaw);
                        int depth = Mathf.RoundToInt(foot.height / Cell);
                        if (depth > row.Depth || row.Depth - depth > MaxShallow) continue;
                        if (depth > bestDepth) { bestDepth = depth; best = yaw; }
                    }
                    if (best < 0) { i++; continue; }
                    // two parks side by side read as one big park with a street through it,
                    // which is not what either of them is
                    var neighbour = row.Units[row.Units.Count - 1];
                    if (IsPark(unit.Members[0]) && IsPark(neighbour.Members[0])) { i++; continue; }
                    unit.Turn(best);
                    pool.RemoveAt(i);
                    // a street between neighbours, or an alley where both are as deep as
                    // the row and walled the length of it - and never against a park, whose
                    // gates want a kerb and a crossing rather than a one-way slot between
                    // two walls
                    var last = row.Units[row.Units.Count - 1];
                    bool alley = last.D == row.Depth && unit.D == row.Depth &&
                                 last.Straight(false) && unit.Straight(true) &&
                                 !IsPark(last.Members[0]) && !IsPark(unit.Members[0]) &&
                                 dice.NextDouble() < AlleyOdds;
                    row.Gaps.Add(alley ? AlleyGap : StreetGap);
                    row.Units.Add(unit);
                }
                rows.Add(row);
            }
            // a row of one unit is a tail hanging off the core; where the unit can stand
            // in another row - no deeper than it, and not much shallower - it goes there
            for (int r = rows.Count - 1; r >= 0; r--)
            {
                if (rows[r].Units.Count > 1) continue;
                var lone = rows[r].Units[0];
                Row home = null;
                int best = -1, bestDepth = -1;
                foreach (var other in rows)
                {
                    if (other == rows[r] || other.Units.Count < 2) continue;
                    foreach (int yaw in Quarters(dice))
                    {
                        int depth = Mathf.RoundToInt(lone.Footprint(yaw).height / Cell);
                        if (depth > other.Depth || other.Depth - depth > MaxShallow + 2) continue;
                        if (depth > bestDepth) { bestDepth = depth; best = yaw; home = other; }
                    }
                }
                if (home == null) continue;
                lone.Turn(best);
                home.Gaps.Add(StreetGap);
                home.Units.Add(lone);
                rows.RemoveAt(r);
            }

            // A PARK NEVER FACES THE BELT. The belt is the last row stood on each side, so
            // the two rows dealt before it are the ones whose parks would look at it across
            // the street behind them - the belt, a park, and (with the row behind that one)
            // a third line of green, which is the one thing the drawing may not do (the
            // user, 2026-08-27). The rows are in no particular order, so a row with a park
            // in it simply changes places with one without.
            for (int r = rows.Count - 1; r >= rows.Count - 2 && r >= 0; r--)
            {
                if (!Green(rows[r])) continue;
                for (int other = 0; other < rows.Count - 2; other++)
                {
                    if (Green(rows[other])) continue;
                    (rows[r], rows[other]) = (rows[other], rows[r]);
                    break;
                }
            }

            Belt(plan, rows, dice);

            // A PARK GROWS TO ITS ROW. Every other unit shallower than its row keeps the
            // ground behind it as a car park; a park cannot - a car park behind a park is
            // nonsense, and it would put tarmac where the far fence should be. Since a park
            // is composed rather than harvested, it simply re-cuts itself to the depth it
            // was dealt, and the recipe fills the larger rectangle.
            foreach (var row in rows)
                foreach (var unit in row.Units)
                {
                    var block = unit.Members[0];
                    if (!IsPark(block) || unit.D >= row.Depth) continue;
                    bool sideways = unit.Yaw == 90 || unit.Yaw == 270;
                    // re-cut in the block's OWN frame, which is the turned frame's other axis
                    // when the unit stands at a quarter turn
                    if (sideways) Reshape(block, row.Depth, block.CD0);
                    else Reshape(block, block.CW0, row.Depth);
                    unit.Turn(unit.Yaw);
                }

            // the rows out from the boulevard, north and south turn and turn about, and
            // EVERY ROW'S EAST END ON THE RIVER: the longest row centred, the rest stood
            // flush with it. The rows used to be jittered about the centre so that their
            // cross streets would either line up with the next row's or stand well clear -
            // two streets meeting the same road a few metres apart run their junctions
            // together into one wide box, and the play harness locks three cars in such a
            // box (seed 1987, cars 25, 27 and 40 at x -145..-120 z -55..-35) - but the river
            // takes that freedom: the line the city stops at has to be straight. So the
            // row's UNITS are reordered instead (Order): the row keeps its length and its
            // end, and its streets fall where they clash least
            int span = 0;
            foreach (var row in rows) span = Mathf.Max(span, row.Length);
            // the core's two long edges, in cells, the longest row centred; the river is
            // along one of them as the seed says, and every row's end on that edge is flush
            int lo = -(span / 2), hi = lo + span;
            bool riverEast = dice.Next(2) == 0;
            // EVERY ROW RUNS THE SAME LENGTH, and the short ones are made up at the land end
            // with residential blocks (Fill). Rows of different lengths put their land-side
            // edge streets a cell or two apart, and where two such streets meet the band
            // between the rows the raster runs their junctions into one wide box - the box
            // the play harness locks three cars in (seed 1987, x -145..-120 z -55..-35)
            int pitch = dice.Next(PitchMin, PitchMax + 1);
            float southKerb = plan.MainRoad.x, northKerb = plan.MainRoad.y;
            float northNext = northKerb, southNext = southKerb;
            bool north = dice.Next(2) == 0;
            var northStreets = new List<int>();   // cross streets of the last row stood on each side, in cells
            var southStreets = new List<int>();
            plan.Lots.Clear();
            foreach (var row in rows)
            {
                var facing = north ? (northStreets.Count > 0 ? northStreets : southStreets)
                                   : (southStreets.Count > 0 ? southStreets : northStreets);
                // EVERY ROW RUNS THE SAME LENGTH, the short ones made up at the land end with
                // residential blocks: rows of different lengths put their land-side edge
                // streets a cell or two apart, and two such streets meeting the band between
                // the rows run their junctions into one wide box - the box the play harness
                // locks three cars in (seed 1987, x -145..-120 z -55..-35)
                Fill(plan, row, span - row.Length, riverEast, facing, lo, hi, pitch);
                Order(row, length => riverEast ? hi - length : lo, facing, dice, riverEast);
                int length = row.Length;
                int x0Cell = riverEast ? hi - length : lo;
                var streets = Streets(row, x0Cell);
                if (north) northStreets = streets; else southStreets = streets;
                float x = x0Cell * Cell;
                float z0, z1;
                // AND EVERY ROW RUNS THE SAME LENGTH, its west end made up with a car park.
                // Rows of different lengths put their west edge streets a cell or two
                // apart, and where two such streets meet the band between the rows the
                // raster runs their junctions together into one wide box - which is the
                // box the play harness locks three cars in, run after run (seed 1987, x
                // -145..-120 z -55..-35). With the ends made up, the west edge streets
                // fall into one street the whole height of the core, like the quay street
                // on the east, and the box is gone. A surface car park at the edge of the
                // downtown is the 1987 picture besides; a park takes the ground instead
                // where the shortfall is more than a car park's width
                // and what the classes could not fill exactly is a car park at the OUTER
                // end, where a block beside it still finds its street within four cells and
                // where it reads as the ground the downtown peters out into
                if (row.Skirt > 0)
                {
                    float skirtLo = riverEast ? lo * Cell : (lo + length) * Cell;
                    float zLo = north ? northNext : southNext - row.Depth * Cell;
                    plan.Lots.Add(Rect.MinMaxRect(skirtLo, zLo, skirtLo + row.Skirt * Cell,
                                                  zLo + row.Depth * Cell));
                }
                // the street behind the row: as long as the row, and as long as the row
                // that will stand behind it, which is not known yet - so it is written for
                // this row now and widened when the next row on this side is stood
                float xEnd = x + length * Cell;
                if (north)
                {
                    z0 = northNext; z1 = z0 + row.Depth * Cell;
                    northNext = z1 + StreetGap * Cell;
                    Widen(plan, Rect.MinMaxRect(x, z0 - StreetGap * Cell, xEnd, z0));
                    plan.Bands.Add(Rect.MinMaxRect(x, z1, xEnd, northNext));
                }
                else
                {
                    z1 = southNext; z0 = z1 - row.Depth * Cell;
                    southNext = z0 - StreetGap * Cell;
                    Widen(plan, Rect.MinMaxRect(x, z1, xEnd, z1 + StreetGap * Cell));
                    plan.Bands.Add(Rect.MinMaxRect(x, southNext, xEnd, z0));
                }
                var line = new System.Text.StringBuilder();
                line.Append(north ? "north" : "south").Append($" row, {row.Depth} deep, z {z0:F0}..{z1:F0}:");
                for (int u = 0; u < row.Units.Count; u++)
                {
                    var unit = row.Units[u];
                    // the front faces the boulevard; the ground behind a shallow unit is its lot
                    float zFront = north ? z0 : z1 - unit.D * Cell;
                    var anchor = new Vector2(x - unit.Box.xMin, zFront - unit.Box.yMin);
                    for (int m = 0; m < unit.Members.Count; m++)
                    {
                        var block = unit.Members[m];
                        block.Pivot = anchor + Turn(unit.Offsets[m], unit.Yaw);
                        block.Shift = Vector2.zero;
                        block.Lot = Rect.zero;
                    }
                    if (unit.D < row.Depth)
                    {
                        float lotZ0 = north ? zFront + unit.D * Cell : z0;
                        unit.Members[0].Lot = new Rect(x, lotZ0, unit.W * Cell, (row.Depth - unit.D) * Cell);
                    }
                    foreach (var lane in unit.Lanes) plan.Lanes.Add(lane.Turned(unit.Yaw).Moved(anchor));
                    line.Append(' ').Append(unit.Members[0].Name).Append(unit.Members.Count > 1 ? "+" + unit.Members[1].Name : "")
                        .Append($"@{unit.Yaw}");
                    x += unit.W * Cell;
                    if (u >= row.Gaps.Count) continue;
                    int gap = row.Gaps[u];
                    if (gap == AlleyGap)
                    {
                        plan.Lanes.Add(new Lane(true, x, z0, z1, dice.Next(2) == 0 ? 1 : -1));
                        line.Append(" |alley|");
                    }
                    else line.Append(" |street|");
                    x += gap * Cell;
                }
                plan.Rows.Add(line.ToString());
                north = !north;
            }
            // on the land side, every declared street runs past the edge streets of the
            // rows beside it: the edge street itself, and a dead end a street's width
            // beyond. On the river side the river takes them all: they end on the quay
            // street, or go on over the water
            River(plan, dice, riverEast, riverEast ? hi : lo, southNext, northNext);
            // and beyond the core: the belt on the land side, and the five quarters of
            // residential blocks that surround the lot (Docs/residential-quarter-plan.md)
            Ring(plan, dice, riverEast, lo, hi, southNext, northNext);
            plan.ParkRuns = Runs(plan);
            return plan;
        }

        /// <summary>Is there a park in this row?</summary>
        static bool Green(Row row)
        {
            foreach (var unit in row.Units)
                if (IsPark(unit.Members[0])) return true;
            return false;
        }

        /// <summary>
        /// The longest run of parks facing one another across a street - what
        /// <see cref="Plan.ParkRuns"/> reports and the verdict refuses above one.
        ///
        /// Measured on the ground the parks actually stand on, not inferred from how they
        /// were dealt: a rule that is judged by the same reasoning that laid it out cannot
        /// catch the case the reasoning missed (Docs/park-plan.md, the lesson of the park
        /// generator).
        /// </summary>
        static int Runs(Plan plan)
        {
            var boxes = new List<Rect>();
            var belt = new List<bool>();
            foreach (var park in plan.Parks) { boxes.Add(park.Box); belt.Add(plan.BeltParks.Contains(park)); }
            if (boxes.Count == 0) return 0;
            var seen = new bool[boxes.Count];
            int most = 1;
            for (int i = 0; i < boxes.Count; i++)
            {
                if (seen[i]) continue;
                seen[i] = true;
                int size = 0;
                var todo = new Queue<int>();
                todo.Enqueue(i);
                while (todo.Count > 0)
                {
                    int a = todo.Dequeue();
                    size++;
                    for (int b = 0; b < boxes.Count; b++)
                    {
                        if (seen[b] || !Faces(boxes[a], boxes[b])) continue;
                        // the belt's own stretches are one park either side of a gate
                        if (belt[a] && belt[b]) continue;
                        seen[b] = true;
                        todo.Enqueue(b);
                    }
                }
                most = Mathf.Max(most, size);
            }
            return most;
        }

        /// <summary>
        /// Do these two pieces of ground look at each other across a street?
        ///
        /// A street, and nothing else: ground that TOUCHES is one park in two pieces (the
        /// belt turns a corner that way), and ground a boulevard apart is not a run either -
        /// a park on each side of a 35 m boulevard is a parkway, which is a thing the period
        /// has. So the gap has to be a street's width or less, and the two have to face each
        /// other along at least fifteen metres, or it is a corner rather than a frontage.
        /// </summary>
        static bool Faces(Rect one, Rect other)
        {
            const float Touching = 2.5f;
            float most = (StreetGap + 1) * Cell;
            float gapX = Mathf.Max(one.xMin - other.xMax, other.xMin - one.xMax);
            float gapZ = Mathf.Max(one.yMin - other.yMax, other.yMin - one.yMax);
            float overX = Mathf.Min(one.xMax, other.xMax) - Mathf.Max(one.xMin, other.xMin);
            float overZ = Mathf.Min(one.yMax, other.yMax) - Mathf.Max(one.yMin, other.yMin);
            if (gapX > Touching && gapX < most && overZ >= 3f * Cell) return true;
            if (gapZ > Touching && gapZ < most && overX >= 3f * Cell) return true;
            return false;
        }

        /// <summary>Widens the declared street on this z band, if there is one, to cover
        /// <paramref name="span"/> as well.</summary>
        static void Widen(Plan plan, Rect span)
        {
            for (int b = 1; b < plan.Bands.Count; b++)
            {
                var band = plan.Bands[b];
                if (Mathf.Abs(band.yMin - span.yMin) > 0.01f) continue;
                plan.Bands[b] = Rect.MinMaxRect(Mathf.Min(band.xMin, span.xMin), band.yMin,
                                                Mathf.Max(band.xMax, span.xMax), band.yMax);
                return;
            }
        }

        /// <summary>The row's cross streets, stood with its first unit at <paramref name="x0"/>
        /// cells: the edge street off either end and every street between two units, each
        /// by its first cell.</summary>
        static List<int> Streets(Row row, int x0)
        {
            var streets = new List<int> { x0 - StreetGap };
            int x = x0;
            for (int u = 0; u < row.Units.Count; u++)
            {
                x += row.Units[u].W;
                if (u < row.Gaps.Count)
                {
                    if (row.Gaps[u] == StreetGap) streets.Add(x);
                    x += row.Gaps[u];
                }
            }
            streets.Add(x);
            return streets;
        }

        /// <summary>How many orders of a row's units are tried for the one whose streets
        /// clash least with the row it faces.</summary>
        const int Orders = 120;

        /// <summary>
        /// Reorders the row's units so that its cross streets either line up with the
        /// facing row's or stand a street's width clear of them - the jitter's job, done
        /// without moving the row. <paramref name="x0Of"/> says where a row of a length
        /// starts: flush with the river's edge, whichever side that is. The gaps are dealt again for each order, by the same
        /// rule (an alley only between two walled neighbours as deep as the row, never
        /// against a park), from dice of the row's own so every order is judged on the
        /// same throws. The first order tried is the one the row was dealt in.
        /// </summary>
        static void Order(Row row, System.Func<int, int> x0Of, List<int> facing, System.Random dice,
                          bool padFirst)
        {
            if (row.Units.Count < 2) return;
            int throws = dice.Next();
            var best = new List<Unit>(row.Units);
            var bestGaps = new List<int>(row.Gaps);
            int bestClash = int.MaxValue;
            // the made-up end keeps its end: a residential block dealt to fill a short row
            // belongs at the edge of the downtown, not shuffled into the middle of it
            var pad = new List<Unit>();
            var free = new List<Unit>();
            foreach (var unit in row.Units) (unit.Pad ? pad : free).Add(unit);
            var order = new List<Unit>(row.Units);
            for (int k = 0; k < Orders; k++)
            {
                if (k > 0)
                {
                    Dice.Shuffle(free, dice);
                    order.Clear();
                    if (padFirst) { order.AddRange(pad); order.AddRange(free); }
                    else { order.AddRange(free); order.AddRange(pad); }
                }
                var gaps = Gaps(order, row.Depth, new System.Random(throws));
                var trial = new Row { Depth = row.Depth };
                trial.Units.AddRange(order);
                trial.Gaps.AddRange(gaps);
                // the row was dealt at a length and the line is drawn from it: an order
                // whose gaps come out to another length would put the row's land end past
                // the edge every other row is flush with
                if (trial.Length != row.Length) continue;
                int clash = 0;
                foreach (int street in Streets(trial, x0Of(trial.Length)))
                    foreach (int other in facing)
                    {
                        int apart = System.Math.Abs(street - other);
                        if (apart > 0 && apart < StreetGap + StreetGap) clash++;
                    }
                if (clash >= bestClash) continue;
                bestClash = clash;
                best = new List<Unit>(order);
                bestGaps = gaps;
                if (clash == 0) break;
            }
            row.Units.Clear();
            row.Units.AddRange(best);
            row.Gaps.Clear();
            row.Gaps.AddRange(bestGaps);
        }

        /// <summary>The gaps between neighbours in this order: a street, or an alley where
        /// both are as deep as the row and walled the length of it and neither is a park.</summary>
        static List<int> Gaps(List<Unit> units, int depth, System.Random dice)
        {
            var gaps = new List<int>();
            for (int u = 1; u < units.Count; u++)
            {
                var last = units[u - 1];
                var unit = units[u];
                bool alley = last.D == depth && unit.D == depth &&
                             last.Straight(false) && unit.Straight(true) &&
                             !last.Pad && !unit.Pad &&
                             !IsPark(last.Members[0]) && !IsPark(unit.Members[0]) &&
                             dice.NextDouble() < AlleyOdds;
                gaps.Add(alley ? AlleyGap : StreetGap);
            }
            return gaps;
        }

        static int[] Quarters(System.Random dice)
        {
            var quarters = new List<int> { 0, 90, 180, 270 };
            Dice.Shuffle(quarters, dice);
            return quarters.ToArray();
        }

        // ------------------------------------------------------------- the verdict

        /// <summary>
        /// How many deals of one seed are tried before the best of them is taken with its
        /// faults on record.
        ///
        /// It was eighty, and eighty was enough while a short row was made up with one park:
        /// one more street to fall clear of the facing row's. Made up with residential blocks
        /// a row carries two to four more streets, every one of them another chance for two
        /// junction boxes to run together, and the share of deals that come out clean falls
        /// from six in a hundred to five - which over thirty seeds left two of them with a
        /// fault. The deals are arithmetic and cost about three milliseconds each; the drawing
        /// is what has to be right. With the five quarters round the core it is four hundred:
        /// the quarters themselves are regular and hardly ever fault, but the core is dealt
        /// afresh for each try and thirty seeds wanted as many as 231 of them.
        /// </summary>
        public const int Deals = 600;

        /// <summary>
        /// The plan for a seed, with the roads drawn off it and the drawing judged:
        /// <see cref="SyntySeed"/> gives the demo's arrangement, any other seed a deal of
        /// the rows. A deal whose drawing has a fault - ground left bare, a block with no
        /// road down a side, a stub of road, two blocks on one cell - is thrown away and the
        /// seed's next deal tried, up to <see cref="Deals"/> times; the same seed always
        /// runs the same deals, so the same seed is the same city. If none is clean the
        /// cleanest is kept, and its report says what is wrong with it.
        /// </summary>
        public static Plan Arrange(List<Block> blocks, int seed, out CoreRoads.Raster raster)
        {
            if (seed == SyntySeed)
            {
                // the reference is the demo's arrangement and nothing else. A block made
                // since the harvest never stood there, and inventing a place for it would
                // change the one thing the reference is kept for - so it is stood aside,
                // out of the plan and out of the raster, and deals under every other seed.
                var stood = Stood(blocks);
                var synty = Synty(stood);
                Aside(blocks, stood);
                raster = CoreRoads.Build(stood, synty);
                return synty;
            }
            Plan best = null;
            CoreRoads.Raster bestRaster = null;
            int bestFaults = int.MaxValue;
            for (int attempt = 0; attempt < Deals; attempt++)
            {
                var plan = Roll(blocks, unchecked(seed * 1000003 + attempt * 7919));
                plan.Seed = seed;
                plan.Attempt = attempt;
                plan.Name = $"seed {seed}" + (attempt > 0 ? $" (deal {attempt + 1})" : "");
                // THE PARKS GO TO THE ROAD READER TOO, and so does the river's ground. They
                // are made by the deal rather than handed in, so the caller's list has none
                // of them - and a park left out of the raster is a hole: the deal spaces the
                // row for it, nothing fills the ground, and the verdict calls it bare. That
                // alone took the share of clean deals from 72 % to 24 %.
                var drawn = CoreRoads.Build(WithGround(blocks, plan), plan);
                // A RUN OF PARKS IS A FAULT LIKE ANY OTHER. It is the plan's fault rather
                // than the drawing's - the roads come out perfectly well - so it is added
                // here, where a deal is accepted or thrown away, and not smuggled into the
                // road reader's own count
                int faults = drawn.Faults + Mathf.Max(0, plan.ParkRuns - 1);
                if (faults == 0)
                {
                    raster = drawn;
                    return plan;
                }
                if (bestRaster == null || faults < bestFaults)
                {
                    best = plan;
                    bestRaster = drawn;
                    bestFaults = faults;
                }
            }
            // the blocks stand where the last deal left them: put them back on the best.
            // The re-deal makes its own parks, so the plan handed back is the one to read
            // them off - the earlier plan's are the wrong objects in the right places.
            var again = Roll(blocks, unchecked(seed * 1000003 + best.Attempt * 7919));
            again.Seed = best.Seed;
            again.Attempt = best.Attempt;
            again.Name = best.Name;
            raster = CoreRoads.Build(WithGround(blocks, again), again);
            return again;
        }

        /// <summary>The caller's blocks and the ground the deal made itself - the parks, the
        /// stretches of promenade, the far bank's kerb - as one list for the road reader.
        /// Neither list is touched.</summary>
        public static List<Block> WithGround(List<Block> blocks, Plan plan)
        {
            if (plan.Parks.Count == 0 && plan.Quays.Count == 0 && plan.Residential.Count == 0 &&
                plan.Bank == null) return blocks;
            var all = new List<Block>(blocks);
            all.AddRange(plan.Parks);
            all.AddRange(plan.Residential);
            all.AddRange(plan.Quays);
            all.AddRange(plan.Aprons);
            if (plan.Bank != null) all.Add(plan.Bank);
            return all;
        }

        /// <summary>Of these blocks, the ones the demo itself stood - the only ones the
        /// reference arrangement knows a place for.</summary>
        static List<Block> Stood(List<Block> blocks)
        {
            var stood = new List<Block>();
            foreach (var block in blocks)
                foreach (var stand in Blocks)
                    if (stand.Demo && stand.Prefab == block.Name) { stood.Add(block); break; }
            return stood;
        }

        /// <summary>How far south of the demo's own ground a block the reference has no
        /// place for is stood. Their southernmost lane runs at z -145, so this is clear
        /// of everything the arrangement draws.</summary>
        const float AsideAt = -300f;

        /// <summary>
        /// Stands the blocks the reference left out in a row of their own, well clear of it.
        ///
        /// They have to be stood SOMEWHERE: a caller places every block it loaded, and one
        /// left where the last deal happened to put it would sit in the middle of the
        /// drawing - on the boulevard, through a block - looking for all the world like
        /// part of the reference.
        /// </summary>
        static void Aside(List<Block> all, List<Block> stood)
        {
            float x = 0f;
            foreach (var block in all)
            {
                if (stood.Contains(block)) continue;
                block.Turn(0);
                block.Shift = Vector2.zero;
                block.Lot = Rect.zero;
                block.Pivot = new Vector2(x - block.Ground0.min.x, AsideAt - block.Ground0.min.z);
                x += (block.CW + 3) * Cell;
            }
        }
    }
}
