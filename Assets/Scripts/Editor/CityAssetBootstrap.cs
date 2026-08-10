using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
// Aliased rather than a whole `using PolyPerfect.City`: that namespace has a Path type of its
// own, which collides with System.IO.Path in LoadFolder.
using HumanBehavior = PolyPerfect.City.HumanBehavior;
using PathFinding = PolyPerfect.City.PathFinding;
using LivingCity.Data;
using LivingCity.Entities;
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
        const string SmokeDir = "Assets/Materials/Smoke";
        const string FacadeShaderPath = "Assets/Shaders/FacadeTint.shader";
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
        const string Military = Root + "Military_T/";
        const string Farm = Root + "Farm_T/";

        /// <summary>
        /// The brokenvector Low Poly Storage Pack, dropped in as it downloads. Nothing loads from
        /// here directly - see YardStock for why the props are re-authored first.
        /// </summary>
        const string StoragePack = "Assets/brokenvector/Low Poly Storage Pack/";

        /// <summary>
        /// Where the re-authored yard stock is written, and the only place the palette reads it
        /// from. Deliberately outside the vendor folder, for both the reasons AuthoredPeople
        /// gives: a pack update is free to overwrite its own folder, and the pack's prefabs are
        /// not usable as they ship - they carry their own Built-in-RP shaders, which under this
        /// project's URP Forward+ renderer draw magenta. See AuthorYardStock.
        /// </summary>
        const string YardStock = ConfigDir + "/YardStock/";

        /// <summary>
        /// The no-border ground tiles. Same surfaces as Tiles_T, minus the kerb drawn round the
        /// edge - which is what you want for anything laid in adjoining rectangles, because two
        /// bordered tiles meeting show a seam down the join.
        /// </summary>
        const string NoBorder = Root + "Tiles-NB_T/";

        /// <summary>
        /// Only one thing is taken from here: ticket-ride-booth. It is a plain 2.24 x 2.80 x 1.71
        /// kiosk, the pack has no parking booth of its own, and nothing about it says fairground
        /// once it is standing at the gate of a car park.
        /// </summary>
        const string Amusement = Root + "Amusement Park_T/";
        const string Trees = Root + "Nature_T/Trees_T/";
        const string Flowers = Root + "Nature_T/Flowers_T/";
        const string Grass = Root + "Nature_T/Grass_T/";
        const string Stones = Root + "Nature_T/Stones_T/";
        const string Clouds = Root + "Nature_T/Clouds_T/";
        const string Vehicles_ = Root + "Vehicles_T/";
        const string CarsAI = Root + "Vehicles_T/Cars_AI_T/";
        const string CarsStatic = Root + "Vehicles_T/Cars_T/";
        const string Boats = Root + "Vehicles_T/Boats_T/";
        const string Pier = Root + "Props_T/Pier_T/";

        /// <summary>
        /// Only the working-waterside pieces are taken from here - buoys, the lifebuoy. The
        /// rest of the folder is deckchairs and sandcastles, holiday kit a working port has
        /// no use for.
        /// </summary>
        const string Beach = Root + "Beach_T/";
        const string PeopleAI = Root + "People_T/People_AI_T/";

        /// <summary>
        /// The Animated People pack, whose cast is four times the size of Epic City's and is the
        /// only source in the project for a business suit, an overcoat or a tailcoat. Its prefabs
        /// are NOT usable as they ship - see AuthorPedestrians.
        /// </summary>
        const string AnimatedPeople = "Assets/polyperfect/Low Poly Animated People/- Prefabs/";

        /// <summary>
        /// Where the converted Animated People walkers are written. Deliberately outside
        /// People_AI_T: that folder is globbed wholesale, so anything dropped in it becomes a
        /// civilian automatically, and the same rule is what keeps the (unbuilt) police officer
        /// out of the crowd. See PrefabDatabase.policeOfficerPrefab.
        /// </summary>
        const string AuthoredPeople = ConfigDir + "/People/";

        /// <summary>
        /// Who does NOT belong on a Chicago pavement in the 1920s.
        ///
        /// Same period rule the zone palettes enforce on buildings, applied to the cast for the
        /// first time. Epic City ships its people for a generic contemporary city with a beach
        /// and a theme park attached, and the folder glob took all of them: two samurai, a geisha,
        /// three swimmers, a lifeguard, three beachgoers, a man in Rastafarian colours and a
        /// metalhead in a band shirt - eleven of twenty-five, every one of them either off-period
        /// or off-continent.
        ///
        /// What survives is deliberately wider than "gangsters": a street of nothing but suits
        /// reads as a cutscene. man-golf stays because the pack's golf model is a flat cap and a
        /// sleeveless vest, which is period menswear rather than sportswear; man-soldier stays as
        /// a demobbed veteran, and the farm pair as the poorer end of the working class.
        /// </summary>
        static readonly string[] OffPeriodPedestrians =
        {
            "boy-beach_AI", "girl-beach_AI", "man-beach_AI", "man-jamaica_AI",
            "man-metalhead_AI", "man-samurai_AI", "man-samurai-black_AI",
            "man-swimming_AI", "woman-geisha_AI", "woman-swimming_AI",
            "woman-swimming-guard_AI",
        };

        /// <summary>
        /// Animated People models worth converting into city walkers, by source prefab name.
        ///
        /// Chosen for SILHOUETTE, because that is all the packs give you: clothing here is
        /// geometry plus a UV pointing at a flat colour swatch in a shared palette atlas, so
        /// there is no texture to re-dress anyone with and no material to tint. business and
        /// butler carry the suit shapes Epic City has exactly two of; coat_winter is the only
        /// long-coat body in either pack; judge reads at street distance as a full-length dark
        /// overcoat. punk and homeless are in for the same reason man-golf is - a crowd needs a
        /// bottom end, and both silhouettes are unremarkable enough to pass once they are not
        /// the thing you are looking at.
        ///
        /// Cost of admission is one extra material: this pack draws from atlas-LPAP, Epic City
        /// from atlas-LPEC, so the crowd is two batches from here on.
        /// </summary>
        static readonly string[] AnimatedPeopleWalkers =
        {
            "man_business", "woman_business",
            "man_coat_winter", "woman_coat_winter",
            "man_butler", "man_judge",
            "man_punk", "woman_punk",
            "man_homeless", "woman_homeless",

            // The children. Epic City has exactly one child left once the beach models are
            // gone, and this pack has six on a shared rig - so unlike every name above, these
            // are here to fix a HOLE rather than to widen a silhouette range. They carry both
            // the crowd's Children group and the school run's roster; the school run is what
            // made the hole worth fixing, because a bus full of the same boy is worse than the
            // pavement version of the same problem.
            "boy_casual_cap", "boy_coat_winter", "boy-large",
            "girl_casual_shorts", "girl_coat_winter", "girl-large",

            // The beat officer - authored through the same conversion but NEVER in the crowd:
            // BuildPedestrianGroups draws from AuthoredPeople by name and does not list him,
            // so the only route onto the street is PoliceDirector via policeOfficerPrefab.
            // Male model only, per design - woman_police is referenced nowhere.
            "man_police",
        };

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

        /// <summary>
        /// What stands in a back alley. Still deliberately short - bins, a dumpster, a bench, the
        /// odd tree - but no longer deliberately light: the alley is where a neglected city puts
        /// its rubbish, so this kit is weighted to it and BlockBuilder fills half the cells.
        ///
        /// The garage, crates, pallets, timber and yard wall that used to be in here are gone.
        /// They are works-yard furniture, and behind a block of flats they read as a builder's
        /// merchant rather than as an alley - which is also why the alley no longer builds ROWS
        /// of anything: the buildings of the interior lots are the fill now, and this is only
        /// what is dropped on the tarmac between them. See BlockBuilder.BuildInterior.
        ///
        /// The refuse props are listed several times on purpose: ShuffleBag deals each entry
        /// once per cycle, so a duplicate is how a kit weights one prop above the others. Eight
        /// of the eleven entries are now bins - up from five of eight - and BlockBuilder's
        /// AlleyPropChance went 0.32 -> 0.50 with it, which is what actually doubles the
        /// rubbish (0.32 x 0.63 = 0.20 per cell against 0.50 x 0.73 = 0.36). The bench and the
        /// two plants come out of that arithmetic roughly where they were (0.12 -> 0.14): the
        /// alley gets dirtier, not more crowded with everything.
        /// </summary>
        static GameObject[] AlleyKit() =>
            Merge(LoadAll(CityProps, "dumpster", "dumpster",
                                     "bin-wheelie", "bin-wheelie", "bin-wheelie",
                                     "bench-old"),
                  LoadAll(Props, "trash-can", "trash-can", "trash-can"),
                  LoadAll(Trees, "tree-lime", "shrub-round"));

        /// <summary>
        /// What furnishes a pocket-park feature strip: seating first - benches twice, so the
        /// bag deals them most often - then shade. No bins and no dumpster; this is
        /// the block's front, not its back.
        /// </summary>
        static GameObject[] PocketParkKit() =>
            Merge(LoadAll(CityProps, "bench-old", "bench-old"),
                  LoadAll(Trees, "tree-lime", "tree-oak", "shrub-round"));

        /// <summary>
        /// The trafika. hot-dog-stand and marketplace-stand-simple are the pack's two pieces
        /// that read as a street kiosk in 1920 - everything else in that register belongs to
        /// the amusement park set and says so.
        /// </summary>
        static GameObject[] KioskKit() =>
            Merge(LoadAll(CityProps, "hot-dog-stand"),
                  LoadAll(Props, "marketplace-stand-simple"));

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

        /// <summary>
        /// Names the layer PedestrianSpawner assigns to every spawned pedestrian. Purely
        /// cosmetic - the physics matrix keys on the index and works unnamed - but an
        /// anonymous layer in the Inspector reads as an accident. Through the editor API
        /// rather than a file edit because the open Editor rewrites TagManager from memory.
        /// </summary>
        static void NamePedestrianLayer()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0)
                return;

            var tagManager = new SerializedObject(assets[0]);
            var slot = tagManager.FindProperty("layers")
                .GetArrayElementAtIndex(PedestrianSpawner.PedestrianLayer);
            if (string.IsNullOrEmpty(slot.stringValue))
            {
                slot.stringValue = "Pedestrians";
                tagManager.ApplyModifiedProperties();
            }
        }

        [MenuItem("Tools/City/Create or Refresh Config Assets")]
        public static void CreateAssets()
        {
            Missing.Clear();
            Directory.CreateDirectory(ConfigDir);

            var config = GetOrCreate<CityConfig>($"{ConfigDir}/CityConfig.asset");
            var db = GetOrCreate<PrefabDatabase>($"{ConfigDir}/PrefabDatabase.asset");

            // Created here so it cannot end up like PerformanceConfig - a ScriptableObject with
            // a CreateAssetMenu that nothing ever makes, nothing references and nobody notices
            // is missing. Its defaults come from the class, and GetOrCreate leaves an existing
            // asset alone, so a yard already tuned in the inspector survives every refresh.
            var lots = GetOrCreate<IndustrialLotConfig>($"{ConfigDir}/IndustrialLotConfig.asset");

            // Same contract for the park's knobs.
            GetOrCreate<ParkConfig>($"{ConfigDir}/ParkConfig.asset");

            // CityConfig.asset survives every refresh with its serialized values intact -
            // GetOrCreate never rewrites an existing asset - so a default changed in
            // CityConfig.cs alone never reaches a project that already has the asset. The
            // layout bounds are design decisions and get stamped here like the palettes;
            // seed, fill ratio, grid size and the entity counts are the user's knobs and
            // are left alone.
            config.minArterialSpacing = 2;
            config.maxArterialSpacing = 4;

            NamePedestrianLayer();

            // Road tiles. tileShape values verified in the prefab files:
            // straight=Straight, curve=Turn, intersection-t=T, intersection=Cross, end=End.
            db.straight = Load(Roads + "tile-road-straight.prefab");
            db.curve = Load(Roads + "tile-road-curve.prefab");
            db.tJunction = Load(Roads + "tile-road-intersection-t.prefab");
            db.cross = Load(Roads + "tile-road-intersection.prefab");
            db.end = Load(Roads + "tile-road-end.prefab");
            db.straightCrosswalk = Load(Roads + "tile-road-straight-crosswalk.prefab");

            // The dual carriageway. Same three tileShapes as the minor road above - verified in
            // the prefab files the same way - so RoadTileTable reuses the same rotations.
            //
            // Which arms carry four lanes was measured, not read off the file name, because the
            // names do not say: tile-road-mainroad-intersection puts the carriageway North-South
            // while tile-road-mainroad-intersection-t puts it East-WEST, with its odd arm being
            // the minor branch. The pack also ships mainroad curves, tapers and three-way
            // mainroad junctions; none is wired up because CityGenerator.Subdivide proves the
            // boulevard can never need one.
            db.mainStraight = Load(Roads + "tile-mainroad-straight.prefab");
            db.mainStraightCrosswalk = Load(Roads + "tile-mainroad-straight-crosswalk.prefab");
            db.mainCross = Load(Roads + "tile-road-mainroad-intersection.prefab");
            db.mainTJunction = Load(Roads + "tile-road-mainroad-intersection-t.prefab");

            db.groundTile = Load(Root + "Tiles_T/tile-plain_concrete.prefab");
            db.trafficLights = Load(Traffic + "traffic-lights_AI.prefab");
            db.mainTrafficLights = Load(Traffic + "traffic-lights-big_AI.prefab");

            // Before the palettes, not after: the Industrial entry reads YardStock through
            // YardKit, and unlike AuthorPedestrians - whose only reader is two hundred lines
            // below - this output has to exist by the time the palette is built.
            AuthorYardStock();

            db.zonePalettes = BuildZonePalettes();

            db.facadeYawFixes = BuildFacadeYawFixes();

            db.chimneyVents = BuildChimneyVents();

            // The post office and the fire station are storefronts in the residential Shops group,
            // not the landmarks of zones, so nothing in the zoning can reach them: every block in
            // the city is free to build another, and the per-lot bag that stops repeats has no
            // memory across lots. This list is the entire cap for all three. The police station
            // joined them when its zone went: it is ResidentialHigh's landmark now, and with
            // maxBlocks no longer capping it, PickLandmark's IsSpent test against this list is
            // the only thing keeping the city to one station. Hospital and school are still not
            // here - they are one block each by quota already - and adding them would change
            // nothing but the reason.
            // The bank is here for a reason none of the others have: it is the one building the
            // city reaches for down TWO paths - ResidentialHigh's landmark bag and its own Bank
            // zone - and ZonePlanner's route roll picks between them precisely so that only one
            // fires. This list is the belt to that braces: if the roll is ever bypassed, or a
            // seed finds a corner of it nobody thought about, the city still ends up with one
            // bank rather than two.
            // The gun shop joins by the post office's exact reasoning: a storefront in the
            // Shops bag that nothing in the zoning can reach, so this list is its entire cap.
            // A ceiling, not a promise - a seed may come up without one, per the user's call.
            db.uniqueBuildings = LoadAll(Buildings, "building-post", "building-firestation",
                                                    "building-policestation", "building-bank",
                                                    "building-shop-china");

            // Only the double lantern. lamp-road is a motorway lantern on a plain steel pole,
            // and lamp-city is out of the city entirely - dropped from the streets first and
            // then from every scatter bag too, so the double lantern is the one lamp there is.
            db.streetLamps = LoadAll(CityProps, "lamp-road-double");
            db.trees = LoadAll(Trees, "tree-oak", "tree-birch", "tree-lime", "tree-round",
                                      "tree-poplar", "shrub", "shrub-round");

            // Period sweep of the street furniture: the bus shelter is a glass canopy, the cycle
            // stand and the cash machine are plainly modern. Post box, lantern and guidepost take
            // their place, and the hot dog stand stays because Chicago.
            //
            // The three refuse props are listed more than once on purpose. StreetPropPlacer.Pick
            // draws uniformly from this array, so a duplicate is how the bag is weighted - the
            // same idiom AlleyKit uses on ShuffleBag. Seven refuse entries against seven of
            // everything else puts the bag at half rubbish, and StreetPropPlacer's slot chance
            // went 0.18 -> 0.24 alongside it so that the hydrants, lanterns, post boxes and
            // benches keep the frequency they had (0.18 x 0.70 = 0.126, 0.24 x 0.50 = 0.12) and
            // only the rubbish doubles. A kerbside dumpster is deliberate: this city is meant to
            // read as neglected, so the front-of-block rule the pocket park keeps does not apply
            // to the street itself.
            db.smallProps = Merge(
                LoadAll(CityProps, "bench-old", "bin-wheelie", "bin-wheelie",
                                   "dumpster", "dumpster",
                                   "fire-hydrant", "hot-dog-stand"),
                LoadAll(Props, "mail-box", "trash-can", "trash-can", "trash-can",
                               "lantern-long", "guidepost"),
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
            // car-camper-vintage (7.29 x 2.54) was dropped rather than left as a silent reject.
            // Since kerbside parking went, every parked vehicle in the city comes out of
            // BlockBuilder.FillStalls, which caps at ParkingLayout.StallDepth (5.6) by
            // StallWidth - 0.3 (2.4) - a marked bay, whether a street bay or a whole car park.
            // There is no longer a second, laxer limit for a camper to fall back on.
            //
            // Measured off the binary FBX vertex arrays (the parser calibrates on this file's own
            // two recorded numbers, 7.29 and 6.25), as length x width in metres. What still does
            // not fit the 5.6 x 2.4 bay, and is therefore rejected and re-rolled by
            // VehiclePicker.Fits every time it is drawn:
            //
            //   car-pickup-modern     5.70 x 2.32   Everyday, 1 of 7
            //   truck                 6.25 x 2.26   Trade, 1 of 3
            //   armored-truck         5.85 x 2.29   Trade, 1 of 3
            //   car-ambulance-pickup  6.24 x 2.43   Hospital zone override - never appears
            //
            // Left in deliberately, unlike the camper: each is a body the city wants and the
            // honest fix is a deeper bay for them, not a shorter list. Note the hospital's
            // override produces NOTHING at all today - its group has no other member - so the
            // hospital has an empty forecourt. Do not "fix" that by raising StallDepth: 5.6m is
            // the marked bay, and widening it moves every painted line. The firetruck (8.54 x
            // 3.02) was the other such override and is referenced nowhere now that the fire
            // station is not a zone.
            //
            // Still out, and why: golf-cart and car-baywatch are not road cars a resident owns;
            // the buses are 9.8-11.3m and cannot fit either limit, so they are AI-only below;
            // the bumper cars, formula car, excavator, bulldozer, crane and road roller are what
            // globbing this folder used to put outside a flat.
            db.parkedCarGroups = new[]
            {
                Bucket("Everyday", 70f, CarsStatic,
                         "car-veteran", "car-passenger", "car-passenger-race", "car-hippie-van",
                         "jeep-open", "car-pickup-modern", "car-caravan-small"),

                // A taxi waits at a kerb as often as it drives, and it is already accepted in
                // traffic below. The police car is not here on purpose: parked, it is studied,
                // and outside the station's own forecourt - ResidentialHigh's landmarkCars
                // override - it would read as a mistake rather than as a beat car.
                Bucket("Service", 8f, CarsStatic, "car-taxi"),

                // The armoured van is the one addition the period actively wants: a bank car is
                // the whole reason this city has a police station in it.
                Bucket("Trade", 22f, CarsStatic, "truck", "car-tow-truck", "armored-truck"),
            };

            // The camper stays in Everyday - pulling it out would resize the bag, and the bags
            // draw from BlockBuilder's shared Buildings stream, so any length change re-lays the
            // whole city (the paintableVehicles constraint). Instead RareVehicleFilter swaps
            // ~9 in 10 of its deals for another Everyday body, on its own stream
            // (SeedOffsets.RareVehicles), which takes it from a guaranteed bag seat (~12% of
            // placed cars) down to roughly one camper in eighty.
            db.rareVehicleNames = new[] { "car-caravan-small" };
            db.rareVehicleKeepChance = 0.1f;

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
                Bucket("Everyday", 66f, CarsAI,
                         "car-veteran_AI", "car-passenger_AI", "car-passenger-race_AI",
                         "car-hippie-van_AI", "jeep-open_AI"),

                // The taxi is a modern shell, but a city street of the era did have taxis and
                // there is no vintage stand-in in the pack. Kept at a low weight, and moving,
                // where the silhouette is read for a moment rather than studied at the kerb.
                // car-police_AI left this bucket when the police became real: the only police
                // cars in the city are the station's patrol fleet (PoliceDirector), which
                // actually returns to base - a random one dissolving at the map edge next to
                // them would read as a bug. See PrefabDatabase.policeCarPrefab.
                Bucket("Service", 14f, CarsAI, "car-taxi_AI"),

                Bucket("Freight", 14f, CarsAI, "truck_AI", "car-tow-truck_AI",
                         "armored-truck_AI"),

                // There is deliberately no bus group here, and there used to be: a "Transit"
                // bucket carrying bus-passenger_AI at 6/100. It was dropped because at 11.28m it
                // was by far the longest thing the pack's CarBehavior had to steer through a
                // junction - the corner-clipping suspect the comment here always named first.
                // Traffic never rejected it (VehicleSpawner calls picker.Next() with no size
                // limits, VehicleSpawner.cs:176, so unlike the kerb and the car parks there is no
                // length ceiling); it simply was not worth the turn.
                //
                // So the only bus in the city is the school bus, and it is not in this list
                // either - it is db.schoolBusPrefab, driven only by SchoolBusDirector, only
                // between the school and its stops, and only with children getting on and off it.
                // Same arrangement as the police car: out of the buckets, owned by a director.
                // That was once an exception carved out of a fleet that had another bus; now it
                // is simply how the one bus works.
                //
                // The weights above therefore total 94, not 100, and that is intended. Vehicle-
                // Picker sums totalWeight over the groups it is actually given (VehiclePicker.cs
                // :47), so the three renormalise themselves to 70.2 / 14.9 / 14.9 and keep the
                // ratios they were tuned at. Topping Everyday back up to 72 would move that mix
                // for no reason - leave the numbers alone.
            };

            // Kept as the flat fallback list PedestrianSpawner drops back to when the weighted
            // groups below are empty - an old scene whose PrefabDatabase predates this change
            // should still populate its pavements rather than log and give up.
            db.aiPedestrians = LoadFolder(PeopleAI, OffPeriodPedestrians);

            AuthorPedestrians();
            db.pedestrianGroups = BuildPedestrianGroups();

            // The police: the fleet's car is the pack's own AI police car (now absent from
            // every traffic bucket above - the patrol fleet is the only source of one), the
            // officer the authored man_police conversion that AuthorPedestrians just wrote.
            db.policeCarPrefab = Load(CarsAI + "car-police_AI.prefab");
            db.policeOfficerPrefab = Load(AuthoredPeople + "man_police_AI.prefab");

            // The school run. The bus is the pack's own AI school bus, absent from every
            // traffic bucket above for the same reason the police car is. The roster draws
            // from the same seven child models the crowd's Children group does - see
            // BuildPedestrianGroups for why that list is now seven rather than one.
            db.schoolBusPrefab = Load(CarsAI + "bus-school_AI.prefab");
            db.schoolChildPrefabs = Children();

            // Which cars VehicleTinter may repaint. Static and _AI forms of the same model both
            // appear, because the same body is parked by BlockBuilder and driven by
            // VehicleSpawner and both should vary.
            //
            // The list is short for a measured reason, not a cautious one. A tint MULTIPLIES the
            // atlas, so it can only darken, and that is only useful where the body swatch is
            // neutral. Sampled off atlas-albedo-LPEC.png through each model's UVs, area-weighted:
            //
            //   PAINTABLE                       LEAVE ALONE
            //   car-passenger      #dbdbda 35%  car-taxi           #fdcf24 yellow
            //   car-caravan-small  #dbdbda 37%  car-police         #384878 navy
            //   car-tow-truck      #dbdbda 15%  bus-school         #fdcf24 yellow
            //   car-veteran        #878282 45%  car-passenger-race #fc7d20 orange
            //   car-pickup-modern  #878282 36%  car-hippie-van     #35ccdd cyan
            //   armored-truck      #878282 49%  jeep-open          #cd9b60 tan
            //   truck              #c1bcb9 37%
            //
            // bus-passenger was measured here too, at #dbdbda 43%, and is gone with the Transit
            // bucket: nothing instantiates it any more, so an entry for it would only be a name
            // the tinter can never match.
            //
            // The right-hand column is excluded by design, not by oversight: yellow times blue is
            // dark green, and a repainted taxi is a taxi with the wrong livery. The police car and
            // the school bus are absent for that reason AND because they are a director's own
            // vehicle - the same arrangement that keeps them out of the traffic buckets.
            //
            // Two of the paintable models exist in one form only, and that is the pack's doing:
            // Cars_AI_T ships no _AI variant for car-caravan-small or car-pickup-modern, so they
            // are parked-only. Naming the missing halves here would land in the Missing warning
            // below.
            db.paintableVehicles = Merge(
                LoadAll(CarsStatic, "car-passenger", "car-veteran", "car-tow-truck",
                                    "armored-truck", "truck", "car-caravan-small",
                                    "car-pickup-modern"),
                LoadAll(CarsAI, "car-passenger_AI", "car-veteran_AI", "car-tow-truck_AI",
                                "armored-truck_AI", "truck_AI"));

            // The port's shift draws the same worker models the crowd's Workers group uses -
            // a docker on the quay and a labourer on the pavement are the same city. Nothing
            // new is authored for this; both prefabs are crowd-proven Epic City AI rigs.
            db.dockWorkerPrefabs = LoadAll(PeopleAI, "man-worker_AI", "man-construction-worker_AI");

            db.pedestrianController = BuildPedestrianController();

            BuildTintPalette(db);

            EditorUtility.SetDirty(config);
            EditorUtility.SetDirty(db);
            EditorUtility.SetDirty(lots);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (Missing.Count > 0)
                Debug.LogWarning($"[CityAssetBootstrap] {Missing.Count} prefab(s) not found:\n - " +
                                 string.Join("\n - ", Missing));

            Debug.Log($"[CityAssetBootstrap] Config assets ready in {ConfigDir}.\n" +
                      $"Zones: {DescribeZones(db.zonePalettes)}\n" +
                      $"AI cars: {Describe(db.aiCarGroups)}, parked cars: {Describe(db.parkedCarGroups)}, " +
                      $"pedestrians: {Describe(db.pedestrianGroups)}, clouds: {db.clouds.Length}.");

            Selection.activeObject = db;
        }

        /// <summary>
        /// The one prefab every authored walker is measured against, so the kit below is COPIED
        /// off a working example rather than guessed at. If the pack ever changes it, the
        /// authored people change with it.
        /// </summary>
        const string PedestrianReference = PeopleAI + "man-mafia_AI.prefab";

        /// <summary>
        /// Converts the Animated People models into walkers Epic City's AI can drive.
        ///
        /// The two packs disagree about who moves the character. Epic City's _AI prefabs carry
        /// HumanBehavior + PathFinding, which walk the transform along a checkpoint route and
        /// need a kinematic Rigidbody and a capsule to trip the crosswalk triggers. Animated
        /// People ships a NavMeshAgent plus a wander script instead, and there is no baked
        /// NavMesh in the generated city for it to stand on - dropped in as-is, the character
        /// simply never moves.
        ///
        /// The subtle half of the conversion is applyRootMotion. Animated People authors it ON
        /// (their locomotion clips carry the displacement), Epic City OFF. Left on, the walker
        /// gets moved twice per frame - once by the clip, once by HumanBehavior - and slides off
        /// its route. This is the single line most likely to be the cause if authored people
        /// drift and Epic City's do not.
        ///
        /// Rebuilt on every run, same overwrite discipline as the animator controller: the
        /// output is generated, so hand-edits in the inspector do not survive and belong here.
        /// </summary>
        /// <summary>
        /// Who does NOT belong in a 1920s works yard, by name fragment.
        ///
        /// The same period rule the zone palettes enforce on buildings and OffPeriodPedestrians
        /// enforces on the cast, applied to the storage pack. The containers are the one the
        /// header already calls out by name - containerisation starts in 1956 - and the pallet is
        /// the same mistake thirty years later still: the EPAL standard is 1961, and Props_T's
        /// `palette` is already in stackProps anyway, so importing a second one defends a
        /// marginal case at the cost of a duplicate.
        ///
        /// The rest are not period-wrong so much as REGISTER-wrong. A suitcase in a factory yard
        /// reads as somebody's mistake rather than as a works, and there is no reading of an amp
        /// rack, a giftbox or a treasure chest that survives the walk past it.
        /// </summary>
        static readonly string[] OffPeriodYardStock =
        {
            "container", "pallet", "amp", "gift", "treasure", "chest", "suitcase", "trashcan",
        };

        /// <summary>
        /// Which of the four palette bags a storage-pack model lands in, by name fragment.
        ///
        /// Ordered, and first match wins - which is load bearing rather than incidental. The
        /// pack's electric box would otherwise be a crate, on the strength of the word "box",
        /// and would then be laid in ranks on a loading apron instead of stood against the wall
        /// of a boiler house. Anything that matches nothing is reported and skipped rather than
        /// swept into a default bag: an unclassified model is a name this table has not been
        /// told about, and silently filing it as a crate is how it stops being visible.
        /// </summary>
        static readonly (string Role, string[] Match)[] YardStockRoles =
        {
            ("barrel",  new[] { "barrel" }),
            ("fixture", new[] { "rack", "locker", "cabinet", "electric", "garbage", "can" }),
            ("crate",   new[] { "crate", "box", "basket" }),
            ("sack",    new[] { "bag", "sack" }),
        };

        /// <summary>
        /// Turns the brokenvector Low Poly Storage Pack into prefabs this project can place.
        ///
        /// The same three problems AuthorPedestrians solves, and one more. The pack's models
        /// carry its own materials on its own custom shaders, which are Built-in-RP and draw
        /// MAGENTA under this project's URP Forward+ renderer - the same failure SmokeMaterial
        /// documents, and one that reads as a bug in the generator rather than in a material. So
        /// nothing the pack ships is referenced: the geometry is re-parented onto a prefab of
        /// ours, wearing a material of ours.
        ///
        /// Rebuilt every run, same overwrite discipline as the animator controller and the
        /// authored walkers: the output is generated, so re-downloading the pack costs one menu
        /// click and hand-edits in the inspector do not survive.
        ///
        /// Silent no-op when the pack is not on disk. This has to stay true - the whole yard
        /// stock has Epic City fallbacks behind it precisely so a project without the pack builds
        /// the same city with less variety, rather than a different one.
        ///
        /// MUST run before BuildZonePalettes, unlike AuthorPedestrians, which gets away with
        /// being called two hundred lines later because only BuildPedestrianGroups reads its
        /// output. The Industrial palette reads YardStock, and the palettes are built first.
        /// </summary>
        static void AuthorYardStock()
        {
            if (!Directory.Exists(StoragePack))
                return;

            var material = YardStockMaterial();
            if (!material)
                return;

            // Rebuilt rather than merged, so a model dropped from the pack disappears from the
            // palette instead of lingering as a prefab nothing re-authors.
            if (Directory.Exists(YardStock))
                AssetDatabase.DeleteAsset(YardStock.TrimEnd('/'));

            Directory.CreateDirectory(YardStock);

            var kept = new List<string>();
            var skipped = new List<string>();
            var unclassified = new List<string>();

            var models = AssetDatabase.FindAssets("t:Model", new[] { StoragePack.TrimEnd('/') })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(p => p)
                .ToArray();

            foreach (var path in models)
            {
                var name = Path.GetFileNameWithoutExtension(path);

                // Compared with every separator stripped, because a pack is free to call the same
                // model "Electric Box", "electric_box" or "ElectricBox" and all three have to hit
                // the same row of the table.
                var key = name.ToLowerInvariant()
                              .Replace(" ", "").Replace("_", "").Replace("-", "");

                if (OffPeriodYardStock.Any(bad => key.Contains(bad)))
                {
                    skipped.Add(name);
                    continue;
                }

                var role = YardStockRoles
                    .FirstOrDefault(r => r.Match.Any(m => key.Contains(m)))
                    .Role;

                if (string.IsNullOrEmpty(role))
                {
                    unclassified.Add(name);
                    continue;
                }

                var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (!source)
                    continue;

                if (Author(source, material, $"{YardStock}{role}-{Slug(name)}.prefab"))
                    kept.Add($"{role}-{Slug(name)}");
            }

            AssetDatabase.SaveAssets();

            Debug.Log($"[CityAssetBootstrap] Yard stock: authored {kept.Count} of {models.Length} " +
                      $"storage-pack models -> {YardStock}\n" +
                      $"  kept: {string.Join(", ", kept)}\n" +
                      $"  off-period or off-register: {string.Join(", ", skipped)}");

            // Separately, and as a warning, because this one is actionable: it means the pack
            // ships a model whose name YardStockRoles has never been shown. Filing it by guess
            // would put it in a yard at the wrong end of the compound.
            if (unclassified.Count > 0)
                Debug.LogWarning($"[CityAssetBootstrap] Yard stock: {unclassified.Count} model(s) " +
                                 $"matched no role and were skipped - add a fragment to " +
                                 $"YardStockRoles if they belong in a yard: " +
                                 $"{string.Join(", ", unclassified)}");
        }

        /// <summary>
        /// One model, saved as a standalone prefab wearing the project's own material.
        ///
        /// Unpacked completely rather than saved as a variant, for the reason AuthorPedestrians
        /// gives: a variant inherits its materials back from the pack the moment the pack is
        /// updated, which is exactly the link this method exists to cut.
        /// </summary>
        static bool Author(GameObject source, Material material, string path)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            if (!instance)
                return false;

            PrefabUtility.UnpackPrefabInstance(
                instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            // Anything the pack put on it. A storage prop is geometry and nothing else; a script
            // this assembly cannot even name would be a null MonoBehaviour in the saved prefab.
            foreach (var script in instance.GetComponentsInChildren<MonoBehaviour>(true))
                if (script)
                    Object.DestroyImmediate(script);

            // Colliders too. Nothing in a yard is walked into - the props stand on ground the
            // pedestrians have no route across - and a few thousand stray colliders is a physics
            // bill for nothing. See the crowd-scale work on layer costs.
            foreach (var collider in instance.GetComponentsInChildren<Collider>(true))
                Object.DestroyImmediate(collider);

            // Every slot, not just the first: a model authored with two submeshes keeps its
            // second material otherwise, and that second material is the magenta one.
            foreach (var renderer in instance.GetComponentsInChildren<MeshRenderer>(true))
            {
                var slots = new Material[renderer.sharedMaterials.Length];
                for (var i = 0; i < slots.Length; i++)
                    slots[i] = material;

                renderer.sharedMaterials = slots;
            }

            var saved = PrefabUtility.SaveAsPrefabAsset(instance, path);
            Object.DestroyImmediate(instance);

            return saved;
        }

        /// <summary>
        /// The one material every authored yard prop wears.
        ///
        /// Built from atlas-LPEC's SHADER rather than from a shader looked up by name, so it
        /// cannot disagree with what the rest of the city draws with and cannot be broken by a
        /// URP upgrade renaming a path. The map is the pack's own palette texture, which is the
        /// same trick atlas-LPEC uses - colour baked into UVs against a small swatch sheet - and
        /// is why one material covers the whole set.
        ///
        /// The importer settings are not decoration. A palette texture read with bilinear
        /// filtering bleeds each swatch into its neighbours along every UV island edge, and on
        /// flat-shaded low-poly props that shows up as a thin wrong-coloured rim on every face.
        /// Point filtering with no mip chain is the fix, and it has to be applied here rather
        /// than by hand or a reimport silently undoes it.
        /// </summary>
        static Material YardStockMaterial()
        {
            var atlas = AssetDatabase.LoadAssetAtPath<Material>(PackMaterials + "atlas-LPEC.mat");
            if (!atlas)
            {
                Missing.Add(PackMaterials + "atlas-LPEC.mat");
                return null;
            }

            var texturePath = AssetDatabase
                .FindAssets("t:Texture2D", new[] { StoragePack.TrimEnd('/') })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(p => p)
                .FirstOrDefault();

            const string Dir = "Assets/Materials/YardStock";
            const string Path_ = Dir + "/atlas-LPSP.mat";

            Directory.CreateDirectory(Dir);

            var material = AssetDatabase.LoadAssetAtPath<Material>(Path_);
            if (!material)
            {
                material = new Material(atlas.shader);
                AssetDatabase.CreateAsset(material, Path_);
            }

            material.shader = atlas.shader;
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", 0f);
            material.SetFloat("_Metallic", 0f);

            if (!string.IsNullOrEmpty(texturePath))
            {
                if (AssetImporter.GetAtPath(texturePath) is TextureImporter importer &&
                    (importer.filterMode != FilterMode.Point || importer.mipmapEnabled))
                {
                    importer.filterMode = FilterMode.Point;
                    importer.mipmapEnabled = false;
                    importer.SaveAndReimport();
                }

                material.SetTexture(
                    "_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>Pack name to this project's lowercase-hyphen convention.</summary>
        static string Slug(string name) =>
            name.Trim().ToLowerInvariant().Replace(" ", "-").Replace("_", "-");

        static void AuthorPedestrians()
        {
            var reference = Load(PedestrianReference);
            if (!reference)
                return;

            // The pack splits these across two objects: the Rigidbody, HumanBehavior and
            // PathFinding sit on the prefab root, but the capsule is one level down on the
            // _Rig child that carries the SkinnedMeshRenderer. So this one lookup has to
            // descend and the other two must not. A root-only GetComponent here returns null
            // and aborts the whole pass at the return below - one line before the output
            // folder is created - and the only visible symptom is that police officers never
            // spawn, because policeOfficerPrefab is the one field with no pack fallback.
            var referenceCapsule = reference.GetComponentInChildren<CapsuleCollider>(true);
            var referenceBody = reference.GetComponent<Rigidbody>();
            var referenceBehavior = reference.GetComponent<HumanBehavior>();
            if (!referenceCapsule || !referenceBody || !referenceBehavior)
            {
                // Named individually: the old message listed all three whatever was actually
                // missing, which is why five bootstrap runs reported this failure without
                // anyone being able to see that only the capsule lookup was at fault.
                var absent = new List<string>();
                if (!referenceCapsule) absent.Add("CapsuleCollider");
                if (!referenceBody) absent.Add("Rigidbody");
                if (!referenceBehavior) absent.Add("HumanBehavior");
                Missing.Add($"{PedestrianReference} :: missing {string.Join(" + ", absent)}");
                return;
            }

            Directory.CreateDirectory(AuthoredPeople);

            foreach (var name in AnimatedPeopleWalkers)
            {
                var source = Load($"{AnimatedPeople}{name}.prefab");
                if (!source)
                    continue;

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);

                // Unpacked completely, not just at the root: what is saved has to be a
                // standalone prefab, because a variant would inherit the wander script and the
                // agent back from the pack the moment the pack is updated.
                PrefabUtility.UnpackPrefabInstance(
                    instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

                foreach (var agent in instance.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true))
                    Object.DestroyImmediate(agent);

                // The pack's mover, and not a MonoBehaviour, so the sweep below cannot catch
                // it. It IS a collider: left on beside the capsule copied from the reference,
                // the walker carries two, and every crosswalk trigger counts it twice.
                foreach (var mover in instance.GetComponentsInChildren<CharacterController>(true))
                    Object.DestroyImmediate(mover);

                // The wander script is the pack's own and lives in a namespace this assembly
                // does not reference, so it goes by elimination: anything that is not one of
                // ours, on a prefab that so far has none of ours. Null entries are missing
                // scripts, which would break the build silently, and go too.
                foreach (var script in instance.GetComponentsInChildren<MonoBehaviour>(true))
                    if (!script || script is not (HumanBehavior or PathFinding))
                        Object.DestroyImmediate(script);

                var animator = instance.GetComponent<Animator>();
                var referenceAnimator = reference.GetComponent<Animator>();
                if (animator)
                {
                    // See the summary: this is the double-movement trap.
                    animator.applyRootMotion = false;
                    if (referenceAnimator)
                        animator.cullingMode = referenceAnimator.cullingMode;
                }

                // Copied off the reference like everything else - "Human" in practice, and
                // load-bearing: Crosswalk counts bodies by this tag, so an Untagged walker
                // (Animated People ships them Untagged) crosses without cars ever seeing it.
                instance.tag = reference.tag;

                // Deliberately the reference's capsule verbatim, children included: an adult
                // capsule on a 1.2m child is oversized, and that is fine. Only two things read
                // it - PedestrianRegistry.MeasureRadius, which takes the radius and ignores the
                // height, and the crosswalk triggers, which want to be generous. Scaling it to
                // the rig would buy nothing and would put a second per-model number in a pass
                // whose whole discipline is "measured off the reference".
                var capsule = instance.AddComponent<CapsuleCollider>();
                capsule.radius = referenceCapsule.radius;
                capsule.height = referenceCapsule.height;
                capsule.center = referenceCapsule.center;
                capsule.direction = referenceCapsule.direction;

                var body = instance.AddComponent<Rigidbody>();
                body.mass = referenceBody.mass;
                body.useGravity = referenceBody.useGravity;
                body.isKinematic = referenceBody.isKinematic;
                body.interpolation = referenceBody.interpolation;
                body.collisionDetectionMode = referenceBody.collisionDetectionMode;
                body.constraints = referenceBody.constraints;

                // HumanBehavior [RequireComponent]s PathFinding, so adding it first means Unity
                // adds PathFinding for us - and adding PathFinding explicitly afterwards would
                // give the walker two of them.
                var behavior = instance.AddComponent<HumanBehavior>();
                behavior.maxspeed = referenceBehavior.maxspeed;

                // randomDestination is set per-instance by PedestrianSpawner, not baked here -
                // the same prefab has to serve a commanded walker later.
                behavior.randomDestination = false;

                PrefabUtility.SaveAsPrefabAsset(instance, $"{AuthoredPeople}{name}_AI.prefab");
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>
        /// The crowd mix, as weights over groups rather than one flat list.
        ///
        /// The flat list was the bug: PedestrianSpawner drew uniformly, so every model was
        /// exactly as common as every other and the one gangster in the pack appeared as often
        /// as the lifeguard. Vehicles hit this first and solved it the same way - see the note
        /// at the top of VehiclePicker about the crawler crane parked outside a flat.
        ///
        /// Weights are shares of the street, not probabilities per model. Suits at 3/10.5 is a
        /// city of clerks, brokers and the odd wise guy; Civilians at 4/10.5 keeps them from
        /// owning it. Children used to be one model at 0.5, because Epic City has exactly one
        /// child left once the beach is gone and a pavement where every child is the same boy
        /// is worse than a pavement with few children. The Animated People conversion gives it
        /// six more, so the weight goes up with the variety that earns it - the constraint was
        /// never that the period had few children.
        /// </summary>
        static PrefabDatabase.WeightedPrefabs[] BuildPedestrianGroups() => new[]
        {
            Crowd("Suits", 3f,
                LoadAll(PeopleAI, "man-mafia_AI", "man-tie_AI"),
                LoadAll(AuthoredPeople, "man_business_AI", "man_butler_AI", "man_judge_AI")),

            Crowd("Civilians", 4f,
                LoadAll(PeopleAI, "man-casual_AI", "man-shirt_AI",
                        "woman-casual_AI", "woman-dress_AI", "woman-ginger_AI"),
                LoadAll(AuthoredPeople, "man_coat_winter_AI", "woman_coat_winter_AI",
                        "woman_business_AI")),

            Crowd("Workers", 2f,
                LoadAll(PeopleAI, "man-worker_AI", "man-construction-worker_AI",
                        "man-farm_AI", "woman-farm_AI", "man-golf_AI")),

            Crowd("Fringe", 1f,
                LoadAll(PeopleAI, "man-soldier_AI"),
                LoadAll(AuthoredPeople, "man_homeless_AI", "woman_homeless_AI",
                        "man_punk_AI", "woman_punk_AI")),

            Crowd("Children", 1.5f, Children()),
        };

        /// <summary>
        /// Every child model in the city, in one place because two systems must agree on it:
        /// the crowd's Children group and SchoolBusDirector's roster. A child waiting at a bus
        /// stop and a child on the pavement have to be able to be the same child, or the school
        /// run reads as a cast of extras bussed in for the occasion.
        /// </summary>
        static GameObject[] Children() => Merge(
            LoadAll(PeopleAI, "boy-sport_AI"),
            LoadAll(AuthoredPeople, "boy_casual_cap_AI", "boy_coat_winter_AI", "boy-large_AI",
                    "girl_casual_shorts_AI", "girl_coat_winter_AI", "girl-large_AI"));

        /// <summary>One weighted slice of the crowd, drawn from more than one pack.</summary>
        static PrefabDatabase.WeightedPrefabs Crowd(
            string label, float weight, params GameObject[][] lists) =>
            new()
            {
                label = label,
                weight = weight,
                prefabs = Merge(lists),
            };

        const string PackPeopleController =
            "Assets/polyperfect/Low Poly Epic City/People Controller.controller";
        const string AnimatedPeopleClips =
            "Assets/polyperfect/Low Poly Animated People/- Animations/Common_Animations/Common_Animation_Set.fbx";

        /// <summary>
        /// The death clip lives in its own FBX rather than the common set, and imports under the
        /// name "Death" rather than the file's "Death_FallForwards" - the take is renamed in the
        /// .meta. Loaded by type rather than by name for that reason.
        /// </summary>
        const string DeathClipAsset =
            "Assets/polyperfect/Low Poly Animated People/- Animations/In Place/Deaths/Death_FallForwards.fbx";

        /// <summary>
        /// Quaternius' Universal Animation Library (CC0), the only source in the project for a
        /// gun. Neither character pack ships an aim or a shoot take, which is why the gunman
        /// was posed by IK to begin with - a guess at what an animator would have done. These
        /// are the animator's answer, so the guess goes.
        ///
        /// The IN-PLACE file, not the _RM one beside it. Every pedestrian here runs with
        /// applyRootMotion off because HumanBehavior drives the transform, so baked root motion
        /// would be thrown away at best and fight the script at worst.
        ///
        /// Takes come through named "Armature|Pistol_Shoot" - the Blender action prefix rides
        /// along into the FBX - so clips are matched on the part after the bar, never on the
        /// whole name.
        /// </summary>
        const string PistolClipAsset = "Assets/Animations/UAL1_Standard.fbx";

        const string PedestrianControllerPath = ConfigDir + "/People Interaction Controller.controller";

        /// <summary>
        /// The animator for interaction-enabled pedestrians: the pack's own People Controller
        /// (whose walk/idle tuning is copied wholesale rather than rebuilt) plus talk, argue
        /// and bench-sit states retargeted from the Animated People pack. Both packs' rigs are
        /// Humanoid already, so retargeting is just clip assignment.
        ///
        /// Rebuilt from scratch on every run - the copy is deleted first - which is the same
        /// overwrite discipline as every list in CreateAssets: hand-edits to the generated
        /// controller do not survive, changes belong here. Nothing references it by GUID
        /// except PrefabDatabase, which is reassigned in the same run; the pack's AI prefabs
        /// keep their original controller and PedestrianSpawner swaps it at runtime.
        ///
        /// One structural rule worth keeping: Talk and Argue hang off AnyState, the sit chain
        /// does NOT. A looping state is safe under AnyState because the only state satisfying
        /// its condition is itself, and canTransitionToSelf blocks that. Sit Down is a
        /// one-shot that leads to Sitting with the condition still true - under AnyState the
        /// machine would re-enter Sit Down from Sitting forever, so the sit entry runs off
        /// the two locomotion states explicitly.
        /// </summary>
        static RuntimeAnimatorController BuildPedestrianController()
        {
            if (!AssetDatabase.LoadAssetAtPath<AnimatorController>(PackPeopleController))
            {
                Missing.Add(PackPeopleController);
                return null;
            }

            var clips = AssetDatabase.LoadAllAssetsAtPath(AnimatedPeopleClips)
                .OfType<AnimationClip>()
                .Where(clip => !clip.name.StartsWith("__preview"))
                .ToDictionary(clip => clip.name, clip => clip);

            AnimationClip Clip(string name)
            {
                if (clips.TryGetValue(name, out var clip))
                    return clip;
                Missing.Add($"{AnimatedPeopleClips} :: {name}");
                return null;
            }

            var talkClip = Clip("Standing_Talking");
            var argueClip = Clip("Standing_Shouting");
            var sitDownClip = Clip("Idle-Sitting_Bench");
            var sittingClip = Clip("Sitting_Bench_Idle");
            var standUpClip = Clip("Sitting-Idle");

            var deathClip = AssetDatabase.LoadAllAssetsAtPath(DeathClipAsset)
                .OfType<AnimationClip>()
                .FirstOrDefault(clip => !clip.name.StartsWith("__preview"));
            if (!deathClip)
                Missing.Add($"{DeathClipAsset} :: death take");

            EnsureAnimationLibraryIsHumanoid();
            var pistolIdleClip = PistolClip("Pistol_Idle_Loop");
            var aimClip = PistolClip("Pistol_Aim_Neutral");
            var shootClip = PistolClip("Pistol_Shoot");

            if (!talkClip || !argueClip || !sitDownClip || !sittingClip || !standUpClip || !deathClip)
                return null;

            if (!ResetControllerFromPack())
            {
                Missing.Add(PedestrianControllerPath);
                return null;
            }

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(PedestrianControllerPath);
            controller.AddParameter(PedestrianAnimation.ActivityParam, AnimatorControllerParameterType.Int);

            // OnAnimatorIK is never called unless the LAYER asks for it, and a gunman aims by IK
            // rather than by clip - the packs ship no aim or shoot take, so GunmanAim reaches
            // the right hand through the humanoid IK goal instead. layers returns a COPY, so the
            // flag has to be written back through the property or it is silently discarded.
            var layers = controller.layers;
            layers[0].iKPass = true;
            controller.layers = layers;

            var machine = controller.layers[0].stateMachine;
            var idle = FindState(machine, "Breathing Idle");
            var walk = FindState(machine, "Standard Walk");
            if (idle == null || walk == null)
            {
                Missing.Add($"{PackPeopleController} :: Breathing Idle / Standard Walk state");
                return controller;
            }

            var talk = AddState(machine, "Talk", talkClip);
            var argue = AddState(machine, "Argue", argueClip);
            var sitDown = AddState(machine, "Sit Down", sitDownClip);
            var sitting = AddState(machine, "Sitting", sittingClip);
            var standUp = AddState(machine, "Stand Up", standUpClip);
            var die = AddState(machine, "Die", deathClip);

            AnyStateEntry(machine, talk, PedestrianAnimation.Talk);
            AnyStateEntry(machine, argue, PedestrianAnimation.Argue);

            // No Exit() and no OnExitTime(): Die is where a walker stops. See
            // PedestrianAnimation.Die for why a one-shot is safe under AnyState here and the
            // sit chain is not. Snappier than the others on purpose - a quarter-second blend
            // into being shot reads as stumbling, not as being hit.
            AnyStateEntry(machine, die, PedestrianAnimation.Die, duration: 0.08f);

            // The gun chain. Skipped rather than half-built if the library is not imported: a
            // controller with an Aim state and no clip in it is a T-pose, which reads as a
            // broken rig rather than as a missing asset.
            if (pistolIdleClip && aimClip && shootClip)
            {
                var pistolIdle = AddState(machine, "Pistol Idle", pistolIdleClip);
                var aim = AddState(machine, "Aim", aimClip);
                var shoot = AddState(machine, "Shoot", shootClip);

                AnyStateEntry(machine, pistolIdle, PedestrianAnimation.PistolIdle);
                AnyStateEntry(machine, aim, PedestrianAnimation.Aim);
                Exit(pistolIdle, idle, PedestrianAnimation.PistolIdle);
                Exit(aim, idle, PedestrianAnimation.Aim);

                // Shoot is NOT an AnyState entry - it is reachable only from Aim - and it fires
                // on a TRIGGER rather than on the activity int. See PedestrianAnimation.ShootParam:
                // an int condition still matched when Shoot handed back to Aim on exit time, so
                // the gunman recoiled in a loop. A trigger is consumed by the transition.
                controller.AddParameter(PedestrianAnimation.ShootParam,
                                        AnimatorControllerParameterType.Trigger);

                var pull = aim.AddTransition(shoot);
                pull.hasExitTime = false;
                pull.duration = 0.02f;
                pull.AddCondition(AnimatorConditionMode.If, 0f, PedestrianAnimation.ShootParam);

                OnExitTime(shoot, aim);
            }
            Exit(talk, idle, PedestrianAnimation.Talk);
            Exit(argue, idle, PedestrianAnimation.Argue);

            SitEntry(idle, sitDown);
            SitEntry(walk, sitDown);
            OnExitTime(sitDown, sitting);
            Exit(sitting, standUp, PedestrianAnimation.Sit);
            OnExitTime(standUp, idle);

            EditorUtility.SetDirty(controller);
            return controller;
        }

        /// <summary>
        /// Puts a clean copy of the pack controller at PedestrianControllerPath, KEEPING the
        /// asset's GUID.
        ///
        /// DeleteAsset followed by CopyAsset - what this used to do - mints a brand new GUID
        /// every run, and every scene that referenced the old one silently loses its animator.
        /// The character keeps standing there in its bind pose with no error anywhere, which is
        /// about the most expensive way a build can fail. The demo scene hit exactly this.
        ///
        /// So the FILE is overwritten and the .meta beside it is left alone, which is what
        /// carries the GUID. Sub-object file IDs do change, but nothing references a state
        /// directly - an Animator points at the controller's main object, whose ID is fixed.
        /// </summary>
        static bool ResetControllerFromPack()
        {
            if (!AssetDatabase.LoadAssetAtPath<AnimatorController>(PedestrianControllerPath))
                return AssetDatabase.CopyAsset(PackPeopleController, PedestrianControllerPath);

            var pack = Path.GetFullPath(PackPeopleController);
            var target = Path.GetFullPath(PedestrianControllerPath);
            if (!File.Exists(pack))
                return false;

            File.Copy(pack, target, overwrite: true);
            AssetDatabase.ImportAsset(PedestrianControllerPath, ImportAssetOptions.ForceUpdate);

            return AssetDatabase.LoadAssetAtPath<AnimatorController>(PedestrianControllerPath);
        }

        /// <summary>
        /// A take from the animation library, matched on the part after the "Armature|" prefix.
        ///
        /// Missing clips are reported but not fatal: the gun chain is skipped and the rest of
        /// the controller still builds, so a project without the library still has walking,
        /// talking, sitting pedestrians.
        /// </summary>
        static AnimationClip PistolClip(string take)
        {
            var clip = AssetDatabase.LoadAllAssetsAtPath(PistolClipAsset)
                .OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview") &&
                                     c.name.Split('|').Last() == take);

            if (!clip)
                Missing.Add($"{PistolClipAsset} :: {take}");

            return clip;
        }

        /// <summary>
        /// Imports the library as Humanoid so its clips retarget onto the two character packs.
        ///
        /// An animation-only FBX defaults to Generic, and a Generic clip played on a Humanoid
        /// character animates NOTHING - no warning, no error, the character just stands there.
        /// That is the same silent failure the missing IK pass produced, and it is worth one
        /// reimport to rule out.
        /// </summary>
        static void EnsureAnimationLibraryIsHumanoid()
        {
            var importer = AssetImporter.GetAtPath(PistolClipAsset) as ModelImporter;
            if (!importer)
            {
                Missing.Add(PistolClipAsset);
                return;
            }

            if (importer.animationType == ModelImporterAnimationType.Human)
                return;

            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.SaveAndReimport();
        }

        static AnimatorState FindState(AnimatorStateMachine machine, string name)
        {
            foreach (var child in machine.states)
                if (child.state && child.state.name == name)
                    return child.state;
            return null;
        }

        static AnimatorState AddState(AnimatorStateMachine machine, string name, AnimationClip clip)
        {
            var state = machine.AddState(name);
            state.motion = clip;
            return state;
        }

        static void AnyStateEntry(AnimatorStateMachine machine, AnimatorState to, int activity,
                                  float duration = 0.25f)
        {
            var transition = machine.AddAnyStateTransition(to);
            transition.canTransitionToSelf = false;
            transition.hasExitTime = false;
            transition.duration = duration;
            transition.AddCondition(AnimatorConditionMode.Equals, activity, PedestrianAnimation.ActivityParam);
        }

        static void SitEntry(AnimatorState from, AnimatorState sitDown)
        {
            var transition = from.AddTransition(sitDown);
            transition.hasExitTime = false;
            transition.duration = 0.25f;
            transition.AddCondition(AnimatorConditionMode.Equals, PedestrianAnimation.Sit,
                                    PedestrianAnimation.ActivityParam);
        }

        /// <summary>Leave a looping activity state the moment its activity value is withdrawn.</summary>
        static void Exit(AnimatorState from, AnimatorState to, int activity)
        {
            var transition = from.AddTransition(to);
            transition.hasExitTime = false;
            transition.duration = 0.25f;
            transition.AddCondition(AnimatorConditionMode.NotEqual, activity, PedestrianAnimation.ActivityParam);
        }

        /// <summary>
        /// A one-shot rolling into its successor when the clip completes. Exit time rather
        /// than trusting the clips' loop import flags - most of the pack's transition clips
        /// are imported looping, and a looping sit-down bobs up and down forever.
        /// </summary>
        static void OnExitTime(AnimatorState from, AnimatorState to)
        {
            var transition = from.AddTransition(to);
            transition.hasExitTime = true;
            transition.exitTime = 0.95f;
            transition.duration = 0.15f;
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
            // Grass is the park's APRON only - the band from the kerb in to its own tiles, which
            // is 10m wide and was concrete, leaving the park looking set back behind a ring while
            // every building on the street stood 8m further forward. It is not a block surface:
            // grass was the bungalow belt's ground and there is no bungalow belt any more.
            // tile-plain_sand is still omitted on purpose - sand in 1920s Chicago reads as a bug,
            // not as variety. Do not "complete the set".
            var grass = Load(Tiles + "tile-plain_grass.prefab");

            var cityTrees = LoadAll(Trees, "tree-oak", "tree-lime", "tree-poplar", "shrub-round");

            return new[]
            {
                // The connective tissue of the city, and the zone the fallback lands on.
                Palette(BlockZone.ResidentialHigh, weight: 26f, maxShare: 1f,
                    groups: new[]
                    {
                        Terrace("Terrace 4+5 floor", 85f, TerraceStreet, TerraceRear, TerraceCorner),

                        StreetShops(15f),
                    },
                    // One development per street: a side is a flush 4/5-storey wall or a run of
                    // shops, not a per-slot lottery of the two. This is what makes the terraces
                    // actually touch.
                    uniformStreetRuns: true,
                    // The police station, demoted from its own zone the way the fire station
                    // was - but through the landmark path, not the Shops bag: it is too wide
                    // for a terrace slot, and a landmark is placed before uniformStreetRuns
                    // locks the side, so the single-prefab-group trap (see StreetShops) does
                    // not apply. db.uniqueBuildings caps it at one per city.
                    // The bank rides in beside the station for the same reason the station is
                    // here at all - 17.10 x 20.53, too wide for a terrace slot and finished on
                    // all four elevations - and this is where it usually ends up. Its other
                    // shape is a Bank block of its own; ZonePlanner rolls once per city between
                    // the two so that exactly one of them happens. Index 1 in this array is
                    // therefore load-bearing: requiredLandmark below points at it.
                    landmarks: LoadAll(Buildings, "building-policestation", "building-bank"),
                    // Under 1 on purpose, unlike the civic zones where the landmark IS the
                    // block: at 1 the station would stand in the first-built residential block
                    // every time, i.e. the same corner of every map. At 0.45 across the six-plus
                    // blocks this zone gets, which block hosts it varies by seed. Since the
                    // station became guaranteed (below), this roll only decides whether an
                    // EARLIER block beats the forced one to it - either way the city has
                    // exactly one, the variety in WHERE is what the 0.45 still buys.
                    landmarkChance: 0.45f,
                    // The bank, which is NOT a ceiling: the city has one, always. A chance can
                    // deliver a station nineteen times in twenty and that is fine for a station;
                    // it is not fine for the building the whole period turns on. ZonePlanner
                    // marks one block with this index - the largest, because the bank is 20.53m
                    // deep and wants a courtyard to push back into - and that block builds it
                    // without rolling. Ignored entirely on the seeds where the bank drew its own
                    // block instead.
                    requiredLandmark: 1,
                    // The station, promoted to the same certainty the day the police became a
                    // system: the patrol fleet and the beat officers both treat it as home, and
                    // a once-in-fifty city with no station stopped being flavour and became a
                    // city with no police. Index 0 is now load-bearing too. ZonePlanner fulfils
                    // this AFTER the bank's route, on the largest unclaimed block of the zone -
                    // see FulfilGuaranteedLandmarks for the ordering and the fallbacks.
                    guaranteedLandmark: 0,
                    // Forecourt cars, NOT parkedCars: parkedCars swaps the picker for the WHOLE
                    // block (BlockBuilder.BuildBlock), which here would fill every bay of every
                    // residential block with patrol cars. landmarkCars reaches only the bay
                    // PlaceLandmark cuts in front of the recessed station.
                    //
                    // One bucket for both of this palette's landmarks, because landmarkCars is a
                    // property of the PALETTE and there is no per-landmark list to hang a second
                    // one off. In practice it serves only the BANK: the station's forecourt is
                    // the patrol fleet's parking, so PlaceLandmark passes maxCars 0 for it -
                    // lines painted, bays reserved, no static bakes - and records the stalls on
                    // a PoliceStation marker for PoliceDirector's four real cars.
                    //
                    // These are the bank's CUSTOMERS, which is why they are ordinary cars. The
                    // bucket used to lead with car-police, and since the station never draws
                    // from it, the only thing it ever produced was a rank of patrol cars outside
                    // the bank - the exact reading parkedCarGroups above rejects police cars to
                    // avoid ("parked, it is studied, and outside the station's own forecourt it
                    // would read as a mistake rather than as a beat car"). The two rules now
                    // agree. The bays these bakes leave empty are not spare: BankVisitorDirector
                    // drives live customers into them, so this list sets the parked HALF of the
                    // forecourt and the traffic groups set the other.
                    //
                    // The van is in a bucket WITH other cars for a mechanical reason: armored-truck
                    // is 5.85 x 2.29 against a marked bay of 5.6 x 2.4, so VehiclePicker rejects it
                    // from every bay it is offered (it is on the reject list above). Alone it would
                    // leave the forecourt empty - exactly what the hospital's single-entry ambulance
                    // bucket does today, which is a defect and not a pattern to copy. Beside cars
                    // that fit, the bay is always occupied. It stays for the reason it is in the
                    // Bank block's own Bullion bucket: a bank car is why this city has a station.
                    //
                    // NOT allowed to go empty, whatever is in it: palette.HasLandmarkCars is the
                    // flag that cuts the forecourt at all (BlockBuilder.PlaceLandmark), so an
                    // empty list would take the bays away from the police station too.
                    landmarkCars: new[]
                    {
                        Bucket("Customers", 1f, CarsStatic,
                                 "car-veteran", "car-passenger", "car-hippie-van", "jeep-open",
                                 "car-pickup-modern", "car-caravan-small", "car-taxi",
                                 "armored-truck"),
                    },
                    // Uncapped, so TargetLotSize alone decides: a one-cell block stays a single
                    // ring, a two-cell block becomes two ringed lots with a real alley between
                    // them, a three-cell block becomes three. This is what makes a big block read
                    // as two or three small ones rather than as one ring around a courtyard - the
                    // interior ROWS are the fill, and what is left over is the alley.
                    //
                    // It was 1 for a while, on the argument that only the outermost row faces a
                    // street. True, and beside the point: the rear elevations face each other
                    // across the alley, which is the view a 1920s block actually has.
                    maxLotsPerAxis: 0,
                    grounds: new[]
                    {
                        Ground("Concrete", 55f, concrete),
                        Ground("Asphalt", 25f, asphalt),
                        Ground("Dirt", 20f, dirt),
                    },
                    // The ring encloses a SERVICE yard, not a garden - grass and dirt here were
                    // what made the interior read as a field with a few trees on it.
                    courtyardGrounds: new[] { concrete, asphalt },
                    // A service yard behind a terrace is patched concrete over patched asphalt,
                    // laid in bays. Both treatments on, but the surface re-roll held back from 1
                    // so the yard still reads as mostly one thing with repairs in it.
                    groundPatchChance: 0.55f,
                    paveJoints: true,
                    alleyProps: AlleyKit(),
                    // Chance a slot on an ALLEY-facing run is a 12m parking bay instead of a
                    // building. This is the "sometimes a car park where a building would be"
                    // variation, and it belongs on the rear elevations rather than in the alley
                    // itself - cars parked down the middle of a 6m alley block it.
                    alleyParkingChance: 0.15f,
                    // One street side of each block gives up its buildings for a car lot or a
                    // pocket park, and the rows pack tight against it - where the length the
                    // terrace kit cannot fill goes. See FeatureStrip.
                    featureStrip: true,
                    kioskPrefabs: KioskKit(),
                    pocketParkProps: PocketParkKit(),
                    // The alley furniture goes down first, so the scatter is only what fills the
                    // corners it could not reach.
                    //
                    // Bins are in this bag now, which puts them on the FRONT of the block, not
                    // just behind it - the one place the city used to keep clean. Deliberate:
                    // the brief is a neglected city, and this palette is 26 of the map's weight,
                    // so nothing else moves the look as much. Density 0.06 -> 0.09 pays for the
                    // three new entries: four trees in a bag of five at 0.06 is 0.048 a try,
                    // four in a bag of eight at 0.09 is 0.045, so the greenery is untouched and
                    // the rubbish is added on top rather than swapped in.
                    scatter: Merge(cityTrees,
                                   LoadAll(CityProps, "bin-wheelie", "dumpster"),
                                   LoadAll(Props, "mail-box", "trash-can")),
                    scatterDensity: 0.09f),

                // Up from 0.3: with the bungalow belt gone, ResidentialHigh and Industrial are
                // the only two zones that fill a map, and a 30% ceiling on the works left the
                // rim as terraces too.
                Palette(BlockZone.Industrial, weight: 12f, maxShare: 0.4f,
                    // The whole block is now a walled compound rather than a terrace of
                    // factories: IndustrialLayout plans a wall, a gate, carriageways in from it
                    // and rows of halls along them, and IndustrialDresser builds onto that. The
                    // perimeter path, the feature strip, the interior pass and the scatter are
                    // all skipped for this zone - see BlockBuilder.BuildBlock.
                    industrialYard: true,
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
                    // The one zone that wants the full mosaic: a works yard IS mixed dirt,
                    // concrete and asphalt, poured piecemeal as the site grew. No joints - none
                    // of it was ever laid in bays.
                    groundPatchChance: 1f,
                    // Tarmac over that mosaic, on the carriageways only. The no-border variant:
                    // a kerb drawn round every road rectangle would tile visibly where two
                    // carriageways meet at the gate.
                    serviceRoadGround: Load(NoBorder + "tile-plain_asphalt-nb.prefab"),
                    // The two surfaces IndustrialLotBuilder composites the yard out of.
                    // Both no-border, because the yard is nothing but patches meeting.
                    yardConcrete: Load(NoBorder + "tile-plain_concrete-nb.prefab"),
                    yardDirt: Load(NoBorder + "tile-plain_dirt-nb.prefab"),
                    // Concrete, not railing. The car park uses fence-classic because a 1.5m wall
                    // would hide the front row of cars, which is the thing being shown; a works
                    // is the opposite - the wall IS the thing being shown, and you are meant to
                    // see the halls over it rather than through it.
                    fenceSegment: Load(Props + "wall-concrete.prefab"),
                    fencePost: Load(Fences + "fence-stone-tower.prefab"),
                    gatePrefab: Load(Military + "military-gate.prefab"),
                    // The same material as before, used differently. These were thirteen entries
                    // in one scatter bag at density 0.22, so they landed anywhere at any yaw;
                    // IndustrialDresser now lays each one as a short row against a hall wall.
                    //
                    // The yard's share of the refuse pass comes through HERE rather than through
                    // scatterDensity, which stays 0 for the reason given below and is skipped for
                    // industrialYard anyway. BuildStacks picks one prefab per hall and repeats it
                    // in a line against a side wall, so a works gets a ROW of dumpsters down its
                    // flank - which is what a real yard looks like, and is the opposite of the
                    // tipped-out uniform noise this pass replaced.
                    stackProps: Merge(
                        LoadAll(Props, "timber", "palette", "package-box"),
                        LoadAll(CityProps, "dumpster", "bin-wheelie"),
                        LoadAll(Construction, "brick-plain-stack", "brick-concrete-stack",
                                              "planks-stack", "cement-bag-pile",
                                              "pipe-concrete-stack")),
                    // Out of the scatter bag and stood deliberately in a hall's back yard. These
                    // two are what give a works its skyline, and the smoke hangs off the chimney.
                    chimneyProps: LoadAll(Buildings, "chimney-big", "water-tower-medium"),
                    // The back band of every row, which is otherwise bare ground: a hall is 6 to
                    // 23m deep in a 26m row. All measured to fit that strip - the garage is
                    // 8 x 10, the silo 3.9 x 4.1, the site hut 13.6 x 13.6, the store 6 x 16.4.
                    // Deliberately NOT here: crane-tower is 72m tall, airport-hangar is 43m wide
                    // and water-station-big is 29 x 24 - each of them would simply never be
                    // placed, and a prefab that can never be drawn is worse than an absent one
                    // because the list reads as though it were.
                    auxBuildings: Merge(
                        LoadAll(Buildings, "building-policestation-garage", "industry-storage",
                                           "water-tower-medium"),
                        LoadAll(Farm, "farm-silo"),
                        LoadAll(Construction, "contruction-large", "restroom-portable")),

                    // The yard stock, read by IndustrialYardDresser off the zones
                    // IndustrialLotPlanner cuts. Each list is the storage pack FIRST and this
                    // pack's own stand-ins second, and both halves are always present: the
                    // storage pack is a variety layer, so an absent or unlicensed one costs
                    // choice and never the feature. YardKit returns empty in silence when the
                    // authored folder does not exist, which is what keeps a project that has not
                    // imported the pack out of the missing-prefab report.
                    //
                    // Barrels are the one role with NO stand-in in Epic City - there is not a
                    // barrel or a drum anywhere in the pack, which is exactly why this is the
                    // half worth importing. Concrete rings and pipe rolls carry the role until
                    // it arrives: they are stockyard goods that come in ranks, which is the
                    // property the arrangement depends on.
                    yardBarrels: Merge(
                        YardKit("barrel"),
                        LoadAll(Construction, "concrete-ring-small", "concrete-ring-medium",
                                              "pipe-roll", "pipe-concrete")),

                    yardCrates: Merge(
                        YardKit("crate"),
                        LoadAll(Props, "package-box", "package-box-open"),
                        LoadAll(Construction, "case-tool")),

                    yardSacks: Merge(
                        YardKit("sack"),
                        LoadAll(Construction, "cement-bag-pile", "cement-bag", "cement-bag-open")),

                    // Edge pieces: things with their backs to something. trash-can is the ash
                    // can the cinder yard wants, and the dumpster and the folded ladder are the
                    // two other pieces in the pack that read as standing against a wall rather
                    // than as having been dropped.
                    yardFixtures: Merge(
                        YardKit("fixture"),
                        LoadAll(Props, "trash-can"),
                        LoadAll(CityProps, "dumpster"),
                        LoadAll(Construction, "ladder-folded", "steel-prop-stack")),
                    // A works runs lorries and a forklift, not saloons - the same idea as the
                    // police car outside the police station. `truck` at 6.25m is rejected by
                    // every marked bay, which is why IndustrialLayout keeps its own deeper
                    // lorry stand; the rest fit the staff park.
                    parkedCars: new[]
                    {
                        Bucket("Works", 1f, CarsStatic,
                               "truck", "car-truck-dump", "car-truck-cement", "forklift"),
                    },
                    // Nothing is scattered any more. The yard is arranged, and uniform noise
                    // across it is precisely what made the old block read as tipped out.
                    scatterDensity: 0f,
                    parkingChance: 0f),

                // maxBlocks rather than a share from here down: a city has one hospital and one
                // school however large it grows. Parks and car parks below keep their share
                // cap, because a bigger city should have more of both.
                //
                // Two blocks built round one building each. landmarkChance is 1 because the
                // landmark IS the zone - a hospital block with no hospital is just houses.
                //
                // The police block was the third and is gone, the same demotion the fire
                // station went through before it: a whole block is the city's scarcest thing,
                // and a station house does not need one. The station is ResidentialHigh's
                // landmark now, recessed behind a forecourt of patrol cars and capped at one
                // per city through db.uniqueBuildings. See the ResidentialHigh palette above.
                Palette(BlockZone.Hospital, weight: 14f, maxShare: 1f, maxBlocks: 1,
                    // A hospital is one building with grounds, not a campus: the single smallest
                    // block the map offers, one perimeter ring, and no more than two
                    // outbuildings beside the landmark. The other civic zones can adopt the
                    // same two caps if they start reading as districts.
                    maxBlockCells: 1,
                    maxLotsPerAxis: 1,
                    maxPerimeterBuildings: 2,
                    groups: new[] { Outbuildings() },
                    landmarks: LoadAll(Buildings, "building-hospital"),
                    landmarkChance: 1f,
                    // An institution's forecourt is laid, swept and uniform - joints on, surface
                    // re-roll low. The opposite end of the dial from the works yard.
                    groundPatchChance: 0.25f,
                    paveJoints: true,
                    // The short perimeter leaves most of the block as open yard, and bare paving
                    // there read as a vacant lot. The pocket-park kit is the right register for
                    // hospital grounds - benches, shade, nothing that says service yard -
                    // and the scatter runs at the top of the pack's density range for the same
                    // reason.
                    alleyProps: PocketParkKit(),
                    scatter: Merge(cityTrees,
                                   LoadAll(CityProps, "bench-old")),
                    scatterDensity: 0.22f,
                    // Higher than the civic default: with the perimeter this short a 0.35 roll
                    // too often leaves the ambulance nowhere to stand.
                    parkingChance: 0.5f,
                    parkedCars: new[] { Bucket("Ambulance", 1f, CarsStatic, "car-ambulance-pickup") }),

                Palette(BlockZone.School, weight: 12f, maxShare: 1f, maxBlocks: 1,
                    // Same footprint discipline as the hospital above: one cell, one ring,
                    // two outbuildings. Left open, the school took a 3x3 block and read as
                    // a campus.
                    maxBlockCells: 1,
                    // The one zone the city PROMISES, and the only palette in the database that
                    // sets this - see ZonePlanner's rescue pass. maxBlocks and guaranteed
                    // together read "exactly one", where the hospital beside it still reads
                    // "at most one". The reason is not that a school matters more than a
                    // hospital: the school block carries the only SchoolMarker there is, and
                    // without one SchoolBusDirector finds nothing, so the bus, the stops and
                    // every schoolchild sit out that seed entirely. A whole system was riding
                    // on a weighted roll it could not see.
                    guaranteed: true,
                    maxLotsPerAxis: 1,
                    maxPerimeterBuildings: 2,
                    groups: new[] { Outbuildings() },
                    landmarks: LoadAll(Buildings, "building-school"),
                    landmarkChance: 1f,
                    // An institution's forecourt is laid, swept and uniform - joints on, surface
                    // re-roll low. The opposite end of the dial from the works yard.
                    groundPatchChance: 0.25f,
                    paveJoints: true,
                    scatter: Merge(cityTrees,
                                   LoadAll(Props, "soccer-gate", "basketball-stand"),
                                   LoadAll(Fences, "fence-picket"),
                                   LoadAll(CityProps, "bench-old")),
                    scatterDensity: 0.16f,
                    parkingChance: 0.1f,
                    // The school's forecourt, and the reason it has one at all:
                    // palette.HasLandmarkCars is the flag PlaceLandmark tests before it recesses
                    // a landmark and cuts bays in the frontage it vacates. Without this list the
                    // school stood flush against the street wall with nowhere to park, which is
                    // why the school bus used to spend its whole life stopped IN the lane.
                    //
                    // Parents' cars, so no taxi and no armored-truck: this bay is the school
                    // run, not a rank. The bays these bakes leave empty are not spare -
                    // SchoolParentDirector drives live cars into them, exactly as the bank's
                    // customers use the bank's - and the first BusStallLength metres of the
                    // frontage are not in this layout at all, being the bus's own bay.
                    landmarkCars: new[]
                    {
                        Bucket("Parents", 1f, CarsStatic,
                                 "car-passenger", "car-pickup-modern", "car-veteran",
                                 "jeep-open", "car-caravan-small"),
                    }),

                // There was a fourth civic zone here, the fire station, and it is gone rather than
                // turned down: it was a whole block - the city's scarcest thing at about ten of
                // them - spent on a single-cell yard whose appliance never fit the bay anyway. The
                // building itself is still in the city, in the residential Shops group below and
                // capped through db.uniqueBuildings. See BlockZone and StreetShops.

                // No groups at all: a park has no street wall. The ground carries a Tile
                // component with sidewalk paths, so it is laid unscaled per cell and the
                // pedestrians walk through - see GroundPlacer.
                //
                // maxBlocks rather than maxShare, and the difference is the whole point. A share
                // scales with the map - 25% of a forty-block city is ten parks - which is right
                // for parkland in general and wrong here: this city has ONE park, however big it
                // gets, the same way it has one hospital. ClashesWithNeighbour is left in place
                // even though one park can have no park neighbour; it costs nothing and stays
                // correct if the count is ever raised.
                // groundPerCell is also what routes this block to ParkDresser instead of the
                // uniform scatter, so scatter/scatterDensity are deliberately left at zero here:
                // the park is laid out against tile-park's baked walks, not sprinkled over them.
                //
                // The fountain goes in as the LANDMARK rather than as one more scatter entry.
                // It measures 5.98 x 5.98 and the tile's paved roundel is radius 4 - it was
                // authored for that spot, and one park gets one, on the centre cell.
                //
                // The boulders that used to be in here (stone-round is 2.58 x 3.03 and 2.27
                // tall) are gone. Drawn from a flat 18-entry bag they came up as often as an oak,
                // and a knee-high boulder in the middle of a city park was the loudest wrong note
                // in the block.
                Palette(BlockZone.Park, weight: 8f, maxShare: 1f, maxBlocks: 1,
                    groups: System.Array.Empty<PrefabDatabase.WeightedGroup>(),
                    // Bare grass, not tile-park: the rewrite paints its own walks over
                    // ParkLayout's spines and authors its own nav tiles, and tile-park's baked
                    // 4-fold cross was both the sameness of every old park and a nav island -
                    // its edge-midpoint endpoints never matched an ordinary road tile's
                    // corner sidewalk nodes.
                    ground: grass,
                    groundPerCell: true,
                    // The one palette that does not want a concrete apron - see LayApron.
                    apronGround: grass,
                    landmarks: LoadAll(Props, "fountain"),
                    landmarkChance: 1f,
                    parkTrees: new[]
                    {
                        // Weighted by crown, not by taste. A row - and a grove stand - is planted
                        // from ONE group, and a quadrant is about 11m across: tree-lime at 7.07m
                        // wide fills one on its own, so it stays an occasional specimen while the
                        // 2-4m species carry the avenues.
                        Bucket("Park street trees", 5f, Trees,
                            "tree-oak", "tree-birch", "tree-beech", "tree-round", "tree"),
                        Bucket("Park uprights", 3f, Trees,
                            "tree-poplar", "tree-tall", "tree-birch-tall", "tree-elipse"),
                        // tree-conifer and tree-spruce were in Trees_T the whole time with no
                        // code path behind them. At 2.4-3.1m across they are exactly the size a
                        // stand wants, and evergreens are what makes a grove read as woodland
                        // rather than as an orchard - so this bucket is up from 2 to 4.
                        Bucket("Park evergreens", 4f, Trees,
                            "tree-fir", "tree-forest", "tree-old", "tree-conifer", "tree-spruce"),
                        Bucket("Park specimens", 1f, Trees, "tree-lime", "tree-bonsai"),
                    },
                    parkUndergrowth: Merge(
                        LoadAll(Trees, "shrub", "shrub-round"),
                        // A sapling among the shrubs is what an unmanaged corner of a park grows,
                        // and at 1.11m across it sits in the undergrowth layer, not the canopy.
                        LoadAll(Trees, "tree-little"),
                        LoadAll(Flowers, "roses", "carnations", "sunflower"),
                        // Nature_T/Grass_T was in the pack the whole time and no code path had
                        // ever referenced it. Tufts at 0.4-2.0m are exactly what a lawn between
                        // tree rows was missing.
                        LoadAll(Grass, "grass", "grass-basic", "grass-clumb",
                                       "grass-long", "grass-tall")),
                    parkBenches: Merge(
                        LoadAll(Props, "bench-forest"),
                        LoadAll(CityProps, "bench-old")),
                    // The knoll - the one centred mound in the pack, and none of the tile-hill
                    // kit can join it: those rise at an EDGE and hang a 6m skirt.
                    parkMounds: LoadAll(Tiles, "tile-plain-hump"),
                    fenceSegment: Load(Fences + "fence-shrub.prefab"),
                    // The rewrite's kinds. lamp-city is the 6.7m cast-iron post - the street's
                    // lamp-road-double is a 9.5m double-arm carriageway fixture that clumped
                    // around the old fountain. The monument is rock-pillar standing in for the
                    // statue the pack never shipped; the boulders are the naturally SMALL
                    // stones at authored scale (rock-terrasse, 6x8m, stays out as an outcrop);
                    // the deadwood is its own list so only the informal archetype can draw it.
                    parkLamps: LoadAll(CityProps, "lamp-city"),
                    parkBins: LoadAll(Props, "trash-can"),
                    parkMonuments: LoadAll(Stones, "rock-pillar"),
                    parkBoulders: LoadAll(Stones, "stone-round", "stone-flat", "stone-oval",
                                                  "stone-small", "rocks-small"),
                    parkDeadTrees: LoadAll(Trees, "tree-dead", "tree-dry", "stump", "stump-small"),
                    // Borderless so the stretched pond patch has no baked rim of its own.
                    parkWaterTile: Load(NoBorder + "tile-water-nb.prefab"),
                    parkGatePiers: Load(Fences + "fence-stone-tower.prefab"),
                    // A carousel was a fixture of a 1920s American park; the informal
                    // archetype rolls one rarely.
                    parkAmusement: LoadAll(Amusement, "carousel-coaster")),

                // The bank's other shape. It is usually ResidentialHigh's landmark - see the
                // requiredLandmark up there - and about one city in four it takes a block of its
                // own instead, which is this. ZonePlanner rolls between the two once per city and
                // then makes good on whichever it rolled, so the city always has exactly one bank
                // and this palette is either the whole story or unused, never half of it.
                //
                // building-bank had been sitting in Buildings_T with no code path near it until
                // now. Measured 17.10 x 20.53 and 13.70m tall, and both numbers earn their keep:
                // the footprint is why it cannot be a storefront in the Shops bag the way the post
                // office is, and the height is why it stands at full size - it is SHORTER than the
                // 23.8m terraces it faces, so the halving that the pack's other big civic pieces
                // want would only make it a doll's house. A one-cell block is a 46 x 46m buildable
                // rect, which leaves it about 13m clear on every side.
                //
                // NO facadeYawFixes entry, and that is a measurement rather than an omission. The
                // mesh is perfectly X-symmetric - 2927 verts mirror 2927 - so the pack's usual
                // FBX X-negation trap cannot apply here, and the entrance is on local +Z: front
                // steps, recessed double doors at z=7.30, the portico, and a line of ten anti-ram
                // bollards across z~9.87 with a gap where the stairs come down. The -Z face is six
                // windows on a flat wall. That is exactly BlockBuilder's convention, so extraYaw is
                // 0 and the house style is to omit a 0. Anyone re-deriving the facing from the
                // pack's own demo scene will get 180 and be wrong: the instance in
                // "City - Night.unity" sits at yaw -90 on an undressed empty lot facing nothing.
                //
                // No fence. The mesh ships its own bollard line across the front, which is the
                // security line a bank actually has; a railing on top of it would read as two
                // fences. The hospital and the school set none either, for the plainer reason that
                // an institution's forecourt is open to the pavement.
                Palette(BlockZone.Bank, weight: 12f, maxShare: 1f, maxBlocks: 1,
                    // The hospital's footprint discipline, for the hospital's reason: one cell,
                    // one ring, two outbuildings beside the landmark. Note this puts three zones -
                    // hospital, school, bank - in competition for the map's one-cell
                    // blocks, of which a 9x7 map has only a handful. That competition, not the
                    // weights alone, is what decides how often each turns up. It was four until
                    // the church went; expect hospital and school measurably more often now.
                    maxBlockCells: 1,
                    maxLotsPerAxis: 1,
                    maxPerimeterBuildings: 2,
                    // NOT Outbuildings(), whose lead prefab is building-policestation-garage. The
                    // station itself is somewhere else entirely in the same city now, standing as
                    // ResidentialHigh's landmark, so its garage in the bank's yard would be
                    // visibly orphaned from it - and a police garage behind a bank reads as a
                    // motor pool besides. Ordinary masonry instead.
                    //
                    // Non-empty on purpose: a palette with groups builds its own perimeter, which
                    // is what keeps this zone out of BlockBuilder entirely - no zone branch, no
                    // dresser of its own.
                    groups: new[] { BankNeighbours() },
                    landmarks: LoadAll(Buildings, "building-bank"),
                    landmarkChance: 1f,
                    // An institution's forecourt is laid, swept and uniform - joints on, surface
                    // re-roll low. The opposite end of the dial from the works yard.
                    groundPatchChance: 0.25f,
                    paveJoints: true,
                    // The hospital's kit rather than the alley kit, and for the hospital's reason:
                    // one ring on a one-cell block leaves most of the yard open, and bare paving
                    // there reads as a vacant lot. Bins and a dumpster would say service yard,
                    // which is the one thing a bank frontage must not say.
                    alleyProps: PocketParkKit(),
                    scatter: Merge(cityTrees,
                                   LoadAll(CityProps, "bench-old")),
                    scatterDensity: 0.18f,
                    // Higher than the civic default for the hospital's reason again: with the
                    // perimeter this short, a 0.35 roll too often leaves the forecourt empty.
                    parkingChance: 0.5f,
                    // parkedCars here rather than landmarkCars, unlike the residential route: the
                    // whole block IS the bank, so swapping the picker block-wide has nothing else
                    // to spoil. Same pairing rule as up there though - the van does not fit a
                    // marked bay and would leave the forecourt bare if it stood in the bucket
                    // alone.
                    parkedCars: new[]
                    {
                        Bucket("Bullion", 1f, CarsStatic, "armored-truck", "car-veteran"),
                    }),

                // The car dealership - building-carwash standing in for the showroom, the pack
                // having no dealership of its own. Measured 19.70 x 17.00 and 6.00m tall: an
                // office block on its local -X half and an OPEN CANOPY on pillars over the +X
                // half, mouth on +X - which is why it carries a 270 entry in facadeYawFixes
                // (see BuildFacadeYawFixes) and fronts the street with its canopy and a 17.0m
                // frontage, not the 19.7m office axis.
                //
                // The bank's one-cell discipline, but landmarkCars rather than parkedCars ON
                // PURPOSE even though the block is the salon's own: HasLandmarkCars is the flag
                // PlaceLandmark tests before it recesses the landmark 6.6m and cuts the
                // forecourt bay across the vacated frontage - and the forecourt IS the
                // dealership. BlockBuilder's isSalon branch then fills every bay with no
                // empty-bay roll: a gap in the rank reads as a business failing.
                //
                // Fourth zone competing for the map's handful of one-cell blocks - see the
                // note on the bank above; expect the hospital measurably less often, and tune
                // this weight against ZoneFrequencySweep, not intuition.
                Palette(BlockZone.CarSalon, weight: 10f, maxShare: 1f, maxBlocks: 1,
                    maxBlockCells: 1,
                    maxLotsPerAxis: 1,
                    maxPerimeterBuildings: 2,
                    // BankNeighbours(), not Outbuildings(), for the bank's reason: a police
                    // garage in the salon's yard would be visibly orphaned. Non-empty keeps
                    // this zone out of BlockBuilder's zone branches entirely.
                    groups: new[] { BankNeighbours() },
                    landmarks: LoadAll(Buildings, "building-carwash"),
                    landmarkChance: 1f,
                    groundPatchChance: 0.25f,
                    paveJoints: true,
                    alleyProps: PocketParkKit(),
                    scatter: Merge(cityTrees,
                                   LoadAll(CityProps, "bench-old")),
                    // Lower than the bank's 0.18: the full forecourt rank is this block's
                    // dressing, and props crowding it would say car park, not showroom.
                    scatterDensity: 0.12f,
                    parkingChance: 0.5f,
                    // The stock. car-passenger and car-veteran are in paintableVehicles, so
                    // VehicleTinter gives each bake its own colour - a rank of one body in
                    // many colours is what a dealership's line-up looks like, where a rank
                    // of identical bakes would read as a fleet depot. The hero bucket keeps
                    // its baked colours: car-formula is referenced nowhere else in the city,
                    // and a formula car on the apron is exactly what an 80s dealer parked
                    // in the window. All four fit VehiclePicker's 5.6 x 2.4 stall test.
                    landmarkCars: new[]
                    {
                        Bucket("Showroom stock", 3f, CarsStatic,
                               "car-passenger", "car-veteran"),
                        Bucket("Showroom heroes", 1f, CarsStatic,
                               "car-formula", "car-passenger-race"),
                    }),

                // The scatter density is up from 0.04 now that the bays register in the block's
                // occupancy list. Before, a lamp could be dropped inside a car - BuildCarRows read
                // that list and never wrote to it - so the only defence was scattering almost
                // nothing. With the bays reserved, the extra props land in the aisles and along
                // the fence, where they belong.
                // maxBlocks rather than the maxShare 0.1 this used to carry. A share cap says
                // "a bigger city gets proportionally more of these", and one car park is now the
                // rule at every map size - 0.1 rounded to one block on the shipped 9x7 map and
                // quietly to three on a forty-block one. Same idiom as the hospital and the bank.
                Palette(BlockZone.Parking, weight: 5f, maxShare: 1f, maxBlocks: 1,
                    // Half the largest 3x3 block - a car park may take up to 2x2/1x4, never
                    // swallow a full-size block.
                    maxBlockCells: 4,
                    groups: System.Array.Empty<PrefabDatabase.WeightedGroup>(),
                    ground: asphalt,
                    carRows: true,
                    // The dumpster and the second bin joined the bag, and the density went 0.1 ->
                    // 0.14 with them: a car park's aisles and fence line are exactly where a
                    // city leaves its skips, and the bays are reserved in the occupancy list so
                    // the extra props cannot land in a car.
                    scatter: Merge(LoadAll(CityProps, "bin-wheelie", "dumpster"),
                                   LoadAll(Props, "mail-box", "trash-can")),
                    scatterDensity: 0.14f,
                    fenceSegment: Load(Fences + "fence-classic.prefab"),
                    fencePost: Load(Fences + "fence-stone-tower.prefab"),
                    parkingBooth: Load(Amusement + "ticket-ride-booth.prefab")),

                // The docks. The fourth whole-block replacement (works, park, car park, port)
                // and the first zone authored since the setting moved to the 1980s - which is
                // why the ISO containers, the container ship and the speedboat are deliberate
                // rather than anachronisms to apologise for.
                //
                // WEIGHT ZERO, and that is the design, not a disabled palette: a port is not
                // a block, it is a waterfront. ZonePlanner rolls CityConfig.portChance once
                // per city and, when it lands, hands EVERY block along one map side to this
                // palette - the same fulfil-by-force shape as the bank's route. The weighted
                // roll never sees portYard palettes at all (guarded in Assign), so the
                // per-block caps below are vestigial and kept only so the asset reads sanely.
                // A landlocked seed is ordinary; every port system stands down on it.
                Palette(BlockZone.Port, weight: 0f, maxShare: 1f, maxBlocks: 0,
                    requiresMapEdge: true,
                    portYard: true,
                    groups: new[]
                    {
                        // The pieces that read as dockside at the measured pad sizes:
                        // industry-warehouse 17.6 x 17.5, industry-storage 6 x 16.4.
                        // airport-hangar (43m) and sea-refinery (32 x 37) are deliberately
                        // absent for the Industrial palette's reason - a prefab that can
                        // never be placed is worse than an absent one.
                        Detached("Docks", 100f, 2f, 6f, 0f, false,
                            "industry-warehouse", "industry-storage"),
                    },
                    // building-port-sea, 27.2 x 20 and 24m tall - the terminal building, and
                    // the reason RowDepth is 22. PortDresser gives it the widest pad rather
                    // than rolling landmarkChance, so the chance stays 0 here.
                    landmarks: LoadAll(Buildings, "building-port-sea"),
                    // The BORDERED concrete on purpose, twice over: the slab's 6m skirt is
                    // what covers the drop from the quay coping to the sunken water, and the
                    // quay strip's does the same where it overhangs. The -nb discipline every
                    // yard patch follows does not apply at the one edge that is supposed to
                    // show a face.
                    ground: concrete,
                    quayGround: concrete,
                    // The -nb water: the bordered tile-water carries the road tiles' kerb ring,
                    // and a kerb round the open sea is exactly the seam the -nb set exists to
                    // avoid. It is stretched into slabs, which its missing Tile component makes
                    // legal - same rule as every plain ground.
                    waterTile: Load(NoBorder + "tile-water-nb.prefab"),
                    pierSegment: Load(Pier + "pier-tile-straight.prefab"),
                    // crane-port only: 20.9 x 4.4 and 11.6m tall, a quay crane. crane-docks
                    // (33m, mid-height pivot) and crane-tower (72m) would dwarf the block.
                    portCranes: LoadAll(Props, "crane-port"),
                    portContainers: LoadAll(Props,
                        "cargo-shipping_blue", "cargo-shipping_green", "cargo-shipping_orange",
                        "cargo-shipping_red", "cargo-shipping_white"),
                    // ship-cargo, 82m - longer than its berth, which is correct; the water
                    // runs a cell past each end of the block to hold the overhang.
                    portShips: LoadAll(Boats, "ship-cargo"),
                    portBoats: LoadAll(Boats, "boat-fishing", "boat-speed"),
                    portProps: Merge(
                        LoadAll(Props, "anchor", "palette", "timber", "package-box"),
                        LoadAll(Beach, "buoy", "lifebuoy")),
                    portQuayLamp: Load(Props + "lantern-long.prefab"),
                    // The apron stock the forklift is nominally moving: rowed against the
                    // warehouse flanks by the dresser, the works' BuildStacks picture.
                    stackProps: Merge(
                        LoadAll(Props, "timber", "palette", "package-box", "cargo-smple"),
                        LoadAll(Construction, "planks-stack", "cement-bag-pile")),
                    // The works wall vocabulary, unchanged: concrete wall, stone piers, the
                    // military gate stretched to the same 9m opening.
                    fenceSegment: Load(Props + "wall-concrete.prefab"),
                    fencePost: Load(Fences + "fence-stone-tower.prefab"),
                    gatePrefab: Load(Military + "military-gate.prefab"),
                    // Working vehicles only, the works' own reasoning: the lorry stand and
                    // any bay this block ever grows draw from here, not from the city's
                    // saloons.
                    parkedCars: new[]
                    {
                        Bucket("Docks", 1f, CarsStatic,
                               "truck", "forklift", "car-truck-dump"),
                    },
                    // Nothing is scattered - the fourth zone to say so, for the works' reason.
                    scatterDensity: 0f,
                    parkingChance: 0f),
            };
        }

        /// <summary>
        /// The storefronts, party-walled, mixed into the palette that builds a continuous street
        /// wall. A fresh instance per palette, because each is serialised into the asset
        /// separately.
        ///
        /// This is what replaced a Commercial zone. A block that is nothing but cafes reads as a
        /// theme park and is not how the period built; a low weight here puts the odd storefront
        /// between the flats instead. cornerPreferred is the other half of it - the corner slot
        /// biases toward these, because the corner is where the tavern and the store went.
        ///
        /// Detached is wrong here and it is what the "houses in the middle of the terrace"
        /// report was actually about. Three separate things follow from the layout flag, and all
        /// three are wanted here: GapFor and SetbackFor both return 0 for a Terrace group, so the
        /// shop sits hard on the kerb flush with the flats either side; and LotKit applies its
        /// no-rear penalty to a Terrace group with no rearPrefabs, which is what keeps these off
        /// the alley elevations, the other place they were showing up. PickCornerGroup still
        /// skips the group - it carries no cornerPrefabs - so the block's four corners remain
        /// the 4/5-floor corner kit.
        ///
        /// The fire station is in here rather than in a zone of its own. A 1920s city fire house
        /// was built INTO the street wall with its doors on the pavement, not set in grounds, and
        /// at 13.65 x 10.82 it is narrower than the post office beside it - it needs no scaling
        /// and no block. It belongs in THIS bag rather than in a group of its own for a mechanical
        /// reason too: uniformStreetRuns locks a whole side to one group, so a single-prefab group
        /// would, once UniqueBuildings has spent its prefab, hand the same rejected candidate back
        /// on every attempt until the run gives up - an empty street side. In a four-prefab bag
        /// the spend is absorbed exactly as the post office's is.
        /// </summary>
        static PrefabDatabase.WeightedGroup StreetShops(float weight) =>
            new()
            {
                label = "Shops",
                weight = weight,
                layout = PrefabDatabase.PieceLayout.Terrace,
                cornerPreferred = true,
                // The tinter's stronger palette. This flag is the whole of what "commercial"
                // means to the generator - there is no Commercial zone, per the note above. The
                // fire station rides along with it: a fire house with painted doors is a fair
                // reading, and if it ever looks like a shopfront the fix is to move that one
                // prefab into the Terrace group's street list, which takes the mild tier.
                commercial = true,
                // The gun shop rides this bag by the fire station's logic exactly: a small
                // storefront built INTO the street wall, capped at one per city through
                // uniqueBuildings, and absorbed by a five-prefab bag once spent. A CEILING,
                // not a promise - per the user's call, a seed may come up without one. The
                // name sweep gives the placed instance its GunShopMarker (the player's
                // counter), not a ShopEntrance - see InteractionMarkers.
                prefabs = LoadAll(Buildings,
                    "building-cafe", "building-restaurant", "building-post",
                    "building-firestation", "building-shop-china"),
            };

        /// <summary>
        /// The yard buildings of a single-landmark block - a garage and a couple of low blocks,
        /// so the rest of the perimeter is not bare while the landmark holds one side.
        /// </summary>
        /// <summary>
        /// What stands beside the bank on its own block. Outbuildings() without the police
        /// garage: the garage belongs to a station that is elsewhere in the city now, and next to
        /// a bank it reads as a motor pool rather than as a back yard.
        /// </summary>
        static PrefabDatabase.WeightedGroup BankNeighbours() =>
            Detached("Bank neighbours", 100f, 3f, 6f, 2f, false,
                "building-house-block", "building-house-block-big");

        static PrefabDatabase.WeightedGroup Outbuildings() =>
            Detached("Outbuildings", 100f, 3f, 6f, 2f, false,
                "building-policestation-garage", "building-house-block",
                "building-house-block-big");

        /// <summary>
        /// Creates the facade tints in two tiers - a mild residential set and a stronger
        /// commercial one, hue for hue - plus the ground shades.
        ///
        /// The facade tints are full copies of atlas-LPEC on the LivingCity/Facade Tint shader
        /// (Assets/Shaders/FacadeTint.shader): URP/Lit with _BaseColor masked by the surface
        /// normal, so the tint paints the walls and leaves the roof the atlas's own colour.
        /// They cannot be Material Variants - a variant is locked to its parent's shader - so
        /// FacadeTint() re-copies the atlas's properties on every run instead, which is the
        /// same "keep tracking the parent" promise done by hand.
        ///
        /// _BaseColor still multiplies everything VERTICAL, windows included, and that is the
        /// remaining ceiling on saturation - why the commercial set stops at "painted
        /// shopfront" rather than "primary colour". The GROUND shades stay plain variants on
        /// URP/Lit: a slab is horizontal, exactly the geometry the facade shader refuses to
        /// tint, and one flat patch of atlas has nothing for a plain multiply to spoil anyway.
        /// </summary>
        static void BuildTintPalette(PrefabDatabase db)
        {
            var atlas = AssetDatabase.LoadAssetAtPath<Material>(PackMaterials + "atlas-LPEC.mat");
            if (!atlas)
            {
                Missing.Add(PackMaterials + "atlas-LPEC.mat");
                return;
            }

            var facadeShader = AssetDatabase.LoadAssetAtPath<Shader>(FacadeShaderPath);
            if (!facadeShader)
            {
                Missing.Add(FacadeShaderPath);
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

            // The same idea two steps along the pack's grey ramp, for the paint GroundPaint lays
            // over the yards. Untextured like the white, so they need no UV authoring either.
            db.paintDarkMaterial = FlatColour(PackMaterials + "Colors/18 GREY-DARK-LPEC.mat");
            db.paintLightMaterial = FlatColour(PackMaterials + "Colors/22 GREY-LIGHTEST-LPEC.mat");

            // Two steps further down the same ramp for the works yard, which is grimier than a
            // street: tyre ruts and the painted rail spur want to read as worn-in rather than as
            // markings, and an oil stain is black.
            db.grimeMaterial = FlatColour(PackMaterials + "Colors/17 GREY-DARKEST-LPEC.mat");
            db.oilMaterial = FlatColour(PackMaterials + "Colors/57 BLACK-LPEC.mat");

            db.smokeMaterial = SmokeMaterial();

            // The puff. cloud-fluffy rather than a billboard, because a soft blurred quad would
            // be the only such surface in a city built entirely from flat-shaded low poly - and
            // the pack ships no smoke texture to make one from anyway.
            var puff = Load(Clouds + "cloud-fluffy.prefab");
            var puffFilter = puff ? puff.GetComponentInChildren<MeshFilter>() : null;
            db.smokePuffMesh = puffFilter ? puffFilter.sharedMesh : null;
            if (!db.smokePuffMesh)
                Debug.LogWarning("[CityAssetBootstrap] No cloud-fluffy mesh - works chimneys " +
                                 "will fall back to billboard smoke.");

            Directory.CreateDirectory(TintDir);

            // Residential: mild but unmistakable - green, tan, brick, blue-grey, cream, olive.
            // Every entry keeps at least one channel at 1.0, because a multiplier below white
            // on all three channels only darkens the atlas into mud. The old near-white set
            // read as no variation at all; these are as far as a facade can go before the
            // multiply starts recolouring windows louder than walls (roofs are out of reach
            // now - the shader masks them - but windows are vertical and still ride along).
            db.buildingTints = new[]
            {
                FacadeTint(atlas, facadeShader, "atlas-LPEC-sage",  new Color(0.85f, 0.96f, 0.83f)),
                FacadeTint(atlas, facadeShader, "atlas-LPEC-ochre", new Color(1.00f, 0.90f, 0.72f)),
                FacadeTint(atlas, facadeShader, "atlas-LPEC-rose",  new Color(1.00f, 0.80f, 0.75f)),
                FacadeTint(atlas, facadeShader, "atlas-LPEC-cool",  new Color(0.82f, 0.88f, 1.00f)),
                FacadeTint(atlas, facadeShader, "atlas-LPEC-warm",  new Color(1.00f, 0.96f, 0.82f)),
                FacadeTint(atlas, facadeShader, "atlas-LPEC-olive", new Color(0.90f, 0.92f, 0.72f)),
            };

            // Commercial: the same six hues pushed roughly twice as far from white - a painted
            // shopfront among the flats, not a different building. Same order as above so a
            // group flipping tiers keeps its hue family.
            db.commercialTints = new[]
            {
                FacadeTint(atlas, facadeShader, "atlas-LPEC-shop-sage",  new Color(0.66f, 0.95f, 0.62f)),
                FacadeTint(atlas, facadeShader, "atlas-LPEC-shop-ochre", new Color(1.00f, 0.78f, 0.45f)),
                FacadeTint(atlas, facadeShader, "atlas-LPEC-shop-rose",  new Color(1.00f, 0.60f, 0.52f)),
                FacadeTint(atlas, facadeShader, "atlas-LPEC-shop-cool",  new Color(0.60f, 0.75f, 1.00f)),
                FacadeTint(atlas, facadeShader, "atlas-LPEC-shop-warm",  new Color(1.00f, 0.92f, 0.55f)),
                FacadeTint(atlas, facadeShader, "atlas-LPEC-shop-olive", new Color(0.82f, 0.88f, 0.45f)),
            };

            // Ground shades range far wider than the facade tints above, and deliberately so.
            // The near-white cap on a building exists because _BaseColor multiplies the WHOLE
            // atlas and drags roofs and windows along with the walls. A ground slab has neither:
            // it is one flat patch of atlas, so the multiplier only ever changes the one thing
            // it is aimed at. Held under 1.1 at the top so a slab never blows out to white, and
            // over 0.7 at the bottom so a yard never reads as a hole.
            db.groundTints = new[]
            {
                Tint(atlas, "atlas-LPEC-ground-dark",  new Color(0.74f, 0.74f, 0.75f)),
                Tint(atlas, "atlas-LPEC-ground-cool",  new Color(0.83f, 0.85f, 0.88f)),
                Tint(atlas, "atlas-LPEC-ground-plain", new Color(0.90f, 0.90f, 0.90f)),
                Tint(atlas, "atlas-LPEC-ground-warm",  new Color(0.97f, 0.94f, 0.88f)),
                Tint(atlas, "atlas-LPEC-ground-earth", new Color(0.93f, 0.87f, 0.79f)),
                Tint(atlas, "atlas-LPEC-ground-pale",  new Color(1.08f, 1.06f, 1.02f)),
            };

            // Cinders. Far darker than anything in the ground ramp above, and kept OUT of it on
            // purpose: Shade draws from groundTints uniformly for every block in the city, so an
            // ash-coloured entry there would turn up under residential terraces. This one is
            // aimed by hand, at the ring round a boiler house and the most worn yard patches.
            db.cinderTint = Tint(atlas, "atlas-LPEC-cinder", new Color(0.42f, 0.40f, 0.38f));

            // Car paint. The one palette in this file that may run at FULL strength, and it is
            // worth saying why, because every other set here is deliberately timid.
            //
            // A facade tint is capped by the atlas already being coloured - multiply can only
            // darken, so a brick wall times a strong blue is mud. A car body is not: measured off
            // atlas-albedo-LPEC.png through each model's own UVs, area-weighted per face, the
            // paint on car-passenger and car-caravan-small is #dbdbda and on car-veteran and
            // car-pickup-modern #878282. Near-white. So #dbdbda x (0.85, 0.18, 0.16) lands on
            // rgb(186, 40, 36) - a genuinely red car, not a brownish one. The comments below give
            // the resulting body colour on #dbdbda; the two greyer swatches take the same tints
            // darker, which is free variety rather than a defect - car-veteran (#878282) comes
            // out a deep maroon on the red and rgb(30, 30, 33) on the charcoal. That last one is
            // the entry to watch if any of these read wrong: it is the darkest combination in the
            // set, and a black car whose glass is darker still has very little shape left.
            //
            // Plain Tint(), never FacadeTint(): the facade shader masks the tint off up-facing
            // normals to spare the roofs, and on a car the roof and the bonnet are exactly the
            // parts you are painting. Nothing else needs a mask either - the glass (#16252d), the
            // trim (#292929) and the 31% pure black are already dark, and dark stays dark under a
            // multiply. The atlas luminance does the masking the shader would have had to.
            //
            // Cream goes over 1 to brighten, the same trick atlas-LPEC-ground-pale uses above.
            db.vehicleTints = new[]
            {
                Tint(atlas, "atlas-LPEC-car-red",      new Color(0.85f, 0.18f, 0.16f)),  // rgb(186,  38,  33)
                Tint(atlas, "atlas-LPEC-car-blue",     new Color(0.20f, 0.36f, 0.79f)),  // rgb( 44,  79, 173)
                Tint(atlas, "atlas-LPEC-car-green",    new Color(0.17f, 0.54f, 0.29f)),  // rgb( 38, 118,  63)
                Tint(atlas, "atlas-LPEC-car-mustard",  new Color(0.94f, 0.74f, 0.21f)),  // rgb(206, 163,  46)
                Tint(atlas, "atlas-LPEC-car-brown",    new Color(0.53f, 0.35f, 0.21f)),  // rgb(116,  76,  45)
                Tint(atlas, "atlas-LPEC-car-cream",    new Color(1.04f, 1.01f, 0.89f)),  // rgb(228, 222, 196)
                Tint(atlas, "atlas-LPEC-car-silver",   new Color(0.78f, 0.79f, 0.81f)),  // rgb(170, 173, 178)
                Tint(atlas, "atlas-LPEC-car-charcoal", new Color(0.26f, 0.27f, 0.29f)),  // rgb( 58,  60,  64)
            };
        }

        /// <summary>
        /// One of the pack's flat colour materials, loaded straight rather than made into a
        /// variant - they carry no texture at all, so there is nothing for a variant to inherit
        /// and nothing to keep tracking.
        /// </summary>
        static Material FlatColour(string path)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material)
                Missing.Add(path);

            return material;
        }

        /// <summary>
        /// The chimney smoke material, authored here because nothing in the project can serve.
        ///
        /// Two constraints, both of which bite silently. Unity's built-in Default-ParticleSystem
        /// material is a Built-in-pipeline shader and renders MAGENTA under URP - the failure
        /// looks like a bug in the particle system rather than in the material. And the pack ships
        /// no smoke, dust or steam texture at all, so there is nothing to bind: the material is
        /// deliberately untextured, and the shape comes from the mesh instead.
        ///
        /// Surface 1 is Transparent and blend 0 is Alpha in URP's Unlit particle shader; both
        /// have to be set alongside the render-state properties, because the shader reads the
        /// numeric properties and the INSPECTOR reads the keywords, and setting only one leaves a
        /// material that renders correctly but shows as Opaque when opened.
        /// </summary>
        static Material SmokeMaterial()
        {
            const string Path = SmokeDir + "/smoke.mat";

            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (!shader)
            {
                Missing.Add("Shader: Universal Render Pipeline/Particles/Unlit");
                return null;
            }

            Directory.CreateDirectory(SmokeDir);

            var material = AssetDatabase.LoadAssetAtPath<Material>(Path);
            if (!material)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, Path);
            }

            material.shader = shader;
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetColor("_BaseColor", new Color(0.78f, 0.78f, 0.76f, 1f));
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");

            EditorUtility.SetDirty(material);
            return material;
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

        /// <summary>
        /// A facade tint: a full copy of the atlas on the roof-masking shader. Not a variant -
        /// a variant cannot change shader - so the "keep tracking the parent" promise the
        /// ground tints get from Unity is delivered here by re-copying the atlas's properties
        /// on every bootstrap run. Existing assets (including the old variant-based ones) are
        /// upgraded in place, keeping their GUIDs and whatever scenes reference them.
        /// </summary>
        static Material FacadeTint(Material atlas, Shader shader, string name, Color color)
        {
            var path = $"{TintDir}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (!material)
            {
                material = new Material(atlas);
                AssetDatabase.CreateAsset(material, path);
            }

            // Order matters: a variant's shader is locked to its parent's, so detach first.
            // CopyPropertiesFromMaterial brings over the atlas textures and floats; assigning
            // the shader afterwards keeps them, because Unity matches properties by name.
            material.parent = null;
            material.shader = atlas.shader;
            material.CopyPropertiesFromMaterial(atlas);
            material.shader = shader;
            material.shaderKeywords = atlas.shaderKeywords;
            material.renderQueue = atlas.renderQueue;
            material.globalIlluminationFlags = atlas.globalIlluminationFlags;
            material.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(material);

            return material;
        }

        static PrefabDatabase.WeightedPrefabs Bucket(
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

        /// <summary>
        /// Which prefabs are authored off the pack's front = local +Z convention, and by how much.
        ///
        /// The FLAT pieces are a hand-entered list, measured once off the meshes and cross-checked
        /// against the pack's own demo scenes: the plain 5floor block's window rows are on local
        /// -Z and its +Z is a 12-vertex blank wall; the china apartment's +Z is its least-windowed
        /// face and the demo never points it at a road; the big house-block is gable-fronted, with
        /// both Z faces blank and the windows on +/-X. house-block and house-block-old are
        /// authored front = +Z and need no entry. If one of these still shows a blank wall to a
        /// street, flip the sign of its entry.
        ///
        /// The CORNER pieces are measured here instead, every time, by CornerFacing. They were
        /// hand-entered too - at +90 and -90, on the belief that the two kits are mirrored
        /// relative to each other - and both were wrong by a quarter turn, which is why every
        /// corner in the city had one elevation facing its street and one blank. A corner piece
        /// has two finished elevations, so "which direction is the front" has no answer for it and
        /// the nearest-road oracle that produced the rest of this list cannot be used; see
        /// CornerFacing for what replaces it.
        /// </summary>
        static PrefabDatabase.FacadeYawFix[] BuildFacadeYawFixes()
        {
            var fixes = new List<PrefabDatabase.FacadeYawFix>
            {
                YawFix(Buildings + "building-block-5floor.prefab", 180f),
                YawFix(Buildings + "building-apartment-china.prefab", 180f),
                YawFix(Buildings + "building-house-block-big.prefab", 90f),

                // The salon. Measured from the FBX, not the demo (the pack's Models scene
                // drops every building at yaw 0, fronts be damned, and the carwash is in no
                // city scene): the mesh is an office block on its -X half and an open canopy
                // on pillars over the +X half, Z-symmetric, mouth on +X. With extra yaw e the
                // wall facing the street is local (-sin e, 0, cos e), so 270 puts +X - the
                // canopy mouth - to the street, a rank of stock in front of an open pavilion.
                // 90 would show the office's back windows; 0/180 its blind gable ends.
                YawFix(Buildings + "building-carwash.prefab", 270f),
            };

            // Kept only for the case where the measurement will not commit - a mesh it cannot
            // read, or a piece whose four elevations are too alike to call. Better a value that
            // was at least once looked at than a silent 0.
            var fallback = new Dictionary<string, float>
            {
                ["building-block-4floor-corner"] = 90f,
                ["building-block-5floor-corner"] = -90f,
            };

            foreach (var name in TerraceCorner)
            {
                var prefab = Load(Buildings + name + ".prefab");
                if (!prefab)
                    continue;

                var measured = CornerFacing.Measure(prefab, out var report);
                Debug.Log($"[CityAssetBootstrap] {name}: {report}");

                var extraYaw = measured ?? fallback.GetValueOrDefault(name, 0f);
                if (!measured.HasValue)
                    Debug.LogWarning($"[CityAssetBootstrap] {name}: falling back to the " +
                                     $"hand-entered {extraYaw:F0} degrees.");

                // 0 is the convention, not a correction - no entry needed, and leaving it out
                // keeps the table to the pieces that actually deviate.
                if (!Mathf.Approximately(extraYaw, 0f))
                    fixes.Add(new PrefabDatabase.FacadeYawFix { prefab = prefab, extraYaw = extraYaw });
            }

            return fixes.ToArray();
        }

        /// <summary>
        /// Every works prefab that has been measured to carry a chimney mouth, derived each run
        /// rather than typed - the same discipline BuildFacadeYawFixes applies to the corner
        /// pieces, and for the same reason: a value read off the raw FBX is mirrored in X, and a
        /// typed constant would preserve that error forever.
        ///
        /// Most of this list comes back empty and that is the expected result: a warehouse has
        /// no chimney. What the log is for is the COUNT, and the whole table was checked against
        /// the meshes before this shipped. It should read exactly:
        ///
        ///     chimney-big          1   (rise 18.4)
        ///     industry-factory     2   (twin stacks, rise 8.6 each)
        ///     industry-factory-old 1   (rise 14.6)
        ///     industry-refinery    1   (rise 5.6)
        ///     everything else      0   (roof lanterns, rise 3.1-3.2, rejected)
        ///
        /// Five vents on industry-factory-hall or seven on industry-building means the rise test
        /// has stopped discriminating; ZERO on industry-factory means the roof reference has gone
        /// back to a maximum and the twin stacks are cancelling each other out. Both of those
        /// happened during development and both look like "smoke is broken" from the scene.
        ///
        /// The terrace list is measured against ChimneyVents.HouseMinRise instead, and the reason
        /// it needs its own threshold is written up there. It should read exactly:
        ///
        ///     building-block-4floor-front   1   (rise 4.4)
        ///     building-block-4floor-short   1   (rise 4.2)
        ///     building-block-4floor-back    1   (rise 4.8)
        ///     building-block-4floor-corner  1   (rise 4.4)
        ///     building-block-5floor         1   (rise 4.0)
        ///     building-block-5floor-front   1   (rise 4.4)
        ///     building-block-5floor-short   1   (rise 4.6)
        ///     building-block-5floor-corner  1   (rise 3.5)
        ///     building-house-block          1   (rise 4.0)
        ///     building-house-block-big      0   (no cluster in the top band at all)
        ///
        /// TWO on any terrace piece means the threshold has slipped under the roof pipes at 1.1
        /// to 1.8m and every house is about to smoke from its plumbing.
        /// </summary>
        static PrefabDatabase.ChimneyVent[] BuildChimneyVents()
        {
            var vents = new List<PrefabDatabase.ChimneyVent>();

            var works = new[]
            {
                "chimney-big", "industry-factory", "industry-factory-old", "industry-factory-hall",
                "industry-warehouse", "industry-storage", "industry-refinery", "industry-building",
                "water-tower-medium",
            };

            // The terrace kit. Every piece carries the same authored chimney column - radius 1.20,
            // 56 to 57 vertices, top at 20.61 on the 4-floor pieces and 23.81 on the 5-floor - so
            // the corners and the -back are in here as well as the street elevations. A terrace is
            // built from all of them and smoke off only the fronts would read as a bug.
            //
            // One known imprecision, left alone deliberately. On building-block-4floor-corner the
            // 2m cluster link swallows two 5-vertex scraps of roof trim at y 17.9-18.2 into the
            // chimney's cluster, which drags the reported centroid from (-2.91, -3.33) to
            // (-2.47, -3.18): the mouth lands about 0.46m off the actual stack. The puff is 1.2m
            // wide at birth so it still covers the chimney, and the alternative - taking the
            // centroid from the cluster's top ring instead of the whole column - would move every
            // works mouth in the baked table too. Not worth that to gain half a metre on one
            // prefab.
            var houses = new[]
            {
                "building-block-4floor-front", "building-block-4floor-short",
                "building-block-4floor-back", "building-block-4floor-corner",
                "building-block-5floor", "building-block-5floor-front",
                "building-block-5floor-short", "building-block-5floor-corner",
                "building-house-block", "building-house-block-big",
            };

            Measure(works, ChimneyVents.WorksMinRise, "works", vents);
            Measure(houses, ChimneyVents.HouseMinRise, "house", vents);

            return vents.ToArray();
        }

        /// <summary>
        /// One family's worth of vent measurement. The threshold is passed rather than looked up,
        /// so which family a prefab belongs to is decided in one visible place.
        /// </summary>
        static void Measure(
            string[] names, float minRise, string family, List<PrefabDatabase.ChimneyVent> into)
        {
            foreach (var name in names)
            {
                var prefab = Load(Buildings + name + ".prefab");
                if (!prefab)
                    continue;

                var mouths = ChimneyVents.Measure(prefab, out var report, minRise);
                Debug.Log($"[CityAssetBootstrap] {name} ({family}, rise >= {minRise}): {report}");

                foreach (var mouth in mouths)
                    into.Add(new PrefabDatabase.ChimneyVent { prefab = prefab, local = mouth });
            }
        }

        static PrefabDatabase.ZonePalette Palette(
            BlockZone zone,
            float weight,
            float maxShare,
            PrefabDatabase.WeightedGroup[] groups,
            int maxBlocks = 0,
            int maxBlockCells = 0,
            bool guaranteed = false,
            int maxPerimeterBuildings = 0,
            GameObject[] landmarks = null,
            float landmarkChance = 0f,
            int requiredLandmark = -1,
            int guaranteedLandmark = -1,
            float landmarkScale = 1f,
            GameObject[] scatter = null,
            float scatterDensity = 0f,
            PrefabDatabase.WeightedPrefabs[] parkTrees = null,
            GameObject[] parkUndergrowth = null,
            GameObject[] parkBenches = null,
            GameObject[] alleyProps = null,
            float alleyParkingChance = 0.35f,
            bool featureStrip = false,
            GameObject[] kioskPrefabs = null,
            GameObject[] pocketParkProps = null,
            GameObject ground = null,
            bool groundPerCell = false,
            GameObject apronGround = null,
            GameObject[] parkMounds = null,
            PrefabDatabase.WeightedPrefabs[] grounds = null,
            GameObject[] courtyardGrounds = null,
            float groundPatchChance = 1f,
            bool paveJoints = false,
            bool uniformStreetRuns = false,
            int maxLotsPerAxis = 0,
            float parkingChance = 0.12f,
            bool carRows = false,
            PrefabDatabase.WeightedPrefabs[] parkedCars = null,
            PrefabDatabase.WeightedPrefabs[] landmarkCars = null,
            GameObject fenceSegment = null,
            GameObject fencePost = null,
            GameObject parkingBooth = null,
            bool industrialYard = false,
            GameObject gatePrefab = null,
            GameObject serviceRoadGround = null,
            GameObject yardConcrete = null,
            GameObject yardDirt = null,
            GameObject[] stackProps = null,
            GameObject[] chimneyProps = null,
            GameObject[] auxBuildings = null,
            // Appended rather than inserted, so no existing call site's argument list shifts.
            GameObject[] yardBarrels = null,
            GameObject[] yardCrates = null,
            GameObject[] yardSacks = null,
            GameObject[] yardFixtures = null,
            // The docks - appended for the same reason.
            bool requiresMapEdge = false,
            bool portYard = false,
            GameObject waterTile = null,
            GameObject quayGround = null,
            GameObject pierSegment = null,
            GameObject[] portCranes = null,
            GameObject[] portContainers = null,
            GameObject[] portShips = null,
            GameObject[] portBoats = null,
            GameObject[] portProps = null,
            // The park rewrite's kinds - appended for the reason the docks were.
            GameObject[] parkLamps = null,
            GameObject[] parkBins = null,
            GameObject[] parkMonuments = null,
            GameObject[] parkBoulders = null,
            GameObject[] parkDeadTrees = null,
            GameObject parkWaterTile = null,
            GameObject parkGatePiers = null,
            GameObject[] parkAmusement = null,
            // Appended after the park rewrite's block, same discipline.
            GameObject portQuayLamp = null) =>
            new()
            {
                zone = zone,
                weight = weight,
                maxShare = maxShare,
                maxBlocks = maxBlocks,
                maxBlockCells = maxBlockCells,
                guaranteed = guaranteed,
                maxPerimeterBuildings = maxPerimeterBuildings,
                groups = groups,
                uniformStreetRuns = uniformStreetRuns,
                maxLotsPerAxis = maxLotsPerAxis,
                landmarks = landmarks ?? System.Array.Empty<GameObject>(),
                landmarkChance = landmarkChance,
                requiredLandmark = requiredLandmark,
                guaranteedLandmark = guaranteedLandmark,
                landmarkScale = landmarkScale,
                scatter = scatter ?? System.Array.Empty<GameObject>(),
                scatterDensity = scatterDensity,
                parkTrees = parkTrees ?? System.Array.Empty<PrefabDatabase.WeightedPrefabs>(),
                parkUndergrowth = parkUndergrowth ?? System.Array.Empty<GameObject>(),
                parkBenches = parkBenches ?? System.Array.Empty<GameObject>(),
                alleyProps = alleyProps ?? System.Array.Empty<GameObject>(),
                alleyParkingChance = alleyParkingChance,
                featureStrip = featureStrip,
                kioskPrefabs = kioskPrefabs ?? System.Array.Empty<GameObject>(),
                pocketParkProps = pocketParkProps ?? System.Array.Empty<GameObject>(),
                ground = ground,
                groundIsTilePerCell = groundPerCell,
                apronGround = apronGround,
                parkMounds = parkMounds ?? System.Array.Empty<GameObject>(),
                grounds = grounds ?? System.Array.Empty<PrefabDatabase.WeightedPrefabs>(),
                courtyardGrounds = courtyardGrounds ?? System.Array.Empty<GameObject>(),
                groundPatchChance = groundPatchChance,
                paveJoints = paveJoints,
                parkingChance = parkingChance,
                carRows = carRows,
                parkedCars = parkedCars ?? System.Array.Empty<PrefabDatabase.WeightedPrefabs>(),
                landmarkCars = landmarkCars ?? System.Array.Empty<PrefabDatabase.WeightedPrefabs>(),
                fenceSegment = fenceSegment,
                fencePost = fencePost,
                parkingBooth = parkingBooth,
                industrialYard = industrialYard,
                gatePrefab = gatePrefab,
                serviceRoadGround = serviceRoadGround,
                yardConcrete = yardConcrete,
                yardDirt = yardDirt,
                stackProps = stackProps ?? System.Array.Empty<GameObject>(),
                chimneyProps = chimneyProps ?? System.Array.Empty<GameObject>(),
                auxBuildings = auxBuildings ?? System.Array.Empty<GameObject>(),
                yardBarrels = yardBarrels ?? System.Array.Empty<GameObject>(),
                yardCrates = yardCrates ?? System.Array.Empty<GameObject>(),
                yardSacks = yardSacks ?? System.Array.Empty<GameObject>(),
                yardFixtures = yardFixtures ?? System.Array.Empty<GameObject>(),
                requiresMapEdge = requiresMapEdge,
                portYard = portYard,
                waterTile = waterTile,
                quayGround = quayGround,
                pierSegment = pierSegment,
                portCranes = portCranes ?? System.Array.Empty<GameObject>(),
                portContainers = portContainers ?? System.Array.Empty<GameObject>(),
                portShips = portShips ?? System.Array.Empty<GameObject>(),
                portBoats = portBoats ?? System.Array.Empty<GameObject>(),
                portProps = portProps ?? System.Array.Empty<GameObject>(),
                portQuayLamp = portQuayLamp,
                parkLamps = parkLamps ?? System.Array.Empty<GameObject>(),
                parkBins = parkBins ?? System.Array.Empty<GameObject>(),
                parkMonuments = parkMonuments ?? System.Array.Empty<GameObject>(),
                parkBoulders = parkBoulders ?? System.Array.Empty<GameObject>(),
                parkDeadTrees = parkDeadTrees ?? System.Array.Empty<GameObject>(),
                parkWaterTile = parkWaterTile,
                parkGatePiers = parkGatePiers,
                parkAmusement = parkAmusement ?? System.Array.Empty<GameObject>(),
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

        /// <summary>
        /// The re-authored storage-pack props of one role, by filename prefix.
        ///
        /// A prefix glob rather than a list of names, which is the opposite of the rule the header
        /// states for every other load here ("every prefab path was read off disk"). The reason it
        /// is right in this one case: these prefabs do not exist until AuthorYardStock writes
        /// them, and it is what names them - so the prefix is a contract this file has with
        /// itself, not a guess about somebody else's pack. Getting it wrong is a compile-time-ish
        /// error you see in the authoring log, not a silent empty list.
        ///
        /// Silent when the folder is absent, and that is deliberate. Routing this through Load
        /// would put eighteen paths into the missing-prefab report on every run of a project that
        /// simply has not imported the pack - which would train the reader to ignore the one
        /// report that catches a real rename.
        /// </summary>
        static GameObject[] YardKit(string prefix)
        {
            if (!Directory.Exists(YardStock))
                return System.Array.Empty<GameObject>();

            return AssetDatabase.FindAssets("t:Prefab", new[] { YardStock.TrimEnd('/') })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => Path.GetFileNameWithoutExtension(p)
                                .StartsWith(prefix + "-", System.StringComparison.Ordinal))
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
