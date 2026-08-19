using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RoadDemo
{
    // The perf pass, once, for whoever is hosting a scene: the city with its grid,
    // seams, island and districts, or a district's own demo scene. It was written
    // three times over (the city's, the port's, the suburb's) and drifted; this is
    // the one copy, and it works on whatever roots it is handed.
    //
    // Three things, in order:
    //   - Optimise: nothing that cannot throw a shadow anyone would see casts one,
    //     and the foliage and water keep their own renderers (a vertex shader moves
    //     their vertices in object space; a merged mesh would swing them around the
    //     merge pivot).
    //   - AssignCullLayers: the small stuff goes on layers the camera stops drawing
    //     past a range.
    //   - Merge: on the first frame, after every Start has seen the pieces, the still
    //     ones fold into a few hundred meshes.
    public static class ScenePerf
    {
        /// <summary>Renderers flatter than this cast no shadow: ground tiles, plates,
        /// decals, the chain on the ground.</summary>
        public const float FlatCasterHeight = 0.6f;

        public const int PropLayer = 20, CrowdLayer = 21, MidLayer = 22;
        public const float PropCullDistance = 230f, CrowdCullDistance = 330f, MidCullDistance = 480f;

        /// <summary>One root offered to the merge: whether each top-level child under it
        /// is its own chunk (a block bake, so a block culls as a block) and a salt that
        /// keeps two roots' pieces in the same 120 m cell apart when they should be.</summary>
        public struct MergeRoot
        {
            public Transform Root;
            public bool PerChildChunk;
            public int Salt;

            public static MergeRoot Of(Transform root, bool perChildChunk = false, int salt = 0)
                => new MergeRoot { Root = root, PerChildChunk = perChildChunk, Salt = salt };
        }

        // ------------------------------------------------------------- shadows

        /// <summary>Shadow pass over the roots that are going to be merged, plus the
        /// ones already combined (<paramref name="flatOnly"/>: only their flat pieces
        /// are looked at). Returns how many renderers are waiting for the merge.</summary>
        public static int Optimise(IList<Transform> roots, IList<Transform> flatOnly, string tag)
        {
            int batched = 0, flat = 0, kept = 0;
            var toBatch = new List<Renderer>();

            for (int r = 0; roots != null && r < roots.Count; r++)
            {
                var root = roots[r];
                if (root == null) continue;
                foreach (var mr in root.GetComponentsInChildren<MeshRenderer>())
                {
                    var mf = mr.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;
                    if (mr.gameObject.isStatic) continue;

                    if (Animated(mr.transform)) continue;
                    if (SwaysOrFlows(mr))
                    {
                        kept++;
                        if (IsWater(mr.name)) { mr.shadowCastingMode = ShadowCastingMode.Off; flat++; }
                        continue;
                    }
                    toBatch.Add(mr);
                    batched++;
                    if (mr.name == "Lawn") { mr.shadowCastingMode = ShadowCastingMode.Off; flat++; }
                }
            }

            for (int r = 0; flatOnly != null && r < flatOnly.Count; r++)
            {
                var root = flatOnly[r];
                if (root == null) continue;
                foreach (var mr in root.GetComponentsInChildren<MeshRenderer>())
                    if (mr.shadowCastingMode != ShadowCastingMode.Off && mr.bounds.size.y < FlatCasterHeight)
                    {
                        mr.shadowCastingMode = ShadowCastingMode.Off;
                        flat++;
                    }
            }
            foreach (var mr in toBatch)
                if (mr.shadowCastingMode != ShadowCastingMode.Off && mr.bounds.size.y < FlatCasterHeight)
                {
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                    flat++;
                }

            Debug.Log($"[{tag}] {batched} renderers to merge, {kept} foliage left to the wind, " +
                      $"{flat} flat renderers cast no shadow");
            return batched;
        }

        // ------------------------------------------------------- distance culling

        static readonly string[] NeverCulled =
            { "Quay", "Abutment", "Walkway", "Soffit", "Girder", "Post", "Deck", "Pier", "Fence",
              "Ground", "Sea", "Water", "Lawn", "Fountain", "Court", "Outfall",
              // the districts': a house or a shed is the quarter, like a block is the city
              "House", "Church", "Shop", "Garage", "Hedge", "Pump Canopy", "Shed", "Warehouse",
              "Crane", "Container", "Ship", "Gate", "Apron" };

        /// <summary>The small things onto the layers the camera drops at a range. A piece
        /// is classified by its top-level parent under the root, so a prop's parts travel
        /// together.</summary>
        public static void AssignCullLayers(IList<Transform> roots, string tag)
        {
            int props = 0, mid = 0;
            for (int r = 0; roots != null && r < roots.Count; r++)
            {
                var root = roots[r];
                if (root == null) continue;
                foreach (var mr in root.GetComponentsInChildren<MeshRenderer>())
                {
                    if (mr.gameObject.layer != 0) continue;
                    var piece = mr.transform;
                    while (piece.parent != null && piece.parent != root) piece = piece.parent;
                    if (System.Array.IndexOf(NeverCulled, piece.name) >= 0) continue;
                    var b = mr.bounds;
                    float h = b.size.y, w = Mathf.Max(b.size.x, b.size.z);
                    if (h < 0.5f) continue;                       // flat: tiles, plates, decals
                    if (h <= 2.8f && w <= 4.5f) { mr.gameObject.layer = PropLayer; props++; }
                    else if (h <= 12f && w <= 12f) { mr.gameObject.layer = MidLayer; mid++; }
                }
            }
            Debug.Log($"[{tag}] distance culling: {props} small props to {PropCullDistance:F0} m, " +
                      $"{mid} trees/lamps/poles to {MidCullDistance:F0} m, the crowd to {CrowdCullDistance:F0} m");
        }

        /// <summary>The camera's per-layer cull distances, the same for every host.</summary>
        public static void ApplyCullDistances(Camera cam)
        {
            var cull = new float[32];
            cull[PropLayer] = PropCullDistance;
            cull[CrowdLayer] = CrowdCullDistance;
            cull[MidLayer] = MidCullDistance;
            cam.layerCullDistances = cull;
            cam.layerCullSpherical = true;
        }

        // --------------------------------------------------------------- merge

        const float MergeCell = 120f;

        struct MergeKey : System.IEquatable<MergeKey>
        {
            public int Chunk;
            public Material Material;
            public int Layer;
            public ShadowCastingMode Shadows;
            public bool Equals(MergeKey o) => Chunk == o.Chunk && Material == o.Material && Layer == o.Layer && Shadows == o.Shadows;
            public override bool Equals(object o) => o is MergeKey k && Equals(k);
            public override int GetHashCode() => (Chunk * 397) ^ (Material ? Material.GetHashCode() : 0) ^ (Layer << 8) ^ ((int)Shadows << 12);
        }

        /// <summary>Fold the still geometry under these roots into merged meshes, one per
        /// (chunk, material, layer, shadow mode), and switch the originals' renderers off.
        /// Everything lands under <paramref name="mergedRoot"/>.</summary>
        public static void Merge(IList<MergeRoot> roots, Transform mergedRoot, string tag)
        {
            var groups = new Dictionary<MergeKey, List<CombineInstance>>();
            var chunkOf = new Dictionary<Transform, int>();
            var unreadable = new HashSet<string>();
            int nextChunk = 1;
            int pieces = 0, verts = 0;

            for (int r = 0; roots != null && r < roots.Count; r++)
            {
                var entry = roots[r];
                var root = entry.Root;
                if (root == null) continue;
                foreach (var mr in root.GetComponentsInChildren<MeshRenderer>())
                {
                    if (!mr.enabled) continue;
                    var mf = mr.GetComponent<MeshFilter>();
                    var mesh = mf ? mf.sharedMesh : null;
                    if (mesh == null) continue;
                    if (mr.gameObject.isStatic) continue;          // one mesh already
                    if (Animated(mr.transform) || SwaysOrFlows(mr)) continue;
                    // a mesh whose import forbids reading cannot be combined - it stays a
                    // renderer of its own, and its file is noted for the editor to open up
                    // (MeshReadAccess, off Logs/unreadable-meshes.txt, on the next reload)
                    if (!mesh.isReadable) { unreadable.Add(mesh.name); continue; }

                    int chunk;
                    if (entry.PerChildChunk)
                    {
                        var top = mr.transform;
                        while (top.parent != null && top.parent != root) top = top.parent;
                        if (!chunkOf.TryGetValue(top, out chunk)) chunkOf[top] = chunk = nextChunk++;
                    }
                    else
                    {
                        var p = mr.bounds.center;
                        chunk = 100000 + Mathf.FloorToInt(p.x / MergeCell) * 1000 + Mathf.FloorToInt(p.z / MergeCell)
                                + entry.Salt;
                    }

                    var mats = mr.sharedMaterials;
                    var matrix = mr.transform.localToWorldMatrix;
                    int subs = Mathf.Min(mats.Length, mesh.subMeshCount);
                    for (int i = 0; i < subs; i++)
                    {
                        if (mats[i] == null) continue;
                        var key = new MergeKey { Chunk = chunk, Material = mats[i], Layer = mr.gameObject.layer, Shadows = mr.shadowCastingMode };
                        if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<CombineInstance>();
                        list.Add(new CombineInstance { mesh = mesh, subMeshIndex = i, transform = matrix });
                    }
                    mr.enabled = false;
                    pieces++;
                    verts += mesh.vertexCount;
                }
            }

            int made = 0;
            foreach (var kv in groups)
            {
                var key = kv.Key;
                var merged = new Mesh { name = "Merged " + key.Material.name, indexFormat = IndexFormat.UInt32 };
                merged.CombineMeshes(kv.Value.ToArray(), true, true, false);
                merged.RecalculateBounds();
                var go = new GameObject(merged.name) { layer = key.Layer };
                go.transform.SetParent(mergedRoot, false);
                go.AddComponent<MeshFilter>().sharedMesh = merged;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = key.Material;
                mr.shadowCastingMode = key.Shadows;
                mr.receiveShadows = true;
                made++;
            }
            if (unreadable.Count > 0)
            {
                var dir = System.IO.Path.Combine(Application.dataPath, "..", "Logs");
                System.IO.Directory.CreateDirectory(dir);
                var path = System.IO.Path.Combine(dir, "unreadable-meshes.txt");
                // every host appends to the one list: the editor opens them all on reload
                var all = new SortedSet<string>(unreadable);
                if (System.IO.File.Exists(path))
                    foreach (var line in System.IO.File.ReadAllLines(path))
                        if (!string.IsNullOrWhiteSpace(line)) all.Add(line.Trim());
                System.IO.File.WriteAllLines(path, all);
                Debug.LogWarning($"[{tag}] {unreadable.Count} kinds of mesh could not be merged (import forbids " +
                                 "reading) and stay as their own renderers - the list is in Logs/unreadable-meshes.txt; " +
                                 "the editor opens them up on its next reload (MeshReadAccess), after which they merge too.");
            }
            Debug.Log($"[{tag}] merged {pieces} pieces ({verts / 1000}k verts) into {made} meshes");
        }

        // -------------------------------------------------------------- shared

        public static void SetLayerDeep(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform) SetLayerDeep(child.gameObject, layer);
        }

        static bool IsWater(string name) => name == "Water" || name == "Sea";

        /// <summary>Under something that turns or is otherwise driven at runtime -
        /// the fairground wheel; a merged piece cannot move.</summary>
        public static bool Animated(Transform t)
        {
            for (var p = t; p != null; p = p.parent)
                if (p.GetComponent<DemoFerrisWheel>() != null) return true;
            return false;
        }

        /// <summary>A material whose shader moves the vertices itself - the pack's
        /// foliage in the wind, the water - which merging would pin to the merge
        /// pivot instead of the piece's own.</summary>
        public static bool SwaysOrFlows(Renderer r)
        {
            if (IsWater(r.name)) return true;
            foreach (var m in r.sharedMaterials)
            {
                if (m == null || m.shader == null) continue;
                var name = m.shader.name;
                if (name.IndexOf("Foliage", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("Wind", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("Water", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("Ocean", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }
    }
}
