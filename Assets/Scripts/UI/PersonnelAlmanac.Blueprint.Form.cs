using System.Collections.Generic;
using LivingCity.Gangs;
using LivingCity.Personnel;
using LivingCity.Property;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static LivingCity.UI.LedgerKit;

namespace LivingCity.UI
{
    /// <summary>
    /// THE PREMISES FORM — one flat, on its own sheet over the blueprint (EPIC 27).
    ///
    /// Three modes, and which one it opens in is never the player's to pick:
    ///   * BUY, when the flat is not ours - the deed and what the rooms take, nothing else;
    ///   * DETAIL, when it is ours and has a role - the room read back, not the pickers;
    ///   * EDIT, when it is ours and has no role yet, or the player asked to change it.
    ///
    /// Buying does not close the form. The flat becomes ours and, having no role, the same
    /// paper lands straight in EDIT: one form, no second trip.
    ///
    /// Every measure is the prototype's: the 1010 sheet under a 34 drop, the 14/20/15 head,
    /// sections at 13/20/14 between hairlines, the 14-unit marks in both pickers, the 322
    /// the men's list may grow to, and the footer's 13/20/16 with its 280-wide primary key.
    /// </summary>
    public sealed partial class PersonnelAlmanac
    {
        const float FormW = 1010f;
        const float FormTop = 34f;
        const float FormHeadH = 88f;
        const float FormPadX = 20f;
        const float FormRowH = 36f;
        const float FormPickHeadH = 24f;
        const float FormMenMax = 322f;
        const float FormFootH = 146f;

        /// <summary>True while the caret is in the name field. The page is repainted on
        /// world events - a man moving, a gang stirring - and a repaint destroys the field
        /// under the player's hands, so the paint waits until they are done typing.</summary>
        bool blueprintTyping;

        void OpenFlatForm(ApartmentUnitId unit)
        {
            blueprintUnit = unit;
            blueprintCaption = unit;
            blueprintFormOpen = true;
            blueprintNote = "";

            var ours = Apartments.TryGet(unit, out var record) &&
                       record.GangId == GangCatalog.PlayerGangId;
            draftRole = ours ? record.Role : UnitRole.Empty;
            draftKeeper = ours ? record.KeeperId : -1;
            draftName = ours ? record.Name : "";
            draftDirty = false;

            // An owned flat with a role opens READ-ONLY. The pickers are a decision, not
            // the first thing a reader is shown.
            blueprintEditing = ours && draftRole == UnitRole.Empty;
            dirty = true;
        }

        void CloseFlatForm()
        {
            blueprintFormOpen = false;
            blueprintEditing = false;
            dirty = true;
        }

        void PaintFlatForm(ApartmentBuilding building)
        {
            var unit = blueprintUnit;
            var gang = GangCatalog.PlayerGangId;
            var ours = Apartments.TryGet(unit, out var record) && record.GangId == gang;
            var state = StateOfFlat(unit, RosterDay);

            var shade = NewRect("Form backdrop", blueprintForm);
            Stretch(shade);
            var shadeFace = shade.gameObject.AddComponent<Image>();
            shadeFace.color = BpBackdrop;
            var dismiss = shade.gameObject.AddComponent<Button>();
            dismiss.targetGraphic = shadeFace;
            dismiss.transition = Selectable.Transition.None;
            dismiss.onClick.AddListener(CloseFlatForm);

            var half = FormW * 0.5f;
            var bodyH = Mathf.Max(MeasureFormLeft(ours), MeasureFormRight(ours, record));
            var formH = FormHeadH + bodyH + FormFootH;

            var sheet = NewRect("Form", blueprintForm);
            PlaceTopLeft(sheet, PageLeft + (PageWidth - FormW) * 0.5f, PageTop - FormTop,
                FormW, formH);
            var paper = Fill(sheet, LedgerV2.Panel);
            paper.raycastTarget = true;      // the paper is not the backdrop
            sheet.gameObject.AddComponent<Button>().transition = Selectable.Transition.None;
            Frame(sheet, 2f, LedgerV2.Ink);

            PaintFormHead(sheet, building, unit, state);

            var body = NewRect("Body", sheet);
            PlaceTopLeft(body, 0f, -FormHeadH, FormW, bodyH);
            Block("Divider", body, half, 0f, 1f, bodyH, LedgerV2.Hair);

            PaintFormLeft(body, unit, record, ours, 0f, half);
            PaintFormRight(body, unit, record, ours, half, half);
            PaintFormFoot(sheet, unit, record, ours, state, FormHeadH + bodyH);
        }

        // ------------------------------------------------------------------ the head

        void PaintFormHead(RectTransform sheet, ApartmentBuilding building,
            ApartmentUnitId unit, UnitState state)
        {
            var band = NewRect("Form head", sheet);
            PlaceTopLeft(band, 0f, 0f, FormW, FormHeadH);
            Fill(band, LedgerV2.Head);

            LedgerV2.Mono(band, FormPadX, -14f, FormW - 320f,
                "PREMISES FORM · " + building.Address, 9.5f, LedgerV2.HeadDim, 18f);
            Line(band, LedgerStyle.Type, 27f, LedgerV2.HeadCream, FormPadX, -28f, 520f, 34f,
                "FLAT " + unit.Door);
            LedgerV2.Mono(band, FormPadX, -64f, 520f,
                Ordinal(unit.Floor) + " FLOOR · DOOR " +
                ApartmentBuildings.DoorLetter(unit.Slot), 11f, LedgerV2.HeadDim, 2f);

            var word = state == UnitState.Raided && Apartments.TryGet(unit, out var raided)
                ? "RAIDED — CLOSED UNTIL DAY " + raided.RaidUntilDay
                : Apartments.Word(state);
            var stateW = word.Length * 7.4f + 12f;
            var closeX = FormW - FormPadX - 28f;
            Block("State", band, closeX - 12f - stateW - 19f, -25f, 10f, 10f,
                StateInk(state));
            LedgerV2.Mono(band, closeX - 12f - stateW, -27f, stateW, word, 11.5f,
                StateInk(state), 6f);

            // The typewriter face has no ✕ (U+2715): it printed as a stray letter.
            var close = LedgerV2.Button(band, "X", closeX, -22f, 28f, 28f,
                CloseFlatForm, LedgerV2.Key.Dark, 14f);
            close.color = LedgerV2.HeadInk;
        }

        // ------------------------------------------------------------------ left column

        const float FormDeedH = 76f;
        const float FormNameH = 88f;

        float MeasureFormLeft(bool ours)
        {
            var body = FormDeedH + FormNameH;
            if (!ours)
                return body + 190f;                       // the roles preview
            return body + (blueprintEditing
                ? 44f + FormPickHeadH + UnitRoles.All.Length * FormRowH + 16f
                : 122f);
        }

        void PaintFormLeft(RectTransform body, ApartmentUnitId unit,
            ApartmentRecord record, bool ours, float x, float w)
        {
            var y = 0f;

            var deed = Section(body, x, y, w, FormDeedH, true);
            SectionLabel(deed, "1 · THE DEED");
            LedgerV2.Mono(deed, w - FormPadX - 280f, -13f, 280f,
                ours ? "signed on day " + record.BoughtOnDay
                    : "cash, at the table, the day it is signed",
                10f, LedgerV2.Label, 6f, TextAlignmentOptions.MidlineRight);
            Line(deed, LedgerStyle.Type, 29f, ours ? LedgerV2.Green : LedgerV2.Ink,
                FormPadX, -30f, w - FormPadX * 2f, 36f,
                ours ? "ON OUR DEED" : Cash(AskPrice(unit)));
            y -= FormDeedH;

            var name = Section(body, x, y, w, FormNameH, true);
            SectionLabel(name, "2 · THE NAME ON THE DOOR");
            if (ours)
            {
                Field(name, FormPadX, -32f, w - FormPadX * 2f, 30f, draftName,
                    value =>
                    {
                        draftName = value;
                        draftDirty = true;
                    },
                    focused => blueprintTyping = focused, 16f);
                LedgerV2.Mono(name, FormPadX, -68f, w - FormPadX * 2f,
                    "what the street will call it", 10f, LedgerV2.Label, 1f);
            }
            else
            {
                Line(name, LedgerStyle.Mono, 16f, LedgerV2.Faint, FormPadX, -34f,
                    w - FormPadX * 2f, 24f, TenantOf(unit)).characterSpacing = 6f;
                Rule(name, FormPadX, -60f, w - FormPadX * 2f, LedgerV2.Rule);
                LedgerV2.Mono(name, FormPadX, -68f, w - FormPadX * 2f,
                    "the lease stands until the deed changes hands", 10f, LedgerV2.Label, 1f);
            }
            y -= FormNameH;

            if (!ours)
            {
                PaintRolePreview(body, x, y, w);
                return;
            }

            if (blueprintEditing)
                PaintRolePicker(body, x, y, w);
            else
                PaintRoleFile(body, record, x, y, w);
        }

        void PaintRolePreview(RectTransform body, float x, float y, float w)
        {
            var box = Section(body, x, y, w, 190f, false);
            SectionLabel(box, "3 · WHAT RUNS OUT OF IT");
            Line(box, LedgerStyle.MonoItalic, 14f, LedgerV2.Body, FormPadX, -30f,
                w - FormPadX * 2f, 24f,
                "Not set until the deed is ours. What the rooms take, a day:");

            var line = -60f;
            for (var i = 0; i < UnitRoles.All.Length; i++)
            {
                var spec = UnitRoles.All[i];
                LedgerV2.Mono(box, FormPadX, line, w * 0.5f, spec.Label, 10f,
                    LedgerV2.Body, 4f);
                LedgerV2.Mono(box, w * 0.5f, line, w * 0.5f - FormPadX,
                    spec.Earn > 0 ? Cash(spec.Earn) + "/DAY OPEN" : "HOLDS, TAKES NOTHING",
                    10f, spec.Earn > 0 ? LedgerV2.Green : LedgerV2.Faint, 2f,
                    TextAlignmentOptions.MidlineRight);
                line -= 17f;
            }
        }

        void PaintRoleFile(RectTransform body, ApartmentRecord record, float x, float y,
            float w)
        {
            var spec = UnitRoles.Of(record.Role);
            var box = Section(body, x, y, w, 122f, false);
            SectionLabel(box, "3 · WHAT RUNS OUT OF IT");
            Line(box, LedgerStyle.Type, 24f, LedgerV2.Ink, FormPadX, -30f,
                w - FormPadX * 2f, 30f, spec.Label);

            LedgerV2.Mono(box, FormPadX, -70f, 180f, "FIT-OUT PAID", 9.5f,
                LedgerV2.Label, 10f);
            Line(box, LedgerStyle.Mono, 15f, LedgerV2.Body, FormPadX, -86f, 180f, 20f,
                record.PaidRole == record.Role ? Cash(spec.FitOut) : "NOT PAID");

            LedgerV2.Mono(box, FormPadX + 204f, -70f, 200f, "HEAT WHILE OPEN", 9.5f,
                LedgerV2.Label, 10f);
            Line(box, LedgerStyle.Mono, 15f,
                spec.Heat >= 3 ? LedgerV2.Red : spec.Heat > 0 ? LedgerV2.Amber
                    : LedgerV2.Green,
                FormPadX + 204f, -86f, 200f, 20f, spec.Heat + " A DAY");
        }

        void PaintRolePicker(RectTransform body, float x, float y, float w)
        {
            var h = 44f + FormPickHeadH + UnitRoles.All.Length * FormRowH + 16f;
            var box = Section(body, x, y, w, h, false);
            SectionLabel(box, "3 · WHAT RUNS OUT OF IT");

            var inner = w - FormPadX * 2f;
            var cols = RoleColumns(inner);

            var head = NewRect("Roles head", box);
            PlaceTopLeft(head, FormPadX, -30f, inner, FormPickHeadH);
            Fill(head, LedgerV2.Head);
            LedgerV2.Mono(head, cols[1].x, -4f, cols[1].w, "ROLE", 9.5f, LedgerV2.HeadDim, 10f);
            LedgerV2.Mono(head, cols[2].x, -4f, cols[2].w, "EARN", 9.5f, LedgerV2.HeadDim,
                10f, TextAlignmentOptions.MidlineRight);
            LedgerV2.Mono(head, cols[3].x, -4f, cols[3].w, "FIT-OUT", 9.5f, LedgerV2.HeadDim,
                10f, TextAlignmentOptions.MidlineRight);
            LedgerV2.Mono(head, cols[4].x, -4f, cols[4].w, "HEAT", 9.5f, LedgerV2.HeadDim,
                10f, TextAlignmentOptions.MidlineRight);

            var list = NewRect("Roles", box);
            PlaceTopLeft(list, FormPadX, -30f - FormPickHeadH, inner,
                UnitRoles.All.Length * FormRowH);
            Frame(list, 1f, LedgerV2.Rule);

            for (var i = 0; i < UnitRoles.All.Length; i++)
            {
                var spec = UnitRoles.All[i];
                var picked = draftRole == spec.Role;

                var row = NewRect("Role " + spec.Label, list);
                PlaceTopLeft(row, 0f, -i * FormRowH, inner, FormRowH);
                var face = Face(row, picked ? LedgerV2.Picked : LedgerV2.Panel);
                Rule(row, 0f, -(FormRowH - 1f), inner, LedgerV2.Hair);

                var mark = NewRect("Mark", row);
                PlaceTopLeft(mark, cols[0].x, -(FormRowH - 14f) * 0.5f, 14f, 14f);
                Fill(mark, picked ? LedgerV2.Ink : LedgerV2.Panel);
                Frame(mark, 1f, LedgerV2.Label);

                Line(row, LedgerStyle.Type, 14.5f, LedgerV2.Ink, cols[1].x,
                    -(FormRowH - 20f) * 0.5f, cols[1].w, 20f, spec.Label);
                Line(row, LedgerStyle.Mono, 11.5f,
                    spec.Earn > 0 ? LedgerV2.Green : LedgerV2.Faint, cols[2].x,
                    -(FormRowH - 20f) * 0.5f, cols[2].w, 20f,
                    spec.Earn > 0 ? Cash(spec.Earn) : "—",
                    TextAlignmentOptions.MidlineRight);
                Line(row, LedgerStyle.Mono, 12.5f, LedgerV2.Body, cols[3].x,
                    -(FormRowH - 20f) * 0.5f, cols[3].w, 20f, Cash(spec.FitOut),
                    TextAlignmentOptions.MidlineRight);
                Line(row, LedgerStyle.Mono, 12f,
                    spec.Heat >= 3 ? LedgerV2.Red : spec.Heat > 0 ? LedgerV2.Amber
                        : LedgerV2.Faint,
                    cols[4].x, -(FormRowH - 20f) * 0.5f, cols[4].w, 20f,
                    spec.Heat.ToString(), TextAlignmentOptions.MidlineRight);

                var role = spec.Role;
                RowButton(row, face, () =>
                {
                    draftRole = role;
                    draftDirty = true;
                    dirty = true;
                });
            }
        }

        /// <summary>The picker's grid: 14 · 1fr · 76 · 84 · 58, 10 of gap, 12 of padding.</summary>
        (float x, float w)[] RoleColumns(float inner)
        {
            const float gap = 10f, pad = 12f, markW = 14f, earnW = 76f, costW = 84f,
                heatW = 58f;
            var labelW = inner - pad * 2f - markW - earnW - costW - heatW - gap * 4f;
            var x = pad;
            var cols = new (float, float)[5];
            cols[0] = (x, markW); x += markW + gap;
            cols[1] = (x, labelW); x += labelW + gap;
            cols[2] = (x, earnW); x += earnW + gap;
            cols[3] = (x, costW); x += costW + gap;
            cols[4] = (x, heatW);
            return cols;
        }

        // ------------------------------------------------------------------ right column

        float MeasureFormRight(bool ours, ApartmentRecord record)
        {
            if (!ours)
                return 130f;

            var role = blueprintEditing ? draftRole
                : record != null ? record.Role : UnitRole.Empty;
            var h = role != UnitRole.Empty ? ContentsHeight(role) : 0f;

            if (!blueprintEditing)
                return h + 144f;

            var men = director != null && director.Roster != null
                ? director.Roster.Members.Count
                : 0;
            var list = Mathf.Min(FormMenMax, Mathf.Max(2, men) * FormRowH);
            return h + 44f + FormPickHeadH + list + 58f;
        }

        float ContentsHeight(UnitRole role) =>
            66f + (UnitRoles.StaffCeiling(role) > 0 ? 26f : 0f) +
            (UnitRoles.Of(role).NeedsBank ? 26f : 0f);

        void PaintFormRight(RectTransform body, ApartmentUnitId unit,
            ApartmentRecord record, bool ours, float x, float w)
        {
            var y = 0f;

            if (!ours)
            {
                var note = Section(body, x, y, w, 130f, false);
                SectionLabel(note, "ONCE IT'S OURS");
                Line(note, LedgerStyle.MonoItalic, 14f, LedgerV2.Body, FormPadX, -32f,
                    w - FormPadX * 2f, 70f,
                    "Buy the deed and this same paper reopens straight to the role and " +
                    "the keeper — one form, no second trip.");
                return;
            }

            var role = blueprintEditing ? draftRole : record.Role;
            if (role != UnitRole.Empty)
                y = PaintRoomContents(body, unit, record, role, x, y, w);

            if (blueprintEditing)
                PaintKeeperPicker(body, x, y, w);
            else
                PaintKeeperFile(body, record, x, y, w);
        }

        /// <summary>
        /// WHAT'S IN THE ROOM. Every figure is the simulation's own - the money actually
        /// behind the table, the hands actually hired - and the two things the room can be
        /// given are given here, where the room is read.
        /// </summary>
        float PaintRoomContents(RectTransform body, ApartmentUnitId unit,
            ApartmentRecord record, UnitRole role, float x, float y, float w)
        {
            var spec = UnitRoles.Of(role);
            var h = ContentsHeight(role);
            var box = Section(body, x, y, w, h, true);
            Fill(box, LedgerV2.PanelDark);

            SectionLabel(box, "WHAT'S IN THE ROOM");
            LedgerV2.Mono(box, w - FormPadX - 320f, -13f, 320f,
                spec.Earn > 0 ? Cash(spec.Earn) + "/DAY WHILE OPEN"
                    : "NO DIRECT TAKE — JUST HOLDS IT",
                10.5f, spec.Earn > 0 ? LedgerV2.Green : LedgerV2.Muted, 6f,
                TextAlignmentOptions.MidlineRight);

            Rule(box, FormPadX, -32f, w - FormPadX * 2f, LedgerV2.Hair);
            Line(box, LedgerStyle.Type, 13.5f, LedgerV2.Body, FormPadX, -38f,
                w - FormPadX * 2f, 20f, spec.What);

            var line = -64f;
            if (UnitRoles.StaffCeiling(role) > 0)
            {
                var staff = record.Staff;
                var ceiling = UnitRoles.StaffCeiling(role);
                line = ContentsRow(box, w, line, UnitRoles.StaffWord(role),
                    staff + " / " + ceiling + " · " +
                    Cash(staff * UnitRoles.StaffWage(role)) + " A DAY",
                    () => Apartments.SetStaff(unit, staff - 1),
                    () => Apartments.SetStaff(unit, Mathf.Min(ceiling, staff + 1)));
            }

            if (spec.NeedsBank)
            {
                var bank = record.Bank;
                line = ContentsRow(box, w, line, "THE BANK",
                    bank > 0 ? Cash(bank) + " behind the table" : "nothing behind it",
                    null, () => StakeTheBank(unit, bank));
            }

            return y - h;
        }

        /// <summary>One line of the room's own contents, with the keys that change it.</summary>
        float ContentsRow(RectTransform box, float w, float y, string label, string value,
            System.Action less, System.Action more)
        {
            Line(box, LedgerStyle.Type, 13.5f, LedgerV2.Body, FormPadX, y, w * 0.4f, 20f,
                label);
            Line(box, LedgerStyle.Mono, 13f, LedgerV2.Ink, FormPadX + w * 0.4f, y,
                w * 0.6f - FormPadX * 2f - 80f, 20f, value,
                TextAlignmentOptions.MidlineRight);

            if (less != null)
                LedgerV2.Button(box, "-", w - FormPadX - 74f, y - 1f, 34f, 22f,
                    () => { less(); dirty = true; }, LedgerV2.Key.Outline, 12f);
            LedgerV2.Button(box, "+", w - FormPadX - 34f, y - 1f, 34f, 22f,
                () => { more(); dirty = true; }, LedgerV2.Key.Outline, 12f);
            return y - 26f;
        }

        void StakeTheBank(ApartmentUnitId unit, int bank)
        {
            const int stake = 2_000;
            var result = outfit != null
                ? outfit.Purchase(stake, "the bank at " + unit.Door)
                : OpResult.Fail("the outfit's books are not open");
            if (!result.Ok)
            {
                blueprintNote = result.Reason;
                return;
            }
            Apartments.SetBank(unit, bank + stake);
        }

        void PaintKeeperFile(RectTransform body, ApartmentRecord record, float x, float y,
            float w)
        {
            var box = Section(body, x, y, w, 144f, false);
            SectionLabel(box, "4 · WHO KEEPS IT");

            var member = director != null && director.Roster != null
                ? director.Roster.Find(record.KeeperId)
                : null;

            if (member == null)
            {
                Line(box, LedgerStyle.Mono, 13f, LedgerV2.PaperBlue, FormPadX, -34f,
                    w - FormPadX * 2f, 20f, "NO KEEPER — THE FLAT READS DARK");
            }
            else
            {
                Line(box, LedgerStyle.Type, 18f, LedgerV2.Ink, FormPadX, -32f,
                    w - FormPadX * 2f - 100f, 24f, member.FullName);
                LedgerV2.Mono(box, FormPadX, -54f, w - FormPadX * 2f - 100f,
                    LedgerText.RankLabel(member.Rank).ToUpperInvariant() + " · KEEPS " +
                    record.Unit.Door, 11f, LedgerV2.Label, 5f);
                Stars(box, w - FormPadX - 85f, -42f,
                    member.GetHalfSteps(UnitRoles.Of(record.Role).Wants), 15f, 17f);
            }

            Line(box, LedgerStyle.MonoItalic, 13f, LedgerV2.Body, FormPadX, -86f,
                w - FormPadX * 2f, 44f,
                "A keeper is off the street. Pull him into a crew and the flat goes dark " +
                "that moment.");
        }

        void PaintKeeperPicker(RectTransform body, float x, float y, float w)
        {
            var roster = director != null ? director.Roster : null;
            var men = new List<Character>();
            if (roster != null)
                for (var i = 0; i < roster.Members.Count; i++)
                    if (!roster.Members[i].Gone)
                        men.Add(roster.Members[i]);

            men.Sort((a, b) =>
            {
                var one = a.Id == draftKeeper ? 0 : 1;
                var two = b.Id == draftKeeper ? 0 : 1;
                return one != two ? one - two
                    : string.CompareOrdinal(a.FullName, b.FullName);
            });

            var listH = Mathf.Min(FormMenMax, Mathf.Max(2, men.Count) * FormRowH);
            var h = 44f + FormPickHeadH + listH + 58f;
            var box = Section(body, x, y, w, h, false);
            SectionLabel(box, "4 · WHO KEEPS IT");
            LedgerV2.Mono(box, w - FormPadX - 300f, -13f, 300f,
                draftRole == UnitRole.Empty ? "a role first" : "one man, one flat", 10f,
                LedgerV2.Label, 6f, TextAlignmentOptions.MidlineRight);

            var inner = w - FormPadX * 2f;
            var cols = KeeperColumns(inner);

            var head = NewRect("Men head", box);
            PlaceTopLeft(head, FormPadX, -30f, inner, FormPickHeadH);
            Fill(head, LedgerV2.Head);
            LedgerV2.Mono(head, cols[1].x, -4f, cols[1].w, "OUR MEN", 9.5f,
                LedgerV2.HeadDim, 10f);
            LedgerV2.Mono(head, cols[2].x, -4f, cols[2].w, "FIT", 9.5f, LedgerV2.HeadDim,
                10f, TextAlignmentOptions.MidlineRight);
            LedgerV2.Mono(head, cols[3].x, -4f, cols[3].w, "STANDING", 9.5f,
                LedgerV2.HeadDim, 10f, TextAlignmentOptions.MidlineRight);

            var window = NewRect("Men window", box);
            PlaceTopLeft(window, FormPadX, -30f - FormPickHeadH, inner, listH);
            window.gameObject.AddComponent<RectMask2D>();
            Frame(window, 1f, LedgerV2.Rule);

            var spec = UnitRoles.Of(draftRole);
            var rows = Mathf.FloorToInt(listH / FormRowH);
            for (var i = 0; i < men.Count && i < rows; i++)
            {
                var member = men[i];
                var kept = Apartments.KeptBy(member.Id);
                var free = RosterOps.CanKeep(roster, member.Id, out var why);
                var note = member.Id == draftKeeper ? "KEEPER"
                    : kept.IsValid ? "keeps " + kept.Door
                    : free ? "" : why;
                var may = free && draftRole != UnitRole.Empty &&
                          (!kept.IsValid || kept.Equals(blueprintUnit));
                var picked = member.Id == draftKeeper;

                var row = NewRect("Man " + member.Id, window);
                PlaceTopLeft(row, 0f, -i * FormRowH, inner, FormRowH);
                var face = Face(row, picked ? LedgerV2.Picked : LedgerV2.Panel);
                Rule(row, 0f, -(FormRowH - 1f), inner, LedgerV2.Hair);

                var mark = NewRect("Mark", row);
                PlaceTopLeft(mark, cols[0].x, -(FormRowH - 14f) * 0.5f, 14f, 14f);
                Fill(mark, picked ? LedgerV2.Ink : LedgerV2.Panel);
                Frame(mark, 1f, may ? LedgerV2.Label : LedgerV2.Hair);

                Line(row, LedgerStyle.Type, 15f, may ? LedgerV2.Ink : LedgerV2.Faint,
                    cols[1].x, -6f, cols[1].w, 18f, member.FullName);
                LedgerV2.Mono(row, cols[1].x, -20f, cols[1].w,
                    LedgerText.RankLabel(member.Rank).ToUpperInvariant() + " · " +
                    PostOf(member), 10f, LedgerV2.Label, 8f);
                Stars(row, cols[2].x + cols[2].w - 62f, -(FormRowH * 0.5f),
                    member.GetHalfSteps(spec.Wants), 12f, 13f);
                LedgerV2.Mono(row, cols[3].x, -(FormRowH - 16f) * 0.5f, cols[3].w, note,
                    10f, picked ? LedgerV2.Ink : LedgerV2.Faint, 6f,
                    TextAlignmentOptions.MidlineRight);

                if (may)
                {
                    var id = member.Id;
                    RowButton(row, face, () =>
                    {
                        draftKeeper = draftKeeper == id ? -1 : id;
                        draftDirty = true;
                        dirty = true;
                    });
                }
            }

            Line(box, LedgerStyle.MonoItalic, 13f, LedgerV2.Body, FormPadX,
                -30f - FormPickHeadH - listH - 9f, inner, 40f,
                "A keeper is off the street. Pull him into a crew and the flat goes dark " +
                "that moment.");
        }

        /// <summary>The men's grid: 14 · 1fr · 68 · 74, 12 of gap and 12 of padding.</summary>
        (float x, float w)[] KeeperColumns(float inner)
        {
            const float gap = 12f, pad = 12f, markW = 14f, fitW = 68f, noteW = 74f;
            var nameW = inner - pad * 2f - markW - fitW - noteW - gap * 3f;
            var x = pad;
            var cols = new (float, float)[4];
            cols[0] = (x, markW); x += markW + gap;
            cols[1] = (x, nameW); x += nameW + gap;
            cols[2] = (x, fitW); x += fitW + gap;
            cols[3] = (x, noteW);
            return cols;
        }

        string PostOf(Character member)
        {
            var roster = director != null ? director.Roster : null;
            if (roster == null)
                return "";
            if (member.Duty == Duty.Keeper)
                return "IN A ROOM";
            return roster.CrewOf(member.Id) != null ? "IN A CREW" : "IN THE POOL";
        }

        // ------------------------------------------------------------------ the footer

        void PaintFormFoot(RectTransform sheet, ApartmentUnitId unit,
            ApartmentRecord record, bool ours, UnitState state, float top)
        {
            var band = NewRect("Form foot", sheet);
            PlaceTopLeft(band, 0f, -top, FormW, FormFootH);
            Fill(band, LedgerV2.PanelBand);
            Rule(band, 0f, 0f, FormW, LedgerV2.Rule);

            var role = blueprintEditing ? draftRole
                : record != null ? record.Role : UnitRole.Empty;
            var spec = UnitRoles.Of(role);
            var owed = ours && blueprintEditing && role != UnitRole.Empty &&
                       (record == null || record.PaidRole != role)
                ? spec.FitOut
                : 0;

            var bill = new (string label, string value, Color ink)[]
            {
                ("DEED", ours ? "PAID" : Cash(AskPrice(unit)) + " OWED",
                    ours ? LedgerV2.Green : LedgerV2.Ink),
                ("FIT-OUT DUE NOW", owed > 0 ? Cash(owed) : "—",
                    owed > 0 ? LedgerV2.Amber : LedgerV2.Faint),
                ("HEAT WHILE OPEN", spec.Heat + " A DAY",
                    spec.Heat >= 3 ? LedgerV2.Red : spec.Heat > 0 ? LedgerV2.Amber
                        : LedgerV2.Faint),
            };
            var x = FormPadX;
            for (var i = 0; i < bill.Length; i++)
            {
                LedgerV2.Mono(band, x, -13f, 230f, bill[i].label, 9.5f, LedgerV2.Label, 13f);
                Line(band, LedgerStyle.Mono, 15f, bill[i].ink, x, -29f, 230f, 20f,
                    bill[i].value);
                x += 230f;
            }

            var keyY = -62f;
            var right = FormW - FormPadX;
            var keeper = ours && record != null ? record.KeeperId : -1;

            if (keeper >= 0)
            {
                right -= 152f;
                LedgerV2.Button(band, "PULL HIM OUT", right, keyY, 152f, 40f,
                    () => PullKeeperOut(unit), LedgerV2.Key.Red, 11.5f);
                right -= 8f;
            }

            if (blueprintEditing && ours && record != null && record.Role != UnitRole.Empty)
            {
                right -= 118f;
                LedgerV2.Button(band, "CANCEL", right, keyY, 118f, 40f, () =>
                {
                    draftRole = record.Role;
                    draftKeeper = record.KeeperId;
                    draftName = record.Name;
                    draftDirty = false;
                    blueprintEditing = false;
                    blueprintNote = "";
                    dirty = true;
                }, LedgerV2.Key.Outline, 11.5f);
                right -= 8f;
            }

            var primaryW = Mathf.Max(280f, right - FormPadX);
            if (!ours)
            {
                LedgerV2.Button(band, "BUY " + Cash(AskPrice(unit)), FormPadX, keyY,
                    primaryW, 40f, () => BuyFlat(unit), LedgerV2.Key.Dark, 13f);
            }
            else if (!blueprintEditing)
            {
                LedgerV2.Button(band, "EDIT ROLE & KEEPER", FormPadX, keyY, primaryW, 40f,
                    () =>
                    {
                        blueprintEditing = true;
                        draftDirty = false;
                        dirty = true;
                    }, LedgerV2.Key.Dark, 13f);
            }
            else
            {
                var save = LedgerV2.Button(band, "SAVE", FormPadX, keyY, primaryW, 40f,
                    () => SaveFlat(unit), LedgerV2.Key.Dark, 13f);
                LedgerV2.KeyEnabled(save, draftDirty, LedgerV2.HeadDim);
            }

            var reason = string.IsNullOrEmpty(blueprintNote)
                ? ReasonFor(unit, record, ours, state, out var ink)
                : Note(out ink);
            LedgerV2.Mono(band, FormPadX, -114f, FormW - FormPadX * 2f, reason, 11f, ink, 1f);
        }

        string Note(out Color ink)
        {
            ink = LedgerV2.Red;
            return blueprintNote;
        }

        /// <summary>
        /// The one contextual line, in the order that decides which of them is true - the
        /// prototype's own precedence: the standing lease, the missing role, the
        /// precinct's seal, the empty bank, the missing keeper, and then what the room
        /// costs to run.
        /// </summary>
        string ReasonFor(ApartmentUnitId unit, ApartmentRecord record, bool ours,
            UnitState state, out Color ink)
        {
            if (!ours)
            {
                ink = LedgerV2.PaperBlue;
                return "The lease stands until the deed changes hands. Buy the flat, " +
                       "name a role and a keeper, then save it all in one line.";
            }

            var role = blueprintEditing ? draftRole : record.Role;
            if (role == UnitRole.Empty)
            {
                ink = LedgerV2.Amber;
                return "Set a role down before naming a keeper — an empty flat has " +
                       "nothing to keep.";
            }
            if (record.RaidUntilDay > RosterDay)
            {
                ink = LedgerV2.Red;
                return "Sealed by the precinct until day " + record.RaidUntilDay +
                       ". A keeper may stand in it, but the door stays shut.";
            }

            var spec = UnitRoles.Of(role);
            if (spec.NeedsBank && record.Bank <= 0)
            {
                ink = LedgerV2.Amber;
                return "The card room has no bank. It stays closed until money is put " +
                       "behind the table.";
            }

            var keeper = blueprintEditing ? draftKeeper : record.KeeperId;
            if (keeper < 0)
            {
                ink = LedgerV2.Red;
                return "No keeper named. The flat reads dark and takes nothing in.";
            }
            if (!KeeperStanding(keeper))
            {
                ink = LedgerV2.Red;
                return "The man who keeps it is not standing in it. The flat reads dark " +
                       "until he is back.";
            }

            ink = LedgerV2.Muted;
            return spec.Label + " · " + Cash(spec.FitOut) + " fit-out · " + spec.Heat +
                   " heat a day while the door is open.";
        }

        // ------------------------------------------------------------------ the writes

        void BuyFlat(ApartmentUnitId unit)
        {
            var price = AskPrice(unit);
            var result = outfit != null
                ? outfit.Purchase(price, "the flat at " + unit.Door)
                : OpResult.Fail("the outfit's books are not open");
            if (!result.Ok)
            {
                blueprintNote = result.Reason;
                dirty = true;
                return;
            }

            Apartments.Buy(unit, GangCatalog.PlayerGangId, RosterDay);
            blueprintNote = "";
            draftRole = UnitRole.Empty;
            draftKeeper = -1;
            draftName = "";
            draftDirty = false;

            // The form does not close: the flat is ours and has no role, so the same paper
            // lands straight in the pickers.
            blueprintEditing = true;
            dirty = true;
        }

        void SaveFlat(ApartmentUnitId unit)
        {
            if (!Apartments.TryGet(unit, out var record))
                return;

            var roster = director != null ? director.Roster : null;
            var spec = UnitRoles.Of(draftRole);

            // The fit-out is charged when the room is turned to a use it has not been paid
            // for. A refit costs again; picking the same role twice does not.
            if (draftRole != UnitRole.Empty && record.PaidRole != draftRole)
            {
                var result = outfit != null
                    ? outfit.Purchase(spec.FitOut,
                        "fitting out " + unit.Door + " as " + spec.Label)
                    : OpResult.Fail("the outfit's books are not open");
                if (!result.Ok)
                {
                    blueprintNote = result.Reason;
                    dirty = true;
                    return;
                }
                Apartments.SetRole(unit, draftRole, true);
            }
            else
            {
                Apartments.SetRole(unit, draftRole, false);
            }

            if (draftKeeper != record.KeeperId)
            {
                if (record.KeeperId >= 0)
                    RosterOps.ClearKeeper(roster, record.KeeperId);

                if (draftKeeper >= 0)
                {
                    var kept = RosterOps.SetKeeper(roster, draftKeeper);
                    if (!kept.Ok)
                    {
                        blueprintNote = kept.Reason;
                        draftKeeper = record.KeeperId;
                        dirty = true;
                        return;
                    }
                }
                Apartments.SetKeeper(unit, draftKeeper);
            }

            Apartments.SetName(unit, draftName);
            blueprintNote = "";
            draftDirty = false;
            blueprintEditing = false;
            dirty = true;
        }

        void PullKeeperOut(ApartmentUnitId unit)
        {
            if (!Apartments.TryGet(unit, out var record) || record.KeeperId < 0)
                return;
            RosterOps.ClearKeeper(director != null ? director.Roster : null,
                record.KeeperId);
            Apartments.SetKeeper(unit, -1);
            draftKeeper = -1;
            blueprintNote = "";
            dirty = true;
        }

        // ------------------------------------------------------------------ the fixture

        /// <summary>One section of the form: the prototype's 13/20/14 padding, closed by a
        /// hairline when another section follows it.</summary>
        RectTransform Section(RectTransform body, float x, float y, float w, float h,
            bool ruled)
        {
            var box = NewRect("Section", body);
            PlaceTopLeft(box, x, y, w, h);
            if (ruled)
                Rule(box, 0f, -(h - 1f), w, LedgerV2.Hair);
            return box;
        }

        void SectionLabel(RectTransform box, string label) =>
            LedgerV2.Mono(box, FormPadX, -13f, 400f, label, 10f, LedgerV2.Muted, 16f);

        static string Ordinal(int floor) => floor switch
        {
            1 => "FIRST", 2 => "SECOND", 3 => "THIRD", 4 => "FOURTH", 5 => "FIFTH",
            6 => "SIXTH", 7 => "SEVENTH", 8 => "EIGHTH", 9 => "NINTH", 10 => "TENTH",
            _ => floor + "TH",
        };
    }
}
