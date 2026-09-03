using System;
using System.Collections.Generic;
using System.Linq;
using RoadDemo;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// One-time authoring pass for GAN-294. It partitions the authored Synty wall and
    /// glass triangles against the measured doorway prism. Outside wall polygons become
    /// the replacement facade and inside polygons become one or two hinged leaves.
    /// Vertices on a cut plane are interpolated, so a triangle crossing the jamb is split
    /// rather than assigned by centroid.
    /// </summary>
    public static class StorefrontLeafBaker
    {
        public const string OutputDir = "Assets/CityKit/Storefront";
        const string SourceDir = "Assets/Synty/PolygonCity/Prefabs/Buildings/";
        const float CutDepth = 1.5f;
        const float Epsilon = 0.0001f;

        public sealed class BakeReport
        {
            public int Modules;
            public int DoorlessTriangles;
            public int LeafTriangles;
            public string[] Assets = Array.Empty<string>();
            public string[] Failures = Array.Empty<string>();
            public bool Passed => Modules == StorefrontDoorCatalog.Count && Failures.Length == 0;
        }

        public sealed class AuditReport
        {
            public int Profiles;
            public int Assets;
            public string[] Failures = Array.Empty<string>();
            public bool Passed => Failures.Length == 0;
        }

        struct Vertex
        {
            public Vector3 Position;
            public Vector3 Normal;
            public Vector4 Tangent;
            public Vector2 Uv;
            public Color Color;

            public static Vertex Lerp(Vertex a, Vertex b, float t) => new Vertex
            {
                Position = Vector3.LerpUnclamped(a.Position, b.Position, t),
                Normal = Vector3.LerpUnclamped(a.Normal, b.Normal, t).normalized,
                Tangent = Vector4.LerpUnclamped(a.Tangent, b.Tangent, t),
                Uv = Vector2.LerpUnclamped(a.Uv, b.Uv, t),
                Color = Color.LerpUnclamped(a.Color, b.Color, t),
            };
        }

        readonly struct CutPlane
        {
            public CutPlane(Vector3 normal, float distance)
            {
                Normal = normal;
                Distance = distance;
            }

            public readonly Vector3 Normal;
            public readonly float Distance;
            public float Side(Vertex vertex) => Vector3.Dot(Normal, vertex.Position) + Distance;
        }

        sealed class MeshBuilder
        {
            readonly List<Vector3> positions = new List<Vector3>(1024);
            readonly List<Vector3> normals = new List<Vector3>(1024);
            readonly List<Vector4> tangents = new List<Vector4>(1024);
            readonly List<Vector2> uv = new List<Vector2>(1024);
            readonly List<Color> colors = new List<Color>(1024);
            readonly List<int>[] triangles;

            public MeshBuilder(int subMeshes)
            {
                triangles = new List<int>[subMeshes];
                for (int i = 0; i < subMeshes; i++)
                    triangles[i] = new List<int>(1024);
            }

            public int TriangleCount => triangles.Sum(t => t.Count / 3);

            public void Add(IReadOnlyList<Vertex> polygon, int subMesh, Vector3 offset)
            {
                if (polygon == null || polygon.Count < 3) return;
                int first = positions.Count;
                for (int i = 0; i < polygon.Count; i++)
                {
                    var vertex = polygon[i];
                    positions.Add(vertex.Position - offset);
                    normals.Add(vertex.Normal);
                    tangents.Add(vertex.Tangent);
                    uv.Add(vertex.Uv);
                    colors.Add(vertex.Color);
                }
                for (int i = 1; i + 1 < polygon.Count; i++)
                {
                    triangles[subMesh].Add(first);
                    triangles[subMesh].Add(first + i);
                    triangles[subMesh].Add(first + i + 1);
                }
            }

            public Mesh Finish(string name)
            {
                var mesh = new Mesh
                {
                    name = name,
                    indexFormat = positions.Count > ushort.MaxValue
                        ? IndexFormat.UInt32 : IndexFormat.UInt16,
                };
                mesh.SetVertices(positions);
                mesh.SetNormals(normals);
                mesh.SetTangents(tangents);
                mesh.SetUVs(0, uv);
                mesh.SetColors(colors);
                mesh.subMeshCount = triangles.Length;
                for (int i = 0; i < triangles.Length; i++)
                    mesh.SetTriangles(triangles[i], i, false);
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        [MenuItem("Tools/City/Residential/Bake Storefront Leaves", priority = 32)]
        public static void BakeMenu()
        {
            var report = BakeAll();
            Debug.Log($"[StorefrontLeafBaker] {(report.Passed ? "PASS" : "FAIL")}: " +
                      $"{report.Modules} modules, {report.DoorlessTriangles} wall triangles, " +
                      $"{report.LeafTriangles} leaf triangles" +
                      (report.Failures.Length > 0
                          ? "; " + string.Join("; ", report.Failures)
                          : string.Empty));
        }

        public static BakeReport BakeAll()
        {
            EnsureFolders();
            var report = new BakeReport();
            var assets = new List<string>();
            var failures = new List<string>();
            for (int i = 0; i < StorefrontDoorCatalog.Count; i++)
            {
                var profile = StorefrontDoorCatalog.At(i);
                try
                {
                    Bake(profile, report, assets);
                    report.Modules++;
                }
                catch (Exception exception)
                {
                    failures.Add(profile.Module + ": " + exception.Message);
                }
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            report.Assets = assets.ToArray();
            report.Failures = failures.ToArray();
            return report;
        }

        public static AuditReport Audit()
        {
            var failures = new List<string>();
            int assets = 0;
            for (int i = 0; i < StorefrontDoorCatalog.Count; i++)
            {
                var profile = StorefrontDoorCatalog.At(i);
                if (profile.Width < 0f || profile.Height < 0f ||
                    profile.Leaves < 0 || profile.Leaves > 2)
                    failures.Add(profile.Module + ": invalid measured door profile");

                string doorless = Path(profile.Module, "_Doorless");
                var wall = AssetDatabase.LoadAssetAtPath<Mesh>(doorless);
                if (wall == null || wall.vertexCount == 0)
                    failures.Add(profile.Module + ": missing/empty " + doorless);
                else
                    assets++;

                for (int leaf = 0; leaf < profile.Leaves; leaf++)
                {
                    string suffix = leaf == 0 ? "_Leaf_L" : "_Leaf_R";
                    string path = Path(profile.Module, suffix);
                    var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                    if (mesh == null || mesh.vertexCount == 0 || mesh.subMeshCount != 2)
                        failures.Add(profile.Module + ": missing/invalid " + path);
                    else
                        assets++;
                }
            }
            return new AuditReport
            {
                Profiles = StorefrontDoorCatalog.Count,
                Assets = assets,
                Failures = failures.ToArray(),
            };
        }

        static void Bake(StorefrontDoorProfile profile, BakeReport report,
                         List<string> assets)
        {
            string sourcePath = SourceDir + profile.Module + ".prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (prefab == null) throw new InvalidOperationException("missing " + sourcePath);

            var filters = prefab.GetComponentsInChildren<MeshFilter>(true);
            var wall = filters.FirstOrDefault(filter => filter != null &&
                filter.sharedMesh != null &&
                string.Equals(StorefrontDoorCatalog.Normalise(filter.sharedMesh.name),
                    profile.Module, StringComparison.OrdinalIgnoreCase));
            if (wall == null || wall.sharedMesh == null)
                throw new InvalidOperationException("authored wall mesh not found");
            var glass = filters.FirstOrDefault(filter => filter != null &&
                filter.sharedMesh != null &&
                filter.sharedMesh.name.IndexOf("Glass", StringComparison.OrdinalIgnoreCase) >= 0);

            var doorless = new MeshBuilder(1);
            var left = new MeshBuilder(2);
            var rightLeaf = new MeshBuilder(2);
            var planes = DoorPrism(profile);
            Vector3 leftHinge = profile.Centre + profile.Right *
                (profile.Leaves == 1 ? -profile.Width * 0.5f : profile.Width * 0.5f);
            Vector3 rightHinge = profile.Centre - profile.Right * profile.Width * 0.5f;

            if (profile.Leaves == 0)
            {
                AddWholeMesh(wall.sharedMesh, doorless, 0, Vector3.zero);
            }
            else
            {
                PartitionMesh(wall.sharedMesh, planes, profile, doorless,
                    left, rightLeaf, 0, leftHinge, rightHinge);
                if (glass != null && glass.sharedMesh != null)
                    PartitionGlass(glass.sharedMesh, planes, profile,
                        left, rightLeaf, 1, leftHinge, rightHinge);
            }

            string wallPath = Path(profile.Module, "_Doorless");
            Save(doorless.Finish(profile.Module + "_Doorless"), wallPath);
            assets.Add(wallPath);
            report.DoorlessTriangles += doorless.TriangleCount;

            if (profile.Leaves > 0)
            {
                string leftPath = Path(profile.Module, "_Leaf_L");
                Save(left.Finish(profile.Module + "_Leaf_L"), leftPath);
                assets.Add(leftPath);
                report.LeafTriangles += left.TriangleCount;
            }
            if (profile.Leaves > 1)
            {
                string rightPath = Path(profile.Module, "_Leaf_R");
                Save(rightLeaf.Finish(profile.Module + "_Leaf_R"), rightPath);
                assets.Add(rightPath);
                report.LeafTriangles += rightLeaf.TriangleCount;
            }
        }

        static CutPlane[] DoorPrism(StorefrontDoorProfile profile)
        {
            Vector3 centre = profile.Centre;
            Vector3 right = profile.Right;
            Vector3 outward = profile.Outward;
            float half = profile.Width * 0.5f;
            return new[]
            {
                Plane(right, centre + right * -half),
                Plane(-right, centre + right * half),
                Plane(Vector3.up, new Vector3(centre.x, -0.02f, centre.z)),
                Plane(Vector3.down, new Vector3(centre.x, profile.Height, centre.z)),
                Plane(outward, centre - outward * CutDepth),
                Plane(-outward, centre + outward * CutDepth),
            };
        }

        static CutPlane Plane(Vector3 inward, Vector3 point) =>
            new CutPlane(inward.normalized, -Vector3.Dot(inward.normalized, point));

        static void PartitionMesh(Mesh mesh, CutPlane[] planes,
                                  StorefrontDoorProfile profile,
                                  MeshBuilder doorless, MeshBuilder left,
                                  MeshBuilder right, int leafSubMesh,
                                  Vector3 leftHinge, Vector3 rightHinge)
        {
            ForEachTriangle(mesh, triangle =>
            {
                var remaining = new List<Vertex>(triangle);
                for (int plane = 0; plane < planes.Length && remaining.Count >= 3; plane++)
                {
                    var outside = Clip(remaining, planes[plane], keepPositive: false);
                    doorless.Add(outside, 0, Vector3.zero);
                    remaining = Clip(remaining, planes[plane], keepPositive: true);
                }
                AddLeafPolygon(remaining, profile, left, right, leafSubMesh,
                    leftHinge, rightHinge);
            });
        }

        static void PartitionGlass(Mesh mesh, CutPlane[] planes,
                                   StorefrontDoorProfile profile,
                                   MeshBuilder left, MeshBuilder right,
                                   int subMesh, Vector3 leftHinge,
                                   Vector3 rightHinge)
        {
            ForEachTriangle(mesh, triangle =>
            {
                var inside = new List<Vertex>(triangle);
                for (int plane = 0; plane < planes.Length && inside.Count >= 3; plane++)
                    inside = Clip(inside, planes[plane], keepPositive: true);
                AddLeafPolygon(inside, profile, left, right, subMesh,
                    leftHinge, rightHinge);
            });
        }

        static void AddLeafPolygon(List<Vertex> polygon,
                                   StorefrontDoorProfile profile,
                                   MeshBuilder left, MeshBuilder right,
                                   int subMesh, Vector3 leftHinge,
                                   Vector3 rightHinge)
        {
            if (polygon == null || polygon.Count < 3) return;
            if (profile.Leaves == 1)
            {
                left.Add(polygon, subMesh, leftHinge);
                return;
            }
            var split = new CutPlane(profile.Right,
                -Vector3.Dot(profile.Right, profile.Centre));
            left.Add(Clip(polygon, split, keepPositive: true), subMesh, leftHinge);
            right.Add(Clip(polygon, split, keepPositive: false), subMesh, rightHinge);
        }

        static List<Vertex> Clip(IReadOnlyList<Vertex> input, CutPlane plane,
                                 bool keepPositive)
        {
            var output = new List<Vertex>(input?.Count + 2 ?? 0);
            if (input == null || input.Count == 0) return output;
            Vertex previous = input[input.Count - 1];
            float previousSide = plane.Side(previous) * (keepPositive ? 1f : -1f);
            bool previousInside = previousSide >= -Epsilon;
            for (int i = 0; i < input.Count; i++)
            {
                Vertex current = input[i];
                float currentSide = plane.Side(current) * (keepPositive ? 1f : -1f);
                bool currentInside = currentSide >= -Epsilon;
                if (currentInside != previousInside)
                {
                    float t = previousSide / (previousSide - currentSide);
                    output.Add(Vertex.Lerp(previous, current, Mathf.Clamp01(t)));
                }
                if (currentInside) output.Add(current);
                previous = current;
                previousSide = currentSide;
                previousInside = currentInside;
            }
            return output;
        }

        static void AddWholeMesh(Mesh mesh, MeshBuilder builder, int subMesh,
                                 Vector3 offset)
        {
            ForEachTriangle(mesh, triangle => builder.Add(triangle, subMesh, offset));
        }

        static void ForEachTriangle(Mesh mesh, Action<Vertex[]> visit)
        {
            if (mesh == null || !mesh.isReadable)
                throw new InvalidOperationException((mesh != null ? mesh.name : "mesh") +
                    " is not CPU-readable");
            var positions = mesh.vertices;
            var normals = mesh.normals;
            var tangents = mesh.tangents;
            var uv = mesh.uv;
            var colors = mesh.colors;
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                var indices = mesh.GetTriangles(sub);
                for (int i = 0; i + 2 < indices.Length; i += 3)
                    visit(new[]
                    {
                        Read(indices[i], positions, normals, tangents, uv, colors),
                        Read(indices[i + 1], positions, normals, tangents, uv, colors),
                        Read(indices[i + 2], positions, normals, tangents, uv, colors),
                    });
            }
        }

        static Vertex Read(int index, Vector3[] positions, Vector3[] normals,
                           Vector4[] tangents, Vector2[] uv, Color[] colors) => new Vertex
        {
            Position = positions[index],
            Normal = normals.Length == positions.Length ? normals[index] : Vector3.up,
            Tangent = tangents.Length == positions.Length ? tangents[index] :
                new Vector4(1f, 0f, 0f, 1f),
            Uv = uv.Length == positions.Length ? uv[index] : Vector2.zero,
            Color = colors.Length == positions.Length ? colors[index] : Color.white,
        };

        static void Save(Mesh generated, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                existing.Clear();
                EditorUtility.CopySerialized(generated, existing);
                existing.name = generated.name;
                UnityEngine.Object.DestroyImmediate(generated);
                EditorUtility.SetDirty(existing);
            }
            else
            {
                AssetDatabase.CreateAsset(generated, path);
            }
        }

        static string Path(string module, string suffix) =>
            OutputDir + "/" + module + suffix + ".asset";

        static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder("Assets/CityKit"))
                AssetDatabase.CreateFolder("Assets", "CityKit");
            if (!AssetDatabase.IsValidFolder(OutputDir))
                AssetDatabase.CreateFolder("Assets/CityKit", "Storefront");
        }
    }
}
