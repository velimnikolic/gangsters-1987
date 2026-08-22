using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// A way across the city ON FOOT, for a man who is not bound to the pavements.
    ///
    /// The crowd walks the sidewalk graph, waits at its lights and goes round the
    /// blocks, because that is what a city looks like. The outfit does not. A crew
    /// told to be somewhere cuts across the lot, over the road against the light,
    /// between the buildings - anywhere a man can physically put his feet. The only
    /// thing it cannot do is walk through a wall.
    ///
    /// So the ground is the map, not the graph: a lattice of squares over everything
    /// the scene has blocked off (WalkObstacles), each one free or not, and the way
    /// through it is the shortest run of free squares. The line is then pulled taut -
    /// a man does not walk a staircase of two-metre steps across an empty lot, he
    /// walks straight at the corner he has to get round - so what comes back is a
    /// handful of corners, not a hundred crumbs.
    ///
    /// The traffic is deliberately NOT on this map: a way drawn round wherever the
    /// cars happened to be standing is a way round nothing a moment later. Cars are
    /// what the walking steers past as it goes (WalkObstacles.Steer, CrewWalker).
    /// </summary>
    public static class WalkRoute
    {
        /// <summary>The lattice pitch. Small enough to find the gap between two
        /// buildings, big enough that a quarter of a mile square is thousands of
        /// squares rather than millions.</summary>
        public const float Cell = 2.5f;

        /// <summary>Ground kept beyond everything blocked, so a route may go round the
        /// outside of the outermost building.</summary>
        const float Margin = 30f;

        /// <summary>How far from a street a man may still be and be IN THE CITY: the
        /// pavement, the frontage, and the yard behind it - about half a block. Beyond
        /// that is grass, and the city stops at its outermost street.</summary>
        const float CityReach = 34f;

        static bool[] _free;
        // and whether a man can actually get from one square to the next, east and
        // north; the other two ways are the same passages read backwards
        static bool[] _passX, _passZ;
        static int _w, _h;
        static float _x0, _z0;
        static int _builtAt = -1;

        static readonly List<int> _open = new List<int>();
        static float[] _cost;
        static int[] _from;
        static int[] _stamp;
        static int _visit;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Forget()
        {
            _free = null;
            _passX = _passZ = null;
            _cost = null; _from = null; _stamp = null;
            _w = _h = 0; _builtAt = -1; _visit = 0;
            _open.Clear();
        }

        /// <summary>Squares the lattice holds, once it is built - for the lab.</summary>
        public static int Squares => _free != null ? _free.Length : 0;

        // ------------------------------------------------------------------ the map

        static bool Ready()
        {
            if (_free != null && _builtAt == WalkObstacles.Version) return true;
            if (WalkObstacles.Max.x <= WalkObstacles.Min.x) return false;   // nothing blocked yet
            Build();
            return _free != null;
        }

        static void Build()
        {
            _x0 = WalkObstacles.Min.x - Margin;
            _z0 = WalkObstacles.Min.y - Margin;
            _w = Mathf.CeilToInt((WalkObstacles.Max.x + Margin - _x0) / Cell) + 1;
            _h = Mathf.CeilToInt((WalkObstacles.Max.y + Margin - _z0) / Cell) + 1;
            if (_w <= 1 || _h <= 1 || (long)_w * _h > 4_000_000L) { _free = null; return; }

            int n = _w * _h;
            _free = new bool[n];
            _passX = new bool[n];
            _passZ = new bool[n];
            _cost = new float[n];
            _from = new int[n];
            _stamp = new int[n];
            _visit = 0;

            // A square is free when a man STOOD IN THE MIDDLE OF IT is clear of
            // everything fixed. Half a man of air, not more: the gap between a
            // building and the kerb is often exactly a pavement wide, and asking for
            // more would wall off half the city's frontages.
            float r = WalkObstacles.Radius;
            var net = LaneNet.Active ?? LaneNet.Shared;
            bool bounded = net != null && net.Roads.Count > 0;
            for (int z = 0; z < _h; z++)
                for (int x = 0; x < _w; x++)
                {
                    var q = new Vector3(_x0 + x * Cell, 0f, _z0 + z * Cell);
                    // the scene's own fence too (WalkObstacles.City): near-a-street is
                    // not enough where the ground past the pavement is bare void - a
                    // way must never cut a corner over ground nobody may stand on
                    _free[z * _w + x] = !WalkObstacles.Standing(q, r) &&
                                        (!bounded || InTheCity(net, q)) &&
                                        WalkObstacles.InCity(q);
                }

            // TWO FREE SQUARES ARE NOT A WAY BETWEEN THEM. A square is free when a man
            // stood in the MIDDLE of it is clear, and a wall thin enough to stand
            // between two middles leaves both of them free while a man cannot get from
            // one to the other. A way drawn through such a pair comes back with a corner
            // three metres off and a metre of air in front of it, and the crew stands
            // there for the rest of the run. So each passage is asked about once, here,
            // and the search only ever steps through one that answers yes.
            for (int z = 0; z < _h; z++)
                for (int x = 0; x < _w; x++)
                {
                    int i = z * _w + x;
                    if (!_free[i]) continue;
                    if (x + 1 < _w && _free[i + 1]) _passX[i] = Walkable(Middle(i), Middle(i + 1));
                    if (z + 1 < _h && _free[i + _w]) _passZ[i] = Walkable(Middle(i), Middle(i + _w));
                }
            _builtAt = WalkObstacles.Version;
        }

        /// <summary>Is this ground part of the city at all?
        ///
        /// A crew walks anywhere a man can put his feet - over the lot, across the road
        /// against the light, down the gap between two buildings - but it does not set
        /// off across the fields. THE CITY IS ITS STREETS: ground within half a block of
        /// one is pavement, frontage or yard and a man may be on it; ground further off
        /// than that is grass, water, or whatever the island is made of out there, and
        /// no way is ever drawn over it.</summary>
        static bool InTheCity(LaneNet net, Vector3 q)
        {
            for (int i = 0; i < net.Roads.Count; i++)
            {
                var road = net.Roads[i];
                var a = road.A;
                var ab = road.B - a;
                float len2 = ab.x * ab.x + ab.z * ab.z;
                if (len2 < 1e-4f) continue;
                float t = Mathf.Clamp01(((q.x - a.x) * ab.x + (q.z - a.z) * ab.z) / len2);
                float dx = q.x - (a.x + ab.x * t), dz = q.z - (a.z + ab.z * t);
                // measured from the KERB, not the centre line: a boulevard is thirty
                // metres wide and that width is street, not distance from a street
                float slack = CityReach + road.HalfRoad;
                if (dx * dx + dz * dz <= slack * slack) return true;
            }
            return false;
        }

        static int Index(Vector3 p, out int x, out int z)
        {
            x = Mathf.Clamp(Mathf.RoundToInt((p.x - _x0) / Cell), 0, _w - 1);
            z = Mathf.Clamp(Mathf.RoundToInt((p.z - _z0) / Cell), 0, _h - 1);
            return z * _w + x;
        }

        static Vector3 Middle(int i) =>
            new Vector3(_x0 + (i % _w) * Cell, 0f, _z0 + (i / _w) * Cell);

        /// <summary>The free square nearest this one, searched outward. A man dealt
        /// inside a wall, or a mark stood against one, still has to be walked to.</summary>
        static int Nearest(int i)
        {
            if (_free[i]) return i;
            int x0 = i % _w, z0 = i / _w;
            for (int ring = 1; ring <= 24; ring++)
                for (int dz = -ring; dz <= ring; dz++)
                    for (int dx = -ring; dx <= ring; dx++)
                    {
                        if (Mathf.Abs(dx) != ring && Mathf.Abs(dz) != ring) continue;
                        int x = x0 + dx, z = z0 + dz;
                        if (x < 0 || z < 0 || x >= _w || z >= _h) continue;
                        int j = z * _w + x;
                        if (_free[j]) return j;
                    }
            return -1;
        }

        /// <summary>The square a man at <paramref name="p"/> can actually SET OFF FROM:
        /// free, and with nothing between him and the middle of it.
        ///
        /// The nearest free square is not good enough to start a way from. A man stood
        /// on clear ground a metre from a wall is nearest to a square on the far side of
        /// that wall, and a way drawn from there begins with a corner he cannot reach:
        /// three metres to it, one metre of air, and a crew stood in front of a building
        /// for the rest of the run. Which side of the wall he is on is the whole
        /// question, so it is asked outright.</summary>
        static int Reachable(Vector3 p)
        {
            int i = Index(p, out int x0, out int z0);
            if (_free[i] && Walkable(p, Middle(i))) return i;
            for (int ring = 1; ring <= 8; ring++)
                for (int dz = -ring; dz <= ring; dz++)
                    for (int dx = -ring; dx <= ring; dx++)
                    {
                        if (Mathf.Abs(dx) != ring && Mathf.Abs(dz) != ring) continue;
                        int x = x0 + dx, z = z0 + dz;
                        if (x < 0 || z < 0 || x >= _w || z >= _h) continue;
                        int j = z * _w + x;
                        if (_free[j] && Walkable(p, Middle(j))) return j;
                    }
            return -1;
        }

        // ------------------------------------------------------------------ the way

        static readonly int[] _dx = { 1, -1, 0, 0, 1, 1, -1, -1 };
        static readonly int[] _dz = { 0, 0, 1, -1, 1, -1, 1, -1 };

        /// <summary>The corners of a way from here to there, into
        /// <paramref name="into"/> (cleared first), ending at
        /// <paramref name="to"/>. False when there is no way at all - then the caller
        /// walks straight at it and lets the steering do what it can.</summary>
        public static bool Plan(Vector3 from, Vector3 to, List<Vector3> into)
        {
            into.Clear();
            if (!Ready()) return false;

            // Where he can SET OFF from; and if there is nowhere - he is in a pocket the
            // lattice cannot see out of - the nearest square will do, because a way with
            // an awkward first metre beats no way at all. The steering covers that metre.
            int a = Reachable(from);
            if (a < 0) a = Nearest(Index(from, out _, out _));
            int b = Nearest(Index(to, out _, out _));
            if (a < 0 || b < 0) return false;
            if (a == b) { into.Add(to); return true; }

            // Near enough to see it: no lattice needed, and no lattice STAIRCASE either.
            if (Walkable(from, to)) { into.Add(to); return true; }

            if (!Search(a, b)) return false;

            // back from the mark to the man, then round the right way
            _crumbs.Clear();
            for (int i = b; i != a; i = _from[i])
            {
                _crumbs.Add(Middle(i));
                if (_crumbs.Count > _free.Length) return false;   // a loop: give it up
            }
            _crumbs.Reverse();
            if (_crumbs.Count > 0) _crumbs[_crumbs.Count - 1] = to; else _crumbs.Add(to);

            Pull(from, _crumbs, into);
            return into.Count > 0;
        }

        static readonly List<Vector3> _crumbs = new List<Vector3>();

        /// <summary>Is there a way from this square one step that way? The passage is
        /// kept on the western/southern square of each pair, so a step the other way
        /// reads the neighbour's.</summary>
        static bool Passable(int x, int z, int dx, int dz)
        {
            if (dx > 0) return _passX[z * _w + x];
            if (dx < 0) return x > 0 && _passX[z * _w + x - 1];
            if (dz > 0) return _passZ[z * _w + x];
            return z > 0 && _passZ[(z - 1) * _w + x];
        }

        static bool Search(int a, int b)
        {
            _visit++;
            _open.Clear();
            _cost[a] = 0f;
            _from[a] = a;
            _stamp[a] = _visit;
            _open.Add(a);

            var goal = Middle(b);
            int guard = 0;
            while (_open.Count > 0)
            {
                // the cheapest open square, guessed distance included
                int best = 0;
                float bestF = float.MaxValue;
                for (int k = 0; k < _open.Count; k++)
                {
                    int c = _open[k];
                    float f = _cost[c] + (Middle(c) - goal).magnitude;
                    if (f < bestF) { bestF = f; best = k; }
                }
                int cur = _open[best];
                _open[best] = _open[_open.Count - 1];
                _open.RemoveAt(_open.Count - 1);
                if (cur == b) return true;
                if (++guard > 200000) return false;

                int cx = cur % _w, cz = cur / _w;
                for (int d = 0; d < 8; d++)
                {
                    int x = cx + _dx[d], z = cz + _dz[d];
                    if (x < 0 || z < 0 || x >= _w || z >= _h) continue;
                    int nb = z * _w + x;
                    if (!_free[nb]) continue;
                    // and there has to be a way from here to there, not just ground at
                    // both ends; a corner is turned only when both of its sides are ways
                    if (d < 4)
                    {
                        if (!Passable(cx, cz, _dx[d], _dz[d])) continue;
                    }
                    else
                    {
                        if (!_free[cz * _w + x] || !_free[z * _w + cx]) continue;
                        if (!Passable(cx, cz, _dx[d], 0) || !Passable(x, cz, 0, _dz[d])) continue;
                        if (!Passable(cx, cz, 0, _dz[d]) || !Passable(cx, z, _dx[d], 0)) continue;
                    }
                    float step = d >= 4 ? Cell * 1.41421f : Cell;
                    float cost = _cost[cur] + step;
                    if (_stamp[nb] == _visit && cost >= _cost[nb]) continue;
                    _stamp[nb] = _visit;
                    _cost[nb] = cost;
                    _from[nb] = cur;
                    _open.Add(nb);
                }
            }
            return false;
        }

        // ------------------------------------------------------------------ taut

        /// <summary>The crumbs pulled into a line: from where he stands, keep the
        /// furthest crumb he can walk STRAIGHT to, stand there, and go again. What is
        /// left is the corners he actually has to get round.</summary>
        static void Pull(Vector3 from, List<Vector3> crumbs, List<Vector3> into)
        {
            var at = from;
            int i = 0;
            while (i < crumbs.Count)
            {
                int keep = -1;
                for (int j = crumbs.Count - 1; j >= i; j--)
                    if (Walkable(at, crumbs[j])) { keep = j; break; }
                // Nothing on the rest of the way can be walked straight to from here. At
                // the very start that is the awkward first metre again - he is handed the
                // next square and steers to it. Further along it means the way has been
                // overtaken by something; he walks what is drawn and asks again there.
                if (keep < 0)
                {
                    if (into.Count > 0) return;
                    keep = i;
                }
                into.Add(crumbs[keep]);
                at = crumbs[keep];
                i = keep + 1;
                if (into.Count > 64) { into.Add(crumbs[crumbs.Count - 1]); return; }
            }
        }

        /// <summary>Is the straight line between these two clear of everything fixed?
        ///
        /// Asked of the same probe the walking itself uses, which steps a third of a
        /// metre at a time. Sampling this line at half a square instead - a metre and a
        /// quarter, against a man two thirds of a metre wide - let a wall fall clean
        /// between two samples: the way came back with a corner drawn straight through
        /// the corner of a building, and the crew stood in front of it for the rest of
        /// the run with three metres to its next corner and one metre of air.</summary>
        static bool Walkable(Vector3 a, Vector3 b)
        {
            var d = b - a;
            d.y = 0f;
            float len = d.magnitude;
            if (len < 0.01f) return true;
            float r = WalkObstacles.Radius;
            if (WalkObstacles.Standing(a, r) || WalkObstacles.Standing(b, r)) return false;
            return WalkObstacles.ClearStanding(a, d / len, r, len) >= len - 0.01f;
        }
    }
}
