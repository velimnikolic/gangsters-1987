using TMPro;
using UnityEngine;
using LivingCity.Personnel;
using static LivingCity.UI.LedgerKit;

namespace LivingCity.UI
{
    /// <summary>
    /// FINANCES: a greenbar sheet off the accounting machine, money in down the left
    /// and money out down the right, with the week's adding-machine tape torn off and
    /// laid beside it. Totals are ruled the way an accountant rules them; a loss is in
    /// red; and the profit after tax gets the boxed callout because it is the one
    /// figure the whole outfit turns on. EARLIER and LATER turn the pad back and forth.
    ///
    /// Every figure is derived at read - wages come off the live roster through
    /// Wages.WeeklyPayroll, the rest through BalanceMath - so hiring a man moves this
    /// page the same frame. A closed week is a record and says so.
    /// </summary>
    public sealed partial class PersonnelAlmanac
    {
        const float FinanceH = -(PageBottom - PageTop);

        /// <summary>The till roll torn off and laid down the right margin.</summary>
        const float TapeW = 296f;
        const float TapeGap = 24f;

        const float GreenW = PageWidth - TapeW - TapeGap;
        const float GreenPad = 16f;
        const float GreenInner = GreenW - GreenPad * 2f;

        /// <summary>The two money columns on the greenbar.</summary>
        const float FinColGap = 24f;
        const float FinColW = (GreenInner - FinColGap) * 0.5f;
        const float FinRightX = FinColW + FinColGap;

        /// <summary>The banded rows the sheet is printed on.</summary>
        const float FinPitch = 26f;

        /// <summary>Where the first banded row's baseline sits, under the head band.</summary>
        const float FinTop = -74f;

        RectTransform financesSheet;
        RectTransform financesContent;
        RectTransform tapeContent;

        /// <summary>How many weeks back the pad is turned; 0 = the open week.</summary>
        int financeWeekBack;

        void BuildFinancesPage(RectTransform sheet)
        {
            var root = NewPageRoot(sheet, LedgerPage.Finances);

            // The greenbar, laid square - its hairline banding would break into steps
            // at any tilt at all.
            financesSheet = Card("Greenbar", root, PageLeft, PageTop, GreenW, FinanceH,
                LedgerStyle.LedgerGreen, shadowSpread: 14f, low: LedgerStyle.LedgerGreenLow);

            // The banding is the paper, not the data: it is drawn once, at the row
            // pitch, and the figures land on it.
            Greenbar(financesSheet, GreenPad, -46f, GreenInner, FinanceH - 60f, FinPitch);

            // The head band the machine prints across the top of a run.
            var band = NewRect("Head", financesSheet);
            PlaceTopLeft(band, GreenPad, -10f, GreenInner, 30f);
            Fill(band, new Color(34f / 255f, 48f / 255f, 28f / 255f, 0.10f));

            financeHeading = Caps(band, 10f, -5f, 560f, "", 15f, LedgerStyle.GreenbarInk, 6f);

            Tape(band, "< EARLIER DAYS", GreenInner - 268f, -2f, 130f, 24f, () =>
            {
                var sheets = outfit ? outfit.Accounts.Sheets.Count : 1;
                if (financeWeekBack < sheets - 1)
                {
                    financeWeekBack++;
                    dirty = true;
                }
            }, size: 10f);
            Tape(band, "LATER DAYS >", GreenInner - 128f, -2f, 128f, 24f, () =>
            {
                if (financeWeekBack > 0)
                {
                    financeWeekBack--;
                    dirty = true;
                }
            }, size: 10f);

            financesContent = NewRect("Figures", financesSheet);
            Stretch(financesContent);

            BuildAddingTape(root);
        }

        TMP_Text financeHeading;

        /// <summary>The adding-machine tape: the same week run through the machine, in
        /// the order the operator punched it, torn off at the bottom. It is a CHECK on
        /// the sheet beside it, which is what a tape is for.</summary>
        void BuildAddingTape(RectTransform root)
        {
            var tape = Card("Tape", root, PageLeft + GreenW + TapeGap, PageTop, TapeW,
                FinanceH - 190f, LedgerStyle.Slip, tiltDegrees: -0.7f, shadowSpread: 12f,
                low: LedgerStyle.SlipLow);

            tapeContent = NewRect("Run", tape);
            Stretch(tapeContent);

            // Torn off the roll: the teeth are the colour of the sheet behind it.
            Perforation(tape, 0f, -(FinanceH - 190f) + 6f, TapeW, LedgerStyle.PaperMid, 12f);
        }

        /// <summary>
        /// Paints the balance sheet and its tape. EVERY figure is derived at this
        /// moment from game state - never a stored display string.
        /// </summary>
        void RebuildFinances()
        {
            foreach (Transform old in financesContent)
                Destroy(old.gameObject);
            foreach (Transform old in tapeContent)
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

            if (financeHeading)
                financeHeading.text = "THE BOOKS · WEEK " + report.Week +
                                      (report.Closed ? " · CLOSED" : " · OPEN");

            if (report.Closed)
                Stamp(financesContent, "CLOSED", GreenPad + GreenInner * 0.5f - 70f, -6f,
                    140f, 34f, tilt: -9f, size: 18f);

            // ---- the payroll, broken out where the biggest number is born ----
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

            // ---- money in, money out ----
            FinanceColumnHead(0f, FinTop + FinPitch, "MONEY IN");
            FinanceColumnHead(FinRightX, FinTop + FinPitch, "MONEY OUT");

            var y = FinTop;
            FinanceRow(0f, y, "Protection", report.IllegalIncome);
            FinanceRow(FinRightX, y, "Wages — " + hoods + (hoods == 1 ? " hood" : " hoods"),
                hoodWages);
            y -= FinPitch;

            FinanceRow(0f, y, "Sales", report.SalesIncome);
            FinanceRow(FinRightX, y, "Wages — " + lieutenants +
                (lieutenants == 1 ? " lieutenant" : " lieutenants"), lieutenantWages);
            y -= FinPitch;

            FinanceRow(0f, y, "Legitimate", report.LegalIncome);
            if (specialists > 0)
                FinanceRow(FinRightX, y, "Retainers — " + specialists, specialistWages);
            else
                FinanceRow(FinRightX, y, "Bribes", report.Bribes);
            y -= FinPitch;

            if (specialists > 0)
            {
                FinanceRow(FinRightX, y, "Bribes", report.Bribes);
                y -= FinPitch;
            }

            FinanceRow(FinRightX, y, "Purchases", report.Purchases);
            y -= FinPitch;
            FinanceRow(FinRightX, y, "Other costs", report.OtherCosts);
            y -= FinPitch;

            // The two totals land on the same rule whatever the column lengths were.
            var totalsY = FinTop - FinPitch * 5f;
            Rule(financesContent, GreenPad, totalsY + FinPitch - 2f, FinColW,
                LedgerStyle.GreenbarInk, 2f);
            Rule(financesContent, GreenPad + FinRightX, totalsY + FinPitch - 2f, FinColW,
                LedgerStyle.GreenbarInk, 2f);
            FinanceRow(0f, totalsY, "TOTAL IN", report.TotalIncome, bold: true);
            FinanceRow(FinRightX, totalsY, "TOTAL OUT", report.TotalOutgoings, bold: true);

            // ---- what is left of it, and what the outfit is worth ----
            var runY = totalsY - FinPitch * 2f;

            var loss = report.TotalProfit < 0;
            var callout = NewRect("Profit", financesContent);
            PlaceTopLeft(callout, GreenPad, runY, FinColW, 34f);
            Fill(callout, new Color(143f / 255f, 33f / 255f, 25f / 255f, loss ? 0.10f : 0.05f));
            Frame(callout, 1f, new Color(143f / 255f, 33f / 255f, 25f / 255f, 0.4f));
            Caps(callout, 12f, -8f, 260f, "PROFIT AFTER TAX", 12f,
                loss ? LedgerStyle.RedPen : LedgerStyle.GreenbarInk, 4f);
            var figure = Line(callout, LedgerStyle.Condensed, 20f,
                loss ? LedgerStyle.RedPen : LedgerStyle.GreenbarInk,
                FinColW - 190f, -6f, 178f, 26f, LedgerText.Cash(report.TotalProfit),
                TextAlignmentOptions.MidlineRight);
            figure.characterSpacing = 1f;

            var leftY = runY - 40f;
            FinanceRow(0f, leftY, "Profit before tax", report.Profit, red: report.Profit < 0);
            leftY -= FinPitch;
            FinanceRow(0f, leftY, "Tax due (" + Outfit.BalanceMath.TaxRatePercent + "%)",
                report.TaxDue);
            leftY -= FinPitch;
            FinanceRow(0f, leftY, "Tax paid", report.TaxPaid);
            leftY -= FinPitch;

            // Stocks are NOW-figures; a closed week's page keeps to its flows.
            var rightY = runY;
            if (!report.Closed)
            {
                FinanceRow(FinRightX, rightY, "Money in safe", report.Safe);
                rightY -= FinPitch;
                FinanceRow(FinRightX, rightY, "Stock at book value", report.Assets);
                rightY -= FinPitch;
                var risky = report.Risk >= Outfit.RiskRating.Moderate;
                FinanceRow(FinRightX, rightY, "Risky money (unlaundered)", report.RiskyMoney,
                    red: risky);
                rightY -= FinPitch;
                FinanceText(FinRightX, rightY, "Risk",
                    LedgerText.RiskLabel(report.Risk).ToUpperInvariant(), bold: risky,
                    red: risky);
                rightY -= FinPitch;
                Rule(financesContent, GreenPad + FinRightX, rightY + FinPitch - 2f, FinColW,
                    LedgerStyle.GreenbarInk, 2f);
                FinanceRow(FinRightX, rightY, "TOTAL WEALTH", report.TotalWealth, bold: true);
                rightY -= FinPitch;
            }
            else
            {
                Line(financesContent, LedgerStyle.MonoItalic, 13f, LedgerStyle.InkDim,
                    GreenPad + FinRightX, rightY, FinColW, FinPitch,
                    "A closed week - the record of what moved.");
                rightY -= FinPitch;
            }

            // Pinned to the foot of the sheet, not to wherever the figures ended:
            // the explainer and the signature are the sheet's bottom margin.
            BuildFinanceFoot(roster, -(FinanceH - 96f));
            BuildTapeRun(report);
        }

        /// <summary>The explainer and the bookkeeper's signature, across the foot. The
        /// name in the box is the outfit's OWN accountant if it has bought one - and
        /// when it has not, the box says who is doing the books instead.</summary>
        void BuildFinanceFoot(Roster roster, float y)
        {
            const float signW = 190f;
            var foot = NewRect("Foot", financesContent);
            PlaceTopLeft(foot, GreenPad, y, GreenInner, 78f);
            Fill(foot, new Color(34f / 255f, 48f / 255f, 28f / 255f, 0.07f));

            Paragraph(foot, LedgerStyle.Serif, 14f, LedgerStyle.GreenbarInk, 14f, -10f,
                GreenInner - signW - 40f, 58f,
                "Every figure is struck from the books as they stand this minute — the " +
                "sheet moves while you read it. Wages run against the whole roster " +
                "whether the men work or not. A big crew with no earner under it is the " +
                "classic way an outfit dies.", lineSpacing: 3f);

            string bookkeeper = null;
            if (roster != null)
                foreach (var member in roster.Members)
                    if (member.Specialty == Specialty.Accountant && !member.Gone)
                    {
                        bookkeeper = member.FirstName.Substring(0, 1) + ". " + member.Surname;
                        break;
                    }

            var box = NewRect("Signature", foot);
            PlaceTopLeft(box, GreenInner - signW - 14f, -8f, signW, 62f);
            Frame(box, 1f, new Color(34f / 255f, 48f / 255f, 28f / 255f, 0.35f));
            Caps(box, 0f, -6f, signW, "POSTED BY", 9f, LedgerStyle.InkLabel, 3f,
                TextAlignmentOptions.Center);
            Line(box, LedgerStyle.SerifItalic, 17f, LedgerStyle.Ballpoint, 0f, -20f, signW,
                24f, bookkeeper ?? "the boss himself", TextAlignmentOptions.Center);
            Caps(box, 0f, -44f, signW, bookkeeper != null ? "BOOKKEEPER" : "NO BOOKKEEPER ON THE BOOKS",
                8.5f, LedgerStyle.InkLabel, 2f, TextAlignmentOptions.Center);
        }

        /// <summary>The tape: the same money punched in the order an operator would
        /// punch it, subtotalled, then netted. Fixed pitch throughout - a tape that
        /// does not line up is not a tape.</summary>
        void BuildTapeRun(Outfit.FinanceReport report)
        {
            const float pitch = 17f;
            const float pad = 18f;
            var inner = TapeW - pad * 2f;
            var y = -18f;

            Caps(tapeContent, pad, y, inner, "ADDING MACHINE TAPE", 10f,
                LedgerStyle.InkLabel, 4f);
            y -= 26f;

            y = TapeLine("wages", report.Wages, y, pad, inner, pitch);
            y = TapeLine("bribes", report.Bribes, y, pad, inner, pitch);
            y = TapeLine("purchases", report.Purchases, y, pad, inner, pitch);
            y = TapeLine("other", report.OtherCosts, y, pad, inner, pitch);
            y = TapeLine("subtotal out", report.TotalOutgoings, y, pad, inner, pitch, true);
            y -= 4f;
            y = TapeLine("protection", report.IllegalIncome, y, pad, inner, pitch);
            y = TapeLine("sales", report.SalesIncome, y, pad, inner, pitch);
            y = TapeLine("legitimate", report.LegalIncome, y, pad, inner, pitch);
            y = TapeLine("subtotal in", report.TotalIncome, y, pad, inner, pitch, true);
            y -= 6f;

            DottedRule(tapeContent, pad, y + 8f, inner, LedgerStyle.InkFaint);
            var net = report.TotalIncome - report.TotalOutgoings;
            Caps(tapeContent, pad, y, 120f, "NET", 12f, LedgerStyle.RedPen, 4f);
            Line(tapeContent, LedgerStyle.MonoBold, 13.5f, LedgerStyle.RedPen,
                pad + inner - 160f, y, 160f, pitch, LedgerText.Cash(net),
                TextAlignmentOptions.MidlineRight);
            y -= 28f;

            Caps(tapeContent, pad, y, inner, "RIBBON LOW · REPLACE SOON", 8.5f,
                LedgerStyle.InkLabel, 2f);
        }

        float TapeLine(string label, int amount, float y, float pad, float inner,
            float pitch, bool rule = false)
        {
            Line(tapeContent, LedgerStyle.Mono, 12f, LedgerStyle.InkSoft, pad, y,
                inner - 130f, pitch, label);
            Line(tapeContent, LedgerStyle.Mono, 12f, LedgerStyle.InkSoft, pad + inner - 150f,
                y, 150f, pitch, LedgerText.Cash(amount), TextAlignmentOptions.MidlineRight);
            if (rule)
                Rule(tapeContent, pad, y - pitch + 2f, inner, LedgerStyle.InkFaint);
            return y - pitch;
        }

        /// <summary>A money column's head: what it is on the left, DOLLARS on the
        /// right, and the rule the machine strikes under both.</summary>
        void FinanceColumnHead(float x, float y, string label)
        {
            Caps(financesContent, GreenPad + x, y, FinColW - 120f, label, 10f,
                LedgerStyle.GreenbarInk, 5f);
            Caps(financesContent, GreenPad + x + FinColW - 120f, y, 120f, "DOLLARS", 10f,
                LedgerStyle.GreenbarInk, 5f, TextAlignmentOptions.MidlineRight);
            Rule(financesContent, GreenPad + x, y - 18f, FinColW,
                new Color(34f / 255f, 48f / 255f, 28f / 255f, 0.35f));
        }

        void FinanceRow(float x, float y, string label, int amount, bool bold = false,
            bool dim = false, bool red = false) =>
            FinanceText(x, y, label, LedgerText.Cash(amount), bold, dim, red);

        void FinanceText(float x, float y, string label, string value, bool bold = false,
            bool dim = false, bool red = false)
        {
            var colour = red ? LedgerStyle.RedPen
                : dim ? LedgerStyle.InkDim
                : LedgerStyle.GreenbarInk;
            var font = bold ? LedgerStyle.MonoBold : LedgerStyle.Mono;

            var text = Line(financesContent, font, 13.5f, colour, GreenPad + x + 4f, y,
                FinColW - 160f, FinPitch, label);
            text.overflowMode = TextOverflowModes.Ellipsis;

            Line(financesContent, font, 13.5f, colour, GreenPad + x + FinColW - 150f, y,
                146f, FinPitch, value, TextAlignmentOptions.MidlineRight);

            // A dotted leader carries the eye from the word to the figure - the one
            // job the whole of a ruled sheet exists to do.
            if (!bold)
                DottedRule(financesContent, GreenPad + x + 4f, y - FinPitch + 5f,
                    FinColW - 8f, new Color(34f / 255f, 48f / 255f, 28f / 255f, 0.22f));
        }
    }
}
