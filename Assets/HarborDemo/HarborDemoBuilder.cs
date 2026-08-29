using System.Collections.Generic;
using RoadDemo;
using UnityEngine;

namespace HarborDemo
{
    // The port's own scene: one component that stands the district up at the origin
    // and hands it a StandaloneDistrictHost for the sun, the camera, the pause keys
    // and the perf pass. The port ITSELF is HarborDistrict, the same object the city
    // builds on one of its shores (RoadDemoBuilder.Districts.cs) - so anything changed
    // here, in this scene, is what the city gets.
    //
    // The fields below are the district's own defaults, out on the inspector for
    // trying things: they are copied onto the district before it is planned.
    public class HarborDemoBuilder : MonoBehaviour
    {
        [Header("Port")]
        [Range(1, 6)] public int berths = 5;
        [Tooltip("Metres from one berth's centre to the next along the quay.")]
        public float berthPitch = 90f;
        [Tooltip("Depth of the concrete working area behind the quay, to the fence.")]
        public float apronDepth = 65f;
        public int seed = 1987;

        [Header("Shipping")]
        [Tooltip("Seconds a ship lies alongside being worked.")]
        public Vector2 stayRange = new Vector2(60f, 120f);
        [Tooltip("Seconds a berth stands empty between one ship's leaving and the next one's showing.")]
        public Vector2 gapRange = new Vector2(15f, 45f);
        [Tooltip("A freighter's cruising speed on the coast run, m/s.")]
        public float sailSpeed = 8f;
        [Tooltip("Ships and boats crossing far out that never dock.")]
        public bool passingTraffic = true;
        [Tooltip("A ship-to-shore gantry over every berth, working the boxes on and off.")]
        public bool quayCranes = true;
        [Tooltip("How hard the surf breaks along the beach.")]
        [Range(0f, 1f)] public float shoreFoam = 0.25f;
        [Tooltip("How much sand shows through the water at the shore.")]
        [Range(0f, 1f)] public float shallowSand = 0.6f;

        [Header("Life on the quay")]
        [Range(0, 24)] public int dockWorkers = 9;
        [Tooltip("Hands aboard every freighter besides her master.")]
        [Range(0, 8)] public int shipCrew = 6;
        public bool forklifts = true;
        [Tooltip("Lorries in off the approach road, through a gate, onto a shed door to " +
                 "be worked, and out through the other gate.")]
        public bool deliveryTruck = true;
        [Range(0, 6)] public int lorries = 3;

        [Header("What the port is")]
        [Tooltip("Let the berths be more than box berths: a bulk quay with its heaps, a " +
                 "roll-on quay with its ranks of imports, a fishing wall. At most one of " +
                 "each to a port, and never every berth.")]
        public bool mixedBerths = false;
        [Tooltip("A boom over each gate lane that lifts for a lorry, the weighbridge in " +
                 "the inbound lane, the customs post and the lay-by.")]
        public bool gateWorks = true;
        [Tooltip("One box that is watched, a hole cut in the wire away from the gates, " +
                 "and a bonded store standing empty with a board on it.")]
        public bool contraband = true;

        [Header("Industrial zone")]
        [Tooltip("Stand about ten industrial blocks on the landward side of the harbor road.")]
        public bool industrialZone = true;
        [Tooltip("Seed 23 deals ten roadside industrial parcels and a clean road raster.")]
        public int industrialSeed = 23;

        void Awake()
        {
#if UNITY_EDITOR
            // the ships and boxes are baked before Play by Editor/HarborDemoAutoBake
            var district = new HarborDistrict
            {
                // The combined HarborDemo is a full container terminal: five working
                // berths guarantee five gantries. The standalone harbor-only switch keeps
                // the inspector's ordinary smaller/mixed-port controls.
                berths = industrialZone ? Mathf.Max(5, berths) : berths,
                berthPitch = berthPitch,
                apronDepth = apronDepth,
                stayRange = stayRange,
                gapRange = gapRange,
                sailSpeed = sailSpeed,
                passingTraffic = passingTraffic,
                quayCranes = industrialZone || quayCranes,
                shoreFoam = shoreFoam,
                shallowSand = shallowSand,
                dockWorkers = dockWorkers,
                shipCrew = shipCrew,
                forklifts = forklifts,
                deliveryTruck = deliveryTruck,
                lorries = lorries,
                mixedBerths = industrialZone ? false : mixedBerths,
                gateWorks = gateWorks,
                contraband = contraband,
            };

            IDistrict scene = district;
            if (industrialZone)
                scene = new HarborIndustrialDistrict(
                    district,
                    new IndustrialDistrict(),
                    industrialSeed);

            var host = gameObject.AddComponent<StandaloneDistrictHost>();
            if (industrialZone)
            {
                // Let the shared host centre the union of the port and the works. The
                // higher view keeps the quay, its road and the whole estate readable as
                // one district rather than opening on one end of the old quay close-up.
                host.cameraDistance = 380f;
                host.cameraYaw = 14f;
                host.cameraPitch = 48f;
                host.cameraFar = 2500f;
            }
            else
            {
                // from over the water, low enough that the stacks and the sheds stand up
                // rather than lie flat as a plan
                host.cameraPivot = new Vector3(0f, 0f, 28f);
                host.cameraDistance = 125f;
                host.cameraYaw = 0f;
                host.cameraPitch = 36f;
            }
            host.skyboxSky = false;                       // the port's plain sky
            host.clearColour = new Color(0.55f, 0.66f, 0.78f);
            host.sunAngles = new Vector3(50f, 20f, 0f);   // over the water
            host.sunIntensity = 1.25f;
            host.reflectionProbe = false;
            host.hint = "WASD/arrows: move   Q/E or right-drag: rotate   wheel: zoom   " +
                        "Space: pause   , . : slower/faster";
            host.HostSeeded(scene, seed);
#else
            Debug.LogError("[HarborDemo] This demo loads Synty prefabs through the AssetDatabase and only runs in the editor.");
#endif
        }
    }

    /// <summary>
    /// The standalone harbor scene as one hostable district. The harbor's back road is
    /// the district's single artery: industry occupies its landward side and its ordinary
    /// service streets open directly onto it. IndustrialDemo keeps its own boulevard;
    /// this roadside composition deliberately does not stand a second one.
    /// </summary>
    public sealed class HarborIndustrialDistrict : IDistrict
    {
        readonly HarborDistrict _harbor;
        readonly IndustrialDistrict _industry;
        readonly int _industrialSeed;
        readonly List<DistrictPortal> _portals = new List<DistrictPortal>();

        const float RoadDriftAllowance = 45f;

        DistrictFrame _frame = DistrictFrame.Identity;
        Vector2 _industryOffset;
        Rect _bounds;

        public HarborIndustrialDistrict(HarborDistrict harbor, IndustrialDistrict industry,
                                        int industrialSeed)
        {
            _harbor = harbor;
            _industry = industry;
            _industrialSeed = industrialSeed;
        }

        public string Name => "Harbor + Industry";
        public DistrictFrame Frame { get => _frame; set => _frame = value; }
        public Rect LocalBounds => _bounds;
        public IReadOnlyList<DistrictPortal> Portals => _portals;
        public int IndustrialParcelCount => _industry.Layout != null ? _industry.Layout.Parcels.Count : 0;

        public void Plan(float[] links, int seed)
        {
            // This composition belongs to the standalone work scene. The shared industry
            // generator deals only its landward half; IndustrialDemo's ordinary plan is
            // unchanged and still owns its boulevard.
            _harbor.Frame = DistrictFrame.Identity;
            _harbor.Plan(null, seed);
            _industry.Frame = DistrictFrame.Identity;
            _industry.externalArtery = true;
            _industry.Plan(null, _industrialSeed);

            var harbor = _harbor.LocalBounds;
            var industry = _industry.LocalBounds;
            var sharedRoad = _industry.ExternalRoad;
            float x = harbor.center.x - industry.center.x;

            // If a service street is already close to the west harbor gate, line the two
            // up exactly. It becomes a proper four-way crossing through the same strip of
            // asphalt instead of two junctions drawn a few metres apart.
            if (_industry.ExternalJunctionXs.Count > 0)
            {
                float westGate = -_harbor.QuayHalf + 20f;
                float nearest = _industry.ExternalJunctionXs[0];
                float distance = Mathf.Abs(nearest + x - westGate);
                foreach (float junction in _industry.ExternalJunctionXs)
                {
                    float candidate = Mathf.Abs(junction + x - westGate);
                    if (candidate >= distance) continue;
                    distance = candidate;
                    nearest = junction;
                }
                if (distance <= 30f) x = westGate - nearest;
            }

            _industryOffset = new Vector2(x, -sharedRoad.y * 0.5f - sharedRoad.x * 0.5f);
            var placedIndustry = Shift(industry, _industryOffset);
            var placedSurface = Shift(_industry.LocalSurfaceBounds, _industryOffset);
            _bounds = Union(harbor, Rect.MinMaxRect(
                placedIndustry.xMin, placedIndustry.yMin,
                placedIndustry.xMax, placedIndustry.yMax + RoadDriftAllowance));

            var northLinks = new List<float>();
            foreach (float junction in _industry.ExternalJunctionXs)
                northLinks.Add(junction + _industryOffset.x);
            _harbor.SetStandaloneBackStreetNorthLinks(northLinks);

            // HarborDemo supplies its own heightfield. Extend that field around the new
            // estate and leave a clean hole beneath its parcel surfaces. The allowance is
            // northward because the exact harbor road line is known only after its sheds
            // have been measured during Build.
            _harbor.PrepareStandaloneGround(Rect.MinMaxRect(
                placedSurface.xMin, placedSurface.yMin,
                placedSurface.xMax, placedSurface.yMax + RoadDriftAllowance));
        }

        public void Reserve(DistrictReservations into)
        {
            ApplyPlannedFrames();
            _harbor.Reserve(into);
            _industry.Reserve(into);
        }

        public void Build(IDistrictHost host)
        {
            _harbor.Frame = ChildFrame(Vector2.zero);
            _harbor.Build(host);
            ApplyIndustryFrame(_harbor.BackStreetContractZ);
            _industry.Build(host);
            Debug.Log($"[HarborDemo] industrial zone: {IndustrialParcelCount} parcels, " +
                      $"seed {_industrialSeed}, joined to the harbor road at " +
                      $"{_industry.ExternalJunctionXs.Count} service junctions; no industrial boulevard.");
        }

        public void Tick(float dt)
        {
            _harbor.Tick(dt);
            _industry.Tick(dt);
        }

        public void Dispose()
        {
            _industry.Dispose();
            _harbor.Dispose();
        }

        void ApplyPlannedFrames()
        {
            _harbor.Frame = ChildFrame(Vector2.zero);
            ApplyIndustryFrame(0f);
        }

        void ApplyIndustryFrame(float harborRoadZ)
        {
            _industry.Frame = ChildFrame(new Vector2(
                _industryOffset.x,
                _industryOffset.y + harborRoadZ));
        }

        DistrictFrame ChildFrame(Vector2 offset) => new DistrictFrame
        {
            origin = _frame.ToWorld(new Vector3(offset.x, 0f, offset.y)),
            yaw = _frame.yaw,
        };

        static Rect Shift(Rect rect, Vector2 by) =>
            new Rect(rect.x + by.x, rect.y + by.y, rect.width, rect.height);

        static Rect Union(Rect one, Rect other) => Rect.MinMaxRect(
            Mathf.Min(one.xMin, other.xMin),
            Mathf.Min(one.yMin, other.yMin),
            Mathf.Max(one.xMax, other.xMax),
            Mathf.Max(one.yMax, other.yMax));
    }
}
