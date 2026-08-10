using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// Lifts the assembled buildings out of the Synty demo scenes into project-owned prefabs
    /// under Assets/CityKit/Buildings, named with the city's existing vocabulary
    /// (building-bank, building-cafe, ...) so the ~15 files that match prefab names at
    /// runtime never notice the pack swap.
    ///
    /// The Synty packs ship buildings only as modular kits; the demo scenes are the one
    /// place assembled buildings exist (PalmCity Demo: 24 groups under City/, all scale 1;
    /// PoliceStation Demo: the whole station as BaseFloor/TopFloor/Garage roots). Extraction
    /// therefore means: copy the group, normalise its pivot (footprint centre at origin,
    /// front facade on +Z), and BAKE the pieces into one mesh per material - an extracted
    /// apartment is 400-900 prefab instances, and a city places hundreds of buildings, so
    /// live nested instances would put six figures of GameObjects in the scene. polyperfect
    /// buildings were one mesh each; the bake keeps that cost model.
    ///
    /// Front detection: at identity rotation every Synty piece faces +Z (verified across the
    /// Overview scenes - 1117 prefabs, all rotation zero). A building's street side is where
    /// its doors are, so the group is yawed by the majority orientation of its door-carrying
    /// wall pieces, rounded to 90. Entries can override the result by hand where the
    /// heuristic misreads; overrides win over measurement, per the log.
    /// </summary>
    public static class SyntyKitExtractor
    {
        const string PalmDemo = "Assets/Synty/PolygonPalmCity/Scenes/Demo.unity";
        const string PoliceDemo = "Assets/Synty/PolygonPoliceStation/Scenes/Demo.unity";

        public const string KitDir = "Assets/CityKit";
        public const string BuildingsDir = KitDir + "/Buildings";
        const string MeshDir = BuildingsDir + "/Meshes";

        /// <summary>
        /// Bumped whenever the mapping below (or the bake logic) changes; stored in
        /// Version.txt beside the output so CreateAssets can skip reopening a 61 MB demo
        /// scene on every refresh. Delete the file (or bump this) to force a re-extract.
        /// </summary>
        public const int Version = 1;
        const string VersionPath = BuildingsDir + "/Version.txt";

        /// <summary>demo group -> city role. yawOverride in degrees, NaN = trust the doors.</summary>
        struct Entry
        {
            public string scene;      // PalmDemo or PoliceDemo
            public string groupPath;  // path of the group under the scene root, e.g. "City/Diner"
            public string role;       // output prefab name (the city's existing vocabulary)
            public float yawOverride; // NaN = measure from door pieces

            public Entry(string scene, string groupPath, string role, float yawOverride = float.NaN)
            {
                this.scene = scene;
                this.groupPath = groupPath;
                this.role = role;
                this.yawOverride = yawOverride;
            }
        }

        /// <summary>
        /// The role mapping. Residential/commercial stock keeps Synty-ish names (they enter
        /// palettes as lists, nothing matches them by name); everything a marker or director
        /// finds BY NAME gets the exact legacy name.
        /// </summary>
        static readonly Entry[] Entries =
        {
            // Named landmarks and storefronts (name-matched at runtime - exact strings).
            new(PalmDemo, "City/CarYard", "building-carwash"),
            new(PalmDemo, "City/Hotel_01", "building-casino"),
            new(PalmDemo, "City/Restaurant_01", "building-restaurant"),
            new(PalmDemo, "City/Restaurant_02", "building-cafe"),
            new(PalmDemo, "City/Diner", "building-burger-joint"),
            new(PalmDemo, "City/Store_02", "building-post"),
            new(PalmDemo, "City/Store_03", "building-shop-china"),
            new(PalmDemo, "City/Gym", "building-school"),
            new(PalmDemo, "City/ParkingGarage", "building-parking-garage"),

            // Residential / commercial stock for the palettes.
            new(PalmDemo, "City/Apartment_01", "building-apartment-01"),
            new(PalmDemo, "City/Apartment_02", "building-apartment-02"),
            new(PalmDemo, "City/Apartment_03", "building-apartment-03"),
            new(PalmDemo, "City/Hotel_02", "building-hotel-small"),
            new(PalmDemo, "City/Skyscraper_01", "building-tower-01"),
            new(PalmDemo, "City/Skyscraper_02", "building-tower-02"),
            new(PalmDemo, "City/Skyscraper_03", "building-tower-03"),
            new(PalmDemo, "City/Store_01", "building-store-01"),
            new(PalmDemo, "City/Toilet", "building-park-toilet"),
        };

        [MenuItem("Tools/City/Rebuild Synty City Kit (Buildings)")]
        public static void ForceExtract()
        {
            AssetDatabase.DeleteAsset(VersionPath);
            ExtractIfStale();
        }

        /// <summary>Runs the extraction unless Version.txt already records this Version.</summary>
        public static void ExtractIfStale()
        {
            var marker = AssetDatabase.LoadAssetAtPath<TextAsset>(VersionPath);
            if (marker && marker.text.Trim() == Version.ToString())
                return;

            EnsureFolders();
            ExtractPalmGroups();
            ExtractPoliceStation();

            System.IO.File.WriteAllText(VersionPath, Version.ToString());
            AssetDatabase.ImportAsset(VersionPath);
            AssetDatabase.SaveAssets();
        }

        static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder(KitDir))
                AssetDatabase.CreateFolder("Assets", "CityKit");
            if (!AssetDatabase.IsValidFolder(BuildingsDir))
                AssetDatabase.CreateFolder(KitDir, "Buildings");
            if (!AssetDatabase.IsValidFolder(MeshDir))
                AssetDatabase.CreateFolder(BuildingsDir, "Meshes");
        }

        static void ExtractPalmGroups()
        {
            var scene = EditorSceneManager.OpenScene(PalmDemo, OpenSceneMode.Additive);
            try
            {
                foreach (var entry in Entries.Where(e => e.scene == PalmDemo))
                    ExtractGroup(scene, entry);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, removeScene: true);
            }
        }

        /// <summary>
        /// The station is split across three roots with different parent offsets; they are
        /// re-parented under one pivot preserving world positions, then baked like any other
        /// group. Street slab, sky backdrop and the demo lighting rigs are deliberately not
        /// taken. The result is the one dressed-interior landmark in the city.
        /// </summary>
        static void ExtractPoliceStation()
        {
            var scene = EditorSceneManager.OpenScene(PoliceDemo, OpenSceneMode.Additive);
            try
            {
                var holder = new GameObject("building-policestation");
                foreach (var rootName in new[] { "BaseFloor", "TopFloor", "Garage" })
                {
                    var root = FindSceneRoot(scene, rootName);
                    if (!root)
                    {
                        Debug.LogWarning($"SyntyKitExtractor: police demo root '{rootName}' not found");
                        continue;
                    }

                    var copy = Object.Instantiate(root);
                    copy.name = rootName;
                    copy.transform.SetParent(holder.transform, worldPositionStays: true);
                    copy.transform.position = root.transform.position;
                    copy.transform.rotation = root.transform.rotation;
                }

                // The station entrance faces +Z in the demo's own frame (entrance wall pieces
                // sit on the BaseFloor's south edge looking at the forecourt street). Doors
                // inside the building outnumber the entrance, so the heuristic is skipped.
                BakeGroup(holder, "building-policestation", yaw: 0f);
                Object.DestroyImmediate(holder);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, removeScene: true);
            }
        }

        static void ExtractGroup(Scene scene, Entry entry)
        {
            var group = FindByPath(scene, entry.groupPath);
            if (!group)
            {
                Debug.LogWarning($"SyntyKitExtractor: '{entry.groupPath}' not found in {entry.scene}");
                return;
            }

            var copy = Object.Instantiate(group.gameObject);
            copy.name = entry.role;
            copy.transform.position = group.position;
            copy.transform.rotation = group.rotation;

            var yaw = float.IsNaN(entry.yawOverride) ? MeasureFrontYaw(copy) : entry.yawOverride;
            BakeGroup(copy, entry.role, yaw);
            Object.DestroyImmediate(copy);
        }

        /// <summary>
        /// Majority yaw of door-carrying wall pieces, rounded to 90. Returns the rotation that
        /// brings that side onto +Z (i.e. the group should be yawed by MINUS this).
        /// </summary>
        static float MeasureFrontYaw(GameObject group)
        {
            var votes = new Dictionary<int, int>();
            foreach (var t in group.GetComponentsInChildren<Transform>(true))
            {
                var n = t.gameObject.name;
                if (!n.StartsWith("SM_Bld")) continue;
                if (!n.Contains("_Wall_Door") && !n.Contains("Wall_Entrance") &&
                    !n.Contains("_Deco_Wall_Door") && !n.Contains("Door_Glass") &&
                    !n.Contains("Door_Folding"))
                    continue;

                var yaw = Mathf.RoundToInt(Mathf.Repeat(t.eulerAngles.y, 360f) / 90f) * 90 % 360;
                votes.TryGetValue(yaw, out var c);
                votes[yaw] = c + 1;
            }

            if (votes.Count == 0)
            {
                Debug.LogWarning($"SyntyKitExtractor: no door pieces in '{group.name}', assuming front = +Z");
                return 0f;
            }

            var best = votes.OrderByDescending(kv => kv.Value).First();
            Debug.Log($"SyntyKitExtractor: '{group.name}' door yaw votes " +
                      string.Join(", ", votes.Select(kv => $"{kv.Key}deg x{kv.Value}")) +
                      $" -> front at {best.Key}deg");
            return best.Key;
        }

        /// <summary>
        /// Turns a live group into a saved prefab: one combined mesh per material (plus a
        /// footprint BoxCollider), pivot at footprint centre, ground at y=0 preserved as
        /// authored, front yawed onto +Z. Decal projectors survive as live children; every
        /// MonoBehaviour and Light is stripped (the demo dressing is geometry, the behaviour
        /// is ours).
        /// </summary>
        internal static void BakeGroup(GameObject group, string role, float yaw)
        {
            foreach (var mb in group.GetComponentsInChildren<MonoBehaviour>(true))
                if (mb) Object.DestroyImmediate(mb);
            foreach (var light in group.GetComponentsInChildren<Light>(true))
                if (light) Object.DestroyImmediate(light.gameObject);
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(group);

            // Rotate the front onto +Z around the group origin, then recentre on the
            // footprint. Renderer bounds are world-space, so measure after the yaw.
            group.transform.rotation = Quaternion.Euler(0f, -yaw, 0f) * group.transform.rotation;

            var renderers = group.GetComponentsInChildren<MeshRenderer>(true)
                .Where(r => r.GetComponent<MeshFilter>() && r.GetComponent<MeshFilter>().sharedMesh)
                .ToArray();
            if (renderers.Length == 0)
            {
                Debug.LogWarning($"SyntyKitExtractor: '{role}' has no mesh renderers, skipped");
                return;
            }

            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            var pivot = new Vector3(bounds.center.x, group.transform.position.y, bounds.center.z);

            // Per-material combine, vertices baked in pivot space.
            var byMaterial = new Dictionary<Material, List<CombineInstance>>();
            var toPivot = Matrix4x4.TRS(pivot, Quaternion.identity, Vector3.one).inverse;
            foreach (var r in renderers)
            {
                var filter = r.GetComponent<MeshFilter>();
                var mats = r.sharedMaterials;
                for (int s = 0; s < filter.sharedMesh.subMeshCount && s < mats.Length; s++)
                {
                    if (!mats[s]) continue;
                    if (!byMaterial.TryGetValue(mats[s], out var list))
                        byMaterial[mats[s]] = list = new List<CombineInstance>();
                    list.Add(new CombineInstance
                    {
                        mesh = filter.sharedMesh,
                        subMeshIndex = s,
                        transform = toPivot * r.localToWorldMatrix,
                    });
                }
            }

            var perMaterial = new List<(Material mat, Mesh mesh)>();
            foreach (var kv in byMaterial)
            {
                var m = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                m.CombineMeshes(kv.Value.ToArray(), mergeSubMeshes: true, useMatrices: true);
                perMaterial.Add((kv.Key, m));
            }

            var final = new Mesh
            {
                name = role,
                indexFormat = UnityEngine.Rendering.IndexFormat.UInt32,
            };
            final.CombineMeshes(
                perMaterial.Select(pm => new CombineInstance { mesh = pm.mesh, transform = Matrix4x4.identity })
                           .ToArray(),
                mergeSubMeshes: false, useMatrices: true);
            final.RecalculateBounds();

            var meshPath = $"{MeshDir}/{role}.asset";
            var existingMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (existingMesh)
            {
                existingMesh.Clear();
                EditorUtility.CopySerialized(final, existingMesh);
                existingMesh.name = role;
            }
            else
            {
                AssetDatabase.CreateAsset(final, meshPath);
                existingMesh = final;
            }

            // Assembled output object: combined mesh + footprint collider (+ decals kept live).
            var output = new GameObject(role);
            var mf = output.AddComponent<MeshFilter>();
            mf.sharedMesh = existingMesh;
            var mr = output.AddComponent<MeshRenderer>();
            mr.sharedMaterials = perMaterial.Select(pm => pm.mat).ToArray();

            var box = output.AddComponent<BoxCollider>();
            box.center = bounds.center - pivot;
            box.size = bounds.size;

            foreach (var decal in group.GetComponentsInChildren<UnityEngine.Rendering.Universal.DecalProjector>(true))
            {
                var d = Object.Instantiate(decal.gameObject, output.transform, worldPositionStays: true);
                d.transform.position = decal.transform.position - pivot;
                d.transform.rotation = decal.transform.rotation;
                d.name = decal.gameObject.name;
            }

            var prefabPath = $"{BuildingsDir}/{role}.prefab";
            PrefabUtility.SaveAsPrefabAsset(output, prefabPath);
            Object.DestroyImmediate(output);

            var size = bounds.size;
            Debug.Log($"SyntyKitExtractor: baked '{role}' {size.x:F1} x {size.y:F1} x {size.z:F1} m, " +
                      $"{perMaterial.Count} material(s), front yaw {yaw}deg");
        }

        static Transform FindByPath(Scene scene, string path)
        {
            var slash = path.IndexOf('/');
            var rootName = slash < 0 ? path : path.Substring(0, slash);
            var root = FindSceneRoot(scene, rootName);
            if (!root) return null;
            return slash < 0 ? root.transform : root.transform.Find(path.Substring(slash + 1));
        }

        static GameObject FindSceneRoot(Scene scene, string name)
            => scene.GetRootGameObjects().FirstOrDefault(go => go.name == name);
    }
}
