using System.Collections;
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

        /// <summary>Names (by their start) that never cull either: the airfield's own
        /// furniture, which is what tells you what you are looking at from a kilometre
        /// away - a hangar, the tower, the windsock, the PAPI boxes, the runway.</summary>
        static readonly string[] AlwaysVisible =
            { "Hangar", "Terminal", "Control tower", "Fire station", "Air freight", "FBO", "Fuel farm",
              "Comms mast", "Apron mast", "Windsock", "PAPI", "Runway", "Taxiway", "Ramp", "Belt Deck" };

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
                    bool always = false;
                    foreach (var n in AlwaysVisible) if (piece.name.StartsWith(n)) { always = true; break; }
                    if (always) continue;
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

        /// <summary>How tall a piece has to be before the merge bothers to remember which
        /// chunk swallowed it. What asks is the cutaway (<see cref="StreetCutaway"/>), and
        /// it only ever asks about buildings - a kerb, a bin or a parked car is not one,
        /// and there are a hundred thousand of those.</summary>
        public const float CutawayHeight = 5f;

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
            var steps = MergeSteps(roots, mergedRoot, tag);
            while (steps.MoveNext()) { }
        }

        /// <summary>Whether a merge is folding a scene in right now. While it is, nothing
        /// may touch a source renderer: the gather reads each one's shadow mode INTO the
        /// group key, so a building hidden a frame too early (<see cref="StreetCutaway"/>
        /// hides by shadow mode) would be merged as a shadows-only group and never come
        /// back. The fold-in is a second or two at the start of a scene, and the cutaway
        /// stands down for it.</summary>
        public static bool Merging => _merging > 0;
        static int _merging;

        /// <summary>The merge, one step at a time. While it is still GATHERING it yields
        /// every few thousand renderers and after every root, and nothing is switched off
        /// yet - the whole city keeps drawing as its own pieces. While it is BUILDING it
        /// yields after every merged mesh, and a source is switched off only in the step
        /// its LAST group is built, so no piece is ever both un-merged and un-drawn: the
        /// frames in between look exactly like the finished merge, only with more draw
        /// calls. Drain it in one frame (<see cref="Merge"/>) for the old behaviour, or
        /// pump it against a millisecond budget to fold the city in over a second of play
        /// instead of one locked frame.</summary>
        public static IEnumerator MergeSteps(IList<MergeRoot> roots, Transform mergedRoot, string tag)
        {
            _merging++;
            var groups = new Dictionary<MergeKey, List<CombineInstance>>();
            // which renderers fed each group, and how many groups each renderer still has
            // left to be built: a source is only safe to hide once that count hits zero,
            // which is what keeps the frame-by-frame build free of gaps
            var owners = new Dictionary<MergeKey, List<MeshRenderer>>();
            var pending = new Dictionary<MeshRenderer, int>();
            var mrKeys = new HashSet<MergeKey>();
            var sources = new HashSet<Mesh>();
            var chunkOf = new Dictionary<Transform, int>();
            // one holder per chunk, carrying the receipt the cutaway reads: it takes the
            // merged meshes as children and remembers the pieces they stand for
            var chunks = new Dictionary<int, MergedChunk>();
            MergedChunk ChunkObject(int id)
            {
                if (chunks.TryGetValue(id, out var found)) return found;
                var holder = new GameObject("Chunk " + id);
                holder.transform.SetParent(mergedRoot, false);
                chunks[id] = found = holder.AddComponent<MergedChunk>();
                return found;
            }
            var unreadable = new HashSet<string>();
            int nextChunk = 1;
            int walked = 0;
            const int GatherYieldEvery = 2500;
            int pieces = 0;
            // long, not int: the city's merge is already a hundred and eighty million
            // vertices and an int runs out at two billion
            long verts = 0;

            for (int r = 0; roots != null && r < roots.Count; r++)
            {
                var entry = roots[r];
                var root = entry.Root;
                if (root == null) continue;
                // what this one root costs, so the tally below says WHERE the geometry
                // is - the streets, the block bakes, or the wilderness on the island -
                // instead of only how much of it there is. Which of them is worth
                // thinning is not a thing to guess at.
                int rootPieces = 0;
                long rootVerts = 0;
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

                    // From here the piece is the chunk's: it will be switched off below
                    // and the chunk is the only thing that can give it back. Buildings -
                    // tall, and carrying the footprint collider every bake has - are
                    // registered for reverse lookup too; nothing else is ever asked for.
                    ChunkObject(chunk).Adopt(mr, mr.bounds.size.y >= CutawayHeight
                                                 && mr.TryGetComponent<Collider>(out _));

                    var mats = mr.sharedMaterials;
                    var matrix = mr.transform.localToWorldMatrix;
                    int subs = Mathf.Min(mats.Length, mesh.subMeshCount);
                    mrKeys.Clear();
                    for (int i = 0; i < subs; i++)
                    {
                        if (mats[i] == null) continue;
                        var key = new MergeKey { Chunk = chunk, Material = mats[i], Layer = mr.gameObject.layer, Shadows = mr.shadowCastingMode };
                        if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<CombineInstance>();
                        list.Add(new CombineInstance { mesh = mesh, subMeshIndex = i, transform = matrix });
                        mrKeys.Add(key);
                    }
                    // NOT switched off here: the source keeps drawing until the LAST of its
                    // groups is built (below), which is what lets the build run across frames
                    // without a piece ever being un-merged and un-drawn at the same time.
                    if (mrKeys.Count > 0)
                    {
                        pending[mr] = mrKeys.Count;
                        foreach (var k in mrKeys)
                        {
                            if (!owners.TryGetValue(k, out var owned)) owners[k] = owned = new List<MeshRenderer>();
                            owned.Add(mr);
                        }
                    }
                    else mr.enabled = false;    // fed no group (all-null materials): drew nothing anyway
                    sources.Add(mesh);
                    pieces++;
                    verts += mesh.vertexCount;
                    rootPieces++;
                    rootVerts += mesh.vertexCount;
                    // gather switches nothing off, so a yield here is free of any gap - it
                    // only bounds how long one frame spends walking a big root
                    if (++walked >= GatherYieldEvery) { walked = 0; yield return null; }
                }
                if (rootPieces > 0)
                    Debug.Log($"[{tag}] merge: {root.name} is {rootPieces} renderers, {rootVerts / 1000}k verts");
                yield return null;
            }

            int made = 0;
            long mergedBytes = 0, mergedBytesLean = 0;
            int strippedGroups = 0;
            var channelBytes = new Dictionary<VertexAttribute, long>();
            foreach (var kv in groups)
            {
                var key = kv.Key;
                // 32-bit indices only where they are actually needed. A merged group of
                // ten thousand vertices - which is what the average one is - indexes fine
                // in 16 bits, and the wider format simply doubles its index buffer for
                // nothing. The count below is an upper bound (a CombineInstance that takes
                // one submesh still counts its mesh whole), so a group that comes out at
                // 16 bits was never going to overflow.
                long bound = 0;
                var parts = kv.Value;
                for (int i = 0; i < parts.Count; i++) bound += parts[i].mesh.vertexCount;
                var merged = new Mesh
                {
                    name = "Merged " + key.Material.name,
                    indexFormat = bound < 65536 ? IndexFormat.UInt16 : IndexFormat.UInt32,
                };
                merged.CombineMeshes(parts.ToArray(), true, true, false);
                merged.RecalculateBounds();

                // The merge's real price is the vertex buffer it CREATES: the sources stay
                // loaded (this only disables their renderers), so every merged vertex is a
                // second copy of geometry the process already holds. Measure it, in the
                // bytes the card is actually asked for, and then stop paying for channels
                // nothing reads.
                //
                // TANGENTS are the big one: a Vector4, 16 bytes of every vertex, and they
                // exist for normal mapping. 30 of the 768 pack materials carry a normal map
                // and they are lasers, fire and tyres - no facade has one - while
                // FacadeTint compiles its tangent-space path behind
                // `shader_feature_local _NORMALMAP`, which no city material enables. A mesh
                // without tangents hands the shader a default; nothing samples it.
                // Guarded per material anyway, so the day a facade DOES take a normal map
                // its group keeps them.
                mergedBytes += VertexBytes(merged);
                if (!NeedsTangents(key.Material))
                {
                    merged.tangents = null;
                    strippedGroups++;
                }

                // TexCoord1 is the lightmap channel, and it cannot have a reader here:
                // the merge builds its renderers with `new GameObject` at runtime, and a
                // runtime object is never in a lightmap - there is no bake to be in, the
                // project holds no lightmap assets, and `generateSecondaryUV: 0` means
                // Unity never authored the channel either (it rides in from the FBX).
                // FacadeTint's LIGHTMAP_ON/DYNAMICLIGHTMAP_ON variants stay unselected
                // for these renderers. Measured at 457 MB of the merge. Structural, not
                // a judgement call - unlike the tangents, this one needs no per-material
                // guard.
                merged.uv2 = null;

                mergedBytesLean += VertexBytes(merged);
                Tally(channelBytes, merged);
                // and then hand it to the GPU and let go of it. A mesh built at runtime is
                // readable by default, which means Unity keeps the whole vertex buffer in
                // system memory as well as on the card - and the city's merge is a hundred
                // and eighty million vertices, so that second copy is gigabytes of a thing
                // nothing ever reads back. Nothing does: the merged root carries no
                // MeshCollider, the map samples LandHeight rather than the ground mesh, and
                // the occlusion hider's stub cutter already asks isReadable first and skips
                // what says no.
                merged.UploadMeshData(true);
                var chunkObject = ChunkObject(key.Chunk);
                var go = new GameObject(merged.name) { layer = key.Layer };
                go.transform.SetParent(chunkObject.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = merged;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = key.Material;
                mr.shadowCastingMode = key.Shadows;
                mr.receiveShadows = true;
                chunkObject.StandsFor(mr);
                made++;

                // this group now draws as one mesh, so the sources that fed it and have no
                // other group still to come are switched off here - a renderer feeding two
                // groups waits for its second, so nothing blinks out early
                if (owners.TryGetValue(key, out var owned))
                    for (int i = 0; i < owned.Count; i++)
                    {
                        var src = owned[i];
                        if (!pending.TryGetValue(src, out var n)) continue;
                        if (n <= 1) { src.enabled = false; pending.Remove(src); }
                        else pending[src] = n - 1;
                    }
                yield return null;
            }
            // The sources are read ONCE, by the combine just above, and never again: their
            // renderers are disabled from here on, so nothing draws them and nothing reads
            // them back. But they are readable - MeshReadAccess opened Read/Write on the
            // pack models precisely so this merge could combine them - and a readable mesh
            // keeps a full copy in SYSTEM memory beside the one on the card. Measured:
            // Mesh Memory 7,834 MB against merged buffers of 2,931 MB, so ~4.9 GB is the
            // sources, carrying that second copy for a pass that is already over.
            //
            // UploadMeshData(true) frees it - the same call the merged meshes get, applied
            // to the input instead of the output. The one consumer that would notice is
            // PlayerOcclusionHider's stub cutter, and it asks isReadable first and caches
            // the refusal, so it degrades to "no stub" rather than breaking. (In this scene
            // it never even runs: it needs a CityBuilder and Game.unity has none.)
            // Every group is built, so each chunk now stands for exactly what it
            // swallowed and may be taken apart on demand. Before this line it is half
            // folded and pulling it apart would leave a hole, which is what Ready says.
            foreach (var kv in chunks) kv.Value.Ready = true;
            _merging = Mathf.Max(0, _merging - 1);

            int freed = 0;
            foreach (var mesh in sources)
            {
                if (!mesh || !mesh.isReadable) continue;
                mesh.UploadMeshData(true);
                freed++;
            }
            Debug.Log($"[{tag}] released the CPU copy of {freed} source meshes ({sources.Count} consumed)");

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
            Debug.Log($"[{tag}] merge vertex buffers: {mergedBytes / 1048576} MB -> {mergedBytesLean / 1048576} MB " +
                      $"({(mergedBytes - mergedBytesLean) / 1048576} MB of dead channels dropped: " +
                      $"tangents from {strippedGroups}/{made} groups, lightmap UVs from all)");

            // What is left, channel by channel, dearest first. Position, normal and uv0
            // are the job; anything else on this list is a candidate for the same
            // treatment the tangents got - but only after it is measured, never before.
            var channels = new System.Text.StringBuilder($"[{tag}] merge vertex channels:");
            var order = new List<KeyValuePair<VertexAttribute, long>>(channelBytes);
            order.Sort((a, b) => b.Value.CompareTo(a.Value));
            foreach (var kv in order)
                channels.Append($" {kv.Key} {kv.Value / 1048576} MB;");
            Debug.Log(channels.ToString());
        }

        /// <summary>Per-channel bytes across the merged set, straight off the vertex
        /// layout the mesh actually ended up with - dimension times format width, not
        /// an assumption about what CombineMeshes produced.</summary>
        static void Tally(Dictionary<VertexAttribute, long> into, Mesh m)
        {
            foreach (var a in m.GetVertexAttributes())
            {
                long bytes = (long)a.dimension * FormatBytes(a.format) * m.vertexCount;
                into.TryGetValue(a.attribute, out var had);
                into[a.attribute] = had + bytes;
            }
        }

        static int FormatBytes(VertexAttributeFormat f) => f switch
        {
            VertexAttributeFormat.Float32 or VertexAttributeFormat.UInt32 or VertexAttributeFormat.SInt32 => 4,
            VertexAttributeFormat.Float16 or VertexAttributeFormat.UNorm16 or VertexAttributeFormat.SNorm16
                or VertexAttributeFormat.UInt16 or VertexAttributeFormat.SInt16 => 2,
            _ => 1,
        };

        /// <summary>What the card is actually asked for, summed over every vertex
        /// stream - the stride is the honest per-vertex cost, not a guess at which
        /// channels the combine happened to produce.</summary>
        static long VertexBytes(Mesh m)
        {
            long bytes = 0;
            for (int s = 0; s < m.vertexBufferCount; s++)
                bytes += (long)m.GetVertexBufferStride(s) * m.vertexCount;
            return bytes;
        }

        /// <summary>A material that samples a normal map needs the tangent frame; one
        /// that does not is carrying 16 bytes a vertex for nothing.</summary>
        static bool NeedsTangents(Material m)
        {
            if (!m) return true;                       // unknown: keep them
            if (m.IsKeywordEnabled("_NORMALMAP")) return true;
            return m.HasProperty(BumpMap) && m.GetTexture(BumpMap);
        }

        static readonly int BumpMap = Shader.PropertyToID("_BumpMap");

        // -------------------------------------------------------------- shared

        public static void SetLayerDeep(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform) SetLayerDeep(child.gameObject, layer);
        }

        static bool IsWater(string name) => name == "Water" || name == "Sea";

        /// <summary>Under something that turns or is otherwise driven at runtime -
        /// the fairground wheel, the bridge that opens; a merged piece cannot move.</summary>
        public static bool Animated(Transform t)
        {
            for (var p = t; p != null; p = p.parent)
                if (p.GetComponent<DemoFerrisWheel>() != null || p.GetComponent<Bascule>() != null) return true;
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
