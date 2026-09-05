using RoadDemo;
using UnityEditor;
using UnityEngine;

namespace LivingCity.EditorTools
{
    public sealed class IndustrialLabWindow : EditorWindow
    {
        enum Choice { All, Works, Plant, Depot, Yard, Strip, Haulage, Fuel, Waste }
        [SerializeField] int seed = 7;
        [SerializeField] Choice recipe;

        [MenuItem("Tools/City/Core/Industrial/Industrial Lab Generator", priority = 59)]
        public static void Open() => GetWindow<IndustrialLabWindow>("Industrial Lab");

        void OnGUI()
        {
            EditorGUILayout.LabelField("Industrial blocks", EditorStyles.boldLabel);
            seed = EditorGUILayout.IntField("Seed", seed);
            recipe = (Choice)EditorGUILayout.EnumPopup("Complex", recipe);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button("Generate four blocks", GUILayout.Height(32))) Generate();
                if (GUILayout.Button("New seed + generate"))
                {
                    seed = Random.Range(1, 1000000);
                    Generate();
                }
            }
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Inspect a block", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
                for (int i = 0; i < 4; i++)
                    if (GUILayout.Button((i + 1).ToString())) Focus(i);
            EditorGUILayout.HelpBox("Play: 1–4 focus a block. WASD moves, Q/E or right-drag rotates, wheel zooms.\nGenerate and bake in Edit mode.", MessageType.Info);
        }

        void Generate()
        {
            IndustrialBlockForge.Generate(seed, recipe.ToString().ToLowerInvariant());
            Focus(0);
        }

        static void Focus(int index)
        {
            var lab = IndustrialBlockForge.Lab();
            foreach (var root in lab.GetRootGameObjects())
            {
                var review = root.GetComponentInChildren<IndustrialLabReview>();
                if (!review) continue;
                review.Focus(index);
                if (!EditorApplication.isPlaying)
                {
                    var camera = review.GetComponent<DemoCamera>();
                    var view = SceneView.lastActiveSceneView;
                    if (view) view.LookAt(camera.pivot, Quaternion.Euler(camera.pitch, camera.yaw, 0f), camera.distance * 0.42f, false, true);
                }
                return;
            }
        }
    }
}
