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

        public const int Version = 6;
        const string VersionPath = TilesDir + "/Version.txt";

        // Synty pieces, the PalmCity demo's own street kit. Sizes and marking orientation
        // verified offline against the binary FBX (all corner pivots; every road piece's
        // markings run along local +X; every road piece shares ONE material, guid 03eccdb9 -
        // the demo builds all its road surfaces from this Generic kit, PalmCity itself ships
        // only highway/decoration pieces). Sidewalks are the PalmCity kit (byte-identical to
        // the Generic one), with their own material exactly as in the demo.
        const string GenRoads = "Assets/Synty/PolygonGeneric/Prefabs/Environment/";
        const string Road10 = GenRoads + "SM_Gen_Env_Road_01.prefab";               // 10 x 10 two-lane, centre dashes
        const string RoadHalf = GenRoads + "SM_Gen_Env_Road_Half_01.prefab";        // 5 along traffic x 10 wide, one dash
        const string StopLine = GenRoads + "SM_Gen_Env_Road_Intersection_01.prefab";// 5 along x 10 wide, stop line at local x~0.6
        const string Crossing = GenRoads + "SM_Gen_Env_Road_Crossing_01.prefab";    // 5 x 5 zebra, bars long in X
        const string Asphalt2 = GenRoads + "SM_Gen_Env_Road_Small_02.prefab";       // 2.5 x 2.5 plain, the atomic filler
        const string Walk = "Assets/Synty/PolygonPalmCity/Prefabs/Environment/SM_Env_Sidewalk_01.prefab";           // 2.5
        const string Kerb = "Assets/Synty/PolygonPalmCity/Prefabs/Environment/SM_Env_Sidewalk_Edge_01.prefab";      // 2.5, gutter on +Z
        const string KerbOuter = "Assets/Synty/PolygonPalmCity/Prefabs/Environment/SM_Env_Sidewalk_Corner_01.prefab"; // wraps two adjacent road sides
        const string KerbInner = "Assets/Synty/PolygonPalmCity/Prefabs/Environment/SM_Env_Sidewalk_Corner_02.prefab"; // corner notch toward a diagonal road

        /// <summary>Visual content spans 45 local metres over the 30-unit authored tile.</summary>
        const float Span = 45f;
        const float Half = Span / 2f;                    // 22.5
        const float VisualScale = 30f / Span;            // 2/3
        const float MinorHalfWidth = 5f;                 // 10 m road
        const float MainHalfWidth = 10f;                 // two 10 m carriageways, butted

        /// <summary>
        /// The demo height model: ONE plane. Road, crossing and sidewalk pieces all sit at
        /// y = 0, butt-jointed with no overlay stacking - every 2.5 m cell of the surface is
        /// covered by exactly one piece, which is how the PalmCity demo lays its 1,877 street
        /// pieces. The polyperfect logic drove cars at authored -0.2 (its road surface was a
        /// sunken slab); BuildRoadTile lifts those Path nodes onto this plane instead, so the
        /// whole city - traffic, parked stalls, forecourts, docking - shares one ground level.
        /// The kerb's own gutter dips below 0 and tucks under the adjacent road piece exactly
        /// as authored. Walk rows behind the kerb that would collide with GroundPlacer's
        /// apron (base at world 0.01) are laid at HiddenWalkY: sunk just enough that their
        /// slab top stays below the apron, they only exist to plug the sliver the avenue's
        /// wider pavement leaves between kerb ring and apron edge.
        /// </summary>
        const float HiddenWalkY = -0.02f;

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

            // The polyperfect logic drove cars at authored -0.2, on its sunken road slab.
            // The Synty surface is one flat plane at 0 (the demo model), so every path node
            // below it - road lanes, and the crossing legs that dip to road level - is
            // lifted onto the plane. Sidewalk nodes already sit at 0 and are untouched.
            foreach (var path in instance.GetComponentsInChildren<CityPath>(true))
            {
                if (path.pathPositions == null)
                    continue;
                foreach (var node in path.pathPositions)
                    if (node && node.position.y < -0.05f)
                        node.position = new Vector3(node.position.x, 0f, node.position.z);
            }

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

                // On the ROOT, not the visual child: PathFinding.FindClosestTile's fast path
                // does collider.transform.GetComponent<Tile>() - same GameObject only - and a
                // child collider silently demotes every tile query to the O(n) registry scan.
                // Root scale is 1 and authored, so the slab is 30 units, not 45.
                var ground = instance.AddComponent<BoxCollider>();
                ground.center = new Vector3(0f, -0.05f, 0f);
                ground.size = new Vector3(Span * VisualScale, 0.1f, Span * VisualScale);

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

        /// <summary>
        /// The carriageway as a PARTITION, demo-style: big pieces claim their cells in an
        /// occupancy grid, the atomic 2.5 m filler takes every remaining asphalt cell, and
        /// nothing overlaps anything.
        ///
        /// Per arm, dashed Road_01 pieces anchor to the RIM in 10 m slots so the dash pitch
        /// continues across every cell seam city-wide. The 45-vs-10 remainder is absorbed at
        /// the centre of straights (where the zebra sits on crosswalk tiles, so plain and
        /// crosswalk straights share one dash phase) and at the mouths of junctions - where
        /// a 7.5 m mouth gets the demo's stop-line piece against the core plus one filler
        /// row, and a curve gets the half road piece so its centre line runs on toward the
        /// bend.
        /// </summary>
        static void LayAsphalt(Transform parent, string name, List<Arm> arms)
        {
            var used = new bool[18, 18];

            void MarkRect(float minX, float minZ, float w, float l)
            {
                for (var x = Mathf.RoundToInt((minX + Half) / 2.5f); x < Mathf.RoundToInt((minX + Half + w) / 2.5f); x++)
                    for (var z = Mathf.RoundToInt((minZ + Half) / 2.5f); z < Mathf.RoundToInt((minZ + Half + l) / 2.5f); z++)
                        if (x >= 0 && x < 18 && z >= 0 && z < 18)
                            used[x, z] = true;
            }

            void Mark((float minX, float minZ, float w, float l) r) => MarkRect(r.minX, r.minZ, r.w, r.l);

            var crosswalk = name.Contains("crosswalk");
            var junction = arms.Count >= 3;

            foreach (var arm in arms)
            {
                var perpReach = arms.Where(o => Mathf.Abs(Vector2.Dot(o.dir, arm.dir)) < 0.5f)
                                    .Select(o => o.halfWidth).DefaultIfEmpty(0f).Max();
                var isStraightRun = arms.Count == 2 && arms.Any(o => o.dir == -arm.dir);
                var runStart = isStraightRun ? (crosswalk ? 2.5f : 0f) : perpReach;

                var lanes = arm.halfWidth > MinorHalfWidth + 0.1f
                    ? new[] { -10f, 0f }   // main: two carriageways, each 10 wide
                    : new[] { -5f };       // minor: one, centred

                foreach (var laneMin in lanes)
                {
                    var slotStart = Half;
                    for (; slotStart - 10f >= runStart - 0.01f; slotStart -= 10f)
                        Mark(PlaceArmPiece(parent, arm, laneMin, slotStart - 10f, 10f, Road10));

                    // The mouth between the junction core (or curve corner) and the first
                    // dash slot: 7.5 m on a minor arm. Stop line against the core at a real
                    // junction, the half road piece on a curve; either way one 2.5 m row is
                    // left for the generic filler below.
                    if (slotStart - runStart >= 4.9f)
                        Mark(PlaceArmPiece(parent, arm, laneMin, runStart, 5f,
                                           junction ? StopLine : RoadHalf, coreFacing: junction));
                }
            }

            if (crosswalk)
            {
                // Zebra row centred on the tile (and on the polyperfect crosswalk trigger):
                // one Crossing piece per 5 m across the full width - two on a street, four
                // on the avenue - bars (long in local X) turned along the traffic axis.
                var arm = arms[0];
                var vertical = Mathf.Abs(arm.dir.y) > 0.5f; // road runs along z
                for (var c = -arm.halfWidth; c < arm.halfWidth - 0.1f; c += 5f)
                {
                    if (vertical) PlaceRect(parent, Crossing, c, -2.5f, 90f);
                    else PlaceRect(parent, Crossing, -2.5f, c, 0f);
                    MarkRect(vertical ? c : -2.5f, vertical ? -2.5f : c, 5f, 5f);
                }
            }

            // Every asphalt cell no big piece claimed: junction cores, dead-end heads, the
            // 5 m plain zone at straight centres, filler rows at the mouths. Every band edge
            // in this file is a multiple of 2.5, so a cell is either fully on the asphalt or
            // fully off it - no holes, no overhang under the kerb.
            for (var x = 0; x < 18; x++)
                for (var z = 0; z < 18; z++)
                {
                    float minX = -Half + x * 2.5f, minZ = -Half + z * 2.5f;
                    if (!used[x, z] && OnAsphalt(arms, minX + 1.25f, minZ + 1.25f, -0.1f))
                        PlaceRect(parent, Asphalt2, minX, minZ, 0f);
                }
        }

        /// <summary>
        /// Generic road art runs its markings along local +X (centre dashes, zebra bars,
        /// and the stop line sits at the piece's LOW-x end), so a piece is yawed to lay X
        /// along the arm. The across range is the same for both signs of an axis - lane
        /// sets are symmetric - so only the along coordinate is signed. For the symmetric
        /// dash pieces the 90/270 choice is arbitrary; an asymmetric piece passes
        /// coreFacing so its low-x end lands on the junction-core side of the footprint.
        /// Returns the footprint for the occupancy grid.
        /// </summary>
        static (float minX, float minZ, float w, float l) PlaceArmPiece(
            Transform parent, Arm arm, float acrossMin, float along, float len,
            string prefabPath, bool coreFacing = false)
        {
            float minX, minZ, yaw;
            if (arm.dir == Vector2.up) { minX = acrossMin; minZ = along; yaw = coreFacing ? 270f : 90f; }
            else if (arm.dir == Vector2.down) { minX = acrossMin; minZ = -along - len; yaw = coreFacing ? 90f : 270f; }
            else if (arm.dir == Vector2.right) { minX = along; minZ = acrossMin; yaw = 0f; }
            else { minX = -along - len; minZ = acrossMin; yaw = 180f; }
            PlaceRect(parent, prefabPath, minX, minZ, yaw);

            var across = 10f; // every arm piece is a full 10 m carriageway wide
            return arm.dir == Vector2.up || arm.dir == Vector2.down
                ? (minX, minZ, across, len)
                : (minX, minZ, len, across);
        }

        /// <summary>
        /// The kerb ring against the asphalt on the 2.5 m grid, demo-style: straight Edge
        /// pieces along the runs, the Corner_01 wrap where the pavement turns a junction
        /// corner (two perpendicular asphalt neighbours), the Corner_02 notch where the
        /// asphalt only touches diagonally. Behind the ring the block apron takes over at
        /// PavementEdge - which on a minor street is exactly the kerb row's outer edge, so
        /// nothing else is drawn there; the avenue's wider pavement leaves a strip between
        /// kerb ring and apron, plugged by plain rows sunk to HiddenWalkY so their slab top
        /// stays below the apron instead of interleaving with it (the v4 "teeth").
        /// </summary>
        static void LaySidewalk(Transform parent, List<Arm> arms)
        {
            for (var x = 0; x < 18; x++)
                for (var z = 0; z < 18; z++)
                {
                    float cx = -Half + x * 2.5f + 1.25f, cz = -Half + z * 2.5f + 1.25f;
                    if (OnAsphalt(arms, cx, cz, 1.2f))
                        continue; // would overlap the carriageway

                    bool n = OnAsphalt(arms, cx, cz + 2.5f, 0f), s = OnAsphalt(arms, cx, cz - 2.5f, 0f);
                    bool e = OnAsphalt(arms, cx + 2.5f, cz, 0f), w = OnAsphalt(arms, cx - 2.5f, cz, 0f);

                    // Corner_01 at yaw 0 wraps the (east, south) pair - measured off the FBX
                    // gutter verts - and each +90 of yaw walks the pair one step round.
                    var yaw = float.NaN;
                    string piece = null;
                    if (e && s) { piece = KerbOuter; yaw = 0f; }
                    else if (s && w) { piece = KerbOuter; yaw = 90f; }
                    else if (w && n) { piece = KerbOuter; yaw = 180f; }
                    else if (n && e) { piece = KerbOuter; yaw = 270f; }
                    else if (s) { piece = Kerb; yaw = 180f; }
                    else if (n) { piece = Kerb; yaw = 0f; }
                    else if (w) { piece = Kerb; yaw = 270f; }
                    else if (e) { piece = Kerb; yaw = 90f; }
                    else
                    {
                        // Corner_02's notch sits at the FBX (+x,+z) corner, which lands on
                        // the (west, north) diagonal after the importer's X mirror.
                        if (OnAsphalt(arms, cx - 2.5f, cz + 2.5f, 0f)) { piece = KerbInner; yaw = 0f; }
                        else if (OnAsphalt(arms, cx - 2.5f, cz - 2.5f, 0f)) { piece = KerbInner; yaw = 90f; }
                        else if (OnAsphalt(arms, cx + 2.5f, cz - 2.5f, 0f)) { piece = KerbInner; yaw = 180f; }
                        else if (OnAsphalt(arms, cx + 2.5f, cz + 2.5f, 0f)) { piece = KerbInner; yaw = 270f; }
                    }

                    if (piece != null)
                        PlaceRect(parent, piece, cx - 1.25f, cz - 1.25f, yaw);
                    else if (OnAsphalt(arms, cx, cz, 6.5f))
                        PlaceRect(parent, Walk, cx - 1.25f, cz - 1.25f, 0f, y: HiddenWalkY);
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
                var matrix = staging.transform.worldToLocalMatrix * r.localToWorldMatrix;
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
