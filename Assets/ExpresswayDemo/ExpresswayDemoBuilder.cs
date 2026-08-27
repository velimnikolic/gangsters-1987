using RoadDemo;
using UnityEngine;

namespace ExpresswayDemo
{
    // A city with a motorway round it.
    //
    // The grid is the city's own - RoadDemoBuilder lays it exactly as it lays
    // Game.unity's - and the expressway is RoadDemoBuilder.Expressway.cs, which the
    // city will be able to roll for itself. This scene only says where it runs.
    //
    //        branch to the airport
    //            ||
    //            ||   EXIT 2 (diamond)          EXIT 1 (branch T to the port)
    //   =========/\=======||========================||=======\
    //   |                 ||                        ||        \
    //   |   +----+----+---++---+----+----+----+-----++--+      |  the band, 120 m out
    //   |   |    |    |   ||   |    |    |    |     ||  |      |
    //   |   +----+----+---++---+----+----+----+-----++--+      /  and down into town
    //   |         the grid                                    /
    //   +----------------------------------------------------+
    //
    // Nothing here carries behaviour: the scene sets fields and the city builds.
    public class ExpresswayDemoBuilder : MonoBehaviour
    {
        [Header("The city")]
        [Tooltip("Roll a different city every Play - grid size, block sizes, avenues, " +
                 "rivers - and let the motorway pick its own line off whatever came out. " +
                 "A road with its lines typed into a scene is a road for one city only.")]
        public bool rollCity = true;
        [Tooltip("Build the motorway at all. Off, this is the plain city the road is " +
                 "measured against - which is the only way to tell a jam the road caused " +
                 "from one the city has anyway.")]
        public bool expressway = true;
        [Tooltip("The city's own number. Above nought it pins the city, so the SAME " +
                 "town can be played with the motorway and without it - which is the " +
                 "only way to tell a jam the road caused from one the city has anyway. " +
                 "Nought: a different town every Play.")]
        public int citySeed = 0;
        [Tooltip("The city when it is NOT rolled: north-south road lines. The motorway " +
                 "wants a long side to run down.")]
        [Min(6)] public int columns = 16;
        [Tooltip("East-west road lines.")]
        [Min(3)] public int rows = 5;
        public float blockWidth = 85f;
        public float blockDepth = 70f;
        [Tooltip("Same seed, same city. soak.sh steps it.")]
        public int spacingSeed = 7;

        [Header("The motorway")]
        [Tooltip("How far outside the city's kerb the trunk runs.")]
        public float bandOffset = 120f;
        [Tooltip("The radius of its corners. 200 m is 45 mph; 260 m is 50.")]
        public float cornerRadius = 200f;
        public float deckHeight = 7f;
        [Tooltip("Which north-south line the far branch runs up, which ones carry a " +
                 "diamond, and which carries the T to the second branch.")]
        public int branchLine = 0;
        public int[] diamondLines = { 4 };
        public int branchTLine = 12;
        [Tooltip("Which east-west line the road comes down on to and dies in.")]
        public int terminusLine = 1;

        [Header("Dressing")]
        public bool guideSigns = true;
        public bool lamps = true;
        public bool billboards = true;
        public bool underDeck = true;
        [Min(0)] public int vagrants = 22;

        [Header("Life")]
        public int carCount = 260;
        public int pedestrianCount = 120;
        public int policeCarCount = 4;
        [Range(0f, 1f)] public float insideAtStart = 0.3f;
        [Range(0f, 24f)] public float startHour = 11f;
        public float realSecondsPerGameHour = 20f;

        void Awake()
        {
#if UNITY_EDITOR
            int nv = Mathf.Max(6, columns);
            int nh = Mathf.Max(3, rows);

            var vBlvd = new bool[nv];
            var hBlvd = new bool[nh];
            // the lines the ramps land on carry the traffic of a whole interchange:
            // they are avenues, not streets
            void Avenue(int i) { if (i >= 0 && i < nv) vBlvd[i] = true; }
            foreach (int i in diamondLines) Avenue(i);
            Avenue(branchTLine);
            Avenue(branchLine);
            if (terminusLine >= 0 && terminusLine < nh) hBlvd[terminusLine] = true;

            var vGaps = new float[nv];
            for (int k = 0; k + 1 < nv; k++) vGaps[k] = blockWidth;
            var hGaps = new float[nh];
            for (int k = 0; k + 1 < nh; k++) hGaps[k] = blockDepth;

            // the city's own spacing arithmetic, dealt across the gaps this scene names
            var vx = RoadDemoBuilder.Centrelines(nv, vBlvd, vGaps);
            var hz = RoadDemoBuilder.Centrelines(nh, hBlvd, hGaps);

            var go = new GameObject("City (with a motorway round it)");
            go.SetActive(false);
            var city = go.AddComponent<RoadDemoBuilder>();

            city.verticalRoadX = vx;
            city.verticalIsBoulevard = vBlvd;
            city.horizontalRoadZ = hz;
            city.horizontalIsBoulevard = hBlvd;
            city.blockWidths = new[] { blockWidth };
            city.blockDepths = new[] { blockDepth };
            city.randomiseBlockSizes = false;
            city.spacingSeed = spacingSeed;

            // no seams, no districts, no belt: the motorway is the only thing outside
            // the grid, and everything about the scene is about it
            city.seams = new Seam[0];
            city.closeStreets = false;
            city.zoneCity = true;
            city.rollCityPlan = rollCity;
            city.rollCityEachPlay = rollCity && citySeed <= 0;
            if (citySeed > 0) city.citySeed = citySeed;
            if (rollCity)
            {
                // the city deals its own lines, block sizes and avenues; what is set
                // above is only what it falls back on
                city.randomiseBlockSizes = true;
                city.blockWidths = new[] { 70f, 85f, 100f };
                city.blockDepths = new[] { 50f, 70f, 95f };
            }
            city.rollDistricts = false;
            city.harborDistrict = false;
            city.airportDistrict = false;
            city.districts = new DistrictSlot[0];
            city.beltFreeway = false;
            city.freewayRoute = new FreewayRoute { on = false };

            city.expressway = new ExpresswayRoute
            {
                on = expressway,
                bandOffset = bandOffset,
                cornerRadius = cornerRadius,
                deckY = deckHeight,
                roll = rollCity,
                branchLine = branchLine,
                branchName = "AIRPORT",
                diamonds = diamondLines,
                trumpetLine = branchTLine,
                trumpetRun = 260f,
                trumpetName = "PORT",
                terminusLine = terminusLine,
                guideSigns = guideSigns,
                lamps = lamps,
                billboards = billboards,
                underDeck = underDeck,
                vagrants = vagrants,
            };

            city.carCount = carCount;
            city.pedestrianCount = pedestrianCount;
            city.policeCarCount = policeCarCount;
            city.policeOfficerCount = 0;
            city.rivalCrewsInCity = 0;
            city.scaleLifeToCity = false;
            city.updateProfile = false;
            city.insideAtStart = insideAtStart;
            city.startHour = startHour;
            city.realSecondsPerGameHour = realSecondsPerGameHour;

            // the island has to hold the whole road: the branch runs a long way north of
            // the grid and the band stands off every side of it
            float north = bandOffset + 430f + 220f;
            city.islandWest = Mathf.Max(220f, bandOffset + 120f);
            city.islandEast = Mathf.Max(300f, bandOffset + 200f);
            city.islandNorth = north;
            city.islandSouth = 200f;
            city.coastWander = 60f;
            city.treesPerHectare = 8f;

            go.SetActive(true);   // and the city, and the road, are built

            // framed off the city that was actually built, not the one this scene
            // would have built for itself
            var built = city.verticalRoadX;
            var builtZ = city.horizontalRoadZ;
            Frame(built[built.Length - 1] * 0.45f, builtZ[builtZ.Length - 1] + bandOffset + 60f);
#else
            Debug.LogError("[ExpresswayDemo] This demo loads Synty prefabs through the AssetDatabase and only runs in the editor.");
#endif
        }

        // The city and the road round it, from above.
        void Frame(float atX, float atZ)
        {
            var rig = FindFirstObjectByType<DemoCamera>();
            if (rig == null) return;
            // looking down the motorway, which is what the scene is for
            rig.pivot = new Vector3(atX, 0f, atZ);
            rig.distance = 430f;
            rig.pitch = 34f;
            rig.yaw = 152f;
            rig.showHint = true;
            rig.hint = "WASD/arrows: move   Q/E or right-drag: rotate   wheel: zoom   " +
                       "Space: pause   , . : slower/faster";
        }
    }
}
