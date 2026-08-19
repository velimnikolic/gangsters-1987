using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    // The city's own quarters. Outside the grid a place is a DISTRICT - the port, the
    // field, a village with its own streets and its own ground. Inside it there is
    // nothing to build: the quarter IS the blocks that are already there. What it
    // needs is a NAME, because a town whose map says KINGSPORT across the middle and
    // nothing else has no places in it - nobody runs numbers "in the city", they run
    // them on Bricktown, they lean on the shopkeepers on Cannery Row.
    //
    // So a quarter here is a rectangle of blocks with a name on it:
    //   - a few blocks across and a few deep (quarterBlocksAcross / Deep);
    //   - never across a SEAM - the river, a park, the wild strip: those are what a
    //     town's own people take as the edge of their neighbourhood, and a name that
    //     straddled the river would be printed over the water;
    //   - named off the city's seed, so the same town keeps its quarters, with the
    //     one over the middle of the grid called Downtown.
    // The map prints them; nothing is built.
    public partial class RoadDemoBuilder
    {
        [Header("City quarters (the named parts of the grid)")]
        [Tooltip("How many blocks across one named quarter of the city runs. Three of " +
                 "these blocks is about three hundred metres - a name you can print on " +
                 "the map and a piece of town you can be told to take over.")]
        [Range(1, 8)] public int quarterBlocksAcross = 3;
        [Tooltip("And how many deep. The rows of this grid are shallower than the " +
                 "columns are wide, so two of them come out about square with three across.")]
        [Range(1, 8)] public int quarterBlocksDeep = 2;

        /// <summary>One named part of the city: the ground it covers, kerb to kerb of the
        /// roads round it. The map prints the name across it; the ledger will want the
        /// rectangle when a crew is told to work a quarter.</summary>
        public readonly struct CityQuarter
        {
            public readonly string Name;
            public readonly Rect World;
            /// <summary>The blocks it holds: column and row indices into the grid's gaps.</summary>
            public readonly int Col0, Col1, Row0, Row1;

            public CityQuarter(string name, Rect world, int col0, int col1, int row0, int row1)
            {
                Name = name; World = world; Col0 = col0; Col1 = col1; Row0 = row0; Row1 = row1;
            }

            public bool Holds(int i, int j) => i >= Col0 && i <= Col1 && j >= Row0 && j <= Row1;
        }

        readonly List<CityQuarter> _quarters = new List<CityQuarter>();

        /// <summary>Every named quarter of the grid, west to east and south to north.</summary>
        public IReadOnlyList<CityQuarter> CityQuarters => _quarters;

        /// <summary>The quarter a point stands in, or null out in the wild.</summary>
        public string QuarterAt(float x, float z)
        {
            for (int i = 0; i < _quarters.Count; i++)
                if (_quarters[i].World.Contains(new Vector2(x, z))) return _quarters[i].Name;
            return null;
        }

        // ------------------------------------------------------------------ plan

        void PlanQuarters()
        {
            _quarters.Clear();
            if (verticalRoadX == null || horizontalRoadZ == null ||
                verticalRoadX.Length < 2 || horizontalRoadZ.Length < 2) return;

            var cols = Chunks(true, Mathf.Max(1, quarterBlocksAcross));
            var rows = Chunks(false, Mathf.Max(1, quarterBlocksDeep));
            if (cols.Count == 0 || rows.Count == 0) return;

            var names = Streets;
            int taken = 0;
            foreach (var c in cols)
                foreach (var r in rows)
                {
                    var world = Rect.MinMaxRect(verticalRoadX[c.lo], horizontalRoadZ[r.lo],
                                                verticalRoadX[c.hi + 1], horizontalRoadZ[r.hi + 1]);
                    string name = names != null ? names.Quarter(taken++) : "Quarter " + taken++;
                    _quarters.Add(new CityQuarter(name, world, c.lo, c.hi, r.lo, r.hi));
                }

            // the middle of the grid is Downtown, whatever it drew: every American town
            // of this size has one and the map is read from it outward
            int best = -1;
            float nearest = float.MaxValue;
            var mid = new Vector2((verticalRoadX[0] + verticalRoadX[verticalRoadX.Length - 1]) * 0.5f,
                                  (horizontalRoadZ[0] + horizontalRoadZ[horizontalRoadZ.Length - 1]) * 0.5f);
            for (int k = 0; k < _quarters.Count; k++)
            {
                float d = (_quarters[k].World.center - mid).sqrMagnitude;
                if (d >= nearest) continue;
                nearest = d; best = k;
            }
            if (best >= 0)
            {
                var q = _quarters[best];
                _quarters[best] = new CityQuarter("Downtown", q.World, q.Col0, q.Col1, q.Row0, q.Row1);
            }

            var story = new List<string>();
            foreach (var q in _quarters) story.Add(q.Name);
            Debug.Log($"[RoadDemo] city quarters ({quarterBlocksAcross}x{quarterBlocksDeep} blocks each): " +
                      string.Join(", ", story));
        }

        /// <summary>The grid's blocks along one axis, cut into runs of <paramref name="size"/>.
        /// A seam - the river, a park, the wild strip - is not a block and ends the run it
        /// falls in: a neighbourhood stops at the water, it does not straddle it. A run
        /// with one block left over at its end gives that block to the group before it,
        /// so no quarter is a single column of houses with a name of its own.</summary>
        List<(int lo, int hi)> Chunks(bool verticalLines, int size)
        {
            var axis = verticalLines ? verticalRoadX : horizontalRoadZ;
            var groups = new List<(int lo, int hi)>();
            int runFrom = -1;

            void Close(int runTo)
            {
                if (runFrom < 0) return;
                int count = runTo - runFrom + 1;
                for (int at = runFrom; at <= runTo;)
                {
                    int last = Mathf.Min(at + size - 1, runTo);
                    // a stub of one at the end joins what came before it
                    if (runTo - last == 1 && count > size) last = runTo;
                    groups.Add((at, last));
                    at = last + 1;
                }
                runFrom = -1;
            }

            for (int gap = 0; gap + 1 < axis.Length; gap++)
            {
                bool seam = SeamAt(verticalLines, gap) != null;
                if (seam) { Close(gap - 1); continue; }
                if (runFrom < 0) runFrom = gap;
                if (gap + 2 >= axis.Length) Close(gap);
            }
            return groups;
        }
    }
}
