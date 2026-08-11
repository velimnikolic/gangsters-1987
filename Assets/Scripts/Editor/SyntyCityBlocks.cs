using UnityEditor;
using UnityEngine;

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
            if (Recipes.Length == 0)
            {
                AssetDatabase.DeleteAsset(BlocksDir);
                return;
            }

            if (!AssetDatabase.IsValidFolder(SyntyKitExtractor.KitDir))
                AssetDatabase.CreateFolder("Assets", "CityKit");
            if (!AssetDatabase.IsValidFolder(BlocksDir))
                AssetDatabase.CreateFolder(SyntyKitExtractor.KitDir, "Blocks");

            // Overwrite in place, never delete-and-recreate: SaveAsPrefabAsset onto the
            // existing path keeps the guid, so block instances already standing in the
            // showroom scene refresh instead of going missing. Only bakes whose recipe
            // was renamed or removed get dropped.
            var live = new System.Collections.Generic.HashSet<string>();
            foreach (var recipe in Recipes)
                live.Add(recipe.name);
            foreach (var found in AssetDatabase.FindAssets("t:Prefab", new[] { BlocksDir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(found);
                if (!live.Contains(System.IO.Path.GetFileNameWithoutExtension(path)))
                    AssetDatabase.DeleteAsset(path);
            }

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

        /// <summary>The baked block prefabs in recipe order, for the showroom row.</summary>
        internal static System.Collections.Generic.List<GameObject> LoadBaked()
        {
            var list = new System.Collections.Generic.List<GameObject>();
            foreach (var recipe in Recipes)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    $"{BlocksDir}/{recipe.name}.prefab");
                if (prefab)
                    list.Add(prefab);
            }
            return list;
        }
    }
}
