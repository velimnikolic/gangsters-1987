using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// The whole cast the packs ship, in one scene to look at: every character prefab
    /// laid out by pack at the south end of the field, every vehicle behind them, one
    /// instance each, each under a label carrying the code the index file names it by.
    ///
    /// This is a picking board, not a bake. The point is to walk it once and say which
    /// bodies a gangster may wear and which wheels a mob may drive - the answers feed
    /// GangCatalog.SoldierModels/LieutenantModels and CrewDemoBuilder.GangsterCars,
    /// which name prefabs by name. Nothing is generated here: the packs already ship
    /// every one of these, and the instances keep their prefab link, so the name read
    /// off a label (or out of Library/CastCatalog.txt) is the name those tables want.
    ///
    /// Rows are laid out by measured footprint, the prop showroom's way - a yacht and a
    /// moped cannot share one cell size - and everyone is turned to face south, the way
    /// the catalog camera looks and the way the labels read.
    /// </summary>
    public static class SyntyCastShowroom
    {
        internal const string ScenePath = "Assets/CastCatalog.unity";
        const string IndexPath = "Library/CastCatalog.txt";

        const string PeopleRoot = "PEOPLE";
        const string VehicleRoot = "VEHICLES";

        /// <summary>Character folders, the likeliest gangster stock first - a walk down
        /// the field starts where the answer probably is.</summary>
        static readonly (string title, string dir)[] People =
        {
            ("GANG WARFARE", "Assets/Synty/PolygonGangWarfare/Prefabs/Character"),
            ("PALM CITY", "Assets/Synty/PolygonPalmCity/Prefabs/Characters"),
            ("POLICE STATION", "Assets/Synty/PolygonPoliceStation/Prefabs/Characters"),
            ("GENERIC", "Assets/Synty/PolygonGeneric/Prefabs/Characters"),
            ("NIGHTCLUBS", "Assets/Synty/PolygonNightclubs/Prefabs/Characters"),
            ("CITY", "Assets/Synty/PolygonCity/Prefabs/Characters"),
        };

        static readonly (string title, string dir)[] Vehicles =
        {
            ("GANG WARFARE", "Assets/Synty/PolygonGangWarfare/Prefabs/Vehicles"),
            ("CITY", "Assets/Synty/PolygonCity/Prefabs/Vehicles"),
            ("POLICE STATION", "Assets/Synty/PolygonPoliceStation/Prefabs/Vehicles"),
            ("PALM CITY", "Assets/Synty/PolygonPalmCity/Prefabs/Vehicles"),
        };

        /// <summary>Not a body and not a vehicle: the modular pieces a character or a
        /// car is dressed with, which show as a floating hat or a loose bumper.</summary>
        static readonly string[] NotAnActor = { "_Attach_", "Steering_Wheel" };

        // A man is a metre wide and his name is longer than he is, so the people field
        // is laid tighter and lettered smaller than the car park behind it.
        const float PeopleRowWidth = 62f;
        const float PeopleGap = 2.4f;
        const float PeopleRowGap = 5f;
        const float VehicleRowWidth = 104f;
        const float VehicleGap = 3.5f;
        const float VehicleRowGap = 9f;
        const float SectionGap = 16f;
        const float AreaGap = 40f;   // between the last man and the first car

        // Everyone faces the camera; a car stands three-quarters on, so its nose and its
        // flank both read - which is the whole difference between a sedan and a limo.
        const float PeopleYaw = 180f;
        const float VehicleYaw = 215f;

        [MenuItem("Tools/City/Catalog/Build Cast Catalog Scene (people + cars)", priority = 24)]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects,
                                                    NewSceneMode.Single);

            var index = new StringBuilder();
            index.AppendLine("CAST CATALOG - " + ScenePath);
            index.AppendLine("Open the scene, walk the field, and name the codes that may");
            index.AppendLine("wear a gangster's face or carry a mob's guns.");
            index.AppendLine();

            var people = new GameObject(PeopleRoot).transform;
            var vehicles = new GameObject(VehicleRoot).transform;

            int men = 0, cars = 0;
            try
            {
                float z = 0f;

                index.AppendLine("PEOPLE");
                index.AppendLine("======");
                foreach (var (title, dir) in People)
                    z = DrawSection(title, dir, people, "P", ref men, z,
                                    PeopleRowWidth, PeopleGap, PeopleRowGap,
                                    PeopleYaw, labelSize: 0.030f, headerHeight: 6f, index);

                index.AppendLine();
                index.AppendLine("VEHICLES");
                index.AppendLine("========");
                z += AreaGap;
                foreach (var (title, dir) in Vehicles)
                    z = DrawSection(title, dir, vehicles, "V", ref cars, z,
                                    VehicleRowWidth, VehicleGap, VehicleRowGap,
                                    VehicleYaw, labelSize: 0.055f, headerHeight: 8f, index);
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
            Debug.Log($"[Cast] {men} people and {cars} vehicles on show in {ScenePath}." +
                      $"\n[Cast] index: {IndexPath}");
        }

        // ------------------------------------------------------------------- layout

        /// <summary>One pack's worth, in rows that wrap on measured width. Returns the z
        /// the next section starts at; <paramref name="count"/> carries the running code
        /// number across the sections, so P01 is the first man in the whole field.</summary>
        static float DrawSection(string title, string dir, Transform parent, string prefix,
                                 ref int count, float z, float rowWidth, float gap,
                                 float rowGap, float yaw, float labelSize, float headerHeight,
                                 StringBuilder index)
        {
            var prefabs = LoadFolder(dir);
            if (prefabs.Count == 0)
            {
                Debug.LogWarning($"[Cast] nothing under {dir}");
                return z;
            }

            Header(title, new Vector3(-gap * 2f, headerHeight, z), parent);

            float cursor = 0f, rowDepth = 0f;
            for (var k = 0; k < prefabs.Count; k++)
            {
                var prefab = prefabs[k];
                if (EditorUtility.DisplayCancelableProgressBar(
                        "Cast catalog", $"{title}: {prefab.name}", (k + 1f) / prefabs.Count))
                    break;

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                instance.transform.SetParent(parent, true);
                instance.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

                var bounds = Footprint(instance);
                if (bounds.size.x <= 0f && bounds.size.z <= 0f)
                {
                    Object.DestroyImmediate(instance);   // nothing to look at
                    continue;
                }

                if (cursor > 0f && cursor + bounds.size.x > rowWidth)
                {
                    cursor = 0f;
                    z += rowDepth + rowGap;
                    rowDepth = 0f;
                }

                // south-west corner of the footprint onto the cursor and the feet onto
                // the ground, so neither a pack's pivot habit nor a car's wheel wells
                // decide where the thing ends up standing
                instance.transform.position += new Vector3(
                    cursor - bounds.min.x, -bounds.min.y, z - bounds.min.z);

                count++;
                var code = prefix + count.ToString("00");
                // a barred body still stands on the board - the board is what the packs
                // ship, not what the game uses - but it says so, in red, so nobody casts
                // it again by eye (LivingCity.Gameplay.VehicleCatalog.Barred)
                var barred = LivingCity.Gameplay.VehicleCatalog.IsBarred(prefab.name);
                Label(code, code + "\n" + ShortName(prefab.name) + (barred ? "\nBARRED" : ""),
                      new Vector3(cursor, bounds.size.y + 0.35f, z), parent, labelSize, barred);
                index.AppendLine($"{code}  {prefab.name,-42} {title}" +
                                 (barred ? "   [BARRED]" : ""));

                cursor += bounds.size.x + gap;
                rowDepth = Mathf.Max(rowDepth, bounds.size.z);
            }

            return z + rowDepth + SectionGap;
        }

        static List<GameObject> LoadFolder(string dir)
        {
            var found = new List<GameObject>();
            if (!AssetDatabase.IsValidFolder(dir)) return found;

            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { dir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                // top level only - the packs keep hats, bumpers and number plates in an
                // Attachments subfolder, and none of those is somebody to cast
                if (System.IO.Path.GetDirectoryName(path).Replace('\\', '/') != dir) continue;

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (!prefab || !HasBody(prefab)) continue;
                if (System.Array.Exists(NotAnActor, mark => prefab.name.Contains(mark))) continue;
                found.Add(prefab);
            }

            found.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return found;
        }

        static bool HasBody(GameObject prefab) =>
            prefab.GetComponentInChildren<MeshRenderer>() ||
            prefab.GetComponentInChildren<SkinnedMeshRenderer>();

        /// <summary>The pack prefix off a label - the code beside it already says which
        /// pack the body came out of, and the name has to fit the metre the man stands
        /// in. The index file keeps the full name, which is what the tables ask for.</summary>
        static string ShortName(string name)
        {
            foreach (var prefix in new[] { "SM_Gen_Chr_", "SM_Chr_", "SM_Veh_", "Character_" })
                if (name.StartsWith(prefix))
                    return name.Substring(prefix.Length);
            return name;
        }

        // ---------------------------------------------------------------- the set

        /// <summary>Clothes read against the ground, not against the skybox - and a body
        /// with no floor under it reads as a body falling.</summary>
        static void Ground()
        {
            var area = SceneBounds();
            var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            plane.name = "Ground";
            plane.transform.position = new Vector3(area.center.x, -0.02f, area.center.z);
            plane.transform.localScale = new Vector3(area.size.x / 10f + 3f, 1f,
                                                     area.size.z / 10f + 3f);
            Object.DestroyImmediate(plane.GetComponent<MeshCollider>());

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader) return;
            var mat = new Material(shader) { name = "Cast Ground" };
            mat.SetColor("_BaseColor", new Color(0.20f, 0.21f, 0.23f));
            mat.SetFloat("_Smoothness", 0.08f);
            plane.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        /// <summary>Back off the first rank of people, high enough to read the labels -
        /// where the scene opens, and the view the field was laid out for.</summary>
        static void FrameCamera()
        {
            var camera = Camera.main;
            if (!camera) return;
            camera.transform.SetPositionAndRotation(
                new Vector3(PeopleRowWidth * 0.5f, 13f, -24f), Quaternion.Euler(20f, 0f, 0f));
            camera.farClipPlane = 1200f;
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

        // --------------------------------------------------------------- measuring

        static Bounds Footprint(GameObject instance)
        {
            var b = RendererBounds(instance);
            return b ?? new Bounds(instance.transform.position, Vector3.zero);
        }

        /// <summary>Only what is on show. A Synty character prefab is one rig carrying
        /// every body in its pack with all but its own switched off, so the sleeping
        /// ones must not widen the slot the waking one stands in.</summary>
        static Bounds? RendererBounds(GameObject go)
        {
            if (!go) return null;
            var bounds = new Bounds();
            var first = true;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (!(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;
                if (first) { bounds = r.bounds; first = false; }
                else bounds.Encapsulate(r.bounds);
            }
            return first ? (Bounds?)null : bounds;
        }

        static Bounds SceneBounds()
        {
            var bounds = new Bounds();
            var first = true;
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
            {
                if (!(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;
                if (first) { bounds = r.bounds; first = false; }
                else bounds.Encapsulate(r.bounds);
            }
            return bounds;
        }

        // --------------------------------------------------------------- lettering

        static void Label(string code, string text, Vector3 position, Transform parent,
                          float size, bool barred = false)
        {
            var go = new GameObject($"{code} label");
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(35f, 0f, 0f));

            var mesh = go.AddComponent<TextMesh>();
            mesh.text = text;
            mesh.fontSize = 64;
            mesh.characterSize = size;
            mesh.lineSpacing = 0.9f;
            mesh.anchor = TextAnchor.LowerLeft;
            mesh.alignment = TextAlignment.Left;
            mesh.color = barred ? new Color(1f, 0.42f, 0.36f) : new Color(0.86f, 0.92f, 1f);
        }

        static void Header(string title, Vector3 position, Transform parent)
        {
            var header = new GameObject($"{title} header");
            header.transform.SetParent(parent, false);
            header.transform.SetPositionAndRotation(position, Quaternion.Euler(35f, 0f, 0f));

            var text = header.AddComponent<TextMesh>();
            text.text = title;
            text.fontSize = 96;
            text.characterSize = 0.22f;
            text.anchor = TextAnchor.LowerRight;
            text.alignment = TextAlignment.Right;
            text.color = new Color(1f, 0.85f, 0.4f);
        }
    }
}
