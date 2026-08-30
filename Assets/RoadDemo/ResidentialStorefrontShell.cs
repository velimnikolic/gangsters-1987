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
    /// A single renderer closes every shop window carried by one residential unit.
    ///
    /// The mesh is deliberately rebuilt from serialized measurements rather than stored
    /// as one generated Mesh per block. That keeps pooled units cheap, and a Residential
    /// review scene which is saved and reopened can recreate the same shallow rooms even
    /// though Unity does not serialize runtime meshes. The open front is the building's
    /// own glass; behind it is only an opaque back wall. There is no horizontal geometry
    /// which could read as loose boards from the game's high camera.
    /// Closed businesses add shallow roller-shutter slats in the second submesh.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    internal sealed class ResidentialStorefrontShell : MonoBehaviour
    {
        const float ShutterInset = 0.16f;
        const int ShutterSlats = 12;

        [SerializeField] ResidentialStorefrontOpening[] openings =
            Array.Empty<ResidentialStorefrontOpening>();
        [SerializeField] int closedMask;

        MeshFilter filter;
        MeshRenderer drawn;
        Mesh mesh;

        readonly List<Vector3> vertices = new List<Vector3>(320);
        readonly List<Vector3> normals = new List<Vector3>(320);
        readonly List<Vector2> uvs = new List<Vector2>(320);
        readonly List<int> shellTriangles = new List<int>(480);
        readonly List<int> shutterTriangles = new List<int>(480);

        internal void Configure(ResidentialStorefrontOpening[] measured, int closed,
                                Material shellMaterial, Material shutterMaterial)
        {
            measured ??= Array.Empty<ResidentialStorefrontOpening>();
            if (openings.Length != measured.Length) openings = new ResidentialStorefrontOpening[measured.Length];
            Array.Copy(measured, openings, measured.Length);
            closedMask = closed;

            EnsureComponents();
            drawn.sharedMaterials = new[] { shellMaterial, shutterMaterial };
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
            EnsureComponents();
            if (openings.Length > 0 && (mesh == null || mesh.vertexCount == 0)) Rebuild();
        }

        void EnsureComponents()
        {
            filter ??= GetComponent<MeshFilter>();
            if (filter == null) filter = gameObject.AddComponent<MeshFilter>();
            drawn ??= GetComponent<MeshRenderer>();
            if (drawn == null) drawn = gameObject.AddComponent<MeshRenderer>();

            drawn.shadowCastingMode = ShadowCastingMode.Off;
            drawn.receiveShadows = false;
            drawn.lightProbeUsage = LightProbeUsage.Off;
            drawn.reflectionProbeUsage = ReflectionProbeUsage.Off;
            drawn.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            drawn.allowOcclusionWhenDynamic = true;

            if (mesh != null) return;
            mesh = new Mesh
            {
                name = "Residential storefront shallow rooms",
                hideFlags = HideFlags.HideAndDontSave,
            };
            mesh.MarkDynamic();
            filter.sharedMesh = mesh;
        }

        void Rebuild()
        {
            EnsureComponents();
            vertices.Clear();
            normals.Clear();
            uvs.Clear();
            shellTriangles.Clear();
            shutterTriangles.Clear();

            for (int i = 0; i < openings.Length; i++)
            {
                if (!openings[i].Entrance) AddRoom(openings[i]);
                if (i < 31 && (closedMask & (1 << i)) != 0) AddShutter(openings[i]);
            }

            mesh.Clear(false);
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(shellTriangles, 0, true);
            mesh.SetTriangles(shutterTriangles, 1, true);
            mesh.RecalculateBounds();
            filter.sharedMesh = mesh;
            drawn.enabled = vertices.Count > 0;
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
                // the otherwise single-renderer panel read as a roller shutter.
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
            if (mesh == null) return;
            if (Application.isPlaying) Destroy(mesh);
            else DestroyImmediate(mesh);
            mesh = null;
        }
    }
}
