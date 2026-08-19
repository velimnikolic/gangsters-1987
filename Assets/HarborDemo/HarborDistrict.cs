using System.Collections.Generic;
using RoadDemo;
using UnityEngine;

namespace HarborDemo
{
    // A cargo port: one long quay along X with the sea to the south, berths at which
    // kit-bashed freighters sail in off the horizon, crab alongside, are worked -
    // containers lifted onto the quay stacks, forklifts shuttling pallets to the
    // sheds, dock hands walking their rounds - and sail out again the other way;
    // behind the quay the container yard, the warehouses with their roller doors to
    // the water, the wire fence and the gates, and the street beyond.
    //
    // It is a DISTRICT (RoadDemo.IDistrict): the same object builds it in its own
    // demo scene, hosted by a StandaloneDistrictHost, and on a shore of the city in
    // RoadDemo, hosted by the city - so what is changed here is what the city gets.
    // Everything is laid out in the port's own coordinates, quay along X and sea at
    // -Z, and the frame turns the whole thing onto whichever shore the city rolled:
    // south at yaw 0, west at 90, north at 180, east at 270.
    public partial class HarborDistrict : IDistrict
    {
        // ---------------------------------------------------------------- settings

        public int berths = 3;
        public float berthPitch = 90f;
        /// <summary>Depth of the concrete working area behind the quay, to the fence.
        /// Worked out again from the sheds while they are built.</summary>
        public float apronDepth = 65f;
        public int seed = 1987;

        public Vector2 stayRange = new Vector2(60f, 120f);
        public Vector2 gapRange = new Vector2(15f, 45f);
        public float sailSpeed = 8f;
        public bool passingTraffic = true;
        public bool quayCranes = true;
        public float shoreFoam = 0.25f;
        public float shallowSand = 0.6f;

        public int dockWorkers = 9;
        public int shipCrew = 6;
        public bool forklifts = true;
        public bool deliveryTruck = true;
        public int lorries = 4;

        // ------------------------------------------------------------ levels

        /// <summary>The water's surface: the palm city's own level under its quays.</summary>
        public const float WaterY = -2.65f;
        /// <summary>The land beyond the concrete, a whisker under the tile tops.</summary>
        public const float LandY = 0.05f;
        /// <summary>The top of the concrete tiles: where the men walk and the trucks roll.</summary>
        public const float TileTop = 0.1f;
        /// <summary>The seabed, out from under the quay wall.</summary>
        public const float SeabedY = -6f;
        /// <summary>The quay wall's face is this far south of the coping line (z = 0).</summary>
        public const float QuayFace = 1.51f;

        /// <summary>Half the quay's length: the berths and five metres of slack each end.</summary>
        public float QuayHalf => berths * berthPitch * 0.5f + 5f;

        // ------------------------------------------------------------ roots, state

        Transform _groundRoot, _quayRoot, _apronRoot, _yardRoot, _warehouseRoot, _fenceRoot, _streetRoot, _liveRoot;
        readonly List<Transform> _roots = new List<Transform>();
        IDistrictHost _host;

        System.Random _rng;
        HarborShipping _shipping;
        readonly List<HarborForklift> _forklifts = new List<HarborForklift>();
        readonly List<HarborCrane> _cranes = new List<HarborCrane>();
        readonly List<HarborWorker> _workers = new List<HarborWorker>();
        readonly List<HarborTruck> _trucks = new List<HarborTruck>();
        PedClips _clips;

        // ------------------------------------------------------------ the district

        DistrictFrame _frame = DistrictFrame.Identity;
        /// <summary>The frame the port's OWN coordinates go through. The port is drawn
        /// with the city behind it at +Z, so this is the district frame slid back to the
        /// port's own origin on the quay line.</summary>
        DistrictFrame _inner = DistrictFrame.Identity;
        float[] _links = { 0f };
        Rect _bounds;
        readonly List<DistrictPortal> _portals = new List<DistrictPortal>();
        readonly List<RoadEdge> _roads = new List<RoadEdge>();
        bool _placed;

        /// <summary>How far the sea reaches out from the quay: the lane the ships run
        /// on and their turn in, which must be open water whatever the coast does.</summary>
        const float BasinReach = 130f;
        /// <summary>Metres of ground behind the street, before the wild starts.</summary>
        const float BackMargin = 20f;

        public string Name => "Harbor";

        public DistrictFrame Frame { get => _frame; set => _frame = value; }

        public Rect LocalBounds => _bounds;

        public IReadOnlyList<DistrictPortal> Portals => _portals;

        /// <summary>The port the city rolled: its seed and how many berths it works.</summary>
        public static HarborDistrict ForCity(DistrictSlot slot)
        {
            var d = new HarborDistrict { seed = slot.seed };
            if (slot.sizeAcross > 0) d.berths = Mathf.Clamp(slot.sizeAcross, 1, 5);
            return d;
        }

        /// <summary>Contract coordinates of a point in the port's own frame: the city
        /// lies at +Z beyond the street, the port's body runs down to the sea.</summary>
        Vector3 ToContract(Vector3 own) => new Vector3(own.x + GateSpanCentre, own.y, own.z - PlannedStreetZ);

        /// <summary>Where the street behind the port runs, before the sheds have said
        /// how deep they are. The reservations and the bounds are worked out from this,
        /// so the ground is right whatever the kit turns out to measure; the build warns
        /// if the sheds want more.</summary>
        const float PlannedStreetZ = 120f;

        /// <summary>Halfway between the two gates: the point the city's links are
        /// measured from, so the port sits square under the road lines it hangs off.</summary>
        float GateSpanCentre => _links != null && _links.Length > 1
            ? (_links[0] + _links[_links.Length - 1]) * 0.5f
            : 0f;

        public void Plan(float[] links, int seed)
        {
            this.seed = seed;
            _links = links != null && links.Length > 0 ? links : null;
            _rng = new System.Random(seed);
            if (_links != null && _links.Length < 2)
                Debug.LogWarning("[Harbor] a port wants two road lines to hang its gates on; " +
                                 "with one it keeps its own and only one gate meets the city.");

            // the quay has to be long enough to carry both gates and their roads
            float span = _links != null ? _links[_links.Length - 1] - _links[0] : 0f;
            if (span > 0f)
            {
                float wanted = span * 0.5f + 40f;
                while (QuayHalf < wanted && berths < 6) berths++;
            }

            // the rectangle in contract coordinates: x measured from the middle of the
            // gate span, z from the street (0) down past the quay to the ships' water
            float half = Mathf.Max(QuayHalf + 40f, span * 0.5f + 60f);
            _bounds = Rect.MinMaxRect(-half, -(BasinReach + PlannedStreetZ), half, 0f);
        }

        public void Reserve(DistrictReservations into)
        {
            var world = _frame.ToWorldRect(_bounds);
            // the yard, the sheds and the street: the port paves its own ground
            var land = _frame.ToWorldRect(Rect.MinMaxRect(_bounds.xMin, -PlannedStreetZ - BackMargin,
                                                          _bounds.xMax, 0f));
            into.Pave(land);
            into.Level(Grow(land, 24f), LandY);
            into.NoFlora(Grow(land, 10f));
            // the basin: from the quay wall out past the lanes the ships run on, the
            // coast may not close in - a freighter has to be able to come alongside
            var basin = _frame.ToWorldRect(Rect.MinMaxRect(_bounds.xMin - 260f, -(BasinReach + PlannedStreetZ),
                                                           _bounds.xMax + 260f, -PlannedStreetZ - 1f));
            into.Sea(basin);
        }

        static Rect Grow(Rect r, float by)
            => Rect.MinMaxRect(r.xMin - by, r.yMin - by, r.xMax + by, r.yMax + by);

        public void Build(IDistrictHost host)
        {
            _host = host;
            _inner = new DistrictFrame
            {
                origin = _frame.ToWorld(new Vector3(GateSpanCentre, 0f, -PlannedStreetZ)),
                yaw = _frame.yaw,
            };

            _groundRoot = Root("Harbor Ground");
            _quayRoot = Root("Harbor Quay");
            _apronRoot = Root("Harbor Apron");
            _yardRoot = Root("Harbor Yard");
            _warehouseRoot = Root("Harbor Warehouses");
            _fenceRoot = Root("Harbor Fence");
            _streetRoot = Root("Harbor Streetscape");
            _liveRoot = host.LiveRoot("Harbor Live");
            _roots.Add(_liveRoot);

            LoadGroundKit();
            BuildWarehouses();      // first: their backs fix the road, the fence and the apron
            if (!host.ProvidesGround)
            {
                BuildWater();
                BuildGround();
            }
            BuildApron();
            BuildQuay();
            BuildContainerYard();
            BuildYardRoads();
            BuildFence();
            BuildBackStreet();
            DressYard();
            BuildDetail();

            if (_streetZ > PlannedStreetZ + 1f)
                Debug.LogWarning($"[Harbor] the sheds want the street at z {_streetZ:F0}, further out than the " +
                                 $"{PlannedStreetZ:F0} m the ground was reserved for - the city's approach will be short.");

            _clips = host.Clips;
            BuildShipping();
            BuildForklifts();
            BuildWorkers();
            BuildTraffic();

            // the port was drawn at its own origin, every piece put down in the port's
            // own coordinates; the roots now carry the whole of it onto its shore. What
            // walks or sails after this works in world coordinates it was handed
            // through the frame (WorldPoints, HarborCargo.Frame, HarborCrane.Frame) or
            // in its own local ones under a root that has moved (the ships).
            MoveIntoPlace();
            BuildPortals();

            for (int i = 0; i < _workers.Count; i++) host.RegisterWalker(_workers[i]);
            host.RegisterRoads(_roads);
            BlockTheYard(host);
        }

        Transform Root(string name)
        {
            var t = _host.StaticRoot(name);
            _roots.Add(t);
            return t;
        }

        public void Tick(float dt)
        {
            // the cranes open the frame, the cargo handlers drive them through the
            // shipping's tick, and whichever was not asked for parks its gear again
            for (int i = 0; i < _cranes.Count; i++) _cranes[i].BeginFrame(dt);
            _shipping?.Tick(dt);
            for (int i = 0; i < _cranes.Count; i++) _cranes[i].EndFrame();
            for (int i = 0; i < _forklifts.Count; i++) _forklifts[i].Tick(dt);
            for (int i = 0; i < _trucks.Count; i++) _trucks[i].Tick(dt);
            // the workers are the host's: it ticks them with the rest of the crowd
            if ((Time.frameCount & 63) == 0) PruneWorkers();
        }

        public void Dispose()
        {
            for (int i = 0; i < _workers.Count; i++) _workers[i].Dispose();
            _shipping?.Dispose();
        }

        // ------------------------------------------------------------ into place

        void MoveIntoPlace()
        {
            if (_placed) return;
            _placed = true;
            var rot = _inner.Rotation;
            foreach (var t in _roots) if (t != null) t.SetPositionAndRotation(_inner.origin, rot);
        }

        /// <summary>A point of the port's own plan, out in the world it stands in. The
        /// still geometry rides its roots, but anything that walks the world every frame
        /// - the dock hands, the lorries - is given its route in these.</summary>
        public Vector3 W(Vector3 own) => _inner.ToWorld(own);

        /// <summary>A route of the port's own points, out in the world.</summary>
        public List<Vector3> WorldPoints(IList<Vector3> own)
        {
            var w = new List<Vector3>(own.Count);
            for (int i = 0; i < own.Count; i++) w.Add(_inner.ToWorld(own[i]));
            return w;
        }

        /// <summary>The frame the port's own coordinates go through - for the gear that
        /// has to turn a world point back into the port's own (the cranes).</summary>
        public DistrictFrame Placed => _inner;

        // The port is a plain object now, not a MonoBehaviour, so the two Unity calls
        // it leans on all over come through here rather than off a base class.
        static GameObject Instantiate(GameObject prefab, Transform parent)
            => Object.Instantiate(prefab, parent);

        static GameObject Instantiate(GameObject prefab, Vector3 pos, Quaternion rot, Transform parent)
            => Object.Instantiate(prefab, pos, rot, parent);

        static void Destroy(Object o) => Object.Destroy(o);
    }
}
