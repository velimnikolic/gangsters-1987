using TMPro;
using UnityEngine;
using LivingCity.Personnel;
using static LivingCity.UI.LedgerKit;

namespace LivingCity.UI
{
    /// <summary>
    /// FINANCES: a greenbar sheet off the accounting machine, money in down the left
    /// and money out down the right, with the adding-machine tape torn off and laid
    /// beside it. Totals are ruled the way an accountant rules them; a loss is in red;
    /// and the profit after tax gets the boxed callout because it is the one figure the
    /// whole outfit turns on.
    ///
    /// The books are kept BY THE DAY - there is no week in this game and nothing waits
    /// for one. The greenbar shows ONE day, and EARLIER / LATER walk the seven the page
    /// is headed with, one day a press. The tape beside it is the check on that: it
    /// runs all seven days at once, so a day that looks quiet on its own can still be
    /// read against the week of trading it sits in.
    ///
    /// Every figure is derived at read - wages come off the live roster through
    /// Wages.DailyPayroll, the rest through BalanceMath - so hiring a man moves this
    /// page the same frame. A closed day is a record and says so.
    /// </summary>
    public sealed partial class PersonnelAlmanac
    {
        /// <summary>The page's own head, over the books.</summary>
        const float FinancesHeadH = 72f;

        static float FinTopY = PageTop - FinancesHeadH;
        static float FinanceH = -(PageBottom - FinTopY);

        /// <summary>The till roll torn off and laid down the right margin.</summary>
        const float TapeW = 296f;
        const float TapeGap = 24f;

        static float GreenW = PageWidth - TapeW - TapeGap;
        const float GreenPad = 16f;
        static float GreenInner = GreenW - GreenPad * 2f;

        /// <summary>The two money columns on the greenbar.</summary>
        const float FinColGap = 24f;
        static float FinColW = (GreenInner - FinColGap) * 0.5f;
        static float FinRightX = FinColW + FinColGap;

        /// <summary>The banded rows the sheet is printed on.</summary>
        const float FinPitch = 26f;

        /// <summary>Where the first banded row's baseline sits, under the head band.</summary>
        const float FinTop = -64f;

        /// <summary>The greenbar takes what the till roll leaves. Measured, because the
        /// sheet is full bleed and the roll's width is fixed - a wider window widens the
        /// two money columns and nothing else.</summary>
        static void MeasureFinancesLayout()
        {
            FinTopY = PageTop - FinancesHeadH;
            FinanceH = -(PageBottom - FinTopY);
            GreenW = PageWidth - TapeW - TapeGap;
            GreenInner = GreenW - GreenPad * 2f;
            FinColW = (GreenInner - FinColGap) * 0.5f;
            FinRightX = FinColW + FinColGap;
        }

        RectTransform financesSheet;
        RectTransform financesContent;
        RectTransform tapeContent;

        /// <summary>How many days back the pad is turned; 0 = today's open sheet. Held
        /// inside <see cref="Outfit.Campaign.BooksWindow"/> - the page is headed LAST
        /// SEVEN DAYS and a heading that can be paged past is a heading that lies.</summary>
        int financeDayBack;

        void BuildFinancesPage(RectTransform sheet)
        {
            var root = NewPageRoot(sheet, LedgerPage.Finances);

            LedgerV2.PageHead(root, PageLeft, PageTop, PageWidth, "FINANCES",
                "THE BOOKS · LAST SEVEN DAYS · STRUCK AS THEY STAND THIS MINUTE");

            financesSheet = LedgerV2.Card("Books", root, PageLeft, FinTopY, GreenW,
                FinanceH, LedgerV2.Money);

            // The panel's own dark band: which of the seven days is on the glass, and
            // the two keys that turn the pad.
            var band = NewRect("Head", financesSheet);
            PlaceTopLeft(band, 0f, 0f, GreenW, 30f);
            Fill(band, LedgerV2.Head);

            financeHeading = LedgerV2.Mono(band, GreenPad, -8f, 400f, "", 10f,
                LedgerV2.HeadInk, 13f);
            financeHeading.font = LedgerStyle.MonoBold;

            financeDayLine = LedgerV2.Mono(band, GreenPad + 410f, -8f, GreenInner - 700f,
                "", 10f, LedgerV2.HeadDim, 4f, TextAlignmentOptions.MidlineRight);

            LedgerV2.Button(band, "< EARLIER DAYS", GreenInner - 268f, -3f, 130f, 24f, () =>
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
            }, red: false, size: 10f);
            LedgerV2.Button(band, "LATER DAYS >", GreenInner - 128f, -3f, 128f, 24f, () =>
            {
                if (financeDayBack > 0)
                {
                    financeDayBack--;
                    dirty = true;
                }
            }, red: false, size: 10f);

            financesContent = NewRect("Figures", financesSheet);
            Stretch(financesContent);

            BuildAddingTape(root);
        }

        TMP_Text financeHeading;
        TMP_Text financeDayLine;

        /// <summary>The adding-machine tape: the same week run through the machine, in
        /// the order the operator punched it, torn off at the bottom. It is a CHECK on
        /// the sheet beside it, which is what a tape is for.</summary>
        void BuildAddingTape(RectTransform root)
        {
            var tape = LedgerV2.Card("Tape", root, PageLeft + GreenW + TapeGap, FinTopY,
                TapeW, FinanceH, LedgerV2.Tape);

            tapeContent = NewRect("Run", tape);
            Stretch(tapeContent);

            // Torn off the roll: the teeth are the colour of the sheet behind it.
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

            // The heading is the SECTION - the seven days the page covers - and the
            // line under it says which of them is on the glass. Two lines rather than
            // one because they answer two different questions.
            if (financeHeading)
                financeHeading.text = "THE BOOKS";

            var shownDate = News.NewsDate.FromClockDay(report.Day - 1);
            if (financeDayLine)
                financeDayLine.text = financeDayBack == 0
                    ? shownDate.Stamped() + "  ·  TODAY, STILL OPEN"
                    : shownDate.Stamped() + "  ·  " +
                      (financeDayBack == 1 ? "YESTERDAY" : financeDayBack + " DAYS BACK") +
                      (report.Closed ? "  ·  CLOSED" : "");

            if (report.Closed)
                LedgerV2.Status(financesContent, GreenPad + GreenInner - 120f, -4f, 120f,
                    22f, "CLOSED", LedgerV2.Head, 10f);

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
                LedgerV2.MoneyEdge, 2f);
            Rule(financesContent, GreenPad + FinRightX, totalsY + FinPitch - 2f, FinColW,
                LedgerV2.MoneyEdge, 2f);
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
                loss ? LedgerV2.Red : LedgerV2.MoneyEdge, 4f);
            var figure = Line(callout, LedgerStyle.Condensed, 20f,
                loss ? LedgerV2.Red : LedgerV2.MoneyEdge,
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
                    LedgerV2.MoneyEdge, 2f);
                FinanceRow(FinRightX, rightY, "TOTAL WEALTH", report.TotalWealth, bold: true);
                rightY -= FinPitch;
            }
            else
            {
                Line(financesContent, LedgerStyle.MonoItalic, 13f, LedgerV2.Muted,
                    GreenPad + FinRightX, rightY, FinColW, FinPitch,
                    "A closed day - the record of what moved.");
                rightY -= FinPitch;
            }

            // Pinned to the foot of the sheet, not to wherever the figures ended:
            // the explainer and the signature are the sheet's bottom margin.
            BuildFinanceFoot(roster, -(FinanceH - 96f));
            BuildTapeRun(report, accounts, roster);
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

            Paragraph(foot, LedgerStyle.Serif, 14f, LedgerV2.MoneyEdge, 14f, -10f,
                GreenInner - signW - 40f, 58f,
                "Every figure is struck from the books as they stand this minute — the " +
                "sheet moves while you read it. The books close at midnight and the men " +
                "are paid then, every day, whether they worked it or not. A big crew " +
                "with no earner under it is the classic way an outfit dies.",
                lineSpacing: 3f);

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
            Caps(box, 0f, -6f, signW, "POSTED BY", 9f, LedgerV2.Label, 3f,
                TextAlignmentOptions.Center);
            Line(box, LedgerStyle.SerifItalic, 17f, LedgerV2.PaperBlue, 0f, -20f, signW,
                24f, bookkeeper ?? "the boss himself", TextAlignmentOptions.Center);
            Caps(box, 0f, -44f, signW, bookkeeper != null ? "BOOKKEEPER" : "NO BOOKKEEPER ON THE BOOKS",
                8.5f, LedgerV2.Label, 2f, TextAlignmentOptions.Center);
        }

        /// <summary>
        /// The tape: the LAST SEVEN DAYS punched through the machine, one line a day in
        /// the order they happened, then netted. It is the check on the greenbar beside
        /// it - the greenbar shows one day, and one day of a real-time outfit can look
        /// like nothing at all; the run says whether that day was normal.
        ///
        /// Fixed pitch throughout: a tape that does not line up is not a tape.
        /// </summary>
        void BuildTapeRun(Outfit.FinanceReport report, Outfit.Accounts accounts,
            Roster roster)
        {
            const float pitch = 17f;
            const float pad = 18f;
            var inner = TapeW - pad * 2f;
            var y = -18f;

            Caps(tapeContent, pad, y, inner, "ADDING MACHINE TAPE", 10f,
                LedgerV2.Label, 4f);
            y -= 20f;
            Caps(tapeContent, pad, y, inner, "SEVEN DAYS, IN ORDER", 8.5f,
                LedgerV2.Muted, 3f);
            y -= 22f;

            // The window: the last seven sheets there are, oldest first, so the run
            // reads down the tape the way the days actually fell.
            var first = accounts.Sheets.Count - Outfit.Campaign.BooksWindow;
            if (first < 0)
                first = 0;

            var liveWages = Outfit.Wages.DailyPayroll(roster);
            var runIn = 0;
            var runOut = 0;
            for (var i = first; i < accounts.Sheets.Count; i++)
            {
                var day = accounts.Sheets[i];
                var line = Outfit.FinanceReport.For(day, liveWages, accounts.Safe,
                    accounts.RiskyMoney, 0);
                runIn += line.TotalIncome;
                runOut += line.TotalOutgoings;

                var stamp = News.NewsDate.FromClockDay(day.Day - 1);
                var net = line.TotalIncome - line.TotalOutgoings;
                // The day on the glass is marked so the eye finds it in the run.
                var label = (day.Day == report.Day ? "\u25B8 " : "  ") + stamp.Short();
                y = TapeLine(label, net, y, pad, inner, pitch);
            }

            y -= 4f;
            y = TapeLine("seven days in", runIn, y, pad, inner, pitch, true);
            y = TapeLine("seven days out", runOut, y, pad, inner, pitch, true);
            y -= 6f;

            LedgerV2.Leader(tapeContent, pad, y + 8f, inner);
            var run = runIn - runOut;
            Caps(tapeContent, pad, y, 120f, "NET", 12f, LedgerV2.Red, 4f);
            Line(tapeContent, LedgerStyle.MonoBold, 13.5f, LedgerV2.Red,
                pad + inner - 160f, y, 160f, pitch, LedgerText.Cash(run),
                TextAlignmentOptions.MidlineRight);
            y -= 24f;

            // What the outfit owes upward, if anything - the one outgoing that is not
            // on the greenbar's own columns until the day it is actually handed over.
            if (outfit)
            {
                var owed = outfit.Tribute.TotalOwed();
                if (owed > 0)
                    y = TapeLine("tribute standing", owed, y, pad, inner, pitch);
            }

            y -= 6f;
            Caps(tapeContent, pad, y, inner, "RIBBON LOW · REPLACE SOON", 8.5f,
                LedgerV2.Label, 2f);
        }

        float TapeLine(string label, int amount, float y, float pad, float inner,
            float pitch, bool rule = false)
        {
            Line(tapeContent, LedgerStyle.Mono, 12f, LedgerV2.Body, pad, y,
                inner - 130f, pitch, label);
            Line(tapeContent, LedgerStyle.Mono, 12f, LedgerV2.Body, pad + inner - 150f,
                y, 150f, pitch, LedgerText.Cash(amount), TextAlignmentOptions.MidlineRight);
            if (rule)
                Rule(tapeContent, pad, y - pitch + 2f, inner, LedgerV2.Rule);
            return y - pitch;
        }

        /// <summary>The dark band over a money column: what the column is, in the
        /// colour of which way the money runs, and the unit held to its right.</summary>
        void FinanceColumnHead(float x, float y, string label)
        {
            var band = NewRect("Column head", financesContent);
            PlaceTopLeft(band, GreenPad + x, y + 8f, FinColW, 28f);
            Fill(band, LedgerV2.Head);

            var name = LedgerV2.Mono(band, 12f, -7f, FinColW - 100f, label, 10f,
                label == "MONEY IN" ? LedgerV2.HeadStreet : LedgerV2.Boss, 13f);
            name.font = LedgerStyle.MonoBold;
            LedgerV2.Mono(band, FinColW - 112f, -7f, 100f, "DOLLARS", 10f,
                LedgerV2.HeadDim, 8f, TextAlignmentOptions.MidlineRight);
        }

        void FinanceRow(float x, float y, string label, int amount, bool bold = false,
            bool dim = false, bool red = false) =>
            FinanceText(x, y, label, LedgerText.Cash(amount), bold, dim, red);

        void FinanceText(float x, float y, string label, string value, bool bold = false,
            bool dim = false, bool red = false)
        {
            var colour = red ? LedgerV2.Red
                : dim ? LedgerV2.Muted
                : LedgerV2.MoneyEdge;
            var font = bold ? LedgerStyle.MonoBold : LedgerStyle.Mono;

            var text = Line(financesContent, font, 13.5f, colour, GreenPad + x + 4f, y,
                FinColW - 160f, FinPitch, label);
            text.overflowMode = TextOverflowModes.Ellipsis;

            Line(financesContent, font, 13.5f, colour, GreenPad + x + FinColW - 150f, y,
                146f, FinPitch, value, TextAlignmentOptions.MidlineRight);

            // A dotted leader carries the eye from the word to the figure - the one
            // job the whole of a ruled sheet exists to do.
            if (!bold)
                LedgerV2.Leader(financesContent, GreenPad + x + 4f, y - FinPitch + 5f,
                    FinColW - 8f);
        }
    }
}
