using System.Collections.Generic;
using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// The industrial estate as a quarter: parcels dealt by <see cref="IndustrialLayout"/>,
    /// composed by <see cref="IndustrialBlocks"/>, the roads <see cref="CoreRoads"/> reads
    /// between them, and a lane graph over the lot.
    ///
    /// Nothing here drives. The lane graph is the city's own - <see cref="LaneNet"/> nodes,
    /// carriageways and lanes, laid the way the grid's are - and the traffic is the city's
    /// <see cref="DemoVehicle"/>. This class only says WHERE the roads are.
    ///
    /// It is <see cref="CoreDistrict"/>'s twin and deliberately so: the two read the same
    /// raster, so the graph is built by the same code (<see cref="RasterGraph"/>) and a
    /// lesson learned in one quarter is not learned twice. What differs is above the raster,
    /// not below it - the deal, the parcels and what stands on them.
    ///
    /// The traffic is not the core's, though. An estate carries LORRIES, and a road with
    /// nothing but saloons on it reads as a suburb with big sheds.
    /// </summary>
    public sealed class IndustrialDistrict : IDistrict
    {
        /// <summary>
        /// Lorries and vans in the quarter's traffic, or NOUGHT to let the quarter work it
        /// out from how much road it was dealt.
        ///
        /// A fixed number is wrong here in a way it is not wrong for the core, because the
        /// core is always about the same size and this is not: the deal gives anything from
        /// 300 to 700 m across. Eighteen was measured against a quarter of 505 x 485 m and
        /// came through five runs with no refusals; the same eighteen on a 315 x 400 m deal
        /// gave FORTY-EIGHT, which is not a fault in the roads but too much traffic on too
        /// little of them.
        /// </summary>
        public int carCount = 0;

        /// <summary>
        /// Square metres of road to a vehicle, when the count is worked out rather than
        /// given.
        ///
        /// It was 3,400, read off the one run that passed - and one run is not a reading.
        /// Over five runs of the 315 x 400 m deal that density came out four clean and one
        /// with 605 refusals of the road-space band, climbing from nothing at 95 s to 605 by
        /// 180 s. Nobody was deadlocked (the worst car stood 23 s) so it is congestion
        /// building rather than a lock, which is exactly what too many vehicles on too few
        /// junctions looks like as a run goes on.
        ///
        /// 4,200 is the same reading with the margin the first number should have had: it
        /// takes the small deal from twelve vehicles to ten, and leaves the big ones where
        /// they were, because those are capped at twenty-four anyway.
        /// </summary>
        const float RoadPerVehicle = 4200f;
        public float streetSpeed = 9f;
        public float arterySpeed = 13f;

        /// <summary>How much of the traffic is a lorry rather than a van or a car. Half:
        /// enough that every view down the artery has one in it, not so much that the
        /// junctions are nothing but lorries waiting for each other.</summary>
        public float lorryShare = 0.5f;

        public string Name => "Industry";
        public DistrictFrame Frame { get; set; } = DistrictFrame.Identity;
        public Rect LocalBounds => _bounds;
        public IReadOnlyList<DistrictPortal> Portals => _portals;

        public LaneNet Net { get; private set; }
        public CoreRoads.Raster Raster => _raster;
        public IndustrialLayout.Plan Layout => _plan;

        readonly List<DemoVehicle> _vehicles = new List<DemoVehicle>();
        readonly List<RoadEdge> _edges = new List<RoadEdge>();
        readonly List<DistrictPortal> _portals = new List<DistrictPortal>();

        IndustrialLayout.Plan _plan;
        CoreRoads.Raster _raster;
        List<IndustrialQuarter.Stood> _stood;
        Rect _bounds;
        Transform _yard;
        int _seed = 1987;

        // ------------------------------------------------------------------------ plan

        /// <summary>
        /// Deals the quarter and judges the drawing. Nothing is stood here: unlike the core,
        /// whose blocks are prefabs that have to BE somewhere before they can be measured,
        /// every parcel of this quarter is composed to a size the deal already knows.
        /// </summary>
        public void Plan(float[] links, int seed)
        {
            _seed = seed;
            _plan = IndustrialLayout.Arrange(seed, out _raster);
            _bounds = IndustrialLayout.Bounds(_raster);
        }

        public void Reserve(DistrictReservations into)
        {
            var world = Frame.ToWorldRect(_bounds);
            into.Pave(world);
            into.Level(Rect.MinMaxRect(world.xMin - 20f, world.yMin - 20f,
                                       world.xMax + 20f, world.yMax + 20f), RoadDemoBuilder.RoadBed);
            into.NoFlora(world);
        }

        // ----------------------------------------------------------------------- build

        public void Build(IDistrictHost host)
        {
            var quarter = new GameObject("Industrial Quarter").transform;
            quarter.SetParent(host.StaticRoot("Industry"), false);
            _yard = quarter;

            _stood = IndustrialQuarter.Stand(_plan, _raster, quarter,
                                             (prefab, under) => Object.Instantiate(prefab, under));

            // everything above was laid in the quarter's own coordinates; the frame is where
            // the city put it, and the lane graph below is built in world ones
            quarter.SetPositionAndRotation(Frame.origin, Frame.Rotation);

            Net = RasterGraph.Build(_raster, Frame, streetSpeed, arterySpeed, streetSpeed * 0.6f);
            _edges.Clear();
            _edges.AddRange(Net.Edges);
            SpawnLorries(host.LiveRoot("Industry Traffic"));

            host.RegisterRoads(_edges);
            for (int i = 0; i < _vehicles.Count; i++) host.RegisterVehicle(_vehicles[i]);
            BlockTheBuildings(host);

            Debug.Log($"[Industry] {_plan.Name}: {_plan.Islands.Count} islands, {_plan.Parcels.Count} parcels " +
                      $"({IndustrialQuarter.Cast(_plan)}), {_raster.Junctions.Count} junctions, " +
                      $"{_raster.Stretches.Count} stretches of road, {_edges.Count} lanes, " +
                      $"{_vehicles.Count} vehicles, {_raster.Faults} faults.{System.Environment.NewLine}" +
                      string.Join(System.Environment.NewLine, _plan.Rows) + System.Environment.NewLine +
                      IndustrialQuarter.Report(_stood) + System.Environment.NewLine + _raster.Report);
        }

        /// <summary>
        /// The quarter's traffic: lorries first, then vans and cars, spread over the lanes
        /// the way the city spreads its own.
        ///
        /// The bodies are the harbour's three - one Synty truck in its three guises, which
        /// is every lorry this project owns - and the rest come off the city's own pool with
        /// the marked ones barred, so no ambulance or squad car is dealt as estate traffic.
        /// </summary>
        void SpawnLorries(Transform parent)
        {
            int want = carCount > 0
                ? carCount
                : Mathf.Clamp(Mathf.RoundToInt(_raster.RoadArea / RoadPerVehicle), 6, 24);
            if (want <= 0 || _edges.Count == 0) return;
            var dice = new System.Random(_seed);
            int placed = 0;
            for (int round = 0; placed < want && round < 40; round++)
            {
                bool any = false;
                foreach (var edge in _edges)
                {
                    if (placed >= want) break;
                    float s = 8f + round * 24f;      // lorries want more room than cars do
                    if (s > edge.Length - 16f) continue;
                    any = true;

                    var prefab = dice.NextDouble() < lorryShare ? Lorry(dice) : CoreRoads.PickCar(dice);
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
                        HalfWide = Mathf.Clamp(box.extents.x, 0.7f, 1.5f),
                    };
                    car.Spawn(edge, s);
                    _vehicles.Add(car);
                    StreetTraffic.Users.Add(car);
                    placed++;
                }
                if (!any) break;
            }
        }

        /// <summary>Every lorry body this project has: the one Synty truck in its three
        /// guises, which is the harbour's own finding (HarborKit.Lorries).</summary>
        static readonly string[] Lorries =
        {
            "Assets/Synty/PolygonGangWarfare/Prefabs/Vehicles/SM_Veh_Truck_01.prefab",
            "Assets/Synty/PolygonTown/Prefabs/Vehicles/SM_Veh_Truck_Delivery_01.prefab",
            "Assets/Synty/PolygonTown/Prefabs/Vehicles/SM_Veh_Truck_01.prefab",
        };

        static GameObject Lorry(System.Random dice)
        {
            for (int k = 0; k < Lorries.Length; k++)
            {
                var prefab = DemoAssetLoad.Load<GameObject>(Lorries[dice.Next(Lorries.Length)]);
                if (prefab != null) return prefab;
            }
            return null;
        }

        /// <summary>Every building's box, so a man off the pavement walks round it and the
        /// map has something to put a card on.</summary>
        void BlockTheBuildings(IDistrictHost host)
        {
            if (_stood == null) return;
            foreach (var one in _stood)
            {
                foreach (Transform piece in one.Root)
                {
                    if (!piece.name.StartsWith("building-", System.StringComparison.OrdinalIgnoreCase) &&
                        !piece.name.StartsWith("SM_Bld_", System.StringComparison.OrdinalIgnoreCase)) continue;
                    if (piece.name.StartsWith("SM_Bld_Fence", System.StringComparison.OrdinalIgnoreCase)) continue;
                    var box = new Bounds();
                    bool any = false;
                    foreach (var renderer in piece.GetComponentsInChildren<Renderer>(true))
                    {
                        if (!any) { box = renderer.bounds; any = true; }
                        else box.Encapsulate(renderer.bounds);
                    }
                    // named for the BUILDING, with its parcel after it - which is what the
                    // port, the villages and the airfield all pass, and what a card naming a
                    // building wants. The parcel's name alone came from copying the core,
                    // and the core is the odd one out: it hands the same name to every
                    // building in a block, so a dozen sheds all answer to "haulage-01".
                    //
                    // Keeping the parcel in the string is not decoration. Whatever reads
                    // these classifies by name first, and "works" or "plant" in it is what
                    // says factory hall; "building-factory" on its own says nothing to that
                    // rule and falls through to a guess off the footprint.
                    if (any) host.Blocked(box, $"{piece.name} ({one.Parcel.Name})");
                }
            }
        }

        public void Tick(float dt) { }

        public void Dispose()
        {
            if (_yard != null && _yard.parent == null) Object.Destroy(_yard.gameObject);
        }
    }
}
