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
        bool organizationDetailOpen;

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

        RectTransform organizationHoverNote;

        /// <summary>The men the book is shouting about, refilled every repaint by
        /// <see cref="Notability.Top"/>. Read-only over the score - the board is thrown
        /// away and rebuilt precisely so nothing here can keep a figure of its own.</summary>
        readonly List<Character> organizationNotable = new List<Character>();

        /// <summary>Hoods the book says could run a crew, gathered for the one line
        /// that answers "who do I promote" against the Boss's span.</summary>
        readonly List<Character> organizationReady = new List<Character>();

        /// <summary>The head of the reason book, in the order the section sets it -
        /// refilled every repaint by <see cref="ReasonFeed.Latest"/>.</summary>
        readonly List<ReasonLine> organizationWords = new List<ReasonLine>();

        /// <summary>How many men the WHO TO LOOK AT panel names. Five or six, never
        /// sixty: the whole point is to answer "who should I be thinking about this
        /// morning" in one glance.</summary>
        const int NotableShown = 6;

        /// <summary>How far back WORD FROM THE CREWS reads. A fortnight of a busy
        /// outfit at the widths this column runs to.</summary>
        const int ReasonsShown = 14;

        /// <summary>Names on the READY FOR A CREW line before it says "and n more" -
        /// the line is a prompt, not a roll.</summary>
        const int ReadyNamed = 4;

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

            foreach (Transform old in organizationFixed)
                Destroy(old.gameObject);
            foreach (Transform old in organizationContent)
                Destroy(old.gameObject);
            organizationHoverNote = null;

            LedgerV2.PageHead(organizationFixed, PageLeft, PageTop, PageWidth,
                "ORGANIZATION",
                "CHAIN OF COMMAND, CAPACITY, AND WHO ANSWERS TO WHOM");
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
                return;
            }

            ReadOrganizationRoll(query);

            if (organizationPickedHoodId >= 0 && !IsPooled(organizationPickedHoodId))
                organizationPickedHoodId = -1;

            var cursor = 0f;

            // The sheet is read in two columns. The chain of command goes down the left
            // because it is the thing a reader scans; the orders the sheet has filed take
            // the right. The blocks themselves are a sheet of their own now (BLOCKS) -
            // this one is about MEN, and who answers to whom.
            if (PageWidth >= TwoColumnSheet)
            {
                var span = PageWidth - ColumnGutter;
                var chainW = span * ChainShare;
                var ledgerW = span - chainW;

                InColumn(0f, chainW);
                var chain = BuildChainOfCommand(query, boss, cursor);

                InColumn(chainW + ColumnGutter, ledgerW);
                var ledger = BuildFiledOrders(cursor);
                ledger = BuildWhoToLookAt(ledger + 24f);
                ledger = BuildWordFromTheCrews(ledger + 24f);

                cursor = Mathf.Max(chain, ledger);
            }
            else
            {
                InColumn(0f, PageWidth);
                cursor = BuildChainOfCommand(query, boss, cursor);
                cursor = BuildFiledOrders(cursor) + 24f;
                cursor = BuildWhoToLookAt(cursor) + 24f;
                cursor = BuildWordFromTheCrews(cursor);
            }
            CloseOrganization(cursor);
        }

        /// <summary>
        /// Who is on the books and what is written against their names: the leaders in
        /// rank order, the pool, the paper each block is on and what the street says
        /// about it. Read once at the head of a repaint - BOTH sheets that print any of
        /// it (ORGANIZATION and BLOCKS) start here, because the player query projects a
        /// fresh object on every call and each figure is asked for from several places.
        /// </summary>
        void ReadOrganizationRoll(IOrganizationQuery query)
        {
            if (query == null || !query.TryGetBoss(out var boss))
                return;

            organizationLeaders.Clear();
            organizationLeaders.Add(boss);
            query.CollectLieutenants(organizationScratch);
            organizationLeaders.AddRange(organizationScratch);
            query.CollectHoods(organizationPeople);
            ReadOrganizationPaper(query);
            ReadOrganizationControl();
            if (TerritoryRuntime.Instance != null)
                TerritoryRuntime.Instance.CollectOccupiedBlocks(
                    GangCatalog.PlayerGangId, blocksOurStreets);
            else
                blocksOurStreets.Clear();
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
            cursor = BuildReadyForACrew(boss, cursor);

            // The spine is drawn last, once the branches below it have said how far it
            // has to run - so it is measured here and filled in at the end.
            var spineTop = cursor + 4f;
            cursor += 12f;

            // His own crew comes first, above the lieutenants, because it is the only
            // branch he leads himself.
            cursor = BuildDetailBranch(cursor);

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
            // Three ceilings stack in the right-hand column - men, lieutenants, blocks -
            // and three of them stand taller than the 124-unit head, so the card is
            // measured off the column rather than clipping it.
            var cardH = Mathf.Max(headH + 4f, 14f + MeterHeight() * 3f + 16f + 14f);

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

            var hire = DarkTape(card, "HIRE A MAN",
                textX, -98f, Mathf.Min(150f, textW), 22f, () => FileRecruit(-1));
            SetActionEnabled(hire, director != null);

            var capacity = query.CapacityOf(boss.Id);
            // The outfit's ground against the ground the Boss can administer: the second
            // figure on his card is the city, not his own paperwork.
            var held = new CapacityMeasure(CountHeldBlocks(), capacity.Blocks.Maximum);

            // Span of control (RANK-002) stands beside the other two ceilings rather
            // than waiting to be discovered by a refusal: a Boss who can hold no more
            // branches must be able to READ that before he tries to make one.
            var span = member != null
                ? new CapacityMeasure(
                    Command.LieutenantsHeld(director.Roster),
                    Command.LieutenantCap(member))
                : default;

            var meterY = 14f;
            meterY += Meter(card, meterX, meterY, meterW, "MEN ON THE BOOKS",
                capacity.Manpower, "man", "men", dark: true) + 8f;
            meterY += Meter(card, meterX, meterY, meterW, "LIEUTENANTS UNDER HIM",
                span, "lieutenant", "lieutenants", dark: true) + 8f;
            meterY += Meter(card, meterX, meterY, meterW, "BLOCKS THE OUTFIT HOLDS",
                held, "block", "blocks", dark: true);

            var height = Mathf.Max(cardH, meterY + 14f);
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

            // FOLLOW-006. The book's marks against the man who holds the branch, in the
            // roll's own three characters. They stand at the right of the name column
            // and the name is cut to what is left, so neither can print over the other.
            var markW = Marks(card, member, x + nameW, -34f, nameW - 90f);
            var name = Line(card, LedgerStyle.Condensed, 18f, LedgerV2.Ink,
                x, -34f, Mathf.Max(90f, nameW - markW - (markW > 0f ? 8f : 0f)),
                LineBox(18f), lieutenant.Name);
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

            // FOLLOW-004. How long he has been exactly what he is - the input to the
            // one loyalty rule the player could act on and could not previously see.
            // Red once it has started costing him, which is the day it matters.
            var today = OrganizationDay;
            var parked = member != null && Loyalty.IsParked(member, today);
            if (member != null)
            {
                var tenure = Line(card, LedgerStyle.Mono, 9f,
                    parked ? LedgerV2.Red : LedgerV2.Label,
                    x, -80f, nameW, LineBox(9f), InRankLine(member, today));
                tenure.characterSpacing = 2f;
                tenure.overflowMode = TextOverflowModes.Ellipsis;
            }

            var took = Meter(card, meterX, 8f, meterW, "MANPOWER UNDER HIM",
                capacity.Manpower, "man", "men", dark: false);
            Meter(card, meterX, 8f + took + 8f, meterW, "BLOCKS ON HIS PAPER",
                capacity.Blocks, "block", "blocks", dark: false);

            var height = headH;

            // FOLLOW-006. A red flag on a HOOD is a man worth watching; a red flag on
            // the man holding a branch is the branch. So the card says it in words, and
            // says what it would cost - and the count comes off the defection
            // arithmetic itself (Defection.WouldFollow), never off the mark: a flag
            // informs and never acts.
            if (member != null && (ManFlags.Of(member) & ManFlag.RedFlag) != 0)
            {
                var would = Defection.WouldFollow(director.Roster, member);
                var band = NewRect("Warning", card);
                PlaceTopLeft(band, flashW, -height, width - flashW - pad, 24f);
                Fill(band, LedgerV2.Wrong);
                var warn = Caps(band, 10f, -6f, width - flashW - pad - 20f,
                    would <= 0
                        ? "BEARS WATCHING · IF HE WALKS HE WALKS ALONE"
                        : would == 1
                            ? "BEARS WATCHING · IF HE WALKS ONE OF HIS MEN GOES WITH HIM"
                            : "BEARS WATCHING · IF HE WALKS " + would +
                              " OF HIS MEN GO WITH HIM",
                    9.5f, LedgerV2.Red, 2f);
                warn.font = LedgerStyle.MonoBold;
                warn.overflowMode = TextOverflowModes.Ellipsis;
                height += 28f;
            }
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

        /// <summary>
        /// THE DETAIL (RANK-003): the men who stand between the Boss and the street.
        /// It reads as a branch because it IS one - a crew the Boss leads himself - so
        /// a guard costs a place at his cap and draws full wages while he learns
        /// almost nothing. That is the whole decision: protection is never free, and a
        /// thin detail is what makes a hit on the Don land.
        /// </summary>
        float BuildDetailBranch(float cursor)
        {
            var detail = director != null ? director.BodyguardDetail() : null;
            var guards = new List<OrganizationPerson>();
            if (detail != null)
                for (var i = 0; i < detail.HoodIds.Count; i++)
                {
                    var guard = Person(detail.HoodIds[i]);
                    if (guard.IsValid)
                        guards.Add(guard);
                }

            var picked = organizationPickedHoodId >= 0
                ? Person(organizationPickedHoodId)
                : default;

            Stub(cursor + 8f);
            var terse = organizationW < NarrowColumn;
            var hint = guards.Count == 0
                ? terse
                    ? "THE DETAIL · NOBODY STANDS WITH HIM"
                    : "THE DETAIL · NOBODY STANDS WITH HIM — THE NEXT SHOT REACHES HIM"
                : terse
                    ? "THE DETAIL · " + guards.Count +
                      (guards.Count == 1 ? " MAN STANDING" : " MEN STANDING")
                    : "THE DETAIL · " + guards.Count +
                      (guards.Count == 1 ? " MAN STANDS" : " MEN STAND") +
                      " IN FRONT OF HIM — FULL WAGES, NO EARNINGS";
            var hintLine = Line(organizationColumn, LedgerStyle.MonoBold, 11f,
                guards.Count == 0 ? LedgerV2.Red : LedgerV2.Boss,
                BranchX + 8f, -cursor, organizationW - BranchX - 8f, LineBox(11f), hint);
            hintLine.overflowMode = TextOverflowModes.Ellipsis;

            var top = cursor + 16f;
            cursor += 22f;
            var contentX = BranchContentX;

            if (picked.IsValid)
            {
                var hoodId = picked.Id;
                var width = organizationW - contentX;
                LedgerV2.Button(organizationColumn,
                    "FILE · PUT " + FirstName(picked.Name).ToUpperInvariant() +
                    " ON THE DETAIL", contentX, -cursor, width, 30f,
                    () => FileDetailPosting(hoodId), red: true, size: 10f);
                cursor += 38f;
            }

            cursor = BuildFaceStrip(guards, contentX, cursor, "THE DETAIL",
                guards.Count == 0
                    ? "NOBODY IN FRONT OF HIM"
                    : guards.Count + (guards.Count == 1 ? " GUARD" : " GUARDS"),
                organizationDetailOpen, ToggleDetail);

            if (organizationDetailOpen)
                cursor = BuildRosterGrid(guards, contentX, cursor, recall: true);

            DottedVRule(organizationColumn, BranchSpineX, -top,
                Mathf.Max(0f, cursor - top - 6f), LedgerV2.Hair);
            return cursor + 8f;
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
            var hireLabel = onHire != null ? "HIRE" : "";
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
                var man = director.Roster != null ? director.Roster.Find(person.Id) : null;
                var today = OrganizationDay;
                if (recall)
                {
                    // The marks take the right of what is left after the key, and the
                    // duty line is cut to the rest. When the cell is too narrow to hold
                    // both, the MARKS win: this is the page where crews are made, and
                    // "he could run one" beats "he is on his lieutenant's crew".
                    var keyW = 66f;
                    var room = Mathf.Max(20f, cell - stateX - keyW - 8f);
                    var markW = Marks(row, man, cell - keyW - 8f, -4f, room, 8.5f);
                    var dutyW = room - markW - (markW > 0f ? 6f : 0f);

                    // FOLLOW-004. Said only when it has started to matter: a man past
                    // the parked line whose ambition is over the floor is a man actively
                    // bleeding loyalty for standing still, and that is a decision the
                    // player could act on. Everybody else answers with his post.
                    var stuck = man != null && Loyalty.IsParked(man, today);
                    if (dutyW > 40f)
                        Line(row, LedgerStyle.Mono, 9.5f,
                            stuck ? LedgerV2.Red : LedgerV2.Label,
                            stateX, -4f, dutyW, 16f,
                            stuck
                                ? "parked " + Loyalty.TimeInRank(man, today) + " days"
                                : HoodDuty(person))
                            .overflowMode = TextOverflowModes.Ellipsis;

                    LedgerV2.Button(row, "RECALL", cell - keyW, -1f, keyW, 21f,
                        () => FileHoodRecall(personId), red: true, outline: true, size: 8f);
                }
                else
                {
                    var isPicked = organizationPickedHoodId == person.Id;
                    var terse = cell < 340f;
                    var room = Mathf.Max(20f, cell - stateX - 6f);
                    var markW = Marks(row, man, cell - 6f, -4f, room, 8.5f);
                    var stateW = room - markW - (markW > 0f ? 6f : 0f);
                    if (stateW > 40f)
                        Caps(row, stateX, -4f, stateW,
                            isPicked
                                ? terse ? "PICKED" : "PICKED · CHOOSE A LIEUTENANT ABOVE"
                                : terse ? "IDLE · TRANSFER"
                                    : "IDLE · TRANSFER TO A LIEUTENANT",
                            9f, isPicked ? LedgerV2.Red : LedgerV2.Label, 2f,
                            TextAlignmentOptions.MidlineRight);
                    RowButton(row, stock, () => PickHood(personId));
                }
            }

            var lines = (men.Count + columns - 1) / columns;
            return cursor + lines * rowH + 8f;
        }

        // ------------------------------------------------- what the book says of them

        /// <summary>
        /// The day the sheet is turned to. The campaign's own, because everything on
        /// this page that counts days - time in rank, the notability fold, the reason
        /// feed - was written against that clock at the day tick.
        /// </summary>
        int OrganizationDay => outfit ? outfit.Campaign.Day : RosterDay;

        /// <summary>
        /// LOY-004's marks against a name, in the ROLL's own language - "LT · GUN · !",
        /// held to the right of whatever room the caller gives them, the red flag
        /// taking the whole line's ink when it is up.
        ///
        /// The roll's painter (PersonnelAlmanac.Personnel.BuildRowMarks) and this one
        /// print the same three characters in the same two pens: there is one visual
        /// language for a mark in this book and a second one would be a second book.
        ///
        /// Answers how much width it actually took, so the caller can cut whatever
        /// stands beside it to what is left and neither can ever print over the other.
        /// </summary>
        static float Marks(Transform row, Character man, float right, float y,
            float room, float size = 9.5f)
        {
            if (man == null || man.Gone || room < 24f)
                return 0f;

            var marks = ManFlags.Of(man);
            if (marks == ManFlag.None)
                return 0f;

            // The book's order is what he could BE first and what he is a danger of
            // second - but a column too narrow for the measured run is cut from its
            // right-hand end, and the flag is the one of the three a reader must not
            // scan past. So in a tight column it leads instead of trailing.
            var tight = room < FlagW;
            var flagged = (marks & ManFlag.RedFlag) != 0;
            var line = tight && flagged ? ManFlags.Mark(ManFlag.RedFlag) : "";
            for (var i = 0; i < ManFlags.All.Length; i++)
            {
                if ((marks & ManFlags.All[i]) == 0)
                    continue;
                if (tight && ManFlags.All[i] == ManFlag.RedFlag)
                    continue;
                if (line.Length > 0)
                    line += " · ";
                line += ManFlags.Mark(ManFlags.All[i]);
            }

            var width = Mathf.Min(room, FlagW);
            var text = LedgerV2.Mono(row, right - width, y, width, line, size,
                flagged ? LedgerV2.Red : LedgerV2.Lieutenant,
                2f, TextAlignmentOptions.MidlineRight);
            text.font = LedgerStyle.MonoBold;
            text.overflowMode = TextOverflowModes.Ellipsis;
            return width;
        }

        /// <summary>
        /// FOLLOW-004. How long he has been exactly what he is, and - when it has
        /// started to cost him - that it is costing him.
        ///
        /// The figure is <see cref="Loyalty.TimeInRank"/>, which is the same figure
        /// <see cref="Loyalty.Drift"/> is charged against, so the page and the
        /// arithmetic can never disagree. A man who has never been anything else has
        /// been what he is since he signed, and the sentence says so rather than
        /// printing a zero.
        /// </summary>
        static string InRankLine(Character man, int today)
        {
            if (man == null)
                return "";
            var days = Loyalty.TimeInRank(man, today);
            var stretch = days == 1 ? "1 DAY" : days + " DAYS";
            var since = man.RankSince > 0 ? "IN RANK " : "AS HE CAME ON · ";
            return Loyalty.IsParked(man, today)
                ? "PARKED " + stretch + " · LOSING " + Loyalty.ParkedLoss + " A WEEK FOR IT"
                : since + stretch;
        }

        /// <summary>
        /// FOLLOW-006. The hoods the book says could run a crew, against the span that
        /// is the actual constraint on making one. This is the page where crews are
        /// made, so this is where the answer to "who do I promote" belongs.
        ///
        /// It NAMES men and does nothing: no key, no filing. A mark informs and never
        /// acts, and a line that offered to promote somebody would be the mark acting.
        /// </summary>
        float BuildReadyForACrew(OrganizationPerson boss, float cursor)
        {
            var roster = director != null ? director.Roster : null;
            if (roster == null)
                return cursor;

            organizationReady.Clear();
            for (var i = 0; i < roster.Members.Count; i++)
            {
                var man = roster.Members[i];
                if (man == null || man.Gone || man.Rank != Rank.Hood)
                    continue;
                if ((ManFlags.Of(man) & ManFlag.LieutenantMaterial) != 0)
                    organizationReady.Add(man);
            }
            if (organizationReady.Count == 0)
                return cursor;

            var named = "";
            var show = Mathf.Min(organizationReady.Count, ReadyNamed);
            for (var i = 0; i < show; i++)
                named += (named.Length > 0 ? ", " : "") +
                         organizationReady[i].FullName;
            if (organizationReady.Count > show)
                named += " and " + (organizationReady.Count - show) + " more";

            var member = roster.Find(boss.Id);
            var room = member != null
                ? Command.LieutenantCap(member) - Command.LieutenantsHeld(roster)
                : 0;
            var against = room > 0
                ? room == 1 ? " · one place left under you"
                    : " · " + room + " places left under you"
                : " · no place left under you";

            var line = Line(organizationColumn, LedgerStyle.Mono, 10.5f,
                room > 0 ? LedgerV2.Lieutenant : LedgerV2.Muted,
                0f, -(cursor + 6f), organizationW, LineBox(10.5f),
                "READY FOR A CREW · " + named + against);
            line.characterSpacing = 1f;
            line.overflowMode = TextOverflowModes.Ellipsis;
            return cursor + LineBox(10.5f) + 8f;
        }

        // ------------------------------------------------------- who to look at (005)

        /// <summary>
        /// FOLLOW-005. The notability figure itself, in the one room it belongs in.
        ///
        /// It is deliberately OFF the roll and off the personal file: attention is
        /// rationed there and a column of numbers would let the player skip learning
        /// who his men are, which is the whole design. This page is where he stands
        /// back and looks at the house, and here the figure is a tool.
        ///
        /// The men and their order are a plain descending sort by
        /// <see cref="Notability.Of"/> - the board is READ and never written, and no
        /// score is cached beside it.
        /// </summary>
        float BuildWhoToLookAt(float cursor)
        {
            cursor = Section(cursor, "III. WHO TO LOOK AT",
                "WHAT THE BOOK IS SHOUTING ABOUT THIS MORNING");

            var roster = director != null ? director.Roster : null;
            var today = OrganizationDay;
            Notability.Top(roster, today, NotableShown, organizationNotable);

            var frame = NewRect("Notable", organizationColumn);
            Fill(frame, LedgerV2.Panel);
            Frame(frame, 1f, LedgerV2.Rule);

            const float pad = 14f;
            const float figureW = 52f;
            const float trendW = 96f;
            const float rowH = 40f;
            var inner = organizationW - pad * 2f;

            if (organizationNotable.Count == 0)
            {
                PlaceTopLeft(frame, 0f, -cursor, organizationW, 46f);
                Line(organizationColumn, LedgerStyle.MonoItalic, 11.5f, LedgerV2.Muted,
                    pad, -(cursor + 14f), inner, 20f,
                    "Nobody is on the books to look at.");
                return cursor + 46f;
            }

            var height = organizationNotable.Count * rowH + 12f;
            PlaceTopLeft(frame, 0f, -cursor, organizationW, height);

            for (var i = 0; i < organizationNotable.Count; i++)
            {
                var man = organizationNotable[i];
                var row = NewRect("Notable " + man.FullName, frame);
                PlaceTopLeft(row, pad, -(6f + i * rowH), inner, rowH);
                if (i < organizationNotable.Count - 1)
                    Rule(row, 0f, -(rowH - 1f), inner, LedgerV2.Hair);

                // The name takes what is left after the figure and the shape of it.
                var nameW = Mathf.Max(80f, inner - figureW - trendW - 16f);
                var name = Line(row, LedgerStyle.Condensed, 15f, LedgerV2.Ink,
                    0f, -2f, nameW, LineBox(15f), man.FullName);
                name.overflowMode = TextOverflowModes.Ellipsis;

                // A man at ninety falling and a man at ninety rising are different
                // problems, and the fold answers both for nothing.
                var trend = Notability.Trend(man, today);
                var shape = trend > 0 ? "CLIMBING" : trend < 0 ? "FALLING AWAY" : "HOLDING";
                Caps(row, inner - figureW - trendW - 8f, -4f, trendW, shape, 9f,
                    trend > 0 ? LedgerV2.Green
                        : trend < 0 ? LedgerStyle.Ballpoint : LedgerV2.Label,
                    2f, TextAlignmentOptions.MidlineRight);

                var score = Notability.Of(man, today);
                var figure = Line(row, LedgerStyle.MonoBold, 15f,
                    score >= Notability.NewsBand ? LedgerV2.Ink : LedgerV2.Muted,
                    inner - figureW, -2f, figureW, LineBox(15f), score.ToString());
                figure.alignment = TextAlignmentOptions.MidlineRight;

                // WHY he is up there, in his own file's words - never re-worded here.
                var cause = Notability.Cause(man);
                var causeText = Line(row, LedgerStyle.Mono, 10f, LedgerV2.Muted,
                    0f, -21f, inner, LineBox(10f),
                    cause.Length > 0 ? cause : "Nothing on his file yet.");
                causeText.overflowMode = TextOverflowModes.Ellipsis;
            }

            return cursor + height;
        }

        // ------------------------------------------------ word from the crews (001)

        /// <summary>
        /// FOLLOW-001. Every movement of a man's character, with the reason the clerk
        /// wrote for it, on the page where the player looks at his own house.
        ///
        /// EPIC 13's law is that there are no silent modifiers: every effect prints
        /// somewhere. The model has always obeyed it - <c>CampaignRunner.
        /// CharacterChanges</c> carries a written reason for every point that moves -
        /// and until this section nothing read the list, so a man deciding he was done
        /// with us read to the player as a number that fell for nothing.
        ///
        /// The reason is the PAYLOAD and is printed verbatim. The pen says which way it
        /// went - the ballpoint the book already uses for a man of ours who is no
        /// longer ours, the green it already uses for a promotion - and nothing here
        /// composes a sentence of its own.
        /// </summary>
        float BuildWordFromTheCrews(float cursor)
        {
            cursor = Section(cursor, "IV. WORD FROM THE CREWS",
                "WHAT MOVED ON THE MEN, AND WHY");

            var book = outfit ? outfit.ReasonBook : null;
            var frame = NewRect("Word", organizationColumn);
            Fill(frame, LedgerV2.Panel);
            Frame(frame, 1f, LedgerV2.Rule);

            const float pad = 14f;
            const float edgeW = 3f;
            var inner = organizationW - pad * 2f;
            var copyX = edgeW + 8f;
            var copyW = inner - copyX;

            if (book == null || book.Count == 0)
            {
                PlaceTopLeft(frame, 0f, -cursor, organizationW, 46f);
                Line(organizationColumn, LedgerStyle.MonoItalic, 11.5f, LedgerV2.Muted,
                    pad, -(cursor + 14f), inner, 20f,
                    book == null
                        ? "No campaign is running on this sheet."
                        : ReasonText.Quiet);
                return cursor + 46f;
            }

            // Newest DAY first, and inside it the loudest movement first. The two
            // orders pull against each other and walking the flat book backwards gets
            // only the first of them - it reads last night back to front and a limited
            // run then keeps the day's +1s and drops the swings this section exists to
            // show. ReasonFeed.Latest is where that is settled, once, and where the
            // headless suite can hold it.
            ReasonFeed.Latest(book, ReasonsShown, organizationWords);
            var run = organizationWords.Count;
            var y = 8f;
            for (var i = 0; i < run; i++)
            {
                var word = organizationWords[i];
                var ink = word.Rising ? LedgerStyle.GreenOk : LedgerStyle.Ballpoint;

                var head = word.Name + " · " +
                           ReasonText.Movement(word.Trait, word.Delta).ToUpperInvariant();
                var stamp = Caps(frame, pad + copyX, -y, copyW - 90f, head, 10f, ink, 3f);
                stamp.font = LedgerStyle.MonoBold;
                stamp.overflowMode = TextOverflowModes.Ellipsis;
                Caps(frame, pad + copyX, -y, copyW, LedgerText.DayStamp(word.Day), 9f,
                    LedgerV2.Label, 2f, TextAlignmentOptions.MidlineRight);

                // MEASURED, not assumed: a reason is a sentence somebody wrote and the
                // long ones run to two lines at this column's width. TMP's ellipsis
                // eats a whole line when the rect cannot hold what it was given, so the
                // rect is sized to what the face says it needs.
                var copy = Paragraph(frame, LedgerStyle.Mono, 11f, LedgerV2.Body,
                    pad + copyX, -(y + 16f), copyW, LineBox(11f), word.Reason,
                    lineSpacing: 0f);
                var tall = Mathf.Max(LineBox(11f),
                    Mathf.Ceil(copy.GetPreferredValues(word.Reason, copyW, 0f).y));
                copy.rectTransform.sizeDelta = new Vector2(copyW, tall);

                var height = 16f + tall + 8f;
                Block("Pen", frame, pad, -y, edgeW, height - 6f, ink);
                y += height;
                if (i < run - 1)
                    Rule(frame, pad, -(y - 4f), inner, LedgerV2.Hair);
            }

            var total = y + 8f;
            PlaceTopLeft(frame, 0f, -cursor, organizationW, total);
            return cursor + total;
        }

        // --------------------------------------------------------------- filed orders

        float BuildFiledOrders(float cursor)
        {
            cursor = Section(cursor, "II. ORDERS FILED WITH THE OUTFIT",
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
        /// <summary>What one meter stands, label to note. Independent of where it is
        /// put, so a card can be measured before its column is drawn.</summary>
        static float MeterHeight(float labelSize = 10f) =>
            LineBox(labelSize) + 4f + MeterTrackH + 3f + LineBox(10f);

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

        void ToggleDetail()
        {
            organizationDetailOpen = !organizationDetailOpen;
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
            blocksMenu = blocksMenu == blockId
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
                SayOnThisSheet(immediate.Ruling);
                dirty = true;
                return;
            }

            outfit.Filings.File(FilingStamp(), text, resolver);
            SayOnThisSheet(FiledNote);
            dirty = true;
        }

        /// <summary>The sheet the reader is on is the sheet the office's answer belongs
        /// on. ORGANIZATION and BLOCKS file with the same office and each keeps its own
        /// line, so a ruling never appears on a page the reader was not looking at.
        /// </summary>
        void SayOnThisSheet(string note)
        {
            if (currentPage == LedgerPage.Blocks)
                blocksNote = note;
            else
                organizationNote = note;
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

        /// <summary>
        /// Puts one man in front of the Don. Filed like every other posting, and
        /// refused for the same reason a branch refuses one: the detail eats the Boss's
        /// own manpower cap, so a full Boss cannot stand another guard up.
        /// </summary>
        void FileDetailPosting(int hoodId)
        {
            var hood = Person(hoodId);
            if (!hood.IsValid)
                return;

            organizationPickedHoodId = -1;
            FileOrder(hood.Name + " put on the Boss's detail.", () =>
            {
                var query = director != null ? director.Organization : null;
                if (query == null || !query.TryGetBoss(out var boss))
                    return Outfit.FilingRuling.Refuse(LedgerText.ReasonNoBoss);

                var manpower = query.CapacityOf(boss.Id).Manpower;
                if (!Outfit.OutfitFilingRules.AcceptsAnotherMan(manpower))
                    return Outfit.FilingRuling.Refuse(
                        Outfit.OutfitFilingRules.ManRefusal(boss.Name, manpower));

                var result = director.AssignToDetail(hoodId);
                return result.Ok
                    ? Outfit.FilingRuling.Grant("he stands with the Don from today")
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

            blocksMenu = default;
            ClearBlocksPendingBlock();
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

            blocksMenu = default;
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
                SayOnThisSheet("the block had no valid name on it");
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


        bool CloseOrganizationTransient()
        {
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
            organizationPickedHoodId = -1;
            HideThumbNote();
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

        /// <summary>Is he one of the men standing in front of the Don?</summary>
        bool IsOnTheDetail(int id)
        {
            var detail = director != null ? director.BodyguardDetail() : null;
            return detail != null && detail.HoodIds.Contains(id);
        }

        /// <summary>What a man is actually doing, in the sheet's own words.</summary>
        string HoodDuty(OrganizationPerson person)
        {
            if (!person.IsAvailable)
                return person.Status.ToString().ToLowerInvariant() + " · off the books";
            if (person.Rank != Rank.Hood)
                return "commands a branch";
            if (person.Assignment == AssignmentKind.Pool)
                return "no post · earning nothing";
            if (IsOnTheDetail(person.Id))
                return "stands with the Don";
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

        static void SetActionEnabled(TMP_Text label, bool enabled) =>
            LedgerV2.KeyEnabled(label, enabled);
    }
}
