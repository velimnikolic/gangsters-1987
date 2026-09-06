using UnityEngine;

namespace SuburbDemo
{
    // The lie of the land. A gentle heightfield under the whole suburb - three slow
    // waves and a handful of hillocks and hollows, slopes of a few per cent, nothing a
    // street would not be graded over - fading to flat at the edge of the map so the
    // city's ground (or the lawn skirt) meets it. Everything that is placed asks
    // TownKit.Ground for its height: the 5 m tiles tilt to the local slope, props and
    // fences stand on it, the houses sit level with their foundations in it, the
    // walkers' nodes are on it and the cars are lifted onto it as they go (OnPlaced).
    public partial class SuburbDistrict
    {
        // three directional waves and some bumps, rolled once per plan
        readonly float[] _waveDir = new float[3], _waveLen = new float[3], _wavePhase = new float[3], _waveAmp = new float[3];
        readonly Vector4[] _bumps = new Vector4[6];   // x, z, sigma, height
        int _bumpCount;
        bool _reliefRolled;

        void RollRelief()
        {
            _reliefRolled = true;
            float[] lens = { 260f, 340f, 420f }, amps = { 0.7f, 0.45f, 0.3f };
            for (int k = 0; k < 3; k++)
            {
                _waveDir[k] = Rnd(0f, Mathf.PI);
                _waveLen[k] = lens[k] * Rnd(0.85f, 1.2f);
                _wavePhase[k] = Rnd(0f, Mathf.PI * 2f);
                _waveAmp[k] = amps[k];
            }
            _bumpCount = Mathf.Min(_bumps.Length, 3 + Rnd(3));
            for (int k = 0; k < _bumpCount; k++)
                _bumps[k] = new Vector4(Rnd(60f, MapWidth - 60f), Rnd(60f, MapHeight - 60f), Rnd(28f, 45f), Rnd(0.6f, 1.0f) * (Chance(0.6f) ? 1f : -1f));
        }

        /// <summary>The ground's height at a point of the suburb's own plan: 0 when the
        /// relief is off, 0 outside the map and within 40 m of its edge.</summary>
        public float Ground(float x, float z)
        {
            if (relief <= 0f || !_reliefRolled) return 0f;
            float mw = MapWidth, mh = MapHeight;
            if (x <= 0f || z <= 0f || x >= mw || z >= mh) return 0f;
            float h = 0f;
            for (int k = 0; k < 3; k++)
            {
                float along = x * Mathf.Cos(_waveDir[k]) + z * Mathf.Sin(_waveDir[k]);
                h += _waveAmp[k] * Mathf.Sin(along / _waveLen[k] * Mathf.PI * 2f + _wavePhase[k]);
            }
            for (int k = 0; k < _bumpCount; k++)
            {
                float dx = x - _bumps[k].x, dz = z - _bumps[k].y, s = _bumps[k].z;
                h += _bumps[k].w * Mathf.Exp(-(dx * dx + dz * dz) / (2f * s * s));
            }
            h *= relief;
            // fade to the flat edge
            float edge = Mathf.Min(Mathf.Min(x, mw - x), Mathf.Min(z, mh - z));
            if (edge < 40f) { float t = edge / 40f; h *= t * t * (3f - 2f * t); }
            // hung off the city the island's ground is flat at 0 under the suburb: hills only, no hollows below it
            if (_links != null) h = Mathf.Max(0f, h);
            return h;
        }

        /// <summary>The ground's height at a world point, for the cars once the suburb stands in its place.</summary>
        float GroundWorld(float wx, float wz)
        {
            var own = _placed ? _inner.ToLocal(new Vector3(wx, 0f, wz)) : new Vector3(wx, 0f, wz);
            return Ground(own.x, own.z);
        }

        /// <summary>The ground's unit normal at a point (finite differences).</summary>
        public Vector3 GroundNormal(float x, float z)
        {
            const float d = 1.5f;
            float hx = Ground(x + d, z) - Ground(x - d, z);
            float hz = Ground(x, z + d) - Ground(x, z - d);
            return new Vector3(-hx / (2f * d), 1f, -hz / (2f * d)).normalized;
        }

        /// <summary>Builds the heightfield mesh that replaces the flat ground plane in
        /// the suburb's own scene: under everything, just below the pavement tiles'
        /// gutters, the hills of the map and the flat skirt far beyond it.</summary>
        void BuildGroundMesh(Transform parent)
        {
            const float step = 10f;
            float skirt = _host.ProvidesGround ? 0f : 600f;
            float x0 = -skirt, z0 = -skirt, x1 = MapWidth + skirt, z1 = MapHeight + skirt;
            int nx = Mathf.CeilToInt((x1 - x0) / step), nz = Mathf.CeilToInt((z1 - z0) / step);
            var verts = new Vector3[(nx + 1) * (nz + 1)];
            var uvs = new Vector2[verts.Length];
            for (int j = 0; j <= nz; j++)
                for (int i = 0; i <= nx; i++)
                {
                    float x = x0 + i * step, z = z0 + j * step;
                    verts[j * (nx + 1) + i] = new Vector3(x, Ground(x, z) - 0.27f, z);
                    uvs[j * (nx + 1) + i] = new Vector2(x / 50f, z / 50f);
                }
            var tris = new int[nx * nz * 6];
            int t = 0;
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    int a = j * (nx + 1) + i, b = a + 1, c = a + nx + 1, d = c + 1;
                    tris[t++] = a; tris[t++] = c; tris[t++] = b;
                    tris[t++] = b; tris[t++] = c; tris[t++] = d;
                }
            var mesh = new Mesh { name = "Suburb Ground", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var go = new GameObject("Ground");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _lawnMat ?? TownKit.Flat("Suburb Ground", new Color(0.33f, 0.47f, 0.2f));
            go.AddComponent<RoadDemo.LandscapeResources>().Mesh(go);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }
}
