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

        /// <summary>The page's own head, over the index.</summary>
        const float FamiliesHeadH = 72f;

        static float FamilyMineY = PageTop - FamiliesHeadH;
        const float FamilyMineH = 56f;

        // ---- the index itself ----

        const float FamilyGap = 18f;
        const float FamilyCardMin = 272f;
        static int FamilyColumns = 5;
        static float FamilyCardW = (PageWidth - FamilyGap * (FamilyColumns - 1)) / FamilyColumns;
        /// <summary>Five rows deep: standing, turf, capos, what they have TAKEN off us
        /// and what is OWED upward. The tribute line is the reason the card grew - a
        /// house you are behind with is a house that is about to be a problem, and it
        /// belongs on its own card - and FOLLOW-002 added the fifth, because a house
        /// that has absorbed one of our lieutenants and his men is a standing fact
        /// about it and the paper's line about that night scrolls away in a week.</summary>
        const float FamilyCardH = 294f;

        static float FamiliesTop = FamilyMineY - FamilyMineH - 10f;
        static float FamiliesHeight = 452f;

        /// <summary>What the legend under the index takes off the foot of the sheet.</summary>
        const float LegendH = 116f;

        static float LegendTop = FamiliesTop - FamiliesHeight - 8f;

        /// <summary>The card index takes the sheet between the boss's own line and the
        /// legend that closes it. Full bleed, so a taller window is another row of cards
        /// and a wider one is another column - the drawer is deeper, not the cards.
        /// </summary>
        static void MeasureDiplomacyLayout()
        {
            FamilyMineY = PageTop - FamiliesHeadH;
            FamiliesTop = FamilyMineY - FamilyMineH - 10f;
            FamilyColumns = Mathf.Max(5,
                Mathf.FloorToInt((PageWidth + FamilyGap) / (FamilyCardMin + FamilyGap)));
            FamilyCardW = (PageWidth - FamilyGap * (FamilyColumns - 1)) / FamilyColumns;
            FamiliesHeight = Mathf.Max(FamilyCardH,
                -(PageBottom - FamiliesTop) - LegendH);
            LegendTop = FamiliesTop - FamiliesHeight - 8f;
        }

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

            LedgerV2.PageHead(diplomacyContent, PageLeft, PageTop, PageWidth, "FAMILIES",
                "THE CARD INDEX · " +
                (rivals == 1 ? "ONE HOUSE" : rivals + " HOUSES") +
                " · PULLED FROM THE ROLODEX");

            if (gangs.Count == 0)
            {
                Line(diplomacyContent, LedgerStyle.MonoItalic, 14f, LedgerV2.Muted,
                    PageLeft, PageTop - 46f, 800f, 24f,
                    "The families have not shown themselves yet.");

                // DEV, editor only: deal a dummy hand of families so the page can be
                // seen dressed before the street layer seeds the real ones. The real
                // generator with a fixed seed, so the preview IS the live layout.
                if (Application.isEditor)
                    LedgerV2.Button(diplomacyContent, "DEAL DUMMY FAMILIES", PageLeft, PageTop - 82f,
                        220f, 28f, () =>
                        {
                            var underworld = Outfit.Underworld.Ensure(1987);
                            Gangs.GangRegistry.Install(Gangs.GangSeeder.Generate(
                                1987, underworld.Dealt,
                                gang => underworld.Of(gang)?.Roster));
                        });
                return;
            }

            if (outfit)
                outfit.CollectKnownHoldings(holdings);
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
                Line(diplomacyContent, LedgerStyle.MonoItalic, 12f, LedgerV2.Muted,
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
                Fill(strip, LedgerV2.At(LedgerV2.Alert, 0.06f));
                Block("Edge", strip, 0f, 0f, 3f, FamilyMineH, LedgerV2.Red);

                var raw = LedgerV2.PortraitPlate(strip, 12f, -6f, 44f, 44f, "");
                var boss = director.Roster?.FindBoss();
                PortraitStudio.Request(
                    PortraitStudio.FindPeoplePrefab(
                        boss != null && !string.IsNullOrEmpty(boss.Look)
                            ? boss.Look : Gangs.GangCatalog.BossModel),
                    PortraitStudio.Framing.Bust, raw);
                Swatch(gang.Id, 68f, -8f, strip);

                var held = Outfit.Turf.CountOf(holdings, gang.Id);
                var name = Line(strip, LedgerStyle.Condensed, 20f, LedgerV2.Ink, 92f, -6f,
                    620f, 26f, gang.Name.ToUpperInvariant() + " · YOURS");
                name.characterSpacing = 3f;
                Line(strip, LedgerStyle.Mono, 13f, LedgerV2.Muted, 92f, -30f, 620f, 20f,
                    "Boss: " + (boss != null ? boss.FullName : Gangs.GangCatalog.BossName) +
                    "  ·  " + held +
                    (held == 1 ? " building" : " buildings") + " on the map");

                var mine = Gangs.GangRegistry.FrontBusinessOf(gang.Id);
                var myBooks = Gangs.GangRegistry.FrontBooksOf(gang.Id);
                var front = Line(strip, LedgerStyle.SerifItalic, 15f, LedgerV2.PaperBlue,
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
            var card = LedgerV2.Card("Family " + gang.Name, familiesContent, x, y, FamilyCardW, FamilyCardH);

            const float pad = 12f;
            var inner = FamilyCardW - pad * 2f;

            // The house's own colour across the card's head - the same colour its
            // turf is painted in on the map, so a card and a block answer to each other
            // without a legend.
            Block("Colour", card, 0f, 0f, FamilyCardW, 4f, GangPalette.Of(gang.Id));

            // The tag band a filed card carries under it.
            var band = NewRect("Tag", card);
            PlaceTopLeft(band, 0f, -4f, FamilyCardW, 26f);
            Fill(band, LedgerV2.PanelDark);
            Caps(band, pad, -8f, 120f, "HOUSE " + (slot + 1).ToString("00"), 9.5f,
                LedgerV2.Red, 3f);
            Caps(band, FamilyCardW - pad - 100f, -8f, 100f,
                "R-" + (100 + gang.Id).ToString("000"), 9.5f, LedgerV2.Label, 3f,
                TextAlignmentOptions.MidlineRight);
            Swatch(gang.Id, FamilyCardW * 0.5f - 8f, -7f, band);

            // Ruled the way an index card is ruled, under everything typed on it.
            var ruling = NewRect("Ruling", card);
            PlaceTopLeft(ruling, 0f, -30f, FamilyCardW, FamilyCardH - 30f);
            ruling.gameObject.AddComponent<RectMask2D>();

            // The face of the family: its capo, wearing the model his soldiers answer
            // to on the street. A family is as many crews as it has capos, and the
            // first of them is the name the drawer files it under.
            var leader = gang.Members.Count > 0 ? gang.Members[0].FullName : "";
            var capos = 0;
            foreach (var man in gang.Members)
                if (man.Lieutenant)
                    capos++;

            var raw = LedgerV2.PortraitPlate(card, FamilyCardW - 66f, -36f, 54f, 62f,
                InitialsOf(leader.Length > 0 ? leader : gang.Name), LedgerV2.Thumb);
            PortraitStudio.Request(
                PortraitStudio.FindPeoplePrefab(Gangs.GangCatalog.LieutenantModels[gang.Id]),
                PortraitStudio.Framing.Bust, raw);

            // LineBox, not 28: a truncating line in the condensed gothic vanishes
            // WHOLE when its rect cannot hold the face's line box, and this name did.
            var name = Line(card, LedgerStyle.Condensed, 22f, LedgerV2.Ink, pad, -36f,
                inner - 88f, LineBox(22f), gang.Name);
            name.characterSpacing = 1f;
            name.overflowMode = TextOverflowModes.Ellipsis;

            var runBy = Caps(card, pad, -72f, inner - 88f,
                leader.Length > 0 ? "RUN BY " + leader : "RUN BY PERSONS UNKNOWN", 9.5f,
                LedgerV2.Label, 2f);
            runBy.overflowMode = TextOverflowModes.Ellipsis;

            // ---- the three rows a card carries ----
            var current = outfit ? outfit.StanceWith(gang.Id) : Outfit.Stance.Peace;
            var pending = Outfit.Stance.Peace;
            var hasPending = outfit && outfit.TryGetPendingStance(gang.Id, out pending);

            CardRow(card, pad, -98f, inner, "STANDING",
                LedgerText.StanceLabel(current) + (hasPending
                    ? " → " + LedgerText.StanceLabel(pending) : ""),
                hasPending ? LedgerV2.Red : LedgerV2.Ink);

            // The meter is CENTRED on its label's line, not hung off the top of it -
            // hung off the top it climbed into the standing row above.
            var held = Outfit.Turf.CountOf(holdings, gang.Id);
            Caps(card, pad, -120f, 90f, "TURF", 9.5f, LedgerV2.Label, 3f);
            LedgerV2.Pips(card, pad + 90f, -128f, 10,
                Mathf.Clamp(Mathf.RoundToInt(10f * held / mostTurf), 0, 10),
                LedgerV2.Ink, 5f, 10f, 7f);
            Line(card, LedgerStyle.Mono, 12f, LedgerV2.Muted, pad + inner - 90f, -120f,
                90f, 18f, held.ToString(), TextAlignmentOptions.MidlineRight);

            CardRow(card, pad, -142f, inner, "CAPOS",
                capos > 0 ? capos.ToString() : "not known", LedgerV2.Ink);

            // FOLLOW-002. What this house has taken off us: the men who walked out of
            // our own book and through its door. Always printed, "nobody" and all, so
            // the card is one fixed grid rather than a layout that moves under the
            // reader when a lieutenant breaks.
            var taken = outfit ? outfit.Runner.MenLostTo(gang.Id) : 0;
            CardRow(card, pad, -164f, inner, "TAKEN",
                taken == 0 ? "nobody of ours"
                    : taken == 1 ? "one of our men"
                        : taken + " of our men",
                taken > 0 ? LedgerV2.Red : LedgerV2.Muted);

            // What the outfit kicks up to this house, and when. A house below the
            // outfit levies nothing, and the row says so rather than printing $0 -
            // "nothing" is the answer the player is working toward.
            var levy = outfit ? outfit.Tribute.For(gang.Id) : null;
            var today = outfit ? outfit.Campaign.Day : 1;
            var hourNow = cityClock ? cityClock.Hour : 0f;
            CardRow(card, pad, -186f, inner, "OWED",
                levy == null || levy.Amount <= 0
                    ? "nothing — you are not under them"
                    : LedgerText.Cash(levy.Amount) + " · " +
                      (levy.Overdue
                          ? "OVERDUE"
                          : LedgerText.DueIn(levy.DueDay, today, hourNow)),
                levy != null && levy.Amount > 0
                    ? (levy.Overdue ? LedgerV2.Red : LedgerV2.Ink)
                    : LedgerV2.Muted);

            // The door it operates behind, written in the margin in pen. The generated
            // city binds a business marker; the street city binds only the books -
            // either one names the door.
            var front = Gangs.GangRegistry.FrontBusinessOf(gang.Id);
            var books = Gangs.GangRegistry.FrontBooksOf(gang.Id);
            Rule(card, pad, -210f, inner, LedgerV2.Rule);
            var note = Paragraph(card, LedgerStyle.SerifItalic, 13.5f, LedgerV2.PaperBlue,
                pad, -218f, inner, 38f,
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
                var button = LedgerV2.Button(card, LedgerText.StanceLabel(choice),
                    pad + s * (buttonW + 4f), -256f, buttonW, 26f, () =>
                    {
                        if (outfit)
                            outfit.SetStance(gangId, choice);
                        dirty = true;
                    }, red: choice == Outfit.Stance.War, size: 10f,
                    outline: choice != effective);
            }
        }

        /// <summary>One line of a card's particulars: the label on the left, the answer
        /// held to the right margin over a dotted rule, and a hairline under the pair.
        /// The label takes only what it needs - "OWED" is four letters and the answer to
        /// it is a sentence, so a fixed ninety-unit label was eating the sentence.</summary>
        static void CardRow(Transform card, float x, float y, float w, string label,
            string value, Color ink)
        {
            const float labelW = 56f;
            LedgerV2.Mono(card, x, y, labelW, label, 9.5f, LedgerV2.Label, 6f);
            var text = Line(card, LedgerStyle.MonoBold, 12f, ink, x + labelW + 6f, y,
                w - labelW - 6f, LineBox(12f), value,
                TextAlignmentOptions.MidlineRight);
            text.overflowMode = TextOverflowModes.Ellipsis;
            LedgerV2.Leader(card, x, y - 17f, w);
        }

        /// <summary>The legend under the drawer - the page must never be the opaque
        /// system. Two columns so it takes a band and not a third of the sheet.</summary>
        void BuildStanceLegend()
        {
            var head = Line(diplomacyContent, LedgerStyle.Condensed, 14f, LedgerV2.Ink,
                PageLeft, LegendTop, PageWidth, LineBox(14f), "WHAT A STANCE DOES");
            head.characterSpacing = 7f;
            Block("Legend rule", diplomacyContent, PageLeft, LegendTop - 22f, PageWidth,
                1f, LedgerV2.SheetRule);

            var half = (PageWidth - 40f) * 0.5f;
            Paragraph(diplomacyContent, LedgerStyle.Mono, 12f, LedgerV2.Muted, PageLeft,
                LegendTop - 28f, half, 72f,
                LedgerText.StanceEffect(Outfit.Stance.Peace) + "\n" +
                LedgerText.StanceEffect(Outfit.Stance.Truce) + "\n" +
                LedgerText.StanceEffect(Outfit.Stance.War), lineSpacing: 3f);

            Paragraph(diplomacyContent, LedgerStyle.Mono, 12f, LedgerV2.Muted,
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
