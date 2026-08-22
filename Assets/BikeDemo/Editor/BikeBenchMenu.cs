using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BikeDemo
{
    /// <summary>
    /// One click to the bike bench, from the menu bar.
    ///
    /// The bench builds itself in the editor already (BikeDemoBuilder is ExecuteAlways),
    /// so opening the scene is enough - but a scene that has built itself is no use if
    /// the view happens to be pointed at the sky, and "I cannot find my way round Unity"
    /// is a fair complaint about every demo in this project. So: open the scene, put the
    /// stand up, point the Scene view at the two men, and select the bootstrap so every
    /// number is already on the Inspector when the user looks down at it.
    ///
    /// Nothing here is needed at runtime and nothing has to be re-run: it is a shortcut,
    /// not a build step.
    /// </summary>
    static class BikeBenchMenu
    {
        const string ScenePath = "Assets/Scenes/BikeDemo.unity";

        [MenuItem("Tools/Bike bench/Open the bike bench", priority = 0)]
        static void Open()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Look();
        }

        [MenuItem("Tools/Bike bench/Look at it", priority = 1)]
        static void Look()
        {
            var bench = Object.FindAnyObjectByType<BikeDemoBuilder>();
            if (bench == null)
            {
                Debug.LogWarning("[BikeDemo] No bench in the open scene - Tools > Bike bench > " +
                                 "Open the bike bench.");
                return;
            }
            // it may have been standing since before the last script reload
            bench.RebuildPreview();
            bench.Frame();
            // the Inspector is where the tuning happens, so leave it open on the knobs
            Selection.activeGameObject = bench.gameObject;
            EditorGUIUtility.PingObject(bench.gameObject);
        }

        [MenuItem("Tools/Bike bench/Put the stand up again", priority = 2)]
        static void Rebuild()
        {
            var bench = Object.FindAnyObjectByType<BikeDemoBuilder>();
            if (bench == null) { Open(); return; }
            bench.RebuildPreview();
            bench.Frame();
        }
    }
}
