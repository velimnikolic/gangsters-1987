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
        public bool regionalRoads;
        public IReadOnlyList<RasterGateways.Gateway> RegionGateways { get; private set; }
        /// <summary>How many suitable left-over parcels become full ParkingDemo amenities.
        /// Other parking raster cells keep CoreRoads' ordinary painted street bays.</summary>
        public int parkingLotCount = 3;
        /// <summary>Live ParkingDemo cars assigned to each retained car park.</summary>
        public int parkingCarsPerLot = 5;
        /// <summary>Maximum number of stand-alone left-over blocks that become PumpDemo
        /// filling stations. A building's rear lot is never a station candidate.</summary>
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
        int _seed = 1987;
        const string EdgePavement =
            "Assets/Synty/PolygonCity/Prefabs/Environments/SM_Env_Sidewalk_01.prefab";

        /// <summary>The generated residential data, independent of whichever views are
        /// currently on camera. Future generator edits replace/invalidate recipes here.</summary>
        public ResidentialBlockModel ResidentialBlocks => _homes;
        /// <summary>Immutable quarters and named blocks produced by the accepted layout.</summary>
        public CoreTerritoryPlan Territory => _plan?.Territory;
        /// <summary>The accepted poses of the authored Core blocks. This is plan data,
        /// exposed read-only so audits can carry source-prefab module coordinates into the
        /// district frame without instantiating the blocks.</summary>
        public IReadOnlyList<CoreLayout.Block> LayoutBlocks => _blocks;
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
        /// <summary>The first plan-owned fire station, or null when the retained city has no
        /// road-served parcel large enough for the complete engine hall and apron.</summary>
        public CoreAmenityLayout.Site FireStationSite => _services.Sites.Find(s => !s.Police)?.Parcel;
        public CoreServicePlan Services => _services;
        /// <summary>Buildable former parking parcels reassigned to generated housing.
        /// Shallow remnants are deliberately absent: they remain ordinary street parking.</summary>
        public IReadOnlyList<CoreAmenityLayout.Site> DevelopmentSites => _developmentSites;
        /// <summary>Fault count of the accepted road drawing before amenity/residential
        /// programming. Adding views on former parking ground must never worsen it.</summary>
        public int AcceptedRoadFaults { get; private set; }

        // ------------------------------------------------------------------ plan

        /// <summary>
        /// Build only this many quarters of the city. Zero is all of it, which is what
        /// every real build wants; two is the test rig.
        ///
        /// The city is dealt whole - the deal is what makes a coherent plan, and half a
        /// deal is not a smaller city, it is a broken one - and then everything outside
        /// the kept quarters is taken back off it and the roads are read again over what
        /// is left. So the rig is a real piece of the real city, at a size a territory
        /// check can stand up in seconds.
        /// </summary>
        public int quarterBudget;

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
            KeepQuarters();
            AcceptedRoadFaults = _raster != null ? _raster.Faults : 0;
            PlanAmenities();
            DevelopRemainders();
            PlanHomes();
            _bounds = Rect.MinMaxRect(_raster.X0, _raster.Z0,
                                      _raster.X(_raster.NX), _raster.Z(_raster.NZ));
            RegionGateways = regionalRoads ? RasterGateways.Select(_raster, seed) : null;
        }

        /// <summary>
        /// Take the city back down to the quarters the rig asked for. Everything - the
        /// harvested blocks, the made-up housing, the parks, the quays, the leftover
        /// parcels - is dropped unless it stands on the kept ground, the territory is
        /// rebuilt from what survived, and the roads are read again so the tarmac stops
        /// where the city now does. A budget of zero does nothing at all, which is what
        /// every real build wants.
        /// </summary>
        void KeepQuarters()
        {
            if (quarterBudget <= 0 || _plan?.Territory == null)
                return;

            var quarters = new List<CoreQuarterDefinition>();
            foreach (var quarter in _plan.Territory.Quarters)
                if (quarter.BlockIds.Count > 0)
                    quarters.Add(quarter);
            if (quarters.Count <= quarterBudget)
                return;

            var kept = Pick(quarters, quarterBudget);

            // The rig is cut to the SHORTEST kept quarter's latitude. Downtown is a spine
            // running the whole height of the city - keeping it whole would put most of the
            // city back on the rig - but the shops are all on it, and a rig with no shop on
            // it cannot test a protection racket. So downtown comes in beside its neighbour
            // and stops where the neighbour does.
            var shortest = kept[0];
            for (int i = 1; i < kept.Count; i++)
                if (kept[i].LocalBounds.height < shortest.LocalBounds.height)
                    shortest = kept[i];
            float bandLow = shortest.LocalBounds.yMin - CoreLayout.Cell;
            float bandHigh = shortest.LocalBounds.yMax + CoreLayout.Cell;

            var keptGround = new List<Rect>(kept.Count);
            foreach (var quarter in kept)
            {
                var bounds = quarter.LocalBounds;
                float low = Mathf.Max(bounds.yMin - CoreLayout.Cell, bandLow);
                float high = Mathf.Min(bounds.yMax + CoreLayout.Cell, bandHigh);
                if (high <= low) continue;
                // A little slack sideways, so a block whose kerb sits a metre past its
                // quarter's line is not sliced off the rig it plainly belongs to.
                keptGround.Add(Rect.MinMaxRect(bounds.xMin - CoreLayout.Cell, low,
                                               bounds.xMax + CoreLayout.Cell, high));
            }

            Drop(_blocks, keptGround);
            Drop(_plan.Residential, keptGround);
            Drop(_plan.Parks, keptGround);
            Drop(_plan.Quays, keptGround);
            Drop(_plan.Aprons, keptGround);
            DropRects(_plan.Lots, keptGround);
            DropRects(_plan.Outside, keptGround);
            _plan.BeltParks.RemoveWhere(block => !Inside(block.Box, keptGround));

            // The bank stands where the deal put it, which may be a quarter away.
            if (_plan.Bank != null && !Inside(_plan.Bank.Box, keptGround))
                _plan.Bank = null;

            // NO RIVER ON A RIG. It runs the whole length of the city whatever is kept -
            // three and a half kilometres of water, promenade, far bank and bridges - and
            // a rig is two quarters of blocks and the ledger over them.
            _plan.Quays.Clear();
            _plan.Aprons.Clear();
            _plan.Bridges.Clear();
            _plan.RiverApproaches.Clear();
            _plan.Water = new Rect();
            var dry = _plan.River;
            dry.Z0 = 0f;
            dry.Z1 = 0f;
            _plan.River = dry;
            _plan.RiverCityZ0 = 0f;
            _plan.RiverCityZ1 = 0f;

            // Read the ground the way every other pass does - the deal's own parks, the
            // promenade, the far kerb - or the roads are laid over holes.
            var standing = CoreLayout.WithGround(_blocks, _plan);
            _plan.Territory = CoreTerritoryPlan.Build(_seed, standing);
            _raster = CoreRoads.Build(standing, _plan);

            var names = new List<string>(kept.Count);
            foreach (var quarter in kept) names.Add(quarter.Id.ToString());
            Debug.Log($"[CoreDemo] {quarterBudget} quarters: {string.Join(" + ", names)} - " +
                      $"{_blocks.Count} blocks, {_plan.Residential.Count} housing rows, " +
                      $"roads {_raster.NX}x{_raster.NZ} cells.");
        }

        /// <summary>
        /// Which quarters the rig is built out of: the most compact one there is, and then
        /// whichever of its neighbours adds the least ground. Compactness is the whole
        /// point - downtown is a spine running the full height of the city, so a rig that
        /// starts there is the whole city again whatever else it keeps.
        /// </summary>
        List<CoreQuarterDefinition> Pick(List<CoreQuarterDefinition> quarters, int budget)
        {
            // The rig starts where the trade is. Every shopfront in the city stands on a
            // catalogue block, and they are not spread evenly: a rig dealt out of two
            // quarters of housing has nothing to lean on and nothing to buy.
            var shops = new Dictionary<CoreQuarterId, int>();
            foreach (var block in _blocks)
            {
                shops.TryGetValue(block.QuarterId, out int count);
                shops[block.QuarterId] = count + 1;
            }

            var kept = new List<CoreQuarterDefinition>();
            var first = quarters[0];
            for (int i = 1; i < quarters.Count; i++)
                if (Shops(shops, quarters[i]) > Shops(shops, first))
                    first = quarters[i];
            kept.Add(first);

            // Then whichever neighbour of it is the most compact: the rig wants a second
            // quarter to have a border with, not a second city.
            while (kept.Count < budget)
            {
                CoreQuarterDefinition best = null;
                foreach (var candidate in quarters)
                {
                    if (kept.Contains(candidate) || !Touches(kept, candidate))
                        continue;
                    if (best == null || Area(candidate.LocalBounds) < Area(best.LocalBounds))
                        best = candidate;
                }
                if (best == null)
                    break;
                kept.Add(best);
            }
            return kept;
        }

        static int Shops(Dictionary<CoreQuarterId, int> shops, CoreQuarterDefinition quarter) =>
            shops.TryGetValue(quarter.Id, out int count) ? count : 0;

        static bool Touches(List<CoreQuarterDefinition> kept, CoreQuarterDefinition candidate)
        {
            for (int i = 0; i < kept.Count; i++)
            {
                var neighbours = kept[i].Neighbours;
                for (int n = 0; n < neighbours.Count; n++)
                    if (neighbours[n] == candidate.Id)
                        return true;
            }
            return false;
        }

        static float Area(Rect box) => box.width * box.height;

        static void Drop(List<CoreLayout.Block> blocks, List<Rect> keptGround)
        {
            for (int i = blocks.Count - 1; i >= 0; i--)
                if (!Inside(blocks[i].Box, keptGround))
                    blocks.RemoveAt(i);
        }

        static void DropRects(List<Rect> rects, List<Rect> keptGround)
        {
            for (int i = rects.Count - 1; i >= 0; i--)
                if (!Inside(rects[i], keptGround))
                    rects.RemoveAt(i);
        }

        /// <summary>
        /// Whether this piece of ground belongs to the rig. Its middle has to stand on kept
        /// ground AND most of it has to lie there: the quays run the whole length of the
        /// river, and one of those kept for its middle would stretch the road reader's
        /// grid over a mile of city that is no longer being built.
        /// </summary>
        static bool Inside(Rect box, List<Rect> keptGround)
        {
            bool middle = false;
            float covered = 0f;
            for (int i = 0; i < keptGround.Count; i++)
            {
                if (keptGround[i].Contains(box.center)) middle = true;
                covered += Overlap(box, keptGround[i]);
            }
            if (!middle) return false;
            float area = Mathf.Max(0.01f, Area(box));
            return covered / area >= 0.6f;
        }

        static float Overlap(Rect box, Rect ground)
        {
            float width = Mathf.Min(box.xMax, ground.xMax) - Mathf.Max(box.xMin, ground.xMin);
            float height = Mathf.Min(box.yMax, ground.yMax) - Mathf.Max(box.yMin, ground.yMin);
            return width <= 0f || height <= 0f ? 0f : width * height;
        }

        /// <summary>
        /// CoreLayout keeps odd-sized remainder parcels as parking because the road raster
        /// needs accounted-for ground. Only plan.Lots belong to no existing block, so only
        /// those may become independent ParkingDemo or PumpDemo blocks. A shallow authored
        /// block's own rear Lot remains part of that block instead of receiving a second
        /// programme. Unclaimed parcels that can carry the full residential pavement ring
        /// become housing; narrow remnants remain ordinary street-side parking.
        /// </summary>
        void PlanAmenities()
        {
            var candidates = new List<Rect>(_plan.Lots);
            var housing = new List<Rect>();
            foreach (var block in _plan.Residential) housing.Add(block.Box);

            CoreAmenityLayout.Select(
                _raster, candidates, _seed,
                Mathf.Max(0, parkingLotCount), Mathf.Max(0, fuelStationCount),
                _parkingSites, _fuelSites, _developmentSites, housing, _amenityReplacements);

            PickCourthouse();
            _services.Plan(_plan, _raster, _developmentSites, _seed, _amenityReplacements);
        }

        /// <summary>The parcel the courthouse takes, or null when nothing downtown is big
        /// enough for it. Local plan coordinates, like every other amenity site.</summary>
        CoreAmenityLayout.Site _courthouseSite;
        readonly CoreServicePlan _services = new CoreServicePlan();
        readonly List<Rect> _amenityReplacements = new List<Rect>();

        /// <summary>
        /// ONE COURTHOUSE, DOWNTOWN (GAN-237). The rule itself is in the shared layout so
        /// that the seed verdict (gangsters_core) MEASURES the same pick the city makes,
        /// rather than an editor-only replica of it.
        /// </summary>
        void PickCourthouse()
        {
            _courthouseSite = CoreAmenityLayout.PickCourthouse(
                _developmentSites, _plan != null ? _plan.Territory : null);
            if (_courthouseSite == null)
                Debug.Log("[Core] no parcel downtown will hold a courthouse; transfers " +
                          "drive out of town on both legs");
        }

        /// <summary>
        /// Put buildable unclaimed remainders back into the logical plan. These sites come only
        /// from plan.Lots, never an authored block's own Lot, so they remain valid in every
        /// quarter. A parcel too shallow for the shared pavement ring stays ordinary painted
        /// parking rather than being filled kerb-to-kerb with apartments.
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
                if (ResidentialLot.Classify(innerW, innerD) == null)
                    continue;

                var block = CoreLayout.Res(residentialIndex++, w, d, (int)site.Entry);
                block.Pivot = site.Box.min;
                block.QuarterId = QuarterFor(site.Box.center);
                block.Turn(0);
                _plan.Residential.Add(block);
            }

            // These rectangles were already classified as non-road parking ground by the
            // accepted raster. Keep that topology: rebuilding it with view-only blocks closes
            // nearby street mouths and merges crossings. The fallback layer owns the visible
            // ground while a detailed view is streamed out, so CoreRoads must not add a second,
            // coplanar hardstanding surface below it.
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
                if (_services.Replaces(box) || _amenityReplacements.Contains(box)) continue;
                int w = Mathf.Max(1, Mathf.RoundToInt(box.width / CoreLayout.Cell));
                int d = Mathf.Max(1, Mathf.RoundToInt(box.height / CoreLayout.Cell));
                int dice = unchecked(_seed * 7919 + Mathf.RoundToInt(box.xMin) * 104729 +
                                     Mathf.RoundToInt(box.yMin) * 1299709);

                // a yard block is one lot on its own plot; every other one is divided into
                // houses, gaps and yards
                var lot = CoreLayout.IsYard(block)
                    ? ResidentialLot.Yard(w, d, dice, block.Unit)
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

        /// <summary>How many authored blocks the deal actually stood. The law is dealt
        /// off this rather than off the pavement: two beat pairs to a block is a rule
        /// about the CITY, and a rig cut to two quarters must get two quarters' worth of
        /// policemen and not a whole city's.</summary>
        public int BlockCount => _blocks.Count;

        /// <summary>How many quarters the city standing on the ground is made of - the
        /// budget where the rig was cut to one, and the plan's own count otherwise. What
        /// the patrol fleet is dealt per quarter is measured against this.</summary>
        public int QuarterCount =>
            quarterBudget > 0
                ? quarterBudget
                : _plan?.Territory != null ? _plan.Territory.Quarters.Count : 0;
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
            if (_raster == null) return false;
            if (RasterGateways.InMouth(RegionGateways, new Vector3(
                _raster.X(i) + 2.5f, 0f, _raster.Z(j) + 2.5f))) return false;
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
            // The quays are the heaviest thing in the city and a budgeted plan is a rig,
            // not a port: skipped whole rather than dealt small, because half a harbour
            // is a worse lie than none.
            if (quarterBudget <= 0)
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
            // the channels the leaves span. Full amenity parcels still use ParkingDemo below;
            // unclaimed narrow parking cells use CoreRoads' ordinary painted street bays.
            CoreRoads.Lay(_raster, (prefab, parent) => Object.Instantiate(prefab, parent), roads,
                          RiverBridge.Skip(_plan, _raster), layCarParks: true,
                          skipParking: ComposedSurfaceAt);
            StandCityEdgePavement(roads);
            // A rig does not need the grid overhead: thousands of renderers of pole and
            // wire over ground a territory test never looks up from.
            if (quarterBudget <= 0)
                CorePowerlines.Stand(_plan, _raster, quarter, _seed);
            var river = new GameObject("River").transform;
            river.SetParent(quarter, false);
            RiverBridge.Dress(_plan, river, (prefab, parent) => Object.Instantiate(prefab, parent), layWater: !regionalRoads);
            // the fairground's wheel turns, as the grid city's does
            foreach (var t in _yard.GetComponentsInChildren<Transform>(true))
                if (t.name.Contains("Ferris") && t.name.Contains("_Rotate") && t.GetComponent<DemoFerrisWheel>() == null)
                    t.gameObject.AddComponent<DemoFerrisWheel>();

            // everything above was laid in the quarter's own coordinates; the frame is
            // where the city put it, and the lane graph below is built in world ones
            quarter.SetPositionAndRotation(Frame.origin, Frame.Rotation);

            BuildLaneGraph();
            StandAmenities(quarter, host);
            CoreServiceViews.Build(_services, quarter, host, Net, _seed);
            StandCourthouse(quarter, host);
            BuildPavementGraph();
            InstallBascules(host, river);
            SpawnCars(host.LiveRoot("Core Traffic"));

            host.RegisterRoads(_edges);
            host.RegisterPavement(_walks);
            for (int i = 0; i < _vehicles.Count; i++) host.RegisterVehicle(_vehicles[i]);
            BlockTheStaticGeometry(host);
            BlockTheResidential(host);
            // Finalize after every building solid is known, so interior/child meshes
            // are not entered again as street furniture.
            WalkObstacles.BlockComposedProps(quarter, Frame.origin.y);
            _recycler?.PrepareNavigation();

            Debug.Log($"[Core] {_plan.Name}: {_blocks.Count} blocks, {_raster.Junctions.Count} junctions, " +
                      $"{_raster.Stretches.Count} stretches of road, {_edges.Count} lanes, " +
                      $"{_vehicles.Count} traffic cars, {_parkingSites.Count} ParkingDemo lots, " +
                      $"{_fuelSites.Count} PumpDemo station(s), " +
                      $"{_services.FireCount} fire station(s), {_services.TotalPoliceCount}/{_services.PoliceTarget} precincts, " +
                      $"{_raster.Faults} faults.{System.Environment.NewLine}" +
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

        /// <summary>The amenity composers and residential fallback layer bring their own
        /// complete surface. Every other raster parking cell receives CoreRoads' ordinary
        /// painted bays; skipping these footprints prevents the triangular coplanar flicker
        /// visible through streamed residential pavement.</summary>
        public bool ComposedSurfaceAt(int i, int j)
        {
            var centre = new Vector2(_raster.X(i) + CoreRoads.Cell * 0.5f,
                                     _raster.Z(j) + CoreRoads.Cell * 0.5f);
            if (CoreAmenityLayout.Contains(_parkingSites, centre)) return true;
            for (int k = 0; k < _fuelSites.Count; k++)
                if (CoreAmenityLayout.FuelSurface(_fuelSites[k]).Contains(centre)) return true;
            // The courthouse parcel is taken OUT of the development list so it cannot also
            // become housing (CoreAmenityLayout.PickCourthouse), which would leave it
            // reading as vacant ground to anything counting programmes off this - and a
            // parcel with a courthouse on it is the least vacant ground in the city.
            if (_courthouseSite != null && _courthouseSite.Box.Contains(centre)) return true;
            if (_services.SurfaceAt(centre)) return true;
            return CoreAmenityLayout.Contains(_developmentSites, centre);
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
            for (int i = 0; i < _fuelSites.Count; i++)
            {
                var planned = _fuelSites[i];
                CoreAmenityLayout.FuelBlockPose(
                    planned, out var localPosition, out int localYaw);

                // FuelStationBlock's shared composers measure in world space while they
                // stand their pieces. Compose once at the origin, inactive so its runtime
                // cannot wake early, then move the finished full-size block onto Core ground.
                var go = new GameObject($"Core Filling Station {i + 1:00} (Full PumpDemo Block)");
                go.SetActive(false);
                var root = go.transform;
                int stationSeed = unchecked(_seed * 7919 + i * 104729 + 3571);
                var stood = FuelStationBlock.Compose(root, stationSeed);
                root.SetParent(quarter, false);
                root.localPosition = localPosition;
                root.localRotation = Quaternion.Euler(0f, localYaw, 0f);

                var runtime = go.GetComponent<FuelStationBlockRuntime>();
                if (runtime != null) runtime.BindCityRoad(Net);
                go.SetActive(true);

                // Publish the actual shop as the map/building obstacle, not the surrounding
                // pavement or driveable apron. The runtime owns precise forecourt blockers.
                if (stood.Station != null)
                {
                    var shop = root.TransformPoint(stood.Station.At(0f, FuelStation.ShopZ));
                    float yaw = (root.rotation * stood.Station.Rot).eulerAngles.y;
                    bool side = Mathf.Abs(Mathf.Sin(yaw * Mathf.Deg2Rad)) > 0.5f;
                    var size = side
                        ? new Vector3(FuelStation.ShopHalfZ * 2f, 10f, FuelStation.ShopHalfX * 2f)
                        : new Vector3(FuelStation.ShopHalfX * 2f, 10f, FuelStation.ShopHalfZ * 2f);
                    host.Blocked(new Bounds(shop + Vector3.up * 5f, size),
                        $"Filling Station {i + 1}");
                }
            }
        }

        /// <summary>
        /// The courthouse itself, stood on the parcel PickCourthouse kept for it. It is a
        /// building and nothing else - no interior, no cells, no clerk (GAN-219's own
        /// rule) - and what it is FOR is that the man in the back of a police car is
        /// driven somewhere the player can see, follow and get in front of.
        /// </summary>
        void StandCourthouse(Transform quarter, IDistrictHost host)
        {
            if (_courthouseSite == null) return;
            var prefab = DemoAssetLoad.Load<GameObject>(CourthousePrefab);
            if (prefab == null)
            {
                host.ReportMissing(CourthousePrefab);
                return;
            }

            var box = _courthouseSite.Box;
            var go = Object.Instantiate(prefab, quarter, false);
            go.name = CourthouseName;
            go.transform.localPosition = new Vector3(box.center.x, 0f, box.center.y);

            // The front looks at the street the parcel's entry side names, which is the
            // side CoreAmenityLayout already read off the raster for a driveway. The bake
            // faces +Z, so the yaw is that side's outward direction.
            go.transform.localRotation = Quaternion.Euler(0f, YawFor(_courthouseSite.Entry), 0f);

            var bounds = new Bounds(go.transform.position, Vector3.zero);
            var seen = false;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (!seen) { bounds = r.bounds; seen = true; }
                else bounds.Encapsulate(r.bounds);
            }
            if (seen) host.Blocked(bounds, CourthouseName);
        }

        const string CourthousePrefab = "Assets/CityKit/Buildings/building-courthouse.prefab";

        /// <summary>What the court is called on the map, the ledger and the announcement.
        /// The name the sweep matches on, too - so it is a constant and not a literal at
        /// three call sites.</summary>
        public const string CourthouseName = "building-courthouse";
        public const string FireStationName = "building-firestation";

        /// <summary>The outward yaw of a parcel's entry side: which way a building on it
        /// turns to face its street.</summary>
        static float YawFor(ParkingEntrySide entry) => entry switch
        {
            ParkingEntrySide.North => 0f,
            ParkingEntrySide.East => 90f,
            ParkingEntrySide.South => 180f,
            _ => 270f,
        };

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
                CorePavement.CageExistingPalms(block.Go.transform,
                    (piece, parent) => Object.Instantiate(piece, parent));
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
        /// The quarter's traffic uses the city's placement and driving rules.
        /// </summary>
        void SpawnCars(Transform parent)
        {
            if (carCount <= 0 || _edges.Count == 0) return;
            var dice = new System.Random(_seed);
            foreach (var slot in TrafficDistribution.Place(_edges, carCount, _seed))
            {
                var prefab = CoreRoads.PickCar(dice);
                if (prefab == null) return;
                var go = Object.Instantiate(prefab, Vector3.zero, Quaternion.identity, parent);
                LivingCity.Gameplay.VehiclePaint.Apply(go, prefab);
                foreach (var body in go.GetComponentsInChildren<Rigidbody>()) Object.Destroy(body);
                foreach (var collider in go.GetComponentsInChildren<Collider>()) Object.Destroy(collider);

                CarBody.MeasureTrafficFootprint(go.transform, out float halfLength, out float halfWidth);
                var car = new DemoVehicle
                {
                    Tf = go.transform,
                    HalfLen = halfLength,
                    HalfWide = halfWidth,
                };
                car.Spawn(slot.Lane, slot.Progress);
                _vehicles.Add(car);
                StreetTraffic.Users.Add(car);   // the men on foot, and the outfit's drivers, see it
            }
        }

        /// <summary>Buildings become walls; props, trees and parked display vehicles
        /// remain furniture. Both stop feet, but only buildings stop sight and become
        /// map-card footprints.</summary>
        void BlockTheStaticGeometry(IDistrictHost host)
        {
            foreach (var block in _blocks)
            {
                if (block.Go == null) continue;
                foreach (Transform piece in block.Go.transform)
                {
                    if (!piece.name.StartsWith("SM_Bld_", System.StringComparison.OrdinalIgnoreCase)) continue;
                    // The authored Core blocks already expose their individual building
                    // roots. Register that boundary before rendering/merging so a cutaway
                    // never mistakes the whole city block for one building.
                    BuildingCutaway.Prepare(piece.gameObject);
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
                var masses = recipe.TurfMasses;
                for (int i = 0; i < masses.Count; i++)
                {
                    var mass = masses[i];
                    bool lot = mass.SourceKind == ResidentialKind.Park ||
                               mass.SourceKind == ResidentialKind.Amenity;
                    // Ordinary residential buildings retain their authored 5 m wall
                    // masks below. Their turf proxy includes roof/canopy overhangs which
                    // are useful to the map but can cover walkable ground at foot level.
                    if (!lot) continue;
                    // Open amenity proxies describe things visible on the map, not a
                    // solid lot. Closed diners are the exception, and only their baked
                    // one-metre structural proxy is exact enough for a permanent wall;
                    // a missing bake must fail back to the streamed shell, never to the
                    // old broad 5 m mask which swallowed the terrace.
                    if (lot && (!WalkObstacles.PhysicalVenueName(mass.SourceName) ||
                                !mass.PrefabDerived))
                        continue;

                    float bottom = Frame.origin.y + mass.Bottom;
                    float top = Frame.origin.y + mass.Top;
                    const float ankle = 0.06f;
                    const float shoulder = 1.9f;
                    if (top < Frame.origin.y + ankle ||
                        bottom > Frame.origin.y + shoulder)
                        continue;

                    var world = Frame.ToWorldRect(mass.Local);
                    float height = Mathf.Max(0.05f, top - bottom);
                    var box = new Bounds(
                        new Vector3(world.center.x, bottom + height * 0.5f,
                                    world.center.y),
                        new Vector3(world.width, height, world.height));
                    host.Blocked(box, $"{recipe.Name}: {mass.SourceName}");
                }

                foreach (var spot in recipe.Plan.Spots)
                {
                    var unit = spot?.Unit;
                    if (unit == null || ResidentialUnits.IsLot(unit)) continue;
                    var turn = ResidentialLot.Turn.Of(unit, spot.Yaw);
                    var used = new bool[turn.CW, turn.CD];
                    for (int j = 0; j < turn.CD; j++)
                    for (int x0 = 0; x0 < turn.CW; x0++)
                    {
                        if (used[x0, j] || !turn.Wall(x0, j)) continue;
                        int wide = 1;
                        while (x0 + wide < turn.CW && !used[x0 + wide, j] &&
                               turn.Wall(x0 + wide, j)) wide++;
                        int deep = 1;
                        bool grow = true;
                        while (j + deep < turn.CD && grow)
                        {
                            for (int x = 0; x < wide; x++)
                                if (used[x0 + x, j + deep] ||
                                    !turn.Wall(x0 + x, j + deep))
                                { grow = false; break; }
                            if (grow) deep++;
                        }
                        for (int x = 0; x < wide; x++)
                            for (int z = 0; z < deep; z++) used[x0 + x, j + z] = true;

                        float cell = ResidentialLot.Cell;
                        var local = new Rect(
                            recipe.LocalBounds.xMin + (spot.I + x0) * cell,
                            recipe.LocalBounds.yMin + (spot.J + j) * cell,
                            wide * cell, deep * cell);
                        var world = Frame.ToWorldRect(local);
                        float bottom = Mathf.Min(0f, unit.Floor);
                        float height = Mathf.Max(2f, unit.MaxH - bottom);
                        var box = new Bounds(
                            new Vector3(world.center.x,
                                Frame.origin.y + bottom + height * 0.5f, world.center.y),
                            new Vector3(world.width, height, world.height));
                        host.Blocked(box, $"{recipe.Name}: {unit.Name}");
                    }
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
            _courthouseSite = null;
            _services.Clear();
            _amenityReplacements.Clear();
            RegionGateways = null;
            // Build-time geometry normally dies with its host. A detached compatibility
            // yard is still ours to clean explicitly.
            if (_yard != null && _yard.parent == null) Object.Destroy(_yard.gameObject);
        }
    }

}
