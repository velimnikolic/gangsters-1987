using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    // The road surface of a motorway, drawn rather than tiled.
    //
    // The pack has ONE piece of motorway: a straight carriageway 11.4 m wide and 20 m
    // long, with a parapet down one edge. There is no bend of any radius a motorway
    // uses, no seven-metre slip road, no widening for an auxiliary lane and no gore.
    // Laying the straight piece round a 260 m curve leaves a four-degree kink in the
    // parapet every twenty metres, and laying it down a one-lane ramp puts eleven
    // metres of carriageway where seven belong - which is what the last three attempts
    // at a freeway did and what they looked like.
    //
    // So the road is EXTRUDED: a cross-section - beam, fascia, deck, parapet - swept
    // along the line the road actually follows, banked into its bends, at whatever
    // width that stretch of road is. The material and the texture coordinates are the
    // Synty deck's OWN, read off its mesh (Probe), so the drawn road and the pack's
    // pieces are the same road; a pack that changes its atlas changes both.
    public static class DeckMesh
    {
        /// <summary>What the road is painted with: the pack's material, the strip of its
        /// atlas the carriageway's surface uses, and a point of plain concrete for
        /// everything that is not road.</summary>
        public struct Skin
        {
            public Material Mat;
            public Vector2 RoadA, RoadB;      // the atlas across the carriageway, kerb to kerb
            public float RoadV;               // and how far up it a metre of road goes
            public Vector2 Concrete;
            public bool Real;                 // read off the pack (else a plain grey stand-in)

            /// <summary>And what the RUNNING SURFACE is made of, where that is not the
            /// same thing as the structure. A viaduct is concrete and the road on top of
            /// it is the same black stuff as the street it came off; drawn in one colour
            /// it was a mile of pale grey running into a dark town, with a seam across
            /// the road wherever the two met. Null: the structure's own surface.</summary>
            public Material RoadMat;
            public Vector2 RoadUV;

            /// <summary>The same structure with that surface laid on it.</summary>
            public Skin Surfaced(Skin surface)
            {
                if (!surface.Real || surface.Mat == null) return this;
                var s = this;
                s.RoadMat = surface.Mat;
                s.RoadUV = surface.Concrete;
                return s;
            }
        }

        // the section, in metres off the road surface
        public const float Parapet = 1.05f;      // wall above the deck
        public const float Kerb = 0.35f;  // and how thick it is
        const float Fascia = 0.5f;        // the edge beam's face
        const float Beam = 1.55f;         // the soffit under the road (the pack's own)
        const float Inset = 0.6f;         // the soffit is drawn in from the edge

        static readonly Dictionary<GameObject, Skin> Skins = new Dictionary<GameObject, Skin>();
        static readonly Dictionary<GameObject, Skin> Flats = new Dictionary<GameObject, Skin>();

        /// <summary>The pack's own colours: the strip of atlas its deck's carriageway
        /// uses across its width, and a patch of the concrete its parapet is made of.
        /// Read once off the mesh - not typed in, so a pack that moves its atlas moves
        /// the drawn road with it.</summary>
        public static Skin Probe(GameObject prefab)
        {
            if (prefab == null) return Grey();
            if (Skins.TryGetValue(prefab, out var had)) return had;
            var skin = Read(prefab);
            Skins[prefab] = skin;
            Debug.Log($"[expressway] deck skin off {prefab.name}: " +
                      (skin.Real ? $"road u {skin.RoadA.x:F3}..{skin.RoadB.x:F3} v {skin.RoadA.y:F3}, " +
                                   $"concrete ({skin.Concrete.x:F3}, {skin.Concrete.y:F3})"
                                 : "not readable - plain concrete instead"));
            return skin;
        }

        /// <summary>A road drawn in ONE colour: a material, and a single point of its
        /// atlas that a flat piece of the kit's own paving uses. The town's road cell is
        /// a plain square with no markings and no run to it, so a road drawn to match it
        /// wants no strip and no metre count - just the colour, everywhere.
        ///
        /// This is what a ramp is made of once it is on the ground. The drawn motorway
        /// takes its surface off the pack's own motorway piece, and that piece is from a
        /// DIFFERENT PACK to the streets: PolygonPalmCity's SM_Env_Road_Highway_01 wears
        /// Road_Grey_01, whose asphalt is rgb(118, 113, 107), and every street in this
        /// city is PolygonCity's Road_01 at rgb(70, 69, 70) - measured off the two
        /// atlases, not guessed. The ramp arrived at a town junction seventy per cent
        /// brighter than the road it was joining, with a seam straight across it.
        ///
        /// On the viaduct the grey is right: a viaduct is not a street, and the pack's
        /// own pillars stand under it in that same grey. On the ground it is not.</summary>
        public static Skin Flat(GameObject prefab)
        {
            if (prefab == null) return Grey();
            if (Flats.TryGetValue(prefab, out var had)) return had;
            var skin = ReadFlat(prefab);
            Flats[prefab] = skin;
            Debug.Log($"[expressway] ground asphalt off {prefab.name}: " +
                      (skin.Real ? $"{skin.Mat.name} at ({skin.Concrete.x:F3}, {skin.Concrete.y:F3})"
                                 : "not readable - plain concrete instead"));
            return skin;
        }

        static Skin ReadFlat(GameObject prefab)
        {
            MeshFilter best = null;
            float bestArea = 0f;
            foreach (var f in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (f.sharedMesh == null) continue;
                var size = f.sharedMesh.bounds.size;
                float area = size.x * size.z;
                if (area <= bestArea) continue;
                bestArea = area;
                best = f;
            }
            var mr = best != null ? best.GetComponent<MeshRenderer>() : null;
            var uv = best != null ? best.sharedMesh.uv : null;
            if (mr == null || mr.sharedMaterial == null || uv == null || uv.Length == 0) return Grey();

            // the middle of the face's patch of atlas, not a corner of it: the piece is a
            // flat square of plain asphalt, so its middle is the colour of the road and
            // its edges are wherever the atlas keeps the next thing along
            var sum = Vector2.zero;
            for (int i = 0; i < uv.Length; i++) sum += uv[i];
            var at = sum / uv.Length;
            return new Skin
            {
                Mat = mr.sharedMaterial,
                RoadA = at, RoadB = at, RoadV = 0f, Concrete = at,
                Real = true,
            };
        }

        static Skin Grey()
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(sh) { name = "Expressway concrete" };
            m.color = new Color(0.58f, 0.57f, 0.55f);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.12f);
            return new Skin { Mat = m, RoadA = Vector2.zero, RoadB = Vector2.zero, RoadV = 0f, Concrete = Vector2.zero };
        }

        static Skin Read(GameObject prefab)
        {
            MeshFilter best = null;
            float bestArea = 0f;
            foreach (var f in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (f.sharedMesh == null) continue;
                var b = f.sharedMesh.bounds.size;
                float area = b.x * b.z;
                if (area > bestArea) { bestArea = area; best = f; }
            }
            var mr = best != null ? best.GetComponent<MeshRenderer>() : null;
            if (best == null || mr == null || mr.sharedMaterial == null) return Grey();

            var mesh = best.sharedMesh;
            var v = mesh.vertices;
            var uv = mesh.uv;
            var tris = mesh.triangles;
            if (uv == null || uv.Length != v.Length) return Grey();

            // the carriageway: the big upward-facing faces at the top of the beam, which
            // on this piece is its own y = 0. The parapet's top is whatever else faces up.
            float roadY = float.MinValue, wallY = float.MinValue;
            for (int i = 0; i < tris.Length; i += 3)
            {
                var a = v[tris[i]]; var b = v[tris[i + 1]]; var c = v[tris[i + 2]];
                var n = Vector3.Cross(b - a, c - a);
                if (n.sqrMagnitude < 1e-9f || n.normalized.y < 0.8f) continue;
                float y = (a.y + b.y + c.y) / 3f;
                float area = n.magnitude * 0.5f;
                if (area > 4f && y > roadY - 0.05f && y < 0.6f) roadY = Mathf.Max(roadY, y);
                if (y > wallY) wallY = y;
            }
            if (roadY == float.MinValue) return Grey();

            var road = new List<int>();
            var wall = new List<int>();
            for (int i = 0; i < tris.Length; i += 3)
            {
                var a = v[tris[i]]; var b = v[tris[i + 1]]; var c = v[tris[i + 2]];
                var n = Vector3.Cross(b - a, c - a);
                if (n.sqrMagnitude < 1e-9f || n.normalized.y < 0.8f) continue;
                float y = (a.y + b.y + c.y) / 3f;
                var into = Mathf.Abs(y - roadY) < 0.12f ? road : Mathf.Abs(y - wallY) < 0.25f ? wall : null;
                if (into == null) continue;
                into.Add(tris[i]); into.Add(tris[i + 1]); into.Add(tris[i + 2]);
            }
            if (road.Count < 3) return Grey();

            // which way the atlas runs across the carriageway: the axis of uv that moves
            // with the piece's own x. Told by which correlates, not by which is bigger -
            // a square patch of atlas would answer that one wrong.
            double sx = 0, su = 0, sv = 0, sxu = 0, sxv = 0, sxx = 0;
            int n2 = 0;
            foreach (int i in road) { sx += v[i].x; su += uv[i].x; sv += uv[i].y; n2++; }
            double mx = sx / n2, mu = su / n2, mv = sv / n2;
            foreach (int i in road)
            {
                double dx = v[i].x - mx;
                sxu += dx * (uv[i].x - mu);
                sxv += dx * (uv[i].y - mv);
                sxx += dx * dx;
            }
            bool acrossIsU = System.Math.Abs(sxu) >= System.Math.Abs(sxv);
            float slope = (float)((acrossIsU ? sxu : sxv) / System.Math.Max(1e-6, sxx));

            float xLo = float.MaxValue, xHi = float.MinValue;
            foreach (int i in road) { xLo = Mathf.Min(xLo, v[i].x); xHi = Mathf.Max(xHi, v[i].x); }
            float cAcross = (float)(acrossIsU ? mu : mv);
            float aAcross = cAcross + slope * (xLo - (float)mx);
            float bAcross = cAcross + slope * (xHi - (float)mx);
            float along = (float)(acrossIsU ? mv : mu);

            // and how far up the atlas a metre of road goes, so the surface tiles down
            // its length the way the piece does
            float zLo = float.MaxValue, zHi = float.MinValue, alongLo = 0f, alongHi = 0f;
            foreach (int i in road)
            {
                float a2 = acrossIsU ? uv[i].y : uv[i].x;
                if (v[i].z < zLo) { zLo = v[i].z; alongLo = a2; }
                if (v[i].z > zHi) { zHi = v[i].z; alongHi = a2; }
            }
            float perMetre = zHi - zLo > 1f ? (alongHi - alongLo) / (zHi - zLo) : 0f;

            Vector2 concrete = new Vector2(0.5f, 0.5f);
            if (wall.Count >= 3)
            {
                double cu = 0, cv = 0;
                foreach (int i in wall) { cu += uv[i].x; cv += uv[i].y; }
                concrete = new Vector2((float)(cu / wall.Count), (float)(cv / wall.Count));
            }

            var skin = new Skin
            {
                Mat = mr.sharedMaterial,
                RoadA = acrossIsU ? new Vector2(aAcross, along) : new Vector2(along, aAcross),
                RoadB = acrossIsU ? new Vector2(bAcross, along) : new Vector2(along, bAcross),
                RoadV = perMetre,
                Concrete = concrete,
                Real = true,
            };
            // the strip has to have some width in the atlas or the road is one colour
            if ((skin.RoadB - skin.RoadA).sqrMagnitude < 1e-8f) skin.RoadV = 0f;
            return skin;
        }

        // ------------------------------------------------------------------ the sweep

        struct Pt { public float X, Y; public Vector2 UV; public bool Road; }

        // A wall is a HEIGHT, not a yes or no. Where a motorway opens - at the nose of
        // a gore, where the deck hands a lane to a ramp - the parapet does not stop
        // dead: it comes down over a few metres and the road runs on without it. Given a
        // bool the section would have to change its point count to say that, and a swept
        // section whose point count changes is not a sweep at all; given a height it says
        // it by lying flat, which costs two triangles of nothing.
        static void Section(List<Pt> into, float lo, float hi, float wallLo, float wallHi, Skin skin)
        {
            into.Clear();
            float roadLo = lo + (wallLo > 0.01f ? Kerb : 0f);
            float roadHi = hi - (wallHi > 0.01f ? Kerb : 0f);
            Vector2 C = skin.Concrete;
            Vector2 RoadUV(float x)
            {
                if (skin.RoadMat != null) return skin.RoadUV;      // one colour, no strip
                float t = Mathf.InverseLerp(roadLo, roadHi, x);
                return Vector2.LerpUnclamped(skin.RoadA, skin.RoadB, t);
            }

            into.Add(new Pt { X = lo, Y = -Fascia, UV = C });
            into.Add(new Pt { X = lo, Y = wallLo, UV = C });
            into.Add(new Pt { X = roadLo, Y = wallLo, UV = C });
            into.Add(new Pt { X = roadLo, Y = 0f, UV = C });

            into.Add(new Pt { X = roadLo, Y = 0f, UV = RoadUV(roadLo), Road = true });
            into.Add(new Pt { X = roadHi, Y = 0f, UV = RoadUV(roadHi), Road = true });

            into.Add(new Pt { X = roadHi, Y = 0f, UV = C });
            into.Add(new Pt { X = roadHi, Y = wallHi, UV = C });
            into.Add(new Pt { X = hi, Y = wallHi, UV = C });
            into.Add(new Pt { X = hi, Y = -Fascia, UV = C });

            // and back along the underside
            into.Add(new Pt { X = hi - Inset, Y = -Beam, UV = C });
            into.Add(new Pt { X = lo + Inset, Y = -Beam, UV = C });
            into.Add(new Pt { X = lo, Y = -Fascia, UV = C });
        }

        /// <summary>How much a bend of this radius is banked: six per cent on anything a
        /// motorway calls a curve, easing off on the long ones.</summary>
        static float Bank(float radius) => radius > 1500f ? 0f : Mathf.Min(0.06f, 90f / Mathf.Max(40f, radius) * 0.06f);

        /// <summary>How much higher the road surface stands <paramref name="across"/>
        /// metres out from the line it was swept along than it does on the line itself:
        /// the bank of the bend, which every section of it gets (Build). Anything stood
        /// ON the road - a light beside the median, a sign on the shoulder - is that far
        /// off the profile's own height, and on this road's corners that is fifteen
        /// centimetres of a post buried or a post in the air.</summary>
        public static float Camber(RoadLine line, float s, float across)
            => -across * Bank(line.RadiusAt(s)) * TurnSign(line, s);

        /// <summary>A stretch of road drawn along a line: the section swept from s0 to
        /// s1, at the height the profile gives it, as wide as it is asked to be, banked
        /// into its bends.
        ///
        /// <paramref name="widthAt"/> answers the half widths (lo, hi) at a station -
        /// which is how a deck grows an auxiliary lane and tapers back out of it - and
        /// <paramref name="heightAt"/> the surface's height there.</summary>
        /// <param name="maxStep">The longest a section may be. The step is taken off
        /// the bend's radius, which on a road whose WIDTH is what changes - a ramp
        /// clipped to the deck it is leaving - says sixteen metres for a taper that
        /// wants four.</param>
        public static GameObject Build(RoadLine line, float s0, float s1,
                                       System.Func<float, float> heightAt,
                                       System.Func<float, Vector2> widthAt,
                                       System.Func<float, Vector2> wallAt, Skin skin,
                                       Transform parent, string name, float maxStep = 999f)
        {
            if (line == null || s1 - s0 < 0.5f) return null;
            var stations = new List<float>();
            float s = s0;
            while (s < s1 - 0.01f)
            {
                stations.Add(s);
                float r = line.RadiusAt(s);
                float step = r < 60f ? 2f : r < 200f ? 4f : r < 900f ? 8f : 16f;
                s += Mathf.Min(step, maxStep);
            }
            stations.Add(s1);
            int n = stations.Count;

            var sec = new List<Pt>();
            var w0 = widthAt(s0);
            var h0 = wallAt != null ? wallAt(s0) : new Vector2(Parapet, Parapet);
            Section(sec, w0.x, w0.y, h0.x, h0.y, skin);
            int m = sec.Count;
            // which of the section's edges is the carriageway. Taken once: the section is
            // swept again at every station and the widths move, but what is road and what
            // is concrete does not.
            var carriageway = new bool[m];
            for (int i = 0; i < m; i++) carriageway[i] = sec[i].Road;

            var verts = new List<Vector3>(m * n * 2);
            var uvs = new List<Vector2>(m * n * 2);
            var tris = new List<int>(m * n * 6);
            var road = new List<int>(n * 6);
            var origin = line.PointAt((s0 + s1) * 0.5f);

            // one strip of its own for every edge of the section: shared along the road
            // (so it is smooth down its length) and never across it (so the parapet does
            // not shade into the carriageway)
            for (int e = 0; e + 1 < m; e++)
            {
                int baseIndex = verts.Count;
                for (int k = 0; k < n; k++)
                {
                    float sk = stations[k];
                    var w = widthAt(sk);
                    var wall = wallAt != null ? wallAt(sk) : new Vector2(Parapet, Parapet);
                    Section(sec, w.x, w.y, wall.x, wall.y, skin);
                    var p = line.PointAt(sk);
                    var right = line.RightAt(sk);
                    float y = heightAt != null ? heightAt(sk) : 0f;
                    float bank = Bank(line.RadiusAt(sk)) * TurnSign(line, sk);
                    for (int j = 0; j < 2; j++)
                    {
                        var pt = sec[e + j];
                        var at = p + right * pt.X;
                        at.y = y + pt.Y - pt.X * bank;
                        verts.Add(at - origin);
                        var t = pt.UV;
                        if (pt.Road && skin.RoadMat == null && skin.RoadV != 0f)
                        {
                            // the surface runs on down the road, so its markings do too
                            var d = skin.RoadB - skin.RoadA;
                            var alongUV = new Vector2(-d.y, d.x).normalized * (skin.RoadV * sk);
                            t += alongUV;
                        }
                        uvs.Add(t);
                    }
                }
                var into = skin.RoadMat != null && carriageway[e] && carriageway[e + 1] ? road : tris;
                for (int k = 0; k + 1 < n; k++)
                {
                    int a = baseIndex + k * 2, b = a + 1, c = a + 2, d2 = a + 3;
                    into.Add(a); into.Add(c); into.Add(b);
                    into.Add(b); into.Add(c); into.Add(d2);
                }
            }

            var mesh = new Mesh { name = name + " mesh" };
            if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            if (road.Count > 0)
            {
                mesh.subMeshCount = 2;
                mesh.SetTriangles(tris, 0);
                mesh.SetTriangles(road, 1);
            }
            else mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = origin;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r2 = go.AddComponent<MeshRenderer>();
            if (road.Count > 0) r2.sharedMaterials = new[] { skin.Mat, skin.RoadMat };
            else r2.sharedMaterial = skin.Mat;
            return go;
        }

        /// <summary>A painted line down a road: a flat strip a hair over the surface,
        /// solid or broken. The pack bakes its markings into the twenty-metre deck piece
        /// and there is no such thing for a road that is drawn instead of tiled, so the
        /// lines are drawn too - which is also the only way to mark a lane that appears
        /// half way along a carriageway and is gone again four hundred metres later.</summary>
        public static GameObject Paint(RoadLine line, float s0, float s1,
                                       System.Func<float, float> heightAt,
                                       System.Func<float, float> offsetAt,
                                       float width, bool broken, Material mat,
                                       Transform parent, string name)
        {
            if (line == null || mat == null || s1 - s0 < 0.15f) return null;
            const float dash = 4f, gap = 8f, lift = 0.02f;
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            var origin = line.PointAt((s0 + s1) * 0.5f);

            void Strip(float from, float to)
            {
                float step = 4f;
                int n = Mathf.Max(1, Mathf.CeilToInt((to - from) / step));
                int b = verts.Count;
                for (int i = 0; i <= n; i++)
                {
                    float s = Mathf.Lerp(from, to, i / (float)n);
                    var p = line.PointAt(s);
                    var r = line.RightAt(s);
                    float d = offsetAt != null ? offsetAt(s) : 0f;
                    float y = (heightAt != null ? heightAt(s) : 0f) + lift;
                    var l = p + r * (d - width * 0.5f); l.y = y;
                    var rt = p + r * (d + width * 0.5f); rt.y = y;
                    verts.Add(l - origin); uvs.Add(new Vector2(0f, i));
                    verts.Add(rt - origin); uvs.Add(new Vector2(1f, i));
                }
                for (int i = 0; i < n; i++)
                {
                    int a = b + i * 2;
                    tris.Add(a); tris.Add(a + 2); tris.Add(a + 1);
                    tris.Add(a + 1); tris.Add(a + 2); tris.Add(a + 3);
                }
            }

            if (broken)
                for (float s = s0; s < s1 - 0.5f; s += dash + gap)
                    Strip(s, Mathf.Min(s + dash, s1));
            else Strip(s0, s1);
            if (verts.Count == 0) return null;

            var mesh = new Mesh { name = name + " paint" };
            if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = origin;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        /// <summary>Which way the road is turning here: +1 to the right, -1 to the left,
        /// 0 on a straight - so the bank tips the right way round.</summary>
        static float TurnSign(RoadLine line, float s)
        {
            float ds = 6f;
            var a = line.DirAt(Mathf.Max(0f, s - ds));
            var b = line.DirAt(Mathf.Min(line.Length, s + ds));
            float cross = a.x * b.z - a.z * b.x;
            return cross < -1e-4f ? 1f : cross > 1e-4f ? -1f : 0f;
        }
    }
}
