using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// The city core's roads, read off the gaps between its blocks.
    ///
    /// The blocks bring their own pavements out of the demo, so a road is whatever lies
    /// between two kerbs, and its width says what it is: one cell (5 m) is a one-way lane,
    /// two a narrow two-lane road, three the city's street (two lanes and a parking strip
    /// each side), four a street with a strip of asphalt along its far kerb, seven the
    /// boulevard (2+2 lanes and the median), eight a boulevard with a strip. Where two
    /// roads meet the gap is wider both ways and is a junction: bare asphalt, a zebra at
    /// every mouth. A block's own bay - the cells of its box its kerb runs round without
    /// anything standing on them - is the car park the demo had there.
    ///
    /// Everything is worked on a 5 m raster of the core's ground; the raster is kept
    /// (<see cref="Raster.Map"/>) so a probe can read the drawing back without a picture,
    /// and every gap the roads could not read is reported (<see cref="Raster.Report"/>) -
    /// that list is what the cuts in <see cref="CoreLayout"/> are tuned against.
    ///
    /// The same code draws the core in the editor and builds it in the game; only the
    /// way a prefab is stood differs, and that is the one delegate passed in.
    /// </summary>
    public static class CoreRoads
    {
        public const float Cell = 5f;
        const string CityEnv = "Assets/Synty/PolygonCity/Prefabs/Environments/";

        // the kit, the pieces RoadDemoBuilder.LoadPrefabs takes, for the same jobs
        const string RoadHalfTile = "SM_Env_Road_YellowLines_02";  // half a two-way street
        const string LaneEdge = "SM_Env_Road_02";                  // boulevard's kerb lane
        const string LaneDash = "SM_Env_Road_Lines_01";            // its inner, dashed lane
        const string MedianTile = "SM_Env_Road_Median_01";
        const string Bare = "SM_Env_Road_Bare_01";                 // asphalt: strips, junctions, lanes
        const string Crossing = "SM_Env_Road_Crossing_01";
        const string Arrow = "SM_Env_Road_Arrow_01";
        const string Divider = "SM_Env_Street_Divider_01";
        /// <summary>Three painted bays in a row, 10 x 5 m, pivot on its +X/+Z corner like
        /// every other piece of the kit.</summary>
        const string ParkingRow = "SM_Env_Road_ParkingLines_01";
        /// <summary>The kit's pavement tile with the kerb along one edge - stretched to a
        /// metre, it is the kerb that closes a car park off from the street.</summary>
        const string KerbTile = "SM_Env_Sidewalk_Straight_01";
        const float KerbDepth = 1f;

        // the widths, the builder's own
        static float StreetHalf => RoadDemoBuilder.RoadHalf(false);
        static float BlvdHalf => RoadDemoBuilder.RoadHalf(true);
        static float ParkLane => StreetHalf - Cell;
        static float MedianHalf => BlvdHalf - ParkLane - 2f * Cell;
        static int StreetCells => Mathf.RoundToInt(2f * StreetHalf / Cell);   // 3
        static int BlvdCells => Mathf.RoundToInt(2f * BlvdHalf / Cell);       // 7

        public enum Kind : byte
        {
            Outside, Bare, Yard,
            LaneEW, LaneNS,           // one way, 5 m
            NarrowEW, NarrowNS,       // two lanes, 10 m, no strips
            StreetEW, StreetNS,       // the city's street, 15 m
            BlvdEW, BlvdNS,           // the boulevard, 35 m
            Wide,                     // a strip of asphalt along a road a cell wider than its profile
            Parking, Block,
        }

        public sealed class Raster
        {
            public Kind[,] Kinds;
            public sbyte[,] Dir;              // lanes: +1 north/east, -1 south/west
            public float X0, Z0;              // the south-west corner of cell (0, 0)
            public int NX, NZ;
            public int Clashes;               // cells two blocks both claim
            public string Map;                // north up, one character per cell
            public string Report;             // the gaps that are not roads, and other oddities
            public int BlockArea, RoadArea, ParkingArea;
            public float X(int i) => X0 + i * Cell;
            public float Z(int j) => Z0 + j * Cell;
            public Kind At(int i, int j) => i < 0 || j < 0 || i >= NX || j >= NZ ? Kind.Outside : Kinds[i, j];
        }

        /// <summary>
        /// Reads the roads off the blocks as they stand: a ring a street wide round every
        /// block, everything the ring encloses, each cell read for what it is.
        /// </summary>
        public static Raster Build(IReadOnlyList<CoreLayout.Block> blocks)
        {
            int s = StreetCells;
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            foreach (var block in blocks)
            {
                var box = block.Box;
                minX = Mathf.Min(minX, box.xMin - s * Cell); maxX = Mathf.Max(maxX, box.xMax + s * Cell);
                minZ = Mathf.Min(minZ, box.yMin - s * Cell); maxZ = Mathf.Max(maxZ, box.yMax + s * Cell);
            }
            var r = new Raster
            {
                X0 = Mathf.Floor(minX / Cell) * Cell,
                Z0 = Mathf.Floor(minZ / Cell) * Cell,
            };
            r.NX = Mathf.CeilToInt((maxX - r.X0) / Cell);
            r.NZ = Mathf.CeilToInt((maxZ - r.Z0) / Cell);
            int w = r.NX, h = r.NZ;
            var kinds = new Kind[w, h];
            var owner = new int[w, h];
            for (int i = 0; i < w; i++)
                for (int j = 0; j < h; j++)
                    owner[i, j] = -1;

            // the blocks by their shapes, their bays as yards
            for (int k = 0; k < blocks.Count; k++)
            {
                var block = blocks[k];
                var box = block.Box;
                int i0 = Mathf.RoundToInt((box.xMin - r.X0) / Cell), j0 = Mathf.RoundToInt((box.yMin - r.Z0) / Cell);
                for (int i = 0; i < block.CW; i++)
                    for (int j = 0; j < block.CD; j++)
                    {
                        int a = i0 + i, b = j0 + j;
                        if (a < 0 || b < 0 || a >= w || b >= h) continue;
                        if (block.Mask[i, j])
                        {
                            if (kinds[a, b] == Kind.Block) r.Clashes++;
                            kinds[a, b] = Kind.Block;
                            owner[a, b] = k;
                        }
                        else if (kinds[a, b] == Kind.Outside)
                        {
                            kinds[a, b] = Kind.Yard;
                            owner[a, b] = k;
                        }
                    }
            }

            // the ring round every block, and whatever the rings enclose
            var ring = new bool[w, h];
            for (int i = 0; i < w; i++)
                for (int j = 0; j < h; j++)
                {
                    if (kinds[i, j] != Kind.Block) continue;
                    for (int a = Mathf.Max(0, i - s); a <= Mathf.Min(w - 1, i + s); a++)
                        for (int b = Mathf.Max(0, j - s); b <= Mathf.Min(h - 1, j + s); b++)
                            ring[a, b] = true;
                }
            for (int i = 0; i < w; i++)
                for (int j = 0; j < h; j++)
                    if (kinds[i, j] == Kind.Outside && ring[i, j]) kinds[i, j] = Kind.Bare;
            // the main road runs the whole width of the core, boulevard-wide, to the edge
            var band = CoreLayout.MainRoad;
            var inBand = new bool[w, h];
            for (int i = 0; i < w; i++)
                for (int j = 0; j < h; j++)
                {
                    float cz = r.Z(j) + Cell * 0.5f;
                    if (cz <= band.x || cz >= band.y) continue;
                    inBand[i, j] = true;
                    if (kinds[i, j] == Kind.Outside) kinds[i, j] = Kind.Bare;
                }
            // and whatever lies between two blocks no farther apart than a boulevard: the
            // middle of a 35 m gap is farther than a street from either kerb, and is road
            int reach = BlvdCells;
            for (int i = 0; i < w; i++)
                for (int j = 0; j < h; j++)
                {
                    if (kinds[i, j] != Kind.Outside) continue;
                    if ((Near(kinds, w, h, i, j, -1, 0, reach) && Near(kinds, w, h, i, j, 1, 0, reach)) ||
                        (Near(kinds, w, h, i, j, 0, -1, reach) && Near(kinds, w, h, i, j, 0, 1, reach)))
                        kinds[i, j] = Kind.Bare;
                }
            var outside = Reach(kinds, w, h, Kind.Outside, true);
            for (int i = 0; i < w; i++)
                for (int j = 0; j < h; j++)
                    if (kinds[i, j] == Kind.Outside && !outside[i, j]) kinds[i, j] = Kind.Bare;

            // a block's bay: the demo's streets run through three of them (a street's
            // width across, many times that long), so a bay that is a CORRIDOR - narrow,
            // long, and not walled by its own block - is road; every other bay is the car
            // park the demo had there
            var read = (Kind[,])kinds.Clone();
            var bays = new bool[w, h];        // road cells that are a block's bay
            for (int i = 0; i < w; i++)
                for (int j = 0; j < h; j++)
                {
                    if (kinds[i, j] != Kind.Yard) continue;
                    int runH = Run(kinds, w, h, i, j, 1, 0, IsAsphalt), runV = Run(kinds, w, h, i, j, 0, 1, IsAsphalt);
                    int across = Mathf.Min(runH, runV), along = Mathf.Max(runH, runV);
                    bool walled = runH <= runV ? Walled(kinds, owner, w, h, i, j, 1, 0) : Walled(kinds, owner, w, h, i, j, 0, 1);
                    bool corridor = across <= StreetCells + 1 && along >= 3 * across && !walled;
                    read[i, j] = corridor ? Kind.Bare : Kind.Parking;
                    bays[i, j] = corridor;
                }
            kinds = read;
            read = (Kind[,])kinds.Clone();

            // the lanes first: a cell of asphalt with a kerb on either side is a one-way
            // lane, and once it is known to be one it is no longer part of the road it
            // opens onto, whose profile then runs past its mouth unbroken
            for (int i = 0; i < w; i++)
                for (int j = 0; j < h; j++)
                {
                    if (kinds[i, j] != Kind.Bare) continue;
                    int runH = Run(kinds, w, h, i, j, 1, 0, IsRoadway), runV = Run(kinds, w, h, i, j, 0, 1, IsRoadway);
                    if (runH == 1 && runV >= StreetCells && Kerbed(kinds, w, h, i, j, 1, 0)) read[i, j] = Kind.LaneNS;
                    else if (runV == 1 && runH >= StreetCells && Kerbed(kinds, w, h, i, j, 0, 1)) read[i, j] = Kind.LaneEW;
                }
            kinds = read;
            read = (Kind[,])kinds.Clone();

            // what each remaining cell of asphalt is, off the runs of asphalt through it
            for (int i = 0; i < w; i++)
                for (int j = 0; j < h; j++)
                {
                    if (kinds[i, j] != Kind.Bare) continue;
                    if (inBand[i, j])
                    {
                        // the main road is the boulevard from one edge of the core to the
                        // other; a cell of it is a junction only where a road comes in
                        // across the band's kerb line on either side
                        int south = Mathf.RoundToInt((band.x - r.Z0) / Cell) - 1;
                        int north = Mathf.RoundToInt((band.y - r.Z0) / Cell);
                        Kind KindAt(int a, int b) => a < 0 || b < 0 || a >= w || b >= h ? Kind.Outside : kinds[a, b];
                        bool crossing = IsRoadway(KindAt(i, south)) || IsRoadway(KindAt(i, north));
                        int back = j - Mathf.RoundToInt((band.x - r.Z0) / Cell);
                        read[i, j] = crossing ? Kind.Bare : (back < BlvdCells ? Kind.BlvdEW : Kind.Wide);
                        continue;
                    }
                    int runH = Run(kinds, w, h, i, j, 1, 0, IsRoadway), runV = Run(kinds, w, h, i, j, 0, 1, IsRoadway);
                    // the run ACROSS a road is its width: a vertical run reads an east-west road.
                    // A boulevard needs a kerb on both sides; a gap that wide against the open
                    // edge is the corner of the ring
                    if (runV >= BlvdCells && !Kerbed(kinds, w, h, i, j, 0, 1)) runV = 0;
                    if (runH >= BlvdCells && !Kerbed(kinds, w, h, i, j, 1, 0)) runH = 0;
                    Kind ew = Profile(runV, false), ns = Profile(runH, true);
                    bool roadEW = ew != Kind.Bare, roadNS = ns != Kind.Bare;
                    // the cell a road has over its profile: a block's bay is that block's
                    // kerb-side parking, its bays opening onto the street; plain ground a
                    // strip of asphalt
                    Kind over = bays[i, j] ? Kind.Parking : Kind.Wide;
                    if (roadEW && !roadNS)
                        read[i, j] = Back(kinds, w, h, i, j, 0, 1) < Base(runV) ? ew : over;
                    else if (roadNS && !roadEW)
                        read[i, j] = Back(kinds, w, h, i, j, 1, 0) < Base(runH) ? ns : over;
                    else read[i, j] = Kind.Bare;
                }

            // a car park cell caught between two lengths of the same street is the
            // street's - the profile runs through rather than breaking for one cell
            for (int i = 1; i < w - 1; i++)
                for (int j = 1; j < h - 1; j++)
                {
                    if (read[i, j] != Kind.Parking) continue;
                    if (read[i - 1, j] == Kind.StreetEW && read[i + 1, j] == Kind.StreetEW) read[i, j] = Kind.StreetEW;
                    else if (read[i, j - 1] == Kind.StreetNS && read[i, j + 1] == Kind.StreetNS) read[i, j] = Kind.StreetNS;
                }

            // the lanes' directions
            r.Dir = new sbyte[w, h];
            var unmatched = 0;
            var unmatchedAt = new List<string>();
            var shifted = new List<(Rect box, int dir)>();
            foreach (var lane in CoreLayout.Lanes)
            {
                var box = lane.Box;
                var shift = CoreLayout.Shift(box);
                shifted.Add((new Rect(box.x + shift.x, box.y + shift.y, box.width, box.height), lane.Direction));
            }
            for (int i = 0; i < w; i++)
                for (int j = 0; j < h; j++)
                {
                    if (read[i, j] != Kind.LaneEW && read[i, j] != Kind.LaneNS) continue;
                    float cx = r.X(i) + Cell * 0.5f, cz = r.Z(j) + Cell * 0.5f;
                    sbyte dir = 0;
                    foreach (var (box, d) in shifted)
                        if (box.Contains(new Vector2(cx, cz))) { dir = (sbyte)d; break; }
                    if (dir == 0) { dir = 1; unmatched++; unmatchedAt.Add($"({cx:F0}, {cz:F0})"); }
                    r.Dir[i, j] = dir;
                }

            r.Kinds = read;
            Count(r);
            r.Map = Draw(r);
            r.Report = Oddities(r, unmatched, unmatchedAt);
            return r;
        }

        /// <summary>What a gap of this many cells across is: a road of that width, or
        /// nothing a road profile fits (bare asphalt).</summary>
        static Kind Profile(int across, bool northSouth)
        {
            int s = StreetCells, b = BlvdCells;
            if (across == 1) return northSouth ? Kind.LaneNS : Kind.LaneEW;
            if (across == 2) return northSouth ? Kind.NarrowNS : Kind.NarrowEW;
            if (across == s || across == s + 1) return northSouth ? Kind.StreetNS : Kind.StreetEW;
            if (across == b || across == b + 1) return northSouth ? Kind.BlvdNS : Kind.BlvdEW;
            return Kind.Bare;
        }

        /// <summary>How many cells of a run of this width carry the profile; the rest is
        /// the strip.</summary>
        static int Base(int across)
        {
            int s = StreetCells, b = BlvdCells;
            if (across == s + 1) return s;
            if (across == b + 1) return b;
            return across;
        }

        static bool IsAsphalt(Kind k) => k == Kind.Bare || k == Kind.Yard;
        /// <summary>Asphalt a road profile is read off: bare cells only - a lane, a car park
        /// and a strip have been taken out by then.</summary>
        static bool IsRoadway(Kind k) => k == Kind.Bare;

        /// <summary>Is there a block cell within <paramref name="reach"/> of (i, j) along
        /// (di, dj), with nothing but open ground or asphalt on the way?</summary>
        static bool Near(Kind[,] kinds, int nx, int nz, int i, int j, int di, int dj, int reach)
        {
            for (int k = 1; k <= reach; k++)
            {
                int a = i + k * di, b = j + k * dj;
                if (a < 0 || b < 0 || a >= nx || b >= nz) return false;
                if (kinds[a, b] == Kind.Block) return true;
            }
            return false;
        }

        /// <summary>Do both ends of the run of asphalt through (i, j) along (di, dj) stop at
        /// a block cell?</summary>
        static bool Kerbed(Kind[,] kinds, int nx, int nz, int i, int j, int di, int dj)
        {
            bool End(int si, int sj)
            {
                int a = i, b = j;
                while (a + si >= 0 && b + sj >= 0 && a + si < nx && b + sj < nz && IsAsphalt(kinds[a + si, b + sj]))
                { a += si; b += sj; }
                a += si; b += sj;
                return a >= 0 && b >= 0 && a < nx && b < nz && (kinds[a, b] == Kind.Block || kinds[a, b] == Kind.Parking);
            }
            return End(di, dj) && End(-di, -dj);
        }

        /// <summary>How many asphalt cells run through (i, j) along (di, dj), the cell itself
        /// included - kerb to kerb.</summary>
        static int Run(Kind[,] kinds, int nx, int nz, int i, int j, int di, int dj, Func<Kind, bool> asphalt)
        {
            int n = 1;
            for (int a = i + di, b = j + dj; a >= 0 && b >= 0 && a < nx && b < nz && asphalt(kinds[a, b]); a += di, b += dj) n++;
            for (int a = i - di, b = j - dj; a >= 0 && b >= 0 && a < nx && b < nz && asphalt(kinds[a, b]); a -= di, b -= dj) n++;
            return n;
        }

        /// <summary>How many asphalt cells lie behind (i, j) against (di, dj) - where in its
        /// run the cell stands.</summary>
        static int Back(Kind[,] kinds, int nx, int nz, int i, int j, int di, int dj)
        {
            int n = 0;
            for (int a = i - di, b = j - dj; a >= 0 && b >= 0 && a < nx && b < nz && IsRoadway(kinds[a, b]); a -= di, b -= dj) n++;
            return n;
        }

        /// <summary>Are both ends of the run of asphalt through (i, j) along (di, dj) the same
        /// block's cells? Then the run is a bay of that block, not a road.</summary>
        static bool Walled(Kind[,] kinds, int[,] owner, int nx, int nz, int i, int j, int di, int dj)
        {
            int End(int si, int sj)
            {
                int a = i, b = j;
                while (a + si >= 0 && b + sj >= 0 && a + si < nx && b + sj < nz && IsAsphalt(kinds[a + si, b + sj]))
                { a += si; b += sj; }
                a += si; b += sj;
                if (a < 0 || b < 0 || a >= nx || b >= nz || kinds[a, b] != Kind.Block) return -1;
                return owner[a, b];
            }
            int one = End(di, dj), other = End(-di, -dj);
            return one >= 0 && one == other;
        }

        /// <summary>Cells of <paramref name="kind"/> reachable from the raster's border
        /// through cells of that kind.</summary>
        static bool[,] Reach(Kind[,] kinds, int w, int h, Kind kind, bool fromBorder)
        {
            var seen = new bool[w, h];
            var todo = new Queue<Vector2Int>();
            for (int i = 0; i < w; i++)
                for (int j = 0; j < h; j++)
                    if (kinds[i, j] == kind && (i == 0 || j == 0 || i == w - 1 || j == h - 1))
                    {
                        seen[i, j] = true;
                        todo.Enqueue(new Vector2Int(i, j));
                    }
            while (todo.Count > 0)
            {
                var c = todo.Dequeue();
                foreach (var n in new[] { new Vector2Int(c.x - 1, c.y), new Vector2Int(c.x + 1, c.y),
                                          new Vector2Int(c.x, c.y - 1), new Vector2Int(c.x, c.y + 1) })
                {
                    if (n.x < 0 || n.y < 0 || n.x >= w || n.y >= h) continue;
                    if (kinds[n.x, n.y] != kind || seen[n.x, n.y]) continue;
                    seen[n.x, n.y] = true;
                    todo.Enqueue(n);
                }
            }
            return seen;
        }

        public static bool IsRoad(Kind k) => k != Kind.Outside && k != Kind.Block;

        static void Count(Raster r)
        {
            int blocks = 0, road = 0, parking = 0;
            for (int i = 0; i < r.NX; i++)
                for (int j = 0; j < r.NZ; j++)
                {
                    var k = r.Kinds[i, j];
                    if (k == Kind.Block) blocks++;
                    else if (k == Kind.Parking) parking++;
                    else if (IsRoad(k)) road++;
                }
            int cell = Mathf.RoundToInt(Cell * Cell);
            r.BlockArea = blocks * cell;
            r.RoadArea = road * cell;
            r.ParkingArea = parking * cell;
        }

        static string Draw(Raster r)
        {
            var map = new StringBuilder();
            map.Append("      ");
            for (int i = 0; i < r.NX; i++)
                map.Append(i % 4 == 0 ? (((int)Mathf.Abs(r.X(i)) / 10) % 10).ToString() : " ");
            map.Append((char)10);
            for (int j = r.NZ - 1; j >= 0; j--)
            {
                map.Append(((int)r.Z(j)).ToString().PadLeft(5)).Append(' ');
                for (int i = 0; i < r.NX; i++)
                {
                    var k = r.Kinds[i, j];
                    map.Append(k switch
                    {
                        Kind.Block => '#', Kind.Bare => '.', Kind.Yard => 'y', Kind.Parking => 'P', Kind.Wide => ':',
                        Kind.StreetEW => '-', Kind.StreetNS => '|', Kind.BlvdEW => '=', Kind.BlvdNS => 'H',
                        Kind.NarrowEW => '~', Kind.NarrowNS => '!', Kind.LaneEW => (r.Dir[i, j] > 0 ? '>' : '<'),
                        Kind.LaneNS => (r.Dir[i, j] > 0 ? '^' : 'v'), _ => ' ',
                    });
                }
                map.Append((char)10);
            }
            return map.ToString();
        }

        /// <summary>The gaps the roads could not read - bare asphalt bigger than a junction
        /// box, narrow roads where the city wants streets, lanes with no direction on
        /// record - with where they are, so the cuts can be argued about.</summary>
        static string Oddities(Raster r, int unmatchedLanes, List<string> unmatchedAt)
        {
            var sb = new StringBuilder();
            var seen = new bool[r.NX, r.NZ];
            int biggestBox = Mathf.Max(StreetCells * BlvdCells, (StreetCells + 1) * (BlvdCells + 1));
            for (int i = 0; i < r.NX; i++)
                for (int j = 0; j < r.NZ; j++)
                {
                    if (r.Kinds[i, j] != Kind.Bare || seen[i, j]) continue;
                    var lot = new List<Vector2Int>();
                    var todo = new Queue<Vector2Int>();
                    todo.Enqueue(new Vector2Int(i, j));
                    seen[i, j] = true;
                    while (todo.Count > 0)
                    {
                        var c = todo.Dequeue();
                        lot.Add(c);
                        foreach (var n in new[] { new Vector2Int(c.x - 1, c.y), new Vector2Int(c.x + 1, c.y),
                                                  new Vector2Int(c.x, c.y - 1), new Vector2Int(c.x, c.y + 1) })
                        {
                            if (n.x < 0 || n.y < 0 || n.x >= r.NX || n.y >= r.NZ) continue;
                            if (r.Kinds[n.x, n.y] != Kind.Bare || seen[n.x, n.y]) continue;
                            seen[n.x, n.y] = true;
                            todo.Enqueue(n);
                        }
                    }
                    if (lot.Count <= biggestBox) continue;
                    int x0 = int.MaxValue, x1 = int.MinValue, z0 = int.MaxValue, z1 = int.MinValue;
                    foreach (var c in lot) { x0 = Mathf.Min(x0, c.x); x1 = Mathf.Max(x1, c.x); z0 = Mathf.Min(z0, c.y); z1 = Mathf.Max(z1, c.y); }
                    sb.AppendLine($"   bare asphalt bigger than a junction: {lot.Count} cells at x {r.X(x0):F0}..{r.X(x1 + 1):F0} z {r.Z(z0):F0}..{r.Z(z1 + 1):F0}");
                }
            int narrow = 0;
            for (int i = 0; i < r.NX; i++)
                for (int j = 0; j < r.NZ; j++)
                    if (r.Kinds[i, j] == Kind.NarrowEW || r.Kinds[i, j] == Kind.NarrowNS) narrow++;
            if (narrow > 0) sb.AppendLine($"   narrow 10 m road: {narrow} cells (the city wants 15 m streets; a cut is missing)");
            if (unmatchedLanes > 0) sb.AppendLine($"   lanes with no direction on record: {unmatchedLanes} cells, run north/east: {string.Join(" ", unmatchedAt)}");
            if (r.Clashes > 0) sb.AppendLine($"   WARNING: {r.Clashes} cell(s) claimed by two blocks");
            return sb.Length == 0 ? "   every gap reads as a road" : sb.ToString().TrimEnd();
        }

        // ------------------------------------------------------------------ the tiles

        /// <summary>
        /// Lays the tiles. A road is laid a 5 m length at a time across its whole width -
        /// the builder's own profiles: a street is parking strip, the two facing halves
        /// (each with its yellow line on the crown and its white line on its own kerb),
        /// parking strip; a boulevard its kerb lanes, dashed inner lanes, median and
        /// divider; a narrow road the two halves alone; a lane bare asphalt with an arrow
        /// at each mouth. The length that opens onto a junction is the zebra. Junctions
        /// and strips are plain asphalt, car parks rows of painted bays.
        /// </summary>
        public static void Lay(Raster r, Func<GameObject, Transform, GameObject> stand, Transform parent)
        {
            int sc = StreetCells, bc = BlvdCells;
            var kinds = r.Kinds;
            Kind At(int i, int j) => r.At(i, j);
            bool Same(int i, int j, int di, int dj, int n)
            {
                for (int k = 0; k < n; k++) if (At(i + k * di, j + k * dj) != kinds[i, j]) return false;
                return true;
            }
            var street = Band(-StreetHalf, StreetHalf);
            var blvdNear = Band(-BlvdHalf, -MedianHalf);
            var blvdFar = Band(MedianHalf, BlvdHalf);
            var kit = new Kit(stand, parent);
            LayCarParks(r, kit);

            for (int i = 0; i < r.NX; i++)
                for (int j = 0; j < r.NZ; j++)
                {
                    float mx = r.X(i), mz = r.Z(j);
                    switch (kinds[i, j])
                    {
                        case Kind.Bare:
                        case Kind.Wide:
                        case Kind.Yard:
                            kit.Tile(Bare, mx, mz, 0, Cell, Cell);
                            break;

                        case Kind.LaneNS:
                        case Kind.LaneEW:
                        {
                            bool ns = kinds[i, j] == Kind.LaneNS;
                            kit.Tile(Bare, mx, mz, 0, Cell, Cell);
                            // the arrow at each mouth, pointing the way the lane runs
                            bool mouth = ns ? At(i, j - 1) != Kind.LaneNS || At(i, j + 1) != Kind.LaneNS
                                            : At(i - 1, j) != Kind.LaneEW || At(i + 1, j) != Kind.LaneEW;
                            if (mouth)
                            {
                                int dir = r.Dir[i, j];
                                int yaw = ns ? (dir > 0 ? 0 : 180) : (dir > 0 ? 90 : 270);
                                kit.Tile(Arrow, mx, mz, yaw, Cell, Cell, 0.01f);
                            }
                            break;
                        }

                        case Kind.NarrowEW:
                            if (At(i, j - 1) == Kind.NarrowEW) break;
                            if (!Same(i, j, 0, 1, 2)) { kit.Tile(Bare, mx, mz, 0, Cell, Cell); break; }
                            kit.Tile(RoadHalfTile, mx, mz, 270, Cell, Cell);
                            kit.Tile(RoadHalfTile, mx, mz + Cell, 90, Cell, Cell);
                            break;

                        case Kind.NarrowNS:
                            if (At(i - 1, j) == Kind.NarrowNS) break;
                            if (!Same(i, j, 1, 0, 2)) { kit.Tile(Bare, mx, mz, 0, Cell, Cell); break; }
                            kit.Tile(RoadHalfTile, mx, mz, 0, Cell, Cell);
                            kit.Tile(RoadHalfTile, mx + Cell, mz, 180, Cell, Cell);
                            break;

                        case Kind.StreetEW:
                        {
                            if (At(i, j - 1) == Kind.StreetEW) break;             // laid from its south cell
                            if (!Same(i, j, 0, 1, sc)) { kit.Tile(Bare, mx, mz, 0, Cell, Cell); break; }
                            float cz = mz + StreetHalf;
                            bool mouth = At(i - 1, j) != Kind.StreetEW && IsRoad(At(i - 1, j)) ||
                                        At(i + 1, j) != Kind.StreetEW && IsRoad(At(i + 1, j));
                            if (mouth)
                                foreach (var t in street) kit.Tile(Crossing, mx, cz + t.off, 0, Cell, t.w);
                            else
                            {
                                kit.Tile(RoadHalfTile, mx, cz - Cell, 270, Cell, Cell);
                                kit.Tile(RoadHalfTile, mx, cz, 90, Cell, Cell);
                                kit.Tile(Bare, mx, cz - StreetHalf, 90, Cell, ParkLane);
                                kit.Tile(Bare, mx, cz + StreetHalf - ParkLane, 90, Cell, ParkLane);
                            }
                            break;
                        }

                        case Kind.StreetNS:
                        {
                            if (At(i - 1, j) == Kind.StreetNS) break;             // laid from its west cell
                            if (!Same(i, j, 1, 0, sc)) { kit.Tile(Bare, mx, mz, 0, Cell, Cell); break; }
                            float cx = mx + StreetHalf;
                            bool mouth = At(i, j - 1) != Kind.StreetNS && IsRoad(At(i, j - 1)) ||
                                        At(i, j + 1) != Kind.StreetNS && IsRoad(At(i, j + 1));
                            if (mouth)
                                foreach (var t in street) kit.Tile(Crossing, cx + t.off, mz, 90, t.w, Cell);
                            else
                            {
                                kit.Tile(RoadHalfTile, cx - Cell, mz, 0, Cell, Cell);
                                kit.Tile(RoadHalfTile, cx, mz, 180, Cell, Cell);
                                kit.Tile(Bare, cx - StreetHalf, mz, 0, ParkLane, Cell);
                                kit.Tile(Bare, cx + StreetHalf - ParkLane, mz, 0, ParkLane, Cell);
                            }
                            break;
                        }

                        case Kind.BlvdNS:
                        {
                            if (At(i - 1, j) == Kind.BlvdNS) break;
                            if (!Same(i, j, 1, 0, bc)) { kit.Tile(Bare, mx, mz, 0, Cell, Cell); break; }
                            float cx = mx + BlvdHalf;
                            bool mouth = At(i, j - 1) != Kind.BlvdNS && IsRoad(At(i, j - 1)) ||
                                        At(i, j + 1) != Kind.BlvdNS && IsRoad(At(i, j + 1));
                            if (mouth)
                            {
                                foreach (var t in blvdNear) kit.Tile(Crossing, cx + t.off, mz, 90, t.w, Cell);
                                foreach (var t in blvdFar) kit.Tile(Crossing, cx + t.off, mz, 90, t.w, Cell);
                                kit.Tile(MedianTile, cx - Cell, mz, 180, Cell, Cell);
                                kit.Tile(MedianTile, cx, mz, 0, Cell, Cell);
                            }
                            else
                            {
                                kit.Tile(LaneEdge, cx - 3f * Cell, mz, 0, Cell, Cell);
                                kit.Tile(LaneDash, cx - 2f * Cell, mz, 180, Cell, Cell);
                                kit.Tile(MedianTile, cx - Cell, mz, 180, Cell, Cell);
                                kit.Tile(MedianTile, cx, mz, 0, Cell, Cell);
                                kit.Tile(LaneDash, cx + Cell, mz, 0, Cell, Cell);
                                kit.Tile(LaneEdge, cx + 2f * Cell, mz, 180, Cell, Cell);
                                kit.Tile(Bare, cx - BlvdHalf, mz, 0, ParkLane, Cell);
                                kit.Tile(Bare, cx + BlvdHalf - ParkLane, mz, 0, ParkLane, Cell);
                                // the divider stops a length short of a junction, as the
                                // builder's does, so a car can turn across
                                if (At(i, j - 2) == Kind.BlvdNS && At(i, j + 2) == Kind.BlvdNS)
                                    kit.Piece(Divider, new Vector3(cx, 0f, mz), 0);
                            }
                            break;
                        }

                        case Kind.BlvdEW:
                        {
                            if (At(i, j - 1) == Kind.BlvdEW) break;
                            if (!Same(i, j, 0, 1, bc)) { kit.Tile(Bare, mx, mz, 0, Cell, Cell); break; }
                            float cz = mz + BlvdHalf;
                            bool mouth = At(i - 1, j) != Kind.BlvdEW && IsRoad(At(i - 1, j)) ||
                                        At(i + 1, j) != Kind.BlvdEW && IsRoad(At(i + 1, j));
                            if (mouth)
                            {
                                foreach (var t in blvdNear) kit.Tile(Crossing, mx, cz + t.off, 0, Cell, t.w);
                                foreach (var t in blvdFar) kit.Tile(Crossing, mx, cz + t.off, 0, Cell, t.w);
                                kit.Tile(MedianTile, mx, cz - Cell, 90, Cell, Cell);
                                kit.Tile(MedianTile, mx, cz, 270, Cell, Cell);
                            }
                            else
                            {
                                kit.Tile(LaneEdge, mx, cz - 3f * Cell, 270, Cell, Cell);
                                kit.Tile(LaneDash, mx, cz - 2f * Cell, 90, Cell, Cell);
                                kit.Tile(MedianTile, mx, cz - Cell, 90, Cell, Cell);
                                kit.Tile(MedianTile, mx, cz, 270, Cell, Cell);
                                kit.Tile(LaneDash, mx, cz + Cell, 270, Cell, Cell);
                                kit.Tile(LaneEdge, mx, cz + 2f * Cell, 90, Cell, Cell);
                                kit.Tile(Bare, mx, cz - BlvdHalf, 90, Cell, ParkLane);
                                kit.Tile(Bare, mx, cz + BlvdHalf - ParkLane, 90, Cell, ParkLane);
                                if (At(i - 2, j) == Kind.BlvdEW && At(i + 2, j) == Kind.BlvdEW)
                                    kit.Piece(Divider, new Vector3(mx, 0f, cz), 90);
                            }
                            break;
                        }
                    }
                }
        }

        /// <summary>
        /// The car parks, lot by lot. A lot is rows of bays with aisles between them: the
        /// kit's row of three bays is one cell deep and two wide, its bays open at the
        /// edge the pivot sits on, so a row faces its aisle by the way it is turned. Rows
        /// run the lot's long way; the row nearest the street is bays facing the street
        /// (the kerb-side parking of a strip a cell deep), then aisle, then bays facing
        /// back to it, bays facing the next aisle, and so on. Stood any other way the
        /// rows' dividing lines join up into stripes the length of the lot.
        /// </summary>
        static void LayCarParks(Raster r, Kit kit)
        {
            var seen = new bool[r.NX, r.NZ];
            for (int i = 0; i < r.NX; i++)
                for (int j = 0; j < r.NZ; j++)
                {
                    if (r.Kinds[i, j] != Kind.Parking || seen[i, j]) continue;
                    var lot = new List<Vector2Int>();
                    var todo = new Queue<Vector2Int>();
                    todo.Enqueue(new Vector2Int(i, j));
                    seen[i, j] = true;
                    while (todo.Count > 0)
                    {
                        var c = todo.Dequeue();
                        lot.Add(c);
                        foreach (var n in new[] { new Vector2Int(c.x - 1, c.y), new Vector2Int(c.x + 1, c.y),
                                                  new Vector2Int(c.x, c.y - 1), new Vector2Int(c.x, c.y + 1) })
                        {
                            if (n.x < 0 || n.y < 0 || n.x >= r.NX || n.y >= r.NZ) continue;
                            if (r.Kinds[n.x, n.y] != Kind.Parking || seen[n.x, n.y]) continue;
                            seen[n.x, n.y] = true;
                            todo.Enqueue(n);
                        }
                    }
                    LayLot(r, kit, lot);
                }
        }

        static void LayLot(Raster r, Kit kit, List<Vector2Int> lot)
        {
            int x0 = int.MaxValue, x1 = int.MinValue, z0 = int.MaxValue, z1 = int.MinValue;
            foreach (var c in lot) { x0 = Mathf.Min(x0, c.x); x1 = Mathf.Max(x1, c.x); z0 = Mathf.Min(z0, c.y); z1 = Mathf.Max(z1, c.y); }
            int w = x1 - x0 + 1, d = z1 - z0 + 1;
            // rows run the long way; the lot is entered from the side with the most road
            bool rowsAlongX = w >= d;
            int Roadside(int di, int dj)
            {
                int n = 0;
                foreach (var c in lot)
                {
                    var k = r.At(c.x + di, c.y + dj);
                    if (IsRoad(k) && k != Kind.Parking) n++;
                }
                return n;
            }
            bool entryLow = rowsAlongX ? Roadside(0, -1) >= Roadside(0, 1) : Roadside(-1, 0) >= Roadside(1, 0);
            int depth = rowsAlongX ? d : w;
            int length = rowsAlongX ? w : d;
            var dice = new System.Random(x0 * 73856093 ^ z0 * 19349663 ^ lot.Count);
            var laid = new HashSet<Vector2Int>();
            var cells = new HashSet<Vector2Int>(lot);

            // the rows, from the street inward: a row of bays backing onto the street and
            // facing the aisle behind it, the aisle, a row facing that aisle; then again.
            // A last row that would face nothing is an aisle. A lot one row deep is
            // kerb-side parking, its bays opening onto the street
            bool Aisle(int k) => depth > 1 && (k % 3 == 1 || (k % 3 == 0 && k == depth - 1));
            bool OpensInward(int k) => depth > 1 && k % 3 == 0;   // towards the higher row, away from the street

            // the drives: the columns at either end of the lot, street to the last aisle,
            // which is how a car gets in and out
            bool Drive(int along) => depth > 1 && length >= 3 && (along == 0 || along == length - 1);

            foreach (var c in lot)
            {
                if (laid.Contains(c)) continue;
                int k = rowsAlongX ? (entryLow ? c.y - z0 : z1 - c.y) : (entryLow ? c.x - x0 : x1 - c.x);
                int along = rowsAlongX ? c.x - x0 : c.y - z0;
                float mx = r.X(c.x), mz = r.Z(c.y);
                if (Aisle(k) || Drive(along))
                {
                    kit.Tile(Bare, mx, mz, 0, Cell, Cell);
                    laid.Add(c);
                    continue;
                }
                // a row of bays is two cells long along the row; the second cell must be
                // the lot's, in the same row, and not a drive
                var next = rowsAlongX ? new Vector2Int(c.x + 1, c.y) : new Vector2Int(c.x, c.y + 1);
                if (!cells.Contains(next) || laid.Contains(next) || Drive(along + 1))
                {
                    kit.Tile(Bare, mx, mz, 0, Cell, Cell);
                    laid.Add(c);
                    continue;
                }
                // the bays open at the edge the pivot sits on. Opening inward means towards
                // the higher row; which side of the world that is depends on where the
                // street is
                bool openLow = OpensInward(k) ? !entryLow : entryLow;
                if (rowsAlongX)
                    kit.Piece(ParkingRow, openLow ? new Vector3(mx, 0f, mz) : new Vector3(mx + 2f * Cell, 0f, mz + Cell), openLow ? 180 : 0);
                else
                    kit.Piece(ParkingRow, openLow ? new Vector3(mx, 0f, mz + 2f * Cell) : new Vector3(mx + Cell, 0f, mz), openLow ? 270 : 90);
                laid.Add(c);
                laid.Add(next);

                // the cars: a bay in three is taken, most of them nosed in, the same ones
                // every time the lot is drawn
                for (int bay = 0; bay < 3; bay++)
                {
                    if (dice.NextDouble() > Occupancy) continue;
                    float at = 2f * Cell * (bay + 0.5f) / 3f;
                    bool noseIn = dice.NextDouble() < NosedIn;
                    Vector3 pos;
                    int nose;   // a car nosed in faces away from the open edge
                    if (rowsAlongX)
                    {
                        pos = new Vector3(mx + at, 0f, mz + Cell * 0.5f);
                        nose = openLow ? (noseIn ? 0 : 180) : (noseIn ? 180 : 0);
                    }
                    else
                    {
                        pos = new Vector3(mx + Cell * 0.5f, 0f, mz + at);
                        nose = openLow ? (noseIn ? 90 : 270) : (noseIn ? 270 : 90);
                    }
                    var car = Cars.Pick(dice);
                    if (car != null) kit.Stand(car, pos, nose);
                }
            }

            // the kerb: a car park is closed off from every road it touches, and opens
            // onto the street only at the mouths of its drives. A lot one row deep is the
            // street's own kerb-side parking and stays open along the street
            foreach (var c in lot)
            {
                int k = rowsAlongX ? (entryLow ? c.y - z0 : z1 - c.y) : (entryLow ? c.x - x0 : x1 - c.x);
                int along = rowsAlongX ? c.x - x0 : c.y - z0;
                float mx = r.X(c.x), mz = r.Z(c.y);
                foreach (var (di, dj) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    var n = new Vector2Int(c.x + di, c.y + dj);
                    if (cells.Contains(n)) continue;
                    var beyond = r.At(n.x, n.y);
                    if (!IsRoad(beyond) || beyond == Kind.Parking) continue;   // the block's own kerb stands there
                    bool inDepth = rowsAlongX ? dj != 0 : di != 0;               // the road lies off the lot's front or back
                    if (inDepth && (depth == 1 || Drive(along))) continue;      // open bays, or a drive's mouth
                    if (dj > 0) kit.Tile(KerbTile, mx, mz + Cell - KerbDepth, 0, Cell, KerbDepth, 0.01f);
                    else if (dj < 0) kit.Tile(KerbTile, mx, mz, 180, Cell, KerbDepth, 0.01f);
                    else if (di > 0) kit.Tile(KerbTile, mx + Cell - KerbDepth, mz, 90, KerbDepth, Cell, 0.01f);
                    else kit.Tile(KerbTile, mx, mz, 270, KerbDepth, Cell, 0.01f);
                }
            }
        }

        /// <summary>How many bays of a car park are taken, and how many of the cars in them
        /// nosed in rather than backing in.</summary>
        const float Occupancy = 0.35f, NosedIn = 0.8f;

        /// <summary>Tiles that close [lo, hi] across a road at nearest to the 5 m beat: each
        /// one's near edge off the axis, and its width (RoadDemoBuilder.Band).</summary>
        static (float off, float w)[] Band(float lo, float hi)
        {
            int n = Mathf.Max(1, Mathf.RoundToInt((hi - lo) / Cell));
            float w = (hi - lo) / n;
            var band = new (float, float)[n];
            for (int k = 0; k < n; k++) band[k] = (lo + k * w, w);
            return band;
        }

        /// <summary>
        /// The cars a car park holds: every road car of the Synty packs the city draws its
        /// traffic from, less what the catalog bars (the wrong decade, the wrong livery)
        /// and less anything marked as police or emergency - VehicleCatalog's rules, the
        /// same ones RoadDemoBuilder's traffic pool applies - each weighted the way the
        /// catalog weights the pool, so the lots hold saloons and not one of everything.
        /// </summary>
        static class Cars
        {
            static readonly string[] Folders =
            {
                "Assets/Synty/PolygonPalmCity/Prefabs/Vehicles",
                "Assets/Synty/PolygonCity/Prefabs/Vehicles",
            };
            static readonly string[] Deny =
            {
                "boat", "yacht", "jetski", "helicopter", "plane", "cart", "scooter", "bike", "moped",
                "bot", "steering", "wheel", "trailer", "monster", "quad", "attach", "bus", "truck",
            };
            static List<GameObject> _pool;

            public static GameObject Pick(System.Random dice)
            {
                if (_pool == null)
                {
                    _pool = new List<GameObject>();
                    foreach (var guid in DemoAssetLoad.Find("t:Prefab", Folders))
                    {
                        string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                        string low = path.ToLowerInvariant();
                        bool denied = false;
                        foreach (var deny in Deny)
                            if (low.Contains(deny)) { denied = true; break; }
                        if (denied) continue;
                        if (LivingCity.Gameplay.VehicleCatalog.IsBarred(path)) continue;
                        if (LivingCity.Gameplay.VehicleCatalog.IsMarkedService(path)) continue;
                        var prefab = DemoAssetLoad.Load<GameObject>(path);
                        if (prefab == null) continue;
                        for (int seat = 0, seats = LivingCity.Gameplay.VehicleCatalog.PoolWeight(path); seat < seats; seat++)
                            _pool.Add(prefab);
                    }
                    if (_pool.Count == 0) Debug.LogWarning("[CoreRoads] no cars for the car parks: the vehicle folders came up empty.");
                }
                return _pool.Count == 0 ? null : _pool[dice.Next(_pool.Count)];
            }
        }

        /// <summary>The road kit, stood through whatever the host stands prefabs with.</summary>
        sealed class Kit
        {
            readonly Func<GameObject, Transform, GameObject> _stand;
            readonly Transform _parent;
            readonly Dictionary<string, GameObject> _prefabs = new Dictionary<string, GameObject>();

            public Kit(Func<GameObject, Transform, GameObject> stand, Transform parent)
            {
                _stand = stand;
                _parent = parent;
            }

            /// <summary>The kit's 5 m piece laid to cover [mx, mx+sizeX] x [mz, mz+sizeZ]
            /// exactly: pivot at its +X/+Z corner (turned by the yaw), scaled to the size -
            /// RoadDemoBuilder.PlaceTile, where the arithmetic is explained.</summary>
            public void Tile(string piece, float mx, float mz, int yaw, float sizeX, float sizeZ, float y = 0f)
            {
                Vector3 pivot, scale;
                switch (yaw)
                {
                    case 0:
                        pivot = new Vector3(mx + sizeX, y, mz + sizeZ);
                        scale = new Vector3(sizeX / Cell, 1f, sizeZ / Cell);
                        break;
                    case 90:
                        pivot = new Vector3(mx + sizeX, y, mz);
                        scale = new Vector3(sizeZ / Cell, 1f, sizeX / Cell);
                        break;
                    case 180:
                        pivot = new Vector3(mx, y, mz);
                        scale = new Vector3(sizeX / Cell, 1f, sizeZ / Cell);
                        break;
                    default:
                        pivot = new Vector3(mx, y, mz + sizeZ);
                        scale = new Vector3(sizeZ / Cell, 1f, sizeX / Cell);
                        break;
                }
                var go = Piece(piece, pivot, yaw);
                if (go && (scale - Vector3.one).sqrMagnitude > 1e-6f) go.transform.localScale = scale;
            }

            /// <summary>Any prefab, stood where it is told, turned by the yaw.</summary>
            public GameObject Stand(GameObject prefab, Vector3 at, int yaw)
            {
                var go = _stand(prefab, _parent);
                go.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, yaw, 0f));
                return go;
            }

            public GameObject Piece(string piece, Vector3 at, int yaw)
            {
                if (!_prefabs.TryGetValue(piece, out var prefab))
                {
                    prefab = DemoAssetLoad.Load<GameObject>(CityEnv + piece + ".prefab");
                    _prefabs[piece] = prefab;
                    if (prefab == null) Debug.LogWarning($"[CoreRoads] {CityEnv}{piece}.prefab is missing; left bare.");
                }
                if (prefab == null) return null;
                var go = _stand(prefab, _parent);
                go.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, yaw, 0f));
                return go;
            }
        }
    }
}
