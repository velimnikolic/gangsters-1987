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

            return failures;
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
