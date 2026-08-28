using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using RoadDemo;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// Draws a park in the open scene, from a seed and a size, without Play.
    ///
    /// Same office as <see cref="CoreCitySketch"/> does for the core and
    /// <see cref="IndustrialSketch"/> for the industrial quarter: the fastest way to find out
    /// whether a plan reads as a place is to stand it where it can be looked at from above,
    /// with a caption saying what it was meant to be. The two verdicts the drawing carries -
    /// the plan's (is the walk one piece, is any ground stranded) and the composer's (is
    /// every cell floored, is the fence whole, is anything standing on the path) - go to the
    /// console beside it, so a drawing that looks right and is not says so.
    /// </summary>
    public static class ParkSketch
    {
        /// <summary>The name of the root the whole drawing hangs off in a scene.</summary>
        public const string SketchRoot = "PARK (sketch)";

        /// <summary>Clear ground between the drawing and the nearest thing already in the
        /// scene.</summary>
        const float Clearance = 60f;

        /// <summary>The seed and size the last drawing was dealt from, so one worth keeping
        /// can be asked for again.</summary>
        internal static int LastSeed;
        internal static string LastMap = "";

        [MenuItem("Tools/City/Park/Sketch A Park", priority = 4)]
        public static void Sketch() => Draw(Random.Range(1, 1000000), "", true);

        [MenuItem("Tools/City/Park/Sketch A Pocket Park (block-08's size)", priority = 5)]
        public static void SketchPocket() => Draw(1987, "6x6", true);

        [MenuItem("Tools/City/Park/Sketch A City Park", priority = 6)]
        public static void SketchBig() => Draw(1987, "22x18", true);

        [MenuItem("Tools/City/Park/Clear The Sketch", priority = 20)]
        public static void Clear()
        {
            var scene = SceneManager.GetActiveScene();
            bool any = false;
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == SketchRoot) { Object.DestroyImmediate(root); any = true; }
            if (!any) { Debug.Log("[Park] there was no sketch in this scene."); return; }
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("[Park] the sketch is gone.");
        }

        /// <summary>
        /// Draws the park a seed and a size give, and returns what it stood.
        ///
        /// <paramref name="size"/> is a class name (pocket, square, park, strip), an explicit
        /// WxD in cells, or empty for anything at all.
        /// </summary>
        public static ParkBlocks.Stood Draw(int seed, string size, bool captions)
        {
            var scene = SceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == SketchRoot) Object.DestroyImmediate(root);

            // the drawing stands clear of everything else: measured before anything is added,
            // moved into place once its size is known
            bool anyScene = SketchFrame.Extent(SketchRoot, out Bounds others);

            Measure(size, new System.Random(seed), out int nx, out int nz);
            var plan = ParkWalk.Lay(nx, nz, ParkWalk.Edge.Alone(), new System.Random(seed));
            LastSeed = seed;
            LastMap = plan.Map;

            var park = new GameObject(SketchRoot);
            SceneManager.MoveGameObjectToScene(park, scene);

            ParkBlocks.ForgetMissing();
            // COMPOSED AT THE ORIGIN AND MOVED AFTERWARDS, which is not a detail: every piece
            // is placed by MEASURING where it lands and setting a WORLD position, because pack
            // pieces pivot at a corner, at one end or in the middle. Given its place first, a
            // park builds itself round the world origin instead (IndustrialQuarter.Stand says
            // the same thing at more length, having learnt it the hard way).
            var stood = ParkBlocks.Compose(plan, park.transform, new System.Random(seed),
                (prefab, parent) => (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent));
            int paved = ParkBlocks.Pave(plan, park.transform, out string pavement,
                (prefab, parent) => (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent), seed);

            ParkWalk.Report(plan, out int faults);
            if (captions) Caption(park.transform, plan, stood, faults);

            if (anyScene)
                park.transform.position = new Vector3(others.min.x - Clearance - plan.Wide, 0f,
                                                      others.center.z - plan.Deep * 0.5f);

            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = park;
            SketchFrame.Frame(park.transform.position + new Vector3(plan.Wide * 0.5f, 0f, plan.Deep * 0.5f),
                              Mathf.Max(plan.Wide, plan.Deep) * 1.4f);

            var log = new System.Text.StringBuilder();
            log.AppendLine($"[Park] {plan.Name} from seed {seed}: {plan.Wide:F0} x {plan.Deep:F0} m, " +
                           $"{plan.Mouths.Count} way(s) in, {plan.Rooms.Count} room(s) " +
                           $"({ParkWalk.Cast(plan)}), {faults} fault(s) in the plan.");
            log.AppendLine(ParkWalk.Report(plan, out _));
            log.AppendLine(ParkBlocks.Report(stood));
            log.AppendLine($"   pavement: {paved} tile(s) - {pavement}");
            log.Append(plan.Map);
            if (faults > 0 || stood.Gaps > 0 || stood.FenceGap > 0.5f) Debug.LogWarning(log.ToString());
            else Debug.Log(log.ToString());
            return stood;
        }

        /// <summary>The size to deal: a class name, an explicit WxD in cells, or anything at
        /// all if nothing was asked for. The same table CoreSim uses, so a size that read one
        /// way there reads the same way here.</summary>
        public static void Measure(string size, System.Random dice, out int nx, out int nz)
        {
            size = (size ?? "").ToLowerInvariant().Trim();
            int at = size.IndexOf('x');
            if (at > 0 && int.TryParse(size.Substring(0, at), out nx) &&
                int.TryParse(size.Substring(at + 1), out nz)) return;

            int growth = 2 * (ParkWalk.Band - 1);
            switch (size)
            {
                case "pocket": nx = dice.Next(5 + growth, 8 + growth); nz = dice.Next(5 + growth, 8 + growth); return;
                case "square": nx = dice.Next(8 + growth, 13 + growth); nz = dice.Next(8 + growth, 13 + growth); return;
                case "park": nx = dice.Next(13 + growth, 31 + growth); nz = dice.Next(13 + growth, 31 + growth); return;
                case "strip": nx = dice.Next(20 + growth, 61 + growth); nz = dice.Next(6 + growth, 9 + growth); return;
                default: nx = dice.Next(5 + growth, 31 + growth); nz = dice.Next(5 + growth, 31 + growth); return;
            }
        }

        /// <summary>A card over the park saying what it was dealt as and what stands on it.
        /// Without one a drawing of a green square cannot be argued about.</summary>
        static void Caption(Transform park, ParkWalk.Plan plan, ParkBlocks.Stood stood, int faults)
        {
            var labels = new GameObject("labels").transform;
            labels.SetParent(park, false);

            var rooms = new List<string>();
            foreach (var room in plan.Rooms)
                if (room.Programme != ParkWalk.Programme.Lawn)
                    rooms.Add($"{ParkWalk.Words(room.Programme)} {room.W * 5}x{room.D * 5}");

            string text = $"{plan.Name}\n{plan.Wide:F0} x {plan.Deep:F0} m, " +
                          $"{plan.Mouths.Count} way(s) in\n" +
                          $"{stood.TreeCount} trees ({stood.Density:F1}/100 m2), " +
                          $"{stood.Benches} benches, {stood.Lamps} lamps\n" +
                          (rooms.Count > 0 ? string.Join(", ", rooms) : "walk and lawn only") +
                          (faults > 0 ? $"\n{faults} FAULTS - see the console" : "");

            SketchFrame.Caption("park label", text,
                new Vector3(plan.Wide * 0.5f, 26f, plan.Deep * 0.5f), labels);
        }
    }
}
