using System.Collections.Generic;
using LivingCity.Personnel;

namespace LivingCity.Outfit
{
    /// <summary>One day's actual transactions. Everything here is money that MOVED -
    /// never a display total; totals, profit, tax and wealth are derived in
    /// <see cref="BalanceMath"/> at read time. An open sheet's wages are derived live
    /// from the roster; <see cref="WagesPaid"/> freezes only when midnight closes the
    /// day and the sheet becomes a historical record.</summary>
    public sealed class DaySheet
    {
        /// <summary>The campaign day this sheet covers - its whole identity. The
        /// finances page pages through these one at a time.</summary>
        public int Day;

        public int LegalIncome;
        public int IllegalIncome;
        public int SalesIncome;

        public int Bribes;
        public int Purchases;
        public int OtherCosts;

        /// <summary>Frozen at midnight; meaningless while the sheet is open.</summary>
        public int WagesPaid;

        public int TaxPaid;

        /// <summary>A committed, read-only record of a finished day.</summary>
        public bool Closed;
    }

    /// <summary>
    /// The outfit's money: the safe, the unlaundered pile, and the daily sheets - the
    /// last sheet is today, still open. Pure data; every mutation goes through
    /// OutfitDirector so the ledger's Version moves with the books.
    /// </summary>
    public sealed class Accounts
    {
        /// <summary>Day one's stake.</summary>
        public const int StartingSafe = 15_000;

        /// <summary>How many days of closed sheets are kept. A year of them: enough for
        /// the finances page to page back through a long campaign, bounded so a machine
        /// left running does not accumulate sheets without end.</summary>
        public const int SheetsKept = 365;

        public int Safe = StartingSafe;

        /// <summary>Illegal profit not yet washed through a legitimate front. Grows at
        /// midnight; the laundering order will drain it later.</summary>
        public int RiskyMoney;

        /// <summary>Oldest first; the last is today's open sheet.</summary>
        public readonly List<DaySheet> Sheets = new List<DaySheet>();

        public DaySheet Current => Sheets.Count > 0 ? Sheets[Sheets.Count - 1] : null;

        /// <summary>The sheet for one campaign day, or null if it is out of the window
        /// kept. Searched from the back: the page nearly always wants a recent day.</summary>
        public DaySheet SheetFor(int day)
        {
            for (var i = Sheets.Count - 1; i >= 0; i--)
                if (Sheets[i].Day == day)
                    return Sheets[i];
            return null;
        }

        /// <summary>Opens tomorrow's sheet and drops any that have fallen out of the
        /// kept window, so a campaign that runs for years does not grow without end.</summary>
        public DaySheet Open(int day)
        {
            var sheet = new DaySheet { Day = day };
            Sheets.Add(sheet);
            if (Sheets.Count > SheetsKept)
                Sheets.RemoveRange(0, Sheets.Count - SheetsKept);
            return sheet;
        }
    }

    public enum RiskRating
    {
        None,
        Low,
        Moderate,
        High,
    }

    /// <summary>
    /// Every derived figure on the Finances page, computed from state at read time -
    /// the page never stores a display string. One static home so the headless suite
    /// asserts the arithmetic the player stares at when he is going broke.
    /// </summary>
    public static class BalanceMath
    {
        /// <summary>Flat rate on declared (positive) profit.</summary>
        public const int TaxRatePercent = 30;

        public const int RiskLowCeiling = 5_000;
        public const int RiskModerateCeiling = 20_000;

        public static int TotalIncome(DaySheet sheet) =>
            sheet == null ? 0 : sheet.LegalIncome + sheet.IllegalIncome + sheet.SalesIncome;

        public static int TotalOutgoings(DaySheet sheet, int wages) =>
            wages + (sheet == null ? 0 : sheet.Bribes + sheet.Purchases + sheet.OtherCosts);

        public static int Profit(DaySheet sheet, int wages) =>
            TotalIncome(sheet) - TotalOutgoings(sheet, wages);

        /// <summary>Tax falls due on profit only - a losing day owes nothing.</summary>
        public static int TaxDue(int profit) =>
            profit > 0 ? profit * TaxRatePercent / 100 : 0;

        public static RiskRating RiskFor(int riskyMoney) =>
            riskyMoney <= 0 ? RiskRating.None
            : riskyMoney < RiskLowCeiling ? RiskRating.Low
            : riskyMoney < RiskModerateCeiling ? RiskRating.Moderate
            : RiskRating.High;

        /// <summary>
        /// The purchase gate's pure half: null means paid (safe debited, the open
        /// day's Purchases line booked); otherwise the refusal, with the shortfall
        /// spelled out and no state touched. OutfitDirector wraps this with Version.
        /// </summary>
        public static string TryPurchase(Accounts accounts, int price)
        {
            if (accounts == null || price < 0)
                return UI.LedgerText.ReasonNoSuchItem;
            if (accounts.Safe < price)
                return UI.LedgerText.InsufficientFunds(price, accounts.Safe);

            accounts.Safe -= price;
            if (accounts.Current != null)
                accounts.Current.Purchases += price;
            return null;
        }

        /// <summary>Book value of everything the outfit holds - today the equipment
        /// stock; property and businesses join the sum when buying lands.</summary>
        public static int AssetsOf(Roster roster)
        {
            if (roster == null)
                return 0;

            var total = 0;
            for (var i = 0; i < roster.Equipment.Count; i++)
                total += roster.Equipment[i].Value;
            return total;
        }
    }

    /// <summary>The Finances page's whole readout as one struct, so the sheet the
    /// player reads and the sheet the tests assert are the same arithmetic.</summary>
    public readonly struct FinanceReport
    {
        public readonly int Day;
        public readonly bool Closed;
        public readonly int LegalIncome;
        public readonly int IllegalIncome;
        public readonly int SalesIncome;
        public readonly int Wages;
        public readonly int Bribes;
        public readonly int Purchases;
        public readonly int OtherCosts;
        public readonly int TotalIncome;
        public readonly int TotalOutgoings;
        public readonly int Profit;
        public readonly int TaxDue;
        public readonly int TaxPaid;
        public readonly int RiskyMoney;
        public readonly RiskRating Risk;
        public readonly int TotalProfit;
        public readonly int Safe;
        public readonly int Assets;
        public readonly int TotalWealth;

        /// <summary>An open sheet reads live wages; a closed one reads what was paid.</summary>
        public static FinanceReport For(DaySheet sheet, int liveWages, int safe,
            int riskyMoney, int assets)
        {
            var wages = sheet != null && sheet.Closed ? sheet.WagesPaid : liveWages;
            return new FinanceReport(sheet, wages, safe, riskyMoney, assets);
        }

        FinanceReport(DaySheet sheet, int wages, int safe, int riskyMoney, int assets)
        {
            Day = sheet?.Day ?? 1;
            Closed = sheet?.Closed ?? false;
            LegalIncome = sheet?.LegalIncome ?? 0;
            IllegalIncome = sheet?.IllegalIncome ?? 0;
            SalesIncome = sheet?.SalesIncome ?? 0;
            Wages = wages;
            Bribes = sheet?.Bribes ?? 0;
            Purchases = sheet?.Purchases ?? 0;
            OtherCosts = sheet?.OtherCosts ?? 0;
            TotalIncome = BalanceMath.TotalIncome(sheet);
            TotalOutgoings = BalanceMath.TotalOutgoings(sheet, wages);
            Profit = TotalIncome - TotalOutgoings;
            TaxDue = BalanceMath.TaxDue(Profit);
            TaxPaid = sheet?.TaxPaid ?? 0;
            RiskyMoney = riskyMoney;
            Risk = BalanceMath.RiskFor(riskyMoney);
            TotalProfit = Profit - TaxPaid;
            Safe = safe;
            Assets = assets;
            TotalWealth = safe + assets;
        }
    }
}
