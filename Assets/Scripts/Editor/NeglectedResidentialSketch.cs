using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RoadDemo;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LivingCity.EditorTools
{
    /// <summary>Build a second set from the saved, manually refined Residential composition.
    /// Rebuild only the derived scene. The source scene and linked prefabs are never saved.</summary>
    public static class NeglectedResidentialSketch
    {
        public const string ScenePath = "Assets/Scenes/NeglectedResidentialDemo.unity";
        const string Materials = "Assets/Materials/ResidentialNeglect";
        public static bool Excluded(string name) =>
            name.IndexOf("police", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("nightclub", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("discotheque", StringComparison.OrdinalIgnoreCase) >= 0;

        [MenuItem("Tools/City/Residential/Build Neglected Set (exclude police and nightclub)", priority = 49)]
        public static void Menu() => Debug.Log(Build());

        [CliCommand("gangsters_residential_neglected", "Build the same Residential set with neglected dressing, excluding police and nightclub.", MainThreadRequired = true)]
        public static object Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play before generating the neglected set.");
            var old = SceneManager.GetSceneByPath(ScenePath);
            if (old.IsValid() && old.isLoaded)
                throw new InvalidOperationException("Close the derived NeglectedResidentialDemo before rebuilding it.");
            var active = SceneManager.GetActiveScene();
            Directory.CreateDirectory(Materials);
            AssetDatabase.Refresh();
            var shader = Shader.Find("LivingCity/Residential Neglect");
            if (!shader) throw new InvalidOperationException("Residential Neglect shader was not imported.");
            // Copy the exact saved hierarchy, including prefab overrides and block positions.
            File.Copy(ResidentialSketch.DemoScene, ScenePath, true);
            AssetDatabase.ImportAsset(ScenePath, ImportAssetOptions.ForceSynchronousImport);
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
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
                int sourceBlocks = roots.Count(g => g.name.StartsWith("RESIDENTIAL "));
                var removed = new List<string>();
                foreach (var root in roots)
                    if (root.name.StartsWith("RESIDENTIAL ") && Excluded(root.name))
                    { removed.Add(root.name); UnityEngine.Object.DestroyImmediate(root); }
                var blocks = scene.GetRootGameObjects().Where(g => g.name.StartsWith("RESIDENTIAL ")).ToArray();
                var reports = new List<object>();
                for (int i = 0; i < blocks.Length; i++)
                {
                    var block = blocks[i];
                    // Editable copy; overrides apply only to this derived scene.
                    foreach (var instance in block.GetComponentsInChildren<Transform>(true)
                        .Where(t => PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject)).ToArray())
                        if (PrefabUtility.IsPartOfPrefabInstance(instance.gameObject))
                            PrefabUtility.UnpackPrefabInstance(instance.gameObject, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                    var report = ResidentialNeglect.Apply(block, 198700 + i, Weather, timber,
                        (prefab, parent) => UnityEngine.Object.Instantiate(prefab, parent));
                    reports.Add(new { block = block.name, report.surfaces, report.boardedWindows, report.tags, report.litter });
                }
                var camera = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Camera>()).First();
                var rig = camera.GetComponent<DemoCamera>() ?? camera.gameObject.AddComponent<DemoCamera>();
                rig.mapTransition = false; rig.mapCeiling = 700; rig.yaw = 20; rig.showHint = true;
                rig.ConfigurePitch(45, 20);
                camera.fieldOfView = 45; camera.farClipPlane = 2000;
                var renderers = blocks[0].GetComponentsInChildren<MeshRenderer>();
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                rig.pivot = new Vector3(bounds.center.x, 0, bounds.center.z);
                rig.FrameSpan(Mathf.Max(bounds.size.x, bounds.size.z), .95f, 75f);
                camera.transform.rotation = Quaternion.Euler(rig.pitch, rig.yaw, 0);
                camera.transform.position = rig.pivot - camera.transform.forward * rig.distance;
                var shadows = camera.GetComponent<DemoShadows>() ?? camera.gameObject.AddComponent<DemoShadows>();
                shadows.rig = rig;
                foreach (var material in map.Values) AssetDatabase.SaveAssetIfDirty(material);
                AssetDatabase.SaveAssetIfDirty(timber);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save neglected set.");
                return new { scene = ScenePath, sourceBlocks, blocks = blocks.Length, removed, reports };
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
            }
        }
    }
}
