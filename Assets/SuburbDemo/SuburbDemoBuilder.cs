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
        [Tooltip("Size: blocks' worth across (~90 m each) and deep (~70 m each).")]
        [Min(1)] public int columns = 9;
        [Min(1)] public int rows = 9;
        [Tooltip("How far the suburb's outline wobbles in from its ellipse: 0 a plain ellipse, 0.3 a proper potato.")]
        [Range(0f, 0.5f)] public float outlineWobble = 0.3f;
        [Tooltip("Lot depth, metres; a block is two lot rows back to back. 25 m: the demo's 3.5 m setback, the house, a 5-10 m yard.")]
        public float lotDepth = 25f;
        [Tooltip("Streets are grown until this share of the map is within a lot's depth of a pavement; the rest stays woods behind the back fences.")]
        [Range(0.5f, 1f)] public float streetCoverage = 0.85f;
        [Tooltip("How readily the growth makes a crossroads (a fourth arm), against 1 for a plain T.")]
        [Range(0f, 1f)] public float fourWayWeight = 0.05f;
        [Tooltip("How readily a street closes a loop onto another, against 1 for growing on.")]
        [Range(0f, 1f)] public float loopWeight = 0.3f;
        [Tooltip("Cul-de-sacs with a turning bulb (a soft budget: a dead end that can go nowhere else still gets one).")]
        public int culDeSacs = 20;
        [Tooltip("A few more little blocks: spurs pushed into the pockets the growth left.")]
        public int extraBlocks = 8;
        [Tooltip("Least carriageway from the junction to the turning bulb, metres.")]
        public float stubLengthMin = 25f;
        [Tooltip("The lie of the land: 0 flat, 1 gentle waves and hillocks of a couple of metres (slopes of a few per cent), more for more.")]
        [Range(0f, 2f)] public float relief = 1f;
        [Tooltip("Share of lots that keep a front fence or hedge.")]
        public float fencedFronts = 0.7f;
        [Tooltip("Share of back yards that get a pool - the pack's in-ground, above-ground or paddling one.")]
        [Range(0f, 1f)] public float poolChance = 0.45f;
        [Tooltip("Scales every tree chance - yards, leftovers, the park, the edge of the grid.")]
        public float treeDensity = 1f;

        [Header("Life")]
        public int carCount = 28;
        public int pedestrianCount = 110;
        [Range(0f, 1f)] public float insideAtStart = 0.4f;
        public int yardIdlers = 16;
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
                outlineWobble = outlineWobble,
                lotDepth = lotDepth,
                streetCoverage = streetCoverage,
                fourWayWeight = fourWayWeight,
                loopWeight = loopWeight,
                culDeSacs = culDeSacs,
                extraBlocks = extraBlocks,
                stubLengthMin = stubLengthMin,
                relief = relief,
                fencedFronts = fencedFronts,
                poolChance = poolChance,
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
            host.cameraDistance = 260f;
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
