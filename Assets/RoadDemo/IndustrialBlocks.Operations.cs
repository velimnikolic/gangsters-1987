using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    public static partial class IndustrialBlocks
    {
        const string ProductionHall = KitBld + "building-production-hall.prefab";
        const string ProcessHall = KitBld + "building-process-hall.prefab";
        const string DistributionHall = KitBld + "building-distribution-hall.prefab";
        static readonly Dictionary<string, Material> WorkMaterials = new Dictionary<string, Material>();

        static Material WorkMaterial(string name, Color colour)
        {
            if (WorkMaterials.TryGetValue(name, out var known) && known) return known;
            string path = "Assets/Materials/Industrial_" + name + ".mat";
            var material = DemoAssetLoad.Load<Material>(path);
            if (!material)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                material.name = "Industrial_" + name;
                material.color = colour;
                if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.12f);
#if UNITY_EDITOR
                if (!Application.isPlaying) UnityEditor.AssetDatabase.CreateAsset(material, path);
#endif
            }
            WorkMaterials[name] = material;
            return material;
        }

        static Material Concrete => WorkMaterial("Concrete", new Color(0.43f, 0.43f, 0.40f));
        static Material Steel => WorkMaterial("Steel", new Color(0.20f, 0.24f, 0.24f));
        static Material Safety => WorkMaterial("Safety", new Color(0.70f, 0.55f, 0.22f));
        static Material Paint => WorkMaterial("Paint", new Color(0.72f, 0.71f, 0.62f));
        static Material Joint => WorkMaterial("Joint", new Color(0.18f, 0.19f, 0.18f));

        // Built-in meshes and persistent materials survive both the lab save and tray bake.
        static GameObject WorkShape(Block b, string name, Vector3 centre, Vector3 size,
                                    Material material, PrimitiveType primitive = PrimitiveType.Cube)
        {
            var go = GameObject.CreatePrimitive(primitive);
            go.name = name;
            go.transform.SetParent(b.Root, false);
            go.transform.position = centre;
            go.transform.localScale = size;
            var collider = go.GetComponent<Collider>();
            if (Application.isPlaying) Object.Destroy(collider); else Object.DestroyImmediate(collider);
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go;
        }

        static void Line(Block b, string name, float x, float z, float w, float d, Material mat)
        {
            if (w <= 0 || d <= 0) return;
            WorkShape(b, name, new Vector3(x, Deck + 0.085f, z), new Vector3(w, 0.014f, d), mat);
        }

        static void ConcretePad(Block b, string name, Rect area)
        {
            if (area.width < 1 || area.height < 1) return;
            WorkShape(b, name, new Vector3(area.center.x, Deck + 0.025f, area.center.y),
                new Vector3(area.width, 0.045f, area.height), Concrete);
            for (float x = area.xMin + 5f; x < area.xMax - 1f; x += 5f)
                Line(b, "concrete expansion joint", x, area.center.y, 0.035f, area.height, Joint);
            for (float z = area.yMin + 5f; z < area.yMax - 1f; z += 5f)
                Line(b, "concrete expansion joint", area.center.x, z, area.width, 0.035f, Joint);
        }

        static void FabricationWorks(Block b, System.Random rng)
        {
            var hf = Foot(ProductionHall, 180f);
            var hall = b.Put(ProductionHall, b.In, b.Far - hf.y, 180f);
            var office = b.Put(FactoryOld, b.In, b.Near, 180f);
            var sf = Foot(Workshop, 180f);
            var shop = b.Put(Workshop, b.Out - sf.x, b.Far - sf.y, 180f);
            float gateX = Mathf.Min(office.xMax + 3f, b.Out - 14f);
            b.Way = Gate(b, gateX, gateX + 11f);

            float crossZ = Mathf.Max(office.yMax + 2f, hall.yMin - 21f);
            b.ReserveRoute(new Rect(b.In + 1f, crossZ, b.Out - b.In - 2f, 8f));
            LoadingFace(b, hall, 36f, rng, "GOODS IN", false);
            if (shop.width > 0f) WorkshopApron(b, shop, rng);

            // Fabrication stock belongs beside the low production floor. Tall boiler
            // stacks and vessel banks are the processing plant's silhouette.
            float stockX = hall.xMax + 4f;
            float stockZ = hall.yMin + 5f;
            b.Prop(PipeStack, stockX, stockZ, 0f);
            b.Prop(WireSpool, stockX, stockZ + 6f, 0f);
            b.Prop(PipeStack, stockX, stockZ + 11f, 180f);
            StaffParking(b, new Rect(b.Out - 15f, b.Near + 2f, 13f, 6f), rng);
            StockPocket(b, new Rect(b.Out - 14f, b.Near + 14f, 11f, 7f), rng, false);
            GateOffice(b, rng);
            Lamps(b, b.Yard(3f), 3);
        }

        static void ProcessingWorks(Block b, System.Random rng)
        {
            // Put the tall process hall on the east, with an outdoor process train on
            // the west. The maintenance shop is now on the frontage, not behind it.
            var hf = Foot(ProcessHall, 180f);
            var hall = b.Put(ProcessHall, b.Out - hf.x, b.Far - hf.y, 180f);
            // The manoeuvring court is booked before the frontage buildings: on the port
            // zone's small parcels the court reaches the frontage, and a shop or shed
            // that would stand in it is refused rather than the court.
            float crossZ = hall.yMin - 21f;
            b.ReserveRoute(new Rect(b.In + 1f, crossZ, b.Out - b.In - 2f, 8f));
            var sf = Foot(Workshop, 180f);
            var shop = b.Put(Workshop, b.Out - sf.x, b.Near, 0f);
            b.Put(YardShed, b.In, b.Near, 180f);
            float gateX = hall.xMin + 4f;
            b.Way = Gate(b, gateX, gateX + 11f);
            LoadingFace(b, hall, 48f, rng, "GOODS IN", false);

            // The maintenance doors face into the court; their apron is north of the
            // building. Its vehicles and clearances follow that orientation too.
            if (shop.width > 0f)
            {
                float depth = Mathf.Min(8f, crossZ - shop.yMax - 0.5f);
                var apron = new Rect(shop.xMin, shop.yMax, shop.width, depth);
                if (depth > 1f)
                {
                    ConcretePad(b, "plant maintenance apron", apron);
                    if (depth >= 6f) b.Prop(Van, shop.center.x, apron.center.y, 0f);
                    b.ReserveService(apron);
                }
                WallBoard(b, shop, "SERVICE");
            }
            ProcessTrain(b, hall, rng);
            float parkingWidth = Mathf.Min(13f, (shop.width > 0f ? shop.xMin : b.Out) - b.Way.y - 3f);
            StaffParking(b, new Rect(b.Way.y + 2f, b.Near + 1f, parkingWidth, 6f), rng);
            StockPocket(b, new Rect(b.In + 1f, crossZ + 10f, 10f, 6f), rng, true);
            GateOffice(b, rng);
            Lamps(b, b.Yard(3f), 3);
        }

        static void ProcessTrain(Block b, Rect hall, System.Random rng)
        {
            if (hall.width < 1f) return;
            var island = Rect.MinMaxRect(b.In + 0.5f, hall.yMin + 1f,
                hall.xMin - 1f, b.Far - 0.5f);
            if (island.width < 13f || island.height < 21f || !b.Room(island)) return;
            ConcretePad(b, "process equipment foundation", island);
            float tankX = island.xMax - 5f;
            float rackX = island.xMax - 0.8f;
            float firstZ = island.yMin + 4f, lastZ = firstZ + 14f;
            for (int i = 0; i < 3; i++)
            {
                float z = firstZ + i * 7f;
                if (!b.Tank(tankX, z, 5f, i == 1 ? 15f : 12f)) continue;
                PipeBetween(b, new Vector3(tankX + 2.5f, Deck + 5f, z),
                    new Vector3(rackX, Deck + 5f, z), 0.3f);
            }
            // A supported pipe rack connects the vessels to the production wall.
            for (int run = 0; run < 2; run++)
                PipeBetween(b, new Vector3(rackX, Deck + 5f + run * 0.5f, firstZ),
                    new Vector3(rackX, Deck + 5f + run * 0.5f, lastZ), 0.3f);
            for (int i = 0; i < 3; i++)
                WorkShape(b, "pipe rack support", new Vector3(rackX, Deck + 2.4f, firstZ + i * 7f),
                    new Vector3(0.22f, 4.8f, 0.4f), Steel);
            PipeBetween(b, new Vector3(rackX, Deck + 5f, firstZ),
                new Vector3(hall.xMin + 0.1f, Deck + 5f, firstZ), 0.3f);

            float stackX = island.xMin + 3.5f, stackZ = island.yMax - 4f;
            b.Chimney(stackX, stackZ, Between(rng, 25f, 29f));
            b.ReserveService(island);
        }

        static void Distribution(Block b, System.Random rng)
        {
            b.Wall = Wall.Wire;
            var hf = Foot(DistributionHall, 180f);
            var hall = b.Put(DistributionHall, b.In + 1f, b.Far - hf.y, 180f);
            // the court first, for the same reason as the plant's: a small parcel keeps
            // its court and loses the gate office, not the other way round
            b.ReserveRoute(new Rect(b.In + 1f, hall.yMin - 22f, b.Out - b.In - 2f, 9f));
            var sf = Foot(YardShed, 180f);
            var office = b.Put(YardShed, b.In, b.Near, 180f);
            float gate = Mathf.Clamp(hall.xMax + 2f, office.xMax + 3f, b.Out - 12f);
            b.Way = Gate(b, gate, gate + 10f);
            LoadingFace(b, hall, 36f, rng, "DESPATCH", true);
            StaffParking(b, new Rect(b.In + 1f, office.yMax + 2f, 13f, 6f), rng);
            StockPocket(b, new Rect(b.In + 20f, b.Near + 2f, 11f, 8f), rng, false);
            GateOffice(b, rng);
            Lamps(b, b.Yard(3f), 3);
        }

        static void TradeCourt(Block b, System.Random rng)
        {
            b.Wall = Wall.Wire;
            // Select distinct buildings by their measured footprint. A barrel-roof hall,
            // brick premises and small outbuildings read as a compound built over time.
            // Never fill spare frontage by repeating the last workshop.
            var stock = new List<string> { Workshop, ShedSmall, DepotGarage, FactoryOld };
            var selected = new List<string> { FactoryHall };
            float remaining = b.Out - b.In - Foot(FactoryHall, 180f).x;
            while (stock.Count > 0)
            {
                var fits = stock.FindAll(path => Foot(path, 180f).x + 3f <= remaining);
                if (fits.Count == 0) break;
                string path = fits[rng.Next(fits.Count)];
                selected.Add(path);
                remaining -= Foot(path, 180f).x + 3f;
                stock.Remove(path);
            }
            if (Foot(YardShed, 180f).x + 3f <= remaining) selected.Add(YardShed);
            for (int i = selected.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (selected[i], selected[j]) = (selected[j], selected[i]);
            }
            var units = new List<(string path, Rect footprint)>();
            float x = b.In, front = b.Far;
            foreach (string path in selected)
            {
                var foot = Foot(path, 180f);
                float setback = Between(rng, 0f, 2f);
                var unit = b.Put(path, x, b.Far - foot.y - setback, 180f);
                if (unit.width > 0f)
                {
                    units.Add((path, unit));
                    front = Mathf.Min(front, unit.yMin);
                }
                x += foot.x + 3f;
            }
            b.Way = Gate(b, b.Out - 12f, b.Out - 2f);
            b.ReserveRoute(new Rect(b.In + 1f, front - 20f, b.Out - b.In - 2f, 8f));
            foreach (var unit in units) TradeApron(b, unit.footprint, unit.path, rng);
            StaffParking(b, new Rect(b.In + 1f, b.Near + 1f, 16f, 6f), rng);
            StockPocket(b, new Rect(b.In + 23f, b.Near + 1f, 10f, 7f), rng, false);
            GateOffice(b, rng);
            Lamps(b, b.Yard(3f), 2);
        }

        static void TradeApron(Block b, Rect unit, string path, System.Random rng)
        {
            // Each business gets the yard it uses, not another copy of the same van,
            // pallet cluster and sign. All goods doors still face the shared court.
            if (path == Workshop) { WorkshopApron(b, unit, rng); return; }
            if (path == YardShed)
            {
                ConcretePad(b, "outbuilding doorstep", new Rect(unit.xMin, unit.yMin - 2f, unit.width, 2f));
                b.Prop(Dumpster, unit.center.x, unit.yMin - 4f, 0f);
                return;
            }
            if (path == FactoryOld)
            {
                ConcretePad(b, "office entrance walk", new Rect(unit.xMin, unit.yMin - 2f, unit.width, 2f));
                StaffParking(b, new Rect(unit.xMin + 1f, unit.yMin - 9f, 8.4f, 5.4f), rng);
                WallBoard(b, unit, "TRADE OFFICE");
                return;
            }
            ConcretePad(b, "trade loading apron", new Rect(unit.xMin, unit.yMin - 10f, unit.width, 10f));
            if (path == FactoryHall)
            {
                b.Prop(Forklift, unit.xMin + 4f, unit.yMin - 5f, 180f);
                b.Prop(PipeStack, unit.xMax - 4f, unit.yMin - 4f, 90f);
                b.Prop(WireSpool, unit.xMax - 4f, unit.yMin - 7.5f, 0f);
                WallBoard(b, unit, "METALWORK");
            }
            else
            {
                b.Prop(path == DepotGarage ? TownLorry : BoxLorry, unit.center.x + 4f, unit.yMin - 5.5f, 180f);
                StockPocket(b, new Rect(unit.xMin + 1f, unit.yMin - 5f, 6f, 4f), rng, false);
            }
            b.ReserveService(new Rect(unit.center.x - 2f, unit.yMin - 10f, 6f, 10f));
        }

        static void LoadingFace(Block b, Rect hall, float nominalWidth, System.Random rng, string label, bool docks)
        {
            if (hall.width < 1f) return;
            var apron = new Rect(hall.xMin, hall.yMin - 11.5f, hall.width, 11.5f);
            // On the port zone's small parcels a frontage building can stand where the
            // court would be; a hall with a building across its face has no loading
            // face, rather than a pad poured under the office. Only BUILDINGS count: the
            // drive booked by the gate and the court itself run over the apron by design.
            foreach (var foot in b.Built) if (foot.Overlaps(apron)) return;
            ConcretePad(b, "reinforced loading apron", apron);
            // Doors are authored nine metres from each end of the nominal wall.
            for (int i = 0; i < 2; i++)
            {
                float x = hall.center.x + (i == 0 ? -1 : 1) * (nominalWidth * 0.5f - 9f);
                for (int side = -1; side <= 1; side += 2)
                {
                    Line(b, "loading bay edge", x + side * 3.4f, hall.yMin - 6f, 0.14f, 10f, Safety);
                    b.Prop(Bollard, x + side * 3.3f, hall.yMin - 0.8f, 0f);
                }
                if (docks)
                {
                    WorkShape(b, "dock platform", new Vector3(x, Deck + 0.6f, hall.yMin - 1.2f),
                        new Vector3(6f, 1.2f, 2.4f), Concrete);
                    for (int s = -1; s <= 1; s += 2)
                        WorkShape(b, "dock rubber bumper", new Vector3(x + s * 1.4f, Deck + 0.65f, hall.yMin - 2.45f),
                            new Vector3(0.24f, 0.7f, 0.2f), Joint);
                }
                float truckZ = hall.yMin - (docks ? 7.4f : 6f);
                if (i == 0) b.Prop(BoxLorry, x, truckZ, 180f);
                var approach = new Rect(x - 3.5f, hall.yMin - 11.5f, 7f, 11.5f);
                b.ReserveService(approach);
                WallBoard(b, hall, label + "  0" + (i + 1));
            }
            b.Prop(Forklift, hall.xMin + 2f, hall.yMin - 4.5f, 180f);
            StockPocket(b, new Rect(hall.center.x - 3f, hall.yMin - 5.5f, 6f, 4f), rng, false);
        }

        static void WorkshopApron(Block b, Rect shop, System.Random rng)
        {
            if (shop.width < 1f) return;
            var apron = new Rect(shop.xMin, shop.yMin - 8f, shop.width, 8f);
            ConcretePad(b, "workshop service apron", apron);
            b.Prop(Van, shop.center.x + 3f, shop.yMin - 4.5f, 180f);
            b.ReserveService(new Rect(shop.center.x, shop.yMin - 8f, 6f, 8f));
            StockPocket(b, new Rect(shop.xMin + 0.5f, shop.yMin - 5f, 5f, 4f), rng, false);
            WallBoard(b, shop, "SERVICE");
        }

        static void StaffParking(Block b, Rect area, System.Random rng)
        {
            int bays = Mathf.FloorToInt(area.width / 2.8f);
            for (int i = 0; i < bays; i++)
            {
                var bay = new Rect(area.xMin + i * 2.8f, area.yMin, 2.8f, 5.4f);
                if (!b.Room(bay)) continue;
                Line(b, "staff parking", bay.xMin, bay.center.y, 0.1f, bay.height, Paint);
                Line(b, "parking wheel stop", bay.center.x, bay.yMin + 0.4f, 1.7f, 0.16f, Concrete);
                if (Chance(rng, 0.65f)) b.Prop(Any(StaffCars, rng), bay.center.x, bay.center.y, 180f);
                b.ReserveService(bay);
            }
        }

        static void StockPocket(Block b, Rect area, System.Random rng, bool chemicals)
        {
            if (area.width < 4f || area.height < 3f || !b.Room(area)) return;
            for (int i = 0; i < Mathf.Min(4, (int)(area.width / 2f)); i++)
            {
                float x = area.xMin + 1f + 2f * i, z = area.yMin + 1.2f;
                var pallet = b.Prop(Pallet, x, z, 0f);
                if (!pallet) continue;
                float y = Box(Pallet).size.y;
                if (chemicals) b.Atop(BarrelMetal, x, z, 0f, y);
                else
                    for (int k = 1; k <= rng.Next(2, 5); k++) b.Atop(Pallet, x, z, 0f, y * k);
            }
            b.ReserveService(area);
            Line(b, "stock zone edge", area.center.x, area.yMax, area.width, 0.12f, Safety);
        }

        static void GateOffice(Block b, System.Random rng)
        {
            Gatepost(b, rng);
            // A narrow pedestrian route alongside the throat, with a visible crossing.
            for (float z = b.Near + 1f; z < b.Near + 4f; z += 0.65f)
                Line(b, "gate pedestrian crossing", (b.Way.x + b.Way.y) * 0.5f, z, b.Way.y - b.Way.x, 0.28f, Paint);
        }

        static void WallBoard(Block b, Rect building, string text)
        {
            // Mounts belong to the authored facade, before the mesh is recentered for
            // baking. Whole-building bounds include awnings and cannot locate a wall.
            var mount = b.SignMount(building, text);
            if (!mount) return;
            float width = mount.lossyScale.x, height = mount.lossyScale.y;
            var board = WorkShape(b, "goods door sign", mount.position,
                new Vector3(width, height, 0.06f), Steel);
            board.transform.rotation = mount.rotation;
            var go = new GameObject("sign " + text);
            go.transform.SetParent(b.Root, false);
            go.transform.position = mount.position + mount.forward * 0.045f;
            // Authored facade faces +Z; TextMesh's readable face points toward -Z.
            go.transform.rotation = mount.rotation * Quaternion.Euler(0f, 180f, 0f);
            var label = go.AddComponent<TextMesh>();
            label.text = text;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.characterSize = 0.13f;
            label.fontSize = 48;
            label.color = new Color(0.86f, 0.83f, 0.66f);
            var size = go.GetComponent<Renderer>().localBounds.size;
            float fit = Mathf.Min(1f, (width - 0.2f) / Mathf.Max(size.x, 0.001f),
                (height - 0.16f) / Mathf.Max(size.y, 0.001f));
            go.transform.localScale = Vector3.one * fit;
        }

        static void PipeBetween(Block b, Vector3 from, Vector3 to, float diameter)
        {
            var go = WorkShape(b, "boiler flue", (from + to) * 0.5f,
                new Vector3(diameter, Vector3.Distance(from, to) * 0.5f, diameter), Steel, PrimitiveType.Cylinder);
            go.transform.rotation = Quaternion.FromToRotation(Vector3.up, to - from);
        }

        static void OperationalMarkings(Block b)
        {
            foreach (var route in b.Routes)
            {
                // The empty cross-court is a manoeuvring lane, with a drain along its edge.
                for (float x = route.xMin + 2f; x < route.xMax - 2f; x += 6f)
                    Line(b, "yard traffic centre dash", x, route.center.y, 2f, 0.12f, Paint);
                Line(b, "trench drain", route.center.x, route.yMax - 0.2f, route.width, 0.24f, Joint);
                for (float x = route.xMin; x < route.xMax; x += 0.8f)
                    Line(b, "drain grate", x, route.yMax - 0.2f, 0.06f, 0.24f, Steel);
            }
        }
    }
}
