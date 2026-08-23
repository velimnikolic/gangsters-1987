using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>What a block is FOR, as the plan sees it. Not a palette and not a
    /// building list - the bakes are chosen by the size of the lot they stand on and
    /// always were. A zone is the one thing the grid could never say for itself:
    /// WHERE in the city a block is. Downtown is the middle, the rim is houses, the
    /// works are on one shore, and the water has a frontage of its own.</summary>
    public enum CityZone
    {
        Downtown,
        Commercial,
        Waterfront,
        Industrial,
        Residential,
    }

    // The zoning, and the pocket parks that come out of it.
    //
    // The live grid had no spatial notion of any kind: a block's contents came from
    // the SIZE of its interior and nothing else, so a warehouse lot and a downtown
    // lot of the same measurements were the same block, and the city read the same
    // in the middle as at its edge. That is the second half of what makes a plan
    // look drawn rather than grown - the first half being streets that all run the
    // full width of the map (RoadDemoBuilder.Closes.cs).
    //
    // Three things hang off a zone, and no fourth: how likely a street beside it is
    // to be closed, how early its lot is served when the hand-composed blocks are
    // handed out (so the good ones concentrate downtown and the rim gets the rolled
    // stock and the terraces), and whether the lot is a block at all or a pocket
    // park. Nothing here builds a building; it decides which lot gets asked first.
    public partial class RoadDemoBuilder
    {
        [Header("Zoning")]
        [Tooltip("Give the blocks a place in the city - downtown in the middle, houses " +
                 "at the rim, works on one shore, a frontage along the water - and let " +
                 "the closures, the block order and the pocket parks read it. Off is " +
                 "the old city: every lot filled by its size alone.")]
        public bool zoneCity = true;

        [Tooltip("Same seed, same works shore and the same pocket parks.")]
        public int zoneSeed = 23;

        [Tooltip("Share of the block interiors left as pocket parks - a square of lawn " +
                 "with paths, trees and a fountain instead of a bake. Rolled against " +
                 "the zone, so the houses get most of them and downtown almost none. " +
                 "0 for a city built out to every kerb.")]
        [Range(0f, 0.3f)] public float pocketParkShare = 0.08f;

        // [column, row] over the BLOCK cells, one shorter than the road arrays on each
        // axis. Null until PlanZones has run, and null for good with zoning off.
        CityZone[,] _zones;
        bool[,] _pocketParks;

        /// <summary>The zone of the block between vertical roads i, i+1 and horizontal
        /// roads j, j+1. Residential for anything off the grid or before the pass has
        /// run - the answer a city gives about ground it has no plan for.</summary>
        public CityZone ZoneAt(int i, int j)
        {
            if (_zones == null) return CityZone.Residential;
            if (i < 0 || i >= _zones.GetLength(0) || j < 0 || j >= _zones.GetLength(1))
                return CityZone.Residential;
            return _zones[i, j];
        }

        /// <summary>Whether this interior was left as a pocket park rather than built
        /// on. Read by BuildBlocks, which lays the lawn instead of asking for a bake.</summary>
        bool IsPocketPark(int i, int j)
        {
            if (_pocketParks == null) return false;
            if (i < 0 || i >= _pocketParks.GetLength(0) || j < 0 || j >= _pocketParks.GetLength(1))
                return false;
            return _pocketParks[i, j];
        }

        // How early a lot is served when the hand-composed blocks are handed out.
        // Downtown first: LotBakeFor prefers a bake nothing has stood yet, so whoever
        // asks first gets the composed block and whoever asks last gets the rolled
        // stock or the generic terrace. That IS the density gradient - the middle of
        // the city carries what somebody arranged by hand, the rim carries houses.
        static int ZoneServingOrder(CityZone zone)
        {
            switch (zone)
            {
                case CityZone.Downtown: return 0;
                case CityZone.Commercial: return 1;
                case CityZone.Waterfront: return 2;
                case CityZone.Industrial: return 3;
                default: return 4;
            }
        }

        /// <summary>What the zoning wants of a street segment, added to its closure
        /// roll (RoadDemoBuilder.Closes.cs). Positive shuts it sooner. A downtown
        /// crossroads stays a crossroads: the middle of a city is the one part of it
        /// that really was surveyed, and a dead end there reads as damage rather than
        /// as growth. The suburbs are the opposite.</summary>
        float ClosureBias(bool vertical, int road, int gap)
        {
            if (_zones == null) return 0f;
            // the two blocks the segment runs between: the more central of them decides,
            // so a street on the edge of downtown is downtown's street
            CityZone a, b;
            if (vertical) { a = ZoneAt(road - 1, gap); b = ZoneAt(road, gap); }
            else { a = ZoneAt(gap, road - 1); b = ZoneAt(gap, road); }
            var zone = ZoneServingOrder(a) <= ZoneServingOrder(b) ? a : b;
            // The numbers are bigger than a roll, on purpose. The roll is uniform on
            // [0, 1), so a bias under 1 only shuffles the order and a share of a third
            // then eats a third of EVERY zone alike - measured, that left five blocks in
            // the whole city still standing on four streets, downtown included, which is
            // a maze and not a city. At this size the bias sorts the zones into bands:
            // the rim goes first and downtown is never reached at all, which is what a
            // real plan looks like - the middle was surveyed, the edge grew.
            switch (zone)
            {
                case CityZone.Downtown: return -1.5f;
                case CityZone.Commercial: return -0.6f;
                case CityZone.Waterfront: return -0.35f;
                case CityZone.Industrial: return 0.1f;
                default: return 0.5f;
            }
        }

        // ------------------------------------------------------------------- plan

        void PlanZones()
        {
            _zones = null;
            _pocketParks = null;
            if (!zoneCity) return;
            int nv = verticalRoadX == null ? 0 : verticalRoadX.Length;
            int nh = horizontalRoadZ == null ? 0 : horizontalRoadZ.Length;
            int cols = nv - 1, rows = nh - 1;
            if (cols < 1 || rows < 1) return;

            _zones = new CityZone[cols, rows];
            _pocketParks = new bool[cols, rows];
            var rng = new System.Random(zoneSeed * 6151 + spacingSeed);

            // the works stand on one shore, drawn once for the city: an industrial belt
            // ringing the whole town would leave it no rim to be houses on
            int worksSide = rng.Next(4);   // 0 south, 1 east, 2 north, 3 west

            // Rings measured in BLOCK indices rather than metres, and Chebyshev rather
            // than Euclidean: a town's zones are read off its street plan, which is
            // rectangles about a middle, not circles about a point.
            float midCol = (cols - 1) * 0.5f, midRow = (rows - 1) * 0.5f;
            float spanCol = Mathf.Max(midCol, 0.5f), spanRow = Mathf.Max(midRow, 0.5f);

            for (int i = 0; i < cols; i++)
                for (int j = 0; j < rows; j++)
                {
                    float ring = Mathf.Max(Mathf.Abs(i - midCol) / spanCol,
                                           Mathf.Abs(j - midRow) / spanRow);
                    // The rings are wide because the seams eat the middle: a river down
                    // row gap 3 and another down column gap 7 cross exactly where
                    // downtown would be, and a tight ring measured against them leaves
                    // the city with three central blocks and no core at all.
                    CityZone zone = ring <= 0.45f ? CityZone.Downtown
                        : ring <= 0.75f ? CityZone.Commercial
                        : CityZone.Residential;

                    // the works: the outermost ring of the drawn shore, and only there
                    bool onWorksShore =
                        worksSide == 0 ? j == 0 :
                        worksSide == 1 ? i == cols - 1 :
                        worksSide == 2 ? j == rows - 1 : i == 0;
                    if (onWorksShore && zone == CityZone.Residential) zone = CityZone.Industrial;

                    // and a block with a river along one side is a frontage rather than
                    // houses. Only houses, though: downtown stands on the quay in every
                    // city there is, and with two rivers crossing the middle a frontage
                    // that outranked the rings would swallow the core - measured, it
                    // took 27 of 66 blocks and left downtown three.
                    if (zone == CityZone.Residential && TouchesRiver(i, j)) zone = CityZone.Waterfront;

                    _zones[i, j] = zone;
                }

            PlanPocketParks(rng, cols, rows);

            var tally = new Dictionary<CityZone, int>();
            for (int i = 0; i < cols; i++)
                for (int j = 0; j < rows; j++)
                {
                    if (InSeam(i, j)) continue;
                    tally.TryGetValue(_zones[i, j], out int have);
                    tally[_zones[i, j]] = have + 1;
                }
            var story = new List<string>();
            foreach (var pair in tally) story.Add($"{pair.Value} {pair.Key.ToString().ToLowerInvariant()}");
            Debug.Log($"[RoadDemo] zoning (seed {zoneSeed}, works on the " +
                      $"{(worksSide == 0 ? "south" : worksSide == 1 ? "east" : worksSide == 2 ? "north" : "west")} " +
                      $"shore): " + string.Join(", ", story));
        }

        // Whether the river runs along any side of this block. The seam list is the
        // truth about water inside the grid, and a block is beside it when one of the
        // four gaps round it carries a river.
        bool TouchesRiver(int i, int j)
        {
            bool River(Seam s) => s != null && s.kind == SeamKind.River;
            return River(SeamAt(true, i - 1)) || River(SeamAt(true, i + 1)) ||
                   River(SeamAt(false, j - 1)) || River(SeamAt(false, j + 1));
        }

        // A pocket park is a lot the city did not build on. Rolled against the zone:
        // the houses get most of them (a square is what a suburb is arranged round),
        // downtown almost none (ground there is too dear to leave as grass), the works
        // none at all. Nothing else in the builder needs to know - BuildBlocks reads
        // the flag and lays a lawn where it would have asked for a bake.
        void PlanPocketParks(System.Random rng, int cols, int rows)
        {
            if (pocketParkShare <= 0f) return;
            float Weight(CityZone z)
            {
                switch (z)
                {
                    case CityZone.Downtown: return 0.3f;
                    case CityZone.Commercial: return 0.7f;
                    case CityZone.Waterfront: return 1.2f;
                    case CityZone.Industrial: return 0.15f;
                    default: return 1.5f;
                }
            }

            int made = 0, lots = 0;
            for (int i = 0; i < cols; i++)
                for (int j = 0; j < rows; j++)
                    if (!InSeam(i, j)) lots++;
            // never more than a third of the town, whatever the share and the weights
            // multiply out to: the one-per-city places (the bank, the school, the
            // station) all need a lot to stand in, and a city of lawns has none left
            int ceiling = Mathf.FloorToInt(lots / 3f);

            for (int i = 0; i < cols; i++)
                for (int j = 0; j < rows; j++)
                {
                    if (InSeam(i, j)) continue;
                    if (made >= ceiling) continue;
                    if (rng.NextDouble() >= pocketParkShare * Weight(_zones[i, j])) continue;
                    _pocketParks[i, j] = true;
                    made++;
                }
            if (made > 0) Debug.Log($"[RoadDemo] {made} of {lots} interiors left as pocket parks");
        }

        // ------------------------------------------------------------------ build

        /// <summary>A block interior left as a square: lawn over the whole lot, a
        /// paved cross of paths joining the four pavements, benches and lamps down
        /// them, trees in the quarters between, and a fountain in the middle when
        /// there is room for one. Returns what the O overlay should print for the
        /// lot.</summary>
        string BuildPocketPark(float xMin, float xMax, float zMin, float zMax)
        {
            LoadSeamKit();
            float floor = FloorLevel();
            float w = xMax - xMin, d = zMax - zMin;
            float cx = (xMin + xMax) * 0.5f, cz = (zMin + zMax) * 0.5f;

            var lawn = GameObject.CreatePrimitive(PrimitiveType.Plane);
            lawn.name = "Square";
            Destroy(lawn.GetComponent<Collider>());
            lawn.transform.SetParent(SeamsRoot, false);
            lawn.transform.position = new Vector3(cx, floor - 0.02f, cz);
            lawn.transform.localScale = new Vector3(w / 10f, 1f, d / 10f);
            lawn.GetComponent<MeshRenderer>().sharedMaterial = _lawnMat;

            // the cross of paths: kerb to kerb both ways, so whichever pavement a
            // walker comes off he is on a path rather than on the grass
            BuildBlockFloor(xMin, xMax, cz - 2.5f, cz + 2.5f, null, true);
            BuildBlockFloor(cx - 2.5f, cx + 2.5f, zMin, zMax, null, true);

            // the fountain at the crossing of the paths when the lot can carry one,
            // with a plate apron round it; a court instead on a lot long enough and
            // nothing at all on a lot that is only a strip
            string heart = "";
            // what the middle of the square is already carrying, so no tree is planted
            // in the fountain or on the court
            var busy = Rect.MinMaxRect(0f, 0f, 0f, 0f);
            if (w > 45f && d > 45f && _fountain != null)
            {
                Prop(_fountain, new Vector3(cx, floor, cz), 0f, SeamsRoot).name = "Fountain";
                BuildBlockFloor(cx - 7.5f, cx + 7.5f, cz - 7.5f, cz + 7.5f, null, true);
                busy = Rect.MinMaxRect(cx - 9f, cz - 9f, cx + 9f, cz + 9f);
                heart = ", fountain";
            }
            else if (w > 32f && d > 42f && _courtBasketball != null)
            {
                // The court measures 7.5 across by 15 along and hangs off its pivot's
                // -X side towards local +Z: from a pivot p it covers x in [p.x-7.5, p.x]
                // and z in [p.z, p.z+15]. Stood in the north-east quarter of the square,
                // clear of both arms of the cross - a court laid on the middle would
                // have the path running straight down it.
                var pos = new Vector3(cx + 10.5f, floor + 0.01f, cz + 3f);
                if (pos.x <= xMax - 2f && pos.z + 15f <= zMax - 2f)
                {
                    Instantiate(_courtBasketball, pos, Quaternion.identity, SeamsRoot).name = "Court";
                    busy = Rect.MinMaxRect(pos.x - 9f, pos.z - 1.5f, pos.x + 1.5f, pos.z + 16.5f);
                    heart = ", court";
                }
            }

            // benches turned to the paths, lamps between them, down both arms
            int slot = 0;
            for (float x = xMin + 8f; x < xMax - 6f; x += 12f, slot++)
            {
                if (Mathf.Abs(x - cx) < 8f) continue;
                float side = slot % 2 == 0 ? -1f : 1f;
                var at = new Vector3(x, floor, cz + side * 3.6f);
                if (slot % 3 == 2 && _lamps.Count > 0) Prop(Pick(_lamps), at, side < 0f ? 0f : 180f, SeamsRoot);
                else if (_benches.Count > 0) PlaceBench(at, side < 0f ? 0f : 180f);
            }
            for (float z = zMin + 8f; z < zMax - 6f; z += 12f, slot++)
            {
                if (Mathf.Abs(z - cz) < 8f) continue;
                float side = slot % 2 == 0 ? -1f : 1f;
                var at = new Vector3(cx + side * 3.6f, floor, z);
                if (slot % 3 == 2 && _lamps.Count > 0) Prop(Pick(_lamps), at, side < 0f ? 90f : 270f, SeamsRoot);
                else if (_benches.Count > 0) PlaceBench(at, side < 0f ? 90f : 270f);
            }

            // trees in the four quarters the paths cut the lawn into, never on a path
            // and never in the fountain
            if (_parkTrees.Count > 0)
            {
                int count = Mathf.Max(2, Mathf.RoundToInt(w * d / 130f));
                for (int t = 0; t < count; t++)
                {
                    float x = Random.Range(xMin + 2.5f, xMax - 2.5f);
                    float z = Random.Range(zMin + 2.5f, zMax - 2.5f);
                    if (Mathf.Abs(x - cx) < 4.5f || Mathf.Abs(z - cz) < 4.5f) continue;
                    if (x > busy.xMin && x < busy.xMax && z > busy.yMin && z < busy.yMax) continue;
                    var pool = _bigTrees.Count > 0 && Random.value < 0.4f ? _bigTrees : _parkTrees;
                    Instantiate(Pick(pool), new Vector3(x, floor - 0.02f, z),
                        Quaternion.Euler(0f, Random.value * 360f, 0f), _flora).name = "Square Tree";
                }
            }

            // The plan draws it green off LotInfo.Green rather than off a SeamInfo:
            // this runs inside BuildBlocks, and BuildSeams - which comes after it -
            // clears the seam list before laying the river.
            return "pocket park" + heart;
        }

        /// <summary>The pocket parks' paths, wired into the walk graph: a node at each
        /// of the four path mouths, joined to the two junction corners on that side and
        /// to a node in the middle. Without this a square is a picture - the crowd walks
        /// round it on the pavements and nobody is ever in it. Called from BuildPedGraph
        /// beside BuildSeamPaths, once the corners exist.</summary>
        void BuildPocketParkPaths()
        {
            if (_corners == null) return;
            const int NE = 0, NW = 1, SW = 2, SE = 3;
            const float WalkY = 0.1f;
            int nv = verticalRoadX.Length, nh = horizontalRoadZ.Length;

            foreach (var lot in _lotPlans)
            {
                if (!lot.Green) continue;
                int i = lot.Column, j = lot.Row;
                if (i < 0 || i + 1 >= nv || j < 0 || j + 1 >= nh) continue;
                var r = lot.Interior;
                float cx = r.center.x, cz = r.center.y;

                var heart = new PedNode { Pos = new Vector3(cx, WalkY, cz) };
                void Mouth(Vector3 at, PedNode a, PedNode b)
                {
                    var m = new PedNode { Pos = at };
                    AddPedLink(m, heart, false, false, null);
                    AddPedLink(m, a, false, false, null);
                    AddPedLink(m, b, false, false, null);
                }
                Mouth(new Vector3(cx, WalkY, r.yMin), _corners[i, j, NE], _corners[i + 1, j, NW]);
                Mouth(new Vector3(cx, WalkY, r.yMax), _corners[i, j + 1, SE], _corners[i + 1, j + 1, SW]);
                Mouth(new Vector3(r.xMin, WalkY, cz), _corners[i, j, NE], _corners[i, j + 1, SE]);
                Mouth(new Vector3(r.xMax, WalkY, cz), _corners[i + 1, j, NW], _corners[i + 1, j + 1, SW]);
            }
        }
    }
}
