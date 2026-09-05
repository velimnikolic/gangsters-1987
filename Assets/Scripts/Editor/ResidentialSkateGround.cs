using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace LivingCity.EditorTools
{
    /// <summary>Replaces the source terrain beneath the harvested skatepark, preserving
    /// its below-grade bowls. Baked once; streamed instances only load the resulting mesh.</summary>
    public static class ResidentialSkateGround
    {
        const string Name = "Skatepark ground";
        const string MeshPath = "Assets/Prefabs/Residential/SkateparkGround.asset";
        const float Pitch = 0.25f;
        const float Sink = 0.06f;

        public static void BakeInto(GameObject root, float width, float depth)
        {
            var old = root.transform.Find(Name);
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            int nx = Mathf.CeilToInt(width / Pitch), nz = Mathf.CeilToInt(depth / Pitch);
            int stride = nx + 1;
            var heights = Enumerable.Repeat(float.NegativeInfinity, stride * (nz + 1)).ToArray();
            Material material = null;
            Vector2 uv = Vector2.zero;
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>())
            {
                var mesh = filter.sharedMesh;
                if (mesh == null) continue;
                string name = mesh.name;
                bool floor = name.StartsWith("SM_Bld_Base_Floor", StringComparison.Ordinal);
                if (!floor && (!name.StartsWith("SM_Env_Ramp_", StringComparison.Ordinal) ||
                    name.Contains("Rail") || name.Contains("Border"))) continue;
                var vertices = mesh.vertices;
                var indices = mesh.triangles;
                var matrix = root.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                for (int i = 0; i < vertices.Length; i++) vertices[i] = matrix.MultiplyPoint3x4(vertices[i]);
                for (int t = 0; t < indices.Length; t += 3)
                {
                    var a = vertices[indices[t]];
                    var b = vertices[indices[t + 1]];
                    var c = vertices[indices[t + 2]];
                    if (Vector3.Cross(b - a, c - a).y <= 0.00001f) continue;
                    if (floor && material == null)
                    {
                        material = filter.GetComponent<MeshRenderer>().sharedMaterial;
                        var uvs = mesh.uv;
                        if (uvs.Length == vertices.Length)
                            uv = (uvs[indices[t]] + uvs[indices[t + 1]] + uvs[indices[t + 2]]) / 3f;
                    }
                    float denominator = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                    int x0 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x)) / Pitch), 0, nx);
                    int x1 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x)) / Pitch), 0, nx);
                    int z0 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Min(a.z, Mathf.Min(b.z, c.z)) / Pitch), 0, nz);
                    int z1 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Max(a.z, Mathf.Max(b.z, c.z)) / Pitch), 0, nz);
                    for (int z = z0; z <= z1; z++)
                        for (int x = x0; x <= x1; x++)
                        {
                            float wa = ((b.z - c.z) * (x * Pitch - c.x) + (c.x - b.x) * (z * Pitch - c.z)) / denominator;
                            float wb = ((c.z - a.z) * (x * Pitch - c.x) + (a.x - c.x) * (z * Pitch - c.z)) / denominator;
                            float wc = 1f - wa - wb;
                            if (wa < -0.0001f || wb < -0.0001f || wc < -0.0001f) continue;
                            int index = z * stride + x;
                            heights[index] = Mathf.Max(heights[index], wa * a.y + wb * b.y + wc * c.y);
                        }
                }
            }
            if (material == null) throw new InvalidOperationException("Skatepark has no readable floor material.");
            var points = new Vector3[heights.Length];
            var texcoords = new Vector2[heights.Length];
            for (int z = 0; z <= nz; z++)
                for (int x = 0; x <= nx; x++)
                {
                    int index = z * stride + x;
                    points[index] = new Vector3(x * Pitch, -Sink, z * Pitch);
                    texcoords[index] = uv;
                }
            // The model already owns every sunken surface. Only fill its outside apron
            // and grade-level gaps; interpolating a second surface through the bowls
            // would cut across their curved walls.
            bool AtGrade(int i) => float.IsNegativeInfinity(heights[i]) || heights[i] >= -Sink;
            var triangles = new System.Collections.Generic.List<int>(nx * nz * 6);
            for (int z = 0; z < nz; z++)
                for (int x = 0; x < nx; x++)
                {
                    int i = z * stride + x;
                    if (AtGrade(i) && AtGrade(i + stride) && AtGrade(i + 1))
                    { triangles.Add(i); triangles.Add(i + stride); triangles.Add(i + 1); }
                    if (AtGrade(i + 1) && AtGrade(i + stride) && AtGrade(i + stride + 1))
                    { triangles.Add(i + 1); triangles.Add(i + stride); triangles.Add(i + stride + 1); }
                }
            var ground = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            if (ground == null) { ground = new Mesh(); AssetDatabase.CreateAsset(ground, MeshPath); }
            ground.Clear();
            ground.name = Name;
            ground.vertices = points;
            ground.uv = texcoords;
            ground.SetTriangles(triangles, 0);
            // This is a backing surface. The authored ramps supply the visible slopes
            // and shadows; their sampled edge must not shade as a second jagged rim.
            ground.normals = Enumerable.Repeat(Vector3.up, points.Length).ToArray();
            ground.RecalculateBounds();
            EditorUtility.SetDirty(ground);
            AssetDatabase.SaveAssetIfDirty(ground);
            var go = new GameObject(Name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(root.transform, false);
            go.GetComponent<MeshFilter>().sharedMesh = ground;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }
}
