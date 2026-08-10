using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using CityTile = LivingCity.City.Tile;
using CityPath = LivingCity.City.Path;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// Builds the composite road tiles: the polyperfect tile's LOGIC (Tile component, Path
    /// children - the entire car and pedestrian nav graph - crosswalk triggers, traffic
    /// light nodes) with Synty VISUALS, saved under Assets/CityKit/Tiles under the same
    /// names the bootstrap already loads.
    ///
    /// Frame arithmetic: a tile is authored 30 units wide and the generator scales every
    /// instance by CityGrid.TileScale (1.56). Synty pieces are metric with a 2.5/5/10 m
    /// module, and 46.8 m is not a multiple of 10 - but 45 m is (4 x Road_01 + 1 half), so
    /// the visual subtree lays 45 m of content under a child scaled 30/45: net world scale
    /// 1.04, slightly larger than authored, never below it. Lane geometry stays owned by
    /// the Path nodes; at these sizes the visual kerb lands at 3.2 authored units on a
    /// street (paths walk at 4) and the RoadSurface analytic bands still cover the drawn
    /// asphalt within their kerb margin.
    ///
    /// Arms are not read from file names: each source tile's Road paths are inspected and
    /// an endpoint within 0.7 of a tile edge is that edge's connection, main if any lane
    /// offset exceeds 3. The same analytic region test then drives both the asphalt fill
    /// and the sidewalk ring, which is what lets one layouter handle straights, junctions,
    /// the curve and the dead end. Everything is baked to one mesh per tile (per-material
    /// submeshes) - the polyperfect tiles were single meshes and a 34x33 city places
    /// hundreds of these.
    /// </summary>
    public static class SyntyRoadTiles
    {
        const string SourceRoads = "Assets/polyperfect/Low Poly Epic City/T/- Prefabs_T/Tiles_T/Roads_T/";
        const string SourceTiles = "Assets/polyperfect/Low Poly Epic City/T/- Prefabs_T/Tiles_T/";

        public const string TilesDir = SyntyKitExtractor.KitDir + "/Tiles";
        const string MeshDir = TilesDir + "/Meshes";

        public const int Version = 1;
        const string VersionPath = TilesDir + "/Version.txt";

        // Synty pieces. Sizes verified offline against the binary FBX (corner pivots).
        const string Road10 = "Assets/Synty/PolygonGeneric/Prefabs/Environment/SM_Gen_Env_Road_01.prefab";          // 10 x 10
        const string Road10Half = "Assets/Synty/PolygonGeneric/Prefabs/Environment/SM_Gen_Env_Road_Half_02.prefab"; // 10 w x 5 l
        const string Crossing = "Assets/Synty/PolygonGeneric/Prefabs/Environment/SM_Gen_Env_Road_Crossing_01.prefab"; // 5 x 5
        const string Asphalt5 = "Assets/Synty/PolygonPoliceStation/Prefabs/Environment/SM_Env_Ground_Small_01.prefab"; // 5 x 5 plain
        const string Walk = "Assets/Synty/PolygonPalmCity/Prefabs/Environment/SM_Env_Sidewalk_01.prefab";           // 2.5
        const string Kerb = "Assets/Synty/PolygonPalmCity/Prefabs/Environment/SM_Env_Sidewalk_Edge_01.prefab";      // 2.5, drop on one edge

        /// <summary>Visual content spans 45 local metres over the 30-unit authored tile.</summary>
        const float Span = 45f;
        const float Half = Span / 2f;                    // 22.5
        const float VisualScale = 30f / Span;            // 2/3
        const float MinorHalfWidth = 5f;                 // 10 m road
        const float MainHalfWidth = 10f;                 // two 10 m carriageways, butted

        struct Arm
        {
            public Vector2 dir;      // unit axis direction in tile local xz
            public float halfWidth;  // metres, local visual frame
        }

        [MenuItem("Tools/City/Rebuild Synty City Kit (Road Tiles)")]
        public static void ForceBuild()
        {
            AssetDatabase.DeleteAsset(VersionPath);
            BuildIfStale();
        }

        public static void BuildIfStale()
        {
            var marker = AssetDatabase.LoadAssetAtPath<TextAsset>(VersionPath);
            if (marker && marker.text.Trim() == Version.ToString())
                return;

            EnsureFolders();

            var roadTiles = new[]
            {
                "tile-road-straight", "tile-road-curve", "tile-road-intersection-t",
                "tile-road-intersection", "tile-road-end", "tile-road-straight-crosswalk",
                "tile-mainroad-straight", "tile-mainroad-straight-crosswalk",
                "tile-road-mainroad-intersection", "tile-road-mainroad-intersection-t",
                "tile-mainroad-intersection",
            };
            foreach (var name in roadTiles)
                BuildRoadTile(name);

            BuildGroundTile("tile-plain_concrete");

            // The plain surfaces the palettes mix into yards and the park lawn. No Synty
            // pack ships a flat ground tile for these, so they are generated quads carrying
            // a Synty material - the low-poly look is a flat colour anyway.
            BuildMaterialTile("tile-plain_asphalt", "Assets/Synty/PolygonPalmCity/Materials/Buildings/Road_Grey_01.mat");
            BuildMaterialTile("tile-plain_dirt", "Assets/Synty/PolygonGeneric/Materials/Generic_Dirt.mat");
            BuildMaterialTile("tile-plain_grass", "Assets/Synty/PolygonGeneric/Materials/Generic_Grass.mat");

            System.IO.File.WriteAllText(VersionPath, Version.ToString());
            AssetDatabase.ImportAsset(VersionPath);
            AssetDatabase.SaveAssets();
        }

        static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder(SyntyKitExtractor.KitDir))
                AssetDatabase.CreateFolder("Assets", "CityKit");
            if (!AssetDatabase.IsValidFolder(TilesDir))
                AssetDatabase.CreateFolder(SyntyKitExtractor.KitDir, "Tiles");
            if (!AssetDatabase.IsValidFolder(MeshDir))
                AssetDatabase.CreateFolder(TilesDir, "Meshes");
        }

        static void BuildRoadTile(string name)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourceRoads + name + ".prefab");
            if (!source)
            {
                Debug.LogWarning($"SyntyRoadTiles: source '{name}' not found, skipped");
                return;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            instance.name = name;
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            instance.transform.localScale = Vector3.one;

            var arms = MeasureArms(instance);
            StripVisuals(instance);

            // Lay the Synty pieces in a metric staging root, bake, then parent the single
            // combined mesh under the logic copy at the 30/45 conversion scale.
            var staging = new GameObject("staging");
            try
            {
                LayAsphalt(staging.transform, name, arms);
                LaySidewalk(staging.transform, arms);

                var visual = new GameObject("SyntyVisual");
                visual.transform.SetParent(instance.transform, false);
                visual.transform.localScale = Vector3.one * VisualScale;

                BakeInto(staging, visual, name);

                var ground = visual.AddComponent<BoxCollider>();
                ground.center = new Vector3(0f, -0.05f, 0f);
                ground.size = new Vector3(Span, 0.1f, Span);

                PrefabUtility.SaveAsPrefabAsset(instance, $"{TilesDir}/{name}.prefab");
                Debug.Log($"SyntyRoadTiles: baked '{name}' ({arms.Count} arm(s))");
            }
            finally
            {
                Object.DestroyImmediate(staging);
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>
        /// The plain ground slab GroundPlacer scales per cell: pure visual, no logic - the
        /// polyperfect original is a bare mesh too. A full 18x18 sidewalk fill, one mesh.
        /// </summary>
        static void BuildGroundTile(string name)
        {
            var root = new GameObject(name);
            var staging = new GameObject("staging");
            try
            {
                for (var x = 0; x < 18; x++)
                    for (var z = 0; z < 18; z++)
                        PlaceRect(staging.transform, Walk, -Half + x * 2.5f, -Half + z * 2.5f, 0f);

                var visual = new GameObject("SyntyVisual");
                visual.transform.SetParent(root.transform, false);
                visual.transform.localScale = Vector3.one * VisualScale;
                BakeInto(staging, visual, name);

                PrefabUtility.SaveAsPrefabAsset(root, $"{TilesDir}/{name}.prefab");
                Debug.Log($"SyntyRoadTiles: baked '{name}'");
            }
            finally
            {
                Object.DestroyImmediate(staging);
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// A 30-unit ground tile as a single generated quad with a Synty material. Same
        /// authored-frame contract as every other tile here (content spans 45 local metres
        /// under the 30/45 child), no logic components - the polyperfect plain tiles were
        /// bare meshes too.
        /// </summary>
        static void BuildMaterialTile(string name, string materialPath)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (!material)
            {
                Debug.LogWarning($"SyntyRoadTiles: material '{materialPath}' not found, '{name}' skipped");
                return;
            }

            var mesh = new Mesh { name = name };
            mesh.vertices = new[]
            {
                new Vector3(-Half, 0f, -Half), new Vector3(Half, 0f, -Half),
                new Vector3(Half, 0f, Half), new Vector3(-Half, 0f, Half),
            };
            mesh.uv = new[] { Vector2.zero, new Vector2(18f, 0f), new Vector2(18f, 18f), new Vector2(0f, 18f) };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateBounds();

            var meshPath = $"{MeshDir}/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (existing)
            {
                existing.Clear();
                EditorUtility.CopySerialized(mesh, existing);
                existing.name = name;
            }
            else
            {
                AssetDatabase.CreateAsset(mesh, meshPath);
                existing = mesh;
            }

            var root = new GameObject(name);
            try
            {
                var visual = new GameObject("SyntyVisual");
                visual.transform.SetParent(root.transform, false);
                visual.transform.localScale = Vector3.one * VisualScale;
                visual.AddComponent<MeshFilter>().sharedMesh = existing;
                visual.AddComponent<MeshRenderer>().sharedMaterial = material;

                PrefabUtility.SaveAsPrefabAsset(root, $"{TilesDir}/{name}.prefab");
                Debug.Log($"SyntyRoadTiles: baked '{name}'");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// Reads the tile's Road paths and returns one arm per touched edge. Works in the
        /// AUTHORED frame (edge at 15, minor lanes 1.5, main lanes up to 4.75) and converts
        /// widths to the metric visual frame.
        /// </summary>
        static List<Arm> MeasureArms(GameObject tile)
        {
            var byEdge = new Dictionary<Vector2, float>(); // dir -> max |lane offset| (authored)
            var tileComponent = tile.GetComponent<CityTile>();
            var paths = tileComponent ? tileComponent.paths : null;
            if (paths == null || paths.Count == 0)
                paths = tile.GetComponentsInChildren<CityPath>(true)
                    .Where(p => p.pathType == LivingCity.City.PathType.Road).ToList();

            foreach (var path in paths)
            {
                if (!path || path.pathType != LivingCity.City.PathType.Road || path.pathPositions == null)
                    continue;
                foreach (var node in new[] { path.pathPositions.FirstOrDefault(), path.pathPositions.LastOrDefault() })
                {
                    if (!node) continue;
                    var p = tile.transform.InverseTransformPoint(node.position);

                    Vector2 dir;
                    float offset;
                    if (Mathf.Abs(p.z - 15f) < 0.7f) { dir = Vector2.up; offset = p.x; }
                    else if (Mathf.Abs(p.z + 15f) < 0.7f) { dir = Vector2.down; offset = p.x; }
                    else if (Mathf.Abs(p.x - 15f) < 0.7f) { dir = Vector2.right; offset = p.z; }
                    else if (Mathf.Abs(p.x + 15f) < 0.7f) { dir = Vector2.left; offset = p.z; }
                    else continue;

                    byEdge.TryGetValue(dir, out var max);
                    byEdge[dir] = Mathf.Max(max, Mathf.Abs(offset));
                }
            }

            return byEdge.Select(kv => new Arm
            {
                dir = kv.Key,
                halfWidth = kv.Value > 3f ? MainHalfWidth : MinorHalfWidth,
            }).ToList();
        }

        /// <summary>
        /// Is this local-metric point on the asphalt (with margin)? An arm covers its band
        /// from the far side of the crossing arms out to its own edge, which joins strips
        /// through the core for straights and leaves an L for the curve; a lone arm keeps a
        /// square head at the centre for the dead-end turning circle.
        /// </summary>
        static bool OnAsphalt(List<Arm> arms, float x, float z, float margin)
        {
            foreach (var arm in arms)
            {
                float along, across;
                float perpReach = 0f;
                var opposite = false;
                foreach (var other in arms)
                {
                    if (other.dir == -arm.dir) opposite = true;
                    if (Mathf.Abs(Vector2.Dot(other.dir, arm.dir)) < 0.5f)
                        perpReach = Mathf.Max(perpReach, other.halfWidth);
                }
                if (perpReach <= 0f && !opposite)
                    perpReach = arm.halfWidth; // dead-end head

                if (arm.dir == Vector2.up) { along = z; across = x; }
                else if (arm.dir == Vector2.down) { along = -z; across = x; }
                else if (arm.dir == Vector2.right) { along = x; across = z; }
                else { along = -x; across = z; }

                if (Mathf.Abs(across) <= arm.halfWidth + margin && along >= -perpReach - margin)
                    return true;
            }
            return false;
        }

        static void LayAsphalt(Transform parent, string name, List<Arm> arms)
        {
            // Plain fill first, on the 5 m grid, everywhere the analytic region says asphalt.
            for (var x = 0; x < 9; x++)
                for (var z = 0; z < 9; z++)
                {
                    float minX = -Half + x * 5f, minZ = -Half + z * 5f;
                    if (OnAsphalt(arms, minX + 2.5f, minZ + 2.5f, -0.1f))
                        PlaceRect(parent, Asphalt5, minX, minZ, 0f);
                }

            // Marked carriageway on top of each arm, outside the junction core, at +2 cm so
            // the markings win the overdraw against the plain fill.
            var crosswalk = name.Contains("crosswalk");
            foreach (var arm in arms)
            {
                var perpReach = arms.Where(o => Mathf.Abs(Vector2.Dot(o.dir, arm.dir)) < 0.5f)
                                    .Select(o => o.halfWidth).DefaultIfEmpty(0f).Max();
                var isStraightRun = arms.Count == 2 && arms.Any(o => o.dir == -arm.dir);

                var lanes = arm.halfWidth > MinorHalfWidth + 0.1f
                    ? new[] { -10f, 0f }   // main: two carriageways, each 10 wide
                    : new[] { -5f };       // minor: one, centred

                foreach (var laneMin in lanes)
                {
                    // Run from the core edge (or the tile centre on a straight) to the rim.
                    var from = isStraightRun ? 0f : perpReach;
                    var runs = SegmentRun(from, Half, crosswalk && isStraightRun);
                    foreach (var (min, len, marked) in runs)
                        PlaceArmPiece(parent, arm, laneMin, min, len, marked);
                }
            }
        }

        /// <summary>
        /// Splits a half-arm run into 10/5 m road pieces; on a crosswalk tile the first
        /// 2.5 m past the centre is left to the crossing overlay. Returns (start, length,
        /// marked) with marked=false for crossing stripes.
        /// </summary>
        static IEnumerable<(float min, float len, bool marked)> SegmentRun(float from, float to, bool crosswalkAtCentre)
        {
            var cursor = from;
            if (crosswalkAtCentre && Mathf.Approximately(from, 0f))
            {
                yield return (0f, 2.5f, false);
                cursor = 2.5f;
            }

            while (to - cursor > 0.1f)
            {
                var remaining = to - cursor;
                var len = remaining >= 10f ? 10f : (remaining >= 5f ? 5f : remaining);
                if (len < 4.9f) yield break; // sub-5 m tail stays plain fill
                yield return (cursor, len, true);
                cursor += len;
            }
        }

        static void PlaceArmPiece(Transform parent, Arm arm, float laneMin, float along, float len, bool marked)
        {
            var prefab = marked ? (len >= 9.9f ? Road10 : Road10Half) : Crossing;
            // Crossing pieces are 5 wide: two side by side per 10 m carriageway.
            var widths = marked ? new[] { 0f } : new[] { 0f, 5f };

            foreach (var w in widths)
            {
                float minX, minZ, yaw;
                var acrossMin = laneMin + w;
                if (arm.dir == Vector2.up) { minX = acrossMin; minZ = along; yaw = 0f; }
                else if (arm.dir == Vector2.down) { minX = -acrossMin - (marked ? 10f : 5f); minZ = -along - len; yaw = 180f; }
                else if (arm.dir == Vector2.right) { minX = along; minZ = acrossMin; yaw = 90f; }
                else { minX = -along - len; minZ = -acrossMin - (marked ? 10f : 5f); yaw = 270f; }

                PlaceRect(parent, prefab, minX, minZ, yaw, y: marked ? 0.02f : 0.03f);
            }
        }

        /// <summary>
        /// Kerb row against the asphalt, one plain row behind it, on the 2.5 m grid. Beyond
        /// that the block apron takes over, exactly as it did against the polyperfect tiles.
        /// </summary>
        static void LaySidewalk(Transform parent, List<Arm> arms)
        {
            for (var x = 0; x < 18; x++)
                for (var z = 0; z < 18; z++)
                {
                    float cx = -Half + x * 2.5f + 1.25f, cz = -Half + z * 2.5f + 1.25f;
                    if (OnAsphalt(arms, cx, cz, 1.2f))
                        continue; // would overlap the carriageway

                    // Which neighbour is asphalt decides kerb vs plain and the kerb's facing.
                    var kerbYaw = float.NaN;
                    if (OnAsphalt(arms, cx, cz - 2.5f, 0f)) kerbYaw = 180f;
                    else if (OnAsphalt(arms, cx, cz + 2.5f, 0f)) kerbYaw = 0f;
                    else if (OnAsphalt(arms, cx - 2.5f, cz, 0f)) kerbYaw = 270f;
                    else if (OnAsphalt(arms, cx + 2.5f, cz, 0f)) kerbYaw = 90f;

                    if (!float.IsNaN(kerbYaw))
                        PlaceRect(parent, Kerb, cx - 1.25f, cz - 1.25f, kerbYaw);
                    else if (OnAsphalt(arms, cx, cz, 6.5f))
                        PlaceRect(parent, Walk, cx - 1.25f, cz - 1.25f, 0f);
                }
        }

        /// <summary>
        /// Places a corner-pivot Synty piece so its footprint lands on the rect starting at
        /// (minX, minZ), whatever the yaw. Footprint size is read from the piece's renderer
        /// bounds at identity.
        /// </summary>
        static void PlaceRect(Transform parent, string prefabPath, float minX, float minZ, float yaw, float y = 0f)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (!prefab)
            {
                Debug.LogWarning($"SyntyRoadTiles: missing piece {prefabPath}");
                return;
            }

            var piece = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            var size = PieceSize(prefab);
            var w = size.x;
            var l = size.z;

            Vector3 pos = Mathf.RoundToInt(Mathf.Repeat(yaw, 360f)) switch
            {
                90 => new Vector3(minX, y, minZ + w),
                180 => new Vector3(minX + w, y, minZ + l),
                270 => new Vector3(minX + l, y, minZ),
                _ => new Vector3(minX, y, minZ),
            };
            piece.transform.SetLocalPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        }

        static readonly Dictionary<GameObject, Vector3> SizeCache = new();

        static Vector3 PieceSize(GameObject prefab)
        {
            if (SizeCache.TryGetValue(prefab, out var cached))
                return cached;
            var bounds = new Bounds();
            var first = true;
            foreach (var r in prefab.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (first) { bounds = r.bounds; first = false; }
                else bounds.Encapsulate(r.bounds);
            }
            return SizeCache[prefab] = bounds.size;
        }

        /// <summary>Removes renderers, mesh filters and non-trigger colliders; keeps every
        /// trigger (crosswalk counters), every Path node and the Tile itself.</summary>
        static void StripVisuals(GameObject tile)
        {
            foreach (var r in tile.GetComponentsInChildren<Renderer>(true))
                if (r) Object.DestroyImmediate(r);
            foreach (var f in tile.GetComponentsInChildren<MeshFilter>(true))
                if (f) Object.DestroyImmediate(f);
            foreach (var c in tile.GetComponentsInChildren<Collider>(true))
                if (c && !c.isTrigger) Object.DestroyImmediate(c);
        }

        /// <summary>One mesh per material set, saved as an asset, mounted on target.</summary>
        static void BakeInto(GameObject staging, GameObject target, string name)
        {
            var byMaterial = new Dictionary<Material, List<CombineInstance>>();
            foreach (var r in staging.GetComponentsInChildren<MeshRenderer>(true))
            {
                var filter = r.GetComponent<MeshFilter>();
                if (!filter || !filter.sharedMesh) continue;
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
                        transform = staging.transform.worldToLocalMatrix * r.localToWorldMatrix,
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

            var final = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            final.CombineMeshes(
                perMaterial.Select(pm => new CombineInstance { mesh = pm.mesh, transform = Matrix4x4.identity })
                           .ToArray(),
                mergeSubMeshes: false, useMatrices: true);
            final.RecalculateBounds();

            var meshPath = $"{MeshDir}/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (existing)
            {
                existing.Clear();
                EditorUtility.CopySerialized(final, existing);
                existing.name = name;
            }
            else
            {
                AssetDatabase.CreateAsset(final, meshPath);
                existing = final;
            }

            var mf = target.AddComponent<MeshFilter>();
            mf.sharedMesh = existing;
            var mr = target.AddComponent<MeshRenderer>();
            mr.sharedMaterials = perMaterial.Select(pm => pm.mat).ToArray();
        }
    }
}
