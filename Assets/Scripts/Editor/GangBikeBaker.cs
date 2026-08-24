using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// The outfit's tourer: the police pack's big machine with the force taken off it.
    ///
    /// The pack ships exactly one motorcycle worth riding two-up (POLICE STATION's
    /// SM_Veh_Motorbike_01, 2.10 m of it) and it is dressed as a patrol bike - panniers
    /// and a top box over the whole back third, a mast and a whip aerial, a windscreen,
    /// and a white body under a blue-and-yellow chequer. None of that belongs under two
    /// men going past a shopfront, so this bakes a stripped black one.
    ///
    /// It is a BAKE and not a hand edit for the ordinary reason: what it removes is
    /// argued in code (bounds and atlas colours, both printed in the log) rather than
    /// clicked once and forgotten, so a pack update is a re-run and not an afternoon.
    /// Nothing under Assets/Synty is written to - the pack's bike is untouched and still
    /// the law's.
    ///
    /// Two operations, and they are different in kind:
    ///
    /// GEOMETRY. The body is ONE mesh of 7,268 triangles with one material, so the
    /// panniers cannot be switched off - they have to be cut out. The mesh is split into
    /// islands (union-find over position-welded vertices; Synty splits every hard edge,
    /// so raw index adjacency finds nothing), and an island is dropped by WHERE IT SITS:
    /// the two panniers and their racks out at the flanks behind the saddle, the top box
    /// above them, and anything tall standing at the very back. That is 1,768 triangles
    /// of police kit off a machine that keeps its frame, its seat, its tail and its
    /// exhaust.
    ///
    /// PAINT. The colour is not the material's, it is the ATLAS the UVs point into, so
    /// tinting the material darkens the tyres and the chrome along with the bodywork and
    /// still leaves the chequer readable in silhouette. Instead the livery is repainted
    /// in UV SPACE: every triangle whose atlas pixel is the white bodywork, the chequer's
    /// yellow or its navy is moved onto the atlas's darkest paint swatch. The chrome, the
    /// tyres, the glass and the lamps keep their own squares, which is why the machine
    /// reads as a black bike and not as a black cut-out.
    ///
    /// Beware the colour space: the atlas is sampled through a LINEAR render texture, so
    /// the numbers below are linear and look far darker than the swatch does on screen
    /// (linear 13 is about sRGB 64). Read them as atlas keys, not as paint.
    /// </summary>
    public static class GangBikeBaker
    {
        const string PoliceBike =
            "Assets/Synty/PolygonPoliceStation/Prefabs/Vehicles/SM_Veh_Motorbike_01.prefab";
        const string PoliceMaterial =
            "Assets/Synty/PolygonPoliceStation/Materials/Vehicles/Police_Vehicle_01.mat";
        const string Folder = "Assets/Prefabs/Vehicles";
        const string Name = "SM_Veh_Motorbike_Tourer_Black";

        /// <summary>The atlas colours the force is wearing, LINEAR, as sampled off
        /// _Albedo_Map: the white bodywork, the chequer's yellow, its two navies, and the
        /// one stray light square. Anything within 10 units of one of these is repainted;
        /// the chrome (97,97,97) and the tyre blacks sit well outside that.
        ///
        /// The pale grey (107,114,124) was tried here and taken back out: the rear deck
        /// that looks light in a render is ALREADY on the black swatch, and only reads
        /// pale because a flat deck takes the key light square on. Sample the atlas before
        /// adding a colour to this list, do not judge it off a picture.</summary>
        static readonly Vector3[] Livery =
        {
            new Vector3(171, 166, 164),
            new Vector3(192, 179, 9),
            new Vector3(9, 19, 66),
            new Vector3(14, 24, 64),
            new Vector3(190, 190, 190),
        };

        /// <summary>Where the repainted triangles are sent: the darkest paint square on
        /// the atlas (linear 13,15,17 - about sRGB 64). Deliberately NOT the tyre black,
        /// so a black tank still reads as a different surface from a tyre.</summary>
        static readonly Vector2 Paint = new Vector2(0.088f, 0.251f);

        [MenuItem("Tools/City/Vehicles/Bake the outfit's black tourer")]
        public static void Bake()
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(PoliceBike);
            if (source == null)
            {
                Debug.LogError("[GangBike] the police pack's tourer is not at " + PoliceBike);
                return;
            }

            var atlas = ReadAtlas();
            if (atlas == null) return;

            if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
                AssetDatabase.CreateFolder("Assets", "Prefabs");
            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets/Prefabs", "Vehicles");

            var go = (GameObject)PrefabUtility.InstantiatePrefab(source);
            go.transform.position = Vector3.zero;
            go.transform.rotation = Quaternion.identity;
            go.name = Name;

            // The visor first, because it is the one part the pack DID leave separate -
            // two nodes, the screen and its glass.
            var screens = new List<GameObject>();
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                if (t != go.transform && t.name.Contains("_Screen"))
                    screens.Add(t.gameObject);
            foreach (var screen in screens)
                Object.DestroyImmediate(screen);

            // Then every mesh on the machine. Only the BODY is cut (the police kit is
            // baked into it); the wheels, bars and shocks are repainted only, and a mesh
            // that came out unchanged is left pointing at the pack's own.
            var meshes = new List<Mesh>();
            int cut = 0, repainted = 0;
            foreach (var filter in go.GetComponentsInChildren<MeshFilter>(true))
            {
                var body = filter.transform == go.transform;
                var made = Rebuild(filter.sharedMesh, atlas, body, out int dropped, out int painted);
                cut += dropped;
                repainted += painted;
                if (dropped == 0 && painted == 0) continue;
                filter.sharedMesh = made;
                meshes.Add(made);
            }
            Object.DestroyImmediate(atlas);

            var meshPath = Folder + "/" + Name + "_Meshes.asset";
            AssetDatabase.DeleteAsset(meshPath);
            if (meshes.Count > 0)
            {
                AssetDatabase.CreateAsset(meshes[0], meshPath);
                for (int i = 1; i < meshes.Count; i++)
                    AssetDatabase.AddObjectToAsset(meshes[i], meshPath);
            }

            var prefabPath = Folder + "/" + Name + ".prefab";
            var saved = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            Object.DestroyImmediate(go);
            AssetDatabase.SaveAssets();

            Debug.Log("[GangBike] " + prefabPath + " - " + cut + " triangles of police kit cut, " +
                      repainted + " repainted black, " + screens.Count + " screen parts off. " +
                      (saved != null ? "Saved." : "SAVE FAILED."));
            if (saved != null)
                Selection.activeObject = saved;
        }

        /// <summary>The pack's albedo atlas as readable pixels, without touching its
        /// import settings: blit to a temporary render texture and read that back. Linear,
        /// so what comes out matches <see cref="Livery"/>.</summary>
        static Texture2D ReadAtlas()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(PoliceMaterial);
            var source = material != null ? material.GetTexture("_Albedo_Map") as Texture2D : null;
            if (source == null)
            {
                Debug.LogError("[GangBike] no _Albedo_Map on " + PoliceMaterial);
                return null;
            }

            var rt = RenderTexture.GetTemporary(source.width, source.height, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Graphics.Blit(source, rt);
            var was = RenderTexture.active;
            RenderTexture.active = rt;
            var copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            copy.Apply();
            RenderTexture.active = was;
            RenderTexture.ReleaseTemporary(rt);
            return copy;
        }

        /// <summary>One mesh, cut (bodies only) and repainted. Returns a new mesh even
        /// when nothing changed; the caller decides whether to keep it.</summary>
        static Mesh Rebuild(Mesh src, Texture2D atlas, bool cutPoliceKit, out int dropped, out int painted)
        {
            dropped = 0;
            painted = 0;
            if (src == null) return null;

            var verts = src.vertices;
            var tris = src.triangles;
            var uv = src.uv;

            var keep = new List<int>(tris.Length);
            if (cutPoliceKit)
            {
                var island = Islands(verts, tris, out var bounds);
                var orphans = Orphans(island, tris, bounds);
                for (int t = 0; t < tris.Length; t += 3)
                {
                    int g = island[tris[t]];
                    if (PoliceKit(bounds[g]) || orphans.Contains(g)) { dropped++; continue; }
                    keep.Add(tris[t]); keep.Add(tris[t + 1]); keep.Add(tris[t + 2]);
                }
            }
            else keep.AddRange(tris);

            var repainted = (Vector2[])uv.Clone();
            for (int t = 0; t < keep.Count; t += 3)
            {
                var centre = (uv[keep[t]] + uv[keep[t + 1]] + uv[keep[t + 2]]) / 3f;
                var pixel = atlas.GetPixelBilinear(centre.x, centre.y);
                var rgb = new Vector3(Mathf.Round(pixel.r * 255f), Mathf.Round(pixel.g * 255f),
                                      Mathf.Round(pixel.b * 255f));
                bool livery = false;
                foreach (var colour in Livery)
                    if ((rgb - colour).sqrMagnitude < 100f) { livery = true; break; }
                if (!livery) continue;
                for (int k = 0; k < 3; k++) repainted[keep[t + k]] = Paint;
                painted++;
            }

            // Only the vertices the kept triangles actually use. Not housekeeping: a mesh
            // that keeps the panniers' vertices keeps the panniers' BOUNDS, and the
            // bounds are what CrewCars.MeasurePrefab parks the machine by - so a bike
            // with its mast cut off would still be measured a metre and a half tall.
            var moved = new int[verts.Length];
            for (int i = 0; i < moved.Length; i++) moved[i] = -1;
            var normals = src.normals;
            var uv2 = src.uv2 != null && src.uv2.Length == verts.Length ? src.uv2 : null;
            var colors = src.colors != null && src.colors.Length == verts.Length ? src.colors : null;

            var outVerts = new List<Vector3>(verts.Length);
            var outNormals = normals != null && normals.Length == verts.Length ? new List<Vector3>(verts.Length) : null;
            var outUv = new List<Vector2>(verts.Length);
            var outUv2 = uv2 != null ? new List<Vector2>(verts.Length) : null;
            var outColors = colors != null ? new List<Color>(verts.Length) : null;
            var outTris = new List<int>(keep.Count);
            foreach (var index in keep)
            {
                if (moved[index] < 0)
                {
                    moved[index] = outVerts.Count;
                    outVerts.Add(verts[index]);
                    outNormals?.Add(normals[index]);
                    outUv.Add(repainted[index]);
                    outUv2?.Add(uv2[index]);
                    outColors?.Add(colors[index]);
                }
                outTris.Add(moved[index]);
            }

            var mesh = new Mesh { name = src.name };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(outVerts);
            if (outNormals != null) mesh.SetNormals(outNormals);
            mesh.SetUVs(0, outUv);
            if (outUv2 != null) mesh.SetUVs(1, outUv2);
            if (outColors != null) mesh.SetColors(outColors);
            mesh.SetTriangles(outTris, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        /// <summary>Which island each vertex belongs to, and what each island measures.
        /// Welded by POSITION first: Synty duplicates a vertex at every hard edge, so the
        /// index adjacency of one pannier is a dozen unconnected shells.</summary>
        static int[] Islands(Vector3[] verts, int[] tris, out Dictionary<int, Bounds> bounds)
        {
            var welded = new Dictionary<Vector3, int>(verts.Length);
            var of = new int[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                var key = new Vector3(Mathf.Round(verts[i].x * 1000f), Mathf.Round(verts[i].y * 1000f),
                                      Mathf.Round(verts[i].z * 1000f));
                if (!welded.TryGetValue(key, out int id)) { id = welded.Count; welded[key] = id; }
                of[i] = id;
            }

            var parent = new int[welded.Count];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;
            for (int t = 0; t < tris.Length; t += 3)
            {
                int a = Find(parent, of[tris[t]]);
                int b = Find(parent, of[tris[t + 1]]);
                int c = Find(parent, of[tris[t + 2]]);
                if (a != b) parent[b] = a;
                if (a != c) parent[c] = a;
            }

            bounds = new Dictionary<int, Bounds>();
            var island = new int[verts.Length];
            for (int i = 0; i < verts.Length; i++) island[i] = Find(parent, of[i]);
            for (int t = 0; t < tris.Length; t += 3)
                for (int k = 0; k < 3; k++)
                {
                    int g = island[tris[t + k]];
                    var v = verts[tris[t + k]];
                    if (bounds.TryGetValue(g, out var b)) { b.Encapsulate(v); bounds[g] = b; }
                    else bounds[g] = new Bounds(v, Vector3.zero);
                }
            return island;
        }

        /// <summary>The islands left hanging in the air once the police kit is out: the
        /// tail lamp and the number plate were carried by the top box's tail, and cutting
        /// it leaves them floating a hand's breadth behind the wheel. Found rather than
        /// listed - grow outwards from the largest island, keeping anything whose bounds
        /// come within three centimetres of something already kept, and whatever the
        /// growth never reaches is not attached to the motorcycle any more.</summary>
        static HashSet<int> Orphans(int[] island, int[] tris, Dictionary<int, Bounds> bounds)
        {
            var live = new List<int>();
            foreach (var g in bounds.Keys)
                if (!PoliceKit(bounds[g])) live.Add(g);
            if (live.Count == 0) return new HashSet<int>();

            int biggest = live[0];
            foreach (var g in live)
                if (bounds[g].size.sqrMagnitude > bounds[biggest].size.sqrMagnitude) biggest = g;

            var attached = new HashSet<int> { biggest };
            bool grew = true;
            while (grew)
            {
                grew = false;
                foreach (var g in live)
                {
                    if (attached.Contains(g)) continue;
                    var reach = bounds[g];
                    reach.Expand(0.03f);
                    foreach (var held in attached)
                        if (reach.Intersects(bounds[held])) { attached.Add(g); grew = true; break; }
                    if (grew) break;
                }
            }

            var orphans = new HashSet<int>();
            foreach (var g in live)
                if (!attached.Contains(g)) orphans.Add(g);
            return orphans;
        }

        static int Find(int[] parent, int x)
        {
            while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; }
            return x;
        }

        /// <summary>Whether an island of the body is police kit rather than motorcycle,
        /// by where it sits in the machine's own frame (nose +Z, wheelbase 1.9 m, saddle
        /// about z -0.2). Measured off the pack, not guessed: the panniers are the only
        /// things out at |x| 0.16-0.43 behind the saddle, the top box the only thing above
        /// them on the centre line, and nothing on a motorcycle stands a metre high at the
        /// very back but a mast and an aerial.</summary>
        static bool PoliceKit(Bounds island)
        {
            var c = island.center;
            bool panniers = c.z < -0.2f && Mathf.Abs(c.x) > 0.15f && c.y > 0.3f && c.y < 1.0f;
            // The box only, NOT the pillion's seat underneath it. They sit one on top of
            // the other on the centre line and a centre-height test cannot tell them
            // apart: the box STARTS above the seat's top (min y 0.76 against the seat's
            // 0.65), so the floor of the island is what separates them. Getting this
            // wrong takes the passenger's saddle off a machine bought to carry two.
            bool topBox = c.z < -0.4f && island.min.y > 0.72f && Mathf.Abs(c.x) < 0.25f;
            bool mast = island.max.y > 0.95f && c.z < -0.5f;
            // The rack the top box sat on: a 65 cm rail down the centre line at seat
            // height, standing a good 20 cm PROUD of the tail once the box is gone. Two
            // things it must not catch, and the WIDTH is what keeps them: the rail is
            // 18 cm across, the pillion's saddle above it is 38, and the rear mudguard
            // hugs the wheel narrower still but nowhere near this long in z.
            bool rack = c.z < -0.4f && Mathf.Abs(c.x) < 0.15f && c.y > 0.35f && c.y < 0.85f
                        && island.size.z > 0.3f && island.size.x < 0.25f;
            return panniers || topBox || mast || rack;
        }
    }
}
