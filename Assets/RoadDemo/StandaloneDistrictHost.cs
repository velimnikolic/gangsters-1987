using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace RoadDemo
{
    /// <summary>
    /// A scene with one district in it and nothing else: the port's own demo, a
    /// suburb's own demo. It is the other half of what the city does - the sun, the
    /// camera, the pause keys, the crowd's tick, the perf pass - so that the quarter
    /// itself is the SAME object in both, and what is changed in the demo is what the
    /// city gets (RoadDemoBuilder.Districts.cs is the city's half).
    ///
    /// The one thing it does not do is lay the ground: in the city the island rings
    /// every district, and in its own scene a district still builds the ground it
    /// always built (ProvidesGround is false here).
    /// </summary>
    public sealed class StandaloneDistrictHost : MonoBehaviour, IDistrictHost
    {
        [Header("Camera")]
        public float cameraDistance = 180f;
        public float cameraYaw = 20f;
        public float cameraPitch = 50f;
        public Vector3 cameraPivot;
        [Tooltip("The camera's far plane: a city block wants a kilometre and a half, an airfield " +
                 "with an aeroplane on final wants four.")]
        public float cameraFar = 1500f;
        public string hint = "WASD/arrows: move   Q/E or right-drag: rotate   wheel: zoom   " +
                             "Space: pause   , . : slower/faster";

        [Header("Sky and light")]
        [Tooltip("The Town demo's sky: Unity's default skybox, ambient and reflections off it. " +
                 "Off leaves a plain colour behind the district - the port's own look.")]
        public bool skyboxSky = true;
        public Color clearColour = new Color(0.62f, 0.74f, 0.85f);
        public Vector3 sunAngles = new Vector3(52f, 35f, 0f);
        public float sunIntensity = 1.3f;
        [Tooltip("Linear fog from x to y metres; zero for none. A field a mile long wants the far end to haze.")]
        public Vector2 fogRange = Vector2.zero;
        public Color fogColour = new Color(0.70f, 0.78f, 0.86f);

        [Tooltip("One realtime probe over the whole district, so glass and car paint " +
                 "carry the street and not only the sky.")]
        public bool reflectionProbe = true;

        readonly List<Transform> _static = new List<Transform>();
        readonly List<Transform> _live = new List<Transform>();
        readonly List<DemoVehicle> _vehicles = new List<DemoVehicle>();
        readonly List<CivilianAgent> _civilians = new List<CivilianAgent>();
        readonly List<PedestrianAgent> _walkers = new List<PedestrianAgent>();
        readonly List<TrafficSignal> _signals = new List<TrafficSignal>();
        readonly List<RoadEdge> _edges = new List<RoadEdge>();
        readonly List<PedLink> _pedLinks = new List<PedLink>();

        IDistrict _district;
        Transform _root;
        CityLife _life;
        PedClips _clips;
        bool _clipsMade;
        bool _merged;
        float _chatScan;
        SignalMaterials _signalMats;

        public Vector2 chatSeconds = new Vector2(6f, 14f);

        /// <summary>The district this scene is showing, once <see cref="Host"/> has run.</summary>
        public IDistrict District => _district;

        Transform Root => _root != null ? _root : (_root = new GameObject("District").transform);

        /// <summary>Build the district here, at the world origin, and put a scene round
        /// it. The caller is the demo scene's own thin component.</summary>
        public void Host(IDistrict district) => HostSeeded(district, 1987);

        /// <summary>The same, with the district's own seed - the demo scene's inspector
        /// value, so the quarter here is the quarter the city would build from it.</summary>
        public void HostSeeded(IDistrict district, int seed)
        {
            _district = district;
            district.Frame = DistrictFrame.Identity;
            district.Plan(null, seed);   // null: on its own, no streets to meet
            // on its own the quarter stands at the world origin, its own south-west
            // corner on (0, 0) - the frame only slides it there, it never turns it
            var b = district.LocalBounds;
            district.Frame = DistrictFrame.At(-b.xMin, -b.yMin, 0);
            district.Build(this);

            var bounds = district.Frame.ToWorldRect(district.LocalBounds);
            BuildLight();
            BuildCamera(bounds);
            // and the quarter's cars breathe here the way they do in the city: the rig
            // finds them itself off the road's own list of users (CarExhaust)
            CarExhaust.Install();
            if (reflectionProbe) BuildReflections(bounds);
            gameObject.AddComponent<CrewDemo.CrewDemoPace>();

            ScenePerf.Optimise(_static, null, district.Name);
            ScenePerf.AssignCullLayers(_static, district.Name);
        }

        void Update()
        {
            if (!_merged && _district != null)
            {
                _merged = true;
                var roots = new List<ScenePerf.MergeRoot>();
                for (int i = 0; i < _static.Count; i++) roots.Add(ScenePerf.MergeRoot.Of(_static[i], salt: i * 500));
                ScenePerf.Merge(roots, new GameObject("Merged").transform, _district.Name);
            }
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            for (int i = 0; i < _signals.Count; i++) _signals[i].UpdateBulbs(_signalMats);
            for (int i = 0; i < _vehicles.Count; i++) _vehicles[i].Tick(dt);
            for (int i = 0; i < _civilians.Count; i++) _civilians[i].TickCivilian(dt);
            CivilianAgent.TickCrowd(dt);
            for (int i = 0; i < _walkers.Count; i++) _walkers[i].Tick(dt);
            _district?.Tick(dt);

            _chatScan -= dt;
            if (_chatScan <= 0f && _life != null && _life.CanChat)
            {
                _chatScan = 1.5f;
                CivilianAgent.PairChats(_civilians, chatSeconds);
            }
        }

        void OnDestroy()
        {
            for (int i = 0; i < _civilians.Count; i++) _civilians[i].Dispose();
            for (int i = 0; i < _walkers.Count; i++) _walkers[i].Dispose();
            _district?.Dispose();
        }

        // ------------------------------------------------------------- the scene

        void BuildLight()
        {
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = sunIntensity;
            sun.color = new Color(1f, 0.96f, 0.88f);
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(sunAngles.x, sunAngles.y, sunAngles.z);
            RenderSettings.sun = sun;

            if (skyboxSky)
            {
#if UNITY_EDITOR
                var sky = UnityEditor.AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
                if (sky != null) RenderSettings.skybox = sky;
#endif
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
                RenderSettings.ambientIntensity = 1f;
                RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Skybox;
                RenderSettings.defaultReflectionResolution = 128;
                RenderSettings.reflectionIntensity = 1f;
            }
            else
            {
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.42f, 0.46f, 0.52f);
            }
            if (fogRange.y > fogRange.x && fogRange.y > 0f)
            {
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.Linear;
                RenderSettings.fogColor = fogColour;
                RenderSettings.fogStartDistance = fogRange.x;
                RenderSettings.fogEndDistance = fogRange.y;
            }
            DynamicGI.UpdateEnvironment();
        }

        void BuildCamera(Rect bounds)
        {
            var camGo = new GameObject("Demo Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 45f;
            cam.farClipPlane = Mathf.Max(300f, cameraFar);
            cam.clearFlags = skyboxSky ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
            cam.backgroundColor = clearColour;
            var camData = cam.GetUniversalAdditionalCameraData();
            camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            camGo.AddComponent<AudioListener>();

            var dc = camGo.AddComponent<DemoCamera>();
            dc.pivot = cameraPivot != Vector3.zero
                ? cameraPivot
                : new Vector3(bounds.center.x, 0f, bounds.center.y);
            dc.distance = cameraDistance;
            dc.yaw = cameraYaw;
            dc.pitch = cameraPitch;
            dc.hintTopPx = 12f;
            dc.showHint = true;
            dc.hint = hint;
            camGo.AddComponent<DemoShadows>().rig = dc;

            var picker = camGo.AddComponent<LivingCity.CameraRig.BuildingCardPicker>();
            picker.pickRoot = Root;

            ScenePerf.ApplyCullDistances(cam);
        }

        void BuildReflections(Rect bounds)
        {
            var go = new GameObject("Reflection Probe");
            go.transform.position = new Vector3(bounds.center.x, 6f, bounds.center.y);
            var probe = go.AddComponent<ReflectionProbe>();
            probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Realtime;
            probe.refreshMode = UnityEngine.Rendering.ReflectionProbeRefreshMode.ViaScripting;
            probe.timeSlicingMode = UnityEngine.Rendering.ReflectionProbeTimeSlicingMode.NoTimeSlicing;
            probe.size = new Vector3(Mathf.Abs(bounds.width) + 40f, 60f, Mathf.Abs(bounds.height) + 40f);
            probe.boxProjection = false;
            probe.resolution = 256;
            probe.cullingMask = ~(1 << ScenePerf.CrowdLayer);
            probe.farClipPlane = 800f;
            probe.RenderProbe();
        }

        // ------------------------------------------------------ IDistrictHost

        Transform IDistrictHost.StaticRoot(string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(Root, false);
            _static.Add(t);
            return t;
        }

        Transform IDistrictHost.LiveRoot(string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(Root, false);
            _live.Add(t);
            return t;
        }

        PedClips IDistrictHost.Clips
        {
            get
            {
                if (!_clipsMade) { _clipsMade = true; _clips = CrewKit.Clips(); }
                return _clips;
            }
        }

        bool IDistrictHost.ProvidesGround => false;

        // no island here, so no island green: the district paints its lawns out of
        // its own pack, the way its demo scene always looked
        Material IDistrictHost.GroundMaterial => null;

        CityLife IDistrictHost.Life => _life ?? (_life = new CityLife
        {
            CanSit = false,
            SitChance = 0f,
            CanChat = CrewKit.Clips().Talk != null,
        });

        void IDistrictHost.RegisterVehicle(DemoVehicle vehicle)
        {
            if (vehicle == null) return;
            _vehicles.Add(vehicle);
            StreetTraffic.Users.Add(vehicle);
        }

        void IDistrictHost.RegisterCivilian(CivilianAgent civilian)
        {
            if (civilian != null) _civilians.Add(civilian);
        }

        void IDistrictHost.RegisterWalker(PedestrianAgent walker)
        {
            if (walker != null) _walkers.Add(walker);
        }

        void IDistrictHost.RegisterSignal(TrafficSignal signal)
        {
            if (signal != null) _signals.Add(signal);
        }

        void IDistrictHost.RegisterRoads(IReadOnlyList<RoadEdge> edges)
        {
            if (edges == null) return;
            for (int i = 0; i < edges.Count; i++) if (edges[i] != null) _edges.Add(edges[i]);
        }

        void IDistrictHost.RegisterPavement(IReadOnlyList<PedLink> links)
        {
            if (links == null) return;
            for (int i = 0; i < links.Count; i++) if (links[i] != null) _pedLinks.Add(links[i]);
        }

        void IDistrictHost.Blocked(Bounds box) => WalkObstacles.Block(box);

        void IDistrictHost.Blocked(Bounds box, string what) => WalkObstacles.Block(box);

        void IDistrictHost.ReportMissing(string what)
            => Debug.LogError($"[{(_district != null ? _district.Name : "District")}] missing prefab: {what}");
    }
}
