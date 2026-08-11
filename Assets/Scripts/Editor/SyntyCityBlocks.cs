using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// City blocks the user composed by hand: groups of catalog buildings arranged in the
    /// showroom scene, dictated block by block, and transcribed here as recipes. The scene
    /// arrangement itself is EPHEMERAL - the catalog rebuild overwrites
    /// Assets/BuildingCatalog.unity and regenerates Assets/CityKit/Catalog with fresh
    /// guids - so this table is the only durable record, and members are named, never
    /// guid-referenced. Same contract as SyntyBuildingCatalog.ManualJoins: the user reads
    /// the scene, says the thing, the decision lands in code.
    ///
    /// Offsets are in the block's own frame: the anchor sits at the footprint centre on
    /// y = 0 (the BakeCluster convention), yaw relative to the member's authored facing.
    /// The showroom places every instance at yaw 180, so a transcription from the scene
    /// subtracts that baseline from both positions and yaws.
    ///
    /// Bakes land in Assets/CityKit/Blocks - OUTSIDE Catalog, which the catalog build
    /// deletes wholesale. They still must re-bake after every catalog build (the nested
    /// instances bind to the current Catalog guids), so Build() calls Bake() itself;
    /// the menu item exists for recipe edits between catalog rebuilds.
    /// </summary>
    public static class SyntyCityBlocks
    {
        const string BlocksDir = SyntyKitExtractor.KitDir + "/Blocks";

        /// <summary>Auto-extracted candidates carry this prefix; the manual-recipe
        /// cleanup in Bake() must never treat them as stale recipes.</summary>
        const string PalmBlockPrefix = "PalmBlock_";

        /// <summary>Two buildings this close (m, XZ gap) share a block; the demo's
        /// streets are 10+ m wide, party-wall rows touch outright.</summary>
        const float ClusterGap = 3f;

        /// <summary>A candidate longer than this (m) splits at its widest gap - the demo's
        /// hotel strip came out 117 m in one piece, twice a usable city block.</summary>
        const float MaxSide = 90f;

        /// <summary>A lone building only makes a block when it is at least this long -
        /// below that it is a Store/Toilet-sized lot the catalog grid already shows.</summary>
        const float SingleMinSide = 28f;

        /// <summary>Props are collected inside the block's own bounds plus this rim (m).
        /// The old 5 m ring around EVERY member pulled whole park lawns in.</summary>
        const float PropRim = 3f;

        /// <summary>Hard ceiling per candidate; the overflow (farthest first) is dropped
        /// and logged - the Mansion drew 1980 clones before this existed.</summary>
        const int PropCap = 300;

        /// <summary>Street furniture and ground plates stay with the street, clouds with
        /// the sky, and grass-clump carpets with the lawn they tile - a block candidate
        /// must not drag the roadway or a whole park along.</summary>
        static readonly string[] PropDeny =
            { "Sidewalk", "Road", "Cloud", "Powerline", "Buoy", "Manhole", "Divider", "Grass_Clump" };

        sealed class BlockRecipe
        {
            public string name;
            public (string prefab, Vector3 pos, float yaw)[] members; // prefab = Catalog name
            public (string path, Vector3 pos, float yaw)[] props;     // full asset path; optional
        }

        // Courtyard dressing draws on the PalmCity demo's own habits, measured offline:
        // Hedge_04 runs at a ~2 m step, benches keep hedges/planters within 3 m, trash
        // bags never stand alone (121/121 cluster in the demo).
        const string PalmEnv = "Assets/Synty/PolygonPalmCity/Prefabs/Environment/";
        const string PalmProp = "Assets/Synty/PolygonPalmCity/Prefabs/Props/";

        /// <summary>Filled per dictation; one entry per block the user called.</summary>
        static readonly BlockRecipe[] Recipes =
        {
            // 2026-08-11, first dictation: two City clusters nested into an S-court,
            // City_05 flipped 180 against City_03 (hand-rotated 0.2 degrees off true in
            // the scene; snapped clean). Anchor = midpoint of the two cluster pivots.
            new()
            {
                name = "residentialblock1",
                members = new (string prefab, Vector3 pos, float yaw)[]
                {
                    ("City_03", new Vector3(-9.9f, 0f, -5.85f), 0f),
                    ("City_05", new Vector3(9.9f, 0f, 5.85f), 180f),
                },
                // The S-court measures x[-20,16], z[-14,2] in the block frame (sub-
                // building AABBs, offline); the alley x[-22,-15], z[6,18]; the east
                // mouth opens at x~18, z[-16,-9]. Everything below stays 1+ m off walls.
                props = new (string path, Vector3 pos, float yaw)[]
                {
                    // seating plaza mid-court: facing benches, lamp, planters, shade palm
                    (PalmProp + "SM_Prop_Bench_Seat_01", new Vector3(-3.5f, 0f, -4.6f), 180f),
                    (PalmProp + "SM_Prop_Bench_Seat_01", new Vector3(-3.5f, 0f, -9.4f), 0f),
                    (PalmProp + "SM_Prop_Street_Lamp_01", new Vector3(0.5f, 0f, -7f), 90f),
                    (PalmProp + "SM_Prop_Planter_04", new Vector3(-7f, 0f, -7f), 0f),
                    (PalmProp + "SM_Prop_Planter_03", new Vector3(0.5f, 0f, -4.6f), 0f),
                    (PalmEnv + "SM_Env_Tree_Palm_04", new Vector3(2f, 0f, -4.5f), 40f),
                    // hedge run along the south court wall (City_03 rear), door gap mid-run
                    (PalmEnv + "SM_Env_Hedge_04", new Vector3(-17f, 0f, -15.6f), 0f),
                    (PalmEnv + "SM_Env_Hedge_04", new Vector3(-15f, 0f, -15.6f), 0f),
                    (PalmEnv + "SM_Env_Hedge_04", new Vector3(-13f, 0f, -15.6f), 0f),
                    (PalmEnv + "SM_Env_Hedge_04", new Vector3(-11f, 0f, -15.6f), 0f),
                    (PalmEnv + "SM_Env_Hedge_04", new Vector3(-9f, 0f, -15.6f), 0f),
                    (PalmEnv + "SM_Env_Hedge_04", new Vector3(-7f, 0f, -15.6f), 0f),
                    (PalmEnv + "SM_Env_Hedge_04", new Vector3(1f, 0f, -15.6f), 0f),
                    (PalmEnv + "SM_Env_Hedge_04", new Vector3(3f, 0f, -15.6f), 0f),
                    (PalmEnv + "SM_Env_Hedge_04", new Vector3(5f, 0f, -15.6f), 0f),
                    (PalmEnv + "SM_Env_Hedge_04", new Vector3(7f, 0f, -15.6f), 0f),
                    (PalmEnv + "SM_Env_Hedge_04", new Vector3(9f, 0f, -15.6f), 0f),
                    (PalmEnv + "SM_Env_Hedge_04", new Vector3(11f, 0f, -15.6f), 0f),
                    // hedge run along the north court wall (City_05 rear), two stretches
                    (PalmEnv + "SM_Env_Hedge_04", new Vector3(-5f, 0f, 4.4f), 0f),
                    (PalmEnv + "SM_Env_Hedge_04", new Vector3(-3f, 0f, 4.4f), 0f),
                    (PalmEnv + "SM_Env_Hedge_04", new Vector3(-1f, 0f, 4.4f), 0f),
                    (PalmEnv + "SM_Env_Hedge_04", new Vector3(1f, 0f, 4.4f), 0f),
                    (PalmEnv + "SM_Env_Hedge_04", new Vector3(7f, 0f, 4.4f), 0f),
                    (PalmEnv + "SM_Env_Hedge_04", new Vector3(9f, 0f, 4.4f), 0f),
                    (PalmEnv + "SM_Env_Hedge_04", new Vector3(11f, 0f, 4.4f), 0f),
                    (PalmEnv + "SM_Env_Hedge_04", new Vector3(13f, 0f, 4.4f), 0f),
                    // west wall greenery
                    (PalmEnv + "SM_Env_Tree_Palm_03", new Vector3(-19.5f, 0f, -2f), 0f),
                    (PalmEnv + "SM_Env_Bush_02", new Vector3(-19f, 0f, -4.5f), 70f),
                    (PalmEnv + "SM_Env_Bush_02", new Vector3(-20f, 0f, 0.5f), 210f),
                    // more palms breaking up the court
                    (PalmEnv + "SM_Env_Tree_Palm_04", new Vector3(12.5f, 0f, 0.5f), 160f),
                    (PalmEnv + "SM_Env_Tree_Palm_Sapling_02", new Vector3(5f, 0f, -12.5f), 0f),
                    (PalmEnv + "SM_Env_Tree_Palm_Sapling_02", new Vector3(-14f, 0f, -11.5f), 250f),
                    // trash corner at the City_05 rear door; bags pile like the demo's
                    (PalmProp + "SM_Prop_Trash_Bin_03", new Vector3(-11.5f, 0f, 3.6f), 180f),
                    (PalmProp + "SM_Prop_Trash_Bag_01", new Vector3(-10.3f, 0f, 3.3f), 37f),
                    (PalmProp + "SM_Prop_Trash_Bag_01", new Vector3(-10.8f, 0f, 4.2f), 152f),
                    (PalmProp + "SM_Prop_Trash_Bag_01", new Vector3(-9.9f, 0f, 3.9f), 284f),
                    // back alley (north-west gap): second bin, more bags
                    (PalmProp + "SM_Prop_Trash_Bin_03", new Vector3(-18.3f, 0f, 8.5f), 90f),
                    (PalmProp + "SM_Prop_Trash_Bag_01", new Vector3(-17.4f, 0f, 9.2f), 200f),
                    (PalmProp + "SM_Prop_Trash_Bag_01", new Vector3(-18f, 0f, 10f), 320f),
                    // bollards closing the east mouth to cars
                    (PalmProp + "SM_Prop_Bollard_02", new Vector3(17.8f, 0f, -10.5f), 0f),
                    (PalmProp + "SM_Prop_Bollard_02", new Vector3(17.8f, 0f, -13f), 0f),
                    (PalmProp + "SM_Prop_Bollard_02", new Vector3(17.8f, 0f, -15.5f), 0f),
                    // grass clumps sprinkled over the open middle
                    (PalmEnv + "SM_Env_Grass_Clump_01", new Vector3(-16f, 0f, -7f), 15f),
                    (PalmEnv + "SM_Env_Grass_Clump_01", new Vector3(-9f, 0f, -12.8f), 130f),
                    (PalmEnv + "SM_Env_Grass_Clump_01", new Vector3(7f, 0f, -2.5f), 245f),
                    (PalmEnv + "SM_Env_Grass_Clump_01", new Vector3(10.5f, 0f, -9f), 80f),
                    (PalmEnv + "SM_Env_Grass_Clump_01", new Vector3(-4f, 0f, 1.5f), 170f),
                    (PalmEnv + "SM_Env_Grass_Clump_01", new Vector3(14f, 0f, -13.5f), 300f),
                },
            },
        };

        [MenuItem("Tools/City/Bake City Blocks", priority = 5)]
        public static void Bake()
        {
            EnsureBlocksFolder();

            // Overwrite in place, never delete-and-recreate: SaveAsPrefabAsset onto the
            // existing path keeps the guid, so block instances already standing in the
            // showroom scene refresh instead of going missing. Only bakes whose recipe
            // was renamed or removed get dropped - PalmBlock_* candidates belong to the
            // extraction pass, not to the recipe table, and stay untouched.
            var live = new HashSet<string>(Recipes.Select(r => r.name));
            foreach (var found in AssetDatabase.FindAssets("t:Prefab", new[] { BlocksDir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(found);
                var name = System.IO.Path.GetFileNameWithoutExtension(path);
                if (!live.Contains(name) && !name.StartsWith(PalmBlockPrefix))
                    AssetDatabase.DeleteAsset(path);
            }

            if (Recipes.Length == 0)
                return;

            var bakedCount = 0;
            foreach (var recipe in Recipes)
            {
                var parentGo = new GameObject(recipe.name);
                var placedBuildings = 0;
                try
                {
                    foreach (var (prefabName, pos, yaw) in recipe.members)
                    {
                        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                            $"{SyntyBuildingCatalog.CatalogDir}/{prefabName}.prefab");
                        if (!prefab)
                        {
                            // The catalog rebuild renames segments when splits or joins
                            // change - the recipe must name its dead member out loud.
                            Debug.LogError($"[Blocks] recipe '{recipe.name}': member " +
                                           $"'{prefabName}' is not in the Catalog - the " +
                                           "rebuild renamed or dropped it; fix the recipe.");
                            continue;
                        }
                        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                        instance.transform.SetParent(parentGo.transform, worldPositionStays: false);
                        instance.transform.localPosition = pos;
                        instance.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
                        placedBuildings++;
                    }

                    foreach (var (path, pos, yaw) in recipe.props ?? System.Array.Empty<(string, Vector3, float)>())
                    {
                        // Recipe rows carry bare prefab names for readability; the asset
                        // on disk always ends in .prefab (this is what silently emptied
                        // the first courtyard bake - 44 null loads).
                        var assetPath = path.EndsWith(".prefab") ? path : path + ".prefab";
                        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                        if (!prefab)
                        {
                            Debug.LogError($"[Blocks] recipe '{recipe.name}': prop '{assetPath}' not found.");
                            continue;
                        }
                        // Plain clone, not a prefab instance: props are set dressing, and
                        // a clone's colliders can be stripped outright so a click in the
                        // court still lands on a BUILDING (CatalogExplorer picks whatever
                        // collider the ray meets first).
                        var instance = Object.Instantiate(prefab, parentGo.transform);
                        instance.name = prefab.name;
                        instance.transform.localPosition = pos;
                        instance.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
                        foreach (var collider in instance.GetComponentsInChildren<Collider>(true))
                            Object.DestroyImmediate(collider);
                    }

                    // Props alone never make a block - without at least one building the
                    // recipe is broken and only the LogError above should remain of it.
                    if (placedBuildings > 0)
                    {
                        PrefabUtility.SaveAsPrefabAsset(parentGo, $"{BlocksDir}/{recipe.name}.prefab");
                        bakedCount++;
                    }
                }
                finally
                {
                    Object.DestroyImmediate(parentGo);
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[Blocks] {bakedCount}/{Recipes.Length} block recipes baked to {BlocksDir}.");
        }

        /// <summary>Baked blocks for the showroom row: manual recipes in table order,
        /// then the auto-extracted PalmBlock candidates by name.</summary>
        internal static List<GameObject> LoadBaked()
        {
            var list = new List<GameObject>();
            var seen = new HashSet<string>();
            foreach (var recipe in Recipes)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    $"{BlocksDir}/{recipe.name}.prefab");
                if (prefab && seen.Add(recipe.name))
                    list.Add(prefab);
            }
            if (AssetDatabase.IsValidFolder(BlocksDir))
                foreach (var path in AssetDatabase.FindAssets("t:Prefab", new[] { BlocksDir })
                                                  .Select(AssetDatabase.GUIDToAssetPath)
                                                  .OrderBy(p => p, System.StringComparer.Ordinal))
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab && seen.Add(prefab.name))
                        list.Add(prefab);
                }
            return list;
        }

        static void EnsureBlocksFolder()
        {
            if (!AssetDatabase.IsValidFolder(SyntyKitExtractor.KitDir))
                AssetDatabase.CreateFolder("Assets", "CityKit");
            if (!AssetDatabase.IsValidFolder(BlocksDir))
                AssetDatabase.CreateFolder(SyntyKitExtractor.KitDir, "Blocks");
        }

        /// <summary>
        /// Reassembles the PalmCity demo's own street blocks as candidate prefabs for the
        /// user's review, buildings AND the dressing between them. Union-find over the
        /// build ledger's world bounds (expanded ClusterGap) groups buildings; every
        /// SM_Prop_/SM_Env_ instance in the still-open demo scene that stands within
        /// PropReach of a member - and is not street furniture, not under a baked
        /// Building shell - is cloned in. Catalog bakes run at yaw 0 with a returned
        /// world pivot, so members placed AT pivot - anchor reproduce the demo exactly.
        ///
        /// Candidates overwrite in place (stable names by south-west order = stable
        /// guids); one that disappears from the demo gets deleted. Single-building
        /// clusters only count when they bring real dressing (5+ props) - the bare
        /// building already stands in the catalog grid.
        /// </summary>
        internal static void ExtractPalmBlocks(Scene demo)
        {
            var ledger = SyntyBuildingCatalog.PalmLedger;
            if (ledger.Count == 0)
                return;
            EnsureBlocksFolder();

            // Union-find by expanded XZ overlap.
            var parent = Enumerable.Range(0, ledger.Count).ToArray();
            int Find(int i) => parent[i] == i ? i : parent[i] = Find(parent[i]);
            for (var a = 0; a < ledger.Count; a++)
                for (var b = a + 1; b < ledger.Count; b++)
                {
                    var ba = ledger[a].bounds;
                    var bb = ledger[b].bounds;
                    var overlapX = Mathf.Min(ba.max.x, bb.max.x) - Mathf.Max(ba.min.x, bb.min.x);
                    var overlapZ = Mathf.Min(ba.max.z, bb.max.z) - Mathf.Max(ba.min.z, bb.min.z);
                    if (overlapX > -ClusterGap && overlapZ > -ClusterGap)
                        parent[Find(a)] = Find(b);
                }
            var clusters = new Dictionary<int, List<int>>();
            for (var i = 0; i < ledger.Count; i++)
            {
                if (!clusters.TryGetValue(Find(i), out var members))
                    clusters[Find(i)] = members = new List<int>();
                members.Add(i);
            }

            // Over-long chains (the 117 m hotel strip) split at their widest gap until
            // every piece fits a city block; a lone building must be block-sized itself.
            var sized = new List<List<int>>();
            foreach (var members in clusters.Values)
                foreach (var piece in SplitLong(members, ledger))
                {
                    var b = UnionBounds(piece, ledger);
                    var longSide = Mathf.Max(b.size.x, b.size.z);
                    if (piece.Count >= 2 || longSide >= SingleMinSide)
                        sized.Add(piece);
                }

            // Prop candidates off the demo scene, by instance root - then each prop
            // joins AT MOST one candidate: the one whose nearest member it is closest
            // to, and only inside that candidate's bounds + rim. The old per-member
            // 5 m ring pulled park lawns in and doubled props at seams.
            var propRoots = new List<Transform>();
            foreach (var root in demo.GetRootGameObjects())
                CollectPropRoots(root.transform, propRoots);

            var unions = sized.Select(m => UnionBounds(m, ledger)).ToList();
            var assigned = sized.Select(_ => new List<(Transform t, float dist)>()).ToList();
            foreach (var prop in propRoots)
            {
                var best = -1;
                var bestDist = float.MaxValue;
                for (var c = 0; c < sized.Count; c++)
                {
                    var u = unions[c];
                    var pos = prop.position;
                    if (pos.x < u.min.x - PropRim || pos.x > u.max.x + PropRim ||
                        pos.z < u.min.z - PropRim || pos.z > u.max.z + PropRim)
                        continue;
                    var d = sized[c].Min(i => DistanceXZ(ledger[i].bounds, pos));
                    if (d < bestDist) { bestDist = d; best = c; }
                }
                if (best >= 0)
                    assigned[best].Add((prop, bestDist));
            }

            // South-west reading order makes names stable between rebuilds.
            var order = Enumerable.Range(0, sized.Count)
                .OrderBy(c => Mathf.Round(unions[c].min.z / 10f))
                .ThenBy(c => unions[c].min.x)
                .ToList();

            var made = new List<string>();
            var summary = new System.Text.StringBuilder();
            foreach (var c in order)
            {
                var members = sized[c];
                var props = assigned[c].OrderBy(p => p.dist).Select(p => p.t).ToList();
                if (members.Count < 2 && props.Count < 5)
                    continue;
                if (props.Count > PropCap)
                {
                    summary.AppendLine($"  (dropping {props.Count - PropCap} farthest props over the {PropCap} cap)");
                    props = props.Take(PropCap).ToList();
                }

                var all = unions[c];
                var anchor = new Vector3(all.center.x, 0f, all.center.z);

                var name = $"{PalmBlockPrefix}{made.Count + 1:00}";
                var parentGo = new GameObject(name);
                try
                {
                    var placed = 0;
                    foreach (var i in members)
                    {
                        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                            $"{SyntyBuildingCatalog.CatalogDir}/{ledger[i].name}.prefab");
                        if (!prefab)
                            continue;
                        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                        instance.transform.SetParent(parentGo.transform, worldPositionStays: false);
                        instance.transform.localPosition = ledger[i].pivot - anchor;
                        placed++;
                    }
                    if (placed == 0)
                        continue;

                    foreach (var prop in props)
                    {
                        var clone = Object.Instantiate(prop.gameObject, parentGo.transform);
                        clone.name = prop.name;
                        clone.transform.position = prop.position - anchor;
                        clone.transform.rotation = prop.rotation;
                        // Set dressing only: no colliders (clicks must land on buildings)
                        // and no lights (a lamp per block would bleed the URP budget).
                        foreach (var collider in clone.GetComponentsInChildren<Collider>(true))
                            Object.DestroyImmediate(collider);
                        foreach (var light in clone.GetComponentsInChildren<Light>(true))
                            Object.DestroyImmediate(light);
                    }

                    PrefabUtility.SaveAsPrefabAsset(parentGo, $"{BlocksDir}/{name}.prefab");
                    made.Add(name);
                    summary.AppendLine($"  {name}: {string.Join(", ", members.Select(i => ledger[i].name))} + {props.Count} props");
                }
                finally
                {
                    Object.DestroyImmediate(parentGo);
                }
            }

            // A candidate the demo no longer yields is stale - manual recipes are not.
            var keep = new HashSet<string>(made);
            foreach (var found in AssetDatabase.FindAssets("t:Prefab", new[] { BlocksDir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(found);
                var stale = System.IO.Path.GetFileNameWithoutExtension(path);
                if (stale.StartsWith(PalmBlockPrefix) && !keep.Contains(stale))
                    AssetDatabase.DeleteAsset(path);
            }

            Debug.Log($"[Blocks] {made.Count} palm block candidates extracted:\n{summary}");
        }

        static Bounds UnionBounds(List<int> members,
                                  List<(string name, Vector3 pivot, Bounds bounds)> ledger)
        {
            var all = ledger[members[0]].bounds;
            foreach (var i in members)
                all.Encapsulate(ledger[i].bounds);
            return all;
        }

        /// <summary>XZ distance from a point to a bounds rect; 0 inside.</summary>
        static float DistanceXZ(Bounds b, Vector3 p)
        {
            var dx = Mathf.Max(Mathf.Max(b.min.x - p.x, p.x - b.max.x), 0f);
            var dz = Mathf.Max(Mathf.Max(b.min.z - p.z, p.z - b.max.z), 0f);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>
        /// Recursively halves a cluster whose long side exceeds MaxSide. The cut lands
        /// at the widest gap between consecutive footprints along that axis; in a
        /// continuous terrace (all gaps ~0) the tie-break pulls the cut toward the
        /// middle party wall, so halves stay balanced.
        /// </summary>
        static List<List<int>> SplitLong(List<int> members,
                                         List<(string name, Vector3 pivot, Bounds bounds)> ledger)
        {
            var all = UnionBounds(members, ledger);
            var alongX = all.size.x >= all.size.z;
            if (Mathf.Max(all.size.x, all.size.z) <= MaxSide || members.Count < 2)
                return new List<List<int>> { members };

            float Min(int i) => alongX ? ledger[i].bounds.min.x : ledger[i].bounds.min.z;
            float Max(int i) => alongX ? ledger[i].bounds.max.x : ledger[i].bounds.max.z;
            var sorted = members.OrderBy(Min).ToList();

            var bestCut = 0;
            var bestScore = float.NegativeInfinity;
            var runningMax = Max(sorted[0]);
            for (var k = 0; k < sorted.Count - 1; k++)
            {
                runningMax = Mathf.Max(runningMax, Max(sorted[k]));
                var gap = Min(sorted[k + 1]) - runningMax;
                var offCentre = Mathf.Abs((k + 1f) / sorted.Count - 0.5f);
                var score = gap - offCentre;
                if (score > bestScore) { bestScore = score; bestCut = k; }
            }

            var result = new List<List<int>>();
            result.AddRange(SplitLong(sorted.Take(bestCut + 1).ToList(), ledger));
            result.AddRange(SplitLong(sorted.Skip(bestCut + 1).ToList(), ledger));
            return result;
        }

        /// <summary>Top-most SM_Prop_/SM_Env_ instances, skipping deny-listed families
        /// and everything under a Building shell (that geometry is already baked into
        /// the building prefab - cloning it again would double it).</summary>
        static void CollectPropRoots(Transform node, List<Transform> found)
        {
            if (node.name == "Building")
                return;
            if (node.name.StartsWith("SM_Prop_") || node.name.StartsWith("SM_Env_"))
            {
                if (!PropDeny.Any(deny => node.name.Contains(deny)) &&
                    node.GetComponentInChildren<MeshRenderer>(true))
                    found.Add(node);
                return;
            }
            foreach (Transform child in node)
                CollectPropRoots(child, found);
        }
    }
}
