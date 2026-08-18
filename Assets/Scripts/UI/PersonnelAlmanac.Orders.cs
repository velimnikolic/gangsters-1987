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
    /// ORDERS: the week's work, typed up as a memo. The lieutenants and how much of
    /// each crew's week is already spoken for; THE JOB - the order being drafted for
    /// the chosen crew, its target picked on the strategic map standing open to the
    /// right; the queue for the week; last week's record; and the red COMMIT tape
    /// that ends planning. This page is the map's IMapTargetingConsumer while a crew
    /// is chosen: area orders drag a box, point orders click a door.
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
        bool pendingCommit;
        readonly List<Rect> highlightRects = new List<Rect>();
        readonly List<int> scratchPast = new List<int>();

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

        Outfit.OrderSpec CurrentDraftSpec()
        {
            categorySpecs.Clear();
            var category = (Outfit.OrderCategory)ordersCategoryIndex;
            foreach (var spec in Outfit.OrderTable.Specs)
                if (spec.Category == category)
                    categorySpecs.Add(spec);
            if (ordersTypeIndex >= categorySpecs.Count)
                ordersTypeIndex = 0;
            return categorySpecs[ordersTypeIndex];
        }

        void RefreshTargeting()
        {
            var mine = StrategicMapHud.Targeting == (IMapTargetingConsumer)this;
            var wants = IsOpen && currentPage == LedgerPage.Orders && ordersCrewId >= 0;
            if (wants)
                StrategicMapHud.Targeting = this;
            else if (mine)
                StrategicMapHud.Targeting = null;
        }

        // ---- IMapTargetingConsumer ----

        public bool WantsArea => CurrentDraftSpec().Mode == Outfit.TargetMode.Area;

        public void OnAreaPreview(Rect worldXZ)
        {
            // Blocks light as the box swallows them - preview shares the capture logic
            // so what lights is exactly what a release would take.
            CaptureArea(worldXZ, preview: true);
            PushHighlights();
        }

        public void OnAreaSelected(Rect worldXZ)
        {
            CaptureArea(worldXZ, preview: false);
            selectedOrderId = -1;
            dirty = true;
        }

        public void OnPointClicked(Vector2 worldXZ, int blockId)
        {
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
            string firstReason = null;

            foreach (var block in CityBlocks.Blocks)
            {
                if (!block.Union.Overlaps(worldRect))
                    continue;
                var reason = EligibleBlockReason(spec.Type, block.Id);
                if (reason == null)
                    draftBlocks.Add(block.Id);
                else
                {
                    skipped++;
                    firstReason ??= reason;
                }
            }

            if (!preview)
                ordersNote = draftBlocks.Count + " block" +
                    (draftBlocks.Count == 1 ? "" : "s") + " taken" +
                    (skipped > 0 ? "; " + skipped + " skipped (" + firstReason + ")" : "") +
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

        string EligibleBlockReason(Outfit.OrderType type, int blockId)
        {
            if (outfit)
                outfit.CollectHoldings(holdings);
            else
                holdings.Clear();

            switch (type)
            {
                case Outfit.OrderType.Extort:
                    if (!BlockHasBusiness(blockId))
                        return "no businesses";
                    // A rival premise on the block shields it - you do not squeeze a
                    // street another family is standing on. Building-held, not block-held.
                    for (var gang = 0; gang < Gangs.GangCatalog.GangCount; gang++)
                        if (gang != Gangs.GangCatalog.PlayerGangId &&
                            Outfit.Turf.CountIn(holdings, blockId, gang) > 0)
                            return "held by " + Gangs.GangRegistry.NameOf(gang);
                    return null;

                case Outfit.OrderType.CollectProtection:
                case Outfit.OrderType.Patrol:
                    return Outfit.Turf.CountIn(
                        holdings, blockId, Gangs.GangCatalog.PlayerGangId) > 0
                        ? null : "not your turf";

                default:
                    return null;
            }
        }

        static bool BlockHasBusiness(int blockId)
        {
            foreach (var business in PropertyRegistry.Businesses)
                if (business && business.BlockId == blockId)
                    return true;
            return false;
        }

        float DraftDistance()
        {
            if (!outfit || !outfit.TryGetHeadquarters(out var hq, out _))
                return 0f;

            Vector2 target;
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
                if (counted == 0)
                    return 0f;
                target = sum / counted;
            }
            else if (draftLabel.Length > 0)
                target = new Vector2(draftX, draftZ);
            else
                return 0f;

            return Vector2.Distance(new Vector2(hq.x, hq.z), target);
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

            var plan = outfit ? outfit.Plan : null;
            Outfit.PlannedOrder selected = null;
            if (plan != null && selectedOrderId >= 0)
                foreach (var order in plan.Confirmed)
                    if (order.Id == selectedOrderId)
                        selected = order;

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
                ordersWeek.text = outfit.TryGetHeadquarters(out _, out var hqBlock)
                    ? "WEEK " + outfit.Campaign.Week + "  ·  HQ at block #" + hqBlock
                    : "WEEK " + outfit.Campaign.Week +
                      "  ·  the families are still settling in";

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
            y = OrdersThisWeek(roster, y);
            y = OrdersLastWeek(y);
            y = OrdersCommit(y);

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

            var plan = outfit.Plan;
            foreach (var crew in roster.Crews)
            {
                var lieutenant = roster.Find(crew.LieutenantId);
                var men = Outfit.CrewKit.MenOf(crew);
                var committed = plan.CommittedMen(crew.Id);
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

                var over = committed > men;
                var committedText = Text("Committed", row, LedgerStyle.Mono, 14f,
                    over ? LedgerStyle.RedPen : LedgerStyle.InkDim,
                    TextAlignmentOptions.MidlineRight);
                FillRow(committedText.rectTransform, OrdersInner - 260f, 252f);
                committedText.text = LedgerText.CommittedLine(committed, men);
                y -= 28f;

                // The week's labour, spent left to right - a pen bar.
                var fraction = men > 0 ? Mathf.Min(1f, committed / (float)men) : 0f;
                Bar(ordersContent, 12f, y, OrdersInner - 16f, 8f, fraction,
                    over ? LedgerStyle.RedPen : LedgerStyle.Ink);
                y -= 18f;
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
                ordersCategoryIndex = (ordersCategoryIndex + 4) % 5;
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
                ordersCategoryIndex = (ordersCategoryIndex + 1) % 5;
                ordersTypeIndex = 0;
                draftBlocks.Clear();
                draftLabel = "";
                dirty = true;
            });
            y -= 26f;

            Tape(ordersContent, "<", 4f, y, 26f, 26f, () =>
            {
                ordersTypeIndex = (ordersTypeIndex + categorySpecs.Count - 1)
                    % categorySpecs.Count;
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
                ordersTypeIndex = (ordersTypeIndex + 1) % categorySpecs.Count;
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
                var hasVehicle = Outfit.CrewKit.HasVehicle(roster, crew);
                var distance = DraftDistance();
                var travel = Outfit.OrderMath.TravelFraction(distance, hasVehicle);
                var needed = Outfit.OrderMath.MenNeeded(spec, targetCount, travel);

                Line(ordersContent, LedgerStyle.Mono, 14f,
                    travel > 0.5f ? LedgerStyle.RedPen : LedgerStyle.InkDim, 4f, y,
                    OrdersInner, 18f,
                    "Travel: " + Mathf.RoundToInt(distance) + "m from HQ " +
                    (hasVehicle ? "by car" : "ON FOOT") + " - eats " +
                    Mathf.RoundToInt(travel * 100f) + "% of each man's week.");
                y -= 20f;

                Line(ordersContent, LedgerStyle.Mono, 14f, LedgerStyle.InkDim, 4f, y, OrdersInner,
                    18f, "Needs about " + needed + " man-week" + (needed == 1 ? "" : "s") +
                         " to finish.");
                y -= 24f;

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

                if (Outfit.OrderMath.Undermanned(spec, targetCount, travel, draftMen))
                    Line(ordersContent, LedgerStyle.MonoItalic, 14f, LedgerStyle.RedPen, 190f, y,
                        OrdersInner - 190f, 24f, "Won't finish this week.");
                y -= 32f;

                var crewId = crew.Id;
                var confirmSpec = spec;
                Tape(ordersContent, "CONFIRM ORDER", 4f, y, 200f, 28f, () =>
                {
                    var order = new Outfit.PlannedOrder
                    {
                        CrewId = crewId,
                        Type = confirmSpec.Type,
                        Men = draftMen,
                        TargetBlockId = draftBlockId,
                        TargetX = draftX,
                        TargetZ = draftZ,
                        TargetLabel = draftLabel,
                    };
                    order.BlockTargets.AddRange(draftBlocks);

                    var result = outfit.ConfirmOrder(order);
                    if (result.Ok)
                    {
                        draftBlocks.Clear();
                        draftLabel = "";
                        draftMen = 1;
                        ordersNote = "Order confirmed - it is in the queue now.";
                    }
                    else
                        ordersNote = result.Reason;
                    dirty = true;
                });
                y -= 36f;
            }

            return y - 4f;
        }

        float OrdersThisWeek(Roster roster, float y)
        {
            y = OrdersHeader("This week", y);
            var plan = outfit.Plan;

            if (plan.Confirmed.Count == 0)
            {
                Line(ordersContent, LedgerStyle.MonoItalic, 14.5f, LedgerStyle.InkDim, 4f, y,
                    OrdersInner, 18f, "No orders in the queue.");
                return y - 26f;
            }

            // The line each crew crosses, computed once for the whole list.
            var pastAll = new HashSet<int>();
            foreach (var crew in roster.Crews)
            {
                Outfit.OrderMath.PastTheLine(plan, crew.Id,
                    Outfit.CrewKit.MenOf(crew), scratchPast);
                foreach (var id in scratchPast)
                    pastAll.Add(id);
            }

            foreach (var order in plan.Confirmed)
            {
                var crew = roster.FindCrew(order.CrewId);
                var lieutenant = crew != null ? roster.Find(crew.LieutenantId) : null;
                var past = pastAll.Contains(order.Id);
                var chosen = order.Id == selectedOrderId;
                var orderId = order.Id;

                var row = NewRect("Order", ordersContent);
                PlaceTopLeft(row, 4f, y, OrdersInner - 100f, 24f);
                var surface = ClickSurface(row);
                RowButton(row, surface, () =>
                {
                    selectedOrderId = chosen ? -1 : orderId;
                    dirty = true;
                });
                if (chosen)
                    Highlight(row, LedgerStyle.Highlighter, inset: 2f);

                var text = Text("Line", row, LedgerStyle.Mono, 14.5f,
                    past ? LedgerStyle.RedPen : LedgerStyle.Ink, TextAlignmentOptions.MidlineLeft);
                FillRow(text.rectTransform, 8f, OrdersInner - 116f);
                text.text = (lieutenant != null ? lieutenant.Surname : "?") +
                    "  ·  " + LedgerText.OrderLabel(order.Type) + "  ·  " +
                    (order.BlockTargets.Count > 0
                        ? order.BlockTargets.Count + " blk"
                        : order.TargetLabel) +
                    "  ·  " + order.Men + " men" +
                    (past ? "  — PAST THE LINE" : "");

                Tape(ordersContent, "^", 4f + OrdersInner - 92f, y, 26f, 22f,
                    () => { outfit.MoveOrder(orderId, -1); dirty = true; }, size: 11f);
                Tape(ordersContent, "v", 4f + OrdersInner - 62f, y, 26f, 22f,
                    () => { outfit.MoveOrder(orderId, 1); dirty = true; }, size: 11f);
                Tape(ordersContent, "X", 4f + OrdersInner - 32f, y, 26f, 22f, () =>
                {
                    outfit.RemoveOrder(orderId);
                    if (selectedOrderId == orderId)
                        selectedOrderId = -1;
                    dirty = true;
                }, red: true, size: 11f);
                y -= 28f;
            }

            return y - 4f;
        }

        float OrdersLastWeek(float y)
        {
            y = OrdersHeader("Last week", y);

            if (outfit.LastWeek.Count == 0)
            {
                Line(ordersContent, LedgerStyle.MonoItalic, 14.5f, LedgerStyle.InkDim, 4f, y,
                    OrdersInner, 18f, "No record yet - the first week is still open.");
                return y - 26f;
            }

            foreach (var record in outfit.LastWeek)
            {
                var color = record.Outcome switch
                {
                    Outfit.OrderOutcome.Completed => LedgerStyle.Ink,
                    Outfit.OrderOutcome.Failed => LedgerStyle.RedPen,
                    _ => LedgerStyle.InkDim,
                };
                Line(ordersContent, LedgerStyle.Mono, 14f, color, 4f, y, OrdersInner, 18f,
                    record.Lieutenant + "  ·  " + LedgerText.OrderLabel(record.Type) +
                    "  ·  " + record.TargetSummary + "  ·  " + record.Men + " men  —  " +
                    LedgerText.OutcomeLabel(record.Outcome).ToUpperInvariant());
                y -= 22f;
            }

            return y - 4f;
        }

        float OrdersCommit(float y)
        {
            y -= 10f;
            if (pendingCommit)
            {
                Paragraph(ordersContent, LedgerStyle.MonoItalic, 14.5f, LedgerStyle.RedPen, 4f, y,
                    OrdersInner, 40f,
                    "End planning? Wages fall due, stances turn, and the week runs as ordered.",
                    lineSpacing: 0f);
                y -= 44f;

                Tape(ordersContent, "COMMIT", 4f, y, 160f, 28f, () =>
                {
                    pendingCommit = false;
                    selectedOrderId = -1;
                    outfit.CommitWeek();
                    ordersNote = "The week is committed.";
                    dirty = true;
                }, red: true);
                Tape(ordersContent, "CANCEL", 172f, y, 120f, 28f, () =>
                {
                    pendingCommit = false;
                    dirty = true;
                });
            }
            else
                Tape(ordersContent, "COMMIT THE WEEK", 4f, y, OrdersInner, 30f, () =>
                {
                    pendingCommit = true;
                    dirty = true;
                }, red: true, size: 13f);

            return y - 40f;
        }
    }
}
