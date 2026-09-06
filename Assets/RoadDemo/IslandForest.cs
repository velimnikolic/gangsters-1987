using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RoadDemo
{
    /// <summary>Scenic vegetation as spatial GPU batches. No colliders, agents or simulation ticks.</summary>
    public sealed class IslandForest : MonoBehaviour
    {
        sealed class Part { public Mesh Mesh; public Material Material; public int Sub; public Matrix4x4 Local; }
        sealed class Species { public readonly List<Part> Parts = new List<Part>(); public float Base, Height; }
        sealed class Batch
        {
            public Part Part;
            public Bounds Bounds;
            public Matrix4x4[] Matrices;
            public readonly List<Matrix4x4> Pending = new List<Matrix4x4>();
        }
        readonly List<Batch> _batches = new List<Batch>();
        readonly List<Material> _materials = new List<Material>();
        readonly Plane[] _planes = new Plane[6];

        public void Build(IslandLandform land)
        {
            var trees = new List<Species>(); var pines = new List<Species>(); var rocks = new List<Species>();
            const string folder = "Assets/Synty/PolygonGeneric/Prefabs/Environment/";
            var copies = new Dictionary<Material, Material>();
            void Load(List<Species> into, string stem, int from, int to)
            {
                for (int i = from; i <= to; i++)
                {
                    var prefab = DemoAssetLoad.Load<GameObject>($"{folder}{stem}_{i:00}.prefab");
                    if (prefab == null) continue;
                    var measured = FreewayKit.Measure(prefab);
                    var species = new Species { Base = measured.min.y, Height = measured.size.y };
                    foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>(true))
                    {
                        if (filter.sharedMesh == null || !filter.TryGetComponent<MeshRenderer>(out var renderer)) continue;
                        var mats = renderer.sharedMaterials;
                        for (int sub = 0; sub < filter.sharedMesh.subMeshCount && sub < mats.Length; sub++)
                        {
                            if (mats[sub] == null) continue;
                            if (!copies.TryGetValue(mats[sub], out var material))
                            {
                                material = new Material(mats[sub]) { name = "Island " + mats[sub].name, enableInstancing = true };
                                copies.Add(mats[sub], material); _materials.Add(material);
                            }
                            species.Parts.Add(new Part { Mesh = filter.sharedMesh, Material = material, Sub = sub,
                                Local = prefab.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix });
                        }
                    }
                    if (species.Parts.Count > 0) into.Add(species);
                }
            }
            Load(trees, "SM_Gen_Env_Tree", 1, 3); Load(pines, "SM_Gen_Env_Tree_Pine", 1, 3);
            Load(rocks, "SM_Gen_Env_Rock", 1, 10);
            var random = new System.Random(land.Seed ^ 0x464f5253);
            // Fill instancing batches close to the API limit. Spatial micro-batches
            // turn a few trees into thousands of managed/native draw submissions.
            var groups = new Dictionary<Part, Batch>();
            var bounds = land.Bounds;
            int placed = 0;
            // Uniform random candidates avoid the south-to-north bias of stopping a
            // grid sweep at a budget. Density is bounded even on an unusually wide seed.
            for (int attempt = 0; attempt < 120000 && placed < 22000; attempt++)
            {
                float x = Mathf.Lerp(bounds.xMin, bounds.xMax, (float)random.NextDouble());
                float z = Mathf.Lerp(bounds.yMin, bounds.yMax, (float)random.NextDouble());
                if (land.Coast(x, z) < 12f || land.WaterDistance(x, z) < 22f ||
                    land.DevelopedDistance(x, z) < 22f || land.Roadside(x, z)) continue;
                float h = land.Height(x, z);
                if (h < 0.3f) continue;
                float dx = (land.Height(x + 3f, z) - land.Height(x - 3f, z)) / 6f;
                float dz = (land.Height(x, z + 3f) - land.Height(x, z - 3f)) / 6f;
                float slope = Mathf.Sqrt(dx * dx + dz * dz);
                float patch = IslandNoise.At(x * 0.0028f, z * 0.0028f, land.Seed + 101);
                bool stone = slope > 0.62f || h > 260f || random.NextDouble() < 0.07;
                if (!stone && random.NextDouble() > Mathf.Lerp(0.08f, 0.85f, Mathf.InverseLerp(0.34f, 0.7f, patch))) continue;
                if (stone && random.NextDouble() > 0.3) continue;
                var bag = stone ? rocks : h > 65f || random.NextDouble() < 0.3 ? pines : trees;
                if (bag.Count == 0) continue;
                var species = bag[random.Next(bag.Count)];
                float scale = stone ? Mathf.Lerp(0.8f, 3f, (float)random.NextDouble())
                    : Mathf.Clamp(Mathf.Lerp(11f, 23f, (float)random.NextDouble()) / Mathf.Max(1f, species.Height), 0.75f, 4.5f);
                var at = new Vector3(x, h - species.Base * scale - 0.08f, z);
                var matrix = Matrix4x4.TRS(at, Quaternion.Euler(0, (float)random.NextDouble() * 360f, 0), Vector3.one * scale);
                foreach (var part in species.Parts)
                {
                    if (!groups.TryGetValue(part, out var batch) || batch.Pending.Count >= 1023)
                    {
                        batch = new Batch { Part = part, Bounds = new Bounds(at, Vector3.one * 60f) };
                        groups[part] = batch; _batches.Add(batch);
                    }
                    batch.Pending.Add(matrix * part.Local);
                    batch.Bounds.Encapsulate(new Bounds(at + Vector3.up * 20f, Vector3.one * 65f));
                }
                placed++;
            }
            int submitted = 0;
            foreach (var batch in _batches)
            {
                batch.Matrices = batch.Pending.ToArray(); submitted += batch.Matrices.Length;
                batch.Pending.Clear();
            }
            float mean = _batches.Count > 0 ? submitted / (float)_batches.Count : 0f;
            Debug.Log($"[CoreDemo] island vegetation: {placed} trees/rocks, {submitted} mesh instances " +
                $"in {_batches.Count} GPU batches ({mean:F0} mean instances/batch).");
        }

        void LateUpdate()
        {
            var camera = Camera.main;
            if (camera == null || camera.cullingMask == 0 || !SystemInfo.supportsInstancing) return;
            GeometryUtility.CalculateFrustumPlanes(camera, _planes);
            var at = camera.transform.position;
            foreach (var batch in _batches)
            {
                float distance = batch.Bounds.SqrDistance(at);
                if (distance > 3400f * 3400f || !GeometryUtility.TestPlanesAABB(_planes, batch.Bounds)) continue;
                Graphics.DrawMeshInstanced(batch.Part.Mesh, batch.Part.Sub, batch.Part.Material,
                    batch.Matrices, batch.Matrices.Length, null,
                    distance < 650f * 650f ? ShadowCastingMode.On : ShadowCastingMode.Off, true, 0, camera, LightProbeUsage.Off);
            }
        }
        void OnDestroy()
        {
            foreach (var material in _materials) if (material != null) Destroy(material);
            _materials.Clear(); _batches.Clear();
        }
    }
}
