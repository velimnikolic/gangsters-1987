using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// The city core's roads, drawn between its blocks.
    ///
    /// A road is a CORRIDOR: a straight strip of ONE width, run as far as the blocks let
    /// it, at one of the widths the city knows - the 5 m one-way alley, 10 m of two lanes,
    /// the 15 m street (two lanes and a parking strip each side), the 35 m boulevard (2+2
    /// and the median). Where two corridors cross, the ground is a junction: bare asphalt,
    /// a zebra at every mouth.
    ///
    /// A road is deliberately NOT read off the width of the gap it stands in. Synty's
    /// blocks are not rectangles - an edge steps in and out by a cell, a corner is cut
    /// away - so a gap read cell by cell is fifteen metres here and ten there, and the
    /// street laid to that reading breaks into pieces with holes between them. The gap is
    /// read once, for the road's whole run, and whatever the corridors do not claim is
    /// ground LEFT OVER: a block's own bay is the car park the demo had there, and any
    /// other left-over cell is LEFT BARE - nothing is stood on it, and the report says how
    /// big it is and where, for the user to say what belongs there. Guessing a strip of
    /// asphalt would only read as one more lane of the road beside it.
    ///
    /// Everything is worked on a 5 m raster of the core's ground; the raster is kept
    /// (<see cref="Raster.Map"/>) so a probe can read the drawing back without a picture,
    /// and the ground the roads left over is reported (<see cref="Raster.Report"/>) -
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
            Spare,                    // ground left over beside a road: left bare, for the user to say
            Parking, Block,
        }

        /// <summary>
        /// A stretch of one road between two junctions - what the traffic needs and the
        /// tiles do not: the crown it runs down, how far it runs, how wide it is, and the
        /// junction box at either end. <see cref="NodeA"/>/<see cref="NodeB"/> index
        /// <see cref="Raster.Junctions"/>, or are -1 where the road simply ends.
        /// </summary>
        public struct Stretch
        {
            public bool Vertical;
            public float Crown;        // x of a north-south road, z of an east-west one
            public float From, To;     // along the road, metres
            public int Width;          // cells across: 1 alley, 2 narrow, 3 street, 7 boulevard
            public int Direction;      // an alley's one way (+1 north/east); 0 for a two-way road
            public int NodeA, NodeB;
        }

        public sealed class Raster
        {
            public Kind[,] Kinds;
            public sbyte[,] Dir;              // lanes: +1 north/east, -1 south/west
            public byte[,] Across;            // how far across its road the cell lies; the road is laid from 0
            public float X0, Z0;              // the south-west corner of cell (0, 0)
            public int NX, NZ;
            public int Clashes;               // cells two blocks both claim
            public string Map;                // north up, one character per cell
            public string Report;             // the ground the roads left over, and other oddities
            public int BlockArea, RoadArea, ParkingArea, SpareArea;
            /// <summary>The junction boxes: the bare asphalt where roads cross.</summary>
            public List<Rect> Junctions = new List<Rect>();
            /// <summary>Every stretch of road between two of them.</summary>
            public List<Stretch> Stretches = new List<Stretch>();
            public float X(int i) => X0 + i * Cell;
            public float Z(int j) => Z0 + j * Cell;
            public Kind At(int i, int j) => i < 0 || j < 0 || i >= NX || j >= NZ ? Kind.Outside : Kinds[i, j];
        }

        /// <summary>
        /// Reads the free ground off the blocks as they stand - a ring a street wide round
        /// every block, and everything the rings enclose - runs the roads through it, and
        /// leaves the rest to the car parks.
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

            // the blocks by their shapes, their bays as yards
            foreach (var block in blocks)
            {
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
                        }
                        else if (kinds[a, b] == Kind.Outside) kinds[a, b] = Kind.Yard;
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
            for (int i = 0; i < w; i++)
                for (int j = 0; j < h; j++)
                {
                    float cz = r.Z(j) + Cell * 0.5f;
                    if (cz > band.x && cz < band.y && kinds[i, j] == Kind.Outside) kinds[i, j] = Kind.Bare;
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

            // ------------------------------------------------------------- the roads
            // A road is NOT read off the gap it stands in. A gap changes width wherever a
            // block's edge steps in or out - block-16's west side is a bay for its northern
            // forty metres, block-12's east side steps out five - and a width read cell by
            // cell then reads one length of a street as fifteen metres, the next as ten,
            // and the cell between them as a junction, which leaves the street in pieces.
            //
            // A road is a CORRIDOR instead: a straight strip of ONE width, run as far as
            // the blocks let it. Everything a corridor does not claim is ground left over -
            // a block's bay is the car park the demo had there, anything else is paved and
            // reported so the cuts can be argued about - and left-over ground is never
            // given a road's markings (Docs/core-district-plan.md).
            var roads = Roads(kinds, w, h, r, band);

            // where two roads cross the ground is a junction: bare asphalt, a zebra at
            // every mouth. A junction takes the whole width of BOTH roads it belongs to, so
            // that a 5 m length of road is either all junction or all road and never half
            // of each - a profile goes down across a road's full width at once, and half a
            // length would leave the other half with no tile at all
            var carries = new Corridor[w, h];
            var junction = new bool[w, h];
            foreach (var road in roads)
                for (int k = road.From; k < road.To; k++)
                    for (int t = 0; t < road.W; t++)
                    {
                        int i = road.Vertical ? road.A + t : k, j = road.Vertical ? k : road.A + t;
                        if (carries[i, j] == null) carries[i, j] = road;
                        else junction[i, j] = true;
                    }
            // a ROAD that ends against ANOTHER road makes a junction there too, even though
            // it could not take that ground for itself. Two cases, and both are broken
            // without it:
            //
            // across - the road T's into a street. Its mouth would otherwise face the flank
            // of a street whose lines run straight past, and what is left of that street
            // between two such mouths is a five-metre stub of markings meaning nothing.
            //
            // ALONG - the road runs on as another road five metres to one side, because the
            // blocks either side of it step out (S3 does exactly this where block-12 juts
            // into it). Without a junction the two are strangers standing face to face: each
            // ends in a dead end of its own, and a car that drives up one turns round in the
            // same five metres the car coming down the other is turning round in. That is a
            // pair of cars locked together for the rest of the run, and it is what the play
            // harness found (Docs/play-harness.md).
            //
            // An alley is not a road for this: its mouth is a gap in the kerb and a give-way
            // sign, not a crossing, and a zebra thrown across a boulevard for one is absurd
            foreach (var road in roads)
                foreach (int k in road.W > 1 ? new[] { road.From - 1, road.To } : new int[0])
                {
                    if (k < 0 || k >= (road.Vertical ? h : w)) continue;
                    for (int t = 0; t < road.W; t++)
                    {
                        int i = road.Vertical ? road.A + t : k, j = road.Vertical ? k : road.A + t;
                        var carrier = carries[i, j];
                        if (carrier != null && carrier != road) junction[i, j] = true;
                    }
                }
            // is this 5 m length of the road a junction? The length may lie beyond the
            // road's own run - what matters is the ground, not whose claim it is
            bool Crossed(Corridor road, int k)
            {
                if (k < 0 || k >= (road.Vertical ? h : w)) return false;
                for (int t = 0; t < road.W; t++)
                {
                    int i = road.Vertical ? road.A + t : k, j = road.Vertical ? k : road.A + t;
                    if (junction[i, j]) return true;
                }
                return false;
            }
            bool Cross(Corridor road, int k)
            {
                bool spread = false;
                for (int t = 0; t < road.W; t++)
                {
                    int i = road.Vertical ? road.A + t : k, j = road.Vertical ? k : road.A + t;
                    if (junction[i, j]) continue;
                    junction[i, j] = true;
                    spread = true;
                }
                return spread;
            }
            // does the road stop beyond this length? A block, or ground no road covers -
            // but NOT the edge of the drawing, where the road carries on into the city
            bool Stops(Corridor road, int k)
            {
                if (k < 0 || k >= (road.Vertical ? h : w)) return false;
                return !Crossed(road, k) && (k < road.From || k >= road.To);
            }
            void Spread()
            {
                for (bool spreading = true; spreading;)
                {
                    spreading = false;
                    foreach (var road in roads)
                        for (int k = road.From; k < road.To; k++)
                            if (Crossed(road, k)) spreading |= Cross(road, k);
                }
            }
            Spread();

            // A 5 m length of road hemmed in between two junctions is not a road: it is the
            // middle of a WIDE crossing, where two streets come at a third five metres apart
            // because the blocks either side of it are not in line. Left as road it draws a
            // stub of yellow line between two zebras. Given to the junction, the crossing is
            // one crossing, which is what a driver sees.
            for (bool absorbing = true; absorbing;)
            {
                absorbing = false;
                foreach (var road in roads)
                    for (int k = road.From; k < road.To; k++)
                    {
                        if (Crossed(road, k)) continue;
                        // hemmed in by the GROUND either side, whoever laid it: a junction,
                        // or the place the road stops. A length at the edge of the drawing
                        // is not hemmed - the road carries on into the city from there
                        if (!Crossed(road, k - 1) && !Stops(road, k - 1)) continue;
                        if (!Crossed(road, k + 1) && !Stops(road, k + 1)) continue;
                        absorbing |= Cross(road, k);
                    }
                if (absorbing) Spread();
            }

            var read = (Kind[,])kinds.Clone();
            r.Across = new byte[w, h];
            foreach (var road in roads)
                for (int k = road.From; k < road.To; k++)
                    for (int t = 0; t < road.W; t++)
                    {
                        int i = road.Vertical ? road.A + t : k, j = road.Vertical ? k : road.A + t;
                        if (junction[i, j]) { read[i, j] = Kind.Bare; continue; }
                        read[i, j] = Profile(road);
                        r.Across[i, j] = (byte)t;
                    }

            // what no road claimed: a block's bay is its car park, the rest is spare ground
            for (int i = 0; i < w; i++)
                for (int j = 0; j < h; j++)
                {
                    if (carries[i, j] != null) continue;
                    if (kinds[i, j] == Kind.Yard) read[i, j] = Kind.Parking;
                    else if (kinds[i, j] == Kind.Bare) read[i, j] = Kind.Spare;
                }

            // the alleys' directions, out of the layout's table by where each alley lies
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
                    if (dir == 0)
                    {
                        dir = 1;
                        unmatched++;
                        if (unmatchedAt.Count < Listed) unmatchedAt.Add($"({cx:F0}, {cz:F0})");
                    }
                    r.Dir[i, j] = dir;
                }

            // the graph the traffic rides: every junction box, and every stretch of road
            // between two of them. The tiles do not need this and the drivers cannot do
            // without it (CoreDistrict turns it into the lane graph)
            var boxOf = new int[w, h];
            for (int i = 0; i < w; i++)
                for (int j = 0; j < h; j++)
                {
                    if (!junction[i, j] || boxOf[i, j] != 0) continue;
                    int mine = r.Junctions.Count + 1;
                    int x0 = i, x1 = i, z0 = j, z1 = j;
                    var todo = new Queue<Vector2Int>();
                    todo.Enqueue(new Vector2Int(i, j));
                    boxOf[i, j] = mine;
                    while (todo.Count > 0)
                    {
                        var c = todo.Dequeue();
                        x0 = Mathf.Min(x0, c.x); x1 = Mathf.Max(x1, c.x);
                        z0 = Mathf.Min(z0, c.y); z1 = Mathf.Max(z1, c.y);
                        foreach (var n in new[] { new Vector2Int(c.x - 1, c.y), new Vector2Int(c.x + 1, c.y),
                                                  new Vector2Int(c.x, c.y - 1), new Vector2Int(c.x, c.y + 1) })
                        {
                            if (n.x < 0 || n.y < 0 || n.x >= w || n.y >= h) continue;
                            if (!junction[n.x, n.y] || boxOf[n.x, n.y] != 0) continue;
                            boxOf[n.x, n.y] = mine;
                            todo.Enqueue(n);
                        }
                    }
                    r.Junctions.Add(Rect.MinMaxRect(r.X(x0), r.Z(z0), r.X(x1 + 1), r.Z(z1 + 1)));
                }

            int BoxAt(Corridor road, int k)
            {
                if (k < 0 || k >= (road.Vertical ? h : w)) return -1;
                for (int t = 0; t < road.W; t++)
                {
                    int i = road.Vertical ? road.A + t : k, j = road.Vertical ? k : road.A + t;
                    if (boxOf[i, j] > 0) return boxOf[i, j] - 1;
                }
                return -1;
            }

            foreach (var road in roads)
                for (int k = road.From; k < road.To;)
                {
                    if (Crossed(road, k)) { k++; continue; }
                    int start = k;
                    while (k < road.To && !Crossed(road, k)) k++;
                    int dir = 0;
                    if (road.W == 1)
                    {
                        int i = road.Vertical ? road.A : start, j = road.Vertical ? start : road.A;
                        dir = r.Dir[i, j];
                    }
                    r.Stretches.Add(new Stretch
                    {
                        Vertical = road.Vertical,
                        Crown = (road.Vertical ? r.X(road.A) : r.Z(road.A)) + road.W * Cell * 0.5f,
                        From = road.Vertical ? r.Z(start) : r.X(start),
                        To = road.Vertical ? r.Z(k) : r.X(k),
                        Width = road.W,
                        Direction = dir,
                        NodeA = BoxAt(road, start - 1),
                        NodeB = BoxAt(road, k),
                    });
                }

            r.Kinds = read;
            Count(r);
            r.Map = Draw(r);
            r.Report = Oddities(r, blocks, roads, unmatched, unmatchedAt);
            return r;
        }

        /// <summary>
        /// A road: a straight strip of one width, running as far as the blocks let it.
        /// <see cref="A"/> is its first cell across - the west cell of a north-south road,
        /// the south cell of an east-west one - and <see cref="From"/>..<see cref="To"/>
        /// its run along itself. All of it in raster cells.
        /// </summary>
        sealed class Corridor
        {
            public bool Vertical;
            public int A, W;
            public int From, To;
            public int Kerbs;          // lengths of it with a block against at least one side
            public bool Declared;      // the layout's own, not read off the blocks
            public int Length => To - From;
            public int Cells => Length * W;
        }

        /// <summary>The road a corridor of this width carries.</summary>
        static Kind Profile(Corridor road)
        {
            if (road.W == BlvdCells) return road.Vertical ? Kind.BlvdNS : Kind.BlvdEW;
            if (road.W == StreetCells) return road.Vertical ? Kind.StreetNS : Kind.StreetEW;
            if (road.W == 2) return road.Vertical ? Kind.NarrowNS : Kind.NarrowEW;
            return road.Vertical ? Kind.LaneNS : Kind.LaneEW;
        }

        /// <summary>
        /// The roads between the blocks.
        ///
        /// The main road goes down first: the layout declares it, and it runs the whole
        /// width of the core whatever stands beside it. The rest are read off the free
        /// ground - every straight strip of a road's width whose whole run is clear of
        /// blocks - and the widest and longest of them are taken first, each one barring
        /// the ground it covers to any other road along the same axis. Where the ground a
        /// road wants is already spoken for, the road goes down in PIECES round it, never
        /// dropped whole: the length beyond the obstruction is the length that serves the
        /// blocks out there. Two roads that cross keep both claims: that is a junction. No
        /// boulevard is looked for: the core has one, the main road; a gap wide enough for a
        /// second is a street with ground left over beside it.
        ///
        /// The alleys are read off the ground too, not laid from
        /// <see cref="CoreLayout.Lanes"/> - that table says which way an alley runs, not
        /// that there is one. An alley the cuts have opened onto a street is no longer an
        /// alley: no road may be laid against the flank of another, because two roads with
        /// no kerb between them read as one wide road with arrows painted down half of it.
        ///
        /// What is not a road, however road-shaped it looks:
        /// - a strip walled in by block at BOTH ends. It goes nowhere. (One end is normal:
        ///   a street runs out to the edge of the core, or stops at a block's face.)
        /// - a strip with another road of the same axis along either flank.
        /// - a strip with no kerb down either side, which is the middle of a car park.
        /// - a strip three-quarters made of one block's bay, which is that block's car park.
        ///   A street may cross a bay - the demo runs three of them through one - but it is
        ///   not made of one. An alley is excused: walled by block on both sides is what an
        ///   alley IS, bay or no bay.
        ///
        /// An alley runs only as far as it is walled, not as far as the ground is clear:
        /// where the blocks either side stop, the alley has its mouth.
        /// </summary>
        static List<Corridor> Roads(Kind[,] kinds, int w, int h, Raster r, Vector2 band)
        {
            var roads = new List<Corridor>();
            var takenNS = new bool[w, h];
            var takenEW = new bool[w, h];

            bool Ours(int i, int j) => i >= 0 && j >= 0 && i < w && j < h;
            bool Free(int i, int j) => Ours(i, j) && kinds[i, j] != Kind.Block && kinds[i, j] != Kind.Outside;
            bool Kerb(int i, int j) => Ours(i, j) && kinds[i, j] == Kind.Block;
            bool Yard(int i, int j) => Ours(i, j) && kinds[i, j] == Kind.Yard;
            void Spot(bool vertical, int a, int t, int k, out int i, out int j)
            {
                if (vertical) { i = a + t; j = k; } else { i = k; j = a + t; }
            }
            bool Clear(bool vertical, int a, int width, int k)
            {
                for (int t = 0; t < width; t++)
                {
                    Spot(vertical, a, t, k, out int i, out int j);
                    if (!Free(i, j)) return false;
                }
                return true;
            }
            // how far a road of this width carries on. A road two cells wide or more runs as
            // far as the ground is clear; an ALLEY is the walled slot itself and no further -
            // where the blocks either side stop, the alley has its mouth. Measured against
            // the whole run of free ground instead, an alley would fail its own test, because
            // the ground at its mouth is the street it opens onto
            bool Runs(bool vertical, int a, int width, int k)
            {
                if (!Clear(vertical, a, width, k)) return false;
                if (width > 1) return true;
                Spot(vertical, a, -1, k, out int li, out int lj);
                Spot(vertical, a, 1, k, out int ri, out int rj);
                return Kerb(li, lj) && Kerb(ri, rj);
            }
            bool Vacant(bool vertical, int a, int width, int k)
            {
                var taken = vertical ? takenNS : takenEW;
                for (int t = 0; t < width; t++)
                {
                    Spot(vertical, a, t, k, out int i, out int j);
                    if (!Free(i, j) || taken[i, j]) return false;
                }
                return true;
            }
            // the row beyond a run's end: the road carries on into more ground, or leaves
            // the core - the edge of the drawing and the ground beyond it are both where
            // the core meets the rest of the city. A run that stops against a wall of
            // block at BOTH ends goes nowhere
            bool Opens(bool vertical, int a, int width, int k)
            {
                if (k < 0 || k >= (vertical ? h : w)) return true;
                for (int t = 0; t < width; t++)
                {
                    Spot(vertical, a, t, k, out int i, out int j);
                    if (!Kerb(i, j)) return true;
                }
                return false;
            }
            // may this 5 m length of the road go down? Its ground has to be clear and
            // unspoken for, and neither flank may be another road of the same axis: with no
            // kerb between them the two would read as one wide road, and an alley laid that
            // way paints its arrows down the side of a street
            bool Layable(Corridor road, int k)
            {
                var taken = road.Vertical ? takenNS : takenEW;
                for (int t = 0; t < road.W; t++)
                {
                    Spot(road.Vertical, road.A, t, k, out int i, out int j);
                    if (!Free(i, j) || taken[i, j]) return false;
                }
                Spot(road.Vertical, road.A, -1, k, out int li, out int lj);
                Spot(road.Vertical, road.A, road.W, k, out int ri, out int rj);
                return !(Ours(li, lj) && taken[li, lj]) && !(Ours(ri, rj) && taken[ri, rj]);
            }
            void Take(Corridor road)
            {
                var taken = road.Vertical ? takenNS : takenEW;
                for (int k = road.From; k < road.To; k++)
                    for (int t = 0; t < road.W; t++)
                    {
                        Spot(road.Vertical, road.A, t, k, out int i, out int j);
                        taken[i, j] = true;
                    }
                roads.Add(road);
            }
            // a declared road goes down over every clear stretch of the line it names
            void Declare(bool vertical, int a, int width, int from, int to)
            {
                if (width < 1 || a < 0 || a + width > (vertical ? w : h)) return;
                int along = vertical ? h : w;
                from = Mathf.Max(0, from);
                to = Mathf.Min(along, to);
                for (int k = from; k < to;)
                {
                    if (!Vacant(vertical, a, width, k)) { k++; continue; }
                    int start = k;
                    while (k < to && Vacant(vertical, a, width, k)) k++;
                    Take(new Corridor
                    {
                        Vertical = vertical, A = a, W = width, From = start, To = k, Declared = true,
                    });
                }
            }

            // the main road, the whole width of the core, boulevard-wide
            Declare(false, Mathf.RoundToInt((band.x - r.Z0) / Cell),
                    Mathf.RoundToInt((band.y - band.x) / Cell), 0, w);
            // and the rest, off the free ground. There is no boulevard among them: the
            // core has one, the main road, and it is declared. A gap wide enough for
            // another is a street with ground left over beside it
            var found = new List<Corridor>();
            foreach (int width in new[] { StreetCells, 2, 1 })
                for (int axis = 0; axis < 2; axis++)
                {
                    bool vertical = axis == 0;
                    int across = vertical ? w : h, along = vertical ? h : w;
                    for (int a = 0; a + width <= across; a++)
                        for (int k = 0; k < along;)
                        {
                            if (!Runs(vertical, a, width, k)) { k++; continue; }
                            int start = k;
                            while (k < along && Runs(vertical, a, width, k)) k++;
                            if (k - start < Mathf.Max(2, width)) continue;
                            if (!Opens(vertical, a, width, start - 1) && !Opens(vertical, a, width, k)) continue;
                            int kerbs = 0, yard = 0;
                            for (int m = start; m < k; m++)
                            {
                                Spot(vertical, a, -1, m, out int li, out int lj);
                                Spot(vertical, a, width, m, out int ri, out int rj);
                                if (Kerb(li, lj) || Kerb(ri, rj)) kerbs++;
                                for (int t = 0; t < width; t++)
                                {
                                    Spot(vertical, a, t, m, out int i, out int j);
                                    if (Yard(i, j)) yard++;
                                }
                            }
                            if (kerbs == 0) continue;   // no kerb either side: a lot, not a road
                            // a strip that is nearly all one block's bay is that block's car
                            // park, however road-shaped it looks. A street may cross a bay -
                            // the demo runs three of them through one - not be made of one.
                            // An alley is excused: walled by block on both sides is what an
                            // alley IS, and the demo tucks several of them into a block's bay
                            if (width > 1 && yard * 4 >= (k - start) * width * 3) continue;
                            found.Add(new Corridor
                            {
                                Vertical = vertical, A = a, W = width, From = start, To = k, Kerbs = kerbs,
                            });
                        }
                }
            // the WIDEST first, and only then the longest. Width before length, because the
            // city wants streets: a 10 m road that happens to run the length of the core
            // would otherwise take the ground out from under every 15 m street that crosses
            // its band, and leave the blocks along it with a narrow road for a frontage.
            // Where two are the same, the one with more kerb against it; the rest of the
            // order only keeps the draw repeatable
            found.Sort((one, other) =>
            {
                int by = other.W.CompareTo(one.W);
                if (by == 0) by = other.Cells.CompareTo(one.Cells);
                if (by == 0) by = other.Kerbs.CompareTo(one.Kerbs);
                if (by == 0) by = other.Length.CompareTo(one.Length);
                if (by == 0) by = other.Vertical.CompareTo(one.Vertical);
                if (by == 0) by = one.A.CompareTo(other.A);
                return by == 0 ? one.From.CompareTo(other.From) : by;
            });
            // A candidate is laid in PIECES round whatever has been taken since it was
            // gathered, never dropped whole. Dropping it whole is what leaves a block with
            // no road down one side: the east-west street north of block-12 stops against
            // block-16, which stands five metres deeper than block-12 does, and the length
            // of it beyond - the length that serves block-15 - would go with it.
            foreach (var road in found)
                for (int k = road.From; k < road.To;)
                {
                    if (!Layable(road, k)) { k++; continue; }
                    int start = k;
                    while (k < road.To && Layable(road, k)) k++;
                    if (k - start < Mathf.Max(2, road.W)) continue;
                    if (!Opens(road.Vertical, road.A, road.W, start - 1) &&
                        !Opens(road.Vertical, road.A, road.W, k)) continue;
                    int kerbs = 0;
                    for (int m = start; m < k; m++)
                    {
                        Spot(road.Vertical, road.A, -1, m, out int li, out int lj);
                        Spot(road.Vertical, road.A, road.W, m, out int ri, out int rj);
                        if (Kerb(li, lj) || Kerb(ri, rj)) kerbs++;
                    }
                    if (kerbs == 0) continue;
                    Take(new Corridor
                    {
                        Vertical = road.Vertical, A = road.A, W = road.W, From = start, To = k, Kerbs = kerbs,
                    });
                }
            return roads;
        }

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

        /// <summary>Ground a road runs over. Left-over ground is not road: nothing is stood
        /// on it, so nothing opens onto it either - no zebra faces it, no kerb closes a car
        /// park off from it.</summary>
        public static bool IsRoad(Kind k) => k != Kind.Outside && k != Kind.Block && k != Kind.Spare;

        static void Count(Raster r)
        {
            int blocks = 0, road = 0, parking = 0, spare = 0;
            for (int i = 0; i < r.NX; i++)
                for (int j = 0; j < r.NZ; j++)
                {
                    var k = r.Kinds[i, j];
                    if (k == Kind.Block) blocks++;
                    else if (k == Kind.Parking) parking++;
                    else if (k == Kind.Spare) spare++;
                    else if (IsRoad(k)) road++;
                }
            int cell = Mathf.RoundToInt(Cell * Cell);
            r.BlockArea = blocks * cell;
            r.RoadArea = road * cell;
            r.ParkingArea = parking * cell;
            r.SpareArea = spare * cell;
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
                        Kind.Block => '#', Kind.Bare => '.', Kind.Yard => 'y', Kind.Parking => 'P', Kind.Spare => ':',
                        Kind.StreetEW => '-', Kind.StreetNS => '|', Kind.BlvdEW => '=', Kind.BlvdNS => 'H',
                        Kind.NarrowEW => '~', Kind.NarrowNS => '!', Kind.LaneEW => (r.Dir[i, j] > 0 ? '>' : '<'),
                        Kind.LaneNS => (r.Dir[i, j] > 0 ? '^' : 'v'), _ => ' ',
                    });
                }
                map.Append((char)10);
            }
            return map.ToString();
        }

        /// <summary>What the drawing came to: the roads in a line, then every patch of
        /// ground they left over and where it is, the 10 m roads where the city wants a
        /// street, and the alleys with no direction on record. This list is what the cuts
        /// in <see cref="CoreLayout"/> are argued about.</summary>
        static string Oddities(Raster r, IReadOnlyList<CoreLayout.Block> blocks, List<Corridor> roads,
                               int unmatchedLanes, List<string> unmatchedAt)
        {
            var sb = new StringBuilder();
            foreach (var block in blocks)
            {
                var sides = Unserved(r, block);
                if (sides.Count > 0)
                    sb.AppendLine($"   WARNING: {block.Name} has no road along its {string.Join(" or ", sides)} side" +
                                  (sides.Count > 1 ? "s" : ""));
            }
            int blvd = 0, street = 0, narrow = 0, alley = 0, declared = 0;
            foreach (var road in roads)
            {
                if (road.W == BlvdCells) blvd++;
                else if (road.W == StreetCells) street++;
                else if (road.W == 2) narrow++;
                else alley++;
                if (road.Declared) declared++;
            }
            sb.AppendLine($"   {roads.Count} roads: {blvd} boulevard, {street} street, {narrow} of 10 m, " +
                          $"{alley} alley ({declared} of them the layout's own)");

            // the ground left over, biggest first: what a cut has still to squeeze out, or
            // what has to be given something to stand on
            var spare = Lots(r, Kind.Spare);
            spare.Sort((one, other) => other.Count.CompareTo(one.Count));
            if (spare.Count > 0)
                sb.AppendLine($"   {spare.Count} patches of ground left bare, {r.SpareArea} m2 in all - " +
                              "nothing is stood on them, biggest first:");
            for (int lot = 0; lot < spare.Count; lot++)
            {
                if (lot == Listed)
                {
                    sb.AppendLine($"   ... and {spare.Count - lot} smaller");
                    break;
                }
                sb.AppendLine($"   left bare: {spare[lot].Count} cells at {Where(r, spare[lot])}");
            }
            foreach (var lot in Lots(r, Kind.Bare))
                if (lot.Count > BlvdCells * BlvdCells)
                    sb.AppendLine($"   a junction wider than two boulevards: {lot.Count} cells at {Where(r, lot)}");

            // a length of road too short to carry its own profile, caught between two
            // junctions: the drawing there is a stub of markings, not a road
            foreach (var stub in Stubs(r))
                sb.AppendLine($"   WARNING: {stub}");
            int narrowCells = 0;
            for (int i = 0; i < r.NX; i++)
                for (int j = 0; j < r.NZ; j++)
                    if (r.Kinds[i, j] == Kind.NarrowEW || r.Kinds[i, j] == Kind.NarrowNS) narrowCells++;
            if (narrowCells > 0)
                sb.AppendLine($"   10 m road: {narrowCells} cells (the city wants 15 m streets; a cut would open them)");
            if (unmatchedLanes > 0)
                sb.AppendLine($"   alleys with no direction on record: {unmatchedLanes} cells, run north/east: " +
                              string.Join(" ", unmatchedAt) + (unmatchedLanes > unmatchedAt.Count ? " ..." : ""));
            if (r.Clashes > 0) sb.AppendLine($"   WARNING: {r.Clashes} cell(s) claimed by two blocks");
            return sb.ToString().TrimEnd();
        }

        /// <summary>How many patches of left-over ground the report names before it counts
        /// the rest.</summary>
        const int Listed = 20;

        /// <summary>How far off a block's face a road still counts as that side's road: the
        /// block's own kerb-side parking, or a strip of ground left over, may stand between
        /// the two and the side is still served.</summary>
        static int Served => StreetCells + 1;

        /// <summary>
        /// The sides of a block no road runs along. A block in a city has a street down each
        /// of its four sides; one that has not is either walled in by its neighbour or left
        /// where a road was dropped, and either way it is a fault in the drawing - so this
        /// is the last thing the report says about the blocks.
        ///
        /// A side is served if, looking straight out from the block's face, a road is met
        /// within <see cref="Served"/> cells with no other block in the way.
        /// </summary>
        static List<string> Unserved(Raster r, CoreLayout.Block block)
        {
            int i0 = Mathf.RoundToInt((block.Box.xMin - r.X0) / Cell);
            int j0 = Mathf.RoundToInt((block.Box.yMin - r.Z0) / Cell);
            var sides = new List<string>();
            foreach (var (di, dj, name) in new[] { (-1, 0, "west"), (1, 0, "east"), (0, -1, "south"), (0, 1, "north") })
            {
                bool served = false, any = false;
                // every cell of the block's face along this side, and the ground straight out
                for (int i = 0; i < block.CW && !served; i++)
                    for (int j = 0; j < block.CD && !served; j++)
                    {
                        if (!block.Mask[i, j]) continue;
                        if (block.Mask[Mathf.Clamp(i + di, 0, block.CW - 1), Mathf.Clamp(j + dj, 0, block.CD - 1)]
                            && i + di >= 0 && j + dj >= 0 && i + di < block.CW && j + dj < block.CD) continue;
                        any = true;
                        for (int step = 1; step <= Served; step++)
                        {
                            var kind = r.At(i0 + i + step * di, j0 + j + step * dj);
                            // the block's own kerb-side parking may stand between its face
                            // and the street; ground left bare may not - that IS the fault
                            if (kind == Kind.Block || kind == Kind.Spare || kind == Kind.Outside) break;
                            if (!IsRoad(kind)) continue;
                            served = true;
                            break;
                        }
                    }
                if (any && !served) sides.Add(name);
            }
            return sides;
        }

        /// <summary>
        /// Lengths of road too short to mean anything: one 5 m length caught between two
        /// junctions, or between a junction and the block the road stops at. A street's
        /// profile needs room to read - yellow line, white line, a parked car - and one
        /// length of it between two zebras reads as a mistake, which is what it is.
        /// </summary>
        static List<string> Stubs(Raster r)
        {
            var found = new List<string>();
            for (int i = 0; i < r.NX; i++)
                for (int j = 0; j < r.NZ; j++)
                {
                    var kind = r.Kinds[i, j];
                    bool ns = kind == Kind.StreetNS || kind == Kind.NarrowNS || kind == Kind.BlvdNS;
                    bool ew = kind == Kind.StreetEW || kind == Kind.NarrowEW || kind == Kind.BlvdEW;
                    if (!ns && !ew) continue;
                    if (r.Across[i, j] != 0) continue;         // once for each length of road
                    int di = ns ? 0 : 1, dj = ns ? 1 : 0;      // along the road
                    if (r.At(i - di, j - dj) == kind || r.At(i + di, j + dj) == kind) continue;
                    // a length at the edge of the drawing is not hemmed in: that is where
                    // the core hands the road to the city grid
                    if (i - di < 0 || j - dj < 0 || i + di >= r.NX || j + dj >= r.NZ) continue;
                    found.Add($"a {(ns ? "north-south" : "east-west")} road has one 5 m length of itself " +
                              $"at x {r.X(i):F0} z {r.Z(j):F0}, hemmed in at both ends");
                }
            return found;
        }

        /// <summary>Every patch of touching cells of one kind.</summary>
        static List<List<Vector2Int>> Lots(Raster r, Kind kind)
        {
            var lots = new List<List<Vector2Int>>();
            var seen = new bool[r.NX, r.NZ];
            for (int i = 0; i < r.NX; i++)
                for (int j = 0; j < r.NZ; j++)
                {
                    if (r.Kinds[i, j] != kind || seen[i, j]) continue;
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
                            if (r.Kinds[n.x, n.y] != kind || seen[n.x, n.y]) continue;
                            seen[n.x, n.y] = true;
                            todo.Enqueue(n);
                        }
                    }
                    lots.Add(lot);
                }
            return lots;
        }

        /// <summary>Where a patch of cells lies, in metres.</summary>
        static string Where(Raster r, List<Vector2Int> lot)
        {
            int x0 = int.MaxValue, x1 = int.MinValue, z0 = int.MaxValue, z1 = int.MinValue;
            foreach (var c in lot)
            {
                x0 = Mathf.Min(x0, c.x); x1 = Mathf.Max(x1, c.x);
                z0 = Mathf.Min(z0, c.y); z1 = Mathf.Max(z1, c.y);
            }
            return $"x {r.X(x0):F0}..{r.X(x1 + 1):F0} z {r.Z(z0):F0}..{r.Z(z1 + 1):F0}";
        }

        // ------------------------------------------------------------------ the tiles

        /// <summary>
        /// Lays the tiles. A road is laid a 5 m length at a time across its whole width -
        /// the builder's own profiles: a street is parking strip, the two facing halves
        /// (each with its yellow line on the crown and its white line on its own kerb),
        /// parking strip; a boulevard its kerb lanes, dashed inner lanes, median and
        /// divider; a narrow road the two halves alone; an alley bare asphalt with an
        /// arrow at each mouth. The length that opens onto a junction is the zebra. Which
        /// cell of a road's width a length is laid from is the raster's to say
        /// (<see cref="Raster.Across"/>); junctions and left-over ground are plain
        /// asphalt, car parks rows of painted bays.
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
            // a length of road whose cells are not all of one kind is laid bare across its
            // whole width - it is a drawing that has gone wrong, but no cell of it is left
            // without a tile
            void Spread(int i, int j, int di, int dj, int n)
            {
                for (int k = 0; k < n; k++)
                {
                    int a = i + k * di, b = j + k * dj;
                    if (a >= 0 && b >= 0 && a < r.NX && b < r.NZ) kit.Tile(Bare, r.X(a), r.Z(b), 0, Cell, Cell);
                }
            }

            for (int i = 0; i < r.NX; i++)
                for (int j = 0; j < r.NZ; j++)
                {
                    float mx = r.X(i), mz = r.Z(j);
                    switch (kinds[i, j])
                    {
                        case Kind.Bare:
                        case Kind.Yard:
                            kit.Tile(Bare, mx, mz, 0, Cell, Cell);
                            break;

                        case Kind.Spare:
                            break;      // left bare on purpose: the report says where it is

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
                            if (r.Across[i, j] != 0) break;                   // laid from its south cell
                            if (!Same(i, j, 0, 1, 2)) { Spread(i, j, 0, 1, 2); break; }
                            kit.Tile(RoadHalfTile, mx, mz, 270, Cell, Cell);
                            kit.Tile(RoadHalfTile, mx, mz + Cell, 90, Cell, Cell);
                            break;

                        case Kind.NarrowNS:
                            if (r.Across[i, j] != 0) break;                   // laid from its west cell
                            if (!Same(i, j, 1, 0, 2)) { Spread(i, j, 1, 0, 2); break; }
                            kit.Tile(RoadHalfTile, mx, mz, 0, Cell, Cell);
                            kit.Tile(RoadHalfTile, mx + Cell, mz, 180, Cell, Cell);
                            break;

                        case Kind.StreetEW:
                        {
                            if (r.Across[i, j] != 0) break;                       // laid from its south cell
                            if (!Same(i, j, 0, 1, sc)) { Spread(i, j, 0, 1, sc); break; }
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
                            if (r.Across[i, j] != 0) break;                       // laid from its west cell
                            if (!Same(i, j, 1, 0, sc)) { Spread(i, j, 1, 0, sc); break; }
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
                            if (r.Across[i, j] != 0) break;                       // laid from its west cell
                            if (!Same(i, j, 1, 0, bc)) { Spread(i, j, 1, 0, bc); break; }
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
                            if (r.Across[i, j] != 0) break;                       // laid from its south cell
                            if (!Same(i, j, 0, 1, bc)) { Spread(i, j, 0, 1, bc); break; }
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
            foreach (var lot in Lots(r, Kind.Parking)) LayLot(r, kit, lot);
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

        /// <summary>A car for the quarter's traffic, out of the same pool the car parks
        /// draw on - the catalogue's road cars, the wrong decade and the marked liveries
        /// left out, weighted as the city weights its own pool.</summary>
        public static GameObject PickCar(System.Random dice) => Cars.Pick(dice);

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
