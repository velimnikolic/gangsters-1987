using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using LivingCity.Gangs;
using LivingCity.Personnel;
using LivingCity.Territory;
using RoadDemo;
using static LivingCity.UI.LedgerKit;

namespace LivingCity.UI
{
    /// <summary>
    /// BLOCKS is the sheet about GROUND: what we hold on the street, what it takes in,
    /// and who stands on it.
    ///
    /// It was a section of ORGANIZATION until 2026-09-02, squeezed into the right half
    /// of a page that is really about men. The block is the unit the whole game is
    /// played in, so it has its own leaf now: the ledger of the blocks around us down
    /// the left with the city under it and the wire under that, and the BLOCK FILE -
    /// the filmed block, its arrangement, its doors and its men - standing on the right
    /// where it does not push the ledger off the page.
    ///
    /// Nothing about a block is composed here. The paper is the organization's
    /// (<see cref="ReadOrganizationRoll"/>), the street's reading is the territory
    /// layer's, and every verb is FILED with the outfit exactly as it was - this sheet
    /// only asks. The file itself is <see cref="BuildBlockFile"/>, unchanged and moved
    /// whole, film and all.
    /// </summary>
    public sealed partial class PersonnelAlmanac
    {
        // ------------------------------------------------------------------ the state

        /// <summary>The block whose "who answers" menu is down, if any.</summary>
        TerritoryBlockId blocksMenu;

        /// <summary>True while the ledger is too narrow for its five columns, so every
        /// block row is drawn as a card that reads downward. Set once per repaint, in
        /// BuildBlockLedger, and read by the row builder under it.</summary>
        bool blocksStackedRows;

        // Map targeting is intentionally transient: the reader picks a block anywhere in
        // the city, and the sheet brings it back with its menu already down. Nothing is
        // filed until a name is chosen out of that menu.
        bool blocksTargeting;
        TerritoryBlockId blocksPendingBlock;
        string blocksPendingName = "";

        readonly List<TerritoryBlockId> blocksRows = new List<TerritoryBlockId>();

        /// <summary>Blocks one of our men is standing on right now. A block earns a line
        /// on this sheet by being on our paper, being ours on the street, or having our
        /// men on it - the third is how ground the outfit is working shows up before a
        /// single deed changes hands.</summary>
        readonly HashSet<TerritoryBlockId> blocksOurStreets =
            new HashSet<TerritoryBlockId>();

        /// <summary>What the sheet last had to say for itself - a map pick landing, a
        /// filing going in, a refusal coming back.</summary>
        string blocksNote = "";

        bool BlocksTargetingActive => blocksTargeting;

        // ----------------------------------------------------------------- the layout

        static float BlocksTop;
        static float BlocksHeight;

        static void MeasureBlocksLayout()
        {
            BlocksTop = PageTop - 76f;
            BlocksHeight = -(PageBottom - BlocksTop);
        }

        /// <summary>Under this the sheet runs one column: the ledger, then the file
        /// under it. Above it the ledger goes down the left and the open block's file
        /// stands beside it, which is the design's own arrangement.</summary>
        const float BlocksTwoColumn = 1180f;

        /// <summary>The design's own two columns: equal measures with 28 units of air
        /// between them (minmax(460px, 1fr) twice).</summary>
        const float BlocksGutter = 28f;

        /// <summary>Air above the first section, and between one section and the next -
        /// the design's 20 and 26.</summary>
        const float BlocksSectionTop = 20f;
        const float BlocksSectionGap = 26f;

        /// <summary>Under this the block ledger stops being a five-column table: its
        /// headings and the key on the end need about this much, and less than that
        /// makes every column narrower than the words standing in it.</summary>
        const float BlocksTableWidth = 620f;

        /// <summary>The key that changes a block's paper. Held short on purpose - the
        /// design's is a small bordered word, not a banner across the row's end.</summary>
        const float BlocksActionColumn = 146f;

        /// <summary>The block ledger is a reading, not a census: past this many rows the
        /// sheet says so and sends the reader to the map rather than printing the city.
        /// </summary>
        const int BlockRowLimit = 12;

        /// <summary>How many of the city's blocks the second list prints before it stops
        /// and says how many more there are. The whole city is the map's business.</summary>
        const int BlocksCityLimit = 24;

        /// <summary>How many slips of the wire stand on this sheet.</summary>
        const int BlocksWireLimit = 12;

        RectTransform blocksFixed;
        internal RectTransform blocksViewport;
        internal RectTransform blocksContent;
        internal float blocksScroll;

        /// <summary>
        /// The column the sections are being laid into, and how wide it is. Every
        /// section builder places against THESE and never against the page, so a section
        /// can be dropped into either column without knowing which it is in.
        /// </summary>
        RectTransform blocksColumn;
        float blocksW;

        readonly List<WireLine> blocksWire = new List<WireLine>();

        // ------------------------------------------------------------------- the page

        void BuildBlocksPage(RectTransform sheet)
        {
            var root = NewPageRoot(sheet, LedgerPage.Blocks);
            blocksFixed = NewRect("Blocks Fixed", root);
            Stretch(blocksFixed);

            blocksViewport = NewRect("Blocks Window", root);
            PlaceTopLeft(blocksViewport, PageLeft, BlocksTop, PageWidth, BlocksHeight);
            blocksViewport.gameObject.AddComponent<RectMask2D>();

            blocksContent = NewRect("Blocks File", blocksViewport);
            blocksContent.anchorMin = new Vector2(0f, 1f);
            blocksContent.anchorMax = new Vector2(1f, 1f);
            blocksContent.pivot = new Vector2(0f, 1f);
            blocksContent.anchoredPosition = Vector2.zero;
            blocksContent.sizeDelta = new Vector2(0f, BlocksHeight);
        }

        void RebuildBlocks()
        {
            if (!blocksFixed || !blocksContent)
                return;

            HideThumbNote();

            if (blocksNote == FiledNote && outfit && outfit.Filings.AwaitingCount == 0)
                blocksNote = "";

            // The live plate is not page furniture. Park it directly under the scrolling
            // content while the old columns are torn down, then BuildBlockModel puts that
            // SAME view into the new plate. The camera and RenderTexture therefore survive
            // ordinary observation/roster repaints instead of being recreated with them.
            ParkBlockModelForRebuild();
            foreach (Transform old in blocksFixed)
                Destroy(old.gameObject);
            foreach (Transform old in blocksContent)
                if (blockCardModel == null || old != blockCardModel.transform)
                    Destroy(old.gameObject);

            LedgerV2.PageHead(blocksFixed, PageLeft, PageTop, PageWidth, "BLOCKS",
                "WHAT WE HOLD ON THE STREET · WHAT IT TAKES IN · WHO STANDS ON IT");
            if (!string.IsNullOrEmpty(blocksNote))
                LedgerV2.Mono(blocksFixed, PageRight - 700f, PageTop - 34f, 700f,
                    blocksNote, 10f, LedgerV2.PaperBlue, 2f,
                    TextAlignmentOptions.MidlineRight);

            ReadOrganizationRoll(director != null ? director.Organization : null);
            CollectBlockRows();

            // The file stands against a block on the ledger. Lose the row - the name was
            // struck off and the street took the ground back - and the file closes with
            // it rather than standing beside a ledger that no longer mentions the block.
            if (blockCardId.IsValid && !blocksRows.Contains(blockCardId))
            {
                blockCardId = default;
                StopBlockFilm();
            }

            var cursor = 0f;
            if (PageWidth >= BlocksTwoColumn)
            {
                var span = PageWidth - BlocksGutter;
                var half = span * 0.5f;

                InBlocksColumn(0f, half);
                var ledger = BuildBlockLedger(BlocksSectionTop);
                ledger = BuildCityBlocks(ledger + BlocksSectionGap);
                ledger = BuildBlockWire(ledger + BlocksSectionGap);
                ledger = BuildStreetJobs(ledger + BlocksSectionGap);

                InBlocksColumn(half + BlocksGutter, span - half);
                var file = BuildBlockDetails(BlocksSectionTop);

                cursor = Mathf.Max(ledger, file);
            }
            else
            {
                InBlocksColumn(0f, PageWidth);
                cursor = BuildBlockLedger(BlocksSectionTop);
                cursor = BuildBlockDetails(cursor + BlocksSectionGap);
                cursor = BuildCityBlocks(cursor + BlocksSectionGap);
                cursor = BuildBlockWire(cursor + BlocksSectionGap);
                cursor = BuildStreetJobs(cursor + BlocksSectionGap);
            }

            CloseBlocks(cursor);
            FinishBlockModelRebuild();
        }

        void InBlocksColumn(float x, float width)
        {
            blocksColumn = NewRect("Column", blocksContent);
            PlaceTopLeft(blocksColumn, x, 0f, width, 1f);
            blocksW = width;
        }

        void CloseBlocks(float cursor)
        {
            var contentHeight = Mathf.Max(BlocksHeight, cursor + 28f);
            blocksContent.sizeDelta = new Vector2(0f, contentHeight);
            blocksScroll = Mathf.Clamp(
                blocksScroll, 0f, Mathf.Max(0f, contentHeight - BlocksHeight));
            blocksContent.anchoredPosition = new Vector2(0f, blocksScroll);
        }

        /// <summary>A section head in the blocks column - the design's 19-point heading
        /// with its aside held to the right over a hairline. Answers the cursor below.
        /// </summary>
        float BlocksSection(float cursor, string title, string aside)
        {
            var y = LedgerV2.Section(blocksColumn, 0f, -cursor, blocksW, title, aside);
            return -y;
        }

        // ------------------------------------------------------------ the block details

        /// <summary>The right-hand column: the open block's whole file, or the plate
        /// that says a block has to be picked before there is one.</summary>
        float BuildBlockDetails(float cursor)
        {
            cursor = BlocksSection(cursor, "BLOCK DETAILS",
                blockCardId.IsValid
                    ? "DRAG THE BLOCK TO TURN IT · CLICK A DOOR"
                    : "ONE BLOCK AT A TIME");

            if (blockCardId.IsValid)
                return BuildBlockFile(cursor);

            var empty = NewRect("No block", blocksColumn);
            PlaceTopLeft(empty, 0f, -cursor, blocksW, 74f);
            Fill(empty, LedgerV2.Panel);
            Frame(empty, 1f, LedgerV2.Rule);
            Caps(empty, 14f, -30f, blocksW - 28f,
                "— PICK A BLOCK FROM THE LEDGER —", 11f, LedgerV2.Muted, 8f,
                TextAlignmentOptions.Midline);
            return cursor + 86f;
        }

        // --------------------------------------------------------- all blocks in the city

        /// <summary>
        /// EVERY block the geography knows, and who holds it - the ledger above prints
        /// only the ones we have something to do with, and a boss who cannot see the
        /// rest cannot see where to go next. One line each: the street's colour, the
        /// name, the ward, and the house whose paper it is on.
        /// </summary>
        float BuildCityBlocks(float cursor)
        {
            var query = TerritoryRuntime.Instance?.PlayerQuery;
            var ids = query?.BlockIds;
            var total = ids != null ? ids.Count : 0;

            cursor = BlocksSection(cursor, "ALL BLOCKS IN THE CITY",
                total > 0 ? total + " ON THE GEOGRAPHY" : "NO GEOGRAPHY IN THIS SCENE");

            if (total == 0)
            {
                Line(blocksColumn, LedgerStyle.MonoItalic, 12f, LedgerV2.Muted,
                    0f, -cursor, blocksW, 20f,
                    "This scene stands up no canonical city to list.");
                return cursor + 32f;
            }

            // The name is a condensed gothic and TMP drops an ellipsised line WHOLE
            // when its rect cannot hold the line box, so both lines are measured with
            // LineBox and the card is made tall enough for the two of them. A card cut
            // to the point size printed the ward and no name at all.
            var nameH = LineBox(13f);
            var wardH = LineBox(9f);
            var cardH = nameH + wardH + 4f;
            const float gutter = 7f;
            var columns = Mathf.Max(1,
                Mathf.FloorToInt((blocksW + gutter) / (210f + gutter)));
            var cardW = (blocksW - gutter * (columns - 1)) / columns;
            var shown = Mathf.Min(total, BlocksCityLimit);

            for (var i = 0; i < shown; i++)
            {
                var blockId = ids[i];
                var control = ControlOf(blockId);
                var card = NewRect("City " + blockId.Value, blocksColumn);
                PlaceTopLeft(card,
                    i % columns * (cardW + gutter),
                    -(cursor + i / columns * (cardH + gutter)),
                    cardW, cardH);
                Fill(card, LedgerV2.Panel);
                RowButton(card, ClickSurface(card), () => OpenBlockCard(blockId));

                var ink = ControlColour(control);
                Block("Dot", card, 10f, -(cardH - 9f) * 0.5f, 9f, 9f, ink);
                var holder = HolderOf(blockId);
                var holderW = Mathf.Min(96f, holder.Length * 6.4f + 10f);
                var textW = cardW - 28f - holderW - 9f;
                Line(card, LedgerStyle.Condensed, 13f, LedgerV2.Ink,
                    28f, -3f, textW, nameH, BlockName(blockId))
                    .overflowMode = TextOverflowModes.Ellipsis;
                Line(card, LedgerStyle.Mono, 9f, LedgerV2.Label,
                    28f, -(nameH + 1f), textW, wardH, NeighborhoodOf(blockId))
                    .overflowMode = TextOverflowModes.Ellipsis;
                Caps(card, cardW - 10f - holderW, -(cardH - 15.5f) * 0.5f, holderW,
                        holder, 9.5f, ink, 4f, TextAlignmentOptions.MidlineRight)
                    .font = LedgerStyle.MonoBold;
            }

            var lines = (shown + columns - 1) / columns;
            cursor += lines * (cardH + gutter);

            if (total > shown)
            {
                Caps(blocksColumn, 0f, -(cursor + 2f), blocksW,
                    "AND " + (total - shown) +
                    " MORE IN THE CITY · OPEN THE MAP TO READ THEM ALL",
                    9f, LedgerV2.Label, 3f);
                cursor += 22f;
            }
            return cursor + 16f;
        }

        /// <summary>The house a block's deeds put it under, in one word. The territory
        /// layer composes it (TerritoryPresentationProfile); this only reads it back.
        /// </summary>
        string HolderOf(TerritoryBlockId blockId)
        {
            var query = TerritoryRuntime.Instance?.PlayerQuery;
            if (query == null || !query.TryGetBlock(blockId, out var block) || block == null)
                return "UNKNOWN";
            var profile = TerritoryPresentationProfile.Default;
            return block.Holding == profile.Holding.NoneLabel ||
                   block.Holding == profile.Holding.UnknownLabel
                ? "OPEN"
                : block.Holding;
        }

        // ------------------------------------------------------------- jobs on the street

        /// <summary>
        /// THE OUTFIT'S OPEN WORK, on the sheet the ground is read on. It is the same
        /// order book ORGANIZATION files into (<see cref="Outfit.OrderBook"/>) - this
        /// only reads it back, because a job is a thing done TO a block and the block is
        /// what the reader is looking at. A book with nothing live in it prints nothing
        /// at all rather than an empty table.
        /// </summary>
        float BuildStreetJobs(float cursor)
        {
            var book = outfit != null ? outfit.Book : null;
            if (book == null)
                return cursor;

            var live = 0;
            for (var i = 0; i < book.Jobs.Count; i++)
                if (book.Jobs[i].Live)
                    live++;
            if (live == 0)
                return cursor;

            cursor = BlocksSection(cursor, "JOBS ON THE STREET",
                live + (live == 1 ? " JOB OUT" : " JOBS OUT"));

            const float rowH = 51f;
            var frame = NewRect("Jobs", blocksColumn);
            PlaceTopLeft(frame, 0f, -cursor, blocksW, live * rowH);
            Fill(frame, LedgerV2.Panel);
            Frame(frame, 1f, LedgerV2.Rule);

            var kindW = Mathf.Min(110f, blocksW * 0.2f);
            var stateW = 96f;
            var menW = 70f;
            var laid = 0;
            for (var i = 0; i < book.Jobs.Count && laid < live; i++)
            {
                var job = book.Jobs[i];
                if (!job.Live)
                    continue;

                var row = NewRect("Job " + job.Id, frame);
                PlaceTopLeft(row, 0f, -(laid * rowH), blocksW, rowH);
                if (laid > 0)
                    Rule(row, 0f, 0f, blocksW, LedgerV2.Rule);

                Caps(row, 18f, -20f, kindW - 14f,
                    LedgerText.OrderLabel(job.Type), 10.5f, LedgerV2.Ink, 6f)
                    .font = LedgerStyle.MonoBold;

                var textW = blocksW - kindW - stateW - menW - 60f;
                Line(row, LedgerStyle.Condensed, 14.5f, LedgerV2.Ink,
                    18f + kindW, -11f, textW, 18f,
                    job.TargetLabel.Length > 0 ? job.TargetLabel : "the street")
                    .overflowMode = TextOverflowModes.Ellipsis;
                Line(row, LedgerStyle.Mono, 10f, LedgerV2.Label,
                    18f + kindW, -29f, textW, 14f,
                    job.TargetBlockId >= 0 ? "block " + job.TargetBlockId
                        : job.BlockTargets.Count > 0
                            ? job.BlockTargets.Count + " blocks"
                            : "no address on the order")
                    .overflowMode = TextOverflowModes.Ellipsis;

                LedgerV2.Mono(row, 32f + kindW + textW, -19f, menW,
                    job.Men + (job.Men == 1 ? " man" : " men"), 11f, LedgerV2.Muted, 1f);

                // The design's state is a filled chip, not a word: a job's stage is the
                // one thing on the row a reader finds without reading.
                var stage = StageWord(job.Stage, out var stageInk, out var stageGround);
                var chip = NewRect("Stage", row);
                PlaceTopLeft(chip, blocksW - 18f - stateW, -15f, stateW, 21f);
                Fill(chip, stageGround);
                Caps(chip, 0f, -6f, stateW, stage, 10f, stageInk, 6f,
                    TextAlignmentOptions.Center).font = LedgerStyle.MonoBold;
                laid++;
            }

            return cursor + live * rowH + 16f;
        }

        /// <summary>Where a job has got to, in the one word its chip prints - and the
        /// two inks the chip is printed in.</summary>
        static string StageWord(Outfit.JobStage stage, out Color ink, out Color ground)
        {
            switch (stage)
            {
                case Outfit.JobStage.Travelling:
                    ink = LedgerV2.Amber;
                    ground = LedgerV2.Picked;
                    return "ON THE WAY";
                case Outfit.JobStage.Working:
                    ink = LedgerV2.HeadCream;
                    ground = LedgerV2.Green;
                    return "AT THE DOOR";
                case Outfit.JobStage.Finished:
                    ink = LedgerV2.Muted;
                    ground = LedgerV2.PanelDark;
                    return "DONE";
                default:
                    ink = LedgerV2.Label;
                    ground = LedgerV2.PanelDark;
                    return "IN HIS BOOK";
            }
        }

        // --------------------------------------------------------- word from the blocks

        /// <summary>
        /// The wire, on the sheet the ground is read on. It is the same book the rail
        /// beside it and the strip over the street print (<see cref="WireBook"/>) - an
        /// EVENT with an hour on it, never a state. What a door's standing IS belongs
        /// on the door's own row in the file, and is never repeated here.
        /// </summary>
        float BuildBlockWire(float cursor)
        {
            WireBook.Collect(outfit, blocksWire);
            var total = blocksWire.Count;

            cursor = BlocksSection(cursor, "WORD FROM THE BLOCKS",
                total > 0 ? total + " SLIPS ON THE BOOKS" : "QUIET");

            if (total == 0)
            {
                Line(blocksColumn, LedgerStyle.MonoItalic, 12f, LedgerV2.Muted,
                    0f, -cursor, blocksW, 20f,
                    "Nothing has come off the blocks yet.");
                return cursor + 32f;
            }

            const float rowH = 31f;
            var shown = Mathf.Min(total, BlocksWireLimit);
            var frame = NewRect("Wire", blocksColumn);
            PlaceTopLeft(frame, 0f, -cursor, blocksW, shown * rowH);
            Fill(frame, LedgerV2.Panel);
            Frame(frame, 1f, LedgerV2.Rule);

            for (var i = 0; i < shown; i++)
            {
                var slip = blocksWire[i];
                var row = NewRect("Slip " + i, frame);
                PlaceTopLeft(row, 0f, -(i * rowH), blocksW, rowH);
                if (i > 0)
                    Rule(row, 0f, 0f, blocksW, LedgerV2.Rule);

                Caps(row, 12f, -9f, 52f, slip.Stamp, 9.5f, LedgerV2.Label, 4f);
                Block("Ink", row, 75f, -11f, 9f, 9f, slip.Ink);
                Line(row, LedgerStyle.Mono, 11f, LedgerV2.Copy,
                    95f, -8f, blocksW - 95f - 100f, 16f, slip.Body)
                    .overflowMode = TextOverflowModes.Ellipsis;
                Caps(row, blocksW - 96f, -9f, 84f, slip.Tag, 9f, slip.Ink, 8f,
                    TextAlignmentOptions.MidlineRight);
            }

            cursor += shown * rowH;
            if (total > shown)
            {
                Caps(blocksColumn, 0f, -(cursor + 6f), blocksW,
                    "AND " + (total - shown) + " OLDER SLIPS · THE RAIL KEEPS THEM ALL",
                    9f, LedgerV2.Label, 3f);
                cursor += 22f;
            }
            return cursor + 16f;
        }

        // -------------------------------------------------------- blocks around us

        /// <summary>
        /// The ledger: every block we have something to do with, one row each - the
        /// name and its ward, whose PAPER it is on, what the STREET says, and the
        /// reading that says what the two add up to. The key on the end of the row
        /// changes the paper; the row itself opens the block's file beside it.
        /// </summary>
        float BuildBlockLedger(float cursor)
        {
            // No key over this head any more: ALL BLOCKS IN THE CITY stands under the
            // ledger and prints every block the geography knows, so a second way into
            // the same list was only a second thing to read.
            cursor = BlocksSection(cursor, "BLOCKS AROUND US", "");

            // Five headings and the key need about six hundred units. Under that the
            // table stops being a table: each block becomes a small card that reads
            // down instead of across, and the head band goes with the columns it named.
            blocksStackedRows = blocksW < BlocksTableWidth;
            float[] columns = null;
            if (!blocksStackedRows)
            {
                var action = BlocksActionColumn;
                var span = blocksW - action;
                var c0 = span * 1.5f / 5.3f;
                var c1 = span * 1.2f / 5.3f;
                var c2 = span * 1.1f / 5.3f;
                var c3 = span - c0 - c1 - c2;
                columns = new[] { c0, c1, c2, c3, action };

                var head = NewRect("Ledger head", blocksColumn);
                PlaceTopLeft(head, 0f, -cursor, blocksW, 25f);
                Fill(head, LedgerV2.Head);
                var headings = new[] { "BLOCK", "PAPER", "STREET", "READING", "" };
                var headColours = new[]
                {
                    LedgerV2.HeadInk, LedgerV2.HeadPaper, LedgerV2.HeadStreet,
                    LedgerV2.HeadInk, LedgerV2.HeadInk,
                };
                var x = 0f;
                for (var i = 0; i < headings.Length; i++)
                {
                    if (headings[i].Length > 0)
                        Caps(head, x + 10f, -7f, columns[i] - 16f, headings[i], 9.5f,
                            headColours[i], 6f).font = LedgerStyle.MonoBold;
                    x += columns[i];
                }
                cursor += 25f;
            }

            if (blocksRows.Count == 0)
            {
                var empty = NewRect("Ledger empty", blocksColumn);
                PlaceTopLeft(empty, 0f, -cursor, blocksW, 44f);
                Fill(empty, LedgerV2.Panel);
                Frame(empty, 1f, LedgerV2.Rule);
                Line(empty, LedgerStyle.MonoItalic, 12f, LedgerV2.Muted,
                    14f, -12f, blocksW - 28f, 20f,
                    "No block is on our paper and none is ours on the street.");
                return cursor + 56f;
            }

            var frame = NewRect("Ledger", blocksColumn);
            PlaceTopLeft(frame, 0f, -cursor, blocksW, 1f);
            Frame(frame, 1f, LedgerV2.Rule);

            var rows = Mathf.Min(blocksRows.Count, BlockRowLimit);
            var height = 0f;
            for (var i = 0; i < rows; i++)
                height = BuildBlockRow(blocksRows[i], columns, cursor, height);

            // The open block keeps its row even when the ledger has stopped printing
            // them: a list cut at twelve must not drop the one block the reader is
            // actually looking at.
            if (blockCardId.IsValid && blocksRows.IndexOf(blockCardId) >= rows)
                height = BuildBlockRow(blockCardId, columns, cursor, height);

            frame.sizeDelta = new Vector2(blocksW, height);
            cursor += height;

            if (blocksRows.Count > rows)
            {
                Caps(blocksColumn, 0f, -(cursor + 6f), blocksW,
                    "AND " + (blocksRows.Count - rows) +
                    " MORE ON THE BOOKS · OPEN THE MAP TO READ THE WHOLE CITY",
                    9f, LedgerV2.Label, 3f);
                cursor += 24f;
            }
            return cursor + 16f;
        }

        float BuildBlockRow(
            TerritoryBlockId blockId, float[] columns, float top, float offset)
        {
            if (blocksStackedRows)
                return BuildStackedBlockRow(blockId, top, offset);

            const float rowH = 43f;
            var leaderId = organizationPaper.TryGetValue(blockId, out var id) ? id : -1;
            var leader = Leader(leaderId);
            var control = ControlOf(blockId);
            var mismatch = leader.IsValid && control == BlockControl.NotOurs;
            var orphan = !leader.IsValid && control == BlockControl.Held;
            var menuOpen = blocksMenu == blockId;

            var open = blockCardId == blockId;

            var row = NewRect("Block " + blockId.Value, blocksColumn);
            PlaceTopLeft(row, 0f, -(top + offset), blocksW, rowH);
            Fill(row, menuOpen
                ? LedgerV2.Money
                : mismatch || orphan ? LedgerV2.Carbon : LedgerV2.Panel);
            // The row itself opens the block's file. The key at the right end still only
            // changes the paper, so a reader after one thing never gets the other.
            RowButton(row, ClickSurface(row), () => OpenBlockCard(blockId));
            if (open)
                Block("Open mark", row, 0f, 0f, 3f, rowH, LedgerV2.Red);
            Rule(row, 0f, 0f, blocksW, LedgerV2.Rule);

            // ONE centre line down the whole row. A TMP line is centred on the geometry
            // it actually draws, so two boxes that share a centre put their words on one
            // line however tall the boxes are - the name and its ward stand on it as a
            // pair, and every mark, word, the reading and the key take the same middle.
            const float nameH = 18f;
            const float wardH = 14f;
            const float pairTop = -(rowH - nameH - wardH) * 0.5f;
            const float wordH = 16f;
            const float wordTop = -(rowH - wordH) * 0.5f;

            var x = 0f;
            Line(row, LedgerStyle.Condensed, 14f, LedgerV2.Ink,
                x + 10f, pairTop, columns[0] - 18f, nameH, BlockName(blockId))
                .overflowMode = TextOverflowModes.Ellipsis;
            Line(row, LedgerStyle.Mono, 9.5f, LedgerV2.Label,
                x + 10f, pairTop - nameH, columns[0] - 18f, wardH,
                NeighborhoodOf(blockId))
                .overflowMode = TextOverflowModes.Ellipsis;
            x += columns[0];

            var paperColour = leader.IsValid ? LedgerV2.PaperBlue : LedgerV2.Red;
            // Hatched, not solid: the design draws what is written on PAPER as a
            // ruled square and what is true on the STREET as a filled one, so the two
            // columns can never be read for each other at a glance.
            LedgerV2.PaperMark(row, x + 10f, LedgerV2.MarkY(wordTop, wordH), paperColour);
            Line(row, LedgerStyle.MonoBold, 11f, paperColour,
                x + 26f, wordTop, columns[1] - 36f, wordH,
                leader.IsValid ? leader.Name : "NOBODY NAMED")
                .overflowMode = TextOverflowModes.Ellipsis;
            x += columns[1];

            var streetColour = ControlColour(control);
            LedgerV2.StreetMark(row, x + 10f, LedgerV2.MarkY(wordTop, wordH), streetColour);
            Line(row, LedgerStyle.MonoBold, 11f, streetColour,
                x + 26f, wordTop, columns[2] - 36f, wordH, ControlWord(control))
                .overflowMode = TextOverflowModes.Ellipsis;
            x += columns[2];

            // ONE line, cut with an ellipsis. The design gives the reading a single
            // measure across the row and the whole sentence stands in the file.
            var reading = Reading(
                leader, control, blocksOurStreets.Contains(blockId),
                out var readingColour);
            var readingText = LedgerV2.Mono(row, x + 10f,
                -(rowH - LineBox(10.5f)) * 0.5f, columns[3] - 18f,
                reading, 10.5f, readingColour, 4f);
            readingText.overflowMode = TextOverflowModes.Ellipsis;
            x += columns[3];

            var label = menuOpen ? "CLOSE"
                : leader.IsValid ? "CHANGE WHO ANSWERS" : "NAME SOMEONE";
            LedgerV2.Button(row, label, x + 8f, -10f, columns[4] - 16f, 23f,
                () => ToggleBlockMenu(blockId),
                menuOpen ? LedgerV2.Key.Dark : LedgerV2.Key.Outline, 9.5f);

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
            var menuOpen = blocksMenu == blockId;
            var open = blockCardId == blockId;

            var row = NewRect("Block " + blockId.Value, blocksColumn);
            PlaceTopLeft(row, 0f, -(top + offset), blocksW, rowH);
            Fill(row, menuOpen
                ? LedgerV2.Money
                : mismatch || orphan ? LedgerV2.Carbon : LedgerV2.Panel);
            // The card itself opens the block's file. The key still only changes the
            // paper, so a reader after one thing never gets the other.
            RowButton(row, ClickSurface(row), () => OpenBlockCard(blockId));
            if (open)
                Block("Open mark", row, 0f, 0f, 3f, rowH, LedgerV2.Red);
            Rule(row, 0f, 0f, blocksW, LedgerV2.Rule);

            var keyW = Mathf.Min(190f, blocksW * 0.42f);
            var label = menuOpen ? "CLOSE"
                : leader.IsValid ? "CHANGE WHO ANSWERS" : "NAME SOMEONE";
            LedgerV2.Button(row, label, blocksW - pad - keyW, -10f, keyW, 26f,
                () => ToggleBlockMenu(blockId), red: false, outline: !menuOpen, size: 9f);

            var titleW = Mathf.Max(60f, blocksW - pad * 2f - keyW - 12f);
            Line(row, LedgerStyle.Condensed, 17f, LedgerV2.Ink,
                pad, -9f, titleW, 22f, BlockName(blockId))
                .overflowMode = TextOverflowModes.Ellipsis;
            Line(row, LedgerStyle.Mono, 10f, LedgerV2.Label,
                pad, -30f, titleW, 16f, NeighborhoodOf(blockId))
                .overflowMode = TextOverflowModes.Ellipsis;

            // Hatched, not solid: what is written on PAPER is a ruled square and what is
            // true on the STREET is a filled one, so the two can never be read for each
            // other at a glance - the marks carry that here, without the headings.
            var half = (blocksW - pad * 2f) * 0.5f;
            var paperColour = leader.IsValid ? LedgerV2.PaperBlue : LedgerV2.Red;
            LedgerV2.PaperMark(row, pad, LedgerV2.MarkY(-53f, 18f), paperColour);
            Line(row, LedgerStyle.MonoBold, 12f, paperColour,
                pad + 18f, -53f, half - 24f, 18f,
                leader.IsValid ? leader.Name : "NOBODY NAMED")
                .overflowMode = TextOverflowModes.Ellipsis;

            var streetColour = ControlColour(control);
            LedgerV2.StreetMark(row, pad + half, LedgerV2.MarkY(-53f, 18f), streetColour);
            Line(row, LedgerStyle.MonoBold, 12f, streetColour,
                pad + half + 18f, -53f, half - 24f, 18f, ControlWord(control))
                .overflowMode = TextOverflowModes.Ellipsis;

            var reading = Reading(
                leader, control, blocksOurStreets.Contains(blockId),
                out var readingColour);
            Paragraph(row, LedgerStyle.Mono, 10.5f, readingColour,
                pad, -74f, blocksW - pad * 2f, 28f, reading, lineSpacing: 1f);

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
            var width = Mathf.Min(340f, blocksW - 28f);
            var menu = NewRect("Menu " + blockId.Value, blocksColumn);
            PlaceTopLeft(menu, blocksW - width - 14f, -top, width, height);
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


        /// <summary>The blocks this sheet answers for: everything on our paper, and
        /// everything the street says is ours whether it is named or not. The block the
        /// reader has just picked off the map heads the list even when it is neither.
        /// </summary>
        void CollectBlockRows()
        {
            blocksRows.Clear();
            if (blocksPendingBlock.IsValid)
                blocksRows.Add(blocksPendingBlock);

            var query = TerritoryRuntime.Instance?.PlayerQuery;
            if (query == null)
            {
                foreach (var pair in organizationPaper)
                    if (pair.Key != blocksPendingBlock)
                        blocksRows.Add(pair.Key);
                return;
            }

            // Ordered by the geography's own block list, so the sheet reads the same way
            // twice for the same city.

            var ids = query.BlockIds;
            for (var i = 0; i < ids.Count; i++)
            {
                var blockId = ids[i];
                if (blockId == blocksPendingBlock)
                    continue;
                if (!organizationPaper.ContainsKey(blockId) &&
                    !IsOurStreet(ControlOf(blockId)) &&
                    !blocksOurStreets.Contains(blockId))
                    continue;
                blocksRows.Add(blockId);
            }
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
                blocksNote = "canonical map targeting is unavailable";
                dirty = true;
                return;
            }

            blocksTargeting = true;
            ClearBlocksPendingBlock();
            blocksNote = "select one canonical block on the map";
            Close();

            // The book takes the player to the map itself - the turf plate by running
            // the boom out past the map line, the generated city's map by opening it -
            // and the map hands the view back when the pick lands.
            if (!map.CanSummon || !map.Summon())
            {
                blocksTargeting = false;
                blocksNote = map.CanSummon ? "the map could not open" : map.SummonHint;
                OpenAtPage(LedgerPage.Blocks);
                return;
            }

            RefreshTargeting();
        }

        /// <summary>Called by the shared IMapTargetingConsumer dispatch.</summary>
        void CaptureBlockPick(int legacyBlockId)
        {
            var runtime = TerritoryRuntime.Instance;
            if (!BlocksTargetingActive || runtime == null ||
                !runtime.TryGetBlock(legacyBlockId, out var blockId))
                return;

            blocksPendingBlock = blockId;
            blocksPendingName = BlockName(blockId);
            blocksMenu = blockId;
            blocksTargeting = false;
            MapTargeting.Clear(this);
            MapTargeting.Surface?.Dismiss();
            blocksNote = blocksPendingName +
                               " picked · name someone for it below";
            blocksScroll = 0f;
            OpenAtPage(LedgerPage.Blocks);
        }

        void ClearBlocksPendingBlock()
        {
            blocksPendingBlock = default;
            blocksPendingName = "";
        }

        /// <summary>Drops the map pick the sheet was waiting for, without opening
        /// anything - the shell calls this when the book turns a page or dies.</summary>
        void StopBlocksTargeting() => blocksTargeting = false;

        /// <summary>Map Esc has no callback, so the closed Ledger notices the map is gone.</summary>
        void CancelBlocksTargetingAndReturn()
        {
            blocksTargeting = false;
            MapTargeting.Clear(this);
            blocksNote = "block selection cancelled";
            OpenAtPage(LedgerPage.Blocks);
        }

        // ------------------------------------------------------------------ transient

        bool CloseBlocksTransient()
        {
            if (blocksMenu.IsValid)
            {
                blocksMenu = default;
                ClearBlocksPendingBlock();
                dirty = true;
                return true;
            }
            if (blockCardPick.IsValid)
            {
                blockCardPick = default;
                DoorMenu.Forget();
                dirty = true;
                return true;
            }
            return false;
        }

        void DismissBlocksTransient()
        {
            blocksMenu = default;
            ClearBlocksPendingBlock();
            // A shut book films nothing and holds no ground up: the second lens and the
            // streamer's hold on the block both belong to an OPEN file.
            StopBlockFilm();
        }
    }
}
