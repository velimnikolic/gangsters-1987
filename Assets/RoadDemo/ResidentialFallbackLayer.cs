using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RoadDemo
{
    /// <summary>A cheap building mass used while the detailed residential view is being
    /// composed. It is deliberately a building, never a green/park filler.</summary>
    public readonly struct ResidentialFallbackMass
    {
        public readonly Rect LocalFootprint;
        public readonly float Height;

        public ResidentialFallbackMass(Rect localFootprint, float height)
        {
            LocalFootprint = localFootprint;
            Height = height;
        }
    }

    /// <summary>Pure fallback plan shared by runtime composition and regression tests.</summary>
    public sealed class ResidentialFallbackDescription
    {
        readonly List<ResidentialFallbackMass> _masses;

        public string Id { get; }
        public string Name { get; }
        public Rect LocalBounds { get; }
        public IReadOnlyList<ResidentialFallbackMass> BuildingMasses => _masses;

        internal ResidentialFallbackDescription(
            string id, string name, Rect localBounds, List<ResidentialFallbackMass> masses)
        {
            Id = id;
            Name = name;
            LocalBounds = localBounds;
            _masses = masses;
        }
    }

    /// <summary>
    /// Converts a residential recipe into one concrete pad and a few coarse building
    /// volumes. This is not a second city generator: the masses come directly from the
    /// accepted recipe and exist only until its detailed RecyclerView holder is attached.
    /// </summary>
    public static class ResidentialFallbackGeometry
    {
        public const float GroundY = RoadDemoBuilder.RoadBed + 0.02f;

        public static List<ResidentialFallbackDescription> Describe(ResidentialBlockModel model)
        {
            var result = new List<ResidentialFallbackDescription>(model?.Count ?? 0);
            if (model == null) return result;
            for (int i = 0; i < model.Blocks.Count; i++)
                result.Add(Describe(model.Blocks[i]));
            return result;
        }

        public static ResidentialFallbackDescription Describe(ResidentialBlockRecipe recipe)
        {
            if (recipe == null) throw new ArgumentNullException(nameof(recipe));

            var masses = new List<ResidentialFallbackMass>();
            var plan = recipe.Plan;
            float width = Mathf.Max(0f, recipe.LocalBounds.width);
            float depth = Mathf.Max(0f, recipe.LocalBounds.height);
            if (plan?.Spots != null)
                for (int i = 0; i < plan.Spots.Count; i++)
                {
                    var spot = plan.Spots[i];
                    var unit = spot?.Unit;
                    if (unit == null || unit.Kind == ResidentialKind.Park) continue;

                    float height = Mathf.Clamp(
                        unit.MaxH > 0.01f ? unit.MaxH : recipe.VisualHeight * 0.5f,
                        6f, Mathf.Max(6f, recipe.VisualHeight));
                    var turn = ResidentialLot.Turn.Of(unit, spot.Yaw);
                    int firstMass = masses.Count;
                    for (int row = 0; row < turn.CD; row++)
                    {
                        int cell = 0;
                        while (cell < turn.CW)
                        {
                            while (cell < turn.CW && !turn.Wall(cell, row)) cell++;
                            if (cell >= turn.CW) break;
                            int from = cell++;
                            while (cell < turn.CW && turn.Wall(cell, row)) cell++;

                            float x0 = Mathf.Clamp(
                                (spot.I + from) * ResidentialLot.Cell, 0f, width);
                            float z0 = Mathf.Clamp(
                                (spot.J + row) * ResidentialLot.Cell, 0f, depth);
                            float x1 = Mathf.Clamp(
                                (spot.I + cell) * ResidentialLot.Cell, 0f, width);
                            float z1 = Mathf.Clamp(
                                (spot.J + row + 1) * ResidentialLot.Cell, 0f, depth);
                            if (x1 - x0 < 0.01f || z1 - z0 < 0.01f) continue;

                            // Stack equal row runs into one clean building wing instead of
                            // drawing a five-metre box seam for every occupied plan cell.
                            int join = -1;
                            for (int m = firstMass; m < masses.Count; m++)
                            {
                                var prior = masses[m];
                                if (Mathf.Abs(prior.LocalFootprint.xMin - x0) > 0.01f ||
                                    Mathf.Abs(prior.LocalFootprint.xMax - x1) > 0.01f ||
                                    Mathf.Abs(prior.LocalFootprint.yMax - z0) > 0.01f) continue;
                                join = m;
                                break;
                            }
                            if (join >= 0)
                            {
                                var prior = masses[join];
                                masses[join] = new ResidentialFallbackMass(
                                    Rect.MinMaxRect(x0, prior.LocalFootprint.yMin, x1, z1), height);
                            }
                            else
                                masses.Add(new ResidentialFallbackMass(
                                    Rect.MinMaxRect(x0, z0, x1, z1), height));
                        }
                    }
                }

            // A malformed recipe must still not uncover the city water. Building-marked
            // cells provide a conservative mass when a future planner omits its Spot.
            if (masses.Count == 0 && plan?.Ground != null)
                for (int j = 0; j < plan.D; j++)
                    for (int i = 0; i < plan.W; i++)
                    {
                        if (plan.Ground[i, j] != ResidentialLot.Use.Building) continue;
                        var cell = new Rect(
                            i * ResidentialLot.Cell, j * ResidentialLot.Cell,
                            ResidentialLot.Cell, ResidentialLot.Cell);
                        masses.Add(new ResidentialFallbackMass(
                            cell, Mathf.Clamp(recipe.VisualHeight * 0.5f, 6f, 14f)));
                    }

            return new ResidentialFallbackDescription(
                recipe.Id, recipe.Name, recipe.LocalBounds, masses);
        }

        internal static Mesh BuildMesh(ResidentialFallbackDescription description)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uv = new List<Vector2>();
            var ground = new List<int>();
            var buildings = new List<int>();

            float width = description.LocalBounds.width;
            float depth = description.LocalBounds.height;
            AddQuad(
                vertices, normals, uv, ground,
                new Vector3(0f, GroundY, 0f),
                new Vector3(0f, GroundY, depth),
                new Vector3(width, GroundY, depth),
                new Vector3(width, GroundY, 0f),
                Vector3.up,
                new Vector2(0f, 0f),
                new Vector2(0f, depth / ResidentialLot.Cell),
                new Vector2(width / ResidentialLot.Cell, depth / ResidentialLot.Cell),
                new Vector2(width / ResidentialLot.Cell, 0f));

            for (int i = 0; i < description.BuildingMasses.Count; i++)
                AddBox(vertices, normals, uv, buildings, description.BuildingMasses[i]);

            var mesh = new Mesh
            {
                name = $"Fallback {description.Name}",
                hideFlags = HideFlags.DontSave,
                indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16,
            };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uv);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(ground, 0, true);
            mesh.SetTriangles(buildings, 1, true);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            return mesh;
        }

        static void AddBox(
            List<Vector3> vertices, List<Vector3> normals, List<Vector2> uv,
            List<int> triangles, ResidentialFallbackMass mass)
        {
            var r = mass.LocalFootprint;
            float y0 = 0f, y1 = mass.Height;
            float u = Mathf.Max(1f, r.width / ResidentialLot.Cell);
            float v = Mathf.Max(1f, r.height / ResidentialLot.Cell);
            float h = Mathf.Max(1f, mass.Height / ResidentialLot.Cell);

            AddQuad(vertices, normals, uv, triangles,
                new Vector3(r.xMin, y1, r.yMin), new Vector3(r.xMax, y1, r.yMin),
                new Vector3(r.xMax, y1, r.yMax), new Vector3(r.xMin, y1, r.yMax),
                Vector3.up, Vector2.zero, new Vector2(u, 0f), new Vector2(u, v), new Vector2(0f, v));
            AddQuad(vertices, normals, uv, triangles,
                new Vector3(r.xMin, y0, r.yMin), new Vector3(r.xMin, y1, r.yMin),
                new Vector3(r.xMin, y1, r.yMax), new Vector3(r.xMin, y0, r.yMax),
                Vector3.left, Vector2.zero, new Vector2(0f, h), new Vector2(v, h), new Vector2(v, 0f));
            AddQuad(vertices, normals, uv, triangles,
                new Vector3(r.xMax, y0, r.yMax), new Vector3(r.xMax, y1, r.yMax),
                new Vector3(r.xMax, y1, r.yMin), new Vector3(r.xMax, y0, r.yMin),
                Vector3.right, Vector2.zero, new Vector2(0f, h), new Vector2(v, h), new Vector2(v, 0f));
            AddQuad(vertices, normals, uv, triangles,
                new Vector3(r.xMax, y0, r.yMin), new Vector3(r.xMax, y1, r.yMin),
                new Vector3(r.xMin, y1, r.yMin), new Vector3(r.xMin, y0, r.yMin),
                Vector3.back, Vector2.zero, new Vector2(0f, h), new Vector2(u, h), new Vector2(u, 0f));
            AddQuad(vertices, normals, uv, triangles,
                new Vector3(r.xMin, y0, r.yMax), new Vector3(r.xMin, y1, r.yMax),
                new Vector3(r.xMax, y1, r.yMax), new Vector3(r.xMax, y0, r.yMax),
                Vector3.forward, Vector2.zero, new Vector2(0f, h), new Vector2(u, h), new Vector2(u, 0f));
        }

        static void AddQuad(
            List<Vector3> vertices, List<Vector3> normals, List<Vector2> uv,
            List<int> triangles,
            Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal,
            Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            int first = vertices.Count;
            vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
            normals.Add(normal); normals.Add(normal); normals.Add(normal); normals.Add(normal);
            uv.Add(ua); uv.Add(ub); uv.Add(uc); uv.Add(ud);
            triangles.Add(first); triangles.Add(first + 1); triangles.Add(first + 2);
            triangles.Add(first); triangles.Add(first + 2); triangles.Add(first + 3);
        }
    }

    /// <summary>
    /// Always-ready residential silhouettes. A detailed recycler holder hides its own
    /// silhouette only after every renderer is attached; leaving or evicting the holder
    /// reveals it first. Fast camera movement can therefore reduce detail, never uncover
    /// the water plane or turn a programmed city block into an empty rectangle.
    /// </summary>
    public sealed class ResidentialFallbackLayer : MonoBehaviour
    {
        sealed class Entry
        {
            public GameObject Root;
            public Mesh Mesh;
            public Rect Bounds;
        }

        readonly Dictionary<string, Entry> _entries =
            new Dictionary<string, Entry>(StringComparer.Ordinal);
        ResidentialBlockModel _model;
        Material _ground;
        Material _building;

        public int BlockCount => _entries.Count;
        public int VisibleBlocks
        {
            get
            {
                int count = 0;
                foreach (var entry in _entries.Values)
                    if (entry.Root != null && entry.Root.activeSelf) count++;
                return count;
            }
        }

        public void Init(ResidentialBlockModel model)
        {
            if (_model != null) _model.Changed -= OnModelChanged;
            ClearEntries();
            _model = model ?? throw new ArgumentNullException(nameof(model));
            EnsureMaterials();
            for (int i = 0; i < _model.Blocks.Count; i++) Add(_model.Blocks[i]);
            _model.Changed += OnModelChanged;
        }

        public bool Contains(string recipeId) => recipeId != null && _entries.ContainsKey(recipeId);

        public bool TryGetLocalBounds(string recipeId, out Rect bounds)
        {
            if (recipeId != null && _entries.TryGetValue(recipeId, out var entry))
            {
                bounds = entry.Bounds;
                return true;
            }
            bounds = default;
            return false;
        }

        public void ShowFallback(string recipeId)
        {
            if (recipeId != null && _entries.TryGetValue(recipeId, out var entry) && entry.Root != null)
                entry.Root.SetActive(true);
        }

        public void HideFallback(string recipeId)
        {
            if (recipeId != null && _entries.TryGetValue(recipeId, out var entry) && entry.Root != null)
                entry.Root.SetActive(false);
        }

        void Add(ResidentialBlockRecipe recipe)
        {
            var description = ResidentialFallbackGeometry.Describe(recipe);
            var root = new GameObject($"Fallback {description.Name}");
            root.transform.SetParent(transform, false);
            root.transform.localPosition = new Vector3(
                description.LocalBounds.xMin, 0f, description.LocalBounds.yMin);
            root.isStatic = true;

            var mesh = ResidentialFallbackGeometry.BuildMesh(description);
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] { _ground, _building };
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            renderer.lightProbeUsage = LightProbeUsage.BlendProbes;

            _entries.Add(description.Id, new Entry
            {
                Root = root,
                Mesh = mesh,
                Bounds = description.LocalBounds,
            });
        }

        void OnModelChanged(ResidentialBlockRecipe recipe, ResidentialBlockChange change)
        {
            if (recipe == null)
            {
                ClearEntries();
                if (_model != null)
                    for (int i = 0; i < _model.Blocks.Count; i++) Add(_model.Blocks[i]);
                return;
            }

            Remove(recipe.Id);
            if (change != ResidentialBlockChange.Removed &&
                _model != null && _model.TryGet(recipe.Id, out var current)) Add(current);
        }

        void EnsureMaterials()
        {
            if (_ground != null && _building != null) return;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            _ground = MakeMaterial(shader, "Residential fallback concrete", new Color(0.34f, 0.33f, 0.32f));
            _building = MakeMaterial(shader, "Residential fallback buildings", new Color(0.49f, 0.34f, 0.27f));
        }

        static Material MakeMaterial(Shader shader, string name, Color colour)
        {
            if (shader == null) return null;
            var material = new Material(shader)
            {
                name = name,
                color = colour,
                hideFlags = HideFlags.DontSave,
            };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", colour);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.05f);
            return material;
        }

        void Remove(string id)
        {
            if (id == null || !_entries.TryGetValue(id, out var entry)) return;
            _entries.Remove(id);
            Discard(entry.Root);
            Discard(entry.Mesh);
        }

        void ClearEntries()
        {
            foreach (var entry in _entries.Values)
            {
                Discard(entry.Root);
                Discard(entry.Mesh);
            }
            _entries.Clear();
        }

        static void Discard(UnityEngine.Object item)
        {
            if (item == null) return;
            if (Application.isPlaying) Destroy(item);
            else DestroyImmediate(item);
        }

        void OnDestroy()
        {
            if (_model != null) _model.Changed -= OnModelChanged;
            _model = null;
            ClearEntries();
            Discard(_ground);
            Discard(_building);
            _ground = null;
            _building = null;
        }
    }
}
