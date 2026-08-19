using System.Collections.Generic;
using UnityEngine;

namespace AirportDemo
{
    /// <summary>
    /// Who is flying, who is taxiing, and who has the runway. One object owns all of
    /// it, because everything that can go wrong on an airfield is two aircraft each
    /// believing it has right of way.
    ///
    /// The taxiways are a small graph - a node on the runway centreline at each
    /// connector, one at its holding position, one where it meets the parallel
    /// taxiway, the taxiway itself as a chain, the ramp lane as another chain, and a
    /// node at every stand - and an aeroplane taxis by walking a path found over it.
    /// The runway is a single lock: one aeroplane at a time, landing traffic first,
    /// and a departure is never cleared with somebody inside four miles on final.
    ///
    /// The circuit is the standard left-hand one, laid out at the field's own scale:
    /// climb straight ahead, turn left crosswind, downwind on the far side, base,
    /// final. Whichever end of the runway is into wind is the end everybody uses.
    /// </summary>
    public sealed class FlightOps
    {
        // ------------------------------------------------------------ the graph

        public sealed class Node
        {
            public Vector3 Pos;
            public string Name;
            public readonly List<Node> Links = new List<Node>();
        }

        readonly List<Node> _nodes = new List<Node>();
        readonly List<Node> _holds = new List<Node>();
        readonly List<Node> _runwayAt = new List<Node>();
        readonly List<Node> _standNodes = new List<Node>();
        readonly List<(Vector3 pos, float yaw)> _stands = new List<(Vector3, float)>();

        readonly List<Aircraft> _fleet = new List<Aircraft>();
        readonly System.Random _rng;
        readonly bool _westerly;
        readonly float _half;

        Aircraft _runwayUser;
        float _runwayFreeIn;          // seconds until the current user is expected clear
        float _commuterTimer;
        readonly float _commuterInterval;

        /// <summary>Whoever wants to know what is happening on the field - the fuel
        /// truck, the baggage train, the marshaller.</summary>
        public IReadOnlyList<Aircraft> Fleet => _fleet;
        public System.Action<Aircraft> OnShutdown, OnStartUp, OnLanded;

        public FlightOps(System.Random rng, bool westerly, float runwayHalf, float commuterInterval)
        {
            _rng = rng;
            _westerly = westerly;
            _half = runwayHalf;
            _commuterInterval = Mathf.Max(60f, commuterInterval);
            _commuterTimer = 25f;
            BuildGraph();
        }

        Node Add(string name, Vector3 pos)
        {
            var n = new Node { Name = name, Pos = pos };
            _nodes.Add(n);
            return n;
        }

        static void Link(Node a, Node b)
        {
            if (a == null || b == null || a == b) return;
            if (!a.Links.Contains(b)) a.Links.Add(b);
            if (!b.Links.Contains(a)) b.Links.Add(a);
        }

        void BuildGraph()
        {
            float y = AirportSpec.PaveY;
            float tz = AirportSpec.TaxiwayZ;
            var taxiwayChain = new List<Node>();

            for (int i = 0; i < AirportSpec.ConnectorX.Length; i++)
            {
                float x = Mathf.Clamp(AirportSpec.ConnectorX[i], -_half + 30f, _half - 30f);
                var onRunway = Add("RWY " + AirportSpec.ConnectorName[i], new Vector3(x, y, 0f));
                var hold = Add("HOLD " + AirportSpec.ConnectorName[i], new Vector3(x, y, AirportSpec.HoldShortZ + 6f));
                var onTaxiway = Add("TWY " + AirportSpec.ConnectorName[i], new Vector3(x, y, tz));
                Link(onRunway, hold);
                Link(hold, onTaxiway);
                _runwayAt.Add(onRunway);
                _holds.Add(hold);
                taxiwayChain.Add(onTaxiway);
            }

            var laneChain = new List<Node>();
            float laneZ = AirportSpec.ApronZ0 + 12f;
            foreach (float ex in AirportSpec.ApronEntryX)
            {
                var onTaxiway = Add("TWY entry", new Vector3(ex, y, tz));
                var onRamp = Add("RAMP entry", new Vector3(ex, y, laneZ));
                Link(onTaxiway, onRamp);
                taxiwayChain.Add(onTaxiway);
                laneChain.Add(onRamp);
            }

            // the stands: the two commuter stands at the terminal, then the tie-down
            // rows on the general aviation ramp
            for (int i = 0; i < AirportSpec.CommuterStandX.Length; i++)
                AddStand(new Vector3(AirportSpec.CommuterStandX[i], y, AirportSpec.CommuterStandZ - AirportSpec.JetLength * 0.45f), 0f, laneChain, laneZ);

            for (int row = 0; row < AirportSpec.TieDownRows; row++)
            {
                float sz = AirportSpec.TieDownRowZ0 + row * AirportSpec.TieDownRowPitch;
                for (float x = AirportSpec.TieDownX0; x <= AirportSpec.TieDownX1 + 0.1f; x += AirportSpec.TieDownPitch)
                    AddStand(new Vector3(x, y, sz), 180f, laneChain, laneZ);
            }

            // and the FBO's two hardstands by the fuel island
            for (int i = -1; i <= 1; i += 2)
                AddStand(new Vector3(AirportSpec.FuelIslandX + i * 16f, y, AirportSpec.FuelIslandZ - 6f), 90f + (i < 0 ? 180f : 0f), laneChain, laneZ);

            Chain(taxiwayChain);
            Chain(laneChain);
        }

        void AddStand(Vector3 pos, float yaw, List<Node> laneChain, float laneZ)
        {
            var approach = Add("lane", new Vector3(pos.x, pos.y, laneZ));
            var stand = Add("STAND " + _stands.Count, pos);
            Link(approach, stand);
            laneChain.Add(approach);
            _standNodes.Add(stand);
            _stands.Add((pos, yaw));
        }

        /// <summary>Links a run of nodes along a line into a chain, nearest to nearest.</summary>
        static void Chain(List<Node> nodes)
        {
            nodes.Sort((a, b) => a.Pos.x.CompareTo(b.Pos.x));
            for (int i = 1; i < nodes.Count; i++) Link(nodes[i - 1], nodes[i]);
        }

        Node NearestNode(Vector3 p, System.Func<Node, bool> filter = null)
        {
            Node best = null;
            float bestD = float.MaxValue;
            foreach (var n in _nodes)
            {
                if (filter != null && !filter(n)) continue;
                float d = (n.Pos - p).sqrMagnitude;
                if (d < bestD) { bestD = d; best = n; }
            }
            return best;
        }

        /// <summary>A path over the taxiways, breadth first - the graph is thirty
        /// nodes wide and a ladder, so nothing cleverer earns its keep.</summary>
        List<Vector3> Path(Node from, Node to)
        {
            var points = new List<Vector3>();
            if (from == null || to == null) return points;
            var came = new Dictionary<Node, Node> { [from] = null };
            var queue = new Queue<Node>();
            queue.Enqueue(from);
            while (queue.Count > 0)
            {
                var n = queue.Dequeue();
                if (n == to) break;
                foreach (var m in n.Links)
                    if (!came.ContainsKey(m)) { came[m] = n; queue.Enqueue(m); }
            }
            if (!came.ContainsKey(to)) return points;
            for (var n = to; n != null; n = came[n]) points.Add(n.Pos);
            points.Reverse();
            if (points.Count > 0) points.RemoveAt(0);   // where we already are
            return points;
        }

        // ------------------------------------------------------------ the fleet

        public int StandCount => _stands.Count;
        public (Vector3 pos, float yaw) Stand(int i) => _stands[Mathf.Clamp(i, 0, _stands.Count - 1)];

        /// <summary>How many stands at the terminal (the airline ones) come before
        /// the tie-down rows in the stand list.</summary>
        public int AirlineStands => AirportSpec.CommuterStandX.Length;

        public void Adopt(Aircraft a, int stand, bool commuter)
        {
            a.Stand = Mathf.Clamp(stand, 0, _stands.Count - 1);
            a.Commuter = commuter;
            switch (a.Class)
            {
                case Aircraft.Kind.Jet:
                    a.RotateSpeed = AirportSpec.JetRotate; a.ClimbSpeed = AirportSpec.JetClimb;
                    a.ApproachSpeed = AirportSpec.JetApproach; a.TakeoffRun = 1250f;
                    break;
                case Aircraft.Kind.Commuter:
                    a.RotateSpeed = AirportSpec.CommuterRotate; a.ClimbSpeed = AirportSpec.CommuterClimb;
                    a.ApproachSpeed = AirportSpec.CommuterApproach; a.TakeoffRun = 800f;
                    break;
                default:
                    a.RotateSpeed = AirportSpec.GaRotate; a.ClimbSpeed = AirportSpec.GaClimb;
                    a.ApproachSpeed = AirportSpec.GaApproach; a.TakeoffRun = 450f;
                    break;
            }
            var (pos, yaw) = _stands[a.Stand];
            a.Park(pos, yaw);
            a.State = Aircraft.Phase.Parked;
            a.Timer = commuter ? _commuterInterval * 0.5f : (float)(_rng.NextDouble() * 90f + 20f);
            _fleet.Add(a);
        }

        // ------------------------------------------------------------ the runway

        /// <summary>Is anybody on final close enough that nothing else should be let
        /// onto the runway?</summary>
        bool SomebodyOnFinal()
        {
            foreach (var a in _fleet)
                if (a.State == Aircraft.Phase.Final) return true;
            return false;
        }

        bool RequestRunway(Aircraft a, bool landing)
        {
            if (_runwayUser == a) return true;
            if (_runwayUser != null) return false;
            if (!landing && SomebodyOnFinal()) return false;
            _runwayUser = a;
            a.HasRunway = true;
            _runwayFreeIn = landing ? 90f : 70f;
            return true;
        }

        void ReleaseRunway(Aircraft a)
        {
            if (_runwayUser != a) return;
            _runwayUser = null;
            a.HasRunway = false;
        }

        // ------------------------------------------------------------ geometry

        /// <summary>The threshold in use and the way a take-off or a landing runs.</summary>
        float ThresholdX => _westerly ? _half : -_half;
        float DepartureX => _westerly ? -_half : _half;
        float RunSign => _westerly ? -1f : 1f;      // +1 rolling east, -1 rolling west
        /// <summary>The circuit is flown on the left of the landing direction.</summary>
        float PatternSide => _westerly ? -1f : 1f;

        /// <summary>The connector nearest a point, for taxiing out and turning off.</summary>
        int NearestConnector(float x)
        {
            int best = 0;
            float bd = float.MaxValue;
            for (int i = 0; i < _holds.Count; i++)
            {
                float d = Mathf.Abs(_holds[i].Pos.x - x);
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        /// <summary>The connector an aeroplane lines up at: the one nearest the
        /// threshold in use, so the whole runway is ahead of it.</summary>
        int DepartureConnector() => NearestConnector(ThresholdX);

        /// <summary>Where a landing rolls out to: the first connector far enough down
        /// the runway that this aeroplane will have slowed by it. A light single is
        /// stopped in four hundred metres and turns off early; a trijet runs on.</summary>
        int ExitConnector(Aircraft a)
        {
            float needed = a == null ? 700f
                : a.Class == Aircraft.Kind.Jet ? 1100f
                : a.Class == Aircraft.Kind.Commuter ? 700f : 420f;
            int best = -1;
            float bestD = float.MaxValue;
            for (int i = 0; i < _holds.Count; i++)
            {
                float along = (_holds[i].Pos.x - ThresholdX) * RunSign;
                if (along < needed) continue;
                if (along < bestD) { bestD = along; best = i; }
            }
            // nothing far enough down: the far end, which is where it will end up
            if (best < 0)
                for (int i = 0; i < _holds.Count; i++)
                {
                    float along = (_holds[i].Pos.x - ThresholdX) * RunSign;
                    if (along > bestD || bestD == float.MaxValue) { bestD = along; best = i; }
                }
            return Mathf.Max(0, best);
        }

        // ------------------------------------------------------------ the tick

        public void Tick(float dt)
        {
            if (_runwayUser != null)
            {
                _runwayFreeIn -= dt;
                if (_runwayFreeIn <= 0f) ReleaseRunway(_runwayUser);   // the safety net, never the normal way out
            }
            _commuterTimer -= dt;

            for (int i = 0; i < _fleet.Count; i++)
            {
                var a = _fleet[i];
                Advance(a, dt);
                a.Tick(dt);
            }
        }

        void Advance(Aircraft a, float dt)
        {
            a.Timer -= dt;
            switch (a.State)
            {
                case Aircraft.Phase.Parked:
                    if (a.Commuter)
                    {
                        if (_commuterTimer > 0f) return;
                        _commuterTimer = _commuterInterval;
                    }
                    else if (a.Timer > 0f) return;
                    a.State = Aircraft.Phase.StartUp;
                    a.Timer = 6f;
                    a.Doors(false);
                    a.Throttle(0.35f);
                    a.Puff(false);
                    OnStartUp?.Invoke(a);
                    break;

                case Aircraft.Phase.StartUp:
                    if (a.Timer > 0f) return;
                    {
                        int c = DepartureConnector();
                        var from = NearestNode(a.Position, n => n.Name.StartsWith("STAND")) ?? NearestNode(a.Position);
                        var path = Path(from, _holds[c]);
                        if (path.Count == 0) { a.State = Aircraft.Phase.Parked; a.Timer = 30f; return; }
                        a.Clear();
                        a.GoAll(path, AirportSpec.TaxiSpeed);
                        a.Throttle(0.22f);
                        a.State = Aircraft.Phase.Taxi;
                    }
                    break;

                case Aircraft.Phase.Taxi:
                    if (!a.Idle) return;
                    a.State = Aircraft.Phase.Hold;
                    a.Throttle(0.12f);
                    a.Timer = 2f;
                    break;

                case Aircraft.Phase.Hold:
                    if (a.Timer > 0f) return;
                    if (!RequestRunway(a, landing: false)) { a.Timer = 3f; return; }
                    {
                        int c = DepartureConnector();
                        float x = _runwayAt[c].Pos.x;
                        a.Clear();
                        // out onto the centreline and round onto the runway heading
                        a.Go(new Vector3(x, AirportSpec.PaveY, 22f), AirportSpec.TaxiTurnSpeed);
                        a.Go(new Vector3(x - RunSign * 14f, AirportSpec.PaveY, 0f), AirportSpec.TaxiTurnSpeed);
                        a.Go(new Vector3(x - RunSign * 20f, AirportSpec.PaveY, 0f), AirportSpec.TaxiSpeed);
                        a.State = Aircraft.Phase.LineUp;
                        a.Throttle(0.25f);
                    }
                    break;

                case Aircraft.Phase.LineUp:
                    if (!a.Idle) return;
                    a.Clear();
                    a.Throttle(1f);
                    // the roll: down the centreline to the point it gets airborne
                    a.Go(new Vector3(ThresholdX + RunSign * a.TakeoffRun, AirportSpec.PaveY, 0f), a.RotateSpeed);
                    a.State = Aircraft.Phase.Roll;
                    break;

                case Aircraft.Phase.Roll:
                    if (!a.Idle && a.Speed < a.RotateSpeed * 0.94f) return;
                    a.Clear();
                    a.Throttle(1f);
                    // climb straight ahead, then the crosswind turn
                    a.Go(new Vector3(DepartureX + RunSign * 500f, 110f, 0f), a.ClimbSpeed, ground: false);
                    a.Go(new Vector3(DepartureX + RunSign * 900f, AirportSpec.PatternAltitude, PatternSide * 320f), a.ClimbSpeed, ground: false);
                    a.State = Aircraft.Phase.Climb;
                    ReleaseRunway(a);
                    break;

                case Aircraft.Phase.Climb:
                    if (!a.Idle) return;
                    a.Throttle(0.75f);
                    if (a.Commuter || _rng.NextDouble() < 0.45)
                    {
                        // away: out of the circuit, out of the map, back in a while
                        a.InCircuit = false;
                        a.Clear();
                        a.Go(new Vector3(DepartureX + RunSign * 3200f, AirportSpec.PatternAltitude + 280f, PatternSide * 1200f),
                             a.ClimbSpeed, ground: false);
                        a.State = Aircraft.Phase.Cruise;
                    }
                    else
                    {
                        FlyCircuit(a);
                    }
                    break;

                case Aircraft.Phase.Cruise:
                    if (!a.Idle) return;
                    if (a.InCircuit)
                    {
                        // round the circuit and onto final - but only with the runway,
                        // and if it is not free another lap is flown rather than a
                        // straight-in over somebody else's landing
                        if (RequestRunway(a, landing: true))
                        {
                            a.Throttle(0.4f);
                            a.Clear();
                            Approach(a);
                            a.State = Aircraft.Phase.Final;
                        }
                        else
                        {
                            FlyCircuit(a);
                        }
                        return;
                    }
                    // departed: out of sight, and back later as an arrival
                    a.Show(false);
                    a.State = Aircraft.Phase.Away;
                    a.Timer = a.Commuter ? _commuterInterval * 0.65f : (float)(_rng.NextDouble() * 120f + 60f);
                    break;

                case Aircraft.Phase.Away:
                    if (a.Timer > 0f) return;
                    if (!RequestRunway(a, landing: true)) { a.Timer = 6f; return; }
                    {
                        a.Show(true);
                        a.InCircuit = false;
                        float sx = ThresholdX - RunSign * AirportSpec.FinalLength;
                        a.Park(new Vector3(sx, AirportSpec.PatternAltitude * 0.62f, 0f), _westerly ? 270f : 90f);
                        a.Clear();
                        a.Throttle(0.4f);
                        Approach(a);
                        a.State = Aircraft.Phase.Final;
                    }
                    break;

                case Aircraft.Phase.Final:
                    if (!a.Idle) return;
                    a.Puff(true);
                    a.Clear();
                    a.Throttle(0.1f);
                    {
                        int c = ExitConnector(a);
                        a.Go(new Vector3(_runwayAt[c].Pos.x, AirportSpec.PaveY, 0f), AirportSpec.TaxiSpeed);
                        a.State = Aircraft.Phase.Rollout;
                    }
                    OnLanded?.Invoke(a);
                    break;

                case Aircraft.Phase.Rollout:
                    if (!a.Idle) return;
                    ReleaseRunway(a);
                    {
                        int c = ExitConnector(a);
                        var path = Path(_runwayAt[c], _standNodes[Mathf.Clamp(a.Stand, 0, _standNodes.Count - 1)]);
                        a.Clear();
                        a.GoAll(path, AirportSpec.TaxiSpeed);
                        a.Throttle(0.2f);
                        // taxiing in is not the same state as taxiing out - Taxi would
                        // send it back to the holding point - so it goes onto Shutdown
                        // with the timer parked high, which reads as "still rolling in"
                        a.State = Aircraft.Phase.Shutdown;
                        a.Timer = 999f;
                    }
                    break;

                case Aircraft.Phase.Shutdown:
                    if (!a.Idle) return;
                    if (a.Timer > 900f)
                    {
                        // just arrived on the stand: swing to the parking heading,
                        // shut down, open up
                        var (pos, yaw) = _stands[Mathf.Clamp(a.Stand, 0, _stands.Count - 1)];
                        a.Park(pos, yaw);
                        a.Throttle(0f);
                        a.Doors(true);
                        a.Timer = a.Commuter ? 60f : (float)(_rng.NextDouble() * 180f + 90f);
                        OnShutdown?.Invoke(a);
                        return;
                    }
                    if (a.Timer > 0f) return;
                    a.Doors(false);
                    a.State = Aircraft.Phase.Parked;
                    a.Timer = a.Commuter ? _commuterInterval : (float)(_rng.NextDouble() * 200f + 60f);
                    break;
            }
        }

        /// <summary>The standard left-hand circuit at the field's own scale: crosswind,
        /// downwind on the far side, base, then final.</summary>
        void FlyCircuit(Aircraft a)
        {
            float side = PatternSide, alt = AirportSpec.PatternAltitude;
            float baseX = ThresholdX - RunSign * (AirportSpec.FinalLength + 150f);
            a.Clear();
            // crosswind out to the downwind line
            a.Go(new Vector3(DepartureX + RunSign * 600f, alt, side * AirportSpec.PatternWidth), a.ClimbSpeed, ground: false);
            // downwind, the length of the field and then some
            a.Go(new Vector3(ThresholdX - RunSign * 500f, alt, side * AirportSpec.PatternWidth), a.ClimbSpeed, ground: false);
            // base, turning in toward the extended centreline
            a.Go(new Vector3(baseX, alt * 0.72f, side * AirportSpec.PatternWidth * 0.45f), a.ApproachSpeed, ground: false);
            a.Go(new Vector3(ThresholdX - RunSign * AirportSpec.FinalLength, alt * 0.55f, 0f), a.ApproachSpeed, ground: false);
            a.InCircuit = true;
            a.State = Aircraft.Phase.Cruise;
            // flown out, that leaves the aeroplane at the start of final at circuit
            // height, which is exactly where Cruise asks for the runway and hands it
            // to Approach - so a circuit and an arrival off the map land the same way
        }

        /// <summary>Final approach: three degrees down the extended centreline to the
        /// threshold, then the flare and the touchdown zone.</summary>
        void Approach(Aircraft a)
        {
            float x0 = ThresholdX - RunSign * AirportSpec.FinalLength;
            a.Go(new Vector3(Mathf.Lerp(x0, ThresholdX, 0.55f), AirportSpec.PatternAltitude * 0.3f, 0f), a.ApproachSpeed, ground: false);
            a.Go(new Vector3(ThresholdX - RunSign * 70f, AirportSpec.PaveY + 4f, 0f), a.ApproachSpeed, ground: false);
            // over the threshold, the flare, and down in the touchdown zone
            a.Go(new Vector3(ThresholdX + RunSign * 300f, AirportSpec.PaveY, 0f), a.ApproachSpeed * 0.85f, ground: false);
        }
    }
}
