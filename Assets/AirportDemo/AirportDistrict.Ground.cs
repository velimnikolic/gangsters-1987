using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace AirportDemo
{
    // The ground and everything paved on it. The field is flat - an airport is the
    // one place in a landscape that has been made flat on purpose - so there is no
    // heightfield here, only one grass plane and the pavements laid over it: the
    // runway and its shoulders, the parallel taxiway and its four connectors, the
    // ramp, the taxilanes off it and the airside service road.
    //
    // Every surface is one generated plane subdivided at a cell, with the pack's own
    // tiling material and a quarter turn of UV per cell so the few flecks in the map
    // do not march in step across a kilometre and a half of concrete. The harbour's
    // rule: a working surface is not a chessboard of slabs.
    public partial class AirportDistrict
    {
        Material _grassMat, _concreteMat, _asphaltMat, _shoulderMat;
        Material _whitePaint, _yellowPaint, _blackPaint, _redPaint, _rubberMat;
        /// <summary>The same paint in three states - fresh, faded, scrubbed - indexed by
        /// a Painter's Tier. Airfield paint is repainted a stretch at a time and worn
        /// off a stretch at a time, and a runway whose every stripe is the same white is
        /// a runway nobody has ever landed on.</summary>
        Material[] _whiteTiers, _yellowTiers;
        /// <summary>What the field has spilt, patched and worn: oil on a stand, a
        /// bitumen patch on the tarmac, the seal down a crack, the dirt a lorry has
        /// tracked onto the grass.</summary>
        Material _stainMat, _patchMat, _sealMat, _dirtMat;
        /// <summary>Two more tones of the ramp's own concrete. A ramp is not one pour -
        /// it is fifty of them, laid over thirty years, and no two batches came out the
        /// same colour. Laying a few bays in these instead of a repeating texture is
        /// what stops eighty thousand square metres reading as one flat sheet.</summary>
        Material _pourPale, _pourDark;
        /// <summary>Every material made here, so Dispose can take them down: a fresh
        /// Material is not destroyed with the renderers that wore it.</summary>
        readonly List<Material> _mats = new List<Material>();

        GameObject _lightWhite, _lightAmber, _lightGreen, _lightRed, _lightBlue;
        GameObject _conePrefab, _bollardPrefab;

        Material Keep(Material mat)
        {
            if (mat != null) _mats.Add(mat);
            return mat;
        }

        void LoadKit()
        {
            _grassMat = Keep(AirportKit.LoadMaterial(AirportKit.GrassMat) ?? AirportKit.Flat("airport grass", new Color(0.35f, 0.47f, 0.25f)));

            // concrete, tarmac and shoulder are ONE tiling material taken to three
            // tones. An airfield's surfaces differ by shade and by age far more than
            // they differ by texture - poured concrete on the ramp, a darker bitumen on
            // the runway, a darker one again on its shoulders - and driving all three
            // off one material means they tile against each other without a join.
            _concreteMat = Surface("airport concrete", 1.16f, new Color(0.62f, 0.62f, 0.60f));
            _asphaltMat = Surface("airport asphalt", 0.52f, new Color(0.28f, 0.28f, 0.29f));
            // the shoulders are the same tarmac, a shade darker, so the runway edge
            // reads as an edge from the air even before the paint goes on
            _shoulderMat = Surface("airport shoulder", 0.40f, new Color(0.22f, 0.22f, 0.23f));
            _pourPale = Surface("airport pour pale", 1.30f, new Color(0.69f, 0.69f, 0.67f));
            _pourDark = Surface("airport pour dark", 1.02f, new Color(0.55f, 0.55f, 0.54f));

            // airfield paint. No pack has a road decal at runway scale - a centreline
            // stripe is 36 m long and 90 cm wide - so the markings are flat colour on
            // quads, which is what airfield paint is.
            _whitePaint = Keep(AirportKit.Flat("airport paint white", new Color(0.88f, 0.88f, 0.85f), 0.05f));
            _yellowPaint = Keep(AirportKit.Flat("airport paint yellow", new Color(0.85f, 0.68f, 0.09f), 0.05f));
            _blackPaint = Keep(AirportKit.Flat("airport paint black", new Color(0.07f, 0.07f, 0.08f), 0.05f));
            _redPaint = Keep(AirportKit.Flat("airport paint red", new Color(0.60f, 0.10f, 0.09f), 0.05f));
            _rubberMat = Keep(AirportKit.Flat("airport rubber", new Color(0.16f, 0.16f, 0.17f), 0.02f));

            // paint ages toward the surface it is on: white goes grey, yellow goes to
            // the colour of the tarmac showing through it
            _whiteTiers = new[]
            {
                _whitePaint,
                Keep(AirportKit.Flat("airport paint white faded", new Color(0.74f, 0.74f, 0.72f), 0.04f)),
                Keep(AirportKit.Flat("airport paint white worn", new Color(0.58f, 0.58f, 0.57f), 0.03f)),
            };
            _yellowTiers = new[]
            {
                _yellowPaint,
                Keep(AirportKit.Flat("airport paint yellow faded", new Color(0.72f, 0.60f, 0.17f), 0.04f)),
                Keep(AirportKit.Flat("airport paint yellow worn", new Color(0.56f, 0.49f, 0.24f), 0.03f)),
            };

            _stainMat = Keep(AirportKit.Flat("airport stain", new Color(0.20f, 0.19f, 0.18f), 0.14f));
            _patchMat = Keep(AirportKit.Flat("airport patch", new Color(0.20f, 0.20f, 0.21f), 0.03f));
            _sealMat = Keep(AirportKit.Flat("airport crack seal", new Color(0.11f, 0.11f, 0.12f), 0.10f));
            _dirtMat = Keep(AirportKit.Flat("airport dirt", new Color(0.40f, 0.35f, 0.26f), 0.02f));

            _lightWhite = AirportKit.TryLoad(AirportKit.EdgeLights[AirportKit.LightWhite]);
            _lightAmber = AirportKit.TryLoad(AirportKit.EdgeLights[AirportKit.LightAmber]);
            _lightGreen = AirportKit.TryLoad(AirportKit.EdgeLights[AirportKit.LightGreen]);
            _lightRed = AirportKit.TryLoad(AirportKit.EdgeLights[AirportKit.LightRed]);
            _lightBlue = AirportKit.TryLoad(AirportKit.EdgeLights[AirportKit.LightBlue]);
            _conePrefab = AirportKit.TryLoad(AirportKit.Cone);
            _bollardPrefab = AirportKit.TryLoad(AirportKit.Bollard);
        }

        /// <summary>One of the field's paved surfaces: the tiling concrete taken to the
        /// tone this surface is, or a flat colour of that tone when the pack material is
        /// not there. The multiplier is applied to the material's OWN base colour rather
        /// than replacing it, so whatever shading the pack authored survives the tint.</summary>
        Material Surface(string name, float tone, Color fallback)
        {
            var mat = Keep(AirportKit.LoadMaterial(AirportKit.ConcreteMat));
            if (mat == null) return Keep(AirportKit.Flat(name, fallback, 0.05f));
            mat.name = name;
            if (mat.HasProperty("_BaseColor"))
            {
                var c = mat.GetColor("_BaseColor") * tone;
                c.a = 1f;
                mat.SetColor("_BaseColor", c);
            }
            return mat;
        }

        // ------------------------------------------------------------ surfaces

        /// <summary>One plane over a rectangle, subdivided at <paramref name="cell"/>.
        ///
        /// Two ways of laying the UVs, and which one is right depends entirely on the
        /// material. An ATLAS material - a patch of a shared page - has to have each
        /// cell mapped inside its own patch, inset off the page edge and turned a
        /// quarter at a time so the few flecks in it do not march in step. A TILING
        /// material must NOT: resetting its UVs every cell is what puts a seam every
        /// fifteen metres across the ramp and makes eighty thousand square metres of
        /// concrete read as a chessboard. For those the UVs run CONTINUOUSLY off the
        /// world position, so the surface has no cell in it at all.
        ///
        /// The airport's big surfaces are all tiling now (AirportKit.ConcreteMat), so
        /// <paramref name="atlas"/> is false for every one of them; the flag stays for
        /// the pack pieces that still need the old behaviour.</summary>
        GameObject FlatPlane(string name, float x0, float x1, float z0, float z1, float y, Material mat, float cell, Transform parent,
                             bool atlas = false, float tile = 24f)
        {
            if (x1 - x0 < 0.05f || z1 - z0 < 0.05f) return null;
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
                    int turn = 0;
                    Vector2[] corners;
                    if (atlas)
                    {
                        turn = fu < 0.999f || fv < 0.999f ? 0 : (i * 7 + j * 13 + (int)(x0 * 3f)) & 3;
                        corners = new[] { new Vector2(U0, U0), new Vector2(U0 + (U1 - U0) * fu, U0),
                                          new Vector2(U0 + (U1 - U0) * fu, U0 + (U1 - U0) * fv), new Vector2(U0, U0 + (U1 - U0) * fv) };
                    }
                    else
                    {
                        // straight off the world, so two neighbouring cells share an
                        // edge in UV as well as in space and the seam disappears
                        float t = Mathf.Max(0.01f, tile);
                        corners = new[] { new Vector2(cx0 / t, cz0 / t), new Vector2(cx1 / t, cz0 / t),
                                          new Vector2(cx1 / t, cz1 / t), new Vector2(cx0 / t, cz1 / t) };
                    }
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

        /// <summary>The field it all stands on: grass from fence to fence and well
        /// beyond, flat, because that is what an airfield is.</summary>
        void BuildGround()
        {
            FlatPlane("Field", AirportSpec.MapX0, AirportSpec.MapX1, AirportSpec.MapZ0, AirportSpec.MapZ1,
                      AirportSpec.LandY, _grassMat, 40f, _groundRoot);
        }

        /// <summary>The runway: tarmac between the edges, a paved shoulder either side,
        /// and the blast pads the two ends need so the grass is not scoured away.</summary>
        void BuildRunway()
        {
            float half = RunwayHalf, w = AirportSpec.RunwayHalfWidth, s = AirportSpec.RunwayShoulder;
            FlatPlane("Runway shoulder", -half - 12f, half + 12f, -w - s, w + s, AirportSpec.PaveY - 0.01f,
                      _shoulderMat, 25f, _airsideRoot);
            FlatPlane("Runway", -half, half, -w, w, AirportSpec.PaveY, _asphaltMat, 25f, _airsideRoot);
        }

        /// <summary>Taxiway A, its shoulders, and the four connectors that run from the
        /// runway edge across the holding position to it - plus the fillets where they
        /// meet, so an aeroplane turning off has pavement under its outer wheel.</summary>
        void BuildTaxiways()
        {
            float half = RunwayHalf;
            float tz = AirportSpec.TaxiwayZ, th = AirportSpec.TaxiwayHalf, ts = AirportSpec.TaxiwayShoulder;
            float runwayEdge = AirportSpec.RunwayHalfWidth + AirportSpec.RunwayShoulder;

            FlatPlane("Taxiway shoulder", -half - 20f, half + 20f, tz - th - ts, tz + th + ts,
                      AirportSpec.PaveY - 0.01f, _shoulderMat, 20f, _airsideRoot);
            FlatPlane("Taxiway A", -half - 20f, half + 20f, tz - th, tz + th, AirportSpec.PaveY, _asphaltMat, 20f, _airsideRoot);

            foreach (float cx in AirportSpec.ConnectorX)
            {
                float x = Mathf.Clamp(cx, -half + 30f, half - 30f);
                FlatPlane("Connector shoulder", x - th - ts, x + th + ts, runwayEdge, tz - th, AirportSpec.PaveY - 0.01f, _shoulderMat, 12f, _airsideRoot);
                FlatPlane("Connector", x - th, x + th, AirportSpec.RunwayHalfWidth - 0.5f, tz + th, AirportSpec.PaveY, _asphaltMat, 12f, _airsideRoot);
                // the fillets: a quarter of tarmac in each inside corner, top and bottom
                Fillet(x - th, AirportSpec.RunwayHalfWidth, -1f, 1f, _airsideRoot);
                Fillet(x + th, AirportSpec.RunwayHalfWidth, 1f, 1f, _airsideRoot);
                Fillet(x - th, tz - th, -1f, -1f, _airsideRoot);
                Fillet(x + th, tz - th, 1f, -1f, _airsideRoot);
            }

            // the taxilanes from the parallel taxiway up onto the ramp
            foreach (float cx in AirportSpec.ApronEntryX)
            {
                float lh = AirportSpec.TaxilaneWidth * 0.5f;
                FlatPlane("Taxilane", cx - lh, cx + lh, tz + th - 0.5f, AirportSpec.ApronZ0 + 0.5f, AirportSpec.PaveY, _asphaltMat, 12f, _airsideRoot);
            }
        }

        /// <summary>A quarter-disc of pavement in the corner where two edges meet, so a
        /// turning aeroplane has tarmac where it actually rolls.</summary>
        void Fillet(float x, float z, float sx, float sz, Transform parent, float radius = 16f, int steps = 8)
        {
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            var corner = new Vector3(x + sx * radius, AirportSpec.PaveY, z + sz * radius);
            verts.Add(corner); norms.Add(Vector3.up); uvs.Add(new Vector2(0.5f, 0.5f));
            for (int i = 0; i <= steps; i++)
            {
                float a = Mathf.PI * 0.5f * i / steps;
                var p = corner + new Vector3(-sx * Mathf.Cos(a) * radius, 0f, -sz * Mathf.Sin(a) * radius);
                verts.Add(p); norms.Add(Vector3.up);
                uvs.Add(new Vector2(0.5f + Mathf.Cos(a) * 0.45f, 0.5f + Mathf.Sin(a) * 0.45f));
            }
            for (int i = 1; i <= steps; i++)
            {
                if (sx * sz > 0f) { tris.Add(0); tris.Add(i); tris.Add(i + 1); }
                else { tris.Add(0); tris.Add(i + 1); tris.Add(i); }
            }
            var mesh = new Mesh { name = "fillet" };
            mesh.SetVertices(verts); mesh.SetNormals(norms); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            var go = new GameObject("Fillet");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _asphaltMat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            go.isStatic = true;
        }

        /// <summary>A band of pavement swept round an arc: the turn at each end of the
        /// kerb loop. A loop road whose ends are square is not a loop - it is a
        /// rectangle a car has to stop and reverse round - and squareness is most of
        /// what made the forecourt read as a car park with a hole in it.
        ///
        /// Angles are degrees measured the usual way (0 along +X, rising toward +Z).</summary>
        GameObject RoadArc(string name, Vector3 centre, float rInner, float rOuter,
                           float a0, float a1, float y, Material mat, Transform parent, int steps = 28)
        {
            if (rOuter <= rInner || steps < 2) return null;
            var verts = new List<Vector3>((steps + 1) * 2);
            var uvs = new List<Vector2>((steps + 1) * 2);
            var norms = new List<Vector3>((steps + 1) * 2);
            var tris = new List<int>(steps * 6);
            float span = (a1 - a0) * Mathf.Deg2Rad;
            float arc = Mathf.Abs(span) * (rInner + rOuter) * 0.5f;
            for (int i = 0; i <= steps; i++)
            {
                float a = a0 * Mathf.Deg2Rad + span * i / steps;
                float c = Mathf.Cos(a), s = Mathf.Sin(a);
                verts.Add(new Vector3(centre.x + c * rInner, y, centre.z + s * rInner));
                verts.Add(new Vector3(centre.x + c * rOuter, y, centre.z + s * rOuter));
                // along the arc in metres, across it in metres: the same units the
                // straight legs are laid in, so the tarmac tiles through the turn
                float u = arc * i / steps / 24f;
                uvs.Add(new Vector2(u, 0f));
                uvs.Add(new Vector2(u, (rOuter - rInner) / 24f));
                norms.Add(Vector3.up); norms.Add(Vector3.up);
            }
            // Winding. On a straight plane "along" and "across" make a right-handed
            // pair and the quad winds one way; on an arc the outward radial is the
            // across, and it points the OPPOSITE way round that pair - so the obvious
            // winding faces the ground and the turn is invisible from above with the
            // grass showing through it. Sweeping backwards flips it again.
            for (int i = 0; i < steps; i++)
            {
                int b = i * 2;
                if (span > 0f)
                {
                    tris.Add(b); tris.Add(b + 3); tris.Add(b + 1);
                    tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
                }
                else
                {
                    tris.Add(b); tris.Add(b + 1); tris.Add(b + 3);
                    tris.Add(b); tris.Add(b + 3); tris.Add(b + 2);
                }
            }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.SetNormals(norms); mesh.SetTriangles(tris, 0);
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

        /// <summary>The ramp: one slab of concrete from the west hangar line to the
        /// freight shed, with the airside service road in asphalt along its back edge
        /// so a truck's route is visible even when nothing is on it.</summary>
        void BuildApron()
        {
            FlatPlane("Ramp", AirportSpec.ApronX0, AirportSpec.ApronX1, AirportSpec.ApronZ0, AirportSpec.ApronZ1,
                      AirportSpec.PaveY, _concreteMat, 15f, _apronRoot);
            float sr = AirportSpec.ServiceRoadWidth * 0.5f;
            FlatPlane("Service road", AirportSpec.ApronX0 - 30f, AirportSpec.ApronX1 + 30f,
                      AirportSpec.ServiceRoadZ - sr, AirportSpec.ServiceRoadZ + sr,
                      AirportSpec.PaveY + 0.005f, _asphaltMat, 15f, _apronRoot);
            // the apron in front of each building, up to its wall, so no building
            // stands on grass with a metre of nothing between it and the concrete
            FlatPlane("Building apron", AirportSpec.ApronX0 - 30f, AirportSpec.ApronX1 + 30f,
                      AirportSpec.ApronZ1, AirportSpec.BuildingFrontZ + 0.5f,
                      AirportSpec.PaveY, _concreteMat, 15f, _apronRoot);
            // and the yard road behind them, inside the wire: what the freight lorries
            // back onto the shed's dock from, and how anything reaches the back of a
            // hangar without crossing the ramp. It stops either side of the terminal,
            // whose own back wall stands on the boundary and leaves no room for it.
            float termHalf = AirportSpec.TerminalWidth * 0.5f + 6f;
            FlatPlane("Rear yard road", AirportSpec.FenceX0 + 10f, AirportSpec.TerminalX - termHalf,
                      AirportSpec.FenceZ - 7f, AirportSpec.FenceZ - 1f,
                      AirportSpec.PaveY, _asphaltMat, 15f, _apronRoot);
            FlatPlane("Rear yard road", AirportSpec.TerminalX + termHalf, AirportSpec.FenceX1 - 10f,
                      AirportSpec.FenceZ - 7f, AirportSpec.FenceZ - 1f,
                      AirportSpec.PaveY, _asphaltMat, 15f, _apronRoot);
        }

        // ------------------------------------------------------------ paint
        //
        // Every marking on the field is a quad; all the quads of one colour go into
        // one mesh. There are some three hundred of them - the centreline alone is
        // twenty-five stripes - and three hundred GameObjects for paint would be
        // three hundred renderers for something a metre wide.

        sealed class Painter
        {
            readonly List<Vector3> _v = new List<Vector3>();
            readonly List<Vector3> _n = new List<Vector3>();
            readonly List<Vector2> _uv = new List<Vector2>();
            readonly List<int>[] _t;

            /// <summary>Which shade the next quad is painted in - fresh, faded, worn.
            /// One mesh still, one renderer still: the tiers are submeshes, so the paint
            /// on a scrubbed touchdown zone can be a different grey from the paint at
            /// the far end without three hundred more GameObjects.</summary>
            public int Tier;

            public Painter(int tiers = 1)
            {
                _t = new List<int>[Mathf.Max(1, tiers)];
                for (int i = 0; i < _t.Length; i++) _t[i] = new List<int>();
            }

            public int Count { get; private set; }

            /// <summary>An axis-aligned rectangle of paint.</summary>
            public void Rect(float x0, float x1, float z0, float z1, float y)
            {
                if (x1 <= x0 || z1 <= z0) return;
                Quad(new Vector3(x0, y, z0), new Vector3(x1, y, z0), new Vector3(x1, y, z1), new Vector3(x0, y, z1));
            }

            /// <summary>A rectangle turned about its own centre - the lead-in lines,
            /// the tie-down tees, anything that does not lie along an axis.</summary>
            public void Turned(Vector3 centre, float yaw, float width, float depth, float y)
            {
                var q = Quaternion.Euler(0f, yaw, 0f);
                float hw = width * 0.5f, hd = depth * 0.5f;
                centre.y = y;
                Quad(centre + q * new Vector3(-hw, 0f, -hd), centre + q * new Vector3(hw, 0f, -hd),
                     centre + q * new Vector3(hw, 0f, hd), centre + q * new Vector3(-hw, 0f, hd));
            }

            /// <summary>A dashed line from A to B: <paramref name="on"/> metres of
            /// paint, <paramref name="off"/> metres of nothing.</summary>
            public void Dashes(Vector3 a, Vector3 b, float width, float on, float off, float y)
            {
                var d = b - a; d.y = 0f;
                float len = d.magnitude;
                if (len < 0.01f) return;
                var dir = d / len;
                float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                for (float s = 0f; s < len; s += on + off)
                {
                    float run = Mathf.Min(on, len - s);
                    if (run < 0.2f) break;
                    Turned(a + dir * (s + run * 0.5f), yaw, width, run, y);
                }
            }

            /// <summary>A ring of paint - the helipad's approach circle.</summary>
            public void Ring(Vector3 centre, float radius, float width, float y, int steps = 48)
            {
                for (int i = 0; i < steps; i++)
                {
                    float a0 = i / (float)steps * Mathf.PI * 2f, a1 = (i + 1) / (float)steps * Mathf.PI * 2f;
                    float r0 = radius - width * 0.5f, r1 = radius + width * 0.5f;
                    Quad(centre + new Vector3(Mathf.Cos(a0) * r0, y - centre.y, Mathf.Sin(a0) * r0),
                         centre + new Vector3(Mathf.Cos(a1) * r0, y - centre.y, Mathf.Sin(a1) * r0),
                         centre + new Vector3(Mathf.Cos(a1) * r1, y - centre.y, Mathf.Sin(a1) * r1),
                         centre + new Vector3(Mathf.Cos(a0) * r1, y - centre.y, Mathf.Sin(a0) * r1));
                }
            }

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                int i = _v.Count;
                _v.Add(a); _v.Add(b); _v.Add(c); _v.Add(d);
                for (int k = 0; k < 4; k++) _n.Add(Vector3.up);
                _uv.Add(new Vector2(0f, 0f)); _uv.Add(new Vector2(1f, 0f)); _uv.Add(new Vector2(1f, 1f)); _uv.Add(new Vector2(0f, 1f));
                var tris = _t[Mathf.Clamp(Tier, 0, _t.Length - 1)];
                tris.Add(i); tris.Add(i + 2); tris.Add(i + 1);
                tris.Add(i); tris.Add(i + 3); tris.Add(i + 2);
                Count++;
            }

            public GameObject Emit(string name, Material mat, Transform parent)
                => Emit(name, new[] { mat }, parent);

            /// <summary>One mesh, one renderer, a submesh per tier that has anything in
            /// it. An empty tier is dropped along with its material rather than left as
            /// a submesh drawing nothing.</summary>
            public GameObject Emit(string name, Material[] mats, Transform parent)
            {
                if (_v.Count == 0 || mats == null || mats.Length == 0) return null;
                var used = new List<Material>();
                var sets = new List<List<int>>();
                for (int i = 0; i < _t.Length; i++)
                {
                    if (_t[i].Count == 0) continue;
                    used.Add(mats[Mathf.Min(i, mats.Length - 1)]);
                    sets.Add(_t[i]);
                }
                if (sets.Count == 0) return null;

                var mesh = new Mesh { name = name, indexFormat = _v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.SetVertices(_v); mesh.SetNormals(_n); mesh.SetUVs(0, _uv);
                mesh.subMeshCount = sets.Count;
                for (int i = 0; i < sets.Count; i++) mesh.SetTriangles(sets[i], i);
                mesh.RecalculateBounds();
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = used.ToArray();
                mr.shadowCastingMode = ShadowCastingMode.Off;
                go.isStatic = true;
                return go;
            }
        }

        /// <summary>A figure or a word painted flat on the ground out of the block
        /// alphabet, reading along <paramref name="yaw"/>.</summary>
        static void PaintLegend(Painter p, string text, Vector3 centre, float height, float yaw, float y, bool tightGap = false)
        {
            float cell = height / AirportKit.Glyph.Rows;
            float glyphW = cell * AirportKit.Glyph.Cols;
            float gap = tightGap ? cell * 0.6f : cell * 1.4f;
            float total = text.Length * glyphW + (text.Length - 1) * gap;
            var runs = new List<Vector2Int>();
            var q = Quaternion.Euler(0f, yaw, 0f);
            for (int i = 0; i < text.Length; i++)
            {
                float gx = -total * 0.5f + i * (glyphW + gap);
                for (int row = 0; row < AirportKit.Glyph.Rows; row++)
                {
                    AirportKit.Glyph.RowRuns(text[i], row, runs);
                    foreach (var r in runs)
                    {
                        float w = (r.y - r.x) * cell;
                        // row 0 is the top of the figure, which is the far end along +Z
                        var local = new Vector3(gx + r.x * cell + w * 0.5f, 0f,
                                                height * 0.5f - (row + 0.5f) * cell);
                        p.Turned(centre + q * local, yaw, w, cell, y);
                    }
                }
            }
        }
    }
}
