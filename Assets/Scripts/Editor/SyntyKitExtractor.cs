using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// The mirrored-piece fix both bake passes share. The Synty demo scenes mirror pieces
    /// freely (half of every roof ridge, many wall runs are localScale -1 copies), and a
    /// combine matrix with negative determinant flips triangle winding - the surface then
    /// renders only from INSIDE, which on a baked building reads as "walls but no roof and
    /// no floors". The fix is the standard one: for mirrored instances, combine a copy of
    /// the source mesh with triangles reversed and normals negated, so the mirror in the
    /// matrix lands the geometry right side out again.
    /// </summary>
    internal static class SyntyBakeUtil
    {
        static readonly System.Collections.Generic.Dictionary<Mesh, Mesh> Flipped = new();

        public static Mesh MeshFor(Mesh source, Matrix4x4 transform)
        {
            if (transform.determinant >= 0f)
                return source;

            if (Flipped.TryGetValue(source, out var cached))
                return cached;

            var copy = Object.Instantiate(source);
            copy.name = source.name + " (winding-flipped)";
            for (var s = 0; s < copy.subMeshCount; s++)
            {
                var tris = copy.GetTriangles(s);
                System.Array.Reverse(tris);
                copy.SetTriangles(tris, s);
            }
            var normals = copy.normals;
            for (var i = 0; i < normals.Length; i++)
                normals[i] = -normals[i];
            copy.normals = normals;

            return Flipped[source] = copy;
        }

        /// <summary>Between bakes the cache must not leak stale editor-only meshes.</summary>
        public static void ClearCache()
        {
            foreach (var mesh in Flipped.Values)
                if (mesh) Object.DestroyImmediate(mesh);
            Flipped.Clear();
        }
    }

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
        const string GangDemo = "Assets/Synty/PolygonGangWarfare/Scenes/Demo.unity";
        const string CoffeeOverview = "Assets/Synty/PolygonCoffeeShop/Scene/Overview.unity";

        public const string KitDir = "Assets/CityKit";
        public const string BuildingsDir = KitDir + "/Buildings";
        const string MeshDir = BuildingsDir + "/Meshes";

        /// <summary>
        /// Bumped whenever the mapping below (or the bake logic) changes; stored in
        /// Version.txt beside the output so CreateAssets can skip reopening a 61 MB demo
        /// scene on every refresh. Delete the file (or bump this) to force a re-extract.
        /// </summary>
        public const int Version = 6;
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
            SyntyBakeUtil.ClearCache();
            ExtractPalmGroups();
            ExtractPoliceStation();
            ExtractGangWarehouse();
            ExtractCoffeeShop();
            SyntyBakeUtil.ClearCache();

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
        /// taken, and the interior dressing is stripped: the demo's prop groups hold ~790
        /// furniture pieces (the whole-station bake weighed 171 MB, half the kit's mesh
        /// budget) that the roofed building never shows to the iso camera. Only exterior
        /// props survive - the street marquee, facade aircons/billboards/downpipes and the
        /// roof vent/antenna dressing (positions measured against the shell bounds offline).
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
                    StripStationInterior(copy.transform);
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

        /// <summary>Prop-group children that stay in the bake: exterior dressing only.</summary>
        static readonly string[] StationExteriorProps =
        {
            "SM_Sign_Police_Station", // free-standing street marquee at the lot edge
            "SM_Prop_Aircon",         // wall-mounted units on the facades
            "SM_Prop_Billboard",
            "SM_Prop_Vents",          // roof ducting
            "SM_Prop_SatDish",
            "SM_Prop_Antenna",
            "SM_Prop_DownPipe",
            "SM_Bld_HeliPad",         // roof-deck stairs (the pad itself is architecture)
        };

        /// <summary>
        /// Deletes the interior dressing from a police-demo root copy. Architecture groups
        /// (Building/Buildings - walls, roof, helipad, the garage's painted parking lines)
        /// pass through untouched; the garage's static Vehicles group goes entirely; the
        /// three prop groups keep only the exterior families above.
        /// </summary>
        static void StripStationInterior(Transform root)
        {
            for (var i = root.childCount - 1; i >= 0; i--)
            {
                var group = root.GetChild(i);
                switch (group.name)
                {
                    case "Vehicles":
                        Object.DestroyImmediate(group.gameObject);
                        break;
                    case "SmallProps":
                    case "LargeProps":
                    case "Props":
                        for (var j = group.childCount - 1; j >= 0; j--)
                        {
                            var prop = group.GetChild(j);
                            if (!StationExteriorProps.Any(prefix => prop.name.StartsWith(prefix)))
                                Object.DestroyImmediate(prop.gameObject);
                        }
                        break;
                }
            }
        }

        /// <summary>
        /// The GangWarfare warehouse compound (the stovariste): the demo is ONE flat Scene
        /// root of ~2550 pieces plus a separate Roof root (lifted so demo players can see
        /// the drug lab inside) - there are no Building/Props groups to filter by, so the
        /// interior is cut geometrically instead: see StripWarehouseInterior. Baked at
        /// yaw 0 - interior doors vastly outnumber the gate, so the door heuristic would
        /// misread; the front is judged in the catalog and corrected here if wrong.
        /// </summary>
        static void ExtractGangWarehouse()
        {
            var scene = EditorSceneManager.OpenScene(GangDemo, OpenSceneMode.Additive);
            try
            {
                var holder = new GameObject("building-warehouse");
                foreach (var rootName in new[] { "Scene", "Roof" })
                {
                    var root = FindSceneRoot(scene, rootName);
                    if (!root)
                    {
                        Debug.LogWarning($"SyntyKitExtractor: gang demo root '{rootName}' not found");
                        continue;
                    }

                    var copy = Object.Instantiate(root);
                    copy.name = rootName;
                    copy.transform.SetParent(holder.transform, worldPositionStays: true);
                    copy.transform.position = root.transform.position;
                    copy.transform.rotation = root.transform.rotation;
                }

                StripWarehouseInterior(holder.transform);
                BakeGroup(holder, "building-warehouse", yaw: 0f);
                Object.DestroyImmediate(holder);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, removeScene: true);
            }
        }

        /// <summary>
        /// The coffee shop pack ships ONE assembled building and no demo street: Overview
        /// holds the SM_Bld_Cafe_01 instance at identity with the front already on +Z (the
        /// facade sign sits at z=+3.6 and the overview camera parks on that side), so the
        /// door heuristic is skipped - its four doors would outvote the entrance. The
        /// tables child is interior dressing and stays behind; doors and both signs are
        /// facade and ride along.
        /// </summary>
        static void ExtractCoffeeShop()
        {
            var scene = EditorSceneManager.OpenScene(CoffeeOverview, OpenSceneMode.Additive);
            try
            {
                var root = FindSceneRoot(scene, "SM_Bld_Cafe_01");
                if (!root)
                {
                    Debug.LogWarning("SyntyKitExtractor: coffee overview root 'SM_Bld_Cafe_01' not found");
                    return;
                }

                var copy = Object.Instantiate(root);
                copy.name = "building-coffeeshop";
                copy.transform.position = root.transform.position;
                copy.transform.rotation = root.transform.rotation;

                for (var i = copy.transform.childCount - 1; i >= 0; i--)
                {
                    var child = copy.transform.GetChild(i);
                    if (child.name.StartsWith("SM_Bld_Cafe_Tables"))
                        Object.DestroyImmediate(child.gameObject);
                }

                BakeGroup(copy, "building-coffeeshop", yaw: 0f);
                Object.DestroyImmediate(copy);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, removeScene: true);
            }
        }

        /// <summary>
        /// Cuts the warehouse interior from the flat compound: the ROOF defines indoors.
        /// Every roof/ceiling piece contributes its XZ footprint; a dressing piece whose
        /// centre stands under one of those footprints with its base well below that
        /// roof's top is inside a hall and goes (lab tanks, money stacks, pallets, the
        /// shooting range rails, hanging lights). Yard dressing - containers, barrels,
        /// fences, gates, anything under open sky - stays, which is what makes the lot
        /// read as a stockyard. Architecture (SM_Bld_*: walls, floors, trusses, fences,
        /// the roofs themselves) always stays; vehicles (SM_Veh_*: forklifts, vans) always
        /// go, parked outside or not. Roof-mounted props survive the height test (their
        /// base sits AT the roof top, not below it).
        /// </summary>
        static void StripWarehouseInterior(Transform holder)
        {
            var roofs = new List<(Rect footprint, float top)>();
            foreach (Transform root in holder)
                foreach (Transform piece in root)
                {
                    if (!piece.name.StartsWith("SM_Bld_Roof") &&
                        !piece.name.StartsWith("SM_Bld_Ceiling") &&
                        !piece.name.StartsWith("SM_Bld_Floor_Ceiling"))
                        continue;
                    if (TryPieceBounds(piece, out var b))
                        roofs.Add((new Rect(b.min.x, b.min.z, b.size.x, b.size.z), b.max.y));
                }

            foreach (Transform root in holder)
                for (var i = root.childCount - 1; i >= 0; i--)
                {
                    var piece = root.GetChild(i);
                    if (piece.name.StartsWith("SM_Veh"))
                    {
                        Object.DestroyImmediate(piece.gameObject);
                        continue;
                    }
                    if (piece.name.StartsWith("SM_Bld"))
                        continue;

                    if (!TryPieceBounds(piece, out var b))
                        continue;
                    var centre = new Vector2(b.center.x, b.center.z);
                    if (roofs.Any(r => r.footprint.Contains(centre) && b.min.y < r.top - 1f))
                        Object.DestroyImmediate(piece.gameObject);
                }
        }

        static bool TryPieceBounds(Transform piece, out Bounds bounds)
        {
            bounds = default;
            var first = true;
            foreach (var r in piece.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (first) { bounds = r.bounds; first = false; }
                else bounds.Encapsulate(r.bounds);
            }
            return !first;
        }

        static void ExtractGroup(Scene scene, Entry entry)
        {
            var group = FindByPath(scene, entry.groupPath);
            if (!group)
            {
                Debug.LogWarning($"SyntyKitExtractor: '{entry.groupPath}' not found in {entry.scene}");
                return;
            }

            // The building SHELL only. A demo group is Building + Dressing + Decals (+
            // Vehicles), and v1 baked the lot - interior furniture, shop stock, graffiti -
            // into meshes of 17-51 MB per building. A city of hundreds of those exhausted
            // the graphics ring buffer and read as "the blocks are gone": placed, unpaid
            // for, undrawn. The Building subtree is the architecture; the dressing was demo
            // set-decoration.
            var shell = group.Find("Building");
            var source = shell ? shell.gameObject : group.gameObject;

            var copy = Object.Instantiate(source);
            copy.name = entry.role;
            copy.transform.position = shell ? shell.position : group.position;
            copy.transform.rotation = shell ? shell.rotation : group.rotation;

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
        /// <returns>The world-space pivot the bake recentred on (footprint centre at the
        /// group's ground height), or null when the group had nothing to bake - lets a
        /// caller reassemble several bakes at their authored relative offsets.</returns>
        internal static Vector3? BakeGroup(GameObject group, string role, float yaw,
                                           string outputDir = BuildingsDir, string meshOutputDir = MeshDir)
        {
            foreach (var mb in group.GetComponentsInChildren<MonoBehaviour>(true))
                if (mb) Object.DestroyImmediate(mb);
            foreach (var light in group.GetComponentsInChildren<Light>(true))
                if (light) Object.DestroyImmediate(light.gameObject);
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(group);

            // Rotate the front onto +Z around the group origin, then recentre on the
            // footprint. Renderer bounds are world-space, so measure after the yaw.
            group.transform.rotation = Quaternion.Euler(0f, -yaw, 0f) * group.transform.rotation;

            // Active geometry only: the Synty prefabs carry disabled alternates (glass
            // inserts, part variants) that v1 baked in on top of the visible set.
            var renderers = group.GetComponentsInChildren<MeshRenderer>(false)
                .Where(r => r.enabled && r.gameObject.activeInHierarchy
                            && r.GetComponent<MeshFilter>() && r.GetComponent<MeshFilter>().sharedMesh)
                .ToArray();
            if (renderers.Length == 0)
            {
                Debug.LogWarning($"SyntyKitExtractor: '{role}' has no mesh renderers, skipped");
                return null;
            }

            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            var pivot = new Vector3(bounds.center.x, group.transform.position.y, bounds.center.z);

            // Per-material combine, vertices baked in pivot space. Mirrored instances get
            // the winding-flipped source copy - see SyntyBakeUtil.
            var byMaterial = new Dictionary<Material, List<CombineInstance>>();
            var toPivot = Matrix4x4.TRS(pivot, Quaternion.identity, Vector3.one).inverse;
            foreach (var r in renderers)
            {
                var filter = r.GetComponent<MeshFilter>();
                var mats = r.sharedMaterials;
                var matrix = toPivot * r.localToWorldMatrix;
                var mesh = SyntyBakeUtil.MeshFor(filter.sharedMesh, matrix);
                for (int s = 0; s < mesh.subMeshCount && s < mats.Length; s++)
                {
                    if (!mats[s]) continue;
                    if (!byMaterial.TryGetValue(mats[s], out var list))
                        byMaterial[mats[s]] = list = new List<CombineInstance>();
                    list.Add(new CombineInstance
                    {
                        mesh = mesh,
                        subMeshIndex = s,
                        transform = matrix,
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

            var meshPath = $"{meshOutputDir}/{role}.asset";
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

            var prefabPath = $"{outputDir}/{role}.prefab";
            PrefabUtility.SaveAsPrefabAsset(output, prefabPath);
            Object.DestroyImmediate(output);

            var size = bounds.size;
            Debug.Log($"SyntyKitExtractor: baked '{role}' {size.x:F1} x {size.y:F1} x {size.z:F1} m, " +
                      $"{perMaterial.Count} material(s), front yaw {yaw}deg");
            return pivot;
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
