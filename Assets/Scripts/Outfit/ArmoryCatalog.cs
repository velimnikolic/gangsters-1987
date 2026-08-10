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

        public ArmoryItem(EquipmentKind kind, string displayName, int price, string note)
        {
            Kind = kind;
            DisplayName = displayName;
            Price = price;
            Note = note;
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
        public static readonly ArmoryItem[] Weapons =
        {
            new ArmoryItem(EquipmentKind.Pistol, ".38 Pistol", 100,
                "Adequate for low-risk work."),
            new ArmoryItem(EquipmentKind.Shotgun, "Shotgun", 750,
                "Devastating up close; pistol range only."),
            new ArmoryItem(EquipmentKind.Rifle, "Rifle", 750,
                "Longest range, highest accuracy. Best value per dollar."),
            new ArmoryItem(EquipmentKind.TommyGun, "Tommy Gun", 2_000,
                "High damage - wildly inaccurate in unskilled hands."),
            new ArmoryItem(EquipmentKind.TwinPistols, "Twin Pack Pistols", 3_000,
                "A pistol for each hand."),
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
