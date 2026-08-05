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
                 "Streets reach the edge of this rectangle and stop there - there is no ring " +
                 "road round the outside, so the outer cells are built on. At the default 2-4 " +
                 "spacing about 45% of the map ends up as street, which puts 9 x 7 at around " +
                 "10 blocks.")]
        [Min(MinGridSize)] public int gridWidth = 9;
        [Min(MinGridSize)] public int gridHeight = 7;

        [Tooltip("Cells between two parallel streets. A gap of s leaves a block (s-1) cells " +
                 "wide, so at 30m cells the 2-4 default gives blocks anywhere from 30m to 90m " +
                 "on each axis. The layout is cut recursively rather than laid out as a grid, " +
                 "so a street stops instead of running the width of the map and the blocks do " +
                 "not line up into rows and columns; every size in the range appears, and the " +
                 "two axes of one block are drawn independently, so wide and narrow sit side " +
                 "by side. Setting both to the same value fixes the block size and brings back " +
                 "some of the regularity.")]
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

        [Header("Ground")]
        [Tooltip("Target side of one paving patch. Every lot and every alley of a block is cut " +
                 "into roughly square patches of about this size, and each rolls its own surface " +
                 "and shade - which is what stops a block reading as one flat rectangle of " +
                 "colour. This is the only dial on how many ground objects the city costs, and " +
                 "it is not linear: at 24 a 106m block comes out around 25 patches, at 12 nearer " +
                 "a hundred. Raise it if the ground object count becomes a problem.")]
        [Min(4f)] public float groundPatchSize = 24f;

        [Range(0f, 1f)]
        [Tooltip("Share of ground slabs that get a shade instead of the plain atlas one. Higher " +
                 "than buildingTintChance because a floor has no windows for the tint to reach - " +
                 "see BuildTintPalette on why ground shades may go far wider than facade ones. " +
                 "0 disables ground shading entirely and leaves every slab the pack's own colour.")]
        public float groundTintChance = 0.85f;

        [Tooltip("Paving joints, yard footpaths and faded repair patches, drawn as one flat mesh " +
                 "per block over the slabs. Off leaves the surfaces plain; the parking bay lines " +
                 "are separate and stay either way.")]
        public bool groundPaint = true;

        [Header("Block geometry")]
        [Tooltip("Distance kept clear between a block's street wall and the road centreline. " +
                 "BlockRect expands a block into its adjacent road tiles by (half a cell - this), " +
                 "so raising it pulls every facade back off the kerb at once.")]
        [Min(0f)] public float sidewalkWidth = 7f;

        [Tooltip("Service alley down the long axis of a block interior. Wide enough to read as a " +
                 "passage and to park a truck across, narrow enough that it never reads as a street.")]
        [Min(0f)] public float alleyWidth = 6f;

        [Tooltip("Separation between two touching facades in a terrace. This is NOT visual " +
                 "spacing - it exists only so coplanar walls do not z-fight, and so the " +
                 "occupancy test can tell 'flush neighbour' from 'genuine overlap'. " +
                 "Bounds.Intersects compares inclusively, so at exactly 0 a flush neighbour " +
                 "reports as a collision and is silently dropped. Do not set this to 0.")]
        [Min(0.01f)] public float partyWallGap = 0.05f;

        [Tooltip("Largest gap the run packer may open between two buildings when it spreads a " +
                 "run's leftover length. The leftover is divided equally across every joint " +
                 "rather than left as one hole at the end of the run - otherwise every block in " +
                 "the city has a visible notch at the same corner. Keep it small.")]
        [Min(0f)] public float maxFillerGap = 0.8f;

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

        [Tooltip("Overrides CarBehavior.maxspeed (prefab default 80-100 km/h). 0 keeps the prefab " +
                 "value. A tile is 30m, so 100 km/h is 27.8 m/s - a car crosses a whole city " +
                 "block in about a second, which leaves no room to react to anything and makes " +
                 "every junction a near miss. Lane limits on the road tiles run 30-80; this caps " +
                 "them all.")]
        [Min(0f)] public float carMaxSpeed = 45f;

        [Tooltip("Seconds of following distance a car keeps to the one in front. This is the " +
                 "time gap, so the metre gap grows with speed; the standstill gap is fixed " +
                 "separately in CarFollowing. Lower it for denser, more aggressive traffic - " +
                 "below about 0.5 cars tailgate hard enough that the hard clamp starts doing the " +
                 "braking instead of the model.")]
        [Min(0.2f)] public float carHeadway = Entities.CarFollowing.DefaultHeadway;

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
