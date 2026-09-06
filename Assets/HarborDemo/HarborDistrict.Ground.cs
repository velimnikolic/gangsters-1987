using System.Collections.Generic;
using RoadDemo;
using UnityEngine;
using UnityEngine.Rendering;

namespace HarborDemo
{
    // The ground the port stands on: the sea as one water plane, the land as a
    // heightfield that is flat under the yard, a beach beyond the quay's two ends
    // and seabed under the water; the apron paved over the working area;
    // the quay wall along the coping line with its bollards, lamps and outfalls; the
    // channel buoys. (The timber pier with the harbour's boats that used to stand off
    // the west end is gone: a box port has no marina.)
    public partial class HarborDistrict
    {
        const float GroundStep = 3f;   // fine enough that the shore shelf is a slope and not two facets
        const float SeaReach = 450f;    // the water and land run this far east and west
        const float SeaSouth = -300f;   // and the water this far out
        const float DefaultLandNorth = 330f;

        float _standaloneLandNorth = DefaultLandNorth;
        float _standaloneFlatNorth;
        Rect _standaloneGroundPad;

        GameObject _waterPrefab, _quayStraight, _quayWorn, _quayPipe, _shoreRock, _paveTile;
        GameObject _bollard1, _bollard3, _pierLamp, _buoy, _buoyBall;
        Material _grassMat, _sandMat;

        bool _groundKitLoaded;

        /// <summary>
        /// Makes room in the harbor's own demo heightfield for a neighbouring district.
        /// The area arrives in the harbor contract frame (the frame used by
        /// <see cref="LocalBounds"/>); the ground itself is authored in quay coordinates,
        /// whose street origin is <see cref="PlannedStreetZ"/> farther north.
        /// </summary>
        internal void PrepareStandaloneGround(Rect contractArea)
        {
            const float Surround = 60f;
            // Keep the pad in contract coordinates. BuildWarehouses measures the exact
            // harbor road later; BuildGround shifts it onto that measured line and lowers
            // the land beneath the industrial surfaces instead of cutting a water-filled
            // hole through the heightfield.
            _standaloneGroundPad = Rect.MinMaxRect(
                contractArea.xMin - GroundStep,
                contractArea.yMin - GroundStep,
                contractArea.xMax + GroundStep,
                contractArea.yMax + GroundStep);
            float plannedOwnNorth = contractArea.yMax + PlannedStreetZ;
            _standaloneFlatNorth = plannedOwnNorth + 20f;
            _standaloneLandNorth = Mathf.Max(DefaultLandNorth, plannedOwnNorth + Surround);
        }

        void LoadGroundKit()
        {
            if (_groundKitLoaded) return;
            _groundKitLoaded = true;
            _waterPrefab = HarborKit.TryLoad(HarborKit.WaterPlane) ?? HarborKit.Load(HarborKit.OceanTile);
            _quayStraight = HarborKit.Load(HarborKit.QuayStraight);
            _quayWorn = HarborKit.TryLoad(HarborKit.QuayWorn);
            _quayPipe = HarborKit.TryLoad(HarborKit.QuayPipe);
            _shoreRock = HarborKit.TryLoad(HarborKit.ShoreRock);
            _bollard1 = HarborKit.Load(HarborKit.Bollard1);
            _bollard3 = HarborKit.TryLoad(HarborKit.Bollard3);
            _pierLamp = HarborKit.Load(HarborKit.PierLamp);
            _buoy = HarborKit.TryLoad(HarborKit.Buoy);
            _buoyBall = HarborKit.TryLoad(HarborKit.BuoyBall);
            _paveTile = HarborKit.TryLoad(HarborKit.PaveTile);
        }

        // ------------------------------------------------------------ water

        /// <summary>One water plane over the whole rectangle, measured once and scaled
        /// to it - the road demo's way; named "Water" so the perf pass leaves its
        /// vertex-animating shader alone.</summary>
        void BuildWater()
        {
            LoadGroundKit();
            if (_waterPrefab == null) return;
            float x0 = -SeaReach, x1 = SeaReach, z0 = SeaSouth, z1 = _standaloneLandNorth;
            var b = HarborKit.PrefabBounds(_waterPrefab);
            float sx = Mathf.Max(0.01f, b.size.x), sz = Mathf.Max(0.01f, b.size.z);
            var water = Instantiate(_waterPrefab, Vector3.zero, Quaternion.identity, _groundRoot);
            water.name = "Water";
            float kx = (x1 - x0) / sx, kz = (z1 - z0) / sz;
            water.transform.localScale = new Vector3(kx, 1f, kz);
            water.transform.position = new Vector3(
                (x0 + x1) * 0.5f - b.center.x * kx,
                WaterY - b.center.y,
                (z0 + z1) * 0.5f - b.center.z * kz);
            foreach (var mr in water.GetComponentsInChildren<MeshRenderer>())
                mr.shadowCastingMode = ShadowCastingMode.Off;
            TuneSurf(water, kx, kz);
        }

        /// <summary>
        /// The water at the shore. Two things make the ribbon along the coast, and the
        /// shader carries a knob for each.
        ///
        /// The foam is laid by depth and broken up with a noise read off the mesh's own
        /// UVs - fine on the fifty-metre plane it was authored for, but this one is
        /// stretched to nine hundred metres by six hundred, which stretches the breakup
        /// with it until the surf reads as one smooth white band a beach wide. So the
        /// noise is tiled back up by the same factors the plane was stretched by, and
        /// what is left is turned down to <see cref="shoreFoam"/>.
        ///
        /// The pale sand seen through the water is the shader's own doing and not the
        /// beach at all: the shallows are tinted <c>_Shallow_Color</c>, which the pack
        /// ships the colour of dry sand, and held until the water is <c>_Deep_Height</c>
        /// deep. <see cref="shallowSand"/> pulls that tint toward the deep colour, brings
        /// the depth ramp in, and closes the water up, so the sea is sea within a stride
        /// of the sand. (The beach itself was cut short to match - see BeachRun.)
        ///
        /// Tuned on a copy; the project's own material is left alone. The .mat still
        /// carries a dozen properties from an older version of this shader
        /// (_Shore_Foam_Intensity and the rest) that nothing reads any more - hence
        /// every write here going through HasProperty.
        /// </summary>
        void TuneSurf(GameObject water, float kx, float kz)
        {
            var renderers = water.GetComponentsInChildren<MeshRenderer>();
            if (renderers.Length == 0 || renderers[0].sharedMaterial == null) return;
            var mat = Keep(new Material(renderers[0].sharedMaterial) { name = "Harbor Water" });
            float k = Mathf.Max(1f, (kx + kz) * 0.5f);

            void Scale(string prop, float by)
            {
                if (mat.HasProperty(prop)) mat.SetFloat(prop, mat.GetFloat(prop) * by);
            }
            void ScaleNoise(string prop, float bx, float bz)
            {
                if (!mat.HasProperty(prop) || mat.shader == null) return;
                int index = mat.shader.FindPropertyIndex(prop);
                if (index < 0) return;
                var type = mat.shader.GetPropertyType(index);
                if (type == ShaderPropertyType.Float || type == ShaderPropertyType.Range)
                {
                    mat.SetFloat(prop, mat.GetFloat(prop) * Mathf.Max(1f, (bx + bz) * 0.5f));
                    return;
                }
                if (type != ShaderPropertyType.Vector) return;
                var v = mat.GetVector(prop);
                mat.SetVector(prop, new Vector4(v.x * bx, v.y * bz, v.z, v.w));
            }
            void Set(string prop, float value)
            {
                if (mat.HasProperty(prop)) mat.SetFloat(prop, value);
            }
            // t = 1 leaves the property as the pack authored it, t = 0 takes it to target
            void Toward(string prop, float target, float t)
            {
                if (mat.HasProperty(prop)) mat.SetFloat(prop, Mathf.Lerp(target, mat.GetFloat(prop), t));
            }

            // the breakup noise, re-tiled by however far the plane was stretched
            ScaleNoise("_Shore_Foam_Noise_Scale", kx, kz);
            ScaleNoise("_Shore_Wave_Foam_Noise_Scale", kx, kz);
            Scale("_Shore_Small_Foam_Tiling", k);
            Scale("_Shore_Edge_Noise_Scale", k);
            Scale("_Ocean_Foam_Breakup_Tiling", k);

            float f = Mathf.Clamp01(shoreFoam);
            Toward("_Shore_Small_Foam_Opacity", 0f, f);
            Toward("_Shore_Edge_Opacity", 0f, f);
            Toward("_Shore_Edge_Thickness", 0f, f);
            Toward("_Shore_Wave_Thickness", 0f, f);
            if (f <= 0.01f)
            {
                Set("_Enable_Shore_Foam", 0f);
                Set("_Enable_Shore_Wave_Foam", 0f);
                Set("_Enable_Shore_Animation", 0f);
            }

            // Sheltered working water: the pack's beach sand tint overwhelms the basin.
            if (mat.HasProperty("_Deep_Color")) mat.SetColor("_Deep_Color", new Color(0.055f, 0.15f, 0.18f, 1f));
            if (mat.HasProperty("_Shallow_Color")) mat.SetColor("_Shallow_Color", new Color(0.17f, 0.29f, 0.28f, 1f));

            // the sandy shallows
            float s = Mathf.Clamp01(shallowSand);
            if (mat.HasProperty("_Shallow_Color") && mat.HasProperty("_Deep_Color"))
            {
                var deep = mat.GetColor("_Deep_Color");
                var shallow = mat.GetColor("_Shallow_Color");
                var tinted = Color.Lerp(deep, shallow, s);
                tinted.a = shallow.a;
                mat.SetColor("_Shallow_Color", tinted);
            }
            Toward("_Deep_Height", 0.2f, s);
            Toward("_Very_Deep_Height", 1.1f, s);
            Toward("_Base_Opacity", 1f, s);

            foreach (var mr in renderers) mr.sharedMaterial = mat;
        }

        // ------------------------------------------------------------ land

        /// <summary>How the shore is cut. A beach that shelves gently is the whole of
        /// the pale ribbon along the coast: the pack's water tints its shallows the
        /// colour of sand and lays its foam by depth, so forty metres of wading reads as
        /// forty metres of painted stripe no matter what the shader is told. So the sand
        /// above the water is kept short and the ground drops away hard just under the
        /// waterline - a stride of wading, then the shelf easing out to the seabed over
        /// twenty-odd metres, the sandy shallows fading to sea colour across it.</summary>
        const float BeachRun = 13f;      // dry sand, from land level down to the waterline
        const float ShelfRun = 26f;      // from the waterline down to the seabed - a long easy slope, so the
                                         // foot of it is not a crease seen through the water as a hard line

        /// <summary>The land's height: flat under the yard, a short beach beyond either
        /// end of the quay, seabed under the water; the coast wanders a little with
        /// noise so the waterline is not a ruler.</summary>
        float LandHeight(float x, float z)
        {
            float half = QuayHalf;
            if (Mathf.Abs(x) <= half + 0.01f)
            {
                // the quay wall stands here: land behind it, seabed in front, the wall's
                // own foot at the coping line
                if (z > 0.01f) return LandY + NorthHills(x, z);
                if (z > -0.01f) return -5.5f;
                return Deeper(SeabedY, -30f - z);
            }
            // the waterline, wandering, a few metres south of the coping so the beach
            // meets the end of the quay wall rather than crossing it
            float shore = -4f + (Mathf.PerlinNoise(x * 0.012f + 3.1f, 0.5f) - 0.5f) * 14f;
            if (z >= shore)
            {
                float t = Mathf.InverseLerp(shore + BeachRun, shore, z);
                float h = Mathf.Lerp(LandY, WaterY, Mathf.SmoothStep(0f, 1f, t));
                if (t <= 0f) h += NorthHills(x, z);
                return h;
            }
            float d = Mathf.InverseLerp(shore, shore - ShelfRun, z);
            // a long easy slope both ends: the sandy shallows fade into the sea over a
            // dozen metres (the gradient the shore is asked for) and the foot is no crease
            if (d >= 1f) return Deeper(SeabedY, (shore - ShelfRun) - z);
            return Mathf.Lerp(WaterY, SeabedY, Mathf.SmoothStep(0f, 1f, d));
        }

        /// <summary>Past the shelf the bed goes on down, slowly, so the sea darkens with
        /// distance the way it does and the bed is never a floor seen through the water.</summary>
        static float Deeper(float at, float beyond) => at - Mathf.Max(0f, beyond) * 0.06f;

        /// <summary>Gentle rises well behind the street - the land is not a table. They
        /// start a good way past the street's far kerb, wherever the sheds have pushed
        /// it to, so the road is never laid up a slope it cannot follow.</summary>
        float NorthHills(float x, float z)
        {
            float foot = Mathf.Max(_streetZ + 30f, _standaloneFlatNorth);
            if (z < foot) return 0f;
            float k = Mathf.InverseLerp(foot, foot + 70f, z);
            return k * (Mathf.PerlinNoise(x * 0.006f, z * 0.006f) * 3.5f);
        }

        void BuildGround()
        {
            // the seabed runs out as far as the water does: a bed that stopped short of
            // the sea's edge showed its own edge through the water as a line along the coast
            float x0 = -SeaReach, x1 = SeaReach, z0 = SeaSouth, z1 = _standaloneLandNorth;
            int nx = Mathf.CeilToInt((x1 - x0) / GroundStep), nz = Mathf.CeilToInt((z1 - z0) / GroundStep);
            var standalonePad = new Rect(
                _standaloneGroundPad.x,
                _standaloneGroundPad.y + _streetZ,
                _standaloneGroundPad.width,
                _standaloneGroundPad.height);
            var verts = new Vector3[(nx + 1) * (nz + 1)];
            for (int j = 0; j <= nz; j++)
                for (int i = 0; i <= nx; i++)
                {
                    float x = x0 + i * GroundStep, z = z0 + j * GroundStep;
                    float y = LandHeight(x, z);
                    if (standalonePad.width > 0f && standalonePad.height > 0f &&
                        standalonePad.Contains(new Vector2(x, z)))
                        y = RoadDemo.RoadDemoBuilder.RoadBed;
                    verts[j * (nx + 1) + i] = new Vector3(x, y, z);
                }
            var grass = new List<int>();
            var sand = new List<int>();
            const float BeachLine = -0.35f;
            float half = QuayHalf;
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    float cx0 = x0 + i * GroundStep, cz0 = z0 + j * GroundStep;
                    // nothing under the concrete: the tiles are the ground there. The
                    // reclaimed bulk pier is a second, offset rectangle of that floor.
                    bool central = cx0 >= -half && cx0 + GroundStep <= half &&
                                   cz0 >= 0f && cz0 + GroundStep <= apronDepth;
                    bool bulk = InsideBulkTerminal(cx0, cx0 + GroundStep,
                                                   cz0, cz0 + GroundStep);
                    if (central || bulk) continue;
                    int a = j * (nx + 1) + i, b = a + 1, c = a + nx + 1, d = c + 1;
                    float low = Mathf.Min(verts[a].y, verts[b].y, verts[c].y, verts[d].y);
                    var into = low < BeachLine ? sand : grass;
                    into.Add(a); into.Add(c); into.Add(b);
                    into.Add(b); into.Add(c); into.Add(d);
                }

            var mesh = new Mesh { name = "Harbor Ground", indexFormat = IndexFormat.UInt32 };
            mesh.vertices = verts;
            mesh.subMeshCount = 2;
            mesh.SetTriangles(grass, 0);
            mesh.SetTriangles(sand, 1);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject("Ground");
            go.transform.SetParent(_groundRoot, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = new[] { GrassMaterial(), SandMaterial() };
            mr.shadowCastingMode = ShadowCastingMode.Off;
            go.isStatic = true;
        }

        /// <summary>The palm city's triplanar ground as it comes: grass on top.</summary>
        Material GrassMaterial()
        {
            if (_grassMat != null) return _grassMat;
            _grassMat = Keep(HarborKit.LoadMaterial(HarborKit.PalmGround)
                             ?? HarborKit.Flat("Harbor Grass", new Color(0.36f, 0.52f, 0.27f), 0.05f));
            return _grassMat;
        }

        /// <summary>Every material the port makes for itself, so Dispose can take them
        /// down: a fresh Material is not destroyed with the renderers that wore it.</summary>
        readonly List<Material> _mats = new List<Material>();

        Material Keep(Material mat)
        {
            if (mat != null) _mats.Add(mat);
            return mat;
        }

        /// <summary>The same material with its sand face turned up - the pack carries
        /// sand on the sides and bottom, so pointing those up is the beach.</summary>
        Material SandMaterial()
        {
            if (_sandMat != null) return _sandMat;
            _sandMat = Keep(HarborKit.LoadMaterial(HarborKit.PalmGround));
            if (_sandMat != null && _sandMat.HasProperty("_Triplanar_Texture_Top"))
            {
                _sandMat.SetTexture("_Triplanar_Texture_Top", _sandMat.GetTexture("_Triplanar_Texture_Side"));
                _sandMat.SetTexture("_Triplanar_Normal_Texture_Top", _sandMat.GetTexture("_Triplanar_Normal_Texture_Side"));
            }
            else _sandMat = Keep(HarborKit.Flat("Harbor Sand", new Color(0.76f, 0.72f, 0.58f), 0.08f));
            return _sandMat;
        }

        // ------------------------------------------------------------ apron

        Material _concreteMat, _asphaltMat;

        /// <summary>The concrete, as the palm city tiles it on its sidewalks - one
        /// poured plane, cast in twelve-metre bays. What the apron used to be made of,
        /// and what it falls back to when the city's paving square is not in the
        /// project.</summary>
        Material ConcreteMaterial()
        {
            if (_concreteMat != null) return _concreteMat;
            _concreteMat = Keep(HarborKit.LoadMaterial(HarborKit.ConcreteMat)
                                ?? HarborKit.Flat("Harbor Concrete", new Color(0.66f, 0.64f, 0.6f), 0.05f));
            _concreteMat.name = "Harbor Concrete";
            return _concreteMat;
        }

        /// <summary>The tarmac, the same way, and darkened a touch: the pack's road grey
        /// is within a shade of its concrete, and a road the colour of the yard is a
        /// stain on it. Dark enough to read as a road from the air, not black.</summary>
        Material AsphaltMaterial()
        {
            if (_asphaltMat != null) return _asphaltMat;
            _asphaltMat = Keep(HarborKit.LoadMaterial(HarborKit.AsphaltMat)
                               ?? HarborKit.Flat("Harbor Asphalt", new Color(0.3f, 0.3f, 0.31f), 0.1f));
            _asphaltMat.name = "Harbor Asphalt";
            var tint = new Color(0.62f, 0.62f, 0.63f, 1f);
            if (_asphaltMat.HasProperty("_BaseColor")) _asphaltMat.SetColor("_BaseColor", tint);
            if (_asphaltMat.HasProperty("_Color")) _asphaltMat.SetColor("_Color", tint);
            return _asphaltMat;
        }

        /// <summary>
        /// A rectangle floored with one piece of the kit laid edge to edge, its top on
        /// <paramref name="top"/>: a core block's floor - which is a carpet of the city
        /// demo's own paving squares - done to a port's measurements.
        ///
        /// Nothing is assumed about the piece. Its footprint and where its pivot sits
        /// inside it are both measured, so the caller need not know that this kit pivots
        /// a ground tile on its +X/+Z corner.
        ///
        /// The rectangle is divided into whole tiles and EVERY tile takes the same share
        /// of the remainder - HarborKit.LayRun's way with a wall, for the same reason. A
        /// carpet that lays true tiles and leaves a part one at the fence draws a seam
        /// down the yard where the sliver row starts; the same slack spread over sixty
        /// tiles is a couple of centimetres each, on paving with no pattern to break.
        ///
        /// A tile keeps no collider - the poured apron never had one, and a thousand box
        /// colliders is a physics scene nobody asked for - and is deliberately NOT marked
        /// static, because static is how ScenePerf is told a renderer is already one mesh
        /// and must be left out of the merge. This carpet wants merging: it comes back
        /// off the first frame as a handful of meshes, one per 120 m cell.
        /// </summary>
        int TileCarpet(string name, float x0, float x1, float z0, float z1, float top,
                       GameObject tile, Transform parent)
        {
            if (tile == null || x1 - x0 < 0.05f || z1 - z0 < 0.05f) return 0;
            var b = HarborKit.PrefabBounds(tile);
            float tx = b.size.x, tz = b.size.z;
            if (tx < 0.05f || tz < 0.05f) return 0;
            MapGeometry.Fill(Rect.MinMaxRect(x0, z0, x1, z1), TurfInk.Concrete, top);

            int nx = Mathf.Max(1, Mathf.RoundToInt((x1 - x0) / tx));
            int nz = Mathf.Max(1, Mathf.RoundToInt((z1 - z0) / tz));
            float cw = (x1 - x0) / nx, cd = (z1 - z0) / nz;
            float sx = cw / tx, sz = cd / tz;
            bool hasColliders = tile.GetComponentInChildren<Collider>(true) != null;

            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                {
                    // the piece's own far corner onto the cell's far corner, its top onto `top`
                    var at = new Vector3(x0 + (i + 1) * cw - b.max.x * sx,
                                         top - b.max.y,
                                         z0 + (j + 1) * cd - b.max.z * sz);
                    var go = Instantiate(tile, at, Quaternion.identity, parent);
                    go.name = name;
                    go.transform.localScale = new Vector3(sx, 1f, sz);
                    if (hasColliders)
                        foreach (var col in go.GetComponentsInChildren<Collider>(true)) Destroy(col);
                }
            return nx * nz;
        }

        /// <summary>One flat rectangle of ground at a height, built as a grid of cells
        /// <paramref name="cell"/> metres square, each mapped to the inside of the
        /// texture. The pack's ground textures are not made to wrap: each carries a
        /// swatch strip along its bottom edge and a soft vignette to its border, so a
        /// plane that simply repeats them draws a palette line every repeat. Mapped
        /// cell by cell into [0.04, 0.96] the strip is never sampled, and the vignette
        /// reads as the pour bays a big slab is cast in - faint, and twelve metres
        /// apart, not five. Partial cells at the edges take their share of the map.</summary>
        GameObject FlatPlane(string name, float x0, float x1, float z0, float z1, float y, Material mat, float cell, Transform parent)
        {
            if (x1 - x0 < 0.05f || z1 - z0 < 0.05f) return null;
            MapGeometry.Fill(Rect.MinMaxRect(x0, z0, x1, z1),
                mat == _asphaltMat ? TurfInk.Road : TurfInk.Concrete, y);
            const float U0 = 0.04f, U1 = 0.96f;
            int nx = Mathf.Max(1, Mathf.CeilToInt((x1 - x0) / cell - 0.001f));
            int nz = Mathf.Max(1, Mathf.CeilToInt((z1 - z0) / cell - 0.001f));
            var verts = new List<Vector3>(nx * nz * 4);
            var uvs = new List<Vector2>(nx * nz * 4);
            var norms = new List<Vector3>(nx * nz * 4);
            var tris = new List<int>(nx * nz * 6);
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    float cx0 = x0 + i * cell, cx1 = Mathf.Min(x1, cx0 + cell);
                    float cz0 = z0 + j * cell, cz1 = Mathf.Min(z1, cz0 + cell);
                    float fu = (cx1 - cx0) / cell, fv = (cz1 - cz0) / cell;
                    // each cell turned a quarter at a time, so the few flecks in the
                    // map do not march in step across the yard
                    int turn = fu < 0.999f || fv < 0.999f ? 0 : (i * 7 + j * 13 + (int)(x0 * 3f)) & 3;
                    var corners = new[] { new Vector2(U0, U0), new Vector2(U0 + (U1 - U0) * fu, U0),
                                          new Vector2(U0 + (U1 - U0) * fu, U0 + (U1 - U0) * fv), new Vector2(U0, U0 + (U1 - U0) * fv) };
                    int b = verts.Count;
                    verts.Add(new Vector3(cx0, y, cz0)); verts.Add(new Vector3(cx1, y, cz0));
                    verts.Add(new Vector3(cx1, y, cz1)); verts.Add(new Vector3(cx0, y, cz1));
                    for (int k = 0; k < 4; k++) { uvs.Add(corners[(k + turn) & 3]); norms.Add(Vector3.up); }
                    tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
                    tris.Add(b); tris.Add(b + 3); tris.Add(b + 2);
                }
            var mesh = new Mesh { name = name, indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetNormals(norms);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            go.isStatic = true;
            return go;
        }

        /// <summary>The working area's floor, top at TileTop, from the west end of the
        /// quay to the east and from the coping line back to the fence: the city's own
        /// pavement squares, carpeted the way a core block is floored, so a port set down
        /// on a shore stands on the same concrete as the streets behind it. The poured
        /// plane stands in when the paving square is not there to be had.</summary>
        void BuildApron()
        {
            PourTerminalApron("Container apron", Rect.MinMaxRect(-QuayHalf, 0f, QuayHalf, apronDepth));
            BuildBulkTerminalApron();
        }

        /// <summary>A strip of asphalt (the yard roads, the gate roads, the aisles) laid
        /// a hair above the concrete so it reads as a surface, not a fight.</summary>
        void AsphaltStrip(float x0, float x1, float z0, float z1, Transform parent)
        {
            FlatPlane("Asphalt", x0, x1, z0, z1, TileTop + 0.012f, AsphaltMaterial(), 10f, parent);
        }

        // ------------------------------------------------------------ quay

        /// <summary>How likely a length of coping is the scuffed piece rather than the
        /// clean one: hard against a berth, hardly at all halfway between two.</summary>
        double WearAt(float x)
        {
            float best = float.MaxValue;
            for (int i = 0; i < berths; i++) best = Mathf.Min(best, Mathf.Abs(x - BerthX(i)));
            return Mathf.Lerp(0.62f, 0.08f, Mathf.InverseLerp(20f, berthPitch * 0.5f, best));
        }

        /// <summary>The wall along the coping line: the city pack's water edge, five
        /// metres a piece, its face to the sea, an outfall pipe now and then; on the
        /// coping the bollards every nine metres and pier lamps every twenty-seven;
        /// rocks piled where the wall ends and the beach takes over; the channel
        /// buoys out where the sailing lanes run.</summary>
        void BuildQuay()
        {
            float half = QuayHalf;
            if (_quayStraight != null)
            {
                for (float x = -half; x < half - 0.1f; x += 5f)
                {
                    // the wall wears where it is worked: a berth's own length of it takes
                    // the scuffed piece three times in five, the stretches between berths
                    // one in eight. A wall equally worn end to end is a texture; a wall
                    // worn under the cranes is a place that has been used.
                    var piece = _quayWorn != null && _rng.NextDouble() < WearAt(x + 2.5f) ? _quayWorn : _quayStraight;
                    HarborKit.Prop(piece, new Vector3(x, 0f, 0f), 0f, _quayRoot, "Quay");
                    if (_quayPipe != null && _rng.NextDouble() < 0.05)
                        HarborKit.Prop(_quayPipe, new Vector3(x + 2.5f, WaterY + 0.9f, -QuayFace + 0.05f), 0f, _quayRoot, "Outfall");
                }
            }
            BuildBulkTerminalQuay();

            // bollards on the coping, a stride in from the face, big and small by turns
            int slot = 0;
            for (float x = -half + 4.5f; x < half - 4f; x += 9f, slot++)
            {
                var bollard = slot % 2 == 0 || _bollard3 == null ? _bollard1 : _bollard3;
                HarborKit.Prop(bollard, new Vector3(x, BollardY, BollardZ), 0f, _quayRoot, "Bollard");
            }
            // lamps back from the coping, out of the forklifts' way, turned to the water.
            // Left with the PREFAB'S OWN NAME on purpose: DemoStreetLamps finds the lamps
            // it lights by prefab name, and while these were renamed "Lamp" the whole quay
            // stood dark at night with the street behind the wire burning.
            for (float x = -half + 13.5f; x < half - 4f; x += 27f)
                HarborKit.Prop(_pierLamp, new Vector3(x, TileTop, 2.2f), 180f, _quayRoot);

            // where the west wall stops the beach begins. The east rocks moved to the
            // outer corner of the reclaimed bulk pier.
            if (_shoreRock != null)
                for (int k = 0; k < 5; k++)
                {
                    var pos = new Vector3(-(half + 1.5f + k * 2.2f), WaterY - 0.6f + k * 0.35f,
                                          -1.5f - k * 2.6f + HarborKit.Range(_rng, -1f, 1f));
                    var rock = HarborKit.Prop(_shoreRock, pos, HarborKit.Range(_rng, 0f, 360f), _quayRoot, "Rock");
                    rock.transform.localScale = Vector3.one * HarborKit.Range(_rng, 0.9f, 1.6f);
                }

            // the channel: tall buoys down the sailing lanes' seaward side, every sixty metres
            if (_buoy != null)
                for (float x = -half - 60f; x <= BulkTerminalEast + 60f; x += 60f)
                    HarborKit.Prop(_buoy, new Vector3(x + HarborKit.Range(_rng, -6f, 6f), WaterY - 0.25f, -(HarborShipping.LaneOffset + (berths - 1) * HarborShipping.LaneStep + 16f) + HarborKit.Range(_rng, -2f, 2f)),
                                   HarborKit.Range(_rng, 0f, 360f), _quayRoot, "Buoy");
        }
    }
}
