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

        GameObject _lightWhite, _lightAmber, _lightGreen, _lightRed, _lightBlue;
        GameObject _conePrefab, _bollardPrefab;

        void LoadKit()
        {
            _grassMat = AirportKit.LoadMaterial(AirportKit.GrassMat) ?? AirportKit.Flat("airport grass", new Color(0.35f, 0.47f, 0.25f));
            _concreteMat = AirportKit.LoadMaterial(AirportKit.ConcreteMat) ?? AirportKit.Flat("airport concrete", new Color(0.62f, 0.62f, 0.60f));
            _asphaltMat = AirportKit.LoadMaterial(AirportKit.AsphaltMat) ?? AirportKit.Flat("airport asphalt", new Color(0.28f, 0.28f, 0.29f));
            // the shoulders are the same tarmac, a shade darker, so the runway edge
            // reads as an edge from the air even before the paint goes on
            _shoulderMat = AirportKit.LoadMaterial(AirportKit.AsphaltMat) ?? AirportKit.Flat("airport shoulder", new Color(0.22f, 0.22f, 0.23f));
            if (_shoulderMat != null && _shoulderMat.HasProperty("_BaseColor"))
                _shoulderMat.SetColor("_BaseColor", _shoulderMat.GetColor("_BaseColor") * 0.72f);
            _shoulderMat.name = "airport shoulder";

            // airfield paint. No pack has a road decal at runway scale - a centreline
            // stripe is 36 m long and 90 cm wide - so the markings are flat colour on
            // quads, which is what airfield paint is.
            _whitePaint = AirportKit.Flat("airport paint white", new Color(0.88f, 0.88f, 0.85f), 0.05f);
            _yellowPaint = AirportKit.Flat("airport paint yellow", new Color(0.85f, 0.68f, 0.09f), 0.05f);
            _blackPaint = AirportKit.Flat("airport paint black", new Color(0.07f, 0.07f, 0.08f), 0.05f);
            _redPaint = AirportKit.Flat("airport paint red", new Color(0.60f, 0.10f, 0.09f), 0.05f);
            _rubberMat = AirportKit.Flat("airport rubber", new Color(0.16f, 0.16f, 0.17f), 0.02f);

            _lightWhite = AirportKit.TryLoad(AirportKit.EdgeLights[AirportKit.LightWhite]);
            _lightAmber = AirportKit.TryLoad(AirportKit.EdgeLights[AirportKit.LightAmber]);
            _lightGreen = AirportKit.TryLoad(AirportKit.EdgeLights[AirportKit.LightGreen]);
            _lightRed = AirportKit.TryLoad(AirportKit.EdgeLights[AirportKit.LightRed]);
            _lightBlue = AirportKit.TryLoad(AirportKit.EdgeLights[AirportKit.LightBlue]);
            _conePrefab = AirportKit.TryLoad(AirportKit.Cone);
            _bollardPrefab = AirportKit.TryLoad(AirportKit.Bollard);
        }

        // ------------------------------------------------------------ surfaces

        /// <summary>One plane over a rectangle, subdivided at <paramref name="cell"/>,
        /// with the pack material's UV inset off the atlas edge and each cell turned a
        /// quarter at a time. The harbour's FlatPlane, which is how every large paved
        /// area in this project is laid.</summary>
        GameObject FlatPlane(string name, float x0, float x1, float z0, float z1, float y, Material mat, float cell, Transform parent)
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
            readonly List<int> _t = new List<int>();

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
                _t.Add(i); _t.Add(i + 2); _t.Add(i + 1);
                _t.Add(i); _t.Add(i + 3); _t.Add(i + 2);
                Count++;
            }

            public GameObject Emit(string name, Material mat, Transform parent)
            {
                if (_v.Count == 0) return null;
                var mesh = new Mesh { name = name, indexFormat = _v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.SetVertices(_v); mesh.SetNormals(_n); mesh.SetUVs(0, _uv); mesh.SetTriangles(_t, 0);
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
