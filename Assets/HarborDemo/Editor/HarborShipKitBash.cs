using System.Collections.Generic;
using System.IO;
using LivingCity.EditorTools;
using UnityEditor;
using UnityEngine;

namespace HarborDemo.EditorTools
{
    /// <summary>
    /// The freighters no Synty pack ships: her shell lofted from a set of levels
    /// (HarborShipKitBash.Hull.cs) so that she has the curves a hull has - the turn of
    /// the bilge, flaring topsides, a raked stem, a counter stern and the sheer running
    /// up to the bow - and everything standing on that shell kit-bashed out of the
    /// Generic pack's Base modules: a deckhouse of window walls aft, plain quads for the
    /// deck plates and the hatch covers, pillars stretched into every post, beam, funnel
    /// and jib. It all bakes into one mesh through SyntyKitExtractor.BakeGroup like
    /// every other kit building. The boxes they carry are the Gang Warfare pack's metal
    /// warehouse walls closed into a 6 x 3 x 3 container, in five colours.
    ///
    /// A hull that is only a shell and a deck reads as a barge, so she also carries the
    /// working detail a ship is recognised by: a raised forecastle over the bow taper
    /// with its break, ladder and windlass; a boot-top band at the waterline and a
    /// rubbing strake up the topsides, both lofted into the shell so nothing z-fights,
    /// with frames between them; a white capping rail all round the bulwark, following
    /// the sheer; hawse pipes, stowed anchors, draft marks and her name in blocks;
    /// pipe runs, vents, bitts and winches along the deck; a deckhouse with bridge
    /// wings out over the water, nav lights at their ends, outside stairs aft, boats
    /// in davits, a banded funnel with its pipes and a radar mast; and her cargo gear -
    /// deck cranes on the newer hulls, kingposts and derricks on the older ones.
    ///
    /// Everything is placed off measured bounds, not guessed pivots: HarborKit.RunOf
    /// reads a wall's run off the prefab, PlaceRun turns it to lie along an edge and
    /// stretches it to fit, Block/Bar stretch the Base pillar into any box or beam,
    /// and Fit scales a loose prop to the size the ship wants - so a change of kit
    /// piece is a change of path constant. Colours are copies of the packs' own
    /// materials with _BaseColor set - the Generic_Basic shader multiplies it into
    /// the albedo - saved under Assets/CityKit/Ships/Materials so the baked prefabs
    /// can reference them.
    ///
    /// The bake recentres a prefab on its own footprint, so every fitting is placed in
    /// pairs port and starboard: anything hung on one side alone would shift the pivot
    /// off the hull's centreline and the shipping's numbers with it.
    ///
    /// HarborShipSpec holds every dimension, shared with the runtime.
    /// </summary>
    public static partial class HarborShipKitBash
    {
        public const int Version = 6;
        const string ShipsDir = "Assets/CityKit/Ships";
        const string MeshDir = ShipsDir + "/Meshes";
        const string MatDir = ShipsDir + "/Materials";
        const string VersionPath = ShipsDir + "/ShipKitVersion.txt";

        const string Base = HarborKit.GenBase;
        const string Wall = Base + "SM_Bld_Base_Wall_01.prefab";
        const string WallHalf = Base + "SM_Bld_Base_Wall_Half_01.prefab";
        const string WallWindow = Base + "SM_Bld_Base_Wall_Window_01.prefab";
        const string WallDoor = Base + "SM_Bld_Base_Wall_Door_01.prefab";
        const string Pillar = Base + "SM_Bld_Base_Pillar_01.prefab";
        const string PlasterMat = "Assets/Synty/PolygonGeneric/Materials/Generic_Plaster.mat";
        const string ConcreteMat = "Assets/Synty/PolygonGeneric/Materials/Generic_Concrete.mat";

        const string GangBld = HarborKit.GangBld;
        const string MetalWall = GangBld + "SM_Bld_Wall_Metal_01.prefab";
        const string MetalDoor = GangBld + "SM_Bld_Wall_Metal_Door_Slide_01.prefab";
        const string MetalRoof = GangBld + "SM_Bld_Roof_Flat_Open_01.prefab";
        const string GangMat = "Assets/Synty/PolygonGangWarfare/Materials/Alts/PolygonGangWarfare_01_A.mat";

        // the loose pieces the ships borrow: a railing by the metre, a ladder, a cable
        // reel and a deck vent, the bridge's dish, and a boat for the davits
        const string Railing = HarborKit.PalmBld + "SM_Bld_Railing_01_Straight_01.prefab";
        const string Ladder = HarborKit.GangProps + "SM_Prop_Ladder_01.prefab";
        const string Wirespool = HarborKit.GangProps + "SM_Prop_Wirespool_01.prefab";
        const string AirVent = HarborKit.GangProps + "SM_Prop_AirVent_01.prefab";
        const string SatDish = HarborKit.CityProps + "SM_Prop_SatDish_01.prefab";
        const string Lifeboat = HarborKit.PalmVeh + "SM_Veh_RIB_Boat_01.prefab";

        const string AnchorChain = HarborKit.GenProps + "SM_Gen_Prop_Chain_Anchor_01.prefab";
        const string Rope = HarborKit.GenProps + "SM_Gen_Prop_Rope_01.prefab";
        const string Crate = HarborKit.GenProps + "SM_Gen_Prop_Crate_02.prefab";
        const string Barrel = HarborKit.GangProps + "SM_Prop_Barrel_Metal_01.prefab";
        const string RescueBuoy = HarborKit.PalmProps + "SM_Prop_Rescue_Buoy_01.prefab";

        static readonly (string name, Color colour)[] BoxColours =
        {
            ("red", new Color(0.48f, 0.19f, 0.14f)),
            ("blue", new Color(0.18f, 0.29f, 0.36f)),
            ("green", new Color(0.23f, 0.34f, 0.28f)),
            ("rust", new Color(0.55f, 0.32f, 0.16f)),
            ("white", new Color(0.82f, 0.82f, 0.80f)),
        };

        [MenuItem("Tools/City/Catalog/Rebuild Harbor Ships (Kit-Bash)", priority = 4)]
        public static void ForceBuild()
        {
            AssetDatabase.DeleteAsset(VersionPath);
            BuildIfStale();
        }

        /// <summary>Whether the fleet on disk is this version's.</summary>
        public static bool IsFresh()
        {
            var marker = AssetDatabase.LoadAssetAtPath<TextAsset>(VersionPath);
            return marker && marker.text.Trim() == Version.ToString();
        }

        public static void BuildIfStale()
        {
            if (IsFresh()) return;
            if (!AssetDatabase.LoadAssetAtPath<GameObject>(Wall))
            {
                Debug.LogWarning("[HarborShipKitBash] The Generic pack's Base kit is missing - no ships baked.");
                return;
            }

            EnsureFolders();
            foreach (var (name, colour) in BoxColours)
                BuildContainer("container-20-" + name, colour);
            foreach (var spec in HarborShipSpec.All)
                BuildShip(spec);

            File.WriteAllText(VersionPath, Version.ToString());
            AssetDatabase.ImportAsset(VersionPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[HarborShipKitBash] baked the harbor fleet and its boxes under " + ShipsDir);
        }

        static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder("Assets/CityKit")) AssetDatabase.CreateFolder("Assets", "CityKit");
            if (!AssetDatabase.IsValidFolder(ShipsDir)) AssetDatabase.CreateFolder("Assets/CityKit", "Ships");
            if (!AssetDatabase.IsValidFolder(MeshDir)) AssetDatabase.CreateFolder(ShipsDir, "Meshes");
            if (!AssetDatabase.IsValidFolder(MatDir)) AssetDatabase.CreateFolder(ShipsDir, "Materials");
        }

        // ------------------------------------------------------------ materials

        /// <summary>One ship's paint locker. The hull colours are hers alone; steel,
        /// boat orange and the two nav lights are shared by the whole fleet.</summary>
        sealed class Paints
        {
            public Material HullUpper, HullLower, Boot, Strake, House, Deck, Funnel, Trim, Mast, Steel, Boat, Rust, NavRed, NavGreen, Glass;
        }

        static Paints PaintsFor(HarborShipSpec s) => new Paints
        {
            HullUpper = Tinted(s.Name + "-hull", PlasterMat, s.HullUpper, 0.35f),
            HullLower = Tinted(s.Name + "-antifoul", PlasterMat, s.HullLower, 0.2f),
            Boot = Tinted(s.Name + "-boot", PlasterMat, s.Boot, 0.3f),
            Strake = Tinted(s.Name + "-strake", PlasterMat, s.Strake, 0.3f),
            House = Tinted(s.Name + "-house", PlasterMat, s.House, 0.2f),
            Deck = Tinted(s.Name + "-deck", ConcreteMat, s.Deck, 0.1f),
            Funnel = Tinted(s.Name + "-funnel", PlasterMat, s.Funnel, 0.3f),
            Trim = Tinted(s.Name + "-trim", PlasterMat, s.Trim, 0.25f),
            Mast = Tinted(s.Name + "-mast", PlasterMat, s.Mast, 0.3f),
            Steel = Tinted("ship-steel", ConcreteMat, new Color(0.30f, 0.31f, 0.33f), 0.3f),
            Boat = Tinted("ship-boat", PlasterMat, new Color(0.86f, 0.37f, 0.07f), 0.3f),
            Rust = Tinted("ship-rust", PlasterMat, new Color(0.33f, 0.18f, 0.11f), 0.12f),
            Glass = Tinted("ship-bridge-glass", PlasterMat, new Color(0.055f, 0.13f, 0.17f), 0.65f),
            NavRed = Tinted("ship-nav-red", PlasterMat, new Color(0.62f, 0.09f, 0.08f), 0.45f),
            NavGreen = Tinted("ship-nav-green", PlasterMat, new Color(0.10f, 0.46f, 0.22f), 0.45f),
        };

        /// <summary>A tinted copy of a pack material, saved once under the ships folder.</summary>
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

        /// <summary>Every opaque pack material on the piece swapped for ours - the
        /// glass in a window stays glass.</summary>
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

        /// <summary>A run of wall modules from A to B, stretched to fit, painted, and
        /// turned so the modules' front (+Z) faces away from <paramref name="inside"/>.</summary>
        static void WallRun(Transform root, GameObject prefab, Vector3 a, Vector3 b, Vector3 inside, Material mat,
                            System.Func<int, GameObject> variant = null)
        {
            if (prefab == null) return;
            // which way round: the piece's front after PlaceRun's yaw is its local +Z
            // turned with the run; test that against the outward side of the edge
            var run = HarborKit.RunOf(prefab);
            var d = b - a; d.y = 0f;
            float yaw = Vector3.SignedAngle(run, d, Vector3.up);
            var front = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            var mid = (a + b) * 0.5f;
            var outward = mid - inside; outward.y = 0f;
            bool flip = Vector3.Dot(front, outward) < 0f;
            var from = flip ? b : a;
            var to = flip ? a : b;
            var group = new GameObject("run").transform;
            group.SetParent(root, false);
            HarborKit.LayRun(prefab, from, to, group, null, variant);
            Paint(group.gameObject, mat);
        }

        /// <summary>A flat plate: fan-triangulated polygon at a height, facing up (or
        /// down, for a bottom nothing may see through).</summary>
        static void Plate(Transform root, string name, IList<Vector2> outline, float y, Material mat, bool up = true)
        {
            var verts = new Vector3[outline.Count];
            for (int i = 0; i < verts.Length; i++) verts[i] = new Vector3(outline[i].x, y, outline[i].y);
            var tris = new List<int>();
            for (int i = 1; i < verts.Length - 1; i++) { tris.Add(0); tris.Add(i); tris.Add(i + 1); }
            var n = Vector3.Cross(verts[1] - verts[0], verts[2] - verts[0]);
            if (up ? n.y < 0f : n.y > 0f) tris.Reverse();
            var mesh = new Mesh { name = name };
            mesh.vertices = verts;
            mesh.triangles = tris.ToArray();
            var uv = new Vector2[verts.Length];
            for (int i = 0; i < uv.Length; i++) uv[i] = new Vector2(verts[i].x * 0.25f, verts[i].z * 0.25f);
            mesh.uv = uv;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        /// <summary>A ring point on the plan, for the plates cut to it.</summary>
        static Vector2 Flat(Vector3 v) => new Vector2(v.x, v.z);

        static void Quad(Transform root, string name, float x0, float x1, float z0, float z1, float y, Material mat)
            => Plate(root, name, new[] { new Vector2(x0, z0), new Vector2(x0, z1), new Vector2(x1, z1), new Vector2(x1, z0) }, y, mat);

        /// <summary>A pillar stretched into a post: pivot at its base.</summary>
        static GameObject Post(Transform root, Vector3 at, float width, float height, Material mat, Vector3? euler = null)
        {
            var prefab = P(Pillar);
            if (prefab == null) return null;
            var b = HarborKit.PrefabBounds(prefab);
            var go = Object.Instantiate(prefab, at, Quaternion.Euler(euler ?? Vector3.zero), root);
            go.name = "post";
            go.transform.localScale = new Vector3(width / Mathf.Max(0.05f, b.size.x), height / Mathf.Max(0.05f, b.size.y), width / Mathf.Max(0.05f, b.size.z));
            Paint(go, mat);
            return go;
        }

        /// <summary>The same pillar stretched into a box of any three sizes, standing on
        /// its base - deckhouse blocks, crane bodies, funnel casings, winch drums.</summary>
        static GameObject Block(Transform root, Vector3 at, Vector3 size, Material mat, float yaw = 0f)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.name = "Steel plate";
            go.transform.SetParent(root, false);
            go.transform.localPosition = at + Vector3.up * (size.y * 0.5f);
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        /// <summary>A beam of that pillar laid from A to B whichever way they lie -
        /// rails, jibs, booms, stays, stringers. Width is across the run and level,
        /// thickness the other way.</summary>
        static GameObject Bar(Transform root, Vector3 from, Vector3 to, float width, float thick, Material mat)
        {
            var prefab = P(Pillar);
            if (prefab == null) return null;
            var d = to - from;
            float len = d.magnitude;
            if (len < 0.02f) return null;
            var dir = d / len;
            // LookRotation puts local +Z on the run; the pitch after it turns the
            // pillar's length (+Y) onto the run and leaves +X across it
            var up = Mathf.Abs(dir.y) > 0.99f ? Vector3.forward : Vector3.up;
            var rot = Quaternion.LookRotation(dir, up) * Quaternion.Euler(90f, 0f, 0f);
            var b = HarborKit.PrefabBounds(prefab);
            var go = Object.Instantiate(prefab, from, rot, root);
            go.name = "bar";
            go.transform.localScale = new Vector3(width / Mathf.Max(0.05f, b.size.x),
                                                  len / Mathf.Max(0.05f, b.size.y),
                                                  thick / Mathf.Max(0.05f, b.size.z));
            Paint(go, mat);
            return go;
        }

        /// <summary>Railing modules laid along a line, painted. Falls back to a plain
        /// top rail on a pack that has no railing.</summary>
        static void RailRun(Transform root, Vector3 a, Vector3 b, Material mat)
        {
            var prefab = P(Railing);
            if (prefab == null)
            {
                Bar(root, a + Vector3.up * 0.95f, b + Vector3.up * 0.95f, 0.1f, 0.1f, mat);
                return;
            }
            var group = new GameObject("rail").transform;
            group.SetParent(root, false);
            HarborKit.LayRun(prefab, a, b, group);
            Paint(group.gameObject, mat);
        }

        /// <summary>A loose prop scaled to stand a given height, sat on its own base.</summary>
        static GameObject Fit(Transform root, string path, Vector3 at, float yaw, float height, Material mat = null)
        {
            var prefab = P(path);
            if (prefab == null) return null;
            var b = HarborKit.PrefabBounds(prefab);
            float k = b.size.y > 0.01f ? height / b.size.y : 1f;
            var go = Object.Instantiate(prefab, at - Vector3.up * (b.min.y * k), Quaternion.Euler(0f, yaw, 0f), root);
            go.name = prefab.name;
            go.transform.localScale = Vector3.one * k;
            if (mat != null) Paint(go, mat);
            return go;
        }

        /// <summary>The same, scaled by its length rather than its height - a boat.</summary>
        static GameObject FitLong(Transform root, string path, Vector3 at, float yaw, float length, Material mat = null)
        {
            var prefab = P(path);
            if (prefab == null) return null;
            var b = HarborKit.PrefabBounds(prefab);
            float run = Mathf.Max(b.size.x, b.size.z);
            float k = run > 0.01f ? length / run : 1f;
            var go = Object.Instantiate(prefab, at - Vector3.up * (b.min.y * k), Quaternion.Euler(0f, yaw, 0f), root);
            go.name = prefab.name;
            go.transform.localScale = Vector3.one * k;
            if (mat != null) Paint(go, mat);
            return go;
        }

        static void Prop(Transform root, string path, Vector3 at, float yaw, float scale = 1f)
        {
            var prefab = P(path);
            if (prefab == null) return;
            var go = Object.Instantiate(prefab, at, Quaternion.Euler(0f, yaw, 0f), root);
            go.transform.localScale = Vector3.one * scale;
        }

        // ------------------------------------------------------------ flat work
        //
        // The painted detail - waterline bands, strakes, hawse pipes, draft marks, the
        // name in blocks - is flat geometry a hair proud of the plating rather than
        // texture: the kit has no decals and the bake would flatten a projector anyway.

        /// <summary>Quads gathered into one mesh, each wound to face where it is told.</summary>
        sealed class Facets
        {
            readonly List<Vector3> _verts = new List<Vector3>();
            readonly List<Vector2> _uvs = new List<Vector2>();
            readonly List<int> _tris = new List<int>();

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
                => Quad(a, b, c, d, outward, 0f, (b - a).magnitude * 0.25f, 0f, (d - a).magnitude * 0.25f);

            /// <summary>The same, with the texture's span given - a skin lofted quad by
            /// quad wants its uv to run on down the hull, not restart at every panel.</summary>
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward, float u0, float u1, float v0, float v1)
            {
                int i = _verts.Count;
                _verts.Add(a); _verts.Add(b); _verts.Add(c); _verts.Add(d);
                _uvs.Add(new Vector2(u0, v0)); _uvs.Add(new Vector2(u1, v0));
                _uvs.Add(new Vector2(u1, v1)); _uvs.Add(new Vector2(u0, v1));
                bool flip = Vector3.Dot(Vector3.Cross(b - a, d - a), outward) < 0f;
                if (flip) { _tris.AddRange(new[] { i, i + 3, i + 2, i, i + 2, i + 1 }); }
                else { _tris.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 }); }
            }

            public void Emit(Transform root, string name, Material mat)
            {
                if (_verts.Count == 0) return;
                var mesh = new Mesh { name = name };
                mesh.SetVertices(_verts);
                mesh.SetUVs(0, _uvs);
                mesh.SetTriangles(_tris, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                var go = new GameObject(name);
                go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            }
        }

        /// <summary>One flat mark on a hull side: centre, which way it faces, how big.</summary>
        static void Mark(Transform root, string name, Vector3 centre, Vector3 face, float width, float height, Material mat)
        {
            var right = Vector3.Cross(Vector3.up, face).normalized;
            var facets = new Facets();
            var w = right * width * 0.5f;
            var h = Vector3.up * height * 0.5f;
            facets.Quad(centre - w - h, centre + w - h, centre + w + h, centre - w + h, face);
            facets.Emit(root, name, mat);
        }

        // ------------------------------------------------------------ the ships

        static void BuildShip(HarborShipSpec s)
        {
            var paints = PaintsFor(s);
            var root = new GameObject(s.Name);
            var t = root.transform;
            try
            {
                var stands = DeckStands(s);       // where the deck has room for a fitting
                var rings = Hull(t, s, paints);   // the lofted shell, and the deck line it cuts
                Decks(t, s, paints, rings);
                DeckFittings(t, s, paints, stands);
                Deckhouse(t, s, paints);
                CargoGear(t, s, paints);
                Dressing(t, s, paints, stands);
                SyntyKitExtractor.BakeGroup(root, s.Name, yaw: 0f, outputDir: ShipsDir, meshOutputDir: MeshDir);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>The decks, cut to the shell's own deck line: the main plate abaft
        /// the forecastle break, the forecastle over the bow taper with its break wall,
        /// ladder and railing, and the hatches - coaming, ribbed cover, cleats.</summary>
        static void Decks(Transform t, HarborShipSpec s, Paints p, Rings rings)
        {
            var half = P(WallHalf);
            int n = rings.DeckStar.Length, k = rings.Break;
            bool forecastle = s.ForecastleRise > 0.1f && k > 0 && k < n - 2;

            var main = new List<Vector2>();
            for (int i = 0; i <= (forecastle ? k : n - 1); i++) main.Add(Flat(rings.DeckStar[i]));
            for (int i = (forecastle ? k : n - 2); i >= 0; i--) main.Add(Flat(rings.DeckPort[i]));
            Plate(t, "deck", main, s.DeckY, p.Deck);

            if (forecastle)
            {
                // the forecastle is a deck above the deck line, so it is cut to the
                // shell's width at its own height - the plating flares out to there
                float fy = s.ForecastleY;
                var head = new List<Vector2>();
                for (int i = k; i < n; i++) head.Add(new Vector2(ShellHalf(s, fy, rings.DeckStar[i].z), rings.DeckStar[i].z));
                for (int i = n - 2; i >= k; i--) head.Add(new Vector2(-ShellHalf(s, fy, rings.DeckPort[i].z), rings.DeckPort[i].z));
                float fz = rings.DeckStar[k].z;
                float bw = ShellHalf(s, fy, fz);
                Plate(t, "forecastle", head, fy, p.Deck);
                // the break: a wall across the ship facing aft, capped and railed
                WallRun(t, half, new Vector3(-bw, s.DeckY, fz), new Vector3(bw, s.DeckY, fz),
                        new Vector3(0f, 0f, fz + 5f), p.HullUpper);
                Bar(t, new Vector3(-bw, fy, fz), new Vector3(bw, fy, fz), 0.4f, 0.16f, p.Trim);
                RailRun(t, new Vector3(-bw + 0.4f, fy, fz + 0.25f), new Vector3(-1.5f, fy, fz + 0.25f), p.Trim);
                RailRun(t, new Vector3(bw - 0.4f, fy, fz + 0.25f), new Vector3(1.5f, fy, fz + 0.25f), p.Trim);
                // the ladder up to it, in the gap between the two rails
                for (int side = -1; side <= 1; side += 2)
                    Bar(t, new Vector3(side * 0.7f, s.DeckY, fz - 1.7f), new Vector3(side * 0.7f, fy + 0.1f, fz - 0.1f), 0.12f, 0.12f, p.Steel);
                for (int step = 0; step < 4; step++)
                {
                    float f = (step + 0.5f) / 4f;
                    float y = s.DeckY + f * s.ForecastleRise;
                    float z = fz - 1.7f + f * 1.6f;
                    Bar(t, new Vector3(-0.7f, y, z), new Vector3(0.7f, y, z), 0.1f, 0.08f, p.Steel);
                }
            }

            foreach (var h in s.Hatches)
            {
                var hc = new Vector3(h.center.x, 0f, h.center.y);
                var c0 = new Vector3(h.xMin, s.DeckY, h.yMin);
                var c1 = new Vector3(h.xMin, s.DeckY, h.yMax);
                var c2 = new Vector3(h.xMax, s.DeckY, h.yMax);
                var c3 = new Vector3(h.xMax, s.DeckY, h.yMin);
                WallRun(t, half, c0, c1, hc, p.Steel);
                WallRun(t, half, c1, c2, hc, p.Steel);
                WallRun(t, half, c2, c3, hc, p.Steel);
                WallRun(t, half, c3, c0, hc, p.Steel);
                float top = s.DeckY + HarborShipSpec.HatchCoaming;
                Quad(t, "hatch", h.xMin, h.xMax, h.yMin, h.yMax, top, p.Steel);
                // the cover's pontoon joints, and the cleats down the coaming
                int ribs = Mathf.Max(2, Mathf.RoundToInt(h.height / 3.5f));
                for (int rib = 1; rib < ribs; rib++)
                {
                    float z = Mathf.Lerp(h.yMin, h.yMax, rib / (float)ribs);
                    Bar(t, new Vector3(h.xMin, top + 0.07f, z), new Vector3(h.xMax, top + 0.07f, z), 0.5f, 0.16f, p.Strake);
                }
                int cleats = Mathf.Max(2, Mathf.RoundToInt(h.height / 2.6f));
                for (int cleat = 0; cleat < cleats; cleat++)
                {
                    float z = Mathf.Lerp(h.yMin + 0.8f, h.yMax - 0.8f, cleat / (cleats - 1f));
                    for (int side = -1; side <= 1; side += 2)
                        Block(t, new Vector3(h.center.x + side * (h.width * 0.5f + 0.16f), s.DeckY + 0.4f, z),
                              new Vector3(0.24f, 0.5f, 0.3f), p.Trim);
                }
            }
        }

        // ------------------------------------------------------------ the boxes

        /// <summary>A twenty-foot box: two metal wall modules a side, a slide door for
        /// the end, two roof slabs; 6 m along its own +Z, 3 wide, 3 high, pivot at the
        /// footprint's centre on the ground - what the bake makes of it.</summary>
        // The fabricated box has ordinary UVs, not the old GangWarfare atlas coordinates.
        // Reset existing materials as well so an upgraded checkout matches a clean bake.
        static Material ContainerPaint(string name, Color colour, float smoothness)
        {
            var clean = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            clean.SetColor("_BaseColor", colour);
            clean.SetFloat("_Smoothness", smoothness);
            clean.SetFloat("_Metallic", 0.12f);
            string path = $"{MatDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                clean.name = name;
                AssetDatabase.CreateAsset(clean, path);
                return clean;
            }
            mat.shader = clean.shader;
            mat.CopyPropertiesFromMaterial(clean);
            mat.shaderKeywords = clean.shaderKeywords;
            Object.DestroyImmediate(clean);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static void BuildContainer(string name, Color colour)
        {
            var mat = ContainerPaint(name, colour, 0.18f);
            var hardware = ContainerPaint("container-hardware", new Color(0.29f, 0.30f, 0.28f), 0.32f);
            var label = ContainerPaint("container-stencil", new Color(0.72f, 0.71f, 0.63f), 0.1f);
            var root = new GameObject(name);
            var t = root.transform;
            try
            {
                const float L = HarborShipSpec.BoxLength, W = HarborShipSpec.BoxWidth, H = HarborShipSpec.BoxHeight;
                Block(t, Vector3.zero, new Vector3(W - 0.08f, H - 0.06f, L - 0.08f), mat);
                // Pressed corrugated walls, framed roof and ISO corner castings.
                for (int side = -1; side <= 1; side += 2)
                {
                    for (float z = -L * 0.5f + 0.3f; z < L * 0.5f - 0.15f; z += 0.28f)
                        Block(t, new Vector3(side * (W * 0.5f - 0.035f), 0.13f, z), new Vector3(0.07f, H - 0.26f, 0.10f), mat);
                    foreach (float y in new[] { 0f, H - 0.1f })
                        Block(t, new Vector3(side * (W * 0.5f - 0.065f), y, 0f), new Vector3(0.13f, 0.10f, L), mat);
                    foreach (float z in new[] { -L * 0.5f + 0.08f, L * 0.5f - 0.08f })
                    {
                        Block(t, new Vector3(side * (W * 0.5f - 0.08f), 0f, z), new Vector3(0.16f, H, 0.16f), mat);
                        foreach (float y in new[] { 0f, H - 0.14f })
                            Block(t, new Vector3(side * (W * 0.5f - 0.08f), y, z), new Vector3(0.16f, 0.14f, 0.16f), hardware);
                    }
                }
                // Twin doors and locking bars, at the end used by the devanning gang.
                for (int side = -1; side <= 1; side += 2)
                {
                    Block(t, new Vector3(side * W * 0.25f, 0.12f, -L * 0.5f + 0.025f), new Vector3(W * 0.5f - 0.15f, H - 0.24f, 0.05f), mat);
                    foreach (float x in new[] { side * 0.28f, side * 0.86f })
                        Block(t, new Vector3(x, 0.16f, -L * 0.5f + 0.015f), new Vector3(0.045f, H - 0.32f, 0.045f), hardware);
                }
                for (int line = 0; line < 3; line++)
                    Block(t, new Vector3(0.73f, H - 0.38f - line * 0.12f, -L * 0.5f - 0.001f), new Vector3(0.42f - line * 0.07f, 0.045f, 0.01f), label);
                for (float z = -L * 0.5f + 0.3f; z < L * 0.5f - 0.2f; z += 0.28f)
                    Block(t, new Vector3(0f, H - 0.05f, z), new Vector3(W - 0.26f, 0.035f, 0.10f), mat);
                SyntyKitExtractor.BakeGroup(root, name, yaw: 0f, outputDir: ShipsDir, meshOutputDir: MeshDir);
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
