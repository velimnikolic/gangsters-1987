using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// Lifts the POLYGON City demo scene out whole and files it as one prefab, so the
    /// city can stand the artist's own downtown in the middle of itself.
    ///
    /// The pack ships a hand-built city - Assets/Synty/PolygonCity/Scenes/Demo.unity,
    /// five thousand instances laid on a 5 m grid by the person who made the models.
    /// Its blocks have what ours do not: a service alley a car can drive down, a lot
    /// of parking in front and another behind, skips and fire escapes down the side
    /// returns, hoardings on the roofs, arrows and bay lines painted on the ground.
    /// Composing that block by block is months of work and it would still be our
    /// guess at their arrangement. This copies the arrangement itself.
    ///
    /// It is a PREFAB rather than an additive scene load because a scene cannot be
    /// moved: the city has to put its downtown where its own grid leaves the hole,
    /// and a prefab instance takes a position, a rotation and a parent like anything
    /// else this builder stands up.
    ///
    /// Every piece is re-instantiated FROM ITS OWN SOURCE PREFAB rather than copied,
    /// so what lands on disk is five thousand references and five thousand transforms
    /// - a few megabytes - instead of five thousand copies of the meshes.
    /// </summary>
    public static class SyntyDemoBlockRip
    {
        const string DemoScene = "Assets/Synty/PolygonCity/Scenes/Demo.unity";
        const string OutDir = "Assets/CityKit/Downtown";
        const string TownName = "synty-downtown";

        /// <summary>What the demo scene carries that the CITY carries too, and would
        /// carry twice: its own camera, its own sun, its own volume and probe, and the
        /// ocean plane it floats on (the city stands on its own island).</summary>
        static readonly string[] SkipRoots =
        {
            "Main Camera", "Directional Light", "Global Volume", "Reflection Probe",
            "Ocean_Plane", "Ocean_Floor", "FX",
        };

        /// <summary>And what is HORIZON rather than city: the painted skyline flat that
        /// stands round the demo pretending to be the rest of the town, the skydome,
        /// and any water. The city brings its own sky, its own sea and its own island;
        /// a downtown that carried theirs would put a painted skyline in the middle of
        /// our streets and a second sea inside the first.</summary>
        static readonly string[] SkipNames =
        {
            "SM_Env_Skyline", "SM_Gen_Env_Skydome", "SM_Env_Water", "Ocean", "Water",
        };

        /// <summary>Anything wider than this is not a building. Backdrops, ground
        /// planes and sky are the only things in the pack at that size.</summary>
        const float MaxPiece = 400f;

        [MenuItem("Tools/City/Catalog/Synty Demo: Rip Town", priority = 40)]
        public static void RipTown()
        {
            var scene = EditorSceneManager.OpenScene(DemoScene, OpenSceneMode.Additive);
            try
            {
                if (!AssetDatabase.IsValidFolder(OutDir))
                    AssetDatabase.CreateFolder("Assets/CityKit", "Downtown");

                var pieces = Pieces(scene, out var box);
                if (pieces.Count == 0) { Debug.LogError("[SyntyTown] nothing in " + DemoScene); return; }

                // the town is re-hung round the middle of its own ground, so standing
                // it anywhere is a matter of putting the root there: the demo was laid
                // out from x -155 to 195, which is not centred on anything
                var centre = new Vector3(box.center.x, 0f, box.center.z);
                var root = new GameObject(TownName);
                root.transform.position = Vector3.zero;

                int stood = 0, missing = 0;
                foreach (var p in pieces)
                {
                    var src = PrefabUtility.GetCorrespondingObjectFromSource(p);
                    GameObject copy;
                    if (src != null)
                        copy = (GameObject)PrefabUtility.InstantiatePrefab(src, root.transform);
                    else { copy = Object.Instantiate(p, root.transform); missing++; }
                    copy.name = p.name;
                    copy.transform.SetPositionAndRotation(
                        p.transform.position - centre, p.transform.rotation);
                    copy.transform.localScale = p.transform.localScale;
                    stood++;
                }

                var mark = root.AddComponent<RoadDemo.SyntyTownMark>();
                mark.span = new Vector2(box.size.x, box.size.z);

                string path = $"{OutDir}/{TownName}.prefab";
                PrefabUtility.SaveAsPrefabAsset(root, path);
                Object.DestroyImmediate(root);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                Debug.Log($"[SyntyTown] {stood} pieces ({missing} plain copies) " +
                          $"spanning {box.size.x:F0} x {box.size.z:F0} m -> {path}");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        // ------------------------------------------------------------ single blocks

        const string BlocksDir = "Assets/CityKit/Blocks";

        /// <summary>The pack's module. Every road and pavement tile is this square.</summary>
        const float Cell = 5f;

        /// <summary>Smaller than this on a side is a traffic island or a strip of
        /// planting, not an interior worth a bake.</summary>
        const float MinSide = 15f;

        /// <summary>Bigger is not a block: it is the park, or the open ground at the
        /// edge of their map where the city stops.</summary>
        const float MaxSide = 130f;

        /// <summary>How square an island has to be before its bounding rectangle is
        /// honest. Two blocks touching at a corner fill far less than this and are
        /// dropped rather than baked as one oversized slab.</summary>
        const float MinFill = 0.55f;

        /// <summary>What the STREET is made of. Note what is NOT here: Road_Bare_01
        /// and Road_Patch_01. Those are the pack's plain asphalt, and in this demo they
        /// are what the yards, the parking lots and the alleys THROUGH a block are
        /// paved with - the very thing being copied. Counting them as carriageway
        /// welded every block to the road network and left three holes in the whole
        /// city instead of thirty blocks.</summary>
        static readonly string[] Carriageway =
        {
            "SM_Env_Road_01", "SM_Env_Road_02", "SM_Env_Road_03",
            "SM_Env_Road_Lines_", "SM_Env_Road_YellowLines_", "SM_Env_Road_Arrow_",
            "SM_Env_Road_Crossing_", "SM_Env_Road_Median_", "SM_Env_Bridge_",
        };

        /// <summary>The pavement. Kept apart from the carriageway because the two
        /// answer different questions: a pavement rings EVERY block, so a column of
        /// cells full of pavement says nothing about where a street runs, while a
        /// column full of carriageway is a street and nothing else.</summary>
        static readonly string[] Pavement = { "SM_Env_Sidewalk_" };

        [MenuItem("Tools/City/Catalog/Synty Demo: Measure Blocks", priority = 41)]
        public static void MeasureBlocks() => Blocks(write: false);

        [MenuItem("Tools/City/Catalog/Synty Demo: Rip Blocks", priority = 42)]
        public static void RipBlocks() => Blocks(write: true);

        struct Part
        {
            public GameObject Go;
            public Bounds Box;
            public bool Street;
            public bool Road;
        }

        static void Blocks(bool write)
        {
            var scene = EditorSceneManager.OpenScene(DemoScene, OpenSceneMode.Additive);
            try
            {
                var pieces = Pieces(scene, out _);
                var parts = new List<Part>(pieces.Count);
                bool any = false;
                Bounds world = new Bounds();
                foreach (var go in pieces)
                {
                    var rs = go.GetComponentsInChildren<Renderer>(true);
                    if (rs.Length == 0) continue;
                    var box = rs[0].bounds;
                    for (int i = 1; i < rs.Length; i++) box.Encapsulate(rs[i].bounds);
                    // the painted skyline flat and the skydome are scenery on the
                    // horizon, not city, and either one swallows the whole grid
                    if (box.size.x > 400f || box.size.z > 400f) continue;
                    bool onRoad = Named(go, Carriageway);
                    parts.Add(new Part { Go = go, Box = box, Road = onRoad,
                                         Street = onRoad || Named(go, Pavement) });
                    if (!any) { world = box; any = true; } else world.Encapsulate(box);
                }
                if (parts.Count == 0) { Debug.LogError("[SyntyBlocks] nothing in " + DemoScene); return; }

                int x0 = Mathf.FloorToInt(world.min.x / Cell), x1 = Mathf.CeilToInt(world.max.x / Cell);
                int z0 = Mathf.FloorToInt(world.min.z / Cell), z1 = Mathf.CeilToInt(world.max.z / Cell);
                int nx = x1 - x0, nz = z1 - z0;
                var street = new bool[nx, nz];
                var road = new bool[nx, nz];
                var used = new bool[nx, nz];
                foreach (var p in parts)
                    foreach (var c in Cells(p.Box, x0, z0, nx, nz))
                    {
                        used[c.x, c.y] = true;
                        if (p.Street) street[c.x, c.y] = true;
                        if (p.Road) road[c.x, c.y] = true;
                    }

                var lots = Lots(road, street, used, nx, nz, x0, z0);
                lots.Sort((a, b) => (b.width * b.height).CompareTo(a.width * a.height));

                var said = new System.Text.StringBuilder();
                foreach (var b in lots)
                    said.Append($"\n    {b.width,5:F0} x {b.height,-5:F0}  at ({b.center.x,7:F1}, {b.center.y,7:F1})");
                Debug.Log($"[SyntyBlocks] {parts.Count} pieces, {lots.Count} block(s):{said}");

                if (!write) return;
                if (!AssetDatabase.IsValidFolder(BlocksDir)) AssetDatabase.CreateFolder("Assets/CityKit", "Blocks");
                int made = 0;
                foreach (var b in lots) if (Bake(b, parts)) made++;
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log($"[SyntyBlocks] {made} block prefab(s) written to {BlocksDir}");
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }

        static bool Named(GameObject go, string[] prefixes)
        {
            var src = PrefabUtility.GetCorrespondingObjectFromSource(go);
            string name = src != null ? src.name : go.name;
            foreach (var prefix in prefixes)
                if (name.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>Which 5 m cells a piece stands in. The box comes off the renderers
        /// rather than the transform because the pack's road tiles pivot at a CORNER:
        /// a tile at (50, 80) covers x 45..50, and a position test files it one cell
        /// out in both axes. Half a cell of slack either side, because a kerb stands
        /// a few centimetres proud of the square it was cut from.</summary>
        static IEnumerable<Vector2Int> Cells(Bounds box, int x0, int z0, int nx, int nz)
        {
            int ax = Mathf.FloorToInt((box.min.x + 0.5f) / Cell) - x0;
            int bx = Mathf.FloorToInt((box.max.x - 0.5f) / Cell) - x0;
            int az = Mathf.FloorToInt((box.min.z + 0.5f) / Cell) - z0;
            int bz = Mathf.FloorToInt((box.max.z - 0.5f) / Cell) - z0;
            for (int i = Mathf.Max(0, ax); i <= Mathf.Min(nx - 1, bx); i++)
                for (int j = Mathf.Max(0, az); j <= Mathf.Min(nz - 1, bz); j++)
                    yield return new Vector2Int(i, j);
        }

        /// <summary>Cut the town into blocks.
        ///
        /// Two things that do not work, and why, because both look right on paper:
        ///
        /// A plain flood fill of the holes in the street mask leaks. This demo's blocks
        /// are not sealed - a driveway crossing the kerb, a plaza that runs into the
        /// pavement, a corner where two tiles leave a gap - and ONE leak welds two
        /// interiors together. Thirty blocks came out as five.
        ///
        /// Cutting along the street lines instead - the columns of cells that are
        /// mostly carriageway - assumes every street runs the width of the town. In
        /// this demo they do not: three north-south lines were found for the whole
        /// city, and the blocks between them were bigger than any block.
        ///
        /// So: fill, and where the fill came out BIGGER than a block, erode that patch
        /// by a cell and fill again. A leak is one cell wide and an erosion cuts it,
        /// while a block that was already the right size is never touched - which
        /// matters, because eroding everything loses every block only two cells deep.
        /// Three rounds, then whatever is still oversized is given up on.</summary>
        static List<Rect> Lots(bool[,] road, bool[,] street, bool[,] used,
                               int nx, int nz, int x0, int z0)
        {
            var inside = new bool[nx, nz];
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                    inside[i, j] = used[i, j] && !street[i, j];

            var kept = new List<Rect>();
            int tooSmall = 0, ragged = 0, givenUp = 0;
            Cut(inside, nx, nz, x0, z0, 0, kept, ref tooSmall, ref ragged, ref givenUp);

            Debug.Log($"[SyntyBlocks] cut {kept.Count} kept; dropped {tooSmall} too small, " +
                      $"{ragged} ragged, {givenUp} still welded after three erosions");
            return kept;
        }

        const int MaxErosions = 3;

        /// <summary>One pass of fill-and-judge over a mask. What is block-sized is
        /// kept; what is too big is handed back to itself one erosion thinner.</summary>
        static void Cut(bool[,] mask, int nx, int nz, int x0, int z0, int round,
                        List<Rect> kept, ref int tooSmall, ref int ragged, ref int givenUp)
        {
            var seen = new bool[nx, nz];
            var stack = new Stack<Vector2Int>();
            var cells = new List<Vector2Int>();

            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                {
                    if (seen[i, j] || !mask[i, j]) continue;
                    cells.Clear();
                    int minI = i, maxI = i, minJ = j, maxJ = j;
                    stack.Push(new Vector2Int(i, j));
                    seen[i, j] = true;
                    while (stack.Count > 0)
                    {
                        var c = stack.Pop();
                        cells.Add(c);
                        minI = Mathf.Min(minI, c.x); maxI = Mathf.Max(maxI, c.x);
                        minJ = Mathf.Min(minJ, c.y); maxJ = Mathf.Max(maxJ, c.y);
                        Push(stack, seen, mask, nx, nz, c.x + 1, c.y);
                        Push(stack, seen, mask, nx, nz, c.x - 1, c.y);
                        Push(stack, seen, mask, nx, nz, c.x, c.y + 1);
                        Push(stack, seen, mask, nx, nz, c.x, c.y - 1);
                    }

                    // measured at the size it will be BAKED at: an eroded patch is
                    // handed back the ring the erosion took, and judging it before
                    // that threw away every block the erosion had to touch
                    int w = maxI - minI + 1 + 2 * round, d = maxJ - minJ + 1 + 2 * round;
                    if (w * Cell < MinSide || d * Cell < MinSide) { tooSmall++; continue; }

                    if (w * Cell > MaxSide || d * Cell > MaxSide)
                    {
                        if (round >= MaxErosions) { givenUp++; continue; }
                        // this patch only: erode it and cut it again, leaving every
                        // other island in the town at the size the fill already found
                        var patch = new bool[nx, nz];
                        foreach (var c in cells) patch[c.x, c.y] = true;
                        var thinner = new bool[nx, nz];
                        foreach (var c in cells)
                            thinner[c.x, c.y] =
                                c.x > 0 && c.x < nx - 1 && c.y > 0 && c.y < nz - 1 &&
                                patch[c.x - 1, c.y] && patch[c.x + 1, c.y] &&
                                patch[c.x, c.y - 1] && patch[c.x, c.y + 1];
                        Cut(thinner, nx, nz, x0, z0, round + 1, kept, ref tooSmall, ref ragged, ref givenUp);
                        continue;
                    }

                    int span = (maxI - minI + 1) * (maxJ - minJ + 1);
                    if (cells.Count < MinFill * span) { ragged++; continue; }

                    // an eroded patch lost a cell off every side; give it back, so the
                    // bake reaches its own kerb again
                    float grow = round * Cell;
                    kept.Add(Rect.MinMaxRect((minI + x0) * Cell - grow, (minJ + z0) * Cell - grow,
                                             (maxI + x0 + 1) * Cell + grow, (maxJ + z0 + 1) * Cell + grow));
                }
        }

        static void Push(Stack<Vector2Int> stack, bool[,] seen, bool[,] mask,
                         int nx, int nz, int i, int j)
        {
            if (i < 0 || j < 0 || i >= nx || j >= nz) return;
            if (seen[i, j] || !mask[i, j]) return;
            seen[i, j] = true;
            stack.Push(new Vector2Int(i, j));
        }

        /// <summary>One block: everything standing inside the rectangle, re-hung round
        /// its middle so the city can drop it on a lot of that size. The pack's own
        /// road and pavement tiles are left behind - the city lays its own carriageway
        /// and its own 6.5 m pavement (theirs is 5 m), and a bake carrying them would
        /// put a second kerb inside the first.</summary>
        static bool Bake(Rect lot, List<Part> parts)
        {
            var centre = new Vector3(lot.center.x, 0f, lot.center.y);
            string name = $"synty_{lot.width:F0}x{lot.height:F0}_{(int)lot.center.x}_{(int)lot.center.y}"
                .Replace("-", "m");
            var root = new GameObject(name);

            int taken = 0;
            foreach (var p in parts)
            {
                if (p.Street) continue;
                var c = p.Box.center;
                if (c.x < lot.xMin || c.x > lot.xMax || c.z < lot.yMin || c.z > lot.yMax) continue;
                var src = PrefabUtility.GetCorrespondingObjectFromSource(p.Go);
                var copy = src != null
                    ? (GameObject)PrefabUtility.InstantiatePrefab(src, root.transform)
                    : Object.Instantiate(p.Go, root.transform);
                copy.name = p.Go.name;
                copy.transform.SetPositionAndRotation(p.Go.transform.position - centre,
                                                      p.Go.transform.rotation);
                copy.transform.localScale = p.Go.transform.localScale;
                taken++;
            }
            if (taken == 0) { Object.DestroyImmediate(root); return false; }

            var tag = root.AddComponent<LivingCity.Generation.BlockLotTag>();
            tag.lotWidth = lot.width;
            tag.lotDepth = lot.height;

            PrefabUtility.SaveAsPrefabAsset(root, $"{BlocksDir}/{name}.prefab");
            Object.DestroyImmediate(root);
            return true;
        }

        /// <summary>Every prefab instance in the demo, and the ground box they all
        /// stand on. Nested instances (a sign bolted to a building, a lamp head on its
        /// pole) come with their parent and are not listed twice.</summary>
        static List<GameObject> Pieces(Scene scene, out Bounds box)
        {
            var found = new List<GameObject>();
            var seen = new HashSet<GameObject>();
            bool any = false;
            box = new Bounds();

            foreach (var root in scene.GetRootGameObjects())
            {
                if (System.Array.IndexOf(SkipRoots, root.name) >= 0) continue;
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    var go = PrefabUtility.GetNearestPrefabInstanceRoot(r.gameObject);
                    if (go == null) go = r.gameObject;
                    // a nested instance inside one we already took comes with it
                    if (Taken(go, seen)) continue;
                    if (!seen.Add(go)) continue;
                    if (Named(go, SkipNames)) continue;

                    var rs = go.GetComponentsInChildren<Renderer>(true);
                    var mine = rs[0].bounds;
                    for (int i = 1; i < rs.Length; i++) mine.Encapsulate(rs[i].bounds);
                    if (mine.size.x > MaxPiece || mine.size.z > MaxPiece) continue;

                    found.Add(go);
                    if (!any) { box = mine; any = true; } else box.Encapsulate(mine);
                }
            }
            return found;
        }

        static bool Taken(GameObject go, HashSet<GameObject> seen)
        {
            for (var t = go.transform.parent; t != null; t = t.parent)
                if (seen.Contains(t.gameObject)) return true;
            return false;
        }
    }
}
