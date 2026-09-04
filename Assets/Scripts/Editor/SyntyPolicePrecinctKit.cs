using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RoadDemo;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// Lifts Synty's complete furnished precinct out of its vendor demo into one
    /// project-owned nested prefab.  This deliberately complements, rather than replaces,
    /// SyntyKitExtractor's lightweight baked building-policestation shell: the shell remains
    /// the streamed city representation, while this asset is the close/detail source with
    /// its ground floor, holding cells, upper floor and underground motor pool intact.
    /// </summary>
    public static class SyntyPolicePrecinctKit
    {
        const int Version = 1;
        const string SourceScenePath =
            "Assets/Synty/PolygonPoliceStation/Scenes/Demo.unity";
        const string OutputDir = "Assets/CityKit/PolicePrecinct";
        const string PrefabPath = OutputDir + "/police-precinct-full-detail.prefab";
        const string VersionPath = OutputDir + "/PolicePrecinctKitVersion.txt";
        const string ReviewScenePath = "Assets/Scenes/PolicePrecinctDemo.unity";
        const string RootName = "Police Precinct - Full Detail";
        const string SourceGradeGuid = "b33a7bb308e88794f971750d595256d9";

        const string PoliceProps =
            "Assets/Synty/PolygonPoliceStation/Prefabs/Props/";
        const string PoliceSigns =
            "Assets/Synty/PolygonPoliceStation/Prefabs/Signs/";
        const string BarrierPath =
            "Assets/Synty/PolygonPalmCity/Prefabs/Props/SM_Prop_Barrier_Gate_01.prefab";

        sealed class BuildParts
        {
            public GameObject site;
            public GameObject garage;
            public GameObject ground;
            public GameObject upper;
            public GameObject vehicles;
            public GameObject lighting;
            public Transform entrance;
            public Transform booking;
            public Transform cells;
            public Transform rampTop;
            public Transform rampBottom;
            public Transform[] barrierArms = Array.Empty<Transform>();
            public Vector3 barrierAxis = Vector3.forward;
            public float barrierLift = 75f;
        }

        public sealed class AuditResult
        {
            public bool passed;
            public string prefab;
            public string scene;
            public int renderers;
            public int authoredProps;
            public int lights;
            public int cellWalls;
            public int cellDoors;
            public int rampPieces;
            public int policeSigns;
            public int stagedVehicles;
            public int parkingBarriers;
            public float width;
            public float height;
            public float depth;
            public string[] failures = Array.Empty<string>();
        }

        [MenuItem("Tools/City/Police Precinct/Rebuild Full-Detail Prefab and Demo", priority = 1)]
        public static void RebuildAndOpen()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            BuildEverything();
        }

        [MenuItem("Tools/City/Police Precinct/Audit Full-Detail Prefab", priority = 2)]
        public static void AuditMenu()
        {
            var result = Audit();
            if (result.passed)
                Debug.Log($"[PolicePrecinct] PASS: {Describe(result)}");
            else
                Debug.LogError($"[PolicePrecinct] FAIL: {string.Join("; ", result.failures)}");
        }

        static AuditResult BuildEverything()
        {
            EnsureFolders();
            BuildDetailedPrefab();
            var scene = BuildReviewScene();
            File.WriteAllText(VersionPath, Version.ToString());
            AssetDatabase.ImportAsset(VersionPath);
            AssetDatabase.SaveAssets();

            var audit = Audit();
            audit.scene = scene.path;
            Debug.Log($"[PolicePrecinct] Built {PrefabPath} and {ReviewScenePath}: " +
                      Describe(audit));
            return audit;
        }

        static void BuildDetailedPrefab()
        {
            var previous = SceneManager.GetActiveScene();
            Scene source = default;
            Scene work = default;
            GameObject holder = null;
            try
            {
                work = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                SceneManager.SetActiveScene(work);
                source = EditorSceneManager.OpenScene(SourceScenePath, OpenSceneMode.Additive);
                SceneManager.SetActiveScene(work);

                holder = new GameObject(RootName);
                SceneManager.MoveGameObjectToScene(holder, work);

                var parts = new BuildParts
                {
                    site = CopyRoot(source, work, holder.transform, "Street",
                                    "00_SITE_STREET_AND_RAMP_ACCESS"),
                    garage = CopyRoot(source, work, holder.transform, "Garage",
                                      "10_UNDERGROUND_PARKING_AND_MOTOR_POOL"),
                    ground = CopyRoot(source, work, holder.transform, "BaseFloor",
                                      "20_GROUND_FLOOR_RECEPTION_BOOKING_AND_CELLS"),
                    upper = CopyRoot(source, work, holder.transform, "TopFloor",
                                     "30_UPPER_FLOOR_OFFICES_AND_OPERATIONS"),
                    vehicles = CopyRoot(source, work, holder.transform, "Vehicles",
                                        "40_EXTERIOR_POLICE_FLEET"),
                    lighting = CopyRoot(source, work, holder.transform, "Lighting (URP)",
                                        "50_INTERIOR_PRACTICAL_LIGHTING"),
                };

                OptimiseNestedDetail(parts);
                AddMarkersAndParkingSecurity(holder, parts);

                var visual = holder.AddComponent<PolicePrecinctVisual>();
                int props = CountNamed(holder.transform, "SM_Prop_") +
                            CountNamed(holder.transform, "SM_Sign_");
                int renderers = holder.GetComponentsInChildren<Renderer>(true).Length;
                int lights = holder.GetComponentsInChildren<Light>(true).Length;
                visual.Configure(
                    parts.site, parts.garage, parts.ground, parts.upper,
                    parts.vehicles, parts.lighting,
                    parts.entrance, parts.booking, parts.cells,
                    parts.rampTop, parts.rampBottom,
                    parts.barrierArms, parts.barrierAxis, parts.barrierLift,
                    props, renderers, lights);

                var saved = PrefabUtility.SaveAsPrefabAsset(holder, PrefabPath);
                if (saved == null)
                    throw new InvalidOperationException("Unity did not save the police precinct prefab.");
            }
            finally
            {
                if (holder != null) UnityEngine.Object.DestroyImmediate(holder);
                if (source.IsValid() && source.isLoaded)
                    EditorSceneManager.CloseScene(source, removeScene: true);
                if (work.IsValid() && work.isLoaded)
                    EditorSceneManager.CloseScene(work, removeScene: true);
                if (previous.IsValid() && previous.isLoaded)
                    SceneManager.SetActiveScene(previous);
            }
        }

        static GameObject CopyRoot(
            Scene source, Scene work, Transform parent, string sourceName, string outputName)
        {
            var original = source.GetRootGameObjects().FirstOrDefault(go => go.name == sourceName);
            if (original == null)
                throw new InvalidOperationException(
                    $"Police Station source scene is missing its '{sourceName}' root.");

            var copy = UnityEngine.Object.Instantiate(original);
            copy.name = outputName;
            if (copy.scene != work)
                SceneManager.MoveGameObjectToScene(copy, work);
            copy.transform.SetParent(parent, worldPositionStays: true);
            copy.transform.SetPositionAndRotation(original.transform.position,
                                                  original.transform.rotation);
            copy.transform.localScale = original.transform.localScale;
            return copy;
        }

        /// <summary>
        /// The source carries roughly eight hundred pieces.  Tiny desk dressing does not
        /// need rigid bodies, colliders or motion vectors; removing those physics/render
        /// costs keeps the requested density viable without flattening the nested prefab
        /// hierarchy into the 171 MB mesh the city extractor deliberately rejected.
        /// </summary>
        static void OptimiseNestedDetail(BuildParts parts)
        {
            foreach (var root in new[] { parts.site, parts.garage, parts.ground,
                                         parts.upper, parts.vehicles })
            {
                if (root == null) continue;
                foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                    GameObjectUtility.RemoveMonoBehavioursWithMissingScript(transform.gameObject);

                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                    if (UnderNamedGroup(renderer.transform, "SmallProps"))
                        renderer.shadowCastingMode = ShadowCastingMode.Off;
                }

                foreach (var group in root.GetComponentsInChildren<Transform>(true)
                                          .Where(t => t.name == "SmallProps"))
                {
                    foreach (var collider in group.GetComponentsInChildren<Collider>(true))
                        UnityEngine.Object.DestroyImmediate(collider);
                    foreach (var body in group.GetComponentsInChildren<Rigidbody>(true))
                        UnityEngine.Object.DestroyImmediate(body);
                }
            }

            if (parts.lighting == null) return;
            foreach (var light in parts.lighting.GetComponentsInChildren<Light>(true))
            {
                if (light.type == LightType.Directional)
                {
                    UnityEngine.Object.DestroyImmediate(light.gameObject);
                    continue;
                }
                light.shadows = LightShadows.None;
                light.lightmapBakeType = LightmapBakeType.Mixed;
                light.renderMode = LightRenderMode.Auto;
            }
        }

        static bool UnderNamedGroup(Transform transform, string group)
        {
            for (var at = transform; at != null; at = at.parent)
                if (at.name == group) return true;
            return false;
        }

        static void AddMarkersAndParkingSecurity(GameObject holder, BuildParts parts)
        {
            if (!TryRampEnds(parts.garage, out var rampTop, out var rampBottom))
            {
                rampTop = new Vector3(-27f, 0f, -6f);
                rampBottom = new Vector3(-27f, -3f, -24f);
            }

            AddParkingSecurity(holder.transform, rampTop, rampBottom,
                               out parts.barrierArms,
                               out parts.barrierAxis,
                               out parts.barrierLift);

            var markers = new GameObject("90_FUNCTIONAL_PLACES");
            markers.transform.SetParent(holder.transform, false);

            var stationSign = FindFirst(holder.transform, "SM_Sign_Police_Station_01");
            var bookingSign = FindFirst(holder.transform, "SM_Sign_Booking_01");
            var cellsSign = FindFirst(holder.transform, "SM_Sign_Cells_01");
            Bounds building = BoundsOf(parts.ground, parts.upper);

            var doors = parts.ground.GetComponentsInChildren<Transform>(true)
                .Where(t => t.name.StartsWith("SM_Bld_Wall_Entrance", StringComparison.Ordinal) ||
                            t.name.StartsWith("SM_Bld_Entrance", StringComparison.Ordinal))
                .ToArray();
            Vector3 signAt = stationSign != null
                ? stationSign.position
                : new Vector3(building.center.x, 0f, building.min.z);
            var door = doors.OrderBy(t => FlatDistanceSquared(t.position, signAt)).FirstOrDefault();
            Vector3 doorAt = door != null ? door.position : signAt;
            var outward = doorAt - building.center;
            outward.y = 0f;
            if (outward.sqrMagnitude < 0.01f) outward = Vector3.back;
            outward.Normalize();
            doorAt += outward * 1.1f;
            doorAt.y = Mathf.Max(0.02f, doorAt.y);

            parts.entrance = Marker(markers.transform, "PUBLIC ENTRANCE", doorAt, outward);
            parts.booking = Marker(markers.transform, "BOOKING DESK",
                bookingSign != null ? bookingSign.position :
                (cellsSign != null ? cellsSign.position - Vector3.right * 4f : building.center),
                Vector3.forward);
            parts.cells = Marker(markers.transform, "MINI HOLDING CELLS",
                cellsSign != null ? cellsSign.position : building.center,
                Vector3.forward);
            parts.rampTop = Marker(markers.transform, "GARAGE RAMP TOP", rampTop,
                                   Flat(rampBottom - rampTop));
            parts.rampBottom = Marker(markers.transform, "GARAGE RAMP BOTTOM", rampBottom,
                                      Flat(rampTop - rampBottom));
        }

        static void AddParkingSecurity(
            Transform parent, Vector3 rampTop, Vector3 rampBottom,
            out Transform[] arms, out Vector3 localAxis, out float lift)
        {
            var group = new GameObject("05_PARKING_SECURITY_AND_BARRIERS").transform;
            group.SetParent(parent, false);

            var downhill = Flat(rampBottom - rampTop);
            if (downhill.sqrMagnitude < 0.01f) downhill = Vector3.back;
            downhill.Normalize();
            var right = Vector3.Cross(Vector3.up, downhill).normalized;
            float yaw = Mathf.Atan2(downhill.x, downhill.z) * Mathf.Rad2Deg;
            var gatePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BarrierPath);
            if (gatePrefab == null)
                throw new FileNotFoundException("Missing shared parking barrier", BarrierPath);

            var gateBounds = FreewayKit.Measure(gatePrefab);
            bool armAlongX = Mathf.Abs(gateBounds.center.x) >= Mathf.Abs(gateBounds.center.z);
            var rootAxis = armAlongX ? Vector3.forward : Vector3.right;
            lift = armAlongX
                ? (gateBounds.center.x >= 0f ? 75f : -75f)
                : (gateBounds.center.z >= 0f ? -75f : 75f);

            var found = new List<Transform>(2);
            Vector3 gateLine = rampTop - downhill * 1.25f;
            gateLine.y = Mathf.Max(0f, rampTop.y);
            foreach (float lane in new[] { -2.5f, 2.5f })
            {
                var laneCentre = gateLine + right * lane;
                // Face the two posts toward opposite kerbs, then derive each root
                // position from the prefab's measured centre so both authored arms
                // actually span their lane instead of merely standing beside it.
                float laneYaw = yaw + (lane < 0f ? 180f : 0f);
                var gateRotation = Quaternion.Euler(0f, laneYaw, 0f);
                var gateCentre = new Vector3(gateBounds.center.x, 0f,
                                             gateBounds.center.z);
                var post = laneCentre - gateRotation * gateCentre;
                var gate = SitPrefab(gatePrefab, post, laneYaw, group,
                                     lane < 0f ? "INBOUND SECURITY BARRIER" :
                                                 "OUTBOUND SECURITY BARRIER");
                var arm = FreewayKit.BoomOf(gate.transform);
                if (arm != null) found.Add(arm);
            }

            localAxis = found.Count > 0
                ? Quaternion.Inverse(found[0].localRotation) * rootAxis
                : rootAxis;
            arms = found.ToArray();

            PlacePrefab(PoliceProps + "SM_Prop_Height_Clearance_01.prefab",
                        gateLine + downhill * 0.5f, yaw, group, "GARAGE HEIGHT CLEARANCE");
            PlacePrefab(PoliceSigns + "SM_Sign_Parking_01.prefab",
                        gateLine + right * 5.6f + Vector3.up * 0.05f,
                        yaw + 180f, group, "SECURE POLICE PARKING SIGN");
            PlacePrefab(PoliceProps + "SM_Prop_Intercom_01.prefab",
                        gateLine + right * 5.1f + Vector3.up * 1.05f,
                        yaw + 180f, group, "GARAGE INTERCOM");
            PlacePrefab(PoliceProps + "SM_Prop_Security_Camera_01.prefab",
                        gateLine - downhill * 1.2f + right * 5.1f + Vector3.up * 3.2f,
                        yaw + 210f, group, "RAMP SECURITY CAMERA");

            for (int i = -1; i <= 1; i += 2)
            {
                PlacePrefab(PoliceProps + "SM_Prop_Road_Spikes_01_Closed_01.prefab",
                            gateLine + downhill * 1.3f + right * (i * 2.5f),
                            yaw, group, "RETRACTED TYRE SPIKES");
                PlacePrefab(PoliceProps + "SM_Prop_Cone_01.prefab",
                            gateLine - downhill * 2f + right * (i * 5.1f),
                            0f, group, "RAMP SAFETY CONE");
            }
        }

        static GameObject SitPrefab(
            GameObject prefab, Vector3 surface, float yaw, Transform parent, string name)
        {
            var bounds = FreewayKit.Measure(prefab);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.name = name;
            go.transform.SetPositionAndRotation(
                surface - Vector3.up * bounds.min.y,
                Quaternion.Euler(0f, yaw, 0f));
            return go;
        }

        static GameObject PlacePrefab(
            string path, Vector3 position, float yaw, Transform parent, string name)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                throw new FileNotFoundException("Missing police precinct detail", path);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.name = name;
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            return go;
        }

        static bool TryRampEnds(GameObject garage, out Vector3 top, out Vector3 bottom)
        {
            top = bottom = default;
            if (garage == null) return false;
            var points = new List<Vector3>();
            foreach (var ramp in garage.GetComponentsInChildren<Transform>(true)
                                        .Where(t => t.name.StartsWith("SM_Env_Road_Ramp_",
                                                                     StringComparison.Ordinal)))
            {
                foreach (var filter in ramp.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (filter.sharedMesh == null) continue;
                    var vertices = filter.sharedMesh.vertices;
                    for (int i = 0; i < vertices.Length; i++)
                        points.Add(filter.transform.TransformPoint(vertices[i]));
                }
            }
            if (points.Count == 0) return false;

            float low = points.Min(p => p.y);
            float high = points.Max(p => p.y);
            const float lip = 0.18f;
            var highPoints = points.Where(p => p.y >= high - lip).ToArray();
            var lowPoints = points.Where(p => p.y <= low + lip).ToArray();
            if (highPoints.Length == 0 || lowPoints.Length == 0) return false;
            top = Average(highPoints);
            bottom = Average(lowPoints);
            return true;
        }

        static Vector3 Average(IReadOnlyList<Vector3> points)
        {
            var sum = Vector3.zero;
            for (int i = 0; i < points.Count; i++) sum += points[i];
            return points.Count > 0 ? sum / points.Count : Vector3.zero;
        }

        static Transform Marker(Transform parent, string name, Vector3 at, Vector3 forward)
        {
            var marker = new GameObject(name).transform;
            marker.SetParent(parent, false);
            marker.position = at;
            forward = Flat(forward);
            if (forward.sqrMagnitude > 0.001f)
                marker.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
            return marker;
        }

        static Transform FindFirst(Transform root, string prefix) =>
            root.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t => t.name.StartsWith(prefix, StringComparison.Ordinal));

        static float FlatDistanceSquared(Vector3 a, Vector3 b)
        {
            a.y = b.y = 0f;
            return (a - b).sqrMagnitude;
        }

        static Vector3 Flat(Vector3 value)
        {
            value.y = 0f;
            return value;
        }

        static Scene BuildReviewScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects,
                                                    NewSceneMode.Single);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
                throw new FileNotFoundException("Police precinct prefab was not built", PrefabPath);
            var precinct = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            precinct.name = RootName;
            precinct.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            var bounds = BoundsOf(precinct);
            BuildReviewGround(bounds);
            ConfigureSun();
            ConfigureGrade();
            ConfigureReflectionProbe(bounds);
            var rig = ConfigureCamera();

            var showcase = new GameObject("Police Precinct Review Controls")
                .AddComponent<PolicePrecinctShowcase>();
            showcase.Configure(precinct.GetComponent<PolicePrecinctVisual>(), rig);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ReviewScenePath))
                throw new IOException("Unity did not save " + ReviewScenePath);
            Selection.activeGameObject = precinct;
            return scene;
        }

        static void BuildReviewGround(Bounds bounds)
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Review Ground Below Underground Garage";
            ground.transform.position = new Vector3(bounds.center.x, bounds.min.y - 0.08f,
                                                    bounds.center.z);
            ground.transform.localScale = new Vector3(
                Mathf.Max(12f, (bounds.size.x + 30f) / 10f), 1f,
                Mathf.Max(12f, (bounds.size.z + 30f) / 10f));
            var material = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Materials/Weapons/Demo Ground.mat");
            if (material != null)
                ground.GetComponent<MeshRenderer>().sharedMaterial = material;
            ground.isStatic = true;
        }

        static void ConfigureSun()
        {
            var light = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None)
                .FirstOrDefault(candidate => candidate.type == LightType.Directional);
            if (light == null) light = new GameObject("Sun").AddComponent<Light>();
            light.name = "Precinct Sun";
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.93f, 0.82f);
            light.intensity = 1.15f;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.82f;
            light.transform.rotation = Quaternion.Euler(48f, -34f, 0f);
            RenderSettings.sun = light;
            RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.52f, 0.61f, 0.72f);
            RenderSettings.ambientEquatorColor = new Color(0.31f, 0.34f, 0.39f);
            RenderSettings.ambientGroundColor = new Color(0.11f, 0.12f, 0.14f);
            RenderSettings.ambientIntensity = 0.92f;
            DynamicGI.UpdateEnvironment();
        }

        static void ConfigureGrade()
        {
            string path = AssetDatabase.GUIDToAssetPath(SourceGradeGuid);
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null) return;
            var volume = new GameObject("Police Precinct Cinematic Grade").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;
            volume.sharedProfile = profile;
        }

        static void ConfigureReflectionProbe(Bounds bounds)
        {
            var go = new GameObject("Precinct Reflection Probe");
            go.transform.position = bounds.center + Vector3.up * 2f;
            var probe = go.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.OnAwake;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.IndividualFaces;
            probe.resolution = 128;
            probe.boxProjection = true;
            probe.size = bounds.size + new Vector3(12f, 12f, 12f);
            probe.nearClipPlane = 0.3f;
            probe.farClipPlane = 220f;
        }

        static DemoCamera ConfigureCamera()
        {
            var camera = UnityEngine.Object.FindAnyObjectByType<Camera>();
            if (camera == null) camera = new GameObject("Main Camera").AddComponent<Camera>();
            camera.name = "Police Precinct Review Camera";
            camera.tag = "MainCamera";
            camera.fieldOfView = 44f;
            camera.nearClipPlane = 0.18f;
            camera.farClipPlane = 420f;
            camera.clearFlags = CameraClearFlags.Skybox;

            var data = camera.GetUniversalAdditionalCameraData();
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.renderPostProcessing = true;
            if (camera.GetComponent<AudioListener>() == null)
                camera.gameObject.AddComponent<AudioListener>();

            var rig = camera.GetComponent<DemoCamera>() ??
                      camera.gameObject.AddComponent<DemoCamera>();
            rig.pivot = new Vector3(-8f, 0.7f, -5f);
            rig.distance = 70f;
            rig.yaw = 202f;
            rig.ConfigurePitch(41f, 18f);
            rig.minDistance = 4f;
            rig.mapTransition = false;
            rig.mapCeiling = 165f;
            rig.showHint = true;
            rig.showZoom = true;
            rig.hintTopPx = 12f;
            rig.hint = "POLICE PRECINCT   1 exterior   2 ground floor   3 cells   " +
                       "4 underground parking   B barrier   L lights   R reset   " +
                       "WASD move   Q/E or right-drag rotate   wheel zoom";
            var rotation = Quaternion.Euler(rig.pitch, rig.yaw, 0f);
            camera.transform.SetPositionAndRotation(
                rig.pivot + rotation * new Vector3(0f, 0f, -rig.distance), rotation);

            var shadows = camera.GetComponent<DemoShadows>() ??
                          camera.gameObject.AddComponent<DemoShadows>();
            shadows.rig = rig;
            DemoCamera.ClaimMainCamera(camera);
            return rig;
        }

        public static AuditResult Audit()
        {
            var failures = new List<string>();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                return new AuditResult
                {
                    passed = false,
                    prefab = PrefabPath,
                    scene = ReviewScenePath,
                    failures = new[] { "detailed prefab is missing" },
                };
            }

            var visual = prefab.GetComponent<PolicePrecinctVisual>();
            if (visual == null) failures.Add("PolicePrecinctVisual topology is missing");
            int renderers = prefab.GetComponentsInChildren<Renderer>(true).Length;
            int props = CountNamed(prefab.transform, "SM_Prop_") +
                        CountNamed(prefab.transform, "SM_Sign_");
            int lights = prefab.GetComponentsInChildren<Light>(true).Length;
            int cellWalls = CountNamed(prefab.transform, "SM_Bld_Wall_Cell_");
            int cellDoors = CountNamed(prefab.transform, "SM_Bld_Wall_Door_Cell_");
            int ramps = CountNamed(prefab.transform, "SM_Env_Road_Ramp_");
            int signs = CountNamed(prefab.transform, "SM_Sign_Police_Station_");
            int vehicles = CountNamed(prefab.transform, "SM_Veh_");
            int barriers = visual != null ? visual.ParkingBarrierArms.Length : 0;
            Bounds bounds = BoundsOf(prefab);

            if (renderers < 500) failures.Add($"only {renderers} renderers; furnished source was not retained");
            if (props < 450) failures.Add($"only {props} props/signs; props-madness layer is incomplete");
            if (cellWalls < 4) failures.Add($"only {cellWalls} cell-wall modules");
            if (cellDoors < 1) failures.Add("holding-cell door is missing");
            if (ramps < 2) failures.Add($"only {ramps} underground ramp pieces");
            if (signs < 1) failures.Add("POLICE STATION sign is missing");
            if (vehicles < 4) failures.Add($"only {vehicles} staged police vehicles");
            if (barriers < 2) failures.Add($"only {barriers} working parking barriers");
            if (lights < 12) failures.Add($"only {lights} interior practical lights");
            if (visual != null && (visual.HoldingCells == null || visual.GarageRampTop == null ||
                                   visual.GarageRampBottom == null || visual.PublicEntrance == null))
                failures.Add("one or more functional place markers are missing");
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ReviewScenePath) == null)
                failures.Add("PolicePrecinctDemo scene is missing");

            return new AuditResult
            {
                passed = failures.Count == 0,
                prefab = PrefabPath,
                scene = ReviewScenePath,
                renderers = renderers,
                authoredProps = props,
                lights = lights,
                cellWalls = cellWalls,
                cellDoors = cellDoors,
                rampPieces = ramps,
                policeSigns = signs,
                stagedVehicles = vehicles,
                parkingBarriers = barriers,
                width = Round(bounds.size.x),
                height = Round(bounds.size.y),
                depth = Round(bounds.size.z),
                failures = failures.ToArray(),
            };
        }

        static string Describe(AuditResult result) =>
            $"passed={result.passed}, {result.width:F1} x {result.height:F1} x " +
            $"{result.depth:F1} m, {result.renderers} renderers, " +
            $"{result.authoredProps} props/signs, {result.cellDoors} cell doors, " +
            $"{result.rampPieces} ramp pieces, {result.parkingBarriers} working barriers, " +
            $"{result.lights} practical lights";

        static int CountNamed(Transform root, string prefix) =>
            root.GetComponentsInChildren<Transform>(true)
                .Count(t => t.name.StartsWith(prefix, StringComparison.Ordinal));

        static Bounds BoundsOf(params GameObject[] roots)
        {
            var renderers = roots.Where(root => root != null)
                .SelectMany(root => root.GetComponentsInChildren<Renderer>(true))
                .Where(renderer => renderer != null)
                .ToArray();
            if (renderers.Length == 0) return new Bounds(Vector3.zero, Vector3.one);
            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        static float Round(float value) => Mathf.Round(value * 10f) / 10f;

        static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder("Assets/CityKit"))
                AssetDatabase.CreateFolder("Assets", "CityKit");
            if (!AssetDatabase.IsValidFolder(OutputDir))
                AssetDatabase.CreateFolder("Assets/CityKit", "PolicePrecinct");
        }

        static string DirtyScene()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.IsValid() && scene.isLoaded && scene.isDirty)
                    return string.IsNullOrEmpty(scene.path) ? scene.name : scene.path;
            }
            return null;
        }

        [CliCommand("gangsters_police_precinct",
                    "Build the full furnished Police Precinct prefab and its dedicated " +
                    "interactive review scene, then audit cells, garage, ramp, signage, " +
                    "barriers, lighting and prop density.",
                    MainThreadRequired = true,
                    Tags = new[] { "gangsters", "police", "precinct", "prefab", "scene" })]
        public static object BuildFromCli()
        {
            if (EditorApplication.isPlaying)
                return new { passed = false, reason = "Leave Play Mode before building the precinct." };
            string dirty = DirtyScene();
            if (!string.IsNullOrEmpty(dirty))
                return new { passed = false, reason = $"The open scene {dirty} has unsaved changes." };
            return BuildEverything();
        }

        [CliCommand("gangsters_police_precinct_audit",
                    "Audit the generated Police Precinct prefab without rebuilding it.",
                    MainThreadRequired = true,
                    Tags = new[] { "gangsters", "police", "precinct", "audit" })]
        public static object AuditFromCli() => Audit();
    }
}
