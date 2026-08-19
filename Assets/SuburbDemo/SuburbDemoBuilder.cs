using RoadDemo;
using UnityEngine;

namespace SuburbDemo
{
    // The suburb's own scene: one component that stands the district up at the origin
    // and hands it a StandaloneDistrictHost for the sun, the camera, the pause keys
    // and the perf pass. The suburb ITSELF is SuburbDistrict, the same object the city
    // builds on its edge (RoadDemoBuilder.Districts.cs) - so anything changed here, in
    // this scene, is what the city gets.
    //
    // The fields below are the district's own defaults, out on the inspector for
    // trying things: they are copied onto the district before it is planned.
    public class SuburbDemoBuilder : MonoBehaviour
    {
        [Header("Plan")]
        public int seed = 1987;
        [Min(1)] public int columns = 4;
        [Min(1)] public int rows = 4;
        [Tooltip("Block lengths (metres, multiples of 5) dealt across the columns - short, the Town demo's way: three to five houses a side.")]
        public int[] blockLengths = { 70, 85, 100 };
        [Tooltip("Lot depth, metres; a block is two lot rows back to back. 25 m: the demo's 3.5 m setback, the house, a 5-10 m yard.")]
        public float lotDepth = 25f;
        [Tooltip("How many interior street lines end in a cul-de-sac instead of crossing a block.")]
        public int culDeSacs = 4;
        [Tooltip("Carriageway from the junction to the turning bulb, metres.")]
        public float stubLength = 20f;
        [Tooltip("Share of lots that keep a front fence or hedge.")]
        public float fencedFronts = 0.7f;
        [Tooltip("Scales every tree chance - yards, leftovers, the park, the edge of the grid.")]
        public float treeDensity = 1f;

        [Header("Life")]
        public int carCount = 14;
        public int pedestrianCount = 40;
        [Range(0f, 1f)] public float insideAtStart = 0.4f;
        public int yardIdlers = 6;
        public float streetSpeed = 9f;
        [Range(0f, 1f)] public float enterChance = 0.35f;
        public Vector2 insideSeconds = new Vector2(20f, 90f);
        public Vector2 chatSeconds = new Vector2(6f, 14f);

        [Header("Debug")]
        [Tooltip("A red marker on every house's doorstep - check the preset front table in TownKit on the first Play.")]
        public bool debugFronts = false;

        void Awake()
        {
#if UNITY_EDITOR
            var district = new SuburbDistrict
            {
                columns = columns,
                rows = rows,
                blockLengths = blockLengths,
                lotDepth = lotDepth,
                culDeSacs = culDeSacs,
                stubLength = stubLength,
                fencedFronts = fencedFronts,
                treeDensity = treeDensity,
                carCount = carCount,
                pedestrianCount = pedestrianCount,
                insideAtStart = insideAtStart,
                yardIdlers = yardIdlers,
                streetSpeed = streetSpeed,
                enterChance = enterChance,
                insideSeconds = insideSeconds,
                chatSeconds = chatSeconds,
                debugFronts = debugFronts,
            };

            var host = gameObject.AddComponent<StandaloneDistrictHost>();
            host.cameraDistance = 180f;
            host.cameraYaw = 20f;
            host.cameraPitch = 50f;
            host.chatSeconds = chatSeconds;
            host.skyboxSky = true;      // the Town demo's own sky
            host.hint = "WASD/arrows: move   Q/E or right-drag: rotate   wheel: zoom   " +
                        "click a house: card   Space: pause   , . : slower/faster";
            host.HostSeeded(district, seed);
#else
            Debug.LogError("[SuburbDemo] This demo loads Synty prefabs through the AssetDatabase and only runs in the editor.");
#endif
        }
    }
}
