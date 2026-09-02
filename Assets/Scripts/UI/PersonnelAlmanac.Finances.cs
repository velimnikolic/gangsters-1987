using TMPro;
using UnityEngine;
using LivingCity.Personnel;
using static LivingCity.UI.LedgerKit;

namespace LivingCity.UI
{
    /// <summary>
    /// FINANCES, drawn to the v2 design sheet: three cards laid on one row - MONEY IN
    /// with the profit callout and the tax lines under it, MONEY OUT with what the
    /// outfit is worth beneath it, and the adding-machine tape torn off beside them.
    /// Under the row, the remark and the box the books are posted in.
    ///
    /// Every measurement here is the design's own: the 20-unit grid gutter, the
    /// 9/16 row padding, the 31-unit dark band, the 3-unit rule under the page head.
    /// Nothing is eyeballed and nothing is a leftover of the old greenbar.
    ///
    /// The books are kept BY THE DAY - there is no week in this game and nothing waits
    /// for one. The two cards show ONE day, and EARLIER / LATER walk the seven the page
    /// is headed with, one day a press. The tape is the check on that: it runs all
    /// seven days at once, so a day that looks quiet on its own can still be read
    /// against the week of trading it sits in.
    ///
    /// Every figure is derived at read - wages come off the live roster through
    /// Wages.DailyPayroll, the rest through BalanceMath - so hiring a man moves this
    /// page the same frame. A closed day is a record and says so.
    /// </summary>
    public sealed partial class PersonnelAlmanac
    {
        // ------------------------------------------------------------ the design's ink
        // The oklch of the finances sheet, converted once. What the shared palette
        // already carries is used from there; these are the values this page alone owns.

        /// <summary>The green card's rows: the label ink and the band's own dim.</summary>
        static readonly Color FinRowInk = LedgerV2.Rgb2(0x2d2824);
        static readonly Color FinBandDim = LedgerV2.Rgb2(0xa89c93);

        /// <summary>The two ways money runs, printed on the dark band.</summary>
        static readonly Color FinInLabel = LedgerV2.Rgb2(0xa0d4a0);
        static readonly Color FinOutLabel = LedgerV2.Rgb2(0xfba587);

        /// <summary>The rules: the hairline between rows, the profit band's edge, the
        /// dotted leader under a tax line and the tape's own.</summary>
        static readonly Color FinProfitRule = LedgerV2.Rgb2(0xaea298);
        static readonly Color FinDotted = LedgerV2.Rgb2(0xc8bbb1);
        static readonly Color FinTaxLabel = LedgerV2.Rgb2(0x51453e);

        /// <summary>The tape's small type, its net rule, and the ribbon note.</summary>
        static readonly Color FinNetLabel = LedgerV2.Rgb2(0x902828);
        static readonly Color FinNetRule = LedgerV2.Rgb2(0x322d29);
        static readonly Color FinRibbon = LedgerV2.Rgb2(0x84776f);

        /// <summary>The foot: the rule over it, the remark's ink, and the box the
        /// books are signed in.</summary>
        static readonly Color FinFootRule = LedgerV2.Rgb2(0xb5a89f);
        static readonly Color FinRemark = LedgerV2.Rgb2(0x322d29);
        static readonly Color FinSignRule = LedgerV2.Rgb2(0x7b6f66);

        // ------------------------------------------------------------- the design's grid

        /// <summary>The page head: title, the line under it, and the 3-unit rule.</summary>
        const float FinancesHeadH = 72f;

        /// <summary>The gutter between the three cards.</summary>
        const float FinGridGap = 20f;

        /// <summary>A card's own side padding, and the padding on its rows.</summary>
        const float FinPad = 16f;

        /// <summary>The heights the design's paddings come to, measured off the type
        /// each row is set in: 9 + line + 9 for a figure row, 11 + line + 11 for a
        /// total, 12 + line + 12 for the profit callout, 8 + line + 8 for a tax line.
        /// </summary>
        const float FinBandH = 31f;
        const float FinRowH = 35f;
        const float FinTotalH = 44f;
        const float FinProfitH = 53f;
        const float FinTaxH = 33f;

        /// <summary>The tape's own paddings and pitch.</summary>
        const float FinTapePad = 16f;
        const float FinTapeRowH = 26f;

        static float FinTopY = PageTop - FinancesHeadH;
        static float FinColW = 532f;
        static float FinCol2X = 552f;
        static float FinCol3X = 1104f;

        /// <summary>Three equal tracks with the design's 20-unit gutters. The grid is
        /// auto-fit at minmax(290, 1fr) over three items, so on any window this sheet
        /// is drawn at the empty tracks collapse and the three cards share the width.
        /// </summary>
        static void MeasureFinancesLayout()
        {
            FinTopY = PageTop - FinancesHeadH;
            FinColW = (PageWidth - FinGridGap * 2f) / 3f;
            FinCol2X = FinColW + FinGridGap;
            FinCol3X = (FinColW + FinGridGap) * 2f;
        }

        RectTransform financesContent;

        /// <summary>How many days back the pad is turned; 0 = today's open sheet. Held
        /// inside <see cref="Outfit.Campaign.BooksWindow"/> - the page is headed LAST
        /// SEVEN DAYS and a heading that can be paged past is a heading that lies.</summary>
        int financeDayBack;

        TMP_Text financeWindowLine;

        void BuildFinancesPage(RectTransform sheet)
        {
            var root = NewPageRoot(sheet, LedgerPage.Finances);

            LedgerV2.PageHead(root, PageLeft, PageTop, PageWidth, "FINANCES",
                "THE BOOKS · LAST SEVEN DAYS · STRUCK AS THEY STAND THIS MINUTE");

            // The two keys that turn the pad, held to the right margin against the
            // title's baseline, with the window they walk written to their left.
            const float keyH = 28f;
            var laterW = LedgerV2.ButtonWidth("LATER DAYS ›", 10.5f, 6f, 14f);
            var earlierW = LedgerV2.ButtonWidth("‹ EARLIER DAYS", 10.5f, 6f, 14f);
            var laterX = PageLeft + PageWidth - laterW;
            var earlierX = laterX - 8f - earlierW;

            LedgerV2.Button(root, "‹ EARLIER DAYS", earlierX, PageTop - 8f, earlierW,
                keyH, () =>
                {
                    var sheets = outfit ? outfit.Accounts.Sheets.Count : 1;
                    var back = sheets - 1;
                    if (back > Outfit.Campaign.BooksWindow - 1)
                        back = Outfit.Campaign.BooksWindow - 1;
                    if (financeDayBack < back)
                    {
                        financeDayBack++;
                        dirty = true;
                    }
                }, LedgerV2.Key.Dark, 10.5f);
            LedgerV2.Button(root, "LATER DAYS ›", laterX, PageTop - 8f, laterW, keyH,
                () =>
                {
                    if (financeDayBack > 0)
                    {
                        financeDayBack--;
                        dirty = true;
                    }
                }, LedgerV2.Key.Dark, 10.5f);

            financeWindowLine = LedgerV2.Mono(root, PageLeft + PageWidth * 0.4f,
                PageTop - 12f, PageWidth * 0.6f - (laterW + earlierW + 14f), "", 11f,
                LedgerV2.Muted, 2f, TextAlignmentOptions.MidlineRight);

            financesContent = NewRect("Books", root);
            PlaceTopLeft(financesContent, PageLeft, FinTopY, PageWidth,
                -(PageBottom - FinTopY));
        }

        /// <summary>
        /// Paints the three cards and the foot under them. EVERY figure is derived at
        /// this moment from game state - never a stored display string.
        /// </summary>
        void RebuildFinances()
        {
            foreach (Transform old in financesContent)
                Destroy(old.gameObject);

            if (!outfit)
                return;

            var accounts = outfit.Accounts;
            var index = accounts.Sheets.Count - 1 - financeDayBack;
            if (index < 0)
                index = 0;
            var sheet = accounts.Sheets.Count > 0 ? accounts.Sheets[index] : null;
            var roster = director.Roster;

            var report = Outfit.FinanceReport.For(
                sheet,
                Outfit.Wages.DailyPayroll(roster),
                accounts.Safe,
                accounts.RiskyMoney,
                Outfit.BalanceMath.AssetsOf(roster));

            // The window the pad is turned to, written where the design writes it.
            var shownDate = News.NewsDate.FromClockDay(report.Day - 1);
            if (financeWindowLine)
                financeWindowLine.text = financeDayBack == 0
                    ? shownDate.Stamped() + " · TODAY STILL OPEN"
                    : shownDate.Stamped() + " · " +
                      (financeDayBack == 1 ? "YESTERDAY" : financeDayBack + " DAYS BACK") +
                      (report.Closed ? " · CLOSED" : "");

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
                    var wage = Outfit.Wages.WageFor(member, RosterDay);
                    if (member.Specialty != Specialty.None)
                    {
                        specialists++;
                        specialistWages += wage;
                    }
                    else if (member.Rank == Rank.Boss)
                    {
                        // The player owns the payroll. He is a real roster Character, but
                        // not a Hood and does not draw an envelope from his own outfit.
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

            var inH = FinBandH + FinRowH * 3f + FinTotalH + FinProfitH + FinTaxH * 3f;
            // WHAT THE ROWS MAY SAY DEPENDS ON WHETHER THE NIGHT IS OVER.
            //
            // An OPEN sheet is a forecast: the live roster IS the obligation, so the
            // payroll is broken out by rank - two lines, three with a specialist on
            // retainer - and that breakdown is exactly what TOTAL OUT carries.
            //
            // A CLOSED sheet is a record, and the breakdown cannot honestly be rebuilt
            // from a roster that has moved since: a man promoted or hired this morning
            // would rewrite last night's page. It gets ONE line, what was actually
            // paid, which is the figure TOTAL OUT is made of.
            var wageRows = report.Closed ? 1f : 2f + (specialists > 0 ? 1f : 0f);
            // Bribes, purchases, other costs.
            var outRows = wageRows + 3f;
            // And what the safe could not cover, which is NOT an outgoing - that money
            // never left. It stands under the total as the debt it is.
            var shortRows = report.WagesShort > 0 ? 1f : 0f;
            var outH = FinBandH + FinRowH * outRows + FinTotalH + FinRowH * shortRows +
                       (report.Closed ? FinRowH : FinBandH + FinRowH * 4f + FinTotalH);

            var moneyIn = LedgerV2.Card("Money in", financesContent, 0f, 0f, FinColW,
                inH, LedgerV2.Money);
            var moneyOut = LedgerV2.Card("Money out", financesContent, FinCol2X, 0f,
                FinColW, outH, LedgerV2.Money);

            // ---- MONEY IN ----
            var y = FinanceBand(moneyIn, "MONEY IN", FinInLabel);
            y = FinanceRow(moneyIn, y, "Protection", report.IllegalIncome, 0);
            y = FinanceRow(moneyIn, y, "Sales", report.SalesIncome, 1);
            y = FinanceRow(moneyIn, y, "Legitimate", report.LegalIncome, 2);
            y = FinanceTotal(moneyIn, y, "TOTAL IN", report.TotalIncome);

            var loss = report.TotalProfit < 0;
            var callout = NewRect("Profit", moneyIn);
            PlaceTopLeft(callout, 0f, y, FinColW, FinProfitH);
            Fill(callout, LedgerV2.ProfitBand);
            Rule(callout, 0f, 0f, FinColW, FinProfitRule);
            var profitInk = loss ? LedgerV2.Red : LedgerV2.Green;
            Caps(callout, FinPad, -12f, FinColW * 0.6f, "PROFIT AFTER TAX", 15f,
                profitInk, 5f);
            LedgerV2.Figure(callout, FinColW * 0.45f, -12f, FinColW * 0.55f - FinPad,
                LedgerText.Cash(report.TotalProfit), 22f, profitInk);
            y -= FinProfitH;

            y = FinanceTax(moneyIn, y, "Profit before tax", report.Profit,
                report.Profit < 0 ? LedgerV2.Red : LedgerV2.Ink);
            y = FinanceTax(moneyIn, y, "Tax due (" + Outfit.BalanceMath.TaxRatePercent +
                "%)", report.TaxDue, LedgerV2.Ink);
            FinanceTax(moneyIn, y, "Tax paid", report.TaxPaid,
                report.TaxPaid == 0 ? LedgerV2.Muted : LedgerV2.Ink);

            // ---- MONEY OUT ----
            var oy = FinanceBand(moneyOut, "MONEY OUT", FinOutLabel);
            var row = 0;
            if (report.Closed)
            {
                // The record: what left the safe, and nothing the live roster has to
                // say about it.
                oy = FinanceRow(moneyOut, oy, "Wages paid", report.Wages, row++);
            }
            else
            {
                oy = FinanceRow(moneyOut, oy, "Wages — " + hoods +
                    (hoods == 1 ? " hood" : " hoods"), hoodWages, row++);
                oy = FinanceRow(moneyOut, oy, "Wages — " + lieutenants +
                    (lieutenants == 1 ? " lieutenant" : " lieutenants"),
                    lieutenantWages, row++);
                if (specialists > 0)
                    oy = FinanceRow(moneyOut, oy, "Retainers — " + specialists,
                        specialistWages, row++);
            }
            oy = FinanceRow(moneyOut, oy, "Bribes", report.Bribes, row++);
            oy = FinanceRow(moneyOut, oy, "Purchases", report.Purchases, row++);
            oy = FinanceRow(moneyOut, oy, "Other costs", report.OtherCosts, row);
            oy = FinanceTotal(moneyOut, oy, "TOTAL OUT", report.TotalOutgoings);

            // WAGE-003. What the safe could not cover, said in red on the sheet that
            // covers the night it happened - never a payroll silently taking the safe
            // below zero.
            //
            // UNDER the total and never among the rows above it. It is money that did
            // NOT move, and printing it as an outgoing made the column lie twice over:
            // a $300 payroll with $200 paid drew $300 of wage rows plus a $100 SHORT
            // against a $200 TOTAL OUT, so the visible items overstated the night by
            // twice the shortfall.
            if (report.WagesShort > 0)
                oy = FinanceRow(moneyOut, oy, "STILL OWED — envelopes not paid",
                    report.WagesShort, -1, red: true);

            // Stocks are NOW-figures; a closed day's page keeps to its flows.
            if (!report.Closed)
            {
                oy = FinanceBand(moneyOut, "WHAT THE OUTFIT IS WORTH", LedgerV2.HeadInk,
                    unit: false, at: oy);
                oy = FinanceRow(moneyOut, oy, "Money in safe", report.Safe, -1);
                oy = FinanceRow(moneyOut, oy, "Stock at book value", report.Assets, -1);
                var risky = report.Risk >= Outfit.RiskRating.Moderate;
                oy = FinanceRow(moneyOut, oy, "Risky money (unlaundered)",
                    report.RiskyMoney, -1, red: risky);
                oy = FinanceText(moneyOut, oy, "Risk",
                    LedgerText.RiskLabel(report.Risk).ToUpperInvariant(), -1,
                    risky ? LedgerV2.Red : LedgerV2.Green);
                FinanceTotal(moneyOut, oy, "TOTAL WEALTH", report.TotalWealth);
            }
            else
            {
                Line(moneyOut, LedgerStyle.MonoItalic, 13f, LedgerV2.Muted, FinPad,
                    oy - 9f, FinColW - FinPad * 2f, LineBox(13f),
                    "A closed day — the record of what moved.");
            }

            var tapeH = BuildTapeRun(report, accounts, roster);

            // ---- the foot, under the tallest of the three ----
            var gridH = Mathf.Max(inH, Mathf.Max(outH, tapeH));
            BuildFinanceFoot(roster, -(gridH + 18f));
        }

        /// <summary>A card's dark band: what the column is, in the colour of which way
        /// the money runs, and the unit held to its right. Answers the y below it.
        /// </summary>
        float FinanceBand(RectTransform card, string label, Color ink,
            bool unit = true, float at = 0f)
        {
            var band = NewRect("Band " + label, card);
            PlaceTopLeft(band, 0f, at, FinColW, FinBandH);
            Fill(band, LedgerV2.Head);

            var name = LedgerV2.Mono(band, FinPad, -9f, FinColW - 120f, label, 10f, ink,
                13f);
            name.font = LedgerStyle.MonoBold;
            if (unit)
                LedgerV2.Mono(band, FinColW - 116f, -9f, 100f, "DOLLARS", 10f,
                    FinBandDim, 8f, TextAlignmentOptions.MidlineRight);
            return at - FinBandH;
        }

        /// <summary>One figure row on a green card: the label on the left, the money
        /// held to the right, a hairline under it and the design's stripe on every
        /// other one. A stripe index below zero is a row the design leaves unstriped.
        /// </summary>
        float FinanceRow(RectTransform card, float y, string label, int amount,
            int stripe, bool red = false) =>
            FinanceText(card, y, label, LedgerText.Cash(amount), stripe,
                red ? LedgerV2.Red : amount == 0 ? LedgerV2.Muted : LedgerV2.Ink);

        float FinanceText(RectTransform card, float y, string label, string value,
            int stripe, Color ink)
        {
            var row = NewRect("Figure " + label, card);
            PlaceTopLeft(row, 0f, y, FinColW, FinRowH);
            if (stripe > 0 && (stripe & 1) == 1)
                Fill(row, LedgerV2.MoneyStripe);
            Rule(row, 0f, -(FinRowH - 1f), FinColW, LedgerV2.MoneyRule);

            var text = Line(row, LedgerStyle.Mono, 13f, FinRowInk, FinPad, -9f,
                FinColW - FinPad * 2f - 150f, LineBox(13f), label);
            text.overflowMode = TextOverflowModes.Ellipsis;

            LedgerV2.Figure(row, FinColW - FinPad - 150f, -9f, 150f, value, 13.5f, ink);
            return y - FinRowH;
        }

        /// <summary>A card's ruled total: the heavy rule the accountant strikes, the
        /// word in the condensed gothic and the figure in the mono.</summary>
        float FinanceTotal(RectTransform card, float y, string label, int amount)
        {
            var row = NewRect("Total " + label, card);
            PlaceTopLeft(row, 0f, y, FinColW, FinTotalH);
            Rule(row, 0f, 0f, FinColW, LedgerV2.MoneyEdge, 2f);
            Caps(row, FinPad, -11f, FinColW * 0.6f, label, 15f, LedgerV2.Ink, 6f);
            LedgerV2.Figure(row, FinColW - FinPad - 170f, -11f, 170f,
                LedgerText.Cash(amount), 17f, LedgerV2.Ink);
            return y - FinTotalH;
        }

        /// <summary>A tax line under the profit callout: smaller type, a dotted leader
        /// under it, and no stripe.</summary>
        float FinanceTax(RectTransform card, float y, string label, int amount, Color ink)
        {
            var row = NewRect("Tax " + label, card);
            PlaceTopLeft(row, 0f, y, FinColW, FinTaxH);
            DottedRule(row, 0f, -(FinTaxH - 1f), FinColW, FinDotted);

            Line(row, LedgerStyle.Mono, 12.5f, FinTaxLabel, FinPad, -8f,
                FinColW - FinPad * 2f - 150f, LineBox(12.5f), label);
            LedgerV2.Figure(row, FinColW - FinPad - 150f, -8f, 150f,
                LedgerText.Cash(amount), 13f, ink);
            return y - FinTaxH;
        }

        /// <summary>
        /// The tape: the LAST SEVEN DAYS punched through the machine, one line a day in
        /// the order they happened, then netted. It is the check on the two cards beside
        /// it - they show one day, and one day of a real-time outfit can look like
        /// nothing at all; the run says whether that day was normal.
        ///
        /// Fixed pitch throughout: a tape that does not line up is not a tape. Answers
        /// the height the card came to, because the grid is laid to the tallest of the
        /// three.
        /// </summary>
        float BuildTapeRun(Outfit.FinanceReport report, Outfit.Accounts accounts,
            Roster roster)
        {
            // The window: the last seven sheets there are, oldest first, so the run
            // reads down the tape the way the days actually fell.
            var first = accounts.Sheets.Count - Outfit.Campaign.BooksWindow;
            if (first < 0)
                first = 0;
            var days = accounts.Sheets.Count - first;

            var height = 14f + 19f + 3f + 13f + 12f + days * FinTapeRowH +
                         12f + 2f + 9f + 24f + 12f + 13f + 20f;

            var card = LedgerV2.Card("Tape", financesContent, FinCol3X, 0f, FinColW,
                height, LedgerV2.Tape);
            var inner = FinColW - FinTapePad * 2f;

            var y = -14f;
            Caps(card, FinTapePad, y, inner, "ADDING MACHINE TAPE", 14f, LedgerV2.Ink, 6f);
            y -= 19f + 3f;
            LedgerV2.Mono(card, FinTapePad, y, inner, "SEVEN DAYS, IN ORDER", 10f,
                LedgerV2.Label, 5f);
            y -= 13f + 12f;

            var liveWages = Outfit.Wages.DailyPayroll(roster);
            var run = 0;
            for (var i = first; i < accounts.Sheets.Count; i++)
            {
                var day = accounts.Sheets[i];
                var line = Outfit.FinanceReport.For(day, liveWages, accounts.Safe,
                    accounts.RiskyMoney, 0);
                var net = line.TotalIncome - line.TotalOutgoings;
                run += net;

                var stamp = News.NewsDate.FromClockDay(day.Day - 1);
                // The day on the glass is marked so the eye finds it in the run.
                var label = (day.Day == report.Day ? "▸ " : "  ") + stamp.Short();
                y = TapeLine(card, label, net, y, inner);
            }

            y -= 12f;
            Rule(card, FinTapePad, y, inner, FinNetRule, 2f);
            y -= 9f;
            Caps(card, FinTapePad, y, 120f, "NET", 15f, FinNetLabel, 8f);
            Line(card, LedgerStyle.MonoBold, 18f,
                run < 0 ? LedgerV2.Red : LedgerV2.Green, FinTapePad + inner - 170f, y,
                170f, LineBox(18f), LedgerText.Cash(run),
                TextAlignmentOptions.MidlineRight);
            y -= 24f + 12f;

            LedgerV2.Mono(card, FinTapePad, y, inner, "RIBBON LOW · REPLACE SOON", 9.5f,
                FinRibbon, 7f);
            return height;
        }

        float TapeLine(RectTransform card, string label, int amount, float y, float inner)
        {
            var row = NewRect("Tape line", card);
            PlaceTopLeft(row, FinTapePad, y, inner, FinTapeRowH);
            DottedRule(row, 0f, -(FinTapeRowH - 1f), inner, FinDotted);

            Line(row, LedgerStyle.Mono, 11.5f, FinTaxLabel, 0f, -5f, inner - 130f,
                LineBox(11.5f), label);
            Line(row, LedgerStyle.MonoBold, 12.5f,
                amount < 0 ? LedgerV2.Red : LedgerV2.Green, inner - 130f, -5f, 130f,
                LineBox(12.5f), LedgerText.Cash(amount),
                TextAlignmentOptions.MidlineRight);
            return y - FinTapeRowH;
        }

        /// <summary>The remark and the box the books are posted in, across the foot of
        /// the sheet under a hairline. The name in the box is the outfit's OWN
        /// accountant if it has bought one - and when it has not, the box says who is
        /// doing the books instead.</summary>
        void BuildFinanceFoot(Roster roster, float y)
        {
            Rule(financesContent, 0f, y, PageWidth, FinFootRule);
            y -= 14f;

            const float signW = 220f;
            const float remarkW = 900f;
            LedgerV2.Copytext(financesContent, 0f, y,
                Mathf.Min(remarkW, PageWidth - signW - 40f), 72f,
                "Every figure is struck from the books as they stand this minute — the " +
                "sheet moves while you read it. The books close at midnight and the men " +
                "are paid then, every day, whether they worked it or not. A big crew " +
                "with no earner under it is the classic way an outfit dies.",
                14.5f, FinRemark);

            string bookkeeper = null;
            if (roster != null)
                foreach (var member in roster.Members)
                    if (member.Specialty == Specialty.Accountant && !member.Gone)
                    {
                        bookkeeper = member.FirstName.Substring(0, 1) + ". " + member.Surname;
                        break;
                    }

            var box = NewRect("Signature", financesContent);
            PlaceTopLeft(box, PageWidth - signW, y, signW, 66f);
            Frame(box, 1f, FinSignRule);
            LedgerV2.Mono(box, 18f, -8f, signW - 36f, "POSTED BY", 9f, LedgerV2.Label,
                12f, TextAlignmentOptions.Center);
            Line(box, LedgerStyle.SerifItalic, 19f, LedgerV2.Signature, 18f, -22f,
                signW - 36f, LineBox(19f), bookkeeper ?? "the boss himself",
                TextAlignmentOptions.Center);
            LedgerV2.Mono(box, 18f, -46f, signW - 36f,
                bookkeeper != null ? "BOOKKEEPER" : "NO BOOKKEEPER ON THE BOOKS", 9f,
                LedgerV2.Label, 8f, TextAlignmentOptions.Center);
        }
    }
}
