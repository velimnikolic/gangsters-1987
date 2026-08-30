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
    /// The pavement graph is read from the same raster by <see cref="RasterPedGraph"/>,
    /// so a host may now put the shared crowd, crews, police and combat on this structure.
    /// Traffic lights and the portals used to weld this quarter into a larger layout are
    /// still structural work for later (Docs/core-district-plan.md, 2.3).
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
        /// <summary>How many of the deal's left-over parcels remain public car parks.</summary>
        public int parkingLotCount = 3;
        /// <summary>Live ParkingDemo cars assigned to each retained car park.</summary>
        public int parkingCarsPerLot = 5;
        /// <summary>How many suitable left-over parcels become PumpDemo filling stations.</summary>
        public int fuelStationCount = 5;

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
        readonly List<PedLink> _walks = new List<PedLink>();
        readonly List<DistrictPortal> _portals = new List<DistrictPortal>();
        readonly ResidentialBlockModel _homes = new ResidentialBlockModel();
        readonly List<CoreAmenityLayout.Site> _parkingSites = new List<CoreAmenityLayout.Site>();
        readonly List<CoreAmenityLayout.Site> _fuelSites = new List<CoreAmenityLayout.Site>();
        readonly List<CoreAmenityLayout.Site> _developmentSites = new List<CoreAmenityLayout.Site>();
        readonly List<ParkingLot> _parkingLots = new List<ParkingLot>();

        CoreRoads.Raster _raster;
        Rect _bounds;
        Transform _yard;          // made only in Build; Plan owns data, never a hidden scene
        CityBlockRecycler _recycler;
        Material _fuelAsphalt;
        int _seed = 1987;
        const string EdgePavement =
            "Assets/Synty/PolygonCity/Prefabs/Environments/SM_Env_Sidewalk_01.prefab";

        /// <summary>The generated residential data, independent of whichever views are
        /// currently on camera. Future generator edits replace/invalidate recipes here.</summary>
        public ResidentialBlockModel ResidentialBlocks => _homes;
        /// <summary>Immutable quarters and named blocks produced by the accepted layout.</summary>
        public CoreTerritoryPlan Territory => _plan?.Territory;
        /// <summary>The seed used by every pure sub-plan, exposed for shared read-only adapters.</summary>
        public int LayoutSeed => _seed;
        /// <summary>Plan-owned public car parks, available to shared map/gameplay adapters
        /// without inspecting their composed scene objects.</summary>
        public IReadOnlyList<CoreAmenityLayout.Site> ParkingSites => _parkingSites;

        /// <summary>The shared parking lots' live cars. The city lighting stack uses
        /// these exact vehicles so a car gets headlamps while moving and only a rare,
        /// non-casting cabin/marker glow while its engine is off in a bay.</summary>
        public IEnumerable<ParkingCar> ParkingCars()
        {
            for (int i = 0; i < _parkingLots.Count; i++)
                for (int j = 0; j < _parkingLots[i].Cars.Count; j++)
                    yield return _parkingLots[i].Cars[j];
        }
        /// <summary>Plan-owned filling stations, kept stable even if their 3D views change.</summary>
        public IReadOnlyList<CoreAmenityLayout.Site> FuelSites => _fuelSites;
        /// <summary>Former parking parcels reassigned to generated housing. Exposed as
        /// read-only plan data so map adapters and regression tests can prove that none of
        /// the accepted ground was silently left without a programme.</summary>
        public IReadOnlyList<CoreAmenityLayout.Site> DevelopmentSites => _developmentSites;
        /// <summary>Fault count of the accepted road drawing before amenity/residential
        /// programming. Adding views on former parking ground must never worsen it.</summary>
        public int AcceptedRoadFaults { get; private set; }

        // ------------------------------------------------------------------ plan

        /// <summary>
        /// Reads the roads off baked block descriptions and deals every generated block as
        /// data. No scene object is made here: the host still needs these bounds before it
        /// can lay the island, camera and map.
        /// </summary>
        public void Plan(float[] links, int seed)
        {
            _seed = seed;
            _blocks.Clear();
            _blocks.AddRange(CoreBlockCatalog.CreateBlocks());
            _homes.Clear();
            // the seed deals the rows and the drawing is judged before it is taken; the
            // Synty seed asks for the demo's own arrangement instead
            _plan = CoreLayout.Arrange(_blocks, seed, out _raster);
            AcceptedRoadFaults = _raster != null ? _raster.Faults : 0;
            PlanAmenities();
            DevelopRemainders();
            PlanHomes();
            _bounds = Rect.MinMaxRect(_raster.X0, _raster.Z0,
                                      _raster.X(_raster.NX), _raster.Z(_raster.NZ));
        }

        /// <summary>
        /// CoreLayout keeps odd-sized remainder parcels as parking because the road raster
        /// needs accounted-for ground. That does not mean every one should be rendered as a
        /// car park. Keep a small, deterministic city-wide set and give the configured number
        /// of larger road-facing parcels to filling stations; every remaining block-sized
        /// parcel becomes housing.
        /// </summary>
        void PlanAmenities()
        {
            var candidates = new List<Rect>(_plan.Lots);
            foreach (var block in CoreLayout.WithGround(_blocks, _plan))
                if (block.Lot.width > 0.01f && block.Lot.height > 0.01f)
                    candidates.Add(block.Lot);

            CoreAmenityLayout.Select(
                _raster, candidates, _seed,
                Mathf.Max(0, parkingLotCount), Mathf.Max(0, fuelStationCount),
                _parkingSites, _fuelSites, _developmentSites);
        }

        /// <summary>
        /// Put every unclaimed block-sized remainder back into the structural plan before the
        /// road raster is final. The original deal already owns the city's proper parks; these
        /// former parking rectangles are residential infill. Thin bays belonging to an
        /// existing prefab become modular apartment frontages instead of anonymous asphalt.
        /// </summary>
        void DevelopRemainders()
        {
            if (_developmentSites.Count == 0) return;

            int residentialIndex = _plan.Residential.Count + 1;
            for (int i = 0; i < _developmentSites.Count; i++)
            {
                var site = _developmentSites[i];
                int w = Mathf.RoundToInt(site.Box.width / CoreLayout.Cell);
                int d = Mathf.RoundToInt(site.Box.height / CoreLayout.Cell);
                int innerW = w - 2 * ResidentialLot.Walk;
                int innerD = d - 2 * ResidentialLot.Walk;
                bool ordinary = ResidentialLot.Classify(innerW, innerD) != null;
                bool frontage = ResidentialLot.CanFrontage(w, d, (int)site.Entry);
                if (!ordinary && !frontage)
                    continue;

                var block = CoreLayout.Res(residentialIndex++, w, d, (int)site.Entry);
                block.Pivot = site.Box.min;
                block.QuarterId = QuarterFor(site.Box.center);
                block.Turn(0);
                _plan.Residential.Add(block);
            }

            // These rectangles were already classified as non-road parking ground by the
            // accepted raster. Keep that raster: rebuilding it with thin view-only blocks
            // closes nearby street mouths, merges crossings and removes the opaque backing
            // whenever a streamed residential view is out of range. Territory and maps need
            // the new logical blocks, but traffic must keep the accepted road drawing and
            // CoreRoads must keep laying plain hardstanding below the streamed buildings.
            var ground = CoreLayout.WithGround(_blocks, _plan);
            _plan.Territory = CoreTerritoryPlan.Build(_seed, ground);
        }

        CoreQuarterId QuarterFor(Vector2 local)
        {
            var territory = _plan.Territory;
            var direct = territory?.QuarterAt(local);
            if (direct.HasValue) return direct.Value;
            if (territory == null || territory.Quarters.Count == 0)
                return CoreQuarterId.Downtown;

            CoreQuarterId best = CoreQuarterId.Downtown;
            float nearest = float.MaxValue;
            for (int i = 0; i < territory.Quarters.Count; i++)
            {
                var quarter = territory.Quarters[i];
                float distance = (quarter.LocalAnchor - local).sqrMagnitude;
                if (distance >= nearest) continue;
                nearest = distance;
                best = quarter.Id;
            }
            return best;
        }

        /// <summary>
        /// The promenade, stretch by stretch - the river's ground the deal cut, composed to
        /// its plan (<see cref="QuayWalk.ForQuay"/>) the way the parks are: at the origin,
        /// then moved to the stretch's corner, under the Build-time yard.
        /// </summary>
        void StandQuays()
        {
            if (_plan == null || _plan.Quays.Count == 0) return;
            Composer.ForgetMissing();
            var wants = QuayWalk.Cast(_plan);
            int venue = 0;
            for (int q = 0; q < _plan.Quays.Count; q++)
            {
                var block = _plan.Quays[q];
                var root = new GameObject(block.Label).transform;
                root.SetParent(_yard, false);
                var box = block.Box;
                int dice = unchecked(_seed * 7919 + Mathf.RoundToInt(box.xMin) * 104729 + Mathf.RoundToInt(box.yMin) * 1299709);
                var walk = QuayWalk.ForQuay(_plan, block, wants[q], new System.Random(dice));
                var stood = QuayBlocks.Compose(walk, root, new System.Random(dice),
                    (prefab, parent) => Object.Instantiate(prefab, parent), venueOffset: venue);
                venue = stood.NextVenueOffset;
                QuayBlocks.Pave(walk, root, out _, (prefab, parent) => Object.Instantiate(prefab, parent), dice);
                CoreLayout.PlaceQuay(_plan, block, root);
                if (stood.Gaps > 0 || stood.RailGap > 0.5f || stood.OnWalk > 0)
                    Debug.LogWarning($"[Core] {block.Label}: {stood.Gaps} cell(s) with no floor, " +
                                     $"{stood.RailGap:F1} m of railing missing, {stood.OnWalk} thing(s) in the way.");
            }
        }

        /// <summary>
        /// The deal's residential blocks as recipes only. ResidentialLot is the adapter's
        /// data source; ResidentialBlocks.Compose is called later by a visible ViewHolder.
        /// </summary>
        void PlanHomes()
        {
            if (_plan == null || _plan.Residential.Count == 0) return;
            foreach (var block in _plan.Residential)
            {
                var box = block.Box;
                int actualW = Mathf.Max(1, Mathf.RoundToInt(box.width / CoreLayout.Cell));
                int actualD = Mathf.Max(1, Mathf.RoundToInt(box.height / CoreLayout.Cell));
                bool frontage = ResidentialLot.CanFrontage(actualW, actualD, block.Artery);
                int w = frontage ? actualW : Mathf.Max(3, actualW);
                int d = frontage ? actualD : Mathf.Max(3, actualD);
                int dice = unchecked(_seed * 7919 + Mathf.RoundToInt(box.xMin) * 104729 +
                                     Mathf.RoundToInt(box.yMin) * 1299709);

                // a yard block is one lot on its own plot; every other one is divided into
                // houses, gaps and yards
                var lot = CoreLayout.IsYard(block)
                    ? ResidentialLot.Yard(w, d, dice, block.Unit)
                    : frontage
                        ? ResidentialLot.Frontage(w, d, dice, block.Artery)
                        : ResidentialLot.Roll(w, d, dice, Mathf.Max(0, block.Artery));
                _homes.Add(new ResidentialBlockRecipe(
                    block.StableId, block.Label, box, lot, dice, block.BlockId, block.QuarterId));
                if (lot.Faults.Count > 0)
                    Debug.LogWarning($"[Core] {block.Label} ({w}x{d} cells, {lot.Klass}): " +
                                     string.Join("; ", lot.Faults));
            }
        }

        /// <summary>Compatibility path for a host that does not provide streamed views.</summary>
        void StandHomes()
        {
            Composer.ForgetMissing();
            foreach (var recipe in _homes.Blocks)
            {
                var root = new GameObject(recipe.Name).transform;
                root.SetParent(_yard, false);
                var stood = recipe.Compose(root);
                root.localPosition = new Vector3(recipe.LocalBounds.xMin, 0f, recipe.LocalBounds.yMin);
                if (stood.Missing > 0)
                    Debug.LogWarning($"[Core] {recipe.Name}: {stood.Missing} piece(s) missing");
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
                var root = new GameObject(block.Label).transform;
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
                    Debug.LogWarning($"[Core] {block.Label}: {stood.Gaps} cell(s) with no floor, " +
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
            // Core is not a solid rectangular slab. Keep island ground beneath its
            // Outside/Spare cells, but remove it wherever the accepted raster really lays
            // a road, pavement, lot or block. This prevents the sea plane from appearing as
            // a moat along the raster's rectangular outer bounds.
            into.Pave(PavesWholeGroundCell);

            // The river is an actual channel through the island, not only a water renderer
            // beneath flat land. Continue it far enough in both directions to meet the sea;
            // unlike a harbour basin it already has two prescribed open ends, so the island
            // must not push it sideways in OpenBasinsToSea.
            if (_plan != null && _plan.Water.width > 0.01f)
            {
                var river = Rect.MinMaxRect(
                    _plan.Water.xMin, _plan.River.Z0 - RiverBridge.Reach,
                    _plan.Water.xMax, _plan.River.Z1 + RiverBridge.Reach);
                into.Sea(Frame.ToWorldRect(river), false);
            }
            into.Level(Rect.MinMaxRect(world.xMin - 20f, world.yMin - 20f, world.xMax + 20f, world.yMax + 20f),
                       RoadDemoBuilder.RoadBed);
            into.NoFlora(world);
        }

        bool PavesWholeGroundCell(float worldX, float worldZ, float halfCell)
        {
            if (_raster == null)
                return false;

            float reach = Mathf.Max(0f, halfCell - 0.01f);
            var a = Frame.ToLocal(new Vector3(worldX - reach, 0f, worldZ - reach));
            var b = Frame.ToLocal(new Vector3(worldX + reach, 0f, worldZ - reach));
            var c = Frame.ToLocal(new Vector3(worldX - reach, 0f, worldZ + reach));
            var d = Frame.ToLocal(new Vector3(worldX + reach, 0f, worldZ + reach));
            float x0 = Mathf.Min(a.x, b.x, c.x, d.x);
            float x1 = Mathf.Max(a.x, b.x, c.x, d.x);
            float z0 = Mathf.Min(a.z, b.z, c.z, d.z);
            float z1 = Mathf.Max(a.z, b.z, c.z, d.z);
            int i0 = Mathf.FloorToInt((x0 - _raster.X0) / CoreRoads.Cell);
            int i1 = Mathf.FloorToInt((x1 - _raster.X0) / CoreRoads.Cell);
            int j0 = Mathf.FloorToInt((z0 - _raster.Z0) / CoreRoads.Cell);
            int j1 = Mathf.FloorToInt((z1 - _raster.Z0) / CoreRoads.Cell);

            for (int i = i0; i <= i1; i++)
                for (int j = j0; j <= j1; j++)
                {
                    var kind = _raster.At(i, j);
                    if (kind == CoreRoads.Kind.Outside && IsCityEdgePavement(i, j))
                        continue;
                    if (kind == CoreRoads.Kind.Outside || kind == CoreRoads.Kind.Water ||
                        kind == CoreRoads.Kind.Spare)
                        return false;
                }
            return true;
        }

        /// <summary>
        /// The city's final five metres are pavement: one exact band in the first Outside
        /// cell touching built ground. It follows the irregular raster edge and never uses
        /// Water as an anchor, so it cannot close the river or recreate the old rectangular
        /// moat. Kept public for the shared TurfMap survey and plan regression tests.
        /// </summary>
        public bool IsCityEdgePavement(int i, int j)
        {
            if (_raster == null || i < 0 || j < 0 || i >= _raster.NX || j >= _raster.NZ ||
                _raster.At(i, j) != CoreRoads.Kind.Outside)
                return false;

            static bool City(CoreRoads.Kind kind) =>
                kind != CoreRoads.Kind.Outside && kind != CoreRoads.Kind.Water &&
                kind != CoreRoads.Kind.Spare;

            return City(_raster.At(i - 1, j)) || City(_raster.At(i + 1, j)) ||
                   City(_raster.At(i, j - 1)) || City(_raster.At(i, j + 1));
        }

        // ----------------------------------------------------------------- build

        public void Build(IDistrictHost host)
        {
            if (host is ICityTerritoryHost territoryHost)
                territoryHost.Territories.Load(Territory, Frame);

            var quarter = new GameObject("Core Quarter").transform;
            quarter.SetParent(host.StaticRoot("Core"), false);
            _yard = new GameObject("Blocks").transform;
            _yard.SetParent(quarter, false);
            StandCoreBlocks(host);
            StandParks();
            StandQuays();

            if (host is IStreamedDistrictHost streamed)
            {
                // The recipe catalogue is the truth for both map and views. Register it
                // before any holder exists so a recycled/off-screen block never vanishes
                // from the survey plate.
                streamed.RegisterResidentialModel(_homes, Frame);
                var views = streamed.StreamRoot("Core Residential Views");
                views.SetPositionAndRotation(Frame.origin, Frame.Rotation);
                var fallbacks = views.gameObject.AddComponent<ResidentialFallbackLayer>();
                fallbacks.Init(_homes);
                _recycler = views.gameObject.AddComponent<CityBlockRecycler>();
                _recycler.Init(_homes, Frame, streamed.ViewConfig, fallbacks: fallbacks);
                streamed.RegisterBlockRecycler(_recycler);
            }
            else StandHomes();

            var roads = new GameObject("Roads").transform;
            roads.SetParent(quarter, false);
            // the road's tiles go down over the water too - the bridge's deck - but not over
            // the channels the leaves span. Parking is composed below by the same generator
            // ParkingDemo uses; the raster's old painted-row renderer is deliberately off.
            CoreRoads.Lay(_raster, (prefab, parent) => Object.Instantiate(prefab, parent), roads,
                          RiverBridge.Skip(_plan, _raster), layCarParks: false,
                          skipPlainParking: AmenitySurfaceAt);
            StandCityEdgePavement(roads);
            CorePowerlines.Stand(_plan, _raster, quarter, _seed);
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
            StandAmenities(quarter, host);
            BuildPavementGraph();
            InstallBascules(host, river);
            SpawnCars(host.LiveRoot("Core Traffic"));

            host.RegisterRoads(_edges);
            host.RegisterPavement(_walks);
            for (int i = 0; i < _vehicles.Count; i++) host.RegisterVehicle(_vehicles[i]);
            BlockTheBuildings(host);
            BlockTheResidential(host);

            Debug.Log($"[Core] {_plan.Name}: {_blocks.Count} blocks, {_raster.Junctions.Count} junctions, " +
                      $"{_raster.Stretches.Count} stretches of road, {_edges.Count} lanes, " +
                      $"{_vehicles.Count} traffic cars, {_parkingSites.Count} ParkingDemo lots, " +
                      $"{_fuelSites.Count} PumpDemo station(s), {_raster.Faults} faults.{System.Environment.NewLine}" +
                      string.Join(System.Environment.NewLine, _plan.Rows) + System.Environment.NewLine + _raster.Report);
        }

        /// <summary>Lays the narrow visible pavement band published by
        /// <see cref="IsCityEdgePavement"/>. Primary-structure terrain is held a few
        /// centimetres below this level, so the band remains opaque without removing a
        /// broad rectangular belt of island ground.</summary>
        void StandCityEdgePavement(Transform roads)
        {
            var edge = new GameObject("City Edge Pavement").transform;
            edge.SetParent(roads, false);
            Composer.Begin((prefab, parent) => Object.Instantiate(prefab, parent));
            for (int i = 0; i < _raster.NX; i++)
                for (int j = 0; j < _raster.NZ; j++)
                {
                    if (!IsCityEdgePavement(i, j)) continue;
                    Composer.Lay(EdgePavement, edge, _raster.X(i), _raster.Z(j),
                        CoreRoads.Cell, CoreRoads.Cell, 0f);
                }
        }

        /// <summary>The shared parking and filling-station composers bring their own surface.
        /// Every other raster parking cell receives plain asphalt in <see cref="CoreRoads.Lay"/>;
        /// skipping only these footprints prevents two coplanar grounds from flickering.</summary>
        bool AmenitySurfaceAt(int i, int j)
        {
            var centre = new Vector2(_raster.X(i) + CoreRoads.Cell * 0.5f,
                                     _raster.Z(j) + CoreRoads.Cell * 0.5f);
            if (CoreAmenityLayout.Contains(_parkingSites, centre)) return true;
            for (int k = 0; k < _fuelSites.Count; k++)
                if (CoreAmenityLayout.FuelSurface(_fuelSites[k]).Contains(centre)) return true;
            return false;
        }

        /// <summary>Stand the shared ParkingDemo and PumpDemo composers on the parcels the
        /// paper plan retained. This happens after the lane graph exists so parking cars can
        /// join the actual Core road beside their gate.</summary>
        void StandAmenities(Transform quarter, IDistrictHost host)
        {
            System.Func<GameObject, Transform, GameObject> stand =
                (prefab, parent) => Object.Instantiate(prefab, parent);

            Transform live = null;
            for (int i = 0; i < _parkingSites.Count; i++)
            {
                var planned = _parkingSites[i];
                var style = CoreAmenityLayout.ParkingStyle(planned, i);
                var site = ParkingBlockSite.Build(
                    planned.Box, planned.Entry, quarter, stand, style: style);
                site.Root.name = $"Core Parking {i + 1:00} - {style}";

                if (live == null) live = host.LiveRoot("Core Parking Traffic");
                // The gate is animated by ParkingLot and must not be folded into the host's
                // static geometry together with the surface and booth.
                if (site.GateRoot != null) site.GateRoot.SetParent(live, true);
                var lot = new ParkingLot(
                    site, Net, Mathf.Max(0, parkingCarsPerLot),
                    unchecked(_seed * 7919 + i * 104729), live);
                if (lot.CarCount > 0) _parkingLots.Add(lot);
            }

            if (_fuelSites.Count == 0) return;
            _fuelAsphalt = ForecourtSet.Asphalt();
            for (int i = 0; i < _fuelSites.Count; i++)
            {
                var planned = _fuelSites[i];
                CoreAmenityLayout.FuelPose(planned, out var localAnchor, out int localYaw);
                var root = new GameObject($"Core Filling Station {i + 1:00} (PumpDemo)").transform;
                root.SetParent(quarter, false);

                var anchor = Frame.ToWorld(localAnchor);
                var rotation = Frame.Rotation * Quaternion.Euler(0f, localYaw, 0f);
                // Core's odd parcels are smaller than the full wayside/PumpDemo programme.
                // Keep the recognisable authored station, but scale its visual cluster around
                // the canopy and give the exact retained rectangle its own opaque road surface.
                var visual = new GameObject("Compact PumpDemo Visuals").transform;
                visual.SetParent(root, false);
                visual.SetPositionAndRotation(anchor, rotation);
                float edge = CoreAmenityLayout.FuelSetBack;
                var station = FuelStation.Stand(
                    visual, anchor, rotation, Frame.origin.y, edge, blockWalkers: false);
                visual.localScale = Vector3.one * CoreAmenityLayout.FuelVisualScale;

                // The city water plane lies beneath every district. Pave the entire assigned
                // rectangle independently of the smaller prop cluster so there is no rear gap.
                float padBack = edge - CoreAmenityLayout.FuelParcelDepth(planned);
                float halfWidth = CoreAmenityLayout.FuelParcelFrontage(planned) * 0.5f;
                ForecourtSet.LayParcel(station, root, _fuelAsphalt, halfWidth, edge, padBack);
                if (TryRendererBounds(visual, out var visualBounds))
                    host.Blocked(visualBounds, $"Filling Station {i + 1}");
            }
        }

        static bool TryRendererBounds(Transform root, out Bounds bounds)
        {
            bounds = default;
            if (root == null) return false;
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return false;
            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return true;
        }

        void StandCoreBlocks(IDistrictHost host)
        {
            for (int i = 0; i < _blocks.Count; i++)
            {
                var block = _blocks[i];
                var prefab = DemoAssetLoad.Load<GameObject>(CoreLayout.BlocksDir + block.Name + ".prefab");
                if (prefab == null)
                {
                    host.ReportMissing(CoreLayout.BlocksDir + block.Name + ".prefab");
                    continue;
                }
                block.Go = Object.Instantiate(prefab, Vector3.zero, Quaternion.identity, _yard);
                block.Go.name = block.Label;
                CoreLayout.Place(block);
            }
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

        void BuildPavementGraph()
        {
            _walks.Clear();
            _walks.AddRange(RasterPedGraph.Build(_raster, Frame));
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
                    if (any) host.Blocked(box, block.Label);
                }
            }
        }

        /// <summary>Residential footprints come from the recipe, not from whichever views
        /// happen to be alive. The map and walkers therefore keep the whole city while the
        /// renderer owns only the camera's small window into it.</summary>
        void BlockTheResidential(IDistrictHost host)
        {
            foreach (var recipe in _homes.Blocks)
            {
                foreach (var spot in recipe.Plan.Spots)
                {
                    var unit = spot?.Unit;
                    if (unit == null || ResidentialUnits.IsLot(unit)) continue;
                    float cell = ResidentialLot.Cell;
                    var local = new Rect(
                        recipe.LocalBounds.xMin + spot.I * cell,
                        recipe.LocalBounds.yMin + spot.J * cell,
                        Mathf.Max(1, spot.CW) * cell,
                        Mathf.Max(1, spot.CD) * cell);
                    var world = Frame.ToWorldRect(local);
                    float height = Mathf.Max(2f, unit.MaxH);
                    var box = new Bounds(
                        new Vector3(world.center.x, Frame.origin.y + height * 0.5f, world.center.y),
                        new Vector3(world.width, height, world.height));
                    host.Blocked(box, $"{recipe.Name}: {unit.Name}");
                }
            }
        }

        public void Tick(float dt)
        {
            for (int i = 0; i < _parkingLots.Count; i++) _parkingLots[i].Tick(dt);
        }

        public void Dispose()
        {
            for (int i = 0; i < _parkingLots.Count; i++) _parkingLots[i].Dispose();
            _parkingLots.Clear();
            // the cars went on the street's list so the men on foot and the outfit's
            // drivers could see them; off it again, or the next quarter dodges ghosts
            foreach (var car in _vehicles) StreetTraffic.Users.Remove(car);
            _vehicles.Clear();
            _walks.Clear();
            _homes.Clear();
            _parkingSites.Clear();
            _fuelSites.Clear();
            _developmentSites.Clear();
            if (_fuelAsphalt != null) Object.Destroy(_fuelAsphalt);
            _fuelAsphalt = null;
            // Build-time geometry normally dies with its host. A detached compatibility
            // yard is still ours to clean explicitly.
            if (_yard != null && _yard.parent == null) Object.Destroy(_yard.gameObject);
        }
    }

    /// <summary>
    /// Pure paper-side choice of which generated remainder parcels become amenities. The
    /// layout is deliberately separate from the scene composition: the same seed and raster
    /// always choose the same bounded set, and tests can judge the cap without loading a
    /// prefab. CoreRoads still owns every cell for topology; this class only assigns its view.
    /// </summary>
    public static class CoreAmenityLayout
    {
        const int MinimumParkingBays = 6;
        // Core uses the recognisable PumpDemo composition at five-eighths scale. Twenty metres
        // is four road-grid cells and still leaves the canopy, shop and two pumps legible; the
        // full 30 m wayside programme was too large for most generated remnants.
        public const float FuelVisualScale = 0.625f;
        public const float FuelFrontage = 20f;
        public const float FuelDepth = 20f;
        public const float FuelSetBack = FuelStation.SetBack * FuelVisualScale;
        // A compact station may own a modest urban lot when cropping it would create a fake
        // unusable sliver. Anything wider is split only when the remainder is real housing.
        const float FuelWholeParcelMaxFrontage = 50f;
        /// <summary>The forecourt may have a short service strip behind it, but never the
        /// depth of a city block. Fifty metres admits one additional real Core parcel when a
        /// fifth station is requested; the whole assigned pad is paved below.</summary>
        const float FuelParcelMaxDepth = 50f;

        public sealed class Site
        {
            public readonly Rect Box;
            public readonly ParkingEntrySide Entry;
            public readonly int Cells;

            public Site(Rect box, ParkingEntrySide entry, int cells)
            {
                Box = box;
                Entry = entry;
                Cells = cells;
            }
        }

        /// <summary>Select fuel first so a parking cap cannot consume the only parcel deep
        /// enough for PumpDemo's store, canopy and back-of-house dressing.</summary>
        public static void Select(
            CoreRoads.Raster raster, IEnumerable<Rect> plannedLots, int seed,
            int parkingCount, int fuelCount,
            List<Site> parking, List<Site> fuel, List<Site> development = null)
        {
            parking.Clear();
            fuel.Clear();
            development?.Clear();
            if (raster == null || plannedLots == null) return;

            // Keep the original, whole-lot candidates for amenity selection. Their order
            // and scores determine the established five filling-station locations.
            var lots = new List<Rect>(plannedLots);
            var candidates = Candidates(raster, lots);
            // Some residential-yard remainders are described as an L made from two
            // rectangles. A cross street cuts through that L in the accepted raster, so
            // neither source rectangle is entirely Parking and the old all-or-nothing
            // candidate filter silently discarded both. Recover the actual rectangular
            // parking runs, but reserve them for housing: they are the large blank blocks
            // players reported, not additional amenity choices.
            var supplementalDevelopment = SupplementalDevelopment(raster, lots, candidates);
            var used = new HashSet<Site>();
            var fuelRemainders = development != null ? new List<Site>() : null;
            for (int i = 0; i < fuelCount; i++)
            {
                var next = Pick(candidates, used, fuel, parking, seed + i * 104729, wantsFuel: true);
                if (next == null) break;
                used.Add(next);
                var station = CropFuelParcel(next, seed + i * 104729, out var remainder);
                fuel.Add(station);
                if (remainder != null) fuelRemainders?.Add(remainder);
            }
            for (int i = 0; i < parkingCount; i++)
            {
                var next = Pick(candidates, used, fuel, parking, seed + i * 7919, wantsFuel: false);
                if (next == null) break;
                used.Add(next);
                parking.Add(next);
            }

            if (development != null)
            {
                for (int i = 0; i < candidates.Count; i++)
                    if (!used.Contains(candidates[i])) development.Add(candidates[i]);
                development.AddRange(fuelRemainders);
                development.AddRange(supplementalDevelopment);
            }
        }

        readonly struct Run : System.IEquatable<Run>
        {
            public readonly int I0;
            public readonly int I1;

            public Run(int i0, int i1) { I0 = i0; I1 = i1; }
            public bool Equals(Run other) => I0 == other.I0 && I1 == other.I1;
            public override bool Equals(object obj) => obj is Run other && Equals(other);
            public override int GetHashCode() => unchecked(I0 * 397 ^ I1);
        }

        /// <summary>
        /// Returns the real parking rectangles inside planned lots which the legacy
        /// whole-rectangle filter could not see. Horizontal runs with the same span are
        /// joined vertically, so a 75 x 40 m city block remains one residential parcel
        /// instead of eight five-metre strips.
        /// </summary>
        static List<Site> SupplementalDevelopment(
            CoreRoads.Raster raster, IReadOnlyList<Rect> plannedLots,
            IReadOnlyList<Site> wholeCandidates)
        {
            var parking = new bool[raster.NX, raster.NZ];
            for (int n = 0; n < plannedLots.Count; n++)
            {
                CellBounds(raster, plannedLots[n], out int i0, out int j0,
                           out int i1, out int j1);
                for (int i = i0; i < i1; i++)
                    for (int j = j0; j < j1; j++)
                        if (raster.At(i, j) == CoreRoads.Kind.Parking)
                            parking[i, j] = true;
            }

            // Whole candidates are already assigned below to fuel, retained parking or
            // ordinary development. Only recover ground not owned by one of them.
            for (int n = 0; n < wholeCandidates.Count; n++)
            {
                CellBounds(raster, wholeCandidates[n].Box, out int i0, out int j0,
                           out int i1, out int j1);
                for (int i = i0; i < i1; i++)
                    for (int j = j0; j < j1; j++)
                        parking[i, j] = false;
            }

            var found = new List<Site>();
            var active = new Dictionary<Run, int>();
            var row = new List<Run>();
            var close = new List<Run>();
            for (int j = 0; j <= raster.NZ; j++)
            {
                row.Clear();
                if (j < raster.NZ)
                {
                    int i = 0;
                    while (i < raster.NX)
                    {
                        while (i < raster.NX && !parking[i, j]) i++;
                        int i0 = i;
                        while (i < raster.NX && parking[i, j]) i++;
                        if (i0 < i) row.Add(new Run(i0, i));
                    }
                }

                close.Clear();
                foreach (var pair in active)
                    if (!row.Contains(pair.Key)) close.Add(pair.Key);
                close.Sort((a, b) => a.I0 != b.I0 ? a.I0.CompareTo(b.I0) : a.I1.CompareTo(b.I1));
                for (int n = 0; n < close.Count; n++)
                {
                    var run = close[n];
                    AddSupplement(raster, run.I0, active[run], run.I1, j, found);
                    active.Remove(run);
                }
                foreach (var run in row)
                    if (!active.ContainsKey(run)) active.Add(run, j);
            }
            return found;
        }

        static void AddSupplement(CoreRoads.Raster raster, int i0, int j0, int i1, int j1,
                                  List<Site> into)
        {
            if (i1 <= i0 || j1 <= j0) return;
            // RoadEntry still chooses the best side even when an unusual one-cell remnant
            // only reaches the street through its adjoining parcel. Residential frontage
            // has a deterministic direction in either case, and no planned asphalt is
            // silently abandoned again.
            RoadEntry(raster, i0, j0, i1, j1, out var entry);
            var box = Rect.MinMaxRect(raster.X(i0), raster.Z(j0),
                                      raster.X(i1), raster.Z(j1));
            into.Add(new Site(box, entry, (i1 - i0) * (j1 - j0)));
        }

        static void CellBounds(CoreRoads.Raster raster, Rect requested,
                               out int i0, out int j0, out int i1, out int j1)
        {
            i0 = Mathf.Clamp(Mathf.RoundToInt((requested.xMin - raster.X0) / CoreRoads.Cell),
                             0, raster.NX);
            i1 = Mathf.Clamp(Mathf.RoundToInt((requested.xMax - raster.X0) / CoreRoads.Cell),
                             0, raster.NX);
            j0 = Mathf.Clamp(Mathf.RoundToInt((requested.yMin - raster.Z0) / CoreRoads.Cell),
                             0, raster.NZ);
            j1 = Mathf.Clamp(Mathf.RoundToInt((requested.yMax - raster.Z0) / CoreRoads.Cell),
                             0, raster.NZ);
        }

        static List<Site> Candidates(CoreRoads.Raster raster, IEnumerable<Rect> plannedLots)
        {
            var found = new List<Site>();
            var seen = new HashSet<string>();
            foreach (var requested in plannedLots)
            {
                int i0 = Mathf.RoundToInt((requested.xMin - raster.X0) / CoreRoads.Cell);
                int i1 = Mathf.RoundToInt((requested.xMax - raster.X0) / CoreRoads.Cell);
                int j0 = Mathf.RoundToInt((requested.yMin - raster.Z0) / CoreRoads.Cell);
                int j1 = Mathf.RoundToInt((requested.yMax - raster.Z0) / CoreRoads.Cell);
                if (i0 < 0 || j0 < 0 || i1 > raster.NX || j1 > raster.NZ || i1 <= i0 || j1 <= j0)
                    continue;

                bool parking = true;
                for (int i = i0; i < i1 && parking; i++)
                    for (int j = j0; j < j1; j++)
                        if (raster.At(i, j) != CoreRoads.Kind.Parking) { parking = false; break; }
                if (!parking) continue;

                string key = $"{i0}:{j0}:{i1}:{j1}";
                if (!seen.Add(key)) continue;
                if (!RoadEntry(raster, i0, j0, i1, j1, out var entry)) continue;
                var box = Rect.MinMaxRect(raster.X(i0), raster.Z(j0),
                                          raster.X(i1), raster.Z(j1));
                found.Add(new Site(box, entry, (i1 - i0) * (j1 - j0)));
            }
            return found;
        }

        static bool RoadEntry(
            CoreRoads.Raster raster, int i0, int j0, int i1, int j1,
            out ParkingEntrySide entry)
        {
            int south = 0, east = 0, north = 0, west = 0;
            for (int i = i0; i < i1; i++)
            {
                if (ServedByRoad(raster.At(i, j0 - 1))) south++;
                if (ServedByRoad(raster.At(i, j1))) north++;
            }
            for (int j = j0; j < j1; j++)
            {
                if (ServedByRoad(raster.At(i1, j))) east++;
                if (ServedByRoad(raster.At(i0 - 1, j))) west++;
            }

            entry = ParkingEntrySide.South;
            int best = south;
            if (east > best) { best = east; entry = ParkingEntrySide.East; }
            if (north > best) { best = north; entry = ParkingEntrySide.North; }
            if (west > best) { best = west; entry = ParkingEntrySide.West; }
            return best > 0;
        }

        static bool ServedByRoad(CoreRoads.Kind kind)
        {
            switch (kind)
            {
                case CoreRoads.Kind.Bare:
                case CoreRoads.Kind.LaneEW:
                case CoreRoads.Kind.LaneNS:
                case CoreRoads.Kind.NarrowEW:
                case CoreRoads.Kind.NarrowNS:
                case CoreRoads.Kind.StreetEW:
                case CoreRoads.Kind.StreetNS:
                case CoreRoads.Kind.BlvdEW:
                case CoreRoads.Kind.BlvdNS:
                    return true;
                default:
                    return false;
            }
        }

        static Site Pick(
            List<Site> candidates, HashSet<Site> used,
            List<Site> fuel, List<Site> parking, int seed, bool wantsFuel)
        {
            Site best = null;
            double bestScore = double.MinValue;
            foreach (var site in candidates)
            {
                if (used.Contains(site)) continue;
                if (wantsFuel ? !FitsFuel(site) : !FitsParking(site)) continue;

                // Area favours useful parcels; distance spreads the few retained amenities
                // across the large core instead of keeping three adjacent remnants.
                double score = site.Box.width * site.Box.height;
                double distance = NearestDistance(site, fuel, parking);
                if (distance > 0d) score += distance * 0.04d;
                uint tie = unchecked((uint)(seed * 486187739 ^
                    Mathf.RoundToInt(site.Box.xMin) * 73856093 ^
                    Mathf.RoundToInt(site.Box.yMin) * 19349663));
                score += tie / (double)uint.MaxValue;
                if (score <= bestScore) continue;
                best = site;
                bestScore = score;
            }
            return best;
        }

        static double NearestDistance(Site site, List<Site> fuel, List<Site> parking)
        {
            double nearest = double.MaxValue;
            foreach (var other in fuel)
                nearest = System.Math.Min(nearest, (site.Box.center - other.Box.center).sqrMagnitude);
            foreach (var other in parking)
                nearest = System.Math.Min(nearest, (site.Box.center - other.Box.center).sqrMagnitude);
            return nearest == double.MaxValue ? 0d : nearest;
        }

        static bool FitsFuel(Site site)
        {
            Dimensions(site.Box, site.Entry, out float frontage, out float depth);
            if (frontage < FuelFrontage || depth < FuelDepth || depth > FuelParcelMaxDepth)
                return false;

            float remainderFrontage = frontage - FuelFrontage;
            if (remainderFrontage < CoreLayout.Cell) return true;
            return RemainderFitsResidential(site, remainderFrontage) ||
                   frontage <= FuelWholeParcelMaxFrontage;
        }

        static bool RemainderFitsResidential(Site site, float remainderFrontage)
        {
            bool side = site.Entry == ParkingEntrySide.East ||
                        site.Entry == ParkingEntrySide.West;
            return FitsResidential(side ? site.Box.width : remainderFrontage,
                                   side ? remainderFrontage : site.Box.height);
        }

        static bool FitsResidential(float width, float depth)
        {
            int w = Mathf.RoundToInt(width / CoreLayout.Cell);
            int d = Mathf.RoundToInt(depth / CoreLayout.Cell);
            return ResidentialLot.Classify(
                w - 2 * ResidentialLot.Walk, d - 2 * ResidentialLot.Walk) != null;
        }

        static Site CropFuelParcel(Site source, int seed, out Site remainder)
        {
            Rect box = source.Box;
            Dimensions(box, source.Entry, out float frontage, out _);
            float remainderFrontage = frontage - FuelFrontage;
            if (remainderFrontage >= CoreLayout.Cell &&
                !RemainderFitsResidential(source, remainderFrontage))
            {
                // Do not manufacture a 5-15 m strip that can be neither a building nor a
                // street. On a modest parcel that ground is the station's service forecourt.
                remainder = null;
                return source;
            }

            bool farEnd = (unchecked(seed * 486187739 + Mathf.RoundToInt(box.center.x) * 7919 +
                                     Mathf.RoundToInt(box.center.y) * 104729) & 1) != 0;
            Rect station;
            Rect rest;
            if (source.Entry == ParkingEntrySide.South || source.Entry == ParkingEntrySide.North)
            {
                float stationX = farEnd ? box.xMax - FuelFrontage : box.xMin;
                station = new Rect(stationX, box.yMin, FuelFrontage, box.height);
                rest = farEnd
                    ? new Rect(box.xMin, box.yMin, box.width - FuelFrontage, box.height)
                    : new Rect(box.xMin + FuelFrontage, box.yMin, box.width - FuelFrontage, box.height);
            }
            else
            {
                float stationY = farEnd ? box.yMax - FuelFrontage : box.yMin;
                station = new Rect(box.xMin, stationY, box.width, FuelFrontage);
                rest = farEnd
                    ? new Rect(box.xMin, box.yMin, box.width, box.height - FuelFrontage)
                    : new Rect(box.xMin, box.yMin + FuelFrontage, box.width, box.height - FuelFrontage);
            }

            remainder = rest.width >= CoreLayout.Cell && rest.height >= CoreLayout.Cell
                ? new Site(rest, source.Entry,
                    Mathf.RoundToInt(rest.width / CoreLayout.Cell) *
                    Mathf.RoundToInt(rest.height / CoreLayout.Cell))
                : null;
            return new Site(station, source.Entry,
                Mathf.RoundToInt(station.width / CoreLayout.Cell) *
                Mathf.RoundToInt(station.height / CoreLayout.Cell));
        }

        static bool FitsParking(Site site)
        {
            Dimensions(site.Box, site.Entry, out float width, out float depth);
            return ParkingBlockPlan.Generate(width, depth).Stalls.Count >= MinimumParkingBays;
        }

        static void Dimensions(Rect box, ParkingEntrySide entry, out float width, out float depth)
        {
            bool side = entry == ParkingEntrySide.East || entry == ParkingEntrySide.West;
            width = side ? box.height : box.width;
            depth = side ? box.width : box.height;
        }

        public static bool Contains(IReadOnlyList<Site> sites, Vector2 point)
        {
            if (sites == null) return false;
            for (int i = 0; i < sites.Count; i++)
                if (sites[i].Box.Contains(point)) return true;
            return false;
        }

        /// <summary>The complete opaque pad reserved for this PumpDemo station. Selection has
        /// already cropped the road frontage to thirty metres; keeping the available depth
        /// here covers the store, service rear and the global water plane beneath the city.</summary>
        public static Rect FuelSurface(Site site) => site.Box;

        public static float FuelParcelDepth(Site site)
        {
            Dimensions(site.Box, site.Entry, out _, out float depth);
            return depth;
        }

        public static float FuelParcelFrontage(Site site)
        {
            Dimensions(site.Box, site.Entry, out float frontage, out _);
            return frontage;
        }

        /// <summary>Cycle through all three accepted ParkingDemo programmes where their
        /// parcel fits: public, urban-block, then long-stay.</summary>
        public static ParkingBlockStyle ParkingStyle(Site site, int index)
        {
            var first = index % 3 == 0 ? ParkingBlockStyle.Attended
                      : index % 3 == 1 ? ParkingBlockStyle.UrbanBlock
                                       : ParkingBlockStyle.LongStay;
            foreach (var style in new[]
            {
                first,
                ParkingBlockStyle.Attended,
                ParkingBlockStyle.LongStay,
                ParkingBlockStyle.UrbanBlock,
            })
            {
                var surface = ParkingBlockSite.Surface(site.Box, style);
                Dimensions(surface, site.Entry, out float width, out float depth);
                if (ParkingBlockPlan.Generate(width, depth).Stalls.Count >= MinimumParkingBays)
                    return style;
            }
            return ParkingBlockStyle.Attended;
        }

        /// <summary>FuelStation's local +Z faces the road. The compact Core visual stands its
        /// scaled setback inside the parcel; PumpDemo and wayside retain their full size.</summary>
        public static void FuelPose(Site site, out Vector3 anchor, out int yaw)
        {
            var box = site.Box;
            switch (site.Entry)
            {
                case ParkingEntrySide.North:
                    anchor = new Vector3(box.center.x, 0f, box.yMax - FuelSetBack);
                    yaw = 0;
                    break;
                case ParkingEntrySide.East:
                    anchor = new Vector3(box.xMax - FuelSetBack, 0f, box.center.y);
                    yaw = 90;
                    break;
                case ParkingEntrySide.West:
                    anchor = new Vector3(box.xMin + FuelSetBack, 0f, box.center.y);
                    yaw = 270;
                    break;
                default:
                    anchor = new Vector3(box.center.x, 0f, box.yMin + FuelSetBack);
                    yaw = 180;
                    break;
            }
        }
    }
}
