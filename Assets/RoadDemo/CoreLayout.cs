using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// The city core's layout: the blocks harvested out of the POLYGON City demo scene,
    /// standing exactly where Synty stood them, with the demo's streets widened to the
    /// city's. One source of truth for the drawing in the editor and the district in the
    /// game (Docs/core-district-plan.md, Docs/synty-demo-anatomy.md).
    ///
    /// The demo's own arrangement is kept because it is the one arrangement of these
    /// blocks that is known to work: every gap between two kerbs is a street, a one-way
    /// lane, a car park or a courtyard, and nothing is left over. What the demo has and
    /// the city does not is streets of 10 m with no parking strip - so the layout carries
    /// a list of CUTS: straight lines along which everything beyond the line is moved
    /// outward, which widens the street on that line and nothing else. All measures are
    /// multiples of 5 m, so every gap that results is again a whole number of cells.
    ///
    /// Positions here are the DEMO's coordinates (metres; +X east, +Z north; the main road
    /// runs along z -10..0). A host that stands the core somewhere else in the world does
    /// so through its DistrictFrame, never by editing these numbers.
    /// </summary>
    public static class CoreLayout
    {
        public const float Cell = 5f;
        public const string BlocksDir = "Assets/Prefabs/CoreBlocks/";

        /// <summary>A block prefab and where its pivot stood in the demo. The pivot is the
        /// one the harvest gave the prefab (the middle of its ground box, on the 5 m beat);
        /// the demo positions were read back by matching every piece of every prefab to
        /// the original scene - all seventeen matched to the piece.</summary>
        public struct Stand
        {
            public string Prefab;
            public float X, Z;
            public Stand(string prefab, float x, float z) { Prefab = prefab; X = x; Z = z; }
        }

        /// <summary>The blocks, in the demo's arrangement. city-hall-block is left out: it
        /// is the tray-baked copy of the same block as block-03, with fewer pieces.</summary>
        public static readonly Stand[] Blocks =
        {
            new Stand("block-01", -90f, -55f),
            new Stand("block-02", -85f, 15f),
            new Stand("block-03", -55f, -95f),
            new Stand("block-04", -50f, -120f),
            new Stand("block-05", -40f, 35f),
            new Stand("block-06", -35f, -40f),
            new Stand("block-07", -20f, -30f),
            new Stand("block-08", -20f, -95f),
            new Stand("block-09", 35f, -40f),
            new Stand("block-10", 35f, 15f),
            new Stand("block-11", 50f, 120f),
            new Stand("block-12", 50f, 50f),
            new Stand("block-13", 75f, -40f),
            new Stand("block-14", 115f, -20f),
            new Stand("block-15", 120f, 125f),
            new Stand("block-16", 120f, 55f),
        };

        /// <summary>
        /// A line the layout is opened along. Everything whose box lies wholly on the far
        /// side of the line (at or beyond <see cref="At"/> for a positive delta, at or
        /// short of it for a negative one) and overlaps the line's span moves by
        /// <see cref="Delta"/>. A block that straddles the line never moves, which is
        /// what keeps a cut from tearing a block in two.
        /// </summary>
        public struct Cut
        {
            public bool Vertical;      // a line x = At (moves things along x); else z = At (along z)
            public float At;
            public float From, To;     // the span of the line along the other axis
            public float Delta;
            public string Why;
            public Cut(bool vertical, float at, float from, float to, float delta, string why)
            { Vertical = vertical; At = at; From = from; To = to; Delta = delta; Why = why; }
        }

        const float Any = 100000f;

        /// <summary>The demo's 10 m streets opened to the city's 15, and the main road to
        /// the city's 35 m boulevard. In the demo's coordinates; a cut's effect is judged
        /// against the demo positions, so their order does not matter.</summary>
        public static readonly Cut[] Cuts =
        {
            new Cut(false, 0f, -Any, Any, 25f, "the main road z -10..0 to a boulevard"),
            new Cut(true, -75f, -110f, -10f, -5f, "street S1, x -75..-65, opened westward so S2 and S3 run straight across the boulevard"),
            new Cut(true, 5f, -Any, Any, 5f, "street S2, x -5..5 (the 15 m stretch north gets a strip)"),
            new Cut(true, 100f, -Any, -10f, 5f, "street S3 south, x 90..100"),
            new Cut(true, 100f, 0f, 105f, 5f, "street S3 middle, x 95..105"),
            new Cut(true, 105f, 110f, Any, 5f, "street S3 north, x 95..105"),
            new Cut(false, -80f, -65f, 90f, -5f, "street z -80..-70, opened southward"),
            // the street z -120..-110 runs through block-04's bay; the block's box reaches
            // z -105, so the line is drawn there
            new Cut(false, -105f, -95f, -5f, -5f, "street z -120..-110, opened southward"),
            // the street z 95..105 is a car park's edge plus a 5 m lane: ten metres more
            new Cut(false, 105f, -Any, Any, 10f, "street z 95..105 and the lane z 100..105"),
        };

        /// <summary>The main road, kerb to kerb (z), as it stands after the cuts: the
        /// demo's z -10..0 opened to the boulevard. It runs the whole width of the core.</summary>
        public static Vector2 MainRoad
        {
            get
            {
                var north = Shift(new Rect(-Any, 0f, 2f * Any, Cell));
                return new Vector2(-10f, 0f + north.y);
            }
        }

        /// <summary>A one-way lane of the demo: five metres of bare asphalt between two
        /// kerbs, an arrow at each mouth. Direction +1 runs north (vertical) or east.</summary>
        public struct Lane
        {
            public bool Vertical;
            public float At;           // the lane's west (vertical) or south (horizontal) kerb
            public float From, To;     // its run along its own axis
            public int Direction;
            public Lane(bool vertical, float at, float from, float to, int direction)
            { Vertical = vertical; At = at; From = from; To = to; Direction = direction; }
            public Rect Box => Vertical ? new Rect(At, From, Cell, To - From) : new Rect(From, At, To - From, Cell);
        }

        /// <summary>The demo's lanes and the way they run. The directions are read off the
        /// arrow tiles where the demo painted one and chosen to alternate where it did not.</summary>
        public static readonly Lane[] Lanes =
        {
            new Lane(true, -100f, -140f, -100f, +1),
            new Lane(true, -75f, 0f, 65f, -1),
            new Lane(true, -45f, -50f, -10f, +1),
            new Lane(true, 60f, -70f, -10f, +1),
            new Lane(true, 60f, 0f, 35f, +1),
            new Lane(true, 135f, 110f, 140f, -1),
            new Lane(false, 65f, -70f, -10f, -1),
            new Lane(false, 100f, 40f, 95f, -1),
            new Lane(false, 105f, 105f, 135f, +1),
            new Lane(false, -50f, -45f, -5f, -1),
            new Lane(false, -145f, -95f, -5f, +1),
            new Lane(false, 35f, 5f, 60f, -1),
            new Lane(true, -40f, -110f, -80f, -1),
        };

        /// <summary>How far the cuts move a box that stands at <paramref name="box"/> in
        /// the demo (x, z as Rect x, y).</summary>
        public static Vector2 Shift(Rect box)
        {
            float dx = 0f, dz = 0f;
            foreach (var cut in Cuts)
            {
                float lo = cut.Vertical ? box.xMin : box.yMin;
                float hi = cut.Vertical ? box.xMax : box.yMax;
                float spanLo = cut.Vertical ? box.yMin : box.xMin;
                float spanHi = cut.Vertical ? box.yMax : box.xMax;
                if (spanHi <= cut.From + 0.01f || spanLo >= cut.To - 0.01f) continue;
                bool beyond = cut.Delta > 0f ? lo >= cut.At - 0.01f : hi <= cut.At + 0.01f;
                if (!beyond) continue;
                if (cut.Vertical) dx += cut.Delta; else dz += cut.Delta;
            }
            return new Vector2(dx, dz);
        }

        // ------------------------------------------------------------------ the blocks

        /// <summary>A block as measured off its instance: its ground, its shape, its height.</summary>
        public sealed class Block
        {
            public string Name;
            public GameObject Go;
            public Vector2 Pivot;              // where the demo stood it
            public Vector2 Shift;              // what the cuts add
            public Bounds Ground;              // pivot-relative box of its paving
            public int CW, CD;                 // the box in cells
            public bool[,] Mask;               // which cells of the box the block fills, [i along x, j along z]
            public int Cells;
            public float MaxH, MeanH;
            public int Buildings, Pieces;

            /// <summary>The ground box in the demo's frame, before the cuts.</summary>
            public Rect DemoBox => new Rect(Pivot.x + Ground.min.x, Pivot.y + Ground.min.z, CW * Cell, CD * Cell);
            /// <summary>The ground box where the block stands now.</summary>
            public Rect Box => new Rect(Pivot.x + Shift.x + Ground.min.x, Pivot.y + Shift.y + Ground.min.z, CW * Cell, CD * Cell);
            public Vector3 Position => new Vector3(Pivot.x + Shift.x, 0f, Pivot.y + Shift.y);
        }

        /// <summary>
        /// Reads a block off an instance of its prefab standing at the origin, unturned:
        /// its ground (the paving the demo gave it, which is its kerb line), what stands
        /// on that ground, and how tall it is. Pieces are the instance's direct children -
        /// the harvest laid every piece flat under the block's root.
        /// </summary>
        public static Block Measure(string name, GameObject go, Vector2 pivot)
        {
            var block = new Block { Name = name, Go = go, Pivot = pivot };
            var cover = new List<Bounds>();
            bool anyGround = false;
            float sumHA = 0f, sumA = 0f;
            foreach (Transform t in go.transform)
            {
                block.Pieces++;
                if (!BoxOf(t.gameObject, out var box)) continue;
                string piece = t.name;
                bool building = Starts(piece, "SM_Bld_");
                bool ground = box.size.y <= 1.5f &&
                              (Starts(piece, "SM_Env_Sidewalk") || Starts(piece, "SM_Env_Grass") ||
                               Starts(piece, "SM_Env_Road") || piece.StartsWith("floor patch"));
                if (ground)
                {
                    if (!anyGround) { block.Ground = box; anyGround = true; }
                    else block.Ground.Encapsulate(box);
                }
                // environment pieces by their footprint, props by their foot: a lamp's box
                // reaches out over the road it lights
                if (ground || building || Starts(piece, "SM_Env_")) cover.Add(box);
                else cover.Add(new Bounds(t.position, Vector3.one * 0.5f));
                if (!building) continue;
                block.Buildings++;
                float h = box.max.y, a = Mathf.Max(1f, box.size.x * box.size.z);
                if (h > block.MaxH) block.MaxH = h;
                sumHA += h * a;
                sumA += a;
            }
            if (!anyGround)
            {
                bool any = false;
                foreach (var box in cover)
                {
                    if (!any) { block.Ground = box; any = true; }
                    else block.Ground.Encapsulate(box);
                }
            }
            block.MeanH = sumA > 0f ? sumHA / sumA : 0f;
            block.CW = Mathf.Max(1, Mathf.RoundToInt(block.Ground.size.x / Cell));
            block.CD = Mathf.Max(1, Mathf.RoundToInt(block.Ground.size.z / Cell));
            Shape(block, cover);
            block.Shift = Shift(block.DemoBox);
            return block;
        }

        /// <summary>Stands the instance where the layout puts it.</summary>
        public static void Place(Block block)
        {
            block.Go.transform.SetPositionAndRotation(block.Position, Quaternion.identity);
        }

        /// <summary>
        /// The block's shape on its own 5 m grid: a cell is the block's when something
        /// stands on it, or when it is shut in by what stands - a courtyard is the block's.
        /// A cell open to the block's edge across bare ground is a bay the demo's kerb ran
        /// round - the demo's car parks, and the lanes and smaller blocks it tucked into
        /// the big blocks' corners.
        /// </summary>
        static void Shape(Block block, List<Bounds> cover)
        {
            int w = block.CW, d = block.CD;
            float x0 = block.Ground.min.x, z0 = block.Ground.min.z;
            var covered = new bool[w, d];
            foreach (var box in cover)
                for (int i = 0; i < w; i++)
                    for (int j = 0; j < d; j++)
                    {
                        float cx = x0 + (i + 0.5f) * Cell, cz = z0 + (j + 0.5f) * Cell;
                        if (cx > box.min.x && cx < box.max.x && cz > box.min.z && cz < box.max.z)
                            covered[i, j] = true;
                    }

            var open = new bool[w, d];
            var todo = new Queue<Vector2Int>();
            for (int i = 0; i < w; i++)
                for (int j = 0; j < d; j++)
                    if (!covered[i, j] && (i == 0 || j == 0 || i == w - 1 || j == d - 1))
                    {
                        open[i, j] = true;
                        todo.Enqueue(new Vector2Int(i, j));
                    }
            while (todo.Count > 0)
            {
                var c = todo.Dequeue();
                foreach (var n in new[] { new Vector2Int(c.x - 1, c.y), new Vector2Int(c.x + 1, c.y),
                                          new Vector2Int(c.x, c.y - 1), new Vector2Int(c.x, c.y + 1) })
                {
                    if (n.x < 0 || n.y < 0 || n.x >= w || n.y >= d) continue;
                    if (covered[n.x, n.y] || open[n.x, n.y]) continue;
                    open[n.x, n.y] = true;
                    todo.Enqueue(n);
                }
            }

            block.Mask = new bool[w, d];
            block.Cells = 0;
            for (int i = 0; i < w; i++)
                for (int j = 0; j < d; j++)
                    if (!open[i, j]) { block.Mask[i, j] = true; block.Cells++; }
        }

        static bool BoxOf(GameObject go, out Bounds box)
        {
            box = new Bounds();
            bool any = false;
            foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                if (!any) { box = renderer.bounds; any = true; }
                else box.Encapsulate(renderer.bounds);
            }
            return any;
        }

        static bool Starts(string name, string prefix) =>
            name.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase);
    }
}
