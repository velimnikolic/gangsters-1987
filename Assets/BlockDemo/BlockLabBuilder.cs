using RoadDemo;
using UnityEngine;

namespace BlockDemo
{
    // The block laboratory: a dozen blocks and nothing else.
    //
    // BlockDemoBuilder stands up a quarter to run a MISSION in - crews, rivals,
    // drive-bys, the bomb run - and most of its knobs are that run's. This scene is
    // the other half of the bargain: no mission, no rivals, no outfit, just the
    // blocks themselves and enough traffic and crowd to tell whether an interior
    // reads. It exists because the thing under test here is what is INSIDE a block -
    // the alley through it, the parking in front of it and behind it, the ground
    // under both - and that wants a scene you can put up in seconds and look at.
    //
    // Nothing here draws a block, exactly as in BlockDemoBuilder: it is
    // RoadDemoBuilder, the city's own builder, handed a small grid. Whatever is
    // fixed in this scene is fixed in Game.unity, because it is the same code.
    // This class may set fields. It may not instantiate anything.
    public class BlockLabBuilder : MonoBehaviour
    {
        [Header("The lab")]
        [Tooltip("Block columns, west to east. 4 gives five road lines with four " +
                 "interiors between them.")]
        [Min(1)] public int columns = 4;
        [Tooltip("Block rows, south to north.")]
        [Min(1)] public int rows = 3;
        [Tooltip("Deal the sizes below across the columns and rows the way the city " +
                 "does, so no two blocks beside each other come out the same.")]
        public bool randomiseSizes = true;
        [Tooltip("Same seed, same lab.")]
        public int spacingSeed = 7;
        [Tooltip("The lab's own palette. It carries SMALL sizes the city's does not, " +
                 "because a cramped lot is the case the interior passes are most " +
                 "likely to get wrong; a size with no lot pad in the catalog takes no " +
                 "bake and falls through to the small-lot content instead.")]
        public float[] blockWidths = { 70f, 85f, 100f };
        public float[] blockDepths = { 50f, 70f, 95f };

        [Header("The avenues")]
        [Tooltip("Which of the north-south road lines are boulevards rather than " +
                 "ordinary streets, counted from the west (0 is the west edge line).")]
        public int[] avenuesNorthSouth = { 1 };
        [Tooltip("The same for the east-west lines, counted from the south.")]
        public int[] avenuesEastWest = new int[0];

        [Header("Which blocks stand")]
        [Tooltip("Which of the blocks filed under a pad code the lab starts handing " +
                 "out at. Step it to walk the whole catalog past the same lots.")]
        [Min(0)] public int blockCycle = 0;

        [Header("Life")]
        [Tooltip("Cars on the streets. Light on purpose: the lab is looked at, not played.")]
        public int carCount = 24;
        public int pedestrianCount = 60;
        [Range(0f, 1f)] public float insideAtStart = 0.35f;

        [Header("The look")]
        [Tooltip("Whose demo scene the grade, the haze and the sun copy. PolygonCity " +
                 "is what this lab is for: its interiors are judged against that " +
                 "pack's own Demo scene, and half of what a screenshot shows is grade.")]
        public DemoGrade.Look look = DemoGrade.Look.PolygonCity;
        [Tooltip("PolygonCity's own demo sun: pitch 50, yaw 212 (their quaternion " +
                 "reads -147.77 the other way round), intensity 1.5, shadows 0.8.")]
        public Vector3 sunAngles = new Vector3(50f, 212f, 0f);
        public float sunIntensity = 1.5f;
        [Range(0f, 1f)] public float sunShadowStrength = 0.8f;
        [Tooltip("Linear haze, start and end in metres. PolygonCity's own demo runs " +
                 "50 -> 400 and that band of haze is a large part of why its pictures " +
                 "read as depth - but it is measured from a lens IN THE STREET. This " +
                 "camera opens 165 m up looking across four blocks, so at their range " +
                 "everything past the near kerb goes white and the interiors - the " +
                 "whole point of the lab - cannot be read at all. Off by default here; " +
                 "turn it on when the camera comes down to the pavement, and set it on " +
                 "the CITY (RoadDemoBuilder.linearHaze) for a whole-town haze.")]
        public Vector2 linearHaze = Vector2.zero;

        [Header("Day")]
        [Range(0f, 24f)] public float startHour = 11f;
        [Tooltip("Real seconds per game hour; 15 runs a day in six minutes.")]
        public float realSecondsPerGameHour = 15f;

        [Header("Round the lab")]
        [Tooltip("Metres of wild ground from the outer kerbs to the water.")]
        public float greenBelt = 80f;

        void Awake()
        {
#if UNITY_EDITOR
            int nv = Mathf.Max(1, columns) + 1;
            int nh = Mathf.Max(1, rows) + 1;

            var vBlvd = RoadDemoBuilder.Avenues(nv, avenuesNorthSouth);
            var hBlvd = RoadDemoBuilder.Avenues(nh, avenuesEastWest);
            // the city's own spacing arithmetic, so an interior lands on a pad size
            // the catalog has blocks for even with the randomiser off
            var vx = RoadDemoBuilder.Centrelines(nv, vBlvd, blockWidths);
            var hz = RoadDemoBuilder.Centrelines(nh, hBlvd, blockDepths);

            // built on a GameObject that is switched off, so every field below is set
            // before RoadDemoBuilder.Awake reads them
            var go = new GameObject("City (block lab)");
            go.SetActive(false);
            var city = go.AddComponent<RoadDemoBuilder>();

            city.verticalRoadX = vx;
            city.verticalIsBoulevard = vBlvd;
            city.horizontalRoadZ = hz;
            city.horizontalIsBoulevard = hBlvd;
            city.blockWidths = blockWidths;
            city.blockDepths = blockDepths;
            city.randomiseBlockSizes = randomiseSizes;
            city.spacingSeed = spacingSeed;
            city.blockCycle = Mathf.Max(0, blockCycle);
            // a lab is what lies BETWEEN the seams: no river, no park, no wild strip
            city.seams = new Seam[0];
            // and no closed streets and no zoning: every junction here is a crossroads
            // and every interior a block, so what an interior pass does to one lot is
            // legible against the lot beside it
            city.closeStreets = false;
            city.zoneCity = false;
            // the lab lays its OWN grid - nothing may roll one over it
            city.rollCityPlan = false;
            city.rollCityEachPlay = false;
            city.rollDistricts = false;
            city.harborDistrict = false;
            city.airportDistrict = false;
            city.districts = new DistrictSlot[0];
            city.beltFreeway = false;

            city.carCount = carCount;
            city.pedestrianCount = pedestrianCount;
            city.insideAtStart = insideAtStart;
            city.policeCarCount = 0;
            city.policeOfficerCount = 0;
            city.rivalCrewsInCity = 0;
            // the counts above are what this lab wants; the city's scaling is for a city
            city.scaleLifeToCity = false;
            city.updateProfile = false;

            city.look = look;
            city.sunAngles = sunAngles;
            city.sunIntensity = sunIntensity;
            city.sunShadowStrength = sunShadowStrength;
            city.linearHaze = linearHaze;

            city.startHour = startHour;
            city.realSecondsPerGameHour = realSecondsPerGameHour;

            float belt = Mathf.Max(20f, greenBelt);
            city.islandWest = belt;
            city.islandEast = belt;
            city.islandNorth = belt;
            city.islandSouth = belt;
            city.coastWander = belt * 0.3f;
            city.treesPerHectare = 14f;

            go.SetActive(true);   // and the lab is built

            FrameTheLab(city);
            Report(city);
#else
            Debug.LogError("[BlockLab] This scene loads Synty prefabs through the AssetDatabase and only runs in the editor.");
#endif
        }

        // High enough to hold the lab, low enough that an interior reads: the whole
        // point is seeing what is inside a block, so this sits lower than the quarter's.
        void FrameTheLab(RoadDemoBuilder city)
        {
            var rig = FindFirstObjectByType<DemoCamera>();
            if (rig == null) return;
            float across = city.verticalRoadX[city.verticalRoadX.Length - 1];
            float deep = city.horizontalRoadZ[city.horizontalRoadZ.Length - 1];
            rig.FrameSpan(Mathf.Max(across, deep));
            rig.pitch = 48f;
            rig.yaw = 30f;
            rig.showHint = true;
            rig.hint = "WASD/arrows: move   Q/E or right-drag: rotate   wheel: zoom   " +
                       "click a building: card   O: the lot plan   Space: pause   , . : slower/faster";
        }

        // What actually landed, on the console: every lot, its pad and its block.
        void Report(RoadDemoBuilder city)
        {
            if (city.LotPlans == null || city.LotPlans.Count == 0)
            {
                Debug.LogWarning("[BlockLab] no lots were planned - check the sizes against the palettes.");
                return;
            }
            var said = new System.Text.StringBuilder();
            foreach (var plan in city.LotPlans)
                said.Append($"\n    {plan.Code ?? "(no pad)"} {plan.Width:F0}x{plan.Depth:F0}: {plan.Contents}");
            Debug.Log($"[BlockLab] {city.LotPlans.Count} blocks (seed {spacingSeed}, " +
                      $"from block {blockCycle}):{said}");
        }
    }
}
