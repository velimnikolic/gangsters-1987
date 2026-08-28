using System;
using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// A block's pavement, laid the way the POLYGON City artists laid theirs.
    ///
    /// Measured off the sixteen blocks harvested out of their demo scene - 410 kerb
    /// tiles, 590 flat squares, 74 corners, 13 drains - and the reading is short:
    ///
    ///  * everything sits on the pack's 5 m module, every tile pivoted on its +X/+Z
    ///    corner at yaw 0, exactly as the road tiles are;
    ///  * THE PAVEMENT IS ONE TILE WIDE. Of the 350 kerb tiles with a building anywhere
    ///    behind them, 275 have that building exactly 5 m in: the facade stands on the
    ///    kerb tile's inner edge and there is nothing between. The wider bands - 10, 25,
    ///    40 m - are yards, parks and car parks, never the pavement itself;
    ///  * the kerb runs round the whole block, SM_Env_Sidewalk_Straight_01 turned to face
    ///    OUT: north 0, east 90, south 180, west 270. All 410 agree;
    ///  * a corner tile wraps one CORNER of its own cell, and which corner is the whole of
    ///    the rule: NE 0, SE 90, SW 180, NW 270. The plain corner (Corner_01, 15 of them)
    ///    and the inner corner (Corner_02, 11) share that table exactly - the plain one
    ///    turns the kerb outward round a convex corner, the inner one inward round a
    ///    notch. The dipped corner, the one with the ramp down to a crossing
    ///    (Dip_Corner_01, 48 - three quarters of all corners) is the same table turned a
    ///    quarter: NE 90, SE 180, SW 270, NW 0. All 74 agree;
    ///  * inside the kerb, the plain square SM_Env_Sidewalk_01 - and in eleven of the
    ///    sixteen blocks it is laid UNDER the buildings as well as between them, which is
    ///    why a block dropped on the city is never seen through;
    ///  * a gutter tile in the run every dozen kerb tiles or so, a storm drain.
    ///
    /// And the furniture, measured the same way (every prop standing on one of the 511
    /// kerb tiles, its place read in the kerb's own frame). It stands in TWO LANES and
    /// nowhere between:
    ///
    ///  * the KERB lane, a metre to a metre and a half in from the kerb line - the street
    ///    lamp (64 of 70 at exactly a metre, facing out, its arm reaching over the
    ///    carriageway), the bollards, the bin, the hydrant, the postbox;
    ///  * the WALL lane, four to four and a half metres in, hard against the building -
    ///    the bench and the newspaper box. Everything else measured out there is mounted
    ///    ON the wall (planters, dishes, cameras, posters) and belongs to the building,
    ///    not to the pavement, so none of it is laid here.
    ///
    /// The rhythm: a lamp every 20 m along a run (21 of their 38 gaps, 9 more at 25) and
    /// the first one A CELL IN from the corner, never on it; a row of bollards a cell in
    /// from a corner; then one bin per 16 kerb tiles, a hydrant and a postbox per 46, a
    /// bench per 85, a newspaper box per 102.
    ///
    /// So a block needs nothing but the ground its buildings stand on: grow that by the
    /// one tile and the shape which comes out IS the block, kerb, corners and all. What
    /// this will not do is guess at ground the buildings did not claim - a tray is not a
    /// block, and paving the whole rectangle would only invent a yard nobody asked for.
    /// Ground beyond the band is left alone and counted in the report.
    ///
    /// The same code lays in the editor and in the game; only the way a prefab is stood
    /// differs, and that is the delegate passed in - the arrangement CoreRoads uses.
    /// </summary>
    public static class CorePavement
    {
        public const float Cell = CoreBlockMetrics.Cell;
        const string CityEnv = "Assets/Synty/PolygonCity/Prefabs/Environments/";

        const string Kerb = "SM_Env_Sidewalk_Straight_01";
        const string Flat = "SM_Env_Sidewalk_01";
        const string DippedCorner = "SM_Env_Sidewalk_Dip_Corner_01";
        const string PlainCorner = "SM_Env_Sidewalk_Corner_01";
        const string InnerCorner = "SM_Env_Sidewalk_Corner_02";
        const string Drain = "SM_Env_Sidewalk_Gutter_01";

        /// <summary>The asphalt a vehicle way is made of, kerb line and all. The pack's
        /// dropped kerb (SM_Env_Sidewalk_Dip_01) is NOT used: it is paving across its whole
        /// five metres, so a drive laid with it reads from above as more pavement and the
        /// block looks closed. A drive is road, right up to the carriageway.</summary>
        const string Road = "SM_Env_Road_Bare_01";

        /// <summary>The painted bays a car park is laid with. The odd one out of the whole
        /// kit: TEN metres by five, so it covers a PAIR of cells and is stood on the pair
        /// rather than on a cell. A run with an odd cell left at the end gets plain asphalt
        /// there instead of half a bay.</summary>
        const string Bay = "SM_Env_Road_ParkingLines_01";

        /// <summary>How many bays are painted on one of those tiles, and how deep they are.
        /// Three across the ten metres, which is 3.33 m apiece - measured off the tile, and
        /// wide enough for the pack's own cars, which the four a real car park would have
        /// fitted in that width would not have been.</summary>
        const int BaysPerTile = 3;

        const string PropsDir = "Assets/Synty/PolygonCity/Prefabs/Props/";

        /// <summary>The street lamp: mast and cantilever arm in one piece, 6.5 m tall, the
        /// arm reaching 2.5 m so that standing a metre in from the kerb line it hangs over
        /// the carriageway. Which is also why it faces OUT.</summary>
        const string Lamp = "SM_Prop_LightPole_Base_01";

        /// <summary>A row of bollards, 3.2 m of them - one piece, laid along the kerb.</summary>
        const string Bollards = "SM_Prop_SidewalkPoles_01";

        /// <summary>
        /// The palm and the basket at its foot, from the palm city rather than this pack -
        /// the pair the game's own kerbs have always carried (SidewalkDressing.Tree).
        ///
        /// Both stand on the SAME spot: the grate is the ground the palm grows out of, so
        /// it is laid first and the palm inside it. Six palms to choose from and two
        /// grates, and the palm is turned to any angle at all rather than to a quarter -
        /// a tree has no facing.
        /// </summary>
        const string PalmDir = "Assets/Synty/PolygonPalmCity/Prefabs/Environment/";
        const string Basket = "SM_Env_Plant_Grate_0";

        /// <summary>Public because a block that already has one of these is a block that has
        /// been planted, which is how <see cref="Plant"/> stays idempotent.</summary>
        public const string PalmPiece = "SM_Env_Tree_Palm_0";
        const string Palm = PalmPiece;
        const int Baskets = 2, PalmKinds = 6;

        /// <summary>Kerb tiles per palm - a few of them, not an avenue.</summary>
        const int PalmEvery = 10;

        /// <summary>Cells between lamps along one run of kerb, and how far in from its end
        /// the first one stands. Twenty metres is their commonest gap by a wide margin,
        /// and a lamp is a cell in from the corner, never on it.</summary>
        const int LampEvery = 4, LampInset = 1;

        /// <summary>The two lanes the furniture stands in, metres in from the kerb line.
        /// Nothing of theirs stands between them.</summary>
        const float LampLane = 1f, KerbLane = 1.5f, WallLane = 4f;

        /// <summary>Where a palm stands: a metre further in than the rest of the kerbside
        /// furniture (2026-08-25, "palme mogu po metra ka unutrasnjosti"). It earns the
        /// room - a palm's head is wide, and at the kerb line it hangs over the
        /// carriageway rather than over the pavement it is planted in.</summary>
        const float PalmLane = KerbLane + 1f;

        /// <summary>One piece of street furniture and how thickly it is spread.</summary>
        readonly struct Furniture
        {
            public readonly string Piece;
            /// <summary>Metres in from the kerb line.</summary>
            public readonly float Across;
            /// <summary>Kerb tiles per one of these.</summary>
            public readonly int Every;
            /// <summary>May it stand any way round, or does it face the street?</summary>
            public readonly bool Turns;
            /// <summary>Does it want a wall at its back?</summary>
            public readonly bool Backed;

            public Furniture(string piece, float across, int every, bool turns, bool backed)
            {
                Piece = piece;
                Across = across;
                Every = every;
                Turns = turns;
                Backed = backed;
            }
        }

        static readonly Furniture[] Kerbside =
        {
            new Furniture("SM_Prop_Trashbin_01", KerbLane, 16, true, false),
            new Furniture("SM_Prop_Hydrant_01", KerbLane, 46, true, false),
            new Furniture("SM_Prop_Mailbox_01", KerbLane, 46, false, false),
            new Furniture("SM_Prop_ParkBench_01", WallLane, 85, false, true),
            new Furniture("SM_Prop_Newspaper_02", WallLane + 0.5f, 102, false, true),
        };

        /// <summary>How much of a cell a building has to cover before the cell counts as
        /// built on - the tray's own figure, for the same reason: a building overlapping a
        /// cell by a hand's breadth has not taken it, and the demo's own blocks show the
        /// facades overhanging their kerb tile by rather more than that.</summary>
        public const float Covers = 0.35f;

        /// <summary>Kerb tiles between storm drains. Their blocks run from one drain in
        /// thirteen tiles to one in forty; a dozen is the middle of that and reads as a
        /// street rather than as a pattern.</summary>
        const int DrainEvery = 12;

        /// <summary>How far a dent may be filled in before the outline is called straight.
        /// It converges in two or three passes on anything real; the cap is against a
        /// shape nobody has thought of.</summary>
        const int Passes = 8;

        /// <summary>
        /// How many bites a block may have out of it, and how small a bite is allowed to
        /// be (cells each way).
        ///
        /// A block is a RECTANGLE with a notch or two out of it - an L, or a rectangle with
        /// a yard bitten into one side. It is never a cross. Grown straight off whatever
        /// buildings are dropped on a tray the outline is a ragged star, so it is taken
        /// back to its own bounding rectangle and only the biggest two notches are kept,
        /// each squared off flush against the side it is bitten out of. Everything else
        /// fills in - ground inside a block is a yard, and their blocks are full of yards.
        ///
        /// Two is the user's figure (2026-08-25): "max 2 ubacene strane". It squares off a
        /// little harder than the artists did in one or two places - their block-12 is a
        /// staircase of four steps - and that is the trade taken deliberately.
        /// </summary>
        const int MostNotches = 2, SmallestNotch = 2;

        /// <summary>A raster bigger than this is not a block - it is a rectangle laid over
        /// half the city, and paving it would stand tens of thousands of tiles.</summary>
        const int MostCells = 40000;

        /// <summary>
        /// How far under the ground a thing has to reach before the ground is understood to
        /// be OPEN there - a sunken entrance stair, a basement door, a subway mouth - and
        /// the floor is not laid over it.
        ///
        /// The figure is read off the pack rather than guessed, and the gap it sits in is
        /// wide: a kerb tile's own skirt stops at -0.23 m and a tree's roots at -0.50, while
        /// every real hole in their city is a metre or more (an apartment's entrance stair
        /// -1.50, a subway entrance -4.15, the deepest -6.43). Sixty centimetres separates
        /// the two with room to spare.
        ///
        /// This is the city's own rule, the same one the block prefabs are laid by: a unit
        /// stands at y = 0 as the pack authored it, and nothing draws a floor under the part
        /// of it that goes below. Their own block-07 - all sunken stoops - carries no floor
        /// inside its kerb at all.
        /// </summary>
        public const float Underground = -0.6f;

        /// <summary>The ground of one block, on the 5 m raster: what the buildings cover,
        /// and what the pavement round them comes to.</summary>
        public sealed class Plan
        {
            /// <summary>World x and z of the low corner of cell (0, 0).</summary>
            public float X0, Z0;
            public int NX, NZ;

            /// <summary>The block itself - every cell that gets a tile.</summary>
            public bool[,] Ground;

            /// <summary>The cells the buildings stand on.</summary>
            public bool[,] Built;

            /// <summary>The cells the block DROPS THROUGH: a sunken entrance stair, a
            /// basement door, a subway mouth, a station's basement garage. They get no
            /// floor - see <see cref="Underground"/>.</summary>
            public bool[,] Sunken;

            /// <summary>And how far down the floor of a sunken cell lies. Nought where the
            /// cell is not sunken. What a way out has to climb - see <see cref="Slope"/>.</summary>
            public float[,] Depth;

            /// <summary>The cells a BUILDING stands over. Not the same as <see cref="Built"/>,
            /// which is a bounding box: a yard cut into a building is built and not roofed,
            /// which is the whole difference a way out is found by.</summary>
            public bool[,] Roofed;

            /// <summary>The cells a vehicle way runs over, from an open yard out to the
            /// street. Road, not pavement - see <see cref="Ways"/>.</summary>
            public bool[,] Drive;

            /// <summary>The cells a DECLARED gate stands in: a way into a walled yard that
            /// no mesh can be read for, because a shut gate is a wall.</summary>
            public bool[,] Gate;

            /// <summary>The cells a way out CLIMBS over, between the street and a yard whose
            /// floor lies below the pavement. They get no tile of any kind: a tile is flat
            /// and this ground is not - the ramp is drawn over them instead
            /// (<see cref="Slopes"/>).</summary>
            public bool[,] Ramp;

            /// <summary>Every ramp the ways out need, ready to be drawn by
            /// <see cref="CoreRamp"/>.</summary>
            public List<Slope> Slopes;

            /// <summary>The cells given over to a CAR PARK cut into the pavement: asphalt
            /// and painted bays rather than paving, and the kerb dropped in front of them.
            /// Declared by whoever composes the block, never read off a mesh - a building
            /// does not say where the cars belonging to it stand.</summary>
            public bool[,] Park;

            /// <summary>Which way a car parked in it faces, as a quarter turn. The bays lie
            /// across that, and so does the tile they are painted on.</summary>
            public int ParkYaw;

            /// <summary>The middle of every bay the car park was painted with, in world
            /// metres, filled as they are laid. What a block's own cars are stood on - the
            /// pavement knows where the paint went and nothing else has to work it out
            /// again.</summary>
            public List<Vector3> Stalls;

            public int Cells, BuiltCells, SunkenCells, DriveCells;

            public bool Any => Cells > 0;

            public bool In(int i, int j) =>
                i >= 0 && j >= 0 && i < NX && j < NZ && Ground[i, j];

            /// <summary>The block's footprint in world metres.</summary>
            public Rect Area
            {
                get
                {
                    int i0 = NX, j0 = NZ, i1 = -1, j1 = -1;
                    for (int i = 0; i < NX; i++)
                        for (int j = 0; j < NZ; j++)
                        {
                            if (!Ground[i, j]) continue;
                            if (i < i0) i0 = i;
                            if (j < j0) j0 = j;
                            if (i > i1) i1 = i;
                            if (j > j1) j1 = j;
                        }
                    if (i1 < 0) return new Rect();
                    return new Rect(X0 + i0 * Cell, Z0 + j0 * Cell,
                                    (i1 - i0 + 1) * Cell, (j1 - j0 + 1) * Cell);
                }
            }
        }

        /// <summary>
        /// One run of way that has to CLIMB: the ground between a yard sunk below the
        /// pavement and the street it is reached from.
        ///
        /// Held as the rectangle of cells it covers rather than as a list of them, because
        /// a ramp is one surface and drawing it cell by cell would put a crease across it
        /// every five metres.
        /// </summary>
        public readonly struct Slope
        {
            /// <summary>The cells it covers, in world metres (x, z).</summary>
            public readonly Rect Area;

            /// <summary>Which way it climbs, on the cell raster: away from the yard,
            /// towards the street.</summary>
            public readonly Vector2Int Up;

            /// <summary>How far below the pavement the yard at its foot lies - negative,
            /// and the height its low edge is drawn at.</summary>
            public readonly float Foot;

            public Slope(Rect area, Vector2Int up, float foot)
            {
                Area = area;
                Up = up;
                Foot = foot;
            }

            /// <summary>How far it runs, and how wide it is, in metres.</summary>
            public float Long => Mathf.Abs(Up.x) * Area.width + Mathf.Abs(Up.y) * Area.height;
            public float Wide => Mathf.Abs(Up.x) * Area.height + Mathf.Abs(Up.y) * Area.width;

            /// <summary>The middle of it, at the pavement's own height.</summary>
            public Vector3 Middle(float y) => new Vector3(Area.center.x, y, Area.center.y);

            /// <summary>Turned so that +Z is the way it climbs.</summary>
            public Quaternion Facing =>
                Quaternion.LookRotation(new Vector3(Up.x, 0f, Up.y), Vector3.up);
        }

        /// <summary>
        /// The block the buildings make: their footprint on the 5 m raster, grown by
        /// <paramref name="band"/> tiles of pavement. CoreDemo's shared default is two
        /// tiles (10 m); callers only override it for a deliberately special block.
        ///
        /// Grown on the DIAGONAL as well as the square, so a rectangular building comes out
        /// with square corners; growing on the square alone cuts every corner off and the
        /// kerb arrives as an octagon. Then two tidyings, both of which the artists' own
        /// outlines pass and a raw grown outline does not: any cell the block encloses
        /// joins it (a yard between two wings is paved, not a hole to see the city
        /// through), and any dent with three of its four sides already in joins it too, so
        /// the kerb runs straight instead of stepping in and out round every bay window.
        /// </summary>
        public static Plan Around(IEnumerable<Bounds> buildings,
                                  int band = CoreBlockMetrics.PavementTiles,
                                  IEnumerable<Bounds> sinks = null,
                                  IEnumerable<Bounds> roofs = null,
                                  IEnumerable<Bounds> gates = null,
                                  IEnumerable<Bounds> parks = null, int parkYaw = 0)
        {
            var boxes = new List<Bounds>();
            if (buildings != null)
                foreach (var box in buildings)
                    if (box.size.x > 0f || box.size.z > 0f) boxes.Add(box);

            var plan = Empty();
            if (boxes.Count == 0) return plan;

            var world = boxes[0];
            for (int b = 1; b < boxes.Count; b++) world.Encapsulate(boxes[b]);

            int margin = Mathf.Max(1, band) + 2;
            plan.X0 = Mathf.Floor(world.min.x / Cell) * Cell - margin * Cell;
            plan.Z0 = Mathf.Floor(world.min.z / Cell) * Cell - margin * Cell;
            plan.NX = Mathf.CeilToInt((world.max.x - plan.X0) / Cell) + margin;
            plan.NZ = Mathf.CeilToInt((world.max.z - plan.Z0) / Cell) + margin;
            if (plan.NX < 3 || plan.NZ < 3 || (long)plan.NX * plan.NZ > MostCells)
            {
                Debug.LogWarning($"[CorePavement] {plan.NX} x {plan.NZ} cells is not a block - " +
                                 "nothing was paved.");
                return Empty();
            }

            plan.Built = new bool[plan.NX, plan.NZ];
            plan.Sunken = new bool[plan.NX, plan.NZ];
            plan.Depth = new float[plan.NX, plan.NZ];
            plan.Roofed = new bool[plan.NX, plan.NZ];
            plan.Drive = new bool[plan.NX, plan.NZ];
            plan.Gate = new bool[plan.NX, plan.NZ];
            plan.Ramp = new bool[plan.NX, plan.NZ];
            plan.Park = new bool[plan.NX, plan.NZ];
            plan.ParkYaw = ((parkYaw % 360) + 360) % 360;
            Claim(plan, boxes, plan.Built);
            if (sinks != null) Sink(plan, sinks);
            if (roofs != null) Claim(plan, roofs, plan.Roofed);
            if (gates != null) Claim(plan, gates, plan.Gate);
            if (parks != null) Claim(plan, parks, plan.Park);

            var ground = (bool[,])plan.Built.Clone();
            for (int step = 0; step < band; step++) ground = Grown(ground, plan.NX, plan.NZ);
            Enclose(ground, plan.NX, plan.NZ);
            Square(ground, plan.NX, plan.NZ);
            Straighten(ground, plan.NX, plan.NZ);
            plan.Ground = ground;

            // a car park is cut out of the PAVEMENT, not out of the kerb: a declared cell
            // at the block's own edge keeps its kerb and is no part of the lot
            for (int i = 0; i < plan.NX; i++)
                for (int j = 0; j < plan.NZ; j++)
                    if (plan.Park[i, j] &&
                        !(plan.In(i + 1, j) && plan.In(i - 1, j) &&
                          plan.In(i, j + 1) && plan.In(i, j - 1)))
                        plan.Park[i, j] = false;

            Ways(plan);

            for (int i = 0; i < plan.NX; i++)
                for (int j = 0; j < plan.NZ; j++)
                {
                    if (ground[i, j]) plan.Cells++;
                    if (plan.Built[i, j]) plan.BuiltCells++;
                    if (ground[i, j] && plan.Sunken[i, j]) plan.SunkenCells++;
                    if (plan.Drive[i, j]) plan.DriveCells++;
                }
            return plan;
        }

        static Plan Empty() => new Plan
        {
            Ground = new bool[0, 0], Built = new bool[0, 0], Sunken = new bool[0, 0],
            Depth = new float[0, 0], Roofed = new bool[0, 0], Drive = new bool[0, 0],
            Gate = new bool[0, 0], Ramp = new bool[0, 0], Slopes = new List<Slope>(),
            Park = new bool[0, 0], Stalls = new List<Vector3>(),
        };

        /// <summary>How wide a hole has to be, across the way it faces, before it is taken
        /// for a gate a vehicle uses rather than a stair somebody walks down. Two cells is
        /// ten metres: a car needs it and a basement stair never has it.</summary>
        const int GateWide = 2;

        /// <summary>
        /// The way out of every open yard: a run of road from the yard to the street, so a
        /// car can reach the ramp it is plainly meant to reach.
        ///
        /// The direction is READ, not declared. A yard is cut into a building and open on
        /// one side; out of the four, a side counts where <see cref="GateWide"/> of the
        /// yard's cells can each drive straight out to the block's edge crossing nothing
        /// roofed and nothing else open - you cannot drive through the station. A single
        /// column stopped by a corner of the building only narrows the gate. Of the sides that qualify the shortest wins, and a tie goes to the wider
        /// mouth. A yard with no side that qualifies gets no way out and stays a hole,
        /// which is the honest answer for a light well.
        /// </summary>
        static void Ways(Plan plan)
        {
            Mouths(plan, plan.Sunken, GateWide);   // a hole wide enough to be a gate
            Mouths(plan, plan.Gate, 1);            // and a gate somebody wrote down
            Drives(plan);                          // and the two ends a car park is entered at
        }

        /// <summary>One pass of <see cref="Ways"/> over one kind of mouth. A declared gate
        /// needs no width test - saying it is a gate is the test.</summary>
        static void Mouths(Plan plan, bool[,] mouth, int least)
        {
            var seen = new bool[plan.NX, plan.NZ];
            var yard = new List<Vector2Int>();
            var lanes = new List<List<Vector2Int>>();

            for (int i = 0; i < plan.NX; i++)
                for (int j = 0; j < plan.NZ; j++)
                {
                    if (seen[i, j] || !plan.Ground[i, j] || !mouth[i, j]) continue;

                    // the yard this cell belongs to, four-connected
                    yard.Clear();
                    yard.Add(new Vector2Int(i, j));
                    seen[i, j] = true;
                    for (int at = 0; at < yard.Count; at++)
                        foreach (var step in Steps)
                        {
                            var next = yard[at] + step;
                            if (next.x < 0 || next.y < 0 || next.x >= plan.NX || next.y >= plan.NZ) continue;
                            if (seen[next.x, next.y] || !plan.Ground[next.x, next.y] ||
                                !mouth[next.x, next.y]) continue;
                            seen[next.x, next.y] = true;
                            yard.Add(next);
                        }

                    var held = new HashSet<Vector2Int>(yard);
                    int bestLong = int.MaxValue, bestWide = 0;
                    var best = new List<List<Vector2Int>>();
                    var bestStep = Vector2Int.zero;

                    foreach (var step in Steps)
                    {
                        lanes.Clear();
                        int longest = 0;

                        foreach (var cell in yard)
                        {
                            // only the cells at the yard's face on this side open onto it
                            if (held.Contains(cell + step)) continue;

                            // a lane of its own: one column blocked by the building's corner
                            // does not shut the gate, it only makes it narrower
                            var run = new List<Vector2Int>();
                            bool blocked = false;
                            var walk = cell + step;
                            while (walk.x >= 0 && walk.y >= 0 && walk.x < plan.NX && walk.y < plan.NZ &&
                                   plan.Ground[walk.x, walk.y])
                            {
                                if (plan.Roofed[walk.x, walk.y] || plan.Sunken[walk.x, walk.y] ||
                                    plan.Drive[walk.x, walk.y])
                                { blocked = true; break; }
                                run.Add(walk);
                                walk += step;
                            }
                            if (blocked || run.Count == 0) continue;
                            longest = Mathf.Max(longest, run.Count);
                            lanes.Add(run);
                        }

                        int wide = lanes.Count;
                        if (wide < least) continue;
                        if (longest > bestLong || (longest == bestLong && wide <= bestWide)) continue;
                        bestLong = longest;
                        bestWide = wide;
                        bestStep = step;
                        best.Clear();
                        best.AddRange(lanes);
                    }

                    foreach (var run in best)
                        foreach (var cell in run) plan.Drive[cell.x, cell.y] = true;
                    Climb(plan, best, bestStep);
                }
        }

        /// <summary>
        /// A way out of a yard whose floor lies BELOW the pavement has to climb, and no flat
        /// tile can be laid on the part of it that does: the run carries a drawn ramp
        /// instead (<see cref="Slope"/>), cut into the pavement between its two walls.
        ///
        /// The crossing - the one cell against the street, where the kerb drops - stays flat
        /// and keeps its tile. Everything between it and the yard is the ramp, which is
        /// what sets the grade: the police station's yard is three metres down and fifteen
        /// metres in, which is one in five, and one in five is what a car park ramp is.
        ///
        /// A yard at the pavement's own level wants none of this and gets none.
        /// </summary>
        static void Climb(Plan plan, List<List<Vector2Int>> lanes, Vector2Int step)
        {
            if (lanes.Count == 0) return;

            // how far down the yard is, read at the cells the run actually comes off
            float foot = 0f;
            foreach (var run in lanes)
            {
                var mouth = run[0] - step;
                foot = Mathf.Min(foot, plan.Depth[mouth.x, mouth.y]);
            }
            if (foot > Underground) return;

            int i0 = int.MaxValue, j0 = int.MaxValue, i1 = int.MinValue, j1 = int.MinValue;
            int cells = 0;
            foreach (var run in lanes)
                for (int at = 0; at < run.Count - 1; at++)
                {
                    var on = run[at];
                    plan.Ramp[on.x, on.y] = true;
                    cells++;
                    i0 = Mathf.Min(i0, on.x); i1 = Mathf.Max(i1, on.x);
                    j0 = Mathf.Min(j0, on.y); j1 = Mathf.Max(j1, on.y);
                }
            if (i1 < i0) return;

            // one surface is drawn over the whole rectangle, so the run has to BE a
            // rectangle. A ragged one - columns starting at different ranks along the yard's
            // face - would have the deck lying over cells that still carry their tile, and a
            // stepped mouth is better left flat than covered with a lie
            if (cells != (i1 - i0 + 1) * (j1 - j0 + 1))
            {
                for (int i = i0; i <= i1; i++)
                    for (int j = j0; j <= j1; j++) plan.Ramp[i, j] = false;
                return;
            }

            plan.Slopes.Add(new Slope(
                new Rect(plan.X0 + i0 * Cell, plan.Z0 + j0 * Cell,
                         (i1 - i0 + 1) * Cell, (j1 - j0 + 1) * Cell),
                step, foot));
        }

        /// <summary>
        /// The way in and out of a car park: its two ENDS, and nothing between them.
        ///
        /// A lot is not a driveway - it is bays with a lane in front of them, and the lane
        /// is a lane because the kerb runs past it and lets cars in only where it stops. Open
        /// the whole frontage instead and the lot is just tarmac spilling onto the street,
        /// which is the very thing the lane was added to stop.
        ///
        /// The ends are read, not declared: the lane's own row, its first and last cell
        /// across, driven straight out to the block's edge. A lot one cell across has one
        /// end and gets one way in, which is honest for a lot that small.
        /// </summary>
        static void Drives(Plan plan)
        {
            var front = FrontOf(plan.ParkYaw);
            var mouths = new List<Vector2Int>();

            for (int i = 0; i < plan.NX; i++)
                for (int j = 0; j < plan.NZ; j++)
                {
                    if (!plan.In(i, j) || !plan.Park[i, j]) continue;
                    // the lane, not the bays: the row with nothing of the lot in front of it
                    if (plan.In(i + front.x, j + front.y) && plan.Park[i + front.x, j + front.y])
                        continue;
                    mouths.Add(new Vector2Int(i, j));
                }
            if (mouths.Count == 0) return;

            // across the lane, so its two ends are the two extremes
            bool acrossX = front.x == 0;
            mouths.Sort((a, b) => acrossX ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));

            Out(plan, mouths[0], front);
            if (mouths.Count > 1) Out(plan, mouths[mouths.Count - 1], front);
        }

        /// <summary>Which way is OUT of a car park: the side the cars' tails are on, since a
        /// bay is entered from behind.</summary>
        static Vector2Int FrontOf(int yaw)
        {
            switch (((yaw % 360) + 360) % 360)
            {
                case 0: return new Vector2Int(0, -1);
                case 90: return new Vector2Int(-1, 0);
                case 180: return new Vector2Int(0, 1);
                default: return new Vector2Int(1, 0);
            }
        }

        /// <summary>One run of drive from a cell out to the block's edge. Stops at anything
        /// roofed or open, which is the same rule <see cref="Mouths"/> walks by.</summary>
        static void Out(Plan plan, Vector2Int from, Vector2Int step)
        {
            var walk = from + step;
            while (plan.In(walk.x, walk.y))
            {
                if (plan.Roofed[walk.x, walk.y] || plan.Sunken[walk.x, walk.y] ||
                    plan.Park[walk.x, walk.y]) return;
                plan.Drive[walk.x, walk.y] = true;
                walk += step;
            }
        }

        static readonly Vector2Int[] Steps =
        {
            new Vector2Int(1, 0), new Vector2Int(-1, 0),
            new Vector2Int(0, 1), new Vector2Int(0, -1),
        };

        /// <summary>
        /// The same claim as <see cref="Claim"/>, and the DEPTH with it: a sunken box is
        /// handed over with its foot at the floor it lays down there, and where two of them
        /// fall in one cell the lower wins.
        ///
        /// Kept apart from Claim because Claim stops at the first box to take a cell, which
        /// is right when the answer is yes or no and wrong when it is a number.
        /// </summary>
        static void Sink(Plan plan, IEnumerable<Bounds> boxes)
        {
            float takes = Covers * Cell * Cell;
            foreach (var box in boxes)
            {
                int i0 = Mathf.Max(0, Mathf.FloorToInt((box.min.x - plan.X0) / Cell));
                int i1 = Mathf.Min(plan.NX - 1, Mathf.FloorToInt((box.max.x - plan.X0) / Cell));
                int j0 = Mathf.Max(0, Mathf.FloorToInt((box.min.z - plan.Z0) / Cell));
                int j1 = Mathf.Min(plan.NZ - 1, Mathf.FloorToInt((box.max.z - plan.Z0) / Cell));
                for (int i = i0; i <= i1; i++)
                    for (int j = j0; j <= j1; j++)
                    {
                        float x = plan.X0 + i * Cell, z = plan.Z0 + j * Cell;
                        float ox = Overlap(box.min.x, box.max.x, x, x + Cell);
                        float oz = Overlap(box.min.z, box.max.z, z, z + Cell);
                        if (ox * oz < takes) continue;
                        plan.Sunken[i, j] = true;
                        if (box.min.y < plan.Depth[i, j]) plan.Depth[i, j] = box.min.y;
                    }
            }
        }

        /// <summary>Marks every cell a box takes more than <see cref="Covers"/> of. A thing
        /// overlapping a cell by a hand's breadth has not taken it.</summary>
        static void Claim(Plan plan, IEnumerable<Bounds> boxes, bool[,] held)
        {
            float takes = Covers * Cell * Cell;
            foreach (var box in boxes)
            {
                int i0 = Mathf.Max(0, Mathf.FloorToInt((box.min.x - plan.X0) / Cell));
                int i1 = Mathf.Min(plan.NX - 1, Mathf.FloorToInt((box.max.x - plan.X0) / Cell));
                int j0 = Mathf.Max(0, Mathf.FloorToInt((box.min.z - plan.Z0) / Cell));
                int j1 = Mathf.Min(plan.NZ - 1, Mathf.FloorToInt((box.max.z - plan.Z0) / Cell));
                for (int i = i0; i <= i1; i++)
                    for (int j = j0; j <= j1; j++)
                    {
                        if (held[i, j]) continue;
                        float x = plan.X0 + i * Cell, z = plan.Z0 + j * Cell;
                        float ox = Overlap(box.min.x, box.max.x, x, x + Cell);
                        float oz = Overlap(box.min.z, box.max.z, z, z + Cell);
                        if (ox * oz >= takes) held[i, j] = true;
                    }
            }
        }

        /// <summary>
        /// Stands the tiles: the kerb round the outside turned to face out, a corner tile
        /// on every corner of the run, the plain square everywhere inside.
        ///
        /// <paramref name="ramps"/> gives the corners the dipped tile, the one with the
        /// ramp down to the crossing - three quarters of the demo's corners, and right for
        /// any corner a street turns. Off, they are the plain kerb corner.
        ///
        /// <paramref name="under"/> lays the floor under the buildings as well. On, because
        /// a block is dropped whole onto a city and a hole in its floor is a hole in the
        /// world; the tiles under a building cost a draw call each and are worth it.
        ///
        /// <paramref name="props"/> stands the street furniture on it - see
        /// <see cref="Furnish"/>. Everything it stands comes off the <paramref name="seed"/>,
        /// so the same block is the same block every time it is built.
        /// </summary>
        public static int Lay(Plan plan, Func<GameObject, Transform, GameObject> stand,
                              Transform parent, out string said, float y = 0f,
                              int seed = 1987, bool ramps = true, bool under = true,
                              bool props = true)
        {
            said = "nothing to pave: no building stands on this ground.";
            if (plan == null || !plan.Any) return 0;

            var pieces = new List<Laid>(plan.Cells);
            int kerbs = 0, corners = 0, notches = 0, floor = 0, odd = 0, sunken = 0, drive = 0;
            int ramp = 0, park = 0;
            var islands = new List<Laid>();

            for (int i = 0; i < plan.NX; i++)
                for (int j = 0; j < plan.NZ; j++)
                {
                    if (!plan.Ground[i, j]) continue;

                    // WHAT THE CELL ITSELF IS, before anything is asked about its
                    // neighbours: a ramp, a car park and a way out are all block and none of
                    // them is pavement, and the kerb is laid against the PAVEMENT'S outline
                    // rather than the block's
                    if (plan.Ramp[i, j]) { ramp++; continue; }
                    if (plan.Park[i, j]) { park++; continue; }

                    // the way out is road, not pavement: the run reads as one surface from
                    // the carriageway to the yard, not as paving with a gap cut in it. The
                    // pack's dropped kerb was tried here and is the wrong piece - it is
                    // paving all the way across, so from above the drive read as pavement
                    // and the block looked ringed with no way into it (2026-08-26, the user:
                    // "policijska stanica nema ukljucenje jer si je okruzio trotoarom")
                    if (plan.Drive[i, j]) { pieces.Add(new Laid(i, j, Road, 0)); drive++; continue; }

                    // the ground is OPEN here - a sunken stair, a basement door, a basement
                    // garage - and a flat tile at nought would be a lid over it. A hole at
                    // the block's own rim keeps its kerb even so: the ring is what the block
                    // IS, and a gap in it is a gap in the city
                    bool rim = !plan.In(i, j + 1) || !plan.In(i + 1, j) ||
                               !plan.In(i, j - 1) || !plan.In(i - 1, j);
                    if (plan.Sunken[i, j] && !rim) { sunken++; continue; }

                    bool north = Paved(plan, i, j + 1), east = Paved(plan, i + 1, j);
                    bool south = Paved(plan, i, j - 1), west = Paved(plan, i - 1, j);
                    int open = (north ? 0 : 1) + (east ? 0 : 1) + (south ? 0 : 1) + (west ? 0 : 1);

                    if (open == 0)
                    {
                        // walled in on all four sides: either the middle of the block or the
                        // inside of a notch, and the diagonal is what tells those apart
                        if (!Paved(plan, i + 1, j + 1)) { pieces.Add(new Laid(i, j, InnerCorner, KerbYaw.Corner(true, true))); notches++; }
                        else if (!Paved(plan, i + 1, j - 1)) { pieces.Add(new Laid(i, j, InnerCorner, KerbYaw.Corner(false, true))); notches++; }
                        else if (!Paved(plan, i - 1, j - 1)) { pieces.Add(new Laid(i, j, InnerCorner, KerbYaw.Corner(false, false))); notches++; }
                        else if (!Paved(plan, i - 1, j + 1)) { pieces.Add(new Laid(i, j, InnerCorner, KerbYaw.Corner(true, false))); notches++; }
                        else if (under || !plan.Built[i, j]) { pieces.Add(new Laid(i, j, Flat, 0)); floor++; }
                        continue;
                    }

                    if (open == 2 && north != south && east != west)
                    {
                        int yaw = KerbYaw.Corner(!north, !east);
                        // the DIPPED corner is the one with the ramp down to a crossing, and
                        // it belongs on a corner the STREET turns. Where the pavement turns
                        // because a driveway is cut through the band the kerb does not drop -
                        // the drive runs beside it, not across it - so that corner takes the
                        // plain piece
                        bool street = ramps && !Cut(plan, i, j);
                        pieces.Add(new Laid(i, j, street ? DippedCorner : PlainCorner,
                                            street ? (yaw + 90) % 360 : yaw));
                        corners++;
                        continue;
                    }

                    // an ISLAND of pavement - the strip left standing between two driveways
                    // cut through the band - is open on three sides, and no tile of theirs
                    // kerbs three. It takes the corner it turns towards the STREET, which is
                    // the end anybody sees, and is left flush where it meets the block's own
                    // tarmac (2026-08-26, the user, of the strip in front of the police
                    // station's car park: "i ovo ostrvce ispred parkinga treba da je
                    // zaobljeno"). Turned at its ends it reads as an island; squared off it
                    // reads as paving somebody forgot to take up
                    if (open == 3 && Island(plan, i, j, north, east, south, west, out int end))
                    {
                        bool crossed = ramps && !Cut(plan, i, j);
                        var turned = new Laid(i, j, crossed ? DippedCorner : PlainCorner,
                                              crossed ? (end + 90) % 360 : end);
                        pieces.Add(turned);
                        islands.Add(turned);
                        corners++;
                        continue;
                    }

                    // one open side is a plain run of kerb; anything else is a cell the
                    // block is only one tile thick at, which cannot happen while the band
                    // is a tile or more. Kerb it towards the open side and say so.
                    if (open != 1) odd++;
                    int face = !north ? 0 : !east ? 90 : !south ? 180 : 270;

                    pieces.Add(new Laid(i, j, Kerb, face));
                    kerbs++;
                }

            // the drains, counted ALONG the kerb rather than scattered: a run of kerb with
            // one gutter in it reads as a street, three in a huddle read as a mistake
            int drains = 0;
            if (kerbs >= DrainEvery)
            {
                int step = Mathf.Abs(seed) % DrainEvery, run = 0;
                for (int p = 0; p < pieces.Count; p++)
                {
                    if (pieces[p].Piece != Kerb) continue;
                    if (run++ % DrainEvery != step) continue;
                    pieces[p] = new Laid(pieces[p].I, pieces[p].J, Drain, pieces[p].Yaw);
                    drains++;
                }
            }

            int laid = 0;
            var put = Stander(stand, parent, () => laid++);

            foreach (var piece in pieces)
                put(piece.Piece, CityEnv,
                      Pivot(plan.X0 + piece.I * Cell, plan.Z0 + piece.J * Cell, y, piece.Yaw),
                      piece.Yaw);
            int tiles = laid;

            string bays = park > 0 ? Bays(plan, y, put) : "";
            string furniture = props ? Furnish(plan, pieces, islands, y, seed, put) : "";

            var area = plan.Area;
            said = $"{area.width:F0} x {area.height:F0} m, {tiles} tile(s): {kerbs - drains} kerb, " +
                   $"{corners} corner(s), {notches} inner corner(s), {drains} drain(s), " +
                   $"{floor} square(s); the buildings cover {plan.BuiltCells} of {plan.Cells} cell(s)" +
                   (sunken > 0 ? $"; {sunken} cell(s) left open where the block drops below ground" : "") +
                   (drive > 0 ? $"; {drive} cell(s) of vehicle way out to the street" : "") +
                   (ramp > 0 ? $"; {ramp} cell(s) of it ramping down into the yard" : "") +
                   (bays.Length > 0 ? "; a car park of " + bays : "") +
                   (furniture.Length > 0 ? "; on the pavement " + furniture : "") +
                   (odd > 0
                       ? $"; {odd} cell(s) carry kerb on more than one side - the block is a tile " +
                         "thick there, which no pavement of theirs is"
                       : "");
            return laid;
        }

        /// <summary>
        /// Does this cell carry PAVEMENT? Not the same question as whether it is part of the
        /// block: a ramp, a car park and a way out are all block and none of them is paved.
        ///
        /// Which is what puts a CORNER where a driveway is cut through the band (2026-08-26,
        /// the user: "sto ne koristis cornere gde treba za pavement?"). Read against the
        /// block instead, the kerb ran straight past a drive as though it were not there and
        /// left the pavement's cut ends raw.
        /// </summary>
        static bool Paved(Plan plan, int i, int j) =>
            plan.In(i, j) && !plan.Drive[i, j] && !plan.Ramp[i, j] && !plan.Park[i, j];

        /// <summary>
        /// The END of an island: a cell of pavement open on three sides, and which corner of
        /// it the kerb turns.
        ///
        /// Three open sides means the strip is one tile thick along one axis and stops along
        /// the other, so the corner is the one between the side facing OUT of the block - the
        /// street - and whichever side the strip stops at. An island with the street on both
        /// sides of it, or on neither, has no such answer and is left square.
        /// </summary>
        static bool Island(Plan plan, int i, int j, bool north, bool east, bool south, bool west,
                           out int yaw)
        {
            yaw = 0;
            bool thin = !north && !south;      // one tile thick north to south
            if (thin == (!east && !west)) return false;

            if (thin)
            {
                bool street = !plan.In(i, j + 1);
                if (street == !plan.In(i, j - 1)) return false;
                yaw = KerbYaw.Corner(street, !east);
                return true;
            }

            bool eastward = !plan.In(i + 1, j);
            if (eastward == !plan.In(i - 1, j)) return false;
            yaw = KerbYaw.Corner(!north, eastward);
            return true;
        }

        /// <summary>Is what turns the kerb here a DRIVEWAY cut through the band, rather than
        /// the street outside the block? Told apart by the block itself: a drive is still
        /// block, the street is not.</summary>
        static bool Cut(Plan plan, int i, int j)
        {
            foreach (var step in Steps)
                if (!Paved(plan, i + step.x, j + step.y) && !plan.In(i + step.x, j + step.y))
                    return false;
            return true;
        }

        /// <summary>The middle of a tile's cell, whichever way the tile is turned.</summary>
        static Vector3 Middle(Plan plan, Laid tile, float y) =>
            Where(plan, tile, Cell * 0.5f, Cell * 0.5f, y);

        /// <summary>
        /// One palm and the grate it grows out of, on the cell whose middle and facing are
        /// given: a step along the kerb so a run of them is not a row of soldiers, the grate
        /// laid first because it is the ground the palm comes out of, and the palm turned to
        /// any angle at all - a tree has no facing.
        ///
        /// The palm lane is <see cref="PalmLane"/> in from the kerb line, which on a five
        /// metre cell is its middle; that is why this needs the middle and nothing else, and
        /// why it serves a block being laid and a block already built alike.
        /// </summary>
        static bool Sapling(Vector3 middle, float yaw, System.Random dice, Stands stand)
        {
            float along = 1.5f + (float)dice.NextDouble() * (Cell - 3f);
            var spot = middle + Quaternion.Euler(0f, yaw, 0f) * Vector3.right * (along - Cell * 0.5f);
            if (!stand(Basket + (dice.Next(Baskets) + 1), PalmDir, spot, yaw + 90 * dice.Next(4)))
                return false;
            stand(Palm + (dice.Next(PalmKinds) + 1), PalmDir, spot, (float)dice.NextDouble() * 360f);
            return true;
        }

        /// <summary>One kerb tile of a block that is ALREADY BUILT: the middle of its cell
        /// and the way it faces. What <see cref="Plant"/> is handed instead of a plan.</summary>
        public readonly struct Kerbstone
        {
            public readonly Vector3 Middle;
            public readonly float Yaw;
            public Kerbstone(Vector3 middle, float yaw) { Middle = middle; Yaw = yaw; }
        }

        /// <summary>
        /// The palms a block that was never laid by this class still wants.
        ///
        /// The sixteen blocks harvested out of the demo scene carry the artists' own street
        /// furniture and no trees, so a city dealt from a mixed pack came out with palms on
        /// the blocks this class had grown and none on the blocks it had only copied
        /// (2026-08-26, the user: "ovi stari blokovi nemaju palme, dodaj i na njih da bude
        /// uniformno"). Same rhythm and same lane as <see cref="Furnish"/> uses - one palm
        /// per <see cref="PalmEvery"/> kerb tiles by default - so a harvested block and a
        /// grown one are planted alike. A caller may request a denser rhythm for a special
        /// block without changing the city-wide default.
        ///
        /// <paramref name="standing"/> is whatever the block already has on it: no palm is
        /// put within <see cref="PalmRoom"/> of one, which is what keeps a tree out of the
        /// demo's own bus shelter. Everything comes off <paramref name="seed"/>, so planting
        /// the same block twice would plant it the same way - and it is not planted twice,
        /// because a block with a palm on it is left alone.
        /// </summary>
        public static int Plant(IReadOnlyList<Kerbstone> kerbs, IReadOnlyList<Vector3> standing,
                                Func<GameObject, Transform, GameObject> stand, Transform parent,
                                int seed, int kerbsPerPalm = PalmEvery)
        {
            if (kerbs == null || kerbs.Count == 0) return 0;

            var put = Stander(stand, parent, null);
            var dice = new System.Random(seed);

            var order = new List<int>(kerbs.Count);
            for (int i = 0; i < kerbs.Count; i++) order.Add(i);
            Dice.Shuffle(order, dice);

            int every = Mathf.Max(1, kerbsPerPalm);
            int planted = 0, want = Mathf.RoundToInt(kerbs.Count / (float)every);
            foreach (int i in order)
            {
                if (planted >= want) break;
                var stone = kerbs[i];
                if (Crowded(stone.Middle, standing)) continue;
                if (!Sapling(stone.Middle, stone.Yaw, dice, put)) break;
                planted++;
            }
            return planted;
        }

        /// <summary>How much room a palm wants to itself. A grate is 2 m across and the
        /// demo's own furniture stands on the same lane, so anything inside this is close
        /// enough to be growing through.</summary>
        const float PalmRoom = 3f;

        static bool Crowded(Vector3 spot, IReadOnlyList<Vector3> standing)
        {
            if (standing == null) return false;
            foreach (var thing in standing)
            {
                float dx = thing.x - spot.x, dz = thing.z - spot.z;
                if (dx * dx + dz * dz < PalmRoom * PalmRoom) return true;
            }
            return false;
        }

        /// <summary>Stands the pack's pieces BY NAME: each one loaded once, put under the
        /// parent and turned. Everything here that puts something down goes through it.</summary>
        static Stands Stander(Func<GameObject, Transform, GameObject> stand, Transform parent,
                              Action stood)
        {
            var kit = new Dictionary<string, GameObject>();
            return (piece, dir, at, turn) =>
            {
                if (!kit.TryGetValue(piece, out var prefab))
                {
                    prefab = DemoAssetLoad.Load<GameObject>(dir + piece + ".prefab");
                    kit[piece] = prefab;
                    if (prefab == null)
                        Debug.LogWarning($"[CorePavement] {dir}{piece}.prefab is missing; " +
                                         "nothing was stood in its place.");
                }
                if (prefab == null) return false;

                var go = stand(prefab, parent);
                if (go == null) return false;
                go.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, turn, 0f));
                stood?.Invoke();
                return true;
            };
        }

        /// <summary>How a piece is stood, whichever way the caller stands things.</summary>
        delegate bool Stands(string piece, string dir, Vector3 at, float turn);

        /// <summary>
        /// The car park, laid in the pack's own painted bays.
        ///
        /// The bay tile is the one piece in the kit that is not a five metre square - it is
        /// ten by five, four bays across it - so it is stood on a PAIR of cells, walking the
        /// lot along whichever axis the bays run on. Which axis that is comes off the way a
        /// parked car faces (<see cref="Plan.ParkYaw"/>), because the bays lie across the
        /// car, not along it; and that is declared rather than read, because nothing on a
        /// block says which way its cars should stand.
        ///
        /// A lot more than one row deep keeps its rows nearest the street as the LANE the
        /// bays are entered off, plain asphalt with no paint on it - which is the whole of
        /// what a car park has that a driveway does not. Ten metres of band will not hold a
        /// bay and a lane both (a bay is five and the pack's own cars are nearly six), so a
        /// block that wants one asks for a wider band instead of a narrower lane.
        ///
        /// A cell left over at the end of a run - an odd lot, or one broken by a way out -
        /// gets plain asphalt. Half a painted bay running off the edge of the tarmac is
        /// worse than no paint at all.
        /// </summary>
        static string Bays(Plan plan, float y, Stands stand)
        {
            bool alongX = plan.ParkYaw % 180 == 0;
            var into = -FrontOf(plan.ParkYaw);          // from the lane towards the bays
            var taken = new bool[plan.NX, plan.NZ];
            int pairs = 0, singles = 0, lane = 0;

            for (int j = 0; j < plan.NZ; j++)
                for (int i = 0; i < plan.NX; i++)
                {
                    if (!Parked(plan, i, j) || taken[i, j]) continue;

                    // the LANE: a row of the lot with more of the lot in front of it. No
                    // paint on it - a bay painted where cars drive is a bay nobody can use
                    if (Parked(plan, i + into.x, j + into.y))
                    {
                        taken[i, j] = true;
                        if (stand(Road, CityEnv, Pivot(plan.X0 + i * Cell, plan.Z0 + j * Cell, y, 0), 0f))
                            lane++;
                        continue;
                    }

                    int ni = alongX ? i + 1 : i, nj = alongX ? j : j + 1;
                    bool pair = Parked(plan, ni, nj) && !taken[ni, nj] &&
                                !Parked(plan, ni + into.x, nj + into.y);

                    if (pair)
                    {
                        taken[i, j] = taken[ni, nj] = true;
                        // the tile hangs off its own +X/+Z corner like every other piece
                        // here, so the pivot goes to the far corner of the PAIR
                        var at = alongX
                            ? new Vector3(plan.X0 + (i + 2) * Cell, y, plan.Z0 + (j + 1) * Cell)
                            : new Vector3(plan.X0 + (i + 1) * Cell, y, plan.Z0 + j * Cell);
                        float turn = alongX ? 0f : 90f;
                        if (!stand(Bay, CityEnv, at, turn)) continue;
                        pairs++;

                        // where the paint went: the tile lies back from its pivot, so its
                        // bays are stepped off in ITS frame and turned with it
                        var facing = Quaternion.Euler(0f, turn, 0f);
                        for (int bay = 0; bay < BaysPerTile; bay++)
                            plan.Stalls.Add(at + facing * new Vector3(
                                -2f * Cell * (bay + 0.5f) / BaysPerTile, 0f, -Cell * 0.5f));
                        continue;
                    }

                    taken[i, j] = true;
                    if (!stand(Road, CityEnv, Pivot(plan.X0 + i * Cell, plan.Z0 + j * Cell, y, 0), 0f))
                        continue;
                    singles++;
                    plan.Stalls.Add(new Vector3(plan.X0 + (i + 0.5f) * Cell, y,
                                                plan.Z0 + (j + 0.5f) * Cell));
                }

            return $"{pairs} tile(s) of bays" +
                   (singles > 0 ? $" and {singles} of plain asphalt" : "") +
                   (lane > 0 ? $", {lane} cell(s) of lane in front of them" : "");
        }

        /// <summary>Is this cell part of the car park - on the block, given over to it, and
        /// not taken by a way out crossing it?</summary>
        static bool Parked(Plan plan, int i, int j) =>
            plan.In(i, j) && plan.Park[i, j] && !plan.Drive[i, j] && !plan.Ramp[i, j];

        /// <summary>
        /// The street furniture, in the two lanes it stands in and to the rhythm it is
        /// spread at - both measured, both in the class note above.
        ///
        /// The lamps and the bollards go by the RUN, a straight length of kerb facing one
        /// way, because a rhythm is a thing you measure ALONG something. The rest is dealt
        /// out over whatever kerb is still clear, shuffled by the seed so a block is the
        /// same block every time it is built and two blocks are not the same block.
        /// </summary>
        static string Furnish(Plan plan, List<Laid> tiles, List<Laid> islands, float y, int seed,
                              Stands stand)
        {
            var kerbs = new List<Laid>();
            foreach (var tile in tiles)
                if (tile.Piece == Kerb || tile.Piece == Drain) kerbs.Add(tile);
            if (kerbs.Count == 0) return "";

            kerbs.Sort((a, b) =>
            {
                int by = a.Yaw.CompareTo(b.Yaw);
                if (by == 0) by = Astride(a).CompareTo(Astride(b));
                if (by == 0) by = Along(a).CompareTo(Along(b));
                return by;
            });

            var dice = new System.Random(seed);
            var taken = new HashSet<Vector2Int>();
            int lamps = 0, rows = 0;

            for (int start = 0; start < kerbs.Count;)
            {
                int end = start + 1;
                while (end < kerbs.Count && kerbs[end].Yaw == kerbs[start].Yaw &&
                       Astride(kerbs[end]) == Astride(kerbs[start]) &&
                       Along(kerbs[end]) == Along(kerbs[end - 1]) + 1) end++;
                int run = end - start;

                // a lamp on the seam between two tiles, which is where two thirds of theirs
                // stand, and the first one a cell in from the corner
                if (run > LampInset + 1)
                    for (int k = LampInset; k < run; k += LampEvery)
                    {
                        var at = kerbs[start + k];
                        if (!stand(Lamp, PropsDir, Where(plan, at, LampLane, 0f, y), at.Yaw)) continue;
                        taken.Add(new Vector2Int(at.I, at.J));
                        lamps++;
                    }

                // bollards guard a corner: one row at the end of every run long enough to
                // have a middle, which comes to two rows a block - theirs come to 1.9. And
                // A CELL from the corner, never on it: 19 of their 30 stand exactly there
                // a cell in, or the next one after that: on a run of six or ten the end
                // cell is exactly where a lamp already stands, and giving up there would
                // leave those runs with no bollards at all. Two runs in three get them -
                // their 30 rows over these same blocks come to two thirds of the runs long
                // enough to have a middle
                if (run >= 4 && dice.Next(3) != 0)
                    for (int back = 1; back <= 2; back++)
                    {
                        var at = kerbs[end - back];
                        if (!taken.Add(new Vector2Int(at.I, at.J))) continue;
                        if (stand(Bollards, PropsDir, Where(plan, at, KerbLane, Cell * 0.5f, y), at.Yaw))
                            rows++;
                        break;
                    }
                start = end;
            }

            var free = new List<Laid>();
            foreach (var tile in kerbs)
                if (!taken.Contains(new Vector2Int(tile.I, tile.J))) free.Add(tile);
            for (int i = free.Count - 1; i > 0; i--)
            {
                int j = dice.Next(i + 1);
                var swap = free[i];
                free[i] = free[j];
                free[j] = swap;
            }
            var spent = new bool[free.Count];

            var tally = new List<string>();
            if (lamps > 0) tally.Add($"{lamps} lamp(s)");
            if (rows > 0) tally.Add($"{rows} row(s) of bollards");

            // the palms, before the small furniture, so a bin never lands where a tree is
            int palms = 0, want = Mathf.RoundToInt(kerbs.Count / (float)PalmEvery);
            for (int i = 0; i < free.Count && palms < want; i++)
            {
                if (spent[i]) continue;
                var at = free[i];
                if (!Sapling(Middle(plan, at, y), at.Yaw, dice, stand)) break;
                spent[i] = true;
                palms++;
            }

            // and one on every ISLAND: the strip of pavement left standing between two
            // driveways is exactly where a town puts a tree, and being all corner tiles it
            // carries none of the kerbside furniture (2026-08-26, "dodaj palme na ostrvo")
            foreach (var island in islands)
                if (Sapling(Middle(plan, island, y), island.Yaw, dice, stand)) palms++;

            if (palms > 0) tally.Add($"{palms} palm(s)");

            foreach (var kind in Kerbside)
            {
                // the rate is kept as a FRACTION rather than rounded: a bench falls due
                // once per 85 kerb tiles and no block of theirs is that long, so rounding
                // would mean no block ever had one. Six of their sixteen do
                float due = kerbs.Count / (float)kind.Every;
                int wanted = Mathf.FloorToInt(due), stood = 0;
                if (dice.NextDouble() < due - wanted) wanted++;
                for (int i = 0; i < free.Count && stood < wanted; i++)
                {
                    if (spent[i]) continue;
                    var at = free[i];
                    // a bench with nothing at its back is a bench in the middle of a street
                    if (kind.Backed)
                    {
                        var back = Behind(at.Yaw);
                        if (!plan.In(at.I + back.x, at.J + back.y) ||
                            !plan.Built[at.I + back.x, at.J + back.y]) continue;
                    }
                    float along = 1f + (float)dice.NextDouble() * (Cell - 2f);
                    float turn = at.Yaw + (kind.Turns ? 90 * dice.Next(4) : 0);
                    if (!stand(kind.Piece, PropsDir, Where(plan, at, kind.Across, along, y), turn)) break;
                    spent[i] = true;
                    stood++;
                }
                if (stood > 0) tally.Add($"{stood} {kind.Piece.Replace("SM_Prop_", "")}");
            }
            return string.Join(", ", tally);
        }

        /// <summary>Which way a run of kerb lies: across its own facing.</summary>
        static int Astride(Laid tile) => tile.Yaw == 0 || tile.Yaw == 180 ? tile.J : tile.I;

        static int Along(Laid tile) => tile.Yaw == 0 || tile.Yaw == 180 ? tile.I : tile.J;

        /// <summary>The cell at a kerb tile's back - where the building is.</summary>
        static Vector2Int Behind(int yaw)
        {
            switch (yaw)
            {
                case 0: return new Vector2Int(0, -1);
                case 90: return new Vector2Int(-1, 0);
                case 180: return new Vector2Int(0, 1);
                default: return new Vector2Int(1, 0);
            }
        }

        /// <summary>A place on a kerb tile, given in the kerb's OWN frame: metres in from
        /// the kerb line, and metres along it. Which is how their furniture was measured,
        /// so the figures in the table can be read straight off.</summary>
        static Vector3 Where(Plan plan, Laid tile, float across, float along, float y)
        {
            float mx = plan.X0 + tile.I * Cell, mz = plan.Z0 + tile.J * Cell;
            switch (tile.Yaw)
            {
                case 0: return new Vector3(mx + along, y, mz + Cell - across);
                case 90: return new Vector3(mx + Cell - across, y, mz + Cell - along);
                case 180: return new Vector3(mx + Cell - along, y, mz + across);
                default: return new Vector3(mx + across, y, mz + along);
            }
        }

        /// <summary>The pack's pivot: the +X/+Z corner of the cell at yaw 0, and whichever
        /// corner lands there once it is turned. Same arithmetic as RoadDemoBuilder.PlaceTile,
        /// where it is spelled out.</summary>
        static Vector3 Pivot(float mx, float mz, float y, int yaw)
        {
            switch (yaw)
            {
                case 0: return new Vector3(mx + Cell, y, mz + Cell);
                case 90: return new Vector3(mx + Cell, y, mz);
                case 180: return new Vector3(mx, y, mz);
                default: return new Vector3(mx, y, mz + Cell);
            }
        }

        static float Overlap(float a0, float a1, float b0, float b1) =>
            Mathf.Max(0f, Mathf.Min(a1, b1) - Mathf.Max(a0, b0));

        static bool[,] Grown(bool[,] held, int nx, int nz)
        {
            var wider = new bool[nx, nz];
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                {
                    if (!held[i, j]) continue;
                    for (int di = -1; di <= 1; di++)
                        for (int dj = -1; dj <= 1; dj++)
                        {
                            int a = i + di, b = j + dj;
                            if (a >= 0 && b >= 0 && a < nx && b < nz) wider[a, b] = true;
                        }
                }
            return wider;
        }

        /// <summary>Whatever the block encloses belongs to it: a yard between two wings is
        /// ground the block owns, not a hole to see the city through. Flooding the empty
        /// ground in from the border is what tells those two apart.</summary>
        static void Enclose(bool[,] held, int nx, int nz)
        {
            var outside = new bool[nx, nz];
            var edge = new Stack<Vector2Int>();

            void Open(int i, int j)
            {
                if (i < 0 || j < 0 || i >= nx || j >= nz) return;
                if (held[i, j] || outside[i, j]) return;
                outside[i, j] = true;
                edge.Push(new Vector2Int(i, j));
            }

            for (int i = 0; i < nx; i++) { Open(i, 0); Open(i, nz - 1); }
            for (int j = 0; j < nz; j++) { Open(0, j); Open(nx - 1, j); }
            while (edge.Count > 0)
            {
                var c = edge.Pop();
                Open(c.x + 1, c.y);
                Open(c.x - 1, c.y);
                Open(c.x, c.y + 1);
                Open(c.x, c.y - 1);
            }

            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                    if (!outside[i, j]) held[i, j] = true;
        }

        /// <summary>
        /// Squares the block off: its own bounding rectangle, less at most
        /// <see cref="MostNotches"/> bites - see there for why.
        ///
        /// A bite is looked for flush against a SIDE, because that is the only kind a
        /// rectangle can have: for each of the four sides, the largest rectangle of missing
        /// ground standing on it, and the biggest of those four is the notch. Then again
        /// for the second one, with the first taken out of play so the two cannot overlap.
        /// Whatever ground is still missing after that is not a notch, it is raggedness,
        /// and it fills in.
        /// </summary>
        static void Square(bool[,] held, int nx, int nz)
        {
            int i0 = nx, j0 = nz, i1 = -1, j1 = -1;
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                {
                    if (!held[i, j]) continue;
                    if (i < i0) i0 = i;
                    if (j < j0) j0 = j;
                    if (i > i1) i1 = i;
                    if (j > j1) j1 = j;
                }
            if (i1 < 0) return;

            var missing = new bool[nx, nz];
            for (int i = i0; i <= i1; i++)
                for (int j = j0; j <= j1; j++)
                    missing[i, j] = !held[i, j];

            for (int notch = 0; notch < MostNotches; notch++)
            {
                if (!Bite(missing, i0, i1, j0, j1, out int ai, out int bi, out int aj, out int bj))
                    break;
                // kept: taken out of play, so the next bite cannot overlap it and the fill
                // below leaves it alone
                for (int i = ai; i <= bi; i++)
                    for (int j = aj; j <= bj; j++)
                        missing[i, j] = false;
                for (int i = ai; i <= bi; i++)
                    for (int j = aj; j <= bj; j++)
                        held[i, j] = false;
            }

            for (int i = i0; i <= i1; i++)
                for (int j = j0; j <= j1; j++)
                    if (missing[i, j]) held[i, j] = true;
        }

        /// <summary>The biggest rectangle of missing ground standing flush on one of the
        /// four sides of the block's box. Returns it in cells, both ends inclusive.</summary>
        static bool Bite(bool[,] missing, int i0, int i1, int j0, int j1,
                         out int ai, out int bi, out int aj, out int bj)
        {
            ai = bi = aj = bj = 0;
            int best = 0;
            int wide = i1 - i0 + 1, tall = j1 - j0 + 1;
            var depth = new int[Mathf.Max(wide, tall)];

            // north and south: a column of missing cells hanging off the top or the bottom
            for (int side = 0; side < 2; side++)
            {
                for (int i = i0; i <= i1; i++)
                {
                    int deep = 0;
                    for (int k = 0; k < tall; k++)
                    {
                        int j = side == 0 ? j1 - k : j0 + k;
                        if (!missing[i, j]) break;
                        deep++;
                    }
                    depth[i - i0] = deep;
                }
                if (!Widest(depth, wide, out int a, out int b, out int deepest)) continue;
                int area = (b - a + 1) * deepest;
                if (area <= best) continue;
                best = area;
                ai = i0 + a;
                bi = i0 + b;
                aj = side == 0 ? j1 - deepest + 1 : j0;
                bj = side == 0 ? j1 : j0 + deepest - 1;
            }

            // east and west: a row of missing cells running in from one end
            for (int side = 0; side < 2; side++)
            {
                for (int j = j0; j <= j1; j++)
                {
                    int deep = 0;
                    for (int k = 0; k < wide; k++)
                    {
                        int i = side == 0 ? i1 - k : i0 + k;
                        if (!missing[i, j]) break;
                        deep++;
                    }
                    depth[j - j0] = deep;
                }
                if (!Widest(depth, tall, out int a, out int b, out int deepest)) continue;
                int area = (b - a + 1) * deepest;
                if (area <= best) continue;
                best = area;
                aj = j0 + a;
                bj = j0 + b;
                ai = side == 0 ? i1 - deepest + 1 : i0;
                bi = side == 0 ? i1 : i0 + deepest - 1;
            }

            return best > 0;
        }

        /// <summary>The widest span of a depth reading that is worth calling a notch, by
        /// area. Both ends inclusive; false when nothing reaches
        /// <see cref="SmallestNotch"/> either way.</summary>
        static bool Widest(int[] depth, int count, out int a, out int b, out int deepest)
        {
            a = b = deepest = 0;
            int best = 0;
            for (int start = 0; start < count; start++)
            {
                int floor = int.MaxValue;
                for (int end = start; end < count; end++)
                {
                    floor = Mathf.Min(floor, depth[end]);
                    if (floor < SmallestNotch) break;
                    int span = end - start + 1;
                    if (span < SmallestNotch) continue;
                    int area = span * floor;
                    if (area <= best) continue;
                    best = area;
                    a = start;
                    b = end;
                    deepest = floor;
                }
            }
            return best > 0;
        }

        /// <summary>Fills the one-cell dents, so the kerb runs straight past a bay window
        /// instead of stepping out and back for it. A dent is a cell with three of its four
        /// sides already inside the block.</summary>
        static void Straighten(bool[,] held, int nx, int nz)
        {
            for (int pass = 0; pass < Passes; pass++)
            {
                bool moved = false;
                for (int i = 0; i < nx; i++)
                    for (int j = 0; j < nz; j++)
                    {
                        if (held[i, j]) continue;
                        int sides = 0;
                        if (j + 1 < nz && held[i, j + 1]) sides++;
                        if (j > 0 && held[i, j - 1]) sides++;
                        if (i + 1 < nx && held[i + 1, j]) sides++;
                        if (i > 0 && held[i - 1, j]) sides++;
                        if (sides < 3) continue;
                        held[i, j] = true;
                        moved = true;
                    }
                if (!moved) return;
            }
        }

        readonly struct Laid
        {
            public readonly int I, J, Yaw;
            public readonly string Piece;

            public Laid(int i, int j, string piece, int yaw)
            {
                I = i;
                J = j;
                Piece = piece;
                Yaw = yaw;
            }
        }
    }
}
