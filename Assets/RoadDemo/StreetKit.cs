using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    // The road demo's street, as a kit: the same tiles on the same 5 m grid, the
    // same profile (sidewalk, two lanes of yellow-lined asphalt, sidewalk - half
    // width 5, kerb to kerb 20), and the same dressing rules the road demo lays
    // down street by street - palms in pavement grates, the tall lamp posts and
    // saplings on the kerb strip every 7 m; benches, bins and bags, planters,
    // sidewalk cafes, bike stands, hedges, utility boxes, a mailbox or a news
    // stand on the outer strip every 9 m; manholes on the carriageway - lifted
    // out of RoadDemoBuilder (LoadPrefabs, FillHorizontalSegment, DressSide,
    // ManholePass) with their numbers kept, so any scene that wants a street gets
    // the road demo's street and not a sketch of one. Editor-only loads, like it.
    public sealed class StreetKit
    {
        public const float Cell = 5f;
        public const float StreetHalf = 5f;   // carriageway half width: 2 lanes
        public const float SidewalkWidth = 5f;
        public const float OuterHalf = StreetHalf + SidewalkWidth; // kerb-strip outer edge

        const string CityEnv = "Assets/Synty/PolygonCity/Prefabs/Environments/";
        const string PalmEnv = "Assets/Synty/PolygonPalmCity/Prefabs/Environment/";
        const string PalmProps = "Assets/Synty/PolygonPalmCity/Prefabs/Props/";

        GameObject _roadWest, _roadEast, _swStraight;
        readonly List<GameObject> _palms = new List<GameObject>();
        readonly List<GameObject> _grates = new List<GameObject>();
        readonly List<GameObject> _lamps = new List<GameObject>();
        readonly List<GameObject> _bins = new List<GameObject>();
        readonly List<GameObject> _benches = new List<GameObject>();
        readonly List<GameObject> _planters = new List<GameObject>();
        readonly List<GameObject> _powerboxes = new List<GameObject>();
        readonly List<GameObject> _bushes = new List<GameObject>();
        readonly List<GameObject> _saplings = new List<GameObject>();
        readonly List<GameObject> _hedges = new List<GameObject>();
        readonly List<GameObject> _topiary = new List<GameObject>();
        readonly List<GameObject> _chairs = new List<GameObject>();
        readonly List<GameObject> _tables = new List<GameObject>();
        readonly List<GameObject> _umbrellas = new List<GameObject>();
        GameObject _bag, _bagOpen, _mailbox, _newsstand, _bikeStand, _manhole;

        readonly Transform _geometry, _flora;
        readonly float _y;
        bool _loaded;

        /// <summary>The benches laid, for anything that wants to seat people on them.</summary>
        public readonly List<(Vector3 pos, float yaw)> Benches = new List<(Vector3, float)>();

        /// <param name="root">Parent for everything laid.</param>
        /// <param name="y">Ground offset: the road demo lays tiles at 0 and walks its
        /// people at 0.1 (the pavement top); a scene whose floor is at 0 lays at -0.1.</param>
        public StreetKit(Transform root, float y = 0f)
        {
            _geometry = new GameObject("Street").transform;
            _geometry.SetParent(root, false);
            // palms and bushes keep out of any static batch: their wind shader displaces
            // vertices in object space, exactly as the road demo keeps its Flora apart
            _flora = new GameObject("Street Flora").transform;
            _flora.SetParent(root, false);
            _y = y;
        }

        public bool Load()
        {
#if UNITY_EDITOR
            GameObject L(string path) => UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
            _roadWest = L(CityEnv + "SM_Env_Road_YellowLines_02.prefab");
            _roadEast = L(CityEnv + "SM_Env_Road_YellowLines_01.prefab");
            _swStraight = L(CityEnv + "SM_Env_Sidewalk_Straight_01.prefab");

            for (int i = 1; i <= 6; i++)
            {
                var palm = L(PalmEnv + "SM_Env_Tree_Palm_0" + i + ".prefab");
                if (palm != null) _palms.Add(palm);
            }

            void Bag(List<GameObject> into, params string[] names)
            {
                foreach (var n in names)
                {
                    var g = L(PalmProps + n + ".prefab") ?? L(PalmEnv + n + ".prefab");
                    if (g != null) into.Add(g);
                }
            }
            Bag(_grates, "SM_Env_Plant_Grate_01", "SM_Env_Plant_Grate_02");
            // Lamp_01 only - the tall arm post that hangs its head over the road
            Bag(_lamps, "SM_Prop_Street_Lamp_01");
            Bag(_bins, "SM_Prop_Trash_Bin_01", "SM_Prop_Trash_Bin_02", "SM_Prop_Trash_Bin_03", "SM_Prop_Trash_Bin_04");
            Bag(_benches, "SM_Prop_Bench_Seat_01", "SM_Prop_Bench_Seat_02");
            Bag(_planters, "SM_Prop_Planter_01", "SM_Prop_Planter_02", "SM_Prop_Planter_03", "SM_Prop_Planter_04");
            Bag(_powerboxes, "SM_Prop_Powerbox_01", "SM_Prop_PowerBox_02", "SM_Prop_PowerBoxes_02", "SM_Prop_PowerBoxes_03");
            Bag(_bushes, "SM_Env_Bush_01", "SM_Env_Bush_02", "SM_Env_Bush_03");
            Bag(_saplings, "SM_Env_Tree_Palm_Sapling_01", "SM_Env_Tree_Palm_Sapling_02", "SM_Env_Tree_Palm_Sapling_03",
                "SM_Env_Tree_Palm_Small_01", "SM_Env_Tree_Palm_Small_03", "SM_Env_Tree_Palm_Small_04", "SM_Env_Tree_Palm_Small_05");
            Bag(_hedges, "SM_Env_Hedge_02", "SM_Env_Hedge_03", "SM_Env_Hedge_04");
            Bag(_topiary, "SM_Env_Hedge_Topiary_02", "SM_Env_Hedge_Topiary_04", "SM_Env_Hedge_Topiary_05", "SM_Env_Hedge_Topiary_06");
            Bag(_chairs, "SM_Prop_Chair_01", "SM_Prop_Chair_03", "SM_Prop_Chair_04");
            Bag(_tables, "SM_Prop_Table_01", "SM_Prop_Table_Outdoor_01");
            Bag(_umbrellas, "SM_Prop_Umbrella_01", "SM_Prop_Umbrella_02", "SM_Prop_Umbrella_03");
            _bag = L(PalmProps + "SM_Prop_Trash_Bag_01.prefab");
            _bagOpen = L(PalmProps + "SM_Prop_Trash_Bag_Open_01.prefab");
            _mailbox = L(PalmProps + "SM_Prop_Mailbox_01.prefab");
            _newsstand = L(PalmProps + "SM_Prop_Newspaper_Stand_01.prefab");
            _bikeStand = L(PalmProps + "SM_Prop_Bike_Stand_02.prefab");
            _manhole = L(PalmProps + "SM_Prop_Manhole_01.prefab");

            _loaded = _roadWest && _roadEast && _swStraight;
            if (!_loaded)
                Debug.LogWarning("[StreetKit] PolygonCity road tiles missing - no street.");
            return _loaded;
#else
            return false;
#endif
        }

        // ------------------------------------------------------------------ the street

        /// <summary>A plain two-way street along X on centre line <paramref name="cz"/>,
        /// from <paramref name="xFrom"/> to <paramref name="xTo"/> (5 m grid), dressed
        /// on both sides the road demo's way. Returns false when the tiles are missing.</summary>
        public bool LayAlongX(float cz, float xFrom, float xTo)
        {
            if (!_loaded && !Load()) return false;

            for (float mx = xFrom; mx < xTo - 0.1f; mx += Cell)
            {
                // RoadDemoBuilder.FillHorizontalSegment, the plain-street branch
                PlaceCell(_swStraight, mx, cz - 10f, 0);
                PlaceCell(_roadEast, mx, cz - 5f, 90);
                PlaceCell(_roadWest, mx, cz, 90);
                PlaceCell(_swStraight, mx, cz + 5f, 180);
            }

            var start = new Vector3(xFrom, 0f, cz);
            float len = xTo - xFrom;
            DressSide(start, Vector3.right, len, Vector3.forward);
            DressSide(start, Vector3.right, len, Vector3.back);
            Manholes(start, Vector3.right, len);
            return true;
        }

        // RoadDemoBuilder.PlaceCell: corner pivots per yaw on the 5 m grid.
        void PlaceCell(GameObject prefab, float mx, float mz, int yaw)
        {
            Vector3 pivot;
            switch (yaw)
            {
                case 0: pivot = new Vector3(mx + Cell, _y, mz + Cell); break;
                case 90: pivot = new Vector3(mx + Cell, _y, mz); break;
                case 180: pivot = new Vector3(mx, _y, mz); break;
                default: pivot = new Vector3(mx, _y, mz + Cell); break;
            }
            Object.Instantiate(prefab, pivot, Quaternion.Euler(0f, yaw, 0f), _geometry);
        }

        // ------------------------------------------------------------------ dressing

        static float YawOf(Vector3 d) => Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        static T Pick<T>(List<T> l) => l[Random.Range(0, l.Count)];

        void Prop(GameObject prefab, Vector3 pos, float yaw, Transform parent)
        {
            if (prefab != null)
                Object.Instantiate(prefab, pos, Quaternion.Euler(0f, yaw, 0f), parent);
        }

        void PlaceBench(Vector3 pos, float yaw)
        {
            if (_benches.Count == 0) return;
            Prop(Pick(_benches), pos, yaw, _geometry);
            Benches.Add((pos, yaw));
        }

        // RoadDemoBuilder.DressSide, the ordinary-street branch, verbatim in its
        // spacings and odds: props ride 0.1 above the tile plane (the pavement top).
        void DressSide(Vector3 start, Vector3 dir, float len, Vector3 outward)
        {
            float half = StreetHalf;
            float faceRoad = YawOf(-outward);
            float alongRoad = YawOf(dir);
            Vector3 At(float t, float lat) => start + dir * t + outward * lat + Vector3.up * (_y + 0.1f);

            // kerb strip: palms in pavement grates, street lamps, saplings
            for (float t = 5f; t < len - 5f; t += 7f)
            {
                var pos = At(t + Random.Range(-1.2f, 1.2f), half + 1.45f); // grate clear of the kerb stone, trunk clear of the walkers
                float r = Random.value;
                if (r < 0.4f && _grates.Count > 0 && _palms.Count > 0)
                {
                    Prop(Pick(_grates), pos, Random.Range(0, 4) * 90f, _geometry);
                    Prop(Pick(_palms), pos, Random.value * 360f, _flora);
                }
                else if (r < 0.6f && _lamps.Count > 0)
                    Prop(Pick(_lamps), pos, faceRoad, _geometry);
                else if (r < 0.72f && _saplings.Count > 0)
                    Prop(Pick(_saplings), pos, Random.value * 360f, _flora);
            }

            // outer strip of ordinary streets: furniture, junk and utilities
            for (float t = 4f; t < len - 4f; t += 9f)
            {
                var pos = At(t + Random.Range(-2f, 2f), half + 4.1f);
                float r = Random.value;
                if (r < 0.16f && _benches.Count > 0)
                {
                    PlaceBench(pos, faceRoad);
                    if (Random.value < 0.6f && _bins.Count > 0)
                        Prop(Pick(_bins), pos + dir * 2.1f, faceRoad, _geometry);
                }
                else if (r < 0.3f && _bins.Count > 0)
                {
                    Prop(Pick(_bins), pos, faceRoad + Random.Range(-25f, 25f), _geometry);
                    int bags = Random.Range(0, 3);
                    for (int k = 0; k < bags; k++)
                        Prop(Random.value < 0.7f ? _bag : _bagOpen,
                            pos + new Vector3(Random.Range(-0.9f, 0.9f), 0f, Random.Range(-0.9f, 0.9f)),
                            Random.value * 360f, _geometry);
                }
                else if (r < 0.45f)
                {
                    int bags = Random.Range(1, 4);
                    for (int k = 0; k < bags; k++)
                        Prop(Random.value < 0.7f ? _bag : _bagOpen,
                            pos + new Vector3(Random.Range(-1.1f, 1.1f), 0f, Random.Range(-1.1f, 1.1f)),
                            Random.value * 360f, _geometry);
                }
                else if (r < 0.55f && _planters.Count > 0)
                    Prop(Pick(_planters), pos, alongRoad, _flora);
                else if (r < 0.66f && _bushes.Count > 0)
                    Prop(Pick(_bushes), pos, Random.value * 360f, _flora);
                else if (r < 0.73f && _tables.Count > 0 && _chairs.Count > 0)
                {
                    // sidewalk cafe: table, a few chairs facing it, often an umbrella
                    Prop(Pick(_tables), pos, faceRoad + Random.Range(-20f, 20f), _geometry);
                    int chairs = Random.Range(2, 4);
                    for (int k = 0; k < chairs; k++)
                    {
                        float ang = Random.value * 360f;
                        var cpos = pos + Quaternion.Euler(0f, ang, 0f) * Vector3.forward * 1.15f;
                        Prop(Pick(_chairs), cpos, ang + 180f, _geometry);
                    }
                    if (Random.value < 0.6f && _umbrellas.Count > 0)
                        Prop(Pick(_umbrellas), pos, Random.value * 360f, _flora);
                }
                else if (r < 0.79f && _bikeStand != null)
                {
                    int hoops = Random.Range(2, 4);
                    for (int k = 0; k < hoops; k++)
                        Prop(_bikeStand, pos + dir * (k * 0.9f), alongRoad, _geometry);
                }
                else if (r < 0.85f && _hedges.Count > 0)
                {
                    Prop(Pick(_hedges), pos, alongRoad, _flora);
                    Prop(Pick(_hedges), pos + dir * 2.75f, alongRoad, _flora);
                }
                else if (r < 0.89f && _topiary.Count > 0)
                    Prop(Pick(_topiary), pos, Random.value * 360f, _flora);
                else if (r < 0.93f && _powerboxes.Count > 0)
                    Prop(Pick(_powerboxes), pos, faceRoad, _geometry);
                else if (r < 0.96f)
                    Prop(_mailbox, pos, faceRoad, _geometry);
                else
                    Prop(_newsstand, pos, faceRoad, _geometry);
            }
        }

        // RoadDemoBuilder.ManholePass, for one plain street: one or two covers.
        void Manholes(Vector3 start, Vector3 dir, float len)
        {
            if (_manhole == null) return;
            int count = Random.Range(1, 3);
            var side = new Vector3(dir.z, 0f, -dir.x);
            for (int k = 0; k < count; k++)
                Prop(_manhole,
                    start + dir * Random.Range(4f, len - 4f) + side * Random.Range(-3f, 3f) +
                    Vector3.up * (_y + 0.02f),
                    Random.value * 360f, _geometry);
        }
    }
}
