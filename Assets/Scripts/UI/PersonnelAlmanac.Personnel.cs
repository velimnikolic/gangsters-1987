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
        //
        // The design's split is 2fr to the printout and minmax(230px, 1fr) to the file:
        // the roll is what a boss reads, and the file is what he opens beside it.

        const float PaneGap = 20f;
        static float PrintW = (PageWidth - PaneGap) * (2f / 3f);
        static float FileW = PageWidth - PaneGap - PrintW;
        static float PaneTop = PageTop - PersonnelHeadH;
        static float PaneH = -(PageBottom - PaneTop);

        static float ListLeft = PageLeft;
        static float FileLeft = PageLeft + PrintW + PaneGap;

        /// <summary>The page's own head, over both panes.</summary>
        const float PersonnelHeadH = 72f;

        // ---- inside the printout ----

        const float PrintPad = 10f;
        static float PrintInner = PrintW - PrintPad * 2f;

        /// <summary>The dark band the column heads are printed on.</summary>
        const float PrintHeadH = 30f;

        /// <summary>What the payroll band at the foot takes off the bottom.</summary>
        const float RollFoot = 58f;

        static float ListHeight = PaneH - PrintHeadH - RollFoot;

        /// <summary>Two lines fit in a row: the man's name with his rank after it, and
        /// under it the post he actually stands on. The design's own row.</summary>
        const float RowHeight = 40f;

        /// <summary>A band naming a run of lines that belong together.</summary>
        const float BandHeight = 26f;

        // The printout's column grid, in printout-inner coordinates. The design's
        // 22 | 1fr | 66 | 46 | 52 | 56 at a six-unit gap, widened where our words are
        // longer than the design's placeholders: a machine pistol does not fit in 66.
        const float ColGap = 6f;
        const float IdxW = 22f;
        const float CarryW = 132f;
        const float CondW = 92f;
        const float StandW = 78f;
        const float WageW = 92f;

        static float ColName = IdxW + ColGap;
        static float NameW = 240f;
        static float ColCarrying = 0f;
        static float ColCondition = 0f;
        static float ColStanding = 0f;
        static float ColWage = 0f;

        // ---- inside the personal file ----

        const float FilePad = 16f;
        static float FileInner = FileW - FilePad * 2f;

        /// <summary>The fixed head band, and the fixed foot the verbs are pinned to;
        /// the file scrolls between them.</summary>
        const float FileHeadH = 30f;
        const float FileFootH = 56f;
        static float FileBodyH = PaneH - FileHeadH - FileFootH;

        /// <summary>How far a hood's line is set in from his lieutenant's.</summary>
        const float HoodIndent = 14f;

        // The dossier body was written against the file pane's old names. They are the
        // same measurements under the v2 names, and aliasing them here keeps one
        // authority for each rather than two numbers that can drift apart.
        static float CardPad => FilePad;
        static float CardInner => FileInner;
        static float CardHead => FileHeadH;
        static float CardFoot => FileFootH;
        static float CardBodyH => FileBodyH;

        /// <summary>The two panes and everything measured inside them. The sheet is
        /// full bleed, so a pane's width and height are struck from the live frame
        /// rather than baked at compile time - a wider window widens both panes and a
        /// taller one lengthens the roll.</summary>
        static void MeasurePersonnelLayout()
        {
            PrintW = (PageWidth - PaneGap) * (2f / 3f);
            FileW = Mathf.Max(300f, PageWidth - PaneGap - PrintW);
            PrintW = PageWidth - PaneGap - FileW;
            PaneTop = PageTop - PersonnelHeadH;
            PaneH = -(PageBottom - PaneTop);

            ListLeft = PageLeft;
            FileLeft = PageLeft + PrintW + PaneGap;

            PrintInner = PrintW - PrintPad * 2f;
            ListHeight = PaneH - PrintHeadH - RollFoot;

            ColWage = PrintInner - WageW;
            ColStanding = ColWage - ColGap - StandW;
            ColCondition = ColStanding - ColGap - CondW;
            ColCarrying = ColCondition - ColGap - CarryW;
            ColName = IdxW + ColGap;
            NameW = Mathf.Max(160f, ColCarrying - ColGap - ColName);

            FileInner = FileW - FilePad * 2f;
            FileBodyH = PaneH - FileHeadH - FileFootH;
        }

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

            LedgerV2.PageHead(root, PageLeft, PageTop, PageWidth, "PERSONNEL",
                "PAYROLL PRINTOUT · LINE PRINTER 03 · EVERY MAN DRAWS WHETHER HE " +
                "WORKS OR NOT");
            BuildPersonnelFilters(root);
            BuildPrintout(root);
            BuildPersonalFile(root);
            BuildSortMenu(root);
        }

        /// <summary>The four filters, held to the right of the page head. A chip is the
        /// dark key when it is actually filtering something, so the sheet says at a
        /// glance that it is not showing the whole roll.</summary>
        void BuildPersonnelFilters(RectTransform root)
        {
            const float h = 26f;
            var y = PageTop - 4f;
            var x = PageWidth + PageLeft;

            x -= 150f;
            showTape = LedgerV2.Chip(root, "", x, y, 150f, h, false, CycleShow);
            x -= 158f;
            postTape = LedgerV2.Chip(root, "", x, y, 150f, h, false, CyclePost);
            x -= 158f;
            rankTape = LedgerV2.Chip(root, "", x, y, 150f, h, false, CycleRank);
            x -= 238f;
            sortTape = LedgerV2.Chip(root, "", x, y, 230f, h, false, ToggleSortMenu);

            sortPill = PillFace(sortTape);
            rankPill = PillFace(rankTape);
            postPill = PillFace(postTape);
            showPill = PillFace(showTape);
        }

        /// <summary>The left pane: the printout. A dark band of column heads, the
        /// scrolling roll under it, and the payroll struck across its foot.</summary>
        void BuildPrintout(RectTransform root)
        {
            var panel = LedgerV2.Card("Printout", root, ListLeft, PaneTop, PrintW, PaneH);

            // ---- the column heads, on the dark band ----
            var band = NewRect("Heads", panel);
            PlaceTopLeft(band, 0f, 0f, PrintW, PrintHeadH);
            Fill(band, LedgerV2.Head);

            var headY = -(PrintHeadH - 14f) * 0.5f;
            LedgerV2.Mono(band, PrintPad, headY, IdxW, "#", 9.5f, LedgerV2.HeadDim, 0f);
            LedgerV2.Mono(band, PrintPad + ColName, headY, NameW, "NAME", 9.5f,
                LedgerV2.HeadInk, 10f);
            LedgerV2.Mono(band, PrintPad + ColCarrying, headY, CarryW, "CARRYING", 9.5f,
                LedgerV2.HeadInk, 8f, TextAlignmentOptions.MidlineRight);
            LedgerV2.Mono(band, PrintPad + ColCondition, headY, CondW, "COND.", 9.5f,
                LedgerV2.HeadInk, 8f, TextAlignmentOptions.MidlineRight);
            LedgerV2.Mono(band, PrintPad + ColStanding, headY, StandW, "STANDING", 9.5f,
                LedgerV2.HeadInk, 8f, TextAlignmentOptions.MidlineRight);
            LedgerV2.Mono(band, PrintPad + ColWage, headY, WageW, "WAGE", 9.5f,
                LedgerV2.HeadInk, 10f, TextAlignmentOptions.MidlineRight);

            // ---- the roll ----
            listViewport = NewRect("Roll", panel);
            PlaceTopLeft(listViewport, PrintPad, -PrintHeadH, PrintInner, ListHeight);
            listViewport.gameObject.AddComponent<RectMask2D>();

            listContent = NewRect("Rows", listViewport);
            listContent.anchorMin = new Vector2(0f, 1f);
            listContent.anchorMax = new Vector2(1f, 1f);
            listContent.pivot = new Vector2(0f, 1f);
            listContent.anchoredPosition = Vector2.zero;
            listContent.sizeDelta = new Vector2(0f, ListHeight);

            // ---- the foot: what the whole sheet costs a day, and why ----
            var foot = NewRect("Payroll", panel);
            PlaceTopLeft(foot, 0f, -(PrintHeadH + ListHeight), PrintW, RollFoot);
            Fill(foot, LedgerV2.PanelBand);
            Block("Foot rule", foot, 0f, 0f, PrintW, 3f, LedgerV2.Ink);

            var title = LedgerV2.Name(foot, PrintPad, -12f, 300f, "PAYROLL · RUNNING", 15f);
            title.characterSpacing = 5f;
            LedgerV2.Mono(foot, PrintPad, -32f, PrintInner - 200f,
                "the jailed and the hurt keep drawing · only the dead come off", 10.5f,
                LedgerV2.Muted, 1f);
            payrollFigure = LedgerV2.Figure(foot, PrintPad + PrintInner - 200f, -16f, 200f,
                "", 22f);
        }

        static Image PillFace(TMP_Text pillLabel) =>
            pillLabel.transform.parent.GetComponent<Image>();

        /// <summary>The right pane: the personal file. A dark head band, a scrolling
        /// body, and the verbs pinned to the foot where a hand would rest.</summary>
        void BuildPersonalFile(RectTransform root)
        {
            var panel = LedgerV2.Card("File", root, FileLeft, PaneTop, FileW, PaneH);

            var band = NewRect("Head", panel);
            PlaceTopLeft(band, 0f, 0f, FileW, FileHeadH);
            Fill(band, LedgerV2.Head);
            var title = LedgerV2.Mono(band, FilePad, -(FileHeadH - 14f) * 0.5f,
                FileInner - 200f, "PERSONAL FILE", 10f, LedgerV2.HeadInk, 13f);
            title.font = LedgerStyle.MonoBold;
            cardFileNo = LedgerV2.Mono(band, FilePad + FileInner - 200f,
                -(FileHeadH - 14f) * 0.5f, 200f, "", 9.5f, LedgerV2.HeadDim, 4f,
                TextAlignmentOptions.MidlineRight);

            cardViewport = NewRect("Body", panel);
            PlaceTopLeft(cardViewport, FilePad, -FileHeadH, FileInner, FileBodyH);
            cardViewport.gameObject.AddComponent<RectMask2D>();

            cardContent = NewRect("Content", cardViewport);
            cardContent.anchorMin = new Vector2(0f, 1f);
            cardContent.anchorMax = new Vector2(1f, 1f);
            cardContent.pivot = new Vector2(0f, 1f);
            cardContent.anchoredPosition = Vector2.zero;
            cardContent.sizeDelta = new Vector2(0f, FileBodyH);

            cardFoot = NewRect("Foot", panel);
            PlaceTopLeft(cardFoot, FilePad, -(FileHeadH + FileBodyH), FileInner, FileFootH);

            // The one shared hover note - child of the PANEL, not the content (which
            // rebuilds under the pointer), raised to last sibling on every show so it
            // prints over whatever it covers.
            hoverNote = NewRect("Note", panel);
            PlaceTopLeft(hoverNote, 0f, 0f, FileInner - 60f, 60f);
            Fill(hoverNote, LedgerV2.Head);
            hoverNoteText = Text("Text", hoverNote, LedgerStyle.Mono, 12f,
                LedgerV2.HeadInk, TextAlignmentOptions.TopLeft);
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
        /// <summary>A chip is the dark key while it is actually filtering, and a
        /// hairline box while it is not: the sheet has to say it is showing less than
        /// the whole roll without being asked.</summary>
        static void SetPill(Image face, TMP_Text label, bool filtering)
        {
            if (!face)
                return;
            face.color = filtering
                ? LedgerV2.Head
                : new Color(LedgerV2.Panel.r, LedgerV2.Panel.g, LedgerV2.Panel.b, 0f);
            label.color = filtering ? LedgerV2.HeadCream : LedgerV2.Ink;
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

            var slip = LedgerV2.Card("SortMenu", root, ListLeft, PageTop + 22f, 260f,
                entries * rowH + 12f, LedgerV2.Head);
            sortMenu = slip.gameObject;
            // The slip's own body must swallow stray clicks.
            var surfaceFill = slip.Find("Face").GetComponent<Image>();
            surfaceFill.raycastTarget = true;

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

                LedgerV2.Mono(row, 12f, -5f, 230f, label, 11f, LedgerV2.HeadInk, 4f);
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
        /// A band across the roll naming a run of lines that belong together - a crew,
        /// the front, the pool. The band is a LABEL: it is not a row and nothing on it
        /// can be pressed, because the lieutenant's own line underneath is still the
        /// crew's handle.
        /// </summary>
        /// <summary>The ground of the band just built - it is also the button's target
        /// graphic when the band happens to be one.</summary>
        Image bandFace;

        RectTransform Band(string title, string aside, float y)
        {
            var rect = NewRect("Band", listContent);
            PlaceTopLeft(rect, 0f, y, PrintInner, BandHeight);
            bandFace = Fill(rect, LedgerV2.PanelDark);
            Block("Band rule", rect, 0f, 0f, PrintInner, 1f, LedgerV2.Rule);

            var label = LedgerV2.Name(rect, ColName, -6f, 320f, title, 13f);
            label.characterSpacing = 9f;
            var note = LedgerV2.Mono(rect, PrintInner - 320f, -6f, 320f, aside, 9.5f,
                LedgerV2.Muted, 4f, TextAlignmentOptions.MidlineRight);
            note.text = aside.ToUpperInvariant();
            return rect;
        }

        void BuildCrewBand(Roster roster, int crewId, float y)
        {
            var crew = roster.FindCrew(crewId);
            var lieutenant = crew != null ? roster.Find(crew.LieutenantId) : null;
            var name = lieutenant != null
                ? LedgerText.CrewName(lieutenant.Surname).ToUpperInvariant()
                : "A CREW";
            var men = crew != null ? crew.HoodIds.Count : 0;
            Band(name, men == 1 ? "one man under him" : men + " men under him", y);
        }

        void BuildSectionHeader(Roster roster, RowKind kind, float y)
        {
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

            var rect = Band(title, aside, y);

            // The front's band is also the BOSS's line: clicking it opens the front
            // card - his face, the desk, the locker - the way a member row opens his.
            // It is the only band that does anything.
            if (kind != RowKind.FrontHeader)
                return;
            if (selectedId == FrontSelection)
                bandFace.color = LedgerV2.Picked;
            bandFace.raycastTarget = true;
            RowButton(rect, bandFace, () => SelectMember(FrontSelection));
        }

        static string FrontAside(Roster roster)
        {
            var manager = roster.Find(roster.FrontId);
            return manager != null ? manager.Surname + " runs the desk" : "nobody at the desk";
        }

        /// <summary>
        /// One man's line: his number, his name with his rank after it, the post he
        /// actually stands on under it, and then the four columns that read straight
        /// down a roll of sixty - what he carries, how he is, whether he is earning,
        /// and what he costs. The wage most of all, because payroll is the pressure the
        /// whole game turns on.
        /// </summary>
        void BuildCharacterRow(Roster roster, int id, float y, int index, bool indent,
            bool lieutenantRow = false)
        {
            var member = roster.Find(id);
            if (member == null)
                return;

            var rect = NewRect("Row", listContent);
            PlaceTopLeft(rect, 0f, y, PrintInner, RowHeight);

            var chosen = id == selectedId;
            var dead = member.Gone; // struck through: dead or deserted

            // ONE Image on the row: it is both the row's ground and the button's target
            // graphic. A second AddComponent<Image> on the same object silently answers
            // null in Unity, and a null target graphic takes the whole row down with it.
            var face = Fill(rect, chosen ? LedgerV2.Picked
                : lieutenantRow ? LedgerV2.PanelBand
                : new Color(LedgerV2.Panel.r, LedgerV2.Panel.g, LedgerV2.Panel.b, 0f));
            face.raycastTarget = true;
            Block("Row rule", rect, 0f, 0f, PrintInner, 1f,
                lieutenantRow ? LedgerV2.Rule : LedgerV2.Hair);

            // A row does ONE thing: it opens that man's file. The ledger reads.
            RowButton(rect, face, () => SelectMember(id));

            var ink = dead ? LedgerV2.Faint : LedgerV2.Ink;
            var nameX = ColName + (indent ? HoodIndent : 0f);

            LedgerV2.Mono(rect, 0f, -8f, IdxW, index.ToString("00"), 10f, LedgerV2.Muted, 0f);

            var name = Line(rect, lieutenantRow ? LedgerStyle.MonoBold : LedgerStyle.Mono,
                lieutenantRow ? 14f : 13f, ink, nameX, -6f, NameW - 110f, 18f,
                member.FullName);
            name.overflowMode = TextOverflowModes.Ellipsis;

            LedgerV2.Mono(rect, nameX + NameW - 110f, -6f, 110f,
                member.Specialty != Specialty.None
                    ? LedgerText.SpecialtyLabel(member.Specialty)
                    : LedgerText.RankLabel(member.Rank),
                9.5f, dead ? LedgerV2.Faint : LedgerV2.Label, 7f,
                TextAlignmentOptions.MidlineRight);

            // The second line: where he stands. A sorted roll answers with the figure it
            // was sorted BY instead, because that is the question the reader just asked.
            LedgerV2.Mono(rect, nameX, -22f, NameW, AsideFor(roster, member), 9.5f,
                LedgerV2.Muted, 5f);

            BuildRowCells(roster, rect, member, dead);

            // The dead are struck through in pen - the record keeps their line.
            if (dead)
                Block("Struck", rect, nameX - 2f, -RowHeight * 0.5f + 1f,
                    NameW - 4f, 1.5f, LedgerV2.Red);
        }

        /// <summary>The line under a man's name: what the roll was sorted by when it was
        /// sorted, and otherwise the post he stands on.</summary>
        string AsideFor(Roster roster, Character member)
        {
            if (options.Sort == SortKey.Loyalty)
                return "loyalty " + member.Loyalty;
            if (options.Sort == SortKey.Attribute)
                return LedgerText.AttributeLabel(options.SortAttribute).ToLowerInvariant() +
                       " " + LedgerText.Stars(member.GetHalfSteps(options.SortAttribute));

            var post = roster.AssignmentOf(member.Id);
            if (post.Kind == AssignmentKind.Pool)
                return "no post";
            var crewName = "";
            if (post.Kind == AssignmentKind.Crew)
            {
                var crew = roster.FindCrew(post.CrewId);
                var lieutenant = crew != null ? roster.Find(crew.LieutenantId) : null;
                crewName = lieutenant != null
                    ? LedgerText.CrewName(lieutenant.Surname) : "a crew";
            }
            return LedgerText.AssignmentLine(post, crewName);
        }

        /// <summary>What a man carries, named. The design prints the WORD, not a picture
        /// of the gun: a column of little photographs cannot be read down.</summary>
        string CarryingLine(Roster roster, Character member)
        {
            carriedScratch.Clear();
            roster.HeldBy(member.Id, carriedScratch);
            string gun = null;
            var extra = 0;
            for (var i = 0; i < carriedScratch.Count; i++)
            {
                var item = carriedScratch[i];
                if (!RosterOps.IsWeapon(item.Kind))
                    continue;
                if (gun == null)
                    gun = LedgerText.EquipmentLabel(item.Kind);
                else
                    extra++;
            }
            if (gun == null)
                return "—";
            return extra > 0 ? gun + " +" + extra : gun;
        }

        readonly List<RosterEquipment> carriedScratch = new List<RosterEquipment>();

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

        /// <summary>The four scan columns, all held to their right margins so they read
        /// straight down the roll.</summary>
        void BuildRowCells(Roster roster, RectTransform rect, Character member, bool dead)
        {
            var carrying = CarryingLine(roster, member);
            LedgerV2.Mono(rect, ColCarrying, -6f, CarryW, carrying, 11f,
                carrying == "—" ? LedgerV2.Red : (dead ? LedgerV2.Faint : LedgerV2.Body),
                1f, TextAlignmentOptions.MidlineRight);

            var status = member.Status;
            LedgerV2.Figure(rect, ColCondition, -6f, CondW, ConditionWord(status), 11f,
                status == CharacterStatus.Active ? LedgerV2.Green
                : dead ? LedgerV2.Faint : LedgerV2.Red);

            // A man on his feet gets no second line: "on his feet" under FIT is a line
            // of type that says nothing, sixty times down the page. The note is for the
            // men something HAPPENED to, which is the only reason to scan the column.
            var note = ConditionNote(member, status);
            if (note.Length > 0)
                LedgerV2.Mono(rect, ColCondition - 40f, -22f, CondW + 40f, note, 9f,
                    dead ? LedgerV2.Faint : LedgerV2.Red, 0f,
                    TextAlignmentOptions.MidlineRight);

            var posted = roster.AssignmentOf(member.Id).Kind != AssignmentKind.Pool;
            var standing = member.Wanted ? "WANTED" : dead ? "-" : posted ? "ACTIVE" : "IDLE";
            LedgerV2.Figure(rect, ColStanding, -6f, StandW, standing, 11f,
                member.Wanted ? LedgerV2.Red
                : dead ? LedgerV2.Faint
                : posted ? LedgerV2.Green : LedgerV2.Red);

            LedgerV2.Figure(rect, ColWage, -6f, WageW,
                LedgerText.Cash(Outfit.Wages.WageFor(member)), 12.5f,
                dead ? LedgerV2.Faint : LedgerV2.Ink);
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
                    "pick a man off the printout", 12f, LedgerV2.Label, 4f,
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
            const float plateW = 94f;
            const float plateH = 116f;
            var raw = LedgerV2.PortraitPlate(cardContent, 0f, -8f, plateW, plateH,
                InitialsOf(member.FullName), LedgerV2.DarkPlate, LedgerV2.DarkPlateInk);
            PortraitStudio.Request(MemberModel(member), PortraitStudio.Framing.Bust, raw);
            Caps(cardContent, 0f, -(plateH + 12f), plateW,
                "P-" + (1100 + member.Id).ToString("0000") + " · 1987", 9f,
                LedgerV2.Label, 2f);

            var textX = plateW + 22f;
            var textW = CardInner - textX;

            var name = Line(cardContent, LedgerStyle.Condensed, 24f, LedgerV2.Ink,
                textX, -20f, textW, LineBox(24f), member.FullName);
            name.characterSpacing = 1f;
            name.overflowMode = TextOverflowModes.Ellipsis;

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
            // The design's order: rank over the name, then a red rule, then the two
            // lines that say who he answers to and where he stands.
            var rank = LedgerV2.Mono(cardContent, textX, -4f, textW,
                rankLine.ToUpperInvariant(), 9.5f,
                member.Rank == Rank.Lieutenant ? LedgerV2.Lieutenant : LedgerV2.Muted, 12f);
            rank.font = LedgerStyle.MonoBold;

            Block("Name rule", cardContent, textX, -50f, Mathf.Min(textW, 220f), 1f,
                LedgerV2.Red);

            LedgerV2.Mono(cardContent, textX, -58f, textW,
                crewName.Length > 0 ? "IN " + crewName : "NO CREW", 11f, LedgerV2.Muted, 3f);

            var y = -84f;
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
                    hasParent ? LedgerV2.Ink : LedgerV2.Red);
            }
            y = Particular("WAGE", LedgerText.Cash(Outfit.Wages.WageFor(member)) + " / day",
                textX, textW, y);
            y = Particular("CONDITION", LedgerText.StatusLabel(member.Status), textX, textW, y,
                member.Status == CharacterStatus.Active ? LedgerV2.Ink : LedgerV2.Red);
            if (TryObservedBlock(member.Id, out var currentBlock))
            {
                y = Particular("CURRENT STATUS", "On street", textX, textW, y);
                y = Particular("CURRENT BLOCK", currentBlock, textX, textW, y);
            }
            y = Particular("LOYALTY", member.Loyalty + " of 100", textX, textW, y,
                member.Loyalty < 35 ? LedgerV2.Red : LedgerV2.Ink);

            // The stamps: the law's word over the photograph, WANTED beside the name.
            // What the law says about him, and what the outfit says: two flat chips,
            // one under the photograph and one against his name. The first edition
            // tilted a rubber stamp across the picture; a terminal has no stamp.
            if (member.Status != CharacterStatus.Active)
                LedgerV2.Status(cardContent, 0f, -(plateH + 26f), plateW, 22f,
                    member.Status switch
                    {
                        CharacterStatus.Dead => "DECEASED",
                        CharacterStatus.Deserted => "DESERTED",
                        CharacterStatus.Jailed => "IN CUSTODY",
                        _ => "IN HOSPITAL",
                    }, LedgerV2.Red, 10f);
            if (member.Wanted)
                LedgerV2.Status(cardContent, CardInner - 96f, -6f, 96f, 22f, "WANTED",
                    LedgerV2.Red, 10f);

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
                LedgerV2.Body, 5f);
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
                LedgerV2.Body, 5f);
            Caps(cardContent, CardInner - 150f, y, 150f,
                member.RapSheet.Count == 1 ? "1 ENTRY" : member.RapSheet.Count + " ENTRIES",
                9f, LedgerV2.Label, 3f, TextAlignmentOptions.MidlineRight);
            y -= 20f;
            Rule(cardContent, 0f, y + 4f, CardInner, LedgerV2.Rule);

            if (member.RapSheet.Count == 0)
            {
                Line(cardContent, LedgerStyle.MonoItalic, 12f, LedgerV2.Muted,
                    0f, y - 2f, CardInner, rowH, "No record. Nothing on him at all.");
                return y - rowH - 10f;
            }

            for (var i = 0; i < member.RapSheet.Count; i++)
            {
                var entry = member.RapSheet[i];

                Line(cardContent, LedgerStyle.Mono, 11f, LedgerV2.Label,
                    0f, y - 2f, dateW, rowH, entry.Date);

                var charge = Line(cardContent, LedgerStyle.Mono, 12f, LedgerV2.Body,
                    dateW, y - 2f, CardInner - dateW - outcomeW - 8f, rowH, entry.Charge);
                charge.overflowMode = TextOverflowModes.Ellipsis;

                var outcome = Line(cardContent, LedgerStyle.Mono, 11f, LedgerV2.Red,
                    CardInner - outcomeW, y - 2f, outcomeW, rowH, entry.Outcome,
                    TextAlignmentOptions.MidlineRight);
                outcome.overflowMode = TextOverflowModes.Ellipsis;

                y -= rowH;
                if (i < member.RapSheet.Count - 1)
                    LedgerV2.Leader(cardContent, 0f, y + 3f, CardInner);
            }

            return y - 10f;
        }

        /// <summary>One particular on the file: the label on the left, the answer held
        /// to the right margin, and the dotted rule the design closes every one of them
        /// with. Answers the y below.</summary>
        float Particular(string label, string value, float x, float w, float y, Color? ink = null)
        {
            LedgerV2.Mono(cardContent, x, y, 130f, label, 10.5f, LedgerV2.Label, 6f);
            var text = LedgerV2.Figure(cardContent, x + 134f, y, w - 134f, value, 12.5f,
                ink ?? LedgerV2.Ink);
            text.overflowMode = TextOverflowModes.Ellipsis;
            LedgerV2.Leader(cardContent, x, y - 19f, w);
            return y - 24f;
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

            Caps(rect, 10f, -6f, w - 20f, label, 9f, LedgerV2.Label, 3f);
            var text = Line(rect, LedgerStyle.Mono, 13f,
                signedOut ? LedgerV2.Ink : LedgerV2.Faint, 10f, -24f, w - 20f, 20f,
                string.IsNullOrEmpty(item) ? "—" : item);
            text.overflowMode = TextOverflowModes.Ellipsis;
        }

        /// <summary>An empty slot on the form: a hairline box, nothing dashed. The
        /// v2 sheet does not draw a form somebody typed on - it draws the form.</summary>
        static void DashedFrame(RectTransform rect, float w, float h) =>
            Frame(rect, 1f, LedgerV2.Rule);

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
            Frame(where, 1f, LedgerV2.Rule);
            Caps(where, 10f, -6f, boxW - 20f, "WHERE HE IS · PER THE MAP", 9f,
                LedgerV2.Label, 3f);
            var line = Line(where, LedgerStyle.Mono, 13f, LedgerV2.Ink, 10f, -26f,
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
            Frame(experience, 1f, LedgerV2.Rule);
            Caps(experience, 10f, -6f, boxW - 20f, "EXPERIENCE · " + word, 9f,
                LedgerV2.Label, 3f);
            LedgerV2.Pips(experience, 10f, -34f, 10, filled, LedgerV2.Green, 6f, 12f, 9f);

            return y - boxH;
        }

        /// <summary>How good a man is at one trade, in the design's stepped meter and
        /// the word that goes with it - and, in pencil under the meter, how far along
        /// he is toward the next half step.</summary>
        float BuildAttributeRow(Character member, CharacterAttribute attribute, float y)
        {
            const float textInset = 3f;
            var label = Line(cardContent, LedgerStyle.Mono, 12.5f, LedgerV2.Body,
                textInset, y, 140f - textInset, 20f,
                LedgerText.AttributeLabel(attribute));
            label.overflowMode = TextOverflowModes.Ellipsis;

            var halfSteps = member.GetHalfSteps(attribute);
            LedgerV2.Pips(cardContent, 150f, y - 10f, AttributeScale.MaxHalfSteps, halfSteps,
                LedgerV2.Red, 5f, 11f, 7f);

            Line(cardContent, LedgerStyle.Mono, 11.5f, LedgerV2.Muted, CardInner - 130f,
                y, 130f, 20f, AttributeWord(halfSteps), TextAlignmentOptions.MidlineRight);

            // Nothing is drawn for a trade he has never practised or one he has already
            // topped out at - an empty rule under every line would be eleven marks
            // saying nothing.
            var cost = Practice.NextCost(member, attribute);
            var banked = member.GetPractice(attribute);
            if (cost > 0 && banked > 0)
                Bar(cardContent, 150f, y - 17f,
                    StepBarWidth(AttributeScale.MaxHalfSteps, 5f, 7f), 3f,
                    Mathf.Clamp01(banked / (float)cost), LedgerV2.Rule);

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
            var copy = Paragraph(rect, LedgerStyle.SerifItalic, 14f, LedgerV2.PaperBlue,
                14f, -8f, CardInner - 26f, 24f, text, lineSpacing: 1f);
            copy.overflowMode = TextOverflowModes.Ellipsis;
            Caps(rect, 14f, -30f, CardInner - 26f, "IN THE MARGIN · PEN", 8.5f,
                LedgerV2.Label, 3f);
            return y - height;
        }

        /// <summary>The front's card - the BOSS's card: his face and name up top, then
        /// the desk, the guards, what sits at the front, and the stock with GIVE
        /// straight into the headquarters locker.</summary>
        float BuildFrontDetail(Roster roster)
        {
            var boss = roster.FindBoss();
            var bossName = boss != null ? boss.FullName : Gangs.GangCatalog.BossName;
            var raw = LedgerV2.PortraitPlate(cardContent, 0f, -8f, 128f, 156f, "THE BOSS");
            PortraitStudio.Request(
                PortraitStudio.FindPeoplePrefab(
                    boss != null && !string.IsNullOrEmpty(boss.Look)
                        ? boss.Look : Gangs.GangCatalog.BossModel),
                PortraitStudio.Framing.Bust, raw);

            var textX = 150f;
            var textW = CardInner - textX;
            var name = Line(cardContent, LedgerStyle.Condensed, 26f, LedgerV2.Ink,
                textX, -6f, textW, 34f, bossName);
            name.characterSpacing = 1f;
            Caps(cardContent, textX, -40f, textW,
                "BOSS · " + Gangs.GangCatalog.Names[Gangs.GangCatalog.PlayerGangId], 12f,
                LedgerV2.Red, 5f);

            var manager = roster.Find(roster.FrontId);
            var y = -70f;
            y = Particular("THE DESK", manager != null
                ? manager.FullName + " runs it"
                : "nobody runs the desk", textX, textW, y,
                manager != null ? LedgerV2.Ink : LedgerV2.Red);

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
                guards > 0 ? LedgerV2.Ink : LedgerV2.Muted);
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
                    LedgerV2.Ink, y);
                var itemId = item.Id;
                LedgerV2.Button(cardContent, "RETURN", CardInner - 100f, y + 24f, 100f, 22f, () =>
                {
                    lastRefusal = "";
                    var result = director.ReturnEquipment(itemId);
                    if (!result.Ok)
                        lastRefusal = result.Reason;
                    dirty = true;
                }, red: false, size: 10f, outline: true);
            }
            if (!anyHeld)
                y = ItemLine("The locker is empty.", "", LedgerV2.Muted, y);

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
                    LedgerV2.Ink, y);
                var itemId = item.Id;
                LedgerV2.Button(cardContent, "GIVE", CardInner - 100f, y + 24f, 100f, 22f, () =>
                {
                    lastRefusal = "";
                    var result = director.GiveEquipmentToFront(itemId);
                    if (!result.Ok)
                        lastRefusal = result.Reason;
                    dirty = true;
                }, red: false, size: 10f);
            }
            if (!anyStock)
                y = ItemLine("The stock is empty.", "", LedgerV2.Muted, y);

            if (lastRefusal.Length > 0)
                y = MarginNote(lastRefusal, y - 10f);

            return y;
        }

        /// <summary>A typed sub-heading on the card, with the design's hairline under.</summary>
        float CardHeading(string label, float y)
        {
            Caps(cardContent, 0f, y, CardInner, label, 11f, LedgerV2.Body, 5f);
            Rule(cardContent, 0f, y - 18f, CardInner, LedgerV2.Rule);
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
                var holder = Line(cardContent, LedgerStyle.Mono, 12f, LedgerV2.Muted,
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
                    LedgerV2.Ink, y);

                // Dealt gear is dealt: no button here, because there is no taking it
                // back off a crew. The word is a state the boss reads, not a verb.
                Caps(cardContent, CardInner - 100f, y + 24f, 100f, "ASSIGNED", 9.5f,
                    LedgerV2.Label, 3f, TextAlignmentOptions.MidlineRight);
            }
            if (!anyHeld)
                y = ItemLine("Nothing signed out.", "", LedgerV2.Muted, y);

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
                        LedgerV2.Ink, y);
                    if (!member.Gone)
                        LedgerV2.Button(cardContent, "GIVE", CardInner - 100f, y + 24f, 100f, 22f, () =>
                        {
                            lastRefusal = "";
                            var result = director.GiveEquipment(item.Id, member.Id);
                            if (!result.Ok)
                                lastRefusal = result.Reason;
                            dirty = true;
                        }, red: false, size: 10f);
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
                        LedgerV2.Muted, y);
                }
            }

            if (!anyStock)
                y = ItemLine("The stock is empty.", "", LedgerV2.Muted, y);

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
            Rule(cardFoot, 0f, 0f, CardInner, LedgerV2.Rule);

            if (member.Gone || member.Specialty != Specialty.None)
            {
                Caps(cardFoot, 0f, -20f, CardInner,
                    member.Gone ? "off the books · nothing left to decide"
                        : "bought talent · neither promoted nor posted",
                    10f, LedgerV2.Label, 3f, TextAlignmentOptions.Center);
                return;
            }

            if (member.Rank == Rank.Boss)
            {
                Caps(cardFoot, 0f, -20f, CardInner,
                    "root command · managed in the organization file",
                    10f, LedgerV2.Label, 3f, TextAlignmentOptions.Center);
                return;
            }

            const float buttonH = 34f;
            var half = (CardInner - 12f) * 0.5f;

            if (pendingConfirm == Confirm.Promote)
            {
                Caps(cardFoot, 0f, -8f, CardInner,
                    LedgerText.PromoteWarning(member.FullName), 9.5f, LedgerV2.Red, 2f);
                LedgerV2.Button(cardFoot, "PROMOTE ANYWAY", 0f, -22f, half, buttonH,
                    () => DoPromote(member.Id), red: true);
                LedgerV2.Button(cardFoot, "NEVER MIND", half + 12f, -22f, half, buttonH, () =>
                {
                    pendingConfirm = Confirm.None;
                    dirty = true;
                }, red: false, outline: true);
                return;
            }

            if (pendingConfirm == Confirm.Demote)
            {
                var crew = roster.CrewOf(member.Id);
                Caps(cardFoot, 0f, -8f, CardInner,
                    LedgerText.DemoteConfirm(member.FirstName,
                        crew != null ? crew.HoodIds.Count : 0), 9.5f, LedgerV2.Red, 2f);
                LedgerV2.Button(cardFoot, "DISBAND HIS CREW", 0f, -22f, half, buttonH, () =>
                {
                    pendingConfirm = Confirm.None;
                    var result = director.Demote(member.Id);
                    lastRefusal = result.Ok ? "" : result.Reason;
                    dirty = true;
                }, red: true);
                LedgerV2.Button(cardFoot, "NEVER MIND", half + 12f, -22f, half, buttonH, () =>
                {
                    pendingConfirm = Confirm.None;
                    dirty = true;
                }, red: false, outline: true);
                return;
            }

            if (member.Rank == Rank.Lieutenant)
            {
                LedgerV2.Button(cardFoot, "OFF THE BOOKS", 0f, -14f, half, buttonH, () =>
                {
                    pendingConfirm = Confirm.Demote;
                    dirty = true;
                }, red: true, outline: true);
                return;
            }

            // PROMOTE and OFF THE BOOKS are the file's ONLY two actions. Where a man
            // reports is the Organization file's business and this dossier only reports
            // it - keeping the two views on the same Character without duplicating authority.
            LedgerV2.Button(cardFoot, "PROMOTE", 0f, -14f, half, buttonH, () =>
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
