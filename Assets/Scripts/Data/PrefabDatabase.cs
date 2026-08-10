using System;
using System.Collections.Generic;
using UnityEngine;
using LivingCity.Generation;

namespace LivingCity.Data
{
    /// <summary>
    /// Every prefab the generator can place. Populate with the "T" (atlas) variants only -
    /// they share a single material, which is the largest draw-call saving available to us.
    ///
    /// Note the package's prefab files carry no "_T" suffix; the T is on the folder
    /// (T/- Prefabs_T/...). Assign these in the inspector rather than loading by name.
    /// </summary>
    [CreateAssetMenu(fileName = "PrefabDatabase", menuName = "Living City/Prefab Database")]
    public sealed class PrefabDatabase : ScriptableObject
    {
        /// <summary>
        /// A named, weighted bucket of interchangeable prefabs.
        ///
        /// Weights exist because the source packs are folders, not catalogues: dropping a whole
        /// folder in and drawing uniformly gives an excavator the same odds as a saloon car.
        /// Grouping by role and weighting the groups is how the city gets a plausible mix.
        /// </summary>
        [Serializable]
        public class WeightedPrefabs
        {
            public string label;
            [Min(0f)] public float weight = 1f;

            public GameObject[] prefabs = Array.Empty<GameObject>();

            public bool IsUsable => weight > 0f && prefabs != null && prefabs.Length > 0;
        }

        /// <summary>
        /// How a group's pieces meet their neighbours along a lot edge.
        ///
        /// The distinction is not cosmetic. The terrace kits are modular parts authored to abut
        /// flush - the -front / -back / -corner / -short suffixes are the pieces of exactly one
        /// continuous wall. Everything else in the pack is a standalone model with its own
        /// setback baked in; butting those together leaves visible seams, and they have no
        /// corner variant because all four of their elevations are finished.
        /// </summary>
        public enum PieceLayout
        {
            Terrace,
            Detached,
        }

        /// <summary>
        /// One kit of interchangeable pieces, split by where each belongs in a terrace.
        ///
        /// The split exists so windows end up facing the street. The package's block prefabs
        /// have a detailed front elevation and a blank rear, so picking uniformly at random
        /// leaves blank walls fronting the pavement and shopfronts facing the alley.
        /// </summary>
        [Serializable]
        public sealed class WeightedGroup : WeightedPrefabs
        {
            [Tooltip("Alley-facing pieces (back). Falls back to the street pieces when empty, " +
                     "which is what happens for the 5-floor kit - it ships no -back variant.")]
            public GameObject[] rearPrefabs = Array.Empty<GameObject>();

            [Tooltip("Corner pieces, used where two runs meet. Terrace layouts only.")]
            public GameObject[] cornerPrefabs = Array.Empty<GameObject>();

            public PieceLayout layout = PieceLayout.Terrace;

            [Tooltip("Detached only: yard left between one building and the next. A range " +
                     "rather than a constant, because evenly spaced houses read as a fence.")]
            [Min(0f)] public float minGap;
            [Min(0f)] public float maxGap;

            [Tooltip("Detached only: how far a building may sit back off the street line.")]
            [Min(0f)] public float maxSetback;

            [Tooltip("Preferred for the slot right beside a street corner. The corner tavern " +
                     "and the ground-floor store are what keep a residential block from " +
                     "reading as barracks.")]
            public bool cornerPreferred;

            [Tooltip("Storefront group - cafes, restaurants, shops. The tinter paints these " +
                     "from the stronger commercialTints palette, so a shop reads as a painted " +
                     "shopfront among the flats instead of one more brown terrace piece.")]
            public bool commercial;

            /// <summary>Inherited <c>prefabs</c> holds the street-facing pieces - the detailed elevations.</summary>
            public GameObject[] PiecesFor(bool facesStreet) =>
                facesStreet || rearPrefabs == null || rearPrefabs.Length == 0
                    ? prefabs
                    : rearPrefabs;

            /// <summary>
            /// Whether this kit has pieces authored for an alley. Used to bias alley-facing runs
            /// toward the kits that actually have blank rears - now that several groups share a
            /// block, an unbiased draw would put shopfronts down the service alley.
            /// </summary>
            public bool HasRear => rearPrefabs != null && rearPrefabs.Length > 0;

            /// <summary>Yard after this piece. Always 0 for a terrace - the wall is continuous.</summary>
            public float GapFor(System.Random rng) =>
                layout == PieceLayout.Terrace || maxGap <= minGap
                    ? Mathf.Max(0f, minGap)
                    : minGap + (float)rng.NextDouble() * (maxGap - minGap);

            public float SetbackFor(System.Random rng) =>
                layout == PieceLayout.Terrace || maxSetback <= 0f
                    ? 0f
                    : (float)rng.NextDouble() * maxSetback;
        }

        /// <summary>
        /// Correction for prefabs authored off the pack's facade convention. BlockBuilder rotates
        /// local +Z toward the street, which is how the pack's own demo scene orients these
        /// buildings - but a few pieces put their windows elsewhere (the plain 5floor's facade is
        /// on -Z and its +Z is a blank wall), so each carries the yaw that puts its windows back
        /// on the pavement.
        ///
        /// For a CORNER piece the number means something different, and getting that wrong is
        /// what put a blank wall on one of every corner's two streets. A corner has two finished
        /// elevations, so there is no single "front" to point at a street; what the value aligns
        /// is the outer QUADRANT, in a convention rotated 45 degrees from the flat one. See
        /// BlockBuilder's corner loop for the derivation and CornerFacing for the measurement
        /// that produces it. Either way the only legal values are 0, 90, 180 and 270 - the pack
        /// is modelled square and anything else leaves a facade skew to its street.
        /// </summary>
        [Serializable]
        public sealed class FacadeYawFix
        {
            public GameObject prefab;
            public float extraYaw;
        }

        /// <summary>
        /// One chimney mouth on one prefab, in that prefab's local space, so a rotated or scaled
        /// instance carries its smoke with it.
        ///
        /// A prefab may have several: industry-factory is twin-stacked at local x = +/-4.22. And
        /// most of the works catalogue has NONE - industry-warehouse, -storage, -building and
        /// -factory-hall are topped by roof lanterns, which look like chimneys to any measurement
        /// that only asks "is this cluster narrow". The test that separates them is how far the
        /// cluster RISES above the roof around it; see ChimneyVents.
        /// </summary>
        [Serializable]
        public sealed class ChimneyVent
        {
            public GameObject prefab;
            public Vector3 local;
        }

        /// <summary>
        /// Everything one kind of block is built from: its buildings, its ground, its landmark
        /// and its litter.
        ///
        /// A palette holds SEVERAL groups on purpose. The previous design rolled one group per
        /// block, which is why a block came out uniformly 4-storey or uniformly 5-storey and
        /// every block in the city looked like one of two things. Here every group in the
        /// palette is live at once and the choice is made per slot, so one street wall carries
        /// a mix - and a low-weight shop group mixed into a residential palette is what puts
        /// the odd cafe on a housing street instead of quarantining cafes in their own zone.
        /// (Zones that instead want each street to read as one development set
        /// <see cref="uniformStreetRuns"/>, which lifts the roll from slot to run.)
        /// </summary>
        [Serializable]
        public sealed class ZonePalette
        {
            public BlockZone zone;

            [Tooltip("Relative odds of a block becoming this zone, before ZonePlanner's radial bias.")]
            [Min(0f)] public float weight = 1f;

            [Tooltip("Largest fraction of the city's blocks this zone may take. 1 = no cap. " +
                     "Use this for things a bigger city should simply get more of - parks, " +
                     "car parks, works.")]
            [Range(0f, 1f)] public float maxShare = 1f;

            [Tooltip("Hard cap in blocks regardless of city size. 0 = none. This is the right " +
                     "control for anything a city has ONE of - the hospital, the car park. A share " +
                     "cap cannot express that: 9% of a twelve-block map is one hospital, but 9% " +
                     "of a forty-block map is three. Note this is a CEILING and not a promise; " +
                     "see 'guaranteed' below for the promise.")]
            [Min(0)] public int maxBlocks;

            [Tooltip("Largest block this zone may take, in cells. 0 = any. The hospital is one " +
                     "landmark, a couple of outbuildings and a car bay - on a nine-cell block " +
                     "that dressing rattles around, so it is steered onto the small blocks " +
                     "instead. If a map has no block this small, the smallest it does have " +
                     "qualifies, so the zone can never be priced out entirely.")]
            [Min(0)] public int maxBlockCells;

            [Tooltip("Promise the city this zone rather than merely permitting it: if the " +
                     "weighted roll never lands on it, ZonePlanner's rescue pass takes the " +
                     "smallest block still going back from the residential fabric and gives it " +
                     "to this zone anyway. OFF for every palette in the shipped database, so " +
                     "the pass does nothing at all today and every zone is a ceiling - at most " +
                     "one hospital, at most one school - the way the post office and the fire " +
                     "station have always been.\n\n" +
                     "What turning it on costs, stated plainly, because it is not free: one " +
                     "block, taken out of ResidentialHigh or Industrial. On a nine-by-seven map " +
                     "that is about a tenth of the city. The block taken is the SMALLEST one " +
                     "still available, which is the right block for a one-cell civic landmark " +
                     "and the wrong one for anything that wants room. The pass also ignores " +
                     "maxBlockCells and overshoots ZonePlanner's shared landmark budget by one, " +
                     "both deliberately - see the comment on the pass itself.")]
            public bool guaranteed;

            [Tooltip("Only blocks touching the map boundary qualify for this zone. The port is " +
                     "why this exists: its water is laid OUTSIDE the grid, beyond the outline, " +
                     "so a port in the middle of the fabric would stand on a sea that is not " +
                     "there. Note the geometry makes this cheap to promise-by-weight - the BSP " +
                     "never cuts at the boundary, so the outer ring of cells is always block " +
                     "land and most blocks on a small map qualify.")]
            public bool requiresMapEdge;

            [Header("Ground")]
            [Tooltip("Slab under the block. Empty falls back to the shared concrete groundTile.")]
            public GameObject ground;

            [Tooltip("Weighted per-block slab choices, so a zone's blocks stop sharing one " +
                     "identical floor. Empty falls back to 'ground', then to the shared " +
                     "concrete groundTile. Only tiles WITHOUT a Tile component belong here - " +
                     "the slab is stretched, and scaling a Tile drags its path nodes off the " +
                     "30m grid.")]
            public WeightedPrefabs[] grounds = Array.Empty<WeightedPrefabs>();

            [Tooltip("Laid over the block's interior, inset by one building depth, so the yard " +
                     "a terrace ring encloses reads as dirt or grass instead of the same " +
                     "concrete as the street. Empty disables the treatment. Ignored for " +
                     "per-cell grounds (parks).")]
            public GameObject[] courtyardGrounds = Array.Empty<GameObject>();

            [Tooltip("Lay the ground prefab unscaled once per cell instead of stretching one " +
                     "slab over the block. Required for tile-park: it carries a Tile component " +
                     "whose sidewalk paths run to +/-15, and scaling it would drag those nodes " +
                     "off the 30m grid and break the link to the pavements.")]
            public bool groundIsTilePerCell;

            [Tooltip("Surface for the band between the road tile's kerb and this block's own " +
                     "ground - GroundPlacer's apron. Empty falls back to the shared concrete " +
                     "groundTile, which is what every built-up zone wants. A park sets grass " +
                     "here instead, so its lawn runs to the kerb rather than stopping 10m short " +
                     "behind a concrete ring. Only tiles WITHOUT a Tile component belong here: " +
                     "the apron is stretched over the whole rect.")]
            public GameObject apronGround;

            [Tooltip("Landform dropped on the lawn - tile-plain-hump is the pack's only centred " +
                     "mound, 30m across and 5.8m tall with all four edges flush, so scaled down " +
                     "it is a knoll that needs no skirt. Do NOT put the tile-hill pieces here: " +
                     "they rise at an EDGE and hang a 6m skirt below it, so one on its own leaves " +
                     "a cliff on three sides.")]
            public GameObject[] parkMounds = Array.Empty<GameObject>();

            [Range(0f, 1f)]
            [Tooltip("How often a yard patch rolls its own surface instead of repeating its " +
                     "lot's. 1 is a full mosaic - right for a works yard, where mixed concrete " +
                     "and dirt is what the place looks like. Lower it for a zone that should " +
                     "read as deliberately surfaced, and 0 leaves the yard one flat colour " +
                     "with only the shade varying.")]
            public float groundPatchChance = 1f;

            [Tooltip("Draw paving joints across the yards of this zone - the thin darker grid " +
                     "that turns a flat slab into a paved forecourt. Right where the ground is " +
                     "meant to be laid rather than poured, so it belongs downtown and behind a " +
                     "terrace, and not in a works yard or a suburban garden.")]
            public bool paveJoints;

            [Header("Buildings")]
            [Tooltip("Empty means no perimeter buildings at all - a park or a car park.")]
            public WeightedGroup[] groups = Array.Empty<WeightedGroup>();

            [Tooltip("Roll the building group once per street-facing run instead of per slot. " +
                     "A period terrace street was built as one development: a side is either " +
                     "a flush 4/5-storey wall, or a row of detached blocks, or shops - not a " +
                     "lottery of all three. Alley sides, corner retail and the neighbour " +
                     "bleed keep their per-slot rolls.")]
            public bool uniformStreetRuns;

            [Tooltip("Cap on Subdivide's columns AND rows. 0 = uncapped, which lets a big block " +
                     "break into two or three ringed lots with alleys between them - the way a " +
                     "big Chicago block reads as several small ones. 1 forces a single perimeter " +
                     "ring: every building fronts a street, and the block encloses one courtyard " +
                     "however large it is.")]
            [Min(0)] public int maxLotsPerAxis;

            [Tooltip("Cap on perimeter buildings per block, the landmark excluded. 0 = " +
                     "unlimited. This is what keeps a landmark block from ringing itself with " +
                     "outbuildings - the cap is spent in placement order, so the buildings " +
                     "cluster on the sides walked first, near the landmark's own.")]
            [Min(0)] public int maxPerimeterBuildings;

            [Tooltip("At most one per block, on the longest street run. This is what makes a " +
                     "block read as 'the hospital block' without filling it with hospitals.")]
            public GameObject[] landmarks = Array.Empty<GameObject>();
            [Range(0f, 1f)] public float landmarkChance;

            [Tooltip("Index into landmarks[] that ONE block of this zone is required to build, " +
                     "or -1 for none. landmarkChance is a probability and cannot express 'the " +
                     "city must end up with one of these': across half a dozen blocks it usually " +
                     "delivers and occasionally does not. ZonePlanner picks the block " +
                     "- the largest, since a required landmark is generally the one that needs " +
                     "room - and marks it, and BlockBuilder then skips both the chance roll and " +
                     "the draw from the bag for that block alone.")]
            public int requiredLandmark = -1;

            [Tooltip("Second landmark index this zone owes the city on EVERY seed, or -1 for " +
                     "none. Same forced-block mechanism as requiredLandmark, fulfilled after " +
                     "it so the older promise picks its host first - a zone can owe two " +
                     "different landmarks (ResidentialHigh owes both the bank and, now that " +
                     "the patrol fleet lives there, the police station) but each needs its " +
                     "own block, since a block holds at most one landmark.")]
            public int guaranteedLandmark = -1;

            [Tooltip("Blocks of city per copy of the guaranteed landmark, or 0 for exactly " +
                     "one per city. The police station is why this exists: one station was " +
                     "right for a ~18-block map and is a token on a ~170-block one, and a flat " +
                     "count would be wrong again on the next resize. ZonePlanner marks " +
                     "max(1, blockCount / this) hosts, farthest-spread so coverage follows " +
                     "the map rather than the flood fill's corner. Ignored when " +
                     "guaranteedLandmark is -1.")]
            [Min(0)] public int guaranteedLandmarkEvery;

            [Tooltip("Uniform scale on the landmark instance. The civic landmarks are the " +
                     "pack's biggest pieces and their block is already the smallest the map " +
                     "offers, so this is the one size lever left - 0.5 halves the building. " +
                     "Nothing uses it today: every landmark in the catalogue turned out to " +
                     "stand better at full size once its block stopped being oversized.")]
            [Min(0.1f)] public float landmarkScale = 1f;

            [Header("Yard")]
            [Tooltip("Dropped on whatever ground the lots left over - the alleys between them " +
                     "and the light-wells inside them - each turned so its front faces away " +
                     "from the nearest wall. Keep it light: bins, a dumpster, a bench, a lamp, " +
                     "a tree. Empty leaves the interior to Scatter instead.")]
            public GameObject[] alleyProps = Array.Empty<GameObject>();

            [Tooltip("Chance a slot on an ALLEY-facing run becomes a 12m parking bay instead of " +
                     "a building - the same idea as parkingChance, on the back elevation. Cars " +
                     "go here rather than in the alley itself, which is only 6m wide.")]
            [Range(0f, 1f)] public float alleyParkingChance = 0.15f;

            [Tooltip("Scattered across whatever the alley furniture leaves free - timber and " +
                     "brick stacks for a works yard, bins behind a terrace. NOT used by the Park " +
                     "zone: uniform noise is exactly what made the park read as a lawn with " +
                     "objects dropped on it. See the Park section below.")]
            public GameObject[] scatter = Array.Empty<GameObject>();
            [Range(0f, 1f)] public float scatterDensity;

            [Header("Park (groundIsTilePerCell zones only)")]
            [Tooltip("Trees planted in rows through the park quadrants. Weighted rather than " +
                     "flat because the species are not interchangeable in size: tree-lime is a " +
                     "7.07m crown against tree-poplar's 2.21m, so an even draw fills a 9.5m " +
                     "quadrant with three limes and nothing else fits. A row draws ONE group and " +
                     "plants the whole row from it - that is what makes an avenue read as planted " +
                     "rather than grown.")]
            public WeightedPrefabs[] parkTrees = Array.Empty<WeightedPrefabs>();

            [Tooltip("Low planting filling the lawn between the rows: shrubs, flowers, grass " +
                     "tufts. Drawn flat because at this size the pieces really are " +
                     "interchangeable, and placed in small clusters so it reads as beds rather " +
                     "than as noise. Anything here wants a footprint under about 2m.")]
            public GameObject[] parkUndergrowth = Array.Empty<GameObject>();

            [Tooltip("Seating set along the path legs and turned to face the path. Kept separate " +
                     "from the undergrowth because these are the only park pieces with a front - " +
                     "a bench at a random yaw in the middle of a lawn is the single clearest " +
                     "tell that a park was generated rather than laid out.")]
            public GameObject[] parkBenches = Array.Empty<GameObject>();

            [Tooltip("Lamps stood along the park walks, cast-iron height not carriageway " +
                     "height - the street's lamp-road-double is a 9.5m double-arm that reads " +
                     "as a road fixture on a garden path. Empty falls back to the shared " +
                     "streetLamps list.")]
            public GameObject[] parkLamps = Array.Empty<GameObject>();

            [Tooltip("Litter bins by the benches and the path junctions. One per two benches, " +
                     "capped by ParkConfig - a park is not a depot.")]
            public GameObject[] parkBins = Array.Empty<GameObject>();

            [Tooltip("The civic square's centre monument - the pack has no statue, so the " +
                     "standing-stone rock-pillar reads as the memorial obelisk.")]
            public GameObject[] parkMonuments = Array.Empty<GameObject>();

            [Tooltip("Boulders for the informal archetype's unkempt corners. Naturally small " +
                     "stones at authored scale, drawn from the shared atlas - never a scaled-up " +
                     "one-off in its own colour.")]
            public GameObject[] parkBoulders = Array.Empty<GameObject>();

            [Tooltip("Dead and bare trees, informal archetype only, capped by ParkConfig. Kept " +
                     "off the parkTrees buckets so a formal allee can never draw one.")]
            public GameObject[] parkDeadTrees = Array.Empty<GameObject>();

            [Tooltip("Water surface for the informal pond, stretched over the pond rect the way " +
                     "the apron is - so it must carry no Tile component.")]
            public GameObject parkWaterTile;

            [Tooltip("Piers flanking each hedge gate. The hedge has no fencePost of its own, " +
                     "which is why the old gates read as accidental holes.")]
            public GameObject parkGatePiers;

            [Tooltip("The rare period fairground piece - a carousel was a fixture of a 1920s " +
                     "American park - rolled by the informal archetype at ParkConfig's chance.")]
            public GameObject[] parkAmusement = Array.Empty<GameObject>();

            [Tooltip("Chance a street-facing slot becomes a surface car park instead of a building.")]
            [Range(0f, 1f)] public float parkingChance = 0.12f;

            [Header("Feature strip")]
            [Tooltip("Give up one street side of each block for a parking strip or pocket " +
                     "park, and pack the building rows tight against it. This is where the " +
                     "length the terrace kit cannot fill goes - see FeatureStrip. Only for " +
                     "zones whose runs should read as an unbroken street wall.")]
            public bool featureStrip;

            [Tooltip("The kiosk of a pocket-park strip, stood at the pavement facing the " +
                     "street - hot-dog-stand, marketplace-stand-simple. One per park, drawn " +
                     "flat. Empty leaves the park unattended.")]
            public GameObject[] kioskPrefabs = Array.Empty<GameObject>();

            [Tooltip("What fills a pocket-park strip besides the kiosk: benches, trees, a " +
                     "lamp. Placed on a coarse grid facing the street. Empty falls back to " +
                     "alleyProps, which carries the same furniture plus the bins.")]
            public GameObject[] pocketParkProps = Array.Empty<GameObject>();

            [Tooltip("Fill the whole block with rows of parked cars - the Parking zone.")]
            public bool carRows;

            [Header("Works yard (industrialYard zones only)")]
            [Tooltip("Lay the block out as a walled compound instead of a terrace: wall, one " +
                     "gate, service roads in from it, halls standing along them with their " +
                     "doors on the road, staff parking by the gate. Replaces the whole " +
                     "perimeter path AND the scatter for this zone - a works is arranged, and " +
                     "the uniform scatter is exactly what made the old industrial block read " +
                     "as tipped out rather than built.")]
            public bool industrialYard;

            [Tooltip("military-gate - stood in the gap the wall leaves. Empty leaves the way " +
                     "in as a plain opening, which reads fine; the gate is the flourish.")]
            public GameObject gatePrefab;

            [Tooltip("Stretched along each service road, so the carriageway reads as tarmac " +
                     "instead of as more yard. tile-plain_asphalt-nb - the no-border variant, " +
                     "because a kerb drawn round every road rectangle would tile visibly where " +
                     "two carriageways meet. Empty leaves the roads the colour of the yard.")]
            public GameObject serviceRoadGround;

            [Tooltip("tile-plain_concrete-nb - laid under the loading aprons, the truck staging " +
                     "and the boiler plot. The no-border variant for the same reason the service " +
                     "road uses one: a kerb drawn round every patch tiles visibly where two of " +
                     "them meet, and the yard is nothing but patches meeting.")]
            public GameObject yardConcrete;

            [Tooltip("tile-plain_dirt-nb - the bulk stockpiles, the scrap corner, and the worn " +
                     "ground between them. Empty leaves the whole yard the colour of its slab, " +
                     "which is the single flat plane this pass exists to break up.")]
            public GameObject yardDirt;

            [Tooltip("Material stacked in the gaps between halls: brick and plank stacks, " +
                     "cement bags, pipe. Placed in GROUPS against a hall wall rather than " +
                     "sprinkled - a works stores things where it uses them, and the same " +
                     "prefabs at uniform density across a yard is what 'nabacano' looks like.")]
            public GameObject[] stackProps = Array.Empty<GameObject>();

            [Tooltip("chimney-big, water-tower-medium - stood deliberately in the back yard of a " +
                     "hall rather than drawn from scatter at a random yaw. These are the pieces " +
                     "that give a works its skyline, and smoke hangs off the chimneys among them.")]
            public GameObject[] chimneyProps = Array.Empty<GameObject>();

            [Tooltip("The small stuff a works is actually made of besides its halls: the garage, " +
                     "the silo, the site hut, the long low store. Placed in the strip BEHIND each " +
                     "hall, which is where the row depth left over goes - a hall is 6 to 23m deep " +
                     "in a 26m row, so without this pass every works has a bare band down the " +
                     "back of every row. Keep these under about 14m on their long side or they " +
                     "will not fit that strip.")]
            public GameObject[] auxBuildings = Array.Empty<GameObject>();

            // The yard stock: what stands on the zones IndustrialLotPlanner cuts out of the
            // compound, read by IndustrialYardDresser.
            //
            // Four arrays named for what the thing IS, rather than six named for the zone kinds
            // they go on. The zone mapping is a switch in IndustrialYardDresser, for the same
            // reason IndustrialLotBuilder.SurfaceFor is a switch over yardConcrete/yardDirt
            // instead of six ground fields: the palette holds art, and the code holds the
            // decision about which art goes where, where it can carry its argument and be read
            // in one place. Keyed by zone, barrels would have to be listed three times over and
            // nothing would keep the copies in step.
            //
            // These are additions, not replacements. stackProps above is unchanged and is still
            // read by IndustrialDresser AND by the raw-material yards here - and its LENGTH must
            // stay unchanged, because BlockBuilder draws from one shared rng stream for the whole
            // city and ShuffleBag consumes n-1 draws to shuffle, so re-sizing an existing bag
            // relayouts every building placed after it.

            [Tooltip("Barrels. The stock that comes in RANKS, which is the strongest picture a " +
                     "works yard has, and why the raw-material zone is the one the planner lets " +
                     "repeat. Laid on a grid at a fixed pitch, never scattered.")]
            public GameObject[] yardBarrels = Array.Empty<GameObject>();

            [Tooltip("Crates, boxes and baskets - the square stock that stacks. Goes on the " +
                     "loading aprons, where it reads as goods waiting to go in or just come out.")]
            public GameObject[] yardCrates = Array.Empty<GameObject>();

            [Tooltip("Sacks: coal, cement, sand. The sagging shapes, which is what stops a yard " +
                     "of boxes and cylinders reading as a warehouse shelf.")]
            public GameObject[] yardSacks = Array.Empty<GameObject>();

            [Tooltip("The things that stand with their backs to something and never stack: " +
                     "shelving racks, lockers, cabinets, the ash can, the switch box. Placed " +
                     "along an EDGE only - free-standing in an open yard a locker reads as a " +
                     "shop fitting rather than as works furniture.")]
            public GameObject[] yardFixtures = Array.Empty<GameObject>();

            [Header("Docks (portYard zones only)")]
            [Tooltip("Lay the block out as a working port instead of a terrace: quay along the " +
                     "map edge, warehouses facing the water, container stacks on the apron, a " +
                     "ship at the berth. Replaces the whole perimeter path and the scatter, the " +
                     "way industrialYard does - a port is arranged around its water the way a " +
                     "works is arranged around its roads. Only meaningful together with " +
                     "requiresMapEdge: the water is laid beyond the outline, and PortLayout " +
                     "refuses a block that does not touch it.")]
            public bool portYard;

            [Tooltip("tile-water - stretched into slabs beyond the map outline, sunk so the " +
                     "quay stands above it. Safe to stretch: it carries no Tile component, " +
                     "same as every tile-plain. Empty leaves the sea unrendered, which reads " +
                     "as the void it is.")]
            public GameObject waterTile;

            [Tooltip("The quay strip along the water edge - the BORDERED tile-plain_concrete, " +
                     "not the -nb variant every other yard patch wants: its 6m skirt is what " +
                     "covers the vertical face down to the sunken water, so the quay edge " +
                     "needs no authored wall. Empty falls back to the block slab.")]
            public GameObject quayGround;

            [Tooltip("pier-tile-straight - the pack's ONE pier piece, a 4m module on legs. " +
                     "Tiled out from the quay into the water as a finger for the small boats. " +
                     "Empty builds no pier.")]
            public GameObject pierSegment;

            [Tooltip("crane-port - stood on the quay strip facing the berth. crane-docks and " +
                     "crane-tower are deliberately absent from the shipped list: 33m tall with " +
                     "a mid-height pivot, and 72m, against a block whose whole quay is ~60m.")]
            public GameObject[] portCranes = Array.Empty<GameObject>();

            [Tooltip("cargo-shipping_* - the ISO boxes, stacked in rows of one colour per " +
                     "stack. Modern on purpose: the setting is the 1980s now, and the " +
                     "container stack is the single strongest picture a modern dock has.")]
            public GameObject[] portContainers = Array.Empty<GameObject>();

            [Tooltip("ship-cargo - moored parallel to the quay, in the water. At 82m it is " +
                     "longer than the block; that is correct, ships outsize their berths, and " +
                     "the water runs a cell past each end of the block to hold the overhang.")]
            public GameObject[] portShips = Array.Empty<GameObject>();

            [Tooltip("boat-fishing, boat-speed - moored along the pier finger, drawn flat.")]
            public GameObject[] portBoats = Array.Empty<GameObject>();

            [Tooltip("Quayside dressing placed singly along the quay and apron: anchor, buoys, " +
                     "the lifebuoy, pallets, timber. The stacked stock goes through yardCrates " +
                     "and stackProps, which the port reads the same way the works does.")]
            public GameObject[] portProps = Array.Empty<GameObject>();

            [Tooltip("lantern-long - stood at a fixed pitch along the inland edge of the quay " +
                     "strip. Its own slot rather than a portProps entry because a working quay " +
                     "is LIT AT INTERVALS, and a bag draw cannot promise an interval.")]
            public GameObject portQuayLamp;

            [Header("Boundary")]
            [Tooltip("One length of boundary, tiled round the edge and stretched slightly to " +
                     "close the run - fence-classic railing on a car park, fence-shrub hedge on " +
                     "a park. Empty leaves the block unbounded, which for a park means it bleeds " +
                     "into the road tile's verge with nothing to say where it starts.")]
            public GameObject fenceSegment;

            [Tooltip("fence-stone-tower - the pier that terminates a fence run, at the four " +
                     "corners and either side of a gate. Car parks only; a hedge has no piers.")]
            public GameObject fencePost;

            [Tooltip("ticket-ride-booth - the attendant's kiosk beside the entrance. From the " +
                     "amusement set, but it is a plain 2.2 x 1.7 kiosk and the pack has no " +
                     "other. Empty leaves the gate unmanned.")]
            public GameObject parkingBooth;

            [Tooltip("Overrides the city-wide parked cars for this zone. Empty uses the shared " +
                     "list. This is how a police car ends up outside the police station and an " +
                     "ambulance outside the hospital - the one detail that tells you what the " +
                     "building is without reading the sign.")]
            public WeightedPrefabs[] parkedCars = Array.Empty<WeightedPrefabs>();

            public bool HasOwnParkedCars
            {
                get
                {
                    if (parkedCars == null) return false;
                    foreach (var group in parkedCars)
                        if (group != null && group.IsUsable)
                            return true;
                    return false;
                }
            }

            [Tooltip("Cars for the landmark's own forecourt bay and nowhere else. Unlike " +
                     "parkedCars this does NOT swap the block-wide picker - the patrol car " +
                     "stands in front of the police station without turning every bay on the " +
                     "block into a police pound. Empty means the landmark sits flush to the " +
                     "street with no forecourt.")]
            public WeightedPrefabs[] landmarkCars = Array.Empty<WeightedPrefabs>();

            public bool HasLandmarkCars
            {
                get
                {
                    if (landmarkCars == null) return false;
                    foreach (var group in landmarkCars)
                        if (group != null && group.IsUsable)
                            return true;
                    return false;
                }
            }

            public bool BuildsPerimeter
            {
                get
                {
                    if (groups == null) return false;
                    foreach (var group in groups)
                        if (group != null && group.IsUsable)
                            return true;
                    return false;
                }
            }
        }

        [Header("Road tiles (Tiles_T/Roads_T)")]
        [Tooltip("tile-road-straight")] public GameObject straight;
        [Tooltip("tile-road-curve - unrotated shape connects North and WEST")] public GameObject curve;
        [Tooltip("tile-road-intersection-t")] public GameObject tJunction;
        [Tooltip("tile-road-intersection")] public GameObject cross;
        [Tooltip("tile-road-end")] public GameObject end;

        [Tooltip("tile-road-straight-crosswalk. Its tileShape is Cross, so it probes all four " +
                 "sides, but side neighbours simply yield no path matches - safe as a straight.")]
        public GameObject straightCrosswalk;

        [Header("Dual carriageway (Tiles_T/Roads_T)")]
        [Tooltip("tile-mainroad-straight. Four lanes at x = +/-1.75 and +/-4.75, pavements at " +
                 "+/-7.25 - all measured out of the prefab. Same 30m module as the road tiles, " +
                 "which is what lets it drop into the same grid.")]
        public GameObject mainStraight;

        [Tooltip("tile-mainroad-straight-crosswalk. Same Cross-tileShape caveat as " +
                 "straightCrosswalk: it probes all four sides, but a run of avenue has no side " +
                 "neighbours to match, so it behaves as a straight.")]
        public GameObject mainStraightCrosswalk;

        [Tooltip("tile-road-mainroad-intersection - the avenue crossing a minor street. At 0 " +
                 "degrees the carriageway runs North-South.")]
        public GameObject mainCross;

        [Tooltip("tile-road-mainroad-intersection-t - a minor street teeing into the avenue. At " +
                 "0 degrees the BRANCH is North and the carriageway runs East-West.")]
        public GameObject mainTJunction;

        [Tooltip("tile-mainroad-intersection - two avenues crossing each other. Four lanes at " +
                 "+/-1.75 and +/-4.75 on BOTH axes, so it is symmetric under rotation and is " +
                 "always placed at 0 degrees.")]
        public GameObject mainMainCross;

        [Header("Ground (Tiles_T)")]
        [Tooltip("tile-plain_concrete - laid under each block so courtyards and alleys are " +
                 "paved rather than showing the road tiles' grass verge. Carries no Tile " +
                 "component, so it never joins the path network.")]
        public GameObject groundTile;

        [Header("Traffic (Traffic_T)")]
        [Tooltip("traffic-lights_AI - self-contained, TrafficLightsControl already wired.")]
        public GameObject trafficLights;

        [Tooltip("traffic-lights-big_AI - the gantry set, for the avenue's crossroads where the " +
                 "small post reads as undersized against four lanes. Carries the same one " +
                 "TrafficLightsControl and four TrafficLight components as trafficLights, so it " +
                 "is a drop-in. Falls back to trafficLights when empty.")]
        public GameObject mainTrafficLights;

        [Header("Buildings (Buildings_T)")]
        [Tooltip("One entry per BlockZone. ZonePlanner picks a zone per block from these " +
                 "weights; BlockBuilder then builds from the matching palette.")]
        public ZonePalette[] zonePalettes = Array.Empty<ZonePalette>();

        [Tooltip("Per-prefab yaw corrections for pieces authored off the +Z facade convention. " +
                 "Rewritten by CityAssetBootstrap - edit the table there, not here.")]
        public FacadeYawFix[] facadeYawFixes = Array.Empty<FacadeYawFix>();

        [Tooltip("Prefabs the finished city may hold at most ONE of, whichever path would place " +
                 "them. The post office is why this exists: it is a street shop in the " +
                 "residential palette rather than the landmark of a zone, so no quota in " +
                 "ZonePlanner can reach it and every block was free to build another. Rewritten " +
                 "by CityAssetBootstrap - edit the list there, not here.")]
        public GameObject[] uniqueBuildings = Array.Empty<GameObject>();

        [Tooltip("atlas-LPEC.mat - the one opaque material every building piece shares. The " +
                 "tinter uses it to recognise which renderer slots it may repaint; anything " +
                 "else (atlas-transparent-LPEC on glass) is left alone, because swapping an " +
                 "opaque tint onto a window would brick it up.")]
        public Material buildingBaseMaterial;

        [Tooltip("Copies of buildingBaseMaterial on the Facade Tint shader (which masks the " +
                 "tint off roofs) - the RESIDENTIAL palette: green, tan, brick, blue-grey. " +
                 "Clearly coloured but moderate, because windows are vertical like the walls " +
                 "and the tint reaches them too. Empty disables tinting.")]
        public Material[] buildingTints = Array.Empty<Material>();

        [Tooltip("Stronger versions of the same hues, for groups marked commercial. Empty " +
                 "falls back to buildingTints, so an asset predating this field still tints.")]
        public Material[] commercialTints = Array.Empty<Material>();

        [Tooltip("58 WHITE-LPEC - the pack's plain white URP/Lit, with no texture bound at all. " +
                 "Painted onto the procedural parking-line mesh, so the markings need no new " +
                 "asset and no UV authoring. Empty leaves car parks unmarked.")]
        public Material lineMaterial;

        [Tooltip("Material variants of buildingBaseMaterial for the ground slabs. Free to range " +
                 "much wider than buildingTints: a floor is one flat patch of the atlas with no " +
                 "roof or windows for _BaseColor to drag along, and it is exactly that width " +
                 "that turns a block's slabs into a mosaic instead of one field of grey. Empty " +
                 "leaves every slab the pack's own colour.")]
        public Material[] groundTints = Array.Empty<Material>();

        [Tooltip("18 GREY-DARK-LPEC - untextured, like lineMaterial. Paving joints and faded " +
                 "repair patches are painted on it. Empty leaves them undrawn.")]
        public Material paintDarkMaterial;

        [Tooltip("22 GREY-LIGHTEST-LPEC - the same idea one end of the ramp up, for the footpath " +
                 "worn across a yard. Empty leaves the paths undrawn.")]
        public Material paintLightMaterial;

        [Tooltip("Ash-dark variant of the atlas, multiplied onto the ground round a boiler house " +
                 "and onto the most worn yard patches.\n\n" +
                 "Deliberately NOT a member of groundTints: GroundPlacer.Shade draws from that " +
                 "array uniformly for every block in the city, so a near-black entry there would " +
                 "put cinder-coloured slabs down residential streets.")]
        public Material cinderTint;

        [Tooltip("17 GREY-DARKEST - tyre ruts and the painted rail spur. Darker than " +
                 "paintDarkMaterial, which is paving joints and has to stay readable as concrete.")]
        public Material grimeMaterial;

        [Tooltip("57 BLACK - oil stains under the lorry stands and the loading aprons.")]
        public Material oilMaterial;

        [Header("Smoke")]
        [Tooltip("Where the chimney mouths are on each prefab, in its own local space. Measured " +
                 "by Tools/City/Measure Chimney Vents and rewritten every bootstrap - do not " +
                 "type values here. Measured rather than typed for the reason CornerFacing " +
                 "exists: the FBX importer negates X, so a vent read off the raw mesh file is " +
                 "mirrored, and industry-refinery's single stack is exactly the case that " +
                 "catches out.")]
        public ChimneyVent[] chimneyVents = Array.Empty<ChimneyVent>();

        [Tooltip("Universal Render Pipeline/Particles/Unlit, transparent, untextured. The pack " +
                 "ships no smoke texture at all, and the Built-in Default-ParticleSystem " +
                 "material renders magenta under URP - so this is authored by the bootstrap. " +
                 "Empty disables smoke however chimneySmoke is set.")]
        public Material smokeMaterial;

        [Tooltip("cloud-fluffy - the particles are MESHES, not billboards. A soft blurred quad " +
                 "is the obvious choice and the wrong one here: every other surface in this city " +
                 "is flat-shaded low poly, and a gaussian plume reads as borrowed from another " +
                 "game. Empty falls back to billboards.")]
        public Mesh smokePuffMesh;

        [Header("Props (Props_T/City_T, Nature_T/Trees_T)")]
        public GameObject[] streetLamps = Array.Empty<GameObject>();
        public GameObject[] trees = Array.Empty<GameObject>();
        public GameObject[] smallProps = Array.Empty<GameObject>();

        [Header("Vehicles")]
        [Tooltip("Vehicles_T/Cars_T - static scenery, no AI. Curated for a city street: ordinary " +
                 "cars, the odd taxi or police car. No buses, lorries or plant machinery - a " +
                 "crawler crane at the kerb of a residential street reads as a bug, because it is.")]
        public WeightedPrefabs[] parkedCarGroups = Array.Empty<WeightedPrefabs>();

        [Tooltip("Vehicles_T/Cars_AI_T - carries CarBehavior + PathFinding. Traffic tolerates the " +
                 "occasional lorry that parking does not; it is passing through. No bus, though: " +
                 "the passenger bus was dropped for the corners it clipped, and the school bus " +
                 "is SchoolBusDirector's own vehicle - see schoolBusPrefab.")]
        public WeightedPrefabs[] aiCarGroups = Array.Empty<WeightedPrefabs>();

        [Tooltip("The models VehicleTinter is allowed to repaint, static and _AI forms alike. " +
                 "Not every car may be: the pack bakes colour into UVs and a tint MULTIPLIES the " +
                 "atlas, so it can only ever darken. That works on the models whose body swatch " +
                 "is neutral - car-passenger and car-caravan-small at #dbdbda, car-veteran and " +
                 "car-pickup-modern at #878282 - and ruins the ones already painted, because " +
                 "yellow times blue is dark green. A taxi, a police car, a school bus, the race " +
                 "car, the hippie van and jeep-open are therefore absent by design, not by " +
                 "oversight. Authored in CityAssetBootstrap, which carries the measured table.\n\n" +
                 "A flat list rather than a flag on the groups above because paintability cuts " +
                 "ACROSS them - Everyday holds both car-passenger and car-hippie-van - and " +
                 "splitting the buckets would change their ShuffleBag lengths, which are drawn " +
                 "from BlockBuilder's shared Buildings stream and would re-lay the whole city.")]
        public GameObject[] paintableVehicles = Array.Empty<GameObject>();

        [Tooltip("Prefab NAMES the ShuffleBags deal as normal but RareVehicleFilter then " +
                 "swaps for an ordinary body from the same group - car-caravan-small, whose bag " +
                 "seat otherwise guarantees it once per Everyday cycle (~12% of placed cars). " +
                 "Names rather than prefab references so the list can never change a bag's " +
                 "length: the bags draw from BlockBuilder's shared Buildings stream and resizing " +
                 "one re-lays the whole city - which is also why the camper cannot simply be " +
                 "removed from parkedCarGroups.")]
        public string[] rareVehicleNames = { "car-caravan-small" };

        [Tooltip("Chance a rare deal is KEPT rather than substituted. 0 retires the camper " +
                 "outright - every deal is swapped for an ordinary same-group body. The roll " +
                 "runs on its own stream (SeedOffsets.RareVehicles), so retuning this cannot " +
                 "move a building. Field initialisers matter here: an older PrefabDatabase.asset " +
                 "that predates these two fields deserialises to these defaults, no bootstrap " +
                 "re-run needed.")]
        [Range(0f, 1f)]
        public float rareVehicleKeepChance = 0f;

        [Tooltip("Body colours for the cars in paintableVehicles: variants of atlas-LPEC whose " +
                 "_BaseColor multiplies the atlas. Unlike buildingTints these may run at full " +
                 "strength, because a car's paint swatch is near-white where a facade's is " +
                 "already coloured - see BuildTintPalette. Empty disables car paint, which is " +
                 "not a fault: every car then keeps the body the pack baked in.")]
        public Material[] vehicleTints = Array.Empty<Material>();

        [Header("People")]
        [Tooltip("The crowd mix, weighted the way traffic is: a group is rolled by weight, then " +
                 "a ShuffleBag deals within it. A flat list made the pack's one gangster exactly " +
                 "as common as its lifeguard. Mixes both packs - Epic City's People_AI_T and the " +
                 "Animated People models the bootstrap converts into Configs/People.")]
        public WeightedPrefabs[] pedestrianGroups = Array.Empty<WeightedPrefabs>();

        [Tooltip("People_T/People_AI_T - carries HumanBehavior + PathFinding. Fallback only: the " +
                 "flat, unweighted list PedestrianSpawner uses when pedestrianGroups is empty, " +
                 "which is what a scene saved before the groups existed will find.")]
        public GameObject[] aiPedestrians = Array.Empty<GameObject>();

        [Tooltip("The pack's People Controller (walk + idle) extended with talk, argue and " +
                 "bench-sit states retargeted from the Animated People pack - every rig " +
                 "involved is already Humanoid. Authored by the bootstrap; assigned onto each " +
                 "spawned pedestrian at runtime so no pack prefab is modified. Empty leaves " +
                 "the pack controller in place and the interaction animations silently off.")]
        public RuntimeAnimatorController pedestrianController;

        [Header("Police")]
        [Tooltip("car-police_AI. Removed from the generic traffic buckets - the only police " +
                 "cars in the city are the station's own patrol fleet, spawned and owned by " +
                 "PoliceDirector.")]
        public GameObject policeCarPrefab;

        [Tooltip("man-police_AI - authored by the bootstrap from the Animated People pack's " +
                 "man_police (male model only, per design): wander/NavMesh stripped, the AI " +
                 "pedestrian kit (kinematic Rigidbody, capsule, HumanBehavior, PathFinding) " +
                 "added. Lives in Assets/Configs, outside the People_AI_T glob, so civilians " +
                 "can never draw it.")]
        public GameObject policeOfficerPrefab;

        [Header("School run")]
        [Tooltip("bus-school_AI. Kept OUT of the generic traffic buckets the same way the " +
                 "police car is: the only school bus in the city is the one SchoolBusDirector " +
                 "spawns and owns, so it is never seen driving a route with nobody on it.")]
        public GameObject schoolBusPrefab;

        [Tooltip("The child models the roster is drawn from - Epic City's one surviving child " +
                 "plus the Animated People children the bootstrap authors. Drawn from by " +
                 "SchoolBusDirector; the SAME models also carry the crowd's Children group, " +
                 "so a child on the pavement and a child at the bus stop are the same city.")]
        public GameObject[] schoolChildPrefabs = Array.Empty<GameObject>();

        [Header("Docks")]
        [Tooltip("The rigs the port's shift is drawn from - the worker models the bootstrap " +
                 "already authors for the crowd (man-worker_AI, man-construction-worker_AI). " +
                 "Drawn from by PortDirector. Same models as the crowd's Workers group, so a " +
                 "docker on the quay and a labourer on the pavement are the same city.")]
        public GameObject[] dockWorkerPrefabs = Array.Empty<GameObject>();

        [Header("Ambient (Nature_T/Clouds_T)")]
        public GameObject[] clouds = Array.Empty<GameObject>();

        /// <summary>
        /// The palette for a zone, or null when the database has no entry for it.
        ///
        /// Callers fall back to ResidentialHigh rather than skipping the block: an unpopulated
        /// zone should still produce ordinary city fabric, because a hole in the street wall is
        /// a far louder bug than a block built from the wrong kit.
        /// </summary>
        public ZonePalette PaletteFor(BlockZone zone)
        {
            if (zonePalettes == null)
                return null;

            foreach (var palette in zonePalettes)
                if (palette != null && palette.zone == zone)
                    return palette;

            foreach (var palette in zonePalettes)
                if (palette != null && palette.zone == BlockZone.ResidentialHigh)
                    return palette;

            return null;
        }

        /// <summary>
        /// Extra Y rotation for a prefab, 0 for anything not in the table. Linear scan on
        /// purpose - the table holds a handful of entries and a dictionary would need cache
        /// invalidation across bootstrap reruns.
        /// </summary>
        public float ExtraYawFor(GameObject prefab)
        {
            if (facadeYawFixes == null)
                return 0f;

            foreach (var fix in facadeYawFixes)
                if (fix != null && fix.prefab == prefab)
                    return fix.extraYaw;

            return 0f;
        }

        /// <summary>
        /// Chimney mouths on a prefab, in its local space. Empty for most of the catalogue, and
        /// that is the normal case rather than a gap in the table - a warehouse has no chimney.
        /// Linear scan for the same reason ExtraYawFor uses one.
        /// </summary>
        public void ChimneyVentsFor(GameObject prefab, List<Vector3> into)
        {
            into.Clear();

            if (chimneyVents == null || !prefab)
                return;

            foreach (var vent in chimneyVents)
                if (vent != null && vent.prefab == prefab)
                    into.Add(vent.local);
        }

        public GameObject GetRoadTile(RoadTileKind kind) => kind switch
        {
            RoadTileKind.Straight => straight,
            RoadTileKind.Curve => curve,
            RoadTileKind.TJunction => tJunction,
            RoadTileKind.Cross => cross,
            RoadTileKind.End => end,
            RoadTileKind.MainStraight => mainStraight,
            RoadTileKind.MainCross => mainCross,
            RoadTileKind.MainTJunction => mainTJunction,
            RoadTileKind.MainMainCross => mainMainCross,
            _ => null,
        };

        /// <summary>Reports what is missing so the generator can fail loudly instead of producing an empty scene.</summary>
        public bool ValidateRoadTiles(out string missing)
        {
            var gaps = string.Empty;
            if (!straight) gaps += " straight";
            if (!curve) gaps += " curve";
            if (!tJunction) gaps += " tJunction";
            if (!cross) gaps += " cross";
            if (!end) gaps += " end";

            missing = gaps.Trim();
            return missing.Length == 0;
        }

        /// <summary>
        /// The dual carriageway's own three tiles, checked separately from ValidateRoadTiles and
        /// deliberately NOT fatal.
        ///
        /// An asset file that predates the boulevard has these empty, and the honest failure
        /// there is a city with an ordinary street where its avenue should be - not no city at
        /// all. RoadNetworkBuilder warns once and falls back to the minor-road tiles, which fit
        /// the same grid and link the same way, so the map stays drivable either way.
        /// Tools/City/Create or Refresh Config Assets fills them in.
        /// </summary>
        public bool ValidateMainRoadTiles(out string missing)
        {
            var gaps = string.Empty;
            if (!mainStraight) gaps += " mainStraight";
            if (!mainCross) gaps += " mainCross";
            if (!mainTJunction) gaps += " mainTJunction";
            if (!mainMainCross) gaps += " mainMainCross";

            missing = gaps.Trim();
            return missing.Length == 0;
        }
    }
}
