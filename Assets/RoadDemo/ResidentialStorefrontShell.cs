using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RoadDemo
{
    /// <summary>One measured patch of shop glass in the local frame of its building.</summary>
    [Serializable]
    internal struct ResidentialStorefrontOpening
    {
        public Vector3 Front;
        public Vector3 Outward;
        public Vector3 Right;
        public float Width;
        public float Height;
        /// <summary>All panes cut from the same authored shop module share one business.</summary>
        public int Group;
        /// <summary>The diagonal pane/door of a cut corner: keep its approach unobstructed.</summary>
        public bool Entrance;
        /// <summary>This pane belongs to an authored two-facade corner-shop module.</summary>
        public bool Corner;

        public ResidentialStorefrontOpening(
            Vector3 front, Vector3 outward, Vector3 right, float width, float height,
            int group = -1, bool entrance = false, bool corner = false)
        {
            Front = front;
            Outward = outward;
            Right = right;
            Width = width;
            Height = height;
            Group = group;
            Entrance = entrance;
            Corner = corner;
        }
    }

    /// <summary>
    /// The shallow rooms behind every shop window carried by one residential unit, one
    /// renderer per facade.
    ///
    /// The meshes are deliberately rebuilt from serialized measurements rather than stored
    /// as generated Meshes per block. That keeps pooled units cheap, and a Residential
    /// review scene which is saved and reopened can recreate the same shallow rooms even
    /// though Unity does not serialize runtime meshes. The open front is the building's
    /// own glass; behind it is only an opaque back wall. There is no horizontal geometry
    /// which could read as loose boards from the game's high camera. Closed businesses add
    /// shallow roller-shutter slats in a second submesh of their facade.
    ///
    /// One renderer per facade, not one for the whole unit: the street cutaway shows the
    /// camera-facing ground floor exactly as authored and hides the rest, and it decides
    /// that per renderer. A single shell for all four sides has no side to be on.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    internal sealed class ResidentialStorefrontShell : MonoBehaviour
    {
        const string FacePrefix = "rooms ";
        const string MeshName = "Residential storefront shallow rooms";
        const float ShutterInset = 0.16f;
        const int ShutterSlats = 12;

        sealed class Face
        {
            public int Key;
            public GameObject Object;
            public MeshFilter Filter;
            public MeshRenderer Drawn;
            public Mesh Mesh;
        }

        [SerializeField] ResidentialStorefrontOpening[] openings =
            Array.Empty<ResidentialStorefrontOpening>();
        [SerializeField] int closedMask;

        Material material;
        Material shutterMaterial;
        readonly List<Face> faces = new List<Face>(4);
        readonly List<int> keys = new List<int>(4);

        readonly List<Vector3> vertices = new List<Vector3>(320);
        readonly List<Vector3> normals = new List<Vector3>(320);
        readonly List<Vector2> uvs = new List<Vector2>(320);
        readonly List<int> shellTriangles = new List<int>(480);
        readonly List<int> shutterTriangles = new List<int>(480);

        /// <summary>Rooms only; no bay is closed.</summary>
        internal void Configure(ResidentialStorefrontOpening[] measured,
                                Material shellMaterial)
        {
            Configure(measured, 0, shellMaterial, null);
        }

        /// <summary>Rooms behind every pane, and roller-shutter slats over the panes whose
        /// bit is set in <paramref name="closed"/>.</summary>
        internal void Configure(ResidentialStorefrontOpening[] measured, int closed,
                                Material shellMaterial, Material rollerMaterial)
        {
            measured ??= Array.Empty<ResidentialStorefrontOpening>();
            if (openings.Length != measured.Length) openings = new ResidentialStorefrontOpening[measured.Length];
            Array.Copy(measured, openings, measured.Length);
            closedMask = closed;

            material = shellMaterial;
            shutterMaterial = rollerMaterial;
            Rebuild();
        }

        internal ResidentialStorefrontOpening[] CopyOpenings()
        {
            var copy = new ResidentialStorefrontOpening[openings.Length];
            Array.Copy(openings, copy, openings.Length);
            return copy;
        }

        void OnEnable()
        {
            RetireSingleShell();
            if (openings.Length > 0 && !Built()) Rebuild();
        }

        bool Built()
        {
            if (faces.Count == 0) return false;
            for (int i = 0; i < faces.Count; i++)
                if (faces[i].Mesh == null || faces[i].Mesh.vertexCount == 0) return false;
            return true;
        }

        /// <summary>The single-renderer shell used to live on this object. A saved review
        /// scene or a pooled instance from before the split still carries its components;
        /// they must not draw beside the facade renderers.</summary>
        void RetireSingleShell()
        {
            if (TryGetComponent<MeshRenderer>(out var legacy) && legacy.enabled)
                legacy.enabled = false;
            if (TryGetComponent<MeshFilter>(out var legacyFilter) && legacyFilter.sharedMesh != null)
                legacyFilter.sharedMesh = null;
        }

        void Rebuild()
        {
            // A review scene saved before the split kept its materials on the root
            // renderer; a rebuild after a domain reload has nothing else to dress with.
            if (material == null && TryGetComponent<MeshRenderer>(out var legacy))
            {
                var kept = legacy.sharedMaterials;
                if (kept.Length > 0) material = kept[0];
                if (kept.Length > 1 && shutterMaterial == null) shutterMaterial = kept[1];
            }
            RetireSingleShell();

            keys.Clear();
            for (int i = 0; i < openings.Length; i++)
            {
                if (!HasRoom(i) && !HasShutter(i)) continue;
                int key = FaceKey(openings[i]);
                if (!keys.Contains(key)) keys.Add(key);
            }
            keys.Sort();

            faces.Clear();
            for (int n = 0; n < keys.Count; n++)
            {
                var face = Adopt(keys[n]);
                vertices.Clear();
                normals.Clear();
                uvs.Clear();
                shellTriangles.Clear();
                shutterTriangles.Clear();
                for (int i = 0; i < openings.Length; i++)
                {
                    if (FaceKey(openings[i]) != keys[n]) continue;
                    if (HasRoom(i)) AddRoom(openings[i]);
                    if (HasShutter(i)) AddShutter(openings[i]);
                }

                var mesh = face.Mesh;
                bool shuttered = shutterTriangles.Count > 0;
                mesh.Clear(false);
                mesh.SetVertices(vertices);
                mesh.SetNormals(normals);
                mesh.SetUVs(0, uvs);
                mesh.subMeshCount = shuttered ? 2 : 1;
                mesh.SetTriangles(shellTriangles, 0, true);
                if (shuttered) mesh.SetTriangles(shutterTriangles, 1, true);
                mesh.RecalculateBounds();
                face.Filter.sharedMesh = mesh;
                if (material != null)
                    face.Drawn.sharedMaterials = shuttered
                        ? new[] { material, shutterMaterial != null ? shutterMaterial : material }
                        : new[] { material };
                face.Drawn.enabled = vertices.Count > 0;
                face.Object.SetActive(true);
                faces.Add(face);
            }

            // A rebind with fewer facades leaves the surplus children asleep, so a pooled
            // unit never shows the rooms of its previous lease.
            for (int i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(i);
                if (!child.name.StartsWith(FacePrefix, StringComparison.Ordinal)) continue;
                bool used = false;
                for (int n = 0; n < faces.Count && !used; n++) used = faces[n].Object == child.gameObject;
                if (!used && child.gameObject.activeSelf) child.gameObject.SetActive(false);
            }
        }

        bool HasRoom(int i) => !openings[i].Entrance;

        bool HasShutter(int i) => i < 31 && (closedMask & (1 << i)) != 0;

        /// <summary>The facade a pane belongs to: which way it looks and how deep into the
        /// plan its wall stands, so the notch of an L and the outer wall it faces the same
        /// way as are two facades, not one.</summary>
        static int FaceKey(ResidentialStorefrontOpening opening)
        {
            Vector3 outward = Flat(opening.Outward, Vector3.forward);
            int side = Mathf.Abs(outward.x) > Mathf.Abs(outward.z)
                ? (outward.x >= 0f ? 1 : 3)
                : (outward.z >= 0f ? 2 : 0);
            int depth = Mathf.RoundToInt(Vector3.Dot(opening.Front, outward) / ResidentialLot.Cell);
            return side * 64 + Mathf.Clamp(depth + 32, 0, 63);
        }

        static string FaceName(int key)
        {
            string side = (key / 64) switch { 1 => "+X", 2 => "+Z", 3 => "-X", _ => "-Z" };
            return $"{FacePrefix}{side} {key % 64 - 32}";
        }

        Face Adopt(int key)
        {
            string name = FaceName(key);
            var child = transform.Find(name);
            if (child == null)
            {
                child = new GameObject(name).transform;
                child.SetParent(transform, false);
            }
            child.gameObject.layer = gameObject.layer;

            var face = new Face { Key = key, Object = child.gameObject };
            face.Filter = child.GetComponent<MeshFilter>();
            if (face.Filter == null) face.Filter = child.gameObject.AddComponent<MeshFilter>();
            face.Drawn = child.GetComponent<MeshRenderer>();
            if (face.Drawn == null) face.Drawn = child.gameObject.AddComponent<MeshRenderer>();

            var drawn = face.Drawn;
            drawn.shadowCastingMode = ShadowCastingMode.Off;
            drawn.receiveShadows = false;
            drawn.lightProbeUsage = LightProbeUsage.Off;
            drawn.reflectionProbeUsage = ReflectionProbeUsage.Off;
            drawn.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            drawn.allowOcclusionWhenDynamic = true;

            var existing = face.Filter.sharedMesh;
            face.Mesh = existing != null && existing.name == MeshName ? existing : NewMesh();
            return face;
        }

        static Mesh NewMesh()
        {
            var mesh = new Mesh
            {
                name = MeshName,
                hideFlags = HideFlags.HideAndDontSave,
            };
            mesh.MarkDynamic();
            return mesh;
        }

        void AddRoom(ResidentialStorefrontOpening opening)
        {
            Vector3 outward = Flat(opening.Outward, Vector3.forward);
            Vector3 right = Flat(opening.Right, Vector3.Cross(Vector3.up, outward));
            Vector3 inward = -outward;
            float width = Mathf.Clamp(opening.Width - 0.16f, 1.2f, 12f);
            float height = Mathf.Clamp(opening.Height, 2.15f, 3.15f);
            float depth = Mathf.Clamp(width * 0.22f, 0.8f, 1.25f);

            Vector3 far = opening.Front + inward * depth;
            Vector3 half = right * (width * 0.5f);
            Vector3 up = Vector3.up * height;

            Vector3 farLeft = far - half;
            Vector3 farRight = far + half;

            AddQuad(farLeft, farRight, farRight + up, farLeft + up, outward,
                    shellTriangles, new Vector2(width, height));
        }

        void AddShutter(ResidentialStorefrontOpening opening)
        {
            Vector3 outward = Flat(opening.Outward, Vector3.forward);
            Vector3 right = Flat(opening.Right, Vector3.Cross(Vector3.up, outward));
            float width = Mathf.Clamp(opening.Width - 0.22f, 1.15f, 12f);
            float height = Mathf.Clamp(opening.Height - 0.18f, 2f, 2.9f);
            Vector3 bottom = opening.Front - outward * ShutterInset + Vector3.up * 0.09f;
            Vector3 half = right * (width * 0.5f);
            float pitch = height / ShutterSlats;

            for (int i = 0; i < ShutterSlats; i++)
            {
                float y0 = i * pitch + 0.012f;
                float y1 = (i + 1) * pitch - 0.012f;
                // The alternating millimetre relief catches the street light and makes
                // the otherwise flat panel read as a roller shutter.
                Vector3 relief = outward * ((i & 1) == 0 ? 0.008f : 0f);
                Vector3 a = bottom - half + Vector3.up * y0 + relief;
                Vector3 b = bottom + half + Vector3.up * y0 + relief;
                Vector3 c = bottom + half + Vector3.up * y1 + relief;
                Vector3 d = bottom - half + Vector3.up * y1 + relief;
                AddQuad(a, b, c, d, outward, shutterTriangles, new Vector2(width, pitch));
            }
        }

        static Vector3 Flat(Vector3 value, Vector3 fallback)
        {
            value.y = 0f;
            if (value.sqrMagnitude < 0.25f) value = fallback;
            value.y = 0f;
            return value.normalized;
        }

        void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal,
                     List<int> triangles, Vector2 size)
        {
            int first = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
            normal.Normalize();
            normals.Add(normal); normals.Add(normal); normals.Add(normal); normals.Add(normal);
            uvs.Add(Vector2.zero);
            uvs.Add(new Vector2(size.x, 0f));
            uvs.Add(size);
            uvs.Add(new Vector2(0f, size.y));

            // Both windings keep these intentionally paper-thin surfaces correct for
            // authored shop modules whose negative scale mirrors their local facade.
            triangles.Add(first); triangles.Add(first + 1); triangles.Add(first + 2);
            triangles.Add(first); triangles.Add(first + 2); triangles.Add(first + 3);
            triangles.Add(first + 2); triangles.Add(first + 1); triangles.Add(first);
            triangles.Add(first + 3); triangles.Add(first + 2); triangles.Add(first);
        }

        void OnDestroy()
        {
            // Every facade child, not only the ones the last rebuild used: a child put to
            // sleep by a rebind with fewer facades still holds a mesh nothing else frees.
            for (int i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(i);
                if (!child.name.StartsWith(FacePrefix, StringComparison.Ordinal)) continue;
                var filter = child.GetComponent<MeshFilter>();
                var mesh = filter != null ? filter.sharedMesh : null;
                if (mesh == null || mesh.name != MeshName) continue;
                filter.sharedMesh = null;
                if (Application.isPlaying) Destroy(mesh);
                else DestroyImmediate(mesh);
            }
            faces.Clear();
        }
    }
}
