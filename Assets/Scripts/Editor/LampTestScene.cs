using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using LivingCity.Ambient;
using LivingCity.Data;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// One lamp, a dark floor, the game's camera angle - and nothing else.
    ///
    /// Exists because judging the lamp lighting inside a generated city means judging it
    /// through a hundred other lights, tinted facades and whatever the weather grade does.
    /// Every question this rig has raised - pools too dim, pools fused, pools visually
    /// detached from their lanterns by the 45-degree camera's parallax, stale preview lights
    /// stacking up - would have been answered in minutes on a single lamp over bare ground.
    ///
    /// The scene is deliberately not saved anywhere: re-running the menu rebuilds it from
    /// scratch, which also makes it the fastest way to re-test after a recompile.
    /// </summary>
    public static class LampTestScene
    {
        [MenuItem("Tools/City/Weather/Lamp Test Scene", priority = 51)]
        static void Create()
        {
            // Scene surgery is an edit-mode job: NewScene and the save prompt both throw
            // InvalidOperationException in Play, which used to kill the click with nothing on
            // screen but a Console entry. Clicked from Play, this now stops Play and finishes
            // the job once the editor has actually left it (leaving is asynchronous).
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.isPlaying = false;
                EditorApplication.playModeStateChanged += CreateOnceEditMode;
                Debug.Log("[LampTest] Leaving Play mode - the test scene opens right after.");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            var db = AssetDatabase.LoadAssetAtPath<PrefabDatabase>("Assets/Configs/PrefabDatabase.asset");
            var config = AssetDatabase.LoadAssetAtPath<CityConfig>("Assets/Configs/CityConfig.asset");

            if (!db || db.streetLamps == null || db.streetLamps.Length == 0 || !db.groundTile)
            {
                Debug.LogError("[LampTest] PrefabDatabase.asset with a street lamp and a ground tile " +
                               "is required - run the asset bootstrap first.");
                return;
            }

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Night without CityWeather: flat near-black ambient plus a whisper of cold moon,
            // so the lamp itself still reads as a silhouette while its pools are the only real
            // light on screen. No fog, no volumes, nothing to argue with.
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.02f, 0.03f, 0.06f);
            RenderSettings.fog = false;

            var moon = new GameObject("Moon (dim)").AddComponent<Light>();
            moon.type = LightType.Directional;
            moon.color = new Color(0.6f, 0.75f, 1f);
            moon.intensity = 0.12f;
            moon.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            // 3x3 ground tiles: the pools are ~9m across and the camera looks in at 45 degrees,
            // so one 30m tile would put the horizon right behind the lamp.
            for (var x = -1; x <= 1; x++)
            for (var z = -1; z <= 1; z++)
            {
                var tile = (GameObject)PrefabUtility.InstantiatePrefab(db.groundTile);
                tile.transform.position = new Vector3(x * 30f, 0f, z * 30f);
            }

            // Arms along +/-X, exactly as authored - heads at (-3.1, 9.2) and (+3.1, 9.2).
            var lamp = (GameObject)PrefabUtility.InstantiatePrefab(db.streetLamps[0]);
            lamp.transform.position = Vector3.zero;

            var systems = new GameObject("City Systems");
            var clock = systems.AddComponent<CityClock>();
            var rig = systems.AddComponent<StreetLampLights>();

            using (var so = new SerializedObject(clock))
            {
                so.FindProperty("config").objectReferenceValue = config;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            using (var so = new SerializedObject(rig))
            {
                so.FindProperty("config").objectReferenceValue = config;
                so.FindProperty("clock").objectReferenceValue = clock;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            // The game's own framing: orthographic, pitch 45 / yaw 45. The parallax under test
            // only exists at this angle, so the test has to look through the same camera. The
            // camera sits back exactly boomLength (200) so the controller's Awake resolves its
            // focus point to the lamp.
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 14f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 500f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.01f, 0.02f, 0.05f);

            var rotation = Quaternion.Euler(45f, 45f, 0f);
            camGo.transform.SetPositionAndRotation(-(rotation * Vector3.forward) * 200f, rotation);

            var controller = camGo.AddComponent<CameraRig.IsometricCameraController>();
            using (var so = new SerializedObject(controller))
            {
                // Start zoomed in on the lamp instead of the game's city-wide default of 40.
                so.FindProperty("orthoSize").floatValue = 14f;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            rig.ApplyForEditor(1f);

            var view = SceneView.lastActiveSceneView;
            if (view)
            {
                view.orthographic = true;
                view.LookAt(Vector3.zero, rotation, 14f);
            }

            Debug.Log("[LampTest] One lamp at the origin, arms along X, lights applied at full " +
                      "night. Q/E rotates in Play. After a recompile or a tuning change, re-run " +
                      "this menu item - it rebuilds the whole scene in one go.");
        }

        static void CreateOnceEditMode(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredEditMode)
                return;

            EditorApplication.playModeStateChanged -= CreateOnceEditMode;
            Create();
        }

        const string CityScenePath = "Assets/Scenes/SampleScene.unity";

        /// <summary>
        /// The way out of the test scene. The test scene is untitled on purpose, so this
        /// discards it without any save ceremony; a REAL scene with unsaved work still gets
        /// the save prompt before the city scene replaces it.
        /// </summary>
        [MenuItem("Tools/City/Weather/Back To City Scene", priority = 52)]
        static void BackToCity()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.isPlaying = false;
                EditorApplication.playModeStateChanged += BackToCityOnceEditMode;
                return;
            }

            if (!System.IO.File.Exists(CityScenePath))
            {
                Debug.LogError($"[LampTest] {CityScenePath} not found - open your city scene by hand.");
                return;
            }

            var active = EditorSceneManager.GetActiveScene();
            if (!string.IsNullOrEmpty(active.path) &&
                !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            EditorSceneManager.OpenScene(CityScenePath);
        }

        static void BackToCityOnceEditMode(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredEditMode)
                return;

            EditorApplication.playModeStateChanged -= BackToCityOnceEditMode;
            BackToCity();
        }

        /// <summary>
        /// The preview light holders are HideFlags.DontSave, which means they even survive
        /// their scene being closed - re-rooted, sceneless, still burning: beams in the next
        /// scene with no lamp above them. Sweeping on every scene open keeps a scene switch
        /// from ever leaking the previous scene's lamp light into this one.
        /// </summary>
        [InitializeOnLoadMethod]
        static void SweepStraysOnSceneOpen() =>
            EditorSceneManager.sceneOpened += (_, _) => StreetLampLights.SweepStrays();
    }
}
