using RoadDemo;
using UnityEngine;

namespace FreewayDemo
{
    // Two quarters a long way apart, and the motorway between them.
    //
    // A city block is a place you can walk across. Two of them six hundred metres
    // apart are not: what joins them is a road nobody stops on - up a ramp out of one
    // quarter, along a deck standing nine metres over open ground, through the toll
    // plaza, down the ramp into the other. That is the whole of what this scene is
    // for, and everything in it is the city's own: RoadDemoBuilder lays the two
    // quarters exactly as it lays Game.unity's, and the freeway between them is
    // RoadDemoBuilder.Freeway.cs, which the city can switch on when it wants one.
    //
    //          W quarter                gap                    E quarter
    //     +----+----+            (a wild strip, 600 m)       +----+----+
    //     |    |    |                                        |    |    |
    //     +----o----+                                        +----o----+
    //          |  the link road out of the quarter                |
    //   =======^================ toll ==========================^=======  the deck
    //
    // Nothing here carries behaviour: the scene sets fields and the city builds.
    public class FreewayDemoBuilder : MonoBehaviour
    {
        [Header("The two quarters")]
        [Tooltip("Block columns in EACH quarter. Two gives three road lines with the " +
                 "middle one carrying the interchange.")]
        [Min(2)] public int columns = 2;
        [Tooltip("Block rows in each quarter.")]
        [Min(1)] public int rows = 2;
        [Tooltip("Block width, metres - the interior between two north-south lines.")]
        public float blockWidth = 85f;
        [Tooltip("Block depth, metres.")]
        public float blockDepth = 70f;
        [Tooltip("How far apart the two quarters stand: the open ground between the last " +
                 "street of one and the first of the other. This is the distance the " +
                 "freeway exists to cover, and it is a wild strip - nothing grows on the " +
                 "corridor itself.")]
        public float quartersApart = 600f;
        [Tooltip("Same seed, same two quarters. soak.sh steps it.")]
        public int spacingSeed = 7;
        [Tooltip("Which of the blocks filed under a pad code the quarters start handing out at.")]
        [Min(0)] public int blockCycle = 0;

        [Header("The freeway")]
        [Tooltip("How far north of the last street the deck's centre line stands: the " +
                 "ramps come down into this ground and the link roads cross it.")]
        public float freewayOff = 120f;
        [Tooltip("The deck's height over the road level. Nine metres is a lorry and a " +
                 "hand's breadth.")]
        public float deckHeight = 9f;
        [Tooltip("A toll plaza on the mainline, half way between the two interchanges.")]
        public bool tollPlaza = true;
        [Tooltip("Seconds a driver takes to pay, once he has stopped at the window.")]
        public float tollDwell = 2.2f;

        [Header("Life")]
        public int carCount = 30;
        public int pedestrianCount = 40;
        [Range(0f, 1f)] public float insideAtStart = 0.35f;
        [Range(0f, 24f)] public float startHour = 11f;
        public float realSecondsPerGameHour = 15f;

        void Awake()
        {
#if UNITY_EDITOR
            int perQuarter = Mathf.Max(2, columns) + 1;         // road lines in one quarter
            int nv = perQuarter * 2;
            int nh = Mathf.Max(1, rows) + 1;

            // the north-south lines: the west quarter, the gap, the east quarter. The
            // middle line of each quarter is a boulevard, and it is the one the ramps
            // come down to - a road that can take what leaves a motorway.
            var vBlvd = new bool[nv];
            vBlvd[perQuarter / 2] = true;
            vBlvd[perQuarter + perQuarter / 2] = true;
            var vGaps = new float[nv];                          // the interior after each line
            for (int k = 0; k + 1 < nv; k++) vGaps[k] = blockWidth;
            vGaps[perQuarter - 1] = Mathf.Max(200f, quartersApart);
            var vx = Centrelines(vBlvd, vGaps);

            var hBlvd = new bool[nh];
            var hGaps = new float[nh];
            for (int k = 0; k + 1 < nh; k++) hGaps[k] = blockDepth;
            var hz = Centrelines(hBlvd, hGaps);

            // where the freeway stands: north of the last street, clear of it
            float gridNorth = hz[nh - 1] + RoadDemoBuilder.RoadHalf(hBlvd[nh - 1]) + RoadDemoBuilder.PavementWidth;
            float across = gridNorth + Mathf.Max(60f, freewayOff);

            var go = new GameObject("City (two quarters and a motorway)");
            go.SetActive(false);
            var city = go.AddComponent<RoadDemoBuilder>();

            city.verticalRoadX = vx;
            city.verticalIsBoulevard = vBlvd;
            city.horizontalRoadZ = hz;
            city.horizontalIsBoulevard = hBlvd;
            city.blockWidths = new[] { blockWidth };
            city.blockDepths = new[] { blockDepth };
            city.randomiseBlockSizes = false;    // the gap is a size of its own: it must not be re-dealt
            city.spacingSeed = spacingSeed;
            city.blockCycle = Mathf.Max(0, blockCycle);

            // the ground between the quarters is not a block: it is the country the
            // motorway crosses, and the streets of neither quarter run into it
            city.seams = new[]
            {
                new Seam { vertical = true, gap = perQuarter - 1, kind = SeamKind.Wild, width = Mathf.Max(200f, quartersApart) },
            };
            city.closeStreets = false;
            city.zoneCity = false;
            city.rollCityPlan = false;
            city.rollCityEachPlay = false;
            city.rollDistricts = false;
            city.harborDistrict = false;
            city.airportDistrict = false;
            city.districts = new DistrictSlot[0];
            city.beltFreeway = false;

            // and the road this scene is about
            city.freewayRoute = new FreewayRoute
            {
                on = true,
                alongZ = false,
                across = across,
                deckY = Mathf.Max(6f, deckHeight),
                interchanges = new[] { perQuarter / 2, perQuarter + perQuarter / 2 },
                tollPlaza = tollPlaza,
                tollDwell = tollDwell,
            };

            city.carCount = carCount;
            city.pedestrianCount = pedestrianCount;
            city.insideAtStart = insideAtStart;
            city.policeCarCount = 0;
            city.policeOfficerCount = 0;
            city.rivalCrewsInCity = 0;
            city.scaleLifeToCity = false;
            city.updateProfile = false;
            city.startHour = startHour;
            city.realSecondsPerGameHour = realSecondsPerGameHour;

            // the green belt has to hold the whole road: the deck stands past the last
            // street at both ends, and the ramps come down north of the grid
            float east = vx[nv - 1];
            float margin = Mathf.Max(200f, freewayOff + 140f);
            city.islandWest = margin;
            city.islandEast = margin;
            city.islandNorth = Mathf.Max(margin, freewayOff + 120f);
            city.islandSouth = 120f;
            city.coastWander = 40f;
            city.treesPerHectare = 10f;

            go.SetActive(true);   // and the road is built

            Frame(city, east, across);
#else
            Debug.LogError("[FreewayDemo] This demo loads Synty prefabs through the AssetDatabase and only runs in the editor.");
#endif
        }

        /// <summary>Road centrelines for one axis: the interior after each line, with
        /// the pavements and half a carriageway either side of it. RoadDemoBuilder's own
        /// arithmetic (PlanLine), written out here because this scene deals the sizes
        /// itself - one of the gaps is six hundred metres of country.</summary>
        static float[] Centrelines(bool[] boulevard, float[] interiors)
        {
            var at = new float[boulevard.Length];
            float pave = RoadDemoBuilder.PavementWidth;
            for (int k = 0; k + 1 < boulevard.Length; k++)
                at[k + 1] = at[k] + RoadDemoBuilder.RoadHalf(boulevard[k]) + pave +
                            interiors[k] + pave + RoadDemoBuilder.RoadHalf(boulevard[k + 1]);
            return at;
        }

        // Both quarters and the road between them, from above.
        void Frame(RoadDemoBuilder city, float east, float across)
        {
            var rig = FindFirstObjectByType<DemoCamera>();
            if (rig == null) return;
            rig.pivot = new Vector3(east * 0.5f, 0f, across * 0.45f);
            rig.FrameSpan(east + 200f, 0.85f, 200f);
            rig.pitch = 46f;
            rig.yaw = 22f;
            rig.showHint = true;
            rig.hint = "WASD/arrows: move   Q/E or right-drag: rotate   wheel: zoom   " +
                       "Space: pause   , . : slower/faster";
        }
    }
}
