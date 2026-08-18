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

        public static readonly ArmoryItem[] Vehicles =
        {
            new ArmoryItem(EquipmentKind.Vehicle, "Jalopy", 800,
                "Runs, mostly. Gets a crew off its feet."),
            new ArmoryItem(EquipmentKind.Vehicle, "Sedan", 1_500,
                "The working car - seats a crew, raises no eyebrows."),
            new ArmoryItem(EquipmentKind.Vehicle, "Panel Van", 2_400,
                "Slow and anonymous; swallows anything."),
        };

        /// <summary>3.0 Firearms stars. Below this, handing a man the tommy gun earns
        /// the amber warning - and is allowed, because the mistake is the player's to
        /// make (the promotion rule's discipline).</summary>
        public const int TommyGunFirearmsFloor = 6;
    }
}
