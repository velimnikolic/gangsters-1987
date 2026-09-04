using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LivingCity.Data;
using RoadDemo;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// Replaces the old seven-panel storefront pretending to be a fire station with one
    /// measured Synty compound: a 30 x 15 m three-bay engine hall and a 12 x 12 m,
    /// three-storey crew wing. The combined shell is baked back over the canonical prefab
    /// path so saved references keep their GUID, while the legacy city palette reference is
    /// removed because Core's shared FireStationBlock now owns the complete amenity.
    /// </summary>
    public static class SyntyFireStationKit
    {
        const int Version = 2;
        const string VersionPath =
            "Assets/CityKit/Buildings/FireStationKitVersion.txt";
        const string PrefabPath = FireStationBlock.ShellPath;
        const string DatabasePath = "Assets/Configs/PrefabDatabase.asset";
        const string ReviewScenePath = "Assets/Scenes/FireStationDemo.unity";
        const string MaterialDir = "Assets/CityKit/FireStation";
        const string WhitePaintPath = MaterialDir + "/firestation-white.mat";
        const string RedPaintPath = MaterialDir + "/firestation-red.mat";

        const string Bld = "Assets/Synty/PolygonGangWarfare/Prefabs/Buildings/";
        const string Props = "Assets/Synty/PolygonGangWarfare/Prefabs/Props/";
        const string BrickWall = Bld + "SM_Bld_Wall_Brick_01.prefab";
        const string BrickWindow = Bld + "SM_Bld_Wall_Brick_Window_01.prefab";
        const string BrickWindowB = Bld + "SM_Bld_Wall_Brick_Window_02.prefab";
        const string BrickWindowC = Bld + "SM_Bld_Wall_Brick_Window_03.prefab";
        const string BrickDoor = Bld + "SM_Bld_Wall_Brick_Door_01.prefab";
        const string BrickCorner = Bld + "SM_Bld_Wall_Brick_Exterior_Corner_01.prefab";
        const string RoofOpen = Bld + "SM_Bld_Roof_Flat_Open_01.prefab";
        const string RoofEdge = Bld + "SM_Bld_Roof_Flat_Straight_01.prefab";
        const string RoofL = Bld + "SM_Bld_Roof_Flat_L_01.prefab";
        const string WallLight = Props + "SM_Prop_Warehouse_WallLight_01.prefab";
        const string FireHose = Props + "SM_Prop_Warehouse_Firehose_01.prefab";
        const string Camera = Props + "SM_Prop_Security_Camera_01.prefab";
        const string AirVent = Props + "SM_Prop_AirVent_02.prefab";
        const string FireDepartmentSign =
            "Assets/Synty/PolygonCity/Prefabs/Props/SM_Prop_Sign_FireDepartment_01.prefab";

        const float Module = 3f;
        const float Course = 3f;
        const float Face = 0.197f;

        enum Side { Front, Right, Back, Left }

        [MenuItem("Tools/City/Fire Station/Rebuild Synty Fire Station", priority = 1)]
        public static void ForceBuild()
        {
            AssetDatabase.DeleteAsset(VersionPath);
            BuildIfStale();
        }

        [MenuItem("Tools/City/Fire Station/Rebuild and Open Review Scene", priority = 2)]
        public static void RebuildAndOpenReviewScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            AssetDatabase.DeleteAsset(VersionPath);
            BuildIfStale();
            BuildReviewScene();
        }

        public static void BuildIfStale()
        {
            EnsureMaterials();
            var marker = AssetDatabase.LoadAssetAtPath<TextAsset>(VersionPath);
            if (!marker || marker.text.Trim() != Version.ToString() ||
                AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
            {
                SyntyBakeUtil.ClearCache();
                BuildShell();
                File.WriteAllText(VersionPath, Version.ToString());
                AssetDatabase.ImportAsset(VersionPath);
            }

            ScrubLegacyDatabase();
            AssetDatabase.SaveAssets();
        }

        static void BuildShell()
        {
            var root = new GameObject("building-firestation");
            try
            {
                BuildEngineHall(root.transform);
                BuildCrewWing(root.transform);
                var pivot = SyntyKitExtractor.BakeGroup(root, "building-firestation", yaw: 0f);
                if (!pivot.HasValue)
                    throw new InvalidOperationException("The fire-station shell contained no renderable Synty pieces.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        static void BuildEngineHall(Transform compound)
        {
            const int width = 10, depth = 5;
            var hall = new GameObject("Three-Bay Engine Hall");
            hall.transform.SetParent(compound, false);
            hall.transform.localPosition = new Vector3(-6f, 0f, 0f);

            WallRing(hall, width, depth, 0,
                // The shutters are no longer baked into the shell. FireStationBlock hangs
                // them as separate top-pivoted panels so its runtime can open real portals.
                Row((BrickWall, 1), (null, 2), (BrickWall, 1),
                    (null, 2), (BrickWall, 1), (null, 2), (BrickWall, 1)),
                new[] { BrickWall, BrickWall, BrickWindow, BrickWall, BrickWindow,
                        BrickWindow, BrickWall, BrickWindow, BrickWall, BrickWall },
                new[] { BrickWall, BrickWindow, BrickWall, BrickWindow, BrickWall });
            // The 6 x 6 roller-door portals span both courses; nulls preserve their openings.
            WallRing(hall, width, depth, 1,
                Row((BrickWall, 1), (null, 2), (BrickWindow, 1),
                    (null, 2), (BrickWindowB, 1), (null, 2), (BrickWall, 1)),
                new[] { BrickWall, BrickWindow, BrickWindowB, BrickWindowC, BrickWindow,
                        BrickWindowB, BrickWindowC, BrickWindow, BrickWindowB, BrickWall },
                new[] { BrickWall, BrickWindow, BrickWindowB, BrickWindow, BrickWall });
            CornerPosts(hall, width, depth, 2);
            FlatRoof(hall, width, depth, 2f * Course);

            foreach (float x in new[] { -9f, 0f, 9f })
                WallProp(hall, WallLight, width, depth, Side.Front, x, 5.25f);
            WallProp(hall, FireHose, width, depth, Side.Front, -4.5f, 0f);
            WallProp(hall, FireHose, width, depth, Side.Front, 4.5f, 0f);
            WallProp(hall, Camera, width, depth, Side.Front, 13.2f, 5.45f);
            Place(hall, AirVent, -9f, 6.08f, -2f, 0f);
            Place(hall, AirVent, 6f, 6.08f, -2f, 0f);
        }

        static void BuildCrewWing(Transform compound)
        {
            const int width = 4, depth = 4, floors = 3;
            var wing = new GameObject("Firefighters' Crew Wing");
            wing.transform.SetParent(compound, false);
            wing.transform.localPosition = new Vector3(15f, 0f, 1.5f);

            WallRing(wing, width, depth, 0,
                Row((BrickWindow, 1), (BrickDoor, 1),
                    (BrickWindowB, 1), (BrickWall, 1)),
                new[] { BrickWall, BrickWindow, BrickWindowB, BrickWall },
                new[] { BrickWall, BrickWindow, BrickWindowB, BrickWall });
            WallRing(wing, width, depth, 1,
                Row((BrickWindowB, 1), (BrickWindowC, 1),
                    (BrickWindow, 1), (BrickWindowB, 1)),
                new[] { BrickWall, BrickWindowC, BrickWindow, BrickWall },
                new[] { BrickWall, BrickWindowC, BrickWindow, BrickWall });
            WallRing(wing, width, depth, 2,
                // A six-metre blind brick panel gives the department sign a deliberate
                // fascia instead of laying white letters across two busy window grids.
                Row((BrickWindowC, 1), (BrickWall, 1),
                    (BrickWall, 1), (BrickWindowC, 1)),
                new[] { BrickWall, BrickWindow, BrickWindowC, BrickWall },
                new[] { BrickWall, BrickWindowB, BrickWindowC, BrickWall });
            CornerPosts(wing, width, depth, floors);
            FlatRoof(wing, width, depth, floors * Course);

            // This is Synty's actual FIRE DEPARTMENT sign, not generated text.
            var sign = WallProp(wing, FireDepartmentSign, width, depth, Side.Front,
                                0f, 7.8f, standoff: 0.06f);
            if (sign != null) sign.transform.localScale *= 1.32f;
            WallProp(wing, WallLight, width, depth, Side.Front, -1.5f, 2.5f);
            WallProp(wing, Camera, width, depth, Side.Front, 5.15f, 8.15f);
            WallProp(wing, FireHose, width, depth, Side.Right, -2.6f, 0f);
            Place(wing, AirVent, -2.5f, 9.08f, -1.5f, 0f);
            Place(wing, AirVent, 2f, 9.08f, 1.5f, 90f);
        }

        static (string path, int modules)[] Row(
            params (string path, int modules)[] row) => row;

        /// <summary>GangWarfare's 3 m walls pivot at their high-x end (local x -3..0).</summary>
        static void WallRing(GameObject root, int width, int depth, int floor,
                             (string path, int modules)[] front,
                             string[] back, string[] sides)
        {
            float halfWidth = width * Module * 0.5f;
            float halfDepth = depth * Module * 0.5f;
            float y = floor * Course;

            float cursor = -halfWidth;
            foreach (var (path, modules) in front)
            {
                Place(root, path, cursor + modules * Module, y, halfDepth, 0f);
                cursor += modules * Module;
            }
            for (int i = 0; i < width; i++)
                Place(root, back[i], -halfWidth + i * Module, y, -halfDepth, 180f);
            for (int i = 0; i < depth; i++)
            {
                float z = -halfDepth + i * Module;
                Place(root, sides[depth - 1 - i], halfWidth, y, z, 90f);
                Place(root, sides[depth - 1 - i], -halfWidth, y, z + Module, 270f);
            }
        }

        static void CornerPosts(GameObject root, int width, int depth, int floors)
        {
            float halfWidth = width * Module * 0.5f;
            float halfDepth = depth * Module * 0.5f;
            for (int floor = 0; floor < floors; floor++)
            {
                float y = floor * Course;
                Place(root, BrickCorner, halfWidth, y, halfDepth, 0f);
                Place(root, BrickCorner, halfWidth, y, -halfDepth, 90f);
                Place(root, BrickCorner, -halfWidth, y, -halfDepth, 180f);
                Place(root, BrickCorner, -halfWidth, y, halfDepth, 270f);
            }
        }

        static void FlatRoof(GameObject root, int width, int depth, float y)
        {
            float halfWidth = width * Module * 0.5f;
            float halfDepth = depth * Module * 0.5f;
            for (int i = 0; i < width; i++)
                for (int j = 0; j < depth; j++)
                {
                    float x = -halfWidth + i * Module;
                    float z = -halfDepth + j * Module;
                    bool left = i == 0, right = i == width - 1;
                    bool back = j == 0, front = j == depth - 1;

                    if (front && left) Place(root, RoofL, x + Module, y, z + Module, 0f);
                    else if (front && right) Place(root, RoofL, x + Module, y, z, 90f);
                    else if (back && right) Place(root, RoofL, x, y, z, 180f);
                    else if (back && left) Place(root, RoofL, x, y, z + Module, 270f);
                    else if (front) Place(root, RoofEdge, x + Module, y, z + Module, 0f);
                    else if (right) Place(root, RoofEdge, x + Module, y, z, 90f);
                    else if (back) Place(root, RoofEdge, x, y, z, 180f);
                    else if (left) Place(root, RoofEdge, x, y, z + Module, 270f);
                    else Place(root, RoofOpen, x + Module, y, z + Module, 0f);
                }
        }

        static GameObject WallProp(GameObject root, string path, int width, int depth, Side side,
                                   float along, float y, float standoff = 0f)
        {
            float front = depth * Module * 0.5f + Face + standoff;
            float flank = width * Module * 0.5f + Face + standoff;
            switch (side)
            {
                case Side.Front: return Place(root, path, along, y, front, 0f);
                case Side.Right: return Place(root, path, flank, y, along, 90f);
                case Side.Back: return Place(root, path, along, y, -front, 180f);
                default: return Place(root, path, -flank, y, along, 270f);
            }
        }

        static GameObject Place(GameObject root, string path,
                                float x, float y, float z, float yaw)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                throw new FileNotFoundException("Missing fire-station Synty module", path);
            var piece = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
            piece.transform.SetLocalPositionAndRotation(
                new Vector3(x, y, z), Quaternion.Euler(0f, yaw, 0f));
            return piece;
        }

        static void EnsureMaterials()
        {
            if (!AssetDatabase.IsValidFolder(MaterialDir))
                AssetDatabase.CreateFolder("Assets/CityKit", "FireStation");
            EnsureMaterial(WhitePaintPath, new Color(0.86f, 0.86f, 0.82f));
            EnsureMaterial(RedPaintPath, new Color(0.64f, 0.035f, 0.025f));
        }

        static void EnsureMaterial(string path, Color colour)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                    throw new InvalidOperationException("URP Lit shader is unavailable.");
                material = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", colour);
            material.SetFloat("_Smoothness", 0.04f);
            EditorUtility.SetDirty(material);
        }

        /// <summary>Remove the canonical GUID from every old storefront/landmark slot. Keeping
        /// it there after replacing a 17 m shop with a 42 m compound would make the legacy
        /// generator overlap a whole street.</summary>
        static int ScrubLegacyDatabase()
        {
            var database = AssetDatabase.LoadAssetAtPath<PrefabDatabase>(DatabasePath);
            if (database == null) return 0;
            int removed = 0;

            if (database.zonePalettes != null)
                foreach (var palette in database.zonePalettes)
                {
                    if (palette == null) continue;
                    palette.landmarks = WithoutFireStation(palette.landmarks, ref removed);
                    if (palette.groups == null) continue;
                    foreach (var group in palette.groups)
                    {
                        if (group == null) continue;
                        group.prefabs = WithoutFireStation(group.prefabs, ref removed);
                        group.rearPrefabs = WithoutFireStation(group.rearPrefabs, ref removed);
                        group.cornerPrefabs = WithoutFireStation(group.cornerPrefabs, ref removed);
                    }
                }
            database.uniqueBuildings = WithoutFireStation(database.uniqueBuildings, ref removed);

            if (removed > 0)
            {
                EditorUtility.SetDirty(database);
                Debug.Log($"[FireStation] Removed {removed} legacy storefront/unique reference(s) " +
                          "from PrefabDatabase; Core now owns the complete station block.");
            }
            return removed;
        }

        static GameObject[] WithoutFireStation(GameObject[] source, ref int removed)
        {
            if (source == null || source.Length == 0) return source ?? Array.Empty<GameObject>();
            var kept = new List<GameObject>(source.Length);
            for (int i = 0; i < source.Length; i++)
            {
                var item = source[i];
                if (item != null && AssetDatabase.GetAssetPath(item) == PrefabPath)
                {
                    removed++;
                    continue;
                }
                kept.Add(item);
            }
            return kept.Count == source.Length ? source : kept.ToArray();
        }

        static Scene BuildReviewScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects,
                                                    NewSceneMode.Single);
            var root = new GameObject("Generated Synty Fire Station").transform;
            var stood = FireStationBlock.Compose(root,
                (prefab, parent) => (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent));

            BuildReviewGround();
            ConfigureLight();
            ConfigureCamera();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ReviewScenePath);
            Selection.activeGameObject = root.gameObject;
            Debug.Log($"[FireStation] Built {ReviewScenePath}: {stood}", root.gameObject);
            return scene;
        }

        static void BuildReviewGround()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Review Ground";
            ground.transform.position = new Vector3(0f, -0.08f, 0f);
            ground.transform.localScale = new Vector3(10f, 1f, 8f);
            var material = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Materials/Weapons/Demo Ground.mat");
            if (material != null) ground.GetComponent<MeshRenderer>().sharedMaterial = material;
            ground.isStatic = true;

            // A simple street outside the apron makes the +Z frontage unambiguous in the
            // review scene without introducing a second road or traffic implementation.
            var road = GameObject.CreatePrimitive(PrimitiveType.Plane);
            road.name = "Front Street";
            road.transform.position = new Vector3(0f, -0.045f, 23.5f);
            road.transform.localScale = new Vector3(5f, 1f, 1.2f);
            var roadMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Synty/PolygonCity/Materials/Misc/Road_01.mat");
            if (roadMaterial != null) road.GetComponent<MeshRenderer>().sharedMaterial = roadMaterial;
            road.isStatic = true;
        }

        static void ConfigureLight()
        {
            var light = UnityEngine.Object.FindAnyObjectByType<Light>();
            if (light == null)
                light = new GameObject("Sun").AddComponent<Light>();
            light.name = "Sun";
            light.type = LightType.Directional;
            light.intensity = 1.25f;
            light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.56f, 0.63f, 0.72f);
            RenderSettings.ambientEquatorColor = new Color(0.34f, 0.36f, 0.39f);
            RenderSettings.ambientGroundColor = new Color(0.17f, 0.16f, 0.15f);
        }

        static void ConfigureCamera()
        {
            var camera = UnityEngine.Object.FindAnyObjectByType<UnityEngine.Camera>();
            if (camera == null)
                camera = new GameObject("Main Camera").AddComponent<UnityEngine.Camera>();
            camera.name = "Fire Station Review Camera";
            camera.tag = "MainCamera";
            camera.fieldOfView = 42f;
            camera.nearClipPlane = 0.2f;
            camera.farClipPlane = 500f;

            var rig = camera.GetComponent<DemoCamera>() ?? camera.gameObject.AddComponent<DemoCamera>();
            rig.pivot = new Vector3(0f, 2.5f, -1.5f);
            rig.distance = 72f;
            rig.yaw = 205f;
            rig.pitch = 38f;
            rig.ConfigurePitch(38f, 16f);
            rig.minDistance = 12f;
            rig.mapTransition = false;
            rig.mapCeiling = 180f;
            rig.showHint = true;
            rig.showZoom = true;
            rig.hintTopPx = 12f;
            rig.hint = "FIRE STATION   WASD/arrows: move   Q/E or right-drag: rotate   wheel: zoom";
            var rotation = Quaternion.Euler(rig.pitch, rig.yaw, 0f);
            camera.transform.SetPositionAndRotation(
                rig.pivot + rotation * new Vector3(0f, 0f, -rig.distance), rotation);
            DemoCamera.ClaimMainCamera(camera);
        }

        [CliCommand("gangsters_firestation",
                    "Rebuild the Synty fire-station shell, remove its legacy storefront " +
                    "references, build FireStationDemo and run its paper contracts.",
                    MainThreadRequired = true,
                    Tags = new[] { "gangsters", "core", "firestation" })]
        public static object BuildFromCli()
        {
            if (EditorApplication.isPlaying)
                return new { passed = false, reason = "Leave Play Mode before rebuilding the fire station." };

            AssetDatabase.DeleteAsset(VersionPath);
            BuildIfStale();
            var scene = BuildReviewScene();
            var failures = LivingCity.Tests.FireStationTests.Run();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Bounds bounds = FireStationBlock.BoundsOf(prefab);
            return new
            {
                passed = prefab != null && failures.Count == 0,
                prefab = PrefabPath,
                scene = scene.path,
                shellMetres = new
                {
                    x = Mathf.Round(bounds.size.x * 10f) / 10f,
                    y = Mathf.Round(bounds.size.y * 10f) / 10f,
                    z = Mathf.Round(bounds.size.z * 10f) / 10f,
                },
                blockMetres = new { frontage = 50, depth = 35 },
                failures = failures.ToArray(),
            };
        }

        [CliCommand("gangsters_firestation_tests",
                    "Run the pure parcel and seed contracts for the generated Core fire station.",
                    MainThreadRequired = true,
                    Tags = new[] { "gangsters", "core", "firestation", "tests" })]
        public static object RunTestsFromCli()
        {
            var failures = LivingCity.Tests.FireStationTests.Run();
            return new { passed = failures.Count == 0, failures = failures.ToArray(), seed = 1987 };
        }

        [CliCommand("gangsters_residential_firestation",
                    "Add or refresh the full functional fire-station block in the existing " +
                    "ResidentialDemo without rebuilding its residential blocks.",
                    MainThreadRequired = true,
                    Tags = new[] { "gangsters", "residential", "firestation" })]
        public static object AddToResidentialDemoFromCli()
        {
            if (EditorApplication.isPlaying)
                return new { passed = false, reason = "Leave Play Mode before editing ResidentialDemo." };

            var active = SceneManager.GetActiveScene();
            if (active.IsValid() && active.isDirty && active.path != ResidentialSketch.DemoScene)
                return new
                {
                    passed = false,
                    reason = $"The active scene {active.path} has unsaved changes; it was left untouched.",
                };

            BuildIfStale();
            var scene = active.path == ResidentialSketch.DemoScene
                ? active
                : EditorSceneManager.OpenScene(ResidentialSketch.DemoScene, OpenSceneMode.Single);
            const int seed = 36902;
            string report = ResidentialSketch.RefreshFireStationBlock(scene, seed);
            bool saved = EditorSceneManager.SaveScene(scene);
            var runtime = scene.GetRootGameObjects()
                .Select(root => root.GetComponent<FireStationBlockRuntime>())
                .FirstOrDefault(component => component != null);
            int operationalProps = 0;
            var detail = runtime != null
                ? runtime.transform.Find("Fire Station Operational Detail")
                : null;
            if (detail != null)
                foreach (Transform cluster in detail)
                    operationalProps += cluster.childCount;
            var at = runtime != null ? runtime.transform.position : Vector3.zero;
            return new
            {
                passed = saved && runtime != null && runtime.ConfiguredEngineCount == 2 &&
                         runtime.ConfiguredDoorCount == 3 && operationalProps >= 40,
                scene = scene.path,
                saved,
                root = runtime != null ? runtime.gameObject.name : null,
                fireEngineRoutes = runtime != null ? runtime.ConfiguredEngineCount : 0,
                workingHangarDoors = runtime != null ? runtime.ConfiguredDoorCount : 0,
                operationalProps,
                position = new { x = at.x, y = at.y, z = at.z },
                report,
            };
        }

        [CliCommand("gangsters_firestation_focus",
                    "Point the live ResidentialDemo camera at the fire-station hangars for " +
                    "visual animation review. Play Mode only; nothing is saved.",
                    MainThreadRequired = true,
                    Tags = new[] { "gangsters", "residential", "firestation", "capture" })]
        public static object FocusLiveFireStationFromCli()
        {
            if (!EditorApplication.isPlaying)
                return new { passed = false, reason = "Enter Play Mode before focusing the hangars." };

            var station = UnityEngine.Object.FindFirstObjectByType<FireStationBlockRuntime>();
            var camera = UnityEngine.Camera.main ??
                         UnityEngine.Object.FindFirstObjectByType<UnityEngine.Camera>();
            if (station == null || camera == null)
                return new { passed = false, reason = "Live fire station or camera was not found." };

            var rig = camera.GetComponent<DemoCamera>();
            if (rig == null)
                return new { passed = false, reason = "The live camera has no shared DemoCamera rig." };

            var pivot = station.transform.TransformPoint(new Vector3(-6f, 2f, -1.5f));
            rig.pivot = pivot;
            rig.distance = 47f;
            rig.yaw = station.transform.eulerAngles.y + 180f;
            rig.pitch = 27f;
            rig.mapTransition = false;
            var rotation = Quaternion.Euler(rig.pitch, rig.yaw, 0f);
            camera.transform.SetPositionAndRotation(
                rig.pivot + rotation * new Vector3(0f, 0f, -rig.distance), rotation);
            DemoCamera.ClaimMainCamera(camera);
            return new
            {
                passed = true,
                camera = camera.name,
                station = station.name,
                pivot = new { x = pivot.x, y = pivot.y, z = pivot.z },
            };
        }
    }
}
