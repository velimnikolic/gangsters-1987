using System;
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
        /// Correction for prefabs authored off the pack's facade convention. BlockBuilder
        /// rotates local +Z toward the street, which is how the pack's own demo scene orients
        /// these buildings - but three block pieces put their windows elsewhere (measured from
        /// the meshes: the plain 5floor's facade is on -Z and its +Z is a blank wall, and the
        /// two corner kits are mirrored relative to each other), so each carries the yaw that
        /// puts its windows back on the pavement.
        /// </summary>
        [Serializable]
        public sealed class FacadeYawFix
        {
            public GameObject prefab;
            public float extraYaw;
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
                     "control for anything a city has ONE of - the hospital, the police station, " +
                     "Chinatown. A share cap cannot express that: 9% of a twelve-block map is " +
                     "one hospital, but 9% of a forty-block map is three.")]
            [Min(0)] public int maxBlocks;

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

            [Header("Buildings")]
            [Tooltip("Empty means no perimeter buildings at all - a park or a car park.")]
            public WeightedGroup[] groups = Array.Empty<WeightedGroup>();

            [Tooltip("Roll the building group once per street-facing run instead of per slot. " +
                     "A period terrace street was built as one development: a side is either " +
                     "a flush 4/5-storey wall, or a row of detached blocks, or shops - not a " +
                     "lottery of all three. Alley sides, corner retail and the neighbour " +
                     "bleed keep their per-slot rolls.")]
            public bool uniformStreetRuns;

            [Tooltip("Cap on Subdivide's columns AND rows. 0 = uncapped. 1 = a single perimeter " +
                     "ring: every building fronts a street and the block encloses one courtyard, " +
                     "instead of interior lots whose rows can only ever face the alleys.")]
            [Min(0)] public int maxLotsPerAxis;

            [Tooltip("At most one per block, on the longest street run. This is what makes a " +
                     "block read as 'the hospital block' without filling it with hospitals.")]
            public GameObject[] landmarks = Array.Empty<GameObject>();
            [Range(0f, 1f)] public float landmarkChance;

            [Header("Yard")]
            [Tooltip("Scattered across the block interior - trees for a park, timber and " +
                     "brick stacks for a works yard.")]
            public GameObject[] scatter = Array.Empty<GameObject>();
            [Range(0f, 1f)] public float scatterDensity;

            [Tooltip("Chance a street-facing slot becomes a surface car park instead of a building.")]
            [Range(0f, 1f)] public float parkingChance = 0.12f;

            [Tooltip("Fill the whole block with rows of parked cars - the Parking zone.")]
            public bool carRows;

            [Header("Car park (carRows zones only)")]
            [Tooltip("fence-classic - one 2m length of railing, tiled round the boundary and " +
                     "stretched slightly to close the run. Empty leaves the lot unfenced.")]
            public GameObject fenceSegment;

            [Tooltip("fence-stone-tower - the pier that terminates a fence run, at the four " +
                     "corners and either side of the gate.")]
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

        [Header("Ground (Tiles_T)")]
        [Tooltip("tile-plain_concrete - laid under each block so courtyards and alleys are " +
                 "paved rather than showing the road tiles' grass verge. Carries no Tile " +
                 "component, so it never joins the path network.")]
        public GameObject groundTile;

        [Header("Traffic (Traffic_T)")]
        [Tooltip("traffic-lights_AI - self-contained, TrafficLightsControl already wired.")]
        public GameObject trafficLights;

        [Header("Buildings (Buildings_T)")]
        [Tooltip("One entry per BlockZone. ZonePlanner picks a zone per block from these " +
                 "weights; BlockBuilder then builds from the matching palette.")]
        public ZonePalette[] zonePalettes = Array.Empty<ZonePalette>();

        [Tooltip("Per-prefab yaw corrections for pieces authored off the +Z facade convention. " +
                 "Rewritten by CityAssetBootstrap - edit the table there, not here.")]
        public FacadeYawFix[] facadeYawFixes = Array.Empty<FacadeYawFix>();

        [Tooltip("atlas-LPEC.mat - the one opaque material every building piece shares. The " +
                 "tinter uses it to recognise which renderer slots it may repaint; anything " +
                 "else (atlas-transparent-LPEC on glass) is left alone, because swapping an " +
                 "opaque tint onto a window would brick it up.")]
        public Material buildingBaseMaterial;

        [Tooltip("Material variants of buildingBaseMaterial overriding only _BaseColor. Kept " +
                 "few and near-white on purpose: colour in this pack lives in the atlas UVs, " +
                 "so _BaseColor multiplies the WHOLE building - a saturated tint drags roofs " +
                 "and windows along with the walls. Empty disables tinting.")]
        public Material[] buildingTints = Array.Empty<Material>();

        [Tooltip("58 WHITE-LPEC - the pack's plain white URP/Lit, with no texture bound at all. " +
                 "Painted onto the procedural parking-line mesh, so the markings need no new " +
                 "asset and no UV authoring. Empty leaves car parks unmarked.")]
        public Material lineMaterial;

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
                 "occasional bus or lorry that parking does not; they are passing through.")]
        public WeightedPrefabs[] aiCarGroups = Array.Empty<WeightedPrefabs>();

        [Header("People")]
        [Tooltip("People_T/People_AI_T - carries HumanBehavior + PathFinding.")]
        public GameObject[] aiPedestrians = Array.Empty<GameObject>();

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

        public GameObject GetRoadTile(RoadTileKind kind) => kind switch
        {
            RoadTileKind.Straight => straight,
            RoadTileKind.Curve => curve,
            RoadTileKind.TJunction => tJunction,
            RoadTileKind.Cross => cross,
            RoadTileKind.End => end,
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
    }
}
