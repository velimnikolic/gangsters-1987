using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace LivingCity.Generation
{
    /// <summary>
    /// Turns a list of flat strokes into one mesh laid over the ground.
    ///
    /// The pack ships no road-marking prefab and no decal, but it does ship a folder of plain
    /// URP/Lit materials with no texture bound at all ("- Materials/Colors"). Painted geometry
    /// on those therefore needs no UV authoring and no new asset, and it batches with nothing
    /// else in the scene - which is why every stroke that shares a material goes into a SINGLE
    /// mesh here rather than one quad each. A car park is roughly forty stripes and a block's
    /// paving joints are a hundred more; that many renderers to draw a few hundred triangles
    /// would be the expensive way to do the cheapest thing on screen.
    ///
    /// A URP Decal Projector was the alternative and is worse for this: it needs the decal
    /// renderer feature switched on in the URP asset, and it costs a full-screen pass to draw
    /// paint that never moves.
    ///
    /// Started life as the body of ParkingMarkings, which is now one caller among several -
    /// the difference between a bay line and a footpath is the width and the colour, nothing
    /// more, so the width moved onto the stroke and the material and lift onto the call.
    /// </summary>
    public static class GroundPaint
    {
        /// <summary>
        /// Clear of every ground layer GroundPlacer lays (see its Layers table), and still far
        /// below the 4-unit pavement, so paint never climbs a kerb.
        /// </summary>
        public const float PaintLift = 0.12f;

        public struct Stroke
        {
            public Vector2 A;
            public Vector2 B;
            public float Width;

            public Stroke(Vector2 a, Vector2 b, float width)
            {
                A = a;
                B = b;
                Width = width;
            }
        }

        /// <summary>
        /// Null when there is nothing to draw or no material to draw it with - a PrefabDatabase
        /// whose paint materials were never assigned should cost the caller a bare yard, not an
        /// exception.
        /// </summary>
        public static GameObject Emit(
            List<Stroke> strokes, Material material, float lift, string name, Transform parent)
        {
            if (strokes == null || strokes.Count == 0 || !material)
                return null;

            var vertices = new List<Vector3>(strokes.Count * 4);
            var normals = new List<Vector3>(strokes.Count * 4);
            var uv = new List<Vector2>(strokes.Count * 4);
            var triangles = new List<int>(strokes.Count * 6);

            foreach (var stroke in strokes)
            {
                var direction = stroke.B - stroke.A;
                var length = direction.magnitude;

                // Skipped rather than emitted as a zero-area quad: degenerate corners left at the
                // world origin would drag the mesh bounds all the way there and wreck culling for
                // a block that is nowhere near it. A zero width is skipped for the same reason.
                if (length < 0.001f || stroke.Width < 0.001f)
                    continue;

                direction /= length;
                var side = new Vector2(-direction.y, direction.x) * (stroke.Width * 0.5f);

                var v = vertices.Count;
                vertices.Add(new Vector3(stroke.A.x - side.x, lift, stroke.A.y - side.y));
                vertices.Add(new Vector3(stroke.A.x + side.x, lift, stroke.A.y + side.y));
                vertices.Add(new Vector3(stroke.B.x + side.x, lift, stroke.B.y + side.y));
                vertices.Add(new Vector3(stroke.B.x - side.x, lift, stroke.B.y - side.y));

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
