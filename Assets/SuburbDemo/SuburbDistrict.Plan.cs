using System.Collections.Generic;
using RoadDemo;
using UnityEngine;

namespace SuburbDemo
{
    // The plan: where the streets run (a grid, some lines ending in a cul-de-sac),
    // which 5 m cells are road, zebra, pavement or free, and how the free cells are
    // cut into lots off the street frontages. Everything later reads the cell grid.
    public partial class SuburbDistrict
    {
        enum CellKind : byte { Free, Road, Zebra, Sidewalk, Wrap, Cap, Lot }
        enum Surface : byte { Grass, Grass2, Driveway, Path, PathDoor, Concrete, Asphalt, Parking, Dirt, None }
        enum SwVariant : byte { Plain, Crossing, Driveway, Path }
        public enum LotUse { House, Church, Shops, Park }

        class Node
        {
            public int I, J;
            public float X, Z;
            public bool S, N, W, E;
            public int Arms => (S ? 1 : 0) + (N ? 1 : 0) + (W ? 1 : 0) + (E ? 1 : 0);
            public RoadNode Road;
            public readonly PedNode[] Corner = new PedNode[4]; // NE, NW, SW, SE
            public TrafficSignal Signal;
        }

        class Segment
        {
            public bool Vertical;
            public float Axis;      // x of a vertical street, z of a horizontal one
            public float Lo, Hi;    // carriageway extent along the axis, junction box face to box face (or to the dead end)
            public Node LoNode, HiNode;
            public bool Stub;       // a cul-de-sac: one end is a turning bulb, not a junction
            public bool DeadHi;     // which end of a stub is the bulb
            public float BulbLo => DeadHi ? Hi - 2f * BulbHalf : Lo;
            public float BulbHi => DeadHi ? Hi : Lo + 2f * BulbHalf;
            public readonly List<RoadEdge> Edges = new List<RoadEdge>();
        }

        class Cut { public bool Vertical; public int Line, Gap; public bool FromLow; }

        public class Lot
        {
            public int Index;
            public int Cx0, Cz0, Cx1, Cz1;       // cell range, exclusive upper
            public Vector3 Front;                // towards the street it faces
            public int WidthCells, DepthCells;
            public bool CornerLeft, CornerRight; // a street down that side too
            public bool Stub;                    // faces a cul-de-sac
            public LotUse Use = LotUse.House;
            public Vector3 Origin, Along, In;    // lot frame: Origin at the front-left corner seen from the street
            public float W => WidthCells * Cell;
            public float D => DepthCells * Cell;
            public Vector3 P(float u, float v) => Origin + Along * u + In * v;
            public GameObject House;
            public GameObject HousePrefab;
            public Vector3 HousePivot;
            public int HouseYaw;
            public int YardIndex = -1;
            public Material Palette;
            public Vector3 DoorPos, DoorOut;
            public bool HasDoor;
            public readonly List<Rect> Taken = new List<Rect>(); // world XZ footprints already standing on the lot
            public readonly List<Vector3> IdleSpots = new List<Vector3>();
        }

        float[] _vx, _hz;                 // street axes
        readonly List<Cut> _cuts = new List<Cut>();
        Node[,] _nodes;
        readonly List<Node> _nodeList = new List<Node>();
        readonly List<Segment> _segments = new List<Segment>();

        int _w, _h;                       // cells
        CellKind[,] _kind;
        Surface[,] _surface;
        int[,] _surfYaw;
        SwVariant[,] _swVariant;
        int[,] _zebraYaw;
        bool[,] _stubSide;
        int[,] _lotOf;
        readonly List<Lot> _lots = new List<Lot>();

        public float MapWidth => _vx[_vx.Length - 1] + StreetHalf + Walk;
        public float MapHeight => _hz[_hz.Length - 1] + StreetHalf + Walk;

        // ------------------------------------------------------------ lines

        void PlanLines()
        {
            var lens = new List<int>();
            var pins = new List<int>();

            if (_links == null)
            {
                // on its own: the blocks are simply dealt out of the palette
                for (int k = 0; k < columns; k++) lens.Add(Mathf.Max(60, Mathf.RoundToInt(blockLengths[k % blockLengths.Length] / 5f) * 5));
                for (int k = lens.Count - 1; k > 0; k--) { int r = _rng.Next(k + 1); (lens[k], lens[r]) = (lens[r], lens[k]); }
                pins.Add(Mathf.Min(1, columns));
            }
            else
            {
                // hung off the city: a street line has to land on every link, so the
                // blocks between two links are cut to fit the gap exactly rather than
                // dealt from the palette. A flank block stands outside the outer links
                // so the suburb does not end on its own approach road.
                lens.Add(FlankLength());
                pins.Add(1);
                for (int k = 0; k + 1 < _links.Length; k++)
                {
                    var run = SplitRun(_links[k + 1] - _links[k]);
                    lens.AddRange(run);
                    pins.Add(pins[pins.Count - 1] + run.Length);
                }
                lens.Add(FlankLength());
                columns = lens.Count;
            }
            _pinColumns = pins.ToArray();

            _vx = new float[columns + 1];
            _hz = new float[rows + 1];
            _vx[0] = Walk + StreetHalf;
            for (int i = 0; i < columns; i++) _vx[i + 1] = _vx[i] + 2f * StreetHalf + 2f * Walk + lens[i];
            float depth = Mathf.Round(lotDepth / Cell) * Cell;
            _hz[0] = Walk + StreetHalf;
            for (int j = 0; j < rows; j++) _hz[j + 1] = _hz[j] + 2f * StreetHalf + 2f * Walk + 2f * depth;

            _w = Mathf.RoundToInt(MapWidth / Cell);
            _h = Mathf.RoundToInt(MapHeight / Cell);
            _kind = new CellKind[_w, _h];
            _surface = new Surface[_w, _h];
            _surfYaw = new int[_w, _h];
            _swVariant = new SwVariant[_w, _h];
            _zebraYaw = new int[_w, _h];
            _stubSide = new bool[_w, _h];
            _lotOf = new int[_w, _h];
            for (int x = 0; x < _w; x++)
                for (int z = 0; z < _h; z++) _lotOf[x, z] = -1;
        }

        int FlankLength()
            => Mathf.Max(60, Mathf.RoundToInt(blockLengths[_rng.Next(blockLengths.Length)] / 5f) * 5);

        /// <summary>The blocks that fill the gap between two of the city's road lines
        /// exactly. A block costs its own length plus the 20 m of street and pavement
        /// beside it, and every length is a multiple of 5 - which the gap is too, since
        /// the city's own spacings are (that is why a suburb's links are five road lines
        /// apart). How many blocks: as near 105 m apiece as the gap allows, and never so
        /// few or so many that one falls outside 70 - 100 m.</summary>
        int[] SplitRun(float gap)
        {
            int g = Mathf.RoundToInt(gap / 5f) * 5;
            if (Mathf.Abs(g - gap) > 0.51f)
                Debug.LogWarning($"[Suburb] a link {gap:F1} m out is not on the 5 m lattice - the street " +
                                 $"will land {Mathf.Abs(g - gap):F1} m off the city's line.");
            int n = Mathf.Clamp(Mathf.RoundToInt(g / 105f), Mathf.CeilToInt(g / 120f), Mathf.Max(1, g / 90));
            n = Mathf.Max(1, n);
            int total = g - 20 * n;
            while (n > 1 && total < 45 * n) { n--; total = g - 20 * n; }
            if (total < 20) { total = 20; }
            var lens = new int[n];
            int each = (total / n) / 5 * 5;
            int left = total - each * n;
            for (int i = 0; i < n; i++) lens[i] = each;
            for (int i = 0; left >= 5; i++, left -= 5) lens[i % n] += 5;
            return lens;
        }

        /// <summary>The suburb's own end of a connecting street: the junction on the far
        /// side of the map gets its north arm, which lays the zebra out to the boundary
        /// where the city's street arrives.</summary>
        void OpenGates()
        {
            if (_links == null) return;
            foreach (int p in _pinColumns)
            {
                if (p < 0 || p > columns) continue;
                var n = _nodes[p, rows];
                if (n == null) continue;
                n.N = true;
            }
        }

        // A cul-de-sac is an interior street line that, in one gap between two
        // crossing streets, runs in from one junction and ends in a turning bulb
        // instead of reaching the next. No line is cut twice and no block hosts two.
        void PlanCuts()
        {
            var candidates = new List<Cut>();
            for (int i = 1; i < columns; i++)
                for (int j = 0; j < rows; j++)
                {
                    candidates.Add(new Cut { Vertical = true, Line = i, Gap = j, FromLow = true });
                    candidates.Add(new Cut { Vertical = true, Line = i, Gap = j, FromLow = false });
                }
            for (int j = 1; j < rows; j++)
                for (int i = 0; i < columns; i++)
                {
                    candidates.Add(new Cut { Vertical = false, Line = j, Gap = i, FromLow = true });
                    candidates.Add(new Cut { Vertical = false, Line = j, Gap = i, FromLow = false });
                }
            for (int k = candidates.Count - 1; k > 0; k--) { int r = _rng.Next(k + 1); (candidates[k], candidates[r]) = (candidates[r], candidates[k]); }

            var usedBlocks = new HashSet<(int, int)>();
            var usedLines = new HashSet<(bool, int)>();
            foreach (var c in candidates)
            {
                if (_cuts.Count >= culDeSacs) break;
                if (usedLines.Contains((c.Vertical, c.Line))) continue;
                // never a cul-de-sac on a line the city's street arrives on: the junction
                // at the top of it would lose its south arm, and with it the whole
                // suburb's way in (BuildPortals found no node there)
                if (c.Vertical && IsPinned(c.Line)) continue;
                var blocks = c.Vertical
                    ? new[] { (c.Line - 1, c.Gap), (c.Line, c.Gap) }
                    : new[] { (c.Gap, c.Line - 1), (c.Gap, c.Line) };
                if (usedBlocks.Contains(blocks[0]) || usedBlocks.Contains(blocks[1])) continue;
                // the bulb and its pavement ring must fit short of the far street's pavement
                float gapLen = c.Vertical ? _hz[c.Gap + 1] - _hz[c.Gap] : _vx[c.Gap + 1] - _vx[c.Gap];
                if (gapLen - 2f * StreetHalf - 2f * Walk < stubLength + 2f * BulbHalf + Walk) continue;
                _cuts.Add(c);
                usedLines.Add((c.Vertical, c.Line));
                usedBlocks.Add(blocks[0]);
                usedBlocks.Add(blocks[1]);
            }
        }

        /// <summary>A street line the city's own road runs out to.</summary>
        bool IsPinned(int column)
        {
            if (_links == null || _pinColumns == null) return false;
            for (int i = 0; i < _pinColumns.Length; i++) if (_pinColumns[i] == column) return true;
            return false;
        }

        Cut CutAt(bool vertical, int line, int gap)
        {
            foreach (var c in _cuts)
                if (c.Vertical == vertical && c.Line == line && c.Gap == gap) return c;
            return null;
        }

        // ------------------------------------------------------------ nodes, segments

        void BuildNetwork()
        {
            _nodes = new Node[columns + 1, rows + 1];
            for (int i = 0; i <= columns; i++)
                for (int j = 0; j <= rows; j++)
                {
                    var n = new Node { I = i, J = j, X = _vx[i], Z = _hz[j] };
                    // a vertical arm is there if the gap beside is uncut, or its stub starts here
                    Cut cs = j > 0 ? CutAt(true, i, j - 1) : null;
                    Cut cn = j < rows ? CutAt(true, i, j) : null;
                    Cut cw = i > 0 ? CutAt(false, j, i - 1) : null;
                    Cut ce = i < columns ? CutAt(false, j, i) : null;
                    n.S = j > 0 && (cs == null || !cs.FromLow);
                    n.N = j < rows && (cn == null || cn.FromLow);
                    n.W = i > 0 && (cw == null || !cw.FromLow);
                    n.E = i < columns && (ce == null || ce.FromLow);
                    bool perpendicular = (n.S || n.N) && (n.W || n.E);
                    if (n.Arms >= 3 || (n.Arms == 2 && perpendicular))
                    {
                        _nodes[i, j] = n;
                        _nodeList.Add(n);
                    }
                }

            // vertical lines
            for (int i = 0; i <= columns; i++)
            {
                Node open = null;
                for (int j = 0; j < rows; j++)
                {
                    var cut = CutAt(true, i, j);
                    if (cut != null)
                    {
                        var seg = new Segment { Vertical = true, Axis = _vx[i], Stub = true, DeadHi = cut.FromLow };
                        if (cut.FromLow)
                        {
                            seg.LoNode = _nodes[i, j];
                            seg.Lo = _hz[j] + StreetHalf;
                            seg.Hi = seg.Lo + stubLength + 2f * BulbHalf;
                        }
                        else
                        {
                            seg.HiNode = _nodes[i, j + 1];
                            seg.Hi = _hz[j + 1] - StreetHalf;
                            seg.Lo = seg.Hi - stubLength - 2f * BulbHalf;
                        }
                        _segments.Add(seg);
                        open = null;
                        continue;
                    }
                    if (open == null) open = _nodes[i, j];
                    var next = _nodes[i, j + 1];
                    if (next != null && open != null)
                    {
                        _segments.Add(new Segment { Vertical = true, Axis = _vx[i], Lo = open.Z + StreetHalf, Hi = next.Z - StreetHalf, LoNode = open, HiNode = next });
                        open = next;
                    }
                }
            }
            // horizontal lines
            for (int j = 0; j <= rows; j++)
            {
                Node open = null;
                for (int i = 0; i < columns; i++)
                {
                    var cut = CutAt(false, j, i);
                    if (cut != null)
                    {
                        var seg = new Segment { Vertical = false, Axis = _hz[j], Stub = true, DeadHi = cut.FromLow };
                        if (cut.FromLow)
                        {
                            seg.LoNode = _nodes[i, j];
                            seg.Lo = _vx[i] + StreetHalf;
                            seg.Hi = seg.Lo + stubLength + 2f * BulbHalf;
                        }
                        else
                        {
                            seg.HiNode = _nodes[i + 1, j];
                            seg.Hi = _vx[i + 1] - StreetHalf;
                            seg.Lo = seg.Hi - stubLength - 2f * BulbHalf;
                        }
                        _segments.Add(seg);
                        open = null;
                        continue;
                    }
                    if (open == null) open = _nodes[i, j];
                    var next = _nodes[i + 1, j];
                    if (next != null && open != null)
                    {
                        _segments.Add(new Segment { Vertical = false, Axis = _hz[j], Lo = open.X + StreetHalf, Hi = next.X - StreetHalf, LoNode = open, HiNode = next });
                        open = next;
                    }
                }
            }
        }

        // ------------------------------------------------------------ cells

        int CellOf(float world) => Mathf.FloorToInt(world / Cell + 0.001f);
        bool InGrid(int cx, int cz) => cx >= 0 && cz >= 0 && cx < _w && cz < _h;

        void Mark(int cx, int cz, CellKind kind)
        {
            if (!InGrid(cx, cz)) return;
            // road beats pavement beats free; explicit pieces beat everything
            var cur = _kind[cx, cz];
            if (kind == CellKind.Sidewalk && (cur == CellKind.Road || cur == CellKind.Zebra || cur == CellKind.Wrap || cur == CellKind.Cap)) return;
            if (kind == CellKind.Road && (cur == CellKind.Zebra || cur == CellKind.Cap)) return;
            _kind[cx, cz] = kind;
        }

        // (along, across) of a street into a cell index pair
        (int, int) AC(Segment s, float along, float across)
            => s.Vertical ? (CellOf(s.Axis + across), CellOf(along)) : (CellOf(along), CellOf(s.Axis + across));

        void MarkStreets()
        {
            foreach (var s in _segments)
            {
                float straightLo = s.Lo, straightHi = s.Hi;
                if (s.Stub) { if (s.DeadHi) straightHi = s.BulbLo; else straightLo = s.BulbHi; }

                for (float a = straightLo; a < straightHi - 0.01f; a += Cell)
                {
                    var (x0, z0) = AC(s, a, -StreetHalf);
                    var (x1, z1) = AC(s, a, 0f);
                    Mark(x0, z0, CellKind.Road);
                    Mark(x1, z1, CellKind.Road);
                    // the pavement's last cell before a bulb belongs to the cap's L wrap
                    bool wrapCell = s.Stub && (s.DeadHi ? a >= straightHi - Cell - 0.01f : a < straightLo + Cell - 0.01f);
                    if (wrapCell) continue;
                    foreach (float across in new[] { -StreetHalf - Walk, StreetHalf })
                    {
                        var (sx, sz) = AC(s, a, across);
                        Mark(sx, sz, CellKind.Sidewalk);
                        if (s.Stub && InGrid(sx, sz)) _stubSide[sx, sz] = true;
                    }
                }
                if (s.Stub) MarkBulb(s);
            }

            foreach (var n in _nodeList) MarkNode(n);
        }

        void MarkBulb(Segment s)
        {
            float bLo = s.BulbLo, bHi = s.BulbHi;
            for (float a = bLo; a < bHi - 0.01f; a += Cell)
                for (float c = -BulbHalf; c < BulbHalf - 0.01f; c += Cell)
                {
                    var (x, z) = AC(s, a, c);
                    bool corner = (a < bLo + 0.01f || a > bHi - Cell - 0.01f) && (c < -BulbHalf + 0.01f || c > BulbHalf - Cell - 0.01f);
                    Mark(x, z, corner ? CellKind.Cap : CellKind.Road);
                }
            // the L wraps: three cells outside each corner cap
            foreach (var (ca, cc) in new[] { (bLo, -BulbHalf), (bLo, BulbHalf - Cell), (bHi - Cell, -BulbHalf), (bHi - Cell, BulbHalf - Cell) })
            {
                float da = ca < bLo + 0.01f ? -Cell : Cell;
                float dc = cc < 0f ? -Cell : Cell;
                foreach (var (a, c) in new[] { (ca + da, cc), (ca + da, cc + dc), (ca, cc + dc) })
                {
                    var (x, z) = AC(s, a, c);
                    if (InGrid(x, z)) { _kind[x, z] = CellKind.Wrap; _stubSide[x, z] = true; }
                }
            }
            // the ring's straight sides
            for (float a = bLo + Cell; a < bHi - Cell - 0.01f; a += Cell)
                foreach (float c in new[] { -BulbHalf - Cell, BulbHalf })
                {
                    var (x, z) = AC(s, a, c);
                    Mark(x, z, CellKind.Sidewalk);
                    if (InGrid(x, z)) _stubSide[x, z] = true;
                }
            float far = s.DeadHi ? bHi : bLo - Cell;
            for (float c = -Cell; c < Cell - 0.01f; c += Cell)
            {
                var (x, z) = AC(s, far, c);
                Mark(x, z, CellKind.Sidewalk);
                if (InGrid(x, z)) _stubSide[x, z] = true;
            }
        }

        void MarkNode(Node n)
        {
            int cx = CellOf(n.X), cz = CellOf(n.Z); // the box is cells cx-1..cx, cz-1..cz
            for (int dx = -1; dx <= 0; dx++)
                for (int dz = -1; dz <= 0; dz++)
                    Mark(cx + dx, cz + dz, CellKind.Road);

            void Approach(bool has, int ax, int az, bool alongX, int yawA, int yawB)
            {
                // the two cells of the arm just outside the box: zebra if the arm exists, pavement if not
                var (x0, z0) = alongX ? (ax, cz - 1) : (cx - 1, az);
                var (x1, z1) = alongX ? (ax, cz) : (cx, az);
                if (has)
                {
                    if (InGrid(x0, z0)) { _kind[x0, z0] = CellKind.Zebra; _zebraYaw[x0, z0] = yawA; }
                    if (InGrid(x1, z1)) { _kind[x1, z1] = CellKind.Zebra; _zebraYaw[x1, z1] = yawB; }
                    // the kerb cells beside the zebra carry the dropped kerb
                    var (f0x, f0z) = alongX ? (ax, cz - 2) : (cx - 2, az);
                    var (f1x, f1z) = alongX ? (ax, cz + 1) : (cx + 1, az);
                    foreach (var (fx, fz) in new[] { (f0x, f0z), (f1x, f1z) })
                    {
                        Mark(fx, fz, CellKind.Sidewalk);
                        if (InGrid(fx, fz) && _kind[fx, fz] == CellKind.Sidewalk) _swVariant[fx, fz] = SwVariant.Crossing;
                    }
                }
                else
                {
                    Mark(x0, z0, CellKind.Sidewalk);
                    Mark(x1, z1, CellKind.Sidewalk);
                }
            }
            // zebra tiles run along local X and their bars sit towards local +X; the demo
            // lays both tiles of a crossing at ONE yaw with the junction box on their +X,
            // so the bars of the two tiles line up: +X -> north for the south arm (270),
            // south for the north arm (90), east for the west arm (0), west for the east arm (180)
            Approach(n.S, 0, cz - 2, false, 270, 270);
            Approach(n.N, 0, cz + 1, false, 90, 90);
            Approach(n.W, cx - 2, 0, true, 0, 0);
            Approach(n.E, cx + 1, 0, true, 180, 180);
            // the four corner cells
            foreach (var (dx, dz) in new[] { (-2, -2), (1, -2), (-2, 1), (1, 1) })
                Mark(cx + dx, cz + dz, CellKind.Sidewalk);
        }

        // ------------------------------------------------------------ lots

        class Run
        {
            public List<(int cx, int cz)> Cells = new List<(int, int)>();
            public Vector3 Front;
            public bool Stub;
            public int Block;
            public bool CornerAtStart, CornerAtEnd;
            public int Length => Cells.Count;
        }

        bool IsPavement(int cx, int cz) => InGrid(cx, cz) && (_kind[cx, cz] == CellKind.Sidewalk || _kind[cx, cz] == CellKind.Wrap);
        bool IsFree(int cx, int cz) => InGrid(cx, cz) && _kind[cx, cz] == CellKind.Free;

        int[,] _block;

        void FloodBlocks()
        {
            _block = new int[_w, _h];
            for (int x = 0; x < _w; x++) for (int z = 0; z < _h; z++) _block[x, z] = -1;
            int id = 0;
            var stack = new Stack<(int, int)>();
            for (int x = 0; x < _w; x++)
                for (int z = 0; z < _h; z++)
                {
                    if (!IsFree(x, z) || _block[x, z] >= 0) continue;
                    stack.Push((x, z));
                    _block[x, z] = id;
                    while (stack.Count > 0)
                    {
                        var (cx, cz) = stack.Pop();
                        foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                        {
                            int nx = cx + dx, nz = cz + dz;
                            if (IsFree(nx, nz) && _block[nx, nz] < 0) { _block[nx, nz] = id; stack.Push((nx, nz)); }
                        }
                    }
                    id++;
                }
        }

        List<Run> FindRuns()
        {
            var runs = new List<Run>();
            // facing -Z (street to the south) and +Z: runs along x; facing +-X: runs along z
            foreach (var (front, dx, dz) in new[] { (Vector3.back, 0, -1), (Vector3.forward, 0, 1), (Vector3.left, -1, 0), (Vector3.right, 1, 0) })
            {
                bool alongX = dx == 0;
                int outer = alongX ? _h : _w, inner = alongX ? _w : _h;
                for (int o = 0; o < outer; o++)
                {
                    Run run = null;
                    for (int i = 0; i < inner; i++)
                    {
                        int cx = alongX ? i : o, cz = alongX ? o : i;
                        bool frontage = IsFree(cx, cz) && IsPavement(cx + dx, cz + dz);
                        if (frontage)
                        {
                            if (run == null) { run = new Run { Front = front, Block = _block[cx, cz] }; runs.Add(run); }
                            run.Cells.Add((cx, cz));
                            if (_stubSide[cx + dx, cz + dz]) run.Stub = true;
                        }
                        else if (run != null) { CloseRun(run, alongX); run = null; }
                    }
                    if (run != null) CloseRun(run, alongX);
                }
            }
            return runs;
        }

        void CloseRun(Run run, bool alongX)
        {
            var first = run.Cells[0];
            var last = run.Cells[run.Cells.Count - 1];
            run.CornerAtStart = alongX ? IsPavement(first.cx - 1, first.cz) : IsPavement(first.cx, first.cz - 1);
            run.CornerAtEnd = alongX ? IsPavement(last.cx + 1, last.cz) : IsPavement(last.cx, last.cz + 1);
        }

        struct Reservation { public Run Run; public int Start, Len, MaxDepth; public LotUse Use; }
        readonly List<Reservation> _reservations = new List<Reservation>();

        void CarveLots()
        {
            FloodBlocks();
            var runs = FindRuns();
            // cul-de-sac frontages first (they are short and must win their corner),
            // then the long street frontages, then whatever is left
            runs.Sort((a, b) => a.Stub != b.Stub ? (a.Stub ? -1 : 1) : b.Length.CompareTo(a.Length));
            ReservePlaces(runs);

            foreach (var run in runs)
            {
                // the run is dealt in pieces between its reservations
                var spans = new List<(int start, int len, LotUse use, int maxDepth)>();
                var res = _reservations.FindAll(r => r.Run == run);
                res.Sort((a, b) => a.Start.CompareTo(b.Start));
                int cursor = 0;
                foreach (var r in res)
                {
                    if (r.Start > cursor) spans.Add((cursor, r.Start - cursor, LotUse.House, MaxDepthCells));
                    spans.Add((r.Start, r.Len, r.Use, r.MaxDepth));
                    cursor = r.Start + r.Len;
                }
                if (cursor < run.Length) spans.Add((cursor, run.Length - cursor, LotUse.House, MaxDepthCells));

                foreach (var span in spans)
                {
                    if (span.use != LotUse.House)
                    {
                        TryLot(run, span.start, span.len, span.maxDepth, span.use);
                        continue;
                    }
                    foreach (var (start, width) in DealWidths(span.start, span.len))
                        TryLot(run, start, width, MaxDepthCells, LotUse.House);
                }
            }
            Debug.Log($"[SuburbDemo] {_lots.Count} lots carved on {_segments.Count} street segments, {_cuts.Count} cul-de-sacs");
        }

        int MaxDepthCells => Mathf.RoundToInt(lotDepth / Cell);
        const int MinDepthCells = 3; // 15 m: a shallow house 3.5 m behind the kerb and a fence behind it

        // Lot widths in cells along a frontage: 4 (20 m) and 5 (25 m) mostly, the odd
        // 3 (a narrow house) and 6 (the big one); the remainder must stay dealable.
        List<(int, int)> DealWidths(int start, int len)
        {
            var list = new List<(int, int)>();
            var valid = new List<int>();
            int at = start, left = len;
            while (left >= 3)
            {
                valid.Clear();
                foreach (int w in new[] { 3, 4, 5, 6 })
                    if (w <= left && (left - w == 0 || left - w >= 3)) valid.Add(w);
                if (valid.Count == 0) break;
                // weights: 4 and 5 carry the street, 3 and 6 are the odd ones out
                float total = 0f;
                foreach (int w in valid) total += Weight(w);
                float roll = Rnd() * total;
                int pick = valid[valid.Count - 1];
                foreach (int w in valid) { roll -= Weight(w); if (roll <= 0f) { pick = w; break; } }
                list.Add((at, pick));
                at += pick;
                left -= pick;
            }
            return list;

            // the demo's lots: most 15-20 m wide with the house nearly filling them
            static float Weight(int w) => w == 4 ? 0.45f : w == 5 ? 0.2f : w == 3 ? 0.32f : 0.03f;
        }

        // A lot off a run: the cells [start, start+width) of the frontage and as many
        // free cells inward as the block allows, up to maxDepth - a rectangle, cut
        // to the shallowest column. Too shallow and the cells stay free (lawn).
        bool TryLot(Run run, int start, int width, int maxDepth, LotUse use)
        {
            if (width < 3) return false;
            int dx = -Mathf.RoundToInt(run.Front.x), dz = -Mathf.RoundToInt(run.Front.z); // inward
            int depth = maxDepth;
            for (int k = 0; k < width; k++)
            {
                var (cx, cz) = run.Cells[start + k];
                int d = 0;
                while (d < maxDepth && IsFree(cx + dx * d, cz + dz * d)) d++;
                depth = Mathf.Min(depth, d);
            }
            int minDepth = use == LotUse.House ? MinDepthCells : 5;
            if (depth < minDepth) return false;

            var lot = new Lot { Index = _lots.Count, Front = run.Front, WidthCells = width, DepthCells = depth, Stub = run.Stub, Use = use };
            int xMin = int.MaxValue, zMin = int.MaxValue, xMax = int.MinValue, zMax = int.MinValue;
            for (int k = 0; k < width; k++)
            {
                var (cx, cz) = run.Cells[start + k];
                for (int d = 0; d < depth; d++)
                {
                    int x = cx + dx * d, z = cz + dz * d;
                    _kind[x, z] = CellKind.Lot;
                    _lotOf[x, z] = lot.Index;
                    xMin = Mathf.Min(xMin, x); xMax = Mathf.Max(xMax, x);
                    zMin = Mathf.Min(zMin, z); zMax = Mathf.Max(zMax, z);
                }
            }
            lot.Cx0 = xMin; lot.Cx1 = xMax + 1; lot.Cz0 = zMin; lot.Cz1 = zMax + 1;
            FrameLot(lot);
            // a street down either side? (look just outside the front row, left and right)
            var l = lot.P(-Cell * 0.5f, Cell * 0.5f);
            var r = lot.P(lot.W + Cell * 0.5f, Cell * 0.5f);
            lot.CornerLeft = IsPavement(CellOf(l.x), CellOf(l.z));
            lot.CornerRight = IsPavement(CellOf(r.x), CellOf(r.z));
            _lots.Add(lot);
            return true;
        }

        // The lot frame: stand on the street looking at the lot. Origin is the
        // front-left corner, Along runs to your right, In runs away from you.
        void FrameLot(Lot lot)
        {
            lot.In = -lot.Front;
            lot.Along = Vector3.Cross(Vector3.up, lot.In);
            float x0 = lot.Cx0 * Cell, x1 = lot.Cx1 * Cell, z0 = lot.Cz0 * Cell, z1 = lot.Cz1 * Cell;
            // the corner with the least Along and the least In
            Vector3 best = Vector3.zero;
            float bestScore = float.MaxValue;
            foreach (var c in new[] { new Vector3(x0, 0f, z0), new Vector3(x1, 0f, z0), new Vector3(x0, 0f, z1), new Vector3(x1, 0f, z1) })
            {
                float score = Vector3.Dot(c, lot.Along) + Vector3.Dot(c, lot.In);
                if (score < bestScore) { bestScore = score; best = c; }
            }
            lot.Origin = best;
        }

        /// <summary>World min corner of the lot's cell (i along, j in).</summary>
        Vector3 LotCell(Lot lot, int i, int j)
        {
            var c = lot.P(i * Cell + Cell * 0.5f, j * Cell + Cell * 0.5f);
            return new Vector3(Mathf.Floor(c.x / Cell + 0.001f) * Cell, 0f, Mathf.Floor(c.z / Cell + 0.001f) * Cell);
        }

        void SetSurface(Lot lot, int i, int j, Surface s, int yaw)
        {
            var c = LotCell(lot, i, j);
            int cx = CellOf(c.x + 1f), cz = CellOf(c.z + 1f);
            if (!InGrid(cx, cz)) return;
            _surface[cx, cz] = s;
            _surfYaw[cx, cz] = yaw;
        }

        /// <summary>The pavement cell in front of the lot's column i, and its variant.</summary>
        void SetFrontPavement(Lot lot, int i, SwVariant v)
        {
            var c = lot.P(i * Cell + Cell * 0.5f, -Cell * 0.5f);
            int cx = CellOf(c.x), cz = CellOf(c.z);
            if (InGrid(cx, cz) && _kind[cx, cz] == CellKind.Sidewalk && _swVariant[cx, cz] == SwVariant.Plain)
                _swVariant[cx, cz] = v;
        }

        // ------------------------------------------------------------ places

        // The church, the shops and the park each take a stretch of one street
        // frontage, in three different blocks: shops and church at a block corner,
        // the park mid-block and as deep as the block goes.
        void ReservePlaces(List<Run> runs)
        {
            var usedBlocks = new HashSet<int>();
            Run Find(int need, bool cornerWanted)
            {
                foreach (var r in runs)
                {
                    if (r.Stub || r.Length < need || usedBlocks.Contains(r.Block)) continue;
                    if (cornerWanted && !(r.CornerAtStart || r.CornerAtEnd)) continue;
                    return r;
                }
                return null;
            }
            void Reserve(Run r, int len, bool corner, LotUse use, int maxDepth)
            {
                if (r == null) { Debug.LogWarning($"[SuburbDemo] no frontage long enough for the {use}"); return; }
                int start;
                if (corner)
                {
                    bool atStart = r.CornerAtStart && (!r.CornerAtEnd || Chance(0.5f));
                    start = atStart ? 0 : r.Length - len;
                }
                else start = (r.Length - len) / 2;
                _reservations.Add(new Reservation { Run = r, Start = start, Len = len, Use = use, MaxDepth = maxDepth });
                usedBlocks.Add(r.Block);
            }
            Reserve(Find(14, true), 14, true, LotUse.Shops, MaxDepthCells + 1);
            Reserve(Find(9, true), 9, true, LotUse.Church, MaxDepthCells);
            Reserve(Find(10, false), 10, false, LotUse.Park, MaxDepthCells * 2);
        }

        void PlanPlaces()
        {
            // places were reserved during the carve; nothing more to decide here
        }

        // ------------------------------------------------------------ sketch

        void OnDrawGizmos()
        {
            if (Application.isPlaying) return;
            if (columns < 1 || rows < 1 || blockLengths == null || blockLengths.Length == 0) return;
            float depth = Mathf.Round(lotDepth / Cell) * Cell;
            var xs = new float[columns + 1];
            var zs = new float[rows + 1];
            xs[0] = Walk + StreetHalf;
            for (int i = 0; i < columns; i++) xs[i + 1] = xs[i] + 2f * StreetHalf + 2f * Walk + blockLengths[i % blockLengths.Length];
            zs[0] = Walk + StreetHalf;
            for (int j = 0; j < rows; j++) zs[j + 1] = zs[j] + 2f * StreetHalf + 2f * Walk + 2f * depth;
            Gizmos.color = new Color(1f, 1f, 1f, 0.35f);
            foreach (float x in xs)
                Gizmos.DrawCube(new Vector3(x, 0f, (zs[0] + zs[rows]) * 0.5f), new Vector3(2f * StreetHalf, 0.1f, zs[rows] - zs[0] + 2f * StreetHalf));
            foreach (float z in zs)
                Gizmos.DrawCube(new Vector3((xs[0] + xs[columns]) * 0.5f, 0f, z), new Vector3(xs[columns] - xs[0] + 2f * StreetHalf, 0.1f, 2f * StreetHalf));
        }
    }
}
