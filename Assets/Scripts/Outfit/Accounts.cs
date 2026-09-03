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
        public int JobIncome;
        public int SalesIncome;

        /// <summary>Protection rounds plus jobs: every dirty dollar booked today.</summary>
        public int DirtyIncome => IllegalIncome + JobIncome;

        public int Bribes;
        public int Purchases;
        public int OtherCosts;

        /// <summary>Frozen at midnight; meaningless while the sheet is open.</summary>
        public int WagesPaid;

        /// <summary>What the safe could NOT cover of the night's wages (WAGE-003):
        /// money the men were owed and did not get. Frozen at midnight beside
        /// <see cref="WagesPaid"/>, and the two together are the whole bill - a payroll
        /// that ran short shows as a red line on the Finances page rather than as a
        /// safe quietly gone negative.</summary>
        public int WagesShort;

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
        /// <summary>
        /// What is in the safe on day one. A million bought the whole price list before a
        /// single shop had been leaned on; twenty-five thousand is a few weeks of payroll,
        /// a cheap front and some guns - which means the racket has to come first, which
        /// is the game. (Docs/economy-prices.md §9.)
        /// </summary>
        public const int StartingSafe = 25_000;

        /// <summary>How many days of closed sheets are kept. A year of them: enough for
        /// the finances page to page back through a long campaign, bounded so a machine
        /// left running does not accumulate sheets without end.</summary>
        public const int SheetsKept = 365;

        public int Safe = StartingSafe;

        /// <summary>The dirty share of <see cref="Safe"/>. It is money physically in
        /// headquarters, not a second balance: 0 &lt;= RiskyMoney &lt;= Safe.</summary>
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

    public enum MoneyKind
    {
        Clean,
        Dirty,
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

        public const int RiskLowCeiling = 25_000;
        public const int RiskModerateCeiling = 100_000;

        public static int TotalIncome(DaySheet sheet) =>
            sheet == null ? 0 : sheet.LegalIncome + sheet.IllegalIncome +
                sheet.JobIncome + sheet.SalesIncome;

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

        public static int CleanOf(Accounts accounts) =>
            accounts == null ? 0 : System.Math.Max(0, accounts.Safe - accounts.RiskyMoney);

        /// <summary>Adds cash to the one physical safe and records which share is dirty.</summary>
        public static void Receive(Accounts accounts, int amount, MoneyKind kind)
        {
            if (accounts == null || amount <= 0)
                return;

            Normalize(accounts);
            accounts.Safe += amount;
            if (kind == MoneyKind.Dirty)
                accounts.RiskyMoney += amount;
        }

        /// <summary>Spends dirty cash first. Null means the full price was paid.</summary>
        public static string Pay(Accounts accounts, int price, out int dirtyPart)
        {
            dirtyPart = 0;
            if (accounts == null || price < 0)
                return UI.LedgerText.ReasonNoSuchItem;

            Normalize(accounts);
            if (accounts.Safe < price)
                return UI.LedgerText.InsufficientFunds(price, accounts.Safe);

            dirtyPart = System.Math.Min(accounts.RiskyMoney, price);
            accounts.RiskyMoney -= dirtyPart;
            accounts.Safe -= price;
            return null;
        }

        /// <summary>Restores a payment with its original dirty/clean composition.</summary>
        public static void Refund(Accounts accounts, int price, int dirtyPart)
        {
            if (accounts == null || price <= 0)
                return;

            Normalize(accounts);
            dirtyPart = System.Math.Max(0, System.Math.Min(price, dirtyPart));
            accounts.Safe += price;
            accounts.RiskyMoney += dirtyPart;
        }

        /// <summary>A raid takes only the dirty pile and leaves clean cash in place.</summary>
        public static int Seize(Accounts accounts)
        {
            if (accounts == null)
                return 0;

            Normalize(accounts);
            var seized = accounts.RiskyMoney;
            accounts.Safe -= seized;
            accounts.RiskyMoney = 0;
            return seized;
        }

        /// <summary>Repairs imported/legacy state at the authoritative money seam.</summary>
        public static void Normalize(Accounts accounts)
        {
            if (accounts == null)
                return;
            accounts.Safe = System.Math.Max(0, accounts.Safe);
            accounts.RiskyMoney = System.Math.Max(0,
                System.Math.Min(accounts.Safe, accounts.RiskyMoney));
        }

        /// <summary>
        /// The purchase gate's pure half: null means paid (safe debited, the open
        /// day's Purchases line booked); otherwise the refusal, with the shortfall
        /// spelled out and no state touched. OutfitDirector wraps this with Version.
        /// </summary>
        public static string TryPurchase(Accounts accounts, int price)
        {
            return TryPurchase(accounts, price, out _);
        }

        public static string TryPurchase(Accounts accounts, int price, out int dirtyPart)
        {
            var refusal = Pay(accounts, price, out dirtyPart);
            if (refusal != null)
                return refusal;
            if (accounts.Current != null)
                accounts.Current.Purchases += price;
            return null;
        }

        public static void RefundPurchase(Accounts accounts, int price, int dirtyPart)
        {
            Refund(accounts, price, dirtyPart);
            if (accounts?.Current != null)
                accounts.Current.Purchases = System.Math.Max(0,
                    accounts.Current.Purchases - System.Math.Max(0, price));
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
        public readonly int JobIncome;
        public readonly int SalesIncome;
        public readonly int Wages;

        /// <summary>What the safe could not cover of the night's payroll (WAGE-003).
        /// Zero on an open sheet - nothing has been paid yet, so nothing is short
        /// yet.</summary>
        public readonly int WagesShort;

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
            JobIncome = sheet?.JobIncome ?? 0;
            SalesIncome = sheet?.SalesIncome ?? 0;
            Wages = wages;
            WagesShort = sheet != null && sheet.Closed ? sheet.WagesShort : 0;
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
