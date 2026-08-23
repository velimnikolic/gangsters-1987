using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using RoadDemo;

namespace SyntyCity
{
    // A city built out of the POLYGON City pack and nothing else.
    //
    // This is not RoadDemoBuilder with different settings. It shares no layout code
    // with it, reads none of its bakes, and lays no PalmCity tile: the street comes
    // from the pack's own kit, the blocks are the ones cut out of the pack's own demo
    // scene (Assets/CityKit/Blocks/synty_*, written by SyntyDemoBlockRip), and the
    // strict centre of town is that demo scene entire.
    //
    // The measurements below are read off Demo.unity rather than chosen. Everything
    // in the kit is a 5 m square and every rotation in that scene is a quarter turn,
    // so the whole city is a grid of cells with a tile and a quarter turn in each.
    //
    //   THE PIVOT. A tile's pivot is a CORNER, not its middle: unturned, the tile at
    //   position p covers x [p.x-5, p.x] and z [p.z-5, p.z]. Turning it moves which
    //   corner the pivot is, so the position for a given cell depends on the yaw -
    //   that is what Stand() is for, and placing a tile any other way puts it one
    //   cell out in one or both axes.
    //
    //   THE STREET, counted across: kerb, carriageway, carriageway, kerb. Four cells,
    //   20 m building line to building line, 10 m of it road. Measured off their
    //   junction at the origin of the demo scene.
    //
    //   THE KERB faces the road: +X is a quarter turn, -X three, -Z a half, +Z none.
    //
    //   THE JUNCTION is those four cells by four: plain asphalt where the two
    //   carriageways cross, a zebra where a carriageway crosses the other street's
    //   kerb line, and a corner piece in each of the four corners.
    //
    // What a block carries and what the street carries is decided at the KERB: the
    // ripped blocks bring everything from the kerb inward, their own pavement fill
    // included, so this builder lays the kerb and the road and nothing else.
    public class SyntyCityBuilder : MonoBehaviour
    {
        public const float Cell = 5f;

        /// <summary>Kerb, carriageway, carriageway, kerb.</summary>
        public const int StreetCells = 4;

        [Header("The town")]
        [Tooltip("Lot columns each side of downtown, and lot rows above and below. " +
                 "2 gives a five-by-five town with the demo scene in the middle of it.")]
        [Min(0)] public int ringsAcross = 2;
        [Min(0)] public int ringsDeep = 2;
        [Tooltip("Same seed, same town.")]
        public int seed = 7;
        [Tooltip("Stand the demo scene itself in the middle. Off leaves a lot there " +
                 "like any other, which is the way to look at the ripped blocks alone.")]
        public bool downtown = true;

        [Header("The look")]
        public Vector3 sunAngles = new Vector3(50f, 212f, 0f);
        public float sunIntensity = 1.5f;
        [Range(0f, 1f)] public float sunShadowStrength = 0.8f;
        public DemoGrade.Look look = DemoGrade.Look.PolygonCity;

        [Header("Day")]
        [Range(0f, 24f)] public float startHour = 11f;
        public float realSecondsPerGameHour = 15f;

        const string Env = "Assets/Synty/PolygonCity/Prefabs/Environments/";
        const string BlocksDir = "Assets/CityKit/Blocks";
        const string TownPath = "Assets/CityKit/Downtown/synty-downtown.prefab";

        GameObject _road, _bare, _kerb, _corner, _zebra, _fill;
        Transform _streets, _blocks;
        System.Random _rng;

        void Awake()
        {
#if UNITY_EDITOR
            _rng = new System.Random(seed);
            if (!LoadKit()) return;

            _streets = new GameObject("Streets").transform;
            _blocks = new GameObject("Blocks").transform;

            var stock = LoadBlocks();
            if (stock.Count == 0) { Debug.LogError("[SyntyCity] no ripped blocks in " + BlocksDir); return; }

            var town = downtown ? UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(TownPath) : null;
            Vector2Int middle = town != null ? CellsOf(Span(town)) : Pick(stock).Cells;

            // the blocks are chosen FIRST and the lots are cut to them, not the other
            // way round: a lot dealt at random and then filled with whatever fits
            // leaves most of itself as empty pavement, which is what the first town
            // came out as - a plain with a few buildings on it
            int across = ringsAcross * 2 + 1, deep = ringsDeep * 2 + 1;
            var chosen = new Bake[across, deep];
            for (int c = 0; c < across; c++)
                for (int r = 0; r < deep; r++)
                    chosen[c, r] = Pick(stock);

            var cols = new int[across];
            var rows = new int[deep];
            for (int c = 0; c < across; c++)
                for (int r = 0; r < deep; r++)
                {
                    cols[c] = Mathf.Max(cols[c], chosen[c, r].Cells.x);
                    rows[r] = Mathf.Max(rows[r], chosen[c, r].Cells.y);
                }
            if (town != null) { cols[across / 2] = middle.x; rows[deep / 2] = middle.y; }

            var xs = Starts(cols);
            var zs = Starts(rows);

            LayStreets(cols, rows, xs, zs);
            LayLots(chosen, cols, rows, xs, zs, town);

            int wide = xs[xs.Length - 1] + cols[cols.Length - 1] + StreetCells;
            int deep = zs[zs.Length - 1] + rows[rows.Length - 1] + StreetCells;
            Scene(new Vector3(wide * Cell * 0.5f, 0f, deep * Cell * 0.5f),
                  Mathf.Max(wide, deep) * Cell);

            Debug.Log($"[SyntyCity] {cols.Length} x {rows.Length} lots, " +
                      $"{wide * Cell:F0} x {deep * Cell:F0} m, {stock.Count} ripped blocks in stock" +
                      (town != null ? $", downtown {middle.x * Cell:F0} x {middle.y * Cell:F0} m" : ""));
#else
            Debug.LogError("[SyntyCity] loads through the AssetDatabase and only runs in the editor.");
#endif
        }

#if UNITY_EDITOR
        // ------------------------------------------------------------------- the kit

        bool LoadKit()
        {
            _road = Load("SM_Env_Road_Lines_01");
            _bare = Load("SM_Env_Road_Bare_01");
            _kerb = Load("SM_Env_Sidewalk_Straight_01");
            _corner = Load("SM_Env_Sidewalk_Corner_01");
            _zebra = Load("SM_Env_Road_Crossing_01");
            _fill = Load("SM_Env_Sidewalk_01");
            return _road && _bare && _kerb && _corner && _zebra && _fill;
        }

        static GameObject Load(string name)
        {
            var go = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(Env + name + ".prefab");
            if (go == null) Debug.LogError("[SyntyCity] missing " + Env + name + ".prefab");
            return go;
        }

        struct Bake
        {
            public GameObject Prefab;
            public Vector2Int Cells;
        }

        static List<Bake> LoadBlocks()
        {
            var stock = new List<Bake>();
            foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:Prefab", new[] { BlocksDir }))
            {
                var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                if (!System.IO.Path.GetFileName(path).StartsWith("synty_")) continue;
                var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;
                var tag = prefab.GetComponent<LivingCity.Generation.BlockLotTag>();
                var size = tag != null && tag.lotWidth > 0f
                    ? new Vector2(tag.lotWidth, tag.lotDepth)
                    : new Vector2(Span(prefab).x, Span(prefab).z);
                stock.Add(new Bake
                {
                    Prefab = prefab,
                    Cells = new Vector2Int(Mathf.RoundToInt(size.x / Cell),
                                           Mathf.RoundToInt(size.y / Cell)),
                });
            }
            stock.Sort((a, b) => string.CompareOrdinal(a.Prefab.name, b.Prefab.name));  // same town every run
            return stock;
        }

        /// <summary>How much ground a prefab covers, horizon scenery ignored.</summary>
        static Vector3 Span(GameObject prefab)
        {
            var rs = prefab.GetComponentsInChildren<Renderer>(true);
            bool any = false;
            var box = new Bounds();
            foreach (var r in rs)
            {
                var b = r.bounds;
                if (b.size.x > 400f || b.size.z > 400f) continue;
                if (!any) { box = b; any = true; } else box.Encapsulate(b);
            }
            return any ? box.size : Vector3.one * 50f;
        }

        static Vector2Int CellsOf(Vector3 span) =>
            new Vector2Int(Mathf.CeilToInt(span.x / Cell), Mathf.CeilToInt(span.z / Cell));

        Bake Pick(List<Bake> stock) => stock[_rng.Next(stock.Count)];

        // ------------------------------------------------------------------ the plan

        /// <summary>Where each lot starts, with a street before the first and between
        /// every pair.</summary>
        static int[] Starts(int[] sizes)
        {
            var at = new int[sizes.Length];
            int cursor = StreetCells;
            for (int k = 0; k < sizes.Length; k++) { at[k] = cursor; cursor += sizes[k] + StreetCells; }
            return at;
        }

        // ---------------------------------------------------------------- the street

        void LayStreets(int[] cols, int[] rows, int[] xs, int[] zs)
        {
            int last = zs[zs.Length - 1] + rows[rows.Length - 1] + StreetCells;
            int end = xs[xs.Length - 1] + cols[cols.Length - 1] + StreetCells;

            // the bands: a street runs before every lot and after the last
            var vx = new List<int>();
            foreach (int x in xs) vx.Add(x - StreetCells);
            vx.Add(end - StreetCells);
            var hz = new List<int>();
            foreach (int z in zs) hz.Add(z - StreetCells);
            hz.Add(last - StreetCells);

            foreach (int sx in vx)
                for (int j = 0; j < last; j++)
                {
                    if (InBand(hz, j)) continue;                    // the junction lays itself
                    Stand(_kerb, sx, j, 90f);                       // west kerb, road to the east
                    Stand(_road, sx + 1, j, 180f);
                    Stand(_road, sx + 2, j, 0f);
                    Stand(_kerb, sx + 3, j, 270f);                  // east kerb
                }

            foreach (int sz in hz)
                for (int i = 0; i < end; i++)
                {
                    if (InBand(vx, i)) continue;
                    Stand(_kerb, i, sz, 0f);                        // south kerb, road to the north
                    Stand(_road, i, sz + 1, 90f);
                    Stand(_road, i, sz + 2, 270f);
                    Stand(_kerb, i, sz + 3, 180f);                  // north kerb
                }

            foreach (int sx in vx)
                foreach (int sz in hz)
                    Junction(sx, sz);
        }

        static bool InBand(List<int> bands, int k)
        {
            foreach (int b in bands) if (k >= b && k < b + StreetCells) return true;
            return false;
        }

        /// <summary>Four cells by four where two streets cross: asphalt where the
        /// carriageways meet, a zebra where a carriageway crosses the other street's
        /// kerb line, a corner in each corner.</summary>
        void Junction(int sx, int sz)
        {
            for (int a = 0; a < StreetCells; a++)
                for (int b = 0; b < StreetCells; b++)
                {
                    bool roadX = a == 1 || a == 2, roadZ = b == 1 || b == 2;
                    int i = sx + a, j = sz + b;
                    if (roadX && roadZ) Stand(_bare, i, j, 0f);
                    else if (roadX) Stand(_zebra, i, j, 90f);       // across the north-south road
                    else if (roadZ) Stand(_zebra, i, j, 0f);        // across the east-west road
                    else Stand(_corner, i, j, Corner(a, b));
                }
        }

        static float Corner(int a, int b) =>
            a == 0 ? (b == 0 ? 0f : 90f) : (b == 0 ? 270f : 180f);

        // ----------------------------------------------------------------- the lots

        void LayLots(Bake[,] chosen, int[] cols, int[] rows, int[] xs, int[] zs, GameObject town)
        {
            int midC = cols.Length / 2, midR = rows.Length / 2;
            for (int c = 0; c < cols.Length; c++)
                for (int r = 0; r < rows.Length; r++)
                {
                    if (town != null && c == midC && r == midR)
                    {
                        Stand(town, xs[c], zs[r], cols[c], rows[r], "Downtown");
                        continue;
                    }
                    var bake = chosen[c, r];
                    Stand(bake.Prefab, xs[c], zs[r], cols[c], rows[r], bake.Prefab.name);
                    Pave(xs[c], zs[r], cols[c], rows[r], bake.Cells);
                }
        }

        void Stand(GameObject prefab, int i, int j, int w, int d, string name)
        {
            var centre = new Vector3((i + w * 0.5f) * Cell, 0f, (j + d * 0.5f) * Cell);
            var go = Instantiate(prefab, centre, Quaternion.identity, _blocks);
            go.name = name;
        }

        /// <summary>The ground a block does not cover, in the pack's own pavement fill,
        /// so a lot bigger than the block standing on it is not a hole.</summary>
        void Pave(int i, int j, int w, int d, Vector2Int block)
        {
            int i0 = i + (w - block.x) / 2, j0 = j + (d - block.y) / 2;
            for (int a = 0; a < w; a++)
                for (int b = 0; b < d; b++)
                {
                    int x = i + a, z = j + b;
                    if (x >= i0 && x < i0 + block.x && z >= j0 && z < j0 + block.y) continue;
                    Stand(_fill, x, z, 0f);
                }
        }

        // ------------------------------------------------------------------- placing

        /// <summary>One kit tile in one cell, turned. The pivot is a corner and turning
        /// moves which corner it is, so the position is worked out per yaw: unturned it
        /// is the cell's far corner, a quarter turn puts it on the near-Z far-X one,
        /// a half turn on the near corner, three quarters on the far-Z near-X one.</summary>
        void Stand(GameObject prefab, int i, int j, float yaw)
        {
            int q = Mathf.RoundToInt(yaw / 90f) & 3;
            float x = (q == 0 || q == 1) ? (i + 1) * Cell : i * Cell;
            float z = (q == 0 || q == 3) ? (j + 1) * Cell : j * Cell;
            var go = Instantiate(prefab, new Vector3(x, 0f, z), Quaternion.Euler(0f, q * 90f, 0f), _streets);
            go.name = prefab.name;
        }

        // ------------------------------------------------------------------- the day

        void Scene(Vector3 centre, float span)
        {
            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = sunIntensity;
            sun.color = new Color(1f, 0.9569f, 0.8392f);
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = Mathf.Clamp01(sunShadowStrength);
            sunGo.transform.rotation = Quaternion.Euler(sunAngles.x, sunAngles.y, sunAngles.z);

            var day = new GameObject("DayNight");
            var clock = day.AddComponent<DemoClock>();
            clock.secondsPerGameHour = Mathf.Max(0.02f, realSecondsPerGameHour);
            clock.startHour = startHour;
            var sky = day.AddComponent<DemoSky>();
            sky.clock = clock;
            sky.sun = sun;
            // the haze is pushed past the far corner of the town: this camera opens
            // high enough to hold half a kilometre of city, and the demo sky's own
            // falloff turns everything past the middle block into white
            sky.linearHaze = new Vector2(span * 0.9f, span * 3f);
            var grade = day.AddComponent<DemoGrade>();
            grade.clock = clock;
            grade.look = look;

            var camGo = new GameObject("Demo Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 45f;
            cam.farClipPlane = 6000f;
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            var rig = camGo.AddComponent<DemoCamera>();
            rig.pivot = centre;
            // no printed map in this scene, so nothing has to be left room for: the
            // wheel may pull back until the whole town is in frame. Left at the demo
            // camera's own 180 m the boom clamps there and a town half a kilometre
            // across is looked at through a keyhole.
            rig.mapAt = span * 2f;
            rig.mapCeiling = span * 2f;
            rig.FrameSpan(span, fill: 0.75f);
            rig.pitch = 48f;
            rig.yaw = 30f;
            rig.showHint = true;
        }
#endif
    }
}
