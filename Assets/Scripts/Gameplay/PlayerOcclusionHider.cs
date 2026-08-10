using System.Collections.Generic;
using LivingCity.Entities;
using LivingCity.Generation;
using UnityEngine;
using UnityEngine.Rendering;

namespace LivingCity.Gameplay
{
    /// <summary>
    /// The camera is a free-roam orthographic boom and buildings are solid opaque
    /// shells, so a player who walks behind a facade simply vanishes. This hides
    /// whatever city building stands between him and the camera - hiding, not
    /// fading, because the whole city shares one opaque atlas material (and a
    /// dozen shared tint variants), so per-renderer transparency does not exist
    /// without shader surgery. shadowCastingMode = ShadowsOnly is the one safe
    /// per-building channel: the mesh disappears, its ground shadow stays so the
    /// street does not visibly lose light, and the collider stays on so bullets,
    /// witness sightlines and movement still treat the invisible wall as solid.
    ///
    /// A bare disappearance reads as a hole in the city, so a hidden building
    /// leaves its bottom fifth standing: the source mesh is clipped once at 20%
    /// of its local height (cut triangles split on the CPU, cached per shared
    /// mesh - terrace pieces reuse one mesh, so the whole city needs only a
    /// handful of stubs) and shown by a pooled stand-in renderer wearing the
    /// building's own materials, so tint and night windows carry over. The stub
    /// casts no shadow - the hidden original still casts the full one.
    /// </summary>
    public sealed class PlayerOcclusionHider : MonoBehaviour
    {
        const float ChestHeight = 1.1f;        // mid-capsule; with the radius the sweep spans the whole silhouette
        const float CastRadius = 0.9f;
        const float BackOffset = 2f;           // SphereCast ignores colliders the start sphere overlaps, and a wall
                                               // the player is hugging is exactly the worst occluder - start the
                                               // sweep on the far side of him instead
        const float CastDistance = 200f;       // > tallest building / sin(45 deg); the boom itself is 200
        const float KeepHiddenSeconds = 0.25f; // hysteresis so skirting a facade edge does not flicker
        const float MinOccluderHeight = 5f;    // parked cars and kiosks live in the same flat category
        const float StubFraction = 0.2f;       // how much of the building stays standing while hidden

        // Everything except pedestrians (the player himself) and the park-nav
        // proxy roots on layer 10 - the same idiom as PlayerMafioso.WallMask.
        static readonly int Mask = ~((1 << PedestrianSpawner.PedestrianLayer) | (1 << 10));

        struct HiddenEntry
        {
            public ShadowCastingMode Original;
            public float LastSeen;
            public GameObject Stub;
        }

        PlayerMafioso player;
        Camera cam;
        Transform buildingsRoot;

        readonly RaycastHit[] hits = new RaycastHit[64];
        readonly Dictionary<MeshRenderer, HiddenEntry> hidden = new();
        readonly List<MeshRenderer> scratch = new();
        readonly List<GameObject> stubPool = new();
        readonly Dictionary<Mesh, Mesh> stubMeshes = new();

        void Update()
        {
            Sweep();
            Restore();
        }

        void Sweep()
        {
            if (!player)
                player = FindAnyObjectByType<PlayerMafioso>();
            if (!cam)
                cam = Camera.main;
            if (!buildingsRoot)
            {
                var builder = FindAnyObjectByType<CityBuilder>();
                var root = builder ? builder.GeneratedRoot : null;
                buildingsRoot = root ? root.Find("Buildings") : null;
            }
            if (!player || !cam || !buildingsRoot)
                return;

            // Orthographic, so player-to-camera is simply -forward everywhere.
            var forward = cam.transform.forward;
            var origin = player.transform.position + Vector3.up * ChestHeight + forward * BackOffset;
            var count = Physics.SphereCastNonAlloc(origin, CastRadius, -forward, hits,
                                                   CastDistance + BackOffset, Mask,
                                                   QueryTriggerInteraction.Ignore);
            for (var i = 0; i < count; i++)
            {
                var collider = hits[i].collider;
                if (!collider || collider.transform.parent != buildingsRoot)
                    continue;

                // A building is one GameObject: collider and renderer side by side.
                var renderer = collider.GetComponent<MeshRenderer>();
                if (!renderer || renderer.bounds.size.y < MinOccluderHeight)
                    continue;

                if (hidden.TryGetValue(renderer, out var entry))
                {
                    entry.LastSeen = Time.time;
                    hidden[renderer] = entry;
                }
                else
                {
                    hidden[renderer] = new HiddenEntry
                    {
                        Original = renderer.shadowCastingMode,
                        LastSeen = Time.time,
                        Stub = ShowStub(renderer),
                    };
                    renderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                }
            }
        }

        void Restore()
        {
            if (hidden.Count == 0)
                return;

            scratch.Clear();
            foreach (var pair in hidden)
                if (!pair.Key || Time.time - pair.Value.LastSeen > KeepHiddenSeconds)
                    scratch.Add(pair.Key);

            foreach (var renderer in scratch)
            {
                var entry = hidden[renderer];
                if (renderer)
                    renderer.shadowCastingMode = entry.Original;
                ReclaimStub(entry.Stub);
                hidden.Remove(renderer);
            }
        }

        /// <summary>Pooled stand-in showing the clipped bottom of the building, in
        /// the building's exact pose and materials. Null when the mesh cannot be
        /// clipped (unreadable) - the building then just hides.</summary>
        GameObject ShowStub(MeshRenderer building)
        {
            var filter = building.GetComponent<MeshFilter>();
            var source = filter ? filter.sharedMesh : null;
            var clipped = source ? StubMesh(source) : null;
            if (!clipped)
                return null;

            GameObject stub;
            if (stubPool.Count > 0)
            {
                stub = stubPool[^1];
                stubPool.RemoveAt(stubPool.Count - 1);
            }
            else
            {
                stub = new GameObject("BuildingStub");
                stub.transform.SetParent(transform, false);
                stub.AddComponent<MeshFilter>();
                var r = stub.AddComponent<MeshRenderer>();
                r.shadowCastingMode = ShadowCastingMode.Off; // the hidden original still casts the full shadow
            }

            stub.GetComponent<MeshFilter>().sharedMesh = clipped;
            stub.GetComponent<MeshRenderer>().sharedMaterials = building.sharedMaterials;

            var t = building.transform;
            stub.transform.SetPositionAndRotation(t.position, t.rotation);
            stub.transform.localScale = t.lossyScale;
            stub.SetActive(true);
            return stub;
        }

        void ReclaimStub(GameObject stub)
        {
            if (!stub)
                return;
            stub.SetActive(false);
            stubPool.Add(stub);
        }

        /// <summary>
        /// The source mesh with everything above 20% of its local height cut away.
        /// Triangles straddling the cut are split against the plane (Sutherland-
        /// Hodgman on each), so the rim is a clean horizontal line and atlas UVs
        /// stay continuous. Computed once per shared mesh and cached; a null is
        /// cached too so an unreadable mesh is not retried every frame.
        ///
        /// isReadable must be checked, not caught: in Play mode Unity does not
        /// throw on a non-readable mesh, it logs an error and hands back EMPTY
        /// arrays - the stub would silently be nothing. BuildingMeshReadability
        /// (editor side) flips Read/Write on the building models so this branch
        /// stays dead in practice.
        /// </summary>
        Mesh StubMesh(Mesh source)
        {
            if (stubMeshes.TryGetValue(source, out var cached))
                return cached;

            var result = source.isReadable
                ? ClipBelow(source, source.bounds.min.y + StubFraction * source.bounds.size.y)
                : null;
            stubMeshes[source] = result;
            return result;
        }

        struct ClipVertex
        {
            public Vector3 Position;
            public Vector3 Normal;
            public Vector2 Uv;

            public static ClipVertex Lerp(in ClipVertex a, in ClipVertex b, float t) => new()
            {
                Position = Vector3.LerpUnclamped(a.Position, b.Position, t),
                Normal = Vector3.Slerp(a.Normal, b.Normal, t),
                Uv = Vector2.LerpUnclamped(a.Uv, b.Uv, t),
            };
        }

        static Mesh ClipBelow(Mesh source, float cutY)
        {
            var positions = source.vertices;
            var normals = source.normals;
            var uvs = source.uv;
            var hasNormals = normals != null && normals.Length == positions.Length;
            var hasUvs = uvs != null && uvs.Length == positions.Length;

            var outVerts = new List<ClipVertex>(positions.Length);
            var subIndices = new List<int>[source.subMeshCount];
            var corners = new ClipVertex[3];
            var poly = new ClipVertex[4]; // a plane-clipped triangle has at most 4 corners

            for (var sub = 0; sub < source.subMeshCount; sub++)
            {
                var indices = subIndices[sub] = new List<int>();
                var tris = source.GetTriangles(sub);
                for (var i = 0; i < tris.Length; i += 3)
                {
                    for (var c = 0; c < 3; c++)
                    {
                        var v = tris[i + c];
                        corners[c] = new ClipVertex
                        {
                            Position = positions[v],
                            Normal = hasNormals ? normals[v] : Vector3.up,
                            Uv = hasUvs ? uvs[v] : Vector2.zero,
                        };
                    }

                    var n = 0;
                    for (var c = 0; c < 3; c++)
                    {
                        var current = corners[c];
                        var next = corners[(c + 1) % 3];
                        var currentIn = current.Position.y <= cutY;
                        var nextIn = next.Position.y <= cutY;
                        if (currentIn)
                            poly[n++] = current;
                        if (currentIn != nextIn)
                        {
                            var t = (cutY - current.Position.y) / (next.Position.y - current.Position.y);
                            poly[n++] = ClipVertex.Lerp(current, next, t);
                        }
                    }

                    // Fan-triangulate whatever survived (0, 3 or 4 corners).
                    for (var c = 1; c + 1 < n; c++)
                    {
                        indices.Add(Emit(outVerts, poly[0]));
                        indices.Add(Emit(outVerts, poly[c]));
                        indices.Add(Emit(outVerts, poly[c + 1]));
                    }
                }
            }

            var mesh = new Mesh
            {
                name = source.name + " (stub)",
                indexFormat = UnityEngine.Rendering.IndexFormat.UInt32,
            };
            var finalPositions = new List<Vector3>(outVerts.Count);
            var finalNormals = new List<Vector3>(outVerts.Count);
            var finalUvs = new List<Vector2>(outVerts.Count);
            foreach (var v in outVerts)
            {
                finalPositions.Add(v.Position);
                finalNormals.Add(v.Normal);
                finalUvs.Add(v.Uv);
            }
            mesh.SetVertices(finalPositions);
            mesh.SetNormals(finalNormals);
            mesh.SetUVs(0, finalUvs);
            mesh.subMeshCount = source.subMeshCount;
            for (var sub = 0; sub < source.subMeshCount; sub++)
                mesh.SetTriangles(subIndices[sub], sub);
            mesh.RecalculateBounds();
            return mesh;
        }

        static int Emit(List<ClipVertex> verts, in ClipVertex v)
        {
            verts.Add(v);
            return verts.Count - 1;
        }

        void OnDisable()
        {
            foreach (var pair in hidden)
            {
                if (pair.Key)
                    pair.Key.shadowCastingMode = pair.Value.Original;
                ReclaimStub(pair.Value.Stub);
            }
            hidden.Clear();
        }

        void OnDestroy()
        {
            foreach (var mesh in stubMeshes.Values)
                if (mesh)
                    Destroy(mesh);
            stubMeshes.Clear();
        }
    }
}
