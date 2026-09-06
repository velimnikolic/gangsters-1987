using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>Measure the posed mesh on boarding, including hair and headwear.
    /// Scratch buffers are shared; no renderer scan or skinning pass during travel.</summary>
    public static class SeatedHeadShape
    {
        static Mesh _scratch;
        static readonly List<SkinnedMeshRenderer> Skins = new List<SkinnedMeshRenderer>();
        static readonly List<MeshFilter> Accessories = new List<MeshFilter>();
        static readonly List<Vector3> Vertices = new List<Vector3>();

        public static void Measure(Transform rider, Transform cabin, Vector3 head,
            ref float crown, ref float radius)
        {
            rider.GetComponentsInChildren(false, Skins);
            if (!_scratch) _scratch = new Mesh { name = "Seated head measurement", hideFlags = HideFlags.HideAndDontSave };
            foreach (var skin in Skins)
            {
                if (!skin.enabled || !skin.sharedMesh) continue;
                skin.BakeMesh(_scratch);
                _scratch.GetVertices(Vertices);
                foreach (var vertex in Vertices) Include(cabin.InverseTransformPoint(skin.transform.TransformPoint(vertex)), head, ref crown, ref radius);
            }
            // Rigid hats/hair can be separate from the skinned body.
            rider.GetComponentsInChildren(false, Accessories);
            foreach (var filter in Accessories)
            {
                if (!filter.sharedMesh) continue;
                var bounds = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var p = bounds.center + Vector3.Scale(bounds.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Include(cabin.InverseTransformPoint(filter.transform.TransformPoint(p)), head, ref crown, ref radius);
                }
            }
            Skins.Clear(); Accessories.Clear(); Vertices.Clear();
        }

        static void Include(Vector3 point, Vector3 head, ref float crown, ref float radius)
        {
            if (point.y < head.y - .08f) return;
            crown = Mathf.Max(crown, point.y - head.y + .035f);
            radius = Mathf.Max(radius, new Vector2(point.x - head.x, point.z - head.z).magnitude + .025f);
        }
    }
}
