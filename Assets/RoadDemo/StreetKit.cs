using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    // The road demo's street, as a kit: the same tiles on the same 5 m grid, the
    // same profile (sidewalk, two lanes of yellow-lined asphalt, sidewalk - half
    // width 5, kerb to kerb 20), and the same dressing rules the road demo lays
    // down street by street (SidewalkDressing): palms in pavement grates and the
    // tall lamp posts on the kerb's beat, the kerb furniture between them - bins,
    // the mailbox, a hydrant, meters, a pay phone, a news box, bike hoops - and a
    // nearly bare frontage of the odd bench, bin, planter or cabinet; manholes on
    // the carriageway - lifted out of RoadDemoBuilder (LoadPrefabs,
    // FillHorizontalSegment, DressSide, ManholePass) with their numbers kept, so
    // any scene that wants a street gets the road demo's street and not a sketch
    // of one. Editor-only loads, like it.
    public sealed class StreetKit
    {
        public const float Cell = 5f;
        /// <summary>The kerb strip a car is left standing on, each side of the
        /// carriageway. The road demo's own note (RoadDemoBuilder.ParkLane) has the
        /// reason: without it a parked car stands in the driving lane, and anything
        /// coming up behind can only get by over the crown - which it may do only with
        /// the far lane empty. The lanes themselves did not move; this is 2.5 m of
        /// asphalt added outside them.</summary>
        public const float ParkLane = 2.5f;
        public const float StreetHalf = RoadHalf + ParkLane;   // two 5 m lanes and a strip each side
        /// <summary>Half a carriageway with no kerb to park at - a road through a yard
        /// or a works, where the ground either side is the yard's own (LayRoadAlongX).
        /// Two lanes and nothing else; also the width of one marked lane.</summary>
        public const float RoadHalf = 5f;
        public const float SidewalkWidth = SidewalkDressing.Width;   // the road demo's pavement
        public const float OuterHalf = StreetHalf + SidewalkWidth; // kerb-strip outer edge

        /// <summary>The 5 m cell origins that close a carriageway of this half width,
        /// measured off its centre line - what a junction square is paved with and what
        /// a zebra band is laid in. Three cells for a street, two for a yard road.</summary>
        static float[] Square(float half)
        {
            int n = Mathf.Max(1, Mathf.RoundToInt(2f * half / Cell));
            var cells = new float[n];
            for (int k = 0; k < n; k++) cells[k] = -half + k * Cell;
            return cells;
        }

        const string CityEnv = "Assets/Synty/PolygonCity/Prefabs/Environments/";
        const string PalmEnv = "Assets/Synty/PolygonPalmCity/Prefabs/Environment/";
        const string PalmProps = "Assets/Synty/PolygonPalmCity/Prefabs/Props/";

        // RoadDemoBuilder._roadHalf: one half of a two-way street, laid twice, the
        // second turned about, so the white kerb line lands on both kerbs
        GameObject _roadHalf, _swStraight, _roadBare, _crossing, _swCorner;
        readonly List<GameObject> _palms = new List<GameObject>();
        static readonly List<GameObject> NoPalms = new List<GameObject>();

        /// <summary>Whether the kerbs get their palms. Off for a road where nobody
        /// planted any - a port's approach, an industrial street.</summary>
        public bool Palms = true;
        readonly List<GameObject> _grates = new List<GameObject>();
        readonly List<GameObject> _lamps = new List<GameObject>();
        readonly List<GameObject> _bins = new List<GameObject>();      // public litter bins, at the kerb
        readonly List<GameObject> _wallBins = new List<GameObject>();  // a building's own, at the wall
        readonly List<GameObject> _benches = new List<GameObject>();
        readonly List<GameObject> _planters = new List<GameObject>();
        readonly List<GameObject> _powerboxes = new List<GameObject>();
        readonly List<GameObject> _bushes = new List<GameObject>();
        readonly List<GameObject> _chairs = new List<GameObject>();
        readonly List<GameObject> _tables = new List<GameObject>();
        readonly List<GameObject> _umbrellas = new List<GameObject>();
        GameObject _bag, _bagOpen, _mailbox, _newsstand, _bikeStand, _manhole;
        GameObject _hydrant, _treeCage, _banner, _meter, _payPhone, _menuStand;

        readonly Transform _geometry, _flora;
        readonly float _y;
        bool _loaded;

        // the same pavement bookkeeping the road demo keeps: what ground the props
        // have taken, and how the three bands of a sidewalk are furnished
        readonly SidewalkPlan _plan = new SidewalkPlan();
        SidewalkDressing _dressing;
        StreetProps _props;

        /// <summary>What every prop laid has claimed - hand it to whatever walks
        /// people down this street so they walk round the furniture.</summary>
        public SidewalkPlan Plan => _plan;

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
            _roadHalf = L(CityEnv + "SM_Env_Road_YellowLines_02.prefab");
            _swStraight = L(CityEnv + "SM_Env_Sidewalk_Straight_01.prefab");
            _roadBare = L(CityEnv + "SM_Env_Road_Bare_01.prefab");
            _crossing = L(CityEnv + "SM_Env_Road_Crossing_01.prefab");
            _swCorner = L(CityEnv + "SM_Env_Sidewalk_Corner_01.prefab");

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
            // the road demo's own split: Bin_01/04 are the public litter bins at the
            // kerb, Bin_02 a building's own bin at the wall; Bin_03 is a dumpster
            Bag(_bins, "SM_Prop_Trash_Bin_01", "SM_Prop_Trash_Bin_04");
            Bag(_wallBins, "SM_Prop_Trash_Bin_02");
            Bag(_benches, "SM_Prop_Bench_Seat_01", "SM_Prop_Bench_Seat_02");
            Bag(_planters, "SM_Prop_Planter_01", "SM_Prop_Planter_02", "SM_Prop_Planter_03", "SM_Prop_Planter_04");
            Bag(_powerboxes, "SM_Prop_Powerbox_01");   // the free-standing cabinet only
            Bag(_bushes, "SM_Env_Bush_01", "SM_Env_Bush_02", "SM_Env_Bush_03");
            Bag(_chairs, "SM_Prop_Chair_01", "SM_Prop_Chair_03", "SM_Prop_Chair_04");
            Bag(_tables, "SM_Prop_Table_01", "SM_Prop_Table_Outdoor_01");
            Bag(_umbrellas, "SM_Prop_Umbrella_01", "SM_Prop_Umbrella_02", "SM_Prop_Umbrella_03");
            _bag = L(PalmProps + "SM_Prop_Trash_Bag_01.prefab");
            _bagOpen = L(PalmProps + "SM_Prop_Trash_Bag_Open_01.prefab");
            _mailbox = L(PalmProps + "SM_Prop_Mailbox_01.prefab");
            _newsstand = L(PalmProps + "SM_Prop_Newspaper_Stand_01.prefab");
            _bikeStand = L(PalmProps + "SM_Prop_Bike_Stand_02.prefab");
            _manhole = L(PalmProps + "SM_Prop_Manhole_01.prefab");
            _hydrant = L(PalmProps + "SM_Prop_Fire_Hydrant_01.prefab");
            _treeCage = L(PalmProps + "SM_Prop_Tree_Cage_01.prefab");
            _banner = L(PalmProps + "SM_Prop_Street_Flag_Sign_02.prefab");
            _meter = L(PalmProps + "SM_Prop_Parking_Meter_01.prefab");
            _payPhone = L(PalmProps + "SM_Prop_Pay_Phone_01.prefab");
            _menuStand = L(PalmProps + "SM_Prop_Menu_Stand_01.prefab");

            _loaded = _roadHalf && _swStraight;
            if (!_loaded)
                Debug.LogWarning("[StreetKit] PolygonCity road tiles missing - no street.");
            return _loaded;
#else
            return false;
#endif
        }
        /// <summary>A plain two-way street along X on centre line <paramref name="cz"/>,
        /// from <paramref name="xFrom"/> to <paramref name="xTo"/> (the 5 m beat, the
        /// odd metres taken by stretching every tile a hair rather than leaving a gap
        /// at the end), dressed on both sides the road demo's way. Either pavement may
        /// be left off - the cells either side of a junction, where a lorry's turn
        /// sweeps the corner and the kerb is splayed back (LayJunction lays the bare
        /// ground there) - and a short length laid that way is not dressed. Returns
        /// false when the tiles are missing.</summary>
        public bool LayAlongX(float cz, float xFrom, float xTo, bool southWalk = true, bool northWalk = true, bool dress = true)
            => LayAlongX(cz, xFrom, xTo, xFrom, xTo, southWalk, northWalk, dress);

        /// <summary>The same, with the pavements laid over a span of their own. What a
        /// stretch between two crossroads needs: the carriageway runs from one zebra
        /// band to the other, while the pavement runs between the corner slabs - the
        /// block's frontage, a cell and a pavement's width shorter at either end.</summary>
        public bool LayAlongX(float cz, float xFrom, float xTo, float walkFrom, float walkTo,
                              bool southWalk, bool northWalk, bool dress)
        {
            if (!_loaded && !Load()) return false;

            // RoadDemoBuilder.FillHorizontalSegment: the carriageway closed exactly
            int tiles = TileCount(xFrom, xTo);
            float len = (xTo - xFrom) / tiles;
            for (int k = 0; k < tiles; k++)
            {
                float mx = xFrom + k * len;
                PlaceTile(_roadHalf, mx, cz - RoadHalf, 270, len, Cell);
                PlaceTile(_roadHalf, mx, cz, 90, len, Cell);
                // the kerb strips, outside the marked lanes: where a car is left standing
                PlaceTile(_roadBare, mx, cz - StreetHalf, 90, len, ParkLane);
                PlaceTile(_roadBare, mx, cz + RoadHalf, 90, len, ParkLane);
            }
            // and the pavement tiles stretched across to the pavement's width, kerb at the road
            int walks = TileCount(walkFrom, walkTo);
            float wlen = (walkTo - walkFrom) / walks;
            for (int k = 0; k < walks; k++)
            {
                float mx = walkFrom + k * wlen;
                if (southWalk) PlaceTile(_swStraight, mx, cz - StreetHalf - SidewalkWidth, 0, wlen, SidewalkWidth);
                if (northWalk) PlaceTile(_swStraight, mx, cz + StreetHalf, 180, wlen, SidewalkWidth);
            }
            if (!dress) return true;

            var start = new Vector3(walkFrom, 0f, cz);
            float dressed = walkTo - walkFrom;
            if (northWalk) DressSide(start, Vector3.right, dressed, Vector3.forward);
            if (southWalk) DressSide(start, Vector3.right, dressed, Vector3.back);
            Manholes(new Vector3(xFrom, 0f, cz), Vector3.right, xTo - xFrom);
            return true;
        }

        /// <summary>The same street along Z on centre line <paramref name="cx"/>: the
        /// west pavement, two lanes, the east pavement.</summary>
        public bool LayAlongZ(float cx, float zFrom, float zTo, bool westWalk = true, bool eastWalk = true, bool dress = true)
            => LayAlongZ(cx, zFrom, zTo, zFrom, zTo, westWalk, eastWalk, dress);

        /// <summary>The same, with the pavements laid over a span of their own
        /// (see the X overload).</summary>
        public bool LayAlongZ(float cx, float zFrom, float zTo, float walkFrom, float walkTo,
                              bool westWalk, bool eastWalk, bool dress)
        {
            if (!_loaded && !Load()) return false;

            // RoadDemoBuilder.FillVerticalSegment: west half then east half, unturned
            int tiles = TileCount(zFrom, zTo);
            float len = (zTo - zFrom) / tiles;
            for (int k = 0; k < tiles; k++)
            {
                float mz = zFrom + k * len;
                PlaceTile(_roadHalf, cx - RoadHalf, mz, 0, Cell, len);
                PlaceTile(_roadHalf, cx, mz, 180, Cell, len);
                PlaceTile(_roadBare, cx - StreetHalf, mz, 0, ParkLane, len);
                PlaceTile(_roadBare, cx + RoadHalf, mz, 0, ParkLane, len);
            }
            int walks = TileCount(walkFrom, walkTo);
            float wlen = (walkTo - walkFrom) / walks;
            for (int k = 0; k < walks; k++)
            {
                float mz = walkFrom + k * wlen;
                if (westWalk) PlaceTile(_swStraight, cx - StreetHalf - SidewalkWidth, mz, 90, SidewalkWidth, wlen);
                if (eastWalk) PlaceTile(_swStraight, cx + StreetHalf, mz, 270, SidewalkWidth, wlen);
            }
            if (!dress) return true;

            var start = new Vector3(cx, 0f, walkFrom);
            float dressed = walkTo - walkFrom;
            if (eastWalk) DressSide(start, Vector3.forward, dressed, Vector3.right);
            if (westWalk) DressSide(start, Vector3.forward, dressed, Vector3.left);
            Manholes(new Vector3(cx, 0f, zFrom), Vector3.forward, zTo - zFrom);
            return true;
        }

        /// <summary>How many tiles close [from, to] at nearest to the 5 m beat -
        /// RoadDemoBuilder.TileCount.</summary>
        static int TileCount(float from, float to) => Mathf.Max(1, Mathf.RoundToInt((to - from) / Cell));

        /// <summary>The carriageway alone - two lanes of yellow-lined asphalt, no
        /// pavement, no furniture - along X on centre line <paramref name="cz"/>. A
        /// road inside a yard or a works, where the ground either side is the
        /// yard's own. Laid in 5 m cells from <paramref name="xFrom"/>.</summary>
        public bool LayRoadAlongX(float cz, float xFrom, float xTo)
        {
            if (!_loaded && !Load()) return false;
            for (float mx = xFrom; mx < xTo - 0.1f; mx += Cell)
            {
                float len = Mathf.Min(Cell, xTo - mx);   // the last cell cut to the end, so nothing overlaps a junction
                PlaceTile(_roadHalf, mx, cz - RoadHalf, 270, len, Cell);
                PlaceTile(_roadHalf, mx, cz, 90, len, Cell);
            }
            return true;
        }

        /// <summary>The same along Z on centre line <paramref name="cx"/>.</summary>
        public bool LayRoadAlongZ(float cx, float zFrom, float zTo)
        {
            if (!_loaded && !Load()) return false;
            for (float mz = zFrom; mz < zTo - 0.1f; mz += Cell)
            {
                // RoadDemoBuilder's vertical street: west half, then the east half turned about
                float len = Mathf.Min(Cell, zTo - mz);
                PlaceTile(_roadHalf, cx - RoadHalf, mz, 0, Cell, len);
                PlaceTile(_roadHalf, cx, mz, 180, Cell, len);
            }
            return true;
        }

        /// <summary>Where two of these meet: the road demo's junction square, four
        /// cells of bare asphalt centred on the crossing, and a pavement cap on any
        /// side asked for - the side a street's pavement runs past on, which makes the
        /// square a T or a bend rather than a crossroads. <paramref name="splaySouth"/>
        /// / <paramref name="splayNorth"/> lay that many cells of bare ground in place
        /// of the pavement either side of the square on that flank - the splayed kerb
        /// a lorry's turn needs; the street is laid without its pavement there
        /// (LayAlongX's southWalk/northWalk).</summary>
        public bool LayJunction(float cx, float cz, bool capNorth = false, bool capSouth = false, bool capEast = false, bool capWest = false,
                                int splaySouth = 0, int splayNorth = 0, float half = StreetHalf)
        {
            if (!_loaded && !Load()) return false;
            if (_roadBare == null) return false;
            // as wide as the roads that meet here: three cells for a street with its
            // parking strips, two for a bare yard road (pass RoadHalf for one of those)
            var square = Square(half);
            foreach (float dx in square)
                foreach (float dz in square)
                    PlaceCell(_roadBare, cx + dx, cz + dz, 0);
            if (capNorth) foreach (float dx in square) PlaceTile(_swStraight, cx + dx, cz + half, 180, Cell, SidewalkWidth);
            if (capSouth) foreach (float dx in square) PlaceTile(_swStraight, cx + dx, cz - half - SidewalkWidth, 0, Cell, SidewalkWidth);
            if (capEast) foreach (float dz in square) PlaceTile(_swStraight, cx + half, cz + dz, 270, SidewalkWidth, Cell);
            if (capWest) foreach (float dz in square) PlaceTile(_swStraight, cx - half - SidewalkWidth, cz + dz, 90, SidewalkWidth, Cell);
            for (int k = 1; k <= splaySouth; k++)
            {
                PlaceTile(_roadBare, cx - half - Cell * k, cz - half - SidewalkWidth, 0, Cell, SidewalkWidth);
                PlaceTile(_roadBare, cx + half + Cell * (k - 1), cz - half - SidewalkWidth, 0, Cell, SidewalkWidth);
            }
            for (int k = 1; k <= splayNorth; k++)
            {
                PlaceTile(_roadBare, cx - half - Cell * k, cz + half, 0, Cell, SidewalkWidth);
                PlaceTile(_roadBare, cx + half + Cell * (k - 1), cz + half, 0, Cell, SidewalkWidth);
            }
            return true;
        }
        /// <summary>A full crossroads at (<paramref name="cx"/>, <paramref name="cz"/>),
        /// the road demo's way (BuildNodeGeometry): the square of bare asphalt, a zebra
        /// band across every arm that carries on, a pavement cap across every arm that
        /// does not - which makes the square a T or a bend - and the four corner slabs,
        /// kerb turned in towards the crossing. The streets that meet here are laid
        /// short of it: their carriageway from the far side of the zebra band (a cell
        /// out from the square), their pavements from the far side of the corner slab
        /// (a pavement's width further still).</summary>
        public bool LayCrossroads(float cx, float cz, bool north = true, bool south = true,
                                  bool east = true, bool west = true)
        {
            if (!_loaded && !Load()) return false;
            if (_roadBare == null) return false;

            // the square is as wide as the street: three cells across, the parking
            // strips included, and the zebra bands run the same width - a crossing
            // goes kerb to kerb, over the strip a car is left on
            var square = Square(StreetHalf);
            foreach (float dx in square)
                foreach (float dz in square)
                    PlaceCell(_roadBare, cx + dx, cz + dz, 0);

            // north and south: the zebra lies across the street running through in Z
            foreach (float dx in square)
            {
                if (north && _crossing) PlaceCell(_crossing, cx + dx, cz + StreetHalf, 90);
                else if (!north) PlaceTile(_swStraight, cx + dx, cz + StreetHalf, 180, Cell, SidewalkWidth);
                if (south && _crossing) PlaceCell(_crossing, cx + dx, cz - StreetHalf - Cell, 90);
                else if (!south) PlaceTile(_swStraight, cx + dx, cz - StreetHalf - SidewalkWidth, 0, Cell, SidewalkWidth);
            }
            // east and west: across the street running through in X
            foreach (float dz in square)
            {
                if (east && _crossing) PlaceCell(_crossing, cx + StreetHalf, cz + dz, 0);
                else if (!east) PlaceTile(_swStraight, cx + StreetHalf, cz + dz, 270, SidewalkWidth, Cell);
                if (west && _crossing) PlaceCell(_crossing, cx - StreetHalf - Cell, cz + dz, 0);
                else if (!west) PlaceTile(_swStraight, cx - StreetHalf - SidewalkWidth, cz + dz, 90, SidewalkWidth, Cell);
            }

            // the corner slabs, as wide and as deep as the pavements they join
            float outer = StreetHalf + SidewalkWidth;
            PlaceTile(_swCorner, cx - outer, cz - outer, 0, SidewalkWidth, SidewalkWidth);
            PlaceTile(_swCorner, cx - outer, cz + StreetHalf, 90, SidewalkWidth, SidewalkWidth);
            PlaceTile(_swCorner, cx + StreetHalf, cz + StreetHalf, 180, SidewalkWidth, SidewalkWidth);
            PlaceTile(_swCorner, cx + StreetHalf, cz - outer, 270, SidewalkWidth, SidewalkWidth);
            return true;
        }

        // RoadDemoBuilder.PlaceTile: the 5 m piece laid to cover [mx, mx+sizeX] x
        // [mz, mz+sizeZ] - pivot at its +X/+Z corner turned by the yaw, scaled to fit.
        void PlaceTile(GameObject prefab, float mx, float mz, int yaw, float sizeX, float sizeZ)
        {
            if (prefab == null) return;
            Vector3 pivot, scale;
            switch (yaw)
            {
                case 0: pivot = new Vector3(mx + sizeX, _y, mz + sizeZ); scale = new Vector3(sizeX / Cell, 1f, sizeZ / Cell); break;
                case 90: pivot = new Vector3(mx + sizeX, _y, mz); scale = new Vector3(sizeZ / Cell, 1f, sizeX / Cell); break;
                case 180: pivot = new Vector3(mx, _y, mz); scale = new Vector3(sizeX / Cell, 1f, sizeZ / Cell); break;
                default: pivot = new Vector3(mx, _y, mz + sizeZ); scale = new Vector3(sizeZ / Cell, 1f, sizeX / Cell); break;
            }
            var go = Object.Instantiate(prefab, pivot, Quaternion.Euler(0f, yaw, 0f), _geometry);
            if ((scale - Vector3.one).sqrMagnitude > 1e-6f) go.transform.localScale = scale;
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

        void Prop(GameObject prefab, Vector3 pos, float yaw, Transform parent)
        {
            if (prefab == null) return;
            Object.Instantiate(prefab, pos, Quaternion.Euler(0f, yaw, 0f), parent);
            if (SidewalkPlan.Footprint(prefab, pos, yaw, out var box)) _plan.Take(box);
        }

        // The road demo's own rule, shared with it (SidewalkDressing): the kerb
        // strip carries the road's furniture, the frontage carries the building's,
        // and the walk between them is left alone.
        void DressSide(Vector3 start, Vector3 dir, float len, Vector3 outward)
        {
            if (_dressing == null)
            {
                _props = new StreetProps
                {
                    Grates = _grates, Palms = Palms ? _palms : NoPalms, Lamps = _lamps,
                    KerbBins = _bins, WallBins = _wallBins, Benches = _benches, Planters = _planters,
                    Powerboxes = _powerboxes,
                    Chairs = _chairs, Tables = _tables, Umbrellas = _umbrellas,
                    TreeCage = _treeCage, Banner = _banner,
                    Bag = _bag, BagOpen = _bagOpen, Mailbox = _mailbox, Newsstand = _newsstand,
                    BikeStand = _bikeStand, Hydrant = _hydrant,
                    Meter = _meter, PayPhone = _payPhone, MenuStand = _menuStand,
                };
                _dressing = new SidewalkDressing
                {
                    Plan = _plan,
                    Geometry = _geometry,
                    Flora = _flora,
                    Lift = _y + 0.1f,
                    BenchLaid = (pos, yaw) => Benches.Add((pos, yaw)),
                };
            }
            _dressing.Dress(_props, start, dir, outward, len, StreetHalf);
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
