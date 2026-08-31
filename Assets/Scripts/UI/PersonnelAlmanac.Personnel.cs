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
    /// The PERSONNEL page: the payroll printout down the left, the outfit's own state
    /// beside it, and one man's personal file down the right - three columns of the
    /// same sheet.
    ///
    /// The printout is what a line printer put out this morning: a numbered column of
    /// men under their crew's band, with what each carries, how he is, how far he can
    /// be trusted, where he stands and what he costs - and the day's payroll totalled
    /// at the foot. The roll is held to a READABLE MEASURE and never stretched: a
    /// column of type as wide as a billboard is a column nobody reads down, so a wider
    /// window widens the outfit column beside it instead.
    ///
    /// The personal file is the dossier: a clipped mug shot, the particulars, the kit
    /// he has signed for, the trades he is rated in, and the two verbs a boss actually
    /// has. It is never empty - with nobody picked it stands open at the front, which
    /// is the boss's own card.
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

        // ---- the three columns ----
        //
        // The roll, the outfit, the file. Only the middle one stretches: the roll is
        // held to the measure a printed column is read at, and the file to the measure
        // a dossier is read at, so the extra width an ultrawide window hands the sheet
        // goes to the one column whose lines are figures rather than sentences.

        const float PaneGap = 20f;

        /// <summary>The roll's measure. Below the floor its columns collide; above the
        /// ceiling a name and the figure beside it are half a screen apart.</summary>
        const float RollMin = 760f;
        const float RollMax = 900f;

        /// <summary>The dossier's measure - a mug shot and the particulars beside it.</summary>
        const float FileMin = 360f;
        const float FileMax = 560f;

        /// <summary>The outfit column takes what is left, between these.</summary>
        const float OutfitMin = 300f;
        const float OutfitMax = 900f;

        static float RollW = RollMin;
        static float OutfitW = OutfitMin;
        static float FileW = FileMin;

        static float RollLeft;
        static float OutfitLeft;
        static float FileLeft;

        static float PaneTop;
        static float PaneH;

        /// <summary>The page's own head, over all three columns.</summary>
        const float PersonnelHeadH = 72f;

        // ---- inside the printout ----

        const float RollPad = 10f;
        static float RollInner = RollMin - RollPad * 2f;

        /// <summary>The dark band the column heads are printed on.</summary>
        const float RollHeadH = 30f;

        /// <summary>What the payroll band takes off the foot of the roll.</summary>
        const float PayrollH = 80f;

        /// <summary>The tallest the roll's window may be. The roll SHRINKS to what is
        /// printed on it - a short outfit ends where its last man ends, with the
        /// payroll struck under him rather than a screen of blank stock between.</summary>
        static float RollBodyMax;

        /// <summary>Two lines fit in a row: the man's name with his rank after it, and
        /// under it the post he actually stands on.</summary>
        const float RowHeight = 40f;

        /// <summary>A band naming a run of lines that belong together.</summary>
        const float BandHeight = 28f;

        /// <summary>Air over a band that is not the first thing on the roll.</summary>
        const float GroupGap = 10f;

        // The printout's column grid, in roll-inner coordinates. Every figure column is
        // held to its right margin so they read straight down a roll of sixty, and
        // nothing is ever printed in the last few units - a right-aligned line with
        // letter-spacing on it loses its final glyph to the panel's edge otherwise.
        const float ColGap = 8f;
        const float IdxW = 22f;
        const float RightInset = 4f;
        const float CarryW = 124f;
        const float CondW = 86f;
        const float LoyalW = 62f;
        const float StandW = 74f;
        const float WageW = 88f;

        /// <summary>The rank word held to the right of the name column.</summary>
        const float RankTagW = 100f;

        static float ColName = IdxW + ColGap;
        static float NameW = 200f;
        static float ColCarrying;
        static float ColCondition;
        static float ColLoyalty;
        static float ColStanding;
        static float ColWage;

        /// <summary>How far a hood's line is set in from his lieutenant's.</summary>
        const float HoodIndent = 22f;

        // ---- inside the personal file ----

        const float FilePad = 16f;
        static float FileInner = FileMin - FilePad * 2f;

        /// <summary>The fixed head band, and the fixed foot the verbs are pinned to;
        /// the file scrolls between them.</summary>
        const float FileHeadH = 30f;
        const float FileFootH = 56f;
        static float FileBodyH;

        // The dossier body was written against the file pane's old names. They are the
        // same measurements under the v2 names, and aliasing them here keeps one
        // authority for each rather than two numbers that can drift apart.
        static float CardPad => FilePad;
        static float CardInner => FileInner;
        static float CardHead => FileHeadH;
        static float CardFoot => FileFootH;
        static float CardBodyH => FileBodyH;

        /// <summary>
        /// The three columns and everything measured inside them. The sheet is full
        /// bleed, so the widths are struck from the live frame rather than baked at
        /// compile time - but only the middle column takes the surplus. What is left
        /// over past all three ceilings (a 5120-wide window) is split either side, so
        /// an enormous monitor puts air around the sheet instead of a name and its
        /// wage a foot apart.
        /// </summary>
        static void MeasurePersonnelLayout()
        {
            PaneTop = PageTop - PersonnelHeadH;
            PaneH = -(PageBottom - PaneTop);

            RollW = Mathf.Clamp(PageWidth * 0.42f, RollMin, RollMax);
            var rest = PageWidth - PaneGap * 2f - RollW;
            FileW = Mathf.Clamp(rest * 0.5f, FileMin, FileMax);
            OutfitW = Mathf.Clamp(rest - FileW, OutfitMin, OutfitMax);

            // A window too narrow for all three floors gives back in the order the page
            // can best afford: the outfit column first, then the file, and the roll
            // last. The roll IS the page.
            var over = RollW + OutfitW + FileW + PaneGap * 2f - PageWidth;
            if (over > 0f)
            {
                var take = Mathf.Min(over, OutfitW - 260f);
                if (take > 0f)
                {
                    OutfitW -= take;
                    over -= take;
                }
            }
            if (over > 0f)
            {
                var take = Mathf.Min(over, FileW - 320f);
                if (take > 0f)
                {
                    FileW -= take;
                    over -= take;
                }
            }
            if (over > 0f)
                RollW = Mathf.Max(600f, RollW - over);

            var total = RollW + OutfitW + FileW + PaneGap * 2f;
            RollLeft = PageLeft + Mathf.Max(0f, (PageWidth - total) * 0.5f);
            OutfitLeft = RollLeft + RollW + PaneGap;
            FileLeft = OutfitLeft + OutfitW + PaneGap;

            RollInner = RollW - RollPad * 2f;
            RollBodyMax = PaneH - RollHeadH - PayrollH;

            ColWage = RollInner - RightInset - WageW;
            ColStanding = ColWage - ColGap - StandW;
            ColLoyalty = ColStanding - ColGap - LoyalW;
            ColCondition = ColLoyalty - ColGap - CondW;
            ColCarrying = ColCondition - ColGap - CarryW;
            ColName = IdxW + ColGap;
            NameW = Mathf.Max(150f, ColCarrying - ColGap - ColName);

            FileInner = FileW - FilePad * 2f;
            FileBodyH = PaneH - FileHeadH - FileFootH;

            MeasureOutfitColumn();
        }

        /// <summary>selectedId's sentinel for "the front is selected" - the boss's
        /// card rather than a member's. Never a real Character id (those are >= 0).</summary>
        const int FrontSelection = -2;

        RectTransform personnelRoot;
        RectTransform rollPanel;
        RectTransform rollFootBand;
        RectTransform filterStrip;
        GameObject rollScaleNote;
        RectTransform listViewport;
        RectTransform listContent;
        RectTransform cardViewport;
        RectTransform cardContent;
        RectTransform cardFoot;
        TMP_Text cardFileNo;
        RectTransform hoverNote;
        TMP_Text hoverNoteText;
        GameObject sortMenu;

        ViewOptions options;

        /// <summary>Nobody picked yet means the file stands open at the FRONT - the
        /// boss's own card. An empty pane with one sentence in the middle of it is not
        /// a state this sheet has.</summary>
        int selectedId = FrontSelection;

        Confirm pendingConfirm;
        string lastRefusal = "";
        float listScroll;
        float cardScroll;

        readonly List<LedgerRow> rows = new List<LedgerRow>();

        void BuildPersonnelPage(RectTransform sheet)
        {
            var root = NewPageRoot(sheet, LedgerPage.Personnel);
            personnelRoot = root;

            LedgerV2.PageHead(root, PageLeft, PageTop, PageWidth, "PERSONNEL",
                "PAYROLL PRINTOUT · LINE PRINTER 03 · EVERY MAN DRAWS WHETHER HE " +
                "WORKS OR NOT");

            // The controls are laid into their own strip on the head's line, and that
            // strip is redrawn on every repaint - a segmented bar has to be able to
            // move its dark cell.
            filterStrip = NewRect("Filters", root);
            PlaceTopLeft(filterStrip, PageLeft, PageTop, PageWidth, 30f);

            BuildPrintout(root);
            BuildOutfitColumn(root);
            BuildPersonalFile(root);
            BuildSortMenu(root);
        }

        /// <summary>The left column: the printout. A dark band of column heads, the
        /// roll under it, and the payroll struck across its foot. The panel's height is
        /// set by the roll itself on every repaint - this lays the furniture at its
        /// tallest and RebuildList pulls it in.</summary>
        void BuildPrintout(RectTransform root)
        {
            rollPanel = LedgerV2.Card("Roll", root, RollLeft, PaneTop, RollW, PaneH);

            // ---- the column heads, on the dark band ----
            var band = NewRect("Heads", rollPanel);
            PlaceTopLeft(band, 0f, 0f, RollW, RollHeadH);
            Fill(band, LedgerV2.Head);

            var headY = -(RollHeadH - 14f) * 0.5f;
            LedgerV2.Mono(band, RollPad, headY, IdxW, "#", 9.5f, LedgerV2.HeadDim, 0f);
            LedgerV2.Mono(band, RollPad + ColName, headY, NameW, "NAME", 9.5f,
                LedgerV2.HeadInk, 10f);
            LedgerV2.Mono(band, RollPad + ColCarrying, headY, CarryW, "CARRYING", 9.5f,
                LedgerV2.HeadInk, 8f, TextAlignmentOptions.MidlineRight);
            LedgerV2.Mono(band, RollPad + ColCondition, headY, CondW, "COND.", 9.5f,
                LedgerV2.HeadInk, 8f, TextAlignmentOptions.MidlineRight);
            LedgerV2.Mono(band, RollPad + ColLoyalty, headY, LoyalW, "LOYALTY", 9.5f,
                LedgerV2.HeadInk, 4f, TextAlignmentOptions.MidlineRight);
            LedgerV2.Mono(band, RollPad + ColStanding, headY, StandW, "STANDING", 9.5f,
                LedgerV2.HeadInk, 4f, TextAlignmentOptions.MidlineRight);
            LedgerV2.Mono(band, RollPad + ColWage, headY, WageW, "WAGE", 9.5f,
                LedgerV2.HeadInk, 10f, TextAlignmentOptions.MidlineRight);

            // ---- the roll ----
            listViewport = NewRect("Roll rows", rollPanel);
            PlaceTopLeft(listViewport, RollPad, -RollHeadH, RollInner, RollBodyMax);
            listViewport.gameObject.AddComponent<RectMask2D>();

            listContent = NewRect("Rows", listViewport);
            listContent.anchorMin = new Vector2(0f, 1f);
            listContent.anchorMax = new Vector2(1f, 1f);
            listContent.pivot = new Vector2(0f, 1f);
            listContent.anchoredPosition = Vector2.zero;
            listContent.sizeDelta = new Vector2(0f, RollBodyMax);

            // ---- the foot: what the whole sheet costs a day, and why ----
            rollFootBand = NewRect("Payroll", rollPanel);
            PlaceTopLeft(rollFootBand, 0f, -(RollHeadH + RollBodyMax), RollW, PayrollH);
            Fill(rollFootBand, LedgerV2.PanelBand);
        }

        /// <summary>The right column: the personal file. A dark head band, a scrolling
        /// body, and the verbs pinned to the foot where a hand would rest.</summary>
        void BuildPersonalFile(RectTransform root)
        {
            var panel = LedgerV2.Card("File", root, FileLeft, PaneTop, FileW, PaneH);

            var band = NewRect("Head", panel);
            PlaceTopLeft(band, 0f, 0f, FileW, FileHeadH);
            Fill(band, LedgerV2.Head);
            var title = LedgerV2.Mono(band, FilePad, -(FileHeadH - 14f) * 0.5f,
                FileInner - 190f, "PERSONAL FILE", 10f, LedgerV2.HeadInk, 13f);
            title.font = LedgerStyle.MonoBold;
            cardFileNo = LedgerV2.Mono(band, FilePad + FileInner - 190f,
                -(FileHeadH - 14f) * 0.5f, 190f - RightInset, "", 9.5f, LedgerV2.HeadDim,
                4f, TextAlignmentOptions.MidlineRight);

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

        // ------------------------------------------------------------- the controls

        void ToggleSortMenu()
        {
            if (sortMenu)
                sortMenu.SetActive(!sortMenu.activeSelf);
        }

        static readonly string[] ShowSegments = { "ALL", "ON THEIR FEET", "OUT OF ACTION" };

        string SortWord() => options.Sort switch
        {
            SortKey.Attribute => "SORT · " +
                LedgerText.AttributeLabel(options.SortAttribute).ToUpperInvariant(),
            SortKey.Loyalty => "SORT · LOYALTY",
            _ => "SORT · ROSTER ORDER",
        };

        /// <summary>
        /// The page's controls: one segmented bar and one menu key, held to the right
        /// of the page head. Two, and only two, because those are the only two that
        /// change what the sheet shows - the roll is GROUPED by crew, the front and the
        /// pool, so a filter that shows one group at a time only hides the shape the
        /// reader came for. Redrawn every repaint: a segmented bar's answer moves.
        /// </summary>
        void RefreshFilterTapes()
        {
            if (!filterStrip || currentPage != LedgerPage.Personnel)
                return;

            foreach (Transform old in filterStrip)
                Destroy(old.gameObject);

            const float h = 26f;
            const float y = -2f;
            const float cell = 124f;

            // Held to the right end of the two columns they govern - never to the page,
            // which would hang them over a dossier they have nothing to do with, and
            // never near the title, which they would run into on a narrow window.
            var x = RollLeft - PageLeft + RollW + PaneGap + OutfitW
                    - cell * ShowSegments.Length;
            LedgerV2.Segmented(filterStrip, x, y, h, ShowSegments,
                (int)options.Availability, pick =>
                {
                    options.Availability = (AvailabilityFilter)pick;
                    listScroll = 0f;
                    dirty = true;
                }, cell);

            var label = LedgerV2.Mono(filterStrip, x - 60f, y - 4f, 52f, "SHOW", 9.5f,
                LedgerV2.Label, 6f, TextAlignmentOptions.MidlineRight);
            label.font = LedgerStyle.MonoBold;

            const float sortW = 236f;
            var sortX = x - 60f - 14f - sortW;
            LedgerV2.Button(filterStrip, SortWord(), sortX, y, sortW, h, ToggleSortMenu,
                LedgerV2.Key.Outline, 9.5f);

            // The slip drops from the key it belongs to, and prints over the roll.
            if (sortMenu)
            {
                var slip = (RectTransform)sortMenu.transform;
                slip.anchoredPosition = new Vector2(PageLeft + sortX, PageTop - 30f);
                sortMenu.transform.SetAsLastSibling();
            }
        }

        /// <summary>A slip of card that drops from the SORT key: thirteen typed
        /// entries, the current one highlighted. Built once; it toggles rather than
        /// rebuilds because its contents never change.</summary>
        void BuildSortMenu(RectTransform root)
        {
            const float rowH = 24f;
            var entries = 2 + AttributeScale.Count;

            var slip = LedgerV2.Card("SortMenu", root, PageLeft, PageTop - 30f, 260f,
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
            foreach (Transform old in rollFootBand)
                Destroy(old.gameObject);
            if (rollScaleNote)
                Destroy(rollScaleNote);
            rollScaleNote = null;

            var roster = director.Roster;
            if (roster == null)
                return;

            RosterView.Build(roster, options, rows);

            var y = 0f;
            var indented = false;
            var first = true;
            var index = 0;

            // A crew's hoods hang off a hairline that runs from the lieutenant's line
            // down past the last of them - the group is a bracket, not an indent.
            var bracketTop = 0f;
            var bracketing = false;

            void CloseBracket(float bottom)
            {
                if (bracketing && bottom < bracketTop)
                    Block("Bracket", listContent, ColName + 6f, bracketTop, 1f,
                        bracketTop - bottom, LedgerV2.Rule);
                bracketing = false;
            }

            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                switch (row.Kind)
                {
                    case RowKind.CrewHeader:
                        CloseBracket(y);
                        indented = true;
                        if (!first)
                            y -= GroupGap;
                        BuildCrewBand(roster, row.CrewId, y);
                        y -= BandHeight;
                        break;

                    case RowKind.FrontHeader:
                    case RowKind.PoolHeader:
                    case RowKind.SpecialistHeader:
                        CloseBracket(y);
                        indented = true;
                        if (!first)
                            y -= GroupGap;
                        BuildSectionHeader(roster, row.Kind, y);
                        y -= BandHeight;
                        break;

                    case RowKind.Lieutenant:
                        BuildCharacterRow(roster, row.CharacterId, y, ++index,
                            indent: false, lieutenantRow: true);
                        y -= RowHeight;
                        bracketTop = y;
                        bracketing = true;
                        break;

                    default:
                        var pooled = roster.AssignmentOf(row.CharacterId).Kind ==
                                     AssignmentKind.Pool;
                        BuildCharacterRow(roster, row.CharacterId, y, ++index, indented,
                            poolRow: pooled);
                        y -= RowHeight;
                        break;
                }
                first = false;
            }
            CloseBracket(y);

            // A filter that answers with nothing says so. An empty window is a bug the
            // reader has to rule out first.
            if (rows.Count == 0)
            {
                var line = Caps(listContent, 0f, -40f, RollInner,
                    "— NO MAN ON THE ROLL ANSWERS TO THIS FILTER —", 11f,
                    LedgerV2.Label, 4f, TextAlignmentOptions.Center);
                line.font = LedgerStyle.Mono;
                y = -96f;
            }

            var contentH = Mathf.Max(-y, RowHeight);
            listContent.sizeDelta = new Vector2(0f, contentH);

            // The roll ends where the roll ends: the window is the shorter of what is
            // printed and what the page has room for, and the payroll band comes up to
            // meet it.
            var bodyH = Mathf.Clamp(contentH, RowHeight * 2f, RollBodyMax);
            listViewport.sizeDelta = new Vector2(RollInner, bodyH);
            rollPanel.sizeDelta = new Vector2(RollW, RollHeadH + bodyH + PayrollH);
            rollFootBand.anchoredPosition = new Vector2(0f, -(RollHeadH + bodyH));

            var maxScroll = Mathf.Max(0f, contentH - bodyH);
            listScroll = Mathf.Clamp(listScroll, 0f, maxScroll);
            listScroll = Mathf.Floor(listScroll / RowHeight) * RowHeight;
            listContent.anchoredPosition = new Vector2(0f, listScroll);

            BuildPayrollBand(roster);

            // Whatever the roll did not need, the house scale takes: the table every
            // figure in the WAGE column is struck from. It only prints when there is
            // room for the whole of it.
            var spare = PaneH - (RollHeadH + bodyH + PayrollH) - PaneGap;
            if (spare >= ScaleNoteH)
                BuildScaleNote(PaneTop - (RollHeadH + bodyH + PayrollH) - PaneGap);
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

        RectTransform Band(string title, string aside, float y, Color ground, Color ink,
            Color asideInk)
        {
            var rect = NewRect("Band", listContent);
            PlaceTopLeft(rect, 0f, y, RollInner, BandHeight);
            bandFace = Fill(rect, ground);

            var label = LedgerV2.Name(rect, ColName - 6f, -6f, RollInner * 0.55f,
                title.ToUpperInvariant(), 13f, ink);
            label.characterSpacing = 9f;

            var asideW = Mathf.Min(360f, RollInner - RollInner * 0.55f - 20f);
            LedgerV2.Mono(rect, RollInner - RightInset - asideW, -6f, asideW,
                aside.ToUpperInvariant(), 9.5f, asideInk, 4f,
                TextAlignmentOptions.MidlineRight);
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
            var aside = men == 1 ? "one man under him" : men + " men under him";
            var blocks = lieutenant != null ? BlocksUnder(lieutenant.Id) : 0;
            if (blocks > 0)
                aside += blocks == 1 ? " · one block" : " · " + blocks + " blocks";
            Band(name, aside, y, LedgerV2.Head, LedgerV2.HeadCream, LedgerV2.HeadDim);
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
                RowKind.PoolHeader => PoolAside(roster),
                _ => "bought talent · not fighters",
            };

            // The pool is the sheet's one alarm: men who eat and do not work. It gets
            // the red band and its men get a wash, so a boss never has to count them.
            var pool = kind == RowKind.PoolHeader;
            var rect = Band(title, aside, y,
                pool ? LedgerV2.Red : LedgerV2.Head,
                LedgerV2.HeadCream,
                pool ? LedgerV2.HeadCream : LedgerV2.HeadDim);

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

        /// <summary>What the pool costs, said out loud on its own band: the men in it
        /// are the page's standing complaint, and a count without the money beside it
        /// is not the complaint.</summary>
        static string PoolAside(Roster roster)
        {
            var men = 0;
            var drawn = 0;
            for (var i = 0; i < roster.Members.Count; i++)
            {
                var member = roster.Members[i];
                if (member.Gone ||
                    roster.AssignmentOf(member.Id).Kind != AssignmentKind.Pool)
                    continue;
                men++;
                drawn += Outfit.Wages.WageFor(member);
            }
            if (men == 0)
                return "nobody idle";
            return (men == 1 ? "one man" : men + " men") + " · no post · " +
                   LedgerText.Cash(drawn) + " a day";
        }

        readonly List<OrganizationBlockResponsibility> blockScratch =
            new List<OrganizationBlockResponsibility>();

        /// <summary>How many blocks a leader answers for. The organization file owns
        /// the fact; the roll only reports it.</summary>
        int BlocksUnder(int leaderId)
        {
            if (director == null || director.Organization == null)
                return 0;
            director.Organization.CollectBlockResponsibilities(leaderId, blockScratch);
            return blockScratch.Count;
        }

        /// <summary>
        /// One man's line: his number, his name with his rank after it, the post he
        /// actually stands on under it, and then the five columns that read straight
        /// down a roll of sixty - what he carries, how he is, how far he can be
        /// trusted, whether he is earning, and what he costs. The wage most of all,
        /// because payroll is the pressure the whole game turns on.
        /// </summary>
        void BuildCharacterRow(Roster roster, int id, float y, int index, bool indent,
            bool lieutenantRow = false, bool poolRow = false)
        {
            var member = roster.Find(id);
            if (member == null)
                return;

            var rect = NewRect("Row", listContent);
            PlaceTopLeft(rect, 0f, y, RollInner, RowHeight);

            var chosen = id == selectedId;
            var dead = member.Gone; // struck through: dead or deserted

            // ONE Image on the row: it is both the row's ground and the button's target
            // graphic. A second AddComponent<Image> on the same object silently answers
            // null in Unity, and a null target graphic takes the whole row down with it.
            var face = Fill(rect, chosen ? LedgerV2.Picked
                : lieutenantRow ? LedgerV2.PanelBand
                : poolRow ? LedgerV2.Wrong
                : new Color(LedgerV2.Panel.r, LedgerV2.Panel.g, LedgerV2.Panel.b, 0f));
            face.raycastTarget = true;
            Block("Row rule", rect, 0f, 0f, RollInner, 1f,
                lieutenantRow ? LedgerV2.Rule : LedgerV2.Hair);

            // The head of a crew carries his rank in the margin - a flash of the
            // lieutenant's brown down the whole line. Weight, not a word: the shape of
            // the roll has to answer "who runs this crew" before anything is read.
            if (lieutenantRow)
                Block("Rank flash", rect, 0f, 0f, 4f, RowHeight, LedgerV2.Lieutenant);

            // A row does ONE thing: it opens that man's file. The ledger reads.
            RowButton(rect, face, () => SelectMember(id));

            var ink = dead ? LedgerV2.Faint : LedgerV2.Ink;
            var nameX = ColName + (indent ? HoodIndent : 0f);
            var nameW = NameW - (indent ? HoodIndent : 0f);

            LedgerV2.Mono(rect, 0f, -8f, IdxW, index.ToString("00"), 10f, LedgerV2.Muted, 0f);

            var name = Line(rect, lieutenantRow ? LedgerStyle.MonoBold : LedgerStyle.Mono,
                lieutenantRow ? 14f : 13f, ink, nameX, -6f, nameW - RankTagW - 6f, 18f,
                member.FullName);
            name.overflowMode = TextOverflowModes.Ellipsis;

            LedgerV2.Mono(rect, nameX + nameW - RankTagW, -6f, RankTagW,
                member.Specialty != Specialty.None
                    ? LedgerText.SpecialtyLabel(member.Specialty)
                    : LedgerText.RankLabel(member.Rank),
                9.5f, dead ? LedgerV2.Faint : LedgerV2.Label, 7f,
                TextAlignmentOptions.MidlineRight);

            // The second line: where he stands. A sorted roll answers with the figure it
            // was sorted BY instead, because that is the question the reader just asked.
            LedgerV2.Mono(rect, nameX, -22f, nameW, AsideFor(roster, member), 9.5f,
                LedgerV2.Muted, 5f);

            BuildRowCells(roster, rect, member, dead);

            // The dead are struck through in pen - the record keeps their line.
            if (dead)
                Block("Struck", rect, nameX - 2f, -RowHeight * 0.5f + 1f,
                    nameW - 4f, 1.5f, LedgerV2.Red);
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

        /// <summary>
        /// What a man carries, named. The design prints the WORD, not a picture of the
        /// gun: a column of little photographs cannot be read down.
        ///
        /// A man the stock book has issued nothing to is NOT carrying nothing - every
        /// man of the outfit has the .38 in his coat, which is what his own file says
        /// two panes over. A column of dashes contradicting the dossier is a column
        /// that carries no information at all; the weight of the type carries it
        /// instead - what the outfit signed out is in ink, what he brought himself is
        /// in grey.
        /// </summary>
        string CarryingLine(Roster roster, Character member, out bool issued)
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

            issued = gun != null;
            if (issued)
                return extra > 0 ? gun + " +" + extra : gun;
            if (member.Gone)
                return "-";
            return member.Specialty != Specialty.None ? "unarmed" : "his own .38";
        }

        readonly List<RosterEquipment> carriedScratch = new List<RosterEquipment>();

        /// <summary>
        /// The clerk's line on what a man is LIKE: the traits that are not Loyalty
        /// (which has its own row) and not the unremarkable middle band. A man nobody
        /// has anything to say about reads "nothing remarkable", which is itself the
        /// most useful thing the book can say about a hood.
        ///
        /// Words, never numbers. The figures behind these are the player's to infer
        /// from what his men actually do; a column of five more digits would let him
        /// skip the whole point of the system.
        /// </summary>
        static string CharacterWords(Character member)
        {
            var words = "";
            for (var i = 0; i < Personality.All.Length; i++)
            {
                var trait = Personality.All[i];
                if (trait == PersonalityTrait.Loyalty)
                    continue;

                var value = Personality.Get(member, trait);
                if (value > 40 && value <= 60)
                    continue;

                words += (words.Length > 0 ? ", " : "") + Personality.Band(trait, value);
            }
            return words.Length > 0 ? words : "nothing remarkable";
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

        /// <summary>The five scan columns, all held to their right margins so they read
        /// straight down the roll.</summary>
        void BuildRowCells(Roster roster, RectTransform rect, Character member, bool dead)
        {
            var carrying = CarryingLine(roster, member, out var issued);
            LedgerV2.Mono(rect, ColCarrying, -6f, CarryW, carrying, 11f,
                dead ? LedgerV2.Faint : issued ? LedgerV2.Body : LedgerV2.Muted,
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
                LedgerV2.Mono(rect, ColCarrying, -22f, CarryW + ColGap + CondW, note, 9f,
                    dead ? LedgerV2.Faint : LedgerV2.Red, 0f,
                    TextAlignmentOptions.MidlineRight);

            // Loyalty is the figure that says which of these men is still yours next
            // week, so it is read down the roll rather than looked up one file at a time.
            LedgerV2.Figure(rect, ColLoyalty, -6f, LoyalW,
                dead ? "-" : member.Loyalty.ToString(), 12f,
                dead ? LedgerV2.Faint
                : member.Loyalty < 35 ? LedgerV2.Red
                : member.Loyalty < 55 ? LedgerV2.Amber : LedgerV2.Ink);

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

        // ------------------------------------------------------------- the payroll

        /// <summary>
        /// The payroll band, struck across the foot of the roll and pinned to the last
        /// man on it. The total is the figure the whole game turns on; the line under it
        /// is where the money actually goes, because "$1,120 a day" is a number and
        /// "$180 of it to men with no post" is an argument.
        /// </summary>
        void BuildPayrollBand(Roster roster)
        {
            Block("Foot rule", rollFootBand, 0f, 0f, RollW, 3f, LedgerV2.Ink);

            var title = LedgerV2.Name(rollFootBand, RollPad, -10f, 300f,
                "PAYROLL · RUNNING", 15f);
            title.characterSpacing = 5f;

            var total = Outfit.Wages.DailyPayroll(roster);
            LedgerV2.Figure(rollFootBand, RollPad + RollInner - 240f, -12f,
                240f - RightInset, LedgerText.Cash(total) + " / day", 22f, LedgerV2.Ink);

            LedgerV2.Mono(rollFootBand, RollPad, -36f, RollInner - 250f,
                PayrollSplit(roster), 9.5f, LedgerV2.Label, 1f);

            LedgerV2.Mono(rollFootBand, RollPad, -56f, RollInner - 250f,
                "the jailed and the hurt keep drawing · only the dead come off", 10.5f,
                LedgerV2.Muted, 1f);
        }

        /// <summary>Where the day's payroll goes, by group. Buckets that hold nothing
        /// are left off the line rather than printed as zeros.</summary>
        static string PayrollSplit(Roster roster)
        {
            var crews = 0;
            var front = 0;
            var pool = 0;
            var bought = 0;
            for (var i = 0; i < roster.Members.Count; i++)
            {
                var member = roster.Members[i];
                var wage = Outfit.Wages.WageFor(member);
                if (wage == 0)
                    continue;
                switch (roster.AssignmentOf(member.Id).Kind)
                {
                    case AssignmentKind.Crew:
                        crews += wage;
                        break;
                    case AssignmentKind.Front:
                        front += wage;
                        break;
                    case AssignmentKind.Specialist:
                        bought += wage;
                        break;
                    default:
                        pool += wage;
                        break;
                }
            }

            var line = "";
            if (crews > 0)
                line += "CREWS " + LedgerText.Cash(crews);
            if (front > 0)
                line += (line.Length > 0 ? "  ·  " : "") + "THE DESK " + LedgerText.Cash(front);
            if (pool > 0)
                line += (line.Length > 0 ? "  ·  " : "") + "THE POOL " + LedgerText.Cash(pool);
            if (bought > 0)
                line += (line.Length > 0 ? "  ·  " : "") + "RETAINERS " + LedgerText.Cash(bought);
            return line.Length > 0 ? line : "NOBODY DRAWS A WAGE";
        }

        /// <summary>What the house scale takes off the bottom of the column.</summary>
        const float ScaleNoteH = 170f;

        /// <summary>
        /// The table every figure in the WAGE column is struck from, printed under the
        /// roll when the roll is short enough to leave room for it. It is the same
        /// Wages table the game pays from - a footnote, never a second set of numbers.
        /// </summary>
        void BuildScaleNote(float top)
        {
            var panel = LedgerV2.Card("House scale", personnelRoot, RollLeft, top, RollW,
                ScaleNoteH);
            rollScaleNote = panel.gameObject;

            LedgerV2.CardHead(panel, RollW, "THE HOUSE SCALE", "WHAT A DAY COSTS");

            var y = -RollHeadH - 10f;
            y = ScaleLine(panel, "HOOD", LedgerText.Cash(Outfit.Wages.HoodBase) +
                " a day, and " + LedgerText.Cash(Outfit.Wages.HoodPerHalfStep) +
                " more for every half-step of talent he has", y);
            y = ScaleLine(panel, "LIEUTENANT", LedgerText.Cash(Outfit.Wages.LieutenantWage) +
                " a day, flat - the house raised him", y);
            y = ScaleLine(panel, "ACCOUNTANT", LedgerText.Cash(Outfit.Wages.AccountantWage) +
                " a day on retainer", y);
            y = ScaleLine(panel, "LAWYER", LedgerText.Cash(Outfit.Wages.LawyerWage) +
                " a day on retainer", y);
            ScaleLine(panel, "OFF THE COLUMN", "whatever he advertised, and " +
                Outfit.Wages.DaysDown + " days of it down before he starts", y);
        }

        float ScaleLine(RectTransform panel, string label, string value, float y)
        {
            LedgerV2.Mono(panel, RollPad, y, 150f, label, 9.5f, LedgerV2.Label, 5f);
            var text = LedgerV2.Mono(panel, RollPad + 158f, y,
                RollInner - 158f - RightInset, value, 11f, LedgerV2.Body, 1f);
            text.overflowMode = TextOverflowModes.Ellipsis;
            LedgerV2.Leader(panel, RollPad, y - 17f, RollInner - RightInset);
            return y - 22f;
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
            if (roster == null)
            {
                if (cardFileNo)
                    cardFileNo.text = "";
                CloseCard(-CardBodyH);
                return;
            }

            // Nobody picked, or a man who has left the books since he was: the file
            // falls back to the FRONT - the boss's own card. There is no empty state.
            var member = selectedId >= 0 ? roster.Find(selectedId) : null;
            if (member == null)
            {
                if (cardFileNo)
                    cardFileNo.text = "F-0001 · THE FRONT";
                CloseCard(BuildFrontDetail(roster));
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
            // What he is LIKE, in words. The numbers behind these are never shown: the
            // player is meant to learn a man's character from what the man does, and a
            // column of five more figures would let him skip that entirely.
            y = Particular("CHARACTER", CharacterWords(member), textX, textW, y);

            // The stamps: the law's word over the photograph, WANTED beside the name.
            // (CharacterWords is the clerk's line on him - see below.)
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
            var outcomeW = Mathf.Min(168f, CardInner * 0.42f);
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
            var labelW = Mathf.Min(130f, w * 0.42f);
            LedgerV2.Mono(cardContent, x, y, labelW, label, 10.5f, LedgerV2.Label, 6f);
            var text = LedgerV2.Figure(cardContent, x + labelW + 4f, y,
                w - labelW - 4f, value, 12.5f, ink ?? LedgerV2.Ink);
            text.overflowMode = TextOverflowModes.Ellipsis;
            LedgerV2.Leader(cardContent, x, y - 19f, w);
            return y - 24f;
        }

        /// <summary>
        /// The kit slots: GUN, MOTOR, VEST across the card. A slot he has something in
        /// is a solid box with the thing named in it; a slot he has nothing in is a
        /// hairline box with a dash in it - the design's own way of saying that an empty
        /// slot is a fact about the man and not an absence of interface.
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
            var meterX = Mathf.Min(150f, CardInner * 0.42f);
            var wordW = Mathf.Min(130f, CardInner * 0.32f);

            var label = Line(cardContent, LedgerStyle.Mono, 12.5f, LedgerV2.Body,
                textInset, y, meterX - textInset - 6f, 20f,
                LedgerText.AttributeLabel(attribute));
            label.overflowMode = TextOverflowModes.Ellipsis;

            var halfSteps = member.GetHalfSteps(attribute);
            LedgerV2.Pips(cardContent, meterX, y - 10f, AttributeScale.MaxHalfSteps,
                halfSteps, LedgerV2.Red, 5f, 11f, 7f);

            Line(cardContent, LedgerStyle.Mono, 11.5f, LedgerV2.Muted, CardInner - wordW,
                y, wordW, 20f, AttributeWord(halfSteps), TextAlignmentOptions.MidlineRight);

            // Nothing is drawn for a trade he has never practised or one he has already
            // topped out at - an empty rule under every line would be eleven marks
            // saying nothing.
            var cost = Practice.NextCost(member, attribute);
            var banked = member.GetPractice(attribute);
            if (cost > 0 && banked > 0)
                Bar(cardContent, meterX, y - 17f,
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
        /// straight into the headquarters locker. This is also what the file stands
        /// open at before anybody has been picked.</summary>
        float BuildFrontDetail(Roster roster)
        {
            var boss = roster.FindBoss();
            var bossName = boss != null ? boss.FullName : Gangs.GangCatalog.BossName;
            var plateW = Mathf.Min(128f, CardInner * 0.4f);
            var raw = LedgerV2.PortraitPlate(cardContent, 0f, -8f, plateW,
                plateW * 1.22f, "THE BOSS");
            PortraitStudio.Request(
                PortraitStudio.FindPeoplePrefab(
                    boss != null && !string.IsNullOrEmpty(boss.Look)
                        ? boss.Look : Gangs.GangCatalog.BossModel),
                PortraitStudio.Framing.Bust, raw);

            var textX = plateW + 22f;
            var textW = CardInner - textX;
            var name = Line(cardContent, LedgerStyle.Condensed, 26f, LedgerV2.Ink,
                textX, -6f, textW, 34f, bossName);
            name.characterSpacing = 1f;
            name.overflowMode = TextOverflowModes.Ellipsis;
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

            y = Mathf.Min(y, -(plateW * 1.22f + 40f)) - 10f;

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
                var holderW = Mathf.Min(210f, CardInner * 0.5f);
                var holder = Line(cardContent, LedgerStyle.Mono, 12f, LedgerV2.Muted,
                    CardInner - 110f - holderW, y, holderW, 22f, who,
                    TextAlignmentOptions.MidlineRight);
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
