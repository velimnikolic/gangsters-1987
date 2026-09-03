using System.Collections.Generic;
using LivingCity.Gangs;
using LivingCity.Personnel;
using LivingCity.Property;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static LivingCity.UI.LedgerKit;

namespace LivingCity.UI
{
    /// <summary>
    /// THE BLUEPRINT — one building's flats, floor by floor (EPIC 27, FLAT-006).
    ///
    /// It wears a popup: a dark backdrop over the book and one centred sheet, 1400 wide at
    /// most, hard-edged, with the dark showing round it. It IS a tab-less page, the way
    /// <see cref="LedgerPage.Orders"/> is, because the book has no modal-over-a-page
    /// pattern; a page gets the lifecycle, the Esc chain and the way back for free.
    ///
    /// EVERY MEASURE ON THIS SHEET IS THE PROTOTYPE'S OWN, taken off
    /// `Docs/design-briefs/blueprint-prototype/outfit-ledger-v2.html` and named here as a
    /// constant rather than guessed at a call site: the sheet's 14/24/30 padding, the
    /// 56 · doors · 166 grid, the 86-unit cell with its 8/8/8/10 padding, the door badge,
    /// the caption bar, the legend and the seven columns of OUR FLATS. The colours are the
    /// ledger's own tokens, which §5 of the UI brief maps one for one onto the
    /// prototype's oklch.
    ///
    /// The way in is the BUILDING itself: its mast on the block film, or its header in the
    /// block file's trade column. Both call <see cref="OpenBlueprint"/>.
    /// </summary>
    public sealed partial class PersonnelAlmanac
    {
        // ------------------------------------------------------------------ the fixture

        /// <summary>The design's sheet: centred paper, never wider than this.</summary>
        const float BpSheetMax = 1400f;

        const float BpPadX = 24f;
        const float BpPadTop = 14f;
        const float BpPadBottom = 30f;

        /// <summary>Title over address, down to the 3-unit rule that closes the head.</summary>
        const float BpHeadH = 72f;

        /// <summary>The plan's own columns: the floor stub, the doors, the floor's read.</summary>
        const float BpFloorW = 56f;
        const float BpSummaryW = 166f;

        /// <summary>A door cell. The prototype's grid is `minmax(0,1fr)` over ten doors
        /// on a plan whose own min-width is 1080 - which is 86 a door - and the panel
        /// SCROLLS SIDEWAYS rather than squeezing when there are more. A landing here runs
        /// from one door to twenty-two, so the scroll is not a nicety: at twenty doors a
        /// squeezed cell cuts the tenant's name to four letters.</summary>
        const float BpCellH = 86f;
        const float BpCellW = 86f;

        const float BpGridHeadH = 24f;
        const float BpCaptionH = 38f;
        const float BpRowH = 34f;

        static readonly Color BpBackdrop = new Color(0.09f, 0.075f, 0.06f, 0.66f);

        /// <summary>oklch(0.86 0.02 68) and oklch(0.93 0.015 74): the two bands of the 45°
        /// hatch a flat that is not ours - and the common ground - is filled with.</summary>
        static readonly Color BpHatchDark = new Color32(0xDC, 0xD0, 0xC2, 0xFF);
        static readonly Color BpHatchLight = new Color32(0xEC, 0xE3, 0xD6, 0xFF);

        // ------------------------------------------------------------------ the state

        ApartmentBuildingId blueprintBuilding;

        /// <summary>The leaf the reader came from, so the ✕ gives the page back.</summary>
        LedgerPage blueprintReturn = LedgerPage.Blocks;

        bool blueprintFormOpen;
        ApartmentUnitId blueprintUnit;

        /// <summary>The door the caption bar is reading: the pointer's, or the picked one.</summary>
        ApartmentUnitId blueprintCaption;

        /// <summary>Edit mode: the pickers, rather than the read-only file.</summary>
        bool blueprintEditing;

        UnitRole draftRole = UnitRole.Empty;
        int draftKeeper = -1;
        string draftName = "";
        bool draftDirty;

        /// <summary>0 none, 1 role, 2 name - the two sortable columns of OUR FLATS.</summary>
        int blueprintSort;
        bool blueprintSortDesc;

        string blueprintNote = "";

        RectTransform blueprintSheet, blueprintFixed, blueprintViewport, blueprintContent,
            blueprintForm;

        /// <summary>The sheet's measured width. Every column on it is cut from this and
        /// never from the page.</summary>
        float blueprintSheetW = BpSheetMax;

        internal float blueprintScroll;

        /// <summary>How far the plan has been dragged sideways, and how far it may go. A
        /// landing of twenty doors is wider than any sheet.</summary>
        RectTransform blueprintPlan;
        float blueprintPlanScroll;
        float blueprintPlanRun;

        readonly List<ApartmentRecord> blueprintOurs = new List<ApartmentRecord>();

        // ------------------------------------------------------------------ the way in

        /// <summary>Opens the sheet over whichever page is showing. The film's mast and the
        /// trade column's building header both land here.</summary>
        public void OpenBlueprint(ApartmentBuildingId building)
        {
            if (!building.IsValid)
                return;
            if (currentPage != LedgerPage.Blueprint)
                blueprintReturn = currentPage;
            blueprintBuilding = building;
            blueprintFormOpen = false;
            blueprintEditing = false;
            blueprintNote = "";
            blueprintCaption = default;
            blueprintScroll = 0f;

            // The page this sheet stands over has to have something ON it: the reader
            // sees it behind the backdrop, and a page whose paint was still pending when
            // the popup opened is a grey hole where the block file should be.
            if (currentPage != LedgerPage.Blueprint)
            {
                Repaint();
                dirty = false;
            }

            SetPage(LedgerPage.Blueprint);
        }

        /// <summary>Esc peels one layer: the flat's form, then the sheet itself.</summary>
        bool CloseBlueprintTransient()
        {
            if (blueprintFormOpen)
            {
                blueprintFormOpen = false;
                blueprintEditing = false;
                dirty = true;
                return true;
            }
            if (currentPage == LedgerPage.Blueprint)
            {
                SetPage(blueprintReturn);
                return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ the building

        void BuildBlueprintPage(RectTransform sheet)
        {
            var root = NewPageRoot(sheet, LedgerPage.Blueprint);

            var backdrop = NewRect("Blueprint backdrop", root);
            Stretch(backdrop);
            var shade = backdrop.gameObject.AddComponent<Image>();
            shade.color = BpBackdrop;
            shade.raycastTarget = true;
            var dismiss = backdrop.gameObject.AddComponent<Button>();
            dismiss.targetGraphic = shade;
            dismiss.transition = Selectable.Transition.None;
            dismiss.onClick.AddListener(() => CloseBlueprintTransient());

            blueprintSheetW = Mathf.Min(BpSheetMax, PageWidth);
            var sheetH = -(PageBottom - PageTop);

            var paper = NewRect("Blueprint sheet", root);
            PlaceTopLeft(paper, PageLeft + (PageWidth - blueprintSheetW) * 0.5f, PageTop,
                blueprintSheetW, sheetH);
            var face = Fill(paper, LedgerV2.Panel);
            // The paper is not the backdrop: a click on it must not close the sheet.
            face.raycastTarget = true;
            paper.gameObject.AddComponent<Button>().transition = Selectable.Transition.None;
            ShadowUnder(paper, 26f);
            blueprintSheet = paper;

            blueprintFixed = NewRect("Blueprint fixed", paper);
            Stretch(blueprintFixed);

            var top = BpPadTop + BpHeadH;
            blueprintViewport = NewRect("Blueprint window", paper);
            PlaceTopLeft(blueprintViewport, BpPadX, -top,
                blueprintSheetW - BpPadX * 2f, sheetH - top - BpPadBottom);
            blueprintViewport.gameObject.AddComponent<RectMask2D>();

            blueprintContent = NewRect("Blueprint content", blueprintViewport);
            blueprintContent.anchorMin = new Vector2(0f, 1f);
            blueprintContent.anchorMax = new Vector2(1f, 1f);
            blueprintContent.pivot = new Vector2(0f, 1f);
            blueprintContent.anchoredPosition = Vector2.zero;
            blueprintContent.sizeDelta = new Vector2(0f, 400f);

            blueprintForm = NewRect("Blueprint form", root);
            Stretch(blueprintForm);
        }

        // ------------------------------------------------------------------ the painting

        void RebuildBlueprint()
        {
            if (!blueprintFixed || !blueprintContent)
                return;

            foreach (Transform old in blueprintFixed) Destroy(old.gameObject);
            foreach (Transform old in blueprintContent) Destroy(old.gameObject);
            foreach (Transform old in blueprintForm) Destroy(old.gameObject);
            bpCaptionDoor = null;

            if (!ApartmentBuildings.TryGet(blueprintBuilding, out var building))
            {
                Line(blueprintFixed, LedgerStyle.Type, 27f, LedgerV2.Ink, BpPadX,
                    -BpPadTop, 400f, 34f, "BLUEPRINT");
                LedgerV2.Mono(blueprintFixed, BpPadX, -BpPadTop - 36f, 400f,
                    "NO BUILDING PICKED", 11f, LedgerV2.Muted, 2f);
                return;
            }

            PaintBpHead(building);

            var y = -20f;                       // the prototype's 20 of air under the rule
            y = PaintBpPlan(building, y);
            y = PaintBpOurFlats(building, y);

            var printed = Mathf.Max(-y + 10f, 200f);
            blueprintContent.sizeDelta = new Vector2(0f, printed);

            // THE PAPER IS CUT TO WHAT IS ON IT. A sheet the height of the page with a
            // hand's width of empty paper under the last row is not the design's sheet.
            var pageH = -(PageBottom - PageTop);
            var wanted = Mathf.Min(pageH, BpPadTop + BpHeadH + printed + BpPadBottom);
            blueprintSheet.sizeDelta = new Vector2(blueprintSheetW, wanted);
            blueprintViewport.sizeDelta = new Vector2(
                blueprintSheetW - BpPadX * 2f,
                wanted - (BpPadTop + BpHeadH) - BpPadBottom);

            if (blueprintFormOpen)
                PaintFlatForm(building);
        }

        /// <summary>Title, address, the five quick facts, the 3-unit rule, and the way out
        /// in the sheet's own corner.</summary>
        void PaintBpHead(ApartmentBuilding building)
        {
            var inner = blueprintSheetW - BpPadX * 2f;
            var top = -BpPadTop;

            Line(blueprintFixed, LedgerStyle.Type, 27f, LedgerV2.Ink,
                BpPadX, top, 520f, 34f, "BLUEPRINT").characterSpacing = 2f;
            LedgerV2.Mono(blueprintFixed, BpPadX, top - 33f, 760f,
                building.Address + " · " + Spell(building.Floors) + " FLOORS OF FLATS · " +
                Spell(building.DoorsPerLanding) + " DOORS TO A LANDING",
                11f, LedgerV2.Muted, 2f);

            PaintBpFacts(building, top);

            Rule(blueprintFixed, BpPadX, top - (BpHeadH - BpPadTop), inner,
                LedgerV2.Ink, 3f);

            // The typewriter face has no ✕ (U+2715) - it printed as a stray letter - so
            // the way out is set in the letter the face does have.
            var close = LedgerV2.Button(blueprintFixed, "X",
                blueprintSheetW - 20f - 30f, -BpPadTop, 30f, 30f,
                () => CloseBlueprintTransient(), LedgerV2.Key.Dark, 15f);
            close.color = LedgerV2.HeadInk;
        }

        /// <summary>ON OUR DEED · OPEN · DARK · SHUT · HEAT, label over value.</summary>
        void PaintBpFacts(ApartmentBuilding building, float top)
        {
            var gang = GangCatalog.PlayerGangId;
            var day = RosterDay;
            int open = 0, dark = 0, shut = 0, heat = 0, held = 0;

            for (var floor = 1; floor <= building.Floors; floor++)
            for (var slot = 0; slot < building.DoorsPerLanding; slot++)
            {
                var unit = new ApartmentUnitId(building.Id, floor, slot);
                if (!Apartments.TryGet(unit, out var record) || record.GangId != gang)
                    continue;
                held++;
                switch (StateOfFlat(unit, day))
                {
                    case UnitState.Open:
                        open++;
                        heat += UnitRoles.Of(record.Role).Heat;
                        break;
                    case UnitState.Raided:
                    case UnitState.NoBank:
                        shut++;
                        break;
                    default:
                        dark++;
                        break;
                }
            }

            var facts = new (string label, string value, Color ink)[]
            {
                ("ON OUR DEED", held + " / " + building.Flats,
                    held > 0 ? LedgerV2.Ink : LedgerV2.Muted),
                ("OPEN", open.ToString(), open > 0 ? LedgerV2.Green : LedgerV2.Muted),
                ("DARK", dark.ToString(), dark > 0 ? LedgerV2.PaperBlue : LedgerV2.Muted),
                ("SHUT", shut.ToString(), shut > 0 ? LedgerV2.Red : LedgerV2.Muted),
                ("HEAT", heat + "/DAY",
                    heat >= 4 ? LedgerV2.Red : heat > 0 ? LedgerV2.Amber : LedgerV2.Muted),
            };

            // The prototype sets them in a row with 26 of gap; the widest reading is
            // "0 / 100", so a 116 column holds every one without measuring text.
            const float factW = 116f;
            var x = blueprintSheetW - 20f - 30f - 20f - facts.Length * factW;
            for (var i = 0; i < facts.Length; i++)
            {
                LedgerV2.Mono(blueprintFixed, x + i * factW, top + 1f, factW - 26f,
                    facts[i].label, 9f, LedgerV2.Label, 13f);
                Line(blueprintFixed, LedgerStyle.Mono, 14.5f, facts[i].ink,
                    x + i * factW, top - 16f, factW - 26f, 19f, facts[i].value);
            }
        }

        // ------------------------------------------------------------------ the plan

        float PaintBpPlan(ApartmentBuilding building, float y)
        {
            var inner = blueprintSheetW - BpPadX * 2f;
            var doors = Mathf.Max(1, building.DoorsPerLanding);
            // The doors keep their measured width and the plan grows past the sheet; a
            // narrow building simply spreads its doors over the room it has.
            var cell = Mathf.Max(BpCellW,
                (inner - BpFloorW - BpSummaryW) / doors);
            var planW = BpFloorW + cell * doors + BpSummaryW;
            var planScrolls = planW > inner;

            Line(blueprintContent, LedgerStyle.Type, 19f, LedgerV2.Ink, 0f, y, 320f, 24f,
                "THE BUILDING").characterSpacing = 4f;
            LedgerV2.Mono(blueprintContent, 340f, y - 3f, inner - 340f,
                "click a door · the form is the clerk's paper", 11f, LedgerV2.Muted, 1f,
                TextAlignmentOptions.MidlineRight);
            y -= 26f;
            Rule(blueprintContent, 0f, y, planW, LedgerV2.SheetRule);
            y -= 14f;

            // One window over the whole plan, so the head, the floors and the caption
            // travel together when it is dragged sideways.
            var planH = BpGridHeadH + (building.Floors + 1) * BpCellH;
            var window = NewRect("Plan window", blueprintContent);
            PlaceTopLeft(window, 0f, y, inner, planH);
            if (planScrolls)
                window.gameObject.AddComponent<RectMask2D>();

            var plan = NewRect("Plan", window);
            plan.anchorMin = plan.anchorMax = new Vector2(0f, 1f);
            plan.pivot = new Vector2(0f, 1f);
            plan.anchoredPosition = new Vector2(-blueprintPlanScroll, 0f);
            plan.sizeDelta = new Vector2(planW, planH);
            blueprintPlan = plan;
            blueprintPlanRun = Mathf.Max(0f, planW - inner);
            if (planScrolls)
                DragSideways(window);

            var planY = 0f;
            var head = NewRect("Plan head", plan);
            PlaceTopLeft(head, 0f, planY, planW, BpGridHeadH);
            Fill(head, LedgerV2.Head);
            LedgerV2.Mono(head, 4f, -4f, BpFloorW - 8f, "FL", 9f, LedgerV2.HeadInk, 6f,
                TextAlignmentOptions.Midline);
            for (var slot = 0; slot < doors; slot++)
                LedgerV2.Mono(head, BpFloorW + slot * cell + 8f, -4f, cell - 12f,
                    ApartmentBuildings.DoorLetter(slot), 10.5f, LedgerV2.HeadDim, 6f);
            LedgerV2.Mono(head, BpFloorW + cell * doors + 11f, -4f, BpSummaryW - 22f,
                "THE FLOOR", 9f, LedgerV2.HeadInk, 6f);
            planY -= BpGridHeadH;

            var day = RosterDay;
            for (var floor = building.Floors; floor >= 1; floor--)
                planY = PaintBpFloor(plan, building, floor, cell, planW, planY, day);
            planY = PaintBpGround(plan, building, cell, planW, planY);

            y -= planH;
            y -= 11f;
            y = PaintBpCaption(blueprintContent, inner, y);
            if (planScrolls)
            {
                LedgerV2.Mono(blueprintContent, 0f, y - 2f, inner,
                    "drag the plan sideways · " + doors + " doors to a landing", 9.5f,
                    LedgerV2.Faint, 4f, TextAlignmentOptions.MidlineRight);
                y -= 16f;
            }
            y -= 12f;
            y = PaintBpLegend(y);
            return y - 22f;
        }

        float PaintBpFloor(RectTransform plan, ApartmentBuilding building, int floor,
            float cell, float planW, float y, int day)
        {
            var gang = GangCatalog.PlayerGangId;
            var doors = building.DoorsPerLanding;

            var row = NewRect("Floor " + floor, plan);
            PlaceTopLeft(row, 0f, y, planW, BpCellH);
            Rule(row, 0f, 0f, planW, LedgerV2.Rule);

            var stub = NewRect("Floor stub", row);
            PlaceTopLeft(stub, 0f, 0f, BpFloorW, BpCellH);
            Fill(stub, LedgerV2.DarkPlate);
            Line(stub, LedgerStyle.Type, 19f, LedgerV2.HeadCream, 0f,
                -(BpCellH * 0.5f - 17f), BpFloorW, 24f, floor.ToString(),
                TextAlignmentOptions.Midline);
            LedgerV2.Mono(stub, 0f, -(BpCellH * 0.5f + 3f), BpFloorW, "FLOOR", 8.5f,
                LedgerV2.HeadDim, 10f, TextAlignmentOptions.Midline);

            int ours = 0, open = 0, dark = 0, shut = 0, heat = 0;
            for (var slot = 0; slot < doors; slot++)
            {
                var unit = new ApartmentUnitId(building.Id, floor, slot);
                var state = StateOfFlat(unit, day);
                PaintBpCell(row, unit, state, BpFloorW + slot * cell, cell);

                if (!Apartments.TryGet(unit, out var record) || record.GangId != gang)
                    continue;
                ours++;
                if (state == UnitState.Open)
                {
                    open++;
                    heat += UnitRoles.Of(record.Role).Heat;
                }
                else if (state == UnitState.Raided || state == UnitState.NoBank) shut++;
                else dark++;
            }

            PaintBpFloorRead(row, BpFloorW + cell * doors, ours, doors, open, dark, shut,
                heat, false);
            return y - BpCellH;
        }

        /// <summary>The ground floor is drawn as what it is - the shops and the entrance -
        /// and NOTHING on it is sold from this sheet.</summary>
        float PaintBpGround(RectTransform plan, ApartmentBuilding building, float cell,
            float planW, float y)
        {
            var doors = building.DoorsPerLanding;
            var row = NewRect("Ground", plan);
            PlaceTopLeft(row, 0f, y, planW, BpCellH);
            Rule(row, 0f, 0f, planW, LedgerV2.Rule);

            var stub = NewRect("Floor stub", row);
            PlaceTopLeft(stub, 0f, 0f, BpFloorW, BpCellH);
            Fill(stub, LedgerV2.DarkPlate);
            Line(stub, LedgerStyle.Type, 12f, LedgerV2.HeadCream, 0f,
                -(BpCellH * 0.5f - 13f), BpFloorW, 18f, "GRD",
                TextAlignmentOptions.Midline);
            LedgerV2.Mono(stub, 0f, -(BpCellH * 0.5f + 3f), BpFloorW, "SHOPS", 8.5f,
                LedgerV2.HeadDim, 10f, TextAlignmentOptions.Midline);

            var entrance = doors / 2;
            for (var slot = 0; slot < doors; slot++)
            {
                var tile = NewRect("Ground " + slot, row);
                PlaceTopLeft(tile, BpFloorW + slot * cell, 0f, cell, BpCellH);
                tile.gameObject.AddComponent<RectMask2D>();
                Hatch(tile, cell, BpCellH);
                Block("Edge", tile, cell - 1f, 0f, 1f, BpCellH, LedgerV2.Hair);

                DoorBadge(tile, 10f, -8f,
                    slot == entrance ? "ENTR" : "G" + ApartmentBuildings.DoorLetter(slot),
                    false);
                Block("State", tile, cell - 18f, -8f, 10f, 10f, LedgerV2.Faint);
                LedgerV2.Mono(tile, 10f, -34f, cell - 20f,
                    slot == entrance ? "HALL & STAIRS" : "SHOP", 11f, LedgerV2.Muted, 4f);
                LedgerV2.Mono(tile, 10f, -(BpCellH - 24f), cell - 20f,
                    slot == entrance ? "COMMON" : "SHOP", 10.5f, LedgerV2.Faint, 7f);
            }

            PaintBpFloorRead(row, BpFloorW + cell * doors, 0, 0, 0, 0, 0, 0, true);
            return y - BpCellH;
        }

        /// <summary>The floor's own read, on the light band at the end of its row.</summary>
        void PaintBpFloorRead(RectTransform row, float x, int ours, int doors, int open,
            int dark, int shut, int heat, bool ground)
        {
            var band = NewRect("Floor read", row);
            PlaceTopLeft(band, x, 0f, BpSummaryW, BpCellH);
            Fill(band, LedgerV2.PanelBand);
            Block("Edge", band, 0f, 0f, 1f, BpCellH, LedgerV2.Hair);

            if (ground)
            {
                LedgerV2.Mono(band, 11f, -(BpCellH * 0.5f - 16f), BpSummaryW - 22f,
                    "NOT SOLD HERE", 11.5f, LedgerV2.Muted, 4f);
                LedgerV2.Mono(band, 11f, -(BpCellH * 0.5f + 1f), BpSummaryW - 22f,
                    "a shop is bought at its door", 10.5f, LedgerV2.Faint, 1f);
                return;
            }

            var openInk = dark > 0 || shut > 0 ? LedgerV2.Red : LedgerV2.Green;
            LedgerV2.Mono(band, 11f, -(BpCellH * 0.5f - 22f), BpSummaryW - 22f,
                "OURS " + ours + "/" + doors, 11.5f, LedgerV2.Body, 4f);
            LedgerV2.Mono(band, 11f, -(BpCellH * 0.5f - 3f), BpSummaryW - 22f,
                "OPEN " + open + (dark > 0 ? " · DARK " + dark : "") +
                (shut > 0 ? " · SHUT " + shut : ""), 10.5f, openInk, 4f);
            LedgerV2.Mono(band, 11f, -(BpCellH * 0.5f + 16f), BpSummaryW - 22f,
                "HEAT " + heat + "/DAY", 10.5f,
                heat >= 4 ? LedgerV2.Red : heat > 0 ? LedgerV2.Amber : LedgerV2.Muted, 4f);
        }

        /// <summary>
        /// One door, exactly as the prototype draws it: the badge and the state square on
        /// the top line, the role or the tenant under it, and at the foot either the
        /// keeper's half-stars, a stamp, or a plain tag.
        /// </summary>
        void PaintBpCell(RectTransform row, ApartmentUnitId unit, UnitState state,
            float x, float cell)
        {
            var picked = blueprintFormOpen && blueprintUnit.Equals(unit);
            var ours = Apartments.TryGet(unit, out var record) &&
                       record.GangId == GangCatalog.PlayerGangId;

            var tile = NewRect("Door " + unit.Door, row);
            PlaceTopLeft(tile, x, 0f, cell, BpCellH);
            tile.gameObject.AddComponent<RectMask2D>();

            var face = ours
                ? Face(tile, picked ? LedgerV2.Picked : LedgerV2.Panel)
                : Hatch(tile, cell, BpCellH);

            // The selection is a 3-unit bar on the cell's own left edge; a hairline closes
            // it on the right.
            if (picked)
                Block("Picked", tile, 0f, 0f, 3f, BpCellH, LedgerV2.Red);
            Block("Edge", tile, cell - 1f, 0f, 1f, BpCellH, LedgerV2.Hair);

            DoorBadge(tile, 10f, -8f, unit.Door, picked);
            Block("State", tile, cell - 18f, -8f, 10f, 10f, StateInk(state));

            var role = !ours ? TenantOf(unit)
                : record.Role == UnitRole.Empty ? "NO ROLE"
                : UnitRoles.Of(record.Role).ShortLabel;
            LedgerV2.Mono(tile, 10f, -34f, cell - 20f, role, 11f,
                state == UnitState.Open ? LedgerV2.Body
                    : !ours ? LedgerV2.PaperBlue : LedgerV2.Muted, 4f);

            var footY = -(BpCellH - 26f);
            var stamp = state switch
            {
                UnitState.Raided => "RAID " + (ours ? record.RaidUntilDay : 0),
                UnitState.NoBank => "NO BANK",
                UnitState.Dark => ours && record.Role != UnitRole.Empty ? "DARK" : "",
                _ => "",
            };

            if (!string.IsNullOrEmpty(stamp))
            {
                var box = NewRect("Stamp", tile);
                var stampW = Mathf.Min(cell - 20f, stamp.Length * 7.6f + 12f);
                PlaceTopLeft(box, 10f, footY, stampW, 18f);
                Frame(box, 1.5f, StateInk(state));
                box.localRotation = Quaternion.Euler(0f, 0f, 3f);
                LedgerV2.Mono(box, 5f, -1f, stampW - 10f, stamp, 9.5f, StateInk(state), 7f);
            }
            else if (ours && record.Role != UnitRole.Empty && record.KeeperId >= 0)
            {
                var keeper = director != null && director.Roster != null
                    ? director.Roster.Find(record.KeeperId)
                    : null;
                if (keeper != null)
                    Stars(tile, 10f, footY - 6f,
                        keeper.GetHalfSteps(UnitRoles.Of(record.Role).Wants), 11f, 12f);
            }
            else
            {
                var tag = !ours ? "TENANT"
                    : record.Role == UnitRole.Empty ? "EMPTY" : "OPEN";
                LedgerV2.Mono(tile, 10f, footY, cell - 20f, tag, 10.5f, StateInk(state), 7f);
            }

            RowButton(tile, face, () => OpenFlatForm(unit));

            // The pointer over a door reads it in the caption bar, and NOTHING repaints:
            // the plan is destroyed and rebuilt whole, and a repaint under a moving
            // pointer is what makes a hover feel broken.
            var hovered = unit;
            var trigger = tile.gameObject.AddComponent<EventTrigger>();
            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ =>
            {
                blueprintCaption = hovered;
                RefreshBpCaption();
            });
            trigger.triggers.Add(enter);
        }

        /// <summary>The plan is taken hold of and pulled sideways, the way the block film
        /// is turned. Nothing repaints: the content is MOVED, so a drag cannot destroy the
        /// grid under the hand doing it.</summary>
        void DragSideways(RectTransform window)
        {
            var trigger = window.gameObject.AddComponent<EventTrigger>();
            var surface = window.gameObject.AddComponent<Image>();
            surface.color = new Color(1f, 1f, 1f, 0f);
            surface.raycastTarget = true;
            surface.transform.SetAsFirstSibling();

            var drag = new EventTrigger.Entry { eventID = EventTriggerType.Drag };
            drag.callback.AddListener(data =>
            {
                var pointer = (PointerEventData)data;
                blueprintPlanScroll = Mathf.Clamp(
                    blueprintPlanScroll - pointer.delta.x, 0f, blueprintPlanRun);
                if (blueprintPlan != null)
                    blueprintPlan.anchoredPosition =
                        new Vector2(-blueprintPlanScroll, 0f);
            });
            trigger.triggers.Add(drag);
        }

        /// <summary>The dark chip a door number is set in - red when that door is the one
        /// the form is open on.</summary>
        void DoorBadge(RectTransform parent, float x, float y, string door, bool picked)
        {
            var badge = NewRect("Badge", parent);
            var w = door.Length * 7.8f + 12f;
            PlaceTopLeft(badge, x, y, w, 18f);
            Fill(badge, picked ? LedgerV2.Red : LedgerV2.DarkPlate);
            Line(badge, LedgerStyle.Mono, 12f, LedgerV2.HeadCream, 6f, -1f, w - 12f, 16f,
                door).characterSpacing = 4f;
        }

        /// <summary>The 45° hatch a flat that is not ours - and the common ground - wears.
        /// Clipped by the cell's own mask, which is what keeps it inside the cell: the
        /// first cut let every stripe run the width of the sheet.</summary>
        Image Hatch(RectTransform tile, float w, float h)
        {
            var face = Fill(tile, BpHatchLight);
            face.raycastTarget = true;
            var span = w + h;
            for (var i = 0; i * 11f < span; i++)
            {
                var stripe = NewRect("Hatch", tile);
                stripe.anchorMin = stripe.anchorMax = new Vector2(0f, 1f);
                stripe.pivot = new Vector2(0f, 1f);
                stripe.anchoredPosition = new Vector2(i * 11f - h, 0f);
                stripe.sizeDelta = new Vector2(5f, span * 1.6f);
                stripe.localRotation = Quaternion.Euler(0f, 0f, -45f);
                var ink = stripe.gameObject.AddComponent<Image>();
                ink.color = BpHatchDark;
                ink.raycastTarget = false;
            }
            return face;
        }

        // ------------------------------------------------------------------ the caption

        TMP_Text bpCaptionDoor, bpCaptionName, bpCaptionLine, bpCaptionState;
        Image bpCaptionDot, bpCaptionBadge;

        float PaintBpCaption(RectTransform plan, float planW, float y)
        {
            var bar = NewRect("Caption", plan);
            PlaceTopLeft(bar, 0f, y, planW, BpCaptionH);
            Fill(bar, LedgerV2.PanelBand);
            Frame(bar, 1f, LedgerV2.Rule);

            var badge = NewRect("Caption badge", bar);
            PlaceTopLeft(badge, 12f, -(BpCaptionH - 18f) * 0.5f, 48f, 18f);
            bpCaptionBadge = Fill(badge, LedgerV2.DarkPlate);
            bpCaptionDoor = Line(badge, LedgerStyle.Mono, 10f, LedgerV2.HeadCream, 0f, -1f,
                48f, 16f, "—", TextAlignmentOptions.Midline);

            bpCaptionName = Line(bar, LedgerStyle.Type, 14.5f, LedgerV2.Ink, 74f,
                -(BpCaptionH - 20f) * 0.5f, 300f, 20f, "");
            bpCaptionName.overflowMode = TextOverflowModes.Ellipsis;

            bpCaptionLine = LedgerV2.Mono(bar, 388f, -(BpCaptionH - 18f) * 0.5f,
                Mathf.Max(120f, planW - 388f - 240f), "", 10.5f, LedgerV2.Muted, 1f);
            bpCaptionLine.overflowMode = TextOverflowModes.Ellipsis;

            bpCaptionDot = Block("Dot", bar, planW - 228f, -(BpCaptionH - 9f) * 0.5f, 9f,
                9f, LedgerV2.Faint);
            bpCaptionState = LedgerV2.Mono(bar, planW - 212f, -(BpCaptionH - 16f) * 0.5f,
                200f, "", 10.5f, LedgerV2.Faint, 5f);

            RefreshBpCaption();
            return y - BpCaptionH;
        }

        void RefreshBpCaption()
        {
            if (bpCaptionDoor == null)
                return;

            var unit = blueprintCaption.IsValid ? blueprintCaption : blueprintUnit;
            if (!unit.IsValid)
            {
                bpCaptionDoor.text = "—";
                bpCaptionName.text = "no door read yet";
                bpCaptionLine.text = "the pointer over a door names it here";
                bpCaptionState.text = "";
                bpCaptionDot.color = LedgerV2.Faint;
                return;
            }

            var state = StateOfFlat(unit, RosterDay);
            var ours = Apartments.TryGet(unit, out var record) &&
                       record.GangId == GangCatalog.PlayerGangId;

            bpCaptionBadge.color = blueprintFormOpen && blueprintUnit.Equals(unit)
                ? LedgerV2.Red : LedgerV2.DarkPlate;
            bpCaptionDoor.text = unit.Door;
            bpCaptionName.text = !ours ? TenantOf(unit)
                : string.IsNullOrEmpty(record.Name) ? UnitRoles.Label(record.Role)
                : record.Name;
            bpCaptionLine.text = !ours
                ? "the lease stands · " + Cash(AskPrice(unit)) + " to take the deed"
                : record.Role == UnitRole.Empty
                    ? "ours, and nothing runs out of it yet"
                    : UnitRoles.Of(record.Role).What;
            bpCaptionState.text = Apartments.Word(state);
            bpCaptionState.color = StateInk(state);
            bpCaptionDot.color = StateInk(state);
        }

        float PaintBpLegend(float y)
        {
            var legend = new (string, Color)[]
            {
                ("OPEN · EARNING", LedgerV2.Green),
                ("DARK · NO KEEPER", LedgerV2.PaperBlue),
                ("CLOSED · NO BANK", LedgerV2.Amber),
                ("RAIDED · SEALED", LedgerV2.Red),
                ("HATCHED · NOT OURS", LedgerV2.Faint),
            };
            var x = 0f;
            for (var i = 0; i < legend.Length; i++)
            {
                Block("Key", blueprintContent, x, y - 1f, 10f, 10f, legend[i].Item2);
                var label = LedgerV2.Mono(blueprintContent, x + 17f, y - 1f, 220f,
                    legend[i].Item1, 9.5f, LedgerV2.Muted, 4f);
                x += label.preferredWidth + 17f + 16f;
            }
            return y - 20f;
        }

        // ------------------------------------------------------------------ OUR FLATS

        float PaintBpOurFlats(ApartmentBuilding building, float y)
        {
            var inner = blueprintSheetW - BpPadX * 2f;
            var gang = GangCatalog.PlayerGangId;
            Apartments.OwnedIn(building.Id, gang, blueprintOurs);
            SortOurFlats();

            Line(blueprintContent, LedgerStyle.Type, 19f, LedgerV2.Ink, 0f, y, 300f, 24f,
                "OUR FLATS").characterSpacing = 4f;
            LedgerV2.Mono(blueprintContent, inner - 360f, y - 3f, 360f,
                "click a row for the full file", 11f, LedgerV2.Muted, 1f,
                TextAlignmentOptions.MidlineRight);
            y -= 26f;
            Rule(blueprintContent, 0f, y, inner, LedgerV2.SheetRule);
            y -= 9f;

            var cols = OurFlatColumns(inner);

            var head = NewRect("Our flats head", blueprintContent);
            PlaceTopLeft(head, 0f, y, inner, 24f);
            Fill(head, LedgerV2.Head);
            HeaderCell(head, cols[1], "ROLE", 1);
            HeaderCell(head, cols[2], "NAME", 2);
            HeaderCell(head, cols[3], "KEEPER", 0);
            HeaderCell(head, cols[4], "EARN", 0);
            HeaderCell(head, cols[5], "HEAT", 0);
            HeaderCell(head, cols[6], "STATUS", 0);
            y -= 24f;

            if (blueprintOurs.Count == 0)
            {
                LedgerV2.Mono(blueprintContent, 10f, y - 12f, inner - 20f,
                    "not one door of this building is on our deed", 11f, LedgerV2.Faint, 1f);
                return y - 34f;
            }

            var day = RosterDay;
            for (var i = 0; i < blueprintOurs.Count; i++)
            {
                var record = blueprintOurs[i];
                var row = NewRect("Flat " + record.Unit.Door, blueprintContent);
                PlaceTopLeft(row, 0f, y, inner, BpRowH);
                var face = Face(row, LedgerV2.Panel);
                Rule(row, 0f, -(BpRowH - 1f), inner, LedgerV2.Hair);

                var state = StateOfFlat(record.Unit, day);
                var spec = UnitRoles.Of(record.Role);

                DoorBadge(row, cols[0].x, -(BpRowH - 18f) * 0.5f, record.Unit.Door, false);
                Cell(row, cols[1], UnitRoles.Label(record.Role),
                    record.Role == UnitRole.Empty ? LedgerV2.Red : LedgerV2.Ink, 13.5f);
                Cell(row, cols[2],
                    string.IsNullOrEmpty(record.Name) ? "—" : record.Name,
                    LedgerV2.Body, 14f);
                Cell(row, cols[3], KeeperName(record.KeeperId),
                    record.KeeperId < 0 ? LedgerV2.Red : LedgerV2.Body, 11.5f);
                Cell(row, cols[4], spec.Earn > 0 ? Cash(spec.Earn) : "—",
                    spec.Earn > 0 && state == UnitState.Open ? LedgerV2.Green : LedgerV2.Faint,
                    12f);
                Cell(row, cols[5], spec.Heat > 0 ? spec.Heat + "/DAY" : "—",
                    spec.Heat >= 3 ? LedgerV2.Red : spec.Heat > 0 ? LedgerV2.Amber
                        : LedgerV2.Faint, 12f);

                var word = Apartments.Word(state);
                var stateText = Cell(row, cols[6], word, StateInk(state), 10.5f);
                Block("Dot", row,
                    cols[6].x + cols[6].w - stateText.preferredWidth - 16f,
                    -(BpRowH - 9f) * 0.5f, 9f, 9f, StateInk(state));

                var unit = record.Unit;
                RowButton(row, face, () => OpenFlatForm(unit));
                y -= BpRowH;
            }

            return y - 10f;
        }

        /// <summary>The prototype's grid: 56 · 150 · 1fr · 150 · 90 · 90 · 130, with 10 of
        /// gap and 10 of padding either side.</summary>
        (float x, float w, TextAlignmentOptions align)[] OurFlatColumns(float inner)
        {
            const float gap = 10f;
            const float pad = 10f;
            const float doorW = 56f, roleW = 150f, keeperW = 150f, earnW = 90f,
                heatW = 90f, statusW = 130f;
            var nameW = Mathf.Max(120f, inner - pad * 2f - doorW - roleW - keeperW -
                earnW - heatW - statusW - gap * 6f);

            var x = pad;
            var cols = new (float, float, TextAlignmentOptions)[7];
            cols[0] = (x, doorW, TextAlignmentOptions.MidlineLeft); x += doorW + gap;
            cols[1] = (x, roleW, TextAlignmentOptions.MidlineLeft); x += roleW + gap;
            cols[2] = (x, nameW, TextAlignmentOptions.MidlineLeft); x += nameW + gap;
            cols[3] = (x, keeperW, TextAlignmentOptions.MidlineLeft); x += keeperW + gap;
            cols[4] = (x, earnW, TextAlignmentOptions.MidlineRight); x += earnW + gap;
            cols[5] = (x, heatW, TextAlignmentOptions.MidlineRight); x += heatW + gap;
            cols[6] = (x, statusW, TextAlignmentOptions.MidlineRight);
            return cols;
        }

        void HeaderCell(RectTransform head, (float x, float w, TextAlignmentOptions align) col,
            string label, int sortKey)
        {
            // Set in the face's own letters: the typewriter has no ▲/▼.
            var arrow = sortKey > 0 && blueprintSort == sortKey
                ? blueprintSortDesc ? " v" : " ^"
                : "";
            var text = LedgerV2.Mono(head, col.x, -4f, col.w, label + arrow, 9.5f,
                sortKey > 0 && blueprintSort == sortKey ? LedgerV2.HeadCream : LedgerV2.HeadDim,
                10f, col.align);
            if (sortKey <= 0)
                return;

            var hit = NewRect("Sort " + label, head);
            PlaceTopLeft(hit, col.x - 4f, 0f, col.w + 8f, 24f);
            var face = hit.gameObject.AddComponent<Image>();
            face.color = new Color(1f, 1f, 1f, 0f);
            RowButton(hit, face, () =>
            {
                if (blueprintSort == sortKey)
                    blueprintSortDesc = !blueprintSortDesc;
                else
                {
                    blueprintSort = sortKey;
                    blueprintSortDesc = false;
                }
                dirty = true;
            });
            text.transform.SetAsLastSibling();
        }

        void SortOurFlats()
        {
            if (blueprintSort == 0)
                return;
            blueprintOurs.Sort((a, b) =>
            {
                var one = blueprintSort == 1
                    ? UnitRoles.Label(a.Role)
                    : string.IsNullOrEmpty(a.Name) ? "~" : a.Name;
                var two = blueprintSort == 1
                    ? UnitRoles.Label(b.Role)
                    : string.IsNullOrEmpty(b.Name) ? "~" : b.Name;
                var order = string.CompareOrdinal(one, two);
                return blueprintSortDesc ? -order : order;
            });
        }

        // ------------------------------------------------------------------ the reading

        UnitState StateOfFlat(ApartmentUnitId unit, int day)
        {
            var gang = GangCatalog.PlayerGangId;
            if (!Apartments.TryGet(unit, out var record) || record.GangId != gang)
                return UnitState.NotOurs;
            return Apartments.StateOf(unit, gang, day, KeeperStanding(record.KeeperId));
        }

        /// <summary>A keeper in a cell or a bed is not standing in the room, and the flat
        /// reads dark that day - the mark stays his, the work does not happen.</summary>
        bool KeeperStanding(int memberId)
        {
            if (memberId < 0)
                return false;
            var member = director != null && director.Roster != null
                ? director.Roster.Find(memberId)
                : null;
            return member != null && !member.Gone &&
                   member.Status == CharacterStatus.Active;
        }

        string KeeperName(int memberId)
        {
            if (memberId < 0)
                return "NOBODY";
            var member = director != null && director.Roster != null
                ? director.Roster.Find(memberId)
                : null;
            return member != null ? member.FullName : "NOBODY";
        }

        static Color StateInk(UnitState state) => state switch
        {
            UnitState.Open => LedgerV2.Green,
            UnitState.Dark => LedgerV2.PaperBlue,
            UnitState.NoBank => LedgerV2.Amber,
            UnitState.Raided => LedgerV2.Red,
            _ => LedgerV2.Faint,
        };

        /// <summary>What the seller asks. The price authority is
        /// <c>EconomyPrices.Apartment</c>; the variance is the flat's own stream, so the
        /// same city always asks the same money for the same door.</summary>
        static int AskPrice(ApartmentUnitId unit)
        {
            var spread = (UnitHash(unit) % 9) - 4;
            return Outfit.EconomyPrices.Apartment + spread * 2_500;
        }

        static readonly string[] TenantNames =
        {
            "GOLDBERG, S.", "PRZYBYLSKI, H.", "VOLKOV, M.", "KOWALCZYK, J.", "ABRUZZO, R.",
            "HANNIGAN, T.", "DE LUCA, P.", "SZABO, E.", "MURPHY, C.", "WEISS, A.",
            "FINNEGAN, D.", "LIPSKI, W.", "ROSSI, N.", "BAUER, K.", "CONTE, V.",
            "NOVAK, S.", "HALLORAN, B.", "STRAND, O.", "MARCHESI, L.", "OSTROWSKI, F.",
        };

        /// <summary>Who lives there now. Off the flat's own hash, so the man on the lease
        /// is the same man every time the sheet is opened.</summary>
        static string TenantOf(ApartmentUnitId unit) =>
            TenantNames[UnitHash(unit) % TenantNames.Length];

        static int UnitHash(ApartmentUnitId unit)
        {
            unchecked
            {
                var hash = (int)2166136261;
                var value = unit.ToString();
                for (var i = 0; i < value.Length; i++)
                    hash = (hash ^ value[i]) * 16777619;
                return hash & int.MaxValue;
            }
        }

        /// <summary>
        /// A fill that can be CLICKED. `LedgerKit.Fill` is documented as "a flat fill,
        /// never a raycast target" - a row painted with it and given a Button is dead to
        /// the pointer, which is what happened to every cell of this sheet on its first
        /// run. The fill and the surface are one Image, because a second Image on one
        /// GameObject comes back null from AddComponent.
        /// </summary>
        static Image Face(RectTransform rect, Color colour)
        {
            var face = Fill(rect, colour);
            face.raycastTarget = true;
            return face;
        }

        /// <summary>One cell of a ruled row, on the column the grid measured. The design
        /// sets the words in the display face and the figures in the typewriter.</summary>
        static TextMeshProUGUI Cell(RectTransform row,
            (float x, float w, TextAlignmentOptions align) col, string text, Color ink,
            float size) =>
            Line(row, size >= 13f ? LedgerStyle.Type : LedgerStyle.Mono, size, ink,
                col.x, -(BpRowH - 20f) * 0.5f, col.w, 20f, text, col.align);

        static string Cash(int amount) => LedgerText.Cash(amount);

        static string Spell(int n) => n switch
        {
            1 => "ONE", 2 => "TWO", 3 => "THREE", 4 => "FOUR", 5 => "FIVE", 6 => "SIX",
            7 => "SEVEN", 8 => "EIGHT", 9 => "NINE", 10 => "TEN",
            _ => n.ToString(),
        };
    }
}
