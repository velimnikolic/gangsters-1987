using System.Collections.Generic;
using System.IO;
using LivingCity.EditorTools;
using UnityEditor;
using UnityEngine;

namespace AirportDemo.EditorTools
{
    /// <summary>
    /// The airport no Synty pack ships. Every piece here is assembled out of pack
    /// modules and baked into one prefab through SyntyKitExtractor.BakeGroup, exactly
    /// the way the harbour bakes its freighters: hangars from the gang pack's
    /// industrial shell (whose sliding leaf is six metres square - the only door in
    /// any pack an aeroplane fits through), the terminal from the Generic Base kit
    /// with the Plaza's glass for its curtain wall, the control tower from a Base
    /// shaft under the prison pack's glazed watchtower cab, and the field furniture -
    /// windsock, PAPI, guidance signs, apron floodlight masts, the airfield lights
    /// themselves - from stretched pack pieces and a handful of generated boxes
    /// wearing pack materials.
    ///
    /// Everything is placed off measured bounds rather than assumed pivots: Unity
    /// mirrors X on import, so a wall the FBX draws from 0 to +3 arrives running 0 to
    /// -3, and AirportKit.RunOf reads which way each piece actually goes. Dimensions
    /// come from AirportSpec, shared with the runtime.
    ///
    /// Convention, as everywhere in this project: a baked building is built with its
    /// front on +Z and its ground at y = 0; the bake recentres the pivot on the
    /// footprint. The hangars, the FBO, the fire station and the freight shed front
    /// the ramp, so the builder turns them 180 degrees; the terminal fronts the kerb
    /// and goes down unturned.
    /// </summary>
    public static partial class AirportKitBash
    {
        // v2: the field grew to ADG III for the Simple Airport jets - a wider terminal,
        // a taller tower, and every dimension re-read from AirportSpec
        public const int Version = 2;
        const string KitDir = "Assets/CityKit/Airport";
        const string MeshDir = KitDir + "/Meshes";
        const string MatDir = KitDir + "/Materials";
        const string VersionPath = KitDir + "/AirportKitVersion.txt";

        [MenuItem("Tools/City/Catalog/Rebuild Airport Kit (Kit-Bash)", priority = 6)]
        public static void ForceBuild()
        {
            AssetDatabase.DeleteAsset(VersionPath);
            BuildIfStale();
        }

        /// <summary>Whether what is on disk is this version's.</summary>
        public static bool IsFresh()
        {
            var marker = AssetDatabase.LoadAssetAtPath<TextAsset>(VersionPath);
            return marker && marker.text.Trim() == Version.ToString();
        }

        public static void BuildIfStale()
        {
            if (IsFresh()) return;
            if (!AssetDatabase.LoadAssetAtPath<GameObject>(AirportKit.MetalWall) ||
                !AssetDatabase.LoadAssetAtPath<GameObject>(AirportKit.BaseWall))
            {
                Debug.LogWarning("[AirportKitBash] the gang pack's shed or the Generic Base kit is missing - no airport baked.");
                return;
            }

            EnsureFolders();
            var t0 = System.DateTime.Now;

            BuildBoxHangar(closed: true);
            BuildBoxHangar(closed: false);
            BuildMaintHangar();
            BuildFbo();
            BuildTerminal();
            BuildTower();
            BuildArff();
            BuildCargoShed();
            BuildFuelFarm();
            BuildGuardBooth();

            BuildWindsock();
            BuildPapi();
            BuildTaxiSign();
            BuildHoldSign();
            BuildApronMast();
            BuildAirStairs();
            BuildBaggageCart();
            BuildFuelBowser();
            BuildChock();
            BuildLights();

            File.WriteAllText(VersionPath, Version.ToString());
            AssetDatabase.ImportAsset(VersionPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[AirportKitBash] baked the airport under {KitDir} in {(System.DateTime.Now - t0).TotalSeconds:F1} s");
        }

        static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder("Assets/CityKit")) AssetDatabase.CreateFolder("Assets", "CityKit");
            if (!AssetDatabase.IsValidFolder(KitDir)) AssetDatabase.CreateFolder("Assets/CityKit", "Airport");
            if (!AssetDatabase.IsValidFolder(MeshDir)) AssetDatabase.CreateFolder(KitDir, "Meshes");
            if (!AssetDatabase.IsValidFolder(MatDir)) AssetDatabase.CreateFolder(KitDir, "Materials");
        }

        // ------------------------------------------------------------ paint

        static Material _metal, _concrete, _plaster, _glass, _white, _yellow, _black, _red, _orange, _green, _blue, _amber, _steel, _rust;

        static Material Metal => _metal ??= Load(AirportKit.MetalMat);
        static Material Concrete => _concrete ??= Load(AirportKit.GenericConcreteMat);
        static Material Plaster => _plaster ??= Load(AirportKit.GenericPlasterMat);
        static Material Glass => _glass ??= Load(AirportKit.GenericGlassMat);
        static Material White => _white ??= Tinted("airport-white", AirportKit.GenericPlasterMat, new Color(0.90f, 0.90f, 0.88f));
        static Material Yellow => _yellow ??= Tinted("airport-yellow", AirportKit.GenericPlasterMat, new Color(0.86f, 0.70f, 0.10f));
        static Material Black => _black ??= Tinted("airport-black", AirportKit.GenericPlasterMat, new Color(0.08f, 0.08f, 0.09f));
        static Material Red => _red ??= Tinted("airport-red", AirportKit.GenericPlasterMat, new Color(0.62f, 0.10f, 0.09f));
        static Material Orange => _orange ??= Tinted("airport-orange", AirportKit.GenericPlasterMat, new Color(0.88f, 0.36f, 0.06f));
        static Material Green => _green ??= Tinted("airport-green", AirportKit.GenericPlasterMat, new Color(0.11f, 0.45f, 0.22f));
        static Material Blue => _blue ??= Tinted("airport-blue", AirportKit.GenericPlasterMat, new Color(0.12f, 0.28f, 0.62f));
        static Material Amber => _amber ??= Tinted("airport-amber", AirportKit.GenericPlasterMat, new Color(0.85f, 0.55f, 0.06f));
        static Material Steel => _steel ??= Tinted("airport-steel", AirportKit.GenericConcreteMat, new Color(0.42f, 0.44f, 0.47f), 0.35f);
        static Material Rust => _rust ??= Tinted("airport-rust", AirportKit.GenericPlasterMat, new Color(0.44f, 0.26f, 0.15f));

        static Material Load(string path) => AssetDatabase.LoadAssetAtPath<Material>(path);

        /// <summary>A tinted copy of a pack material, saved once under the kit folder so
        /// the baked prefabs can reference it.</summary>
        static Material Tinted(string name, string sourcePath, Color colour, float smoothness = -1f)
        {
            var path = $"{MatDir}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing)
            {
                existing.SetColor("_BaseColor", colour);
                if (existing.HasProperty("_Color")) existing.SetColor("_Color", colour);
                if (smoothness >= 0f && existing.HasProperty("_Smoothness")) existing.SetFloat("_Smoothness", smoothness);
                EditorUtility.SetDirty(existing);
                return existing;
            }
            var src = AssetDatabase.LoadAssetAtPath<Material>(sourcePath);
            var mat = src ? new Material(src) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.name = name;
            mat.SetColor("_BaseColor", colour);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", colour);
            if (smoothness >= 0f && mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        /// <summary>Every opaque pack material on the piece swapped for ours; glass
        /// stays glass.</summary>
        static void Paint(GameObject piece, Material mat)
        {
            if (piece == null || mat == null) return;
            foreach (var r in piece.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    if (mats[i].name.IndexOf("Glass", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    mats[i] = mat;
                    changed = true;
                }
                if (changed) r.sharedMaterials = mats;
            }
        }

        // ------------------------------------------------------------ pieces

        static GameObject P(string path) => AssetDatabase.LoadAssetAtPath<GameObject>(path);

        static Transform Group(Transform parent, string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }

        static GameObject Put(Transform root, string path, Vector3 at, float yaw = 0f, Vector3? scale = null)
        {
            var prefab = P(path);
            if (prefab == null) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
            go.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, yaw, 0f));
            if (scale.HasValue) go.transform.localScale = scale.Value;
            return go;
        }

        /// <summary>A run of wall modules from A to B at height <paramref name="y"/>,
        /// turned so the piece's front (+Z) faces away from <paramref name="inside"/>.
        /// The harbour's WallRun, which is the only way to lay a Synty wall without
        /// guessing which way its pivot runs.</summary>
        static void WallRun(Transform root, string path, Vector3 a, Vector3 b, Vector3 inside, float y = 0f,
                            Material mat = null, System.Func<int, GameObject> variant = null)
        {
            var prefab = P(path);
            if (prefab == null) return;
            a.y = y; b.y = y;
            var run = AirportKit.RunOf(prefab);
            var d = b - a; d.y = 0f;
            if (d.sqrMagnitude < 1e-6f) return;
            float yaw = Vector3.SignedAngle(run, d, Vector3.up);
            var front = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            var mid = (a + b) * 0.5f;
            var outward = mid - inside; outward.y = 0f;
            bool flip = outward.sqrMagnitude > 1e-6f && Vector3.Dot(front, outward) < 0f;
            var group = Group(root, "run");
            AirportKit.LayRun(prefab, flip ? b : a, flip ? a : b, group, null, variant);
            if (mat != null) Paint(group.gameObject, mat);
        }

        /// <summary>One piece laid exactly from A to B (no stretching, no repeat) - a
        /// door leaf, a gate, a lintel.</summary>
        static GameObject One(Transform root, string path, Vector3 a, Vector3 b, Vector3 inside, float y = 0f, Material mat = null)
        {
            var prefab = P(path);
            if (prefab == null) return null;
            a.y = y; b.y = y;
            var run = AirportKit.RunOf(prefab);
            var d = b - a; d.y = 0f;
            float yaw = Vector3.SignedAngle(run, d, Vector3.up);
            var front = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            var outward = (a + b) * 0.5f - inside; outward.y = 0f;
            bool flip = outward.sqrMagnitude > 1e-6f && Vector3.Dot(front, outward) < 0f;
            var go = AirportKit.PlaceRun(prefab, flip ? b : a, flip ? a : b, root, fit: true);
            if (go != null && mat != null) Paint(go, mat);
            return go;
        }

        // ------------------------------------------------------------ generated shapes
        //
        // What no pack has at all: a hangar roof forty feet across, a fuel tank, a
        // windsock cone, a lamp lens. Boxes and cylinders wearing the same pack
        // material as the walls beside them, which is what the harbour's hulls are.

        static GameObject Mesh(Transform root, string name, Mesh mesh, Material mat, Vector3 at)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.localPosition = at;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        /// <summary>A box, centred on <paramref name="centre"/>.</summary>
        static GameObject Slab(Transform root, string name, Vector3 centre, Vector3 size, Material mat, float uvScale = 0.25f)
        {
            var m = new Mesh { name = name };
            var h = size * 0.5f;
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var uv = new List<Vector2>();
            var tris = new List<int>();
            void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, float w, float hgt)
            {
                int i = v.Count;
                v.Add(a); v.Add(b); v.Add(c); v.Add(d);
                for (int k = 0; k < 4; k++) n.Add(normal);
                uv.Add(new Vector2(0f, 0f)); uv.Add(new Vector2(w * uvScale, 0f));
                uv.Add(new Vector2(w * uvScale, hgt * uvScale)); uv.Add(new Vector2(0f, hgt * uvScale));
                tris.Add(i); tris.Add(i + 2); tris.Add(i + 1);
                tris.Add(i); tris.Add(i + 3); tris.Add(i + 2);
            }
            Face(new Vector3(-h.x, -h.y, h.z), new Vector3(h.x, -h.y, h.z), new Vector3(h.x, h.y, h.z), new Vector3(-h.x, h.y, h.z), Vector3.forward, size.x, size.y);
            Face(new Vector3(h.x, -h.y, -h.z), new Vector3(-h.x, -h.y, -h.z), new Vector3(-h.x, h.y, -h.z), new Vector3(h.x, h.y, -h.z), Vector3.back, size.x, size.y);
            Face(new Vector3(h.x, -h.y, h.z), new Vector3(h.x, -h.y, -h.z), new Vector3(h.x, h.y, -h.z), new Vector3(h.x, h.y, h.z), Vector3.right, size.z, size.y);
            Face(new Vector3(-h.x, -h.y, -h.z), new Vector3(-h.x, -h.y, h.z), new Vector3(-h.x, h.y, h.z), new Vector3(-h.x, h.y, -h.z), Vector3.left, size.z, size.y);
            Face(new Vector3(-h.x, h.y, h.z), new Vector3(h.x, h.y, h.z), new Vector3(h.x, h.y, -h.z), new Vector3(-h.x, h.y, -h.z), Vector3.up, size.x, size.z);
            Face(new Vector3(-h.x, -h.y, -h.z), new Vector3(h.x, -h.y, -h.z), new Vector3(h.x, -h.y, h.z), new Vector3(-h.x, -h.y, h.z), Vector3.down, size.x, size.z);
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return Mesh(root, name, m, mat, centre);
        }

        /// <summary>A cylinder about the Y axis, its foot at <paramref name="foot"/>.</summary>
        static GameObject Tube(Transform root, string name, Vector3 foot, float radius, float height, Material mat, int sides = 12, bool cap = true)
        {
            var m = new Mesh { name = name };
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var uv = new List<Vector2>();
            var tris = new List<int>();
            for (int i = 0; i < sides; i++)
            {
                float a0 = i / (float)sides * Mathf.PI * 2f, a1 = (i + 1) / (float)sides * Mathf.PI * 2f;
                var p0 = new Vector3(Mathf.Cos(a0) * radius, 0f, Mathf.Sin(a0) * radius);
                var p1 = new Vector3(Mathf.Cos(a1) * radius, 0f, Mathf.Sin(a1) * radius);
                var nm = (p0 + p1).normalized;
                int b = v.Count;
                v.Add(p0); v.Add(p1); v.Add(p1 + Vector3.up * height); v.Add(p0 + Vector3.up * height);
                for (int k = 0; k < 4; k++) n.Add(nm);
                uv.Add(new Vector2(i / (float)sides, 0f)); uv.Add(new Vector2((i + 1) / (float)sides, 0f));
                uv.Add(new Vector2((i + 1) / (float)sides, 1f)); uv.Add(new Vector2(i / (float)sides, 1f));
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
                tris.Add(b); tris.Add(b + 3); tris.Add(b + 2);
            }
            if (cap)
            {
                for (int end = 0; end < 2; end++)
                {
                    float y = end == 0 ? 0f : height;
                    var nm = end == 0 ? Vector3.down : Vector3.up;
                    int centre = v.Count;
                    v.Add(new Vector3(0f, y, 0f)); n.Add(nm); uv.Add(new Vector2(0.5f, 0.5f));
                    for (int i = 0; i <= sides; i++)
                    {
                        float a = i / (float)sides * Mathf.PI * 2f;
                        v.Add(new Vector3(Mathf.Cos(a) * radius, y, Mathf.Sin(a) * radius));
                        n.Add(nm);
                        uv.Add(new Vector2(0.5f + Mathf.Cos(a) * 0.5f, 0.5f + Mathf.Sin(a) * 0.5f));
                    }
                    for (int i = 0; i < sides; i++)
                    {
                        if (end == 0) { tris.Add(centre); tris.Add(centre + i + 1); tris.Add(centre + i + 2); }
                        else { tris.Add(centre); tris.Add(centre + i + 2); tris.Add(centre + i + 1); }
                    }
                }
            }
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return Mesh(root, name, m, mat, foot);
        }

        /// <summary>A gable roof over the rectangle, ridge running along X, with its two
        /// slopes, its two ends and a little overhang - a hangar roof.</summary>
        static void Gable(Transform root, string name, float x0, float x1, float z0, float z1,
                          float eaveY, float ridgeRise, Material mat, float overhang = 0.4f)
        {
            x0 -= overhang; x1 += overhang; z0 -= overhang; z1 += overhang;
            float zc = (z0 + z1) * 0.5f, ridgeY = eaveY + ridgeRise;
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var uv = new List<Vector2>();
            var tris = new List<int>();
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                int i = v.Count;
                var nm = Vector3.Cross(b - a, d - a).normalized;
                v.Add(a); v.Add(b); v.Add(c); v.Add(d);
                for (int k = 0; k < 4; k++) n.Add(nm);
                uv.Add(new Vector2(a.x * 0.2f, a.z * 0.2f)); uv.Add(new Vector2(b.x * 0.2f, b.z * 0.2f));
                uv.Add(new Vector2(c.x * 0.2f, c.z * 0.2f)); uv.Add(new Vector2(d.x * 0.2f, d.z * 0.2f));
                tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
                tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
            }
            void Tri(Vector3 a, Vector3 b, Vector3 c)
            {
                int i = v.Count;
                var nm = Vector3.Cross(b - a, c - a).normalized;
                v.Add(a); v.Add(b); v.Add(c);
                for (int k = 0; k < 3; k++) n.Add(nm);
                uv.Add(new Vector2(a.x * 0.2f, a.y * 0.2f)); uv.Add(new Vector2(b.x * 0.2f, b.y * 0.2f)); uv.Add(new Vector2(c.x * 0.2f, c.y * 0.2f));
                tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
            }
            var eN0 = new Vector3(x0, eaveY, z1); var eN1 = new Vector3(x1, eaveY, z1);
            var eS0 = new Vector3(x0, eaveY, z0); var eS1 = new Vector3(x1, eaveY, z0);
            var r0 = new Vector3(x0, ridgeY, zc); var r1 = new Vector3(x1, ridgeY, zc);
            Quad(eN1, eN0, r0, r1);   // north slope
            Quad(eS0, eS1, r1, r0);   // south slope
            Tri(eS0, r0, eN0);        // west gable
            Tri(eN1, r1, eS1);        // east gable
            // the soffit, so the roof is not paper from below
            Quad(eS0, eN0, eN1, eS1);
            var m = new Mesh { name = name };
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            Mesh(root, name, m, mat, Vector3.zero);
        }

        /// <summary>A flat slab of roof with a parapet round it - what a terminal or an
        /// office has.</summary>
        static void FlatRoof(Transform root, string name, float x0, float x1, float z0, float z1, float y,
                             Material deck, Material parapet, float parapetHeight = 0.9f, float thickness = 0.25f)
        {
            Slab(root, name, new Vector3((x0 + x1) * 0.5f, y - thickness * 0.5f, (z0 + z1) * 0.5f),
                 new Vector3(x1 - x0, thickness, z1 - z0), deck);
            float t = 0.28f;
            Slab(root, name + " parapet N", new Vector3((x0 + x1) * 0.5f, y + parapetHeight * 0.5f, z1 - t * 0.5f), new Vector3(x1 - x0, parapetHeight, t), parapet);
            Slab(root, name + " parapet S", new Vector3((x0 + x1) * 0.5f, y + parapetHeight * 0.5f, z0 + t * 0.5f), new Vector3(x1 - x0, parapetHeight, t), parapet);
            Slab(root, name + " parapet E", new Vector3(x1 - t * 0.5f, y + parapetHeight * 0.5f, (z0 + z1) * 0.5f), new Vector3(t, parapetHeight, z1 - z0 - t * 2f), parapet);
            Slab(root, name + " parapet W", new Vector3(x0 + t * 0.5f, y + parapetHeight * 0.5f, (z0 + z1) * 0.5f), new Vector3(t, parapetHeight, z1 - z0 - t * 2f), parapet);
        }

        /// <summary>A slab floor - the concrete an assembly stands on, so a shed is not
        /// see-through when the camera looks in at its open door.</summary>
        static void Floor(Transform root, float x0, float x1, float z0, float z1, float y, Material mat)
            => Slab(root, "floor", new Vector3((x0 + x1) * 0.5f, y - 0.06f, (z0 + z1) * 0.5f), new Vector3(x1 - x0, 0.12f, z1 - z0), mat);

        /// <summary>The block alphabet, painted on a face: one quad per run of cells.
        /// Used for the taxiway guidance boards and the fire station's number.</summary>
        static void Legend(Transform root, string text, Vector3 centre, float height, Material mat,
                           float yaw = 0f, float depth = 0.03f)
        {
            if (string.IsNullOrEmpty(text)) return;
            float cell = height / AirportKit.Glyph.Rows;
            float glyphW = cell * AirportKit.Glyph.Cols;
            float gap = cell;
            float total = text.Length * glyphW + (text.Length - 1) * gap;
            var runs = new List<Vector2Int>();
            var turn = Quaternion.Euler(0f, yaw, 0f);
            for (int i = 0; i < text.Length; i++)
            {
                float gx = -total * 0.5f + i * (glyphW + gap);
                for (int row = 0; row < AirportKit.Glyph.Rows; row++)
                {
                    AirportKit.Glyph.RowRuns(text[i], row, runs);
                    foreach (var r in runs)
                    {
                        float w = (r.y - r.x) * cell;
                        var local = new Vector3(gx + r.x * cell + w * 0.5f,
                                                height * 0.5f - (row + 0.5f) * cell, 0f);
                        Slab(root, "legend", centre + turn * local, new Vector3(w, cell, depth), mat)
                            .transform.localRotation = turn;
                    }
                }
            }
        }

        // ------------------------------------------------------------ baking

        /// <summary>Bakes the assembled group into one prefab and clears the scratch.</summary>
        static void Bake(GameObject group, string name)
        {
            SyntyKitExtractor.BakeGroup(group, name, yaw: 0f, KitDir, MeshDir);
            Object.DestroyImmediate(group);
        }

        static GameObject Scratch(string name)
        {
            var go = new GameObject(name);
            go.transform.position = Vector3.zero;
            return go;
        }
    }
}
