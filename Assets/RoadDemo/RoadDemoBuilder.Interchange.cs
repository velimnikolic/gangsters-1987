using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    // How you get ON to the elevated freeway, and off it again - which is the whole
    // difference between a road and a wall with cars running along the top of it.
    // Before this the deck rode over the grid on its pillars from one end of the map
    // to the other and came down only at its two termini, a hundred and sixty metres
    // outside the last junction: from anywhere in the city there was no way onto it,
    // and the corridor it ran down was a fenced strip of cracked asphalt nobody could
    // reach.
    //
    // Three things live here, and they are the three parts of a real interchange:
    //
    //  * the FRONTAGE ROADS - a service carriageway down each side of the corridor,
    //    at grade, against its outer edge so the bounding street's pavement serves as
    //    its own. They are ordinary two-way roads OF THE LANE GRAPH and they meet
    //    EVERY street of the grid that passes under the deck at a real crossroads, so
    //    the corridor belongs to the local network instead of dividing it. The city's
    //    own traffic drives them; a patrol car can route down one.
    //  * the SLIP ROADS - a diamond at one cross street: four one-way ramps, an exit
    //    and an entrance to each deck, climbing between the frontage road and the
    //    freeway. Their surface climbs with them (Carriageway.SurfaceAt), which is
    //    what the lane graph could not do before.
    //  * the DECK ITSELF in the lane graph, as TWO ONE-WAY carriageways - which is
    //    what lets a ramp join one of them without a car crossing the other to reach
    //    it - so a slip road leads somewhere and the freeway is a road the city can
    //    use: on at the belt or at the interchange, over the grid, off at either.
    //
    // and one repair that belongs with them: a junction on the freeway network used
    // to be laid with BuildBlockFloor - a LOT floor, whose cells go down at random
    // rotations and are then cracked, patched and manholed, because a lot is what it
    // is for. On a carriageway it read as exactly what the eye reported: an empty
    // square in the middle of the road, at every belt crossing, at all four belt
    // corners and on both freeway terminal pads. They are surfaced as roads now.
    public partial class RoadDemoBuilder
    {
        // ------------------------------------------------------------------ sizes
        //
        // Across the corridor from its centre line, on either side:
        //
        //    0      11.4   17.1        30            45   50   55      70
        //    |  deck   | wire | ramp gore | ramp foot   | verge| frontage | verge |
        //    |<- 22.8 twin ->|            |<- 11.4 wide ->|     |<- 10 ->|
        //
        // which is why the highway seam is a hundred and forty metres wide and not
        // thirty. A freeway you can get on and off is not the deck: it is the deck,
        // a ramp each side of it, a service road each side of THAT, and enough room
        // between the service road's crossroads and the bounding street's that two
        // junctions are not sitting in each other's laps. That is about a hundred and
        // fifty metres of city, and it always was.

        /// <summary>The frontage road's centre line, off the corridor's own. It sits
        /// against the outer edge of the seam, so the bounding street's pavement is its
        /// pavement too - which is what a service road down the side of a freeway has.</summary>
        public const float FrontageOff = 50f;
        /// <summary>Half a frontage road: two lanes and no parking - StreetKit's plain
        /// yard carriageway, which is exactly what this is.</summary>
        const float FrontageHalf = StreetKit.RoadHalf;
        static readonly float[] FrontageLanes = { 2.5f };
        /// <summary>Half the ground under the deck that stays fenced asphalt: the twin
        /// deck is 22.8 m across, so this leaves a shoulder either side of it and the
        /// wire stands between that and the ramps.</summary>
        const float UnderHalf = 20f;
        /// <summary>Each deck's own centre line, off the corridor's: the twin deck is
        /// two 11.4 m carriageways meeting on the corridor's centre.</summary>
        const float DeckOff = 5.7f;
        /// <summary>Two lanes on a one-way deck, off its own centre line.</summary>
        static readonly float[] DeckLanes = { -2.85f, 2.85f };
        /// <summary>A slip road's line where it meets the cross street, and where it
        /// meets the deck - it tapers between the two as it climbs. The gore line is a
        /// deck's width outboard of the freeway, so the ramp runs ALONGSIDE it there
        /// rather than through it.</summary>
        const float RampFootOff = 30f, RampGoreOff = 17.1f;
        /// <summary>How far along the corridor a slip road runs: enough to climb the
        /// nine metres at about one in seventeen, which is a ramp and not a wall.</summary>
        const float RampRun = 160f;
        /// <summary>A slip road meets the cross street no closer than this to it, even
        /// where the street itself is narrow.</summary>
        const float RampFoot = 12f;
        static readonly float[] RampLanes = { 0f };
        /// <summary>Metres a second on the freeway, its slip roads and its service roads.</summary>
        const float FreewaySpeed = 22f, RampSpeed = 11f, FrontageSpeed = 12f;

        // ------------------------------------------------------------------ plan

        /// <summary>An elevated freeway's ground works: planned while the seam is laid
        /// (BuildHighway, which is what knows its height profile) and turned into lanes
        /// when the city's graph is built. The nodes are made by hand rather than by
        /// LaneNet.AddNode because the graph does not exist yet at that point -
        /// BuildGraph adopts them, and chains the grid's own streets through them.</summary>
        sealed class FreewayPlan
        {
            public bool AlongZ;                 // the corridor's axis
            public float Mid;                   // its centre line, across
            public float SLink, NLink;          // the belt's line at each terminus
            public System.Func<float, float> Height;
            public RoadNode SEnd, NEnd;         // the terminal T on the belt

            /// <summary>Where a frontage road crosses a street of the grid: which side
            /// of the corridor (-1 west/south, +1 east/north), the street's centre line
            /// along the corridor, and the crossroads there.</summary>
            public readonly List<(int side, float u, RoadNode node)> Gates = new List<(int, float, RoadNode)>();

            public bool HasRamps;
            /// <summary>The cross street the diamond hangs off, and how wide it is.</summary>
            public float RampU, RampHalf;
            /// <summary>Where the four ramps meet the cross street, one each side.</summary>
            public RoadNode FootW, FootE;
            /// <summary>The gores on the decks: an exit and an entrance to each. The
            /// deck running with +u is on the +side, so its exit leaves BEFORE the cross
            /// street and its entrance joins after; the one running back is the mirror
            /// of that, which is why the two are not the same node.</summary>
            public RoadNode ExitE, EntryE, ExitW, EntryW;
        }

        readonly List<FreewayPlan> _freeways = new List<FreewayPlan>();

        /// <summary>Nodes standing in the MIDDLE of a grid segment - the crossroads a
        /// freeway corridor puts in a street that runs under its deck. Keyed by the
        /// segment BuildGraph is about to lay: whether the road is a vertical one, its
        /// index, and the gap of the other axis it crosses. In order from the A end.</summary>
        readonly Dictionary<(bool vertical, int road, int gap), List<RoadNode>> _midSegment
            = new Dictionary<(bool, int, int), List<RoadNode>>();

        List<RoadNode> MidSegment(bool verticalRoad, int road, int gap)
            => _midSegment.TryGetValue((verticalRoad, road, gap), out var list) ? list : null;

        /// <summary>The frontage roads and the interchange of one elevated freeway.
        /// Nothing goes into the lane graph here - it does not exist yet - but every
        /// node the graph will need is made and filed against the grid segment it
        /// stands in.</summary>
        FreewayPlan PlanInterchange(Seam s, float mid, float sLink, float nLink, System.Func<float, float> height)
        {
            bool alongZ = s.vertical;
            var plan = new FreewayPlan
            {
                AlongZ = alongZ, Mid = mid, SLink = sLink, NLink = nLink, Height = height,
            };

            // the termini: one box each, spanning the corridor, where the freeway meets
            // the belt. The belt reads these when it lays its own side (Belt.cs) - it
            // was already breaking its decks for the pad; now it stops at a junction.
            plan.SEnd = FreewayNode(alongZ, mid, sLink, 17.5f, 12.5f);
            plan.NEnd = FreewayNode(alongZ, mid, nLink, 17.5f, 12.5f);

            plan.HasRamps = PickInterchange(alongZ, out plan.RampU, out plan.RampHalf);
            if (!plan.HasRamps)
                Debug.LogWarning("[RoadDemo] freeway: no cross street of the grid has " + RampRun +
                                 " m of room either side of it, so this corridor gets its frontage roads " +
                                 "but no slip roads - there is no way onto the deck except at the termini.");

            // a crossroads where each frontage road meets each street of the grid that
            // passes under the deck, and at the interchange the two ramp terminals too
            int roads = alongZ ? horizontalRoadZ.Length : verticalRoadX.Length;
            for (int r = 0; r < roads; r++)
            {
                float u = alongZ ? horizontalRoadZ[r] : verticalRoadX[r];
                float half = alongZ ? HHalf(r) : VHalf(r);
                bool interchange = plan.HasRamps && Mathf.Abs(u - plan.RampU) < 0.5f;

                var west = FreewayNode(alongZ, mid - FrontageOff, u, FrontageHalf, half);
                var east = FreewayNode(alongZ, mid + FrontageOff, u, FrontageHalf, half);
                plan.Gates.Add((-1, u, west));
                plan.Gates.Add((+1, u, east));

                // the street through the corridor, west to east: its own junction, the
                // frontage crossroads, the ramp terminals inboard of them, and out again
                var chain = new List<RoadNode> { west };
                if (interchange)
                {
                    plan.FootW = FreewayNode(alongZ, mid - RampFootOff, u, FrontageHalf, half);
                    plan.FootE = FreewayNode(alongZ, mid + RampFootOff, u, FrontageHalf, half);
                    chain.Add(plan.FootW);
                    chain.Add(plan.FootE);
                }
                chain.Add(east);
                if (alongZ) _midSegment[(false, r, s.gap)] = chain;   // a horizontal street crossing a column gap
                else _midSegment[(true, r, s.gap)] = chain;
            }

            if (plan.HasRamps)
            {
                // the gores. The +side deck runs with +u: it is left BEFORE the cross
                // street and joined after it. The -side deck runs back, so it is the
                // other way about - which is what makes this a diamond and not two
                // ramps pointing the wrong way.
                plan.ExitE = FreewayNode(alongZ, mid + DeckOff * 2f, plan.RampU - RampRun, 8f, 10f);
                plan.EntryE = FreewayNode(alongZ, mid + DeckOff * 2f, plan.RampU + RampRun, 8f, 10f);
                plan.ExitW = FreewayNode(alongZ, mid - DeckOff * 2f, plan.RampU + RampRun, 8f, 10f);
                plan.EntryW = FreewayNode(alongZ, mid - DeckOff * 2f, plan.RampU - RampRun, 8f, 10f);
            }
            _freeways.Add(plan);
            return plan;
        }

        /// <summary>A node in the corridor's own frame: (across, along) rather than
        /// (x, z), so a freeway down either axis reads the same.</summary>
        static RoadNode FreewayNode(bool alongZ, float across, float along, float halfAcross, float halfAlong)
        {
            float x = alongZ ? across : along, z = alongZ ? along : across;
            float hx = alongZ ? halfAcross : halfAlong, hz = alongZ ? halfAlong : halfAcross;
            return new RoadNode
            {
                I = -4, J = -1,
                X = x, Z = z, XMin = x - hx, XMax = x + hx, ZMin = z - hz, ZMax = z + hz,
                StopSetback = 3f,
            };
        }

        /// <summary>Which street the diamond hangs off: the boulevard nearest the middle
        /// of the run, so that what comes off the freeway lands on a road that can take
        /// it - and far enough from either end for the ramps to have their length.</summary>
        bool PickInterchange(bool alongZ, out float u, out float half)
        {
            u = 0f; half = StreetHalf;
            var lines = alongZ ? horizontalRoadZ : verticalRoadX;
            var blvd = alongZ ? horizontalIsBoulevard : verticalIsBoulevard;
            var ext = GridExtent(!alongZ);
            float middle = (ext.lo + ext.hi) * 0.5f;
            int best = -1;
            float bestD = float.MaxValue;
            for (int pass = 0; pass < 2 && best < 0; pass++)
                for (int r = 0; r < lines.Length; r++)
                {
                    if (pass == 0 && !blvd[r]) continue;          // a boulevard for choice
                    if (lines[r] - RampRun < ext.lo + 10f || lines[r] + RampRun > ext.hi - 10f) continue;
                    float d = Mathf.Abs(lines[r] - middle);
                    if (d < bestD) { bestD = d; best = r; }
                }
            if (best < 0) return false;
            u = lines[best];
            half = alongZ ? HHalf(best) : VHalf(best);
            return true;
        }

        // ----------------------------------------------------------------- lanes

        /// <summary>The grid's own segment from a to b - laid as one carriageway, or,
        /// where a freeway corridor crosses it, as a chain of them through the
        /// crossroads standing in it. That is the whole of what the corridor costs the
        /// grid: the street still runs from one junction to the next, it just has two
        /// or four more junctions on the way.</summary>
        void LaneSegment(LaneNet net, bool verticalRoad, int road, int gap, RoadNode a, RoadNode b,
                         float centre, float half, float[] lanes, float limit, float median)
        {
            var extra = MidSegment(verticalRoad, road, gap);
            var chain = new List<RoadNode> { a };
            if (extra != null) chain.AddRange(extra);
            chain.Add(b);
            for (int k = 0; k + 1 < chain.Count; k++)
            {
                RoadNode p = chain[k], q = chain[k + 1];
                Vector3 from = verticalRoad ? new Vector3(centre, 0f, p.ZMax) : new Vector3(p.XMax, 0f, centre);
                Vector3 to = verticalRoad ? new Vector3(centre, 0f, q.ZMin) : new Vector3(q.XMin, 0f, centre);
                if ((to - from).sqrMagnitude < 1f) continue;
                net.AddRoad(from, to, half, lanes, limit, p, q, verticalRoad, median);
            }
        }

        /// <summary>Every freeway's own lanes, once the grid's are in and before the
        /// graph is finished.</summary>
        void BuildFreewayLanes(LaneNet net)
        {
            foreach (var plan in _freeways)
            {
                foreach (var g in plan.Gates) net.Nodes.Add(g.node);
                net.Nodes.Add(plan.SEnd);
                net.Nodes.Add(plan.NEnd);
                if (plan.HasRamps)
                {
                    net.Nodes.Add(plan.FootW); net.Nodes.Add(plan.FootE);
                    net.Nodes.Add(plan.ExitE); net.Nodes.Add(plan.EntryE);
                    net.Nodes.Add(plan.ExitW); net.Nodes.Add(plan.EntryW);
                }
                BuildFrontageLanes(net, plan);
                // the decks only go into the graph if their termini lead somewhere. With
                // no belt the freeway ends on a turnaround pad that is not in the graph
                // at all, and a car that drove up there would sit on it for ever - so
                // the deck keeps its own traffic instead (Seams.cs).
                if (!BeltOn) continue;
                BuildDeckLanes(net, plan);
                if (plan.HasRamps) BuildSlipLanes(net, plan);
            }
            if (_freeways.Count == 0) return;
            int gates = 0, ramps = 0;
            foreach (var p in _freeways) { gates += p.Gates.Count; if (p.HasRamps && BeltOn) ramps += 4; }
            Debug.Log($"[RoadDemo] freeway: {_freeways.Count} corridor(s), {gates} frontage crossroads, " +
                      $"{ramps} slip roads" + (BeltOn ? " - decks in the lane graph" : " - decks NOT in the graph (no belt)"));
        }

        Vector3 Corridor(FreewayPlan p, float across, float along)
            => p.AlongZ ? new Vector3(across, 0f, along) : new Vector3(along, 0f, across);

        /// <summary>How far along the corridor a node's box reaches, lo and hi.</summary>
        static (float lo, float hi) Along(FreewayPlan p, RoadNode n)
            => p.AlongZ ? (n.ZMin, n.ZMax) : (n.XMin, n.XMax);

        /// <summary>The edge of a node's box on the side a point lies.</summary>
        static float EdgeToward(FreewayPlan p, RoadNode n, float target)
        {
            var (lo, hi) = Along(p, n);
            return target > (lo + hi) * 0.5f ? hi : lo;
        }

        /// <summary>A frontage road: crossroads to crossroads, all the way down each
        /// side of the corridor.</summary>
        void BuildFrontageLanes(LaneNet net, FreewayPlan plan)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                var line = new List<(float u, RoadNode node)>();
                foreach (var g in plan.Gates) if (g.side == side) line.Add((g.u, g.node));
                line.Sort((a, b) => a.u.CompareTo(b.u));
                float across = plan.Mid + side * FrontageOff;
                for (int k = 0; k + 1 < line.Count; k++)
                {
                    float a = EdgeToward(plan, line[k].node, line[k + 1].u);
                    float b = EdgeToward(plan, line[k + 1].node, line[k].u);
                    if (b - a < 1f) continue;
                    net.AddRoad(Corridor(plan, across, a), Corridor(plan, across, b),
                                FrontageHalf, FrontageLanes, FrontageSpeed,
                                line[k].node, line[k + 1].node, plan.AlongZ);
                }
            }
        }

        /// <summary>The deck: two one-way carriageways, each following the run's own
        /// height profile - up off the belt, over the grid on the pillars, down the far
        /// side - and broken at its gores, which is where a car leaves it or joins it.</summary>
        void BuildDeckLanes(LaneNet net, FreewayPlan plan)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                float across = plan.Mid + side * DeckOff;
                bool forward = side > 0;
                var stops = new List<(float u, RoadNode node)>
                {
                    (plan.SLink, plan.SEnd),
                    (plan.NLink, plan.NEnd),
                };
                if (plan.HasRamps)
                {
                    if (forward) { stops.Add((plan.RampU - RampRun, plan.ExitE)); stops.Add((plan.RampU + RampRun, plan.EntryE)); }
                    else { stops.Add((plan.RampU - RampRun, plan.EntryW)); stops.Add((plan.RampU + RampRun, plan.ExitW)); }
                }
                stops.Sort((a, b) => a.u.CompareTo(b.u));
                if (!forward) stops.Reverse();      // A to B is the way the traffic runs

                for (int k = 0; k + 1 < stops.Count; k++)
                {
                    float u0 = EdgeToward(plan, stops[k].node, stops[k + 1].u);
                    float u1 = EdgeToward(plan, stops[k + 1].node, stops[k].u);
                    var a = Corridor(plan, across, u0);
                    var b = Corridor(plan, across, u1);
                    if ((b - a).sqrMagnitude < 4f) continue;
                    var road = net.AddOneWay(a, b, DeckOff, DeckLanes, FreewaySpeed,
                                             stops[k].node, stops[k + 1].node, plan.AlongZ);
                    float du = u1 - u0, len = Mathf.Max(road.Length, 0.01f);
                    var height = plan.Height;
                    road.SurfaceY = height(u0);
                    road.SurfaceAt = s => height(u0 + du * Mathf.Clamp01(s / len));
                }
            }
        }

        /// <summary>The four slip roads of the diamond, each one way: an exit down off a
        /// deck to the cross street, and an entrance back up. Each tapers the five
        /// metres from the deck's flank out to its own terminal as it climbs, so the
        /// ramp stands ALONGSIDE the freeway at the gore rather than inside it.</summary>
        void BuildSlipLanes(LaneNet net, FreewayPlan plan)
        {
            // (side, gore, its u, whether the ramp runs deck -> street)
            var ramps = new (int side, RoadNode gore, float u, bool off)[]
            {
                (+1, plan.ExitE, plan.RampU - RampRun, true),
                (+1, plan.EntryE, plan.RampU + RampRun, false),
                (-1, plan.ExitW, plan.RampU + RampRun, true),
                (-1, plan.EntryW, plan.RampU - RampRun, false),
            };
            foreach (var r in ramps)
            {
                var footNode = r.side > 0 ? plan.FootE : plan.FootW;
                if (footNode == null || r.gore == null) continue;
                float uFoot = EdgeToward(plan, footNode, r.u);
                // no closer to the crossroads than a ramp's own nose
                uFoot = r.u > plan.RampU ? Mathf.Max(uFoot, plan.RampU + RampFoot)
                                         : Mathf.Min(uFoot, plan.RampU - RampFoot);
                float uGore = EdgeToward(plan, r.gore, plan.RampU);

                var atFoot = Corridor(plan, plan.Mid + r.side * RampFootOff, uFoot);
                var atGore = Corridor(plan, plan.Mid + r.side * RampGoreOff, uGore);
                float yGore = plan.Height(uGore);

                var a = r.off ? atGore : atFoot;
                var b = r.off ? atFoot : atGore;
                var nodeA = r.off ? r.gore : footNode;
                var nodeB = r.off ? footNode : r.gore;
                var road = net.AddOneWay(a, b, FrontageHalf, RampLanes, RampSpeed, nodeA, nodeB, plan.AlongZ);
                float y0 = r.off ? yGore : GradeY, y1 = r.off ? GradeY : yGore;
                float len = Mathf.Max(road.Length, 0.01f);
                road.SurfaceY = y0;
                road.SurfaceAt = s => Mathf.Lerp(y0, y1, Mathf.Clamp01(s / len));
            }
        }

        // -------------------------------------------------------------- geometry

        /// <summary>A junction on the freeway network, surfaced as a ROAD: the grid's
        /// own bare road cells over the box, a zebra band across every arm a street
        /// actually arrives on, and, if it is a place people walk, the kerb corners.
        /// What stood here before was a lot floor - see the note at the top.</summary>
        void PaveFreewayJunction(float cx, float cz, float halfX, float halfZ,
                                 float armHalfX = 0f, float armHalfZ = 0f,
                                 bool north = false, bool south = false, bool east = false, bool west = false,
                                 bool kerbs = false)
        {
            if (_bare == null) return;
            for (float mx = cx - halfX; mx < cx + halfX - 0.1f; mx += Cell)
                for (float mz = cz - halfZ; mz < cz + halfZ - 0.1f; mz += Cell)
                    PlaceCellOnce(_bare, mx, mz, 0);

            if (_crossing != null && armHalfX > 0.1f)
                foreach (var t in Band(-armHalfX, armHalfX))
                {
                    if (north) PlaceTileOnce(_crossing, cx + t.off, cz + halfZ, 90, t.w, Cell);
                    if (south) PlaceTileOnce(_crossing, cx + t.off, cz - halfZ - Cell, 90, t.w, Cell);
                }
            if (_crossing != null && armHalfZ > 0.1f)
                foreach (var t in Band(-armHalfZ, armHalfZ))
                {
                    if (east) PlaceTileOnce(_crossing, cx + halfX, cz + t.off, 0, Cell, t.w);
                    if (west) PlaceTileOnce(_crossing, cx - halfX - Cell, cz + t.off, 0, Cell, t.w);
                }

            if (!kerbs || _swCorner == null) return;
            PlaceTile(_swCorner, cx - halfX - Sidewalk, cz - halfZ - Sidewalk, 0, Sidewalk, Sidewalk);
            PlaceTile(_swCorner, cx - halfX - Sidewalk, cz + halfZ, 90, Sidewalk, Sidewalk);
            PlaceTile(_swCorner, cx + halfX, cz + halfZ, 180, Sidewalk, Sidewalk);
            PlaceTile(_swCorner, cx + halfX, cz - halfZ - Sidewalk, 270, Sidewalk, Sidewalk);
        }

        /// <summary>The frontage roads' asphalt and the slip roads' decks, laid with the
        /// rest of the seam. The lanes come later (BuildFreewayLanes); this is what is
        /// seen.</summary>
        void BuildInterchangeGeometry(FreewayPlan plan)
        {
            EnsureConnectorKit();
            int roads = plan.AlongZ ? horizontalRoadZ.Length : verticalRoadX.Length;

            for (int side = -1; side <= 1; side += 2)
            {
                float across = plan.Mid + side * FrontageOff;
                // the carriageway from one street to the next, stopping at each
                for (int r = 0; r + 1 < roads; r++)
                {
                    float u0 = plan.AlongZ ? horizontalRoadZ[r] + HHalf(r) : verticalRoadX[r] + VHalf(r);
                    float u1 = plan.AlongZ ? horizontalRoadZ[r + 1] - HHalf(r + 1) : verticalRoadX[r + 1] - VHalf(r + 1);
                    if (u1 - u0 < Cell) continue;
                    if (plan.AlongZ) _connectorKit.LayRoadAlongZ(across, u0, u1);
                    else _connectorKit.LayRoadAlongX(across, u0, u1);
                }
                // the give-way band where it meets each street. The street's own asphalt
                // is already down - it runs the width of the map under the deck - so all
                // a crossroads wants here is its marking.
                if (_crossing == null) continue;
                for (int r = 0; r < roads; r++)
                {
                    float u = plan.AlongZ ? horizontalRoadZ[r] : verticalRoadX[r];
                    float half = plan.AlongZ ? HHalf(r) : VHalf(r);
                    foreach (var t in Band(-FrontageHalf, FrontageHalf))
                    {
                        if (plan.AlongZ)
                        {
                            PlaceTileOnce(_crossing, across + t.off, u + half, 90, t.w, Cell);
                            PlaceTileOnce(_crossing, across + t.off, u - half - Cell, 90, t.w, Cell);
                        }
                        else
                        {
                            PlaceTileOnce(_crossing, u + half, across + t.off, 0, Cell, t.w);
                            PlaceTileOnce(_crossing, u - half - Cell, across + t.off, 0, Cell, t.w);
                        }
                    }
                }
            }

            if (plan.HasRamps) BuildSlipGeometry(plan);
        }

        /// <summary>A slip road's own deck, and the stretch of ordinary road between its
        /// foot and the cross street. The ramp is made of the same pieces the freeway
        /// is, laid along a line that is square to nothing - it tapers across the
        /// corridor as it climbs - each piece a straight chord of the climb, with a pier
        /// under it wherever it stands high enough to want one.</summary>
        void BuildSlipGeometry(FreewayPlan plan)
        {
            var ramps = new (int side, float u)[]
            {
                (+1, plan.RampU - RampRun), (+1, plan.RampU + RampRun),
                (-1, plan.RampU + RampRun), (-1, plan.RampU - RampRun),
            };
            foreach (var r in ramps)
            {
                // the same foot the lane starts at (BuildSlipLanes), so the deck and
                // the carriageway on it are the same piece of road
                float uFoot = plan.RampU + Mathf.Sign(r.u - plan.RampU) * Mathf.Max(RampFoot, plan.RampHalf);
                var foot = Corridor(plan, plan.Mid + r.side * RampFootOff, uFoot);
                var gore = Corridor(plan, plan.Mid + r.side * RampGoreOff, r.u);
                // the ramp's own deck all the way from the crossroads to the gore -
                // it is the road surface, so nothing else is laid under it
                LayRampDeck(foot, GradeY, gore, plan.Height(r.u));
            }
        }

        /// <summary>A run of deck pieces from one point to another, climbing as it goes.
        /// The piece is 11.4 m across and 20 m along its own +Z with its pivot riding
        /// the +X edge, so it goes down a half-width off the line it is meant to be
        /// centred on - the same convention the seam freeway's own rows use (Seams.cs,
        /// LayDeckRow), only turned to whatever angle the ramp actually runs at.</summary>
        void LayRampDeck(Vector3 a, float ya, Vector3 b, float yb)
        {
            if (_highwayDeck == null) return;
            const float DeckLen = 20f;
            var d = b - a; d.y = 0f;
            float run = d.magnitude;
            if (run < 1f) return;
            var dir = d / run;
            float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            var right = new Vector3(dir.z, 0f, -dir.x);      // the piece's own +X, level
            int count = Mathf.Max(1, Mathf.RoundToInt(run / DeckLen));
            float len = run / count;
            float pitch = Mathf.Atan2(ya - yb, run) * Mathf.Rad2Deg;
            for (int k = 0; k < count; k++)
            {
                var at = a + dir * (k * len) + right * 5f;
                at.y = Mathf.Lerp(ya, yb, k / (float)count);
                var go = Instantiate(_highwayDeck, at, Quaternion.Euler(pitch, yaw, 0f), SeamsRoot);
                go.name = "Ramp deck";
                if (Mathf.Abs(len - DeckLen) > 0.01f)
                    go.transform.localScale = new Vector3(1f, 1f, len / DeckLen);

                if (_highwayPillar == null) continue;
                float y = Mathf.Lerp(ya, yb, (k + 0.5f) / count);
                if (y < 3.5f) continue;                       // a road needs no piers
                var pier = a + dir * ((k + 0.5f) * len);
                pier.y = y;
                // never in a carriageway. A ramp climbs over the streets between its
                // foot and its gore, and a pier standing in one of them is a wreck
                // waiting - the same rule the main deck's piers follow.
                if (InAnyRoad(pier)) continue;
                Prop(_highwayPillar, pier, yaw, SeamsRoot).name = "Ramp pier";
            }
        }

        /// <summary>Whether a point stands in any street of the grid, its parking
        /// strips and a metre of air included.</summary>
        bool InAnyRoad(Vector3 p)
        {
            for (int i = 0; i < verticalRoadX.Length; i++)
                if (Mathf.Abs(p.x - verticalRoadX[i]) < VHalf(i) + 1.5f) return true;
            for (int j = 0; j < horizontalRoadZ.Length; j++)
                if (Mathf.Abs(p.z - horizontalRoadZ[j]) < HHalf(j) + 1.5f) return true;
            return false;
        }

        void EnsureConnectorKit()
        {
            if (_connectorKit != null) return;
            var root = ((IDistrictHost)this).StaticRoot("District Roads");
            _connectorKit = new StreetKit(root) { Palms = false };
        }
    }
}
