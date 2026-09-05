using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using LivingCity.Outfit;
using static LivingCity.UI.LedgerKit;

namespace LivingCity.UI
{
    /// <summary>
    /// DIRECTION A - THE TABLE. A relationship map on a table tilted ten degrees away
    /// from the reader.
    ///
    /// THE GEOMETRY. The design is a fixed 1120 x 720 plane, scaled to fit the stage and
    /// tipped back 10 degrees, with everything that must be READ counter-rotated flat to
    /// the viewer and depth carried by translateZ. A screen-space canvas has no
    /// perspective of its own, so the projection is done here rather than asked of the
    /// renderer: <see cref="ProjectTable"/> tips a plane point, lifts it, divides it by
    /// the design's 1700 units of perspective and answers where it lands and how much
    /// bigger it got. A viewer-facing quad has one depth for all four corners, so one
    /// uniform scale is EXACT rather than an approximation; a card lying flat on the
    /// table takes the tilt's cosine on its height as well. The only thing given up is
    /// the sub-one-percent keystone on a flat card, which no reader can see at ten
    /// degrees.
    ///
    /// THE MOTION. Nothing tweens itself in uGUI, so the two progresses this screen
    /// needs - a card standing UP and a word OPENING - are advanced in
    /// <see cref="TickFamilies"/> along the design's own cubic-bezier(.2,.8,.2,1), and
    /// every animated piece is placed from them. No repaint is taken for a frame of
    /// motion: the plane is built once and afterwards only re-placed.
    ///
    /// WHERE THE DESIGN'S NUMBERS WERE GROWN. The book lifts its small print 15%
    /// (<see cref="LedgerKit.BookSize"/>) because the ledger is read at a greater
    /// distance than a street card, and that rule is older than this sheet. Structural
    /// widths are the design's to the unit - 252, 274, 180, 176, 374 - and only the
    /// boxes that HOLD type were grown to match what actually prints. The card is 92
    /// rather than 70 for the same reason plus the design's own rule that the situation
    /// line must never be truncated.
    /// </summary>
    public sealed partial class PersonnelAlmanac
    {
        // ------------------------------------------------------------------ the plane

        const float PlaneW = 1120f;
        const float PlaneH = 720f;
        const float PlaneCx = 560f;
        const float PlaneCy = 344f;

        /// <summary>The whole of the third dimension: ten degrees of tilt and 1700
        /// units of perspective.</summary>
        const float PlaneTilt = 10f;
        const float PlaneDepth = 1700f;

        static readonly float TiltCos = Mathf.Cos(PlaneTilt * Mathf.Deg2Rad);
        static readonly float TiltSin = Mathf.Sin(PlaneTilt * Mathf.Deg2Rad);

        const float HouseCardW = 252f;
        const float HouseCardH = 92f;

        const float DossierW = 274f;
        const float ChipsW = 180f;
        const float ShutW = 176f;
        const float WordingW = 374f;
        const float ChipH = 34f;
        const float ChipGap = 7f;

        /// <summary>How far off the table each standee stands. The chips column rises
        /// further while a word is open, so its wording clears the dossier.</summary>
        const float LiftDossier = 70f;
        const float LiftChips = 132f;
        const float LiftChipsOpen = 168f;
        const float LiftShut = 24f;

        /// <summary>The design's five hand-placed seats. A city that deals a different
        /// number of houses gets an ellipse through the same envelope instead - the
        /// table is a table however many chairs stand round it.</summary>
        static readonly Vector2[] FiveSeats =
        {
            new Vector2(200f, 152f), new Vector2(920f, 152f), new Vector2(978f, 430f),
            new Vector2(800f, 620f), new Vector2(212f, 596f),
        };

        // ------------------------------------------------------------------ the state

        /// <summary>Which of the open words is expanded into its wording, or -1.
        /// </summary>
        int tableMove = -1;

        readonly List<TableMove> tableOpen = new List<TableMove>();
        readonly List<ShutMove> tableShutList = new List<ShutMove>();
        readonly HouseReading tableRead = new HouseReading();
        readonly HouseReading tableCardRead = new HouseReading();
        readonly TableContext tableCtx = new TableContext();

        RectTransform tableStage;
        float tableFit = 1f;

        /// <summary>A piece whose only motion is a fade, and the two alphas it fades
        /// between as its progress runs 0 to 1.</summary>
        readonly struct TableFade
        {
            public TableFade(CanvasGroup group, float rest, float open)
            {
                Group = group;
                Rest = rest;
                Open = open;
            }

            public CanvasGroup Group { get; }
            public float Rest { get; }
            public float Open { get; }
        }

        /// <summary>A panel standing off the table: where its bottom edge sits on the
        /// plane, and how far it lifts when it is fully up.</summary>
        sealed class TableStandee
        {
            public RectTransform Root;
            public float Bx;
            public float By;
            public float Lift;
            public float LiftOpen = -1f;
        }

        readonly List<TableFade> tableRiseFades = new List<TableFade>();
        readonly List<TableFade> tableWordFades = new List<TableFade>();
        readonly List<TableStandee> tableStandees = new List<TableStandee>();

        /// <summary>0 while every card lies flat, 1 with one standing. The second is
        /// the wording's own progress.</summary>
        float tableRise;
        float tableRiseFrom;
        float tableRiseTo;
        float tableRiseT = 1f;

        float tableWordOpen;
        float tableWordFrom;
        float tableWordTo;
        float tableWordT = 1f;

        void ForgetTablePieces()
        {
            tableRiseFades.Clear();
            tableWordFades.Clear();
            tableStandees.Clear();
            tableStage = null;
        }

        // ------------------------------------------------------------- the projection

        /// <summary>
        /// A point on the design plane, lifted <paramref name="z"/> off it, as it lands
        /// on the screen: the offset from the stage's centre in canvas units, and the
        /// factor everything at that depth is drawn bigger by.
        /// </summary>
        void ProjectTable(float px, float py, float z, out Vector2 at, out float k)
        {
            var u = (px - PlaneCx) * tableFit;
            var v = (py - PlaneCy) * tableFit;
            var lift = z * tableFit;
            var y = v * TiltCos - lift * TiltSin;
            var depth = v * TiltSin + lift * TiltCos;
            k = PlaneDepth / Mathf.Max(1f, PlaneDepth - depth);
            at = new Vector2(u * k, -y * k);
        }

        /// <summary>cubic-bezier(.2,.8,.2,1) - the one curve every piece of this screen
        /// moves on, solved for y at a given x rather than approximated by something
        /// that merely eases out.</summary>
        static float TableEase(float t)
        {
            t = Mathf.Clamp01(t);
            const float x1 = 0.2f;
            const float x2 = 0.2f;
            const float y1 = 0.8f;
            const float y2 = 1f;
            var u = t;
            for (var i = 0; i < 6; i++)
            {
                var s = 1f - u;
                var x = 3f * s * s * u * x1 + 3f * s * u * u * x2 + u * u * u;
                var slope = 3f * s * s * x1 + 6f * s * u * (x2 - x1) + 3f * u * u * (1f - x2);
                if (slope < 1e-4f)
                    break;
                u = Mathf.Clamp01(u - (x - t) / slope);
            }
            var r = 1f - u;
            return 3f * r * r * u * y1 + 3f * r * u * u * y2 + u * u * u;
        }

        /// <summary>
        /// One frame of the table's motion. Called from the book's own Update; it takes
        /// no repaint, only re-places what is already built.
        /// </summary>
        void TickFamilies()
        {
            if (currentPage != LedgerPage.Diplomacy || tableStage == null)
                return;

            var moved = false;
            if (tableRiseT < 1f)
            {
                tableRiseT = Mathf.Min(1f, tableRiseT + Time.unscaledDeltaTime / 0.55f);
                tableRise = Mathf.LerpUnclamped(tableRiseFrom, tableRiseTo,
                    TableEase(tableRiseT));
                moved = true;
            }
            if (tableWordT < 1f)
            {
                tableWordT = Mathf.Min(1f, tableWordT + Time.unscaledDeltaTime / 0.34f);
                tableWordOpen = Mathf.LerpUnclamped(tableWordFrom, tableWordTo,
                    TableEase(tableWordT));
                moved = true;
            }
            if (moved)
                PlaceTableMotion();
        }

        void AimTableRise(float to)
        {
            if (Mathf.Approximately(tableRiseTo, to))
                return;
            tableRiseFrom = tableRise;
            tableRiseTo = to;
            tableRiseT = 0f;
        }

        void AimTableWord(float to)
        {
            if (Mathf.Approximately(tableWordTo, to))
                return;
            tableWordFrom = tableWordOpen;
            tableWordTo = to;
            tableWordT = 0f;
        }

        /// <summary>Every animated piece placed from the two progresses. The standees
        /// carry the depth; everything else carries a fade.</summary>
        void PlaceTableMotion()
        {
            for (var i = 0; i < tableRiseFades.Count; i++)
            {
                var fade = tableRiseFades[i];
                if (fade.Group)
                    fade.Group.alpha = Mathf.Lerp(fade.Rest, fade.Open, tableRise);
            }
            for (var i = 0; i < tableWordFades.Count; i++)
            {
                var fade = tableWordFades[i];
                if (fade.Group)
                    fade.Group.alpha = Mathf.Lerp(fade.Rest, fade.Open, tableWordOpen);
            }
            for (var i = 0; i < tableStandees.Count; i++)
            {
                var standee = tableStandees[i];
                if (!standee.Root)
                    continue;
                var lift = standee.LiftOpen >= 0f
                    ? Mathf.Lerp(standee.Lift, standee.LiftOpen, tableWordOpen)
                    : standee.Lift;
                ProjectTable(standee.Bx, standee.By, lift * tableRise, out var at, out var k);
                standee.Root.anchoredPosition = at;
                var scale = tableFit * k;
                standee.Root.localScale = new Vector3(scale, scale, 1f);
            }
        }

        // -------------------------------------------------------------- the build pass

        /// <summary>
        /// The table, dealt. The stage is a desk; the plane is fitted to it; the lines
        /// are drawn before the cards so a card always sits on top of its own standing;
        /// and the open house's three standees are built last, over everything.
        /// </summary>
        void BuildTheTable(IReadOnlyList<Gangs.Gang> gangs)
        {
            tableStage = NewRect("Stage", diplomacyContent);
            PlaceTopLeft(tableStage, 0f, StageTop, SheetW, StageH);
            tableStage.gameObject.AddComponent<RectMask2D>();

            // The desk is the PAGE's - BuildFamiliesRoom laid it under the head and the
            // foot as well, because the design's whole screen is one room. The stage
            // only masks and takes the press: touching the table lays the open card down.
            var felt = NewRect("Table", tableStage);
            Stretch(felt);
            RowButton(felt, ClickSurface(felt), CloseTheCard);

            tableFit = Mathf.Clamp(
                Mathf.Min(SheetW / (PlaneW + 40f), StageH / (PlaneH + 20f)), 0.42f, 1f);

            var plane = NewRect("Plane", tableStage);
            plane.anchorMin = plane.anchorMax = new Vector2(0.5f, 0.5f);
            plane.pivot = new Vector2(0.5f, 0.5f);
            plane.anchoredPosition = Vector2.zero;
            plane.sizeDelta = new Vector2(SheetW, StageH);

            // ---- who sits where ----
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

            var seats = Seats(rivals.Count);
            var middle = tableFocus >= 0 ? Find(rivals, tableFocus) : us;
            if (middle == null)
            {
                tableFocus = -1;
                middle = us;
            }

            var world = Underworld.Current;
            var day = outfit ? outfit.Campaign.Day : 1;
            var mine = Gangs.GangCatalog.PlayerGangId;

            // The ring: the rivals in their seats, with our own card taking the seat of
            // whichever house has been called into the middle.
            var ring = new List<Gangs.Gang>(rivals.Count);
            for (var i = 0; i < rivals.Count; i++)
                ring.Add(rivals[i].Id == tableFocus ? us : rivals[i]);

            var open = tableFor >= 0;
            var selSeat = -1;
            for (var i = 0; i < ring.Count; i++)
                if (ring[i] != null && ring[i].Id == tableFor)
                    selSeat = i;
            var selCentre = middle != null && middle.Id == tableFor;
            if (open && selSeat < 0 && !selCentre)
            {
                tableFor = -1;
                open = false;
            }

            AimTableRise(open ? 1f : 0f);
            AimTableWord(open && tableMove >= 0 ? 1f : 0f);

            // ---- the lines ----
            for (var i = 0; i < ring.Count; i++)
            {
                var occupant = ring[i];
                if (occupant == null || middle == null)
                    continue;
                var tie = HouseTable.Between(world, middle.Id, occupant.Id, day);
                BuildTie(plane, seats[i], tie, open && i != selSeat);
            }

            // ---- the cards, far ones first so a near one always overlaps ----
            var order = new List<int>();
            for (var i = 0; i < ring.Count; i++)
                order.Add(i);
            order.Sort((a, b) => seats[a].y.CompareTo(seats[b].y));

            for (var i = 0; i < order.Count; i++)
            {
                var seat = order[i];
                var occupant = ring[seat];
                if (occupant == null)
                    continue;
                BuildHouseCard(plane, occupant, seats[seat], us, middle,
                    open && seat == selSeat, open);
            }
            if (middle != null)
                BuildHouseCard(plane, middle, new Vector2(PlaneCx, PlaneCy), us, middle,
                    open && selCentre, open);

            // ---- the open house stands up ----
            if (!open)
            {
                PlaceTableMotion();
                return;
            }

            var standing = selCentre ? middle : ring[selSeat];
            var seatOf = selCentre ? new Vector2(PlaneCx, PlaneCy) : seats[selSeat];
            BuildStandingHouse(plane, standing, seatOf, gangs, world, mine, day);
            PlaceTableMotion();
        }

        static Gangs.Gang Find(List<Gangs.Gang> gangs, int gangId)
        {
            for (var i = 0; i < gangs.Count; i++)
                if (gangs[i].Id == gangId)
                    return gangs[i];
            return null;
        }

        /// <summary>Where the chairs stand. Five is the design's own hand-placed table;
        /// any other number is laid on the ellipse those five sit inside, stepped half
        /// a seat off the crown so nothing ever sits straight above the middle.</summary>
        static Vector2[] Seats(int count)
        {
            if (count == FiveSeats.Length)
                return FiveSeats;
            var seats = new Vector2[count];
            const float rx = 404f;
            const float ry = 274f;
            for (var i = 0; i < count; i++)
            {
                // Half a step off the top, so an even hand straddles the middle's
                // crown instead of parking a card straight above it.
                var angle = (-90f + 180f / count + 360f * i / count) * Mathf.Deg2Rad;
                seats[i] = new Vector2(PlaneCx + rx * Mathf.Cos(angle),
                    PlaneCy + ry * Mathf.Sin(angle));
            }
            return seats;
        }

        // ------------------------------------------------------------------- the lines

        /// <summary>
        /// One standing, drawn from the middle of the table to a seat: the line in its
        /// own weight and break, and the words it carries, held off the cards at either
        /// end and turned flat to the reader.
        /// </summary>
        void BuildTie(RectTransform plane, Vector2 seat, HouseTie tie, bool dim)
        {
            ProjectTable(PlaneCx, PlaneCy, 0f, out var from, out var kFrom);
            ProjectTable(seat.x, seat.y, 0f, out var to, out var kTo);
            var delta = to - from;
            var length = delta.magnitude;
            if (length < 1f)
                return;

            var line = NewRect("Standing", plane);
            line.anchorMin = line.anchorMax = new Vector2(0.5f, 0.5f);
            line.pivot = new Vector2(0f, 0.5f);
            line.anchoredPosition = from;
            var weight = TieWeight(tie.Kind) * tableFit * (kFrom + kTo) * 0.5f;
            line.sizeDelta = new Vector2(length, weight);
            line.localRotation = Quaternion.Euler(0f, 0f,
                Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
            TieRule(line, 0f, 0f, length, tie.Kind);
            tableRiseFades.Add(new TableFade(line.gameObject.AddComponent<CanvasGroup>(),
                1f, dim ? 0.2f : 1f));

            // The words sit a fixed distance out from the middle, never a fixed
            // fraction: a long line would otherwise drop its label on top of a card.
            var planeLen = Vector2.Distance(seat, new Vector2(PlaneCx, PlaneCy));
            var along = Mathf.Min(0.38f, 190f / Mathf.Max(1f, planeLen));
            ProjectTable(PlaneCx + (seat.x - PlaneCx) * along,
                PlaneCy + (seat.y - PlaneCy) * along, 0f, out var mid, out var kMid);

            var word = (tie.Kind == TieKind.Peace ? "peace" : tie.What).ToUpperInvariant();
            var boxW = 16f + MonoWidth(word, 11.4f, 9f);
            var label = NewRect("Standing word", plane);
            label.anchorMin = label.anchorMax = new Vector2(0.5f, 0.5f);
            label.pivot = new Vector2(0.5f, 0.5f);
            label.anchoredPosition = mid;
            label.sizeDelta = new Vector2(boxW, 28f);
            var scale = tableFit * kMid;
            label.localScale = new Vector3(scale, scale, 1f);
            Fill(label, LedgerStyle.DeskMid);
            Frame(label, 1f, LedgerV2.At(LedgerStyle.RailGold, 0.26f));
            var text = Text("Word", label, LedgerStyle.MonoBold, 11.4f, TieTone(tie.Kind),
                TextAlignmentOptions.Center);
            Stretch(text.rectTransform);
            text.characterSpacing = 9f;
            text.text = word;
            tableRiseFades.Add(new TableFade(label.gameObject.AddComponent<CanvasGroup>(),
                1f, dim ? 0.12f : 1f));
        }

        // ------------------------------------------------------------------- the cards

        /// <summary>
        /// A house's card, lying on the table. Five units of its own colour down the
        /// spine, its capo's photograph, the name with the standing and the strength
        /// held to the right, and under a dotted rule the one line that matters about
        /// this house today.
        /// </summary>
        void BuildHouseCard(RectTransform plane, Gangs.Gang gang, Vector2 seat,
            Gangs.Gang us, Gangs.Gang middle, bool selected, bool anyOpen)
        {
            var ours = us != null && gang.Id == us.Id;
            var inMiddle = middle != null && gang.Id == middle.Id;

            var card = NewRect("Card " + gang.Name, plane);
            card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);
            card.pivot = new Vector2(0.5f, 0.5f);
            ProjectTable(seat.x, seat.y, 0f, out var at, out var k);
            card.anchoredPosition = at;
            card.sizeDelta = new Vector2(HouseCardW, HouseCardH);
            var scale = tableFit * k;
            // A card lies FLAT on the table, so the tilt takes its height as well.
            card.localScale = new Vector3(scale, scale * TiltCos, 1f);

            tableRiseFades.Add(new TableFade(card.gameObject.AddComponent<CanvasGroup>(),
                1f, !anyOpen ? 1f : selected ? 0f : 0.4f));

            var face = NewRect("Card", card);
            Stretch(face);
            var paper = Shadowed(face, LedgerV2.Panel);
            Frame(face, 1f, ours ? LedgerStyle.RailGold : LedgerV2.Rule);

            Block("Spine", face, 0f, 0f, 5f, HouseCardH, GangPalette.Of(gang.Id));

            var mug = NewRect("Mug", face);
            PlaceTopLeft(mug, 5f, 0f, 46f, HouseCardH);
            Fill(mug, LedgerV2.PanelBand);
            mug.gameObject.AddComponent<RectMask2D>();
            var leader = gang.Members.Count > 0 ? gang.Members[0].FullName : gang.Name;
            var raw = LedgerV2.PortraitPlate(mug, -12f, 0f, 70f, HouseCardH,
                InitialsOf(leader), LedgerV2.PanelBand);
            PortraitStudio.Request(
                PortraitStudio.FindPeoplePrefab(ours
                    ? Gangs.GangCatalog.BossModel
                    : Gangs.GangCatalog.LieutenantModels[gang.Id]),
                PortraitStudio.Framing.Bust, raw);

            // ---- the name, and the two figures held to the right margin ----
            string powerText;
            string stanceWord;
            Color stanceTone;
            string flag;
            if (ours)
            {
                var held = Turf.CountOf(holdings, gang.Id);
                var power = PowerFigure(gang.Id);
                powerText = power < 0 ? "?" : power.ToString();
                stanceWord = "OURS";
                stanceTone = LedgerStyle.RailSafeGold;
                var men = director != null && director.Roster != null
                    ? director.Roster.Members.Count : 0;
                flag = "OURS · " + held + (held == 1 ? " DOOR" : " DOORS") + " · " +
                       men + (men == 1 ? " MAN" : " MEN") + " ON THE BOOKS";
            }
            else
            {
                HouseTable.Read(Underworld.Current, gang, Gangs.GangCatalog.PlayerGangId,
                    outfit ? outfit.Campaign.Day : 1, holdings, PowerFigure, tableCardRead);
                powerText = tableCardRead.PowerText;
                stanceWord = tableCardRead.Stance;
                stanceTone = TieTone(tableCardRead.Tie);
                flag = tableCardRead.Flag.ToUpperInvariant();
            }

            const float pad = 11f;
            const float col = 51f;
            var inner = HouseCardW - col - pad;
            var powerW = MonoWidth(powerText, 15.6f, 0f) + 6f;
            var stanceW = MonoWidth(stanceWord, 11.4f, 10f) + 6f;

            Line(face, LedgerStyle.MonoBold, 15.6f, LedgerV2.Ink,
                HouseCardW - pad - powerW, -9f, powerW, 20f, powerText,
                TextAlignmentOptions.MidlineRight);
            var stance = Line(face, LedgerStyle.MonoBold, 11.4f, stanceTone,
                HouseCardW - pad - powerW - 7f - stanceW, -11f, stanceW, 16f, stanceWord,
                TextAlignmentOptions.MidlineRight);
            stance.characterSpacing = 10f;

            var nameW = Mathf.Max(40f, inner - powerW - stanceW - 14f);
            var written = gang.Name.ToUpperInvariant();
            var nameSize = 26.6f;
            while (nameSize > 18f && CondensedWidth(written, nameSize) > nameW)
                nameSize -= 0.5f;
            var name = Line(face, LedgerStyle.Condensed, nameSize, LedgerV2.Ink,
                col + pad, -8f, nameW, 24f, written);
            name.characterSpacing = 2f;
            name.overflowMode = TextOverflowModes.Ellipsis;

            DottedRule(face, col + pad, -38f, inner, LedgerV2.Dotted);
            var line = Paragraph(face, LedgerStyle.Mono, 10.8f, LedgerV2.Label,
                col + pad, -44f, inner, HouseCardH - 44f - 8f, flag, lineSpacing: 2f);
            line.characterSpacing = 6f;

            // ---- the card takes a press, the chip under it takes a different one ----
            if (!ours)
            {
                var houseId = gang.Id;
                paper.raycastTarget = true;
                RowButton(face, paper, () => OpenTheCard(houseId));
            }

            if (inMiddle && ours)
                return;

            var backToUs = ours || gang.Id == tableFocus;
            var word = backToUs ? "BACK TO US" : "FOCUS";
            var chipW = 18f + MonoWidth(word, 10.8f, 12f);
            var chip = NewRect("Focus", card);
            PlaceTopLeft(chip, HouseCardW - 8f - chipW, -(HouseCardH + 12f), chipW, 24f);
            var chipSkin = Shadowed(chip, LedgerV2.Head);
            chipSkin.raycastTarget = true;
            Frame(chip, 1f, LedgerStyle.RailGold);
            var chipWord = Text("Word", chip, LedgerStyle.MonoBold, 10.8f,
                LedgerStyle.RailGold, TextAlignmentOptions.Center);
            Stretch(chipWord.rectTransform);
            chipWord.characterSpacing = 12f;
            chipWord.text = word;
            var target = backToUs ? -1 : gang.Id;
            RowButton(chip, chipSkin, () => FocusTheTable(target));
        }

        // --------------------------------------------------------------- the standees

        /// <summary>
        /// The open house, standing up off the table: the dossier of the man, the column
        /// of words we may say to him, and the board of the ones we may not with the
        /// gateway's reason under each. The three are anchored to one base so the group
        /// always has room to rise, and they flip about it for a house sitting right of
        /// the middle.
        /// </summary>
        void BuildStandingHouse(RectTransform plane, Gangs.Gang house, Vector2 seat,
            IReadOnlyList<Gangs.Gang> gangs, Underworld world, int mine, int day)
        {
            // The wash: the table reads as a layer under the group rather than as a
            // collision with it. The design lays a 2400 x 1600 sheet under the base,
            // which at every anchor covers the whole plane - so it is the stage.
            var wash = NewRect("Wash", plane);
            Stretch(wash);
            Fill(wash, LedgerV2.At(LedgerStyle.DeskDeep, 0.62f));
            tableRiseFades.Add(new TableFade(wash.gameObject.AddComponent<CanvasGroup>(),
                0f, 1f));

            // Where the card was: the dashed footprint it left on the felt.
            var foot = NewRect("Footprint", plane);
            foot.anchorMin = foot.anchorMax = new Vector2(0.5f, 0.5f);
            foot.pivot = new Vector2(0.5f, 0.5f);
            ProjectTable(seat.x, seat.y, 0f, out var footAt, out var footK);
            foot.anchoredPosition = footAt;
            foot.sizeDelta = new Vector2(HouseCardW, HouseCardH);
            foot.localScale = new Vector3(tableFit * footK, tableFit * footK * TiltCos, 1f);
            DashedFrame(foot, HouseCardW, HouseCardH, LedgerV2.At(LedgerStyle.RailGold, 0.38f));
            tableRiseFades.Add(new TableFade(foot.gameObject.AddComponent<CanvasGroup>(),
                0f, 1f));

            ReadTheHouse(house, world, mine, day, gangs);

            var flip = seat.x > PlaneCx;
            var groupAbs = Mathf.Clamp(seat.x - 336f, 40f, PlaneW - 664f);

            // The dossier is the tall one, and it rises from the base: the base is
            // pushed down far enough that its head clears the top of the plane.
            var dossierH = DossierHeight();
            var low = Mathf.Min(660f, Mathf.Max(452f, dossierH + 66f));
            var baseY = Mathf.Clamp(seat.y, low, 660f) - 43f;
            var dossierAbs = groupAbs + 186f;
            var chipsAbs = flip ? groupAbs + 4f : groupAbs + 478f;
            var shutAbs = flip ? groupAbs + 480f : groupAbs;
            var roomRight = !flip && chipsAbs + WordingW <= PlaneW - 12f;

            BuildShutBoard(plane, shutAbs + ShutW * 0.5f, baseY);
            BuildDossier(plane, dossierAbs + DossierW * 0.5f, baseY);
            BuildActionColumn(plane, chipsAbs + ChipsW * 0.5f, baseY - 2f, roomRight,
                gangs);
        }

        /// <summary>The reading and the words, both off <see cref="HouseTable"/> - the
        /// card, the dossier and the keys are one reading, taken once.</summary>
        void ReadTheHouse(Gangs.Gang house, Underworld world, int mine, int day,
            IReadOnlyList<Gangs.Gang> gangs)
        {
            HouseTable.Read(world, house, mine, day, holdings, PowerFigure, tableRead);

            tableCtx.World = world;
            tableCtx.Mine = mine;
            tableCtx.Them = house.Id;
            tableCtx.Day = day;
            tableCtx.Safe = outfit ? outfit.Accounts.Safe : 0;
            tableCtx.DailyPayroll = director != null && director.Roster != null
                ? Wages.DailyPayroll(director.Roster) : 0;
            CollectStreets();
            tableCtx.Streets = tableStreets.Count;
            CollectEnvoys();
            tableCtx.Envoys = tableEnvoys.Count;

            tableCtx.AnyThird = false;
            tableCtx.AnyWarToJoin = false;
            for (var i = 0; i < gangs.Count; i++)
            {
                var gang = gangs[i];
                if (gang.IsPlayer || gang.Id == house.Id)
                    continue;
                tableCtx.AnyThird = true;
                if (outfit && outfit.StanceWith(gang.Id) == Stance.War)
                    tableCtx.AnyWarToJoin = true;
            }

            var look = HouseOps.Look != null && world != null && world.Player != null
                ? HouseOps.Look(world.Player) : null;
            tableCtx.TheirDays = look != null
                ? look.TheirEndurance(new Territory.TerritoryGangId(house.Id)) : -1;

            HouseTable.Words(tableCtx, tableOpen, tableShutList);
            if (tableMove >= tableOpen.Count)
                tableMove = -1;
        }

        /// <summary>A standee: a panel standing on the table, bottom edge on the plane
        /// at (bx, by), lifted off it as the group rises.</summary>
        RectTransform Standee(RectTransform plane, string name, float bx, float by,
            float w, float h, float lift, float liftOpen = -1f)
        {
            var rect = NewRect(name, plane);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = new Vector2(w, h);
            tableStandees.Add(new TableStandee
            {
                Root = rect,
                Bx = bx,
                By = by,
                Lift = lift,
                LiftOpen = liftOpen,
            });
            tableRiseFades.Add(new TableFade(rect.gameObject.AddComponent<CanvasGroup>(),
                0f, 1f));
            return rect;
        }

        // ----------------------------------------------------------------- the dossier

        const float DossierHeadH = 34f;
        const float DossierMugW = 74f;
        const float DossierMugH = 90f;
        const float LeaderPitch = 28f;

        /// <summary>The width the dossier's full-bleed rows are laid to.</summary>
        static float DossierInner => DossierW - 20f;

        /// <summary>
        /// What the dossier will measure before it is built. The group's base is set
        /// off this, so a tall sheet is never stood up through the top of the table.
        /// </summary>
        float DossierHeight()
        {
            var head = DossierHeadH + 10f +
                       Mathf.Max(DossierMugH, LeaderPitch * 5f);
            var height = head +
                CopyBlock(tableRead.PowerNote, 12.3f, DossierInner, 1f) + 8f +
                TraitPitch * 4f + 8f +
                CopyBlock(tableRead.Personality, 11.8f, DossierInner, 2f);
            if (tableRead.TheyAsk)
                height += 10f + 20f +
                          CopyBlock(tableRead.AskBody, 12.3f, DossierInner, 2f) + 6f;
            height += 12f + 20f + Mathf.Min(3, Mathf.Max(1, tableRead.Record.Count)) * 22f;
            return height + 12f;
        }

        const float TraitPitch = 26f;

        /// <summary>
        /// THE MAN, THE READING AND THE RECORD on one standing sheet. The whole of what
        /// the boss needs about a house: the five figures beside his photograph, what
        /// the strength MEANS in words, the three things about the man himself, the
        /// door he keeps, what they have asked us, and the last words that passed
        /// between the two houses.
        /// </summary>
        void BuildDossier(RectTransform plane, float centreX, float baseY)
        {
            var rowsX = 10f + DossierMugW + 11f;
            var rowsW = DossierW - rowsX - 10f;
            var bodyBottom = DossierHeadH + 10f +
                             Mathf.Max(DossierMugH, LeaderPitch * 5f);
            var height = DossierHeight();

            var card = Standee(plane, "Dossier", centreX, baseY, DossierW, height,
                LiftDossier);
            Shadowed(card, LedgerV2.Panel).raycastTarget = true;
            Frame(card, 1f, LedgerV2.Ink);

            var band = NewRect("Head", card);
            PlaceTopLeft(band, 0f, 0f, DossierW, DossierHeadH);
            Fill(band, LedgerV2.Head);
            var name = Line(band, LedgerStyle.Condensed, 18.5f, LedgerV2.HeadCream, 12f,
                -8f, DossierW - 24f - 140f, 20f, tableRead.Name.ToUpperInvariant());
            name.characterSpacing = 5f;
            name.overflowMode = TextOverflowModes.Ellipsis;
            var who = Line(band, LedgerStyle.Mono, 10.2f, LedgerV2.HeadDim,
                DossierW - 12f - 150f, -9f, 150f, 16f,
                (tableRead.Code + " · " + tableRead.Boss).ToUpperInvariant(),
                TextAlignmentOptions.MidlineRight);
            who.characterSpacing = 10f;
            who.overflowMode = TextOverflowModes.Ellipsis;

            var mug = NewRect("Mug", card);
            PlaceTopLeft(mug, 10f, -(DossierHeadH + 10f), DossierMugW, DossierMugH);
            Fill(mug, LedgerV2.PanelBand);
            Frame(mug, 1f, LedgerV2.Rule);
            var raw = LedgerV2.PortraitPlate(mug, 1f, -1f, DossierMugW - 2f,
                DossierMugH - 2f, InitialsOf(tableRead.Boss), LedgerV2.PanelBand);
            PortraitStudio.Request(
                PortraitStudio.FindPeoplePrefab(
                    Gangs.GangCatalog.LieutenantModels[tableRead.GangId]),
                PortraitStudio.Framing.Bust, raw);

            // ---- the reading, beside his photograph ----
            var ourPower = PowerFigure(Gangs.GangCatalog.PlayerGangId);
            var y = -(DossierHeadH + 10f);
            y = LeaderRow(card, rowsX, y, rowsW, "POWER", tableRead.PowerText,
                tableRead.Power < 0 ? LedgerV2.PaperBlue
                    : ourPower >= 0 && tableRead.Power > ourPower ? LedgerV2.Red
                    : LedgerV2.Ink);
            y = LeaderRow(card, rowsX, y, rowsW, "DOORS",
                tableRead.BlocksTotal > tableRead.Blocks
                    ? tableRead.Blocks + " of " + tableRead.BlocksTotal
                    : tableRead.Blocks.ToString(),
                LedgerV2.Ink);
            y = LeaderRow(card, rowsX, y, rowsW, "CAPOS",
                tableRead.CaposKnown ? tableRead.Capos.ToString() : "not counted",
                tableRead.CaposKnown ? LedgerV2.Ink : LedgerV2.PaperBlue);
            y = LeaderRow(card, rowsX, y, rowsW, "TAKEN", tableRead.TakenText,
                tableRead.Taken > 0 ? LedgerV2.Red : LedgerV2.Muted);
            LeaderRow(card, rowsX, y, rowsW, "OWED", tableRead.OwedText,
                tableRead.TheyOwe > 0 || tableRead.WeOwe > 0 ? LedgerV2.Red : LedgerV2.Muted);

            // ---- what the strength MEANS, which is the thing a figure will not say ----
            y = -bodyBottom;
            var noteH = CopyBlock(tableRead.PowerNote, 12.3f, DossierInner, 1f);
            Paragraph(card, LedgerStyle.SerifItalic, 12.3f,
                tableRead.Power < 0 ? LedgerV2.PaperBlue : LedgerV2.Muted, 10f, y,
                DossierInner, noteH, tableRead.PowerNote, lineSpacing: 1f);
            y -= noteH + 8f;

            // ---- the man himself ----
            Rule(card, 10f, y + 4f, DossierInner, LedgerV2.Hair);
            y = TraitRow(card, 10f, y, DossierInner, "TEMPER", tableRead.Temper);
            y = TraitRow(card, 10f, y, DossierInner, "KEEPS HIS WORD",
                tableRead.KeepsHisWord);
            y = TraitRow(card, 10f, y, DossierInner, "FOUND AT NIGHT",
                tableRead.FoundAtNight);
            y = TraitRow(card, 10f, y, DossierInner, "THE DOOR", tableRead.Front);

            y -= 8f;
            var proseH = CopyBlock(tableRead.Personality, 11.8f, DossierInner, 2f);
            Paragraph(card, LedgerStyle.Serif, 11.8f, LedgerV2.Body, 10f, y,
                DossierInner, proseH, tableRead.Personality, lineSpacing: 2f);
            y -= proseH;

            // ---- what they have asked us, in the wire's own words ----
            if (tableRead.TheyAsk)
            {
                y -= 10f;
                Rule(card, 10f, y + 4f, DossierInner, LedgerV2.Hair);
                var when = Caps(card, 10f, y, DossierInner,
                    "THEY ASK · " + tableRead.AskWhen, 10.2f, LedgerV2.Red, 12f);
                when.font = LedgerStyle.Mono;
                when.overflowMode = TextOverflowModes.Ellipsis;
                y -= 20f;
                var askH = CopyBlock(tableRead.AskBody, 12.3f, DossierInner, 2f);
                Paragraph(card, LedgerStyle.Serif, 12.3f, LedgerV2.Body, 10f, y,
                    DossierInner, askH, tableRead.AskBody, lineSpacing: 2f);
                y -= askH + 6f;
            }

            // ---- the record: the last words that passed between the two houses ----
            y -= 12f;
            Rule(card, 10f, y + 4f, DossierInner, LedgerV2.Hair);
            var head = Caps(card, 10f, y, DossierInner,
                tableRead.Record.Count == 0 ? "THE RECORD"
                    : "THE RECORD · " + tableRead.Record.Count, 10.2f, LedgerV2.Label, 12f);
            head.font = LedgerStyle.Mono;
            y -= 20f;
            if (tableRead.Record.Count == 0)
            {
                Line(card, LedgerStyle.SerifItalic, 11.3f, LedgerV2.Muted, 10f, y,
                    DossierInner, 20f, "nothing has passed between the houses");
                return;
            }
            for (var i = 0; i < tableRead.Record.Count && i < 3; i++)
            {
                var entry = tableRead.Record[i];
                var stamp = LedgerV2.Mono(card, 10f, y, 54f, entry.When, 9.5f,
                    entry.Fresh ? LedgerV2.Red : LedgerV2.Label, 4f);
                stamp.overflowMode = TextOverflowModes.Ellipsis;
                var what = Line(card, LedgerStyle.Type, 11.6f, LedgerV2.Body, 68f, y,
                    DossierInner - 58f, 18f, entry.What);
                what.overflowMode = TextOverflowModes.Ellipsis;
                y -= 22f;
            }
        }

        /// <summary>One thing about the man: the word on the left, the answer held to
        /// the right margin over a dotted leader.</summary>
        static float TraitRow(Transform card, float x, float y, float w, string label,
            string figure)
        {
            LedgerV2.Mono(card, x, y, w * 0.5f, label, 10.2f, LedgerV2.Label, 8f);
            var value = Line(card, LedgerStyle.MonoBold, 11.4f,
                figure == "unknown" ? LedgerV2.PaperBlue : LedgerV2.Ink, x, y, w,
                LineBox(11.4f), figure, TextAlignmentOptions.MidlineRight);
            value.overflowMode = TextOverflowModes.Ellipsis;
            LedgerV2.Leader(card, x, y - 17f, w);
            return y - TraitPitch;
        }

        /// <summary>The design's LeaderRow: the label, the dotted leader between, and
        /// the figure held to the right margin. Answers the y the next row starts at.
        /// </summary>
        static float LeaderRow(Transform parent, float x, float y, float w, string label,
            string figure, Color tone)
        {
            LedgerV2.Mono(parent, x, y, w * 0.55f, label, 10.2f, LedgerV2.Label, 8f);
            var value = Line(parent, LedgerStyle.MonoBold, 12.6f, tone, x, y, w,
                LineBox(12.6f), figure, TextAlignmentOptions.MidlineRight);
            value.overflowMode = TextOverflowModes.Ellipsis;
            LedgerV2.Leader(parent, x, y - 18f, w);
            return y - LeaderPitch;
        }

        // ----------------------------------------------------------- the action column

        /// <summary>
        /// One key a word. Pressing it hangs the wording off it rather than opening a
        /// modal - the table stays visible behind every decision made on it. The other
        /// keys drop back while one is open, and the shut board goes to a whisper.
        /// </summary>
        void BuildActionColumn(RectTransform plane, float centreX, float baseY,
            bool roomRight, IReadOnlyList<Gangs.Gang> gangs)
        {
            var count = Mathf.Max(1, tableOpen.Count);
            var height = count * ChipH + (count - 1) * ChipGap;
            var column = Standee(plane, "Our word", centreX, baseY, ChipsW, height,
                LiftChips, LiftChipsOpen);

            if (tableOpen.Count == 0)
            {
                var none = Line(column, LedgerStyle.MonoItalic, 11.4f, LedgerV2.HeadDim,
                    0f, 0f, ChipsW, ChipH, "nothing can be said today",
                    TextAlignmentOptions.Center);
                none.overflowMode = TextOverflowModes.Ellipsis;
                return;
            }

            // Where the column will STAND when it is fully up, so a wording panel can
            // be told whether it has room to hang under its key or must open upward.
            ProjectTable(centreX, baseY, LiftChipsOpen, out var standing, out var standK);
            var scale = tableFit * standK;

            for (var i = 0; i < tableOpen.Count; i++)
            {
                var move = tableOpen[i];
                var index = i;
                var on = tableMove == i;

                var chip = NewRect("Key " + move.Label, column);
                PlaceTopLeft(chip, 0f, -i * (ChipH + ChipGap), ChipsW, ChipH);
                var keySkin = Shadowed(chip, ChipFace(move.Face));
                keySkin.raycastTarget = true;
                Frame(chip, 1f, ChipEdge(move.Face));
                var word = Text("Word", chip, LedgerStyle.MonoBold, 12.6f,
                    ChipInk(move.Face), TextAlignmentOptions.Center);
                Stretch(word.rectTransform, 2f);
                word.characterSpacing = 11f;
                word.text = move.Label.ToUpperInvariant();
                RowButton(chip, keySkin, () => PickTableMove(index));

                tableWordFades.Add(new TableFade(chip.gameObject.AddComponent<CanvasGroup>(),
                    1f, on ? 1f : 0.4f));

                if (!on)
                    continue;
                chip.SetAsLastSibling();
                var chipBottom = standing.y +
                                 (height - i * (ChipH + ChipGap) - ChipH) * scale;
                BuildWording(chip, move, roomRight, gangs, chipBottom, scale);
            }
        }

        static Color ChipFace(MoveFace face) => face switch
        {
            MoveFace.Dark => LedgerV2.Head,
            MoveFace.Red => LedgerV2.Red,
            MoveFace.Ghost => LedgerV2.PanelBand,
            _ => LedgerV2.Panel,
        };

        static Color ChipInk(MoveFace face) => face switch
        {
            MoveFace.Dark => LedgerV2.HeadCream,
            MoveFace.Red => LedgerV2.HeadCream,
            MoveFace.Ghost => LedgerV2.Red,
            _ => LedgerV2.Ink,
        };

        static Color ChipEdge(MoveFace face) => face switch
        {
            MoveFace.Dark => LedgerStyle.RailGold,
            MoveFace.Red => LedgerV2.Red,
            _ => LedgerV2.Rule,
        };

        /// <summary>
        /// THE WORDING, hung off the key that opened it: what the move actually says,
        /// what it may point at, who carries it, and the two keys that end the question.
        /// A right-hand house opens leftward so the panel never leaves the table.
        /// </summary>
        void BuildWording(RectTransform chip, TableMove move, bool roomRight,
            IReadOnlyList<Gangs.Gang> gangs, float chipBottom, float scale)
        {
            var panel = NewRect("Wording", chip);
            var body = NewRect("Body", panel);

            var termsH = CopyBlock(move.Terms, 12.8f, WordingW - 30f, 2f);
            Paragraph(panel, LedgerStyle.Serif, 12.8f, LedgerV2.Body, 15f, -12f,
                WordingW - 30f, termsH, move.Terms, lineSpacing: 2f);
            var y = -12f - termsH - 8f;

            const float labelW = 60f;
            const float rowX = 15f;
            const float fieldX = rowX + labelW + 11f;
            var fieldW = WordingW - fieldX - 15f;

            if (move.NeedsThird)
            {
                CollectThirds(gangs, move.Word == ProposalKind.JoinWar);
                LedgerV2.Mono(panel, rowX, y, labelW, "AGAINST", 10.8f, LedgerV2.Label, 10f);
                y = ThirdPicker(panel, fieldX, y, fieldW);
            }

            if (move.NeedsStreet)
            {
                LedgerV2.Mono(panel, rowX, y, labelW, "STREET", 10.8f, LedgerV2.Label, 10f);
                y = StreetPicker(panel, fieldX, y, fieldW);
            }

            if (move.NeedsMoney)
            {
                LedgerV2.Mono(panel, rowX, y, labelW, "MONEY", 10.8f, LedgerV2.Label, 10f);
                y = MoneyPicker(panel, fieldX, y, fieldW, move.MoneyCeiling);
            }

            // CARRIED. The prototype fixes this row to "in a man's hand"; the game has a
            // telephone as well, and what a man in their front room is worth is a real
            // lever - his streetwise moves their desk our way, and he can be shot at
            // their door. So the row is the choice, and the design's own line is the
            // note under it.
            if (move.AnswerTo < 0 && !move.War)
            {
                LedgerV2.Mono(panel, rowX, y, labelW, "CARRIED", 10.8f, LedgerV2.Label, 10f);
                y = CarriedPicker(panel, fieldX, y, fieldW);

                // A man sent into a house we are at war with is a man they can keep,
                // and the sheet says so BEFORE the key is pressed rather than in the
                // record afterwards.
                if (tableInPerson && tableEnvoys.Count > 0 && tableRead.Tie == TieKind.War)
                {
                    var envoy = tableEnvoys[Mathf.Clamp(tableEnvoy, 0, tableEnvoys.Count - 1)];
                    var risk = envoy.FullName + " walks into a house we are at war with. " +
                               "If they want a second man of ours, we are handing them one.";
                    LedgerV2.Mono(panel, rowX, y, labelW, "THE RISK", 10.8f, LedgerV2.Red, 12f);
                    var riskH = CopyBlock(risk, 12.3f, fieldW, 1f);
                    Paragraph(panel, LedgerStyle.Serif, 12.3f, LedgerV2.Red, fieldX, y,
                        fieldW, riskH, risk, lineSpacing: 1f);
                    y -= riskH + 6f;
                }
            }

            if (!string.IsNullOrEmpty(tableNote))
            {
                var refused = Paragraph(panel, LedgerStyle.MonoItalic, 11.4f, LedgerV2.Red,
                    rowX, y, WordingW - 30f, 34f, "· " + tableNote, lineSpacing: 1f);
                refused.overflowMode = TextOverflowModes.Truncate;
                y -= 36f;
            }

            y -= 4f;
            const float sendW = 156f;
            LedgerV2.Button(panel, move.Send, rowX, y, sendW, 32f, () => SayTheWord(move),
                move.SendIsRed ? LedgerV2.Key.Red : LedgerV2.Key.Dark, 10.5f);
            LedgerV2.Button(panel, "NEVER MIND", rowX + sendW + 9f, y, 128f, 32f,
                () => PickTableMove(-1), LedgerV2.Key.Ghost, 10.5f);
            y -= 32f + 14f;

            var height = -y;

            // A key near the foot of the column has no room under it: the panel would
            // hang off the bottom of the stage and be cut by the mask. It opens UPWARD
            // instead, which is what a hand would do with the sheet.
            var below = chipBottom - (6f + height) * scale > -StageH * 0.5f + 10f;
            PlaceTopLeft(panel, roomRight ? 0f : -(WordingW - ChipsW),
                below ? -(ChipH + 6f) : height + 6f, WordingW, height);
            PlaceTopLeft(body, 0f, 0f, WordingW, height);
            Shadowed(body, LedgerV2.Panel).raycastTarget = true;
            Frame(body, 1f, LedgerV2.Ink);
            Block("Spine", body, 0f, 0f, 4f, height, LedgerV2.Red);
            body.SetAsFirstSibling();
        }

        // ------------------------------------------------------------- the shut board

        void BuildShutBoard(RectTransform plane, float centreX, float baseY)
        {
            const float pad = 12f;
            var inner = ShutW - pad * 2f;

            // Measured before the board is made: a reason is a sentence, and a board
            // sized to one line would cut half of them off.
            var rowH = new float[Mathf.Max(1, tableShutList.Count)];
            var rows = 0f;
            for (var i = 0; i < tableShutList.Count; i++)
            {
                rowH[i] = 24f + CopyBlock(tableShutList[i].Why, 11.3f, inner, 1f) + 8f;
                rows += rowH[i];
            }
            if (tableShutList.Count == 0)
                rows = 26f;
            var height = 10f + 22f + 8f + rows + 6f;

            var board = Standee(plane, "Shut", centreX, baseY, ShutW, height, LiftShut);
            ClickSurface(board).color = LedgerStyle.Rail;
            Frame(board, 1f, LedgerV2.At(LedgerStyle.RailGold, 0.26f));
            tableWordFades.Add(new TableFade(board.gameObject.AddComponent<CanvasGroup>(),
                1f, 0.16f));

            var kicker = Caps(board, pad, -10f, inner,
                "SHUT · " + tableShutList.Count, 10.8f, LedgerStyle.RailKicker, 13f);
            kicker.font = LedgerStyle.Mono;

            var y = -40f;
            if (tableShutList.Count == 0)
            {
                Line(board, LedgerStyle.SerifItalic, 11.3f, LedgerStyle.RailNote, pad, y,
                    inner, 22f, "every word is open today");
                return;
            }

            for (var i = 0; i < tableShutList.Count; i++)
            {
                var shut = tableShutList[i];
                var label = Caps(board, pad, y, inner, shut.Label, 10.8f,
                    LedgerStyle.RailLabel, 8f);
                label.font = LedgerStyle.Mono;
                label.overflowMode = TextOverflowModes.Ellipsis;
                Paragraph(board, LedgerStyle.SerifItalic, 11.3f, LedgerStyle.RailNote,
                    pad, y - 22f, inner, rowH[i] - 30f, shut.Why, lineSpacing: 1f);
                Block("Hair", board, pad, y - rowH[i] + 6f, inner, 1f,
                    LedgerStyle.RailHair);
                y -= rowH[i];
            }
        }

        // ------------------------------------------------------------------ furniture

        /// <summary>
        /// A panel on the table: the book's own two-layer shadow - a tight contact and
        /// a wide soft cast, the same pair every card in the ledger stands on - and the
        /// face over it.
        ///
        /// The face is a CHILD, never the rect's own Image. A parent's graphic draws
        /// BEFORE its children, so a shadow laid inside a rect that carried its own
        /// fill was painted straight over the panel: every card, dossier and key on
        /// this sheet came out grey under a square of black. Content added to the rect
        /// after this call lands over the face, which is where it belongs.
        /// </summary>
        static Image Shadowed(RectTransform rect, Color face)
        {
            TableShadow(rect, 12f, -4f, 0.26f);
            TableShadow(rect, 3f, -1f, 0.42f);
            var skin = NewRect("Face", rect);
            Stretch(skin);
            return Fill(skin, face);
        }

        static void TableShadow(RectTransform panel, float spread, float drop,
            float strength)
        {
            var shadow = NewRect("Shadow", panel);
            shadow.anchorMin = Vector2.zero;
            shadow.anchorMax = Vector2.one;
            shadow.offsetMin = new Vector2(-spread, -spread + drop);
            shadow.offsetMax = new Vector2(spread, spread + drop);
            var image = shadow.gameObject.AddComponent<Image>();
            image.sprite = LedgerStyle.SoftShadow;
            image.type = Image.Type.Sliced;
            image.color = new Color(0f, 0f, 0f, strength);
            image.raycastTarget = false;
        }

        /// <summary>
        /// The height a block of the serif copy face will actually take in a column of
        /// this width, with a floor under it so an empty string still leaves a line's
        /// room. The measuring itself is the book's own <see cref="CopyHeight"/> - a
        /// real TMP pass off a hidden face at the size that PRINTS - because a panel
        /// cut to an estimate is a panel that eats the end of a sentence.
        /// </summary>
        static float CopyBlock(string copy, float size, float width, float lineSpacing) =>
            Mathf.Max(LineBox(BookSize(size)), CopyHeight(copy, size, width, lineSpacing) + 6f);

        /// <summary>Four dashed edges round a rect - the footprint a card leaves behind.
        /// </summary>
        static void DashedFrame(Transform parent, float w, float h, Color colour)
        {
            const float dash = 6f;
            const float pitch = 12f;
            for (var x = 0f; x < w; x += pitch)
            {
                var run = Mathf.Min(dash, w - x);
                Block("Dash", parent, x, 0f, run, 1f, colour);
                Block("Dash", parent, x, -(h - 1f), run, 1f, colour);
            }
            for (var y = 0f; y < h; y += pitch)
            {
                var run = Mathf.Min(dash, h - y);
                Block("Dash", parent, 0f, -y, 1f, run, colour);
                Block("Dash", parent, w - 1f, -y, 1f, run, colour);
            }
        }

        // --------------------------------------------------------------- the doing

        void OpenTheCard(int gangId)
        {
            if (tableFor == gangId)
                return;
            tableFor = gangId;
            tableMove = -1;
            tableNote = "";
            tableMoney = 0;
            dirty = true;
        }

        void CloseTheCard()
        {
            if (tableFor < 0 && tableMove < 0)
                return;
            tableFor = -1;
            tableMove = -1;
            tableNote = "";
            dirty = true;
        }

        void FocusTheTable(int gangId)
        {
            tableFocus = gangId;
            tableFor = -1;
            tableMove = -1;
            dirty = true;
        }

        /// <summary>A key pressed: its wording opens under it with the figure the word
        /// starts from already wound in. Pressing the same key again folds it.</summary>
        void PickTableMove(int index)
        {
            if (index < 0 || index == tableMove)
            {
                tableMove = -1;
                tableNote = "";
                dirty = true;
                return;
            }
            tableMove = index;
            tableNote = "";
            var move = index < tableOpen.Count ? tableOpen[index] : null;
            tableMoney = move == null ? 0
                : move.Word == ProposalKind.Bill ? move.MoneyCeiling
                : move.Word == ProposalKind.TributeTerms ? move.MoneyCeiling / 2
                : 0;
            dirty = true;
        }
    }
}
