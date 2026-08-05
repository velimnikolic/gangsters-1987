using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using LivingCity.Data;
using LivingCity.Generation;

namespace LivingCity.EditorTools
{
    /// <summary>
    /// Creates and fills in Configs/CityConfig.asset and Configs/PrefabDatabase.asset.
    ///
    /// Every prefab path here was read off disk rather than taken from documentation - the
    /// package's prefab FILES carry no "_T" suffix (the T is on the folder), and the props are
    /// split across Props_T and its City_T and Fence_T subfolders rather than sitting in one
    /// place. Anything that fails to load is reported rather than silently skipped, so a package
    /// update that renames assets surfaces immediately.
    ///
    /// The zone palettes below are also where the PERIOD is enforced. The city is Chicago in the
    /// 1920s, so every office tower in the pack is excluded - they run from 39m to 92m and all
    /// of them read as glass. The tallest thing here is the 5-storey terrace kit at 23.8m, which
    /// is the right measure for a street of the era outside the Loop. Also out: the mall, the
    /// burger joint, the car wash, the data centre, the modern houses - and the cargo containers,
    /// which are the easy one to miss because they suit an industrial yard perfectly well and are
    /// thirty years early (containerisation starts in 1956). Construction_T's timber, brick
    /// stacks and cement bags stand in for them.
    /// </summary>
    public static class CityAssetBootstrap
    {
        const string ConfigDir = "Assets/Configs";
        const string TintDir = "Assets/Materials/BuildingTints";
        const string Root = "Assets/polyperfect/Low Poly Epic City/T/- Prefabs_T/";
        const string PackMaterials = "Assets/polyperfect/Low Poly Epic City/- Materials/";

        const string Roads = Root + "Tiles_T/Roads_T/";
        const string Tiles = Root + "Tiles_T/";
        const string Traffic = Root + "Traffic_T/";
        const string Buildings = Root + "Buildings_T/";
        const string Props = Root + "Props_T/";
        const string CityProps = Root + "Props_T/City_T/";
        const string Fences = Root + "Props_T/Fence_T/";
        const string Construction = Root + "Construction_T/";

        /// <summary>
        /// Only one thing is taken from here: ticket-ride-booth. It is a plain 2.24 x 2.80 x 1.71
        /// kiosk, the pack has no parking booth of its own, and nothing about it says fairground
        /// once it is standing at the gate of a car park.
        /// </summary>
        const string Amusement = Root + "Amusement Park_T/";
        const string Trees = Root + "Nature_T/Trees_T/";
        const string Flowers = Root + "Nature_T/Flowers_T/";
        const string Stones = Root + "Nature_T/Stones_T/";
        const string Clouds = Root + "Nature_T/Clouds_T/";
        const string Vehicles_ = Root + "Vehicles_T/";
        const string CarsAI = Root + "Vehicles_T/Cars_AI_T/";
        const string CarsStatic = Root + "Vehicles_T/Cars_T/";
        const string PeopleAI = Root + "People_T/People_AI_T/";

        /// <summary>
        /// The 4- and 5-storey kits in ONE list, which is the whole point.
        ///
        /// A ShuffleBag deals every piece once before repeating any, so a single bag over both
        /// kits guarantees they alternate along one and the same run. Two separate groups would
        /// not: the group is the unit of choice, so a run would tend to settle into one kit. The
        /// height difference (20.6 vs 23.8m) and the depth difference (13.4 vs 15.7m) then give
        /// the terrace a stepped silhouette, which is what a street of the period looked like.
        ///
        /// The 5-floor kit ships no -back variant, so the alley wall is 4floor-back throughout.
        /// Monotonous, but it is a blind wall nobody sees; the alternative is a shopfront facing
        /// the bins.
        /// </summary>
        static readonly string[] TerraceStreet =
        {
            "building-block-4floor-front", "building-block-4floor-short",
            "building-block-5floor", "building-block-5floor-front", "building-block-5floor-short",
        };

        static readonly string[] TerraceRear = { "building-block-4floor-back" };

        static readonly string[] TerraceCorner =
        {
            "building-block-4floor-corner", "building-block-5floor-corner",
        };

        static readonly List<string> Missing = new();

        const string DebugZoneMenu = "Tools/City/Debug - Only Residential High";

        /// <summary>
        /// Debugging toggle: every block becomes ResidentialHigh, so a change to that palette
        /// shows on every block of the next Generate. State lives in CityConfig.asset
        /// (debugSingleZone), not in EditorPrefs, so the generated city and the checkbox can
        /// never disagree. Remember to Clear + Generate after flipping it.
        /// </summary>
        [MenuItem(DebugZoneMenu, priority = 2)]
        public static void ToggleDebugResidentialHigh()
        {
            var config = AssetDatabase.LoadAssetAtPath<CityConfig>($"{ConfigDir}/CityConfig.asset");
            if (!config)
            {
                Debug.LogWarning("[CityAssetBootstrap] No CityConfig.asset yet - run " +
                                 "Tools/City/Create or Refresh Config Assets first.");
                return;
            }

            config.debugSingleZone = !config.debugSingleZone;
            config.debugZone = BlockZone.ResidentialHigh;
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();

            Debug.Log(config.debugSingleZone
                ? "[CityAssetBootstrap] Debug zoning ON - every block will be ResidentialHigh. " +
                  "Clear Generated City, then Generate."
                : "[CityAssetBootstrap] Debug zoning OFF - normal zoning restored. " +
                  "Clear Generated City, then Generate.");
        }

        [MenuItem(DebugZoneMenu, true)]
        static bool ToggleDebugResidentialHighValidate()
        {
            var config = AssetDatabase.LoadAssetAtPath<CityConfig>($"{ConfigDir}/CityConfig.asset");
            Menu.SetChecked(DebugZoneMenu, config && config.debugSingleZone);
            return true;
        }

        [MenuItem("Tools/City/Create or Refresh Config Assets")]
        public static void CreateAssets()
        {
            Missing.Clear();
            Directory.CreateDirectory(ConfigDir);

            var config = GetOrCreate<CityConfig>($"{ConfigDir}/CityConfig.asset");
            var db = GetOrCreate<PrefabDatabase>($"{ConfigDir}/PrefabDatabase.asset");

            // CityConfig.asset survives every refresh with its serialized values intact -
            // GetOrCreate never rewrites an existing asset - so a default changed in
            // CityConfig.cs alone never reaches a project that already has the asset. The
            // layout bounds are design decisions and get stamped here like the palettes;
            // seed, fill ratio, grid size and the entity counts are the user's knobs and
            // are left alone.
            config.minArterialSpacing = 2;
            config.maxArterialSpacing = 4;

            // Road tiles. tileShape values verified in the prefab files:
            // straight=Straight, curve=Turn, intersection-t=T, intersection=Cross, end=End.
            db.straight = Load(Roads + "tile-road-straight.prefab");
            db.curve = Load(Roads + "tile-road-curve.prefab");
            db.tJunction = Load(Roads + "tile-road-intersection-t.prefab");
            db.cross = Load(Roads + "tile-road-intersection.prefab");
            db.end = Load(Roads + "tile-road-end.prefab");
            db.straightCrosswalk = Load(Roads + "tile-road-straight-crosswalk.prefab");

            db.groundTile = Load(Root + "Tiles_T/tile-plain_concrete.prefab");
            db.trafficLights = Load(Traffic + "traffic-lights_AI.prefab");

            db.zonePalettes = BuildZonePalettes();

            // Measured off the FBX geometry, not guessed: the plain 5floor block's window rows
            // are on local -Z (its +Z is a 12-vertex blank wall), and the two corner kits are
            // mirrored relative to each other, so a single global offset can never fix both.
            // The china apartment's +Z is its least-windowed face and the pack's own demo
            // scenes never point it at a road; the big house-block has blank gable ends on
            // both Z faces, windows on +/-X. house-block and house-block-old are authored
            // front = +Z and need no entry.
            // If a facade still shows a blank wall to a street, flip the sign of its entry.
            db.facadeYawFixes = new[]
            {
                YawFix(Buildings + "building-block-5floor.prefab", 180f),
                YawFix(Buildings + "building-block-4floor-corner.prefab", 90f),
                YawFix(Buildings + "building-block-5floor-corner.prefab", -90f),
                YawFix(Buildings + "building-apartment-china.prefab", 180f),
                YawFix(Buildings + "building-house-block-big.prefab", 90f),
            };

            // lamp-city first and lamp-road dropped: lamp-road is a motorway lantern on a plain
            // steel pole. The ornate one is the period fitting.
            db.streetLamps = LoadAll(CityProps, "lamp-city", "lamp-road-double");
            db.trees = LoadAll(Trees, "tree-oak", "tree-birch", "tree-lime", "tree-round",
                                      "tree-poplar", "shrub", "shrub-round");

            // Period sweep of the street furniture: the bus shelter is a glass canopy, the cycle
            // stand and the cash machine are plainly modern. Post box, lantern and guidepost take
            // their place, and the hot dog stand stays because Chicago.
            db.smallProps = Merge(
                LoadAll(CityProps, "bench-old", "bin-wheelie", "dumpster",
                                   "fire-hydrant", "hot-dog-stand"),
                LoadAll(Props, "mail-box", "trash-can", "lantern-long", "guidepost"),
                LoadAll(Vehicles_, "bike-old"));

            db.clouds = LoadAll(Clouds, "cloud-big", "cloud-fluffy", "cloud-long", "cloud-triangle");

            // Vehicles are curated by hand, not globbed. Cars_T holds 32 prefabs of which only
            // about six read as an ordinary civilian car; the rest are plant machinery, a
            // formula car and fairground bumper cars. Dropping the folder in and drawing
            // uniformly is what put excavators and crawler cranes on residential kerbs.
            //
            // car-veteran is still the only genuinely vintage car in the pack, and for a while it
            // was the only civilian one used - which made every other car on the street the same
            // model. That reads worse than the anachronism does: a repeated silhouette is noticed
            // in seconds, a rounded saloon among vintage architecture takes a second look. So
            // every body in the pack that a private owner would plausibly drive is in, at an
            // equal share, and car-veteran is now one of eight rather than the whole street.
            //
            // car-camper-vintage is 7.29m, over ParkedCarPlacer.MaxParkedLength, so
            // VehiclePicker.Fits rejects it at every kerb and it can only ever land in a block
            // car park (limit 11m). That is left as it is rather than raised - a camper belongs
            // in a car park, not nose to tail on a terrace - so do not "fix" the silent reject.
            //
            // Still out, and why: golf-cart and car-baywatch are not road cars a resident owns;
            // the buses are 9.8-11.3m and cannot fit either limit, so they are AI-only below;
            // the bumper cars, formula car, excavator, bulldozer, crane and road roller are what
            // globbing this folder used to put outside a flat.
            db.parkedCarGroups = new[]
            {
                Vehicles("Everyday", 70f, CarsStatic,
                         "car-veteran", "car-passenger", "car-passenger-race", "car-hippie-van",
                         "jeep-open", "car-pickup-modern", "car-camper-vintage",
                         "car-caravan-small"),

                // A taxi waits at a kerb as often as it drives, and it is already accepted in
                // traffic below. The police car is not here on purpose: parked, it is studied,
                // and outside the Police zone - which has its own override - it would read as
                // a mistake rather than as a beat car.
                Vehicles("Service", 8f, CarsStatic, "car-taxi"),

                // The armoured van is the one addition the period actively wants: a bank car is
                // the whole reason this city has a police station in it.
                Vehicles("Trade", 22f, CarsStatic, "truck", "car-tow-truck", "armored-truck"),
            };

            // motorbike_AI is deliberately absent from Everyday. Every vehicle in the pack is
            // an empty shell - there is not one SkinnedMeshRenderer in Cars_AI_T - and a car
            // gets away with it because the cabin is behind tinted glass. A bike does not: at
            // 66/100 weight it was about four riderless motorbikes on screen at once, and it
            // reads as a bug rather than as stylisation. Putting a rider on it is not a small
            // job either, because the Epic City pack ships no seated animation (its People
            // Controller has exactly two states, Breathing Idle and Standard Walk), so the
            // pose would have to come from the Animated People pack by humanoid retargeting.
            // If that ever gets done, add the prefab variant HERE - this list overwrites
            // PrefabDatabase.aiCarGroups wholesale on every run, so an inspector-assigned
            // prefab would be wiped by the next Set Up Scene.
            db.aiCarGroups = new[]
            {
                // The same bodies as the kerb, so a car that drives past and a car that is parked
                // belong to the same city. Every one carries the pack's CarBehavior + PathFinding
                // pair and the same wheel rig as car-veteran_AI.
                //
                // Shorter than the parked list because Cars_AI_T is not a mirror of Cars_T: the
                // pack ships no _AI variant for car-pickup-modern, car-camper-vintage or
                // car-caravan-small, so those three are parked-only. Adding them here would just
                // land in the Missing warning at the end of this file.
                Vehicles("Everyday", 66f, CarsAI,
                         "car-veteran_AI", "car-passenger_AI", "car-passenger-race_AI",
                         "car-hippie-van_AI", "jeep-open_AI"),

                // The taxi and the police car are modern shells, but a city street of the era
                // did have both and there is no vintage stand-in in the pack. Kept at a low
                // weight, and moving, where the silhouette is read for a moment rather than
                // studied at the kerb.
                Vehicles("Service", 14f, CarsAI, "car-taxi_AI", "car-police_AI"),

                Vehicles("Freight", 14f, CarsAI, "truck_AI", "car-tow-truck_AI",
                         "armored-truck_AI"),

                // A passenger bus is the one large body worth having: VehicleSpawner calls
                // picker.Next() with no size limits (VehicleSpawner.cs:176), so unlike the kerb
                // and the car parks, traffic has no length ceiling and an 11.28m vehicle is
                // legal here. Kept to 6/100 - it is the biggest silhouette on the road and two
                // on screen at once would own the street. bus-school stays out: a yellow
                // American school bus is the loudest anachronism in the pack.
                //
                // Watch this one first if traffic starts clipping corners. It is by far the
                // longest thing the pack's CarBehavior has to steer through a junction, and
                // dropping this single group is the whole fix.
                Vehicles("Transit", 6f, CarsAI, "bus-passenger_AI"),
            };

            db.aiPedestrians = LoadFolder(PeopleAI);

            BuildTintPalette(db);

            EditorUtility.SetDirty(config);
            EditorUtility.SetDirty(db);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (Missing.Count > 0)
                Debug.LogWarning($"[CityAssetBootstrap] {Missing.Count} prefab(s) not found:\n - " +
                                 string.Join("\n - ", Missing));

            Debug.Log($"[CityAssetBootstrap] Config assets ready in {ConfigDir}.\n" +
                      $"Zones: {DescribeZones(db.zonePalettes)}\n" +
                      $"AI cars: {Describe(db.aiCarGroups)}, parked cars: {Describe(db.parkedCarGroups)}, " +
                      $"pedestrians: {db.aiPedestrians.Length}, clouds: {db.clouds.Length}.");

            Selection.activeObject = db;
        }

        /// <summary>
        /// What each kind of block is built from. This is the catalogue the whole generator
        /// hangs off - see BlockZone for what the zones mean and why there is no Commercial one.
        ///
        /// Sizes were measured off the meshes, not guessed. A block is 46m across (one cell),
        /// 76m (two) or 106m (three), and BlockBuilder.Subdivide turns that into lot runs of
        /// roughly 32-46m, so anything wider than ~35m can never be placed. That is what rules
        /// out building-office-big (90m), the stadium (85m), the train station (58m) and the
        /// airport - they would be drawn, measured, rejected and redrawn forever.
        /// </summary>
        static PrefabDatabase.ZonePalette[] BuildZonePalettes()
        {
            var park = Load(Tiles + "tile-park.prefab");
            var asphalt = Load(Tiles + "tile-plain_asphalt.prefab");
            var concrete = Load(Tiles + "tile-plain_concrete.prefab");
            var dirt = Load(Tiles + "tile-plain_dirt.prefab");
            var grass = Load(Tiles + "tile-plain_grass.prefab");
            // tile-plain_sand exists too, and is omitted on purpose - a sand block in 1920s
            // Chicago reads as a bug, not as variety. Do not "complete the set".

            var cityTrees = LoadAll(Trees, "tree-oak", "tree-lime", "tree-poplar", "shrub-round");

            return new[]
            {
                // The connective tissue of the city, and the zone the fallback lands on.
                Palette(BlockZone.ResidentialHigh, weight: 26f, maxShare: 1f,
                    groups: new[]
                    {
                        Terrace("Terrace 4+5 floor", 55f, TerraceStreet, TerraceRear, TerraceCorner),

                        Detached("Old blocks", 30f, 1f, 3f, 0f, false,
                            "building-house-block", "building-house-block-big",
                            "building-house-block-old", "building-apartment-china"),

                        Shops(15f),
                    },
                    // One development per street: a side is a flush 4/5-storey wall, or a row
                    // of old blocks, or shops - not a per-slot lottery of all three. This is
                    // what makes the terraces actually touch.
                    uniformStreetRuns: true,
                    // A single ring of buildings around one courtyard, however thick the
                    // block. Uncapped subdivision gave a 3-cell block 3x3 ringed lots - six
                    // parallel rows, of which only the outermost faced a street.
                    maxLotsPerAxis: 1,
                    grounds: new[]
                    {
                        Ground("Concrete", 55f, concrete),
                        Ground("Asphalt", 25f, asphalt),
                        Ground("Dirt", 20f, dirt),
                    },
                    // The terrace ring encloses a yard, so the treatment reads strongest here.
                    courtyardGrounds: new[] { dirt, grass },
                    scatter: Merge(cityTrees,
                                   LoadAll(CityProps, "dumpster", "bin-wheelie", "hot-dog-stand"),
                                   LoadAll(Props, "mail-box")),
                    scatterDensity: 0.10f),

                // Chicago's bungalow belt: detached, set back off the pavement, with the odd
                // store on the corner. The setback is what separates it visually from the
                // terraces above - those sit hard on the kerb, these do not.
                Palette(BlockZone.ResidentialLow, weight: 20f, maxShare: 1f,
                    groups: new[]
                    {
                        Detached("Houses", 94f, 3f, 7f, 4f, false,
                            "building-house-family-small", "building-house-family-large",
                            "building-cabin-big", "building-cabin-small",
                            "building-house-block-old", "building-house-block"),

                        Shops(6f),
                    },
                    grounds: new[]
                    {
                        Ground("Grass", 45f, grass),
                        Ground("Concrete", 30f, concrete),
                        Ground("Dirt", 25f, dirt),
                    },
                    scatter: Merge(cityTrees,
                                   LoadAll(Trees, "shrub"),
                                   LoadAll(Fences, "fence-picket"),
                                   LoadAll(Props, "shed-dog", "sand-box", "dryer-outside",
                                                  "flowers-window")),
                    scatterDensity: 0.16f),

                // Not a zone of towers - there are none left. The same masonry at its densest,
                // with the civic set pieces of the era as landmarks.
                Palette(BlockZone.Downtown, weight: 12f, maxShare: 0.3f,
                    groups: new[]
                    {
                        Terrace("Terrace 4+5 floor", 70f, TerraceStreet, TerraceRear, TerraceCorner),
                        Shops(30f, minGap: 0f, maxGap: 2f),
                    },
                    grounds: new[]
                    {
                        Ground("Concrete", 70f, concrete),
                        Ground("Asphalt", 30f, asphalt),
                    },
                    landmarks: LoadAll(Buildings, "building-bank", "building-cinema",
                                                  "building-casino", "building-hotel",
                                                  "building-museum"),
                    landmarkChance: 0.85f,
                    scatter: Merge(LoadAll(CityProps, "bench-old", "fire-hydrant", "lamp-city"),
                                   LoadAll(Props, "trash-can", "marketplace-stand-simple",
                                                  "guidepost")),
                    scatterDensity: 0.12f,
                    parkingChance: 0.06f),

                Palette(BlockZone.Industrial, weight: 12f, maxShare: 0.3f,
                    groups: new[]
                    {
                        Detached("Works", 100f, 2f, 6f, 0f, false,
                            "industry-factory-old", "industry-factory", "industry-factory-hall",
                            "industry-warehouse", "industry-storage", "industry-refinery",
                            "industry-building"),
                    },
                    grounds: new[]
                    {
                        Ground("Dirt", 50f, dirt),
                        Ground("Asphalt", 30f, asphalt),
                        Ground("Concrete", 20f, concrete),
                    },
                    scatter: Merge(
                        LoadAll(Buildings, "chimney-big", "water-tower-medium"),
                        LoadAll(Props, "timber", "palette", "package-box"),
                        LoadAll(Construction, "brick-plain-stack", "brick-concrete-stack",
                                              "planks-stack", "cement-bag-pile",
                                              "pipe-concrete-stack")),
                    scatterDensity: 0.22f,
                    parkingChance: 0.04f),

                // maxBlocks rather than a share from here down: a city has one Chinatown, one
                // hospital and one police station however large it grows. Parks and car parks
                // below keep their share cap, because a bigger city should have more of both.
                Palette(BlockZone.Chinatown, weight: 10f, maxShare: 1f, maxBlocks: 1,
                    groups: new[]
                    {
                        Detached("Chinatown", 55f, 1f, 4f, 0f, false,
                            "building-apartment-china", "building-antique-china"),

                        Detached("Chinatown shops", 45f, 1f, 3f, 0f, true,
                            "building-restuarant-china", "building-shop-china",
                            "building-market-china"),
                    },
                    landmarks: LoadAll(Buildings, "building-pagoda-china", "building-temple-china"),
                    landmarkChance: 0.9f,
                    scatter: Merge(LoadAll(Buildings, "gate-china"),
                                   LoadAll(Props, "lantern-long", "lantern-sphere",
                                                  "marketplace-stand-simple"),
                                   LoadAll(Trees, "tree-bonsai")),
                    scatterDensity: 0.14f),

                // Four blocks built round one building each. landmarkChance is 1 because the
                // landmark IS the zone - a police block with no police station is just houses.
                Palette(BlockZone.Police, weight: 14f, maxShare: 1f, maxBlocks: 1,
                    groups: new[] { Outbuildings() },
                    landmarks: LoadAll(Buildings, "building-policestation"),
                    landmarkChance: 1f,
                    scatter: Merge(cityTrees,
                                   LoadAll(Fences, "fence-classic"),
                                   LoadAll(CityProps, "lamp-city")),
                    scatterDensity: 0.12f,
                    parkingChance: 0.35f,
                    parkedCars: new[] { Vehicles("Patrol", 1f, CarsStatic, "car-police") }),

                Palette(BlockZone.Hospital, weight: 14f, maxShare: 1f, maxBlocks: 1,
                    groups: new[] { Outbuildings() },
                    landmarks: LoadAll(Buildings, "building-hospital"),
                    landmarkChance: 1f,
                    scatter: Merge(cityTrees,
                                   LoadAll(CityProps, "bench-old", "lamp-city")),
                    scatterDensity: 0.14f,
                    parkingChance: 0.35f,
                    parkedCars: new[] { Vehicles("Ambulance", 1f, CarsStatic, "car-ambulance-pickup") }),

                Palette(BlockZone.School, weight: 12f, maxShare: 1f, maxBlocks: 1,
                    groups: new[] { Outbuildings() },
                    landmarks: LoadAll(Buildings, "building-school"),
                    landmarkChance: 1f,
                    scatter: Merge(cityTrees,
                                   LoadAll(Props, "soccer-gate", "basketball-stand"),
                                   LoadAll(Fences, "fence-picket"),
                                   LoadAll(CityProps, "bench-old")),
                    scatterDensity: 0.16f,
                    parkingChance: 0.1f),

                Palette(BlockZone.FireStation, weight: 10f, maxShare: 1f, maxBlocks: 1,
                    groups: new[] { Outbuildings() },
                    landmarks: LoadAll(Buildings, "building-firestation"),
                    landmarkChance: 1f,
                    scatter: Merge(cityTrees, LoadAll(Fences, "fence-classic")),
                    scatterDensity: 0.12f,
                    parkingChance: 0.3f,
                    parkedCars: new[] { Vehicles("Appliance", 1f, CarsStatic, "firetruck") }),

                // No groups at all: a park has no street wall. The ground carries a Tile
                // component with sidewalk paths, so it is laid unscaled per cell and the
                // pedestrians walk through - see GroundPlacer.
                Palette(BlockZone.Park, weight: 8f, maxShare: 0.15f,
                    groups: System.Array.Empty<PrefabDatabase.WeightedGroup>(),
                    ground: park,
                    groundPerCell: true,
                    scatter: Merge(
                        LoadAll(Trees, "tree-oak", "tree-birch", "tree-lime", "tree-poplar",
                                       "tree-beech", "tree-fir", "tree-round",
                                       "shrub", "shrub-round"),
                        LoadAll(Props, "bench-forest", "fountain", "lantern-sphere"),
                        LoadAll(CityProps, "bench-old"),
                        LoadAll(Flowers, "roses", "carnations"),
                        LoadAll(Stones, "stone-round", "stone-flat", "stone-oval")),
                    scatterDensity: 0.5f),

                // The scatter density is up from 0.04 now that the bays register in the block's
                // occupancy list. Before, a lamp could be dropped inside a car - BuildCarRows read
                // that list and never wrote to it - so the only defence was scattering almost
                // nothing. With the bays reserved, the extra props land in the aisles and along
                // the fence, where they belong.
                Palette(BlockZone.Parking, weight: 5f, maxShare: 0.1f,
                    groups: System.Array.Empty<PrefabDatabase.WeightedGroup>(),
                    ground: asphalt,
                    carRows: true,
                    scatter: Merge(LoadAll(CityProps, "lamp-city", "bin-wheelie"),
                                   LoadAll(Props, "mail-box")),
                    scatterDensity: 0.1f,
                    fenceSegment: Load(Fences + "fence-classic.prefab"),
                    fencePost: Load(Fences + "fence-stone-tower.prefab"),
                    parkingBooth: Load(Amusement + "ticket-ride-booth.prefab")),
            };
        }

        /// <summary>
        /// The shop group mixed into the residential palettes. A fresh instance per palette,
        /// because each is serialised into the asset separately.
        ///
        /// This is what replaced a Commercial zone. A block that is nothing but cafes reads as a
        /// theme park and is not how the period built; a low weight here puts the odd storefront
        /// between the flats instead. cornerPreferred is the other half of it - the corner slot
        /// biases toward these, because the corner is where the tavern and the store went.
        /// </summary>
        static PrefabDatabase.WeightedGroup Shops(float weight, float minGap = 1f, float maxGap = 3f) =>
            Detached("Shops", weight, minGap, maxGap, 0f, true,
                "building-cafe", "building-restaurant", "building-post",
                "building-shop-china", "building-market-china");

        /// <summary>
        /// The yard buildings of a single-landmark block - a garage and a couple of low blocks,
        /// so the rest of the perimeter is not bare while the landmark holds one side.
        /// </summary>
        static PrefabDatabase.WeightedGroup Outbuildings() =>
            Detached("Outbuildings", 100f, 3f, 6f, 2f, false,
                "building-policestation-garage", "building-house-block",
                "building-house-block-big");

        /// <summary>
        /// Creates the near-white facade tints as Material Variants of atlas-LPEC.
        ///
        /// Variants rather than copies so the tints inherit the atlas textures and smoothness
        /// and keep tracking the parent - editing atlas-LPEC later does not leave four stale
        /// clones behind. They stay on the same URP/Lit shader as the parent, which is what
        /// lets the SRP Batcher keep batching across all of them.
        ///
        /// Colours are deliberately close to white. _BaseColor multiplies the entire atlas, so
        /// anything saturated tints the roof and windows too.
        /// </summary>
        static void BuildTintPalette(PrefabDatabase db)
        {
            var atlas = AssetDatabase.LoadAssetAtPath<Material>(PackMaterials + "atlas-LPEC.mat");
            if (!atlas)
            {
                Missing.Add(PackMaterials + "atlas-LPEC.mat");
                return;
            }

            db.buildingBaseMaterial = atlas;

            // Not a tint and not a variant - "58 WHITE-LPEC" is one of the pack's flat colour
            // materials, with no texture bound at all, which is exactly what the procedural
            // parking-line mesh wants. Loaded here because this is where materials are resolved.
            const string White = PackMaterials + "Colors/58 WHITE-LPEC.mat";
            db.lineMaterial = AssetDatabase.LoadAssetAtPath<Material>(White);
            if (!db.lineMaterial)
                Missing.Add(White);
            Directory.CreateDirectory(TintDir);

            db.buildingTints = new[]
            {
                Tint(atlas, "atlas-LPEC-warm", new Color(1.00f, 0.97f, 0.92f)),
                Tint(atlas, "atlas-LPEC-cool", new Color(0.94f, 0.95f, 1.00f)),
                Tint(atlas, "atlas-LPEC-rose", new Color(1.00f, 0.93f, 0.90f)),
                Tint(atlas, "atlas-LPEC-sage", new Color(0.95f, 0.97f, 0.94f)),
            };
        }

        static Material Tint(Material parent, string name, Color color)
        {
            var path = $"{TintDir}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (!material)
            {
                material = new Material(parent);
                AssetDatabase.CreateAsset(material, path);
            }

            // Assigning parent is what turns a plain material into a variant. Set it every run
            // so a material left over from an earlier copy-based version gets upgraded in place
            // rather than silently staying a detached clone.
            material.parent = parent;
            material.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(material);

            return material;
        }

        static PrefabDatabase.WeightedPrefabs Vehicles(
            string label, float weight, string folder, params string[] names) =>
            new()
            {
                label = label,
                weight = weight,
                prefabs = LoadAll(folder, names),
            };

        /// <summary>One weighted ground-slab choice. A missing tile yields an unusable entry,
        /// which PickGround skips - same failure mode as every other Load here.</summary>
        static PrefabDatabase.WeightedPrefabs Ground(string label, float weight, GameObject tile) =>
            new()
            {
                label = label,
                weight = weight,
                prefabs = tile ? new[] { tile } : System.Array.Empty<GameObject>(),
            };

        static PrefabDatabase.FacadeYawFix YawFix(string path, float extraYaw) =>
            new()
            {
                prefab = Load(path),
                extraYaw = extraYaw,
            };

        static PrefabDatabase.ZonePalette Palette(
            BlockZone zone,
            float weight,
            float maxShare,
            PrefabDatabase.WeightedGroup[] groups,
            int maxBlocks = 0,
            GameObject[] landmarks = null,
            float landmarkChance = 0f,
            GameObject[] scatter = null,
            float scatterDensity = 0f,
            GameObject ground = null,
            bool groundPerCell = false,
            PrefabDatabase.WeightedPrefabs[] grounds = null,
            GameObject[] courtyardGrounds = null,
            bool uniformStreetRuns = false,
            int maxLotsPerAxis = 0,
            float parkingChance = 0.12f,
            bool carRows = false,
            PrefabDatabase.WeightedPrefabs[] parkedCars = null,
            GameObject fenceSegment = null,
            GameObject fencePost = null,
            GameObject parkingBooth = null) =>
            new()
            {
                zone = zone,
                weight = weight,
                maxShare = maxShare,
                maxBlocks = maxBlocks,
                groups = groups,
                uniformStreetRuns = uniformStreetRuns,
                maxLotsPerAxis = maxLotsPerAxis,
                landmarks = landmarks ?? System.Array.Empty<GameObject>(),
                landmarkChance = landmarkChance,
                scatter = scatter ?? System.Array.Empty<GameObject>(),
                scatterDensity = scatterDensity,
                ground = ground,
                groundIsTilePerCell = groundPerCell,
                grounds = grounds ?? System.Array.Empty<PrefabDatabase.WeightedPrefabs>(),
                courtyardGrounds = courtyardGrounds ?? System.Array.Empty<GameObject>(),
                parkingChance = parkingChance,
                carRows = carRows,
                parkedCars = parkedCars ?? System.Array.Empty<PrefabDatabase.WeightedPrefabs>(),
                fenceSegment = fenceSegment,
                fencePost = fencePost,
                parkingBooth = parkingBooth,
            };

        /// <summary>Modular kit whose pieces abut flush, split by street / alley / corner role.</summary>
        static PrefabDatabase.WeightedGroup Terrace(
            string label, float weight, string[] street, string[] rear, string[] corner) =>
            new()
            {
                label = label,
                weight = weight,
                layout = PrefabDatabase.PieceLayout.Terrace,
                prefabs = LoadAll(Buildings, street),
                rearPrefabs = LoadAll(Buildings, rear),
                cornerPrefabs = LoadAll(Buildings, corner),
            };

        /// <summary>
        /// Standalone buildings, spaced apart. No rear or corner lists: these models are finished
        /// on all four elevations, so any of them can face an alley and none has a corner variant.
        /// </summary>
        static PrefabDatabase.WeightedGroup Detached(
            string label, float weight, float minGap, float maxGap, float maxSetback,
            bool cornerPreferred, params string[] names) =>
            new()
            {
                label = label,
                weight = weight,
                layout = PrefabDatabase.PieceLayout.Detached,
                minGap = minGap,
                maxGap = maxGap,
                maxSetback = maxSetback,
                cornerPreferred = cornerPreferred,
                prefabs = LoadAll(Buildings, names),
            };

        static GameObject[] Merge(params GameObject[][] lists) =>
            lists.SelectMany(list => list).ToArray();

        /// <summary>
        /// "ResidentialHigh 12b/8s, Park 0b/17s, ..." - buildings and scatter per zone. A zone
        /// showing 0b when it should have a street wall means its prefab names went stale, which
        /// is otherwise only visible as a suspiciously empty block in the scene.
        /// </summary>
        static string DescribeZones(PrefabDatabase.ZonePalette[] palettes) =>
            string.Join(", ", palettes.Select(p =>
                $"{p.zone} {p.groups.Sum(g => g.prefabs.Length)}b/{p.scatter.Length}s"));

        /// <summary>"12 in 3 groups (Everyday 5, Service 2, ...)" - enough to spot an empty group.</summary>
        static string Describe(PrefabDatabase.WeightedPrefabs[] groups)
        {
            var total = groups.Sum(g => g.prefabs.Length);
            var detail = string.Join(", ", groups.Select(g => $"{g.label} {g.prefabs.Length}"));
            return $"{total} in {groups.Length} groups ({detail})";
        }

        static GameObject Load(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!prefab)
                Missing.Add(path);
            return prefab;
        }

        static GameObject[] LoadAll(string folder, params string[] names) =>
            names.Select(n => Load($"{folder}{n}.prefab"))
                 .Where(p => p)
                 .ToArray();

        /// <summary>Loads every prefab in a folder, minus any excluded names.</summary>
        static GameObject[] LoadFolder(string folder, params string[] exclude)
        {
            if (!Directory.Exists(folder))
            {
                Missing.Add(folder);
                return System.Array.Empty<GameObject>();
            }

            return AssetDatabase.FindAssets("t:Prefab", new[] { folder.TrimEnd('/') })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !exclude.Contains(Path.GetFileNameWithoutExtension(p)))
                .OrderBy(p => p)
                .Select(AssetDatabase.LoadAssetAtPath<GameObject>)
                .Where(p => p)
                .ToArray();
        }

        static T GetOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset)
                return asset;

            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }
    }
}
