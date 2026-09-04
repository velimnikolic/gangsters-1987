using System;
using System.Collections.Generic;
using RoadDemo;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LivingCity.EditorTools
{
    /// <summary>Playable GAN-294 review scene: every source shop and one visitor per bay.</summary>
    public static class StorefrontTrafficShowroom
    {
        public const string ScenePath = "Assets/Scenes/StorefrontDemo.unity";
        const string SourceDir = "Assets/Synty/PolygonCity/Prefabs/Buildings/";
        const string IdleClip = "Assets/Animations/People/Breathing Idle.anim";
        const string WalkClip = "Assets/Animations/People/Standard Walk.anim";
        const float Spacing = 11f;
        const float BrokenRowZ = -10f;

        static readonly string[] Characters =
        {
            "Assets/Synty/PolygonPalmCity/Prefabs/Characters/SM_Chr_City_Male_01.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Characters/SM_Chr_City_Female_01.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Characters/SM_Chr_Rich_Male_01.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Characters/SM_Chr_City_Female_02.prefab",
            "Assets/Synty/PolygonCity/Prefabs/Characters/Character_Male_Jacket.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Characters/SM_Chr_City_Male_02.prefab",
            "Assets/Synty/PolygonCity/Prefabs/Characters/Character_Female_Jacket.prefab",
            "Assets/Synty/PolygonPalmCity/Prefabs/Characters/SM_Chr_Rich_Female_01.prefab",
        };

        public sealed class Report
        {
            public string Scene;
            public int Modules;
            public int AnimatedEntrances;
            public int Visitors;
            public int SharedEntrances;
            public int BrokenFronts;
            public string[] Failures = Array.Empty<string>();
            public bool Passed => Modules == StorefrontDoorCatalog.Count &&
                                  AnimatedEntrances == StorefrontDoorCatalog.Count &&
                                  Visitors == StorefrontDoorCatalog.Count &&
                                  SharedEntrances == 1 &&
                                  BrokenFronts == StorefrontDoorCatalog.Count - 1 &&
                                  Failures.Length == 0;
        }

        [MenuItem("Tools/City/Residential/Build Storefront Traffic Demo", priority = 34)]
        public static void DrawMenu()
        {
            var report = Draw();
            Debug.Log($"[Storefront traffic demo] {(report.Passed ? "PASS" : "FAIL")}: " +
                      $"{report.Modules} shops, {report.AnimatedEntrances} entrances, " +
                      $"{report.Visitors} visitors, {report.SharedEntrances} shared, " +
                      $"{report.BrokenFronts} broken fronts; " +
                      report.Scene + (report.Failures.Length > 0
                          ? "; " + string.Join("; ", report.Failures)
                          : string.Empty));
        }

        public static Report Draw()
        {
            var report = new Report { Scene = ScenePath };
            var failures = new List<string>();
            Scene previous = SceneManager.GetActiveScene();
            Scene loadedDemo = SceneManager.GetSceneByPath(ScenePath);
            bool reuseLoadedDemo = loadedDemo.IsValid() && loadedDemo.isLoaded;
            Scene demo = reuseLoadedDemo
                ? loadedDemo
                : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
                                              NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(demo);
                if (reuseLoadedDemo)
                    foreach (var sceneRoot in demo.GetRootGameObjects())
                        UnityEngine.Object.DestroyImmediate(sceneRoot);
                RenderSettings.fog = false;
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = new Color(0.72f, 0.78f, 0.86f);
                RenderSettings.ambientEquatorColor = new Color(0.42f, 0.45f, 0.50f);
                RenderSettings.ambientGroundColor = new Color(0.22f, 0.22f, 0.24f);

                var root = new GameObject("STOREFRONT TRAFFIC DEMO");
                SceneManager.MoveGameObjectToScene(root, demo);
                var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>(IdleClip);
                var walk = AssetDatabase.LoadAssetAtPath<AnimationClip>(WalkClip);
                if (idle == null) failures.Add("idle animation missing");
                if (walk == null) failures.Add("walk animation missing");

                for (int i = 0; i < StorefrontDoorCatalog.Count; i++)
                    BuildStation(root.transform, demo, i, idle, walk,
                                 report, failures);

                float minX = -5f;
                float maxX = (StorefrontDoorCatalog.Count - 1) * Spacing;
                float centreX = (minX + maxX) * 0.5f;
                float width = maxX - minX + 14f;
                Floor(root.transform, centreX, width);
                CameraAndLight(root.transform, centreX, width);
                Header(root.transform, centreX);

                EditorSceneManager.MarkSceneDirty(demo);
                if (!EditorSceneManager.SaveScene(demo, ScenePath))
                    failures.Add("could not save " + ScenePath);
            }
            catch (Exception exception)
            {
                failures.Add(exception.GetType().Name + ": " + exception.Message);
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded && previous != demo)
                    SceneManager.SetActiveScene(previous);
                if (!reuseLoadedDemo)
                    EditorSceneManager.CloseScene(demo, removeScene: true);
            }

            report.Failures = failures.ToArray();
            AssetDatabase.Refresh();
            return report;
        }

        static void BuildStation(Transform root, Scene demo, int index,
                                 AnimationClip idle, AnimationClip walk,
                                 Report report, List<string> failures)
        {
            var profile = StorefrontDoorCatalog.At(index);
            float x = index * Spacing;
            var station = new GameObject($"{index + 1:00} {profile.Module}");
            station.transform.SetParent(root, false);

            var module = Instantiate(profile.Module, demo, station.transform,
                                     new Vector3(x, 0f, 0f), failures);
            if (module == null) return;
            report.Modules++;

            var display = module.GetComponent<Storefront>();
            if (display == null) display = module.AddComponent<Storefront>();
            display.ConfigurePreview();
            if (display.Module != profile.Module)
                failures.Add(profile.Module + ": storefront preview did not configure");
            if (display.LeafCount != profile.Leaves)
                failures.Add(profile.Module + $": expected {profile.Leaves} leaves, got " +
                             display.LeafCount);
            ValidatePreviewPanes(module, display, profile.Module, failures);

            Storefront entrance = display;
            string subtitle = profile.Leaves == 1 ? "single door" : "double door";
            if (profile.Leaves == 0)
            {
                // Shop 05 is an authored glass display with no cut. Production binds it
                // to the adjacent doored bay; the demo makes that shared entrance explicit.
                var shared = Instantiate("SM_Bld_Shop_01", demo, station.transform,
                                         new Vector3(x + 5f, 0f, 0f), failures);
                if (shared != null)
                {
                    entrance = shared.GetComponent<Storefront>();
                    if (entrance == null) entrance = shared.AddComponent<Storefront>();
                    entrance.ConfigurePreview();
                    shared.name = profile.Module + " shared entrance (SM_Bld_Shop_01)";
                    report.SharedEntrances++;
                    subtitle = "display bay / shared door";
                }
            }

            if (entrance == null || entrance.LeafCount == 0)
            {
                failures.Add(profile.Module + ": no animated entrance for visitor");
                return;
            }
            report.AnimatedEntrances++;

            var characterPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Characters[index]);
            if (characterPrefab == null)
            {
                failures.Add(profile.Module + ": visitor prefab missing");
                return;
            }
            var visitor = (GameObject)PrefabUtility.InstantiatePrefab(characterPrefab, demo);
            visitor.name = profile.Module + " visitor";
            visitor.transform.SetParent(station.transform, true);
            var traffic = station.AddComponent<StorefrontDemoTraffic>();
            traffic.Configure(entrance, visitor.transform, idle, walk,
                              index / (float)StorefrontDoorCatalog.Count);
            report.Visitors++;

            Label(station.transform, ShortName(profile.Module) + "\n" + subtitle,
                  new Vector3(x - 2.5f, 4.25f, 0.15f), 0.10f,
                  new Color(0.08f, 0.08f, 0.09f), TextAnchor.LowerCenter);

            BuildBrokenExample(station.transform, demo, profile, x,
                               report, failures);
        }

        static void BuildBrokenExample(
            Transform station, Scene demo, StorefrontDoorProfile profile, float x,
            Report report, List<string> failures)
        {
            var module = Instantiate(profile.Module, demo, station,
                                     new Vector3(x, 0f, BrokenRowZ), failures);
            if (module == null) return;
            var storefront = module.GetComponent<Storefront>();
            if (storefront == null) storefront = module.AddComponent<Storefront>();
            storefront.ConfigurePreview();
            module.name = profile.Module + " broken-glass review";
            ValidatePreviewPanes(module, storefront,
                                 profile.Module + " broken review", failures);

            string subtitle = "no authored window";
            if (storefront.PaneCount > 0)
            {
                storefront.SetPreviewState(StorefrontState.Smashed);
                subtitle = "broken glass";
                if (HasBrokenGlass(storefront)) report.BrokenFronts++;
                else failures.Add(profile.Module +
                                  ": smashed preview produced no broken-glass mesh");
            }
            Label(station, ShortName(profile.Module) + "\n" + subtitle,
                  new Vector3(x - 2.5f, 4.25f, BrokenRowZ + 0.15f), 0.10f,
                  new Color(0.08f, 0.08f, 0.09f), TextAnchor.LowerCenter);
        }

        static bool HasBrokenGlass(Storefront storefront)
        {
            for (int i = 0; i < storefront.transform.childCount; i++)
            {
                var child = storefront.transform.GetChild(i);
                if (!child.name.StartsWith("Broken glass", StringComparison.Ordinal))
                    continue;
                foreach (var filter in child.GetComponentsInChildren<MeshFilter>(true))
                    if (filter != null && filter.sharedMesh != null &&
                        filter.sharedMesh.vertexCount > 0)
                        return true;
            }
            return false;
        }

        static void ValidatePreviewPanes(GameObject module, Storefront storefront,
                                         string moduleName, List<string> failures)
        {
            var sourceGlass = new List<MeshFilter>();
            foreach (var filter in module.GetComponentsInChildren<MeshFilter>(true))
                if (filter != null && filter.sharedMesh != null &&
                    filter.name.EndsWith("_Glass", StringComparison.OrdinalIgnoreCase))
                    sourceGlass.Add(filter);

            if (sourceGlass.Count == 0)
            {
                if (storefront.PaneCount != 0)
                    failures.Add(moduleName + ": solid facade grew replacement glass");
                return;
            }
            if (storefront.PaneCount == 0)
            {
                failures.Add(moduleName + ": authored glass produced no replacement panes");
                return;
            }

            Bounds authored = default;
            bool haveBounds = false;
            for (int i = 0; i < sourceGlass.Count; i++)
            {
                var filter = sourceGlass[i];
                var vertices = filter.sharedMesh.vertices;
                for (int n = 0; n < vertices.Length; n++)
                {
                    Vector3 local = module.transform.InverseTransformPoint(
                        filter.transform.TransformPoint(vertices[n]));
                    if (!haveBounds)
                    {
                        authored = new Bounds(local, Vector3.zero);
                        haveBounds = true;
                    }
                    else authored.Encapsulate(local);
                }
            }
            // Generated panes deliberately reach close to the floor even when the
            // imported glass starts above a sill. Keep that vertical allowance while
            // making the lateral/depth check tight enough to catch stray bevel planes.
            authored.Expand(new Vector3(0.2f, 0.9f, 0.2f));

            var panes = module.transform.Find("Panes");
            if (panes == null) return;
            foreach (var filter in panes.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter == null || filter.sharedMesh == null) continue;
                var vertices = filter.sharedMesh.vertices;
                for (int n = 0; n < vertices.Length; n++)
                {
                    Vector3 local = module.transform.InverseTransformPoint(
                        filter.transform.TransformPoint(vertices[n]));
                    if (authored.Contains(local)) continue;
                    failures.Add(moduleName +
                                 ": replacement glass extends outside authored frontage");
                    return;
                }
            }
        }

        static GameObject Instantiate(string module, Scene scene, Transform parent,
                                      Vector3 position, List<string> failures)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SourceDir + module + ".prefab");
            if (prefab == null)
            {
                failures.Add(module + ": source prefab missing");
                return null;
            }
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.name = module;
            instance.transform.SetParent(parent, true);
            instance.transform.position = position;
            return instance;
        }

        static string ShortName(string module)
        {
            const string prefix = "SM_Bld_";
            return module.StartsWith(prefix, StringComparison.Ordinal)
                ? module.Substring(prefix.Length).ToUpperInvariant()
                : module.ToUpperInvariant();
        }

        static void Label(Transform parent, string text, Vector3 position,
                          float size, Color colour, TextAnchor anchor)
        {
            var go = new GameObject("Label " + text.Replace('\n', ' '));
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            var label = go.AddComponent<TextMesh>();
            label.text = text;
            label.fontSize = 48;
            label.characterSize = size;
            label.color = colour;
            label.anchor = anchor;
            label.alignment = TextAlignment.Center;
        }

        static void Header(Transform parent, float centreX)
        {
            Label(parent, "STOREFRONT GLASS + TRAFFIC DEMO\n" +
                          "FRONT: doors + visitors   BACK: broken-window review",
                  new Vector3(centreX, 7.5f, -0.1f), 0.12f,
                  new Color(0.12f, 0.12f, 0.14f), TextAnchor.LowerCenter);
        }

        static void Floor(Transform parent, float centreX, float width)
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "review pavement";
            floor.transform.SetParent(parent, false);
            floor.transform.position = new Vector3(centreX, -0.15f, -5f);
            floor.transform.localScale = new Vector3(width, 0.25f, 25f);
            var collider = floor.GetComponent<Collider>();
            if (collider != null) UnityEngine.Object.DestroyImmediate(collider);
            var renderer = floor.GetComponent<Renderer>();
            if (renderer != null)
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(
                    "Assets/Synty/PolygonGeneric/Materials/Generic_Concrete.mat");
                if (material != null) renderer.sharedMaterial = material;
            }
        }

        static void CameraAndLight(Transform parent, float centreX, float width)
        {
            var sun = new GameObject("Sun");
            sun.transform.SetParent(parent, false);
            sun.transform.rotation = Quaternion.Euler(48f, -28f, 0f);
            var light = sun.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            light.shadows = LightShadows.Soft;

            var cameraObject = new GameObject("Storefront demo camera");
            cameraObject.transform.SetParent(parent, false);
            cameraObject.transform.position = new Vector3(centreX, 32f, 64f);
            cameraObject.transform.LookAt(new Vector3(centreX, 2.2f, -5f));
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 52f;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 220f;
            camera.clearFlags = CameraClearFlags.Skybox;
            cameraObject.AddComponent<AudioListener>();

            var rig = cameraObject.AddComponent<DemoCamera>();
            rig.pivot = new Vector3(centreX, 1.9f, -5f);
            rig.distance = Mathf.Clamp(width * 0.70f, 58f, 72f);
            rig.yaw = 180f;
            rig.pitch = 30f;
            rig.minDistance = 11f;
            rig.mapTransition = false;
            rig.mapCeiling = 150f;
            rig.showHint = true;
            rig.hintTopPx = 14f;
            rig.hint = "PLAY MODE: automatic shop visits   WASD/arrows: move   " +
                       "Q/E or right-drag: rotate   wheel: zoom";
            var shadows = cameraObject.AddComponent<DemoShadows>();
            shadows.rig = rig;
            shadows.margin = 35f;
            shadows.maxDistance = 180f;
        }
    }
}
