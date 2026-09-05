using System.Collections.Generic;
using RoadDemo;
using UnityEngine;

namespace HarborDemo
{
    public partial class HarborDistrict
    {
        void BuildTransitFrontage(float from, float to, ref int design, ref float backMax,
            List<Vector2> taken, GameObject loadingDock)
        {
            float width = to - from;
            if (width < 24f) return;
            int count = width > 105f ? 2 : 1;
            float pitch = width / count;
            for (int i = 0; i < count; i++)
            {
                float x0 = from + i * pitch + 3f, x1 = from + (i + 1) * pitch - 3f;
                float front = ShedFrontZ + (design % 3) * 0.8f;
                var bounds = BuildTransitShed(x0, x1, front, design++);
                taken.Add(new Vector2(bounds.min.x, bounds.max.x));
                backMax = Mathf.Max(backMax, bounds.max.z);
                if (loadingDock != null) BackDocks(loadingDock, bounds);
            }
        }

        void BuildTerminalGatehouse(float gateX, bool inbound)
        {
            float w = inbound ? 6f : 4.2f, d = inbound ? 8f : 4.5f, h = inbound ? 3.8f : 3f;
            var root = new GameObject(inbound ? "West customs office" : "East exit checkpoint").transform;
            root.SetParent(_warehouseRoot, false);
            root.localPosition = new Vector3(gateX + GateRoadHalf + 1f + w * 0.5f,
                TileTop, _serviceRoadZ0 - d * 0.5f - 2f);
            var shell = new Draft(); var glazing = new Draft(); var roof = new Draft();
            shell.Box(new Vector3(0f, h * 0.5f, 0f), new Vector3(w, h, d));
            roof.Box(new Vector3(0f, h + 0.12f, 0f), new Vector3(w + 0.8f, 0.24f, d + 0.8f));
            int windows = inbound ? 3 : 1;
            for (int k = 0; k < windows; k++)
                glazing.Box(new Vector3(-w * 0.5f - 0.025f, h - 1.1f, (k - (windows - 1) * 0.5f) * 2.2f),
                    new Vector3(0.04f, inbound ? 1.1f : 1.5f, inbound ? 1.5f : 3.3f));
            glazing.Box(new Vector3(0f, h - 1.1f, -d * 0.5f - 0.025f), new Vector3(w - 1f, 1.2f, 0.04f));
            if (inbound)
                roof.Box(new Vector3(w * 0.3f, h + 0.7f, d * 0.3f), new Vector3(0.6f, 1.4f, 0.6f));
            else
                roof.Box(new Vector3(0f, h + 0.4f, 0f), new Vector3(w + 0.4f, 0.55f, 0.35f));
            TerminalMesh(root, "Gatehouse walls", shell, Keep(HarborKit.Flat(root.name + " walls",
                inbound ? new Color(0.42f, 0.28f, 0.20f) : new Color(0.62f, 0.64f, 0.60f), 0.1f)));
            TerminalMesh(root, "Gatehouse windows", glazing, Keep(HarborKit.Flat(root.name + " glass", new Color(0.12f, 0.24f, 0.28f), 0.5f)));
            TerminalMesh(root, "Gatehouse roof", roof, Keep(HarborKit.Flat(root.name + " roof", new Color(0.24f, 0.28f, 0.28f), 0.15f)));
            var collider = root.gameObject.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, h * 0.5f, 0f); collider.size = new Vector3(w, h, d);
            BuildingCutaway.Prepare(root.gameObject);
        }

        Bounds BuildTransitShed(float x0, float x1, float front, int design)
        {
            int kind = design % 4;
            string[] names = { "Transit warehouse", "Sawtooth cargo hall", "Cold storage", "Marine workshop" };
            float[] depths = { 23f, 24f, 21f, 23f };
            float[] eaves = { 8.5f, 9f, 11f, 7.5f };
            Color[] colours = { new Color(0.52f, 0.48f, 0.38f), new Color(0.47f, 0.49f, 0.46f),
                new Color(0.68f, 0.69f, 0.64f), new Color(0.40f, 0.23f, 0.16f) };
            float w = x1 - x0, d = depths[kind], h = eaves[kind];
            var root = new GameObject(names[kind] + " " + (design + 1)).transform;
            root.SetParent(_warehouseRoot, false);
            root.localPosition = new Vector3((x0 + x1) * 0.5f, TileTop + ShedLift, front);
            var wall = new Draft();
            var roof = new Draft();
            var trim = new Draft();
            var glass = new Draft();
            var doors = new Draft();
            var bodyMat = Keep(HarborKit.Flat(root.name + " masonry", colours[kind], 0.08f));
            var roofMat = Keep(HarborKit.Flat(root.name + " roof", kind == 0 ? new Color(0.34f, 0.19f, 0.13f) :
                new Color(0.24f, 0.28f, 0.29f), 0.17f));
            var trimMat = Keep(HarborKit.Flat(root.name + " concrete framing", new Color(0.56f, 0.55f, 0.50f), 0.12f));
            var glassMat = Keep(HarborKit.Flat(root.name + " clerestory glass", new Color(0.17f, 0.28f, 0.31f), 0.5f));
            var doorMat = Keep(HarborKit.Flat(root.name + " doors", kind == 2 ? new Color(0.18f, 0.29f, 0.36f) :
                new Color(0.22f, 0.25f, 0.23f), 0.14f));

            wall.Box(new Vector3(0f, h * 0.5f, d * 0.5f), new Vector3(w, h, d));
            trim.Box(new Vector3(0f, 0.3f, d * 0.5f), new Vector3(w + 0.2f, 0.6f, d + 0.2f));
            int bays = Mathf.Clamp(Mathf.FloorToInt(w / 13f), 2, 6);
            float bayPitch = (w - 5f) / bays;
            for (int bay = 0; bay < bays; bay++)
            {
                float x = -w * 0.5f + 2.5f + (bay + 0.5f) * bayPitch;
                float doorWidth = kind == 3 ? 5.5f : 4.3f;
                doors.Box(new Vector3(x, 2.5f, -0.07f), new Vector3(doorWidth, 4.6f, 0.12f));
                foreach (float side in new[] { -1f, 1f })
                    trim.Box(new Vector3(x + side * (doorWidth * 0.5f + 0.18f), 2.7f, -0.2f), new Vector3(0.3f, 5.4f, 0.4f));
                trim.Box(new Vector3(x, 5.25f, -0.2f), new Vector3(doorWidth + 0.65f, 0.35f, 0.4f));
                for (float y = 0.7f; y < 4.8f; y += 0.33f)
                    trim.Box(new Vector3(x, y, -0.14f), new Vector3(doorWidth - 0.16f, 0.035f, 0.025f));
                if (kind != 2)
                {
                    glass.Box(new Vector3(x, h - 1.25f, -0.04f), new Vector3(bayPitch - 1.4f, 1.35f, 0.08f));
                    for (int pane = 0; pane < 5; pane++)
                        trim.Box(new Vector3(x + (pane - 2f) * (bayPitch - 1.4f) / 4f, h - 1.25f, -0.10f),
                            new Vector3(0.08f, 1.45f, 0.08f));
                }
                _shedDoors.Add(new Vector3(root.localPosition.x + x, TileTop, front - 1.5f));
            }
            // Full-height structural piers rhythmically divide the facades without cloning buildings.
            for (float x = -w * 0.5f; x <= w * 0.5f; x += kind == 2 ? 2.6f : 7f)
                trim.Box(new Vector3(x, h * 0.5f, -0.12f), new Vector3(kind == 2 ? 0.07f : 0.32f, h, 0.24f));

            if (kind == 1)
            {
                int teeth = Mathf.Max(3, Mathf.RoundToInt(w / 12f));
                float step = w / teeth;
                for (int i = 0; i < teeth; i++)
                {
                    float a = -w * 0.5f + i * step, b = a + step;
                    roof.Quad(new Vector3(a, h, -0.6f), new Vector3(a, h, d + 0.6f),
                        new Vector3(b, h + 3.4f, d + 0.6f), new Vector3(b, h + 3.4f, -0.6f));
                    glass.Box(new Vector3(b - 0.02f, h + 1.7f, d * 0.5f), new Vector3(0.08f, 3.4f, d));
                    wall.Triangle(new Vector3(a, h, 0f), new Vector3(b, h + 3.4f, 0f), new Vector3(b, h, 0f));
                    wall.Triangle(new Vector3(b, h, d), new Vector3(b, h + 3.4f, d), new Vector3(a, h, d));
                    for (float z = 0f; z <= d; z += 3f)
                        trim.Box(new Vector3(b + 0.035f, h + 1.7f, z), new Vector3(0.12f, 3.5f, 0.10f));
                }
            }
            else if (kind == 2)
            {
                roof.Box(new Vector3(0f, h + 0.18f, d * 0.5f), new Vector3(w + 0.6f, 0.36f, d + 0.6f));
                for (int unit = 0; unit < 3; unit++)
                {
                    float x = (unit - 1f) * w * 0.25f;
                    trim.Box(new Vector3(x, h + 1.1f, d * 0.7f), new Vector3(3.8f, 1.8f, 2.8f));
                    doors.Box(new Vector3(x, h + 1.1f, d * 0.7f - 1.42f), new Vector3(3.3f, 1.2f, 0.04f));
                }
                // Insulated loading canopy projects toward the yard, above truck clearance.
                roof.Box(new Vector3(0f, 5.8f, -1.25f), new Vector3(w - 2f, 0.22f, 2.5f));
            }
            else
            {
                float rise = kind == 0 ? 3.8f : 2.5f;
                roof.Quad(new Vector3(-w * 0.5f - 0.6f, h, -0.7f), new Vector3(-w * 0.5f - 0.6f, h + rise, d * 0.5f),
                    new Vector3(w * 0.5f + 0.6f, h + rise, d * 0.5f), new Vector3(w * 0.5f + 0.6f, h, -0.7f));
                roof.Quad(new Vector3(-w * 0.5f - 0.6f, h + rise, d * 0.5f), new Vector3(-w * 0.5f - 0.6f, h, d + 0.7f),
                    new Vector3(w * 0.5f + 0.6f, h, d + 0.7f), new Vector3(w * 0.5f + 0.6f, h + rise, d * 0.5f));
                foreach (float side in new[] { -1f, 1f })
                {
                    float x = side * w * 0.5f;
                    if (side < 0f) wall.Triangle(new Vector3(x, h, 0f), new Vector3(x, h, d), new Vector3(x, h + rise, d * 0.5f));
                    else wall.Triangle(new Vector3(x, h, d), new Vector3(x, h, 0f), new Vector3(x, h + rise, d * 0.5f));
                }
                // Ridge ventilators give the brick workshop a separate roof silhouette.
                if (kind == 3)
                    for (int i = 0; i < 3; i++)
                        trim.Box(new Vector3((i - 1f) * w * 0.25f, h + rise + 0.45f, d * 0.5f), new Vector3(2.2f, 0.9f, 2.2f));
            }
            TerminalMesh(root, "Masonry", wall, bodyMat);
            TerminalMesh(root, "Roof", roof, roofMat);
            TerminalMesh(root, "Structure", trim, trimMat);
            if (kind != 2) TerminalMesh(root, "Glazing", glass, glassMat);
            TerminalMesh(root, "Loading doors", doors, doorMat);
            var collider = root.gameObject.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, h * 0.5f, d * 0.5f);
            collider.size = new Vector3(w, h, d);
            BuildingCutaway.Prepare(root.gameObject);
            // Only the ground footprint fixes docks and streets; roof overhangs do not move roads.
            return new Bounds(new Vector3((x0 + x1) * 0.5f, TileTop + h * 0.5f, front + d * 0.5f), new Vector3(w, h, d));
        }
    }
}
