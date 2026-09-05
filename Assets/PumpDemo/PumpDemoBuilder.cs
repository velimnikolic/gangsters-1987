using System.Collections.Generic;
using RoadDemo;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace PumpDemo
{
    // One road out of town and a filling station on the north side of it: the bench
    // the forecourt is watched on.
    //
    // Nothing on it is new life. The station is the Town pack's own whole gas station
    // (TownClusters.GasStation, the same cluster the city stands beside its connecting
    // roads); the cars are the city's traffic (StreetTraffic / RoadCar); the crowd is
    // the city's crowd (CivilianAgent), which chats in pairs and goes into shops
    // because the city's crowd does; and the errand that turns an ordinary car into a
    // customer is FuelCustomer, which lives with the rest of the driving so the city's
    // wayside station can be given one later without a line of it changing.
    //
    // What this scene does is take the city away and leave the forecourt: a road with
    // traffic on it, a pavement with people on it, a shop with a door, and two pumps
    // under a canopy. Editor only, like the demos it is built from - bodies, clips and
    // props come through the AssetDatabase.
    public class PumpDemoBuilder : MonoBehaviour
    {
        [Header("The road")]
        [Tooltip("How long the road runs between its two ends. It is laid on the 5 m tile beat.")]
        public float roadLength = 260f;
        [Tooltip("How far south the back road runs. It carries nothing and is dressed with " +
                 "nothing; it is there so the traffic has a CIRCUIT. A single road with a " +
                 "dead end at each end is not a road, it is a car park: thirteen cars drove " +
                 "west, met the end, and the whole demo stood there for three minutes.")]
        public float backRoadZ = -90f;
        [Tooltip("Where the side street joins the main road. WITHOUT IT THE CIRCUIT IS A " +
                 "ONE-WAY TRAP: a rectangle of two-lane roads is two SEPARATE one-way " +
                 "cycles - anticlockwise on one set of lanes, clockwise on the other - " +
                 "with no connector between them, so a car that happens to be going the " +
                 "wrong way round can never reach the station's kerb and drives laps for " +
                 "ever. The side street's two T-junctions are what lets a driver cross " +
                 "from one cycle to the other.")]
        public float sideStreetX = -60f;
        [Tooltip("The bare ground either side of it.")]
        public Color groundColour = new Color(0.28f, 0.30f, 0.24f);
        [Tooltip("The forecourt's worn asphalt.")]
        public Color apronColour = new Color(0.19f, 0.19f, 0.20f);
        [Tooltip("Lay the road demo's own dressing - lamps, bins, the kerb furniture.")]
        public bool kitDressing = true;

        [Header("The traffic")]
        [Tooltip("Cars driving the road that want nothing - something for a customer to " +
                 "wait for before he turns in.")]
        [Range(0, 30)] public int trafficCars = 8;
        [Tooltip("Cars that want petrol. Each one cycles: drives, books a bay, turns in, " +
                 "is filled, pays, and drives on.")]
        [Range(0, 12)] public int fuelCars = 6;
        [Tooltip("Seconds of driving before the FIRST of them wants petrol - staggered so " +
                 "they do not all arrive together in the first ten seconds.")]
        public Vector2 firstTank = new Vector2(4f, 70f);

        [Header("The people")]
        [Tooltip("Passers-by on the ROAD's pavements. They never set foot on the station: " +
                 "the forecourt's walk is a graph of its own.")]
        [Range(0, 80)] public int pedestrians = 12;
        [Tooltip("People on the station itself who are not filling a car - the couple at " +
                 "the shop. Two is a forecourt; twenty is a bus stop.")]
        [Range(0, 6)] public int stationWalkers = 2;
        [Tooltip("How likely a passer-by is to turn into the shop when he goes past its door.")]
        [Range(0f, 1f)] public float enterChance = 0.5f;
        [Tooltip("Seconds a customer stays inside the shop.")]
        public Vector2 insideSeconds = new Vector2(9f, 26f);
        public int nameSeed = 1987;

        [Header("The camera")]
        public float cameraYaw = 16f;
        public float cameraPitch = 26f;
        public float cameraDistance = 52f;
        [Tooltip("What the camera opens looking at, in metres north of the road's centre line.")]
        public float cameraPivotZ = StationZ - 4f;

        [Header("Watch")]
        [Tooltip("Print what every customer is doing every few seconds, and shout when one " +
                 "gets stuck. This is what a headless run is read against.")]
        public bool audit = true;
        public float auditEvery = 15f;

        // ------------------------------------------------------------ the ground plan
        //
        // The road runs along X through z = 0 with the road demo's own cross-section:
        // 7.5 m of asphalt each side of the crown, then 6.5 m of pavement. The station
        // stands on the north side, its canopy ten metres back from the pavement's
        // outer edge, turned so the cluster's front (+Z) faces the road.

        const float Cell = StreetKit.Cell;
        const float Half = StreetKit.StreetHalf;      // 7.5 m of asphalt each side
        const float Walk = StreetKit.SidewalkWidth;   // 6.5 m of pavement
        const float Outer = StreetKit.OuterHalf;      // 14 m: the pavement's outer edge
        const float RoadY = -0.08f;                   // the asphalt, sunk a tenth
        const float WalkY = 0f;                       // the pavement top, where people stand

        /// <summary>The forecourt's own surface. It is at ROAD level, not pavement
        /// level: a filling station's frontage is a dropped kerb the whole way across,
        /// which is what lets a car turn in at all. Everything standing on it - the
        /// canopy, the island, the store, and the man who got out - stands at this
        /// height, and the step up to the footway is the pavement's own kerb.</summary>
        const float ApronY = -0.1f;

        /// <summary>The bare ground, under everything laid on it.</summary>
        const float GroundY = -0.13f;

        /// <summary>How much of the road's frontage the station takes. Exactly the
        /// apron's own width: a frontage even a metre wider than the asphalt leaves a
        /// strip with no pavement and no forecourt on it, which is a metre of bare grass
        /// between the two - the seam the first build showed at both ends.</summary>
        const float FrontageHalf = FuelStation.ApronHalfX;

        /// <summary>The canopy's centre, ten metres back off the pavement.</summary>
        const float StationZ = Outer + FuelStation.SetBack;

        /// <summary>The two edges the forecourt is cut to, in the STATION'S frame: the
        /// pavement's outer edge, where the apron ends, and the carriageway's, where the
        /// crossovers end. The station faces south (its rotation is a half turn), so its
        /// local z counts back from the road as the world's z counts toward it.</summary>
        const float FrontZ = StationZ - Outer, KerbZ = StationZ - Half;

        /// <summary>How far off the road a customer leaves the lane and comes back to it.</summary>
        const float KerbX = 24f;

        /// <summary>Half the road's length: the two corners are at +-this, on the main
        /// road's centre line.</summary>
        float HalfRun => Mathf.Round(roadLength * 0.5f / Cell) * Cell;

        LaneNet _net;
        Carriageway _main;
        FuelStation _station;
        StreetKit _kit;
        DemoCamera _camera;
        CityLife _life;
        readonly List<FuelCustomer> _customers = new List<FuelCustomer>();
        readonly List<DemoVehicle> _asVehicles = new List<DemoVehicle>();
        readonly List<CivilianAgent> _walkers = new List<CivilianAgent>();
        readonly List<PedLink> _pedLinks = new List<PedLink>();
        readonly List<PedLink> _forecourtLinks = new List<PedLink>();
        List<GameObject> _people;
        float _chatScan, _auditAt;

        void Awake()
        {
#if UNITY_EDITOR
            BuildGround();
            BuildRoad();

            _net = BuildRoadNet();
            LaneNet.Active = _net;

            var stationRoot = new GameObject("Filling Station").transform;
            _station = FuelStation.Stand(stationRoot, new Vector3(0f, 0f, StationZ),
                                         Quaternion.Euler(0f, 180f, 0f), ApronY,
                                         ForecourtSet.CrossZ(FrontZ, KerbZ));
            // the asphalt is laid AFTER the station, because it is laid to fit it, and
            // the paint after the asphalt
            ForecourtSet.LayApron(_station, stationRoot, Flat("Pump Demo Apron", apronColour, 0.14f),
                                  FrontZ, KerbZ);
            ForecourtSet.Paint(_station, stationRoot,
                               Flat("Pump Demo Paint", new Color(0.84f, 0.84f, 0.80f), 0.05f),
                               Flat("Pump Demo Paint Blue", new Color(0.15f, 0.34f, 0.64f), 0.05f));
            // the dressing and the tree line belong to the station and not to this
            // scene: the city's wayside forecourt wants the same hedge on the same
            // frontage, and a bench that grew its own would be a bench that proves
            // nothing about the city
            _station.Dress(stationRoot, new System.Random(nameSeed + 101));
            BuildParkedCars();
            WireTheRoad();

            // the set is the road corridor and the forecourt; the bare ground round them
            // is backdrop, and nobody strolls out onto it (the fence every scene lays)
            WalkObstacles.City.Add(Rect.MinMaxRect(-HalfRun, -Outer, HalfRun,
                                                   StationZ + FuelStation.ApronBack));

            var clips = CrewKit.Clips();
            if (clips.Walk == null || clips.Idle == null)
                Debug.LogWarning("[PumpDemo] Walk/idle clips missing under Assets/Animations/People - " +
                                 "the people will slide.");

            BuildPavements();
            BuildTraffic(clips);
            BuildCustomers(clips);
            BuildLight();
            BuildCamera();
            gameObject.AddComponent<CrewDemo.CrewDemoPace>();
            Debug.Log($"[PumpDemo] {_customers.Count} customers, {_walkers.Count} on foot, " +
                      $"{_station.Bays.Length} bays.");
#else
            Debug.LogError("[PumpDemo] This demo loads Synty prefabs through the AssetDatabase " +
                           "and only runs in the editor.");
#endif
        }

        void Update()
        {
            float dt = Time.deltaTime;
            for (int i = 0; i < _customers.Count; i++) _customers[i].TickErrand(dt);
            TickPavementLife(dt);
            TickAudit(dt);
        }

        void OnDestroy()
        {
            for (int i = 0; i < _walkers.Count; i++) _walkers[i].Dispose();
            for (int i = 0; i < _customers.Count; i++)
            {
                _customers[i].Despawn();
                StreetTraffic.Users.Remove(_customers[i]);
                _customers[i].Driver?.Dispose();
            }
        }

        // ------------------------------------------------------------------- the set

        /// <summary>The bare ground either side of the road. It is laid a couple of
        /// centimetres BELOW the road and the forecourt, because it runs under both -
        /// a ground plane at the same height as the apron simply hid it.</summary>
        void BuildGround()
        {
            var mat = Flat("Pump Demo Ground", groundColour, 0.05f);
            float halfX = HalfRun + 60f;
            // ONE slab under the lot of it. Cut into four round the road corridor it
            // left a hairline of nothing at every seam, and the demo's first shots had
            // sky showing through the road where two lengths of it met.
            Slab(-halfX, backRoadZ - 60f, halfX, StationZ + 90f, mat, GroundY);
        }

        void BuildRoad()
        {
#if UNITY_EDITOR
            var root = new GameObject("Roadscape").transform;
            _kit = new StreetKit(root, y: -0.1f);
            if (!_kit.Load()) return;

            float run = HalfRun;
            float x0 = -run + Half + Cell, x1 = run - Half - Cell;    // carriageway, zebra to zebra
            float w0 = -run + Half + Walk, w1 = run - Half - Walk;    // pavements, corner slab to corner slab

            // Either side of the station: the road demo's own street, both pavements,
            // palms and bins and lamps on them. ACROSS THE STATION'S FRONTAGE: no north
            // pavement and nothing standing - the forecourt's crossover runs from the
            // kerb line to the pumps, and a palm planted in the middle of it is a palm a
            // car turning in drives through.
            float sx = sideStreetX;
            _kit.LayAlongX(0f, x0, sx - Half - Cell, w0, sx - Half - Walk, true, true, kitDressing);
            _kit.LayAlongX(0f, sx + Half + Cell, -FrontageHalf, sx + Half + Walk, -FrontageHalf,
                           true, true, kitDressing);
            _kit.LayAlongX(0f, FrontageHalf, x1, FrontageHalf, w1, true, true, kitDressing);

            // THE FRONTAGE. The footway carries on across it and is broken only at the
            // two crossovers, which is what a filling station's frontage looks like and
            // what stops a car turning in over twenty metres of pavement. Nothing is
            // dressed along here: a palm or a bin in the middle of a mouth is a palm a
            // car drives through.
            float mIn = FuelStation.MouthX - FuelStation.MouthHalf;    // 4 m: the near edge of a mouth
            float mOut = FuelStation.MouthX + FuelStation.MouthHalf;   // 14 m: the far one
            foreach (var (from, to, walk) in new[]
            {
                (-FrontageHalf, -mOut, true),   // pavement out to the west mouth
                (-mOut, -mIn, false),           // the west crossover
                (-mIn, mIn, true),              // the island of pavement between them
                (mIn, mOut, false),             // the east crossover
                (mOut, FrontageHalf, true),     // pavement on to the east end
            })
                _kit.LayAlongX(0f, from, to, from, to, true, walk, false);

            // the circuit's other sides: bare carriageway, no pavements, nothing standing
            // on them. Nobody is meant to look at them - they are how a car that has
            // driven past the station comes back to it, and the side street is how it
            // gets onto the lane the station is on at all.
            _kit.LayAlongX(backRoadZ, x0, sx - Half - Cell, false, false, false);
            _kit.LayAlongX(backRoadZ, sx + Half + Cell, x1, false, false, false);
            _kit.LayAlongZ(-run, backRoadZ + Half + Cell, -Half - Cell, false, false, false);
            _kit.LayAlongZ(run, backRoadZ + Half + Cell, -Half - Cell, false, false, false);
            _kit.LayAlongZ(sx, backRoadZ + Half + Cell, -Half - Cell, false, false, false);

            // the four corners, each a bend rather than a crossroads, and the side
            // street's two T's
            _kit.LayCrossroads(-run, 0f, north: false, south: true, east: true, west: false);
            _kit.LayCrossroads(run, 0f, north: false, south: true, east: false, west: true);
            _kit.LayCrossroads(-run, backRoadZ, north: true, south: false, east: true, west: false);
            _kit.LayCrossroads(run, backRoadZ, north: true, south: false, east: false, west: true);
            _kit.LayCrossroads(sx, 0f, north: false, south: true, east: true, west: true);
            _kit.LayCrossroads(sx, backRoadZ, north: true, south: false, east: true, west: true);
#endif
        }

        static Material Flat(string name, Color colour, float smoothness)
            => ForecourtSet.Flat(name, colour, smoothness);

        static void Slab(float xFrom, float zFrom, float xTo, float zTo, Material mat, float y = 0f)
        {
            if (xTo <= xFrom || zTo <= zFrom) return;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = mat != null ? mat.name : "Floor";
            floor.transform.position = new Vector3((xFrom + xTo) * 0.5f, y, (zFrom + zTo) * 0.5f);
            floor.transform.localScale = new Vector3((xTo - xFrom) / 10f, 1f, (zTo - zFrom) / 10f);
            Destroy(floor.GetComponent<Collider>());
            if (mat) floor.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        // One road a car may drive from end to end, two lanes, a dead end at each end
        // where it turns round. Right-hand traffic, so the lane on the STATION'S side
        // is the westbound one - which is why a customer arrives from the east and
        // never crosses the oncoming lane to turn in.
        LaneNet BuildRoadNet()
        {
            var net = new LaneNet();
            float run = HalfRun;
            var offsets = new[] { 2.5f };
            const float limit = 12f;
            var nw = net.AddNode(-run, 0f, Half, Half, stopSetback: 1.5f);
            var ne = net.AddNode(run, 0f, Half, Half, stopSetback: 1.5f);
            var sw = net.AddNode(-run, backRoadZ, Half, Half, stopSetback: 1.5f);
            var se = net.AddNode(run, backRoadZ, Half, Half, stopSetback: 1.5f);

            float sx = sideStreetX;
            var nMid = net.AddNode(sx, 0f, Half, Half, stopSetback: 1.5f);
            var sMid = net.AddNode(sx, backRoadZ, Half, Half, stopSetback: 1.5f);

            net.AddRoad(new Vector3(nw.XMax, 0f, 0f), new Vector3(nMid.XMin, 0f, 0f),
                        Half, offsets, limit, nw, nMid, false);
            // the station's own stretch: both the kerb it turns off and the kerb it
            // comes back onto are on this one, so a customer never has to change road
            _main = net.AddRoad(new Vector3(nMid.XMax, 0f, 0f), new Vector3(ne.XMin, 0f, 0f),
                                Half, offsets, limit, nMid, ne, false);
            net.AddRoad(new Vector3(sw.XMax, 0f, backRoadZ), new Vector3(sMid.XMin, 0f, backRoadZ),
                        Half, offsets, limit, sw, sMid, false);
            net.AddRoad(new Vector3(sMid.XMax, 0f, backRoadZ), new Vector3(se.XMin, 0f, backRoadZ),
                        Half, offsets, limit, sMid, se, false);
            net.AddRoad(new Vector3(-run, 0f, sw.ZMax), new Vector3(-run, 0f, nw.ZMin),
                        Half, offsets, limit, sw, nw, true);
            net.AddRoad(new Vector3(run, 0f, se.ZMax), new Vector3(run, 0f, ne.ZMin),
                        Half, offsets, limit, se, ne, true);
            net.AddRoad(new Vector3(sx, 0f, sMid.ZMax), new Vector3(sx, 0f, nMid.ZMin),
                        Half, offsets, limit, sMid, nMid, true);
            net.Finish();
            return net;
        }

        /// <summary>Which lane the station stands on, and where along it a customer
        /// leaves the road and comes back to it.</summary>
        void WireTheRoad()
        {
            RoadEdge westbound = null;
            if (_main != null)
                foreach (var lane in _main.Lanes)
                    if (Vector3.Dot(lane.Dir, Vector3.right) < -0.5f) westbound = lane;
            if (westbound == null)
            {
                Debug.LogError("[PumpDemo] No westbound lane: the station has no road.");
                return;
            }
            _station.Lane = westbound;
            _station.KerbInS = Along(westbound, KerbX);
            _station.KerbOutS = Along(westbound, -KerbX);
            _station.KerbIn = westbound.Start + westbound.Dir * _station.KerbInS;
            _station.KerbOut = westbound.Start + westbound.Dir * _station.KerbOutS;
            _station.MapRoads(_net);
        }

        /// <summary>Progress along a lane of the point above this x, kept off both ends.</summary>
        static float Along(RoadEdge lane, float x)
        {
            var point = new Vector3(x, 0f, lane.Start.z);
            return Mathf.Clamp(Vector3.Dot(point - lane.Start, lane.Dir), 6f, lane.Length - 6f);
        }

        // ------------------------------------------------------------------- the cars

        void BuildTraffic(PedClips clips)
        {
            if (trafficCars <= 0 || _net == null) return;
            var bodies = CarBodies();
            if (bodies.Count == 0)
            {
                Debug.LogWarning("[PumpDemo] No pack cars found for the traffic.");
                return;
            }
            var traffic = gameObject.AddComponent<StreetTraffic>();
            traffic.Init(bodies, _net, RoadY, trafficCars, _people, clips.SitLoop);
        }

        void BuildCustomers(PedClips clips)
        {
#if UNITY_EDITOR
            if (fuelCars <= 0 || _station == null || _station.Lane == null) return;
            var bodies = CarBodies();
            var people = PassersBy();
            if (bodies.Count == 0 || people.Count == 0) return;

            var root = new GameObject("Customers").transform;
            var crowd = CrewKit.ForCrowd(clips, new System.Random(nameSeed + 31));
            var lanes = new List<RoadEdge>();
            foreach (var road in _net.Roads)
                foreach (var lane in road.Lanes) lanes.Add(lane);

            for (int i = 0; i < fuelCars; i++)
            {
                var prefab = bodies[Random.Range(0, bodies.Count)];
                var go = Instantiate(prefab, root);
                go.name = prefab.name;
                LivingCity.Gameplay.VehiclePaint.Apply(go, prefab);
                foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>()) Destroy(mb);
                foreach (var rb in go.GetComponentsInChildren<Rigidbody>()) Destroy(rb);
                foreach (var col in go.GetComponentsInChildren<Collider>()) Destroy(col);

                var body = new CarBody(go.transform);
                var car = new FuelCustomer
                {
                    Tf = go.transform, Body = body, HalfLen = body.TrafficHalfLength, HalfWide = body.TrafficHalfWidth,
                    AxleBack = body.AxleBack, RoadY = RoadY, Net = _net, Tag = "traffic",
                    Plate = $"customer {i + 1}",
                };

                // the same man twice: sat at the wheel while the car drives, on his feet
                // the moment it stops at the pump. A driver who got out looking like
                // somebody else would be the one thing on the forecourt nobody believes.
                var face = people[Random.Range(0, people.Count)];
                car.Seated = CarOccupant.Seat(go.transform, face, clips.SitLoop, body.SeatLocalPoint(0));
                car.Driver = MakeDriver(face, crowd, root);

                // spread down the road, and NEVER on top of somebody. The traffic was
                // laid first and it fills the same lanes on its own beat (StreetTraffic
                // lays at 8 m, 30 m, 52 m...), so a customer dropped at a figure of its
                // own landed on one about one time in three - two bodies in the same
                // metre, which nothing can separate from the inside, and the pair stood
                // there wedged for the first half minute of every run.
                var lane = lanes[i % lanes.Count];
                float progress = FreeSpot(lane, 19f + (i / (float)fuelCars) * (lane.Length - 40f));
                car.Spawn(lane, progress);
                car.SetStation(_station, Random.Range(firstTank.x, firstTank.y));
                StreetTraffic.Users.Add(car);
                _customers.Add(car);
                _asVehicles.Add(car);
            }
#endif
        }

        /// <summary>The still bodies: the tanker at the back and a car or two in the
        /// parking row. Which bodies is the scene's business; where they stand is the
        /// station's (ForecourtSet.StandTheStill).</summary>
        void BuildParkedCars()
        {
#if UNITY_EDITOR
            var root = new GameObject("Standing").transform;
            var lorry = FindVehicle("SM_Veh_Truck_Delivery_01") ?? FindVehicle("SM_Veh_Truck_01");
            ForecourtSet.StandTheStill(_station, root, lorry, CarBodies(), new System.Random(nameSeed + 13));
#endif
        }

        /// <summary>The nearest progress along this lane, at or after the one wanted,
        /// with nothing standing within a body and a half of it.</summary>
        static float FreeSpot(RoadEdge lane, float want)
        {
            float lo = 8f, hi = Mathf.Max(lo, lane.Length - 12f);
            for (int step = 0; step < 24; step++)
            {
                float at = Mathf.Clamp(want + step * 13f, lo, hi);
                var point = lane.Start + lane.Dir * at;
                bool clear = true;
                var users = StreetTraffic.Users;
                for (int i = 0; i < users.Count && clear; i++)
                {
                    var d = users[i].RoadPosition - point;
                    d.y = 0f;
                    if (d.sqrMagnitude < 121f) clear = false;
                }
                if (clear) return at;
                if (at >= hi) break;
            }
            return Mathf.Clamp(want, lo, hi);
        }

        FuelDriver MakeDriver(GameObject prefab, PedClips clips, Transform root)
        {
            var go = Instantiate(prefab, root);
            go.name = prefab.name + " (driver)";
            foreach (var col in go.GetComponentsInChildren<Collider>()) Destroy(col);
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>()) Destroy(rb);
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>()) Destroy(mb);
            foreach (var animator in go.GetComponentsInChildren<Animator>())
                animator.runtimeAnimatorController = null;
            var driver = new FuelDriver { Speed = Random.Range(1.25f, 1.55f), Tag = "driver" };
            driver.InitAt(go.transform, clips, new Vector3(0f, ApronY, StationZ), Quaternion.identity);
            driver.Show(false);
            return driver;
        }

        // The pack cars anybody may be driving, weighted the way the city's pool is
        // (VehicleCatalog.PoolWeight): a saloon is common, a muscle car is not.
        List<GameObject> _cars;

        List<GameObject> CarBodies() => _cars ??= TestBench.WeightedCars(FindVehicle);

        static readonly string[] VehicleFolders =
        {
            "Assets/Synty/PolygonCity/Prefabs/Vehicles/",
            "Assets/Synty/PolygonPalmCity/Prefabs/Vehicles/",
            "Assets/Synty/PolygonTown/Prefabs/Vehicles/",
        };

        static GameObject FindVehicle(string name)
        {
#if UNITY_EDITOR
            foreach (var folder in VehicleFolders)
            {
                var path = folder + name + ".prefab";
                if (LivingCity.Gameplay.VehicleCatalog.IsMarkedService(path)) continue;
                var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab) return prefab;
            }
#endif
            return null;
        }

        // The bodies the road's nobodies wear - the people on the pavement and the
        // people at the wheel. The city's own rule: no police, no gang coats.
        List<GameObject> PassersBy()
        {
            if (_people != null) return _people;
            _people = new List<GameObject>();
#if UNITY_EDITOR
            foreach (var folder in new[]
            {
                "Assets/Synty/PolygonCity/Prefabs/Characters",
                "Assets/Synty/PolygonPalmCity/Prefabs/Characters",
            })
                foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
                {
                    var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                    var file = System.IO.Path.GetFileNameWithoutExtension(path);
                    var name = file.ToLowerInvariant();
                    if (name.Contains("police") || name.Contains("attach")) continue;
                    if (LivingCity.Gangs.GangLooks.IsGangBody(file)) continue;
                    if (LivingCity.Entities.CrowdLooks.IsBarred(file)) continue;
                    var go = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    var animator = go ? go.GetComponentInChildren<Animator>() : null;
                    if (animator == null || animator.avatar == null || !animator.avatar.isHuman) continue;
                    _people.Add(go);
                }
#endif
            return _people;
        }

        // ------------------------------------------------------------- the pavements
        //
        // Two walks down the road and a loop off the north one that goes up the side of
        // the forecourt, along the front of the shop and back - so the crowd on the
        // road has a reason to be on the station's ground, and passes the shop door
        // close enough to turn into it.

        void BuildPavements()
        {
#if UNITY_EDITOR
            float northZ = Outer - Walk * 0.5f;
            var xs = new List<float>();
            float walkEnd = HalfRun - Half - Walk - 4f;
            for (float x = -walkEnd; x <= walkEnd; x += 22f)
                if (Mathf.Abs(x - sideStreetX) > Half + Walk + 2f) xs.Add(x);
            xs.Sort();

            var north = new List<PedNode>();
            var south = new List<PedNode>();
            foreach (float x in xs)
            {
                north.Add(new PedNode { Pos = new Vector3(x, WalkY, northZ) });
                south.Add(new PedNode { Pos = new Vector3(x, WalkY, -northZ) });
            }
            for (int i = 1; i < north.Count; i++)
            {
                Join(north[i - 1], north[i], false);
                Join(south[i - 1], south[i], false);
            }
            // two crossings, well clear of the forecourt's mouths
            foreach (float at in new[] { -70f, 70f })
            {
                int i = Nearest(north, at);
                Join(north[i], south[i], true);
            }

            // THE STATION'S OWN WALK IS A GRAPH OF ITS OWN, joined to nothing. A
            // forecourt is not a thoroughfare: hook it onto the pavement and every
            // passer-by on three hundred metres of road wanders across it, and the
            // demo had two dozen people milling about between the pumps. Separate, it
            // carries exactly the couple of people the scene puts on it, and they are
            // the shop's customers.
            var eastEnd = new PedNode { Pos = new Vector3(13f, ApronY, StationZ + 4.7f) };
            var westEnd = new PedNode { Pos = new Vector3(-13f, ApronY, StationZ + 4.7f) };
            TestBench.Join(eastEnd, westEnd, false, _forecourtLinks);   // across the shop front, past its door

            // What is left of the ground to walk on. Read against EVERYTHING blocked and
            // not just the street kit's own plan (WalkObstacles.SampleWalk): the kit
            // knows where its lamps and bins are and knows nothing about the shop wall,
            // the hedge on the frontage, the gas cage or the cars standing in the
            // parking row - all of which the station blocked off. Sampled against the
            // kit alone, the two people on the forecourt walked through the shop.
            //
            // The FORECOURT'S walk especially: it was never sampled against anything at
            // all, so its crowd had the run of the whole station.
            foreach (var link in _pedLinks)
                WalkObstacles.SampleWalk(link, SidewalkDressing.WalkRadius);
            foreach (var link in _forecourtLinks)
                WalkObstacles.SampleWalk(link, SidewalkDressing.WalkRadius);

            _life = new CityLife
            {
                SitChance = 0f,
                EnterChance = enterChance,
                InsideSeconds = insideSeconds,
                CanSit = false,
                CanChat = true,
            };
            WireDoor(_station.ShopDoor, Vector3.back, null);
            _life.SortStops();

            SpawnCrowd(_pedLinks, pedestrians);
            SpawnCrowd(_forecourtLinks, stationWalkers);
#endif
        }

        static int Nearest(List<PedNode> nodes, float x)
        {
            int best = 0;
            float bestD = float.MaxValue;
            for (int i = 0; i < nodes.Count; i++)
            {
                float d = Mathf.Abs(nodes[i].Pos.x - x);
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        void Join(PedNode a, PedNode b, bool gated) => TestBench.Join(a, b, gated, _pedLinks);

        /// <summary>Hang a door off the stretch of walk that fronts it, both ways - the
        /// same wiring the city does (RoadDemoBuilder.BuildCityLife).</summary>
        void WireDoor(Vector3 pos, Vector3 outward, GameObject owner)
        {
            PedLink fwd = null;
            float t = 0f, best = 14f * 14f;
            foreach (var link in _forecourtLinks)
            {
                if (link.Gated || link.Length < 6f) continue;
                var dir = (link.To.Pos - link.From.Pos) / link.Length;
                float s = Mathf.Clamp(Vector3.Dot(pos - link.From.Pos, dir), 2f, link.Length - 2f);
                var q = link.From.Pos + dir * s;
                float dx = q.x - pos.x, dz = q.z - pos.z;
                float d = dx * dx + dz * dz;
                if (d < best) { best = d; fwd = link; t = s; }
            }
            if (fwd == null) { Debug.LogWarning("[PumpDemo] The shop door reaches no walk."); return; }
            PedLink back = null;
            foreach (var r in fwd.To.Links) if (r.To == fwd.From) back = r;
            if (back == null) return;

            var door = new DemoDoor
            {
                Pos = pos, Outward = outward, Building = owner,
                LinkFwd = fwd, LinkBack = back, EntryT = t,
                EntryPos = Vector3.Lerp(fwd.From.Pos, fwd.To.Pos, t / fwd.Length),
            };
            _life.Doors.Add(door);
            _life.AddStop(fwd, t, door, null);
            _life.AddStop(back, fwd.Length - t, door, null);
        }

        Transform _crowdRoot;

        void SpawnCrowd(List<PedLink> graph, int howMany)
        {
#if UNITY_EDITOR
            if (howMany <= 0 || graph.Count == 0) return;
            var prefabs = PassersBy();
            if (prefabs.Count == 0) return;
            var clips = CrewKit.Clips();
            if (clips.Walk == null || clips.Idle == null) return;

            var variety = new System.Random(nameSeed + 7);
            var root = _crowdRoot != null ? _crowdRoot
                     : _crowdRoot = new GameObject("Passers-by").transform;
            var pavements = graph.FindAll(l => !l.Gated);
            if (pavements.Count == 0) return;
            for (int k = 0; k < howMany; k++)
            {
                var link = pavements[Random.Range(0, pavements.Count)];
                var prefab = prefabs[Random.Range(0, prefabs.Count)];
                var go = Instantiate(prefab, root);
                go.name = prefab.name;
                foreach (var col in go.GetComponentsInChildren<Collider>()) Destroy(col);
                foreach (var rb in go.GetComponentsInChildren<Rigidbody>()) Destroy(rb);
                foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>()) Destroy(mb);
                foreach (var animator in go.GetComponentsInChildren<Animator>())
                    animator.runtimeAnimatorController = null;
                var agent = new CivilianAgent { Speed = Random.Range(1.2f, 1.8f) };
                if (!agent.Init(go.transform, CrewKit.ForCrowd(clips, variety), link,
                           Random.value * link.Length * 0.9f))
                { Destroy(go); continue; }
                agent.Setup(_life);
                _walkers.Add(agent);
            }
#endif
        }

        void TickPavementLife(float dt) => TestBench.TickPavementLife(_walkers, null, dt, ref _chatScan);

        // ------------------------------------------------------------------- the watch

        void TickAudit(float dt)
        {
            if (!audit || _station == null) return;
            _auditAt -= dt;
            if (_auditAt > 0f) return;
            _auditAt = auditEvery;

            int busy = 0;
            for (int i = 0; i < _station.Bays.Length; i++) if (_station.Taken(i)) busy++;
            var line = new System.Text.StringBuilder();
            line.Append($"[PumpDemo] {busy}/{_station.Bays.Length} bays taken");
            for (int i = 0; i < _customers.Count; i++)
                line.Append($" | {_customers[i].Plate}: {_customers[i].Doing}");
            int inside = 0;
            for (int i = 0; i < _walkers.Count; i++)
                if (_walkers[i].State == CivilianAgent.Mode.Inside) inside++;
            line.Append($" | {inside} of {_walkers.Count} in the shop");
            Debug.Log(line.ToString());
        }

        // ------------------------------------------------------------------- the sky

        void BuildLight()
        {
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.2f;
            sun.color = new Color(1f, 0.96f, 0.88f);
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(48f, 155f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.44f, 0.46f, 0.5f);
        }

        const string Hint =
            "WASD/arrows: move   Q/E or right-drag: rotate   wheel: zoom   Space: hold";

        void BuildCamera()
        {
            var camGo = new GameObject("Demo Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 45f;
            cam.farClipPlane = 900f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.57f, 0.68f, 0.79f);
            cam.GetUniversalAdditionalCameraData().antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            camGo.AddComponent<AudioListener>();

            _camera = camGo.AddComponent<DemoCamera>();
            gameObject.AddComponent<DemoAudio>().Init(null, _camera, _asVehicles, null, _walkers);
            // looking down onto the forecourt from over the road
            _camera.pivot = new Vector3(0f, 0f, cameraPivotZ);
            _camera.distance = cameraDistance;
            _camera.yaw = cameraYaw;
            _camera.pitch = cameraPitch;
            _camera.showHint = true;
            _camera.hint = Hint;
        }
    }
}
