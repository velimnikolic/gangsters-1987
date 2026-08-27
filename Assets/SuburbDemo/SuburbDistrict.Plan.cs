using System.Collections.Generic;
using RoadDemo;
using UnityEngine;

namespace SuburbDemo
{
    // The plan: where the streets run, which 5 m cells are road, zebra, pavement or
    // free, and how the free cells are cut into lots off the street frontages.
    // Everything later reads the cell grid.
    //
    // The suburb is not the rectangle of its lattice but a potato inside it (the mask,
    // `_wild`): streets and lots keep to the potato, what lies outside is woods - so
    // the edge of the suburb is an irregular line of streets dying into the trees.
    //
    // The streets are "loops and lollipops" the way the Town demo's own are (one spine,
    // T junctions, streets that end) rather than a grid: a lattice of POTENTIAL axes
    // 30-40 m apart, a skeleton (the north edge street and the spines the city's roads
    // arrive on), then streets grown along the lattice gaps by weighted choice - on
    // straight a bit, round a corner, seldom closing a loop, seldom a crossroads - until
    // most of the map is within a lot's depth of a pavement. Dead ends become
    // cul-de-sacs with a turning bulb, or run off the map's edge under a flat cap;
    // what can do neither is pushed on or dropped. Two rules keep it buildable: kept
    // parallel streets are never nearer than MinParallel where they overlap (two rows of
    // lots between), junctions along a street never nearer than MinJunction.
    public partial class SuburbDistrict
    {
        enum CellKind : byte { Free, Road, Zebra, Sidewalk, Wrap, Cap, Lot }
        enum Surface : byte { Grass, Grass2, Driveway, Path, PathDoor, Concrete, Asphalt, Parking, Dirt, None }
        enum SwVariant : byte { Plain, Crossing, Driveway, Path }
        public enum LotUse { House, Church, GasStation, Hardware, Shop, Park }

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
            public bool Stub;       // a dead end: one end is a turning bulb or a flat cap, not a junction
            public bool DeadHi;     // which end is the dead one
            public bool Open;       // the dead end is a flat cap on the map's edge (no lanes; the demo's Road_Corner_End pair)
            public float EndLen => Open ? Cell : 2f * BulbHalf;
            public float BulbLo => DeadHi ? Hi - EndLen : Lo;
            public float BulbHi => DeadHi ? Hi : Lo + EndLen;
            public readonly List<RoadEdge> Edges = new List<RoadEdge>();
        }

        public class Lot
        {
            public int Index;
            public int Cx0, Cz0, Cx1, Cz1;       // cell range, exclusive upper
            public Vector3 Front;                // towards the street it faces
            public int WidthCells, DepthCells;
            public bool CornerLeft, CornerRight; // a street down that side too
            public bool Stub;                    // faces a cul-de-sac or a street's dead end
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
            public bool HasPool;
            public readonly List<Rect> Taken = new List<Rect>(); // world XZ footprints already standing on the lot
            public readonly List<Vector3> IdleSpots = new List<Vector3>();
        }

        // a lattice gap: the stretch of a potential axis between two neighbouring lattice points
        struct Gap
        {
            public bool V; public int I, J;
            public Gap(bool v, int i, int j) { V = v; I = i; J = j; }
            public override int GetHashCode() => (V ? 1 : 0) + I * 2 + J * 2048;
            public override bool Equals(object o) => o is Gap g && g.V == V && g.I == I && g.J == J;
        }

        // how a dead-end gap ends: a bulb (or flat cap) at its high or low end
        struct DeadEnd { public bool DeadHi, Open; public float Straight; }

        float[] _vx, _hz;                 // lattice axes (potential streets)
        int _nx, _nz;                     // lattice gaps across / deep (_vx.Length - 1, _hz.Length - 1)
        bool[,] _keptV, _keptH;           // the gaps that are streets: V[i, j] = (i,j)-(i,j+1), H[i, j] = (i,j)-(i+1,j)
        bool[,] _skelV, _skelH;           // the skeleton: never pruned
        readonly HashSet<Gap> _banned = new HashSet<Gap>();
        readonly Dictionary<Gap, DeadEnd> _ends = new Dictionary<Gap, DeadEnd>();
        int _stubsMade;
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
        bool[,] _wild;                    // cells outside the suburb's outline: woods, never a street or a lot
        readonly float[] _phis = new float[3];
        readonly List<Lot> _lots = new List<Lot>();

        public float MapWidth => _vx[_vx.Length - 1] + StreetHalf + Walk;
        public float MapHeight => _hz[_hz.Length - 1] + StreetHalf + Walk;

        // growth weights: on straight after 1 / 2 gaps (never after 3), round a corner at a
        // dead end, closing a loop, making a crossroads, running to the map's edge
        const float StraightAfter1 = 1.5f, StraightAfter2 = 0.6f, TurnWeight = 1.5f, TailWeight = 0.3f;
        const float StubMax = 60f;

        // ------------------------------------------------------------ lattice

        void PlanLines()
        {
            var spac = new List<int>();
            var pins = new List<int>();

            if (_links == null)
            {
                // on its own: a lattice across `columns` blocks' worth of width, the spines
                // near a third and two thirds of it
                int width = Mathf.Max(1, columns) * 90;
                spac.AddRange(SplitGap(width));
                var xs = new List<float> { Walk + StreetHalf };
                foreach (int d in spac) xs.Add(xs[xs.Count - 1] + d);
                int Nearest(float t)
                {
                    int best = 1;
                    for (int k = 1; k + 1 < xs.Count; k++) if (Mathf.Abs(xs[k] - t) < Mathf.Abs(xs[best] - t)) best = k;
                    return best;
                }
                int a = Nearest(xs[0] + width / 3f), b = Nearest(xs[0] + 2f * width / 3f);
                pins.Add(a);
                if (b != a) pins.Add(b);
            }
            else
            {
                // hung off the city: a spine has to land on every link, so the lattice between
                // two links is cut to fit the gap exactly. The links say WHERE the spines are,
                // not how wide the quarter is: the rest of the width goes into the two flanks
                // outside the outer links, so a suburb pinned to a single road comes out a
                // suburb and not a ribbon of houses either side of its own approach road.
                float between = _links[_links.Length - 1] - _links[0];
                float rest = Mathf.Max(1, columns) * 90f - between;
                var fl = SplitGap(FlankOf(rest));
                spac.AddRange(fl);
                pins.Add(fl.Length);
                for (int k = 0; k + 1 < _links.Length; k++)
                {
                    var run = SplitGap(_links[k + 1] - _links[k]);
                    spac.AddRange(run);
                    pins.Add(pins[pins.Count - 1] + run.Length);
                }
                spac.AddRange(SplitGap(FlankOf(rest)));
            }
            _pinColumns = pins.ToArray();
            var deep = SplitGap(Mathf.Max(1, rows) * 70);

            _nx = spac.Count;
            _nz = deep.Length;
            _vx = new float[_nx + 1];
            _hz = new float[_nz + 1];
            _vx[0] = Walk + StreetHalf;
            for (int i = 0; i < _nx; i++) _vx[i + 1] = _vx[i] + spac[i];
            _hz[0] = Walk + StreetHalf;
            for (int j = 0; j < _nz; j++) _hz[j + 1] = _hz[j] + deep[j];

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
            BuildMask();
        }

        // ------------------------------------------------------------ the outline

        /// <summary>The suburb's outline: an ellipse in the lattice's rectangle, its radius
        /// wobbled by three low sinusoids (a potato, never a rectangle); hung off the city
        /// it is the lower half of one, its centre on the city's edge, plus a strip along
        /// that edge so the edge street and the pins always stand. Cells outside are wild.</summary>
        void BuildMask()
        {
            for (int k = 0; k < 3; k++) _phis[k] = Rnd(0f, Mathf.PI * 2f);
            _wild = new bool[_w, _h];
            for (int x = 0; x < _w; x++)
                for (int z = 0; z < _h; z++)
                    _wild[x, z] = !InsideOutline((x + 0.5f) * Cell, (z + 0.5f) * Cell);
        }

        bool InsideOutline(float x, float z)
        {
            float mw = MapWidth, mh = MapHeight;
            float dx, dz;
            if (_links == null)
            {
                dx = (x - mw * 0.5f) / (mw * 0.5f - 10f);
                dz = (z - mh * 0.5f) / (mh * 0.5f - 10f);
            }
            else
            {
                if (z >= mh - 45f) return true;
                dx = (x - mw * 0.5f) / (mw * 0.5f - 10f);
                dz = (z - mh) / (mh - 20f);
            }
            float th = Mathf.Atan2(dz, dx);
            float n = (1f + 0.6f * Mathf.Sin(2f * th + _phis[0]) + 0.3f * Mathf.Sin(3f * th + _phis[1]) + 0.1f * Mathf.Sin(5f * th + _phis[2])) * 0.5f;
            float r = 1f - outlineWobble * Mathf.Clamp01(n);
            return dx * dx + dz * dz <= r * r;
        }

        bool Wild(int cx, int cz) => InGrid(cx, cz) && _wild[cx, cz];

        /// <summary>Does the gap's 20 m band, the junction boxes at both its ends included,
        /// lie inside the outline?</summary>
        bool MaskOk(Gap g)
        {
            float axis = AxisOf(g), lo = LoOf(g), hi = HiOf(g);
            int ca = Mathf.RoundToInt(axis / Cell);
            int c0 = Mathf.RoundToInt((lo - 10f) / Cell), c1 = Mathf.RoundToInt((hi + 10f) / Cell);
            for (int a = Mathf.Max(0, c0); a < Mathf.Min(g.V ? _h : _w, c1); a++)
                for (int c = ca - 2; c < ca + 2; c++)
                {
                    int x = g.V ? c : a, z = g.V ? a : c;
                    if (!InGrid(x, z) || _wild[x, z]) return false;
                }
            return true;
        }

        /// <summary>May this gap ever be a street? Not on the lattice's outer lines, and inside the outline.</summary>
        bool Keepable(Gap g) => !OnOuterLine(g) && MaskOk(g);

        /// <summary>A dead end at (i,j) whose one arm leaves towards d: can the street not
        /// go straight on (off the lattice, or out of the outline)? Then it ends here,
        /// against the woods.</summary>
        bool EdgeEnd(int i, int j, int d)
        {
            var g = GapFrom(i, j, Opp(d));
            return !g.HasValue || !Keepable(g.Value);
        }

        /// <summary>Half of what is left of the suburb's width once the links have taken
        /// their share, on the 5 m lattice and jittered a little so the quarter is not
        /// symmetrical about its approach road. Never less than a block's worth: a flank
        /// is what keeps the outermost street off the boundary.</summary>
        int FlankOf(float rest) => Mathf.Max(45, Mathf.RoundToInt(rest * 0.5f / 5f) * 5 + (_rng.Next(5) - 2) * 5);

        /// <summary>Lattice spacings `lo`..`hi` each, multiples of 5, summing to the gap,
        /// dealt unevenly so no two axes sit in step. The gap is a multiple of 5 when the
        /// city's own spacings are (that is why a suburb's links are five road lines apart).</summary>
        int[] SplitGap(float gap, int lo = 30, int hi = 40)
        {
            int g = Mathf.RoundToInt(gap / 5f) * 5;
            if (Mathf.Abs(g - gap) > 0.51f)
                Debug.LogWarning($"[Suburb] a link {gap:F1} m out is not on the 5 m lattice - the street " +
                                 $"will land {Mathf.Abs(g - gap):F1} m off the city's line.");
            int n = Mathf.Max(1, Mathf.RoundToInt(g / ((lo + hi) * 0.5f)));
            n = Mathf.Max(n, Mathf.CeilToInt(g / (float)hi));
            n = Mathf.Min(n, Mathf.Max(1, g / lo));
            int each = (g / n) / 5 * 5;
            var lens = new int[n];
            for (int i = 0; i < n; i++) lens[i] = each;
            int left = g - each * n;
            for (int i = 0; left >= 5; i++, left -= 5) lens[i % n] += 5;
            for (int k = 0; k < n * 3; k++)
            {
                int a = _rng.Next(n), b = _rng.Next(n);
                if (a == b) continue;
                if (lens[a] - 5 >= lo && lens[b] + 5 <= hi) { lens[a] -= 5; lens[b] += 5; }
            }
            return lens;
        }

        /// <summary>The suburb's own end of a connecting street: the junction at the head
        /// of a pinned spine gets its north arm, which lays the zebra out to the boundary
        /// where the city's street arrives.</summary>
        void OpenGates()
        {
            if (_links == null) return;
            foreach (int p in _pinColumns)
            {
                if (p < 0 || p > _nx) continue;
                var n = _nodes[p, _nz];
                if (n == null) continue;
                n.N = true;
            }
        }

        // ------------------------------------------------------------ kept gaps

        bool Kept(Gap g) => g.V ? _keptV[g.I, g.J] : _keptH[g.I, g.J];
        void SetKept(Gap g, bool v) { if (g.V) _keptV[g.I, g.J] = v; else _keptH[g.I, g.J] = v; }
        bool Skeleton(Gap g) => g.V ? _skelV[g.I, g.J] : _skelH[g.I, g.J];
        bool OnOuterLine(Gap g) => g.V ? (g.I == 0 || g.I == _nx) : g.J == 0;
        float AxisOf(Gap g) => g.V ? _vx[g.I] : _hz[g.J];
        float LoOf(Gap g) => g.V ? _hz[g.J] : _vx[g.I];
        float HiOf(Gap g) => g.V ? _hz[g.J + 1] : _vx[g.I + 1];

        // directions out of a lattice point: 0 N, 1 S, 2 E, 3 W
        const int DN = 0, DS = 1, DE = 2, DW = 3;
        static int Opp(int d) => d == DN ? DS : d == DS ? DN : d == DE ? DW : DE;
        static bool Collinear(int a, int b) => a == Opp(b);

        /// <summary>The lattice gap leaving point (i,j) towards d, or null off the lattice.</summary>
        Gap? GapFrom(int i, int j, int d)
        {
            switch (d)
            {
                case DN: return j < _nz ? new Gap(true, i, j) : (Gap?)null;
                case DS: return j > 0 ? new Gap(true, i, j - 1) : (Gap?)null;
                case DE: return i < _nx ? new Gap(false, i, j) : (Gap?)null;
                default: return i > 0 ? new Gap(false, i - 1, j) : (Gap?)null;
            }
        }

        static (int, int) Step(int i, int j, int d)
            => d == DN ? (i, j + 1) : d == DS ? (i, j - 1) : d == DE ? (i + 1, j) : (i - 1, j);

        /// <summary>The kept gaps at a lattice point, as the directions they leave it in.</summary>
        int ArmsAt(int i, int j, List<int> dirs)
        {
            dirs.Clear();
            for (int d = 0; d < 4; d++)
            {
                var g = GapFrom(i, j, d);
                if (g.HasValue && Kept(g.Value)) dirs.Add(d);
            }
            return dirs.Count;
        }

        readonly List<int> _dirs = new List<int>(4), _dirs2 = new List<int>(4);

        int Degree(int i, int j) => ArmsAt(i, j, _dirs);

        /// <summary>A junction: three arms, or two that turn a corner.</summary>
        bool IsJunction(int i, int j)
        {
            int n = ArmsAt(i, j, _dirs);
            if (n >= 3) return true;
            return n == 2 && !Collinear(_dirs[0], _dirs[1]);
        }

        /// <summary>How many kept gaps run on in a straight line from (i,j) towards d.</summary>
        int StraightRun(int i, int j, int d)
        {
            int n = 0;
            while (n < 20)
            {
                var g = GapFrom(i, j, d);
                if (!g.HasValue || !Kept(g.Value)) return n;
                (i, j) = Step(i, j, d);
                n++;
            }
            return n;
        }

        /// <summary>May this gap be a street? No kept parallel street nearer than MinParallel
        /// where their extents overlap, and every junction it would make MinJunction from
        /// any other along both lines through it.</summary>
        bool Allowed(Gap g) => Allowed(g, minJunction);

        bool Allowed(Gap g, float junctionSpacing)
        {
            float axis = AxisOf(g), lo = LoOf(g), hi = HiOf(g);
            int lines = g.V ? _nx + 1 : _nz + 1;
            for (int li = 0; li < lines; li++)
            {
                float oa = g.V ? _vx[li] : _hz[li];
                if (li == (g.V ? g.I : g.J) || Mathf.Abs(oa - axis) >= minParallel - 0.01f) continue;
                int n = g.V ? _nz : _nx;
                for (int k = 0; k < n; k++)
                {
                    var o = g.V ? new Gap(true, li, k) : new Gap(false, k, li);
                    if (!Kept(o)) continue;
                    if (LoOf(o) < hi - 0.01f && HiOf(o) > lo + 0.01f) return false;
                }
            }
            SetKept(g, true);
            bool ok = true;
            foreach (var (ei, ej) in new[] { (g.I, g.J), g.V ? (g.I, g.J + 1) : (g.I + 1, g.J) })
            {
                if (!IsJunction(ei, ej)) continue;
                for (int x = 0; x <= _nx && ok; x++)
                    if (x != ei && IsJunction(x, ej) && Mathf.Abs(_vx[x] - _vx[ei]) < junctionSpacing - 0.01f) ok = false;
                for (int z = 0; z <= _nz && ok; z++)
                    if (z != ej && IsJunction(ei, z) && Mathf.Abs(_hz[z] - _hz[ej]) < junctionSpacing - 0.01f) ok = false;
                if (!ok) break;
            }
            SetKept(g, false);
            return ok;
        }

        bool Usable(Gap? g) => g.HasValue && !Kept(g.Value) && !_banned.Contains(g.Value) && Keepable(g.Value) && Allowed(g.Value);

        // ------------------------------------------------------------ coverage

        /// <summary>The share of the map's free cells within a lot's depth of a street - the
        /// growth stops when enough of it is.</summary>
        float Coverage()
        {
            int reach = MaxDepthCells + 1;
            var dist = DistanceFromStreets();
            int free = 0, near = 0;
            for (int x = 0; x < _w; x++)
                for (int z = 0; z < _h; z++)
                {
                    if (dist[x, z] == 0 || _wild[x, z]) continue;
                    free++;
                    if (dist[x, z] <= reach) near++;
                }
            return near / (float)Mathf.Max(1, free);
        }

        /// <summary>For every cell, how many cells it lies from the nearest street's band (0 on it).</summary>
        int[,] DistanceFromStreets()
        {
            var dist = new int[_w, _h];
            for (int x = 0; x < _w; x++) for (int z = 0; z < _h; z++) dist[x, z] = int.MaxValue;
            var q = new Queue<(int, int)>();
            void Paint(int x0, int x1, int z0, int z1)
            {
                for (int x = Mathf.Max(0, x0); x < Mathf.Min(_w, x1); x++)
                    for (int z = Mathf.Max(0, z0); z < Mathf.Min(_h, z1); z++)
                        if (dist[x, z] != 0) { dist[x, z] = 0; q.Enqueue((x, z)); }
            }
            for (int i = 0; i <= _nx; i++)
                for (int j = 0; j < _nz; j++)
                    if (_keptV[i, j]) { int cx = Mathf.RoundToInt(_vx[i] / Cell); Paint(cx - 2, cx + 2, Mathf.RoundToInt(_hz[j] / Cell) - 2, Mathf.RoundToInt(_hz[j + 1] / Cell) + 2); }
            for (int i = 0; i < _nx; i++)
                for (int j = 0; j <= _nz; j++)
                    if (_keptH[i, j]) { int cz = Mathf.RoundToInt(_hz[j] / Cell); Paint(Mathf.RoundToInt(_vx[i] / Cell) - 2, Mathf.RoundToInt(_vx[i + 1] / Cell) + 2, cz - 2, cz + 2); }
            while (q.Count > 0)
            {
                var (x, z) = q.Dequeue();
                foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    int nx = x + dx, nz = z + dz;
                    if (nx < 0 || nz < 0 || nx >= _w || nz >= _h) continue;
                    if (dist[nx, nz] > dist[x, z] + 1) { dist[nx, nz] = dist[x, z] + 1; q.Enqueue((nx, nz)); }
                }
            }
            return dist;
        }

        // ------------------------------------------------------------ growth

        void PlanStreets()
        {
            _keptV = new bool[_nx + 1, _nz];
            _keptH = new bool[_nx, _nz + 1];
            _skelV = new bool[_nx + 1, _nz];
            _skelH = new bool[_nx, _nz + 1];
            _banned.Clear();
            _ends.Clear();
            _stubsMade = 0;

            // the skeleton. Hung off the city: the edge street along the city's side and
            // the spines down from it on the pins, as far into the outline as it goes. On
            // its own: the spines, and a cross street through the middle, each kept outward
            // from the centre as far as the outline lets it.
            void KeepSpine(int p, int from, int step)
            {
                for (int j = from; j >= 0 && j < _nz; j += step)
                {
                    if (!MaskOk(new Gap(true, p, j))) break;
                    _keptV[p, j] = true; _skelV[p, j] = true;
                }
            }
            if (_links != null)
            {
                for (int i = 0; i < _nx; i++) { _keptH[i, _nz] = true; _skelH[i, _nz] = true; }
                foreach (int p in _pinColumns) KeepSpine(p, _nz - 1, -1);
            }
            else
            {
                int midRow = 1, midCol = 1;
                for (int j = 1; j < _nz; j++) if (Mathf.Abs(_hz[j] - MapHeight * 0.5f) < Mathf.Abs(_hz[midRow] - MapHeight * 0.5f)) midRow = j;
                for (int i = 1; i < _nx; i++) if (Mathf.Abs(_vx[i] - MapWidth * 0.5f) < Mathf.Abs(_vx[midCol] - MapWidth * 0.5f)) midCol = i;
                foreach (int p in _pinColumns) { KeepSpine(p, midRow, 1); KeepSpine(p, midRow - 1, -1); }
                for (int i = midCol; i < _nx; i++) { if (!MaskOk(new Gap(false, i, midRow))) break; _keptH[i, midRow] = true; _skelH[i, midRow] = true; }
                for (int i = midCol - 1; i >= 0; i--) { if (!MaskOk(new Gap(false, i, midRow))) break; _keptH[i, midRow] = true; _skelH[i, midRow] = true; }
            }

            int steps = 0;
            for (int round = 0; round < 4; round++)
            {
                steps += Grow(800 - steps);
                Prune();
                if (Coverage() >= streetCoverage - 0.03f) break;
            }
            int spurs = FillPockets();
            if (spurs > 0) Prune();
            int tails = 0, stubs = 0;
            foreach (var e in _ends.Values) { if (e.Open) tails++; else stubs++; }
            Debug.Log($"[Suburb] streets: {steps} lattice gaps grown, coverage {Coverage():F2}, {stubs} cul-de-sacs, {tails} streets out to the edge");
        }

        /// <summary>A few more little blocks: spurs into the pockets the growth left - a
        /// cul-de-sac or a short street off an existing one, allowed nearer its
        /// neighbouring junction than the through streets are (SpurJunction, the way a
        /// real cul-de-sac sits), each where it brings the most uncovered ground within a
        /// lot's reach. Returns how many were added.</summary>
        int FillPockets()
        {
            int reach = MaxDepthCells + 1, added = 0;
            var cands = new List<(int gain, Gap g)>();
            for (int k = 0; k < extraBlocks; k++)
            {
                var dist = DistanceFromStreets();
                cands.Clear();
                for (int pass = 0; pass < 2; pass++)
                {
                    bool vert = pass == 0;
                    int ni = vert ? _nx + 1 : _nx, nj = vert ? _nz : _nz + 1;
                    for (int i = 0; i < ni; i++)
                        for (int j = 0; j < nj; j++)
                        {
                            var g = new Gap(vert, i, j);
                            if (Kept(g) || _banned.Contains(g) || !Keepable(g)) continue;
                            var (bi, bj) = vert ? (i, j + 1) : (i + 1, j);
                            int da = Degree(i, j), db = Degree(bi, bj);
                            if ((da == 0) == (db == 0)) continue;      // one end on the graph, one free
                            bool nearIsA = da > 0;
                            var (ni2, nj2) = nearIsA ? (i, j) : (bi, bj);
                            var (fi, fj) = nearIsA ? (bi, bj) : (i, j);
                            if (!AllLive(ni2, nj2)) continue;
                            if (!Allowed(g, SpurJunction)) continue;
                            int farD = nearIsA ? (vert ? DS : DW) : (vert ? DN : DE);   // the far point's arm, back along the gap
                            if (!EdgeEnd(fi, fj, farD) && !CanGoOn(g, fi, fj)) continue;
                            int gain = GainOf(g, dist, reach);
                            if (gain < 12) continue;
                            cands.Add((gain, g));
                        }
                }
                if (cands.Count == 0) break;
                cands.Sort((a, b) => b.gain.CompareTo(a.gain));
                SetKept(cands[Rnd(Mathf.Min(3, cands.Count))].g, true);
                added++;
            }
            return added;
        }

        const float SpurJunction = 35f;

        /// <summary>Every arm of the point reaches a junction (none is a dead end).</summary>
        bool AllLive(int i, int j)
        {
            int n = ArmsAt(i, j, _dirs);
            var dirs = new List<int>(_dirs);
            foreach (int d in dirs) if (!ArmIsLive(i, j, d)) return false;
            return true;
        }

        /// <summary>The uncovered cells inside the outline a street on this gap would bring within reach.</summary>
        int GainOf(Gap g, int[,] dist, int reach)
        {
            float axis = AxisOf(g), lo = LoOf(g), hi = HiOf(g);
            int ca = Mathf.RoundToInt(axis / Cell), c0 = Mathf.RoundToInt(lo / Cell), c1 = Mathf.RoundToInt(hi / Cell);
            int n = 0;
            for (int a = c0 - reach; a < c1 + reach; a++)
                for (int c = ca - 2 - reach; c < ca + 2 + reach; c++)
                {
                    int x = g.V ? c : a, z = g.V ? a : c;
                    if (!InGrid(x, z) || _wild[x, z] || dist[x, z] <= reach) continue;
                    int da = Mathf.Max(0, Mathf.Max(c0 - a, a - c1 + 1)), dc = Mathf.Max(0, Mathf.Max(ca - 2 - c, c - (ca + 1)));
                    if (da + dc <= reach) n++;
                }
            return n;
        }

        /// <summary>With this gap kept, could the street at its far (dead) end go on
        /// somewhere, or is it already long enough for a turning bulb? No hopeless
        /// one-gap spurs.</summary>
        bool CanGoOn(Gap g, int fi, int fj)
        {
            SetKept(g, true);
            bool ok = false;
            try
            {
                if (ArmsAt(fi, fj, _dirs2) == 1)
                {
                    int d = _dirs2[0];
                    if (AvailFor(fi, fj, d) >= stubLengthMin) ok = true;
                    for (int t = 0; t < 4 && !ok; t++)
                    {
                        if (t == d) continue;
                        if (Usable(GapFrom(fi, fj, t))) ok = true;
                    }
                }
            }
            finally { SetKept(g, false); }
            return ok;
        }

        readonly List<(float w, Gap g)> _cands = new List<(float, Gap)>();

        void Candidates()
        {
            _cands.Clear();
            for (int pass = 0; pass < 2; pass++)
            {
                bool vert = pass == 0;
                int ni = vert ? _nx + 1 : _nx, nj = vert ? _nz : _nz + 1;
                for (int i = 0; i < ni; i++)
                    for (int j = 0; j < nj; j++)
                    {
                        var g = new Gap(vert, i, j);
                        if (Kept(g) || _banned.Contains(g) || !Keepable(g)) continue;
                        var (bi, bj) = vert ? (i, j + 1) : (i + 1, j);
                        int da = Degree(i, j), db = Degree(bi, bj);
                        if (da == 0 && db == 0) continue;
                        if (!Allowed(g)) continue;
                        bool farIsB = da > 0 && db == 0, farIsA = db > 0 && da == 0;
                        // the far end against the edge of the outline: a tail there, no lookahead
                        bool farEdge = (farIsB && EdgeEnd(bi, bj, vert ? DS : DW)) || (farIsA && EdgeEnd(i, j, vert ? DN : DE));
                        if (farIsB && !farEdge && !CanGoOn(g, bi, bj)) continue;
                        if (farIsA && !farEdge && !CanGoOn(g, i, j)) continue;
                        float w = 1f;
                        // at each attached end: on straight (weighted down with the run behind),
                        // or round the corner at a dead end, and seldom a fourth arm
                        foreach (var (pi, pj, dOut) in new[] { (i, j, vert ? DN : DE), (bi, bj, vert ? DS : DW) })
                        {
                            int d = Degree(pi, pj);
                            if (d == 0) continue;
                            int run = StraightRun(pi, pj, Opp(dOut));
                            if (run >= 1) w *= run == 1 ? StraightAfter1 : run == 2 ? StraightAfter2 : 0f;
                            else if (d == 1) w *= TurnWeight;
                            if (d >= 3) w *= fourWayWeight;
                        }
                        if (da > 0 && db > 0) w *= loopWeight;
                        if (farEdge) w *= TailWeight;
                        if (w > 0f) _cands.Add((w, g));
                    }
            }
        }

        int Grow(int maxSteps)
        {
            int steps = 0;
            while (steps < maxSteps)
            {
                if (Coverage() >= streetCoverage) break;
                Candidates();
                if (_cands.Count == 0) break;
                float total = 0f;
                foreach (var c in _cands) total += c.w;
                float roll = Rnd() * total;
                var pick = _cands[_cands.Count - 1].g;
                foreach (var c in _cands) { roll -= c.w; if (roll <= 0f) { pick = c.g; break; } }
                SetKept(pick, true);
                steps++;
            }
            return steps;
        }

        // ------------------------------------------------------------ pruning

        /// <summary>The axis of the first real junction behind a dead end at (i,j) whose
        /// only arm leaves it towards d - walking back through straight-through points.</summary>
        float JunctionAxisBehind(int i, int j, int d)
        {
            var (ci, cj) = Step(i, j, d);
            for (int guard = 0; guard < 100; guard++)
            {
                int n = ArmsAt(ci, cj, _dirs2);
                if (n != 2 || !Collinear(_dirs2[0], _dirs2[1])) break;
                (ci, cj) = Step(ci, cj, d);
            }
            return GapFrom(i, j, d).Value.V ? _hz[cj] : _vx[ci];
        }

        /// <summary>Room for a cul-de-sac at the dead end (i,j): the straight carriageway
        /// a turning bulb and its pavement ring would leave, short of the dead lattice point.</summary>
        float AvailFor(int i, int j, int d)
        {
            var g = GapFrom(i, j, d).Value;
            float dead = g.V ? _hz[j] : _vx[i];
            return Mathf.Abs(dead - JunctionAxisBehind(i, j, d)) - StreetHalf - 2f * BulbHalf - Walk - Walk;
        }

        /// <summary>Does the street leaving (i,j) towards d reach another junction, rather
        /// than a dead end?</summary>
        bool ArmIsLive(int i, int j, int d)
        {
            int ci = i, cj = j;
            for (int guard = 0; guard < 100; guard++)
            {
                (ci, cj) = Step(ci, cj, d);
                int n = ArmsAt(ci, cj, _dirs2);
                if (n >= 3) return true;
                if (n <= 1) return false;
                if (!Collinear(_dirs2[0], _dirs2[1])) return true;
            }
            return true;
        }

        int LiveArms(int i, int j)
        {
            int n = ArmsAt(i, j, _dirs), live = 0;
            var dirs = new List<int>(_dirs);
            foreach (int d in dirs) if (ArmIsLive(i, j, d)) live++;
            return live;
        }

        void SetEnd(Gap g, bool deadHi, bool open, float straight)
        {
            _ends[g] = new DeadEnd { DeadHi = deadHi, Open = open, Straight = straight };
            if (!open) _stubsMade++;
        }

        /// <summary>Dead ends. Phase A settles the structure: a dead end inside the map
        /// becomes a cul-de-sac (turning bulb) where there is room and budget, else the
        /// street is pushed on a gap (straight first, round a corner else, onto the graph
        /// only when nothing else will do), else dropped; a dead end on the map's edge
        /// waits. Phase B decides the edge ends: a flat cap (a tail, no lanes) only where
        /// the junction it hangs from keeps two live arms - a car must never be forced
        /// into a tail - else a bulb, else the gap goes and A runs again.</summary>
        void Prune()
        {
            var ends = new List<(int i, int j, Gap g, int d)>();
            void DeadEnds()
            {
                ends.Clear();
                for (int i = 0; i <= _nx; i++)
                    for (int j = 0; j <= _nz; j++)
                        if (ArmsAt(i, j, _dirs) == 1) ends.Add((i, j, GapFrom(i, j, _dirs[0]).Value, _dirs[0]));
            }

            for (int outer = 0; outer < 8; outer++)
            {
                // ---- A
                bool changed = true;
                for (int guard = 0; changed && guard < 500; guard++)
                {
                    changed = false;
                    DeadEnds();
                    foreach (var (i, j, g, d) in ends)
                    {
                        // (an earlier entry of this pass may have pushed a street onto this point)
                        if (ArmsAt(i, j, _dirs) != 1 || !Kept(g)) continue;
                        if (_ends.ContainsKey(g) || EdgeEnd(i, j, d)) continue;
                        bool deadHi = d == DS || d == DW;    // the arm leaves towards S/W: the dead point is the gap's high end
                        float avail = AvailFor(i, j, d);
                        if (_stubsMade < culDeSacs && avail >= stubLengthMin && Chance(0.7f))
                        {
                            float straight = Mathf.Max(stubLengthMin, avail - new[] { 0f, 5f, 10f, 15f, 20f }[Rnd(5)]);
                            SetEnd(g, deadHi, false, Mathf.Min(straight, StubMax, Mathf.Max(stubLengthMin, avail)));
                            continue;
                        }
                        // push on
                        var opts = new List<(float w, Gap g, int t)>();
                        var ahead = GapFrom(i, j, Opp(d));
                        int run = StraightRun(i, j, d);
                        if (run <= 2 && Usable(ahead)) opts.Add((run <= 1 ? StraightAfter1 : StraightAfter2, ahead.Value, Opp(d)));
                        for (int t = 0; t < 4; t++)
                        {
                            if (t == d || t == Opp(d)) continue;
                            var gg = GapFrom(i, j, t);
                            if (Usable(gg)) opts.Add((TurnWeight, gg.Value, t));
                        }
                        var pool = new List<(float w, Gap g, int t)>();
                        foreach (var o in opts) { var (fi, fj) = Step(i, j, o.t); if (Degree(fi, fj) == 0) pool.Add(o); }
                        if (pool.Count == 0) foreach (var o in opts) pool.Add(o);
                        if (pool.Count > 0)
                        {
                            float total = 0f;
                            foreach (var o in pool) total += o.w;
                            float roll = Rnd() * total;
                            var pick = pool[pool.Count - 1];
                            foreach (var o in pool) { roll -= o.w; if (roll <= 0f) { pick = o; break; } }
                            SetKept(pick.g, true);
                            changed = true;
                            continue;
                        }
                        if (avail >= stubLengthMin || Skeleton(g))
                        {
                            // over budget and no way on: a bulb after all (a spine's end is never
                            // dropped, even if its bulb has to stand a little past its lattice point)
                            SetEnd(g, deadHi, false, Mathf.Clamp(avail, stubLengthMin, StubMax));
                            continue;
                        }
                        SetKept(g, false);
                        _banned.Add(g);
                        changed = true;
                    }
                }
                // ---- B
                bool redo = false;
                DeadEnds();
                foreach (var (i, j, g, d) in ends)
                {
                    if (ArmsAt(i, j, _dirs) != 1 || !Kept(g)) continue;
                    if (!EdgeEnd(i, j, d) || _ends.ContainsKey(g)) continue;
                    bool deadHi = d == DS || d == DW;
                    // the junction this end hangs from
                    var (ji, jj) = Step(i, j, d);
                    for (int guard = 0; guard < 100; guard++)
                    {
                        int n = ArmsAt(ji, jj, _dirs2);
                        if (n != 2 || !Collinear(_dirs2[0], _dirs2[1])) break;
                        (ji, jj) = Step(ji, jj, d);
                    }
                    if (LiveArms(ji, jj) >= 2) { SetEnd(g, deadHi, true, 0f); continue; }
                    float avail = AvailFor(i, j, d);
                    if (avail >= stubLengthMin) { SetEnd(g, deadHi, false, Mathf.Min(StubMax, avail)); continue; }
                    SetKept(g, false);
                    _banned.Add(g);
                    redo = true;
                }
                if (!redo) return;
            }
        }

        // ------------------------------------------------------------ nodes, segments

        void BuildNetwork()
        {
            _nodes = new Node[_nx + 1, _nz + 1];
            _nodeList.Clear();
            _segments.Clear();
            for (int i = 0; i <= _nx; i++)
                for (int j = 0; j <= _nz; j++)
                {
                    int n = ArmsAt(i, j, _dirs);
                    if (n == 0) continue;
                    var node = new Node { I = i, J = j, X = _vx[i], Z = _hz[j] };
                    foreach (int d in _dirs) { if (d == DN) node.N = true; else if (d == DS) node.S = true; else if (d == DE) node.E = true; else node.W = true; }
                    bool perpendicular = (node.S || node.N) && (node.W || node.E);
                    if (node.Arms >= 3 || (node.Arms == 2 && perpendicular))
                    {
                        _nodes[i, j] = node;
                        _nodeList.Add(node);
                    }
                }
            for (int i = 0; i <= _nx; i++) WalkLine(true, i);
            for (int j = 0; j <= _nz; j++) WalkLine(false, j);
        }

        /// <summary>The segments along one lattice line: kept gaps merged between junctions,
        /// a dead end closing a segment with its bulb or cap at the low or the high end.</summary>
        void WalkLine(bool vert, int li)
        {
            int n = vert ? _nz : _nx;
            float lineAxis = vert ? _vx[li] : _hz[li];
            Node open = null;
            DeadEnd? pendingLo = null;
            float pendingLoPos = 0f;
            float Face(Node nd) => vert ? nd.Z : nd.X;

            for (int k = 0; k < n; k++)
            {
                var g = vert ? new Gap(true, li, k) : new Gap(false, k, li);
                if (!Kept(g)) { open = null; pendingLo = null; continue; }
                float axisLo = vert ? _hz[k] : _vx[k], axisHi = vert ? _hz[k + 1] : _vx[k + 1];
                var nLo = vert ? _nodes[li, k] : _nodes[k, li];
                var nHi = vert ? _nodes[li, k + 1] : _nodes[k + 1, li];
                _ends.TryGetValue(g, out var end);
                bool hasEnd = _ends.ContainsKey(g);

                if (hasEnd && !end.DeadHi)
                {
                    // a bulb or cap at the LOW end of this gap; its junction comes later on the line
                    pendingLo = end;
                    pendingLoPos = axisLo - Cell;   // a cap's cell is [axisLo-5, axisLo), its wrap beyond
                }
                else if (open == null) open = nLo;

                if (hasEnd && end.DeadHi)
                {
                    if (open == null) { Debug.LogWarning("[Suburb] a dead end with no junction behind it"); continue; }
                    float face = Face(open) + StreetHalf;
                    if (end.Open) _segments.Add(new Segment { Vertical = vert, Axis = lineAxis, Lo = face, Hi = axisHi + Cell, LoNode = open, Stub = true, DeadHi = true, Open = true });
                    else _segments.Add(new Segment { Vertical = vert, Axis = lineAxis, Lo = face, Hi = face + end.Straight + 2f * BulbHalf, LoNode = open, Stub = true, DeadHi = true });
                    open = null; pendingLo = null;
                    continue;
                }
                if (nHi == null) continue;
                float hiFace = Face(nHi) - StreetHalf;
                if (pendingLo.HasValue)
                {
                    var pl = pendingLo.Value;
                    if (pl.Open) _segments.Add(new Segment { Vertical = vert, Axis = lineAxis, Lo = pendingLoPos, Hi = hiFace, HiNode = nHi, Stub = true, DeadHi = false, Open = true });
                    else _segments.Add(new Segment { Vertical = vert, Axis = lineAxis, Lo = hiFace - pl.Straight - 2f * BulbHalf, Hi = hiFace, HiNode = nHi, Stub = true, DeadHi = false });
                    pendingLo = null;
                }
                else if (open != null)
                    _segments.Add(new Segment { Vertical = vert, Axis = lineAxis, Lo = Face(open) + StreetHalf, Hi = hiFace, LoNode = open, HiNode = nHi });
                open = nHi;
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
                    bool wrapCell = s.Stub && !s.Open && (s.DeadHi ? a >= straightHi - Cell - 0.01f : a < straightLo + Cell - 0.01f);
                    if (wrapCell) continue;
                    foreach (float across in new[] { -StreetHalf - Walk, StreetHalf })
                    {
                        var (sx, sz) = AC(s, a, across);
                        Mark(sx, sz, CellKind.Sidewalk);
                        if (s.Stub && InGrid(sx, sz)) _stubSide[sx, sz] = true;
                    }
                }
                if (s.Stub && s.Open) MarkFlatEnd(s);
                else if (s.Stub) MarkBulb(s);
            }

            foreach (var n in _nodeList) MarkNode(n);
        }

        /// <summary>The demo's street end: two Road_Corner_End caps side by side, each under
        /// its 10 x 10 Sidewalk_Corner_End wrap - the pavement turns across the end.</summary>
        void MarkFlatEnd(Segment s)
        {
            float capA = s.BulbLo;
            float da = s.DeadHi ? Cell : -Cell;
            foreach (var (cc, dc) in new[] { (-Cell, -Cell), (0f, Cell) })
            {
                var (x, z) = AC(s, capA, cc);
                Mark(x, z, CellKind.Cap);
                foreach (var (a, c) in new[] { (capA + da, cc), (capA + da, cc + dc), (capA, cc + dc) })
                {
                    var (wx, wz) = AC(s, a, c);
                    if (InGrid(wx, wz)) { _kind[wx, wz] = CellKind.Wrap; _stubSide[wx, wz] = true; }
                }
            }
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

            // a zebra on every arm of a junction; a street merely turning a corner (two arms)
            // gets none - its road runs on round the bend
            bool zebras = n.Arms >= 3;
            void Approach(bool has, int ax, int az, bool alongX, int yawA, int yawB)
            {
                // the two cells of the arm just outside the box: zebra if the arm exists, pavement if not
                var (x0, z0) = alongX ? (ax, cz - 1) : (cx - 1, az);
                var (x1, z1) = alongX ? (ax, cz) : (cx, az);
                if (has && zebras)
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
                else if (!has)
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
        bool IsFree(int cx, int cz) => InGrid(cx, cz) && _kind[cx, cz] == CellKind.Free && !_wild[cx, cz];

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
            Debug.Log($"[SuburbDemo] {_lots.Count} lots carved on {_segments.Count} street segments");
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

        // The church, the gas station, the hardware store, the shop and the park each
        // take a stretch of one street frontage, every one in a block of its own and
        // spread about the suburb (a random pick among the frontages that fit): the
        // station and the church at a block corner, the park mid-block and as deep as
        // the block goes.
        void ReservePlaces(List<Run> runs)
        {
            var usedBlocks = new HashSet<int>();
            var fits = new List<Run>();
            Run Find(int need, bool cornerWanted)
            {
                fits.Clear();
                foreach (var r in runs)
                {
                    if (r.Stub || r.Length < need || usedBlocks.Contains(r.Block)) continue;
                    if (cornerWanted && !(r.CornerAtStart || r.CornerAtEnd)) continue;
                    fits.Add(r);
                }
                return fits.Count > 0 ? fits[Rnd(fits.Count)] : null;
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
            Reserve(Find(7, true), 7, true, LotUse.GasStation, MaxDepthCells + 1);
            Reserve(Find(9, true), 9, true, LotUse.Church, MaxDepthCells);
            Reserve(Find(7, Chance(0.5f)), 7, false, LotUse.Hardware, MaxDepthCells + 1);
            Reserve(Find(7, Chance(0.5f)), 7, false, LotUse.Shop, MaxDepthCells + 1);
            Reserve(Find(10, false), 10, false, LotUse.Park, MaxDepthCells * 2);
        }
    }
}
