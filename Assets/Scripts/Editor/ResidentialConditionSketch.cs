using System;
using System.Collections.Generic;
using System.Linq;
using RoadDemo;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace LivingCity.EditorTools
{
    public static class ResidentialConditionSketch
    {
        const string Folder = "Assets/CityKit/ResidentialCondition";
        const string CatalogPath = "Assets/Resources/ResidentialConditionCatalog.asset";
        public static bool Excluded(string name) => name.IndexOf("police", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("nightclub", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("discotheque", StringComparison.OrdinalIgnoreCase) >= 0;

        [MenuItem("Tools/City/Residential/Prepare Dynamic Condition", priority = 49)]
        public static void Menu() => Debug.Log(Build());
        [CliCommand("gangsters_residential_condition", "Replace the comparison copies with dynamic condition controls on the original blocks.", MainThreadRequired = true)]
        public static object Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Leave Play before editing the scene.");
            BuildCatalog();
            var scene = SceneManager.GetSceneByPath(ResidentialSketch.DemoScene);
            if (!scene.IsValid() || !scene.isLoaded) scene = EditorSceneManager.OpenScene(ResidentialSketch.DemoScene, OpenSceneMode.Additive);
            Configure(scene);
            bool saved = EditorSceneManager.SaveScene(scene, ResidentialSketch.DemoScene);
            return new { saved, scene = ResidentialSketch.DemoScene, mode = "One set, dynamic neglect + shared Settings density" };
        }

        public static void Configure(Scene scene)
        {
            var roots = scene.GetRootGameObjects();
            foreach (var root in roots)
                if (root.name == "RESIDENTIAL NEGLECTED COMPARISON" || root.name == "Residential comparison labels") Object.DestroyImmediate(root);
            var camera = roots.Where(g => g).SelectMany(g => g.GetComponentsInChildren<Camera>(true)).First();
            foreach (var old in camera.GetComponents<ResidentialComparisonView>()) Object.DestroyImmediate(old);
            var rig = camera.GetComponent<DemoCamera>();
            // A freshly generated scene has only Unity's default Camera. The runtime
            // adapter cannot install controls until AFTER this authoring pass saves it.
            if (!rig)
            {
                rig = camera.gameObject.AddComponent<DemoCamera>();
                var view = CityViewConfig.Resolve();
                rig.mapAt = CameraGrounding.BoomForHeight(view.Max3DDistance, view.StreetPitch);
                rig.ConfigurePitch(view.StreetPitch, view.PitchFreedom);
                rig.yaw = 20f;
                camera.fieldOfView = 45f;
            }
            var shadows = camera.GetComponent<DemoShadows>() ?? camera.gameObject.AddComponent<DemoShadows>();
            shadows.rig = rig;
            var demo = camera.GetComponent<ResidentialConditionDemo>() ?? camera.gameObject.AddComponent<ResidentialConditionDemo>();
            demo.blocks = scene.GetRootGameObjects().Where(g => g.name.StartsWith("RESIDENTIAL ") && !Excluded(g.name)).ToArray();
            demo.neglect = 0;
            bool first = true;
            var all = new Bounds();
            foreach (var block in demo.blocks)
                foreach (var renderer in block.GetComponentsInChildren<MeshRenderer>())
                {
                    if (first) { all = renderer.bounds; first = false; } else all.Encapsulate(renderer.bounds);
                }
            demo.allBounds = all;
            rig.showHint = false; rig.mapTransition = false;
            rig.mapCeiling = Mathf.Max(260f, Mathf.Max(all.size.x, all.size.z) * 1.35f);
            camera.farClipPlane = Mathf.Max(camera.farClipPlane, rig.mapCeiling * 3f);
            if (demo.blocks.Length > 0)
            {
                var renderers = demo.blocks[0].GetComponentsInChildren<MeshRenderer>();
                var bounds = renderers[0].bounds;
                foreach (var r in renderers) bounds.Encapsulate(r.bounds);
                rig.pivot = new Vector3(bounds.center.x, 0, bounds.center.z);
                rig.FrameSpan(Mathf.Max(bounds.size.x, bounds.size.z), 1.05f, 75);
                camera.transform.rotation = Quaternion.Euler(rig.pitch, rig.yaw, 0);
                camera.transform.position = rig.pivot - camera.transform.forward * rig.distance;
            }
            EditorSceneManager.MarkSceneDirty(scene);
        }

        public static void BuildCatalog()
        {
            System.IO.Directory.CreateDirectory(Folder);
            System.IO.Directory.CreateDirectory("Assets/Resources");
            AssetDatabase.Refresh();
            var catalog = AssetDatabase.LoadAssetAtPath<ResidentialConditionCatalog>(CatalogPath);
            if (!catalog) { catalog = ScriptableObject.CreateInstance<ResidentialConditionCatalog>(); AssetDatabase.CreateAsset(catalog, CatalogPath); }
            catalog.weatherShader = Shader.Find("LivingCity/Residential Neglect");
            if (!catalog.weatherShader) throw new InvalidOperationException("Condition shader missing.");
            var prefabs = new List<GameObject>();
            string[] sources = {
                "Assets/Synty/PolygonCity/Prefabs/Props/SM_Prop_TrashBag_01.prefab",
                "Assets/Synty/PolygonPalmCity/Prefabs/Props/SM_Prop_Junk_Cardboard_01.prefab",
                "Assets/Synty/PolygonPoliceStation/Prefabs/Props/SM_Prop_Bottle_Small_01.prefab",
                "Assets/Synty/PolygonGangWarfare/Prefabs/Weapons/SM_Wep_BrokenBottle_01.prefab"
            };
            for (int i = 0; i < sources.Length; i++) prefabs.Add(BakeProp(sources[i], i >= 2));
            var glass = Material("BottleGlass", new Color(.21f, .39f, .3f), .7f);
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            var rng = new System.Random(1987);
            for (int i = 0; i < 9; i++)
            {
                var p = new Vector3((float)rng.NextDouble() * .6f - .3f, .004f + i * .0002f, (float)rng.NextDouble() * .4f - .2f);
                int n = vertices.Count;
                vertices.Add(p); vertices.Add(p + new Vector3(.045f, 0, .12f)); vertices.Add(p + new Vector3(.09f, .009f, .015f));
                triangles.Add(n); triangles.Add(n + 1); triangles.Add(n + 2);
            }
            prefabs.Add(MeshPrefab("BrokenGlass", vertices, triangles, glass));
            var shutterMat = Material("ShutterMetal", new Color(.22f, .23f, .21f), .2f);
            vertices.Clear(); triangles.Clear();
            // Unit-width measured facade insert: corrugation is baked once, not rebuilt by sliders.
            for (int i = 0; i < 24; i++)
            {
                float y0 = -.5f + i / 24f, y1 = -.5f + (i + 1) / 24f;
                float z0 = (i & 1) == 0 ? -.45f : .45f, z1 = -z0;
                int n = vertices.Count;
                vertices.Add(new Vector3(-.5f, y0, z0)); vertices.Add(new Vector3(.5f, y0, z0));
                vertices.Add(new Vector3(-.5f, y1, z1)); vertices.Add(new Vector3(.5f, y1, z1));
                triangles.Add(n); triangles.Add(n + 1); triangles.Add(n + 2);
                triangles.Add(n + 2); triangles.Add(n + 1); triangles.Add(n + 3);
            }
            catalog.shutter = MeshPrefab("ClosedShutter", vertices, triangles, shutterMat);
            catalog.litter = prefabs.ToArray();
            EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssetIfDirty(catalog);
        }
        static Material Material(string name, Color colour, float smoothness)
        {
            string path = Folder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material) { material = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material, path); }
            material.color = colour; material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Cull", 0);
            EditorUtility.SetDirty(material); AssetDatabase.SaveAssetIfDirty(material); return material;
        }
        static GameObject MeshPrefab(string name, List<Vector3> vertices, List<int> triangles, Material material)
        {
            string meshPath = Folder + "/" + name + ".asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (!mesh) { mesh = new Mesh(); AssetDatabase.CreateAsset(mesh, meshPath); }
            mesh.Clear(); mesh.name = name; mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); EditorUtility.SetDirty(mesh); AssetDatabase.SaveAssetIfDirty(mesh);
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            try
            {
                go.GetComponent<MeshFilter>().sharedMesh = mesh; go.GetComponent<MeshRenderer>().sharedMaterial = material;
                go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                return PrefabUtility.SaveAsPrefabAsset(go, Folder + "/" + name + ".prefab");
            }
            finally { Object.DestroyImmediate(go); }
        }
        static GameObject BakeProp(string path, bool fallen)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!source) throw new InvalidOperationException("Missing cosmetic source: " + path);
            var copy = Object.Instantiate(source); var clean = new GameObject(source.name + "_Cosmetic");
            try
            {
                copy.transform.SetPositionAndRotation(Vector3.zero, fallen ? Quaternion.Euler(0, 0, 90) : Quaternion.identity);
                var bounds = new Bounds(); bool first = true;
                foreach (var filter in copy.GetComponentsInChildren<MeshFilter>())
                {
                    var renderer = filter.GetComponent<MeshRenderer>();
                    if (!renderer || !renderer.enabled) continue;
                    var part = new GameObject(filter.name, typeof(MeshFilter), typeof(MeshRenderer)); part.transform.SetParent(clean.transform, false);
                    part.transform.SetPositionAndRotation(filter.transform.position, filter.transform.rotation); part.transform.localScale = filter.transform.lossyScale;
                    part.GetComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                    var mr = part.GetComponent<MeshRenderer>(); mr.sharedMaterials = renderer.sharedMaterials;
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    if (first) { bounds = mr.bounds; first = false; } else bounds.Encapsulate(mr.bounds);
                }
                foreach (Transform part in clean.transform) part.position -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
                return PrefabUtility.SaveAsPrefabAsset(clean, Folder + "/" + clean.name + ".prefab");
            }
            finally { Object.DestroyImmediate(copy); Object.DestroyImmediate(clean); }
        }
    }
}
