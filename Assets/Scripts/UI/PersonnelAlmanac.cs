using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using LivingCity.Gameplay;
using LivingCity.Personnel;
using static LivingCity.UI.LedgerKit;

namespace LivingCity.UI
{
    /// <summary>
    /// The outfit ledger, 1987: a manila file open on the boss's desk, filling the
    /// screen - centred, so an ultrawide monitor puts the desk lamp's light either
    /// side of it instead of stretching the file into a billboard. Five divider tabs
    /// - the morning paper, the personnel roll, the books, the armory catalogue, the
    /// card index of families - on aged stock, punched down both edges, with a blotter
    /// strip of readouts under the masthead and the night's telex slips clipped in
    /// beneath it. Opened with P.
    ///
    /// The ledger is READ-ONLY bookkeeping in spirit: it reports where the men are and
    /// what they cost. Orders are laid against the city on the map, never here.
    ///
    /// Built for sixty men even though the game opens with six: grouping, sorting and
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

        /// <summary>The file's own width. It is FIXED, and the folder is centred in
        /// whatever canvas it finds itself in: the sheet is a physical document, and a
        /// document does not get wider because the monitor did. On 16:9 that leaves a
        /// hand's breadth of desk either side; on an ultrawide, more desk.</summary>
        const float BookW = 1600f;
        const float BookH = 1032f;

        /// <summary>How far the folder's top edge sits below the screen's, leaving room
        /// for the divider tabs to stand above it.</summary>
        const float BookTop = -44f;

        /// <summary>The manila shell showing round the sheet inside it.</summary>
        const float PaperInset = 9f;

        const float PaperW = BookW - PaperInset * 2f;
        const float PaperH = BookH - PaperInset * 2f;

        /// <summary>The divider tabs, on the folder's top edge.</summary>
        const float TabH = 38f;
        const float TabActiveH = 44f;
        const float TabGap = 6f;
        const float TabInset = 26f;

        /// <summary>The punched holes: a column of them this far in from each edge.</summary>
        const float PunchCentre = 15f;

        // Paper-local layout every page shares.
        const float PageLeft = 42f;
        const float PageRight = PaperW - 42f;
        const float PageWidth = PageRight - PageLeft;

        // The masthead, the blotter and the telex row are shared chrome: every page
        // starts under them, and the ticker closes the sheet under every page.
        const float RuleY = -120f;
        const float HudY = -134f;
        const float HudH = 88f;
        const float TelexY = -230f;
        const float TelexH = 52f;

        /// <summary>Content starts under the telex slips.</summary>
        const float PageTop = -288f;

        /// <summary>The last usable y on the paper - the ticker owns what is below.</summary>
        const float PageBottom = -(PaperH - 44f);

        const float TickerY = -(PaperH - 30f);

        /// <summary>True while the book is open. Every world-input reader checks this -
        /// the keyboard half of the modal shield (the raycast-target desk is the pointer
        /// half).</summary>
        public static bool IsOpen { get; private set; }

        /// <summary>True while open AND on the frame the book closes: Esc readers poll,
        /// polling cannot consume, and Update order is arbitrary - a reader running after
        /// the close would otherwise act on the very press that closed the book.</summary>
        public static bool ClaimsEsc => IsOpen || Time.frameCount == lastCloseFrame;

        static int lastCloseFrame = -1;

        // Static state outlives Play when domain reload is off - same fix as OverlayRegistry.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetForPlay()
        {
            IsOpen = false;
            lastCloseFrame = -1;
        }

        /// <summary>The book's tabs, in strip order. The paper is the entry page - the
        /// boss opens the folder on what the city thinks of him, then turns to work.</summary>
        public enum LedgerPage
        {
            Newspaper,
            Personnel,
            Finances,
            Armory,
            Diplomacy,
            Orders,
        }

        /// <summary>The tabs the folder actually shows, in strip order. ORDERS is the
        /// last page of the enum and deliberately has no tab: the orders panel is off
        /// the book. Its page root still builds, so SetPage can reach it in code.</summary>
        static readonly string[] TabNames =
            { "THE PAPER", "PERSONNEL", "FINANCES", "ARMORY", "FAMILIES" };

        /// <summary>What a real file's tabs say: the sheet is one leaf of a numbered
        /// file, and the ticker prints which one. Pure furniture, and the design's.</summary>
        static readonly int[] TabFolios = { 1, 4, 7, 11, 15, 17 };
        const int Folios = 18;

        Canvas canvas;
        GameObject page;
        RectTransform paper;
        TMP_Text headerCount;
        TMP_Text headerDate;
        TMP_Text headerClock;
        TMP_Text headerSafe;
        TMP_Text tickerLeft;
        TMP_Text tickerRight;
        TMP_Text hudClock;
        RectTransform hudRoot;
        RectTransform telexRoot;

        LedgerPage currentPage = LedgerPage.Newspaper;
        readonly GameObject[] pageRoots = new GameObject[6];
        readonly Image[] tabFaces = new Image[6];
        readonly TMP_Text[] tabLabels = new TMP_Text[6];
        readonly RectTransform[] tabRects = new RectTransform[6];

        PersonnelDirector director;
        OutfitDirector outfit;
        PlayerMafioso player;
        Ambient.CityClock cityClock;

        /// <summary>Scratch for Turf reads - refilled from the markers on use.</summary>
        readonly List<Outfit.Turf.Holding> holdings = new List<Outfit.Turf.Holding>();

        int paintedVersion = -1;
        int paintedOutfitVersion = -1;
        int paintedGangVersion = -1;
        bool dirty;

        void Start()
        {
            director = PersonnelDirector.Instance
                ? PersonnelDirector.Instance
                : FindAnyObjectByType<PersonnelDirector>();
            outfit = OutfitDirector.Instance
                ? OutfitDirector.Instance
                : FindAnyObjectByType<OutfitDirector>();
            player = FindAnyObjectByType<PlayerMafioso>();
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

            if (keyboard.pKey.wasPressedThisFrame)
            {
                if (IsOpen)
                    Close();
                else
                    Open();
            }

            if (!IsOpen)
                return;

            // The book must never hide the WASTED card (sortingOrder 95, under this 110).
            // Arrest cannot happen while the book is open - H is behind the modal guard -
            // so death is the one ending that can arrive mid-read.
            if (player && player.IsDead)
            {
                Close();
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
                if (pendingConfirm != Confirm.None)
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

            UpdateScroll();
            RefreshClock();

            var outfitVersion = outfit ? outfit.Version : 0;
            if (dirty || paintedVersion != director.Version ||
                paintedOutfitVersion != outfitVersion ||
                paintedGangVersion != Gangs.GangRegistry.Version)
            {
                paintedVersion = director.Version;
                paintedOutfitVersion = outfitVersion;
                paintedGangVersion = Gangs.GangRegistry.Version;
                dirty = false;
                Repaint();
            }
        }

        /// <summary>Repaints the page that is showing. Each page owns its own rebuild;
        /// the masthead, the blotter, the telex row and the ticker are re-read on every
        /// pass because they are cheap and read the same state.</summary>
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
            RefreshHeader();
            RefreshBlotter();
            RefreshTelex();
            RefreshTicker();
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

        void Open()
        {
            if (!page || director.Roster == null)
                return;

            page.SetActive(true);
            IsOpen = true;
            // The file covers the glass, so nothing can be left live under it. The
            // strategic map is a screen of its own again: if it was up, it comes down
            // with the folder opening, and it does not come back by itself.
            if (StrategicMapHud.Instance && StrategicMapHud.IsOpen)
                StrategicMapHud.Instance.Close();
            // The folder always opens on the morning paper - the day's frame - and
            // the working pages keep their state for when the boss turns to them.
            SetPage(LedgerPage.Newspaper);
        }

        void Close()
        {
            if (page)
                page.SetActive(false);
            IsOpen = false;
            RefreshTargeting();
            if (StrategicMapHud.Instance)
                StrategicMapHud.Instance.SetTargetHighlights(null, Color.clear);
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
            IsOpen = false;
            RefreshTargeting();
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
            for (var i = 0; i < pageRoots.Length; i++)
                if (pageRoots[i])
                    pageRoots[i].SetActive(i == (int)pageKind);

            // Leaving the orders page clears whatever it lit on the map.
            if (pageKind != LedgerPage.Orders && StrategicMapHud.Instance)
                StrategicMapHud.Instance.SetTargetHighlights(null, Color.clear);
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
            // Expand, NOT match-height: match-height clips the folder's sides on any
            // window narrower than 16:9. Expand guarantees the canvas is at least
            // 1920x1080 in reference units both ways, so the fixed folder fits whole -
            // and on an ultrawide the extra reference width simply becomes more desk
            // either side of a centred file.
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            go.AddComponent<GraphicRaycaster>();
        }

        void BuildBook()
        {
            page = new GameObject("Page", typeof(RectTransform));
            page.transform.SetParent(canvas.transform, false);
            Stretch((RectTransform)page.transform);

            // ---- the desk: the whole screen, and the modal shield ----
            var desk = NewRect("Desk", page.transform);
            Stretch(desk);
            var deskImage = desk.gameObject.AddComponent<Image>();
            deskImage.sprite = null;
            deskImage.color = LedgerStyle.DeskDeep;
            deskImage.raycastTarget = true;
            desk.gameObject.AddComponent<RectMask2D>();

            // Walnut at the top where the light is, near-black at the foot. The stripe
            // over it is the grain: two per cent, and meant not to be seen.
            var fall = NewRect("Fall", desk);
            Stretch(fall);
            Gradient(fall, LedgerStyle.DeskFall);
            var stripe = NewRect("Grain", desk);
            Stretch(stripe);
            Texture(stripe, LedgerStyle.DeskStripe, new Color(1f, 1f, 1f, 0.35f),
                2600f, 1200f, 32f);

            // Dust and the grain of an old finish. Finer than the stripe and at a
            // different pitch, so the two never beat against each other into a moire.
            var dust = NewRect("Speckle", desk);
            Stretch(dust);
            Texture(dust, LedgerStyle.Speckle, new Color(1f, 1f, 1f, 0.22f),
                2600f, 1200f, 48f);

            // The light over the desk, above and behind the file - the one source
            // every shadow on the sheet agrees with.
            var lamp = NewRect("Lamp", desk);
            lamp.anchorMin = lamp.anchorMax = new Vector2(0.5f, 1f);
            lamp.pivot = new Vector2(0.5f, 0.5f);
            lamp.anchoredPosition = new Vector2(0f, 60f);
            lamp.sizeDelta = new Vector2(2600f, 1500f);
            var lampImage = lamp.gameObject.AddComponent<RawImage>();
            lampImage.texture = LedgerStyle.RadialLight;
            lampImage.color = LedgerStyle.Lamp;
            lampImage.raycastTarget = false;

            // ---- the folder: manila, a shade darker than its pages, centred ----
            var folder = NewRect("Folder", desk);
            folder.anchorMin = folder.anchorMax = new Vector2(0.5f, 1f);
            folder.pivot = new Vector2(0.5f, 1f);
            folder.anchoredPosition = new Vector2(0f, BookTop);
            folder.sizeDelta = new Vector2(BookW, BookH);
            // Even the folder is not square to the desk - an eighth of a degree, which
            // is under noticing and over reading as a rectangle drawn by a machine.
            folder.localRotation = Quaternion.Euler(0f, 0f, -0.12f);
            Stock(folder, LedgerStyle.Manila, LedgerStyle.ManilaLow);
            Grain(folder, BookW, BookH, 1.2f);
            ShadowUnder(folder, 34f, LedgerStyle.FolderShadow);

            // ---- the paper: the page itself; its mask clips anything laid past ----
            paper = NewRect("Paper", folder);
            PlaceTopLeft(paper, PaperInset, -PaperInset, PaperW, PaperH);
            Gradient(paper, LedgerStyle.SheetFall);
            Grain(paper, PaperW, PaperH);
            // The folder's own leaf is the oldest paper on the desk: it takes the fold,
            // the foxing and the light before anything is laid on top of it.
            Aging(paper, PaperW, PaperH);
            Frame(paper, 1f, new Color(120f / 255f, 95f / 255f, 55f / 255f, 0.35f));
            paper.gameObject.AddComponent<RectMask2D>();

            // The sheet came out of a ring binder, and the coffee sat on the corner.
            PunchStrip(paper, PunchCentre, 0f, PaperH);
            PunchStrip(paper, PaperW - PunchCentre, 0f, PaperH);
            CoffeeStain(paper, PaperW - 320f, -60f, 132f);

            // The tabs come AFTER the sheet so the active one can be cut from the same
            // stock and lap over the sheet's top edge - which is what makes a divider
            // tab read as part of the page instead of a button above it.
            BuildTabs(folder);

            BuildHeader(paper);
            BuildBlotter(paper);
            BuildTelex(paper);
            BuildTicker(paper);

            // ---- the pages, in tab order; each is a full-paper root ----
            BuildNewspaperPage(paper);
            BuildPersonnelPage(paper);
            BuildFinancesPage(paper);
            BuildArmoryPage(paper);
            BuildDiplomacyPage(paper);
            BuildOrdersPage(paper);

            SetPage(LedgerPage.Newspaper);

            // The lamp's other half, and the LAST thing drawn: the room falls away at
            // the edges of the light. Over the folder, not under it - the file is IN
            // the pool of light, and a vignette behind it would only darken the desk.
            Vignette(page.transform);

            // Built active for TMP's sake (a TextMeshProUGUI only loads its font in
            // OnEnable, which never runs under an inactive parent), hidden until P.
            page.SetActive(false);
        }

        RectTransform NewPageRoot(RectTransform sheet, LedgerPage kind)
        {
            var root = NewRect("Page " + kind, sheet);
            Stretch(root);
            pageRoots[(int)kind] = root.gameObject;
            return root;
        }

        /// <summary>The width a divider tab needs for its word - the design's 28 units
        /// of padding either side over letter-spaced condensed caps.</summary>
        static float TabWidthFor(string label) => 56f + label.Length * 8.2f;

        /// <summary>The folder's index tabs along its top edge. The active tab is cut
        /// from the same stock as the page, stands taller and reaches down INTO the
        /// folder so the two fuse; the rest sit back in darker manila - the way a real
        /// file tells you where you are. The file number and the CLOSE tab hold the
        /// far end of the strip.</summary>
        void BuildTabs(RectTransform folder)
        {
            var strip = NewRect("Tabs", folder);
            strip.anchorMin = new Vector2(0f, 1f);
            strip.anchorMax = new Vector2(1f, 1f);
            strip.pivot = new Vector2(0.5f, 0f);
            strip.anchoredPosition = new Vector2(0f, 0f);
            strip.sizeDelta = new Vector2(0f, TabActiveH);

            var x = TabInset;
            for (var i = 0; i < TabNames.Length; i++)
            {
                var kind = (LedgerPage)i;
                var w = TabWidthFor(TabNames[i]);
                var rect = NewRect("Tab " + TabNames[i], strip);
                // Anchored to the strip's BOTTOM edge: a tab grows upward and its foot
                // stays welded to the folder, whatever height it is drawn at.
                rect.anchorMin = rect.anchorMax = new Vector2(0f, 0f);
                rect.pivot = new Vector2(0f, 0f);
                rect.anchoredPosition = new Vector2(x, 0f);
                rect.sizeDelta = new Vector2(w, TabH);
                tabRects[i] = rect;

                var face = rect.gameObject.AddComponent<Image>();
                face.sprite = LedgerStyle.RoundedSmall;
                face.type = Image.Type.Sliced;
                face.color = LedgerStyle.ManilaDim;
                face.raycastTarget = true;

                var button = rect.gameObject.AddComponent<Button>();
                button.targetGraphic = face;
                button.transition = Selectable.Transition.None;
                button.onClick.AddListener(() => SetPage(kind));

                var label = Text("Label", rect, LedgerStyle.Condensed, 13f,
                    new Color(76f / 255f, 60f / 255f, 38f / 255f), TextAlignmentOptions.Center);
                Stretch(label.rectTransform);
                label.characterSpacing = 4f;
                label.text = TabNames[i];

                tabFaces[i] = face;
                tabLabels[i] = label;
                x += w + TabGap;
            }

            // The far end of the strip: what the file is called, and the way out.
            var fileMark = Text("File", strip, LedgerStyle.Condensed, 11f,
                new Color(0.78f, 0.70f, 0.56f), TextAlignmentOptions.MidlineRight);
            fileMark.rectTransform.anchorMin = new Vector2(1f, 0f);
            fileMark.rectTransform.anchorMax = new Vector2(1f, 0f);
            fileMark.rectTransform.pivot = new Vector2(1f, 0f);
            fileMark.rectTransform.anchoredPosition = new Vector2(-TabInset - 110f, 12f);
            fileMark.rectTransform.sizeDelta = new Vector2(160f, 20f);
            fileMark.characterSpacing = 4f;
            fileMark.text = "FILE 04-B";

            var close = Tape(strip, "CLOSE", 0f, 0f, 100f, TabH, Close, red: true, size: 13f);
            var closeRect = (RectTransform)close.transform.parent;
            closeRect.anchorMin = closeRect.anchorMax = new Vector2(1f, 0f);
            closeRect.pivot = new Vector2(1f, 0f);
            closeRect.anchoredPosition = new Vector2(-TabInset, 0f);
        }

        void RefreshTabs()
        {
            for (var i = 0; i < tabFaces.Length; i++)
            {
                if (!tabFaces[i])
                    continue;
                var active = i == (int)currentPage;
                // The active tab is cut from the page's own stock; the rest are the
                // folder's darker manila.
                tabFaces[i].color = active ? LedgerStyle.Paper : LedgerStyle.ManilaDim;
                tabLabels[i].color = active
                    ? LedgerStyle.Ink
                    : new Color(76f / 255f, 60f / 255f, 38f / 255f);
                // Taller, and pushed two units down into the folder so the seam closes.
                tabRects[i].sizeDelta = new Vector2(tabRects[i].sizeDelta.x,
                    active ? TabActiveH : TabH);
                // The active tab reaches down through the manila margin and onto the
                // sheet itself, so there is no seam between the word and the page.
                tabRects[i].anchoredPosition = new Vector2(
                    tabRects[i].anchoredPosition.x, active ? -(PaperInset + 3f) : 0f);
                if (active)
                    tabRects[i].SetAsLastSibling();
            }
        }

        // -------------------------------------------------------------- the masthead

        /// <summary>The head of every ledger sheet: what the file is, how many men are
        /// on it, the stamp across the middle, and - held to the right margin - the
        /// date, the running clock and what is actually in the safe. Closed with the
        /// design's heavy rule over its own ghost.</summary>
        void BuildHeader(RectTransform sheet)
        {
            var title = Line(sheet, LedgerStyle.Condensed, 40f, LedgerStyle.Ink,
                PageLeft, -12f, 760f, 52f, "OUTFIT LEDGER");
            title.characterSpacing = 3f;

            headerCount = Caps(sheet, PageLeft, -66f, 760f, "", 11f, LedgerStyle.InkDim, 5f);

            // Struck across the middle of the head, clear of the title and the date.
            Stamp(sheet, "CONFIDENTIAL", PageLeft + PageWidth * 0.5f - 128f, -38f, 256f, 54f,
                tilt: -7f, size: 20f);

            Caps(sheet, PageRight - 420f, -12f, 420f, "STRUCK AS THE BOOKS STAND",
                11f, LedgerStyle.InkDim, 5f, TextAlignmentOptions.MidlineRight);

            headerDate = Line(sheet, LedgerStyle.Mono, 14f, LedgerStyle.InkMid,
                PageRight - 420f, -32f, 420f, 20f, "", TextAlignmentOptions.MidlineRight);
            headerDate.characterSpacing = 2f;

            headerClock = Line(sheet, LedgerStyle.Mono, 22f, LedgerStyle.RedPen,
                PageRight - 420f, -54f, 420f, 28f, "--:--:--",
                TextAlignmentOptions.MidlineRight);
            headerClock.characterSpacing = 3f;

            // What is in the safe, boxed - the one figure the boss looks for first.
            var safeBox = NewRect("Safe", sheet);
            PlaceTopLeft(safeBox, PageRight - 220f, -88f, 220f, 26f);
            Fill(safeBox, new Color(43f / 255f, 36f / 255f, 24f / 255f, 0.06f));
            Frame(safeBox, 1f, LedgerStyle.InkFaint);
            Caps(safeBox, 10f, -4f, 60f, "SAFE", 10f, LedgerStyle.InkLabel, 4f);
            VRule(safeBox, 62f, 0f, 26f, LedgerStyle.InkFaint);
            headerSafe = Line(safeBox, LedgerStyle.MonoBold, 14f, LedgerStyle.Ink,
                70f, -3f, 140f, 20f, "", TextAlignmentOptions.MidlineRight);

            DoubleRule(sheet, PageLeft, RuleY, PageWidth, LedgerStyle.Ink);
        }

        void RefreshHeader()
        {
            var day = outfit ? outfit.Campaign.Day : 1;
            var date = News.NewsDate.FromClockDay(day - 1);
            if (headerDate)
                headerDate.text = date.Stamped();
            if (headerCount)
            {
                var men = director.Roster != null ? director.Roster.Members.Count : 0;
                headerCount.text = (men == 1 ? "1 MAN ON THE BOOKS" : men + " MEN ON THE BOOKS")
                    + "   |   KEPT BY HAND · NOT FOR THE BOOKS";
            }
            if (headerSafe)
                headerSafe.text = outfit ? LedgerText.Cash(outfit.Accounts.Safe) : "--";
        }

        /// <summary>The clock is the one thing on the sheet that moves by itself, so it
        /// is written in place - two SetText calls a frame instead of a rebuild.</summary>
        void RefreshClock()
        {
            if (!headerClock)
                return;

            if (cityClock)
            {
                var hour = cityClock.Hour;
                var h = Mathf.FloorToInt(hour);
                var m = Mathf.FloorToInt(hour % 1f * 60f);
                var s = Mathf.FloorToInt(hour * 3600f % 60f);
                headerClock.SetText("{0:00}:{1:00}:{2:00}", h, m, s);
                if (hudClock)
                    hudClock.SetText("{0:00}:{1:00}:{2:00}", h, m, s);
                return;
            }

            // No city clock in the standalone ledger scene - the sheet says so rather
            // than inventing a time.
            headerClock.SetText("--:--:--");
            if (hudClock)
                hudClock.SetText("--:--:--");
        }

        // --------------------------------------------------------------- the blotter

        /// <summary>How many readouts the blotter strip carries. The design's five.</summary>
        const int BlotterCells = 5;

        readonly TMP_Text[] hudValue = new TMP_Text[BlotterCells];
        readonly TMP_Text[] hudNote = new TMP_Text[BlotterCells];
        readonly RectTransform[] hudMeter = new RectTransform[BlotterCells];

        static readonly string[] BlotterLabels =
        {
            "ON THE CLOCK", "POLICE HEAT", "RESPECT ON THE STREET",
            "TRIBUTE DUE", "IN THE SAFE",
        };

        /// <summary>
        /// The blotter: a dark strip of five readouts under the masthead, the way a
        /// desk pad shows through the sheet laid on it. Label, figure, a stepped meter
        /// and a line of plain English under each - the last of which is the point,
        /// because a number nobody can read is not a readout.
        ///
        /// The fourth cell is the TRIBUTE countdown, and it reads a real book: what the
        /// houses above the outfit are owed and when the man calls for it (Outfit.Tribute,
        /// re-priced off live turf every midnight). Nothing on this strip is furniture -
        /// a figure nobody can act on has no business on a blotter.
        /// </summary>
        void BuildBlotter(RectTransform sheet)
        {
            hudRoot = NewRect("Blotter", sheet);
            PlaceTopLeft(hudRoot, PageLeft, HudY, PageWidth, HudH);
            Stock(hudRoot, LedgerStyle.Blotter, LedgerStyle.BlotterLow);

            var cellW = PageWidth / BlotterCells;
            for (var i = 0; i < BlotterCells; i++)
            {
                var cell = NewRect("Cell " + i, hudRoot);
                PlaceTopLeft(cell, i * cellW, 0f, cellW, HudH);

                if (i > 0)
                    VRule(cell, 0f, 8f, HudH - 16f, LedgerStyle.BlotterRule);

                Caps(cell, 16f, -8f, cellW - 26f, BlotterLabels[i], 9.5f,
                    LedgerStyle.HudLabel, 5f);

                hudValue[i] = Line(cell, LedgerStyle.Condensed, 21f, LedgerStyle.HudCream,
                    16f, -22f, cellW - 26f, 28f, "");
                hudValue[i].characterSpacing = 1f;

                var meter = NewRect("Meter", cell);
                PlaceTopLeft(meter, 16f, -52f, cellW - 26f, 12f);
                hudMeter[i] = meter;

                // Two lines of room: the design is explicit that the sub-note wraps and
                // must never be clipped, and "nine days of payroll left at this rate"
                // does not fit on one.
                hudNote[i] = Paragraph(cell, LedgerStyle.Mono, 9.5f, LedgerStyle.HudNote,
                    16f, -62f, cellW - 26f, 26f, "", lineSpacing: 0f);
                hudNote[i].overflowMode = TextOverflowModes.Ellipsis;
            }

            hudClock = hudValue[0];
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

        void RefreshBlotter()
        {
            if (hudRoot == null || !hudRoot.gameObject.activeSelf)
                return;

            var roster = director.Roster;
            var perDay = Outfit.Wages.DailyPayroll(roster);

            // ---- the clock. RefreshClock writes the figure; the note is the date.
            var day = outfit ? outfit.Campaign.Day : 1;
            var date = News.NewsDate.FromClockDay(day - 1);
            SetMeter(0, 0, 0, LedgerStyle.HudCream);
            hudNote[0].text = date.Stamped();

            // ---- what the police think.
            var heat = outfit ? outfit.Heat : 0;
            var heatSteps = Mathf.Clamp(Mathf.CeilToInt(heat / 10f), 0, 10);
            hudValue[1].text = HeatWord(heat);
            hudValue[1].color = heat < 25 ? LedgerStyle.HudAmber : LedgerStyle.SoftRed;
            SetMeter(1, heatSteps, 10, LedgerStyle.HudMeterWarm);
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
            hudValue[2].color = LedgerStyle.HudCream;
            SetMeter(2, respectSteps, 10, LedgerStyle.HudAmber);
            hudNote[2].text = all > 0
                ? mine + " of " + all + " houses in the city are yours"
                : "no business in the city answers to you";

            // ---- what is kicked up, and when the man calls for it.
            var levy = outfit ? outfit.Tribute.Nearest() : null;
            if (levy == null)
            {
                hudValue[3].text = "NOTHING";
                hudValue[3].color = LedgerStyle.HudCream;
                SetMeter(3, 0, Outfit.Tribute.CycleDays, LedgerStyle.HudAmber);
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
                    ? LedgerStyle.SoftRed
                    : LedgerStyle.HudCream;
                SetMeter(3, away, Outfit.Tribute.CycleDays, LedgerStyle.HudMeterWarm);
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
                ? LedgerStyle.SoftRed
                : LedgerStyle.HudCream;
            SetMeter(4, Mathf.Clamp(runway, 0, 10), 10, LedgerStyle.HudAmber);
            hudNote[4].text = perDay > 0
                ? "-" + LedgerText.Cash(perDay).Substring(1) + " a day · " +
                  (runway >= 10 ? "ten days or better" : runway + " days of payroll left")
                : "nobody is drawing pay";
        }

        /// <summary>Redraws one blotter meter. The strip is rebuilt rather than tinted
        /// because the step COUNT moves, not just the colour. The figure's own colour is
        /// each cell's business: a full bar is bad news on the heat and good news on the
        /// runway, so nothing here may touch it.</summary>
        void SetMeter(int index, int filled, int steps, Color colour)
        {
            var meter = hudMeter[index];
            if (!meter)
                return;
            for (var i = meter.childCount - 1; i >= 0; i--)
                Destroy(meter.GetChild(i).gameObject);
            if (steps > 0)
                StepBar(meter, 0f, -6f, steps, filled, colour, 5f, 10f, 7f);
        }

        // ----------------------------------------------------------- the telex slips

        /// <summary>Three slips at most, the design's row.</summary>
        const int TelexSlips = 3;

        /// <summary>
        /// What came in overnight, clipped under the blotter: the precinct's word, the
        /// street's, and whatever was pushed under the door. Every line is derived from
        /// live state - they are a READOUT written as intelligence, never an inbox, and
        /// nothing on a slip can be pressed.
        /// </summary>
        void BuildTelex(RectTransform sheet)
        {
            telexRoot = NewRect("Telex", sheet);
            PlaceTopLeft(telexRoot, PageLeft, TelexY, PageWidth, TelexH);
        }

        void RefreshTelex()
        {
            if (telexRoot == null || !telexRoot.gameObject.activeSelf)
                return;

            for (var i = telexRoot.childCount - 1; i >= 0; i--)
                Destroy(telexRoot.GetChild(i).gameObject);

            var roster = director.Roster;
            var day = outfit ? outfit.Campaign.Day : 1;

            // Composed first, laid out second: the slips SPAN the sheet whatever they
            // come to. Sized to a fixed three, two slips would leave a third of the
            // row bare and the row would read as a thing that had failed to load.
            telexSlips.Clear();

            // The precinct: what the heat actually means for the men on the street.
            var heat = outfit ? outfit.Heat : 0;
            telexSlips.Add(("TELEX · 4TH PRECINCT", Clockstamp(day),
                heat == 0
                    ? "No file downtown carries your name. Nothing has been asked about " +
                      "anyone on this sheet."
                    : "The precinct has " + heat + " against you. Expect to be looked at " +
                      "on any job worked in daylight."));

            // The street: the last thing a crew came back with.
            if (outfit != null && outfit.Records.Count > 0)
            {
                var record = outfit.Records[outfit.Records.Count - 1];
                telexSlips.Add(("WIRE · THE STREET", Clockstamp(record.Day),
                    record.Lieutenant + "'s men worked " +
                    LedgerText.OrderLabel(record.Type).ToLowerInvariant() + " at " +
                    record.TargetSummary + ". " +
                    LedgerText.OutcomeLabel(record.Outcome) + "."));
            }

            // Under the door: who is on the books earning nothing.
            if (roster != null && telexSlips.Count < TelexSlips)
            {
                var idle = 0;
                string firstIdle = null;
                for (var i = 0; i < roster.Members.Count; i++)
                {
                    var member = roster.Members[i];
                    if (member.Gone || member.Status != CharacterStatus.Active)
                        continue;
                    if (roster.AssignmentOf(member.Id).Kind != AssignmentKind.Pool)
                        continue;
                    idle++;
                    firstIdle ??= member.FullName;
                }
                telexSlips.Add(("NOTE · UNDER THE DOOR", "-",
                    idle == 0
                        ? "Every man on the books is posted. Nobody is standing about on " +
                          "the outfit's money."
                        : (idle == 1
                            ? firstIdle + " is standing idle and drawing pay. "
                            : idle + " men are standing idle and drawing pay, " +
                              firstIdle + " among them. ") +
                          "Somewhere on the map there is work for them."));
            }

            const float gap = 12f;
            var across = Mathf.Max(1, telexSlips.Count);
            var w = (PageWidth - gap * (across - 1)) / across;
            for (var i = 0; i < telexSlips.Count; i++)
            {
                var (source, time, body) = telexSlips[i];
                Slip(telexRoot, i * (w + gap), 0f, w, TelexH, source, time, body);
            }
        }

        /// <summary>Scratch for the night's slips - composed, then laid out to span.</summary>
        readonly List<(string Source, string Time, string Body)> telexSlips =
            new List<(string, string, string)>();

        /// <summary>A telex machine stamps the hour it printed, not the date.</summary>
        string Clockstamp(int day)
        {
            if (cityClock)
                return Mathf.FloorToInt(cityClock.Hour).ToString("00") + ":" +
                       Mathf.FloorToInt(cityClock.Hour % 1f * 60f).ToString("00");
            return "DAY " + day;
        }

        // --------------------------------------------------------------- the ticker

        /// <summary>The line along the foot of every sheet: what the book is doing on
        /// the left, the keys and the folio on the right.</summary>
        void BuildTicker(RectTransform sheet)
        {
            Rule(sheet, PageLeft, TickerY - 8f, PageWidth, LedgerStyle.InkFaint);
            tickerLeft = Caps(sheet, PageLeft, TickerY, PageWidth * 0.55f, "", 11f,
                LedgerStyle.InkPale, 5f);
            tickerRight = Caps(sheet, PageRight - PageWidth * 0.45f, TickerY,
                PageWidth * 0.45f, "", 11f, LedgerStyle.InkPale, 5f,
                TextAlignmentOptions.MidlineRight);
        }

        void RefreshTicker()
        {
            if (!tickerLeft)
                return;

            tickerLeft.text = TickerLine().ToUpperInvariant();
            tickerRight.text = "[ ] TURN THE PAGE   [ESC] SHUT THE FILE   |   PAGE " +
                TabFolios[(int)currentPage].ToString("00") + " OF " + Folios;
        }

        /// <summary>
        /// The live line along the foot. A refusal outranks everything - the book must
        /// never swallow a NO - then whatever the page the boss is on has to say, then
        /// the one figure that is always true.
        /// </summary>
        string TickerLine()
        {
            if (lastRefusal.Length > 0)
                return lastRefusal;
            if (currentPage == LedgerPage.Armory && armoryNote.Length > 0)
                return armoryNote;
            if (currentPage == LedgerPage.Newspaper && classifiedNote.Length > 0)
                return classifiedNote;

            var roster = director.Roster;
            if (currentPage == LedgerPage.Personnel && roster != null && selectedId >= 0)
            {
                var member = roster.Find(selectedId);
                if (member != null)
                {
                    var post = roster.AssignmentOf(member.Id);
                    // AssignmentLine ANSWERS the crew's name for a crew posting, so it
                    // has to be handed one - an empty string prints an empty middle.
                    var crewName = "";
                    if (post.Kind == AssignmentKind.Crew)
                    {
                        var crew = roster.FindCrew(post.CrewId);
                        var lieutenant = crew != null ? roster.Find(crew.LieutenantId) : null;
                        crewName = lieutenant != null
                            ? LedgerText.CrewName(lieutenant.Surname) : "a crew";
                    }
                    return member.FullName + " · " +
                           LedgerText.AssignmentLine(post, crewName) + " · " +
                           LedgerText.Cash(Outfit.Wages.WageFor(member)) + " a day";
                }
            }

            return "payroll running · " +
                   LedgerText.Cash(Outfit.Wages.DailyPayroll(roster)) + " a day";
        }
    }
}
