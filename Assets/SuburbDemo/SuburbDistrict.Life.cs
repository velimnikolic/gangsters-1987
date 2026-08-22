using System.Collections.Generic;
using RoadDemo;
using UnityEngine;

namespace SuburbDemo
{
    // Life on the grid: the lane graph the cars ride (two lanes a street, a little
    // loop of three short edges round each turning bulb so a car comes back out of a
    // cul-de-sac as two left turns, never a U-turn the driver refuses), hidden
    // stop-sign logic at the junctions (a TrafficSignal with no poles), the pavement
    // graph the residents walk with zebra crossings at every junction, every house's
    // door wired to the pavement so people go in and come out, and the spawns.
    public partial class SuburbDistrict
    {
        readonly List<RoadEdge> _edges = new List<RoadEdge>();
        readonly HashSet<RoadEdge> _stubEdges = new HashSet<RoadEdge>();
        readonly List<PedLink> _pedLinks = new List<PedLink>();
        readonly List<DemoDoor> _doors = new List<DemoDoor>();

        // ------------------------------------------------------------ lanes

        RoadEdge AddEdge(RoadNode from, RoadNode to, Vector3 start, Vector3 end, bool northSouth, bool stub)
        {
            var dir = end - start;
            float len = dir.magnitude;
            dir /= Mathf.Max(len, 0.001f);
            var e = new RoadEdge { From = from, To = to, Start = start, End = end, Dir = dir, Length = len, NorthSouth = northSouth, SpeedLimit = streetSpeed };
            from?.Outgoing.Add(e);
            to?.Incoming.Add(e);
            _edges.Add(e);
            if (stub) _stubEdges.Add(e);
            return e;
        }

        RoadNode NewRoadNode(float x, float z, float half = StreetHalf)
            => new RoadNode { X = x, Z = z, XMin = x - half, XMax = x + half, ZMin = z - half, ZMax = z + half };

        void BuildLaneGraph()
        {
            foreach (var n in _nodeList)
            {
                n.Road = NewRoadNode(n.X, n.Z);
                n.Road.I = n.I;
                n.Road.J = n.J;
                if (n.Arms >= 3)
                {
                    // stop-sign logic: the junction alternates its axes like a light would,
                    // offset per junction so the whole grid does not breathe in step
                    n.Signal = new TrafficSignal(((n.I * 31 + n.J * 17) % 13) / 13f * TrafficSignal.Cycle);
                    n.Road.Signal = n.Signal;
                    _signals.Add(n.Signal);
                }
            }

            foreach (var s in _segments)
            {
                // a street's flat-capped end at the map's edge carries no lanes: there is no
                // turning in 10 m and no U-turn, so no car is sent down it (the plan sees to
                // it that no junction forces one there)
                if (s.Open) continue;
                if (!s.Stub) { ThroughLanes(s); continue; }
                StubLanes(s);
            }

            // The plan means every node to let every arrival out again without a U-turn,
            // and where it fails it is worth saying so - but it is not an error, which is
            // what this used to be raised as. A street that ends in a flat cap on the map's
            // edge lays no lanes down the cap at all (the Open test above), so the junction
            // behind it offers nothing but the way the car came in; and RoadCar knows that
            // shape and drives it - PlanNext falls through to "the turn-round at a dead end,
            // or anything at all" and takes the return connector. Nothing is stranded. So
            // this is a note about the PLAN, said once with all the places in it, rather
            // than a red line per dead end for a thing the driver already handles.
            var turnRounds = new List<string>();
            foreach (var e in _edges)
            {
                bool ok = false;
                foreach (var o in e.To.Outgoing) if (Vector3.Dot(o.Dir, e.Dir) >= -0.5f) ok = true;
                if (!ok) turnRounds.Add($"({e.End.x:F0},{e.End.z:F0})");
            }
            if (turnRounds.Count > 0)
                Debug.LogWarning($"[SuburbDemo] lane graph: {turnRounds.Count} dead end(s) a car can only leave by " +
                                 $"turning round, at {string.Join(", ", turnRounds)} - the flat caps on the map's edge " +
                                 "carry no lanes, so the junction behind one is the end of the road");
            Debug.Log($"[SuburbDemo] lane graph: {_edges.Count} edges, {_signals.Count} junctions with stop logic");
        }

        // (along, across) -> world, for a segment's axis
        Vector3 AW(Segment s, float along, float across) => s.Vertical ? new Vector3(s.Axis + across, 0f, along) : new Vector3(along, 0f, s.Axis + across);

        void ThroughLanes(Segment s)
        {
            // right-hand traffic: heading +along the lane sits at +across for a vertical
            // street (east of the axis), -across for a horizontal one (south of it)
            float right = s.Vertical ? 2.5f : -2.5f;
            s.Edges.Add(AddEdge(s.LoNode.Road, s.HiNode.Road, AW(s, s.Lo, right), AW(s, s.Hi, right), s.Vertical, false));
            s.Edges.Add(AddEdge(s.HiNode.Road, s.LoNode.Road, AW(s, s.Hi, -right), AW(s, s.Lo, -right), s.Vertical, false));
        }

        // Into the bulb on the right-hand lane, round it in three short edges with a
        // 5 m box at each corner node (set back 2.5 m from the corner so the turn
        // curve has room), out on the other lane. The slide from the street lane at
        // +-2.5 to the bulb lane at +-7.5 reads to the driver as "straight" (dot = 1)
        // and the curve maker draws it as a diagonal.
        void StubLanes(Segment s)
        {
            float right = s.Vertical ? 2.5f : -2.5f;
            float dirSign = s.DeadHi ? 1f : -1f;                 // +1: the bulb lies at +along of the junction
            var junction = s.DeadHi ? s.LoNode.Road : s.HiNode.Road;
            float jFace = s.DeadHi ? s.Lo : s.Hi;                // junction box face
            float bNear = s.DeadHi ? s.BulbLo : s.BulbHi;        // bulb edge nearest the junction
            float bFar = s.DeadHi ? s.BulbHi : s.BulbLo;         // far edge
            float inLane = dirSign * right;                      // across of the inbound lane (right-hand traffic, whichever way the stub runs)
            float outLane = -inLane;
            float bulbIn = Mathf.Sign(inLane) * (BulbHalf - 2.5f);
            float bulbOut = -bulbIn;
            float farLane = bFar - dirSign * 2.5f;               // along of the far-side lane

            var nIn = NewRoadNode(AW(s, bNear, inLane).x, AW(s, bNear, inLane).z, 2.5f);
            var nFarIn = NewRoadNode(AW(s, farLane, bulbIn).x, AW(s, farLane, bulbIn).z, 2.5f);
            var nFarOut = NewRoadNode(AW(s, farLane, bulbOut).x, AW(s, farLane, bulbOut).z, 2.5f);
            var nOut = NewRoadNode(AW(s, bNear, outLane).x, AW(s, bNear, outLane).z, 2.5f);

            bool ns = s.Vertical;
            // in from the junction to the bulb mouth
            s.Edges.Add(AddEdge(junction, nIn, AW(s, jFace, inLane), AW(s, bNear - dirSign * 2.5f, inLane), ns, true));
            // E1: up the near side of the bulb on the wide lane
            s.Edges.Add(AddEdge(nIn, nFarIn, AW(s, bNear + dirSign * 2.5f, bulbIn), AW(s, farLane - dirSign * 2.5f, bulbIn), ns, true));
            // E2: across the far side
            s.Edges.Add(AddEdge(nFarIn, nFarOut, AW(s, farLane, bulbIn - Mathf.Sign(bulbIn) * 2.5f), AW(s, farLane, bulbOut + Mathf.Sign(bulbIn) * 2.5f), !ns, true));
            // E3: back down the other side
            s.Edges.Add(AddEdge(nFarOut, nOut, AW(s, farLane - dirSign * 2.5f, bulbOut), AW(s, bNear + dirSign * 2.5f, bulbOut), ns, true));
            // out to the junction
            s.Edges.Add(AddEdge(nOut, junction, AW(s, bNear - dirSign * 2.5f, outLane), AW(s, jFace, outLane), ns, true));
        }

        // ------------------------------------------------------------ pavements

        PedNode PedAt(float x, float z) => new PedNode { Pos = new Vector3(x, WalkY + Ground(x, z), z) };

        /// <summary>A pavement link from a to b in pieces no longer than 20 m, a node on
        /// the ground at every joint - so a walker follows the lie of the land instead of
        /// a chord through a hill.</summary>
        void AddPedChain(PedNode a, PedNode b)
        {
            var d = b.Pos - a.Pos;
            d.y = 0f;
            float len = d.magnitude;
            int pieces = Mathf.Max(1, Mathf.CeilToInt(len / 20f));
            if (relief <= 0f) pieces = 1;
            var prev = a;
            for (int k = 1; k <= pieces; k++)
            {
                PedNode next;
                if (k == pieces) next = b;
                else
                {
                    float x = a.Pos.x + d.x * k / pieces, z = a.Pos.z + d.z * k / pieces;
                    next = PedAt(x, z);
                }
                AddPedLink(prev, next, false, false, null);
                prev = next;
            }
        }

        void AddPedLink(PedNode a, PedNode b, bool gated, bool blocksNS, TrafficSignal sig)
        {
            float len = (b.Pos - a.Pos).magnitude;
            var ab = new PedLink { From = a, To = b, Length = len, Gated = gated, BlocksNorthSouth = blocksNS, Signal = sig };
            var ba = new PedLink { From = b, To = a, Length = len, Gated = gated, BlocksNorthSouth = blocksNS, Signal = sig };
            a.Links.Add(ab);
            b.Links.Add(ba);
            _pedLinks.Add(ab);
            _pedLinks.Add(ba);
        }

        const int NE = 0, NW = 1, SW = 2, SE = 3;

        void BuildPedGraph()
        {
            const float Off = StreetHalf + Walk * 0.5f; // the middle of the corner slab
            foreach (var n in _nodeList)
            {
                n.Corner[NE] = PedAt(n.X + Off, n.Z + Off);
                n.Corner[NW] = PedAt(n.X - Off, n.Z + Off);
                n.Corner[SW] = PedAt(n.X - Off, n.Z - Off);
                n.Corner[SE] = PedAt(n.X + Off, n.Z - Off);
                // across each arm a zebra (held by the junction's stop logic where there
                // is one); where no arm, the box edge is plain pavement
                AddPedLink(n.Corner[NW], n.Corner[NE], n.N, true, n.N ? n.Signal : null);
                AddPedLink(n.Corner[SW], n.Corner[SE], n.S, true, n.S ? n.Signal : null);
                AddPedLink(n.Corner[NE], n.Corner[SE], n.E, false, n.E ? n.Signal : null);
                AddPedLink(n.Corner[NW], n.Corner[SW], n.W, false, n.W ? n.Signal : null);
            }

            foreach (var s in _segments)
            {
                if (!s.Stub)
                {
                    if (s.Vertical)
                    {
                        AddPedChain(s.LoNode.Corner[NW], s.HiNode.Corner[SW]);
                        AddPedChain(s.LoNode.Corner[NE], s.HiNode.Corner[SE]);
                    }
                    else
                    {
                        AddPedChain(s.LoNode.Corner[SE], s.HiNode.Corner[SW]);
                        AddPedChain(s.LoNode.Corner[NE], s.HiNode.Corner[NW]);
                    }
                    continue;
                }
                StubPavement(s);
            }
            Debug.Log($"[SuburbDemo] pavement graph: {_pedLinks.Count} links");
        }

        // Down both sides of the stub and round the ring of pavement about the bulb - or,
        // at a flat-capped end, straight across the end under the cap's wrap.
        void StubPavement(Segment s)
        {
            float dirSign = s.DeadHi ? 1f : -1f;
            var junction = s.DeadHi ? s.LoNode : s.HiNode;
            float bNear = s.DeadHi ? s.BulbLo : s.BulbHi;
            float bFar = s.DeadHi ? s.BulbHi : s.BulbLo;
            float walkIn = StreetHalf + Walk * 0.5f;            // +-7.5: the stub's pavements
            float ringOut = s.Open ? walkIn : BulbHalf + Walk * 0.5f;   // +-12.5: the ring's sides (the cap's wrap is no wider than the pavements)
            float ringNear = bNear - dirSign * Walk * 0.5f;     // along of the ring's near row
            float ringFar = bFar + dirSign * Walk * 0.5f;       // along of the ring's far row

            // which junction corners the stub's pavements leave from
            PedNode cLeft, cRight; // at -across and +across
            if (s.Vertical) { if (s.DeadHi) { cLeft = junction.Corner[NW]; cRight = junction.Corner[NE]; } else { cLeft = junction.Corner[SW]; cRight = junction.Corner[SE]; } }
            else { if (s.DeadHi) { cLeft = junction.Corner[SE]; cRight = junction.Corner[NE]; } else { cLeft = junction.Corner[SW]; cRight = junction.Corner[NW]; } }

            PedNode N(float along, float across) { var p = AW(s, along, across); return PedAt(p.x, p.z); }
            var inL = N(ringNear, -walkIn);
            var inR = N(ringNear, walkIn);
            var farL = N(ringFar, -ringOut);
            var farR = N(ringFar, ringOut);
            AddPedChain(cLeft, inL);
            if (s.Open)
            {
                AddPedLink(inL, farL, false, false, null);
            }
            else
            {
                var nearL = N(ringNear, -ringOut);
                var nearR = N(ringNear, ringOut);
                AddPedLink(inL, nearL, false, false, null);
                AddPedLink(nearL, farL, false, false, null);
                AddPedLink(farR, nearR, false, false, null);
                AddPedLink(nearR, inR, false, false, null);
            }
            AddPedLink(farL, farR, false, false, null);
            if (s.Open) AddPedLink(farR, inR, false, false, null);
            AddPedChain(inR, cRight);
        }

        // ------------------------------------------------------------ doors

        void BuildDoors(CityLife life)
        {
            // the one life of the scene, the host's: a walker out of a suburb house is
            // the same crowd as the one on the city's boulevard
            _life = life;

            bool NearestLink(Vector3 p, float maxDist, out PedLink fwd, out float t)
            {
                fwd = null;
                t = 0f;
                float bestD = maxDist * maxDist;
                foreach (var l in _pedLinks)
                {
                    if (l.Gated || l.Length < 6f) continue;
                    var dir = (l.To.Pos - l.From.Pos) / l.Length;
                    float sAlong = Mathf.Clamp(Vector3.Dot(p - l.From.Pos, dir), 2f, l.Length - 2f);
                    var q = l.From.Pos + dir * sAlong;
                    float dx = q.x - p.x, dz = q.z - p.z;
                    float d = dx * dx + dz * dz;
                    if (d < bestD) { bestD = d; fwd = l; t = sAlong; }
                }
                return fwd != null;
            }

            PedLink Reverse(PedLink l)
            {
                foreach (var r in l.To.Links)
                    if (r.To == l.From) return r;
                return null;
            }

            int wired = 0;
            foreach (var lot in _lots)
            {
                if (!lot.HasDoor) continue;
                // a door is reached from the pavement in front of it, never from a side street
                if (!NearestLink(lot.DoorPos, 24f, out var fwd, out var t)) continue;
                var linkDir = (fwd.To.Pos - fwd.From.Pos) / fwd.Length;
                if (Mathf.Abs(Vector3.Dot(linkDir, lot.Front)) > 0.5f) continue;
                var back = Reverse(fwd);
                if (back == null) continue;
                var door = new DemoDoor
                {
                    Pos = new Vector3(lot.DoorPos.x, WalkY + Ground(lot.DoorPos.x, lot.DoorPos.z), lot.DoorPos.z), Outward = lot.DoorOut, LinkFwd = fwd, LinkBack = back, EntryT = t,
                    EntryPos = Vector3.Lerp(fwd.From.Pos, fwd.To.Pos, t / fwd.Length),
                };
                _life.Doors.Add(door);
                _doors.Add(door);
                _life.AddStop(fwd, t, door, null);
                _life.AddStop(back, back.Length - t, door, null);
                wired++;
            }
            _life.SortStops();
            Debug.Log($"[SuburbDemo] {wired} doors on the pavements");
        }

        // ------------------------------------------------------------ spawns

        // The road demo's driver, except that the long vehicles keep to the through
        // streets: a bus has no business in a cul-de-sac and no room to turn in one.
        class SuburbVehicle : DemoVehicle
        {
            public bool Long;
            public HashSet<RoadEdge> Avoid;
            public System.Func<float, float, float> GroundWorld;   // the relief under the car, in world coordinates
            static readonly List<RoadEdge> L = new List<RoadEdge>(), R = new List<RoadEdge>();

            /// <summary>The shared driver places the car at the flat road level; the suburb
            /// lifts it onto its hills and leans it to the slope ahead.</summary>
            protected override void OnPlaced(float dt, float speed, float steerDegrees)
            {
                if (GroundWorld == null || Tf == null) return;
                var p = Tf.position;
                var f = Tf.forward;
                float h = GroundWorld(p.x, p.z);
                float ahead = GroundWorld(p.x + f.x * 2f, p.z + f.z * 2f), behind = GroundWorld(p.x - f.x * 2f, p.z - f.z * 2f);
                float pitch = -Mathf.Atan2(ahead - behind, 4f) * Mathf.Rad2Deg;
                Tf.SetPositionAndRotation(new Vector3(p.x, p.y + h, p.z), Quaternion.AngleAxis(pitch, Tf.right) * Tf.rotation);
            }

            protected override RoadEdge PickNext(RoadEdge straight, List<RoadEdge> lefts, List<RoadEdge> rights)
            {
                if (!Long || Avoid == null) return base.PickNext(straight, lefts, rights);
                var s = straight != null && Avoid.Contains(straight) ? null : straight;
                L.Clear(); R.Clear();
                foreach (var e in lefts) if (!Avoid.Contains(e)) L.Add(e);
                foreach (var e in rights) if (!Avoid.Contains(e)) R.Add(e);
                if (s == null && L.Count == 0 && R.Count == 0) return base.PickNext(straight, lefts, rights);
                return base.PickNext(s, L, R);
            }
        }

        void SpawnCars()
        {
            var cars = TownKit.LoadAll(TownKit.Cars);
            var longs = TownKit.LoadAll(TownKit.LongVehicles);
            if (cars.Count == 0 && longs.Count == 0) return;
            var root = new GameObject("Cars").transform;
            root.SetParent(_lifeRoot, false);
            // somebody at the wheel of every car (CarOccupant): the town's own people,
            // culled with the crowd. The buses and trucks go without - their cab sits
            // nowhere near a car's seat table, and a man set by it would be in the box
            var drivers = TownKit.LoadAll(TownKit.People);
            var sitLoop = CrewKit.PeopleClip("Sitting_Bench_Idle");

            var through = _edges.FindAll(e => !_stubEdges.Contains(e) && e.Length > 30f);
            int placed = 0;
            for (int round = 0; placed < carCount && round < 40; round++)
            {
                bool any = false;
                foreach (var e in through)
                {
                    if (placed >= carCount) break;
                    float sAlong = 8f + round * 22f;
                    if (sAlong > e.Length - 14f) continue;
                    any = true;
                    bool isLong = longs.Count > 0 && Chance(0.25f);
                    var prefab = isLong ? Pick(longs) : Pick(cars);
                    var go = Object.Instantiate(prefab, Vector3.zero, Quaternion.identity, root);
                    TownKit.StripForStatic(go);
                    var bounds = new Bounds(go.transform.position, Vector3.zero);
                    foreach (var r in go.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
                    var v = new SuburbVehicle { Tf = go.transform, HalfLen = bounds.extents.z + 0.3f, Long = isLong || bounds.size.z > 6.5f, Avoid = _stubEdges, GroundWorld = relief > 0f ? GroundWorld : null };
                    v.Spawn(e, sAlong);
                    if (!v.Long) CarOccupant.Crew(go.transform, drivers, sitLoop, passengerChance: 0.3f, layer: ScenePerf.CrowdLayer);
                    _vehicles.Add(v);
                    placed++;
                }
                if (!any) break;
            }
        }

        void SpawnResidents()
        {
            var people = TownKit.LoadAll(TownKit.People);
            if (people.Count == 0) return;
            var clips = CrewKit.Clips();
            if (clips.Walk == null || clips.Idle == null)
            {
                Debug.LogWarning("[SuburbDemo] walk/idle clips missing under Assets/Animations/People - no residents.");
                return;
            }
            if (clips.Talk != null) _life.CanChat = true;
            var root = new GameObject("Residents").transform;
            root.SetParent(_lifeRoot, false);
            var pavements = _pedLinks.FindAll(l => !l.Gated);
            if (pavements.Count == 0) return;

            int fromDoors = _life.Doors.Count > 0 ? Mathf.RoundToInt(pedestrianCount * insideAtStart) : 0;
            var variety = new System.Random(seed + 7);
            for (int k = 0; k < pedestrianCount; k++)
            {
                var link = Pick(pavements);
                var go = Object.Instantiate(Pick(people), root);
                PrepareBody(go);
                TownKit.SetLayerDeep(go, ScenePerf.CrowdLayer);
                var agent = new CivilianAgent { Speed = Rnd(1.2f, 1.8f) };
                agent.Init(go.transform, CrewKit.ForCrowd(clips, variety), link, Rnd() * link.Length * 0.9f);
                agent.Setup(_life);
                if (k < fromDoors) agent.SpawnInside(Rnd(2f, 60f));
                _civilians.Add(agent);
            }
        }

        // A few people stood about in their back yards and on the forecourt - by the
        // grill, at the pool, at the pump - breathing, going nowhere.
        void SpawnIdlers()
        {
            var people = TownKit.LoadAll(TownKit.People);
            if (people.Count == 0) return;
            var clips = CrewKit.Clips();
            if (clips.Idle == null) return;
            var spots = new List<(Vector3, Vector3)>();
            foreach (var lot in _lots)
                foreach (var p in lot.IdleSpots) spots.Add((W(new Vector3(p.x, p.y + Ground(p.x, p.z), p.z)), _inner.ToWorldDir(lot.Front)));
            for (int k = spots.Count - 1; k > 0; k--) { int r = _rng.Next(k + 1); (spots[k], spots[r]) = (spots[r], spots[k]); }
            var root = new GameObject("Idlers").transform;
            root.SetParent(_lifeRoot, false);
            for (int k = 0; k < Mathf.Min(yardIdlers, spots.Count); k++)
            {
                var (pos, front) = spots[k];
                var go = Object.Instantiate(Pick(people), root);
                PrepareBody(go);
                TownKit.SetLayerDeep(go, ScenePerf.CrowdLayer);
                var agent = new PedestrianAgent();
                agent.InitAt(go.transform, new PedClips { Walk = clips.Walk, Idle = clips.Idle }, pos, Quaternion.Euler(0f, Rnd(0f, 360f), 0f));
                _idlers.Add(agent);
            }
        }

        static void PrepareBody(GameObject go)
        {
            foreach (var col in go.GetComponentsInChildren<Collider>()) Object.Destroy(col);
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>()) Object.Destroy(rb);
            foreach (var animator in go.GetComponentsInChildren<Animator>()) animator.runtimeAnimatorController = null;
            foreach (var r in go.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }
}
