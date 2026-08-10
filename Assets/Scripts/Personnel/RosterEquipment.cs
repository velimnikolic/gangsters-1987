namespace LivingCity.Personnel
{
    public enum EquipmentKind
    {
        Pistol,
        Vehicle,
    }

    /// <summary>
    /// One item in the outfit's shared stock - a pistol in the drawer, the car out back.
    /// The item records its holder; a character records nothing. One field, one owner, so
    /// two men holding the same gun is unrepresentable, not merely checked.
    ///
    /// Deliberately NOT WeaponCatalog / WeaponDef: those are the player-character's arsenal
    /// for the live shooting layer ("revolver", "magnum"). This is the organization's
    /// ledger stock, and the two must be free to evolve apart - a roster pistol has no
    /// damage table, and the arsenal has no notion of who among sixty men signed it out.
    /// </summary>
    public sealed class RosterEquipment
    {
        public const int Unheld = -1;

        public int Id;
        public EquipmentKind Kind;
        public string DisplayName = "";
        public int HolderId = Unheld;
    }
}
