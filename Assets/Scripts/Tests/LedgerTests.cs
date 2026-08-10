using System.Collections.Generic;
using LivingCity.Outfit;
using LivingCity.Personnel;
using LivingCity.UI;

namespace LivingCity.Tests
{
    /// <summary>
    /// The ledger's money model: the campaign calendar, wage derivation, balance-sheet
    /// arithmetic, and the exact-cash formatter. Same discipline as
    /// <see cref="PersonnelTests"/>: plain static class, failures as data, no
    /// UnityEngine - the whole Outfit core is engine-free on purpose.
    /// </summary>
    public static class LedgerTests
    {
        public static List<string> Run()
        {
            var failures = new List<string>();

            CalendarDerivesYear(failures);
            WagesDeriveFromTheRoster(failures);
            HiringMovesThePayroll(failures);
            BalanceArithmetic(failures);
            TaxOnlyOnProfit(failures);
            RiskThresholds(failures);
            AssetsAreBookValue(failures);
            ReportUsesFrozenWagesWhenClosed(failures);
            CashFormatsExactly(failures);
            PurchaseGateDebitsAndBooks(failures);
            CataloguePricesMatchTheSheet(failures);
            NewStockEntersThePoolUnheld(failures);
            StancesTurnOverAtCommit(failures);
            TerritorySeedsFromTheFronts(failures);
            StanceWordingIsExhaustive(failures);

            return failures;
        }

        static void StancesTurnOverAtCommit(List<string> failures)
        {
            var relations = new GangRelations();

            if (relations.StanceWith(1) != Stance.Peace)
                failures.Add("StancesTurnOverAtCommit: the outfit does not arrive quietly.");

            relations.SetPending(1, Stance.War);
            if (relations.StanceWith(1) != Stance.Peace)
                failures.Add("StancesTurnOverAtCommit: war landed mid-week.");
            if (!relations.TryGetPending(1, out var pending) || pending != Stance.War)
                failures.Add("StancesTurnOverAtCommit: the pending change vanished.");

            // "Never mind" - setting back to the current stance withdraws the change.
            relations.SetPending(1, Stance.Peace);
            if (relations.TryGetPending(1, out _))
                failures.Add("StancesTurnOverAtCommit: a withdrawn change survived.");

            relations.SetPending(1, Stance.Truce);
            relations.ApplyPending();
            if (relations.StanceWith(1) != Stance.Truce ||
                relations.TryGetPending(1, out _))
                failures.Add("StancesTurnOverAtCommit: the commit did not turn the stance.");
        }

        static void TerritorySeedsFromTheFronts(List<string> failures)
        {
            // A 6x6 grid of blocks, ids 0..35, centres 10 apart.
            var blocks = new List<TerritorySeeder.BlockPoint>();
            for (var z = 0; z < 6; z++)
                for (var x = 0; x < 6; x++)
                    blocks.Add(new TerritorySeeder.BlockPoint(z * 6 + x, x * 10f, z * 10f));

            var fronts = new List<TerritorySeeder.FrontPoint>
            {
                new TerritorySeeder.FrontPoint(0, 0, 0f, 0f),      // the player, corner
                new TerritorySeeder.FrontPoint(1, 35, 50f, 50f),   // far corner
                new TerritorySeeder.FrontPoint(2, 5, 50f, 0f),     // third corner
            };

            var map = new TerritoryMap();
            TerritorySeeder.Seed(map, blocks, fronts, playerGangId: 0);

            if (map.CountOf(0) != 1)
                failures.Add($"TerritorySeedsFromTheFronts: player holds {map.CountOf(0)}.");
            if (map.CountOf(1) != TerritorySeeder.RivalBlocks ||
                map.CountOf(2) != TerritorySeeder.RivalBlocks)
                failures.Add("TerritorySeedsFromTheFronts: a rival missed his four.");

            if (map.OwnerOf(0) != 0 || map.OwnerOf(35) != 1 || map.OwnerOf(5) != 2)
                failures.Add("TerritorySeedsFromTheFronts: a front block went to the wrong gang.");

            // Growth is nearest-first: gang 1's turf must stay in its corner.
            foreach (var claim in map.Claims)
                if (claim.Value == 1 && claim.Key < 22)
                    failures.Add($"TerritorySeedsFromTheFronts: gang 1 leapt to block {claim.Key}.");

            // Same inputs, same turf - byte-for-byte.
            var again = new TerritoryMap();
            TerritorySeeder.Seed(again, blocks, fronts, playerGangId: 0);
            foreach (var claim in map.Claims)
                if (again.OwnerOf(claim.Key) != claim.Value)
                    failures.Add("TerritorySeedsFromTheFronts: reseeding disagreed.");
        }

        static void StanceWordingIsExhaustive(List<string> failures)
        {
            foreach (Stance stance in System.Enum.GetValues(typeof(Stance)))
            {
                if (LedgerText.StanceLabel(stance).Length == 0)
                    failures.Add($"StanceWordingIsExhaustive: {stance} has no label.");
                if (LedgerText.StanceEffect(stance).Length == 0)
                    failures.Add($"StanceWordingIsExhaustive: {stance} has no effect line.");
            }
            if (LedgerText.StanceTakesEffect.Length == 0 ||
                LedgerText.StrengthUnknown.Length == 0)
                failures.Add("StanceWordingIsExhaustive: a diplomacy line is empty.");
        }

        static void PurchaseGateDebitsAndBooks(List<string> failures)
        {
            var accounts = new Accounts();
            accounts.Sheets.Add(new WeekSheet { Week = 1 });

            if (BalanceMath.TryPurchase(accounts, 750) != null)
                failures.Add("PurchaseGateDebitsAndBooks: an affordable buy refused.");
            if (accounts.Safe != Accounts.StartingSafe - 750 ||
                accounts.Current.Purchases != 750)
                failures.Add("PurchaseGateDebitsAndBooks: the safe and the sheet disagree.");

            var refusal = BalanceMath.TryPurchase(accounts, 999_999);
            if (refusal == null || refusal.Length == 0)
                failures.Add("PurchaseGateDebitsAndBooks: short money bought anyway.");
            if (accounts.Safe != Accounts.StartingSafe - 750 ||
                accounts.Current.Purchases != 750)
                failures.Add("PurchaseGateDebitsAndBooks: a refusal touched the books.");
        }

        static void CataloguePricesMatchTheSheet(List<string> failures)
        {
            var expected = new Dictionary<string, int>
            {
                { ".38 Pistol", 100 }, { "Shotgun", 750 }, { "Rifle", 750 },
                { "Tommy Gun", 2000 }, { "Twin Pack Pistols", 3000 },
            };

            foreach (var item in ArmoryCatalog.Weapons)
            {
                if (!expected.TryGetValue(item.DisplayName, out var price))
                    failures.Add($"CataloguePricesMatchTheSheet: unexpected {item.DisplayName}.");
                else if (item.Price != price)
                    failures.Add($"CataloguePricesMatchTheSheet: {item.DisplayName} at " +
                                 $"{item.Price}.");
                if (item.Note.Length == 0)
                    failures.Add($"CataloguePricesMatchTheSheet: {item.DisplayName} " +
                                 "has no note.");
            }
            if (ArmoryCatalog.Weapons.Length != expected.Count)
                failures.Add("CataloguePricesMatchTheSheet: weapon count drifted.");
            if (ArmoryCatalog.Vehicles.Length == 0)
                failures.Add("CataloguePricesMatchTheSheet: no vehicles for sale.");
        }

        static void NewStockEntersThePoolUnheld(List<string> failures)
        {
            var roster = RosterSeeder.Generate(42);
            var before = roster.Equipment.Count;
            var assetsBefore = BalanceMath.AssetsOf(roster);

            var item = RosterOps.AddEquipment(roster, EquipmentKind.TommyGun,
                "Tommy Gun", 2000);

            if (roster.Equipment.Count != before + 1 ||
                item.HolderId != RosterEquipment.Unheld)
                failures.Add("NewStockEntersThePoolUnheld: the buy did not pool unheld.");
            if (BalanceMath.AssetsOf(roster) != assetsBefore + 2000)
                failures.Add("NewStockEntersThePoolUnheld: assets missed the book value.");

            // The exclusivity rules apply to bought stock like seeded stock.
            var a = roster.Members[0];
            var b = roster.Members[1];
            RosterOps.GiveEquipment(roster, item.Id, a.Id);
            if (RosterOps.GiveEquipment(roster, item.Id, b.Id).Ok)
                failures.Add("NewStockEntersThePoolUnheld: one tommy gun, two holders.");
        }

        static void CalendarDerivesYear(List<string> failures)
        {
            var campaign = new Campaign { Week = 1 };
            if (campaign.Year != Campaign.StartYear || campaign.WeekOfYear != 1)
                failures.Add("CalendarDerivesYear: week 1 misreads.");

            campaign.Week = 52;
            if (campaign.Year != Campaign.StartYear || campaign.WeekOfYear != 52)
                failures.Add("CalendarDerivesYear: week 52 misreads.");

            campaign.Week = 53;
            if (campaign.Year != Campaign.StartYear + 1 || campaign.WeekOfYear != 1)
                failures.Add("CalendarDerivesYear: the year does not roll at 53.");
        }

        static void WagesDeriveFromTheRoster(List<string> failures)
        {
            var roster = RosterSeeder.Generate(42);
            var expected = 0;
            foreach (var member in roster.Members)
                expected += Wages.WageFor(member);

            if (Wages.WeeklyPayroll(roster) != expected || expected <= 0)
                failures.Add("WagesDeriveFromTheRoster: payroll is not the member sum.");

            // The dead come off the books; the jailed stay on them.
            roster.Members[1].Status = CharacterStatus.Dead;
            var afterDeath = Wages.WeeklyPayroll(roster);
            roster.Members[2].Status = CharacterStatus.Jailed;
            var afterJail = Wages.WeeklyPayroll(roster);

            if (afterDeath >= expected)
                failures.Add("WagesDeriveFromTheRoster: a dead man is still paid.");
            if (afterJail != afterDeath)
                failures.Add("WagesDeriveFromTheRoster: jail changed the wage bill.");
        }

        static void HiringMovesThePayroll(List<string> failures)
        {
            var roster = RosterSeeder.Generate(7);
            var before = Wages.WeeklyPayroll(roster);

            var recruit = new Character { Id = roster.NextCharacterId() };
            for (var a = 0; a < AttributeScale.Count; a++)
                recruit.SetHalfSteps((CharacterAttribute)a, 6);
            roster.Members.Add(recruit);

            if (Wages.WeeklyPayroll(roster) <= before)
                failures.Add("HiringMovesThePayroll: a recruit did not raise the bill.");
        }

        static void BalanceArithmetic(List<string> failures)
        {
            var sheet = new WeekSheet
            {
                Week = 3,
                LegalIncome = 1000,
                IllegalIncome = 2500,
                SalesIncome = 500,
                Bribes = 300,
                Purchases = 750,
                OtherCosts = 50,
            };

            var report = FinanceReport.For(sheet, liveWages: 900, safe: 4200,
                riskyMoney: 0, assets: 1800);

            if (report.TotalIncome != 4000)
                failures.Add($"BalanceArithmetic: income {report.TotalIncome}.");
            if (report.TotalOutgoings != 2000)
                failures.Add($"BalanceArithmetic: outgoings {report.TotalOutgoings}.");
            if (report.Profit != 2000)
                failures.Add($"BalanceArithmetic: profit {report.Profit}.");
            if (report.TaxDue != 600)
                failures.Add($"BalanceArithmetic: tax due {report.TaxDue}.");
            if (report.TotalProfit != 2000)
                failures.Add($"BalanceArithmetic: total profit {report.TotalProfit} " +
                             "(no tax paid yet).");
            if (report.TotalWealth != 6000)
                failures.Add($"BalanceArithmetic: wealth {report.TotalWealth}.");
        }

        static void TaxOnlyOnProfit(List<string> failures)
        {
            if (BalanceMath.TaxDue(-500) != 0)
                failures.Add("TaxOnlyOnProfit: a losing week owed tax.");
            if (BalanceMath.TaxDue(1000) != 1000 * BalanceMath.TaxRatePercent / 100)
                failures.Add("TaxOnlyOnProfit: the rate is wrong.");
        }

        static void RiskThresholds(List<string> failures)
        {
            if (BalanceMath.RiskFor(0) != RiskRating.None ||
                BalanceMath.RiskFor(BalanceMath.RiskLowCeiling - 1) != RiskRating.Low ||
                BalanceMath.RiskFor(BalanceMath.RiskLowCeiling) != RiskRating.Moderate ||
                BalanceMath.RiskFor(BalanceMath.RiskModerateCeiling) != RiskRating.High)
                failures.Add("RiskThresholds: a boundary lands in the wrong band.");
        }

        static void AssetsAreBookValue(List<string> failures)
        {
            var roster = RosterSeeder.Generate(42);
            // Seeded stock: three $100 pistols and a $1,500 car.
            if (BalanceMath.AssetsOf(roster) != 1800)
                failures.Add($"AssetsAreBookValue: {BalanceMath.AssetsOf(roster)}.");
        }

        static void ReportUsesFrozenWagesWhenClosed(List<string> failures)
        {
            var open = new WeekSheet { Week = 1 };
            var closed = new WeekSheet { Week = 1, Closed = true, WagesPaid = 640 };

            if (FinanceReport.For(open, 555, 0, 0, 0).Wages != 555)
                failures.Add("ReportUsesFrozenWagesWhenClosed: open sheet ignores live wages.");
            if (FinanceReport.For(closed, 555, 0, 0, 0).Wages != 640)
                failures.Add("ReportUsesFrozenWagesWhenClosed: closed sheet re-derives.");
        }

        static void CashFormatsExactly(List<string> failures)
        {
            if (LedgerText.Cash(0) != "$0" ||
                LedgerText.Cash(850) != "$850" ||
                LedgerText.Cash(1247) != "$1,247" ||
                LedgerText.Cash(-300) != "-$300" ||
                LedgerText.Cash(1250000) != "$1,250,000")
                failures.Add("CashFormatsExactly: the exact formatter rounds or misplaces.");
        }
    }
}
