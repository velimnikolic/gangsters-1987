using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
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
        const string ConfigPath = "Assets/Configs/CityConfig.asset";
        const string VolumeProfilePath = "Assets/Settings/SampleSceneProfile.asset";

        [MenuItem("Tools/City/Set Up Scene (One Click)", priority = 0)]
        public static void SetUpScene()
        {
            if (!EnsureSafeScene())
                return;

            // 1. Config assets, populated from the verified prefab paths.
            CityAssetBootstrap.CreateAssets();

            var config = AssetDatabase.LoadAssetAtPath<CityConfig>(ConfigPath);
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

            // Chat pairing and the bench/shop index. Runtime-only like the spawners; the
            // markers it reads are baked into the generated city above.
            var director = spawners.GetComponent<PedestrianInteractionDirector>()
                        ?? spawners.AddComponent<PedestrianInteractionDirector>();
            CityEditorUtils.SetField(director, "config", config);

            // The police: patrol fleet + beat officers, homed on the PoliceStation marker the
            // build above just attached. Runtime-only like the other spawners.
            var police = spawners.GetComponent<PoliceDirector>() ?? spawners.AddComponent<PoliceDirector>();
            CityEditorUtils.SetField(police, "config", config);
            CityEditorUtils.SetField(police, "prefabs", prefabs);

            var clouds = spawners.GetComponent<CloudSystem>() ?? spawners.AddComponent<CloudSystem>();
            CityEditorUtils.SetField(clouds, "config", config);
            CityEditorUtils.SetField(clouds, "prefabs", prefabs);

            // Works chimneys. Runtime like the clouds and for the same reason - generation left
            // a SmokeVent marker at each measured chimney mouth and this raises the plumes on
            // Start, so nothing that moves is baked into the saved scene.
            var smoke = spawners.GetComponent<SmokeStackSystem>() ?? spawners.AddComponent<SmokeStackSystem>();
            CityEditorUtils.SetField(smoke, "config", config);
            CityEditorUtils.SetField(smoke, "prefabs", prefabs);

            var birdsPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BirdsPrefabPath);
            if (birdsPrefab)
            {
                var birds = spawners.GetComponent<BirdFlockSystem>() ?? spawners.AddComponent<BirdFlockSystem>();
                CityEditorUtils.SetField(birds, "config", config);
                CityEditorUtils.SetField(birds, "birdFlockPrefab", birdsPrefab);
            }

            // 5. Camera: orthographic, isometric, framed on the middle of the city.
            SetUpCamera(builder, config);

            // 6. Key light and the post-processing volume. These used to arrive only from Unity's
            //    default new scene, which meant setting up into an emptied scene produced a city
            //    lit by ambient alone.
            SetUpLighting();

            // 7. The clock readout across the top of the screen. Idempotent, and it quietly does
            //    nothing on the one run where it has to import TextMeshPro's fonts first.
            CityHudSetup.EnsureHud();

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
            // Camera.main only reports cameras that are enabled, on an active object and tagged
            // MainCamera. A camera that is merely switched off is therefore invisible to it - and
            // to the renderer, which is exactly what "Display 1 No cameras rendering" means. Adopt
            // whatever is already there, inactive ones included, before adding a second one.
            var camera = Camera.main
                      ?? Object.FindAnyObjectByType<Camera>(FindObjectsInactive.Include);

            if (!camera)
            {
                var cameraObject = new GameObject("Main Camera");
                camera = cameraObject.AddComponent<Camera>();
            }

            // Every remaining way a scene can end up with nothing rendering: object deactivated,
            // component unticked, or output pointed at a display that is not there.
            camera.gameObject.SetActive(true);
            camera.enabled = true;
            camera.targetDisplay = 0;

            if (!camera.CompareTag("MainCamera"))
                camera.tag = "MainCamera";

            if (!camera.GetComponent<AudioListener>())
                camera.gameObject.AddComponent<AudioListener>();

            camera.orthographic = true;
            camera.orthographicSize = 60f;

            // Far enough back to clear the whole city at any zoom, and a near plane that will
            // not clip the ground once the boom swings around.
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 1000f;

            // Without this the Global Volume renders nothing: the profile's bloom and vignette are
            // sampled per-camera, so a camera with post-processing off leaves the scene flat even
            // though the volume is sitting right there in the hierarchy.
            var urp = camera.GetUniversalAdditionalCameraData();
            urp.renderPostProcessing = true;
            urp.renderShadows = true;

            var controller = camera.GetComponent<IsometricCameraController>()
                          ?? camera.gameObject.AddComponent<IsometricCameraController>();

            CityEditorUtils.SetField(controller, "cityBuilder", builder);

            // The camera-feel values, restated here rather than left to the field initialisers.
            // They currently agree with the script's own defaults, but this method overwrites
            // whatever is in the scene, so a silent divergence between the two would mean
            // "Restore Camera and Lighting" quietly reverts a tuning pass. Keeping them all
            // explicit makes the intended values readable in one place - if you change one, change
            // it in IsometricCameraController.cs and in SampleScene.unity too.
            //
            // maxOrthoSize 200: the city is 16x7 cells = 480x210 m of bounds, which needs 183 to
            // frame entirely at 4:3. The old 80 showed less than half of it.
            CityEditorUtils.SetFloat(controller, "maxOrthoSize", 200f);
            CityEditorUtils.SetFloat(controller, "scrollZoomSpeed", 6f);
            CityEditorUtils.SetFloat(controller, "keyboardPanSpeed", 60f);
            // dragPanSpeed 3: a mouse drag is limited by how far the hand travels in one stroke,
            // so the ground is deliberately moved three times the cursor distance. Touch stays 1:1.
            CityEditorUtils.SetFloat(controller, "dragPanSpeed", 3f);

            // Frame the middle of the city so the first frame in Play is not looking at empty space.
            var centre = config
                ? new Vector3(config.WorldWidth * 0.5f, 0f, config.WorldHeight * 0.5f)
                : Vector3.zero;
            var rotation = Quaternion.Euler(45f, 45f, 0f);
            camera.transform.SetPositionAndRotation(centre - rotation * Vector3.forward * 200f, rotation);
        }

        /// <summary>
        /// Puts back the three objects that are easy to lose and awkward to recreate by hand -
        /// the camera, the key light and the post-processing volume - without touching anything
        /// else in the scene.
        ///
        /// Reverting the scene file would do the same, but only by throwing away every other
        /// unsaved change with it. The settings applied here are the ones the scene was saved
        /// with, so the result matches what a revert would have produced for these three objects
        /// and leaves the rest of the session's work alone.
        /// </summary>
        [MenuItem("Tools/City/Restore Camera and Lighting", priority = 2)]
        public static void RestoreCameraAndLighting()
        {
            var config = AssetDatabase.LoadAssetAtPath<CityConfig>(ConfigPath);
            var builder = Object.FindAnyObjectByType<CityBuilder>();

            SetUpCamera(builder, config);
            SetUpLighting();

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            if (!builder)
                Debug.LogWarning("[CitySetup] No CityBuilder in the scene, so the camera has " +
                                 "nothing to frame itself against. Run Tools/City/Generate to " +
                                 "rebuild the city, then this again.");

            // Naming the camera and its position turns "it still says no cameras rendering" into
            // something checkable: either this line did not appear, or it did and the object it
            // names is sitting somewhere you can go look at.
            var camera = Camera.main;
            Selection.activeGameObject = camera ? camera.gameObject : null;

            Debug.Log($"[CitySetup] Restored '{(camera ? camera.name : "?")}' at " +
                      $"{(camera ? camera.transform.position.ToString() : "?")}, " +
                      "plus the directional light and global volume.", camera);
        }

        static void SetUpLighting()
        {
            SetUpKeyLight();
            SetUpGlobalVolume();
            EnsureWeather();
        }

        /// <summary>
        /// Creates and wires the CityWeather component if it is not already there, and returns it.
        ///
        /// Public because the weather preview menu items need it: hitting "preview smog" in a
        /// scene that has no weather component should give you smog, not an explanation of why it
        /// cannot. Idempotent, so calling it from both paths is free.
        /// </summary>
        public static CityWeather EnsureWeather()
        {
            var host = GameObject.Find("Spawners") ?? new GameObject("Spawners");
            var weather = Object.FindAnyObjectByType<CityWeather>()
                       ?? host.AddComponent<CityWeather>();

            var config = AssetDatabase.LoadAssetAtPath<CityConfig>(ConfigPath);

            var clock = Object.FindAnyObjectByType<CityClock>() ?? host.AddComponent<CityClock>();
            CityEditorUtils.SetField(clock, "config", config);

            var windows = Object.FindAnyObjectByType<NightWindows>()
                       ?? host.AddComponent<NightWindows>();

            CityEditorUtils.SetField(windows, "clock", clock);

            var lamps = Object.FindAnyObjectByType<StreetLampLights>()
                     ?? host.AddComponent<StreetLampLights>();

            CityEditorUtils.SetField(lamps, "config", config);
            CityEditorUtils.SetField(lamps, "clock", clock);

            var headlights = Object.FindAnyObjectByType<CarHeadlights>()
                          ?? host.AddComponent<CarHeadlights>();

            CityEditorUtils.SetField(headlights, "config", config);
            CityEditorUtils.SetField(headlights, "clock", clock);

            CityEditorUtils.SetField(weather, "config", config);

            CityEditorUtils.SetField(weather, "clock", clock);
            CityEditorUtils.SetField(weather, "sun", SetUpKeyLight());
            CityEditorUtils.SetField(weather, "globalVolume", SetUpGlobalVolume());

            CityEditorUtils.SetField(weather, "clearDuskProfile",
                AssetDatabase.LoadAssetAtPath<VolumeProfile>(WeatherProfiles.ClearDuskPath));
            CityEditorUtils.SetField(weather, "overcastProfile",
                AssetDatabase.LoadAssetAtPath<VolumeProfile>(WeatherProfiles.OvercastPath));
            CityEditorUtils.SetField(weather, "smogProfile",
                AssetDatabase.LoadAssetAtPath<VolumeProfile>(WeatherProfiles.SmogPath));

            return weather;
        }

        /// <summary>
        /// The scene's key light, at the intensity and colour temperature it was authored with.
        /// A default Unity directional light is white at intensity 1; this one is 5000K at 2, and
        /// the difference between the two is most of what "the lighting looks wrong" means here.
        /// </summary>
        static Light SetUpKeyLight()
        {
            var lightObject = GameObject.Find("Directional Light");
            if (!lightObject)
                lightObject = new GameObject("Directional Light");

            lightObject.transform.SetPositionAndRotation(
                new Vector3(0f, 3f, 0f), Quaternion.Euler(50f, -30f, 0f));

            // Ternary, not `??`. Light is a native component, and a missing one comes back as a
            // wrapper around a null pointer rather than a C# null - which `??` accepts, skipping
            // the AddComponent and throwing MissingComponentException on the first write below.
            // This is why the Console has been reporting "There is no 'Light' attached to the
            // Directional Light game object". Unity's implicit bool is the check that reads the
            // pointer. The MonoBehaviour cases above are unaffected: those do return a real null.
            var existingLight = lightObject.GetComponent<Light>();
            var light = existingLight ? existingLight : lightObject.AddComponent<Light>();

            light.type = LightType.Directional;
            light.color = Color.white;
            light.intensity = 2f;
            light.useColorTemperature = true;
            light.colorTemperature = 5000f;
            light.bounceIntensity = 1f;
            light.lightmapBakeType = LightmapBakeType.Realtime;

            light.shadows = LightShadows.Soft;
            light.shadowStrength = 1f;
            light.shadowBias = 0.05f;
            light.shadowNormalBias = 0.4f;
            light.shadowNearPlane = 0.2f;

            var urp = lightObject.GetComponent<UniversalAdditionalLightData>()
                   ?? lightObject.AddComponent<UniversalAdditionalLightData>();
            urp.usePipelineSettings = true;

            return light;
        }

        /// <summary>
        /// The global post-processing volume. Paired with renderPostProcessing on the camera -
        /// either one alone does nothing.
        ///
        /// The volume is created whether or not a profile turns up, because CityWeather swaps a
        /// weather grade onto it moments later; refusing to make it over a missing base profile
        /// would take the weather down with it.
        /// </summary>
        static Volume SetUpGlobalVolume()
        {
            var volumeObject = GameObject.Find("Global Volume");
            if (!volumeObject)
                volumeObject = new GameObject("Global Volume");

            volumeObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            var volume = volumeObject.GetComponent<Volume>() ?? volumeObject.AddComponent<Volume>();

            volume.isGlobal = true;
            volume.priority = 0f;
            volume.blendDistance = 0f;
            volume.weight = 1f;

            // Only as a fallback: a volume that already carries a weather grade must keep it.
            if (!volume.sharedProfile)
                volume.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);

            return volume;
        }

        [MenuItem("Tools/City/Clear Generated City", priority = 1)]
        public static void ClearCity()
        {
            // Every builder, not FindAnyObjectByType. That call picks an arbitrary one when the
            // scene holds more than one - and a scene ends up with a spare unconfigured "City"
            // easily enough, since both this setup menu and the generator window will create one.
            // Landing on the empty spare made Clear silently do nothing, which is indistinguishable
            // from a broken menu item. Clearing all of them cannot pick wrong.
            var builders = Object.FindObjectsByType<CityBuilder>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            if (builders.Length == 0)
            {
                Debug.Log("[CitySetup] Nothing to clear - no CityBuilder in the scene.");
                return;
            }

            var cleared = 0;
            foreach (var builder in builders)
                cleared += builder.Clear();

            if (cleared > 0)
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            Debug.Log($"[CitySetup] Cleared {cleared} generated " +
                      $"{(cleared == 1 ? "root" : "roots")} " +
                      $"from {builders.Length} CityBuilder{(builders.Length == 1 ? "" : "s")}.");

            // What is left under each builder. If a city is still visible after this says it
            // cleared everything, this list is what identifies where it actually lives.
            foreach (var builder in builders)
                Debug.Log($"[CitySetup] '{builder.name}' now holds: {DescribeChildren(builder.transform)}", builder);

            WarnAboutSpareBuilders(builders);
        }

        /// <summary>
        /// A second CityBuilder is not fatal now that the clear sweeps all of them, but it still
        /// makes the generator window a coin flip over which one it edits. Logged with the object
        /// as context so clicking the warning pings the stray in the hierarchy.
        /// </summary>
        static string DescribeChildren(Transform parent)
        {
            if (parent.childCount == 0) return "(nothing)";

            var names = new string[parent.childCount];
            for (var i = 0; i < parent.childCount; i++)
                names[i] = $"'{parent.GetChild(i).name}'";
            return string.Join(", ", names);
        }

        static void WarnAboutSpareBuilders(CityBuilder[] builders)
        {
            if (builders.Length < 2) return;

            foreach (var builder in builders)
            {
                if (builder.Config && builder.Prefabs) continue;
                Debug.LogWarning(
                    $"[CitySetup] '{builder.name}' is a spare CityBuilder with no CityConfig or " +
                    "PrefabDatabase assigned. Delete it - while it is in the scene the generator " +
                    "window may pick it instead of the real one.", builder);
            }
        }
    }
}
