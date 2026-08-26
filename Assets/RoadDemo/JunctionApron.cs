using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    // What a road junction is actually made of.
    //
    // Laid as a RECTANGLE of the kit's 5 m cells, a junction is what every crossroads
    // in this city is, and where two streets of the same width cross, nobody looks
    // twice. A ramp terminal is not that. A ten-metre arterial is met at right angles
    // by a ramp that arrives one lane wide, and the box drawn round the pair of them
    // came out eighteen metres by twenty-two: four metres of bare asphalt standing in
    // front of the ramp's mouth, seven more beside it, and a square corner on all four
    // corners. From the air it read as a car park with a road through it - and the
    // width of the road changed AT THE KERB LINE rather than on the road, which is the
    // one thing no junction anywhere does.
    //
    // So a terminal is drawn instead: the two roads' own widths, and a kerb return of
    // a lorry's radius in each of the four corners between them. That is the shape a
    // junction has, and the reason anything longer than a car can turn into one.
    public static class JunctionApron
    {
        /// <summary>The junction where a road running <paramref name="along"/>, half
        /// <paramref name="armHalf"/> wide, is crossed at right angles by one whose own
        /// section runs from <paramref name="sideLo"/> to <paramref name="sideHi"/>
        /// across it - with a kerb return of <paramref name="radius"/> in each of the
        /// four corners. Flat: both roads meet it at the height of the city's own
        /// asphalt, and the piece is laid a centimetre under them so that where they
        /// overlap it is theirs that is seen.</summary>
        public static GameObject Build(Vector3 centre, Vector3 along, float armHalf,
                                       float sideLo, float sideHi, float radius, float y,
                                       DeckMesh.Skin skin, Transform parent, string name)
        {
            along.y = 0f;
            if (!skin.Real || along.sqrMagnitude < 1e-6f) return null;
            along.Normalize();
            var across = new Vector3(along.z, 0f, -along.x);

            float a = Mathf.Max(1f, armHalf);
            float lo = Mathf.Min(sideLo, -1f), hi = Mathf.Max(sideHi, 1f);
            float r = Mathf.Max(1f, radius);
            float endAlong = r + Mathf.Max(-lo, hi);   // how far the arm runs each way
            float endAcross = a + r;                   // and the crossing road's, likewise

            // the outline, anticlockwise: down one arm, round a kerb return, out along
            // the crossing road, and so on round all four
            var edge = new List<Vector2>();
            void Put(Vector2 p)
            {
                if (edge.Count > 0 && (edge[edge.Count - 1] - p).sqrMagnitude < 1e-4f) return;
                edge.Add(p);
            }
            void Return(float cu, float cv, float from, float to)
            {
                const int steps = 8;
                for (int i = 0; i <= steps; i++)
                {
                    float ang = Mathf.Lerp(from, to, i / (float)steps) * Mathf.Deg2Rad;
                    Put(new Vector2(cu + Mathf.Cos(ang) * r, cv + Mathf.Sin(ang) * r));
                }
            }
            Put(new Vector2(endAlong, -a));
            Put(new Vector2(endAlong, a));
            Return(hi + r, a + r, -90f, -180f);
            Put(new Vector2(lo, endAcross));
            Return(lo - r, a + r, 0f, -90f);
            Put(new Vector2(-endAlong, a));
            Put(new Vector2(-endAlong, -a));
            Return(lo - r, -a - r, 90f, 0f);
            Put(new Vector2(hi, -endAcross));
            Return(hi + r, -a - r, 180f, 90f);
            if (edge.Count > 1 && (edge[0] - edge[edge.Count - 1]).sqrMagnitude < 1e-4f)
                edge.RemoveAt(edge.Count - 1);
            if (edge.Count < 3) return null;

            // a fan from the middle: a rounded cross is seen whole from its own centre,
            // so there is nothing here that wants triangulating properly
            var verts = new List<Vector3>(edge.Count + 1) { Vector3.zero };
            var uvs = new List<Vector2>(edge.Count + 1) { skin.Concrete };
            foreach (var p in edge)
            {
                verts.Add(along * p.x + across * p.y);
                uvs.Add(skin.Concrete);
            }
            var tris = new List<int>(edge.Count * 3);
            for (int i = 0; i < edge.Count; i++)
            {
                tris.Add(0);
                tris.Add(i + 1);
                tris.Add(i + 1 == edge.Count ? 1 : i + 2);
            }

            var mesh = new Mesh { name = name + " mesh" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(centre.x, y, centre.z);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = skin.Mat;
            return go;
        }
    }
}
