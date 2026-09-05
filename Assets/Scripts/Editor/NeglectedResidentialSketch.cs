using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RoadDemo;
using TMPro;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LivingCity.EditorTools
{
    /// <summary>Add a translated comparison set without regenerating the normal blocks.</summary>
    public static class NeglectedResidentialSketch
    {
        public const string ScenePath = ResidentialSketch.DemoScene;
        public const string ComparisonRoot = "RESIDENTIAL NEGLECTED COMPARISON";
        const string LegacyScene = "Assets/Scenes/NeglectedResidentialDemo.unity";
        const string Materials = "Assets/Materials/ResidentialNeglect";
        public static bool Excluded(string name) =>
            name.IndexOf("police", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("nightclub", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("discotheque", StringComparison.OrdinalIgnoreCase) >= 0;

        [MenuItem("Tools/City/Residential/Add or Refresh Neglected Comparison Set", priority = 49)]
        public static void Menu() => Debug.Log(Build());

        [CliCommand("gangsters_residential_neglected", "Build the same Residential set with neglected dressing, excluding police and nightclub.", MainThreadRequired = true)]
        public static object Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play before generating the neglected set.");
            var active = SceneManager.GetActiveScene();
            var scene = SceneManager.GetSceneByPath(ScenePath);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            Scene legacy = default;
            GameObject staging = null;
            Directory.CreateDirectory(Materials);
            AssetDatabase.Refresh();
            var shader = Shader.Find("LivingCity/Residential Neglect");
            if (!shader) throw new InvalidOperationException("Residential Neglect shader was not imported.");
            try
            {
                SceneManager.SetActiveScene(scene);
                var map = new Dictionary<Material, Material>();
                Material Weather(Material source)
                {
                    if (map.TryGetValue(source, out var known)) return known;
                    bool synty = source.HasProperty("_Albedo_Map");
                    string textureProperty = synty ? "_Albedo_Map" : "_BaseMap";
                    var tex = source.GetTexture(textureProperty);
                    string identity = source.name + "_" + (tex ? tex.name : "plain");
                    string safe = string.Concat(identity.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
                    string path = Materials + "/" + safe + ".mat";
                    var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (!mat) { mat = new Material(source); AssetDatabase.CreateAsset(mat, path); }
                    else mat.CopyPropertiesFromMaterial(source);
                    mat.name = safe;
                    mat.shader = shader;
                    mat.SetTexture("_BaseMap", tex);
                    mat.SetTextureScale("_BaseMap", source.GetTextureScale(textureProperty));
                    mat.SetTextureOffset("_BaseMap", source.GetTextureOffset(textureProperty));
                    if (synty)
                    {
                        mat.shaderKeywords = Array.Empty<string>();
                        mat.SetColor("_BaseColor", Color.white);
                        mat.SetFloat("_AlphaClip", 1);
                        mat.SetFloat("_Cutoff", .5f);
                        mat.EnableKeyword("_ALPHATEST_ON");
                        mat.renderQueue = 2450;
                        if (source.HasProperty("_Normal_Map") && source.GetTexture("_Normal_Map"))
                        {
                            mat.SetTexture("_BumpMap", source.GetTexture("_Normal_Map"));
                            mat.EnableKeyword("_NORMALMAP");
                        }
                    }
                    EditorUtility.SetDirty(mat);
                    if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", .08f);
                    map[source] = mat;
                    return mat;
                }
                var timber = AssetDatabase.LoadAssetAtPath<Material>(Materials + "/WeatheredTimber.mat");
                if (!timber)
                {
                    timber = new Material(shader) { name = "WeatheredTimber", color = new Color(.34f,.24f,.14f) };
                    AssetDatabase.CreateAsset(timber, Materials + "/WeatheredTimber.mat");
                }
                var roots = scene.GetRootGameObjects();
                var originals = roots.Where(g => g.name.StartsWith("RESIDENTIAL ") &&
                    g.name != ComparisonRoot).ToArray();
                var expected = originals.Where(g => !Excluded(g.name)).ToArray();
                if (expected.Length == 0) throw new InvalidOperationException("No normal residential blocks found.");
                var oldSet = roots.FirstOrDefault(g => g.name == ComparisonRoot);
                bool migrate = oldSet == null && File.Exists(LegacyScene);
                GameObject[] sources = expected;
                if (migrate)
                {
                    legacy = EditorSceneManager.OpenPreviewScene(LegacyScene);
                    sources = legacy.GetRootGameObjects().Where(g => g.name.StartsWith("RESIDENTIAL ")).ToArray();
                    if (!sources.Select(g => g.name).OrderBy(n => n).SequenceEqual(
                            expected.Select(g => g.name).OrderBy(n => n)))
                    {
                        // The normal bench may have been regenerated since the separate set
                        // was built. Compare the current geometry instead of pairing old seeds.
                        EditorSceneManager.ClosePreviewScene(legacy);
                        legacy = default;
                        migrate = false;
                        sources = expected;
                    }
                }
                staging = new GameObject("Neglected comparison staging");
                SceneManager.MoveGameObjectToScene(staging, scene);
                var blocks = new List<GameObject>();
                var reports = new List<object>();
                for (int i = 0; i < sources.Length; i++)
                {
                    var block = UnityEngine.Object.Instantiate(sources[i]);
                    block.name = sources[i].name;
                    SceneManager.MoveGameObjectToScene(block, scene);
                    block.transform.SetParent(staging.transform, true);
                    blocks.Add(block);
                    if (migrate) continue;
                    foreach (var instance in block.GetComponentsInChildren<Transform>(true)
                        .Where(t => PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject)).ToArray())
                        if (PrefabUtility.IsPartOfPrefabInstance(instance.gameObject))
                            PrefabUtility.UnpackPrefabInstance(instance.gameObject, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                    var report = ResidentialNeglect.Apply(block, 198700 + i, Weather, timber,
                        (prefab, parent) => UnityEngine.Object.Instantiate(prefab, parent));
                    reports.Add(new { block = block.name, report.surfaces, report.boardedWindows, report.tags, report.litter });
                }
                Bounds normalBounds = BoundsOf(originals), copyBounds = BoundsOf(blocks);
                float offset = Mathf.Ceil((normalBounds.max.x + 45f - copyBounds.min.x) / 5f) * 5f;
                staging.transform.position = new Vector3(offset, 0, 0);
                staging.name = ComparisonRoot;
                if (oldSet) UnityEngine.Object.DestroyImmediate(oldSet);
                var camera = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Camera>()).First();
                var rig = camera.GetComponent<DemoCamera>() ?? camera.gameObject.AddComponent<DemoCamera>();
                rig.mapTransition = false; rig.mapCeiling = 1500; rig.yaw = 20; rig.showHint = false;
                rig.ConfigurePitch(45, 20);
                camera.fieldOfView = 45; camera.farClipPlane = 2000;
                var renderers = expected[0].GetComponentsInChildren<MeshRenderer>();
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                rig.pivot = new Vector3(bounds.center.x, 0, bounds.center.z);
                rig.FrameSpan(Mathf.Max(bounds.size.x, bounds.size.z), .95f, 75f);
                camera.transform.rotation = Quaternion.Euler(rig.pitch, rig.yaw, 0);
                camera.transform.position = rig.pivot - camera.transform.forward * rig.distance;
                var comparison = camera.GetComponent<ResidentialComparisonView>() ??
                    camera.gameObject.AddComponent<ResidentialComparisonView>();
                comparison.rig = rig;
                comparison.offset = new Vector3(offset, 0, 0);
                comparison.splitX = (normalBounds.max.x + copyBounds.min.x + offset) * .5f;
                normalBounds.Encapsulate(new Bounds(copyBounds.center + comparison.offset, copyBounds.size));
                comparison.allBounds = normalBounds;
                AddLabels(scene, comparison, BoundsOf(originals), BoundsOf(blocks));
                var shadows = camera.GetComponent<DemoShadows>() ?? camera.gameObject.AddComponent<DemoShadows>();
                shadows.rig = rig;
                foreach (var material in map.Values) AssetDatabase.SaveAssetIfDirty(material);
                AssetDatabase.SaveAssetIfDirty(timber);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save neglected set.");
                staging = null;
                return new { scene = ScenePath, normalBlocks = originals.Length, neglectedBlocks = blocks.Count, migrated = migrate, offset, reports };
            }
            finally
            {
                if (staging) UnityEngine.Object.DestroyImmediate(staging);
                if (legacy.IsValid()) EditorSceneManager.ClosePreviewScene(legacy);
                if (opened) EditorSceneManager.CloseScene(scene, true);
                if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
            }
        }
        [CliCommand("gangsters_residential_comparison_labels", "Label both sets in ResidentialDemo without regenerating blocks.", MainThreadRequired = true)]
        public static object LabelComparison()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play before saving comparison labels.");
            var scene = SceneManager.GetSceneByPath(ScenePath);
            if (!scene.IsValid() || !scene.isLoaded)
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            var roots = scene.GetRootGameObjects();
            var group = roots.First(g => g.name == ComparisonRoot);
            var originals = roots.Where(g => g.name.StartsWith("RESIDENTIAL ") && g != group);
            var comparison = roots.SelectMany(g => g.GetComponentsInChildren<ResidentialComparisonView>()).First();
            AddLabels(scene, comparison, BoundsOf(originals), BoundsOf(new[] { group }));
            var rig = comparison.rig;
            rig.pivot = new Vector3(comparison.allBounds.center.x, 0, comparison.allBounds.center.z);
            rig.FrameSpan(Mathf.Max(comparison.allBounds.size.x, comparison.allBounds.size.z), 1.5f);
            rig.transform.rotation = Quaternion.Euler(rig.pitch, rig.yaw, 0);
            rig.transform.position = rig.pivot - rig.transform.forward * rig.distance;
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save labels.");
            SceneManager.SetActiveScene(scene);
            return new { scene = ScenePath, labels = comparison.setLabels.Select(t => t.name).ToArray() };
        }

        static void AddLabels(Scene scene, ResidentialComparisonView comparison, Bounds normal, Bounds neglected)
        {
            const string labelRoot = "Residential comparison labels";
            var previous = scene.GetRootGameObjects().FirstOrDefault(g => g.name == labelRoot);
            if (previous) UnityEngine.Object.DestroyImmediate(previous);
            var root = new GameObject(labelRoot);
            SceneManager.MoveGameObjectToScene(root, scene);
            string materialPath = Materials + "/ComparisonLabelBackdrop.mat";
            var background = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (!background)
            {
                background = new Material(Shader.Find("Universal Render Pipeline/Unlit"))
                    { name = "ComparisonLabelBackdrop", color = new Color(.035f, .045f, .055f) };
                AssetDatabase.CreateAsset(background, materialPath);
            }
            Transform Label(string title, Bounds bounds, Color color)
            {
                var go = new GameObject(title);
                go.transform.SetParent(root.transform, false);
                go.transform.position = new Vector3(bounds.center.x, bounds.max.y + 24f, bounds.max.z + 12f);
                go.transform.rotation = Quaternion.Euler(comparison.rig.pitch, comparison.rig.yaw, 0);
                var label = go.AddComponent<TextMeshPro>();
                label.text = title;
                label.font = TMP_Settings.defaultFontAsset;
                label.fontStyle = FontStyles.Bold;
                label.fontSize = 150;
                label.enableAutoSizing = true;
                label.fontSizeMin = 90;
                label.fontSizeMax = 150;
                label.alignment = TextAlignmentOptions.Center;
                label.textWrappingMode = TextWrappingModes.NoWrap;
                label.color = color;
                float width = Mathf.Max(230, bounds.size.x - 20);
                label.rectTransform.sizeDelta = new Vector2(width, 28);
                var backing = GameObject.CreatePrimitive(PrimitiveType.Cube);
                backing.name = "Label background";
                backing.transform.SetParent(go.transform, false);
                backing.transform.localPosition = new Vector3(0, 0, .3f);
                backing.transform.localScale = new Vector3(width + 12, 30, .2f);
                backing.GetComponent<MeshRenderer>().sharedMaterial = background;
                UnityEngine.Object.DestroyImmediate(backing.GetComponent<Collider>());
                return go.transform;
            }
            comparison.setLabels = new[] {
                Label("NORMALNI BLOKOVI", normal, new Color(.8f, .93f, 1f)),
                Label("ZAPUSTENI BLOKOVI", neglected, new Color(1f, .83f, .56f))
            };
        }

        static Bounds BoundsOf(IEnumerable<GameObject> blocks)
        {
            var renderers = blocks.SelectMany(g => g.GetComponentsInChildren<MeshRenderer>(true)).ToArray();
            if (renderers.Length == 0) throw new InvalidOperationException("No block geometry found.");
            var result = renderers[0].bounds;
            foreach (var renderer in renderers) result.Encapsulate(renderer.bounds);
            return result;
        }
    }
}
