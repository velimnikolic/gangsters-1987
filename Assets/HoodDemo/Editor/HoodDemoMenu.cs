using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HoodDemo.Editor
{
    /// <summary>One click to the animation wall. The scene is intentionally a tiny
    /// bootstrap and builds its live figures on Play, because PlayableGraphs do not
    /// advance reliably in edit mode.</summary>
    static class HoodDemoMenu
    {
        internal const string ScenePath = "Assets/Scenes/HoodDemo.unity";

        [MenuItem("Tools/Hood animation demo/Open the animation wall", priority = 0)]
        static void Open()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var builder = Object.FindAnyObjectByType<HoodDemoBuilder>();
            if (builder != null)
            {
                Selection.activeGameObject = builder.gameObject;
                EditorGUIUtility.PingObject(builder.gameObject);
            }
        }

        [MenuItem("Tools/Hood animation demo/Open and play", priority = 1)]
        static void OpenAndPlay()
        {
            Open();
            EditorApplication.delayCall += () =>
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode &&
                    SceneManager.GetActiveScene().path == ScenePath)
                    EditorApplication.EnterPlaymode();
            };
        }

        [MenuItem("Tools/Hood animation demo/Print current animation list", priority = 2)]
        static void Print()
        {
            var entries = HoodDemoBuilder.Catalogue();
            var text = new System.Text.StringBuilder("[HoodDemo] ")
                .Append(entries.Count).Append(" implemented clips / variants\n");
            string section = null;
            foreach (var entry in entries)
            {
                if (entry.Section != section)
                {
                    section = entry.Section;
                    text.Append("\n").Append(section).Append("\n");
                }
                text.Append("  ").Append(entry.State).Append("  —  ")
                    .Append(entry.Clip != null ? entry.Clip.name : "MISSING");
                if (entry.Upper != null) text.Append(" + ").Append(entry.Upper.name);
                if (entry.Weapon.HasValue) text.Append("  [").Append(entry.Weapon.Value).Append(']');
                text.Append('\n');
            }
            Debug.Log(text.ToString());
        }
    }
}
