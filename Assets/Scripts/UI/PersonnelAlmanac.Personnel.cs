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

        // ---- the two panes ----
        //
        // The roll and the file, and NEITHER of them stretches: a printed column is
        // read at the measure it was set at, and a dossier at the measure a dossier is
        // read at. What a wide window hands the sheet past both ceilings is left as
        // air either side rather than spent pulling a name and its wage apart.

        const float PaneGap = 20f;

        /// <summary>The roll's measure. Below the floor its columns collide; above the
        /// ceiling a name and the figure beside it are half a screen apart.</summary>
        const float RollMin = 760f;
        const float RollMax = 900f;

        /// <summary>The dossier's measure - a mug shot and the particulars beside it.</summary>
        const float FileMin = 360f;
        const float FileMax = 640f;

        static float RollW = RollMin;
        static float FileW = FileMin;

        static float RollLeft;
        static float FileLeft;

        static float PaneTop;
        static float PaneH;

        /// <summary>The page's own head, over both panes.</summary>
        const float PersonnelHeadH = 72f;

        // ---- inside the printout ----

        const float RollPad = 14f;
        static float RollInner = RollMin - RollPad * 2f;

        /// <summary>The dark band the column heads are printed on.</summary>
        const float RollHeadH = 37f;

        /// <summary>What the payroll band takes off the foot of the roll.</summary>
        const float PayrollH = 92f;

        /// <summary>The tallest the roll's window may be. The roll SHRINKS to what is
        /// printed on it - a short outfit ends where its last man ends, with the
        /// payroll struck under him rather than a screen of blank stock between.</summary>
        static float RollBodyMax;

        /// <summary>Two lines fit in a row: the man's name, and under it what he is
        /// to the outfit. Generous on purpose - this is the sheet a boss reads down,
        /// not a table he audits.</summary>
        const float RowHeight = 55f;

        /// <summary>A band naming a run of lines that belong together. Set like a row,
        /// because it IS one - the roll is a single printout.</summary>
        const float BandHeight = 53f;

        // The printout's column grid, in roll-inner coordinates. Every figure column is
        // held to its right margin so they read straight down a roll of sixty, and
        // nothing is ever printed in the last few units - a right-aligned line with
        // letter-spacing on it loses its final glyph to the panel's edge otherwise.
        const float ColGap = 8f;
        const float IdxW = 24f;
        const float RightInset = 4f;
        const float CarryW = 132f;
        const float CondW = 54f;
        const float StandW = 68f;
        const float WageW = 72f;

        static float ColName = IdxW + ColGap;
        static float NameW = 200f;
        static float ColCarrying;
        static float ColCondition;
        static float ColStanding;
        static float ColWage;

        // ---- inside the personal file ----

        const float FilePad = 16f;
        static float FileInner = FileMin - FilePad * 2f;

        /// <summary>The fixed head band, and the fixed foot the verbs are pinned to;
        /// the file scrolls between them.</summary>
        const float FileHeadH = 30f;
        const float FileFootH = 56f;
        /// <summary>The tallest the dossier's window may be. Like the roll, the file
        /// is only as long as what is printed on it - the panel is pulled in to the
        /// card it is showing, and a short card ends where it ends.</summary>
        static float FileBodyMax;

        /// <summary>What the dossier's window is RIGHT NOW - set by the card last laid
        /// on it.</summary>
        float fileBodyH;

        // The dossier body was written against the file pane's old names. They are the
        // same measurements under the v2 names, and aliasing them here keeps one
        // authority for each rather than two numbers that can drift apart.
        static float CardPad => FilePad;
        static float CardInner => FileInner;
        static float CardHead => FileHeadH;
        static float CardFoot => FileFootH;
        float CardBodyH => fileBodyH;

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

            RollW = Mathf.Clamp(PageWidth * 0.62f, RollMin, RollMax);
            FileW = Mathf.Clamp(PageWidth - PaneGap - RollW, FileMin, FileMax);

            // A window too narrow for both floors gives back off the file first: the
            // roll IS the page.
            var over = RollW + FileW + PaneGap - PageWidth;
            if (over > 0f)
                RollW = Mathf.Max(600f, RollW - over);

            var total = RollW + FileW + PaneGap;
            RollLeft = PageLeft + Mathf.Max(0f, (PageWidth - total) * 0.5f);
            FileLeft = RollLeft + RollW + PaneGap;

            RollInner = RollW - RollPad * 2f;
            RollBodyMax = PaneH - RollHeadH - PayrollH;

            ColWage = RollInner - RightInset - WageW;
            ColStanding = ColWage - ColGap - StandW;
            ColCondition = ColStanding - ColGap - CondW;
            ColCarrying = ColCondition - ColGap - CarryW;
            ColName = IdxW + ColGap;
            NameW = Mathf.Max(150f, ColCarrying - ColGap - ColName);

            FileInner = FileW - FilePad * 2f;
            FileBodyMax = PaneH - FileHeadH - FileFootH;
        }

        /// <summary>selectedId's sentinel for "the front is selected" - the boss's
        /// card rather than a member's. Never a real Character id (those are >= 0).</summary>
        const int FrontSelection = -2;

        RectTransform personnelRoot;
        RectTransform rollPanel;
        RectTransform rollFootBand;
        RectTransform filterStrip;
        RectTransform listViewport;
        RectTransform listContent;
        RectTransform filePanel;
        RectTransform cardViewport;
        RectTransform cardContent;
        RectTransform cardFoot;
        TMP_Text cardFileNo;
        RectTransform hoverNote;
        TMP_Text hoverNoteText;
        GameObject sortMenu;

        /// <summary>
        /// The roll opens on NOTABILITY, not on the roster's own order (NOTE-001): the
        /// men something has happened to stand at the top of their group and the corner
        /// boy waits at the bottom until he earns a look. Roster order is still one key
        /// away on the SORT slip - it is the order the book is FILED in, and the reader
        /// wants it back the moment he is looking for a particular man rather than for
        /// news.
        /// </summary>
        ViewOptions options = new ViewOptions { Sort = SortKey.Notability };

        /// <summary>Today's notability figures, or null in a scene with no campaign
        /// running behind the book. Read, never written - the runner owns it and
        /// rebuilds it at the day tick.</summary>
        NotabilityBoard Board => outfit ? outfit.Runner.Notability : null;

        /// <summary>Today, on the campaign's calendar - the day every fade on this
        /// page is measured from. Day one in a scene with no campaign, which is the
        /// day everything that has ever happened in it happened on.</summary>
        int Today => outfit ? outfit.Campaign.Day : 1;

        /// <summary>Nobody picked. The file still stands open at the FRONT - the
        /// boss's own card, because an empty pane with one sentence in the middle of it
        /// is not a state this sheet has - but no line on the roll is marked for it:
        /// nothing has been picked yet, and the roll must not say otherwise.</summary>
        int selectedId = -1;

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
            LedgerV2.Mono(band, RollPad, headY, IdxW, "#", 10f, LedgerV2.HeadDim, 0f);
            LedgerV2.Mono(band, RollPad + ColName, headY, NameW, "NAME", 10f,
                LedgerV2.HeadInk, 10f);
            LedgerV2.Mono(band, RollPad + ColCarrying, headY, CarryW, "CARRYING", 10f,
                LedgerV2.HeadInk, 8f, TextAlignmentOptions.MidlineRight);
            LedgerV2.Mono(band, RollPad + ColCondition, headY, CondW, "COND.", 10f,
                LedgerV2.HeadInk, 8f, TextAlignmentOptions.MidlineRight);
            LedgerV2.Mono(band, RollPad + ColStanding, headY, StandW, "STANDING", 10f,
                LedgerV2.HeadInk, 4f, TextAlignmentOptions.MidlineRight);
            LedgerV2.Mono(band, RollPad + ColWage, headY, WageW, "WAGE", 10f,
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

            filePanel = panel;
            fileBodyH = FileBodyMax;

            cardViewport = NewRect("Body", panel);
            PlaceTopLeft(cardViewport, FilePad, -FileHeadH, FileInner, FileBodyMax);
            cardViewport.gameObject.AddComponent<RectMask2D>();

            cardContent = NewRect("Content", cardViewport);
            cardContent.anchorMin = new Vector2(0f, 1f);
            cardContent.anchorMax = new Vector2(1f, 1f);
            cardContent.pivot = new Vector2(0f, 1f);
            cardContent.anchoredPosition = Vector2.zero;
            cardContent.sizeDelta = new Vector2(0f, FileBodyMax);

            cardFoot = NewRect("Foot", panel);
            PlaceTopLeft(cardFoot, FilePad, -(FileHeadH + FileBodyMax), FileInner, FileFootH);

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
            SortKey.Notability => "SORT · WHAT HAPPENED",
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

            // Held to the right end of the sheet's own two panes - never to the page
            // edge, which on a wide window is out in the air past them.
            var x = FileLeft - PageLeft + FileW - cell * ShowSegments.Length;
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

        /// <summary>A slip of card that drops from the SORT key: fourteen typed
        /// entries, the current one highlighted. Built once; it toggles rather than
        /// rebuilds because its contents never change. WHAT HAPPENED heads the slip
        /// because it is the sheet's own default; ROSTER ORDER sits under it.</summary>
        void BuildSortMenu(RectTransform root)
        {
            const float rowH = 24f;
            var entries = 3 + AttributeScale.Count;

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
                    label = "WHAT HAPPENED";
                else if (i == 1)
                    label = "ROSTER ORDER";
                else if (i <= AttributeScale.Count + 1)
                    label = LedgerText.AttributeLabel((CharacterAttribute)(i - 2))
                        .ToUpperInvariant();
                else
                    label = "LOYALTY";

                var row = NewRect("Entry", slip);
                PlaceTopLeft(row, 6f, -6f - i * rowH, 248f, rowH);
                var surface = ClickSurface(row);
                RowButton(row, surface, () =>
                {
                    if (index == 0)
                        options.Sort = SortKey.Notability;
                    else if (index == 1)
                        options.Sort = SortKey.Roster;
                    else if (index <= AttributeScale.Count + 1)
                    {
                        options.Sort = SortKey.Attribute;
                        options.SortAttribute = (CharacterAttribute)(index - 2);
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

            var roster = director.Roster;
            if (roster == null)
                return;

            // Today's figures, handed to the view rather than read by it: the sim owns
            // the board and rebuilds it at the day tick, and a page with no campaign
            // behind it (every demo scene) hands null and gets roster order.
            options.Board = Board;
            RosterView.Build(roster, options, rows);

            var y = 0f;
            var index = 0;

            // A crew's header IS its lieutenant's line - bold, on its own ground, with
            // the count of the men under him where a post would go. Only when he is
            // filtered off the roll do his men need a band naming the crew they are
            // standing in, and that band is the only thing this remembers.
            var pendingCrew = -1;

            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                switch (row.Kind)
                {
                    case RowKind.CrewHeader:
                        pendingCrew = row.CrewId;
                        break;

                    case RowKind.FrontHeader:
                    case RowKind.PoolHeader:
                    case RowKind.SpecialistHeader:
                        pendingCrew = -1;
                        BuildSectionBand(roster, row.Kind, y);
                        y -= BandHeight;
                        break;

                    case RowKind.Lieutenant:
                        pendingCrew = -1;
                        BuildCharacterRow(roster, row.CharacterId, y, ++index,
                            lieutenantRow: true);
                        y -= RowHeight;
                        break;

                    default:
                        if (pendingCrew >= 0)
                        {
                            BuildOrphanBand(roster, pendingCrew, y);
                            y -= BandHeight;
                            pendingCrew = -1;
                        }
                        var pooled = roster.AssignmentOf(row.CharacterId).Kind ==
                                     AssignmentKind.Pool;
                        BuildCharacterRow(roster, row.CharacterId, y, ++index,
                            poolRow: pooled);
                        y -= RowHeight;
                        break;
                }
            }

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
        }

        /// <summary>The ground of the band just built - it is also the button's target
        /// graphic when the band happens to be one.</summary>
        Image bandFace;

        /// <summary>
        /// A band across the roll naming a run of lines that belong together - the
        /// front, the pool, the bought talent. It is set like a row and not like a
        /// heading: the same ground, the name where a man's name would be and the
        /// particular under it where his post would be. The roll is one printout, not
        /// a stack of sections with black rules between them.
        /// </summary>
        RectTransform Band(string title, string sub, float y)
        {
            var rect = NewRect("Band", listContent);
            PlaceTopLeft(rect, 0f, y, RollInner, BandHeight);
            bandFace = Fill(rect,
                new Color(LedgerV2.Panel.r, LedgerV2.Panel.g, LedgerV2.Panel.b, 0f));
            Block("Band rule", rect, 0f, 0f, RollInner, 2f, LedgerV2.Ink);

            var label = Line(rect, LedgerStyle.MonoBold, 16f, LedgerV2.Ink, ColName,
                -10f, RollInner - ColName - RightInset, 22f, title.ToUpperInvariant());
            label.characterSpacing = 4f;
            Caps(rect, ColName, -32f, RollInner - ColName - RightInset,
                sub.ToUpperInvariant(), 9.5f, LedgerV2.Label, 4f);
            return rect;
        }

        /// <summary>The band a crew's men get when their own lieutenant has been
        /// filtered off the roll - otherwise they would stand under somebody else's
        /// name.</summary>
        void BuildOrphanBand(Roster roster, int crewId, float y)
        {
            var crew = roster.FindCrew(crewId);
            var lieutenant = crew != null ? roster.Find(crew.LieutenantId) : null;
            Band(lieutenant != null
                    ? LedgerText.CrewName(lieutenant.Surname)
                    : "A CREW",
                lieutenant != null
                    ? lieutenant.FullName + " is not on this roll"
                    : "no lieutenant", y);
        }

        void BuildSectionBand(Roster roster, RowKind kind, float y)
        {
            var title = kind switch
            {
                RowKind.FrontHeader => "THE FRONT",
                RowKind.PoolHeader => "THE POOL",
                _ => "SPECIALISTS",
            };
            var sub = kind switch
            {
                RowKind.FrontHeader => FrontAside(roster),
                RowKind.PoolHeader => PoolAside(roster),
                _ => "bought talent · not fighters",
            };

            var rect = Band(title, sub, y);

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

        /// <summary>What the pool costs, said on its own band: the men in it are the
        /// page's standing complaint, and a count without the money beside it is not
        /// the complaint.</summary>
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
                return "unassigned · earning nothing";
            return "unassigned · earning nothing · " + LedgerText.Cash(drawn) + " a day";
        }

        /// <summary>
        /// One man's line: his number, his name, the post he actually stands on under
        /// it, and then the four columns that read straight down a roll of sixty -
        /// what he carries, how he is, whether he is earning, and what he costs. The
        /// wage most of all, because payroll is the pressure the whole game turns on.
        ///
        /// A lieutenant's line is the same line, weighted: bolder name, a rule over
        /// it, and the count of his men where his post would be. The ground is the
        /// same as any hood's - no dark banner, no indent, no bracket.
        /// </summary>
        void BuildCharacterRow(Roster roster, int id, float y, int index,
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
                : new Color(LedgerV2.Panel.r, LedgerV2.Panel.g, LedgerV2.Panel.b, 0f));
            face.raycastTarget = true;
            Block("Row rule", rect, 0f, 0f, RollInner, 1f, LedgerV2.Hair);

            // The head of a crew opens with a rule of his own - the one mark on the
            // roll that says a new crew starts here.
            if (lieutenantRow)
                Block("Crew rule", rect, 0f, 0f, RollInner, 2f, LedgerV2.Ink);

            // A row does ONE thing: it opens that man's file. The ledger reads.
            RowButton(rect, face, () => SelectMember(id));

            var ink = dead ? LedgerV2.Faint : LedgerV2.Ink;

            LedgerV2.Mono(rect, 0f, -12f, IdxW, index.ToString("00"), 10.5f,
                LedgerV2.Muted, 0f);

            var name = Line(rect, lieutenantRow ? LedgerStyle.MonoBold : LedgerStyle.Mono,
                16f, ink, ColName, -10f, NameW, 22f, member.FullName);
            name.overflowMode = TextOverflowModes.Ellipsis;

            // The second line: what he is to the outfit. A lieutenant answers with the
            // men under him; everybody else with the post he stands on - and a sorted
            // roll answers with the figure it was sorted BY, because that is the
            // question the reader just asked.
            // What happened to him beats everything else the line could say; failing
            // that a lieutenant answers with his men, and everybody else with the post
            // he stands on or the figure the roll was sorted by.
            var aside = NewsAside(member);
            if (aside.Length == 0)
                aside = lieutenantRow && (options.Sort == SortKey.Roster ||
                                          options.Sort == SortKey.Notability)
                    ? MenUnderLine(roster, member)
                    : AsideFor(roster, member);
            // The marks stand at the right of the name column, on the aside's own
            // line, and the aside is measured to what is left - never over the top of
            // it. A man with nothing said about him gives the whole width back.
            var marks = ManFlags.Of(member);
            var asideW = marks == ManFlag.None
                ? NameW
                : Mathf.Max(90f, NameW - FlagW - ColGap);
            var asideText = LedgerV2.Mono(rect, ColName, -32f, asideW, aside, 10f,
                lieutenantRow ? LedgerV2.Label : LedgerV2.Muted,
                lieutenantRow ? 5f : 1f);
            asideText.overflowMode = TextOverflowModes.Ellipsis;
            if (lieutenantRow)
                asideText.text = aside.ToUpperInvariant();
            if (marks != ManFlag.None && !dead)
                BuildRowMarks(rect, marks);

            // NOTE-001's tick: something happened to him inside the week. A rule in the
            // gutter rather than a word - it is a "look here", and it expires on its
            // own as the days pass under it.
            if (!dead && Board != null && Board.Fresh(id))
                Block("New", rect, IdxW - 4f, -12f, 3f, 18f, LedgerV2.Red);

            BuildRowCells(roster, rect, member, dead);

            // The dead are struck through in pen - the record keeps their line.
            if (dead)
                Block("Struck", rect, ColName - 2f, -26f, NameW - 4f, 1.5f, LedgerV2.Red);
        }

        /// <summary>The room the ledger's three marks are set in, at the right end of
        /// the name column. MEASURED against the longest of them - "LT · GUN · !" at
        /// 9.5pt mono, letter-spaced - and the aside beside it is cut to whatever is
        /// left, so neither can ever print over the other.</summary>
        const float FlagW = 92f;

        /// <summary>
        /// LOY-004's marks, as the roll prints them: what he could be, and what he is a
        /// danger of. The red flag takes the whole line's ink when it is up - it is the
        /// one of the three the reader must not scan past - and the other two share the
        /// lieutenant's blue.
        ///
        /// A mark is a STATEMENT and never an action: nothing on this page or under it
        /// behaves differently because a man carries one.
        /// </summary>
        void BuildRowMarks(RectTransform rect, ManFlag marks)
        {
            var line = "";
            for (var i = 0; i < ManFlags.All.Length; i++)
            {
                if ((marks & ManFlags.All[i]) == 0)
                    continue;
                if (line.Length > 0)
                    line += " · ";
                line += ManFlags.Mark(ManFlags.All[i]);
            }

            var mark = LedgerV2.Mono(rect, ColName + NameW - FlagW, -32f, FlagW, line,
                9.5f,
                (marks & ManFlag.RedFlag) != 0 ? LedgerV2.Red : LedgerV2.Lieutenant,
                2f, TextAlignmentOptions.MidlineRight);
            mark.font = LedgerStyle.MonoBold;
        }

        /// <summary>
        /// The last thing that happened to him, for the roll that is sorted by exactly
        /// that - and only while it is still this week's news. Empty otherwise: a man
        /// nothing has happened to answers with his post as he always did, because
        /// "nothing happened" is not a line worth printing sixty times down a page.
        /// </summary>
        string NewsAside(Character member)
        {
            if (options.Sort != SortKey.Notability || Board == null ||
                !Board.Fresh(member.Id) || member.Career.Count == 0)
                return "";
            return member.Career[member.Career.Count - 1].Line;
        }

        /// <summary>How many men stand under a lieutenant - the line his own row
        /// carries where another man carries his post.</summary>
        static string MenUnderLine(Roster roster, Character member)
        {
            var crew = roster.CrewOf(member.Id);
            var men = crew != null ? crew.HoodIds.Count : 0;
            return men == 0 ? "no men under him"
                : men == 1 ? "one man under him"
                : men + " men under him";
        }

        /// <summary>The line under a man's name: what the roll was sorted by when it was
        /// sorted, and otherwise the post he stands on. The crew he is in is NOT named:
        /// he is printed under his lieutenant, which says it already.</summary>
        string AsideFor(Roster roster, Character member)
        {
            if (options.Sort == SortKey.Loyalty)
                return "loyalty " + member.Loyalty;
            if (options.Sort == SortKey.Attribute)
                return LedgerText.AttributeLabel(options.SortAttribute).ToLowerInvariant() +
                       " " + LedgerText.Stars(member.GetHalfSteps(options.SortAttribute));

            var post = roster.AssignmentOf(member.Id);
            switch (post.Kind)
            {
                case AssignmentKind.Pool:
                    return "no post";
                case AssignmentKind.Front:
                    return "runs the front";
                case AssignmentKind.Specialist:
                    return "on retainer";
                case AssignmentKind.Boss:
                    return "runs the outfit";
            }

            var crew = roster.FindCrew(post.CrewId);
            var lieutenant = crew != null ? roster.Find(crew.LieutenantId) : null;
            return lieutenant != null ? "under " + lieutenant.Surname : "in a crew";
        }

        /// <summary>
        /// What a man carries, named. The design prints the WORD, not a picture of the
        /// gun: a column of little photographs cannot be read down.
        ///
        /// A man the stock book has issued nothing to is NOT carrying nothing - every
        /// man of the outfit has the .38 in his coat, which is what his own file says
        /// in the pane opposite. The weight of the type carries the difference: what
        /// the outfit signed out is in ink, what he brought himself is in grey.
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
                return "—";
            return member.Specialty != Specialty.None ? "—" : "his own .38";
        }

        readonly List<RosterEquipment> carriedScratch = new List<RosterEquipment>();

        /// <summary>How a man is, in the word a clerk would type.</summary>
        static string ConditionWord(CharacterStatus status) => status switch
        {
            CharacterStatus.Active => "FIT",
            CharacterStatus.Hospitalized => "HURT",
            CharacterStatus.Jailed => "JAILED",
            CharacterStatus.Dead => "DEAD",
            _ => "GONE",
        };

        /// <summary>Hurt is a man who comes back; jailed and dead are not. The column
        /// is read down, so the three have to be told apart by colour before they are
        /// read.</summary>
        static Color ConditionInk(CharacterStatus status) => status switch
        {
            CharacterStatus.Active => LedgerV2.Green,
            CharacterStatus.Hospitalized => LedgerV2.Amber,
            _ => LedgerV2.Red,
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
            var carrying = CarryingLine(roster, member, out var issued);
            LedgerV2.Mono(rect, ColCarrying, -12f, CarryW, carrying, 12f,
                dead ? LedgerV2.Faint : issued ? LedgerV2.Body : LedgerV2.Muted,
                1f, TextAlignmentOptions.MidlineRight);

            var status = member.Status;
            LedgerV2.Figure(rect, ColCondition, -12f, CondW, ConditionWord(status), 12f,
                dead ? LedgerV2.Faint : ConditionInk(status));

            // A man on his feet gets no second line: "on his feet" under FIT is a line
            // of type that says nothing, sixty times down the page. The note is for the
            // men something HAPPENED to, which is the only reason to scan the column.
            var note = ConditionNote(member, status);
            if (note.Length > 0)
                LedgerV2.Mono(rect, ColCarrying, -32f, CarryW + ColGap + CondW, note, 9.5f,
                    dead ? LedgerV2.Faint : LedgerV2.Red, 0f,
                    TextAlignmentOptions.MidlineRight);

            var posted = roster.AssignmentOf(member.Id).Kind != AssignmentKind.Pool;
            var standing = member.Wanted ? "WANTED" : dead ? "—" : posted ? "ACTIVE" : "IDLE";
            LedgerV2.Figure(rect, ColStanding, -12f, StandW, standing, 12f,
                member.Wanted ? LedgerV2.Red
                : dead ? LedgerV2.Faint
                : posted ? LedgerV2.Green : LedgerV2.Red);

            LedgerV2.Figure(rect, ColWage, -12f, WageW,
                LedgerText.Cash(Outfit.Wages.WageFor(member)), 15.5f,
                dead ? LedgerV2.Faint : member.WageDemand > 0 ? LedgerV2.Red
                : LedgerV2.Ink);

            // A standing demand is a DECISION waiting on the page, and the roll is what
            // the reader scans: what he asked for goes under what he draws, so the file
            // that can answer it is one click away rather than a discovery.
            if (!dead && member.WageDemand > 0)
                LedgerV2.Mono(rect, ColWage, -32f, WageW,
                    "WANTS " + LedgerText.Cash(member.WageDemand), 9.5f, LedgerV2.Red,
                    0f, TextAlignmentOptions.MidlineRight);
        }

        // ------------------------------------------------------------- the payroll

        /// <summary>
        /// The payroll band, struck across the foot of the roll and pinned to the last
        /// man on it: what the whole sheet costs a day, and the one line that says why
        /// it does not fall when a man goes down.
        /// </summary>
        void BuildPayrollBand(Roster roster)
        {
            Block("Foot rule", rollFootBand, 0f, 0f, RollW, 4f, LedgerV2.Ink);

            var title = Line(rollFootBand, LedgerStyle.Condensed,
                21f, LedgerV2.Ink, RollPad, -16f, RollInner - 330f, 28f,
                "PAYROLL · RUNNING");
            title.characterSpacing = 6f;

            LedgerV2.Mono(rollFootBand, RollPad, -48f, RollInner - 330f,
                "the jailed and the hurt keep drawing · only the dead come off", 11f,
                LedgerV2.Muted, 1f);

            LedgerV2.Figure(rollFootBand, RollPad + RollInner - 330f, -22f,
                330f - RightInset,
                LedgerText.Cash(Outfit.Wages.DailyPayroll(roster)) + " / day", 29f,
                LedgerV2.Ink);
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
            // The panel comes down to the card: a dossier that runs past the page
            // scrolls inside a full-height window, and a short one - the front's card,
            // a man with no record - ends where it ends rather than trailing half a
            // screen of blank stock under the last line.
            var contentH = Mathf.Max(120f, -y + 16f);
            fileBodyH = Mathf.Clamp(contentH, 120f, FileBodyMax);

            cardContent.sizeDelta = new Vector2(0f, contentH);
            cardViewport.sizeDelta = new Vector2(FileInner, fileBodyH);
            if (filePanel)
                filePanel.sizeDelta = new Vector2(FileW, FileHeadH + fileBodyH + FileFootH);
            cardFoot.anchoredPosition = new Vector2(FilePad, -(FileHeadH + fileBodyH));

            var maxScroll = Mathf.Max(0f, contentH - fileBodyH);
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
            // PSY-003. A man who has asked for the rate has asked YOU, and the asking
            // does not stop until it is answered: while the demand stands he goes on
            // skimming and his loyalty goes on draining, so the answer belongs on his
            // own file, beside the wage it is about.
            y = BuildRaiseDemand(member, textX, textW, y);
            y = Particular("CONDITION", LedgerText.StatusLabel(member.Status), textX, textW, y,
                member.Status == CharacterStatus.Active ? LedgerV2.Ink : LedgerV2.Red);
            if (TryObservedBlock(member.Id, out var currentBlock))
            {
                y = Particular("CURRENT STATUS", "On street", textX, textW, y);
                y = Particular("CURRENT BLOCK", currentBlock, textX, textW, y);
            }
            y = Particular("LOYALTY", member.Loyalty + " of 100", textX, textW, y,
                member.Loyalty < Loyalty.WatchBand ? LedgerV2.Red : LedgerV2.Ink);
            // What he is LIKE, in words. The numbers behind these are never shown: the
            // player is meant to learn a man's character from what the man does, and a
            // column of five more figures would let him skip that entirely.
            y = Particular("CHARACTER", CharacterWords(member), textX, textW, y);
            // LOY-004. The book's own verdict, so the player is never asked to read
            // eleven numbers to find the man worth promoting or the man worth watching.
            // The red flag prints in red; the two he could BE print in ink.
            var marks = ManFlags.Of(member);
            if (marks != ManFlag.None)
                y = Particular("THE BOOK SAYS", ManFlags.Line(marks), textX, textW, y,
                    (marks & ManFlag.RedFlag) != 0 ? LedgerV2.Red : LedgerV2.Ink);
            // ECON-006. His NAME, quarter by quarter - earned at doors he has leaned on
            // and forgotten wherever he stops going. It already scales what he is worth
            // standing on a street and what a shop does when he asks; a mechanic the
            // player cannot see is a mechanic he cannot plan around, so it is printed
            // in the same words everything else here is printed in.
            var known = KnownOnLine(member.Id);
            if (known.Length > 0)
                y = Particular("KNOWN ON", known, textX, textW, y);

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

            // ---- and what WE have on him ----
            y = BuildCareer(member, y);

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

        /// <summary>
        /// WITH THE OUTFIT: the story the rap sheet cannot tell. The city knows what he
        /// was charged with; this is what he actually did for us - who brought him in,
        /// the crews he stood in, the nights he came out of, the ranks he held.
        ///
        /// Built exactly like the rap sheet above it and set in the same type, because
        /// it IS the same document continued: oldest line first, a life read forward.
        /// The copy WRAPS rather than truncates - an incident is a sentence, and a
        /// sentence cut off at the panel edge by TMP's ellipsis is a line the player
        /// cannot finish - so each entry is measured for the height it really needs.
        /// </summary>
        float BuildCareer(Character member, float y)
        {
            const float dayW = 96f;
            const float lineH = 17f;

            Caps(cardContent, 0f, y, CardInner - 150f, "WITH THE OUTFIT", 11f,
                LedgerV2.Body, 5f);
            Caps(cardContent, CardInner - 150f, y, 150f,
                member.Career.Count == 1 ? "1 ENTRY" : member.Career.Count + " ENTRIES",
                9f, LedgerV2.Label, 3f, TextAlignmentOptions.MidlineRight);
            y -= 20f;
            Rule(cardContent, 0f, y + 4f, CardInner, LedgerV2.Rule);

            if (member.Career.Count == 0)
            {
                Line(cardContent, LedgerStyle.MonoItalic, 12f, LedgerV2.Muted,
                    0f, y - 2f, CardInner, lineH, "Nothing yet. He has only just come on.");
                return y - lineH - 10f;
            }

            var copyW = CardInner - dayW;
            for (var i = 0; i < member.Career.Count; i++)
            {
                var entry = member.Career[i];

                Line(cardContent, LedgerStyle.Mono, 11f, LedgerV2.Label,
                    0f, y - 2f, dayW, lineH, LedgerText.DayStamp(entry.Day));

                var copy = Paragraph(cardContent, LedgerStyle.Mono, 12f,
                    InkFor(entry.Kind), dayW, y - 3f, copyW, lineH, entry.Line,
                    lineSpacing: 0f);
                var tall = Mathf.Max(lineH,
                    Mathf.Ceil(copy.GetPreferredValues(entry.Line, copyW, 0f).y));

                // The street it happened on, under the date, and ONLY when the
                // sentence has not already named it: most of the feed's own lines read
                // "... at Pearl Street" and set it twice would be the file arguing with
                // itself. A place with nowhere to print was the field quietly going to
                // waste - it is stored on every entry and was read by nothing.
                var where = entry.Where;
                if (where.Length > 0 &&
                    entry.Line.IndexOf(where, System.StringComparison.OrdinalIgnoreCase) < 0)
                {
                    tall = Mathf.Max(tall, lineH * 2f);
                    var place = LedgerV2.Mono(cardContent, 0f, y - lineH - 1f, dayW,
                        where, 9.5f, LedgerV2.Muted, 1f);
                    place.overflowMode = TextOverflowModes.Ellipsis;
                }

                copy.rectTransform.sizeDelta = new Vector2(copyW, tall);

                y -= tall;
                if (i < member.Career.Count - 1)
                    LedgerV2.Leader(cardContent, 0f, y + 3f, CardInner);
            }

            return y - 10f;
        }

        /// <summary>The pen one line of a man's history is set in - the book's own
        /// rule that a record is read by colour before it is read by word. A rank
        /// change is the spine of the file and prints in the lieutenant's blue;
        /// anything that put him off his feet or off the books prints in red.</summary>
        static Color InkFor(CareerKind kind) => kind switch
        {
            CareerKind.Rank => LedgerV2.Lieutenant,
            CareerKind.Condition => LedgerV2.Red,
            CareerKind.Struck => LedgerV2.Red,
            CareerKind.Improved => LedgerV2.Green,
            CareerKind.Posting => LedgerV2.Muted,
            _ => LedgerV2.Body,
        };

        /// <summary>
        /// The clerk's line on what a man is LIKE, in words - never the numbers behind
        /// them. Loyalty is left out: it has its own particular directly above this
        /// one, and saying it twice in two different registers reads as two facts.
        /// </summary>
        static string CharacterWords(Character member)
        {
            var words = "";
            for (var i = 0; i < Personality.All.Length; i++)
            {
                var trait = Personality.All[i];
                if (trait == PersonalityTrait.Loyalty)
                    continue;
                if (words.Length > 0)
                    words += ", ";
                words += Personality.Band(trait, Personality.Get(member, trait));
            }
            return words;
        }

        /// <summary>One particular on the file: the label on the left, the answer held
        /// to the right margin, and the dotted rule the design closes every one of them
        /// with. Answers the y below.</summary>
        static readonly List<(string Neighborhood, float Name)> KnownOn =
            new List<(string, float)>();

        /// <summary>
        /// The quarters this man is known on, best first, in words (ECON-006). At most
        /// three: a file is a page a reader scans, and a man who has worked ten streets
        /// is described by the three he is biggest on.
        /// </summary>
        static string KnownOnLine(int characterId)
        {
            var runtime = RoadDemo.TerritoryRuntime.Instance;
            if (runtime == null || runtime.Reputation == null)
                return "";

            runtime.Reputation.CollectFor(characterId, runtime.GameHour, KnownOn);
            if (KnownOn.Count == 0)
                return "";

            var line = "";
            var shown = Mathf.Min(3, KnownOn.Count);
            for (var i = 0; i < shown; i++)
                line += (i > 0 ? " · " : "") + KnownOn[i].Neighborhood + " (" +
                        Territory.TerritoryReputationLedger.Word(KnownOn[i].Name) + ")";
            if (KnownOn.Count > shown)
                line += " · +" + (KnownOn.Count - shown) + " more";
            return line;
        }

        /// <summary>
        /// He wants the rate (PSY-003), and there are two answers. YES moves his
        /// envelope for good and stops him taking it out of the till himself; NO costs
        /// him loyalty and leaves the underpaid clock running, so the ladder simply
        /// goes on from where it was. Nothing here decides for the player: a demand
        /// nobody answers stands, which is the point of it.
        /// </summary>
        float BuildRaiseDemand(Character member, float x, float w, float y)
        {
            if (member.WageDemand <= 0)
                return y;

            const float rowH = 30f;
            var band = NewRect("Raise demand", cardContent);
            PlaceTopLeft(band, x, y + 2f, w, rowH + 20f);
            Fill(band, LedgerV2.Wrong);

            var asking = Line(band, LedgerStyle.MonoBold, 10.5f, LedgerV2.Red,
                6f, -4f, w - 12f, LineBox(10.5f),
                "HE WANTS " + LedgerText.Cash(member.WageDemand) + " A DAY" +
                (member.Skimming ? " — AND IS TAKING IT ANYWAY" : ""));
            asking.characterSpacing = 2f;
            asking.overflowMode = TextOverflowModes.Ellipsis;

            var half = (w - 12f - 8f) * 0.5f;
            var id = member.Id;
            LedgerV2.Button(band, "GRANT IT", 6f, -20f, half, rowH - 6f, () =>
            {
                var result = director.GrantRaise(id);
                lastRefusal = result.Ok ? "" : result.Reason;
                dirty = true;
            }, red: false, size: 9.5f);
            LedgerV2.Button(band, "REFUSE HIM", 6f + half + 8f, -20f, half, rowH - 6f, () =>
            {
                var result = director.RefuseRaise(id);
                lastRefusal = result.Ok ? "" : result.Reason;
                dirty = true;
            }, red: true, size: 9.5f);

            return y - (rowH + 26f);
        }

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
                        crew != null ? crew.HoodIds.Count : 0) + " " +
                    LedgerText.DemoteCost, 9.5f, LedgerV2.Red, 2f);
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

            // LOY-003. The choice is made on the man's HISTORY, so what the book has
            // already decided about him stands beside the key that acts on it - the
            // player should never have to scroll back up the file to find out whether
            // this is the man he thought it was.
            var marks = ManFlags.Of(member);
            if (marks != ManFlag.None)
                Caps(cardFoot, half + 12f, -22f, half, ManFlags.Line(marks), 9.5f,
                    (marks & ManFlag.RedFlag) != 0 ? LedgerV2.Red : LedgerV2.Lieutenant,
                    2f, TextAlignmentOptions.MidlineRight);
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
