using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using LivingCity.Personnel;
using LivingCity.Police;
using static LivingCity.UI.LedgerKit;

namespace LivingCity.UI
{
    /// <summary>
    /// THE LAW — the ninth sheet of the book (GAN-302), redrawn as a mind map.
    ///
    /// What the state has against the outfit was scattered over the men's own files: a
    /// held man's case on HIS page, the lawyer's record on HIS, a wanted man's grade
    /// nowhere at all, and a case that had closed only as a line of prose on a rap
    /// sheet. This sheet is the one place it is all read at once.
    ///
    /// THE REDESIGN. The first cut put four scrolling fields on one page, which buried
    /// the soonest court day and spent a third of the width on three starved panes. The
    /// sheet now carries ONE CASE AT A TIME AS A MAP: the charge in the middle, the men
    /// named branching left, the witnesses branching right, counsel's read hanging
    /// below. Every node starts folded to a line of identity and opens at a click into
    /// its description and the keys that belong to it - so the three operations only
    /// ever appear on the thing the player picked. The open cases are a tab strip across
    /// the head, the house is a rail down the right, and the closed cases are a drawer
    /// along the foot.
    ///
    /// The page still holds NO state the model does not. Every row comes from
    /// <see cref="LawSheet.Collect"/> at paint, and the only writes are the three
    /// operations the man's own file already offers, through the same
    /// <see cref="RoadDemo.LawDesk"/>. What it does hold is VIEW state - which file is
    /// open, which man is in hand, which nodes are unfolded and where each of its four
    /// wheel fields is standing - and every one of those is thrown away freely.
    /// </summary>
    public sealed partial class PersonnelAlmanac
    {
        // ------------------------------------------------------- design px to a size
        //
        // The handoff is written in CSS px of the design system's own faces, and this
        // book's point sizes were written against the faces LedgerStyle replaced. The
        // conversion is one division, and it lives in these three lines so no number
        // below has to carry it. The book's own lift for small print (LedgerKit
        // BookSize) then applies on top, exactly as it does on every other sheet.

        static float LawMonoAt(float px) =>
            LedgerStyle.FromPx(px, LedgerStyle.MonoOptical);

        static float LawCondAt(float px) =>
            LedgerStyle.FromPx(px, LedgerStyle.CondensedOptical);

        static float LawSerifAt(float px) =>
            LedgerStyle.FromPx(px, LedgerStyle.SerifOptical);

        // What one line of each face ADVANCES, so a node can be measured before it is
        // drawn. The figures are the ones the rail was cut to: IBM Plex prints a line
        // box of 1.080 x its point size, Oswald 1.281. PT Serif is set to the design's
        // own 1.45 leading. LineBox is deliberately not used here - it is the height a
        // rect needs so TMP does not DROP the line, which is a third again what the
        // face actually prints, and a map measured on it stands half empty.

        static float LawMonoRun(float px) => BookSize(LawMonoAt(px)) * 1.080f + 3f;

        static float LawCondRun(float px) => BookSize(LawCondAt(px)) * 1.281f + 2f;

        static float LawSerifRun(float px, int lines = 1) =>
            BookSize(LawSerifAt(px)) * 1.45f * lines + 3f;

        // ------------------------------------------------------------- the fixture

        /// <summary>What <see cref="LedgerV2.PageHead"/> takes off the top of a page -
        /// the title, the line under it and the heavy rule that closes the pair.</summary>
        const float LawHeadDrop = 72f;

        /// <summary>Between the four bands of the sheet.</summary>
        const float LawBandGap = 10f;

        /// <summary>The OPEN FILES strip and the CLOSED FILES drawer.</summary>
        const float LawStripH = 74f;

        /// <summary>The desk line at the foot: what the desk last did, or refused.</summary>
        const float LawDeskH = 22f;

        /// <summary>THE HOUSE rail beside the map, and the air between them.</summary>
        const float LawRailW = 320f;
        const float LawBodyGap = 14f;

        /// <summary>A panel's dark head band, and the air inside a panel of TYPE. The
        /// map takes its panel whole - see the note where its window is built.</summary>
        const float LawPanelHead = 30f;
        const float LawPanelPadX = 16f;
        const float LawPanelPadY = 14f;

        /// <summary>The dark label cell that opens each strip, and the width of one
        /// tab in each of them.</summary>
        const float LawLabelW = 120f;
        const float LawTabW = 196f;
        const float LawFileW = 186f;

        static float LawTop, LawBodyY, LawBodyH, LawDrawerY, LawDeskY,
            LawMapW, LawRailX;

        /// <summary>Measured against the fixture the book was actually laid out at -
        /// never guessed, because the sheet is re-laid whenever the window moves. Every
        /// band takes its stated height and the MAP takes what is left, which is the
        /// only way the design's 640 becomes a real number on a frame that is not
        /// exactly the one it was drawn on.</summary>
        static void MeasureLaw()
        {
            LawTop = PageTop - LawHeadDrop;

            var run = LawTop - PageBottom;
            LawBodyY = LawTop - LawStripH - LawBandGap;
            LawBodyH = Mathf.Max(180f,
                run - (LawStripH * 2f + LawDeskH + LawBandGap * 3f));
            LawDrawerY = LawBodyY - LawBodyH - LawBandGap;
            LawDeskY = LawDrawerY - LawStripH - LawBandGap;

            LawMapW = Mathf.Max(320f, PageWidth - LawRailW - LawBodyGap);
            LawRailX = PageLeft + LawMapW + LawBodyGap;
        }

        RectTransform lawFixed;
        RectTransform lawStripViewport, lawStripContent;
        RectTransform lawMapViewport, lawMapContent;
        RectTransform lawRailViewport, lawRailContent;
        RectTransform lawDrawerViewport, lawDrawerContent;
        LedgerLinks lawLinks;

        readonly LawSheetRows lawRows = new LawSheetRows();

        // -------------------------------------------------------------- the view state
        //
        // None of this is the world's. It is which file the strip has open, which man is
        // in hand, which nodes are unfolded and where each wheel field stands - cleared
        // freely, and re-derived from nothing when the sheet is next opened.

        /// <summary>The open file the strip has selected, by case id; -1 takes the
        /// first, which is the soonest court day.</summary>
        int lawOpenCaseId = -1;

        /// <summary>THE MAN IN HAND - whose link to the case is the red one.</summary>
        int lawSelectedManId = -1;

        /// <summary>
        /// AND THE ONE WHOSE FILE STANDS OPEN. One at a time, deliberately: an open man
        /// carries POST BAIL, SKIP BAIL and CUT HIM LOOSE, and three men open at once
        /// puts nine irreversible keys on a map where only one link is red. The whole
        /// argument of the redesign is that the keys sit on the man the player picked
        /// and nowhere else, so picking another man folds the last one.
        /// </summary>
        int lawOpenManId = -1;

        /// <summary>Whether the complaint node - the one a case with nobody taken shows
        /// in the men's column - stands unfolded. It has no keys and no man behind it,
        /// so it is a flag rather than an id.</summary>
        bool lawNobodyOpen;

        /// <summary>Which of the OTHER nodes stand unfolded: the case itself ("case")
        /// and a witness's seat on his file.</summary>
        readonly HashSet<string> lawExpanded = new HashSet<string>();

        /// <summary>
        /// A WHEEL POSITION PER REGION. Four windows on one sheet, and a shared offset
        /// makes them fight: scroll a long map, put the pointer on a short rail, and the
        /// clamp for the short one drags the long one back to the top. The ORDERS page
        /// shared the same field and inherited whatever the law sheet was last left at.
        /// </summary>
        Vector2 lawMapScroll;
        float lawStripScroll, lawRailScroll, lawDrawerScroll;

        /// <summary>Set when the map must come back to the middle of its stage - on the
        /// first paint of the sheet and whenever another file is picked.</summary>
        bool lawCentreMap = true;

        /// <summary>What the desk last did, and how it reads.</summary>
        string lawDeskLine = "";
        Color lawDeskTone = LedgerV2.Faint;

        // ------------------------------------------------------------- the building

        void BuildLawPage(RectTransform sheet)
        {
            var root = NewPageRoot(sheet, LedgerPage.Law);
            MeasureLaw();

            // The two strips are bands of panel stock under the design's drop shadow,
            // opened by a dark label cell; what scrolls in each is the run of tabs
            // beside it, and it scrolls SIDEWAYS.
            var strip = LedgerV2.Card("Law open files", root, PageLeft, LawTop,
                PageWidth, LawStripH);
            lawStripViewport = LawWindow(strip, "Open files", LawLabelW, 0f,
                PageWidth - LawLabelW, LawStripH, out lawStripContent);

            var drawer = LedgerV2.Card("Law closed files", root, PageLeft, LawDrawerY,
                PageWidth, LawStripH);
            lawDrawerViewport = LawWindow(drawer, "Closed files", LawLabelW, 0f,
                PageWidth - LawLabelW, LawStripH, out lawDrawerContent);

            // THE MAP TAKES THE WHOLE PANEL under its head band. Every other panel in
            // the book insets its run by the design's 14/16, and the rail beside this
            // one still does - but the map's run is a DARK STAGE, not type on paper, and
            // a strip of panel stock around it reads as a mount rather than as the
            // panel's own inside (the user's ruling, 2026-09-06).
            var map = LedgerV2.Card("Law map", root, PageLeft, LawBodyY, LawMapW,
                LawBodyH);
            lawMapViewport = LawWindow(map, "Map", 0f, -LawPanelHead, LawMapW,
                LawBodyH - LawPanelHead, out lawMapContent);

            // The links go UNDER every node: they are drawn first, they never take a
            // click, and they are one mesh however many of them there are.
            var links = NewRect("Law links", lawMapContent);
            Stretch(links);
            // THE RENDERER FIRST, BY HAND. Graphic's own [RequireComponent] is not
            // applied to a subclass added from script: the component lands with no
            // CanvasRenderer, and the first time this page root is hidden
            // MaskableGraphic.OnDisable dereferences it and takes SetPage down - inside
            // BuildBook, before the chrome exists, so the book comes up frozen with the
            // world paused behind it and no key that closes anything.
            links.gameObject.AddComponent<CanvasRenderer>();
            lawLinks = links.gameObject.AddComponent<LedgerLinks>();

            var rail = LedgerV2.Card("Law house", root, LawRailX, LawBodyY, LawRailW,
                LawBodyH);
            lawRailViewport = LawWindow(rail, "House", LawPanelPadX,
                -(LawPanelHead + LawPanelPadY),
                LawRailW - LawPanelPadX * 2f,
                LawBodyH - LawPanelHead - LawPanelPadY * 2f, out lawRailContent);

            // LAST, so it draws OVER the four panels: the page head, the two dark head
            // bands, the strips' label cells and the desk line are all repainted from
            // scratch every pass, and a fixed layer built before the panels would have
            // put every one of them behind a panel face.
            lawFixed = NewRect("Law Fixed", root);
            Stretch(lawFixed);
        }

        /// <summary>A window on a run bigger than itself. The content rect is sized by
        /// whoever fills it - <see cref="LawSettleDown"/> and its two neighbours - and
        /// pivots at its top-left, so both axes are one subtraction.</summary>
        RectTransform LawWindow(RectTransform parent, string name, float x, float y,
            float w, float h, out RectTransform content)
        {
            var viewport = NewRect("Law " + name + " Window", parent);
            PlaceTopLeft(viewport, x, y, w, h);
            viewport.gameObject.AddComponent<RectMask2D>();

            content = NewRect("Law " + name, viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(0f, 1f);
            content.pivot = new Vector2(0f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(w, h);
            return viewport;
        }

        // ------------------------------------------------------------- the painting

        void RebuildLaw()
        {
            if (!lawFixed || !lawMapContent)
                return;

            MeasureLaw();
            foreach (Transform old in lawFixed) Destroy(old.gameObject);
            foreach (Transform old in lawStripContent) Destroy(old.gameObject);
            foreach (Transform old in lawRailContent) Destroy(old.gameObject);
            foreach (Transform old in lawDrawerContent) Destroy(old.gameObject);
            for (var i = lawMapContent.childCount - 1; i >= 0; i--)
            {
                var child = lawMapContent.GetChild(i);
                // The link layer is built once and refilled: it is a mesh, not a run of
                // rects, and destroying it would take the component with it.
                if (lawLinks && child == lawLinks.transform) continue;
                Destroy(child.gameObject);
            }

            CollectLaw();

            var file = LawOpenFileRow();
            LedgerV2.PageHead(lawFixed, PageLeft, PageTop, PageWidth, "THE LAW",
                LawHeadLine());

            LawStripLabel("OPEN FILES", LawStripCount(), PageLeft, LawTop);
            LawStripLabel("CLOSED FILES · THE DRAWER",
                lawRows.Archive.Count + (lawRows.Archive.Count == 1 ? " file" : " files"),
                PageLeft, LawDrawerY);

            LawPanelHeadBand(PageLeft, LawBodyY, LawMapW,
                "THE STATE'S CASE  ·  THE MAP", LawMapHeadRight(file));
            LawPanelHeadBand(LawRailX, LawBodyY, LawRailW, "THE HOUSE",
                LawRailHeadRight());

            PaintLawStrip();
            PaintLawMap(file);
            PaintLawHouse();
            PaintLawDrawer();
            PaintLawDeskLine();
        }

        /// <summary>
        /// Every row, derived at this moment. The complainant's nerve comes from the
        /// PIPELINE'S own gate rather than a fear figure read here: a Connected owner
        /// turns up whatever the street has done to him, and a sheet that guessed would
        /// tell the boss his shopkeeper was frightened off while the man was putting the
        /// crew away.
        /// </summary>
        void CollectLaw()
        {
            var pipeline = RoadDemo.LawDesk.Pipeline;
            var roster = director != null ? director.Roster : null;
            var counsel = Lawyer.Counsel(roster);
            LawSheet.Collect(
                pipeline, roster, Gameplay.PlayerCommands.House.Value,
                outfit ? outfit.Campaign.Day : 0,
                counsel == null ? 0 : Lawyer.Skill(counsel),
                pipeline != null ? pipeline.ComplainantStillTalks : null,
                lawRows);
        }

        // ---------------------------------------------------------- what is on the head

        string LawHeadLine()
        {
            var named = 0;
            for (var i = 0; i < lawRows.Docket.Count; i++)
                named += lawRows.Docket[i].Defendants.Count;
            var day = outfit ? outfit.Campaign.Day : 0;
            return "DAY " + day + "  ·  " + lawRows.Docket.Count +
                   (lawRows.Docket.Count == 1 ? " open case" : " open cases") +
                   "  ·  " + named + (named == 1 ? " man named" : " men named") +
                   "  ·  " + lawRows.Inside.Count + " inside  ·  " +
                   lawRows.Wanted.Count + " wanted";
        }

        string LawStripCount()
        {
            var named = 0;
            for (var i = 0; i < lawRows.Docket.Count; i++)
                named += lawRows.Docket[i].Defendants.Count;
            return lawRows.Docket.Count + " open  ·  " + named +
                   (named == 1 ? " man" : " men");
        }

        string LawRailHeadRight() =>
            lawRows.Inside.Count + " inside  ·  " + lawRows.Wanted.Count + " wanted";

        string LawMapHeadRight(DocketRow file)
        {
            if (file == null)
                return "";
            var men = file.Defendants.Count;
            return (file.NobodyTaken || file.CourtDay <= 0
                       ? "NOBODY TAKEN"
                       : "COURT DAY " + file.CourtDay) +
                   "  ·  " + (men == 0 ? "no" : men.ToString()) +
                   (men == 1 ? " man" : " men");
        }

        /// <summary>The file the strip has open: the one the player picked, or the
        /// soonest, which is the first row the collector sorted.</summary>
        DocketRow LawOpenFileRow()
        {
            if (lawRows.Docket.Count == 0)
                return null;
            for (var i = 0; i < lawRows.Docket.Count; i++)
                if (lawRows.Docket[i].File != null &&
                    lawRows.Docket[i].File.CaseId == lawOpenCaseId)
                    return lawRows.Docket[i];
            return lawRows.Docket[0];
        }

        // --------------------------------------------------------- the OPEN FILES strip

        /// <summary>The dark cell that opens a strip: what the strip is, over what it
        /// holds. It is drawn on the fixed layer, not in the run, so it does not scroll
        /// away with the tabs.</summary>
        void LawStripLabel(string word, string count, float x, float y)
        {
            var cell = NewRect("Law strip label", lawFixed);
            PlaceTopLeft(cell, x, y, LawLabelW, LawStripH);
            Fill(cell, LedgerV2.Head);

            var label = LedgerV2.Mono(cell, 12f, -22f, LawLabelW - 24f, word,
                LawMonoAt(8f), LedgerV2.HeadInk, 4f);
            label.font = LedgerStyle.MonoBold;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Truncate;
            label.rectTransform.sizeDelta =
                new Vector2(LawLabelW - 24f, LawMonoRun(8f) * 2f);

            LedgerV2.Mono(cell, 12f, -(22f + LawMonoRun(8f) * 2f), LawLabelW - 24f,
                count, LawMonoAt(7.5f), LedgerV2.HeadDim, 2.5f);
        }

        /// <summary>
        /// A panel's dark head band: what the panel is on the left, what it holds on the
        /// right. It is <see cref="LedgerV2.CardHead"/>'s drawing at the design's own
        /// two sizes, laid on the FIXED layer over the panel rather than inside it - the
        /// panels are built once and only their runs are cleared, so a head painted into
        /// a card would stack another band on it at every repaint.
        /// </summary>
        void LawPanelHeadBand(float x, float y, float w, string label, string right)
        {
            var band = NewRect("Law panel head", lawFixed);
            PlaceTopLeft(band, x, y, w, LawPanelHead);
            Fill(band, LedgerV2.Head);

            var text = LedgerV2.Mono(band, 16f, -(LawPanelHead - LawMonoRun(8.5f)) * 0.5f,
                w * 0.6f, label, LawMonoAt(8.5f), LedgerV2.HeadInk, 5f);
            text.font = LedgerStyle.MonoBold;
            text.overflowMode = TextOverflowModes.Ellipsis;

            if (string.IsNullOrEmpty(right))
                return;
            var note = LedgerV2.Mono(band, w - 16f - w * 0.38f,
                -(LawPanelHead - LawMonoRun(8f)) * 0.5f, w * 0.38f, right,
                LawMonoAt(8f), LedgerV2.HeadDim, 3f, TextAlignmentOptions.MidlineRight);
            note.overflowMode = TextOverflowModes.Ellipsis;
        }

        void PaintLawStrip()
        {
            if (lawRows.Docket.Count == 0)
            {
                LawStripEmpty(lawStripContent, "NO CASE AGAINST US");
                LawSettleAcross(lawStripContent, lawStripViewport, LawTabW,
                    ref lawStripScroll);
                return;
            }

            var open = LawOpenFileRow();
            var x = 0f;
            for (var i = 0; i < lawRows.Docket.Count; i++)
            {
                var row = lawRows.Docket[i];
                var picked = row == open;
                var tab = NewRect("File " + row.File.CaseId, lawStripContent);
                PlaceTopLeft(tab, x, 0f, LawTabW, LawStripH);
                Fill(tab, picked ? LedgerV2.Picked : LedgerV2.PanelBand);

                // The picked tab wears a red edge along its top - the one thing on the
                // strip that says WHICH of them the map below is drawing.
                if (picked)
                    Block("Edge", tab, 0f, 0f, LawTabW, 4f, LedgerV2.Red);
                Block("Seam", tab, LawTabW - 1f, 0f, 1f, LawStripH, LedgerV2.Hair);

                var tone = LawCourtTone(row);
                var y = -10f;
                var stamp = LedgerV2.Mono(tab, 12f, y, LawTabW - 24f,
                    row.NobodyTaken || row.CourtDay <= 0
                        ? "NOT LISTED"
                        : "DAY " + row.CourtDay,
                    LawMonoAt(9f), tone, 3f);
                stamp.font = LedgerStyle.MonoBold;
                y -= LawMonoRun(9f);

                var charge = Line(tab, LedgerStyle.Condensed, LawCondAt(14f),
                    LedgerV2.Ink, 12f, y, LawTabW - 24f, LawCondRun(14f),
                    row.Charge.ToUpperInvariant());
                charge.characterSpacing = 1f;
                charge.overflowMode = TextOverflowModes.Ellipsis;
                y -= LawCondRun(14f);

                LedgerV2.Mono(tab, 12f, y, LawTabW - 24f,
                    row.Where.ToUpperInvariant(), LawMonoAt(8f), LedgerV2.Muted, 2.5f);

                var caseId = row.File.CaseId;
                RowButton(tab, ClickSurface(tab), () => LawOpenCase(caseId));
                x += LawTabW;
            }

            LawSettleAcross(lawStripContent, lawStripViewport, x, ref lawStripScroll);
        }

        /// <summary>Picking a file selects it, folds every node the last one had open
        /// and brings the map back to the middle. A map left scrolled where the last
        /// case's men stood is a map that opens on empty stage.</summary>
        void LawOpenCase(int caseId)
        {
            if (lawOpenCaseId != caseId)
            {
                lawOpenCaseId = caseId;
                lawExpanded.Clear();
                lawSelectedManId = -1;
                lawOpenManId = -1;
                lawNobodyOpen = false;
                lawCentreMap = true;
            }
            dirty = true;
        }

        // ------------------------------------------------------- the CLOSED FILES drawer

        void PaintLawDrawer()
        {
            if (lawRows.Archive.Count == 0)
            {
                LawStripEmpty(lawDrawerContent, "NOTHING HAS COME TO COURT");
                LawSettleAcross(lawDrawerContent, lawDrawerViewport, LawFileW,
                    ref lawDrawerScroll);
                return;
            }

            var x = 0f;
            for (var i = 0; i < lawRows.Archive.Count; i++)
            {
                var row = lawRows.Archive[i];
                var file = NewRect("Closed " + row.File.CaseId, lawDrawerContent);
                PlaceTopLeft(file, x, 0f, LawFileW, LawStripH);
                Fill(file, LedgerV2.Panel);
                Block("Seam", file, LawFileW - 1f, 0f, 1f, LawStripH, LedgerV2.Hair);

                var y = -10f;
                var stamp = LedgerV2.Mono(file, 12f, y, LawFileW - 24f,
                    "DAY " + row.Day, LawMonoAt(9f), LedgerV2.Label, 3f);
                stamp.font = LedgerStyle.MonoBold;
                y -= LawMonoRun(9f);

                var charge = Line(file, LedgerStyle.Condensed, LawCondAt(13f),
                    LedgerV2.Ink, 12f, y, LawFileW - 24f, LawCondRun(13f),
                    row.Charge.ToUpperInvariant());
                charge.characterSpacing = 1f;
                charge.overflowMode = TextOverflowModes.Ellipsis;
                y -= LawCondRun(13f);

                LedgerV2.Mono(file, 12f, y, LawFileW - 24f,
                    row.Where.ToUpperInvariant(), LawMonoAt(8f), LedgerV2.Muted, 2.5f);

                var archived = row;
                RowButton(file, ClickSurface(file), () => LawReadClosedFile(archived));
                x += LawFileW;
            }

            LawSettleAcross(lawDrawerContent, lawDrawerViewport, x, ref lawDrawerScroll);
        }

        /// <summary>A closed file has no map to open - what it has is what the court
        /// did, in <see cref="LedgerText.CaseOutcomeLine"/>'s own words, and that goes
        /// on the desk line where the sheet keeps everything else it has to say.</summary>
        void LawReadClosedFile(ArchiveRow row)
        {
            var said = row.Charge.ToUpperInvariant() +
                       (string.IsNullOrEmpty(row.Where) ? "" : "  ·  " + row.Where) +
                       "  ·  DAY " + row.Day + "  —  ";
            if (row.Lines.Count == 0)
                said += row.Note;
            else
                for (var i = 0; i < row.Lines.Count; i++)
                    said += (i > 0 ? "  ·  " : "") + row.Lines[i];
            LawSay(said, LedgerV2.Ink);
        }

        void LawStripEmpty(RectTransform run, string word)
        {
            LedgerV2.Mono(run, 12f, -(LawStripH - LawMonoRun(9.5f)) * 0.5f, 320f, word,
                LawMonoAt(9.5f), LedgerV2.Muted, 3f);
        }

        // ----------------------------------------------------------- THE HOUSE rail

        void PaintLawHouse()
        {
            var w = LawRailW - LawPanelPadX * 2f;
            var y = 0f;

            y = LedgerV2.Section(lawRailContent, 0f, y, w, "I. INSIDE",
                lawRows.Inside.Count == 0
                    ? "NONE"
                    : lawRows.Inside.Count + (lawRows.Inside.Count == 1 ? " man" : " men"));
            y = PaintLawInside(w, y) - 10f;

            y = LedgerV2.Section(lawRailContent, 0f, y, w, "II. WANTED",
                lawRows.Wanted.Count == 0
                    ? "NONE"
                    : lawRows.Wanted.Count + (lawRows.Wanted.Count == 1 ? " man" : " men"));
            y = PaintLawWanted(w, y) - 10f;

            y = LedgerV2.Section(lawRailContent, 0f, y, w, "III. COUNSEL",
                lawRows.Counsel.Has ? "ON RETAINER" : "NONE");
            y = PaintLawCounsel(w, y);

            LawSettleDown(lawRailContent, lawRailViewport, y, ref lawRailScroll);
        }

        float PaintLawInside(float w, float y)
        {
            if (lawRows.Inside.Count == 0)
                return LawEmpty(lawRailContent, "NOBODY INSIDE", w, y);

            var today = outfit ? outfit.Campaign.Day : 0;
            for (var i = 0; i < lawRows.Inside.Count; i++)
            {
                var man = lawRows.Inside[i];
                var row = LawRailRow(w, ref y, man.CharacterId, man.Name,
                    man.Charge.ToUpperInvariant() + "  ·  " + man.Stage.ToUpperInvariant(),
                    LedgerV2.Label, LawRailFigureW, out var figureX, out var figureY);

                var soon = man.CourtDay > 0 && today > 0 && man.CourtDay - today <= 1;
                var when = man.Life
                    ? "LIFE"
                    : man.OutOnDay > 0
                        ? "OUT DAY " + man.OutOnDay +
                          (today > 0 && man.OutOnDay > today
                              ? " (" + (man.OutOnDay - today) + "D)" : "")
                        : man.CourtDay > 0 ? "COURT DAY " + man.CourtDay : "";
                var figure = LedgerV2.Mono(row, figureX, figureY, LawRailFigureW, when,
                    LawMonoAt(8.5f), man.Life || soon ? LedgerV2.Red : LedgerV2.Muted,
                    1.5f, TextAlignmentOptions.MidlineRight);
                figure.font = LedgerStyle.MonoBold;
            }
            return y;
        }

        float PaintLawWanted(float w, float y)
        {
            if (lawRows.Wanted.Count == 0)
                return LawEmpty(lawRailContent, "NOBODY WANTED", w, y);

            for (var i = 0; i < lawRows.Wanted.Count; i++)
            {
                var man = lawRows.Wanted[i];
                LawRailRow(w, ref y, man.CharacterId, man.Name,
                    (man.Word + "  ·  " + man.When).ToUpperInvariant(), LedgerV2.Red,
                    0f, out _, out _);
            }
            return y;
        }

        /// <summary>One name on the rail: his photograph, his name over what he is, and
        /// the room a figure needs at the right margin.</summary>
        /// <summary>The right margin a prisoner's OUT DAY or COURT DAY needs. A WANTED
        /// row has no figure at all and gives the whole measure to the name.</summary>
        const float LawRailFigureW = 108f;

        RectTransform LawRailRow(float w, ref float y, int characterId, string name,
            string under, Color underTone, float figureW, out float figureX,
            out float figureY)
        {
            const float plateW = 26f;
            const float plateH = 31f;
            var nameRun = LawSerifRun(11.5f);
            var underRun = LawMonoRun(8f);
            var h = Mathf.Max(plateH, nameRun + underRun) + 10f;

            var row = NewRect("Rail row", lawRailContent);
            PlaceTopLeft(row, 0f, y, w, h);

            var member = director != null && director.Roster != null
                ? director.Roster.Find(characterId) : null;
            var plate = LedgerV2.PortraitPlate(row, 0f, -(h - plateH) * 0.5f, plateW,
                plateH, InitialsOf(name), LedgerV2.DarkPlate, LedgerV2.DarkPlateInk);
            if (member != null)
                PortraitStudio.Request(MemberModel(member), PortraitStudio.Framing.Bust,
                    plate);

            var textX = plateW + 9f;
            var textW = w - textX - (figureW > 0f ? figureW + 4f : 0f);
            var top = -(h - nameRun - underRun) * 0.5f;
            var label = Line(row, LedgerStyle.Serif, LawSerifAt(11.5f), LedgerV2.Ink,
                textX, top, textW, nameRun, name);
            label.overflowMode = TextOverflowModes.Ellipsis;
            LawOpenFile(row, textX, top, textW, nameRun, characterId);

            LedgerV2.Mono(row, textX, top - nameRun, w - textX, under, LawMonoAt(8f),
                underTone, 1.5f);

            figureX = w - figureW;
            figureY = -(h - LawMonoRun(8.5f)) * 0.5f;

            Block("Hair", row, 0f, -(h - 1f), w, 1f, LedgerV2.Hair);
            y -= h;
            return row;
        }

        float PaintLawCounsel(float w, float y)
        {
            var counsel = lawRows.Counsel;
            if (!counsel.Has)
            {
                var none = LedgerV2.Mono(lawRailContent, 0f, y, w,
                    "NO COUNSEL ON THE BOOKS", LawMonoAt(10f), LedgerV2.Red, 3f);
                none.font = LedgerStyle.MonoBold;
                y -= LawMonoRun(10f) + 6f;

                var ad = Paragraph(lawRailContent, LedgerStyle.Serif,
                    LawSerifAt(11.5f), LedgerV2.Body, 0f, y, w, LawSerifRun(11.5f, 2),
                    "The column runs an advertisement every " +
                    Outfit.HireMarket.LawyerAdEveryDays + " days.");
                ad.overflowMode = TextOverflowModes.Truncate;
                y -= LawSerifRun(11.5f, 2) + 8f;

                LedgerV2.Button(lawRailContent, "THE COLUMN", 0f, y,
                    LedgerV2.ButtonWidth("THE COLUMN", 10.5f, 6f, 12f), 30f,
                    () =>
                    {
                        LawSay("THE COLUMN  ·  THE BOOK TURNS TO THE CLASSIFIED PAGE",
                            LedgerV2.Ink);
                        SetPage(LedgerPage.Newspaper);
                    }, LedgerV2.Key.Dark);
                return y - 38f;
            }

            var name = Line(lawRailContent, LedgerStyle.Condensed, LawCondAt(17f),
                LedgerV2.Ink, 0f, y, w, LawCondRun(17f), counsel.Name);
            name.overflowMode = TextOverflowModes.Ellipsis;
            LawOpenFile(lawRailContent, 0f, y, w, LawCondRun(17f), counsel.CharacterId);
            y -= LawCondRun(17f) + 4f;

            Stars(lawRailContent, 0f, y - 8f,
                AttributeScale.MaxHalfSteps * counsel.Skill / Lawyer.MaxSkill, 13f, 14f);
            y -= 20f;

            y = LawLeaderRow(w, y, "Kept out",
                counsel.Won + (counsel.Won == 1 ? " man" : " men"), LedgerV2.Green);
            y = LawLeaderRow(w, y, "Went down",
                counsel.Lost + (counsel.Lost == 1 ? " man" : " men"), LedgerV2.Red);
            y = LawLeaderRow(w, y, "Retainer",
                LedgerText.Cash(counsel.Wage) + " a day", LedgerV2.Ink);
            y -= 4f;

            LedgerV2.Mono(lawRailContent, 0f, y, w,
                LedgerText.Cash(counsel.Wage) + " A DAY  ·  " +
                (counsel.CanAskBail ? "CAN ASK FOR BAIL" : "CANNOT GET A HEARING LISTED"),
                LawMonoAt(8.7f), counsel.CanAskBail ? LedgerV2.Muted : LedgerV2.Red,
                1.5f);
            return y - LawMonoRun(8.7f);
        }

        /// <summary>A label, a run of dots and the figure that answers it - the design's
        /// LeaderRow, in the kit's own dotted rule.</summary>
        float LawLeaderRow(float w, float y, string label, string figure, Color tone)
        {
            var run = LawMonoRun(8.7f);
            var text = LedgerV2.Mono(lawRailContent, 0f, y, w * 0.45f,
                label.ToUpperInvariant(), LawMonoAt(8.7f), LedgerV2.Label, 1.5f);
            text.overflowMode = TextOverflowModes.Ellipsis;

            var figureW = w * 0.42f;
            var value = LedgerV2.Mono(lawRailContent, w - figureW, y, figureW,
                figure.ToUpperInvariant(), LawMonoAt(8.7f), tone, 1.5f,
                TextAlignmentOptions.MidlineRight);
            value.font = LedgerStyle.MonoBold;

            LedgerV2.Leader(lawRailContent, w * 0.46f, y - run * 0.5f,
                w - figureW - w * 0.48f);
            return y - run - 3f;
        }

        // ------------------------------------------------------------- the desk line

        void PaintLawDeskLine()
        {
            var word = string.IsNullOrEmpty(lawDeskLine)
                ? "PICK A MAN ON THE MAP  ·  THE DESK POSTS, SKIPS AND CUTS LOOSE"
                : lawDeskLine;
            var tone = string.IsNullOrEmpty(lawDeskLine) ? LedgerV2.Faint : lawDeskTone;

            var line = LedgerV2.Mono(lawFixed, PageLeft, LawDeskY,
                PageWidth - 240f, word, LawMonoAt(9f), tone, 2f);
            line.overflowMode = TextOverflowModes.Ellipsis;

            LedgerV2.Mono(lawFixed, PageLeft + PageWidth - 240f, LawDeskY, 240f,
                "FOLIO 17  ·  P CLOSES THE BOOK", LawMonoAt(9f), LedgerV2.Faint, 2f,
                TextAlignmentOptions.MidlineRight);
        }

        /// <summary>
        /// THE BOOK SHUT. The sheet's view state is not worth keeping across a close -
        /// the desk line is about something the player has already seen happen, and a
        /// map left scrolled where the last case's men stood opens on empty stage. The
        /// FILE the strip had open is kept: reopening the book on the case the boss was
        /// working is the whole reason the strip remembers anything.
        /// </summary>
        void LawSheetClosed()
        {
            lawDeskLine = "";
            lawDeskTone = LedgerV2.Faint;
            lawCentreMap = true;
        }

        /// <summary>What the desk has to say, and how it reads. Nothing about the world
        /// is written here - it is the line under the sheet and nothing else.</summary>
        void LawSay(string word, Color tone)
        {
            lawDeskLine = word;
            lawDeskTone = tone;
            dirty = true;
        }

        // ------------------------------------------------------------------- the desk
        //
        // TWO DOORS, ONE DESK: bail, skip and cut go through RoadDemo.LawDesk, the same
        // desk the man's own PERSONNEL file calls. What is added here is only the words
        // - the refusal is the desk's own, and the success line is this sheet reading
        // back what it just asked for.

        void LawPostBail(DefendantRow man)
        {
            var result = RoadDemo.LawDesk.PostBail(man.CharacterId);
            if (result.Ok)
                LawSay("BAIL POSTED  ·  " + LedgerText.Cash(man.Bail) +
                       " OUT OF THE SAFE  ·  " + LawSurname(man.Name) +
                       " WALKS OUT TODAY", LedgerV2.Green);
            else
                LawRefuse(result.Reason);
            dirty = true;
        }

        void LawSkipBail(DefendantRow man)
        {
            var result = RoadDemo.LawDesk.SkipBail(man.CharacterId);
            if (result.Ok)
                LawSay("BAIL SKIPPED  ·  " + LawSurname(man.Name) +
                       " IS WANTED FROM TODAY  ·  " + LedgerText.Cash(man.Bail) +
                       " IS GONE", LedgerV2.Red);
            else
                LawRefuse(result.Reason);
            dirty = true;
        }

        void LawCutLoose(DefendantRow man)
        {
            var result = RoadDemo.LawDesk.CutLoose(man.CharacterId);
            if (result.Ok)
                LawSay("CUT LOOSE  ·  " + LawSurname(man.Name) +
                       " IS OFF THE BOOKS. WHAT HE SAYS IN COURT IS HIS OWN.",
                    LedgerV2.Red);
            else
                LawRefuse(result.Reason);
            dirty = true;
        }

        /// <summary>A refusal reads on the desk line AND goes up the wire: the strip is
        /// where the book has always put a refusal, and the sheet cannot quietly swallow
        /// one just because it now has a line of its own.</summary>
        void LawRefuse(string reason)
        {
            lastRefusal = reason;
            LawSay("THE DESK REFUSES  ·  " + reason.ToUpperInvariant(), LedgerV2.Red);
        }

        /// <summary>The last word of a name, which is what the desk calls a man.</summary>
        static string LawSurname(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "HE";
            var parts = name.Split(' ');
            for (var i = parts.Length - 1; i >= 0; i--)
                if (parts[i].Length > 0 && parts[i][0] != '"')
                    return parts[i].ToUpperInvariant();
            return name.ToUpperInvariant();
        }

        // ------------------------------------------------------------------- the telex

        /// <summary>
        /// What the strip says over this sheet. Deliberately NOT the "nothing on this
        /// sheet happens at the click" line the ORGANIZATION and CHAIN OF COMMAND sheets
        /// carry: like PERSONNEL, this one acts at the click - for exactly the three
        /// operations the man's own file already offers.
        /// </summary>
        void ComposeLawTelex()
        {
            CollectLaw();

            var onBail = 0;
            for (var i = 0; i < lawRows.Inside.Count; i++)
                if (lawRows.Inside[i].Stage ==
                    LedgerText.StageLabel(PrisonStage.Bailed))
                    onBail++;

            telexMessages.Add((
                lawRows.Inside.Count + (lawRows.Inside.Count == 1 ? " man" : " men") +
                " in the city's hands  ·  " + onBail + " on bail  ·  " +
                lawRows.Wanted.Count + " wanted",
                lawRows.Inside.Count > 0 || lawRows.Wanted.Count > 0
                    ? TelexVoice.Warn : TelexVoice.Plain));

            var soonest = -1;
            for (var i = 0; i < lawRows.Docket.Count; i++)
            {
                if (lawRows.Docket[i].NobodyTaken) continue;
                soonest = i;
                break;
            }
            if (soonest >= 0)
            {
                var row = lawRows.Docket[soonest];
                telexMessages.Add((
                    (row.DaysToCourt > 0
                        ? "Court day in " + row.DaysToCourt +
                          (row.DaysToCourt == 1 ? " day" : " days")
                        : "Court day is today") +
                    " — " + row.Charge.ToUpperInvariant() +
                    (string.IsNullOrEmpty(row.Where) ? "" : " at " + row.Where),
                    row.DaysToCourt <= 1 ? TelexVoice.Urgent : TelexVoice.Plain));
            }
            else
            {
                telexMessages.Add(("Nothing of ours is listed to be heard",
                    TelexVoice.Plain));
            }

            telexMessages.Add(lawRows.Counsel.Has
                ? ("Counsel: " + lawRows.Counsel.Name + ", " + lawRows.Counsel.Skill +
                   " of " + Lawyer.MaxSkill, TelexVoice.Plain)
                : ("No counsel on the books · a man with no lawyer gets no bail hearing " +
                   "listed at all", TelexVoice.Warn));
        }

        // ------------------------------------------------------------------ the words

        /// <summary>How near the court day is, in the sheet's three tones - and the
        /// paper blue of a complaint nobody has been taken on, which is not near
        /// anything.</summary>
        Color LawCourtTone(DocketRow row)
        {
            if (row.NobodyTaken || row.CourtDay <= 0)
                return LedgerV2.PaperBlue;
            if (row.DaysToCourt <= 1)
                return LedgerV2.Red;
            return row.DaysToCourt <= 3 ? LedgerV2.Amber : LedgerV2.Muted;
        }

        /// <summary>Counsel's read, in colour. The words are
        /// <see cref="Verdict.Leaning"/>'s own four bands and the two certainties beside
        /// them; nothing here re-words any of them, it only says how they read.</summary>
        static Color LawReadTone(string read)
        {
            if (read == Verdict.NoWitnessesLeft) return LedgerV2.Green;
            if (read == Verdict.NoCounselToAsk) return LedgerV2.Red;
            if (read == Verdict.Leaning(1f) || read == Verdict.Leaning(0.6f))
                return LedgerV2.Red;
            return read == Verdict.Leaning(0.4f) ? LedgerV2.Amber : LedgerV2.Green;
        }

        /// <summary>The same reading over the map's dark stage. The paper greys and reds
        /// disappear on it, so every tone on the stage is its rail twin.</summary>
        static Color LawOnRail(Color paper)
        {
            if (paper == LedgerV2.Red) return LedgerStyle.RailRed;
            if (paper == LedgerV2.Amber) return LedgerStyle.RailGold;
            if (paper == LedgerV2.Green) return LedgerStyle.RailGreen;
            if (paper == LedgerV2.PaperBlue) return LedgerStyle.RailValue;
            return LedgerStyle.RailLabel;
        }

        // ------------------------------------------------------------------- the parts

        float LawEmpty(RectTransform parent, string word, float w, float y)
        {
            LedgerV2.Mono(parent, 0f, y, w, word, LawMonoAt(9.5f), LedgerV2.Muted, 3f);
            return y - (LawMonoRun(9.5f) + 6f);
        }

        /// <summary>A run's height: what was drawn, never less than the window, so a
        /// short sheet does not scroll and a long one does.</summary>
        static float LawRun(float y) => Mathf.Max(-y + 8f, 0f);

        /// <summary>
        /// The run is as tall as what was drawn, and the region KEEPS ITS OWN PLACE -
        /// clamped to its own new height, so a pane that shrank comes back inside itself
        /// without dragging the pane next to it anywhere.
        /// </summary>
        static void LawSettleDown(RectTransform content, RectTransform viewport, float y,
            ref float scroll)
        {
            var run = LawRun(y);
            var window = viewport != null ? viewport.rect.height : 0f;
            content.sizeDelta = new Vector2(viewport != null ? viewport.rect.width : 0f,
                Mathf.Max(run, window));
            scroll = Mathf.Clamp(scroll, 0f, Mathf.Max(0f, run - window));
            content.anchoredPosition = new Vector2(0f, scroll);
        }

        /// <summary>The same, sideways: the strips are runs of tabs, not columns of
        /// rows, and they keep their own place along their own axis.</summary>
        static void LawSettleAcross(RectTransform content, RectTransform viewport,
            float x, ref float scroll)
        {
            var window = viewport != null ? viewport.rect.width : 0f;
            content.sizeDelta = new Vector2(Mathf.Max(x, window),
                viewport != null ? viewport.rect.height : 0f);
            scroll = Mathf.Clamp(scroll, 0f, Mathf.Max(0f, x - window));
            content.anchoredPosition = new Vector2(-scroll, 0f);
        }

        /// <summary>And the map, which reads both ways at once.</summary>
        static void LawSettleMap(RectTransform content, RectTransform viewport,
            Vector2 stage, ref Vector2 scroll, bool centre)
        {
            var window = viewport != null
                ? new Vector2(viewport.rect.width, viewport.rect.height) : Vector2.zero;
            content.sizeDelta = stage;
            var reach = new Vector2(Mathf.Max(0f, stage.x - window.x),
                Mathf.Max(0f, stage.y - window.y));
            if (centre)
                scroll = reach * 0.5f;
            scroll = new Vector2(Mathf.Clamp(scroll.x, 0f, reach.x),
                Mathf.Clamp(scroll.y, 0f, reach.y));
            content.anchoredPosition = new Vector2(-scroll.x, scroll.y);
        }

        // ---------------------------------------------------------------- the wheel

        /// <summary>
        /// FOUR WHEEL FIELDS, and whichever the pointer sits over takes the notch - the
        /// PERSONNEL and ARMORY rule. The two strips and the map read ACROSS as well as
        /// down, which is why this sheet answers the wheel itself instead of nominating
        /// one region to the book's own router.
        /// </summary>
        void ScrollLaw(float wheel, Vector2 point)
        {
            if (Over(lawMapViewport, point))
            {
                var reach = Mathf.Max(0f,
                    lawMapContent.sizeDelta.y - lawMapViewport.rect.height);
                lawMapScroll.y = Mathf.Clamp(lawMapScroll.y - wheel * WheelStep, 0f,
                    reach);
                lawMapContent.anchoredPosition =
                    new Vector2(-lawMapScroll.x, lawMapScroll.y);
                return;
            }

            if (Over(lawRailViewport, point))
            {
                var reach = Mathf.Max(0f,
                    lawRailContent.sizeDelta.y - lawRailViewport.rect.height);
                lawRailScroll = Mathf.Clamp(lawRailScroll - wheel * WheelStep, 0f, reach);
                lawRailContent.anchoredPosition = new Vector2(0f, lawRailScroll);
                return;
            }

            // A strip has no down to scroll, so the plain notch walks it along. A wheel
            // that did nothing over a row of tabs that plainly runs off the edge would
            // read as a broken strip.
            if (Over(lawStripViewport, point))
                LawWalk(lawStripContent, lawStripViewport, -wheel * WheelStep,
                    ref lawStripScroll);
            else if (Over(lawDrawerViewport, point))
                LawWalk(lawDrawerContent, lawDrawerViewport, -wheel * WheelStep,
                    ref lawDrawerScroll);
        }

        /// <summary>A sideways notch - a trackpad's second axis, or a wheel that tilts.
        /// Answers whether it meant anything here.</summary>
        bool ScrollLawAcross(float across, Vector2 point)
        {
            if (Over(lawMapViewport, point))
            {
                var reach = Mathf.Max(0f,
                    lawMapContent.sizeDelta.x - lawMapViewport.rect.width);
                lawMapScroll.x = Mathf.Clamp(lawMapScroll.x + across * WheelStep, 0f,
                    reach);
                lawMapContent.anchoredPosition =
                    new Vector2(-lawMapScroll.x, lawMapScroll.y);
                return true;
            }
            if (Over(lawStripViewport, point))
                return LawWalk(lawStripContent, lawStripViewport, across * WheelStep,
                    ref lawStripScroll);
            if (Over(lawDrawerViewport, point))
                return LawWalk(lawDrawerContent, lawDrawerViewport, across * WheelStep,
                    ref lawDrawerScroll);
            return false;
        }

        static bool LawWalk(RectTransform content, RectTransform viewport, float by,
            ref float scroll)
        {
            if (!content || !viewport)
                return false;
            var reach = Mathf.Max(0f, content.sizeDelta.x - viewport.rect.width);
            scroll = Mathf.Clamp(scroll + by, 0f, reach);
            content.anchoredPosition = new Vector2(-scroll, content.anchoredPosition.y);
            return true;
        }

        // ------------------------------------------------------------------ the jumps

        /// <summary>
        /// THE ORDER IS GIVEN ON THE STREET. The sheet does not file a lean: it closes
        /// the book and puts the map on the man, where the crew's own LEAN ON THE
        /// WITNESS card lives. The shape BLOCKS uses - Close(), then the map.
        /// </summary>
        void LeanOnWitness(int seed, Vector3 at)
        {
            var map = MapTargeting.Surface;
            Close();
            if (map == null || !map.CanSummon || !map.Summon())
            {
                lastRefusal = map != null && !string.IsNullOrEmpty(map.SummonHint)
                    ? map.SummonHint
                    : LedgerText.ReasonNoMap;
                return;
            }
            map.FocusOn(at);
        }

        /// <summary>
        /// A NAME ON THIS SHEET OPENS THE MAN'S OWN FILE.
        ///
        /// The surface is a rect of its OWN, laid over the name rather than on it: a
        /// GameObject may hold one Image, a text already holds a graphic, and Unity
        /// answers the second AddComponent with null instead of an exception - which
        /// takes the whole paint down on the next line, far from the cause
        /// (LedgerKit.ClickSurface says so in as many words).
        /// </summary>
        void LawOpenFile(RectTransform parent, float x, float y, float w, float h,
            int characterId)
        {
            if (characterId < 0) return;
            var hit = NewRect("Open file", parent);
            PlaceTopLeft(hit, x, y, w, h);
            RowButton(hit, ClickSurface(hit), () =>
            {
                SetPage(LedgerPage.Command);
                OpenCommandDossier(characterId);
            });
        }
    }
}
