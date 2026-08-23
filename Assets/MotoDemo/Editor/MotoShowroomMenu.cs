using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MotoDemo
{
    /// <summary>
    /// One click to the showroom, from the menu bar.
    ///
    /// The rank builds itself in the editor already (MotoDemoBuilder is ExecuteAlways),
    /// so opening the scene is enough - but a scene that has built itself is no use if
    /// the view happens to be pointed at the sky. So: open the scene, stand the line
    /// up, point the Scene view at it, and select the bootstrap so every switch is on
    /// the Inspector when the user looks down at it.
    ///
    /// The third item answers the question without opening anything: it prints every
    /// two-wheeler in the project with its pack and its path, which is what settles
    /// "which of the two SM_Veh_Motorbike_01s did that table mean".
    /// </summary>
    static class MotoShowroomMenu
    {
        const string ScenePath = "Assets/Scenes/MotoDemo.unity";

        [MenuItem("Tools/Moto showroom/Open the showroom", priority = 0)]
        static void Open()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Look();
        }

        [MenuItem("Tools/Moto showroom/Look at it", priority = 1)]
        static void Look()
        {
            var showroom = Object.FindAnyObjectByType<MotoDemoBuilder>();
            if (showroom == null)
            {
                Debug.LogWarning("[MotoDemo] No showroom in the open scene - Tools > Moto " +
                                 "showroom > Open the showroom.");
                return;
            }
            // it may have been standing since before the last script reload
            showroom.RebuildPreview();
            showroom.Frame();
            Selection.activeGameObject = showroom.gameObject;
            EditorGUIUtility.PingObject(showroom.gameObject);
        }

        [MenuItem("Tools/Moto showroom/List every two-wheeler", priority = 2)]
        static void List()
        {
            var machines = MotoDemoBuilder.Machines();
            var sb = new StringBuilder("[MotoDemo] ").Append(machines.Count)
                .Append(" two-wheelers in the project:\n");
            foreach (var m in machines)
                sb.Append("  ").Append(m.Kind.ToString().PadRight(11))
                  .Append(m.Name.PadRight(24)).Append(m.Path).Append('\n');
            Debug.Log(sb.ToString());
        }
    }
}
