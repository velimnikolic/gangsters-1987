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

        // Door hardware stays on the facade; service equipment stays against the
        // side walls so neither the aircraft opening nor the apron is cluttered.
        static void BuildHangarEquipment(Transform t, float w, float d, float eave,
            float rise, float doorWidth, bool open, bool workshop)
        {
            float hx = w * 0.5f, hz = d * 0.5f, head = 6f;
            Slab(t, "door track hood", new Vector3(0, head + 0.12f, hz + 0.45f),
                new Vector3(w, 0.22f, 0.7f), Steel);
            for (float x = -hx + 0.5f; x < hx; x += 3f)
                Slab(t, "track wall bracket", new Vector3(x, head + 0.3f, hz + 0.25f),
                    new Vector3(0.12f, 0.55f, 0.5f), Black);
            if (!open)
                for (float x = -doorWidth * 0.5f + 3f; x < doorWidth * 0.5f; x += 6f)
                {
                    Slab(t, "door leaf reinforcing rail", new Vector3(x, 2.7f, hz + 0.3f),
                        new Vector3(5.7f, 0.12f, 0.12f), Steel);
                    Slab(t, "door pull handle", new Vector3(x + 2.35f, 1.15f, hz + 0.45f),
                        new Vector3(0.08f, 0.55f, 0.14f), Black);
                }
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * (hx - 0.6f);
                Slab(t, "jamb safety strip", new Vector3(x, 1.1f, hz + 0.4f),
                    new Vector3(0.24f, 2.2f, 0.12f), Yellow);
                for (int band = 0; band < 4; band++)
                    Slab(t, "jamb black band", new Vector3(x, 0.3f + band * 0.5f, hz + 0.47f),
                        new Vector3(0.26f, 0.2f, 0.04f), Black);
                Beam(t, "floodlight arm", new Vector3(x, head - 0.4f, hz),
                    new Vector3(x, head - 0.25f, hz + 0.95f), 0.09f, Steel);
                var lamp = Slab(t, "apron floodlight housing", new Vector3(x, head - 0.35f, hz + 0.95f),
                    new Vector3(0.65f, 0.32f, 0.4f), Black);
                lamp.transform.localRotation = Quaternion.Euler(25f, 0, 0);
                Slab(t, "apron floodlight lens", new Vector3(x, head - 0.43f, hz + 1.15f),
                    new Vector3(0.54f, 0.2f, 0.04f), White).transform.localRotation = lamp.transform.localRotation;

                // Vertical facade ribs and a louvred vent on each gable wall.
                for (float z = -hz + 1f; z < hz; z += 3f)
                    Slab(t, "side wall stiffener", new Vector3(side * (hx + 0.12f), eave * 0.5f, z),
                        new Vector3(0.18f, eave, 0.12f), Steel);
                Slab(t, "vent recess", new Vector3(side * (hx + 0.24f), eave - 1f, 0),
                    new Vector3(0.12f, 1.2f, 2f), Black);
                for (int blade = 0; blade < 6; blade++)
                    Slab(t, "vent louvre", new Vector3(side * (hx + 0.34f), eave - 1.5f + blade * 0.2f, 0),
                        new Vector3(0.25f, 0.07f, 1.9f), Steel);

                // Raised rooflight cassettes follow the actual slope, including overhang.
                float roofZ = side * hz * 0.5f;
                float roofY = eave + rise * (1f - Mathf.Abs(roofZ) / (hz + 0.4f));
                var tilt = Quaternion.Euler(side * Mathf.Atan(rise / (hz + 0.4f)) * Mathf.Rad2Deg, 0, 0);
                for (int bay = -1; bay <= 1; bay++)
                {
                    var centre = new Vector3(bay * w * 0.25f, roofY + 0.16f, roofZ);
                    Slab(t, "rooflight frame", centre, new Vector3(2.1f, 0.22f, 3.4f), Steel)
                        .transform.localRotation = tilt;
                    Slab(t, "rooflight panel", centre + tilt * new Vector3(0, 0.14f, 0),
                        new Vector3(1.85f, 0.06f, 3.1f), Glass).transform.localRotation = tilt;
                    Slab(t, "rooflight crossbar", centre + tilt * new Vector3(0, 0.2f, 0),
                        new Vector3(2f, 0.06f, 0.08f), Steel).transform.localRotation = tilt;
                }
            }

            // Personnel entrance on the west side, clear of the maintenance office.
            float serviceZ = -hz + 4f;
            Slab(t, "personnel door frame", new Vector3(-hx - 0.18f, 1.2f, serviceZ),
                new Vector3(0.2f, 2.4f, 1.3f), Steel);
            Slab(t, "personnel door", new Vector3(-hx - 0.3f, 1.14f, serviceZ),
                new Vector3(0.06f, 2.22f, 1.1f), Blue);
            Slab(t, "personnel door handle", new Vector3(-hx - 0.38f, 1.05f, serviceZ + 0.37f),
                new Vector3(0.1f, 0.08f, 0.22f), Steel);
            Slab(t, "personnel rain hood", new Vector3(-hx - 0.55f, 2.65f, serviceZ),
                new Vector3(1.15f, 0.12f, 1.7f), Steel);
            Slab(t, "electrical cabinet", new Vector3(-hx - 0.35f, 1.3f, serviceZ + 2f),
                new Vector3(0.45f, 1.2f, 0.65f), Steel);
            Tube(t, "electrical conduit", new Vector3(-hx - 0.2f, 1.9f, serviceZ + 2f),
                0.035f, eave - 2f, Black, 6);
            Legend(t, "STAFF", new Vector3(-hx - 0.35f, 2.15f, serviceZ), 0.22f, White, yaw: 270f);

            if (open) BuildHangarWorkshop(t, hx, hz, eave);
            if (workshop)
            {
                // The lean-to office has its own recognisable window rhythm.
                foreach (float windowZ in new[] { -5f, 4.5f })
                {
                    Slab(t, "office window frame", new Vector3(hx + 5.05f, 1.9f, windowZ),
                        new Vector3(0.16f, 1.2f, 2.3f), Steel);
                    Slab(t, "office glazing", new Vector3(hx + 5.15f, 1.9f, windowZ),
                        new Vector3(0.05f, 1f, 2.1f), Glass);
                    Slab(t, "office mullion", new Vector3(hx + 5.2f, 1.9f, windowZ),
                        new Vector3(0.06f, 1.1f, 0.06f), Steel);
                }
            }
        }

        static void BuildHangarWorkshop(Transform t, float hx, float hz, float eave)
        {
            for (float z = -hz + 3f; z < hz; z += 6f)
            {
                Beam(t, "interior roof tie", new Vector3(-hx + 0.25f, eave - 0.35f, z),
                    new Vector3(hx - 0.25f, eave - 0.35f, z), 0.18f, Steel);
                Slab(t, "workshop strip lamp", new Vector3(0, eave - 0.5f, z),
                    new Vector3(2.4f, 0.1f, 0.22f), White);
            }
            float x = hx - 1.1f;
            Slab(t, "workbench", new Vector3(x, 0.95f, -hz + 4f),
                new Vector3(1.4f, 0.15f, 4f), Steel);
            for (int end = -1; end <= 1; end += 2)
                Slab(t, "workbench legs", new Vector3(x, 0.45f, -hz + 4f + end * 1.7f),
                    new Vector3(1.2f, 0.9f, 0.1f), Black);
            Slab(t, "tool cabinet", new Vector3(x, 0.6f, -hz + 7f),
                new Vector3(1.2f, 1.2f, 0.75f), Red);
            for (int drawer = 0; drawer < 4; drawer++)
                Slab(t, "tool drawer pull", new Vector3(x - 0.63f, 0.25f + drawer * 0.25f, -hz + 7f),
                    new Vector3(0.06f, 0.04f, 0.5f), Steel);
            for (int shelf = 0; shelf < 3; shelf++)
                Slab(t, "stores shelf", new Vector3(-hx + 0.9f, 0.3f + shelf * 0.9f, -hz + 2.5f),
                    new Vector3(1.2f, 0.1f, 3.5f), Steel);
            for (int end = -1; end <= 1; end += 2)
                for (int face = -1; face <= 1; face += 2)
                    Slab(t, "stores rack upright", new Vector3(-hx + 0.9f + face * 0.55f, 1.2f, -hz + 2.5f + end * 1.65f),
                        new Vector3(0.08f, 2.4f, 0.08f), Steel);
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
