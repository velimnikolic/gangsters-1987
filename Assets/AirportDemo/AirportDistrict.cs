using System.Collections.Generic;
using RoadDemo;
using UnityEngine;

namespace AirportDemo
{
    // A 1987 American regional airport: a runway long and wide enough for the trijet
    // that brings the morning flight in, with its parallel taxiway and four
    // connectors, painted to the FAA's own dimensions and lit round its edges; a
    // continuous concrete ramp behind it with the light aeroplanes on their tie-down
    // rows, the two airline stands and the helipad; the row of box hangars and the
    // maintenance shop at the west end, the FBO and its fuel island, the terminal and
    // the control tower in the middle, the fire station, the freight shed and the fuel
    // farm at the east; the wire fence with its two gates; and landside, the kerb loop
    // under its canopy, the car park and the approach road.
    //
    // And it works: aeroplanes start up, taxi the graph, hold short of the runway,
    // line up, roll, climb out, fly the circuit or go off the map and come back down
    // final; a bowser meets whoever shuts down; the baggage train runs out to the
    // stand; passengers walk out of the terminal across the ramp, because in 1987 at
    // a field like this that is exactly what they did.
    //
    // It is a DISTRICT (RoadDemo.IDistrict): the same object builds it in its own
    // demo scene, hosted by a StandaloneDistrictHost, and off a shore of the city in
    // the game, hosted by the city - so what is changed here is what the city gets.
    // The field is laid out in its OWN coordinates - the runway along X at z = 0,
    // everything landside growing toward +Z, the approach road at the top - and the
    // frame turns the whole of it onto whichever shore the city rolled: the city lies
    // beyond the approach road, at the field's +Z. The still geometry rides its roots
    // into place (MoveIntoPlace); everything that flies, drives or walks works in the
    // coordinates of the Live root it hangs under, which is moved with the rest, so
    // the flight ops never hear of the frame (the walkers turn the world's obstacle
    // field into those coordinates as they go).
    //
    // The AIRCRAFT are Simple Airport's - the only pack in the project with any -
    // and are scaled at Play to the span their class flies at. EVERYTHING ELSE is
    // Synty: the people, the ground vehicles, the buildings, the furniture. That
    // split is deliberate; see the note at the top of AirportKit. What no pack has -
    // hangars at aircraft scale, the tower, the windsock, the airfield lights, the
    // ground equipment - is baked out of Synty modules by Editor/AirportKitBash
    // before Play.
    public partial class AirportDistrict : IDistrict
    {
        // ---------------------------------------------------------------- settings

        public int seed = 1987;
        /// <summary>Runway length in metres. 1800 m (6,000 ft) takes the trijet; 1200 is
        /// a plain general aviation strip and the jet will not use it.</summary>
        public float runwayLength = AirportSpec.RunwayLength;
        /// <summary>Which way the wind is blowing: with it westerly, runway 27 is in use
        /// and every circuit is flown to the west.</summary>
        public bool westerlyWind = true;
        /// <summary>Edge lights, threshold bars, PAPI and the beacon.</summary>
        public bool airfieldLighting = true;
        /// <summary>How many of the six box hangars stand open with an aeroplane inside.</summary>
        public int openHangars = 1;

        /// <summary>Scheduled aeroplanes, one to each airline stand. They land, the
        /// passengers walk off, the next lot walk on, and they go - and half of them
        /// are already inbound when the field opens, so the first thing seen is an
        /// arrival rather than four aeroplanes waiting on a clock.</summary>
        public int airlineAircraft = 4;
        /// <summary>Light singles that actually fly. Deliberately few: a county field
        /// sees a handful of light movements an hour, and a Cessna in the circuit every
        /// half minute reads as a flying school rather than as an airport.</summary>
        public int lightAircraft = 2;
        public int parkedAircraft = 18;
        public float commuterInterval = 220f;
        /// <summary>Three on the ramp: the sheriff's, which keeps its pad and flies a
        /// patrol; a charter that comes and goes; and an air ambulance that is not here
        /// at all until it drops in off the country.</summary>
        public bool helicopters = true;
        /// <summary>The passengers walk off and on. Off, the aeroplanes turn round on a
        /// timer and nobody crosses the ramp.</summary>
        public bool boarding = true;
        /// <summary>Bodies the turnarounds share. A passenger only exists while he is
        /// walking, so four stands working at once cost this many and not four cabins'
        /// worth.</summary>
        public int boardingPool = 20;

        public bool groundEquipment = true;
        public int rampCrew = 8;
        public int lorries = 1;

        public int cars = 13;
        public int parkedCars = 60;
        public int passengers = 34;
        /// <summary>A sheriff's car on the kerb and a plain sedan watching the general
        /// aviation gate - 1987, and this is how the cocaine came north.</summary>
        public bool theLaw = true;

        // ------------------------------------------------------------ roots, state

        Transform _groundRoot, _airsideRoot, _markingRoot, _lightRoot, _apronRoot,
                  _buildingRoot, _fenceRoot, _landsideRoot, _detailRoot, _floraRoot, _liveRoot;
        /// <summary>Every root that is carried into place - the still geometry and the
        /// Live root everything that moves works under.</summary>
        readonly List<Transform> _roots = new List<Transform>();
        IDistrictHost _host;

        System.Random _rng;
        PedClips _clips;
        FlightOps _flights;
        GroundOps _ground;
        AirportBoarding _boarding;
        AirportPeople _people;
        AirportTraffic _traffic;
        readonly List<Rotorcraft> _rotors = new List<Rotorcraft>();
        /// <summary>The parked cars, blocked to walkers once the field stands in place.</summary>
        readonly List<GameObject> _parkedBodies = new List<GameObject>();

        /// <summary>Half the runway, from the middle of the field.</summary>
        public float RunwayHalf => runwayLength * 0.5f;

        // ------------------------------------------------------------ the district

        DistrictFrame _frame = DistrictFrame.Identity;
        /// <summary>The frame the field's OWN coordinates go through: the district frame
        /// slid back to the field's own origin, the middle of the runway.</summary>
        DistrictFrame _inner = DistrictFrame.Identity;
        float[] _links;
        Rect _bounds;
        readonly List<DistrictPortal> _portals = new List<DistrictPortal>();
        readonly List<RoadEdge> _roads = new List<RoadEdge>();
        bool _placed;

        /// <summary>Where the district ends toward the city, in the field's own z: the
        /// far side of the approach junction's box - the portal stands on it, and the
        /// city's street arrives there.</summary>
        public const float BoundaryZ = AirportSpec.StreetZ + StreetKit.StreetHalf;

        public string Name => "Airport";

        public DistrictFrame Frame { get => _frame; set => _frame = value; }

        public Rect LocalBounds => _bounds;

        public IReadOnlyList<DistrictPortal> Portals => _portals;

        /// <summary>The airport the city rolled: its seed.</summary>
        public static AirportDistrict ForCity(DistrictSlot slot) => new AirportDistrict { seed = slot.seed };

        /// <summary>Contract coordinates of a point in the field's own frame: the city
        /// lies at +Z beyond the approach road, the field runs down below zero, and the
        /// one link - the approach - is at x = 0.</summary>
        static Vector3 ToContract(Vector3 own)
            => new Vector3(own.x - AirportSpec.ApproachX, own.y, own.z - BoundaryZ);

        /// <summary>The field's own coordinates of a point in the world of its own demo
        /// scene, where the standalone host slides the district so its south-west corner
        /// lies on the origin: what the scene's camera pivot is worked out from.</summary>
        public static Vector3 StandaloneWorld(Vector3 own)
            => new Vector3(own.x - AirportSpec.MapX0, own.y, own.z - AirportSpec.MapZ0);

        public void Plan(float[] links, int seed)
        {
            this.seed = seed;
            _links = links != null && links.Length > 0 ? links : null;
            _rng = new System.Random(seed);
            if (_links != null && _links.Length > 1)
                Debug.LogWarning("[Airport] a field has one road in; the city offered " + _links.Length +
                                 " - only the first is met, at the approach.");
            _bounds = Rect.MinMaxRect(AirportSpec.MapX0 - AirportSpec.ApproachX, AirportSpec.MapZ0 - BoundaryZ,
                                      AirportSpec.MapX1 - AirportSpec.ApproachX, 0f);
        }

        public void Reserve(DistrictReservations into)
        {
            // an airfield is the one place in a landscape made flat on purpose: the
            // island's ground is held dead level at the field's grass level from runway
            // end to runway end and a little beyond (the pavements stand a step over it,
            // the way they stand over the field's own grass plane in its own scene), and
            // nothing grows inside the wire or against it
            var world = _frame.ToWorldRect(_bounds);
            into.Level(Grow(world, 30f), AirportSpec.LandY);
            into.NoFlora(Grow(world, 14f));
        }

        static Rect Grow(Rect r, float by)
            => Rect.MinMaxRect(r.xMin - by, r.yMin - by, r.xMax + by, r.yMax + by);

        public void Build(IDistrictHost host)
        {
            _host = host;
            _inner = new DistrictFrame
            {
                origin = _frame.ToWorld(new Vector3(-AirportSpec.ApproachX, 0f, -BoundaryZ)),
                yaw = _frame.yaw,
            };

            // the hangars, the tower and the field furniture are baked before Play by
            // Editor/AirportDemoAutoBake (a runtime class cannot call the editor assembly)
            _groundRoot = Root("Airport Ground");
            _airsideRoot = Root("Airport Airside");
            _markingRoot = Root("Airport Markings");
            _lightRoot = Root("Airport Lights");
            _apronRoot = Root("Airport Apron");
            _buildingRoot = Root("Airport Buildings");
            _fenceRoot = Root("Airport Fence");
            _landsideRoot = Root("Airport Landside");
            _detailRoot = Root("Airport Detail");
            _floraRoot = Root("Airport Flora");
            _liveRoot = host.LiveRoot("Airport Live");
            _roots.Add(_liveRoot);

            LoadKit();
            // the grass: the field's own plane in its own scene; in the city the island
            // is the grass, held flat at the field's level (Reserve) from end to end
            if (!host.ProvidesGround) BuildGround();
            BuildRunway();          // then every paved surface over it
            BuildTaxiways();
            BuildApron();
            PaintRunway();          // the markings, once every surface is down
            PaintTaxiways();
            PaintApron();
            BuildAirfieldLights();
            BuildWindsock();
            BuildBuildings();
            BuildFence();
            BuildLandside();
            BuildDetail();

            _clips = host.Clips;
            if (_clips.Walk == null || _clips.Idle == null)
                Debug.LogWarning("[Airport] walk/idle clips missing under Assets/Animations/People - the people will slide.");

            BuildTaxiGraph();
            BuildBoarding();        // the pool of passengers, before anybody lands
            BuildFlights();
            BuildRotorcraft();
            BuildGroundOps();
            BuildLandsideTraffic();
            BuildPeople();
            BuildParkedAircraft();

            AssignCullLayers();
            StripColliders();

            // the field was drawn at its own origin, every piece put down in the field's
            // own coordinates; the roots now carry the whole of it onto its shore. What
            // flies, drives or walks after this works in the Live root's own coordinates,
            // which moved with it.
            MoveIntoPlace();
            BuildPortals();
            BlockTheField(host);
            host.RegisterRoads(_roads);
        }

        Transform Root(string name)
        {
            var t = _host.StaticRoot(name);
            _roots.Add(t);
            return t;
        }

        public void Tick(float dt)
        {
            if (dt <= 0f) return;
            _flights?.Tick(dt);
            for (int i = 0; i < _rotors.Count; i++) _rotors[i].Tick(dt);
            _ground?.Tick(dt);
            _boarding?.Tick(dt);
            _traffic?.Tick(dt);
            _people?.Tick(dt);
        }

        public void Dispose()
        {
            _people?.Dispose();
            _boarding?.Dispose();
            _ground?.Dispose();
        }

        // ------------------------------------------------------------ into place

        void MoveIntoPlace()
        {
            if (_placed) return;
            _placed = true;
            var rot = _inner.Rotation;
            foreach (var t in _roots) if (t != null) t.SetPositionAndRotation(_inner.origin, rot);
        }

        /// <summary>A point of the field's own plan, out in the world it stands in: what
        /// the lane graph the city welds onto is built in (Portals.cs).</summary>
        public Vector3 W(Vector3 own) => _inner.ToWorld(own);

        /// <summary>A heading of the field's own plan, in the world.</summary>
        public float WYaw(float ownYaw) => _inner.ToWorldYaw(ownYaw);

        /// <summary>A round of the field's own points, out in the world.</summary>
        public List<Vector3> WorldPoints(IList<Vector3> own)
        {
            var w = new List<Vector3>(own.Count);
            for (int i = 0; i < own.Count; i++) w.Add(_inner.ToWorld(own[i]));
            return w;
        }

        /// <summary>The frame the field's own coordinates go through.</summary>
        public DistrictFrame Placed => _inner;

        // ------------------------------------------------------------ rng helpers

        float Rnd() => (float)_rng.NextDouble();
        float Rnd(float lo, float hi) => lo + (float)_rng.NextDouble() * (hi - lo);
        int Rnd(int n) => _rng.Next(n);
        bool Chance(float p) => _rng.NextDouble() < p;
        T Pick<T>(IList<T> list) => list == null || list.Count == 0 ? default : list[_rng.Next(list.Count)];

        // The field is a plain object now, not a MonoBehaviour, so the two Unity calls
        // it leans on come through here rather than off a base class.
        static GameObject Instantiate(GameObject prefab, Transform parent)
            => Object.Instantiate(prefab, parent);

        static void Destroy(Object o) => Object.Destroy(o);
    }
}
