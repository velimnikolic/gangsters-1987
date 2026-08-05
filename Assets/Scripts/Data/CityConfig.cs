using UnityEngine;

namespace LivingCity.Data
{
    [CreateAssetMenu(fileName = "CityConfig", menuName = "Living City/City Config")]
    public sealed class CityConfig : ScriptableObject
    {
        /// <summary>
        /// HumanBehavior.SetRandomDestination() only accepts a destination between 60m and
        /// 300m away, so the grid must be big enough that even the most central pedestrian
        /// has somewhere 60m off. At 30m per cell the worst case is the centre cell, whose
        /// farthest corner sits sqrt(2) * (N-1)/2 * 30 metres away:
        ///
        ///   N=3 -> 42m  (too small: pedestrians strand, and pre-patch this hung the Editor)
        ///   N=5 -> 85m  (safe)
        ///   N=7 -> 127m (comfortable, and clears CarBehavior's 90m default too)
        ///
        /// 5 is the hard floor; 7 is the first size where cars also work without lowering
        /// CarBehavior.minDistance. Do not lower this.
        /// </summary>
        public const int MinGridSize = 5;

        [Header("Layout")]
        [Tooltip("Size of the map in cells; cells are 30m, so 9 x 7 is 240 x 180 metres. " +
                 "Streets run from one edge of this rectangle to the other and stop there - " +
                 "there is no ring road round the outside, so the outer cells are built on. " +
                 "At the default 2-4 arterial spacing the block count is about " +
                 "((width+1)/3) x ((height+1)/3), which puts 9 x 7 at around 9 blocks.")]
        [Min(MinGridSize)] public int gridWidth = 9;
        [Min(MinGridSize)] public int gridHeight = 7;

        [Tooltip("Cells between arterial roads. A gap of s leaves a block (s-1) cells wide, so " +
                 "at 30m cells the 2-4 default gives blocks of 30m, 60m and 90m. Narrowing this " +
                 "range reduces how many distinct layouts a seed can produce - 3-4 on a 9x9 grid " +
                 "admits only one partition, making every seed identical.")]
        [Min(2)] public int minArterialSpacing = 2;
        [Min(2)] public int maxArterialSpacing = 4;

        [Header("Determinism")]
        [Tooltip("Same seed must always produce the same city. Each subsystem derives its own " +
                 "System.Random from this so that one subsystem's draw count cannot shift another's.")]
        public int seed = 42;

        [Header("Buildings")]
        [Range(0.3f, 1f)]
        [Tooltip("Chance that each slot along a block's street wall is actually built. " +
                 "Keep it high - a city block should read as a continuous terrace, and the " +
                 "occasional gap is for relief, not for density. 1 = completely unbroken.")]
        public float blockFillRatio = 0.92f;

        [Range(0f, 1f)]
        [Tooltip("Share of buildings that get a tint material instead of the plain atlas one. " +
                 "The untinted remainder is not waste - a street reads as varied because SOME " +
                 "facades differ, and leaving a third at the original palette keeps the pack's " +
                 "art present. 0 disables tinting entirely.")]
        public float buildingTintChance = 0.65f;

        [Header("Entities")]
        [Min(0)] public int carCount = 30;
        [Min(0)] public int pedestrianCount = 50;

        [Tooltip("Seconds between entity spawns. Entities only - road tiles must all be " +
                 "instantiated in one synchronous pass, never staggered.")]
        [Min(0f)] public float entitySpawnInterval = 0.1f;

        [Tooltip("How many random destinations a car serves before it is routed to a gap in the " +
                 "map's outline and driven off the map. Drawn per car, inclusive at both ends. " +
                 "Raising it makes traffic look more local and lowers the spawn churn; the " +
                 "trade is that a car lingers, and the population is finite, so at high values " +
                 "the same cars circulate all session. 0 sends a car straight out again, which " +
                 "reads as a motorway rather than a city.")]
        public Vector2Int wanderRoutesBeforeExit = new Vector2Int(1, 2);

        [Tooltip("Overrides CarBehavior.minDistance (prefab default 90m). 0 keeps the prefab " +
                 "value, which is correct at 7x7 and above. Below that, a car near the centre " +
                 "cannot find any tile 90m away and sits still logging " +
                 "'Target Tile not found farther then 90m' - lower this to about 50 there.")]
        [Min(0f)] public float carMinTravelDistance;

        [Header("Debug")]
        [Tooltip("Bypass ZonePlanner and stamp every block with Debug Zone. Isolates one " +
                 "palette's layout rules so a change to it shows on every block at once. " +
                 "Toggled from Tools/City; leave off for normal cities.")]
        public bool debugSingleZone;

        [Tooltip("The zone every block becomes while Debug Single Zone is on.")]
        public Generation.BlockZone debugZone = Generation.BlockZone.ResidentialHigh;

        public float WorldWidth => (gridWidth - 1) * Generation.CityGrid.CellSize;
        public float WorldHeight => (gridHeight - 1) * Generation.CityGrid.CellSize;

        void OnValidate()
        {
            gridWidth = Mathf.Max(MinGridSize, gridWidth);
            gridHeight = Mathf.Max(MinGridSize, gridHeight);
            maxArterialSpacing = Mathf.Max(minArterialSpacing, maxArterialSpacing);
        }
    }
}
