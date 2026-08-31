using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OcclusionDemo.Editor
{
    static class OcclusionDemoMenu
    {
        const string ScenePath = "Assets/Scenes/OcclusionDemo.unity";

        [MenuItem("Tools/City/Occlusion Gradient/Open Demo", priority = 30)]
        static void Open()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[OcclusionDemo] Stop Play mode before changing scenes.");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var builder = Object.FindAnyObjectByType<OcclusionDemoBuilder>();
            if (builder)
            {
                Selection.activeGameObject = builder.gameObject;
                EditorGUIUtility.PingObject(builder.gameObject);
            }
        }
    }
}
