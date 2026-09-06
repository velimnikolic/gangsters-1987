using UnityEngine;

namespace RoadDemo
{
    /// <summary>
    /// Lets the game's runtime stand on a district-supplied city structure instead
    /// of building its rectangular grid.  CoreDemo uses this path; traffic, crowd,
    /// police, crews, combat, time, audio and map remain the ordinary RoadDemo passes.
    /// </summary>
    public partial class RoadDemoBuilder
    {
        IDistrict _primaryStructure;
        int _primaryStructureSeed;
        Rect _primaryWorld;
        CoreRegion _coreRegion;
        IslandLandform _regionalIsland;
        public CoreRegion Region => _coreRegion;

        /// <summary>True when this builder is hosting an alternative city structure.</summary>
        public bool HasPrimaryStructure => _primaryStructure != null;

        /// <summary>The whole structural town in world XZ, valid after its plan pass.</summary>
        public Rect PrimaryWorldBounds => _primaryWorld;

        /// <summary>Core-specific plan access for shared read-only adapters such as the
        /// survey map. Gameplay still talks to the generic territory/streaming interfaces.</summary>
        internal CoreDistrict PrimaryCore => _primaryStructure as CoreDistrict;

        /// <summary>
        /// Select a district as the whole city structure. Call while the builder's
        /// GameObject is inactive, before Awake runs.
        /// </summary>
        public void ConfigurePrimaryStructure(IDistrict structure, int seed)
        {
            if (structure == null)
                throw new System.ArgumentNullException(nameof(structure));
            if (isActiveAndEnabled)
                throw new System.InvalidOperationException(
                    "ConfigurePrimaryStructure must run before RoadDemoBuilder is activated.");
            _primaryStructure = structure;
            _primaryStructureSeed = seed;
        }

        void PlanPrimaryStructure()
        {
            BuiltFromSeed = _primaryStructureSeed;
            _primaryStructure.Frame = DistrictFrame.Identity;
            _primaryStructure.Plan(null, _primaryStructureSeed);

            // As in the standalone district demos, its south-west plan corner sits on
            // the world origin. The structure itself never has to know who hosts it.
            var local = _primaryStructure.LocalBounds;
            _primaryStructure.Frame = DistrictFrame.At(-local.xMin, -local.yMin, 0);
            var structureWorld = _primaryStructure.Frame.ToWorldRect(local);
            // The raster bounds are the tarmac. Pavement centres and a crew's loose
            // formation stand just outside the outer kerb, so the playable/map/island
            // bound must carry that last half pavement as well.
            float pavement = SidewalkWidth * 0.5f;
            _primaryWorld = Rect.MinMaxRect(
                structureWorld.xMin - pavement, structureWorld.yMin - pavement,
                structureWorld.xMax + pavement, structureWorld.yMax + pavement);

            _primaryStructure.Reserve(_reservations);
            _districtPlans.Add(new DistrictPlan(
                _primaryStructure.Name, DistrictKind.Pad, _primaryWorld));
            _built.Add(_primaryStructure);

            WalkObstacles.City.Clear();
            WalkObstacles.City.Add(_primaryWorld);
            Debug.Log($"[RoadDemo] primary structure: {_primaryStructure.Name}, " +
                      $"{_primaryWorld.width:F0} x {_primaryWorld.height:F0} m, seed {_primaryStructureSeed}");
        }

        void BuildPrimaryStructure()
        {
            EnsureLife();
            _districtGroup = new GameObject(_primaryStructure.Name).transform;
            _districtGroup.SetParent(DistrictRoot, false);
            _primaryStructure.Build(this);
            _districtGroup = null;

            // Core's registered RoadEdges are the traffic list; its LaneNet also has
            // to become the active network used by bikes, crews and route helpers.
            if (_primaryStructure is CoreDistrict core)
            {
                Net = core.Net;
                LaneNet.Active = Net;
            }

            if (PrimaryCore != null && PrimaryCore.regionalRoads) BuildCoreRegion();

            if (Net == null)
                Debug.LogError($"[RoadDemo] {_primaryStructure.Name} supplied no LaneNet; " +
                               "cars and route-driven systems cannot run.");
            if (_pedLinks.Count == 0)
                Debug.LogError($"[RoadDemo] {_primaryStructure.Name} supplied no pavement; " +
                               "people, crews and foot police cannot run.");
        }
    }
}
