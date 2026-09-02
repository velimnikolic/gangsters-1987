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
        /// buildings and to go locally around a cafe table group, big enough that a
        /// quarter of a mile square remains well below the lattice safety cap.</summary>
        public const float Cell = 1.25f;

        /// <summary>Ground kept beyond everything blocked, so a route may go round the
        /// outside of the outermost building.</summary>
        const float Margin = 30f;

        static bool[] _free;
        // and whether a man can actually get from one square to the next, east and
        // north; the other two ways are the same passages read backwards
        static bool[] _passX, _passZ;
        // the way the carriageway under each square runs, or zero where a square is not
        // on one. Kept as two floats rather than a Vector3 to hold the lattice down.
        static float[] _roadAx, _roadAz;
        // Every expensive answer is filled only when a search actually reaches that
        // square/edge. Building all of CoreDemo on the first right click was hundreds of
        // thousands of obstacle and lane probes on Unity's input frame.
        static int[] _freeAt, _passXAt, _passZAt, _roadAt;
        static int _cacheAt;
        static int _w, _h;
        static float _x0, _z0;
        static int _builtAt = -1;

        // the open set, a heap keyed on guessed total cost: the list it replaces was
        // scanned end to end for every square taken off it, a square of the squares
        // opened on a long way across the city
        static readonly WalkHeap _open = new WalkHeap();
        static float[] _cost;
        static int[] _from;
        static int[] _stamp;
        static int[] _closedAt, _goalAt;
        static float[] _goalExit;
        static int _visit;

        struct EndpointAnchor
        {
            public int Square;
            public float Distance;
            public float ConnectorCost;
        }

        // An endpoint is continuous ground, while A* walks cell centres. Keeping only
        // its metrically nearest centre makes that arbitrary snap choose which side of
        // the first/last corner the whole route uses. Give the search a small fan of
        // proved connectors instead; it will pay their real lengths when choosing the
        // cheapest complete way.
        const float AnchorSlack = 2f * Cell;
        const int MaxAnchorRing = 24;
        static readonly List<EndpointAnchor> _startAnchors = new List<EndpointAnchor>(32);
        static readonly List<EndpointAnchor> _goalAnchors = new List<EndpointAnchor>(32);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Forget()
        {
            _free = null;
            _passX = _passZ = null;
            _roadAx = _roadAz = null;
            _freeAt = _passXAt = _passZAt = _roadAt = null;
            _cost = null; _from = null; _stamp = null;
            _closedAt = _goalAt = null; _goalExit = null;
            _w = _h = 0; _builtAt = -1; _cacheAt = 0; _visit = 0;
            _open.Clear();
        }

        /// <summary>Squares the lattice holds, once it is built - for the lab.</summary>
        public static int Squares => _free != null ? _free.Length : 0;

        // ------------------------------------------------------------------ the map

        static bool Ready(Vector3 from, Vector3 to)
        {
            if (_free != null && _builtAt == WalkObstacles.Version &&
                InAddressSpace(from) && InAddressSpace(to)) return true;
            if (WalkObstacles.Max.x <= WalkObstacles.Min.x) return false;   // nothing blocked yet
            PrepareLattice(from, to);
            return _free != null;
        }

        static bool InAddressSpace(Vector3 p) =>
            p.x >= _x0 && p.z >= _z0 &&
            p.x <= _x0 + (_w - 1) * Cell && p.z <= _z0 + (_h - 1) * Cell;

        /// <summary>World-aligned origin for a lattice boundary. Obstacle streaming may
        /// grow or shrink the ledger bounds, but it must never slide every navigation
        /// square by an arbitrary fraction of a cell.</summary>
        internal static float AlignedOrigin(float lower) =>
            Mathf.Floor(lower / Cell) * Cell;

        /// <summary>Prepare the lattice's address space, not its contents. Occupancy,
        /// passages and road axes are memoised on demand by Free, Passable and Along.</summary>
        static void PrepareLattice(Vector3 from, Vector3 to)
        {
            float minX = Mathf.Min(WalkObstacles.Min.x, Mathf.Min(from.x, to.x));
            float minZ = Mathf.Min(WalkObstacles.Min.y, Mathf.Min(from.z, to.z));
            float maxX = Mathf.Max(WalkObstacles.Max.x, Mathf.Max(from.x, to.x));
            float maxZ = Mathf.Max(WalkObstacles.Max.y, Mathf.Max(from.z, to.z));
            float x0 = AlignedOrigin(minX - Margin);
            float z0 = AlignedOrigin(minZ - Margin);
            float x1 = Mathf.Ceil((maxX + Margin) / Cell) * Cell;
            float z1 = Mathf.Ceil((maxZ + Margin) / Cell) * Cell;

            // The address space is a high-water mark for this run. A streamed plan
            // disappearing may invalidate occupancy, but it may not phase-shift or
            // shrink the grid underneath an identical later order.
            if (_free != null)
            {
                x0 = Mathf.Min(x0, _x0);
                z0 = Mathf.Min(z0, _z0);
                x1 = Mathf.Max(x1, _x0 + (_w - 1) * Cell);
                z1 = Mathf.Max(z1, _z0 + (_h - 1) * Cell);
            }
            int w = Mathf.RoundToInt((x1 - x0) / Cell) + 1;
            int h = Mathf.RoundToInt((z1 - z0) / Cell) + 1;
            if (w <= 1 || h <= 1 || (long)w * h > 4_000_000L)
            {
                _free = null;
                return;
            }

            bool sameAddressSpace = _free != null && w == _w && h == _h &&
                                    Mathf.Abs(x0 - _x0) < 0.001f &&
                                    Mathf.Abs(z0 - _z0) < 0.001f;
            _x0 = x0; _z0 = z0; _w = w; _h = h;
            int n = w * h;
            if (!sameAddressSpace)
            {
                _free = new bool[n];
                _passX = new bool[n];
                _passZ = new bool[n];
                _roadAx = new float[n];
                _roadAz = new float[n];
                _freeAt = new int[n];
                _passXAt = new int[n];
                _passZAt = new int[n];
                _roadAt = new int[n];
                _cost = new float[n];
                _from = new int[n];
                _stamp = new int[n];
                _closedAt = new int[n];
                _goalAt = new int[n];
                _goalExit = new float[n];
                _visit = 0;
                _cacheAt = 1;
            }
            else if (_cacheAt == int.MaxValue)
            {
                System.Array.Clear(_freeAt, 0, n);
                System.Array.Clear(_passXAt, 0, n);
                System.Array.Clear(_passZAt, 0, n);
                System.Array.Clear(_roadAt, 0, n);
                _cacheAt = 1;
            }
            else _cacheAt++;

            _builtAt = WalkObstacles.Version;
        }

        /// <summary>A square is free when a man stood in its middle clears every fixed
        /// obstacle and remains inside the city fence. Asked once per obstacle version.</summary>
        static bool Free(int i)
        {
            if (_freeAt[i] == _cacheAt) return _free[i];
            var q = Middle(i);
            _free[i] = !WalkObstacles.Standing(q, WalkObstacles.Radius) &&
                       WalkObstacles.InCity(q);
            _freeAt[i] = _cacheAt;
            return _free[i];
        }

        /// <summary>The way the carriageway under this point runs, normalised - or zero
        /// off the asphalt. The same reading the walking makes of itself
        /// (CrewWalker.OnCarriageway), so the way drawn and the steps taken cannot
        /// disagree about where the road is.</summary>
        static Vector3 RoadAxisAt(LaneNet net, Vector3 q)
        {
            if (net == null) return Vector3.zero;
            var road = net.Locate(q, out _, out float d, 10f);
            if (road == null || Mathf.Abs(d) >= road.HalfRoad) return Vector3.zero;
            var axis = road.Axis;
            axis.y = 0f;
            return axis.sqrMagnitude > 1e-6f ? axis.normalized : Vector3.zero;
        }

        /// <summary>What a step ALONG a carriageway costs, as a multiple of the same step
        /// on the pavement. Seven, which is to say: a man will go seventy metres round
        /// rather than walk ten down the middle of a street - and still cross one the
        /// moment crossing is the shorter way, because a step ACROSS costs nothing extra.</summary>
        const float AlongToll = 6f;

        /// <summary>How much of a straight line may lie along a carriageway before the
        /// line is refused outright (the taut-pulling and the near-enough shortcut both
        /// ask). Four metres is a man stepping off a kerb at an angle; forty is a man
        /// walking down the road.</summary>
        const float AlongLimit = 4f;

        /// <summary>How far this step goes ALONG the road under the square it lands on,
        /// as a fraction of the step: 0 straight across, 1 straight down it.</summary>
        static float Along(int square, float dx, float dz)
        {
            if (_roadAt[square] != _cacheAt)
            {
                var axis = RoadAxisAt(LaneNet.Active ?? LaneNet.Shared, Middle(square));
                _roadAx[square] = axis.x;
                _roadAz[square] = axis.z;
                _roadAt[square] = _cacheAt;
            }
            float ax = _roadAx[square], az = _roadAz[square];
            if (ax == 0f && az == 0f) return 0f;
            float len = Mathf.Sqrt(dx * dx + dz * dz);
            if (len < 1e-6f) return 0f;
            return Mathf.Abs((dx * ax + dz * az) / len);
        }

        /// <summary>Metres of this straight line that are spent going ALONG a
        /// carriageway rather than over one. Sampled, because a line crosses squares the
        /// lattice cannot be asked about wholesale.</summary>
        static float AlongRun(Vector3 a, Vector3 b)
        {
            var d = b - a;
            d.y = 0f;
            float len = d.magnitude;
            if (len < 0.01f) return 0f;
            var dir = d / len;
            float run = 0f;
            float stepLen = Mathf.Min(1f, len);
            for (float t = 0f; t < len; t += stepLen)
            {
                var q = a + dir * (t + stepLen * 0.5f);
                int i = Index(q, out _, out _);
                run += Along(i, dir.x, dir.z) * Mathf.Min(stepLen, len - t);
            }
            return run;
        }

        static int Index(Vector3 p, out int x, out int z)
        {
            x = Mathf.Clamp(Mathf.RoundToInt((p.x - _x0) / Cell), 0, _w - 1);
            z = Mathf.Clamp(Mathf.RoundToInt((p.z - _z0) / Cell), 0, _h - 1);
            return z * _w + x;
        }

        static Vector3 Middle(int i) =>
            new Vector3(_x0 + (i % _w) * Cell, 0f, _z0 + (i / _w) * Cell);

        /// <summary>The squares a man at <paramref name="p"/> can actually SET OFF FROM:
        /// free, and with nothing between him and their middles.
        ///
        /// The nearest free square is not good enough to start a way from. A man stood
        /// on clear ground a metre from a wall is nearest to a square on the far side of
        /// that wall, and a way drawn from there begins with a corner he cannot reach:
        /// three metres to it, one metre of air, and a crew stood in front of a building
        /// for the rest of the run. Which side of the wall he is on is the whole
        /// question, so it is asked outright.</summary>
        static bool Reachable(Vector3 p, bool keepOffRoad, List<EndpointAnchor> into)
        {
            into.Clear();
            if (!WalkObstacles.InCity(p) ||
                WalkObstacles.Standing(p, WalkObstacles.Radius)) return false;
            Index(p, out int x0, out int z0);
            float nearest = float.MaxValue;
            for (int ring = 0; ring <= MaxAnchorRing; ring++)
            {
                for (int dz = -ring; dz <= ring; dz++)
                    for (int dx = -ring; dx <= ring; dx++)
                    {
                        if (Mathf.Abs(dx) != ring && Mathf.Abs(dz) != ring) continue;
                        int x = x0 + dx, z = z0 + dz;
                        if (x < 0 || z < 0 || x >= _w || z >= _h) continue;
                        int j = z * _w + x;
                        if (!Free(j)) continue;
                        var q = Middle(j);
                        float sx = q.x - p.x, sz = q.z - p.z;
                        float distance = Mathf.Sqrt(sx * sx + sz * sz);
                        // Once a nearer connector has been proved, points outside its
                        // useful fan need not make an obstacle query. Candidates kept
                        // before an even nearer one is found are pruned below.
                        if (distance > nearest + AnchorSlack + 1e-4f) continue;
                        if (!Walkable(p, q)) continue;
                        float along = keepOffRoad ? AlongRun(p, q) : 0f;
                        if (keepOffRoad && along > AlongLimit) continue;
                        nearest = Mathf.Min(nearest, distance);
                        into.Add(new EndpointAnchor
                        {
                            Square = j,
                            Distance = distance,
                            ConnectorCost = distance + AlongToll * along
                        });
                    }

                // Every later Chebyshev ring is at least this far away along one world
                // axis. Finish the whole fan: a farther directly-visible centre can
                // avoid a much dearer first lattice turn, which is precisely why one
                // greedy anchor was wrong.
                float nextMinimum = (ring + 0.5f) * Cell;
                if (into.Count > 0 && nextMinimum > nearest + AnchorSlack + 1e-4f)
                    break;
            }

            if (into.Count == 0) return false;
            float farthest = nearest + AnchorSlack + 1e-4f;
            for (int i = into.Count - 1; i >= 0; i--)
                if (into[i].Distance > farthest) into.RemoveAt(i);
            return into.Count > 0;
        }

        // ------------------------------------------------------------------ the way

        static readonly int[] _dx = { 1, -1, 0, 0, 1, 1, -1, -1 };
        static readonly int[] _dz = { 0, 0, 1, -1, 1, -1, 1, -1 };

        /// <summary>The corners of a way from here to there, into
        /// <paramref name="into"/> (cleared first), ending at
        /// <paramref name="to"/>. False when there is no way at all - then the caller
        /// walks straight at it and lets the steering do what it can.</summary>
        public static bool Plan(Vector3 from, Vector3 to, List<Vector3> into,
            bool keepOffRoad = false)
        {
            into.Clear();
            // Clear ground is by far the common order and needs no city-sized address
            // space at all. The obstacle query is proportional only to this line.
            if (!keepOffRoad && Walkable(from, to))
            {
                into.Add(to);
                return true;
            }
            if (!Ready(from, to)) return false;

            // Both exact endpoints must see their lattice anchors. Picking merely the
            // first free cell can put the anchor on the far side of a wall; runtime
            // steering cannot make an invalid first or last connector valid.
            if (!Reachable(from, keepOffRoad, _startAnchors) ||
                !Reachable(to, keepOffRoad, _goalAnchors)) return false;

            // Near enough to see it: no lattice needed, and no lattice STAIRCASE either.
            // Unless the line lies down a street - the whole point of keeping off the
            // road is that the SHORT way is the one that goes down the middle of it.
            if (Walkable(from, to) && (!keepOffRoad || AlongRun(from, to) <= AlongLimit))
            { into.Add(to); return true; }

            if (!Search(_startAnchors, _goalAnchors, to, keepOffRoad,
                        out int a, out int b)) return false;

            // back from the mark to the man, then round the right way
            _crumbs.Clear();
            for (int i = b; i != a; i = _from[i])
            {
                _crumbs.Add(Middle(i));
                if (_crumbs.Count > _free.Length) return false;   // a loop: give it up
            }
            _crumbs.Reverse();
            // Keep BOTH proved connectors. Replacing b by the exact destination, or
            // omitting a, turns a valid lattice route into an unproved shortcut at the
            // very two places most likely to sit beside a wall.
            _crumbs.Insert(0, Middle(a));
            _crumbs.Add(to);

            if (!Pull(from, _crumbs, into, keepOffRoad))
            {
                into.Clear();
                return false;
            }
            return into.Count > 0;
        }

        static readonly List<Vector3> _crumbs = new List<Vector3>();

        /// <summary>Is there a way from this square one step that way? The passage is
        /// kept on the western/southern square of each pair, so a step the other way
        /// reads the neighbour's.</summary>
        static bool Passable(int x, int z, int dx, int dz)
        {
            if (dx > 0) return Passage(z * _w + x, z * _w + x + 1, true);
            if (dx < 0) return x > 0 && Passage(z * _w + x - 1, z * _w + x, true);
            if (dz > 0) return Passage(z * _w + x, (z + 1) * _w + x, false);
            return z > 0 && Passage((z - 1) * _w + x, z * _w + x, false);
        }

        static bool Passage(int from, int to, bool xAxis)
        {
            var stamps = xAxis ? _passXAt : _passZAt;
            var values = xAxis ? _passX : _passZ;
            if (stamps[from] == _cacheAt) return values[from];
            if (!Free(from) || !Free(to)) values[from] = false;
            else
            {
                var a = Middle(from);
                var b = Middle(to);
                // Their centres are already known free. Only the ground BETWEEN them
                // remains to ask; calling Walkable repeated both expensive endpoint
                // occupancy probes for every edge A* opened.
                values[from] = WalkObstacles.InCity((a + b) * 0.5f) &&
                               !WalkObstacles.BlocksStanding(a, b, WalkObstacles.Radius);
            }
            stamps[from] = _cacheAt;
            return values[from];
        }

        /// <summary>A* with virtual continuous endpoints. Every proved start connector
        /// seeds its real cost, and reaching a goal anchor pays that connector too. The
        /// Euclidean distance to the exact mark is a lower bound on every such finish,
        /// so the first completed route is not accepted until no open route can beat it.</summary>
        static bool Search(List<EndpointAnchor> starts, List<EndpointAnchor> goals,
            Vector3 exactGoal, bool keepOffRoad, out int chosenStart, out int chosenGoal)
        {
            chosenStart = chosenGoal = -1;
            // Every route cost in this ground planner is horizontal. Including a
            // character/terrain Y offset in the heuristic could overestimate that cost
            // and make the best-completion early-out accept a longer route.
            exactGoal.y = 0f;
            _visit++;
            _open.Clear();

            for (int i = 0; i < goals.Count; i++)
            {
                var anchor = goals[i];
                if (_goalAt[anchor.Square] != _visit ||
                    anchor.ConnectorCost < _goalExit[anchor.Square])
                {
                    _goalAt[anchor.Square] = _visit;
                    _goalExit[anchor.Square] = anchor.ConnectorCost;
                }
            }
            for (int i = 0; i < starts.Count; i++)
            {
                var anchor = starts[i];
                if (_stamp[anchor.Square] == _visit &&
                    anchor.ConnectorCost >= _cost[anchor.Square]) continue;
                _cost[anchor.Square] = anchor.ConnectorCost;
                _from[anchor.Square] = anchor.Square;
                _stamp[anchor.Square] = _visit;
                _open.Push(anchor.Square, anchor.ConnectorCost +
                           (Middle(anchor.Square) - exactGoal).magnitude);
            }

            int guard = 0;
            float best = float.MaxValue;
            while (_open.Count > 0)
            {
                // the cheapest open square, guessed distance included. A square reached
                // again by a cheaper way sits in the heap twice; the dearer copy comes
                // up later, finds nothing it can better, and costs eight looks
                int cur = _open.Pop();
                if (_closedAt[cur] == _visit) continue;
                _closedAt[cur] = _visit;
                float lowerBound = _cost[cur] + (Middle(cur) - exactGoal).magnitude;
                if (lowerBound > best + 1e-4f) break;
                if (++guard > 200000) return false;

                if (_goalAt[cur] == _visit)
                {
                    float total = _cost[cur] + _goalExit[cur];
                    if (total < best - 1e-4f ||
                        (Mathf.Abs(total - best) <= 1e-4f &&
                         (chosenGoal < 0 || cur < chosenGoal)))
                    {
                        best = total;
                        chosenGoal = cur;
                    }
                }

                int cx = cur % _w, cz = cur / _w;
                for (int d = 0; d < 8; d++)
                {
                    int x = cx + _dx[d], z = cz + _dz[d];
                    if (x < 0 || z < 0 || x >= _w || z >= _h) continue;
                    int nb = z * _w + x;
                    if (_closedAt[nb] == _visit) continue;
                    if (!Free(nb)) continue;
                    // and there has to be a way from here to there, not just ground at
                    // both ends; a corner is turned only when both of its sides are ways
                    if (d < 4)
                    {
                        if (!Passable(cx, cz, _dx[d], _dz[d])) continue;
                    }
                    else
                    {
                        // The diagonal itself is the edge a shoulder-circle traverses.
                        // Two clear L-shaped alternatives neither prove that diagonal
                        // (a table may sit in its middle) nor are required when the
                        // diagonal gap itself is genuinely wide enough.
                        if (!Walkable(Middle(cur), Middle(nb))) continue;
                    }
                    float step = d >= 4 ? Cell * 1.41421356f : Cell;
                    // A CROSSING IS FREE; WALKING DOWN THE ROAD IS NOT. The toll is on
                    // the part of the step that lies along the carriageway, so a way
                    // over a street costs what a street is wide and a way down one costs
                    // seven times its length - which puts the man back on the pavement
                    // without ever walling the road off.
                    if (keepOffRoad)
                        step *= 1f + AlongToll * Along(nb, _dx[d], _dz[d]);
                    float cost = _cost[cur] + step;
                    if (_stamp[nb] == _visit && cost >= _cost[nb]) continue;
                    _stamp[nb] = _visit;
                    _cost[nb] = cost;
                    _from[nb] = cur;
                    _open.Push(nb, cost + (Middle(nb) - exactGoal).magnitude);
                }
            }
            if (chosenGoal < 0) return false;
            chosenStart = chosenGoal;
            for (int n = 0; _from[chosenStart] != chosenStart; n++)
            {
                chosenStart = _from[chosenStart];
                if (n > _free.Length) return false;
            }
            return true;
        }

        // ------------------------------------------------------------------ taut

        /// <summary>The crumbs pulled into a line: from where he stands, keep the
        /// furthest crumb he can walk STRAIGHT to, stand there, and go again. What is
        /// left is the corners he actually has to get round.</summary>
        static bool Pull(Vector3 from, List<Vector3> crumbs, List<Vector3> into,
            bool keepOffRoad = false)
        {
            var at = from;
            int i = 0;
            while (i < crumbs.Count)
            {
                // Visibility along a bent crumb path is not monotone: a near crumb just
                // behind a convex corner may be hidden while a later one above its
                // tangent is clear again. Ask from the end and take the first proved
                // chord; that is the actual furthest visible crumb.
                int keep = i;
                bool found = false;
                for (int j = crumbs.Count - 1; j >= i; j--)
                {
                    bool clear = Walkable(at, crumbs[j]);
                    if (clear && (!keepOffRoad || AlongRun(at, crumbs[j]) <= AlongLimit))
                    {
                        keep = j;
                        found = true;
                        break;
                    }
                }
                // A connected lattice path guarantees its immediate next point. If
                // that invariant is ever broken, fail closed; never emit a segment and
                // hope the live steer somehow crosses the wall for the planner.
                if (!found) return false;
                if ((crumbs[keep] - at).sqrMagnitude > 0.01f * 0.01f)
                    into.Add(crumbs[keep]);
                at = crumbs[keep];
                i = keep + 1;
                if (into.Count > 256) return false;
            }
            return true;
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
            if (!WalkObstacles.InCity(a) || !WalkObstacles.InCity(b)) return false;
            float r = WalkObstacles.Radius;
            // Reject bad endpoints before sampling the interior. Apart from being the
            // right contract for a zero-length chord, this avoids hundreds of city
            // probes when an old streamed shell has appeared around an endpoint.
            if (WalkObstacles.Standing(a, r) || WalkObstacles.Standing(b, r)) return false;
            if (len < 0.01f) return true;
            int citySamples = Mathf.CeilToInt(len / 0.5f);
            for (int i = 1; i < citySamples; i++)
                if (!WalkObstacles.InCity(a + d * (i / (float)citySamples))) return false;
            return !WalkObstacles.BlocksStanding(a, b, r);
        }

        /// <summary>The exact geometric contract used by both the planner and the
        /// crew's shared-corridor validator.</summary>
        internal static bool ChordClear(Vector3 a, Vector3 b) => Walkable(a, b);
    }
}
