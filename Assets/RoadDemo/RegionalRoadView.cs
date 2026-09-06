using UnityEngine;

namespace RoadDemo
{
    /// <summary>Continuous asphalt and kerbs share the collector's driving curve.</summary>
    public static class RegionalRoadView
    {
        public static void Bend(RoadLine line, IDistrictHost host, GameObject asphalt)
        {
            var root = host.StaticRoot("Rounded collector");
            var owned = root.gameObject.AddComponent<LandscapeResources>();
            var road = DeckMesh.Flat(asphalt);
            var pavement = owned.Material(new Material(Shader.Find("Universal Render Pipeline/Lit"))
                { name = "Collector paving", color = new Color(0.58f, 0.56f, 0.51f) });
            Ribbon(line, -StreetKit.StreetHalf, StreetKit.StreetHalf, 0f, road.Mat, road.Concrete, root, owned);
            foreach (float side in new[] { -1f, 1f })
                Ribbon(line, side < 0 ? -StreetKit.OuterHalf : StreetKit.StreetHalf,
                    side < 0 ? -StreetKit.StreetHalf : StreetKit.OuterHalf, 0.13f,
                    pavement, Vector2.zero, root, owned);
        }

        public static void Ribbon(RoadLine line, float lo, float hi, float height, Material material,
            Vector2 uv, Transform root, LandscapeResources owned, float depth = 0.2f)
        {
            int n = Mathf.Max(2, Mathf.CeilToInt(line.Length / 2f));
            var vertices = new Vector3[(n + 1) * 4]; var uvs = new Vector2[vertices.Length];
            var triangles = new int[n * 18];
            for (int i = 0; i <= n; i++)
            {
                float s = line.Length * i / n;
                var a = line.Pose(s, lo); var b = line.Pose(s, hi);
                a.y = b.y = height;
                vertices[i * 4] = a; vertices[i * 4 + 1] = b;
                a.y -= depth; b.y -= depth;
                vertices[i * 4 + 2] = a; vertices[i * 4 + 3] = b;
                for (int k = 0; k < 4; k++) uvs[i * 4 + k] = uv;
                if (i == n) continue;
                int v = i * 4, t = i * 18;
                int[] face = { 0,4,1, 1,4,5, 0,2,4, 2,6,4, 1,5,3, 3,5,7 };
                for (int k = 0; k < face.Length; k++) triangles[t + k] = v + face[k];
            }
            var mesh = new Mesh { name = height == 0f ? "Collector asphalt" : "Continuous collector pavement" };
            mesh.vertices = vertices; mesh.uv = uvs; mesh.triangles = triangles; mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var go = new GameObject(mesh.name); go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>().sharedMaterial = material;
            go.isStatic = true; owned.Mesh(go);
        }
    }
}
