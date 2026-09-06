using LivingCity.Personnel;

namespace LivingCity.Outfit
{
    public readonly struct ArmoryItem
    {
        public readonly EquipmentKind Kind;
        public readonly string DisplayName;
        public readonly int Price;

        /// <summary>The dealer's one line of truth about the merchandise - shown in the
        /// catalogue so the player buys with open eyes.</summary>
        public readonly string Note;

        /// <summary>The Synty pack prefab the catalogue photographs, by exact name (see
        /// LedgerModelSet.weapons). Null falls back to the kind's slot - which is why two
        /// listings of one kind (the .38 and the twin pack are both a pistol body)
        /// can still name what they photograph. Vehicles name their exact prefab key; BodyFor shares it with
        /// the catalogue photograph and physical delivery.</summary>
        public readonly string ModelName;

        public ArmoryItem(EquipmentKind kind, string displayName, int price, string note,
            string modelName = null)
        {
            Kind = kind;
            DisplayName = displayName;
            Price = price;
            Note = note;
            ModelName = modelName;
        }
    }

    /// <summary>
    /// What money buys. Pure tables (the WeaponCatalog zero-rule world is the PLAYER
    /// arsenal's; this is the outfit's stock and stays data-only). Prices follow the
    /// reference sheet; vehicles are priced on the same scale so the first car is a
    /// real decision against a second tommy gun.
    /// </summary>
    public static class ArmoryCatalog
    {
        /// <summary>
        /// The counter, weakest first - .38, twin pack, shotgun, machine pistol,
        /// rifle, tommy gun - and priced up that same ladder. Guns only - the
        /// PolygonGangWarfare pack's bats and blades are deliberately NOT stock, because
        /// an outfit's armory arms men rather than equipping a brawl. Bodies are that
        /// pack's, with the plain rifle borrowed from PolygonPalmCity, which ships the
        /// only long gun that is not gold.
        /// </summary>
        public static readonly ArmoryItem[] Weapons =
        {
            // The firearms, weakest to strongest - and priced in that order, so the
            // counter reads as a ladder: what a man can hit, how hard, how far. The
            // .38 is not for sale: every man of the outfit carries one by default,
            // and the counter sells what is BETTER than the gun in his coat.
            new ArmoryItem(EquipmentKind.TwinPistols, "Twin Pack Pistols", 150,
                "A pistol for each hand - twice the lead at the same range.",
                "SM_Wep_Pistol_Revolver_01"),
            new ArmoryItem(EquipmentKind.Shotgun, "Shotgun", 300,
                "Devastating up close; nothing past the kerb.",
                "SM_Wep_Shotgun_01"),
            new ArmoryItem(EquipmentKind.MachinePistol, "Machine Pistol", 600,
                "Empties a clip across a room and hits what it pleases.",
                "SM_Wep_Machine_Pistol_01"),
            new ArmoryItem(EquipmentKind.Rifle, "Rifle", 800,
                "Longest range, highest accuracy - a street away.",
                "SM_Wep_Rifle_01"),
            new ArmoryItem(EquipmentKind.TommyGun, "Tommy Gun", 2_000,
                "High damage at range - wildly inaccurate in unskilled hands.",
                "SM_Wep_SubMachineGun_01"),
        };

        /// <summary>The authored civilian vehicles and armoured crew truck, cheapest first.
        /// Showroom value bands set their campaign purchase prices. Police transport
        /// is not merchandise. ModelName is the exact prefab key
        /// shared by the catalogue photograph, delivery and campaign travel model.</summary>
        public static readonly ArmoryItem[] Vehicles =
        {
            new ArmoryItem(EquipmentKind.Vehicle, "SRBO", 3_990,
                "A small three-door hatchback with plain steel wheels.", "14_Borough_Mica"),
            new ArmoryItem(EquipmentKind.Vehicle, "HIKARI DX", 8_500,
                "A compact everyday sedan with a modest price and footprint.", "07_Hikari_DX"),
            new ArmoryItem(EquipmentKind.Vehicle, "BAYSIDE CLASSIC", 12_500,
                "An ordinary full-size sedan with a roomy cabin.", "06_Bayside_Classic"),
            new ArmoryItem(EquipmentKind.Vehicle, "CALDER VOYAGER", 15_500,
                "A six-seat passenger van with three rows and windows all the way back.", "13_Calder_Voyager"),
            new ArmoryItem(EquipmentKind.Vehicle, "BAYSIDE TRAIL", 16_500,
                "A short three-door 4x4 with a rear-mounted spare.", "08_Bayside_Trail"),
            new ArmoryItem(EquipmentKind.Vehicle, "MONARCH TOWNLINE", 22_500,
                "A broad American luxury sedan with a formal roof.", "05_Monarch_Townline"),
            new ArmoryItem(EquipmentKind.Vehicle, "CALDER MARIVELLE", 23_000,
                "A long luxury sedan with a padded vinyl roof.", "04_Calder_Marivelle"),
            new ArmoryItem(EquipmentKind.Vehicle, "BAYSIDE RANGER", 23_500,
                "A full-size two-door SUV with a tall, broad body.", "09_Bayside_Ranger"),
            new ArmoryItem(EquipmentKind.Vehicle, "VAHREN DREI", 27_000,
                "A compact sporting sedan with a restrained silver finish.", "Vahren_Drei"),
            new ArmoryItem(EquipmentKind.Vehicle, "MONARCH BASTION", 30_000,
                "A four-door armoured crew truck with a protected cab and open cargo bed.", "12_Monarch_Bastion"),
            new ArmoryItem(EquipmentKind.Vehicle, "ALBION HIGHLAND", 35_000,
                "An executive 4x4 with alloy wheels and bright trim.", "10_Albion_Highland"),
            new ArmoryItem(EquipmentKind.Vehicle, "ALBION SIX", 37_000,
                "A low sporting luxury sedan with a long sculpted bonnet.", "03_Albion_Six"),
            new ArmoryItem(EquipmentKind.Vehicle, "KRONEN K58", 64_000,
                "A premium executive import with a long rear cabin.", "02_Kronen_K58"),
            new ArmoryItem(EquipmentKind.Vehicle, "REGENT BELLAVERE", 119_000,
                "A long-wheelbase luxury sedan with ivory coachwork.", "01_Regent_Bellavere"),
        };

        /// <summary>
        /// The counter's second shelf of wheels, and a different thing from the first.
        /// A car is how a crew gets to work; a motorcycle is how two men get past a
        /// man's front door with a gun and are round the corner before the street has
        /// turned its head. It carries no crew and it counts as no crew's vehicle
        /// (CrewKit.HasVehicle) - what it buys is the drive-by, and the counter prices
        /// it that way: the machine that will do the job properly costs more than the
        /// jalopy that would carry four men, and the two that would not cost a good
        /// deal less.
        ///
        /// Four machines. Two are Palm City's - the motorbike a man would choose and the
        /// moped that delivers everybody's dinner - and the other two are the outfit's
        /// own: the police pack's big tourer with the force cut off it and painted black
        /// (GangBikeBaker), and the 450 enduro, which is no pack body at all but a model
        /// brought in from outside them and baked into a prefab (EnduroBikeBaker). The
        /// pack's own liveried tourer stays the law's (VehicleCatalog.PoliceMotorcycles),
        /// and the two electric machines that pack also ships are barred by the calendar.
        ///
        /// THE ENDURO IS THE ONE THAT LEAVES THE ROAD. It is the cheapest thing on the
        /// shelf that will still carry two men and get away, and what it is sold for is
        /// what a dirt bike is for: it does not care what it is standing on, it is off
        /// down an alley or across a lot while a car is still looking for the turning.
        /// Priced under the road machines because it is a stripped competition bike with
        /// no plates, no lights worth the name and nowhere to hide a long gun.
        ///
        /// THE SCOOTER WAS THE THIRD AND IS NOT ANY MORE, and the reason is measured
        /// rather than felt. Read off the pack by BikeBody at runtime: the motorbike has
        /// a 2.45 m wheelbase on 0.57 m wheels, the moped 1.49 on 0.35 - and
        /// SM_Veh_Scooter_01 has 0.80 m on 0.20 m wheels, four of them (Wheel_FL/FR/
        /// RL/RR), and no handlebar part the pose can find, so a rider's fists are put
        /// on a guess. It is a mobility scooter, not a motorcycle. Two armed men cannot
        /// be seated on eighty centimetres of it at any spacing, and the counter does
        /// not sell a machine that cannot do the job it is sold for.
        ///
        /// What plays each listing is PortraitStudio.VehicleModelFor, so the body in the
        /// catalogue cut is the body that turns up at the kerb (CrewCars.BodyFor reads
        /// the same table). THE MOPED SOLD HERE IS NOT QUITE THE PACK'S: the counter's
        /// is SM_Veh_Moped_01_NoBox, a variant with the delivery box taken off, because
        /// the pack bolts that box over the machine's back third - where the second man
        /// sits. The traffic's mopeds keep theirs.
        /// </summary>
        public static readonly ArmoryItem[] Motorcycles =
        {
            new ArmoryItem(EquipmentKind.Motorcycle, "Tourer", 8_545,
                "Big, black and built for two - the machine for a job across town."),
            new ArmoryItem(EquipmentKind.Motorcycle, "Motorbike", 2_500,
                "Two men, a gun off the back, and gone before the street looks up."),
            new ArmoryItem(EquipmentKind.Motorcycle, "Enduro", 1_500,
                "A dirt bike. Down an alley, over a lot, gone where a car cannot follow."),
            new ArmoryItem(EquipmentKind.Motorcycle, "Moped", 500,
                "Nobody looks twice at a delivery boy. Nobody hears one coming either."),
        };

        /// <summary>
        /// The counter's back shelf: what a crew throws. A grenade is not RANKED into a
        /// man's hand the way a gun is (RosterOps.IsGrenade - no stat sorts charges,
        /// one is the same as the next) - it is a countable charge, thrown at a rival or
        /// a shopfront or laid under a car (DemoCrews.Bomb). Bought one at a time exactly
        /// as a gun is (each BUY one charge on the books), and given either to a
        /// LIEUTENANT as his crew's loose stock, from this page, or straight into a named
        /// man's hand - a corner hood's included - from the grenade drawer on his own
        /// file, which is where the boss says WHO carries the bomb. Struck off the moment
        /// it is thrown, so a crew's count is what it has been given and not yet spent.
        /// Priced so a handful is a real call against a better gun.
        /// </summary>
        public static readonly ArmoryItem[] Explosives =
        {
            new ArmoryItem(EquipmentKind.Grenade, "Grenade", 175,
                "One pineapple - thrown at a man or a shopfront, or laid under a car.",
                "SM_Wep_Grenade_01"),
        };

        /// <summary>The pack body that PLAYS a listing, by exact prefab name. One table,
        /// and it used to live in PortraitStudio - which meant the only code that could
        /// ask what a "Panel Van" actually is was code that could touch UnityEngine. The
        /// catalogue's photograph (PortraitStudio), the body at the kerb
        /// (CrewCars.BodyFor) and the campaign's own arithmetic (CrewKit.MachineOf, which
        /// wants to know how fast the thing is) all read this, so it belongs beside the
        /// prices rather than beside the camera.
        ///
        /// Three listings name a body this project MADE rather than one a pack ships -
        /// the armoured wagon, the blacked tourer, the boxless moped - and the comments
        /// on the arrays above say why for each.</summary>
        public static string BodyFor(string displayName)
        {
            foreach (var item in Vehicles)
                if (item.DisplayName == displayName) return item.ModelName;
            return LegacyBodyFor(displayName);
        }

        // Existing inventory/save names still resolve after leaving the sales shelf.
        static string LegacyBodyFor(string displayName) => displayName switch
        {
            "Jalopy" => "14_Borough_Mica",
            "Sedan" => "06_Bayside_Classic",
            "Panel Van" => "13_Calder_Voyager",
            "Armoured Wagon" => "12_Monarch_Bastion",
            // The two-wheelers, Palm City's. Named exactly, and never by a substring:
            // "Motorbike" also names the police pack's liveried tourer, and the outfit
            // does not ride one of those.
            "Motorbike" => "SM_Veh_Motorbike_01",
            // THE TOURER IS NO PACK BODY AT ALL. The other machine big enough to carry
            // two armed men is the police pack's, and it comes dressed as a patrol bike -
            // panniers, top box, mast, chequer. GangBikeBaker cuts all that off and paints
            // it black, and Assets/Prefabs/Vehicles/SM_Veh_Motorbike_Tourer_Black.prefab
            // is what this listing sells. The law still rides the pack's own
            // (VehicleCatalog.PoliceMotorcycles), untouched.
            "Tourer" => "SM_Veh_Motorbike_Tourer_Black",
            // THE ENDURO CAME FROM OUTSIDE THE PACKS ALTOGETHER - a .glb read and baked
            // into Assets/Prefabs/Vehicles/SM_Veh_Motorbike_Enduro_450.prefab by
            // EnduroBikeBaker, which is also where the note on how big it was scaled
            // lives. Like the tourer it is in no traffic bucket: the city never rides one,
            // the outfit does.
            "Enduro" => "SM_Veh_Motorbike_Enduro_450",
            // THE OUTFIT'S MOPED IS THE BOXLESS ONE. The pack ships the moped with a
            // delivery box bolted over its back third (SM_Veh_Moped_01_Box, 0.61 m of
            // it centred 0.79 m behind the axle line) - which is exactly where a
            // pillion sits, so a man on the back of the stock machine rides inside the
            // luggage. Assets/Prefabs/Vehicles/SM_Veh_Moped_01_NoBox.prefab is a
            // variant of the pack's with that box taken off, and it is what the
            // counter sells and what CrewCars stands at the kerb. The traffic's
            // delivery mopeds are untouched (VehicleCatalog.Motorcycles still names
            // the stock body) - a delivery boy keeps his box.
            "Moped" => "SM_Veh_Moped_01_NoBox",
            "Scooter" => "SM_Veh_Scooter_01",
            _ => "06_Bayside_Classic",
        };

        /// <summary>3.0 Combat stars. Below this, handing a man the tommy gun earns
        /// the amber warning - and is allowed, because the mistake is the player's to
        /// make (the promotion rule's discipline).</summary>
        public const int TommyGunCombatFloor = 6;
    }
}
