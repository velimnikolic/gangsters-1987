using System.Collections.Generic;
using UnityEngine;

namespace HarborDemo
{
    public partial class HarborDistrict
    {
        readonly List<Mesh> _terminalMeshes = new List<Mesh>();
        readonly Material[] _slabMaterials = new Material[6];

        // Large reinforced pours with sealed joints, separate from the city's sidewalks.
        // Geometry is grouped by finish, so hundreds of pours cost six renderers.
        void PourTerminalApron(string name, Rect area)
        {
            var batches = new Draft[6];
            for (int i = 0; i < batches.Length; i++)
            {
                batches[i] = new Draft();
                if (_slabMaterials[i] == null)
                {
                    float tone = 0.47f + i * 0.009f;
                    _slabMaterials[i] = Keep(HarborKit.Flat("Quay concrete " + i,
                        new Color(tone + 0.025f, tone + 0.018f, tone), 0.06f));
                }
            }
            for (float z = area.yMin; z < area.yMax - 0.01f; z += 10f)
                for (float x = area.xMin; x < area.xMax - 0.01f; x += 12f)
                {
                    float w = Mathf.Min(12f, area.xMax - x), d = Mathf.Min(10f, area.yMax - z);
                    int finish = Mathf.Abs(Mathf.RoundToInt(x * 7f + z * 13f)) % batches.Length;
                    batches[finish].Box(new Vector3(x + w * 0.5f, TileTop - 0.15f, z + d * 0.5f),
                        new Vector3(w - 0.035f, 0.3f, d - 0.035f));
                }
            for (int i = 0; i < batches.Length; i++)
                TerminalMesh(_apronRoot, name + " / pours " + i, batches[i], _slabMaterials[i]);
        }

        void TerminalMesh(Transform parent, string name, Draft draft, Material material)
        {
            var mesh = draft.Bake(name);
            _terminalMeshes.Add(mesh);
            MeshPart(parent, name, mesh, material);
        }

        void BuildTerminalInfrastructure()
        {
            var root = Root("Harbor Terminal Infrastructure");
            var yellow = Keep(HarborKit.Flat("Faded quay safety paint", new Color(0.72f, 0.56f, 0.20f), 0.05f));
            var steel = Keep(HarborKit.Flat("Terminal galvanized steel", new Color(0.31f, 0.34f, 0.34f), 0.25f));
            var dark = Keep(HarborKit.Flat("Drain grilles", new Color(0.12f, 0.14f, 0.14f), 0.1f));
            var lamp = Keep(HarborKit.Flat("Floodlight lenses", new Color(0.78f, 0.78f, 0.65f), 0.4f));
            var paint = new Draft();
            var metal = new Draft();
            var drains = new Draft();
            var lenses = new Draft();
            float y = TileTop + 0.028f;

            // Mark the same clear door corridors that constrain apron furniture.
            foreach (var door in _shedDoors)
            {
                float from = ShoulderZ + 3f, to = door.z - 1f;
                foreach (float side in new[] { -3.2f, 3.2f })
                    paint.Box(new Vector3(door.x + side, y, (from + to) * 0.5f),
                        new Vector3(0.10f, 0.014f, to - from));
            }

            // Two continuous edge lines define the working strip beside the crane rails.
            foreach (float z in new[] { 3.7f, HarborCrane.LandRailZ + 1.1f })
                paint.Box(new Vector3(0f, y, z), new Vector3(QuayHalf * 2f - 5f, 0.014f, 0.16f));
            for (int i = 0; i < berths; i++)
            {
                float bx = BerthX(i);
                // Mark the live landing bays using the cargo system's actual slot geometry.
                if (IsBoxBerth(i))
                    for (int k = 0; k < 7; k++)
                    {
                        float x = bx - 21f + k * BoxPitch;
                        foreach (float z in new[] { LiveRowZ - 1.45f, LiveRowZ + 1.45f })
                            paint.Box(new Vector3(x, y, z), new Vector3(6.4f, 0.014f, 0.10f));
                        foreach (float edge in new[] { -3.2f, 3.2f })
                            paint.Box(new Vector3(x + edge, y, LiveRowZ), new Vector3(0.10f, 0.014f, 2.9f));
                    }

                // High mast lighting sits beyond the storage strip, clear of forklift aisles.
                float mx = bx - 32f, mz = BlockZ1 + 0.4f;
                metal.Box(new Vector3(mx, TileTop + 12f, mz), new Vector3(0.38f, 24f, 0.38f));
                metal.Box(new Vector3(mx, TileTop + 23.8f, mz), new Vector3(5f, 0.25f, 0.5f));
                for (int k = 0; k < 4; k++)
                {
                    float lx = mx - 1.8f + k * 1.2f;
                    metal.Box(new Vector3(lx, TileTop + 23.5f, mz), new Vector3(0.85f, 0.7f, 0.65f));
                    lenses.Box(new Vector3(lx, TileTop + 23.45f, mz - 0.34f), new Vector3(0.69f, 0.45f, 0.025f));
                }
                // Short drainage channels at the apron edge, outside all vehicle wheel paths.
                for (int k = 0; k < 28; k++)
                    drains.Box(new Vector3(bx - 5.4f + k * 0.4f, y, 3.1f), new Vector3(0.12f, 0.016f, 0.55f));
            }
            TerminalMesh(root, "Working bay markings", paint, yellow);
            TerminalMesh(root, "Floodlight masts", metal, steel);
            TerminalMesh(root, "Apron drainage", drains, dark);
            TerminalMesh(root, "Floodlight faces", lenses, lamp);
        }
    }
}
