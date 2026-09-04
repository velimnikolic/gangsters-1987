using System;
using System.Linq;
using RoadDemo;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// Non-destructive ResidentialDemo integration for the compact precinct block. It
    /// never regenerates the residential recipes: the precinct is inserted into the
    /// second row and the existing car-yard root is translated just far enough to make
    /// the normal fifteen-metre street between them.
    /// </summary>
    public static class ResidentialPolicePrecinctSketch
    {
        const string ScenePath = ResidentialSketch.DemoScene;
        const string RootPrefix = "RESIDENTIAL police precinct compact ordinary block";
        const string GymPrefix = "RESIDENTIAL gym ";
        const string CarYardPrefix = "RESIDENTIAL caryard ";
        const float StreetGap = 15f;
        const float FurnitureOverhangAllowance = 1.5f;
        const int Seed = 87041;

        public sealed class Result
        {
            public bool passed;
            public bool saved;
            public bool refreshed;
            public string scene;
            public string root;
            public Point position;
            public Point carYardShift;
            public float leftStreet;
            public float rightStreet;
            public float crossStreet;
            public int renderers;
            public int propsAndSigns;
            public int holdingCellWalls;
            public int holdingCellDoors;
            public int frontWallModules;
            public int rearWallModules;
            public int leftWallModules;
            public int rightWallModules;
            public int undergroundObjects;
            public int surfaceBarriers;
            public int surfaceDriveCells;
            public int parkingPavementOverlapCells;
            public int surfacePatrolCars;
            public int markedPatrolStalls;
            public string report;
            public string[] failures = Array.Empty<string>();
        }

        public sealed class Point
        {
            public float x;
            public float y;
            public float z;

            public static Point Of(Vector3 value) => new Point
            {
                x = ResidentialPolicePrecinctSketch.Round(value.x),
                y = ResidentialPolicePrecinctSketch.Round(value.y),
                z = ResidentialPolicePrecinctSketch.Round(value.z),
            };
        }

        [MenuItem("Tools/City/Residential/Add or Refresh Compact Police Precinct Block",
                  priority = 46)]
        public static void ApplyMenu()
        {
            var result = ApplyPreservingOpenScenes();
            Debug.Log("[ResidentialPrecinct] " + Describe(result));
            EditorUtility.DisplayDialog(
                "Residential police precinct", Describe(result), "OK");
        }

        [MenuItem("Tools/City/Residential/Audit Compact Police Precinct Block",
                  priority = 47)]
        public static void AuditMenu()
        {
            var result = AuditPreservingOpenScenes();
            Debug.Log("[ResidentialPrecinct] " + Describe(result));
            EditorUtility.DisplayDialog(
                "Residential police precinct audit", Describe(result), "OK");
        }

        public static Result ApplyPreservingOpenScenes()
        {
            if (EditorApplication.isPlaying)
                return Failed("Leave Play Mode before editing ResidentialDemo.");

            SyntyPolicePrecinctCompactKit.Build();
            return WithResidentialScene(edit: true, scene => Apply(scene, save: true));
        }

        /// <summary>
        /// Called by ResidentialSketch while it owns a fresh, unsaved scene. The compact
        /// asset is shared with the explicit add/refresh command; this method only places
        /// it and lets ResidentialSketch perform the scene's one authoritative save.
        /// </summary>
        public static Result EnsureInGeneratedDemo(Scene scene)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(
                    SyntyPolicePrecinctCompactKit.CompactPath) == null)
                SyntyPolicePrecinctCompactKit.Build();
            return Apply(scene, save: false);
        }

        public static Result AuditPreservingOpenScenes()
        {
            if (EditorApplication.isPlaying)
                return Failed("Leave Play Mode before auditing ResidentialDemo.");
            return WithResidentialScene(edit: false, scene => Audit(scene, null, false,
                                                                     Vector3.zero));
        }

        static Result WithResidentialScene(bool edit, Func<Scene, Result> action)
        {
            var before = SceneManager.GetActiveScene();
            var scene = SceneManager.GetSceneByPath(ScenePath);
            bool openedHere = !scene.IsValid() || !scene.isLoaded;
            if (!openedHere && scene.isDirty && before.path != ScenePath)
                return Failed("ResidentialDemo is already loaded with unsaved changes; " +
                              "those changes were left untouched.");

            try
            {
                if (openedHere)
                    scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
                return action(scene);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                return Failed(ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                if (openedHere && scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);
                if (before.IsValid() && before.isLoaded)
                    SceneManager.SetActiveScene(before);
            }
        }

        static Result Apply(Scene scene, bool save)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return Failed("ResidentialDemo could not be loaded.");

            var gym = Root(scene, GymPrefix);
            var carYard = Root(scene, CarYardPrefix);
            if (gym == null || carYard == null)
                return Failed("ResidentialDemo must contain its gym and car-yard blocks.");

            var existing = Root(scene, RootPrefix);
            bool refreshed = existing != null;
            Vector3 position;
            if (refreshed)
            {
                position = existing.transform.position;
                UnityEngine.Object.DestroyImmediate(existing);
            }
            else
            {
                var gymBounds = BoundsOf(gym);
                var yardBounds = BoundsOf(carYard);
                float blockMinX = CeilGrid(gymBounds.max.x + StreetGap);
                float blockMinZ = RoundGrid(yardBounds.min.z);
                position = new Vector3(
                    blockMinX - PolicePrecinctBlock.PreviewBounds.xMin,
                    0f,
                    blockMinZ - PolicePrecinctBlock.PreviewBounds.yMin);
            }

            var precinct = new GameObject($"{RootPrefix} seed {Seed}");
            SceneManager.MoveGameObjectToScene(precinct, scene);
            var stood = PolicePrecinctBlock.Compose(precinct.transform, Seed, Raise);
            precinct.transform.position = position;

            // Only the existing car-yard root moves, and only on first insertion (or when
            // repairing a partial insertion). Every child and every manual edit inside it
            // travels intact. Snapping the shift to the five-metre city grid preserves its
            // relationship with all its authored pavement tiles.
            var blockWorld = WorldRect(position, PolicePrecinctBlock.PreviewBounds);
            var carYardBefore = carYard.transform.position;
            var yard = BoundsOf(carYard);
            float requiredMinX = blockWorld.xMax + StreetGap;
            if (yard.min.x < requiredMinX)
            {
                float shift = CeilGrid(requiredMinX - yard.min.x);
                carYard.transform.position += Vector3.right * shift;
            }
            var carYardShift = carYard.transform.position - carYardBefore;

            EditorSceneManager.MarkSceneDirty(scene);
            bool saved = !save || EditorSceneManager.SaveScene(scene, ScenePath);
            var result = Audit(scene, stood, refreshed, carYardShift);
            result.saved = save && saved;
            if (save) result.passed &= saved;
            if (save && !saved)
                result.failures = result.failures.Concat(
                    new[] { "Unity did not save ResidentialDemo." }).ToArray();
            return result;
        }

        static Result Audit(
            Scene scene, PolicePrecinctBlock.Stood stood, bool refreshed,
            Vector3 carYardShift)
        {
            var failures = new System.Collections.Generic.List<string>();
            var precinct = Root(scene, RootPrefix);
            var gym = Root(scene, GymPrefix);
            var carYard = Root(scene, CarYardPrefix);
            if (precinct == null) failures.Add("compact precinct root is missing");
            if (gym == null) failures.Add("gym neighbour is missing");
            if (carYard == null) failures.Add("car-yard neighbour is missing");

            var visual = precinct != null
                ? precinct.GetComponentInChildren<PolicePrecinctVisual>(true) : null;
            var station = precinct != null
                ? precinct.GetComponentInChildren<LivingCity.Entities.PoliceStation>(true) : null;
            var layout = precinct != null
                ? precinct.GetComponent<PolicePrecinctBlockLayout>() : null;
            if (visual == null) failures.Add("PolicePrecinctVisual topology is missing");
            if (station == null || station.StallCount != 4)
                failures.Add("the shared PoliceStation marker does not own four real stalls");
            if (layout == null)
                failures.Add("the saved surface-only block layout is missing");

            int renderers = precinct != null
                ? precinct.GetComponentsInChildren<Renderer>(true).Length : 0;
            int props = precinct != null
                ? Count(precinct.transform, "SM_Prop_") + Count(precinct.transform, "SM_Sign_")
                : 0;
            int cells = precinct != null ? Count(precinct.transform, "SM_Bld_Wall_Cell_") : 0;
            int doors = precinct != null ? Count(precinct.transform, "SM_Bld_Wall_Door_Cell_") : 0;
            int underground = precinct != null
                ? precinct.GetComponentsInChildren<Transform>(true).Count(t =>
                    t.name.IndexOf("UNDERGROUND", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    t.name.IndexOf("GARAGE RAMP", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    t.name.StartsWith("SM_Env_Road_Ramp_", StringComparison.Ordinal))
                : 0;
            int barriers = precinct != null
                ? Count(precinct.transform, "SURFACE PARKING ENTRY BARRIER") +
                  Count(precinct.transform, "SURFACE PARKING EXIT BARRIER") : 0;
            var front = precinct != null
                ? Find(precinct.transform, "FRONT FACADE - TWO STOREYS CLOSED") : null;
            var rear = precinct != null
                ? Find(precinct.transform, "REAR FACADE - TWO STOREYS CLOSED") : null;
            var leftSide = precinct != null
                ? Find(precinct.transform, "LEFT FACADE - TWO STOREYS CLOSED") : null;
            var rightSide = precinct != null
                ? Find(precinct.transform, "RIGHT FACADE - TWO STOREYS CLOSED") : null;
            int frontModules = DirectChildren(front);
            int rearModules = DirectChildren(rear);
            int leftModules = DirectChildren(leftSide);
            int rightModules = DirectChildren(rightSide);

            if (renderers < 320) failures.Add($"only {renderers} renderers are present");
            if (props < 145) failures.Add($"only {props} props/signs are present");
            if (cells < 6 || doors < 2) failures.Add("two-cell mini holding suite is incomplete");
            if (frontModules != 12 || rearModules != 12 ||
                leftModules != 16 || rightModules != 16)
                failures.Add($"facades are incomplete F/R/L/R " +
                             $"{frontModules}/{rearModules}/{leftModules}/{rightModules}");
            float facadeBaseY = precinct != null ? precinct.transform.position.y : 0f;
            ValidateFacadeGeometry(failures, "front", front, facadeBaseY, 14.7f, true);
            ValidateFacadeGeometry(failures, "rear", rear, facadeBaseY, 14.7f, true);
            ValidateFacadeGeometry(failures, "left", leftSide, facadeBaseY, 19.7f, false);
            ValidateFacadeGeometry(failures, "right", rightSide, facadeBaseY, 19.7f, false);
            if (underground != 0)
                failures.Add($"{underground} underground/ramp object(s) are still present");
            if (barriers < 1)
                failures.Add("the surface parking entrance has no barrier");
            if (visual != null && (visual.UndergroundGarage != null ||
                                   visual.GarageRampTop != null ||
                                   visual.GarageRampBottom != null))
                failures.Add("the compact station still advertises underground parking");
            if (layout != null)
            {
                if (layout.UndergroundObjects != 0)
                    failures.Add("surface layout metadata still contains underground objects");
                if (layout.ParkingPavementOverlapCells != 0)
                    failures.Add($"parking overlaps pavement in " +
                                 $"{layout.ParkingPavementOverlapCells} cell(s)");
                if (layout.SurfaceDriveCells < 1)
                    failures.Add("the surface parking has no road connection");
                if (layout.SurfaceParkingBounds.Overlaps(layout.BuildingBounds))
                    failures.Add("surface parking overlaps the station footprint");
            }

            float left = 0f, right = 0f, cross = 0f;
            if (precinct != null)
            {
                var block = WorldRect(
                    precinct.transform.position, PolicePrecinctBlock.PreviewBounds);
                if (gym != null)
                {
                    left = block.xMin - BoundsOf(gym).max.x;
                    if (left < StreetGap - FurnitureOverhangAllowance)
                        failures.Add($"left street is only {left:F1} m");
                }
                if (carYard != null)
                {
                    right = BoundsOf(carYard).min.x - block.xMax;
                    if (right < StreetGap - FurnitureOverhangAllowance)
                        failures.Add($"right street is only {right:F1} m");
                }

                float nearestBelow = float.NegativeInfinity;
                foreach (var other in scene.GetRootGameObjects())
                {
                    if (other == precinct || other == gym || other == carYard) continue;
                    if (!other.name.StartsWith("RESIDENTIAL ", StringComparison.Ordinal)) continue;
                    var box = BoundsOf(other);
                    if (box.max.z <= block.yMin + 0.1f)
                        nearestBelow = Mathf.Max(nearestBelow, box.max.z);
                }
                if (!float.IsNegativeInfinity(nearestBelow))
                {
                    cross = block.yMin - nearestBelow;
                    if (cross < StreetGap - FurnitureOverhangAllowance)
                        failures.Add($"cross street is only {cross:F1} m");
                }
            }

            var rootPosition = precinct != null ? precinct.transform.position : Vector3.zero;
            return new Result
            {
                passed = failures.Count == 0,
                scene = scene.path,
                root = precinct != null ? precinct.name : null,
                position = Point.Of(rootPosition),
                carYardShift = Point.Of(carYardShift),
                refreshed = refreshed,
                leftStreet = Round(left),
                rightStreet = Round(right),
                crossStreet = Round(cross),
                renderers = renderers,
                propsAndSigns = props,
                holdingCellWalls = cells,
                holdingCellDoors = doors,
                frontWallModules = frontModules,
                rearWallModules = rearModules,
                leftWallModules = leftModules,
                rightWallModules = rightModules,
                undergroundObjects = underground,
                surfaceBarriers = barriers,
                surfaceDriveCells = layout?.SurfaceDriveCells ?? stood?.SurfaceDriveCells ?? 0,
                parkingPavementOverlapCells = layout?.ParkingPavementOverlapCells ??
                    stood?.ParkingPavementOverlapCells ?? -1,
                surfacePatrolCars = stood?.SurfacePatrolCars ??
                    (precinct != null
                        ? Count(precinct.transform, "SM_Veh_Sedan_01_Preset_Police") +
                          Count(precinct.transform, "SM_Veh_Pickup_01_Preset_Police")
                        : 0),
                markedPatrolStalls = station != null ? station.StallCount : 0,
                report = stood?.ToString() ?? "saved compact precinct block",
                failures = failures.ToArray(),
            };
        }

        static GameObject Root(Scene scene, string prefix) =>
            scene.GetRootGameObjects().FirstOrDefault(
                root => root.name.StartsWith(prefix, StringComparison.Ordinal));

        static GameObject Raise(GameObject asset, Transform parent) =>
            asset == null ? null :
            (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);

        static Bounds BoundsOf(GameObject go)
        {
            if (go == null) return default;
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return new Bounds(go.transform.position, Vector3.one);
            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        static Transform Find(Transform root, string name) =>
            root.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t => t.name == name);

        static int DirectChildren(Transform root) => root == null ? 0 : root.childCount;

        static void ValidateFacadeGeometry(
            System.Collections.Generic.List<string> failures, string side,
            Transform facade, float baseY, float requiredSpan, bool alongX)
        {
            if (facade == null) return;
            var bounds = BoundsOf(facade.gameObject);
            float span = alongX ? bounds.size.x : bounds.size.z;
            bool startsAtGround = bounds.min.y >= baseY - 0.15f &&
                                  bounds.min.y <= baseY + 0.15f;
            bool reachesUpperStorey = bounds.max.y >= baseY + 5.9f;
            if (span < requiredSpan || !startsAtGround || !reachesUpperStorey)
                failures.Add($"{side} facade is not a closed two-storey wall " +
                             $"({span:F1} m span, y {bounds.min.y - baseY:F1}.." +
                             $"{bounds.max.y - baseY:F1})");
        }

        static Rect WorldRect(Vector3 position, Rect local) =>
            new Rect(position.x + local.xMin, position.z + local.yMin,
                     local.width, local.height);

        static int Count(Transform root, string prefix) =>
            root.GetComponentsInChildren<Transform>(true)
                .Count(t => t.name.StartsWith(prefix, StringComparison.Ordinal));

        static float CeilGrid(float value) =>
            Mathf.Ceil(value / ResidentialLot.Cell) * ResidentialLot.Cell;

        static float RoundGrid(float value) =>
            Mathf.Round(value / ResidentialLot.Cell) * ResidentialLot.Cell;

        static float Round(float value) => Mathf.Round(value * 10f) / 10f;

        static Result Failed(string reason) => new Result
        {
            passed = false,
            scene = ScenePath,
            failures = new[] { reason },
            report = reason,
        };

        static string Describe(Result result)
        {
            if (result == null) return "No result.";
            if (!result.passed)
                return "FAILED: " + string.Join("; ", result.failures ?? Array.Empty<string>());
            return $"{result.root} saved at ({result.position.x:F0}, {result.position.z:F0}); " +
                   $"streets L/R/X {result.leftStreet:F1}/{result.rightStreet:F1}/" +
                   $"{result.crossStreet:F1} m; {result.renderers} renderers, " +
                   $"{result.propsAndSigns} props/signs, walls F/R/L/R " +
                   $"{result.frontWallModules}/{result.rearWallModules}/" +
                   $"{result.leftWallModules}/{result.rightWallModules}, " +
                   $"underground={result.undergroundObjects}, " +
                   $"surface gates={result.surfaceBarriers}, " +
                   $"parking/pavement overlaps={result.parkingPavementOverlapCells}, " +
                   $"{result.surfacePatrolCars}/{result.markedPatrolStalls} visible/marked stalls.";
        }

        [CliCommand("gangsters_residential_police_precinct",
                    "Build the compact precinct and insert or refresh its ordinary block " +
                    "inside ResidentialDemo without regenerating any residential recipe.",
                    MainThreadRequired = true,
                    Tags = new[] { "gangsters", "residential", "police", "precinct" })]
        public static object ApplyFromCli() => ApplyPreservingOpenScenes();

        [CliCommand("gangsters_residential_police_precinct_audit",
                    "Audit the compact precinct and its three ordinary-block street gaps " +
                    "inside the saved ResidentialDemo.",
                    MainThreadRequired = true,
                    Tags = new[] { "gangsters", "residential", "police", "precinct", "audit" })]
        public static object AuditFromCli() => AuditPreservingOpenScenes();
    }
}
