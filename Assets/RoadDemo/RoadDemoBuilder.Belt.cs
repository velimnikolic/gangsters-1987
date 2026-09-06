using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    // The belt freeway: a dual carriageway at grade that rings the whole city on the
    // island, out in the wild ground between the grid's last sidewalk and the
    // quarters hanging off it. Every road out of town - the streets the suburbs,
    // the port and the airport hang on - crosses it at a junction, and the elevated
    // freeway through the grid comes down off its pillars and Ts into it at both
    // ends: so there is a road from any quarter to any other that never goes through
    // downtown, which is what a highway network is for.
    //
    // It is laid out of the same Highway deck pieces the seam freeway is (twin decks
    // side by side at ground level), on pads of asphalt where it is crossed, and it
    // is IN the lane graph: the belt is four lanes of the same network the streets
    // are, so the traffic wanders onto it, a patrol car routes along it to the next
    // quarter over, and a crew car can take the long way round.
    public partial class RoadDemoBuilder
    {
        [Header("The belt freeway")]
        [Tooltip("A dual carriageway round the whole city at grade, out in the wild strip: every " +
                 "district road crosses it, the elevated freeway Ts into it, and all of it is in the " +
                 "lane graph - the road from the suburb to the port that never goes through downtown. " +
                 "OFF: the town has no freeways at all for now, and the wild strip between the city " +
                 "and its quarters is wild ground and the approach roads across it, nothing else.")]
        public bool beltFreeway = false;
        [Tooltip("Metres a second on the belt.")]
        public float beltSpeed = 17f;

        /// <summary>Metres from the grid's outer sidewalk to the belt's centre line. It is
        /// what the elevated freeway's descent measures - a step past the last junction,
        /// the pitched decks, the moulded ramp, the flat tail and the T (Seams.cs,
        /// BuildHighway: 20 + 44 + 40 + 40 + 22) - so the freeway lands exactly on it.</summary>
        public const float BeltOut = 166f;
        /// <summary>Half the belt kerb to kerb: two Highway deck pieces of 11.4 m side by side.</summary>
        public const float BeltHalf = 11.4f;
        /// <summary>Half a crossing's pad across the belt: where a crossing street stops.
        /// Three cells out from the centre line - the decks and a shoulder of pad.</summary>
        public const float BeltPadHalf = 15f;
        /// <summary>Half a crossing's pad along the belt: the street's own width and the
        /// cell the city lays a zebra in - the same 25 m square the grid's junctions are.</summary>
        const float BeltAlongHalf = StreetHalf + Cell;
        /// <summary>Half a corner pad: the belt turns the corner in a 30 m square.</summary>
        const float BeltCornerHalf = 15f;
        /// <summary>The deck's lanes off each deck's centre line (HighwayTraffic uses the
        /// same), by offset off the belt's own centre: 5.7 +/- 2.6.</summary>
        static readonly float[] BeltLanes = { 3.1f, 8.3f };
        /// <summary>A belt junction has no zebra: the nose stops a little short of the box.</summary>
        const float BeltStopSetback = 2.5f;

        // ------------------------------------------------------------ the slips
        //
        // A crossroads is how you get onto a street. It is not how you get onto a
        // freeway: on a freeway the turning traffic leaves the running lanes before
        // it slows and joins them after it has got up to speed, down a road of its
        // own. The belt was a dual carriageway with four plain crossroads on it and
        // nothing else - you stopped dead on the highway to turn off it - so every
        // crossing gets the four free-flow right turns a real interchange has:
        //
        //        street                       ., gore (on the belt, 80 m out)
        //     ------+------                  /
        //           |                    ---+--- the belt
        //      foot o . . slip . . o gore
        //           |
        //
        // one slip to each quadrant, each one way, each leaving the belt's OUTER lane
        // at a gore and landing on the crossing street's own lane at a foot (and the
        // other way about for the two that join). The left turns and the straight
        // ahead still go through the box; these four never touch it.

        /// <summary>How far up the belt from a crossing its slip roads leave and join
        /// it. Long enough that the ramp lies within thirty degrees of the belt's own
        /// line, which is what a merge looks like - and what keeps the lane graph from
        /// reading the exit and the entrance of one quadrant as a way of turning
        /// round (LaneNet.Prepare drops a movement over 120 degrees).</summary>
        const float BeltGoreOut = 80f;
        /// <summary>How far along the crossing street the slips' other ends stand.</summary>
        const float BeltFootOut = 46f;
        /// <summary>Half a gore's box along the belt, half a foot's along the street.</summary>
        const float BeltGoreHalf = 6f, BeltFootHalf = 6f;
        /// <summary>The belt's outer lane, off its centre line: where a slip leaves it.</summary>
        const float BeltOuterLane = 8.3f;
        /// <summary>The crossing street's own lane, off its centre line.</summary>
        const float BeltStreetLane = 2.5f;
        /// <summary>Metres a second on a slip road, and its half width (one lane).</summary>
        const float BeltSlipSpeed = 12f, BeltSlipHalf = 5f;
        static readonly float[] BeltSlipLanes = { 0f };
        /// <summary>Metres of crossing street a slip road's foot needs past the belt,
        /// before the quarter's own boundary: its box and a stretch of street to merge
        /// on. A quarter nearer in than this keeps a plain crossroads - which is why
        /// the roll stands them a good quarter of a mile out (CityLayout).</summary>
        public const float SlipStreetRoom = BeltFootOut + BeltFootHalf + 20f;

        /// <summary>One crossing of the belt: the street that cuts it, the crossroads
        /// itself, and the ends of its four slip roads - two gores up and down the belt,
        /// two feet out along the street.</summary>
        sealed class BeltCross
        {
            public CityEdge Edge;
            public bool Vertical;        // the belt side runs along Z
            public float U;              // the belt's own centre line (across)
            public float V;              // the crossing street's line (along the belt)
            public RoadNode Node;
            public RoadNode GoreLo, GoreHi;   // on the belt, at V -/+ BeltGoreOut
            public RoadNode FootLo, FootHi;   // on the street, at U -/+ BeltFootOut
            public float Strip;               // how far past the grid the quarter stands
            public bool Slips;
        }

        /// <summary>Where the belt runs on each shore: the coordinate of its centre line
        /// (z for the south and north sides, x for the west and east).</summary>
        readonly Dictionary<CityEdge, float> _beltU = new Dictionary<CityEdge, float>();

        /// <summary>The crossings: a district street cutting the belt, by the shore it
        /// is on and the street's centre line along that shore.</summary>
        readonly List<BeltCross> _beltJunctions = new List<BeltCross>();

        /// <summary>Whether the belt is laid in this city.</summary>
        bool BeltOn => beltFreeway && _beltU.Count == 4;

        /// <summary>The belt's centre line on a shore (the link the freeway ends on).</summary>
        public bool BeltLine(CityEdge edge, out float u) => _beltU.TryGetValue(edge, out u);

        /// <summary>The crossing where a district street on this shore cuts the belt,
        /// or null if it crosses nowhere near one.</summary>
        BeltCross BeltCrossFor(CityEdge edge, float across)
        {
            foreach (var j in _beltJunctions)
                if (j.Edge == edge && Mathf.Abs(j.V - across) < 0.5f) return j;
            return null;
        }

        /// <summary>A point in the belt's own frame on this shore: <paramref name="u"/>
        /// across it, <paramref name="v"/> along it.</summary>
        static Vector3 BeltPoint(bool vertical, float u, float v)
            => vertical ? new Vector3(u, 0f, v) : new Vector3(v, 0f, u);

        // ------------------------------------------------------------------ plan

        /// <summary>Where the belt runs: after Respace, before anything is laid, so the
        /// island, the freeway and the connectors all agree on it.</summary>
        void PlanBelt()
        {
            _beltU.Clear();
            if (!beltFreeway) return;
            if (_coreRegion == null && (verticalRoadX == null || horizontalRoadZ == null || verticalRoadX.Length < 2 || horizontalRoadZ.Length < 2)) return;
            var ex = _coreRegion != null ? (_coreRegion.BeltBounds.xMin, _coreRegion.BeltBounds.xMax) : GridExtent(true);
            var ez = _coreRegion != null ? (_coreRegion.BeltBounds.yMin, _coreRegion.BeltBounds.yMax) : GridExtent(false);
            _beltU[CityEdge.South] = ez.Item1 - BeltOut;
            _beltU[CityEdge.North] = ez.Item2 + BeltOut;
            _beltU[CityEdge.West] = ex.Item1 - BeltOut;
            _beltU[CityEdge.East] = ex.Item2 + BeltOut;
        }

        // ----------------------------------------------------------------- build

        /// <summary>The belt itself: its junction nodes and carriageways into the lane
        /// graph, the decks, the pads, and the ground held flat and bare beneath. After
        /// the city's own graph (it adds to it) and before the districts are welded on
        /// (their connectors run through its junctions).</summary>
        void BuildBelt()
        {
            _beltJunctions.Clear();
            if (!BeltOn || Net == null) return;
            LoadSeamKit();

            var crossings = BeltConnections.Collect(_builtSlots, verticalRoadX, horizontalRoadZ, _coreRegion);

            float xW = _beltU[CityEdge.West], xE = _beltU[CityEdge.East];
            float zS = _beltU[CityEdge.South], zN = _beltU[CityEdge.North];

            // the corner junctions, one node each
            var sw = BeltNode(xW, zS, BeltCornerHalf, BeltCornerHalf);
            var se = BeltNode(xE, zS, BeltCornerHalf, BeltCornerHalf);
            var nw = BeltNode(xW, zN, BeltCornerHalf, BeltCornerHalf);
            var ne = BeltNode(xE, zN, BeltCornerHalf, BeltCornerHalf);
            // road asphalt, not a lot floor: a bend of the belt is carriageway
            foreach (var (x, z) in new[] { (xW, zS), (xE, zS), (xW, zN), (xE, zN) })
                PaveFreewayJunction(x, z, BeltCornerHalf, BeltCornerHalf);

            int roads = 0;
            roads += BuildBeltSide(CityEdge.South, false, zS, sw, se, crossings[CityEdge.South]);
            roads += BuildBeltSide(CityEdge.North, false, zN, nw, ne, crossings[CityEdge.North]);
            roads += BuildBeltSide(CityEdge.West, true, xW, sw, nw, crossings[CityEdge.West]);
            roads += BuildBeltSide(CityEdge.East, true, xE, se, ne, crossings[CityEdge.East]);

            // the connectors of every node the belt touched, now that all its roads are in
            foreach (var n in new[] { sw, se, nw, ne }) Net.Rebuild(n);
            foreach (var j in _beltJunctions)
            {
                Net.Rebuild(j.Node);
                Net.Rebuild(j.GoreLo); Net.Rebuild(j.GoreHi);
                // the feet wait for the district's own street (WeldRoads rebuilds them)
            }
            // and the freeway termini, which the belt has just given their side arms
            foreach (var kv in _highwayEnds) if (kv.Value.Node != null) Net.Rebuild(kv.Value.Node);
            _edges.Clear();
            _edges.AddRange(Net.Edges);

            int slipped = 0;
            foreach (var j in _beltJunctions) if (j.Slips) slipped++;
            Debug.Log($"[RoadDemo] belt freeway: {roads} carriageways round the island, " +
                      $"{_beltJunctions.Count} crossings ({slipped} with slip roads, " +
                      $"{slipped * 4} ramps), {BeltOut:F0} m out from the grid");
        }

        RoadNode BeltNode(float x, float z, float halfX, float halfZ)
        {
            var n = Net.AddNode(x, z, halfX, halfZ, BeltStopSetback);
            n.I = -3; n.J = -(_beltJunctions.Count + 1);
            return n;
        }

        /// <summary>One side of the belt, corner to corner: the junction nodes where the
        /// district streets cross it, the carriageways between them, the decks between
        /// the pads, and the ground beneath. Returns how many carriageways it laid.</summary>
        int BuildBeltSide(CityEdge edge, bool vertical, float u, RoadNode cornerLo, RoadNode cornerHi,
                          List<(float v, float strip)> crossings)
        {
            // the stops along this side, lo to hi: the corner, every crossing, the corner
            var stops = new List<(float v, RoadNode node, float padHalf)>();
            float lo = vertical ? cornerLo.Z : cornerLo.X;
            float hi = vertical ? cornerHi.Z : cornerHi.X;
            stops.Add((lo, cornerLo, BeltCornerHalf));
            crossings.Sort((a, b) => a.v.CompareTo(b.v));
            foreach (var (v, strip) in crossings)
            {
                if (v < lo + BeltCornerHalf + BeltAlongHalf + 5f || v > hi - BeltCornerHalf - BeltAlongHalf - 5f)
                {
                    Debug.LogWarning($"[RoadDemo] belt: a crossing at {v:F0} on the {edge} lands on a corner - skipped");
                    continue;
                }
                // a crossroads at grade: the box is the street's width along the belt and
                // the pad's width across it, so the belt's lanes stop at the street's kerb
                // line and the street's lanes stop at the pad's edge
                var node = vertical ? BeltNode(u, v, BeltPadHalf, StreetHalf) : BeltNode(v, u, StreetHalf, BeltPadHalf);
                _beltJunctions.Add(new BeltCross
                {
                    Edge = edge, Vertical = vertical, U = u, V = v, Node = node, Strip = strip,
                });
                stops.Add((v, node, BeltAlongHalf));
                // the box surfaced as a road, with a zebra across the street that
                // crosses - the belt itself runs through and is not walked over
                if (vertical)
                    PaveFreewayJunction(u, v, BeltPadHalf, BeltAlongHalf,
                                        armHalfZ: StreetKit.StreetHalf, east: true, west: true, kerbs: true);
                else
                    PaveFreewayJunction(v, u, BeltAlongHalf, BeltPadHalf,
                                        armHalfX: StreetKit.StreetHalf, north: true, south: true, kerbs: true);
            }
            stops.Add((hi, cornerHi, BeltCornerHalf));
            stops.Sort((a, b) => a.v.CompareTo(b.v));

            // the seam freeway's terminal T on this shore: its pad is already laid
            // (BuildHighway). It is a JUNCTION of the belt now - the freeway's two decks
            // come down off their pillars and join here, so there is a road from the
            // city's own streets, up a slip road, along the deck and out to any quarter
            // on the island without ever going through downtown.
            if (_highwayEnds.TryGetValue(edge, out var end) && end.Vertical == vertical && end.Node != null &&
                end.Mid > lo + BeltCornerHalf + 20f && end.Mid < hi - BeltCornerHalf - 20f)
            {
                stops.Add((end.Mid, end.Node, 17.5f));
                stops.Sort((a, b) => a.v.CompareTo(b.v));
            }

            // the slip roads: a crossing gets them where there is room on the belt both
            // sides of it for the gores to stand clear of whatever the next stop is -
            // the corner, the freeway's T, another crossing. Where there is not, that
            // crossing keeps the plain crossroads and says so.
            const float SlipRoom = BeltGoreOut + BeltGoreHalf + 8f;
            var withSlips = new List<BeltCross>();
            for (int i = 0; i < stops.Count; i++)
            {
                var cross = BeltCrossFor(edge, stops[i].v);
                if (cross == null || (_coreRegion != null && cross.Strip == 0f)) continue;
                float roomLo = i > 0 ? stops[i].v - stops[i - 1].v - stops[i - 1].padHalf : float.MaxValue;
                float roomHi = i + 1 < stops.Count ? stops[i + 1].v - stops[i].v - stops[i + 1].padHalf : float.MaxValue;
                if (roomLo < SlipRoom || roomHi < SlipRoom)
                {
                    Debug.LogWarning($"[RoadDemo] belt: the crossing at {cross.V:F0} on the {edge} has only " +
                                     $"{Mathf.Min(roomLo, roomHi):F0} m of belt beside it ({SlipRoom:F0} wanted) - " +
                                     "it keeps a plain crossroads with no slip roads.");
                    continue;
                }
                // and room on the STREET, past the belt, for the outer foot to stand
                // between the crossroads and the quarter's own boundary
                if (cross.Strip < BeltOut + SlipStreetRoom)
                {
                    Debug.LogWarning($"[RoadDemo] belt: the quarter on the {edge} at {cross.V:F0} stands " +
                                     $"{cross.Strip:F0} m out, and its road has no room past the belt for a slip " +
                                     $"road's foot ({BeltOut + SlipStreetRoom:F0} m wanted) - plain crossroads.");
                    continue;
                }
                withSlips.Add(cross);
            }
            foreach (var cross in withSlips)
            {
                MakeBeltSlipNodes(cross);
                // the gores break the belt's carriageway but not its decks: a gore has
                // no pad, the road simply runs through it
                stops.Add((cross.V - BeltGoreOut, cross.GoreLo, 0f));
                stops.Add((cross.V + BeltGoreOut, cross.GoreHi, 0f));
            }
            stops.Sort((a, b) => a.v.CompareTo(b.v));

            int laid = 0;
            for (int i = 0; i + 1 < stops.Count; i++)
            {
                var a = stops[i];
                var b = stops[i + 1];
                // the carriageway, box edge to box edge
                float sa = vertical ? a.node.ZMax : a.node.XMax;
                float sb = vertical ? b.node.ZMin : b.node.XMin;
                if (sb - sa < 1f) continue;
                var pa = vertical ? new Vector3(u, 0f, sa) : new Vector3(sa, 0f, u);
                var pb = vertical ? new Vector3(u, 0f, sb) : new Vector3(sb, 0f, u);
                var road = Net.AddRoad(pa, pb, BeltHalf, BeltLanes, beltSpeed, a.node, b.node, vertical);
                road.SurfaceY = GradeY;        // the deck stands a hand over the plain
                road.ParkingA = road.ParkingB = false;
                laid++;

                // the decks, pad edge to pad edge - the freeway's T is one of the stops
                // now, so its pad breaks them like any other junction
                LayBeltDecks(vertical, u, a.v + a.padHalf, b.v - b.padHalf);
            }

            // the slips themselves, now that every node on this side exists
            foreach (var cross in _beltJunctions)
                if (cross.Edge == edge && cross.Slips) laid += BuildBeltSlips(cross);

            // the ground: flat at the plain's level and bare, the hills standing back
            // from it the way they do from the freeway's run-out, nothing growing on it -
            // EXCEPT over a river's channel. Held flat there, the belt filled the river
            // in: it rings the island, so it crosses every river's way out to the sea,
            // and each of those was a dam of grass with a road on top. The channel keeps
            // its bed and the belt goes over it on a bridge.
            float half = BeltHalf + 9f;
            float gLo = lo - BeltCornerHalf - 4f, gHi = hi + BeltCornerHalf + 4f;
            Rect Shoulder(float a, float b) => vertical
                ? Rect.MinMaxRect(u - half, a, u + half, b)
                : Rect.MinMaxRect(a, u - half, b, u + half);
            foreach (var run in ClearOfRivers(gLo, gHi, vertical, RiverClear))
                _reservations.Level(Shoulder(run.lo, run.hi), RoadBed);
            foreach (var cut in RiverCrossings(vertical))
            {
                float cLo = Mathf.Max(gLo, cut.lo), cHi = Mathf.Min(gHi, cut.hi);
                if (cHi - cLo < 1f) continue;
                BeltRiverBridge(vertical, u, cLo, cHi);
            }
            _reservations.NoFlora(Shoulder(gLo, gHi));
            _highwayRuns.Add((vertical, u - BeltHalf, u + BeltHalf, lo - BeltCornerHalf - 20f, hi + BeltCornerHalf + 20f));
            // (the map draws the grid alone, the quarters and the belt with them are
            // off its sheet - the same gap the turf map has for the districts)
            return laid;
        }

        /// <summary>The four ends a crossing's slip roads run between: two gores on the
        /// belt, up and down it, and two feet out along the crossing street.</summary>
        void MakeBeltSlipNodes(BeltCross c)
        {
            bool vert = c.Vertical;
            float u = c.U, v = c.V;
            // on the belt: the whole carriageway wide, a few metres along it
            c.GoreLo = vert ? BeltNode(u, v - BeltGoreOut, BeltHalf, BeltGoreHalf)
                            : BeltNode(v - BeltGoreOut, u, BeltGoreHalf, BeltHalf);
            c.GoreHi = vert ? BeltNode(u, v + BeltGoreOut, BeltHalf, BeltGoreHalf)
                            : BeltNode(v + BeltGoreOut, u, BeltGoreHalf, BeltHalf);
            // on the street: the street's own width, a few metres along it
            c.FootLo = vert ? BeltNode(u - BeltFootOut, v, BeltFootHalf, StreetHalf)
                            : BeltNode(v, u - BeltFootOut, StreetHalf, BeltFootHalf);
            c.FootHi = vert ? BeltNode(u + BeltFootOut, v, BeltFootHalf, StreetHalf)
                            : BeltNode(v, u + BeltFootOut, StreetHalf, BeltFootHalf);
            c.Slips = true;
        }

        /// <summary>The four slip roads of one crossing, each one way, each carrying the
        /// right turn of its quadrant clear of the crossroads: two off the belt onto the
        /// street, two off the street onto the belt.
        ///
        /// Written in the belt's own frame - u across it, v along it - so a side down
        /// either axis reads the same. The one thing that is NOT the same is which way
        /// "right" lies: on a side running north-south the right of the running
        /// direction is +u, on one running east-west it is -u (Cross(up, +X) = -Z), and
        /// that sign is <c>hand</c>. Everything measured across the belt carries it;
        /// everything measured along the belt does not.</summary>
        int BuildBeltSlips(BeltCross c)
        {
            if (Net == null || !c.Slips) return 0;
            bool vert = c.Vertical;
            float hand = vert ? 1f : -1f;
            float u = c.U, v = c.V;
            float gore = BeltGoreOut - BeltGoreHalf;   // the gore's box edge, toward the crossroads
            float foot = BeltFootOut - BeltFootHalf;   // the foot's box edge, toward the belt
            Vector3 P(float across, float along) => BeltPoint(vert, across, along);

            // the feet by which side of the belt they stand on rather than by their
            // coordinate: the +hand one takes the traffic that leaves the belt running
            // with the axis, whichever axis that is
            var footPlus = hand > 0f ? c.FootHi : c.FootLo;
            var footMinus = hand > 0f ? c.FootLo : c.FootHi;

            int laid = 0;
            // off the belt running with the axis, right onto the street (it leaves at the
            // gore BEFORE the crossroads, from the belt's outer lane)
            laid += BeltSlip(vert, c.GoreLo, footPlus,
                             P(u + hand * BeltOuterLane, v - gore),
                             P(u + hand * foot, v - BeltStreetLane));
            // off the belt running against the axis, right onto the street the other way
            laid += BeltSlip(vert, c.GoreHi, footMinus,
                             P(u - hand * BeltOuterLane, v + gore),
                             P(u - hand * foot, v + BeltStreetLane));
            // off the street, right onto the belt running against the axis
            laid += BeltSlip(vert, footMinus, c.GoreLo,
                             P(u - hand * foot, v - BeltStreetLane),
                             P(u - hand * BeltOuterLane, v - gore));
            // off the street, right onto the belt running with it
            laid += BeltSlip(vert, footPlus, c.GoreHi,
                             P(u + hand * foot, v + BeltStreetLane),
                             P(u + hand * BeltOuterLane, v + gore));
            return laid;
        }

        /// <summary>One slip road: a single lane one way in the graph, and the same
        /// Highway deck the belt itself is made of laid along it at grade.</summary>
        int BeltSlip(bool vertical, RoadNode from, RoadNode to, Vector3 a, Vector3 b)
        {
            if (from == null || to == null || (b - a).sqrMagnitude < 4f) return 0;
            var road = Net.AddOneWay(a, b, BeltSlipHalf, BeltSlipLanes, BeltSlipSpeed, from, to, vertical);
            road.SurfaceY = GradeY;
            LayRampDeck(a, GradeY, b, GradeY);
            return 1;
        }

        /// <summary>Where the belt crosses a river's channel it is a BRIDGE. Its decks
        /// already run straight over (LayBeltDecks breaks them only for a pad), and the
        /// ground is left uncarved under them now; what was missing is everything that
        /// says bridge - the soffit under the roadway, the girders across it, a post at
        /// each bank - and it comes out of the same PolygonCity kit the town's own
        /// bridges over the same river are dressed with.</summary>
        void BeltRiverBridge(bool vertical, float mid, float lo, float hi)
        {
            LoadSeamKit();
            // the twin deck is 22.8 m across; to the 5 m beat that is five cells
            const float DeckHalf = 12.5f;
            Vector3 W(float along, float across, float y) => vertical
                ? new Vector3(mid + across, y, along)
                : new Vector3(along, y, mid + across);

            if (_bridgeUnderside != null)
                for (float m = lo; m < hi - 0.1f; m += Cell)
                    for (float x = -DeckHalf; x < DeckHalf - 0.1f; x += Cell)
                    {
                        // the same pivot convention DressBridge uses: the slab covers
                        // local +X / -Z, and a quarter turn puts it on the other corner
                        var pivot = vertical ? W(m + Cell, x, GradeY) : W(m + Cell, x + Cell, GradeY);
                        var rot = vertical ? Quaternion.identity : Quaternion.Euler(0f, 90f, 0f);
                        Instantiate(_bridgeUnderside, pivot, rot, SeamsRoot).name = "Belt soffit";
                    }

            if (_bridgeSupport != null)
            {
                var rot = vertical ? Quaternion.identity : Quaternion.Euler(0f, 90f, 0f);
                for (float m = lo + 7.5f; m < hi - 3f; m += 15f)
                    foreach (float seat in new[] { -6f, 6f })
                        Instantiate(_bridgeSupport, W(m, seat, GradeY - 1.8f), rot, SeamsRoot).name = "Belt girder";
            }
            if (_bridgePillar != null)
                foreach (float bank in new[] { lo, hi })
                    foreach (float side in new[] { -DeckHalf - 1.5f, DeckHalf + 1.5f })
                        Instantiate(_bridgePillar, W(bank, side, GradeY), Quaternion.identity, SeamsRoot).name = "Belt post";
        }

        /// <summary>Twin decks at grade from u0 to u1 along the side, the same pieces and
        /// the same convention as the seam freeway's tails (Seams.cs, LayDeckRow): the
        /// near deck running +u with its pivot edge on the centre line, the far one
        /// turned about, both barriers outward; a row's length absorbed by stretching
        /// every piece a hair.</summary>
        void LayBeltDecks(bool alongZ, float mid, float u0, float u1)
        {
            if (_highwayDeck == null || u1 - u0 < 2f) return;
            const float DeckLen = 20f, DeckHalf = 5.7f;
            int count = Mathf.Max(1, Mathf.RoundToInt((u1 - u0) / DeckLen));
            float len = (u1 - u0) / count;
            for (int k = 0; k < count; k++)
            {
                float u = u0 + k * len;
                GameObject a, b;
                if (alongZ)
                {
                    a = Instantiate(_highwayDeck, new Vector3(mid - DeckHalf + 5f, GradeY, u), Quaternion.identity, SeamsRoot);
                    b = Instantiate(_highwayDeck, new Vector3(mid + DeckHalf - 5f, GradeY, u + len), Quaternion.Euler(0f, 180f, 0f), SeamsRoot);
                }
                else
                {
                    a = Instantiate(_highwayDeck, new Vector3(u, GradeY, mid + DeckHalf - 5f), Quaternion.Euler(0f, 90f, 0f), SeamsRoot);
                    b = Instantiate(_highwayDeck, new Vector3(u + len, GradeY, mid - DeckHalf + 5f), Quaternion.Euler(0f, -90f, 0f), SeamsRoot);
                }
                a.name = b.name = "Belt Deck";
                if (Mathf.Abs(len - DeckLen) > 0.01f)
                    a.transform.localScale = b.transform.localScale = new Vector3(1f, 1f, len / DeckLen);
            }
        }
    }
}
