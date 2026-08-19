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
            "SM_Veh_Sedan_01",
            "SM_Veh_Suv_01",
        };

        /// <summary>What the law drives. The marked cars, and the only bodies a patrol,
        /// a dispatch answer or a station forecourt is dealt.</summary>
        public static readonly string[] PoliceCars =
        {
            "SM_Veh_Sedan_01_Preset_Police",
            "SM_Veh_Pickup_01_Preset_Police",
        };

        /// <summary>The force's own pack. EVERY vehicle in it wears a livery - its
        /// materials are Police_Vehicle_01..12 - and its names give nothing away
        /// ("SM_Veh_Car_01", "SM_Veh_Van_01"), which is how four marked bodies used to
        /// end up in civilian traffic: a name filter looking for "police" found none of
        /// them. The folder is the rule instead.</summary>
        public const string PoliceFleetFolder =
            "Assets/Synty/PolygonPoliceStation/Prefabs/Vehicles/";

        /// <summary>Marked bodies that live outside that pack - the palm city's two
        /// presets (the fleet the patrols actually drive, <see cref="PoliceCars"/>) and
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

            var name = FileName(nameOrPath);
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

            var name = FileName(nameOrPath);
            foreach (var marked in Service)
                if (name == marked || name.StartsWith(marked + "_"))
                    return true;
            return false;
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

            var name = FileName(nameOrPath);
            foreach (var barred in Barred)
                if (name == barred || name.StartsWith(barred + "_"))
                    return true;
            return false;
        }

        /// <summary>The prefab name out of an asset path, with the extension off; a bare
        /// name comes back as it went in.</summary>
        static string FileName(string path)
        {
            var cut = path.LastIndexOfAny(new[] { '/', '\\' });
            var name = cut >= 0 ? path.Substring(cut + 1) : path;
            return name.EndsWith(".prefab") ? name.Substring(0, name.Length - 7) : name;
        }
    }
}
