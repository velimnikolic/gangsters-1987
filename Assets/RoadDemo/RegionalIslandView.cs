using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RoadDemo
{
    /// <summary>Continuous terrain and ocean views. The landform owns all sampled heights.</summary>
    public static class RegionalIslandView
    {
        const float Tile = 480f, Step = 20f;

        /// <summary>Height of an unpaved terrain triangle, from BuildTile's grid.
        /// Paved cells retain the landform's level here; their rendered basement bed
        /// is deliberately below the streets the camera must clear. The continuous
        /// landform can lie below the mesh on sharp road shoulders.</summary>
        public static float SurfaceHeight(IslandLandform land, float x, float z)
        {
            var area = land.Bounds;
            if (x < area.xMin || x >= area.xMax || z < area.yMin || z >= area.yMax)
                return 0f;
            float tileX = area.xMin + Mathf.Floor((x - area.xMin) / Tile) * Tile;
            float tileZ = area.yMin + Mathf.Floor((z - area.yMin) / Tile) * Tile;
            float x0 = tileX + Mathf.Floor((x - tileX) / Step) * Step;
            float z0 = tileZ + Mathf.Floor((z - tileZ) / Step) * Step;
            float x1 = Mathf.Min(x0 + Step, area.xMax);
            float z1 = Mathf.Min(z0 + Step, area.yMax);
            float u = (x - x0) / (x1 - x0), v = (z - z0) / (z1 - z0);
            float b = land.Height(x1, z0), c = land.Height(x0, z1);
            return u + v <= 1f
                ? land.Height(x0, z0) * (1f - u - v) + b * u + c * v
                : b * (1f - v) + c * (1f - u) + land.Height(x1, z1) * (u + v - 1f);
        }

        public static void Build(IslandLandform land, DistrictReservations reservations, Transform parent)
        {
            var owned = parent.gameObject.AddComponent<LandscapeResources>();
            var terrainShader = DemoAssetLoad.Load<Shader>("Assets/Shaders/IslandTerrain.shader") ?? Shader.Find("Universal Render Pipeline/Lit");
            var waterShader = DemoAssetLoad.Load<Shader>("Assets/Shaders/IslandOcean.shader") ?? Shader.Find("Universal Render Pipeline/Lit");
            var terrain = owned.Material(new Material(terrainShader) { name = "Island meadow, forest floor and granite" });
            var sea = owned.Material(new Material(waterShader) { name = "Open ocean and sheltered harbour" });
            var area = land.Bounds;
            int count = 0, vertices = 0;
            var terrainClock = System.Diagnostics.Stopwatch.StartNew();
            for (float z = area.yMin; z < area.yMax; z += Tile)
                for (float x = area.xMin; x < area.xMax; x += Tile)
                    vertices += BuildTile(land, reservations, parent, owned, terrain,
                        Rect.MinMaxRect(x, z, Mathf.Min(x + Tile, area.xMax), Mathf.Min(z + Tile, area.yMax)), count++);
            terrainClock.Stop();
            Debug.Log($"[CoreDemo] terrain mesh: {vertices:N0} vertices in {count} tiles, " +
                $"built in {terrainClock.ElapsedMilliseconds} ms.");

            // Include the complete shipping reservations, not just the land's bounds.
            // Water has its own renderer and never goes through the static terrain merge.
            var ocean = area;
            foreach (var water in reservations.Water)
            {
                ocean.xMin = Mathf.Min(ocean.xMin, water.xMin - 500f); ocean.xMax = Mathf.Max(ocean.xMax, water.xMax + 500f);
                ocean.yMin = Mathf.Min(ocean.yMin, water.yMin - 500f); ocean.yMax = Mathf.Max(ocean.yMax, water.yMax + 500f);
            }
            var waterRoot = new GameObject("Island Ocean").transform;
            waterRoot.SetParent(parent, false);
            Ocean(land, ocean, waterRoot, owned, sea);
            RegionalQuayView.Build(land, parent, owned);
            parent.gameObject.AddComponent<IslandForest>().Build(land);
            Debug.Log($"[CoreDemo] island: {area.width:F0} x {area.height:F0} m, {count} continuous terrain tiles; ocean covers shipping lanes.");
        }

        static int BuildTile(IslandLandform land, DistrictReservations reservations, Transform parent,
            LandscapeResources owned, Material material, Rect box, int index)
        {
            int nx = Mathf.CeilToInt(box.width / Step), nz = Mathf.CeilToInt(box.height / Step);
            var vertices = new Vector3[(nx + 1) * (nz + 1)];
            var normals = new Vector3[vertices.Length];
            var colors = new Color[vertices.Length];
            var dry = new List<Vector3>();
            var triangles = new List<int>();
            // A one-vertex halo shares each expensive landform sample between
            // position, normal and biome instead of evaluating Height five times.
            int stride = nx + 3;
            var heights = new float[(nx + 3) * (nz + 3)];
            float LocalX(int i) => i < 0 ? i * Step : i > nx
                ? box.width + (i - nx) * Step : Mathf.Min(i * Step, box.width);
            float LocalZ(int j) => j < 0 ? j * Step : j > nz
                ? box.height + (j - nz) * Step : Mathf.Min(j * Step, box.height);
            for (int j = -1; j <= nz + 1; j++) for (int i = -1; i <= nx + 1; i++)
                heights[(j + 1) * stride + i + 1] = land.Height(box.xMin + LocalX(i), box.yMin + LocalZ(j));
            for (int j = 0; j <= nz; j++) for (int i = 0; i <= nx; i++)
            {
                float lx = Mathf.Min(i * Step, box.width), lz = Mathf.Min(j * Step, box.height);
                float x = box.xMin + lx, z = box.yMin + lz;
                float height = heights[(j + 1) * stride + i + 1];
                int k = j * (nx + 1) + i;
                vertices[k] = new Vector3(lx, height, lz);
                float dx = (heights[(j + 1) * stride + i + 2] - heights[(j + 1) * stride + i]) /
                    Mathf.Max(1f, LocalX(i + 1) - LocalX(i - 1));
                float dz = (heights[(j + 2) * stride + i + 1] - heights[j * stride + i + 1]) /
                    Mathf.Max(1f, LocalZ(j + 1) - LocalZ(j - 1));
                normals[k] = new Vector3(-dx, 1f, -dz).normalized;
                colors[k] = Biome(land, x, z, height, Mathf.Sqrt(dx * dx + dz * dz));
            }
            for (int j = 0; j < nz; j++) for (int i = 0; i < nx; i++)
            {
                int a = j * (nx + 1) + i, b = a + 1, c = a + nx + 1, d = c + 1;
                float x = box.xMin + (i + 0.5f) * Step, z = box.yMin + (j + 0.5f) * Step;
                if (reservations.InPaved(x, z, Step * 0.5f) && land.WaterDistance(x, z) > 0f)
                {
                    // Keep the same dry subgrade beneath authored basements/subways.
                    // It is an independent quad: pulling shared vertices down would
                    // cut a trench along the surrounding public pavement.
                    int start = vertices.Length + dry.Count;
                    dry.Add(new Vector3(i * Step, RoadDemoBuilder.DryUrbanBedY, j * Step));
                    dry.Add(new Vector3(i * Step, RoadDemoBuilder.DryUrbanBedY, Mathf.Min((j + 1) * Step, box.height)));
                    dry.Add(new Vector3(Mathf.Min((i + 1) * Step, box.width), RoadDemoBuilder.DryUrbanBedY, j * Step));
                    dry.Add(new Vector3(Mathf.Min((i + 1) * Step, box.width), RoadDemoBuilder.DryUrbanBedY, Mathf.Min((j + 1) * Step, box.height)));
                    triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
                    triangles.Add(start + 2); triangles.Add(start + 1); triangles.Add(start + 3);
                    continue;
                }
                triangles.Add(a); triangles.Add(c); triangles.Add(b);
                triangles.Add(b); triangles.Add(c); triangles.Add(d);
            }
            if (triangles.Count == 0) return 0;
            var mesh = new Mesh { name = "Island terrain " + index, indexFormat = IndexFormat.UInt32 };
            int oldCount = vertices.Length;
            System.Array.Resize(ref vertices, oldCount + dry.Count);
            System.Array.Resize(ref normals, vertices.Length); System.Array.Resize(ref colors, vertices.Length);
            dry.CopyTo(vertices, oldCount);
            for (int i = oldCount; i < vertices.Length; i++) { normals[i] = Vector3.up; colors[i] = new Color(0.16f, 0.17f, 0.17f); }
            mesh.vertices = vertices; mesh.normals = normals; mesh.colors = colors;
            mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds();
            var go = new GameObject(mesh.name);
            go.transform.SetParent(parent, false); go.transform.localPosition = new Vector3(box.xMin, 0, box.yMin);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            go.isStatic = true; owned.Mesh(go);
            return vertices.Length;
        }

        static Color Biome(IslandLandform land, float x, float z, float height, float slope)
        {
            float noise = IslandNoise.At(x * 0.009f, z * 0.009f, land.Seed + 73);
            var rock = Color.Lerp(new Color(0.30f, 0.31f, 0.28f), new Color(0.54f, 0.53f, 0.46f), noise);
            float exposed = Mathf.Max(Mathf.InverseLerp(0.32f, 0.8f, slope), Mathf.InverseLerp(235f, 335f, height));
            float shore = Mathf.Min(land.Coast(x, z), land.WaterDistance(x, z));
            float sand = 1f - Mathf.InverseLerp(4f, 60f, shore);
            float blend = sand + exposed * (1f - sand);
            var color = (rock * exposed * (1f - sand) + new Color(0.65f, 0.60f, 0.43f) * sand) / Mathf.Max(0.001f, blend);
            color.a = blend;
            return color;
        }

        static void Ocean(IslandLandform land, Rect area, Transform parent, LandscapeResources owned, Material material)
        {
            const float size = 960f, step = 60f;
            for (float z = area.yMin - 500f; z < area.yMax + 500f; z += size)
                for (float x = area.xMin - 500f; x < area.xMax + 500f; x += size)
                {
                    const int n = 16;
                    var vertices = new Vector3[(n + 1) * (n + 1)];
                    var colors = new Color[vertices.Length]; var triangles = new int[n * n * 6];
                    for (int j = 0; j <= n; j++) for (int i = 0; i <= n; i++)
                    {
                        int k = j * (n + 1) + i;
                        float wx = x + i * step, wz = z + j * step;
                        vertices[k] = new Vector3(i * step, RoadDemoBuilder.WaterY, j * step);
                        float depth = RoadDemoBuilder.WaterY - land.Height(wx, wz);
                        colors[k] = new Color(Mathf.Clamp01(depth / 18f), Mathf.Clamp01(depth / 2f),
                            land.WaterDistance(wx, wz) <= 0f ? 1f : 0f, 1f);
                    }
                    int t = 0;
                    for (int j = 0; j < n; j++) for (int i = 0; i < n; i++)
                    {
                        int a = j * (n + 1) + i;
                        triangles[t++] = a; triangles[t++] = a + n + 1; triangles[t++] = a + 1;
                        triangles[t++] = a + 1; triangles[t++] = a + n + 1; triangles[t++] = a + n + 2;
                    }
                    var mesh = new Mesh { name = "Ocean patch" };
                    mesh.vertices = vertices; mesh.colors = colors; mesh.triangles = triangles;
                    mesh.RecalculateNormals(); mesh.RecalculateBounds();
                    var go = new GameObject("Ocean water"); go.transform.SetParent(parent, false);
                    go.transform.localPosition = new Vector3(x, 0, z);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
                    renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = true;
                    owned.Mesh(go);
                }
        }
    }
}
