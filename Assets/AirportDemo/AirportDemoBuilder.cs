using System.Collections.Generic;
using RoadDemo;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace AirportDemo
{
    // A 1987 American regional airport, built at Play out of this one component: a
    // runway long and wide enough for the trijet that brings the morning flight in,
    // with its parallel taxiway and four connectors, painted to the FAA's own
    // dimensions and lit round its edges; a continuous concrete ramp behind it with
    // the light aeroplanes on their tie-down rows, the two airline stands and the
    // helipad; the row of box hangars and the maintenance shop at the west end, the
    // FBO and its fuel island, the terminal and the control tower in the middle, the
    // fire station, the freight shed and the fuel farm at the east; the wire fence
    // with its two gates; and landside, the kerb loop under its canopy, the car park
    // and the approach road.
    //
    // And it works: aeroplanes start up, taxi the graph, hold short of the runway,
    // line up, roll, climb out, fly the circuit or go off the map and come back down
    // final; a bowser meets whoever shuts down; the baggage train runs out to the
    // stand; passengers walk out of the terminal across the ramp, because in 1987 at
    // a field like this that is exactly what they did.
    //
    // The AIRCRAFT are Simple Airport's - the only pack in the project with any -
    // and are scaled at Play to the span their class flies at. EVERYTHING ELSE is
    // Synty: the people, the ground vehicles, the buildings, the furniture. That
    // split is deliberate; see the note at the top of AirportKit. What no pack has -
    // hangars at aircraft scale, the tower, the windsock, the airfield lights, the
    // ground equipment - is baked out of Synty modules by Editor/AirportKitBash
    // before Play. From the other demos this borrows only machinery: the camera rig,
    // the walker base and its clips, the wheel spin, the street kit and the perf
    // pass. The traffic is its own - the road demo's lane graph is being rebuilt as
    // this is written, and a one-way kerb loop wants none of it.
    public partial class AirportDemoBuilder : MonoBehaviour
    {
        [Header("The field")]
        public int seed = 1987;
        [Tooltip("Runway length in metres. 1800 m (6,000 ft) takes the trijet; 1200 is a plain general aviation strip and the jet will not use it.")]
        public float runwayLength = AirportSpec.RunwayLength;
        [Tooltip("Which way the wind is blowing: with it westerly, runway 27 is in use and every circuit is flown to the west.")]
        public bool westerlyWind = true;
        [Tooltip("Edge lights, threshold bars, PAPI and the beacon. Off saves a hundred and thirty small renderers.")]
        public bool airfieldLighting = true;
        [Tooltip("How many of the six box hangars stand open with an aeroplane inside.")]
        [Range(0, 3)] public int openHangars = 1;

        [Header("Flying")]
        [Tooltip("Aeroplanes working the field: taxiing, flying the circuit, coming and going. The first two take the airline stands - a trijet and a turboprop - and the rest are light singles off the tie-downs.")]
        [Range(0, 10)] public int activeAircraft = 6;
        [Tooltip("Aeroplanes tied down on the general aviation ramp, going nowhere.")]
        [Range(0, 30)] public int parkedAircraft = 15;
        [Tooltip("Seconds between one commuter departure and the next.")]
        public float commuterInterval = 220f;
        [Tooltip("Two helicopters on the pad: one that keeps it and flies a patrol, one charter that comes and goes.")]
        public bool helicopters = true;

        [Header("The ground")]
        [Tooltip("A bowser out to every aeroplane that shuts down, a baggage train to the commuter stand, a follow-me to lead an arrival in.")]
        public bool groundEquipment = true;
        [Range(0, 20)] public int rampCrew = 8;
        [Tooltip("Lorries in through the freight gate to the shed and out again.")]
        [Range(0, 4)] public int lorries = 2;

        [Header("Landside")]
        [Range(0, 40)] public int cars = 18;
        [Range(0, 120)] public int parkedCars = 60;
        [Range(0, 80)] public int passengers = 34;
        [Tooltip("A sheriff's car on the kerb and a plain sedan watching the general aviation gate - 1987, and this is how the cocaine came north.")]
        public bool theLaw = true;

        // ------------------------------------------------------------ roots
        Transform _root, _groundRoot, _airsideRoot, _markingRoot, _lightRoot, _apronRoot,
                  _buildingRoot, _fenceRoot, _landsideRoot, _detailRoot, _floraRoot, _liveRoot, _mergedRoot;

        System.Random _rng;
        PedClips _clips;
        FlightOps _flights;
        GroundOps _ground;
        AirportPeople _people;
        AirportTraffic _traffic;
        readonly List<Rotorcraft> _rotors = new List<Rotorcraft>();
        readonly List<PedestrianAgent> _idlers = new List<PedestrianAgent>();
        bool _merged;

        /// <summary>Half the runway, from the middle of the field.</summary>
        public float RunwayHalf => runwayLength * 0.5f;

        void Awake()
        {
#if UNITY_EDITOR
            // the hangars, the tower and the field furniture are baked before Play by
            // Editor/AirportDemoAutoBake (a runtime class cannot call the editor
            // assembly, even under this #if)
            _rng = new System.Random(seed);
            _root = new GameObject("Airport").transform;
            _groundRoot = Root("Ground");
            _airsideRoot = Root("Airside");
            _markingRoot = Root("Markings");
            _lightRoot = Root("Airfield Lights");
            _apronRoot = Root("Apron");
            _buildingRoot = Root("Buildings");
            _fenceRoot = Root("Fence");
            _landsideRoot = Root("Landside");
            _detailRoot = Root("Detail");
            _floraRoot = Root("Flora");
            _liveRoot = Root("Live");

            LoadKit();
            BuildGround();          // grass, then every paved surface over it
            BuildRunway();
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
            BuildLight();
            BuildCamera();
            gameObject.AddComponent<CrewDemo.CrewDemoPace>();

            _clips = CrewKit.Clips();
            if (_clips.Walk == null || _clips.Idle == null)
                Debug.LogWarning("[AirportDemo] walk/idle clips missing under Assets/Animations/People - the people will slide.");

            BuildTaxiGraph();
            BuildFlights();
            BuildRotorcraft();
            BuildGroundOps();
            BuildLandsideTraffic();
            BuildPeople();
            BuildParkedAircraft();

            AssignCullLayers();
            OptimiseScene();
#else
            Debug.LogError("[AirportDemo] This demo loads Synty prefabs through the AssetDatabase and only runs in the editor.");
#endif
        }

        Transform Root(string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(_root, false);
            return t;
        }

        void Update()
        {
            if (!_merged) MergeStaticGeometry();
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            _flights?.Tick(dt);
            for (int i = 0; i < _rotors.Count; i++) _rotors[i].Tick(dt);
            _ground?.Tick(dt);
            _traffic?.Tick(dt);
            _people?.Tick(dt);
            for (int i = 0; i < _idlers.Count; i++) _idlers[i].Tick(dt);
        }

        void OnDestroy()
        {
            _people?.Dispose();
            _ground?.Dispose();
            for (int i = 0; i < _idlers.Count; i++) _idlers[i].Dispose();
        }

        // ------------------------------------------------------------ rng helpers

        float Rnd() => (float)_rng.NextDouble();
        float Rnd(float lo, float hi) => lo + (float)_rng.NextDouble() * (hi - lo);
        int Rnd(int n) => _rng.Next(n);
        bool Chance(float p) => _rng.NextDouble() < p;
        T Pick<T>(IList<T> list) => list == null || list.Count == 0 ? default : list[_rng.Next(list.Count)];

        // ------------------------------------------------------------ light, camera

        void BuildLight()
        {
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.28f;
            sun.color = new Color(1f, 0.96f, 0.88f);
            sun.shadows = LightShadows.Soft;
            // late afternoon out of the south-west: the hangar fronts and the terminal
            // glass are lit, and everything on the ramp throws a shadow across it
            sun.transform.rotation = Quaternion.Euler(46f, -35f, 0f);
            RenderSettings.sun = sun;
#if UNITY_EDITOR
            var sky = UnityEditor.AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
            if (sky != null) RenderSettings.skybox = sky;
#endif
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Skybox;
            RenderSettings.defaultReflectionResolution = 128;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.70f, 0.78f, 0.86f);
            RenderSettings.fogStartDistance = 900f;
            RenderSettings.fogEndDistance = 2600f;
            DynamicGI.UpdateEnvironment();
        }

        void BuildCamera()
        {
            var camGo = new GameObject("Demo Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 45f;
            // the far end of the runway is 750 m off and the circuit goes further
            cam.farClipPlane = 4000f;
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.backgroundColor = new Color(0.58f, 0.70f, 0.84f);
            var camData = cam.GetUniversalAdditionalCameraData();
            camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            camGo.AddComponent<AudioListener>();

            var dc = camGo.AddComponent<DemoCamera>();
            // over the ramp looking down the field: the terminal and the tower in the
            // middle distance, the runway beyond them, the hangars off to the left
            dc.pivot = new Vector3(-20f, 0f, 200f);
            dc.distance = 330f;
            dc.yaw = 12f;
            dc.pitch = 32f;
            dc.hintTopPx = 12f;
            dc.showHint = true;
            dc.hint = "WASD/arrows: move   Q/E or right-drag: rotate   wheel: zoom   " +
                      "Space: pause   , . : slower/faster";
            camGo.AddComponent<DemoShadows>().rig = dc;

            var dists = new float[32];
            dists[PropLayer] = PropCullDistance;
            dists[CrowdLayer] = CrowdCullDistance;
            dists[MidLayer] = MidCullDistance;
            cam.layerCullDistances = dists;
            cam.layerCullSpherical = true;
        }
    }
}
