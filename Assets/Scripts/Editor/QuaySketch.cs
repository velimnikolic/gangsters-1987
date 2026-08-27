using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using RoadDemo;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// Draws one stretch of the promenade in the open scene, from a seed and a size,
    /// without Play - the park lab's office (<see cref="ParkSketch"/>) for the river.
    ///
    /// The stretch is laid as the core would lay it: a street arriving at the kerb every
    /// block's width, a bridge at its south end and the line's end at its north, and the
    /// fairground, the landing and two cafes asked of it. The plan's verdict and the
    /// composer's go to the console beside the drawing.
    /// </summary>
    public static class QuaySketch
    {
        public const string SketchRoot = "QUAY (sketch)";
        const float Clearance = 60f;

        internal static int LastSeed;
        internal static string LastMap = "";

        [MenuItem("Tools/City/River/Sketch The Quay", priority = 4)]
        public static void Sketch() => Draw(Random.Range(1, 1000000), 8, 32, true);

        [MenuItem("Tools/City/River/Sketch A Short Quay", priority = 5)]
        public static void SketchShort() => Draw(1987, 7, 14, true);

        [MenuItem("Tools/City/River/Clear The Sketch", priority = 20)]
        public static void Clear()
        {
            var scene = SceneManager.GetActiveScene();
            bool any = false;
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == SketchRoot) { Object.DestroyImmediate(root); any = true; }
            if (!any) { Debug.Log("[Quay] there was no sketch in this scene."); return; }
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("[Quay] the sketch is gone.");
        }

        /// <summary>The plan a seed and a size give, laid as the core lays a stretch: the
        /// streets arriving at random block widths, a bridge at the south end, the line's
        /// end at the north.</summary>
        public static QuayWalk.Plan Plan(int seed, int depth, int length)
        {
            var dice = new System.Random(seed);
            var mouths = new List<QuayWalk.Mouth>();
            for (int z = 1 + dice.Next(6, 12); z + 3 <= length - 1; z += 3 + dice.Next(7, 21))
                mouths.Add(new QuayWalk.Mouth(z, z + 3));
            var wants = new QuayWalk.Wants { Fair = true, FairAtStart = false, Landing = true, Diner = true, Terraces = Mathf.Max(1, length / 8) };
            return QuayWalk.Lay(depth, length, mouths, QuayWalk.End.Bridge, QuayWalk.End.Line, wants, dice);
        }

        /// <summary>Draws the stretch, and returns what it stood.</summary>
        public static QuayBlocks.Stood Draw(int seed, int depth, int length, bool captions)
        {
            var scene = SceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == SketchRoot) Object.DestroyImmediate(root);
            bool anyScene = SketchFrame.Extent(SketchRoot, out Bounds others);

            var plan = Plan(seed, depth, length);
            LastSeed = seed;
            LastMap = plan.Map;

            var quay = new GameObject(SketchRoot);
            SceneManager.MoveGameObjectToScene(quay, scene);
            Composer.ForgetMissing();
            // composed at the origin and moved afterwards: every piece is placed by
            // measuring where it lands
            var stood = QuayBlocks.Compose(plan, quay.transform, new System.Random(seed),
                (prefab, parent) => (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent));
            int paved = QuayBlocks.Pave(plan, quay.transform, out string pavement,
                (prefab, parent) => (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent), seed);
            Water(quay.transform, plan);

            string said = QuayWalk.Report(plan, out int faults);
            if (captions) Caption(quay.transform, plan, stood, faults);

            float wide = plan.Depth * QuayWalk.Cell + 80f, deep = plan.Length * QuayWalk.Cell;
            if (anyScene)
                quay.transform.position = new Vector3(others.min.x - Clearance - wide, 0f, others.center.z - deep * 0.5f);

            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = quay;
            SketchFrame.Frame(quay.transform.position + new Vector3(wide * 0.4f, 0f, deep * 0.5f), Mathf.Max(wide, deep) * 1.2f);

            var log = new System.Text.StringBuilder();
            log.AppendLine($"[Quay] seed {seed}: {said}");
            log.AppendLine(QuayBlocks.Report(stood));
            log.AppendLine($"   pavement: {paved} tile(s) - {pavement}");
            log.Append(plan.Map);
            if (faults > 0 || stood.Gaps > 0 || stood.RailGap > 0.5f || stood.OnWalk > 0) Debug.LogWarning(log.ToString());
            else Debug.Log(log.ToString());
            return stood;
        }

        /// <summary>Water east of the wall, so the drawing reads as a bank and not a kerb.</summary>
        static void Water(Transform root, QuayWalk.Plan plan)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Synty/PNB_Core/Prefabs/SM_Env_Water_Plane_01.prefab");
            if (prefab == null) return;
            var water = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
            water.name = "Water";
            float x0 = plan.Depth * QuayWalk.Cell, x1 = x0 + 70f, z0 = -40f, z1 = plan.Length * QuayWalk.Cell + 40f;
            water.transform.localScale = new Vector3((x1 - x0) / 50f, 1f, (z1 - z0) / 50f);
            water.transform.position = new Vector3((x0 + x1) * 0.5f, QuayBlocks.WaterY, (z0 + z1) * 0.5f);
        }

        static void Caption(Transform quay, QuayWalk.Plan plan, QuayBlocks.Stood stood, int faults)
        {
            var labels = new GameObject("labels").transform;
            labels.SetParent(quay, false);
            var rooms = new List<string>();
            foreach (var room in plan.Rooms) rooms.Add($"{room.Programme.ToString().ToLowerInvariant()} {room.Length * 5}");
            string text = $"quay seed {LastSeed}\n{plan.Depth * 5} x {plan.Length * 5} m, {plan.Mouths.Count} street(s)\n" +
                          $"{stood.Lamps} lamps, {stood.Benches} benches, {stood.Tables} tables, {stood.BoatCount} boats" +
                          (stood.Wheel ? ", the wheel" : "") + "\n" + string.Join(", ", rooms) +
                          (faults > 0 ? $"\n{faults} FAULTS - see the console" : "");
            SketchFrame.Caption("quay label", text,
                new Vector3(plan.Depth * QuayWalk.Cell * 0.5f, 26f, plan.Length * QuayWalk.Cell * 0.5f), labels);
        }
    }
}
