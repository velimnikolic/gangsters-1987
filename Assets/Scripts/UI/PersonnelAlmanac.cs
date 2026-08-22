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
    /// The outfit ledger, 1987: a manila folder open on the boss's desk, taking the
    /// LEFT half of the screen while the strategic map holds the right. Six tabbed
    /// pages - the morning paper, the personnel roll, the balance sheet, the armory
    /// catalogue, the families, the week's orders - all typed on cream stock, marked
    /// in highlighter and red pen, stamped where the law has had a word, with
    /// Polaroids for faces and label-maker tape for the verbs. Opened with P.
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
    /// click-paced, so a few hundred objects per rebuild is the affordable choice.
    /// This file is the shell - the desk, the folder, the tabs, the header, input and
    /// scrolling; each page lives in its own partial.
    /// </summary>
    public sealed partial class PersonnelAlmanac : MonoBehaviour, IMapTargetingConsumer
    {
        const int SortingOrder = 110;

        /// <summary>The folder is the LEFT HALF of the 1920-reference canvas; the
        /// strategic map owns the right half while the book is open.</summary>
        const float HalfWidth = 960f;

        // The desk fixture, in canvas units from the top-left of the left half.
        const float FolderX = 10f;
        const float FolderY = -60f;
        const float FolderW = 940f;
        const float FolderH = 1010f;

        const float PaperX = 20f;
        const float PaperY = -72f;
        const float PaperW = 920f;
        const float PaperH = 988f;

        /// <summary>The tab strip sits on the folder's top edge, above the paper.</summary>
        const float TabY = -30f;
        const float TabH = 34f;
        const float TabW = 128f;
        const float TabGap = 6f;

        // Paper-local layout every page shares.
        const float PageLeft = 24f;
        const float PageRight = PaperW - 24f;
        const float PageWidth = PageRight - PageLeft;

        /// <summary>Content starts under the header's double rule.</summary>
        const float PageTop = -78f;

        /// <summary>The last usable y on the paper.</summary>
        const float PageBottom = -(PaperH - 16f);

        /// <summary>True while the book is open. Every world-input reader checks this -
        /// the keyboard half of the modal shield (the raycast-target desk is the pointer
        /// half).</summary>
        public static bool IsOpen { get; private set; }

        /// <summary>True while open AND on the frame the book closes: Esc readers poll,
        /// polling cannot consume, and Update order is arbitrary - a reader running after
        /// the close would otherwise act on the very press that closed the book.</summary>
        public static bool ClaimsEsc => IsOpen || Time.frameCount == lastCloseFrame;

        /// <summary>True while the book stands open: the strategic map is docked into
        /// the right half the whole time, live for panning, zooming, inspection and -
        /// on the ORDERS page - target selection.</summary>
        public static bool MapInteractive => IsOpen;

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

        static readonly string[] TabNames =
            { "THE PAPER", "PERSONNEL", "FINANCES", "ARMORY", "FAMILIES", "ORDERS" };

        Canvas canvas;
        GameObject page;
        RectTransform paper;
        TMP_Text headerTitle;
        TMP_Text headerDate;
        TMP_Text headerCount;

        LedgerPage currentPage = LedgerPage.Newspaper;
        readonly GameObject[] pageRoots = new GameObject[6];
        readonly Image[] tabFaces = new Image[6];
        readonly TMP_Text[] tabLabels = new TMP_Text[6];
        readonly RectTransform[] tabRects = new RectTransform[6];

        PersonnelDirector director;
        OutfitDirector outfit;
        PlayerMafioso player;

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

            // [ and ] turn the pages; the tabs are the pointer's way.
            if (keyboard.leftBracketKey.wasPressedThisFrame)
                SetPage((LedgerPage)(((int)currentPage + pageRoots.Length - 1)
                    % pageRoots.Length));
            if (keyboard.rightBracketKey.wasPressedThisFrame)
                SetPage((LedgerPage)(((int)currentPage + 1) % pageRoots.Length));

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
                if (pendingCommit)
                {
                    pendingCommit = false;
                    dirty = true;
                }
                else if (pendingConfirm != Confirm.None)
                {
                    pendingConfirm = Confirm.None;
                    dirty = true;
                }
                else if (assignMode)
                {
                    assignMode = false;
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
        /// the header line and filter tapes are re-read on every pass because they
        /// are cheap and read the same state.</summary>
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
            // The war room assembles: the strategic map docks into the right half for
            // as long as the book is open. No city yet = the map declines quietly and
            // the right half just shows the world.
            if (StrategicMapHud.Instance)
                StrategicMapHud.Instance.OpenBeside();
            // The folder always opens on the morning paper - the week's frame - and
            // the working pages keep their state for when the boss turns to them.
            SetPage(LedgerPage.Newspaper);
        }

        void Close()
        {
            if (page)
                page.SetActive(false);
            IsOpen = false;
            if (StrategicMapHud.Targeting == (IMapTargetingConsumer)this)
                StrategicMapHud.Targeting = null;
            if (StrategicMapHud.Instance)
            {
                StrategicMapHud.Instance.SetTargetHighlights(null, Color.clear);
                // The map came up with the book; it goes down with it.
                if (StrategicMapHud.IsOpen)
                    StrategicMapHud.Instance.Close();
            }
            lastCloseFrame = Time.frameCount;
            assignMode = false;
            pendingConfirm = Confirm.None;
            lastRefusal = "";
            HideHoverNote();
            if (sortMenu)
                sortMenu.SetActive(false);
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

            // The map stands open beside the book the whole time - ORDERS just arms
            // targeting on it. Leaving the page clears its highlight layer.
            if (pageKind != LedgerPage.Orders)
            {
                pendingCommit = false;
                if (StrategicMapHud.Instance)
                    StrategicMapHud.Instance.SetTargetHighlights(null, Color.clear);
            }
            RefreshTargeting();

            if (pageKind != LedgerPage.Personnel)
            {
                assignMode = false;
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
                    viewport = listViewport;
                    content = listContent;
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
            // 1920x1080 in reference units both ways, so the fixed folder fits whole.
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            go.AddComponent<GraphicRaycaster>();
        }

        void BuildBook()
        {
            page = new GameObject("Page", typeof(RectTransform));
            page.transform.SetParent(canvas.transform, false);
            Stretch((RectTransform)page.transform);

            // ---- the desk: the left half, edge to edge, and the modal shield ----
            var desk = NewRect("Desk", page.transform);
            desk.anchorMin = Vector2.zero;
            desk.anchorMax = new Vector2(0.5f, 1f);
            desk.offsetMin = desk.offsetMax = Vector2.zero;
            var deskImage = desk.gameObject.AddComponent<Image>();
            deskImage.sprite = null;
            deskImage.color = LedgerStyle.Desk;
            deskImage.raycastTarget = true;
            desk.gameObject.AddComponent<RectMask2D>();
            Grain(desk, HalfWidth, 1200f, 1.6f);

            // The desk lamp, up and to the left of the folder - the one light source
            // every shadow on the desk agrees with.
            var lamp = NewRect("Lamp", desk);
            lamp.anchorMin = lamp.anchorMax = new Vector2(0f, 1f);
            lamp.pivot = new Vector2(0.5f, 0.5f);
            lamp.anchoredPosition = new Vector2(180f, -40f);
            lamp.sizeDelta = new Vector2(1500f, 1500f);
            var lampImage = lamp.gameObject.AddComponent<RawImage>();
            lampImage.texture = LedgerStyle.RadialLight;
            lampImage.color = LedgerStyle.Lamp;
            lampImage.raycastTarget = false;

            // ---- the folder: manila, a shade darker than its pages ----
            var folder = NewRect("Folder", desk);
            PlaceTopLeft(folder, FolderX, FolderY, FolderW, FolderH);
            Fill(folder, LedgerStyle.Manila);
            Grain(folder, FolderW, FolderH, 1.2f);
            ShadowUnder(folder, 14f);

            BuildTabs(desk);

            // ---- the paper: the page itself; its mask clips anything laid past ----
            paper = NewRect("Paper", desk);
            PlaceTopLeft(paper, PaperX, PaperY, PaperW, PaperH);
            Fill(paper, LedgerStyle.Paper);
            Grain(paper, PaperW, PaperH);
            ShadowUnder(paper, 6f);
            paper.gameObject.AddComponent<RectMask2D>();

            BuildHeader(paper);

            // ---- the pages, in tab order; each is a full-paper root ----
            BuildNewspaperPage(paper);
            BuildPersonnelPage(paper);
            BuildFinancesPage(paper);
            BuildArmoryPage(paper);
            BuildDiplomacyPage(paper);
            BuildOrdersPage(paper);

            // The CLOSE tape on the desk, past the tab strip - the boss shuts the folder.
            Tape(desk, "CLOSE", HalfWidth - 114f, TabY, 94f, 30f, Close, red: true);

            SetPage(LedgerPage.Newspaper);

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

        /// <summary>The folder's index tabs along its top edge. The active tab is cut
        /// from the same stock as the page and joins it; the rest sit back in darker
        /// manila - the way a real folder tells you where you are.</summary>
        void BuildTabs(RectTransform desk)
        {
            for (var i = 0; i < TabNames.Length; i++)
            {
                var kind = (LedgerPage)i;
                var rect = NewRect("Tab " + TabNames[i], desk);
                PlaceTopLeft(rect, PaperX + i * (TabW + TabGap), TabY, TabW, TabH + 8f);
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

                var label = Text("Label", rect, LedgerStyle.Type, 14f, LedgerStyle.Ink,
                    TextAlignmentOptions.Center);
                Stretch(label.rectTransform);
                label.rectTransform.offsetMin = new Vector2(0f, 8f);
                label.characterSpacing = 2f;
                label.text = TabNames[i];

                tabFaces[i] = face;
                tabLabels[i] = label;
            }
        }

        void RefreshTabs()
        {
            for (var i = 0; i < tabFaces.Length; i++)
            {
                if (!tabFaces[i])
                    continue;
                var active = i == (int)currentPage;
                tabFaces[i].color = active ? LedgerStyle.Paper : LedgerStyle.ManilaDim;
                tabLabels[i].color = active ? LedgerStyle.Ink : LedgerStyle.InkDim;
                // The active tab is pulled forward: a touch taller, and drawn last so it
                // overlaps its neighbours and the paper's top edge with no seam.
                tabRects[i].sizeDelta = new Vector2(TabW, active ? TabH + 12f : TabH + 8f);
                tabRects[i].anchoredPosition = new Vector2(
                    PaperX + i * (TabW + TabGap), active ? TabY : TabY - 4f);
                if (active)
                    tabRects[i].SetAsLastSibling();
            }
            // The paper itself must draw over every tab but the active one - it comes
            // last among the desk's children, then the active tab is raised past it,
            // then the CLOSE tape. Cheap to reassert on every turn.
            if (paper)
            {
                paper.SetAsLastSibling();
                tabRects[(int)currentPage].SetAsLastSibling();
                var close = paper.parent.Find("Tape CLOSE");
                if (close)
                    close.SetAsLastSibling();
            }
        }

        /// <summary>The typed header every ledger page shares: title, the campaign
        /// date, the men-on-the-books count, a CONFIDENTIAL stamp, and the double
        /// rule under it all. The paper's own page paints over it whole.</summary>
        void BuildHeader(RectTransform sheet)
        {
            headerTitle = Line(sheet, LedgerStyle.Type, 24f, LedgerStyle.Ink, PageLeft, -12f,
                420f, 36f, "OUTFIT LEDGER");
            headerTitle.characterSpacing = 5f;

            headerCount = Line(sheet, LedgerStyle.Mono, 14f, LedgerStyle.InkDim, PageLeft,
                -44f, 420f, 18f, "");
            headerCount.characterSpacing = 1f;

            headerDate = Line(sheet, LedgerStyle.Mono, 14f, LedgerStyle.Ink, PageRight - 420f,
                -16f, 420f, 20f, "", TextAlignmentOptions.MidlineRight);
            var sub = Line(sheet, LedgerStyle.Mono, 12.5f, LedgerStyle.InkDim,
                PageRight - 420f, -40f, 420f, 18f,
                "PRIVATE ACCOUNT · NOT FOR THE BOOKS", TextAlignmentOptions.MidlineRight);
            sub.characterSpacing = 1f;

            // Struck across the double rule, clear of the title and the date.
            Stamp(sheet, "CONFIDENTIAL", 372f, -36f, 176f, 34f, tilt: -5f, size: 16f);

            DoubleRule(sheet, PageLeft, -64f, PageWidth, LedgerStyle.Ink);
        }

        void RefreshHeader()
        {
            var week = outfit ? outfit.Campaign.Week : 1;
            var date = News.NewsDate.FromClockDay((week - 1) * 7);
            if (headerDate)
                headerDate.text = "WEEK " + week + "  ·  " + date.Masthead();
            if (headerCount)
                headerCount.text = director.Roster != null
                    ? LedgerText.MemberCount(director.Roster.Members.Count)
                    : "";
        }
    }
}
