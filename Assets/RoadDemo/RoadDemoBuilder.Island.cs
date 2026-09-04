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
        // Cut back to a bit over half (2026-08-21). Tripling them fixed the model-on-a-
        // table look and overshot: with coastWander and SeaMargin on top, each side ran
        // some eighteen hundred metres of ground, and the city's own density curve gives
        // up long before that - DressWilderness thins from NearWild to FarWild, 340 m to
        // 1250 m, and past FarWild the wood is already down to its floor of a fifth. So
        // everything beyond that was ground and trees laid at the thinnest the pass
        // knows how to lay them, out past the fog, for a camera that culls props at
        // 230 m. These widths put the water about twelve hundred metres out - still
        // three times the four hundred that read as a table, and no longer forty-two
        // square kilometres of island around a city that is under one.
        public float islandWest = 760f;
        public float islandEast = 630f;
        public float islandNorth = 600f;
        public float islandSouth = 700f;
        [Tooltip("How far the coastline strays in and out from those widths.")]
        public float coastWander = 240f;

        [Tooltip("Which shore is MAINLAND: the town is a peninsula joined to the country " +
                 "on that side, and the ground runs out that way instead of ending in a " +
                 "beach. None is the island the city was. Nothing else changes - the " +
                 "other three shores keep their coast, and the widths above still say how " +
                 "much wild ground each of them carries. A quarter that needs open water " +
                 "will not be laid on the mainland shore (CityLayout.Landlocked): a port " +
                 "there would be a quay onto a field.")]
        public CityEdge mainlandEdge = CityEdge.None;

        [Tooltip("Let the city's own seed decide whether the town is an island or a " +
                 "peninsula, and which way the country lies. Off leaves mainlandEdge " +
                 "exactly as it is set above.")]
        public bool rollShoreline = true;

        [Tooltip("How often the roll comes out a peninsula rather than an island.")]
        [Range(0f, 1f)] public float peninsulaChance = 0.5f;

        /// <summary>Island or peninsula, and which shore the country is on. Its own draw
        /// off the layout seed and taken BEFORE the quarters are rolled: the port asks
        /// whether a shore is landlocked before it picks one, and the heightfield asks
        /// how far the coast is on every side.</summary>
        void PlanShoreline()
        {
            if (!rollShoreline) return;
            var rng = new System.Random(cityLayoutSeed * 131 + 61);
            if (rng.NextDouble() >= peninsulaChance) { mainlandEdge = CityEdge.None; return; }
            mainlandEdge = (CityEdge)rng.Next(4);
            Debug.Log($"[RoadDemo] the town is a peninsula: the country lies to the " +
                      $"{mainlandEdge.ToString().ToLowerInvariant()}, and nothing that wants " +
                      "open water is laid on that shore");
        }

        /// <summary>Whether the town is joined to the country across this shore, so
        /// nothing that wants the sea may stand on it. Read by the district roll.</summary>
        public bool Landlocked(CityEdge edge) => mainlandEdge != CityEdge.None && edge == mainlandEdge;

        /// <summary>How far the mainland shore's waterline is pushed BEYOND the outer
        /// bound of the ground mesh. The mesh reaches width + coastWander + SeaMargin
        /// past the town, so the coast has to be at least that much further out again or
        /// the outer ground turns to beach and sea after all; and the margin on top of
        /// that has to clear the coast's own wander (240 m, which swings the waterline
        /// either way) and the hills' coast fade (they flatten under 150 m of dry land),
        /// or the country beyond the town comes out as a plain with a lagoon in it.
        /// Seven hundred clears both with room to spare and costs not one vertex - it
        /// moves the waterline, not the mesh.</summary>
        const float MainlandReach = 700f;

        /// <summary>The metres of dry land a direction is worth: the shore's own width,
        /// or that plus the whole mesh reach and more where the peninsula joins the
        /// country. Read by CoastDistance and by nothing else - deliberately NOT by the
        /// mesh's own extent, since widening the mesh pushes its bound out by exactly as
        /// much as it pushes the coast and gains nothing.</summary>
        float ShoreReach(CityEdge edge, float width)
            => Landlocked(edge) ? width + coastWander + SeaMargin + MainlandReach : width;
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
        /// <summary>A quiet, opaque bed below paved city ground. Authored basement
        /// entrances and subway stairs deliberately have no street-level tile over them,
        /// but the sea is one plane under the whole island; without this bed that plane
        /// shows through every opening deeper than <see cref="WaterY"/>. Ten centimetres
        /// above the water keeps those openings visibly sunken and dry.</summary>
        public const float DryUrbanBedY = WaterY + 0.10f;
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

        /// <summary>Below this the island's own ground wears sand and above it grass.
        /// The map paints its country by the same two lines, or the plan says beach
        /// where the city says field.</summary>
        public const float BeachLine = -0.35f;

        /// <summary>What the quarters asked of the ground. The MAP reads it: a port's
        /// basin is the one piece of the plan nobody else can report - it is not a seam,
        /// not a block and not a footprint, it is water the island was told to leave
        /// open - and the yards the quarters pave are the ground their roads run on.</summary>
        public DistrictReservations Reservations => _reservations;

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
            // A peninsula is this one substitution and nothing else: in the direction the
            // country lies, the coast is simply further away than any ground the island
            // builds - so toSea never falls to the beach band, the shore lerp never
            // fires, and what would have been sand and seabed comes out as more of the
            // same wild ground. The corner blend below carries it round the two corners
            // of the neck by itself.
            float wx = dir.x > 0f ? ShoreReach(CityEdge.East, islandEast)
                                  : ShoreReach(CityEdge.West, islandWest);
            float wz = dir.y > 0f ? ShoreReach(CityEdge.North, islandNorth)
                                  : ShoreReach(CityEdge.South, islandSouth);
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
            // A generated primary structure can have intentional holes inside its
            // rectangular bounds. Its exact paved mask removes covered ground later;
            // what remains sits just below the authored surfaces instead of exposing the
            // sea plane or fighting coplanar road tiles at the irregular city edge.
            if (d <= 0f) return HasPrimaryStructure ? RoadBed : 0f;
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
                    // and only where the river actually runs: a tributary stops at the
                    // river it joins, and carving its channel the whole length of the map
                    // would leave a dry canyon across the half of the town it never
                    // reaches - and take that shore away from the airport with it
                    var reach = SeamRun(s);
                    float along = s.vertical ? z : x;
                    if (along < reach.lo - RiverBank || along > reach.hi + RiverBank) continue;
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

            // The ground mesh reaches the same distance on a peninsula as on an island,
            // mainland shore included, and that is deliberate. The coast on the mainland
            // side is pushed MainlandReach beyond this bound (ShoreReach), so every
            // vertex out to the bound comes back dry - the beach and the seabed simply
            // never happen on that side, and the country ends where the world does
            // instead of in a strand. Widening the mesh there as well would only have
            // put the waterline back INSIDE it: the extra margin counts against toSea
            // exactly as the reach does, and the first version of this drowned the outer
            // five hundred metres of its own peninsula.
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

        /// <summary>One tile of the ground. False if it contains neither island terrain
        /// nor the dry bed kept below paved city ground.</summary>
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
            var dryVerts = new List<Vector3>();
            var dry = new List<int>();
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
                    // The ordinary grid fully tiles this rectangle. A primary structure
                    // such as Core does not: its own reservation publishes an exact paved
                    // mask so Outside cells keep real island ground.
                    bool ordinaryCity = !HasPrimaryStructure &&
                        cx > _gx0 && cx < _gx1 && cz > _gz0 && cz < _gz1;
                    bool paved = _reservations.InPaved(cx, cz, GroundStep * 0.5f);
                    if (ordinaryCity || paved)
                    {
                        // The sea spans the whole island. Keep an opaque bed below city
                        // surfaces so deliberately open basement/subway cells reveal dry
                        // ground rather than that sea plane. Real rivers and harbour basins
                        // remain open water.
                        if (!_reservations.InWater(cx, cz))
                        {
                            int dryBase = verts.Length + dryVerts.Count;
                            dryVerts.Add(new Vector3(i * GroundStep, DryUrbanBedY, j * GroundStep));
                            dryVerts.Add(new Vector3(i * GroundStep, DryUrbanBedY, (j + 1) * GroundStep));
                            dryVerts.Add(new Vector3((i + 1) * GroundStep, DryUrbanBedY, j * GroundStep));
                            dryVerts.Add(new Vector3((i + 1) * GroundStep, DryUrbanBedY, (j + 1) * GroundStep));
                            dry.Add(dryBase); dry.Add(dryBase + 1); dry.Add(dryBase + 2);
                            dry.Add(dryBase + 2); dry.Add(dryBase + 1); dry.Add(dryBase + 3);
                        }
                        continue;
                    }
                    int a = j * (nx + 1) + i, b = a + 1, c = a + nx + 1, d = c + 1;
                    float low = Mathf.Min(verts[a].y, verts[b].y, verts[c].y, verts[d].y);
                    var into = low < BeachLine ? sand : grass;
                    into.Add(a); into.Add(c); into.Add(b);
                    into.Add(b); into.Add(c); into.Add(d);
                }

            if (grass.Count == 0 && sand.Count == 0 && dry.Count == 0) return false;

            var allVerts = new Vector3[verts.Length + dryVerts.Count];
            System.Array.Copy(verts, allVerts, verts.Length);
            dryVerts.CopyTo(allVerts, verts.Length);

            var mesh = new Mesh { name = "Island Ground " + index, indexFormat = IndexFormat.UInt32 };
            mesh.vertices = allVerts;
            mesh.subMeshCount = 3;
            mesh.SetTriangles(grass, 0);
            mesh.SetTriangles(sand, 1);
            mesh.SetTriangles(dry, 2);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject("Ground " + index);
            go.transform.SetParent(IslandRoot, false);
            go.transform.position = new Vector3(x0, 0f, z0);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = new[] { GrassMaterial(), SandMaterial(), DryUrbanBedMaterial() };
            mr.shadowCastingMode = ShadowCastingMode.On;
            go.isStatic = true;
            return true;
        }

        Material _grassMat, _sandMat, _dryUrbanBedMat;

        Material DryUrbanBedMaterial()
        {
            if (_dryUrbanBedMat != null) return _dryUrbanBedMat;
            _dryUrbanBedMat = ForecourtSet.Asphalt();
            if (_dryUrbanBedMat != null) _dryUrbanBedMat.name = "Dry urban subgrade";
            return _dryUrbanBedMat;
        }

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
            int placed = 0, dropped = 0;
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
                    // and the pack's collider goes with it. Nothing in the wilderness is
                    // physical: the cars ride LaneNet, the walkers read WalkObstacles (a
                    // plan of boxes, not a physics scene), and a click is looking for a
                    // building. All a tree's collider ever does is turn up in the
                    // RaycastAll that BuildingCardPicker fires three kilometres down the
                    // ray, to be sorted and thrown away. Twenty-odd thousand of them.
                    foreach (var col in go.GetComponentsInChildren<Collider>()) { Destroy(col); dropped++; }
                    placed++;
                }
            Debug.Log($"[RoadDemo] island: {placed} wild things on the ground, {dropped} colliders dropped with them");
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
                if (across <= span.lo - 16f || across >= span.hi + 16f) continue;
                var reach = SeamRun(s);
                float along = s.vertical ? z : x;
                if (along > reach.lo - 16f && along < reach.hi + 16f) return true;
            }
            return false;
        }
    }
}
