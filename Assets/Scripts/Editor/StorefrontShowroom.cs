using System;
using System.Collections.Generic;
using RoadDemo;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LivingCity.EditorTools
{
    /// <summary>Deterministic visual bench: untouched source modules above, live states below.</summary>
    public static class StorefrontShowroom
    {
        public const string ScenePath = "Assets/Scenes/StorefrontBench.unity";
        const string SourceDir = "Assets/Synty/PolygonCity/Prefabs/Buildings/";
        const string ShutterMaterial =
            "Assets/Synty/PolygonMapsPrison/Materials/Concrete_Dark_01.mat";

        static readonly StorefrontState[] States =
        {
            StorefrontState.Intact,
            StorefrontState.Open,
            StorefrontState.Smashed,
            StorefrontState.Burning,
            StorefrontState.Boarded,
            StorefrontState.Shuttered,
        };

        public sealed class Report
        {
            public string Scene;
            public int SourceModules;
            public int LiveStorefronts;
            public int StateExamples;
            public string[] Failures = Array.Empty<string>();
            public bool Passed => SourceModules == StorefrontDoorCatalog.Count &&
                                  LiveStorefronts == StorefrontDoorCatalog.Count &&
                                  StateExamples == States.Length &&
                                  Failures.Length == 0;
        }

        [MenuItem("Tools/City/Residential/Build Storefront Bench", priority = 33)]
        public static void DrawMenu()
        {
            var report = Draw();
            Debug.Log($"[Storefront bench] {(report.Passed ? "PASS" : "FAIL")}: " +
                      $"{report.SourceModules} source, {report.LiveStorefronts} live, " +
                      $"{report.StateExamples} states, " +
                      report.Scene + (report.Failures.Length > 0
                          ? "; " + string.Join("; ", report.Failures)
                          : string.Empty));
        }

        public static Report Draw()
        {
            var report = new Report { Scene = ScenePath };
            var failures = new List<string>();
            Scene previous = SceneManager.GetActiveScene();
            Scene bench = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
                                                       NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(bench);
                RenderSettings.fog = false;
                var root = new GameObject("STOREFRONT BENCH");
                SceneManager.MoveGameObjectToScene(root, bench);
                var shutter = AssetDatabase.LoadAssetAtPath<Material>(ShutterMaterial);

                float span = (StorefrontDoorCatalog.Count - 1) * 13f;
                for (int i = 0; i < StorefrontDoorCatalog.Count; i++)
                {
                    var profile = StorefrontDoorCatalog.At(i);
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                        SourceDir + profile.Module + ".prefab");
                    if (prefab == null)
                    {
                        failures.Add(profile.Module + ": source prefab missing");
                        continue;
                    }

                    float x = i * 13f;
                    var source = (GameObject)PrefabUtility.InstantiatePrefab(prefab, bench);
                    source.name = profile.Module + " source";
                    source.transform.SetParent(root.transform, true);
                    source.transform.position = new Vector3(x, 0f, 10f);
                    Label(root.transform, profile.Module,
                        new Vector3(x - 2.5f, 4.2f, 10f));
                    report.SourceModules++;

                    var live = (GameObject)PrefabUtility.InstantiatePrefab(prefab, bench);
                    live.name = profile.Module;
                    live.transform.SetParent(root.transform, true);
                    live.transform.position = new Vector3(x, 0f, 0f);
                    var storefront = live.GetComponent<Storefront>();
                    if (storefront == null) storefront = live.AddComponent<Storefront>();
                    storefront.ConfigurePreview(shutter);
                    storefront.SetPreviewState(StorefrontState.Open);
                    Label(root.transform, profile.Module + " · Open",
                        new Vector3(x - 2.5f, 4.2f, 0f));
                    report.LiveStorefronts++;
                }

                var statePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    SourceDir + "SM_Bld_Shop_01.prefab");
                if (statePrefab == null)
                    failures.Add("SM_Bld_Shop_01: state-row source prefab missing");
                else
                {
                    float stateStart = (span - (States.Length - 1) * 13f) * 0.5f;
                    for (int i = 0; i < States.Length; i++)
                    {
                        float x = stateStart + i * 13f;
                        var sample = (GameObject)PrefabUtility.InstantiatePrefab(statePrefab, bench);
                        sample.name = "SM_Bld_Shop_01 " + States[i];
                        sample.transform.SetParent(root.transform, true);
                        sample.transform.position = new Vector3(x, 0f, -10f);
                        var storefront = sample.AddComponent<Storefront>();
                        storefront.ConfigurePreview(shutter);
                        storefront.SetPreviewState(States[i]);
                        Label(root.transform, States[i].ToString(),
                            new Vector3(x - 2.5f, 4.2f, -10f));
                        report.StateExamples++;
                    }
                }

                Floor(root.transform, span * 0.5f, span + 18f);
                CameraAndLight(root.transform, span * 0.5f);
                EditorSceneManager.MarkSceneDirty(bench);
                if (!EditorSceneManager.SaveScene(bench, ScenePath))
                    failures.Add("could not save " + ScenePath);
            }
            catch (Exception exception)
            {
                failures.Add(exception.Message);
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded)
                    SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(bench, removeScene: true);
            }
            report.Failures = failures.ToArray();
            AssetDatabase.Refresh();
            return report;
        }

        static void Label(Transform parent, string text, Vector3 position)
        {
            var go = new GameObject("Label " + text);
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            var label = go.AddComponent<TextMesh>();
            label.text = text;
            label.fontSize = 42;
            label.characterSize = 0.12f;
            label.color = new Color(0.12f, 0.12f, 0.12f);
            label.anchor = TextAnchor.LowerLeft;
        }

        static void Floor(Transform parent, float centreX, float width)
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "review ground";
            floor.transform.SetParent(parent, false);
            floor.transform.position = new Vector3(centreX, -0.15f, 0f);
            floor.transform.localScale = new Vector3(width, 0.25f, 28f);
            var renderer = floor.GetComponent<Renderer>();
            if (renderer != null)
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(
                    "Assets/Synty/PolygonGeneric/Materials/Generic_Concrete.mat");
                if (material != null) renderer.sharedMaterial = material;
            }
        }

        static void CameraAndLight(Transform parent, float centreX)
        {
            var sun = new GameObject("Sun");
            sun.transform.SetParent(parent, false);
            sun.transform.rotation = Quaternion.Euler(48f, -28f, 0f);
            var light = sun.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.shadows = LightShadows.Soft;

            var cameraObject = new GameObject("Storefront review camera");
            cameraObject.transform.SetParent(parent, false);
            cameraObject.transform.position = new Vector3(centreX, 30f, 58f);
            cameraObject.transform.LookAt(new Vector3(centreX, 2.2f, 0f));
            var camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 55f;
            camera.farClipPlane = 220f;
            camera.clearFlags = CameraClearFlags.Skybox;
        }
    }
}
