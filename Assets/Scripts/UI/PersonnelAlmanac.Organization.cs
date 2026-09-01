using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using LivingCity.Gangs;
using LivingCity.Personnel;
using LivingCity.Territory;
using RoadDemo;
using static LivingCity.UI.LedgerKit;

namespace LivingCity.UI
{
    /// <summary>
    /// ORGANIZATION is Sheet II: the chain of command drawn as one continuous spine,
    /// the block ledger set against it, and the orders the sheet has filed with the
    /// outfit. Nothing on this page mutates anything at the click - a verb is FILED,
    /// stands as an unanswered request, and the outfit's office grants or refuses it
    /// (Outfit.OutfitFilings). Every figure is an IOrganizationQuery or territory
    /// snapshot; every mutation still leaves through PersonnelDirector or the territory
    /// command gateway, now from inside the filing's resolver.
    /// </summary>
    public sealed partial class PersonnelAlmanac
    {
        static float OrganizationTop = PageTop - 76f;
        static float OrganizationHeight = 614f;

        /// <summary>The chain of command takes the whole sheet under its own head. The
        /// sheet is full bleed, so the window's height is the page's height - a taller
        /// window shows more of the branch before the reader has to roll it.</summary>
        static void MeasureOrganizationLayout()
        {
            OrganizationTop = PageTop - 76f;
            OrganizationHeight = -(PageBottom - OrganizationTop);
        }

        /// <summary>The dashed spine every branch hangs off, and the stub that reaches
        /// out of it to a card.</summary>
        const float SpineX = 10f;
        const float SpineStub = 6f;
        const float BranchX = 20f;
        const float BranchSpineX = BranchX + 5f;
        const float BranchContentX = BranchSpineX + 5f;

        /// <summary>How many faces stand on a branch before the rest become "+n".</summary>
        const int ThumbLimit = 10;

        /// <summary>The block ledger is a reading, not a census: past this many rows the
        /// sheet says so and sends the reader to the map rather than printing the city.
        /// </summary>
        const int BlockRowLimit = 12;

        RectTransform organizationFixed;
        internal RectTransform organizationViewport;
        internal RectTransform organizationContent;
        internal float organizationScroll;

        /// <summary>
        /// The column the sections are being laid into, and how wide it is. A narrow
        /// sheet runs one column down the page; a wide one runs the chain of command
        /// down the left and the ledger down the right, because a 2000-unit line of a
        /// four-word heading is not a page, it is a banner with a page inside it.
        ///
        /// Every section builder places against THESE and never against the page, so a
        /// section can be dropped into either column without knowing which it is in.
        /// </summary>
        RectTransform organizationColumn;
        float organizationW;

        /// <summary>Under this the sheet is one column. Above it the chain of command
        /// goes down the left and the block ledger down the right. The canvas guarantees
        /// a 1920-unit frame, which is 1636 units of page, so two columns are the
        /// ordinary sheet and the single column below is the safety net, not the design.
        /// </summary>
        const float TwoColumnSheet = 1500f;

        /// <summary>Air between the columns.</summary>
        const float ColumnGutter = 26f;

        /// <summary>What share of the page the chain takes. The rest is the ledger's,
        /// which needs the wider half: it carries a five-column row with a key on the end
        /// of it and the block file that opens underneath.</summary>
        const float ChainShare = 0.42f;

        /// <summary>A column narrower than this cannot hold a row that reads across, so
        /// every section that draws one stacks it instead. Measured off the widest fixed
        /// run on the page: a 190-unit name beside two 150-unit meters and a plate.</summary>
        const float NarrowColumn = 640f;

        /// <summary>Under this the block ledger stops being a five-column table. Its five
        /// headings and a 230-unit key need about nine hundred units; less than that and
        /// every column is narrower than the words standing in it.</summary>
        const float LedgerTableWidth = 900f;

        /// <summary>The line the sheet writes when an order goes in. It is cleared the
        /// moment the office has nothing left to answer, so a stale "not answered yet"
        /// never stands over a page where everything has been ruled on.</summary>
        const string FiledNote = "filed · the outfit has not answered yet";

        string organizationNote = "";

        /// <summary>The man picked out of the pool, waiting for a branch to take him.</summary>
        int organizationPickedHoodId = -1;

        /// <summary>Which branch rosters are open, and whether the pool's is.</summary>
        readonly HashSet<int> organizationOpenBranches = new HashSet<int>();
        bool organizationPoolOpen = true;

        /// <summary>The block whose "who answers" menu is down, if any.</summary>
        TerritoryBlockId organizationBlockMenu;

        /// <summary>True while the ledger is too narrow for its five columns, so every
        /// block row is drawn as a card that reads downward. Set once per repaint, in
        /// BuildBlockLedger, and read by the row builder under it.</summary>
        bool organizationStackedRows;

        // Map targeting is intentionally transient: the reader picks a block anywhere in
        // the city, and the sheet brings it back with its menu already down. Nothing is
        // filed until a name is chosen out of that menu.
        bool organizationTargetingBlock;
        TerritoryBlockId organizationPendingBlock;
        string organizationPendingBlockName = "";

        readonly List<OrganizationPerson> organizationLeaders =
            new List<OrganizationPerson>();
        readonly List<OrganizationPerson> organizationPeople =
            new List<OrganizationPerson>();
        readonly List<OrganizationPerson> organizationScratch =
            new List<OrganizationPerson>();
        readonly List<OrganizationBlockResponsibility> organizationResponsibilities =
            new List<OrganizationBlockResponsibility>();
        /// <summary>blockId to the leader who answers for it, rebuilt every repaint.</summary>
        readonly Dictionary<TerritoryBlockId, int> organizationPaper =
            new Dictionary<TerritoryBlockId, int>();

        /// <summary>What the street says about every block, read ONCE per repaint. The
        /// player query projects a fresh presentation object on every call, and this
        /// sheet asks about the same block from four places.</summary>
        readonly Dictionary<TerritoryBlockId, BlockControl> organizationControl =
            new Dictionary<TerritoryBlockId, BlockControl>();

        readonly List<TerritoryBlockId> organizationBlockRows = new List<TerritoryBlockId>();

        /// <summary>Blocks one of our men is standing on right now. A block earns a line
        /// on this sheet by being on our paper, being ours on the street, or having our
        /// men on it - the third is how ground the outfit is working shows up before a
        /// single deed changes hands.</summary>
        readonly HashSet<TerritoryBlockId> organizationOurStreets =
            new HashSet<TerritoryBlockId>();

        RectTransform organizationHoverNote;

        bool OrganizationTargetingActive => organizationTargetingBlock;

        // ------------------------------------------------------------------ the page

        void BuildOrganizationPage(RectTransform sheet)
        {
            var root = NewPageRoot(sheet, LedgerPage.Organization);
            organizationFixed = NewRect("Organization Fixed", root);
            Stretch(organizationFixed);

            organizationViewport = NewRect("Organization Window", root);
            PlaceTopLeft(organizationViewport, PageLeft, OrganizationTop,
                PageWidth, OrganizationHeight);
            organizationViewport.gameObject.AddComponent<RectMask2D>();

            organizationContent = NewRect("Organization File", organizationViewport);
            organizationContent.anchorMin = new Vector2(0f, 1f);
            organizationContent.anchorMax = new Vector2(1f, 1f);
            organizationContent.pivot = new Vector2(0f, 1f);
            organizationContent.anchoredPosition = Vector2.zero;
            organizationContent.sizeDelta = new Vector2(0f, OrganizationHeight);
        }

        void RebuildOrganization()
        {
            if (!organizationFixed || !organizationContent)
                return;

            // The faces under the pointer are about to be destroyed, and a destroyed
            // object sends no PointerExit - drop the hover card with them.
            HideThumbNote();

            if (organizationNote == FiledNote && outfit && outfit.Filings.AwaitingCount == 0)
                organizationNote = "";

            // The live plate is not page furniture. Park it directly under the scrolling
            // content while the old columns are torn down, then BuildBlockModel puts that
            // SAME view into the new plate. The camera and RenderTexture therefore survive
            // ordinary observation/roster repaints instead of being recreated with them.
            ParkBlockModelForRebuild();
            foreach (Transform old in organizationFixed)
                Destroy(old.gameObject);
            foreach (Transform old in organizationContent)
                if (blockCardModel == null || old != blockCardModel.transform)
                    Destroy(old.gameObject);
            organizationHoverNote = null;

            LedgerV2.PageHead(organizationFixed, PageLeft, PageTop, PageWidth,
                "ORGANIZATION",
                "CHAIN OF COMMAND, CAPACITY, AND WHO ANSWERS FOR WHICH BLOCK");
            if (!string.IsNullOrEmpty(organizationNote))
                LedgerV2.Mono(organizationFixed, PageRight - 700f, PageTop - 34f, 700f,
                    organizationNote, 10f, LedgerV2.PaperBlue, 2f,
                    TextAlignmentOptions.MidlineRight);

            var query = director != null ? director.Organization : null;
            if (query == null || !query.TryGetBoss(out var boss))
            {
                Line(organizationContent, LedgerStyle.MonoItalic, 14f,
                    LedgerV2.Red, 0f, 0f, PageWidth, 24f,
                    "The command file has no authoritative Boss Character.");
                CloseOrganization(24f);
                FinishBlockModelRebuild();
                return;
            }

            organizationLeaders.Clear();
            organizationLeaders.Add(boss);
            query.CollectLieutenants(organizationScratch);
            organizationLeaders.AddRange(organizationScratch);
            query.CollectHoods(organizationPeople);
            ReadOrganizationPaper(query);
            ReadOrganizationControl();
            if (TerritoryRuntime.Instance != null)
                TerritoryRuntime.Instance.CollectOccupiedBlocks(
                    GangCatalog.PlayerGangId, organizationOurStreets);
            else
                organizationOurStreets.Clear();

            if (organizationPickedHoodId >= 0 && !IsPooled(organizationPickedHoodId))
                organizationPickedHoodId = -1;

            var cursor = 0f;

            // The sheet is read in two columns. The chain of command goes down the left
            // because it is the thing a reader scans; the ledger takes the right, with
            // the orders the sheet has filed under it. The block file is no longer a
            // section of its own - it opens inside the ledger, under the row it belongs
            // to, so the reader never has to look away from the block he clicked.
            if (PageWidth >= TwoColumnSheet)
            {
                var span = PageWidth - ColumnGutter;
                var chainW = span * ChainShare;
                var ledgerW = span - chainW;

                InColumn(0f, chainW);
                var chain = BuildChainOfCommand(query, boss, cursor);

                InColumn(chainW + ColumnGutter, ledgerW);
                var ledger = BuildBlockLedger(cursor);
                ledger = BuildFiledOrders(ledger);

                cursor = Mathf.Max(chain, ledger);
            }
            else
            {
                InColumn(0f, PageWidth);
                cursor = BuildChainOfCommand(query, boss, cursor);
                cursor = BuildBlockLedger(cursor);
                cursor = BuildFiledOrders(cursor);
            }
            CloseOrganization(cursor);
            FinishBlockModelRebuild();
        }

        /// <summary>Opens a column inside the page and points every section builder at
        /// it. The column carries no fill and no mask of its own - it is where a section
        /// stands, not something the reader ever sees.</summary>
        void InColumn(float x, float width)
        {
            organizationColumn = NewRect("Column", organizationContent);
            PlaceTopLeft(organizationColumn, x, 0f, width, 1f);
            organizationW = width;
        }

        void CloseOrganization(float cursor)
        {
            var contentHeight = Mathf.Max(OrganizationHeight, cursor + 28f);
            organizationContent.sizeDelta = new Vector2(0f, contentHeight);
            organizationScroll = Mathf.Clamp(
                organizationScroll, 0f, Mathf.Max(0f, contentHeight - OrganizationHeight));
            organizationContent.anchoredPosition = new Vector2(0f, organizationScroll);
        }

        // ---------------------------------------------------------- chain of command

        float BuildChainOfCommand(
            IOrganizationQuery query, OrganizationPerson boss, float cursor)
        {
            cursor = Section(cursor, "I. CHAIN OF COMMAND",
                "EACH MAN ANSWERS TO EXACTLY ONE MAN ABOVE HIM");

            cursor = BuildBossCard(query, boss, cursor);

            // The spine is drawn last, once the branches below it have said how far it
            // has to run - so it is measured here and filled in at the end.
            var spineTop = cursor + 4f;
            cursor += 12f;

            for (var i = 1; i < organizationLeaders.Count; i++)
                cursor = BuildLieutenantBranch(query, organizationLeaders[i], cursor);

            if (organizationLeaders.Count == 1)
            {
                Stub(cursor + 10f);
                Line(organizationColumn, LedgerStyle.MonoItalic, 12f, LedgerV2.Muted,
                    BranchX, -cursor, organizationW - BranchX, 20f,
                    "No lieutenant branches are on the books.");
                cursor += 30f;
            }

            cursor = BuildPoolBranch(cursor);

            DottedVRule(organizationColumn, SpineX, -spineTop,
                Mathf.Max(0f, cursor - spineTop - 8f), LedgerV2.Dotted);
            return cursor + 14f;
        }

        /// <summary>The dashed stub that reaches out of the spine to one branch.</summary>
        void Stub(float cursor) =>
            LedgerV2.Leader(organizationColumn, SpineX, -cursor, SpineStub);

        float BuildBossCard(
            IOrganizationQuery query, OrganizationPerson boss, float cursor)
        {
            // The design's 78 | 1fr | max-330 grid at a fourteen-unit gutter. The
            // 124-unit head is a FLOOR: two stacked meters with their notes come to
            // more than that, and a card sized under it clips the second note.
            const float headH = 124f;
            const float plateW = 78f;
            const float gutter = 14f;
            const float meterMax = 330f;
            const float pad = 12f;
            const float textX = plateW + gutter;
            const float cardH = headH + 4f;

            // The Boss always keeps one capacity column on his right. Let that column
            // take a measured share of the card instead of dropping both meters into a
            // wide footer; the portrait/name remain one block on the left at every page
            // width this layout supports.
            var meterW = Mathf.Clamp(
                (organizationW - textX - gutter) * 0.42f, 150f, meterMax);
            var meterX = organizationW - meterW - pad;
            var textW = Mathf.Max(140f, meterX - gutter - textX);

            var card = NewRect("Boss", organizationColumn);
            PlaceTopLeft(card, 0f, -cursor, organizationW, cardH);
            Fill(card, LedgerV2.FilmPlate);
            // The same warm stage and full-card falloff as the filmed block. Built
            // before the card contents so the vignette shades only their background.
            Vignette(card);
            Block("Rank", card, 0f, 0f, organizationW, 4f, LedgerV2.Red);

            var member = director.Roster != null ? director.Roster.Find(boss.Id) : null;
            Face(card, 0f, -4f, plateW, cardH - 4f, member, "BOSS",
                LedgerV2.DarkPlate, LedgerV2.HeadDim);

            var kicker = Line(card, LedgerStyle.MonoBold, 10f, LedgerV2.Boss,
                textX, -16f, textW, LineBox(10f), "BOSS · YOU");
            kicker.characterSpacing = 14f;

            var name = Line(card, LedgerStyle.Condensed, 23f, LedgerV2.HeadCream,
                textX, -32f, textW, LineBox(23f), boss.Name);
            name.characterSpacing = 1f;
            name.overflowMode = TextOverflowModes.Ellipsis;

            // Under the name's own line box, not through it: the design's rule is a
            // five-unit margin below a 23-point line, and 23 points of Oswald stand
            // over thirty-five units tall.
            Block("Name rule", card, textX, -(32f + LineBox(23f) + 3f),
                Mathf.Min(190f, textW), 1f, LedgerV2.Red);

            var under = Line(card, LedgerStyle.Mono, 10f, LedgerV2.HeadDim,
                textX, -78f, textW, LineBox(10f),
                textW >= 330f
                    ? "HEAD OF THE FAMILY · ANSWERS TO NOBODY"
                    : "HEAD OF THE FAMILY");
            under.characterSpacing = 3f;
            under.overflowMode = TextOverflowModes.Ellipsis;

            var hire = DarkTape(card, "HIRE · " + LedgerText.Cash(director.HoodRecruitmentCost),
                textX, -98f, Mathf.Min(150f, textW), 22f, () => FileRecruit(-1));
            SetActionEnabled(hire, director != null);

            var capacity = query.CapacityOf(boss.Id);
            // The outfit's ground against the ground the Boss can administer: the second
            // figure on his card is the city, not his own paperwork.
            var held = new CapacityMeasure(CountHeldBlocks(), capacity.Blocks.Maximum);

            var took = Meter(card, meterX, 14f, meterW, "MEN ON THE BOOKS",
                capacity.Manpower, "man", "men", dark: true);
            Meter(card, meterX, 14f + took + 8f, meterW, "BLOCKS THE OUTFIT HOLDS",
                held, "block", "blocks", dark: true);
            var height = cardH;
            card.sizeDelta = new Vector2(organizationW, height);

            // The file's own corner marks, top right and bottom right: what a photograph
            // mounted on a card carries. Twelve units, one thick, and nothing else.
            CornerMark(card, organizationW - 18f, -9f, true);
            CornerMark(card, organizationW - 18f, -(height - 17f), false);
            return cursor + height;
        }

        /// <summary>One corner mark - two hairlines meeting, the way a card mount is
        /// cornered. Right-hand side only, which is where the design puts them.</summary>
        static void CornerMark(Transform card, float x, float y, bool top)
        {
            const float arm = 12f;
            Block("Corner across", card, x, top ? y : y - arm + 1f, arm, 1f,
                LedgerV2.HeadDim);
            Block("Corner down", card, x + arm - 1f, y, 1f, arm, LedgerV2.HeadDim);
        }

        float BuildLieutenantBranch(
            IOrganizationQuery query, OrganizationPerson lieutenant, float cursor)
        {
            // The design's 70 | 1fr | meter grid, with the two capacity meters stacked
            // in one right-hand column just as they are on the Boss card.
            const float headH = 124f;
            const float plateW = 70f;
            const float gutter = 14f;
            const float flashW = 5f;
            const float pad = 14f;
            const float meterMax = 260f;

            var width = organizationW - BranchX;
            var x = flashW + plateW + gutter;
            var leaderId = lieutenant.Id;
            var picking = organizationPickedHoodId >= 0;
            var meterW = Mathf.Clamp((width - x - gutter) * 0.42f, 150f, meterMax);
            var meterX = width - pad - meterW;
            var nameW = Mathf.Max(140f, meterX - gutter - x);

            Stub(cursor + headH * 0.5f);

            var capacity = query.CapacityOf(lieutenant.Id);
            var card = NewRect("Lieutenant " + lieutenant.Name, organizationColumn);
            PlaceTopLeft(card, BranchX, -cursor, width, headH);
            Fill(card, LedgerV2.Panel);

            var member = director.Roster != null ? director.Roster.Find(lieutenant.Id) : null;
            Face(card, flashW, 0f, plateW, headH, member,
                InitialsOf(lieutenant.Name), LedgerV2.Portrait, LedgerV2.Muted);

            var rank = Line(card, LedgerStyle.MonoBold, 10f,
                capacity.IsOverCapacity ? LedgerV2.Red : LedgerV2.Lieutenant,
                x, -18f, nameW, LineBox(10f), "LIEUTENANT");
            rank.characterSpacing = 8f;
            var name = Line(card, LedgerStyle.Condensed, 18f, LedgerV2.Ink,
                x, -34f, nameW, LineBox(18f), lieutenant.Name);
            name.characterSpacing = 0.5f;
            name.overflowMode = TextOverflowModes.Ellipsis;

            // ECON-005: the man read as a WORD (archetype - derived, never assigned)
            // and the crew's rounds policy beside it. A click cycles the policy;
            // the archetype is his own and no click changes it.
            var branchCrew = director.Roster != null
                ? director.Roster.CrewOf(lieutenant.Id)
                : null;
            var reading = LieutenantArchetypes.Word(LieutenantArchetypes.Of(member)) +
                          (branchCrew != null
                              ? " · " + LieutenantArchetypes.Word(branchCrew.Policy) +
                                " ROUNDS"
                              : "");
            // The reading stays with the name; the right column belongs only to the
            // two capacity meters.
            var readingW = nameW;
            var readingRow = NewRect("Reading", card);
            PlaceTopLeft(readingRow, x, -62f, readingW, 16f);
            var readingFace = ClickSurface(readingRow);
            Line(readingRow, LedgerStyle.Mono, 9f, LedgerV2.Label,
                0f, 0f, readingW, 14f, reading).characterSpacing = 2f;
            if (branchCrew != null)
            {
                var branchCrewId = branchCrew.Id;
                var next = (CrewPolicy)(((int)branchCrew.Policy + 1) % 4);
                RowButton(readingRow, readingFace,
                    () => { director.SetCrewPolicy(branchCrewId, next); dirty = true; });
            }

            var took = Meter(card, meterX, 8f, meterW, "MANPOWER UNDER HIM",
                capacity.Manpower, "man", "men", dark: false);
            Meter(card, meterX, 8f + took + 8f, meterW, "BLOCKS ON HIS PAPER",
                capacity.Blocks, "block", "blocks", dark: false);

            var height = headH;
            if (picking)
            {
                var picked = Person(organizationPickedHoodId);
                var hoodId = organizationPickedHoodId;
                LedgerV2.Button(card,
                    "FILE · PUT " + FirstName(picked.Name).ToUpperInvariant() +
                    " UNDER HIM", x, -height, width - x - pad, 30f,
                    () => FileHoodPlacement(hoodId, leaderId), red: false, size: 10f);
                height += 38f;
            }

            card.sizeDelta = new Vector2(width, height);
            // The flash runs the whole card, including the filing row while a transfer
            // is waiting.
            Block("Rank", card, 0f, 0f, flashW, height,
                capacity.IsOverCapacity ? LedgerV2.Red : LedgerV2.Lieutenant);

            cursor += height;
            return BuildBranchRoster(query, lieutenant, cursor);
        }

        /// <summary>The faces on a branch, the count beside them, and the roster the
        /// reader can open under it.</summary>
        float BuildBranchRoster(
            IOrganizationQuery query, OrganizationPerson lieutenant, float cursor)
        {
            query.CollectDirectSubordinates(lieutenant.Id, organizationScratch);
            var men = new List<OrganizationPerson>();
            for (var i = 0; i < organizationScratch.Count; i++)
                if (organizationScratch[i].Rank == Rank.Hood)
                    men.Add(organizationScratch[i]);

            var top = cursor;
            cursor += 8f;
            var contentX = BranchContentX;
            var branchId = lieutenant.Id;
            cursor = BuildFaceStrip(men, contentX, cursor, lieutenant.Name,
                BranchSummary(men), organizationOpenBranches.Contains(lieutenant.Id),
                () => ToggleBranch(branchId), () => FileRecruit(branchId));

            if (organizationOpenBranches.Contains(lieutenant.Id))
                cursor = BuildRosterGrid(men, contentX, cursor, recall: true);

            DottedVRule(organizationColumn, BranchSpineX, -top,
                Mathf.Max(0f, cursor - top - 6f), LedgerV2.Hair);
            return cursor + 10f;
        }

        float BuildPoolBranch(float cursor)
        {
            var pool = new List<OrganizationPerson>();
            for (var i = 0; i < organizationPeople.Count; i++)
                if (organizationPeople[i].IsUnassigned && organizationPeople[i].IsAvailable)
                    pool.Add(organizationPeople[i]);

            Stub(cursor + 8f);
            var picked = organizationPickedHoodId >= 0 ? Person(organizationPickedHoodId)
                : default;
            // The long sentence is what a full-width sheet says. A column has no room
            // for it, and a hint cut off in the middle is not a hint.
            var terse = organizationW < NarrowColumn;
            var hint = picked.IsValid
                ? terse
                    ? "PICKED: " + picked.Name + " — NOW FILE HIM UNDER A LIEUTENANT"
                    : "PICKED: " + picked.Name +
                      " — NOW PRESS FILE ON THE LIEUTENANT WHO SHOULD TAKE HIM"
                : pool.Count > 0
                    ? terse
                        ? "UNDER YOU · " + pool.Count + " IDLE — TRANSFER A MAN"
                        : "UNDER YOU DIRECTLY · " + pool.Count +
                          " IDLE, NO BRANCH, NO EARNINGS — TRANSFER A MAN TO A LIEUTENANT"
                    : "UNDER YOU DIRECTLY · NOBODY IS SITTING IDLE";
            var hintLine = Line(organizationColumn, LedgerStyle.MonoBold, 11f,
                LedgerV2.Red, BranchX + 8f, -cursor, organizationW - BranchX - 8f,
                LineBox(11f), hint);
            hintLine.overflowMode = TextOverflowModes.Ellipsis;

            var top = cursor + 16f;
            cursor += 22f;
            var contentX = BranchContentX;
            cursor = BuildFaceStrip(pool, contentX, cursor, "THE BOSS",
                pool.Count == 1 ? "1 MAN IDLE" : pool.Count + " MEN IDLE",
                organizationPoolOpen, TogglePool);

            if (organizationPoolOpen)
                cursor = BuildRosterGrid(pool, contentX, cursor, recall: false);

            DottedVRule(organizationColumn, BranchSpineX, -top,
                Mathf.Max(0f, cursor - top - 6f), LedgerV2.Hair);
            return cursor + 8f;
        }

        float BuildFaceStrip(List<OrganizationPerson> men, float x, float cursor,
            string underName, string summary, bool open, UnityAction onToggle,
            UnityAction onHire = null)
        {
            // The design's thumbnails: thirty by thirty-eight at a three-unit gap, each
            // standing on a three-unit bar in the colour of whether he is earning.
            const float thumbW = 30f;
            const float thumbH = 38f;
            const float pitch = 33f;
            var rosterLabel = open
                ? "HIDE ROSTER"
                : "OPEN ROSTER · " + men.Count + " →";
            var hireLabel = onHire != null
                ? "HIRE · " + LedgerText.Cash(director.HoodRecruitmentCost)
                : "";
            var rosterW = LedgerV2.ButtonWidth(rosterLabel, 10f);
            var hireW = onHire != null ? LedgerV2.ButtonWidth(hireLabel, 10f) : 0f;
            var keysW = rosterW + (onHire != null ? hireW + 8f : 0f);
            var keysX = organizationW - keysW;

            // Ten faces are 330 units. A column that cannot show ten shows what it can
            // and says how many it did not, which is what the +n was always for.
            var faceRoom = Mathf.Max(pitch, keysX - x - 80f);
            var limit = Mathf.Clamp(Mathf.FloorToInt(faceRoom / pitch), 1, ThumbLimit);

            var shown = Mathf.Min(men.Count, limit);
            for (var i = 0; i < shown; i++)
                Thumb(men[i], underName, x + i * pitch, cursor, thumbW, thumbH);

            var run = x + shown * pitch + 7f;
            if (men.Count > shown)
            {
                Line(organizationColumn, LedgerStyle.MonoBold, 11f, LedgerV2.Body,
                    run, -(cursor + 12f), 60f, LineBox(11f), "+" + (men.Count - shown));
                run += 46f;
            }

            var noteW = keysX - run - 12f;
            if (noteW > 40f)
            {
                var note = Line(organizationColumn, LedgerStyle.Mono, 11f, LedgerV2.Muted,
                    run, -(cursor + 12f), noteW, LineBox(11f), summary);
                note.overflowMode = TextOverflowModes.Ellipsis;
            }

            var keyY = -(cursor + 6f);
            LedgerV2.Button(organizationColumn,
                rosterLabel, keysX, keyY, rosterW, 26f, onToggle,
                red: false, outline: true, size: 10f);

            if (onHire != null)
            {
                var hire = LedgerV2.Button(organizationColumn,
                    hireLabel, keysX + rosterW + 8f, keyY, hireW, 26f, onHire,
                    red: false, outline: true, size: 10f);
                SetActionEnabled(hire, director != null);
            }

            return cursor + thumbH + 10f;
        }

        void Thumb(OrganizationPerson person, string underName,
            float x, float cursor, float w, float h)
        {
            var member = director.Roster != null ? director.Roster.Find(person.Id) : null;
            var slot = NewRect("Face " + person.Name, organizationColumn);
            PlaceTopLeft(slot, x, -cursor, w, h);
            Face(slot, 0f, 0f, w, h, member, "", LedgerV2.Thumb, LedgerV2.Muted);

            // The bar he stands on: green when he is posted and earning, red when he is
            // drawing pay for nothing. The one thing a wall of faces has to say.
            var posted = director.Roster != null &&
                         director.Roster.AssignmentOf(person.Id).Kind != AssignmentKind.Pool;
            Block("Duty", slot, 0f, -(h - 3f), w, 3f,
                posted ? LedgerV2.Green : LedgerV2.Red);

            var zone = slot.gameObject.AddComponent<OrganizationFaceZone>();
            zone.almanac = this;
            zone.personId = person.Id;
            zone.underName = underName;

            var personId = person.Id;
            RowButton(slot, ClickSurface(slot), () => ViewPersonnelMember(personId));
        }

        float BuildRosterGrid(
            List<OrganizationPerson> men, float x, float cursor, bool recall)
        {
            // Four columns at the sheet's width, the same 290-unit minimum the design
            // sets: a roster is read down a column, not across a line of empty paper.
            const float minCell = 290f;
            const float columnGap = 12f;
            var width = organizationW - x;
            var columns = Mathf.Clamp(
                Mathf.FloorToInt((width + columnGap) / (minCell + columnGap)), 1, 4);
            const float rowH = 24f;
            var cell = (width - columnGap * (columns - 1)) / columns;

            Rule(organizationColumn, x, -cursor, width, LedgerV2.Rule);
            cursor += 6f;

            if (men.Count == 0)
            {
                Line(organizationColumn, LedgerStyle.MonoItalic, 11.5f, LedgerV2.Muted,
                    x, -cursor, width, 20f,
                    recall ? "Nobody reports to him." : "Nobody is sitting idle.");
                return cursor + 26f;
            }

            for (var i = 0; i < men.Count; i++)
            {
                var person = men[i];
                var column = i % columns;
                var line = i / columns;
                var rowX = x + column * (cell + columnGap);
                var rowY = cursor + line * rowH;

                var row = NewRect("Man " + person.Name, organizationColumn);
                PlaceTopLeft(row, rowX, -rowY, cell, rowH);
                // One Graphic per object: the pool row's own stock IS its click surface.
                Image stock = null;
                if (!recall)
                {
                    stock = Fill(row, organizationPickedHoodId == person.Id
                        ? LedgerV2.Money
                        : LedgerV2.Panel);
                    stock.raycastTarget = true;
                }
                Rule(row, 0f, -(rowH - 1f), cell, LedgerV2.Hair);

                var posted = HasPost(person);
                Block("Dot", row, 0f, -8f, 7f, 7f,
                    posted ? LedgerV2.Green : LedgerV2.Red);
                // The name takes what the cell has left after the duty and the key: a
                // fixed 170 in a 300-unit cell prints a name over the words beside it.
                var nameW = Mathf.Max(80f, cell * 0.46f);
                var stateX = 14f + nameW + 8f;
                Line(row, LedgerStyle.Condensed, 13f, LedgerV2.Ink,
                    14f, -3f, nameW, 18f, person.Name)
                    .overflowMode = TextOverflowModes.Ellipsis;

                var personId = person.Id;
                if (recall)
                {
                    Line(row, LedgerStyle.Mono, 9.5f, LedgerV2.Label,
                        stateX, -4f, Mathf.Max(20f, cell - stateX - 70f), 16f,
                        HoodDuty(person))
                        .overflowMode = TextOverflowModes.Ellipsis;
                    LedgerV2.Button(row, "RECALL", cell - 66f, -1f, 66f, 21f,
                        () => FileHoodRecall(personId), red: true, outline: true, size: 8f);
                }
                else
                {
                    var isPicked = organizationPickedHoodId == person.Id;
                    var terse = cell < 340f;
                    Caps(row, stateX, -4f, Mathf.Max(20f, cell - stateX - 6f),
                        isPicked
                            ? terse ? "PICKED" : "PICKED · CHOOSE A LIEUTENANT ABOVE"
                            : terse ? "IDLE · TRANSFER" : "IDLE · TRANSFER TO A LIEUTENANT",
                        9f, isPicked ? LedgerV2.Red : LedgerV2.Label, 2f,
                        TextAlignmentOptions.MidlineRight);
                    RowButton(row, stock, () => PickHood(personId));
                }
            }

            var lines = (men.Count + columns - 1) / columns;
            return cursor + lines * rowH + 8f;
        }

        // -------------------------------------------------------------- block ledger

        float BuildBlockLedger(float cursor)
        {
            cursor = Section(cursor, "II. BLOCK LEDGER", "");
            var mapReady = MapTargeting.Available &&
                           TerritoryRuntime.Instance?.Commands != null;
            var seeAllW = Mathf.Min(300f, organizationW * 0.5f);
            var seeAll = LedgerV2.Button(organizationColumn,
                seeAllW < 220f ? "ALL BLOCKS" : "SEE ALL BLOCKS IN THE CITY",
                organizationW - seeAllW, -(cursor - 46f), seeAllW, 28f,
                BeginBlockTargeting, red: false, outline: true, size: 9.5f);
            SetActionEnabled(seeAll, mapReady);

            CollectBlockRows();

            // The file stands under a ROW. Lose the row - the name was struck off and the
            // street took the ground back - and the file closes with it rather than
            // hanging under a ledger that no longer mentions the block.
            if (blockCardId.IsValid && !organizationBlockRows.Contains(blockCardId))
            {
                blockCardId = default;
                StopBlockFilm();
            }

            // Five headings and a 230-unit key need about nine hundred units. Under that
            // the table stops being a table: each block becomes a small card that reads
            // down instead of across, and the head band goes with the columns it named.
            organizationStackedRows = organizationW < LedgerTableWidth;
            float[] columns = null;
            if (!organizationStackedRows)
            {
                var action = 230f;
                var span = organizationW - action;
                var c0 = span * 1.5f / 5.3f;
                var c1 = span * 1.2f / 5.3f;
                var c2 = c1;
                var c3 = span - c0 - c1 - c2;
                columns = new[] { c0, c1, c2, c3, action };

                var head = NewRect("Ledger head", organizationColumn);
                PlaceTopLeft(head, 0f, -cursor, organizationW, 32f);
                Fill(head, LedgerV2.Head);
                var headings = new[]
                {
                    "BLOCK", "RESPONSIBLE · PAPER", "CONTROL · STREET",
                    "READING", "CHANGE THE PAPER",
                };
                var headColours = new[]
                {
                    LedgerV2.HeadInk, LedgerV2.HeadPaper, LedgerV2.HeadStreet,
                    LedgerV2.HeadInk, LedgerV2.HeadInk,
                };
                var x = 0f;
                for (var i = 0; i < headings.Length; i++)
                {
                    Caps(head, x + 14f, -9f, columns[i] - 20f, headings[i], 9.5f,
                        headColours[i], 4f);
                    x += columns[i];
                }
                cursor += 32f;
            }

            if (organizationBlockRows.Count == 0)
            {
                var empty = NewRect("Ledger empty", organizationColumn);
                PlaceTopLeft(empty, 0f, -cursor, organizationW, 44f);
                Fill(empty, LedgerV2.Panel);
                Frame(empty, 1f, LedgerV2.Rule);
                Line(empty, LedgerStyle.MonoItalic, 12f, LedgerV2.Muted,
                    14f, -12f, organizationW - 28f, 20f,
                    "No block is on our paper and none is ours on the street.");
                return cursor + 56f;
            }

            var frame = NewRect("Ledger", organizationColumn);
            PlaceTopLeft(frame, 0f, -cursor, organizationW, 1f);
            Frame(frame, 1f, LedgerV2.Rule);

            var rows = Mathf.Min(organizationBlockRows.Count, BlockRowLimit);
            var height = 0f;
            for (var i = 0; i < rows; i++)
            {
                var blockId = organizationBlockRows[i];
                height = BuildBlockRow(blockId, columns, cursor, height);
                if (blockCardId == blockId)
                    height = BuildBlockFile(cursor + height) - cursor;
            }

            // The open block reads its file even when the ledger has stopped printing
            // rows: a list cut at twelve must not swallow the one block the reader is
            // actually looking at.
            if (blockCardId.IsValid &&
                organizationBlockRows.IndexOf(blockCardId) >= rows)
            {
                height = BuildBlockRow(blockCardId, columns, cursor, height);
                height = BuildBlockFile(cursor + height) - cursor;
            }
            frame.sizeDelta = new Vector2(organizationW, height);
            cursor += height;

            if (organizationBlockRows.Count > rows)
            {
                Caps(organizationColumn, 0f, -(cursor + 6f), organizationW,
                    "AND " + (organizationBlockRows.Count - rows) +
                    " MORE ON THE BOOKS · OPEN THE MAP TO READ THE WHOLE CITY",
                    9f, LedgerV2.Label, 3f);
                cursor += 24f;
            }
            return cursor + 16f;
        }

        float BuildBlockRow(
            TerritoryBlockId blockId, float[] columns, float top, float offset)
        {
            if (organizationStackedRows)
                return BuildStackedBlockRow(blockId, top, offset);

            const float rowH = 54f;
            var leaderId = organizationPaper.TryGetValue(blockId, out var id) ? id : -1;
            var leader = Leader(leaderId);
            var control = ControlOf(blockId);
            var mismatch = leader.IsValid && control == BlockControl.NotOurs;
            var orphan = !leader.IsValid && control == BlockControl.Held;
            var menuOpen = organizationBlockMenu == blockId;

            var open = blockCardId == blockId;

            var row = NewRect("Block " + blockId.Value, organizationColumn);
            PlaceTopLeft(row, 0f, -(top + offset), organizationW, rowH);
            Fill(row, menuOpen
                ? LedgerV2.Money
                : mismatch || orphan ? LedgerV2.Carbon : LedgerV2.Panel);
            // The row itself opens the block's file. The key at the right end still only
            // changes the paper, so a reader after one thing never gets the other.
            RowButton(row, ClickSurface(row), () => OpenBlockCard(blockId));
            if (open)
                Block("Open mark", row, 0f, 0f, 3f, rowH, LedgerV2.Red);
            Rule(row, 0f, 0f, organizationW, LedgerV2.Rule);

            var x = 0f;
            Line(row, LedgerStyle.Condensed, 17f, LedgerV2.Ink,
                x + 14f, -10f, columns[0] - 24f, 22f, BlockName(blockId));
            Line(row, LedgerStyle.Mono, 10f, LedgerV2.Label,
                x + 14f, -31f, columns[0] - 24f, 16f, NeighborhoodOf(blockId));
            x += columns[0];

            var paperColour = leader.IsValid ? LedgerV2.PaperBlue : LedgerV2.Red;
            // Hatched, not solid: the design draws what is written on PAPER as a
            // ruled square and what is true on the STREET as a filled one, so the two
            // columns can never be read for each other at a glance.
            LedgerV2.PaperMark(row, x + 14f, -21f, paperColour);
            Line(row, LedgerStyle.MonoBold, 12f, paperColour,
                x + 32f, -20f, columns[1] - 42f, 18f,
                leader.IsValid ? leader.Name : "NOBODY NAMED");
            x += columns[1];

            var streetColour = ControlColour(control);
            LedgerV2.StreetMark(row, x + 14f, -21f, streetColour);
            Line(row, LedgerStyle.MonoBold, 12f, streetColour,
                x + 32f, -20f, columns[2] - 42f, 18f, ControlWord(control));
            x += columns[2];

            var reading = Reading(
                leader, control, organizationOurStreets.Contains(blockId),
                out var readingColour);
            Paragraph(row, LedgerStyle.Mono, 11f, readingColour,
                x + 14f, -12f, columns[3] - 24f, 34f, reading, lineSpacing: 1f);
            x += columns[3];

            var label = menuOpen ? "CLOSE"
                : leader.IsValid ? "CHANGE WHO ANSWERS" : "NAME SOMEONE";
            LedgerV2.Button(row, label, x + 14f, -13f, columns[4] - 28f, 28f,
                () => ToggleBlockMenu(blockId), red: false, outline: !menuOpen, size: 9f);

            offset += rowH;
            return menuOpen
                ? offset + BuildBlockMenu(blockId, leaderId, top + offset, columns)
                : offset;
        }

        /// <summary>
        /// The same block, read down instead of across. A column has no room for five
        /// headings, so the name and its ward take the first line, the paper and the
        /// street stand side by side on the second, and the reading runs under both with
        /// the key that changes the paper held to the top right. Everything the wide row
        /// says, in the order a reader takes it.
        /// </summary>
        float BuildStackedBlockRow(TerritoryBlockId blockId, float top, float offset)
        {
            const float rowH = 104f;
            const float pad = 14f;
            var leaderId = organizationPaper.TryGetValue(blockId, out var id) ? id : -1;
            var leader = Leader(leaderId);
            var control = ControlOf(blockId);
            var mismatch = leader.IsValid && control == BlockControl.NotOurs;
            var orphan = !leader.IsValid && control == BlockControl.Held;
            var menuOpen = organizationBlockMenu == blockId;
            var open = blockCardId == blockId;

            var row = NewRect("Block " + blockId.Value, organizationColumn);
            PlaceTopLeft(row, 0f, -(top + offset), organizationW, rowH);
            Fill(row, menuOpen
                ? LedgerV2.Money
                : mismatch || orphan ? LedgerV2.Carbon : LedgerV2.Panel);
            // The card itself opens the block's file. The key still only changes the
            // paper, so a reader after one thing never gets the other.
            RowButton(row, ClickSurface(row), () => OpenBlockCard(blockId));
            if (open)
                Block("Open mark", row, 0f, 0f, 3f, rowH, LedgerV2.Red);
            Rule(row, 0f, 0f, organizationW, LedgerV2.Rule);

            var keyW = Mathf.Min(190f, organizationW * 0.42f);
            var label = menuOpen ? "CLOSE"
                : leader.IsValid ? "CHANGE WHO ANSWERS" : "NAME SOMEONE";
            LedgerV2.Button(row, label, organizationW - pad - keyW, -10f, keyW, 26f,
                () => ToggleBlockMenu(blockId), red: false, outline: !menuOpen, size: 9f);

            var titleW = Mathf.Max(60f, organizationW - pad * 2f - keyW - 12f);
            Line(row, LedgerStyle.Condensed, 17f, LedgerV2.Ink,
                pad, -9f, titleW, 22f, BlockName(blockId))
                .overflowMode = TextOverflowModes.Ellipsis;
            Line(row, LedgerStyle.Mono, 10f, LedgerV2.Label,
                pad, -30f, titleW, 16f, NeighborhoodOf(blockId))
                .overflowMode = TextOverflowModes.Ellipsis;

            // Hatched, not solid: what is written on PAPER is a ruled square and what is
            // true on the STREET is a filled one, so the two can never be read for each
            // other at a glance - the marks carry that here, without the headings.
            var half = (organizationW - pad * 2f) * 0.5f;
            var paperColour = leader.IsValid ? LedgerV2.PaperBlue : LedgerV2.Red;
            LedgerV2.PaperMark(row, pad, -54f, paperColour);
            Line(row, LedgerStyle.MonoBold, 12f, paperColour,
                pad + 18f, -53f, half - 24f, 18f,
                leader.IsValid ? leader.Name : "NOBODY NAMED")
                .overflowMode = TextOverflowModes.Ellipsis;

            var streetColour = ControlColour(control);
            LedgerV2.StreetMark(row, pad + half, -54f, streetColour);
            Line(row, LedgerStyle.MonoBold, 12f, streetColour,
                pad + half + 18f, -53f, half - 24f, 18f, ControlWord(control))
                .overflowMode = TextOverflowModes.Ellipsis;

            var reading = Reading(
                leader, control, organizationOurStreets.Contains(blockId),
                out var readingColour);
            Paragraph(row, LedgerStyle.Mono, 10.5f, readingColour,
                pad, -74f, organizationW - pad * 2f, 28f, reading, lineSpacing: 1f);

            offset += rowH;
            return menuOpen
                ? offset + BuildBlockMenu(blockId, leaderId, top + offset, null)
                : offset;
        }

        float BuildBlockMenu(
            TerritoryBlockId blockId, int leaderId, float top, float[] columns)
        {
            var options = organizationLeaders.Count + (leaderId >= 0 ? 1 : 0);
            var height = 30f + options * 30f;
            var width = Mathf.Min(340f, organizationW - 28f);
            var menu = NewRect("Menu " + blockId.Value, organizationColumn);
            PlaceTopLeft(menu, organizationW - width - 14f, -top, width, height);
            Fill(menu, LedgerV2.Head);
            Frame(menu, 1f, LedgerV2.Head);
            Caps(menu, 12f, -9f, width - 24f,
                "WHO ANSWERS FOR " + BlockName(blockId).ToUpperInvariant(),
                9f, LedgerV2.HeadDim, 3f);

            var y = 30f;
            for (var i = 0; i < organizationLeaders.Count; i++)
            {
                var leader = organizationLeaders[i];
                var capacity = director.Organization.CapacityOf(leader.Id).Blocks;
                var full = capacity.Current >= capacity.Maximum;
                var isBoss = leader.Rank == Rank.Boss;
                var target = leader.Id;

                var option = NewRect("Option " + leader.Name, menu);
                PlaceTopLeft(option, 0f, -y, width, 30f);
                Rule(option, 0f, 0f, width, LedgerV2.HeadDim);
                Line(option, LedgerStyle.Condensed, 13f,
                    isBoss ? LedgerV2.Amber : LedgerV2.HeadCream,
                    12f, -6f, width - 150f, 18f,
                    isBoss ? leader.Name + " · YOU" : leader.Name)
                    .overflowMode = TextOverflowModes.Ellipsis;
                Caps(option, width - 138f, -7f, 126f,
                    capacity.Current + " / " + capacity.Maximum + (full ? " · FULL" : ""),
                    9f, full ? LedgerV2.Red : LedgerV2.HeadDim, 2f,
                    TextAlignmentOptions.MidlineRight);
                RowButton(option, ClickSurface(option),
                    () => FileBlockResponsibility(blockId, target));
                y += 30f;
            }

            if (leaderId >= 0)
            {
                var strike = NewRect("Option strike", menu);
                PlaceTopLeft(strike, 0f, -y, width, 30f);
                Rule(strike, 0f, 0f, width, LedgerV2.HeadDim);
                Line(strike, LedgerStyle.Condensed, 13f, LedgerV2.Red,
                    12f, -6f, width - 24f, 18f, "Nobody · strike the name off");
                RowButton(strike, ClickSurface(strike),
                    () => FileBlockRemoval(blockId, leaderId));
            }

            return height + 8f;
        }

        // --------------------------------------------------------------- filed orders

        float BuildFiledOrders(float cursor)
        {
            cursor = Section(cursor, "III. ORDERS FILED WITH THE OUTFIT",
                "THIS SHEET ASKS · THE OUTFIT ANSWERS");

            var filings = outfit ? outfit.Filings : null;
            var count = filings != null ? Mathf.Min(filings.All.Count, 6) : 0;

            const float stampW = 80f;
            const float chipW = 112f;
            const float rulingW = 320f;

            // Wide, a filing is one line: stamp, what was asked, the status chip, and the
            // ruling held to the right margin. In a column the four will not stand on one
            // line, so the ask takes the first and the chip and its ruling the second.
            var stacked = organizationW < stampW + chipW + rulingW + 260f;
            var rowH = stacked ? 58f : 40f;
            // A wrapping footer needs two lines of room in a column and one across a
            // sheet. The panel is sized for what its own footer will actually take.
            var footH = stacked ? 48f : 30f;

            var frame = NewRect("Filings", organizationColumn);
            var height = count * rowH + footH;
            PlaceTopLeft(frame, 0f, -cursor, organizationW, height);
            Fill(frame, LedgerV2.Panel);
            Frame(frame, 1f, LedgerV2.Rule);

            for (var i = 0; i < count; i++)
            {
                var filing = filings.All[i];
                var row = NewRect("Filing " + filing.Id, frame);
                PlaceTopLeft(row, 0f, -(i * rowH), organizationW, rowH);
                Rule(row, 14f, -(rowH - 1f), organizationW - 28f, LedgerV2.Hair);

                var chip = NewRect("Status", row);
                Fill(chip, StatusColour(filing.Status));
                Caps(chip, 0f, -5f, chipW, StatusWord(filing.Status), 9.5f,
                    LedgerV2.Panel, 4f, TextAlignmentOptions.Center);

                if (stacked)
                {
                    var textX = 20f + stampW + 12f;
                    Line(row, LedgerStyle.Mono, 11f, LedgerV2.Label,
                        20f, -10f, stampW, 18f, filing.Stamp);
                    Line(row, LedgerStyle.Mono, 12f, LedgerV2.Body,
                        textX, -10f, Mathf.Max(40f, organizationW - textX - 20f), 18f,
                        filing.Text)
                        .overflowMode = TextOverflowModes.Ellipsis;

                    PlaceTopLeft(chip, 20f, -32f, chipW, 20f);
                    Line(row, LedgerStyle.Mono, 10.5f, LedgerV2.Label,
                        20f + chipW + 12f, -32f,
                        Mathf.Max(40f, organizationW - 52f - chipW), 18f, filing.Ruling)
                        .overflowMode = TextOverflowModes.Ellipsis;
                }
                else
                {
                    var textX = 20f + stampW + 16f;
                    var chipX = organizationW - 20f - rulingW - 16f - chipW;

                    Line(row, LedgerStyle.Mono, 11f, LedgerV2.Label,
                        20f, -12f, stampW, 18f, filing.Stamp);
                    Line(row, LedgerStyle.Mono, 12f, LedgerV2.Body,
                        textX, -12f, chipX - textX - 16f, 18f, filing.Text);

                    PlaceTopLeft(chip, chipX, -10f, chipW, 22f);

                    Line(row, LedgerStyle.Mono, 10.5f, LedgerV2.Label,
                        organizationW - 20f - rulingW, -12f, rulingW, 18f, filing.Ruling)
                        .alignment = TextAlignmentOptions.MidlineRight;
                }
            }

            var awaiting = filings != null ? filings.AwaitingCount : 0;
            var footer = filings == null
                ? "The outfit's filing office is not open in this scene · orders take " +
                  "effect the moment they are given."
                : count == 0
                    ? "Nothing has been asked of the outfit yet."
                    : awaiting > 0
                        ? "Nothing above has happened yet. The outfit is still ruling on " +
                          awaiting + "."
                        : "Every order on this sheet has been ruled on.";
            Paragraph(frame, LedgerStyle.Mono, 11f, LedgerV2.Muted,
                16f, -(count * rowH + 6f), organizationW - 32f, footH - 10f, footer,
                lineSpacing: 1f);
            return cursor + height;
        }

        // -------------------------------------------------------------------- pieces

        float Section(float cursor, string title, string caption)
        {
            // Wide, the heading takes the left half and its caption is held to the right
            // margin of the same line. In a column a four-word heading set across half
            // the measure loses its own last word, so the title takes the whole measure
            // at a smaller strike and the caption drops to a line of its own.
            var narrow = organizationW < 720f;
            var heading = Line(organizationColumn, LedgerStyle.Condensed,
                narrow ? 16f : 20f, LedgerV2.Ink, 0f, -cursor,
                narrow ? organizationW : organizationW * 0.5f, 26f, title);
            heading.characterSpacing = narrow ? 2f : 4f;
            heading.overflowMode = TextOverflowModes.Ellipsis;
            if (caption.Length > 0)
            {
                if (narrow)
                {
                    Caps(organizationColumn, 0f, -(cursor + 22f), organizationW,
                        caption, 9f, LedgerV2.Label, 2f);
                    cursor += 16f;
                }
                else
                {
                    var half = organizationW * 0.5f;
                    Caps(organizationColumn, half, -(cursor + 4f), half,
                        caption, 9.5f, LedgerV2.Label, 3f,
                        TextAlignmentOptions.MidlineRight);
                }
            }
            cursor += 30f;
            Rule(organizationColumn, 0f, -cursor, organizationW, LedgerV2.Rule);
            return cursor + 12f;
        }

        /// <summary>One capacity meter: the figure, the bar, and the plain sentence
        /// saying what the figure means for the next order.</summary>
        /// <summary>
        /// A capacity meter, exactly as the design strikes one: the label on the left of
        /// its own line with the figure held to the right of it, a flat trough under the
        /// pair, and the line of plain English under that. Answers the height it took,
        /// so a caller can stack two without counting.
        /// </summary>
        static float Meter(Transform card, float x, float top, float width, string label,
            CapacityMeasure measure, string unit, string plural, bool dark,
            float labelSize = 10f, float figureSize = 15f)
        {
            var over = measure.IsOverCapacity;
            var full = !over && measure.Current >= measure.Maximum;
            var ink = over ? LedgerV2.Red
                : full ? LedgerV2.Amber
                : dark ? LedgerV2.HeadCream : LedgerV2.Ink;
            var labelInk = over ? ink : dark ? LedgerV2.HeadDim : LedgerV2.Muted;

            const float figureW = 96f;
            var name = Line(card, LedgerStyle.Mono, labelSize, labelInk,
                x, -top, width - figureW - 8f, LineBox(labelSize), label);
            name.characterSpacing = 6f;
            name.overflowMode = TextOverflowModes.Ellipsis;

            Line(card, LedgerStyle.MonoBold, figureSize, ink,
                x + width - figureW, -(top - 1f), figureW, LineBox(figureSize),
                measure.Current + " / " + measure.Maximum)
                .alignment = TextAlignmentOptions.MidlineRight;

            var barY = top + LineBox(labelSize) + 4f;
            MeterBar(card, x, barY, width,
                measure.Maximum <= 0 ? 0f : (float)measure.Current / measure.Maximum,
                ink, dark);

            var room = measure.Maximum - measure.Current;
            var note = over
                ? "OVER BY " + measure.Overage + " · the outfit will not add more"
                : full
                    ? "at the limit · no room for another " + unit
                    : room + " more " + (room == 1 ? unit : plural) + " will fit";
            var noteY = barY + MeterTrackH + 3f;
            var line = Line(card, over ? LedgerStyle.MonoBold : LedgerStyle.Mono, 10f,
                over ? ink : dark ? LedgerV2.HeadDim : LedgerV2.Muted,
                x, -noteY, width, LineBox(10f), note);
            line.overflowMode = TextOverflowModes.Ellipsis;
            return noteY + LineBox(10f) - top;
        }

        /// <summary>The design's trough: seven units, flat, no border, and the fill runs
        /// it edge to edge. A framed track with an inset fill reads as an empty box with
        /// a sliver in it at the fractions this page actually shows - 2 of 50 is four
        /// per cent, and four per cent of a bordered box is the border.</summary>
        const float MeterTrackH = 7f;

        static void MeterBar(Transform card, float x, float top, float width,
            float fraction, Color ink, bool dark)
        {
            var track = NewRect("Meter track", card);
            PlaceTopLeft(track, x, -top, width, MeterTrackH);
            Fill(track, dark ? LedgerStyle.RailTrough : LedgerV2.Trough);

            var filled = Mathf.Clamp01(fraction);
            if (filled <= 0f)
                return;
            var run = NewRect("Meter fill", track);
            // At least a unit of ink: a man on the books is a man on the books, and a
            // meter that prints nothing for him has lost the only thing it was for.
            PlaceTopLeft(run, 0f, 0f, Mathf.Max(1f, width * filled), MeterTrackH);
            Fill(run, ink);
        }

        /// <summary>A man's photograph in a plate, or the hatch when no model resolves.
        /// The face is always the one he wears on the street - never a picked file.</summary>
        static void Face(Transform parent, float x, float y, float w, float h,
            Character member, string initials = "", Color? ground = null,
            Color? ink = null)
        {
            var raw = LedgerV2.PortraitPlate(parent, x, y, w, h, initials, ground, ink);
            if (member != null)
                PortraitStudio.Request(MemberModel(member), PortraitStudio.Framing.Bust, raw);
        }

        /// <summary>A button on the boss's dark stock - Tape's ink is invisible there.</summary>
        TMP_Text DarkTape(Transform parent, string label, float x, float y,
            float w, float h, UnityAction onClick)
        {
            var rect = NewRect("Dark tape " + label, parent);
            PlaceTopLeft(rect, x, y, w, h);
            var face = rect.gameObject.AddComponent<Image>();
            face.sprite = null;
            face.color = new Color(1f, 1f, 1f, 0.06f);
            face.raycastTarget = true;
            Frame(rect, 1f, LedgerV2.HeadDim);

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = face;
            var colours = button.colors;
            colours.normalColor = Color.white;
            colours.highlightedColor = new Color(2.4f, 2.4f, 2.4f);
            colours.selectedColor = colours.highlightedColor;
            colours.pressedColor = new Color(0.6f, 0.6f, 0.6f);
            colours.disabledColor = new Color(1f, 1f, 1f, 0.4f);
            button.colors = colours;
            if (onClick != null)
                button.onClick.AddListener(onClick);

            var text = Text("Label", rect, LedgerStyle.Condensed, 9.5f,
                LedgerV2.HeadCream, TextAlignmentOptions.Center);
            Stretch(text.rectTransform);
            text.characterSpacing = 5f;
            text.text = label.ToUpperInvariant();
            return text;
        }

        // ------------------------------------------------------------- the hover card

        /// <summary>Prints the card that stands over a hovered face. It is built on the
        /// FIXED layer, not in the scrolling file: the window is masked, and a card that
        /// stood proud of a face at the top of it would be cut in half.</summary>
        void ShowThumbNote(int personId, string underName, RectTransform face)
        {
            if (!organizationFixed)
                return;
            HideThumbNote();

            var person = Person(personId);
            if (!person.IsValid)
                return;

            const float width = 250f;
            const float height = 64f;
            organizationHoverNote = NewRect("Face note", organizationFixed);
            Fill(organizationHoverNote, LedgerV2.Head);
            Frame(organizationHoverNote, 1f, LedgerV2.HeadDim);

            Line(organizationHoverNote, LedgerStyle.Condensed, 14f, LedgerV2.HeadCream,
                11f, -7f, width - 22f, 20f, person.Name);
            Caps(organizationHoverNote, 11f, -25f, width - 22f,
                (person.Rank == Rank.Hood ? "HOOD · UNDER " : "UNDER ") +
                underName.ToUpperInvariant(),
                9f, LedgerV2.HeadDim, 3f);
            var posted = HasPost(person);
            Block("Dot", organizationHoverNote, 11f, -44f, 7f, 7f,
                posted ? LedgerV2.Green : LedgerV2.Red);
            Line(organizationHoverNote, LedgerStyle.Mono, 10.5f, LedgerV2.HeadCream,
                24f, -42f, width - 34f, 16f, HoodDuty(person));

            var corners = new Vector3[4];
            face.GetWorldCorners(corners);
            // corners[1] is the top-left of the face, which is the corner the card is
            // hung from - eight units above it, six to the left, in the fixed layer's
            // own top-left frame.
            var local = organizationFixed.InverseTransformPoint(corners[1]);
            var x = Mathf.Clamp(local.x - 6f, PageLeft, PageRight - width);
            PlaceTopLeft(organizationHoverNote, x, local.y + height + 8f, width, height);
            organizationHoverNote.SetAsLastSibling();
        }

        void HideThumbNote()
        {
            if (organizationHoverNote)
                Destroy(organizationHoverNote.gameObject);
            organizationHoverNote = null;
        }

        /// <summary>The pointer half of a face's hover card. AddComponent-only.</summary>
        sealed class OrganizationFaceZone : MonoBehaviour, IPointerEnterHandler,
            IPointerExitHandler
        {
            public PersonnelAlmanac almanac;
            public int personId;
            public string underName;

            public void OnPointerEnter(PointerEventData eventData) =>
                almanac.ShowThumbNote(personId, underName, (RectTransform)transform);

            public void OnPointerExit(PointerEventData eventData) =>
                almanac.HideThumbNote();
        }

        // -------------------------------------------------------------- the sheet's verbs

        void ViewPersonnelMember(int memberId)
        {
            SelectMember(memberId);
            SetPage(LedgerPage.Personnel);
        }

        void ToggleBranch(int leaderId)
        {
            if (!organizationOpenBranches.Remove(leaderId))
                organizationOpenBranches.Add(leaderId);
            dirty = true;
        }

        void TogglePool()
        {
            organizationPoolOpen = !organizationPoolOpen;
            dirty = true;
        }

        void PickHood(int hoodId)
        {
            organizationPickedHoodId = organizationPickedHoodId == hoodId ? -1 : hoodId;
            organizationNote = organizationPickedHoodId >= 0
                ? "pick the lieutenant who should take him"
                : "";
            dirty = true;
        }

        void ToggleBlockMenu(TerritoryBlockId blockId)
        {
            organizationBlockMenu = organizationBlockMenu == blockId
                ? default
                : blockId;
            dirty = true;
        }

        // ------------------------------------------------------------------- filings

        /// <summary>Files one order with the outfit. Where there is no filing office in
        /// the scene the order is carried out at once instead, so a demo rig without the
        /// campaign director still commands the same systems.</summary>
        void FileOrder(string text, System.Func<Outfit.FilingRuling> resolver)
        {
            if (!outfit)
            {
                var immediate = resolver();
                organizationNote = immediate.Ruling;
                dirty = true;
                return;
            }

            outfit.Filings.File(FilingStamp(), text, resolver);
            organizationNote = FiledNote;
            dirty = true;
        }

        string FilingStamp()
        {
            var day = outfit ? outfit.Campaign.Day : 1;
            if (!cityClock)
                return "D" + day;
            return "D" + day + " " +
                   Mathf.FloorToInt(cityClock.Hour).ToString("00") + ":" +
                   Mathf.FloorToInt(cityClock.Hour % 1f * 60f).ToString("00");
        }

        void FileHoodPlacement(int hoodId, int leaderId)
        {
            var hood = Person(hoodId);
            var leader = Leader(leaderId);
            if (!hood.IsValid || !leader.IsValid)
                return;

            organizationPickedHoodId = -1;
            FileOrder(hood.Name + " put under " + leader.Name + ".", () =>
            {
                var query = director != null ? director.Organization : null;
                var target = Leader(leaderId);
                if (query == null || !target.IsValid)
                    return Outfit.FilingRuling.Refuse(
                        "that command node is no longer on the books");

                // The Boss's own branch is where an idle man LIVES, so a return to it is
                // never refused. Only a branch can be full.
                var manpower = query.CapacityOf(leaderId).Manpower;
                if (target.Rank == Rank.Lieutenant &&
                    !Outfit.OutfitFilingRules.AcceptsAnotherMan(manpower))
                    return Outfit.FilingRuling.Refuse(
                        Outfit.OutfitFilingRules.ManRefusal(target.Name, manpower));

                var result = SubmitHoodAssignment(hoodId, target);
                return result.Ok
                    ? Outfit.FilingRuling.Grant("he reports to " + target.Name + " from today")
                    : Outfit.FilingRuling.Refuse(result.Reason);
            });
        }

        void FileHoodRecall(int hoodId)
        {
            var hood = Person(hoodId);
            if (!hood.IsValid)
                return;

            FileOrder(hood.Name + " pulled back under the Boss.", () =>
            {
                var boss = organizationLeaders.Count > 0 ? organizationLeaders[0] : default;
                if (!boss.IsValid)
                    return Outfit.FilingRuling.Refuse(LedgerText.ReasonNoBoss);
                var result = SubmitHoodAssignment(hoodId, boss);
                return result.Ok
                    ? Outfit.FilingRuling.Grant("off the branch · idle and drawing pay")
                    : Outfit.FilingRuling.Refuse(result.Reason);
            });
        }

        void FileRecruit(int leaderId)
        {
            var leader = leaderId >= 0 ? Leader(leaderId) : default;
            var text = leader.IsValid && leader.Rank == Rank.Lieutenant
                ? "A man requested for " + leader.Name + "'s branch. " +
                  LedgerText.Cash(director.HoodRecruitmentCost) + " committed."
                : "A man requested under the Boss directly. " +
                  LedgerText.Cash(director.HoodRecruitmentCost) + " committed.";

            FileOrder(text, () =>
            {
                var query = director != null ? director.Organization : null;
                var target = leaderId >= 0 ? Leader(leaderId) : default;
                if (query == null)
                    return Outfit.FilingRuling.Refuse("the command file is unavailable");

                // Refused BEFORE the money leaves the safe: a branch that cannot hold
                // the man must not cost the outfit fifty dollars to find that out.
                if (target.IsValid && target.Rank == Rank.Lieutenant)
                {
                    var manpower = query.CapacityOf(target.Id).Manpower;
                    if (!Outfit.OutfitFilingRules.AcceptsAnotherMan(manpower))
                        return Outfit.FilingRuling.Refuse(
                            target.Name + " has no room (" + manpower.Current + "/" +
                            manpower.Maximum + ") · nobody hired");
                }

                var hired = director.RecruitHood(out var newId);
                if (!hired.Ok)
                    return Outfit.FilingRuling.Refuse(hired.Reason);

                var recruit = director.Roster != null ? director.Roster.Find(newId) : null;
                var name = recruit != null ? recruit.FullName : "the new man";
                if (!target.IsValid || target.Rank != Rank.Lieutenant)
                    return Outfit.FilingRuling.Grant(
                        name + " reported · idle until you place him");

                var placed = SubmitHoodAssignment(newId, target);
                return placed.Ok
                    ? Outfit.FilingRuling.Grant(name + " reports to " + target.Name)
                    : Outfit.FilingRuling.Grant(
                        name + " reported · " + placed.Reason + ", so he waits in the pool");
            });
        }

        void FileBlockResponsibility(TerritoryBlockId blockId, int leaderId)
        {
            var leader = Leader(leaderId);
            if (!leader.IsValid || !blockId.IsValid)
                return;

            organizationBlockMenu = default;
            ClearOrganizationPendingBlock();
            var blockName = BlockName(blockId);
            FileOrder("Responsibility for " + blockName + " struck against " +
                      leader.Name + ".", () =>
            {
                var runtime = TerritoryRuntime.Instance;
                var query = director != null ? director.Organization : null;
                var target = Leader(leaderId);
                if (query == null || !target.IsValid || runtime?.Commands == null)
                    return Outfit.FilingRuling.Refuse(
                        "the territory command gateway is unavailable");

                var blocks = query.CapacityOf(target.Id).Blocks;
                query.CollectBlockResponsibilities(target.Id, organizationResponsibilities);
                var alreadyHis = false;
                for (var i = 0; i < organizationResponsibilities.Count; i++)
                    if (organizationResponsibilities[i].BlockId == blockId)
                        alreadyHis = true;
                if (!alreadyHis && target.Rank == Rank.Lieutenant &&
                    !Outfit.OutfitFilingRules.AcceptsAnotherBlock(blocks))
                    return Outfit.FilingRuling.Refuse(
                        Outfit.OutfitFilingRules.BlockRefusal(target.Name, blocks));

                var node = target.Rank == Rank.Boss
                    ? TerritoryCommandNodeId.Boss(target.Id)
                    : TerritoryCommandNodeId.Lieutenant(target.Id);
                var result = runtime.Commands.Submit(new AssignBlockResponsibilityCommand(
                    blockId,
                    new TerritoryGangId(GangCatalog.PlayerGangId),
                    node,
                    target.Rank == Rank.Boss ? new TerritoryCharacterId(target.Id) : default,
                    target.Rank == Rank.Lieutenant
                        ? new TerritoryCharacterId(target.Id)
                        : default));
                if (!result.WasAccepted)
                    return Outfit.FilingRuling.Refuse(result.Reason);

                return Outfit.FilingRuling.Grant(
                    !IsOurStreet(ReadControl(blockId))
                        ? "filed as written · the block is not ours"
                        : target.Name + " answers for it from today");
            });
        }

        void FileBlockRemoval(TerritoryBlockId blockId, int leaderId)
        {
            var leader = Leader(leaderId);
            if (!blockId.IsValid)
                return;

            organizationBlockMenu = default;
            var blockName = BlockName(blockId);
            FileOrder("The name struck off " + blockName + ".", () =>
            {
                var runtime = TerritoryRuntime.Instance;
                var result = runtime != null
                    ? runtime.RemoveBlockResponsibility(blockId, leaderId)
                    : OpResult.Fail("the territory command gateway is unavailable");
                return result.Ok
                    ? Outfit.FilingRuling.Grant(
                        "nobody answers for it now · control unchanged")
                    : Outfit.FilingRuling.Refuse(result.Reason);
            });
            if (!leader.IsValid)
                organizationNote = "the block had no valid name on it";
        }

        OpResult SubmitHoodAssignment(int hoodId, OrganizationPerson leader)
        {
            var commands = TerritoryRuntime.Instance?.Commands;
            if (commands == null)
                return leader.Rank == Rank.Boss
                    ? director.AssignToBoss(hoodId, leader.Id)
                    : director.AssignToLieutenant(hoodId, leader.Id);

            var result = leader.Rank == Rank.Boss
                ? commands.Submit(new AssignHoodToBossCommand(
                    new TerritoryCharacterId(hoodId),
                    new TerritoryCharacterId(leader.Id)))
                : commands.Submit(new AssignHoodToLieutenantCommand(
                    new TerritoryCharacterId(hoodId),
                    new TerritoryCharacterId(leader.Id)));
            return result.Status == TerritoryCommandStatus.Succeeded
                ? OpResult.Success
                : OpResult.Fail(string.IsNullOrEmpty(result.Reason)
                    ? "the command was not completed"
                    : result.Reason);
        }

        // ------------------------------------------------------------- map targeting

        /// <summary>The whole city, on the map, so a block that is on nobody's paper and
        /// nobody's street can still be named. The pick comes back as a row at the head
        /// of the block ledger with its menu already down.</summary>
        void BeginBlockTargeting()
        {
            var map = MapTargeting.Surface;
            if (map == null || TerritoryRuntime.Instance?.Commands == null)
            {
                organizationNote = "canonical map targeting is unavailable";
                dirty = true;
                return;
            }

            organizationTargetingBlock = true;
            ClearOrganizationPendingBlock();
            organizationNote = "select one canonical block on the map";
            Close();

            // The book takes the player to the map itself - the turf plate by running
            // the boom out past the map line, the generated city's map by opening it -
            // and the map hands the view back when the pick lands.
            if (!map.CanSummon || !map.Summon())
            {
                organizationTargetingBlock = false;
                organizationNote = map.CanSummon ? "the map could not open" : map.SummonHint;
                OpenAtPage(LedgerPage.Organization);
                return;
            }

            RefreshTargeting();
        }

        /// <summary>Called by the shared IMapTargetingConsumer dispatch.</summary>
        void CaptureOrganizationBlock(int legacyBlockId)
        {
            var runtime = TerritoryRuntime.Instance;
            if (!OrganizationTargetingActive || runtime == null ||
                !runtime.TryGetBlock(legacyBlockId, out var blockId))
                return;

            organizationPendingBlock = blockId;
            organizationPendingBlockName = BlockName(blockId);
            organizationBlockMenu = blockId;
            organizationTargetingBlock = false;
            MapTargeting.Clear(this);
            MapTargeting.Surface?.Dismiss();
            organizationNote = organizationPendingBlockName +
                               " picked · name someone for it below";
            organizationScroll = 0f;
            OpenAtPage(LedgerPage.Organization);
        }

        void ClearOrganizationPendingBlock()
        {
            organizationPendingBlock = default;
            organizationPendingBlockName = "";
        }

        /// <summary>Drops the map pick the sheet was waiting for, without opening
        /// anything - the shell calls this when the book turns a page or dies.</summary>
        void StopOrganizationTargeting() => organizationTargetingBlock = false;

        /// <summary>Map Esc has no callback, so the closed Ledger notices the map is gone.</summary>
        void CancelOrganizationTargetingAndReturn()
        {
            organizationTargetingBlock = false;
            MapTargeting.Clear(this);
            organizationNote = "block selection cancelled";
            OpenAtPage(LedgerPage.Organization);
        }

        bool CloseOrganizationTransient()
        {
            if (organizationBlockMenu.IsValid)
            {
                organizationBlockMenu = default;
                ClearOrganizationPendingBlock();
                dirty = true;
                return true;
            }
            if (organizationPickedHoodId >= 0)
            {
                organizationPickedHoodId = -1;
                organizationNote = "";
                dirty = true;
                return true;
            }
            return false;
        }

        void DismissOrganizationTransient()
        {
            organizationBlockMenu = default;
            organizationPickedHoodId = -1;
            ClearOrganizationPendingBlock();
            HideThumbNote();
            // A shut book films nothing and holds no ground up: the second lens and the
            // streamer's hold on the block both belong to an OPEN file.
            StopBlockFilm();
        }

        // ---------------------------------------------------------------- the reading

        /// <summary>What the street says about a block, from OUR side of it. The
        /// territory layer derives control from the deeds standing on the block
        /// (TerritoryControlDerivation); this is that reading turned into the four words
        /// the sheet prints.</summary>
        enum BlockControl
        {
            NotOurs,
            Theirs,
            Contested,
            Held,
            Unknown,
        }

        void ReadOrganizationPaper(IOrganizationQuery query)
        {
            organizationPaper.Clear();
            for (var i = 0; i < organizationLeaders.Count; i++)
            {
                var leaderId = organizationLeaders[i].Id;
                query.CollectBlockResponsibilities(leaderId, organizationResponsibilities);
                for (var b = 0; b < organizationResponsibilities.Count; b++)
                    organizationPaper[organizationResponsibilities[b].BlockId] = leaderId;
            }
        }

        /// <summary>The blocks this sheet answers for: everything on our paper, and
        /// everything the street says is ours whether it is named or not. The block the
        /// reader has just picked off the map heads the list even when it is neither.
        /// </summary>
        void CollectBlockRows()
        {
            organizationBlockRows.Clear();
            if (organizationPendingBlock.IsValid)
                organizationBlockRows.Add(organizationPendingBlock);

            var query = TerritoryRuntime.Instance?.PlayerQuery;
            if (query == null)
            {
                foreach (var pair in organizationPaper)
                    if (pair.Key != organizationPendingBlock)
                        organizationBlockRows.Add(pair.Key);
                return;
            }

            // Ordered by the geography's own block list, so the sheet reads the same way
            // twice for the same city.

            var ids = query.BlockIds;
            for (var i = 0; i < ids.Count; i++)
            {
                var blockId = ids[i];
                if (blockId == organizationPendingBlock)
                    continue;
                if (!organizationPaper.ContainsKey(blockId) &&
                    !IsOurStreet(ControlOf(blockId)) &&
                    !organizationOurStreets.Contains(blockId))
                    continue;
                organizationBlockRows.Add(blockId);
            }
        }

        void ReadOrganizationControl()
        {
            organizationControl.Clear();
            var query = TerritoryRuntime.Instance?.PlayerQuery;
            if (query == null)
                return;
            var ids = query.BlockIds;
            for (var i = 0; i < ids.Count; i++)
                organizationControl[ids[i]] = ReadControl(ids[i]);
        }

        /// <summary>Ground the street already counts as ours in some measure.</summary>
        static bool IsOurStreet(BlockControl control) =>
            control == BlockControl.Held || control == BlockControl.Contested;

        BlockControl ControlOf(TerritoryBlockId blockId) =>
            organizationControl.TryGetValue(blockId, out var control)
                ? control
                : ReadControl(blockId);

        /// <summary>The live read, straight off the player query - what a filing's
        /// resolver has to use, because it runs long after the page was painted.</summary>
        static BlockControl ReadControl(TerritoryBlockId blockId)
        {
            var query = TerritoryRuntime.Instance?.PlayerQuery;
            if (query == null || !query.TryGetBlock(blockId, out var block) || block == null)
                return BlockControl.Unknown;

            var profile = TerritoryPresentationProfile.Default;
            if (block.Control == profile.Uncontrolled)
                return BlockControl.NotOurs;
            // Dominated counts: a street run outright is HELD, not unknown - reading it
            // as unknown dropped the row and slammed the open block file shut the
            // moment a family did too well.
            if (block.Control != profile.Controlled && block.Control != profile.Contested &&
                block.Control != profile.Influenced && block.Control != profile.Dominated)
                return BlockControl.Unknown;

            // Control alone does not say WHOSE. A held block we hold no premise on
            // belongs to another house, and the sheet has to say so rather than print it
            // green. It is the DEEDS that answer this, not who happens to be standing
            // there: a crew passing through a rival street does not make it ours.
            var ours = block.Holding != profile.Holding.NoneLabel &&
                       block.Holding != profile.Holding.UnknownLabel;
            if (!ours)
                return BlockControl.Theirs;
            return block.Control == profile.Controlled ||
                   block.Control == profile.Dominated
                ? BlockControl.Held
                : BlockControl.Contested;
        }

        static string ControlWord(BlockControl control) => control switch
        {
            BlockControl.Held => "HELD BY US",
            BlockControl.Contested => "CONTESTED",
            BlockControl.Theirs => "ANOTHER HOUSE",
            BlockControl.NotOurs => "NOT OURS",
            _ => "NOT KNOWN",
        };

        static Color ControlColour(BlockControl control) => control switch
        {
            BlockControl.Held => LedgerV2.Green,
            BlockControl.Contested => LedgerV2.Amber,
            BlockControl.Theirs => LedgerV2.Red,
            BlockControl.NotOurs => LedgerV2.Red,
            _ => LedgerV2.Label,
        };

        /// <summary>The sentence that says what the paper and the street add up to.
        /// The two can disagree in four ways and each of them is a different problem, so
        /// the sheet writes the problem out rather than leaving the reader to compare two
        /// columns.</summary>
        static string Reading(
            OrganizationPerson leader, BlockControl control, bool manned, out Color colour)
        {
            var ours = control == BlockControl.Held || control == BlockControl.Contested;

            if (leader.IsValid && !ours)
            {
                colour = manned ? LedgerV2.Amber : LedgerV2.Red;
                if (manned)
                    return leader.Name + " has men on it and not a premise to show for it.";
                return control == BlockControl.Theirs
                    ? leader.Name + " answers for another house's ground. Paper only."
                    : leader.Name + " answers for ground we do not hold. Paper only.";
            }
            if (!leader.IsValid && control == BlockControl.Held)
            {
                colour = LedgerV2.Red;
                return "We hold it and nobody answers for it.";
            }
            if (control == BlockControl.Contested)
            {
                colour = LedgerV2.Amber;
                return leader.IsValid
                    ? "His, but another house is pushing on it."
                    : "Contested, and nobody is named for it.";
            }
            if (!leader.IsValid)
            {
                colour = LedgerV2.Muted;
                return manned
                    ? "Our men are standing on it. No premise here is ours yet."
                    : "Nobody named. Nothing to answer for.";
            }
            colour = LedgerV2.Green;
            return "Paper and street agree.";
        }

        static Color StatusColour(Outfit.FilingStatus status) => status switch
        {
            Outfit.FilingStatus.Granted => LedgerV2.Green,
            Outfit.FilingStatus.Refused => LedgerV2.Red,
            _ => LedgerV2.Filed,
        };

        static string StatusWord(Outfit.FilingStatus status) => status switch
        {
            Outfit.FilingStatus.Granted => "GRANTED",
            Outfit.FilingStatus.Refused => "REFUSED",
            _ => "FILED",
        };

        int CountHeldBlocks()
        {
            var query = TerritoryRuntime.Instance?.PlayerQuery;
            if (query == null)
                return 0;
            var held = 0;
            var ids = query.BlockIds;
            for (var i = 0; i < ids.Count; i++)
                if (ControlOf(ids[i]) == BlockControl.Held)
                    held++;
            return held;
        }

        int CountPaperOnly()
        {
            var count = 0;
            foreach (var pair in organizationPaper)
                if (ControlOf(pair.Key) == BlockControl.NotOurs)
                    count++;
            return count;
        }

        string BranchSummary(List<OrganizationPerson> men)
        {
            if (men.Count == 0)
                return "NO HOODS UNDER HIM";
            var posted = 0;
            for (var i = 0; i < men.Count; i++)
                if (HasPost(men[i]))
                    posted++;
            return posted + " POSTED · " + (men.Count - posted) + " WITHOUT A POST";
        }

        static bool HasPost(OrganizationPerson person) =>
            person.IsAvailable && person.Rank == Rank.Hood &&
            person.Assignment != AssignmentKind.Pool;

        /// <summary>What a man is actually doing, in the sheet's own words.</summary>
        string HoodDuty(OrganizationPerson person)
        {
            if (!person.IsAvailable)
                return person.Status.ToString().ToLowerInvariant() + " · off the books";
            if (person.Rank != Rank.Hood)
                return "commands a branch";
            if (person.Assignment == AssignmentKind.Pool)
                return "no post · earning nothing";
            if (TryObservedBlock(person.Id, out var blockName))
                return "on the street · " + blockName;
            return person.Assignment == AssignmentKind.Front
                ? "front post"
                : "on his lieutenant's crew";
        }

        string NeighborhoodOf(TerritoryBlockId blockId)
        {
            var query = TerritoryRuntime.Instance?.PlayerQuery;
            return query != null && query.TryGetBlock(blockId, out var block) &&
                   block != null && block.NeighborhoodName.Length > 0
                ? block.NeighborhoodName
                : "ward unrecorded";
        }

        string BlockName(TerritoryBlockId blockId)
        {
            var query = TerritoryRuntime.Instance?.PlayerQuery;
            return query != null && query.TryGetBlock(blockId, out var block) && block != null
                ? block.BlockName
                : blockId.Value;
        }

        bool TryObservedBlock(int characterId, out string blockName)
        {
            var truth = TerritoryRuntime.Instance?.DebugTruth;
            if (truth != null)
                for (var i = 0; i < truth.BlockIds.Count; i++)
                    if (truth.TryGetBlock(truth.BlockIds[i], out var block) && block != null)
                        for (var a = 0; a < block.Actors.Count; a++)
                            if (block.Actors[a].GangId.IsValid &&
                                block.Actors[a].GangId.Value == GangCatalog.PlayerGangId &&
                                block.Actors[a].CharacterId.IsValid &&
                                block.Actors[a].CharacterId.Value == characterId)
                            {
                                blockName = block.Definition.DisplayName;
                                return true;
                            }
            blockName = "";
            return false;
        }

        OrganizationPerson Leader(int id)
        {
            for (var i = 0; i < organizationLeaders.Count; i++)
                if (organizationLeaders[i].Id == id)
                    return organizationLeaders[i];
            return default;
        }

        OrganizationPerson Person(int id)
        {
            for (var i = 0; i < organizationPeople.Count; i++)
                if (organizationPeople[i].Id == id)
                    return organizationPeople[i];
            return Leader(id);
        }

        bool IsPooled(int id)
        {
            var person = Person(id);
            return person.IsValid && person.IsUnassigned && person.IsAvailable;
        }

        static string FirstName(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName))
                return "HIM";
            var space = fullName.IndexOf(' ');
            return space > 0 ? fullName.Substring(0, space) : fullName;
        }

        static string OutfitTitle(string bossName)
        {
            if (string.IsNullOrWhiteSpace(bossName))
                return "THE OUTFIT";
            var words = bossName.Split(' ');
            var surname = words.Length > 0 ? words[words.Length - 1] : "";
            return "THE " + surname.ToUpperInvariant() + " OUTFIT";
        }

        static void SetActionEnabled(TMP_Text label, bool enabled)
        {
            if (!label)
                return;
            var button = label.GetComponentInParent<Button>();
            if (button)
                button.interactable = enabled;
            if (!enabled)
                label.color = LedgerV2.Rule;
        }
    }
}
