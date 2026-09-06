using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static LivingCity.UI.LedgerKit;

namespace LivingCity.UI
{
    /// <summary>
    /// The ruled register itself, and the day rail beside it.
    ///
    /// The page is PRE-RULED - the column rules and the tail ruling run to the foot of
    /// the sheet whether or not there is anything on them - and every line, day band and
    /// run divider is a view out of a pool, bound to whatever item the scroll has brought
    /// into the window. Two thousand slips cost twenty-odd views.
    /// </summary>
    public sealed partial class WireSheet
    {
        RectTransform viewport, heldBand, railHost, railRun, emptyHost;
        TextMeshProUGUI heldWord, emptyCopy, emptyWord;
        readonly List<RectTransform> columnRules = new List<RectTransform>();
        readonly List<RowView> rowPool = new List<RowView>();
        readonly List<BandView> bandPool = new List<BandView>();
        readonly List<RunView> runPool = new List<RunView>();
        readonly List<Image> tailPool = new List<Image>();
        readonly List<TickView> tickPool = new List<TickView>();

        sealed class RowView
        {
            public RectTransform rect, pen;
            public Image face, edge, mark;
            public Image[] hollow;
            public TextMeshProUGUI day, tag, body, heat, money, source, file;
            public WireHit hit;
        }

        sealed class BandView
        {
            public RectTransform rect;
            public TextMeshProUGUI day, counts;
            public Image[] tally;
            public TextMeshProUGUI[] tallyCount;
        }

        sealed class RunView
        {
            public RectTransform rect;
            public TextMeshProUGUI label;
        }

        sealed class TickView
        {
            public RectTransform rect, bar, red;
            public Image face, barFace, redFace;
            public TextMeshProUGUI num;
            public WireHit hit;
        }

        // ---------------------------------------------------------------- the block

        void BuildRegister()
        {
            var y = RegisterTop;
            var head = NewRect("Column head", root);
            PlaceTopLeft(head, Pad, y, RegisterW, HeadBandH);
            Fill(head, LedgerV2.Head);
            var ink = LedgerV2.HeadCream;
            Cell(head, DayX + 12f, 0f, DayW - 12f, HeadBandH, "DAY", MonoPt(11f), ink, 13f);
            Cell(head, TagX + 12f, 0f, TagW - 12f, HeadBandH, "TAG", MonoPt(11f), ink, 13f);
            Cell(head, BodyX + 12f, 0f, BodyW - 24f, HeadBandH, "DISPATCH — AS FILED",
                MonoPt(11f), ink, 13f);
            Cell(head, HeatX, 0f, HeatW - 10f, HeadBandH, "HEAT", MonoPt(11f), ink, 13f,
                TextAlignmentOptions.MidlineRight);
            Cell(head, MoneyX, 0f, MoneyW - 10f, HeadBandH, "MONEY", MonoPt(11f), ink, 13f,
                TextAlignmentOptions.MidlineRight);
            Cell(head, SourceX + 12f, 0f, SourceW - 12f, HeadBandH, "SOURCE", MonoPt(11f),
                ink, 13f);
            Cell(head, FileX, 0f, FileW - 12f, HeadBandH, "FILE", MonoPt(11f), ink, 13f,
                TextAlignmentOptions.MidlineRight);

            heldBand = NewRect("Held", root);
            PlaceTopLeft(heldBand, Pad, y - HeadBandH, RegisterW, HeldH);
            Fill(heldBand, LedgerStyle.Ballpoint);
            heldWord = Cell(heldBand, 12f, 0f, RegisterW - 200f, HeldH, "", MonoPt(11f),
                LedgerV2.HeadCream, 12f);
            Cell(heldBand, RegisterW - 180f, 0f, 168f, HeldH, "TAKE THEM ↑",
                MonoPt(11f), LedgerV2.HeadInk, 12f, TextAlignmentOptions.MidlineRight);
            var heldHit = Hit(heldBand);
            heldHit.click = _ => TakeHeld(true);
            heldBand.gameObject.SetActive(false);

            viewport = NewRect("Register", root);
            PlaceTopLeft(viewport, Pad, y - HeadBandH, RegisterW, ViewportH);
            viewport.gameObject.AddComponent<RectMask2D>();
            columnRules.Clear();
            for (var i = 0; i < ColumnRules.Length; i++)
            {
                var rule = NewRect("Column rule", viewport);
                PlaceTopLeft(rule, ColumnRules[i], 0f, 1f, ViewportH);
                Fill(rule, LedgerV2.Hair);
                columnRules.Add(rule);
            }

            rowPool.Clear();
            bandPool.Clear();
            runPool.Clear();
            tailPool.Clear();
            // The ruling runs on under an empty page, so the words that say the page is
            // empty stand on their own patch of paper rather than across it.
            Rule(root, Pad, RegisterBottom, RegisterW, LedgerV2.SheetRule);
            emptyHost = NewRect("Nothing", viewport);
            PlaceTopLeft(emptyHost, 0f, -100f, RegisterW, 200f);
            emptyWord = Stamp(emptyHost, "NIL RETURN", (RegisterW - 220f) * 0.5f, 0f,
                220f, 62f).GetComponentInChildren<TextMeshProUGUI>();
            Block("Empty ground", emptyHost, (RegisterW - 720f) * 0.5f, -78f, 720f, 62f,
                LedgerV2.Panel);
            emptyCopy = Paragraph(emptyHost, LedgerStyle.Mono, MonoPt(13.5f),
                LedgerV2.Copy, (RegisterW - 700f) * 0.5f, -86f, 700f, 50f, "",
                lineSpacing: 4f);
            emptyCopy.alignment = TextAlignmentOptions.Top;
            emptyHost.gameObject.SetActive(false);
        }

        /// <summary>The notice band takes its 24 units out of the viewport rather than
        /// standing over a line: an entry hidden under a notice about entries is the one
        /// thing this page must never do.</summary>
        void PaintHeld()
        {
            if (!heldBand)
                return;
            var held = heldNew > 0;
            heldBand.gameObject.SetActive(held);
            if (held)
                heldWord.text = heldNew == 1
                    ? "1 NEW ENTRY HELD ABOVE — YOUR PLACE WAS KEPT"
                    : heldNew + " NEW ENTRIES HELD ABOVE — YOUR PLACE WAS KEPT";
            var top = RegisterTop - HeadBandH - (held ? HeldH : 0f);
            PlaceTopLeft(viewport, Pad, top, RegisterW, ViewportH);
            for (var i = 0; i < columnRules.Count; i++)
                columnRules[i].sizeDelta = new Vector2(1f, ViewportH);
        }

        // ---------------------------------------------------------------- the window

        /// <summary>Bind the window of the register the scroll has brought into view.
        /// Everything outside it keeps its view in the pool, switched off.</summary>
        void Lay()
        {
            if (!viewport)
                return;
            PaintHeld();

            var empty = register.Count == 0;
            emptyHost.gameObject.SetActive(empty);
            if (empty)
            {
                emptyWord.text = narrow.Narrowed ? "NO MATCH" : "NIL RETURN";
                emptyCopy.text = narrow.Narrowed
                    ? "No dispatches match this scope. Clear the scope to read the whole archive."
                    : "Nothing on the wire yet. New dispatches will appear here.";
            }

            var top = scroll - Window;
            var bottom = scroll + ViewportH + Window;
            int rows = 0, bands = 0, runs = 0, tails = 0;
            var items = register.Items;
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item.Y > bottom)
                    break;
                if (item.Y + item.H < top)
                    continue;
                var y = item.Y - scroll;
                switch (item.Kind)
                {
                    case WireItemKind.Line:
                        BindRow(rows++, y, item);
                        break;
                    case WireItemKind.Day:
                        BindBand(bands++, y, item);
                        break;
                    default:
                        BindRun(runs++, y, item);
                        break;
                }
            }

            // The page is ruled to its foot whether or not the archive reaches it.
            var ruled = Mathf.Max(0f, register.Height - scroll) + WireRegister.LineH;
            for (; ruled - 1f < ViewportH; ruled += WireRegister.LineH)
                Tail(tails++, ruled - 1f);

            for (var i = rows; i < rowPool.Count; i++)
                rowPool[i].rect.gameObject.SetActive(false);
            for (var i = bands; i < bandPool.Count; i++)
                bandPool[i].rect.gameObject.SetActive(false);
            for (var i = runs; i < runPool.Count; i++)
                runPool[i].rect.gameObject.SetActive(false);
            for (var i = tails; i < tailPool.Count; i++)
                tailPool[i].gameObject.SetActive(false);
        }

        void Tail(int slot, float y)
        {
            while (tailPool.Count <= slot)
            {
                var rule = NewRect("Tail rule", viewport);
                PlaceTopLeft(rule, 0f, 0f, RegisterW, 1f);
                tailPool.Add(Fill(rule, LedgerV2.Hair));
            }
            var image = tailPool[slot];
            image.gameObject.SetActive(true);
            ((RectTransform)image.transform).anchoredPosition = new Vector2(0f, -y);
        }

        // ------------------------------------------------------------------- a line

        void BindRow(int slot, float y, WireItem item)
        {
            while (rowPool.Count <= slot)
                rowPool.Add(NewRow());
            var view = rowPool[slot];
            var line = register.Kept[item.Index];
            var ink = line.Ink;
            var severe = line.Weight == WireWeight.Severe;
            var picked = item.Index == drawnIndex;
            var over = item.Index == hovered;

            view.rect.gameObject.SetActive(true);
            view.rect.anchoredPosition = new Vector2(0f, -y);
            view.hit.index = item.Index;

            view.face.color = picked ? LedgerV2.Picked
                : over ? Hovered
                : severe ? LedgerV2.PanelBand
                : item.Banded ? Banded : LedgerV2.At(LedgerV2.Panel, 0f);

            var edge = picked ? 4f : severe ? 3f : 0f;
            view.edge.gameObject.SetActive(edge > 0f);
            view.edge.color = ink;
            ((RectTransform)view.edge.transform).sizeDelta = new Vector2(edge, LineH);

            var mark = severe ? 13f : line.Weight == WireWeight.Notable ? 10f : 7f;
            PlaceTopLeft(view.pen, PenX + (PenW - mark) * 0.5f, -(LineH - mark) * 0.5f,
                mark, mark);
            var hollow = line.Weight == WireWeight.Routine;
            view.mark.color = hollow ? new Color(0f, 0f, 0f, 0f) : ink;
            for (var i = 0; i < view.hollow.Length; i++)
            {
                view.hollow[i].gameObject.SetActive(hollow);
                view.hollow[i].color = ink;
            }

            view.day.text = line.Stamp.StartsWith("DAY ")
                ? line.Stamp.Substring(4) : line.Stamp;
            view.day.color = LedgerV2.Copy;

            view.tag.text = line.Tag.ToUpperInvariant();
            view.tag.color = ink;
            view.tag.font = hollow ? LedgerStyle.Mono : LedgerStyle.MonoBold;

            view.body.text = line.Body;
            view.body.color = severe ? LedgerV2.Ink : LedgerV2.Copy;
            view.body.font = severe ? LedgerStyle.MonoBold : LedgerStyle.Mono;

            view.heat.text = line.Heat > 0 ? "+" + line.Heat + " HEAT" : "";
            view.heat.color = ink;

            view.money.text = line.Money > 0 ? LedgerText.Cash(line.Money) : "";
            var pen = WireRegister.PenOf(ink);
            view.money.color = pen == WirePen.Red || pen == WirePen.Amber
                ? ink : LedgerV2.Ink;

            view.source.text = item.Label;
            view.file.text = (picked || over ? "› " : "") + line.FileWord;
            view.file.color = picked || over ? LedgerV2.Ink : LedgerV2.Muted;
        }

        RowView NewRow()
        {
            var view = new RowView();
            view.rect = NewRect("Line", viewport);
            PlaceTopLeft(view.rect, 0f, 0f, RegisterW, WireRegister.LineH);

            var face = NewRect("Face", view.rect);
            PlaceTopLeft(face, 0f, 0f, RegisterW, LineH);
            view.face = Fill(face, LedgerV2.At(LedgerV2.Panel, 0f));
            Rule(view.rect, 0f, -LineH, RegisterW, LedgerV2.Hair);

            var edge = NewRect("Event ink", view.rect);
            PlaceTopLeft(edge, 0f, 0f, 3f, LineH);
            view.edge = Fill(edge, LedgerV2.Ink);

            view.pen = NewRect("Pen", view.rect);
            PlaceTopLeft(view.pen, PenX, 0f, 10f, 10f);
            view.mark = Fill(view.pen, LedgerV2.Ink);
            Frame(view.pen, 1f, LedgerV2.Ink);
            var edges = new List<Image>();
            foreach (Transform child in view.pen)
                if (child.name == "Edge")
                    edges.Add(child.GetComponent<Image>());
            view.hollow = edges.ToArray();

            view.day = Cell(view.rect, DayX + 12f, 0f, DayW - 12f, LineH, "",
                MonoPt(12f), LedgerV2.Copy, 3f);
            view.tag = Cell(view.rect, TagX + 12f, 0f, TagW - 18f, LineH, "",
                MonoPt(11f), LedgerV2.Ink, 6f, TextAlignmentOptions.MidlineLeft,
                LedgerStyle.MonoBold);
            view.body = Cell(view.rect, BodyX + 12f, 0f, BodyW - 24f, LineH, "",
                MonoPt(12f), LedgerV2.Copy);
            view.heat = Cell(view.rect, HeatX, 0f, HeatW - 10f, LineH, "", MonoPt(11.5f),
                LedgerV2.Ink, 4f, TextAlignmentOptions.MidlineRight, LedgerStyle.MonoBold);
            view.money = Cell(view.rect, MoneyX, 0f, MoneyW - 10f, LineH, "", MonoPt(12f),
                LedgerV2.Ink, 0f, TextAlignmentOptions.MidlineRight, LedgerStyle.MonoBold);
            view.source = Cell(view.rect, SourceX + 12f, 0f, SourceW - 14f, LineH, "",
                MonoPt(10.5f), LedgerV2.Muted, 5f);
            view.file = Cell(view.rect, FileX, 0f, FileW - 12f, LineH, "", MonoPt(10.5f),
                LedgerV2.Muted, 9f, TextAlignmentOptions.MidlineRight);

            view.hit = Hit(view.rect);
            view.hit.click = Draw;
            view.hit.enter = Hover;
            view.hit.exit = index => { if (hovered == index) Hover(-1); };
            return view;
        }

        // -------------------------------------------------------------- a day's band

        void BindBand(int slot, float y, WireItem item)
        {
            while (bandPool.Count <= slot)
                bandPool.Add(NewBand());
            var view = bandPool[slot];
            view.rect.gameObject.SetActive(true);
            view.rect.anchoredPosition = new Vector2(0f, -y);

            WireDay day = null;
            var days = register.Days;
            for (var i = 0; i < days.Count; i++)
                if (days[i].Day == item.Day)
                {
                    day = days[i];
                    break;
                }
            if (day == null)
                return;

            view.day.text = "DAY " + day.Day;
            view.counts.text = day.Counts;

            var pens = 0;
            for (var i = 0; i < 5; i++)
                if (day.Pens[i] > 0)
                    pens++;

            var shown = 0;
            for (var i = 0; i < 5; i++)
            {
                var count = day.Pens[i];
                var on = count > 0;
                view.tally[i].gameObject.SetActive(on);
                view.tallyCount[i].gameObject.SetActive(on);
                if (!on)
                    continue;
                var x = RegisterW - 14f - (pens - shown) * 44f;
                ((RectTransform)view.tally[i].transform).anchoredPosition =
                    new Vector2(x, -(WireRegister.DayH - 9f) * 0.5f);
                view.tally[i].color = WireRegister.InkOf((WirePen)i);
                view.tallyCount[i].rectTransform.anchoredPosition = new Vector2(x + 13f,
                    view.tallyCount[i].rectTransform.anchoredPosition.y);
                view.tallyCount[i].text = count.ToString();
                shown++;
            }
        }

        BandView NewBand()
        {
            var view = new BandView();
            view.rect = NewRect("Day band", viewport);
            PlaceTopLeft(view.rect, 0f, 0f, RegisterW, WireRegister.DayH);
            Fill(view.rect, LedgerV2.PanelDark);
            Block("Band top", view.rect, 0f, 0f, RegisterW, 3f, LedgerV2.Head);
            Rule(view.rect, 0f, -WireRegister.DayH, RegisterW, LedgerV2.SheetRule);

            view.day = Line(view.rect, LedgerStyle.Condensed, GothicPt(19f), LedgerV2.Ink,
                12f, -(WireRegister.DayH - LineBox(GothicPt(19f))) * 0.5f, DayW + 40f,
                LineBox(GothicPt(19f)), "");
            view.day.characterSpacing = 3f;
            view.counts = Cell(view.rect, TagX + 12f, 0f, 900f, WireRegister.DayH, "",
                MonoPt(11f), LedgerV2.Copy, 10f);

            view.tally = new Image[5];
            view.tallyCount = new TextMeshProUGUI[5];
            for (var i = 0; i < 5; i++)
            {
                view.tally[i] = Block("Pen tally", view.rect, 0f, 0f, 9f, 9f,
                    LedgerV2.Ink);
                view.tallyCount[i] = Cell(view.rect, 0f, 0f, 28f, WireRegister.DayH, "",
                    MonoPt(10.5f), LedgerV2.Copy, 4f);
            }
            return view;
        }

        // ------------------------------------------------------------ a run's divider

        void BindRun(int slot, float y, WireItem item)
        {
            while (runPool.Count <= slot)
                runPool.Add(NewRun());
            var view = runPool[slot];
            view.rect.gameObject.SetActive(true);
            view.rect.anchoredPosition = new Vector2(0f, -y);
            view.label.text = item.Label;
        }

        RunView NewRun()
        {
            var view = new RunView();
            view.rect = NewRect("Run", viewport);
            PlaceTopLeft(view.rect, 0f, 0f, RegisterW, WireRegister.RunH);
            Rule(view.rect, 0f, -WireRegister.RunH, RegisterW, LedgerV2.Hair);
            view.label = Cell(view.rect, TagX, 0f, 700f, WireRegister.RunH, "",
                MonoPt(10.5f), LedgerV2.Muted, 16f);
            return view;
        }

        // ----------------------------------------------------------------- the rail

        void BuildRail()
        {
            var x = Pad + RegisterW + RailGap;
            railHost = NewRect("Day rail", root);
            PlaceTopLeft(railHost, x, RegisterTop, RailW, RegisterH);
            var head = NewRect("Rail head", railHost);
            PlaceTopLeft(head, 0f, 0f, RailW, HeadBandH);
            Fill(head, LedgerV2.Head);
            Cell(head, 0f, 0f, RailW - 5f, HeadBandH, "DAYS", MonoPt(10f),
                LedgerV2.HeadInk, 10f, TextAlignmentOptions.MidlineRight);
            tickPool.Clear();
            railRun = NewRect("Rail run", railHost);
            PlaceTopLeft(railRun, 0f, -HeadBandH, RailW, RegisterH - HeadBandH);
            railRun.gameObject.AddComponent<RectMask2D>();
        }

        /// <summary>
        /// One tick a day, newest at the top, its bar as long as that day was busy and
        /// its head in the red pen for the share of it that was blood or hands laid on.
        /// The design's floor of 9 units a tick is a ceiling here as well: a campaign of
        /// four hundred days must still stand on one rail, so a long book prints thinner
        /// ticks rather than running off the foot of the page.
        /// </summary>
        void PaintRail()
        {
            if (!railRun)
                return;
            var days = register.Days;
            var runH = RegisterH - HeadBandH;
            railRun.sizeDelta = new Vector2(RailW, runH);
            var tickH = days.Count > 0
                ? Mathf.Min(22f, runH / days.Count) : 22f;
            var busiest = Mathf.Max(1, register.Busiest);
            var current = register.DayAt(scroll);

            var used = 0;
            for (var i = 0; i < days.Count; i++)
            {
                var day = days[i];
                var y = i * tickH;
                if (y > runH)
                    break;
                while (tickPool.Count <= used)
                    tickPool.Add(NewTick());
                var view = tickPool[used++];
                view.rect.gameObject.SetActive(true);
                PlaceTopLeft(view.rect, 0f, -y, RailW, tickH);
                view.hit.index = day.Day;

                var isolated = narrow.DayOnly == day.Day;
                view.face.color = day.Day == current ? LedgerV2.Picked
                    : day.Day == hoveredDay ? Hovered
                    : new Color(0f, 0f, 0f, 0f);
                view.num.text = day.Day % 5 == 0 || day.Day == current || isolated
                    ? day.Day.ToString() : "";
                view.num.rectTransform.sizeDelta = new Vector2(16f,
                    view.num.rectTransform.sizeDelta.y);

                var barH = Mathf.Max(3f, tickH - 4f);
                var wide = day.Count > 0
                    ? Mathf.Max(3f, Mathf.Round(14f * day.Count / busiest)) : 2f;
                var red = day.Count > 0
                    ? Mathf.Round(wide * day.Hard / day.Count) : 0f;
                PlaceTopLeft(view.bar, 18f, -(tickH - barH) * 0.5f, wide, barH);
                view.barFace.color = day.Count == 0 ? LedgerV2.Hair
                    : isolated ? LedgerV2.Ink : LedgerV2.SheetRule;
                PlaceTopLeft(view.red, 0f, 0f, red, barH);
                view.red.gameObject.SetActive(red > 0f);
                view.redFace.color = isolated ? LedgerV2.Ink : LedgerStyle.RedPen;
            }
            for (var i = used; i < tickPool.Count; i++)
                tickPool[i].rect.gameObject.SetActive(false);
        }

        TickView NewTick()
        {
            var view = new TickView();
            view.rect = NewRect("Day tick", railRun);
            PlaceTopLeft(view.rect, 0f, 0f, RailW, 12f);
            view.face = Fill(view.rect, new Color(0f, 0f, 0f, 0f));
            view.num = Cell(view.rect, 0f, 0f, 16f, 12f, "", MonoPt(9.5f),
                LedgerV2.Muted, 0f, TextAlignmentOptions.MidlineRight);
            view.bar = NewRect("Bar", view.rect);
            PlaceTopLeft(view.bar, 18f, 0f, 12f, 6f);
            view.barFace = Fill(view.bar, LedgerV2.SheetRule);
            view.red = NewRect("Hard", view.bar);
            PlaceTopLeft(view.red, 0f, 0f, 6f, 6f);
            view.redFace = Fill(view.red, LedgerStyle.RedPen);

            view.hit = Hit(view.rect);
            view.hit.click = JumpToDay;
            view.hit.doubleClick = IsolateDay;
            view.hit.enter = day => RailHover(day, true);
            view.hit.exit = day => RailHover(day, false);
            return view;
        }

        /// <summary>The tick under the pointer: tinted, and what it holds printed beside
        /// it.</summary>
        void RailHover(int day, bool over)
        {
            hoveredDay = over ? day : -1;
            if (over)
                RailTip(day);
            else
                HideTip();
            PaintRail();
        }

        /// <summary>What the rail says about a day, printed to the LEFT of it at the
        /// tick's own height - the rail is 34 units wide and the sheet ends at its
        /// right edge.</summary>
        void RailTip(int day)
        {
            var runH = RegisterH - HeadBandH;
            var tickH = register.Days.Count > 0
                ? Mathf.Min(22f, runH / register.Days.Count) : 22f;
            ShowTip("DAY " + day + " · " + DayCount(day) + " IN SCOPE",
                Pad + RegisterW - 200f,
                RegisterTop - HeadBandH - RailIndex(day) * tickH);
        }

        int DayCount(int day)
        {
            var days = register.Days;
            for (var i = 0; i < days.Count; i++)
                if (days[i].Day == day)
                    return days[i].Count;
            return 0;
        }

        int RailIndex(int day)
        {
            var days = register.Days;
            for (var i = 0; i < days.Count; i++)
                if (days[i].Day == day)
                    return i;
            return 0;
        }
    }
}
