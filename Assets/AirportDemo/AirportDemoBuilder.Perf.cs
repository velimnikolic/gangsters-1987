using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace AirportDemo
{
    // The road demo's perf pass, for an airfield. There is a great deal of ground
    // here - a kilometre and a half of runway, eighty thousand square metres of ramp
    // - but very little on it, so the work is not in the buildings: it is in the
    // hundred and thirty airfield lights, the three hundred quads of paint, the
    // fence by the panel and the cars in the car park.
    //
    // Small things go on layers the camera stops drawing past a range; nothing
    // flatter than a kerb casts a shadow; and everything that never moves is merged
    // into one mesh per 150 m chunk, material, layer and shadow mode on the first
    // Update - a bigger chunk than the city uses, because the field is bigger and
    // emptier than a city block.
    public partial class AirportDemoBuilder
    {
        public const int PropLayer = 20, CrowdLayer = 21, MidLayer = 22;
        public const float PropCullDistance = 260f, CrowdCullDistance = 340f, MidCullDistance = 620f;
        const float FlatCasterHeight = 0.6f;
        const float MergeCell = 150f;

        /// <summary>What never gets culled by distance, whatever its size: the field's
        /// own furniture, which is what tells you what you are looking at from a
        /// kilometre away.</summary>
        static readonly string[] NeverCulled =
        {
            "Hangar", "Terminal", "Control tower", "Fire station", "Air freight", "FBO",
            "Fuel farm", "Comms mast", "Apron mast", "Windsock", "PAPI", "Fence", "Ground", "Runway", "Taxiway", "Ramp",
        };

        void AssignCullLayers()
        {
            int props = 0, mid = 0;
            foreach (var root in new[] { _airsideRoot, _apronRoot, _buildingRoot, _fenceRoot, _landsideRoot, _detailRoot, _floraRoot, _lightRoot })
            {
                if (root == null) continue;
                foreach (var mr in root.GetComponentsInChildren<MeshRenderer>())
                {
                    if (mr.gameObject.layer != 0) continue;
                    var piece = mr.transform;
                    while (piece.parent != null && piece.parent != root) piece = piece.parent;
                    bool keep = false;
                    foreach (var n in NeverCulled)
                        if (piece.name.StartsWith(n) || piece.name.Contains(n)) keep = true;
                    if (keep) continue;
                    var b = mr.bounds;
                    float h = b.size.y, w = Mathf.Max(b.size.x, b.size.z);
                    if (h < 0.5f) continue;                        // flat: paint, tiles, plates
                    if (h <= 2.8f && w <= 4.5f) { mr.gameObject.layer = PropLayer; props++; }
                    else if (h <= 12f && w <= 12f) { mr.gameObject.layer = MidLayer; mid++; }
                }
            }
            Debug.Log($"[AirportDemo] distance culling: {props} small props to {PropCullDistance:F0} m, {mid} poles and lamps to {MidCullDistance:F0} m");
        }

        void OptimiseScene()
        {
            int flat = 0, stripped = 0;
            foreach (var root in new[] { _groundRoot, _airsideRoot, _markingRoot, _lightRoot, _apronRoot, _buildingRoot, _fenceRoot, _landsideRoot, _detailRoot, _floraRoot })
            {
                if (root == null) continue;
                foreach (var col in root.GetComponentsInChildren<Collider>()) { Destroy(col); stripped++; }
                foreach (var mr in root.GetComponentsInChildren<MeshRenderer>())
                {
                    if (mr.shadowCastingMode != ShadowCastingMode.Off && mr.bounds.size.y < FlatCasterHeight)
                    {
                        mr.shadowCastingMode = ShadowCastingMode.Off;
                        flat++;
                    }
                }
            }
            Debug.Log($"[AirportDemo] {flat} flat renderers cast no shadow, {stripped} colliders dropped");
        }

        struct MergeKey
        {
            public int Chunk;
            public Material Material;
            public int Layer;
            public ShadowCastingMode Shadows;
            public bool Equals(MergeKey o) => Chunk == o.Chunk && Material == o.Material && Layer == o.Layer && Shadows == o.Shadows;
            public override bool Equals(object o) => o is MergeKey k && Equals(k);
            public override int GetHashCode() => (Chunk * 397) ^ (Material ? Material.GetHashCode() : 0) ^ (Layer << 8) ^ ((int)Shadows << 12);
        }

        void MergeStaticGeometry()
        {
            if (_merged) return;
            _merged = true;
            var groups = new Dictionary<MergeKey, List<CombineInstance>>();
            var unreadable = new HashSet<string>();
            int pieces = 0, verts = 0;

            foreach (var root in new[] { _airsideRoot, _markingRoot, _lightRoot, _apronRoot, _buildingRoot, _fenceRoot, _landsideRoot, _detailRoot })
            {
                if (root == null) continue;
                foreach (var mr in root.GetComponentsInChildren<MeshRenderer>())
                {
                    if (!mr.enabled) continue;
                    var mf = mr.GetComponent<MeshFilter>();
                    var mesh = mf ? mf.sharedMesh : null;
                    if (mesh == null) continue;
                    if (SwaysOrFlows(mr)) continue;
                    if (!mesh.isReadable) { unreadable.Add(mesh.name); continue; }

                    var p = mr.bounds.center;
                    int chunk = 100000 + Mathf.FloorToInt(p.x / MergeCell) * 1000 + Mathf.FloorToInt(p.z / MergeCell);
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

            _mergedRoot = new GameObject("Merged").transform;
            int made = 0;
            foreach (var kv in groups)
            {
                var key = kv.Key;
                var merged = new Mesh { name = "Merged " + key.Material.name, indexFormat = IndexFormat.UInt32 };
                merged.CombineMeshes(kv.Value.ToArray(), true, true, false);
                merged.RecalculateBounds();
                var go = new GameObject(merged.name) { layer = key.Layer };
                go.transform.SetParent(_mergedRoot, false);
                go.AddComponent<MeshFilter>().sharedMesh = merged;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = key.Material;
                mr.shadowCastingMode = key.Shadows;
                mr.receiveShadows = true;
                made++;
            }
            if (unreadable.Count > 0)
            {
                var path = System.IO.Path.Combine(Application.dataPath, "..", "Logs", "unreadable-meshes.txt");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                System.IO.File.WriteAllLines(path, unreadable);
                Debug.LogWarning($"[AirportDemo] {unreadable.Count} kinds of mesh could not be merged (import forbids reading) and stay " +
                                 "as their own renderers - the list is in Logs/unreadable-meshes.txt; the editor opens them up on its next " +
                                 "reload (MeshReadAccess), after which they merge too.");
            }
            Debug.Log($"[AirportDemo] merged {pieces} pieces ({verts / 1000}k verts) into {made} meshes");
        }

        /// <summary>A material whose shader moves the vertices itself, which a merge
        /// would pin to the merged mesh's own pivot.</summary>
        static bool SwaysOrFlows(Renderer r)
        {
            foreach (var m in r.sharedMaterials)
            {
                if (m == null || m.shader == null) continue;
                var name = m.shader.name;
                if (name.IndexOf("Foliage", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("Wind", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("Water", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }
    }
}
