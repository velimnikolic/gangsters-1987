using System;
using System.Collections.Generic;

namespace LivingCity.Property
{
    /// <summary>One flat: the building it is in, the floor it stands on, and the door on
    /// that landing. Floors count from 1; the ground floor is the shops and the entrance and
    /// holds no flats.</summary>
    public readonly struct ApartmentUnitId : IEquatable<ApartmentUnitId>
    {
        public ApartmentUnitId(ApartmentBuildingId building, int floor, int slot)
        {
            Building = building;
            Floor = floor;
            Slot = slot;
        }

        public ApartmentBuildingId Building { get; }
        public int Floor { get; }
        public int Slot { get; }

        public bool IsValid => Building.IsValid && Floor > 0 && Slot >= 0;

        /// <summary>What is stamped on the door: "3C".</summary>
        public string Door => Floor + ApartmentBuildings.DoorLetter(Slot);

        /// <summary>A flat as one string an intent can carry: building|floor|slot.</summary>
        public string Key => Building.Value + "|" + Floor + "|" + Slot;

        public bool Equals(ApartmentUnitId other) =>
            Building.Equals(other.Building) && Floor == other.Floor && Slot == other.Slot;

        public override bool Equals(object obj) => obj is ApartmentUnitId o && Equals(o);

        public override int GetHashCode() =>
            unchecked((Building.GetHashCode() * 397 ^ Floor) * 397 ^ Slot);

        public override string ToString() => Building.Value + "#" + Door;

        public static bool operator ==(ApartmentUnitId a, ApartmentUnitId b) => a.Equals(b);

        public static bool operator !=(ApartmentUnitId a, ApartmentUnitId b) => !a.Equals(b);
    }

    /// <summary>What a flat reads as on the sheet and to the simulation.</summary>
    public enum UnitState
    {
        /// <summary>Somebody else's home. The tenant's lease stands until the deed moves.</summary>
        NotOurs,

        /// <summary>Ours, a role set, a keeper standing in it: it works.</summary>
        Open,

        /// <summary>Ours, but nobody keeps it - or the man who does is in a cell or a bed.
        /// A dark flat does NOTHING, and a dark cash stash is what a raid finds easiest.</summary>
        Dark,

        /// <summary>A card room with no money behind the table is shut.</summary>
        NoBank,

        /// <summary>Sealed by the precinct until an absolute campaign day.</summary>
        Raided,
    }

    /// <summary>The simulation's record of one flat we hold.</summary>
    public sealed class ApartmentRecord
    {
        public ApartmentUnitId Unit;

        /// <summary>Which gang holds the deed. -1 is the honest majority, as in
        /// <see cref="LivingCity.Business.BusinessDeeds"/>.</summary>
        public int GangId = -1;

        /// <summary>The name the player typed on the door.</summary>
        public string Name = "";

        public UnitRole Role = UnitRole.Empty;

        /// <summary>The man who keeps it, or -1. He is OFF THE STREET while he does.</summary>
        public int KeeperId = -1;

        /// <summary>What the fit-out has been paid for, so a role changed twice is not
        /// charged twice for the same room and a refit is not free.</summary>
        public UnitRole PaidRole = UnitRole.Empty;

        /// <summary>Money behind the card room's table.</summary>
        public int Bank;

        /// <summary>Absolute campaign day the precinct's seal comes off. 0 = never raided.</summary>
        public int RaidUntilDay;

        /// <summary>The day the deed was signed, absolute.</summary>
        public int BoughtOnDay;

        /// <summary>Hired hands working out of the room: the girls in a brothel, the
        /// doctor in an infirmary. They are LEDGER ROWS and not street entities - paid a
        /// day rate at the tick, and nobody walks anywhere (EPIC 27, FLAT-007).</summary>
        public int Staff;
    }

    /// <summary>
    /// The apartment book: which flats the outfit holds, what each is used for, and who keeps
    /// it. Simulation state keyed by (building, floor, door) - the same shape
    /// <see cref="LivingCity.Business.BusinessDeeds"/> keeps for a shop, and for the same
    /// reason: the building on the screen is a VIEW, and a record that lived on it would be
    /// lost the moment the recycler pooled the street.
    ///
    /// Nothing here charges money or moves a man: the caller pays through the outfit's own
    /// seam and marks the keeper through <c>RosterOps</c>, then writes the result here. That
    /// keeps the book pure enough for the headless suite.
    /// </summary>
    public static class Apartments
    {
        static readonly Dictionary<ApartmentUnitId, ApartmentRecord> book =
            new Dictionary<ApartmentUnitId, ApartmentRecord>();

        static readonly List<ApartmentRecord> order = new List<ApartmentRecord>();

        /// <summary>A repaint key: it moves on every buy, role, keeper, name and raid.</summary>
        public static int Version { get; private set; }

        /// <summary>Raised after a flat changes. Carries the unit that moved.</summary>
        public static event Action<ApartmentUnitId> Changed;

        public static IReadOnlyList<ApartmentRecord> All => order;

        public static void Clear()
        {
            book.Clear();
            order.Clear();
            Version++;
        }

        public static bool TryGet(ApartmentUnitId unit, out ApartmentRecord record) =>
            book.TryGetValue(unit, out record);

        /// <summary>The <see cref="ApartmentUnitId.Key"/> read back.</summary>
        public static bool TryParseKey(string key, out ApartmentUnitId unit)
        {
            unit = default;
            if (string.IsNullOrEmpty(key))
                return false;
            var parts = key.Split('|');
            if (parts.Length != 3 || !int.TryParse(parts[1], out var floor) ||
                !int.TryParse(parts[2], out var slot))
                return false;
            unit = new ApartmentUnitId(new ApartmentBuildingId(parts[0]), floor, slot);
            return unit.IsValid;
        }

        public static bool IsOurs(ApartmentUnitId unit, int gangId) =>
            book.TryGetValue(unit, out var record) && record.GangId == gangId;

        public static int OwnerOf(ApartmentUnitId unit) =>
            book.TryGetValue(unit, out var record) ? record.GangId : -1;

        /// <summary>Every flat a gang holds in one building, in door order.</summary>
        public static void OwnedIn(
            ApartmentBuildingId building, int gangId, List<ApartmentRecord> into)
        {
            into?.Clear();
            if (into == null)
                return;
            for (var i = 0; i < order.Count; i++)
            {
                var record = order[i];
                if (record.GangId != gangId || !record.Unit.Building.Equals(building))
                    continue;
                into.Add(record);
            }
            into.Sort(CompareByDoor);
        }

        /// <summary>How many flats of a building are on our deed - the header's count.</summary>
        public static int CountIn(ApartmentBuildingId building, int gangId)
        {
            var held = 0;
            for (var i = 0; i < order.Count; i++)
                if (order[i].GangId == gangId && order[i].Unit.Building.Equals(building))
                    held++;
            return held;
        }

        /// <summary>Every flat a gang holds anywhere, for the day tick and the finances.</summary>
        public static void OwnedBy(int gangId, List<ApartmentRecord> into)
        {
            into?.Clear();
            if (into == null)
                return;
            for (var i = 0; i < order.Count; i++)
                if (order[i].GangId == gangId)
                    into.Add(order[i]);
        }

        /// <summary>
        /// EVERY UNIT NOBODY HOLDS in a building (EPIC 40, PRE-001) - what a mind
        /// leases from, in door order, top floor first like the sheet. The blueprint
        /// form picks by hand; a mind has to be handed a list.
        /// </summary>
        public static void VacantIn(ApartmentBuilding building, List<ApartmentUnitId> into)
        {
            into?.Clear();
            if (into == null || building == null)
                return;
            for (var floor = building.Floors; floor >= 1; floor--)
                for (var slot = 0; slot < building.DoorsPerLanding; slot++)
                {
                    var unit = new ApartmentUnitId(building.Id, floor, slot);
                    if (OwnerOf(unit) < 0)
                        into.Add(unit);
                }
        }

        /// <summary>The flat a man keeps, or an invalid id. One man, one flat.</summary>
        public static ApartmentUnitId KeptBy(int memberId)
        {
            if (memberId < 0)
                return default;
            for (var i = 0; i < order.Count; i++)
                if (order[i].KeeperId == memberId)
                    return order[i].Unit;
            return default;
        }

        // ------------------------------------------------------------------ the writes

        /// <summary>Writes the deed. The price is paid by the caller through the outfit's
        /// own pay seam before this is reached.</summary>
        public static ApartmentRecord Buy(ApartmentUnitId unit, int gangId, int day)
        {
            if (!unit.IsValid)
                return null;
            var record = Ensure(unit);
            record.GangId = gangId;
            record.BoughtOnDay = day;
            Touch(unit);
            return record;
        }

        public static void SetName(ApartmentUnitId unit, string name)
        {
            if (!book.TryGetValue(unit, out var record))
                return;
            record.Name = name ?? "";
            Touch(unit);
        }

        /// <summary>Sets what the room is used for. The fit-out is charged by the caller;
        /// <see cref="ApartmentRecord.PaidRole"/> is what says whether it has been.</summary>
        public static void SetRole(ApartmentUnitId unit, UnitRole role, bool fitOutPaid)
        {
            if (!book.TryGetValue(unit, out var record))
                return;
            record.Role = role;
            if (fitOutPaid)
                record.PaidRole = role;
            Touch(unit);
        }

        /// <summary>Puts a man in the room, or takes him out with -1. The roster's own rules
        /// about who may be spared to it live in <c>RosterOps.SetKeeper</c>; this only
        /// records the result, and clears him off any other flat so one man keeps one flat.</summary>
        public static void SetKeeper(ApartmentUnitId unit, int memberId)
        {
            if (!book.TryGetValue(unit, out var record))
                return;

            if (memberId >= 0)
                for (var i = 0; i < order.Count; i++)
                    if (order[i].KeeperId == memberId && !order[i].Unit.Equals(unit))
                    {
                        order[i].KeeperId = -1;
                        Changed?.Invoke(order[i].Unit);
                    }

            record.KeeperId = memberId;
            Touch(unit);
        }

        /// <summary>How many hands are hired into the room. The room's own ceiling is the
        /// caller's business; the book only records it.</summary>
        public static void SetStaff(ApartmentUnitId unit, int staff)
        {
            if (!book.TryGetValue(unit, out var record))
                return;
            record.Staff = Math.Max(0, staff);
            Touch(unit);
        }

        public static void SetBank(ApartmentUnitId unit, int bank)
        {
            if (!book.TryGetValue(unit, out var record))
                return;
            record.Bank = Math.Max(0, bank);
            Touch(unit);
        }

        /// <summary>The precinct seals it until an absolute day.</summary>
        public static void Raid(ApartmentUnitId unit, int untilDay)
        {
            if (!book.TryGetValue(unit, out var record))
                return;
            record.RaidUntilDay = untilDay;
            record.Bank = 0;
            Touch(unit);
        }

        // ------------------------------------------------------------------ the reading

        /// <summary>
        /// What the flat reads as today. The order matters and is the sheet's: not ours
        /// beats everything, then the precinct's seal, then a role that was never set, then
        /// the missing keeper, then an empty bank.
        /// </summary>
        public static UnitState StateOf(ApartmentUnitId unit, int gangId, int day,
                                        bool keeperStanding)
        {
            if (!book.TryGetValue(unit, out var record) || record.GangId != gangId)
                return UnitState.NotOurs;
            if (record.RaidUntilDay > day)
                return UnitState.Raided;
            if (record.Role == UnitRole.Empty)
                return UnitState.Dark;
            if (record.KeeperId < 0 || !keeperStanding)
                return UnitState.Dark;
            if (UnitRoles.Of(record.Role).NeedsBank && record.Bank <= 0)
                return UnitState.NoBank;
            return UnitState.Open;
        }

        public static string Word(UnitState state) => state switch
        {
            UnitState.Open => "OPEN",
            UnitState.Dark => "DARK",
            UnitState.NoBank => "CLOSED — BANK EMPTY",
            UnitState.Raided => "RAIDED",
            _ => "NOT OURS",
        };

        /// <summary>A flat read back off a save, field for field. The save is the only
        /// caller: every other write goes through the operations above, which is what
        /// keeps the rules in one place.</summary>
        public static void Restore(ApartmentUnitId unit, int gangId, string name,
            UnitRole role, int keeperId, UnitRole paidRole, int bank, int raidUntilDay,
            int boughtOnDay, int staff = 0)
        {
            if (!unit.IsValid)
                return;
            var record = Ensure(unit);
            record.GangId = gangId;
            record.Name = name ?? "";
            record.Role = role;
            record.KeeperId = keeperId;
            record.PaidRole = paidRole;
            record.Bank = bank;
            record.RaidUntilDay = raidUntilDay;
            record.BoughtOnDay = boughtOnDay;
            record.Staff = staff;
            Version++;
        }

        static ApartmentRecord Ensure(ApartmentUnitId unit)
        {
            if (book.TryGetValue(unit, out var record))
                return record;
            record = new ApartmentRecord { Unit = unit };
            book.Add(unit, record);
            order.Add(record);
            return record;
        }

        static int CompareByDoor(ApartmentRecord a, ApartmentRecord b) =>
            a.Unit.Floor != b.Unit.Floor
                ? b.Unit.Floor.CompareTo(a.Unit.Floor)
                : a.Unit.Slot.CompareTo(b.Unit.Slot);

        static void Touch(ApartmentUnitId unit)
        {
            Version++;
            Changed?.Invoke(unit);
        }
    }
}
