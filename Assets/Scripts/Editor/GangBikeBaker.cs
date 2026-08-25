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
    /// PAINT, and it is two operations rather than one because the first cut of this
    /// made the machine flat. The colour is not the material's, it is the ATLAS the UVs
    /// point into, so tinting the whole material darkens the tyres and the chrome along
    /// with the bodywork. The first bake answered that by moving EVERY livery triangle -
    /// the white bodywork, the chequer's yellow and both its navies - onto one dark
    /// swatch, and that is exactly what was wrong with it: the tank, the fairing, the
    /// mudguards and the flanks all came out the same square, so the bike read as one
    /// unbroken block of colour with a wheel at each end.
    ///
    /// So the livery is split by what it WAS:
    ///
    ///   the chequer   yellow and the two navies, the flanks and the bands - moved onto
    ///                 the atlas's darkest paint swatch, as before. This is the trim, and
    ///                 it stays dark whatever the machine is painted.
    ///   the bodywork  the white panels - LEFT WHERE THEY ARE, on the atlas's big flat
    ///                 white field (sRGB 214,211,210, which covers most of the upper
    ///                 half), and moved instead onto a SUBMESH OF THEIR OWN under a
    ///                 material this project owns.
    ///
    /// That second move is what makes the machine colourable at all. The police pack
    /// ships no alts - its twelve vehicle atlases hold one paint palette between them -
    /// so there is nothing to swap. But a bright flat field under a material whose
    /// _BaseColor the shader MULTIPLIES into the albedo takes any colour asked of it, and
    /// VehiclePaint.TourerPalette is the list of them; <see cref="BakePaints"/> writes one
    /// material per colour, all sharing the pack's one texture, so the palette is free.
    /// The prefab is saved wearing the graphite, which lands near sRGB 48 - the black the
    /// machine has always been - and the street swaps it for another at spawn.
    ///
    /// Beware the colour space, TWICE over. The atlas is sampled through a LINEAR render
    /// texture, so the livery keys below are linear and look far darker than the swatch
    /// does on screen (linear 171 is sRGB 214). And the shader's BaseColor is declared
    /// HDR (Generic_Basic.shadergraph, m_ColorMode 1), which is the one kind of colour
    /// property Unity does NOT gamma-convert on the way in - see <see cref="BakePaints"/>,
    /// which asks the shader rather than assuming either way.
    /// </summary>
    public static class GangBikeBaker
    {
        const string PoliceBike =
            "Assets/Synty/PolygonPoliceStation/Prefabs/Vehicles/SM_Veh_Motorbike_01.prefab";
        const string PoliceMaterial =
            "Assets/Synty/PolygonPoliceStation/Materials/Vehicles/Police_Vehicle_01.mat";
        const string Folder = "Assets/Prefabs/Vehicles";
        const string PaintFolder = Folder + "/Tourer";
        const string Name = "SM_Veh_Motorbike_Tourer_Black";

        /// <summary>The atlas colours the machine's PANELS are wearing, LINEAR, as sampled
        /// off _Albedo_Map: the white bodywork and the one stray light square. Anything
        /// within 10 units of one of these becomes the paint - its own submesh, its own
        /// material, its UVs untouched, because the white field it already points at is
        /// the brightest flat ground on the atlas and so the best thing to tint.
        ///
        /// The pale grey (107,114,124) was tried here and taken back out: the rear deck
        /// that looks light in a render is ALREADY on the black swatch, and only reads
        /// pale because a flat deck takes the key light square on. Sample the atlas before
        /// adding a colour to this list, do not judge it off a picture.</summary>
        static readonly Vector3[] Bodywork =
        {
            new Vector3(171, 166, 164),   // sRGB 214,211,210 - the big white field
            new Vector3(190, 190, 190),
        };

        /// <summary>The force's markings, same units: the chequer's yellow and its two
        /// navies. These are moved onto <see cref="Trim"/> and stay on the pack's own
        /// material, so they read as the machine's dark flanks whatever it is painted.
        /// The chrome (97,97,97) and the tyre blacks sit well outside the 10-unit reach
        /// of any of them and are never touched.</summary>
        static readonly Vector3[] Chequer =
        {
            new Vector3(192, 179, 9),
            new Vector3(9, 19, 66),
            new Vector3(14, 24, 64),
        };

        /// <summary>
        /// Where the markings are sent: the bottom step of the atlas's charcoal column
        /// (sRGB 37,39,42 - linear about 5,5,6). Deliberately NOT the tyre black, so a
        /// dark flank still reads as a different surface from a tyre.
        ///
        /// One step down from where the first bake put them, and the step is the whole
        /// point. The flanks have to stay darker than ANY paint the machine can wear, or
        /// the two-tone collapses on the dark end of the palette - measured against the
        /// old swatch (sRGB 63,66,71) the maroon came out at a luminance ratio of 1.00,
        /// which is to say the panels and the flanks were the same tone and the machine
        /// was flat again in all but hue. Against this one the worst paint in the palette
        /// separates by 1.42. Its own column runs 37,39,42 up to 106,110,116, so the
        /// neighbours a mip drags in are charcoal too.
        ///
        /// The swatch grid is 44 px on a 4096 atlas (0.0107 of u), NOT the 128 px a
        /// glance at the layout suggests; these are the centre of a cell, measured.
        /// </summary>
        static readonly Vector2 Trim = new Vector2(0.0804f, 0.2296f);

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

            // The palette before the meshes: the paint slot they are about to grow needs
            // a material to be born wearing, and the graphite is it.
            //
            // And nothing may proceed without one. A mesh split into two submeshes whose
            // renderer holds one material draws the second submesh with NOTHING - which
            // on this machine is every panel it has, so the bike would come out of the
            // bake as a frame, two wheels and a hole where the bodywork was. Better no
            // re-bake at all than that, and the prefab on disk is left as it stands.
            var paints = BakePaints();
            if (paints.Length == 0)
            {
                Debug.LogError("[GangBike] no paints baked - the machine would lose its "
                               + "bodywork to an empty material slot. Nothing written.");
                Object.DestroyImmediate(atlas);
                Object.DestroyImmediate(go);
                return;
            }

            var graphite = paints[0];

            // Then every mesh on the machine. Only the BODY is cut (the police kit is
            // baked into it); the wheels, bars and shocks are repainted only, and a mesh
            // that came out unchanged is left pointing at the pack's own.
            var meshes = new List<Mesh>();
            int cut = 0, marked = 0, painted = 0;
            foreach (var filter in go.GetComponentsInChildren<MeshFilter>(true))
            {
                var body = filter.transform == go.transform;
                var made = Rebuild(filter.sharedMesh, atlas, body,
                                   out int dropped, out int trimmed, out int bodywork);
                cut += dropped;
                marked += trimmed;
                painted += bodywork;
                if (dropped == 0 && trimmed == 0 && bodywork == 0) continue;
                filter.sharedMesh = made;
                meshes.Add(made);
                // A mesh that grew a paint submesh needs the slot to go with it. Meshes
                // that came out one submesh keep the one material they had.
                if (bodywork > 0) GivePaintSlot(filter, graphite);
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
                      marked + " markings blacked, " + painted + " onto the paint submesh, " +
                      screens.Count + " screen parts off, " + paints.Length + " paints in " +
                      PaintFolder + ". " + (saved != null ? "Saved." : "SAVE FAILED."));
            if (painted == 0)
                Debug.LogWarning("[GangBike] NOTHING went onto the paint submesh - the machine " +
                                 "cannot take a colour. Sample the atlas at the bodywork before " +
                                 "trusting the Bodywork keys.");
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

        /// <summary>
        /// One mesh, cut (bodies only) and split. Returns a new mesh even when nothing
        /// changed; the caller decides whether to keep it.
        ///
        /// Comes back with ONE submesh when the mesh has no bodywork on it (a wheel, a
        /// shock) and TWO when it has: submesh 0 is everything the pack's own material
        /// still draws, submesh 1 is the panels that take the paint. Two submeshes share
        /// one vertex buffer, so the split costs a draw call and not a byte of geometry.
        /// </summary>
        static Mesh Rebuild(Mesh src, Texture2D atlas, bool cutPoliceKit,
                            out int dropped, out int trimmed, out int bodywork)
        {
            dropped = 0;
            trimmed = 0;
            bodywork = 0;
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

            // Which of the kept triangles are panels. Read once, off the atlas, and held
            // as a flag per triangle: the answer is wanted twice below - to move the
            // markings, and to sort the triangles into their two submeshes - and each
            // reading is a bilinear sample of a 4096px texture.
            var isPanel = new bool[keep.Count / 3];
            var repainted = (Vector2[])uv.Clone();
            for (int t = 0; t < keep.Count; t += 3)
            {
                var centre = (uv[keep[t]] + uv[keep[t + 1]] + uv[keep[t + 2]]) / 3f;
                var pixel = atlas.GetPixelBilinear(centre.x, centre.y);
                var rgb = new Vector3(Mathf.Round(pixel.r * 255f), Mathf.Round(pixel.g * 255f),
                                      Mathf.Round(pixel.b * 255f));

                if (Matches(rgb, Bodywork))
                {
                    // NOT moved. The white field it already sits on is the tint's ground.
                    isPanel[t / 3] = true;
                    bodywork++;
                    continue;
                }

                if (!Matches(rgb, Chequer)) continue;
                for (int k = 0; k < 3; k++) repainted[keep[t + k]] = Trim;
                trimmed++;
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
            // Two index lists over ONE vertex buffer. The sort is by triangle, so a
            // vertex a panel shares with its neighbour is written once and pointed at
            // from both.
            var packTris = new List<int>(keep.Count);
            var paintTris = new List<int>(bodywork * 3);
            for (int t = 0; t < keep.Count; t += 3)
            {
                var into = isPanel[t / 3] ? paintTris : packTris;
                for (int k = 0; k < 3; k++)
                {
                    var index = keep[t + k];
                    if (moved[index] < 0)
                    {
                        moved[index] = outVerts.Count;
                        outVerts.Add(verts[index]);
                        outNormals?.Add(normals[index]);
                        outUv.Add(repainted[index]);
                        outUv2?.Add(uv2[index]);
                        outColors?.Add(colors[index]);
                    }
                    into.Add(moved[index]);
                }
            }

            var mesh = new Mesh { name = src.name };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(outVerts);
            if (outNormals != null) mesh.SetNormals(outNormals);
            mesh.SetUVs(0, outUv);
            if (outUv2 != null) mesh.SetUVs(1, outUv2);
            if (outColors != null) mesh.SetColors(outColors);
            mesh.subMeshCount = paintTris.Count > 0 ? 2 : 1;
            mesh.SetTriangles(packTris, 0);
            if (paintTris.Count > 0) mesh.SetTriangles(paintTris, 1);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        /// <summary>Whether an atlas reading is one of these keys, within the 10 units
        /// either side that a bilinear sample off a 4096px texture can wander.</summary>
        static bool Matches(Vector3 rgb, Vector3[] keys)
        {
            foreach (var key in keys)
                if ((rgb - key).sqrMagnitude < 100f) return true;
            return false;
        }

        /// <summary>
        /// The machine's paints, one material per colour in VehiclePaint.TourerPalette,
        /// written into <see cref="PaintFolder"/> and returned in the palette's order -
        /// so the first of them is the graphite the prefab is saved wearing.
        ///
        /// Each is a copy of the pack's own vehicle material with one property changed,
        /// which is why the palette costs nothing: all of them point at the SAME 4096px
        /// atlas the machine was already loading. A material that is already there is
        /// re-copied from the pack rather than left alone, so a pack update reaches the
        /// paints on the next re-bake instead of leaving them on a stale shader.
        ///
        /// THE COLOUR SPACE IS ASKED FOR, NOT ASSUMED. A plain ShaderLab Color property
        /// is authored in sRGB and Unity converts it on the way to the GPU; an [HDR] one
        /// is taken as already linear and is not converted. Generic_Basic declares
        /// BaseColor HDR (m_ColorMode 1), so writing the palette's sRGB values straight
        /// in would give a machine far darker and duller than the swatch says - which is
        /// the kind of wrongness that looks like a lighting problem for a week. The
        /// shader is asked which it is and the value converted to match.
        /// </summary>
        static Material[] BakePaints()
        {
            var source = AssetDatabase.LoadAssetAtPath<Material>(PoliceMaterial);
            if (source == null)
            {
                Debug.LogError("[GangBike] no pack material at " + PoliceMaterial + "; no paints baked.");
                return new Material[0];
            }

            if (!AssetDatabase.IsValidFolder(PaintFolder))
                AssetDatabase.CreateFolder(Folder, "Tourer");

            var linear = WantsLinear(source.shader, "_BaseColor");
            var palette = LivingCity.Gameplay.VehiclePaint.TourerPalette;
            var made = new List<Material>(palette.Length);

            foreach (var paint in palette)
            {
                var file = LivingCity.Gameplay.VehiclePaint.TourerMaterialName(paint.Name);
                var path = PaintFolder + "/" + file + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                var fresh = material == null;
                if (fresh) material = new Material(source);
                else material.CopyPropertiesFromMaterial(source);

                material.name = file;
                material.SetColor("_BaseColor", linear ? paint.Tint.linear : paint.Tint);

                if (fresh) AssetDatabase.CreateAsset(material, path);
                else EditorUtility.SetDirty(material);
                made.Add(material);
            }

            return made.ToArray();
        }

        /// <summary>Whether this shader wants a linear value written into that colour -
        /// true for an [HDR] property, false for the ordinary kind Unity converts for us.
        /// A shader that has no such property at all answers false, which is the harmless
        /// way round: the write is then a no-op rather than a wrong colour.</summary>
        static bool WantsLinear(Shader shader, string property)
        {
            if (shader == null) return false;
            var index = shader.FindPropertyIndex(property);
            if (index < 0) return false;
            var flags = shader.GetPropertyFlags(index);
            return (flags & UnityEngine.Rendering.ShaderPropertyFlags.HDR) != 0
                   && QualitySettings.activeColorSpace == ColorSpace.Linear;
        }

        /// <summary>Gives this renderer the second material slot its new paint submesh
        /// draws through, keeping whatever the pack put in the first. Called only for
        /// meshes that actually grew one - a renderer with more slots than submeshes
        /// draws nothing extra, but it is a lie about the body and the next reader has
        /// to work out which.</summary>
        static void GivePaintSlot(MeshFilter filter, Material paint)
        {
            if (paint == null) return;
            var renderer = filter.GetComponent<MeshRenderer>();
            if (renderer == null) return;

            var slots = renderer.sharedMaterials;
            if (slots.Length >= 2) { slots[1] = paint; renderer.sharedMaterials = slots; return; }

            renderer.sharedMaterials = new[] { slots.Length > 0 ? slots[0] : null, paint };
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
