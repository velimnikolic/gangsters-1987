using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// The city core as a quarter: the blocks harvested out of the POLYGON City demo,
    /// dealt into rows by the seed (<see cref="CoreLayout.Arrange"/>), the roads
    /// <see cref="CoreRoads"/> runs between them, and the lane graph the traffic rides
    /// over both (Docs/core-district-plan.md).
    ///
    /// Nothing here drives. The lane graph is the city's own - <see cref="LaneNet"/>
    /// nodes, carriageways and lanes, laid the way <c>RoadDemoBuilder.BuildGraph</c> lays
    /// the grid's - and the cars are the city's <see cref="DemoVehicle"/>. This class
    /// only says WHERE the roads are; how a car takes a corner is the shared code's, and
    /// is not touched from here.
    ///
    /// The raster hands over two things and the rest follows from them: a junction box
    /// wherever roads cross (<see cref="CoreRoads.Raster.Junctions"/>) and a stretch of
    /// road between two of those (<see cref="CoreRoads.Raster.Stretches"/>). A box becomes
    /// a <see cref="RoadNode"/>, a stretch becomes a <see cref="Carriageway"/> with a lane
    /// each way, and <see cref="LaneNet.Finish"/> builds every way across every box.
    ///
    /// Not here yet: the pavement graph and the people on it, the traffic lights at the
    /// four crossings Synty put them on, and the PORTALS the city welds its own streets
    /// onto - so for now this stands on its own, in its own scene, and the cars in it are
    /// what says whether the roads read (Docs/core-district-plan.md, 2.3).
    /// </summary>
    public sealed class CoreDistrict : IDistrict
    {
        /// <summary>Cars in the quarter's traffic. Twenty-four is what the quarter
        /// carries without a queue standing: at forty the harness finds cars touching in
        /// two runs of six, at twenty-four in none of five (Docs/play-harness.md).</summary>
        public int carCount = 24;
        public float streetSpeed = 9f;
        public float boulevardSpeed = 13f;
        /// <summary>An alley is one way and slow: nobody hurries down five metres of it.</summary>
        public float alleySpeed = 5f;

        public string Name => "Core";
        public DistrictFrame Frame { get; set; } = DistrictFrame.Identity;
        public Rect LocalBounds => _bounds;
        public IReadOnlyList<DistrictPortal> Portals => _portals;

        /// <summary>The lane graph, once <see cref="Build"/> has run.</summary>
        public LaneNet Net { get; private set; }

        /// <summary>The raster the quarter was drawn off - its map and its report are
        /// what a probe reads to see whether the drawing came out.</summary>
        public CoreRoads.Raster Raster => _raster;

        readonly List<CoreLayout.Block> _blocks = new List<CoreLayout.Block>();
        readonly List<DemoVehicle> _vehicles = new List<DemoVehicle>();
        readonly List<RoadEdge> _edges = new List<RoadEdge>();
        readonly List<DistrictPortal> _portals = new List<DistrictPortal>();

        CoreRoads.Raster _raster;
        Rect _bounds;
        Transform _yard;          // the blocks stand here between Plan and Build
        int _seed = 1987;

        // ------------------------------------------------------------------ plan

        /// <summary>
        /// Stands the blocks up and reads the roads off them. The blocks have to BE
        /// somewhere to be measured - a prefab's renderers only report their real size
        /// once the thing is in a scene - so they go up here, off to one side and out of
        /// everyone's way, and <see cref="Build"/> hands them to the host.
        /// </summary>
        public void Plan(float[] links, int seed)
        {
            _seed = seed;
            _yard = new GameObject("Core (unplaced)").transform;
            _blocks.Clear();
            foreach (var stand in CoreLayout.Blocks)
            {
                var prefab = DemoAssetLoad.Load<GameObject>(CoreLayout.BlocksDir + stand.Prefab + ".prefab");
                if (prefab == null) continue;
                var go = Object.Instantiate(prefab, Vector3.zero, Quaternion.identity, _yard);
                go.name = stand.Prefab;
                _blocks.Add(CoreLayout.Measure(stand.Prefab, go));
            }
            // the seed deals the rows and the drawing is judged before it is taken; the
            // Synty seed asks for the demo's own arrangement instead
            _plan = CoreLayout.Arrange(_blocks, seed, out _raster);
            foreach (var block in _blocks) CoreLayout.Place(block);
            StandParks();
            StandQuays();
            _bounds = Rect.MinMaxRect(_raster.X0, _raster.Z0,
                                      _raster.X(_raster.NX), _raster.Z(_raster.NZ));
        }

        /// <summary>
        /// The promenade, stretch by stretch - the river's ground the deal cut, composed to
        /// its plan (<see cref="QuayWalk.ForQuay"/>) the way the parks are: at the origin,
        /// then moved to the stretch's corner, under the same unplaced yard.
        /// </summary>
        void StandQuays()
        {
            if (_plan == null || _plan.Quays.Count == 0) return;
            Composer.ForgetMissing();
            var wants = QuayWalk.Cast(_plan);
            for (int q = 0; q < _plan.Quays.Count; q++)
            {
                var block = _plan.Quays[q];
                var root = new GameObject(block.Name).transform;
                root.SetParent(_yard, false);
                var box = block.Box;
                int dice = unchecked(_seed * 7919 + Mathf.RoundToInt(box.xMin) * 104729 + Mathf.RoundToInt(box.yMin) * 1299709);
                var walk = QuayWalk.ForQuay(_plan, block, wants[q], new System.Random(dice));
                var stood = QuayBlocks.Compose(walk, root, new System.Random(dice),
                    (prefab, parent) => Object.Instantiate(prefab, parent));
                QuayBlocks.Pave(walk, root, out _, (prefab, parent) => Object.Instantiate(prefab, parent), dice);
                CoreLayout.PlaceQuay(_plan, block, root);
                if (stood.Gaps > 0 || stood.RailGap > 0.5f || stood.OnWalk > 0)
                    Debug.LogWarning($"[Core] {block.Name}: {stood.Gaps} cell(s) with no floor, " +
                                     $"{stood.RailGap:F1} m of railing missing, {stood.OnWalk} thing(s) in the way.");
            }
        }

        /// <summary>
        /// The deal's parks, composed into the rectangles it gave them.
        ///
        /// A park is the one block in the core with no prefab behind it: the deal decides how
        /// big it is and the recipe fills that. Built under the same unplaced yard as the
        /// blocks, so <see cref="Build"/> carries the whole quarter into the world in one
        /// move - and composed at the ORIGIN before being moved, because every piece is
        /// placed by measuring where it lands.
        /// </summary>
        void StandParks()
        {
            if (_plan == null || _plan.Parks.Count == 0) return;
            ParkBlocks.ForgetMissing();

            foreach (var block in _plan.Parks)
            {
                var root = new GameObject(block.Name).transform;
                root.SetParent(_yard, false);

                var box = block.Box;
                int nx = Mathf.Max(3, Mathf.RoundToInt(box.width / CoreLayout.Cell));
                int nz = Mathf.Max(3, Mathf.RoundToInt(box.height / CoreLayout.Cell));
                int dice = unchecked(_seed * 7919 + Mathf.RoundToInt(box.xMin) * 104729 +
                                     Mathf.RoundToInt(box.yMin) * 1299709);

                var walk = ParkWalk.Lay(nx, nz, ParkWalk.Edge.Alone(), new System.Random(dice));
                var stood = ParkBlocks.Compose(walk, root, new System.Random(dice),
                    (prefab, parent) => Object.Instantiate(prefab, parent));
                ParkBlocks.Pave(walk, root, out _,
                    (prefab, parent) => Object.Instantiate(prefab, parent), dice);

                root.position = new Vector3(box.xMin, 0f, box.yMin);

                if (stood.Gaps > 0 || stood.FenceGap > 0.5f)
                    Debug.LogWarning($"[Core] {block.Name}: {stood.Gaps} cell(s) with no floor, " +
                                     $"{stood.FenceGap:F1} m of fence missing.");
            }
        }

        /// <summary>The plan the quarter was dealt: which seed, which deal of it, and the
        /// rows the blocks went into.</summary>
        public CoreLayout.Plan Layout => _plan;
        CoreLayout.Plan _plan;

        public void Reserve(DistrictReservations into)
        {
            var world = Frame.ToWorldRect(_bounds);
            into.Pave(world);
            into.Level(Rect.MinMaxRect(world.xMin - 20f, world.yMin - 20f, world.xMax + 20f, world.yMax + 20f),
                       RoadDemoBuilder.RoadBed);
            into.NoFlora(world);
        }

        // ----------------------------------------------------------------- build

        public void Build(IDistrictHost host)
        {
            var quarter = new GameObject("Core Quarter").transform;
            quarter.SetParent(host.StaticRoot("Core"), false);
            _yard.SetParent(quarter, false);
            _yard.name = "Blocks";

            var roads = new GameObject("Roads").transform;
            roads.SetParent(quarter, false);
            // the road's tiles go down over the water too - the bridge's deck - but not over
            // the channels the leaves span
            CoreRoads.Lay(_raster, (prefab, parent) => Object.Instantiate(prefab, parent), roads,
                          RiverBridge.Skip(_plan, _raster));
            var river = new GameObject("River").transform;
            river.SetParent(quarter, false);
            RiverBridge.Dress(_plan, river, (prefab, parent) => Object.Instantiate(prefab, parent));
            // the fairground's wheel turns, as the grid city's does
            foreach (var t in _yard.GetComponentsInChildren<Transform>(true))
                if (t.name.Contains("Ferris") && t.name.Contains("_Rotate") && t.GetComponent<DemoFerrisWheel>() == null)
                    t.gameObject.AddComponent<DemoFerrisWheel>();

            // everything above was laid in the quarter's own coordinates; the frame is
            // where the city put it, and the lane graph below is built in world ones
            quarter.SetPositionAndRotation(Frame.origin, Frame.Rotation);

            BuildLaneGraph();
            InstallBascules(host, river);
            SpawnCars(host.LiveRoot("Core Traffic"));

            host.RegisterRoads(_edges);
            for (int i = 0; i < _vehicles.Count; i++) host.RegisterVehicle(_vehicles[i]);
            BlockTheBuildings(host);

            Debug.Log($"[Core] {_plan.Name}: {_blocks.Count} blocks, {_raster.Junctions.Count} junctions, " +
                      $"{_raster.Stretches.Count} stretches of road, {_edges.Count} lanes, " +
                      $"{_vehicles.Count} cars, {_raster.Faults} faults.{System.Environment.NewLine}" +
                      string.Join(System.Environment.NewLine, _plan.Rows) + System.Environment.NewLine + _raster.Report);
        }

        /// <summary>
        /// The bridges open: every bridge's leaves, stood shut by <see cref="RiverBridge"/>,
        /// get their <see cref="Bascule"/> on the carriageway the lane graph laid over the
        /// channel, and one sailboat - the boat on the river no shut bridge can pass, its
        /// mast is 13.7 m - sails the whole line (<see cref="RiverBoat"/>), calling each
        /// bridge as it comes to it.
        /// </summary>
        void InstallBascules(IDistrictHost host, Transform river)
        {
            if (Net == null || _plan.Quays.Count == 0 || _plan.Bridges.Count == 0) return;
            var line = _plan.River;
            float mid = (line.Wall + line.FarWater) * 0.5f;
            var from = Frame.ToWorld(new Vector3(mid, RiverBridge.WaterY, line.Z0 - RiverBridge.Reach + 10f));
            var to = Frame.ToWorld(new Vector3(mid, RiverBridge.WaterY, line.Z1 + RiverBridge.Reach - 10f));
            var axis = (to - from).normalized;

            var bridges = new List<Bascule>();
            var along = new List<float>();
            foreach (var bridge in _plan.Bridges)
            {
                var deck = river.Find(RiverBridge.DeckName(bridge));
                if (deck == null) continue;
                var channel = RiverBridge.ChannelOf(_plan, bridge);
                // the carriageway over the channel: the one the channel's middle lies on
                var centre = Frame.ToWorld(new Vector3(channel.center.x, 0f, channel.center.y));
                Carriageway best = null;
                float bestOff = 3f, bestS = 0f;
                foreach (var road in Net.Roads)
                {
                    float s = Vector3.Dot(centre - road.A, road.Axis);
                    if (s < 0f || s > road.Length) continue;
                    float off = Mathf.Abs(Vector3.Dot(centre - road.A, road.Right));
                    if (off < bestOff) { bestOff = off; best = road; bestS = s; }
                }
                if (best == null)
                {
                    Debug.LogWarning($"[Core] no carriageway crosses the channel of {deck.name}; it stays shut.");
                    continue;
                }
                var bascule = deck.gameObject.AddComponent<Bascule>();
                bascule.Road = best;
                bascule.S0 = bestS - RiverBridge.Channel * 0.5f;
                bascule.S1 = bestS + RiverBridge.Channel * 0.5f;
                foreach (Transform piece in deck)
                    if (piece.name.Contains(" leaf")) bascule.Leaves.Add(piece);
                bridges.Add(bascule);
                along.Add(Vector3.Dot(centre - from, axis));
            }
            if (bridges.Count == 0) return;

            var sail = DemoAssetLoad.Load<GameObject>("Assets/Synty/PolygonPalmCity/Prefabs/Vehicles/SM_Veh_Sailboat_01.prefab");
            if (sail == null)
            {
                Debug.LogWarning("[Core] the palm city's sailboat is missing; the bridges stay shut.");
                return;
            }
            var boat = Object.Instantiate(sail, host.LiveRoot("Core River"));
            boat.name = "Sailboat";
            boat.transform.position = from;
            var run = boat.AddComponent<RiverBoat>();
            run.From = from;
            run.To = to;
            run.Bridges = bridges;
            run.Along = along;
        }

        /// <summary>
        /// The lane graph: a node for every junction box, a carriageway down every stretch
        /// of road between two of them, and the lanes on it at the offsets the city uses -
        /// one each way on a street, two each way and a median on the boulevard, one alone
        /// down a one-way alley. LaneNet.Finish lays the connectors and the conflict table
        /// across every box, exactly as it does for the grid.
        /// </summary>
        void BuildLaneGraph()
        {
            // the graph itself is RasterGraph's: the industrial quarter reads the same
            // raster and wants the same graph off it, and the three faults the harness
            // found in this one (a lane ending in mid air, two dead ends facing each other,
            // a stretch too short to stand a car on) are not worth learning twice
            Net = RasterGraph.Build(_raster, Frame, streetSpeed, boulevardSpeed, alleySpeed);
            _edges.Clear();
            _edges.AddRange(Net.Edges);
        }

        // ------------------------------------------------------------------ cars

        /// <summary>
        /// The quarter's traffic, spread over the lanes the way the city spreads its own:
        /// a car every eighteen metres round and round the lane list until the count is
        /// met, each one a plain DemoVehicle on the graph. It is the city's car, driven by
        /// the city's code; only the roads under it are this quarter's.
        /// </summary>
        void SpawnCars(Transform parent)
        {
            if (carCount <= 0 || _edges.Count == 0) return;
            var dice = new System.Random(_seed);
            int placed = 0;
            for (int round = 0; placed < carCount && round < 40; round++)
            {
                bool any = false;
                foreach (var edge in _edges)
                {
                    if (placed >= carCount) break;
                    float s = 6f + round * 18f;
                    if (s > edge.Length - 12f) continue;
                    any = true;

                    var prefab = CoreRoads.PickCar(dice);
                    if (prefab == null) return;
                    var go = Object.Instantiate(prefab, Vector3.zero, Quaternion.identity, parent);
                    LivingCity.Gameplay.VehiclePaint.Apply(go, prefab);
                    foreach (var body in go.GetComponentsInChildren<Rigidbody>()) Object.Destroy(body);
                    foreach (var collider in go.GetComponentsInChildren<Collider>()) Object.Destroy(collider);

                    var box = new Bounds(go.transform.position, Vector3.zero);
                    foreach (var renderer in go.GetComponentsInChildren<Renderer>()) box.Encapsulate(renderer.bounds);
                    var car = new DemoVehicle
                    {
                        Tf = go.transform,
                        HalfLen = box.extents.z + 0.3f,
                        HalfWide = Mathf.Clamp(box.extents.x, 0.7f, 1.3f),
                    };
                    car.Spawn(edge, s);
                    _vehicles.Add(car);
                    StreetTraffic.Users.Add(car);   // the men on foot, and the outfit's drivers, see it
                    placed++;
                }
                if (!any) break;
            }
        }

        /// <summary>Every building's box, so a man off the pavement walks round it and the
        /// map has something to put a card on.</summary>
        void BlockTheBuildings(IDistrictHost host)
        {
            foreach (var block in _blocks)
            {
                if (block.Go == null) continue;
                foreach (Transform piece in block.Go.transform)
                {
                    if (!piece.name.StartsWith("SM_Bld_", System.StringComparison.OrdinalIgnoreCase)) continue;
                    var box = new Bounds();
                    bool any = false;
                    foreach (var renderer in piece.GetComponentsInChildren<Renderer>(true))
                    {
                        if (!any) { box = renderer.bounds; any = true; }
                        else box.Encapsulate(renderer.bounds);
                    }
                    if (any) host.Blocked(box, block.Name);
                }
            }
        }

        public void Tick(float dt) { }

        public void Dispose()
        {
            // the yard is the blocks' home between Plan and Build; if Build never came,
            // it is still standing where Plan left it
            if (_yard != null && _yard.parent == null) Object.Destroy(_yard.gameObject);
        }
    }
}
