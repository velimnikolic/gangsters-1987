namespace LivingCity.Gameplay
{
    /// <summary>
    /// Which pack vehicles the game is allowed to put on a street, and in whose hands.
    /// Picked by hand off the cast catalog (Tools > City > Catalog > Build Cast Catalog
    /// Scene), the same way the gangster bodies were - so the two decisions that shape
    /// every street picture live in one file each instead of in a dozen literals.
    ///
    /// Plain pack prefab names, resolved wherever the caller already resolves cars: the
    /// road demo's folder scan, the crew demo's AssetDatabase load, the picture desk's
    /// PortraitStudio. Engine-free, so the headless suite can hold it to its own rules.
    /// </summary>
    public static class VehicleCatalog
    {
        /// <summary>What a mob drives. The outfit's car and every rival's come out of
        /// here - a wise guy in a golf cart is not a wise guy.</summary>
        public static readonly string[] GangsterCars =
        {
            "06_Bayside_Classic",
            "09_Bayside_Ranger",
        };

        /// <summary>The two-wheelers that may be ridden, in the order a street wants
        /// them: a workaday motorcycle first, the moped that delivers everybody's
        /// dinner behind it. All of them are 1987 machines - the two electric ones the
        /// pack also ships are on the <see cref="Barred"/> list with the survey car and
        /// the pavement robot, and belong to another decade.
        ///
        /// Kept apart from <see cref="GangsterCars"/> and from the folder scans on
        /// purpose. Every scan in the project denies "bike", "moped" and "scooter" by
        /// name (RoadDemoBuilder's vehicleDeny), because for years a two-wheeler in the
        /// traffic was a car-shaped thing sliding along with nobody on it. That denial
        /// stays; a bike now reaches the street the way a cruiser does, by being asked
        /// for BY NAME, and what asks is the code that also knows how to seat a man on
        /// it (StreetBikes, RoadBike).</summary>
        public static readonly string[] Motorcycles =
        {
            "SM_Veh_Motorbike_01",
            "SM_Veh_Moped_01",
        };

        /// <summary>The law's own two-wheeler: the police pack's tourer. Liveried, so
        /// <see cref="IsMarkedService"/> already keeps it out of civilian traffic - it
        /// is named here for whoever puts a patrolman on one.</summary>
        public static readonly string[] PoliceMotorcycles =
        {
            "SM_Veh_Motorbike_01",
            "SM_Veh_Motorbike_02",
        };

        public const string PoliceTransportModel = "11_Borough_Warden";
        public const string PoliceTransportPath = CivilianVehicleCatalog.Folder + PoliceTransportModel + ".prefab";

        /// <summary>The existing marked pickup and the authored custody van. Both use
        /// the shared force roster, dispatch and prisoner transport lifecycle.</summary>
        public static readonly string[] PoliceCars =
        {
            "SM_Veh_Pickup_01_Preset_Police",
            PoliceTransportModel,
        };

        /// <summary>The force's own pack. EVERY vehicle in it wears a livery - its
        /// materials are Police_Vehicle_01..12 - and its names give nothing away
        /// ("SM_Veh_Car_01", "SM_Veh_Van_01"), which is how four marked bodies used to
        /// end up in civilian traffic: a name filter looking for "police" found none of
        /// them. The folder is the rule instead.</summary>
        public const string PoliceFleetFolder =
            "Assets/Synty/PolygonPoliceStation/Prefabs/Vehicles/";

        /// <summary>Marked bodies that live outside that pack - the palm city's two
        /// presets (of which the pickup is the fleet patrols actually drive,
        /// <see cref="PoliceCars"/>) and
        /// the city pack's own cruiser. Named here so a scan that only ever sees a
        /// prefab name still knows them.</summary>
        static readonly string[] Liveried =
        {
            "SM_Veh_Sedan_01_Preset_Police",
            "SM_Veh_Pickup_01_Preset_Police",
            "SM_Veh_Car_Police_01",
        };

        /// <summary>Whether this prefab - by asset path or by bare name - belongs to the
        /// law. Civilian traffic, parked cars and street dressing all drop what this
        /// answers true for: a marked car in the flow of ordinary traffic reads as a
        /// patrol that is ignoring the city, which is worse than no patrol at all.
        ///
        /// Deliberately NOT a bar (<see cref="IsBarred"/>): these bodies are wanted, in
        /// the force's hands. A caller that asks for one BY NAME - a cruiser for a
        /// patrol, a station forecourt, the crew demo's black sedan repainted out of a
        /// police body - is asking on purpose and is not filtered.
        ///
        /// A path is answered by its folder, which catches the whole liveried pack; a
        /// bare name falls back to <see cref="Liveried"/> plus the presets, so the
        /// pack's own generic names ("SM_Veh_Car_01") are only safe to ask about with a
        /// path. Scans have one - they are walking folders.</summary>
        public static bool IsPoliceVehicle(string nameOrPath)
        {
            if (string.IsNullOrEmpty(nameOrPath))
                return false;

            if (nameOrPath.Replace('\\', '/').Contains(PoliceFleetFolder))
                return true;

            var name = BareName(nameOrPath);
            if (name == "BOROUGH WARDEN") return true;
            foreach (var marked in Liveried)
                if (name == marked || name.StartsWith(marked + "_"))
                    return true;
            foreach (var marked in PoliceCars)
                if (name == marked || name.StartsWith(marked + "_"))
                    return true;
            return false;
        }

        /// <summary>The other liveried bodies - somebody's marked vehicle rather than a
        /// car a citizen drives. An ambulance and a coastguard pickup are on service
        /// whenever they are seen, so idling in a queue of traffic is the one thing
        /// they cannot be doing; the coastguard is also thirty miles inland out there.
        ///
        /// The taxis, the food sedan and the construction pickup are NOT here on
        /// purpose - a livery is not the test, being on a call is. A taxi in traffic is
        /// a taxi doing its job.</summary>
        static readonly string[] Service =
        {
            "SM_Veh_Car_Ambo_01",
            "SM_Veh_Pickup_01_Preset_Coastguard",
        };

        /// <summary>Whether this prefab is anybody's marked vehicle - the law
        /// (<see cref="IsPoliceVehicle"/>) or the other services. THE question the
        /// civilian pools ask: traffic, kerbside parking and street dressing take what
        /// this answers false for and nothing else.
        ///
        /// A filter and not a bar, exactly like the police half: a caller that names one
        /// still gets it, which is how a hospital lot still parks ambulances
        /// (BlockParkingBay's Emergency fleet).</summary>
        public static bool IsMarkedService(string nameOrPath)
        {
            if (IsPoliceVehicle(nameOrPath))
                return true;
            if (string.IsNullOrEmpty(nameOrPath))
                return false;

            var name = BareName(nameOrPath);
            foreach (var marked in Service)
                if (name == marked || name.StartsWith(marked + "_"))
                    return true;
            return false;
        }

        /// <summary>The bodies whose paint IS their livery - a cab's yellow, the food sedan's
        /// house colours, the works pickup's site orange. They are wanted in traffic and are
        /// not filtered out of it, but nothing may repaint them: strip the colour off a cab
        /// and what is left is a saloon wearing a roof sign.</summary>
        static readonly string[] Liveries =
        {
            "SM_Veh_Sedan_01_Preset_Taxi",
            "SM_Veh_Pickup_01_Preset_Taxi",
            "SM_Veh_Sedan_01_Preset_Food",
            "SM_Veh_Pickup_01_Preset_Construction",
            "SM_Veh_Car_Taxi_01",
        };

        /// <summary>Whether this body carries somebody's colours - the law and the services
        /// (<see cref="IsMarkedService"/>) or a trade's own livery. THE question VehiclePaint
        /// asks: every civilian body that answers false is given a colour of its own, and the
        /// ones that answer true keep the paint the pack shipped.</summary>
        public static bool WearsLivery(string nameOrPath)
        {
            if (IsMarkedService(nameOrPath))
                return true;
            if (string.IsNullOrEmpty(nameOrPath))
                return false;

            var name = BareName(nameOrPath);
            foreach (var liveried in Liveries)
                if (name == liveried || name.StartsWith(liveried + "_"))
                    return true;
            return false;
        }

        /// <summary>How many seats a body is given in a pool that is drawn from uniformly.
        /// The ordinary saloon, pickup, van and SUV take this many; see
        /// <see cref="PoolWeight"/> for the bodies that take fewer.</summary>
        public const int OrdinaryWeight = 6;

        /// <summary>One body and the number of seats it may have.</summary>
        public readonly struct Rarity
        {
            public readonly string Name;
            public readonly int Weight;

            public Rarity(string name, int weight) { Name = name; Weight = weight; }
        }

        /// <summary>The bodies that must not turn up as often as a saloon. Every pool in the
        /// game is a flat list drawn from uniformly, so a folder scan made the two supercars
        /// and the muscle car three of seventeen bodies - one car in six on a 1987 street was
        /// an exotic, which reads as a showroom rather than as a city.
        ///
        /// Weights rather than a bar: these are wanted, rarely. At the numbers below the pair
        /// of supercars is about one car in forty, the muscle car and the beach buggy about
        /// one in twenty-five each.</summary>
        public static readonly Rarity[] Uncommon =
        {
            new Rarity("SM_Veh_Supercar_01", 1),
            new Rarity("SM_Veh_Supercar_02", 1),
            new Rarity("SM_Veh_Car_Muscle_01", 2),
            new Rarity("SM_Veh_Buggy_01", 2),
        };

        /// <summary>The seats this body takes in a pool - <see cref="OrdinaryWeight"/> unless
        /// it is named in <see cref="Uncommon"/>. A caller that gathers cars adds the prefab
        /// this many times and draws uniformly as it always did; the weighting is in the list
        /// rather than in the draw, so the three call sites need not agree on an rng.</summary>
        public static int PoolWeight(string nameOrPath)
        {
            if (string.IsNullOrEmpty(nameOrPath))
                return OrdinaryWeight;

            var name = BareName(nameOrPath);
            foreach (var rare in Uncommon)
                if (name == rare.Name || name.StartsWith(rare.Name + "_"))
                    return rare.Weight;
            return OrdinaryWeight;
        }

        /// <summary>Bodies that must never reach a scene - not as traffic, not parked,
        /// not as a photograph, not as street dressing. Filtered by name wherever
        /// vehicles are gathered, so a folder scan cannot quietly re-adopt one.
        ///
        /// The second group is the calendar rather than the picture: the game is 1987,
        /// and these were not on any street in it.
        ///
        ///   Lidar        a roof-mounted survey rig - street-mapping cars are 2007 on
        ///   Delivery_Bot a pavement delivery robot
        ///   E_Bike       electric bicycle
        ///   E_Scooter    rental kick-scooter
        ///   Pickup Taxi  a taxi-liveried pickup truck; an American cab is a sedan,
        ///                then and now, so this one is not the calendar but the map
        ///
        /// The scans deny most of these by name already ("bot", "bike", "scooter"), but
        /// a deny list is one builder's housekeeping - barring them here is the game
        /// saying it, once, for every scene.</summary>
        public static readonly string[] Barred =
        {
            "SM_Veh_Limousine_01",
            "SM_Veh_Hot_Dog_Cart_01",
            "SM_Veh_Juice_Cart_01",
            "SM_Veh_Sedan_01_Preset_Lidar",
            "SM_Veh_Delivery_Bot_01",
            "SM_Veh_E_Bike_01",
            "SM_Veh_E_Scooter_01",
            "SM_Veh_Pickup_01_Preset_Taxi",
        };

        /// <summary>Whether this prefab name - or asset path ending in one - is barred.
        /// Substring-free on purpose: "SM_Veh_Limousine_01_Insert_01" is a part of the
        /// barred car and goes out with it, but nothing else may be caught by accident.
        /// </summary>
        public static bool IsBarred(string nameOrPath)
        {
            if (string.IsNullOrEmpty(nameOrPath))
                return false;

            var name = BareName(nameOrPath);
            foreach (var barred in Barred)
                if (name == barred || name.StartsWith(barred + "_"))
                    return true;
            return false;
        }

        /// <summary>The prefab name out of an asset path, with the extension off; a bare
        /// name comes back as it went in.
        ///
        /// Also takes off what a SCENE adds to a name, which a path never carries: the
        /// "(Clone)" Instantiate hangs on an instance, and the " (frame)" a bike's model
        /// wears under its driving pivot (RoadBike.Build). Both are there so a caller
        /// holding a live transform rather than a prefab can still ask these questions -
        /// which is how a car finds its own machine (<see cref="VehiclePerformance"/>)
        /// without every builder in the game having to hand it one.</summary>
        static readonly char[] Separators = { '/', '\\' };

        public static string BareName(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";

            var cut = path.LastIndexOfAny(Separators);
            var name = cut >= 0 ? path.Substring(cut + 1) : path;
            if (name.EndsWith(".prefab")) name = name.Substring(0, name.Length - 7);
            if (name.EndsWith("(Clone)")) name = name.Substring(0, name.Length - 7);
            
            if (name.EndsWith(" (frame)")) name = name.Substring(0, name.Length - 8);
            return name.TrimEnd();
        }
    }
}
