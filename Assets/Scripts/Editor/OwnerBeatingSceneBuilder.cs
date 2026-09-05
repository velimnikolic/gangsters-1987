using System;
using RoadDemo;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace CityEditor
{
    public static class OwnerBeatingSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/OwnerBeatingDemo.unity";
        const string Folder = "Assets/OwnerBeatingDemo";
        [MenuItem("Tools/City/Build Owner Beating Scene")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play before building the scene.");
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                var root = new GameObject("Owner beating - reusable street scene");
                var road = Material("Asphalt", new Color(0.13f, 0.14f, 0.15f));
                var pavement = Material("Pavement", new Color(0.49f, 0.47f, 0.43f));
                var paint = Material("Road paint", new Color(0.73f, 0.66f, 0.43f));
                Box(root.transform, "Street", new Vector3(0f, -0.12f, 11f), new Vector3(32f, 0.2f, 10f), road);
                Box(root.transform, "Broad sidewalk", new Vector3(0f, 0.02f, 5f), new Vector3(30f, 0.22f, 6f), pavement);
                Box(root.transform, "Kerb", new Vector3(0f, 0.04f, 8f), new Vector3(30f, 0.27f, 0.18f), pavement);
                for (int i = -3; i <= 3; i++)
                    Box(root.transform, "Lane marking", new Vector3(i * 4.5f, -0.009f, 12f), new Vector3(2.3f, 0.014f, 0.1f), paint);
                var shop = Prefab("Assets/Synty/PolygonTown/Prefabs/Buildings/SM_Bld_Shop_01.prefab", root.transform,
                    Vector3.zero, 0f);
                shop.name = "Corner grocery";
                Prefab("Assets/Synty/PolygonTown/Prefabs/Buildings/SM_Bld_Shop_01.prefab", root.transform,
                    new Vector3(-8.3f, 0f, 0f), 0f);
                Prefab("Assets/Synty/PolygonTown/Prefabs/Buildings/SM_Bld_Shop_01.prefab", root.transform,
                    new Vector3(8.3f, 0f, 0f), 0f);
                Prefab("Assets/Synty/PolygonTown/Prefabs/Props/SM_Prop_ShopCounter_01.prefab", shop,
                    new Vector3(1.6f, 0.15f, 0f), 180f);
                Prefab("Assets/Synty/PolygonTown/Prefabs/Props/SM_Prop_ShopShelf_Wall_01.prefab", shop,
                    new Vector3(-2.4f, 0.15f, -2.7f), 0f);
                var man = Prefab("Assets/Synty/PolygonGangWarfare/Prefabs/Character/SM_Chr_Italian_Gangster_01.prefab",
                    root.transform, new Vector3(2.5f, 0.15f, 7.1f), -135f);
                man.name = "Enforcer";
                var owner = Prefab("Assets/Synty/PolygonTown/Prefabs/Characters/SM_Chr_ShopKeeper_01.prefab",
                    root.transform, new Vector3(0.2f, 0.15f, 1.7f), 0f);
                owner.name = "Shop owner";
                var control = new GameObject("Beating sequence"); control.transform.SetParent(root.transform);
                var beat = control.AddComponent<OwnerBeatingSequence>();
                beat.walk = CrewKit.StockWalk; beat.idle = CrewKit.StockIdle;
                beat.threat = CrewKit.SpeakGestures[0];
                beat.jab = CrewKit.UalClip("Punch_Jab"); beat.cross = CrewKit.UalClip("Punch_Cross");
                beat.combination = Mixamo("Jab Cross");
                beat.knee = Mixamo("Illegal Knee");
                beat.hitHead = Mixamo("Big Hit To Head");
                beat.hitBody = Mixamo("Big Hit To Head (1)");
                beat.hitHeavy = Mixamo("Big Hit To Head (2)");
                beat.fall = Mixamo("Kick To The Groin");
                beat.recover = Mixamo("Getting Up"); beat.crawl = Mixamo("Crawling");
                beat.impacts = new AudioClip[4];
                for (int i = 0; i < 4; i++) beat.impacts[i] = AssetDatabase.LoadAssetAtPath<AudioClip>(
                    "Assets/Audio/Weapons/punch_" + (i + 1) + ".wav");
                var demo = control.AddComponent<OwnerBeatingDemo.OwnerBeatingDemo>();
                demo.sequence = beat; demo.gangster = man; demo.owner = owner; demo.shop = shop;
                demo.inside = Anchor(root.transform, "Inside", new Vector3(0f, 0.15f, 2.7f));
                demo.outside = Anchor(root.transform, "Door approach", new Vector3(0f, 0.15f, 5.15f));
                demo.street = Anchor(root.transform, "Street confrontation", new Vector3(0f, 0.15f, 7f));
                // Serialized references keep the actors and clips available in a player build.
                PrefabUtility.SaveAsPrefabAsset(root, Folder + "/OwnerBeatingSet.prefab");
                var key = new GameObject("Late afternoon sun").AddComponent<Light>();
                key.type = LightType.Directional; key.intensity = 1.3f;
                key.color = new Color(1f, 0.88f, 0.7f); key.shadows = LightShadows.Soft;
                key.transform.rotation = Quaternion.Euler(48f, 145f, 0f);
                var fill = new GameObject("Soft storefront fill").AddComponent<Light>();
                fill.type = LightType.Point; fill.range = 16f; fill.intensity = 3f;
                fill.color = new Color(1f, 0.9f, 0.79f);
                fill.transform.position = new Vector3(0f, 3.8f, 8f);
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.58f, 0.59f, 0.62f);
                RenderSettings.fog = false;
                var camera = new GameObject("Main Camera") { tag = "MainCamera" };
                var cam = camera.AddComponent<Camera>(); cam.fieldOfView = 44f;
                cam.nearClipPlane = 0.08f; cam.farClipPlane = 150f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.22f, 0.27f, 0.32f);
                camera.AddComponent<AudioListener>();
                var orbit = camera.AddComponent<DemoCamera>();
                orbit.pivot = new Vector3(0f, 0.85f, 5.6f); orbit.distance = 8f;
                orbit.yaw = 125f; orbit.ConfigurePitch(22f, 16f);
                orbit.minDistance = 3f; orbit.mapCeiling = 60f; orbit.mapTransition = false;
                orbit.showHint = true; orbit.hintTopPx = 12f;
                orbit.hint = "WASD / arrows: move   Q/E / right-drag: orbit   Wheel: zoom\nR: replay   Space: pause   1: normal speed   2: slow motion";
                camera.transform.position = orbit.pivot + Quaternion.Euler(orbit.pitch, orbit.yaw, 0f) * (Vector3.back * orbit.distance);
                camera.transform.LookAt(orbit.pivot);
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid()) SceneManager.SetActiveScene(previous);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[OwnerBeatingDemo] Built " + ScenePath);
        }
        static AnimationClip Mixamo(string name)
        {
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(OwnerBeatingAnimationImport.Root + name + ".fbx"))
                if (asset is AnimationClip clip && !clip.name.StartsWith("__")) return clip;
            throw new InvalidOperationException("Missing owner animation: " + name);
        }
        static Transform Anchor(Transform parent, string name, Vector3 at)
        { var go = new GameObject(name); go.transform.SetParent(parent); go.transform.position = at; return go.transform; }
        static Transform Prefab(string path, Transform parent, Vector3 at, float yaw)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (source == null) throw new InvalidOperationException("Missing scene asset: " + path);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(source, parent);
            go.transform.localPosition = at; go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            return go.transform;
        }
        static Material Material(string name, Color color)
        {
            string path = Folder + "/" + name.Replace(" ", "") + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, path); }
            mat.color = color; mat.SetFloat("_Smoothness", 0.15f); EditorUtility.SetDirty(mat); return mat;
        }
        static void Box(Transform parent, string name, Vector3 at, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name;
            go.transform.SetParent(parent); go.transform.localPosition = at; go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = mat;
        }
    }
}
