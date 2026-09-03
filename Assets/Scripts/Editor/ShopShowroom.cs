using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// Every shopfront module the packs ship, one of each in a row per pack, with its
    /// name floating above it and a red tile on the pavement where its door was
    /// measured - so the storefront work can be argued over a scene rather than a
    /// table. The eight POLYGON City modules are the ones the harvested residential
    /// units are built from (Docs/residential-blocks-plan.md §0.1); their door figures
    /// come from the offline FBX measurement of 2026-09-03 and the tile is there to be
    /// checked against the drawn door, not trusted.
    ///
    /// Modules stand at yaw 180 so their +Z face looks at a camera on the south side,
    /// the way the cast and prop showrooms are laid out. The door tile is a child of the
    /// module in its own local frame (pivot at the NE corner, module filling x -5..0),
    /// so it rides the rotation and any mirror in the figures shows up as a tile beside
    /// the door rather than a wrong number in a table.
    /// </summary>
    public static class ShopShowroom
    {
        internal const string ScenePath = "Assets/Scenes/ShopDemo.unity";
        const string IndexPath = "Library/ShopCatalog.txt";
        const string RootName = "SHOPS";

        const float Gap = 4f;
        const float RowGap = 14f;
        const float LabelLift = 0.8f;

        /// <summary>Where the door of a POLYGON City module is, in the module's own frame
        /// (metres; x across the face, z out of it), and what kind of door it is.</summary>
        struct Door
        {
            public float X, Z, Yaw, Width;
            public string Note;
        }

        static readonly Dictionary<string, Door> Doors = new Dictionary<string, Door>
        {
            // "levo/desno" is as seen FROM THE STREET; X is the module's own frame
            // (pivot at the NE corner, face on +Z), so the two read opposite ways.
            ["SM_Bld_Shop_01"] = new Door { X = -2.50f, Z = 0.8f, Width = 1.7f, Note = "vrata: SREDINA, 2 krila, staklo 1.3 m" },
            ["SM_Bld_Shop_02"] = new Door { X = -4.03f, Z = 0.8f, Width = 1.25f, Note = "vrata: DESNO, 2 krila, staklo 1.05 m, prozorcic iznad" },
            ["SM_Bld_Shop_03"] = new Door { X = -5.00f, Z = 0.8f, Width = 1.9f, Note = "vrata: SREDINA, 2 krila, staklo 1.7 m (modul 10 m)" },
            ["SM_Bld_Shop_04"] = new Door { X = -2.50f, Z = 0.8f, Width = 1.2f, Note = "vrata: SREDINA, 1 krilo, staklo 1.2 m, uvucena 0.9 m" },
            ["SM_Bld_Shop_05"] = new Door { Width = 0f, Note = "NEMA VRATA - samo izlog" },
            ["SM_Bld_Shop_06"] = new Door { X = -4.35f, Z = 0.8f, Width = 1.1f, Note = "vrata: DESNO, PUNA (bez stakla), roletna u zidu" },
            ["SM_Bld_Shop_Corner_01"] = new Door { X = -0.84f, Z = 0.8f, Width = 1.3f, Note = "vrata: LEVI kraj lica, 2 krila, staklo 1.3 m" },
            ["SM_Bld_Shop_Corner_02"] = new Door { X = -0.30f, Z = -0.30f, Yaw = 45f, Width = 1.27f, Note = "vrata: NA KOSINI ugla, 1 krilo, staklo 0.9 m" },
        };

        static readonly (string title, string[] prefabs)[] Sections =
        {
            ("POLYGON CITY - moduli iz residential zgrada (vrata izmerena)", new[]
            {
                "Assets/Synty/PolygonCity/Prefabs/Buildings/SM_Bld_Shop_01.prefab",
                "Assets/Synty/PolygonCity/Prefabs/Buildings/SM_Bld_Shop_02.prefab",
                "Assets/Synty/PolygonCity/Prefabs/Buildings/SM_Bld_Shop_03.prefab",
                "Assets/Synty/PolygonCity/Prefabs/Buildings/SM_Bld_Shop_04.prefab",
                "Assets/Synty/PolygonCity/Prefabs/Buildings/SM_Bld_Shop_05.prefab",
                "Assets/Synty/PolygonCity/Prefabs/Buildings/SM_Bld_Shop_06.prefab",
                "Assets/Synty/PolygonCity/Prefabs/Buildings/SM_Bld_Shop_Corner_01.prefab",
                "Assets/Synty/PolygonCity/Prefabs/Buildings/SM_Bld_Shop_Corner_02.prefab",
            }),
            // Awnings (Shop_Cover), the concrete slab, the sign and the beach kiosks are
            // dressing or standalone stalls, not shopfronts we would ever open - out.
            ("POLYGON TOWN - radnje sa ODVOJENIM krilima vrata (_Door_L / _Door_R)", new[]
            {
                "Assets/Synty/PolygonTown/Prefabs/Buildings/SM_Bld_Shop_01.prefab",
                "Assets/Synty/PolygonTown/Prefabs/Buildings/SM_Bld_Shop_02.prefab",
                "Assets/Synty/PolygonTown/Prefabs/Buildings/SM_Bld_Shop_03.prefab",
            }),
            ("NASE KIT RADNJE - stoje same u redu (Assets/Prefabs/Residential)", new[]
            {
                "Assets/Prefabs/Residential/radnja1.prefab",
                "Assets/Prefabs/Residential/radnja2.prefab",
                "Assets/Prefabs/Residential/radnja3.prefab",
                "Assets/Prefabs/Residential/pizzapub.prefab",
                "Assets/Prefabs/Residential/pizzapub2.prefab",
            }),
        };

        [MenuItem("Tools/City/Residential/Build Shop Showroom Scene", priority = 60)]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects,
                                                    NewSceneMode.Single);
            var root = new GameObject(RootName).transform;
            var index = new StringBuilder();
            index.AppendLine("SHOP SHOWROOM - " + ScenePath);
            index.AppendLine();

            int shown = 0;
            float z = 0f;
            var marker = MarkerMaterial();
            try
            {
                foreach (var (title, prefabs) in Sections)
                {
                    index.AppendLine(title);
                    index.AppendLine(new string('=', title.Length));
                    Header(title, new Vector3(-Gap * 2f, 0f, z), root);

                    float cursor = 0f, rowDepth = 0f;
                    for (var k = 0; k < prefabs.Length; k++)
                    {
                        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabs[k]);
                        if (!prefab)
                        {
                            Debug.LogWarning($"[Shops] missing {prefabs[k]}");
                            index.AppendLine($"  (missing) {prefabs[k]}");
                            continue;
                        }
                        if (EditorUtility.DisplayCancelableProgressBar(
                                "Shop showroom", $"{title}: {prefab.name}",
                                (k + 1f) / prefabs.Length))
                            return;

                        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                        instance.transform.SetParent(root, true);
                        // face the camera on the south side
                        instance.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
                        var bounds = Footprint(instance);
                        instance.transform.position += new Vector3(
                            cursor - bounds.min.x, 0f, z - bounds.min.z);
                        bounds = Footprint(instance);

                        var note = "";
                        if (Doors.TryGetValue(prefab.name, out var door) &&
                            prefabs[k].Contains("PolygonCity"))
                        {
                            note = door.Note;
                            if (door.Width > 0f)
                                DoorTile(instance.transform, door, marker);
                        }

                        Label(prefab.name, note,
                              new Vector3(bounds.center.x, bounds.max.y + LabelLift, bounds.min.z),
                              root);
                        index.AppendLine($"  {prefab.name,-28} x {bounds.min.x,6:0.0}..{bounds.max.x,6:0.0}" +
                                         $"  {bounds.size.x,5:0.0} x {bounds.size.z,4:0.0} m" +
                                         (note.Length > 0 ? "   " + note : ""));

                        cursor += bounds.size.x + Gap;
                        rowDepth = Mathf.Max(rowDepth, bounds.size.z);
                        shown++;
                    }

                    index.AppendLine();
                    z += rowDepth + RowGap;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            Ground();
            FrameCamera();
            SetSun();

            EditorSceneManager.SaveScene(scene, ScenePath);
            System.IO.File.WriteAllText(IndexPath, index.ToString());
            Debug.Log($"[Shops] {shown} shopfronts on show in {ScenePath}.\n[Shops] index: {IndexPath}");
        }

        /// <summary>A red tile on the pavement in front of the door, in the module's own
        /// frame - it turns with the module.</summary>
        static void DoorTile(Transform module, Door door, Material paint)
        {
            var tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tile.name = "door tile";
            Object.DestroyImmediate(tile.GetComponent<Collider>());
            tile.transform.SetParent(module, false);
            tile.transform.localPosition = new Vector3(door.X, 0.04f, door.Z);
            tile.transform.localRotation = Quaternion.Euler(0f, door.Yaw, 0f);
            tile.transform.localScale = new Vector3(door.Width, 0.08f, 0.6f);
            tile.GetComponent<MeshRenderer>().sharedMaterial = paint;
        }

        static Material MarkerMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var mat = new Material(shader) { name = "Door tile" };
            mat.SetColor("_BaseColor", new Color(0.95f, 0.12f, 0.1f));
            mat.SetFloat("_Smoothness", 0.1f);
            return mat;
        }

        static Bounds Footprint(GameObject instance)
        {
            var bounds = new Bounds(instance.transform.position, Vector3.zero);
            var first = true;
            foreach (var r in instance.GetComponentsInChildren<MeshRenderer>())
            {
                if (first) { bounds = r.bounds; first = false; }
                else bounds.Encapsulate(r.bounds);
            }
            return bounds;
        }

        static Bounds SceneBounds()
        {
            var bounds = new Bounds();
            var first = true;
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude))
            {
                if (first) { bounds = r.bounds; first = false; }
                else bounds.Encapsulate(r.bounds);
            }
            return bounds;
        }

        /// <summary>The name above the roof, and under it the door note in smaller
        /// type; both narrow enough that neighbours 4 m apart do not overlap.</summary>
        static void Label(string name, string note, Vector3 position, Transform parent)
        {
            Text($"{name} label", name, 0.075f, position + new Vector3(0f, 0.55f, 0f),
                 new Color(0.85f, 0.93f, 1f), parent);
            if (note.Length > 0)
                Text($"{name} note", note, 0.045f, position, new Color(1f, 0.75f, 0.35f), parent);
        }

        static void Header(string title, Vector3 position, Transform parent)
        {
            Text($"{title} header", title, 0.12f, position + new Vector3(0f, 6f, 0f),
                 new Color(1f, 0.85f, 0.4f), parent, TextAnchor.LowerLeft, TextAlignment.Left);
        }

        static void Text(string objectName, string text, float size, Vector3 position,
                         Color colour, Transform parent,
                         TextAnchor anchor = TextAnchor.LowerCenter,
                         TextAlignment alignment = TextAlignment.Center)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(30f, 0f, 0f));

            var mesh = go.AddComponent<TextMesh>();
            mesh.text = text;
            mesh.fontSize = 48;
            mesh.characterSize = size;
            mesh.anchor = anchor;
            mesh.alignment = alignment;
            mesh.color = colour;
        }

        static void Ground()
        {
            var area = SceneBounds();
            var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            plane.name = "Ground";
            plane.transform.position = new Vector3(area.center.x, -0.02f, area.center.z);
            plane.transform.localScale = new Vector3(area.size.x / 10f + 4f, 1f,
                                                     area.size.z / 10f + 4f);
            Object.DestroyImmediate(plane.GetComponent<MeshCollider>());

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader) return;
            var mat = new Material(shader) { name = "Shop Ground" };
            mat.SetColor("_BaseColor", new Color(0.20f, 0.21f, 0.23f));
            mat.SetFloat("_Smoothness", 0.08f);
            plane.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        static void FrameCamera()
        {
            var camera = Camera.main;
            if (!camera) return;
            camera.transform.SetPositionAndRotation(
                new Vector3(24f, 9f, -22f), Quaternion.Euler(18f, 0f, 0f));
            camera.farClipPlane = 600f;
        }

        static void SetSun()
        {
            foreach (var light in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude))
            {
                if (light.type != LightType.Directional) continue;
                light.transform.rotation = Quaternion.Euler(48f, 200f, 0f);
                light.intensity = 1.2f;
                light.color = new Color(1f, 0.96f, 0.9f);
            }

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.42f, 0.44f, 0.48f);
        }
    }
}
