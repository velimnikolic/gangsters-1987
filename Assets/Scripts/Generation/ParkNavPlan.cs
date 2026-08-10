using System.Collections.Generic;
using UnityEngine;

namespace LivingCity.Generation
{
    /// <summary>
    /// Turns a ParkLayout.Plan's spines into the per-cell pedestrian path polylines the pack's
    /// nav system can actually consume. Pure geometry - ParkNavBuilder instantiates what this
    /// returns, and the tests exercise this without a scene.
    ///
    /// The shape of the output is dictated by two hard facts of the pack's Tile linking:
    ///
    ///   1. Paths link ACROSS tiles only - Tile.GetNextPaths matches a path's last node to a
    ///      NEIGHBOUR tile's first nodes, never to paths on its own tile. So a path can never
    ///      hand over mid-cell; every polyline here runs from one linkable point to another.
    ///
    ///   2. The only linkable points are the road tiles' own sidewalk endpoints (the plan's
    ///      entrance anchors) and points shared EXACTLY by two park cells' paths on their common
    ///      boundary (playback skips node 0 of each next path, so junction points must
    ///      coincide).
    ///
    /// So the unit of planning is the ROUTE: a complete walk from one road anchor to another,
    /// found by Dijkstra over the spine graph, then split at the internal cell boundaries into
    /// per-cell segments that share their crossing points exactly. A route both of whose ends
    /// are the same anchor is legal and useful - it is how a single-entrance park or the far
    /// side of a civic ring stays walkable (out, around, and back).
    /// </summary>
    public static class ParkNavPlan
    {
        /// <summary>Nodes closer than this along a path are merged - HumanBehavior's arrival radius is 0.75.</summary>
        const float MinNodeSpacing = 1.2f;

        /// <summary>
        /// Two positions this close are the same graph node. Wider than a tolerance usually
        /// wants to be, on purpose: a junction is detected as "spine A's nearest SAMPLE to
        /// spine B", and the sample can sit half a SampleStep from where B actually touches -
        /// so the weld has to bridge that or the two spines never share a node and the graph
        /// quietly falls apart into disconnected walks. Sized against the worst case measured
        /// in anger: an endpoint-on-spine junction whose nearest sample sat 1.71 away (half a
        /// step along plus the lateral sampling drift). Distinct planned nodes are never this
        /// close together - the cut thinning keeps cuts three samples (~9m) apart - and a
        /// merged node only bends a route by the weld radius, well under the node spacing cap.
        /// </summary>
        const float Weld = 2.3f;

        /// <summary>A spine covered less than this much by the entrance routes gets a scenic detour.</summary>
        const float CoverageFloor = 0.5f;

        /// <summary>More directed paths than this on one cell is a graph bug, not a park.</summary>
        const int MaxPathsPerCell = 24;

        public struct CellPaths
        {
            public Vector2Int Cell;
            public List<Vector2[]> Paths;
        }

        public static List<CellPaths> ForPlan(
            ParkLayout.Plan plan, CityGrid grid, List<Vector2Int> cells, List<string> warnings)
        {
            var result = new List<CellPaths>();
            if (plan.Entrances.Count == 0 || plan.Spines.Count == 0 || cells == null
                || cells.Count == 0)
                return result;

            var graph = BuildGraph(plan);
            if (graph.Nodes.Count == 0)
                return result;

            // Every unordered entrance pair, both directions; a lone entrance promenades.
            var routes = new List<List<Vector2>>();
            for (var i = 0; i < plan.Entrances.Count; i++)
            for (var j = i + 1; j < plan.Entrances.Count; j++)
            {
                var route = Route(graph, plan.Entrances[i].Anchor, plan.Entrances[j].Anchor, null);
                if (route != null)
                    routes.Add(route);
                else
                    warnings?.Add($"no walk between entrances {i} and {j}");
            }

            // Scenic detours: a painted walk no route covers is a walk no pedestrian ever
            // takes. An open spine gets a discounted re-route through it; a closed ring gets a
            // promenade - out, once around the WHOLE loop, and back - because a shortest-path
            // reroute always takes the shorter arc and leaves the far side of the ring dead.
            for (var s = 0; s < plan.Spines.Count; s++)
            {
                if (Coverage(plan.Spines[s], routes) >= CoverageFloor)
                    continue;
                var from = plan.Entrances[0].Anchor;
                var scenic = plan.Spines[s].Kind == ParkLayout.SpineKind.PlazaRing
                    ? Promenade(graph, from, s, plan.Spines[s])
                    : Route(graph, from,
                        plan.Entrances[plan.Entrances.Count > 1 ? 1 : 0].Anchor,
                        plan.Spines[s].Points);
                if (scenic != null)
                    routes.Add(scenic);
                else
                    warnings?.Add($"no scenic route through spine {s}; it stays unwalked");
            }

            // Split every route at the internal cell boundaries and hand each piece to its
            // cell, in both directions.
            var inBlock = new HashSet<Vector2Int>(cells);
            var byCell = new Dictionary<Vector2Int, List<Vector2[]>>();
            foreach (var route in routes)
            foreach (var (cell, piece) in Split(route, grid, inBlock))
            {
                if (!byCell.TryGetValue(cell, out var list))
                    byCell[cell] = list = new List<Vector2[]>();
                AddDeduped(list, piece);
                AddDeduped(list, Reversed(piece));
            }

            foreach (var cell in cells)
            {
                if (!byCell.TryGetValue(cell, out var list))
                    continue;
                if (list.Count > MaxPathsPerCell)
                {
                    warnings?.Add($"cell {cell} capped at {MaxPathsPerCell} of {list.Count} paths");
                    list.RemoveRange(MaxPathsPerCell, list.Count - MaxPathsPerCell);
                }
                result.Add(new CellPaths { Cell = cell, Paths = list });
            }

            return result;
        }

        // ------------------------------------------------------------------ graph

        sealed class Graph
        {
            public readonly List<Vector2> Nodes = new();
            public readonly List<List<(int To, float Cost, Vector2[] Piece, int Spine)>> Edges = new();

            public int NodeAt(Vector2 p, bool create)
            {
                for (var i = 0; i < Nodes.Count; i++)
                    if ((Nodes[i] - p).sqrMagnitude < Weld * Weld)
                        return i;
                if (!create)
                    return -1;
                Nodes.Add(p);
                Edges.Add(new List<(int, float, Vector2[], int)>());
                return Nodes.Count - 1;
            }

            public void Link(int a, int b, Vector2[] piece, int spine)
            {
                var cost = ParkLayout.PolylineLength(piece);
                Edges[a].Add((b, cost, piece, spine));
                Edges[b].Add((a, cost, Reversed(piece), spine));
            }
        }

        static Graph BuildGraph(ParkLayout.Plan plan)
        {
            var graph = new Graph();

            // Cut parameters per spine: endpoints plus every junction with another spine.
            var cuts = new List<SortedSet<int>>();
            for (var s = 0; s < plan.Spines.Count; s++)
                cuts.Add(new SortedSet<int> { 0, plan.Spines[s].Points.Length - 1 });

            // Junctions are found as "a sampled point of one spine lying on another spine".
            // The spines were sampled at ~3m, so a genuine crossing always has a sample within
            // half a step of it; welding at node level absorbs the residue. Exact
            // segment-segment intersection was tried on paper and loses: it inserts points the
            // OTHER spine does not have, and the two spines then disagree about where the
            // junction is - which is the one thing the no-intra-tile-linking rule cannot absorb.
            for (var s = 0; s < plan.Spines.Count; s++)
            {
                var points = plan.Spines[s].Points;
                for (var i = 0; i < points.Length; i++)
                for (var o = 0; o < plan.Spines.Count; o++)
                {
                    if (o == s)
                        continue;
                    if (ParkLayout.DistanceToPolyline(points[i], plan.Spines[o].Points) < 1.6f)
                    {
                        cuts[s].Add(i);
                        break;
                    }
                }
            }

            // Consecutive junction samples come from one crossing seen several times; thin them
            // so the graph does not fill with 3m edges around every junction.
            for (var s = 0; s < plan.Spines.Count; s++)
            {
                var thinned = new SortedSet<int>();
                var previous = int.MinValue;
                foreach (var index in cuts[s])
                {
                    var last = plan.Spines[s].Points.Length - 1;
                    if (index != 0 && index != last && previous >= 0 && index - previous <= 2)
                    {
                        previous = index;
                        continue;
                    }
                    thinned.Add(index);
                    previous = index;
                }
                cuts[s] = thinned;
            }

            // A spine that ENDS on another spine is a junction by construction - the secondary
            // join and the loop trail both do it. The sweep above only guarantees that SOME
            // sample cut the other spine near the touch, and the thinning may then keep a
            // sample too far from this spine's endpoint for the weld to bridge (measured 2.43
            // against the 2.3 weld when the city went to TileScale 1.56, which left the loop
            // a cul-de-sac: reachable at one end, so no route could pass THROUGH it and the
            // scenic detour silently collapsed onto the main path). Cut the other spine at its
            // sample NEAREST the endpoint - that sample is at most half a step away along the
            // other spine, which the weld always bridges.
            for (var s = 0; s < plan.Spines.Count; s++)
            {
                var points = plan.Spines[s].Points;
                foreach (var index in new[] { 0, points.Length - 1 })
                for (var o = 0; o < plan.Spines.Count; o++)
                {
                    if (o == s)
                        continue;
                    var other = plan.Spines[o].Points;
                    if (ParkLayout.DistanceToPolyline(points[index], other) >= 1.6f)
                        continue;
                    var best = 0;
                    var bestSq = float.MaxValue;
                    for (var i = 0; i < other.Length; i++)
                    {
                        var apart = (other[i] - points[index]).sqrMagnitude;
                        if (apart >= bestSq)
                            continue;
                        bestSq = apart;
                        best = i;
                    }
                    cuts[o].Add(best);
                }
            }

            for (var s = 0; s < plan.Spines.Count; s++)
            {
                var points = plan.Spines[s].Points;
                var indices = new List<int>(cuts[s]);
                for (var c = 1; c < indices.Count; c++)
                {
                    var from = indices[c - 1];
                    var to = indices[c];
                    if (to <= from)
                        continue;
                    var piece = new Vector2[to - from + 1];
                    System.Array.Copy(points, from, piece, 0, piece.Length);
                    var a = graph.NodeAt(piece[0], create: true);
                    var b = graph.NodeAt(piece[^1], create: true);
                    if (a == b && piece.Length < 3)
                        continue;
                    graph.Link(a, b, piece, s);
                }
            }

            return graph;
        }

        // ------------------------------------------------------------------ routing

        /// <summary>
        /// Dijkstra from anchor to anchor. <paramref name="discounted"/> marks one spine's
        /// polyline whose edges cost a hundredth of their length - the scenic-detour lever.
        /// Same-node start and end is answered as the cheapest genuine loop, not the empty walk.
        /// </summary>
        static List<Vector2> Route(Graph graph, Vector2 from, Vector2 to, Vector2[] discounted)
        {
            var start = graph.NodeAt(from, create: false);
            var end = graph.NodeAt(to, create: false);
            if (start < 0 || end < 0)
                return null;

            float EdgeCost((int To, float Cost, Vector2[] Piece, int Spine) edge) =>
                discounted != null && edge.Piece.Length > 1
                && ParkLayout.DistanceToPolyline(edge.Piece[edge.Piece.Length / 2], discounted) < 0.5f
                    ? edge.Cost * 0.01f
                    : edge.Cost;

            if (start != end)
            {
                var walk = Dijkstra(graph, start, end, EdgeCost);
                return walk == null ? null : Concatenate(walk);
            }

            // Loop: leave by the cheapest edge, then route from its far node back to start
            // without immediately retracing (handled by Dijkstra naturally - the return leg is
            // free to reuse ground, a stroll retraces all the time).
            List<Vector2> best = null;
            var bestCost = float.MaxValue;
            foreach (var edge in graph.Edges[start])
            {
                var back = Dijkstra(graph, edge.To, start, EdgeCost);
                if (back == null)
                    continue;
                var cost = EdgeCost(edge);
                foreach (var leg in back)
                    cost += EdgeCost(leg);
                if (cost >= bestCost)
                    continue;
                bestCost = cost;
                var pieces = new List<(int, float, Vector2[], int)> { edge };
                pieces.AddRange(back);
                best = Concatenate(pieces);
            }
            return best;
        }

        /// <summary>
        /// Out from the anchor to the ring, once around the whole loop, and back the way it
        /// came. The walk a park visitor actually takes, and the only route shape that covers
        /// a closed ring's far side.
        /// </summary>
        static List<Vector2> Promenade(
            Graph graph, Vector2 anchor, int spine, ParkLayout.Spine ring)
        {
            var start = graph.NodeAt(anchor, create: false);
            var ringNode = graph.NodeAt(ring.Points[0], create: false);
            if (start < 0 || ringNode < 0)
                return null;

            var legIn = start == ringNode
                ? new List<(int To, float Cost, Vector2[] Piece, int Spine)>()
                : Dijkstra(graph, start, ringNode, edge => edge.Cost);
            if (legIn == null)
                return null;

            // Follow the ring's own edges once around. Every ring node carries exactly two of
            // them (one per neighbour along the loop, adjacency holds both directions), so
            // "not straight back where I came from" walks the loop.
            var loop = new List<(int To, float Cost, Vector2[] Piece, int Spine)>();
            var at = ringNode;
            var cameFrom = -1;
            for (var guard = 0; guard < graph.Nodes.Count * 2 + 2; guard++)
            {
                var stepped = false;
                foreach (var edge in graph.Edges[at])
                {
                    if (edge.Spine != spine || edge.To == cameFrom)
                        continue;
                    loop.Add(edge);
                    cameFrom = at;
                    at = edge.To;
                    stepped = true;
                    break;
                }
                if (!stepped)
                    return null;
                if (at == ringNode)
                    break;
            }
            if (at != ringNode || loop.Count == 0)
                return null;

            var points = Concatenate(legIn);
            foreach (var point in Concatenate(loop))
                if (points.Count == 0 || (points[^1] - point).sqrMagnitude > 1e-4f)
                    points.Add(point);
            var back = Concatenate(legIn);
            back.Reverse();
            foreach (var point in back)
                if (points.Count == 0 || (points[^1] - point).sqrMagnitude > 1e-4f)
                    points.Add(point);
            return points.Count >= 2 ? points : null;
        }

        static List<(int To, float Cost, Vector2[] Piece, int Spine)> Dijkstra(
            Graph graph, int start, int end,
            System.Func<(int To, float Cost, Vector2[] Piece, int Spine), float> edgeCost)
        {
            var distance = new float[graph.Nodes.Count];
            var visited = new bool[graph.Nodes.Count];
            var cameBy = new (int From, int Edge)[graph.Nodes.Count];
            for (var i = 0; i < distance.Length; i++)
            {
                distance[i] = float.MaxValue;
                cameBy[i] = (-1, -1);
            }
            distance[start] = 0f;

            while (true)
            {
                var current = -1;
                var best = float.MaxValue;
                for (var i = 0; i < distance.Length; i++)
                    if (!visited[i] && distance[i] < best)
                    {
                        best = distance[i];
                        current = i;
                    }
                if (current < 0)
                    return null;
                if (current == end)
                    break;
                visited[current] = true;

                for (var e = 0; e < graph.Edges[current].Count; e++)
                {
                    var edge = graph.Edges[current][e];
                    var through = distance[current] + edgeCost(edge);
                    if (through >= distance[edge.To])
                        continue;
                    distance[edge.To] = through;
                    cameBy[edge.To] = (current, e);
                }
            }

            var walk = new List<(int, float, Vector2[], int)>();
            var at = end;
            while (at != start)
            {
                var (from, edgeIndex) = cameBy[at];
                if (from < 0)
                    return null;
                walk.Add(graph.Edges[from][edgeIndex]);
                at = from;
            }
            walk.Reverse();
            return walk;
        }

        static List<Vector2> Concatenate(
            List<(int To, float Cost, Vector2[] Piece, int Spine)> walk)
        {
            var points = new List<Vector2>();
            foreach (var edge in walk)
            foreach (var point in edge.Piece)
                if (points.Count == 0 || (points[^1] - point).sqrMagnitude > 1e-4f)
                    points.Add(point);
            return points;
        }

        // ------------------------------------------------------------------ coverage

        static float Coverage(ParkLayout.Spine spine, List<List<Vector2>> routes)
        {
            if (spine.Points == null || spine.Points.Length < 2)
                return 1f;
            var covered = 0;
            foreach (var point in spine.Points)
            {
                foreach (var route in routes)
                    if (DistanceToRoute(point, route) < 1.6f)
                    {
                        covered++;
                        break;
                    }
            }
            return covered / (float)spine.Points.Length;
        }

        static float DistanceToRoute(Vector2 p, List<Vector2> route)
        {
            var best = float.MaxValue;
            for (var i = 1; i < route.Count; i++)
            {
                var a = route[i - 1];
                var b = route[i];
                var ab = b - a;
                var lengthSq = ab.sqrMagnitude;
                var t = lengthSq < 1e-8f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / lengthSq);
                best = Mathf.Min(best, (p - (a + ab * t)).magnitude);
            }
            return best;
        }

        // ------------------------------------------------------------------ splitting

        /// <summary>
        /// One route into per-cell pieces, cut ONLY at the boundaries between two of the park's
        /// own cells. The road-side tails stay attached to their edge cell's piece: the hedge
        /// line is outside the cell, so the whole verge tail overhangs the neighbouring road
        /// cell, and that is fine - ownership decides link topology, not geometry.
        /// </summary>
        static IEnumerable<(Vector2Int Cell, Vector2[] Piece)> Split(
            List<Vector2> route, CityGrid grid, HashSet<Vector2Int> inBlock)
        {
            if (route.Count < 2)
                yield break;

            // The internal boundary lines, as world coordinates per axis.
            var linesX = new SortedSet<float>();
            var linesZ = new SortedSet<float>();
            foreach (var cell in inBlock)
            {
                if (inBlock.Contains(cell + new Vector2Int(1, 0)))
                    linesX.Add(cell.x * CityGrid.CellSize + CityGrid.CellSize * 0.5f);
                if (inBlock.Contains(cell + new Vector2Int(0, 1)))
                    linesZ.Add(cell.y * CityGrid.CellSize + CityGrid.CellSize * 0.5f);
            }

            // The owning cell is TRACKED through the crossings rather than guessed from a
            // midpoint: a sliver that skims a corner has a midpoint on the very lines being
            // cut, and a wrong guess strands two pieces in one cell where the pack cannot
            // link them. Seeded from the first segment midpoint that floor-maps into the
            // block (the road-side tails stay with the edge cell they enter).
            var current = FirstCell(route, inBlock);

            // The block's outer rect: an internal line only counts where it actually separates
            // two park cells. A road-side tail can cross the line's EXTENSION out on the
            // pavement, and splitting there would strand a piece in a road cell.
            var blockMin = new Vector2(float.MaxValue, float.MaxValue);
            var blockMax = new Vector2(float.MinValue, float.MinValue);
            foreach (var cell in inBlock)
            {
                var centre = new Vector2(cell.x * CityGrid.CellSize, cell.y * CityGrid.CellSize);
                var half = CityGrid.CellSize * 0.5f;
                blockMin = Vector2.Min(blockMin, centre - new Vector2(half, half));
                blockMax = Vector2.Max(blockMax, centre + new Vector2(half, half));
            }

            var piece = new List<Vector2> { route[0] };
            for (var i = 1; i < route.Count; i++)
            {
                var from = route[i - 1];
                var to = route[i];

                // Crossings of this segment, in order along it, each knowing which way it steps.
                var crossings = new List<(float T, Vector2Int Step)>();
                foreach (var x in linesX)
                    if ((from.x - x) * (to.x - x) < 0f)
                    {
                        var t = (x - from.x) / (to.x - from.x);
                        var z = Mathf.Lerp(from.y, to.y, t);
                        if (z > blockMin.y - 0.01f && z < blockMax.y + 0.01f)
                            crossings.Add((t, new Vector2Int(to.x > from.x ? 1 : -1, 0)));
                    }
                foreach (var z in linesZ)
                    if ((from.y - z) * (to.y - z) < 0f)
                    {
                        var t = (z - from.y) / (to.y - from.y);
                        var x = Mathf.Lerp(from.x, to.x, t);
                        if (x > blockMin.x - 0.01f && x < blockMax.x + 0.01f)
                            crossings.Add((t, new Vector2Int(0, to.y > from.y ? 1 : -1)));
                    }
                crossings.Sort((a, b) => a.T.CompareTo(b.T));

                foreach (var (t, step) in crossings)
                {
                    var crossing = Vector2.Lerp(from, to, t);
                    piece.Add(crossing);
                    yield return (current, Finish(piece));
                    piece = new List<Vector2> { crossing };
                    current += step;
                }
                piece.Add(to);
            }
            if (piece.Count >= 2)
                yield return (current, Finish(piece));
        }

        /// <summary>
        /// The cell the route starts in: the first segment midpoint that floor-maps into the
        /// block. The tails before it (anchor, gate, verge) belong to that same edge cell.
        /// </summary>
        static Vector2Int FirstCell(List<Vector2> route, HashSet<Vector2Int> inBlock)
        {
            for (var i = 1; i < route.Count; i++)
            {
                var mid = (route[i - 1] + route[i]) * 0.5f;
                var cell = new Vector2Int(
                    Mathf.FloorToInt((mid.x + CityGrid.CellSize * 0.5f) / CityGrid.CellSize),
                    Mathf.FloorToInt((mid.y + CityGrid.CellSize * 0.5f) / CityGrid.CellSize));
                if (inBlock.Contains(cell))
                    return cell;
            }

            // A route that never enters the block proper (a short tail-to-tail hop): nearest
            // cell to its start.
            Vector2Int best = default;
            var bestDistance = float.MaxValue;
            foreach (var cell in inBlock)
            {
                var centre = new Vector2(cell.x * CityGrid.CellSize, cell.y * CityGrid.CellSize);
                var d = (centre - route[0]).sqrMagnitude;
                if (d >= bestDistance)
                    continue;
                bestDistance = d;
                best = cell;
            }
            return best;
        }

        /// <summary>
        /// Endpoint-preserving thin: interior nodes closer than MinNodeSpacing to their
        /// predecessor go - the follower consumes sub-arrival-radius nodes one physics step
        /// each and reads as stuttering.
        /// </summary>
        static Vector2[] Finish(List<Vector2> piece)
        {
            var thinned = new List<Vector2> { piece[0] };
            for (var i = 1; i < piece.Count - 1; i++)
                if ((piece[i] - thinned[^1]).magnitude >= MinNodeSpacing)
                    thinned.Add(piece[i]);
            if ((piece[^1] - thinned[^1]).magnitude < 1e-4f && thinned.Count > 1)
                thinned.RemoveAt(thinned.Count - 1);
            thinned.Add(piece[^1]);
            return thinned.ToArray();
        }

        static Vector2[] Reversed(Vector2[] points)
        {
            var reversed = new Vector2[points.Length];
            for (var i = 0; i < points.Length; i++)
                reversed[i] = points[points.Length - 1 - i];
            return reversed;
        }

        static void AddDeduped(List<Vector2[]> list, Vector2[] piece)
        {
            if (piece.Length < 2)
                return;
            foreach (var existing in list)
            {
                if (existing.Length != piece.Length)
                    continue;
                var same = true;
                for (var i = 0; i < piece.Length && same; i++)
                    same = (existing[i] - piece[i]).sqrMagnitude < 0.01f;
                if (same)
                    return;
            }
            list.Add(piece);
        }
    }
}
