using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using LivingCity.Ambient;
using LivingCity.CameraRig;
using LivingCity.Data;
using LivingCity.Entities;
using LivingCity.Generation;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// One menu item that takes an empty scene to a running city: creates the config assets,
    /// builds the static layer, wires up the runtime spawners and points the camera at it.
    ///
    /// Everything here is doable by hand through the generator window and the Inspector; this
    /// exists so none of it has to be.
    /// </summary>
    public static class CitySceneSetup
    {
        const string BirdsPrefabPath = "Assets/polyperfect/Low Poly Animated People/- Particles/birds.prefab";

        [MenuItem("Tools/City/Set Up Scene (One Click)", priority = 0)]
        public static void SetUpScene()
        {
            if (!EnsureSafeScene())
                return;

            // 1. Config assets, populated from the verified prefab paths.
            CityAssetBootstrap.CreateAssets();

            var config = AssetDatabase.LoadAssetAtPath<CityConfig>("Assets/Configs/CityConfig.asset");
            var prefabs = AssetDatabase.LoadAssetAtPath<PrefabDatabase>("Assets/Configs/PrefabDatabase.asset");

            if (!config || !prefabs)
            {
                EditorUtility.DisplayDialog("City setup failed",
                    "Could not create the config assets in Assets/Configs. Check the Console for details.", "OK");
                return;
            }

            // 2. The city object, with the generator wired to those configs.
            var cityObject = GameObject.Find("City") ?? new GameObject("City");
            var builder = cityObject.GetComponent<CityBuilder>() ?? cityObject.AddComponent<CityBuilder>();

            CityEditorUtils.SetField(builder, "config", config);
            CityEditorUtils.SetField(builder, "prefabs", prefabs);
            CityEditorUtils.SetBool(builder, "buildOnStart", false);

            if (!cityObject.GetComponent<PathDebugGizmos>())
                cityObject.AddComponent<PathDebugGizmos>();

            // 3. Generate the static layer straight into the scene.
            var root = builder.Build(CityEditorUtils.Spawn);
            if (root)
                CityEditorUtils.MarkStaticForBatching(root);

            // 4. Runtime spawners. These stay empty until Play - cars and pedestrians are the
            //    only things that must NOT be baked into the scene.
            var spawners = GameObject.Find("Spawners") ?? new GameObject("Spawners");

            var vehicles = spawners.GetComponent<VehicleSpawner>() ?? spawners.AddComponent<VehicleSpawner>();
            CityEditorUtils.SetField(vehicles, "config", config);
            CityEditorUtils.SetField(vehicles, "prefabs", prefabs);

            var people = spawners.GetComponent<PedestrianSpawner>() ?? spawners.AddComponent<PedestrianSpawner>();
            CityEditorUtils.SetField(people, "config", config);
            CityEditorUtils.SetField(people, "prefabs", prefabs);

            var clouds = spawners.GetComponent<CloudSystem>() ?? spawners.AddComponent<CloudSystem>();
            CityEditorUtils.SetField(clouds, "config", config);
            CityEditorUtils.SetField(clouds, "prefabs", prefabs);

            var birdsPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BirdsPrefabPath);
            if (birdsPrefab)
            {
                var birds = spawners.GetComponent<BirdFlockSystem>() ?? spawners.AddComponent<BirdFlockSystem>();
                CityEditorUtils.SetField(birds, "config", config);
                CityEditorUtils.SetField(birds, "birdFlockPrefab", birdsPrefab);
            }

            // 5. Camera: orthographic, isometric, framed on the middle of the city.
            SetUpCamera(builder, config);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Selection.activeGameObject = cityObject;

            Debug.Log("[CitySetup] Scene ready. Press Play to see traffic. " +
                      "Save the scene (Cmd+S) to keep the generated city.");
        }

        /// <summary>
        /// Refuses to generate into a scene that belongs to an imported package.
        ///
        /// Those demo scenes are vendor content - generating into one mixes the city with a
        /// hundred-plus showcase objects and, worse, drags in scripts written for the legacy
        /// Input backend. Low Poly Animated People's "3RD Person" prefab is the usual culprit:
        /// its PlayerController calls Input.GetAxisRaw, which throws every frame under this
        /// project's Input System setting and buries every other message in the Console.
        /// </summary>
        static bool EnsureSafeScene()
        {
            var scene = EditorSceneManager.GetActiveScene();
            var path = scene.path;

            var isPackageScene = !string.IsNullOrEmpty(path) &&
                                 path.Replace('\\', '/').StartsWith("Assets/polyperfect/");

            if (!isPackageScene)
                return true;

            var choice = EditorUtility.DisplayDialogComplex(
                "That is a package demo scene",
                $"The open scene is:\n{path}\n\n" +
                "It ships with the asset package and contains demo objects that use the old " +
                "Input system, which throws errors continuously in this project.\n\n" +
                "Generate the city into a fresh scene instead?",
                "Create a new scene",
                "Cancel",
                "Generate here anyway");

            switch (choice)
            {
                case 0:
                    if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                        return false;
                    EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                    return true;

                case 1:
                    return false;

                default:
                    Debug.LogWarning("[CitySetup] Generating into a package demo scene. Expect " +
                                     "unrelated errors from its legacy-Input demo scripts.");
                    return true;
            }
        }

        static void SetUpCamera(CityBuilder builder, CityConfig config)
        {
            var camera = Camera.main;
            if (!camera)
            {
                var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
                camera = cameraObject.AddComponent<Camera>();
                cameraObject.AddComponent<AudioListener>();
            }

            camera.orthographic = true;
            camera.orthographicSize = 60f;

            // Far enough back to clear the whole city at any zoom, and a near plane that will
            // not clip the ground once the boom swings around.
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 1000f;

            var controller = camera.GetComponent<IsometricCameraController>()
                          ?? camera.gameObject.AddComponent<IsometricCameraController>();

            CityEditorUtils.SetField(controller, "cityBuilder", builder);

            // Frame the middle of the city so the first frame in Play is not looking at empty space.
            var centre = new Vector3(config.WorldWidth * 0.5f, 0f, config.WorldHeight * 0.5f);
            var rotation = Quaternion.Euler(45f, 45f, 0f);
            camera.transform.SetPositionAndRotation(centre - rotation * Vector3.forward * 200f, rotation);
        }

        [MenuItem("Tools/City/Clear Generated City", priority = 1)]
        public static void ClearCity()
        {
            var builder = Object.FindFirstObjectByType<CityBuilder>();
            if (!builder)
            {
                Debug.Log("[CitySetup] Nothing to clear - no CityBuilder in the scene.");
                return;
            }

            builder.Clear();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }
    }
}
