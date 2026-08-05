using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace LivingCity.Generation
{
    /// <summary>
    /// Turns a block's parking lines into one flat mesh laid over the asphalt.
    ///
    /// The pack ships no road-marking prefab and no decal, but it does ship
    /// "- Materials/Colors/58 WHITE-LPEC.mat": a plain white URP/Lit with no texture bound at
    /// all. Painted geometry on that material therefore needs no UV authoring and no new asset,
    /// and it batches with nothing else in the scene - which is why every line in a block goes
    /// into a SINGLE mesh here rather than one quad per stripe. A car park is roughly forty
    /// stripes; forty renderers to draw a few hundred triangles would be the expensive way to do
    /// the cheapest thing on screen.
    ///
    /// A URP Decal Projector was the alternative and is worse for this: it needs the decal
    /// renderer feature switched on in the URP asset, and it costs a full-screen pass to draw
    /// paint that never moves.
    /// </summary>
    public static class ParkingMarkings
    {
        /// <summary>Real bay lines are 10cm. 15 reads better at the default camera distance.</summary>
        public const float LineWidth = 0.15f;

        /// <summary>
        /// Clear of both ground layers: GroundPlacer lays its slab at 0.02 and a courtyard at
        /// 0.04. Still far below the 4-unit pavement, so the paint never climbs a kerb.
        /// </summary>
        public const float LineLift = 0.06f;

        /// <summary>
        /// Null when there is nothing to draw or no material to draw it with - a PrefabDatabase
        /// whose lineMaterial was never assigned should cost the caller a bare lot, not an
        /// exception.
        /// </summary>
        public static GameObject Emit(
            List<ParkingLayout.Line> lines, Material material, string name, Transform parent)
        {
            if (lines == null || lines.Count == 0 || !material)
                return null;

            var vertices = new List<Vector3>(lines.Count * 4);
            var normals = new List<Vector3>(lines.Count * 4);
            var uv = new List<Vector2>(lines.Count * 4);
            var triangles = new List<int>(lines.Count * 6);

            var half = LineWidth * 0.5f;

            foreach (var line in lines)
            {
                var direction = line.B - line.A;
                var length = direction.magnitude;

                // Skipped rather than emitted as a zero-area quad: degenerate corners left at the
                // world origin would drag the mesh bounds all the way there and wreck culling for
                // a block that is nowhere near it.
                if (length < 0.001f)
                    continue;

                direction /= length;
                var side = new Vector2(-direction.y, direction.x) * half;

                var v = vertices.Count;
                vertices.Add(new Vector3(line.A.x - side.x, LineLift, line.A.y - side.y));
                vertices.Add(new Vector3(line.A.x + side.x, LineLift, line.A.y + side.y));
                vertices.Add(new Vector3(line.B.x + side.x, LineLift, line.B.y + side.y));
                vertices.Add(new Vector3(line.B.x - side.x, LineLift, line.B.y - side.y));

                for (var k = 0; k < 4; k++)
                    normals.Add(Vector3.up);

                // Trivial, but present: MarkStaticForBatching sets ContributeGI, and a mesh with
                // no UVs makes the lightmapper complain the moment anyone bakes.
                uv.Add(new Vector2(0f, 0f));
                uv.Add(new Vector2(1f, 0f));
                uv.Add(new Vector2(1f, length));
                uv.Add(new Vector2(0f, length));

                triangles.Add(v + 0);
                triangles.Add(v + 1);
                triangles.Add(v + 2);
                triangles.Add(v + 0);
                triangles.Add(v + 2);
                triangles.Add(v + 3);
            }

            if (vertices.Count == 0)
                return null;

            var mesh = new Mesh { name = name };
            if (vertices.Count > 65000)
                mesh.indexFormat = IndexFormat.UInt32;

            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();

            var instance = new GameObject(name);
            instance.transform.SetParent(parent, false);

            // The vertices are already in world space, like every position the placers hand to
            // SpawnPrefab, so the object itself has to sit at the origin - inheriting a parent's
            // offset would double it.
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            instance.AddComponent<MeshFilter>().sharedMesh = mesh;

            var renderer = instance.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;

            // Paint lying flat on the ground has no silhouette to cast, and asking for one costs
            // a shadow-map draw for nothing. It still RECEIVES, so lines darken under a building.
            renderer.shadowCastingMode = ShadowCastingMode.Off;

            return instance;
        }
    }
}
