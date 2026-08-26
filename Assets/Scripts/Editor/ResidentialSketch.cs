using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using RoadDemo;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// The bench the residential block is looked at on: one block into the open scene, or a
    /// demo scene of five blocks of every class the recipe knows.
    ///
    /// The deal and the verdict are <see cref="ResidentialLot"/>'s and are judged with no
    /// editor at all (<c>Tools/CoreSim --residential</c>); this only stands the answer up so
    /// somebody can look at it.
    /// </summary>
    public static class ResidentialSketch
    {
        public const string DemoScene = "Assets/Scenes/ResidentialDemo.unity";
        const string Root = "RESIDENTIAL";
        const string Bare = "Assets/Synty/PolygonCity/Prefabs/Environments/SM_Env_Road_Bare_01.prefab";

        /// <summary>The five blocks the demo scene stands: every class the recipe knows, and
        /// the block class twice because it is the one a quarter is mostly made of.</summary>
        static readonly (string Name, int W, int D)[] Five =
        {
            ("block", 15, 17),
            ("corner", 8, 7),
            ("row", 6, 13),
            ("court", 18, 18),
            ("block", 12, 14),
        };

        /// <summary>The street between two blocks: three cells, 15 m, the city street the
        /// core deals (CoreRoads.StreetCells).</summary>
        const int Street = 3;

        [MenuItem("Tools/City/Residential/Sketch A Block", priority = 41)]
        public static void SketchMenu()
        {
            string said = Sketch(SceneManager.GetActiveScene(), 1, "block", 14, 15);
            Debug.Log(said);
        }

        [MenuItem("Tools/City/Residential/Demo Scene (five blocks)", priority = 42)]
        public static void DemoMenu()
        {
            string said = Demo(1);
            Debug.Log(said);
            EditorUtility.DisplayDialog("Residential demo", said, "OK");
        }

        /// <summary>One block into a scene that is already open, beside whatever is in it.</summary>
        public static string Sketch(Scene scene, int seed, string klass, int w, int d)
        {
            var root = new GameObject($"{Root} {klass} {w}x{d} seed {seed}");
            SceneManager.MoveGameObjectToScene(root, scene);
            root.transform.position = Vector3.zero;      // composed at the origin, see Demo()

            var plan = ResidentialLot.Roll(w, d, seed, artery: 0);
            var stood = ResidentialBlocks.Compose(plan, root.transform, new System.Random(seed), Raise);
            ResidentialBlocks.Mouths(plan, root.transform, stood);
            return ResidentialLot.Report(plan) + "\n" + ResidentialLot.Map(plan) + stood;
        }

        /// <summary>
        /// The demo scene: five blocks in a line on one street, asphalt between them, saved
        /// to <see cref="DemoScene"/>. The scene is made fresh every time - it is generated,
        /// so there is nothing in it worth keeping.
        /// </summary>
        public static string Demo(int seed)
        {
            // a fresh scene replaces whatever is open - and that can be the harvest scene
            // with buildings named by hand in it, so the editor asks before it is dropped
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return "cancelled";
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects,
                                                    NewSceneMode.Single);
            RenderSettings.fog = false;                 // a bench is looked at, not driven through
            var sun = Object.FindFirstObjectByType<Light>();
            if (sun != null)
            {
                sun.transform.rotation = Quaternion.Euler(46f, 41f, 0f);
                sun.intensity = 1.1f;
                sun.shadows = LightShadows.Soft;
            }

            var said = new StringBuilder();
            var plans = new List<(ResidentialLot.Plan Plan, int X)>();
            int at = Street;
            int deepest = 0;

            foreach (var (name, w, d) in Five)
            {
                var plan = ResidentialLot.Roll(w, d, seed++, artery: 0);
                var root = new GameObject($"{Root} {name} {w}x{d}");

                // COMPOSED AT THE ORIGIN AND MOVED AFTERWARDS. Everything the composer sets
                // down is placed by measuring where it lands in the world, not by its parent,
                // so a root moved first is a root whose children ignore it: the first run of
                // this scene stood all five blocks on top of one another.
                var stood = ResidentialBlocks.Compose(plan, root.transform, new System.Random(seed), Raise);
                ResidentialBlocks.Mouths(plan, root.transform, stood);
                root.transform.position = new Vector3(at * ResidentialLot.Cell, 0f,
                                                      Street * ResidentialLot.Cell);

                said.AppendLine($"{name,-7} {ResidentialLot.Report(plan)}");
                said.AppendLine($"        {stood}");
                plans.Add((plan, at));
                at += w + Street;
                deepest = Mathf.Max(deepest, d);
            }

            Asphalt(at + Street, deepest + 2 * Street, plans);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, DemoScene);

            int faults = plans.Sum(p => p.Plan.Faults.Count);
            said.Insert(0, $"{plans.Count} block(s) in {DemoScene}, {faults} fault(s)\n");
            return said.ToString();
        }

        /// <summary>A carpet of bare asphalt everywhere no block stands - the streets between
        /// them and the ground round the edge. It is a bench, not a city: the roads a quarter
        /// gets are dealt by <c>CoreRoads</c> and are not this tool's business.</summary>
        static void Asphalt(int w, int d, List<(ResidentialLot.Plan Plan, int X)> plans)
        {
            var road = new GameObject("STREETS");
            var taken = new HashSet<Vector2Int>();
            foreach (var (plan, x) in plans)
                for (int i = 0; i < plan.W; i++)
                    for (int j = 0; j < plan.D; j++)
                        taken.Add(new Vector2Int(x + i, Street + j));

            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(Bare);
            if (asset == null) return;
            for (int i = 0; i < w; i++)
                for (int j = 0; j < d; j++)
                {
                    if (taken.Contains(new Vector2Int(i, j))) continue;
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, road.transform);
                    go.transform.position = new Vector3(i * ResidentialLot.Cell, -0.06f,
                                                        j * ResidentialLot.Cell);
                }
        }

        /// <summary>How the editor raises a prefab: a linked instance, so a block can be
        /// baked afterwards and keep its links.</summary>
        static GameObject Raise(GameObject asset, Transform parent)
        {
            if (asset == null) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
            return go;
        }
    }
}
