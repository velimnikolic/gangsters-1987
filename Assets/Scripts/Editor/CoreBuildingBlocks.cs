using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// The catalog's own buildings, brought into the city core.
    ///
    /// Two jobs, and they are the same job at two sizes.
    ///
    /// A building big enough to BE a block - the warehouse yard, the police station, the
    /// nightclub - is stood on a tray on its own, the pavement is grown round it and it is
    /// baked into <see cref="CoreBlockTray.OutDir"/> as a block the city deals like any
    /// other (<see cref="RoadDemo.CoreLayout.Blocks"/>). Nothing is composed and nothing is
    /// invented: the pavement is all it is given, which is all that was asked for.
    ///
    /// A building smaller than that - the diner, the parking garages, the skatepark, the
    /// coffee shop - is STOCK. It stands in a row beside the trays, to be dragged onto one
    /// and combined with others into a block by hand, the same gesture the bare library is
    /// used with (<see cref="CoreBlockTray.ShowBare"/>).
    ///
    /// Either way the building is first COPIED into the kit's own Buildings folder under
    /// the kit's name, and blocks are baked against the copy. Two reasons, and both of them
    /// bite:
    ///
    ///  * Assets/CityKit/Catalog is rebuilt by DELETING it whole
    ///    (<see cref="SyntyBuildingCatalog"/>), so every guid in it changes on the next
    ///    catalog build. A block prefab pointing in there would be left pointing at ghosts,
    ///    and a core block is hand-made and never re-baked - it would not be noticed until
    ///    the city came up full of holes. Buildings is never deleted.
    ///  * a name that starts "building-" is what the block machinery reads as a building -
    ///    <see cref="RoadDemo.CoreLayout.Measure"/> when it measures a block, and the tray
    ///    when it decides what the pavement goes round. A catalog bake called "Diner" is
    ///    invisible to both.
    /// </summary>
    public static class CoreBuildingBlocks
    {
        const string CatalogDir = SyntyKitExtractor.KitDir + "/Catalog";
        const string BuildingsDir = SyntyKitExtractor.BuildingsDir;
        const string BuildingsMeshDir = BuildingsDir + "/Meshes";

        /// <summary>The row the stock stands in. Kept out of every sweep as firmly as the
        /// bare library, and for the same reason: it stands within arm's reach of a
        /// rectangle by design, and nothing in it is a block waiting to be baked.</summary>
        internal const string StockRoot = "CORE BUILDINGS (stock)";

        const float Cell = 5f;

        /// <summary>Walking room between two things standing in a row.</summary>
        const float Gap = 30f;

        /// <summary>How many buildings stand in one row of the stock before it wraps.</summary>
        const int RowOf = 6;

        // ----------------------------------------------------------------- the copies

        /// <summary>A catalog bake, and the name its copy takes in the kit.</summary>
        readonly struct Copy
        {
            public readonly string From, To;
            public Copy(string from, string to) { From = from; To = to; }
        }

        /// <summary>What the core was asked for (2026-08-26) and what each copy is called.
        /// The coffee shop is not here because the kit already owns it - the catalog only
        /// ever showed it as a reference to <see cref="BuildingsDir"/>.</summary>
        static readonly Copy[] Wanted =
        {
            new Copy("Diner", "building-diner"),
            new Copy("Restaurant_02", "building-restaurant-02"),
            new Copy("ParkingGarage_A", "building-parking-garage-a"),
            new Copy("ParkingGarage_B", "building-parking-garage-b"),
            new Copy("Skatepark", "building-skatepark"),
            // the nightclub is a block of its own below, and a block's pieces have to come
            // from a folder that outlives the next catalog build like every other piece
            new Copy("NightClub", "building-nightclub"),
        };

        /// <summary>The stock row, in the order it stands. The nightclub is left out: it is
        /// a block, not an ingredient.</summary>
        static readonly string[] Stock =
        {
            "building-diner",
            "building-restaurant-02",
            "building-parking-garage-a",
            "building-parking-garage-b",
            "building-skatepark",
            "building-coffeeshop",
        };

        /// <summary>A building big enough to be a block on its own, and the block it
        /// becomes.</summary>
        readonly struct Recipe
        {
            public readonly string Block, Building;

            /// <summary>The building arrives as a finished yard - see
            /// <see cref="RoadDemo.CoreTray.ownGround"/>.</summary>
            public readonly bool OwnGround;

            public Recipe(string block, string building, bool ownGround = false)
            {
                Block = block;
                Building = building;
                OwnGround = ownGround;
            }
        }

        static readonly Recipe[] Recipes =
        {
            // the warehouse is a walled yard the pack drew whole, gate and markings and
            // all: the pavement rings it and keeps out
            new Recipe("warehouse-block", "building-warehouse", ownGround: true),
            new Recipe("police-station-block", "building-policestation"),
            new Recipe("nightclub-block", "building-nightclub"),
        };

        /// <summary>
        /// Where a vehicle way enters a walled yard, in the building's OWN metres: the
        /// centre of the gate on the wall, and how wide it opens.
        ///
        /// Declared, not read, and it has to be: a shut gate is a wall. The warehouse's
        /// stands in an unbroken run of fence, and every test a mesh can be put to - how
        /// deep it goes, how tall it stands, whether anything is over it - says wall.
        ///
        /// So it was measured off the model instead (2026-08-26): the occupancy of
        /// everything standing 0.5-4 m, on a 1 m grid, has the yard's own concrete road
        /// running north between the sheds and the open asphalt, and the fence closing
        /// across it at z +28.5. The road is x 0..+8 there, which is what these numbers
        /// are. The 2 m opening a little east of it is a doorway, not a gate.
        ///
        /// A building with no entry here simply has none, and the pavement rings it whole.
        /// </summary>
        static readonly Dictionary<string, (Vector3 At, float Wide)> Gates =
            new Dictionary<string, (Vector3, float)>
            {
                ["building-warehouse"] = (new Vector3(4f, 0f, 28.5f), 8f),
            };

        /// <summary>
        /// The gate on one piece standing on a tray, in world metres, or false if that
        /// building has none.
        ///
        /// Keyed off the SOURCE prefab's name, the same identity the rest of the block
        /// machinery reads, so a tray somebody furnished by hand gets the gate the forge
        /// would have given it.
        /// </summary>
        internal static bool GateOf(GameObject piece, out Bounds gate)
        {
            gate = new Bounds();
            if (piece == null) return false;

            var source = PrefabUtility.GetCorrespondingObjectFromSource(piece);
            string name = source != null ? source.name : piece.name;
            if (!Gates.TryGetValue(name, out var told)) return false;

            var at = piece.transform.TransformPoint(told.At);
            var out1 = piece.transform.TransformVector(Vector3.right) * told.Wide;
            var thick = piece.transform.TransformVector(Vector3.forward) * Cell;
            gate = new Bounds(at, new Vector3(Mathf.Max(Mathf.Abs(out1.x), Mathf.Abs(thick.x)),
                                              1f,
                                              Mathf.Max(Mathf.Abs(out1.z), Mathf.Abs(thick.z))));
            return true;
        }

        /// <summary>
        /// Copies every catalog building the core wants into the kit, and says what it did.
        ///
        /// A copy that is already there is LEFT ALONE, whatever the catalog now says. It
        /// may have been edited since, and any block baked against it points at it by guid;
        /// silently writing a fresh bake over it would change blocks nobody asked to change.
        /// </summary>
        public static object[] CopyBuildings()
        {
            var told = new List<object>();
            foreach (var one in Wanted)
            {
                string path = CopyOne(one, out bool wrote);
                told.Add(new
                {
                    from = $"{CatalogDir}/{one.From}.prefab",
                    name = one.To,
                    path,
                    wrote,
                    trouble = path == null ? "the catalog has no such building" : null,
                });
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return told.ToArray();
        }

        /// <summary>One building copied: the prefab, the mesh it draws, and the names of
        /// both. The mesh is copied and not merely referenced for the same reason the
        /// prefab is - it lives in Catalog/Meshes and goes when the folder goes.</summary>
        static string CopyOne(Copy one, out bool wrote)
        {
            wrote = false;
            string to = $"{BuildingsDir}/{one.To}.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(to) != null) return to;

            string from = $"{CatalogDir}/{one.From}.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(from) == null)
            {
                Debug.LogWarning($"[CoreBuildings] {from} is missing - build the catalog " +
                                 "(Tools/City/Catalog/Build The Building Catalog) and run this again.");
                return null;
            }

            Folder(BuildingsMeshDir);
            if (!AssetDatabase.CopyAsset(from, to))
            {
                Debug.LogWarning($"[CoreBuildings] {from} could not be copied to {to}.");
                return null;
            }

            var contents = PrefabUtility.LoadPrefabContents(to);
            try
            {
                // the ROOT's name is the one the block machinery reads off an instance
                // (PrefabUtility.GetCorrespondingObjectFromSource().name), so renaming the
                // file alone would leave the copy as invisible as the original
                contents.name = one.To;
                int nth = 0;
                foreach (var filter in contents.GetComponentsInChildren<MeshFilter>(true))
                    filter.sharedMesh = Mine(filter.sharedMesh, one.To, ref nth);
                PrefabUtility.SaveAsPrefabAsset(contents, to);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }

            wrote = true;
            return to;
        }

        /// <summary>The kit's own copy of a mesh the catalog owns: the same geometry under
        /// a path and a name of its own, so the next catalog rebuild cannot take it away.
        /// A mesh from anywhere else is handed back untouched - it is not the catalog's to
        /// lose, and copying it would only be a second megabyte of the same thing.</summary>
        static Mesh Mine(Mesh mesh, string name, ref int nth)
        {
            if (mesh == null) return null;
            string path = AssetDatabase.GetAssetPath(mesh);
            if (string.IsNullOrEmpty(path) || !path.StartsWith(CatalogDir + "/")) return mesh;

            string label = nth == 0 ? name : $"{name}-{nth + 1}";
            nth++;

            string to = $"{BuildingsMeshDir}/{label}.asset";
            if (AssetDatabase.LoadAssetAtPath<Mesh>(to) == null && !AssetDatabase.CopyAsset(path, to))
            {
                Debug.LogWarning($"[CoreBuildings] {path} could not be copied to {to}; " +
                                 $"{name} still draws the catalog's mesh and will lose it on " +
                                 "the next catalog build.");
                return mesh;
            }

            var copy = AssetDatabase.LoadAssetAtPath<Mesh>(to);
            if (copy == null) return mesh;
            if (copy.name != label) { copy.name = label; EditorUtility.SetDirty(copy); }
            return copy;
        }

        // ------------------------------------------------------------------ the blocks

        /// <summary>
        /// Bakes the blocks that are one building each.
        ///
        /// It writes no prefab itself. Each building is stood on a tray of its own and
        /// <see cref="CoreBlockTray"/> does the rest, so a block made this way goes through
        /// exactly the same door as a harvested one: the same pavement round the buildings,
        /// the same pivot on the 5 m beat, the same BlockLotTag, the same folder. The trays
        /// are taken down afterwards - one left standing takes in whatever is dragged near
        /// it next.
        ///
        /// A block already on disk is left alone unless <paramref name="force"/> says
        /// otherwise: it may have been paved by hand since, and this would pave it again.
        /// </summary>
        public static object[] BakeBlocks(bool force) =>
            IndustrialBlockForge.InTheLab(scene => BakeIn(scene, force));

        static object[] BakeIn(Scene scene, bool force)
        {
            CopyBuildings();

            var told = new List<Told>();
            var trays = new List<Transform>();
            float x = 0f;

            foreach (var recipe in Recipes)
            {
                var one = new Told
                {
                    Block = recipe.Block,
                    Building = recipe.Building,
                    Path = $"{CoreBlockTray.OutDir}/{recipe.Block}.prefab",
                };
                told.Add(one);

                string building = $"{BuildingsDir}/{recipe.Building}.prefab";
                if (!force && AssetDatabase.LoadAssetAtPath<GameObject>(one.Path) != null)
                {
                    one.Trouble = "a block of that name is on disk already; --force bakes it again";
                    continue;
                }

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(building);
                if (prefab == null)
                {
                    one.Trouble = $"{building} is missing";
                    continue;
                }

                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                if (!Box(go, out var box))
                {
                    Object.DestroyImmediate(go);
                    one.Trouble = $"{recipe.Building} draws nothing";
                    continue;
                }

                // the building is stood on the module the pavement is laid on: a wall that
                // falls half across a cell leaves the kerb a different distance out on the
                // two sides of the same building. The box is measured from the pivot, so
                // this is where the pivot goes to put the FOOTPRINT on the beat
                float half = Up(box.size.x) * 0.5f;
                var centre = new Vector3(Snap(x + half), 0f, 0f);
                go.transform.position = new Vector3(centre.x - box.center.x, 0f, centre.z - box.center.z);

                var tray = CoreBlockTray.MakeTray(scene, recipe.Block, centre,
                                                  Up(box.size.x) + 4f * Cell,
                                                  Up(box.size.z) + 4f * Cell);
                var panel = tray.GetComponent<RoadDemo.CoreTray>();
                if (panel) panel.ownGround = recipe.OwnGround;
                go.transform.SetParent(tray, true);

                trays.Add(tray);
                one.Tried = true;
                x = centre.x + half + Gap;
            }

            int written = CoreBlockTray.BakeQuietly(scene, out var said);
            Debug.Log($"[CoreBuildings] {written} block prefab(s) written to " +
                      CoreBlockTray.OutDir + ": " + string.Join("; ", said));

            // the trays have done their work and the bake has emptied them; left standing,
            // they would take in whatever is dragged near them next
            foreach (var tray in trays)
                if (tray) Undo.DestroyObjectImmediate(tray.gameObject);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!string.IsNullOrEmpty(scene.path)) EditorSceneManager.SaveScene(scene);

            // what is ON DISK when it is over is the answer, not what was attempted
            return told.Select(one => (object)new
            {
                block = one.Block,
                building = one.Building,
                path = one.Path,
                tried = one.Tried,
                wrote = AssetDatabase.LoadAssetAtPath<GameObject>(one.Path) != null,
                trouble = one.Trouble,
            }).ToArray();
        }

        /// <summary>What became of one recipe, while it is still becoming it.</summary>
        sealed class Told
        {
            public string Block, Building, Path, Trouble;
            public bool Tried;
        }

        // ------------------------------------------------------------------ the stock

        /// <summary>
        /// Stands the buildings a core block is composed from in a row beside the trays,
        /// ready to be dragged onto one.
        ///
        /// Beside them, not out behind the city, for the reason the bare library is there
        /// too: what you drag from and what you drag onto want to be in the same view. And
        /// north of them, because the trays march WESTWARD as they are added and a row laid
        /// that way would be standing where the next rectangle wants to go.
        ///
        /// Nothing is written to disk and nothing on disk is touched - this is a row of
        /// instances, and Hide The Building Stock clears it.
        /// </summary>
        [MenuItem("Tools/City/Core/Buildings/Show The Building Stock", priority = 44)]
        public static void ShowStock()
        {
            var scene = SceneManager.GetActiveScene();
            int stood = StockRow(scene, out var row);
            if (stood == 0)
            {
                EditorUtility.DisplayDialog("Show The Building Stock",
                    "None of the core's buildings are in the kit yet.\n\n" +
                    "Run Tools/City/Core/Buildings/Copy The Catalog Buildings Into The Kit " +
                    "first - it copies them out of the catalog under the kit's own names.", "OK");
                return;
            }

            var view = SceneView.lastActiveSceneView;
            if (view) view.Frame(row, false);
            Debug.Log($"[CoreBuildings] {stood} building(s) standing under \"{StockRoot}\". " +
                      "Drag them onto a tray IN THE HIERARCHY and save the scene: the pavement " +
                      "is grown round whatever they add up to and the block is baked to " +
                      CoreBlockTray.OutDir + ". The row itself is kept out of every sweep, so " +
                      "nothing left standing in it can be baked by accident.");
        }

        /// <summary>Stands the row and says how many are in it, with nothing to click.
        /// <see cref="ShowStock"/> is this with a dialog and a camera move on top, which is
        /// right for a menu item and fatal from a pipeline command - a modal stops the
        /// editor's main thread dead waiting for a hand that is not there.</summary>
        internal static int StandStock(Scene scene) => StockRow(scene, out _);

        [MenuItem("Tools/City/Core/Buildings/Hide The Building Stock", priority = 45)]
        public static void HideStock()
        {
            var scene = SceneManager.GetActiveScene();
            int cleared = 0;
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == StockRoot) { Object.DestroyImmediate(root); cleared++; }

            if (cleared > 0) EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log(cleared > 0
                ? "[CoreBuildings] the stock row is gone; the prefabs in " + BuildingsDir + " are untouched."
                : "[CoreBuildings] there was no stock row standing.");
        }

        static int StockRow(Scene scene, out Bounds row)
        {
            row = new Bounds();

            // first, so the row never measures itself and marches further north every time
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == StockRoot) Object.DestroyImmediate(root);

            var buildings = new List<GameObject>();
            foreach (var name in Stock)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{BuildingsDir}/{name}.prefab");
                if (prefab == null)
                {
                    Debug.LogWarning($"[CoreBuildings] {BuildingsDir}/{name}.prefab is missing; " +
                                     "it is left out of the row.");
                    continue;
                }
                buildings.Add(prefab);
            }
            if (buildings.Count == 0) return 0;

            var rowRoot = new GameObject(StockRoot);
            SceneManager.MoveGameObjectToScene(rowRoot, scene);

            float middle = 0f, z = Snap(NorthEdge(scene) + Gap);
            var trays = Root(scene, CoreBlockTray.TraysRoot);
            if (trays && Box(trays, out var stood)) middle = Snap(stood.center.x);
            bool first = true;

            for (int i = 0; i < buildings.Count; i += RowOf)
            {
                int end = Mathf.Min(i + RowOf, buildings.Count);
                var sizes = new List<Vector2>();
                float span = -Gap, deepest = 0f;
                for (int k = i; k < end; k++)
                {
                    var size = Measure(buildings[k]);
                    sizes.Add(size);
                    span += size.x + Gap;
                    deepest = Mathf.Max(deepest, size.y);
                }

                float rowZ = Snap(z + deepest * 0.5f);
                float x = Snap(middle - span * 0.5f);
                for (int k = i; k < end; k++)
                {
                    var size = sizes[k - i];
                    var at = new Vector3(Snap(x + size.x * 0.5f), 0f, rowZ);
                    var copy = (GameObject)PrefabUtility.InstantiatePrefab(buildings[k], rowRoot.transform);
                    copy.transform.SetPositionAndRotation(at, Quaternion.identity);

                    float top = Box(copy, out var drawn) ? drawn.max.y : 0f;
                    Caption($"{buildings[k].name} label",
                            $"{buildings[k].name}\n{size.x:F0} x {size.y:F0} m",
                            new Vector3(at.x, Mathf.Max(6f, top + 4f), at.z + size.y * 0.5f + 4f),
                            rowRoot.transform);

                    var mine = new Bounds(at, new Vector3(size.x, 20f, size.y));
                    if (first) { row = mine; first = false; } else row.Encapsulate(mine);
                    x += size.x + Gap;
                }
                z = rowZ + deepest * 0.5f + Gap;
            }

            EditorSceneManager.MarkSceneDirty(scene);
            return buildings.Count;
        }

        /// <summary>Far edge of everything the scene already holds, so the row stands clear
        /// of the trays and of the bare library rather than through them.</summary>
        static float NorthEdge(Scene scene)
        {
            float edge = 0f;
            bool any = false;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == StockRoot) continue;
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    // the painted skyline and the skydome are the horizon, not ground a row
                    // has to keep clear of
                    if (r.bounds.size.x > 400f || r.bounds.size.z > 400f) continue;
                    if (!any) { edge = r.bounds.max.z; any = true; }
                    else edge = Mathf.Max(edge, r.bounds.max.z);
                }
            }
            return any ? edge : 0f;
        }

        // ------------------------------------------------------------------- the menu

        [MenuItem("Tools/City/Core/Buildings/Copy The Catalog Buildings Into The Kit", priority = 40)]
        public static void CopyFromMenu()
        {
            var told = CopyBuildings();
            foreach (var one in told) Debug.Log($"[CoreBuildings] {one}");
            EditorUtility.DisplayDialog("Copy The Catalog Buildings Into The Kit",
                $"{told.Length} building(s) looked at; see the Console for each. They are in " +
                BuildingsDir + " now, under names starting \"building-\" - which is what the " +
                "block machinery reads as a building.", "OK");
        }

        [MenuItem("Tools/City/Core/Buildings/Bake The Building Blocks", priority = 41)]
        public static void BakeFromMenu()
        {
            var told = BakeBlocks(force: false);
            foreach (var one in told) Debug.Log($"[CoreBuildings] {one}");
            EditorUtility.DisplayDialog("Bake The Building Blocks",
                $"{told.Length} block(s) looked at; see the Console for each and for what the " +
                "tray wrote. A block already on disk was left alone - delete it first to bake " +
                "it again.", "OK");
        }

        // ----------------------------------------------------------------- the measures

        static GameObject Root(Scene scene, string name) =>
            scene.GetRootGameObjects().FirstOrDefault(go => go.name == name);

        /// <summary>What a building takes on the ground, rounded up to whole cells.</summary>
        static Vector2 Measure(GameObject prefab)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                if (!Box(go, out var box)) return new Vector2(Cell, Cell);
                return new Vector2(Up(box.size.x), Up(box.size.z));
            }
            finally { Object.DestroyImmediate(go); }
        }

        static bool Box(GameObject go, out Bounds box)
        {
            box = new Bounds();
            if (!go) return false;
            bool any = false;
            foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                if (!any) { box = renderer.bounds; any = true; }
                else box.Encapsulate(renderer.bounds);
            }
            return any;
        }

        /// <summary>A caption turned so it can be READ from where anybody stands to read
        /// it - south of the row, which is where the trays are.</summary>
        static void Caption(string name, string text, Vector3 position, Transform parent)
        {
            BlockLotPads.PadLabel(name, text, position, parent);
            var caption = parent.Find(name);
            if (caption) caption.rotation = Quaternion.Euler(35f, 180f, 0f);
        }

        static float Snap(float v) => Mathf.Round(v / Cell) * Cell;

        static float Up(float v) => Mathf.Max(Cell, Mathf.Ceil(v / Cell) * Cell);

        /// <summary>Makes a folder if it is not there, one level at a time.</summary>
        static void Folder(string path)
        {
            var parts = path.Split('/');
            var walked = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                if (!AssetDatabase.IsValidFolder(walked + "/" + parts[i]))
                    AssetDatabase.CreateFolder(walked, parts[i]);
                walked += "/" + parts[i];
            }
        }
    }
}
