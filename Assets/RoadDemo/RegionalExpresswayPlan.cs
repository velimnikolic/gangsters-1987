using System;
using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>Regional road composition using the ExpresswayDemo curve/deck/ramp contract.</summary>
    public sealed class RegionalExpresswayPlan
    {
        public const float Band = 166f, Terminal = 90f, DeckHeight = 7f;
        public sealed class Access
        {
            public CityEdge Edge;
            public RoadNode Node;
            public Vector3 Face;
            public bool Outlying;
            public CoreRegion.Connection Connection;
        }
        public sealed class GroundRoad
        {
            public RoadNode A, B;
            public Vector3 From, To;
            public RoadLine Path;
            RoadLine _straight;
            public RoadLine Line => Path ?? (_straight ??= RoadLine.Straight(From, To));
            public Access Access;
        }
        public sealed class Deck
        {
            public RoadLine Line;
            public readonly List<float> Exchanges = new List<float>();
            public readonly List<float> Bridges = new List<float>();
            public float ChannelHalf;
            public float Height(float s)
            {
                float height = DeckHeight;
                foreach (float at in Bridges)
                    height = Mathf.Max(height, DeckHeight + 16f * Mathf.SmoothStep(0f, 1f,
                        Mathf.InverseLerp(ChannelHalf + 475f, ChannelHalf + 115f, Mathf.Abs(Delta(s, at, Line.Length)))));
                return height;
            }
            public float Half(float s)
            {
                float half = ExpresswayLayout.DeckHalf;
                foreach (float at in Exchanges)
                {
                    float delta = Delta(s, at, Line.Length);
                    float exit = Mathf.Min(Mathf.Clamp01((delta + ExpresswayLayout.AuxOut) / 75f),
                        Mathf.Clamp01((-ExpresswayLayout.Gore + 65f - delta) / 65f));
                    float entry = Mathf.Min(Mathf.Clamp01((delta - ExpresswayLayout.Gore + 65f) / 65f),
                        Mathf.Clamp01((ExpresswayLayout.AuxOut - delta) / 75f));
                    float blend = Mathf.Max(exit, entry);
                    half = Mathf.Max(half, Mathf.Lerp(ExpresswayLayout.DeckHalf, ExpresswayLayout.AuxHalf, blend));
                }
                return half;
            }
            public float Wall(float s)
            {
                foreach (float at in Exchanges)
                    if (Mathf.Abs(Delta(s, at - ExpresswayLayout.Gore, Line.Length)) < 90f ||
                        Mathf.Abs(Delta(s, at + ExpresswayLayout.Gore, Line.Length)) < 90f) return 0f;
                return DeckMesh.Parapet;
            }
        }
        public sealed class Ramp
        {
            public RoadLine Line;
            public Deck Deck;
            public RoadNode A, B;
            public bool Off;
            public float DeckLevel;
            public float Height(float s)
            {
                float flat = Mathf.Min(65f, Line.Length * 0.25f);
                return Off ? ExpresswayLayout.Grade(DeckLevel, 0f, flat, Line.Length, s)
                    : ExpresswayLayout.Grade(0f, DeckLevel, 0f, Line.Length - flat, s);
            }
            public Vector2 Width(float s)
            {
                Deck.Line.Project(Line.PointAt(s), out float ds, out float d);
                float aligned = Vector3.Dot(Line.RightAt(s), Deck.Line.RightAt(ds));
                float inner = -ExpresswayLayout.RampHalf;
                if (aligned > 0.5f && d > 0f && d < 24f)
                    inner = Mathf.Clamp((Deck.Half(ds) - d) / aligned, inner, ExpresswayLayout.RampHalf - 0.05f);
                return new Vector2(inner, ExpresswayLayout.RampHalf);
            }
        }

        public readonly List<Deck> Decks = new List<Deck>();
        public readonly List<Ramp> Ramps = new List<Ramp>();
        public readonly List<GroundRoad> Ground = new List<GroundRoad>();
        public readonly List<RoadNode> Junctions = new List<RoadNode>();
        public readonly Rect Bounds;
        readonly LaneNet _net;
        readonly Dictionary<(int, int), RoadNode> _groundNodes = new Dictionary<(int, int), RoadNode>();

        public RegionalExpresswayPlan(Rect city, IReadOnlyList<Access> access, LaneNet net, int seed, Rect river)
        {
            _net = net;
            var ring = Rect.MinMaxRect(city.xMin - Band, city.yMin - Band, city.xMax + Band, city.yMax + Band);
            Bounds = Rect.MinMaxRect(ring.xMin - 160f, ring.yMin - 160f, ring.xMax + 160f, ring.yMax + 160f);
            float radius = Mathf.Min(240f + new System.Random(seed ^ 0x45585052).Next(5) * 15f,
                Mathf.Min(ring.width, ring.height) * 0.24f);
            // The complete river span and its abutments must lie on a straight deck.
            // Widen the ring when the river is near a rounded corner.
            if (river.width > 0f)
            {
                ring.xMin = Mathf.Min(ring.xMin, river.xMin - radius - 160f);
                ring.xMax = Mathf.Max(ring.xMax, river.xMax + radius + 160f);
            }
            Bounds = Rect.MinMaxRect(ring.xMin - 160f, ring.yMin - 160f, ring.xMax + 160f, ring.yMax + 160f);
            var points = new List<Vector3>();
            RoadLine.Corner(points, new Vector3(ring.xMin, 0, ring.yMin), Vector3.left, Vector3.forward, radius);
            RoadLine.Corner(points, new Vector3(ring.xMin, 0, ring.yMax), Vector3.forward, Vector3.right, radius);
            RoadLine.Corner(points, new Vector3(ring.xMax, 0, ring.yMax), Vector3.right, Vector3.back, radius);
            RoadLine.Corner(points, new Vector3(ring.xMax, 0, ring.yMin), Vector3.back, Vector3.left, radius);
            points.Add(points[0]);
            var trunk = RoadLine.Through(points, Vector3.left, Vector3.left);
            Decks.Add(new Deck { Line = trunk.Offset(ExpresswayLayout.DeckOff) });
            Decks.Add(new Deck { Line = trunk.Offset(-ExpresswayLayout.DeckOff).Reversed() });
            foreach (var deck in Decks)
            {
                deck.ChannelHalf = river.width * 0.5f;
                if (river.width <= 0f) continue;
                // Preserve the existing tall river boat's sailing corridor. Grade
                // the road up to a fixed high bridge; no bridge/boat rules change.
                for (float s = 0f; s < deck.Line.Length; s += 8f)
                {
                    float end = Mathf.Min(s + 8f, deck.Line.Length);
                    float a = deck.Line.PointAt(s).x - river.center.x, b = deck.Line.PointAt(end).x - river.center.x;
                    if (a * b < 0f || (a == 0f && b != 0f))
                        deck.Bridges.Add(Mathf.Lerp(s, end, Mathf.Abs(a) / (Mathf.Abs(a) + Mathf.Abs(b))));
                }
            }
            foreach (CityEdge edge in new[] { CityEdge.North, CityEdge.East, CityEdge.South, CityEdge.West })
            {
                var entries = new List<Access>();
                foreach (var a in access) if (a.Edge == edge) entries.Add(a);
                if (entries.Count == 0) continue;
                bool ns = CoreRegion.Vertical(edge);
                float across = ns ? city.center.x : city.center.y;
                if (ns)
                    foreach (var a in entries)
                        if (!a.Outlying)
                        { across = Mathf.Clamp(a.Face.x, ring.xMin + radius + 30f, ring.xMax - radius - 30f); break; }
                if (ns && river.width > 0f)
                    across = river.center.x < city.center.x ? ring.xMax - radius - 30f : ring.xMin + radius + 30f;
                float closest = 40f, snapped = across;
                foreach (var a in entries)
                {
                    float axis = CoreRegion.Across(edge, a.Face), distance = Mathf.Abs(axis - across);
                    if (distance >= closest) continue;
                    closest = distance; snapped = axis;
                }
                across = snapped; // share the junction instead of drawing a zero-length link between touching pads
                var outward = RasterGateways.Outward(edge);
                float edgeAt = edge == CityEdge.North ? city.yMax : edge == CityEdge.South ? city.yMin
                    : edge == CityEdge.East ? ring.xMax - Band : ring.xMin + Band;
                var at = ns ? new Vector3(across, 0, edgeAt) : new Vector3(edgeAt, 0, across);
                var inner = Node(at + outward * (Band - Terminal));
                var outer = Node(at + outward * (Band + Terminal));
                // Local collectors sit behind the ramp terminals. Putting them
                // on the terminals would overlap their east/west ramp mouths.
                var innerRoad = Node(at + outward * (Band - Terminal - 40f));
                var outerRoad = Node(at + outward * (Band + Terminal + 40f));
                AddGround(innerRoad, inner); AddGround(inner, outer); AddGround(outer, outerRoad);
                var inside = new List<RoadNode> { innerRoad };
                var outside = new List<RoadNode> { outerRoad };
                foreach (var a in entries)
                {
                    float axis = CoreRegion.Across(edge, a.Face);
                    var anchor = ns ? new Vector3(axis, 0, edgeAt) : new Vector3(edgeAt, 0, axis);
                    var node = Node(anchor + outward * (a.Outlying ? Band + Terminal + 40f : Band - Terminal - 40f));
                    var list = a.Outlying ? outside : inside;
                    if (!list.Contains(node)) list.Add(node);
                    Vector3 face = Face(node, a.Face - new Vector3(node.X, 0, node.Z));
                    Ground.Add(new GroundRoad { A = node, B = a.Node, From = face, To = a.Face, Access = a });
                }
                ConnectCollector(inside, ns); ConnectCollector(outside, ns);
                foreach (var deck in Decks)
                {
                    deck.Line.Project(at + outward * Band, out float s, out _);
                    // Both decks use their right-hand terminal: diamonds pass beneath
                    // the viaduct and never put an at-grade crossing on the mainline.
                    var right = deck.Line.RightAt(s);
                    var terminal = Vector3.Dot(right, outward) > 0 ? outer : inner;
                    deck.Exchanges.Add(s);
                    PlanRamps(deck, s, terminal);
                }
            }
            RegionalCollectors.Round(this, net);
            foreach (var deck in Decks) WireDeck(deck);
            foreach (var ramp in Ramps)
            {
                var r = ramp;
                var road = net.AddCurve(r.Line, ExpresswayLayout.RampHalf, new[] { 0f },
                    ExpresswayLayout.RampSpeed, r.A, r.B, true, RoadClass.Ramp);
                road.SurfaceAt = r.Height; road.SurfaceY = r.Height(0); road.Elevated = true;
            }
            foreach (var road in Ground)
                if (road.Path == null) RegionalRoads.Link(net, road.A, road.B, road.From, road.To, ExpresswayLayout.ArterialSpeed);
                else
                {
                    var curve = net.AddCurve(road.Path, StreetKit.StreetHalf, new[] { 2.5f },
                        9f, road.A, road.B, false);
                    curve.ParkingA = curve.ParkingB = false;
                }
        }

        RoadNode Node(Vector3 at)
        {
            var key = (Mathf.RoundToInt(at.x * 10f), Mathf.RoundToInt(at.z * 10f));
            if (_groundNodes.TryGetValue(key, out var node)) return node;
            node = _net.AddNode(at.x, at.z, StreetKit.StreetHalf, StreetKit.StreetHalf, 3f);
            _groundNodes.Add(key, node); Junctions.Add(node);
            return node;
        }
        static Vector3 Face(RoadNode node, Vector3 dir) => RasterGateways.Face(node, dir.normalized);
        void AddGround(RoadNode a, RoadNode b)
        {
            if (a == b) return;
            var direction = new Vector3(b.X - a.X, 0, b.Z - a.Z);
            Ground.Add(new GroundRoad { A = a, B = b, From = Face(a, direction), To = Face(b, -direction) });
        }
        void ConnectCollector(List<RoadNode> nodes, bool ns)
        {
            nodes.Sort((a, b) => (ns ? a.X : a.Z).CompareTo(ns ? b.X : b.Z));
            for (int i = 1; i < nodes.Count; i++) AddGround(nodes[i - 1], nodes[i]);
        }

        readonly Dictionary<(Deck, int), RoadNode> _seams = new Dictionary<(Deck, int), RoadNode>();
        static float Delta(float s, float at, float length) => Mathf.Repeat(s - at + length * 0.5f, length) - length * 0.5f;
        RoadNode Seam(Deck deck, float s)
        {
            if (s >= deck.Line.Length - 0.01f) s = 0f;
            var key = (deck, Mathf.RoundToInt(s * 10f));
            if (_seams.TryGetValue(key, out var node)) return node;
            var at = deck.Line.PointAt(s);
            node = _net.AddNode(at.x, at.z, 0.1f, 0.1f, 0f);
            node.Seam = true; _seams.Add(key, node);
            return node;
        }
        void PlanRamps(Deck deck, float at, RoadNode terminal)
        {
            float off = Mathf.Repeat(at - ExpresswayLayout.Gore, deck.Line.Length);
            float on = Mathf.Repeat(at + ExpresswayLayout.Gore, deck.Line.Length);
            Vector3 tangent = deck.Line.DirAt(at), centre = new Vector3(terminal.X, 0, terminal.Z);
            var exit = RoadLine.Bezier(deck.Line.Pose(off, ExpresswayLayout.AuxOff), deck.Line.DirAt(off),
                centre - tangent * StreetKit.StreetHalf, tangent, 0.45f, 0.4f);
            var entry = RoadLine.Bezier(centre + tangent * StreetKit.StreetHalf, tangent,
                deck.Line.Pose(on, ExpresswayLayout.AuxOff), deck.Line.DirAt(on), 0.4f, 0.45f);
            // Retain the authored endpoint headings instead of the sampled chords.
            exit = RoadLine.Through(exit.Pts, deck.Line.DirAt(off), tangent);
            entry = RoadLine.Through(entry.Pts, tangent, deck.Line.DirAt(on));
            Ramps.Add(new Ramp { Deck = deck, Line = exit, A = Seam(deck, off), B = terminal, Off = true, DeckLevel = deck.Height(off) });
            Ramps.Add(new Ramp { Deck = deck, Line = entry, A = terminal, B = Seam(deck, on), DeckLevel = deck.Height(on) });
        }
        void WireDeck(Deck deck)
        {
            var cuts = new List<float> { 0f, deck.Line.Length };
            foreach (float s in deck.Exchanges)
                foreach (float delta in new[] { -ExpresswayLayout.AuxOut, -ExpresswayLayout.Gore,
                            ExpresswayLayout.Gore, ExpresswayLayout.AuxOut })
                    cuts.Add(Mathf.Repeat(s + delta, deck.Line.Length));
            cuts.Sort();
            for (int i = 1; i < cuts.Count; i++)
            {
                float a = cuts[i - 1], b = cuts[i];
                if (b - a < 0.1f) continue;
                float middle = (a + b) * 0.5f;
                bool aux = false, exit = false;
                foreach (float s in deck.Exchanges)
                    if (Mathf.Abs(Delta(middle, s, deck.Line.Length)) < ExpresswayLayout.AuxOut &&
                        Mathf.Abs(Delta(middle, s, deck.Line.Length)) > ExpresswayLayout.Gore)
                    { aux = true; exit = Delta(middle, s, deck.Line.Length) < 0f; }
                var road = _net.AddCurve(deck.Line.Sub(a, b), ExpresswayLayout.DeckHalf,
                    aux ? new[] { -2.85f, 2.85f, ExpresswayLayout.AuxOff } : ExpresswayLayout.DeckLanes,
                    ExpresswayLayout.DeckSpeed, Seam(deck, a), Seam(deck, b), true, RoadClass.Freeway);
                float start = a;
                road.SurfaceAt = s => deck.Height(start + s); road.SurfaceY = deck.Height(a); road.Elevated = true;
                road.HalfPlus = aux ? ExpresswayLayout.AuxHalf : ExpresswayLayout.DeckHalf;
                if (aux)
                {
                    var lane = road.LaneFor(1, ExpresswayLayout.AuxOff);
                    lane.Auxiliary = true; lane.Exit = exit;
                }
            }
        }
    }
}
