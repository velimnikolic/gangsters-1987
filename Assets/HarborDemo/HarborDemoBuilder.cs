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
        [Range(1, 5)] public int berths = 3;
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
        [Range(0, 6)] public int lorries = 4;

        void Awake()
        {
#if UNITY_EDITOR
            // the ships and boxes are baked before Play by Editor/HarborDemoAutoBake
            var district = new HarborDistrict
            {
                berths = berths,
                berthPitch = berthPitch,
                apronDepth = apronDepth,
                stayRange = stayRange,
                gapRange = gapRange,
                sailSpeed = sailSpeed,
                passingTraffic = passingTraffic,
                quayCranes = quayCranes,
                shoreFoam = shoreFoam,
                shallowSand = shallowSand,
                dockWorkers = dockWorkers,
                shipCrew = shipCrew,
                forklifts = forklifts,
                deliveryTruck = deliveryTruck,
                lorries = lorries,
            };

            var host = gameObject.AddComponent<StandaloneDistrictHost>();
            // from over the water, low enough that the stacks and the sheds stand up
            // rather than lie flat as a plan
            host.cameraPivot = new Vector3(0f, 0f, 28f);
            host.cameraDistance = 125f;
            host.cameraYaw = 0f;
            host.cameraPitch = 36f;
            host.skyboxSky = false;                       // the port's plain sky
            host.clearColour = new Color(0.55f, 0.66f, 0.78f);
            host.sunAngles = new Vector3(50f, 20f, 0f);   // over the water
            host.sunIntensity = 1.25f;
            host.reflectionProbe = false;
            host.hint = "WASD/arrows: move   Q/E or right-drag: rotate   wheel: zoom   " +
                        "Space: pause   , . : slower/faster";
            host.HostSeeded(district, seed);
#else
            Debug.LogError("[HarborDemo] This demo loads Synty prefabs through the AssetDatabase and only runs in the editor.");
#endif
        }
    }
}
