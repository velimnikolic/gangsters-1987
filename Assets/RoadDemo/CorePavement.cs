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
        public const float Cell = 5f;
        const string CityEnv = "Assets/Synty/PolygonCity/Prefabs/Environments/";

        const string Kerb = "SM_Env_Sidewalk_Straight_01";
        const string Flat = "SM_Env_Sidewalk_01";
        const string DippedCorner = "SM_Env_Sidewalk_Dip_Corner_01";
        const string PlainCorner = "SM_Env_Sidewalk_Corner_01";
        const string InnerCorner = "SM_Env_Sidewalk_Corner_02";
        const string Drain = "SM_Env_Sidewalk_Gutter_01";

        const string PropsDir = "Assets/Synty/PolygonCity/Prefabs/Props/";

        /// <summary>The street lamp: mast and cantilever arm in one piece, 6.5 m tall, the
        /// arm reaching 2.5 m so that standing a metre in from the kerb line it hangs over
        /// the carriageway. Which is also why it faces OUT.</summary>
        const string Lamp = "SM_Prop_LightPole_Base_01";

        /// <summary>A row of bollards, 3.2 m of them - one piece, laid along the kerb.</summary>
        const string Bollards = "SM_Prop_SidewalkPoles_01";

        /// <summary>Cells between lamps along one run of kerb, and how far in from its end
        /// the first one stands. Twenty metres is their commonest gap by a wide margin,
        /// and a lamp is a cell in from the corner, never on it.</summary>
        const int LampEvery = 4, LampInset = 1;

        /// <summary>The two lanes the furniture stands in, metres in from the kerb line.
        /// Nothing of theirs stands between them.</summary>
        const float LampLane = 1f, KerbLane = 1.5f, WallLane = 4f;

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

        /// <summary>A raster bigger than this is not a block - it is a rectangle laid over
        /// half the city, and paving it would stand tens of thousands of tiles.</summary>
        const int MostCells = 40000;

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

            public int Cells, BuiltCells;

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
        /// The block the buildings make: their footprint on the 5 m raster, grown by
        /// <paramref name="band"/> tiles of pavement - one, unless the caller knows better,
        /// because one is what the demo does.
        ///
        /// Grown on the DIAGONAL as well as the square, so a rectangular building comes out
        /// with square corners; growing on the square alone cuts every corner off and the
        /// kerb arrives as an octagon. Then two tidyings, both of which the artists' own
        /// outlines pass and a raw grown outline does not: any cell the block encloses
        /// joins it (a yard between two wings is paved, not a hole to see the city
        /// through), and any dent with three of its four sides already in joins it too, so
        /// the kerb runs straight instead of stepping in and out round every bay window.
        /// </summary>
        public static Plan Around(IEnumerable<Bounds> buildings, int band = 1)
        {
            var boxes = new List<Bounds>();
            if (buildings != null)
                foreach (var box in buildings)
                    if (box.size.x > 0f || box.size.z > 0f) boxes.Add(box);

            var plan = new Plan { Ground = new bool[0, 0], Built = new bool[0, 0] };
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
                return new Plan { Ground = new bool[0, 0], Built = new bool[0, 0] };
            }

            plan.Built = new bool[plan.NX, plan.NZ];
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
                        if (plan.Built[i, j]) continue;
                        float x = plan.X0 + i * Cell, z = plan.Z0 + j * Cell;
                        float ox = Overlap(box.min.x, box.max.x, x, x + Cell);
                        float oz = Overlap(box.min.z, box.max.z, z, z + Cell);
                        if (ox * oz >= takes) plan.Built[i, j] = true;
                    }
            }

            var ground = (bool[,])plan.Built.Clone();
            for (int step = 0; step < band; step++) ground = Grown(ground, plan.NX, plan.NZ);
            Enclose(ground, plan.NX, plan.NZ);
            Straighten(ground, plan.NX, plan.NZ);
            plan.Ground = ground;

            for (int i = 0; i < plan.NX; i++)
                for (int j = 0; j < plan.NZ; j++)
                {
                    if (ground[i, j]) plan.Cells++;
                    if (plan.Built[i, j]) plan.BuiltCells++;
                }
            return plan;
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
            int kerbs = 0, corners = 0, notches = 0, floor = 0, odd = 0;

            for (int i = 0; i < plan.NX; i++)
                for (int j = 0; j < plan.NZ; j++)
                {
                    if (!plan.Ground[i, j]) continue;
                    bool north = plan.In(i, j + 1), east = plan.In(i + 1, j);
                    bool south = plan.In(i, j - 1), west = plan.In(i - 1, j);
                    int open = (north ? 0 : 1) + (east ? 0 : 1) + (south ? 0 : 1) + (west ? 0 : 1);

                    if (open == 0)
                    {
                        // walled in on all four sides: either the middle of the block or the
                        // inside of a notch, and the diagonal is what tells those apart
                        if (!plan.In(i + 1, j + 1)) { pieces.Add(new Laid(i, j, InnerCorner, Quadrant(true, true))); notches++; }
                        else if (!plan.In(i + 1, j - 1)) { pieces.Add(new Laid(i, j, InnerCorner, Quadrant(false, true))); notches++; }
                        else if (!plan.In(i - 1, j - 1)) { pieces.Add(new Laid(i, j, InnerCorner, Quadrant(false, false))); notches++; }
                        else if (!plan.In(i - 1, j + 1)) { pieces.Add(new Laid(i, j, InnerCorner, Quadrant(true, false))); notches++; }
                        else if (under || !plan.Built[i, j]) { pieces.Add(new Laid(i, j, Flat, 0)); floor++; }
                        continue;
                    }

                    if (open == 2 && north != south && east != west)
                    {
                        int yaw = Quadrant(!north, !east);
                        pieces.Add(new Laid(i, j, ramps ? DippedCorner : PlainCorner,
                                            ramps ? (yaw + 90) % 360 : yaw));
                        corners++;
                        continue;
                    }

                    // one open side is a plain run of kerb; anything else is a cell the
                    // block is only one tile thick at, which cannot happen while the band
                    // is a tile or more. Kerb it towards the open side and say so.
                    if (open != 1) odd++;
                    pieces.Add(new Laid(i, j, Kerb, !north ? 0 : !east ? 90 : !south ? 180 : 270));
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

            var kit = new Dictionary<string, GameObject>();
            int laid = 0;

            bool Stand(string piece, string dir, Vector3 at, float turn)
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
                laid++;
                return true;
            }

            foreach (var piece in pieces)
                Stand(piece.Piece, CityEnv,
                      Pivot(plan.X0 + piece.I * Cell, plan.Z0 + piece.J * Cell, y, piece.Yaw),
                      piece.Yaw);
            int tiles = laid;

            string furniture = props ? Furnish(plan, pieces, y, seed, Stand) : "";

            var area = plan.Area;
            said = $"{area.width:F0} x {area.height:F0} m, {tiles} tile(s): {kerbs - drains} kerb, " +
                   $"{corners} corner(s), {notches} inner corner(s), {drains} drain(s), " +
                   $"{floor} square(s); the buildings cover {plan.BuiltCells} of {plan.Cells} cell(s)" +
                   (furniture.Length > 0 ? "; on the pavement " + furniture : "") +
                   (odd > 0
                       ? $"; {odd} cell(s) carry kerb on more than one side - the block is a tile " +
                         "thick there, which no pavement of theirs is"
                       : "");
            return laid;
        }

        /// <summary>How a piece is stood, whichever way the caller stands things.</summary>
        delegate bool Stands(string piece, string dir, Vector3 at, float turn);

        /// <summary>
        /// The street furniture, in the two lanes it stands in and to the rhythm it is
        /// spread at - both measured, both in the class note above.
        ///
        /// The lamps and the bollards go by the RUN, a straight length of kerb facing one
        /// way, because a rhythm is a thing you measure ALONG something. The rest is dealt
        /// out over whatever kerb is still clear, shuffled by the seed so a block is the
        /// same block every time it is built and two blocks are not the same block.
        /// </summary>
        static string Furnish(Plan plan, List<Laid> tiles, float y, int seed, Stands stand)
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

        /// <summary>A corner tile wraps one corner of its cell, and which corner IS the
        /// yaw: NE 0, SE 90, SW 180, NW 270. The plain corner and the inner corner both
        /// read straight off this; the dipped corner is a quarter further round.</summary>
        static int Quadrant(bool north, bool east) => north ? (east ? 0 : 270) : (east ? 90 : 180);

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
