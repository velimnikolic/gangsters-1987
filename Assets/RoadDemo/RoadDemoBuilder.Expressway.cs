using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    // The expressway: a trunk on piers round the outside of the city, branches hung off
    // it, and an end at each of them that is a road and not a cliff.
    //
    //   branch (at grade)        the band, on piers                    into town
    //   ====\                                                            /====
    //        \___ climb ___/=========|=========|===============\ fall __/
    //                            diamond    T to the second branch    terminus
    //
    // Three things make it a motorway rather than the ring the city used to roll:
    //
    //  - the line BENDS (RoadLine + Carriageway.Path), so a corner is a 260 m radius
    //    and not four square junctions;
    //  - a ramp leaves and joins at a SEAM (RoadNode.Seam), which is not a crossing:
    //    nothing conflicts there, so the deck's own traffic never stops for a ramp;
    //  - the exit is taken from a lane of its own - a deceleration lane the driver has
    //    to move over into (RoadCar's lane changing) - and an entrance is a lane that
    //    runs out, so a car that joins has to merge.
    //
    // Nothing here is switched on in Game.unity. The demo scene sets the route.
    public partial class RoadDemoBuilder
    {
        [Header("The expressway")]
        [Tooltip("A motorway round the outside of the city: a trunk on piers with a " +
                 "branch at each end and interchanges along it. Off in the city; the " +
                 "expressway demo scene switches it on.")]
        public ExpresswayRoute expressway = new ExpresswayRoute();

        // ------------------------------------------------------------------ the plan

        sealed class XwStretch
        {
            public float S0, S1;                 // along this deck's own line, travel order
            public RoadNode A, B;
            public bool Aux;                     // carries an auxiliary lane
            public bool AuxIsExit;               // ... and it is the one the exit is taken from
        }

        sealed class XwDeck
        {
            public int Side;                     // +1: the right-hand deck (runs with trunk s)
            public RoadLine Line;                // its own centre line, in travel order
            public readonly List<XwStretch> Stretches = new List<XwStretch>();
            public readonly List<(float s, float lo, float hi)> AuxWindows = new List<(float, float, float)>();

            /// <summary>The barrier at the mouth of this carriageway, where the road
            /// begins at a junction of the town. A car joins the motorway here and at
            /// no other place a ramp does not cover.</summary>
            public RoadNode TollNode;
            public float TollS;
        }

        sealed class XwRamp
        {
            public RoadLine Line;
            public RoadNode A, B;
            public float Y0, Y1;
            /// <summary>Where along the ramp it does its climbing (or its falling). A
            /// ramp that has to pass UNDER the deck it came off has to be on the ground
            /// before it gets there, not still coming down - which is 1.9 m of headroom
            /// and a bridge through the roof of a car.</summary>
            public float GradeFrom, GradeTo = -1f;
            public string Tag;

            public float Lift(float s)
            {
                float to = GradeTo > 0f ? GradeTo : Line.Length;
                return ExpresswayLayout.Grade(Y0, Y1, GradeFrom, to, s);
            }

            /// <summary>How far across this ramp's own line the DECK's pavement still
            /// reaches, sampled every few metres (MeasureRampClips). A ramp leaves at the
            /// gore in a lane of the motorway: its centre line is 6.65 m off the deck's
            /// and the deck there is 9.5 m wide, so a full 7.3 m slab laid from the gore
            /// is six and a half metres of second road surface lying ON the first, with
            /// its own edge beam hanging down through the carriageway. That is what a
            /// ramp that "does not line up with the road" is.</summary>
            public float[] Clip;
            public const float ClipStep = 4f;

            /// <summary>The barrier on the way in, and how far up the ramp it stands.
            /// A turnpike takes its money where you JOIN it, so every ramp on to the
            /// motorway has one and no ramp off it does.</summary>
            public RoadNode TollNode;
            public float TollS;

            /// <summary>Which way this one goes: an off ramp falls off the deck and
            /// arrives at the arterial, an on ramp leaves the arterial and climbs.</summary>
            public bool Falling => Y0 > Y1;

            /// <summary>The abutment: the station at which the ramp's surface passes
            /// <paramref name="height"/> on its way down (or up). Above it the road is a
            /// bridge and is made of what a bridge is made of; below it, it is a road in
            /// a town and is made of the same asphalt as the street it joins. A real one
            /// changes surface there and nowhere else - and certainly not at the kerb
            /// line of the junction, which is what having one surface for the whole ramp
            /// amounts to.</summary>
            public float Abutment(float height)
            {
                for (float s = 0f; s < Line.Length; s += 4f)
                    if (Falling ? Lift(s) <= height : Lift(s) >= height) return s;
                return Line.Length;
            }

            /// <summary>How wide the ramp opens where it meets the arterial, and over
            /// what length it opens. A ramp arrives at a signal ONE LANE wide and has to
            /// hold two - the car waiting to turn across the arterial and the one that is
            /// not - so it widens, the way every ramp terminal on earth widens. Laid at
            /// its running width all the way to the kerb line and met there by a junction
            /// two and a half times as wide, what it did instead was turn four lanes into
            /// two at a square corner, which you can see from the air and which is no
            /// kind of road joint at all.</summary>
            public const float MouthHalf = ExpresswayLayout.RampHalf + 3.65f, FlareRun = 80f;

            /// <summary>How much of THIS ramp the mouth is allowed: never more than
            /// its outer half. The gore is measured off the far end (Clip) and takes
            /// about fifty metres of a two-hundred-metre ramp; on a ramp short enough for
            /// the two to meet, the deck's edge wins and the kerb line would jump a metre
            /// and a half at the station where it stopped winning.</summary>
            float FlareOver => Mathf.Min(FlareRun, Line.Length * 0.45f);

            /// <summary>Where along the ramp the mouth opens out: the last stretch of an
            /// off ramp, the first of an on ramp.</summary>
            public void MouthRun(out float from, out float to)
            {
                float run = FlareOver;
                if (Falling) { to = Line.Length; from = Mathf.Max(0f, to - run); }
                else { from = 0f; to = Mathf.Min(Line.Length, run); }
            }

            /// <summary>How far out the ramp's left edge stands here on the mouth's
            /// account alone: its running width down the length of it, easing out to the
            /// mouth over the flare - eased, so the kerb line has no corner in it where
            /// the widening begins.</summary>
            float Flare(float s)
            {
                float run = FlareOver;
                float back = Falling ? Line.Length - s : s;
                if (back >= run) return ExpresswayLayout.RampHalf;
                float t = 1f - Mathf.Clamp01(back / run);
                return Mathf.Lerp(ExpresswayLayout.RampHalf, MouthHalf, t * t * (3f - 2f * t));
            }

            /// <summary>Where the ramp's own slab begins, across its line: hard against
            /// the deck's edge while the two are one road, the full half width once the
            /// ramp has drawn away, and the mouth's width where it arrives. Between the
            /// first two it IS the gore - a sliver at the nose opening out into a road.
            ///
            /// The two never argue: the gore is at one end of a two-hundred-metre ramp
            /// and the mouth at the other, so wherever the deck still reaches across this
            /// line, the deck's edge is the answer.</summary>
            public float Inner(float s)
            {
                float lo = -ExpresswayLayout.RampHalf;
                if (Clip != null && Clip.Length >= 2)
                {
                    float t = Mathf.Clamp(s / ClipStep, 0f, Clip.Length - 1.001f);
                    int i = (int)t;
                    lo = Mathf.Lerp(Clip[i], Clip[i + 1], t - i);
                }
                return lo > -ExpresswayLayout.RampHalf + 0.01f ? lo : -Flare(s);
            }

            /// <summary>Is the ramp its own road here - clear of the deck altogether?</summary>
            public bool Clear(float s) => Inner(s) <= -ExpresswayLayout.RampHalf + 0.05f;
        }

        sealed class XwRoad     // an ordinary two-way road the expressway needed: an
        {                       // arterial through a diamond, a gate street at a branch
            public Vector3 A, B;
            public RoadNode NodeA, NodeB;
            public bool Boulevard;
        }

        ExpresswayLayout _xw;
        Transform _xwRoot;
        Material _xwPaint;
        TollPlaza _xwToll;
        DeckMesh.Skin _xwSkin;      // the motorway: the pack's structure, the town's road
        DeckMesh.Skin _xwGround;    // and the town's asphalt on its own, for the junctions
        readonly XwDeck[] _xwDecks = new XwDeck[2];
        readonly List<XwRamp> _xwRamps = new List<XwRamp>();
        readonly List<XwRoad> _xwRoads = new List<XwRoad>();
        readonly List<RoadNode> _xwSignals = new List<RoadNode>();
        readonly List<(Exchange ex, RoadNode inner, RoadNode outer)> _xwTerminals
            = new List<(Exchange, RoadNode, RoadNode)>();
        bool _xwReady;

        internal bool ExpresswayOn => _xwReady;

        // ------------------------------------------------------------------ building

        /// <summary>The whole road, laid with the other ground works: its line, its
        /// decks, its ramps, the streets it needs on the ground and the junctions all of
        /// those will hang off. The lane graph comes later (WireExpressway), off the
        /// same nodes, so the two cannot disagree.</summary>
        void BuildExpressway()
        {
            var route = expressway;
            if (route == null || !route.on) return;
            if (verticalRoadX == null || verticalRoadX.Length < 4 || horizontalRoadZ == null || horizontalRoadZ.Length < 3)
            {
                Debug.LogWarning("[expressway] the grid is too small to run a motorway round - nothing built.");
                return;
            }

            var kerb = GridKerbRect();
            _xw = ExpresswayLayout.Solve(route, verticalRoadX, horizontalRoadZ, kerb,
                                         spacingSeed, verticalIsBoulevard, horizontalIsBoulevard);
            if (_xw.Trunk == null || _xw.Trunk.Length < 600f)
            {
                Debug.LogWarning("[expressway] no room for a trunk - nothing built.");
                return;
            }

            _xwRoot = ((IDistrictHost)this).StaticRoot("Expressway");
            EnsureConnectorKit();
            _xwGround = DeckMesh.Flat(_bare);
            // the pack's concrete for what is concrete - parapet, kerb, edge beam,
            // soffit - and the town's own asphalt for the carriageway on top of it
            _xwSkin = DeckMesh.Probe(FreewayKit.TryLoad(FreewayKit.DeckPath)).Surfaced(_xwGround);

            PlanExpresswayEnds();
            PlanExpresswayToll();
            PlanExpresswayDecks();
            PlanExpresswayInterchanges();
            ReserveExpresswayGround();      // after the ramps: it holds the ground under them too

            MeasureRampClips();         // and with it, where each ramp starts to fall
            PlanRampTolls();            // which says how far up a ramp is still flat
            PlanDeckTolls();            // and how far along the trunk is still at grade
            MeasureRampWindows();       // read off the ramps' heights
            LayExpresswayDecks();
            LayExpresswayRamps();
            LayExpresswayGroundRoads();
            LayExpresswayPiers();
            LayExpresswayPaint();
            LayExpresswayToll();

            _xwReady = true;
            Debug.Log($"[expressway] {_xw.Why}; {_xwRamps.Count} ramps, {_xwRoads.Count} ground roads.");
        }

        /// <summary>The rectangle of the grid's outermost pavements.</summary>
        Rect GridKerbRect()
        {
            int lastV = verticalRoadX.Length - 1, lastH = horizontalRoadZ.Length - 1;
            float x0 = verticalRoadX[0] - VHalf(0) - Sidewalk;
            float x1 = verticalRoadX[lastV] + VHalf(lastV) + Sidewalk;
            float z0 = horizontalRoadZ[0] - HHalf(0) - Sidewalk;
            float z1 = horizontalRoadZ[lastH] + HHalf(lastH) + Sidewalk;
            return Rect.MinMaxRect(x0, z0, x1, z1);
        }

        /// <summary>A junction box of the expressway's own, square to the world.</summary>
        RoadNode XwNode(Vector3 at, float halfX, float halfZ, float setback, bool seam)
        {
            return new RoadNode
            {
                I = -9, J = -9,
                X = at.x, Z = at.z,
                XMin = at.x - halfX, XMax = at.x + halfX, ZMin = at.z - halfZ, ZMax = at.z + halfZ,
                StopSetback = setback,
                Seam = seam,
            };
        }

        /// <summary>What a carriageway is given at each end when there is no junction
        /// there to measure against. Only a fallback: the road is normally run on to the
        /// junction's own kerb (DeckMeets).</summary>
        const float EndMargin = 10f;

        /// <summary>Where a carriageway stops at the junction it dies in: ON that
        /// junction's kerb line, walked out of its box along the road's own line.
        ///
        /// A flat ten metres was the guess, and the note beside it said the junction
        /// reached across them. It does not. A street junction of this grid is 7.5 m of
        /// half width, and the trunk's own end stands a couple of metres OUTSIDE it - so
        /// the road stopped twelve and a half metres short of the kerb and ended in the
        /// grass, alongside the edge of the city's floor rather than on top of it. From
        /// the air the motorway ran into the side of the town and disappeared under it.
        /// </summary>
        float DeckMeets(XwDeck deck, RoadNode node, int toward)
        {
            float len = deck.Line.Length;
            float fallback = toward > 0 ? EndMargin : len - EndMargin;
            if (node == null) return fallback;
            deck.Line.Project(node.Centre, out float centre, out float off);
            // the junction has to be the one this end of the road actually arrives at
            if (off > 60f) return fallback;
            if (toward > 0 ? centre > 80f : centre < len - 80f) return fallback;
            for (float d = 0f; d <= 60f; d += 0.5f)
            {
                float at = Mathf.Clamp(centre + toward * d, 0f, len);
                var p = deck.Line.PointAt(at);
                if (p.x < node.XMin || p.x > node.XMax || p.z < node.ZMin || p.z > node.ZMax)
                    return at;
                if (at <= 0f || at >= len) return at;
            }
            return fallback;
        }

        /// <summary>A seam box on a deck: small, square, and standing in the road.</summary>
        RoadNode XwSeam(RoadLine line, float s)
        {
            var at = line.PointAt(s);
            return XwNode(at, 8f, 8f, 0.5f, seam: true);
        }

        // ---------------------------------------------------------------- the decks

        void PlanExpresswayDecks()
        {
            for (int k = 0; k < 2; k++)
            {
                int side = k == 0 ? +1 : -1;
                var line = side > 0
                    ? _xw.Trunk.Offset(+ExpresswayLayout.DeckOff)
                    : _xw.Trunk.Offset(-ExpresswayLayout.DeckOff).Reversed();
                _xwDecks[k] = new XwDeck { Side = side, Line = line };
            }

            foreach (var deck in _xwDecks)
            {
                // where the interchanges fall on THIS deck's own line, in travel order
                var stations = new List<(float s, int kind)>();   // kind 1 aux in, 2 fork, 3 merge, 4 aux out
                foreach (var ex in _xw.Exchanges)
                {
                    float sIn = DeckSOn(deck, ex.S - deck.Side * ExpresswayLayout.AuxIn);
                    float sFork = DeckSOn(deck, ex.S - deck.Side * ExpresswayLayout.Gore);
                    float sMerge = DeckSOn(deck, ex.S + deck.Side * ExpresswayLayout.Gore);
                    float sOut = DeckSOn(deck, ex.S + deck.Side * ExpresswayLayout.AuxOut);
                    stations.Add((sIn, 1)); stations.Add((sFork, 2));
                    stations.Add((sMerge, 3)); stations.Add((sOut, 4));
                    deck.AuxWindows.Add((ex.S, sIn, sFork));
                    deck.AuxWindows.Add((ex.S, sMerge, sOut));
                }
                stations.Sort((a, b) => a.s.CompareTo(b.s));

                // the ends: the road runs ON to the kerb line of the junction it
                // dies in, found by walking out of that junction's own box
                float head = DeckMeets(deck, TerminalNodeFor(deck, first: true), toward: +1);
                float tail = DeckMeets(deck, TerminalNodeFor(deck, first: false), toward: -1);
                var cuts = new List<(float s, int kind)> { (head, 0) };
                foreach (var st in stations)
                    if (st.s > head + 20f && st.s < tail - 20f) cuts.Add(st);
                cuts.Add((tail, 0));

                for (int i = 0; i + 1 < cuts.Count; i++)
                {
                    var a = cuts[i]; var b = cuts[i + 1];
                    var stretch = new XwStretch
                    {
                        S0 = a.s + (a.kind == 0 ? 0f : 2f),
                        S1 = b.s - (b.kind == 0 ? 0f : 2f),
                        Aux = a.kind == 1 || a.kind == 3,
                        AuxIsExit = a.kind == 1,
                    };
                    if (stretch.S1 - stretch.S0 < 12f) continue;
                    deck.Stretches.Add(stretch);
                }
                // and a seam box between each pair
                for (int i = 0; i + 1 < deck.Stretches.Count; i++)
                {
                    float at = (deck.Stretches[i].S1 + deck.Stretches[i + 1].S0) * 0.5f;
                    var n = XwSeam(deck.Line, at);
                    deck.Stretches[i].B = n;
                    deck.Stretches[i + 1].A = n;
                }
            }
        }

        readonly List<(RoadLine line, float s, float y, RoadNode node, bool avenue)> _xwTollNodes
            = new List<(RoadLine, float, float, RoadNode, bool)>();

        /// <summary>Where the money is taken: a barrier across the ARM, half way along
        /// the branch, which is where a 1987 expressway put one. Not on the mainline -
        /// the trunk's own run between two interchanges is four metres long once their
        /// ramps have had their share of it, and a plaza wants two hundred.</summary>
        void PlanExpresswayToll()
        {
            var go = new GameObject("Toll plaza");
            go.transform.SetParent(_traffic, false);
            _xwToll = go.AddComponent<TollPlaza>();
        }


        /// <summary>Trunk-s to this deck's own s: taken by projection, so the outside of
        /// a bend - which is longer than the line it was offset from - still has its
        /// stations in the right places.</summary>
        float DeckSOn(XwDeck deck, float trunkS)
        {
            var p = _xw.Trunk.Pose(Mathf.Clamp(trunkS, 0f, _xw.Trunk.Length), deck.Side * ExpresswayLayout.DeckOff);
            deck.Line.Project(p, out float s, out _);
            return s;
        }

        /// <summary>The surface of a carriageway at a station: the trunk's own profile,
        /// eased down to the height of the CITY's asphalt over the last few metres at
        /// each end. The motorway runs at a hand over the bed it is held flat to
        /// (GradeY) and the town's road cell is a flat piece laid at nought, so a road
        /// that arrives at its terminus on the motorway's own grade arrives over a kerb -
        /// at the one junction where every car on the road is turning.</summary>
        float DeckSurfaceY(XwDeck deck, float s)
        {
            float y = _xw.HeightAt(TrunkS(deck, s));
            float len = deck.Line.Length;
            float toEnd = Mathf.Min(s, len - s);
            if (toEnd < DeckLanding)
                y = Mathf.Lerp(ExpresswayLayout.StreetY, y, Mathf.Clamp01(toEnd / DeckLanding));
            return y;
        }

        /// <summary>Over what length it comes down that last hand's breadth. Long enough
        /// that the grade is nothing - twelve centimetres over twenty metres - and short
        /// enough to be inside the junction's own approach.</summary>
        const float DeckLanding = 20f;

        float TrunkS(XwDeck deck, float s)
        {
            _xw.Trunk.Project(deck.Line.PointAt(Mathf.Clamp(s, 0f, deck.Line.Length)), out float ts, out _);
            return ts;
        }

        /// <summary>How wide the deck is at a station: two lanes, or three where it is
        /// carrying an exit or an entrance, tapered in and out at the open end.</summary>
        Vector2 DeckWidth(XwDeck deck, float s)
        {
            float hi = ExpresswayLayout.DeckHalf;
            const float taper = 55f;
            foreach (var w in deck.AuxWindows)
            {
                if (s < w.lo - taper || s > w.hi + taper) continue;
                float t;
                if (s < w.lo) t = Mathf.InverseLerp(w.lo - taper, w.lo, s);
                else if (s > w.hi) t = Mathf.InverseLerp(w.hi + taper, w.hi, s);
                else t = 1f;
                hi = Mathf.Max(hi, Mathf.Lerp(ExpresswayLayout.DeckHalf, ExpresswayLayout.AuxHalf,
                                              Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t))));
            }
            // and a plaza's own apron, where the road opens out for the booth to stand
            // on. The same widening an auxiliary lane gets, for the same reason: there
            // is nowhere else on an eleven-metre carriageway to put a building.
            if (deck.TollNode != null)
            {
                float t = 1f - Mathf.InverseLerp(TollApronRun, TollApronRun + taper,
                                                 Mathf.Abs(s - deck.TollS));
                if (t > 0f)
                    hi = Mathf.Max(hi, Mathf.Lerp(ExpresswayLayout.DeckHalf, ExpresswayLayout.AuxHalf,
                                                  Mathf.SmoothStep(0f, 1f, t)));
            }
            return new Vector2(-ExpresswayLayout.DeckHalf, hi);
        }

        /// <summary>How much of the plaza's apron is at its full width before it tapers
        /// back into the carriageway.</summary>
        const float TollApronRun = 26f;

        /// <summary>The walls of a deck: NONE on the inside, because the two decks of a
        /// dual carriageway share one line of barrier between them and not a wall each
        /// (it is why the pack's own deck piece is drawn with a parapet on one edge and
        /// a bare one on the other); a parapet on the outside, except at a GORE, where
        /// it comes down and lets the ramp out. A wall standing between a deck and the
        /// ramp leaving it, at the very point they part, is a trench with two roads at
        /// the bottom of it - which is what this used to build.</summary>
        Vector2 DeckWall(XwDeck deck, float s)
        {
            float hi = DeckMesh.Parapet;
            // and the median: a concrete kerb down the inner edge of each deck, which
            // with the metre and a half between them IS the central reserve. Drawn into
            // the road rather than stood up out of a thousand barrier props - one of
            // which, in this pack, is an orange thing off a building site.
            const float median = 0.85f;
            // The outer parapet is DOWN exactly where a ramp is running beside the road,
            // and NOWHERE ELSE. A wall between a motorway and its own slip road, at the
            // place the two are still side by side, is a fence down the middle of a road;
            // a motorway with no parapet for half a mile either side of that is a viaduct
            // with nothing to stop a car going over the edge. Where the ramps actually
            // are is measured off the ramps (MeasureRampWindows), not guessed at.
            const float ease = 22f;
            if (_xwOpen.TryGetValue(deck, out var windows))
                foreach (var w in windows)
                {
                    if (s < w.from - ease || s > w.to + ease) continue;
                    float t = s < w.from ? Mathf.InverseLerp(w.from - ease, w.from, s)
                            : s > w.to ? Mathf.InverseLerp(w.to + ease, w.to, s)
                            : 1f;
                    hi = Mathf.Min(hi, Mathf.Lerp(DeckMesh.Parapet, 0f, Mathf.SmoothStep(0f, 1f, t)));
                }
            return new Vector2(median, hi);
        }

        readonly Dictionary<XwDeck, List<(float from, float to)>> _xwOpen
            = new Dictionary<XwDeck, List<(float, float)>>();

        /// <summary>Where each deck has a ramp running beside it, in its own stations:
        /// walked off the ramps themselves, so the parapet opens for exactly as long as
        /// there is a road on the other side of it and closes again the moment there is
        /// not. Also the auxiliary lanes' own stretches, where the ramp has not left yet
        /// but the lane it will leave by is already there.</summary>
        void MeasureRampWindows()
        {
            const float beside = 15f;      // a ramp this near the deck's edge is beside it
            foreach (var deck in _xwDecks)
            {
                // ONLY where a ramp is. The auxiliary lane's own stretches used to be in
                // this list as well, and an auxiliary lane starts 340 m before the gore
                // and runs 400 m past the entrance - so a third of a mile of viaduct
                // either side of every interchange stood with no railing at all, over a
                // seven-metre drop, because a lane had widened. A lane is not a ramp: the
                // parapet simply moves out with the edge it stands on.
                var windows = new List<(float from, float to)>();
                float edge = ExpresswayLayout.DeckHalf;
                foreach (var ramp in _xwRamps)
                {
                    float from = float.MaxValue, to = float.MinValue;
                    for (float rs = 0f; rs <= ramp.Line.Length; rs += 5f)
                    {
                        var p = ramp.Line.PointAt(rs);
                        if (!deck.Line.Near(p, 60f)) continue;
                        deck.Line.Project(p, out float ds, out float d);
                        if (ds < 1f || ds > deck.Line.Length - 1f) continue;
                        if (Mathf.Abs(d) - edge > beside) continue;
                        // and only where the two are at the same height: a ramp passing
                        // UNDER the deck is not beside it
                        if (Mathf.Abs(ramp.Lift(rs) - DeckSurfaceY(deck, ds)) > 1.5f) continue;
                        from = Mathf.Min(from, ds);
                        to = Mathf.Max(to, ds);
                    }
                    if (to > from) windows.Add((from, to));
                }
                windows.Sort((a, b) => a.from.CompareTo(b.from));
                var merged = new List<(float from, float to)>();
                foreach (var w in windows)
                {
                    if (merged.Count > 0 && w.from <= merged[merged.Count - 1].to + 30f)
                    {
                        var last = merged[merged.Count - 1];
                        merged[merged.Count - 1] = (last.from, Mathf.Max(last.to, w.to));
                    }
                    else merged.Add(w);
                }
                _xwOpen[deck] = merged;
            }
        }

        /// <summary>What a ramp carries down its outside: a parapet while it is in the
        /// air, and a kerb once it is on the ground. A metre of concrete wall standing
        /// beside a road at street level is a wall in a field, and at the mouth it would
        /// be a wall laid across the junction. Its inside carries nothing - along the
        /// gore that edge IS the deck's, and the two share one surface.</summary>
        static System.Func<float, Vector2> RampWalls(XwRamp ramp)
        {
            return s =>
            {
                // a parapet while the road is a bridge, a kerb once it is not, and
                // between the two it comes down with the road
                float t = Mathf.InverseLerp(RampAbutment, RampAbutment + 1.6f,
                                            ramp.Lift(s) - ExpresswayLayout.StreetY);
                return new Vector2(0f, Mathf.Lerp(RampKerb, DeckMesh.Parapet, t));
            };
        }

        /// <summary>And how high that kerb is. Never nought: a wall of no height is still
        /// a kerb to the section that draws it (DeckMesh.Section), and a ramp whose kerb
        /// blinks out of existence half way along has a hand's breadth of notch in the
        /// edge of its carriageway at the station where it goes.</summary>
        const float RampKerb = 0.12f;

        /// <summary>And how high the ramp stands where it stops being a bridge: its
        /// parapet has come down to that kerb by here (RampWalls), and it is as far up
        /// the ramp as a toll plaza can be put and still be on level ground
        /// (PlanRampTolls). What the road is MADE of does not change here - the
        /// carriageway is the town's asphalt the whole way and the concrete is kept for
        /// the concrete (DeckMesh.Skin.Surfaced), because a road that changes colour half
        /// way along is two roads.</summary>
        const float RampAbutment = 1f;

        /// <summary>And how high it has to stand before a pier is worth putting under
        /// it: below this the road is on an embankment, which is not drawn.</summary>
        const float PierWorth = 3.5f;

        /// <summary>Where each ramp is still running on the deck's own pavement, and by
        /// how much: walked once along every ramp and kept, because the answer is a
        /// projection down a two-kilometre polyline and the mesh asks for it a few
        /// hundred times a ramp.</summary>
        void MeasureRampClips()
        {
            ClipRamps();
            foreach (var ramp in _xwRamps) LevelRampAlongDeck(ramp);
            ClipRamps();     // the clip is read off the ramp's own height, which just moved
        }

        /// <summary>A ramp does not begin to fall at the nose. It is in a LANE of the
        /// motorway there, on the motorway's own slab, and a ramp that starts dropping at
        /// the gore puts a step down the middle of that slab - thirty centimetres by the
        /// time it is thirty metres along, a metre and a quarter by sixty, with the
        /// deck's edge beam standing in the daylight between them. It holds the deck's
        /// height for as long as it is beside it and does its climbing or its falling
        /// afterwards, on the length that is left.</summary>
        void LevelRampAlongDeck(XwRamp ramp)
        {
            var clip = ramp.Clip;
            if (clip == null || clip.Length < 2) return;
            bool On(int i) => clip[i] > -ExpresswayLayout.RampHalf + 0.05f;
            const float ease = 12f;                 // and a little past it, for the sag curve
            if (ramp.Falling)
            {
                // off the deck: the run from the nose that is still on the deck's slab
                int i = 0;
                while (i < clip.Length && On(i)) i++;
                if (i > 0) ramp.GradeFrom = (i - 1) * XwRamp.ClipStep + ease;
            }
            else
            {
                // on to it: the run back from the merge, which the climb must be over by
                int i = clip.Length - 1;
                while (i >= 0 && On(i)) i--;
                if (i < clip.Length - 1)
                    ramp.GradeTo = Mathf.Max(30f, (i + 1) * XwRamp.ClipStep - ease);
            }
        }

        // WHERE A BARRIER BELONGS.
        //
        // A toll works only where the road it stands on is a CUT: everybody who uses the
        // thing being charged for passes it exactly once, and nobody else passes at all.
        // On this road there is one such place and it is the BRANCH - the causeway out to
        // the port or the mainland. One way on, one way off, a barrier half way along it
        // (PlanDiamond), and it charges every car that crosses and no other car in the
        // city. That is the toll this road was planned with, and it is what the causeways
        // this city is drawn from charged for in 1987.
        //
        // The TRUNK is not a cut anywhere. Every interchange is a way on and a way off,
        // so a barrier across the carriageway would charge only the cars that happen to
        // be passing it and let everybody between two interchanges travel free - and
        // there is nowhere to put one in any case: the interchanges stand 800 m apart and
        // their auxiliary lanes take 340 m before the gore and 400 m after the merge,
        // which leaves FOUR METRES of open trunk between one and the next. A plaza wants
        // two hundred.
        //
        // Which leaves the ramps. A booth on every ramp ON to the motorway does work as a
        // system - a flat fare, paid once, by everyone who joins - but only if every way
        // on has one, and the trunk's two ends are junctions of the city (TerminalNodeFor)
        // that nothing charges. Built on the ramps alone it is a toll with a free door
        // beside it, and a stop at every entrance into the bargain. So it is off unless
        // it is asked for (expressway.rampTolls), and if it is ever turned on for good
        // the two termini want barriers of their own.

        /// <summary>The barriers on the ramps, when they are asked for: one on every ramp
        /// ON to the motorway, standing where the ramp is still low - half way to the
        /// point at which the road leaves the ground (Abutment) - and never nearer the
        /// junction than the kerb returns reach.</summary>
        void PlanRampTolls()
        {
            if (_xwToll == null || expressway == null || !expressway.tollRoad) return;
            foreach (var ramp in _xwRamps)
            {
                if (ramp.Falling) continue;              // money on the way in, not out
                if (ramp.Line.Length < TollClear * 4f) continue;
                float s = Mathf.Max(TollClear, ramp.Abutment(RampAbutment) * 0.5f);
                var at = ramp.Line.PointAt(s);
                var node = XwNode(at, TollBoxHalf, TollBoxHalf, 3f, seam: false);
                var gate = new TollGate
                {
                    Name = (ramp.Tag ?? "ramp") + " toll",
                    Dwell = 2.2f,
                    Node = node,
                };
                node.Toll = gate;
                _xwToll.Gates.Add(gate);
                ramp.TollNode = node;
                ramp.TollS = s;
            }
        }

        /// <summary>The other way on: the trunk's own two ends. Each carriageway begins
        /// at a junction of the town (TerminalNodeFor), and a car that joins there has
        /// passed no ramp at all - which is the free door beside the toll, and the reason
        /// a driver could come on at one interchange and off at the next for nothing.
        ///
        /// One barrier a carriageway, at its MOUTH, which is the end it begins at: the two
        /// decks run opposite ways, so their mouths are at opposite ends of the road and
        /// the pair of them close both. It stands half way along the run the trunk is
        /// still at street level for, so the plaza is on flat ground and not on the
        /// climb.</summary>
        void PlanDeckTolls()
        {
            if (_xwToll == null || expressway == null || !expressway.tollRoad) return;
            foreach (var deck in _xwDecks)
            {
                if (deck.Stretches.Count == 0) continue;
                var first = deck.Stretches[0];
                float s = Mathf.Max(TollClear, DeckFlatRun(deck) * 0.5f);
                if (s - TollBoxHalf < first.S0 + 1f || s + TollBoxHalf > first.S1 - 1f) continue;
                var at = deck.Line.PointAt(s);
                var node = XwNode(at, TollBoxHalf, TollBoxHalf, 3f, seam: false);
                var gate = new TollGate
                {
                    Name = $"entry plaza {(deck.Side > 0 ? "A" : "B")}",
                    Dwell = 2.2f,
                    Node = node,
                };
                node.Toll = gate;
                _xwToll.Gates.Add(gate);
                deck.TollNode = node;
                deck.TollS = s;
            }
        }

        /// <summary>How far this carriageway runs at street level from its own mouth
        /// before the trunk begins to climb.</summary>
        float DeckFlatRun(XwDeck deck)
        {
            for (float s = 0f; s < deck.Line.Length; s += 10f)
                if (_xw.HeightAt(TrunkS(deck, s)) > ExpresswayLayout.GradeY + 0.5f) return s;
            return deck.Line.Length;
        }

        /// <summary>How far up a ramp its plaza stands at the nearest, and how big the
        /// box it holds is. Twelve clears the junction's own kerb returns (KerbReturn,
        /// ten metres past the arterial's edge) and is comfortably more than the box's
        /// own half, so there is always a length of ramp between the terminal and the
        /// gate for the queue to stand on.</summary>
        const float TollClear = 12f, TollBoxHalf = 7f;

        /// <summary>Half a toll lane. Narrow - a car creeps through a plaza - and it is
        /// what leaves room on the mouth's own extra width for the island beside it.
        /// </summary>
        const float TollLaneHalf = 1.9f;

        /// <summary>And how far either way the island is marked off.</summary>
        const float TollIslandRun = 9f;

        /// <summary>How deep a line a car stops at is painted. Two feet: a metre of it,
        /// which is what the paint would draw at its shortest, is a white slab lying
        /// across the road rather than a line painted on it.</summary>
        const float StopLine = 0.6f;

        void ClipRamps()
        {
            foreach (var ramp in _xwRamps)
            {
                int n = Mathf.Max(2, Mathf.CeilToInt(ramp.Line.Length / XwRamp.ClipStep) + 1);
                var clip = new float[n];
                for (int i = 0; i < n; i++)
                {
                    float s = Mathf.Min(i * XwRamp.ClipStep, ramp.Line.Length);
                    float lo = -ExpresswayLayout.RampHalf;
                    var p = ramp.Line.PointAt(s);
                    var dir = ramp.Line.DirAt(s);
                    foreach (var deck in _xwDecks)
                    {
                        if (deck == null || !deck.Line.Near(p, 60f)) continue;
                        deck.Line.Project(p, out float ds, out float d);
                        // a ramp always leaves and joins on the RIGHT of its own deck, and
                        // runs the way that deck does: anything else is the other
                        // carriageway, whose measurements do not map on to this line
                        if (d <= 0f || ds < 1f || ds > deck.Line.Length - 1f) continue;
                        if (Vector3.Dot(dir, deck.Line.DirAt(ds)) < 0.5f) continue;
                        // and it has to be BESIDE the deck, not under it
                        if (Mathf.Abs(ramp.Lift(s) - DeckSurfaceY(deck, ds)) > 1.5f) continue;
                        // a finger's width outside the deck's edge, so the two slabs meet
                        // at a joint rather than fight for the same face
                        lo = Mathf.Max(lo, DeckWidth(deck, ds).y - d + 0.05f);
                    }
                    clip[i] = Mathf.Min(lo, ExpresswayLayout.RampHalf - 0.35f);
                }
                ramp.Clip = clip;
            }
        }

        void LayExpresswayDecks()
        {
            foreach (var deck in _xwDecks)
            {
                var d = deck;
                float len = d.Line.Length;
                // in pieces, so the far half of a two-kilometre road can be culled
                const float chunk = 160f;
                for (float s = EndMargin; s < len - EndMargin; s += chunk)
                {
                    float to = Mathf.Min(s + chunk, len - EndMargin);
                    DeckMesh.Build(d.Line, s, to,
                                   ss => DeckSurfaceY(d, ss),
                                   ss => DeckWidth(d, ss),
                                   ss => DeckWall(d, ss), _xwSkin, _xwRoot,
                                   $"Deck {(d.Side > 0 ? "A" : "B")} {s:F0}");
                }
            }
        }

        void LayExpresswayPiers()
        {
            var pillar = FreewayKit.TryLoad(FreewayKit.PillarPath);
            if (pillar == null) return;
            foreach (var deck in _xwDecks)
            {
                for (float s = 20f; s < deck.Line.Length - 20f; s += 20f)
                {
                    float y = DeckSurfaceY(deck, s);
                    if (y < 3.5f) continue;
                    var at = deck.Line.PointAt(s);
                    at.y = y - DeckMeshBeam;
                    if (!XwPierFree(at)) continue;
                    var dir = deck.Line.DirAt(s);
                    FreewayKit.StandPillar(pillar, at, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, _xwRoot);
                }
            }
        }

        /// <summary>How far the deck's own beam hangs below its road surface: a pier
        /// stands under the beam, not under the road.</summary>
        const float DeckMeshBeam = 1.5f;

        /// <summary>Where a pier may stand: never in a street of the grid, never in one
        /// of the expressway's own ground roads, and never in a ramp.</summary>
        bool XwPierFree(Vector3 p)
        {
            if (InAnyRoad(p)) return false;
            foreach (var r in _xwRoads)
            {
                var a = r.A; var b = r.B;
                if (NearSegment(p, a, b) < (r.Boulevard ? BoulevardHalf : StreetHalf) + 3f) return false;
            }
            foreach (var ramp in _xwRamps)
            {
                if (!ramp.Line.Near(p, ExpresswayLayout.RampHalf + 3f)) continue;
                ramp.Line.Project(p, out float s, out float d);
                if (s > 1f && s < ramp.Line.Length - 1f && Mathf.Abs(d) < ExpresswayLayout.RampHalf + 3f) return false;
            }
            return true;
        }

        static float NearSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            p.y = a.y = b.y = 0f;
            var ab = b - a;
            float len2 = ab.sqrMagnitude;
            float t = len2 > 1e-4f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2) : 0f;
            return Vector3.Distance(p, a + ab * t);
        }

        /// <summary>The lines. The pack bakes its own into a twenty-metre straight and
        /// there is no such piece for a road that bends, widens, and drops a lane four
        /// hundred metres later - so they are drawn: an edge line down each side of every
        /// carriageway, a broken line between the lanes, and another between the outside
        /// lane and the auxiliary one wherever there is one.</summary>
        void LayExpresswayPaint()
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            _xwPaint = new Material(lit) { name = "Road markings", color = new Color(0.93f, 0.92f, 0.86f) };
            if (_xwPaint.HasProperty("_Smoothness")) _xwPaint.SetFloat("_Smoothness", 0.05f);

            foreach (var deck in _xwDecks)
            {
                var d = deck;
                float len = d.Line.Length;
                for (float s = EndMargin; s < len - EndMargin; s += 300f)
                {
                    float to = Mathf.Min(s + 300f, len - EndMargin);
                    float lift(float ss) => DeckSurfaceY(d, ss);
                    DeckMesh.Paint(d.Line, s, to, lift, ss => -ExpresswayLayout.DeckHalf + 0.5f,
                                   0.16f, false, _xwPaint, _xwRoot, "Edge line");
                    DeckMesh.Paint(d.Line, s, to, lift, ss => DeckWidth(d, ss).y - 0.5f,
                                   0.16f, false, _xwPaint, _xwRoot, "Edge line");
                    DeckMesh.Paint(d.Line, s, to, lift, ss => 0f,
                                   0.14f, true, _xwPaint, _xwRoot, "Lane line");
                }
                // and the lane the exit is taken from, marked off from the through lanes
                foreach (var w in d.AuxWindows)
                    DeckMesh.Paint(d.Line, w.lo, w.hi, ss => DeckSurfaceY(d, ss),
                                   ss => (2.85f + ExpresswayLayout.AuxOff) * 0.5f,
                                   0.14f, true, _xwPaint, _xwRoot, "Auxiliary line");
            }

            foreach (var r in _xwRamps)
            {
                var ramp = r;
                float len = ramp.Line.Length;
                DeckMesh.Paint(ramp.Line, 2f, len - 2f, ramp.Lift,
                               _ => ExpresswayLayout.RampHalf - 0.5f,
                               0.16f, false, _xwPaint, _xwRoot, "Ramp edge");
                // the inner line only where there IS an inner edge. Along the gore the
                // ramp's own edge is the deck's, and the deck has already painted it -
                // a second line laid on top of it is two lines fighting for one face.
                float from = -1f, to = -1f;
                for (float s = 2f; s <= len - 2f; s += 4f)
                    if (ramp.Clear(s)) { if (from < 0f) from = s; to = s; }
                if (to > from + 10f)
                    DeckMesh.Paint(ramp.Line, from, to, ramp.Lift,
                                   ss => ramp.Inner(ss) + 0.5f,       // out with the mouth
                                   0.16f, false, _xwPaint, _xwRoot, "Ramp edge");

                // the mouth: the lane that waits to turn across the arterial marked off
                // from the one that does not, and - on the ramp that has to give way -
                // the line the pair of them stop at
                ramp.MouthRun(out float mouthFrom, out float mouthTo);
                if (mouthTo - mouthFrom > 12f)
                {
                    DeckMesh.Paint(ramp.Line, mouthFrom + 6f, mouthTo - 1.5f, ramp.Lift,
                                   _ => -ExpresswayLayout.RampHalf,
                                   0.14f, true, _xwPaint, _xwRoot, "Mouth lane line");
                    if (ramp.Falling)
                        DeckMesh.Paint(ramp.Line, mouthTo - StopLine - 0.8f, mouthTo - 0.8f, ramp.Lift,
                                       ss => (ramp.Inner(ss) + ExpresswayLayout.RampHalf) * 0.5f,
                                       XwRamp.MouthHalf + ExpresswayLayout.RampHalf - 0.6f,
                                       false, _xwPaint, _xwRoot, "Stop line");
                }
            }

        }

        /// <summary>The plaza itself: a boom over every LANE of the road it stands
        /// across and a booth on the island beside it, the arms lifting when the driver
        /// has paid (TollGate, RoadCar.CanEnter).
        ///
        /// Laid off THAT ROAD'S own lanes. The barrier stands half way along an
        /// arterial - which where the interchange is on an avenue is two carriageways of
        /// two lanes with fifteen metres of median between them - and a plaza laid to
        /// the MOTORWAY'S measurements instead (two lanes 5.7 m either side of one line)
        /// put every arm of it in that median and both booths on the grass, with the
        /// traffic going by untouched on both sides of the lot. Which is what it
        /// did.</summary>
        void LayExpresswayToll()
        {
            if (_xwToll == null) return;
            var boom = FreewayKit.TryLoad(FreewayKit.BoomPath);
            var booth = FreewayKit.TryLoad(FreewayKit.BoothPath);
            var live = new GameObject("Toll booms").transform;
            live.SetParent(_traffic, false);

            foreach (var t in _xwTollNodes)
            {
                var dir = t.line.DirAt(t.s);
                float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                var lanes = LaneOffsets(t.avenue);
                // the apron. A plaza is paved kerb to kerb; what the booths stood on was
                // the grass between two carriageways. Only the MEDIAN is laid - the
                // carriageways have their own tiles already, and those are not in the
                // cell map, so a bare cell put over one would fight it for the same face.
                if (t.avenue)
                {
                    var c = t.line.PointAt(t.s);
                    bool eastWest = Mathf.Abs(dir.x) >= Mathf.Abs(dir.z);
                    float med = BoulevardHalf - StreetKit.RoadHalf * 2f;   // its own kerbs
                    PaveFreewayJunction(c.x, c.z, eastWest ? TollApron : med, eastWest ? med : TollApron);
                }
                // Which side of a lane the island is on: the median, where an avenue has
                // one, and the kerb on a street, which is the only room there is. The
                // money is taken from the driver's left either way.
                float island = t.avenue ? -TollIsland : TollIsland;
                float boothOff = lanes[0] + island * 2f;
                for (int side = -1; side <= 1; side += 2)
                {
                    if (booth != null)
                    {
                        var at = t.line.Pose(t.s, side * boothOff);
                        at.y = t.y;
                        FreewayKit.Sit(booth, at, yaw, _xwRoot, "Toll booth");
                    }
                    if (boom == null) continue;
                    foreach (float lane in lanes)
                    {
                        // the post on the island beside the lane, the arm out across it
                        var at = t.line.Pose(t.s, side * (lane + island));
                        at.y = t.y;
                        StandXwBoom(boom, at, -side * Mathf.Sign(island), dir, live, t.node.Toll);
                    }
                }
            }
            foreach (var ramp in _xwRamps)
                if (ramp.TollNode != null) LayRampToll(ramp, boom, booth, live);
            foreach (var deck in _xwDecks)
                if (deck.TollNode != null) LayDeckToll(deck, boom, booth, live);
            int onRamps = 0, mouths = 0;
            foreach (var r in _xwRamps) if (r.TollNode != null) onRamps++;
            foreach (var d in _xwDecks) if (d.TollNode != null) mouths++;
            Debug.Log($"[expressway] toll: {_xwToll.Gates.Count} gate(s) - " +
                      $"{_xwTollNodes.Count} on the branch, {onRamps} on the ramps up " +
                      $"and {mouths} at the trunk's own ends; every way on is charged.");
        }

        /// <summary>The plaza at the mouth of a carriageway: a booth on the apron the road
        /// opens out into (DeckWidth), and an arm over each of the two lanes - both lifted
        /// by the one gate, because a gate lets one car through whichever lane he is
        /// in.</summary>
        void LayDeckToll(XwDeck deck, GameObject boom, GameObject booth, Transform live)
        {
            float s = deck.TollS;
            var line = deck.Line;
            var dir = line.DirAt(s);
            float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            float y = _xw.HeightAt(TrunkS(deck, s));
            float shoulder = DeckWidth(deck, s).y;
            if (booth != null)
            {
                // hard against the far edge of the apron, measured off the piece rather
                // than assumed: the apron is as wide as it is and the booth has to stand
                // ON it, not half over the drop at the end of it
                float half = FreewayKit.Measure(booth).size.x * 0.5f;
                var at = line.Pose(s, shoulder - half - 0.3f);
                at.y = y;
                FreewayKit.Sit(booth, at, yaw, _xwRoot, "Toll booth");
            }
            if (boom == null) return;
            // one arm a lane, each post standing at ITS OWN LANE'S far edge and the arm
            // reaching back across it. Posted on the near edge instead, an arm of four
            // metres covers the lane it stands beside and stops half way over the next.
            foreach (float lane in ExpresswayLayout.DeckLanes)
            {
                var stand = line.Pose(s, lane + TollLaneHalf);
                stand.y = y;
                StandXwBoom(boom, stand, -1f, dir, live, deck.TollNode.Toll);
            }
        }

        /// <summary>A plaza on an on-ramp: one lane, and the island on the driver's LEFT
        /// because that is the side his window is on and the side the money is taken
        /// from. The room for it is already there - the ramp's mouth opens out on its
        /// left (XwRamp.Flare) and the traffic lane stays where it was.</summary>
        void LayRampToll(XwRamp ramp, GameObject boom, GameObject booth, Transform live)
        {
            float s = ramp.TollS;
            var line = ramp.Line;
            var dir = line.DirAt(s);
            float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            float y = ramp.Lift(s);
            float post = -TollLaneHalf;                       // the island's near edge
            if (booth != null)
            {
                var at = line.Pose(s, post - TollIsland);
                at.y = y;
                FreewayKit.Sit(booth, at, yaw, _xwRoot, "Toll booth");
            }
            // the island's edge and the line the money is paid at. A booth on a shoulder
            // with nothing marked round it is a hut beside a road; what makes a plaza is
            // the paint that tells a driver which side of it to pass.
            float edge = ramp.Inner(s) + 0.5f;
            DeckMesh.Paint(line, s - TollIslandRun, s + TollIslandRun, ramp.Lift,
                           _ => post, 0.2f, false, _xwPaint, _xwRoot, "Toll island");
            DeckMesh.Paint(line, s - StopLine * 0.5f, s + StopLine * 0.5f, ramp.Lift,
                           _ => (post + ExpresswayLayout.RampHalf) * 0.5f,
                           ExpresswayLayout.RampHalf - post - 0.6f,
                           false, _xwPaint, _xwRoot, "Toll line");
            if (boom == null) return;
            var stand = line.Pose(s, post);
            stand.y = y;
            StandXwBoom(boom, stand, 1f, dir, live, ramp.TollNode.Toll);
        }

        /// <summary>Post to lane centre: the island a boom stands on, which is where
        /// the booth beside it stands too - one island further out again.</summary>
        const float TollIsland = 2.6f;
        /// <summary>And how far the plaza's own apron runs along the road: five cells of
        /// it, which is a car length either side of the arms.</summary>
        const float TollApron = 12.5f;

        /// <summary>One boom, its arm reaching across the lane. Which way the arm points
        /// out of its own pivot is measured off the prefab, and the lift turns about
        /// whichever local axis the arm actually lies on.</summary>
        void StandXwBoom(GameObject prefab, Vector3 at, float reach, Vector3 along, Transform parent, TollGate gate)
        {
            var b = FreewayKit.Measure(prefab);
            bool armAlongX = Mathf.Abs(b.center.x) >= Mathf.Abs(b.center.z);
            var across = new Vector3(along.z, 0f, -along.x) * reach;
            float yaw = armAlongX
                ? Mathf.Atan2(-across.z, across.x) * Mathf.Rad2Deg
                : Mathf.Atan2(across.x, across.z) * Mathf.Rad2Deg;
            if (armAlongX && b.center.x < 0f) yaw += 180f;
            if (!armAlongX && b.center.z < 0f) yaw += 180f;

            var go = FreewayKit.Sit(prefab, at, yaw, parent, "Toll boom");
            if (go == null) return;
            var axis = armAlongX ? Vector3.forward : Vector3.right;
            float lift = armAlongX ? (b.center.x >= 0f ? 75f : -75f) : (b.center.z >= 0f ? -75f : 75f);
            var arm = FreewayKit.BoomOf(go.transform);
            gate.Arms.Add(arm == null
                ? new TollArm(go.transform, axis, lift)
                : new TollArm(arm, Quaternion.Inverse(arm.localRotation) * axis, lift));
        }

        // -------------------------------------------------------- the interchanges

        void PlanExpresswayInterchanges()
        {
            foreach (var ex in _xw.Exchanges)
            {
                PlanDiamond(ex);
            }
        }

        /// <summary>A tight diamond: an arterial from the grid's edge junction out under
        /// the deck to a gate beyond it, a signalled terminal on it for each deck, and
        /// two ramps a deck between them and the motorway.</summary>
        void PlanDiamond(Exchange ex)
        {
            var right = _xw.Right(ex.S);
            var at = _xw.Trunk.PointAt(ex.S);
            float term = ExpresswayLayout.TerminalOff;
            bool leaf = ex.Kind == ExchangeKind.Branch;
            // The arterial is as wide as THE LINE IT CARRIES ON FROM. An avenue of 35 m
            // welded to a street of 15 at the city's edge junction puts its outer lanes
            // outside that junction's box, and the turns into them cross each other on
            // their way out: three cars locked together on the first hundred metres of
            // it, nine thousand refused steps, on the third city the tally tried.
            bool avenue = ex.Line >= 0 && ex.Line < verticalIsBoulevard.Length && verticalIsBoulevard[ex.Line];

            // the arterial runs across the trunk: from the grid's edge junction, past the
            // inner terminal, under the deck, past the outer one, to the gate - and where
            // the interchange is the way to a BRANCH, on out to whatever is at the end of
            // it, with the money taken half way along
            var gridNode = EdgeNodeOn(ex.Line, toward: -right);
            float gateOff = leaf
                ? -(ExpresswayLayout.SpurOff + Mathf.Max(120f, ex.Run))
                : -Mathf.Max(160f, term + 100f);

            float half = (avenue ? BoulevardHalf : StreetHalf) + 1.5f;
            // stopped WELL back from the box, the way the city's own junctions are: a
            // nose over the line is a nose in the boot of whatever is crossing, and a
            // touch of eighty centimetres is enough to put both cars into reverse and
            // lock them there for the rest of the run
            var inner = XwNode(at + right * term, TermHalfX(right, half), TermHalfZ(right, half), 5.7f, seam: false);
            var outer = XwNode(at + right * -term, TermHalfX(right, half), TermHalfZ(right, half), 5.7f, seam: false);
            var gate = XwNode(at + right * gateOff, TermHalfX(right, half), TermHalfZ(right, half), 3f, seam: false);
            _xwSignals.Add(inner); _xwSignals.Add(outer);
            _xwTerminals.Add((ex, inner, outer));

            if (gridNode != null) AddXwRoad(gridNode.Centre, inner.Centre, gridNode, inner, boulevard: avenue);
            AddXwRoad(inner.Centre, outer.Centre, inner, outer, boulevard: avenue);
            var along = _xw.Dir(ex.S);
            if (leaf)
            {
                // the branch: the arterial carries on out, with a barrier across it half
                // way. NOT four free-flowing ramps of its own - two of those have to
                // reach the far side of the motorway, and two roads crossing at grade
                // with nothing to say who goes first is three cars locked together and
                // eleven thousand refused steps, which is what the first shape of this
                // cost. A branch that starts at a signal is what a city does anyway.
                var mid = XwNode((outer.Centre + gate.Centre) * 0.5f, 11f, 11f, 3f, seam: false);
                if (_xwToll != null)
                {
                    var gateToll = new TollGate
                    {
                        Name = (ex.Name ?? "branch") + " plaza",
                        Dwell = 2.2f,
                        Node = mid,
                    };
                    mid.Toll = gateToll;
                    _xwToll.Gates.Add(gateToll);
                    _xwTollNodes.Add((RoadLine.Straight(outer.Centre, gate.Centre),
                                      Vector3.Distance(outer.Centre, gate.Centre) * 0.5f,
                                      ExpresswayLayout.GradeY, mid, avenue));
                }
                AddXwRoad(outer.Centre, mid.Centre, outer, mid, boulevard: avenue);
                AddXwRoad(mid.Centre, gate.Centre, mid, gate, boulevard: avenue);
            }
            else AddXwRoad(outer.Centre, gate.Centre, outer, gate, boulevard: avenue);

            // the street the arterial dies on, so nothing ends in the air. A crossroads
            // with an arm each way and nothing more: the loop of streets this used to get
            // was a place nobody had ever driven, and the cars filled it and locked
            // together in it.
            var g1 = XwNode(gate.Centre + along * 110f, 9f, 9f, 3f, seam: false);
            var g2 = XwNode(gate.Centre - along * 110f, 9f, 9f, 3f, seam: false);
            AddXwRoad(gate.Centre, g1.Centre, gate, g1, boulevard: false);
            AddXwRoad(gate.Centre, g2.Centre, gate, g2, boulevard: false);

            // and the ramps: off the deck 220 m before the arterial, on to it 220 m after
            foreach (var deck in _xwDecks)
            {
                var terminal = deck.Side > 0 ? inner : outer;
                RampOffDeck(deck, ex, terminal);
                RampOnToDeck(deck, ex, terminal);
            }
        }

        /// <summary>A ramp terminal's box: as wide as the arterial it stands on and as
        /// long as the ramp coming into it.</summary>
        static float TermHalfX(Vector3 right, float across) => Mathf.Abs(right.x) > 0.5f ? 11f : across;
        static float TermHalfZ(Vector3 right, float across) => Mathf.Abs(right.x) > 0.5f ? across : 11f;

        /// <summary>The grid junction the arterial of this interchange leaves, on the
        /// edge of the city nearest the motorway.</summary>
        RoadNode EdgeNodeOn(int line, Vector3 toward)
        {
            if (_nodes == null) return null;
            int j = toward.z > 0f ? horizontalRoadZ.Length - 1 : 0;
            if (line < 0 || line >= verticalRoadX.Length) return null;
            return _nodes[line, j];
        }

        void AddXwRoad(Vector3 a, Vector3 b, RoadNode na, RoadNode nb, bool boulevard)
        {
            if ((b - a).sqrMagnitude < 25f) return;
            _xwRoads.Add(new XwRoad { A = a, B = b, NodeA = na, NodeB = nb, Boulevard = boulevard });
        }

        /// <summary>A ramp DOWN off a deck: it leaves at the gore in the lane it has been
        /// given, runs alongside the motorway while it falls, and arrives at the terminal
        /// still pointing the way it was going - which is what a diamond's ramp does.</summary>
        void RampOffDeck(XwDeck deck, Exchange ex, RoadNode terminal)
        {
            float sFork = DeckSOn(deck, ex.S - deck.Side * ExpresswayLayout.Gore);
            var seam = SeamAt(deck, sFork);
            if (seam == null) return;
            var p0 = deck.Line.Pose(sFork + 2f, ExpresswayLayout.AuxOff);
            var d0 = deck.Line.DirAt(sFork);
            var p1 = ApproachPoint(terminal, d0, back: true);
            var line = Bezier(p0, d0, p1, d0, 0.5f, 0.4f);
            _xwRamps.Add(new XwRamp
            {
                Line = line, A = seam, B = terminal,
                Y0 = DeckSurfaceY(deck, sFork), Y1 = ExpresswayLayout.StreetY,
                Tag = $"Exit {ex.Number} {(deck.Side > 0 ? "A" : "B")}",
            });
        }

        /// <summary>And one UP on to it: from the terminal, climbing, to the gore where
        /// the deck has an acceleration lane waiting.</summary>
        void RampOnToDeck(XwDeck deck, Exchange ex, RoadNode terminal)
        {
            float sMerge = DeckSOn(deck, ex.S + deck.Side * ExpresswayLayout.Gore);
            var seam = SeamAt(deck, sMerge);
            if (seam == null) return;
            var d1 = deck.Line.DirAt(sMerge);
            var p1 = deck.Line.Pose(sMerge - 2f, ExpresswayLayout.AuxOff);
            var p0 = ApproachPoint(terminal, d1, back: false);
            var line = Bezier(p0, d1, p1, d1, 0.4f, 0.5f);
            _xwRamps.Add(new XwRamp
            {
                Line = line, A = terminal, B = seam,
                Y0 = ExpresswayLayout.StreetY, Y1 = DeckSurfaceY(deck, sMerge),
                Tag = $"Entry {ex.Number} {(deck.Side > 0 ? "A" : "B")}",
            });
        }

        /// <summary>Where a ramp meets a junction box, coming in (or going out) along a
        /// direction: on the box's edge, not its centre.</summary>
        static Vector3 ApproachPoint(RoadNode n, Vector3 dir, bool back)
        {
            var c = n.Centre;
            float hx = (n.XMax - n.XMin) * 0.5f, hz = (n.ZMax - n.ZMin) * 0.5f;
            var d = back ? -dir : dir;
            float tx = Mathf.Abs(d.x) > 1e-3f ? hx / Mathf.Abs(d.x) : float.MaxValue;
            float tz = Mathf.Abs(d.z) > 1e-3f ? hz / Mathf.Abs(d.z) : float.MaxValue;
            return c + d * Mathf.Min(tx, tz);
        }

        RoadNode SeamAt(XwDeck deck, float s)
        {
            RoadNode best = null;
            float bestD = 26f;
            foreach (var st in deck.Stretches)
            {
                foreach (var n in new[] { st.A, st.B })
                {
                    if (n == null) continue;
                    float d = Vector3.Distance(n.Centre, deck.Line.PointAt(s));
                    if (d < bestD) { bestD = d; best = n; }
                }
            }
            return best;
        }

        /// <summary>A ramp's line: a cubic through the two ends on their own headings.
        /// One curve for the whole thing - out of the gore, across, and into the
        /// terminal - so there is no place on it where the wheel has to be snatched.</summary>
        static RoadLine Bezier(Vector3 p0, Vector3 d0, Vector3 p3, Vector3 d3, float k0, float k1)
        {
            p0.y = p3.y = 0f;
            d0.y = d3.y = 0f;
            d0.Normalize(); d3.Normalize();
            float span = Vector3.Distance(p0, p3);
            var c0 = p0 + d0 * (span * k0);
            var c1 = p3 - d3 * (span * k1);
            int n = Mathf.Clamp(Mathf.CeilToInt(span / 4f), 8, 200);
            var pts = new List<Vector3>(n + 1);
            for (int i = 0; i <= n; i++)
            {
                float t = i / (float)n, u = 1f - t;
                pts.Add(u * u * u * p0 + 3f * u * u * t * c0 + 3f * u * t * t * c1 + t * t * t * p3);
            }
            return RoadLine.Through(pts);
        }


        // ------------------------------------------------------------ ground works

        /// <summary>The ground the road stands on: held flat and bare under the whole
        /// corridor and down every street the interchanges need, in a chain of squares
        /// along the line - which is what a reservation that only knows rectangles can
        /// be given of a road that bends.</summary>
        void ReserveExpresswayGround()
        {
            int rects = 0;
            void Hold(Vector3 at, float half)
            {
                var r = new Rect(at.x - half, at.z - half, half * 2f, half * 2f);
                _reservations.Level(r, RoadBed);
                _reservations.NoFlora(r);
                // HELD, not cut. Dropping the heightfield's cells under the road does
                // make it visible - and leaves a trench of open sea either side of it,
                // because a dropped cell is a cell the island does not draw at all. The
                // ground is levelled to the road bed instead: six centimetres under the
                // asphalt, which is what every other road in this city stands on.
                rects++;
            }
            void HoldLine(RoadLine line, float half, float step)
            {
                for (float s = 0f; s <= line.Length + step; s += step)
                {
                    float at = Mathf.Min(s, line.Length);
                    Hold(line.PointAt(at), half);
                }
            }
            // The WHOLE road, not just the trunk: a branch, a ramp, an arterial or a gate
            // street that nothing holds down has the island's own knolls rolling over it,
            // and a road at grade under a knoll is a road you cannot see. Squares rather
            // than one long rectangle because the road bends, and coarse ones because
            // every rectangle here is asked about at every sample of the heightfield.
            var lay = _xw;
            HoldLine(lay.Trunk, 48f, 30f);
            foreach (var r in _xwRamps) HoldLine(r.Line, 26f, 25f);
            foreach (var r in _xwRoads)
            {
                var a2 = r.A; var bb = r.B;
                float len = Vector3.Distance(a2, bb);
                int n = Mathf.Max(1, Mathf.CeilToInt(len / 30f));
                for (int i = 0; i <= n; i++) Hold(Vector3.Lerp(a2, bb, i / (float)n), 24f);
            }
            foreach (var ex in lay.Exchanges)
                for (float o = -ExpresswayLayout.ExchangeAcross; o <= ExpresswayLayout.ExchangeAcross; o += 50f)
                    for (float u = -ExpresswayLayout.ExchangeAlong * 0.5f; u <= ExpresswayLayout.ExchangeAlong * 0.5f; u += 50f)
                        Hold(lay.Trunk.Pose(Mathf.Clamp(ex.S + u, 0f, lay.Trunk.Length), o), 36f);
            Debug.Log($"[expressway] ground: {rects} squares held flat at {RoadBed:F2} m.");
        }

        /// <summary>The ramps' own asphalt, drawn the way the decks are: seven and a
        /// half metres of road with a wall down each side, falling on its own grade.</summary>
        void LayExpresswayRamps()
        {
            foreach (var r in _xwRamps)
            {
                var ramp = r;
                float len = ramp.Line.Length;
                Vector2 wide(float s) => new Vector2(ramp.Inner(s), ExpresswayLayout.RampHalf);
                var walls = RampWalls(ramp);
                // eight metres a section, not the sixteen a straight would be given: what
                // changes over the gore is the WIDTH, and the bend knows nothing about it
                for (float s = 0f; s < len; s += 120f)
                    DeckMesh.Build(ramp.Line, s, Mathf.Min(s + 120f, len), ramp.Lift, wide,
                                   walls, _xwSkin, _xwRoot, ramp.Tag, 8f);

                var pillar = FreewayKit.TryLoad(FreewayKit.PillarPath);
                if (pillar == null) continue;
                for (float s = 12f; s < len - 12f; s += 20f)
                {
                    float y = ramp.Lift(s);
                    if (y < PierWorth) continue;
                    var at = ramp.Line.PointAt(s);
                    at.y = y - DeckMeshBeam;
                    if (!XwPierFree(at)) continue;
                    var dir = ramp.Line.DirAt(s);
                    FreewayKit.StandPillar(pillar, at, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, _xwRoot);
                }
            }
        }

        /// <summary>The streets on the ground the interchanges need: the arterials, the
        /// gate streets, the branches' own divided roads.</summary>
        void LayExpresswayGroundRoads()
        {
            foreach (var r in _xwRoads)
            {
                bool alongX = Mathf.Abs(r.B.x - r.A.x) > Mathf.Abs(r.B.z - r.A.z);
                // An arterial carrying a whole interchange is an AVENUE, not a street:
                // two carriageways with a median between them, which is what the ramp
                // terminals need if a car is ever to wait to turn left across one.
                //
                // Each carriageway is centred on the MIDDLE OF THE LANES IT CARRIES,
                // taken off the same LaneOffsets the graph is built from, so the asphalt
                // and the lanes cannot drift apart. Laid at a number of its own it was
                // two and a half metres out, and a boulevard's inner lane ran with one
                // wheel over the kerb line.
                var lanes = LaneOffsets(r.Boulevard);
                float mid = (lanes[0] + lanes[lanes.Length - 1]) * 0.5f;
                float[] sides = r.Boulevard ? new[] { -mid, mid } : new[] { 0f };
                foreach (float side in sides)
                {
                    // and the strips either side of it, so the road is the width the town
                    // built it, all the way to the junction it leaves the town by
                    bool lo = !r.Boulevard || side < 0f, hi = !r.Boulevard || side > 0f;
                    if (alongX)
                    {
                        float x0 = Mathf.Min(r.A.x, r.B.x), x1 = Mathf.Max(r.A.x, r.B.x);
                        _connectorKit.LayRoadAlongX(r.A.z + side, x0, x1);
                        _connectorKit.LayShouldersAlongX(r.A.z + side, x0, x1, lo, hi);
                    }
                    else
                    {
                        float z0 = Mathf.Min(r.A.z, r.B.z), z1 = Mathf.Max(r.A.z, r.B.z);
                        _connectorKit.LayRoadAlongZ(r.A.x + side, z0, z1);
                        _connectorKit.LayShouldersAlongZ(r.A.x + side, z0, z1, lo, hi);
                    }
                }
            }
            PaveRampTerminals();
        }

        /// <summary>How tight a ramp terminal's kerb returns are: ten metres, which is
        /// what a lorry coming off a motorway needs to turn into an arterial without
        /// putting a wheel over the far kerb.</summary>
        const float KerbReturn = 10f;

        /// <summary>The junctions the interchanges stand on, surfaced as junctions: the
        /// arterial's width, the ramps' mouths, and a kerb return in each of the four
        /// corners between them (JunctionApron). What stood here was the grid's own
        /// rectangle of 5 m cells - eighteen metres by twenty-two, square at every
        /// corner, with four metres of bare asphalt in front of each ramp's mouth and
        /// seven more beside it.</summary>
        void PaveRampTerminals()
        {
            var skin = _xwGround;
            if (!skin.Real) return;
            foreach (var t in _xwTerminals)
            {
                PaveRampTerminal(t.inner, skin);
                PaveRampTerminal(t.outer, skin);
            }
        }

        void PaveRampTerminal(RoadNode node, DeckMesh.Skin skin)
        {
            if (node == null) return;
            var along = Vector3.zero;
            float half = StreetHalf;
            foreach (var r in _xwRoads)
            {
                if (r.NodeA != node && r.NodeB != node) continue;
                along = r.B - r.A;
                along.y = 0f;
                half = r.Boulevard ? BoulevardHalf : StreetHalf;
                break;
            }
            if (along.sqrMagnitude < 1e-6f) return;

            // how wide the ramps open where they meet it, in the ARTERIAL's own frame: a
            // ramp measures its width off its own line, and the two lines cross
            float lo = 0f, hi = 0f;
            bool any = false;
            foreach (var ramp in _xwRamps)
            {
                bool leaves = ramp.A == node, arrives = ramp.B == node;
                if (!leaves && !arrives) continue;
                float s = arrives ? ramp.Line.Length : 0f;
                var dir = ramp.Line.DirAt(s);
                float sign = Mathf.Sign(Vector3.Dot(new Vector3(dir.z, 0f, -dir.x), along));
                float a = ramp.Inner(s) * sign, b = ExpresswayLayout.RampHalf * sign;
                lo = Mathf.Min(lo, Mathf.Min(a, b));
                hi = Mathf.Max(hi, Mathf.Max(a, b));
                any = true;
            }
            if (!any) return;

            // a centimetre under both roads, so that where it runs beneath them it is
            // theirs that is seen and not two surfaces fighting for one face
            JunctionApron.Build(node.Centre, along, half, lo, hi, KerbReturn,
                                ExpresswayLayout.StreetY - 0.01f, skin, _xwRoot, "Ramp terminal");
        }

        // ------------------------------------------------------------------- lanes

        /// <summary>The expressway in the lane graph: the decks (one carriageway a
        /// stretch, so a stretch that carries an exit can have a lane the next one does
        /// not), the ramps, the branches and the streets under it - laid while BuildGraph
        /// is running and before its connectors are.</summary>
        void WireExpressway(LaneNet net)
        {
            if (!_xwReady || net == null) return;

            foreach (var deck in _xwDecks)
            {
                foreach (var st in deck.Stretches)
                {
                    if (st.A != null && !net.Nodes.Contains(st.A)) net.Nodes.Add(st.A);
                    if (st.B != null && !net.Nodes.Contains(st.B)) net.Nodes.Add(st.B);
                }
                if (deck.TollNode != null && !net.Nodes.Contains(deck.TollNode)) net.Nodes.Add(deck.TollNode);
            }
            foreach (var r in _xwRamps)
            {
                if (r.A != null && !net.Nodes.Contains(r.A)) net.Nodes.Add(r.A);
                if (r.B != null && !net.Nodes.Contains(r.B)) net.Nodes.Add(r.B);
                if (r.TollNode != null && !net.Nodes.Contains(r.TollNode)) net.Nodes.Add(r.TollNode);
            }
            foreach (var r in _xwRoads)
            {
                if (r.NodeA != null && !net.Nodes.Contains(r.NodeA)) net.Nodes.Add(r.NodeA);
                if (r.NodeB != null && !net.Nodes.Contains(r.NodeB)) net.Nodes.Add(r.NodeB);
            }

            // the decks
            foreach (var deck in _xwDecks)
            {
                var d = deck;
                RoadNode head = TerminalNodeFor(d, first: true);
                RoadNode tail = TerminalNodeFor(d, first: false);
                void Piece(XwStretch st, float s0, float s1, RoadNode a, RoadNode b)
                {
                    if (s1 - s0 < 1f) return;
                    var line = d.Line.Sub(s0, s1);
                    var offs = st.Aux
                        ? new[] { -2.85f, 2.85f, ExpresswayLayout.AuxOff }
                        : ExpresswayLayout.DeckLanes;
                    var road = net.AddCurve(line, ExpresswayLayout.DeckHalf, offs, ExpresswayLayout.DeckSpeed,
                                            a, b, oneWay: true, cls: RoadClass.Freeway);
                    road.HalfPlus = st.Aux ? ExpresswayLayout.AuxHalf : ExpresswayLayout.DeckHalf;
                    road.HalfMinus = ExpresswayLayout.DeckHalf;
                    // trunk-s along this stretch, as a straight line between its ends:
                    // a projection down a two-kilometre polyline is not a thing to do for
                    // every car every frame, and over one stretch the two agree to a metre
                    float t0 = TrunkS(d, s0), t1 = TrunkS(d, s1);
                    float span = Mathf.Max(1f, s1 - s0);
                    var lay = _xw;
                    road.SurfaceY = lay.HeightAt(t0);
                    road.SurfaceAt = s => lay.HeightAt(Mathf.Lerp(t0, t1, Mathf.Clamp01(s / span)));
                    road.Elevated = lay.Elevated((t0 + t1) * 0.5f);
                    if (st.Aux)
                    {
                        var aux = road.LaneFor(+1, ExpresswayLayout.AuxOff);
                        if (aux != null) { aux.Auxiliary = true; aux.Exit = st.AuxIsExit; }
                    }
                }
                for (int i = 0; i < d.Stretches.Count; i++)
                {
                    var st = d.Stretches[i];
                    var a = st.A ?? head;
                    var b = st.B ?? tail;
                    // the plaza at the mouth is a BOX in the graph, so the stretch it
                    // stands in is two roads with the box between them
                    if (d.TollNode != null && st.S0 < d.TollS && d.TollS < st.S1)
                    {
                        Piece(st, st.S0, d.TollS - TollBoxHalf, a, d.TollNode);
                        Piece(st, d.TollS + TollBoxHalf, st.S1, d.TollNode, b);
                    }
                    else Piece(st, st.S0, st.S1, a, b);
                }
            }

            // the ramps - in two pieces where one carries a toll, because a gate is a
            // BOX in the graph and a box has to have a road either side of it
            foreach (var r in _xwRamps)
            {
                if (r.A == null || r.B == null) continue;
                var ramp = r;
                void Piece(float s0, float s1, RoadNode a, RoadNode b)
                {
                    if (s1 - s0 < 1f) return;
                    bool whole = s0 <= 0f && s1 >= ramp.Line.Length;
                    var line = whole ? ramp.Line : ramp.Line.Sub(s0, s1);
                    var road = net.AddCurve(line, ExpresswayLayout.RampHalf, RampLane,
                                            ExpresswayLayout.RampSpeed, a, b, oneWay: true, cls: RoadClass.Ramp);
                    road.SurfaceY = ramp.Lift(s0);
                    road.SurfaceAt = ss => ramp.Lift(s0 + ss);
                    road.Elevated = Mathf.Max(ramp.Lift(s0), ramp.Lift(s1)) > 2.5f;
                }
                if (ramp.TollNode == null) Piece(0f, ramp.Line.Length, r.A, r.B);
                else
                {
                    Piece(0f, ramp.TollS - TollBoxHalf, r.A, ramp.TollNode);
                    Piece(ramp.TollS + TollBoxHalf, ramp.Line.Length, ramp.TollNode, r.B);
                }
            }

            // and the streets on the ground
            foreach (var r in _xwRoads)
            {
                if (r.NodeA == null || r.NodeB == null) continue;
                var a = ApproachPoint(r.NodeA, r.B - r.A, back: false);
                var bb = ApproachPoint(r.NodeB, r.B - r.A, back: true);
                if ((bb - a).sqrMagnitude < 16f) continue;
                bool northSouth = Mathf.Abs(r.B.z - r.A.z) > Mathf.Abs(r.B.x - r.A.x);
                // a SHORT link between two signals is signed slower than an open
                // avenue: a hundred metres of road between two red lights is not a
                // place to be doing thirteen metres a second at
                float len2 = Vector3.Distance(a, bb);
                float limit = r.Boulevard ? boulevardSpeed : streetSpeed;
                if (len2 < 160f) limit = Mathf.Min(limit, streetSpeed);
                var road = net.AddRoad(a, bb,
                                       r.Boulevard ? BoulevardHalf : StreetHalf,
                                       LaneOffsets(r.Boulevard),
                                       limit,
                                       r.NodeA, r.NodeB, northSouth,
                                       r.Boulevard ? 5f : 0f);
                road.ParkingA = road.ParkingB = false;
                road.Class = r.Boulevard ? RoadClass.Boulevard : RoadClass.Street;
            }
        }

        static readonly float[] RampLane = { 0f };

        /// <summary>The junction a deck begins or ends in: the grid's own at the town
        /// end, the branch's gate at the other.</summary>
        RoadNode TerminalNodeFor(XwDeck deck, bool first)
        {
            // deck +1 runs from the far branch to the town; deck -1 the other way
            bool townEnd = (deck.Side > 0) == !first;
            return townEnd ? _xwTownNode : _xwHeadNode;
        }

        RoadNode _xwTownNode, _xwHeadNode;

        /// <summary>The two ends of the whole road, made before the decks are wired: the
        /// city's own edge junction at one, a gate street at the other.</summary>
        void PlanExpresswayEnds()
        {
            int lastV = verticalRoadX.Length - 1;
            int tl = Mathf.Clamp(_xw.TerminusLine, 0, horizontalRoadZ.Length - 1);
            _xwTownNode = _nodes != null ? _nodes[lastV, tl] : null;
            if (_xwTownNode == null)
                _xwTownNode = XwNode(_xw.Trunk.PointAt(_xw.Trunk.Length), 16f, 16f, 4f, seam: false);

            // the far end is a junction of the grid too, on the line the trunk comes
            // off: the road runs from one edge of the city to the other
            int bl = Mathf.Clamp(_xw.BranchLine, 0, verticalRoadX.Length - 1);
            int lastH = horizontalRoadZ.Length - 1;
            _xwHeadNode = _nodes != null ? _nodes[bl, lastH] : null;
            if (_xwHeadNode == null)
                _xwHeadNode = XwNode(_xw.Trunk.PointAt(0f), 13f, 13f, 3f, seam: false);
        }

        // ----------------------------------------------------------------- signals

        /// <summary>The expressway's own signalled junctions: the ramp terminals on each
        /// arterial and the gate every branch dies on. The grid's signals are built off
        /// its array of junctions and these are not in it.</summary>
        void SignalExpressway()
        {
            if (!_xwReady || _xwSignals.Count == 0) return;
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var housing = new Material(lit) { color = new Color(0.16f, 0.16f, 0.17f) };
            if (housing.HasProperty("_Smoothness")) housing.SetFloat("_Smoothness", 0.35f);

            // A DIAMOND RUNS ON ONE CONTROLLER. Its two terminals stand 116 m apart with
            // the motorway between them; given a signal each, they go red at different
            // moments and the road between them becomes a box a queue is trapped in -
            // it fills, and the next car released from the far terminal arrives at nine
            // metres a second on a line of cars that has nowhere to go. That was fifteen
            // thousand refused steps on the twelfth city the tally tried.
            foreach (var t in _xwTerminals)
            {
                if (t.inner == null || t.outer == null) continue;
                var pair = new TrafficSignal(Mathf.Abs(t.inner.X * 7f + t.inner.Z * 13f) % TrafficSignal.Cycle);
                t.inner.Signal = pair;
                t.outer.Signal = pair;
                _signals.Add(pair);
            }

            foreach (var n in _xwSignals)
            {
                if (n == null || n.Incoming.Count == 0) continue;
                var sig = n.Signal;
                if (sig == null) sig = new TrafficSignal(Mathf.Abs(n.X * 7f + n.Z * 13f) % TrafficSignal.Cycle);
                bool fresh = n.Signal == null;
                n.Signal = sig;
                if (fresh) _signals.Add(sig);
                var seen = new List<Vector3>();
                foreach (var e in n.Incoming)
                {
                    var d = e.DirIn.sqrMagnitude > 0.1f ? e.DirIn : e.Dir;
                    bool had = false;
                    foreach (var s in seen) if (Vector3.Dot(s, d) > 0.9f) { had = true; break; }
                    if (had) continue;
                    seen.Add(d);
                    XwSignalHead(n, d, sig, housing);
                }
            }
        }

        /// <summary>One signal head on the corner of a box, facing the cars coming at
        /// it: the same three bulbs the grid's junctions carry, on a plain post.</summary>
        void XwSignalHead(RoadNode n, Vector3 d, TrafficSignal sig, Material housing)
        {
            var right = new Vector3(d.z, 0f, -d.x);
            float hx = (n.XMax - n.XMin) * 0.5f + 2f, hz = (n.ZMax - n.ZMin) * 0.5f + 2f;
            var corner = n.Centre + d * (Mathf.Abs(d.x) * hx + Mathf.Abs(d.z) * hz)
                                  + right * (Mathf.Abs(right.x) * hx + Mathf.Abs(right.z) * hz);
            var face = -d;
            var head = corner + Vector3.up * 4.05f - right * 2.4f;

            var pole = new GameObject("Expressway signal").transform;
            pole.SetParent(_traffic, false);
            pole.SetPositionAndRotation(corner, Quaternion.LookRotation(face, Vector3.up));

            var mast = GameObject.CreatePrimitive(PrimitiveType.Cube);
            mast.name = "Post";
            mast.transform.SetParent(pole, false);
            mast.transform.position = corner + Vector3.up * 2.6f;
            mast.transform.localScale = new Vector3(0.22f, 5.2f, 0.22f);
            Destroy(mast.GetComponent<Collider>());
            mast.GetComponent<MeshRenderer>().sharedMaterial = housing;

            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = "Head";
            box.transform.SetParent(pole, false);
            box.transform.SetPositionAndRotation(head, Quaternion.LookRotation(face));
            box.transform.localScale = new Vector3(0.55f, 1.55f, 0.22f);
            Destroy(box.GetComponent<Collider>());
            box.GetComponent<MeshRenderer>().sharedMaterial = housing;

            var set = new TrafficSignal.BulbSet { NorthSouth = Mathf.Abs(d.z) > 0.5f };
            for (int k = 0; k < 3; k++)
            {
                var bulb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                bulb.name = k == 0 ? "Red" : k == 1 ? "Yellow" : "Green";
                bulb.transform.SetParent(pole, false);
                bulb.transform.position = head + Vector3.up * (0.45f - 0.45f * k) + face * 0.16f;
                bulb.transform.localScale = Vector3.one * 0.34f;
                Destroy(bulb.GetComponent<Collider>());
                var r = bulb.GetComponent<MeshRenderer>();
                if (k == 0) set.R = r; else if (k == 1) set.Y = r; else set.G = r;
            }
            sig.AddBulbs(set);
        }


        /// <summary>What the console says when the road is built.</summary>
        internal string ExpresswayStory()
            => _xwReady ? _xw.Why : "no expressway";
    }
}
