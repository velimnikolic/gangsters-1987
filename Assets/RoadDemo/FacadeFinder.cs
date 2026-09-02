using UnityEngine;

namespace RoadDemo
{
    // Which way a baked building faces. The catalogue's convention is "facade on
    // +Z", but a hand-baked shop does not always keep it (the blue coffee shop
    // stood with its back to the street), so a scene that sets a building down at
    // a kerb asks the mesh instead of trusting the file: a storefront is the busy
    // side - door, windows, awning, signage - so of the four sides of the footprint
    // the one with the most vertices in its outer quarter is the front. Flat back
    // and party walls carry almost none. A named override wins over the measure for
    // the day the heuristic is wrong about a particular building.
    public static class FacadeFinder
    {
        public enum Side { PlusZ, PlusX, MinusZ, MinusX }

        /// <summary>The side of this instance's footprint the facade is on, read off
        /// its meshes in the instance's own space (so measure it before rotating it).</summary>
        public static Side FrontOf(GameObject instance, out string report) =>
            FrontOf(instance, out report, out _);

        /// <summary>As above, and where the front WALL actually stands: metres in from
        /// the footprint's edge on that side to the densest plane of vertices - a bake
        /// that carries a forecourt, a stoop or a lot slab in its mesh has its wall
        /// well inside its bounds, and setting the bounds to the kerb parks the shop
        /// a yard up the street.</summary>
        public static Side FrontOf(GameObject instance, out string report, out float wallInset)
        {
            wallInset = 0f;
            var root = instance.transform;
            var filters = instance.GetComponentsInChildren<MeshFilter>();
            var counts = new int[4];
            Bounds bounds = default;
            bool started = false;

            // pass one: the footprint
            foreach (var f in filters)
            {
                var mesh = f.sharedMesh;
                if (!mesh) continue;
                var b = mesh.bounds;
                var c = b.center; var e = b.extents;
                for (int i = 0; i < 8; i++)
                {
                    var corner = c + new Vector3((i & 1) == 0 ? e.x : -e.x, (i & 2) == 0 ? e.y : -e.y, (i & 4) == 0 ? e.z : -e.z);
                    var p = root.InverseTransformPoint(f.transform.TransformPoint(corner));
                    if (started) bounds.Encapsulate(p); else { bounds = new Bounds(p, Vector3.zero); started = true; }
                }
            }
            if (!started)
            {
                report = "no mesh";
                return Side.PlusZ;
            }

            // pass two: how much detail sits against each side, in the outer quarter.
            // mesh.vertices hands out a fresh copy on every read, so each mesh is read
            // once here, brought into the instance's space, and kept for pass three
            float bandX = Mathf.Max(bounds.size.x * 0.25f, 0.5f);
            float bandZ = Mathf.Max(bounds.size.z * 0.25f, 0.5f);
            var points = new Vector3[filters.Length][];
            for (int k = 0; k < filters.Length; k++)
            {
                var f = filters[k];
                var mesh = f.sharedMesh;
                // Unity logs a native error rather than reliably throwing when vertices
                // are requested from a non-readable imported mesh, so guard explicitly.
                if (!mesh || !mesh.isReadable) continue;
                Vector3[] verts;
                try { verts = mesh.vertices; }
                catch { continue; } // a mesh locked against reading: skip it, judge on the rest
                var local = points[k] = new Vector3[verts.Length];
                for (int i = 0; i < verts.Length; i++)
                {
                    var p = local[i] = root.InverseTransformPoint(f.transform.TransformPoint(verts[i]));
                    if (p.z >= bounds.max.z - bandZ) counts[(int)Side.PlusZ]++;
                    if (p.x >= bounds.max.x - bandX) counts[(int)Side.PlusX]++;
                    if (p.z <= bounds.min.z + bandZ) counts[(int)Side.MinusZ]++;
                    if (p.x <= bounds.min.x + bandX) counts[(int)Side.MinusX]++;
                }
            }

            int best = 0;
            for (int i = 1; i < 4; i++) if (counts[i] > counts[best]) best = i;
            var front = (Side)best;

            // pass three: the wall plane on that side - a 0.1 m histogram of the
            // vertices' distance in from the edge, its densest bin is the wall
            var bins = new int[64];
            float band = front == Side.PlusX || front == Side.MinusX ? bandX : bandZ;
            foreach (var local in points)
            {
                if (local == null) continue;
                foreach (var p in local)
                {
                    float d = front switch
                    {
                        Side.PlusZ => bounds.max.z - p.z,
                        Side.PlusX => bounds.max.x - p.x,
                        Side.MinusZ => p.z - bounds.min.z,
                        _ => p.x - bounds.min.x,
                    };
                    if (d < 0f || d > band) continue;
                    int bin = Mathf.Min(bins.Length - 1, (int)(d / 0.1f));
                    bins[bin]++;
                }
            }
            int wall = 0;
            for (int i = 1; i < bins.Length; i++) if (bins[i] > bins[wall]) wall = i;
            wallInset = wall * 0.1f + 0.05f;

            report = "+Z " + counts[0] + "  +X " + counts[1] + "  -Z " + counts[2] + "  -X " + counts[3] +
                     " -> front " + front + ", wall " + wallInset.ToString("0.0") + " m in from the edge";
            return front;
        }

        /// <summary>Degrees of Y rotation that turn <paramref name="front"/> onto +Z -
        /// the catalogue's own convention, after which the usual "yaw the building
        /// onto its lot" arithmetic holds.</summary>
        public static float YawToPlusZ(Side front) => front switch
        {
            Side.PlusX => -90f,
            Side.MinusZ => 180f,
            Side.MinusX => 90f,
            _ => 0f,
        };
    }
}
