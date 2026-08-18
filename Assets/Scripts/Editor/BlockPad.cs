using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// The rectangle an authoring pass works on: one pad in BLOCK LOTS, or one saved
    /// block standing on its own pad in the BLOCK WORKBENCH.
    ///
    /// Every auto pass needs the same three answers - which pad the user means, what
    /// stands on it, and where its own output goes - so they live here once instead of
    /// once per pass. The output rule is what lets the passes run in any order: each
    /// writes under a root named "auto ..." (see <see cref="PropsRoot"/>,
    /// <see cref="ParkingRoot"/> and <see cref="FloorRoot"/>), a re-roll destroys only
    /// its own root, and the other passes decide by that name whether the contents are
    /// input (the floor reads the props and the cars, and beds them in grass or asphalt
    /// to suit; the props keep out of the parking) or ground to be ignored (the props
    /// must never take the floor for an obstacle, or a block dressed floor-first would
    /// come out bare).
    /// </summary>
    internal sealed class BlockPad
    {
        /// <summary>The composed buildings' own root, written by the randomiser. It is the
        /// only auto root that holds BUILDINGS rather than dressing, which is why the other
        /// passes see it as an obstacle like any hand-dragged block.</summary>
        internal const string BlockRoot = "auto block";

        internal const string PropsRoot = "auto props";

        /// <summary>The parking pass's own root. Its own command since the yard is a
        /// thing you either want cars in or do not - the scatter never lays any.</summary>
        internal const string ParkingRoot = "auto parking";

        internal const string FloorRoot = "auto floor";

        /// <summary>How far off a pad a thing may stand and still belong to it. The pads
        /// are 25 m apart, so this cannot reach the neighbour.</summary>
        internal const float Margin = 4f;

        internal string label;

        /// <summary>The lot pad's own code - "A1" for the 70 x 50 m pad. What a captured
        /// block is filed under, and how RoadDemoBuilder knows which interior it was
        /// composed for. A workbench pad carries the code of the pad its block was
        /// composed on.</summary>
        internal string code;

        internal Vector3 centre;
        internal float width, depth;

        /// <summary>The workbench group that owns this pad's content, or null for a bare
        /// lot pad - whose content is whatever the scene has standing over it.</summary>
        internal Transform group;

        internal float MinX => centre.x - width * 0.5f;
        internal float MaxX => centre.x + width * 0.5f;
        internal float MinZ => centre.z - depth * 0.5f;
        internal float MaxZ => centre.z + depth * 0.5f;

        internal bool Holds(Vector3 point, float margin = 0f) =>
            Mathf.Abs(point.x - centre.x) <= width * 0.5f + margin &&
            Mathf.Abs(point.z - centre.z) <= depth * 0.5f + margin;

        // ------------------------------------------------------------------- picking

        /// <summary>Every pad in the scene: the lot pads first, then the workbench.</summary>
        internal static List<BlockPad> All()
        {
            var scene = SceneManager.GetActiveScene();
            var pads = new List<BlockPad>();

            var lots = scene.GetRootGameObjects().FirstOrDefault(go => go.name == BlockLotPads.RootName);
            if (lots)
                foreach (Transform pad in lots.transform)
                    if (BlockLotCapture.TryReadPadName(pad.name, out var code, out var w, out var d))
                        pads.Add(new BlockPad
                        {
                            label = $"lot {code}",
                            code = code,
                            centre = new Vector3(pad.position.x, 0f, pad.position.z),
                            width = w,
                            depth = d,
                        });

            var bench = scene.GetRootGameObjects().FirstOrDefault(go => go.name == BlockLotCapture.WorkbenchRoot);
            if (bench)
                foreach (Transform block in bench.transform)
                    if (BlockLotCapture.TryReadWorkbenchName(block.name, out var name, out var code,
                                                             out var w, out var d))
                        pads.Add(new BlockPad
                        {
                            label = $"block {name}",
                            code = code,
                            centre = new Vector3(block.position.x, 0f, block.position.z),
                            width = w,
                            depth = d,
                            group = block,
                        });

            return pads;
        }

        /// <summary>The pad the user means: the one the selection stands on (or belongs
        /// to, for a workbench block), or the only one holding anything at all. Says what
        /// to select when it cannot tell, and returns false having explained itself.</summary>
        internal static bool TryPick(out BlockPad pad) => TryPick(out pad, requireContent: true);

        /// <summary>
        /// As above, but a pass that BUILDS a block rather than dressing one wants an EMPTY
        /// pad - so it asks with <paramref name="requireContent"/> false and the fallback
        /// "the only pad in use" (and the advice to go and drag a building in) is dropped:
        /// with nothing standing anywhere, every pad is an equally good answer and only the
        /// selection can say which.
        /// </summary>
        internal static bool TryPick(out BlockPad pad, bool requireContent)
        {
            pad = null;
            var pads = All();
            if (pads.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "No pads",
                    "This scene has no lot pads. Run Tools/City/Catalog/Draw Block Lot Pads first.", "OK");
                return false;
            }

            var selected = Selection.activeTransform;
            if (selected)
            {
                // a selection inside a workbench group belongs to it whatever its
                // bounds say - a block may well overhang its own pad while being edited
                for (var node = selected; node; node = node.parent)
                {
                    var owner = pads.FirstOrDefault(p => p.group == node);
                    if (owner != null)
                    {
                        pad = owner;
                        break;
                    }
                }
                pad ??= pads.FirstOrDefault(p => p.Holds(selected.position, Margin));
            }

            if (pad != null)
                return true;

            if (!requireContent)
            {
                EditorUtility.DisplayDialog(
                    "Which lot?",
                    "Select the lot you mean - its painted pad under BLOCK LOTS, or anything " +
                    "standing on it - and run this again.", "OK");
                return false;
            }

            var busy = pads.Where(p => p.Occupied()).ToList();
            if (busy.Count == 1)
            {
                pad = busy[0];
                return true;
            }

            EditorUtility.DisplayDialog(
                "Which block?",
                busy.Count == 0
                    ? "No pad holds anything yet. Drag catalog buildings onto one of the " +
                      "BLOCK LOTS pads first - a block is composed before it is dressed."
                    : "More than one pad is in use: " + string.Join(", ", busy.Select(b => b.label)) +
                      ".\n\nSelect something on the one you mean and run this again.",
                "OK");
            return false;
        }

        /// <summary>Whether anything the user put here stands on the pad. An auto floor
        /// does not count - it is this toolchain's own output, and a pad carrying only a
        /// floor is still an empty pad.</summary>
        internal bool Occupied()
        {
            foreach (var renderer in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude))
            {
                if (!Holds(renderer.bounds.center, Margin))
                    continue;
                if (group && !renderer.transform.IsChildOf(group))
                    continue;
                if (IsPadFurniture(renderer.transform) || Under(renderer.transform, FloorRoot))
                    continue;
                return true;
            }
            return false;
        }

        // -------------------------------------------------------------------- output

        /// <summary>A fresh, empty root for one auto pass, the previous one destroyed:
        /// a child of the workbench group when there is one, a scene root named after the
        /// pad otherwise. Nothing the user placed by hand can ever be under it.</summary>
        internal Transform ResetAuto(string rootName)
        {
            ClearAuto(rootName);

            if (group)
            {
                var child = new GameObject(rootName);
                child.transform.SetParent(group, false);
                child.transform.position = centre;
                return child.transform;
            }

            var loose = new GameObject($"{label} {rootName}");
            loose.transform.position = centre;
            return loose.transform;
        }

        /// <summary>The destroying half of <see cref="ResetAuto"/>, for a pass that is
        /// throwing another pass's output away rather than rewriting its own: true when
        /// there was one to throw.</summary>
        internal bool ClearAuto(string rootName)
        {
            if (group)
            {
                var stale = group.Find(rootName);
                if (!stale)
                    return false;
                Object.DestroyImmediate(stale.gameObject);
                return true;
            }

            var name = $"{label} {rootName}";
            var found = false;
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                if (root.name == name)
                {
                    Object.DestroyImmediate(root);
                    found = true;
                }
            return found;
        }

        /// <summary>Pads, the painted lot planes and the floating captions: ground and
        /// signage, never block content.</summary>
        internal static bool IsPadFurniture(Transform node)
        {
            for (var t = node; t; t = t.parent)
                if (t.name == "pad" || t.name == BlockLotPads.RootName ||
                    t.name.StartsWith("Lot ") || t.name.EndsWith(" label") ||
                    t.GetComponent<TextMesh>())
                    return true;
            return false;
        }

        /// <summary>Whether the node stands under an auto root of this name - both the
        /// workbench child ("auto floor") and the loose scene root ("lot A1 auto floor").</summary>
        internal static bool Under(Transform node, string rootName)
        {
            for (var t = node; t; t = t.parent)
                if (t.name == rootName || t.name.EndsWith(" " + rootName))
                    return true;
            return false;
        }

        // ------------------------------------------------------------------- content

        /// <summary>One thing standing on the pad, resolved back to the asset it came
        /// from - which is what tells a building from a prop, the same rule the capture
        /// pass writes recipes by.</summary>
        internal sealed class Item
        {
            internal Transform node;
            internal string path;       // asset path; empty when nothing resolved it
            internal bool building;
            internal Bounds bounds;     // world renderer bounds

            internal Rect Footprint => Rect.MinMaxRect(bounds.min.x, bounds.min.z,
                                                       bounds.max.x, bounds.max.z);

            /// <summary>Geometry reaching below the pad - the skatepark bowl and its
            /// like. A floor laid under one of those would cap the hole.</summary>
            internal bool Digs => bounds.min.y < -0.2f;
        }

        /// <summary>
        /// Everything standing on the pad, one entry per prefab instance (buildings
        /// nested inside another block resolve to their own catalog prefab, exactly as
        /// the capture pass reads them). The auto floor is always skipped - it is the
        /// ground being planned, not content - and the auto props are included or not as
        /// the caller asks.
        /// </summary>
        internal List<Item> Contents(bool withAutoProps)
        {
            var items = new List<Item>();
            if (group)
                foreach (Transform child in group)
                    Walk(child, items, withAutoProps);
            else
                foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                {
                    if (root.name == BlockLotPads.RootName || root.name == BlockLotCapture.WorkbenchRoot ||
                        root.name == SyntyPropShowroom.RootName)
                        continue;
                    Walk(root.transform, items, withAutoProps);
                }
            return items;
        }

        void Walk(Transform node, List<Item> items, bool withAutoProps)
        {
            if (!node.gameObject.activeInHierarchy)
                return;
            if (node.GetComponent<TextMesh>() || node.name == "pad" || node.name.EndsWith(" label") ||
                node.name.StartsWith("Lot "))
                return;
            if (node.name == FloorRoot || node.name.EndsWith(" " + FloorRoot))
                return;
            if (!withAutoProps && (node.name == PropsRoot || node.name.EndsWith(" " + PropsRoot)))
                return;

            var bounds = BlockLotCapture.RendererBounds(node.gameObject);
            if (!bounds.HasValue)
                return;                     // nothing to see anywhere below here

            if (PrefabUtility.IsAnyPrefabInstanceRoot(node.gameObject))
            {
                Add(items, node, bounds.Value);
                return;
            }

            // Not an instance root: look for instances underneath, and only when there
            // are none is this one loose object (a prop cloned into a bake keeps the
            // prefab's name but loses the link).
            var before = items.Count;
            foreach (Transform child in node)
                Walk(child, items, withAutoProps);
            if (items.Count == before && node.GetComponent<MeshRenderer>())
                Add(items, node, bounds.Value);
        }

        void Add(List<Item> items, Transform node, Bounds bounds)
        {
            if (!Holds(bounds.center, Margin))
                return;
            var path = BlockLotCapture.SourcePathOf(node);
            items.Add(new Item
            {
                node = node,
                path = path,
                building = !string.IsNullOrEmpty(path) && BlockLotCapture.IsBuilding(path),
                bounds = bounds,
            });
        }
    }
}
