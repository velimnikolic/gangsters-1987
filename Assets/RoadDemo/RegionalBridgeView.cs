using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>Paired steel arch trusses over the clear river channel; scenery only.</summary>
    public static class RegionalBridgeView
    {
        public static void Build(RegionalExpresswayPlan.Deck deck, Transform parent, LandscapeResources owned, Material steel,
            System.Func<float, float, float> ground)
        {
            var concrete = owned.Material(new Material(Shader.Find("Universal Render Pipeline/Lit"))
                { name = "Bridge pier concrete", color = new Color(0.52f, 0.51f, 0.46f) });
            foreach (float centre in deck.Bridges)
            {
                var vertices = new List<Vector3>(); var triangles = new List<int>();
                float half = deck.ChannelHalf + 95f;
                const int bays = 16;
                // Explicit end supports carry every river span. They stand on the banks,
                // outside the navigable channel, regardless of generic viaduct-pier spacing.
                foreach (float side in new[] { -1f, 1f })
                {
                    float station = Mathf.Repeat(centre + side * half, deck.Line.Length);
                    var at = deck.Line.PointAt(station);
                    float floor = ground(at.x, at.z), top = deck.Height(station) - 1.55f;
                    var pier = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    pier.name = "River bridge bank pier"; pier.transform.SetParent(parent, false);
                    pier.transform.localPosition = new Vector3(at.x, (floor + top) * 0.5f, at.z);
                    pier.transform.localRotation = Quaternion.LookRotation(deck.Line.DirAt(station));
                    pier.transform.localScale = new Vector3(ExpresswayLayout.DeckHalf * 2f, top - floor + 0.4f, 4f);
                    pier.GetComponent<MeshRenderer>().sharedMaterial = concrete;
                    Object.Destroy(pier.GetComponent<Collider>()); pier.isStatic = true;
                }
                Vector3 Point(int i, float side, bool arch)
                {
                    float t = i / (float)bays, s = Mathf.Repeat(centre + Mathf.Lerp(-half, half, t), deck.Line.Length);
                    var p = deck.Line.Pose(s, side * (ExpresswayLayout.DeckHalf + 0.25f));
                    p.y = deck.Height(s) + 0.8f + (arch ? 11f * Mathf.Sin(t * Mathf.PI) : 0f);
                    return p;
                }
                for (int i = 0; i < bays; i++)
                    foreach (float side in new[] { -1f, 1f })
                    {
                        Beam(Point(i, side, false), Point(i + 1, side, false), 0.45f, vertices, triangles);
                        Beam(Point(i, side, true), Point(i + 1, side, true), 0.65f, vertices, triangles);
                        Beam(Point(i, side, false), Point(i + 1, side, true), 0.24f, vertices, triangles);
                        Beam(Point(i + 1, side, false), Point(i + 1, side, true), 0.22f, vertices, triangles);
                    }
                for (int i = 3; i <= bays - 3; i += 2)
                    Beam(Point(i, -1, true), Point(i, 1, true), 0.38f, vertices, triangles);
                var mesh = new Mesh { name = "River bridge steel arch" };
                mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
                var go = new GameObject(mesh.name); go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = steel;
                go.isStatic = true; owned.Mesh(go);
            }
        }

        static void Beam(Vector3 a, Vector3 b, float size, List<Vector3> vertices, List<int> triangles)
        {
            if ((b - a).sqrMagnitude < 0.01f) return;
            var forward = (b - a).normalized;
            var right = Vector3.Cross(forward, Mathf.Abs(forward.y) > 0.95f ? Vector3.forward : Vector3.up).normalized * size * 0.5f;
            var up = Vector3.Cross(right.normalized, forward) * size * 0.5f;
            int n = vertices.Count;
            vertices.Add(a - right - up); vertices.Add(a + right - up);
            vertices.Add(a + right + up); vertices.Add(a - right + up);
            vertices.Add(b - right - up); vertices.Add(b + right - up);
            vertices.Add(b + right + up); vertices.Add(b - right + up);
            foreach (int k in new[] { 0,2,1,0,3,2, 4,5,6,4,6,7, 0,1,5,0,5,4,
                1,2,6,1,6,5, 2,3,7,2,7,6, 3,0,4,3,4,7 }) triangles.Add(n + k);
        }
    }
}
