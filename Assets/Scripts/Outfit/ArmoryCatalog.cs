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
        /// can still name what they photograph. Vehicles name nothing: PortraitStudio finds their bodies in
        /// the city's own PrefabDatabase.</summary>
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
            new ArmoryItem(EquipmentKind.TwinPistols, "Twin Pack Pistols", 250,
                "A pistol for each hand - twice the lead at the same range.",
                "SM_Wep_Pistol_Revolver_01"),
            new ArmoryItem(EquipmentKind.Shotgun, "Shotgun", 750,
                "Devastating up close; nothing past the kerb.",
                "SM_Wep_Shotgun_01"),
            new ArmoryItem(EquipmentKind.MachinePistol, "Machine Pistol", 1_250,
                "Empties a clip across a room and hits what it pleases.",
                "SM_Wep_Machine_Pistol_01"),
            new ArmoryItem(EquipmentKind.Rifle, "Rifle", 1_750,
                "Longest range, highest accuracy - a street away.",
                "SM_Wep_Rifle_01"),
            new ArmoryItem(EquipmentKind.TommyGun, "Tommy Gun", 2_000,
                "High damage at range - wildly inaccurate in unskilled hands.",
                "SM_Wep_SubMachineGun_01"),
        };

        /// <summary>
        /// The first shelf of wheels: what a crew rides to work in, cheapest first, and
        /// the ladder is anonymity against money. The three pack bodies are what anybody
        /// can buy; the fourth is the outfit's own.
        ///
        /// THE ARMOURED WAGON IS NO PACK BODY. It is Palm City's SUV rebuilt by
        /// ArmouredSuvBuilder - gunmetal paint, plate over the sills and doors, a bull
        /// bar, bars across every window and a plate on the roof - and it is priced as
        /// the decision it is: 6,000 against a starting safe of 15,000
        /// (Accounts.StartingSafe), which is the working car, the van and a tommy gun
        /// all at once. It is the boss's car and the counter should make a player think
        /// twice, not shrug.
        ///
        /// Like the tourer and the boxless moped, it names no model here and is found by
        /// PortraitStudio.VehicleModelFor, the one table the catalogue's photograph and
        /// the body at the kerb (CrewCars.BodyFor) both read.
        /// </summary>
        public static readonly ArmoryItem[] Vehicles =
        {
            new ArmoryItem(EquipmentKind.Vehicle, "Jalopy", 800,
                "Runs, mostly. Gets a crew off its feet."),
            new ArmoryItem(EquipmentKind.Vehicle, "Sedan", 1_500,
                "The working car - seats a crew, raises no eyebrows."),
            new ArmoryItem(EquipmentKind.Vehicle, "Panel Van", 2_400,
                "Slow and anonymous; swallows anything."),
            new ArmoryItem(EquipmentKind.Vehicle, "Armoured Wagon", 6_000,
                "Plated doors, barred glass, a bar on the nose - and every eye on the street."),
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
            new ArmoryItem(EquipmentKind.Motorcycle, "Tourer", 1_400,
                "Big, black and built for two - the machine for a job across town."),
            new ArmoryItem(EquipmentKind.Motorcycle, "Motorbike", 1_200,
                "Two men, a gun off the back, and gone before the street looks up."),
            new ArmoryItem(EquipmentKind.Motorcycle, "Enduro", 900,
                "A dirt bike. Down an alley, over a lot, gone where a car cannot follow."),
            new ArmoryItem(EquipmentKind.Motorcycle, "Moped", 500,
                "Nobody looks twice at a delivery boy. Nobody hears one coming either."),
        };

        /// <summary>
        /// The counter's back shelf: what a crew throws. A grenade is not dealt into a
        /// man's hand the way a gun is (RosterOps.IsGrenade) - it is a countable charge
        /// the crew carries and spends one at a time, thrown at a rival or a shopfront or
        /// laid under a car (DemoCrews.Bomb). Bought and given to a lieutenant one at a
        /// time exactly as a gun is (each BUY one charge on the books, each GIVE hands the
        /// crew one more to carry), and struck off the moment it is thrown - so a crew's
        /// grenade count is just how many the lieutenant has been given and not yet spent.
        /// Priced so a handful is a real call against a better gun.
        /// </summary>
        public static readonly ArmoryItem[] Explosives =
        {
            new ArmoryItem(EquipmentKind.Grenade, "Grenade", 175,
                "One pineapple - thrown at a man or a shopfront, or laid under a car."),
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
        public static string BodyFor(string displayName) => displayName switch
        {
            "Jalopy" => "SM_Veh_Pickup_01",
            "Sedan" => "SM_Veh_Sedan_01",
            "Panel Van" => "SM_Veh_Van_01",
            // THE WAGON IS THE OUTFIT'S OWN BODY, like the tourer below: Palm City's SUV
            // rebuilt by ArmouredSuvBuilder into Assets/Prefabs/Vehicles/
            // SM_Veh_Suv_01_Armoured.prefab. No traffic bucket holds it (it is not in
            // VehicleCatalog and never turns up in the city's own cars), so the ledger
            // photographs it off LedgerModelSet.vehicles the way it photographs a bike.
            "Armoured Wagon" => "SM_Veh_Suv_01_Armoured",
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
            _ => "SM_Veh_Sedan_01",
        };

        /// <summary>3.0 Firearms stars. Below this, handing a man the tommy gun earns
        /// the amber warning - and is allowed, because the mistake is the player's to
        /// make (the promotion rule's discipline).</summary>
        public const int TommyGunFirearmsFloor = 6;
    }
}
