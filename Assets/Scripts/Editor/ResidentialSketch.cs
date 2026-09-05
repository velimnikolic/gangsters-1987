using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.Pipeline.Commands;
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
        const string FuelRoot = Root + " pumpdemo full functional";
        const string FireRoot = Root + " firestation full functional";

        /// <summary>The bench's prefix for a BAKED core block - a building big enough to be
        /// a block on its own, with its pavement already grown round it by
        /// <c>CoreBuildingBlocks</c>. These are dealt as-is by the city and by MiniCoreDemo;
        /// nothing is composed here, the prefab is stood and measured.</summary>
        const string CoreRoot = Root + " core ";

        const string CoreBlockDir = "Assets/Prefabs/CoreBlocks/";

        /// <summary>The two the user asked this bench for (2026-09-05): the police station
        /// MiniCoreDemo stands - which is NOT the compact precinct already in this scene -
        /// and the nightclub.</summary>
        static readonly string[] CoreBlocks = { "police-station-block", "nightclub-block" };

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

        [MenuItem("Tools/City/Residential/Demo Scene (ten blocks)", priority = 42)]
        public static void DemoMenu()
        {
            string said = Demo(FreshSeed());
            Debug.Log(said);
            EditorUtility.DisplayDialog("Residential demo", said, "OK");
        }

        [MenuItem("Tools/City/Residential/Add Full Functional PumpDemo Block", priority = 43)]
        public static void AddFuelMenu()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != DemoScene)
            {
                EditorUtility.DisplayDialog("Residential demo",
                    "Open ResidentialDemo before adding its functional pump block.", "OK");
                return;
            }
            string said = AddFuelBlock(scene, FreshSeed());
            Debug.Log("[Demo] " + said);
            EditorUtility.DisplayDialog("Residential demo", said, "OK");
        }

        [MenuItem("Tools/City/Residential/Add Full Functional Fire Station Block", priority = 44)]
        public static void AddFireStationMenu()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != DemoScene)
            {
                EditorUtility.DisplayDialog("Residential demo",
                    "Open ResidentialDemo before adding its functional fire-station block.", "OK");
                return;
            }
            string said = AddFireStationBlock(scene, FreshSeed());
            Debug.Log("[Demo] " + said);
            EditorUtility.DisplayDialog("Residential demo", said, "OK");
        }

        [MenuItem("Tools/City/Residential/Refresh Full Functional Fire Station Block", priority = 45)]
        public static void RefreshFireStationMenu()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != DemoScene)
            {
                EditorUtility.DisplayDialog("Residential demo",
                    "Open ResidentialDemo before refreshing its functional fire-station block.", "OK");
                return;
            }
            string said = RefreshFireStationBlock(scene, FreshSeed());
            Debug.Log("[Demo] " + said);
            EditorUtility.DisplayDialog("Residential demo", said, "OK");
        }

        [MenuItem("Tools/City/Residential/Add Baked Core Blocks (police station, nightclub)",
                  priority = 48)]
        public static void AddCoreBlocksMenu()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != DemoScene)
            {
                EditorUtility.DisplayDialog("Residential demo",
                    "Open ResidentialDemo before adding its baked core blocks.", "OK");
                return;
            }
            string said = AddCoreBlocks(scene, FreshSeed());
            Debug.Log("[Demo] " + said);
            EditorUtility.DisplayDialog("Residential demo", said, "OK");
        }

        [CliCommand("gangsters_residential_core_blocks",
                    "Stand the baked core blocks - the MiniCoreDemo police station and the " +
                    "nightclub - in ResidentialDemo without rebuilding a residential recipe.",
                    MainThreadRequired = true,
                    Tags = new[] { "gangsters", "residential", "block" })]
        public static object CoreBlocksFromCli()
        {
            if (EditorApplication.isPlaying)
                return new { passed = false, reason = "Leave Play Mode before editing ResidentialDemo." };

            var active = SceneManager.GetActiveScene();
            if (active.IsValid() && active.isDirty && active.path != DemoScene)
                return new
                {
                    passed = false,
                    reason = $"The active scene {active.path} has unsaved changes; it was left untouched.",
                };

            var scene = active.path == DemoScene
                ? active
                : EditorSceneManager.OpenScene(DemoScene, OpenSceneMode.Single);
            string said = AddCoreBlocks(scene, FreshSeed());
            bool saved = EditorSceneManager.SaveScene(scene, DemoScene);
            return new { passed = saved, saved, scene = DemoScene, report = said };
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
        /// followed by the full functional PumpDemo station on its own review row, with
        /// open editor ground between them, saved to <see cref="DemoScene"/>. A long
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

            // The pump is deliberately NOT a ResidentialLot recipe. It is a shared fuel
            // station block with generated city pavement and two road connectors. Its
            // standalone LaneNet is logical only -- no PumpDemo road loop is drawn here --
            // and still lets the real FuelCustomer errand run in Play.
            int fuelRow = atZ + rowDepth + Street;
            var fuel = ComposeFuel(scene, seed++);
            var fuelBounds = FuelStationBlock.PreviewBounds;
            fuel.root.transform.position = new Vector3(
                Street * ResidentialLot.Cell - fuelBounds.xMin,
                0f,
                fuelRow * ResidentialLot.Cell - fuelBounds.yMin);
            said.AppendLine($"pumpdemo-full  {fuel.stood}");
            index++;

            // The compact precinct is a shared civic composer, not a ResidentialLot
            // recipe. It still belongs inside this grid: the integration inserts it in
            // the second row, moves only the car-yard root to retain the normal street,
            // and never re-deals any of the blocks above.
            var precinct = ResidentialPolicePrecinctSketch.EnsureInGeneratedDemo(scene);
            said.AppendLine($"police-precinct {precinct.report}");
            if (precinct.passed) index++;

            // The fire station is a shared civic composer too, and it belongs in the
            // generated scene rather than only in the hand-added rows: a re-deal used to
            // drop it on the floor and the block had to be added back by hand every time.
            said.AppendLine("firestation    " + AddFireStationBlock(scene, seed++));
            index++;

            // And the two baked core blocks the city itself deals: the police station
            // MiniCoreDemo stands (not the compact precinct above) and the nightclub.
            said.AppendLine("core-blocks    " + AddCoreBlocks(scene, seed++));
            index += CoreBlocks.Length;
            ResidentialConditionSketch.Configure(scene);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, DemoScene);

            int faults = plans.Sum(p => p.Plan.Faults.Count);
            said.Insert(0, $"{index} block(s) in {DemoScene} from seed {first}, {faults} fault(s)\n");
            return said.ToString();
        }

        /// <summary>
        /// Append only the pump to an existing ResidentialDemo. This is the safe path for
        /// a manually adjusted review scene: unlike <see cref="Demo"/>, it neither clears
        /// nor re-deals any residential block. Repeated calls are idempotent.
        /// </summary>
        public static string AddFuelBlock(Scene scene, int seed)
        {
            if (!scene.IsValid() || !scene.isLoaded) return "ResidentialDemo is not loaded.";
            foreach (var root in scene.GetRootGameObjects())
                if (root.name.StartsWith(FuelRoot, System.StringComparison.Ordinal))
                    return root.name + " is already present; nothing was changed.";

            bool found = false;
            float maxZ = 0f;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (!root.name.StartsWith(Root + " ", System.StringComparison.Ordinal)) continue;
                var renderers = root.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    maxZ = found ? Mathf.Max(maxZ, renderers[i].bounds.max.z)
                                 : renderers[i].bounds.max.z;
                    found = true;
                }
            }

            float gap = Street * ResidentialLot.Cell;
            float nextMinZ = found ? Mathf.Ceil(maxZ / ResidentialLot.Cell) * ResidentialLot.Cell + gap
                                   : gap;
            var fuel = ComposeFuel(scene, seed);
            var bounds = FuelStationBlock.PreviewBounds;
            fuel.root.transform.position = new Vector3(
                gap - bounds.xMin, 0f, nextMinZ - bounds.yMin);
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = fuel.root;
            return fuel.root.name + " added without rebuilding the other blocks; " + fuel.stood;
        }

        /// <summary>
        /// Append the complete Synty fire station to a separate review row, wrapped as an
        /// ordinary city block: the shared CorePavement ring is generated round its parcel
        /// and one crossover is cut through the frontage for the appliances. This is the
        /// same non-destructive contract as <see cref="AddFuelBlock"/>: no residential
        /// recipe is re-dealt and a repeated call changes nothing. Its two authored fire
        /// engines are wired to the shared RoadCar/PatrolDocking runtime in Play.
        /// </summary>
        public static string AddFireStationBlock(Scene scene, int seed)
        {
            if (!scene.IsValid() || !scene.isLoaded) return "ResidentialDemo is not loaded.";
            foreach (var root in scene.GetRootGameObjects())
                if (root.name.StartsWith(FireRoot, System.StringComparison.Ordinal))
                    return root.name + " is already present; nothing was changed.";

            float gap = Street * ResidentialLot.Cell;
            float nextMinZ = NextReviewRow(scene, gap);
            var station = ComposeFireStation(scene, seed);
            var bounds = FireStationBlock.BlockBounds;
            station.root.transform.position = new Vector3(
                gap - bounds.xMin, 0f, nextMinZ - bounds.yMin);
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = station.root;
            return station.root.name + " added without rebuilding the other blocks; " +
                   station.stood + ", two working fire-engine routes";
        }

        /// <summary>
        /// Recompose only the existing fire-station root in place. This lets its shared
        /// recipe gain props or functional wiring without regenerating a single residential
        /// lot, moving the manually arranged review rows, or touching PumpDemo.
        /// </summary>
        public static string RefreshFireStationBlock(Scene scene, int seed)
        {
            if (!scene.IsValid() || !scene.isLoaded) return "ResidentialDemo is not loaded.";
            GameObject existing = null;
            foreach (var root in scene.GetRootGameObjects())
                if (root.name.StartsWith(FireRoot, System.StringComparison.Ordinal))
                {
                    existing = root;
                    break;
                }
            if (existing == null) return AddFireStationBlock(scene, seed);

            var position = existing.transform.position;
            var rotation = existing.transform.rotation;
            var scale = existing.transform.localScale;
            int sibling = existing.transform.GetSiblingIndex();
            UnityEngine.Object.DestroyImmediate(existing);

            var station = ComposeFireStation(scene, seed);
            station.root.transform.SetPositionAndRotation(position, rotation);
            station.root.transform.localScale = scale;
            station.root.transform.SetSiblingIndex(sibling);
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = station.root;
            return station.root.name + " refreshed in place; all other demo roots were left " +
                   "untouched; " + station.stood + ", two working fire-engine routes";
        }

        /// <summary>
        /// Stand the baked core blocks on one review row of their own. Same non-destructive
        /// contract as <see cref="AddFuelBlock"/>: no residential recipe is re-dealt, no
        /// other root moves, and a block already in the scene is left exactly as it is.
        ///
        /// They arrive as blocks - <c>CoreBuildingBlocks</c> grew the ten-metre pavement
        /// round each one when it baked them - so there is nothing to wrap here. Their
        /// footprint is read from <see cref="CoreBlockCatalog"/>, the same baked table the
        /// city lays them out with, rather than off their renderers: furniture hangs over a
        /// kerb and the block's own ground is what the street gap is measured against.
        /// </summary>
        public static string AddCoreBlocks(Scene scene, int seed)
        {
            if (!scene.IsValid() || !scene.isLoaded) return "ResidentialDemo is not loaded.";

            var wanted = new List<string>();
            foreach (string name in CoreBlocks)
                if (RootNamed(scene, CoreRoot + name) == null) wanted.Add(name);
            if (wanted.Count == 0)
                return "the baked core blocks are already present; nothing was changed.";

            var boxes = CoreBlockCatalog.CreateBlocks();
            float gap = Street * ResidentialLot.Cell;
            float rowZ = NextReviewRow(scene, gap);
            float atX = gap;
            var said = new StringBuilder();

            foreach (string name in wanted)
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(
                    CoreBlockDir + name + ".prefab");
                if (asset == null)
                {
                    said.Append(name + " is missing from " + CoreBlockDir + "; ");
                    continue;
                }
                var block = boxes.FirstOrDefault(b => b.Name == name);
                if (block == null)
                {
                    said.Append(name + " is not in the baked block table; ");
                    continue;
                }

                var ground = new Rect(block.Ground.min.x, block.Ground.min.z,
                                      block.CW * CoreBlockMetrics.Cell,
                                      block.CD * CoreBlockMetrics.Cell);
                var go = (GameObject)PrefabUtility.InstantiatePrefab(asset);
                SceneManager.MoveGameObjectToScene(go, scene);
                go.name = CoreRoot + name;
                go.transform.position = new Vector3(
                    atX - ground.xMin, 0f, rowZ - ground.yMin);
                atX += ground.width + gap;

                said.Append($"{name} {ground.width:F0} x {ground.height:F0} m at " +
                            $"({go.transform.position.x:F0}, {go.transform.position.z:F0}); ");
            }

            EditorSceneManager.MarkSceneDirty(scene);
            return said.ToString();
        }

        static GameObject RootNamed(Scene scene, string prefix) =>
            scene.GetRootGameObjects().FirstOrDefault(
                root => root.name.StartsWith(prefix, System.StringComparison.Ordinal));

        static float NextReviewRow(Scene scene, float gap)
        {
            bool found = false;
            float maxZ = 0f;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (!root.name.StartsWith(Root + " ", System.StringComparison.Ordinal)) continue;
                var renderers = root.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    maxZ = found ? Mathf.Max(maxZ, renderers[i].bounds.max.z)
                                 : renderers[i].bounds.max.z;
                    found = true;
                }
            }
            return found ? Mathf.Ceil(maxZ / ResidentialLot.Cell) * ResidentialLot.Cell + gap
                         : gap;
        }

        static (GameObject root, FuelStationBlock.Stood stood) ComposeFuel(Scene scene, int seed)
        {
            var root = new GameObject($"{FuelRoot} seed {seed}");
            SceneManager.MoveGameObjectToScene(root, scene);
            // Like the residential composer: stand at the origin, translate only after
            // every child has been parented. FuelStation and StreetKit both place world
            // transforms while composing.
            var stood = FuelStationBlock.Compose(root.transform, seed);
            return (root, stood);
        }

        static (GameObject root, FireStationBlock.Stood stood) ComposeFireStation(
            Scene scene, int seed)
        {
            var root = new GameObject($"{FireRoot} seed {seed}");
            SceneManager.MoveGameObjectToScene(root, scene);
            // Shared composers measure while they stand their children, so this block is
            // also composed at the origin and translated only once it is complete.
            var stood = FireStationBlock.ComposeBlock(root.transform, seed, Raise);
            var live = root.AddComponent<FireStationBlockRuntime>();
            live.Configure(seed, stood.Vehicles, stood.FireEngines, stood.BayDoors);
            return (root, stood);
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
