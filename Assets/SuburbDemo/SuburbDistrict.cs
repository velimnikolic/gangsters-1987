using System.Collections.Generic;
using RoadDemo;
using UnityEngine;

namespace SuburbDemo
{
    // An American suburb out of Synty POLYGON Town, built from nothing but the pack:
    // a small grid of two-lane streets with a few cul-de-sacs, blocks carved into
    // house lots (a preset house each, driveway, path, fences, a yard), a church, a
    // corner of shops with a gas pump, a pocket park with a playground - and life:
    // cars on the lane graph, residents on the pavements going in and out of their
    // houses. Layout and looks are the Town demo scene's own.
    //
    // It is a DISTRICT (RoadDemo.IDistrict): the same object builds it in its own
    // demo scene, hosted by a StandaloneDistrictHost, and out on the edge of the city
    // in RoadDemo, hosted by the city itself - so what is changed here is what the
    // city gets. Everything is laid out in the suburb's own coordinates from (0,0)
    // and only put in its place at the end (MoveIntoPlace), which is why the frame
    // costs nothing to the code that draws houses.
    //
    // Geometry on a 5 m lattice. A street is 2 road tiles (10 m) with a 5 m sidewalk
    // either side. Lots are cut off the block frontages in 15 / 20 / 25 / 30 m widths
    // and up to `lotDepth` deep; two rows of lots stand back to back in a block.
    public partial class SuburbDistrict : IDistrict
    {
        public const float Cell = TownKit.Cell;
        const float StreetHalf = 5f;   // axis to kerb: two 5 m lanes
        const float Walk = 5f;         // sidewalk width
        const float BulbHalf = 10f;    // cul-de-sac turning bulb: 20 x 20 m of road
        const float WalkY = 0.13f;     // top of a Town sidewalk tile

        // ---------------------------------------------------------------- settings
        //
        // The defaults are the suburb: the demo scene's thin component copies its own
        // inspector over them, the city takes them as they stand and only says how big
        // and which seed. Change one here and both scenes change.

        public int seed = 1987;
        public int columns = 4;
        public int rows = 4;
        /// <summary>Block lengths (metres, multiples of 5) dealt across the columns -
        /// short, the Town demo's way: three to five houses a side.</summary>
        public int[] blockLengths = { 70, 85, 100 };
        /// <summary>Lot depth, metres; a block is two lot rows back to back. 25 m: the
        /// demo's 3.5 m setback, the house, a 5-10 m yard.</summary>
        public float lotDepth = 25f;
        public int culDeSacs = 4;
        public float stubLength = 20f;
        public float fencedFronts = 0.7f;
        public float treeDensity = 1f;

        public int carCount = 14;
        public int pedestrianCount = 40;
        public float insideAtStart = 0.4f;
        public int yardIdlers = 6;
        public float streetSpeed = 9f;
        public float enterChance = 0.35f;
        public Vector2 insideSeconds = new Vector2(20f, 90f);
        public Vector2 chatSeconds = new Vector2(6f, 14f);
        public bool debugFronts = false;

        // ------------------------------------------------------------ roots, state

        Transform _groundRoot, _streetRoot, _lotRoot, _placeRoot, _floraRoot, _lifeRoot;
        readonly List<Transform> _roots = new List<Transform>();
        System.Random _rng;
        IDistrictHost _host;

        readonly List<DemoVehicle> _vehicles = new List<DemoVehicle>();
        readonly List<CivilianAgent> _civilians = new List<CivilianAgent>();
        readonly List<PedestrianAgent> _idlers = new List<PedestrianAgent>();
        readonly List<TrafficSignal> _signals = new List<TrafficSignal>();
        CityLife _life;

        // ------------------------------------------------------------ the district

        DistrictFrame _frame = DistrictFrame.Identity;
        /// <summary>The frame the suburb's OWN coordinates go through: the district's
        /// frame slid back the depth of the map, because the city lies at the contract's
        /// local +Z and the suburb is laid out from its own south-west corner.</summary>
        DistrictFrame _inner = DistrictFrame.Identity;
        float[] _links = { 0f };
        int[] _pinColumns = { 1 };
        Rect _bounds;
        readonly List<DistrictPortal> _portals = new List<DistrictPortal>();
        bool _placed;

        public string Name => "Suburb";

        /// <summary>The suburb the city rolled: its own seed, and how deep it runs.
        /// How WIDE is not asked - the columns are cut to fit the road lines it is
        /// pinned to (SplitRun), which is the whole point of the pins.</summary>
        public static SuburbDistrict ForCity(DistrictSlot slot)
        {
            var d = new SuburbDistrict { seed = slot.seed };
            if (slot.sizeDeep > 0) d.rows = Mathf.Clamp(slot.sizeDeep, 1, 6);
            return d;
        }

        public DistrictFrame Frame { get => _frame; set => _frame = value; }

        public Rect LocalBounds => _bounds;

        public IReadOnlyList<DistrictPortal> Portals => _portals;

        /// <summary>Contract coordinates of a point in the suburb's own frame: the city
        /// lies at +Z, the suburb's north edge is z = 0 and its body runs down to -MapHeight.</summary>
        Vector3 ToContract(Vector3 own) => new Vector3(own.x - PinShift, own.y, own.z - MapHeight);

        float PinShift => _vx != null && _pinColumns != null && _pinColumns.Length > 0 &&
                          _pinColumns[0] < _vx.Length ? _vx[_pinColumns[0]] : 0f;

        public void Plan(float[] links, int seed)
        {
            this.seed = seed;
            _rng = new System.Random(seed);
            _links = links != null && links.Length > 0 ? links : null;

            LoadKit();
            PlanLines();
            PlanCuts();
            BuildNetwork();
            OpenGates();
            MarkStreets();
            CarveLots();
            PlanPlaces();

            _bounds = Rect.MinMaxRect(-PinShift, -MapHeight, MapWidth - PinShift, 0f);
        }

        public void Reserve(DistrictReservations into)
        {
            var world = _frame.ToWorldRect(_bounds);
            into.Pave(world);
            into.Level(Grow(world, 30f), 0f);
            into.NoFlora(Grow(world, 10f));
        }

        static Rect Grow(Rect r, float by)
            => Rect.MinMaxRect(r.xMin - by, r.yMin - by, r.xMax + by, r.yMax + by);

        public void Build(IDistrictHost host)
        {
            _host = host;
            _inner = new DistrictFrame
            {
                origin = _frame.ToWorld(new Vector3(-PinShift, 0f, -MapHeight)),
                yaw = _frame.yaw,
            };

            _groundRoot = Root("Suburb Ground");
            _streetRoot = Root("Suburb Streets");
            _lotRoot = Root("Suburb Lots");
            _placeRoot = Root("Suburb Places");
            _floraRoot = Root("Suburb Flora");
            _lifeRoot = host.LiveRoot("Suburb Life");

            ComposeLots();      // decides every lot's surfaces and members
            ComposePlaces();
            LayGround(host.ProvidesGround);
            BuildStreetFurniture();
            BuildBusStops();
            BuildLeftovers();

            BuildLaneGraph();
            BuildPedGraph();
            BuildDoors(host.Life);

            // everything so far was laid out in the suburb's own coordinates; now it
            // goes where the city put it, geometry and graphs together, and only then
            // does anything start walking about
            MoveIntoPlace();
            BuildPortals();

            SpawnCars();
            SpawnResidents();
            SpawnIdlers();
            StripColliders();   // and the click boxes after them

            host.RegisterRoads(_edges);
            host.RegisterPavement(_pedLinks);
            for (int i = 0; i < _signals.Count; i++) host.RegisterSignal(_signals[i]);
            for (int i = 0; i < _vehicles.Count; i++) host.RegisterVehicle(_vehicles[i]);
            for (int i = 0; i < _civilians.Count; i++) host.RegisterCivilian(_civilians[i]);
            for (int i = 0; i < _idlers.Count; i++) host.RegisterWalker(_idlers[i]);
            BlockTheBuildings(host);
        }

        Transform Root(string name)
        {
            var t = _host.StaticRoot(name);
            _roots.Add(t);
            return t;
        }

        /// <summary>The district ticks nothing of its own: its cars, its residents and
        /// its idlers were handed to the host, which ticks them with the city's.</summary>
        public void Tick(float dt) { }

        public void Dispose()
        {
            for (int i = 0; i < _civilians.Count; i++) _civilians[i].Dispose();
            for (int i = 0; i < _idlers.Count; i++) _idlers[i].Dispose();
        }

        // ------------------------------------------------------------ into place

        /// <summary>Move the whole suburb from its own corner to where the city put it:
        /// the roots carry the geometry, and the data that was written in world
        /// coordinates while it was built - the lane graph, the pavement, the doors -
        /// is turned with them.</summary>
        void MoveIntoPlace()
        {
            if (_placed) return;
            _placed = true;
            var rot = _inner.Rotation;
            foreach (var t in _roots) if (t != null) t.SetPositionAndRotation(_inner.origin, rot);
            if (_lifeRoot != null) _lifeRoot.SetPositionAndRotation(_inner.origin, rot);
            if (_frame.yaw == 0 && _inner.origin == Vector3.zero) return;

            var nodes = new HashSet<RoadNode>();
            foreach (var e in _edges)
            {
                if (e.From != null) nodes.Add(e.From);
                if (e.To != null) nodes.Add(e.To);
                e.Start = _inner.ToWorld(e.Start);
                e.End = _inner.ToWorld(e.End);
                e.Dir = _inner.ToWorldDir(e.Dir);
            }
            foreach (var n in nodes) MoveNode(n);

            var walked = new HashSet<PedNode>();
            foreach (var l in _pedLinks)
            {
                if (l.From != null) walked.Add(l.From);
                if (l.To != null) walked.Add(l.To);
            }
            foreach (var n in walked) n.Pos = _inner.ToWorld(n.Pos);

            foreach (var d in _doors)
            {
                d.Pos = _inner.ToWorld(d.Pos);
                d.EntryPos = _inner.ToWorld(d.EntryPos);
                d.Outward = _inner.ToWorldDir(d.Outward);
            }
        }

        void MoveNode(RoadNode n)
        {
            var a = _inner.ToWorld(new Vector3(n.XMin, 0f, n.ZMin));
            var b = _inner.ToWorld(new Vector3(n.XMax, 0f, n.ZMax));
            var c = _inner.ToWorld(new Vector3(n.X, 0f, n.Z));
            n.X = c.x; n.Z = c.z;
            n.XMin = Mathf.Min(a.x, b.x); n.XMax = Mathf.Max(a.x, b.x);
            n.ZMin = Mathf.Min(a.z, b.z); n.ZMax = Mathf.Max(a.z, b.z);
        }

        /// <summary>A point of the suburb's own plan, in the world it now stands in.</summary>
        Vector3 W(Vector3 own) => _placed ? _inner.ToWorld(own) : own;

        // ------------------------------------------------------------ rng helpers

        float Rnd() => (float)_rng.NextDouble();
        float Rnd(float lo, float hi) => lo + (float)_rng.NextDouble() * (hi - lo);
        int Rnd(int n) => _rng.Next(n);
        bool Chance(float p) => _rng.NextDouble() < p;
        T Pick<T>(IList<T> list) => list[_rng.Next(list.Count)];
    }
}
