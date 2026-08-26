using System.Collections.Generic;
using System.Linq;
using RoadDemo;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// Draws the city core in the open scene, to be looked at: the harvested blocks dealt
    /// into rows by a seed (<see cref="CoreLayout.Roll"/>), judged and dealt again until
    /// the drawing is clean (<see cref="CoreLayout.Arrange"/>), and the roads
    /// <see cref="CoreRoads"/> runs between them. The same classes build the core in the
    /// game; this is their editor host. The Synty item draws the demo's own arrangement,
    /// the reference every deal is measured against (Docs/core-district-plan.md).
    ///
    /// Re-running it wipes the last drawing. The raster is kept in
    /// <see cref="CoreRoads.Raster.Map"/> (read back through <see cref="LastMap"/>) so the
    /// drawing can be checked without a picture, and the seed that drew it in
    /// <see cref="LastSeed"/> so it can be drawn again.
    /// </summary>
    public static class CoreCitySketch
    {
        internal const string CityRoot = CoreLayout.SketchRoot;
        /// <summary>Clear ground between the drawing and the nearest thing already in the
        /// scene.</summary>
        const float Clearance = 60f;

        /// <summary>The last raster drawn, as text - a probe's view of the drawing.</summary>
        internal static string LastMap = "";
        /// <summary>The seed the last drawing was dealt from.</summary>
        internal static int LastSeed = CoreLayout.SyntySeed;

        /// <summary>A fresh deal every time: a seed nobody chose, printed in the log so
        /// the city it gave can be asked for again.</summary>
        [MenuItem("Tools/City/Core/Sketch The Core City", priority = 8)]
        public static void Sketch() => Draw(Random.Range(1, 1000000));

        [MenuItem("Tools/City/Core/Sketch The Core City (Synty's arrangement)", priority = 9)]
        public static void SketchSynty() => Draw(CoreLayout.SyntySeed);

        /// <summary>Draws the core a seed gives, in the open scene, and returns its plan.</summary>
        public static CoreLayout.Plan Draw(int seed)
        {
            var scene = SceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == CityRoot) Object.DestroyImmediate(root);

            // the drawing stands clear of everything else: measured before anything is
            // added, moved into place once its size is known
            bool anyScene = Extent(scene, out Bounds others);

            var city = new GameObject(CityRoot);
            var blocks = Stand(city.transform, (prefab, parent) => (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent));
            if (blocks.Count == 0)
            {
                Object.DestroyImmediate(city);
                EditorUtility.DisplayDialog("Sketch The Core City",
                    $"No block prefabs found under {CoreLayout.BlocksDir}.", "OK");
                return null;
            }

            var plan = CoreLayout.Arrange(blocks, seed, out var raster);
            foreach (var block in blocks) CoreLayout.Place(block);
            Parks(plan, city.transform, seed);
            Quays(plan, city.transform, seed);
            LastMap = raster.Map;
            LastSeed = seed;
            var roads = new GameObject("roads");
            roads.transform.SetParent(city.transform, false);
            // the road's tiles go down over the water too - that is the bridge's deck - but
            // not over the channels the leaves span
            CoreRoads.Lay(raster, (prefab, parent) => (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent),
                          roads.transform, RiverBridge.Skip(plan, raster));
            var river = new GameObject("river");
            river.transform.SetParent(city.transform, false);
            RiverBridge.Dress(plan, river.transform, (prefab, parent) => (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent));

            var labels = new GameObject("labels");
            labels.transform.SetParent(city.transform, false);
            foreach (var block in blocks)
            {
                var box = block.Box;
                string stood = seed == CoreLayout.SyntySeed
                    ? $"moved ({block.Shift.x:+0;-0;0}, {block.Shift.y:+0;-0;0})"
                    : $"turned {block.Yaw}";
                Caption($"{block.Name} label", $"{block.Name}\n{block.MaxH:F0} m\n{stood}",
                    new Vector3(box.center.x, Mathf.Max(12f, block.MaxH + 8f), box.center.y),
                    labels.transform);
            }

            float minX = raster.X0, maxX = raster.X(raster.NX), minZ = raster.Z0, maxZ = raster.Z(raster.NZ);
            Caption("city label",
                $"{CityRoot}\n{blocks.Count} blocks, {plan.Name}\nstreets 15 m, the main road a 35 m boulevard" +
                (raster.Faults > 0 ? $"\n{raster.Faults} FAULTS - see the console" : ""),
                new Vector3((minX + maxX) * 0.5f, 75f, (minZ + maxZ) * 0.5f), labels.transform);
            if (anyScene)
                city.transform.position = new Vector3(others.min.x - Clearance - maxX, 0f,
                                                      others.center.z - (minZ + maxZ) * 0.5f);

            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = city;
            Frame(city.transform.position + new Vector3((minX + maxX) * 0.5f, 0f, (minZ + maxZ) * 0.5f),
                  Mathf.Max(maxX - minX, maxZ - minZ));

            var log = new System.Text.StringBuilder();
            log.AppendLine($"[CoreCity] {plan.Name}: {blocks.Count} blocks drawn under \"{CityRoot}\": {maxX - minX:F0} x {maxZ - minZ:F0} m. " +
                           $"Blocks {raster.BlockArea} m2, road {raster.RoadArea} m2, car parks {raster.ParkingArea} m2, " +
                           $"left over {raster.SpareArea} m2, {raster.Faults} faults:");
            foreach (var row in plan.Rows) log.AppendLine("   " + row);
            log.AppendLine(raster.Report);
            log.AppendLine("   blocks:");
            foreach (var block in blocks)
                log.AppendLine($"   {block.Name,-10} {block.CW * 5,3} x {block.CD * 5,3}  {block.MaxH,5:F1} m  @{block.Yaw,3}  " +
                               $"box x {block.Box.xMin,5:F0}..{block.Box.xMax,5:F0}  z {block.Box.yMin,5:F0}..{block.Box.yMax,5:F0}" +
                               (block.Lot.width > 0f ? $"  lot z {block.Lot.yMin:F0}..{block.Lot.yMax:F0}" : ""));
            if (raster.Faults > 0) Debug.LogWarning(log.ToString());
            else Debug.Log(log.ToString());
            return plan;
        }

        /// <summary>
        /// The deal's parks, composed on the spot.
        ///
        /// A park has no prefab to instantiate - that is the whole of what makes it different
        /// from the other blocks - so it is built to the rectangle the deal gave it, by the
        /// same code the park lab draws with. Composed at the ORIGIN and moved afterwards,
        /// because every piece is placed by measuring where it lands in world space; given
        /// its place first, a park builds itself around the world origin.
        /// </summary>
        static void Parks(CoreLayout.Plan plan, Transform city, int seed)
        {
            if (plan == null || plan.Parks.Count == 0) return;

            var green = new GameObject("parks").transform;
            green.SetParent(city, false);
            ParkBlocks.ForgetMissing();

            foreach (var block in plan.Parks)
            {
                var root = new GameObject(block.Name).transform;
                root.SetParent(green, false);

                // the park is dealt in the core's metres; the recipe works in its own, from a
                // corner, so the size goes in and the corner is applied afterwards
                var box = block.Box;
                int nx = Mathf.Max(3, Mathf.RoundToInt(box.width / CoreLayout.Cell));
                int nz = Mathf.Max(3, Mathf.RoundToInt(box.height / CoreLayout.Cell));
                int dice = unchecked(seed * 7919 + Mathf.RoundToInt(box.xMin) * 104729 +
                                     Mathf.RoundToInt(box.yMin) * 1299709);

                var walk = ParkWalk.Lay(nx, nz, ParkWalk.Edge.Alone(), new System.Random(dice));
                var stood = ParkBlocks.Compose(walk, root, new System.Random(dice),
                    (prefab, parent) => (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent));
                ParkBlocks.Pave(walk, root, out _,
                    (prefab, parent) => (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent), dice);

                root.position = new Vector3(box.xMin, 0f, box.yMin);

                ParkWalk.Report(walk, out int faults);
                if (faults > 0 || stood.Gaps > 0 || stood.FenceGap > 0.5f)
                    Debug.LogWarning($"[CoreCity] {block.Name}: {faults} fault(s) in the plan, " +
                                     $"{stood.Gaps} cell(s) with no floor, {stood.FenceGap:F1} m of fence missing.");
            }
        }

        /// <summary>
        /// The promenade, stretch by stretch, composed on the spot like the parks: the plan
        /// read off the core (which streets arrive, what each end meets, what the line asks
        /// of it), composed at the origin and moved to the stretch's corner.
        /// </summary>
        static void Quays(CoreLayout.Plan plan, Transform city, int seed)
        {
            if (plan == null || plan.Quays.Count == 0) return;
            var bank = new GameObject("quays").transform;
            bank.SetParent(city, false);
            Composer.ForgetMissing();
            var wants = QuayWalk.Cast(plan);
            for (int q = 0; q < plan.Quays.Count; q++)
            {
                var block = plan.Quays[q];
                var root = new GameObject(block.Name).transform;
                root.SetParent(bank, false);
                var box = block.Box;
                int dice = unchecked(seed * 7919 + Mathf.RoundToInt(box.xMin) * 104729 + Mathf.RoundToInt(box.yMin) * 1299709);
                var walk = QuayWalk.ForQuay(plan, block, wants[q], new System.Random(dice));
                var stood = QuayBlocks.Compose(walk, root, new System.Random(dice),
                    (prefab, parent) => (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent));
                QuayBlocks.Pave(walk, root, out _,
                    (prefab, parent) => (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent), dice);
                CoreLayout.PlaceQuay(plan, block, root);

                QuayWalk.Report(walk, out int faults);
                if (faults > 0 || stood.Gaps > 0 || stood.RailGap > 0.5f || stood.OnWalk > 0)
                    Debug.LogWarning($"[CoreCity] {block.Name}: {faults} fault(s) in the plan, " +
                                     $"{stood.Gaps} cell(s) with no floor, {stood.RailGap:F1} m of railing missing, " +
                                     $"{stood.OnWalk} thing(s) in the way.");
            }
        }

        /// <summary>Every block prefab stood at the origin under the parent and measured,
        /// unplaced: what <see cref="CoreLayout.Arrange"/> deals with.</summary>
        public static List<CoreLayout.Block> Stand(Transform parent, System.Func<GameObject, Transform, GameObject> stand)
        {
            var blocks = new List<CoreLayout.Block>();
            foreach (var entry in CoreLayout.Blocks)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CoreLayout.BlocksDir + entry.Prefab + ".prefab");
                if (prefab == null)
                {
                    Debug.LogWarning($"[CoreCity] {CoreLayout.BlocksDir}{entry.Prefab}.prefab is missing; skipped.");
                    continue;
                }
                var go = stand(prefab, parent);
                go.name = entry.Prefab;
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                blocks.Add(CoreLayout.Measure(entry.Prefab, go));
            }
            return blocks;
        }

        /// <summary>Everything already standing in the scene, the drawing itself excepted.</summary>
        static bool Extent(Scene scene, out Bounds box)
        {
            box = new Bounds();
            bool any = false;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == CityRoot) continue;
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (!any) { box = r.bounds; any = true; }
                    else box.Encapsulate(r.bounds);
                }
            }
            return any;
        }

        static void Caption(string name, string text, Vector3 position, Transform parent)
        {
            BlockLotPads.PadLabel(name, text, position, parent);
            var caption = parent.Find(name);
            if (caption) caption.rotation = Quaternion.Euler(35f, 180f, 0f);
        }

        /// <summary>Turns the scene view onto the drawing, from the south and well above it,
        /// so the skyline reads - that is what it was drawn for.</summary>
        static void Frame(Vector3 centre, float span)
        {
            var view = SceneView.lastActiveSceneView;
            if (view == null) return;
            view.LookAt(centre, Quaternion.Euler(50f, 0f, 0f), span * 0.55f, false);
        }
    }
}
