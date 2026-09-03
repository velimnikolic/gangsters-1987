using System;
using System.Collections.Generic;
using LivingCity.Personnel;

namespace LivingCity.Outfit
{
    public interface IHeadquartersPhysicalSource
    {
        void CollectHeadquartersInside(List<InsideCrew> into);
    }

    public readonly struct InsideCrew
    {
        public readonly int LieutenantId;
        public readonly string LieutenantName;
        public readonly int Men;

        public InsideCrew(int lieutenantId, string lieutenantName, int men)
        {
            LieutenantId = lieutenantId;
            LieutenantName = lieutenantName ?? "";
            Men = Math.Max(0, men);
        }
    }

    public readonly struct HeadquartersStock
    {
        public readonly int Unheld;
        public readonly int Locker;
        public readonly int GuardHands;

        public HeadquartersStock(int unheld, int locker, int guardHands)
        {
            Unheld = unheld;
            Locker = locker;
            GuardHands = guardHands;
        }

        public int AtHeadquarters => Unheld + Locker + GuardHands;
    }

    /// <summary>A pure, immutable reading of everything physically kept at headquarters.</summary>
    public sealed class HeadquartersReport
    {
        readonly HeadquartersStock[] stock;

        public int Safe { get; private set; }
        public int Dirty { get; private set; }
        public int Clean { get; private set; }
        public RiskRating Risk { get; private set; }
        public string DeskManager { get; private set; }
        public int Guards { get; private set; }
        public IReadOnlyList<InsideCrew> Inside { get; private set; }
        public int UnheldItems { get; private set; }
        public int LockerItems { get; private set; }
        public int GuardHeldItems { get; private set; }

        HeadquartersReport()
        {
            stock = new HeadquartersStock[Enum.GetValues(typeof(EquipmentKind)).Length];
            DeskManager = "";
            Inside = Array.Empty<InsideCrew>();
        }

        public HeadquartersStock Stock(EquipmentKind kind) => stock[(int)kind];

        public static HeadquartersReport For(Accounts accounts, Roster roster,
            IReadOnlyList<InsideCrew> inside)
        {
            var report = new HeadquartersReport();
            if (accounts != null)
            {
                report.Safe = Math.Max(0, accounts.Safe);
                report.Dirty = Math.Max(0, Math.Min(report.Safe, accounts.RiskyMoney));
                report.Clean = report.Safe - report.Dirty;
                report.Risk = BalanceMath.RiskFor(report.Dirty);
            }

            if (roster != null)
            {
                var manager = roster.Find(roster.FrontId);
                report.DeskManager = manager != null && !manager.Gone
                    ? manager.FullName : "";

                for (var i = 0; i < roster.Members.Count; i++)
                {
                    var member = roster.Members[i];
                    if (member.Status == CharacterStatus.Active &&
                        member.Rank == Rank.Hood &&
                        member.Id != roster.FrontId &&
                        roster.AssignmentOf(member.Id).Kind == AssignmentKind.Pool)
                        report.Guards++;
                }

                var unheld = new int[report.stock.Length];
                var locker = new int[report.stock.Length];
                var hands = new int[report.stock.Length];
                for (var i = 0; i < roster.Equipment.Count; i++)
                {
                    var item = roster.Equipment[i];
                    var kind = (int)item.Kind;
                    if (kind < 0 || kind >= report.stock.Length)
                        continue;
                    if (item.OwnerId == RosterEquipment.Unheld)
                    {
                        unheld[kind]++;
                        report.UnheldItems++;
                    }
                    else if (item.OwnerId == RosterEquipment.FrontArmory &&
                             item.HolderId == RosterEquipment.FrontArmory)
                    {
                        locker[kind]++;
                        report.LockerItems++;
                    }
                    else if (item.OwnerId == RosterEquipment.FrontArmory &&
                             item.HolderId >= 0)
                    {
                        hands[kind]++;
                        report.GuardHeldItems++;
                    }
                }
                for (var i = 0; i < report.stock.Length; i++)
                    report.stock[i] = new HeadquartersStock(unheld[i], locker[i], hands[i]);
            }

            if (inside != null && inside.Count > 0)
            {
                var copy = new InsideCrew[inside.Count];
                for (var i = 0; i < copy.Length; i++)
                    copy[i] = inside[i];
                report.Inside = Array.AsReadOnly(copy);
            }
            return report;
        }
    }

    public static class HeadquartersText
    {
        static readonly EquipmentKind[] Guns =
        {
            EquipmentKind.Pistol, EquipmentKind.Shotgun, EquipmentKind.Rifle,
            EquipmentKind.TommyGun, EquipmentKind.TwinPistols, EquipmentKind.MachinePistol,
        };

        public static string Armory(HeadquartersReport report) =>
            StockLine(report, false, true);

        public static string InHands(HeadquartersReport report) =>
            StockLine(report, true, false);

        public static string Vehicles(HeadquartersReport report)
        {
            if (report == null)
                return "nothing out back";
            var cars = report.Stock(EquipmentKind.Vehicle);
            var bikes = report.Stock(EquipmentKind.Motorcycle);
            var parts = new List<string>(2);
            Add(parts, cars.AtHeadquarters, "car", "cars");
            Add(parts, bikes.AtHeadquarters, "motorcycle", "motorcycles");
            return parts.Count == 0 ? "nothing out back" : string.Join(" · ", parts);
        }

        public static string Inside(HeadquartersReport report)
        {
            if (report == null || report.Inside.Count == 0)
                return "nobody";
            var parts = new List<string>(report.Inside.Count);
            for (var i = 0; i < report.Inside.Count; i++)
            {
                var row = report.Inside[i];
                var surname = Surname(row.LieutenantName);
                var possessive = surname.EndsWith("s", StringComparison.OrdinalIgnoreCase)
                    ? surname + "'" : surname + "'s";
                parts.Add(possessive + " crew, " + row.Men +
                          (row.Men == 1 ? " man" : " men"));
            }
            return string.Join(" · ", parts);
        }

        static string StockLine(HeadquartersReport report, bool handsOnly,
            bool includeGrenades)
        {
            if (report == null)
                return handsOnly ? "nobody armed" : "nothing in the armory";
            var parts = new List<string>();
            for (var i = 0; i < Guns.Length; i++)
            {
                var row = report.Stock(Guns[i]);
                var count = handsOnly ? row.GuardHands : row.Unheld + row.Locker;
                Add(parts, count, Singular(Guns[i]), Plural(Guns[i]));
            }
            if (includeGrenades)
            {
                var row = report.Stock(EquipmentKind.Grenade);
                Add(parts, row.Unheld + row.Locker + row.GuardHands,
                    "grenade", "grenades");
            }
            return parts.Count == 0
                ? handsOnly ? "nobody armed" : "nothing in the armory"
                : string.Join(" · ", parts);
        }

        static void Add(List<string> parts, int count, string one, string many)
        {
            if (count > 0)
                parts.Add(count + " " + (count == 1 ? one : many));
        }

        static string Singular(EquipmentKind kind)
        {
            switch (kind)
            {
                case EquipmentKind.Shotgun: return "shotgun";
                case EquipmentKind.Rifle: return "rifle";
                case EquipmentKind.TommyGun: return "tommy gun";
                case EquipmentKind.TwinPistols: return "pair of twin pistols";
                case EquipmentKind.MachinePistol: return "machine pistol";
                default: return "pistol";
            }
        }

        static string Plural(EquipmentKind kind)
        {
            switch (kind)
            {
                case EquipmentKind.Shotgun: return "shotguns";
                case EquipmentKind.Rifle: return "rifles";
                case EquipmentKind.TommyGun: return "tommy guns";
                case EquipmentKind.TwinPistols: return "pairs of twin pistols";
                case EquipmentKind.MachinePistol: return "machine pistols";
                default: return "pistols";
            }
        }

        static string Surname(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName))
                return "Unknown";
            var at = fullName.LastIndexOf(' ');
            return at >= 0 && at + 1 < fullName.Length ? fullName.Substring(at + 1) : fullName;
        }
    }
}
