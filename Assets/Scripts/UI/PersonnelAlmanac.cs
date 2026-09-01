using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using LivingCity.Gameplay;
using LivingCity.Personnel;
using RoadDemo;
using static LivingCity.UI.LedgerKit;

namespace LivingCity.UI
{
    /// <summary>
    /// The outfit ledger, 1987: a manila file open on the boss's desk, filling the
    /// screen - centred, so an ultrawide monitor puts the desk lamp's light either
    /// side of it instead of stretching the file into a billboard. Six divider tabs
    /// - the morning paper, personnel, organization, finances, the armory catalogue,
    /// and the card index of families - on aged stock, punched down both edges, with a blotter
    /// strip of readouts under the masthead and the night's telex slips clipped in
    /// beneath it. Opened with P.
    ///
    /// Most pages are bookkeeping. ORGANIZATION is the administrative exception: it
    /// transfers real Characters and files block responsibility through the shared
    /// authority. Tactical street orders are still laid against the city on the map.
    ///
    /// Built for sixty men even though the game opens with the Boss and six staff: grouping, sorting and
    /// filtering are the screen, not decoration.
    ///
    /// The desk under the folder is a raycast target ON PURPOSE - it IS the modal
    /// shield: with it under the pointer every world picker's IsPointerOverGameObject
    /// guard stands down, so the city cannot be clicked through the book. sortingOrder
    /// 110: above every readout, below the context menu's 120.
    ///
    /// Esc is POLLED here and elsewhere, and polled input cannot be consumed - so the
    /// almanac exposes ClaimsEsc (true while open AND on the frame it closes) and every
    /// other Esc reader yields on it.
    ///
    /// Repaint is the versioned rebuild the HUDs use: a page is torn down and rebuilt
    /// when a director's Version or any local view state moves. Mutations are
    /// click-paced, so a few hundred objects per rebuild is the affordable choice. The
    /// one thing that moves every frame - the clock - is written in place instead, so
    /// a ticking second never costs a rebuild. This file is the shell; each page lives
    /// in its own partial.
    /// </summary>
    public sealed partial class PersonnelAlmanac : MonoBehaviour, IMapTargetingConsumer
    {
        const int SortingOrder = 110;

        // ------------------------------------------------------------ the fixture

        // The 1987 frame, and it is FULL BLEED: a chrome bar over the whole window, a
        // status rail down its left, a telex strip across the sheet's head and a footer
        // under it. Nothing is centred and nothing is a fixed document any more - the
        // frame IS the window, and the sheet takes whatever the window leaves.
        //
        // Which means the page-local numbers below cannot be const. They are measured
        // once per build, in MeasureFrame, and every page's own derived layout is
        // measured from them in the same pass. The canvas is ScaleWithScreenSize on a
        // 1920x1080 reference in Expand mode, so the frame is NEVER smaller than the
        // reference either way: full bleed only ever hands a page more room than it
        // was drawn for, never less.

        /// <summary>The chrome bar across the top - the title, the tabs and the way out.</summary>
        const float ChromeH = 44f;

        /// <summary>The status rail down the left. The design's 236, and fixed: it holds
        /// figures, and a figure column that reflows is a figure column nobody learns.</summary>
        const float RailW = 236f;

        /// <summary>The telex strip over the sheet.</summary>
        const float TelexH = 30f;

        /// <summary>The footer line under the sheet.</summary>
        const float FooterH = 26f;

        /// <summary>The window, in canvas reference units. Measured, never assumed.</summary>
        static float FrameW = 1920f;
        static float FrameH = 1080f;

        /// <summary>The sheet column: everything right of the rail and between the telex
        /// strip and the footer.</summary>
        static float SheetW = FrameW - RailW;
        static float SheetH = FrameH - ChromeH - TelexH - FooterH;

        // Sheet-local layout every page shares. The design's 24 units of margin either
        // side, 16 off the top and 30 clear at the foot.
        static float PageLeft = 24f;
        static float PageRight = SheetW - 24f;
        static float PageWidth = PageRight - PageLeft;

        /// <summary>Content starts here, under the sheet's top margin.</summary>
        static float PageTop = -16f;

        /// <summary>The last usable y on the sheet.</summary>
        static float PageBottom = -(SheetH - 30f);

        /// <summary>
        /// Measures the frame off the live canvas and hands every page its own derived
        /// layout. Called once before the book is built, and again whenever the window
        /// moves - a full-bleed frame that did not re-measure would be a frame that lied
        /// about where its own edges are.
        /// </summary>
        void MeasureFrame()
        {
            var frame = ReferenceFrame();
            FrameW = frame.x;
            FrameH = frame.y;

            SheetW = FrameW - RailW;
            SheetH = FrameH - ChromeH - TelexH - FooterH;

            PageLeft = 24f;
            PageRight = SheetW - 24f;
            PageWidth = PageRight - PageLeft;
            PageTop = -16f;
            PageBottom = -(SheetH - 30f);

            // Each page derives its own columns and pane heights off the four numbers
            // above. They were consts when the sheet was a fixed document; they are
            // measured now, in one pass, so no page can be painted against a stale frame.
            MeasureNewspaperLayout();
            MeasurePersonnelLayout();
            MeasureOrganizationLayout();
            MeasureFinancesLayout();
            MeasureArmoryLayout();
            MeasureDiplomacyLayout();
            MeasureOrdersLayout();
        }

        /// <summary>True while the book is open. Every world-input reader checks this -
        /// the keyboard half of the modal shield (the raycast-target desk is the pointer
        /// half).</summary>
        public static bool IsOpen { get; private set; }

        /// <summary>True while open AND on the frame the book closes: Esc readers poll,
        /// polling cannot consume, and Update order is arbitrary - a reader running after
        /// the close would otherwise act on the very press that closed the book.</summary>
        public static bool ClaimsEsc => IsOpen || Time.frameCount == lastCloseFrame;

        static int lastCloseFrame = -1;

        /// <summary>The tab the book was last left on. Closing and reopening returns the
        /// boss to the page he was working, not to the paper. Static so a scene reload
        /// keeps it; only pages with a tab are remembered - ORDERS is off the book.</summary>
        static LedgerPage lastTab = LedgerPage.Newspaper;

        // Static state outlives Play when domain reload is off - same fix as OverlayRegistry.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            IsOpen = false;
            lastCloseFrame = -1;
            lastTab = LedgerPage.Newspaper;
        }

        /// <summary>The book's tabs, in strip order. The paper is where a fresh game
        /// opens - the boss reads what the city thinks of him - and after that the book
        /// reopens wherever he closed it.</summary>
        public enum LedgerPage
        {
            Newspaper,
            Personnel,
            Organization,
            Finances,
            Armory,
            Diplomacy,
            Orders,
        }

        /// <summary>The tabs the folder actually shows, in strip order. ORDERS is the
        /// last page of the enum and deliberately has no tab: the orders panel is off
        /// the book. Its page root still builds, so SetPage can reach it in code.</summary>
        static readonly string[] TabNames =
            { "THE PAPER", "PERSONNEL", "ORGANIZATION", "FINANCES", "ARMORY", "FAMILIES" };

        /// <summary>What a real file's tabs say: the sheet is one leaf of a numbered
        /// file, and the ticker prints which one. Pure furniture, and the design's.</summary>
        static readonly int[] TabFolios = { 1, 4, 7, 10, 13, 16, 18 };
        const int Folios = 18;

        Canvas canvas;
        // The ledger is a full-screen modal document. Keep the exact enabled state
        // of every other canvas so closing it does not guess which HUDs were visible.
        readonly List<Canvas> suspendedCanvases = new List<Canvas>();
        GameObject page;

        /// <summary>The sheet body - the pane right of the rail and between the telex
        /// strip and the footer. Every page root is parented here.</summary>
        RectTransform paper;

        /// <summary>The frame the book was last built against. A full-bleed frame has
        /// to notice the window moving under it.</summary>
        Vector2 builtFrame;

        // ---- the chrome bar ----
        RectTransform chromeRoot;
        RectTransform tabStrip;

        // ---- the status rail ----
        RectTransform railRoot;
        RectTransform railFlags;
        TMP_Text railClock;
        TMP_Text railPayroll;
        TMP_Text railPayrollNote;

        // ---- the telex strip ----
        RectTransform telexRoot;
        RectTransform telexRun;
        RectTransform telexViewport;
        Image telexDot;
        TMP_Text telexStamp;
        float telexRunWidth;
        float telexOffset;

        // ---- the footer ----
        TMP_Text footerLeft;
        TMP_Text footerRight;

        LedgerPage currentPage = LedgerPage.Newspaper;
        readonly GameObject[] pageRoots = new GameObject[7];
        readonly Image[] tabFaces = new Image[6];
        readonly TMP_Text[] tabLabels = new TMP_Text[6];
        readonly RectTransform[] tabRects = new RectTransform[6];

        PersonnelDirector director;
        OutfitDirector outfit;
        Ambient.CityClock cityClock;

        /// <summary>Scratch for Turf reads - refilled from the markers on use.</summary>
        readonly List<Outfit.Turf.Holding> holdings = new List<Outfit.Turf.Holding>();

        int paintedVersion = -1;
        int paintedOutfitVersion = -1;
        int paintedGangVersion = -1;
        int paintedTerritoryVersion = -1;
        int paintedTerritoryObservationVersion = -1;
        bool dirty;

        void Start()
        {
            director = PersonnelDirector.Instance
                ? PersonnelDirector.Instance
                : FindAnyObjectByType<PersonnelDirector>();
            outfit = OutfitDirector.Instance
                ? OutfitDirector.Instance
                : FindAnyObjectByType<OutfitDirector>();
            cityClock = FindAnyObjectByType<Ambient.CityClock>();

            if (!director)
            {
                Debug.LogWarning("[Almanac] No PersonnelDirector in the scene - the " +
                                 "ledger is off.", this);
                enabled = false;
                return;
            }

            if (TMP_Settings.instance == null || TMP_Settings.defaultFontAsset == null)
            {
                Debug.LogWarning("[Almanac] No TMP default font - the ledger is disabled " +
                                 "until TMP essentials are imported " +
                                 "(Tools/City/Import TMP Essentials).", this);
                enabled = false;
                return;
            }

            EnsureEventSystem();
            BuildCanvas();
            BuildBook();
        }

        /// <summary>ContextMenuUI usually gets here first, but the almanac must not assume
        /// component start order - whichever runs first brings the EventSystem.</summary>
        static void EnsureEventSystem()
        {
            if (EventSystem.current)
                return;

            var host = new GameObject("EventSystem");
            host.AddComponent<EventSystem>();
            host.AddComponent<InputSystemUIInputModule>();
        }

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (!IsOpen && keyboard.pKey.wasPressedThisFrame &&
                (OrganizationTargetingActive || OrdersTargetingActive))
            {
                MapTargeting.Surface?.Dismiss();
                if (OrganizationTargetingActive)
                    CancelOrganizationTargetingAndReturn();
                else
                    CancelOrderTargetingAndReturn();
                return;
            }

            if (keyboard.pKey.wasPressedThisFrame)
            {
                if (IsOpen)
                    Close();
                else
                    Open();
            }

            if (!IsOpen)
            {
                // A summonable map has no cancel callback. When Esc closes it during an
                // Organization pick, return to the exact dossier instead of leaving an
                // invisible targeting consumer armed behind the world. A map that CANNOT
                // be summoned - the turf plate, which is a zoom level - is not up yet by
                // design, so the pick waits there instead of cancelling itself.
                var surface = MapTargeting.Surface;
                var mapGone = surface != null && surface.CanSummon && !surface.IsShowing;
                if (OrganizationTargetingActive && mapGone)
                    CancelOrganizationTargetingAndReturn();
                else if (OrdersTargetingActive && mapGone)
                    CancelOrderTargetingAndReturn();
                return;
            }

            // [ and ] turn the pages; the tabs are the pointer's way. Both walk the
            // TABS, not the page roots - a page with no tab is not in the book.
            if (keyboard.leftBracketKey.wasPressedThisFrame)
                SetPage((LedgerPage)(((int)currentPage + TabNames.Length - 1)
                    % TabNames.Length));
            if (keyboard.rightBracketKey.wasPressedThisFrame)
                SetPage((LedgerPage)(((int)currentPage + 1) % TabNames.Length));

            // F2: the sixty-man scale roster - the ledger is specified to stay usable
            // at sixty, and this is how a reviewer sees that without editor wiring.
            if (keyboard.f2Key.wasPressedThisFrame)
            {
                director.DebugSeedLarge(60);
                selectedId = -1;
                listScroll = 0f;
                dirty = true;
            }

            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                // Innermost state first - each Esc peels one layer, closing last.
                if (currentPage == LedgerPage.Organization &&
                    CloseOrganizationTransient())
                {
                    // The organization page consumed this Esc.
                }
                else if (pendingConfirm != Confirm.None)
                {
                    pendingConfirm = Confirm.None;
                    dirty = true;
                }
                else if (givePickerItemId >= 0)
                {
                    givePickerItemId = -1;
                    dirty = true;
                }
                else if (sortMenu && sortMenu.activeSelf)
                {
                    sortMenu.SetActive(false);
                }
                else
                {
                    Close();
                    return;
                }
            }

            // A full-bleed frame has to notice the window moving under it. A whole unit
            // of tolerance, because a dragged window edge lands on fractional reference
            // units and a book rebuilt on every fraction is a book that flickers.
            var frame = ReferenceFrame();
            if (Mathf.Abs(frame.x - builtFrame.x) > 1f ||
                Mathf.Abs(frame.y - builtFrame.y) > 1f)
            {
                RebuildForFrame();
                return;
            }

            UpdateScroll();
            RefreshClock();
            RunTelex();

            var outfitVersion = outfit ? outfit.Version : 0;
            var territoryVersion = TerritoryRuntime.Instance
                ? TerritoryRuntime.Instance.StateVersion
                : -1;
            var territoryObservationVersion = TerritoryRuntime.Instance
                ? TerritoryRuntime.Instance.ObservationVersion
                : -1;
            // The reader is holding the block on the organization sheet. A repaint
            // destroys the sheet whole and with it the model under their hand, and this
            // page is repainted often - an observation tick, a man moving, a gang
            // stirring. Whatever fell due waits for the hand to let go: the end of the
            // turn raises the flag itself, and nothing is lost because the versions are
            // only marked painted when the paint actually happens.
            if (blockCardModel != null && blockCardModel.Turning)
                return;

            if (dirty || paintedVersion != director.Version ||
                paintedOutfitVersion != outfitVersion ||
                paintedGangVersion != Gangs.GangRegistry.Version ||
                paintedTerritoryVersion != territoryVersion ||
                paintedTerritoryObservationVersion != territoryObservationVersion)
            {
                paintedVersion = director.Version;
                paintedOutfitVersion = outfitVersion;
                paintedGangVersion = Gangs.GangRegistry.Version;
                paintedTerritoryVersion = territoryVersion;
                paintedTerritoryObservationVersion = territoryObservationVersion;
                dirty = false;
                Repaint();
            }
        }

        /// <summary>Repaints the page that is showing. Each page owns its own rebuild;
        /// the rail, the telex strip and the footer are re-read on every pass because
        /// they are cheap and every page shows the same three.</summary>
        void Repaint()
        {
            switch (currentPage)
            {
                case LedgerPage.Newspaper:
                    RebuildNewspaper();
                    break;
                case LedgerPage.Personnel:
                    RebuildList();
                    RebuildDetail();
                    break;
                case LedgerPage.Organization:
                    RebuildOrganization();
                    break;
                case LedgerPage.Finances:
                    RebuildFinances();
                    break;
                case LedgerPage.Armory:
                    RebuildArmory();
                    break;
                case LedgerPage.Diplomacy:
                    RebuildDiplomacy();
                    break;
                case LedgerPage.Orders:
                    RebuildOrders();
                    break;
            }
            RefreshRail();
            RefreshTelex();
            RefreshFooter();
            RefreshFilterTapes();
        }

        /// <summary>The standalone menu scene's way in - Open is otherwise the P key's
        /// alone. False until the page is built and the roster seeded, so the scene
        /// polls from Update instead of racing Start order.</summary>
        public bool TryOpenBook()
        {
            if (IsOpen || !page || !director || director.Roster == null)
                return false;

            Open();
            return true;
        }

        void Open() => OpenAtPage(lastTab);

        /// <summary>
        /// Opens the same modal folder at a specific leaf. Normal P-key entry reopens on
        /// the tab the book was left on; map targeting names its own working page.
        /// </summary>
        void OpenAtPage(LedgerPage pageKind)
        {
            if (!page || director.Roster == null)
                return;

            if (pageKind != LedgerPage.Organization && OrganizationTargetingActive)
            {
                StopOrganizationTargeting();
                MapTargeting.Clear(this);
            }
            if (pageKind != LedgerPage.Orders && OrdersTargetingActive)
            {
                StopOrderTargeting();
                MapTargeting.Clear(this);
            }
            MapTargeting.Surface?.Dismiss();
            page.SetActive(true);
            IsOpen = true;
            SuspendOtherCanvases();
            SetPage(pageKind);
        }

        void Close()
        {
            if (page)
                page.SetActive(false);
            IsOpen = false;
            RestoreOtherCanvases();
            DismissOrganizationTransient();
            RefreshTargeting();
            MapTargeting.Surface?.SetTargetHighlights(null, Color.clear);
            lastCloseFrame = Time.frameCount;
            pendingConfirm = Confirm.None;
            lastRefusal = "";
            HideHoverNote();
            if (sortMenu)
                sortMenu.SetActive(false);
        }

        /// <summary>Play-stop or a scene torn down with the book open: the static flag
        /// would otherwise keep every world-input reader standing down in the next
        /// scene, and the map would keep sending clicks to a page that is gone.</summary>
        void OnDestroy()
        {
            RestoreOtherCanvases();
            IsOpen = false;
            StopOrganizationTargeting();
            StopOrderTargeting();
            DismissOrganizationTransient();
            RefreshTargeting();
        }

        /// <summary>
        /// Hide every other screen-space UI while the ledger is open. Runtime HUDs
        /// are separate canvases in this project, so this keeps the modal rule in one
        /// place instead of making every HUD know about every other HUD. Canvases that
        /// are already disabled are left alone and are not restored by us.
        /// </summary>
        void SuspendOtherCanvases()
        {
            RestoreOtherCanvases();

            var all = FindObjectsByType<Canvas>(FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (var i = 0; i < all.Length; i++)
            {
                var other = all[i];
                if (!other || !other.enabled || other == canvas)
                    continue;

                // Never suspend the ledger itself, or an ancestor canvas that owns it.
                // The latter is important if a scene embeds the ledger under a shared
                // root canvas: disabling that root would disable the modal too.
                if (other.transform.IsChildOf(canvas.transform) ||
                    canvas.transform.IsChildOf(other.transform))
                    continue;

                suspendedCanvases.Add(other);
                other.enabled = false;
            }
        }

        void RestoreOtherCanvases()
        {
            for (var i = 0; i < suspendedCanvases.Count; i++)
            {
                var other = suspendedCanvases[i];
                if (other)
                    other.enabled = true;
            }
            suspendedCanvases.Clear();
        }

        /// <summary>
        /// Turns the folder to a tab. Page STATE persists - the personnel selection,
        /// filters and scroll live in fields untouched here - only the transient
        /// interaction modes (an armed assign, a pending confirm, an open dropdown)
        /// drop, because a mode you cannot see must never swallow the next click.
        /// </summary>
        public void SetPage(LedgerPage pageKind)
        {
            currentPage = pageKind;
            if ((int)pageKind < TabNames.Length)
                lastTab = pageKind;
            for (var i = 0; i < pageRoots.Length; i++)
                if (pageRoots[i])
                    pageRoots[i].SetActive(i == (int)pageKind);

            // Leaving the orders page clears whatever it lit on the map.
            if (pageKind != LedgerPage.Orders)
                MapTargeting.Surface?.SetTargetHighlights(null, Color.clear);
            RefreshTargeting();

            if (pageKind != LedgerPage.Personnel)
            {
                pendingConfirm = Confirm.None;
                HideHoverNote();
                if (sortMenu)
                    sortMenu.SetActive(false);
            }
            if (pageKind != LedgerPage.Armory)
            {
                givePickerItemId = -1;
                armoryNote = "";
            }
            if (pageKind != LedgerPage.Organization)
                DismissOrganizationTransient();

            RefreshTabs();
            dirty = true;
        }

        // --------------------------------------------------------------- scrolling

        /// <summary>Reference units of travel per wheel notch. The roster snaps to
        /// whole rows so its rows stay on the paper's rules; the other regions glide.</summary>
        const float WheelStep = 34f;

        void UpdateScroll()
        {
            var mouse = Mouse.current;
            if (mouse == null)
                return;

            var wheel = mouse.scroll.ReadValue().y;
            if (wheel == 0f)
                return;

            var point = mouse.position.ReadValue();

            // A page nominates its scrolling regions; the wheel means nothing anywhere
            // else on the sheet. The armory nominates two - the merchandise board and the
            // stock book - and whichever the pointer sits over takes the wheel.
            RectTransform viewport;
            RectTransform content;
            switch (currentPage)
            {
                case LedgerPage.Personnel:
                    // Two regions on this page - the roll and the dossier - and
                    // whichever the pointer sits over takes the wheel.
                    if (cardViewport && RectTransformUtility
                            .RectangleContainsScreenPoint(cardViewport, point))
                    {
                        viewport = cardViewport;
                        content = cardContent;
                    }
                    else
                    {
                        viewport = listViewport;
                        content = listContent;
                    }
                    break;
                case LedgerPage.Organization:
                    viewport = organizationViewport;
                    content = organizationContent;
                    break;
                case LedgerPage.Armory:
                    if (catalogueViewport && RectTransformUtility
                            .RectangleContainsScreenPoint(catalogueViewport, point))
                    {
                        viewport = catalogueViewport;
                        content = catalogueContent;
                    }
                    else
                    {
                        viewport = stockViewport;
                        content = stockContent;
                    }
                    break;
                case LedgerPage.Orders:
                    viewport = ordersViewport;
                    content = ordersContent;
                    break;
                case LedgerPage.Diplomacy:
                    viewport = familiesViewport;
                    content = familiesContent;
                    break;
                default:
                    return;
            }

            if (!content)
                return;

            // Only while the pointer is over the region - the rest of the page is fixed
            // and the wheel must not surprise-scroll a list the player is not reading.
            if (!RectTransformUtility.RectangleContainsScreenPoint(viewport, point))
                return;

            var maxScroll = Mathf.Max(0f, content.sizeDelta.y - viewport.rect.height);
            if (viewport == listViewport)
            {
                var notch = wheel > 0f ? -1f : 1f;
                listScroll = Mathf.Clamp(listScroll + notch * RowHeight, 0f, maxScroll);
                listScroll = Mathf.Round(listScroll / RowHeight) * RowHeight;
                if (listScroll > maxScroll)
                    listScroll = Mathf.Floor(maxScroll / RowHeight) * RowHeight;
                content.anchoredPosition = new Vector2(0f, listScroll);
            }
            else if (viewport == cardViewport)
            {
                cardScroll = Mathf.Clamp(cardScroll - wheel * WheelStep, 0f, maxScroll);
                content.anchoredPosition = new Vector2(0f, cardScroll);
                // A note pinned to a row that just slid out from under the pointer
                // would hang there - the note goes with the roll.
                HideHoverNote();
            }
            else if (viewport == catalogueViewport)
            {
                catalogueScroll = Mathf.Clamp(catalogueScroll - wheel * WheelStep, 0f,
                    maxScroll);
                content.anchoredPosition = new Vector2(0f, catalogueScroll);
                // the "more above / more below" marks live on the fixed layer and are
                // drawn by the rebuild, so a roll has to ask for one
                dirty = true;
            }
            else if (viewport == familiesViewport)
            {
                // No rebuild on a notch: the dossier's cards carry twenty photographs,
                // and the window's one hint line is printed fixed for exactly that
                // reason (RebuildDiplomacy).
                familiesScroll = Mathf.Clamp(familiesScroll - wheel * WheelStep, 0f,
                    maxScroll);
                content.anchoredPosition = new Vector2(0f, familiesScroll);
            }
            else if (viewport == stockViewport)
            {
                stockScroll = Mathf.Clamp(stockScroll - wheel * WheelStep, 0f, maxScroll);
                content.anchoredPosition = new Vector2(0f, stockScroll);
            }
            else if (viewport == organizationViewport)
            {
                organizationScroll = Mathf.Clamp(
                    organizationScroll - wheel * WheelStep, 0f, maxScroll);
                content.anchoredPosition = new Vector2(0f, organizationScroll);
            }
            else
            {
                ordersScroll = Mathf.Clamp(ordersScroll - wheel * WheelStep, 0f, maxScroll);
                content.anchoredPosition = new Vector2(0f, ordersScroll);
            }
        }

        // ------------------------------------------------------------ construction

        void BuildCanvas()
        {
            var go = new GameObject("Personnel Ledger", typeof(RectTransform));
            go.transform.SetParent(transform, false);

            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            // Expand, NOT match-height. The frame is full bleed, and Expand is what
            // guarantees the canvas is at least 1920x1080 in reference units BOTH ways:
            // that is the floor every page was drawn against, so a wider or taller
            // window only ever hands the sheet more room, never less.
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            go.AddComponent<GraphicRaycaster>();

            // The scaler writes the canvas rect in its own layout pass. Force one now,
            // so the first MeasureFrame reads the real frame instead of a zero rect.
            Canvas.ForceUpdateCanvases();
        }

        /// <summary>
        /// The frame in canvas reference units: the CANVAS's own rect, which is what
        /// the scaler has already solved and what every rect on the sheet is laid in.
        ///
        /// Screen.width is deliberately NOT used. In the editor it answers the game
        /// view inside the player loop and the editor window outside it, so a watch
        /// built on it sees the frame move every time anything asks from the wrong
        /// place, and tears the book down for nothing. The canvas rect is one answer
        /// from one authority.
        ///
        /// An unsolved rect answers with the LAST frame we knew, never with an invented
        /// one. Inventing a frame is what makes the watch see a window that moved when
        /// it did not, and tear the book down every frame for a rect that had simply
        /// not been written yet.
        /// </summary>
        Vector2 ReferenceFrame()
        {
            if (canvas)
            {
                var rect = ((RectTransform)canvas.transform).rect;
                if (rect.width >= 1f && rect.height >= 1f)
                    // Expand mode guarantees the reference floor both ways; clamp
                    // anyway, so a frame nobody expected can never hand a page less
                    // room than it was drawn for.
                    lastGoodFrame = new Vector2(
                        Mathf.Max(1920f, rect.width), Mathf.Max(1080f, rect.height));
            }
            return lastGoodFrame;
        }

        /// <summary>The last frame the canvas actually reported. Seeded at the reference
        /// resolution, which is also the floor Expand mode guarantees.</summary>
        Vector2 lastGoodFrame = new Vector2(1920f, 1080f);

        void BuildBook()
        {
            MeasureFrame();
            builtFrame = new Vector2(FrameW, FrameH);

            page = new GameObject("Page", typeof(RectTransform));
            page.transform.SetParent(canvas.transform, false);
            Stretch((RectTransform)page.transform);

            // ---- the backdrop: the whole screen, and the modal shield ----
            //
            // OVERSCANNED on purpose. A CanvasScaler solves its rect against the game
            // view, and that rect can sit a few units inside the actual backbuffer -
            // enough for a hairline of the city to show down an edge of a modal that is
            // supposed to be the only thing on screen. Four hundred units of overscan
            // costs one quad and closes the question for good. It carries no mask, so
            // nothing clips it back to the canvas.
            //
            // It is a raycast target ON PURPOSE - it IS the shield: with it under the
            // pointer every world picker's IsPointerOverGameObject guard stands down,
            // so the city cannot be clicked through the ledger.
            var backdrop = NewRect("Backdrop", page.transform);
            Stretch(backdrop, -400f);
            var ground = Fill(backdrop, LedgerStyle.Ground);
            ground.raycastTarget = true;

            // The frame proper: canvas-sized, masked, and everything the ledger draws
            // lives inside it.
            var frame = NewRect("Frame", page.transform);
            Stretch(frame);
            frame.gameObject.AddComponent<RectMask2D>();

            BuildChrome(frame);
            BuildRail(frame);

            // ---- the sheet: right of the rail, between the telex strip and the footer ----
            paper = NewRect("Sheet", frame);
            PlaceTopLeft(paper, RailW, -(ChromeH + TelexH), SheetW, SheetH);
            Fill(paper, LedgerStyle.Ground);
            paper.gameObject.AddComponent<RectMask2D>();

            BuildTelex(frame);
            BuildFooter(frame);

            // ---- the pages, in tab order; each is a full-sheet root ----
            BuildNewspaperPage(paper);
            BuildPersonnelPage(paper);
            BuildOrganizationPage(paper);
            BuildFinancesPage(paper);
            BuildArmoryPage(paper);
            BuildDiplomacyPage(paper);
            BuildOrdersPage(paper);

            SetPage(currentPage);

            // Built active for TMP's sake (a TextMeshProUGUI only loads its font in
            // OnEnable, which never runs under an inactive parent), hidden until P.
            page.SetActive(false);
        }

        /// <summary>
        /// The window moved under a full-bleed frame. Everything painted was measured
        /// against the old one, so the book is torn down and built again. Page STATE
        /// survives: the leaf, the selection, the filters and every scroll offset are
        /// fields on this component, and nothing here touches them.
        /// </summary>
        void RebuildForFrame()
        {
            var wasOpen = page && page.activeSelf;
            if (page)
                Destroy(page);
            page = null;
            BuildBook();
            if (wasOpen)
            {
                page.SetActive(true);
                SetPage(currentPage);
            }
            dirty = true;
        }

        RectTransform NewPageRoot(RectTransform sheet, LedgerPage kind)
        {
            var root = NewRect("Page " + kind, sheet);
            Stretch(root);
            pageRoots[(int)kind] = root.gameObject;
            return root;
        }

        // ------------------------------------------------------------- the chrome bar

        /// <summary>The point size and letter-spacing of a tab's word.</summary>
        const float TabTextSize = 11f;
        const float TabTextSpacing = 8f;

        /// <summary>The way out, held to the far end of the bar.</summary>
        const float CloseW = 88f;

        /// <summary>The width a chrome tab needs: the design's 20 units of padding
        /// either side over letter-spaced mono caps. IBM Plex Mono is monospaced at
        /// 0.6 em (LedgerStyle documents the ratio), and TMP's characterSpacing is in
        /// hundredths of an em - so a tab's width is arithmetic and never costs a
        /// layout pass to find out.</summary>
        static float TabWidthFor(string label) =>
            40f + label.Length * (TabTextSize * 0.6f + TabTextSize * TabTextSpacing / 100f);

        /// <summary>
        /// The bar across the head of the frame: what the file is called, the six tabs,
        /// and the way out. The tabs are flat words in a dark bar now rather than
        /// divider tabs cut from manila - the ledger is a terminal, and a terminal does
        /// not pretend to be a folder.
        /// </summary>
        void BuildChrome(RectTransform frame)
        {
            chromeRoot = NewRect("Chrome", frame);
            PlaceTopLeft(chromeRoot, 0f, 0f, FrameW, ChromeH);
            Fill(chromeRoot, LedgerStyle.Chrome);

            // The title block is the rail's width, so the bar and the rail below it
            // share one edge all the way down the frame.
            var title = Line(chromeRoot, LedgerStyle.Condensed, 17f, LedgerStyle.ChromeTitle,
                16f, -11f, 136f, 24f, "OUTFIT LEDGER");
            title.characterSpacing = 6f;

            var file = Line(chromeRoot, LedgerStyle.Mono, 10f, LedgerStyle.ChromeFile,
                160f, -13f, 70f, 18f, "FILE 04-B");
            file.characterSpacing = 8f;

            VRule(chromeRoot, RailW, 0f, ChromeH, LedgerStyle.ChromeRule);

            BuildTabs(chromeRoot);
            BuildClose(chromeRoot);
        }

        /// <summary>The six tabs, packed left from the rail's edge. Masked, so a window
        /// too narrow for the strip clips the last word instead of running it over the
        /// way out.</summary>
        void BuildTabs(RectTransform chrome)
        {
            tabStrip = NewRect("Tabs", chrome);
            PlaceTopLeft(tabStrip, RailW, 0f, Mathf.Max(0f, FrameW - RailW - CloseW), ChromeH);
            tabStrip.gameObject.AddComponent<RectMask2D>();

            var x = 0f;
            for (var i = 0; i < TabNames.Length; i++)
            {
                var kind = (LedgerPage)i;
                var w = TabWidthFor(TabNames[i]);
                var rect = NewRect("Tab " + TabNames[i], tabStrip);
                PlaceTopLeft(rect, x, 0f, w, ChromeH);
                tabRects[i] = rect;

                // The idle face is the bar's own colour rather than nothing: an
                // invisible face cannot take a hover tint, and a tab that does not
                // answer the pointer does not read as a tab.
                var face = rect.gameObject.AddComponent<Image>();
                face.sprite = null;
                face.color = LedgerStyle.Chrome;
                face.raycastTarget = true;

                var button = rect.gameObject.AddComponent<Button>();
                button.targetGraphic = face;
                var colours = button.colors;
                colours.normalColor = Color.white;
                colours.highlightedColor = new Color(1.6f, 1.6f, 1.6f);
                colours.selectedColor = colours.highlightedColor;
                colours.pressedColor = new Color(0.8f, 0.8f, 0.8f);
                button.colors = colours;
                button.onClick.AddListener(() => SetPage(kind));

                var label = Text("Label", rect, LedgerStyle.Mono, TabTextSize,
                    LedgerStyle.TabIdle, TextAlignmentOptions.Center);
                Stretch(label.rectTransform);
                label.characterSpacing = TabTextSpacing;
                label.text = TabNames[i];

                tabFaces[i] = face;
                tabLabels[i] = label;
                x += w;
            }
        }

        void BuildClose(RectTransform chrome)
        {
            var rect = NewRect("Close", chrome);
            PlaceTopLeft(rect, FrameW - CloseW, 0f, CloseW, ChromeH);

            var face = rect.gameObject.AddComponent<Image>();
            face.sprite = null;
            face.color = LedgerStyle.TapeRed;
            face.raycastTarget = true;

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = face;
            var colours = button.colors;
            colours.normalColor = Color.white;
            colours.highlightedColor = new Color(1.25f, 1.25f, 1.25f);
            colours.selectedColor = colours.highlightedColor;
            colours.pressedColor = new Color(0.78f, 0.78f, 0.78f);
            button.colors = colours;
            button.onClick.AddListener(Close);

            var label = Text("Label", rect, LedgerStyle.Mono, 11f,
                LedgerStyle.TabActiveText, TextAlignmentOptions.Center);
            Stretch(label.rectTransform);
            label.characterSpacing = 11f;
            label.text = "CLOSE";
        }

        void RefreshTabs()
        {
            for (var i = 0; i < tabFaces.Length; i++)
            {
                if (!tabFaces[i])
                    continue;
                var active = i == (int)currentPage;
                tabFaces[i].color = active ? LedgerStyle.TabRed : LedgerStyle.Chrome;
                tabLabels[i].color = active
                    ? LedgerStyle.TabActiveText
                    : LedgerStyle.TabIdle;
            }
        }

        // ------------------------------------------------------------ the status rail

        /// <summary>How many readouts head the rail. The design's five.</summary>
        const int RailTiles = 5;

        /// <summary>The two capacity meters and the five rows under THE OUTFIT.</summary>
        const int RailMeters = 2;
        const int RailRows = 5;

        /// <summary>The four rows under THIS WEEK.</summary>
        const int RailWeekRows = 4;

        /// <summary>At most three things can be waiting on an answer at once.</summary>
        const int RailFlagSlots = 3;

        const float RailPad = 16f;
        const float RailInner = RailW - RailPad * 2f;
        const float RailTileH = 96f;
        const float RailFigureW = 74f;
        const float RailPayrollH = 68f;

        readonly TMP_Text[] hudValue = new TMP_Text[RailTiles];
        readonly TMP_Text[] hudNote = new TMP_Text[RailTiles];
        readonly RectTransform[] hudMeter = new RectTransform[RailTiles];

        readonly TMP_Text[] railMeterLabel = new TMP_Text[RailMeters];
        readonly TMP_Text[] railMeterText = new TMP_Text[RailMeters];
        readonly RectTransform[] railMeterFill = new RectTransform[RailMeters];
        readonly Image[] railMeterInk = new Image[RailMeters];

        readonly TMP_Text[] railRowLabel = new TMP_Text[RailRows];
        readonly TMP_Text[] railRowValue = new TMP_Text[RailRows];
        readonly TMP_Text[] railWeekLabel = new TMP_Text[RailWeekRows];
        readonly TMP_Text[] railWeekValue = new TMP_Text[RailWeekRows];

        readonly RectTransform[] railFlagSlot = new RectTransform[RailFlagSlots];
        readonly Image[] railFlagMark = new Image[RailFlagSlots];
        readonly TMP_Text[] railFlagText = new TMP_Text[RailFlagSlots];

        static readonly string[] RailLabels =
        {
            "ON THE CLOCK", "POLICE HEAT", "RESPECT", "TRIBUTE DUE", "IN THE SAFE",
        };

        /// <summary>
        /// The status rail: the blotter stood on its end and given room to say what it
        /// means. Five readouts head it - the day, the precinct, the street, what is
        /// owed and what is in the safe - then the outfit's two capacities and the
        /// counts that go with them, then the week, then whatever is actually waiting
        /// on the boss. The running payroll is pinned to its foot, because that is the
        /// one figure that is true on every page.
        ///
        /// Built once with fixed slots. Only the pip strips, the meter fills and the
        /// flags panel move on a repaint - the rest is written in place.
        /// </summary>
        void BuildRail(RectTransform frame)
        {
            railRoot = NewRect("Rail", frame);
            PlaceTopLeft(railRoot, 0f, -ChromeH, RailW, FrameH - ChromeH);
            Fill(railRoot, LedgerStyle.Rail);
            railRoot.gameObject.AddComponent<RectMask2D>();

            var cursor = 0f;
            for (var i = 0; i < RailTiles; i++)
            {
                var tile = NewRect("Tile " + RailLabels[i], railRoot);
                PlaceTopLeft(tile, 0f, -cursor, RailW, RailTileH);
                Rule(tile, 0f, -(RailTileH - 1f), RailW, LedgerStyle.ChromeRule);

                var label = Caps(tile, RailPad, -12f, RailInner, RailLabels[i], 9f,
                    LedgerStyle.RailLabel, 13f);
                label.overflowMode = TextOverflowModes.Ellipsis;

                hudValue[i] = Line(tile, LedgerStyle.Condensed, 21f, LedgerStyle.RailValue,
                    RailPad, -28f, RailInner - RailFigureW, 30f, "");
                hudValue[i].characterSpacing = 1f;
                hudValue[i].overflowMode = TextOverflowModes.Ellipsis;

                // The pips sit on the figure's own line, held to the right margin -
                // the design's meter is a reading OF the figure, not a bar under it.
                var meter = NewRect("Meter", tile);
                PlaceTopLeft(meter, RailPad, -28f, RailInner, 30f);
                hudMeter[i] = meter;

                // Two lines of room: "no business in the city answers to you" does not
                // fit on one at this measure, and a note that clips is not a note.
                hudNote[i] = Paragraph(tile, LedgerStyle.Mono, 9.5f, LedgerStyle.RailNote,
                    RailPad, -60f, RailInner, 32f, "", lineSpacing: 0f);
                hudNote[i].overflowMode = TextOverflowModes.Ellipsis;

                cursor += RailTileH;
            }

            railClock = hudNote[0];
            cursor = BuildRailOutfit(cursor);
            cursor = BuildRailWeek(cursor);
            BuildRailFlags(cursor);
            BuildRailPayroll();
        }

        float BuildRailOutfit(float cursor)
        {
            var height = 100f + RailRows * 21f + 8f;
            var panel = NewRect("The Outfit", railRoot);
            PlaceTopLeft(panel, 0f, -cursor, RailW, height);
            Rule(panel, 0f, -(height - 1f), RailW, LedgerStyle.ChromeRule);
            Caps(panel, RailPad, -12f, RailInner, "THE OUTFIT", 9f,
                LedgerStyle.RailKicker, 16f);

            for (var i = 0; i < RailMeters; i++)
            {
                var y = -(32f + i * 34f);
                railMeterLabel[i] = Line(panel, LedgerStyle.Mono, 9.5f,
                    LedgerStyle.RailLabel, RailPad, y, RailInner - RailFigureW, 14f, "");
                railMeterLabel[i].characterSpacing = 5f;
                railMeterLabel[i].overflowMode = TextOverflowModes.Ellipsis;

                railMeterText[i] = Line(panel, LedgerStyle.MonoBold, 13f,
                    LedgerStyle.RailValue, RailPad + RailInner - RailFigureW, y - 1f,
                    RailFigureW, 16f, "", TextAlignmentOptions.MidlineRight);

                var trough = NewRect("Trough", panel);
                PlaceTopLeft(trough, RailPad, y - 18f, RailInner, 6f);
                Fill(trough, LedgerStyle.RailTrough);

                var ink = NewRect("Fill", trough);
                PlaceTopLeft(ink, 0f, 0f, 0f, 6f);
                railMeterInk[i] = Fill(ink, LedgerStyle.RailValue);
                railMeterFill[i] = ink;
            }

            for (var i = 0; i < RailRows; i++)
            {
                var y = -(100f + i * 21f);
                Rule(panel, RailPad, y, RailInner, LedgerStyle.RailHair);
                railRowLabel[i] = Line(panel, LedgerStyle.Mono, 10f, LedgerStyle.RailLabel,
                    RailPad, y - 3f, RailInner - RailFigureW, 16f, "");
                railRowLabel[i].overflowMode = TextOverflowModes.Ellipsis;
                railRowValue[i] = Line(panel, LedgerStyle.MonoBold, 12.5f,
                    LedgerStyle.RailBright, RailPad + RailInner - RailFigureW, y - 3f,
                    RailFigureW, 16f, "", TextAlignmentOptions.MidlineRight);
            }

            return cursor + height;
        }

        float BuildRailWeek(float cursor)
        {
            var height = 30f + RailWeekRows * 21f + 8f;
            var panel = NewRect("This Week", railRoot);
            PlaceTopLeft(panel, 0f, -cursor, RailW, height);
            Rule(panel, 0f, -(height - 1f), RailW, LedgerStyle.ChromeRule);
            Caps(panel, RailPad, -12f, RailInner, "THIS WEEK", 9f,
                LedgerStyle.RailKicker, 16f);

            for (var i = 0; i < RailWeekRows; i++)
            {
                var y = -(30f + i * 21f);
                Rule(panel, RailPad, y, RailInner, LedgerStyle.RailHair);
                railWeekLabel[i] = Line(panel, LedgerStyle.Mono, 10f, LedgerStyle.RailLabel,
                    RailPad, y - 3f, RailInner - RailFigureW, 16f, "");
                railWeekLabel[i].overflowMode = TextOverflowModes.Ellipsis;
                railWeekValue[i] = Line(panel, LedgerStyle.MonoBold, 12.5f,
                    LedgerStyle.RailBright, RailPad + RailInner - RailFigureW, y - 3f,
                    RailFigureW, 16f, "", TextAlignmentOptions.MidlineRight);
            }

            return cursor + height;
        }

        void BuildRailFlags(float cursor)
        {
            var height = 28f + RailFlagSlots * 30f + 6f;
            railFlags = NewRect("Needs An Answer", railRoot);
            PlaceTopLeft(railFlags, 0f, -cursor, RailW, height);
            Rule(railFlags, 0f, -(height - 1f), RailW, LedgerStyle.ChromeRule);
            Caps(railFlags, RailPad, -11f, RailInner, "NEEDS AN ANSWER", 9f,
                LedgerStyle.RailKicker, 16f);

            for (var i = 0; i < RailFlagSlots; i++)
            {
                var slot = NewRect("Flag " + i, railFlags);
                PlaceTopLeft(slot, 0f, -(28f + i * 30f), RailW, 30f);
                railFlagSlot[i] = slot;

                var mark = NewRect("Mark", slot);
                PlaceTopLeft(mark, RailPad, -5f, 6f, 6f);
                railFlagMark[i] = Fill(mark, LedgerStyle.RailRed);

                railFlagText[i] = Paragraph(slot, LedgerStyle.Mono, 10f,
                    LedgerStyle.RailBright, RailPad + 14f, -1f, RailInner - 14f, 30f, "",
                    lineSpacing: 0f);
                railFlagText[i].overflowMode = TextOverflowModes.Ellipsis;
            }
        }

        /// <summary>The one figure that is true on every page, held to the rail's foot
        /// whatever the window's height - anchored to the bottom, not laid at a cursor.
        /// </summary>
        void BuildRailPayroll()
        {
            var pay = NewRect("Payroll", railRoot);
            pay.anchorMin = new Vector2(0f, 0f);
            pay.anchorMax = new Vector2(1f, 0f);
            pay.pivot = new Vector2(0.5f, 0f);
            pay.anchoredPosition = Vector2.zero;
            pay.sizeDelta = new Vector2(0f, RailPayrollH);
            Fill(pay, LedgerStyle.Rail);
            Rule(pay, 0f, 0f, RailW, LedgerStyle.ChromeRule);

            Caps(pay, RailPad, -12f, RailInner, "PAYROLL RUNNING", 9f,
                LedgerStyle.RailLabel, 13f);
            railPayroll = Line(pay, LedgerStyle.Condensed, 17f, LedgerStyle.RailRed,
                RailPad, -26f, RailInner, 24f, "");
            railPayrollNote = Line(pay, LedgerStyle.Mono, 9.5f, LedgerStyle.RailNote,
                RailPad, -50f, RailInner, 14f, "paid at midnight, worked or not");
        }

        /// <summary>The heat scale, in the words a precinct would use.</summary>
        static string HeatWord(int heat) =>
            heat < 10 ? "LOW"
            : heat < 25 ? "NOTICED"
            : heat < 50 ? "WATCHED"
            : heat < 80 ? "HOT"
            : "HUNTED";

        /// <summary>How much of the city's held property is the outfit's, as a word.</summary>
        static string RespectWord(int steps) =>
            steps <= 1 ? "THIN"
            : steps <= 3 ? "SLIGHT"
            : steps <= 5 ? "KNOWN"
            : steps <= 7 ? "STRONG"
            : "FEARED";

        void RefreshRail()
        {
            if (railRoot == null || !railRoot.gameObject.activeSelf)
                return;

            TallyOutfit();
            RefreshRailTiles();
            RefreshRailOutfit();
            RefreshRailWeek();
            RefreshRailFlags();

            var perDay = Outfit.Wages.DailyPayroll(director.Roster);
            if (railPayroll)
                railPayroll.text = LedgerText.Cash(perDay) + " / day";
            if (railPayrollNote)
                railPayrollNote.text = perDay > 0
                    ? "paid at midnight, worked or not"
                    : "nobody is drawing pay";
        }

        void RefreshRailTiles()
        {
            var roster = director.Roster;
            var perDay = Outfit.Wages.DailyPayroll(roster);

            // ---- the clock. RefreshClock writes the note in place every frame; the
            // figure is the date, which only moves at the day tick.
            var day = outfit ? outfit.Campaign.Day : 1;
            var date = News.NewsDate.FromClockDay(day - 1);
            hudValue[0].text = date.Stamped();
            hudValue[0].color = LedgerStyle.RailValue;
            SetMeter(0, 0, 0, LedgerStyle.RailValue);
            RefreshClock();

            // ---- what the police think.
            var heat = outfit ? outfit.Heat : 0;
            var heatSteps = Mathf.Clamp(Mathf.CeilToInt(heat / 10f), 0, 10);
            hudValue[1].text = HeatWord(heat);
            hudValue[1].color = heat < 25 ? LedgerStyle.RailAmber : LedgerStyle.RailRed;
            SetMeter(1, heatSteps, 10, LedgerStyle.RailAmber);
            hudNote[1].text = heat == 0
                ? "nobody downtown has your name yet"
                : heat + " on the precinct's board";

            // ---- what the street thinks: the outfit's share of the held city.
            var mine = 0;
            var all = 0;
            if (outfit)
            {
                outfit.CollectHoldings(holdings);
                mine = Outfit.Turf.CountOf(holdings, Gangs.GangCatalog.PlayerGangId);
                all = holdings.Count;
                holdings.Clear();
            }
            var respectSteps = all > 0 ? Mathf.Clamp(Mathf.RoundToInt(10f * mine / all), 0, 10) : 0;
            hudValue[2].text = RespectWord(respectSteps);
            hudValue[2].color = LedgerStyle.RailAmber;
            SetMeter(2, respectSteps, 10, LedgerStyle.RailAmber);
            hudNote[2].text = all > 0
                ? mine + " of " + all + " houses in the city are yours"
                : "no business in the city answers to you";

            // ---- what is kicked up, and when the man calls for it.
            var levy = outfit ? outfit.Tribute.Nearest() : null;
            if (levy == null)
            {
                hudValue[3].text = "NOTHING";
                hudValue[3].color = LedgerStyle.RailValue;
                SetMeter(3, 0, Outfit.Tribute.CycleDays, LedgerStyle.RailAmber);
                hudNote[3].text = "no house in this city is above you";
            }
            else
            {
                var houseName = Gangs.GangCatalog.Names[levy.GangId];
                var hourNow = cityClock ? cityClock.Hour : 0f;
                // The meter runs DOWN as the day comes: full is a fresh cycle, empty
                // is the man on the step. Overdue leaves it empty and turns the figure.
                var away = Mathf.Clamp(levy.DueDay - day, 0, Outfit.Tribute.CycleDays);
                hudValue[3].text = LedgerText.Cash(levy.Amount);
                hudValue[3].color = levy.Overdue || away <= 1
                    ? LedgerStyle.RailRed
                    : LedgerStyle.RailValue;
                SetMeter(3, away, Outfit.Tribute.CycleDays, LedgerStyle.RailAmber);
                hudNote[3].text = levy.Overdue
                    ? "OVERDUE to " + houseName + " · they have not forgotten"
                    : "to " + houseName + " · " +
                      LedgerText.DueInPlain(levy.DueDay, day, hourNow);
            }

            // ---- what is in the safe, and how long it lasts at this burn.
            var safe = outfit ? outfit.Accounts.Safe : 0;
            var runway = perDay > 0 ? safe / perDay : 0;
            hudValue[4].text = LedgerText.Cash(safe);
            // Three days' burn is the mark: below it the outfit is one bad night from
            // missing a payroll, and the figure says so before the arithmetic does.
            hudValue[4].color = perDay > 0 && runway <= 3
                ? LedgerStyle.RailRed
                : LedgerStyle.RailGold;
            SetMeter(4, Mathf.Clamp(runway, 0, 10), 10, LedgerStyle.RailSafeGold);
            hudNote[4].text = perDay > 0
                ? "-" + LedgerText.Cash(perDay).Substring(1) + " a day · " +
                  (runway >= 10 ? "ten days or better" : runway + " days of payroll left")
                : "nobody is drawing pay";
        }

        void RefreshRailOutfit()
        {
            // Each rail meter has a colour of its own in the design - men are warm
            // stock, ground is green - and both turn red only at the ceiling.
            SetRailMeter(0, "MEN ON THE BOOKS", railMen, railManCap, LedgerStyle.RailBright);
            SetRailMeter(1, "BLOCKS HELD", railHeld, railBlockCap, LedgerStyle.RailGreen);

            SetRailRow(railRowLabel, railRowValue, 0, "lieutenants",
                railLieutenants.ToString(), LedgerStyle.RailBright);
            SetRailRow(railRowLabel, railRowValue, 1, "posted, earning",
                railPosted.ToString(),
                railPosted > 0 ? LedgerStyle.RailGreen : LedgerStyle.RailLabel);
            SetRailRow(railRowLabel, railRowValue, 2, "idle, drawing pay",
                railIdle.ToString(),
                railIdle > 0 ? LedgerStyle.RailRed : LedgerStyle.RailLabel);
            SetRailRow(railRowLabel, railRowValue, 3, "hurt or jailed",
                railHurt.ToString(),
                railHurt > 0 ? LedgerStyle.RailGold : LedgerStyle.RailLabel);
            SetRailRow(railRowLabel, railRowValue, 4, "carrying a piece",
                railArmed + " / " + railMen, LedgerStyle.RailBright);
        }

        void RefreshRailWeek()
        {
            SetRailRow(railWeekLabel, railWeekValue, 0, "blocks contested",
                railContested.ToString(),
                railContested > 0 ? LedgerStyle.RailGold : LedgerStyle.RailLabel);
            SetRailRow(railWeekLabel, railWeekValue, 1, "houses at war",
                railAtWar.ToString(),
                railAtWar > 0 ? LedgerStyle.RailRed : LedgerStyle.RailLabel);
            SetRailRow(railWeekLabel, railWeekValue, 2, "stock signed out",
                railIssued + " / " + railStock, LedgerStyle.RailBright);
            SetRailRow(railWeekLabel, railWeekValue, 3, "profit this week",
                LedgerText.Cash(railProfit),
                railProfit < 0 ? LedgerStyle.RailRed : LedgerStyle.RailGreen);
        }

        void RefreshRailFlags()
        {
            var slot = 0;
            if (railOverCapacity > 0)
                SetRailFlag(slot++, LedgerStyle.RailRed,
                    railOverCapacity == 1
                        ? "a lieutenant is over capacity — he refuses the next man"
                        : railOverCapacity + " lieutenants are over capacity — they refuse the next man");
            if (railIdle > 0)
                SetRailFlag(slot++, LedgerStyle.RailRed,
                    railIdle == 1
                        ? "one man idle under you, paid at midnight for nothing"
                        : railIdle + " men idle under you, paid at midnight for nothing");
            if (railPaperOnly > 0)
                SetRailFlag(slot++, LedgerStyle.RailGold,
                    railPaperOnly == 1
                        ? "one block on paper we do not hold on the street"
                        : railPaperOnly + " blocks on paper we do not hold on the street");

            for (var i = slot; i < RailFlagSlots; i++)
                if (railFlagSlot[i])
                    railFlagSlot[i].gameObject.SetActive(false);
            if (railFlags)
                railFlags.gameObject.SetActive(slot > 0);
        }

        void SetRailFlag(int index, Color mark, string text)
        {
            if (index >= RailFlagSlots || !railFlagSlot[index])
                return;
            railFlagSlot[index].gameObject.SetActive(true);
            railFlagMark[index].color = mark;
            railFlagText[index].text = text;
        }

        void SetRailMeter(int index, string label, int current, int maximum, Color ink)
        {
            if (!railMeterLabel[index])
                return;
            var full = maximum > 0 && current >= maximum;
            var colour = full ? LedgerStyle.RailRed : ink;

            railMeterLabel[index].text = label;
            railMeterText[index].text = current + " / " + maximum;
            railMeterText[index].color = colour;
            railMeterInk[index].color = colour;
            var fraction = maximum > 0 ? Mathf.Clamp01((float)current / maximum) : 0f;
            railMeterFill[index].sizeDelta = new Vector2(RailInner * fraction, 6f);
        }

        static void SetRailRow(TMP_Text[] labels, TMP_Text[] values, int index,
            string label, string value, Color colour)
        {
            if (!labels[index])
                return;
            labels[index].text = label;
            values[index].text = value;
            values[index].color = colour;
        }

        /// <summary>Redraws one rail tile's pips, held to the right margin. The strip is
        /// rebuilt rather than tinted because the step COUNT moves, not just the colour.
        /// The figure's own colour is each tile's business: a full bar is bad news on the
        /// heat and good news on the runway, so nothing here may touch it.</summary>
        void SetMeter(int index, int filled, int steps, Color colour)
        {
            var meter = hudMeter[index];
            if (!meter)
                return;
            for (var i = meter.childCount - 1; i >= 0; i--)
                Destroy(meter.GetChild(i).gameObject);
            if (steps <= 0)
                return;
            var x = RailInner - LedgerV2.PipsWidth(steps, 5f, 7f);
            LedgerV2.Pips(meter, x, -15f, steps, filled, colour, 5f, 10f, 7f);
        }

        // -------------------------------------------------------------- what is true

        // The figures the rail and the telex strip both read. Tallied once a repaint,
        // from the same authorities the pages use - never from a page's own scratch,
        // because the rail is on every page and the pages are not.
        int railMen, railManCap, railHeld, railBlockCap, railLieutenants;
        int railPosted, railIdle, railHurt, railArmed;
        int railContested, railAtWar, railIssued, railStock, railProfit;
        int railOverCapacity, railPaperOnly, railAwaiting;
        string railFirstIdle = "";

        /// <summary>Scratch for the armed tally - a man, not a gun, per entry.</summary>
        readonly HashSet<int> railArmedMen = new HashSet<int>();

        void TallyOutfit()
        {
            railMen = railManCap = railHeld = railBlockCap = railLieutenants = 0;
            railPosted = railIdle = railHurt = railArmed = 0;
            railContested = railAtWar = railIssued = railStock = railProfit = 0;
            railOverCapacity = railPaperOnly = railAwaiting = 0;
            railFirstIdle = "";

            var roster = director.Roster;
            if (roster != null)
            {
                for (var i = 0; i < roster.Members.Count; i++)
                {
                    var member = roster.Members[i];
                    if (member.Gone)
                        continue;
                    railMen++;
                    if (member.Rank == Rank.Lieutenant)
                        railLieutenants++;
                    if (member.Status != CharacterStatus.Active)
                        railHurt++;
                    if (roster.AssignmentOf(member.Id).Kind == AssignmentKind.Pool &&
                        member.Rank == Rank.Hood)
                    {
                        railIdle++;
                        if (railFirstIdle.Length == 0)
                            railFirstIdle = member.FullName;
                    }
                    else
                    {
                        railPosted++;
                    }
                }

                // The stock book counts ITEMS signed out; "carrying a piece" counts
                // MEN. A man holding two guns is one armed man, and the rail must not
                // read as though he were two.
                railArmedMen.Clear();
                railStock = roster.Equipment.Count;
                for (var i = 0; i < roster.Equipment.Count; i++)
                {
                    var item = roster.Equipment[i];
                    if (item.OwnerId == RosterEquipment.Unheld)
                        continue;
                    railIssued++;
                    if (RosterOps.IsWeapon(item.Kind))
                        railArmedMen.Add(item.OwnerId);
                }
                railArmed = railArmedMen.Count;
            }

            // The command file: capacity, and what is named on our paper. Read here
            // rather than borrowed off the organization page, because the rail is
            // painted on all six pages and that page's scratch is only fresh on one.
            var query = director != null ? director.Organization : null;
            if (query != null && query.TryGetBoss(out var boss))
            {
                organizationLeaders.Clear();
                organizationLeaders.Add(boss);
                query.CollectLieutenants(organizationScratch);
                organizationLeaders.AddRange(organizationScratch);
                ReadOrganizationPaper(query);
                ReadOrganizationControl();

                var capacity = query.CapacityOf(boss.Id);
                railManCap = capacity.Manpower.Maximum;
                railBlockCap = capacity.Blocks.Maximum;

                for (var i = 0; i < organizationLeaders.Count; i++)
                    if (query.CapacityOf(organizationLeaders[i].Id).IsOverCapacity)
                        railOverCapacity++;

                railHeld = CountHeldBlocks();
                railPaperOnly = CountPaperOnly();
            }

            var territory = TerritoryRuntime.Instance?.PlayerQuery;
            if (territory != null)
            {
                var ids = territory.BlockIds;
                for (var i = 0; i < ids.Count; i++)
                    if (ControlOf(ids[i]) == BlockControl.Contested)
                        railContested++;
            }

            if (outfit)
            {
                var gangs = Gangs.GangRegistry.Gangs;
                for (var i = 0; i < gangs.Count; i++)
                    if (gangs[i].Id != Gangs.GangCatalog.PlayerGangId &&
                        outfit.Relations.StanceWith(gangs[i].Id) == Outfit.Stance.War)
                        railAtWar++;

                railAwaiting = outfit.Filings.AwaitingCount;

                var sheet = outfit.Accounts.Current;
                if (sheet != null)
                    railProfit = Outfit.BalanceMath.Profit(
                        sheet, Outfit.Wages.DailyPayroll(roster));
            }
        }

        /// <summary>The clock is the one thing on the rail that moves by itself, so it
        /// is written in place - one SetText a frame instead of a rebuild.</summary>
        void RefreshClock()
        {
            if (!railClock)
                return;

            var day = outfit ? outfit.Campaign.Day : 1;
            if (cityClock)
            {
                var hour = cityClock.Hour;
                var h = Mathf.FloorToInt(hour);
                var m = Mathf.FloorToInt(hour % 1f * 60f);
                var s = Mathf.FloorToInt(hour * 3600f % 60f);
                railClock.SetText("day {0} · {1:00}:{2:00}:{3:00}", day, h, m, s);
                return;
            }

            // No city clock in the standalone ledger scene - the rail says so rather
            // than inventing a time.
            railClock.SetText("day {0} · books still open", day);
        }

        // -------------------------------------------------------------- the telex strip

        /// <summary>Reference units the wire runs a second. Fixed rather than a duration,
        /// so a short night and a long one read at the same pace.</summary>
        const float TelexSpeed = 46f;

        /// <summary>The badge that holds the wire's source, and the day stamp that
        /// closes it.</summary>
        const float TelexBadgeW = 186f;
        const float TelexStampW = 74f;

        /// <summary>
        /// The telex strip across the head of the sheet: what came in overnight, run
        /// past on a wire. Every line is derived from live state - it is a READOUT
        /// written as intelligence, never an inbox, and nothing on the wire can be
        /// pressed. A refusal leads it, because the book must never swallow a NO.
        /// </summary>
        void BuildTelex(RectTransform frame)
        {
            telexRoot = NewRect("Telex", frame);
            PlaceTopLeft(telexRoot, RailW, -ChromeH, SheetW, TelexH);
            Fill(telexRoot, LedgerStyle.TelexPaper);
            Rule(telexRoot, 0f, -(TelexH - 1f), SheetW, LedgerStyle.TelexRule);

            var badge = NewRect("Source", telexRoot);
            PlaceTopLeft(badge, 0f, 0f, TelexBadgeW, TelexH);
            Fill(badge, LedgerStyle.TelexBadge);

            var dot = NewRect("Lamp", badge);
            PlaceTopLeft(dot, 14f, -12f, 6f, 6f);
            telexDot = Fill(dot, LedgerStyle.TelexDot);

            var source = Caps(badge, 29f, -10f, TelexBadgeW - 39f,
                "TELEX · 4TH PRECINCT", 9f, LedgerStyle.TelexBadgeInk, 12f);
            source.font = LedgerStyle.MonoBold;

            telexViewport = NewRect("Wire", telexRoot);
            PlaceTopLeft(telexViewport, TelexBadgeW, 0f,
                Mathf.Max(0f, SheetW - TelexBadgeW - TelexStampW), TelexH);
            telexViewport.gameObject.AddComponent<RectMask2D>();

            telexRun = NewRect("Run", telexViewport);
            PlaceTopLeft(telexRun, 0f, 0f, 0f, TelexH);

            telexStamp = Caps(telexRoot, SheetW - TelexStampW - 14f, -10f, TelexStampW,
                "", 9f, LedgerStyle.TelexStamp, 10f, TextAlignmentOptions.MidlineRight);
        }

        /// <summary>What the wire is carrying, and how loud. Urgent flashes, warn is
        /// amber and plain is ink - the design's three voices.</summary>
        enum TelexVoice
        {
            Plain,
            Warn,
            Urgent,
        }

        readonly List<(string Text, TelexVoice Voice)> telexMessages =
            new List<(string, TelexVoice)>();

        void RefreshTelex()
        {
            if (telexRoot == null || !telexRoot.gameObject.activeSelf || !telexRun)
                return;

            ComposeTelex();

            for (var i = telexRun.childCount - 1; i >= 0; i--)
                Destroy(telexRun.GetChild(i).gameObject);

            // Laid out once to measure, then laid a second time end to end: a wire that
            // ran out would show a gap the width of the sheet before it came round.
            telexRunWidth = LayTelexRun(0f);
            LayTelexRun(telexRunWidth);
            telexRun.sizeDelta = new Vector2(telexRunWidth * 2f, TelexH);
            if (telexRunWidth > 0f)
                telexOffset = Mathf.Repeat(telexOffset, telexRunWidth);

            if (telexStamp)
                telexStamp.text = "DAY " + (outfit ? outfit.Campaign.Day : 1);
        }

        /// <summary>Lays the whole run once from x and answers what it came to.</summary>
        float LayTelexRun(float x)
        {
            const float pad = 22f;
            var cursor = x;
            for (var i = 0; i < telexMessages.Count; i++)
            {
                var (body, voice) = telexMessages[i];
                var width = pad * 2f + body.Length * (11.5f * 0.6f + 11.5f * 0.02f) + 16f;

                var slot = NewRect("Wire " + i, telexRun);
                PlaceTopLeft(slot, cursor, 0f, width, TelexH);

                var mark = NewRect("Mark", slot);
                PlaceTopLeft(mark, pad, -12f, 5f, 5f);
                Fill(mark, voice == TelexVoice.Urgent ? LedgerStyle.TelexDot
                    : voice == TelexVoice.Warn ? LedgerStyle.TelexDotWarn
                    : LedgerStyle.TelexDotPlain);

                var text = Line(slot,
                    voice == TelexVoice.Plain ? LedgerStyle.Mono : LedgerStyle.MonoBold,
                    11.5f,
                    voice == TelexVoice.Urgent ? LedgerStyle.TelexUrgent
                    : voice == TelexVoice.Warn ? LedgerStyle.TelexWarn
                    : LedgerStyle.TelexPlain,
                    pad + 15f, -8f, width - pad - 15f, 16f, body);
                text.characterSpacing = 2f;

                cursor += width;
            }
            return cursor - x;
        }

        /// <summary>
        /// The night's wire. A refusal leads - the book must never swallow a NO - then
        /// whatever the page the boss is on has to say, then what is true of the whole
        /// outfit, then the three lines that belong to this page alone.
        /// </summary>
        void ComposeTelex()
        {
            telexMessages.Clear();

            if (lastRefusal.Length > 0)
                telexMessages.Add((lastRefusal, TelexVoice.Urgent));

            var context = PageNote();
            if (context.Length > 0)
                telexMessages.Add((context, TelexVoice.Plain));

            if (railOverCapacity > 0)
                telexMessages.Add((railOverCapacity == 1
                    ? "A LIEUTENANT IS OVER CAPACITY — he refuses the next man"
                    : railOverCapacity + " LIEUTENANTS ARE OVER CAPACITY — they refuse the next man",
                    TelexVoice.Urgent));
            if (railIdle > 0)
                telexMessages.Add((railIdle == 1
                    ? railFirstIdle + " is idle under you — paid at midnight for nothing"
                    : railIdle + " men idle under you, " + railFirstIdle +
                      " among them — paid at midnight for nothing", TelexVoice.Urgent));
            if (railPaperOnly > 0)
                telexMessages.Add((railPaperOnly +
                    (railPaperOnly == 1 ? " block named" : " blocks named") +
                    " on paper we do not hold on the street", TelexVoice.Warn));
            if (railAwaiting > 0)
                telexMessages.Add(("The outfit is still ruling on " + railAwaiting +
                    (railAwaiting > 1 ? " orders" : " order"), TelexVoice.Plain));

            ComposePageTelex();

            // A wire with nothing on it is a machine that has failed, not a quiet night.
            if (telexMessages.Count == 0)
                telexMessages.Add(("Nothing on the wire. The city is quiet and the books " +
                    "are open.", TelexVoice.Plain));
        }

        void ComposePageTelex()
        {
            var heat = outfit ? outfit.Heat : 0;
            switch (currentPage)
            {
                case LedgerPage.Newspaper:
                    telexMessages.Add((heat == 0
                        ? "No file downtown carries your name. Nothing has been asked " +
                          "about anyone on this sheet."
                        : "The precinct has " + heat + " against you. Expect to be looked " +
                          "at on any job worked in daylight.", TelexVoice.Plain));
                    if (outfit != null && outfit.Records.Count > 0)
                    {
                        var record = outfit.Records[outfit.Records.Count - 1];
                        telexMessages.Add((record.Lieutenant + "'s men worked " +
                            LedgerText.OrderLabel(record.Type).ToLowerInvariant() + " at " +
                            record.TargetSummary + ". " +
                            LedgerText.OutcomeLabel(record.Outcome) + ".", TelexVoice.Plain));
                    }
                    break;

                case LedgerPage.Personnel:
                    telexMessages.Add(("Payroll runs " +
                        LedgerText.Cash(Outfit.Wages.DailyPayroll(director.Roster)) +
                        " a day · the jailed and the hurt keep drawing", TelexVoice.Plain));
                    telexMessages.Add((railHurt > 0
                        ? railHurt + (railHurt == 1 ? " man hurt or jailed" : " men hurt or jailed") +
                          " and still on the books"
                        : "Every man on the books is fit",
                        railHurt > 0 ? TelexVoice.Warn : TelexVoice.Plain));
                    telexMessages.Add((railPosted + " of " + railMen +
                        " men are posted and earning", TelexVoice.Plain));
                    break;

                case LedgerPage.Organization:
                    telexMessages.Add(("Each man answers to exactly one man above him",
                        TelexVoice.Plain));
                    telexMessages.Add((railHeld > 0
                        ? railHeld + (railHeld == 1 ? " block held" : " blocks held") + " on the street"
                        : "The outfit holds no ground at all",
                        railHeld > 0 ? TelexVoice.Plain : TelexVoice.Urgent));
                    telexMessages.Add((railContested > 0
                        ? railContested + (railContested == 1 ? " block" : " blocks") +
                          " contested — another house is pushing on it"
                        : "Nobody is pushing on our ground today",
                        railContested > 0 ? TelexVoice.Warn : TelexVoice.Plain));
                    break;

                case LedgerPage.Finances:
                    telexMessages.Add((railProfit < 0
                        ? "The books are running at a loss of " +
                          LedgerText.Cash(-railProfit) + " today"
                        : "The books are ahead by " + LedgerText.Cash(railProfit) + " today",
                        railProfit < 0 ? TelexVoice.Urgent : TelexVoice.Plain));
                    telexMessages.Add(("Books close at midnight · the men are paid then, " +
                        "worked or not", TelexVoice.Plain));
                    telexMessages.Add(("No bookkeeper on the books — every figure is " +
                        "struck by hand", TelexVoice.Plain));
                    break;

                case LedgerPage.Armory:
                    telexMessages.Add(("Mail-order catalogue · no names, no paperwork, " +
                        "kerbside delivery", TelexVoice.Plain));
                    telexMessages.Add(("A man with no piece is a man who runs", TelexVoice.Warn));
                    telexMessages.Add((railIssued + " of " + railStock +
                        " items signed out · stock signed out is stock the precinct can " +
                        "trace back to a face", TelexVoice.Plain));
                    break;

                case LedgerPage.Diplomacy:
                    telexMessages.Add((railAtWar > 0
                        ? railAtWar + (railAtWar == 1 ? " house is" : " houses are") +
                          " at war with you — their men engage yours on sight"
                        : "Nobody in this city is at war with you",
                        railAtWar > 0 ? TelexVoice.Urgent : TelexVoice.Plain));
                    telexMessages.Add(("Stance changes take effect when the week is " +
                        "committed, never mid-plan", TelexVoice.Plain));
                    break;

                case LedgerPage.Orders:
                    telexMessages.Add(("This sheet asks · the outfit answers", TelexVoice.Plain));
                    break;
            }
        }

        /// <summary>
        /// What the page the boss is reading has to say for itself right now - the
        /// armory's last word, the classified column's, or the man picked off the
        /// printout. Empty when the page has nothing of its own to add.
        /// </summary>
        string PageNote()
        {
            if (currentPage == LedgerPage.Armory && armoryNote.Length > 0)
                return armoryNote;
            if (currentPage == LedgerPage.Newspaper && classifiedNote.Length > 0)
                return classifiedNote;

            var roster = director.Roster;
            if (currentPage != LedgerPage.Personnel || roster == null || selectedId < 0)
                return "";

            var member = roster.Find(selectedId);
            if (member == null)
                return "";

            var post = roster.AssignmentOf(member.Id);
            // AssignmentLine ANSWERS the crew's name for a crew posting, so it has to be
            // handed one - an empty string prints an empty middle.
            var crewName = "";
            if (post.Kind == AssignmentKind.Crew)
            {
                var crew = roster.FindCrew(post.CrewId);
                var lieutenant = crew != null ? roster.Find(crew.LieutenantId) : null;
                crewName = lieutenant != null
                    ? LedgerText.CrewName(lieutenant.Surname) : "a crew";
            }
            return member.FullName + " · " + LedgerText.AssignmentLine(post, crewName) +
                   " · " + LedgerText.Cash(Outfit.Wages.WageFor(member)) + " a day";
        }

        /// <summary>The wire runs, and the source lamp blinks with it. Both are written
        /// in place off unscaled time - the city's clock must not slow the machine when
        /// the game is paused under the modal.</summary>
        void RunTelex()
        {
            if (telexRun && telexRunWidth > 0f)
            {
                telexOffset = Mathf.Repeat(
                    telexOffset + Time.unscaledDeltaTime * TelexSpeed, telexRunWidth);
                telexRun.anchoredPosition = new Vector2(-telexOffset, 0f);
            }

            if (telexDot)
            {
                // The design's steps(1, end) keyframe: lit for the first 55% of the
                // cycle, dim for the rest. A fade would read as a glow, not a lamp.
                var lit = Time.unscaledTime % 1.1f < 0.605f;
                var colour = LedgerStyle.TelexDot;
                colour.a = lit ? 1f : 0.25f;
                telexDot.color = colour;
            }
        }

        // ------------------------------------------------------------------ the footer

        /// <summary>The line under the sheet: the date on the left, the keys and the
        /// folio on the right.</summary>
        void BuildFooter(RectTransform frame)
        {
            var foot = NewRect("Footer", frame);
            PlaceTopLeft(foot, RailW, -(FrameH - FooterH), SheetW, FooterH);
            Fill(foot, LedgerStyle.Chrome);

            footerLeft = Caps(foot, 20f, -7f, SheetW * 0.4f, "", 10f,
                LedgerStyle.FooterInk, 8f);
            footerRight = Caps(foot, SheetW - 20f - SheetW * 0.55f, -7f, SheetW * 0.55f,
                "", 10f, LedgerStyle.FooterInkDim, 8f, TextAlignmentOptions.MidlineRight);
        }

        void RefreshFooter()
        {
            if (!footerLeft)
                return;

            var day = outfit ? outfit.Campaign.Day : 1;
            footerLeft.text = News.NewsDate.FromClockDay(day - 1).Stamped();
            footerRight.text = "[ ] TURN THE PAGE   [ESC] SHUT THE FILE   |   PAGE " +
                TabFolios[(int)currentPage].ToString("00") + " OF " + Folios;
        }
    }
}
