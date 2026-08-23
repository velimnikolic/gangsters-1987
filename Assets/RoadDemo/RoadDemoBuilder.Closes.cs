using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    // The closes: the streets that do NOT run the width of the map.
    //
    // A plain lattice reads as a plan drawn by one hand in one afternoon, and no
    // city looks like that. The one thing that breaks it is a street that stops -
    // a run of four blocks and then nothing, a T where a crossroads was expected,
    // a cul-de-sac at the edge of town where the surveyor ran out of money. The
    // grid already knows how to do this: a street that ends on the river's quay is
    // exactly the same thing, and every pass in the builder consults ONE predicate
    // to find out (SegmentOpen). So a close is not new topology - it is the seam's
    // own machinery applied to a gap that has no seam in it.
    //
    // What is left behind is the interesting part. The junctions at both ends cap
    // themselves with pavement (BuildNodeGeometry), the carriageway is never laid,
    // the lane graph skips the segment, no lamps and no manholes go down. That
    // leaves a street-shaped hole between two blocks, and the city fills it the way
    // a real one does: the pavements run on through, and the carriageway between
    // them is grassed over into a walk with trees and benches. The crowd still
    // walks it, the cars cannot, and the block behind it has a garden instead of a
    // frontage - which is what "organic" comes down to on the ground.
    public partial class RoadDemoBuilder
    {
        [Header("Closed streets")]
        [Tooltip("Let ordinary streets stop short instead of every one of them running " +
                 "the width of the map. A closed segment lays no carriageway: its two " +
                 "junctions cap into a T or a dead end, and the strip between them is " +
                 "grassed over into a walk. Boulevards are never closed - they are what " +
                 "holds the city together - and a gap that is already a seam is left to " +
                 "the seam.")]
        public bool closeStreets = true;

        [Tooltip("Same seed, same streets closed. Its own number rather than the " +
                 "spacing seed's, so the block sizes can be re-rolled without moving " +
                 "every dead end in the city.")]
        public int closureSeed = 11;

        [Tooltip("Share of the closable street segments that actually close. This is the " +
                 "one number that decides whether the place reads as a grid: the grid's " +
                 "block boundaries run the whole width of the map, and the only thing " +
                 "that can stop one is a closed street. Measured: a sixth left four sides " +
                 "on nearly every block in the city and changed nothing anyone could see; " +
                 "a half took the last crossroads out of downtown as well. At this figure, " +
                 "with the zone bias behind it, downtown keeps ten of its twelve blocks on " +
                 "four streets and the rim keeps none of its own.")]
        [Range(0f, 0.6f)] public float closureShare = 0.45f;

        [Tooltip("The most consecutive gaps one closure takes along the same road. This " +
                 "is what makes a MISSING STREET rather than a hole: a street that stops " +
                 "for three blocks and starts again reads as a street that was never cut " +
                 "through, and the blocks either side of it become one long block. Scatter " +
                 "the same number of closures one gap at a time and the grid survives them.")]
        [Range(1, 5)] public int closureRunMax = 3;

        [Tooltip("Of the closed streets, the share that become a WALK - lawn, trees and " +
                 "benches down the old carriageway. The rest are built over: the blocks " +
                 "either side keep their buildings and the strip between them is their " +
                 "shared yard, so what the eye reads is one block twice the size. Yards " +
                 "are what breaks up the block sizes; walks are what makes the green.")]
        [Range(0f, 1f)] public float closureGreenShare = 0.25f;

        [Tooltip("How much likelier a segment at the very edge of the grid is to close " +
                 "than one in the middle. A town frays at its rim - the streets that " +
                 "peter out are the outermost ones - so the roll is weighted rather " +
                 "than flat.")]
        [Range(0f, 1f)] public float closureEdgeBias = 0.35f;

        // [road, gap]: a vertical road i that does not cross horizontal gap j, and a
        // horizontal road j that does not cross vertical gap i. Null until PlanCloses
        // has run, which is the state the edit-time gizmo sees.
        bool[,] _closedV, _closedH;

        // and which of them are walks rather than yards - one flag per segment, but set
        // per RUN, so a street is grassed over end to end or built over end to end
        bool[,] _greenV, _greenH;

        bool CloseIsGreen(bool verticalRoad, int road, int gap)
        {
            var mask = verticalRoad ? _greenV : _greenH;
            return mask != null && road >= 0 && road < mask.GetLength(0) &&
                   gap >= 0 && gap < mask.GetLength(1) && mask[road, gap];
        }

        readonly List<Rect> _mergedYards = new List<Rect>();

        /// <summary>The strips where a closed street was built over rather than grassed:
        /// ground that belongs to the two blocks either side of it. The plan draws these
        /// the colour of a block, which is how two lots and the yard between them come
        /// out as one block on the map instead of as two with a road between.</summary>
        public IReadOnlyList<Rect> MergedYards => _mergedYards;

        /// <summary>Whether road <paramref name="road"/> was CLOSED across
        /// <paramref name="gap"/> of the other axis - the street stops here and the
        /// strip beyond it is a walk, not a carriageway. Consulted by SegmentOpen, so
        /// every pass in the builder honours it without knowing it exists.</summary>
        bool Closed(bool verticalRoad, int road, int gap)
        {
            var mask = verticalRoad ? _closedV : _closedH;
            if (mask == null) return false;
            if (road < 0 || road >= mask.GetLength(0)) return false;
            if (gap < 0 || gap >= mask.GetLength(1)) return false;
            return mask[road, gap];
        }

        /// <summary>A close: a segment shut by the roll rather than by a seam, so the
        /// strip between its two junctions is this city's to grass over. A street that
        /// ends on the river is NOT one - the river lays its own quay there.</summary>
        bool IsClose(bool verticalRoad, int road, int gap)
            => Closed(verticalRoad, road, gap) && SeamAt(!verticalRoad, gap) == null;

        /// <summary>Whether a walker can get down this segment: either it is an open
        /// street with pavements, or it is a close, whose pavements run on through the
        /// grass. Only the CARS are stopped by a close.</summary>
        bool WalkThrough(bool verticalRoad, int road, int gap)
            => SegmentOpen(verticalRoad, road, gap) || IsClose(verticalRoad, road, gap);

        /// <summary>Whether there is actually a carriageway on road
        /// <paramref name="road"/> at <paramref name="along"/> metres up its own axis.
        /// For the plan, which letters a street's name along its whole line and would
        /// otherwise print it across the river, through a park and down the middle of
        /// every close. Outside the grid it answers true: the approach roads run on out
        /// to the quarters.</summary>
        public bool StreetOpenAt(bool verticalRoad, int road, float along)
        {
            var axis = verticalRoad ? horizontalRoadZ : verticalRoadX;
            if (axis == null || axis.Length < 2) return true;
            if (along <= axis[0] || along >= axis[axis.Length - 1]) return true;
            for (int k = 0; k + 1 < axis.Length; k++)
            {
                if (along < axis[k] || along > axis[k + 1]) continue;
                // inside a junction box there is always road, whatever the segments
                // either side of it do
                float halfLo = verticalRoad ? HHalf(k) : VHalf(k);
                float halfHi = verticalRoad ? HHalf(k + 1) : VHalf(k + 1);
                if (along <= axis[k] + halfLo || along >= axis[k + 1] - halfHi) return true;
                return SegmentOpen(verticalRoad, road, k);
            }
            return true;
        }

        // ------------------------------------------------------------------- plan

        void PlanCloses()
        {
            int nv = verticalRoadX == null ? 0 : verticalRoadX.Length;
            int nh = horizontalRoadZ == null ? 0 : horizontalRoadZ.Length;
            if (nv < 2 || nh < 2) return;
            _closedV = new bool[nv, nh - 1];
            _closedH = new bool[nh, nv - 1];
            _greenV = new bool[nv, nh - 1];
            _greenH = new bool[nh, nv - 1];
            _mergedYards.Clear();
            if (!closeStreets || closureShare <= 0f) return;

            // The graph as it stands, before a single street is shut. A city with a
            // river in it is already not a full lattice, and a seam badly placed can
            // strand a corner of the grid on its own; if it has, that is the seam's
            // business and not this pass's, so the baseline is measured rather than
            // assumed and every closure is judged against IT.
            int baseline = ReachableNodes();

            var rng = new System.Random(closureSeed * 7919 + spacingSeed);
            var candidates = new List<(float score, bool vertical, int road, int gap)>();

            void Offer(bool vertical, int road, int gap, int gaps)
            {
                // a boulevard never stops: it is the road the whole city is hung on,
                // and the one thing that carries the traffic over the seams
                if (vertical ? verticalIsBoulevard[road] : horizontalIsBoulevard[road]) return;
                // a gap with a seam in it is already decided - the quay, the park's edge
                if (SeamAt(!vertical, gap) != null) return;
                // and the outermost road of each axis is left whole: it is the town's
                // own perimeter, it has a block on ONE side only - so closing it merges
                // nothing - and its junctions already have three legs, so a closure
                // there makes a cul-de-sac rather than a bigger block. Measured, letting
                // the rim roads close cost thirty-six dead ends and bought eight merges.
                int lines = vertical ? verticalRoadX.Length : horizontalRoadZ.Length;
                if (road == 0 || road == lines - 1) return;
                // the rim frays first: a segment in the outermost gap of its axis - the
                // last block before the town runs out - is likelier to be the one that
                // stops, which is where a real plan gives up
                bool rim = gap == 0 || gap == gaps - 1;
                float bias = rim ? closureEdgeBias : 0f;
                candidates.Add(((float)rng.NextDouble() - bias - ClosureBias(vertical, road, gap),
                    vertical, road, gap));
            }

            for (int i = 0; i < nv; i++)
                for (int j = 0; j + 1 < nh; j++) Offer(true, i, j, nh - 1);
            for (int j = 0; j < nh; j++)
                for (int i = 0; i + 1 < nv; i++) Offer(false, j, i, nv - 1);

            // lowest score first: the roll and the biases together are one ordering,
            // and the share says how far down it to go
            candidates.Sort((a, b) => a.score.CompareTo(b.score));
            int want = Mathf.RoundToInt(candidates.Count * Mathf.Clamp01(closureShare));

            int closed = 0, runs = 0, greens = 0, refused = 0;
            int gapsV = nh - 1, gapsH = nv - 1;

            // One segment shut, if it may be. False when it would leave a block on
            // fewer than two streets or strand a junction from the network - a dead end
            // is a street, an island is a bug, and a car spawned on one has no way out.
            bool Shut(bool vertical, int road, int gap, bool green)
            {
                int gaps = vertical ? gapsV : gapsH;
                if (gap < 0 || gap >= gaps) return false;
                if (Closed(vertical, road, gap)) return false;
                if (SeamAt(!vertical, gap) != null) return false;
                if (!FrontageAllows(vertical, road, gap)) { refused++; return false; }
                var mask = vertical ? _closedV : _closedH;
                mask[road, gap] = true;
                if (ReachableNodes() < baseline) { mask[road, gap] = false; refused++; return false; }
                (vertical ? _greenV : _greenH)[road, gap] = green;
                closed++;
                return true;
            }

            foreach (var c in candidates)
            {
                if (closed >= want) break;
                // A street is grassed over end to end or built over end to end: half a
                // run of lawn and half a run of yard reads as neither.
                bool green = rng.NextDouble() < closureGreenShare;
                if (!Shut(c.vertical, c.road, c.gap, green)) continue;
                runs++;
                if (green) greens++;

                // and now the run: the same street kept shut for another gap or two
                // either way. THIS is the pass's whole point. The same number of
                // closures scattered one gap at a time leaves every block boundary
                // running the full width of the map, which is the grid the eye reads;
                // a run of two or three takes a boundary OUT for a stretch, and the
                // blocks on both sides of it become one.
                int want2 = 1 + rng.Next(Mathf.Max(1, closureRunMax));
                for (int step = 1; step < want2 && closed < want; step++)
                {
                    int side = (step & 1) == 1 ? 1 : -1;
                    int reach = (step + 1) / 2;
                    if (!Shut(c.vertical, c.road, c.gap + side * reach, green))
                        Shut(c.vertical, c.road, c.gap - side * reach, green);
                }
            }

            Debug.Log($"[RoadDemo] closed streets (seed {closureSeed}): {closed} segments in " +
                      $"{runs} runs of {candidates.Count} closable - {greens} grassed over into " +
                      $"walks, {runs - greens} built over as yards between merged blocks" +
                      (refused > 0 ? $"; {refused} refused (a block left on one street, or a " +
                                     "stranded junction)" : ""));
        }

        // Both blocks either side of a closed segment must still front at least two
        // streets. A block with one street left is a yard reached down an alley; a
        // block with none is a walled garden nothing can deliver to, and the ownership
        // layer hangs its businesses off frontages.
        bool FrontageAllows(bool vertical, int road, int gap)
        {
            if (vertical) return CellKeepsFrontage(road - 1, gap) && CellKeepsFrontage(road, gap);
            return CellKeepsFrontage(gap, road - 1) && CellKeepsFrontage(gap, road);
        }

        // Counts what WOULD be left after the caller sets its bit: called before the
        // bit is set, so the segment being judged is counted here by hand.
        bool CellKeepsFrontage(int i, int j)
        {
            int nv = verticalRoadX.Length, nh = horizontalRoadZ.Length;
            if (i < 0 || i + 1 >= nv || j < 0 || j + 1 >= nh) return true;   // off the grid
            if (InSeam(i, j)) return true;                                   // not a block at all
            int open = 0;
            if (SegmentOpen(true, i, j)) open++;          // west
            if (SegmentOpen(true, i + 1, j)) open++;      // east
            if (SegmentOpen(false, j, i)) open++;         // south
            if (SegmentOpen(false, j + 1, i)) open++;     // north
            return open >= 3;                              // three now, two after this one
        }

        // How many junctions can be driven to from the first one, over open segments
        // only. Plain breadth-first over the lattice - a hundred and thirty-five nodes,
        // run once per candidate closure, and the whole pass is under a millisecond.
        int ReachableNodes()
        {
            int nv = verticalRoadX.Length, nh = horizontalRoadZ.Length;
            var seen = new bool[nv, nh];
            var queue = new Queue<(int i, int j)>();
            seen[0, 0] = true;
            queue.Enqueue((0, 0));
            int count = 0;
            while (queue.Count > 0)
            {
                var (i, j) = queue.Dequeue();
                count++;
                void Step(int di, int dj, bool open)
                {
                    int ni = i + di, nj = j + dj;
                    if (ni < 0 || ni >= nv || nj < 0 || nj >= nh || seen[ni, nj] || !open) return;
                    seen[ni, nj] = true;
                    queue.Enqueue((ni, nj));
                }
                Step(0, 1, j + 1 < nh && SegmentOpen(true, i, j));
                Step(0, -1, j > 0 && SegmentOpen(true, i, j - 1));
                Step(1, 0, i + 1 < nv && SegmentOpen(false, j, i));
                Step(-1, 0, i > 0 && SegmentOpen(false, j, i - 1));
            }
            return count;
        }

        // ------------------------------------------------------------------ build

        /// <summary>What is left where a street was closed: a walk, or a yard. Runs
        /// after the seams, so the park kit is loaded and the lawn is the parks' own -
        /// a grassed close is a pocket park in the shape of a street.</summary>
        void BuildCloses()
        {
            if (_closedV == null || _closedH == null) return;
            LoadSeamKit();
            int laid = 0;
            for (int i = 0; i < verticalRoadX.Length; i++)
                for (int j = 0; j + 1 < horizontalRoadZ.Length; j++)
                {
                    if (!IsClose(true, i, j)) continue;
                    var a = _nodes[i, j];
                    var b = _nodes[i, j + 1];
                    DressClose(true, verticalRoadX[i], VHalf(i), a.ZMax + Sidewalk, b.ZMin - Sidewalk,
                        CloseIsGreen(true, i, j));
                    laid++;
                }
            for (int j = 0; j < horizontalRoadZ.Length; j++)
                for (int i = 0; i + 1 < verticalRoadX.Length; i++)
                {
                    if (!IsClose(false, j, i)) continue;
                    var a = _nodes[i, j];
                    var b = _nodes[i + 1, j];
                    DressClose(false, horizontalRoadZ[j], HHalf(j), a.XMax + Sidewalk, b.XMin - Sidewalk,
                        CloseIsGreen(false, j, i));
                    laid++;
                }
            if (laid > 0) Debug.Log($"[RoadDemo] {laid} closed street segment(s) laid");
        }

        // One close, written for a street running along Z (a vertical road) and turned
        // by W() for a horizontal one. `centre` is the road's own centreline, `half`
        // its carriageway half-width, and [lo, hi] the run between the two junction
        // caps - which is precisely the ground the carriageway would have covered.
        void DressClose(bool alongZ, float centre, float half, float lo, float hi, bool green)
        {
            float len = hi - lo;
            if (len < Cell) return;
            Vector3 W(float u, float v, float y) => alongZ ? new Vector3(v, y, u) : new Vector3(u, y, v);
            float floor = FloorLevel();
            var area = alongZ
                ? Rect.MinMaxRect(centre - half - Sidewalk, lo, centre + half + Sidewalk, hi)
                : Rect.MinMaxRect(lo, centre - half - Sidewalk, hi, centre + half + Sidewalk);

            // the pavements run on through, exactly where they would have been: the
            // walk graph's links along this segment sit on them (BuildPedGraph), so
            // this is not decoration - it is the ground under the crowd's route
            if (alongZ)
            {
                BuildBlockFloor(centre - half - Sidewalk, centre - half, lo, hi, null, true);
                BuildBlockFloor(centre + half, centre + half + Sidewalk, lo, hi, null, true);
            }
            else
            {
                BuildBlockFloor(lo, hi, centre - half - Sidewalk, centre - half, null, true);
                BuildBlockFloor(lo, hi, centre + half, centre + half + Sidewalk, null, true);
            }

            if (!green)
            {
                // Built over: the strip is paved right across, and the two blocks either
                // side of it - which kept their own buildings, since a lot is a lot
                // whatever the street beside it does - read as ONE block with a yard
                // down the middle of it. This is what takes a block boundary out of the
                // plan, and it is the reason the city stops looking like a grid: a run
                // of three of these makes a block three deep where every other one is
                // one deep.
                if (alongZ) BuildBlockFloor(centre - half, centre + half, lo, hi, null, true);
                else BuildBlockFloor(lo, hi, centre - half, centre + half, null, true);
                _mergedYards.Add(area);

                // a yard's own furniture: bins against the walls, a planter or two, and
                // nothing down the middle - a delivery has to get down it
                int step = 0;
                for (float u = lo + 6f; u < hi - 4f; u += 9f, step++)
                {
                    float side = step % 2 == 0 ? -1f : 1f;
                    float faceIn = alongZ ? (side < 0f ? 90f : 270f) : (side < 0f ? 0f : 180f);
                    var at = W(u, centre + side * (half - 1.1f), floor);
                    if (step % 3 == 2 && _planters.Count > 0) Prop(Pick(_planters), at, faceIn, SeamsRoot);
                    else if (_bins.Count > 0) Prop(Pick(_bins), at, faceIn, SeamsRoot);
                }
                return;
            }

            // the lawn where the asphalt was, a hair under the kerb
            var lawn = GameObject.CreatePrimitive(PrimitiveType.Plane);
            lawn.name = "Close";
            Destroy(lawn.GetComponent<Collider>());
            lawn.transform.SetParent(SeamsRoot, false);
            lawn.transform.position = W((lo + hi) * 0.5f, centre, floor - 0.02f);
            lawn.transform.localScale = alongZ
                ? new Vector3(half * 2f / 10f, 1f, len / 10f)
                : new Vector3(len / 10f, 1f, half * 2f / 10f);
            lawn.GetComponent<MeshRenderer>().sharedMaterial = _lawnMat;

            // the map draws it as green ground: a close is a park as far as the plan is
            // concerned, and the alternative is a street-shaped strip of asphalt colour
            // on the map with nothing on it
            _seamPlans.Add(new SeamInfo(SeamKind.Park, area, alongZ));

            // benches turned to the grass off both pavements, a lamp between them, and
            // trees down the crown. Everything stays inside the old carriageway: the
            // pavements either side carry the walk links, and the clearance pass would
            // only have to cut back anything standing on them.
            int slot = 0;
            for (float u = lo + 7f; u < hi - 5f; u += 12f, slot++)
            {
                float side = slot % 2 == 0 ? -1f : 1f;
                float faceIn = alongZ ? (side < 0f ? 90f : 270f) : (side < 0f ? 0f : 180f);
                if (slot % 3 == 2 && _lamps.Count > 0)
                    Prop(Pick(_lamps), W(u, centre + side * (half - 1.2f), floor), faceIn, SeamsRoot);
                else if (_benches.Count > 0)
                    PlaceBench(W(u, centre + side * (half - 1.6f), floor), faceIn);
            }
            if (_parkTrees.Count > 0)
            {
                int count = Mathf.Max(1, Mathf.RoundToInt(len / 14f));
                for (int t = 0; t < count; t++)
                {
                    float u = Random.Range(lo + 3f, hi - 3f);
                    float v = centre + Random.Range(-half * 0.45f, half * 0.45f);
                    var pool = _bigTrees.Count > 0 && Random.value < 0.35f ? _bigTrees : _parkTrees;
                    Instantiate(Pick(pool), W(u, v, floor - 0.02f),
                        Quaternion.Euler(0f, Random.value * 360f, 0f), _flora).name = "Close Tree";
                }
            }
        }
    }
}
