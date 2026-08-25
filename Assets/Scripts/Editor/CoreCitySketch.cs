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
    /// Draws the city core in the open scene, to be looked at: the harvested blocks where
    /// Synty stood them in the POLYGON City demo, the demo's streets opened to the city's
    /// widths by the cuts in <see cref="CoreLayout"/>, and the roads <see cref="CoreRoads"/>
    /// runs between them. The same two classes build the core in the game; this is their
    /// editor host, where the cuts get argued about (Docs/core-district-plan.md) - the log
    /// it leaves says how much ground the roads left over, and where.
    ///
    /// Re-running it wipes the last drawing. The raster is kept in
    /// <see cref="CoreRoads.Raster.Map"/> (read back through <see cref="LastMap"/>) so the
    /// drawing can be checked without a picture.
    /// </summary>
    public static class CoreCitySketch
    {
        internal const string CityRoot = "CORE CITY (sketch)";
        /// <summary>Clear ground between the drawing and the nearest thing already in the
        /// scene.</summary>
        const float Clearance = 60f;

        /// <summary>The last raster drawn, as text - a probe's view of the drawing.</summary>
        internal static string LastMap = "";

        [MenuItem("Tools/City/Core/Sketch The Core City", priority = 8)]
        public static void Sketch()
        {
            var scene = SceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == CityRoot) Object.DestroyImmediate(root);

            // the drawing stands clear of everything else: measured before anything is
            // added, moved into place once its size is known
            bool anyScene = Extent(scene, out Bounds others);

            var city = new GameObject(CityRoot);
            var blocks = new List<CoreLayout.Block>();
            foreach (var stand in CoreLayout.Blocks)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CoreLayout.BlocksDir + stand.Prefab + ".prefab");
                if (prefab == null)
                {
                    Debug.LogWarning($"[CoreCity] {CoreLayout.BlocksDir}{stand.Prefab}.prefab is missing; skipped.");
                    continue;
                }
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, city.transform);
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var block = CoreLayout.Measure(stand.Prefab, go, new Vector2(stand.X, stand.Z));
                CoreLayout.Place(block);
                blocks.Add(block);
            }
            if (blocks.Count == 0)
            {
                Object.DestroyImmediate(city);
                EditorUtility.DisplayDialog("Sketch The Core City",
                    $"No block prefabs found under {CoreLayout.BlocksDir}.", "OK");
                return;
            }

            var raster = CoreRoads.Build(blocks);
            LastMap = raster.Map;
            var roads = new GameObject("roads");
            roads.transform.SetParent(city.transform, false);
            CoreRoads.Lay(raster, (prefab, parent) => (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent),
                          roads.transform);

            var labels = new GameObject("labels");
            labels.transform.SetParent(city.transform, false);
            foreach (var block in blocks)
            {
                var box = block.Box;
                Caption($"{block.Name} label",
                    $"{block.Name}\n{block.MaxH:F0} m\nmoved ({block.Shift.x:+0;-0;0}, {block.Shift.y:+0;-0;0})",
                    new Vector3(box.center.x, Mathf.Max(12f, block.MaxH + 8f), box.center.y),
                    labels.transform);
            }

            float minX = raster.X0, maxX = raster.X(raster.NX), minZ = raster.Z0, maxZ = raster.Z(raster.NZ);
            Caption("city label",
                $"{CityRoot}\n{blocks.Count} blocks as Synty stood them\nstreets 15 m, the main road a 35 m boulevard",
                new Vector3((minX + maxX) * 0.5f, 75f, (minZ + maxZ) * 0.5f), labels.transform);
            if (anyScene)
                city.transform.position = new Vector3(others.min.x - Clearance - maxX, 0f,
                                                      others.center.z - (minZ + maxZ) * 0.5f);

            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = city;
            Frame(city.transform.position + new Vector3((minX + maxX) * 0.5f, 0f, (minZ + maxZ) * 0.5f),
                  Mathf.Max(maxX - minX, maxZ - minZ));

            var log = new System.Text.StringBuilder();
            log.AppendLine($"[CoreCity] {blocks.Count} blocks drawn under \"{CityRoot}\": {maxX - minX:F0} x {maxZ - minZ:F0} m. " +
                           $"Blocks {raster.BlockArea} m2, road {raster.RoadArea} m2, car parks {raster.ParkingArea} m2, " +
                           $"left over {raster.SpareArea} m2:");
            log.AppendLine(raster.Report);
            log.AppendLine("   blocks (moved by the cuts):");
            foreach (var block in blocks)
                log.AppendLine($"   {block.Name,-10} {block.CW * 5,3} x {block.CD * 5,3}  {block.MaxH,5:F1} m  " +
                               $"box x {block.Box.xMin,5:F0}..{block.Box.xMax,5:F0}  z {block.Box.yMin,5:F0}..{block.Box.yMax,5:F0}" +
                               $"  (demo x {block.DemoBox.xMin,5:F0}..{block.DemoBox.xMax,5:F0}  z {block.DemoBox.yMin,5:F0}..{block.DemoBox.yMax,5:F0})");
            Debug.Log(log.ToString());
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
