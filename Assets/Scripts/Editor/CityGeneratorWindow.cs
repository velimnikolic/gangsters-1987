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

        void FindBuilder()
        {
            if (!builder)
                builder = FindFirstObjectByType<CityBuilder>();
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
            var root = builder.GeneratedRoot;
            if (root)
                Undo.DestroyObjectImmediate(root.gameObject);

            MarkSceneDirty();
        }

        static void MarkSceneDirty()
        {
            if (!Application.isPlaying)
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }
    }
}
