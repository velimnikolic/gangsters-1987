using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using LivingCity.Outfit;
using static LivingCity.UI.LedgerKit;

namespace LivingCity.UI
{
    /// <summary>
    /// DIRECTION B - THE WAR ROOM. The same table, folded into a column.
    ///
    /// A dark rail of houses on the left, our own house at the head of it with a gold
    /// spine running down through every row, and each row joined to that spine by a
    /// connector drawn in ITS standing with us - the map of direction A, folded into
    /// 372 units. The rows are grouped by whether they need an answer today. Under them
    /// the same five-line legend, then the standings BETWEEN the other houses that we
    /// are not part of, then what we are made of.
    ///
    /// On the right, the open house on paper: the man, the reading, what they ask, our
    /// word with its shut column, and the record of everything that has passed between
    /// the two houses.
    ///
    /// Every figure comes from <see cref="HouseTable"/> and every key from the same
    /// form rows direction A uses, so nothing on this sheet can disagree with the map.
    /// </summary>
    public sealed partial class PersonnelAlmanac
    {
        const float RailWidth = 372f;
        const float HouseRailPad = 18f;
        const float RailGutter = 31f;
        const float RailRowH = 64f;

        /// <summary>Whether the shut column is showing its reasons.</summary>
        bool warWhyNot;

        void BuildWarRoom(IReadOnlyList<Gangs.Gang> gangs)
        {
            var world = Underworld.Current;
            var day = outfit ? outfit.Campaign.Day : 1;
            var mine = Gangs.GangCatalog.PlayerGangId;

            var rivals = new List<Gangs.Gang>();
            Gangs.Gang us = null;
            foreach (var gang in gangs)
            {
                if (gang.IsPlayer)
                    us = gang;
                else
                    rivals.Add(gang);
            }
            if (rivals.Count == 0)
                return;

            // The rail always has a house open: the first one that needs a word today.
            if (tableFor < 0 || !IsRival(gangs, tableFor))
                tableFor = PickTheLoudest(rivals, world, mine, day);

            var open = Find(rivals, tableFor);
            if (open == null)
                return;

            ReadTheHouse(open, world, mine, day, gangs);

            var railH = BuildRail(rivals, us, world, mine, day);
            var paneH = BuildWarPane(open, gangs);
            SizeFamiliesContent(Mathf.Max(railH, paneH) + 24f);
        }

        /// <summary>The house the sheet opens on: the one holding a word of ours, then
        /// the one at war, then whoever owes us. A war room that opens on a quiet house
        /// is a war room nobody reads.</summary>
        int PickTheLoudest(List<Gangs.Gang> rivals, Underworld world, int mine, int day)
        {
            var best = rivals[0].Id;
            var bestRank = -1;
            for (var i = 0; i < rivals.Count; i++)
            {
                HouseTable.Read(world, rivals[i], mine, day, holdings, PowerFigure,
                    tableCardRead);
                var rank = tableCardRead.TheyAsk ? 4
                    : tableCardRead.Tie == TieKind.War ? 3
                    : tableCardRead.Overdue ? 2
                    : tableCardRead.TheyOwe > 0 ? 1 : 0;
                if (rank <= bestRank)
                    continue;
                bestRank = rank;
                best = rivals[i].Id;
            }
            return best;
        }

        // -------------------------------------------------------------------- the rail

        float BuildRail(List<Gangs.Gang> rivals, Gangs.Gang us, Underworld world,
            int mine, int day)
        {
            var rail = NewRect("Rail", familiesContent);
            PlaceTopLeft(rail, PageLeft, 0f, RailWidth, StageH);
            Fill(rail, LedgerStyle.Rail);

            var inner = RailWidth - HouseRailPad * 2f;
            var y = -20f;

            var kicker = Caps(rail, HouseRailPad, y, inner, "THE TABLE · DAY " + day, 16.2f,
                LedgerStyle.RailKicker, 16f);
            y -= 24f;
            var sub = Caps(rail, HouseRailPad, y, inner,
                "EVERY LINE IS A STANDING · MIDNIGHT TO MIDNIGHT", 10.8f,
                LedgerStyle.RailNote, 9f);
            sub.font = LedgerStyle.Mono;
            y -= 32f;

            // ---- our own house, at the head of the spine ----
            var ours = NewRect("Ours", rail);
            PlaceTopLeft(ours, HouseRailPad, y, inner, 52f);
            Fill(ours, LedgerV2.At(LedgerStyle.RailGold, 0.09f));
            Frame(ours, 1f, LedgerStyle.RailGold);
            Block("Spine", ours, 0f, 0f, 4f, 52f, LedgerStyle.RailGold);
            var held = us != null ? Turf.CountOf(holdings, us.Id) : 0;
            var boss = director != null && director.Roster != null
                ? director.Roster.FindBoss() : null;
            var name = Line(ours, LedgerStyle.Condensed, 23.1f, LedgerV2.HeadCream, 16f,
                -9f, inner - 16f - 96f, 22f,
                us != null ? us.Name : Gangs.GangCatalog.Names[Gangs.GangCatalog.PlayerGangId]);
            name.characterSpacing = 2f;
            name.overflowMode = TextOverflowModes.Ellipsis;
            var line = Caps(ours, 16f, -33f, inner - 16f - 96f,
                "OURS · " + (boss != null ? boss.FullName : Gangs.GangCatalog.BossName) +
                " · " + held + (held == 1 ? " DOOR" : " DOORS"), 10.8f,
                LedgerStyle.RailLabel, 9f);
            line.font = LedgerStyle.Mono;
            line.overflowMode = TextOverflowModes.Ellipsis;
            var ourPower = us != null ? PowerFigure(us.Id) : -1;
            var word = Line(ours, LedgerStyle.MonoBold, 11.4f, LedgerStyle.RailSafeGold,
                inner - 96f, -9f, 84f, 18f, "OURS", TextAlignmentOptions.MidlineRight);
            word.characterSpacing = 10f;
            Line(ours, LedgerStyle.MonoBold, 16.2f, LedgerStyle.RailGold, inner - 96f,
                -30f, 84f, 20f, ourPower < 0 ? "?" : ourPower.ToString(),
                TextAlignmentOptions.MidlineRight);
            y -= 52f;

            // ---- the two groups ----
            var loud = new List<Gangs.Gang>();
            var quiet = new List<Gangs.Gang>();
            for (var i = 0; i < rivals.Count; i++)
            {
                HouseTable.Read(world, rivals[i], mine, day, holdings, PowerFigure,
                    tableCardRead);
                if (tableCardRead.TheyAsk || tableCardRead.Tie == TieKind.War ||
                    tableCardRead.Overdue)
                    loud.Add(rivals[i]);
                else
                    quiet.Add(rivals[i]);
            }

            y = RailGroup(rail, y, "NEEDS YOUR WORD", LedgerStyle.RailRed, loud.Count);
            for (var i = 0; i < loud.Count; i++)
                y = RailRow(rail, y, loud[i], world, mine, day);
            y = RailGroup(rail, y, "QUIET", LedgerStyle.RailLabel, quiet.Count);
            for (var i = 0; i < quiet.Count; i++)
                y = RailRow(rail, y, quiet[i], world, mine, day);

            // The spine runs on past the last row before it stops.
            Block("Spine tail", rail, HouseRailPad + 16f, y, 2f, 16f, LedgerStyle.RailGold);
            y -= 26f;

            // ---- the legend ----
            for (var i = 0; i < FamiliesLegend.Length; i++)
            {
                var entry = FamiliesLegend[i];
                TieRule(rail, HouseRailPad, y - 7f, 30f, entry.Kind);
                var label = Caps(rail, HouseRailPad + 39f, y - 14f, inner - 39f,
                    entry.Word + " · " + TieMeaning(entry.Kind), 10.8f,
                    LedgerStyle.RailLabel, 10f);
                label.font = LedgerStyle.Mono;
                label.overflowMode = TextOverflowModes.Ellipsis;
                y -= 22f;
            }
            y -= 16f;

            // ---- what stands between the others, which is not our business until it is ----
            var between = Caps(rail, HouseRailPad, y, inner, "BETWEEN THEM · NOT US", 10.8f,
                LedgerStyle.RailKicker, 14f);
            between.font = LedgerStyle.Mono;
            y -= 22f;
            Block("Between rule", rail, HouseRailPad, y, inner, 1f, LedgerStyle.RailHair);
            y -= 8f;

            var pairs = 0;
            for (var a = 0; a < rivals.Count && pairs < 6; a++)
            for (var b = a + 1; b < rivals.Count && pairs < 6; b++)
            {
                var tie = HouseTable.Between(world, rivals[a].Id, rivals[b].Id, day);
                if (tie.Kind == TieKind.Peace)
                    continue;
                pairs++;
                TieRule(rail, HouseRailPad, y - 16f, 34f, tie.Kind);
                var pair = Line(rail, LedgerStyle.Condensed, 17.4f, LedgerStyle.RailValue,
                    HouseRailPad + 44f, y, inner - 44f, 18f,
                    rivals[a].Name + " ↔ " + rivals[b].Name);
                pair.characterSpacing = 2f;
                pair.overflowMode = TextOverflowModes.Ellipsis;
                var what = Caps(rail, HouseRailPad + 44f, y - 20f, inner - 44f,
                    tie.What, 10.8f, TieTone(tie.Kind), 8f);
                what.font = LedgerStyle.Mono;
                what.overflowMode = TextOverflowModes.Ellipsis;
                Block("Hair", rail, HouseRailPad, y - 42f, inner, 1f, LedgerStyle.RailHair);
                y -= 48f;
            }
            if (pairs == 0)
            {
                Line(rail, LedgerStyle.SerifItalic, 12.3f, LedgerStyle.RailNote, HouseRailPad,
                    y, inner, 22f, "Nothing stands between them that we know of.");
                y -= 28f;
            }

            // ---- what we are made of ----
            y -= 16f;
            var men = director != null && director.Roster != null
                ? director.Roster.Members.Count : 0;
            var span = boss != null
                ? Personnel.Command.ManCap(boss, Personnel.OrganizationLimits.Default)
                : men;
            y -= LedgerV2.Meter(rail, HouseRailPad, y, inner, "MEN ON THE BOOKS", men,
                Mathf.Max(men, span), "man", "men", dark: true) + 14f;
            var city = Mathf.Max(1, holdings.Count);
            y -= LedgerV2.Meter(rail, HouseRailPad, y, inner, "DOORS HELD", held, city,
                "block", "blocks", dark: true) + 14f;

            var height = -y + 28f;
            rail.sizeDelta = new Vector2(RailWidth, Mathf.Max(StageH, height));
            return height;
        }

        static string TieMeaning(TieKind kind) => kind switch
        {
            TieKind.War => "ON SIGHT",
            TieKind.Truce => "TERRITORIAL",
            TieKind.Pact => "SWORN BOTH WAYS",
            TieKind.Tribute => "MONEY EVERY CYCLE",
            _ => "NO ENGAGEMENT",
        };

        float RailGroup(Transform rail, float y, string label, Color ink, int count)
        {
            Block("Spine", rail, HouseRailPad + 16f, y, 2f, 34f, LedgerStyle.RailGold);
            var head = Caps(rail, HouseRailPad + RailGutter + 14f, y - 12f,
                RailWidth - HouseRailPad * 2f - RailGutter - 14f,
                label + " · " + count, 10.8f, ink, 14f);
            head.font = LedgerStyle.Mono;
            return y - 34f;
        }

        /// <summary>
        /// One house on the rail: the connector drawn in its standing with us, its
        /// colour, its capo, its name with the line that matters, and the two figures.
        /// </summary>
        float RailRow(Transform rail, float y, Gangs.Gang gang, Underworld world,
            int mine, int day)
        {
            HouseTable.Read(world, gang, mine, day, holdings, PowerFigure, tableCardRead);
            var on = gang.Id == tableFor;

            var row = NewRect("House " + gang.Name, rail);
            PlaceTopLeft(row, HouseRailPad, y, RailWidth - HouseRailPad * 2f, RailRowH);
            var face = ClickSurface(row);
            face.color = on ? new Color(1f, 1f, 1f, 0.09f) : Color.clear;
            var houseId = gang.Id;
            RowButton(row, face, () => OpenTheCard(houseId));

            // The gutter: the gold spine, and the connector that joins this house to it.
            Block("Spine", row, 16f, 0f, 2f, RailRowH, LedgerStyle.RailGold);
            TieRule(row, 18f, -(RailRowH * 0.5f), RailGutter - 18f, tableCardRead.Tie);

            Block("Colour", row, RailGutter, 0f, 4f, RailRowH, GangPalette.Of(gang.Id));

            var mug = NewRect("Mug", row);
            PlaceTopLeft(mug, RailGutter + 4f, 0f, 64f, RailRowH);
            Fill(mug, LedgerStyle.RailTrough);
            mug.gameObject.AddComponent<RectMask2D>();
            var leader = gang.Members.Count > 0 ? gang.Members[0].FullName : gang.Name;
            var raw = LedgerV2.PortraitPlate(mug, -6f, 0f, 76f, RailRowH,
                InitialsOf(leader), LedgerStyle.RailTrough, LedgerStyle.RailLabel);
            PortraitStudio.Request(
                PortraitStudio.FindPeoplePrefab(Gangs.GangCatalog.LieutenantModels[gang.Id]),
                PortraitStudio.Framing.Bust, raw);

            var textX = RailGutter + 4f + 64f + 12f;
            var right = 96f;
            var textW = RailWidth - HouseRailPad * 2f - textX - right;

            var name = Line(row, LedgerStyle.Condensed, 23.1f, LedgerStyle.RailValue,
                textX, -10f, textW, 22f, gang.Name);
            name.characterSpacing = 2f;
            name.overflowMode = TextOverflowModes.Ellipsis;
            var flag = Caps(row, textX, -34f, textW, tableCardRead.Flag, 10.8f,
                LedgerStyle.RailLabel, 6f);
            flag.font = LedgerStyle.Mono;
            flag.overflowMode = TextOverflowModes.Ellipsis;

            var stance = Line(row, LedgerStyle.MonoBold, 12f, TieTone(tableCardRead.Tie),
                RailWidth - HouseRailPad * 2f - right, -10f, right - 12f, 18f,
                tableCardRead.Stance, TextAlignmentOptions.MidlineRight);
            stance.characterSpacing = 10f;
            Line(row, LedgerStyle.MonoBold, 15.6f,
                tableCardRead.Power < 0 ? LedgerStyle.RailNote : LedgerStyle.RailValue,
                RailWidth - HouseRailPad * 2f - right, -32f, right - 12f, 20f,
                tableCardRead.PowerText, TextAlignmentOptions.MidlineRight);

            Block("Hair", row, RailGutter, -(RailRowH - 1f),
                RailWidth - HouseRailPad * 2f - RailGutter, 1f, LedgerStyle.RailHair);
            return y - RailRowH;
        }

        // -------------------------------------------------------------------- the pane

        float BuildWarPane(Gangs.Gang house, IReadOnlyList<Gangs.Gang> gangs)
        {
            var left = PageLeft + RailWidth + 26f;
            var width = PageRight - left - 4f;
            var y = -22f;

            // ---- the head strip ----
            var name = Line(familiesContent, LedgerStyle.Condensed, 46.3f, LedgerV2.Ink,
                left, y, width * 0.5f, 46f, tableRead.Name.ToUpperInvariant());
            name.characterSpacing = 1f;
            name.overflowMode = TextOverflowModes.Ellipsis;

            var chipW = 26f + MonoWidth(tableRead.Stance, 10.5f, 6f);
            LedgerV2.Status(familiesContent, left + width * 0.5f - chipW - 240f, y - 16f,
                chipW, 20f, tableRead.Stance, TieTone(tableRead.Tie));
            var since = Caps(familiesContent, left + width * 0.5f - 234f, y - 16f, 230f,
                tableRead.StanceSince, 12f, LedgerV2.Muted, 6f);
            since.font = LedgerStyle.Mono;
            since.overflowMode = TextOverflowModes.Ellipsis;

            var who = Caps(familiesContent, left + width * 0.5f, y, width * 0.5f,
                tableRead.Code + " · RUN BY " + tableRead.Boss, 11.4f, LedgerV2.Label, 10f,
                TextAlignmentOptions.MidlineRight);
            who.font = LedgerStyle.Mono;
            who.overflowMode = TextOverflowModes.Ellipsis;
            var front = Line(familiesContent, LedgerStyle.SerifItalic, 13.3f,
                LedgerV2.Muted, left + width * 0.5f, y - 22f, width * 0.5f, 22f,
                "Front: " + tableRead.Front, TextAlignmentOptions.MidlineRight);
            front.overflowMode = TextOverflowModes.Ellipsis;

            y -= 56f;
            Block("Head rule", familiesContent, left, y, width, 3f, LedgerV2.Ink);
            y -= 20f;

            // ---- the man, and the two readings beside him ----
            const float manW = 300f;
            var colX = left + manW + 16f;
            var colW = width - manW - 16f;
            var manH = BuildTheManPanel(left, y, manW);
            var halfW = (colW - 16f) * 0.5f;
            var readingH = BuildReadingPanel(colX, y, halfW);
            var askH = BuildAskPanel(colX + halfW + 16f, y, halfW);
            var rowH = Mathf.Max(readingH, askH);

            var wordY = y - rowH - 16f;
            var wordH = BuildOurWordPanel(colX, wordY, colW, gangs);
            var recordY = wordY - wordH - 16f;
            var recordH = BuildRecordPanel(colX, recordY, colW);

            var rightBottom = -(recordY - recordH);
            var manBottom = -(y - Mathf.Max(manH, rowH + 16f + wordH + 16f + recordH));
            return Mathf.Max(rightBottom, manBottom) + 24f;
        }

        float BuildTheManPanel(float x, float y, float w)
        {
            var traitsH = 3f * 26f;
            var proseH = CopyBlock(tableRead.Personality, 13.3f, w - 32f, 2f);
            var mugH = w * 264f / 216f * 0.62f;
            var height = mugH + 14f + 30f + 20f + proseH + 16f + traitsH + 18f;

            var card = LedgerV2.Card("The man", familiesContent, x, y, w, height);
            var mug = NewRect("Mug", card);
            PlaceTopLeft(mug, 0f, 0f, w, mugH);
            Fill(mug, LedgerV2.PanelDark);
            mug.gameObject.AddComponent<RectMask2D>();
            var raw = LedgerV2.PortraitPlate(mug, 0f, 0f, w, mugH,
                InitialsOf(tableRead.Boss), LedgerV2.PanelDark);
            PortraitStudio.Request(
                PortraitStudio.FindPeoplePrefab(
                    Gangs.GangCatalog.LieutenantModels[tableRead.GangId]),
                PortraitStudio.Framing.Bust, raw);
            Block("Head rule", card, 0f, -mugH, w, 1f, LedgerV2.Ink);

            var inner = w - 32f;
            var yy = -(mugH + 14f);
            var boss = Line(card, LedgerStyle.Condensed, 27.8f, LedgerV2.Ink, 16f, yy,
                inner, 26f, tableRead.Boss.ToUpperInvariant());
            boss.characterSpacing = 2f;
            boss.overflowMode = TextOverflowModes.Ellipsis;
            yy -= 30f;
            var role = Caps(card, 16f, yy, inner, "BOSS · " + tableRead.Name, 10.8f,
                LedgerV2.Label, 10f);
            role.font = LedgerStyle.Mono;
            yy -= 20f;
            Paragraph(card, LedgerStyle.Serif, 13.3f, LedgerV2.Body, 16f, yy, inner,
                proseH, tableRead.Personality, lineSpacing: 3f);
            yy -= proseH + 8f;
            Rule(card, 16f, yy, inner, LedgerV2.Hair);
            yy -= 8f;
            yy = TraitRow(card, 16f, yy, inner, "TEMPER", tableRead.Temper);
            yy = TraitRow(card, 16f, yy, inner, "KEEPS HIS WORD", tableRead.KeepsHisWord);
            TraitRow(card, 16f, yy, inner, "FOUND MOST NIGHTS", tableRead.FoundAtNight);
            return height;
        }

        static float TraitRow(Transform card, float x, float y, float w, string label,
            string figure)
        {
            LedgerV2.Mono(card, x, y, w * 0.55f, label, 10.2f, LedgerV2.Label, 8f);
            var value = Line(card, LedgerStyle.MonoBold, 12f,
                figure == "unknown" ? LedgerV2.PaperBlue : LedgerV2.Ink, x, y, w,
                LineBox(12f), figure, TextAlignmentOptions.MidlineRight);
            value.overflowMode = TextOverflowModes.Ellipsis;
            LedgerV2.Leader(card, x, y - 18f, w);
            return y - 26f;
        }

        float BuildReadingPanel(float x, float y, float w)
        {
            var noteH = CopyBlock(tableRead.PowerNote, 12.3f, w - 32f, 2f);
            var height = 30f + 14f + 26f + noteH + 12f + 26f + 26f + 14f + 46f + 14f +
                         24f + 14f;
            var card = LedgerV2.Card("The reading", familiesContent, x, y, w, height);
            var top = LedgerV2.CardHead(card, w, "THE READING",
                tableRead.Power < 0 ? "NO EYES INSIDE" : "COUNTED");
            var inner = w - 32f;
            var yy = top - 8f;

            yy = TraitRow(card, 16f, yy, inner, "POWER", tableRead.PowerText);
            Paragraph(card, LedgerStyle.SerifItalic, 12.3f, LedgerV2.Muted, 16f, yy,
                inner, noteH, tableRead.PowerNote, lineSpacing: 2f);
            yy -= noteH + 10f;
            yy = TraitRow(card, 16f, yy, inner, "MEN OF OURS TAKEN", tableRead.TakenText);
            yy = TraitRow(card, 16f, yy, inner, "TRIBUTE", tableRead.OwedText);
            yy -= 6f;
            yy -= LedgerV2.Meter(card, 16f, yy, inner, "DOORS HELD", tableRead.Blocks,
                Mathf.Max(tableRead.Blocks, tableRead.BlocksTotal), "block", "blocks") + 12f;

            LedgerV2.Mono(card, 16f, yy, inner * 0.5f, "CAPOS UNDER HIM", 10.2f,
                LedgerV2.Label, 8f);
            if (tableRead.CaposKnown)
            {
                var pipsW = LedgerV2.PipsWidth(6);
                LedgerV2.Pips(card, 16f + inner - pipsW - 34f,
                    LedgerV2.MarkY(yy, 18f, 9f), 6, Mathf.Clamp(tableRead.Capos, 0, 6),
                    GangPalette.Of(tableRead.GangId));
                LedgerV2.Figure(card, 16f + inner - 30f, yy, 30f,
                    tableRead.Capos.ToString(), 13.8f);
            }
            else
                LedgerV2.Figure(card, 16f, yy, inner, "not counted", 12f,
                    LedgerV2.PaperBlue);
            return height;
        }

        float BuildAskPanel(float x, float y, float w)
        {
            var inner = w - 32f;
            var bodyH = tableRead.TheyAsk
                ? CopyBlock(tableRead.AskBody, 13.3f, inner, 2f)
                : 40f;
            var noteH = CopyBlock(tableRead.Note, 13.3f, inner, 2f);
            var height = 30f + 14f + (tableRead.TheyAsk ? 22f : 0f) + bodyH + 14f +
                         (tableRead.TheyAsk ? 26f : 0f) + 16f + noteH + 14f;

            var card = LedgerV2.Card("They ask", familiesContent, x, y, w, height);
            var top = LedgerV2.CardHead(card, w, "THEY ASK",
                tableRead.TheyAsk ? "WAITING ON US" : "NOTHING WAITING");
            var yy = top - 8f;

            if (tableRead.TheyAsk)
            {
                var when = Caps(card, 16f, yy, inner, tableRead.AskWhen, 10.8f,
                    LedgerV2.Red, 12f);
                when.font = LedgerStyle.Mono;
                when.overflowMode = TextOverflowModes.Ellipsis;
                yy -= 22f;
                Paragraph(card, LedgerStyle.Serif, 13.3f, LedgerV2.Body, 16f, yy, inner,
                    bodyH, tableRead.AskBody, lineSpacing: 3f);
                yy -= bodyH + 10f;
                var chipW = 26f + MonoWidth(tableRead.AskChip, 10.5f, 6f);
                LedgerV2.Status(card, 16f, yy, Mathf.Min(inner, chipW), 20f,
                    tableRead.AskChip, LedgerV2.Red);
                yy -= 26f;
            }
            else
            {
                Paragraph(card, LedgerStyle.SerifItalic, 13.3f, LedgerV2.Muted, 16f, yy,
                    inner, bodyH, "Nothing waiting. The last word out of this house was ours.",
                    lineSpacing: 3f);
                yy -= bodyH + 4f;
            }

            Rule(card, 16f, yy - 6f, inner, LedgerV2.Hair);
            Paragraph(card, LedgerStyle.Serif, 13.3f, LedgerV2.Muted, 16f, yy - 16f,
                inner, noteH, tableRead.Note, lineSpacing: 3f);
            return height;
        }

        /// <summary>
        /// OUR WORD: the keys grouped the way a boss thinks about them - talk, press,
        /// money, the table - with the shut column beside them and its "why not?" toggle.
        /// The form for the pressed key opens inside the panel.
        /// </summary>
        float BuildOurWordPanel(float x, float y, float w, IReadOnlyList<Gangs.Gang> gangs)
        {
            const float shutW = 264f;
            var keysW = w - shutW - 24f - 32f;
            var keyW = 152f;
            var perRow = Mathf.Max(1, Mathf.FloorToInt((keysW - 106f) / (keyW + 9f)));

            // What the keys come to, before the panel is made.
            var rows = 0;
            for (var g = 0; g < WarGroups.Length; g++)
            {
                var count = CountInGroup(WarGroups[g]);
                if (count > 0)
                    rows += Mathf.CeilToInt((float)count / perRow);
            }
            var keysH = Mathf.Max(1, rows) * 42f;

            var move = tableMove >= 0 && tableMove < tableOpen.Count
                ? tableOpen[tableMove] : null;
            var formH = move == null ? 0f : WarFormHeight(move, w - 32f);
            var shutH = warWhyNot ? ShutColumnHeight(shutW) : 60f;
            var height = 30f + 16f + Mathf.Max(keysH, shutH) + formH + 18f;

            var card = LedgerV2.Card("Our word", familiesContent, x, y, w, height);
            LedgerV2.CardHead(card, w, "OUR WORD",
                tableOpen.Count + " OPEN · " + tableShutList.Count + " SHUT");
            var yy = -30f - 14f;
            var top = yy;

            for (var g = 0; g < WarGroups.Length; g++)
            {
                var group = WarGroups[g];
                if (CountInGroup(group) == 0)
                    continue;
                LedgerV2.Mono(card, 16f, yy, 100f, group.ToUpperInvariant(), 10.8f,
                    LedgerV2.Label, 13f);
                var column = 0;
                for (var i = 0; i < tableOpen.Count; i++)
                {
                    if (tableOpen[i].Group != group)
                        continue;
                    var index = i;
                    var picked = tableMove == i;
                    var key = LedgerV2.Button(card, tableOpen[i].Label,
                        122f + column % perRow * (keyW + 9f),
                        yy - column / perRow * 42f, keyW, 32f,
                        () => PickTableMove(index),
                        picked ? LedgerV2.Key.Dark : FaceKey(tableOpen[i].Face), 10.5f);
                    key.overflowMode = TextOverflowModes.Ellipsis;
                    column++;
                }
                yy -= Mathf.CeilToInt((float)column / perRow) * 42f;
            }

            // ---- the shut column ----
            var shutX = w - shutW - 16f;
            VRule(card, shutX - 20f, top + 6f, Mathf.Max(keysH, shutH), LedgerV2.Hair);
            LedgerV2.Mono(card, shutX, top, shutW - 90f, "SHUT · " + tableShutList.Count,
                10.8f, LedgerV2.Label, 13f);
            var toggle = Line(card, LedgerStyle.Mono, 11.4f, LedgerV2.Red,
                shutX + shutW - 88f, top, 88f, 18f, warWhyNot ? "hide why" : "why not?",
                TextAlignmentOptions.MidlineRight);
            DottedRule(card, shutX + shutW - 88f, top - 16f, 88f, LedgerV2.Red);
            var hit = NewRect("Why", card);
            PlaceTopLeft(hit, shutX + shutW - 92f, top + 2f, 92f, 22f);
            RowButton(hit, ClickSurface(hit), () =>
            {
                warWhyNot = !warWhyNot;
                dirty = true;
            });

            var sy = top - 26f;
            if (!warWhyNot)
            {
                var words = "";
                for (var i = 0; i < tableShutList.Count; i++)
                    words += (i > 0 ? " · " : "") + tableShutList[i].Label;
                Paragraph(card, LedgerStyle.SerifItalic, 12.8f, LedgerV2.Muted, shutX, sy,
                    shutW, 60f, tableShutList.Count == 0
                        ? "Every word is open to this house today."
                        : words, lineSpacing: 2f);
            }
            else
                for (var i = 0; i < tableShutList.Count; i++)
                {
                    var shut = tableShutList[i];
                    var label = Caps(card, shutX, sy, shutW, shut.Label, 11.4f,
                        LedgerV2.Faint, 8f);
                    label.font = LedgerStyle.Mono;
                    var whyH = CopyBlock(shut.Why, 12.3f, shutW, 2f);
                    Paragraph(card, LedgerStyle.SerifItalic, 12.3f, LedgerV2.Muted, shutX,
                        sy - 20f, shutW, whyH, shut.Why, lineSpacing: 1f);
                    sy -= 22f + whyH + 6f;
                }

            // ---- the form ----
            if (move != null)
                BuildWarForm(card, 16f, top - Mathf.Max(keysH, shutH) - 4f, w - 32f, move,
                    gangs);
            return height;
        }

        static readonly string[] WarGroups = { "Talk", "Press", "Money", "The table" };

        int CountInGroup(string group)
        {
            var count = 0;
            for (var i = 0; i < tableOpen.Count; i++)
                if (tableOpen[i].Group == group)
                    count++;
            return count;
        }

        static LedgerV2.Key FaceKey(MoveFace face) => face switch
        {
            MoveFace.Dark => LedgerV2.Key.Dark,
            MoveFace.Red => LedgerV2.Key.Red,
            MoveFace.Ghost => LedgerV2.Key.Ghost,
            _ => LedgerV2.Key.Outline,
        };

        float ShutColumnHeight(float w)
        {
            var height = 0f;
            for (var i = 0; i < tableShutList.Count; i++)
                height += 22f + CopyBlock(tableShutList[i].Why, 12.3f, w, 2f) + 6f;
            return Mathf.Max(60f, height);
        }

        float WarFormHeight(TableMove move, float w)
        {
            var height = 30f + CopyBlock(move.Terms, 14.3f, w - 36f, 2f) + 16f;
            if (move.NeedsThird)
                height += 34f;
            if (move.NeedsStreet)
                height += 34f;
            if (move.NeedsMoney)
                height += 34f;
            if (move.AnswerTo < 0 && !move.War)
                height += 34f + (tableInPerson && tableEnvoys.Count > 0 ? 34f : 0f) + 32f;
            if (!string.IsNullOrEmpty(tableNote))
                height += 36f;
            return height + 32f + 32f;
        }

        void BuildWarForm(RectTransform card, float x, float y, float w, TableMove move,
            IReadOnlyList<Gangs.Gang> gangs)
        {
            var height = WarFormHeight(move, w);
            var form = NewRect("Form", card);
            PlaceTopLeft(form, x, y, w, height);
            Fill(form, LedgerV2.PanelBand);
            Frame(form, 1f, LedgerV2.Rule);
            Block("Spine", form, 0f, 0f, 3f, height, LedgerV2.Red);

            var head = Line(form, LedgerStyle.Condensed, 19.7f, LedgerV2.Ink, 18f, -12f,
                w - 36f, 22f, move.Head.ToUpperInvariant());
            head.characterSpacing = 4f;
            head.overflowMode = TextOverflowModes.Ellipsis;

            var termsH = CopyBlock(move.Terms, 14.3f, w - 36f, 2f);
            Paragraph(form, LedgerStyle.Serif, 14.3f, LedgerV2.Body, 18f, -40f, w - 36f,
                termsH, move.Terms, lineSpacing: 3f);
            var yy = -40f - termsH - 10f;

            const float labelW = 70f;
            var fieldX = 18f + labelW + 14f;
            var fieldW = Mathf.Min(420f, w - fieldX - 18f);

            if (move.NeedsThird)
            {
                CollectThirds(gangs, move.Word == ProposalKind.JoinWar);
                LedgerV2.Mono(form, 18f, yy, labelW, "AGAINST", 11.4f, LedgerV2.Label, 10f);
                yy = ThirdPicker(form, fieldX, yy, fieldW);
            }
            if (move.NeedsStreet)
            {
                LedgerV2.Mono(form, 18f, yy, labelW, "STREET", 11.4f, LedgerV2.Label, 10f);
                yy = StreetPicker(form, fieldX, yy, fieldW);
            }
            if (move.NeedsMoney)
            {
                LedgerV2.Mono(form, 18f, yy, labelW, "MONEY", 11.4f, LedgerV2.Label, 10f);
                yy = MoneyPicker(form, fieldX, yy, fieldW, move.MoneyCeiling);
            }
            if (move.AnswerTo < 0 && !move.War)
            {
                LedgerV2.Mono(form, 18f, yy, labelW, "CARRIED", 11.4f, LedgerV2.Label, 10f);
                yy = CarriedPicker(form, fieldX, yy, Mathf.Min(520f, w - fieldX - 18f));

                // THE RISK. A man sent into a house we are at war with is a man they can
                // keep, and the sheet says so before the key is pressed rather than in
                // the record afterwards.
                if (tableInPerson && tableEnvoys.Count > 0 &&
                    tableRead.Tie == TieKind.War)
                {
                    LedgerV2.Mono(form, 18f, yy, labelW, "THE RISK", 11.4f, LedgerV2.Red, 12f);
                    var risk = tableEnvoys[Mathf.Clamp(tableEnvoy, 0, tableEnvoys.Count - 1)];
                    Paragraph(form, LedgerStyle.Serif, 13.3f, LedgerV2.Red, fieldX, yy,
                        w - fieldX - 18f, 30f,
                        risk.FullName + " walks into a house we are at war with. If they " +
                        "want a second man of ours, we are handing them one.",
                        lineSpacing: 2f);
                    yy -= 32f;
                }
            }

            if (!string.IsNullOrEmpty(tableNote))
            {
                Paragraph(form, LedgerStyle.MonoItalic, 12f, LedgerV2.Red, 18f, yy,
                    w - 36f, 32f, "· " + tableNote, lineSpacing: 1f);
                yy -= 36f;
            }

            LedgerV2.Button(form, move.Send, 18f, yy, 160f, 32f, () => SayTheWord(move),
                move.SendIsRed ? LedgerV2.Key.Red : LedgerV2.Key.Dark, 10.5f);
            LedgerV2.Button(form, "NEVER MIND", 186f, yy, 140f, 32f,
                () => PickTableMove(-1), LedgerV2.Key.Ghost, 10.5f);
        }

        float BuildRecordPanel(float x, float y, float w)
        {
            var rows = Mathf.Max(1, tableRead.Record.Count);
            var height = 30f + 12f + rows * 30f + 14f;
            var card = LedgerV2.Card("The record", familiesContent, x, y, w, height,
                LedgerV2.PanelDark);
            LedgerV2.CardHead(card, w, "THE RECORD",
                tableRead.Record.Count + (tableRead.Record.Count == 1
                    ? " ENTRY" : " ENTRIES"));
            var yy = -30f - 10f;
            if (tableRead.Record.Count == 0)
            {
                Line(card, LedgerStyle.SerifItalic, 13.3f, LedgerV2.Muted, 16f, yy,
                    w - 32f, 24f, "Nothing has passed between the houses.");
                return height;
            }
            for (var i = 0; i < tableRead.Record.Count; i++)
            {
                var entry = tableRead.Record[i];
                var when = Caps(card, 16f, yy, 110f, entry.When, 11.4f,
                    entry.Fresh ? LedgerV2.Red : LedgerV2.Label, 6f);
                when.font = LedgerStyle.Mono;
                var what = Line(card, LedgerStyle.Type, 11.6f, LedgerV2.Body, 132f, yy,
                    w - 148f, 20f, entry.What);
                what.overflowMode = TextOverflowModes.Ellipsis;
                DottedRule(card, 16f, yy - 22f, w - 32f, LedgerV2.Dotted);
                yy -= 30f;
            }
            return height;
        }
    }
}
