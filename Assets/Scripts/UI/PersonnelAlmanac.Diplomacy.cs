using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static LivingCity.UI.LedgerKit;

namespace LivingCity.UI
{
    /// <summary>
    /// FAMILIES: the card index - one ruled index card a house, filed across the sheet,
    /// each with its tag and card number, the capo's Polaroid clipped to it, the door
    /// it operates behind written in the margin in pen, what ground it holds, and the
    /// three stances with the standing choice ringed in red. The player's own line
    /// reads first, above the index, as the yardstick.
    ///
    /// Strength is never printed as a figure: the game has no reconnaissance, and a
    /// number the player could not have earned would be the one lie on the sheet. What
    /// IS printed is turf, which is on the map and therefore knowable.
    /// </summary>
    public sealed partial class PersonnelAlmanac
    {
        // ---- the boss's own line, over the index ----

        const float FamilyMineY = PageTop - 48f;
        const float FamilyMineH = 56f;

        // ---- the index itself ----

        const int FamilyColumns = 5;
        const float FamilyGap = 18f;
        const float FamilyCardW = (PageWidth - FamilyGap * (FamilyColumns - 1)) / FamilyColumns;
        /// <summary>Four rows deep: standing, turf, capos and what is OWED upward. The
        /// tribute line is the reason the card grew - a house you are behind with is a
        /// house that is about to be a problem, and it belongs on its own card.</summary>
        const float FamilyCardH = 272f;

        const float FamiliesTop = FamilyMineY - FamilyMineH - 10f;
        const float FamiliesHeight = 452f;

        const float LegendTop = FamiliesTop - FamiliesHeight - 8f;

        /// <summary>The ruling on an index card - the design's 26-unit blue lines.</summary>
        const float FamilyRulePitch = 26f;

        RectTransform diplomacyContent;
        RectTransform familiesViewport;
        RectTransform familiesContent;
        float familiesScroll;

        void BuildDiplomacyPage(RectTransform sheet)
        {
            var root = NewPageRoot(sheet, LedgerPage.Diplomacy);
            diplomacyContent = NewRect("Families", root);
            Stretch(diplomacyContent);

            // The rivals live in a window of their own so a family added to the
            // catalog never pushes the legend off the sheet.
            familiesViewport = NewRect("Index", root);
            PlaceTopLeft(familiesViewport, PageLeft, FamiliesTop, PageWidth, FamiliesHeight);
            familiesViewport.gameObject.AddComponent<RectMask2D>();

            familiesContent = NewRect("Cards", familiesViewport);
            familiesContent.anchorMin = new Vector2(0f, 1f);
            familiesContent.anchorMax = new Vector2(1f, 1f);
            familiesContent.pivot = new Vector2(0f, 1f);
            familiesContent.anchoredPosition = Vector2.zero;
            familiesContent.sizeDelta = new Vector2(0f, FamiliesHeight);
        }

        void RebuildDiplomacy()
        {
            foreach (Transform old in diplomacyContent)
                Destroy(old.gameObject);
            foreach (Transform old in familiesContent)
                Destroy(old.gameObject);

            var gangs = Gangs.GangRegistry.Gangs;
            var rivals = 0;
            foreach (var gang in gangs)
                if (!gang.IsPlayer)
                    rivals++;

            var heading = Line(diplomacyContent, LedgerStyle.Condensed, 19f, LedgerStyle.Ink,
                PageLeft, PageTop, 700f, 26f,
                "THE CARD INDEX · " + (rivals == 1 ? "ONE HOUSE" : rivals + " HOUSES"));
            heading.characterSpacing = 5f;
            Caps(diplomacyContent, PageRight - 600f, PageTop - 1f, 600f,
                "PULLED FROM THE ROLODEX · KEEP IN ORDER", 10f, LedgerStyle.InkLabel, 4f,
                TextAlignmentOptions.MidlineRight);

            if (gangs.Count == 0)
            {
                Line(diplomacyContent, LedgerStyle.MonoItalic, 14f, LedgerStyle.InkDim,
                    PageLeft, PageTop - 46f, 800f, 24f,
                    "The families have not shown themselves yet.");

                // DEV, editor only: deal a dummy hand of families so the page can be
                // seen dressed before the street layer seeds the real ones. The real
                // generator with a fixed seed, so the preview IS the live layout.
                if (Application.isEditor)
                    Tape(diplomacyContent, "DEAL DUMMY FAMILIES", PageLeft, PageTop - 82f,
                        220f, 28f, () => Gangs.GangRegistry.Install(
                            Gangs.GangSeeder.Generate(1987, director.Roster)));
                return;
            }

            if (outfit)
                outfit.CollectHoldings(holdings);
            else
                holdings.Clear();

            // What the biggest house holds - the scale every turf meter is read against.
            var mostTurf = 1;
            foreach (var gang in gangs)
            {
                var held = Outfit.Turf.CountOf(holdings, gang.Id);
                if (held > mostTurf)
                    mostTurf = held;
            }

            BuildOwnLine(gangs);

            // The rivals, in the window: their y is measured from ITS top edge, not the
            // page's, so scrolling is one anchoredPosition and never a re-layout.
            var slot = 0;
            foreach (var gang in gangs)
            {
                if (gang.IsPlayer)
                    continue;
                FamilyCard(gang, slot, mostTurf);
                slot++;
            }

            var cardRows = (slot + FamilyColumns - 1) / FamilyColumns;
            SizeFamiliesContent(cardRows * FamilyCardH +
                Mathf.Max(0, cardRows - 1) * FamilyGap);

            // The window has no edge of its own on the paper, so the count says what is
            // in the drawer and the wheel says how to reach it. Printed once, on the
            // fixed layer: a rebuild per wheel notch would re-photograph twenty capos.
            if (slot > 0)
                Line(diplomacyContent, LedgerStyle.MonoItalic, 12f, LedgerStyle.InkDim,
                    PageLeft, FamiliesTop + 18f, PageWidth, 16f,
                    slot + " card" + (slot == 1 ? "" : "s") + " in the drawer" +
                    (cardRows * (FamilyCardH + FamilyGap) > FamiliesHeight
                        ? "  ·  roll the wheel over them"
                        : ""),
                    TextAlignmentOptions.MidlineRight);

            BuildStanceLegend();
        }

        /// <summary>The don's own line, over the index: his face, his colour, his front
        /// and his ground. A player who cannot read off his own address has to hunt the
        /// city for premises he supposedly owns - the map's gold square is a dot, not a
        /// street name.</summary>
        void BuildOwnLine(System.Collections.Generic.IReadOnlyList<Gangs.Gang> gangs)
        {
            foreach (var gang in gangs)
            {
                if (!gang.IsPlayer)
                    continue;

                var strip = NewRect("Yours", diplomacyContent);
                PlaceTopLeft(strip, PageLeft, FamilyMineY, PageWidth, FamilyMineH);
                Fill(strip, new Color(143f / 255f, 33f / 255f, 25f / 255f, 0.06f));
                Block("Edge", strip, 0f, 0f, 3f, FamilyMineH, LedgerStyle.RedPen);

                var raw = Plate(strip, 12f, -6f, 44f, 44f, "");
                PortraitStudio.Request(
                    PortraitStudio.FindPeoplePrefab(Gangs.GangCatalog.BossModel),
                    PortraitStudio.Framing.Bust, raw);
                Swatch(gang.Id, 68f, -8f, strip);

                var held = Outfit.Turf.CountOf(holdings, gang.Id);
                var name = Line(strip, LedgerStyle.Condensed, 20f, LedgerStyle.Ink, 92f, -6f,
                    620f, 26f, gang.Name.ToUpperInvariant() + " · YOURS");
                name.characterSpacing = 3f;
                Line(strip, LedgerStyle.Mono, 13f, LedgerStyle.InkDim, 92f, -30f, 620f, 20f,
                    "Boss: " + Gangs.GangCatalog.BossName + "  ·  " + held +
                    (held == 1 ? " building" : " buildings") + " on the map");

                var mine = Gangs.GangRegistry.FrontBusinessOf(gang.Id);
                var myBooks = Gangs.GangRegistry.FrontBooksOf(gang.Id);
                var front = Line(strip, LedgerStyle.SerifItalic, 15f, LedgerStyle.Ballpoint,
                    PageWidth - 700f, -18f, 688f, 22f,
                    mine ? "Front: " + mine.BusinessName
                    : myBooks != null
                        ? "Front: " + myBooks.Sign +
                          (string.IsNullOrEmpty(myBooks.Address) ? "" : ", " + myBooks.Address)
                        : "Front: none of your own yet",
                    TextAlignmentOptions.MidlineRight);
                front.overflowMode = TextOverflowModes.Ellipsis;
                return;
            }
        }

        /// <summary>One house's index card, in the drawer's grid.</summary>
        void FamilyCard(Gangs.Gang gang, int slot, int mostTurf)
        {
            var column = slot % FamilyColumns;
            var row = slot / FamilyColumns;
            var x = column * (FamilyCardW + FamilyGap);
            var y = -row * (FamilyCardH + FamilyGap);

            // Square on the page: a tilted card turns every hairline on it into a
            // staircase. The Polaroid pinned to it carries the crookedness instead.
            var card = Card("Family " + gang.Name, familiesContent, x, y, FamilyCardW,
                FamilyCardH, LedgerStyle.IndexCard, shadowSpread: 8f,
                low: LedgerStyle.IndexCardLow);

            const float pad = 12f;
            var inner = FamilyCardW - pad * 2f;

            // The tag band a filed card carries across its head.
            var band = NewRect("Tag", card);
            PlaceTopLeft(band, 0f, 0f, FamilyCardW, 30f);
            Fill(band, new Color(143f / 255f, 33f / 255f, 25f / 255f, 0.10f));
            Caps(band, pad, -8f, 120f, "HOUSE " + (slot + 1).ToString("00"), 9.5f,
                LedgerStyle.DeepRed, 3f);
            Caps(band, FamilyCardW - pad - 100f, -8f, 100f,
                "R-" + (100 + gang.Id).ToString("000"), 9.5f, LedgerStyle.InkLabel, 3f,
                TextAlignmentOptions.MidlineRight);
            Swatch(gang.Id, FamilyCardW * 0.5f - 8f, -7f, band);

            // Ruled the way an index card is ruled, under everything typed on it.
            var ruling = NewRect("Ruling", card);
            PlaceTopLeft(ruling, 0f, -30f, FamilyCardW, FamilyCardH - 30f);
            ruling.gameObject.AddComponent<RectMask2D>();
            for (var line = FamilyRulePitch; line < FamilyCardH - 30f; line += FamilyRulePitch)
                Rule(ruling, 0f, -line, FamilyCardW, LedgerStyle.RuleBlue);

            // The face of the family: its capo, wearing the model his soldiers answer
            // to on the street. A family is as many crews as it has capos, and the
            // first of them is the name the drawer files it under.
            var leader = gang.Members.Count > 0 ? gang.Members[0].FullName : "";
            var capos = 0;
            foreach (var man in gang.Members)
                if (man.Lieutenant)
                    capos++;

            var raw = Polaroid(card, FamilyCardW - 84f, -36f, 56f,
                InitialsOf(leader.Length > 0 ? leader : gang.Name),
                gang.Id % 2 == 0 ? -4f : 3f, out _);
            PortraitStudio.Request(
                PortraitStudio.FindPeoplePrefab(Gangs.GangCatalog.LieutenantModels[gang.Id]),
                PortraitStudio.Framing.Bust, raw);

            // LineBox, not 28: a truncating line in the condensed gothic vanishes
            // WHOLE when its rect cannot hold the face's line box, and this name did.
            var name = Line(card, LedgerStyle.Condensed, 22f, LedgerStyle.Ink, pad, -36f,
                inner - 88f, LineBox(22f), gang.Name);
            name.characterSpacing = 1f;
            name.overflowMode = TextOverflowModes.Ellipsis;

            var runBy = Caps(card, pad, -72f, inner - 88f,
                leader.Length > 0 ? "RUN BY " + leader : "RUN BY PERSONS UNKNOWN", 9.5f,
                LedgerStyle.InkLabel, 2f);
            runBy.overflowMode = TextOverflowModes.Ellipsis;

            // ---- the three rows a card carries ----
            var current = outfit ? outfit.Relations.StanceWith(gang.Id) : Outfit.Stance.Peace;
            var pending = Outfit.Stance.Peace;
            var hasPending = outfit && outfit.Relations.TryGetPending(gang.Id, out pending);

            CardRow(card, pad, -98f, inner, "STANDING",
                LedgerText.StanceLabel(current) + (hasPending
                    ? " → " + LedgerText.StanceLabel(pending) : ""),
                hasPending ? LedgerStyle.RedPen : LedgerStyle.Ink);

            // The meter is CENTRED on its label's line, not hung off the top of it -
            // hung off the top it climbed into the standing row above.
            var held = Outfit.Turf.CountOf(holdings, gang.Id);
            Caps(card, pad, -120f, 90f, "TURF", 9.5f, LedgerStyle.InkLabel, 3f);
            StepBar(card, pad + 90f, -128f, 10,
                Mathf.Clamp(Mathf.RoundToInt(10f * held / mostTurf), 0, 10),
                LedgerStyle.Ink, 5f, 10f, 7f);
            Line(card, LedgerStyle.Mono, 12f, LedgerStyle.InkDim, pad + inner - 90f, -120f,
                90f, 18f, held.ToString(), TextAlignmentOptions.MidlineRight);

            CardRow(card, pad, -142f, inner, "CAPOS",
                capos > 0 ? capos.ToString() : "not known", LedgerStyle.Ink);

            // What the outfit kicks up to this house, and when. A house below the
            // outfit levies nothing, and the row says so rather than printing $0 -
            // "nothing" is the answer the player is working toward.
            var levy = outfit ? outfit.Tribute.For(gang.Id) : null;
            var today = outfit ? outfit.Campaign.Day : 1;
            var hourNow = cityClock ? cityClock.Hour : 0f;
            CardRow(card, pad, -164f, inner, "OWED",
                levy == null || levy.Amount <= 0
                    ? "nothing — you are not under them"
                    : LedgerText.Cash(levy.Amount) + " · " +
                      (levy.Overdue
                          ? "OVERDUE"
                          : LedgerText.DueIn(levy.DueDay, today, hourNow)),
                levy != null && levy.Amount > 0
                    ? (levy.Overdue ? LedgerStyle.RedPen : LedgerStyle.Ink)
                    : LedgerStyle.InkDim);

            // The door it operates behind, written in the margin in pen. The generated
            // city binds a business marker; the street city binds only the books -
            // either one names the door.
            var front = Gangs.GangRegistry.FrontBusinessOf(gang.Id);
            var books = Gangs.GangRegistry.FrontBooksOf(gang.Id);
            Rule(card, pad, -188f, inner, LedgerStyle.InkFaint);
            var note = Paragraph(card, LedgerStyle.SerifItalic, 13.5f, LedgerStyle.Ballpoint,
                pad, -196f, inner, 38f,
                front ? front.BusinessName + " is the door."
                : books != null
                    ? books.Sign +
                      (string.IsNullOrEmpty(books.Address) ? "" : ", " + books.Address) +
                      " is the door."
                    : "Nobody has found their door yet.",
                lineSpacing: 1f);
            note.overflowMode = TextOverflowModes.Ellipsis;

            // ---- the three stances, the standing one ringed ----
            var effective = hasPending ? pending : current;
            var buttonW = (inner - 8f) / 3f;
            for (var s = 0; s < 3; s++)
            {
                var choice = (Outfit.Stance)s;
                var gangId = gang.Id;
                var button = Tape(card, LedgerText.StanceLabel(choice),
                    pad + s * (buttonW + 4f), -234f, buttonW, 26f, () =>
                    {
                        if (outfit)
                            outfit.SetStance(gangId, choice);
                        dirty = true;
                    }, red: choice == Outfit.Stance.War, size: 10f,
                    outline: choice != effective);
                if (choice == effective)
                    PenRing((RectTransform)button.transform.parent, LedgerStyle.RedPen);
            }
        }

        static void CardRow(Transform card, float x, float y, float w, string label,
            string value, Color ink)
        {
            Caps(card, x, y, 90f, label, 9.5f, LedgerStyle.InkLabel, 3f);
            var text = Line(card, LedgerStyle.Mono, 12.5f, ink, x + 90f, y, w - 90f, 18f,
                value);
            text.overflowMode = TextOverflowModes.Ellipsis;
        }

        /// <summary>The legend under the drawer - the page must never be the opaque
        /// system. Two columns so it takes a band and not a third of the sheet.</summary>
        void BuildStanceLegend()
        {
            Caps(diplomacyContent, PageLeft, LegendTop, PageWidth, "WHAT A STANCE DOES",
                12f, LedgerStyle.InkMid, 5f);
            Rule(diplomacyContent, PageLeft, LegendTop - 20f, PageWidth, LedgerStyle.InkFaint);

            var half = (PageWidth - 40f) * 0.5f;
            Paragraph(diplomacyContent, LedgerStyle.Mono, 12f, LedgerStyle.InkDim, PageLeft,
                LegendTop - 28f, half, 72f,
                LedgerText.StanceEffect(Outfit.Stance.Peace) + "\n" +
                LedgerText.StanceEffect(Outfit.Stance.Truce) + "\n" +
                LedgerText.StanceEffect(Outfit.Stance.War), lineSpacing: 3f);

            Paragraph(diplomacyContent, LedgerStyle.Mono, 12f, LedgerStyle.InkDim,
                PageLeft + half + 40f, LegendTop - 28f, half, 72f,
                LedgerText.StanceTakesEffect + "  Strength reads " +
                LedgerText.StrengthUnknown + " until you have eyes inside a family - " +
                "reconnaissance is work, not a birthright. Their turf shows on the map " +
                "in their colour; the streets are not a secret.", lineSpacing: 3f);
        }

        /// <summary>Height the cards actually came to, and the scroll held inside it -
        /// the armory counter's SizeCatalogueContent, for the same reason: the position
        /// must survive a rebuild, or every ownership change would throw the player back
        /// to the top of the drawer.</summary>
        void SizeFamiliesContent(float height)
        {
            familiesContent.sizeDelta = new Vector2(0f, Mathf.Max(FamiliesHeight, height));
            var maxScroll = Mathf.Max(0f, familiesContent.sizeDelta.y - FamiliesHeight);
            familiesScroll = Mathf.Clamp(familiesScroll, 0f, maxScroll);
            familiesContent.anchoredPosition = new Vector2(0f, familiesScroll);
        }

        /// <summary>The family's map colour, as the coloured dot sticker an office
        /// puts on a file.</summary>
        void Swatch(int gangId, float x, float y, Transform parent = null)
        {
            var rect = NewRect("Swatch", parent ? parent : diplomacyContent);
            PlaceTopLeft(rect, x, y, 16f, 16f);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = LedgerStyle.Disc;
            image.color = GangPalette.Of(gangId);
            image.raycastTarget = false;
        }
    }
}
