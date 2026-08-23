using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// Where an elevated freeway runs, and where you get on and off it. A route is a
    /// straight line across the map at a height, a list of the city's own road lines
    /// its interchanges hang off, and a toll plaza in the middle - which is the whole
    /// of what a motorway is that a long bridge is not.
    /// </summary>
    [System.Serializable]
    public class FreewayRoute
    {
        public bool on;
        [Tooltip("The line runs north-south (else east-west).")]
        public bool alongZ;
        [Tooltip("Its centre line, across its own axis: the x of a north-south freeway, " +
                 "the z of an east-west one. Off the grid is where a freeway between two " +
                 "quarters belongs; over it is a viaduct down a corridor.")]
        public float across = 320f;
        [Tooltip("The deck's road surface, metres over the city's own road level. It does " +
                 "not change along the run: the freeway is elevated end to end and it is " +
                 "the ramps that come down.")]
        public float deckY = 9f;
        [Tooltip("Which road lines of the OTHER axis the interchanges hang off, counted " +
                 "from the west for a north-south street. A ramp needs 160 m of line " +
                 "either side of the street it lands on, so two interchanges want 320 m " +
                 "between them.")]
        public int[] interchanges = new int[0];
        [Tooltip("A barrier plaza on the mainline, between the two furthest-apart " +
                 "interchanges: booths, booms, and every driver stopping to pay.")]
        public bool tollPlaza = true;
        [Tooltip("Seconds the money takes, once he has stopped.")]
        public float tollDwell = 2.2f;
    }

    // The elevated freeway: a road between two quarters that are too far apart to
    // drive between through the streets.
    //
    // It is a plain thing on purpose. Two one-way decks side by side on piers, level
    // from end to end; at each interchange a ramp down off one deck and a ramp up onto
    // the other, both landing on a link road that runs out of the quarter and under the
    // freeway; a barrier plaza on the mainline where the money is taken. Nothing here
    // rides over the grid, nothing weaves through a corridor, and there is no piece of
    // it a car cannot get on to or off - which is what was wrong with the freeway the
    // city used to roll (RoadDemoBuilder.Seams.cs, still there and still switched off
    // by the NoFreeways pass).
    //
    //   quarter                                                        quarter
    //     |                     the toll plaza                            |
    //     |  ramp up   ==========|=|==========================  ramp down |
    //     +--- link ---o---------o-o-------------------------o--- link ---+
    //                foot       gates                       foot
    //
    // The link road is what makes the whole thing a road: it leaves a junction of the
    // quarter's own grid, runs out under the deck, and ends at the far foot - so a car
    // that drives up it has the ramp as its way on, and a car that comes down the ramp
    // has the quarter as its way off. No dead ends and no turn-rounds.
    public partial class RoadDemoBuilder
    {
        [Header("The elevated freeway")]
        [Tooltip("A motorway between two quarters: elevated, with a way on and a way off " +
                 "at each end and a toll plaza between them. Off in the city (the old " +
                 "corridor freeway is off too - see the NoFreeways pass); the freeway " +
                 "demo scene switches it on.")]
        public FreewayRoute freewayRoute = new FreewayRoute();

        // ------------------------------------------------------------------ sizes

        /// <summary>Each deck's centre line, off the route's own: two 11.4 m
        /// carriageways meeting down the middle of the line.</summary>
        const float FreeDeckOff = 5.7f;
        /// <summary>Half a deck, kerb to kerb.</summary>
        const float FreeDeckHalf = 5.7f;
        /// <summary>Two lanes on a one-way deck, off its own centre.</summary>
        static readonly float[] FreeDeckLanes = { -2.85f, 2.85f };
        /// <summary>The gore: where a ramp meets its deck. The NODE stands a deck's
        /// width out, so its box covers both the deck's outer lane and the ramp's end;
        /// the ramp's own asphalt is laid a half piece further out again, alongside the
        /// freeway rather than inside it.</summary>
        const float FreeGoreOff = 11.4f, FreeGoreDeck = 17.1f;
        /// <summary>Where the ramp meets the link road, off the route's centre.</summary>
        const float FreeFootOff = 30f;
        /// <summary>How far along the line a ramp runs: nine metres of climb over this
        /// is about one in eighteen, which is a ramp and not a wall.</summary>
        const float FreeRampRun = 160f;
        /// <summary>How much deck stands past the outermost gore - the nose of the road,
        /// so the freeway does not stop dead where the last car leaves it.</summary>
        const float FreeOvershoot = 45f;
        /// <summary>The ramp's foot: the city's own road level.</summary>
        const float FreeGrade = 0.12f;
        const float FreeSpeed = 25f, FreeRampSpeed = 11f;

        // -------------------------------------------------------------- the plan

        sealed class FreeStation
        {
            public int Line;              // the grid road line the ramps land on
            public float U;               // where that line crosses the freeway
            public RoadNode Grid;         // the junction of the grid the link road leaves
            public RoadNode Near, Far;    // the feet, on the near and far side of the deck
            public int NearSide;
            public RoadNode Foot(int side) => side == NearSide ? Near : Far;
        }

        sealed class FreeGore
        {
            public float U;
            public int Side;              // which deck
            public bool Entry;            // onto the deck (else off it)
            public FreeStation Station;
            public RoadNode Node;
        }

        sealed class FreeStop
        {
            public float U;
            public RoadNode Node;
        }

        readonly List<FreeStation> _freeStations = new List<FreeStation>();
        readonly List<FreeGore> _freeGores = new List<FreeGore>();
        readonly List<(int side, List<FreeStop> stops)> _freeRuns = new List<(int, List<FreeStop>)>();
        readonly List<(int side, float u, RoadNode node, TollGate gate)> _freeTolls
            = new List<(int, float, RoadNode, TollGate)>();
        Transform _freewayRoot;
        TollPlaza _tollPlaza;
        float _freeFrom, _freeTo;
        bool _freeReady;

        /// <summary>The freeway's own footprint, for the island and the flora.</summary>
        internal bool FreewayOn => _freeReady;

        // ------------------------------------------------------------------ frame

        /// <summary>A point in the route's own frame: across the line, along it.</summary>
        Vector3 FreeAt(float across, float along, float y = 0f)
            => freewayRoute.alongZ ? new Vector3(across, y, along) : new Vector3(along, y, across);

        /// <summary>A node in the route's frame, its box given across and along.</summary>
        RoadNode FreeNode(float across, float along, float halfAcross, float halfAlong)
        {
            var p = FreeAt(across, along);
            float hx = freewayRoute.alongZ ? halfAcross : halfAlong;
            float hz = freewayRoute.alongZ ? halfAlong : halfAcross;
            return new RoadNode
            {
                I = -7, J = -1,
                X = p.x, Z = p.z, XMin = p.x - hx, XMax = p.x + hx, ZMin = p.z - hz, ZMax = p.z + hz,
                StopSetback = 3f,
            };
        }

        /// <summary>A node's box edge along the LINE, on the side a point lies.</summary>
        float FreeEdgeAlong(RoadNode n, float towardU)
        {
            float c = freewayRoute.alongZ ? n.Z : n.X;
            float lo = freewayRoute.alongZ ? n.ZMin : n.XMin;
            float hi = freewayRoute.alongZ ? n.ZMax : n.XMax;
            return towardU > c ? hi : lo;
        }

        /// <summary>And ACROSS it, which is the axis the link road runs down.</summary>
        float FreeEdgeAcross(RoadNode n, float towardAcross)
        {
            float c = freewayRoute.alongZ ? n.X : n.Z;
            float lo = freewayRoute.alongZ ? n.XMin : n.ZMin;
            float hi = freewayRoute.alongZ ? n.XMax : n.ZMax;
            return towardAcross > c ? hi : lo;
        }

        float FreeAcrossOf(RoadNode n) => freewayRoute.alongZ ? n.X : n.Z;
        float FreeAlongOf(RoadNode n) => freewayRoute.alongZ ? n.Z : n.X;

        // ------------------------------------------------------------------ build

        /// <summary>The freeway's geometry, and every node its lanes will want. Laid
        /// with the rest of the ground works, before the lane graph exists; WireFreeway
        /// puts it into the graph while BuildGraph is running.</summary>
        void BuildFreeway()
        {
            var route = freewayRoute;
            if (route == null || !route.on) return;

            var lines = route.alongZ ? horizontalRoadZ : verticalRoadX;
            var other = route.alongZ ? verticalRoadX : horizontalRoadZ;
            if (lines == null || lines.Length == 0 || route.interchanges == null || route.interchanges.Length == 0)
            {
                Debug.LogWarning("[freeway] no interchange lines named - nothing built.");
                return;
            }

            // which side of the freeway the city is on: the link roads run out of it
            bool beyondHigh = route.across > other[other.Length - 1];
            int end = beyondHigh ? other.Length - 1 : 0;
            int nearSide = beyondHigh ? -1 : +1;

            foreach (int line in route.interchanges)
            {
                if (line < 0 || line >= lines.Length)
                {
                    Debug.LogWarning($"[freeway] interchange line {line} is off the grid - skipped.");
                    continue;
                }
                var st = new FreeStation
                {
                    Line = line,
                    U = lines[line],
                    NearSide = nearSide,
                    Grid = route.alongZ ? _nodes[end, line] : _nodes[line, end],
                };
                st.Near = FreeNode(route.across + nearSide * FreeFootOff, st.U, 7f, 8f);
                st.Far = FreeNode(route.across - nearSide * FreeFootOff, st.U, 7f, 8f);
                _freeStations.Add(st);
            }
            if (_freeStations.Count < 2)
            {
                Debug.LogWarning("[freeway] a freeway wants two interchanges at least - nothing built.");
                _freeStations.Clear();
                return;
            }
            _freeStations.Sort((a, b) => a.U.CompareTo(b.U));

            // the two decks, each with its gores in the order its traffic meets them.
            // The deck running with +u keeps to its own right, which is +x on a
            // north-south line and -z on an east-west one.
            int sF = route.alongZ ? +1 : -1;
            _freeFrom = float.MaxValue; _freeTo = float.MinValue;
            for (int k = 0; k < 2; k++)
            {
                int side = k == 0 ? sF : -sF;
                bool forward = side == sF;
                var gores = new List<FreeGore>();
                foreach (var st in _freeStations)
                {
                    float exitU = forward ? st.U - FreeRampRun : st.U + FreeRampRun;
                    float entryU = forward ? st.U + FreeRampRun : st.U - FreeRampRun;
                    gores.Add(new FreeGore { U = exitU, Side = side, Entry = false, Station = st });
                    gores.Add(new FreeGore { U = entryU, Side = side, Entry = true, Station = st });
                }
                gores.Sort((a, b) => forward ? a.U.CompareTo(b.U) : b.U.CompareTo(a.U));
                // nothing may leave a deck before anything has joined it, and nothing may
                // join it past the last way off: those gores are ramps to nowhere
                while (gores.Count > 0 && !gores[0].Entry) gores.RemoveAt(0);
                while (gores.Count > 0 && gores[gores.Count - 1].Entry) gores.RemoveAt(gores.Count - 1);
                if (gores.Count < 2) continue;

                var stops = new List<FreeStop>();
                foreach (var g in gores)
                {
                    g.Node = FreeNode(route.across + side * FreeGoreOff, g.U, 8f, 9f);
                    _freeGores.Add(g);
                    stops.Add(new FreeStop { U = g.U, Node = g.Node });
                    _freeFrom = Mathf.Min(_freeFrom, g.U);
                    _freeTo = Mathf.Max(_freeTo, g.U);
                }
                _freeRuns.Add((side, stops));
            }
            if (_freeRuns.Count == 0)
            {
                Debug.LogWarning("[freeway] neither deck came out with a way on and a way off - nothing built.");
                return;
            }

            _freewayRoot = ((IDistrictHost)this).StaticRoot("Freeway");
            EnsureConnectorKit();
            BuildTollPlaza(route);
            LayFreewayGround(route);
            LayFreewayDecks(route);
            LayFreewayRamps(route);
            LayFreewayLinks(route);
            _freeReady = true;

            Debug.Log($"[freeway] {_freeStations.Count} interchanges, {_freeGores.Count} ramps, " +
                      $"{_freeTolls.Count} toll gates, deck {_freeFrom:F0} to {_freeTo:F0} m at {route.deckY:F1} m.");
        }

        /// <summary>The plaza's own nodes and gates - one gate on each deck, both at the
        /// same point of the line, which is what a barrier across a motorway is.</summary>
        void BuildTollPlaza(FreewayRoute route)
        {
            if (!route.tollPlaza) return;
            // between the two interchanges that stand furthest apart: the open run,
            // where a plaza belongs and where every crossing has to pass it
            float u = (_freeStations[0].U + _freeStations[_freeStations.Count - 1].U) * 0.5f;

            var go = new GameObject("Toll plaza");
            go.transform.SetParent(_traffic, false);
            _tollPlaza = go.AddComponent<TollPlaza>();

            foreach (var run in _freeRuns)
            {
                // it must fall on the deck's own run, between a way on and a way off
                float lo = Mathf.Min(run.stops[0].U, run.stops[run.stops.Count - 1].U);
                float hi = Mathf.Max(run.stops[0].U, run.stops[run.stops.Count - 1].U);
                if (u < lo + 30f || u > hi - 30f) continue;

                var node = FreeNode(route.across + run.side * FreeDeckOff, u, FreeDeckHalf, 5f);
                var gate = new TollGate
                {
                    Name = run.side > 0 ? "toll +" : "toll -",
                    Dwell = Mathf.Max(0.2f, route.tollDwell),
                    Node = node,
                };
                node.Toll = gate;
                _tollPlaza.Gates.Add(gate);
                _freeTolls.Add((run.side, u, node, gate));

                // and into the deck's own list of stops, in travel order
                bool forward = run.stops[0].U < run.stops[run.stops.Count - 1].U;
                int at = run.stops.Count;
                for (int i = 0; i < run.stops.Count; i++)
                {
                    bool past = forward ? run.stops[i].U > u : run.stops[i].U < u;
                    if (past) { at = i; break; }
                }
                run.stops.Insert(at, new FreeStop { U = u, Node = node });
            }
            LayTollPlaza(route);
        }

        // --------------------------------------------------------------- geometry

        /// <summary>The ground the freeway stands on: held flat and bare from the first
        /// pier to the last, and down each link road, so the island's hills part round
        /// it instead of swallowing a pier.</summary>
        void LayFreewayGround(FreewayRoute route)
        {
            float lo = _freeFrom - FreeOvershoot - 10f, hi = _freeTo + FreeOvershoot + 10f;
            float half = FreeFootOff + 18f;
            var strip = route.alongZ
                ? Rect.MinMaxRect(route.across - half, lo, route.across + half, hi)
                : Rect.MinMaxRect(lo, route.across - half, hi, route.across + half);
            _reservations.Level(strip, RoadBed);
            _reservations.NoFlora(strip);

            foreach (var st in _freeStations)
            {
                if (st.Grid == null) continue;
                float a = FreeAcrossOf(st.Grid), b = route.across + st.NearSide * -FreeFootOff;
                var link = route.alongZ
                    ? Rect.MinMaxRect(Mathf.Min(a, b) - 14f, st.U - 14f, Mathf.Max(a, b) + 14f, st.U + 14f)
                    : Rect.MinMaxRect(st.U - 14f, Mathf.Min(a, b) - 14f, st.U + 14f, Mathf.Max(a, b) + 14f);
                _reservations.Level(link, RoadBed);
                _reservations.NoFlora(link);
            }
        }

        /// <summary>The decks themselves: two runs of the pack's own deck piece on
        /// piers, level from the nose of the road to its tail.</summary>
        void LayFreewayDecks(FreewayRoute route)
        {
            var deck = FreewayKit.TryLoad(FreewayKit.DeckPath);
            var pillar = FreewayKit.TryLoad(FreewayKit.PillarPath);
            if (deck == null) { Debug.LogWarning("[freeway] no deck piece in the pack - no road laid."); return; }

            float from = _freeFrom - FreeOvershoot, to = _freeTo + FreeOvershoot;
            foreach (var run in _freeRuns)
            {
                float across = route.across + run.side * FreeDeckOff;
                int laid = FreewayKit.LayDeck(deck, pillar,
                    FreeAt(across, from), route.deckY, FreeAt(across, to), route.deckY,
                    _freewayRoot, PierFree, "Deck");
                if (laid == 0) Debug.LogWarning("[freeway] a deck came out empty.");
            }
        }

        /// <summary>The ramps: the same deck pieces, laid along a line that climbs and
        /// tapers at once - from the link road at grade out at 30 m, up to the gore
        /// alongside the freeway.</summary>
        void LayFreewayRamps(FreewayRoute route)
        {
            var deck = FreewayKit.TryLoad(FreewayKit.DeckPath);
            var pillar = FreewayKit.TryLoad(FreewayKit.PillarPath);
            if (deck == null) return;

            foreach (var g in _freeGores)
            {
                var foot = g.Station.Foot(g.Side);
                float uFoot = FreeEdgeAlong(foot, g.U);
                var a = FreeAt(route.across + g.Side * FreeFootOff, uFoot);
                var b = FreeAt(route.across + g.Side * FreeGoreDeck, g.U);
                FreewayKit.LayDeck(deck, pillar, a, FreeGrade, b, route.deckY,
                                   _freewayRoot, PierFree, "Ramp");
            }
        }

        /// <summary>The link roads: ordinary asphalt from the quarter's own junction out
        /// under the deck to the far foot, and a junction pad at each foot.</summary>
        void LayFreewayLinks(FreewayRoute route)
        {
            foreach (var st in _freeStations)
            {
                if (st.Grid == null) continue;
                float a = FreeAcrossOf(st.Grid);
                float b = route.across - st.NearSide * FreeFootOff;   // the far foot
                float lo = Mathf.Min(a, b), hi = Mathf.Max(a, b);
                if (route.alongZ) _connectorKit.LayRoadAlongX(st.U, lo, hi);
                else _connectorKit.LayRoadAlongZ(st.U, lo, hi);

                foreach (var f in new[] { st.Near, st.Far })
                    PaveFreewayJunction(f.X, f.Z, (f.XMax - f.XMin) * 0.5f, (f.ZMax - f.ZMin) * 0.5f);
            }
        }

        /// <summary>The plaza: an apron outboard of each deck for the booths to stand
        /// on, a booth at each end of the barrier line, and a boom over every lane.
        /// The arms are the one part of a freeway that moves, so they hang off the live
        /// root rather than the static one - a merged mesh cannot lift.</summary>
        void LayTollPlaza(FreewayRoute route)
        {
            if (_freeTolls.Count == 0) return;
            var deck = FreewayKit.TryLoad(FreewayKit.DeckPath);
            var pillar = FreewayKit.TryLoad(FreewayKit.PillarPath);
            var boom = FreewayKit.TryLoad(FreewayKit.BoomPath);
            var booth = FreewayKit.TryLoad(FreewayKit.BoothPath);
            if (boom == null && booth == null)
                Debug.LogWarning("[freeway] neither boom nor booth in the packs: the plaza is a line on the road.");

            var live = new GameObject("Toll booms").transform;
            live.SetParent(_traffic, false);

            foreach (var t in _freeTolls)
            {
                float across = route.across + t.side * FreeDeckOff;
                float u = t.u;

                // the apron: one deck piece outboard of the carriageway, so nothing the
                // plaza stands on is in a lane
                if (deck != null)
                {
                    float apron = across + t.side * (FreeDeckHalf * 2f);
                    FreewayKit.LayDeck(deck, pillar, FreeAt(apron, u - 20f), route.deckY,
                                       FreeAt(apron, u + 20f), route.deckY, _freewayRoot,
                                       PierFree, "Plaza apron");
                }

                // the two lanes' booms: one off the outer edge, one off the island the
                // decks share down the middle of the line
                float outer = across + t.side * FreeDeckHalf;
                float inner = route.across;
                if (boom != null)
                {
                    StandBoom(boom, FreeAt(outer, u, route.deckY), -t.side, live, t.gate);
                    StandBoom(boom, FreeAt(inner, u, route.deckY), t.side, live, t.gate);
                }
                if (booth != null)
                {
                    FreewayKit.Sit(booth, FreeAt(across + t.side * (FreeDeckHalf + 3f), u - 4f, route.deckY),
                                   FreeYaw(-t.side), _freewayRoot, "Toll booth");
                }
            }
        }

        /// <summary>A yaw that faces along the ACROSS axis, one way or the other.</summary>
        float FreeYaw(int side)
        {
            var dir = FreeAt(side, 0f) - FreeAt(0f, 0f);
            return Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        }

        /// <summary>One boom, its arm reaching across the lane. Which way the arm points
        /// out of its own pivot is measured off the prefab, so the gate faces the right
        /// way whichever axis the freeway runs down, and the lift turns about the axis
        /// the arm actually lies on.</summary>
        void StandBoom(GameObject prefab, Vector3 at, int reach, Transform parent, TollGate gate)
        {
            var b = FreewayKit.Measure(prefab);
            bool armAlongX = Mathf.Abs(b.center.x) >= Mathf.Abs(b.center.z);
            // the world direction the arm must point: across the deck, toward the lane
            var dir = FreeAt(reach, 0f) - FreeAt(0f, 0f);
            dir = dir.normalized;
            float yaw = armAlongX
                ? Mathf.Atan2(-dir.z, dir.x) * Mathf.Rad2Deg      // local +X onto dir
                : Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;      // local +Z onto dir
            if (armAlongX && b.center.x < 0f) yaw += 180f;
            if (!armAlongX && b.center.z < 0f) yaw += 180f;

            var go = FreewayKit.Sit(prefab, at, yaw, parent, "Toll boom");
            if (go == null) return;
            // lifting: an arm along local X swings about local Z, one along local Z about
            // local X, and the sign is whichever takes its far end upward
            var axis = armAlongX ? Vector3.forward : Vector3.right;
            float lift = armAlongX
                ? (b.center.x >= 0f ? 75f : -75f)
                : (b.center.z >= 0f ? -75f : 75f);
            gate.Arm = new TollArm(go.transform, axis, lift);
        }

        /// <summary>Where a pier may stand: never in a street of the grid, and never in
        /// one of the freeway's own link roads either - a pier in the road a car comes
        /// off the ramp onto is the same wreck waiting as one in a street.</summary>
        bool PierFree(Vector3 p)
        {
            if (InAnyRoad(p)) return false;
            foreach (var st in _freeStations)
            {
                float along = freewayRoute.alongZ ? p.z : p.x;
                if (Mathf.Abs(along - st.U) > StreetHalf + 1.5f) continue;
                float across = freewayRoute.alongZ ? p.x : p.z;
                float a = st.Grid != null ? FreeAcrossOf(st.Grid)
                                          : freewayRoute.across + st.NearSide * (FreeFootOff + 40f);
                float b = freewayRoute.across - st.NearSide * FreeFootOff;
                if (across >= Mathf.Min(a, b) - 2f && across <= Mathf.Max(a, b) + 2f) return false;
            }
            return true;
        }

        // ------------------------------------------------------------------ lanes

        /// <summary>The freeway in the lane graph: the decks, the ramps and the link
        /// roads, laid while BuildGraph is running and before its connectors are.</summary>
        void WireFreeway(LaneNet net)
        {
            if (!_freeReady || net == null) return;
            var route = freewayRoute;

            foreach (var st in _freeStations) { net.Nodes.Add(st.Near); net.Nodes.Add(st.Far); }
            foreach (var g in _freeGores) net.Nodes.Add(g.Node);
            foreach (var t in _freeTolls) net.Nodes.Add(t.node);

            // the link roads: out of the quarter, under the deck, to the far foot
            foreach (var st in _freeStations)
            {
                if (st.Grid != null) FreeLink(net, st.Grid, st.Near);
                FreeLink(net, st.Near, st.Far);
            }

            // the decks, gore to gore with the toll gate standing between
            foreach (var run in _freeRuns)
            {
                float across = route.across + run.side * FreeDeckOff;
                for (int k = 0; k + 1 < run.stops.Count; k++)
                {
                    var p = run.stops[k];
                    var q = run.stops[k + 1];
                    var a = FreeAt(across, FreeEdgeAlong(p.Node, q.U));
                    var b = FreeAt(across, FreeEdgeAlong(q.Node, p.U));
                    if ((b - a).sqrMagnitude < 4f) continue;
                    var road = net.AddOneWay(a, b, FreeDeckHalf, FreeDeckLanes, FreeSpeed,
                                             p.Node, q.Node, route.alongZ);
                    road.SurfaceY = route.deckY;
                    road.Elevated = true;
                }
            }

            // the ramps: one way, climbing or falling with the road under them
            foreach (var g in _freeGores)
            {
                var foot = g.Station.Foot(g.Side);
                var atFoot = FreeAt(route.across + g.Side * FreeFootOff, FreeEdgeAlong(foot, g.U));
                var atGore = FreeAt(route.across + g.Side * FreeGoreOff, FreeEdgeAlong(g.Node, g.Station.U));
                var a = g.Entry ? atFoot : atGore;
                var b = g.Entry ? atGore : atFoot;
                var nodeA = g.Entry ? foot : g.Node;
                var nodeB = g.Entry ? g.Node : foot;
                var road = net.AddOneWay(a, b, StreetKit.RoadHalf, FreeRampLanes, FreeRampSpeed,
                                         nodeA, nodeB, route.alongZ);
                float y0 = g.Entry ? FreeGrade : route.deckY;
                float y1 = g.Entry ? route.deckY : FreeGrade;
                float len = Mathf.Max(road.Length, 0.01f);
                road.SurfaceY = y0;
                road.SurfaceAt = s => Mathf.Lerp(y0, y1, Mathf.Clamp01(s / len));
                road.Elevated = true;      // it is the freeway too: the trace counts it as such
            }
        }

        static readonly float[] FreeRampLanes = { 0f };

        /// <summary>An ordinary two-way street between two of the freeway's own
        /// junctions, down the axis across the line.</summary>
        void FreeLink(LaneNet net, RoadNode a, RoadNode b)
        {
            float u = FreeAlongOf(b);
            float acrossA = FreeAcrossOf(a), acrossB = FreeAcrossOf(b);
            var from = FreeAt(FreeEdgeAcross(a, acrossB), FreeAlongOf(a));
            var to = FreeAt(FreeEdgeAcross(b, acrossA), u);
            if ((to - from).sqrMagnitude < 4f) return;
            // as wide as the asphalt under it and no wider - LayRoadAlongZ lays a plain
            // two-lane carriageway, and a graph road wider than its own road would put a
            // parked car on the grass beside it
            var link = net.AddRoad(from, to, StreetKit.RoadHalf, LaneOffsets(false), streetSpeed,
                                   a, b, !freewayRoute.alongZ);
            link.ParkingA = link.ParkingB = false;
        }

        /// <summary>What the plaza took, for the console when a run ends.</summary>
        internal string FreewayStory()
            => _tollPlaza == null ? "no toll plaza" : _tollPlaza.Story();
    }
}
