using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using LivingCity.Data;
using LivingCity.Generation;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// Generates the static layer of the city into the OPEN SCENE, which is then saved with
    /// the city in it. This is deliberate, not a convenience:
    ///
    /// - Static batching is baked at build/scene-load time. Setting the Static flag on a
    ///   runtime-instantiated GameObject does nothing at all; runtime geometry would need
    ///   StaticBatchingUtility.Combine(). Generating in the editor lets
    ///   GameObjectUtility.SetStaticEditorFlags() batch the city normally.
    /// - No generation cost or frame spike at startup on mobile.
    /// - The layout can be inspected and hand-tweaked before shipping.
    ///
    /// Only cars, pedestrians, clouds and birds are spawned at runtime.
    /// </summary>
    public sealed class CityGeneratorWindow : EditorWindow
    {
        [MenuItem("Tools/City/Generate")]
        static void Open() => GetWindow<CityGeneratorWindow>("City Generator").Show();

        CityBuilder builder;
        Vector2 scroll;

        void OnEnable() => FindBuilder();

        /// <summary>
        /// Picks the builder that actually holds a city, then any configured one, and only then
        /// whatever is left. FindFirstObjectByType alone was a coin flip whenever the scene had a
        /// spare unconfigured "City" - and landing on the spare showed the "assign a CityConfig"
        /// warning while a perfectly good city sat in the scene.
        /// </summary>
        void FindBuilder()
        {
            if (builder) return;

            var builders = FindObjectsByType<CityBuilder>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            foreach (var candidate in builders)
                if (candidate.GeneratedRoot)
                {
                    builder = candidate;
                    return;
                }

            foreach (var candidate in builders)
                if (candidate.Config && candidate.Prefabs)
                {
                    builder = candidate;
                    return;
                }

            builder = builders.Length > 0 ? builders[0] : null;
        }

        void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);

            EditorGUILayout.LabelField("Living City", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            builder = (CityBuilder)EditorGUILayout.ObjectField("City Builder", builder, typeof(CityBuilder), true);

            if (!builder)
            {
                EditorGUILayout.HelpBox(
                    "No CityBuilder in the scene. Create one, then assign a CityConfig and a PrefabDatabase to it.",
                    MessageType.Info);

                if (GUILayout.Button("Create CityBuilder GameObject"))
                    CreateBuilder();

                EditorGUILayout.EndScrollView();
                return;
            }

            var config = builder.Config;
            var prefabs = builder.Prefabs;

            if (!config || !prefabs)
            {
                EditorGUILayout.HelpBox(
                    "Assign a CityConfig and a PrefabDatabase on the CityBuilder before generating.",
                    MessageType.Warning);
                EditorGUILayout.EndScrollView();
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Layout", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField("Grid", $"{config.gridWidth} x {config.gridHeight} cells");
            EditorGUILayout.LabelField("World size", $"{config.WorldWidth:0} x {config.WorldHeight:0} m");
            EditorGUILayout.LabelField("Seed", config.seed.ToString());
            EditorGUI.indentLevel--;

            if (config.gridWidth < CityConfig.MinGridSize || config.gridHeight < CityConfig.MinGridSize)
            {
                EditorGUILayout.HelpBox(
                    $"Grid is below {CityConfig.MinGridSize}x{CityConfig.MinGridSize}. HumanBehavior only accepts a " +
                    "destination 60-300m away, so pedestrians will have nowhere valid to walk.",
                    MessageType.Error);
            }

            if (!prefabs.ValidateRoadTiles(out var missing))
            {
                EditorGUILayout.HelpBox($"PrefabDatabase is missing road tiles:{"\n"}{missing}", MessageType.Error);
                EditorGUILayout.EndScrollView();
                return;
            }

            EditorGUILayout.Space();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Generate", GUILayout.Height(30)))
                    Generate();

                if (GUILayout.Button("Clear", GUILayout.Height(30)))
                    Clear();
            }

            if (GUILayout.Button("Randomise Seed and Generate"))
            {
                Undo.RecordObject(config, "Randomise Seed");
                config.seed = Random.Range(0, int.MaxValue);
                EditorUtility.SetDirty(config);
                Generate();
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Generation writes into the open scene. Save the scene afterwards - that saved " +
                "hierarchy is what makes static batching work and keeps startup free of a generation spike.",
                MessageType.None);

            EditorGUILayout.EndScrollView();
        }

        void CreateBuilder()
        {
            var go = new GameObject("City");
            builder = go.AddComponent<CityBuilder>();
            Undo.RegisterCreatedObjectUndo(go, "Create CityBuilder");
            Selection.activeGameObject = go;
            MarkSceneDirty();
        }

        void Generate()
        {
            var group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Generate City");

            var root = builder.Build(CityEditorUtils.Spawn);
            if (root)
            {
                CityEditorUtils.MarkStaticForBatching(root);
                Undo.RegisterCreatedObjectUndo(root.gameObject, "Generate City");
            }

            Undo.CollapseUndoOperations(group);
            MarkSceneDirty();
        }

        void Clear()
        {
            // Through the builder, same as every other route, and deliberately NOT through
            // Undo.DestroyObjectImmediate: the lamp rig's preview lights inside the hierarchy
            // are HideFlags.DontSave, which the undo system cannot record - tearing the city
            // down through undo left them orphaned in mid-air where the lamps used to be.
            // A cleared city is regenerated in seconds; it does not need to be undoable.
            var destroyed = builder.Clear();
            if (destroyed > 0)
            {
                MarkSceneDirty();
                Debug.Log($"[CityGenerator] Cleared {destroyed} generated " +
                          $"{(destroyed == 1 ? "root" : "roots")} under '{builder.name}'.", builder);
            }
            else
            {
                // Dumps what IS under the builder. "Nothing to clear" on its own reads as a broken
                // button; the child list says immediately whether this is the wrong CityBuilder or
                // a root under a name nothing recognises.
                Debug.LogWarning($"[CityGenerator] '{builder.name}' has no generated city to clear. " +
                                 $"Its children: {DescribeChildren(builder.transform)}. " +
                                 "If a city is visible in the scene it belongs to a different CityBuilder - " +
                                 "use Tools/City/Clear Generated City, which clears every one of them.", builder);
            }
        }

        static string DescribeChildren(Transform parent)
        {
            if (parent.childCount == 0) return "(none)";

            var names = new string[parent.childCount];
            for (var i = 0; i < parent.childCount; i++)
                names[i] = $"'{parent.GetChild(i).name}'";
            return string.Join(", ", names);
        }

        static void MarkSceneDirty()
        {
            if (!Application.isPlaying)
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }
    }
}
