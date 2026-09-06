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
    /// THE LAW's centre panel: ONE CASE, DRAWN AS A MAP.
    ///
    /// The charge sits in the middle of a dark stage, the men named branch left, the
    /// witnesses branch right, and counsel's read hangs below. Every node is folded to a
    /// line of identity until it is clicked, and only then does it carry its description
    /// and the keys that belong to it - which is the whole argument of the redesign: the
    /// three operations appear on the man the player picked and nowhere else.
    ///
    /// GEOMETRY IS MEASURED, NEVER TYPED. Each node's height is the sum of the line
    /// boxes the faces actually print, so a lift in the book's small print or a swapped
    /// face moves the nodes instead of overflowing them; the stage is then grown to hold
    /// whatever came out and centred in the window. The design's own figures - the 96
    /// unit gutter, the 26 and 24 unit gaps, the 104 unit drop to the read, the ±52 unit
    /// reach on every bezier - are the ones kept, because they are the drawing.
    /// </summary>
    public sealed partial class PersonnelAlmanac
    {
        // ----------------------------------------------------------- the drawing's own

        /// <summary>The air between the case node and the column beside it.</summary>
        const float LawGutter = 96f;

        /// <summary>Between two men, and between two witnesses.</summary>
        const float LawManGap = 26f;
        const float LawWitnessGap = 24f;

        /// <summary>From the foot of the case node down to counsel's read.</summary>
        const float LawReadDrop = 104f;

        /// <summary>How far a link's control point reaches off each end. It is what
        /// makes the curve leave the case sideways instead of pointing at the node.</summary>
        const float LawLinkReach = 52f;

        /// <summary>The stage the map is drawn on at the reference frame. It is a floor,
        /// not a size: the stage is never smaller than its window, and never smaller
        /// than what the nodes need.</summary>
        const float LawStageW = 1300f;
        const float LawStageH = 600f;

        /// <summary>Where the middle line sits on the stage - deliberately above centre,
        /// because counsel's read hangs below the case and needs the room.</summary>
        const float LawMidShare = 250f / 600f;

        /// <summary>Node widths, folded and open, straight off the drawing.</summary>
        const float LawCaseW = 380f, LawCaseOpenW = 420f;
        const float LawManW = 262f, LawManOpenW = 330f;
        const float LawWitnessW = 244f, LawWitnessOpenW = 300f;
        const float LawReadW = 400f;

        /// <summary>Air inside a node, and the keys stacked in an open man.</summary>
        const float LawNodePadX = 11f, LawNodePadY = 9f;
        const float LawKeyH = 30f, LawKeyGap = 5f;

        /// <summary>A node's shadow on the dark stage, in the three weights the design
        /// gives them.</summary>
        static readonly Color LawCaseShadow = new Color(0f, 0f, 0f, 0.60f);
        static readonly Color LawManShadow = new Color(0f, 0f, 0f, 0.55f);
        static readonly Color LawLeafShadow = new Color(0f, 0f, 0f, 0.50f);

        /// <summary>One node, measured before anything is drawn: everything the layout
        /// needs to place it and to lay a link into it.</summary>
        struct LawNode
        {
            public float X, Y, W, H;
            public float Centre;
            public bool Open;
            public int Index;
        }

        readonly List<LawNode> lawMen = new List<LawNode>();
        readonly List<LawNode> lawWitnesses = new List<LawNode>();

        // -------------------------------------------------------------------- the paint

        void PaintLawMap(DocketRow file)
        {
            if (lawLinks)
                lawLinks.Clear();

            var window = new Vector2(lawMapViewport.rect.width,
                lawMapViewport.rect.height);

            if (file == null)
            {
                LawStageFace(window);
                LawSettleMap(lawMapContent, lawMapViewport, window, ref lawMapScroll,
                    true);
                lawCentreMap = false;
                var word = LedgerV2.Mono(lawMapContent, 0f,
                    -(window.y - LawMonoRun(10f)) * 0.5f, window.x,
                    "NO CASE AGAINST US  ·  NOTHING IS LISTED TO BE HEARD",
                    LawMonoAt(10f), LedgerStyle.RailLabel, 3f,
                    TextAlignmentOptions.Center);
                word.overflowMode = TextOverflowModes.Ellipsis;
                return;
            }

            // ---- measure everything before a single rect is placed ----

            var caseOpen = lawExpanded.Contains("case");
            var caseW = caseOpen ? LawCaseOpenW : LawCaseW;
            var caseH = LawCaseHeight(file, caseOpen);

            lawMen.Clear();
            if (file.Defendants.Count == 0)
            {
                lawMen.Add(new LawNode
                {
                    W = lawNobodyOpen ? LawManOpenW : LawManW,
                    H = LawNobodyHeight(lawNobodyOpen),
                    Open = lawNobodyOpen,
                    Index = -1,
                });
            }
            else
            {
                for (var i = 0; i < file.Defendants.Count; i++)
                {
                    var man = file.Defendants[i];
                    var open = man.CharacterId == lawOpenManId;
                    lawMen.Add(new LawNode
                    {
                        W = open ? LawManOpenW : LawManW,
                        H = LawManHeight(man, open),
                        Open = open,
                        Index = i,
                    });
                }
            }

            lawWitnesses.Clear();
            for (var i = 0; i < file.Witnesses.Count; i++)
            {
                var witness = file.Witnesses[i];
                var open = lawExpanded.Contains(LawWitnessKey(file, i));
                lawWitnesses.Add(new LawNode
                {
                    W = open ? LawWitnessOpenW : LawWitnessW,
                    H = LawWitnessHeight(witness, open),
                    Open = open,
                    Index = i,
                });
            }

            var readH = LawReadHeight(file);
            var menRun = LawColumnRun(lawMen, LawManGap);
            var witnessRun = LawColumnRun(lawWitnesses, LawWitnessGap);
            var branchW = Mathf.Max(LawWidest(lawMen), LawWidest(lawWitnesses));

            var stageW = Mathf.Max(LawStageW, window.x,
                caseW + (LawGutter + branchW) * 2f + 40f);

            // The middle line: the drawing's own share of the stage, but never so high
            // that a tall column would be cut off at the top.
            var mid = Mathf.Max(
                Mathf.Max(LawStageH, window.y) * LawMidShare,
                26f + Mathf.Max(caseH, Mathf.Max(menRun, witnessRun)) * 0.5f);

            var caseX = Mathf.Round((stageW - caseW) * 0.5f);
            var caseY = mid - caseH * 0.5f;
            var readX = Mathf.Round((stageW - LawReadW) * 0.5f);
            var readY = caseY + caseH + LawReadDrop;

            var stageH = Mathf.Max(LawStageH, window.y);
            stageH = Mathf.Max(stageH, readY + readH + 26f);
            stageH = Mathf.Max(stageH,
                mid + Mathf.Max(menRun, witnessRun) * 0.5f + 26f);

            LawStack(lawMen, mid, LawManGap, caseX, -LawGutter, right: true);
            LawStack(lawWitnesses, mid, LawWitnessGap, caseX + caseW, LawGutter,
                right: false);

            // ---- and now draw it ----

            var stage = new Vector2(stageW, stageH);
            LawStageFace(stage);
            LawSettleMap(lawMapContent, lawMapViewport, stage, ref lawMapScroll,
                lawCentreMap);
            lawCentreMap = false;

            LawDrawLinks(file, caseX, caseW, mid, caseY, caseH, readX, readY);

            if (lawMen.Count > 0)
                LawCaption("THE MEN NAMED", lawMen[0].X,
                    Mathf.Max(4f, lawMen[0].Y - 20f));
            if (lawWitnesses.Count > 0)
                LawCaption("WHO WILL GIVE EVIDENCE", lawWitnesses[0].X,
                    Mathf.Max(4f, lawWitnesses[0].Y - 20f));

            PaintLawCaseNode(file, caseX, caseY, caseW, caseH, caseOpen);

            for (var i = 0; i < lawMen.Count; i++)
            {
                var node = lawMen[i];
                if (node.Index < 0)
                    PaintLawNobodyNode(node);
                else
                    PaintLawManNode(file, file.Defendants[node.Index], node);
            }

            for (var i = 0; i < lawWitnesses.Count; i++)
                PaintLawWitnessNode(file, file.Witnesses[lawWitnesses[i].Index],
                    lawWitnesses[i]);

            PaintLawReadNode(file, readX, readY, readH);
        }

        // ------------------------------------------------------------- the stage itself

        /// <summary>The dark ground the map is drawn on, with the lamp over it. Pushed
        /// to the back of the run so the links - which are built once and kept - still
        /// lie over it and under every node.</summary>
        void LawStageFace(Vector2 stage)
        {
            var face = NewRect("Stage", lawMapContent);
            PlaceTopLeft(face, 0f, 0f, stage.x, stage.y);
            Fill(face, LedgerStyle.Rail);

            var lamp = NewRect("Lamp", face);
            var reach = Mathf.Max(stage.x, stage.y) * 1.2f;
            PlaceTopLeft(lamp, (stage.x - reach) * 0.5f, -(stage.y * 0.45f - reach * 0.5f),
                reach, reach);
            var light = lamp.gameObject.AddComponent<RawImage>();
            light.texture = LedgerStyle.RadialLight;
            light.color = LedgerV2.At(LedgerStyle.Lamp, 0.10f);
            light.raycastTarget = false;

            face.SetAsFirstSibling();
        }

        void LawCaption(string word, float x, float y)
        {
            var caption = Line(lawMapContent, LedgerStyle.Condensed, LawCondAt(8.6f),
                LedgerStyle.RailLabel, x, -y, 260f, LawCondRun(8.6f),
                word.ToUpperInvariant());
            caption.characterSpacing = 14f;
        }

        // ------------------------------------------------------------------- the links

        /// <summary>
        /// Every link, drawn UNDER the nodes and taking no clicks. A witness who will
        /// not be heard is a BROKEN line - the one thing on the map a player reads
        /// without opening anything.
        /// </summary>
        void LawDrawLinks(DocketRow file, float caseX, float caseW, float mid,
            float caseY, float caseH, float readX, float readY)
        {
            if (!lawLinks)
                return;

            for (var i = 0; i < lawMen.Count; i++)
            {
                var node = lawMen[i];
                var picked = node.Index >= 0 &&
                             file.Defendants[node.Index].CharacterId == lawSelectedManId;
                var edge = node.X + node.W;
                lawLinks.AddCurve(
                    new Vector2(caseX, mid),
                    new Vector2(caseX - LawLinkReach, mid),
                    new Vector2(edge + LawLinkReach, node.Centre),
                    new Vector2(edge, node.Centre),
                    picked ? LedgerStyle.RailRed : LedgerStyle.RailLabel,
                    picked ? 2.4f : 1.4f);
            }

            for (var i = 0; i < lawWitnesses.Count; i++)
            {
                var node = lawWitnesses[i];
                var witness = file.Witnesses[node.Index];
                var heard = LawWitnessHeard(witness);
                lawLinks.AddCurve(
                    new Vector2(caseX + caseW, mid),
                    new Vector2(caseX + caseW + LawLinkReach, mid),
                    new Vector2(node.X - LawLinkReach, node.Centre),
                    new Vector2(node.X, node.Centre),
                    LawOnRail(LawWitnessTone(witness)), 1.4f,
                    heard ? 0f : 5f, heard ? 0f : 4f);
            }

            lawLinks.AddLine(new Vector2(caseX + caseW * 0.5f, caseY + caseH),
                new Vector2(readX + LawReadW * 0.5f, readY),
                LedgerStyle.RailRed, 1.4f);
        }

        // --------------------------------------------------------------- the case node

        float LawCaseHeight(DocketRow file, bool open)
        {
            var h = LawNodePadY + 3f
                    + LawMonoRun(7.8f) + 3f
                    + LawCondRun(26f) + 3f
                    + LawMonoRun(9.5f)
                    + 6f + LawMonoRun(8.7f)
                    + LawNodePadY + 3f;
            if (open)
                h += 7f + 1f + 7f + LawSerifRun(13.5f, 3) + 3f + LawMonoRun(8.5f);
            return h;
        }

        void PaintLawCaseNode(DocketRow file, float x, float y, float w, float h,
            bool open)
        {
            var node = NewRect("Case " + file.File.CaseId, lawMapContent);
            PlaceTopLeft(node, x, -y, w, h);
            Fill(node, LedgerV2.Head);
            Frame(node, 1f, LedgerStyle.RailSafeGold);
            ShadowUnder(node, 12f, LawCaseShadow);

            var pad = 14f;
            var inner = w - pad * 2f;
            var top = -(LawNodePadY + 3f);

            var badge = LedgerV2.Mono(node, pad, top, inner * 0.62f,
                "FOLIO 17  ·  THE STATE'S CASE", LawMonoAt(7.8f), LedgerV2.HeadDim, 5f);
            badge.font = LedgerStyle.MonoBold;
            badge.overflowMode = TextOverflowModes.Ellipsis;

            LedgerV2.Mono(node, pad + inner * 0.62f, top, inner * 0.38f,
                open ? "CLICK TO FOLD" : "CLICK FOR THE FILE", LawMonoAt(7.5f),
                LedgerStyle.RailSafeGold, 4f, TextAlignmentOptions.MidlineRight);
            top -= LawMonoRun(7.8f) + 3f;

            var charge = Line(node, LedgerStyle.Condensed, LawCondAt(26f),
                LedgerV2.HeadCream, pad, top, inner, LawCondRun(26f),
                file.Charge.ToUpperInvariant());
            charge.characterSpacing = 0.5f;
            charge.overflowMode = TextOverflowModes.Ellipsis;
            top -= LawCondRun(26f) + 3f;

            var where = LedgerV2.Mono(node, pad, top, inner,
                file.Where.ToUpperInvariant(), LawMonoAt(9.5f), LedgerV2.HeadInk, 4f);
            where.font = LedgerStyle.MonoBold;
            where.overflowMode = TextOverflowModes.Ellipsis;
            top -= LawMonoRun(9.5f);

            if (open)
            {
                top -= 7f;
                Block("Rule", node, pad, top, inner, 1f, LedgerStyle.ChromeRule);
                top -= 1f + 7f;

                var story = Paragraph(node, LedgerStyle.Serif, LawSerifAt(13.5f),
                    LedgerStyle.RailBright, pad, top, inner, LawSerifRun(13.5f, 3),
                    LawCaseStory(file));
                story.overflowMode = TextOverflowModes.Truncate;
                top -= LawSerifRun(13.5f, 3) + 3f;

                LedgerV2.Mono(node, pad, top, inner, LawMapHeadRight(file),
                    LawMonoAt(8.5f), LedgerStyle.RailLabel, 1.5f);
                top -= LawMonoRun(8.5f);
            }

            top -= 6f;
            LedgerV2.Mono(node, pad, top, inner, LawCaseMeta(file), LawMonoAt(8.7f),
                LawOnRail(LawCourtTone(file)), 1.5f);

            RowButton(node, ClickSurface(node), () => LawToggle("case"));
        }

        /// <summary>What the file says when it is opened - and it says it in the same
        /// words the tab and the head band do, only joined up.</summary>
        string LawCaseStory(DocketRow file)
        {
            var listed = file.NobodyTaken || file.CourtDay <= 0
                ? "Nothing is listed to be heard — nobody of ours has been taken on it yet."
                : "Listed to be heard on day " + file.CourtDay + ".";

            var give = 0;
            var heard = 0;
            for (var i = 0; i < file.Witnesses.Count; i++)
            {
                if (file.Witnesses[i].Witness.Standing != WitnessStanding.WillTestify)
                    continue;
                give++;
                if (file.Witnesses[i].WillBeHeard) heard++;
            }

            var evidence = give == 0
                ? "Nobody will give evidence."
                : give + " will give evidence · " +
                  (heard > 0 ? heard + " will be heard." : "none will be heard.");

            return "Opened day " + file.OpenedDay + ". " + listed + " " + evidence;
        }

        string LawCaseMeta(DocketRow file)
        {
            if (file.NobodyTaken || file.CourtDay <= 0)
                return "ON THE DOCKET  ·  NOBODY TAKEN" +
                       (file.Counts > 0
                           ? "  ·  +" + file.Counts +
                             (file.Counts == 1 ? " COUNT" : " COUNTS") : "");
            return "OPENED DAY " + file.OpenedDay + "  ·  COURT DAY " + file.CourtDay +
                   (file.DaysToCourt > 0
                       ? " (" + file.DaysToCourt +
                         (file.DaysToCourt == 1 ? " DAY)" : " DAYS)")
                       : "") +
                   (file.Counts > 0
                       ? "  ·  +" + file.Counts +
                         (file.Counts == 1 ? " COUNT" : " COUNTS") : "");
        }

        // ---------------------------------------------------------------- a man's node

        float LawManHeight(DefendantRow man, bool open)
        {
            if (!open)
                return LawNodePadY * 2f + Mathf.Max(40f,
                    LawCondRun(15f) + LawMonoRun(8.2f));

            var head = Mathf.Max(80f,
                LawCondRun(19f) + LawMonoRun(8.5f) + LawMonoRun(7.5f));
            var answer = string.IsNullOrEmpty(man.Answer)
                ? 0f : LawSerifRun(11.5f) + 5f;
            return LawNodePadY * 2f + head + 7f + LawSerifRun(12.5f, 3) + answer + 7f +
                   LawKeyH * 3f + LawKeyGap * 2f;
        }

        void PaintLawManNode(DocketRow file, DefendantRow man, LawNode node)
        {
            var picked = man.CharacterId == lawSelectedManId;
            var rect = NewRect("Man " + man.CharacterId, lawMapContent);
            PlaceTopLeft(rect, node.X, -node.Y, node.W, node.H);
            Fill(rect, picked ? LedgerV2.Picked : LedgerV2.PanelBand);
            Frame(rect, picked ? 2f : 1f, picked ? LedgerV2.Red : LedgerV2.Rule);
            ShadowUnder(rect, 11f, LawManShadow);
            rect.gameObject.AddComponent<RectMask2D>();

            var member = director != null && director.Roster != null
                ? director.Roster.Find(man.CharacterId) : null;
            var hiding = man.Stage == LawSheet.Hiding;
            var stageLine = man.Stage.ToUpperInvariant() +
                            (man.Bail > 0
                                ? "  ·  BAIL " + LedgerText.Cash(man.Bail)
                                : "  ·  NO BAIL");
            var stageTone = hiding ? LedgerV2.Red : LedgerV2.Muted;

            if (!node.Open)
            {
                const float plateW = 34f;
                const float plateH = 40f;
                var plate = LedgerV2.PortraitPlate(rect, LawNodePadX,
                    -(node.H - plateH) * 0.5f, plateW, plateH, InitialsOf(man.Name),
                    LedgerV2.DarkPlate, LedgerV2.DarkPlateInk);
                if (member != null)
                    PortraitStudio.Request(MemberModel(member),
                        PortraitStudio.Framing.Bust, plate);

                var textX = LawNodePadX + plateW + 10f;
                var textW = node.W - textX - LawNodePadX;
                var top = -(node.H - LawCondRun(15f) - LawMonoRun(8.2f)) * 0.5f;

                var name = Line(rect, LedgerStyle.Condensed, LawCondAt(15f),
                    LedgerV2.Ink, textX, top, textW, LawCondRun(15f), man.Name);
                name.overflowMode = TextOverflowModes.Ellipsis;

                var stage = LedgerV2.Mono(rect, textX, top - LawCondRun(15f), textW,
                    stageLine, LawMonoAt(8.2f), stageTone, 1.5f);
                stage.font = LedgerStyle.MonoBold;
                stage.overflowMode = TextOverflowModes.Ellipsis;
            }
            else
            {
                const float plateW = 66f;
                const float plateH = 80f;
                var plate = LedgerV2.PortraitPlate(rect, LawNodePadX, -LawNodePadY,
                    plateW, plateH, InitialsOf(man.Name), LedgerV2.DarkPlate,
                    LedgerV2.DarkPlateInk);
                if (member != null)
                    PortraitStudio.Request(MemberModel(member),
                        PortraitStudio.Framing.Bust, plate);

                var textX = LawNodePadX + plateW + 11f;
                var textW = node.W - textX - LawNodePadX;
                var top = -LawNodePadY;

                var name = Line(rect, LedgerStyle.Condensed, LawCondAt(19f),
                    LedgerV2.Ink, textX, top, textW, LawCondRun(19f), man.Name);
                name.overflowMode = TextOverflowModes.Ellipsis;
                LawOpenFile(rect, textX, top, textW, LawCondRun(19f), man.CharacterId);
                top -= LawCondRun(19f);

                var stage = LedgerV2.Mono(rect, textX, top, textW, stageLine,
                    LawMonoAt(8.5f), stageTone, 1.5f);
                stage.font = LedgerStyle.MonoBold;
                stage.overflowMode = TextOverflowModes.Ellipsis;
                top -= LawMonoRun(8.5f);

                LedgerV2.Mono(rect, textX, top, textW, "CLICK TO FOLD",
                    LawMonoAt(7.5f), LedgerV2.Faint, 4f);

                var inner = node.W - LawNodePadX * 2f;
                var body = -(LawNodePadY + Mathf.Max(80f,
                    LawCondRun(19f) + LawMonoRun(8.5f) + LawMonoRun(7.5f)) + 7f);

                var story = Paragraph(rect, LedgerStyle.Serif, LawSerifAt(12.5f),
                    LedgerV2.Body, LawNodePadX, body, inner, LawSerifRun(12.5f, 3),
                    LawManStory(file, man));
                story.overflowMode = TextOverflowModes.Truncate;
                body -= LawSerifRun(12.5f, 3);

                if (!string.IsNullOrEmpty(man.Answer))
                {
                    body -= 5f;
                    var answer = Line(rect, LedgerStyle.SerifItalic, LawSerifAt(11.5f),
                        man.Answer.Contains("sprung")
                            ? LedgerV2.Red : LedgerV2.PaperBlue,
                        LawNodePadX, body, inner, LawSerifRun(11.5f), man.Answer);
                    answer.overflowMode = TextOverflowModes.Ellipsis;
                    body -= LawSerifRun(11.5f);
                }

                // THE THREE KEYS, stacked, at the foot of the node - and the SAME desk
                // the man's own file calls. A key he cannot press is drawn outline
                // rather than taken away: a row that has vanished tells nobody why.
                var keys = -(node.H - LawNodePadY - LawKeyH * 3f - LawKeyGap * 2f);
                LedgerV2.Button(rect, "POST BAIL", LawNodePadX, keys, inner, LawKeyH,
                    () => LawPostBail(man), red: false, size: 10.5f,
                    outline: !man.CanPostBail);
                LedgerV2.Button(rect, "SKIP BAIL", LawNodePadX,
                    keys - (LawKeyH + LawKeyGap), inner, LawKeyH,
                    () => LawSkipBail(man), red: true, size: 10.5f,
                    outline: !man.CanSkipBail);
                LedgerV2.Button(rect, "CUT HIM LOOSE", LawNodePadX,
                    keys - (LawKeyH + LawKeyGap) * 2f, inner, LawKeyH,
                    () => LawCutLoose(man), red: true, size: 10.5f,
                    outline: !man.CanCutLoose);
            }

            var id = man.CharacterId;
            RowButton(rect, ClickSurface(rect), () => LawPickMan(id));
        }

        /// <summary>
        /// Takes a man in hand, and opens his file - ONE at a time. Clicking the man
        /// whose file already stands open folds it and leaves him in hand; clicking any
        /// other man folds whatever was open and opens him instead, so the three keys
        /// are never drawn on a man the red link is not pointing at.
        /// </summary>
        void LawPickMan(int characterId)
        {
            lawOpenManId = lawOpenManId == characterId ? -1 : characterId;
            lawSelectedManId = characterId;
            lawNobodyOpen = false;
            dirty = true;
        }

        /// <summary>Why he is on this file, in the file's own words. Nothing here is a
        /// state word: the stage, the bail and the charge all come from their own
        /// tables, and this only joins them into a sentence.</summary>
        string LawManStory(DocketRow file, DefendantRow man)
        {
            var named = "Named on the " + file.Charge.ToLowerInvariant() +
                        (string.IsNullOrEmpty(file.Where)
                            ? ". " : " at " + file.Where.ToLowerInvariant() + ". ");
            if (man.Stage == LawSheet.Hiding)
                return named + "The city has a warrant out, and no bail can be set on a " +
                       "man it does not hold.";
            if (man.Stage == LedgerText.StageLabel(PrisonStage.Bailed))
                return named + "He is out on " + LedgerText.Cash(man.Bail) +
                       " of the house's money until the court sits.";
            if (man.Bail > 0)
                return named + "Held since day " + file.OpenedDay + "; the court will " +
                       "take " + LedgerText.Cash(man.Bail) + " to let him walk until " +
                       "it sits.";
            return named + "Held since day " + file.OpenedDay +
                   "; there is no bail to be had on this one.";
        }

        // ----------------------------------------------------- and when nobody was taken

        float LawNobodyHeight(bool open)
        {
            var h = LawNodePadY * 2f + LawCondRun(15f) + LawMonoRun(8.2f);
            if (open)
                h += 7f + LawSerifRun(12.5f, 2);
            return h;
        }

        /// <summary>A complaint on the docket with nobody of ours taken on it. It is
        /// DASHED, because it is not a man - it is a count waiting to happen, and the
        /// map must not read as though the state had somebody.</summary>
        void PaintLawNobodyNode(LawNode node)
        {
            var rect = NewRect("Nobody taken", lawMapContent);
            PlaceTopLeft(rect, node.X, -node.Y, node.W, node.H);
            Fill(rect, LedgerV2.PanelBand);
            ShadowUnder(rect, 11f, LawManShadow);

            DottedRule(rect, 0f, 0f, node.W, LedgerV2.PaperBlue);
            DottedRule(rect, 0f, -(node.H - 1f), node.W, LedgerV2.PaperBlue);
            DottedVRule(rect, 0f, 0f, node.H, LedgerV2.PaperBlue);
            DottedVRule(rect, node.W - 1f, 0f, node.H, LedgerV2.PaperBlue);

            var inner = node.W - LawNodePadX * 2f;
            var top = -LawNodePadY;
            var name = Line(rect, LedgerStyle.Condensed, LawCondAt(15f), LedgerV2.Ink,
                LawNodePadX, top, inner, LawCondRun(15f), "Nobody taken");
            name.overflowMode = TextOverflowModes.Ellipsis;
            top -= LawCondRun(15f);

            var stage = LedgerV2.Mono(rect, LawNodePadX, top, inner,
                "ON THE DOCKET  ·  NOBODY TAKEN", LawMonoAt(8.2f), LedgerV2.PaperBlue,
                1.5f);
            stage.font = LedgerStyle.MonoBold;
            top -= LawMonoRun(8.2f);

            if (node.Open)
            {
                top -= 7f;
                var story = Paragraph(rect, LedgerStyle.Serif, LawSerifAt(12.5f),
                    LedgerV2.Muted, LawNodePadX, top, inner, LawSerifRun(12.5f, 2),
                    "Nobody of ours has been taken on this one. It sits on the docket " +
                    "as a complaint.");
                story.overflowMode = TextOverflowModes.Truncate;
            }

            RowButton(rect, ClickSurface(rect), () =>
            {
                lawNobodyOpen = !lawNobodyOpen;
                dirty = true;
            });
        }

        // ------------------------------------------------------------ a witness's node

        static string LawWitnessKey(DocketRow file, int index) =>
            file.File.CaseId + "w" + index;

        static bool LawWitnessHeard(WitnessRow witness) =>
            witness.Witness.Standing == WitnessStanding.WillTestify &&
            witness.WillBeHeard;

        /// <summary>The standing's own tone: a man the court will hear is the worst news
        /// on the map, a man who will talk to no purpose is the middle one, and a man
        /// who will not stand up at all is the good one.</summary>
        static Color LawWitnessTone(WitnessRow witness)
        {
            if (witness.Witness.Standing != WitnessStanding.WillTestify)
                return LedgerV2.Green;
            return witness.WillBeHeard ? LedgerV2.Red : LedgerV2.Amber;
        }

        float LawWitnessHeight(WitnessRow witness, bool open)
        {
            var h = LawNodePadY * 2f + LawSerifRun(12.5f) + 5f + LawMonoRun(8.2f);
            if (open)
                h += 7f + 1f + 7f + LawSerifRun(12f, 2) +
                     (witness.CanLeanOn ? LawKeyGap + LawKeyH : 0f);
            return h;
        }

        void PaintLawWitnessNode(DocketRow file, WitnessRow witness, LawNode node)
        {
            var tone = LawWitnessTone(witness);
            var rect = NewRect("Witness " + node.Index, lawMapContent);
            PlaceTopLeft(rect, node.X, -node.Y, node.W, node.H);
            Fill(rect, LedgerV2.PanelBand);
            ShadowUnder(rect, 10f, LawLeafShadow);
            Block("Edge", rect, 0f, 0f, 4f, node.H, tone);
            rect.gameObject.AddComponent<RectMask2D>();

            var x = LawNodePadX;
            var inner = node.W - x - LawNodePadX;
            var top = -LawNodePadY;

            var kind = Line(rect, LedgerStyle.Serif, LawSerifAt(12.5f), LedgerV2.Body,
                x, top, inner, LawSerifRun(12.5f), witness.Kind);
            kind.overflowMode = TextOverflowModes.Ellipsis;
            top -= LawSerifRun(12.5f) + 5f;

            var standing = LedgerV2.Mono(rect, x, top, inner,
                witness.Standing.ToUpperInvariant(), LawMonoAt(8.2f), tone, 1.5f);
            standing.overflowMode = TextOverflowModes.Ellipsis;
            top -= LawMonoRun(8.2f);

            if (node.Open)
            {
                top -= 7f;
                Block("Rule", rect, x, top, inner, 1f, LedgerV2.Hair);
                top -= 1f + 7f;

                var story = Paragraph(rect, LedgerStyle.Serif, LawSerifAt(12f),
                    LedgerV2.Muted, x, top, inner, LawSerifRun(12f, 2),
                    LawWitnessStory(witness));
                story.overflowMode = TextOverflowModes.Truncate;
                top -= LawSerifRun(12f, 2);

                if (witness.CanLeanOn)
                {
                    var seed = witness.Witness.Seed;
                    var at = new Vector3(witness.Witness.X, witness.Witness.Y,
                        witness.Witness.Z);
                    LedgerV2.Button(rect, "LEAN ON HIM", x, top - LawKeyGap, inner,
                        LawKeyH, () => LeanOnWitness(seed, at), LedgerV2.Key.Red);
                }
            }

            var key = LawWitnessKey(file, node.Index);
            RowButton(rect, ClickSurface(rect), () => LawToggle(key));
        }

        static string LawWitnessStory(WitnessRow witness)
        {
            if (LawWitnessHeard(witness))
                return "The court will hear him, and his word carries further than ours.";
            if (witness.Witness.Standing == WitnessStanding.WillTestify)
                return "He will talk, but the court has no reason to listen to him.";
            return "He will not stand up in court. Nothing of his gets in.";
        }

        // --------------------------------------------------------- counsel's read node

        float LawReadHeight(DocketRow file) =>
            11f + LawCondRun(8.6f) + 3f + LawSerifRun(18f, 2) + 6f +
            LawMonoRun(8.5f) + 11f;

        void PaintLawReadNode(DocketRow file, float x, float y, float h)
        {
            var rect = NewRect("Counsel says", lawMapContent);
            PlaceTopLeft(rect, x, -y, LawReadW, h);
            Fill(rect, LedgerV2.PanelBand);
            ShadowUnder(rect, 10f, LawLeafShadow);
            Block("Edge", rect, 0f, 0f, 4f, h, LedgerV2.Red);

            var pad = 13f;
            var inner = LawReadW - pad * 2f;
            var top = -11f;

            var label = Line(rect, LedgerStyle.Condensed, LawCondAt(8.6f),
                LedgerStyle.InkLabel, pad, top, inner, LawCondRun(8.6f),
                "COUNSEL SAYS");
            label.characterSpacing = 14f;
            top -= LawCondRun(8.6f) + 3f;

            // WORDS, NEVER A NUMBER, and never re-worded here: Verdict.Leaning's four
            // bands and the two certainties beside them are the one table the street
            // banner reads from too.
            var read = Paragraph(rect, LedgerStyle.Serif, LawSerifAt(18f),
                LawReadTone(file.Read), pad, top, inner, LawSerifRun(18f, 2), file.Read);
            read.overflowMode = TextOverflowModes.Truncate;
            top -= LawSerifRun(18f, 2) + 6f;

            var counsel = lawRows.Counsel;
            LedgerV2.Mono(rect, pad, top, inner,
                counsel.Has
                    ? "COUNSEL  ·  " + counsel.Name.ToUpperInvariant() + "  ·  " +
                      LedgerText.Cash(counsel.Wage) + " A DAY"
                    : "NO COUNSEL — NO BAIL HEARING WILL BE LISTED",
                LawMonoAt(8.5f), counsel.Has ? LedgerV2.Body : LedgerV2.Red, 1.5f);
        }

        // ------------------------------------------------------------------- the stack

        /// <summary>A column's whole height, gaps and all.</summary>
        static float LawColumnRun(List<LawNode> column, float gap)
        {
            if (column.Count == 0)
                return 0f;
            var run = gap * (column.Count - 1);
            for (var i = 0; i < column.Count; i++)
                run += column[i].H;
            return run;
        }

        static float LawWidest(List<LawNode> column)
        {
            var widest = 0f;
            for (var i = 0; i < column.Count; i++)
                widest = Mathf.Max(widest, column[i].W);
            return widest;
        }

        /// <summary>
        /// Stacks a branch on the middle line. A column is CENTRED on the middle rather
        /// than started at it, so a single man hangs level with the charge and three
        /// spread evenly either side of it; the right-hand column of the two is
        /// right-aligned to its gutter, so the ends of the links stand in a line however
        /// wide each node happens to be.
        /// </summary>
        static void LawStack(List<LawNode> column, float mid, float gap, float edge,
            float gutter, bool right)
        {
            var y = mid - LawColumnRun(column, gap) * 0.5f;
            for (var i = 0; i < column.Count; i++)
            {
                var node = column[i];
                node.X = right ? edge + gutter - node.W : edge + gutter;
                node.Y = y;
                node.Centre = y + node.H * 0.5f;
                column[i] = node;
                y += node.H + gap;
            }
        }

        /// <summary>Folds a node open or shut. Nothing else on the sheet moves - the
        /// stage is re-measured at the next paint and every other node keeps its
        /// place.</summary>
        void LawToggle(string key)
        {
            if (!lawExpanded.Remove(key))
                lawExpanded.Add(key);
            dirty = true;
        }
    }
}
