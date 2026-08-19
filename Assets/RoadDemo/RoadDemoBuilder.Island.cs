using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RoadDemo
{
    // The island. Past its last road the city gives out into wilderness - the
    // island's own ground, rolling a little, wooded here and there, rock at the
    // water's edge - which slopes down through a beach into the sea that lies
    // all round. The ground is one heightfield ringing the grid (never under it:
    // the grid tiles its own floor), grass on top and sand where it drops toward
    // the water, and the sea is one plane under everything at the water level
    // the quays and the boats already stand at. The rivers leave the city as
    // channels carved through that ground, the highway rides on out over it.
    public partial class RoadDemoBuilder
    {
        [Header("The island")]
        [Tooltip("Metres of wild ground from the last road to the water on each side, before the coast wanders.")]
        public float islandWest = 460f;
        public float islandEast = 380f;
        public float islandNorth = 360f;
        public float islandSouth = 420f;
        [Tooltip("How far the coastline strays in and out from those widths.")]
        public float coastWander = 90f;
        [Tooltip("Woods per hectare, roughly - the wilderness's tree density.")]
        public float treesPerHectare = 30f;
        [Tooltip("How high the island's proper hills stand over the plain, out where the " +
                 "wild ground is wide enough to carry them (the knolls near the roads stay low).")]
        public float hillHeight = 26f;

        /// <summary>Metres between heightfield vertices; the shore's slope is a few of these.</summary>
        const float GroundStep = 8f;
        /// <summary>The seabed's depth off the beach; well below the water.</summary>
        const float SeabedY = -5f;
        /// <summary>How far past the coast the ground mesh (and the sea) run.</summary>
        const float SeaMargin = 260f;

        Transform _islandRoot;
        Transform IslandRoot => _islandRoot != null ? _islandRoot : (_islandRoot = new GameObject("Island").transform);

        readonly List<GameObject> _wildTrees = new List<GameObject>();
        readonly List<GameObject> _wildPines = new List<GameObject>();
        readonly List<GameObject> _wildDead = new List<GameObject>();
        readonly List<GameObject> _wildRocks = new List<GameObject>();
        readonly List<GameObject> _wildBushes = new List<GameObject>();
        readonly List<GameObject> _wildGrass = new List<GameObject>();
        readonly List<GameObject> _wildLogs = new List<GameObject>();
        readonly List<GameObject> _wildCliffs = new List<GameObject>();
        bool _wildKitLoaded;

        void LoadWildKit()
        {
            if (_wildKitLoaded) return;
            _wildKitLoaded = true;
            const string Gen = "Assets/Synty/PolygonGeneric/Prefabs/Environment/";
            void Bag(List<GameObject> into, string stem, int from, int to)
            {
                for (int k = from; k <= to; k++)
                {
                    var p = Load($"{Gen}{stem}_{k:00}.prefab");
                    if (p != null) into.Add(p);
                }
            }
            Bag(_wildTrees, "SM_Gen_Env_Tree", 1, 3);
            Bag(_wildPines, "SM_Gen_Env_Tree_Pine", 1, 3);
            Bag(_wildDead, "SM_Gen_Env_Tree_Dead", 1, 3);
            Bag(_wildRocks, "SM_Gen_Env_Rock", 1, 10);
            Bag(_wildBushes, "SM_Gen_Env_Bush", 1, 4);
            Bag(_wildBushes, "SM_Gen_Env_Bush_Large", 1, 4);
            Bag(_wildBushes, "SM_Gen_Env_Shrub", 1, 3);
            Bag(_wildGrass, "SM_Gen_Env_Grass_Tall", 1, 4);
            Bag(_wildGrass, "SM_Gen_Env_Grass", 1, 7);
            Bag(_wildGrass, "SM_Gen_Env_Fern", 1, 3);
            Bag(_wildGrass, "SM_Gen_Env_Flowers", 1, 8);
            Bag(_wildLogs, "SM_Gen_Env_Log", 1, 2);
            Bag(_wildLogs, "SM_Gen_Env_Stump", 1, 3);
            // the rock faces the military warehouse demo kitbashes its hill from:
            // dirt cliffs on the slopes, grey cliff heads at the crowns
            Bag(_wildCliffs, "SM_Gen_Env_Dirt_Cliff", 1, 8);
            Bag(_wildCliffs, "SM_Gen_Env_Cliff", 1, 4);
            // the city's own trees stand nearer the streets
            foreach (var t in _parkTrees) _wildTrees.Add(t);
        }

        // ---------------------------------------------------------------- terrain

        float _gx0, _gx1, _gz0, _gz1;
        int _coastSeed;

        /// <summary>The districts' own ground: rectangles that count as city rather than
        /// wilderness, so the coast is measured from THEIR edge and the island reaches
        /// out past them instead of drowning them. Filled by RoadDemoBuilder.Districts.cs
        /// before the island is built; empty in a city with no districts.</summary>
        readonly List<Rect> _landRects = new List<Rect>();

        /// <summary>What the districts asked of the ground: what they pave themselves,
        /// what must lie flat, what must be open water, where nothing grows.</summary>
        readonly DistrictReservations _reservations = new DistrictReservations();

        /// <summary>How far the point lies outside the city - the grid rectangle or any
        /// district's own ground, whichever it is nearest - and the way out from it:
        /// the nearest edge point and the outward direction.</summary>
        float OutsideGrid(float x, float z, out Vector2 dir, out Vector2 at)
        {
            float best = OutsideRect(x, z, _gx0, _gx1, _gz0, _gz1, out dir, out at);
            for (int i = 0; i < _landRects.Count && best > 0f; i++)
            {
                var r = _landRects[i];
                float d = OutsideRect(x, z, r.xMin, r.xMax, r.yMin, r.yMax, out var dir2, out var at2);
                if (d < best) { best = d; dir = dir2; at = at2; }
            }
            return best;
        }

        static float OutsideRect(float x, float z, float x0, float x1, float z0, float z1,
                                 out Vector2 dir, out Vector2 at)
        {
            float cx = Mathf.Clamp(x, x0, x1), cz = Mathf.Clamp(z, z0, z1);
            at = new Vector2(cx, cz);
            var d = new Vector2(x - cx, z - cz);
            float len = d.magnitude;
            dir = len > 1e-3f ? d / len : Vector2.zero;
            return len;
        }

        /// <summary>The coast's distance from the grid in the direction <paramref name="dir"/>
        /// at edge point <paramref name="at"/>: the side's own width, blended round the
        /// corners, and wandering with a slow noise so no shore runs straight.</summary>
        float CoastDistance(Vector2 dir, Vector2 at)
        {
            float wx = dir.x > 0f ? islandEast : islandWest;
            float wz = dir.y > 0f ? islandNorth : islandSouth;
            float w = dir.x * dir.x * wx + dir.y * dir.y * wz; // dir is unit: weights sum to 1
            float n = Mathf.PerlinNoise((at.x + _coastSeed) * 0.0032f, (at.y - _coastSeed) * 0.0032f) - 0.5f;
            float n2 = Mathf.PerlinNoise((at.x - _coastSeed) * 0.011f, (at.y + _coastSeed) * 0.011f) - 0.5f;
            return Mathf.Max(30f, w + n * 2f * coastWander + n2 * 0.4f * coastWander);
        }

        /// <summary>The island's height at (x, z): flush with the pavement beside the
        /// grid, then rolling wild ground, then the beach's slope into the sea; carved
        /// down where a river runs out to the water.</summary>
        float IslandHeight(float x, float z)
        {
            // a district's basin is water wherever it says so, whatever the coast does:
            // a ship has to be able to sail in to the quay
            if (_reservations.InWater(x, z)) return SeabedY;

            float d = OutsideGrid(x, z, out var dir, out var at);
            if (d <= 0f) return 0f;
            float coast = CoastDistance(dir, at);

            // rolling ground: low knolls, never a cliff, and dead flat by the road
            float hills = (Mathf.PerlinNoise((x + 311f) * 0.010f, (z - 173f) * 0.010f) - 0.32f) * 9f;
            hills += (Mathf.PerlinNoise(x * 0.041f, z * 0.041f) - 0.5f) * 1.2f;

            // the proper hills, after the military warehouse demo's: a slow noise whose
            // high lobes stand up as real relief, only out where the wild ground is deep
            // (never shouldering the last sidewalk) and dying away again well before the
            // coast so the beach stays a beach and no summit drops sheer into the sea
            float lobe = Mathf.PerlinNoise((x - 1907f) * 0.0042f, (z + 1313f) * 0.0042f);
            lobe = Mathf.Max(0f, lobe - 0.45f) / 0.55f;                  // hills, and plains between them
            float far = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(55f, 210f, d));
            float coastFade = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(coast - 150f, coast - 60f, d));
            hills += far * coastFade * hillHeight * lobe * lobe;

            // the freeway's corridor: the relief dies away across a broad shoulder on
            // both sides of its grade-level run, or a knoll would bury the deck
            hills *= HighwayFade(x, z);

            float rise = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(14f, 70f, d));
            float land = 0.03f + rise * Mathf.Max(0.05f, hills);

            // the beach: the ground slides from land level down under the water over
            // the last stretch before the coast, and on down to the seabed past it
            float shore = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(coast - 42f, coast + 6f, d));
            float h = Mathf.Lerp(land, SeabedY - Mathf.Min(6f, (d - coast) * 0.03f), shore);

            // the rivers' channels out to the sea
            if (seams != null)
                foreach (var s in seams)
                {
                    if (s == null || s.kind != SeamKind.River) continue;
                    var span = SeamSpan(s);
                    float across = s.vertical ? x : z;
                    float outOf = Mathf.Max(0f, span.lo - across, across - span.hi);
                    if (outOf > 14f) continue;
                    // in the channel: bed; on its lip: the bank sloping down to it
                    float bank = 1f - Mathf.SmoothStep(0f, 1f, outOf / 14f);
                    h = Mathf.Lerp(h, Mathf.Min(h, SeabedY + 1f), bank);
                }

            // a district's apron: held flat at its own level, easing back into the wild
            // ground over the last few metres so nothing steps
            if (_reservations.FlatAt(x, z, FlatBlend, out float level, out float w))
                h = Mathf.Lerp(h, level, w);
            return h;
        }

        /// <summary>Over how many metres a district's flat ground eases into the hills.
        /// Wider than it was when the knolls were knee-high: a 25 m hill needs a longer
        /// shoulder to come down onto an apron without a wall of grass.</summary>
        const float FlatBlend = 38f;

        // ------------------------------------------------------------------ build

        void BuildIsland(float gx0, float gx1, float gz0, float gz1)
        {
            _gx0 = gx0; _gx1 = gx1; _gz0 = gz0; _gz1 = gz1;
            _coastSeed = spacingSeed * 37 % 1000;
            LoadSeamKit();
            LoadWildKit();

            float reachW = islandWest + coastWander + SeaMargin, reachE = islandEast + coastWander + SeaMargin;
            float reachS = islandSouth + coastWander + SeaMargin, reachN = islandNorth + coastWander + SeaMargin;
            // the island rings the CITY: the grid and every district hanging off it
            float lx0 = gx0, lx1 = gx1, lz0 = gz0, lz1 = gz1;
            foreach (var r in _landRects)
            {
                lx0 = Mathf.Min(lx0, r.xMin); lx1 = Mathf.Max(lx1, r.xMax);
                lz0 = Mathf.Min(lz0, r.yMin); lz1 = Mathf.Max(lz1, r.yMax);
            }
            foreach (var r in _reservations.Water)
            {
                lx0 = Mathf.Min(lx0, r.xMin); lx1 = Mathf.Max(lx1, r.xMax);
                lz0 = Mathf.Min(lz0, r.yMin); lz1 = Mathf.Max(lz1, r.yMax);
            }
            float x0 = lx0 - reachW, x1 = lx1 + reachE, z0 = lz0 - reachS, z1 = lz1 + reachN;

            BuildGround(x0, x1, z0, z1);

            // the sea: one plane under everything, out past the ground's edge, at the
            // level the quays and boats already stand at
            WaterTiles(x0 - 400f, x1 + 400f, z0 - 400f, z1 + 400f);
            foreach (Transform t in SeamsRoot)
                if (t.name == "Water") t.gameObject.name = "Sea";

            DressWilderness(x0, x1, z0, z1);
        }

        // The ground as a heightfield over the island's whole extent, cells inside
        // the grid rectangle left out; grass on the land triangles, sand where they
        // dip toward the water. World-space triplanar materials, so no UVs.
        void BuildGround(float x0, float x1, float z0, float z1)
        {
            int nx = Mathf.CeilToInt((x1 - x0) / GroundStep), nz = Mathf.CeilToInt((z1 - z0) / GroundStep);
            var verts = new Vector3[(nx + 1) * (nz + 1)];
            for (int j = 0; j <= nz; j++)
                for (int i = 0; i <= nx; i++)
                {
                    float x = x0 + i * GroundStep, z = z0 + j * GroundStep;
                    verts[j * (nx + 1) + i] = new Vector3(x, IslandHeight(x, z), z);
                }
            var grass = new List<int>();
            var sand = new List<int>();
            const float BeachLine = -0.35f; // below this a triangle wears sand
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    float cx = x0 + (i + 0.5f) * GroundStep, cz = z0 + (j + 0.5f) * GroundStep;
                    // no ground under the grid: it tiles its own floor - nor under a
                    // district, which tiles its own
                    if (cx > _gx0 && cx < _gx1 && cz > _gz0 && cz < _gz1) continue;
                    if (_reservations.InPaved(cx, cz)) continue;
                    int a = j * (nx + 1) + i, b = a + 1, c = a + nx + 1, d = c + 1;
                    float low = Mathf.Min(verts[a].y, verts[b].y, verts[c].y, verts[d].y);
                    var into = low < BeachLine ? sand : grass;
                    into.Add(a); into.Add(c); into.Add(b);
                    into.Add(b); into.Add(c); into.Add(d);
                }

            var mesh = new Mesh { name = "Island Ground", indexFormat = IndexFormat.UInt32 };
            mesh.vertices = verts;
            mesh.subMeshCount = 2;
            mesh.SetTriangles(grass, 0);
            mesh.SetTriangles(sand, 1);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject("Ground");
            go.transform.SetParent(IslandRoot, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = new[] { GrassMaterial(), SandMaterial() };
            mr.shadowCastingMode = ShadowCastingMode.On;
            go.isStatic = true;
        }

        Material _grassMat, _sandMat;

        /// <summary>PalmCity's triplanar ground as it comes: grass on top.</summary>
        Material GrassMaterial()
        {
            if (_grassMat != null) return _grassMat;
            _grassMat = LoadMaterial(PalmGround);
            if (_grassMat == null)
            {
                _grassMat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.36f, 0.55f, 0.27f) };
                _grassMat.SetFloat("_Smoothness", 0.05f);
            }
            return _grassMat;
        }

        /// <summary>The same material with its sand face turned upward - the pack
        /// carries sand on the sides and bottom, so pointing those up is the beach.</summary>
        Material SandMaterial()
        {
            if (_sandMat != null) return _sandMat;
            _sandMat = LoadMaterial(PalmGround);
            if (_sandMat != null)
            {
                _sandMat.SetTexture("_Triplanar_Texture_Top", _sandMat.GetTexture("_Triplanar_Texture_Side"));
                _sandMat.SetTexture("_Triplanar_Normal_Texture_Top", _sandMat.GetTexture("_Triplanar_Normal_Texture_Side"));
            }
            else
            {
                _sandMat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.76f, 0.72f, 0.58f) };
                _sandMat.SetFloat("_Smoothness", 0.08f);
            }
            return _sandMat;
        }

        // ------------------------------------------------------------ wilderness

        // What grows and lies on the wild ground: woods in patches where the noise
        // says woods, single trees and dead ones between, rock toward the shore,
        // bushes and tall grass everywhere the woods are not. Nothing within a
        // stride of the last sidewalk, nothing on the beach, nothing in a river's
        // channel or under the highway's run out.
        void DressWilderness(float x0, float x1, float z0, float z1)
        {
            var rng = new System.Random(spacingSeed * 53 + 7);
            float Next(float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);
            GameObject PickOf(List<GameObject> l) => l.Count == 0 ? null : l[rng.Next(l.Count)];

            // one pass over the whole extent in cells, each cell rolling what it holds
            const float CellSize = 12f;
            int placed = 0;
            for (float z = z0; z < z1; z += CellSize)
                for (float x = x0; x < x1; x += CellSize)
                {
                    float px = x + Next(0f, CellSize), pz = z + Next(0f, CellSize);
                    float d = OutsideGrid(px, pz, out var dir, out var at);
                    if (d < 9f) continue;                              // the strip beside the sidewalk
                    float h = IslandHeight(px, pz);
                    if (h < 0.05f) continue;                             // beach and sea
                    float coast = CoastDistance(dir, at);
                    bool nearShore = d > coast - 55f;
                    if (UnderHighwayOrRiver(px, pz)) continue;
                    // a district's yard, apron and approach are its own ground
                    if (_reservations.InPaved(px, pz) || _reservations.InBare(px, pz)) continue;

                    // the ground's grade here: how steep, and which way is downhill
                    float gx = (IslandHeight(px + 4f, pz) - IslandHeight(px - 4f, pz)) / 8f;
                    float gz = (IslandHeight(px, pz + 4f) - IslandHeight(px, pz - 4f)) / 8f;
                    float grade = Mathf.Sqrt(gx * gx + gz * gz);

                    // woods where the slow noise is high; clearings where it is low
                    float woods = Mathf.PerlinNoise((px - 900f) * 0.0075f, (pz + 400f) * 0.0075f);
                    float roll = (float)rng.NextDouble();
                    // per-hectare density -> per 12 m cell (144 m^2 = 0.0144 ha)
                    float treeChance = treesPerHectare * 0.0144f * (woods > 0.55f ? 2.6f : woods > 0.4f ? 1f : 0.25f);
                    if (nearShore) treeChance *= 0.5f;
                    if (grade > 0.5f) treeChance *= 0.3f;   // little takes root on a rock face

                    GameObject prefab = null;
                    float yaw = Next(0f, 360f), scale = 1f, sink = 0.05f;
                    // a steep face wears the military demo's rock: a cliff piece turned
                    // to look downhill, sunk into the slope, with rubble round its foot
                    if (grade > 0.38f && h > 2f && roll < 0.55f && _wildCliffs.Count > 0)
                    {
                        prefab = PickOf(_wildCliffs);
                        yaw = Mathf.Atan2(-gx, -gz) * Mathf.Rad2Deg + Next(-18f, 18f);
                        scale = Next(0.9f, 1.7f);
                        sink = 0.6f * scale;
                    }
                    else if (roll < treeChance)
                    {
                        float kind = (float)rng.NextDouble();
                        // pine takes the high ground, the way it does on the warehouse hill
                        float pineAt = h > 9f ? 0.25f : 0.6f;
                        prefab = kind < pineAt ? PickOf(_wildTrees) : kind < 0.88f ? PickOf(_wildPines) : PickOf(_wildDead);
                        scale = Next(0.85f, 1.25f);
                    }
                    else if (roll < treeChance + (nearShore || grade > 0.35f ? 0.22f : 0.05f))
                    {
                        prefab = PickOf(_wildRocks);
                        scale = Next(0.7f, 1.6f);
                    }
                    else if (roll < treeChance + 0.30f)
                    {
                        prefab = (float)rng.NextDouble() < 0.7f ? PickOf(_wildBushes) : PickOf(_wildGrass);
                        scale = Next(0.8f, 1.3f);
                    }
                    else if (roll < treeChance + 0.34f && woods > 0.45f)
                    {
                        prefab = PickOf(_wildLogs);
                    }
                    if (prefab == null) continue;

                    var go = Instantiate(prefab, new Vector3(px, h - sink, pz), Quaternion.Euler(0f, yaw, 0f), IslandRoot);
                    go.transform.localScale = Vector3.one * scale;
                    go.name = "Wild " + prefab.name;
                    placed++;
                }
            Debug.Log($"[RoadDemo] island: {placed} wild things on the ground");
        }

        /// <summary>How much of the island's relief survives at (x, z) against the
        /// freeway: 0 in its corridor, easing to 1 over a broad shoulder either side,
        /// so the grade-level run lies in a natural-looking flat the hills stand back
        /// from rather than a trench cut through them.</summary>
        float HighwayFade(float x, float z)
        {
            if (seams == null) return 1f;
            float fade = 1f;
            foreach (var s in seams)
            {
                if (s == null || s.kind != SeamKind.Highway) continue;
                var span = SeamSpan(s);
                float across = s.vertical ? x : z;
                float outOf = Mathf.Max(0f, span.lo - across, across - span.hi);
                fade = Mathf.Min(fade, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(18f, 95f, outOf)));
            }
            return fade;
        }

        /// <summary>Whether the highway runs out over this spot or a river's channel
        /// takes it - nothing grows there.</summary>
        bool UnderHighwayOrRiver(float x, float z)
        {
            if (seams == null) return false;
            foreach (var s in seams)
            {
                if (s == null) continue;
                var span = SeamSpan(s);
                float across = s.vertical ? x : z;
                float margin = s.kind == SeamKind.River ? 16f : s.kind == SeamKind.Highway ? 12f : -1f;
                if (margin < 0f) continue;
                if (across > span.lo - margin && across < span.hi + margin) return true;
            }
            return false;
        }
    }
}
