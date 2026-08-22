namespace LivingCity.Personnel
{
    /// <summary>Appended-only: Pistol and Vehicle came first and their indices are
    /// load-bearing for nothing, but the habit costs nothing either.</summary>
    public enum EquipmentKind
    {
        Pistol,
        Vehicle,
        Shotgun,
        Rifle,
        TommyGun,
        TwinPistols,
        MachinePistol,

        /// <summary>A two-wheeler. Wheels rather than a gun (RosterOps.IsWeapon), so
        /// the quartermaster deals it by Driving with the cars - but NOT a crew's
        /// vehicle either (CrewKit.HasVehicle): a machine that carries two men does
        /// not put a crew of five on the road, and the week's travel arithmetic must
        /// not pretend it does. What it is for is the pillion (CrewBike).</summary>
        Motorcycle,

        /// <summary>A grenade - a crew's throwing charge (DemoCrews.Bomb). Neither a
        /// gun nor a wheel (RosterOps.IsGrenade), so the quartermaster deals it into no
        /// man's hand: it is a COUNTABLE stock the lieutenant's crew carries, spent one
        /// at a time when thrown or laid. Owned by the lieutenant like any gear (the
        /// via-lieutenant rule), counted by DemoCrews.BindBombs.</summary>
        Grenade,
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

        /// <summary>The FRONT as an owner (and as the locker's holder id): the boss
        /// dumps gear at headquarters and NormalizeArms deals it to the men guarding
        /// the desk. Surplus sits on this id as holder too.</summary>
        public const int FrontArmory = -2;

        public int Id;
        public EquipmentKind Kind;
        public string DisplayName = "";

        /// <summary>Whose GROUP the item belongs to - the user's rule: gear stays in
        /// the parent. A lieutenant's member id (his crew's deck), FrontArmory (the
        /// desk's deck), or Unheld (the safe). A man who leaves his group carries
        /// nothing out - the deal re-runs over the group the item still belongs to.</summary>
        public int OwnerId = Unheld;

        /// <summary>Who CARRIES it right now - always someone inside the owner's
        /// group (or the owner id itself for warehoused surplus). Recomputed by
        /// NormalizeArms; never the source of ownership.</summary>
        public int HolderId = Unheld;

        /// <summary>Book value - what it cost, what the Assets line counts.</summary>
        public int Value;
    }
}
