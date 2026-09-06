using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using LivingCity.Personnel;
using LivingCity.Territory;
using RoadDemo;
using static LivingCity.UI.LedgerKit;

namespace LivingCity.UI
{
    /// <summary>
    /// THE BLOCK DRAWER - the fixed shell down the right of the BLOCKS sheet that holds
    /// the open block: the filmed ground, who is responsible for what on it, and the
    /// three tabs that carry everything else (the design's handoff, 2026-09-06).
    ///
    /// Two things this arrangement fixes, and they are the whole reason it exists.
    ///
    /// RESPONSIBILITY IS NOT AN ORDER. "Who carries the bag" and "who answers for it"
    /// used to sit as rows in WHAT YOU CAN DO, next to SHAKE DOWN THE BLOCK. They are
    /// standing assignments, not verbs, and they are lifted out into their own strip of
    /// three cells that is always readable without opening anything. The three are three
    /// DIFFERENT men: the lieutenant answers for the block on paper, the who-goes crew
    /// executes field orders, and the collector walks the paying doors alone, out of the
    /// crew's street line.
    ///
    /// NOTHING FIT. The file was a column that grew downward until the ledger beside it
    /// was pushed off the page. The drawer is a fixed vertical shell with EXACTLY ONE
    /// scroll region in its ordinary state - the tab body - and the plate, the roles
    /// strip, the tab bar and the action footer are all fixed. The two pickers open as
    /// overlays over the WHOLE shell, plate included, because anchoring a candidate list
    /// under the roles strip leaves it under two rows of room.
    ///
    /// Nothing is composed here. Every figure, refusal and verb is the block file's
    /// (<see cref="ReadBlockFile"/>, <see cref="BlockRacketSeam"/>); this file only says
    /// where each of them stands.
    /// </summary>
    public sealed partial class PersonnelAlmanac
    {
        // ------------------------------------------------------------------ the state

        /// <summary>Which of the three tabs is showing: ORDERS, THE BOOK, THE DOORS.
        /// </summary>
        int blockCardTab;

        /// <summary>The overlay that is down over the drawer, if any. One at a time, by
        /// construction: there is one field and not three flags, so opening one cannot
        /// leave another standing behind it.</summary>
        enum BlockSheet
        {
            None,
            Lieutenant,
            Collector,
            Squad,
        }

        BlockSheet blockCardSheet;

        /// <summary>The drawer's own measure. Everything under the title bar is laid
        /// against THIS and never against the page: the drawer is a column of its own
        /// and does not know how wide the sheet behind it is.</summary>
        float blockCardW;

        /// <summary>The one scroll region of the drawer's ordinary state - the tab body.
        /// </summary>
        internal RectTransform blockTabViewport;
        internal RectTransform blockTabContent;
        internal float blockTabScroll;
        internal TMP_Text blockTabMoreAbove;
        internal TMP_Text blockTabMoreBelow;

        /// <summary>The overlay's own list. A second region, but never at the same time:
        /// an overlay covers the tab body it stands over.</summary>
        internal RectTransform blockSheetViewport;
        internal RectTransform blockSheetContent;
        internal float blockSheetScroll;
        internal TMP_Text blockSheetMoreAbove;
        internal TMP_Text blockSheetMoreBelow;

        // ----------------------------------------------------------------- the layout

        /// <summary>The dark band the drawer opens with: the block's name, its ward and
        /// premises, the chips that say what state it is in, and the way out.</summary>
        const float BlockDrawerHeadH = 50f;

        /// <summary>The three role cells. Fixed: they carry one label, one name and one
        /// affordance each, and a strip that reflowed would move the name a reader is
        /// looking for.</summary>
        const float BlockRolesH = 68f;

        /// <summary>The tab bar and the action footer, both fixed.</summary>
        const float BlockTabBarH = 45f;
        const float BlockFooterH = 51f;

        /// <summary>The tab body is never given less than this. The plate gives room up
        /// before the body does - a drawer whose body cannot hold two rows is a drawer
        /// with no content in it.</summary>
        const float BlockBodyMin = 150f;

        /// <summary>The design's padding inside the drawer: 12 either side of the tab
        /// bodies, the footer and the overlay lists.</summary>
        const float BlockPad = 12f;

        /// <summary>The band the office's last word stands in, over the foot of the tab
        /// body and under the footer's keys.</summary>
        const float BlockSayingH = 30f;

        /// <summary>THE MEASURE ANY SENTENCE IS SET TO. The drawer took two thirds of
        /// the sheet on 2026-09-06 and every serif line in it went to a hundred
        /// characters, which is not a column a reader's eye can return down. Figures,
        /// names and labels still take the whole width - they are scanned, not read.
        /// </summary>
        const float BlockCopyMeasure = 520f;

        /// <summary>A sentence's own width inside a rect of <paramref name="room"/>.
        /// </summary>
        static float CopyMeasure(float room) => Mathf.Min(room, BlockCopyMeasure);

        /// <summary>
        /// A sentence sized to what it actually took, and the height it took.
        ///
        /// TMP TRUNCATES a paragraph to the rect it was handed, so a box guessed at a
        /// line count silently eats the tail - which is what cut "Name a lieutena…" off
        /// the policy panel. preferredHeight reports the whole of it whatever the
        /// overflow mode says, so the rect is grown to match before anything is laid
        /// under it.
        /// </summary>
        static float CopyHeight(TMP_Text copy, float least = 16f)
        {
            var height = Mathf.Max(least, copy.preferredHeight);
            var rect = copy.rectTransform;
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, height);
            return height;
        }

        // ------------------------------------------------------------------ the shell

        /// <summary>
        /// The whole drawer, laid against the sheet's fixed layer so it never scrolls
        /// with the ledger beside it.
        /// </summary>
        void BuildBlockDrawer(RectTransform host)
        {
            blockCardW = BlocksDrawerW;
            // The windows are rebuilt below, or not at all where there is no block and
            // no picker. Forget the old ones first: a wheel read in the same frame as
            // the rebuild would otherwise scroll a rect that is on its way out.
            blockTabViewport = null;
            blockTabContent = null;
            blockSheetViewport = null;
            blockSheetContent = null;
            blockTabMoreAbove = blockTabMoreBelow = null;
            blockSheetMoreAbove = blockSheetMoreBelow = null;

            var x = PageLeft + BlocksLedgerW;
            VRule(host, x, BlocksTop, BlocksHeight, LedgerV2.SheetRule);

            var drawer = NewRect("Block drawer", host);
            PlaceTopLeft(drawer, x + BlocksDrawerRule, BlocksTop, blockCardW,
                BlocksHeight);
            Fill(drawer, LedgerV2.Panel);
            drawer.gameObject.AddComponent<RectMask2D>();

            if (!blockCardId.IsValid)
            {
                BuildEmptyDrawer(drawer);
                return;
            }

            ReadBlockFile();
            BuildDrawerTitle(drawer);

            // THE OVERLAY HOST WRAPS THE PLATE TOO. A picker anchored under the roles
            // strip would open into whatever room the strip left it; anchored here it
            // gets the whole drawer under the title bar, which is what a list of men
            // with two readings each needs.
            var host2 = NewRect("Overlay host", drawer);
            var inner = BlocksHeight - BlockDrawerHeadH;
            PlaceTopLeft(host2, 0f, -BlockDrawerHeadH, blockCardW, inner);

            // The plate gives room up before the tab body does: a short window still
            // has to show two rows of whatever tab is open.
            var plateH = Mathf.Clamp(
                inner - BlockRolesH - BlockTabBarH - BlockFooterH - BlockBodyMin,
                120f, BlockPlateH);
            var y = BuildBlockModel(host2, 0f, plateH);
            y += BuildRolesStrip(host2, y);
            y += BuildTabBar(host2, y);

            // The office's last word takes its room OUT of the tab body rather than
            // covering it: a warning laid over the foot of a scrolling list hides rows
            // that are still under the pointer, and a hidden row is a row a reader
            // presses without reading.
            var footTop = inner - BlockFooterH;
            var sayingH = BlockCardSaying.Length > 0 ? BlockSayingH : 0f;
            var bodyH = Mathf.Max(BlockBodyMin, footTop - sayingH - y);
            BuildTabBody(host2, y, bodyH);
            if (sayingH > 0f)
                BuildDrawerSaying(host2, y + bodyH, sayingH);
            BuildDrawerFooter(host2, footTop);

            switch (blockCardSheet)
            {
                case BlockSheet.Lieutenant:
                    BuildLieutenantSheet(host2, inner);
                    break;
                case BlockSheet.Collector:
                    BuildCollectorSheet(host2, inner);
                    break;
                case BlockSheet.Squad:
                    BuildSquadSheet(host2, inner);
                    break;
            }
        }

        /// <summary>The drawer with no block in it. It keeps its shell rather than
        /// disappearing: a column that comes and goes moves the ledger beside it every
        /// time a reader opens a block.</summary>
        void BuildEmptyDrawer(RectTransform drawer)
        {
            var band = NewRect("Empty head", drawer);
            PlaceTopLeft(band, 0f, 0f, blockCardW, BlockDrawerHeadH);
            Fill(band, LedgerV2.Head);
            Caps(band, BlockPad, -15f, blockCardW - BlockPad * 2f, "NO BLOCK OPEN",
                10.8f, LedgerV2.HeadDim, 18f).font = LedgerStyle.MonoBold;

            var plate = NewRect("Empty plate", drawer);
            PlaceTopLeft(plate, 0f, -BlockDrawerHeadH, blockCardW, BlockPlateH);
            Fill(plate, ModelPlate);
            Caps(plate, 0f, -(BlockPlateH * 0.5f - 8f), blockCardW,
                    "PICK A BLOCK FROM THE LEDGER", 10.8f, ModelCaption, 22f,
                    TextAlignmentOptions.Center)
                .font = LedgerStyle.Mono;

            LedgerV2.Copytext(drawer, BlockPad,
                -(BlockDrawerHeadH + BlockPlateH + 14f),
                CopyMeasure(blockCardW - BlockPad * 2f), 120f,
                "A block's file is the block itself, filmed, with everything the outfit " +
                "knows about the ground under it. Open one from the ledger.", 12.8f,
                LedgerV2.Muted, italic: true);
        }

        // ------------------------------------------------------------- the title bar

        void BuildDrawerTitle(RectTransform drawer)
        {
            var band = NewRect("Drawer head", drawer);
            PlaceTopLeft(band, 0f, 0f, blockCardW, BlockDrawerHeadH);
            Fill(band, LedgerV2.Head);

            var leader = ResponsibleLeader();

            // The way out is the same click the ledger row is: a file is shut by the
            // block it belongs to, and this is that block.
            const float shutW = 22f;
            var shut = NewRect("Shut", band);
            PlaceTopLeft(shut, blockCardW - BlockPad - shutW,
                -(BlockDrawerHeadH - shutW) * 0.5f, shutW, shutW);
            Fill(shut, new Color(0f, 0f, 0f, 0f));
            Frame(shut, 1f, LedgerV2.HeadDim);
            var open = blockCardId;
            RowButton(shut, ClickSurface(shut), () => OpenBlockCard(open));
            Caps(shut, 0f, -3f, shutW, "X", 10.8f, LedgerV2.HeadInk, 0f,
                TextAlignmentOptions.Center).font = LedgerStyle.MonoBold;

            // The chips stand right to left off the way out: the round first, because
            // it is the state that stops an order going, then the vacancy.
            var taken = shutW + 10f;
            if (blockRacketOk && blockRacket.RoundOut)
                taken += Chip("Round is out", LedgerV2.Amber, taken);
            if (!leader.IsValid)
                taken += Chip("Nobody named", LedgerV2.Red, taken);

            var titleW = Mathf.Max(80f, blockCardW - BlockPad * 2f - taken);
            LedgerV2.Name(band, BlockPad, -2f, titleW, BlockName(blockCardId), 17.4f,
                LedgerV2.HeadCream);
            LedgerV2.Mono(band, BlockPad, -25f, titleW,
                    NeighborhoodOf(blockCardId) + " · " + blockCardTrades.Count +
                    " PREMISES · DAY " + RosterDay, 10.8f, LedgerV2.HeadDim, 14f)
                .overflowMode = TextOverflowModes.Ellipsis;

            float Chip(string word, Color ground, float already)
            {
                var w = Mathf.Ceil(LedgerV2.MonoWidth(word, 10.5f, 6f)) + 20f;
                LedgerV2.Status(band, blockCardW - BlockPad - already - w,
                    -(BlockDrawerHeadH - 20f) * 0.5f, w, 20f, word, ground, 10.5f);
                return w + 8f;
            }
        }

        /// <summary>Who this block is on the paper of, or nobody.</summary>
        OrganizationPerson ResponsibleLeader() =>
            Leader(organizationPaper.TryGetValue(blockCardId, out var id) ? id : -1);

        // ----------------------------------------------------------- the roles strip

        /// <summary>
        /// THE CORE OF THE REDESIGN: three cells that say who answers for the block, who
        /// carries its bag and who walks it when an order goes out. Always visible,
        /// never inside a dropdown, and each one opens the picker that changes it.
        /// </summary>
        float BuildRolesStrip(RectTransform host, float top)
        {
            var strip = NewRect("Roles", host);
            PlaceTopLeft(strip, 0f, -top, blockCardW, BlockRolesH);
            Fill(strip, LedgerV2.PanelBand);
            Rule(strip, 0f, -(BlockRolesH - 1f), blockCardW, LedgerV2.Rule);

            var leader = ResponsibleLeader();
            var bagCrewId = ResponsibleCrewId();
            var carried = blockRacketOk && blockRacket.CollectorId >= 0;

            var cellW = (blockCardW - 2f) / 3f;
            Cell(0, "ANSWERS FOR IT",
                leader.IsValid ? leader.Name : "NAME SOMEONE",
                leader.IsValid ? LedgerV2.Ink : LedgerV2.Red,
                BlockSheet.Lieutenant);
            Cell(1, "CARRIES THE BAG",
                carried ? blockRacket.CollectorName
                    : bagCrewId < 0 ? "NO CREW" : "NOBODY",
                carried ? LedgerV2.Ink : LedgerV2.Red,
                BlockSheet.Collector);
            Cell(2, "WHO GOES", WhoGoesWord(out var goesInk), goesInk,
                BlockSheet.Squad);
            return BlockRolesH;

            void Cell(int index, string label, string value, Color ink, BlockSheet sheet)
            {
                var cell = NewRect("Role " + label, strip);
                PlaceTopLeft(cell, index * (cellW + 1f), 0f, cellW, BlockRolesH - 1f);
                var open = blockCardSheet == sheet;
                Fill(cell, open ? LedgerV2.Picked : LedgerV2.Panel);
                // The hairline is drawn ON the cell, never faked with a gap over the
                // container's fill: a strip that fakes its rules shows the container
                // as a grey slab wherever a cell does not reach.
                Frame(cell, 1f, LedgerV2.Rule);
                RowButton(cell, ClickSurface(cell), () => OpenBlockSheet(sheet));

                Caps(cell, 10f, -6f, cellW - 20f, label, 9.6f, LedgerV2.Label, 14f)
                    .font = LedgerStyle.MonoBold;
                LedgerV2.Name(cell, 10f, -19f, cellW - 20f, value, 16.2f, ink);
                Caps(cell, 10f, -44f, cellW - 20f, open ? "CLOSE ×" : "CHANGE ›", 9.6f,
                    LedgerV2.Red, 12f).font = LedgerStyle.Mono;
            }
        }

        /// <summary>The crew whose lieutenant's paper this block is on, or -1.</summary>
        int ResponsibleCrewId() =>
            BlockMissionChoice.ResponsibleCrew(director?.Roster, blockCardId)?.Id ?? -1;

        /// <summary>Who walks the doors when a field order goes out, short enough for
        /// one line of a role cell: "KAMINSKI +CREW", or "VANCE, ALONE".</summary>
        string WhoGoesWord(out Color ink)
        {
            ink = LedgerV2.Ink;
            var roster = director != null ? director.Roster : null;
            if (roster == null)
            {
                ink = LedgerV2.Red;
                return "NO ROSTER";
            }
            if (DoorMenu.SelectedPersonId >= 0)
            {
                var man = roster.Find(DoorMenu.SelectedPersonId);
                return man != null ? man.Surname + ", alone" : "NOBODY";
            }
            var crewId = DoorMenu.SelectedCrewId >= 0
                ? DoorMenu.SelectedCrewId
                : WalkingCrewId();
            var crew = crewId >= 0 ? roster.FindCrew(crewId) : null;
            if (crew == null)
            {
                ink = LedgerV2.Red;
                return "NOBODY";
            }
            var lieutenant = roster.Find(crew.LieutenantId);
            return (lieutenant != null ? lieutenant.Surname : "the crew") + " +crew";
        }

        /// <summary>Opens one picker, or shuts the one that is down. Opening any picker
        /// drops the door that was picked, so the drawer never carries two overlays.
        /// </summary>
        void OpenBlockSheet(BlockSheet sheet)
        {
            blockCardSheet = blockCardSheet == sheet ? BlockSheet.None : sheet;
            if (blockCardSheet != BlockSheet.None)
                blockCardPick = default;
            blockSheetScroll = 0f;
            dirty = true;
        }

        // ------------------------------------------------------------------ the tabs

        static readonly string[] BlockTabs = { "Orders", "The book", "The doors" };

        float BuildTabBar(RectTransform host, float top)
        {
            var bar = NewRect("Tab bar", host);
            PlaceTopLeft(bar, 0f, -top, blockCardW, BlockTabBarH);
            Fill(bar, LedgerV2.PanelBand);
            Rule(bar, 0f, -(BlockTabBarH - 1f), blockCardW, LedgerV2.Rule);
            LedgerV2.Segmented(bar, BlockPad, -9f, 26f, BlockTabs, blockCardTab,
                PickBlockTab, (blockCardW - BlockPad * 2f) / BlockTabs.Length, 9.5f);
            return BlockTabBarH;
        }

        void PickBlockTab(int tab)
        {
            if (blockCardTab != tab)
                blockTabScroll = 0f;
            blockCardTab = tab;
            // Switching tabs closes any picker: the reader has moved to a different
            // question and the overlay was answering the last one.
            blockCardSheet = BlockSheet.None;
            blockCardPick = default;
            dirty = true;
        }

        /// <summary>The one scroll region: whichever tab is showing, laid into a window
        /// of exactly the room the shell has left over.</summary>
        void BuildTabBody(RectTransform host, float top, float height)
        {
            blockTabViewport = NewRect("Tab window", host);
            PlaceTopLeft(blockTabViewport, 0f, -top, blockCardW, height);
            Fill(blockTabViewport, LedgerStyle.Ground);
            blockTabViewport.gameObject.AddComponent<RectMask2D>();

            blockTabContent = NewRect("Tab body", blockTabViewport);
            PlaceTopLeft(blockTabContent, 0f, 0f, blockCardW, height);

            var cursor = blockCardTab switch
            {
                1 => BuildBookTab(blockTabContent),
                2 => BuildDoorsTab(blockTabContent),
                _ => BuildOrdersTab(blockTabContent),
            };

            var content = Mathf.Max(height, cursor);
            blockTabContent.sizeDelta = new Vector2(blockCardW, content);
            blockTabScroll = Mathf.Clamp(blockTabScroll, 0f,
                Mathf.Max(0f, content - height));
            blockTabContent.anchoredPosition = new Vector2(0f, blockTabScroll);

            // The drawer scrolls on its own, and says so: the ledger column beside it is
            // a separate run and neither wheel reaches the other.
            BuildScrollMarks(blockTabViewport, blockCardW - BlockPad,
                BlockTabs[blockCardTab].ToLowerInvariant(),
                out blockTabMoreAbove, out blockTabMoreBelow);
            ShowScrollMarks(blockTabMoreAbove, blockTabMoreBelow, blockTabScroll,
                content - height);
        }

        /// <summary>
        /// WHAT THE OFFICE SAID, over the foot of whatever tab is open. It stands in the
        /// shell and not in a tab body because the thing that gets refused is very often
        /// asked from a picker opened over THE BOOK or THE DOORS - a refusal printed
        /// only under the orders is a refusal the reader never sees.
        /// </summary>
        void BuildDrawerSaying(RectTransform host, float top, float height)
        {
            var band = NewRect("Saying", host);
            PlaceTopLeft(band, 0f, -top, blockCardW, height);
            Fill(band, LedgerV2.Wrong);
            // It stands OVER the foot of the list, so it takes the clicks that would
            // otherwise reach whatever the last row under it happens to be.
            ClickSurface(band);
            Rule(band, 0f, 0f, blockCardW, LedgerV2.Red);
            LedgerV2.Mono(band, BlockPad, -(height - LineBox(10.8f)) * 0.5f,
                    blockCardW - BlockPad * 2f, BlockCardSaying, 10.8f, LedgerV2.Red, 1f)
                .overflowMode = TextOverflowModes.Ellipsis;
        }

        // ------------------------------------------------------------- the footer

        void BuildDrawerFooter(RectTransform host, float top)
        {
            var foot = NewRect("Footer", host);
            PlaceTopLeft(foot, 0f, -top, blockCardW, BlockFooterH);
            Fill(foot, LedgerV2.PanelBand);
            Rule(foot, 0f, 0f, blockCardW, LedgerV2.Rule);

            var mapped = MapTargeting.Available &&
                         TerritoryRuntime.Instance?.Commands != null;
            var mark = LedgerV2.Button(foot, "MARK ON MAP", BlockPad, -10f, 132f, 30f,
                BeginBlockTargeting, LedgerV2.Key.Outline, 10.5f);
            SetActionEnabled(mark, mapped);

            // ONE key gives the block's first available order, and the line beside it
            // names which one - a red key whose verb the reader cannot read is a key
            // nobody presses twice.
            var order = PrimaryOrder();
            const float giveW = 152f;
            var give = LedgerV2.Button(foot, "GIVE THE ORDER",
                blockCardW - BlockPad - giveW, -10f, giveW, 30f,
                () =>
                {
                    var run = PrimaryOrder();
                    run.Run?.Invoke();
                },
                LedgerV2.Key.Red, 10.5f);
            SetActionEnabled(give, order.Run != null);

            var wordX = BlockPad + 132f + 10f;
            var wordW = blockCardW - BlockPad - giveW - 10f - wordX;
            if (wordW > 40f)
                LedgerV2.Mono(foot, wordX, -18f, wordW,
                        order.Run != null ? order.Title : "NOTHING CAN GO OUT",
                        9.6f, order.Run != null ? LedgerV2.Label : LedgerV2.Red, 10f,
                        TextAlignmentOptions.MidlineRight)
                    .overflowMode = TextOverflowModes.Ellipsis;
        }
    }
}
