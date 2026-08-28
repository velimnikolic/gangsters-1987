using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// The hand route out of the POLYGON City demo scene, for the city core v2: a
    /// rectangle on their floor, a block dragged onto it, Ctrl+S, and the block is a
    /// prefab in <see cref="OutDir"/>.
    ///
    /// Why a tray at all, when <see cref="SyntyDemoBlockRip"/> already cuts the whole
    /// demo into blocks by itself: the automatic cut has to GUESS where a block ends.
    /// It reads the kerb, floods the holes, erodes what leaks and gives up on what is
    /// still welded - and what comes out is every block the demo happens to have,
    /// including the ones nobody wants. A core is chosen, not swept. The tray is where
    /// the choosing happens: an eye picks what is worth keeping, the machine does the
    /// filing.
    ///
    /// WHAT IT TAKES. Everything, exactly as it stands. Nothing is classified, nothing
    /// is stripped, nothing is left behind for being a car or a road tile or a
    /// scaffold: a piece is baked if its middle is inside the rectangle, or if it hangs
    /// under the tray in the hierarchy. Which of the two put it there does not matter,
    /// so dragging across the scene and dragging in the hierarchy both work, and a
    /// rectangle dropped over a block WHERE IT STANDS bakes it without anything moving
    /// at all. Anything parented to the tray is taken WHOLE - drag a group on and the
    /// group arrives as a group.
    ///
    /// THE PAINT. The demo does not just place the pack's prefabs, it repaints them as
    /// it places: 467 of its pieces wear a material their own source prefab has never
    /// heard of, and a further nine hundred carry some other override. So a copy is not
    /// a fresh instance of the source prefab - it is a fresh instance with the scene's
    /// own overrides replayed onto it, and a plain deep copy for the few pieces that
    /// have been structurally rebuilt. Instantiating the source and leaving it at that
    /// is how a block comes out of the bake in the wrong colours.
    ///
    /// Otherwise every piece keeps its link to its own source prefab, so a block on
    /// disk is a few dozen references and a few dozen transforms, not a few dozen
    /// copies of the Synty meshes.
    ///
    /// The demo scene is a VENDOR scene. Trays are saved into it, which is why they all
    /// hang under one root - delete <see cref="TraysRoot"/> and the scene is Synty's
    /// again, and git restores it even from that.
    /// </summary>
    [InitializeOnLoad]
    public static class CoreBlockTray
    {
        /// <summary>The one root every tray hangs under, so the whole apparatus is one
        /// line in the hierarchy and one delete to be rid of.</summary>
        internal const string TraysRoot = "CORE TRAYS";

        /// <summary>Deliberately NOT Assets/CityKit/Blocks: that folder is the automatic
        /// rip's output and it overwrites itself on every run. A hand-picked block must
        /// never be collateral of a sweep. A folder of its own under Prefabs, so the
        /// core can be looked through as a folder without the rest of the kit in it.</summary>
        internal const string OutDir = "Assets/Prefabs/CoreBlocks";

        /// <summary>The root the baked blocks are stood back up under for looking at.
        /// Kept out of every sweep: a block standing in the review row must never be
        /// claimed by a rectangle and baked into another block.</summary>
        internal const string ReviewRoot = "CORE BLOCKS (review)";

        /// <summary>The painted answer to "show me where you think the blocks are". Kept out
        /// of every sweep, like the review row - it is a drawing OF the scene, never part
        /// of it.</summary>
        internal const string MapRoot = "CORE BLOCK MAP";

        /// <summary>The library the new blocks are cut from: every baked block standing
        /// stripped of its pavement, to be dragged onto a tray and recombined.
        ///
        /// Kept out of every sweep as firmly as the review row, and for a sharper reason: a
        /// harvest of the scene would take the whole library and file it back over the very
        /// prefabs it was made from. What takes a block OUT of the library is dragging it
        /// onto a tray IN THE HIERARCHY - it is under the tray then, not under this, and
        /// the sweep reaches through the group to the pieces.</summary>
        internal const string BareRoot = "CORE BLOCKS (bare)";

        const string PadName = "pad";
        const string LabelName = "label";

        /// <summary>What the generated pavement hangs under, on the tray. A group rather
        /// than forty loose children, so re-paving is one delete and a hand-laid tile
        /// dragged on beside it is never mistaken for the machine's work - and the sweep
        /// reaches THROUGH it, so each tile still bakes as its own linked instance.</summary>
        const string PavingName = "paving";

        /// <summary>The pack's module. Every road and pavement tile in the demo is this
        /// square, so a pivot snapped to it keeps a baked block on the grid it was laid
        /// out on.</summary>
        const float Cell = 5f;

        /// <summary>Walking room between trays, and how far west of everything the next
        /// one is laid.</summary>
        const float Gap = 30f;

        /// <summary>How far outside the rectangle a piece may stand and still count as
        /// standing on it - half a cell, so a kerb tile flush with the edge is in.</summary>
        const float Slack = 2.5f;

        /// <summary>Above the ground rather than below it: a tray laid over the city has
        /// to read THROUGH the road it covers, and a pad sunk under the tarmac like the
        /// catalog's lot pads would be invisible exactly where it matters most.</summary>
        const float PadHeight = 0.05f;

        /// <summary>How many blocks stand in one row of the review yard before it wraps.</summary>
        const int RowOf = 6;

        const float DefaultWidth = 90f;
        const float DefaultDepth = 60f;

        const string BakeOnSaveMenu = "Tools/City/Core/Bake Trays On Save";
        const string BakeOnSaveKey = "gangsters1987.CoreBlockTray.bakeOnSave";

        const string ClearAfterMenu = "Tools/City/Core/Clear Tray After Bake";
        const string ClearAfterKey = "gangsters1987.CoreBlockTray.clearAfterBake";

        static readonly Color Paint = new Color(0.28f, 0.62f, 1f, 0.28f);

        static CoreBlockTray()
        {
            EditorSceneManager.sceneSaved -= OnSceneSaved;
            EditorSceneManager.sceneSaved += OnSceneSaved;
        }

        // ------------------------------------------------------------------ the trays

        [MenuItem("Tools/City/Core/Add Block Tray", priority = 1)]
        public static void AddTray()
        {
            var scene = SceneManager.GetActiveScene();
            var centre = new Vector3(Snap(WestEdge(scene) - Gap - DefaultWidth * 0.5f), 0f, 0f);
            var tray = NewTray(scene, centre, DefaultWidth, DefaultDepth);

            Undo.RegisterCreatedObjectUndo(tray.gameObject, "Add Block Tray");
            Selection.activeGameObject = tray.gameObject;
            EditorGUIUtility.PingObject(tray.gameObject);
            var view = SceneView.lastActiveSceneView;
            if (view)
                view.Frame(new Bounds(centre, new Vector3(DefaultWidth + Gap, 20f,
                                                          DefaultDepth + Gap)), false);

            Debug.Log($"[CoreTray] \"{tray.name}\" laid down on clear ground west of the demo. " +
                      $"Drag a block onto it - or move the rectangle over one where it stands - " +
                      $"and save the scene. It bakes to {OutDir}/{tray.name}.prefab; rename the " +
                      "tray to name the prefab. Scale the pad to change the rectangle.");
        }

        static Transform NewTray(Scene scene, Vector3 centre, float width, float depth)
        {
            var trays = TraysOf(scene);
            if (!trays) trays = new GameObject(TraysRoot).transform;
            return MakeTray(scene, NextName(trays), centre, width, depth);
        }

        /// <summary>A tray under a name of somebody else's choosing, for a block that
        /// already knows what it is called. <see cref="IndustrialBlockForge"/> composes a
        /// block in the scene and then hands it to the bake this way, so that a composed
        /// block and a harvested one go through the very same door.</summary>
        internal static Transform MakeTray(Scene scene, string name, Vector3 centre,
                                           float width, float depth)
        {
            var trays = TraysOf(scene);
            if (!trays) trays = new GameObject(TraysRoot).transform;

            var tray = new GameObject(name);
            tray.transform.SetParent(trays, false);
            tray.transform.position = centre;

            Buttons(tray.transform);
            Pad(centre, width, depth, tray.transform);
            Caption(LabelName,
                    $"{name}\n{width:F0} x {depth:F0} m\nsave the scene to bake",
                    new Vector3(centre.x, 4f, centre.z + depth * 0.5f + 4f),
                    tray.transform);

            EditorSceneManager.MarkSceneDirty(scene);
            return tray.transform;
        }

        static Transform TraysOf(Scene scene) =>
            scene.GetRootGameObjects().FirstOrDefault(go => go.name == TraysRoot)?.transform;

        /// <summary>"core-01", "core-02"... The tray's name IS the prefab's name, so this
        /// is a placeholder until the block is called what it is.</summary>
        static string NextName(Transform trays)
        {
            int highest = 0;
            foreach (Transform tray in trays)
            {
                var match = System.Text.RegularExpressions.Regex.Match(tray.name, @"^core-(\d+)$");
                if (match.Success && int.TryParse(match.Groups[1].Value, out var n))
                    highest = Mathf.Max(highest, n);
            }
            return $"core-{highest + 1:00}";
        }

        /// <summary>The rectangle itself. A Plane, not a Quad: the Plane lies in XZ facing
        /// up as built, while the Quad stands in XY and has to be turned - turn it the
        /// wrong way and the pad faces the ground, invisible from everywhere anyone looks.
        /// Its mesh is 10 x 10 units, hence the ten. No collider, so it can never be
        /// clicked by accident while a block is being dragged across it.</summary>
        static void Pad(Vector3 centre, float width, float depth, Transform parent)
        {
            var pad = GameObject.CreatePrimitive(PrimitiveType.Plane);
            pad.name = PadName;
            var collider = pad.GetComponent<Collider>();
            if (collider) Object.DestroyImmediate(collider);

            pad.transform.SetParent(parent, false);
            pad.transform.position = new Vector3(centre.x, PadHeight, centre.z);
            pad.transform.localScale = new Vector3(width / 10f, 1f, depth / 10f);

            var renderer = pad.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = Glass();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        /// <summary>
        /// A caption over a rectangle, turned so it can be READ.
        ///
        /// A TextMesh is legible from BEHIND its own forward axis - the billboard trick for
        /// one is to point its forward away from the camera - so the catalog's captions,
        /// built facing +Z, come out mirrored to anyone standing north of them. Everything
        /// here is north of its caption, because everything here is reached by flying south
        /// out of the city, so every caption is turned to meet that.
        /// </summary>
        static void Caption(string name, string text, Vector3 position, Transform parent)
        {
            BlockLotPads.PadLabel(name, text, position, parent);
            var caption = parent.Find(name);
            if (caption) caption.rotation = Quaternion.Euler(35f, 180f, 0f);
        }

        /// <summary>The pad's paint: unlit so it reads the same whatever the demo's
        /// lighting is doing, and TRANSPARENT so a tray laid over the city tints the block
        /// instead of hiding it. URP's unlit shader ships opaque; these are the switches
        /// the material inspector throws when you pick Transparent, thrown from code.</summary>
        static Material Glass()
        {
            const string path = "Assets/Materials/CoreTrayPad.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
                if (shader == null)
                {
                    Debug.LogError("[CoreTray] no unlit shader found; the pad stays unpainted.");
                    return null;
                }
                mat = new Material(shader) { name = "CoreTrayPad" };
                AssetDatabase.CreateAsset(mat, path);
            }

            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);
            if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Paint);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", Paint);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>Far edge of everything the scene already holds, so a tray laid on clear
        /// ground never lands on the demo or on the tray laid before it.</summary>
        static float WestEdge(Scene scene)
        {
            float edge = 0f;
            bool any = false;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == ReviewRoot || root.name == MapRoot || root.name == BareRoot ||
                    root.name == CoreBuildingBlocks.StockRoot) continue;
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    // the painted skyline and the skydome are the horizon, not ground a
                    // tray has to keep clear of
                    if (r.bounds.size.x > 400f || r.bounds.size.z > 400f) continue;
                    if (!any) { edge = r.bounds.min.x; any = true; }
                    else edge = Mathf.Min(edge, r.bounds.min.x);
                }
            }
            return any ? edge : 0f;
        }

        // ----------------------------------------------------------------- the pavement

        /// <summary>
        /// Lays the pavement round whatever buildings are standing on the trays, in the
        /// demo's own manner - <see cref="RoadDemo.CorePavement"/> holds the reading of it
        /// and the reasons.
        ///
        /// Round the BUILDINGS, not over the rectangle. The tray is a place to stand things
        /// while they are chosen; the block is the ground the buildings actually take, plus
        /// the one tile of kerb the artists put round it. Paving the rectangle would hand
        /// every block a yard the size of whatever tray it happened to be dropped on.
        ///
        /// It runs again as often as it is asked: the old pavement is deleted first, so a
        /// building nudged five metres and a second run leaves no orphan tiles. Everything
        /// goes through Undo, deletions included.
        /// </summary>
        [MenuItem("Tools/City/Core/Pave The Trays", priority = 3)]
        public static void PaveTrays()
        {
            var scene = SceneManager.GetActiveScene();
            var trays = TraysOf(scene);
            var said = new List<string>();
            int paved = trays ? PaveAll(scene, trays, Sweep(scene, trays), force: true, out said) : 0;
            if (paved == 0)
            {
                EditorUtility.DisplayDialog("Pave The Trays",
                    "No tray in this scene is holding a building.\n\n" +
                    "Lay one down with Tools/City/Core/Add Block Tray and drag the buildings " +
                    "onto the rectangle. What is looked for is the pack's own buildings " +
                    "(SM_Bld_...) standing on the ground - the pavement is laid round what " +
                    "THEY cover, not over the rectangle.", "OK");
                return;
            }

            EditorUtility.DisplayDialog("Pave The Trays",
                $"{paved} tray(s) paved:\n\n" + string.Join("\n", said) +
                "\n\nCtrl+Z takes it back off. Save the scene to bake the blocks.", "OK");
        }

        /// <summary>
        /// Paves the trays and says what it laid on each.
        ///
        /// <paramref name="force"/> re-lays a tray that has been paved already. Off - which
        /// is how the bake calls it - a tray that already carries pavement is left exactly
        /// as it is, so a kerb moved by hand or a corner swapped for a plain one survives
        /// every save afterwards. The menu item above is the way to say "do it again".
        ///
        /// The sweep is handed in rather than taken: the bake needs one of its own straight
        /// afterwards anyway, and a scene of five thousand instances is not worth walking
        /// twice for nothing.
        /// </summary>
        static int PaveAll(Scene scene, Transform trays, List<Piece> pieces, bool force,
                           out List<string> said)
        {
            said = new List<string>();
            int paved = 0;

            foreach (Transform tray in trays)
            {
                Buttons(tray);
                if (!PaveTray(tray, pieces, force, out var line)) continue;
                paved++;
                said.Add($"{tray.name}: {line}");
            }

            if (paved > 0)
            {
                Undo.SetCurrentGroupName("Pave block trays");
                EditorSceneManager.MarkSceneDirty(scene);
            }
            return paved;
        }

        /// <summary>One tray paved. Returns false for a tray holding no buildings, and for
        /// one that is already paved unless <paramref name="force"/> says otherwise.</summary>
        static bool PaveTray(Transform tray, List<Piece> pieces, bool force, out string said)
        {
            said = null;
            if (!TryRect(tray, out var rect)) return false;

            var already = tray.Find(PavingName);
            if (!force && (already || Kerbed(tray, rect, pieces))) return false;

            // the buildings alone say where the block is. A lamp's box hangs out over the
            // road it lights and a parked car covers most of a cell, and either of them
            // read as ground would pull the kerb out into the street
            var walls = Walls(tray, rect, already, pieces);
            if (walls.Count == 0) return false;

            if (already) Undo.DestroyObjectImmediate(already.gameObject);

            var group = new GameObject(PavingName);
            Undo.RegisterCreatedObjectUndo(group, "Pave block tray");
            group.transform.SetParent(tray, false);

            // the seed is the tray's NAME: the same tray is furnished the same way every
            // time it is paved, and no two trays come out wearing the same lamps
            int seed = 17;
            foreach (char letter in tray.name) seed = seed * 31 + letter;

            var panel = tray.GetComponent<RoadDemo.CoreTray>();
            int band = panel ? Mathf.Clamp(panel.pavementTiles, 1, 4)
                             : RoadDemo.CoreBlockMetrics.PavementTiles;
            Ground(tray, rect, already, pieces, out var open, out var roofed, out var gates,
                   out var parks, out int parkYaw, out var fleet);
            var plan = RoadDemo.CorePavement.Around(walls, band, open, roofed, gates,
                                                    parks, parkYaw);
            int laid = RoadDemo.CorePavement.Lay(
                plan, (prefab, parent) => (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent),
                group.transform, out said, y: tray.position.y, seed: seed,
                under: !(panel && panel.ownGround));
            laid += Ramps(plan, group.transform, tray.position.y, tray.name);
            laid += Parked(plan, group.transform, fleet);

            if (laid != 0) return true;
            Undo.DestroyObjectImmediate(group);
            return false;
        }

        /// <summary>
        /// The cars standing in a block's own car park - one to every bay the pavement
        /// painted, all facing the way the lot was declared to face.
        ///
        /// NOSED UP TO THE BACK OF THE BAY, not stood in the middle of it. The pack's bay
        /// is five metres deep and the pack's own cars are 5.8 and 6.9 long, so a car
        /// centred in one hangs over BOTH lines and the lot reads as too small for the
        /// force parked in it (2026-08-26, the user: "parking je nekako premali, uvek viri
        /// auto s njega"). Nosed up, the front wheels are at the line they should be at and
        /// what hangs over is the tail, over the crossing the car drove in across - which is
        /// what a car park full of cars actually looks like. Nothing is scaled to fit: the
        /// pack drew the bay and the pack drew the car, and the answer is where the car is
        /// put, not how big it is.
        ///
        /// The fleet is dealt round in order rather than at random, so the block comes out
        /// the same way every time it is baked.
        /// </summary>
        static int Parked(RoadDemo.CorePavement.Plan plan, Transform parent, string[] fleet)
        {
            if (plan?.Stalls == null || fleet == null || fleet.Length == 0) return 0;

            var facing = Quaternion.Euler(0f, plan.ParkYaw, 0f);
            int stood = 0;

            foreach (var stall in plan.Stalls)
            {
                var name = fleet[stood % fleet.Length];
                var prefab = Find(name);
                if (prefab == null)
                {
                    Debug.LogWarning($"[CoreTray] no prefab called {name} - the car park is a " +
                                     "bay short.");
                    continue;
                }
                var car = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                car.transform.SetPositionAndRotation(stall, facing);
                Undo.RegisterCreatedObjectUndo(car, "Pave block tray");

                RoadDemo.CoreRoads.InBay(car, stall, plan.ParkYaw);
                stood++;
            }
            return stood;
        }

        /// <summary>One pack prefab by its plain name, the way the catalogs give them.</summary>
        static GameObject Find(string name)
        {
            foreach (var guid in AssetDatabase.FindAssets($"{name} t:Prefab"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(path) != name) continue;
                return AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }
            return null;
        }

        /// <summary>Tiles a palm may be planted on: the kerb of a block, whichever way it
        /// runs. The corner pieces are left out - a corner is where the bollards guard and
        /// where two runs meet, and a tree standing on one blocks both.</summary>
        static readonly string[] PalmTiles =
        {
            "SM_Env_Sidewalk_Straight", "SM_Env_Sidewalk_Gutter",
        };

        /// <summary>What already stands on a block, so a palm is not planted through it:
        /// everything the pack calls a prop, and anything green already growing.</summary>
        static readonly string[] Standing =
        {
            "SM_Prop_", "SM_Env_Tree", "SM_Env_Plant",
        };

        /// <summary>
        /// The palms the HARVESTED blocks never had.
        ///
        /// A block this class grew is furnished as it is laid (CorePavement.Furnish); a block
        /// copied out of the demo scene carries the artists' own furniture and no trees at
        /// all, so a city dealt from both came out half planted (2026-08-26, the user: "ovi
        /// stari blokovi nemaju palme, dodaj i na njih da bude uniformno"). This plants the
        /// second kind to the same rhythm as the first.
        ///
        /// Idempotent by inspection rather than by a marker: a block with a palm on it has
        /// been planted and is left alone. So it can be run again after a harvest without
        /// thickening what is already done, and run on a grown block it does nothing.
        /// </summary>
        [MenuItem("Tools/City/Core/Plant Palms On Baked Blocks", priority = 43)]
        public static void PlantPalmsFromMenu()
        {
            int blocks = PlantPalms(out var said);
            Debug.Log($"[CoreTray] palms planted on {blocks} block(s): " + string.Join("; ", said));
            if (blocks > 0) AssetDatabase.SaveAssets();
        }

        /// <summary>Plants every baked block that has a kerb and no tree. Returns how many
        /// were written; <paramref name="said"/> gets a line each.</summary>
        internal static int PlantPalms(out List<string> said)
        {
            said = new List<string>();
            if (!AssetDatabase.IsValidFolder(OutDir)) return 0;

            int written = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { OutDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string name = System.IO.Path.GetFileNameWithoutExtension(path);
                var block = PrefabUtility.LoadPrefabContents(path);
                if (block == null) continue;

                try
                {
                    var kerbs = new List<RoadDemo.CorePavement.Kerbstone>();
                    var standing = new List<Vector3>();
                    bool already = false;

                    foreach (Transform piece in block.transform)
                    {
                        if (Named(piece.gameObject, RoadDemo.CorePavement.PalmPiece))
                        { already = true; break; }
                        if (Named(piece.gameObject, Standing)) { standing.Add(piece.position); continue; }
                        if (!Named(piece.gameObject, PalmTiles)) continue;

                        // the pack pivots every tile on the +X/+Z corner of its own cell, so
                        // the middle is half a cell back along the TILE'S own axes - which is
                        // the whole of what the turn has to be read through
                        kerbs.Add(new RoadDemo.CorePavement.Kerbstone(
                            piece.TransformPoint(new Vector3(-HalfCell, 0f, -HalfCell)),
                            piece.eulerAngles.y));
                    }

                    if (already) { said.Add($"{name}: has trees already"); continue; }
                    if (kerbs.Count == 0) { said.Add($"{name}: no kerb to plant on"); continue; }

                    // the block's own name is the seed, so the same block is planted the same
                    // way every time and no two blocks are planted alike
                    int seed = 17;
                    foreach (char letter in name) seed = seed * 31 + letter;

                    int palms = RoadDemo.CorePavement.Plant(
                        kerbs, standing,
                        (prefab, parent) => (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent),
                        block.transform, seed);

                    if (palms == 0)
                    {
                        said.Add($"{name}: no room on {kerbs.Count} kerb tile(s)");
                        continue;
                    }

                    PrefabUtility.SaveAsPrefabAsset(block, path);
                    written++;
                    said.Add($"{name}: {palms} palm(s) on {kerbs.Count} kerb tile(s)");
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(block);
                }
            }
            return written;
        }

        /// <summary>Half a cell of the pack's module.</summary>
        const float HalfCell = RoadDemo.CorePavement.Cell * 0.5f;

        /// <summary>Where a block's drawn ramps are kept. A mesh built in the scene and
        /// saved nowhere does not survive being written into a prefab - the block would come
        /// out with a MeshFilter pointing at nothing - so each one goes on disk beside the
        /// blocks themselves and the block carries the asset.</summary>
        const string MeshDir = OutDir + "/Meshes";

        /// <summary>The ramps the plan asked for, drawn and stood. Two surfaces apiece and a
        /// material each - see <see cref="RoadDemo.CoreRamp"/>.</summary>
        static int Ramps(RoadDemo.CorePavement.Plan plan, Transform parent, float y, string name)
        {
            if (plan?.Slopes == null || plan.Slopes.Count == 0) return 0;

            EnsureFolder(MeshDir);
            string file = string.Join("_", name.Split(System.IO.Path.GetInvalidFileNameChars()));
            int stood = 0;

            for (int at = 0; at < plan.Slopes.Count; at++)
            {
                var slope = plan.Slopes[at];
                RoadDemo.CoreRamp.Build(slope, out var deck, out var walls);
                string tag = plan.Slopes.Count > 1 ? $" {at + 1}" : "";
                if (Stand(deck, $"ramp{tag} deck", $"{file}-ramp{at + 1}-deck", parent, slope, y)) stood++;
                if (Stand(walls, $"ramp{tag} walls", $"{file}-ramp{at + 1}-walls", parent, slope, y)) stood++;
            }
            return stood;
        }

        /// <summary>One drawn surface saved and stood, turned so that its own +Z climbs the
        /// way the slope does.</summary>
        static bool Stand(RoadDemo.CoreRamp.Drawn drawn, string name, string file,
                          Transform parent, RoadDemo.CorePavement.Slope slope, float y)
        {
            if (!drawn.Any) return false;

            string path = $"{MeshDir}/{file}.asset";
            var already = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (already == null) AssetDatabase.CreateAsset(drawn.Mesh, path);
            else
            {
                // the same block re-paved: the asset keeps its guid so every block already
                // pointing at it follows the change instead of being left on a ghost
                already.Clear();
                already.SetVertices(drawn.Mesh.vertices);
                already.SetUVs(0, drawn.Mesh.uv);
                already.SetTriangles(drawn.Mesh.triangles, 0);
                already.RecalculateNormals();
                already.RecalculateBounds();
                EditorUtility.SetDirty(already);
                Object.DestroyImmediate(drawn.Mesh);
            }
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);

            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            Undo.RegisterCreatedObjectUndo(go, "Pave block tray");
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(slope.Middle(y), slope.Facing);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sharedMaterial = drawn.Wearing;
            return true;
        }

        /// <summary>What the kit's own building bakes are called - the catalog's buildings
        /// as this project files them (Assets/CityKit/Buildings), and what
        /// <see cref="RoadDemo.CoreLayout.Measure"/> reads as a building too.</summary>
        const string KitBuilding = "building-";

        /// <summary>The buildings standing on one tray - what its block is measured from.
        /// The pack's own and the kit's, only the ones with their feet on the ground; and
        /// of the pack's, only the ones tall enough to be a wall, because the fire escapes
        /// and roof housings it also calls buildings are up in the air and a block is not
        /// as wide as its roofline. A kit bake is a whole building in one piece by
        /// construction, so nothing of it is up in the air and the height is no test - a
        /// skatepark is barely knee high and is still the ground a block is made of.</summary>
        static List<Bounds> Walls(Transform tray, Rect rect, Transform paving, List<Piece> pieces)
        {
            var walls = new List<Bounds>();
            foreach (var piece in pieces)
            {
                // an earlier tray's old pavement was destroyed a moment ago and the sweep
                // still remembers it
                if (!piece.Go) continue;
                if (piece.Owner != tray && (piece.Owner != null || !Holds(rect, piece.Box.center)))
                    continue;
                if (paving && piece.Go.transform.parent == paving) continue;
                bool kit = Named(piece.Go, KitBuilding);
                if (!kit && !Named(piece.Go, "SM_Bld_")) continue;
                if (piece.Box.min.y > 1f || (!kit && piece.Box.max.y < 2f)) continue;
                walls.Add(piece.Box);
            }
            return walls;
        }

        /// <summary>
        /// Does the block on this tray already carry a kerb of its own?
        ///
        /// A block dragged straight off the demo brings one, and so does a block composed
        /// with its own - the industrial forge lays its ring before it hands the block to
        /// the tray. Laying the generated pavement over either is two kerbs in the same
        /// place. The bare library exists to take the first one off
        /// (<see cref="ShowBare"/>), and a block that still has it is not asking for
        /// another; <c>force</c> is how somebody says otherwise.
        /// </summary>
        static bool Kerbed(Transform tray, Rect rect, List<Piece> pieces)
        {
            foreach (var piece in pieces)
            {
                if (!piece.Go) continue;
                if (piece.Owner != tray && (piece.Owner != null || !Holds(rect, piece.Box.center)))
                    continue;
                if (Named(piece.Go, KerbTiles)) return true;
            }
            return false;
        }

        /// <summary>
        /// What on this tray leaves the ground OPEN: a sunken entrance stair, a basement
        /// door, a subway mouth, a vehicle yard cut into the block. Those cells are floored
        /// over at nought by nothing, which is the city's own rule for a unit that goes
        /// under - and the reason the pack's own block-07, all sunken stoops, carries no
        /// floor inside its kerb.
        ///
        /// Not only the buildings: whatever it is that goes down there, the ground is open
        /// over it. But going down is not enough on its own - see
        /// <see cref="Scan"/>, which is where the question is actually settled.
        /// </summary>
        static void Ground(Transform tray, Rect rect, Transform paving, List<Piece> pieces,
                           out List<Bounds> open, out List<Bounds> roofed, out List<Bounds> gates,
                           out List<Bounds> parks, out int parkYaw, out string[] fleet)
        {
            open = new List<Bounds>();
            roofed = new List<Bounds>();
            gates = new List<Bounds>();
            parks = new List<Bounds>();
            parkYaw = 0;
            fleet = null;

            foreach (var piece in pieces)
            {
                if (!piece.Go) continue;
                if (piece.Owner != tray && (piece.Owner != null || !Holds(rect, piece.Box.center)))
                    continue;
                if (paving && piece.Go.transform.parent == paving) continue;

                // a walled yard's way in cannot be read off the mesh - see GateOf
                if (CoreBuildingBlocks.GateOf(piece.Go, out var gate)) gates.Add(gate);

                // nor can the ground a building's own cars stand on - see ParkOf
                if (CoreBuildingBlocks.ParkOf(piece.Go, out var park, out int yaw, out var cars))
                {
                    parks.Add(park);
                    parkYaw = yaw;
                    fleet = cars;
                }

                bool dips = piece.Box.min.y <= RoadDemo.CorePavement.Underground;

                // only a BUILDING roofs ground. A lamp is three times head room tall and a
                // palm taller still, and counting either would wall a yard in with the very
                // furniture the pavement stood there
                bool stands = piece.Box.max.y >= HeadRoom &&
                              (Named(piece.Go, KitBuilding) || Named(piece.Go, "SM_Bld_"));
                if (!dips && !stands) continue;

                if (Scan(piece.Go, dips ? open : null, stands ? roofed : null)) continue;

                // nothing legible to read: the box stands in for whichever mask asked, which
                // errs the safe way twice over - open ground stays open, roofed ground stays
                // shut, and a way out is refused rather than driven through a wall
                if (dips) open.Add(piece.Box);
                if (stands) roofed.Add(piece.Box);
            }
        }

        /// <summary>How much of a thing has to stand over a cell before the ground under it
        /// is understood to be COVERED rather than open.
        ///
        /// Read off the police station, which is what forced the question: every cell its
        /// walls stand on tops out at 6 m or more, and every cell of its sunken vehicle yard
        /// at 2 m or less - the parapet round the drop. Three metres sits in the middle of
        /// that gap, above any railing and below any storey.</summary>
        const float HeadRoom = 3f;

        /// <summary>
        /// What one piece leaves OPEN and what it ROOFS, cell by cell off its mesh rather
        /// than off its bounding box.
        ///
        /// A cell is OPEN where the piece lays a FLOOR below ground in it - not where its
        /// lowest vertex happens to be low, which was the old reading and was wrong at both
        /// ends. It called a whole police station one 60 x 40 m hole because its box reaches
        /// -4.25; and at the other end it called a corner of the same station solid ground
        /// on the strength of three square metres of kerb, and left the city showing through
        /// beside it. So the FLOOR is what is measured - up-facing horizontal surface, by
        /// area, at the height it lies at - and a cell counts as dropping through when
        /// <see cref="RoadDemo.CorePavement.Covers"/> of it is floored below
        /// <see cref="RoadDemo.CorePavement.Underground"/>.
        ///
        /// And a roof over it changes nothing (2026-08-26, the user, of the police station:
        /// "ne sme da ima pavement ispod sebe jer ide u - po vertikali"). A slab at nought
        /// over a basement is a lid inside the building, whether the building is over it or
        /// not; the pack's own rule is that a unit which goes below ground is given no floor
        /// at all, and the roof only ever mattered to the way OUT - which is why Roofed is
        /// still measured, and separately.
        ///
        /// Cells are snapped to the world 5 m beat, which is the beat
        /// <see cref="RoadDemo.CorePavement.Around"/> lays its raster on, so a box handed
        /// back here covers exactly one cell of it. An open cell's box carries the floor it
        /// found as its foot: that is how far a way out has to climb.
        ///
        /// Returns false when the piece cannot be read whole - no MeshFilter at all, or a
        /// mesh whose data is gone - and nothing is written to either list.
        ///
        /// Either list may be null: a prop that dips is asked only what it leaves open, and
        /// a building with no basement only what it stands on.
        /// </summary>
        static bool Scan(GameObject go, List<Bounds> open, List<Bounds> roofed)
        {
            const float cell = RoadDemo.CorePavement.Cell;
            var floors = new Dictionary<Vector2Int, Vector2>();   // cell -> (floor area, lowest floor)
            var tops = new Dictionary<Vector2Int, float>();       // cell -> highest point
            bool read = false;

            foreach (var filter in go.GetComponentsInChildren<MeshFilter>())
            {
                var mesh = filter.sharedMesh;
                if (mesh == null) continue;

                // isReadable is NOT the test here. The merge releases the CPU copy of every
                // mesh it folds in (RoadDemo's merge, on entering play), which clears that
                // flag on the asset for the rest of the editor session - and a bake taken
                // afterwards would quietly fall back to bounding boxes. In the editor the
                // data is on disk either way, so it is asked for and the answer is the test
                var points = mesh.vertices;
                var corners = mesh.triangles;
                if (points.Length == 0 || corners.Length == 0) return false;
                read = true;

                var toWorld = filter.transform.localToWorldMatrix;
                for (int t = 0; t + 2 < corners.Length; t += 3)
                {
                    var a = toWorld.MultiplyPoint3x4(points[corners[t]]);
                    var b = toWorld.MultiplyPoint3x4(points[corners[t + 1]]);
                    var c = toWorld.MultiplyPoint3x4(points[corners[t + 2]]);
                    var key = new Vector2Int(Mathf.FloorToInt((a.x + b.x + c.x) / (3f * cell)),
                                             Mathf.FloorToInt((a.z + b.z + c.z) / (3f * cell)));

                    float high = Mathf.Max(a.y, Mathf.Max(b.y, c.y));
                    if (!tops.TryGetValue(key, out float had) || high > had) tops[key] = high;

                    // a floor: level, facing up, and low enough to be under the pavement
                    var normal = Vector3.Cross(b - a, c - a);
                    float twice = normal.magnitude;
                    if (twice < Sliver || normal.y < Level * twice) continue;
                    float y = (a.y + b.y + c.y) / 3f;
                    if (y > RoadDemo.CorePavement.Underground) continue;

                    // the area it covers on the GROUND, not the area of the triangle: a
                    // ramp lying at an angle floors less than its own size
                    float covers = normal.y * 0.5f;
                    if (floors.TryGetValue(key, out var floor))
                        floors[key] = new Vector2(floor.x + covers, Mathf.Min(floor.y, y));
                    else
                        floors[key] = new Vector2(covers, y);
                }
            }
            if (!read) return false;

            // the order cells come out of the table in does not matter: each one is claimed
            // on its own and claiming is idempotent
            float enough = RoadDemo.CorePavement.Covers * cell * cell;
            foreach (var pair in tops)
                if (pair.Value >= HeadRoom)
                    roofed?.Add(Square(pair.Key, RoadDemo.CorePavement.Underground, pair.Value));

            foreach (var pair in floors)
            {
                if (pair.Value.x < enough) continue;
                open?.Add(Square(pair.Key, pair.Value.y, RoadDemo.CorePavement.Underground));
            }
            return true;
        }

        /// <summary>How square a triangle has to lie before it counts as floor rather than
        /// wall. A car park ramp a driver would call steep is one in five, and 0.95 lets
        /// everything gentler than about eighteen degrees count as ground.</summary>
        const float Level = 0.95f;

        /// <summary>A triangle smaller than this has no area worth adding up.</summary>
        const float Sliver = 1e-6f;

        /// <summary>One cell of the world raster, as a box from one height to another.</summary>
        static Bounds Square(Vector2Int at, float low, float high)
        {
            const float cell = RoadDemo.CorePavement.Cell;
            if (high < low) (low, high) = (high, low);
            return new Bounds(
                new Vector3((at.x + 0.5f) * cell, (low + high) * 0.5f, (at.y + 0.5f) * cell),
                new Vector3(cell, Mathf.Max(0.1f, high - low), cell));
        }

        /// <summary>
        /// Gives a tray its buttons if it has none - an old tray, laid before there were
        /// any. Adding a component dirties the scene, so this only ever runs where the
        /// scene is being written to anyway.
        ///
        /// On the PAD as well as on the tray, because clicking the blue rectangle in the
        /// scene selects the pad, and clicking the blue rectangle is what anybody does.
        /// </summary>
        static void Buttons(Transform tray)
        {
            if (!tray.GetComponent<RoadDemo.CoreTray>()) tray.gameObject.AddComponent<RoadDemo.CoreTray>();
            var pad = tray.Find(PadName);
            if (pad && !pad.GetComponent<RoadDemo.CoreTray>()) pad.gameObject.AddComponent<RoadDemo.CoreTray>();
        }

        /// <summary>The tray something belongs to: itself if it is one, its parent if what
        /// was clicked is the pad. A tray is the thing with a pad under it.</summary>
        internal static Transform TrayOf(Transform thing)
        {
            if (!thing) return null;
            if (thing.Find(PadName)) return thing;
            if (thing.parent && thing.parent.Find(PadName)) return thing.parent;
            return null;
        }

        // ------------------------------------------------------ what the buttons ask for

        /// <summary>Paves one tray, sweeping the scene for it. The Pave button.</summary>
        internal static bool PaveOne(Transform tray, out string said)
        {
            said = "this tray is not in an open scene.";
            var scene = tray.gameObject.scene;
            var trays = TraysOf(scene);
            if (!trays) return false;

            if (!PaveTray(tray, Sweep(scene, trays), force: true, out said))
            {
                said = "nothing to pave: no building of the pack's stands on this tray.";
                return false;
            }
            Undo.SetCurrentGroupName("Pave block tray");
            EditorSceneManager.MarkSceneDirty(scene);
            return true;
        }

        /// <summary>Takes everything off one tray, without baking any of it. Through Undo,
        /// like every other deletion here.</summary>
        internal static int EmptyOne(Transform tray)
        {
            var scene = tray.gameObject.scene;
            var trays = TraysOf(scene);
            if (!trays || !TryRect(tray, out var rect)) return 0;

            var mine = new List<GameObject>();
            foreach (var piece in Sweep(scene, trays))
                if (piece.Go && (piece.Owner == tray ||
                                 (piece.Owner == null && Holds(rect, piece.Box.center))))
                    mine.Add(piece.Go);
            return Remove(scene, mine);
        }

        /// <summary>What one tray is holding, for the buttons to say so. Sweeps the scene,
        /// so it is read when the selection or the hierarchy changes and not every
        /// repaint.</summary>
        internal static void Holding(Transform tray, out int pieces, out int buildings,
                                     out bool paved, out Vector2 size)
        {
            pieces = buildings = 0;
            size = Vector2.zero;
            var paving = tray.Find(PavingName);
            paved = paving;

            var scene = tray.gameObject.scene;
            var trays = TraysOf(scene);
            if (!trays || !TryRect(tray, out var rect)) return;

            var mine = new List<Piece>();
            foreach (var piece in Sweep(scene, trays))
                if (piece.Go && (piece.Owner == tray ||
                                 (piece.Owner == null && Holds(rect, piece.Box.center))))
                    mine.Add(piece);

            pieces = mine.Count;
            buildings = Walls(tray, rect, paving, mine).Count;
            if (mine.Count == 0) return;
            var box = Ground(mine);
            size = new Vector2(box.size.x, box.size.z);
        }

        /// <summary>Where a tray's block would be written.</summary>
        internal static string PrefabPath(Transform tray) => $"{OutDir}/{tray.name}.prefab";

        // --------------------------------------------------------------- the review row

        /// <summary>
        /// Stands every block that has been baked back up in the scene, in a row south of
        /// the city, so a block can be LOOKED at instead of taken on trust from a log
        /// line. They are instances of the prefabs themselves, so what stands in the row
        /// is exactly what is on disk - a piece missing here is a piece missing from the
        /// block.
        ///
        /// Safe to run again after every bake: the old row is destroyed FIRST, which is
        /// also what stops the row marching further south each time it measures the scene
        /// to find out where south is.
        /// </summary>
        [MenuItem("Tools/City/Core/Show Baked Blocks", priority = 40)]
        public static void ShowBaked()
        {
            var scene = SceneManager.GetActiveScene();
            int stood = Review(scene, out var row);
            if (stood == 0)
            {
                EditorUtility.DisplayDialog("Show Baked Blocks",
                    $"Nothing has been baked into {OutDir} yet.\n\n" +
                    "Drag a block onto a tray and save the scene first.", "OK");
                return;
            }

            var view = SceneView.lastActiveSceneView;
            if (view) view.Frame(row, false);
            Debug.Log($"[CoreTray] {stood} baked block(s) standing under \"{ReviewRoot}\", south " +
                      $"of the city. They are instances of the prefabs in {OutDir} - run this " +
                      "again after a bake to refresh the row, or Hide Baked Blocks to clear it.");
        }

        [MenuItem("Tools/City/Core/Hide Baked Blocks", priority = 41)]
        public static void HideBaked()
        {
            var scene = SceneManager.GetActiveScene();
            int cleared = 0;
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == ReviewRoot || root.name == BareRoot)
                { Object.DestroyImmediate(root); cleared++; }

            if (cleared > 0) EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log(cleared > 0
                ? $"[CoreTray] the review row and the bare library are gone; the prefabs in " +
                  $"{OutDir} are untouched."
                : "[CoreTray] there was no review row to clear.");
        }

        // ------------------------------------------------------------- the bare library

        /// <summary>The tiles a block's pavement is made of. All of them come off - the
        /// pavement is what the new block will be given back, cut to ITS shape.</summary>
        const string Paving = "SM_Env_Sidewalk";

        /// <summary>The tiles that make the pavement's EDGE. What stands on one of these
        /// is street furniture; what stands further in is the block's own.</summary>
        static readonly string[] KerbTiles =
        {
            "SM_Env_Sidewalk_Straight", "SM_Env_Sidewalk_Corner", "SM_Env_Sidewalk_Dip",
            "SM_Env_Sidewalk_Gutter", "SM_Env_Sidewalk_Merger",
        };

        /// <summary>Exactly what <see cref="RoadDemo.CorePavement"/> stands back on a
        /// pavement, and nothing else. Anything here left on the block would come back
        /// DOUBLED the moment the new block is paved - two lamp posts in one place. The
        /// lamp's whole family is named, not just its mast: the arm, the lights and the
        /// crossing box are instances of their own, mounted on it, and a mast taken away
        /// from under them leaves them hanging in the air.
        ///
        /// Everything else stays. The give-way sign, the parking meter, the litter, the
        /// grass, the fences, the trees, and every aircon unit and fire escape on the
        /// walls: none of it is laid by the generator, so none of it can be doubled by it -
        /// and a block that arrives stripped of its character is not worth recombining.</summary>
        static readonly string[] StreetFurniture =
        {
            "SM_Prop_LightPole_", "SM_Prop_SidewalkPoles_", "SM_Prop_Trashbin_",
            "SM_Prop_Hydrant_", "SM_Prop_Mailbox_", "SM_Prop_ParkBench_", "SM_Prop_Newspaper_",
        };

        /// <summary>
        /// Stands every baked block in a row WITHOUT its pavement - the library the new
        /// blocks are cut from.
        ///
        /// A block off the tray is a lump of city: buildings, their dressing, the ground
        /// they stand on and the kerb round it. Recombining two of those means the two
        /// kerbs meet in the middle of the new block, which is why the pavement comes off
        /// here and is laid back on afterwards, cut to whatever shape the buildings end up
        /// making (Tools/City/Core/Pave The Trays).
        ///
        /// Each block is unpacked to its outermost root only, so the parts inside stay
        /// instances of the pack's own prefabs and a rebake keeps every link.
        ///
        /// Nothing is written to disk and nothing on disk is touched: this is a row of
        /// instances, and Hide Baked Blocks clears it.
        /// </summary>
        [MenuItem("Tools/City/Core/Show Blocks Without Pavement", priority = 42)]
        public static void ShowBare()
        {
            var scene = SceneManager.GetActiveScene();
            int stood = BareLibrary(scene, out var row, out int stripped);
            if (stood == 0)
            {
                EditorUtility.DisplayDialog("Show Blocks Without Pavement",
                    $"Nothing has been baked into {OutDir} yet.\n\n" +
                    "Drag a block onto a tray and save the scene first.", "OK");
                return;
            }

            var view = SceneView.lastActiveSceneView;
            if (view) view.Frame(row, false);
            Debug.Log($"[CoreTray] {stood} block(s) standing bare under \"{BareRoot}\", " +
                      $"{stripped} piece(s) of pavement and street furniture taken off them.\n" +
                      "[CoreTray] drag one onto a tray IN THE HIERARCHY to use it - the row " +
                      "itself is kept out of every sweep, so nothing standing in it can be " +
                      "baked or harvested by accident. Combine as many as you like on one " +
                      "tray; the pavement is laid round whatever they add up to.");
        }

        static int BareLibrary(Scene scene, out Bounds row, out int stripped)
        {
            row = new Bounds();
            stripped = 0;

            // first, so the row does not measure itself and march further south every time
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == BareRoot) Object.DestroyImmediate(root);

            if (!AssetDatabase.IsValidFolder(OutDir)) return 0;
            var blocks = AssetDatabase.FindAssets("t:Prefab", new[] { OutDir })
                                      .Select(AssetDatabase.GUIDToAssetPath)
                                      .OrderBy(path => path, System.StringComparer.Ordinal)
                                      .Select(AssetDatabase.LoadAssetAtPath<GameObject>)
                                      .Where(block => block != null)
                                      .ToList();
            if (blocks.Count == 0) return 0;

            var sizes = blocks.Select(Measure).ToList();
            var rowRoot = new GameObject(BareRoot);

            // BESIDE THE TRAYS, not out behind the city. The library is a thing to drag
            // FROM, and dragging is easier when what you drag from and what you drag onto
            // are in the same view. North of them, because the trays themselves march
            // WESTWARD as they are added (AddTray) and a library laid out that way would
            // be standing where the next rectangle wants to go.
            //
            // Which is also why nothing in here may ever be swept: it is within arm's
            // reach of a rectangle by design.
            var trays = TraysOf(scene);
            float middle = 0f, z;
            if (trays && TryBox(trays.gameObject, out var stood)) { middle = stood.center.x; z = Snap(stood.max.z + Gap); }
            else z = Snap(SouthEdge(scene) - Gap * 2f);
            bool first = true;

            for (int i = 0; i < blocks.Count; i += RowOf)
            {
                int end = Mathf.Min(i + RowOf, blocks.Count);
                float span = -Gap, deepest = 0f;
                for (int k = i; k < end; k++)
                {
                    span += sizes[k].x + Gap;
                    deepest = Mathf.Max(deepest, sizes[k].y);
                }

                float rowZ = Snap(z + deepest * 0.5f);
                float x = Snap(middle - span * 0.5f);
                for (int k = i; k < end; k++)
                {
                    var at = new Vector3(Snap(x + sizes[k].x * 0.5f), 0f, rowZ);
                    var copy = (GameObject)PrefabUtility.InstantiatePrefab(blocks[k], rowRoot.transform);
                    copy.transform.position = at;
                    PrefabUtility.UnpackPrefabInstance(copy, PrefabUnpackMode.OutermostRoot,
                                                       InteractionMode.AutomatedAction);
                    stripped += Strip(copy);

                    float top = TryBox(copy, out var stoodBox) ? stoodBox.max.y : 0f;
                    Caption($"{blocks[k].name} label",
                            $"{blocks[k].name}\nno pavement",
                            new Vector3(at.x, Mathf.Max(6f, top + 4f), at.z + sizes[k].y * 0.5f + 4f),
                            rowRoot.transform);

                    var mine = new Bounds(at, new Vector3(sizes[k].x, 20f, sizes[k].y));
                    if (first) { row = mine; first = false; } else row.Encapsulate(mine);
                    x += sizes[k].x + Gap;
                }
                z = rowZ + deepest * 0.5f + Gap;
            }

            EditorSceneManager.MarkSceneDirty(scene);
            return blocks.Count;
        }

        /// <summary>Takes the pavement off one block, and off it only what stood ON the
        /// pavement's edge - see <see cref="StreetFurniture"/> for where that line is
        /// drawn. Read cell by cell against the block's own pivot, so where the row happens
        /// to stand makes no difference.</summary>
        static int Strip(GameObject block)
        {
            var kerbs = new HashSet<Vector2Int>();
            var doomed = new List<GameObject>();
            var origin = block.transform.position;

            Vector2Int At(Vector3 point) =>
                new Vector2Int(Mathf.FloorToInt((point.x - origin.x) / Cell),
                               Mathf.FloorToInt((point.z - origin.z) / Cell));

            foreach (Transform part in block.transform)
            {
                if (!Named(part.gameObject, Paving)) continue;
                if (Named(part.gameObject, KerbTiles) && TryBox(part.gameObject, out var box))
                    kerbs.Add(At(box.center));
                doomed.Add(part.gameObject);
            }
            foreach (Transform part in block.transform)
                if (Named(part.gameObject, StreetFurniture) && kerbs.Contains(At(part.position)))
                    doomed.Add(part.gameObject);

            foreach (var part in doomed) Object.DestroyImmediate(part);
            return doomed.Count;
        }

        static int Review(Scene scene, out Bounds row)
        {
            row = new Bounds();
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == ReviewRoot) Object.DestroyImmediate(root);

            if (!AssetDatabase.IsValidFolder(OutDir)) return 0;
            var blocks = AssetDatabase.FindAssets("t:Prefab", new[] { OutDir })
                                      .Select(AssetDatabase.GUIDToAssetPath)
                                      .OrderBy(path => path, System.StringComparer.Ordinal)
                                      .Select(AssetDatabase.LoadAssetAtPath<GameObject>)
                                      .Where(block => block != null)
                                      .ToList();
            if (blocks.Count == 0) return 0;

            var sizes = blocks.Select(Measure).ToList();
            var rowRoot = new GameObject(ReviewRoot);
            float z = Snap(SouthEdge(scene) - Gap);
            bool first = true;

            // wrapped into rows rather than one long line: thirty blocks in a row is two
            // and a half kilometres of flying to see the last one
            for (int i = 0; i < blocks.Count; i += RowOf)
            {
                int end = Mathf.Min(i + RowOf, blocks.Count);
                float span = -Gap, deepest = 0f;
                for (int k = i; k < end; k++)
                {
                    span += sizes[k].x + Gap;
                    deepest = Mathf.Max(deepest, sizes[k].y);
                }

                float rowZ = Snap(z - deepest * 0.5f);
                float x = Snap(-span * 0.5f);
                for (int k = i; k < end; k++)
                {
                    var at = new Vector3(Snap(x + sizes[k].x * 0.5f), 0f, rowZ);
                    var copy = (GameObject)PrefabUtility.InstantiatePrefab(blocks[k], rowRoot.transform);
                    copy.transform.position = at;
                    // clear of the roof, whatever this one is: a caption at a fixed height
                    // hangs inside the third storey of anything taller than a shop
                    float top = TryBox(copy, out var stood) ? stood.max.y : 0f;
                    Caption($"{blocks[k].name} label",
                            $"{blocks[k].name}\n{sizes[k].x:F0} x {sizes[k].y:F0} m",
                            new Vector3(at.x, Mathf.Max(6f, top + 4f), at.z + sizes[k].y * 0.5f + 4f),
                            rowRoot.transform);

                    var mine = new Bounds(at, new Vector3(sizes[k].x, 20f, sizes[k].y));
                    if (first) { row = mine; first = false; } else row.Encapsulate(mine);
                    x += sizes[k].x + Gap;
                }
                z = rowZ - deepest * 0.5f - Gap;
            }

            EditorSceneManager.MarkSceneDirty(scene);
            return blocks.Count;
        }

        /// <summary>A block's footprint: what the bake wrote down, or what it measures if
        /// the tag has been taken off.</summary>
        static Vector2 Measure(GameObject block)
        {
            var tag = block.GetComponent<LivingCity.Generation.BlockLotTag>();
            if (tag && tag.lotWidth > 0f && tag.lotDepth > 0f)
                return new Vector2(tag.lotWidth, tag.lotDepth);

            var renderers = block.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return new Vector2(Cell, Cell);
            var box = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) box.Encapsulate(renderers[i].bounds);
            return new Vector2(Up(box.size.x), Up(box.size.z));
        }

        /// <summary>Near edge of everything the scene holds, so the review row stands clear
        /// of the city and of the trays rather than through them.</summary>
        static float SouthEdge(Scene scene)
        {
            float edge = 0f;
            bool any = false;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == ReviewRoot || root.name == BareRoot ||
                    root.name == CoreBuildingBlocks.StockRoot) continue;
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (r.bounds.size.x > 400f || r.bounds.size.z > 400f) continue;
                    if (!any) { edge = r.bounds.min.z; any = true; }
                    else edge = Mathf.Min(edge, r.bounds.min.z);
                }
            }
            return any ? edge : 0f;
        }

        // ------------------------------------------------------------------- the bake

        [MenuItem("Tools/City/Core/Bake Trays Now", priority = 20)]
        public static void BakeNow()
        {
            var scene = SceneManager.GetActiveScene();
            int written = BakeAll(scene, force: true, out var said, out _, out _);

            // a block that has just been baked has to appear where the other blocks are, or
            // the only proof it exists is a line of text. The row is rebuilt from the folder,
            // so the new one takes its place in the order rather than being hung on the end -
            // and only for someone who has the row open, which is the same rule the save
            // follows (OnSceneSaved). Nobody who has never asked for the row gets one.
            if (written > 0 && scene.GetRootGameObjects().Any(go => go.name == ReviewRoot))
                Review(scene, out _);

            if (said.Count == 0)
            {
                EditorUtility.DisplayDialog("Bake Trays",
                    "No tray in this scene is holding anything.\n\n" +
                    "Lay one down with Tools/City/Core/Add Block Tray, drag a block onto the " +
                    "rectangle - or move the rectangle over one - and bake again.", "OK");
                return;
            }

            EditorUtility.DisplayDialog("Bake Trays",
                $"{written} block prefab(s) written to {OutDir}:\n\n" + string.Join("\n", said), "OK");
        }

        /// <summary>
        /// The same bake with nothing to click.
        ///
        /// <see cref="BakeNow"/> always ends in a dialog, which is right for a menu item and
        /// disastrous for anything else: called from a pipeline command it stops the editor's
        /// main thread dead, waiting for a hand that is not there, and every call after it
        /// times out until somebody walks over and presses OK. Anything that is not a menu
        /// item bakes through here and says what happened in its own way.
        /// </summary>
        internal static int BakeQuietly(Scene scene, out List<string> said) =>
            BakeAll(scene, force: true, out said, out _, out _);

        static bool BakeOnSave => EditorPrefs.GetBool(BakeOnSaveKey, true);

        /// <summary>Whether a block, once it is safely on disk, is taken back out of the
        /// scene - so the rectangle stands empty and ready for the next one instead of
        /// having to be cleared by hand every time. It DELETES scene objects, which is why
        /// it is a switch and why every deletion goes through Undo.</summary>
        static bool ClearAfter => EditorPrefs.GetBool(ClearAfterKey, true);

        [MenuItem(ClearAfterMenu, priority = 22)]
        static void ToggleClearAfter() => EditorPrefs.SetBool(ClearAfterKey, !ClearAfter);

        [MenuItem(ClearAfterMenu, validate = true)]
        static bool ToggleClearAfterState()
        {
            Menu.SetChecked(ClearAfterMenu, ClearAfter);
            return true;
        }

        [MenuItem(BakeOnSaveMenu, priority = 21)]
        static void ToggleBakeOnSave() => EditorPrefs.SetBool(BakeOnSaveKey, !BakeOnSave);

        [MenuItem(BakeOnSaveMenu, validate = true)]
        static bool ToggleBakeOnSaveState()
        {
            Menu.SetChecked(BakeOnSaveMenu, BakeOnSave);
            return true;
        }

        /// <summary>Guards the second save below from coming back round through here.</summary>
        static bool resaving;

        static void OnSceneSaved(Scene scene)
        {
            if (resaving || !BakeOnSave) return;
            if (!scene.IsValid() || !scene.isLoaded) return;
            if (!scene.GetRootGameObjects().Any(go => go.name == TraysRoot)) return;

            int written = BakeAll(scene, force: false, out var said, out int cleared, out int paved);
            if (written > 0)
                Debug.Log($"[CoreTray] {written} block(s) baked to {OutDir} on save:\n  " +
                          string.Join("\n  ", said));

            // the row is only kept current for someone who has asked to see it
            bool showing = written > 0 &&
                           scene.GetRootGameObjects().Any(go => go.name == ReviewRoot);
            if (showing) Review(scene, out _);
            if (cleared == 0 && paved == 0 && !showing) return;

            // both of those happen AFTER the scene has been written, so the file on disk
            // still holds the block that was just cleared out of it. Put that right once,
            // outside this callback, so one Ctrl+S leaves the prefab written, the rectangle
            // clear, and no dirty scene trailing behind it.
            var again = scene;
            EditorApplication.delayCall += () =>
            {
                if (!again.IsValid() || !again.isLoaded || !again.isDirty) return;
                resaving = true;
                try { EditorSceneManager.SaveScene(again); }
                finally { resaving = false; }
            };
        }

        struct Piece
        {
            public GameObject Go;
            public Bounds Box;

            /// <summary>The tray this piece is PARENTED under, or null for a piece standing
            /// loose in the scene - which is claimed by rectangle instead.</summary>
            public Transform Owner;
        }

        /// <summary>Every tray in the scene, written down. Returns how many prefabs were
        /// actually written; <paramref name="said"/> gets a line per tray that held
        /// something, whether it was written or found unchanged.</summary>
        static int BakeAll(Scene scene, bool force, out List<string> said, out int cleared,
                           out int paved)
        {
            said = new List<string>();
            cleared = 0;
            paved = 0;
            var trays = TraysOf(scene);
            if (!trays) return 0;

            var pieces = Sweep(scene, trays);

            // a tray holding bare buildings is paved first, so dragging a few of them on
            // and pressing Ctrl+S is the whole of the job. Never a SECOND time: once a tray
            // carries pavement it is the user's, and a save must not undo a kerb they moved
            paved = PaveAll(scene, trays, pieces, force: false, out var laid);
            if (paved > 0)
            {
                Debug.Log($"[CoreTray] paved {paved} tray(s) before baking:\n  " +
                          string.Join("\n  ", laid));
                pieces = Sweep(scene, trays);   // the tiles just laid are the block's too
            }

            // what has been written down, and can therefore be taken out of the scene
            var spent = new List<GameObject>();
            var taken = new HashSet<string>();
            int written = 0;

            foreach (Transform tray in trays)
            {
                if (!TryRect(tray, out var rect))
                {
                    Debug.LogWarning($"[CoreTray] \"{tray.name}\" has no child named \"{PadName}\" - " +
                                     "it is not a tray and was skipped. Delete it, or lay a real one " +
                                     "down with Tools/City/Core/Add Block Tray.");
                    continue;
                }

                var mine = pieces.Where(p => p.Owner == tray ||
                                             (p.Owner == null && Holds(rect, p.Box.center))).ToList();
                if (mine.Count == 0) continue;

                // several blocks may be dragged onto one rectangle; clear ground between
                // them is what says they are several, and each becomes a prefab of its own
                var blocks = Split(mine);
                if (blocks.Count > 1)
                {
                    string bare = $"{OutDir}/{tray.name}.prefab";
                    if (AssetDatabase.LoadAssetAtPath<GameObject>(bare) != null)
                        Debug.LogWarning($"[CoreTray] \"{tray.name}\" now holds {blocks.Count} separate " +
                                         $"blocks and writes numbered prefabs, so {bare} is no longer " +
                                         "written by it. Delete it if you do not want it.");
                }

                for (int b = 0; b < blocks.Count; b++)
                {
                    string block = blocks.Count == 1 ? tray.name : $"{tray.name}-{b + 1:00}";

                    // a block's name IS its prefab's name, so two of them called the same
                    // thing would write over each other and only the last would survive. Say
                    // so rather than quietly losing a block someone spent an hour composing.
                    if (!taken.Add(block))
                    {
                        Debug.LogWarning($"[CoreTray] two blocks are called \"{block}\" - only the " +
                                         "first was baked. Rename one of the trays; the name is what " +
                                         "the prefab is filed under.");
                        continue;
                    }

                    var wrote = Bake(block, blocks[b], force, out var line);
                    if (wrote == Wrote.Yes) written++;
                    if (wrote == Wrote.No) continue;
                    said.Add(line);
                    // unchanged counts too - it is on disk either way, and being on disk is
                    // the whole condition for letting it go
                    foreach (var piece in blocks[b]) spent.Add(piece.Go);
                }
            }

            if (written > 0)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            cleared = Clear(scene, spent);
            if (cleared > 0)
                said.Add($"the rectangle was emptied: {cleared} piece(s) taken out of the scene " +
                         "(Ctrl+Z puts them back)");
            return written;
        }

        /// <summary>
        /// Takes the pieces a bake has just written down back out of the scene, so the
        /// rectangle stands empty for the next block instead of being cleared by hand.
        ///
        /// Through Undo, every one of them, because this DELETES: one Ctrl+Z and the block
        /// is standing on the tray again. The tray, its pad and its caption are never
        /// touched - only what the bake actually claimed and filed.
        ///
        /// Worth knowing before leaving it on: it clears what it BAKED, and a rectangle laid
        /// over the demo in place bakes the demo. Drag copies onto a tray standing on clear
        /// ground and there is nothing to lose; move the rectangle over the city itself and
        /// it will take that away too.
        /// </summary>
        static int Clear(Scene scene, List<GameObject> spent) =>
            ClearAfter ? Remove(scene, spent) : 0;

        /// <summary>The removal itself. Through Undo, always - this deletes.</summary>
        static int Remove(Scene scene, List<GameObject> spent)
        {
            if (spent.Count == 0) return 0;

            int gone = 0;
            foreach (var piece in spent)
            {
                if (!piece) continue;
                Undo.DestroyObjectImmediate(piece);
                gone++;
            }
            if (gone == 0) return 0;

            // the pavement group outlives its tiles by one line of code; left standing it
            // would tell the next bake this tray has been paved already
            var trays = TraysOf(scene);
            if (trays)
                foreach (Transform tray in trays)
                {
                    var paving = tray.Find(PavingName);
                    if (paving && paving.childCount == 0) Undo.DestroyObjectImmediate(paving.gameObject);
                }

            Undo.SetCurrentGroupName("Clear block tray");
            EditorSceneManager.MarkSceneDirty(scene);
            return gone;
        }

        /// <summary>
        /// Sweeps the trays clear WITHOUT baking anything - for a block dragged on and
        /// thought better of, or a tray left holding the last one.
        ///
        /// Nothing is written down first, so whatever was standing there is only in the
        /// undo stack afterwards. Which is the difference worth keeping straight: the bake
        /// clears what it has just filed, this clears what nobody asked to keep.
        /// </summary>
        [MenuItem("Tools/City/Core/Empty Trays (without baking)", priority = 23)]
        public static void EmptyTrays()
        {
            var scene = SceneManager.GetActiveScene();
            int gone = Remove(scene, StandingIn(scene));
            Debug.Log(gone > 0
                ? $"[CoreTray] {gone} piece(s) taken off the trays. NOTHING was baked - " +
                  "Ctrl+Z puts them back."
                : "[CoreTray] the trays are already empty.");
        }

        /// <summary>Everything the trays are holding, however it got there.</summary>
        static List<GameObject> StandingIn(Scene scene)
        {
            var standing = new List<GameObject>();
            var trays = TraysOf(scene);
            if (!trays) return standing;

            var pieces = Sweep(scene, trays);
            foreach (Transform tray in trays)
            {
                if (!TryRect(tray, out var rect)) continue;
                foreach (var piece in pieces)
                    if (piece.Owner == tray || (piece.Owner == null && Holds(rect, piece.Box.center)))
                        standing.Add(piece.Go);
            }
            return standing;
        }

        enum Wrote { Yes, Unchanged, No }

        static Wrote Bake(string name, List<Piece> pieces, bool force, out string said,
                          bool pave = true)
        {
            // measured on the GROUND it stands on, not on everything it carries: a lamp's
            // bounding box reaches out over the road it lights, and letting that into the
            // measurement put the pivot half a street off centre and reported a block
            // seventy metres wider than it is
            var box = Ground(pieces);

            // snapped to the module the demo was laid out on, so every tile inside the
            // block keeps its 5 m alignment against the pivot the city places it by
            var centre = new Vector3(Snap(box.center.x), 0f, Snap(box.center.z));
            float width = Up(box.size.x), depth = Up(box.size.z);

            string file = string.Join("_", name.Split(System.IO.Path.GetInvalidFileNameChars()));
            string path = $"{OutDir}/{file}.prefab";
            string signature = Signature(pieces, centre);

            if (!force && signature == SignatureOf(path))
            {
                said = $"{name}: unchanged ({pieces.Count} pieces)";
                return Wrote.Unchanged;
            }

            EnsureFolder(OutDir);
            var root = new GameObject(name);
            int copied = 0;
            foreach (var piece in pieces)
            {
                var p = piece.Go.transform.position;
                // x and z only: a piece stands at the height the pack gave it, and the
                // demo's basements go below zero on purpose
                Restand(piece.Go, root.transform,
                        new Vector3(p.x - centre.x, p.y, p.z - centre.z), out bool linked);
                if (!linked) copied++;
            }

            // a harvest takes the demo as the demo laid it and invents nothing
            int patched = pave ? FillFloor(pieces, root.transform, centre) : 0;

            var tag = root.AddComponent<LivingCity.Generation.BlockLotTag>();
            tag.lotWidth = width;
            tag.lotDepth = depth;

            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);

            said = $"{name}: {pieces.Count} pieces, {width:F0} x {depth:F0} m" +
                   (patched > 0 ? $", {patched} hole(s) in the floor paved over" : "") +
                   (copied > 0 ? $" ({copied} deep-copied - rebuilt or unlinked)" : "");
            return Wrote.Yes;
        }

        /// <summary>
        /// One piece, stood under a new parent exactly as it stands in the scene.
        ///
        /// The link to the source prefab is worth keeping - it is the difference between
        /// a block that is a list of references and a block that is a list of copies - so
        /// the copy is a fresh instance of the source WITH THE SCENE'S OVERRIDES REPLAYED
        /// onto it. Without that replay the demo's own repainting is lost and the block
        /// bakes in the pack's default colours, which is the trap this whole method
        /// exists for.
        ///
        /// A piece that has been rebuilt rather than merely retuned - a child added, a
        /// component added or taken off - has no such replay, so it is deep-copied whole
        /// and loses its link. Completeness first.
        ///
        /// <paramref name="linked"/> says whether the link survived.
        /// </summary>
        internal static GameObject Restand(GameObject piece, Transform parent, Vector3 position,
                                           out bool linked)
        {
            var source = PrefabUtility.GetCorrespondingObjectFromSource(piece);
            linked = source != null && !Rebuilt(piece);

            GameObject copy;
            if (linked)
            {
                copy = (GameObject)PrefabUtility.InstantiatePrefab(source, parent);
                PrefabUtility.SetPropertyModifications(copy, PrefabUtility.GetPropertyModifications(piece));
            }
            else
            {
                copy = Object.Instantiate(piece, parent);
            }

            copy.name = piece.name;
            copy.transform.SetPositionAndRotation(position, piece.transform.rotation);
            // lossy, not local: a piece dragged into a tray that has been scaled carries a
            // local scale that means nothing outside that tray
            copy.transform.localScale = piece.transform.lossyScale;
            return copy;
        }

        /// <summary>Has this instance been changed in a way property modifications cannot
        /// replay - a child added or removed, a component added or stripped?</summary>
        static bool Rebuilt(GameObject piece)
        {
            if (!PrefabUtility.IsPartOfPrefabInstance(piece)) return true;
            // Added CHILDREN are deliberately not counted. In this demo they are almost
            // always another prefab instance parented on in the scene - a vent on a vent -
            // and those are now swept as pieces of their own. Deep-copying the parent to
            // keep them would stand every one of them twice.
            return PrefabUtility.GetRemovedGameObjects(piece).Count > 0 ||
                   PrefabUtility.GetAddedComponents(piece).Count > 0 ||
                   PrefabUtility.GetRemovedComponents(piece).Count > 0;
        }

        /// <summary>What a bake of these pieces would put on disk, as one string, compared
        /// against the same reading of the prefab already there - so a save that changed
        /// nothing rewrites nothing, and Ctrl+S stays cheap however many trays are out.</summary>
        static string Signature(List<Piece> pieces, Vector3 centre)
        {
            var lines = new List<string>(pieces.Count);
            foreach (var piece in pieces)
            {
                var t = piece.Go.transform;
                lines.Add(Row(piece.Go.name,
                              new Vector3(t.position.x - centre.x, t.position.y, t.position.z - centre.z),
                              t.rotation, t.lossyScale));
            }
            lines.Sort(System.StringComparer.Ordinal);
            return string.Join("\n", lines);
        }

        static string SignatureOf(string path)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!asset) return null;

            var lines = new List<string>();
            foreach (Transform child in asset.transform)
            {
                if (child.name == PatchName) continue;
                lines.Add(Row(child.name, child.localPosition, child.localRotation, child.localScale));
            }
            lines.Sort(System.StringComparer.Ordinal);
            return string.Join("\n", lines);
        }

        static string Row(string name, Vector3 position, Quaternion rotation, Vector3 scale) =>
            $"{name}|{position.x:F3},{position.y:F3},{position.z:F3}" +
            $"|{rotation.x:F4},{rotation.y:F4},{rotation.z:F4},{rotation.w:F4}" +
            $"|{scale.x:F3},{scale.y:F3},{scale.z:F3}";

        // ------------------------------------------------------------- the leftover kerb

        /// <summary>
        /// Selects the pavement that is one tile wide and nothing else - the kerb the
        /// lifted roads left behind, and the strip that edged the whole demo.
        ///
        /// It is worth having as a SELECTION rather than as something the harvest works
        /// around. That strip is the single cause of every remaining complaint: it is
        /// continuous, so no rule about gaps can cut it; it welds blocks that a street
        /// separates; whatever survives of it comes out as a block of its own made of
        /// pavement and a few stray lamps; and the thinning that severs it also pinches
        /// real blocks into three or four. Delete the strip and none of that is true any
        /// more, and the thinning can be turned off.
        ///
        /// One tile wide is the whole test. Erode the paved ground by a cell: a strip that
        /// narrow vanishes completely, and everything a block is made of does not. What is
        /// selected is every paving piece with no cell left standing afterwards.
        ///
        /// Buildings are never selected, whatever they stand on.
        /// </summary>
        [MenuItem("Tools/City/Core/Select Leftover Kerb", priority = 4)]
        public static void SelectKerb()
        {
            var scene = SceneManager.GetActiveScene();
            var pieces = Sweep(scene, TraysOf(scene)).Where(p => p.Owner == null).ToList();
            var paving = pieces.Where(p => !Named(p.Go, "SM_Bld_") && IsStructure(p.Go)).ToList();
            if (paving.Count == 0)
            {
                EditorUtility.DisplayDialog("Select Leftover Kerb",
                    "No loose paving in this scene to look through.", "OK");
                return;
            }

            var box = paving[0].Box;
            for (int i = 1; i < paving.Count; i++) box.Encapsulate(paving[i].Box);
            Phase(paving, out float x0, out float z0);

            int i0 = Mathf.FloorToInt((box.min.x - x0) / Cell) - 2;
            int j0 = Mathf.FloorToInt((box.min.z - z0) / Cell) - 2;
            int nx = Mathf.CeilToInt((box.max.x - x0) / Cell) + 2 - i0;
            int nz = Mathf.CeilToInt((box.max.z - z0) / Cell) + 2 - j0;
            if (nx < 3 || nz < 3 || (long)nx * nz > 250000L) return;

            // the ground as it stands, buildings included: a kerb tile tucked against a
            // wall is a metre of a block's floor, not a strip
            var solid = new bool[nx, nz];
            foreach (var piece in pieces)
                if (IsStructure(piece.Go))
                    foreach (var c in Cells(piece.Box, x0, z0, i0, j0, nx, nz))
                        solid[c.x, c.y] = true;

            var fat = new bool[nx, nz];
            for (int i = 1; i < nx - 1; i++)
                for (int j = 1; j < nz - 1; j++)
                    fat[i, j] = solid[i, j] &&
                                solid[i - 1, j] && solid[i + 1, j] &&
                                solid[i, j - 1] && solid[i, j + 1];

            // A kerb tile is ALWAYS thin - it is the edge of the block, so the ground on
            // its far side is the street. Judging it on its own cell would select every
            // kerb in the demo, the ring round each block included. What matters is whether
            // it is the edge OF something: a tile touching a block's body stays, a tile with
            // nothing but more tiles either side of it is the strip.
            var near = new bool[nx, nz];
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                {
                    if (!fat[i, j]) continue;
                    near[i, j] = true;
                    for (int n = 0; n < 8; n++)
                    {
                        int x = i + Step[n].x, z = j + Step[n].y;
                        if (x < 0 || z < 0 || x >= nx || z >= nz) continue;
                        near[x, z] = true;
                    }
                }

            var strip = new List<Object>();
            foreach (var piece in paving)
            {
                bool standing = false;
                foreach (var c in Cells(piece.Box, x0, z0, i0, j0, nx, nz))
                    if (near[c.x, c.y]) { standing = true; break; }
                if (!standing) strip.Add(piece.Go);
            }

            Selection.objects = strip.ToArray();
            Debug.Log($"[CoreTray] {strip.Count} paving piece(s) selected - every one of them a " +
                      "tile wide with nothing beside it, which is the kerb the lifted roads left. " +
                      "Delete them and run the harvest again; blocks will then be cut on bare " +
                      "ground alone, which stops the thinning splitting one block into three.");
        }

        // -------------------------------------------------------------------- the map

        /// <summary>One colour per block, cycled. Neighbours differ, which is the only thing
        /// a map of touching shapes has to get right.</summary>
        static readonly Color[] Inks =
        {
            new Color(0.95f, 0.35f, 0.25f), new Color(0.30f, 0.70f, 0.95f),
            new Color(0.55f, 0.85f, 0.35f), new Color(0.95f, 0.75f, 0.25f),
            new Color(0.75f, 0.45f, 0.90f), new Color(0.30f, 0.85f, 0.75f),
            new Color(0.95f, 0.50f, 0.70f), new Color(0.65f, 0.65f, 0.65f),
        };

        /// <summary>
        /// Paints, over the demo itself, exactly which ground each block is claiming - one
        /// colour per block, one square per cell, standing where the block stands.
        ///
        /// This is the harvest's own working answered out loud, and it exists because
        /// arguing about a block from a list of sizes is hopeless: a number says a block is
        /// 160 x 145 m, and what has to be seen is WHERE those metres are and which of them
        /// belong to the block next door. Nothing is baked and nothing is moved - it is a
        /// drawing laid over the ground, and deleting the root takes it away again.
        /// </summary>
        [MenuItem("Tools/City/Core/Draw Block Map Over The Demo", priority = 6)]
        public static void DrawMap()
        {
            var scene = SceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == MapRoot) Object.DestroyImmediate(root);

            var pieces = Sweep(scene, TraysOf(scene)).Where(p => p.Owner == null).ToList();
            var structure = new List<Piece>();
            var dressing = new List<Piece>();
            foreach (var piece in pieces)
                (IsStructure(piece.Go) ? structure : dressing).Add(piece);
            if (structure.Count == 0)
            {
                EditorUtility.DisplayDialog("Draw Block Map",
                    "No buildings or paving stand loose in this scene.", "OK");
                return;
            }

            var blocks = Split(structure, close: false, pockets: false);
            blocks.RemoveAll(block => block.Count < 2);
            Deal(blocks, dressing);
            blocks.Sort((a, b) =>
            {
                var pa = Middle(a);
                var pb = Middle(b);
                int byX = Mathf.RoundToInt(pa.x).CompareTo(Mathf.RoundToInt(pb.x));
                return byX != 0 ? byX : Mathf.RoundToInt(pa.y).CompareTo(Mathf.RoundToInt(pb.y));
            });

            Phase(structure, out float x0, out float z0);
            var map = new GameObject(MapRoot);

            for (int b = 0; b < blocks.Count; b++)
            {
                var block = blocks[b];
                var box = Ground(block);

                int i0 = Mathf.FloorToInt((box.min.x - x0) / Cell) - 1;
                int j0 = Mathf.FloorToInt((box.min.z - z0) / Cell) - 1;
                int nx = Mathf.CeilToInt((box.max.x - x0) / Cell) + 1 - i0;
                int nz = Mathf.CeilToInt((box.max.z - z0) / Cell) + 1 - j0;
                if (nx < 1 || nz < 1) continue;

                // GROUND by its footprint, everything else by its FOOT. Painting a prop's
                // bounding box was making this drawing lie: a street lamp measures twenty
                // metres across because of the arm hanging over the carriageway, so one
                // lamp painted half a street, and block after block appeared to be spilling
                // over roads it had never touched.
                var held = new bool[nx, nz];
                foreach (var piece in block)
                {
                    if (IsStructure(piece.Go))
                    {
                        foreach (var c in Cells(piece.Box, x0, z0, i0, j0, nx, nz))
                            held[c.x, c.y] = true;
                        continue;
                    }

                    var foot = piece.Go.transform.position;
                    int fi = Mathf.FloorToInt((foot.x - x0) / Cell) - i0;
                    int fj = Mathf.FloorToInt((foot.z - z0) / Cell) - j0;
                    if (fi >= 0 && fj >= 0 && fi < nx && fj < nz) held[fi, fj] = true;
                }

                string name = $"block-{b + 1:00}";
                Tint(name, held, nx, nz, x0, z0, i0, j0, Inks[b % Inks.Length], map.transform);

                float top = box.max.y;
                Caption($"{name} label",
                        $"{name}\n{box.size.x:F0} x {box.size.z:F0} m\n{block.Count} pieces",
                        new Vector3(box.center.x, Mathf.Max(8f, top + 5f), box.center.z),
                        map.transform);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = map;
            Debug.Log($"[CoreTray] {blocks.Count} block(s) painted over the demo under " +
                      $"\"{MapRoot}\" - one colour each, one square per 5 m cell they claim. " +
                      "Nothing was baked. Delete that root to clear the drawing.");
        }

        /// <summary>What a block MEASURES, counting only the ground it stands on. A block's
        /// pieces include its lamps, and a lamp's bounding box reaches out over the road it
        /// lights, so measuring everything reports a block half a street wider than it
        /// is.</summary>
        static Bounds Ground(List<Piece> block)
        {
            bool any = false;
            var box = new Bounds();
            foreach (var piece in block)
            {
                if (!IsStructure(piece.Go)) continue;
                if (!any) { box = piece.Box; any = true; } else box.Encapsulate(piece.Box);
            }
            if (any) return box;

            box = block[0].Box;
            for (int i = 1; i < block.Count; i++) box.Encapsulate(block[i].Box);
            return box;
        }

        /// <summary>One block's claim as a single flat mesh - a quad per cell, welded into
        /// one object so a map of thirty blocks is thirty renderers and not three thousand.</summary>
        static void Tint(string name, bool[,] held, int nx, int nz, float x0, float z0,
                          int i0, int j0, Color ink, Transform parent)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                {
                    if (!held[i, j]) continue;
                    float x = x0 + (i0 + i) * Cell, z = z0 + (j0 + j) * Cell;
                    int v = verts.Count;
                    verts.Add(new Vector3(x, MapHeight, z));
                    verts.Add(new Vector3(x + Cell, MapHeight, z));
                    verts.Add(new Vector3(x + Cell, MapHeight, z + Cell));
                    verts.Add(new Vector3(x, MapHeight, z + Cell));
                    tris.Add(v); tris.Add(v + 2); tris.Add(v + 1);
                    tris.Add(v); tris.Add(v + 3); tris.Add(v + 2);
                }
            if (verts.Count == 0) return;

            var mesh = new Mesh { name = name };
            if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = Ink(ink);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        /// <summary>Well above the pavement: the map is meant to be read from the air, and a
        /// square that fights the kerb for the same depth reads as neither.</summary>
        const float MapHeight = 0.4f;

        /// <summary>One saved material per ink, so the drawing survives a reload instead of
        /// turning magenta the first time the editor recompiles.</summary>
        static Material Ink(Color colour)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Materials"))
                AssetDatabase.CreateFolder("Assets", "Materials");

            string path = $"Assets/Materials/CoreBlockMap_{ColorUtility.ToHtmlStringRGB(colour)}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
                if (shader == null) return null;
                mat = new Material(shader) { name = System.IO.Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(mat, path);
            }

            var paint = new Color(colour.r, colour.g, colour.b, 0.55f);
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);
            if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", paint);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", paint);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ---------------------------------------------------------------- the harvest

        /// <summary>Matched against the SOURCE prefab's name, not the instance's: the demo
        /// renames and numbers its instances freely, and the source never lies.</summary>
        static bool Named(GameObject go, params string[] prefixes)
        {
            var source = PrefabUtility.GetCorrespondingObjectFromSource(go);
            string name = source != null ? source.name : go.name;
            foreach (var prefix in prefixes)
                if (name.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>
        /// What decides where one block ends: its ground - the paving, the grass and the
        /// buildings standing on them.
        ///
        /// The paving has to be in it. Cutting on buildings alone looked right and was not:
        /// the demo runs alleys, light wells and yards through the middle of a block, and
        /// every one of those became a cut, so one block came out as three or four. The
        /// pavement runs straight through them, which is precisely what says they are one
        /// block.
        ///
        /// What the paving brings with it is the long thin kerb that lined every lifted
        /// road, welding distant blocks into 240 m slabs. That is not solved by cutting
        /// somewhere else - it is solved by <see cref="Cap"/>, below.
        /// </summary>
        static bool IsStructure(GameObject go) =>
            Named(go, "SM_Bld_", "SM_Env_Sidewalk", "SM_Env_Grass");

        /// <summary>The ground a block stands on, as the demo laid it. What a block's
        /// territory is allowed to spread along.</summary>
        static bool IsPaving(Bounds box, GameObject go) =>
            box.size.y <= 1.5f && Named(go, "SM_Env_Sidewalk", "SM_Env_Grass");

        /// <summary>
        /// Is this piece read by its FOOTPRINT rather than by its foot?
        ///
        /// Everything the pack calls environment is: paving, grass, fences, trees, subway
        /// entrances. Those sit ON the ground, so the ground they cover is the truth about
        /// where they are - and some of them are pivoted at one END, which is how a subway
        /// entrance came to be standing with its foot on bare ground seven metres from its
        /// own body, and was thrown away as belonging to nothing.
        ///
        /// Props are not, and must not be. A street lamp's box reaches out over the
        /// carriageway it lights, and reading that box is what put the lamps of a lifted
        /// road into the block across the way.
        /// </summary>
        static bool ByFootprint(Bounds box, GameObject go) =>
            IsPaving(box, go) || Named(go, "SM_Env_");

        /// <summary>Wider than this on a side and it is not a block: it is two blocks with
        /// the lifted road's kerb still stitching them together. Same figure the automatic
        /// rip uses, and for the same demo.</summary>
        const float Cap = 130f;

        /// <summary>
        /// Takes the whole demo at once, now that its roads have been lifted out.
        ///
        /// With the carriageway gone, the blocks are already separate things standing on
        /// separate patches of pavement - so there is nothing left to choose by hand. This
        /// cuts on the STRUCTURE alone (paving, buildings, grass) with the gap-closing
        /// turned off, because what separates two blocks now is exactly the width of the
        /// street that used to run between them.
        ///
        /// Then the dressing is dealt back: every prop, tree, fence, sign and aircon unit
        /// joins the block whose ground it actually stands on, cell for cell, keeping the
        /// position it has always had. Anything standing on NO block - the traffic lights,
        /// dividers and covers left stranded where the road was - is ignored, which is the
        /// whole reason the cutting is done on structure first.
        ///
        /// A patch with no building on it is not a block: that is the demo's own outer
        /// kerb, the pavement that edged the city, and it is dropped.
        /// </summary>
        [MenuItem("Tools/City/Core/Harvest Blocks From Scene", priority = 5)]
        public static void Harvest()
        {
            var scene = SceneManager.GetActiveScene();
            var pieces = Sweep(scene, TraysOf(scene)).Where(p => p.Owner == null).ToList();

            var structure = new List<Piece>();
            var dressing = new List<Piece>();
            foreach (var piece in pieces)
                (IsStructure(piece.Go) ? structure : dressing).Add(piece);

            if (structure.Count == 0)
            {
                EditorUtility.DisplayDialog("Harvest Blocks",
                    "No buildings stand loose in this scene, so there is no block here to " +
                    "take.", "OK");
                return;
            }

            // Not closed, and capped: bare cells are where the road was lifted out, and
            // anything that still comes out bigger than a city block is a kerb strip
            // stitching two of them together, which the erosions cut.
            // No thinning: the one-tile kerb it was there to sever has been taken out of
            // the scene by hand, which is the better cure - the thinning also pinched real
            // blocks at their narrow points and split one into three.
            //
            // No pockets either. Claiming whatever a block encircles sounds right and is
            // not: once the blocks along a street close the ring at both of its ends, the
            // STREET is enclosed too, and a block was taking the carriageway and every lamp
            // standing in it. A block is the ground it actually covers.
            var blocks = Split(structure, close: false, pockets: false);
            int strips = blocks.RemoveAll(block => block.Count < 2);
            if (blocks.Count == 0)
            {
                EditorUtility.DisplayDialog("Harvest Blocks",
                    "Every group of buildings found was a single stray piece - nothing that " +
                    "reads as a block.", "OK");
                return;
            }

            int stranded = Deal(blocks, dressing);

            // west to east, then south to north, so the numbering reads off the map
            blocks.Sort((a, b) =>
            {
                var pa = Middle(a);
                var pb = Middle(b);
                int byX = Mathf.RoundToInt(pa.x).CompareTo(Mathf.RoundToInt(pb.x));
                return byX != 0 ? byX : Mathf.RoundToInt(pa.y).CompareTo(Mathf.RoundToInt(pb.y));
            });

            EnsureFolder(OutDir);
            var said = new List<string>();
            int written = 0;
            for (int i = 0; i < blocks.Count; i++)
            {
                string name = $"block-{i + 1:00}";
                var at = Middle(blocks[i]);
                if (Bake(name, blocks[i], force: true, out var line, pave: false) == Wrote.Yes)
                    written++;
                said.Add($"{line}   (stood at {at.x:F0}, {at.y:F0} in the demo)");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            int stood = Review(scene, out var yard);
            EditorSceneManager.MarkSceneDirty(scene);

            var view = SceneView.lastActiveSceneView;
            if (view) view.Frame(yard, false);

            Debug.Log($"[CoreTray] harvested {written} block(s) from the scene into {OutDir}, " +
                      $"and stood {stood} of them south of the city:\n  " + string.Join("\n  ", said) +
                      $"\n[CoreTray] {strips} lone building part(s) were skipped, and {stranded} " +
                      "loose piece(s) belonging to no block - the kerb that lined the lifted " +
                      "roads, and the props stranded out in them - were left where they are.");
        }

        /// <summary>How far a block's territory may spread along the paving from its own
        /// walls - three cells, fifteen metres. Wide enough for the ring of pavement round
        /// a block and the yard behind it; short enough that it never walks the length of
        /// the kerb that used to line the road.</summary>
        const int Spread = 3;

        /// <summary>
        /// Gives every loose piece to the block whose ground it stands on - the paving it
        /// was laid on, the props on its walls and roofs, the trees and fences in its yard.
        ///
        /// Territory is grown from the BUILDINGS outward THROUGH THE PAVING, a cell at a
        /// time and no further than <see cref="Spread"/>, all blocks growing at once so a
        /// shared strip is halved between them rather than fought over. That shape is the
        /// point: it follows the ground the block actually stands on instead of taking a
        /// box round it, so the kerb that ran away down the lifted road is reached for
        /// fifteen metres and then dropped, and nothing at all is reached across the bare
        /// scar where the carriageway was.
        ///
        /// A piece is then read by CELLS rather than by distance: a sign bolted to a wall,
        /// an aircon unit on a roof and a bin against a shopfront all sit over the block's
        /// own cells, while a traffic light stranded in a lifted road sits over none. One
        /// too small to fill a cell - a hydrant, a manhole cover - is placed by the single
        /// cell its middle falls in, or it would measure as covering nothing and be thrown
        /// away with the road.
        ///
        /// Returns how many pieces belonged to no block and were left where they were.
        /// </summary>
        static int Deal(List<List<Piece>> blocks, List<Piece> dressing)
        {
            bool any = false;
            var box = new Bounds();
            foreach (var block in blocks)
                foreach (var piece in block)
                    if (!any) { box = piece.Box; any = true; } else box.Encapsulate(piece.Box);
            foreach (var piece in dressing)
                box.Encapsulate(piece.Box);
            if (!any) return dressing.Count;

            var everything = new List<Piece>(dressing);
            foreach (var block in blocks) everything.AddRange(block);
            Phase(everything, out float x0, out float z0);

            int i0 = Mathf.FloorToInt((box.min.x - x0) / Cell) - 1;
            int j0 = Mathf.FloorToInt((box.min.z - z0) / Cell) - 1;
            int nx = Mathf.CeilToInt((box.max.x - x0) / Cell) + 1 - i0;
            int nz = Mathf.CeilToInt((box.max.z - z0) / Cell) + 1 - j0;
            if (nx < 1 || nz < 1 || (long)nx * nz > 1000000L) return dressing.Count;

            var owner = new int[nx, nz];
            var wave = new List<Vector2Int>();
            for (int b = 0; b < blocks.Count; b++)
                foreach (var piece in blocks[b])
                    foreach (var c in Cells(piece.Box, x0, z0, i0, j0, nx, nz))
                        if (owner[c.x, c.y] == 0)
                        {
                            owner[c.x, c.y] = b + 1;
                            wave.Add(c);
                        }

            // where the demo's own ground lies, which is the only way territory travels
            var paved = new bool[nx, nz];
            foreach (var piece in dressing)
                if (IsPaving(piece.Box, piece.Go))
                    foreach (var c in Cells(piece.Box, x0, z0, i0, j0, nx, nz))
                        paved[c.x, c.y] = true;

            for (int step = 0; step < Spread && wave.Count > 0; step++)
            {
                var next = new List<Vector2Int>();
                foreach (var c in wave)
                {
                    int mine = owner[c.x, c.y];
                    Spill(owner, paved, next, nx, nz, c.x + 1, c.y, mine);
                    Spill(owner, paved, next, nx, nz, c.x - 1, c.y, mine);
                    Spill(owner, paved, next, nx, nz, c.x, c.y + 1, mine);
                    Spill(owner, paved, next, nx, nz, c.x, c.y - 1, mine);
                }
                wave = next;
            }

            int stranded = 0;
            var votes = new Dictionary<int, int>();
            foreach (var piece in dressing)
            {
                int mine;
                if (ByFootprint(piece.Box, piece.Go))
                {
                    // ground reads honestly by the cells it covers
                    votes.Clear();
                    foreach (var c in Cells(piece.Box, x0, z0, i0, j0, nx, nz))
                    {
                        int who = owner[c.x, c.y];
                        if (who == 0) continue;
                        votes.TryGetValue(who, out int n);
                        votes[who] = n + 1;
                    }

                    mine = 0;
                    int most = 0;
                    foreach (var vote in votes)
                        if (vote.Value > most) { most = vote.Value; mine = vote.Key; }
                    if (mine == 0)
                    {
                        int ci = Mathf.FloorToInt((piece.Box.center.x - x0) / Cell) - i0;
                        int cj = Mathf.FloorToInt((piece.Box.center.z - z0) / Cell) - j0;
                        if (ci >= 0 && cj >= 0 && ci < nx && cj < nz) mine = owner[ci, cj];
                    }
                }
                else
                {
                    // A PROP IS JUDGED BY ITS FOOT. Its bounding box is not where it is -
                    // a street lamp's box is twenty metres wide because of the arm that
                    // reaches out over the carriageway, and reading that box handed the
                    // lamps standing in a lifted road to the block across the way, where
                    // they turned up hanging over bare ground. The pivot is at the base on
                    // every prop in this pack, and the base is the thing that is standing
                    // somewhere.
                    var foot = piece.Go.transform.position;
                    int fi = Mathf.FloorToInt((foot.x - x0) / Cell) - i0;
                    int fj = Mathf.FloorToInt((foot.z - z0) / Cell) - j0;
                    mine = fi >= 0 && fj >= 0 && fi < nx && fj < nz ? owner[fi, fj] : 0;
                }

                if (mine == 0) { stranded++; continue; }
                blocks[mine - 1].Add(piece);
            }
            return stranded;
        }

        static void Spill(int[,] owner, bool[,] paved, List<Vector2Int> next,
                          int nx, int nz, int i, int j, int mine)
        {
            if (i < 0 || j < 0 || i >= nx || j >= nz) return;
            if (owner[i, j] != 0 || !paved[i, j]) return;
            owner[i, j] = mine;
            next.Add(new Vector2Int(i, j));
        }

        // ------------------------------------------------------------------ the cutting

        /// <summary>
        /// Cuts what one rectangle claimed into separate blocks wherever there is clear
        /// ground between them, so several blocks can be dragged onto one tray at once and
        /// come off it as several prefabs.
        ///
        /// It works in CELLS rather than distances between pieces, because a block is a
        /// contiguous patch of ground and not a swarm of near neighbours: a lamp post ten
        /// metres from a wall is still that block's lamp post when the pavement runs from
        /// one to the other, and is not when it does not.
        ///
        /// The patch is grown by a cell before it is cut, so a driveway, a light well or a
        /// gap between two tiles never saws a block in half. What that leaves is the rule
        /// worth knowing: up to ten metres of nothing joins, fifteen and more separates.
        /// Every street in this demo is wider than that, and no yard inside a block is.
        ///
        /// <paramref name="close"/> is what turns that growing off, for the one case where
        /// it is wrong: a demo whose ROADS HAVE BEEN LIFTED OUT, where what separates two
        /// blocks is exactly the width of the carriageway that used to be there - ten
        /// metres, which the growing would close. Cutting on the bare patches instead
        /// leaves every block where the pavement actually stops.
        ///
        /// <paramref name="open"/> is the answer to the long thin KERB. What the demo left
        /// when its roads were lifted is not only the ring round each block: it is a line of
        /// pavement one tile wide that ran the length of every street and round the whole
        /// city, and that line touches block after block. No rule about gaps can cut it,
        /// because there is no gap - it is a continuous strip. Thinning the covered ground
        /// by a cell first erases anything one cell wide and leaves everything fatter than
        /// that standing, which is precisely the difference between a kerb and a block.
        /// Pieces left on the ring the thinning took find their way home through
        /// <see cref="Nearest"/>.
        ///
        /// <paramref name="cap"/> is the older, narrower version of the same idea, kept for
        /// a patch that comes out too big for any other reason. A patch that comes out WIDER than the cap is eroded by a cell and cut
        /// again: one erosion severs a strip two cells wide while leaving a real block
        /// untouched, and three rounds of it separate everything this demo welds together.
        /// It is the same trick <see cref="SyntyDemoBlockRip"/> plays for the same reason.
        /// Zero turns it off, which is what a hand-dragged tray wants - there, whatever was
        /// dragged on is the block, however big.
        /// </summary>
        static List<List<Piece>> Split(List<Piece> pieces, bool close = true, float cap = 0f,
                                      int open = 0, bool pockets = true)
        {
            var one = new List<List<Piece>> { pieces };
            if (pieces.Count < 2) return one;

            var box = pieces[0].Box;
            for (int i = 1; i < pieces.Count; i++) box.Encapsulate(pieces[i].Box);

            Phase(pieces, out float x0, out float z0);

            // a ring of margin, so a piece on the outermost edge still has room to grow into
            int i0 = Mathf.FloorToInt((box.min.x - x0) / Cell) - 2;
            int i1 = Mathf.CeilToInt((box.max.x - x0) / Cell) + 2;
            int j0 = Mathf.FloorToInt((box.min.z - z0) / Cell) - 2;
            int j1 = Mathf.CeilToInt((box.max.z - z0) / Cell) + 2;
            int nx = i1 - i0, nz = j1 - j0;
            if (nx < 3 || nz < 3 || (long)nx * nz > 250000L) return one;

            var solid = new bool[nx, nz];
            foreach (var piece in pieces)
                foreach (var c in Cells(piece.Box, x0, z0, i0, j0, nx, nz))
                    solid[c.x, c.y] = true;

            // grown by one cell in every direction: two patches with two cells of nothing
            // between them close up and count as one block, three cells stay two blocks
            var grown = solid;
            if (close)
            {
                grown = new bool[nx, nz];
                for (int i = 0; i < nx; i++)
                    for (int j = 0; j < nz; j++)
                        if (solid[i, j])
                            for (int a = -1; a <= 1; a++)
                                for (int b = -1; b <= 1; b++)
                                    grown[Mathf.Clamp(i + a, 0, nx - 1),
                                          Mathf.Clamp(j + b, 0, nz - 1)] = true;
            }

            for (int o = 0; o < open; o++)
            {
                var thinner = new bool[nx, nz];
                for (int i = 1; i < nx - 1; i++)
                    for (int j = 1; j < nz - 1; j++)
                        thinner[i, j] = grown[i, j] &&
                                        grown[i - 1, j] && grown[i + 1, j] &&
                                        grown[i, j - 1] && grown[i, j + 1];
                grown = thinner;
            }

            var label = new int[nx, nz];
            int blocks = 0;
            var todo = grown;
            var seen = new bool[nx, nz];
            var stack = new Stack<Vector2Int>();
            var cells = new List<Vector2Int>();

            for (int round = 0; round <= Erosions; round++)
            {
                var retry = new bool[nx, nz];
                bool again = false;
                System.Array.Clear(seen, 0, seen.Length);

                for (int i = 0; i < nx; i++)
                    for (int j = 0; j < nz; j++)
                    {
                        if (!todo[i, j] || seen[i, j]) continue;

                        cells.Clear();
                        int minI = i, maxI = i, minJ = j, maxJ = j;
                        seen[i, j] = true;
                        stack.Push(new Vector2Int(i, j));
                        while (stack.Count > 0)
                        {
                            var c = stack.Pop();
                            cells.Add(c);
                            minI = Mathf.Min(minI, c.x); maxI = Mathf.Max(maxI, c.x);
                            minJ = Mathf.Min(minJ, c.y); maxJ = Mathf.Max(maxJ, c.y);
                            for (int n = 0; n < 8; n++)
                            {
                                // four ways when the cut is strict. A diagonal step crosses
                                // a corner where two streets meet, and that one step was
                                // enough to weld the blocks on either side of them together.
                                if (!close && n >= 4) break;
                                int x = c.x + Step[n].x, z = c.y + Step[n].y;
                                if (x < 0 || z < 0 || x >= nx || z >= nz) continue;
                                if (!todo[x, z] || seen[x, z]) continue;
                                seen[x, z] = true;
                                stack.Push(new Vector2Int(x, z));
                            }
                        }

                        // measured at the size it will be BAKED at: an eroded patch is given
                        // back the ring the erosion took, and judging it before that throws
                        // away every block an erosion had to touch
                        float wide = (maxI - minI + 1 + 2 * round) * Cell;
                        float deep = (maxJ - minJ + 1 + 2 * round) * Cell;
                        if (cap > 0f && round < Erosions && (wide > cap || deep > cap))
                        {
                            var patch = new bool[nx, nz];
                            foreach (var c in cells) patch[c.x, c.y] = true;
                            foreach (var c in cells)
                                retry[c.x, c.y] =
                                    c.x > 0 && c.x < nx - 1 && c.y > 0 && c.y < nz - 1 &&
                                    patch[c.x - 1, c.y] && patch[c.x + 1, c.y] &&
                                    patch[c.x, c.y - 1] && patch[c.x, c.y + 1];
                            again = true;
                            continue;
                        }

                        blocks++;
                        foreach (var c in cells) label[c.x, c.y] = blocks;
                    }

                if (!again) break;
                todo = retry;
            }
            if (blocks < 2) return one;

            if (pockets) Inside(label, nx, nz);

            var cut = new List<List<Piece>>();
            for (int b = 0; b < blocks; b++) cut.Add(new List<Piece>());
            foreach (var piece in pieces)
            {
                int mine = 0;
                foreach (var c in Cells(piece.Box, x0, z0, i0, j0, nx, nz))
                    if (label[c.x, c.y] != 0) { mine = label[c.x, c.y]; break; }

                // A piece can be too small to fill a cell at all - a door, a roof unit, a
                // window box - because Cells() takes half a metre off every side before it
                // measures. Handing those to the first block was quietly gathering every
                // small part in the demo into one 154 x 115 m "block" that held six walls.
                // Ask instead where it is STANDING, then look a little wider.
                if (mine == 0)
                    mine = Nearest(label, nx, nz, piece.Box.center, x0, z0, i0, j0, open + 1);
                if (mine == 0) continue;   // standing on nothing at all: it is not in a block

                cut[mine - 1].Add(piece);
            }
            cut.RemoveAll(block => block.Count == 0);
            if (cut.Count < 2) return one;

            // west to east, then south to north, so the numbering a bake hands out is the
            // order they stand in and not the order the flood happened to find them
            cut.Sort((a, b) =>
            {
                var pa = Middle(a);
                var pb = Middle(b);
                int byX = Mathf.RoundToInt(pa.x).CompareTo(Mathf.RoundToInt(pb.x));
                return byX != 0 ? byX : Mathf.RoundToInt(pa.y).CompareTo(Mathf.RoundToInt(pb.y));
            });
            return cut;
        }

        /// <summary>The block covering the cell a point stands in, or the nearest one
        /// within reach. It has to reach as far as the erosions cut, or every piece standing
        /// on the ring they took would be left with no block at all.</summary>
        static int Nearest(int[,] label, int nx, int nz, Vector3 point,
                           float x0, float z0, int i0, int j0, int reach)
        {
            int mi = Mathf.FloorToInt((point.x - x0) / Cell) - i0;
            int mj = Mathf.FloorToInt((point.z - z0) / Cell) - j0;
            for (int ring = 0; ring <= reach; ring++)
                for (int a = -ring; a <= ring; a++)
                    for (int b = -ring; b <= ring; b++)
                    {
                        if (Mathf.Max(Mathf.Abs(a), Mathf.Abs(b)) != ring) continue;
                        int i = mi + a, j = mj + b;
                        if (i < 0 || j < 0 || i >= nx || j >= nz) continue;
                        if (label[i, j] != 0) return label[i, j];
                    }
            return 0;
        }

        /// <summary>
        /// Gives a block everything its own kerb encloses.
        ///
        /// A block is not the pavement - it is what the pavement goes round. A park is
        /// grass and trees inside a ring of kerb, a yard is bare ground behind a terrace,
        /// and a courtyard is nothing at all with a building on four sides of it; none of
        /// those is covered by anything the cutter can see, and every one of them belongs
        /// to the block that encircles it. Cutting on the covered cells alone took the
        /// kerb and left the park standing outside it.
        ///
        /// Which empty cells are INSIDE is not a matter of looking - it is a matter of
        /// what the open ground can reach. Flooding the empty cells inwards from the edge
        /// of the grid finds everything that is outside; whatever the flood never arrives
        /// at is enclosed, and is handed to whichever block encircles it.
        /// </summary>
        static void Inside(int[,] label, int nx, int nz)
        {
            var outside = new bool[nx, nz];
            var stack = new Stack<Vector2Int>();
            for (int i = 0; i < nx; i++) { Air(stack, outside, label, nx, nz, i, 0);
                                           Air(stack, outside, label, nx, nz, i, nz - 1); }
            for (int j = 0; j < nz; j++) { Air(stack, outside, label, nx, nz, 0, j);
                                           Air(stack, outside, label, nx, nz, nx - 1, j); }
            while (stack.Count > 0)
            {
                var c = stack.Pop();
                Air(stack, outside, label, nx, nz, c.x + 1, c.y);
                Air(stack, outside, label, nx, nz, c.x - 1, c.y);
                Air(stack, outside, label, nx, nz, c.x, c.y + 1);
                Air(stack, outside, label, nx, nz, c.x, c.y - 1);
            }

            Pockets(label, outside, nx, nz);

            // every enclosed cell takes the label of whatever it is enclosed BY, spreading
            // inwards from the ring so a pocket several cells across is filled throughout
            var wave = new List<Vector2Int>();
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                    if (label[i, j] != 0) wave.Add(new Vector2Int(i, j));

            while (wave.Count > 0)
            {
                var next = new List<Vector2Int>();
                foreach (var c in wave)
                {
                    int mine = label[c.x, c.y];
                    Enclose(label, outside, next, nx, nz, c.x + 1, c.y, mine);
                    Enclose(label, outside, next, nx, nz, c.x - 1, c.y, mine);
                    Enclose(label, outside, next, nx, nz, c.x, c.y + 1, mine);
                    Enclose(label, outside, next, nx, nz, c.x, c.y - 1, mine);
                }
                wave = next;
            }
        }

        /// <summary>
        /// Sorts the enclosed ground into pockets a block should have, and corridors it
        /// should not.
        ///
        /// A courtyard behind a terrace is enclosed, and belongs to the block round it. So
        /// is a street, whenever the blocks along it happen to close the ring at both ends
        /// - and handing THAT to a block gives it the whole carriageway and every lamp
        /// standing in it, which is exactly the complaint this is answering.
        ///
        /// What tells them apart is shape, not size alone. A yard is compact and fills its
        /// own rectangle; a street is long, thin, and mostly rectangle it does not fill.
        /// Anything failing either test is put back outside where it came from.
        /// </summary>
        static void Pockets(int[,] label, bool[,] outside, int nx, int nz)
        {
            var seen = new bool[nx, nz];
            var stack = new Stack<Vector2Int>();
            var cells = new List<Vector2Int>();

            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                {
                    if (seen[i, j] || outside[i, j] || label[i, j] != 0) continue;

                    cells.Clear();
                    int minI = i, maxI = i, minJ = j, maxJ = j;
                    seen[i, j] = true;
                    stack.Push(new Vector2Int(i, j));
                    while (stack.Count > 0)
                    {
                        var c = stack.Pop();
                        cells.Add(c);
                        minI = Mathf.Min(minI, c.x); maxI = Mathf.Max(maxI, c.x);
                        minJ = Mathf.Min(minJ, c.y); maxJ = Mathf.Max(maxJ, c.y);
                        for (int n = 0; n < 4; n++)
                        {
                            int x = c.x + Step[n].x, z = c.y + Step[n].y;
                            if (x < 0 || z < 0 || x >= nx || z >= nz) continue;
                            if (seen[x, z] || outside[x, z] || label[x, z] != 0) continue;
                            seen[x, z] = true;
                            stack.Push(new Vector2Int(x, z));
                        }
                    }

                    int wide = maxI - minI + 1, deep = maxJ - minJ + 1;
                    bool yard = Mathf.Max(wide, deep) * Cell <= Pocket &&
                                cells.Count >= 0.5f * wide * deep;
                    if (yard) continue;

                    foreach (var c in cells) outside[c.x, c.y] = true;
                }
        }

        /// <summary>Longest a pocket may be on a side and still be a yard rather than a
        /// street with its ends closed off.</summary>
        const float Pocket = 60f;

        static void Air(Stack<Vector2Int> stack, bool[,] outside, int[,] label,
                        int nx, int nz, int i, int j)
        {
            if (i < 0 || j < 0 || i >= nx || j >= nz) return;
            if (outside[i, j] || label[i, j] != 0) return;
            outside[i, j] = true;
            stack.Push(new Vector2Int(i, j));
        }

        static void Enclose(int[,] label, bool[,] outside, List<Vector2Int> next,
                            int nx, int nz, int i, int j, int mine)
        {
            if (i < 0 || j < 0 || i >= nx || j >= nz) return;
            if (label[i, j] != 0 || outside[i, j]) return;
            label[i, j] = mine;
            next.Add(new Vector2Int(i, j));
        }

        /// <summary>How many times an oversized patch is thinned before it is taken as it
        /// is. Three cuts every neck this demo has.</summary>
        const int Erosions = 3;

        /// <summary>
        /// Where the grid's lines fall, read off the paving itself rather than assumed.
        ///
        /// One flat module-square tile is enough to fix it: the demo lays them edge to
        /// edge, so any one of their edges is on the grid every other one is on. With no
        /// paving to read - a tray holding nothing but a building - round fives will do,
        /// since there is then nothing whose edges could be cut in half.
        /// </summary>
        static void Phase(List<Piece> pieces, out float x0, out float z0)
        {
            foreach (var piece in pieces)
                if (IsTile(piece.Box))
                {
                    x0 = piece.Box.min.x - Mathf.Floor(piece.Box.min.x / Cell) * Cell;
                    z0 = piece.Box.min.z - Mathf.Floor(piece.Box.min.z / Cell) * Cell;
                    return;
                }
            x0 = z0 = 0f;
        }

        /// <summary>The four ways first, then the four corners, so one loop serves both a
        /// strict cut and a lenient one.</summary>
        static readonly Vector2Int[] Step =
        {
            new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1),
            new Vector2Int(1, 1), new Vector2Int(1, -1), new Vector2Int(-1, 1), new Vector2Int(-1, -1),
        };

        static Vector2 Middle(List<Piece> block)
        {
            var box = block[0].Box;
            for (int i = 1; i < block.Count; i++) box.Encapsulate(block[i].Box);
            return new Vector2(box.center.x, box.center.z);
        }

        /// <summary>
        /// Which cells of the grid a piece stands in. Half a metre is taken off every side
        /// first: these tiles are laid edge to edge, and a tile that reaches exactly to a
        /// cell boundary would otherwise claim the cell beyond it and weld itself to
        /// whatever is over there.
        ///
        /// The grid's PHASE has to be handed in, and getting it wrong is not a rounding
        /// error - it is the difference between a street and no street. This demo lays its
        /// paving on fives offset by a metre (tile edges at -189, -184, -179), so a grid
        /// snapped to round fives cuts every tile across two cells in each axis. One tile
        /// then reads as four, every gap smears shut, and a 5 m one-way street stops
        /// existing. See <see cref="Phase"/>.
        /// </summary>
        static IEnumerable<Vector2Int> Cells(Bounds box, float x0, float z0,
                                            int i0, int j0, int nx, int nz)
        {
            int ai = Mathf.Max(0, Mathf.FloorToInt((box.min.x + 0.5f - x0) / Cell) - i0);
            int bi = Mathf.Min(nx - 1, Mathf.FloorToInt((box.max.x - 0.5f - x0) / Cell) - i0);
            int aj = Mathf.Max(0, Mathf.FloorToInt((box.min.z + 0.5f - z0) / Cell) - j0);
            int bj = Mathf.Min(nz - 1, Mathf.FloorToInt((box.max.z - 0.5f - z0) / Cell) - j0);
            for (int i = ai; i <= bi; i++)
                for (int j = aj; j <= bj; j++)
                    yield return new Vector2Int(i, j);
        }

        // ------------------------------------------------------------------- the floor

        /// <summary>The pack's plain pavement square: the flat fill that lies between the
        /// kerb and the building line, which is what a block's floor is made of. Only the
        /// fallback - a patch is cut from the block's OWN paving wherever it has some, so
        /// it carries whatever the demo repainted that block's pavement to.</summary>
        const string FillTile =
            "Assets/Synty/PolygonCity/Prefabs/Environments/SM_Env_Sidewalk_01.prefab";

        /// <summary>What a laid patch is called inside the block. The change detector skips
        /// them, so the bake never mistakes its own work for something that was dragged on
        /// and rewrites the prefab on every save for ever.</summary>
        const string PatchName = "floor patch";

        /// <summary>How much of a cell something has to cover before the cell counts as
        /// covered. A tile covers its own cell whole; a building overlapping a cell by a
        /// hand's breadth has not floored it.</summary>
        const float Covers = 0.35f;

        /// <summary>Flat, at ground level, and one module square - the demo's paving.</summary>
        static bool IsTile(Bounds box) =>
            box.size.y <= 1.5f && box.max.y <= 1.5f && box.min.y >= -1.5f &&
            Mathf.Abs(box.size.x - Cell) < 1f && Mathf.Abs(box.size.z - Cell) < 1f;

        /// <summary>Paving with no other word in its name - "SM_Env_Sidewalk_07" and not
        /// "SM_Env_Sidewalk_Straight_01". The kerbs, dips and gutters are a block's EDGE,
        /// and one of those laid in the middle of a yard reads as a kerb across it.</summary>
        static bool IsPlainTile(GameObject go)
        {
            var source = PrefabUtility.GetCorrespondingObjectFromSource(go);
            string name = source != null ? source.name : go.name;
            return System.Text.RegularExpressions.Regex.IsMatch(name, @"^SM_Env_Sidewalk_\d+$");
        }

        /// <summary>
        /// Lays pavement in the holes - the cells inside a block that nothing stands on
        /// and nothing covers, which a block dropped on the city would be seen straight
        /// through.
        ///
        /// The grid is taken FROM THE BLOCK rather than assumed. This demo's paving is
        /// laid on fives offset by a metre - its tile edges fall on -189, -184, -179 - so
        /// a grid snapped to multiples of five would cut every tile in half and report
        /// holes everywhere. One of the block's own tiles gives the phase; a plain one of
        /// them, if it has any, gives the patch its material and its corner pivot too.
        ///
        /// A hole is uncovered AND enclosed. Flooding the uncovered cells inwards from the
        /// border is what tells those apart: the open ground outside a block is not a hole
        /// in it, and an L-shaped block keeps its notch instead of being squared off.
        ///
        /// Coverage counts EVERYTHING, not just paving - a building's own footprint is not
        /// a hole, though it is not pavement either. It is measured off bounding boxes,
        /// which overstates what a building covers; that errs towards leaving a hole
        /// alone rather than driving a slab of pavement through somebody's front steps.
        /// </summary>
        static int FillFloor(List<Piece> pieces, Transform root, Vector3 centre)
        {
            bool phased = false, plained = false;
            float x0 = 0f, z0 = 0f;
            GameObject plain = null;

            foreach (var piece in pieces)
            {
                if (!IsTile(piece.Box)) continue;
                if (!phased) { x0 = piece.Box.min.x; z0 = piece.Box.min.z; phased = true; }
                if (!plained && IsPlainTile(piece.Go)) { plain = piece.Go; plained = true; }
            }
            if (!phased) return 0;   // no paving at all: the block has no floor to hole

            var box = pieces[0].Box;
            for (int i = 1; i < pieces.Count; i++) box.Encapsulate(pieces[i].Box);

            // one ring of margin all round, so the flood always has an outside to start in
            int i0 = Mathf.FloorToInt((box.min.x - x0) / Cell) - 1;
            int i1 = Mathf.CeilToInt((box.max.x - x0) / Cell) + 1;
            int j0 = Mathf.FloorToInt((box.min.z - z0) / Cell) - 1;
            int j1 = Mathf.CeilToInt((box.max.z - z0) / Cell) + 1;
            int nx = i1 - i0, nz = j1 - j0;
            if (nx < 3 || nz < 3 || (long)nx * nz > 40000L) return 0;

            var covered = new bool[nx, nz];
            foreach (var piece in pieces)
            {
                int ai = Mathf.Max(0, Mathf.FloorToInt((piece.Box.min.x - x0) / Cell) - i0);
                int bi = Mathf.Min(nx - 1, Mathf.FloorToInt((piece.Box.max.x - x0) / Cell) - i0);
                int aj = Mathf.Max(0, Mathf.FloorToInt((piece.Box.min.z - z0) / Cell) - j0);
                int bj = Mathf.Min(nz - 1, Mathf.FloorToInt((piece.Box.max.z - z0) / Cell) - j0);
                for (int i = ai; i <= bi; i++)
                    for (int j = aj; j <= bj; j++)
                    {
                        if (covered[i, j]) continue;
                        float cx = x0 + (i0 + i) * Cell, cz = z0 + (j0 + j) * Cell;
                        float ox = Overlap(piece.Box.min.x, piece.Box.max.x, cx, cx + Cell);
                        float oz = Overlap(piece.Box.min.z, piece.Box.max.z, cz, cz + Cell);
                        if (ox * oz >= Covers * Cell * Cell) covered[i, j] = true;
                    }
            }

            var outside = Outside(covered, nx, nz);

            var holes = new List<Vector3>();
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                    if (!covered[i, j] && !outside[i, j])
                        holes.Add(new Vector3(x0 + (i0 + i) * Cell + Cell * 0.5f, 0f,
                                              z0 + (j0 + j) * Cell + Cell * 0.5f));
            if (holes.Count == 0) return 0;

            // the block's own paving by preference; the pack's plain square when it has
            // none of its own, stood loose for a moment so it can be measured
            GameObject borrowed = null;
            var tile = plain;
            if (tile == null)
            {
                var pack = AssetDatabase.LoadAssetAtPath<GameObject>(FillTile);
                if (pack == null)
                {
                    Debug.LogWarning("[CoreTray] " + holes.Count + " hole(s) in the floor and no " +
                                     "pavement to patch them with - " + FillTile + " is missing.");
                    return 0;
                }
                borrowed = (GameObject)PrefabUtility.InstantiatePrefab(pack);
                tile = borrowed;
            }

            int laid = 0;
            try
            {
                if (!TryBox(tile, out var tileBox)) return 0;
                // these tiles pivot at a CORNER, so what a cell centre has to be given is
                // the pivot's own offset from the middle of the tile it draws
                var offset = tile.transform.position - tileBox.center;
                foreach (var hole in holes)
                {
                    var at = new Vector3(hole.x + offset.x - centre.x,
                                         tile.transform.position.y,
                                         hole.z + offset.z - centre.z);
                    Restand(tile, root, at, out _).name = PatchName;
                    laid++;
                }
            }
            finally
            {
                if (borrowed) Object.DestroyImmediate(borrowed);
            }

            return laid;
        }

        /// <summary>Which uncovered cells the open ground can be walked to from the edge of
        /// the grid. Whatever it cannot reach is enclosed, and therefore a hole.</summary>
        static bool[,] Outside(bool[,] covered, int nx, int nz)
        {
            var outside = new bool[nx, nz];
            var stack = new Stack<Vector2Int>();

            for (int i = 0; i < nx; i++) { Wet(stack, outside, covered, nx, nz, i, 0);
                                           Wet(stack, outside, covered, nx, nz, i, nz - 1); }
            for (int j = 0; j < nz; j++) { Wet(stack, outside, covered, nx, nz, 0, j);
                                           Wet(stack, outside, covered, nx, nz, nx - 1, j); }

            while (stack.Count > 0)
            {
                var c = stack.Pop();
                Wet(stack, outside, covered, nx, nz, c.x + 1, c.y);
                Wet(stack, outside, covered, nx, nz, c.x - 1, c.y);
                Wet(stack, outside, covered, nx, nz, c.x, c.y + 1);
                Wet(stack, outside, covered, nx, nz, c.x, c.y - 1);
            }
            return outside;
        }

        static void Wet(Stack<Vector2Int> stack, bool[,] outside, bool[,] covered,
                        int nx, int nz, int i, int j)
        {
            if (i < 0 || j < 0 || i >= nx || j >= nz) return;
            if (outside[i, j] || covered[i, j]) return;
            outside[i, j] = true;
            stack.Push(new Vector2Int(i, j));
        }

        static float Overlap(float aMin, float aMax, float bMin, float bMax) =>
            Mathf.Max(0f, Mathf.Min(aMax, bMax) - Mathf.Max(aMin, bMin));

        // ------------------------------------------------------------------ the sweep

        /// <summary>
        /// Everything a tray could claim, measured once for all trays.
        ///
        /// Two passes, because the two gestures mean different things. What has been
        /// dragged ONTO A TRAY is taken whole, exactly as it hangs there - a group stays a
        /// group, and a thing with no renderer at all still counts. What merely STANDS
        /// somewhere is taken piece by piece, each prefab instance one piece, and offered
        /// to whichever rectangle its middle falls inside.
        /// </summary>
        static List<Piece> Sweep(Scene scene, Transform trays)
        {
            var found = new List<Piece>();
            var seen = new HashSet<GameObject>();

            foreach (Transform tray in trays)
                foreach (Transform child in tray)
                {
                    Mark(child, seen);
                    if (child.name == PadName || child.name == LabelName) continue;
                    Bag(child, tray, found);
                }

            // the rectangles, for the stock rule below
            var counters = new List<Rect>();
            if (trays)
                foreach (Transform tray in trays)
                    if (TryRect(tray, out var counter)) counters.Add(counter);

            foreach (var root in scene.GetRootGameObjects())
            {
                // the review row is what has already been baked, the map is paint, and the
                // industrial candidates are a question nobody has answered yet - none of
                // the three is a block waiting to be claimed by a rectangle
                if (root.name == ReviewRoot || root.name == MapRoot ||
                    root.name == IndustrialBlockForge.CandidatesRoot) continue;

                // the bare library and the row of loose buildings beside it are STOCK, and
                // stock is invisible until it is put on the counter. A block of it slid
                // onto a rectangle is being used and is swept like anything else; the rest
                // of the row is not there at all - which is what stops a harvest from
                // filing the whole library back over the very prefabs it was made from,
                // while it stands within arm's reach by design
                bool stock = root.name == BareRoot || root.name == CoreBuildingBlocks.StockRoot;
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    var go = PrefabUtility.GetNearestPrefabInstanceRoot(r.gameObject);
                    if (go == null) go = r.gameObject;
                    // EVERY instance is its own piece, nested or not. It is tempting to let a
                    // nested one travel with its parent, and this demo is why that is wrong:
                    // it parents rooftop ductwork to other rooftop ductwork, chaining runs
                    // across the whole city, so one "piece" measured 142 x 88 m with its foot
                    // seventy metres from its own middle. Whichever block that foot landed on
                    // was handed the ducting of four blocks. Taking each instance separately
                    // costs nothing - a copy is made from its own source prefab, so scene
                    // parenting is flattened rather than duplicated.
                    if (!seen.Add(go)) continue;
                    if (Reads(go)) continue;
                    if (!TryBox(go, out var box)) continue;
                    // the painted skyline and the skydome would swallow every rectangle
                    if (box.size.x > 400f || box.size.z > 400f) continue;
                    if (stock && !counters.Any(counter => Holds(counter, box.center))) continue;

                    found.Add(new Piece { Go = go, Box = box, Owner = null });
                }
            }

            return found;
        }

        /// <summary>
        /// One thing standing on a tray, as PIECES.
        ///
        /// A prefab instance is a piece, whole, whatever is nested inside it - that is what
        /// a piece is. A plain GameObject holding other things is not a piece, it is a BAG:
        /// the generated <see cref="PavingName"/>, a block dragged over from the bare
        /// library, a group somebody made to move six things at once. Taking a bag as one
        /// object would bake it as a single deep copy and every part inside it would lose
        /// its link to the pack, so the bag is opened and what is inside is what stands on
        /// the tray. An empty plain object is a piece too - a light, a marker, whatever it
        /// is, it is not a bag.
        /// </summary>
        static void Bag(Transform thing, Transform tray, List<Piece> found)
        {
            if (Reads(thing.gameObject)) return;
            if (thing.childCount == 0 || PrefabUtility.IsAnyPrefabInstanceRoot(thing.gameObject))
            {
                found.Add(new Piece { Go = thing.gameObject, Box = BoxOf(thing.gameObject),
                                      Owner = tray });
                return;
            }
            foreach (Transform inside in thing) Bag(inside, tray, found);
        }

        /// <summary>Writing, not building: the caption over a tray and the name over every
        /// block in the bare library. It has a renderer like anything else, so without this
        /// a block slid onto a rectangle with its own name floating above it would bake the
        /// name into the prefab.</summary>
        static bool Reads(GameObject go) => go.GetComponent<TextMesh>();

        static void Mark(Transform branch, HashSet<GameObject> seen)
        {
            foreach (var t in branch.GetComponentsInChildren<Transform>(true))
                seen.Add(t.gameObject);
        }

        /// <summary>What a piece measures - or, for something that draws nothing at all,
        /// the point it stands on, so an empty group dragged onto a tray still has a
        /// middle to be claimed by.</summary>
        static Bounds BoxOf(GameObject go) =>
            TryBox(go, out var box) ? box : new Bounds(go.transform.position, Vector3.zero);

        /// <summary>
        /// What a piece measures - ITS OWN renderers, not those of instances parented onto
        /// it in the scene.
        ///
        /// That distinction is the whole reason a run of rooftop ducting came out as one
        /// object a hundred and forty-two metres across. The demo parents ductwork onto
        /// ductwork, so measuring everything underneath a piece measured half the city, put
        /// its middle seventy metres from its own foot, and handed four blocks' worth of
        /// pipe to whichever block that foot happened to land on. Each nested instance is a
        /// piece in its own right and is measured as one.
        /// </summary>
        static bool TryBox(GameObject go, out Bounds box)
        {
            box = new Bounds();
            if (!go) return false;

            bool nested = PrefabUtility.IsAnyPrefabInstanceRoot(go);
            bool any = false;
            foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                if (nested && PrefabUtility.GetNearestPrefabInstanceRoot(renderer.gameObject) != go)
                    continue;
                if (!any) { box = renderer.bounds; any = true; }
                else box.Encapsulate(renderer.bounds);
            }
            return any;
        }

        // --------------------------------------------------------------- the rectangle

        /// <summary>The tray's rectangle, read off the painted plane rather than off a
        /// field, so it can be moved and scaled with the ordinary tools.</summary>
        static bool TryRect(Transform tray, out Rect rect)
        {
            rect = default;
            var pad = tray.Find(PadName);
            if (!pad) return false;

            var scale = pad.lossyScale;
            float width = Mathf.Abs(scale.x) * 10f, depth = Mathf.Abs(scale.z) * 10f;
            rect = new Rect(pad.position.x - width * 0.5f, pad.position.z - depth * 0.5f, width, depth);
            return true;
        }

        static bool Holds(Rect rect, Vector3 point) =>
            point.x >= rect.xMin - Slack && point.x <= rect.xMax + Slack &&
            point.z >= rect.yMin - Slack && point.z <= rect.yMax + Slack;

        static float Snap(float v) => Mathf.Round(v / Cell) * Cell;

        static float Up(float v) => Mathf.Max(Cell, Mathf.Ceil(v / Cell) * Cell);

        /// <summary>Makes whatever folder <see cref="OutDir"/> names, one level at a time,
        /// so moving the output somewhere else is a one-line change and not a hunt through
        /// hard-coded CreateFolder calls.</summary>
        static void EnsureFolder(string dir)
        {
            var parts = dir.Split('/');
            var path = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                if (!AssetDatabase.IsValidFolder(path + "/" + parts[i]))
                    AssetDatabase.CreateFolder(path, parts[i]);
                path += "/" + parts[i];
            }
        }
    }
}
