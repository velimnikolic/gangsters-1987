using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using LivingCity.Data;
using LivingCity.Generation;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// A one-block laboratory: the smallest legal city (5x5 grid - the boundary ring road
    /// and one interior block) built into its own scene with NOTHING else - no pedestrian
    /// or vehicle spawners, no weather, no HUDs, no directors. What Play shows is exactly
    /// what the generator laid down, which is the point: when the real city misbehaves,
    /// this is where a block, a road tile or a facade can be stared at in isolation.
    ///
    /// The scene builds from its own config asset (OneBlockConfig.asset), stamped fresh on
    /// every rebuild - the real CityConfig is never touched, so seeds, counts and knobs of
    /// the actual city stay exactly as they are. debugSingleZone forces ResidentialHigh, so
    /// the block is the ordinary terrace-and-shops kind the city is mostly made of.
    /// </summary>
    public static class OneBlockDebugScene
    {
        const string ScenePath = "Assets/OneBlock.unity";
        const string ConfigPath = "Assets/Configs/OneBlockConfig.asset";

        [MenuItem("Tools/City/Build One Block Debug Scene", priority = 3)]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            // Fresh database (and the Synty kit behind it) before anything loads from it.
            CityAssetBootstrap.CreateAssets();

            var prefabs = AssetDatabase.LoadAssetAtPath<PrefabDatabase>("Assets/Configs/PrefabDatabase.asset");
            if (!prefabs)
            {
                Debug.LogError("[OneBlock] No PrefabDatabase.asset - refresh configs first.");
                return;
            }

            var config = LoadOrCreateConfig();

            // Dry-run the generator BEFORE touching any scene: if the layout cannot produce
            // streets and blocks, say so with numbers instead of saving an empty scene - the
            // first version of this fixture did exactly that, silently.
            var probe = CityGenerator.Generate(config);
            if (probe == null || probe.BlockCount <= 1)
            {
                EditorUtility.DisplayDialog("One Block build failed",
                    $"CityGenerator produced {(probe == null ? 0 : probe.BlockCount)} block(s) on a " +
                    $"{config.gridWidth}x{config.gridHeight} grid with arterial spacing " +
                    $"{config.minArterialSpacing}-{config.maxArterialSpacing}. The Console names the cause.",
                    "OK");
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // Re-load BOTH assets after the scene change: LoadOrCreateConfig ends in
            // SaveAssets, and the import that runs around NewScene recreates the config's
            // native object - the C# reference held from before comes out fake-null, and
            // wiring it produced the "config=NULL, prefabs=OK" failure this line fixes.
            config = AssetDatabase.LoadAssetAtPath<CityConfig>(ConfigPath);
            prefabs = AssetDatabase.LoadAssetAtPath<PrefabDatabase>("Assets/Configs/PrefabDatabase.asset");
            if (!config || !prefabs)
            {
                EditorUtility.DisplayDialog("One Block build failed",
                    $"Re-load after NewScene: config={(config ? "OK" : "NULL")}, " +
                    $"prefabs={(prefabs ? "OK" : "NULL")}.", "OK");
                return;
            }

            var cityObject = new GameObject("City");
            var builder = cityObject.AddComponent<CityBuilder>();
            CityEditorUtils.SetField(builder, "config", config);
            CityEditorUtils.SetField(builder, "prefabs", prefabs);
            CityEditorUtils.SetField(builder, "lotConfig",
                AssetDatabase.LoadAssetAtPath<IndustrialLotConfig>("Assets/Configs/IndustrialLotConfig.asset"));
            CityEditorUtils.SetField(builder, "parkConfig",
                AssetDatabase.LoadAssetAtPath<ParkConfig>("Assets/Configs/ParkConfig.asset"));
            CityEditorUtils.SetBool(builder, "buildOnStart", false);
            cityObject.AddComponent<PathDebugGizmos>();

            // Verify the wiring actually landed before building - Build's own guard returns
            // null silently from this fixture's point of view. If the SerializedObject route
            // did not stick (the failure the empty scene pointed at), fall back to plain
            // reflection and check again.
            if (!builder.Config || !builder.Prefabs)
            {
                var type = typeof(CityBuilder);
                const System.Reflection.BindingFlags flags =
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                type.GetField("config", flags)?.SetValue(builder, config);
                type.GetField("prefabs", flags)?.SetValue(builder, prefabs);
                Debug.LogWarning("[OneBlock] SerializedObject wiring did not stick; used reflection. " +
                                 $"After fallback: config={(builder.Config ? "OK" : "NULL")}, " +
                                 $"prefabs={(builder.Prefabs ? "OK" : "NULL")}.");
            }

            if (!builder.Config || !builder.Prefabs)
            {
                EditorUtility.DisplayDialog("One Block build failed",
                    $"Could not wire the builder: config={(builder.Config ? "OK" : "NULL")}, " +
                    $"prefabs={(builder.Prefabs ? "OK" : "NULL")}. Send the Console output.", "OK");
                return;
            }

            var root = builder.Build(CityEditorUtils.Spawn);
            if (!root || root.childCount == 0)
            {
                EditorUtility.DisplayDialog("One Block build failed",
                    $"CityBuilder.Build produced {(root ? "an empty root" : "null")} " +
                    $"(config={(builder.Config ? "OK" : "NULL")}, prefabs={(builder.Prefabs ? "OK" : "NULL")}). " +
                    "The Console's last error names the cause.", "OK");
                return;
            }

            CityEditorUtils.MarkStaticForBatching(root);

            // Traffic and pedestrians ARE wanted here - watching one car take the junction
            // and one walker cross is the fixture's whole purpose. Only these two spawners:
            // still no weather, police, school run, gangs or HUDs.
            var spawners = new GameObject("Spawners");
            var vehicles = spawners.AddComponent<Entities.VehicleSpawner>();
            CityEditorUtils.SetField(vehicles, "config", config);
            CityEditorUtils.SetField(vehicles, "prefabs", prefabs);
            var people = spawners.AddComponent<Entities.PedestrianSpawner>();
            CityEditorUtils.SetField(people, "config", config);
            CityEditorUtils.SetField(people, "prefabs", prefabs);

            FrameCamera(config);

            EditorSceneManager.SaveScene(scene, ScenePath);

            var buildings = root.Find("Buildings");
            var roads = root.Find("Roads");
            Debug.Log($"[OneBlock] Built {config.gridWidth}x{config.gridHeight} debug city into " +
                      $"{ScenePath}: {probe.BlockCount} blocks, " +
                      $"{(roads ? roads.childCount : 0)} road objects, " +
                      $"{(buildings ? buildings.childCount : 0)} building objects. " +
                      "No spawners, no weather, no HUD - the static city only.");
        }

        /// <summary>
        /// The debug fixture's own config. STAMPED on every rebuild - these values are the
        /// definition of the scene, not knobs; change them here, rebuild, done.
        /// </summary>
        static CityConfig LoadOrCreateConfig()
        {
            var config = AssetDatabase.LoadAssetAtPath<CityConfig>(ConfigPath);
            if (!config)
            {
                config = ScriptableObject.CreateInstance<CityConfig>();
                AssetDatabase.CreateAsset(config, ConfigPath);
            }

            // Streets in this generator exist ONLY as subdivision cuts (the outer ring is
            // deliberately unpaved), so "no arterials" means NO STREETS AT ALL and an empty
            // scene - the first version of this fixture proved it. The minimum city that
            // lives is therefore a 7x7 grid with the spacing pinned to 4: Subdivide's cut
            // range [minBlock, size-1-minBlock] collapses to exactly {3} on both axes, so
            // every rebuild carves the same cross of two streets - one junction, four
            // straight arms ending at the map edge - around four 3x3-cell blocks.
            config.gridWidth = 7;
            config.gridHeight = 7;
            config.minArterialSpacing = 4;
            config.maxArterialSpacing = 4;
            config.minBoulevards = 0;
            config.maxBoulevards = 0;

            // One seed, always: a debug scene that changes between rebuilds cannot be
            // compared against yesterday's screenshot.
            config.seed = 1;

            // A handful of each, so a single car and a single walker can be watched end to
            // end - the point of the whole fixture. The real city runs hundreds/thousands.
            config.carCount = 6;
            config.pedestrianCount = 12;

            // Everything ResidentialHigh - the ordinary block the city is made of - and no
            // port roll on a map with no room for one.
            config.debugSingleZone = true;
            config.portChance = 0f;

            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            return config;
        }

        /// <summary>The default camera, parked over the block at the city's own 45-degree look.</summary>
        static void FrameCamera(CityConfig config)
        {
            var camera = Camera.main;
            if (!camera)
                return;

            var centre = new Vector3(config.WorldWidth / 2f, 0f, config.WorldHeight / 2f);
            camera.transform.SetPositionAndRotation(
                centre + new Vector3(0f, 110f, -110f),
                Quaternion.Euler(45f, 0f, 0f));
            camera.farClipPlane = 600f;
        }
    }
}
