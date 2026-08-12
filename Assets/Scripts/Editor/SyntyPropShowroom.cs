using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// Every loose prop both Synty packs ship, one instance each, lined up west of
    /// the lot pads in the catalog scene: trees, benches, bins, lamps, signage - the
    /// small stuff a block is dressed with once its buildings stand.
    ///
    /// The packs already ship each prop as its own prefab, so nothing is baked here.
    /// The instances keep their prefab link: drag one onto a lot pad and it stays a
    /// prefab instance, and the name in the hierarchy is the prefab name, which is
    /// what a block recipe needs to name it.
    ///
    /// Rows are laid out by measured footprint rather than a fixed step - a palm and
    /// a bollard cannot share one cell size - and each family (Bench_Seat, Tree_Palm,
    /// ...) gets one label at the item it starts on, so the field stays readable
    /// without a label per prop.
    /// </summary>
    public static class SyntyPropShowroom
    {
        internal const string RootName = "PROPS";

        /// <summary>Prop-bearing folders of both packs. Buildings, vehicles,
        /// characters, weapons and FX are deliberately absent - this is dressing.</summary>
        static readonly (string title, string dir)[] Sources =
        {
            ("PALM CITY PROPS", "Assets/Synty/PolygonPalmCity/Prefabs/Props"),
            ("PALM CITY ENVIRONMENT", "Assets/Synty/PolygonPalmCity/Prefabs/Environment"),
            ("PALM CITY SIGNS", "Assets/Synty/PolygonPalmCity/Prefabs/Signs"),
            ("CITY PROPS", "Assets/Synty/PolygonCity/Prefabs/Props"),
            ("CITY ENVIRONMENT", "Assets/Synty/PolygonCity/Prefabs/Environments"),
        };

        const float RowWidth = 240f;   // how far east a row runs before it wraps
        const float Gap = 2.5f;        // between two props in a row
        const float RowGap = 6f;       // between rows of one section
        const float SectionGap = 20f;  // between sections
        const float Clearance = 60f;   // clear of the lot pads
        const float Cell = 5f;

        [MenuItem("Tools/City/Draw Prop Showroom", priority = 7)]
        public static void DrawShowroom()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != SyntyBuildingCatalog.ScenePath)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    return;
                scene = EditorSceneManager.OpenScene(SyntyBuildingCatalog.ScenePath,
                                                     OpenSceneMode.Single);
            }

            int shown = Draw();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[Props] {shown} props on show in {SyntyBuildingCatalog.ScenePath} " +
                      $"under \"{RootName}\".");
        }

        /// <summary>Lays the showroom out west of the lot pads, level with them.
        /// Safe to call twice - an older set is removed first. Returns the count.</summary>
        internal static int Draw()
        {
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                if (root.name == RootName)
                    Object.DestroyImmediate(root);

            var props = new GameObject(RootName);
            Anchor(out float x0, out float z);

            var shown = 0;
            try
            {
                foreach (var (title, dir) in Sources)
                {
                    var prefabs = LoadFolder(dir);
                    if (prefabs.Count == 0)
                    {
                        Debug.LogWarning($"[Props] nothing under {dir}");
                        continue;
                    }

                    Header(title, new Vector3(x0 - Gap * 2f, 0f, z), props.transform);

                    float cursor = x0, rowDepth = 0f;
                    string family = null;
                    for (var k = 0; k < prefabs.Count; k++)
                    {
                        var prefab = prefabs[k];
                        if (EditorUtility.DisplayCancelableProgressBar(
                                "Prop showroom", $"{title}: {prefab.name}",
                                (k + 1f) / prefabs.Count))
                            return shown;

                        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                        instance.transform.SetParent(props.transform, true);
                        var bounds = Footprint(instance);
                        if (bounds.size.x <= 0f && bounds.size.z <= 0f)
                        {
                            Object.DestroyImmediate(instance);   // nothing to look at
                            continue;
                        }

                        if (cursor > x0 && cursor - x0 + bounds.size.x > RowWidth)
                        {
                            cursor = x0;
                            z += rowDepth + RowGap;
                            rowDepth = 0f;
                        }

                        // south-west corner of the footprint onto the cursor, so
                        // neighbours never touch whatever the pivot happens to be
                        instance.transform.position += new Vector3(
                            cursor - bounds.min.x, 0f, z - bounds.min.z);

                        var name = FamilyOf(prefab.name);
                        if (name != family)
                        {
                            Label(name, new Vector3(cursor, bounds.size.y + 1.5f, z),
                                  props.transform);
                            family = name;
                        }

                        cursor += bounds.size.x + Gap;
                        rowDepth = Mathf.Max(rowDepth, bounds.size.z);
                        shown++;
                    }

                    z += rowDepth + SectionGap;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            return shown;
        }

        /// <summary>West of the lot pads and level with their south edge; west of the
        /// whole showroom when no pads have been drawn yet.</summary>
        static void Anchor(out float x0, out float z0)
        {
            var pads = RendererBounds(GameObject.Find(BlockLotPads.RootName));
            if (pads.HasValue)
            {
                x0 = Snap(pads.Value.min.x - Clearance - RowWidth);
                z0 = Snap(pads.Value.min.z);
                return;
            }

            var scene = SceneBounds();
            x0 = Snap(scene.min.x - Clearance - RowWidth);
            z0 = Snap(scene.max.z + Clearance);
        }

        static List<GameObject> LoadFolder(string dir)
        {
            var found = new List<GameObject>();
            if (!AssetDatabase.IsValidFolder(dir)) return found;

            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { dir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                // top level only: the packs keep their kit-bashed variants in
                // subfolders, and those belong to whoever built them
                if (System.IO.Path.GetDirectoryName(path).Replace('\\', '/') != dir) continue;

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab && prefab.GetComponentInChildren<MeshRenderer>())
                    found.Add(prefab);
            }

            found.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return found;
        }

        /// <summary>Trailing variant number off the name: SM_Prop_Bench_Seat_02 and
        /// _01 are one family, and the pack's prefix is noise on a label.</summary>
        static string FamilyOf(string name)
        {
            var cut = name.LastIndexOf('_');
            if (cut > 0 && name.Substring(cut + 1).All(char.IsDigit))
                name = name.Substring(0, cut);

            foreach (var prefix in new[] { "SM_Prop_", "SM_Env_", "SM_Sign_", "SM_Pro_" })
                if (name.StartsWith(prefix))
                    return name.Substring(prefix.Length);
            return name;
        }

        static Bounds Footprint(GameObject instance)
        {
            var b = RendererBounds(instance);
            return b ?? new Bounds(instance.transform.position, Vector3.zero);
        }

        static Bounds? RendererBounds(GameObject go)
        {
            if (!go) return null;
            var bounds = new Bounds();
            var first = true;
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
            {
                if (first) { bounds = r.bounds; first = false; }
                else bounds.Encapsulate(r.bounds);
            }
            return first ? (Bounds?)null : bounds;
        }

        static Bounds SceneBounds()
        {
            var bounds = new Bounds();
            var first = true;
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude))
            {
                if (first) { bounds = r.bounds; first = false; }
                else bounds.Encapsulate(r.bounds);
            }
            return bounds;
        }

        static float Snap(float v) => Mathf.Round(v / Cell) * Cell;

        static void Label(string text, Vector3 position, Transform parent)
        {
            var go = new GameObject($"{text} label");
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(35f, 0f, 0f));

            var mesh = go.AddComponent<TextMesh>();
            mesh.text = text;
            mesh.fontSize = 48;
            mesh.characterSize = 0.16f;
            mesh.anchor = TextAnchor.LowerLeft;
            mesh.alignment = TextAlignment.Left;
            mesh.color = new Color(0.8f, 0.9f, 1f);
        }

        static void Header(string title, Vector3 position, Transform parent)
        {
            var header = new GameObject($"{title} header");
            header.transform.SetParent(parent, false);
            header.transform.SetPositionAndRotation(position + new Vector3(0f, 12f, 0f),
                                                    Quaternion.Euler(35f, 0f, 0f));

            var text = header.AddComponent<TextMesh>();
            text.text = title;
            text.fontSize = 96;
            text.characterSize = 0.6f;
            text.anchor = TextAnchor.LowerRight;
            text.alignment = TextAlignment.Right;
            text.color = new Color(1f, 0.85f, 0.4f);
        }
    }
}
