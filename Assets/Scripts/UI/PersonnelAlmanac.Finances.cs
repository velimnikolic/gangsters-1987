using TMPro;
using UnityEngine;
using LivingCity.Personnel;
using static LivingCity.UI.LedgerKit;

namespace LivingCity.UI
{
    /// <summary>
    /// FINANCES: the accountant's balance sheet - a green-ruled columnar pad clipped
    /// into the folder, income down the left, outgoings down the right, every figure
    /// in Courier in its own ruled amount column, totals double-underlined, a loss in
    /// red pen, and a CLOSED stamp across any week already in the books. EARLIER and
    /// LATER tapes turn the pad back and forth.
    /// </summary>
    public sealed partial class PersonnelAlmanac
    {
        const float SheetX = PageLeft;
        const float SheetY = PageTop - 4f;
        const float SheetW = PageWidth;
        const float SheetH = 860f;
        const float SheetPitch = 26f;

        /// <summary>The two ledger columns on the pad, in sheet coordinates.</summary>
        const float ColLeft = 24f;
        const float ColRight = SheetW * 0.5f + 12f;
        const float ColWidth = SheetW * 0.5f - 36f;
        const float AmountWidth = 132f;

        RectTransform financesSheet;
        RectTransform financesContent;

        /// <summary>How many weeks back the pad is turned; 0 = the open week.</summary>
        int financeWeekBack;

        void BuildFinancesPage(RectTransform sheet)
        {
            var root = NewPageRoot(sheet, LedgerPage.Finances);

            // The pad, laid square on the ledger page - its hairline rules would break
            // into steps at any tilt.
            financesSheet = Card("Pad", root, SheetX, SheetY, SheetW, SheetH,
                LedgerStyle.LedgerGreen);

            // The pad's own rules: green lines every row, and the amount columns
            // boxed by double verticals - the columnar paper an accountant buys.
            var rules = NewRect("Rules", financesSheet);
            Stretch(rules);
            for (var y = 64f; y < SheetH - 8f; y += SheetPitch)
                Rule(rules, 8f, -y, SheetW - 16f, LedgerStyle.RuleGreen);
            foreach (var colX in new[] { ColLeft, ColRight })
            {
                var amountX = colX + ColWidth - AmountWidth;
                VRule(rules, amountX, -40f, SheetH - 48f, LedgerStyle.RuleGreen);
                VRule(rules, amountX + 3f, -40f, SheetH - 48f, LedgerStyle.RuleGreen);
                VRule(rules, colX + ColWidth, -40f, SheetH - 48f, LedgerStyle.RuleGreen);
            }

            Tape(financesSheet, "< EARLIER", SheetW - 232f, -12f, 108f, 24f, () =>
            {
                var sheets = outfit ? outfit.Accounts.Sheets.Count : 1;
                if (financeWeekBack < sheets - 1)
                {
                    financeWeekBack++;
                    dirty = true;
                }
            });
            Tape(financesSheet, "LATER >", SheetW - 116f, -12f, 92f, 24f, () =>
            {
                if (financeWeekBack > 0)
                {
                    financeWeekBack--;
                    dirty = true;
                }
            });

            financesContent = NewRect("Figures", financesSheet);
            Stretch(financesContent);
        }

        /// <summary>
        /// Paints the balance sheet. EVERY figure is derived at this moment from game
        /// state - wages from the live roster via Wages.WeeklyPayroll, totals through
        /// BalanceMath - never a stored display string; hire a man and this page moves
        /// the same frame. Historical sheets are closed records and say so.
        /// </summary>
        void RebuildFinances()
        {
            foreach (Transform old in financesContent)
                Destroy(old.gameObject);

            if (!outfit)
                return;

            var accounts = outfit.Accounts;
            var index = accounts.Sheets.Count - 1 - financeWeekBack;
            if (index < 0)
                index = 0;
            var sheet = accounts.Sheets.Count > 0 ? accounts.Sheets[index] : null;
            var roster = director.Roster;

            var report = Outfit.FinanceReport.For(
                sheet,
                Outfit.Wages.WeeklyPayroll(roster),
                accounts.Safe,
                accounts.RiskyMoney,
                Outfit.BalanceMath.AssetsOf(roster));

            var heading = Line(financesContent, LedgerStyle.Type, 17f, LedgerStyle.Ink, ColLeft,
                -12f, 480f, 28f, "BALANCE SHEET  ·  WEEK " + report.Week);
            heading.characterSpacing = 3f;

            if (report.Closed)
                Stamp(financesContent, "CLOSED", ColLeft + 320f, -4f, 130f, 34f, tilt: -9f,
                    size: 20f);

            // ---- income left, outgoings right ----
            var y = -64f;

            ColumnHead(ColLeft, y, "INCOME");
            ColumnHead(ColRight, y, "OUTGOINGS");
            y -= SheetPitch;

            FinanceRow(ColLeft, y, "Legal", report.LegalIncome);
            FinanceRow(ColRight, y, "Wages", report.Wages);
            y -= SheetPitch;

            FinanceRow(ColLeft, y, "Illegal", report.IllegalIncome);
            // The payroll breakdown, right where the biggest number is born - the
            // player going broke must SEE that the wage bill is his own roster.
            var hoods = 0;
            var lieutenants = 0;
            var specialists = 0;
            var hoodWages = 0;
            var lieutenantWages = 0;
            var specialistWages = 0;
            if (roster != null)
                foreach (var member in roster.Members)
                {
                    var wage = Outfit.Wages.WageFor(member);
                    if (member.Specialty != Specialty.None)
                    {
                        specialists++;
                        specialistWages += wage;
                    }
                    else if (member.Rank == Rank.Lieutenant)
                    {
                        lieutenants++;
                        lieutenantWages += wage;
                    }
                    else
                    {
                        hoods++;
                        hoodWages += wage;
                    }
                }

            FinanceRow(ColRight, y, "    " + hoods + " hoods", hoodWages, dim: true);
            y -= SheetPitch;

            FinanceRow(ColLeft, y, "Sales", report.SalesIncome);
            FinanceRow(ColRight, y, "    " + lieutenants + " lieutenants", lieutenantWages,
                dim: true);
            y -= SheetPitch;

            if (specialists > 0)
            {
                FinanceRow(ColRight, y, "    " + specialists + " on retainer",
                    specialistWages, dim: true);
                y -= SheetPitch;
            }

            FinanceRow(ColRight, y, "Bribes", report.Bribes);
            y -= SheetPitch;
            FinanceRow(ColRight, y, "Purchases", report.Purchases);
            y -= SheetPitch;
            FinanceRow(ColRight, y, "Other costs", report.OtherCosts);
            y -= SheetPitch * 2f;

            FinanceRow(ColLeft, y, "TOTAL IN", report.TotalIncome, bold: true);
            FinanceRow(ColRight, y, "TOTAL OUT", report.TotalOutgoings, bold: true);
            y -= SheetPitch * 2f;

            // ---- the derived run, down the left column ----
            var loss = report.Profit < 0;
            FinanceRow(ColLeft, y, "PROFIT", report.Profit, bold: true, red: loss);
            y -= SheetPitch;
            FinanceRow(ColLeft, y, "Tax due (" + Outfit.BalanceMath.TaxRatePercent + "%)",
                report.TaxDue);
            y -= SheetPitch;
            FinanceRow(ColLeft, y, "Tax paid", report.TaxPaid);
            y -= SheetPitch;
            FinanceRow(ColLeft, y, "PROFIT AFTER TAX", report.TotalProfit, bold: true,
                red: report.TotalProfit < 0);
            y -= SheetPitch * 2f;

            // Stocks are NOW-figures; a closed week's page keeps to its flows.
            if (!report.Closed)
            {
                var risky = report.Risk >= Outfit.RiskRating.Moderate;
                FinanceRow(ColLeft, y, "Risky money (unlaundered)", report.RiskyMoney,
                    red: risky);
                y -= SheetPitch;
                FinanceText(ColLeft, y, "Risk", LedgerText.RiskLabel(report.Risk).ToUpperInvariant(),
                    bold: risky, red: risky);
                y -= SheetPitch * 2f;

                FinanceRow(ColLeft, y, "MONEY IN SAFE", report.Safe, bold: true);
                y -= SheetPitch;
                FinanceRow(ColLeft, y, "Assets (stock at book value)", report.Assets);
                y -= SheetPitch;
                FinanceRow(ColLeft, y, "TOTAL WEALTH", report.TotalWealth, bold: true);
                y -= SheetPitch;
            }
            else
            {
                Line(financesContent, LedgerStyle.MonoItalic, 14.5f, LedgerStyle.InkDim,
                    ColLeft, y, SheetW - ColLeft * 2f, SheetPitch,
                    "A closed week - the record of what moved. Current holdings live " +
                    "on the open sheet.");
                y -= SheetPitch;
            }

            // ---- the accountant's note, pencilled in the right column's foot ----
            Paragraph(financesContent, LedgerStyle.MonoItalic, 12.5f,
                LedgerStyle.InkDim, ColRight, -SheetH + 236f, ColWidth, 210f,
                "Every figure on this sheet is computed from the books as they stand " +
                "this instant. Wages are the whole roster, week in, week out - the " +
                "jailed and the hospitalized stay on the payroll; only the dead come " +
                "off. Recruit a man on the PERSONNEL page and the wage line moves " +
                "before you can turn back to look at it.\n\n" +
                "A big crew with no income is the classic way an outfit dies.",
                lineSpacing: 5f);
        }

        void ColumnHead(float x, float y, string label)
        {
            var head = Line(financesContent, LedgerStyle.Type, 14.5f, LedgerStyle.Ink, x + 4f, y,
                ColWidth - AmountWidth, SheetPitch, label);
            head.characterSpacing = 3f;
            var amount = Line(financesContent, LedgerStyle.Type, 12f, LedgerStyle.InkDim,
                x + ColWidth - AmountWidth, y, AmountWidth, SheetPitch, "$",
                TextAlignmentOptions.Midline);
            amount.characterSpacing = 3f;
        }

        void FinanceRow(float x, float y, string label, int amount, bool bold = false,
            bool dim = false, bool red = false) =>
            FinanceText(x, y, label, LedgerText.Cash(amount), bold, dim, red);

        void FinanceText(float x, float y, string label, string value, bool bold = false,
            bool dim = false, bool red = false)
        {
            var color = red ? LedgerStyle.RedPen : dim ? LedgerStyle.InkDim : LedgerStyle.Ink;
            var font = bold ? LedgerStyle.MonoBold : LedgerStyle.Mono;

            Line(financesContent, font, 14.5f, color, x + 4f, y, ColWidth - AmountWidth - 8f,
                SheetPitch, label);

            var amountX = x + ColWidth - AmountWidth;
            Line(financesContent, font, 14.5f, color, amountX + 6f, y, AmountWidth - 12f,
                SheetPitch, value, TextAlignmentOptions.MidlineRight);

            // Totals are double-underlined in the amount column, as an accountant does.
            if (bold)
                DoubleRule(financesContent, amountX + 6f, y - SheetPitch + 5f,
                    AmountWidth - 12f, color);
        }
    }
}
