using UnityEngine;

namespace AirportDemo.EditorTools
{
    public static partial class AirportKitBash
    {
        // These pieces pass through the existing material-combining bake, so detail
        // does not become a renderer or a runtime behaviour for every rib and mullion.
        static void BuildTerminalArchitecture(Transform t, float hx, float hz, float ux, float uz)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                float z = side * (hz + 0.65f);
                Slab(t, "continuous sunshade", new Vector3(0, 2.75f, z),
                    new Vector3(hx * 2 + 0.8f, 0.18f, 1.6f), Plaster);
                Slab(t, "bronze fascia", new Vector3(0, 2.95f, z + side * 0.75f),
                    new Vector3(hx * 2 + 0.8f, 0.24f, 0.12f), Steel);
                // Deep fins cast real shadows across the upper hall's window band.
                for (float x = -ux; x <= ux; x += 3.8f)
                    Slab(t, "hall sun fin", new Vector3(x, 4.6f, side * (uz + 0.45f)),
                        new Vector3(0.16f, 2.85f, 1.1f), Plaster);
                Slab(t, "hall overhang", new Vector3(0, 6.15f, side * (uz + 0.55f)),
                    new Vector3(ux * 2 + 1.6f, 0.22f, 2f), Plaster);
            }

            Slab(t, "airside name board", new Vector3(0, 5.2f, uz + 1.15f),
                new Vector3(24f, 1.25f, 0.18f), Blue);
            Legend(t, "COUNTY AIRPORT", new Vector3(0, 5.2f, uz + 1.27f), 0.7f, White);
            for (int i = 0; i < AirportSpec.CommuterStandX.Length; i++)
            {
                // Buildings rotate 180 degrees when placed; keep each sign over its
                // authoritative boarding door, including the reversal of local X.
                float x = -AirportSpec.GateDoorX(i);
                Slab(t, "gate hood", new Vector3(x, 2.45f, hz + 1.1f),
                    new Vector3(4.5f, 0.16f, 2.3f), Steel);
                Slab(t, "gate sign", new Vector3(x, 2.85f, hz + 2.2f),
                    new Vector3(2.8f, 0.7f, 0.12f), Blue);
                Legend(t, "G" + (i + 1), new Vector3(x, 2.85f, hz + 2.3f), 0.48f, White);
            }
            // Roof seams and raised skylights give the broad flat wings a metre scale.
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * (ux + 13f);
                for (int i = -1; i <= 1; i++)
                {
                    Slab(t, "skylight curb", new Vector3(x, 3.38f, i * 7f),
                        new Vector3(6f, 0.42f, 2.4f), Steel);
                    Slab(t, "skylight glass", new Vector3(x, 3.62f, i * 7f),
                        new Vector3(5.65f, 0.1f, 2.05f), Glass);
                }
            }
        }

        static void BuildHangarFinish(Transform t, float w, float d, float eave, float rise, string label)
        {
            float hz = d * 0.5f;
            for (float x = -w * 0.5f + 0.6f; x < w * 0.5f; x += 1.5f)
                for (int side = -1; side <= 1; side += 2)
                    Beam(t, "standing roof seam", new Vector3(x, eave + rise + 0.05f, 0),
                        new Vector3(x, eave + 0.04f, side * (hz + 0.35f)), 0.045f, Steel);
            for (int side = -1; side <= 1; side += 2)
            {
                Slab(t, "eaves gutter", new Vector3(0, eave - 0.05f, side * (hz + 0.43f)),
                    new Vector3(w + 0.9f, 0.16f, 0.18f), Steel);
                for (int end = -1; end <= 1; end += 2)
                    Tube(t, "downpipe", new Vector3(end * (w * 0.5f - 0.35f), 0.15f, side * (hz + 0.35f)),
                        0.07f, eave - 0.2f, Steel, 6);
            }
            Slab(t, "hangar sign", new Vector3(0, eave + 0.5f, hz + 0.22f),
                new Vector3(w * 0.65f, 1f, 0.16f), Blue);
            for (int side = -1; side <= 1; side += 2)
                Slab(t, "sign bracket", new Vector3(side * w * 0.22f, eave + 0.2f, hz + 0.12f),
                    new Vector3(0.1f, 1.6f, 0.1f), Steel);
            Legend(t, label, new Vector3(0, eave + 0.5f, hz + 0.33f), 0.62f, White);
        }

        static void Beam(Transform t, string name, Vector3 a, Vector3 b, float width, Material mat)
        {
            var go = Slab(t, name, (a + b) * 0.5f, new Vector3(width, width, (b - a).magnitude), mat);
            go.transform.localRotation = Quaternion.LookRotation(b - a);
        }

        static void BuildControlCab(Transform t, float shaftTop)
        {
            float sill = shaftTop + 1.15f, top = shaftTop + 3.85f;
            Tube(t, "octagonal cab base", new Vector3(0, shaftTop + 0.24f, 0), 3.65f, 0.91f, Plaster, 8);
            // Outward-sloping panes under a deep roof overhang establish the cab's
            // silhouette from across the ramp; mullions break up the glazing.
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f, b = (i + 1) * Mathf.PI / 4f;
                Vector3 Point(float angle, float radius, float y)
                    => new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);
                var p0 = Point(a, 3.65f, sill);
                var p1 = Point(b, 3.65f, sill);
                var p2 = Point(b, 4.2f, top);
                var p3 = Point(a, 4.2f, top);
                var mesh = new Mesh { name = "tower glazing" };
                mesh.vertices = new[] { p0, p1, p2, p3 };
                mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                Mesh(t, "sloped cab pane", mesh, Glass, Vector3.zero);
                Beam(t, "cab mullion", p0, p3, 0.14f, Steel);
                Beam(t, "cab sill", p0, p1, 0.13f, Steel);
            }
            Tube(t, "cab roof overhang", new Vector3(0, top, 0), 4.75f, 0.32f, Steel, 8);
            Tube(t, "cab roof cap", new Vector3(0, top + 0.32f, 0), 4.25f, 0.18f, RoofDeck, 8);
            for (int side = -1; side <= 1; side += 2)
                Slab(t, "shaft accent", new Vector3(side * 2.58f, shaftTop * 0.5f, 0),
                    new Vector3(0.15f, shaftTop, 0.8f), Blue);
        }
    }
}
