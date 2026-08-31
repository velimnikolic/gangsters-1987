using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using LivingCity.Personnel;
using static LivingCity.UI.LedgerKit;

namespace LivingCity.UI
{
    /// <summary>
    /// The PERSONNEL page: the payroll printout down the left, one man's personal file
    /// down the right, both on the same sheet of the folder.
    ///
    /// The printout is what a line printer put out this morning: a numbered column of
    /// men under their crew's band, with what each carries, how he is, where he stands
    /// and what he costs - and the day's payroll totalled at the foot. The personal
    /// file is the dossier: a clipped mug shot, the particulars, the kit he has signed
    /// for, the trades he is rated in, and the two verbs a boss actually has.
    ///
    /// Nothing here posts a man anywhere. Where a man IS is reported, never set - the
    /// orders that move him are laid against the city on the map.
    /// </summary>
    public sealed partial class PersonnelAlmanac
    {
        enum Confirm
        {
            None,
            Promote,
            Demote,
        }

        // ---- the two panes ----

        const float PaneGap = 26f;
        const float PaneW = (PageWidth - PaneGap) * 0.5f;
        const float PaneH = -(PageBottom - PageTop);

        const float ListLeft = PageLeft;
        const float CardLeft = PageLeft + PaneW + PaneGap;

        // ---- inside the printout ----

        const float PrintPad = 18f;
        const float PrintInner = PaneW - PrintPad * 2f;

        /// <summary>The printout's own body, inside its padding - every y below is
        /// measured in it, so the payroll at the foot lands ON the sheet and not past
        /// its bottom edge.</summary>
        const float PrintBodyH = PaneH - PrintPad * 2f;

        /// <summary>Where the numbered rows begin, under the column heads.</summary>
        const float RollTop = -96f;

        /// <summary>What the payroll total at the foot takes off the bottom.</summary>
        const float RollFoot = 74f;

        /// <summary>Two lines fit in a row: the CONDITION column prints a state word
        /// with its note under it, and 28 units put the note's line box through the
        /// word's. Fewer rows on the glass at once is the price, and the roll scrolls.</summary>
        const float RowHeight = 32f;
        const float ListHeight = PrintBodyH + RollTop - RollFoot;
        const float HoodIndent = 18f;

        // The printout's column grid, in printout-inner coordinates. CONDITION carries
        // a state word with a free-text note UNDER it ("HURT" over "2 ribs · back in 4
        // days"), so it is the widest of the scan columns and the note gets the whole
        // of it rather than what is left beside the word.
        const float ColName = 0f;
        const float ColCarrying = 236f;
        const float ColCondition = 318f;
        const float ColStanding = 460f;
        const float ColValue = 540f;
        const float ColWage = PrintInner - 112f;

        // ---- inside the personal file ----

        const float CardPad = 20f;
        const float CardInner = PaneW - CardPad * 2f;

        /// <summary>The fixed head band and the fixed foot; the dossier scrolls between
        /// them, because eleven rated trades plus a kit list will not fit any card the
        /// sheet has room for.</summary>
        const float CardHead = 36f;
        const float CardFoot = 62f;
        const float CardBodyH = PaneH - CardHead - CardFoot;

        /// <summary>selectedId's sentinel for "the front is selected" - the boss's
        /// card rather than a member's. Never a real Character id (those are >= 0).</summary>
        const int FrontSelection = -2;

        RectTransform listViewport;
        RectTransform listContent;
        RectTransform cardViewport;
        RectTransform cardContent;
        RectTransform cardFoot;
        TMP_Text cardFileNo;
        RectTransform hoverNote;
        TMP_Text hoverNoteText;
        GameObject sortMenu;
        TMP_Text sortTape, rankTape, postTape, showTape;
        Image sortPill, rankPill, postPill, showPill;
        TMP_Text payrollFigure;

        ViewOptions options;
        int selectedId = -1;
        Confirm pendingConfirm;
        string lastRefusal = "";
        float listScroll;
        float cardScroll;

        readonly List<LedgerRow> rows = new List<LedgerRow>();

        void BuildPersonnelPage(RectTransform sheet)
        {
            var root = NewPageRoot(sheet, LedgerPage.Personnel);

            BuildPrintout(root);
            BuildPersonalFile(root);
            BuildSortMenu(root);
        }

        /// <summary>The left pane: line-printer stock, the filter pills, the column
        /// heads, the scrolling roll, and the payroll struck across the foot.</summary>
        void BuildPrintout(RectTransform root)
        {
            // A fraction off square, like everything else loose in the folder: paper
            // that somebody laid down by hand is never true, and a stack of perfectly
            // square rectangles is the one thing that reads as UI whatever is drawn on
            // it. The design's own figure for this sheet.
            var sheet = Card("Printout", root, ListLeft, PageTop, PaneW, PaneH,
                LedgerStyle.Printout, tiltDegrees: -0.28f, shadowSpread: 14f,
                low: LedgerStyle.PrintoutLow);
            Aging(sheet, PaneW, PaneH);
            var body = NewRect("Body", sheet);
            Stretch(body, PrintPad);

            Caps(body, 0f, -10f, PrintInner, "PAYROLL PRINTOUT · LINE PRINTER 03", 13f,
                LedgerStyle.InkMid, 4f);

            // The four filters. A pill is dark when it is actually filtering something,
            // so the printout says at a glance that it is not showing the whole roll.
            sortTape = Pill(body, "", 0f, -38f, 232f, 24f, false, ToggleSortMenu);
            rankTape = Pill(body, "", 238f, -38f, 150f, 24f, false, CycleRank);
            postTape = Pill(body, "", 394f, -38f, 150f, 24f, false, CyclePost);
            showTape = Pill(body, "", 550f, -38f, PrintInner - 550f, 24f, false, CycleShow);
            sortPill = PillFace(sortTape);
            rankPill = PillFace(rankTape);
            postPill = PillFace(postTape);
            showPill = PillFace(showTape);

            // The column heads, and the rule the printer struck under them.
            Caps(body, ColName, -70f, 200f, "NAME", 9.5f);
            Caps(body, ColCarrying, -70f, 84f, "CARRYING", 9.5f);
            Caps(body, ColCondition, -70f, 110f, "CONDITION", 9.5f);
            Caps(body, ColStanding, -70f, 80f, "STANDING", 9.5f);
            Caps(body, ColWage, -70f, 112f, "WAGE", 9.5f, null, 4f,
                TextAlignmentOptions.MidlineRight);
            Rule(body, ColName, -88f, PrintInner, LedgerStyle.InkFaint);

            listViewport = NewRect("Roll", body);
            PlaceTopLeft(listViewport, ColName, RollTop, PrintInner, ListHeight);
            listViewport.gameObject.AddComponent<RectMask2D>();

            listContent = NewRect("Rows", listViewport);
            listContent.anchorMin = new Vector2(0f, 1f);
            listContent.anchorMax = new Vector2(1f, 1f);
            listContent.pivot = new Vector2(0f, 1f);
            listContent.anchoredPosition = Vector2.zero;
            listContent.sizeDelta = new Vector2(0f, ListHeight);

            // The foot: what the whole sheet costs a day, and why it costs it.
            var footY = RollTop - ListHeight - 10f;
            DoubleRule(body, ColName, footY, PrintInner, LedgerStyle.Ink);
            Caps(body, ColName, footY - 12f, 300f, "PAYROLL · RUNNING", 13f,
                LedgerStyle.InkMid, 4f);
            payrollFigure = Line(body, LedgerStyle.Condensed, 19f, LedgerStyle.Ink,
                PrintInner - 300f, footY - 14f, 300f, 24f, "",
                TextAlignmentOptions.MidlineRight);
            var note = Paragraph(body, LedgerStyle.MonoItalic, 11f, LedgerStyle.InkDim,
                ColName, footY - 36f, PrintInner, 28f,
                "pay falls due as it falls due · the jailed and the hurt keep drawing · " +
                "only the dead come off", lineSpacing: 1f);
            note.overflowMode = TextOverflowModes.Ellipsis;
        }

        static Image PillFace(TMP_Text pillLabel) =>
            pillLabel.transform.parent.GetComponent<Image>();

        /// <summary>The right pane: the personal file. A fixed head, a scrolling body,
        /// and the verbs pinned to the foot where a hand would rest.</summary>
        void BuildPersonalFile(RectTransform root)
        {
            var card = Card("File", root, CardLeft, PageTop, PaneW, PaneH,
                LedgerStyle.Card, tiltDegrees: 0f, shadowSpread: 14f,
                low: LedgerStyle.CardLow);
            // A horizontal fold reads as clipped text in this dense, scrollable file.
            // Keep the paper lighting and foxing here; reserve the crease for loose
            // sheets such as the newspaper and roster.
            Aging(card, PaneW, PaneH, includeCrease: false);

            // The file is BOTH clipped and stapled - it was assembled twice, which is
            // what a personal file in a working office looks like.
            Clip(card, PaneW * 0.5f, 0f);
            Staple(card, 16f, -16f);
            PencilSmudge(card, 22f, -(PaneH - 70f), 130f, 46f);

            Caps(card, CardPad, -10f, 300f, "PERSONAL FILE", 12f, LedgerStyle.InkMid, 5f);
            cardFileNo = Caps(card, PaneW - CardPad - 200f, -10f, 200f, "", 11f,
                LedgerStyle.InkLabel, 4f, TextAlignmentOptions.MidlineRight);
            Rule(card, CardPad, -CardHead + 4f, CardInner, LedgerStyle.InkFaint);

            cardViewport = NewRect("Body", card);
            PlaceTopLeft(cardViewport, CardPad, -CardHead, CardInner, CardBodyH);
            cardViewport.gameObject.AddComponent<RectMask2D>();

            cardContent = NewRect("Content", cardViewport);
            cardContent.anchorMin = new Vector2(0f, 1f);
            cardContent.anchorMax = new Vector2(1f, 1f);
            cardContent.pivot = new Vector2(0f, 1f);
            cardContent.anchoredPosition = Vector2.zero;
            cardContent.sizeDelta = new Vector2(0f, CardBodyH);

            cardFoot = NewRect("Foot", card);
            PlaceTopLeft(cardFoot, CardPad, -(CardHead + CardBodyH), CardInner, CardFoot);

            // The one shared hover note - a sticky note, child of the CARD, not the
            // content (which rebuilds under the pointer), raised to last sibling on
            // every show so it prints over whatever it covers.
            hoverNote = StickyNote(card, CardInner - 60f, 60f);
            hoverNoteText = Text("Text", hoverNote, LedgerStyle.Mono, 12.5f, LedgerStyle.Ink,
                TextAlignmentOptions.TopLeft);
            Stretch(hoverNoteText.rectTransform, 10f);
            hoverNoteText.textWrappingMode = TextWrappingModes.Normal;
            hoverNote.gameObject.SetActive(false);
        }

        // ---------------------------------------------------------- filter pills

        void ToggleSortMenu()
        {
            if (sortMenu)
                sortMenu.SetActive(!sortMenu.activeSelf);
        }

        void CycleRank()
        {
            options.Rank = (RankFilter)(((int)options.Rank + 1) % 4);
            dirty = true;
        }

        void CyclePost()
        {
            options.Assignment = (AssignmentFilter)(((int)options.Assignment + 1) % 4);
            dirty = true;
        }

        void CycleShow()
        {
            options.Availability = (AvailabilityFilter)(((int)options.Availability + 1) % 3);
            dirty = true;
        }

        /// <summary>A pill is INK when it is holding something back and a wash of ink
        /// when it is not - so a short roll never looks like the whole outfit.</summary>
        static void SetPill(Image face, TMP_Text label, bool filtering)
        {
            if (!face)
                return;
            face.color = filtering ? LedgerStyle.TapeBlack : LedgerStyle.TapeIdle;
            label.color = filtering ? LedgerStyle.TapeText : LedgerStyle.InkMid;
        }

        void RefreshFilterTapes()
        {
            if (!sortTape)
                return;

            sortTape.text = options.Sort switch
            {
                SortKey.Attribute => "SORT: " +
                    LedgerText.AttributeLabel(options.SortAttribute).ToUpperInvariant(),
                SortKey.Loyalty => "SORT: LOYALTY",
                _ => "SORT: ROSTER ORDER",
            };
            rankTape.text = options.Rank switch
            {
                RankFilter.Hoods => "RANK: HOODS",
                RankFilter.Lieutenants => "RANK: LIEUTENANTS",
                RankFilter.Specialists => "RANK: SPECIALISTS",
                _ => "RANK: ALL",
            };
            postTape.text = options.Assignment switch
            {
                AssignmentFilter.Crews => "POST: CREWS",
                AssignmentFilter.Pool => "POST: POOL",
                AssignmentFilter.Front => "POST: FRONT",
                _ => "POST: ALL",
            };
            showTape.text = options.Availability switch
            {
                AvailabilityFilter.ActiveOnly => "SHOW: ACTIVE",
                AvailabilityFilter.Unavailable => "SHOW: OUT OF ACTION",
                _ => "SHOW: ALL",
            };

            SetPill(sortPill, sortTape, options.Sort != SortKey.Roster);
            SetPill(rankPill, rankTape, options.Rank != RankFilter.All);
            SetPill(postPill, postTape, options.Assignment != AssignmentFilter.All);
            SetPill(showPill, showTape, options.Availability != AvailabilityFilter.All);

            if (payrollFigure)
            {
                payrollFigure.text =
                    LedgerText.Cash(Outfit.Wages.DailyPayroll(director.Roster)) + " / day";
            }
        }

        /// <summary>A slip of card that drops from the SORT pill: thirteen typed
        /// entries, the current one highlighted. Built once; it toggles rather than
        /// rebuilds because its contents never change. Built LAST under the page so
        /// hierarchy order draws it over the roll.</summary>
        void BuildSortMenu(RectTransform root)
        {
            const float rowH = 24f;
            var entries = 2 + AttributeScale.Count;

            var slip = Card("SortMenu", root, ListLeft + PrintPad, PageTop - 84f, 260f,
                entries * rowH + 12f, LedgerStyle.Printout, low: LedgerStyle.PrintoutLow);
            sortMenu = slip.gameObject;
            // The slip's own body must swallow stray clicks.
            PaperOf(slip).raycastTarget = true;

            for (var i = 0; i < entries; i++)
            {
                var index = i;
                string label;
                if (i == 0)
                    label = "ROSTER ORDER";
                else if (i <= AttributeScale.Count)
                    label = LedgerText.AttributeLabel((CharacterAttribute)(i - 1))
                        .ToUpperInvariant();
                else
                    label = "LOYALTY";

                var row = NewRect("Entry", slip);
                PlaceTopLeft(row, 6f, -6f - i * rowH, 248f, rowH);
                var surface = ClickSurface(row);
                RowButton(row, surface, () =>
                {
                    if (index == 0)
                        options.Sort = SortKey.Roster;
                    else if (index <= AttributeScale.Count)
                    {
                        options.Sort = SortKey.Attribute;
                        options.SortAttribute = (CharacterAttribute)(index - 1);
                    }
                    else
                        options.Sort = SortKey.Loyalty;

                    sortMenu.SetActive(false);
                    dirty = true;
                });

                Caps(row, 12f, -5f, 230f, label, 11f, LedgerStyle.InkSoft, 3f);
            }

            sortMenu.SetActive(false);
        }

        // ------------------------------------------------------------------ the roll

        void RebuildList()
        {
            foreach (Transform old in listContent)
                Destroy(old.gameObject);

            var roster = director.Roster;
            if (roster == null)
                return;

            var effective = options;

            RosterView.Build(roster, effective, rows);

            var y = 0f;
            var indented = false;
            var first = true;
            var index = 0;
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                switch (row.Kind)
                {
                    case RowKind.CrewHeader:
                        indented = true;
                        if (!first)
                            y -= 8f;
                        BuildCrewBand(roster, row.CrewId, y);
                        y -= RowHeight;
                        break;

                    case RowKind.FrontHeader:
                    case RowKind.PoolHeader:
                    case RowKind.SpecialistHeader:
                        indented = true;
                        if (!first)
                            y -= 8f;
                        BuildSectionHeader(roster, row.Kind, y);
                        y -= RowHeight;
                        break;

                    case RowKind.Lieutenant:
                        BuildCharacterRow(roster, row.CharacterId, y, ++index,
                            indent: false, lieutenantRow: true);
                        y -= RowHeight;
                        break;

                    default:
                        BuildCharacterRow(roster, row.CharacterId, y, ++index, indented);
                        y -= RowHeight;
                        break;
                }
                first = false;
            }

            listContent.sizeDelta = new Vector2(0f, Mathf.Max(ListHeight, -y));
            var maxScroll = Mathf.Max(0f, listContent.sizeDelta.y - ListHeight);
            listScroll = Mathf.Clamp(listScroll, 0f, maxScroll);
            listScroll = Mathf.Floor(listScroll / RowHeight) * RowHeight;
            listContent.anchoredPosition = new Vector2(0f, listScroll);
        }

        /// <summary>
        /// A band across the roll naming a crew, with a dotted leader out to a note on
        /// the right - the way a printout separates a run of lines that belong together.
        /// The band is a LABEL: clicking it does nothing, because the lieutenant's own
        /// row underneath is still the crew's handle (it selects him, and in assign mode
        /// it takes the man into his crew).
        /// </summary>
        void BuildCrewBand(Roster roster, int crewId, float y)
        {
            var crew = roster.FindCrew(crewId);
            var lieutenant = crew != null ? roster.Find(crew.LieutenantId) : null;
            var name = lieutenant != null
                ? LedgerText.CrewName(lieutenant.Surname).ToUpperInvariant()
                : "A CREW";

            var rect = NewRect("Crew Band", listContent);
            PlaceTopLeft(rect, 0f, y, PrintInner, RowHeight);

            var label = Caps(rect, 0f, -8f, 320f, name, 13f, LedgerStyle.Ink, 4f);
            var men = crew != null ? crew.HoodIds.Count : 0;
            var note = Caps(rect, PrintInner - 300f, -9f, 300f,
                men == 1 ? "one man under him" : men + " men under him", 10f,
                LedgerStyle.InkLabel, 2f, TextAlignmentOptions.MidlineRight);

            var from = label.preferredWidth + 12f;
            var to = PrintInner - note.preferredWidth - 12f;
            if (to > from)
                DottedRule(rect, from, -16f, to - from, LedgerStyle.InkDotted);
        }

        void BuildSectionHeader(Roster roster, RowKind kind, float y)
        {
            var rect = NewRect("Section", listContent);
            PlaceTopLeft(rect, 0f, y, PrintInner, RowHeight);

            // The front's header is also the BOSS's row: clicking it opens the front
            // card - his face, the desk, the locker - the way a member row opens his.
            // It is the only header that does anything: a section head is a label, and
            // the book has nothing to drop a man onto any more.
            var frontSelectable = kind == RowKind.FrontHeader;
            var chosen = frontSelectable && selectedId == FrontSelection;

            if (frontSelectable)
                RowButton(rect, ClickSurface(rect), () => SelectMember(FrontSelection));
            else if (chosen)
                Highlight(rect, LedgerStyle.Highlighter);

            var title = kind switch
            {
                RowKind.FrontHeader => "THE FRONT",
                RowKind.PoolHeader => "THE POOL",
                _ => "SPECIALISTS",
            };
            var aside = kind switch
            {
                RowKind.FrontHeader => FrontAside(roster),
                RowKind.PoolHeader => "unassigned · earning nothing",
                _ => "bought talent · not fighters",
            };

            var label = Caps(rect, 8f, -8f, 320f, title, 13f, LedgerStyle.Ink, 4f);
            var note = Caps(rect, PrintInner - 320f, -9f, 320f, aside, 10f,
                LedgerStyle.InkLabel, 2f, TextAlignmentOptions.MidlineRight);

            var from = 8f + label.preferredWidth + 12f;
            var to = PrintInner - note.preferredWidth - 12f;
            if (to > from)
                DottedRule(rect, from, -16f, to - from, LedgerStyle.InkDotted);
        }

        static string FrontAside(Roster roster)
        {
            var manager = roster.Find(roster.FrontId);
            return manager != null ? manager.Surname + " runs the desk" : "nobody at the desk";
        }

        void BuildCharacterRow(Roster roster, int id, float y, int index, bool indent,
            bool lieutenantRow = false)
        {
            var member = roster.Find(id);
            if (member == null)
                return;

            var rect = NewRect("Row", listContent);
            PlaceTopLeft(rect, 0f, y, PrintInner, RowHeight);

            var chosen = id == selectedId;

            // A row does ONE thing: it opens that man's file. The ledger reads.
            RowButton(rect, ClickSurface(rect), () => SelectMember(id));

            if (chosen)
                Highlight(rect, LedgerStyle.Highlighter);
            else
                Rule(rect, 0f, -RowHeight, PrintInner, LedgerStyle.InkHair);

            var dead = member.Gone; // struck through: dead or deserted
            var ink = dead ? LedgerStyle.InkDim : LedgerStyle.Ink;

            var numberX = 8f + (indent ? HoodIndent : 0f);
            var number = Line(rect, LedgerStyle.Mono, 10.5f, LedgerStyle.InkLabel,
                numberX, 0f, 24f, RowHeight, index.ToString("00"));
            FillRow(number.rectTransform, numberX, 24f);

            var nameX = numberX + 28f;
            var name = Text("Name", rect, lieutenantRow ? LedgerStyle.MonoBold : LedgerStyle.Mono,
                14.5f, ink, TextAlignmentOptions.MidlineLeft);
            name.overflowMode = TextOverflowModes.Ellipsis;
            FillRow(name.rectTransform, nameX, 150f);
            name.text = member.FullName;

            // The rank tag rides after the name at a FIXED width, and the name's own
            // cell is what gives: a tag that can be pushed off the row is a tag nobody
            // can read on the one line where rank decides everything.
            var tagX = nameX + name.preferredWidth + 10f;
            var tagRoom = ColCarrying - 8f - tagX;
            if (tagRoom > 40f)
            {
                var rank = Caps(rect, 0f, 0f, tagRoom,
                    member.Specialty != Specialty.None
                        ? LedgerText.SpecialtyLabel(member.Specialty)
                        : LedgerText.RankLabel(member.Rank),
                    9.5f, dead ? LedgerStyle.InkFaint : LedgerStyle.InkLabel, 3f);
                FillRow(rank.rectTransform, tagX, tagRoom);
            }

            BuildGunCell(roster, rect, member, dead);
            BuildRowCells(roster, rect, member, dead);

            // The dead are struck through in pen - the record keeps their line.
            if (dead)
                Rule(rect, nameX - 2f, -RowHeight * 0.5f + 1f, ColCarrying - 12f - nameX,
                    LedgerStyle.RedPen, 1.5f);
        }

        /// <summary>How wide the gun's cut is in the CARRYING column.</summary>
        const float GunWidth = 70f, GunHeight = 20f;

        /// <summary>The body of the gun a man carries: the first firearm on his line in
        /// the armory, else the .38 every man of the outfit carries in his coat - the
        /// default that is nobody's stock and never on the counter.</summary>
        static GameObject SidearmModel(Roster roster, Character member)
        {
            var carried = new List<RosterEquipment>();
            roster.HeldBy(member.Id, carried);
            foreach (var item in carried)
            {
                // Wheels of any sort are not the gun in his coat - the motorcycle
                // joined the stock book after this line was written, and a man was
                // very nearly drawn holding one (RosterOps.IsWeapon is the one test).
                if (!RosterOps.IsWeapon(item.Kind))
                    continue;
                string modelName = null;
                foreach (var listing in Outfit.ArmoryCatalog.Weapons)
                    if (listing.DisplayName == item.DisplayName) { modelName = listing.ModelName; break; }
                var model = LedgerModelSet.WeaponModelFor(item.Kind, modelName);
                if (model)
                    return model;
            }
            return LedgerModelSet.WeaponModelFor(EquipmentKind.Pistol, DefaultSidearmModel);
        }

        /// <summary>The revolver every man carries by default - Gang Warfare's .38.</summary>
        const string DefaultSidearmModel = "SM_Wep_Pistol_Revolver_01";

        /// <summary>The CARRYING column: a small newsprint of the man's gun, cut to the
        /// barrel band, so the roll reads who carries what at a glance without a word
        /// of type. Specialists carry nothing and show nothing.</summary>
        void BuildGunCell(Roster roster, RectTransform rect, Character member, bool dead)
        {
            if (member.Specialty != Specialty.None)
                return;
            var model = SidearmModel(roster, member);
            if (!model)
                return;

            var cut = NewRect("Gun", rect);
            cut.sizeDelta = new Vector2(GunWidth, GunHeight);
            cut.anchorMin = new Vector2(0f, 0.5f);
            cut.anchorMax = new Vector2(0f, 0.5f);
            cut.pivot = new Vector2(0f, 0.5f);
            cut.anchoredPosition = new Vector2(ColCarrying, 0f);
            var raw = cut.gameObject.AddComponent<RawImage>();
            raw.raycastTarget = false;
            raw.enabled = false;
            raw.uvRect = new Rect(0f, 0.25f, 1f, 0.5f);
            raw.color = dead ? new Color(1f, 1f, 1f, 0.45f) : Color.white;
            PortraitStudio.Request(model, PortraitStudio.Framing.Item, raw,
                PortraitStudio.Treatment.Newsprint);
        }

        /// <summary>How a man is, in the word a clerk would type.</summary>
        static string ConditionWord(CharacterStatus status) => status switch
        {
            CharacterStatus.Active => "FIT",
            CharacterStatus.Hospitalized => "HURT",
            CharacterStatus.Jailed => "HELD",
            CharacterStatus.Dead => "DEAD",
            _ => "GONE",
        };

        /// <summary>
        /// The line under the state word: what happened to him and how long he is out
        /// for, in DAYS. The man's own note carries the particulars (it was written
        /// when he went down); the campaign supplies the countdown, so a note never
        /// goes stale as the days pass under it.
        /// </summary>
        string ConditionNote(Character member, CharacterStatus status)
        {
            if (status == CharacterStatus.Active)
                return "";
            if (status == CharacterStatus.Dead)
                return "off the books";
            if (status == CharacterStatus.Deserted)
                return "ran";

            var today = outfit ? outfit.Campaign.Day : 1;
            var left = LedgerText.DaysLeft(member.BackOnDay, today);
            return member.ConditionNote.Length > 0
                ? member.ConditionNote + " · " + left
                : left;
        }

        /// <summary>
        /// The scan columns: CONDITION, STANDING, the sorted value when a sort is on,
        /// and the WAGE. Those four read straight down a column of sixty men, which is
        /// what makes sixty men scannable - the wage most of all, because payroll is
        /// the pressure the whole game turns on.
        /// </summary>
        void BuildRowCells(Roster roster, RectTransform rect, Character member, bool dead)
        {
            // ---- condition: a state word, and under it what is actually wrong ----
            var status = member.Status;
            var conditionW = ColStanding - ColCondition - 8f;
            var condition = Caps(rect, 0f, 0f, conditionW, ConditionWord(status), 11f,
                status == CharacterStatus.Active ? LedgerStyle.GreenOk
                : dead ? LedgerStyle.InkDim : LedgerStyle.RedPen, 2f);
            FillCell(condition.rectTransform, ColCondition, conditionW, 6f, 14f);

            // A man on his feet gets no second line: "on his feet" under FIT is a line
            // of type that says nothing, sixty times down the page. The note is for the
            // men something HAPPENED to, which is the only reason to scan the column.
            var note = ConditionNote(member, status);
            if (note.Length > 0)
            {
                var noteText = Text("Note", rect, LedgerStyle.Mono, 9.5f,
                    dead ? LedgerStyle.InkFaint : LedgerStyle.RedPen,
                    TextAlignmentOptions.MidlineLeft);
                noteText.overflowMode = TextOverflowModes.Ellipsis;
                FillCell(noteText.rectTransform, ColCondition, conditionW, -7f, 13f);
                noteText.text = note;
            }

            // ---- standing ----
            var posted = roster.AssignmentOf(member.Id).Kind != AssignmentKind.Pool;
            var standing = member.Wanted ? "WANTED" : dead ? "-" : posted ? "ACTIVE" : "IDLE";
            var standingText = Caps(rect, 0f, 0f, 76f, standing, 11f,
                member.Wanted ? LedgerStyle.RedPen
                : dead ? LedgerStyle.InkFaint
                : posted ? LedgerStyle.GreenOk : LedgerStyle.InkPale, 2f);
            FillRow(standingText.rectTransform, ColStanding, 76f);

            // ---- the sorted value, when the roll is sorted by one ----
            if (options.Sort != SortKey.Roster)
            {
                var value = Text("Value", rect, LedgerStyle.MonoBold, 13f, LedgerStyle.Ink,
                    TextAlignmentOptions.MidlineRight);
                FillRow(value.rectTransform, ColValue, ColWage - ColValue - 12f);
                value.text = options.Sort == SortKey.Loyalty
                    ? member.Loyalty.ToString()
                    : LedgerText.Stars(member.GetHalfSteps(options.SortAttribute));
            }

            // ---- what he costs ----
            var wage = Text("Wage", rect, LedgerStyle.MonoBold, 14f,
                dead ? LedgerStyle.InkFaint : LedgerStyle.Ink,
                TextAlignmentOptions.MidlineRight);
            FillRow(wage.rectTransform, ColWage, 112f);
            wage.text = LedgerText.Cash(Outfit.Wages.WageFor(member));
        }

        void SelectMember(int id)
        {
            selectedId = id;
            pendingConfirm = Confirm.None;
            lastRefusal = "";
            cardScroll = 0f;
            dirty = true;
        }

        // ------------------------------------------------------------ the personal file

        /// <summary>The face this member wears in his photograph and on the street - the
        /// same man in both, and never a body one of his own crewmates is wearing. The
        /// rule and the approved stock live together in Gangs.GangLooks; the book only
        /// asks it who this man is, and the roster it asks against is the director's.
        /// </summary>
        public static GameObject MemberModel(Character member) =>
            PortraitStudio.FindPeoplePrefab(Gangs.GangLooks.LookFor(member,
                Gameplay.PersonnelDirector.Instance != null
                    ? Gameplay.PersonnelDirector.Instance.Roster : null));

        /// <summary>First letters of the first and last word of a name - "Don Salvatore
        /// Ricci" prints DR in the slot until his photograph arrives.</summary>
        static string InitialsOf(string fullName)
        {
            var parts = fullName.Split(' ');
            var head = parts.Length > 0 && parts[0].Length > 0
                ? parts[0][0].ToString() : "";
            var tail = parts.Length > 1 && parts[parts.Length - 1].Length > 0
                ? parts[parts.Length - 1][0].ToString() : "";
            return head + tail;
        }

        /// <summary>Prints the sticky note just under the hovered row, sized to its
        /// copy. Row coordinates are the card content's, which the note hangs beside on
        /// the card itself - so the head band's offset has to be added back.</summary>
        void ShowHoverNote(string note, RectTransform row)
        {
            if (note.Length == 0 || hoverNote == null)
                return;

            hoverNoteText.text = note;
            hoverNote.gameObject.SetActive(true);
            hoverNote.SetAsLastSibling();

            var width = CardInner - 60f;
            var height = hoverNoteText.GetPreferredValues(note, width - 20f, 0f).y + 20f;
            hoverNote.sizeDelta = new Vector2(width, height);
            hoverNote.anchoredPosition = new Vector2(CardPad + 30f,
                row.anchoredPosition.y + cardContent.anchoredPosition.y
                - row.sizeDelta.y - CardHead - 2f);
        }

        void HideHoverNote()
        {
            if (hoverNote != null)
                hoverNote.gameObject.SetActive(false);
        }

        /// <summary>The pointer half of the card's hover notes: an invisible zone laid
        /// over one stat row. AddComponent-only, never serialized.</summary>
        sealed class StatHoverZone : MonoBehaviour, IPointerEnterHandler,
            IPointerExitHandler
        {
            public PersonnelAlmanac almanac;
            public string note;

            public void OnPointerEnter(PointerEventData eventData) =>
                almanac.ShowHoverNote(note, (RectTransform)transform);

            public void OnPointerExit(PointerEventData eventData) =>
                almanac.HideHoverNote();
        }

        void RebuildDetail()
        {
            // The rows under the pointer are about to be destroyed, and destroyed
            // rows send no PointerExit - drop the note with them.
            HideHoverNote();

            foreach (Transform old in cardContent)
                Destroy(old.gameObject);
            foreach (Transform old in cardFoot)
                Destroy(old.gameObject);

            var roster = director.Roster;
            if (roster != null && selectedId == FrontSelection)
            {
                if (cardFileNo)
                    cardFileNo.text = "F-0001 · THE FRONT";
                CloseCard(BuildFrontDetail(roster));
                return;
            }

            var member = roster != null && selectedId >= 0 ? roster.Find(selectedId) : null;
            if (member == null)
            {
                if (cardFileNo)
                    cardFileNo.text = "";
                var hint = Caps(cardContent, 0f, -(CardBodyH * 0.5f), CardInner,
                    "pick a man off the printout", 12f, LedgerStyle.InkLabel, 4f,
                    TextAlignmentOptions.Center);
                hint.text = "— PICK A MAN OFF THE PRINTOUT —";
                CloseCard(-CardBodyH);
                return;
            }

            if (cardFileNo)
                cardFileNo.text = "P-" + (1100 + member.Id).ToString("0000");

            var y = BuildDossier(roster, member);
            CloseCard(y);
            BuildActionStrip(roster, member);
        }

        /// <summary>Sizes and re-clamps the dossier's scroll to whatever was just laid
        /// on it. Called with the y the last thing on the card ended at.</summary>
        void CloseCard(float y)
        {
            cardContent.sizeDelta = new Vector2(0f, Mathf.Max(CardBodyH, -y + 16f));
            var maxScroll = Mathf.Max(0f, cardContent.sizeDelta.y - CardBodyH);
            cardScroll = Mathf.Clamp(cardScroll, 0f, maxScroll);
            cardContent.anchoredPosition = new Vector2(0f, cardScroll);
        }

        /// <summary>The dossier proper. Returns the y it finished at.</summary>
        float BuildDossier(Roster roster, Character member)
        {
            // ---- the mug shot, and the particulars beside it ----
            const float plateW = 128f;
            const float plateH = 156f;
            var raw = Plate(cardContent, 0f, -8f, plateW, plateH, "MUG SHOT");
            PortraitStudio.Request(MemberModel(member), PortraitStudio.Framing.Bust, raw);
            Caps(cardContent, 0f, -(plateH + 12f), plateW,
                "P-" + (1100 + member.Id).ToString("0000") + " · 1987", 9f,
                LedgerStyle.InkLabel, 2f);

            var textX = plateW + 22f;
            var textW = CardInner - textX;

            var name = Line(cardContent, LedgerStyle.Condensed, 26f, LedgerStyle.Ink,
                textX, -6f, textW, 34f, member.FullName);
            name.characterSpacing = 1f;

            var assignment = roster.AssignmentOf(member.Id);
            var crewName = "";
            if (assignment.Kind == AssignmentKind.Crew)
            {
                var crew = roster.FindCrew(assignment.CrewId);
                var lieutenant = crew != null ? roster.Find(crew.LieutenantId) : null;
                crewName = lieutenant != null
                    ? LedgerText.CrewName(lieutenant.Surname)
                    : "A crew";
            }

            var rankLine = member.Specialty != Specialty.None
                ? LedgerText.SpecialtyLabel(member.Specialty)
                : LedgerText.RankLabel(member.Rank);
            Caps(cardContent, textX, -40f, textW,
                rankLine + " · " + (crewName.Length > 0 ? crewName : "no crew"), 12f,
                LedgerStyle.RedPen, 5f);

            var y = -70f;
            y = Particular("POST", LedgerText.AssignmentLine(assignment, crewName), textX,
                textW, y);
            if (member.Rank == Rank.Boss)
            {
                y = Particular("REPORTS TO", "Nobody · root command", textX, textW, y);
            }
            else if (member.Rank == Rank.Lieutenant ||
                     (member.Rank == Rank.Hood && member.Specialty == Specialty.None))
            {
                OrganizationPerson commandParent = default;
                var hasParent = director.Organization != null &&
                                director.Organization.TryGetCommandParent(
                                    member.Id, out commandParent);
                y = Particular("REPORTS TO",
                    hasParent ? commandParent.Name : "No valid command parent",
                    textX, textW, y,
                    hasParent ? LedgerStyle.Ink : LedgerStyle.RedPen);
            }
            y = Particular("WAGE", LedgerText.Cash(Outfit.Wages.WageFor(member)) + " / day",
                textX, textW, y);
            y = Particular("CONDITION", LedgerText.StatusLabel(member.Status), textX, textW, y,
                member.Status == CharacterStatus.Active ? LedgerStyle.Ink : LedgerStyle.RedPen);
            if (TryObservedBlock(member.Id, out var currentBlock))
            {
                y = Particular("CURRENT STATUS", "On street", textX, textW, y);
                y = Particular("CURRENT BLOCK", currentBlock, textX, textW, y);
            }
            y = Particular("LOYALTY", member.Loyalty + " of 100", textX, textW, y,
                member.Loyalty < 35 ? LedgerStyle.RedPen : LedgerStyle.Ink);

            // The stamps: the law's word over the photograph, WANTED beside the name.
            if (member.Status != CharacterStatus.Active)
                Stamp(cardContent, member.Status switch
                {
                    CharacterStatus.Dead => "DECEASED",
                    CharacterStatus.Deserted => "DESERTED",
                    CharacterStatus.Jailed => "IN CUSTODY",
                    _ => "IN HOSPITAL",
                }, 2f, -46f, 124f, 30f, tilt: -14f, size: 14f);
            if (member.Wanted)
                Stamp(cardContent, "WANTED", CardInner - 110f, -2f, 104f, 28f, tilt: 8f,
                    size: 14f);

            y = Mathf.Min(y, -(plateH + 30f)) - 10f;

            // ---- what he has signed for ----
            y = BuildKitSlots(roster, member, y);
            y -= 12f;

            // ---- where he is, and how far along he is ----
            y = BuildStandingBoxes(roster, member, assignment, crewName, y);
            y -= 14f;

            // ---- the trades ----
            const float traitTextInset = 3f;
            Caps(cardContent, traitTextInset, y, CardInner - traitTextInset,
                "TRAITS · AS TYPED", 11f,
                LedgerStyle.InkMid, 5f);
            y -= 22f;
            for (var a = 0; a < AttributeScale.Count; a++)
                y = BuildAttributeRow(member, (CharacterAttribute)a, y);
            y -= 8f;

            // ---- what the city has on him ----
            y = BuildRapSheet(member, y);

            // ---- the gear the outfit keeps, and his crew's share of it ----
            y = BuildEquipmentSection(roster, member, y);

            if (lastRefusal.Length > 0)
                y = MarginNote(lastRefusal, y - 10f);

            return y;
        }

        /// <summary>
        /// RAP SHEET: what the city has on him, oldest line first - date, charge, and
        /// how it ended, the outcome in red because it is the half that says whether he
        /// is a liability. Every man is dealt one with his name, so a clean sheet is a
        /// FACT about the man and gets said out loud rather than leaving a gap.
        /// </summary>
        float BuildRapSheet(Character member, float y)
        {
            const float dateW = 96f;
            const float outcomeW = 168f;
            const float rowH = 19f;

            Caps(cardContent, 0f, y, CardInner - 150f, "RAP SHEET", 11f,
                LedgerStyle.InkMid, 5f);
            Caps(cardContent, CardInner - 150f, y, 150f,
                member.RapSheet.Count == 1 ? "1 ENTRY" : member.RapSheet.Count + " ENTRIES",
                9f, LedgerStyle.InkLabel, 3f, TextAlignmentOptions.MidlineRight);
            y -= 20f;
            Rule(cardContent, 0f, y + 4f, CardInner, LedgerStyle.InkFaint);

            if (member.RapSheet.Count == 0)
            {
                Line(cardContent, LedgerStyle.MonoItalic, 12f, LedgerStyle.InkDim,
                    0f, y - 2f, CardInner, rowH, "No record. Nothing on him at all.");
                return y - rowH - 10f;
            }

            for (var i = 0; i < member.RapSheet.Count; i++)
            {
                var entry = member.RapSheet[i];

                Line(cardContent, LedgerStyle.Mono, 11f, LedgerStyle.InkLabel,
                    0f, y - 2f, dateW, rowH, entry.Date);

                var charge = Line(cardContent, LedgerStyle.Mono, 12f, LedgerStyle.InkSoft,
                    dateW, y - 2f, CardInner - dateW - outcomeW - 8f, rowH, entry.Charge);
                charge.overflowMode = TextOverflowModes.Ellipsis;

                var outcome = Line(cardContent, LedgerStyle.Mono, 11f, LedgerStyle.RedPen,
                    CardInner - outcomeW, y - 2f, outcomeW, rowH, entry.Outcome,
                    TextAlignmentOptions.MidlineRight);
                outcome.overflowMode = TextOverflowModes.Ellipsis;

                y -= rowH;
                if (i < member.RapSheet.Count - 1)
                    DottedRule(cardContent, 0f, y + 3f, CardInner, LedgerStyle.InkHair);
            }

            return y - 10f;
        }

        /// <summary>One LABEL / value line of the particulars grid.</summary>
        float Particular(string label, string value, float x, float w, float y, Color? ink = null)
        {
            Caps(cardContent, x, y, 96f, label, 9.5f, LedgerStyle.InkLabel, 3f);
            var text = Line(cardContent, LedgerStyle.Mono, 13.5f, ink ?? LedgerStyle.Ink,
                x + 100f, y, w - 100f, 20f, value);
            text.overflowMode = TextOverflowModes.Ellipsis;
            return y - 22f;
        }

        /// <summary>
        /// The kit slots: GUN, MOTOR, VEST across the card. A slot he has something in
        /// is a solid box with the thing named in it; a slot he has nothing in is a
        /// DASHED box with a dash in it - the design's own way of saying that an empty
        /// slot is a fact about the man and not an absence of interface.
        ///
        /// The dash is drawn as four hairline runs rather than a border style, because
        /// UGUI has no dashed border and a texture for three boxes is not worth it.
        /// </summary>
        float BuildKitSlots(Roster roster, Character member, float y)
        {
            const float gap = 10f;
            var slotW = (CardInner - gap * 2f) / 3f;
            const float slotH = 52f;

            var held = new List<RosterEquipment>();
            roster.HeldBy(member.Id, held);

            string gun = null, motor = null, vest = null;
            for (var i = 0; i < held.Count; i++)
            {
                var item = held[i];
                if (RosterOps.IsWeapon(item.Kind))
                    gun ??= item.DisplayName;
                else if (item.Kind == EquipmentKind.Vehicle || item.Kind == EquipmentKind.Motorcycle)
                    motor ??= item.DisplayName;
                else
                    vest ??= item.DisplayName;
            }

            // Every man of the outfit carries the .38 in his coat whether the stock book
            // knows it or not - it is what the roll draws beside his name, so the slot
            // must say the same thing rather than a dash the printout contradicts.
            var issued = gun != null;
            if (gun == null && member.Specialty == Specialty.None)
                gun = "Revolver, .38 — his own";
            KitSlot("GUN", gun, 0f, y, slotW, slotH, filled: issued);
            KitSlot("MOTOR", motor, slotW + gap, y, slotW, slotH);
            KitSlot("VEST", vest, (slotW + gap) * 2f, y, slotW, slotH);
            return y - slotH;
        }

        void KitSlot(string label, string item, float x, float y, float w, float h,
            bool? filled = null)
        {
            var rect = NewRect("Kit " + label, cardContent);
            PlaceTopLeft(rect, x, y, w, h);

            var signedOut = filled ?? !string.IsNullOrEmpty(item);
            if (signedOut)
            {
                Fill(rect, new Color(43f / 255f, 36f / 255f, 24f / 255f, 0.06f));
                Frame(rect, 1f, new Color(43f / 255f, 36f / 255f, 24f / 255f, 0.5f));
            }
            else
                DashedFrame(rect, w, h);

            Caps(rect, 10f, -6f, w - 20f, label, 9f, LedgerStyle.InkLabel, 3f);
            var text = Line(rect, LedgerStyle.Mono, 13f,
                signedOut ? LedgerStyle.Ink : LedgerStyle.InkPale, 10f, -24f, w - 20f, 20f,
                string.IsNullOrEmpty(item) ? "—" : item);
            text.overflowMode = TextOverflowModes.Ellipsis;
        }

        /// <summary>A dashed hairline box - an empty slot on a form.</summary>
        static void DashedFrame(RectTransform rect, float w, float h)
        {
            var ink = new Color(43f / 255f, 36f / 255f, 24f / 255f, 0.42f);
            DottedRule(rect, 0f, 0f, w, ink);
            DottedRule(rect, 0f, -h + 1f, w, ink);
            // The sides are the same dotted run stood on end.
            for (var side = 0; side < 2; side++)
            {
                var edge = NewRect("Dash", rect);
                PlaceTopLeft(edge, side == 0 ? 0f : w - 1f, 0f, h, 1f);
                edge.pivot = new Vector2(0f, 1f);
                edge.localRotation = Quaternion.Euler(0f, 0f, -90f);
                var raw = edge.gameObject.AddComponent<RawImage>();
                raw.texture = LedgerStyle.DotRule;
                raw.color = ink;
                raw.uvRect = new Rect(0f, 0f, h / 4f, 1f);
                raw.raycastTarget = false;
            }
        }

        /// <summary>
        /// Two boxes side by side: where the map says he is, and how far along he is.
        /// The left one is READ-ONLY on purpose and says so - a man is sent somewhere
        /// from the map, and the file only reports it.
        /// </summary>
        float BuildStandingBoxes(Roster roster, Character member, Assignment assignment,
            string crewName, float y)
        {
            const float gap = 12f;
            const float boxH = 56f;
            var boxW = (CardInner - gap) * 0.5f;

            var where = NewRect("Where", cardContent);
            PlaceTopLeft(where, 0f, y, boxW, boxH);
            Fill(where, new Color(43f / 255f, 36f / 255f, 24f / 255f, 0.05f));
            Frame(where, 1f, LedgerStyle.InkFaint);
            Caps(where, 10f, -6f, boxW - 20f, "WHERE HE IS · PER THE MAP", 9f,
                LedgerStyle.InkLabel, 3f);
            var line = Line(where, LedgerStyle.Mono, 13f, LedgerStyle.Ink, 10f, -26f,
                boxW - 20f, 20f, LedgerText.AssignmentLine(assignment, crewName));
            line.overflowMode = TextOverflowModes.Ellipsis;

            // How practised he is overall - the sum of what he has banked, as a word
            // and a run of ten. Derived, never stored: the roster has no XP.
            var steps = member.TotalHalfSteps();
            var floor = AttributeScale.Count * AttributeScale.MinHalfSteps;
            var ceiling = AttributeScale.Count * AttributeScale.MaxHalfSteps;
            var span = Mathf.Max(1, ceiling - floor);
            var filled = Mathf.Clamp(Mathf.RoundToInt(10f * (steps - floor) / span), 0, 10);
            var word = filled <= 3 ? "GREEN" : filled <= 6 ? "PROVEN" : "SEASONED";

            var experience = NewRect("Experience", cardContent);
            PlaceTopLeft(experience, boxW + gap, y, boxW, boxH);
            Fill(experience, new Color(43f / 255f, 36f / 255f, 24f / 255f, 0.05f));
            Frame(experience, 1f, LedgerStyle.InkFaint);
            Caps(experience, 10f, -6f, boxW - 20f, "EXPERIENCE · " + word, 9f,
                LedgerStyle.InkLabel, 3f);
            StepBar(experience, 10f, -34f, 10, filled, LedgerStyle.GreenOk, 6f, 12f, 9f);

            return y - boxH;
        }

        /// <summary>How good a man is at one trade, in the design's stepped meter and
        /// the word that goes with it - and, in pencil under the meter, how far along
        /// he is toward the next half step.</summary>
        float BuildAttributeRow(Character member, CharacterAttribute attribute, float y)
        {
            const float textInset = 3f;
            var label = Line(cardContent, LedgerStyle.Mono, 12.5f, LedgerStyle.InkSoft,
                textInset, y, 140f - textInset, 20f,
                LedgerText.AttributeLabel(attribute));
            label.overflowMode = TextOverflowModes.Ellipsis;

            var halfSteps = member.GetHalfSteps(attribute);
            StepBar(cardContent, 150f, y - 10f, AttributeScale.MaxHalfSteps, halfSteps,
                LedgerStyle.RedPen, 5f, 11f, 7f);

            Line(cardContent, LedgerStyle.Mono, 11.5f, LedgerStyle.InkDim, CardInner - 130f,
                y, 130f, 20f, AttributeWord(halfSteps), TextAlignmentOptions.MidlineRight);

            // Nothing is drawn for a trade he has never practised or one he has already
            // topped out at - an empty rule under every line would be eleven marks
            // saying nothing.
            var cost = Practice.NextCost(member, attribute);
            var banked = member.GetPractice(attribute);
            if (cost > 0 && banked > 0)
                Bar(cardContent, 150f, y - 17f,
                    StepBarWidth(AttributeScale.MaxHalfSteps, 5f, 7f), 3f,
                    Mathf.Clamp01(banked / (float)cost), LedgerStyle.InkFaint);

            // The whole line is a hover zone: rest the pointer on a trade and the
            // sticky note under it says what the number is FOR.
            var zone = NewRect("Hover", cardContent);
            PlaceTopLeft(zone, 0f, y, CardInner, 20f);
            ClickSurface(zone);
            var hover = zone.gameObject.AddComponent<StatHoverZone>();
            hover.almanac = this;
            hover.note = LedgerText.AttributeNote(attribute);

            return y - 21f;
        }

        /// <summary>A rating as a word - the design's wanting / middling / sound /
        /// exceptional, laid over this roster's own ten-step scale.</summary>
        static string AttributeWord(int halfSteps) =>
            halfSteps <= 3 ? "wanting"
            : halfSteps <= 5 ? "middling"
            : halfSteps <= 8 ? "sound"
            : "exceptional";

        /// <summary>A line in blue ballpoint down a ruled margin - a refusal, a hand's
        /// aside. The one voice on the sheet that is not typed.</summary>
        float MarginNote(string text, float y)
        {
            var height = 44f;
            var rect = NewRect("Margin", cardContent);
            PlaceTopLeft(rect, 0f, y, CardInner, height);
            Fill(rect, new Color(120f / 255f, 95f / 255f, 55f / 255f, 0.07f));
            Block("Edge", rect, 0f, 0f, 3f, height,
                new Color(47f / 255f, 74f / 255f, 122f / 255f, 0.55f));
            var copy = Paragraph(rect, LedgerStyle.SerifItalic, 14f, LedgerStyle.Ballpoint,
                14f, -8f, CardInner - 26f, 24f, text, lineSpacing: 1f);
            copy.overflowMode = TextOverflowModes.Ellipsis;
            Caps(rect, 14f, -30f, CardInner - 26f, "IN THE MARGIN · PEN", 8.5f,
                LedgerStyle.InkLabel, 3f);
            return y - height;
        }

        /// <summary>The front's card - the BOSS's card: his face and name up top, then
        /// the desk, the guards, what sits at the front, and the stock with GIVE
        /// straight into the headquarters locker.</summary>
        float BuildFrontDetail(Roster roster)
        {
            var boss = roster.FindBoss();
            var bossName = boss != null ? boss.FullName : Gangs.GangCatalog.BossName;
            var raw = Plate(cardContent, 0f, -8f, 128f, 156f, "THE BOSS");
            PortraitStudio.Request(
                PortraitStudio.FindPeoplePrefab(
                    boss != null && !string.IsNullOrEmpty(boss.Look)
                        ? boss.Look : Gangs.GangCatalog.BossModel),
                PortraitStudio.Framing.Bust, raw);

            var textX = 150f;
            var textW = CardInner - textX;
            var name = Line(cardContent, LedgerStyle.Condensed, 26f, LedgerStyle.Ink,
                textX, -6f, textW, 34f, bossName);
            name.characterSpacing = 1f;
            Caps(cardContent, textX, -40f, textW,
                "BOSS · " + Gangs.GangCatalog.Names[Gangs.GangCatalog.PlayerGangId], 12f,
                LedgerStyle.RedPen, 5f);

            var manager = roster.Find(roster.FrontId);
            var y = -70f;
            y = Particular("THE DESK", manager != null
                ? manager.FullName + " runs it"
                : "nobody runs the desk", textX, textW, y,
                manager != null ? LedgerStyle.Ink : LedgerStyle.RedPen);

            var guards = 0;
            for (var i = 0; i < roster.Members.Count; i++)
            {
                var member = roster.Members[i];
                if (member.Status == CharacterStatus.Active &&
                    member.Id != roster.FrontId &&
                    roster.AssignmentOf(member.Id).Kind == AssignmentKind.Pool)
                    guards++;
            }
            y = Particular("ON GUARD", guards == 1 ? "1 hood at the front"
                : guards + " hoods at the front", textX, textW, y,
                guards > 0 ? LedgerStyle.Ink : LedgerStyle.InkDim);
            y = Particular("IN THE SAFE",
                outfit ? LedgerText.Cash(outfit.Accounts.Safe) : "--", textX, textW, y);

            y = Mathf.Min(y, -186f) - 10f;

            // What the front holds - the locker and the guards' hands.
            y = CardHeading("AT THE FRONT", y);
            var anyHeld = false;
            for (var i = 0; i < roster.Equipment.Count; i++)
            {
                var item = roster.Equipment[i];
                if (item.OwnerId != RosterEquipment.FrontArmory)
                    continue;
                var holder = roster.Find(item.HolderId);
                anyHeld = true;

                y = ItemLine(LedgerText.EquipmentLine(item.Kind, item.DisplayName),
                    holder != null ? LedgerText.HeldByLine(holder.FullName) : "in the locker",
                    LedgerStyle.Ink, y);
                var itemId = item.Id;
                Tape(cardContent, "RETURN", CardInner - 100f, y + 24f, 100f, 22f, () =>
                {
                    lastRefusal = "";
                    var result = director.ReturnEquipment(itemId);
                    if (!result.Ok)
                        lastRefusal = result.Reason;
                    dirty = true;
                }, size: 10f, outline: true);
            }
            if (!anyHeld)
                y = ItemLine("The locker is empty.", "", LedgerStyle.InkDim, y);

            // The stock: GIVE dumps gear at the front, the guards draw it at once.
            y = CardHeading("ARMORY", y - 8f);
            var anyStock = false;
            for (var i = 0; i < roster.Equipment.Count; i++)
            {
                var item = roster.Equipment[i];
                if (item.OwnerId != RosterEquipment.Unheld)
                    continue;
                anyStock = true;

                y = ItemLine(LedgerText.EquipmentLine(item.Kind, item.DisplayName), "",
                    LedgerStyle.Ink, y);
                var itemId = item.Id;
                Tape(cardContent, "GIVE", CardInner - 100f, y + 24f, 100f, 22f, () =>
                {
                    lastRefusal = "";
                    var result = director.GiveEquipmentToFront(itemId);
                    if (!result.Ok)
                        lastRefusal = result.Reason;
                    dirty = true;
                }, size: 10f);
            }
            if (!anyStock)
                y = ItemLine("The stock is empty.", "", LedgerStyle.InkDim, y);

            if (lastRefusal.Length > 0)
                y = MarginNote(lastRefusal, y - 10f);

            return y;
        }

        /// <summary>A typed sub-heading on the card, with the design's hairline under.</summary>
        float CardHeading(string label, float y)
        {
            Caps(cardContent, 0f, y, CardInner, label, 11f, LedgerStyle.InkMid, 5f);
            Rule(cardContent, 0f, y - 18f, CardInner, LedgerStyle.InkFaint);
            return y - 26f;
        }

        /// <summary>One line of gear: what it is on the left, who has it on the right.</summary>
        float ItemLine(string what, string who, Color ink, float y)
        {
            var text = Line(cardContent, LedgerStyle.Mono, 13f, ink, 0f, y,
                CardInner - 210f, 22f, what);
            text.overflowMode = TextOverflowModes.Ellipsis;
            if (who.Length > 0)
            {
                var holder = Line(cardContent, LedgerStyle.Mono, 12f, LedgerStyle.InkDim,
                    CardInner - 320f, y, 210f, 22f, who, TextAlignmentOptions.MidlineRight);
                holder.overflowMode = TextOverflowModes.Ellipsis;
            }
            return y - 24f;
        }

        /// <summary>The gear half of a card - and a LIEUTENANT's card only. Gear issues
        /// to the head of a crew and is read back off the same card, so on a hood's or a
        /// specialist's card both listings are somebody else's book: what he carries is
        /// already in his kit slots above and in the gun drawn beside his name.</summary>
        float BuildEquipmentSection(Roster roster, Character member, float y)
        {
            if (member.Rank != Rank.Lieutenant)
                return y;

            y = BuildCrewDeck(roster, member, y);
            return BuildArmoryStock(roster, member, y - 8f);
        }

        /// <summary>What his crew owns. Read-only by design: gear dealt to a crew stays
        /// with it, so each line carries the word ASSIGNED where a verb would be.</summary>
        float BuildCrewDeck(Roster roster, Character member, float y)
        {
            y = CardHeading("IN HAND · HIS CREW'S", y);

            // His crew's DECK, not his own two hands: every item the crew owns, wherever
            // the deal has put it. NormalizeArms moves holders and never owners, so a
            // piece riding on one of his hoods still reads on his card - and this is now
            // the only card it appears on.
            var anyHeld = false;
            for (var i = 0; i < roster.Equipment.Count; i++)
            {
                var item = roster.Equipment[i];
                if (item.OwnerId != member.Id)
                    continue;
                anyHeld = true;

                var holder = roster.Find(item.HolderId);
                y = ItemLine(LedgerText.EquipmentLine(item.Kind, item.DisplayName),
                    holder != null && holder.Id != member.Id
                        ? LedgerText.HeldByLine(holder.FullName)
                        : "",
                    LedgerStyle.Ink, y);

                // Dealt gear is dealt: no button here, because there is no taking it
                // back off a crew. The word is a state the boss reads, not a verb.
                Caps(cardContent, CardInner - 100f, y + 24f, 100f, "ASSIGNED", 9.5f,
                    LedgerStyle.InkLabel, 3f, TextAlignmentOptions.MidlineRight);
            }
            if (!anyHeld)
                y = ItemLine("Nothing signed out.", "", LedgerStyle.InkDim, y);

            return y;
        }

        /// <summary>The rest of the outfit's gear as he sees it: what is in the safe,
        /// with GIVE, and what another group already holds, muted and untakeable.</summary>
        float BuildArmoryStock(Roster roster, Character member, float y)
        {
            y = CardHeading("ARMORY · WHAT IS LEFT", y);

            var anyStock = false;
            for (var i = 0; i < roster.Equipment.Count; i++)
            {
                var item = roster.Equipment[i];
                // What his crew already owns is the IN HAND listing above, not stock.
                if (item.OwnerId == member.Id)
                    continue;
                anyStock = true;

                if (item.OwnerId == RosterEquipment.Unheld)
                {
                    y = ItemLine(LedgerText.EquipmentLine(item.Kind, item.DisplayName), "",
                        LedgerStyle.Ink, y);
                    if (!member.Gone)
                        Tape(cardContent, "GIVE", CardInner - 100f, y + 24f, 100f, 22f, () =>
                        {
                            lastRefusal = "";
                            var result = director.GiveEquipment(item.Id, member.Id);
                            if (!result.Ok)
                                lastRefusal = result.Reason;
                            dirty = true;
                        }, size: 10f);
                }
                else
                {
                    // The finite pool made visible: an item another group owns shows
                    // here, muted and with no verb. Nothing on this card can take it -
                    // a crew's gear stays with the crew, and only the front's locker
                    // gives anything back to the safe.
                    var holder = roster.Find(item.HolderId);
                    y = ItemLine(LedgerText.EquipmentLine(item.Kind, item.DisplayName),
                        LedgerText.HeldByLine(holder != null ? holder.FullName
                            : item.OwnerId == RosterEquipment.FrontArmory
                                ? "the front" : "?"),
                        LedgerStyle.InkDim, y);
                }
            }

            if (!anyStock)
                y = ItemLine("The stock is empty.", "", LedgerStyle.InkDim, y);

            return y;
        }

        /// <summary>
        /// The card's verbs, pinned to the foot of the file where the scroll cannot
        /// take them away. Because there is no dialog system and never has been, a
        /// confirm is the same strip with the warning typed across it and the two
        /// answers in its place.
        /// </summary>
        void BuildActionStrip(Roster roster, Character member)
        {
            Rule(cardFoot, 0f, 0f, CardInner, LedgerStyle.InkFaint);

            if (member.Gone || member.Specialty != Specialty.None)
            {
                Caps(cardFoot, 0f, -20f, CardInner,
                    member.Gone ? "off the books · nothing left to decide"
                        : "bought talent · neither promoted nor posted",
                    10f, LedgerStyle.InkLabel, 3f, TextAlignmentOptions.Center);
                return;
            }

            if (member.Rank == Rank.Boss)
            {
                Caps(cardFoot, 0f, -20f, CardInner,
                    "root command · managed in the organization file",
                    10f, LedgerStyle.InkLabel, 3f, TextAlignmentOptions.Center);
                return;
            }

            const float buttonH = 34f;
            var half = (CardInner - 12f) * 0.5f;

            if (pendingConfirm == Confirm.Promote)
            {
                Caps(cardFoot, 0f, -8f, CardInner,
                    LedgerText.PromoteWarning(member.FullName), 9.5f, LedgerStyle.RedPen, 2f);
                Tape(cardFoot, "PROMOTE ANYWAY", 0f, -22f, half, buttonH,
                    () => DoPromote(member.Id), red: true);
                Tape(cardFoot, "NEVER MIND", half + 12f, -22f, half, buttonH, () =>
                {
                    pendingConfirm = Confirm.None;
                    dirty = true;
                }, outline: true);
                return;
            }

            if (pendingConfirm == Confirm.Demote)
            {
                var crew = roster.CrewOf(member.Id);
                Caps(cardFoot, 0f, -8f, CardInner,
                    LedgerText.DemoteConfirm(member.FirstName,
                        crew != null ? crew.HoodIds.Count : 0), 9.5f, LedgerStyle.RedPen, 2f);
                Tape(cardFoot, "DISBAND HIS CREW", 0f, -22f, half, buttonH, () =>
                {
                    pendingConfirm = Confirm.None;
                    var result = director.Demote(member.Id);
                    lastRefusal = result.Ok ? "" : result.Reason;
                    dirty = true;
                }, red: true);
                Tape(cardFoot, "NEVER MIND", half + 12f, -22f, half, buttonH, () =>
                {
                    pendingConfirm = Confirm.None;
                    dirty = true;
                }, outline: true);
                return;
            }

            if (member.Rank == Rank.Lieutenant)
            {
                Tape(cardFoot, "OFF THE BOOKS", 0f, -14f, half, buttonH, () =>
                {
                    pendingConfirm = Confirm.Demote;
                    dirty = true;
                }, red: true, outline: true);
                return;
            }

            // PROMOTE and OFF THE BOOKS are the file's ONLY two actions. Where a man
            // reports is the Organization file's business and this dossier only reports
            // it - keeping the two views on the same Character without duplicating authority.
            Tape(cardFoot, "PROMOTE", 0f, -14f, half, buttonH, () =>
            {
                var check = director.CheckPromote(member.Id);
                if (!check.CanPromote)
                    lastRefusal = check.Reason;
                else if (check.LowStatWarning)
                    pendingConfirm = Confirm.Promote;
                else
                    DoPromote(member.Id);
                dirty = true;
            });
        }

        void DoPromote(int id)
        {
            pendingConfirm = Confirm.None;
            var result = director.Promote(id, out _);
            lastRefusal = result.Ok ? "" : result.Reason;
            dirty = true;
        }
    }
}
