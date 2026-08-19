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
        [Tooltip("Metres of wild ground from the last road to the water on each side, before the coast wanders. " +
                 "Three times what it was: at four hundred metres the far shore stood in every shot down every " +
                 "street and the town read as a model on a table, and the port's basin - which reaches nine " +
                 "hundred metres out for its ships - ate most of the width it had.")]
        public float islandWest = 1380f;
        public float islandEast = 1140f;
        public float islandNorth = 1080f;
        public float islandSouth = 1260f;
        [Tooltip("How far the coastline strays in and out from those widths.")]
        public float coastWander = 240f;
        [Tooltip("Woods per hectare, roughly - the wilderness's tree density.")]
        public float treesPerHectare = 30f;
        [Tooltip("How high the island's proper hills stand over the plain, out where the " +
                 "wild ground is wide enough to carry them (the knolls near the roads stay low).")]
        public float hillHeight = 26f;

        /// <summary>Metres between heightfield vertices; the shore's slope is a few of
        /// these. Ten rather than eight since the island tripled: the beach still gets
        /// five rows of quads down it, and the heightfield is half a million vertices
        /// instead of eight hundred thousand - which is half the build and half the
        /// memory for ground nobody can see through the fog anyway.</summary>
        const float GroundStep = 10f;
        /// <summary>The level the island holds its ground at under a carriageway that is
        /// laid on top of it - the roads out to the quarters, the freeway's link roads,
        /// the belt's own shoulder, a filling station's apron. Six centimetres under the
        /// asphalt rather than dead level with it: held at exactly zero, the ground and
        /// the road tiles are the same plane, and two surfaces in one plane tear at each
        /// other across the whole road as the camera moves - the road showing through the
        /// grass in bands, which is what "the road is under the floor" looks like. Below
        /// the tile it simply never shows; the sidewalk's own 10 cm kerb hides the
        /// step at the shoulder.</summary>
        public const float RoadBed = -0.06f;
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
        /// wilderness, so the wild never grows over a quarter and the coast never cuts
        /// into one - it keeps a beach past its fence instead (CoastFrom). Filled by
        /// RoadDemoBuilder.Districts.cs before the island is built; empty in a city with
        /// no districts.</summary>
        readonly List<Rect> _landRects = new List<Rect>();

        /// <summary>What the districts asked of the ground: what they pave themselves,
        /// what must lie flat, what must be open water, where nothing grows.</summary>
        readonly DistrictReservations _reservations = new DistrictReservations();

        /// <summary>How far the point lies outside the city - the grid rectangle or any
        /// district's own ground, whichever it is nearest: what the hills are kept off
        /// and the wild is grown past. <paramref name="toSea"/> comes back with the
        /// metres of dry land left between the point and the water.</summary>
        float OutsideGrid(float x, float z, out float toSea)
        {
            // The waterline is a width of shore measured from the TOWN, and from nothing
            // else. It used to be measured from whatever ground was nearest - the grid or
            // a quarter - which meant a port pushed out to the end of the island took the
            // whole shore with it: another kilometre of wilderness appeared beyond the
            // quay, the port stood inland, and its basin had to be cut out to the sea as a
            // canal - which is the rectangular notch of water the port sat in. Measured
            // from the grid, a quarter simply SPENDS the shore it stands on.
            float best = OutsideRect(x, z, _gx0, _gx1, _gz0, _gz1, out var dir, out var at);
            toSea = CoastDistance(dir, at) - best;
            for (int i = 0; i < _landRects.Count; i++)
            {
                var r = _landRects[i];
                float d = OutsideRect(x, z, r.xMin, r.xMax, r.yMin, r.yMax, out _, out _);
                if (d < best) best = d;
                // and a quarter is never drowned by a coast that wandered inward: it keeps
                // its own ground and a strand round it, whatever the width says
                if (d < Beach) toSea = Mathf.Max(toSea, Beach - d + 6f);
            }
            return best;
        }

        /// <summary>The least shore left round a quarter that stands at the island's end.</summary>
        const float Beach = 70f;

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

        /// <summary>Over how many metres a district's basin eases out into the ground
        /// round it. A basin is a plain rectangle - the water a ship needs, and no
        /// shape at all - and taken as one it cut the sea out of the island as a pit:
        /// grass at head height on one side of the line and seabed on the other, a
        /// wall going down into the water the whole way round the port and out to the
        /// horizon on both flanks. Eased over a beach's width the same rectangle reads
        /// as a bay.</summary>
        const float ShoreBlend = 80f;

        /// <summary>Over how many metres the island's proper hills come back up after a
        /// basin. Wider than the shore itself: the beach is where the ground meets the
        /// water, this is how far inland the country stays low.</summary>
        const float BasinFade = 220f;

        /// <summary>The island's height at (x, z): the ground as it lies, and then the
        /// basins a district asked for cut down into it over a beach.</summary>
        float IslandHeight(float x, float z)
        {
            // a district's basin is water wherever it says so, whatever the coast does:
            // a ship has to be able to sail in to the quay
            _reservations.WaterAt(x, z, ShoreBlend, out float basin);
            if (basin >= 0.999f) return SeabedY;
            float ground = DryHeight(x, z, out float held);
            // a district's OWN ground is not dragged into its basin: the yard beside
            // the quay is concrete at the quay's level and stays there, and the beach
            // begins where the yard ends. Without this the blend that turned the pit
            // into a bay pulled the port's own apron under water with it.
            basin = Mathf.Min(basin, 1f - held);
            // and the basin never RAISES the ground, only cuts it
            return basin > 0f ? Mathf.Min(ground, Mathf.Lerp(ground, SeabedY, basin)) : ground;
        }

        /// <summary>The ground as it lies, with no basin cut into it: flush with the
        /// pavement beside the grid, then rolling wild ground, then the beach's slope
        /// into the sea; carved down where a river runs out to the water.</summary>
        /// <param name="held">How firmly some district or road is holding this ground
        /// at a level of its own: 1 on its own paving, easing to 0 at the edge of its
        /// apron. The basin's blend is damped by it.</param>
        float DryHeight(float x, float z, out float held)
        {
            held = 1f;
            float d = OutsideGrid(x, z, out float toSea);
            if (d <= 0f) return 0f;
            held = 0f;

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
            float coastFade = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(150f, 60f, toSea));
            hills += far * coastFade * hillHeight * lobe * lobe;

            // the freeway's corridor: the relief dies away across a broad shoulder on
            // both sides of its grade-level run, or a knoll would bury the deck
            hills *= HighwayFade(x, z);
            // and round a basin the ground lies low: a port stands at the foot of the
            // land, not on top of a cliff, and a hill on the lip of the basin drops its
            // whole twenty-five metres into the water over one shoulder however wide
            // the blend under it is
            if (_reservations.WaterAt(x, z, BasinFade, out float nearBasin)) hills *= 1f - nearBasin;

            float rise = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(14f, 70f, d));
            float land = 0.03f + rise * Mathf.Max(0.05f, hills);

            // the beach: the ground slides from land level down under the water over
            // the last stretch before the coast, and on down to the seabed past it
            float shore = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(42f, -6f, toSea));
            float h = Mathf.Lerp(land, SeabedY - Mathf.Min(6f, -toSea * 0.03f), shore);

            // the rivers' channels out to the sea
            if (seams != null)
                foreach (var s in seams)
                {
                    if (s == null || s.kind != SeamKind.River) continue;
                    var span = SeamSpan(s);
                    float across = s.vertical ? x : z;
                    float outOf = Mathf.Max(0f, span.lo - across, across - span.hi);
                    if (outOf > RiverBank) continue;
                    // in the channel: bed; on its lip: the bank sloping down to it
                    float bank = 1f - Mathf.SmoothStep(0f, 1f, outOf / RiverBank);
                    h = Mathf.Lerp(h, Mathf.Min(h, SeabedY + 1f), bank);
                }

            // a district's apron: held flat at its own level, easing back into the wild
            // ground over the last few metres so nothing steps
            if (_reservations.FlatAt(x, z, FlatBlend, out float level, out float w))
            {
                h = Mathf.Lerp(h, level, w);
                held = w;
            }
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
            OpenBasinsToSea();

            float reachW = islandWest + coastWander + SeaMargin, reachE = islandEast + coastWander + SeaMargin;
            float reachS = islandSouth + coastWander + SeaMargin, reachN = islandNorth + coastWander + SeaMargin;
            // the island rings the CITY: the grid and every district hanging off it
            float lx0 = gx0, lx1 = gx1, lz0 = gz0, lz1 = gz1;
            foreach (var r in _landRects)
            {
                lx0 = Mathf.Min(lx0, r.xMin); lx1 = Mathf.Max(lx1, r.xMax);
                lz0 = Mathf.Min(lz0, r.yMin); lz1 = Mathf.Max(lz1, r.yMax);
            }
            // a basin is NOT taken into the island's own extent. It used to be, because a
            // port sitting inland had its basin pushed out as a canal through the whole
            // shore and the ground had to follow it; now a port stands at the coast and
            // its water is the sea off it (CoastFrom). The port's shipping lane, which
            // runs the length of the island so the freighters come in from the end of the
            // map, would otherwise have dragged the heightfield out a mile each way -
            // four hundred thousand vertices of seabed nobody can see, past the fog, out
            // where the water plane alone is all there is to draw.
            float x0 = lx0 - reachW, x1 = lx1 + reachE, z0 = lz0 - reachS, z1 = lz1 + reachN;
            _islandArea = Rect.MinMaxRect(x0, z0, x1, z1);

            BuildGround(x0, x1, z0, z1);

            // the sea: one plane under everything, out past the ground's edge, at the
            // level the quays and boats already stand at
            WaterTiles(x0 - 400f, x1 + 400f, z0 - 400f, z1 + 400f);
            foreach (Transform t in SeamsRoot)
                if (t.name == "Water") t.gameObject.name = "Sea";

            DressWilderness(x0, x1, z0, z1);
        }

        /// <summary>A port's basin has to reach OPEN water, and the port cannot know
        /// how far that is: it reserves a rectangle as long as it thinks it needs -
        /// nine hundred metres, which cleared the old island's four-hundred-metre wild
        /// band with room to spare - and on an island three times that it reserved a
        /// pond in the middle of a field with the sea a kilometre further on. So the
        /// island, which is the one thing that knows where its own coast runs, pushes
        /// the seaward end of every basin out until it is past it.</summary>
        void OpenBasinsToSea()
        {
            float cx = (_gx0 + _gx1) * 0.5f, cz = (_gz0 + _gz1) * 0.5f;
            for (int i = 0; i < _reservations.Water.Count; i++)
            {
                var r = _reservations.Water[i];
                if (i < _reservations.WaterOpens.Count && !_reservations.WaterOpens[i]) continue;
                var away = new Vector2(r.center.x - cx, r.center.y - cz);
                if (away.sqrMagnitude < 1f) continue;
                bool alongX = Mathf.Abs(away.x) > Mathf.Abs(away.y);
                float sign = alongX ? Mathf.Sign(away.x) : Mathf.Sign(away.y);

                // the widest the coast runs anywhere across the basin's seaward end
                float need = 0f;
                for (int k = 0; k <= 4; k++)
                {
                    float t = k / 4f;
                    float px = alongX ? (sign > 0f ? r.xMax : r.xMin) : Mathf.Lerp(r.xMin, r.xMax, t);
                    float pz = alongX ? Mathf.Lerp(r.yMin, r.yMax, t) : (sign > 0f ? r.yMax : r.yMin);
                    OutsideGrid(px, pz, out float toSea);
                    need = Mathf.Max(need, toSea + SeaMargin * 0.5f);
                }
                if (need <= 1f) continue;

                _reservations.Water[i] = alongX
                    ? (sign > 0f ? Rect.MinMaxRect(r.xMin, r.yMin, r.xMax + need, r.yMax)
                                 : Rect.MinMaxRect(r.xMin - need, r.yMin, r.xMax, r.yMax))
                    : (sign > 0f ? Rect.MinMaxRect(r.xMin, r.yMin, r.xMax, r.yMax + need)
                                 : Rect.MinMaxRect(r.xMin, r.yMin - need, r.xMax, r.yMax));
                Debug.Log($"[RoadDemo] island: a basin pushed {need:F0} m further out, so the port " +
                          "opens onto the sea rather than onto a pond");
            }
        }

        /// <summary>The whole ground the island was laid over, in world XZ - the city,
        /// its quarters, the wilderness round them and the beach. Zero-sized until
        /// BuildIsland has run. The map draws its countryside off this.</summary>
        public Rect IslandArea => _islandArea;
        Rect _islandArea;

        /// <summary>The ground's height at a point, the way the island itself was
        /// built: above zero is land, well below it is seabed. Public for the map,
        /// which samples it coarsely to draw a coastline it cannot read off the
        /// merged mesh.</summary>
        public float LandHeight(float x, float z) => IslandHeight(x, z);

        /// <summary>How wide a tile of the ground mesh is - a whole number of ground
        /// steps. The island used to be ONE mesh, which was fine while it was a few
        /// hundred metres of grass round the grid: at three times that it is six
        /// kilometres of it, and one mesh is one renderer that is always drawn, always
        /// in the shadow pass, and cannot be culled a corner at a time. Tiled, the far
        /// shore behind the camera costs nothing.</summary>
        const float GroundTileSpan = 480f;

        // The ground as a heightfield over the island's whole extent, cells inside
        // the grid rectangle left out; grass on the land triangles, sand where they
        // dip toward the water. World-space triplanar materials, so no UVs. Laid out
        // in tiles, whose vertices agree along their seams because the height is a
        // function of the point and of nothing else.
        void BuildGround(float x0, float x1, float z0, float z1)
        {
            int tx = Mathf.CeilToInt((x1 - x0) / GroundTileSpan), tz = Mathf.CeilToInt((z1 - z0) / GroundTileSpan);
            int tiles = 0;
            for (int b = 0; b < tz; b++)
                for (int a = 0; a < tx; a++)
                {
                    float ax0 = x0 + a * GroundTileSpan, az0 = z0 + b * GroundTileSpan;
                    float ax1 = Mathf.Min(x1, ax0 + GroundTileSpan), az1 = Mathf.Min(z1, az0 + GroundTileSpan);
                    if (ax1 - ax0 < 1f || az1 - az0 < 1f) continue;
                    if (GroundTile(ax0, ax1, az0, az1, tiles)) tiles++;
                }
            Debug.Log($"[RoadDemo] island: ground in {tiles} tiles of {GroundTileSpan:F0} m " +
                      $"over {(x1 - x0):F0} x {(z1 - z0):F0} m");
        }

        /// <summary>One tile of the ground. False if it was all city and all paving and
        /// there was nothing to draw.</summary>
        bool GroundTile(float x0, float x1, float z0, float z1, int index)
        {
            int nx = Mathf.CeilToInt((x1 - x0) / GroundStep), nz = Mathf.CeilToInt((z1 - z0) / GroundStep);
            var verts = new Vector3[(nx + 1) * (nz + 1)];
            for (int j = 0; j <= nz; j++)
                for (int i = 0; i <= nx; i++)
                {
                    float x = x0 + i * GroundStep, z = z0 + j * GroundStep;
                    // local to the tile's own corner, so the tile has a position in the
                    // world and the merge and the culling can tell one from the next
                    verts[j * (nx + 1) + i] = new Vector3(i * GroundStep, IslandHeight(x, z), j * GroundStep);
                }
            var grass = new List<int>();
            var sand = new List<int>();
            const float BeachLine = -0.35f; // below this a triangle wears sand
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    float cx = x0 + (i + 0.5f) * GroundStep, cz = z0 + (j + 0.5f) * GroundStep;
                    // no ground under the grid: it tiles its own floor - nor under a
                    // district, which tiles its own. A cell is dropped only where the
                    // WHOLE of it is paved (the rectangle pulled in by half a cell):
                    // dropped on its centre alone, a cell hanging half out of the port's
                    // yard took a four-metre bite of ground with it, and the ring of
                    // those bites round the quarter is the open air you could see the
                    // sea through.
                    if (cx > _gx0 && cx < _gx1 && cz > _gz0 && cz < _gz1) continue;
                    if (_reservations.InPaved(cx, cz, GroundStep * 0.5f)) continue;
                    int a = j * (nx + 1) + i, b = a + 1, c = a + nx + 1, d = c + 1;
                    float low = Mathf.Min(verts[a].y, verts[b].y, verts[c].y, verts[d].y);
                    var into = low < BeachLine ? sand : grass;
                    into.Add(a); into.Add(c); into.Add(b);
                    into.Add(b); into.Add(c); into.Add(d);
                }

            if (grass.Count == 0 && sand.Count == 0) return false;

            var mesh = new Mesh { name = "Island Ground " + index, indexFormat = IndexFormat.UInt32 };
            mesh.vertices = verts;
            mesh.subMeshCount = 2;
            mesh.SetTriangles(grass, 0);
            mesh.SetTriangles(sand, 1);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject("Ground " + index);
            go.transform.SetParent(IslandRoot, false);
            go.transform.position = new Vector3(x0, 0f, z0);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = new[] { GrassMaterial(), SandMaterial() };
            mr.shadowCastingMode = ShadowCastingMode.On;
            go.isStatic = true;
            return true;
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
            // where the thinning starts and where it bottoms out, metres from the city
            const float NearWild = 340f, FarWild = 1250f, FarWildDensity = 0.22f;
            int placed = 0;
            for (float z = z0; z < z1; z += CellSize)
                for (float x = x0; x < x1; x += CellSize)
                {
                    float px = x + Next(0f, CellSize), pz = z + Next(0f, CellSize);
                    float d = OutsideGrid(px, pz, out float toSea);
                    if (d < 9f) continue;                              // the strip beside the sidewalk
                    if (toSea < -12f) continue;                        // out at sea: nothing to dress
                    // the wood thins with the drive out. The island is kilometres across
                    // now; laid at the same density all the way to the coast it is eighty
                    // thousand trees, and seventy of those thousand stand where nobody
                    // ever goes, in fog, past the cull distance - the frame pays for them
                    // in the merge and the memory and gives nothing back.
                    if (rng.NextDouble() > Mathf.Lerp(1f, FarWildDensity,
                            Mathf.InverseLerp(NearWild, FarWild, d))) continue;
                    float h = IslandHeight(px, pz);
                    if (h < 0.05f) continue;                             // beach and sea
                    bool nearShore = toSea < 55f;
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
        /// from rather than a trench cut through them. Only along the freeway's ACTUAL
        /// run (_highwayRuns) - past its termini the hills close back in.</summary>
        float HighwayFade(float x, float z)
        {
            float fade = 1f;
            foreach (var run in _highwayRuns)
            {
                float along = run.vertical ? z : x;
                if (along < run.uLo - 40f || along > run.uHi + 40f) continue;
                float across = run.vertical ? x : z;
                float outOf = Mathf.Max(0f, run.lo - across, across - run.hi);
                fade = Mathf.Min(fade, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(18f, 95f, outOf)));
            }
            return fade;
        }

        /// <summary>Whether the highway runs over this spot or a river's channel
        /// takes it - nothing grows there.</summary>
        bool UnderHighwayOrRiver(float x, float z)
        {
            foreach (var run in _highwayRuns)
            {
                float along = run.vertical ? z : x;
                if (along < run.uLo || along > run.uHi) continue;
                float across = run.vertical ? x : z;
                if (across > run.lo - 12f && across < run.hi + 12f) return true;
            }
            if (seams == null) return false;
            foreach (var s in seams)
            {
                if (s == null || s.kind != SeamKind.River) continue;
                var span = SeamSpan(s);
                float across = s.vertical ? x : z;
                if (across > span.lo - 16f && across < span.hi + 16f) return true;
            }
            return false;
        }
    }
}
