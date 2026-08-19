using RoadDemo;
using UnityEngine;

namespace BlockDemo
{
    // One block of the city, on its own.
    //
    // The city is ninety blocks, and opening it to look at one of them costs a minute
    // of building and a frame time to match. This scene stands up exactly ONE: the
    // interior, the four streets round it with their pavements, the four junctions
    // that close them, and enough life to see it move. Nothing here draws a block:
    // it is RoadDemoBuilder itself, the city's own builder, handed a grid of two road
    // lines by two - so the pad, the bake, the floor, the kerb dressing and the doors
    // are the same code that lays them in Game.unity, and what is fixed here is fixed
    // there (the same bargain the port and the suburb scenes make: one object, two
    // hosts).
    //
    // The knobs are what a block IS: which pad size it stands on, which of the blocks
    // filed for that pad, and whether an avenue or an ordinary street runs down its
    // west and south sides. Change them and press Play again - a one-block city is up
    // in a couple of seconds.
    public class BlockDemoBuilder : MonoBehaviour
    {
        [Header("The lot")]
        [Tooltip("The catalog's pad code: a letter for the width column (A/B/C = the " +
                 "first/second/third entry of the widths below) and a number for the " +
                 "depth row. B2 is the 85 x 70 m pad.")]
        public string lot = "B2";
        [Tooltip("Which block filed under that pad stands here. 0 is the one the city " +
                 "puts down first; step it to see the next composed block or rolled " +
                 "stock for the same rectangle.")]
        [Min(0)] public int block = 0;
        [Tooltip("Kept the same list as the city's, so a pad code means the same size here.")]
        public float[] blockWidths = { 70f, 85f, 100f };
        public float[] blockDepths = { 50f, 70f, 95f };

        [Header("The streets round it")]
        [Tooltip("A boulevard down the west side instead of an ordinary street - the " +
                 "wider carriageway, the planted median, the longer crossings.")]
        public bool boulevardWest = false;
        [Tooltip("The same for the south side.")]
        public bool boulevardSouth = false;

        [Header("Life")]
        [Tooltip("Cars on the four streets. They are what the kerb strips and the " +
                 "crossings are for, so a handful is worth having.")]
        public int carCount = 6;
        public int pedestrianCount = 16;
        [Range(0f, 1f)] public float insideAtStart = 0.3f;
        [Tooltip("A patrol car and an officer on the beat - only if the block that " +
                 "lands here carries the police station, since the patrols dock at its forecourt.")]
        public bool police = false;

        [Header("Day")]
        [Range(0f, 24f)] public float startHour = 11f;
        [Tooltip("Real seconds per game hour; 15 runs a day in six minutes.")]
        public float realSecondsPerGameHour = 15f;

        [Header("Round the block")]
        [Tooltip("Metres of wild ground from the outer kerbs to the water. The block " +
                 "stands on its own islet - the same island code the city uses, so the " +
                 "grass, the beach and the sea meet the pavement the way they really do.")]
        public float greenBelt = 70f;

        void Awake()
        {
#if UNITY_EDITOR
            float width = Pick(blockWidths, Column(), 85f);
            float depth = Pick(blockDepths, Row(), 70f);

            // The grid: two lines each way, one interior between them. The spacing is
            // the city's own arithmetic - half a carriageway, the pavement, the
            // interior, the pavement, half a carriageway - so the block comes out at
            // exactly the pad size and the kit tiles it the way it tiles the city.
            float pave = RoadDemoBuilder.PavementWidth;
            float x1 = RoadDemoBuilder.RoadHalf(boulevardWest) + pave + width + pave +
                       RoadDemoBuilder.RoadHalf(false);
            float z1 = RoadDemoBuilder.RoadHalf(boulevardSouth) + pave + depth + pave +
                       RoadDemoBuilder.RoadHalf(false);

            // built on a GameObject that is switched off, so every field below is set
            // before RoadDemoBuilder.Awake reads them
            var go = new GameObject("City (one block)");
            go.SetActive(false);
            var city = go.AddComponent<RoadDemoBuilder>();

            city.verticalRoadX = new[] { 0f, x1 };
            city.verticalIsBoulevard = new[] { boulevardWest, false };
            city.horizontalRoadZ = new[] { 0f, z1 };
            city.horizontalIsBoulevard = new[] { boulevardSouth, false };
            city.blockWidths = blockWidths;
            city.blockDepths = blockDepths;
            city.blockCycle = Mathf.Max(0, block);
            // the spacing is authored above, exactly; nothing is re-rolled
            city.randomiseBlockSizes = false;
            city.seams = new Seam[0];
            // no port, no suburbs: a block is not a quarter
            city.rollDistricts = false;
            city.harborDistrict = false;
            city.districts = new DistrictSlot[0];

            city.carCount = carCount;
            city.pedestrianCount = pedestrianCount;
            city.insideAtStart = insideAtStart;
            city.policeCarCount = police ? 1 : 0;
            city.policeOfficerCount = police ? 1 : 0;
            city.rivalCrewsInCity = 0;
            city.rivalHoodsInCity = 0;
            // the counts above are what one block wants; the city's scaling is for a city
            city.scaleLifeToCity = false;
            city.updateProfile = false;

            city.startHour = startHour;
            city.realSecondsPerGameHour = realSecondsPerGameHour;

            float belt = Mathf.Max(20f, greenBelt);
            city.islandWest = belt;
            city.islandEast = belt;
            city.islandNorth = belt;
            city.islandSouth = belt;
            city.coastWander = belt * 0.3f;
            city.treesPerHectare = 14f;

            go.SetActive(true);   // and the city - all one block of it - is built

            FrameTheBlock(width, depth);
            Report(city, width, depth);
#else
            Debug.LogError("[BlockDemo] This scene loads Synty prefabs through the AssetDatabase and only runs in the editor.");
#endif
        }

        // Close enough that the shopfronts read, high enough to see over the terrace.
        void FrameTheBlock(float width, float depth)
        {
            var rig = FindFirstObjectByType<DemoCamera>();
            if (rig == null) return;
            rig.distance = Mathf.Max(90f, 1.35f * Mathf.Max(width, depth));
            rig.pitch = 42f;
            rig.yaw = 30f;
            rig.showHint = true;
            rig.hint = "WASD/arrows: move   Q/E or right-drag: rotate   wheel: zoom   " +
                       "click a building: card   O: the lot plan   Space: pause   , . : slower/faster";
        }

        // What actually landed, on the console: the pad, the size and the block's name,
        // so stepping the block number reads as a list rather than a guess.
        void Report(RoadDemoBuilder city, float width, float depth)
        {
            if (city.LotPlans == null || city.LotPlans.Count == 0)
            {
                Debug.LogWarning("[BlockDemo] no lot was planned - check the pad code against the palettes.");
                return;
            }
            var plan = city.LotPlans[0];
            Debug.Log($"[BlockDemo] lot {plan.Code ?? "(no pad)"} - {width:F0} x {depth:F0} m, " +
                      $"block {block}: {plan.Contents}");
        }

        int Column()
        {
            var code = (lot ?? "").Trim().ToUpperInvariant();
            return code.Length > 0 ? code[0] - 'A' : 1;
        }

        int Row()
        {
            var code = (lot ?? "").Trim();
            return code.Length > 1 ? code[1] - '1' : 1;
        }

        static float Pick(float[] palette, int at, float fallback)
        {
            if (palette == null || palette.Length == 0) return fallback;
            return palette[Mathf.Clamp(at, 0, palette.Length - 1)];
        }
    }
}
