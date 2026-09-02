using RoadDemo;
using UnityEngine;

namespace BlockDemo
{
    // A quarter of the city, on its own.
    //
    // The city is ninety blocks, and opening it to look at one corner costs a minute
    // of building and a frame time to match. This scene stands up a HANDFUL: a few
    // block columns by a few block rows, with the streets between them, the avenues
    // that cross them, the junctions, the pavements and enough life to see it move -
    // which is what a quarter is (in the city proper, whatever lies between two seams).
    //
    // Nothing here draws a block: it is RoadDemoBuilder itself, the city's own
    // builder, handed a small grid. The pad sizes, the bakes, the floors, the kerb
    // dressing, the doors and the crowd are the same code that lays them in
    // Game.unity, so what is fixed here is fixed there - the same bargain the port
    // and the suburb scenes make (one object, two hosts).
    //
    // Change a knob, press Play again: a quarter is up in a few seconds.
    public class BlockDemoBuilder : MonoBehaviour
    {
        [Header("The quarter")]
        [Tooltip("Block columns, west to east. 3 gives four road lines with three " +
                 "interiors between them.")]
        [Min(1)] public int columns = 3;
        [Tooltip("Block rows, south to north.")]
        [Min(1)] public int rows = 3;
        [Tooltip("Deal the sizes below across the columns and rows the way the city " +
                 "does, so no two blocks beside each other come out the same. Off " +
                 "takes them in palette order, which is the plainest grid they allow.")]
        public bool randomiseSizes = true;
        [Tooltip("Same seed, same quarter.")]
        public int spacingSeed = 7;
        [Tooltip("Kept the same list as the city's, so a size means the same pad here. " +
                 "Only sizes with a lot pad in the catalog scene belong here.")]
        public float[] blockWidths = { 70f, 85f, 100f };
        public float[] blockDepths = { 50f, 70f, 95f };

        [Header("The avenues")]
        [Tooltip("Which of the north-south road lines are boulevards rather than " +
                 "ordinary streets, counted from the west (0 is the west edge line).")]
        public int[] avenuesNorthSouth = { 1 };
        [Tooltip("The same for the east-west lines, counted from the south.")]
        public int[] avenuesEastWest = new int[0];

        [Header("Which blocks stand")]
        [Tooltip("Which of the blocks filed under a pad code the quarter starts handing " +
                 "out at. 0 is the city's own order; step it to walk the whole catalog " +
                 "past the same lots.")]
        [Min(0)] public int blockCycle = 0;

        [Header("Life")]
        [Tooltip("Cars on the streets. The city runs about seventy over twelve " +
                 "blocks; this is deliberately lighter, because a quarter is looked at " +
                 "rather than played.")]
        public int carCount = 28;
        public int pedestrianCount = 70;
        [Range(0f, 1f)] public float insideAtStart = 0.35f;
        [Tooltip("The law on the street: a beat pair on every block from the first " +
                 "frame, plus the patrol car and the station pair where a block that " +
                 "lands here carries the police station.")]
        public bool police = true;

        [Header("The lab run")]
        [Tooltip("Rival mobs standing about the quarter for the outfit to go at. The city " +
                 "deals them the same way; here they are the run's targets.")]
        [Range(0, 3)] public int rivalCrews = 0;
        [Range(1, 4)] public int rivalHoods = 3;
        [Tooltip("Sim seconds after the quarter is up before the outfit gets in its car and " +
                 "is sent at the rivals, one after another, and parks when they are down. " +
                 "0: nobody is sent anywhere - a normal Play session.")]
        public float missionAfter = 0f;
        [Tooltip("Seconds of drive-by passes at one crew before the men get out and " +
                 "finish it on foot.")]
        public float missionPasses = 45f;
        [Tooltip("The mission on foot: no car at all. The crew walks the length of the " +
                 "quarter to the mob furthest from it - over the lots, across the roads, " +
                 "never mind the lights - and has it out with them there.")]
        public bool missionOnFoot;

        [Tooltip("The walkabout mission: no fight, no car - the crew walks corner to " +
                 "corner of the quarter down the pavements and through the lights, and " +
                 "the crew audit judges the walk itself.")]
        public bool missionWalk;
        [Tooltip("Walkabout: corners to walk (0 leaves the mission's default 3). A bigger " +
                 "quarter has longer legs, so fewer of them still fills a run.")]
        [Min(0)] public int missionWalkLegs = 0;
        [Tooltip("Walkabout: the HARD ceiling on one leg in seconds (0 leaves the default " +
                 "600). A leg on a 4x quarter is ~900 m and takes far longer than the " +
                 "small quarter it was tuned on - raise this to judge the big scene fairly.")]
        [Min(0)] public float missionLegPatience = 0f;
        [Tooltip("The nerve lever: chance a man shot to his last hit breaks and runs. " +
                 "Below 0 leaves the game's own figure (0.4). The brawl soak turns it " +
                 "up so runners appear every run.")]
        public float panicChance = -1f;

        [Tooltip("The bomb mission: the outfit's crew is stocked with grenades and made " +
                 "to throw a few at a rival crew and then lay a charge under a car and " +
                 "drive it off. The throw must thin the rival; the planted charge must " +
                 "spring when the car moves and tear it to scrap.")]
        public bool missionBomb;
        [Tooltip("Bomb mission: grenades thrown at the rival before the plant test.")]
        [Min(1)] public int missionBombThrows = 3;
        [Tooltip("Bomb mission: swing the camera onto the action (burning shop, blown " +
                 "car) so a headless --shot frames it. For grabbing pictures, off otherwise.")]
        public bool missionBombShot;

        [Tooltip("THE CAR BOMB: the crew walks up to a car belonging to a rival crew, lays " +
                 "a charge under it, walks clear, and the rival is then sent for his own " +
                 "car - he gets in, drives off, and it blows under him. Overrides every " +
                 "other mission.")]
        public bool missionCarBomb;
        [Tooltip("Car bomb: metres the crew walks away from the charge before the rival is " +
                 "sent for his car. Must clear the blast (6 m) and the range a rival opens " +
                 "fire at (24 m).")]
        [Min(10f)] public float missionCarBombClearBy = 45f;
        [Tooltip("Car bomb: seconds any one leg may take before the run fails.")]
        [Min(10f)] public float missionCarBombPatience = 90f;
        [Tooltip("Car bomb: seconds to let the rest of the rival's crew climb in after the " +
                 "first man is seated, before the car is driven off.")]
        [Min(0f)] public float missionCarBombSettle = 8f;

        [Tooltip("The mission on two wheels: no car, no march. The outfit's crew sends " +
                 "two men on the motorcycle the ledger bought it, one pass at a rival " +
                 "and home again, over and over. Needs outfitMotorcycle set - a run " +
                 "with no machine on the street fails on purpose.")]
        public bool missionMoto;
        [Tooltip("The car mission with a roadblock: the hunted mob is marched into the " +
                 "carriageway in front of the outfit's car every few seconds, which is " +
                 "the scene the run-down exists for and the one a quarter does not make " +
                 "by itself.")]
        public bool missionRoadblock;
        [Tooltip("Two wheels: how many passes to ride before the run is done.")]
        [Min(1)] public int missionPassesRidden = 3;
        [Tooltip("Two wheels: force one of the four endings on every pass rather than " +
                 "waiting for the quarter to deal it - 2 the man on the back is shot, " +
                 "3 the rider is shot, 4 the tank catches. 0 rides them as they fall. " +
                 "See BlockDemoMission.forceAct.")]
        [Range(0, 4)] public int missionMotoAct;
        [Tooltip("A motorcycle bought off the armory counter and signed for by the " +
                 "outfit's first lieutenant, by listing name: Motorbike, Moped, " +
                 "Scooter. Empty buys none.")]
        public string outfitMotorcycle = "";

        [Header("The outfit")]
        [Tooltip("Crews of the outfit sent out, one lieutenant each - the ledger is " +
                 "rewritten to match before anybody stands up. 0 leaves the seeded " +
                 "roster alone (one lieutenant and his two hoods).")]
        [Min(0)] public int outfitLieutenants = 0;
        [Tooltip("Hoods behind each of those lieutenants. 0 sends them out alone.")]
        [Min(0)] public int outfitHoods = 0;
        [Tooltip("Every man on the street - ours off the armory counter, theirs off the " +
                 "same ladder - carrying his own piece instead of a crew holding five " +
                 "copies of one gun.")]
        public bool mixedArms = false;

        [Header("Day")]
        [Range(0f, 24f)] public float startHour = 11f;
        [Tooltip("Real seconds per game hour; 15 runs a day in six minutes.")]
        public float realSecondsPerGameHour = 15f;

        [Header("Round the quarter")]
        [Tooltip("Metres of wild ground from the outer kerbs to the water. The quarter " +
                 "stands on its own islet - the same island code the city uses, so the " +
                 "grass, the beach and the sea meet the pavement the way they really do.")]
        public float greenBelt = 80f;

        void Awake()
        {
#if UNITY_EDITOR
            int nv = Mathf.Max(1, columns) + 1;
            int nh = Mathf.Max(1, rows) + 1;

            var vBlvd = RoadDemoBuilder.Avenues(nv, avenuesNorthSouth);
            var hBlvd = RoadDemoBuilder.Avenues(nh, avenuesEastWest);
            // The spacing is the city's own arithmetic - half a carriageway, the
            // pavement, the interior, the pavement, half a carriageway - so every
            // interior lands on a pad size the catalog has blocks for. Written out here
            // in full because the quarter must hit the pads with the randomiser OFF
            // too; with it on, RoadDemoBuilder.Respace deals the palette again and
            // shuffles it, exactly as it does for the city.
            var vx = RoadDemoBuilder.Centrelines(nv, vBlvd, blockWidths);
            var hz = RoadDemoBuilder.Centrelines(nh, hBlvd, blockDepths);

            // built on a GameObject that is switched off, so every field below is set
            // before RoadDemoBuilder.Awake reads them
            var go = new GameObject("City (one quarter)");
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
            // a quarter is what lies BETWEEN the seams: no river, no park, no wild strip
            city.seams = new Seam[0];
            // and no closed streets and no zoning either, for the same reason the seams
            // are gone: this is a lab, and the thing under test is the crew, the car or
            // the mission - not the plan. Every junction here is a crossroads and every
            // interior a block, so a run's route is the same route every time and a
            // fault in the tally is the crew's rather than the surveyor's. The city
            // itself carries both (RoadDemoBuilder.Closes.cs, .Zones.cs).
            city.closeStreets = false;
            city.zoneCity = false;
            // and the lab lays its OWN grid, so nothing may roll one over it: the streets
            // and the seeds set here are the experiment's settings, not a starting point
            city.rollCityPlan = false;
            city.rollCityEachPlay = false;
            // and no port and no suburbs either - those are quarters of their own, and
            // they have scenes of their own
            city.rollDistricts = false;
            city.harborDistrict = false;
            city.airportDistrict = false;
            city.districts = new DistrictSlot[0];
            // nor the belt freeway round the island: there is no island here
            city.beltFreeway = false;

            city.carCount = carCount;
            city.pedestrianCount = pedestrianCount;
            city.insideAtStart = insideAtStart;
            city.policeCarCount = police ? 1 : 0;
            city.policeOfficerCount = police ? 2 : 0;   // the station pair, two abreast
            // and a pair on EVERY block of the lab - the quarter is small and the
            // point is to look at them
            city.policeBeatPairs = police ? columns * rows : 0;
            city.rivalCrewsInCity = rivalCrews;
            city.rivalHoodsInCity = rivalHoods;
            // the counts above are what this quarter wants; the city's scaling is for a city
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

            go.SetActive(true);   // and the quarter is built

            // the books first: the men who will be stood up are the men the ledger
            // says the outfit has, so the run's outfit is written before it deals
            if (outfitLieutenants > 0 || !string.IsNullOrEmpty(outfitMotorcycle))
            {
                var books = gameObject.AddComponent<BlockDemoOutfit>();
                books.lieutenants = outfitLieutenants;
                books.hoodsEach = outfitHoods;
                books.mixedArms = mixedArms;
                books.armsSeed = spacingSeed;
                books.motorcycle = outfitMotorcycle;
            }

            if (missionAfter > 0f)
            {
                var mission = gameObject.AddComponent<BlockDemoMission>();
                mission.startAfter = missionAfter;
                mission.passesBefore = missionPasses;
                mission.onFoot = missionOnFoot;
                mission.walkabout = missionWalk;
                mission.panic = panicChance;
                mission.motoDriveBy = missionMoto;
                mission.roadblock = missionRoadblock;
                mission.passes = missionPassesRidden;
                mission.forceAct = missionMotoAct;
                mission.bombRun = missionBomb;
                mission.bombThrows = missionBombThrows;
                mission.bombShotCam = missionBombShot;
                mission.carBombRun = missionCarBomb;
                mission.carBombClearBy = missionCarBombClearBy;
                mission.carBombPatience = missionCarBombPatience;
                mission.carBombSettle = missionCarBombSettle;
                if (missionWalkLegs > 0) mission.walkLegs = missionWalkLegs;
                if (missionLegPatience > 0f) mission.legPatience = missionLegPatience;
            }

            FrameTheQuarter(city);
            Report(city);
#else
            Debug.LogError("[BlockDemo] This scene loads Synty prefabs through the AssetDatabase and only runs in the editor.");
#endif
        }

        // High enough to hold the whole quarter, low enough that the frontages read.
        void FrameTheQuarter(RoadDemoBuilder city)
        {
            var rig = FindFirstObjectByType<DemoCamera>();
            if (rig == null) return;
            // the grid as it ended up: Respace may have re-dealt every size
            float across = city.verticalRoadX[city.verticalRoadX.Length - 1];
            float deep = city.horizontalRoadZ[city.horizontalRoadZ.Length - 1];
            rig.FrameSpan(Mathf.Max(across, deep), fill: 0.95f);
            rig.pitch = 45f;
            rig.yaw = 30f;
            rig.showHint = true;
            rig.hint = "WASD/arrows: move   Q/E or right-drag: rotate   wheel: zoom   " +
                       "click a building: card   O: the lot plan   Space: pause   , . : slower/faster";
        }

        // What actually landed, on the console: every lot, its pad and its block, so
        // stepping blockCycle reads as a list rather than a guess.
        void Report(RoadDemoBuilder city)
        {
            if (city.LotPlans == null || city.LotPlans.Count == 0)
            {
                Debug.LogWarning("[BlockDemo] no lots were planned - check the sizes against the palettes.");
                return;
            }
            var said = new System.Text.StringBuilder();
            foreach (var plan in city.LotPlans)
                said.Append($"\n    {plan.Code ?? "(no pad)"} {plan.Width:F0}x{plan.Depth:F0}: {plan.Contents}");
            Debug.Log($"[BlockDemo] {city.LotPlans.Count} blocks (seed {spacingSeed}, " +
                      $"from block {blockCycle}):{said}");
        }
    }
}
