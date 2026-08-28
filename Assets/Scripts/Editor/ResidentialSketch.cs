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
    /// demo scene of compact, currently production-ready residential recipes.
    ///
    /// The deal and the verdict are <see cref="ResidentialLot"/>'s and are judged with no
    /// editor at all (<c>Tools/CoreSim --residential</c>); this only stands the answer up so
    /// somebody can look at it.
    /// </summary>
    public static class ResidentialSketch
    {
        public const string DemoScene = "Assets/Scenes/ResidentialDemo.unity";
        const string Root = "RESIDENTIAL";

        /// <summary>The blocks the demo scene stands. The large Court recipe is intentionally
        /// absent here: at its minimum 80 x 80 m it needs a dedicated courtyard programme,
        /// and presenting its unfinished paved centre made the whole demo read as empty.
        /// The Block class appears three times because it supplies the alleys, rear parking
        /// and mixed frontage this residential quarter is meant to demonstrate. Kept to the
        /// small end of each class - the user, 2026-08-27: "izbegavaj velike residential
        /// blokove".
        ///
        /// <c>Unit</c> names a YARD BLOCK - a block that is one lot, cut to the lot and its
        /// pavement ring (the user, 2026-08-28: "dodaj u residential demo da mi iscrtava i te
        /// gym caryard skatepark blokove"). Their sizes are not typed here: they are measured
        /// off the units themselves, so a re-harvest moves the bench with them.
        ///
        /// <c>Diner</c> is different: it reserves that complete Palm City venue in the middle
        /// programme of an ordinary block, after the four corner houses. It is deliberately
        /// present on two of the five mixed blocks rather than appended as another yard block.</summary>
        // The named recipes below were measured with a one-cell pavement. When the shared
        // CoreDemo pavement grows, grow the whole block by the same amount on both sides so
        // the usable interior -- and therefore the recipe and its seed -- stay unchanged.
        const int PavementGrowth = 2 * (ResidentialLot.Walk - 1);

        static readonly (string Name, int W, int D, string Unit, string Diner)[] Five =
        {
            ("block", 13 + PavementGrowth, 15 + PavementGrowth, null, "dinner"),
            ("corner", 8 + PavementGrowth, 7 + PavementGrowth, null, null),
            ("row", 6 + PavementGrowth, 13 + PavementGrowth, null, null),
            ("block", 13 + PavementGrowth, 15 + PavementGrowth, null, null),
            ("block", 14 + PavementGrowth, 15 + PavementGrowth, null, "dinner2"),
        };

        /// <summary>The bench's blocks, the yard blocks measured in after them.</summary>
        static IEnumerable<(string Name, int W, int D, string Unit, string Diner)> Bench()
        {
            foreach (var one in Five) yield return one;
            foreach (string name in ResidentialLot.OwnBlock)
            {
                var unit = ResidentialUnits.All.FirstOrDefault(u => u.Name == name);
                if (unit == null) continue;
                ResidentialLot.YardDimensions(unit, out int w, out int d);
                yield return (name, w, d, name, null);
            }
        }

        /// <summary>The street between two blocks: three cells, 15 m, the city street the
        /// core deals (CoreRoads.StreetCells).</summary>
        const int Street = 3;

        /// <summary>A fresh seed off the clock, so every deal from the menu is a new one -
        /// the first cut dealt seed 1 every time (the user, 2026-08-27: "dobijam uvek iste").
        /// The seed is in the root's name and in the report, so a deal worth looking at
        /// again can be dealt again.</summary>
        static int FreshSeed() => 1 + (int)((System.DateTime.Now.Ticks / 10000) % 89999);

        [MenuItem("Tools/City/Residential/Sketch A Block", priority = 41)]
        public static void SketchMenu()
        {
            string said = Sketch(SceneManager.GetActiveScene(), FreshSeed(), "block", 14, 15);
            Debug.Log(said);
        }

        [MenuItem("Tools/City/Residential/Demo Scene (eight blocks)", priority = 42)]
        public static void DemoMenu()
        {
            string said = Demo(FreshSeed());
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
            return ResidentialLot.Report(plan) + "\n" + ResidentialLot.Map(plan) + stood;
        }

        /// <summary>
        /// The demo scene: the residential recipes and yard blocks in two compact rows,
        /// with open editor ground between them, saved to <see cref="DemoScene"/>. A long
        /// single row left a full block-depth of dead test ground behind every shallow lot.
        /// The road carpet is intentionally absent: this bench is for dragging and judging
        /// residential blocks, and thousands of selectable asphalt tiles only get in the
        /// way. Production streets remain the core road system's responsibility. The scene
        /// is made fresh every time - it is generated, so there is nothing in it worth keeping.
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
            var plans = new List<(ResidentialLot.Plan Plan, int X, int Z)>();
            int atX = Street;
            int atZ = Street;
            int rowDepth = 0;
            int first = seed;
            int index = 0;

            foreach (var (name, w, d, unit, diner) in Bench())
            {
                if (index > 0 && index % 4 == 0)
                {
                    atX = Street;
                    atZ += rowDepth + Street;
                    rowDepth = 0;
                }
                var plan = unit == null
                    ? ResidentialLot.Roll(w, d, seed++, artery: 0,
                        forced: name switch
                        {
                            "block" => ResidentialLot.Klass.Block,
                            "corner" => ResidentialLot.Klass.Corner,
                            "row" => ResidentialLot.Klass.Row,
                            "court" => ResidentialLot.Klass.Court,
                            _ => null,
                        },
                        featuredDiner: diner)
                    : ResidentialLot.Yard(w, d, seed++, unit);
                string label = diner == null ? name : $"{name}+{diner}";
                var root = new GameObject($"{Root} {label} {w}x{d} seed {plan.Seed}");

                // COMPOSED AT THE ORIGIN AND MOVED AFTERWARDS. Everything the composer sets
                // down is placed by measuring where it lands in the world, not by its parent,
                // so a root moved first is a root whose children ignore it: the first run of
                // this scene stood all blocks on top of one another.
                var stood = ResidentialBlocks.Compose(plan, root.transform, new System.Random(seed), Raise);
                root.transform.position = new Vector3(atX * ResidentialLot.Cell, 0f,
                                                      atZ * ResidentialLot.Cell);

                said.AppendLine($"{label,-14} {ResidentialLot.Report(plan)}");
                said.AppendLine($"        {stood}");
                plans.Add((plan, atX, atZ));
                atX += w + Street;
                rowDepth = Mathf.Max(rowDepth, d);
                index++;
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, DemoScene);

            int faults = plans.Sum(p => p.Plan.Faults.Count);
            said.Insert(0, $"{plans.Count} block(s) in {DemoScene} from seed {first}, {faults} fault(s)\n");
            return said.ToString();
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
