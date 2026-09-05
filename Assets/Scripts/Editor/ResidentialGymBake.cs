using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RoadDemo;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LivingCity.EditorTools
{
    /// <summary>Bakes the shared gym venue, retaining the original entrance architecture.</summary>
    public static class ResidentialGymBake
    {
        const string Path = "Assets/Prefabs/Residential/gym.prefab";
        const string Folder = "Assets/Prefabs/Residential/Gym";
        const string Palm = "Assets/Synty/PolygonPalmCity/Prefabs/";
        const float Deck = ResidentialGym.Deck;
        static Material concrete, rubber, turf, steel, wood, paint, wear, soil, warmSlab;
        static Transform geometry;

        [MenuItem("Tools/City/Residential/Bake Expanded Gym")]
        static void Menu() => Bake();

        public static bool Bake()
        {
            var root = PrefabUtility.LoadPrefabContents(Path);
            try
            {
                EnsureEntrance(root);
                while (root.transform.childCount > 0) Object.DestroyImmediate(root.transform.GetChild(0).gameObject);
                concrete = Material("Concrete", new Color(.48f, .465f, .435f), "STONE");
                rubber = Material("Rubber", new Color(.115f, .125f, .13f), "PLASTIC");
                turf = Material("TrainingTurf", new Color(.19f, .255f, .19f), "PLASTIC");
                steel = Material("Steel", new Color(.14f, .16f, .16f), "PAINT", .3f);
                wood = Material("Timber", new Color(.36f, .235f, .135f), "WOOD");
                paint = Material("Markings", new Color(.66f, .64f, .54f), "PAINT");
                wear = Material("RubberWear", new Color(.155f, .16f, .16f), "PLASTIC");
                soil = Material("PlantingSoil", new Color(.17f, .125f, .085f), "STONE");
                warmSlab = Material("ConcreteRepair", new Color(.435f, .418f, .382f), "STONE");
                geometry = Group(root.transform, "Venue structure");
                BuildGround();
                BuildBoundary(root.transform);
                var entrance = (GameObject)PrefabUtility.InstantiatePrefab(
                    AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/Entrance.prefab"), root.transform);
                entrance.name = "Original gym entrance";
                entrance.transform.localPosition = new Vector3(.8f, Deck - 1.253f, 5f);
                PrefabUtility.RecordPrefabInstancePropertyModifications(entrance.transform);
                BuildTraining(root.transform);
                BuildRest(root.transform);
                BuildPlanting(root.transform);
                CombineStructure();
                var unit = ResidentialGym.Describe();
                var tag = root.GetComponent<LivingCity.Generation.BlockLotTag>();
                if (tag == null) tag = root.AddComponent<LivingCity.Generation.BlockLotTag>();
                tag.lotWidth = tag.lotDepth = 25f;
                ResidentialHarvest.PreparePhysics(root, 25f, 25f, 0f, unit.MaxH);
                var proxy = ResidentialTurfPrefab.BakeInto(root, 25f, 25f, unit.MaxH);
                var bounds = Bounds(root);
                if (bounds.min.x < -.01f || bounds.min.z < -.01f || bounds.max.x > 25.01f ||
                    bounds.max.z > 25.01f || bounds.min.y < -.01f || bounds.max.y > unit.MaxH + .01f)
                    throw new InvalidOperationException("Gym exceeds its catalog envelope: " + bounds);
                PrefabUtility.SaveAsPrefabAsset(root, Path);
                var catalog = AssetDatabase.LoadAssetAtPath<ResidentialTurfCatalog>(ResidentialTurfCatalog.AssetPath);
                if (catalog != null)
                {
                    catalog.ReplaceEntry("gym", proxy.CopyMasses());
                    AssetDatabase.SaveAssetIfDirty(catalog);
                }
                AssetDatabase.SaveAssets();
                Debug.Log("[Gym] Baked 25 x 25m venue; renderers=" + root.GetComponentsInChildren<Renderer>(true).Length);
                return true;
            }
            finally { geometry = null; PrefabUtility.UnloadPrefabContents(root); }
        }

        static void EnsureEntrance(GameObject root)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/Entrance.prefab") != null) return;
            Directory.CreateDirectory(Folder);
            AssetDatabase.Refresh();
            var source = new GameObject("Original gym entrance");
            try
            {
                foreach (var part in root.transform.Cast<Transform>().ToArray())
                {
                    string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(part.gameObject);
                    if (!System.IO.Path.GetFileName(path).StartsWith("SM_Bld_")) continue;
                    var b = Bounds(part.gameObject);
                    if (b.max.x > 4f || b.min.y < 1.1f || path.Contains("Fence")) continue;
                    part.SetParent(source.transform, false);
                }
                if (source.transform.childCount < 10) throw new InvalidOperationException("Original entrance modules not found");
                PrefabUtility.SaveAsPrefabAsset(source, Folder + "/Entrance.prefab");
            }
            finally { Object.DestroyImmediate(source); }
        }

        static void BuildGround()
        {
            for (int x = 0; x < 5; x++) for (int z = 0; z < 5; z++)
            {
                if (x == 2 && z == 0) GroundRect(10f, 1.2f, 5f, 3.8f);
                else if (x == 0 && z == 1)
                {
                    GroundRect(0f, 5f, 5f, 1f);
                    GroundRect(0f, 8.4f, 5f, 1.6f);
                }
                else GroundRect(x * 5f, z * 5f, 5f, 5f);
            }
            Pad(14f, 2f, 9, 9, rubber);
            Pad(14f, 14f, 9, 9, rubber);
            Pad(1.5f, 1.5f, 5, 4, rubber);
            Box("Sled track", new Vector3(9.8f, Deck + .012f, 12.5f), new Vector3(3f, .024f, 19f), turf);
            for (int i = 0; i <= 9; i++)
            {
                float z = 3.5f + i * 2;
                Box("Track distance marks", new Vector3(9.8f, Deck + .026f, z), new Vector3(2.82f, .002f, .045f), paint);
                if (i < 9) Box("Lane edge", new Vector3(8.4f, Deck + .026f, z + .9f), new Vector3(.045f, .002f, 1.72f), paint);
            }
            // Four shallow steps meet the public pavement, all within the venue footprint.
            for (int i = 0; i < 4; i++)
                Box("Entrance step", new Vector3(12.5f, (i + 1) * .05f, .16f + i * .30f),
                    new Vector3(4.8f, (i + 1) * .1f, .32f), concrete);
            // The deck recess makes room for the ramp; its sloped mesh starts at pavement level.
            Ramp();
            var rng = new System.Random(198713);
            for (int i = 0; i < 38; i++)
            {
                float x = 14.3f + (float)rng.NextDouble() * 8.2f;
                float z = (i < 19 ? 2.3f : 14.3f) + (float)rng.NextDouble() * 8.2f;
                Scuff(x, z, .12f + (float)rng.NextDouble() * .28f, rng);
            }
            // Drain channels are recessed dark strips with a small metal grate pattern.
            foreach (float x in new[] { 7.35f, 23.8f })
            {
                Box("Drain channel", new Vector3(x, Deck + .001f, 12.5f), new Vector3(.15f, .004f, 21f), steel);
                for (int i = 0; i < 84; i++)
                    Box("Drain crossbar", new Vector3(x, Deck + .005f, 2.05f + i * .25f), new Vector3(.16f, .008f, .035f), paint);
            }
        }

        static void GroundRect(float x, float z, float width, float depth)
        {
            Box("Concrete foundation", new Vector3(x + width * .5f, .175f, z + depth * .5f),
                new Vector3(width - .018f, .35f, depth - .018f), concrete);
            Box("Expansion-jointed slab", new Vector3(x + width * .5f, .375f, z + depth * .5f),
                new Vector3(width - .018f, .05f, depth - .018f),
                ((int)x * 7 + (int)z * 11) % 4 == 0 ? warmSlab : concrete);
        }

        static void Pad(float x, float z, int w, int d, Material mat)
        {
            for (int i = 0; i < w; i++) for (int j = 0; j < d; j++)
                Box("Rubber safety tile", new Vector3(x + i + .5f, Deck + .012f, z + j + .5f),
                    new Vector3(.988f, .024f, .988f), mat);
        }

        static void BuildTraining(Transform root)
        {
            var weights = Group(root, "Free weights and strength");
            Stand("SM_Prop_Gym_Bench_Press_01", weights, 16f, 4.2f, 0f, Deck + .024f);
            Stand("SM_Prop_Gym_Bench_Press_01", weights, 20.5f, 4.2f, 0f, Deck + .024f);
            Stand("SM_Prop_Gym_Squat_Rack_01", weights, 16f, 8.3f, 180f, Deck + .024f);
            Stand("SM_Prop_Gym_Leg_Press_01", weights, 20.5f, 8.1f, 90f, Deck + .024f);
            for (int i = 0; i < 3; i++) Stand("SM_Prop_Gym_Dumbbell_Rack_01", weights, 15.9f + i * 2.8f, 12f, 180f, Deck);
            foreach (var at in new[] { new Vector2(14.6f, 4.1f), new Vector2(19.1f, 4.8f), new Vector2(14.6f, 8.2f), new Vector2(22.1f, 9.2f) })
            {
                Stand("SM_Prop_Gym_Weight_Stack_01", weights, at.x, at.y, 19f, Deck + .024f);
                Stand("SM_Prop_Gym_Plate_01", weights, at.x + .42f, at.y + .36f, 12f, Deck + .024f);
            }
            var bars = Group(root, "Calisthenics and mobility");
            Stand("SM_Prop_Gym_Frame_01", bars, 16.2f, 17f, 90f, Deck + .024f);
            Stand("SM_Prop_Gym_Frame_01", bars, 20.4f, 17f, 90f, Deck + .024f);
            Stand("SM_Prop_Gym_Frame_03", bars, 16.4f, 21.1f, 0f, Deck + .024f);
            Stand("SM_Prop_Gym_Frame_04", bars, 20.7f, 21.1f, 0f, Deck + .024f);
            for (int i = 0; i < 3; i++)
                Stand("SM_Prop_Gym_Kettle_Bell_0" + (i + 1), bars, 18f + i * .48f, 14.6f, i * 27, Deck + .024f);
            var cardio = Group(root, "Warm-up and stretching");
            Stand("SM_Prop_Gym_Bike_01", cardio, 3f, 3.5f, 0f, Deck + .024f);
            Stand("SM_Prop_Gym_Bike_01", cardio, 5f, 3.5f, 0f, Deck + .024f);
            for (int i = 0; i < 3; i++)
                Box("Stretching mat", new Vector3(5.9f, Deck + .01f, 17f + i * 2.2f), new Vector3(.75f, .02f, 1.7f), turf);
        }

        static void BuildRest(Transform root)
        {
            var rest = Group(root, "Shaded recovery and entrance");
            foreach (float x in new[] { 1.3f, 6.6f }) foreach (float z in new[] { 18.5f, 24f })
                Box("Shade post", new Vector3(x, Deck + 1.5f, z), new Vector3(.14f, 3f, .14f), steel);
            foreach (float x in new[] { 1.3f, 6.6f })
                Box("Shade beam", new Vector3(x, Deck + 3.02f, 21.25f), new Vector3(.18f, .22f, 5.85f), wood);
            for (int i = 0; i < 12; i++)
                Box("Timber shade slat", new Vector3(3.95f, Deck + 3.15f, 18.55f + i * .49f), new Vector3(5.8f, .12f, .19f), wood);
            Stand("SM_Prop_Bench_Seat_01", rest, 2.3f, 21.1f, 90f, Deck);
            Stand("SM_Prop_Bench_Seat_01", rest, 5.6f, 23f, 180f, Deck);
            Stand("SM_Prop_Bench_Seat_03", rest, 5.8f, 9.7f, 90f, Deck);
            Stand("SM_Prop_Bench_Seat_03", rest, 5.8f, 15.3f, 90f, Deck);
            Stand("SM_Prop_Bench_Seat_01", rest, 6.7f, 1.4f, 180f, Deck);
            Stand("SM_Prop_Bench_Seat_01", rest, 10.3f, 23.5f, 180f, Deck);
            Stand("SM_Prop_Trash_Bin_01", rest, 6.4f, 23.5f, 0f, Deck);
            Stand("SM_Prop_Trash_Bin_01", rest, 8.6f, 1.3f, 0f, Deck);
            // Timber-backed notice board beside the open entrance, supported by steel legs.
            foreach (float x in new[] { 15.2f, 17.1f })
                Box("Notice board post", new Vector3(x, Deck + .9f, 1f), new Vector3(.06f, 1.8f, .06f), steel);
            Box("Notice board", new Vector3(16.15f, Deck + 1.25f, 1f), new Vector3(2.15f, 1.15f, .09f), wood);
            Label(rest, "IRON YARD", new Vector3(16.15f, Deck + 1.5f, .942f), .24f);
            Label(rest, "OPEN AIR TRAINING", new Vector3(16.15f, Deck + 1.17f, .942f), .105f);
            Label(rest, "RETURN YOUR WEIGHTS", new Vector3(16.15f, Deck + .92f, .942f), .078f);
        }

        static void BuildPlanting(Transform root)
        {
            var garden = Group(root, "Planted edges");
            // Low beds break up the concrete at its edges. The south steps, west ramp,
            // entrance facade and the continuous central route remain open.
            Bed(3.4f, .9f, 4.5f, .75f, 4);
            Bed(21.2f, 1f, 5f, .9f, 5);
            Bed(24.12f, 6.5f, .65f, 8f, 7);
            Bed(18f, 24.05f, 10.5f, .7f, 8);
            Bed(2.8f, 17.25f, 3.9f, 1.15f, 4);
            Bed(3f, 23.8f, 2.4f, .75f, 3);

            void Bed(float x, float z, float w, float d, int count)
            {
                const float rim = .075f, height = .34f;
                Box("Planter soil", new Vector3(x, Deck + height - .075f, z),
                    new Vector3(w - rim * 2f, .12f, d - rim * 2f), soil);
                foreach (float sign in new[] { -1f, 1f })
                {
                    Box("Planter long wall", new Vector3(x, Deck + height * .5f, z + sign * (d - rim) * .5f),
                        new Vector3(w, height, rim), concrete);
                    Box("Planter end wall", new Vector3(x + sign * (w - rim) * .5f, Deck + height * .5f, z),
                        new Vector3(rim, height, d - rim * 2f), concrete);
                }
                for (int i = 0; i < count; i++)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Palm +
                        "Environment/SM_Env_Bush_0" + (1 + i % 3) + ".prefab");
                    if (prefab == null) throw new FileNotFoundException("Gym planting bush");
                    var plant = (GameObject)PrefabUtility.InstantiatePrefab(prefab, garden);
                    plant.name = "Planter shrub " + i;
                    plant.transform.localRotation = Quaternion.Euler(0f, i * 137.5f, 0f);
                    var b = Bounds(plant);
                    float width = w > d ? (w - .18f) / count * 1.1f : w - .15f;
                    float depth = d > w ? (d - .18f) / count * 1.1f : d - .15f;
                    float fit = Mathf.Min(width / b.size.x, depth / b.size.z);
                    plant.transform.localScale = Vector3.Scale(plant.transform.localScale,
                        new Vector3(fit, (.5f + i % 3 * .1f) / b.size.y, fit));
                    float along = (i + .5f) / count - .5f;
                    Center(plant, x + (w > d ? along * (w - .15f) : 0f),
                        z + (d > w ? along * (d - .15f) : 0f), Deck + height - .02f);
                    // These low beds are read at city-camera distance. Bake LOD0 into
                    // the shared material meshes instead of culling each tiny shrub.
                    var lod = plant.GetComponentInChildren<LODGroup>();
                    var visible = lod != null ? lod.GetLODs()[0].renderers
                        : plant.GetComponentsInChildren<Renderer>();
                    foreach (var renderer in visible)
                    {
                        var source = renderer.GetComponent<MeshFilter>();
                        if (source == null || source.sharedMesh == null) continue;
                        var leaf = new GameObject("Planted foliage");
                        leaf.transform.SetParent(geometry, false);
                        leaf.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
                        leaf.transform.localScale = source.transform.lossyScale;
                        leaf.AddComponent<MeshFilter>().sharedMesh = source.sharedMesh;
                        leaf.AddComponent<MeshRenderer>().sharedMaterial = renderer.sharedMaterial;
                    }
                    Object.DestroyImmediate(plant);
                }
            }
            Object.DestroyImmediate(garden.gameObject);
        }

        static void BuildBoundary(Transform root)
        {
            var fence = Group(root, "Perimeter fence");
            for (int side = 0; side < 4; side++) for (int i = 0; i < 10; i++)
            {
                if (side == 0 && (i == 4 || i == 5)) continue;
                if (side == 3 && (i == 2 || i == 3)) continue;
                float x = side == 1 ? 24.65f : side == 3 ? .35f : 1.25f + i * 2.5f;
                float z = side == 0 ? .35f : side == 2 ? 24.65f : 1.25f + i * 2.5f;
                var panel = Stand("SM_Bld_Fence_01", fence, x, z, side % 2 == 0 ? 0f : 90f, Deck + .08f, true);
                panel.transform.localScale = Vector3.Scale(panel.transform.localScale, new Vector3(.972f, .65f, 1f));
                Center(panel, x, z, Deck + .08f);
                float px = side % 2 == 0 ? .12f + i * 2.5f : x;
                float pz = side % 2 == 0 ? z : .12f + i * 2.5f;
                Box("Fence post", new Vector3(px, Deck + .77f, pz), new Vector3(.075f, 1.54f, .075f), steel);
                PrefabUtility.RecordPrefabInstancePropertyModifications(panel.transform);
            }
        }

        static void Ramp()
        {
            // A separate approach on the west edge joins the low terrace at x=5.
            var mesh = new Mesh { name = "Gym ramp" };
            mesh.vertices = new[] { new Vector3(0,0,6), new Vector3(5,Deck,6), new Vector3(5,Deck,8.4f), new Vector3(0,0,8.4f), new Vector3(5,0,6), new Vector3(5,0,8.4f) };
            mesh.triangles = new[] { 0,2,1,0,3,2,0,1,4,3,5,2,4,1,2,4,2,5 };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            MeshPart("Step-free approach", mesh, concrete);
        }

        static void Scuff(float x, float z, float radius, System.Random rng)
        {
            var v = new Vector3[11]; var indices = new int[30];
            v[0] = new Vector3(x, Deck + .026f, z);
            for (int i = 0; i < 10; i++)
            {
                float angle = i * Mathf.PI * .2f;
                float r = radius * (.6f + (float)rng.NextDouble() * .4f);
                v[i + 1] = v[0] + new Vector3(Mathf.Cos(angle) * r, 0, Mathf.Sin(angle) * r * .65f);
                indices[i * 3] = 0; indices[i * 3 + 1] = (i + 1) % 10 + 1; indices[i * 3 + 2] = i + 1;
            }
            var mesh = new Mesh { name = "Floor wear" }; mesh.vertices = v; mesh.triangles = indices;
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); MeshPart("Wear around equipment", mesh, wear);
        }

        static Material Material(string name, Color color, string profile, float metallic = 0f)
        {
            string path = "Assets/Materials/Gym_" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(Shader.Find("LivingCity/Street Prop Surface")); AssetDatabase.CreateAsset(mat, path); }
            mat.SetColor("_BaseColor", color); mat.SetTexture("_BaseMap", null);
            mat.SetFloat("_Metallic", metallic); mat.SetFloat("_Smoothness", .16f);
            mat.SetFloat("_Prop", profile == "WOOD" ? 1f : profile == "STONE" ? 2f : profile == "PLASTIC" ? 3f : 0f);
            mat.shaderKeywords = new[] { "_PROP_" + profile }; mat.enableInstancing = true;
            EditorUtility.SetDirty(mat); return mat;
        }

        static Transform Group(Transform parent, string name)
        { var go = new GameObject(name); go.transform.SetParent(parent, false); return go.transform; }

        static void Box(string name, Vector3 at, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name;
            go.transform.SetParent(geometry, false); go.transform.localPosition = at; go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat; Object.DestroyImmediate(go.GetComponent<Collider>());
        }

        static void MeshPart(string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name); go.transform.SetParent(geometry, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        static GameObject Stand(string name, Transform parent, float x, float z, float yaw, float y, bool building = false)
        {
            string path = Palm + (building ? "Buildings/" : "Props/") + name + ".prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) throw new FileNotFoundException(path);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent); go.name = name;
            go.transform.localRotation = Quaternion.Euler(0, yaw, 0); Center(go, x, z, y);
            if (name.StartsWith("SM_Prop_Gym_"))
                foreach (var renderer in go.GetComponentsInChildren<MeshRenderer>())
                {
                    bool iron = name.Contains("Weight_Stack") || name.Contains("Plate_") ||
                                name.Contains("Kettle_Bell") || name.Contains("Dumbbell_Rack");
                    var finish = iron ? steel : AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/StreetProps_Palm_Paint.mat");
                    renderer.sharedMaterials = Enumerable.Repeat(finish, renderer.sharedMaterials.Length).ToArray();
                    PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                }
            PrefabUtility.RecordPrefabInstancePropertyModifications(go.transform); return go;
        }

        static void Center(GameObject go, float x, float z, float bottom)
        {
            var b = Bounds(go); go.transform.position += new Vector3(x - b.center.x, bottom - b.min.y, z - b.center.z);
        }

        static Bounds Bounds(GameObject go)
        {
            var rr = go.GetComponentsInChildren<Renderer>(true); if (rr.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = rr[0].bounds; foreach (var r in rr) b.Encapsulate(r.bounds); return b;
        }

        static void Label(Transform parent, string value, Vector3 at, float size)
        {
            var go = new GameObject(value); go.transform.SetParent(parent, false); go.transform.localPosition = at;
            go.transform.localRotation = Quaternion.identity;
            var text = go.AddComponent<TextMesh>(); text.text = value; text.fontSize = 64; text.characterSize = size;
            text.anchor = TextAnchor.MiddleCenter; text.alignment = TextAlignment.Center;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); text.color = new Color(.89f, .85f, .70f);
            go.GetComponent<MeshRenderer>().sharedMaterial = text.font.material;
            var bounds = go.GetComponent<MeshRenderer>().bounds;
            go.transform.localScale *= Mathf.Min(1.88f / Mathf.Max(.001f, bounds.size.x),
                                                size / Mathf.Max(.001f, bounds.size.y));
        }

        static void CombineStructure()
        {
            var filters = geometry.GetComponentsInChildren<MeshFilter>();
            foreach (var group in filters.GroupBy(f => f.GetComponent<MeshRenderer>().sharedMaterial))
            {
                string path = Folder + "/" + group.Key.name + ".asset";
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (mesh == null) { mesh = new Mesh(); AssetDatabase.CreateAsset(mesh, path); }
                mesh.Clear(); mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.CombineMeshes(group.Select(f => new CombineInstance { mesh = f.sharedMesh,
                    transform = geometry.worldToLocalMatrix * f.transform.localToWorldMatrix }).ToArray(), true, true);
                mesh.name = group.Key.name; EditorUtility.SetDirty(mesh);
                var go = new GameObject(group.Key.name); go.transform.SetParent(geometry.parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>().sharedMaterial = group.Key;
            }
            var transient = filters.Select(f => f.sharedMesh).Where(m => string.IsNullOrEmpty(AssetDatabase.GetAssetPath(m))).Distinct().ToArray();
            Object.DestroyImmediate(geometry.gameObject);
            foreach (var mesh in transient) Object.DestroyImmediate(mesh);
        }
    }
}
