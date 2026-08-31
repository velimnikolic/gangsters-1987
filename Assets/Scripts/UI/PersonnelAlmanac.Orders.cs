using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using LivingCity.Gameplay;
using LivingCity.Personnel;
using static LivingCity.UI.LedgerKit;

namespace LivingCity.UI
{
    /// <summary>
    /// ORDERS: the outfit's work in hand, typed up as a memo. The lieutenants and what
    /// each crew is doing this minute; THE JOB - the order being drafted for the chosen
    /// crew, its target picked on the strategic map standing open to the right; the
    /// open book; and the record of what has come back. This page is the map's
    /// IMapTargetingConsumer while a crew is chosen: area orders drag a box, point
    /// orders click a door.
    ///
    /// There is no commit tape. The game runs in real time: an order is issued and the
    /// crew leaves, and the page redraws under the player as they travel, work and
    /// report - which is why every line here is derived at repaint and none is stored.
    /// </summary>
    public sealed partial class PersonnelAlmanac
    {
        const float OrdersTop = PageTop - 36f;
        const float OrdersInner = PageWidth - 8f;

        RectTransform ordersViewport;
        RectTransform ordersContent;
        float ordersScroll;

        int ordersCrewId = -1;
        int ordersCategoryIndex;
        int ordersTypeIndex;
        readonly List<Outfit.OrderSpec> categorySpecs = new List<Outfit.OrderSpec>();
        readonly List<int> draftBlocks = new List<int>();
        int draftBlockId = -1;
        float draftX;
        float draftZ;
        string draftLabel = "";
        int draftMen = 1;
        string ordersNote = "";
        int selectedOrderId = -1;
        readonly List<Rect> highlightRects = new List<Rect>();

        void BuildOrdersPage(RectTransform sheet)
        {
            var root = NewPageRoot(sheet, LedgerPage.Orders);

            var heading = Line(root, LedgerStyle.Type, 18f, LedgerStyle.Ink, PageLeft, PageTop,
                400f, 30f, "ORDERS");
            heading.characterSpacing = 4f;
            ordersWeek = Line(root, LedgerStyle.Mono, 14.5f, LedgerStyle.InkDim, PageLeft + 140f,
                PageTop + 2f, PageWidth - 140f, 26f, "", TextAlignmentOptions.MidlineRight);
            Rule(root, PageLeft, PageTop - 30f, PageWidth, LedgerStyle.Ink);

            ordersViewport = NewRect("Viewport", root);
            PlaceTopLeft(ordersViewport, PageLeft, OrdersTop, PageWidth,
                OrdersTop - PageBottom);
            ordersViewport.gameObject.AddComponent<RectMask2D>();

            ordersContent = NewRect("Content", ordersViewport);
            ordersContent.anchorMin = new Vector2(0f, 1f);
            ordersContent.anchorMax = new Vector2(1f, 1f);
            ordersContent.pivot = new Vector2(0f, 1f);
            ordersContent.anchoredPosition = Vector2.zero;
            ordersContent.sizeDelta = new Vector2(0f, 400f);
        }

        TMP_Text ordersWeek;

        /// <summary>How many categories the order table has - read off the enum, so
        /// the cyclers wrap on the truth rather than on a number that goes stale the
        /// day a category is added.</summary>
        static readonly int CategoryCount =
            System.Enum.GetValues(typeof(Outfit.OrderCategory)).Length;

        int _cachedCategoryIndex = -1;
        int _cachedTypeIndex = -1;
        Outfit.OrderSpec _cachedSpec;

        /// <summary>The order being drafted. The map asks for it every frame of a drag
        /// (WantsArea), so the category's specs are gathered once per index change and
        /// the answer kept until a cycler moves.</summary>
        Outfit.OrderSpec CurrentDraftSpec()
        {
            if (ordersCategoryIndex == _cachedCategoryIndex && ordersTypeIndex == _cachedTypeIndex)
                return _cachedSpec;

            FillCategorySpecs(ordersCategoryIndex);
            // A category the table has no orders for cannot be drafted from: the first
            // that has any stands in for it, and a table with none at all drafts the
            // empty spec rather than throwing under the map's drag.
            for (var i = 0; categorySpecs.Count == 0 && i < CategoryCount; i++)
            {
                ordersCategoryIndex = i;
                FillCategorySpecs(i);
            }
            if (ordersTypeIndex >= categorySpecs.Count)
                ordersTypeIndex = 0;

            _cachedCategoryIndex = ordersCategoryIndex;
            _cachedTypeIndex = ordersTypeIndex;
            _cachedSpec = categorySpecs.Count > 0 ? categorySpecs[ordersTypeIndex] : default;
            return _cachedSpec;
        }

        void FillCategorySpecs(int categoryIndex)
        {
            categorySpecs.Clear();
            var category = (Outfit.OrderCategory)categoryIndex;
            foreach (var spec in Outfit.OrderTable.Specs)
                if (spec.Category == category)
                    categorySpecs.Add(spec);
        }

        void RefreshTargeting()
        {
            var wantsOrders = IsOpen && currentPage == LedgerPage.Orders && ordersCrewId >= 0;
            var wantsOrganization = !IsOpen && OrganizationTargetingActive;
            var wants = wantsOrders || wantsOrganization;
            if (wants)
                StrategicMapHud.SetTargeting(this);
            else
                StrategicMapHud.ClearTargeting(this);
        }

        // ---- IMapTargetingConsumer ----

        public bool WantsArea => !OrganizationTargetingActive &&
                                 CurrentDraftSpec().Mode == Outfit.TargetMode.Area;

        public void OnAreaPreview(Rect worldXZ)
        {
            if (OrganizationTargetingActive)
                return;
            // Blocks light as the box swallows them - preview shares the capture logic
            // so what lights is exactly what a release would take.
            CaptureArea(worldXZ, preview: true);
            PushHighlights();
        }

        public void OnAreaSelected(Rect worldXZ)
        {
            if (OrganizationTargetingActive)
                return;
            CaptureArea(worldXZ, preview: false);
            selectedOrderId = -1;
            dirty = true;
        }

        public void OnPointClicked(Vector2 worldXZ, int blockId)
        {
            if (OrganizationTargetingActive)
            {
                CaptureOrganizationBlock(blockId);
                return;
            }

            var spec = CurrentDraftSpec();
            if (spec.Mode == Outfit.TargetMode.Area)
            {
                // A bare click under an area order takes the one block it landed on.
                if (blockId >= 0)
                    CaptureArea(CityBlocks.Get(blockId)?.Union ?? default, preview: false);
            }
            else
                CapturePoint(worldXZ, blockId);

            selectedOrderId = -1;
            dirty = true;
        }

        void CaptureArea(Rect worldRect, bool preview)
        {
            var spec = CurrentDraftSpec();
            draftBlocks.Clear();
            draftLabel = "";
            draftBlockId = -1;
            var skipped = 0;
            var firstRefusal = Refusal.None;
            var firstRival = -1;

            // The holdings are gathered once for the whole box: a preview runs this
            // every frame of the drag, over every block the box swallows.
            if (outfit)
                outfit.CollectHoldings(holdings);
            else
                holdings.Clear();

            foreach (var block in CityBlocks.Blocks)
            {
                if (!block.Union.Overlaps(worldRect))
                    continue;
                var refusal = Refuse(spec.Type, block.Id, out var rival);
                if (refusal == Refusal.None)
                    draftBlocks.Add(block.Id);
                else
                {
                    skipped++;
                    if (firstRefusal == Refusal.None)
                    {
                        firstRefusal = refusal;
                        firstRival = rival;
                    }
                }
            }

            // The blocks' centre IS the area order's place: the crew has to travel to
            // somewhere, and a box of blocks travels to the middle of itself. Without
            // it an area job would read as having no coordinates and its men would
            // arrive instantly however far across town the box was dragged.
            if (draftBlocks.Count > 0)
            {
                var sum = Vector2.zero;
                var counted = 0;
                foreach (var id in draftBlocks)
                {
                    var block = CityBlocks.Get(id);
                    if (block == null)
                        continue;
                    sum += block.Center;
                    counted++;
                }
                if (counted > 0)
                {
                    draftX = sum.x / counted;
                    draftZ = sum.y / counted;
                }
            }

            if (!preview)
                ordersNote = draftBlocks.Count + " block" +
                    (draftBlocks.Count == 1 ? "" : "s") + " taken" +
                    (skipped > 0
                        ? "; " + skipped + " skipped (" + RefusalText(firstRefusal, firstRival) + ")"
                        : "") +
                    ".";
        }

        void CapturePoint(Vector2 world, int blockId)
        {
            var spec = CurrentDraftSpec();

            Entities.BusinessMarker best = null;
            var bestSqr = 45f * 45f;
            foreach (var business in PropertyRegistry.Businesses)
            {
                if (!business)
                    continue;
                var position = business.transform.position;
                var dx = position.x - world.x;
                var dz = position.z - world.y;
                var sqr = dx * dx + dz * dz;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = business;
                }
            }

            var needsBusiness = spec.Type == Outfit.OrderType.SmashUp ||
                spec.Type == Outfit.OrderType.Raid ||
                spec.Type == Outfit.OrderType.Torch ||
                spec.Type == Outfit.OrderType.Bomb ||
                spec.Type == Outfit.OrderType.BuyPremises ||
                spec.Type == Outfit.OrderType.SetUpBusiness ||
                spec.Type == Outfit.OrderType.RunBusiness ||
                spec.Type == Outfit.OrderType.AdjustProtection;

            // Verbose BEFORE assignment, opaque after execution - that split is the
            // design: the planner explains, the report never does.
            if (needsBusiness && !best)
            {
                ordersNote = LedgerText.OrderLabel(spec.Type) +
                    " wants a business door - nothing stands there.";
                return;
            }
            if (blockId < 0 && !best)
            {
                ordersNote = "Open street - nothing to target.";
                return;
            }

            draftBlocks.Clear();
            if (best)
            {
                draftBlockId = best.BlockId;
                var position = best.transform.position;
                draftX = position.x;
                draftZ = position.z;
                draftLabel = best.BusinessName;
            }
            else
            {
                draftBlockId = blockId;
                draftX = world.x;
                draftZ = world.y;
                draftLabel = "Block #" + blockId;
            }
            ordersNote = "Target: " + draftLabel + ".";
        }

        /// <summary>Why a block cannot take an order of this type - a code, not a
        /// sentence: the preview asks over every block under the drag, every frame,
        /// and only the note that follows a release ever reads the words.</summary>
        enum Refusal
        {
            None,
            NoBusinesses,
            HeldByRival,
            NotYourTurf,
        }

        /// <summary>Reads <see cref="holdings"/> as the caller filled them - the box
        /// gathers them once, not once per block.</summary>
        Refusal Refuse(Outfit.OrderType type, int blockId, out int rival)
        {
            rival = -1;
            switch (type)
            {
                case Outfit.OrderType.Extort:
                    if (!BlockHasBusiness(blockId))
                        return Refusal.NoBusinesses;
                    // A rival premise on the block shields it - you do not squeeze a
                    // street another family is standing on. Building-held, not block-held.
                    for (var gang = 0; gang < Gangs.GangCatalog.GangCount; gang++)
                        if (gang != Gangs.GangCatalog.PlayerGangId &&
                            Outfit.Turf.CountIn(holdings, blockId, gang) > 0)
                        {
                            rival = gang;
                            return Refusal.HeldByRival;
                        }
                    return Refusal.None;

                case Outfit.OrderType.CollectProtection:
                case Outfit.OrderType.Patrol:
                    return Outfit.Turf.CountIn(
                        holdings, blockId, Gangs.GangCatalog.PlayerGangId) > 0
                        ? Refusal.None : Refusal.NotYourTurf;

                default:
                    return Refusal.None;
            }
        }

        static string RefusalText(Refusal refusal, int rival)
        {
            switch (refusal)
            {
                case Refusal.NoBusinesses: return "no businesses";
                case Refusal.HeldByRival: return "held by " + Gangs.GangRegistry.NameOf(rival);
                case Refusal.NotYourTurf: return "not your turf";
                default: return "";
            }
        }

        static bool BlockHasBusiness(int blockId)
        {
            foreach (var business in PropertyRegistry.Businesses)
                if (business && business.BlockId == blockId)
                    return true;
            return false;
        }

        /// <summary>Metres from headquarters to the draft's place - the same figure the
        /// director charges the crew when the job starts, quoted before the player
        /// commits to it. Both read draftX/draftZ, which CaptureArea and CapturePoint
        /// are the only writers of.</summary>
        float DraftDistance()
        {
            if (!outfit || (draftBlocks.Count == 0 && draftLabel.Length == 0))
                return 0f;
            if (!outfit.TryGetHeadquarters(out var hq, out _))
                return 0f;

            return Vector2.Distance(new Vector2(hq.x, hq.z), new Vector2(draftX, draftZ));
        }

        void PushHighlights()
        {
            if (!StrategicMapHud.Instance)
                return;

            highlightRects.Clear();
            // The still-unconfirmed draft washes the map in highlighter yellow; a
            // confirmed order the player is reading back washes in ink. The two states
            // must never look alike.
            var color = new Color(1f, 0.85f, 0.15f, 0.32f);

            var book = outfit ? outfit.Book : null;
            Outfit.Job selected = null;
            if (book != null && selectedOrderId >= 0)
                foreach (var job in book.Jobs)
                    if (job.Id == selectedOrderId)
                        selected = job;

            if (selected != null)
            {
                color = new Color(0.16f, 0.14f, 0.12f, 0.30f);
                foreach (var id in selected.BlockTargets)
                    AddBlockRect(id);
                if (selected.BlockTargets.Count == 0)
                    highlightRects.Add(new Rect(
                        selected.TargetX - 14f, selected.TargetZ - 14f, 28f, 28f));
            }
            else
            {
                foreach (var id in draftBlocks)
                    AddBlockRect(id);
                if (draftBlocks.Count == 0 && draftLabel.Length > 0)
                    highlightRects.Add(new Rect(draftX - 14f, draftZ - 14f, 28f, 28f));
            }

            StrategicMapHud.Instance.SetTargetHighlights(highlightRects, color);
        }

        void AddBlockRect(int blockId)
        {
            var block = CityBlocks.Get(blockId);
            if (block != null)
                highlightRects.Add(block.Union);
        }

        void RebuildOrders()
        {
            foreach (Transform old in ordersContent)
                Destroy(old.gameObject);

            var roster = director.Roster;
            if (roster == null || !outfit)
                return;

            if (ordersWeek)
            {
                var date = outfit.Campaign.DayName + " · DAY " + outfit.Campaign.Day;
                ordersWeek.text = outfit.TryGetHeadquarters(out _, out var hqBlock)
                    ? date + "  ·  HQ at block #" + hqBlock
                    : date + "  ·  the families are still settling in";
            }

            var y = -6f;

            if (ordersNote.Length > 0)
            {
                Paragraph(ordersContent, LedgerStyle.MonoItalic, 14f, LedgerStyle.RedPen, 4f, y,
                    OrdersInner, 40f, ordersNote, lineSpacing: 0f);
                y -= 44f;
            }

            y = OrdersCrewList(roster, y);
            if (ordersCrewId >= 0)
                y = OrdersJobCard(roster, y);
            y = OrdersInHand(roster, y);
            y = OrdersRises(y);
            y = OrdersRecord(y);

            ordersContent.sizeDelta = new Vector2(0f, Mathf.Max(400f, -y + 20f));
            var maxScroll = Mathf.Max(0f,
                ordersContent.sizeDelta.y - ordersViewport.rect.height);
            ordersScroll = Mathf.Clamp(ordersScroll, 0f, maxScroll);
            ordersContent.anchoredPosition = new Vector2(0f, ordersScroll);

            PushHighlights();
        }

        float OrdersHeader(string label, float y) =>
            Heading(ordersContent, 4f, y - 4f, OrdersInner, label, 12.5f);

        float OrdersCrewList(Roster roster, float y)
        {
            y = OrdersHeader("Lieutenants", y);

            if (roster.Crews.Count == 0)
            {
                Line(ordersContent, LedgerStyle.MonoItalic, 14.5f, LedgerStyle.InkDim, 4f, y,
                    OrdersInner, 20f, "Nobody runs a crew. Promote a man on the PERSONNEL page.");
                return y - 28f;
            }

            var book = outfit.Book;
            foreach (var crew in roster.Crews)
            {
                var lieutenant = roster.Find(crew.LieutenantId);
                var men = Outfit.CrewKit.MenOf(crew);
                var menOut = book.MenOut(crew.Id);
                var current = book.CurrentFor(crew.Id);
                var hasVehicle = Outfit.CrewKit.HasVehicle(roster, crew);
                var chosen = crew.Id == ordersCrewId;
                var crewId = crew.Id;

                var row = NewRect("Crew", ordersContent);
                PlaceTopLeft(row, 4f, y, OrdersInner, 26f);
                var surface = ClickSurface(row);
                RowButton(row, surface, () =>
                {
                    ordersCrewId = crewId;
                    selectedOrderId = -1;
                    draftBlocks.Clear();
                    draftLabel = "";
                    ordersNote = "";
                    RefreshTargeting();
                    dirty = true;
                });
                if (chosen)
                    Highlight(row, LedgerStyle.Highlighter, inset: 2f);

                var name = Text("Name", row, LedgerStyle.MonoBold, 14.5f, LedgerStyle.Ink,
                    TextAlignmentOptions.MidlineLeft);
                FillRow(name.rectTransform, 8f, 260f);
                name.text = lieutenant != null ? lieutenant.FullName : "?";

                var kit = Text("Kit", row, LedgerStyle.Mono, 14.5f, LedgerStyle.InkDim,
                    TextAlignmentOptions.MidlineLeft);
                FillRow(kit.rectTransform, 280f, 300f);
                kit.text = men + " men  ·  " + (hasVehicle ? "by car" : "on foot");

                var busy = Text("Out", row, LedgerStyle.Mono, 14f, LedgerStyle.InkDim,
                    TextAlignmentOptions.MidlineRight);
                FillRow(busy.rectTransform, OrdersInner - 260f, 252f);
                busy.text = LedgerText.MenOutLine(menOut, men);
                y -= 28f;

                // What they are doing this minute, in the lieutenant's own words - the
                // page redraws under the player as the hours run down.
                var doing = current == null
                    ? "idle at the front"
                    : LedgerText.OrderLabel(current.Type) + " - " +
                      LedgerText.StageLine(current);
                var queued = book.LiveCount(crew.Id) - (current != null ? 1 : 0);
                Line(ordersContent, LedgerStyle.MonoItalic, 13.5f,
                    current == null ? LedgerStyle.InkDim : LedgerStyle.Ink, 12f, y,
                    OrdersInner - 16f, 18f,
                    doing + (queued > 0 ? "   (" + queued + " more waiting)" : ""));
                y -= 22f;
            }

            return y - 4f;
        }

        float OrdersJobCard(Roster roster, float y)
        {
            var crew = roster.FindCrew(ordersCrewId);
            if (crew == null)
            {
                ordersCrewId = -1;
                return y;
            }

            y = OrdersHeader("The job", y);

            var spec = CurrentDraftSpec();

            // Category and type cyclers - the whole order table, four small tapes.
            Tape(ordersContent, "<", 4f, y, 26f, 22f, () =>
            {
                ordersCategoryIndex = (ordersCategoryIndex + CategoryCount - 1) % CategoryCount;
                ordersTypeIndex = 0;
                draftBlocks.Clear();
                draftLabel = "";
                dirty = true;
            });
            var category = Line(ordersContent, LedgerStyle.Type, 14f, LedgerStyle.InkDim, 36f, y,
                OrdersInner - 72f, 22f,
                LedgerText.CategoryLabel((Outfit.OrderCategory)ordersCategoryIndex)
                    .ToUpperInvariant(), TextAlignmentOptions.Center);
            category.characterSpacing = 2f;
            Tape(ordersContent, ">", 4f + OrdersInner - 30f, y, 26f, 22f, () =>
            {
                ordersCategoryIndex = (ordersCategoryIndex + 1) % CategoryCount;
                ordersTypeIndex = 0;
                draftBlocks.Clear();
                draftLabel = "";
                dirty = true;
            });
            y -= 26f;

            Tape(ordersContent, "<", 4f, y, 26f, 26f, () =>
            {
                var count = Mathf.Max(1, categorySpecs.Count);
                ordersTypeIndex = (ordersTypeIndex + count - 1) % count;
                draftBlocks.Clear();
                draftLabel = "";
                dirty = true;
            });
            var type = Line(ordersContent, LedgerStyle.Type, 17f, LedgerStyle.Ink, 36f, y,
                OrdersInner - 72f, 26f, LedgerText.OrderLabel(spec.Type).ToUpperInvariant(),
                TextAlignmentOptions.Center);
            type.characterSpacing = 3f;
            Tape(ordersContent, ">", 4f + OrdersInner - 30f, y, 26f, 26f, () =>
            {
                ordersTypeIndex = (ordersTypeIndex + 1) % Mathf.Max(1, categorySpecs.Count);
                draftBlocks.Clear();
                draftLabel = "";
                dirty = true;
            });
            y -= 32f;

            Line(ordersContent, LedgerStyle.Mono, 14f, LedgerStyle.InkDim, 4f, y, OrdersInner, 18f,
                LedgerText.RequirementLine(spec.PrimaryAttribute, spec.PrimaryFloorHalfSteps));
            y -= 20f;

            if (spec.PrimaryFloorHalfSteps > 0)
            {
                var best = Outfit.CrewKit.BestAt(roster, crew, spec.PrimaryAttribute);
                if (best < spec.PrimaryFloorHalfSteps)
                {
                    Line(ordersContent, LedgerStyle.MonoItalic, 14f, LedgerStyle.RedPen, 4f, y,
                        OrdersInner, 18f,
                        "Best man has " + LedgerText.Stars(best) + " - they can try anyway.");
                    y -= 20f;
                }
            }

            Paragraph(ordersContent, LedgerStyle.Mono, 14f, LedgerStyle.InkDim, 4f, y,
                OrdersInner, 38f, LedgerText.TargetModeHint(spec.Mode), lineSpacing: 0f);
            y -= 42f;

            var targetCount = spec.Mode == Outfit.TargetMode.Area
                ? draftBlocks.Count
                : (draftLabel.Length > 0 ? 1 : 0);

            var targets = Line(ordersContent,
                targetCount > 0 ? LedgerStyle.MonoBold : LedgerStyle.Mono, 14.5f,
                targetCount > 0 ? LedgerStyle.Ink : LedgerStyle.InkDim, 4f, y,
                OrdersInner - 100f, 20f,
                targetCount == 0
                    ? "TARGETS: none yet - unconfirmed"
                    : "TARGETS: " + (spec.Mode == Outfit.TargetMode.Area
                        ? draftBlocks.Count + " blocks"
                        : draftLabel) + " - unconfirmed");
            if (targetCount > 0)
            {
                Highlight((RectTransform)targets.transform, LedgerStyle.Highlighter, inset: 0f);
                Tape(ordersContent, "CLEAR", 4f + OrdersInner - 84f, y, 84f, 20f, () =>
                {
                    draftBlocks.Clear();
                    draftLabel = "";
                    ordersNote = "";
                    dirty = true;
                }, size: 11f);
            }
            y -= 24f;

            if (targetCount > 0)
            {
                var available = Outfit.CrewKit.MenOf(crew);
                if (draftMen > available)
                    draftMen = available < 1 ? 1 : available;

                var hasVehicle = Outfit.CrewKit.HasVehicle(roster, crew);
                var distance = DraftDistance();
                var driving = Outfit.CrewKit.BestAt(roster, crew, CharacterAttribute.Driving);
                // WHAT they drive, not merely whether: a jalopy and an armoured wagon are
                // hours apart across this city (OrderMath.TravelHours), and a card that
                // said "by car" for both was quoting a number the player could not read
                var vehicle = Outfit.CrewKit.VehicleOf(roster, crew);
                var travel = Outfit.OrderMath.TravelHours(distance, hasVehicle, driving,
                    Outfit.CrewKit.MachineTopOf(roster, crew));
                var work = Outfit.OrderMath.WorkHours(spec, targetCount, draftMen);
                var standing = spec.Resolution == Outfit.JobResolution.Standing;

                Line(ordersContent, LedgerStyle.Mono, 14f,
                    travel > 8f ? LedgerStyle.RedPen : LedgerStyle.InkDim, 4f, y,
                    OrdersInner, 18f,
                    "Travel: " + Mathf.RoundToInt(distance) + "m from HQ " +
                    (hasVehicle ? "by " + (vehicle.Length > 0 ? vehicle.ToUpperInvariant() : "car")
                                : "ON FOOT") +
                    " - " + LedgerText.Hours(travel) + " each way.");
                y -= 20f;

                Line(ordersContent, LedgerStyle.Mono, 14f, LedgerStyle.InkDim, 4f, y, OrdersInner,
                    18f, standing
                        ? "A standing watch - they hold it until you call them off."
                        : "The work itself: " + LedgerText.Hours(work) + " with " +
                          draftMen + (draftMen == 1 ? " man." : " men."));
                y -= 20f;

                if (!standing)
                {
                    var best = Outfit.CrewKit.BestAt(roster, crew, spec.PrimaryAttribute);
                    var organization = Outfit.CrewKit.BestAt(roster, crew,
                        CharacterAttribute.Organization);
                    // The other work the lieutenant is carrying - the same quantity
                    // OutfitDirector freezes onto a job as it starts (LiveCount less
                    // the job itself). Quoted for a job STARTED NOW: one sent to the
                    // back of a long queue may come up to a book that has emptied and
                    // do better than this line promised, which is the right way round
                    // for a quote to be wrong.
                    var depth = outfit.Book.LiveCount(crew.Id);
                    var chance = Outfit.OrderResolution.ChanceFor(spec, best, depth,
                        organization);
                    Line(ordersContent, LedgerStyle.Mono, 14f,
                        chance < 0.4f ? LedgerStyle.RedPen : LedgerStyle.InkDim, 4f, y,
                        OrdersInner, 18f,
                        spec.Resolution == Outfit.JobResolution.Street
                            ? "The street will decide this one."
                            : "Coming off: " + LedgerText.OddsLine(chance) + ".");
                    y -= 20f;
                }
                y -= 4f;

                Tape(ordersContent, "-", 4f, y, 26f, 24f, () =>
                {
                    if (draftMen > 1)
                    {
                        draftMen--;
                        dirty = true;
                    }
                });
                Line(ordersContent, LedgerStyle.MonoBold, 14f, LedgerStyle.Ink, 34f, y, 110f, 24f,
                    "MEN: " + draftMen, TextAlignmentOptions.Center);
                Tape(ordersContent, "+", 148f, y, 26f, 24f, () =>
                {
                    draftMen++;
                    dirty = true;
                });

                if (draftMen >= available)
                    Line(ordersContent, LedgerStyle.MonoItalic, 14f, LedgerStyle.InkDim, 190f, y,
                        OrdersInner - 190f, 24f, "That is the whole crew.");
                y -= 32f;

                var crewId = crew.Id;
                var issueSpec = spec;
                Tape(ordersContent, "SEND THEM", 4f, y, 200f, 28f, () =>
                {
                    var job = new Outfit.Job
                    {
                        CrewId = crewId,
                        Type = issueSpec.Type,
                        Men = draftMen,
                        TargetBlockId = draftBlockId,
                        TargetX = draftX,
                        TargetZ = draftZ,
                        TargetLabel = draftLabel,
                    };
                    job.BlockTargets.AddRange(draftBlocks);

                    var result = outfit.IssueOrder(job);
                    if (result.Ok)
                    {
                        draftBlocks.Clear();
                        draftLabel = "";
                        draftMen = 1;
                        ordersNote = "Issued. They go as soon as they are free.";
                    }
                    else
                        ordersNote = result.Reason;
                    dirty = true;
                });
                y -= 36f;
            }

            return y - 4f;
        }

        float OrdersInHand(Roster roster, float y)
        {
            y = OrdersHeader("In hand", y);
            var book = outfit.Book;

            if (book.Jobs.Count == 0)
            {
                Line(ordersContent, LedgerStyle.MonoItalic, 14.5f, LedgerStyle.InkDim, 4f, y,
                    OrdersInner, 18f, "Nobody is out. The city is somebody else's tonight.");
                return y - 26f;
            }

            foreach (var job in book.Jobs)
            {
                var crew = roster.FindCrew(job.CrewId);
                var lieutenant = crew != null ? roster.Find(crew.LieutenantId) : null;
                var underway = job.Stage != Outfit.JobStage.Queued;
                var chosen = job.Id == selectedOrderId;
                var jobId = job.Id;

                var row = NewRect("Job", ordersContent);
                PlaceTopLeft(row, 4f, y, OrdersInner - 100f, 24f);
                var surface = ClickSurface(row);
                RowButton(row, surface, () =>
                {
                    selectedOrderId = chosen ? -1 : jobId;
                    dirty = true;
                });
                if (chosen)
                    Highlight(row, LedgerStyle.Highlighter, inset: 2f);

                var text = Text("Line", row, LedgerStyle.Mono, 14.5f,
                    underway ? LedgerStyle.Ink : LedgerStyle.InkDim,
                    TextAlignmentOptions.MidlineLeft);
                FillRow(text.rectTransform, 8f, OrdersInner - 116f);
                text.text = (lieutenant != null ? lieutenant.Surname : "?") +
                    "  ·  " + LedgerText.OrderLabel(job.Type) + "  ·  " +
                    (job.BlockTargets.Count > 0
                        ? job.BlockTargets.Count + " blk"
                        : job.TargetLabel) +
                    "  ·  " + job.Men + " men  —  " + LedgerText.StageLine(job);

                // A job the crew is already out on cannot be reordered - they are
                // there - so the arrows only appear on work still waiting its turn.
                if (!underway)
                {
                    Tape(ordersContent, "^", 4f + OrdersInner - 92f, y, 26f, 22f,
                        () => { outfit.MoveOrder(jobId, -1); dirty = true; }, size: 11f);
                    Tape(ordersContent, "v", 4f + OrdersInner - 62f, y, 26f, 22f,
                        () => { outfit.MoveOrder(jobId, 1); dirty = true; }, size: 11f);
                }
                Tape(ordersContent, "X", 4f + OrdersInner - 32f, y, 26f, 22f, () =>
                {
                    outfit.CancelOrder(jobId);
                    if (selectedOrderId == jobId)
                        selectedOrderId = -1;
                    dirty = true;
                }, red: true, size: 11f);
                y -= 28f;
            }

            return y - 4f;
        }

        /// <summary>Who got better overnight. Only ever today's - the almanac's
        /// PERSONNEL page is where a man's whole sheet is read.</summary>
        float OrdersRises(float y)
        {
            if (outfit.Rises.Count == 0)
                return y;

            y = OrdersHeader("Come along", y);
            foreach (var rise in outfit.Rises)
            {
                Line(ordersContent, LedgerStyle.Mono, 14f, LedgerStyle.Ink, 4f, y,
                    OrdersInner, 18f,
                    rise.Name + " - " + LedgerText.AttributeLabel(rise.Attribute) +
                    " now " + LedgerText.Stars(rise.HalfSteps) + ".");
                y -= 20f;
            }

            return y - 4f;
        }

        float OrdersRecord(float y)
        {
            y = OrdersHeader("The record", y);

            if (outfit.Records.Count == 0)
            {
                Line(ordersContent, LedgerStyle.MonoItalic, 14.5f, LedgerStyle.InkDim, 4f, y,
                    OrdersInner, 18f, "Nothing has come back yet.");
                return y - 26f;
            }

            foreach (var record in outfit.Records)
            {
                var color = record.Outcome switch
                {
                    Outfit.OrderOutcome.Completed => LedgerStyle.Ink,
                    Outfit.OrderOutcome.Failed => LedgerStyle.RedPen,
                    _ => LedgerStyle.InkDim,
                };
                Line(ordersContent, LedgerStyle.Mono, 14f, color, 4f, y, OrdersInner, 18f,
                    "D" + record.Day + "  ·  " + record.Lieutenant + "  ·  " +
                    LedgerText.OrderLabel(record.Type) + "  ·  " + record.TargetSummary +
                    "  —  " + LedgerText.OutcomeLabel(record.Outcome).ToUpperInvariant() +
                    (record.Money != 0 ? "  " + LedgerText.Cash(record.Money) : ""));
                y -= 22f;
            }

            return y - 4f;
        }
    }
}
