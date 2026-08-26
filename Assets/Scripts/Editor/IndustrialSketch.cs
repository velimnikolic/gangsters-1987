using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using RoadDemo;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// Draws a whole industrial quarter in the open scene, from a seed, without Play.
    ///
    /// Same office as <see cref="CoreCitySketch"/> does for the core, and for the same
    /// reason: the fastest way to find out whether a deal reads as a place is to stand it
    /// where it can be looked at from above, with a caption on every parcel saying what it
    /// was meant to be. The verdicts the drawing carries - the raster's report on the roads
    /// and the composer's on the parcels - go to the console beside it, so a drawing that
    /// looks right and is not says so.
    /// </summary>
    public static class IndustrialSketch
    {
        const string LabelName = "label";

        /// <summary>Clear ground between the drawing and the nearest thing already in the
        /// scene.</summary>
        const float Clearance = 80f;

        /// <summary>The seed the last drawing was dealt from, so a drawing worth keeping can
        /// be asked for again.</summary>
        internal static int LastSeed;

        /// <summary>The last raster drawn, as text - a probe's view of the drawing.</summary>
        internal static string LastMap = "";

        [MenuItem("Tools/City/Industrial/Sketch The Industrial Quarter", priority = 4)]
        public static void Sketch() => Draw(Random.Range(1, 1000000), true);

        [MenuItem("Tools/City/Industrial/Sketch The Industrial Quarter (seed 1987)", priority = 5)]
        public static void SketchSeeded() => Draw(1987, true);

        [MenuItem("Tools/City/Industrial/Clear The Sketch", priority = 20)]
        public static void Clear()
        {
            var scene = SceneManager.GetActiveScene();
            bool any = false;
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == IndustrialQuarter.SketchRoot) { Object.DestroyImmediate(root); any = true; }
            if (!any) { Debug.Log("[Industry] there was no sketch in this scene."); return; }
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("[Industry] the sketch is gone.");
        }

        /// <summary>Draws the quarter a seed gives and returns its plan.</summary>
        public static IndustrialLayout.Plan Draw(int seed, bool captions)
        {
            var scene = SceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == IndustrialQuarter.SketchRoot) Object.DestroyImmediate(root);

            // the drawing stands clear of everything else: measured before anything is
            // added, moved into place once its size is known
            bool anyScene = Extent(out Bounds others);

            var plan = IndustrialLayout.Arrange(seed, out var raster);
            if (plan == null)
            {
                Debug.LogError("[Industry] the deal came to nothing; no quarter was drawn.");
                return null;
            }
            LastSeed = seed;
            LastMap = raster.Map;

            var quarter = new GameObject(IndustrialQuarter.SketchRoot);
            SceneManager.MoveGameObjectToScene(quarter, scene);
            var stood = IndustrialQuarter.Stand(plan, raster, quarter.transform,
                (prefab, parent) => (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent));

            if (captions) Captions(quarter.transform, plan, stood, raster);

            var bounds = IndustrialLayout.Bounds(raster);
            if (anyScene)
                quarter.transform.position = new Vector3(others.min.x - Clearance - bounds.xMax, 0f,
                                                         others.center.z - bounds.center.y);

            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = quarter;
            Frame(quarter.transform.position + new Vector3(bounds.center.x, 0f, bounds.center.y),
                  Mathf.Max(bounds.width, bounds.height));

            var log = new System.Text.StringBuilder();
            log.AppendLine($"[Industry] {plan.Name}: {plan.Islands.Count} islands, {plan.Parcels.Count} parcels " +
                           $"({IndustrialQuarter.Cast(plan)}) over {bounds.width:F0} x {bounds.height:F0} m. " +
                           $"Blocks {raster.BlockArea} m2, road {raster.RoadArea} m2, left over {raster.SpareArea} m2, " +
                           $"{raster.Faults} faults in the roads:");
            foreach (var row in plan.Rows) log.AppendLine("   " + row);
            log.AppendLine(IndustrialQuarter.Report(stood));
            log.AppendLine(raster.Report);
            if (raster.Faults > 0) Debug.LogWarning(log.ToString());
            else Debug.Log(log.ToString());
            return plan;
        }

        /// <summary>A card over every parcel saying what it was dealt as and how big, and
        /// one over the quarter saying what the whole deal came to. Without them a drawing
        /// of eleven grey yards cannot be argued about.</summary>
        static void Captions(Transform quarter, IndustrialLayout.Plan plan,
                             List<IndustrialQuarter.Stood> stood, CoreRoads.Raster raster)
        {
            var labels = new GameObject("labels").transform;
            labels.SetParent(quarter, false);
            foreach (var one in stood)
            {
                var box = one.Parcel.Box;
                string trouble = one.Gaps == 0 && one.WallInBuilding == 0 && one.WallGap < 0.5f
                    ? "" : $"\n{one.Gaps} holes, {one.WallGap:F0} m of fence missing";
                if (!string.IsNullOrEmpty(one.Refused)) trouble += $"\nrefused: {one.Refused}";
                Caption($"{one.Parcel.Name} label",
                        $"{one.Parcel.Recipe.ToString().ToLowerInvariant()}\n" +
                        $"{box.width:F0} x {box.height:F0} m\nfronts {one.Parcel.Face.ToString().ToLowerInvariant()}{trouble}",
                        new Vector3(box.center.x, 26f, box.center.y), labels);
            }
            var bounds = IndustrialLayout.Bounds(raster);
            Caption("quarter label",
                    $"{IndustrialQuarter.SketchRoot}\n{plan.Parcels.Count} parcels, {plan.Name}\n" +
                    $"artery 35 m, streets 15 m" +
                    (raster.Faults > 0 ? $"\n{raster.Faults} FAULTS - see the console" : ""),
                    new Vector3(bounds.center.x, 90f, bounds.center.y), labels);
        }

        static void Caption(string name, string text, Vector3 position, Transform parent)
        {
            BlockLotPads.PadLabel(name, text, position, parent);
            var caption = parent.Find(name);
            if (caption) caption.rotation = Quaternion.Euler(35f, 180f, 0f);
        }

        /// <summary>
        /// Everything already standing, the drawing itself excepted - across EVERY open
        /// scene, not only the active one.
        ///
        /// The editor here habitually has two loaded at once (the harvest scene and the
        /// industrial lab), and the Scene view draws both. Measured against the active scene
        /// alone the drawing stood clear of its own scene's contents and straight through
        /// the other's - half the quarter with a tower block in the middle of it, which
        /// reads as a fault in the quarter and is not one.
        ///
        /// A particle system is skipped: until it plays, its renderer reports whatever
        /// bounds it happens to hold - usually an empty box at the world origin - and one
        /// plume of chimney smoke would drag the measurement across the whole map.
        /// </summary>
        static bool Extent(out Bounds box)
        {
            box = new Bounds();
            bool any = false;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var open = SceneManager.GetSceneAt(i);
                if (!open.isLoaded) continue;
                foreach (var root in open.GetRootGameObjects())
                {
                    if (root.name == IndustrialQuarter.SketchRoot) continue;
                    foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                    {
                        if (renderer is ParticleSystemRenderer) continue;
                        if (!any) { box = renderer.bounds; any = true; }
                        else box.Encapsulate(renderer.bounds);
                    }
                }
            }
            return any;
        }

        static void Frame(Vector3 centre, float span)
        {
            var view = SceneView.lastActiveSceneView;
            if (view == null) return;
            view.LookAt(centre, Quaternion.Euler(55f, 0f, 0f), span * 1.1f);
        }
    }
}
