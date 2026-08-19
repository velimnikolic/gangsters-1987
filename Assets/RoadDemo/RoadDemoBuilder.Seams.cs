using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    public enum SeamKind { River, Park, Wild, Highway }

    /// <summary>
    /// One gap of the grid that is not blocks: what lies between two districts.
    /// A seam takes the gap between road line <see cref="gap"/> and the next
    /// (numbered from the south for a horizontal seam, from the west for a vertical
    /// one), <see cref="width"/> metres kerb to kerb; the boulevards cross it, the
    /// ordinary streets end at it - on a quay over the river, at the lawn's edge in
    /// a park. An elevated highway is the exception: it rides over the whole grid on
    /// pillars, and every road passes beneath it.
    /// </summary>
    [System.Serializable]
    public class Seam
    {
        [Tooltip("Runs north-south between two vertical roads (else east-west between two horizontal ones).")]
        public bool vertical;
        [Tooltip("Which gap: between road line 'gap' and 'gap + 1', counted from the south / west.")]
        public int gap;
        public SeamKind kind = SeamKind.River;
        [Tooltip("Metres from one kerb to the other, snapped to the 5 m cell.")]
        public float width = 90f;
    }

    // The seams: what the city puts between its districts. A river with quays on
    // both banks, the boulevards carried over it on bridges, boats moored below
    // the promenade; a park the streets stop at, with a path down its middle that
    // the crowd walks. Both are laid on the same 5 m lattice as the roads, out of
    // the same packs, and both cut through the plain grid without breaking its
    // rule: every road still runs the width of the map, it is the SEAM that says
    // whether a given road crosses it.
    public partial class RoadDemoBuilder
    {
        // ------------------------------------------------------------------ plan

        /// <summary>The seam in the gap between line <paramref name="gap"/> and the
        /// next of the vertical (X) or horizontal (Z) road lines, or null.</summary>
        Seam SeamAt(bool verticalLines, int gap)
        {
            if (seams == null) return null;
            foreach (var s in seams)
                if (s != null && s.vertical == verticalLines && s.gap == gap) return s;
            return null;
        }

        bool AnySeam(bool verticalLines)
        {
            if (seams == null) return false;
            foreach (var s in seams) if (s != null && s.vertical == verticalLines) return true;
            return false;
        }

        /// <summary>Whether grid cell (i, j) - between vertical roads i, i+1 and
        /// horizontal roads j, j+1 - lies in a seam rather than being a block.</summary>
        bool InSeam(int i, int j) => SeamAt(true, i) != null || SeamAt(false, j) != null;

        /// <summary>Whether road <paramref name="road"/> (a vertical one, or a horizontal
        /// one) actually runs across gap <paramref name="gap"/> of the OTHER axis: it does
        /// unless a seam lies there and the road is an ordinary street - a street ends
        /// on the quay and at the park's edge; a boulevard bridges the river and drives
        /// through the park.</summary>
        bool SegmentOpen(bool verticalRoad, int road, int gap)
        {
            var seam = SeamAt(!verticalRoad, gap);
            if (seam == null || seam.kind == SeamKind.Highway) return true; // under the deck, all roads
            return verticalRoad ? verticalIsBoulevard[road] : horizontalIsBoulevard[road];
        }

        /// <summary>Whether the river lies right beside road <paramref name="road"/> on
        /// the side <paramref name="side"/> (+1 = north / east, -1 = south / west): the
        /// road is an embankment road and that sidewalk its promenade.</summary>
        bool RiverBeside(bool verticalRoad, int road, int side)
        {
            int gap = side > 0 ? road : road - 1;
            if (gap < 0) return false;
            // a vertical road's east side looks onto a vertical seam (a column gap)
            return SeamAt(verticalRoad, gap) is Seam s && s.kind == SeamKind.River;
        }

        /// <summary>An open segment over the river: a bridge.</summary>
        bool IsBridge(bool verticalRoad, int road, int gap)
            => SeamAt(!verticalRoad, gap) is Seam s && s.kind == SeamKind.River &&
               SegmentOpen(verticalRoad, road, gap);

        string SeamStory()
        {
            if (seams == null || seams.Length == 0) return "";
            var parts = new List<string>();
            foreach (var s in seams)
                if (s != null)
                    parts.Add($"{s.kind.ToString().ToLowerInvariant()} in {(s.vertical ? "column" : "row")} gap {s.gap} ({s.width:F0} m)");
            return "; seams: " + string.Join(", ", parts);
        }

        /// <summary>The seam's own strip, bank to bank (the lot rectangle of the gap,
        /// running the whole length of the grid), for the map and the fringe.</summary>
        public readonly struct SeamInfo
        {
            public readonly SeamKind Kind;
            public readonly Rect Area;   // world XZ, bank to bank, grid end to grid end
            public readonly bool Vertical;
            public SeamInfo(SeamKind kind, Rect area, bool vertical) { Kind = kind; Area = area; Vertical = vertical; }
        }

        readonly List<SeamInfo> _seamPlans = new List<SeamInfo>();

        /// <summary>Every seam the demo laid, for the map to draw.</summary>
        public IReadOnlyList<SeamInfo> SeamPlans => _seamPlans;

        // The strip a seam takes: from the near kerb's sidewalk to the far one's, i.e.
        // exactly the interior a block would have had there.
        (float lo, float hi) SeamSpan(Seam s)
        {
            if (s.vertical)
                return (verticalRoadX[s.gap] + VHalf(s.gap) + Sidewalk,
                        verticalRoadX[s.gap + 1] - VHalf(s.gap + 1) - Sidewalk);
            return (horizontalRoadZ[s.gap] + HHalf(s.gap) + Sidewalk,
                    horizontalRoadZ[s.gap + 1] - HHalf(s.gap + 1) - Sidewalk);
        }

        // The grid's own extent along an axis, outer sidewalk to outer sidewalk.
        (float lo, float hi) GridExtent(bool x)
        {
            if (x)
                return (verticalRoadX[0] - VHalf(0) - Sidewalk,
                        verticalRoadX[verticalRoadX.Length - 1] + VHalf(verticalRoadX.Length - 1) + Sidewalk);
            return (horizontalRoadZ[0] - HHalf(0) - Sidewalk,
                    horizontalRoadZ[horizontalRoadZ.Length - 1] + HHalf(horizontalRoadZ.Length - 1) + Sidewalk);
        }

        /// <summary>The stretches of [from, to] along one axis that no river takes -
        /// the sand fringe is laid in these, and the water shows between them.</summary>
        IEnumerable<(float, float)> SeamFreeRuns(float from, float to, bool alongX)
        {
            var cuts = new List<(float lo, float hi)>();
            if (seams != null)
                foreach (var s in seams)
                    if (s != null && s.kind == SeamKind.River && s.vertical == alongX)
                        cuts.Add(SeamSpan(s));
            cuts.Sort((a, b) => a.lo.CompareTo(b.lo));
            float at = from;
            foreach (var c in cuts)
            {
                if (c.hi <= at || c.lo >= to) continue;
                if (c.lo > at) yield return (at, c.lo);
                at = Mathf.Max(at, c.hi);
            }
            if (at < to) yield return (at, to);
        }

        // ------------------------------------------------------------- the crowd

        // The counts on the inspector were tuned on the four-by-three grid of twelve
        // blocks; a city of twice the blocks gets twice the traffic and twice the
        // crowd, or the same hundred cars would thin to nothing over it.
        void ScaleLifeToCity()
        {
            // a scene that wants the counts above exactly as they stand - one block on
            // its own (BlockDemo.unity), or the city with the life pinned for a
            // frame-time reading - turns the scaling off
            if (!scaleLifeToCity) return;
            int lots = 0;
            for (int i = 0; i + 1 < verticalRoadX.Length; i++)
                for (int j = 0; j + 1 < horizontalRoadZ.Length; j++)
                    if (!InSeam(i, j)) lots++;
            float factor = Mathf.Max(0.5f, lots / 12f);
            if (Mathf.Approximately(factor, 1f)) return;
            // capped: a city of ninety blocks does not need a thousand walkers, each an
            // animated humanoid - the camera only ever holds a corner of it at a time.
            // The caps are on the inspector because they are the one knob that trades
            // the town's life for the frame: halve them and the frame halves with them,
            // which is also how you find out whether the frame is the LIFE at all.
            carCount = Mathf.RoundToInt(carCount * Mathf.Min(factor, maxCarScale));
            pedestrianCount = Mathf.RoundToInt(pedestrianCount * Mathf.Min(factor, maxCrowdScale));
            policeCarCount = Mathf.Max(policeCarCount, Mathf.RoundToInt(policeCarCount * Mathf.Sqrt(factor)));
            policeOfficerCount = Mathf.Max(policeOfficerCount, Mathf.RoundToInt(policeOfficerCount * Mathf.Sqrt(factor)));
            Debug.Log($"[RoadDemo] {lots} blocks: {carCount} cars, {pedestrianCount} pedestrians, " +
                      $"{policeCarCount} patrol cars, {policeOfficerCount} officers");
        }

        // ------------------------------------------------------------------- kit

        const string SeamCityEnv = "Assets/Synty/PolygonCity/Prefabs/Environments/";
        const string SeamPalmEnv = "Assets/Synty/PolygonPalmCity/Prefabs/Environment/";
        const string SeamPalmProps = "Assets/Synty/PolygonPalmCity/Prefabs/Props/";
        const string SeamPalmVeh = "Assets/Synty/PolygonPalmCity/Prefabs/Vehicles/";
        const string SeamGenericEnv = "Assets/Synty/PolygonGeneric/Prefabs/Environment/";

        // The quay wall (PolygonCity WaterEdge, measured): pivot on the land edge, the
        // piece runs 5 m along its local +X and its wall body reaches ~1.5 m toward
        // local -Z, which must face the water; the coping stands 0.43 m proud. The
        // water tile is 30 x 30 m covering local -X / +Z from its pivot. The bridge
        // kit: Bridge_Edge is a 5 m walkway with a parapet, 5.2 m wide toward local
        // +X (outward), running along local -Z; Underside a 5 x 5 soffit slab under
        // the roadway (local +X / -Z); Support a 23.4 m girder along local X.
        GameObject _quayStraight, _quayStraightWorn, _quayCorner, _quayPipe;
        GameObject _waterTile, _bridgeEdge, _bridgeUnderside, _bridgeSupport, _bridgePillar, _bridgeWall;
        GameObject _pierLamp, _fountain, _courtBasketball, _courtTennis, _pavilion;
        GameObject _highwayDeck, _highwayPillar, _chainFence;
        readonly List<GameObject> _boats = new List<GameObject>();
        readonly List<GameObject> _parkTrees = new List<GameObject>();
        readonly List<GameObject> _bigTrees = new List<GameObject>();
        Material _lawnMat;
        bool _seamKitLoaded;

        void LoadSeamKit()
        {
            if (_seamKitLoaded) return;
            _seamKitLoaded = true;
            _quayStraight = Load(SeamCityEnv + "SM_Env_WaterEdge_Straight_03.prefab");
            _quayStraightWorn = Load(SeamCityEnv + "SM_Env_WaterEdge_Straight_02.prefab");
            _quayCorner = Load(SeamCityEnv + "SM_Env_WaterEdge_Corner_01.prefab");
            _quayPipe = Load(SeamCityEnv + "SM_Env_WaterEdge_Pipe_01.prefab");
            // the water the PalmCity demo scene stands its own city in: PNB_Core's
            // plane on the pack's water shader (shore foam off the depth buffer, so
            // it laps at the quays by itself), not the City pack's ocean tile
            _waterTile = Load("Assets/Synty/PNB_Core/Prefabs/SM_Env_Water_Plane_01.prefab") ??
                         Load(SeamCityEnv + "SM_Env_Ocean_Tile_01.prefab");
            _bridgeEdge = Load(SeamCityEnv + "SM_Env_Bridge_Edge_01.prefab");
            _bridgeUnderside = Load(SeamCityEnv + "SM_Env_Bridge_Underside_01.prefab");
            _bridgeSupport = Load(SeamCityEnv + "SM_Env_Bridge_Support_01.prefab");
            _bridgePillar = Load(SeamCityEnv + "SM_Env_Bridge_Pillar_01.prefab");
            _bridgeWall = Load(SeamCityEnv + "SM_Env_Bridge_Wall_01.prefab");
            _pierLamp = Load(SeamPalmProps + "SM_Prop_Pier_Lamp_01.prefab");
            _fountain = Load(SeamPalmProps + "SM_Prop_Fountain_01.prefab");
            _pavilion = Load(SeamPalmProps + "SM_Prop_Pavilion_01.prefab");
            _courtBasketball = Load(SeamPalmEnv + "SM_Env_Court_BasketBall_01.prefab");
            _courtTennis = Load(SeamPalmEnv + "SM_Env_Court_Tennis_01.prefab");
            _highwayDeck = Load(SeamPalmEnv + "SM_Env_Road_Highway_01.prefab");
            _highwayPillar = Load(SeamPalmEnv + "SM_Env_Road_Highway_Pillar_01.prefab");
            _chainFence = Load(SeamCityEnv + "SM_Env_Fence_01.prefab");
            foreach (var name in new[] { "SM_Veh_Power_Boat_01", "SM_Veh_RIB_Boat_01", "SM_Veh_Party_Boat_01", "SM_Veh_Sailboat_01" })
            {
                var boat = Load(SeamPalmVeh + name + ".prefab");
                if (boat != null) _boats.Add(boat);
            }
            foreach (var name in new[] { "SM_Env_Tree_01", "SM_Env_Tree_02", "SM_Env_Tree_03" })
            {
                var tree = Load(SeamCityEnv + name + ".prefab");
                if (tree != null) _parkTrees.Add(tree);
            }
            foreach (var name in new[] { "SM_Gen_Env_Tree_01", "SM_Gen_Env_Tree_02", "SM_Gen_Env_Tree_03", "SM_Gen_Env_Tree_Pine_01" })
            {
                var tree = Load(SeamGenericEnv + name + ".prefab");
                if (tree != null) _bigTrees.Add(tree);
            }
            // the park's lawn: the pack's triplanar grass, a fresh instance - the fringe
            // takes its own and turns it to sand
            _lawnMat = LoadMaterial(PalmGround);
            if (_lawnMat == null)
            {
                _lawnMat = new Material(Shader.Find("Universal Render Pipeline/Lit"))
                    { color = new Color(0.36f, 0.55f, 0.27f) };
                _lawnMat.SetFloat("_Smoothness", 0.05f);
            }
            if (_quayStraight == null || _waterTile == null || _bridgeEdge == null)
                Debug.LogWarning("[RoadDemo] the PolygonCity water/bridge kit is missing - the river " +
                                 "will be laid without quays or bridge walkways.");
        }

        // ---------------------------------------------------------------- levels

        /// <summary>The water's surface, below the quay's coping - the PalmCity demo
        /// scene's own level under its streets, so the pack's shore foam is tuned to
        /// exactly this depth of wall showing above the water.</summary>
        const float WaterY = -2.65f;

        /// <summary>How far past the grid a river's quays run before the natural bank
        /// takes over; the river itself is a channel through the island's ground out
        /// to the sea (RoadDemoBuilder.Island.cs), the highway rides on out over it.</summary>
        const float QuayReach = 10f;

        // ------------------------------------------------------------------ build

        Transform _seamsRoot;

        /// <summary>The scene root everything the seams lay hangs off; made on first
        /// use, since a bridge is dressed before BuildSeams runs.</summary>
        Transform SeamsRoot => _seamsRoot != null ? _seamsRoot : (_seamsRoot = new GameObject("Seams").transform);

        void BuildSeams()
        {
            _seamPlans.Clear();
            if (seams == null || seams.Length == 0) return;
            LoadSeamKit();
            foreach (var s in seams)
            {
                if (s == null) continue;
                if (s.gap < 0 || s.gap + 1 >= (s.vertical ? verticalRoadX.Length : horizontalRoadZ.Length))
                {
                    Debug.LogWarning($"[RoadDemo] seam gap {s.gap} is off the grid - skipped");
                    continue;
                }
                var span = SeamSpan(s);
                var ext = GridExtent(!s.vertical);
                var area = s.vertical
                    ? Rect.MinMaxRect(span.lo, ext.lo, span.hi, ext.hi)
                    : Rect.MinMaxRect(ext.lo, span.lo, ext.hi, span.hi);
                _seamPlans.Add(new SeamInfo(s.kind, area, s.vertical));
                if (s.kind == SeamKind.River) BuildRiver(s);
                else if (s.kind == SeamKind.Park || s.kind == SeamKind.Wild) BuildPark(s);
                else BuildHighway(s);
            }
        }

        // ------------------------------------------------------------------ river

        // Everything about the river is written for one running along X (an east-west
        // river between two rows) and turned by U()/V(): u runs along the river, v
        // across it, low bank first. A vertical river swaps the two.
        void BuildRiver(Seam s)
        {
            bool alongX = !s.vertical;
            var (bankLo, bankHi) = SeamSpan(s);
            var ext = GridExtent(alongX);
            // the quays run a step past the grid, then the island's own banks take over;
            // the water is the sea's, which fills the channel the island leaves for it
            float uMin = ext.lo - QuayReach, uMax = ext.hi + QuayReach;

            Vector3 W(float u, float v, float y) => alongX ? new Vector3(u, y, v) : new Vector3(v, y, u);

            // where the bridges land: the boulevards of the OTHER axis crossing this gap
            var bridges = new List<(float centre, float half)>();
            int roads = alongX ? verticalRoadX.Length : horizontalRoadZ.Length;
            for (int r = 0; r < roads; r++)
            {
                if (!SegmentOpen(alongX, r, s.gap)) continue;
                float c = alongX ? verticalRoadX[r] : horizontalRoadZ[r];
                float half = alongX ? VHalf(r) : HHalf(r);
                bridges.Add((c, half + Cell)); // carriageway plus the walkways
            }
            bool UnderBridge(float u, float margin)
            {
                foreach (var b in bridges)
                    if (u > b.centre - b.half - margin && u < b.centre + b.half + margin) return true;
                return false;
            }

            // the quays: one wall piece per 5 m along both banks, the coping at street
            // level and the wall dropping into the water; under a bridge the kit's
            // abutment wall stands in for it, below the deck
            foreach (bool low in new[] { true, false })
            {
                float bank = low ? bankLo : bankHi;
                // the piece's local -Z must face the water: +Z off the low bank means the
                // low bank's wall points local -Z toward +v, the high bank's toward -v
                float yaw;
                if (alongX) yaw = low ? 180f : 0f;
                else yaw = low ? -90f : 90f;
                for (float u = uMin; u < uMax - 0.1f; u += Cell)
                {
                    bool bridged = UnderBridge(u + Cell * 0.5f, 0f);
                    var piece = bridged ? _bridgeWall
                        : (Random.value < 0.18f && _quayStraightWorn != null ? _quayStraightWorn : _quayStraight);
                    if (piece == null) continue;
                    // pivot at the piece's local origin: the +X end of its run for the
                    // yaws that turn +X back along -u
                    float pu = (alongX ? (low ? u + Cell : u) : (low ? u : u + Cell));
                    Instantiate(piece, W(pu, bank, 0f), Quaternion.Euler(0f, yaw, 0f), SeamsRoot).name =
                        bridged ? "Abutment" : "Quay";
                    // an outfall pipe in the wall now and then, just above the water
                    if (!bridged && _quayPipe != null && Random.value < 0.06f)
                    {
                        float dir = low ? 1f : -1f; // toward the water
                        var pos = W(u + Cell * 0.5f, bank + dir * 0.9f, WaterY + 0.9f);
                        Instantiate(_quayPipe, pos, Quaternion.Euler(0f, yaw, 0f), SeamsRoot).name = "Outfall";
                    }
                }
            }

            // the promenade: pier lamps at the coping and benches turned to the water,
            // along the grid only (the town's own frontage), never under a bridge
            float gridLo = ext.lo, gridHi = ext.hi;
            foreach (bool low in new[] { true, false })
            {
                float bank = low ? bankLo : bankHi;
                float inward = low ? -1f : 1f;      // from the bank back onto the land
                float faceWater = alongX ? (low ? 0f : 180f) : (low ? 90f : 270f);
                int slot = 0;
                for (float u = gridLo + 7f; u < gridHi - 5f; u += 9f, slot++)
                {
                    if (UnderBridge(u, 4f)) continue;
                    // the sidewalk cell behind the quay is 5 m deep: lamps a metre in
                    // from the coping, benches (the demo's own, so the sitters fit them)
                    // turned to the water, a bin here and there
                    if (slot % 2 == 0 && _pierLamp != null)
                        Prop(_pierLamp, W(u, bank + inward * 1.1f, 0.1f), faceWater, SeamsRoot);
                    else if (Random.value < 0.6f && _benches.Count > 0)
                        PlaceBench(W(u, bank + inward * 2.6f, 0.1f), faceWater); // a seat with a view
                    else if (Random.value < 0.5f && _bins.Count > 0)
                        Prop(Pick(_bins), W(u, bank + inward * 1.2f, 0.1f), faceWater + 180f, SeamsRoot);
                }
            }

            // boats moored along the quays, a few metres off the wall, bows along the
            // river; none under a bridge, none out in the fringe
            if (_boats.Count > 0)
            {
                foreach (bool low in new[] { true, false })
                {
                    float bank = low ? bankLo : bankHi;
                    float off = low ? 1f : -1f; // into the water
                    for (float u = gridLo + 20f; u < gridHi - 20f; u += Random.Range(34f, 62f))
                    {
                        if (UnderBridge(u, 22f)) continue;
                        var boat = Pick(_boats);
                        float yaw = (alongX ? 90f : 0f) + (Random.value < 0.5f ? 0f : 180f) + Random.Range(-4f, 4f);
                        var pos = W(u, bank + off * Random.Range(3.2f, 4.6f), WaterY);
                        Instantiate(boat, pos, Quaternion.Euler(0f, yaw, 0f), SeamsRoot).name = "Boat";
                    }
                }
            }
        }

        /// <summary>Water over a rectangle: the water plane, measured once and scaled to
        /// the rectangle - one renderer however big the river or the bay.</summary>
        void WaterTiles(float x0, float x1, float z0, float z1)
        {
            if (_waterTile == null || x1 - x0 < 1f || z1 - z0 < 1f) return;
            var b = PrefabBoundsOf(_waterTile);
            float sx = Mathf.Max(0.01f, b.size.x), sz = Mathf.Max(0.01f, b.size.z);
            var water = Instantiate(_waterTile, Vector3.zero, Quaternion.identity, SeamsRoot);
            water.name = "Water";
            water.transform.localScale = new Vector3((x1 - x0) / sx, 1f, (z1 - z0) / sz);
            // the plane's own centre may sit off its pivot: place by the centre
            water.transform.position = new Vector3(
                (x0 + x1) * 0.5f - b.center.x * (x1 - x0) / sx,
                WaterY - b.center.y,
                (z0 + z1) * 0.5f - b.center.z * (z1 - z0) / sz);
        }

        /// <summary>The bridge kit over an open segment across the river: the walkways
        /// with their parapets in place of the sidewalks, the soffit under the deck,
        /// girders across it over the water, lamps along the parapets.</summary>
        void DressBridge(bool verticalRoad, int road, RoadNode a, RoadNode b)
        {
            LoadSeamKit();
            var seam = SeamAt(!verticalRoad, verticalRoad ? a.J : a.I);
            if (seam == null) return;
            var (bankLo, bankHi) = SeamSpan(seam);
            float centre = verticalRoad ? verticalRoadX[road] : horizontalRoadZ[road];
            float half = verticalRoad ? VHalf(road) : HHalf(road);
            float from = verticalRoad ? a.ZMax : a.XMax;   // along the road
            float to = verticalRoad ? b.ZMin : b.XMin;
            Vector3 W(float along, float across, float y) => verticalRoad
                ? new Vector3(centre + across, y, along)
                : new Vector3(along, y, centre + across);

            // walkways: Bridge_Edge runs along local -Z, 5.2 m wide toward local +X, which
            // must point off the deck. Vertical road: west side +X -> -X (yaw 180), pivot at
            // the near end; east side yaw 0, pivot at the far end. Horizontal: north side
            // +X -> +Z (yaw -90), south side yaw 90.
            // over the water only, bank to bank (the banks are on the 5 m beat: a
            // seam's width is); the pavement at each end of the segment is the
            // embankment's own, and the junction's corner slab stands there
            for (float m = bankLo; m < bankHi - 0.1f; m += Cell)
            {
                if (_bridgeEdge != null)
                {
                    if (verticalRoad)
                    {
                        Instantiate(_bridgeEdge, W(m, -half, 0f), Quaternion.Euler(0f, 180f, 0f), SeamsRoot).name = "Walkway";
                        Instantiate(_bridgeEdge, W(m + Cell, half, 0f), Quaternion.identity, SeamsRoot).name = "Walkway";
                    }
                    else
                    {
                        // yaw -90: local +X -> +Z (north, off the deck), local -Z -> +X: piece covers [px, px + 5]
                        Instantiate(_bridgeEdge, W(m, half, 0f), Quaternion.Euler(0f, -90f, 0f), SeamsRoot).name = "Walkway";
                        // yaw 90: local +X -> -Z (south), local -Z -> -X: covers [px - 5, px]
                        Instantiate(_bridgeEdge, W(m + Cell, -half, 0f), Quaternion.Euler(0f, 90f, 0f), SeamsRoot).name = "Walkway";
                    }
                }
                // the soffit under every roadway cell over the water (local +X / -Z from
                // the pivot: pivot at the -X / +Z corner of the cell; a quarter turn puts
                // it at the +X / +Z corner)
                if (_bridgeUnderside != null)
                    for (float x = -half; x < half - 0.1f; x += Cell)
                    {
                        var pivot = verticalRoad ? W(m + Cell, x, 0f) : W(m + Cell, x + Cell, 0f);
                        var rot = verticalRoad ? Quaternion.identity : Quaternion.Euler(0f, 90f, 0f);
                        Instantiate(_bridgeUnderside, pivot, rot, SeamsRoot).name = "Soffit";
                    }
            }

            // girders across the deck every 15 m over the water: one spans a street,
            // a boulevard takes two side by side (each 23.4 m long along local X)
            if (_bridgeSupport != null)
            {
                float[] seats = half > 12f ? new[] { -8f, 8f } : new[] { 0f };
                var rot = verticalRoad ? Quaternion.identity : Quaternion.Euler(0f, 90f, 0f);
                for (float m = bankLo + 7.5f; m < bankHi - 3f; m += 15f)
                    foreach (float seat in seats)
                        Instantiate(_bridgeSupport, W(m, seat, -1.8f), rot, SeamsRoot).name = "Girder";
            }

            // the parapet's end posts where the walkways meet the banks, and lamps down
            // the parapets, turned in over the roadway
            float outer = half + 4.6f;
            if (_bridgePillar != null)
                foreach (float bank in new[] { bankLo, bankHi })
                    foreach (float side in new[] { -outer, outer })
                        Instantiate(_bridgePillar, W(bank, side, 0f), Quaternion.identity, SeamsRoot).name = "Post";
            if (_pierLamp != null)
                for (float m = bankLo + 10f; m < bankHi - 6f; m += 16f)
                    foreach (float side in new[] { -outer + 0.4f, outer - 0.4f })
                    {
                        float faceIn = verticalRoad ? (side < 0f ? 90f : 270f) : (side < 0f ? 0f : 180f);
                        Prop(_pierLamp, W(m, side, 0.15f), faceIn, SeamsRoot);
                    }
        }

        // ------------------------------------------------------------------- park

        // A park is the strip between its two edge roads, cut by whatever boulevards
        // drive through it (and by the river, where the two cross): the lawn, a
        // concrete path down the middle with a cross path where every street ends at
        // the edge, trees, benches and lamps along the path, a fountain at the
        // heart of the longest reach and a court or two.
        void BuildPark(Seam s)
        {
            // a WILD seam is the same strip left to itself: no path, no bench, no
            // fountain - woods, rock and scrub, the island's wilderness reaching in
            bool wild = s.kind == SeamKind.Wild;
            if (wild) LoadWildKit();
            bool alongZ = s.vertical; // the park runs along Z between two vertical roads
            var (edgeLo, edgeHi) = SeamSpan(s);          // across the park
            var ext = GridExtent(!alongZ);               // along the park (grid ends)
            Vector3 W(float u, float v, float y) => alongZ ? new Vector3(v, y, u) : new Vector3(u, y, v);
            float mid = (edgeLo + edgeHi) * 0.5f;
            float breadth = edgeHi - edgeLo;

            // the reaches: [ext.lo, ext.hi] minus every crossing road's corridor (kerb
            // to kerb plus its sidewalks) and minus the river where it crosses
            var cuts = new List<(float lo, float hi)>();
            int roads = alongZ ? horizontalRoadZ.Length : verticalRoadX.Length;
            var pathEnds = new List<float>();     // where a closed street's cross path runs
            for (int r = 0; r < roads; r++)
            {
                float c = alongZ ? horizontalRoadZ[r] : verticalRoadX[r];
                float h = alongZ ? HHalf(r) : VHalf(r);
                if (SegmentOpen(!alongZ, r, s.gap)) cuts.Add((c - h - Sidewalk, c + h + Sidewalk));
                else pathEnds.Add(c);
            }
            foreach (var other in seams)
                if (other != null && other != s && other.vertical != s.vertical)
                {
                    var span = SeamSpan(other);
                    // the river takes its banks' sidewalks too - the quays run through
                    cuts.Add((span.lo - Sidewalk, span.hi + Sidewalk));
                }
            cuts.Sort((x, y) => x.lo.CompareTo(y.lo));
            var reaches = new List<(float lo, float hi)>();
            float at = ext.lo;
            foreach (var c in cuts)
            {
                if (c.hi <= at) continue;
                if (c.lo > at + 1f) reaches.Add((at, c.lo));
                at = Mathf.Max(at, c.hi);
            }
            if (at < ext.hi - 1f) reaches.Add((at, ext.hi));

            float floor = FloorLevel();
            int longest = -1;
            for (int k = 0; k < reaches.Count; k++)
                if (longest < 0 || reaches[k].hi - reaches[k].lo > reaches[longest].hi - reaches[longest].lo) longest = k;

            for (int k = 0; k < reaches.Count; k++)
            {
                var (lo, hi) = reaches[k];
                float len = hi - lo;

                // the lawn: one plane, a hair under the sidewalk's top
                var lawn = GameObject.CreatePrimitive(PrimitiveType.Plane);
                lawn.name = "Lawn";
                Destroy(lawn.GetComponent<Collider>());
                lawn.transform.SetParent(SeamsRoot, false);
                lawn.transform.position = W((lo + hi) * 0.5f, mid, floor - 0.02f);
                lawn.transform.localScale = alongZ
                    ? new Vector3(breadth / 10f, 1f, len / 10f)
                    : new Vector3(len / 10f, 1f, breadth / 10f);
                lawn.GetComponent<MeshRenderer>().sharedMaterial = _lawnMat;

                if (wild)
                {
                    DressWildStrip(alongZ, lo, hi, edgeLo, edgeHi, floor);
                    continue;
                }

                // the spine path, 5 m of concrete plates down the middle of the reach,
                // and a cross path at every street that ends on the park's edge
                if (alongZ) BuildBlockFloor(mid - 2.5f, mid + 2.5f, lo, hi, null, true);
                else BuildBlockFloor(lo, hi, mid - 2.5f, mid + 2.5f, null, true);
                foreach (float c in pathEnds)
                {
                    if (c < lo + 3f || c > hi - 3f) continue;
                    if (alongZ) BuildBlockFloor(edgeLo, edgeHi, c - 2.5f, c + 2.5f, null, true);
                    else BuildBlockFloor(c - 2.5f, c + 2.5f, edgeLo, edgeHi, null, true);
                }

                // benches and lamps down the spine, alternating sides, turned to the path
                int slot = 0;
                for (float u = lo + 8f; u < hi - 6f; u += 11f, slot++)
                {
                    bool nearCross = false;
                    foreach (float c in pathEnds) if (Mathf.Abs(c - u) < 5f) nearCross = true;
                    if (nearCross) continue;
                    float side = slot % 2 == 0 ? -1f : 1f;
                    float facePath = alongZ ? (side < 0f ? 90f : 270f) : (side < 0f ? 0f : 180f);
                    if (slot % 3 == 2 && _lamps.Count > 0)
                        Prop(Pick(_lamps), W(u, mid + side * 3.4f, floor), facePath, SeamsRoot);
                    else if (_benches.Count > 0)
                        PlaceBench(W(u, mid + side * 3.6f, floor), facePath);
                }

                // the fountain at the heart of the longest reach; a court in the others
                // when they are long enough for one, off to one side of the spine
                if (k == longest && _fountain != null && len > 30f)
                {
                    float u = (lo + hi) * 0.5f;
                    Prop(_fountain, W(u, mid, floor), 0f, SeamsRoot).name = "Fountain";
                    // a plate apron round it, over the lawn
                    if (alongZ) BuildBlockFloor(mid - 7.5f, mid + 7.5f, u - 7.5f, u + 7.5f, null, true);
                    else BuildBlockFloor(u - 7.5f, u + 7.5f, mid - 7.5f, mid + 7.5f, null, true);
                }
                else if (len > 40f && breadth > 40f && (k % 2 == 0 ? _courtBasketball : _courtTennis) is GameObject court)
                {
                    // the court is 7.5 x 15 (or 12.5) along local +Z from its pivot's -X side;
                    // stood beside the spine, its length along the park
                    float u = lo + len * 0.5f;
                    float v = mid + (k % 2 == 0 ? 1f : -1f) * (2.5f + 4f + 3.75f);
                    var rot = alongZ ? Quaternion.identity : Quaternion.Euler(0f, 90f, 0f);
                    // pivot: local x in [-7.5, 0], z in [0, 15] -> centre at (-3.75, 7.5)
                    var pos = alongZ ? W(u - 7.5f, v + 3.75f, floor + 0.01f) : W(u - 7.5f, v - 3.75f, floor + 0.01f);
                    Instantiate(court, pos, rot, SeamsRoot).name = "Court";
                }

                // trees: the city's own along the edges, the tall ones in the middle
                // ground, none on the paths, none crowding the fountain
                int count = Mathf.RoundToInt(len * breadth / 110f);
                for (int t = 0; t < count; t++)
                {
                    float u = Random.Range(lo + 3f, hi - 3f);
                    float v = Random.Range(edgeLo + 2.5f, edgeHi - 2.5f);
                    if (Mathf.Abs(v - mid) < 4.5f) continue;                            // the spine and its benches
                    bool onCross = false;
                    foreach (float c in pathEnds) if (Mathf.Abs(c - u) < 4f) onCross = true;
                    if (onCross) continue;
                    if (k == longest && Mathf.Abs(u - (lo + hi) * 0.5f) < 10f && Mathf.Abs(v - mid) < 10f) continue;
                    bool edge = Mathf.Abs(v - mid) > breadth * 0.5f - 9f;
                    var pool = edge || _bigTrees.Count == 0 ? _parkTrees : (Random.value < 0.55f ? _bigTrees : _parkTrees);
                    if (pool.Count == 0) continue;
                    Instantiate(Pick(pool), W(u, v, floor - 0.02f),
                        Quaternion.Euler(0f, Random.value * 360f, 0f), _flora).name = "Park Tree";
                }
            }
        }

        // ---------------------------------------------------------------- highway

        /// <summary>The elevated deck's road surface above the street.</summary>
        const float DeckY = 9f;

        /// <summary>The deck's level once it has come down off the pillars, out of
        /// town: just proud of the island's plain, an embankment rather than a bridge.</summary>
        const float GradeY = 0.45f;

        /// <summary>Metres the ramp takes to come down from the deck to the grade -
        /// about a four-degree descent, which is what a freeway takes.</summary>
        const float RampRun = 130f;

        // An elevated freeway down the strip: two decks side by side on pillars, one
        // for each direction, running out past the grid both ways; every road of the
        // grid passes under it. The ground beneath is what such ground is - worn
        // asphalt behind chain-link, a fence along both sides of every reach between
        // the crossing roads. Past the last junction the deck ramps DOWN: out of town
        // it runs at grade over the island's ground - the hills held off it - and on
        // out over the water to the mainland off-stage. Its own traffic runs on it
        // (HighwayTraffic): cars that never leave the deck, following the ramps,
        // wrapping round out of sight in the fringe.
        void BuildHighway(Seam s)
        {
            bool alongZ = s.vertical;
            var (edgeLo, edgeHi) = SeamSpan(s);
            var ext = GridExtent(!alongZ);
            // out over the island and the water beyond it, to the mainland off-stage
            float uMin = ext.lo - (alongZ ? islandSouth : islandWest) - 160f;
            float uMax = ext.hi + (alongZ ? islandNorth : islandEast) + 160f;
            float mid = (edgeLo + edgeHi) * 0.5f;
            Vector3 W(float u, float v, float y) => alongZ ? new Vector3(v, y, u) : new Vector3(u, y, v);

            // the deck's height along its run: full height over the grid and a step
            // past it, then down the ramp, then at grade all the way off-stage
            float loTop = ext.lo - 25f, hiTop = ext.hi + 25f;
            float DeckHeight(float u)
            {
                if (u >= loTop && u <= hiTop) return DeckY;
                float off = u < loTop ? loTop - u : u - hiTop;
                return Mathf.Lerp(DeckY, GradeY, Mathf.Clamp01(off / RampRun));
            }

            // the ground where the ramps land: held dead flat and bare, so each ramp's
            // foot meets a level shoulder. The rest of the grade-level run needs no
            // rectangle - the island's own hills part around the freeway's corridor
            // (IslandHeight fades them out across it) and the shore takes over at the
            // coast, the deck flying the last reach out over the beach and the water.
            float shoulder = (edgeHi - edgeLo) * 0.5f + 9f;
            float rampLo = loTop - RampRun - 40f, rampHi = hiTop + RampRun + 40f;
            {
                var r = alongZ ? Rect.MinMaxRect(mid - shoulder, rampLo, mid + shoulder, ext.lo)
                               : Rect.MinMaxRect(rampLo, mid - shoulder, ext.lo, mid + shoulder);
                _reservations.Level(r, 0f);
                _reservations.NoFlora(r);
                r = alongZ ? Rect.MinMaxRect(mid - shoulder, ext.hi, mid + shoulder, rampHi)
                           : Rect.MinMaxRect(ext.hi, mid - shoulder, rampHi, mid + shoulder);
                _reservations.Level(r, 0f);
                _reservations.NoFlora(r);
            }

            // the ground under the deck, reach by reach between the crossing roads
            int roads = alongZ ? horizontalRoadZ.Length : verticalRoadX.Length;
            for (int r = 0; r + 1 < roads; r++)
            {
                float lo = (alongZ ? horizontalRoadZ[r] + HHalf(r) : verticalRoadX[r] + VHalf(r)) + Sidewalk;
                float hi = (alongZ ? horizontalRoadZ[r + 1] - HHalf(r + 1) : verticalRoadX[r + 1] - VHalf(r + 1)) - Sidewalk;
                if (hi - lo < Cell) continue;
                // the river crossing under the highway is water, not asphalt
                bool water = false;
                foreach (var other in seams)
                    if (other != null && other != s && other.kind == SeamKind.River && other.vertical != s.vertical)
                    {
                        var span = SeamSpan(other);
                        if (span.lo < hi && span.hi > lo) water = true;
                    }
                if (water) continue;
                if (alongZ) BuildBlockFloor(edgeLo, edgeHi, lo, hi, null, false);
                else BuildBlockFloor(lo, hi, edgeLo, edgeHi, null, false);

                // chain-link down both long sides of the reach (the piece runs 5 m
                // along local +X from its pivot), a gap left where nothing meets it
                if (_chainFence != null)
                    foreach (float v in new[] { edgeLo + 0.3f, edgeHi - 0.3f })
                        for (float u = lo + 2.5f; u + Cell <= hi - 2.5f + 0.1f; u += Cell)
                        {
                            var rot = alongZ ? Quaternion.Euler(0f, -90f, 0f) : Quaternion.identity; // +X -> +Z, or +X
                            Prop(_chainFence, W(u, v, FloorLevel()), rot.eulerAngles.y, SeamsRoot).name = "Fence";
                        }
            }

            // the decks: Road_Highway_01 runs 20 m along local +Z and 11.4 m across
            // local X, from x = -10.7 to +0.7 - the pivot rides its +X edge. Two decks
            // meet pivot-edge to pivot-edge down the middle of the strip: the near
            // one running +u (yaw 0, pivot at u), the far one turned about (yaw 180,
            // pivot at u + 20) so both carry their outer barrier outward.
            const float DeckLen = 20f, DeckHalf = 5.7f;
            if (_highwayDeck != null)
            {
                for (float u = uMin; u < uMax - 0.1f; u += DeckLen)
                {
                    // each piece is a straight chord of the height profile: both of its
                    // ends sit ON the profile, so neighbours always meet, and on the
                    // ramp the pieces pitch nose-down toward the grade
                    float h0 = DeckHeight(u), h1 = DeckHeight(u + DeckLen);
                    float pitch = Mathf.Atan2(h0 - h1, DeckLen) * Mathf.Rad2Deg;
                    if (alongZ)
                    {
                        Instantiate(_highwayDeck, new Vector3(mid - DeckHalf + 5f, h0, u), Quaternion.Euler(pitch, 0f, 0f), SeamsRoot).name = "Deck";
                        Instantiate(_highwayDeck, new Vector3(mid + DeckHalf - 5f, h1, u + DeckLen), Quaternion.Euler(-pitch, 180f, 0f), SeamsRoot).name = "Deck";
                    }
                    else
                    {
                        // yaw 90: local +Z -> +X (along u), local +X -> -Z: the -10.7..0.7 spread lands
                        // at z in [pz - 0.7, pz + 10.7]; yaw -90: local +Z -> -X, +X -> +Z
                        Instantiate(_highwayDeck, new Vector3(u, h0, mid + DeckHalf - 5f), Quaternion.Euler(pitch, 90f, 0f), SeamsRoot).name = "Deck";
                        Instantiate(_highwayDeck, new Vector3(u + DeckLen, h1, mid - DeckHalf + 5f), Quaternion.Euler(-pitch, -90f, 0f), SeamsRoot).name = "Deck";
                    }
                }
            }
            // a pier under the middle of the twin deck every 20 m, its T-head across
            // the strip; the pillar hangs 16 m below its pivot, so its foot is buried.
            // None once the ramp is down near the grade: an embankment needs no piers.
            if (_highwayPillar != null)
            {
                var rot = alongZ ? Quaternion.identity : Quaternion.Euler(0f, 90f, 0f);
                for (float u = uMin + DeckLen * 0.5f; u < uMax; u += DeckLen)
                {
                    if (DeckHeight(u) < 3.5f) continue;
                    // never in a road: a pier standing in a carriageway is a wreck waiting
                    bool inRoad = false;
                    for (int r = 0; r < roads; r++)
                    {
                        float c = alongZ ? horizontalRoadZ[r] : verticalRoadX[r];
                        float h = alongZ ? HHalf(r) : VHalf(r);
                        if (Mathf.Abs(u - c) < h + 2f) inRoad = true;
                    }
                    if (inRoad) continue;
                    Prop(_highwayPillar, W(u, mid, DeckHeight(u)), rot.eulerAngles.y, SeamsRoot).name = "Pier";
                }
            }

            // the through traffic, riding the same height profile down the ramps
            if (_carPrefabs.Count > 0)
            {
                var traffic = SeamsRoot.gameObject.AddComponent<HighwayTraffic>();
                var lanes = new List<HighwayTraffic.Lane>();
                // two lanes a deck, 2.6 m either side of each deck's centre line
                foreach (float side in new[] { -1f, 1f })
                    foreach (float lane in new[] { -2.6f, 2.6f })
                        lanes.Add(new HighwayTraffic.Lane
                        {
                            Across = mid + side * DeckHalf + lane,
                            // right-hand traffic: heading +Z the right side is +X, heading +X it is -Z
                            Direction = alongZ ? (side < 0f ? -1f : 1f) : (side < 0f ? 1f : -1f),
                        });
                int cars = Mathf.Max(6, Mathf.RoundToInt((uMax - uMin) / 45f));
                traffic.Init(alongZ, uMin, uMax, 0.02f, lanes, cars, _carPrefabs, _cars,
                    _pedPrefabs, _sitLoopClip, CrowdLayer, DeckHeight);
                _highwayTraffic.Add(traffic);
            }
        }

        readonly List<HighwayTraffic> _highwayTraffic = new List<HighwayTraffic>();

        /// <summary>A wild reach: woods and scrub over the lawn, thicker in the middle,
        /// a few rocks and a fallen log; nothing a stride from the edge roads.</summary>
        void DressWildStrip(bool alongZ, float lo, float hi, float edgeLo, float edgeHi, float floor)
        {
            Vector3 W(float u, float v, float y) => alongZ ? new Vector3(v, y, u) : new Vector3(u, y, v);
            float len = hi - lo, breadth = edgeHi - edgeLo;
            int count = Mathf.RoundToInt(len * breadth / 45f);
            for (int t = 0; t < count; t++)
            {
                float u = Random.Range(lo + 3f, hi - 3f);
                float v = Random.Range(edgeLo + 3f, edgeHi - 3f);
                float roll = Random.value;
                GameObject prefab;
                float scale = 1f;
                if (roll < 0.42f) { prefab = _wildTrees.Count > 0 ? Pick(_wildTrees) : null; scale = Random.Range(0.85f, 1.25f); }
                else if (roll < 0.55f) { prefab = _wildPines.Count > 0 ? Pick(_wildPines) : null; scale = Random.Range(0.85f, 1.2f); }
                else if (roll < 0.62f) { prefab = _wildDead.Count > 0 ? Pick(_wildDead) : null; }
                else if (roll < 0.72f) { prefab = _wildRocks.Count > 0 ? Pick(_wildRocks) : null; scale = Random.Range(0.6f, 1.4f); }
                else if (roll < 0.95f) { prefab = _wildBushes.Count > 0 ? Pick(_wildBushes) : null; scale = Random.Range(0.8f, 1.3f); }
                else { prefab = _wildLogs.Count > 0 ? Pick(_wildLogs) : null; }
                if (prefab == null) continue;
                var go = Instantiate(prefab, W(u, v, floor - 0.04f), Quaternion.Euler(0f, Random.value * 360f, 0f), _flora);
                go.transform.localScale = Vector3.one * scale;
                go.name = "Wild " + prefab.name;
            }
        }

        // ------------------------------------------------------------- the paths

        // The park's spine as pavement the crowd can walk: a node where every cross
        // path meets the spine, linked to the corners of the street that ends there
        // and to the next node along; a boulevard through the park is simply
        // stepped over (an ungated link - a park road, not a highway). The river
        // adds nothing: the quays are the embankment streets' own sidewalks.
        void BuildSeamPaths()
        {
            if (seams == null || _corners == null) return;
            const int NE = 0, NW = 1, SW = 2, SE = 3;
            const float WalkY = 0.1f;
            foreach (var s in seams)
            {
                if (s == null || s.kind != SeamKind.Park) continue;
                if (s.gap < 0 || s.gap + 1 >= (s.vertical ? verticalRoadX.Length : horizontalRoadZ.Length)) continue;
                var (edgeLo, edgeHi) = SeamSpan(s);
                float mid = (edgeLo + edgeHi) * 0.5f;
                int g = s.gap;
                int roads = s.vertical ? horizontalRoadZ.Length : verticalRoadX.Length;
                PedNode last = null;
                float lastAt = 0f;
                for (int r = 0; r < roads; r++)
                {
                    if (SegmentOpen(!s.vertical, r, g)) continue; // a boulevard: no cross path
                    float c = s.vertical ? horizontalRoadZ[r] : verticalRoadX[r];
                    var node = new PedNode { Pos = s.vertical ? new Vector3(mid, WalkY, c) : new Vector3(c, WalkY, mid) };
                    // the ends of the cross path: the corners of the two junctions the
                    // street stops at, on the park side of each
                    if (s.vertical)
                    {
                        AddPedLink(node, _corners[g, r, NE], false, false, null);
                        AddPedLink(node, _corners[g, r, SE], false, false, null);
                        AddPedLink(node, _corners[g + 1, r, NW], false, false, null);
                        AddPedLink(node, _corners[g + 1, r, SW], false, false, null);
                    }
                    else
                    {
                        AddPedLink(node, _corners[r, g, NE], false, false, null);
                        AddPedLink(node, _corners[r, g, NW], false, false, null);
                        AddPedLink(node, _corners[r, g + 1, SE], false, false, null);
                        AddPedLink(node, _corners[r, g + 1, SW], false, false, null);
                    }
                    // along the spine to the previous node - unless the river lies between
                    if (last != null && !RiverBetween(s, lastAt, c))
                        AddPedLink(last, node, false, false, null);
                    last = node;
                    lastAt = c;
                }
            }
        }

        bool RiverBetween(Seam park, float a, float b)
        {
            foreach (var other in seams)
            {
                if (other == null || other == park || other.kind != SeamKind.River || other.vertical == park.vertical) continue;
                var span = SeamSpan(other);
                float lo = Mathf.Min(a, b), hi = Mathf.Max(a, b);
                if (span.lo < hi && span.hi > lo) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// The freeway's own cars: they ride the deck's lanes end to end at a steady
    /// clip and wrap round in the fringe, out of sight - no junctions, no lights,
    /// no lane graph, which is what a through road is. Each lane keeps one speed,
    /// so nobody overtakes into anybody.
    /// </summary>
    public class HighwayTraffic : MonoBehaviour
    {
        public struct Lane
        {
            public float Across;    // world offset across the strip
            public float Direction; // +1 along the strip's axis, -1 back
        }

        struct Car
        {
            public Transform Tf;
            public int Lane;
            public float U;
            public float Speed;
        }

        /// <summary>Metres a second the slowest lane keeps up.</summary>
        const float HighwayPace = 17f;

        bool _alongZ;
        float _uMin, _uMax, _y;
        List<Lane> _lanes;
        System.Func<float, float> _deckAt;
        readonly List<Car> _cars = new List<Car>();

        public void Init(bool alongZ, float uMin, float uMax, float y, List<Lane> lanes, int count,
            List<GameObject> prefabs, Transform parent,
            IList<GameObject> people = null, AnimationClip sitLoop = null, int peopleLayer = -1,
            System.Func<float, float> deckAt = null)
        {
            _alongZ = alongZ;
            _uMin = uMin;
            _uMax = uMax;
            _y = y;
            _lanes = lanes;
            _deckAt = deckAt;
            float length = uMax - uMin;
            for (int k = 0; k < count; k++)
            {
                var prefab = prefabs[Random.Range(0, prefabs.Count)];
                var go = Instantiate(prefab, parent);
                go.name = "Highway " + prefab.name;
                foreach (var rb in go.GetComponentsInChildren<Rigidbody>()) Destroy(rb);
                foreach (var col in go.GetComponentsInChildren<Collider>()) Destroy(col);
                // a driver, now and then a passenger - nobody drives an empty car
                CarOccupant.Crew(go.transform, people, sitLoop, passengerChance: 0.3f, layer: peopleLayer);
                int lane = k % lanes.Count;
                int inLane = k / lanes.Count;
                int perLane = Mathf.Max(1, Mathf.CeilToInt((float)count / lanes.Count));
                // spread evenly down the lane, each lane staggered so none run abreast
                float u = uMin + ((inLane + 0.5f + 0.37f * lane) * (length / perLane)) % length;
                _cars.Add(new Car
                {
                    Tf = go.transform,
                    Lane = lane,
                    U = u,
                    Speed = HighwayPace + 1.3f * (lane % 2) + 0.9f * (lane / 2),
                });
                Place(_cars.Count - 1);
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            for (int k = 0; k < _cars.Count; k++)
            {
                var c = _cars[k];
                var lane = _lanes[c.Lane];
                c.U += lane.Direction * c.Speed * dt;
                if (c.U > _uMax) c.U -= _uMax - _uMin;
                else if (c.U < _uMin) c.U += _uMax - _uMin;
                _cars[k] = c;
                Place(k);
            }
        }

        void Place(int k)
        {
            var c = _cars[k];
            var lane = _lanes[c.Lane];
            // the deck's height under the car, and its slope for the nose to follow
            float deck = _deckAt != null ? _deckAt(c.U) : 0f;
            float pitch = 0f;
            if (_deckAt != null)
            {
                float slope = (_deckAt(c.U + 3f) - _deckAt(c.U - 3f)) / 6f;
                pitch = -Mathf.Atan(slope * lane.Direction) * Mathf.Rad2Deg;
            }
            Vector3 pos = _alongZ ? new Vector3(lane.Across, _y + deck, c.U) : new Vector3(c.U, _y + deck, lane.Across);
            float yaw = _alongZ ? (lane.Direction > 0f ? 0f : 180f) : (lane.Direction > 0f ? 90f : 270f);
            c.Tf.SetPositionAndRotation(pos, Quaternion.Euler(pitch, yaw, 0f));
        }

        /// <summary>The cars, for the headlights.</summary>
        public IEnumerable<Transform> Cars()
        {
            foreach (var c in _cars) yield return c.Tf;
        }
    }
}
