using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// Reads a block straight off the lot pad and writes it down for good.
    ///
    /// The catalog scene is a generated showroom: every catalog build makes it from
    /// scratch, and the lot pads are redrawn under a root that is destroyed first. So
    /// buildings dragged onto a pad live exactly until the next rebuild - which is how
    /// a composed block, and in particular a building pulled OUT of another block
    /// (City_01_B and friends, which are catalog prefabs in their own right), went
    /// missing. Nothing was saving them.
    ///
    /// Capture walks every pad, resolves whatever stands on it back to the prefab it
    /// came from - top-level instances AND buildings nested inside another block
    /// instance - and writes one JSON recipe per block into
    /// Assets/CityKit/BlockRecipes, then bakes. The recipes name their members, so
    /// they survive the guid churn of a catalog rebuild.
    ///
    /// The reverse pass, the WORKBENCH, gives every saved block a pad of its own lot
    /// size north of the lot-pad grid and stands the block back on it, ready to be
    /// edited and captured again. The catalog build runs it, so a rebuild now hands
    /// the composed blocks back instead of eating them.
    /// </summary>
    public static class BlockLotCapture
    {
        internal const string WorkbenchRoot = "BLOCK WORKBENCH";

        const float Cell = 5f;
        const float Gap = 25f;          // between two workbench pads
        const float Clearance = 60f;    // between the lot pads and the workbench
        const float RowWidth = 520f;    // how far east a workbench row runs before it wraps

        /// <summary>How far off a pad an object may sit and still count as standing on
        /// it. Pads are 25 m apart, so this cannot reach the neighbour.</summary>
        const float PadMargin = 4f;

        // ------------------------------------------------------------------ capture

        [MenuItem("Tools/City/Catalog/Capture Blocks From Lot Pads", priority = 40)]
        public static void CaptureBlocks()
        {
            if (!OpenCatalogScene())
                return;

            var pending = Collect();
            if (pending.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "Nothing to capture",
                    "No buildings or props stand on a lot pad or a workbench pad.\n\n" +
                    "Drag catalog buildings onto one of the BLOCK LOTS pads first, then " +
                    "run this again.", "OK");
                return;
            }

            // Always ask, even when every block already has a name. A capture used to
            // write down whatever it found, which meant adding ONE new block re-saved
            // the whole workbench with it; the window is where you say "just this one".
            BlockCaptureWindow.Open(pending, Write);
        }

        static void Write(List<PendingBlock> pending)
        {
            var written = new List<string>();
            foreach (var block in pending)
            {
                if (string.IsNullOrEmpty(block.name) || block.members.Count == 0)
                    continue;

                var recipe = new BlockRecipeData
                {
                    name = block.name,
                    lot = block.lot,
                    lotWidth = block.width,
                    lotDepth = block.depth,
                    members = block.members,
                    props = block.props,
                };
                BlockRecipeStore.Save(recipe);
                written.Add($"{recipe.name} (lot {recipe.lot}): " +
                            $"{recipe.members.Count} buildings, {recipe.props.Count} props");

                foreach (var lost in block.unresolved)
                    Debug.LogWarning($"[Blocks] '{block.name}': '{lost}' stands on the pad but is " +
                                     "not a prefab instance and no prefab of that name exists - " +
                                     "it is NOT in the saved block. Drag it in from the Project " +
                                     "window (or the PROPS showroom) instead of copying it.");
            }

            // Blocks with no building are not blocks; say so rather than writing them.
            foreach (var empty in pending.Where(p => p.members.Count == 0 && !string.IsNullOrEmpty(p.name)))
                Debug.LogWarning($"[Blocks] '{empty.name}' holds no catalog building - not saved. " +
                                 "A block needs at least one building; props alone are dressing.");

            if (written.Count == 0)
            {
                Debug.LogWarning("[Blocks] nothing captured.");
                return;
            }

            AssetDatabase.SaveAssets();
            SyntyCityBlocks.Bake();
            Debug.Log($"[Blocks] captured to {BlockRecipeStore.Dir}:\n  " +
                      string.Join("\n  ", written) +
                      "\n[Blocks] baked to Assets/CityKit/Blocks. Run " +
                      "Tools/City/Catalog/Restore Saved Blocks On Pads to stand them on their own pads.");
        }

        /// <summary>
        /// One pad written down under a name given from code, for a pass that composed the
        /// block itself and has nothing to ask the user (<see cref="BlockLotStock"/>). The
        /// recipe is saved but NOT baked - a batch bakes once at the end rather than once
        /// per block. Null = nothing on the pad resolved to a catalog building, which for a
        /// composed block means the compose found nothing to stand.
        /// </summary>
        internal static BlockRecipeData Capture(BlockPad pad, string name)
        {
            var area = new PendingBlock
            {
                lot = pad.code,
                width = pad.width,
                depth = pad.depth,
                centre = pad.centre,
                name = name,
                scope = pad.group,
            };
            var areas = new List<PendingBlock> { area };

            // The same two readings Collect makes: a workbench group owns its content
            // outright, a bare lot pad takes whatever the scene has standing over it.
            if (pad.group)
                Visit(pad.group, areas, byGeometry: false);
            else
                foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                {
                    if (root.name == BlockLotPads.RootName || root.name == WorkbenchRoot ||
                        root.name == SyntyPropShowroom.RootName)
                        continue;
                    Visit(root.transform, areas, byGeometry: true);
                }

            if (area.members.Count == 0)
                return null;

            var recipe = new BlockRecipeData
            {
                name = name,
                lot = area.lot,
                lotWidth = area.width,
                lotDepth = area.depth,
                members = area.members,
                props = area.props,
            };
            BlockRecipeStore.Save(recipe);

            if (area.unresolved.Count > 0)
                Debug.LogWarning($"[Blocks] '{name}': {area.unresolved.Count} object(s) on the pad " +
                                 "are not prefab instances and no prefab of their name exists, so " +
                                 $"they are NOT in the saved block: {string.Join(", ", area.unresolved.Distinct())}");
            return recipe;
        }

        /// <summary>Everything standing on a lot pad or a workbench pad, resolved back
        /// to the prefabs it came from.</summary>
        static List<PendingBlock> Collect()
        {
            var scene = SceneManager.GetActiveScene();
            var areas = new List<PendingBlock>();

            // 1. the lot pads: fresh compositions, named by the user at capture time
            var lots = scene.GetRootGameObjects().FirstOrDefault(go => go.name == BlockLotPads.RootName);
            if (lots)
                foreach (Transform pad in lots.transform)
                {
                    if (!TryReadPadName(pad.name, out var code, out var width, out var depth))
                        continue;
                    areas.Add(new PendingBlock
                    {
                        lot = code,
                        width = width,
                        depth = depth,
                        centre = new Vector3(pad.position.x, 0f, pad.position.z),
                        name = null,        // asked for in the capture window
                        scope = null,       // filled from the whole scene, by geometry
                    });
                }

            // 2. the workbench: blocks that already have a name, being edited
            var bench = scene.GetRootGameObjects().FirstOrDefault(go => go.name == WorkbenchRoot);
            if (bench)
                foreach (Transform group in bench.transform)
                {
                    if (!TryReadWorkbenchName(group.name, out var name, out var code,
                                              out var width, out var depth))
                        continue;
                    areas.Add(new PendingBlock
                    {
                        lot = code,
                        width = width,
                        depth = depth,
                        centre = new Vector3(group.position.x, 0f, group.position.z),
                        name = name,
                        scope = group,      // filled from this group only
                    });
                }

            if (areas.Count == 0)
                return new List<PendingBlock>();

            // Workbench groups own their content outright; the lot pads take whatever
            // the scene has standing over them.
            foreach (var area in areas.Where(a => a.scope))
                Visit(area.scope, new List<PendingBlock> { area }, byGeometry: false);

            var lotAreas = areas.Where(a => !a.scope).ToList();
            if (lotAreas.Count > 0)
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (root.name == BlockLotPads.RootName || root.name == WorkbenchRoot ||
                        root.name == SyntyPropShowroom.RootName)
                        continue;
                    Visit(root.transform, lotAreas, byGeometry: true);
                }

            return areas.Where(a => a.members.Count > 0 || a.props.Count > 0).ToList();
        }

        /// <summary>
        /// Depth-first over the scene, recording each prefab instance that stands on a
        /// pad. An instance that does NOT stand on one is still descended into: this is
        /// exactly the case of a building dragged out of another block, which stays a
        /// nested prefab instance under a parent standing elsewhere.
        /// </summary>
        static void Visit(Transform node, List<PendingBlock> areas, bool byGeometry)
        {
            if (!node.gameObject.activeInHierarchy)
                return;
            // pads, captions and the pad group itself carry no block content
            if (node.GetComponent<TextMesh>() || node.name == "pad" ||
                node.name.EndsWith(" label"))
                return;

            var bounds = RendererBounds(node.gameObject);
            if (bounds.HasValue)
            {
                var area = byGeometry ? AreaAt(areas, bounds.Value.center) : areas[0];
                if (area != null && PrefabUtility.IsAnyPrefabInstanceRoot(node.gameObject))
                {
                    Record(area, node);
                    return;
                }

                if (area != null)
                {
                    // Not an instance root: look for instances underneath, and only
                    // when there are none is this a loose object that has to be named
                    // some other way (props cloned into a bake lose their prefab link
                    // but keep the prefab's name).
                    var before = area.members.Count + area.props.Count;
                    foreach (Transform child in node)
                        Visit(child, areas, byGeometry);
                    if (area.members.Count + area.props.Count == before &&
                        node.GetComponent<MeshRenderer>())
                        Record(area, node);
                    return;
                }
            }

            foreach (Transform child in node)
                Visit(child, areas, byGeometry);
        }

        static void Record(PendingBlock area, Transform node)
        {
            var path = SourcePathOf(node);
            if (string.IsNullOrEmpty(path))
            {
                area.unresolved.Add(node.name);
                return;
            }

            var pos = ToBlock(node.position, area.centre);
            var yaw = Normalise(node.eulerAngles.y - 180f);

            if (IsBuilding(path))
                area.members.Add(new BlockMemberData
                {
                    prefab = System.IO.Path.GetFileNameWithoutExtension(path),
                    pos = pos,
                    yaw = yaw,
                    mirrorX = node.lossyScale.x < 0f,
                });
            else
                // Scale as well as place: a ground tile stretched over a courtyard
                // (BlockFloorFiller) is one prefab doing the work of forty, and a prop
                // written down without its scale comes back the size of one paving slab.
                area.props.Add(new BlockPropData
                {
                    path = path,
                    pos = pos,
                    yaw = yaw,
                    scale = node.lossyScale,
                });
        }

        /// <summary>
        /// The asset a scene object came from - or a prefab of the same name for a loose
        /// clone that has no link left.
        ///
        /// Walks the prefab chain outwards-in rather than jumping to the original source:
        /// the object inside whatever prefab contains it, then inside whatever contains
        /// THAT, and stops at the first asset this object is the ROOT of. That is the
        /// prefab which was actually instantiated. Walking to the original source instead
        /// passes straight through it into the MODEL for any pack whose prefabs are fbx
        /// variants - which the whole PalmCity pack is - and "Models/SM_x.fbx" is a path
        /// no recipe row can be stood back up from as a prefab.
        ///
        /// The step-at-a-time walk keeps what the original-source jump was there for: a
        /// building dragged OUT of another block is a nested instance whose first link is
        /// a CHILD of the outer block prefab, not its root, so the walk carries on and
        /// resolves it to its own catalog prefab rather than to the block.
        /// </summary>
        internal static string SourcePathOf(Transform node)
        {
            for (var source = PrefabUtility.GetCorrespondingObjectFromSource(node.gameObject);
                 source;
                 source = PrefabUtility.GetCorrespondingObjectFromSource(source))
            {
                var path = AssetDatabase.GetAssetPath(source);
                if (!string.IsNullOrEmpty(path) &&
                    AssetDatabase.LoadAssetAtPath<GameObject>(path) == source)
                    return path;
            }
            return PrefabByName(node.name);
        }

        static readonly Dictionary<string, string> NameLookup = new();

        static string PrefabByName(string name)
        {
            var clean = name.Replace("(Clone)", "").Trim();
            var bracket = clean.LastIndexOf(" (", System.StringComparison.Ordinal);
            if (bracket > 0 && clean.EndsWith(")"))
                clean = clean.Substring(0, bracket);
            if (NameLookup.TryGetValue(clean, out var cached))
                return cached;

            var found = "";
            foreach (var guid in AssetDatabase.FindAssets($"{clean} t:Prefab"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(path) == clean)
                {
                    found = path;
                    break;
                }
            }
            // a miss is not remembered: the prefab may be baked into the catalogue a
            // moment later, and a cached "" would hide it for the rest of the session
            if (found.Length > 0)
                NameLookup[clean] = found;
            return found;
        }

        internal static bool IsBuilding(string path) =>
            path.StartsWith(SyntyBuildingCatalog.CatalogDir) ||
            path.StartsWith(SyntyKitExtractor.BuildingsDir) ||
            path.StartsWith(SyntyCityBlocks.BlocksDir);

        static PendingBlock AreaAt(List<PendingBlock> areas, Vector3 point)
        {
            foreach (var area in areas)
                if (Mathf.Abs(point.x - area.centre.x) <= area.width * 0.5f + PadMargin &&
                    Mathf.Abs(point.z - area.centre.z) <= area.depth * 0.5f + PadMargin)
                    return area;
            return null;
        }

        // ---------------------------------------------------------------- workbench

        [MenuItem("Tools/City/Catalog/Restore Saved Blocks On Pads", priority = 41)]
        public static void RestoreBlocks()
        {
            if (!OpenCatalogScene())
                return;

            var count = DrawWorkbench();
            var scene = SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[Blocks] {count} saved block(s) standing on their own pads under " +
                      $"\"{WorkbenchRoot}\" in {SyntyBuildingCatalog.ScenePath}.");
        }

        /// <summary>
        /// Takes the whole workbench out of the scene. For when the composing is done
        /// and the showroom should be the showroom again - the pads, their labels and
        /// the standing blocks are scene objects only, and the recipes they were stood
        /// up from are what the blocks actually ARE.
        /// </summary>
        [MenuItem("Tools/City/Catalog/Clean Workbench", priority = 42)]
        public static void CleanWorkbench()
        {
            if (!OpenCatalogScene())
                return;

            var scene = SceneManager.GetActiveScene();
            var bench = scene.GetRootGameObjects().FirstOrDefault(go => go.name == WorkbenchRoot);
            if (!bench)
            {
                EditorUtility.DisplayDialog(
                    "Nothing to clean",
                    $"There is no \"{WorkbenchRoot}\" in {SyntyBuildingCatalog.ScenePath}.", "OK");
                return;
            }

            var blocks = 0;
            foreach (Transform child in bench.transform)
                if (TryReadWorkbenchName(child.name, out _, out _, out _, out _))
                    blocks++;

            if (!EditorUtility.DisplayDialog(
                    "Clean the workbench?",
                    $"{blocks} block(s) come off their pads.\n\n" +
                    $"The recipes in {BlockRecipeStore.Dir} and the bakes in " +
                    $"{SyntyCityBlocks.BlocksDir} are untouched - Restore Saved Blocks On " +
                    "Pads stands them back up, and a catalog rebuild draws them again by " +
                    "itself.\n\nAn edit made on a pad and not captured IS lost.",
                    "Clean", "Cancel"))
                return;

            Object.DestroyImmediate(bench);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[Blocks] workbench cleaned - {blocks} block(s) taken off their pads. " +
                      "The recipes and the bakes are untouched.");
        }

        /// <summary>Stands every saved recipe back on a pad of its lot size, north of
        /// the lot-pad grid. Safe to call twice - the old workbench is removed first.
        /// Returns how many blocks were laid out.</summary>
        internal static int DrawWorkbench()
        {
            var scene = SceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == WorkbenchRoot)
                    Object.DestroyImmediate(root);

            // The generated stock is left off: BlockLotStock rolls twenty-seven blocks at a
            // time, and a bench holding those would bury the handful somebody is actually
            // editing. They are baked and in the city all the same - re-roll them rather
            // than editing them, or capture a keeper under a name of your own.
            var recipes = BlockRecipeStore.LoadAll()
                .Where(r => !r.name.StartsWith(BlockLotStock.Prefix, System.StringComparison.Ordinal))
                .ToList();
            if (recipes.Count == 0)
                return 0;

            var bench = new GameObject(WorkbenchRoot);
            Anchor(out var x0, out var z0);
            BlockLotPads.PadLabel($"{WorkbenchRoot} label",
                                  $"{WorkbenchRoot}\n(saved blocks - edit, then capture)",
                                  new Vector3(x0, 18f, z0 - 12f), bench.transform);

            float cursorX = x0, z = z0, rowDepth = 0f;
            var placed = 0;
            foreach (var recipe in recipes)
            {
                var width = recipe.lotWidth > 1f ? recipe.lotWidth : 70f;
                var depth = recipe.lotDepth > 1f ? recipe.lotDepth : 50f;
                if (cursorX > x0 && cursorX - x0 + width > RowWidth)
                {
                    cursorX = x0;
                    z = Snap(z + rowDepth + Gap);
                    rowDepth = 0f;
                }

                var centre = new Vector3(cursorX + width * 0.5f, 0f, z + depth * 0.5f);
                Place(recipe, centre, width, depth, bench.transform);
                placed++;

                cursorX = Snap(cursorX + width + Gap);
                rowDepth = Mathf.Max(rowDepth, depth);
            }
            return placed;
        }

        /// <summary>One workbench pad: the painted lot rectangle, its caption, and the
        /// block's members and props standing on it exactly as the recipe records
        /// them.</summary>
        static void Place(BlockRecipeData recipe, Vector3 centre, float width, float depth,
                          Transform parent)
        {
            var lot = string.IsNullOrEmpty(recipe.lot) ? "?" : recipe.lot;
            var group = new GameObject($"Block {recipe.name} ({lot} {width:F0}x{depth:F0})");
            group.transform.SetParent(parent, false);
            group.transform.position = centre;

            BlockLotPads.PadPlane("pad", centre, width, depth, BlockLotPads.PaintFor(lot),
                                  group.transform);
            BlockLotPads.PadLabel("label",
                                  $"{recipe.name}\nlot {lot} - {width:F0} x {depth:F0} m",
                                  new Vector3(centre.x, 5f, centre.z + depth * 0.5f + 4f),
                                  group.transform);

            foreach (var member in recipe.members)
            {
                var prefab = LoadBuilding(member.prefab);
                if (!prefab)
                {
                    Debug.LogError($"[Blocks] '{recipe.name}': member '{member.prefab}' is in " +
                                   "neither Catalog, Buildings nor Blocks - the catalog rebuild " +
                                   "renamed or dropped it; fix the recipe.");
                    continue;
                }
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                instance.transform.SetParent(group.transform, worldPositionStays: false);
                instance.transform.SetPositionAndRotation(
                    ToWorld(member.pos, centre),
                    Quaternion.Euler(0f, Normalise(member.yaw + 180f), 0f));
                if (member.mirrorX)
                    instance.transform.localScale = new Vector3(-1f, 1f, 1f);
            }

            foreach (var prop in recipe.props)
            {
                var prefab = BlockRecipeStore.LoadProp(prop.path, out var path);
                if (!prefab)
                {
                    Debug.LogError($"[Blocks] '{recipe.name}': prop '{path}' not found.");
                    continue;
                }
                // A prefab INSTANCE, not a clone: the link is what lets the next
                // capture name it again.
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                instance.transform.SetParent(group.transform, worldPositionStays: false);
                instance.transform.SetPositionAndRotation(
                    ToWorld(prop.pos, centre),
                    Quaternion.Euler(0f, Normalise(prop.yaw + 180f), 0f));
                instance.transform.localScale = prop.Scale;
            }
        }

        internal static GameObject LoadBuilding(string name) =>
            AssetDatabase.LoadAssetAtPath<GameObject>($"{SyntyBuildingCatalog.CatalogDir}/{name}.prefab")
            ?? AssetDatabase.LoadAssetAtPath<GameObject>($"{SyntyKitExtractor.BuildingsDir}/{name}.prefab")
            ?? AssetDatabase.LoadAssetAtPath<GameObject>($"{SyntyCityBlocks.BlocksDir}/{name}.prefab");

        /// <summary>North of the lot pads, aligned to their west edge; north of the
        /// whole showroom when no pads have been drawn yet.</summary>
        static void Anchor(out float x0, out float z0)
        {
            var pads = RendererBounds(GameObject.Find(BlockLotPads.RootName));
            if (pads.HasValue)
            {
                x0 = Snap(pads.Value.min.x);
                z0 = Snap(pads.Value.max.z + Clearance);
                return;
            }

            var bounds = new Bounds();
            var first = true;
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude))
            {
                if (first) { bounds = r.bounds; first = false; }
                else bounds.Encapsulate(r.bounds);
            }
            x0 = first ? 0f : Snap(bounds.min.x);
            z0 = first ? Clearance : Snap(bounds.max.z + Clearance);
        }

        // ------------------------------------------------------------------- shared

        /// <summary>"Lot B2 (85x70)" -> B2, 85, 70. Labels and anything else fail.</summary>
        internal static bool TryReadPadName(string name, out string code, out float width,
                                            out float depth)
        {
            code = null;
            width = depth = 0f;
            var match = System.Text.RegularExpressions.Regex.Match(
                name, @"^Lot (\w+) \(([\d.]+)x([\d.]+)\)$");
            if (!match.Success)
                return false;
            code = match.Groups[1].Value;
            return Parse(match.Groups[2].Value, out width) & Parse(match.Groups[3].Value, out depth);
        }

        /// <summary>"Block residentialblock2 (A1 70x50)" -> name, lot, size.</summary>
        internal static bool TryReadWorkbenchName(string name, out string block, out string code,
                                                  out float width, out float depth)
        {
            block = code = null;
            width = depth = 0f;
            var match = System.Text.RegularExpressions.Regex.Match(
                name, @"^Block (\S+) \((\w+) ([\d.]+)x([\d.]+)\)$");
            if (!match.Success)
                return false;
            block = match.Groups[1].Value;
            code = match.Groups[2].Value;
            return Parse(match.Groups[3].Value, out width) & Parse(match.Groups[4].Value, out depth);
        }

        static bool Parse(string text, out float value) =>
            float.TryParse(text, System.Globalization.NumberStyles.Float,
                           System.Globalization.CultureInfo.InvariantCulture, out value);

        /// <summary>Scene metres to the block's own frame: the anchor is the pad centre
        /// at y = 0, turned through the showroom's 180 baseline - the same frame the
        /// hand-dictated recipes in SyntyCityBlocks use.</summary>
        static Vector3 ToBlock(Vector3 world, Vector3 centre) =>
            new(centre.x - world.x, world.y, centre.z - world.z);

        static Vector3 ToWorld(Vector3 local, Vector3 centre) =>
            new(centre.x - local.x, local.y, centre.z - local.z);

        static float Normalise(float yaw)
        {
            yaw = Mathf.Repeat(yaw, 360f);
            return yaw > 180f ? yaw - 360f : yaw;
        }

        static float Snap(float v) => Mathf.Round(v / Cell) * Cell;

        internal static Bounds? RendererBounds(GameObject go)
        {
            if (!go) return null;
            var bounds = new Bounds();
            var first = true;
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
            {
                if (first) { bounds = r.bounds; first = false; }
                else bounds.Encapsulate(r.bounds);
            }
            return first ? (Bounds?)null : bounds;
        }

        internal static bool OpenCatalogScene()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path == SyntyBuildingCatalog.ScenePath)
                return true;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return false;
            EditorSceneManager.OpenScene(SyntyBuildingCatalog.ScenePath, OpenSceneMode.Single);
            return true;
        }

        /// <summary>One pad's worth of composed block, on its way to a recipe.</summary>
        internal sealed class PendingBlock
        {
            public string lot;
            public float width, depth;
            public Vector3 centre;
            public string name;         // null on a lot pad: the window asks for it
            public Transform scope;     // set for workbench pads: their own subtree only
            public readonly List<BlockMemberData> members = new();
            public readonly List<BlockPropData> props = new();
            public readonly List<string> unresolved = new();
        }
    }

    /// <summary>
    /// Picks and names the blocks a capture found. A lot pad holds whatever was dragged
    /// onto it and cannot know what it is called; a workbench pad already carries its
    /// name and is shown for confirmation only.
    ///
    /// Picking is the point of the window. Everything standing on a pad is FOUND, but
    /// only what is ticked is written down - so adding one new block does not re-save
    /// the whole workbench along with it. New compositions start ticked, blocks that
    /// are already saved start unticked: tick one only when you edited it.
    /// </summary>
    sealed class BlockCaptureWindow : EditorWindow
    {
        List<BlockLotCapture.PendingBlock> _pending;
        System.Action<List<BlockLotCapture.PendingBlock>> _onCapture;
        string[] _names;
        bool[] _take;
        Vector2 _scroll;

        internal static void Open(List<BlockLotCapture.PendingBlock> pending,
                                  System.Action<List<BlockLotCapture.PendingBlock>> onCapture)
        {
            var window = CreateInstance<BlockCaptureWindow>();
            window.titleContent = new GUIContent("Capture blocks");
            window._pending = pending;
            window._onCapture = onCapture;
            window._names = pending.Select(p => p.name ?? Suggest(p.lot)).ToArray();
            // A lot pad carries a fresh composition and is why the capture was run;
            // a workbench pad carries a block that is already on disk.
            window._take = pending.Select(p => !p.scope).ToArray();
            window.minSize = new Vector2(460f, 200f);
            window.ShowUtility();
        }

        /// <summary>A free name for this lot: a1block1, a1block2, ... - the user
        /// usually types their own over it.</summary>
        static string Suggest(string lot)
        {
            var stem = (lot ?? "block").ToLowerInvariant() + "block";
            for (var n = 1; n < 100; n++)
                if (!BlockRecipeStore.Exists(stem + n))
                    return stem + n;
            return stem;
        }

        void OnGUI()
        {
            if (_pending == null)
            {
                Close();
                return;
            }

            EditorGUILayout.HelpBox(
                "Tick what to save; the rest is left exactly as it is on disk. An existing " +
                "name overwrites that recipe - which is how you save an edit to a block you " +
                "already captured.", MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("New only", GUILayout.Width(80)))
                for (var i = 0; i < _take.Length; i++)
                    _take[i] = !_pending[i].scope;
            if (GUILayout.Button("All", GUILayout.Width(60)))
                for (var i = 0; i < _take.Length; i++)
                    _take[i] = true;
            if (GUILayout.Button("None", GUILayout.Width(60)))
                for (var i = 0; i < _take.Length; i++)
                    _take[i] = false;
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            for (var i = 0; i < _pending.Count; i++)
            {
                var block = _pending[i];
                var where = block.scope ? "saved block" : $"lot {block.lot}";
                _take[i] = EditorGUILayout.ToggleLeft(
                    $"{where} ({block.width:F0} x {block.depth:F0} m) - " +
                    $"{block.members.Count} buildings, {block.props.Count} props" +
                    (block.unresolved.Count > 0 ? $", {block.unresolved.Count} unnamed" : ""),
                    _take[i], EditorStyles.boldLabel);

                using (new EditorGUI.DisabledScope(!_take[i]))
                {
                    _names[i] = EditorGUILayout.TextField("Block name", _names[i]);
                    if (BlockRecipeStore.Exists(BlockRecipeStore.Sanitise(_names[i])))
                        EditorGUILayout.LabelField(" ", "overwrites the saved block of that name");
                }
                EditorGUILayout.Space();
            }
            EditorGUILayout.EndScrollView();

            var taking = _take.Count(t => t);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Cancel"))
                Close();
            using (new EditorGUI.DisabledScope(taking == 0))
                if (GUILayout.Button(taking == 1 ? "Capture 1 block" : $"Capture {taking} blocks"))
                {
                    var chosen = new List<BlockLotCapture.PendingBlock>();
                    for (var i = 0; i < _pending.Count; i++)
                    {
                        if (!_take[i])
                            continue;
                        _pending[i].name = BlockRecipeStore.Sanitise(_names[i]);
                        chosen.Add(_pending[i]);
                    }
                    var onCapture = _onCapture;
                    _pending = null;
                    Close();
                    onCapture(chosen);
                }
            EditorGUILayout.EndHorizontal();
        }
    }
}
