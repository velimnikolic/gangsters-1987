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
        const float OrganizationTop = PageTop - 46f;
        const float OrganizationHeight = 614f;

        /// <summary>The dashed spine every branch hangs off, and the stub that reaches
        /// out of it to a card.</summary>
        const float SpineX = 34f;
        const float SpineStub = 18f;
        const float BranchX = 56f;

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
        readonly List<ReadTile> organizationTiles = new List<ReadTile>();

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

            foreach (Transform old in organizationFixed)
                Destroy(old.gameObject);
            foreach (Transform old in organizationContent)
                Destroy(old.gameObject);
            organizationHoverNote = null;

            var heading = Line(organizationFixed, LedgerStyle.Condensed, 30f,
                LedgerStyle.Ink, PageLeft, PageTop, 760f, 38f, "ORGANIZATION");
            heading.characterSpacing = 4f;
            Caps(organizationFixed, PageLeft, PageTop - 32f, 900f,
                "SHEET II · CHAIN OF COMMAND, CAPACITY, AND WHO ANSWERS FOR WHICH BLOCK",
                9.5f, LedgerStyle.InkLabel, 3f);
            if (!string.IsNullOrEmpty(organizationNote))
                Caps(organizationFixed, PageRight - 900f, PageTop - 32f, 900f,
                    organizationNote, 9.5f, LedgerStyle.Ballpoint, 2f,
                    TextAlignmentOptions.MidlineRight);
            Rule(organizationFixed, PageLeft, PageTop - 44f, PageWidth, LedgerStyle.Ink, 2f);

            var query = director != null ? director.Organization : null;
            if (query == null || !query.TryGetBoss(out var boss))
            {
                Line(organizationContent, LedgerStyle.MonoItalic, 14f,
                    LedgerStyle.RedPen, 0f, 0f, PageWidth, 24f,
                    "The command file has no authoritative Boss Character.");
                CloseOrganization(24f);
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
            cursor = BuildOrganizationTiles(query, cursor);
            cursor = BuildChainOfCommand(query, boss, cursor);
            cursor = BuildBlockLedger(cursor);
            cursor = BuildFiledOrders(cursor);
            CloseOrganization(cursor);
        }

        void CloseOrganization(float cursor)
        {
            var contentHeight = Mathf.Max(OrganizationHeight, cursor + 28f);
            organizationContent.sizeDelta = new Vector2(0f, contentHeight);
            organizationScroll = Mathf.Clamp(
                organizationScroll, 0f, Mathf.Max(0f, contentHeight - OrganizationHeight));
            organizationContent.anchoredPosition = new Vector2(0f, organizationScroll);
        }

        // -------------------------------------------------------------- what to read

        float BuildOrganizationTiles(IOrganizationQuery query, float cursor)
        {
            var over = 0;
            for (var i = 1; i < organizationLeaders.Count; i++)
                if (query.CapacityOf(organizationLeaders[i].Id).IsOverCapacity)
                    over++;
            var idle = CountUnassigned();
            var paperOnly = CountPaperOnly();
            var awaiting = outfit ? outfit.Filings.AwaitingCount : 0;

            organizationTiles.Clear();
            if (over > 0)
                organizationTiles.Add(new ReadTile(over.ToString(), LedgerStyle.RedPen,
                    "COMMANDERS OVER CAPACITY", "he will refuse the next man"));
            if (idle > 0)
                organizationTiles.Add(new ReadTile(idle.ToString(), LedgerStyle.RedPen,
                    "MEN IDLE UNDER YOU", "paid daily, earning nothing"));
            if (paperOnly > 0)
                organizationTiles.Add(new ReadTile(paperOnly.ToString(), LedgerStyle.PenAmber,
                    "NAMED ON GROUND WE DO NOT HOLD", "paper only · earns nothing"));
            if (awaiting > 0)
                organizationTiles.Add(new ReadTile(awaiting.ToString(), LedgerStyle.Ballpoint,
                    "ORDERS AWAITING RULING", "the outfit has not answered yet"));
            if (organizationTiles.Count == 0)
                return cursor;

            // The tiles fill the row however many there are - one alert is a banner
            // across the sheet, four are a quarter each. Never a stub at the left with
            // three quarters of empty paper beside it.
            const float gap = 12f;
            var width = (PageWidth - gap * (organizationTiles.Count - 1)) /
                        organizationTiles.Count;
            for (var i = 0; i < organizationTiles.Count; i++)
                Tile(organizationTiles[i], i * (width + gap), cursor, width);
            return cursor + 60f;
        }

        readonly struct ReadTile
        {
            public ReadTile(string value, Color accent, string label, string caption)
            {
                Value = value;
                Accent = accent;
                Label = label;
                Caption = caption;
            }

            public string Value { get; }
            public Color Accent { get; }
            public string Label { get; }
            public string Caption { get; }
        }

        void Tile(ReadTile read, float x, float cursor, float width)
        {
            const float height = 46f;
            var tile = NewRect("Tile " + read.Label, organizationContent);
            PlaceTopLeft(tile, x, -cursor, width, height);
            Stock(tile, LedgerStyle.Printout, LedgerStyle.PrintoutLow);
            Frame(tile, 1f, LedgerStyle.InkFaint);
            Block("Accent", tile, 0f, 0f, 4f, height, read.Accent);

            Line(tile, LedgerStyle.Condensed, 22f, read.Accent,
                14f, -10f, 58f, 26f, read.Value);
            Caps(tile, 76f, -9f, width - 86f, read.Label, 9f, LedgerStyle.InkMid, 3f);
            Line(tile, LedgerStyle.Mono, 9.5f, LedgerStyle.InkLabel,
                76f, -26f, width - 86f, 16f, read.Caption);
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
                Line(organizationContent, LedgerStyle.MonoItalic, 12f, LedgerStyle.InkDim,
                    BranchX, -cursor, 700f, 20f,
                    "No lieutenant branches are on the books.");
                cursor += 30f;
            }

            cursor = BuildPoolBranch(cursor);

            DottedVRule(organizationContent, SpineX, -spineTop,
                Mathf.Max(0f, cursor - spineTop - 8f), LedgerStyle.InkDotted);
            return cursor + 14f;
        }

        /// <summary>The dashed stub that reaches out of the spine to one branch.</summary>
        void Stub(float cursor) =>
            DottedRule(organizationContent, SpineX, -cursor, SpineStub,
                LedgerStyle.InkDotted);

        float BuildBossCard(
            IOrganizationQuery query, OrganizationPerson boss, float cursor)
        {
            const float height = 118f;
            var card = NewRect("Boss", organizationContent);
            PlaceTopLeft(card, 0f, -cursor, PageWidth, height);
            Stock(card, LedgerStyle.Blotter, LedgerStyle.BlotterLow);
            Frame(card, 1f, LedgerStyle.DeskDeep);
            Block("Rank", card, 0f, 0f, PageWidth, 4f, LedgerStyle.RedPen);

            const float plateW = 86f;
            var member = director.Roster != null ? director.Roster.Find(boss.Id) : null;
            Face(card, 4f, -4f, plateW, height - 8f, member);

            const float x = 102f;
            Caps(card, x, -14f, 320f, "BOSS · YOU", 9.5f, LedgerStyle.SoftRed, 6f);
            var name = Line(card, LedgerStyle.Condensed, 24f, LedgerStyle.HudCream,
                x, -30f, 440f, 32f, boss.Name);
            name.characterSpacing = 1f;
            Rule(card, x, -66f, 210f, LedgerStyle.RedPen);
            Caps(card, x, -74f, 440f, "HEAD OF THE FAMILY · ANSWERS TO NOBODY",
                9f, LedgerStyle.HudLabel, 3f);
            var hire = DarkTape(card, "HIRE · " + LedgerText.Cash(director.HoodRecruitmentCost),
                x, -90f, 168f, 24f, () => FileRecruit(-1));
            SetActionEnabled(hire, director != null);

            var capacity = query.CapacityOf(boss.Id);
            const float meterW = 380f;
            var meterX = PageWidth - meterW - 16f;
            Meter(card, meterX, 14f, meterW, "MEN ON THE BOOKS",
                capacity.Manpower, "man", "men", dark: true);
            // The outfit's ground against the ground the Boss can administer: the second
            // figure on his card is the city, not his own paperwork.
            Meter(card, meterX, 62f, meterW, "BLOCKS THE OUTFIT HOLDS",
                new CapacityMeasure(CountHeldBlocks(), capacity.Blocks.Maximum),
                "block", "blocks", dark: true);
            return cursor + height;
        }

        float BuildLieutenantBranch(
            IOrganizationQuery query, OrganizationPerson lieutenant, float cursor)
        {
            const float height = 96f;
            Stub(cursor + height * 0.5f);

            var capacity = query.CapacityOf(lieutenant.Id);
            var card = NewRect("Lieutenant " + lieutenant.Name, organizationContent);
            var width = PageWidth - BranchX;
            PlaceTopLeft(card, BranchX, -cursor, width, height);
            Stock(card, LedgerStyle.Printout, LedgerStyle.PrintoutLow);
            Frame(card, 1f, LedgerStyle.InkFaint);
            Block("Rank", card, 0f, 0f, 5f, height,
                capacity.IsOverCapacity ? LedgerStyle.RedPen : LedgerStyle.PenAmber);

            const float plateW = 78f;
            var member = director.Roster != null ? director.Roster.Find(lieutenant.Id) : null;
            Face(card, 5f, 0f, plateW, height, member);

            const float x = 97f;
            Caps(card, x, -10f, 230f, "LIEUTENANT", 9.5f, LedgerStyle.InkLabel, 5f);
            var name = Line(card, LedgerStyle.Condensed, 19f, LedgerStyle.Ink,
                x, -26f, 230f, 26f, lieutenant.Name);
            name.characterSpacing = 0.5f;
            var leaderId = lieutenant.Id;
            Tape(card, "HIRE · " + LedgerText.Cash(director.HoodRecruitmentCost),
                x, -58f, 158f, 24f, () => FileRecruit(leaderId), outline: true, size: 9f);

            // The two meters take whatever the card has left between the name column and
            // the right edge, and give back only what the FILE button needs when a man is
            // waiting to be placed. Nothing on this card is a fixed island.
            const float meterX = 345f;
            const float meterGap = 22f;
            var picking = organizationPickedHoodId >= 0;
            var acceptW = picking ? 286f : 0f;
            var meterW = (width - meterX - 16f - acceptW - meterGap) * 0.5f;
            Meter(card, meterX, 20f, meterW, "MANPOWER UNDER HIM",
                capacity.Manpower, "man", "men", dark: false);
            Meter(card, meterX + meterW + meterGap, 20f, meterW, "BLOCKS ON HIS PAPER",
                capacity.Blocks, "block", "blocks", dark: false);

            if (picking)
            {
                var picked = Person(organizationPickedHoodId);
                var hoodId = organizationPickedHoodId;
                Tape(card, "FILE · PUT " + FirstName(picked.Name).ToUpperInvariant() +
                     " UNDER HIM", width - 286f, -31f, 270f, 34f,
                    () => FileHoodPlacement(hoodId, leaderId), size: 10f);
            }

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
            var contentX = BranchX + 22f + 14f;
            cursor = BuildFaceStrip(men, contentX, cursor, lieutenant.Name,
                BranchSummary(men), organizationOpenBranches.Contains(lieutenant.Id),
                () => ToggleBranch(lieutenant.Id));

            if (organizationOpenBranches.Contains(lieutenant.Id))
                cursor = BuildRosterGrid(men, contentX, cursor, recall: true);

            DottedVRule(organizationContent, BranchX + 22f, -top,
                Mathf.Max(0f, cursor - top - 6f), LedgerStyle.InkHair);
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
            var hint = picked.IsValid
                ? "PICKED: " + picked.Name +
                  " — NOW PRESS FILE ON THE LIEUTENANT WHO SHOULD TAKE HIM"
                : pool.Count > 0
                    ? "UNDER YOU DIRECTLY · " + pool.Count +
                      " IDLE, NO BRANCH, NO EARNINGS — CLICK A MAN TO PLACE HIM"
                    : "UNDER YOU DIRECTLY · NOBODY IS SITTING IDLE";
            Caps(organizationContent, BranchX + 8f, -cursor, PageWidth - BranchX - 8f,
                hint, 9.5f, LedgerStyle.RedPen, 3f);

            var top = cursor + 16f;
            cursor += 22f;
            var contentX = BranchX + 22f + 14f;
            cursor = BuildFaceStrip(pool, contentX, cursor, "THE BOSS",
                pool.Count == 1 ? "1 MAN IDLE" : pool.Count + " MEN IDLE",
                organizationPoolOpen, TogglePool);

            if (organizationPoolOpen)
                cursor = BuildRosterGrid(pool, contentX, cursor, recall: false);

            DottedVRule(organizationContent, BranchX + 22f, -top,
                Mathf.Max(0f, cursor - top - 6f), LedgerStyle.InkHair);
            return cursor + 8f;
        }

        float BuildFaceStrip(List<OrganizationPerson> men, float x, float cursor,
            string underName, string summary, bool open, UnityAction onToggle)
        {
            const float thumbW = 28f;
            const float thumbH = 36f;
            const float pitch = 31f;

            var shown = Mathf.Min(men.Count, ThumbLimit);
            for (var i = 0; i < shown; i++)
                Thumb(men[i], underName, x + i * pitch, cursor, thumbW, thumbH);

            var run = x + shown * pitch + 6f;
            if (men.Count > ThumbLimit)
            {
                Line(organizationContent, LedgerStyle.MonoBold, 11f, LedgerStyle.InkMid,
                    run, -(cursor + 12f), 60f, 18f, "+" + (men.Count - ThumbLimit));
                run += 46f;
            }

            Caps(organizationContent, run, -(cursor + 12f), 620f, summary,
                9.5f, LedgerStyle.InkLabel, 3f);

            Tape(organizationContent, open ? "HIDE ROSTER"
                    : "OPEN ROSTER · " + men.Count, PageWidth - 210f, -(cursor + 6f),
                210f, 26f, onToggle, outline: true, size: 9f);
            return cursor + thumbH + 8f;
        }

        void Thumb(OrganizationPerson person, string underName,
            float x, float cursor, float w, float h)
        {
            var member = director.Roster != null ? director.Roster.Find(person.Id) : null;
            var slot = NewRect("Face " + person.Name, organizationContent);
            PlaceTopLeft(slot, x, -cursor, w, h);
            Face(slot, 0f, 0f, w, h, member);

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
            var width = PageWidth - x;
            var columns = Mathf.Clamp(
                Mathf.FloorToInt((width + columnGap) / (minCell + columnGap)), 1, 4);
            const float rowH = 24f;
            var cell = (width - columnGap * (columns - 1)) / columns;

            Rule(organizationContent, x, -cursor, width, LedgerStyle.InkFaint);
            cursor += 6f;

            if (men.Count == 0)
            {
                Line(organizationContent, LedgerStyle.MonoItalic, 11.5f, LedgerStyle.InkDim,
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

                var row = NewRect("Man " + person.Name, organizationContent);
                PlaceTopLeft(row, rowX, -rowY, cell, rowH);
                // One Graphic per object: the pool row's own stock IS its click surface.
                Image stock = null;
                if (!recall)
                {
                    stock = Fill(row, organizationPickedHoodId == person.Id
                        ? LedgerStyle.LedgerGreen
                        : LedgerStyle.Printout);
                    stock.raycastTarget = true;
                }
                Rule(row, 0f, -(rowH - 1f), cell, LedgerStyle.InkHair);

                var posted = HasPost(person);
                Block("Dot", row, 0f, -8f, 7f, 7f,
                    posted ? LedgerStyle.GreenOk : LedgerStyle.RedPen);
                Line(row, LedgerStyle.Condensed, 13f, LedgerStyle.Ink,
                    14f, -3f, 170f, 18f, person.Name);

                var personId = person.Id;
                if (recall)
                {
                    Line(row, LedgerStyle.Mono, 9.5f, LedgerStyle.InkLabel,
                        188f, -4f, cell - 258f, 16f, HoodDuty(person));
                    Tape(row, "RECALL", cell - 66f, -1f, 66f, 21f,
                        () => FileHoodRecall(personId), red: true, outline: true, size: 8f);
                }
                else
                {
                    var isPicked = organizationPickedHoodId == person.Id;
                    Caps(row, 188f, -4f, cell - 194f,
                        isPicked ? "PICKED · CHOOSE A LIEUTENANT ABOVE" : "IDLE · CLICK TO PLACE",
                        9f, isPicked ? LedgerStyle.RedPen : LedgerStyle.InkLabel, 2f,
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
            var seeAll = Tape(organizationContent, "SEE ALL BLOCKS IN THE CITY",
                PageWidth - 300f, -(cursor - 46f), 300f, 28f,
                BeginBlockTargeting, outline: true, size: 9.5f);
            SetActionEnabled(seeAll, mapReady);

            CollectBlockRows();
            var action = 230f;
            var span = PageWidth - action;
            var c0 = span * 1.5f / 5.3f;
            var c1 = span * 1.2f / 5.3f;
            var c2 = c1;
            var c3 = span - c0 - c1 - c2;
            var columns = new[] { c0, c1, c2, c3, action };

            var head = NewRect("Ledger head", organizationContent);
            PlaceTopLeft(head, 0f, -cursor, PageWidth, 32f);
            Fill(head, LedgerStyle.Blotter);
            var headings = new[]
            {
                "BLOCK", "RESPONSIBLE · PAPER", "CONTROL · STREET",
                "READING", "CHANGE THE PAPER",
            };
            var headColours = new[]
            {
                LedgerStyle.HudCream, LedgerStyle.SoftRed, LedgerStyle.HudAmber,
                LedgerStyle.HudCream, LedgerStyle.HudCream,
            };
            var x = 0f;
            for (var i = 0; i < headings.Length; i++)
            {
                Caps(head, x + 14f, -9f, columns[i] - 20f, headings[i], 9.5f,
                    headColours[i], 4f);
                x += columns[i];
            }
            cursor += 32f;

            if (organizationBlockRows.Count == 0)
            {
                var empty = NewRect("Ledger empty", organizationContent);
                PlaceTopLeft(empty, 0f, -cursor, PageWidth, 44f);
                Stock(empty, LedgerStyle.Printout, LedgerStyle.PrintoutLow);
                Frame(empty, 1f, LedgerStyle.InkFaint);
                Line(empty, LedgerStyle.MonoItalic, 12f, LedgerStyle.InkDim,
                    14f, -12f, PageWidth - 28f, 20f,
                    "No block is on our paper and none is ours on the street.");
                return cursor + 56f;
            }

            var frame = NewRect("Ledger", organizationContent);
            PlaceTopLeft(frame, 0f, -cursor, PageWidth, 1f);
            Frame(frame, 1f, LedgerStyle.InkFaint);

            var rows = Mathf.Min(organizationBlockRows.Count, BlockRowLimit);
            var height = 0f;
            for (var i = 0; i < rows; i++)
                height = BuildBlockRow(organizationBlockRows[i], columns, cursor, height);
            frame.sizeDelta = new Vector2(PageWidth, height);
            cursor += height;

            if (organizationBlockRows.Count > rows)
            {
                Caps(organizationContent, 0f, -(cursor + 6f), PageWidth,
                    "AND " + (organizationBlockRows.Count - rows) +
                    " MORE ON THE BOOKS · OPEN THE MAP TO READ THE WHOLE CITY",
                    9f, LedgerStyle.InkLabel, 3f);
                cursor += 24f;
            }
            return cursor + 16f;
        }

        float BuildBlockRow(
            TerritoryBlockId blockId, float[] columns, float top, float offset)
        {
            const float rowH = 54f;
            var leaderId = organizationPaper.TryGetValue(blockId, out var id) ? id : -1;
            var leader = Leader(leaderId);
            var control = ControlOf(blockId);
            var mismatch = leader.IsValid && control == BlockControl.NotOurs;
            var orphan = !leader.IsValid && control == BlockControl.Held;
            var menuOpen = organizationBlockMenu == blockId;

            var row = NewRect("Block " + blockId.Value, organizationContent);
            PlaceTopLeft(row, 0f, -(top + offset), PageWidth, rowH);
            Fill(row, menuOpen
                ? LedgerStyle.LedgerGreen
                : mismatch || orphan ? LedgerStyle.Carbon : LedgerStyle.Printout);
            Rule(row, 0f, 0f, PageWidth, LedgerStyle.InkFaint);

            var x = 0f;
            Line(row, LedgerStyle.Condensed, 17f, LedgerStyle.Ink,
                x + 14f, -10f, columns[0] - 24f, 22f, BlockName(blockId));
            Line(row, LedgerStyle.Mono, 10f, LedgerStyle.InkLabel,
                x + 14f, -31f, columns[0] - 24f, 16f, NeighborhoodOf(blockId));
            x += columns[0];

            var paperColour = leader.IsValid ? LedgerStyle.Ballpoint : LedgerStyle.RedPen;
            var paperMark = NewRect("Paper mark", row);
            PlaceTopLeft(paperMark, x + 14f, -21f, 12f, 12f);
            Frame(paperMark, 2f, paperColour);
            Texture(paperMark, LedgerStyle.Hatch, paperColour, 12f, 12f, 4f);
            Line(row, LedgerStyle.MonoBold, 12f, paperColour,
                x + 32f, -20f, columns[1] - 42f, 18f,
                leader.IsValid ? leader.Name : "NOBODY NAMED");
            x += columns[1];

            var streetColour = ControlColour(control);
            Block("Street mark", row, x + 14f, -21f, 11f, 11f, streetColour);
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
            Tape(row, label, x + 14f, -13f, columns[4] - 28f, 28f,
                () => ToggleBlockMenu(blockId), outline: !menuOpen, size: 9f);

            offset += rowH;
            return menuOpen
                ? offset + BuildBlockMenu(blockId, leaderId, top + offset, columns)
                : offset;
        }

        float BuildBlockMenu(
            TerritoryBlockId blockId, int leaderId, float top, float[] columns)
        {
            var options = organizationLeaders.Count + (leaderId >= 0 ? 1 : 0);
            var height = 30f + options * 30f;
            var width = 340f;
            var menu = NewRect("Menu " + blockId.Value, organizationContent);
            PlaceTopLeft(menu, PageWidth - width - 14f, -top, width, height);
            Fill(menu, LedgerStyle.Blotter);
            Frame(menu, 1f, LedgerStyle.DeskDeep);
            Caps(menu, 12f, -9f, width - 24f,
                "WHO ANSWERS FOR " + BlockName(blockId).ToUpperInvariant(),
                9f, LedgerStyle.HudLabel, 3f);

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
                Rule(option, 0f, 0f, width, LedgerStyle.BlotterRule);
                Line(option, LedgerStyle.Condensed, 13f,
                    isBoss ? LedgerStyle.HudAmber : LedgerStyle.HudCream,
                    12f, -6f, 190f, 18f,
                    isBoss ? leader.Name + " · YOU" : leader.Name);
                Caps(option, 200f, -7f, width - 212f,
                    capacity.Current + " / " + capacity.Maximum + (full ? " · FULL" : ""),
                    9f, full ? LedgerStyle.SoftRed : LedgerStyle.HudNote, 2f,
                    TextAlignmentOptions.MidlineRight);
                RowButton(option, ClickSurface(option),
                    () => FileBlockResponsibility(blockId, target));
                y += 30f;
            }

            if (leaderId >= 0)
            {
                var strike = NewRect("Option strike", menu);
                PlaceTopLeft(strike, 0f, -y, width, 30f);
                Rule(strike, 0f, 0f, width, LedgerStyle.BlotterRule);
                Line(strike, LedgerStyle.Condensed, 13f, LedgerStyle.SoftRed,
                    12f, -6f, 260f, 18f, "Nobody · strike the name off");
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

            var frame = NewRect("Filings", organizationContent);
            var height = count * 40f + 30f;
            PlaceTopLeft(frame, 0f, -cursor, PageWidth, height);
            Stock(frame, LedgerStyle.Printout, LedgerStyle.PrintoutLow);
            Frame(frame, 1f, LedgerStyle.InkFaint);

            for (var i = 0; i < count; i++)
            {
                var filing = filings.All[i];
                var row = NewRect("Filing " + filing.Id, frame);
                PlaceTopLeft(row, 0f, -(i * 40f), PageWidth, 40f);
                Rule(row, 14f, -39f, PageWidth - 28f, LedgerStyle.InkHair);

                const float stampW = 80f;
                const float chipW = 112f;
                const float rulingW = 320f;
                var textX = 20f + stampW + 16f;
                var chipX = PageWidth - 20f - rulingW - 16f - chipW;

                Line(row, LedgerStyle.Mono, 11f, LedgerStyle.InkLabel,
                    20f, -12f, stampW, 18f, filing.Stamp);
                Line(row, LedgerStyle.Mono, 12f, LedgerStyle.InkSoft,
                    textX, -12f, chipX - textX - 16f, 18f, filing.Text);

                var chip = NewRect("Status", row);
                PlaceTopLeft(chip, chipX, -10f, chipW, 22f);
                Fill(chip, StatusColour(filing.Status));
                Caps(chip, 0f, -5f, chipW, StatusWord(filing.Status), 9.5f,
                    LedgerStyle.Paper, 4f, TextAlignmentOptions.Center);

                Line(row, LedgerStyle.Mono, 10.5f, LedgerStyle.InkLabel,
                    PageWidth - 20f - rulingW, -12f, rulingW, 18f, filing.Ruling)
                    .alignment = TextAlignmentOptions.MidlineRight;
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
            Line(frame, LedgerStyle.Mono, 11f, LedgerStyle.InkDim,
                16f, -(count * 40f + 6f), PageWidth - 32f, 18f, footer);
            return cursor + height;
        }

        // -------------------------------------------------------------------- pieces

        float Section(float cursor, string title, string caption)
        {
            var heading = Line(organizationContent, LedgerStyle.Condensed, 20f,
                LedgerStyle.Ink, 0f, -cursor, 760f, 26f, title);
            heading.characterSpacing = 4f;
            if (caption.Length > 0)
                Caps(organizationContent, PageWidth - 760f, -(cursor + 4f), 760f,
                    caption, 9.5f, LedgerStyle.InkLabel, 3f,
                    TextAlignmentOptions.MidlineRight);
            cursor += 30f;
            Rule(organizationContent, 0f, -cursor, PageWidth, LedgerStyle.InkFaint);
            return cursor + 12f;
        }

        /// <summary>One capacity meter: the figure, the bar, and the plain sentence
        /// saying what the figure means for the next order.</summary>
        static void Meter(Transform card, float x, float top, float width, string label,
            CapacityMeasure measure, string unit, string plural, bool dark)
        {
            var over = measure.IsOverCapacity;
            var full = !over && measure.Current >= measure.Maximum;
            var ink = over
                ? dark ? LedgerStyle.SoftRed : LedgerStyle.RedPen
                : full
                    ? dark ? LedgerStyle.HudAmber : LedgerStyle.PenAmber
                    : dark ? LedgerStyle.HudCream : LedgerStyle.Ink;
            var labelInk = dark ? LedgerStyle.HudLabel : LedgerStyle.InkLabel;

            Caps(card, x, -top, width - 90f, label, 9.5f,
                over ? ink : labelInk, 4f);
            Line(card, LedgerStyle.MonoBold, 15f, ink,
                x + width - 96f, -(top + 1f), 96f, 20f,
                measure.Current + " / " + measure.Maximum)
                .alignment = TextAlignmentOptions.MidlineRight;
            MeterBar(card, x, top + 19f, width,
                measure.Maximum <= 0 ? 0f : (float)measure.Current / measure.Maximum,
                ink, dark);

            var room = measure.Maximum - measure.Current;
            var note = over
                ? "OVER BY " + measure.Overage + " · the outfit will not add more"
                : full
                    ? "at the limit · no room for another " + unit
                    : room + " more " + (room == 1 ? unit : plural) + " will fit";
            Line(card, LedgerStyle.Mono, 9.5f,
                over ? ink : dark ? LedgerStyle.HudNote : LedgerStyle.InkDim,
                x, -(top + 29f), width, 16f, note);
        }

        /// <summary>The meter's track and what is filled of it. The track is drawn, not
        /// implied: a figure like 2/50 fills four percent of it, and without a printed
        /// track that reads as no meter at all rather than as an almost-empty one.
        /// </summary>
        static void MeterBar(Transform card, float x, float top, float width,
            float fraction, Color ink, bool dark)
        {
            const float height = 8f;
            var track = NewRect("Meter track", card);
            PlaceTopLeft(track, x, -top, width, height);
            Fill(track, dark ? LedgerStyle.DeskMid : LedgerStyle.PaperDeep);
            Frame(track, 1f, dark ? LedgerStyle.HudNote : LedgerStyle.InkPale);

            var filled = Mathf.Clamp01(fraction);
            if (filled <= 0f)
                return;
            var run = NewRect("Meter fill", track);
            PlaceTopLeft(run, 1f, -1f,
                Mathf.Max(2f, (width - 2f) * filled), height - 2f);
            Fill(run, ink);
        }

        /// <summary>A man's photograph in a plate, or the hatch when no model resolves.
        /// The face is always the one he wears on the street - never a picked file.</summary>
        static void Face(Transform parent, float x, float y, float w, float h,
            Character member)
        {
            var raw = Plate(parent, x, y, w, h, "");
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
            Frame(rect, 1f, LedgerStyle.HudLabel);

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
                LedgerStyle.HudCream, TextAlignmentOptions.Center);
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
            Fill(organizationHoverNote, LedgerStyle.Blotter);
            Frame(organizationHoverNote, 1f, LedgerStyle.HudLabel);

            Line(organizationHoverNote, LedgerStyle.Condensed, 14f, LedgerStyle.HudCream,
                11f, -7f, width - 22f, 20f, person.Name);
            Caps(organizationHoverNote, 11f, -25f, width - 22f,
                (person.Rank == Rank.Hood ? "HOOD · UNDER " : "UNDER ") +
                underName.ToUpperInvariant(),
                9f, LedgerStyle.HudLabel, 3f);
            var posted = HasPost(person);
            Block("Dot", organizationHoverNote, 11f, -44f, 7f, 7f,
                posted ? LedgerStyle.GreenOk : LedgerStyle.RedPen);
            Line(organizationHoverNote, LedgerStyle.Mono, 10.5f, LedgerStyle.HudCream,
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
            if (block.Control != profile.Controlled && block.Control != profile.Contested &&
                block.Control != profile.Influenced)
                return BlockControl.Unknown;

            // Control alone does not say WHOSE. A held block we hold no premise on
            // belongs to another house, and the sheet has to say so rather than print it
            // green. It is the DEEDS that answer this, not who happens to be standing
            // there: a crew passing through a rival street does not make it ours.
            var ours = block.Holding != profile.Holding.NoneLabel &&
                       block.Holding != profile.Holding.UnknownLabel;
            if (!ours)
                return BlockControl.Theirs;
            return block.Control == profile.Controlled
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
            BlockControl.Held => LedgerStyle.GreenOk,
            BlockControl.Contested => LedgerStyle.PenAmber,
            BlockControl.Theirs => LedgerStyle.RedPen,
            BlockControl.NotOurs => LedgerStyle.RedPen,
            _ => LedgerStyle.InkLabel,
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
                colour = manned ? LedgerStyle.PenAmber : LedgerStyle.RedPen;
                if (manned)
                    return leader.Name + " has men on it and not a premise to show for it.";
                return control == BlockControl.Theirs
                    ? leader.Name + " answers for another house's ground. Paper only."
                    : leader.Name + " answers for ground we do not hold. Paper only.";
            }
            if (!leader.IsValid && control == BlockControl.Held)
            {
                colour = LedgerStyle.RedPen;
                return "We hold it and nobody answers for it.";
            }
            if (control == BlockControl.Contested)
            {
                colour = LedgerStyle.PenAmber;
                return leader.IsValid
                    ? "His, but another house is pushing on it."
                    : "Contested, and nobody is named for it.";
            }
            if (!leader.IsValid)
            {
                colour = LedgerStyle.InkDim;
                return manned
                    ? "Our men are standing on it. No premise here is ours yet."
                    : "Nobody named. Nothing to answer for.";
            }
            colour = LedgerStyle.GreenOk;
            return "Paper and street agree.";
        }

        static Color StatusColour(Outfit.FilingStatus status) => status switch
        {
            Outfit.FilingStatus.Granted => LedgerStyle.GreenOk,
            Outfit.FilingStatus.Refused => LedgerStyle.RedPen,
            _ => LedgerStyle.Ballpoint,
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

        int CountUnassigned()
        {
            var count = 0;
            for (var i = 0; i < organizationPeople.Count; i++)
                if (organizationPeople[i].IsUnassigned &&
                    organizationPeople[i].IsAvailable)
                    count++;
            return count;
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
                label.color = LedgerStyle.InkFaint;
        }
    }
}
