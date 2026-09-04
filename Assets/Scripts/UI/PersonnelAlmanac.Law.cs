using TMPro;
using UnityEngine;
using UnityEngine.UI;
using LivingCity.Personnel;
using LivingCity.Police;
using static LivingCity.UI.LedgerKit;

namespace LivingCity.UI
{
    /// <summary>
    /// THE LAW — the ninth sheet of the book (GAN-302).
    ///
    /// What the state has against the outfit was scattered over the men's own files: a
    /// held man's case on HIS page, the lawyer's record on HIS, a wanted man's grade
    /// nowhere at all, and a case that had closed only as a line of prose on a rap sheet.
    /// This sheet is the one place it is all read at once - the docket with its
    /// witnesses, the cells, the men the city is looking for, the counsel on retainer,
    /// and the archive of everything the court has already done.
    ///
    /// The page holds NO state the model does not. Every row comes from
    /// <see cref="LawSheet.Collect"/> at paint, and the only writes are the three
    /// operations the man's own file already offers, through the same
    /// <see cref="RoadDemo.LawDesk"/>.
    /// </summary>
    public sealed partial class PersonnelAlmanac
    {
        // ------------------------------------------------------------- the fixture

        /// <summary>Air under the page head, matching every other sheet.</summary>
        const float LawHeadDrop = 76f;

        /// <summary>Between the docket and the boxes, and between the boxes.</summary>
        const float LawGutter = 16f;

        /// <summary>The docket's share of the width. The rest is the three boxes.</summary>
        const float LawDocketShare = 0.60f;

        /// <summary>The archive's share of the height under the page head.</summary>
        const float LawArchiveShare = 0.26f;

        static float LawTop, LawHeight, LawDocketW, LawBoxW, LawBoxX, LawTopH,
            LawArchiveTop, LawArchiveH;

        /// <summary>Measured against the fixture the book was actually laid out at -
        /// never guessed, because the sheet is re-laid whenever the window moves.</summary>
        static void MeasureLaw()
        {
            LawTop = PageTop - LawHeadDrop;
            LawHeight = -(PageBottom - LawTop);
            LawArchiveH = LawHeight * LawArchiveShare;
            LawTopH = LawHeight - LawArchiveH - LawGutter;
            LawArchiveTop = LawTop - LawTopH - LawGutter;
            LawDocketW = (PageWidth - LawGutter) * LawDocketShare;
            LawBoxW = PageWidth - LawGutter - LawDocketW;
            LawBoxX = PageLeft + LawDocketW + LawGutter;
        }

        RectTransform lawFixed;
        internal RectTransform lawDocketViewport, lawDocketContent;
        internal RectTransform lawInsideViewport, lawInsideContent;
        internal RectTransform lawWantedViewport, lawWantedContent;
        internal RectTransform lawArchiveViewport, lawArchiveContent;
        RectTransform lawCounselBox;

        readonly LawSheetRows lawRows = new LawSheetRows();

        /// <summary>
        /// A WHEEL POSITION PER REGION. Four windows on one sheet, and a shared offset
        /// makes them fight: scroll a long docket, put the pointer on a short INSIDE,
        /// and the clamp for the short one drags the long one back to the top. The
        /// ORDERS page shared the same field and inherited whatever the law sheet was
        /// last left at.
        /// </summary>
        internal float lawDocketScroll, lawInsideScroll, lawWantedScroll, lawArchiveScroll;

        // ------------------------------------------------------------- the building

        void BuildLawPage(RectTransform sheet)
        {
            var root = NewPageRoot(sheet, LedgerPage.Law);
            MeasureLaw();

            lawFixed = NewRect("Law Fixed", root);
            Stretch(lawFixed);

            var boxH = (LawTopH - LawGutter * 2f) / 3f;

            lawDocketViewport = LawWindow(root, "Docket", PageLeft, LawTop,
                LawDocketW, LawTopH, out lawDocketContent);
            lawInsideViewport = LawWindow(root, "Inside", LawBoxX, LawTop,
                LawBoxW, boxH, out lawInsideContent);
            lawWantedViewport = LawWindow(root, "Wanted", LawBoxX,
                LawTop - boxH - LawGutter, LawBoxW, boxH, out lawWantedContent);
            lawArchiveViewport = LawWindow(root, "Verdicts", PageLeft, LawArchiveTop,
                PageWidth, LawArchiveH, out lawArchiveContent);

            // The counsel box is one man and never scrolls: a retainer that needed a
            // wheel would mean the outfit had hired a firm.
            lawCounselBox = NewRect("Law Counsel", root);
            PlaceTopLeft(lawCounselBox, LawBoxX, LawTop - (boxH + LawGutter) * 2f,
                LawBoxW, boxH);
        }

        RectTransform LawWindow(RectTransform root, string name, float x, float y,
            float w, float h, out RectTransform content)
        {
            var viewport = NewRect("Law " + name + " Window", root);
            PlaceTopLeft(viewport, x, y, w, h);
            viewport.gameObject.AddComponent<RectMask2D>();

            content = NewRect("Law " + name, viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0f, h);
            return viewport;
        }

        // ------------------------------------------------------------- the painting

        void RebuildLaw()
        {
            if (!lawFixed || !lawDocketContent)
                return;

            MeasureLaw();
            foreach (Transform old in lawFixed) Destroy(old.gameObject);
            foreach (Transform old in lawDocketContent) Destroy(old.gameObject);
            foreach (Transform old in lawInsideContent) Destroy(old.gameObject);
            foreach (Transform old in lawWantedContent) Destroy(old.gameObject);
            foreach (Transform old in lawArchiveContent) Destroy(old.gameObject);
            foreach (Transform old in lawCounselBox) Destroy(old.gameObject);

            LedgerV2.PageHead(lawFixed, PageLeft, PageTop, PageWidth, "THE LAW",
                "THE DOCKET, THE CELLS AND THE MEN WHO ARE NOT HOME · " +
                "AS THE PRECINCT HAS IT");

            CollectLaw();

            PaintDocket();
            PaintInside();
            PaintWanted();
            PaintCounsel();
            PaintArchive();
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

        // ------------------------------------------------------------------ the docket

        void PaintDocket()
        {
            var y = 0f;
            y = LawSectionHead(lawDocketContent, "THE DOCKET", LawDocketW, y);

            if (lawRows.Docket.Count == 0)
            {
                y = LawEmpty(lawDocketContent, "NO CASE AGAINST US", LawDocketW, y);
                LawSettle(lawDocketContent, lawDocketViewport, y,
                    ref lawDocketScroll);
                return;
            }

            for (var i = 0; i < lawRows.Docket.Count; i++)
                y = PaintCase(lawRows.Docket[i], y) - 10f;

            LawSettle(lawDocketContent, lawDocketViewport, y, ref lawDocketScroll);
        }

        float PaintCase(DocketRow row, float y)
        {
            const float pad = 10f;
            var w = LawDocketW;
            var card = NewRect("Case " + row.File.CaseId, lawDocketContent);
            Fill(card, LedgerV2.PanelBand);

            var inner = w - pad * 2f;
            var top = -pad;

            var head = Line(card, LedgerStyle.MonoBold, 11f, LedgerV2.Ink,
                pad, top, inner, LineBox(11f),
                row.Charge.ToUpperInvariant() +
                (string.IsNullOrEmpty(row.Where) ? "" : "  ·  " + row.Where));
            head.characterSpacing = 2f;
            head.overflowMode = TextOverflowModes.Ellipsis;
            top -= LineBox(11f) + 2f;

            var when = row.NobodyTaken
                ? "ON THE DOCKET  ·  nobody taken"
                : "COURT DAY " + row.CourtDay +
                  (row.DaysToCourt > 0
                      ? "  (" + row.DaysToCourt +
                        (row.DaysToCourt == 1 ? " day)" : " days)")
                      : "");
            LedgerV2.Mono(card, pad, top, inner,
                "OPENED DAY " + row.OpenedDay + "  ·  " + when +
                (row.Counts > 0
                    ? "  ·  +" + row.Counts + " COUNT" + (row.Counts == 1 ? "" : "S")
                    : ""),
                9.5f, row.NobodyTaken ? LedgerV2.PaperBlue : LedgerV2.Muted, 1.5f);
            top -= LineBox(9.5f) + 6f;

            for (var i = 0; i < row.Defendants.Count; i++)
                top = PaintDefendant(card, row.Defendants[i], pad, inner, top);

            if (row.Witnesses.Count > 0)
            {
                top -= 2f;
                LedgerV2.Mono(card, pad, top, inner, "WHO WILL GIVE EVIDENCE", 9f,
                    LedgerV2.Label, 2f);
                top -= LineBox(9f) + 2f;
                for (var i = 0; i < row.Witnesses.Count; i++)
                    top = PaintWitness(card, row.Witnesses[i], pad, inner, top);
            }

            top -= 4f;
            LedgerV2.Mono(card, pad, top, inner,
                "COUNSEL SAYS  ·  " + row.Read, 9.5f,
                row.Read == Verdict.NoWitnessesLeft ? LedgerV2.Green
                : row.Read == Verdict.NoCounselToAsk ? LedgerV2.Red
                : LedgerV2.Body, 1.5f);
            top -= LineBox(9.5f) + 2f;

            LedgerV2.Mono(card, pad, top, inner,
                "COUNSEL  ·  " + (row.Counsel.Length > 0 ? row.Counsel : "NONE"),
                9f, row.Counsel.Length > 0 ? LedgerV2.Muted : LedgerV2.Red, 1.5f);
            top -= LineBox(9f) + pad;

            PlaceTopLeft(card, 0f, y, w, -top);
            return y + top;
        }

        float PaintDefendant(RectTransform card, DefendantRow man, float x, float w,
            float y)
        {
            const float keyH = 22f;
            var name = Line(card, LedgerStyle.Serif, 11f, LedgerV2.Ink, x, y,
                w * 0.5f, LineBox(11f), man.Name);
            name.overflowMode = TextOverflowModes.Ellipsis;
            LawOpenFile(card, x, y, w * 0.5f, LineBox(11f), man.CharacterId);

            LedgerV2.Mono(card, x + w * 0.5f, y, w * 0.5f,
                man.Stage.ToUpperInvariant() +
                (man.Bail > 0 ? "  ·  BAIL " + LedgerText.Cash(man.Bail) : "  ·  NO BAIL"),
                9f, man.Stage == LawSheet.Hiding ? LedgerV2.Red : LedgerV2.Muted, 1.5f,
                TextAlignmentOptions.MidlineRight);
            y -= LineBox(11f) + 2f;

            if (!string.IsNullOrEmpty(man.Answer))
            {
                LedgerV2.Mono(card, x, y, w, man.Answer.ToUpperInvariant(), 8.5f,
                    man.Answer.Contains("sprung") ? LedgerV2.Red : LedgerV2.PaperBlue, 1.2f);
                y -= LineBox(8.5f) + 2f;
            }

            // The three keys, and the SAME desk the man's own file calls: two doors on
            // one operation, never two implementations of it.
            var id = man.CharacterId;
            var third = (w - 12f) / 3f;
            LedgerV2.Button(card, "POST BAIL", x, y, third, keyH, () =>
            {
                var result = RoadDemo.LawDesk.PostBail(id);
                lastRefusal = result.Ok ? "" : result.Reason;
                dirty = true;
            }, red: false, size: 9f, outline: !man.CanPostBail);

            LedgerV2.Button(card, "SKIP BAIL", x + third + 6f, y, third, keyH, () =>
            {
                var result = RoadDemo.LawDesk.SkipBail(id);
                lastRefusal = result.Ok ? "" : result.Reason;
                dirty = true;
            }, red: true, size: 9f, outline: !man.CanSkipBail);

            LedgerV2.Button(card, "CUT HIM LOOSE", x + (third + 6f) * 2f, y, third, keyH,
                () =>
                {
                    var result = RoadDemo.LawDesk.CutLoose(id);
                    lastRefusal = result.Ok ? "" : result.Reason;
                    dirty = true;
                }, red: true, size: 9f, outline: !man.CanCutLoose);

            return y - (keyH + 8f);
        }

        float PaintWitness(RectTransform card, WitnessRow witness, float x, float w,
            float y)
        {
            const float keyW = 92f;
            const float keyH = 20f;
            var textW = w - (witness.CanLeanOn ? keyW + 8f : 0f);

            var line = Line(card, LedgerStyle.Serif, 10f, LedgerV2.Body, x, y,
                textW * 0.62f, LineBox(10f), witness.Kind);
            line.overflowMode = TextOverflowModes.Ellipsis;

            LedgerV2.Mono(card, x + textW * 0.62f, y, textW * 0.38f, witness.Standing,
                8.5f,
                witness.Witness.Standing == WitnessStanding.WillTestify
                    ? (witness.WillBeHeard ? LedgerV2.Red : LedgerV2.Amber)
                    : LedgerV2.Green,
                1.2f, TextAlignmentOptions.MidlineRight);

            if (witness.CanLeanOn)
            {
                var seed = witness.Witness.Seed;
                var at = new Vector3(witness.Witness.X, witness.Witness.Y,
                    witness.Witness.Z);
                LedgerV2.Button(card, "LEAN ON HIM", x + textW + 8f, y, keyW, keyH,
                    () => LeanOnWitness(seed, at), red: true, size: 8.5f);
            }

            return y - (LineBox(10f) + 4f);
        }

        // ------------------------------------------------------------------ the boxes

        void PaintInside()
        {
            var y = LawSectionHead(lawInsideContent, "INSIDE", LawBoxW, 0f);
            if (lawRows.Inside.Count == 0)
            {
                y = LawEmpty(lawInsideContent, "NOBODY INSIDE", LawBoxW, y);
                LawSettle(lawInsideContent, lawInsideViewport, y, ref lawInsideScroll);
                return;
            }

            var today = outfit ? outfit.Campaign.Day : 0;
            for (var i = 0; i < lawRows.Inside.Count; i++)
            {
                var man = lawRows.Inside[i];
                var id = man.CharacterId;
                var name = Line(lawInsideContent, LedgerStyle.Serif, 10.5f, LedgerV2.Ink,
                    0f, y, LawBoxW * 0.55f, LineBox(10.5f), man.Name);
                name.overflowMode = TextOverflowModes.Ellipsis;
                LawOpenFile(lawInsideContent, 0f, y, LawBoxW * 0.55f, LineBox(10.5f), id);

                var when = man.Life
                    ? "life"
                    : man.OutOnDay > 0
                        ? "out day " + man.OutOnDay +
                          (today > 0 && man.OutOnDay > today
                              ? "  (" + (man.OutOnDay - today) + "d)" : "")
                        : man.CourtDay > 0 ? "court day " + man.CourtDay : "";
                LedgerV2.Mono(lawInsideContent, LawBoxW * 0.55f, y, LawBoxW * 0.45f,
                    when.ToUpperInvariant(), 8.5f, LedgerV2.Muted, 1.2f,
                    TextAlignmentOptions.MidlineRight);
                y -= LineBox(10.5f);

                LedgerV2.Mono(lawInsideContent, 0f, y, LawBoxW,
                    man.Charge + "  ·  " + man.Stage, 8.5f, LedgerV2.Label, 1.2f);
                y -= LineBox(8.5f) + 6f;
            }
            LawSettle(lawInsideContent, lawInsideViewport, y, ref lawInsideScroll);
        }

        void PaintWanted()
        {
            var y = LawSectionHead(lawWantedContent, "WANTED", LawBoxW, 0f);
            if (lawRows.Wanted.Count == 0)
            {
                y = LawEmpty(lawWantedContent, "NOBODY WANTED", LawBoxW, y);
                LawSettle(lawWantedContent, lawWantedViewport, y, ref lawWantedScroll);
                return;
            }

            for (var i = 0; i < lawRows.Wanted.Count; i++)
            {
                var man = lawRows.Wanted[i];
                var name = Line(lawWantedContent, LedgerStyle.Serif, 10.5f, LedgerV2.Ink,
                    0f, y, LawBoxW, LineBox(10.5f), man.Name);
                name.overflowMode = TextOverflowModes.Ellipsis;
                LawOpenFile(lawWantedContent, 0f, y, LawBoxW, LineBox(10.5f),
                    man.CharacterId);
                y -= LineBox(10.5f);

                LedgerV2.Mono(lawWantedContent, 0f, y, LawBoxW,
                    man.Word + "  ·  " + man.When, 8.5f, LedgerV2.Red, 1.2f);
                y -= LineBox(8.5f) + 6f;
            }
            LawSettle(lawWantedContent, lawWantedViewport, y, ref lawWantedScroll);
        }

        void PaintCounsel()
        {
            var y = LawSectionHead(lawCounselBox, "COUNSEL", LawBoxW, 0f);
            var counsel = lawRows.Counsel;

            if (!counsel.Has)
            {
                LedgerV2.Mono(lawCounselBox, 0f, y, LawBoxW, "NO COUNSEL ON THE BOOKS",
                    9.5f, LedgerV2.Red, 1.5f);
                y -= LineBox(9.5f) + 2f;
                LedgerV2.Mono(lawCounselBox, 0f, y, LawBoxW,
                    "the column runs an ad every " + Outfit.HireMarket.LawyerAdEveryDays +
                    " days", 8.5f, LedgerV2.Muted, 1.2f);
                y -= LineBox(8.5f) + 6f;
                LedgerV2.Button(lawCounselBox, "THE COLUMN", 0f, y,
                    LedgerV2.ButtonWidth("THE COLUMN", 9f, 6f, 12f), 22f,
                    () => { SetPage(LedgerPage.Newspaper); dirty = true; },
                    LedgerV2.Key.Dark, 9f);
                return;
            }

            var name = Line(lawCounselBox, LedgerStyle.Serif, 11f, LedgerV2.Ink, 0f, y,
                LawBoxW, LineBox(11f), counsel.Name);
            name.overflowMode = TextOverflowModes.Ellipsis;
            LawOpenFile(lawCounselBox, 0f, y, LawBoxW, LineBox(11f),
                counsel.CharacterId);
            y -= LineBox(11f) + 2f;

            LedgerKit.Stars(lawCounselBox, 0f, y - 10f,
                AttributeScale.MaxHalfSteps * counsel.Skill / Lawyer.MaxSkill, 13f, 14f);
            y -= 20f;

            LedgerV2.Mono(lawCounselBox, 0f, y, LawBoxW,
                counsel.Won + " KEPT OUT  ·  " + counsel.Lost + " WENT DOWN", 9f,
                LedgerV2.Muted, 1.5f);
            y -= LineBox(9f) + 2f;

            LedgerV2.Mono(lawCounselBox, 0f, y, LawBoxW,
                LedgerText.Cash(counsel.Wage) + " A DAY  ·  " +
                (counsel.CanAskBail ? "can ask for bail" : "cannot get a hearing listed"),
                8.5f, counsel.CanAskBail ? LedgerV2.Muted : LedgerV2.Red, 1.2f);
        }

        // ----------------------------------------------------------------- the archive

        void PaintArchive()
        {
            var y = LawSectionHead(lawArchiveContent, "VERDICTS", PageWidth, 0f);
            if (lawRows.Archive.Count == 0)
            {
                y = LawEmpty(lawArchiveContent, "NOTHING HAS COME TO COURT", PageWidth, y);
                LawSettle(lawArchiveContent, lawArchiveViewport, y,
                    ref lawArchiveScroll);
                return;
            }

            for (var i = 0; i < lawRows.Archive.Count; i++)
            {
                var row = lawRows.Archive[i];
                var stamp = Line(lawArchiveContent, LedgerStyle.MonoBold, 9.5f,
                    LedgerV2.Label, 0f, y, 110f, LineBox(9.5f), "DAY " + row.Day);
                stamp.characterSpacing = 1.5f;

                var head = Line(lawArchiveContent, LedgerStyle.Serif, 10.5f, LedgerV2.Ink,
                    116f, y, PageWidth - 116f, LineBox(10.5f),
                    row.Charge + (string.IsNullOrEmpty(row.Where)
                        ? "" : "  ·  " + row.Where));
                head.overflowMode = TextOverflowModes.Ellipsis;
                y -= LineBox(10.5f);

                if (row.Lines.Count == 0)
                {
                    LedgerV2.Mono(lawArchiveContent, 116f, y, PageWidth - 116f,
                        row.Note, 8.5f, LedgerV2.Muted, 1.2f);
                    y -= LineBox(8.5f);
                }
                else
                {
                    for (var v = 0; v < row.Lines.Count; v++)
                    {
                        var line = Line(lawArchiveContent, LedgerStyle.Serif, 9.5f,
                            LedgerV2.Body, 116f, y, PageWidth - 116f, LineBox(9.5f),
                            row.Lines[v]);
                        line.overflowMode = TextOverflowModes.Ellipsis;
                        y -= LineBox(9.5f);
                    }
                    if (!string.IsNullOrEmpty(row.Note))
                    {
                        LedgerV2.Mono(lawArchiveContent, 116f, y, PageWidth - 116f,
                            row.Note, 8.5f, LedgerV2.Muted, 1.2f);
                        y -= LineBox(8.5f);
                    }
                }
                y -= 8f;
            }
            LawSettle(lawArchiveContent, lawArchiveViewport, y, ref lawArchiveScroll);
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

        // ------------------------------------------------------------------- the parts

        float LawSectionHead(RectTransform parent, string word, float w, float y)
        {
            var head = Line(parent, LedgerStyle.MonoBold, 10f, LedgerV2.Ink, 0f, y, w,
                LineBox(10f), word);
            head.characterSpacing = 3f;
            y -= LineBox(10f) + 2f;
            LedgerKit.Rule(parent, 0f, y, w, LedgerV2.Rule, 1f);
            return y - 8f;
        }

        float LawEmpty(RectTransform parent, string word, float w, float y)
        {
            LedgerV2.Mono(parent, 0f, y, w, word, 9.5f, LedgerV2.Muted, 2f);
            return y - (LineBox(9.5f) + 6f);
        }

        /// <summary>A run's height: what was drawn, never less than the window, so a
        /// short sheet does not scroll and a long one does.</summary>
        static float LawRun(float y) => Mathf.Max(-y + 8f, 0f);

        /// <summary>
        /// The run is as tall as what was drawn, and the region KEEPS ITS OWN PLACE -
        /// clamped to its own new height, so a pane that shrank comes back inside itself
        /// without dragging the pane next to it anywhere.
        /// </summary>
        static void LawSettle(RectTransform content, RectTransform viewport, float y,
            ref float scroll)
        {
            var run = LawRun(y);
            content.sizeDelta = new Vector2(0f, run);
            var window = viewport != null ? viewport.rect.height : 0f;
            scroll = Mathf.Clamp(scroll, 0f, Mathf.Max(0f, run - window));
            content.anchoredPosition = new Vector2(0f, scroll);
        }

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
