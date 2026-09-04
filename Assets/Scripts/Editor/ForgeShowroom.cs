using System;
using System.Collections.Generic;
using RoadDemo;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// Builds the forge's own disposable review scene without replacing or editing the
    /// scene somebody else has open. The saved scene is a thin host for the runtime
    /// controller; all buildings still come from ResidentialFacade + StandSheet.
    /// </summary>
    public static class ForgeShowroom
    {
        public const string ScenePath = "Assets/Scenes/ForgeDemo.unity";

        public sealed class Report
        {
            public string Scene;
            public string Mode;
            public int PropsPercent;
            public int Sheets;
            public int Faults;
            public int ExpectedParts;
            public int StoodParts;
            public int ExpectedStorefronts;
            public int Storefronts;
            public int ExpectedStorefrontBays;
            public int CoveredStorefrontBays;
            public string[] Signatures = Array.Empty<string>();
            public string[] Bounds = Array.Empty<string>();
            public string[] Failures = Array.Empty<string>();
            public bool Passed => Sheets > 0 && Faults == 0 && Failures.Length == 0;
        }

        [MenuItem("Tools/City/Residential/Forge Showroom", priority = 35)]
        public static void BuildMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[Forge showroom] Stop Play mode before rebuilding the scene.");
                return;
            }
            var report = BuildGallery(1987, ResidentialFacade.DefaultPropsPercent);
            Debug.Log($"[Forge showroom] {(report.Passed ? "PASS" : "FAIL")}: " +
                      $"{report.Sheets} sheet(s), {report.Faults} fault(s), {report.Scene}" +
                      (report.Failures.Length > 0
                          ? "; " + string.Join("; ", report.Failures)
                          : string.Empty));
        }

        public static Report BuildSingle(
            int seed, int length, int floors,
            int propsPercent = ResidentialFacade.DefaultPropsPercent)
        {
            CheckPropsPercent(propsPercent);
            return Build("single", propsPercent, controller =>
            {
                controller.seed = seed;
                controller.length = length;
                controller.floors = floors;
                controller.propsPercent = propsPercent;
                return new[] { controller.GenerateBlock(Raise) };
            });
        }

        public static Report BuildGallery(
            int seed, int propsPercent = ResidentialFacade.DefaultPropsPercent)
        {
            CheckPropsPercent(propsPercent);
            return Build("gallery", propsPercent, controller =>
            {
                controller.seed = seed;
                controller.propsPercent = propsPercent;
                return controller.GenerateGallery(Raise);
            });
        }

        static void CheckPropsPercent(int propsPercent)
        {
            if (propsPercent < ResidentialFacade.MinPropsPercent ||
                propsPercent > ResidentialFacade.MaxPropsPercent)
                throw new ArgumentOutOfRangeException(
                    nameof(propsPercent), propsPercent,
                    $"Optional props must be {ResidentialFacade.MinPropsPercent}.." +
                    $"{ResidentialFacade.MaxPropsPercent} percent.");
        }

        static Report Build(string mode, int propsPercent,
                            Func<ForgeDemoController, ResidentialFacade.Sheet[]> draw)
        {
            var report = new Report
            {
                Scene = ScenePath,
                Mode = mode,
                PropsPercent = propsPercent,
            };
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                report.Failures = new[] { "stop Play mode before rebuilding ForgeDemo" };
                return report;
            }
            var failures = new List<string>();
            var signatures = new List<string>();
            var bounds = new List<string>();
            Scene previous = SceneManager.GetActiveScene();
            Scene loaded = SceneManager.GetSceneByPath(ScenePath);
            bool reuseLoaded = loaded.IsValid() && loaded.isLoaded;
            Scene forge = reuseLoaded
                ? loaded
                : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            ForgeDemoController controller = null;

            try
            {
                if (!forge.IsValid())
                    throw new InvalidOperationException("Could not create the ForgeDemo scene.");
                SceneManager.SetActiveScene(forge);
                if (reuseLoaded)
                {
                    foreach (var oldController in
                             UnityEngine.Object.FindObjectsByType<ForgeDemoController>(
                                 FindObjectsInactive.Include, FindObjectsSortMode.None))
                        if (oldController != null && oldController.gameObject.scene == forge)
                            oldController.ForgetStandingUnits();
                    foreach (var sceneRoot in forge.GetRootGameObjects())
                        UnityEngine.Object.DestroyImmediate(sceneRoot);
                }

                ConfigureEnvironment();
                var root = new GameObject(ForgeDemoController.ContentRootName);
                SceneManager.MoveGameObjectToScene(root, forge);
                controller = root.AddComponent<ForgeDemoController>();
                var sheets = draw(controller) ?? Array.Empty<ResidentialFacade.Sheet>();
                for (int i = 0; i < sheets.Length; i++)
                {
                    var sheet = sheets[i];
                    if (sheet == null)
                    {
                        failures.Add($"sheet {i + 1}: forge returned null");
                        continue;
                    }
                    if (report.Sheets == 0)
                        report.PropsPercent = sheet.PropsPercent;
                    report.Sheets++;
                    report.Faults += sheet.Faults?.Length ?? 0;
                    signatures.Add(sheet.Signature ?? $"sheet-{i + 1}");
                    AuditStanding(controller, sheet, i, report, bounds, failures);
                }

                CameraAndLight(forge);
                EditorSceneManager.MarkSceneDirty(forge);
                if (!EditorSceneManager.SaveScene(forge, ScenePath))
                    failures.Add("could not save " + ScenePath);
            }
            catch (Exception exception)
            {
                failures.Add(exception.GetType().Name + ": " + exception.Message);
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded && previous != forge)
                    SceneManager.SetActiveScene(previous);
                if (!reuseLoaded && forge.IsValid() && forge.isLoaded)
                {
                    if (controller != null) controller.ForgetStandingUnits();
                    EditorSceneManager.CloseScene(forge, removeScene: true);
                }
            }

            report.Signatures = signatures.ToArray();
            report.Bounds = bounds.ToArray();
            report.Failures = failures.ToArray();
            AssetDatabase.Refresh();
            return report;
        }

        static void AuditStanding(
            ForgeDemoController controller, ResidentialFacade.Sheet sheet, int index,
            Report report, List<string> bounds, List<string> failures)
        {
            var generated = controller != null
                ? controller.transform.Find(ForgeDemoController.GeneratedRootName)
                : null;
            var building = FindDirect(generated, sheet.Signature);
            if (building == null)
            {
                failures.Add($"sheet {index + 1}: stood building {sheet.Signature} is missing");
                return;
            }

            var audit = building.GetComponent<ForgeStandAudit>();
            if (audit == null)
            {
                failures.Add($"sheet {index + 1}: stood building has no ForgeStandAudit");
                return;
            }
            report.ExpectedParts += audit.RequiredParts;
            report.StoodParts += audit.RaisedParts;
            report.ExpectedStorefronts += audit.RequiredStorefronts;
            report.Storefronts += audit.Storefronts;
            report.ExpectedStorefrontBays += audit.RequiredStorefrontBays;
            report.CoveredStorefrontBays += audit.CoveredStorefrontBays;
            bounds.Add($"{sheet.Signature}: " +
                       $"x {audit.RendererMin.x:0.###}..{audit.RendererMax.x:0.###}, " +
                       $"y {audit.RendererMin.y:0.###}..{audit.RendererMax.y:0.###}, " +
                       $"z {audit.RendererMin.z:0.###}..{audit.RendererMax.z:0.###}; " +
                       $"inside Unit = {audit.WithinUnitBounds}");
            if (audit.RaisedParts != audit.RequiredParts ||
                audit.Storefronts < audit.RequiredStorefronts ||
                audit.CoveredStorefrontBays < audit.RequiredStorefrontBays ||
                !audit.HasRendererBounds || !audit.WithinUnitBounds)
                failures.Add($"sheet {index + 1}: stood audit reports parts " +
                             $"{audit.RaisedParts}/{audit.RequiredParts}, storefronts " +
                             $"{audit.Storefronts}/{audit.RequiredStorefronts}, bays " +
                             $"{audit.CoveredStorefrontBays}/{audit.RequiredStorefrontBays}, " +
                             $"bounds {(audit.WithinUnitBounds ? "inside" : "outside")}");
        }

        static Transform FindDirect(Transform parent, string name)
        {
            if (parent == null || string.IsNullOrEmpty(name)) return null;
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child != null && child.name == name) return child;
            }
            return null;
        }

        static GameObject Raise(GameObject prefab, Transform parent)
        {
            if (prefab == null) return null;
            return (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        }

        static void ConfigureEnvironment()
        {
            RenderSettings.fog = false;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.72f, 0.78f, 0.86f);
            RenderSettings.ambientEquatorColor = new Color(0.42f, 0.45f, 0.50f);
            RenderSettings.ambientGroundColor = new Color(0.22f, 0.22f, 0.24f);
        }

        static void CameraAndLight(Scene scene)
        {
            var sun = new GameObject("Forge Sun");
            SceneManager.MoveGameObjectToScene(sun, scene);
            sun.transform.rotation = Quaternion.Euler(48f, -28f, 0f);
            var light = sun.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            light.shadows = LightShadows.Soft;

            // Saved plain: ReviewSceneCamera installs the shared WASD/orbit/zoom rig on Play.
            var cameraObject = new GameObject("Forge review camera");
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetPositionAndRotation(
                new Vector3(30f, 22f, -42f), Quaternion.Euler(25f, 0f, 0f));
            var camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 45f;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 900f;
            camera.clearFlags = CameraClearFlags.Skybox;
            cameraObject.AddComponent<AudioListener>();
        }
    }
}
