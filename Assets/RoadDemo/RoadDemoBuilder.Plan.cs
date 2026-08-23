using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    // The plan: how many streets there are, which of them are avenues, and where the
    // water and the green go. Everything else about the city was already drawn from
    // its seed - the block sizes, the closed streets, the zoning, the pocket parks,
    // the quarters, the shoreline - and these three were the last things still typed
    // out by hand in a field initialiser. Which meant every city ever built by this
    // project had fifteen streets one way and nine the other, avenues on the same
    // four lines, and the river in row gap three: a town that varied in its details
    // and never in its bones.
    //
    // So the bones are rolled too, and off ONE number. citySeed is the city - give
    // the same number twice and the same town stands up twice, streets, water,
    // gangs and all; leave rollCityEachPlay on and every Play is a town nobody has
    // seen before.
    //
    // Two rules are not negotiable and both were learned by measuring rather than by
    // thinking, so they are enforced here and not left to the roll:
    //
    //   ONE SHORE MUST STAY WHOLE. The airport's field is a mile of runway - wider
    //   than the town it serves - so it cannot be laid on a shore a river runs out
    //   of, and if all four are crossed the city simply has no airport. A river the
    //   full length of the map takes two shores; so only ONE axis carries a
    //   full-length river, and the other's is a tributary that stops in it and takes
    //   one shore. Three crossed, one clear, always.
    //
    //   EVERY RIVER NEEDS TWO BRIDGES. A river is only crossed by the avenues, and a
    //   town whose halves hang on a single bridge is a town where every car in it
    //   queues at the same junction. If the roll leaves a river short, a street beside
    //   it is promoted to an avenue until it has two.
    public partial class RoadDemoBuilder
    {
        [Header("The city plan")]
        [Tooltip("THE city: one number the whole town is drawn from. The same number " +
                 "builds the same streets, the same river, the same blocks, the same " +
                 "mobs standing on them. Ignored while rollCityEachPlay is on.")]
        public int citySeed = 1987;

        [Tooltip("A town nobody has seen before, every Play. Off pins the city to " +
                 "citySeed above - which is what you want the moment something is wrong " +
                 "and you need the same city twice (the seed is printed to the log " +
                 "either way, so a city worth keeping can always be pinned afterwards).")]
        public bool rollCityEachPlay = true;

        [Tooltip("Roll the STREET PLAN itself - how many streets, which are avenues, " +
                 "where the river and the parks lie - rather than taking the arrays as " +
                 "they stand. Off is the authored plan, which is what a lab scene wants: " +
                 "it lays its own grid and the thing under test is not the surveyor.")]
        public bool rollCityPlan = true;

        [Tooltip("How many streets the town runs north-south, and east-west. Both ends " +
                 "inclusive; the roll takes one from each. Under a dozen and the place is " +
                 "a village once the seams have taken their gaps.")]
        public Vector2Int streetsAcross = new Vector2Int(13, 18);
        public Vector2Int streetsDeep = new Vector2Int(8, 11);

        [Tooltip("How many parks the roll may lay, and whether it may leave a strip of " +
                 "wild ground through the town as well.")]
        public Vector2Int parkCount = new Vector2Int(1, 2);
        [Range(0f, 1f)] public float wildStripChance = 0.6f;
        [Tooltip("How often the town's river is joined by a second one coming down to " +
                 "meet it. Never two full rivers - see the note at the head of this file.")]
        [Range(0f, 1f)] public float tributaryChance = 0.55f;

        /// <summary>The seed this city was actually built from, whatever the inspector
        /// said. Printed, and worth printing: it is the whole town in one number.</summary>
        public int BuiltFromSeed { get; private set; }

        // What the repair passes had to do to the roll, said once at the end rather than
        // as it happens. Not tidiness: a Debug.Log inside a repair loop is an internal
        // call, and a headless harness (which is the only thing that can check three
        // hundred cities) dies on the first one and unwinds the whole pass - so the
        // repair that comes AFTER the log never runs and reads exactly like a repair
        // that does not work. Three rounds of chasing stranded junctions that were not
        // there went into learning this.
        readonly List<string> _planNotes = new List<string>();

        // ------------------------------------------------------------------- plan

        /// <summary>The first pass of all. Draws the number of the city, hands every
        /// other pass its own seed off that one number, and - unless a scene lays its
        /// own grid - rolls the street plan and the seams before anything reads them.</summary>
        void PlanCity()
        {
            if (rollCityEachPlay)
                citySeed = Random.Range(1, int.MaxValue - 1);
            BuiltFromSeed = citySeed;

            // Every pass keeps its own seed field, because pinning ONE of them while the
            // rest move is how a question gets answered ("is it the spacing or the
            // closures?"). They are simply all dealt from the city's number now, and by
            // odd multipliers rather than by adding a small offset: seeds one apart make
            // System.Random streams that start alike, and two passes drawing nearly the
            // same numbers is how a city ends up with its parks and its closures on the
            // same lines.
            spacingSeed = Mix(citySeed, 1);
            closureSeed = Mix(citySeed, 2);
            zoneSeed = Mix(citySeed, 3);
            cityLayoutSeed = Mix(citySeed, 4);

            _planNotes.Clear();
            if (rollCityPlan) RollPlan(new System.Random(Mix(citySeed, 5)));

            Debug.Log($"[RoadDemo] city seed {citySeed}: " +
                      $"{verticalRoadX.Length} streets across, {horizontalRoadZ.Length} deep, " +
                      $"{Count(verticalIsBoulevard) + Count(horizontalIsBoulevard)} of them avenues" +
                      SeamStory() +
                      (_planNotes.Count > 0 ? "; " + string.Join("; ", _planNotes) : ""));
        }

        static int Mix(int seed, int stream)
        {
            unchecked
            {
                int h = seed * 73856093 ^ stream * 19349663;
                h ^= h >> 13;
                h *= 1274126177;
                return h == int.MinValue ? 1 : Mathf.Abs(h);
            }
        }

        static int Count(bool[] flags)
        {
            int n = 0;
            if (flags != null) foreach (var f in flags) if (f) n++;
            return n;
        }

        void RollPlan(System.Random rng)
        {
            int nv = Between(rng, streetsAcross, 13);
            int nh = Between(rng, streetsDeep, 8);

            // The positions here are only the fallback spacing: Respace re-lays every
            // line from the block palette (PlanLine), and all it keeps of these is the
            // first one and how many there are. So they are a plain even comb.
            verticalRoadX = Comb(nv, 100f);
            horizontalRoadZ = Comb(nh, 80f);
            verticalIsBoulevard = RollAvenues(rng, nv);
            horizontalIsBoulevard = RollAvenues(rng, nh);

            RollSeams(rng, nv, nh);
        }

        static int Between(System.Random rng, Vector2Int range, int floor)
        {
            int lo = Mathf.Max(floor, Mathf.Min(range.x, range.y));
            int hi = Mathf.Max(lo, Mathf.Max(range.x, range.y));
            return lo + rng.Next(hi - lo + 1);
        }

        static float[] Comb(int n, float step)
        {
            var line = new float[n];
            for (int k = 0; k < n; k++) line[k] = k * step;
            return line;
        }

        // The avenues: never the outermost line - the town's own perimeter street is not
        // an arterial - and one every three or four after the first, which is what puts
        // a wide road within a couple of blocks of anywhere in the city.
        static bool[] RollAvenues(System.Random rng, int n)
        {
            var blvd = new bool[n];
            for (int k = 1 + rng.Next(2); k < n - 1; k += 3 + rng.Next(2)) blvd[k] = true;
            return blvd;
        }

        // ------------------------------------------------------------------ seams

        void RollSeams(System.Random rng, int nv, int nh)
        {
            var list = new List<Seam>();
            // gaps already spoken for, per axis, with their neighbours: two seams in
            // adjacent gaps leave a single row of blocks stranded between two waters
            var takenV = new HashSet<int>();
            var takenH = new HashSet<int>();

            bool Take(bool vertical, int gap)
            {
                var taken = vertical ? takenV : takenH;
                if (taken.Contains(gap)) return false;
                taken.Add(gap - 1); taken.Add(gap); taken.Add(gap + 1);
                return true;
            }

            // a gap somewhere in the body of the grid: never the first or the last, so
            // the town always has a street of blocks outside its water and its parks
            int Pick(bool vertical, System.Random r)
            {
                int gaps = (vertical ? nv : nh) - 1;
                int lo = 2, hi = gaps - 3;
                if (hi < lo) return -1;
                for (int tries = 0; tries < 24; tries++)
                {
                    int gap = lo + r.Next(hi - lo + 1);
                    var taken = vertical ? takenV : takenH;
                    if (!taken.Contains(gap)) return gap;
                }
                return -1;
            }

            // ---- the river. One axis carries it, the whole length of the map.
            bool riverVertical = rng.NextDouble() < 0.5;
            int riverGap = Pick(riverVertical, rng);
            if (riverGap >= 0 && Take(riverVertical, riverGap))
                list.Add(new Seam
                {
                    vertical = riverVertical,
                    gap = riverGap,
                    kind = SeamKind.River,
                    width = 100f + rng.Next(11) * 5f,
                });

            // ---- and its tributary, on the OTHER axis, running from one shore down to
            // the river and stopping in it. Never the full length: that would cross the
            // last two shores as well and leave the airport nowhere to stand.
            if (riverGap >= 0 && rng.NextDouble() < tributaryChance)
            {
                int gap = Pick(!riverVertical, rng);
                if (gap >= 0 && Take(!riverVertical, gap))
                {
                    // The main river lies between road lines riverGap and riverGap + 1 of
                    // the tributary's own cross axis. Coming down from the high shore the
                    // tributary runs from riverGap + 1 to the end; from the low shore, from
                    // the start to riverGap. Either way its mouth lands on the quay road's
                    // outer kerb, which is where the main channel begins (SeamRun).
                    // A tributary needs ROOM: its run has to carry two avenues over it,
                    // and a run of two road lines has no line between its ends to promote.
                    // Measured before this: a river with one bridge, and on the seeds
                    // where the run was shorter still, a corner of the grid nothing could
                    // drive to. So the end with more room wins, and if neither has three
                    // lines the town simply has the one river.
                    int crossLast = (riverVertical ? nh : nv) - 1;
                    int highRun = crossLast - (riverGap + 1) + 1;
                    int lowRun = riverGap + 1;
                    bool fromHigh = highRun == lowRun ? rng.NextDouble() < 0.5 : highRun > lowRun;
                    if (Mathf.Max(highRun, lowRun) >= 3)
                        list.Add(new Seam
                        {
                            vertical = !riverVertical,
                            gap = gap,
                            kind = SeamKind.River,
                            // a pad size, so the dry column past the confluence is a lot the
                            // catalog has blocks composed for
                            width = 85f,
                            fromRoad = fromHigh ? riverGap + 1 : -1,
                            toRoad = fromHigh ? -1 : riverGap,
                        });
                }
            }

            // ---- the parks, and a strip of wild ground if the roll wants one
            int parks = Between(rng, parkCount, 0);
            for (int k = 0; k < parks; k++)
            {
                bool vertical = rng.NextDouble() < 0.5;
                int gap = Pick(vertical, rng);
                if (gap < 0) { gap = Pick(!vertical, rng); vertical = !vertical; }
                if (gap < 0 || !Take(vertical, gap)) continue;
                list.Add(new Seam
                {
                    vertical = vertical,
                    gap = gap,
                    kind = SeamKind.Park,
                    width = 50f + rng.Next(5) * 5f,
                });
            }
            if (rng.NextDouble() < wildStripChance)
            {
                bool vertical = rng.NextDouble() < 0.5;
                int gap = Pick(vertical, rng);
                if (gap < 0) { gap = Pick(!vertical, rng); vertical = !vertical; }
                if (gap >= 0 && Take(vertical, gap))
                    list.Add(new Seam
                    {
                        vertical = vertical,
                        gap = gap,
                        kind = SeamKind.Wild,
                        width = 70f + rng.Next(5) * 5f,
                    });
            }

            seams = list.ToArray();
            // Twice round, and the order matters both ways. JoinTheGrid promotes streets
            // that EnsureCrossings never looked at, and every promotion opens a line
            // across every seam it crosses - which can leave a seam with more ways over
            // it than it had, and can never leave one with fewer. Running the pair until
            // nothing changes is cheaper than reasoning about which of them goes first.
            for (int round = 0; round < 3; round++)
            {
                EnsureCrossings();
                if (!JoinTheGrid()) break;
            }
        }

        /// <summary>Every junction drivable from every other, before a single street is
        /// closed.
        ///
        /// Two crossings per seam is not enough on its own, and this is the part that is
        /// not obvious: a seam's crossings can BOTH lie on the far side of another seam.
        /// A park down column gap 2 and a river along row gap 2 cut the south-west corner
        /// of the town out as a square of its own, and each of them can honestly report
        /// two ways across it - both of them somewhere else. Measured: six of sixty
        /// rolled cities had a pocket of four to eight junctions nothing could drive to,
        /// with every seam satisfied.
        ///
        /// So the plan is checked the way the closure pass checks itself, and repaired
        /// the same way it was cut: the street between a junction the network reaches and
        /// one it does not is promoted to an avenue, which opens it across every seam it
        /// crosses. Each pass joins at least one pocket, so a dozen is far more than a
        /// grid this size can need.</summary>
        bool JoinTheGrid()
        {
            int nv = verticalRoadX.Length, nh = horizontalRoadZ.Length;
            bool any = false;
            // one promotion per pass, and there are only so many streets to promote: the
            // cap is the whole grid rather than a round dozen, because a city of eighteen
            // streets by eleven that the roll cut badly needs more than twelve and the
            // twelve-pass version shipped six stranded pockets in three hundred cities
            for (int pass = 0; pass < nv + nh; pass++)
            {
                var seen = Reached(nv, nh);
                bool joined = false;
                for (int i = 0; i < nv && !joined; i++)
                    for (int j = 0; j < nh && !joined; j++)
                    {
                        if (!seen[i, j]) continue;
                        // a vertical road carries the segment north; a horizontal one east
                        if (j + 1 < nh && !seen[i, j + 1] && !verticalIsBoulevard[i])
                        { verticalIsBoulevard[i] = true; joined = true; break; }
                        if (i + 1 < nv && !seen[i + 1, j] && !horizontalIsBoulevard[j])
                        { horizontalIsBoulevard[j] = true; joined = true; break; }
                    }
                if (!joined) break;
                any = true;
                _planNotes.Add("a pocket of the grid had no way out of it, so a street beside " +
                               "the seam that cut it is an avenue now");
            }
            return any;
        }

        bool[,] Reached(int nv, int nh)
        {
            var seen = new bool[nv, nh];
            var queue = new Queue<(int i, int j)>();
            seen[0, 0] = true;
            queue.Enqueue((0, 0));
            while (queue.Count > 0)
            {
                var (i, j) = queue.Dequeue();
                void Step(int ni, int nj, bool open)
                {
                    if (ni < 0 || ni >= nv || nj < 0 || nj >= nh || seen[ni, nj] || !open) return;
                    seen[ni, nj] = true;
                    queue.Enqueue((ni, nj));
                }
                Step(i, j + 1, j + 1 < nh && SegmentOpen(true, i, j));
                Step(i, j - 1, j > 0 && SegmentOpen(true, i, j - 1));
                Step(i + 1, j, i + 1 < nv && SegmentOpen(false, j, i));
                Step(i - 1, j, i > 0 && SegmentOpen(false, j, i - 1));
            }
            return seen;
        }

        /// <summary>Every seam crossed by at least two avenues.
        ///
        /// A river is not the only thing that cuts the town: SegmentOpen gives the SAME
        /// answer for a park and for a river - only an avenue goes through, an ordinary
        /// street stops at the edge - so a park the roll happened to lay where no avenue
        /// crosses it splits the city exactly as a bridgeless river does. Measured over
        /// sixty rolled cities before this: three of them had a corner of the grid
        /// nothing could drive to, and two had a river hanging on one bridge. The
        /// closure pass cannot save it either - it measures its own baseline against
        /// whatever the seams left, so a grid already broken stays broken.
        ///
        /// Two, not one: one crossing keeps the halves joined and puts every car in the
        /// town through the same junction.</summary>
        void EnsureCrossings()
        {
            if (seams == null) return;
            foreach (var s in seams)
            {
                if (s == null || s.kind == SeamKind.Highway) continue;   // every road goes under a deck
                // a vertical seam is crossed by the horizontal roads, and only over the
                // stretch it actually runs
                var blvd = s.vertical ? horizontalIsBoulevard : verticalIsBoulevard;
                var (lo, hi) = SeamRoads(s);
                int have = 0;
                for (int r = lo; r <= hi; r++) if (blvd[r]) have++;
                if (have >= 2) continue;

                // outward from the middle of the seam's own run, and never the town's
                // outermost street - the perimeter is not an arterial
                int mid = (lo + hi) / 2;
                for (int step = 0; step <= hi - lo && have < 2; step++)
                    foreach (int r in new[] { mid + step, mid - step })
                    {
                        if (r < lo || r > hi || r == 0 || r == blvd.Length - 1 || blvd[r]) continue;
                        blvd[r] = true;
                        have++;
                        _planNotes.Add($"the {(s.vertical ? "north-south" : "east-west")} " +
                                       $"{s.kind.ToString().ToLowerInvariant()} had {have - 1} way(s) " +
                                       $"across it, so crossing street {r} is an avenue now");
                        break;
                    }
                if (have < 2)
                    _planNotes.Add($"WARNING the {s.kind} in {(s.vertical ? "column" : "row")} gap " +
                                   $"{s.gap} has only {have} way(s) across it and no street left to promote");
            }
        }
    }
}
